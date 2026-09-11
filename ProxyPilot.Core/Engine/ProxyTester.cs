using System.Net;
using ProxyPilot.Core.Config;

namespace ProxyPilot.Core.Engine;

public static class ProxyTester
{
    public static async Task<string> TestAsync(IReadOnlyList<ProxyServer> hops, CancellationToken cancellationToken = default)
    {
        if (hops.Count == 0)
            return "Нет прокси для проверки.";

        var dest = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 443);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var client = await ProxyClient.ConnectAsync(hops, dest, cancellationToken, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            sw.Stop();
            return $"OK: {string.Join(" -> ", hops.Select(h => h.Display))} за {sw.ElapsedMilliseconds} мс (CONNECT 1.1.1.1:443).";
        }
        catch (Exception ex)
        {
            sw.Stop();
            return $"Ошибка ({sw.ElapsedMilliseconds} мс): {ex.Message}";
        }
    }
}
