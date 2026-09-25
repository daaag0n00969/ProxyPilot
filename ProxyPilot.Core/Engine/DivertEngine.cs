using System.Net;
using System.Runtime.InteropServices;
using ProxyPilot.Core.Config;
using ProxyPilot.Core.Native;

namespace ProxyPilot.Core.Engine;

public sealed class DivertEngine : IDisposable
{
    private readonly ProcessLookup _processes = new();
    private readonly object _sendLock = new();
    private Profile _profile = Profile.CreateDefault();
    private RuleEngine _rules = new(Profile.CreateDefault());
    private RelayServer? _relay;
    private CancellationTokenSource? _cts;
    private IntPtr _networkHandle = new(-1);
    private IntPtr _dnsHandle = new(-1);
    private IntPtr _quicHandle = new(-1);
    private IntPtr _socketHandle = new(-1);
    private int _selfPid;
    private bool _running;

    public bool IsRunning => _running;
    public int RelayPort => _relay?.Port ?? 0;
    public event Action<ConnectionEvent>? ConnectionChanged;
    public event Action<EngineLogEvent>? Log;

    public void Start(Profile profile)
    {
        if (_running)
            Stop();

        _profile = profile;
        _rules = new RuleEngine(profile);
        _selfPid = Environment.ProcessId;
        _cts = new CancellationTokenSource();
        _relay = new RelayServer();
        _relay.ConnectionChanged += e => ConnectionChanged?.Invoke(e);
        _relay.Log += e => Log?.Invoke(e);
        _relay.Start(profile.RelayPort, _cts.Token);

        var filter = BuildTcpFilter();
        try
        {
            _networkHandle = OpenNetwork(filter, "сетевой перехват");
            WinDivertNative.WinDivertSetParam(_networkHandle, WinDivertNative.ParamQueueLength, 16384);
            WinDivertNative.WinDivertSetParam(_networkHandle, WinDivertNative.ParamQueueSize, 32 * 1024 * 1024);
            WinDivertNative.WinDivertSetParam(_networkHandle, WinDivertNative.ParamQueueTime, 8000);

            if (_profile.DnsViaProxy)
                _dnsHandle = OpenNetwork("outbound and ip and udp.DstPort == 53", "перехват DNS");

            _quicHandle = OpenNetwork("outbound and ip and udp.DstPort == 443", "перехват QUIC");

            _socketHandle = WinDivertNative.WinDivertOpen("true", WinDivertNative.LayerSocket, 0,
                WinDivertNative.FlagSniff | WinDivertNative.FlagRecvOnly);

            _running = true;
            _ = Task.Run(() => RecvLoop(_cts.Token));
            if (_profile.DnsViaProxy)
                _ = Task.Run(() => DnsRecvLoop(_cts.Token));
            _ = Task.Run(() => QuicLoop(_cts.Token));
            if (!WinDivertNative.IsInvalid(_socketHandle))
                _ = Task.Run(() => SocketLoop(_cts.Token));
        }
        catch
        {
            Stop();
            throw;
        }

        DnsCache.SeedKnownCloud();
        Emit($"Перехват запущен. build=fast-start релей :{_relay.Port}. Фильтр: {filter}");
        FileLog.Write("DNS seed steamcloudsweden.blob.core.windows.net -> 20.60.253.225, 20.209.216.97, 20.60.253.129");
        if (_profile.DnsViaProxy && _profile.Proxies.Count > 0)
            _ = Task.Run(() => PrewarmCloudDns(_profile.Proxies, _cts.Token));
    }

    public void Stop()
    {
        _running = false;
        try { _cts?.Cancel(); } catch { /* ignore */ }
        Close(ref _networkHandle);
        Close(ref _dnsHandle);
        Close(ref _quicHandle);
        Close(ref _socketHandle);
        _relay?.Dispose();
        _relay = null;
        _cts?.Dispose();
        _cts = null;
        Emit("Перехват остановлен.");
    }

