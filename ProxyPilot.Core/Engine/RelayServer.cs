using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ProxyPilot.Core.Config;

namespace ProxyPilot.Core.Engine;

internal sealed class NatEntry
{
    public required IPAddress ClientAddress { get; init; }
    public required ushort ClientPort { get; init; }
    public required IPAddress DestAddress { get; init; }
    public required ushort DestPort { get; init; }
    public required string ProcessName { get; init; }
    public required int Pid { get; init; }
    public required ProfileRule Rule { get; init; }
    public required IReadOnlyList<ProxyServer> Hops { get; init; }
    public string? Hostname { get; init; }
    public Guid ConnectionId { get; init; } = Guid.NewGuid();
    public DateTime Created { get; } = DateTime.Now;
}

internal sealed class RelayServer : IDisposable
{
    private readonly ConcurrentDictionary<ushort, NatEntry> _nat = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public int Port { get; private set; }
    public event Action<ConnectionEvent>? ConnectionChanged;
    public event Action<EngineLogEvent>? Log;

    public void Add(NatEntry entry) => _nat[entry.ClientPort] = entry;
    public bool TryGet(ushort clientPort, out NatEntry entry) => _nat.TryGetValue(clientPort, out entry!);
    public void Remove(ushort clientPort) => _nat.TryRemove(clientPort, out _);

    public void Start(int preferredPort, CancellationToken cancellationToken)
    {
        Port = Bind(preferredPort);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => AcceptLoop(_cts.Token));
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* shutting down */ }
        try { _listener?.Stop(); } catch { /* shutting down */ }
        _cts?.Dispose();
        _nat.Clear();
    }

    private int Bind(int preferred)
    {
        Exception? last = null;
        for (var port = preferred; port < preferred + 20; port++)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start(256);
                return port;
            }
            catch (Exception ex)
            {
                last = ex;
                _listener = null;
            }
        }
        throw new InvalidOperationException("Не удалось открыть локальный релей.", last);
    }

    private async Task AcceptLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                EmitLog("Ошибка accept: " + ex.Message, true);
                continue;
            }
            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var peer = client.Client.RemoteEndPoint as IPEndPoint;
            if (peer is null)
                return;
            if (!_nat.TryGetValue((ushort)peer.Port, out var entry))
            {
                EmitLog($"Нет NAT для {peer}", true);
                return;
            }

            var dest = new IPEndPoint(entry.DestAddress, entry.DestPort);
            var target = string.IsNullOrWhiteSpace(entry.Hostname)
                ? $"{entry.DestAddress}:{entry.DestPort}"
                : $"{entry.Hostname} ({entry.DestAddress}):{entry.DestPort}";
            ConnectionEvent? ev = new ConnectionEvent
            {
                Id = entry.ConnectionId,
                ProcessName = entry.ProcessName,
                Pid = entry.Pid,
                Target = target,
                RuleName = entry.Rule.Name,
                Action = RuleAction.Proxy,
                ProxyName = string.Join(" -> ", entry.Hops.Select(h => h.Display)),
                Status = "Соединение с прокси..."
            };
            ConnectionChanged?.Invoke(ev);

            TcpClient? remote = null;
            try
            {
                client.NoDelay = true;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                FileLog.Write($"SOCKS start {entry.ProcessName} CONNECT {entry.Hostname ?? dest.Address.ToString()}:{dest.Port} via {entry.Hops[0].Display}");
                remote = await ProxyClient.ConnectAsync(entry.Hops, dest, cancellationToken, hostname: entry.Hostname).ConfigureAwait(false);
                remote.NoDelay = true;
                FileLog.Write($"SOCKS ok {entry.ProcessName} {entry.Hostname ?? dest.ToString()} in {sw.ElapsedMilliseconds}ms");
                ev.Status = "Прокси";
                ConnectionChanged?.Invoke(ev);

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var toRemote = CopyAsync(client.GetStream(), remote.GetStream(), n =>
                {
                    ev.BytesSent += n;
                    ConnectionChanged?.Invoke(ev);
                }, linked.Token);
                var toClient = CopyAsync(remote.GetStream(), client.GetStream(), n =>
                {
                    ev.BytesReceived += n;
                    ConnectionChanged?.Invoke(ev);
                }, linked.Token);
                await Task.WhenAll(toRemote, toClient).ConfigureAwait(false);
                ev.Status = $"Закрыто ↑{ev.BytesSent} ↓{ev.BytesReceived}";
                ConnectionChanged?.Invoke(ev);
            }
            catch (Exception ex)
            {
                ev.Status = "Ошибка: " + ex.Message;
                ConnectionChanged?.Invoke(ev);
                EmitLog($"{entry.ProcessName} -> {dest}: {ex.Message}", true);
            }
            finally
            {
                remote?.Dispose();
                _nat.TryRemove(entry.ClientPort, out _);
            }
        }
    }

    private static async Task CopyAsync(Stream from, Stream to, Action<int> onBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[65536];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var n = await from.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (n <= 0)
                    break;
                await to.WriteAsync(buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
                onBytes(n);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on stop
        }
        catch (IOException)
        {
            // peer closed
        }
        try { to.Flush(); } catch { /* closed */ }
        try
        {
            if (to is NetworkStream ns)
                ns.Socket.Shutdown(SocketShutdown.Send);
            else
                to.Close();
        }
        catch { /* closed */ }
    }

    private void EmitLog(string message, bool error)
    {
        FileLog.Write(message, error);
        Log?.Invoke(new EngineLogEvent { Message = message, IsError = error });
    }
}
