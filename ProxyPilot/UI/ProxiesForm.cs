using ProxyPilot.Core.Config;

namespace ProxyPilot.UI;

internal sealed class ProxiesForm : Form
{
    private readonly DataGridView _grid = new();
    private List<ProxyServer> _items;

    public List<ProxyServer> Result => _items;

    public ProxiesForm(IEnumerable<ProxyServer> proxies)
    {
        _items = proxies.Select(Clone).ToList();
        Text = "Прокси-серверы";
        Size = new Size(820, 420);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);

        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("name", "Имя");
        _grid.Columns.Add("type", "Тип");
        _grid.Columns.Add("host", "Адрес");
        _grid.Columns.Add("port", "Порт");
        _grid.Columns.Add("user", "Логин");
        Controls.Add(_grid);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(8)
        };
        bar.Controls.Add(MakeButton("Добавить", Add));
        bar.Controls.Add(MakeButton("Изменить", Edit));
        bar.Controls.Add(MakeButton("Удалить", Delete));
        var ok = MakeButton("OK", (_, _) => { DialogResult = DialogResult.OK; Close(); });
        bar.Controls.Add(ok);
        Controls.Add(bar);
        Theme.Apply(this);
        Reload();
    }

    private void Reload()
    {
        _grid.Rows.Clear();
        foreach (var p in _items)
            _grid.Rows.Add(p.Name, p.Type, p.Host, p.Port, p.Username ?? "");
    }

    private void Add(object? sender, EventArgs e)
    {
        var item = new ProxyServer();
        using var dlg = new ProxyEditForm(item);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _items.Add(item);
            Reload();
        }
    }

    private void Edit(object? sender, EventArgs e)
    {
        var idx = _grid.CurrentRow?.Index ?? -1;
        if (idx < 0 || idx >= _items.Count)
            return;
        using var dlg = new ProxyEditForm(_items[idx]);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            Reload();
    }

    private void Delete(object? sender, EventArgs e)
    {
        var idx = _grid.CurrentRow?.Index ?? -1;
        if (idx < 0 || idx >= _items.Count)
            return;
        _items.RemoveAt(idx);
        Reload();
    }

    private static ProxyServer Clone(ProxyServer p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Type = p.Type,
        Host = p.Host,
        Port = p.Port,
        Username = p.Username,
        Password = p.Password
    };

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
        b.Click += onClick;
        return b;
    }
}

internal sealed class ProxyEditForm : Form
{
    public ProxyEditForm(ProxyServer proxy)
    {
        Text = "Прокси";
        Size = new Size(420, 360);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), RowCount = 7 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var name = AddRow(table, 0, "Имя", proxy.Name);
        var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        type.Items.AddRange(["Socks5", "Socks4", "HttpConnect"]);
        type.SelectedItem = proxy.Type.ToString();
        table.Controls.Add(new Label { Text = "Тип", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        table.Controls.Add(type, 1, 1);
        var host = AddRow(table, 2, "Адрес", proxy.Host);
        var port = AddRow(table, 3, "Порт", proxy.Port.ToString());
        var user = AddRow(table, 4, "Логин", proxy.Username ?? "");
        var pass = AddRow(table, 5, "Пароль", proxy.Password ?? "");
        ((TextBox)pass).UseSystemPasswordChar = true;
        var ok = new Button { Text = "Сохранить", Dock = DockStyle.Fill };
        ok.Click += (_, _) =>
        {
            proxy.Name = name.Text.Trim();
            proxy.Type = Enum.Parse<ProxyType>(type.SelectedItem!.ToString()!);
            proxy.Host = host.Text.Trim();
            proxy.Port = int.TryParse(port.Text, out var p) ? p : proxy.Port;
            proxy.Username = string.IsNullOrWhiteSpace(user.Text) ? null : user.Text.Trim();
            proxy.Password = string.IsNullOrWhiteSpace(pass.Text) ? null : pass.Text;
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
}