    public void Dispose() => Stop();

    private IntPtr OpenNetwork(string filter, string what)
    {
        var compileError = WinDivertNative.DescribeFilterError(filter);
        if (compileError != null)
            throw new InvalidOperationException($"Некорректный фильтр WinDivert ({what}): {compileError}");

        var handle = WinDivertNative.WinDivertOpen(filter, WinDivertNative.LayerNetwork, 1000, 0);
        if (WinDivertNative.IsInvalid(handle))
            throw new InvalidOperationException(OpenError(what) + Environment.NewLine + "Фильтр: " + filter);
        return handle;
    }

    private string BuildTcpFilter()
    {
        // WinDivert rejects `not (...)` groups. Exclusions use `!=` or are applied in user mode.
        var parts = new List<string> { "outbound", "ip", "tcp" };
        foreach (var proxy in _profile.Proxies)
            parts.Add($"tcp.DstPort != {proxy.Port}");
        parts.Add($"tcp.DstPort != {_relay!.Port}");
        return string.Join(" and ", parts);
    }

    private void RecvLoop(CancellationToken cancellationToken)
    {
        var packet = new byte[40 + 0xFFFF];
        while (!cancellationToken.IsCancellationRequested && _running)
        {
            var addr = new WinDivertAddress();
            uint recvLen;
            if (!WinDivertNative.WinDivertRecv(_networkHandle, packet, (uint)packet.Length, out recvLen, ref addr))
            {
                if (cancellationToken.IsCancellationRequested || !_running)
                    return;
                continue;
            }

            try
            {
                HandlePacket(packet, (int)recvLen, ref addr);
            }
            catch (Exception ex)
            {
                Emit("Пакет: " + ex.Message, true);
                Passthrough(packet, (int)recvLen, ref addr);
            }
        }
    }

    private void DnsRecvLoop(CancellationToken cancellationToken)
    {
        var packet = new byte[40 + 0xFFFF];
        while (!cancellationToken.IsCancellationRequested && _running)
        {
            var addr = new WinDivertAddress();
            if (!WinDivertNative.WinDivertRecv(_dnsHandle, packet, (uint)packet.Length, out var recvLen, ref addr))
            {
                if (cancellationToken.IsCancellationRequested || !_running)
                    return;
                continue;
            }

            try
            {
                HandleDnsQuery(packet, (int)recvLen, ref addr);
            }
            catch (Exception ex)
            {
                Emit("DNS: " + ex.Message, true);
                SendOn(_dnsHandle, packet, (int)recvLen, ref addr);
            }
        }
    }

    private void SocketLoop(CancellationToken cancellationToken)
    {
        var dummy = new byte[1];
        while (!cancellationToken.IsCancellationRequested && _running)
        {
            var addr = new WinDivertAddress();
            if (!WinDivertNative.WinDivertRecv(_socketHandle, dummy, 0, out _, ref addr))
                continue;
            if (addr.Event != WinDivertNative.EventSocketConnect || addr.ProcessId == 0)
                continue;
            var remote = new IPAddress(BitConverter.GetBytes(addr.RemoteAddr0));
            _processes.Remember((int)addr.ProcessId, remote, addr.RemotePort, addr.LocalPort);
        }
    }

