using System.Net;
using System.Text;
using ProxyPilot.Core.Engine;
using ProxyPilot.Core.Native;
using Xunit;

namespace ProxyPilot.Tests;

public class ChatGptRouteTests
{
    [Fact]
    public void Sni_ReadsChatGptName()
    {
        var hello = ClientHello("chatgpt.com");
        Assert.True(TlsSni.TryRead(hello, out var host, out var needMore));
        Assert.False(needMore);
        Assert.Equal("chatgpt.com", host);
    }

    [Fact]
    public void Sni_IgnoresPlainHttpAndIncompleteRecord()
    {
        Assert.False(TlsSni.TryRead("GET / HTTP/1.1\r\n"u8, out var host, out var needMore));
        Assert.False(needMore);
        Assert.Null(host);

        var partial = ClientHello("files.oaiusercontent.com").AsSpan(0, 8).ToArray();
        Assert.False(TlsSni.TryRead(partial, out host, out needMore));
        Assert.True(needMore);
        Assert.Null(host);
    }

    [Fact]
    public void QuicReject_IsOnlyTheChatEditors()
    {
        Assert.True(EditorProcesses.RejectQuic("Code.exe"));
        Assert.True(EditorProcesses.RejectQuic("ChatGPT.exe"));
        Assert.True(EditorProcesses.UsesSni("ChatGPT.exe"));
        Assert.True(EditorProcesses.RejectQuic("Cursor.exe"));
        Assert.True(EditorProcesses.UsesSni("codex-code-mode-host.exe"));
        Assert.False(EditorProcesses.RejectQuic("steam.exe"));
        Assert.False(EditorProcesses.RejectQuic("Fallout76.exe"));
        Assert.False(EditorProcesses.RejectQuic("nvcontainer.exe"));
        Assert.False(EditorProcesses.RejectQuic("Microsoft.VisualStudio.Code.Server.exe"));
        Assert.False(EditorProcesses.RejectQuic("codex-windows-sandbox-service.exe"));
    }

    [Fact]
    public void Icmp_QuotesUdpPortsAndLeavesOtherPacketsAlone()
    {
        var udp = UdpPacket(IPAddress.Parse("192.168.1.61"), 50000, IPAddress.Parse("8.47.69.6"), 443);
        var parsed = PacketParser.Parse(udp, udp.Length);
        var icmp = IcmpPortUnreachable.Build(udp, udp.Length, parsed);
        Assert.NotNull(icmp);
        Assert.Equal(1, icmp![9]);
        Assert.Equal(3, icmp[20]);
        Assert.Equal(3, icmp[21]);
        Assert.Equal(udp.AsSpan(12, 4).ToArray(), icmp.AsSpan(28 + 12, 4).ToArray());
        Assert.Equal(udp.AsSpan(16, 4).ToArray(), icmp.AsSpan(28 + 16, 4).ToArray());
        Assert.Equal(443, (icmp[28 + parsed.IpHeaderLength + 2] << 8) | icmp[28 + parsed.IpHeaderLength + 3]);

        parsed = PacketParser.Parse(udp, 10);
        Assert.Null(IcmpPortUnreachable.Build(udp, 10, parsed));
    }

    private static byte[] ClientHello(string hostname)
    {
        var name = Encoding.ASCII.GetBytes(hostname);
        var serverName = new byte[2 + 1 + 2 + name.Length];
        serverName[0] = (byte)((name.Length + 3) >> 8);
        serverName[1] = (byte)(name.Length + 3);
        serverName[2] = 0;
        serverName[3] = (byte)(name.Length >> 8);
        serverName[4] = (byte)name.Length;
        name.CopyTo(serverName, 5);

        var extension = new byte[4 + serverName.Length];
        extension[2] = (byte)(serverName.Length >> 8);
        extension[3] = (byte)serverName.Length;
        serverName.CopyTo(extension, 4);

        var body = new byte[34 + 1 + 2 + 2 + 1 + 1 + 2 + extension.Length];
        body[0] = 3;
        body[1] = 3;
        body[34] = 0;
        body[35] = 0;
        body[36] = 2;
        body[37] = 0x00;
        body[38] = 0x2f;
        body[39] = 1;
        body[40] = 0;
        body[41] = (byte)(extension.Length >> 8);
        body[42] = (byte)extension.Length;
        extension.CopyTo(body, 43);

        var handshake = new byte[4 + body.Length];
        handshake[0] = 1;
        handshake[1] = (byte)(body.Length >> 16);
        handshake[2] = (byte)(body.Length >> 8);
        handshake[3] = (byte)body.Length;
        body.CopyTo(handshake, 4);

        var record = new byte[5 + handshake.Length];
        record[0] = 0x16;
        record[1] = 3;
        record[2] = 3;
        record[3] = (byte)(handshake.Length >> 8);
        record[4] = (byte)handshake.Length;
        handshake.CopyTo(record, 5);
        return record;
    }

    private static byte[] UdpPacket(IPAddress src, int srcPort, IPAddress dst, int dstPort)
    {
        var packet = new byte[28];
        packet[0] = 0x45;
        packet[2] = 0;
        packet[3] = 28;
        packet[8] = 64;
        packet[9] = 17;
        src.GetAddressBytes().CopyTo(packet, 12);
        dst.GetAddressBytes().CopyTo(packet, 16);
        packet[20] = (byte)(srcPort >> 8);
        packet[21] = (byte)srcPort;
        packet[22] = (byte)(dstPort >> 8);
        packet[23] = (byte)dstPort;
        packet[24] = 0;
        packet[25] = 8;
        return packet;
    }
}
