using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace ProxyPilot.Core.Engine;

internal static class TlsSni
{
    public const int MaxHello = 16384 + 5;

    public static bool TryRead(ReadOnlySpan<byte> data, out string? hostname, out bool needMore)
    {
        hostname = null;
        needMore = false;
        if (data.Length < 5)
        {
            needMore = data.Length == 0 || data[0] == 0x16;
            return false;
        }

        if (data[0] != 0x16 || data[1] != 0x03)
            return false;

        var recordLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(3, 2));
        if (recordLen == 0 || recordLen > 16384)
            return false;
        if (data.Length < 5 + recordLen)
        {
            needMore = true;
            return false;
        }

        var handshake = data.Slice(5, recordLen);
        if (handshake.Length < 4 || handshake[0] != 0x01)
            return false;
        var helloLen = (handshake[1] << 16) | (handshake[2] << 8) | handshake[3];
        if (helloLen < 34 || handshake.Length < 4 + helloLen)
            return false;

        var body = handshake.Slice(4, helloLen);
        var index = 34;
        if (!Skip(body, ref index, 1) || !Skip(body, ref index, 2) || !Skip(body, ref index, 1))
            return false;
        if (index + 2 > body.Length)
            return false;

        var extensionsLength = ReadUInt16(body, ref index);
        if (index + extensionsLength > body.Length)
            return false;
        var extensionsEnd = index + extensionsLength;
        while (index + 4 <= extensionsEnd)
        {
            var extensionType = ReadUInt16(body, ref index);
            var extensionLength = ReadUInt16(body, ref index);
            if (index + extensionLength > extensionsEnd)
                return false;
            if (extensionType == 0)
            {
                hostname = ReadHostName(body.Slice(index, extensionLength));
                return hostname != null;
            }

            index += extensionLength;
        }

        return false;
    }

    public static async Task<(string? Hostname, byte[] Prefix)> PeekAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(800));
        var buffer = new byte[MaxHello];
        var got = 0;
        try
        {
            while (got < buffer.Length && !cancellationToken.IsCancellationRequested)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(got, Math.Min(4096, buffer.Length - got)), timeout.Token).ConfigureAwait(false);
                if (n <= 0)
                    break;
                got += n;
                if (!TryRead(buffer.AsSpan(0, got), out var hostname, out var needMore))
                {
                    if (needMore)
                        continue;
                    break;
                }

                return (hostname, buffer[..got]);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        if (got > 0 && TryRead(buffer.AsSpan(0, got), out var late, out _))
            return (late, buffer[..got]);
        return (null, got == 0 ? [] : buffer[..got]);
    }

    private static string? ReadHostName(ReadOnlySpan<byte> extension)
    {
        if (extension.Length < 5)
            return null;
        var listLength = BinaryPrimitives.ReadUInt16BigEndian(extension);
        if (listLength < 3 || 2 + listLength > extension.Length)
            return null;
        if (extension[2] != 0)
            return null;
        var nameLength = BinaryPrimitives.ReadUInt16BigEndian(extension.Slice(3, 2));
        if (nameLength == 0 || 5 + nameLength > extension.Length || nameLength > listLength - 3)
            return null;
        var name = Encoding.ASCII.GetString(extension.Slice(5, nameLength)).Trim().TrimEnd('.');
        if (name.Length is 0 or > 253 || !name.Contains('.') || IPAddress.TryParse(name, out _))
            return null;
        foreach (var ch in name)
        {
            if (ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '-' or '_')
                continue;
            return null;
        }

        return name;
    }

    private static bool Skip(ReadOnlySpan<byte> body, ref int index, int lengthBytes)
    {
        if (index + lengthBytes > body.Length)
            return false;
        var length = lengthBytes == 1 ? body[index] : BinaryPrimitives.ReadUInt16BigEndian(body.Slice(index, 2));
        index += lengthBytes;
        if (length < 0 || index + length > body.Length)
            return false;
        index += length;
        return true;
    }

    private static int ReadUInt16(ReadOnlySpan<byte> body, ref int index)
    {
        var value = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(index, 2));
        index += 2;
        return value;
    }
}
