using ProxyPilot.Core.Config;

namespace ProxyPilot.UI;

internal sealed class RulesForm : Form
{
    private readonly DataGridView _grid = new();
    private List<ProfileRule> _items;
    private readonly List<ProxyServer> _proxies;
    private readonly List<ProxyChain> _chains;

    public List<ProfileRule> Result => _items;

    public RulesForm(IEnumerable<ProfileRule> rules, List<ProxyServer> proxies, List<ProxyChain> chains)
    {
        _items = rules.Select(Clone).ToList();
        _proxies = proxies;
        _chains = chains;
        Text = "Правила проксификации";
        Size = new Size(980, 520);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);

        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("on", "Вкл");
        _grid.Columns.Add("name", "Имя");
        _grid.Columns.Add("apps", "Приложения");
        _grid.Columns.Add("targets", "Цели");
        _grid.Columns.Add("action", "Действие");
        _grid.Columns.Add("proxy", "Прокси");
        Controls.Add(_grid);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
        bar.Controls.Add(MakeButton("Добавить", Add));
        bar.Controls.Add(MakeButton("Изменить", Edit));
        bar.Controls.Add(MakeButton("Удалить", Delete));
        bar.Controls.Add(MakeButton("Вверх", (_, _) => MoveRule(-1)));
        bar.Controls.Add(MakeButton("Вниз", (_, _) => MoveRule(1)));
        bar.Controls.Add(MakeButton("OK", (_, _) => { DialogResult = DialogResult.OK; Close(); }));
        Controls.Add(bar);
        Theme.Apply(this);
        Reload();
    }

    private void Reload()
    {
        _grid.Rows.Clear();
        foreach (var r in _items)
        {
            var proxy = _proxies.FirstOrDefault(p => p.Id == r.ProxyId)?.Display
                        ?? _chains.FirstOrDefault(c => c.Id == r.ProxyId)?.Name
                        ?? "";
            _grid.Rows.Add(r.Enabled ? "да" : "нет", r.Name,
                string.Join("; ", r.Applications),
                string.Join("; ", r.Targets),
                r.Action, r.Action == RuleAction.Proxy ? proxy : "");
        }
    }

    private void Add(object? s, EventArgs e)
    {
        var rule = new ProfileRule();
        using var dlg = new RuleEditForm(rule, _proxies, _chains);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _items.Add(rule);
            Reload();
        }
    }

    private void Edit(object? s, EventArgs e)
    {
        var idx = _grid.CurrentRow?.Index ?? -1;
        if (idx < 0 || idx >= _items.Count) return;
        using var dlg = new RuleEditForm(_items[idx], _proxies, _chains);
        if (dlg.ShowDialog(this) == DialogResult.OK) Reload();
    }

    private void Delete(object? s, EventArgs e)
    {
        var idx = _grid.CurrentRow?.Index ?? -1;
        if (idx < 0 || idx >= _items.Count) return;
        _items.RemoveAt(idx);
        Reload();
    }

    private void MoveRule(int delta)
    {
        var idx = _grid.CurrentRow?.Index ?? -1;
        var next = idx + delta;
        if (idx < 0 || next < 0 || next >= _items.Count) return;
        (_items[idx], _items[next]) = (_items[next], _items[idx]);
        Reload();
        _grid.ClearSelection();
        _grid.Rows[next].Selected = true;
        _grid.CurrentCell = _grid.Rows[next].Cells[0];
    }

    private static ProfileRule Clone(ProfileRule r) => new()
    {
        Name = r.Name,
        Enabled = r.Enabled,
        Applications = [.. r.Applications],
        Targets = [.. r.Targets],
        Action = r.Action,
        ProxyId = r.ProxyId
    };

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
        b.Click += onClick;
        return b;
    }
}

internal sealed class RuleEditForm : Form
{
    public RuleEditForm(ProfileRule rule, List<ProxyServer> proxies, List<ProxyChain> chains)
    {
        Text = "Правило";
        Size = new Size(560, 420);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), RowCount = 7 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var enabled = new CheckBox { Text = "Включено", Checked = rule.Enabled, AutoSize = true };
        table.Controls.Add(enabled, 1, 0);
        var name = AddRow(table, 1, "Имя", rule.Name);
        var apps = AddRow(table, 2, "Приложения", string.Join("; ", rule.Applications));
        apps.PlaceholderText = "steam.exe; chrome.exe  (пусто = любые)";
        var targets = AddRow(table, 3, "Цели", string.Join("; ", rule.Targets));
        targets.PlaceholderText = "127.0.0.1; *:443; 10.0.0.0/8  (пусто = любые)";
        var action = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        action.Items.AddRange(["Direct", "Proxy", "Block"]);
        action.SelectedItem = rule.Action.ToString();
        table.Controls.Add(new Label { Text = "Действие", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        table.Controls.Add(action, 1, 4);
        var proxy = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        foreach (var p in proxies)
            proxy.Items.Add(new Option(p.Id, p.Display));
        foreach (var c in chains)
            proxy.Items.Add(new Option(c.Id, "Цепочка: " + c.Name));
        if (proxy.Items.Count > 0)
        {
            var selected = proxy.Items.Cast<Option>().FirstOrDefault(o => o.Id == rule.ProxyId);
            proxy.SelectedItem = selected ?? proxy.Items[0];
        }
        table.Controls.Add(new Label { Text = "Прокси", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 5);
        table.Controls.Add(proxy, 1, 5);
        var ok = new Button { Text = "Сохранить", Dock = DockStyle.Fill };
        ok.Click += (_, _) =>
        {
            rule.Enabled = enabled.Checked;
            rule.Name = name.Text.Trim();
            rule.Applications = PpxImporter.SplitList(apps.Text);
            rule.Targets = PpxImporter.SplitList(targets.Text);
            rule.Action = Enum.Parse<RuleAction>(action.SelectedItem!.ToString()!);
            rule.ProxyId = (proxy.SelectedItem as Option)?.Id;
            DialogResult = DialogResult.OK;
            Close();
        };
        table.Controls.Add(ok, 1, 6);
        Controls.Add(table);
        Theme.Apply(this);
    }

    private static TextBox AddRow(TableLayoutPanel table, int row, string label, string value)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        var box = new TextBox { Text = value, Dock = DockStyle.Fill };
        table.Controls.Add(box, 1, row);
        return box;
    }

    private sealed record Option(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
