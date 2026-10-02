using System.Net;
using System.Net.Sockets;
using ProxyPilot.Core.Config;

namespace ProxyPilot.Core.Engine;

public static class Socks5Udp
{
    public static bool IsLocal(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return true;
        var b = address.GetAddressBytes();
        if (b[0] is 0 or 127 or >= 224)
            return true;
        if (b[0] == 10)
            return true;
        if (b[0] == 192 && b[1] == 168)
            return true;
        if (b[0] == 172 && b[1] is >= 16 and <= 31)
            return true;
        if (b[0] == 169 && b[1] == 254)
            return true;
        return false;
    }

    public static byte[] Encode(IPAddress address, int port, ReadOnlySpan<byte> payload)
    {
        var ip = address.GetAddressBytes();
        if (ip.Length != 4)
            throw new ArgumentException("SOCKS5 UDP relay supports IPv4.", nameof(address));
        var buf = new byte[10 + payload.Length];
        buf[3] = 1;
        ip.CopyTo(buf.AsSpan(4));
        buf[8] = (byte)(port >> 8);
        buf[9] = (byte)port;
        payload.CopyTo(buf.AsSpan(10));
        return buf;
    }

    public static bool TryDecode(ReadOnlySpan<byte> packet, out IPAddress address, out int port, out int payloadOffset)
    {
        address = IPAddress.None;
        port = 0;
        payloadOffset = 0;
        if (packet.Length < 10 || packet[2] != 0 || packet[3] != 1)
            return false;
        address = new IPAddress(packet.Slice(4, 4));
        port = (packet[8] << 8) | packet[9];
        payloadOffset = 10;
        return true;
    }
}

internal sealed class Socks5UdpTunnel : IDisposable
{
    private readonly ProxyServer _proxy;
    private readonly object _gate = new();
    private TcpClient? _control;
    private UdpClient? _udp;
    private IPEndPoint? _relay;

    public Socks5UdpTunnel(ProxyServer proxy) => _proxy = proxy;

    public bool IsUp
    {
        get { lock (_gate) return _udp != null; }
    }

    public bool TrySend(IPAddress destination, int port, ReadOnlySpan<byte> payload)
    {
        try
        {
            EnsureConnected();
            var datagram = Socks5Udp.Encode(destination, port, payload);
            lock (_gate)
            {
                if (_udp == null || _relay == null)
                    return false;
                _udp.Send(datagram, datagram.Length, _relay);
            }
            return true;
        }
        catch (Exception ex)
        {
            Drop();
            FileLog.Write($"UDP Fallout 76: отправка не удалась: {ex.Message}", true);
            return false;
        }
    }

    public bool TryReceive(byte[] buffer, out int length)
    {
        length = 0;
        UdpClient? udp;
        lock (_gate)
            udp = _udp;
        if (udp == null)
            return false;
        try
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            var data = udp.Receive(ref remote);
            if (data.Length > buffer.Length)
                return false;
            Buffer.BlockCopy(data, 0, buffer, 0, data.Length);
            length = data.Length;
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
        {
            return false;
        }
    }

    public void Drop()
    {
        lock (_gate)
        {
            try { _udp?.Close(); } catch { /* ignore */ }
            try { _control?.Close(); } catch { /* ignore */ }
            _udp = null;
            _control = null;
            _relay = null;
        }
    }

    public void Dispose() => Drop();

    private void EnsureConnected()
    {
        lock (_gate)
        {
            if (_udp != null)
                return;
            var tcp = new TcpClient { NoDelay = true };
            tcp.ReceiveTimeout = 8000;
            tcp.SendTimeout = 8000;
            try
            {
                var ep = _proxy.EndPoint();
                tcp.Connect(ep);
                var stream = tcp.GetStream();
                Handshake(stream);
                var relay = Associate(stream);
                var udp = new UdpClient(0);
                udp.Client.ReceiveTimeout = 1000;
                _control = tcp;
                _udp = udp;
                _relay = relay;
                FileLog.Write($"UDP Fallout 76: SOCKS5 UDP {relay.Address}:{relay.Port} via {_proxy.Display}");
            }
            catch
            {
                tcp.Dispose();
                throw;
            }
        }
    }

    private void Handshake(NetworkStream stream)
    {
        var useAuth = !string.IsNullOrEmpty(_proxy.Username);
        stream.Write(useAuth ? new byte[] { 5, 2, 0, 2 } : new byte[] { 5, 1, 0 });
        var method = ReadExact(stream, 2);
        if (method[0] != 5)
            throw new IOException("Прокси вернул не SOCKS5.");
        if (method[1] == 2)
        {
            var user = System.Text.Encoding.UTF8.GetBytes(_proxy.Username ?? "");
            var pass = System.Text.Encoding.UTF8.GetBytes(_proxy.Password ?? "");
            var auth = new byte[3 + user.Length + pass.Length];
            auth[0] = 1;
            auth[1] = (byte)user.Length;
            user.CopyTo(auth.AsSpan(2));
            auth[2 + user.Length] = (byte)pass.Length;
            pass.CopyTo(auth.AsSpan(3 + user.Length));
            stream.Write(auth);
            var authReply = ReadExact(stream, 2);
            if (authReply[1] != 0)
                throw new IOException("SOCKS5: отказ в аутентификации.");
        }
        else if (method[1] != 0)
        {
            throw new IOException($"SOCKS5: метод {method[1]} не поддерживается.");
        }
    }

    private static IPEndPoint Associate(NetworkStream stream)
    {
        stream.Write(new byte[] { 5, 3, 0, 1, 0, 0, 0, 0, 0, 0 });
        var head = ReadExact(stream, 4);
        if (head[1] != 0)
            throw new IOException($"SOCKS5 UDP ASSOCIATE отклонён, код {head[1]}.");
        if (head[3] != 1)
            throw new IOException("SOCKS5 UDP ASSOCIATE вернул не IPv4.");
        var body = ReadExact(stream, 6);
        var address = new IPAddress(body.AsSpan(0, 4));
        var port = (body[4] << 8) | body[5];
        if (port == 0)
            throw new IOException("SOCKS5 UDP ASSOCIATE не выдал порт.");
        return new IPEndPoint(address, port);
    }

    private static byte[] ReadExact(NetworkStream stream, int count)
    {
        var buf = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var n = stream.Read(buf, offset, count - offset);
            if (n == 0)
                throw new EndOfStreamException("Прокси закрыл соединение.");
            offset += n;
        }
        return buf;
    }
}
