using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

sealed class LauncherWindow : Form
{
    readonly Color ink = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(24, 40, 56);
    readonly Color muted = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(82, 101, 121);
    readonly Color blue = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(33, 92, 221);
    readonly Label sessionTitle, sessionDetail, hostTitle, hostDetail;
    readonly Button desktop, reconnect, startHost, settings, endSession;
    readonly TextBox address;
    readonly Label placeholderTitle, placeholderBody;
    readonly Panel workspace, placeholder;
    readonly ToolStripStatusLabel footer;
    readonly ToolTip tips = new ToolTip();
    HostSnapshot host = new HostSnapshot(HostState.Waiting, "Start the child desktop to check its Sunshine instance.", 0);
    bool connected, busy, ending, setupReady = true;
    public event EventHandler ConnectRequested, ReconnectRequested, StartHostRequested, SettingsRequested, EndSessionRequested, GuideRequested, MinimizeRequested;
    public bool Preview { get; private set; }

    public LauncherWindow(bool preview)
    {
        Preview = preview;
        Text = preview ? "ChildStream - Preview" : "ChildStream";
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f); ForeColor = ink;
        BackColor = SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(239, 243, 248);
        ClientSize = new Size(1180, 760); MinimumSize = new Size(900, 640);
        StartPosition = FormStartPosition.CenterScreen;

