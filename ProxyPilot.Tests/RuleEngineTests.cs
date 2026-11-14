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
    public void NvidiaApp_GoesThroughProxy()
    {
        var engine = new RuleEngine(Profile.CreateDefault());
        Assert.Equal(RuleAction.Proxy, engine.Evaluate("NVIDIA App.exe", IPAddress.Parse("8.8.8.8"), 443).Action);
        Assert.Equal(RuleAction.Proxy, engine.Evaluate("nvcontainer.exe", IPAddress.Parse("8.8.8.8"), 443).Action);
        Assert.Equal("NVIDIA App via Happ", engine.Evaluate("nvcontainer.exe", IPAddress.Parse("8.8.8.8"), 443).Rule.Name);
    }

    [Fact]
    public void VsCode_AndCodex_GoThroughProxy()
    {
        var engine = new RuleEngine(Profile.CreateDefault());
        Assert.Equal(RuleAction.Proxy, engine.Evaluate("Code.exe", IPAddress.Parse("1.1.1.1"), 443).Action);
        Assert.Equal(RuleAction.Proxy, engine.Evaluate("codex.exe", IPAddress.Parse("1.1.1.1"), 443).Action);
        Assert.Equal(RuleAction.Proxy, engine.Evaluate("ChatGPT.exe", IPAddress.Parse("1.1.1.1"), 443).Action);
        Assert.Equal("VS Code / ChatGPT via Happ", engine.Evaluate("ChatGPT.exe", IPAddress.Parse("1.1.1.1"), 443).Rule.Name);
        Assert.Equal(RuleAction.Direct, engine.Evaluate("ChatGPT.exe", IPAddress.Loopback, 10809).Action);
        Assert.Equal(RuleAction.Direct, engine.Evaluate("codex-windows-sandbox-service.exe", IPAddress.Parse("1.1.1.1"), 443).Action);
    }

    [Fact]
    public void MigrateVsCodeChatGpt_AddsDesktopAppOnce()
    {
        var profile = Profile.CreateDefault();
        var rule = profile.Rules.Single(r => r.Name == "VS Code / ChatGPT via Happ");
        rule.Applications.RemoveAll(a => a.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase));
        ProfileStore.MigrateVsCodeChatGpt(profile);
        ProfileStore.MigrateVsCodeChatGpt(profile);
        Assert.Equal(1, rule.Applications.Count(a => a.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("Default", profile.Rules[^1].Name);
        Assert.DoesNotContain(profile.Rules.Where(r => r.Name != rule.Name).SelectMany(r => r.Applications),
            a => a.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NeverwinterPatch_GoesThroughProxy_GamePortsStayDirect()
    {
        var engine = new RuleEngine(Profile.CreateDefault());
        var download = engine.Evaluate("Neverwinter.exe", IPAddress.Parse("2.22.50.108"), 80);
        Assert.Equal(RuleAction.Proxy, download.Action);
        Assert.Equal("Neverwinter patch via Happ", download.Rule.Name);
        Assert.Equal(RuleAction.Direct, engine.Evaluate("Neverwinter.exe", IPAddress.Parse("208.95.184.162"), 7004).Action);
        Assert.Equal(RuleAction.Direct, engine.Evaluate("notepad.exe", IPAddress.Parse("2.22.50.108"), 80).Action);
    }

    [Fact]
    public void MigrateNeverwinterPatch_InsertsRuleOnce()
    {
        var profile = Profile.CreateDefault();
        profile.Rules.RemoveAll(r => r.Applications.Any(a => a.Equals("Neverwinter.exe", StringComparison.OrdinalIgnoreCase)));
        ProfileStore.MigrateNeverwinterPatch(profile);
        var rule = profile.Rules.Single(r => r.Applications.Any(a => a.Equals("Neverwinter.exe", StringComparison.OrdinalIgnoreCase)));
        rule.Targets.Clear();
        rule.Targets.Add("*.nwhttppatch.crypticstudios.com");
        rule.Applications.RemoveAll(a => a.Equals("GameClient.exe", StringComparison.OrdinalIgnoreCase));
        ProfileStore.MigrateNeverwinterPatch(profile);
        Assert.Equal(["*:80"], rule.Targets);
        Assert.Contains(rule.Applications, a => a.Equals("GameClient.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Default", profile.Rules[^1].Name);
    }

    [Fact]
    public void Bitcraft_GoesThroughProxy_OtherGamesStayDirect()
    {
        var engine = new RuleEngine(Profile.CreateDefault());
        var game = engine.Evaluate("BitCraft.exe", IPAddress.Parse("125.253.87.85"), 443);
        Assert.Equal(RuleAction.Proxy, game.Action);
        Assert.Equal("Bitcraft via Happ", game.Rule.Name);
        Assert.Equal(RuleAction.Direct, engine.Evaluate("BitCraft.exe", IPAddress.Loopback, 10809).Action);
        Assert.Equal(RuleAction.Direct, engine.Evaluate("notepad.exe", IPAddress.Parse("125.253.87.85"), 443).Action);
    }

    [Fact]
    public void MigrateBitcraft_InsertsRuleOnce()
    {
        var profile = Profile.CreateDefault();
        profile.Rules.RemoveAll(r => r.Applications.Any(a => a.Equals("BitCraft.exe", StringComparison.OrdinalIgnoreCase)));
        ProfileStore.MigrateBitcraft(profile);
        ProfileStore.MigrateBitcraft(profile);
        Assert.Equal(1, profile.Rules.Count(r => r.Name == "Bitcraft via Happ"));
        Assert.Equal("Default", profile.Rules[^1].Name);
    }

    [Fact]
    public void Ffxiv_GoesThroughProxy_OtherGamesStayDirect()
    {
        var engine = new RuleEngine(Profile.CreateDefault());
        var launcher = engine.Evaluate("ffxivlauncher64.exe", IPAddress.Parse("184.24.77.141"), 443);
        var game = engine.Evaluate("ffxiv_dx11.exe", IPAddress.Parse("204.2.229.10"), 54992);
        Assert.Equal(RuleAction.Proxy, launcher.Action);
        Assert.Equal("Final Fantasy XIV via Happ", launcher.Rule.Name);
        Assert.Equal(RuleAction.Proxy, game.Action);
        Assert.Equal(RuleAction.Direct, engine.Evaluate("notepad.exe", IPAddress.Parse("184.24.77.141"), 443).Action);
        Assert.Equal(RuleAction.Direct, engine.Evaluate("Neverwinter.exe", IPAddress.Parse("184.24.77.141"), 443).Action);
        Assert.Equal(RuleAction.Proxy, engine.Evaluate("GameClient.exe", IPAddress.Parse("92.122.166.194"), 80).Action);
        Assert.Equal(RuleAction.Direct, engine.Evaluate("GameClient.exe", IPAddress.Parse("208.95.184.122"), 7255).Action);
    }

    [Fact]
    public void MigrateFfxiv_InsertsRuleOnce()
    {
        var profile = Profile.CreateDefault();
        profile.Rules.RemoveAll(r => r.Applications.Any(a => a.Equals("ffxiv_dx11.exe", StringComparison.OrdinalIgnoreCase)));
        ProfileStore.MigrateFfxiv(profile);
        ProfileStore.MigrateFfxiv(profile);
        Assert.Equal(1, profile.Rules.Count(r => r.Name == "Final Fantasy XIV via Happ"));
        Assert.Equal("Default", profile.Rules[^1].Name);
    }

    [Fact]
    public void Fallout76_GoesThroughProxy_OtherGamesStayDirect()
    {
        var engine = new RuleEngine(Profile.CreateDefault());
        var fallout = engine.Evaluate("Fallout76.exe", IPAddress.Parse("18.119.193.126"), 443);
        Assert.Equal(RuleAction.Proxy, fallout.Action);
        Assert.Equal("Fallout 76 via Happ", fallout.Rule.Name);
        Assert.Equal(RuleAction.Direct, engine.Evaluate("notepad.exe", IPAddress.Parse("18.119.193.126"), 443).Action);
        Assert.Equal(RuleAction.Proxy, engine.Evaluate("steam.exe", IPAddress.Parse("1.1.1.1"), 443).Action);
    }

    [Fact]
    public void MigrateFallout76_InsertsRuleOnce()
    {
        var profile = Profile.CreateDefault();
        profile.Rules.RemoveAll(r => r.Applications.Any(a => a.Equals("Fallout76.exe", StringComparison.OrdinalIgnoreCase)));
        ProfileStore.MigrateFallout76(profile);
        ProfileStore.MigrateFallout76(profile);
        Assert.Equal(1, profile.Rules.Count(r => r.Name == "Fallout 76 via Happ"));
        Assert.Equal("Default", profile.Rules[^1].Name);
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
