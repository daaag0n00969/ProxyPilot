using System.Diagnostics;
using System.Security.Principal;
using ProxyPilot.Core.Config;
using ProxyPilot.Core.Engine;

namespace ProxyPilot.UI;

internal sealed class MainForm : Form
{
    private readonly DivertEngine _engine = new();
    private readonly DataGridView _grid = new();
    private readonly NotifyIcon _tray = new();
    private readonly ToolStripStatusLabel _status = new("Остановлен");
    private readonly ToolStripStatusLabel _counts = new("");
    private readonly Dictionary<Guid, DataGridViewRow> _rows = [];
    private readonly Button _start = new();
    private Profile _profile;

    public MainForm()
    {
        _profile = ProfileStore.LoadOrCreate();
        Text = "ProxyPilot";
        Size = new Size(1100, 640);
        MinimumSize = new Size(800, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;

        var menu = BuildMenu();
        MainMenuStrip = menu;
        Controls.Add(menu);

        _start.Text = "Запустить";
        _start.Size = new Size(140, 36);
        _start.Location = new Point(12, 36);
        _start.Click += (_, _) => Toggle();
        Controls.Add(_start);

        var hint = new Label
        {
            AutoSize = true,
            Location = new Point(165, 44),
            Text = "Прозрачный прокси по процессам · SOCKS5 / SOCKS4 / HTTP CONNECT · импорт Proxifier .ppx",
            ForeColor = Theme.Muted
        };
        Controls.Add(hint);

        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _grid.Location = new Point(12, 80);
        _grid.Size = new Size(ClientSize.Width - 24, ClientSize.Height - 130);
        _grid.Columns.Add("time", "Время");
        _grid.Columns.Add("app", "Приложение");
        _grid.Columns.Add("target", "Цель");
        _grid.Columns.Add("rule", "Правило");
        _grid.Columns.Add("action", "Действие");
        _grid.Columns.Add("proxy", "Прокси");
        _grid.Columns.Add("bytes", "Трафик");
        _grid.Columns.Add("status", "Статус");
        _grid.Columns[0].FillWeight = 70;
        _grid.Columns[1].FillWeight = 90;
        _grid.Columns[2].FillWeight = 140;
        Controls.Add(_grid);

        var status = new StatusStrip { BackColor = Theme.Panel, ForeColor = Theme.Text };
        status.Items.Add(_status);
        status.Items.Add(new ToolStripStatusLabel { Spring = true });
        status.Items.Add(_counts);
        Controls.Add(status);

        _tray.Text = "ProxyPilot";
        _tray.Visible = true;
        _tray.Icon = SystemIcons.Shield;
        _tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; };
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Открыть", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; });
        trayMenu.Items.Add("Выход", null, (_, _) => { _tray.Visible = false; Close(); });
        _tray.ContextMenuStrip = trayMenu;

        _engine.ConnectionChanged += OnConnection;
        _engine.Log += OnLog;

        Theme.Apply(this);
        _start.BackColor = Theme.Proxy;
        _start.FlatAppearance.BorderColor = Theme.Proxy;

        Load += (_, _) =>
        {
            if (!IsAdmin())
                OfferElevation();
            else if (_profile.AutoStart)
                StartEngine();
            UpdateCounts();
        };
        FormClosing += (_, e) =>
        {
            if (_profile.MinimizeToTray && e.CloseReason == CloseReason.UserClosing && _tray.Visible && WindowState != FormWindowState.Minimized)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            _engine.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
        };
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && _profile.MinimizeToTray)
                Hide();
        };
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { BackColor = Theme.Panel, ForeColor = Theme.Text };
        var file = new ToolStripMenuItem("Файл");
        file.DropDownItems.Add("Импорт Proxifier (.ppx)...", null, (_, _) => ImportPpx());
        file.DropDownItems.Add("Экспорт профиля...", null, (_, _) => Export());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Выход", null, (_, _) => { _tray.Visible = false; Close(); });
        var profile = new ToolStripMenuItem("Профиль");
        profile.DropDownItems.Add("Прокси-серверы...", null, (_, _) => EditProxies());
        profile.DropDownItems.Add("Правила...", null, (_, _) => EditRules());
        profile.DropDownItems.Add("Параметры...", null, (_, _) =>
        {
            using var dlg = new OptionsForm(_profile);
            if (dlg.ShowDialog(this) == DialogResult.OK)
                ProfileStore.Save(_profile);
        });
        var actions = new ToolStripMenuItem("Действия");
        actions.DropDownItems.Add("Запустить / остановить", null, (_, _) => Toggle());
        actions.DropDownItems.Add("Проверить прокси...", null, async (_, _) => await TestProxy());
        actions.DropDownItems.Add("Очистить журнал", null, (_, _) => { _grid.Rows.Clear(); _rows.Clear(); UpdateCounts(); });
        var help = new ToolStripMenuItem("Справка");
        help.DropDownItems.Add("О программе", null, (_, _) => MessageBox.Show(this,
            "ProxyPilot — прозрачный прокси-клиент для Windows.\n\n" +
            "Как Proxifier: перехватывает TCP приложений, которые сами прокси не умеют, " +
            "и отправляет выбранные процессы в SOCKS5 / SOCKS4 / HTTP CONNECT.\n\n" +
            "Ваш профиль Happ (127.0.0.1:10808) подхватывается автоматически.\n" +
            "Нужны права администратора. UDP по умолчанию идёт напрямую.",
            "ProxyPilot", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.AddRange(new ToolStripItem[] { file, profile, actions, help });
        return menu;
    }

    private void Toggle()
    {
        if (_engine.IsRunning) StopEngine();
        else StartEngine();
    }

    private void StartEngine()
    {
        try
        {
            ProfileStore.Save(_profile);
            _engine.Start(_profile);
            _start.Text = "Остановить";
            _start.BackColor = Theme.Block;
            _status.Text = $"Работает · релей :{_engine.RelayPort}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось запустить", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopEngine()
    {
        _engine.Stop();
        _start.Text = "Запустить";
        _start.BackColor = Theme.Proxy;
        _status.Text = "Остановлен";
    }

    private void OnConnection(ConnectionEvent ev)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => OnConnection(ev));
            return;
        }

        var bytes = ev.BytesSent + ev.BytesReceived == 0 ? "" : $"{Format(ev.BytesSent)} / {Format(ev.BytesReceived)}";
        if (_rows.TryGetValue(ev.Id, out var row) && row.Index >= 0)
        {
            row.Cells[5].Value = ev.ProxyName;
            row.Cells[6].Value = bytes;
            row.Cells[7].Value = ev.Status;
            Colorize(row, ev);
            return;
        }

        if (_grid.Rows.Count > 2500)
            _grid.Rows.RemoveAt(0);

        var idx = _grid.Rows.Add(ev.Time.ToString("HH:mm:ss"), ev.ProcessName, ev.Target, ev.RuleName, ev.Action, ev.ProxyName, bytes, ev.Status);
        var added = _grid.Rows[idx];
        _rows[ev.Id] = added;
        Colorize(added, ev);
        _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, _grid.RowCount - 1);
        UpdateCounts();
    }

    private void OnLog(EngineLogEvent ev)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => OnLog(ev));
            return;
        }
        _status.Text = ev.Message;
        if (ev.IsError)
            _status.ForeColor = Theme.Error;
        else
            _status.ForeColor = Theme.Text;
    }

    private static void Colorize(DataGridViewRow row, ConnectionEvent ev)
    {
        var color = ev.Action switch
        {
            RuleAction.Proxy => Theme.Proxy,
            RuleAction.Block => Theme.Block,
            _ => Theme.Direct
        };
        if (ev.Status.StartsWith("Ошибка", StringComparison.OrdinalIgnoreCase))
            color = Theme.Error;
        row.Cells[4].Style.ForeColor = color;
        row.Cells[7].Style.ForeColor = color;
    }

    private void UpdateCounts()
    {
        var proxy = _profile.Rules.Count(r => r.Enabled && r.Action == RuleAction.Proxy);
        _counts.Text = $"Правил: {_profile.Rules.Count}  ·  через прокси: {proxy}  ·  строк: {_grid.Rows.Count}";
    }

    private void EditProxies()
    {
        using var dlg = new ProxiesForm(_profile.Proxies);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _profile.Proxies = dlg.Result;
        ProfileStore.Save(_profile);
        MaybeRestart();
    }

    private void EditRules()
    {
        using var dlg = new RulesForm(_profile.Rules, _profile.Proxies, _profile.Chains);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _profile.Rules = dlg.Result;
        ProfileStore.Save(_profile);
        UpdateCounts();
        MaybeRestart();
    }

    private void ImportPpx()
    {
        using var ofd = new OpenFileDialog { Filter = "Proxifier (*.ppx)|*.ppx|Все файлы|*.*" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _profile = PpxImporter.Import(ofd.FileName);
            ProfileStore.Save(_profile);
            UpdateCounts();
            MessageBox.Show(this, "Профиль импортирован.", "ProxyPilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
            MaybeRestart();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Export()
    {
        using var sfd = new SaveFileDialog { Filter = "JSON|*.json", FileName = "profile.json" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        ProfileStore.Save(_profile, sfd.FileName);
    }

    private async Task TestProxy()
    {
        var hops = _profile.Proxies.Take(1).ToList();
        if (hops.Count == 0)
        {
            MessageBox.Show(this, "Сначала добавьте прокси.", "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var result = await ProxyTester.TestAsync(hops);
        MessageBox.Show(this, result, "Проверка прокси", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void MaybeRestart()
    {
        if (!_engine.IsRunning) return;
        StopEngine();
        StartEngine();
    }

    private void OfferElevation()
    {
        var r = MessageBox.Show(this,
            "Для перехвата TCP нужны права администратора — как у Proxifier.\nПерезапустить с повышением прав?",
            "ProxyPilot", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r != DialogResult.Yes) return;
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas"
            });
            _tray.Visible = false;
            Close();
        }
        catch
        {
            // user cancelled UAC
        }
    }

    private static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string Format(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / 1024.0 / 1024.0:0.#} MB";
    }
}
