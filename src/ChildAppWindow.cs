using System;
using System.Drawing;
using System.Windows.Forms;

sealed class ChildAppWindow : Form
{
    readonly ListView list; readonly Button open, browse, refresh; readonly TextBox detail; readonly Font headerFont;
    public event Action<string> LaunchRequested; public event EventHandler BrowseRequested, RefreshRequested;
    public ChildAppWindow() {
        Text = "Open an app in Annex"; Font = new Font("Segoe UI", 9.5f); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(620, 370); MinimumSize = new Size(480, 330); StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Background; ForeColor = Theme.Text;
        Theme.DarkChrome(this); headerFont = new Font(Font, FontStyle.Bold);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Choose an app to open in the attached Annex desktop.", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 10) }, 0, 0);
        list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill, AccessibleName = "Installed apps", Margin = new Padding(0, 0, 0, 10), BackColor = Theme.Surface, ForeColor = Theme.Text }; list.Columns.Add("App", 145); list.Columns.Add("Location", 410); layout.Controls.Add(list, 0, 1);
        if (!Theme.HighContrast) {
            list.BorderStyle = BorderStyle.FixedSingle; list.OwnerDraw = true; Theme.DarkScrollbars(list);
            // Last column fills the width so no unpainted header area remains.
            list.Resize += delegate { if (list.Columns.Count == 2) list.Columns[1].Width = Math.Max(120, list.ClientSize.Width - list.Columns[0].Width); };
            list.DrawColumnHeader += (s, e) => {
                using (var fill = new SolidBrush(Theme.Rail)) e.Graphics.FillRectangle(fill, e.Bounds);
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, headerFont, Rectangle.Inflate(e.Bounds, -6, 0), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            list.DrawItem += (s, e) => { };
            list.DrawSubItem += (s, e) => {
                bool selected = e.Item.Selected;
                using (var fill = new SolidBrush(selected ? Theme.SurfaceHover : Theme.Surface)) e.Graphics.FillRectangle(fill, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, Rectangle.Inflate(e.Bounds, -6, 0), e.ColumnIndex == 0 ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }
        detail = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, BackColor = Theme.Background, ForeColor = Theme.Muted, ScrollBars = ScrollBars.Vertical, AccessibleName = "App result", Text = "Browsers use a separate Annex profile. Steam, games and Wallpaper Engine may have session or shared-setting restrictions." }; layout.Controls.Add(detail, 0, 2);
        Theme.DarkScrollbars(detail);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty }; layout.Controls.Add(actions, 0, 3);
        open = MakeButton("Open", ButtonKind.Primary, delegate { if (list.SelectedItems.Count == 1 && LaunchRequested != null) LaunchRequested((string)list.SelectedItems[0].Tag); });
        browse = MakeButton("Browse app...", ButtonKind.Secondary, delegate { if (BrowseRequested != null) BrowseRequested(this, EventArgs.Empty); });
        refresh = MakeButton("Refresh apps", ButtonKind.Secondary, delegate { if (RefreshRequested != null) RefreshRequested(this, EventArgs.Empty); });
        actions.Controls.Add(open); actions.Controls.Add(browse); actions.Controls.Add(refresh); list.SelectedIndexChanged += delegate { open.Enabled = list.Enabled && list.SelectedItems.Count == 1; };
        open.Enabled = false; AcceptButton = open;
    }
    Button MakeButton(string text, ButtonKind kind, EventHandler click) { var button = new AnnexButton { Text = text, AccessibleName = text, Kind = kind, AutoSize = true, MinimumSize = new Size(0, 34), Margin = new Padding(0, 6, 8, 0), Padding = new Padding(12, 2, 12, 2) }; button.Click += click; return button; }
    public void SetAvailable(bool available) { list.Enabled = browse.Enabled = refresh.Enabled = available; open.Enabled = available && list.SelectedItems.Count == 1; if (!available) detail.Text = "Connect the Annex desktop before opening an app."; }
    public void SetApps(AgentApp[] apps, string message) {
        string selected = list.SelectedItems.Count == 1 ? (string)list.SelectedItems[0].Tag : null; list.BeginUpdate(); list.Items.Clear();
        foreach (AgentApp app in apps) { var item = new ListViewItem(app.Name) { Tag = app.Id }; item.SubItems.Add(app.Path); list.Items.Add(item); if (app.Id == selected) item.Selected = true; }
        list.EndUpdate(); if (!string.IsNullOrEmpty(message)) detail.Text = message; else if (apps.Length == 0) detail.Text = "No apps found. Browse to an installed app or game EXE.";
        open.Enabled = list.Enabled && list.SelectedItems.Count == 1;
    }
}
