namespace ProxyPilot.Core.Native;

internal static class IcmpPortUnreachable
{
    public static byte[]? Build(byte[] original, int length, ParsedPacket parsed)
    {
        if (!parsed.Ok || !parsed.IsUdp || parsed.Fragment || parsed.IpHeaderLength < 20)
            return null;
        var quoted = parsed.IpHeaderLength + 8;
        if (length < quoted || quoted > 60 + 8)
            return null;

        var total = 20 + 8 + quoted;
        var packet = new byte[total];
        packet[0] = 0x45;
        packet[8] = 64;
        packet[9] = 1;
        PacketParser.SetIpLength(packet, total);
        PacketParser.SetAddresses(packet, parsed.DstAddress, parsed.SrcAddress);
        packet[20] = 3;
        packet[21] = 3;
        Buffer.BlockCopy(original, 0, packet, 28, quoted);
        return packet;
    }
}