        var statusBar = new StatusStrip { SizingGrip = true };
        footer = new ToolStripStatusLabel(preview ? "Preview mode - synthetic data, no system changes" : "Ready. Your physical desktop stays available.") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        statusBar.Items.Add(footer); Controls.Add(statusBar);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = BackColor };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout); layout.BringToFront();
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = SystemColors.Window, Margin = Padding.Empty };
        var rail = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(24, 22, 24, 22), Margin = Padding.Empty };
        rail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(rail); layout.Controls.Add(scroll, 0, 0);
        Action<Control> add = control => { int row = rail.RowCount++; rail.RowStyles.Add(new RowStyle(SizeType.AutoSize)); rail.Controls.Add(control, 0, row); };
        add(Label("ChildStream", 21, FontStyle.Bold, ink, 0, 22));
        add(Label("Second desktop", 10, FontStyle.Bold, muted, 0, 5));
        sessionTitle = Label("Not connected", 14, FontStyle.Bold, ink, 0, 6); add(sessionTitle);
        sessionDetail = Label("Start a separate desktop for your stream. Keep this viewer attached while playing.", 9.5f, FontStyle.Regular, muted, 0, 12); add(sessionDetail);
        desktop = Button("&Start desktop", true, (s, e) => Raise(ConnectRequested)); add(desktop);
        reconnect = Button("&Reconnect", false, (s, e) => Raise(ReconnectRequested)); reconnect.Enabled = false; add(reconnect);
        add(new Panel { Height = 1, Dock = DockStyle.Top, BackColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(217, 226, 236), Margin = new Padding(0, 17, 0, 17) });
        add(Label("Streaming host", 10, FontStyle.Bold, muted, 0, 5));
        hostTitle = Label("Waiting for desktop", 14, FontStyle.Bold, ink, 0, 6); add(hostTitle);
        hostDetail = Label(host.Message, 9.5f, FontStyle.Regular, muted, 0, 12); add(hostDetail);
        startHost = Button("Start &Sunshine", true, (s, e) => Raise(StartHostRequested)); add(startHost);
        settings = Button("Open Sunshine settings", false, (s, e) => Raise(SettingsRequested)); add(settings);
        address = new TextBox { ReadOnly = true, Text = "Available after host check", Width = 210, BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window, ForeColor = ink, Margin = new Padding(8, 5, 8, 0), AccessibleName = "Client pairing address" };
        tips.SetToolTip(address, "Pair from Moonlight or a compatible client. Use this PC's LAN IP with this port if its name does not resolve.");
        var copyAddress = Button("Copy address", false, (s, e) => {
            if (!HostConfiguration.IsValidPort(host.BasePort)) return;
            try { Clipboard.SetText(address.Text); SetNotice("Pairing address copied."); }
            catch (Exception) { SetNotice("Clipboard is busy. Select and copy the address manually."); }
        }); copyAddress.Dock = DockStyle.None;
        add(Button("Setup and help", false, (s, e) => Raise(GuideRequested)));

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(24), Margin = Padding.Empty };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.Controls.Add(right, 1, 0);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 14) };
        tools.Controls.Add(Label("Desktop preview", 11, FontStyle.Bold, ink, 8, 8));
        tools.Controls.Add(Button("Minimize to tray", false, (s, e) => Raise(MinimizeRequested)));
        endSession = Button("End session...", false, (s, e) => Raise(EndSessionRequested)); tools.Controls.Add(endSession); right.Controls.Add(tools, 0, 0);
        workspace = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Window, Margin = Padding.Empty };
        right.Controls.Add(workspace, 0, 1);
        placeholder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(38), BackColor = SystemColors.Window };
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); center.RowStyles.Add(new RowStyle(SizeType.AutoSize)); center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var message = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
        placeholderTitle = Label("Your stream's own desktop", 24, FontStyle.Bold, ink, 0, 14);
        placeholderBody = Label("Start a second Windows desktop for games and apps. Sunshine shares that desktop with your client while you keep using this one.", 11, FontStyle.Regular, muted, 0, 0);
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        message.Controls.Add(placeholderTitle); message.Controls.Add(placeholderBody); center.Controls.Add(message, 0, 1); placeholder.Controls.Add(center); workspace.Controls.Add(placeholder);
        Action fitPlaceholder = delegate {
            int width = Math.Max(200, placeholder.ClientSize.Width - placeholder.Padding.Horizontal - 8);
            placeholderTitle.MaximumSize = placeholderBody.MaximumSize = new Size(width, 0);
            message.MaximumSize = new Size(width, 0);
        };
        placeholder.Resize += delegate { fitPlaceholder(); }; Shown += delegate { fitPlaceholder(); };
        var pairing = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 14, 0, 0) };
        pairing.Controls.Add(Label("Client address", 9.5f, FontStyle.Bold, ink, 8, 0)); pairing.Controls.Add(address); pairing.Controls.Add(copyAddress); right.Controls.Add(pairing, 0, 2);
        SetHost(host); UpdateActions();
    }

    Label Label(string text, float size, FontStyle style, Color color, int top, int bottom)
    {
        return new Label { Text = text, AutoSize = true, MaximumSize = new Size(252, 0), Font = new Font("Segoe UI", size, style), ForeColor = color, Margin = new Padding(0, top, 0, bottom), UseMnemonic = false };
    }
    Button Button(string text, bool primary, EventHandler action)
    {
        var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, primary ? 38 : 30), Dock = DockStyle.Top, Padding = new Padding(10, 3, 10, 3), Margin = new Padding(0, 0, 6, 6), FlatStyle = SystemInformation.HighContrast ? FlatStyle.Standard : FlatStyle.Flat, UseVisualStyleBackColor = SystemInformation.HighContrast, AccessibleName = text.Replace("&", "") };
        button.Tag = primary;
        button.EnabledChanged += delegate { StyleButton(button); };
        StyleButton(button);
        button.Click += action; return button;
    }
    void StyleButton(Button button)
    {
        if (SystemInformation.HighContrast) return;
        bool primary = button.Tag is bool && (bool)button.Tag;
        button.BackColor = !button.Enabled ? Color.FromArgb(233, 238, 244) : primary ? blue : SystemColors.Window;
        button.ForeColor = !button.Enabled ? muted : primary ? Color.White : ink;
        button.FlatAppearance.BorderColor = !button.Enabled ? Color.FromArgb(217, 226, 236) : primary ? blue : Color.FromArgb(195, 207, 221);
    }
    void Raise(EventHandler action) { if (action != null) action(this, EventArgs.Empty); }
    void UpdateActions()
    {
        desktop.Enabled = setupReady && !ending; reconnect.Enabled = connected && !ending;
        startHost.Visible = host.State != HostState.Running && host.State != HostState.Waiting && host.State != HostState.Checking;
        startHost.Enabled = !busy && !ending && LauncherPolicy.CanStartHost(host, connected);
        settings.Visible = host.State == HostState.Running;
        settings.Enabled = connected && !busy && !ending && LauncherPolicy.SettingsUrl(host) != null;
        desktop.Tag = !connected; StyleButton(desktop);
        endSession.Enabled = connected && !ending && !Preview;
    }
    public void SetBusy(bool value) { busy = value; UpdateActions(); }
    public void SetEnding(bool value) { ending = value; UpdateActions(); }
    public void SetNotice(string message) { footer.Text = Preview ? "Preview mode - " + message : message; }
    public void SetSession(string title, string detail, bool attached, bool ready)
    {
        connected = attached; setupReady = ready; sessionTitle.Text = title; sessionDetail.Text = detail;
        desktop.Text = attached ? "Show &desktop" : "&Start desktop";
        UpdateActions();
    }
    public void SetHost(HostSnapshot value)
    {
        host = value;
        hostTitle.Text = value.State == HostState.Running ? "Sunshine running" : value.State == HostState.Stopped ? "Sunshine is off" : value.State == HostState.Missing ? "Setup required" : value.State == HostState.Unavailable ? "Helper unavailable" : value.State == HostState.Starting ? "Starting Sunshine" : value.State == HostState.Checking ? "Checking Sunshine" : value.State == HostState.Error ? "Needs attention" : value.State == HostState.Unknown ? "Status unknown" : "Waiting for desktop";
        hostTitle.ForeColor = SystemInformation.HighContrast ? ink : value.State == HostState.Running ? Color.FromArgb(20, 104, 71) : value.State == HostState.Error || value.State == HostState.Missing ? Color.FromArgb(180, 35, 53) : ink;
        hostDetail.Text = value.Message.Length > 230 ? value.Message.Substring(0, 227) + "..." : value.Message;
        hostDetail.AccessibleDescription = value.Message; tips.SetToolTip(hostDetail, value.Message);
        address.Text = HostConfiguration.IsValidPort(value.BasePort) ? (Preview ? "gaming-pc" : Environment.MachineName) + ":" + value.BasePort : "Available after host check";
        UpdateActions();
    }
    public void ShowGuidance(string title, string body)
    {
        placeholderTitle.Text = title; placeholderBody.Text = body; placeholder.BringToFront();
    }
    public void AttachViewer(Control viewer)
    {
        viewer.Dock = DockStyle.Fill; workspace.Controls.Add(viewer); viewer.BringToFront();
    }
    protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
}

