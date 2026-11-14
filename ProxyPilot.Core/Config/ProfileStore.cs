using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProxyPilot.Core.Config;

public static class ProfileStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProxyPilot");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "profile.json");

    public static Profile LoadOrCreate(string? preferredPpx = null)
    {
        Directory.CreateDirectory(ConfigDirectory);

        if (File.Exists(ConfigPath))
        {
            var existing = Load(ConfigPath);
            Save(existing);
            return existing;
        }

        var ppx = preferredPpx;
        if (string.IsNullOrWhiteSpace(ppx))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidate = Path.Combine(home, "Proxifier-Steam-Happ.ppx");
            if (File.Exists(candidate))
                ppx = candidate;
        }

        if (!string.IsNullOrWhiteSpace(ppx) && File.Exists(ppx))
        {
            var imported = PpxImporter.Import(ppx);
            Save(imported);
            return imported;
        }

        var profile = Profile.CreateDefault();
        Save(profile);
        return profile;
    }

    public static Profile Load(string path)
    {
        var json = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize<Profile>(json, JsonOptions) ?? Profile.CreateDefault();
        MigrateSteamHelpers(profile);
        MigrateLoopbackCidr(profile);
        MigrateVsCodeChatGpt(profile);
        MigrateNvidiaApp(profile);
        MigrateConnectivity(profile);
        MigrateFallout76(profile);
        MigrateNeverwinterPatch(profile);
        MigrateFfxiv(profile);
        MigrateBitcraft(profile);
        return profile;
    }

    internal static void MigrateSteamHelpers(Profile profile)
    {
        foreach (var rule in profile.Rules)
        {
            if (!rule.Applications.Any(a => a.Equals("steam.exe", StringComparison.OrdinalIgnoreCase)))
                continue;
            foreach (var extra in new[] { "steamservice.exe", "GameOverlayUI.exe" })
            {
                if (!rule.Applications.Any(a => a.Equals(extra, StringComparison.OrdinalIgnoreCase)))
                    rule.Applications.Add(extra);
            }
        }
    }

    internal static void MigrateLoopbackCidr(Profile profile)
    {
        var localhost = profile.Rules.FirstOrDefault(r =>
            r.Name.Equals("Localhost", StringComparison.OrdinalIgnoreCase) ||
            r.Targets.Any(t => t.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)));
        if (localhost is null)
            return;
        localhost.Targets.RemoveAll(t =>
            t.Equals("127.0.0.0/8", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("127.147.0.0/16", StringComparison.OrdinalIgnoreCase));
    }

    internal static void MigrateVsCodeChatGpt(Profile profile)
    {
        var existing = profile.Rules.FirstOrDefault(r =>
            r.Applications.Any(a => a.Equals("Code.exe", StringComparison.OrdinalIgnoreCase)));
        if (existing is not null)
        {
            if (!existing.Applications.Any(a => a.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase)))
                existing.Applications.Add("ChatGPT.exe");
            return;
        }

        var proxyId = profile.Proxies.FirstOrDefault()?.Id;
        var insertAt = Math.Max(0, profile.Rules.Count - 1);
        profile.Rules.Insert(insertAt, new ProfileRule
        {
            Name = "VS Code / ChatGPT via Happ",
            Action = RuleAction.Proxy,
            ProxyId = proxyId,
            Applications =
            [
                "Code.exe",
                "Code - Insiders.exe",
                "Cursor.exe",
                "codex.exe",
                "codex-code-mode-host.exe",
                "codex-command-runner.exe",
                "ChatGPT.exe"
            ]
        });
    }

    internal static void MigrateNvidiaApp(Profile profile)
    {
        if (profile.Rules.Any(r => r.Applications.Any(a => a.Equals("NVIDIA App.exe", StringComparison.OrdinalIgnoreCase)
                                                            || a.Equals("nvcontainer.exe", StringComparison.OrdinalIgnoreCase))))
            return;
        var proxyId = profile.Proxies.FirstOrDefault()?.Id;
        var insertAt = Math.Max(0, profile.Rules.Count - 1);
        profile.Rules.Insert(insertAt, new ProfileRule
        {
            Name = "NVIDIA App via Happ",
            Action = RuleAction.Proxy,
            ProxyId = proxyId,
            Applications =
            [
                "NVIDIA App.exe",
                "NVIDIA Overlay.exe",
                "nvcontainer.exe",
                "NVIDIA Share.exe",
                "NVIDIA GeForce Experience.exe",
                "NVIDIA Web Helper.exe"
            ]
        });
    }

    internal static void MigrateConnectivity(Profile profile)
    {
        if (profile.Rules.Any(r => r.Targets.Any(t => t.Contains("msftconnecttest", StringComparison.OrdinalIgnoreCase))))
            return;
        var proxyId = profile.Proxies.FirstOrDefault()?.Id;
        var insertAt = Math.Max(0, profile.Rules.Count - 1);
        profile.Rules.Insert(insertAt, new ProfileRule
        {
            Name = "Windows online check via Happ",
            Action = RuleAction.Proxy,
            ProxyId = proxyId,
            Targets =
            [
                "www.msftconnecttest.com",
                "ipv6.msftconnecttest.com",
                "dns.msftncsi.com",
                "www.msftncsi.com"
            ]
        });
    }

    internal static void MigrateFallout76(Profile profile)
    {
        if (profile.Rules.Any(r => r.Applications.Any(a => a.Equals("Fallout76.exe", StringComparison.OrdinalIgnoreCase))))
            return;
        var proxyId = profile.Proxies.FirstOrDefault(p => p.Type == ProxyType.Socks5)?.Id
                      ?? profile.Proxies.FirstOrDefault()?.Id;
        var insertAt = Math.Max(0, profile.Rules.Count - 1);
        profile.Rules.Insert(insertAt, new ProfileRule
        {
            Name = "Fallout 76 via Happ",
            Action = RuleAction.Proxy,
            ProxyId = proxyId,
            Applications = ["Fallout76.exe"]
        });
    }

    internal static void MigrateNeverwinterPatch(Profile profile)
    {
        var existing = profile.Rules.FirstOrDefault(r =>
            r.Applications.Any(a => a.Equals("Neverwinter.exe", StringComparison.OrdinalIgnoreCase)));
        if (existing is not null)
        {
            if (!existing.Targets.Any(t => t.Equals("*:80", StringComparison.OrdinalIgnoreCase)))
            {
                existing.Targets.Clear();
                existing.Targets.Add("*:80");
            }
            if (!existing.Applications.Any(a => a.Equals("GameClient.exe", StringComparison.OrdinalIgnoreCase)))
                existing.Applications.Add("GameClient.exe");
            return;
        }

        var proxyId = profile.Proxies.FirstOrDefault(p => p.Type == ProxyType.Socks5)?.Id
                      ?? profile.Proxies.FirstOrDefault()?.Id;
        var insertAt = Math.Max(0, profile.Rules.Count - 1);
        profile.Rules.Insert(insertAt, new ProfileRule
        {
            Name = "Neverwinter patch via Happ",
            Action = RuleAction.Proxy,
            ProxyId = proxyId,
            Applications = ["Neverwinter.exe", "GameClient.exe"],
            Targets = ["*:80"]
        });
    }

    internal static void MigrateBitcraft(Profile profile)
    {
        if (profile.Rules.Any(r => r.Applications.Any(a => a.Equals("BitCraft.exe", StringComparison.OrdinalIgnoreCase))))
            return;
        var proxyId = profile.Proxies.FirstOrDefault(p => p.Type == ProxyType.Socks5)?.Id
                      ?? profile.Proxies.FirstOrDefault()?.Id;
        var insertAt = Math.Max(0, profile.Rules.Count - 1);
        profile.Rules.Insert(insertAt, new ProfileRule
        {
            Name = "Bitcraft via Happ",
            Action = RuleAction.Proxy,
            ProxyId = proxyId,
            Applications = ["BitCraft.exe"]
        });
    }

    internal static void MigrateFfxiv(Profile profile)
    {
        if (profile.Rules.Any(r => r.Applications.Any(a =>
                a.Equals("ffxiv_dx11.exe", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("ffxivlauncher64.exe", StringComparison.OrdinalIgnoreCase))))
            return;
        var proxyId = profile.Proxies.FirstOrDefault(p => p.Type == ProxyType.Socks5)?.Id
                      ?? profile.Proxies.FirstOrDefault()?.Id;
        var insertAt = Math.Max(0, profile.Rules.Count - 1);
        profile.Rules.Insert(insertAt, new ProfileRule
        {
            Name = "Final Fantasy XIV via Happ",
            Action = RuleAction.Proxy,
            ProxyId = proxyId,
            Applications =
            [
                "ffxivlauncher64.exe",
                "ffxivlauncher.exe",
                "ffxivboot64.exe",
                "ffxivboot.exe",
                "ffxiv_dx11.exe",
                "ffxiv_dx9.exe"
            ]
        });
    }

    public static void Save(Profile profile, string? path = null)
    {
        path ??= ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(profile, JsonOptions));
    }
}
