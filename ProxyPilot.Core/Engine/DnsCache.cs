using System.Collections.Concurrent;
using System.Net;

namespace ProxyPilot.Core.Engine;

public static class DnsCache
{
    private static readonly ConcurrentDictionary<string, string> IpToHost = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, IPAddress[]> NameToIps = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> CloudDirectIps = new(StringComparer.OrdinalIgnoreCase);

    public static void SeedKnownCloud()
    {
        RememberAnswers("steamcloudsweden.blob.core.windows.net",
        [
            IPAddress.Parse("20.60.253.225"),
            IPAddress.Parse("20.209.216.97"),
            IPAddress.Parse("20.60.253.129")
        ]);
    }

    public static void Remember(IPAddress ip, string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.None))
            return;
        hostname = hostname.Trim().TrimEnd('.');
        IpToHost[ip.ToString()] = hostname;
        if (IsCloudStorageHost(hostname))
            CloudDirectIps[ip.ToString()] = 1;
    }

    public static string? Lookup(IPAddress ip) =>
        IpToHost.TryGetValue(ip.ToString(), out var host) ? host : null;

    public static bool TryGet(string hostname, out IPAddress[] ips) =>
        NameToIps.TryGetValue(hostname.Trim().TrimEnd('.'), out ips!);

    public static bool IsCloudDirect(IPAddress ip) =>
        CloudDirectIps.ContainsKey(ip.ToString());

    public static bool IsCloudStorageHost(string? qname)
    {
        if (string.IsNullOrWhiteSpace(qname))
            return false;
        return qname.Contains("steamcloud", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("blob.core.windows.net", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("steamusercontent.com", StringComparison.OrdinalIgnoreCase)
               || (qname.Contains("amazonaws.com", StringComparison.OrdinalIgnoreCase) && qname.Contains("steam", StringComparison.OrdinalIgnoreCase))
               || (qname.Contains("storage.googleapis.com", StringComparison.OrdinalIgnoreCase) && qname.Contains("steam", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSteamNetworkHost(string? qname)
    {
        if (string.IsNullOrWhiteSpace(qname))
            return false;
        return IsCloudStorageHost(qname)
               || qname.Contains("steampowered.com", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("steamcontent.com", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("steamserver.net", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("steamstatic.com", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("steamcommunity.com", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("steamusercontent.com", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("valvesoftware.com", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("steam-chat.com", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("steampipe", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("steamconnecttest.com", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNvidiaHost(string? qname)
    {
        if (string.IsNullOrWhiteSpace(qname))
            return false;
        return qname.Contains("nvidia.com", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("geforce.com", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("nvidiagrid.net", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOpenAiHost(string? qname)
    {
        if (string.IsNullOrWhiteSpace(qname))
            return false;
        return qname.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("openai.com", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("oaistatic.com", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("oaiusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsConnectivityHost(string? qname)
    {
        if (string.IsNullOrWhiteSpace(qname))
            return false;
        return qname.Contains("msftconnecttest.com", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("msftncsi.com", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCloudHost(string? qname) =>
        IsSteamNetworkHost(qname) || IsOpenAiHost(qname) || IsNvidiaHost(qname) || IsConnectivityHost(qname);

    public static bool IsNoisyHost(string? qname)
    {
        if (string.IsNullOrWhiteSpace(qname))
            return true;
        return qname.Contains("kaspersky", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("wpad", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("nvidia", StringComparison.OrdinalIgnoreCase)
               || qname.Contains("msftncsi", StringComparison.OrdinalIgnoreCase)
               || qname.Equals("lan", StringComparison.OrdinalIgnoreCase);
    }

    public static void ObserveResponse(string? qname, byte[] response)
    {
        if (string.IsNullOrWhiteSpace(qname))
            return;
        var ips = DnsOverProxy.ReadARecords(response);
        if (ips.Count > 0)
            NameToIps[qname.Trim().TrimEnd('.')] = ips.ToArray();
        foreach (var ip in ips)
            Remember(ip, qname);
    }

    public static void RememberAnswers(string hostname, IReadOnlyList<IPAddress> ips)
    {
        hostname = hostname.Trim().TrimEnd('.');
        if (ips.Count > 0)
            NameToIps[hostname] = ips.ToArray();
        foreach (var ip in ips)
            Remember(ip, hostname);
    }
}
