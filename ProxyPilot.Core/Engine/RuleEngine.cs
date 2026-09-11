using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using ProxyPilot.Core.Config;

namespace ProxyPilot.Core.Engine;

public sealed class RuleEngine
{
    private readonly Profile _profile;
    private readonly Dictionary<string, ProxyServer> _proxies;
    private readonly Dictionary<string, ProxyChain> _chains;
    private readonly HashSet<IPAddress> _localhostIps;
    private readonly Dictionary<string, HashSet<IPAddress>> _hostIps;

    public RuleEngine(Profile profile)
    {
        _profile = profile;
        _proxies = profile.Proxies.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        _chains = profile.Chains.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        _localhostIps = CollectLocalAddresses();
        _hostIps = new Dictionary<string, HashSet<IPAddress>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in profile.Rules)
        {
            foreach (var target in rule.Targets)
            {
                var host = HostPart(target.Trim());
                if (host.Length == 0 || host is "*" or "any" || host.Contains('*') || host.Contains('?') || host.Contains('/'))
                    continue;
                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "::1")
                    continue;
                if (host.Equals("%ComputerName%", StringComparison.OrdinalIgnoreCase))
                    host = Environment.MachineName;
                if (IPAddress.TryParse(host, out _))
                    continue;
                if (_hostIps.ContainsKey(host))
                    continue;
                try
                {
                    _hostIps[host] = Dns.GetHostAddresses(host).ToHashSet();
                }
                catch
                {
                    _hostIps[host] = [];
                }
            }
        }
    }

    private static string HostPart(string pattern)
    {
        var colon = pattern.LastIndexOf(':');
        if (colon > 0 && !pattern.Contains('/') && TryParsePortRange(pattern[(colon + 1)..], out _, out _))
            return pattern[..colon];
        return pattern;
    }

    public RuleDecision Evaluate(string processFileName, IPAddress destination, int port)
    {
        foreach (var rule in _profile.Rules.Where(r => r.Enabled))
        {
            if (!ProcessMatches(rule.Applications, processFileName))
                continue;
            if (!TargetMatches(rule.Targets, destination, port, DnsCache.Lookup(destination)))
                continue;

            if (rule.Action != RuleAction.Proxy)
                return new RuleDecision(rule, rule.Action, null, []);

            var hops = ResolveHops(rule.ProxyId);
            var first = hops.FirstOrDefault();
            return new RuleDecision(rule, RuleAction.Proxy, first, hops);
        }

        var fallback = new ProfileRule { Name = "Default", Action = RuleAction.Direct };
        return new RuleDecision(fallback, RuleAction.Direct, null, []);
    }

    public bool ProcessHasProxyRule(string processFileName)
    {
        return _profile.Rules.Any(r =>
            r.Enabled &&
            r.Action == RuleAction.Proxy &&
            ProcessMatches(r.Applications, processFileName));
    }

    public bool IsProxyEndpoint(IPAddress destination, int port)
    {
        foreach (var proxy in _profile.Proxies)
        {
            if (proxy.Port != port)
                continue;
            if (IPAddress.TryParse(proxy.Host, out var ip) && ip.Equals(destination))
                return true;
            if (destination.Equals(IPAddress.Loopback) &&
                (proxy.Host is "127.0.0.1" or "localhost"))
                return true;
        }
        return false;
    }

    public IReadOnlyList<ProxyServer> ResolveHops(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return _profile.Proxies.Take(1).ToList();
        if (_proxies.TryGetValue(id, out var proxy))
            return [proxy];
        if (_chains.TryGetValue(id, out var chain))
            return chain.ProxyIds.Select(pid => _proxies.GetValueOrDefault(pid)).Where(p => p != null).Cast<ProxyServer>().ToList();
        return [];
    }

    public static bool ProcessMatches(IReadOnlyList<string> patterns, string processFileName)
    {
        if (patterns.Count == 0)
            return true;
        var fileName = Path.GetFileName(processFileName);
        foreach (var raw in patterns)
        {
            var pattern = raw.Trim().Trim('"');
            if (pattern.Length == 0)
                continue;
            if (Wildcard(pattern, fileName) || Wildcard(pattern, processFileName))
                return true;
        }
        return false;
    }

    public bool TargetMatches(IReadOnlyList<string> patterns, IPAddress destination, int port, string? hostname = null)
    {
        if (patterns.Count == 0)
            return true;
        foreach (var raw in patterns)
        {
            if (MatchSingleTarget(raw.Trim(), destination, port, hostname))
                return true;
        }
        return false;
    }

    public static bool Wildcard(string pattern, string value)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private bool MatchSingleTarget(string pattern, IPAddress destination, int port, string? hostname = null)
    {
        if (pattern.Length == 0)
            return false;

        var hostPart = pattern;
        int? minPort = null, maxPort = null;
        var colon = pattern.LastIndexOf(':');
        if (colon > 0 && !pattern.Contains('/'))
        {
            var after = pattern[(colon + 1)..];
            if (TryParsePortRange(after, out var a, out var b))
            {
                hostPart = pattern[..colon];
                minPort = a;
                maxPort = b;
            }
        }

        if (minPort is not null && (port < minPort || port > maxPort))
            return false;

        if (hostPart is "*" or "any")
            return true;

        if (hostPart.Equals("%ComputerName%", StringComparison.OrdinalIgnoreCase))
            hostPart = Environment.MachineName;

        if (hostPart.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            hostPart is "127.0.0.1" or "::1")
        {
            return destination.Equals(IPAddress.Loopback) ||
                   destination.Equals(IPAddress.IPv6Loopback) ||
                   _localhostIps.Contains(destination);
        }

        if (hostPart.Contains('/'))
            return CidrContains(hostPart, destination);

        if (IPAddress.TryParse(hostPart, out var ip))
            return ip.Equals(destination);

        hostname ??= DnsCache.Lookup(destination);

        if (hostPart.Contains('*') || hostPart.Contains('?'))
            return hostname != null && Wildcard(hostPart, hostname);

        if (hostname != null && hostname.Equals(hostPart, StringComparison.OrdinalIgnoreCase))
            return true;

        return _hostIps.TryGetValue(hostPart, out var ips) && ips.Contains(destination);
    }

    public static bool CidrContains(string cidr, IPAddress address)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var network) || !int.TryParse(parts[1], out var prefix))
            return false;
        if (address.AddressFamily != AddressFamily.InterNetwork || network.AddressFamily != AddressFamily.InterNetwork)
            return false;
        if (prefix is < 0 or > 32)
            return false;

        var addrBytes = address.GetAddressBytes();
        var netBytes = network.GetAddressBytes();
        var addr = (uint)((addrBytes[0] << 24) | (addrBytes[1] << 16) | (addrBytes[2] << 8) | addrBytes[3]);
        var net = (uint)((netBytes[0] << 24) | (netBytes[1] << 16) | (netBytes[2] << 8) | netBytes[3]);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        return (addr & mask) == (net & mask);
    }

    public static bool TryParsePortRange(string text, out int min, out int max)
    {
        min = max = 0;
        var dash = text.IndexOf('-');
        if (dash < 0)
        {
            if (!int.TryParse(text, out min) || min is < 0 or > 65535)
                return false;
            max = min;
            return true;
        }

        if (!int.TryParse(text[..dash], out min) || !int.TryParse(text[(dash + 1)..], out max))
            return false;
        return min is >= 0 and <= 65535 && max is >= 0 and <= 65535 && min <= max;
    }

    private static HashSet<IPAddress> CollectLocalAddresses()
    {
        var set = new HashSet<IPAddress> { IPAddress.Loopback, IPAddress.IPv6Loopback };
        try
        {
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
                set.Add(ip);
            foreach (var ip in Dns.GetHostAddresses(Environment.MachineName))
                set.Add(ip);
        }
        catch
        {
            // Local hostname resolution can fail offline; loopback still matches.
        }
        return set;
    }
}
