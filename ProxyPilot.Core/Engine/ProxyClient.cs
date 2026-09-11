using System.Net;
using System.Net.Sockets;
using System.Text;
using ProxyPilot.Core.Config;

namespace ProxyPilot.Core.Engine;

public static class ProxyClient
{
    public static async Task<TcpClient> ConnectAsync(
        IReadOnlyList<ProxyServer> hops,
        IPEndPoint destination,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        string? hostname = null)
    {
        if (hops.Count == 0)
            throw new InvalidOperationException("Не задан прокси-сервер.");

        var first = hops[0];
        var client = new TcpClient { NoDelay = true };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(20));
        try
        {
            var ep = first.EndPoint();
            await client.ConnectAsync(ep.Address, ep.Port, timeoutCts.Token).ConfigureAwait(false);
            var stream = client.GetStream();

            for (var i = 0; i < hops.Count; i++)
            {
                var hop = hops[i];
                var next = i == hops.Count - 1
                    ? destination
                    : hops[i + 1].EndPoint();
                var name = i == hops.Count - 1 ? hostname : null;
                await HandshakeAsync(stream, hop, next, timeoutCts.Token, name).ConfigureAwait(false);
            }

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public static Task HandshakeAsync(
        Stream stream,
        ProxyServer proxy,
        IPEndPoint destination,
        CancellationToken cancellationToken,
        string? hostname = null) =>
        proxy.Type switch
        {
            ProxyType.Socks4 => Socks4Async(stream, destination, cancellationToken),
            ProxyType.HttpConnect => HttpConnectAsync(stream, proxy, destination, cancellationToken, hostname),
            _ => Socks5Async(stream, proxy, destination, cancellationToken, hostname)
        };

    private static async Task Socks5Async(Stream stream, ProxyServer proxy, IPEndPoint destination, CancellationToken cancellationToken, string? hostname = null)
    {
        var useAuth = !string.IsNullOrEmpty(proxy.Username);
        if (useAuth)
            await stream.WriteAsync(new byte[] { 5, 2, 0, 2 }, cancellationToken).ConfigureAwait(false);
        else
            await stream.WriteAsync(new byte[] { 5, 1, 0 }, cancellationToken).ConfigureAwait(false);

        var method = new byte[2];
        await ReadExactAsync(stream, method, cancellationToken).ConfigureAwait(false);
        if (method[0] != 5)
            throw new IOException("Прокси вернул не SOCKS5.");

        if (method[1] == 2)
        {
            var user = Encoding.UTF8.GetBytes(proxy.Username ?? "");
            var pass = Encoding.UTF8.GetBytes(proxy.Password ?? "");
            var auth = new byte[3 + user.Length + pass.Length];
            auth[0] = 1;
            auth[1] = (byte)user.Length;
            Buffer.BlockCopy(user, 0, auth, 2, user.Length);
            auth[2 + user.Length] = (byte)pass.Length;
            Buffer.BlockCopy(pass, 0, auth, 3 + user.Length, pass.Length);
            await stream.WriteAsync(auth, cancellationToken).ConfigureAwait(false);
            var authReply = new byte[2];
            await ReadExactAsync(stream, authReply, cancellationToken).ConfigureAwait(false);
            if (authReply[1] != 0)
                throw new IOException("SOCKS5: отказ в аутентификации.");
        }
        else if (method[1] != 0)
        {
            throw new IOException($"SOCKS5: метод {method[1]} не поддерживается.");
        }

        await SendSocksRequestAsync(stream, destination, hostname, cancellationToken).ConfigureAwait(false);
        await ReadSocks5ReplyAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendSocksRequestAsync(Stream stream, IPEndPoint destination, string? hostname, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(hostname) && hostname.Length is > 0 and < 256 && !IPAddress.TryParse(hostname, out _))
        {
            var hostBytes = Encoding.ASCII.GetBytes(hostname);
            var request = new byte[7 + hostBytes.Length];
            request[0] = 5;
            request[1] = 1;
            request[2] = 0;
            request[3] = 3;
            request[4] = (byte)hostBytes.Length;
            hostBytes.CopyTo(request, 5);
            request[5 + hostBytes.Length] = (byte)(destination.Port >> 8);
            request[6 + hostBytes.Length] = (byte)destination.Port;
            await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            return;
        }

        var addr = destination.Address;
        if (addr.IsIPv4MappedToIPv6)
            addr = addr.MapToIPv4();

        byte[] ipRequest;
        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            ipRequest = new byte[10];
            ipRequest[0] = 5;
            ipRequest[1] = 1;
            ipRequest[2] = 0;
            ipRequest[3] = 1;
            addr.GetAddressBytes().CopyTo(ipRequest, 4);
            ipRequest[8] = (byte)(destination.Port >> 8);
            ipRequest[9] = (byte)destination.Port;
        }
        else
        {
            var bytes = addr.GetAddressBytes();
            ipRequest = new byte[22];
            ipRequest[0] = 5;
            ipRequest[1] = 1;
            ipRequest[2] = 0;
            ipRequest[3] = 4;
            bytes.CopyTo(ipRequest, 4);
            ipRequest[20] = (byte)(destination.Port >> 8);
            ipRequest[21] = (byte)destination.Port;
        }

        await stream.WriteAsync(ipRequest, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadSocks5ReplyAsync(Stream stream, CancellationToken cancellationToken)
    {
        var head = new byte[4];
        await ReadExactAsync(stream, head, cancellationToken).ConfigureAwait(false);
        if (head[1] != 0)
            throw new IOException($"SOCKS5 CONNECT отклонён, код {head[1]}.");

        var rest = head[3] switch
        {
            1 => 6,
            4 => 18,
            3 => await ReadDomainTailAsync(stream, cancellationToken),
            _ => throw new IOException("Неизвестный ATYP в ответе SOCKS5.")
        };
        if (head[3] != 3)
        {
            var buf = new byte[rest];
            await ReadExactAsync(stream, buf, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> ReadDomainTailAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lenBuf = new byte[1];
        await ReadExactAsync(stream, lenBuf, cancellationToken).ConfigureAwait(false);
        var tail = new byte[lenBuf[0] + 2];
        await ReadExactAsync(stream, tail, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static async Task Socks4Async(Stream stream, IPEndPoint destination, CancellationToken cancellationToken)
    {
        var addr = destination.Address;
        if (addr.IsIPv4MappedToIPv6)
            addr = addr.MapToIPv4();
        if (addr.AddressFamily != AddressFamily.InterNetwork)
            throw new IOException("SOCKS4 поддерживает только IPv4.");

        var request = new byte[9];
        request[0] = 4;
        request[1] = 1;
        request[2] = (byte)(destination.Port >> 8);
        request[3] = (byte)destination.Port;
        addr.GetAddressBytes().CopyTo(request, 4);
        request[8] = 0;
        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);

        var reply = new byte[8];
        await ReadExactAsync(stream, reply, cancellationToken).ConfigureAwait(false);
        if (reply[1] != 90)
            throw new IOException($"SOCKS4 отклонён, код {reply[1]}.");
    }

    private static async Task HttpConnectAsync(Stream stream, ProxyServer proxy, IPEndPoint destination, CancellationToken cancellationToken, string? hostname = null)
    {
        var host = string.IsNullOrWhiteSpace(hostname) ? destination.Address.ToString() : hostname;
        var req = new StringBuilder()
            .Append("CONNECT ").Append(host).Append(':').Append(destination.Port).Append(" HTTP/1.1\r\n")
            .Append("Host: ").Append(host).Append(':').Append(destination.Port).Append("\r\n");
        if (!string.IsNullOrEmpty(proxy.Username))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{proxy.Username}:{proxy.Password}"));
            req.Append("Proxy-Authorization: Basic ").Append(token).Append("\r\n");
        }
        req.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(req.ToString()), cancellationToken).ConfigureAwait(false);

        var buffer = new byte[1024];
        var total = 0;
        var text = "";
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken).ConfigureAwait(false);
            if (n <= 0)
                break;
            total += n;
            text = Encoding.ASCII.GetString(buffer, 0, total);
            if (text.Contains("\r\n\r\n"))
                break;
        }

        var statusLine = text.Split('\n')[0];
        if (!statusLine.Contains(" 200"))
            throw new IOException("HTTP CONNECT не установлен: " + statusLine.Trim());
    }

    public static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
            if (n == 0)
                throw new EndOfStreamException("Прокси закрыл соединение.");
            offset += n;
        }
    }
}
