using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ProxyPilot.Core.Config;

namespace ProxyPilot.Core.Engine;

public static class DnsOverProxy
{
    public static Task<byte[]> ResolveAsync(
        byte[] query,
        IReadOnlyList<ProxyServer> hops,
        IPAddress dnsServer,
        CancellationToken cancellationToken) =>
        ResolveDoHAsync(query, hops, dnsServer, cancellationToken);

    public static async Task<byte[]> ResolveDoHAsync(
        byte[] query,
        IReadOnlyList<ProxyServer> hops,
        IPAddress httpsDns,
        CancellationToken cancellationToken)
    {
        using var tcp = await ProxyClient.ConnectAsync(
            hops,
            new IPEndPoint(httpsDns, 443),
            cancellationToken,
            TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        using var ssl = new System.Net.Security.SslStream(tcp.GetStream(), false, static (_, _, _, _) => true);
        var sni = httpsDns.Equals(IPAddress.Parse("8.8.8.8")) ? "dns.google" : "cloudflare-dns.com";
        await ssl.AuthenticateAsClientAsync(sni).ConfigureAwait(false);

        var body = query;
        var header =
            $"POST /dns-query HTTP/1.1\r\n" +
            $"Host: {sni}\r\n" +
            "Accept: application/dns-message\r\n" +
            "Content-Type: application/dns-message\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n";
        await ssl.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken).ConfigureAwait(false);
        await ssl.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await ssl.FlushAsync(cancellationToken).ConfigureAwait(false);

        var raw = new MemoryStream();
        await ssl.CopyToAsync(raw, cancellationToken).ConfigureAwait(false);
        var bytes = raw.ToArray();
        var split = IndexOf(bytes, "\r\n\r\n"u8.ToArray());
        if (split < 0)
            throw new IOException("DoH: нет заголовков HTTP.");
        var head = Encoding.ASCII.GetString(bytes, 0, split);
        if (!head.Contains(" 200"))
            throw new IOException("DoH HTTP " + head.Split('\n')[0].Trim());
        var payload = bytes[(split + 4)..];
        foreach (var line in head.Split("\r\n"))
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (int.TryParse(line["Content-Length:".Length..].Trim(), out var cl) && cl > 0 && cl <= payload.Length)
                payload = payload[..cl];
            break;
        }
        if (payload.Length < 12)
            throw new IOException("DoH: пустое тело.");
        return payload;
    }

    private static int IndexOf(byte[] hay, byte[] needle)
    {
        for (var i = 0; i <= hay.Length - needle.Length; i++)
        {
            var n = 0;
            while (n < needle.Length && hay[i + n] == needle[n]) n++;
            if (n == needle.Length)
                return i;
        }
        return -1;
    }

    public static string? FirstQuestionName(byte[] message)
    {
        if (!TryReadQuestion(message, out var name, out _, out _))
            return null;
        return name;
    }

    public static ushort QuestionType(byte[] message)
    {
        TryReadQuestion(message, out _, out var type, out _);
        return type;
    }

    public static byte[] BuildQuery(string name, ushort type)
    {
        var labels = name.Trim().TrimEnd('.').Split('.')
            .SelectMany(l => new[] { (byte)l.Length }.Concat(Encoding.ASCII.GetBytes(l)))
            .Concat(new byte[] { 0 }).ToArray();
        var msg = new byte[12 + labels.Length + 4];
        msg[0] = 0x12;
        msg[1] = 0x34;
        msg[2] = 0x01;
        msg[5] = 1;
        labels.CopyTo(msg, 12);
        var t = 12 + labels.Length;
        msg[t] = (byte)(type >> 8);
        msg[t + 1] = (byte)type;
        msg[t + 3] = 1;
        return msg;
    }

    public static byte[] BuildAResponse(byte[] query, IReadOnlyList<IPAddress> ips)
    {
        if (!TryReadQuestion(query, out _, out _, out var questionEnd))
            return EmptyAnswer(query);
        var v4 = ips.Where(i => i.AddressFamily == AddressFamily.InterNetwork).ToList();
        var response = new byte[questionEnd + v4.Count * 16];
        Buffer.BlockCopy(query, 0, response, 0, questionEnd);
        var flags = BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(2, 2));
        flags = (ushort)((flags | 0x8080) & ~0x0200);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), flags);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(6, 2), (ushort)v4.Count);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(8, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(10, 2), 0);
        var o = questionEnd;
        foreach (var ip in v4)
        {
            response[o] = 0xC0;
            response[o + 1] = 0x0C;
            response[o + 3] = 1;
            response[o + 5] = 1;
            response[o + 10] = 0;
            response[o + 11] = 4;
            BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(o + 6, 4), 60);
            ip.GetAddressBytes().CopyTo(response, o + 12);
            o += 16;
        }
        return response;
    }

    public static byte[] EmptyAnswer(byte[] query)
    {
        if (!TryReadQuestion(query, out _, out _, out var questionEnd))
            return query;
        var response = new byte[questionEnd];
        Buffer.BlockCopy(query, 0, response, 0, questionEnd);
        var flags = BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(2, 2));
        flags = (ushort)((flags | 0x8080) & ~0x0200);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), flags);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(6, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(8, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(10, 2), 0);
        return response;
    }

    public static List<IPAddress> ReadARecords(byte[] message)
    {
        var result = new List<IPAddress>();
        if (message.Length < 12)
            return result;
        var qd = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(4, 2));
        var an = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(6, 2));
        var offset = 12;
        for (var i = 0; i < qd; i++)
        {
            if (!SkipName(message, ref offset) || offset + 4 > message.Length)
                return result;
            offset += 4;
        }

        for (var i = 0; i < an; i++)
        {
            if (!SkipName(message, ref offset) || offset + 10 > message.Length)
                return result;
            var type = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(offset, 2));
            var rdlength = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(offset + 8, 2));
            offset += 10;
            if (offset + rdlength > message.Length)
                return result;
            if (type == 1 && rdlength == 4)
                result.Add(new IPAddress(message.AsSpan(offset, 4)));
            offset += rdlength;
        }
        return result;
    }

    public static bool TryReadQuestion(byte[] message, out string? name, out ushort type, out int end)
    {
        name = null;
        type = 0;
        end = 0;
        if (message.Length < 12)
            return false;
        var offset = 12;
        var labels = new List<string>();
        while (offset < message.Length)
        {
            var len = message[offset];
            if (len == 0)
            {
                offset++;
                break;
            }
            if ((len & 0xC0) == 0xC0)
                return false;
            offset++;
            if (offset + len > message.Length)
                return false;
            labels.Add(Encoding.ASCII.GetString(message, offset, len));
            offset += len;
        }
        if (offset + 4 > message.Length)
            return false;
        type = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(offset, 2));
        end = offset + 4;
        name = labels.Count == 0 ? null : string.Join('.', labels);
        return name != null;
    }

    private static bool SkipName(byte[] message, ref int offset)
    {
        var jumps = 0;
        var cursor = offset;
        var advanced = false;
        while (cursor < message.Length && jumps < 10)
        {
            var len = message[cursor];
            if (len == 0)
            {
                if (!advanced)
                    offset = cursor + 1;
                return true;
            }
            if ((len & 0xC0) == 0xC0)
            {
                if (cursor + 1 >= message.Length)
                    return false;
                if (!advanced)
                    offset = cursor + 2;
                cursor = ((len & 0x3F) << 8) | message[cursor + 1];
                jumps++;
                advanced = true;
                continue;
            }
            cursor += 1 + len;
            if (!advanced)
                offset = cursor;
        }
        return false;
    }
}