    private void HandlePacket(byte[] packet, int length, ref WinDivertAddress addr)
    {
        var parsed = PacketParser.Parse(packet, length);
        if (!parsed.Ok || parsed.Fragment)
        {
            Passthrough(packet, length, ref addr);
            return;
        }

        if (!parsed.IsTcp)
        {
            Passthrough(packet, length, ref addr);
            return;
        }

        var relayPort = (ushort)_relay!.Port;
        if (parsed.DstPort == relayPort)
        {
            Passthrough(packet, length, ref addr);
            return;
        }

        if (parsed.SrcPort == relayPort)
        {
            ReflectFromRelay(packet, length, parsed, ref addr);
            return;
        }

        if (_relay.TryGet(parsed.SrcPort, out var existing) &&
            existing.DestAddress.Equals(parsed.DstAddress) &&
            existing.DestPort == parsed.DstPort)
        {
            ReflectToRelay(packet, length, parsed, relayPort, ref addr);
            return;
        }

        if (!parsed.IsSyn)
        {
            Passthrough(packet, length, ref addr);
            return;
        }

        var pid = _processes.FindPid(parsed.SrcAddress, parsed.SrcPort, parsed.DstAddress, parsed.DstPort);
        if (pid == 0)
            pid = _processes.FindPid(IPAddress.Any, parsed.SrcPort, parsed.DstAddress, parsed.DstPort);
        var name = pid == _selfPid ? "ProxyPilot.exe" : _processes.GetName(pid);

        if (_profile.LoopDetection && _rules.IsProxyEndpoint(parsed.DstAddress, parsed.DstPort))
        {
            LogSyn(name, pid, parsed, "DIRECT", "loop-guard");
            Passthrough(packet, length, ref addr);
            return;
        }

        var decision = _rules.Evaluate(name, parsed.DstAddress, parsed.DstPort);
        if (decision.Action == RuleAction.Direct || pid == _selfPid)
        {
            LogSyn(name, pid, parsed, "DIRECT", decision.Rule.Name);
            Passthrough(packet, length, ref addr);
            return;
        }

        if (decision.Action == RuleAction.Block)
        {
            ConnectionChanged?.Invoke(new ConnectionEvent
            {
                ProcessName = name,
                Pid = pid,
                Target = $"{parsed.DstAddress}:{parsed.DstPort}",
                RuleName = decision.Rule.Name,
                Action = RuleAction.Block,
                Status = "Блок"
            });
            return;
        }

        if (decision.Hops.Count == 0)
        {
            Emit($"Правило «{decision.Rule.Name}» без прокси, Direct.", true);
            Passthrough(packet, length, ref addr);
            return;
        }

        _relay.Add(new NatEntry
        {
            ClientAddress = parsed.SrcAddress,
            ClientPort = parsed.SrcPort,
            DestAddress = parsed.DstAddress,
            DestPort = parsed.DstPort,
            ProcessName = name,
            Pid = pid,
            Rule = decision.Rule,
            Hops = decision.Hops,
            Hostname = DnsCache.Lookup(parsed.DstAddress)
        });
        LogSyn(name, pid, parsed, "PROXY", decision.Rule.Name);
        ReflectToRelay(packet, length, parsed, relayPort, ref addr);
    }

