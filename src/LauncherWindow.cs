using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

sealed class LauncherWindow : Form
{
    readonly Color ink = Theme.Text;
    readonly Color muted = Theme.Muted;
    readonly StatusLabel sessionTitle, hostTitle;
    readonly Label sessionDetail, hostDetail;
    readonly AnnexButton desktop, reconnect, startHost, settings, endSession, copyAddress, copySettings, openApp, refreshHosts, browseHost;
    readonly ComboBox hostChoice;
    readonly Label webLabel;
    readonly TextBox address, settingsAddress;
    readonly PairingSteps pairingHelp;
    readonly Label placeholderTitle, placeholderBody;
    readonly Panel workspace, placeholder;
    readonly ToolStripStatusLabel footer;
    readonly ToolTip tips = new ToolTip();
    HostSnapshot host = new HostSnapshot(HostState.Waiting, "Start the Annex desktop to check its Sunshine instance.", 0);
    bool connected, busy, ending, showPairingHelp, setupReady = true;
    bool hasSelection;
    string hostName;
    public event EventHandler ConnectRequested, ReconnectRequested, StartHostRequested, SettingsRequested, EndSessionRequested, GuideRequested, MinimizeRequested;
    public event EventHandler RefreshHostsRequested, BrowseHostRequested, OpenAppRequested;
    public event Action<string> HostSelected, InstallRequested;
    public bool Preview { get; private set; }

    const int RailWidth = 300, CardText = 222;

