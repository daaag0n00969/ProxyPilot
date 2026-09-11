using ProxyPilot.Core.Config;

namespace ProxyPilot.Core.Engine;

internal static class FileLog
{
    private static readonly object Gate = new();
    private static string LogPath => Path.Combine(ProfileStore.ConfigDirectory, "engine.log");

    public static void Write(string message, bool error = false)
    {
        try
        {
            Directory.CreateDirectory(ProfileStore.ConfigDirectory);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {(error ? "ERR" : "INF")} {message}{Environment.NewLine}";
            lock (Gate)
            {
                var path = LogPath;
                if (File.Exists(path) && new FileInfo(path).Length > 2_000_000)
                {
                    var bak = path + ".1";
                    File.Delete(bak);
                    File.Move(path, bak);
                }
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // logging must not break the engine
        }
    }
}