    private void HandleDnsQuery(byte[] packet, int length, ref WinDivertAddress addr)
    {
        var parsed = PacketParser.Parse(packet, length);
        if (!parsed.Ok || !parsed.IsUdp || parsed.DstPort != 53)
        {
            SendOn(_dnsHandle, packet, length, ref addr);
            return;
        }

        var pid = _processes.FindUdpPid(parsed.SrcPort);
        var name = pid == _selfPid ? "ProxyPilot.exe" : _processes.GetName(pid);
        var query = packet.AsSpan(parsed.UdpPayloadOffset, parsed.UdpPayloadLength).ToArray();
        var qname = DnsOverProxy.FirstQuestionName(query) ?? "?";
        var qtype = DnsOverProxy.QuestionType(query);
        var cloudName = DnsCache.IsCloudHost(qname);
        if (cloudName || !DnsCache.IsNoisyHost(qname))
            FileLog.Write($"DNS query {qname} type={qtype} pid={pid} {name} {parsed.SrcAddress}:{parsed.SrcPort} -> {parsed.DstAddress}");

        if (pid == _selfPid || (!_rules.ProcessHasProxyRule(name) && !cloudName))
        {
            if (cloudName)
                FileLog.Write($"DNS PASSTHROUGH unexpected {qname} proc={name}", true);
            SendOn(_dnsHandle, packet, length, ref addr);
            return;
        }

        FileLog.Write($"DNS steal {qname} type={qtype} via Happ (proc={name})");

        if (qtype == 1 && DnsCache.TryGet(qname, out var cached) && cached.Length > 0)
        {
            var response = DnsOverProxy.BuildAResponse(query, cached);
            InjectDnsResponse(packet, parsed, response, addr.IfIdx, addr.SubIfIdx, addr.LayerEventFlags);
            FileLog.Write($"DNS cache-hit {qname} -> {string.Join(", ", cached.Select(i => i.ToString()))}");
            return;
        }

        if (qtype == 28)
        {
            InjectDnsResponse(packet, parsed, DnsOverProxy.EmptyAnswer(query), addr.IfIdx, addr.SubIfIdx, addr.LayerEventFlags);
            if (cloudName)
                FileLog.Write($"DNS AAAA {qname}: empty (force IPv4)");
            return;
        }

        var hops = FirstHopsForProcess(name);
        if (hops.Count == 0)
        {
            SendOn(_dnsHandle, packet, length, ref addr);
            return;
        }

        var packetCopy = new byte[length];
        Buffer.BlockCopy(packet, 0, packetCopy, 0, length);
        var parsedCopy = parsed;
        var flags = addr.LayerEventFlags;
        var ifIdx = addr.IfIdx;
        var subIfIdx = addr.SubIfIdx;
        _ = Task.Run(async () =>
        {
            var original = new WinDivertAddress { LayerEventFlags = flags, IfIdx = ifIdx, SubIfIdx = subIfIdx };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var response = await ResolveViaProxyAsync(query, hops, CancellationToken.None).ConfigureAwait(false);
                DnsCache.ObserveResponse(qname, response);
                var records = DnsOverProxy.ReadARecords(response);
                FileLog.Write($"DNS answer {qname} -> {string.Join(", ", records)} ({response.Length}b, {sw.ElapsedMilliseconds}ms) inject→{parsedCopy.SrcAddress}:{parsedCopy.SrcPort}");
                InjectDnsResponse(packetCopy, parsedCopy, response, ifIdx, subIfIdx, flags);
            }
            catch (Exception ex)
            {
                FileLog.Write($"DNS {qname} via proxy failed after {sw.ElapsedMilliseconds}ms: {ex.Message}", true);
                SendOn(_dnsHandle, packetCopy, packetCopy.Length, ref original);
            }
        });
    }

    private async Task PrewarmCloudDns(IReadOnlyList<ProxyServer> hops, CancellationToken cancellationToken)
    {
        var names = new[]
        {
            "steamcloudsweden.blob.core.windows.net",
            "steamclouduseast2.blob.core.windows.net",
            "steamcloud-frf.s3.dualstack.eu-central-1.amazonaws.com",
            "steamcloud-eu-ams.storage.googleapis.com",
            "api.steampowered.com",
            "cdn.steampipe.steamcontent.com",
            "lancache.steamcontent.com"
        };
        foreach (var name in names)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var query = DnsOverProxy.BuildQuery(name, 1);
                var response = await ResolveViaProxyAsync(query, hops, cancellationToken).ConfigureAwait(false);
                DnsCache.ObserveResponse(name, response);
                FileLog.Write($"DNS prewarm {name} -> {string.Join(", ", DnsOverProxy.ReadARecords(response))} ({sw.ElapsedMilliseconds}ms)");
            }
            catch (Exception ex)
            {
                FileLog.Write($"DNS prewarm {name} failed: {ex.Message}", true);
            }
        }
    }

    private async Task<byte[]> ResolveViaProxyAsync(byte[] query, IReadOnlyList<ProxyServer> hops, CancellationToken cancellationToken)
    {
        var servers = new[] { _profile.DnsServer, "1.1.1.1", "8.8.8.8" }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        Exception? last = null;
        foreach (var server in servers)
        {
            if (!IPAddress.TryParse(server, out var ip))
                continue;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(6));
                FileLog.Write($"DNS DoH try {ip}:443 via SOCKS");
                return await DnsOverProxy.ResolveDoHAsync(query, hops, ip, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                last = ex;
                FileLog.Write($"DNS TCP {server}:53 via SOCKS failed: {ex.Message}", true);
            }
        }
        throw last ?? new IOException("Нет DNS-сервера для резолва через прокси.");
    }

    private void InjectDnsResponse(
        byte[] original, ParsedPacket parsed, byte[] response, uint ifIdx, uint subIfIdx, uint flags)
    {
        var ipLen = parsed.IpHeaderLength;
        var total = ipLen + 8 + response.Length;
        var packet = new byte[total];
        Buffer.BlockCopy(original, 0, packet, 0, ipLen);
        packet[8] = 64;
        PacketParser.SetAddresses(packet, parsed.DstAddress, parsed.SrcAddress);
        PacketParser.SetIpLength(packet, total);
        PacketParser.SetUdpPorts(packet, ipLen, 53, parsed.SrcPort);
        PacketParser.SetUdpLength(packet, ipLen, 8 + response.Length);
        Buffer.BlockCopy(response, 0, packet, ipLen + 8, response.Length);

        var addr = new WinDivertAddress
        {
            LayerEventFlags = flags,
            IfIdx = ifIdx,
            SubIfIdx = subIfIdx
        };
        addr.Outbound = false;
        bool sent;
        lock (_sendLock)
        {
            WinDivertNative.WinDivertHelperCalcChecksums(packet, (uint)total, ref addr, 0);
            sent = WinDivertNative.WinDivertSend(_dnsHandle, packet, (uint)total, out _, ref addr);
        }
        if (!sent)
            FileLog.Write($"DNS inject FAIL win32={Marshal.GetLastWin32Error()} to {parsed.SrcAddress}:{parsed.SrcPort} {total}b", true);
    }

    private IReadOnlyList<ProxyServer> FirstHopsForProcess(string processName)
    {
        foreach (var rule in _profile.Rules.Where(r => r.Enabled && r.Action == RuleAction.Proxy))
        {
            if (!RuleEngine.ProcessMatches(rule.Applications, processName))
                continue;
            var hops = _rules.ResolveHops(rule.ProxyId);
            if (hops.Count > 0)
                return hops;
        }
        return _profile.Proxies.Take(1).ToList();
    }

    private void QuicLoop(CancellationToken cancellationToken)
    {
        var packet = new byte[0xFFFF];
        while (!cancellationToken.IsCancellationRequested && _running)
        {
            var addr = new WinDivertAddress();
            if (!WinDivertNative.WinDivertRecv(_quicHandle, packet, (uint)packet.Length, out var recvLen, ref addr))
            {
                if (cancellationToken.IsCancellationRequested || !_running)
                    return;
                continue;
            }

            var parsed = PacketParser.Parse(packet, (int)recvLen);
            if (!parsed.Ok || !parsed.IsUdp)
            {
                SendOn(_quicHandle, packet, (int)recvLen, ref addr);
                continue;
            }

            var pid = _processes.FindUdpPid(parsed.SrcPort);
            var name = pid == _selfPid ? "ProxyPilot.exe" : _processes.GetName(pid);
            if (pid != _selfPid && _rules.ProcessHasProxyRule(name))
                continue;

            SendOn(_quicHandle, packet, (int)recvLen, ref addr);
        }
    }

    private void ReflectToRelay(byte[] packet, int length, ParsedPacket parsed, ushort relayPort, ref WinDivertAddress addr)
    {
        PacketParser.SetAddresses(packet, parsed.DstAddress, parsed.SrcAddress);
        PacketParser.SetTcpPorts(packet, parsed.TransportOffset, parsed.SrcPort, relayPort);
        addr.Outbound = false;
        SendOn(_networkHandle, packet, length, ref addr);
    }

    private void ReflectFromRelay(byte[] packet, int length, ParsedPacket parsed, ref WinDivertAddress addr)
    {
        if (!_relay!.TryGet(parsed.DstPort, out var entry))
        {
            Passthrough(packet, length, ref addr);
            return;
        }

        PacketParser.SetAddresses(packet, entry.DestAddress, entry.ClientAddress);
        PacketParser.SetTcpPorts(packet, parsed.TransportOffset, entry.DestPort, parsed.DstPort);
        addr.Outbound = false;
        SendOn(_networkHandle, packet, length, ref addr);
    }

    private void Passthrough(byte[] packet, int length, ref WinDivertAddress addr)
    {
        if (WinDivertNative.IsInvalid(_networkHandle))
            return;
        lock (_sendLock)
            WinDivertNative.WinDivertSend(_networkHandle, packet, (uint)length, out _, ref addr);
    }

    private void SendOn(IntPtr handle, byte[] packet, int length, ref WinDivertAddress addr)
    {
        if (WinDivertNative.IsInvalid(handle))
            return;
        lock (_sendLock)
        {
            WinDivertNative.WinDivertHelperCalcChecksums(packet, (uint)length, ref addr, 0);
            WinDivertNative.WinDivertSend(handle, packet, (uint)length, out _, ref addr);
        }
    }

    private void LogSyn(string name, int pid, ParsedPacket parsed, string action, string ruleName)
    {
        var host = DnsCache.Lookup(parsed.DstAddress);
        var interesting = action is "PROXY" or "Блок"
                          || name.Contains("steam", StringComparison.OrdinalIgnoreCase)
                          || name.Contains("Code", StringComparison.OrdinalIgnoreCase)
                          || name.Contains("codex", StringComparison.OrdinalIgnoreCase)
                          || name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                          || name.Equals("nvcontainer.exe", StringComparison.OrdinalIgnoreCase)
                          || DnsCache.IsCloudHost(host);
        if (interesting)
            FileLog.Write($"TCP {action} {name} pid={pid} {parsed.DstAddress}:{parsed.DstPort} host={host ?? "-"} rule={ruleName}");

        var mapped = action switch
        {
            "PROXY" => RuleAction.Proxy,
            "Блок" => RuleAction.Block,
            _ => RuleAction.Direct
        };
        if (interesting)
        {
            ConnectionChanged?.Invoke(new ConnectionEvent
            {
                ProcessName = name,
                Pid = pid,
                Target = host is null ? $"{parsed.DstAddress}:{parsed.DstPort}" : $"{host} ({parsed.DstAddress}):{parsed.DstPort}",
                RuleName = ruleName,
                Action = mapped,
                Status = action
            });
        }
    }

    private void Emit(string message, bool error = false)
    {
        FileLog.Write(message, error);
        Log?.Invoke(new EngineLogEvent { Message = message, IsError = error });
    }

    private static void Close(ref IntPtr handle)
    {
        if (WinDivertNative.IsInvalid(handle))
            return;
        WinDivertNative.WinDivertShutdown(handle, WinDivertNative.ShutdownBoth);
        WinDivertNative.WinDivertClose(handle);
        handle = new IntPtr(-1);
    }

    private static string OpenError(string what)
    {
        var err = Marshal.GetLastWin32Error();
        return err switch
        {
            5 => $"Нет прав администратора для {what}.",
            2 => "Не найден WinDivert.dll / WinDivert64.sys рядом с программой.",
            577 or 654 => "Драйвер WinDivert заблокирован (нужна подпись/отключение ложного срабатывания антивируса).",
            _ => $"Не удалось открыть {what}. Код Win32 {err}."
        };
    }
}
