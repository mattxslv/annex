using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// App picker for the Annex desktop: searchable list with app icons.
sealed class ChildAppWindow : Form
{
    readonly ListBox list; readonly AnnexButton open, browse, refresh; readonly Label detail; readonly TextBox search;
    readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
    readonly Font nameFont, pathFont;
    AgentApp[] apps = new AgentApp[0];
    int hover = -1;
    bool available = true;
    public event Action<string> LaunchRequested; public event EventHandler BrowseRequested, RefreshRequested;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

    public ChildAppWindow() {
        Text = "Open an app in Annex"; Font = new Font("Segoe UI", 9.5f); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(600, 520); MinimumSize = new Size(480, 420); StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Background; ForeColor = Theme.Text;
        Theme.DarkChrome(this);
        nameFont = new Font("Segoe UI Semibold", 10.5f); pathFont = new Font("Segoe UI", 8.5f);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 20, 22, 18), ColumnCount = 1, RowCount = 5, BackColor = Theme.Background };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(layout);

        var header = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 14), BackColor = Theme.Background };
        header.Controls.Add(new Label { Text = "Open an app", AutoSize = true, Font = new Font("Segoe UI Semibold", 14), ForeColor = Theme.Text, Margin = Padding.Empty });
        header.Controls.Add(new Label { Text = "It opens inside your Annex desktop, not on this one.", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(1, 2, 0, 0) });
        layout.Controls.Add(header, 0, 0);

        search = new TextBox { AccessibleName = "Search apps" };
        var searchField = new FieldBox(search) { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 10) };
        search.HandleCreated += delegate { SendMessage(search.Handle, 0x1501, (IntPtr)1, "Search apps"); };
        search.TextChanged += delegate { Filter(); };
        search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Down && list.Items.Count > 0) { list.Focus(); if (list.SelectedIndex < 0) list.SelectedIndex = 0; e.Handled = true; } };
        layout.Controls.Add(searchField, 0, 1);

        list = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 54, BorderStyle = BorderStyle.None, BackColor = Theme.Background, ForeColor = Theme.Text, IntegralHeight = false, AccessibleName = "Installed apps", Margin = new Padding(0, 0, 0, 10) };
        Theme.DarkScrollbars(list);
        list.DrawItem += DrawApp;
        list.MouseMove += (s, e) => { int index = list.IndexFromPoint(e.Location); if (index != hover) { hover = index; list.Invalidate(); } };
        list.MouseLeave += delegate { hover = -1; list.Invalidate(); };
        list.SelectedIndexChanged += delegate { UpdateOpen(); list.Invalidate(); };
        list.DoubleClick += delegate { Launch(); };
        list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Launch(); e.Handled = true; } };
        layout.Controls.Add(list, 0, 2);

        detail = new Label { AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Faint, Margin = new Padding(0, 0, 0, 12), UseMnemonic = false, AccessibleName = "App result",
            Text = "Browsers open with a separate Annex profile. Steam, games and Wallpaper Engine may share settings with your main desktop." };
        layout.Controls.Add(detail, 0, 3);
        layout.Resize += delegate { detail.MaximumSize = new Size(Math.Max(200, layout.ClientSize.Width - layout.Padding.Horizontal), 0); };

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Background };
        open = MakeButton("Open", ButtonKind.Primary, delegate { Launch(); });
        browse = MakeButton("Browse app...", ButtonKind.Secondary, delegate { if (BrowseRequested != null) BrowseRequested(this, EventArgs.Empty); });
        refresh = MakeButton("Refresh apps", ButtonKind.Ghost, delegate { if (RefreshRequested != null) RefreshRequested(this, EventArgs.Empty); });
        actions.Controls.Add(open); actions.Controls.Add(browse); actions.Controls.Add(refresh); layout.Controls.Add(actions, 0, 4);
        open.Enabled = false; AcceptButton = open;
        Shown += delegate { search.Focus(); };
    }

    AnnexButton MakeButton(string text, ButtonKind kind, EventHandler click) { var button = new AnnexButton { Text = text, AccessibleName = text, Kind = kind, AutoSize = true, MinimumSize = new Size(kind == ButtonKind.Primary ? 110 : 0, 36), Margin = new Padding(0, 0, 8, 0), Padding = new Padding(14, 2, 14, 2) }; button.Click += click; return button; }

    AgentApp Selected { get { return list.SelectedItem as AgentApp; } }
    void UpdateOpen() { open.Enabled = available && Selected != null; }
    void Launch() { var app = Selected; if (available && app != null && LaunchRequested != null) LaunchRequested(app.Id); }

    void Filter()
    {
        string query = search.Text.Trim();
        string selectedId = Selected != null ? Selected.Id : null;
        list.BeginUpdate(); list.Items.Clear();
        foreach (AgentApp app in apps)
            if (query.Length == 0 || app.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || app.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) {
                list.Items.Add(app); if (app.Id == selectedId) list.SelectedItem = app;
            }
        list.EndUpdate(); UpdateOpen();
    }

    Bitmap IconFor(string path)
    {
        Bitmap bitmap;
        if (icons.TryGetValue(path, out bitmap)) return bitmap;
        try { using (Icon icon = File.Exists(path) ? Icon.ExtractAssociatedIcon(path) : null) bitmap = icon != null ? icon.ToBitmap() : null; }
        catch (Exception) { bitmap = null; }
        icons[path] = bitmap; return bitmap;
    }

    void DrawApp(object sender, DrawItemEventArgs e)
    {
        var g = e.Graphics;
        using (var back = new SolidBrush(Theme.Background)) g.FillRectangle(back, e.Bounds);
        if (e.Index < 0 || e.Index >= list.Items.Count) return;
        var app = (AgentApp)list.Items[e.Index];
        float s = DeviceDpi / 96f;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var card = new RectangleF(e.Bounds.X + 0.5f, e.Bounds.Y + 2.5f, e.Bounds.Width - 2f, e.Bounds.Height - 5f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (selected || e.Index == hover)
            using (var path = Theme.Rounded(card, 9 * s)) {
                using (var fill = new SolidBrush(selected ? Theme.SurfaceHover : Theme.Card)) g.FillPath(fill, path);
                using (var pen = new Pen(selected ? Theme.Accent : Theme.Border)) g.DrawPath(pen, path);
            }
        int iconSize = (int)(32 * s), left = e.Bounds.X + (int)(12 * s);
        var iconBox = new Rectangle(left, e.Bounds.Y + (e.Bounds.Height - iconSize) / 2, iconSize, iconSize);
        Bitmap bitmap = IconFor(app.Path);
        if (bitmap != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(bitmap, iconBox); }
        else using (var path = Theme.Rounded(iconBox, 7 * s)) using (var fill = new SolidBrush(Theme.Surface)) g.FillPath(fill, path);
        int textLeft = iconBox.Right + (int)(12 * s), width = e.Bounds.Right - textLeft - (int)(12 * s);
        TextRenderer.DrawText(g, app.Name, nameFont, new Rectangle(textLeft, e.Bounds.Y + (int)(8 * s), width, (int)(20 * s)), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, app.Path, pathFont, new Rectangle(textLeft, e.Bounds.Y + (int)(28 * s), width, (int)(18 * s)), Theme.Faint, TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
    }

    public void SetAvailable(bool value) { available = value; list.Enabled = browse.Enabled = refresh.Enabled = search.Enabled = value; UpdateOpen(); if (!value) detail.Text = "Connect the Annex desktop before opening an app."; }

    public void SetApps(AgentApp[] value, string message) {
        apps = (value ?? new AgentApp[0]).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        Filter();
        if (!string.IsNullOrEmpty(message)) detail.Text = message; else if (apps.Length == 0) detail.Text = "No apps found. Browse to an installed app or game EXE.";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { foreach (var bitmap in icons.Values) if (bitmap != null) bitmap.Dispose(); nameFont.Dispose(); pathFont.Dispose(); }
        base.Dispose(disposing);
    }
}
