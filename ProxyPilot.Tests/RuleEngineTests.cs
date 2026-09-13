using System.Net;
using ProxyPilot.Core.Config;
using ProxyPilot.Core.Engine;
using Xunit;

namespace ProxyPilot.Tests;

public class RuleEngineTests
{
    [Fact]
    public void DefaultProfile_RoutesSteamThroughHapp()
    {
        var engine = new RuleEngine(Profile.CreateDefault());
        var decision = engine.Evaluate("steam.exe", IPAddress.Parse("1.1.1.1"), 443);
        Assert.Equal(RuleAction.Proxy, decision.Action);
        Assert.Equal("100", decision.Proxy!.Id);
        Assert.Equal("Steam via Happ", decision.Rule.Name);
    }

    [Fact]
    public void DefaultProfile_KeepsHappDirect()
    {
        var engine = new RuleEngine(Profile.CreateDefault());
        var decision = engine.Evaluate("Happ.exe", IPAddress.Parse("1.1.1.1"), 443);
        Assert.Equal(RuleAction.Direct, decision.Action);
        Assert.Equal("Keep TUI and proxy stack direct", decision.Rule.Name);
    }

    [Fact]
    public void Localhost_IsDirectEvenForSteam()
    {
        var engine = new RuleEngine(Profile.CreateDefault());
        var decision = engine.Evaluate("steam.exe", IPAddress.Loopback, 27060);
        Assert.Equal(RuleAction.Direct, decision.Action);
        Assert.Equal("Localhost", decision.Rule.Name);
    }

    [Fact]
    public void HappFakeIp_ForSteamContent_GoesThroughProxy()
    {
        var engine = new RuleEngine(Profile.CreateDefault());
        var contentFake = engine.Evaluate("steam.exe", IPAddress.Parse("127.147.0.11"), 80);
        var cloudFake = engine.Evaluate("steam.exe", IPAddress.Parse("127.229.0.132"), 443);
        Assert.Equal(RuleAction.Proxy, contentFake.Action);
        Assert.Equal(RuleAction.Proxy, cloudFake.Action);
    }

    [Fact]
    public void GrokBot_QuotedName_Matches()
    {
        var engine = new RuleEngine(Profile.CreateDefault());
        var decision = engine.Evaluate("grok bot.exe", IPAddress.Parse("8.8.8.8"), 443);
        Assert.Equal(RuleAction.Proxy, decision.Action);
        Assert.Equal("Grok Bot via Happ", decision.Rule.Name);
    }

    [Fact]
    public void ProcessWildcard_Matches()
    {
        Assert.True(RuleEngine.ProcessMatches(["steam*"], "steamwebhelper.exe"));
        Assert.True(RuleEngine.ProcessMatches(["*.exe"], "chrome.exe"));
        Assert.False(RuleEngine.ProcessMatches(["steam.exe"], "chrome.exe"));
    }

    [Fact]
    public void Cidr_MatchesPrivateRange()
    {
        Assert.True(RuleEngine.CidrContains("10.0.0.0/8", IPAddress.Parse("10.1.2.3")));
        Assert.False(RuleEngine.CidrContains("10.0.0.0/8", IPAddress.Parse("11.0.0.1")));
        Assert.True(RuleEngine.CidrContains("192.168.0.0/16", IPAddress.Parse("192.168.10.20")));
    }

    [Fact]
    public void PortRange_Parses()
    {
        Assert.True(RuleEngine.TryParsePortRange("443", out var a, out var b));
        Assert.Equal(443, a);
        Assert.Equal(443, b);
        Assert.True(RuleEngine.TryParsePortRange("27015-27050", out a, out b));
        Assert.Equal(27015, a);
        Assert.Equal(27050, b);
    }

    [Fact]
    public void TargetPort_RestrictsMatch()
    {
        var profile = Profile.CreateDefault();
        profile.Rules.Insert(0, new ProfileRule
        {
            Name = "HTTPS only",
            Action = RuleAction.Block,
            Applications = ["steam.exe"],
            Targets = ["*:443"]
        });
        var engine = new RuleEngine(profile);
        Assert.Equal(RuleAction.Block, engine.Evaluate("steam.exe", IPAddress.Parse("1.1.1.1"), 443).Action);
        Assert.Equal(RuleAction.Proxy, engine.Evaluate("steam.exe", IPAddress.Parse("1.1.1.1"), 80).Action);
    }
}