static class LauncherPreview
{
    public static LauncherWindow Create(HostState state)
    {
        var form = new LauncherWindow(true);
        form.SetSession("Connected", "Preview of a separate Windows desktop. No real session is connected.", true, true);
        form.SetHost(new HostSnapshot(state, state == HostState.Running ? "Sunshine is running in the child desktop. Pair your compatible client with this host." : state == HostState.Stopped ? "Sunshine is off in the child desktop. Start it to make this desktop available to your client." : state == HostState.Checking ? "Checking Sunshine in the attached child desktop..." : "Child helper unavailable. Run setup and reconnect a newly signed-in child desktop, or start the helper inside the existing child desktop.", state == HostState.Unavailable || state == HostState.Checking ? 0 : 48989));
        form.ShowGuidance("Preview mode", "This is a synthetic GUI preview. No Windows session, credentials, streaming host, or system settings are accessed.");
        form.StartHostRequested += delegate { form.SetHost(new HostSnapshot(HostState.Running, "Preview: Sunshine would be started only after confirmation in the real launcher.", 48989)); };
        form.ConnectRequested += delegate { form.SetNotice("The desktop action is simulated."); };
        form.ReconnectRequested += delegate { form.SetNotice("Reconnect is simulated."); };
        form.SettingsRequested += delegate { form.SetNotice("Settings would open on the validated loopback URL."); };
        form.GuideRequested += delegate { form.SetNotice("Setup is separate and requires administrator rights."); };
        form.MinimizeRequested += delegate { form.WindowState = FormWindowState.Minimized; };
        return form;
    }
    public static int Capture(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (HostState state in new[] { HostState.Stopped, HostState.Running, HostState.Unavailable, HostState.Checking })
        using (var form = Create(state)) {
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
            form.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, state.ToString().ToLowerInvariant() + ".png"), ImageFormat.Png); }
            if (state == HostState.Stopped) {
                form.ClientSize = new Size(900, 640); Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, "compact.png"), ImageFormat.Png); }
                form.Scale(new SizeF(1.5f, 1.5f)); Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, "large-layout.png"), ImageFormat.Png); }
            }
            form.Close();
        }
        return 0;
    }
}