    public LauncherWindow(bool preview)
    {
        Preview = preview;
        hasSelection = preview; hostName = preview ? "Sunshine" : "host";
        Text = preview ? "Annex - Preview" : "Annex";
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f); ForeColor = ink;
        BackColor = Theme.Background;
        ClientSize = new Size(1180, 760); MinimumSize = new Size(900, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Theme.DarkChrome(this);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = BackColor, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, RailWidth)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        // ---- Sidebar: brand + two cards, full window height ----
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Rail, Margin = Padding.Empty };
        Theme.DarkScrollbars(scroll);
        var rail = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(18, 22, 18, 18), Margin = Padding.Empty, BackColor = Theme.Rail };
        rail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(rail); layout.Controls.Add(scroll, 0, 0);
        Action<Control> add = control => { int row = rail.RowCount++; rail.RowStyles.Add(new RowStyle(SizeType.AutoSize)); rail.Controls.Add(control, 0, row); };

        var brand = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 2, Margin = new Padding(6, 0, 0, 22), BackColor = Theme.Rail };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); brand.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var mark = new LogoMark { Size = new Size(44, 44), MinimumSize = new Size(44, 44), Margin = new Padding(0, 0, 12, 0) };
        brand.Controls.Add(mark, 0, 0); brand.SetRowSpan(mark, 2);
        brand.Controls.Add(Label("Annex", 20, FontStyle.Bold, ink, 0, 0), 1, 0);
        brand.Controls.Add(Label("Second desktop for streaming", 8.5f, FontStyle.Regular, Theme.Faint, 0, 0), 1, 1);
        add(brand);

        var desktopCard = new CardPanel { Margin = new Padding(0, 0, 0, 14) };
        desktopCard.Add(Section("SECOND DESKTOP"));
        sessionTitle = Status("Not connected"); desktopCard.Add(sessionTitle);
        sessionDetail = Label("Start a separate desktop for your stream. Keep this viewer attached while playing.", 9, FontStyle.Regular, muted, 0, 12); desktopCard.Add(sessionDetail);
        desktop = Button("&Start desktop", true, (s, e) => Raise(ConnectRequested)); desktopCard.Add(desktop);
        reconnect = Button("&Reconnect", false, (s, e) => Raise(ReconnectRequested)); reconnect.Enabled = false; reconnect.Kind = ButtonKind.Ghost; reconnect.Tag = ButtonKind.Ghost; desktopCard.Add(reconnect);
        add(desktopCard);

        var hostCard = new CardPanel { Margin = Padding.Empty };
        hostCard.Add(Section("STREAMING HOST"));
        hostTitle = Status("Waiting for desktop"); hostCard.Add(hostTitle);
        hostChoice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, AccessibleName = "Streaming host installation", Margin = new Padding(0, 4, 0, 10), Visible = false };
        if (!Theme.HighContrast) {
            hostChoice.BackColor = Theme.Surface; hostChoice.ForeColor = Theme.Text;
            hostChoice.DrawMode = DrawMode.OwnerDrawFixed; hostChoice.ItemHeight = 24; Theme.DarkCombo(hostChoice);
            hostChoice.DrawItem += (s, e) => {
                bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                using (var fill = new SolidBrush(selected ? Theme.SurfaceHover : Theme.Surface)) e.Graphics.FillRectangle(fill, e.Bounds);
                if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, hostChoice.Items[e.Index].ToString(), e.Font, Rectangle.Inflate(e.Bounds, -6, 0), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }
        hostChoice.SelectionChangeCommitted += delegate { var choice = hostChoice.SelectedItem as HostChoice; if (choice != null && HostSelected != null) HostSelected(choice.Id); }; hostCard.Add(hostChoice);
        hostDetail = Label(host.Message, 9, FontStyle.Regular, muted, 0, 12);
        startHost = Button("Start &Sunshine", true, (s, e) => Raise(StartHostRequested)); hostCard.Add(startHost);
        settings = Button("Open Sunshine settings", false, (s, e) => Raise(SettingsRequested)); hostCard.Add(settings);
        hostCard.Add(hostDetail);
        // Three equal, centred tool buttons under a divider.
        hostCard.Add(new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Theme.Border, Margin = new Padding(0, 2, 0, 6) });
        var hostTools = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Card };
        for (int c = 0; c < 3; c++) hostTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        refreshHosts = Tool("Refresh", "Refresh hosts", (s, e) => Raise(RefreshHostsRequested));
        browseHost = Tool("Browse", "Browse host", (s, e) => Raise(BrowseHostRequested));
        var install = Tool("Install", "Install a host...", null);
        // Built once and reused: disposing the menu in its Closed event crashed when an item was clicked.
        var vendors = new ContextMenuStrip { Renderer = Theme.MenuRenderer(), BackColor = Theme.Surface, ForeColor = Theme.Text };
        vendors.Items.Add("Sunshine (download and set up)", null, (s, e) => { if (InstallRequested != null) InstallRequested("Sunshine"); });
        vendors.Items.Add("Apollo (open download page)", null, (s, e) => { if (InstallRequested != null) InstallRequested("Apollo"); });
        Disposed += delegate { vendors.Dispose(); };
        install.Click += delegate { vendors.Show(install, new Point(0, install.Height)); };
        foreach (var tool in new[] { refreshHosts, browseHost, install }) { tool.Dock = DockStyle.Fill; tool.AutoSize = false; tool.Height = 32; tool.Margin = new Padding(1, 0, 1, 0); }
        hostTools.Controls.Add(refreshHosts, 0, 0); hostTools.Controls.Add(browseHost, 1, 0); hostTools.Controls.Add(install, 2, 0);
        hostCard.Add(hostTools);
        add(hostCard);

        // ---- Main column: toolbar, viewer, pairing, status bar (only under this column) ----
        var main = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Theme.Background };
        layout.Controls.Add(main, 1, 0);
        var statusBar = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = true, BackColor = Theme.Background, ForeColor = Theme.Muted, Renderer = Theme.MenuRenderer() };
        footer = new ToolStripStatusLabel(preview ? "Preview mode - synthetic data, no system changes" : "Ready. Your physical desktop stays available.") { Spring = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Faint };
        statusBar.Items.Add(footer);
        var repair = new ToolStripButton("Setup / repair") { AccessibleName = "Setup / repair", ForeColor = Theme.Muted }; repair.Click += (s, e) => Raise(GuideRequested); statusBar.Items.Add(repair);

        address = new TextBox { ReadOnly = true, Text = "Available after host check", Width = 210, AccessibleName = "Client pairing address" };
        copyAddress = Button("Copy address", false, (s, e) => {
            if (!HostConfiguration.IsValidPort(host.BasePort)) return;
            try { Clipboard.SetText(address.Text); SetNotice("Pairing address copied."); }
            catch (Exception) { SetNotice("Clipboard is busy. Select and copy the address manually."); }
        }); copyAddress.Dock = DockStyle.None;
        settingsAddress = new TextBox { ReadOnly = true, Text = "Available when Sunshine is running", Width = 250, AccessibleName = "Sunshine web UI URL" };
        copySettings = Button("Copy settings URL", false, (s, e) => {
            string url = LauncherPolicy.SettingsUrl(host);
            if (!connected || url == null) return;
            try { Clipboard.SetText(url); SetNotice(LauncherPolicy.SettingsFeedback(true)); }
            catch (Exception) { SetNotice("Clipboard is busy. Select and copy the Host web UI URL manually."); }
        }); copySettings.Dock = DockStyle.None;

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(22, 18, 22, 10), Margin = Padding.Empty, BackColor = Theme.Background };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.Controls.Add(right); main.Controls.Add(statusBar);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 12), BackColor = Theme.Background };
        tools.Controls.Add(Label("Desktop", 13, FontStyle.Bold, ink, 6, 6)); tools.Controls[0].Margin = new Padding(0, 5, 18, 0);
        openApp = Button("Open &app", false, (s, e) => Raise(OpenAppRequested)); openApp.Dock = DockStyle.None; openApp.Margin = new Padding(0, 0, 8, 0); tools.Controls.Add(openApp);
        var tray = Button("Minimize to tray", false, (s, e) => Raise(MinimizeRequested)); tray.Dock = DockStyle.None; tray.Margin = new Padding(0, 0, 8, 0); tools.Controls.Add(tray);
        endSession = Button("End session...", false, (s, e) => Raise(EndSessionRequested)); endSession.Kind = ButtonKind.Danger; endSession.Tag = ButtonKind.Danger; endSession.Dock = DockStyle.None; endSession.Margin = Padding.Empty; tools.Controls.Add(endSession); right.Controls.Add(tools, 0, 0);
        workspace = new Panel { Name = "DesktopWorkspace", AccessibleName = "Annex desktop workspace", Dock = DockStyle.Fill, BackColor = Color.FromArgb(10, 12, 16), Margin = Padding.Empty };
        if (Theme.HighContrast) workspace.BackColor = SystemColors.Window;
        right.Controls.Add(workspace, 0, 1);
        placeholder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(44), BackColor = workspace.BackColor, AutoScroll = true };
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, MinimumSize = new Size(0, 260), BackColor = workspace.BackColor };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); center.RowStyles.Add(new RowStyle(SizeType.AutoSize)); center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var message = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top, BackColor = workspace.BackColor };
        placeholderTitle = Label("Your stream's own desktop", 24, FontStyle.Bold, ink, 0, 12);
        placeholderBody = Label("Start a second Windows desktop for games and apps. Sunshine shares that desktop with your client while you keep using this one.", 11, FontStyle.Regular, muted, 0, 0);
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        message.Controls.Add(placeholderTitle); message.Controls.Add(placeholderBody); center.Controls.Add(message, 0, 1); placeholder.Controls.Add(center); workspace.Controls.Add(placeholder);
        Action fitPlaceholder = delegate {
            int width = Math.Max(200, placeholder.ClientSize.Width - placeholder.Padding.Horizontal - 8);
            placeholderTitle.MaximumSize = placeholderBody.MaximumSize = new Size(Math.Min(width, 640), 0);
            message.MaximumSize = new Size(width, 0);
        };
        placeholder.Resize += delegate { fitPlaceholder(); }; Shown += delegate { fitPlaceholder(); };

        var pairing = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 3, RowCount = 4, Margin = new Padding(0, 14, 0, 0), BackColor = Theme.Background };
        pairing.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); pairing.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pairing.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int row = 0; row < 4; row++) pairing.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var clientLabel = Label("Client address", 9.5f, FontStyle.Bold, muted, 7, 0); clientLabel.MinimumSize = new Size(112, 0); clientLabel.MaximumSize = Size.Empty;
        var addressField = new FieldBox(address) { Dock = DockStyle.Fill, Margin = new Padding(8, 0, 8, 8) }; var settingsField = new FieldBox(settingsAddress) { Dock = DockStyle.Fill, Margin = new Padding(8, 0, 8, 8) };
        copyAddress.Margin = copySettings.Margin = new Padding(0, 0, 0, 8); copyAddress.MinimumSize = copySettings.MinimumSize = new Size(0, addressField.Height);
        pairing.Controls.Add(clientLabel, 0, 0); pairing.Controls.Add(addressField, 1, 0); pairing.Controls.Add(copyAddress, 2, 0);
        webLabel = Label("Host web UI", 9.5f, FontStyle.Bold, muted, 7, 0); webLabel.MinimumSize = new Size(112, 0); webLabel.MaximumSize = Size.Empty;
        pairing.Controls.Add(webLabel, 0, 1); pairing.Controls.Add(settingsField, 1, 1); pairing.Controls.Add(copySettings, 2, 1);
        pairingHelp = new PairingSteps { Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 4), Visible = false, Font = new Font("Segoe UI", 9f) };
        var steps = Button("Show pairing steps", false, null); steps.Dock = DockStyle.None; steps.Name = "PairingSteps"; steps.Margin = new Padding(0, 2, 6, 4); steps.Kind = ButtonKind.Ghost; steps.Tag = ButtonKind.Ghost;
        steps.Click += delegate { showPairingHelp = !showPairingHelp; pairingHelp.Visible = showPairingHelp; steps.Text = showPairingHelp ? "Hide pairing steps" : "Show pairing steps"; steps.AccessibleName = steps.Text; };
        pairing.Controls.Add(steps, 0, 2); pairing.SetColumnSpan(steps, 3); pairing.Controls.Add(pairingHelp, 0, 3); pairing.SetColumnSpan(pairingHelp, 3);
        right.Controls.Add(pairing, 0, 2);
        SetHost(host); UpdateActions();
    }

    Label Label(string text, float size, FontStyle style, Color color, int top, int bottom)
    {
        return new Label { Text = text, AutoSize = true, MaximumSize = new Size(CardText, 0), Font = new Font("Segoe UI", size, style), ForeColor = color, BackColor = Color.Transparent, Margin = new Padding(0, top, 0, bottom), UseMnemonic = false };
    }
    Label Section(string text) { return Label(text, 7.5f, FontStyle.Bold, Theme.Faint, 0, 6); }
    StatusLabel Status(string text)
    {
        return new StatusLabel { Text = text, MaximumSize = new Size(CardText, 0), Font = new Font("Segoe UI Semibold", 13.5f), ForeColor = ink, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 4) };
    }
    AnnexButton Button(string text, bool primary, EventHandler action)
    {
        var button = new AnnexButton { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, primary ? 40 : 34), Dock = DockStyle.Top, Padding = new Padding(14, 3, 14, 3), Margin = new Padding(0, 0, 0, 8), AccessibleName = text.Replace("&", "") };
        button.Tag = primary;
        StyleButton(button);
        button.Click += action; return button;
    }
    AnnexButton Tool(string text, string accessibleName, EventHandler action)
    {
        var button = new AnnexButton { Text = text, Kind = ButtonKind.Ghost, Tag = ButtonKind.Ghost, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 30), Padding = new Padding(8, 1, 8, 1), Margin = new Padding(0, 0, 2, 2), AccessibleName = accessibleName };
        if (action != null) button.Click += action;
        return button;
    }
    void StyleButton(AnnexButton button)
    {
        if (button.Tag is ButtonKind) return;
        button.Kind = button.Tag is bool && (bool)button.Tag ? ButtonKind.Primary : ButtonKind.Secondary;
    }
    // "Sunshine · annex-dev" instead of the full path; the path stays in the tooltip.
    static string ShortLocation(string path)
    {
        try {
            string dir = Path.GetDirectoryName(path);
            string[] skip = { "sunshine", "apollo", "vibepollo", "bin" };
            while (Path.GetDirectoryName(dir) != null && Array.IndexOf(skip, Path.GetFileName(dir).ToLowerInvariant()) >= 0) dir = Path.GetDirectoryName(dir);
            string name = Path.GetFileName(dir);
            return string.IsNullOrEmpty(name) ? dir : name;
        } catch (ArgumentException) { return path; }
    }
    void Raise(EventHandler action) { if (action != null) action(this, EventArgs.Empty); }
    void UpdateActions()
    {
        desktop.Enabled = setupReady && !ending; reconnect.Enabled = connected && !ending;
        startHost.Visible = hasSelection && host.State != HostState.Running && host.State != HostState.Waiting && host.State != HostState.Checking;
        startHost.Enabled = hasSelection && !busy && !ending && LauncherPolicy.CanStartHost(host, connected);
        settings.Visible = host.State == HostState.Running;
        settings.Enabled = connected && !busy && !ending && LauncherPolicy.SettingsUrl(host) != null;
        copyAddress.Enabled = connected && !ending && host.State == HostState.Running && HostConfiguration.IsValidPort(host.BasePort);
        copySettings.Enabled = connected && !ending && LauncherPolicy.SettingsUrl(host) != null;
        desktop.Tag = !connected; StyleButton(desktop);
        endSession.Enabled = connected && !ending && !Preview;
        openApp.Enabled = connected && !busy && !ending;
        refreshHosts.Enabled = browseHost.Enabled = hostChoice.Enabled = connected && !busy && !ending;
    }
    public void SetBusy(bool value) { busy = value; UpdateActions(); }
    public void SetEnding(bool value) { ending = value; UpdateActions(); }
    public bool LabelPreview = true; // README screenshots turn the "Preview mode" prefix off
    public void SetNotice(string message) { footer.Text = Preview && LabelPreview ? "Preview mode - " + message : message; }
    public void SetSession(string title, string detail, bool attached, bool ready)
    {
        connected = attached; setupReady = ready; sessionTitle.Text = title; sessionDetail.Text = detail;
        sessionTitle.DotColor = attached ? Theme.Accent : ready ? Theme.Faint : Theme.Warn;
        desktop.Text = attached ? "Show &desktop" : "&Start desktop";
        desktop.AccessibleName = desktop.Text.Replace("&", "");
        UpdateActions();
    }
    public void SetHost(HostSnapshot value)
    {
        host = value;
        hostTitle.Text = value.State == HostState.Running ? hostName + " running" : value.State == HostState.Stopped ? hostName + " is off" : value.State == HostState.Missing ? "No host detected" : value.State == HostState.Unavailable ? "Helper unavailable" : value.State == HostState.Starting ? "Starting " + hostName : value.State == HostState.Checking ? "Checking " + hostName : value.State == HostState.Error ? "Needs attention" : value.State == HostState.Unknown ? "Status unknown" : "Waiting for desktop";
        hostTitle.DotColor = value.State == HostState.Running ? Theme.Good : value.State == HostState.Error || value.State == HostState.Unavailable ? Theme.Bad : value.State == HostState.Starting || value.State == HostState.Checking || value.State == HostState.Unknown ? Theme.Warn : Theme.Faint;
        hostDetail.Text = value.Message.Length > 230 ? value.Message.Substring(0, 227) + "..." : value.Message;
        hostDetail.AccessibleDescription = value.Message;
        address.Text = value.State == HostState.Running && HostConfiguration.IsValidPort(value.BasePort) ? (Preview ? "gaming-pc" : Environment.MachineName) + ":" + value.BasePort : "Available when host is running";
        settingsAddress.Text = LauncherPolicy.SettingsUrl(value) ?? "Available when host is running";
        pairingHelp.Text = LauncherPolicy.PairingInstructions(Preview ? "gaming-pc" : Environment.MachineName, value, hostName);
        UpdateActions();
    }
    sealed class HostChoice { public string Id, Text; public override string ToString() { return Text; } }
    public void SetDiscovery(AgentReply reply) {
        hasSelection = !string.IsNullOrEmpty(reply.SelectionId); hostName = "host"; hostChoice.BeginUpdate(); hostChoice.Items.Clear();
        foreach (AgentHost item in reply.Hosts) {
            // VibePollo can't run a second instance inside Annex (it is usually the main host), so it isn't offered.
            if (item.Name == "VibePollo" && item.Id != reply.SelectionId) continue;
            var choice = new HostChoice { Id = item.Id, Text = item.Name + " · " + ShortLocation(item.Path) }; hostChoice.Items.Add(choice);
            if (item.Id == reply.SelectionId) { hostChoice.SelectedItem = choice; hostName = item.Name; }
        }
        hostChoice.Visible = hostChoice.Items.Count > 1; hostChoice.EndUpdate();
        startHost.Text = "Start " + hostName; startHost.AccessibleName = startHost.Text;
        settings.Text = "Open " + hostName + " settings"; settings.AccessibleName = settings.Text; settingsAddress.AccessibleName = hostName == "host" ? "Host web UI URL" : hostName + " web UI URL";
        SetHost(reply.Status); if (!string.IsNullOrEmpty(reply.Diagnostic)) SetNotice(reply.Diagnostic);
    }
    public void ShowGuidance(string title, string body)
    {
        placeholderTitle.Text = title; placeholderBody.Text = body; placeholder.Visible = true; placeholder.BringToFront();
    }
    Control viewer;
    // The viewer stays behind the placeholder until RevealViewer, so its start-up flashes stay hidden.
    public void AttachViewer(Control control)
    {
        viewer = control; viewer.Dock = DockStyle.Fill; viewer.BackColor = Color.Black; workspace.Controls.Add(viewer); placeholder.BringToFront();
    }
    public void RevealViewer() { if (viewer != null) { viewer.BringToFront(); placeholder.Visible = false; } }
    protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
}

