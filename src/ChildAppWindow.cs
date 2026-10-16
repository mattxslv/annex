using System;
using System.Drawing;
using System.Windows.Forms;

sealed class ChildAppWindow : Form
{
    readonly ListView list; readonly Button open, browse, refresh; readonly TextBox detail;
    public event Action<string> LaunchRequested; public event EventHandler BrowseRequested, RefreshRequested;
    public ChildAppWindow() {
        Text = "Open in child desktop"; Font = new Font("Segoe UI", 9.5f); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(620, 370); MinimumSize = new Size(480, 330); StartPosition = FormStartPosition.CenterParent; BackColor = SystemColors.Window; ForeColor = SystemColors.WindowText;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Choose an app to open in the attached child desktop.", AutoSize = true, Margin = new Padding(0, 0, 0, 10) }, 0, 0);
        list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill, AccessibleName = "Installed child apps", Margin = new Padding(0, 0, 0, 10) }; list.Columns.Add("App", 145); list.Columns.Add("Location", 410); layout.Controls.Add(list, 0, 1);
        detail = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, BackColor = SystemColors.Window, ForeColor = SystemColors.WindowText, ScrollBars = ScrollBars.Vertical, AccessibleName = "Child app result", Text = "Browsers use a separate child profile. Steam, games and Wallpaper Engine may have session or shared-setting restrictions." }; layout.Controls.Add(detail, 0, 2);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty }; layout.Controls.Add(actions, 0, 3);
        open = MakeButton("Open in child", delegate { if (list.SelectedItems.Count == 1 && LaunchRequested != null) LaunchRequested((string)list.SelectedItems[0].Tag); });
        browse = MakeButton("Browse app...", delegate { if (BrowseRequested != null) BrowseRequested(this, EventArgs.Empty); });
        refresh = MakeButton("Refresh apps", delegate { if (RefreshRequested != null) RefreshRequested(this, EventArgs.Empty); });
        actions.Controls.Add(open); actions.Controls.Add(browse); actions.Controls.Add(refresh); list.SelectedIndexChanged += delegate { open.Enabled = list.Enabled && list.SelectedItems.Count == 1; };
        open.Enabled = false; AcceptButton = open;
    }
    Button MakeButton(string text, EventHandler click) { var button = new Button { Text = text, AccessibleName = text, AutoSize = true, MinimumSize = new Size(0, 32), UseVisualStyleBackColor = true, Margin = new Padding(0, 6, 8, 0), Padding = new Padding(8, 2, 8, 2) }; button.Click += click; return button; }
    public void SetAvailable(bool available) { list.Enabled = browse.Enabled = refresh.Enabled = available; open.Enabled = available && list.SelectedItems.Count == 1; if (!available) detail.Text = "Connect the child desktop before opening an app."; }
    public void SetApps(AgentApp[] apps, string message) {
        string selected = list.SelectedItems.Count == 1 ? (string)list.SelectedItems[0].Tag : null; list.BeginUpdate(); list.Items.Clear();
        foreach (AgentApp app in apps) { var item = new ListViewItem(app.Name) { Tag = app.Id }; item.SubItems.Add(app.Path); list.Items.Add(item); if (app.Id == selected) item.Selected = true; }
        list.EndUpdate(); if (!string.IsNullOrEmpty(message)) detail.Text = message; else if (apps.Length == 0) detail.Text = "No apps found. Browse to an installed app or game EXE.";
        open.Enabled = list.Enabled && list.SelectedItems.Count == 1;
    }
}
