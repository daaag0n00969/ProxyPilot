using System.Net;
using System.Text.Json.Serialization;

namespace ProxyPilot.Core.Config;

public enum ProxyType
{
    Socks5,
    Socks4,
    HttpConnect
}

public enum RuleAction
{
    Direct,
    Proxy,
    Block
}

public enum UdpMode
{
    Bypass,
    Proxy
}

public sealed class Profile
{
    public bool DnsViaProxy { get; set; } = true;
    public UdpMode UdpMode { get; set; } = UdpMode.Bypass;
    public bool LoopDetection { get; set; } = true;
    public bool AutoStart { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public string DnsServer { get; set; } = "1.1.1.1";
    public int RelayPort { get; set; } = 34010;
    public List<ProxyServer> Proxies { get; set; } = [];
    public List<ProxyChain> Chains { get; set; } = [];
    public List<ProfileRule> Rules { get; set; } = [];

    public static Profile CreateDefault()
    {
        var happ = new ProxyServer
        {
            Id = "100",
            Name = "Happ SOCKS5",
            Type = ProxyType.Socks5,
            Host = "127.0.0.1",
            Port = 10808
        };

        return new Profile
        {
            DnsViaProxy = true,
            UdpMode = UdpMode.Bypass,
            LoopDetection = true,
            Proxies = [happ],
            Rules =
            [
                new ProfileRule
                {
                    Name = "Localhost",
                    Action = RuleAction.Direct,
                    Targets = ["localhost", "127.0.0.1", "%ComputerName%", "::1"]
                },
                new ProfileRule
                {
                    Name = "Keep TUI and proxy stack direct",
                    Action = RuleAction.Direct,
                    Applications = ["grok.exe", "xray.exe", "Happ.exe", "happd.exe", "Proxifier.exe", "ProxyPilot.exe"]
                },
                new ProfileRule
                {
                    Name = "Grok Bot via Happ",
                    Action = RuleAction.Proxy,
                    ProxyId = happ.Id,
                    Applications = ["grok bot.exe"]
                },
                new ProfileRule
                {
                    Name = "Steam via Happ",
                    Action = RuleAction.Proxy,
                    ProxyId = happ.Id,
                    Applications = ["steam.exe", "steamwebhelper.exe", "steamservice.exe", "GameOverlayUI.exe"]
                },
                new ProfileRule
                {
                    Name = "NVIDIA App via Happ",
                    Action = RuleAction.Proxy,
                    ProxyId = happ.Id,
                    Applications =
                    [
                        "NVIDIA App.exe",
                        "NVIDIA Overlay.exe",
                        "nvcontainer.exe",
                        "NVIDIA Share.exe",
                        "NVIDIA GeForce Experience.exe",
                        "NVIDIA Web Helper.exe"
                    ]
                },
                new ProfileRule
                {
                    Name = "VS Code / ChatGPT via Happ",
                    Action = RuleAction.Proxy,
                    ProxyId = happ.Id,
                    Applications =
                    [
                        "Code.exe",
                        "Code - Insiders.exe",
                        "Cursor.exe",
                        "codex.exe",
                        "codex-code-mode-host.exe",
                        "codex-command-runner.exe"
                    ]
                },
                new ProfileRule
                {
                    Name = "Windows online check via Happ",
                    Action = RuleAction.Proxy,
                    ProxyId = happ.Id,
                    Targets =
                    [
                        "www.msftconnecttest.com",
                        "ipv6.msftconnecttest.com",
                        "dns.msftncsi.com",
                        "www.msftncsi.com"
                    ]
                },
                new ProfileRule
                {
                    Name = "Default",
                    Action = RuleAction.Direct
                }
            ]
        };
    }
}

public sealed class ProxyServer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "SOCKS5";
    public ProxyType Type { get; set; } = ProxyType.Socks5;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1080;
    public string? Username { get; set; }
    public string? Password { get; set; }

    [JsonIgnore]
    public string Display => string.IsNullOrWhiteSpace(Name) ? $"{Type} {Host}:{Port}" : $"{Name} ({Host}:{Port})";

    public IPEndPoint EndPoint() => new(ParseHost(Host), Port);

    public static IPAddress ParseHost(string host)
    {
        if (IPAddress.TryParse(host, out var ip))
            return ip;
        var addresses = Dns.GetHostAddresses(host);
        var v4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        return v4 ?? addresses[0];
    }
}

public sealed class ProxyChain
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Chain";
    public List<string> ProxyIds { get; set; } = [];
}

public sealed class ProfileRule
{
    public string Name { get; set; } = "Rule";
    public bool Enabled { get; set; } = true;
    public List<string> Applications { get; set; } = [];
    public List<string> Targets { get; set; } = [];
    public RuleAction Action { get; set; } = RuleAction.Direct;
    public string? ProxyId { get; set; }
}

public readonly record struct RuleDecision(
    ProfileRule Rule,
    RuleAction Action,
    ProxyServer? Proxy,
    IReadOnlyList<ProxyServer> Hops);
