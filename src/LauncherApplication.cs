using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed class LauncherApplication : IDisposable
{
    readonly string root;
    readonly LauncherWindow window;
    readonly AgentClient client;
    readonly SessionRequestGate requests = new SessionRequestGate();
    readonly HostPromptPolicy prompts = new HostPromptPolicy();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Timer timer = new Timer { Interval = 2000 };
    readonly NotifyIcon tray;
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    RdpBox rdp;
    dynamic ocx;
    RdpScaleSettings scale;
    string password;
    string selection = "", selectedName = "host";
    ChildAppWindow appWindow;
    uint child;
    long lastAttempt;
    bool wantReconnect, ending, inFlight, promptOpen, setupReady, settingUp;
    HostSnapshot host = new HostSnapshot(HostState.Waiting, "Start the Annex desktop to check its Sunshine instance.", 0);

    public LauncherApplication(string baseDirectory)
    {
        root = baseDirectory; client = new AgentClient(root); window = new LauncherWindow(false);
        Icon icon = SystemIcons.Application;
        try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        window.Icon = icon;
        tray = new NotifyIcon { Icon = icon, Visible = true, Text = "Annex" };
        menu.Renderer = Theme.MenuRenderer();
        menu.Items.Add("Show Annex", null, delegate { Show(); });
        menu.Items.Add("Reconnect", null, delegate { Reconnect(); });
        menu.Items.Add("Forget saved password", null, delegate {
            bool cleared = Program.ClearPassword();
            tray.ShowBalloonTip(2000, "Annex", cleared ? "Saved password removed. It will be requested next time you launch." : "Could not remove cred.bin. Check its file permissions and launcher.log.", cleared ? ToolTipIcon.Info : ToolTipIcon.Error);
        });
        menu.Items.Add("End session (closes games)", null, delegate { EndSession(); });
        menu.Items.Add("Exit viewer", null, delegate { window.Close(); });
        tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { Show(); };
        window.ConnectRequested += delegate { Connect(); };
        window.ReconnectRequested += delegate { Reconnect(); };
        window.StartHostRequested += delegate { ConfirmStart(); };
        window.SettingsRequested += delegate {
            string url = LauncherPolicy.SettingsUrl(host);
            if (url != null && StillConnected(child) && TryOpen(url))
                window.SetNotice(LauncherPolicy.SettingsFeedback(false));
        };
        window.GuideRequested += delegate { SetupRepair(); };
        window.RefreshHostsRequested += delegate { RequestHost(new AgentRequest { Command = "STATUS" }); };
        window.HostSelected += id => { requests.Select(id); selection = id; RequestHost(new AgentRequest { Command = "SELECT_HOST", SelectionId = id }); };
        window.BrowseHostRequested += delegate { BrowseHost(); };
        window.OpenAppRequested += delegate { OpenApps(); };
        window.InstallRequested += name => {
            string url = name == "Sunshine" ? "https://github.com/LizardByte/Sunshine/releases" : name == "Apollo" ? "https://github.com/ClassicOldSong/Apollo/releases" : name == "VibePollo" ? "https://github.com/Nonary/Vibepollo/releases" : null;
            if (url != null) TryOpen(url);
        };
        window.EndSessionRequested += delegate { EndSession(); };
        window.MinimizeRequested += delegate { window.WindowState = FormWindowState.Minimized; };
        window.Resize += delegate {
            if (window.WindowState != FormWindowState.Minimized) return;
            window.Hide(); tray.ShowBalloonTip(1500, "Annex", "Still attached. Double-click the tray icon to restore the viewer.", ToolTipIcon.Info);
        };
        window.FormClosing += delegate { ending = true; wantReconnect = false; requests.Close(); timer.Stop(); tray.Visible = false; };
        window.Shown += delegate { DiscoverBeforeConnect(); };
        timer.Tick += delegate { Poll(); };
        bool enabled;
        bool known = Program.ChildSessionsEnabled(out enabled);
        setupReady = known && enabled;
        if (!setupReady) {
            window.SetSession("Setup required", known ? "Windows child sessions are not enabled yet." : "Windows could not report child-session support. Check launcher.log.", false, false);
            window.ShowGuidance("One-time desktop setup", "Run scripts\\setup.ps1 once: it enables the Windows Annex desktop and registers the Annex helper. Setup / repair re-applies the Windows settings later. Windows may ask for administrator approval. A streaming host is optional.");
            window.SetHost(new HostSnapshot(HostState.Waiting, "A streaming host is optional. Set up Windows, then start the desktop.", 0));
        }
    }

    public int Run() { timer.Start(); Application.Run(window); return 0; }
    void Show() { window.Show(); window.WindowState = FormWindowState.Normal; window.Activate(); }
    bool TryOpen(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); return true; }
        catch (Exception ex) { Program.Log("open failed: " + ex.Message); window.SetNotice("Could not open it: " + ex.Message + ". Copy the displayed URL and open it manually."); return false; }
    }
    int State()
    {
        try { return ocx == null ? 0 : (int)ocx.Connected; }
        catch (Exception ex) { Program.Log("RDP state: " + ex.Message); return 0; }
    }
    bool StillConnected(uint session)
    {
        if (ending || session == 0 || State() != 1) return false;
        uint actual;
        return HostNative.WTSGetChildSessionId(out actual) && actual == session && PeerIdentity.IsTarget(actual, (uint)HostNative.CurrentSession(), HostNative.WTSGetActiveConsoleSessionId());
    }
    void Connect()
    {
        if (ending || !setupReady) return;
        if (State() == 1) { Show(); rdp.Focus(); return; }
        if (State() != 0) return;
        string account = Environment.MachineName + @"\" + Environment.UserName;
        if (password == null) password = Program.LoadPassword();
        if (password == null) {
            password = Program.PromptPassword(account);
            if (password == null) { window.SetNotice("Connection canceled. No Annex desktop was opened."); return; }
            Program.SavePassword(password);
        }
        try {
            if (rdp == null) {
                rdp = new RdpBox(); window.AttachViewer(rdp); rdp.CreateControl(); ocx = rdp.Ocx;
                scale = new RdpScaleSettings(name => { object value; ((IMsRdpExtendedSettings)rdp.Ocx).get_Property(name, out value); return value; }, (name, value) => { ((IMsRdpExtendedSettings)rdp.Ocx).put_Property(name, ref value); });
            }
            ocx.Server = "localhost"; ocx.UserName = account; ocx.AdvancedSettings2.ClearTextPassword = password;
            DisplaySettings display = DisplaySettings.Parse(null);
            try { string path = Path.Combine(root, "display.cfg"); if (File.Exists(path)) display = DisplaySettings.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { Program.Log("display.cfg: " + ex.Message); }
            ocx.DesktopWidth = display.Width; ocx.DesktopHeight = display.Height;
            try { ocx.AdvancedSettings2.SmartSizing = true; } catch { }
            try { scale.Apply(display.Scale); } catch (Exception ex) { Program.Log("scale: " + ex.Message); }
            try { ocx.ColorDepth = 32; } catch { }
            try { ocx.AdvancedSettings7.EnableCredSspSupport = true; } catch { }
            object valueChild = true; ((IMsRdpExtendedSettings)rdp.Ocx).put_Property("ConnectToChildSession", ref valueChild);
            lastAttempt = clock.ElapsedMilliseconds; wantReconnect = true;
            window.SetSession("Connecting...", "Opening the second Windows desktop. Keep the viewer attached during your stream.", false, true);
            RdpInputSettings.Connect(ocx, new Action<string>(message => { Program.Log(message); window.SetNotice(message); }));
        }
        catch (Exception ex) {
            wantReconnect = false; Program.Log("connect failed: " + ex);
            window.SetSession("Connection failed", ex.Message, false, true);
            window.SetNotice("Check your Windows password, session support, and launcher.log, then try again.");
            if (ocx == null && rdp != null) { rdp.Dispose(); rdp = null; }
        }
    }
    void Reconnect()
    {
        if (ending || !setupReady) return;
        requests.Update(0, false); child = 0;
        try { if (State() != 0) { wantReconnect = true; ocx.Disconnect(); lastAttempt = clock.ElapsedMilliseconds - 6001; } else Connect(); }
        catch (Exception ex) { window.SetNotice("Reconnect failed: " + ex.Message); }
    }
    void Poll()
    {
        if (ending || !setupReady) return;
        int state = State(); uint actual = 0;
        if (state == 1) {
            uint found;
            if (HostNative.WTSGetChildSessionId(out found) && PeerIdentity.IsTarget(found, (uint)HostNative.CurrentSession(), HostNative.WTSGetActiveConsoleSessionId())) actual = found;
        }
        HostSnapshot pending = LauncherPolicy.ForSession(host, child, actual);
        if (!Object.ReferenceEquals(host, pending)) { host = pending; window.SetHost(host); }
        requests.Update(actual, state == 1 && actual != 0); child = actual;
        if (appWindow != null && !appWindow.IsDisposed) appWindow.SetAvailable(actual != 0 && state == 1);
        if (state == 1) window.SetSession("Connected", "The Annex desktop is attached. You can keep using your physical desktop.", true, true);
        else if (state != 0) window.SetSession("Connecting...", "Windows is opening the second desktop.", false, true);
        else if (wantReconnect) window.SetSession("Reconnecting...", "The viewer disconnected. Your Annex desktop may still be running.", false, true);
        if (state == 1 && child != 0 && !inFlight && !promptOpen) RequestHost(new AgentRequest { Command = "STATUS" });
        if (state == 0 && wantReconnect && clock.ElapsedMilliseconds - lastAttempt > 6000) Connect();
    }
    async void RequestHost(AgentRequest request)
    {
        if (ending || inFlight || !StillConnected(child)) return;
        uint requestedChild = child; int token = requests.Generation;
        inFlight = true; window.SetBusy(true); bool ask = false;
        try {
            AgentReply result = await Task.Run(() => client.Request(requestedChild, request));
            if (!window.IsDisposed && requests.Accepts(token, requestedChild) && StillConnected(requestedChild)) {
                selection = result.SelectionId; requests.Select(selection); selectedName = "host";
                foreach (AgentHost item in result.Hosts) if (item.Id == selection) selectedName = item.Name;
                host = result.Status; window.SetDiscovery(result);
                ask = (request.Command == "STATUS" || request.Command == "SELECT_HOST") && (appWindow == null || appWindow.IsDisposed) && prompts.ShouldAsk(requestedChild, selection, host.State, true);
            }
        }
        catch (Exception ex) { if (!window.IsDisposed && !ending) window.SetNotice("Host check failed: " + ex.Message); }
        finally { inFlight = false; if (!window.IsDisposed) window.SetBusy(false); }
        if (ask && !ending && !window.IsDisposed) ConfirmStart();
    }
    void ConfirmStart()
    {
        var approval = new SessionConfirmation(requests, child);
        string approvedSelection = selection;
        if (string.IsNullOrEmpty(approvedSelection) || promptOpen || inFlight || !approval.CanProceed(child) || !StillConnected(approval.Child) || !LauncherPolicy.CanStartHost(host, true)) return;
        promptOpen = true;
        DialogResult answer;
        try { answer = MessageBox.Show(window, "Start " + selectedName + " inside the Annex desktop?\n\nThis uses Annex's own settings and ports. Windows may ask you to approve elevation inside that desktop.", "Start " + selectedName + "?", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2); }
        finally { promptOpen = false; }
        if (answer == DialogResult.Yes) {
            if (approvedSelection == selection && approval.CanProceed(child) && StillConnected(approval.Child)) RequestHost(new AgentRequest { Command = "START_HOST", SelectionId = approvedSelection });
            else window.SetNotice("The desktop or host selection changed. Review its status and confirm startup again.");
        }
    }
    async void DiscoverBeforeConnect() {
        try {
            var found = await Task.Run(() => HostDiscovery.Find(root, new NativeHostDiscovery()));
            if (ending || window.IsDisposed || child != 0 || State() != 0) return;
            window.SetDiscovery(new AgentReply { Status = LauncherPolicy.DiscoverySnapshot(found, false), Diagnostic = HostInstallation.Limit(string.Join(" ", found.Diagnostics), 512) });
            string registered = InstallationIdentity.Load(); if (registered != null && !InstallationIdentity.SameRoot(registered, root)) window.SetNotice("The helper is registered for " + registered + ". Open that folder's EXE, or use Setup / repair to register this folder.");
        } catch (Exception ex) { if (!window.IsDisposed) window.SetNotice("Host discovery did not finish: " + ex.Message); }
    }
    async void SetupRepair() {
        if (settingUp || ending) return; settingUp = true;
        try {
            using (var process = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "-setup") { UseShellExecute = true, WorkingDirectory = root, WindowStyle = ProcessWindowStyle.Hidden })) {
                if (process == null) throw new IOException("Windows did not start setup."); await Task.Run(() => process.WaitForExit());
                if (ending || window.IsDisposed) return;
                bool enabled; setupReady = process.ExitCode == 0 && Program.ChildSessionsEnabled(out enabled) && enabled;
                if (setupReady && State() == 0) { window.SetSession("Ready", "Windows desktop setup is ready. A streaming host is optional.", false, true); window.ShowGuidance("Ready for a second desktop", "Choose Start desktop. If a helper is already running from an older EXE, restart it inside the Annex desktop after saving your work."); }
                window.SetNotice(setupReady ? "Setup is ready. Existing Annex desktops may need their helper restarted; your apps were left open." : "Setup did not finish. Review the setup message and launcher.log, then retry.");
            }
        } catch (Exception ex) { if (!window.IsDisposed) window.SetNotice("Setup failed: " + ex.Message); }
        finally { settingUp = false; }
    }
    void BrowseHost() {
        if (inFlight || !StillConnected(child)) return;
        using (var picker = new OpenFileDialog { Title = "Choose Sunshine, Apollo or VibePollo", Filter = "Streaming host executable (*.exe)|*.exe", CheckFileExists = true }) {
            var approval = new SessionConfirmation(requests, child);
            if (picker.ShowDialog(window) != DialogResult.OK || !approval.CanProceed(child) || !StillConnected(child)) return;
            try { var chosen = HostDiscovery.FromExecutable(picker.FileName, new NativeHostDiscovery()); HostSelectionStore.Save(Path.Combine(root, "host-selection.txt"), chosen.Id); requests.Select("browsed:" + chosen.Id); selection = ""; RequestHost(new AgentRequest { Command = "STATUS" }); }
            catch (Exception ex) { window.SetNotice(ex.Message); }
        }
    }
    void OpenApps() {
        if (!StillConnected(child)) return;
        if (appWindow != null && !appWindow.IsDisposed) { appWindow.Activate(); return; }
        appWindow = new ChildAppWindow(); appWindow.RefreshRequested += delegate { RequestApps(new AgentRequest { Command = "LIST_APPS" }); };
        appWindow.LaunchRequested += id => RequestApps(new AgentRequest { Command = "LAUNCH_APP", AppId = id });
        appWindow.BrowseRequested += delegate {
            var approval = new SessionConfirmation(requests, child);
            using (var picker = new OpenFileDialog { Title = "Choose an installed app or game", Filter = "Windows application (*.exe)|*.exe", CheckFileExists = true })
                if (picker.ShowDialog(appWindow) == DialogResult.OK && approval.CanProceed(child) && StillConnected(child)) RequestApps(new AgentRequest { Command = "REGISTER_APP", ExecutablePath = picker.FileName });
        };
        appWindow.Show(window); RequestApps(new AgentRequest { Command = "LIST_APPS" });
    }
    async void RequestApps(AgentRequest request) {
        if (ending || appWindow == null || appWindow.IsDisposed || !StillConnected(child)) return;
        if (inFlight) { appWindow.SetApps(new AgentApp[0], "A host/app check is in progress. Choose Refresh apps in a moment."); return; }
        uint target = child; int token = requests.Generation; var picker = appWindow; inFlight = true; window.SetBusy(true);
        try {
            AgentReply reply = await Task.Run(() => client.Request(target, request));
            if (!picker.IsDisposed && requests.Accepts(token, target) && StillConnected(target)) picker.SetApps(reply.Apps, reply.Status.State == HostState.Unavailable ? reply.Status.Message : reply.Diagnostic);
        } catch (Exception ex) { if (!picker.IsDisposed) picker.SetApps(new AgentApp[0], ex.Message); }
        finally { inFlight = false; if (!window.IsDisposed) window.SetBusy(false); }
    }
    void EndSession()
    {
        var approval = new SessionConfirmation(requests, child);
        if (ending || promptOpen || !approval.CanProceed(child) || !StillConnected(approval.Child)) return;
        promptOpen = true;
        DialogResult answer;
        try { answer = MessageBox.Show(window, "Sign out the Annex desktop? All games and applications in it will close.", "End Annex session", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2); }
        finally { promptOpen = false; }
        if (answer != DialogResult.Yes) return;
        if (!approval.CanProceed(child) || !StillConnected(approval.Child)) {
            window.SetNotice("The desktop connection changed. Review it and choose End session again."); return;
        }
        bool previousReconnect = wantReconnect; ending = true; wantReconnect = false; requests.Update(0, false); window.SetEnding(true);
        try {
            new ChildSessionController(Program.GetChildSessionId, Program.LogoffChildSession).EndConfirmedSession(approval.Child, (uint)HostNative.CurrentSession(), HostNative.WTSGetActiveConsoleSessionId());
            requests.Close(); window.SetNotice("Sign-out requested. Closing the viewer without reconnecting.");
            var quit = new Timer { Interval = 3000 }; quit.Tick += delegate { quit.Stop(); quit.Dispose(); window.Close(); }; quit.Start();
        }
        catch (Exception ex) {
            ending = false; wantReconnect = previousReconnect; window.SetEnding(false); Program.Log("signout failed: " + ex.Message);
            MessageBox.Show(window, "Could not sign out the Annex desktop.\n" + ex.Message, "Annex", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    public void Dispose() { requests.Close(); timer.Dispose(); tray.Dispose(); menu.Dispose(); window.Dispose(); }
}
