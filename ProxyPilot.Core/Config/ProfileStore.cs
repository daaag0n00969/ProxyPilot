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

    public static void Save(Profile profile, string? path = null)
    {
        path ??= ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(profile, JsonOptions));
    }
}
