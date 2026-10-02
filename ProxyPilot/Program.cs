using ProxyPilot.UI;

namespace ProxyPilot;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var startNow = args.Any(a => a.Equals("--start", StringComparison.OrdinalIgnoreCase));
        Application.Run(new MainForm(startNow));
    }
}
