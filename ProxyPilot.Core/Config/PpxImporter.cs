using System.Xml.Linq;

namespace ProxyPilot.Core.Config;

public static class PpxImporter
{
    public static Profile Import(string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root ?? throw new InvalidDataException("Пустой профиль Proxifier.");
        var profile = new Profile
        {
            DnsViaProxy = root.Element("Options")?.Element("Resolve")?.Element("ViaProxy")?.Attribute("enabled")?.Value == "true",
            LoopDetection = root.Element("Options")?.Element("ConnectionLoopDetection")?.Attribute("enabled")?.Value != "false",
            UdpMode = ParseUdp(root.Element("Options")?.Element("Udp")?.Attribute("mode")?.Value)
        };

        foreach (var node in root.Element("ProxyList")?.Elements("Proxy") ?? [])
        {
            var id = node.Attribute("id")?.Value ?? Guid.NewGuid().ToString("N")[..8];
            var type = (node.Attribute("type")?.Value ?? "SOCKS5").ToUpperInvariant();
            profile.Proxies.Add(new ProxyServer
            {
                Id = id,
                Name = $"{type} {node.Element("Address")?.Value}:{node.Element("Port")?.Value}",
                Type = type switch
                {
                    "SOCKS4" => ProxyType.Socks4,
                    "HTTPS" or "HTTP" => ProxyType.HttpConnect,
                    _ => ProxyType.Socks5
                },
                Host = node.Element("Address")?.Value ?? "127.0.0.1",
                Port = int.TryParse(node.Element("Port")?.Value, out var p) ? p : 1080,
                Username = node.Element("Username")?.Value,
                Password = node.Element("Password")?.Value
            });
        }

        foreach (var node in root.Element("ChainList")?.Elements("Chain") ?? [])
        {
            var id = node.Attribute("id")?.Value ?? Guid.NewGuid().ToString("N")[..8];
            var hops = node.Elements("Proxy")
                .Select(x => x.Attribute("id")?.Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToList();
            profile.Chains.Add(new ProxyChain
            {
                Id = id,
                Name = node.Element("Name")?.Value ?? "Chain",
                ProxyIds = hops
            });
        }

        foreach (var node in root.Element("RuleList")?.Elements("Rule") ?? [])
        {
            var actionNode = node.Element("Action");
            var actionType = actionNode?.Attribute("type")?.Value ?? "Direct";
            var proxyId = actionNode?.Value;
            profile.Rules.Add(new ProfileRule
            {
                Name = node.Element("Name")?.Value ?? "Rule",
                Enabled = node.Attribute("enabled")?.Value != "false",
                Applications = SplitList(node.Element("Applications")?.Value),
                Targets = SplitList(node.Element("Targets")?.Value),
                Action = actionType.ToLowerInvariant() switch
                {
                    "block" => RuleAction.Block,
                    "proxy" => RuleAction.Proxy,
                    _ => RuleAction.Direct
                },
                ProxyId = string.IsNullOrWhiteSpace(proxyId) ? null : proxyId.Trim()
            });
        }

        if (profile.Rules.All(r => !r.Name.Equals("Keep TUI and proxy stack direct", StringComparison.OrdinalIgnoreCase)
                                   && !r.Applications.Any(a => a.Equals("ProxyPilot.exe", StringComparison.OrdinalIgnoreCase))))
        {
            var insertAt = Math.Min(1, profile.Rules.Count);
            profile.Rules.Insert(insertAt, new ProfileRule
            {
                Name = "ProxyPilot self",
                Action = RuleAction.Direct,
                Applications = ["ProxyPilot.exe"]
            });
        }

        return profile;
    }

    public static List<string> SplitList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in raw)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (ch == ';' && !inQuotes)
            {
                AddToken(result, current);
                continue;
            }
            current.Append(ch);
        }
        AddToken(result, current);
        return result;
    }

    private static void AddToken(List<string> result, System.Text.StringBuilder current)
    {
        var token = current.ToString().Trim();
        current.Clear();
        if (token.Length > 0)
            result.Add(token);
    }

    private static UdpMode ParseUdp(string? mode) =>
        string.Equals(mode, "mode_proxy", StringComparison.OrdinalIgnoreCase)
            ? UdpMode.Proxy
            : UdpMode.Bypass;
}
