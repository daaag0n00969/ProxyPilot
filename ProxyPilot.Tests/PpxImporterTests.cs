using ProxyPilot.Core.Config;
using Xunit;

namespace ProxyPilot.Tests;

public class PpxImporterTests
{
    [Fact]
    public void SplitList_KeepsQuotedNames()
    {
        var list = PpxImporter.SplitList("steam.exe; \"grok bot.exe\"; Happ.exe");
        Assert.Equal(["steam.exe", "grok bot.exe", "Happ.exe"], list);
    }

    [Fact]
    public void Import_UserHappProfile()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Proxifier-Steam-Happ.ppx");
        if (!File.Exists(path))
            return;

        var profile = PpxImporter.Import(path);
        Assert.True(profile.DnsViaProxy);
        Assert.Contains(profile.Proxies, p => p.Host == "127.0.0.1" && p.Port == 10808);
        Assert.Contains(profile.Rules, r => r.Name == "Steam via Happ" && r.Action == RuleAction.Proxy);
        Assert.Contains(profile.Rules, r => r.Applications.Any(a => a.Equals("grok bot.exe", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(RuleAction.Direct, profile.Rules.Last().Action);
    }

    [Fact]
    public void Import_MinimalXml()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ProxifierProfile>
              <Options>
                <Resolve><ViaProxy enabled="true" /></Resolve>
                <Udp mode="mode_bypass" />
              </Options>
              <ProxyList>
                <Proxy id="100" type="SOCKS5">
                  <Port>1080</Port>
                  <Address>127.0.0.1</Address>
                </Proxy>
              </ProxyList>
              <RuleList>
                <Rule enabled="true">
                  <Action type="Proxy">100</Action>
                  <Applications>game.exe</Applications>
                  <Name>Game</Name>
                </Rule>
                <Rule enabled="true">
                  <Action type="Direct" />
                  <Name>Default</Name>
                </Rule>
              </RuleList>
            </ProxifierProfile>
            """;
        var file = Path.GetTempFileName();
        File.WriteAllText(file, xml);
        try
        {
            var profile = PpxImporter.Import(file);
            Assert.Single(profile.Proxies);
            Assert.Equal(ProxyType.Socks5, profile.Proxies[0].Type);
            Assert.Contains(profile.Rules, r => r.Name == "Game" && r.ProxyId == "100");
        }
        finally
        {
            File.Delete(file);
        }
    }
}
