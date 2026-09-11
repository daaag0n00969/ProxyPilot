using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace ProxyPilot.Core.Native;

internal readonly struct ParsedPacket
{
    public readonly bool Ok;
    public readonly int IpHeaderLength;
    public readonly int TransportOffset;
    public readonly ProtocolType Protocol;
    public readonly IPAddress SrcAddress;
    public readonly IPAddress DstAddress;
    public readonly ushort SrcPort;
    public readonly ushort DstPort;
    public readonly byte TcpFlags;
    public readonly bool Fragment;
    public readonly int UdpPayloadOffset;
    public readonly int UdpPayloadLength;

    public bool IsTcp => Protocol == ProtocolType.Tcp;
    public bool IsUdp => Protocol == ProtocolType.Udp;
    public bool IsSyn => (TcpFlags & 0x02) != 0 && (TcpFlags & 0x10) == 0;
    public bool IsRst => (TcpFlags & 0x04) != 0;
    public bool IsFin => (TcpFlags & 0x01) != 0;

    public ParsedPacket(bool ok, int ipHeaderLength, int transportOffset, ProtocolType protocol,
        IPAddress src, IPAddress dst, ushort srcPort, ushort dstPort, byte tcpFlags, bool fragment,
        int udpPayloadOffset, int udpPayloadLength)
    {
        Ok = ok;
        IpHeaderLength = ipHeaderLength;
        TransportOffset = transportOffset;
        Protocol = protocol;
        SrcAddress = src;
        DstAddress = dst;
        SrcPort = srcPort;
        DstPort = dstPort;
        TcpFlags = tcpFlags;
        Fragment = fragment;
        UdpPayloadOffset = udpPayloadOffset;
        UdpPayloadLength = udpPayloadLength;
    }

    public static ParsedPacket Fail => new(false, 0, 0, ProtocolType.Unspecified, IPAddress.None, IPAddress.None, 0, 0, 0, false, 0, 0);
}

internal static class PacketParser
{
    public static ParsedPacket Parse(byte[] buffer, int length)
    {
        if (length < 20)
            return ParsedPacket.Fail;
        var versionIhl = buffer[0];
        if ((versionIhl >> 4) != 4)
            return ParsedPacket.Fail;
        var ihl = (versionIhl & 0x0F) * 4;
        if (ihl < 20 || length < ihl)
            return ParsedPacket.Fail;

        var frag = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(6, 2));
        var offset = (ushort)(frag & 0x1FFF);
        var moreFrag = (frag & 0x2000) != 0;
        var fragment = moreFrag || offset != 0;

        var protocol = (ProtocolType)buffer[9];
        var src = new IPAddress(buffer.AsSpan(12, 4));
        var dst = new IPAddress(buffer.AsSpan(16, 4));

        if (protocol == ProtocolType.Tcp)
        {
            if (length < ihl + 14)
                return ParsedPacket.Fail;
            var srcPort = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(ihl, 2));
            var dstPort = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(ihl + 2, 2));
            var flags = buffer[ihl + 13];
            return new ParsedPacket(true, ihl, ihl, protocol, src, dst, srcPort, dstPort, flags, fragment, 0, 0);
        }

        if (protocol == ProtocolType.Udp)
        {
            if (length < ihl + 8)
                return ParsedPacket.Fail;
            var srcPort = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(ihl, 2));
            var dstPort = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(ihl + 2, 2));
            var udpLen = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(ihl + 4, 2));
            var payloadLen = Math.Max(0, Math.Min(udpLen - 8, length - ihl - 8));
            return new ParsedPacket(true, ihl, ihl, protocol, src, dst, srcPort, dstPort, 0, fragment, ihl + 8, payloadLen);
        }

        return new ParsedPacket(true, ihl, ihl, protocol, src, dst, 0, 0, 0, fragment, 0, 0);
    }

    public static void SetAddresses(byte[] buffer, IPAddress src, IPAddress dst)
    {
        src.GetAddressBytes().CopyTo(buffer, 12);
        dst.GetAddressBytes().CopyTo(buffer, 16);
    }

    public static void SetTcpPorts(byte[] buffer, int tcpOffset, ushort srcPort, ushort dstPort)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(tcpOffset, 2), srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(tcpOffset + 2, 2), dstPort);
    }

    public static void SetUdpPorts(byte[] buffer, int udpOffset, ushort srcPort, ushort dstPort)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(udpOffset, 2), srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(udpOffset + 2, 2), dstPort);
    }

    public static void SetIpLength(byte[] buffer, int length) =>
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2, 2), (ushort)length);

    public static void SetUdpLength(byte[] buffer, int udpOffset, int udpLength) =>
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(udpOffset + 4, 2), (ushort)udpLength);
}