static class LauncherPreview
{
    public static LauncherWindow Create(HostState state)
    {
        var form = new LauncherWindow(true);
        form.SetSession("Connected", "Preview of a separate Windows desktop. No real session is connected.", true, true);
        form.SetHost(new HostSnapshot(state, state == HostState.Running ? "Sunshine is running in the Annex desktop. Pair your compatible client with this host." : state == HostState.Stopped ? "Sunshine is off in the Annex desktop. Start it to make this desktop available to your client." : state == HostState.Checking ? "Checking Sunshine in the attached Annex desktop..." : "Annex helper unavailable. Run setup and reconnect a newly signed-in Annex desktop, or start the helper inside the existing Annex desktop.", state == HostState.Unavailable || state == HostState.Checking ? 0 : 48989));
        form.ShowGuidance("Preview mode", "This is a synthetic GUI preview. No Windows session, credentials, streaming host, or system settings are accessed.");
        form.StartHostRequested += delegate { form.SetHost(new HostSnapshot(HostState.Running, "Preview: Sunshine would be started only after confirmation in the real launcher.", 48989)); };
        form.ConnectRequested += delegate { form.SetNotice("The desktop action is simulated."); };
        form.ReconnectRequested += delegate { form.SetNotice("Reconnect is simulated."); };
        form.SettingsRequested += delegate { form.SetNotice("Settings would open on the validated loopback URL."); };
        form.GuideRequested += delegate { form.SetNotice("Setup is separate and requires administrator rights."); };
        form.MinimizeRequested += delegate { form.WindowState = FormWindowState.Minimized; };
        return form;
    }
    // README screenshots: real windows, example data, window contents only (no title bar).
    public static int CaptureScreens(string directory, string desktopImage)
    {
        Directory.CreateDirectory(directory);
        Action<Form, string> save = (window, name) => {
            Application.DoEvents();
            using (var full = new Bitmap(window.Width, window.Height)) {
                window.DrawToBitmap(full, new Rectangle(Point.Empty, window.Size));
                Point origin = window.PointToScreen(Point.Empty);
                var client = new Rectangle(origin.X - window.Left, origin.Y - window.Top, window.ClientSize.Width, window.ClientSize.Height);
                using (var crop = full.Clone(client, full.PixelFormat)) crop.Save(Path.Combine(directory, name), ImageFormat.Png);
            }
        };
        using (var form = Create(HostState.Running)) {
            form.Text = "Annex"; form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
            form.ClientSize = new Size(1180, 720);
            form.SetSession("Connected", "The Annex desktop is attached. You can keep using your physical desktop.", true, true);
            form.SetHost(new HostSnapshot(HostState.Running, "Sunshine is running in the Annex desktop. Pair your phone, tablet or TV with this host.", 48989));
            form.ShowGuidance("Your stream's own desktop", "Games and apps run here while you keep using your normal desktop. Sunshine streams this one to your phone, tablet or TV.");
            form.LabelPreview = false; form.SetNotice("Ready. Your physical desktop stays available.");
            form.Show();
            if (desktopImage != null) {
                // Stands in for the RDP viewer: a sample desktop, scaled like SmartSizing does.
                var desktop = new PictureBox { Image = Image.FromFile(desktopImage), SizeMode = PictureBoxSizeMode.Zoom };
                form.AttachViewer(desktop); form.RevealViewer();
            }
            save(form, "launcher.png");
            ((Button)form.Controls.Find("PairingSteps", true)[0]).PerformClick();
            save(form, "pairing.png");
            form.Close();
        }
        using (var apps = new ChildAppWindow()) {
            apps.ShowInTaskbar = false; apps.StartPosition = FormStartPosition.Manual; apps.Location = new Point(-20000, -20000);
            apps.ClientSize = new Size(596, 463); // same size as the installer screenshot next to it
            apps.SetApps(new[] {
                new AgentApp { Id = new string('a', 64), Name = "Chrome", Path = @"C:\Program Files\Google\Chrome\Application\chrome.exe" },
                new AgentApp { Id = new string('b', 64), Name = "Edge", Path = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" },
                new AgentApp { Id = new string('c', 64), Name = "Steam", Path = @"C:\Program Files (x86)\Steam\steam.exe" } }, "");
            apps.Show(); save(apps, "apps.png"); apps.Close();
        }
        return 0;
    }

    public static int Capture(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (HostState state in new[] { HostState.Stopped, HostState.Running, HostState.Unavailable, HostState.Checking, HostState.Missing })
        using (var form = Create(state)) {
            if (state == HostState.Missing) form.SetDiscovery(new AgentReply { Status = new HostSnapshot(HostState.Missing, "Install Sunshine or Apollo to stream. You can keep using the Annex desktop.", 0) });
            if (state == HostState.Running) form.SetNotice(LauncherPolicy.SettingsFeedback(true));
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
            form.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, state.ToString().ToLowerInvariant() + ".png"), ImageFormat.Png); }
            if (state == HostState.Running) {
                ((Button)form.Controls.Find("PairingSteps", true)[0]).PerformClick(); Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, "pairing.png"), ImageFormat.Png); }
                form.ClientSize = new Size(900, 640); Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, "pairing-compact.png"), ImageFormat.Png); }
            }
            if (state == HostState.Stopped) {
                string selected = new string('a', 64);
                form.SetDiscovery(new AgentReply { SelectionId = selected, Hosts = new[] { new AgentHost { Id = selected, Name = "Apollo", Path = @"C:\Program Files\Apollo\sunshine.exe", Version = "0.4" }, new AgentHost { Id = new string('b', 64), Name = "Sunshine", Path = @"E:\Streaming host with a long folder name\Sunshine\sunshine.exe", Version = "2026.914" } }, Status = new HostSnapshot(HostState.Stopped, "The host is off. Start it when you want to stream this desktop.", 48989) });
                form.SetNotice(LauncherPolicy.SettingsFeedback(false));
                form.ClientSize = new Size(900, 640); Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, "compact.png"), ImageFormat.Png); }
                form.Scale(new SizeF(1.5f, 1.5f)); Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, "large-layout.png"), ImageFormat.Png); }
            }
            form.Close();
        }
        TextBox password;
        using (var prompt = Program.PasswordForm(@"GAMING-PC\player", out password)) {
            prompt.ShowInTaskbar = false; prompt.StartPosition = FormStartPosition.Manual; prompt.Location = new Point(-20000, -20000); prompt.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(prompt.Width, prompt.Height)) { prompt.DrawToBitmap(bitmap, new Rectangle(Point.Empty, prompt.Size)); bitmap.Save(Path.Combine(directory, "password.png"), ImageFormat.Png); }
            prompt.Close();
        }
        using (var dialog = AnnexDialog.Build("End the Annex session?", "This signs out the Annex desktop. Games and apps running in it will close. Annex stays open so you can start a new one.", "Sign out", "Cancel", ButtonKind.Danger, Theme.Bad)) {
            dialog.ShowInTaskbar = false; dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000); dialog.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(directory, "dialog.png"), ImageFormat.Png); }
            dialog.Close();
        }
        using (var apps = new ChildAppWindow()) {
            apps.ShowInTaskbar = false; apps.StartPosition = FormStartPosition.Manual; apps.Location = new Point(-20000, -20000); apps.SetApps(new[] { new AgentApp { Id = new string('a', 64), Name = "Chrome", Path = @"C:\Program Files\Google\Chrome\Application\chrome.exe" }, new AgentApp { Id = new string('b', 64), Name = "Wallpaper Engine", Path = @"C:\Program Files (x86)\Steam\steamapps\common\wallpaper_engine\wallpaper64.exe" } }, "Browsers use a separate Annex profile. Check the app's window in the Annex desktop after opening it."); apps.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(apps.Width, apps.Height)) { apps.DrawToBitmap(bitmap, new Rectangle(Point.Empty, apps.Size)); bitmap.Save(Path.Combine(directory, "apps.png"), ImageFormat.Png); }
            apps.Close();
        }
        return 0;
    }
}
