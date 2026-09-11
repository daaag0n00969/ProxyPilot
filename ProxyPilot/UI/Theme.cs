namespace ProxyPilot.UI;

internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(28, 28, 34);
    public static readonly Color Panel = Color.FromArgb(38, 38, 46);
    public static readonly Color Grid = Color.FromArgb(44, 44, 54);
    public static readonly Color Text = Color.FromArgb(232, 232, 236);
    public static readonly Color Muted = Color.FromArgb(160, 160, 170);
    public static readonly Color Accent = Color.FromArgb(70, 140, 230);
    public static readonly Color AccentDark = Color.FromArgb(45, 95, 170);
    public static readonly Color Direct = Color.FromArgb(180, 180, 190);
    public static readonly Color Proxy = Color.FromArgb(90, 200, 130);
    public static readonly Color Block = Color.FromArgb(230, 90, 90);
    public static readonly Color Error = Color.FromArgb(240, 170, 70);

    public static void Apply(Control root)
    {
        root.BackColor = Bg;
        root.ForeColor = Text;
        foreach (Control child in root.Controls)
            Apply(child);
        if (root is Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = AccentDark;
            button.BackColor = AccentDark;
            button.ForeColor = Color.White;
            button.Cursor = Cursors.Hand;
        }
        if (root is DataGridView grid)
        {
            grid.BackgroundColor = Grid;
            grid.GridColor = Color.FromArgb(60, 60, 72);
            grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Panel;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Panel;
            grid.DefaultCellStyle.BackColor = Grid;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = AccentDark;
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }
    }
}
