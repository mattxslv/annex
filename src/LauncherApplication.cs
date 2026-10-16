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
    uint child;
    long lastAttempt;
    bool wantReconnect, ending, inFlight, promptOpen, setupReady;
    HostSnapshot host = new HostSnapshot(HostState.Waiting, "Start the child desktop to check its Sunshine instance.", 0);

    public LauncherApplication(string baseDirectory)
    {
        root = baseDirectory; client = new AgentClient(root); window = new LauncherWindow(false);
        Icon icon = SystemIcons.Application;
        try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        window.Icon = icon;
        tray = new NotifyIcon { Icon = icon, Visible = true, Text = "ChildStream" };
        menu.Items.Add("Show ChildStream", null, delegate { Show(); });
        menu.Items.Add("Reconnect", null, delegate { Reconnect(); });
        menu.Items.Add("Forget saved password", null, delegate {
            bool cleared = Program.ClearPassword();
            tray.ShowBalloonTip(2000, "ChildStream", cleared ? "Saved password removed. It will be requested next time you launch." : "Could not remove cred.bin. Check its file permissions and launcher.log.", cleared ? ToolTipIcon.Info : ToolTipIcon.Error);
        });
        menu.Items.Add("End session (closes games)", null, delegate { EndSession(); });
        menu.Items.Add("Exit viewer", null, delegate { window.Close(); });
        tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { Show(); };
        window.ConnectRequested += delegate { Connect(); };
        window.ReconnectRequested += delegate { Reconnect(); };
        window.StartHostRequested += delegate { ConfirmStart(); };
        window.SettingsRequested += delegate {
            string url = LauncherPolicy.SettingsUrl(host);
            if (url != null && StillConnected(child)) TryOpen(url);
        };
        window.GuideRequested += delegate {
            string readme = Path.Combine(root, "README.md");
            TryOpen(File.Exists(readme) ? readme : "https://github.com/ocr1n/childstream/blob/review/reliability/README.md#setup");
        };
        window.EndSessionRequested += delegate { EndSession(); };
        window.MinimizeRequested += delegate { window.WindowState = FormWindowState.Minimized; };
        window.Resize += delegate {
            if (window.WindowState != FormWindowState.Minimized) return;
            window.Hide(); tray.ShowBalloonTip(1500, "ChildStream", "Still attached. Double-click the tray icon to restore the viewer.", ToolTipIcon.Info);
        };
        window.FormClosing += delegate { ending = true; wantReconnect = false; requests.Close(); timer.Stop(); tray.Visible = false; };
        timer.Tick += delegate { Poll(); };
        bool enabled;
        bool known = Program.ChildSessionsEnabled(out enabled);
        setupReady = known && enabled;
        if (!setupReady) {
            window.SetSession("Setup required", known ? "Windows child sessions are not enabled yet." : "Windows could not report child-session support. Check launcher.log.", false, false);
            window.ShowGuidance("One-time setup required", "Extract the portable ZIP, then run scripts\\setup.ps1 as administrator. It installs portable Sunshine and enables the child desktop. Reopen ChildStream afterward.");
            window.SetHost(new HostSnapshot(HostState.Missing, "Setup is required before this app can create a child desktop or check its Sunshine instance.", 0));
        }
    }

    public int Run() { timer.Start(); Application.Run(window); return 0; }
    void Show() { window.Show(); window.WindowState = FormWindowState.Normal; window.Activate(); }
    void TryOpen(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { window.SetNotice("Could not open it: " + ex.Message); }
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
            if (password == null) { window.SetNotice("Connection canceled. No child desktop was opened."); return; }
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
            ocx.Connect();
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
        if (state == 1) window.SetSession("Connected", "The child desktop is attached. You can keep using your physical desktop.", true, true);
        else if (state != 0) window.SetSession("Connecting...", "Windows is opening the second desktop.", false, true);
        else if (wantReconnect) window.SetSession("Reconnecting...", "The viewer disconnected. Your child session may still be running.", false, true);
        if (state == 1 && child != 0 && !inFlight && !promptOpen) RequestHost("STATUS");
        if (state == 0 && wantReconnect && clock.ElapsedMilliseconds - lastAttempt > 6000) Connect();
    }
    async void RequestHost(string command)
    {
        if (ending || inFlight || !StillConnected(child)) return;
        uint requestedChild = child; int token = requests.Generation;
        inFlight = true; window.SetBusy(true); bool ask = false;
        try {
            HostSnapshot result = await Task.Run(() => client.Request(requestedChild, command));
            if (!window.IsDisposed && requests.Accepts(token, requestedChild) && StillConnected(requestedChild)) {
                host = result; window.SetHost(result);
                ask = command == "STATUS" && prompts.ShouldAsk(requestedChild, result.State, true);
            }
        }
        catch (Exception ex) { if (!window.IsDisposed && !ending) window.SetNotice("Host check failed: " + ex.Message); }
        finally { inFlight = false; if (!window.IsDisposed) window.SetBusy(false); }
        if (ask && !ending && !window.IsDisposed) ConfirmStart();
    }
    void ConfirmStart()
    {
        uint approvedChild = child;
        if (promptOpen || inFlight || !StillConnected(approvedChild) || !LauncherPolicy.CanStartHost(host, true)) return;
        promptOpen = true;
        DialogResult answer;
        try { answer = MessageBox.Show(window, "Sunshine is off or could not start in the child desktop.\n\nStart Sunshine there now? Windows may ask you to approve elevation inside that desktop.", "Start Sunshine?", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2); }
        finally { promptOpen = false; }
        if (answer == DialogResult.Yes && approvedChild == child && StillConnected(approvedChild)) RequestHost("START");
    }
    void EndSession()
    {
        if (ending || child == 0) return;
        if (MessageBox.Show(window, "Sign out the child desktop? All games and applications in it will close.", "End child session", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        bool previousReconnect = wantReconnect; ending = true; wantReconnect = false; requests.Update(0, false); window.SetEnding(true);
        try {
            new ChildSessionController(Program.GetChildSessionId, Program.LogoffChildSession).EndSession((uint)HostNative.CurrentSession(), HostNative.WTSGetActiveConsoleSessionId());
            requests.Close(); window.SetNotice("Sign-out requested. Closing the viewer without reconnecting.");
            var quit = new Timer { Interval = 3000 }; quit.Tick += delegate { quit.Stop(); quit.Dispose(); window.Close(); }; quit.Start();
        }
        catch (Exception ex) {
            ending = false; wantReconnect = previousReconnect; window.SetEnding(false); Program.Log("signout failed: " + ex.Message);
            MessageBox.Show(window, "Could not sign out the child desktop.\n" + ex.Message, "ChildStream", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    public void Dispose() { requests.Close(); timer.Dispose(); tray.Dispose(); menu.Dispose(); window.Dispose(); }
}
