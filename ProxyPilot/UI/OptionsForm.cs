using ProxyPilot.Core.Config;

namespace ProxyPilot.UI;

internal sealed class OptionsForm : Form
{
    public OptionsForm(Profile profile)
    {
        Text = "Параметры";
        Size = new Size(460, 320);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        var dns = new CheckBox { Text = "Резолвить имена через прокси (анти-leak DNS)", Checked = profile.DnsViaProxy, AutoSize = true };
        var loop = new CheckBox { Text = "Защита от петель (не проксировать сам прокси)", Checked = profile.LoopDetection, AutoSize = true };
        var tray = new CheckBox { Text = "Сворачивать в трей", Checked = profile.MinimizeToTray, AutoSize = true };
        var auto = new CheckBox { Text = "Запускать перехват при старте", Checked = profile.AutoStart, AutoSize = true };
        var dnsHost = new TextBox { Text = profile.DnsServer, Width = 180 };
        var ok = new Button { Text = "Сохранить", AutoSize = true };
        ok.Click += (_, _) =>
        {
            profile.DnsViaProxy = dns.Checked;
            profile.LoopDetection = loop.Checked;
            profile.MinimizeToTray = tray.Checked;
            profile.AutoStart = auto.Checked;
            profile.DnsServer = dnsHost.Text.Trim();
            DialogResult = DialogResult.OK;
            Close();
        };

        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(dns);
        panel.Controls.Add(new Label { Text = "DNS-сервер за прокси:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        panel.Controls.Add(dnsHost);
        panel.Controls.Add(loop);
        panel.Controls.Add(tray);
        panel.Controls.Add(auto);
        panel.Controls.Add(ok);
        Controls.Add(panel);
        Theme.Apply(this);
    }
}
