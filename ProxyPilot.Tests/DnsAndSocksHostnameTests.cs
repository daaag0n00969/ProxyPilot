using System.Net;
using System.Net.Sockets;
using ProxyPilot.Core.Config;
using ProxyPilot.Core.Engine;
using Xunit;

namespace ProxyPilot.Tests;

public class DnsAndSocksHostnameTests
{
    [Fact]
    public void EmptyAnswer_ClearsCounts_KeepsQuestion()
    {
        var query = BuildQuery("steamcloud.steampowered.com", 28);
        var response = DnsOverProxy.EmptyAnswer(query);
        Assert.Equal(0, (response[6] << 8) | response[7]);
        Assert.Equal("steamcloud.steampowered.com", DnsOverProxy.FirstQuestionName(response));
        Assert.Equal(28, DnsOverProxy.QuestionType(query));
    }

    [Fact]
    public void ReadARecords_ParsesIPv4()
    {
        var qname = "example.com";
        var query = BuildQuery(qname, 1);
        var ip = IPAddress.Parse("93.184.216.34");
        var response = BuildAResponse(query, ip);
        var records = DnsOverProxy.ReadARecords(response);
        Assert.Single(records);
        Assert.Equal(ip, records[0]);
    }

    [Fact]
    public void SeedKnownCloud_IsCacheHit()
    {
        DnsCache.SeedKnownCloud();
        Assert.True(DnsCache.TryGet("steamcloudsweden.blob.core.windows.net", out var ips));
        Assert.Contains(ips, i => i.ToString() == "20.60.253.225");
        Assert.True(DnsCache.IsCloudDirect(IPAddress.Parse("20.60.253.225")));
    }

    [Fact]
    public void DnsCache_RemembersLookup()
    {
        var ip = IPAddress.Parse("203.0.113.9");
        DnsCache.Remember(ip, "steamcloudsweden.blob.core.windows.net");
        Assert.Equal("steamcloudsweden.blob.core.windows.net", DnsCache.Lookup(ip));
        Assert.True(DnsCache.IsCloudDirect(ip));
    }

    [Fact]
    public void BuildAResponse_RoundTripsRecords()
    {
        var query = DnsOverProxy.BuildQuery("steamcloudsweden.blob.core.windows.net", 1);
        var ips = new[] { IPAddress.Parse("20.60.253.225"), IPAddress.Parse("20.209.216.97") };
        var response = DnsOverProxy.BuildAResponse(query, ips);
        Assert.Equal(["20.60.253.225", "20.209.216.97"], DnsOverProxy.ReadARecords(response).Select(i => i.ToString()));
        Assert.Equal("steamcloudsweden.blob.core.windows.net", DnsOverProxy.FirstQuestionName(response));
    }

    [Fact]
    public void WildcardTarget_MatchesCachedHostname()
    {
        var ip = IPAddress.Parse("203.0.113.10");
        DnsCache.Remember(ip, "foo.steamusercontent.com");
        var profile = Profile.CreateDefault();
        profile.Rules.Insert(0, new ProfileRule
        {
            Name = "Cloud names",
            Action = RuleAction.Proxy,
            ProxyId = "100",
            Applications = ["steam.exe"],
            Targets = ["*.steamusercontent.com"]
        });
        var engine = new RuleEngine(profile);
        var decision = engine.Evaluate("steam.exe", ip, 443);
        Assert.Equal("Cloud names", decision.Rule.Name);
    }

    [Fact]
    public async Task Socks5_SendsDomainAtyp()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = Task.Run(async () =>
        {
            using var incoming = await listener.AcceptTcpClientAsync(cts.Token);
            var stream = incoming.GetStream();
            var hello = new byte[3];
            await ProxyClient.ReadExactAsync(stream, hello, cts.Token);
            await stream.WriteAsync(new byte[] { 5, 0 }, cts.Token);
            var head = new byte[5];
            await ProxyClient.ReadExactAsync(stream, head, cts.Token);
            Assert.Equal(new byte[] { 5, 1, 0, 3 }, head[..4]);
            var host = new byte[head[4] + 2];
            await ProxyClient.ReadExactAsync(stream, host, cts.Token);
            Assert.Equal("steamusercontent.com", System.Text.Encoding.ASCII.GetString(host, 0, head[4]));
            await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 0, 0, 0, 0, 0, 0 }, cts.Token);
        });

        var hops = new List<ProxyServer> { new() { Type = ProxyType.Socks5, Host = "127.0.0.1", Port = port } };
        using var client = await ProxyClient.ConnectAsync(
            hops,
            new IPEndPoint(IPAddress.Parse("1.2.3.4"), 443),
            cts.Token,
            hostname: "steamusercontent.com");
        await server;
        listener.Stop();
    }

    private static byte[] BuildQuery(string name, ushort type)
    {
        var labels = name.Split('.').SelectMany(l => new[] { (byte)l.Length }.Concat(System.Text.Encoding.ASCII.GetBytes(l))).Concat(new byte[] { 0 }).ToArray();
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

    private static byte[] BuildAResponse(byte[] query, IPAddress ip)
    {
        var extra = 16;
        var response = new byte[query.Length + extra];
        Buffer.BlockCopy(query, 0, response, 0, query.Length);
        response[2] = 0x81;
        response[3] = 0x80;
        response[7] = 1;
        var o = query.Length;
        response[o] = 0xC0;
        response[o + 1] = 0x0C;
        response[o + 3] = 1;
        response[o + 5] = 1;
        response[o + 10] = 0;
        response[o + 11] = 4;
        ip.GetAddressBytes().CopyTo(response, o + 12);
        return response;
    }
}
