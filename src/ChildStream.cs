using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

[ComImport, Guid("302D8188-0052-4807-806A-362B628F9AC5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMsRdpExtendedSettings
{
    void put_Property([MarshalAs(UnmanagedType.BStr)] string name, ref object value);
    void get_Property([MarshalAs(UnmanagedType.BStr)] string name, out object value);
}

public class RdpBox : AxHost
{
    // Microsoft RDP Client Control (MsTscAx)
    public RdpBox() : base("8B918B82-7985-4C24-89DF-C33AD2BBFBCD") { }
    public object Ocx { get { return GetOcx(); } }
}

static class Program
{
    [DllImport("wtsapi32.dll", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    static extern bool WTSEnableChildSessions(bool enable);
    [DllImport("wtsapi32.dll", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    static extern bool WTSIsChildSessionsEnabled(out bool enabled);
    [DllImport("wtsapi32.dll", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    static extern bool WTSGetChildSessionId(out uint sessionId);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSLogoffSession(IntPtr server, uint sessionId, bool wait);
    [DllImport("kernel32.dll")]
    static extern uint WTSGetActiveConsoleSessionId();

    static string baseDir = AppDomain.CurrentDomain.BaseDirectory;
    static string logPath = Path.Combine(baseDir, "launcher.log");
    static string credPath = Path.Combine(baseDir, "cred.bin");

    static void Log(string msg)
    {
        try { File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine); } catch { }
    }

    static string LoadPassword()
    {
        try
        {
            if (File.Exists(credPath))
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(credPath), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) { Log("cred load failed: " + ex.Message); }
        return null;
    }

    static void SavePassword(string pw)
    {
        try { File.WriteAllBytes(credPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(pw), null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) { Log("cred save failed: " + ex.Message); }
    }

    static bool ClearPassword()
    {
        try { File.Delete(credPath); return true; }
        catch (Exception ex) { Log("cred delete failed: " + ex.Message); return false; }
    }

    static uint GetChildSessionId()
    {
        uint sessionId;
        if (!WTSGetChildSessionId(out sessionId)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return sessionId;
    }

    static void LogoffChildSession(uint sessionId)
    {
        if (!WTSLogoffSession(IntPtr.Zero, sessionId, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
        Log("signout requested for child session " + sessionId);
    }

    static string PromptPassword(string account)
    {
        using (var d = new Form { Text = "Child Session - one-time setup", Width = 440, Height = 190, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false })
        {
            var lbl = new Label { Text = "Enter the Windows password for " + account + "\n(stored DPAPI-encrypted, used to auto-login the child session):", Left = 12, Top = 12, Width = 400, Height = 36 };
            var tb = new TextBox { Left = 12, Top = 56, Width = 398, UseSystemPasswordChar = true };
            var ok = new Button { Text = "Save", Left = 246, Top = 92, Width = 80, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 332, Top = 92, Width = 80, DialogResult = DialogResult.Cancel };
            d.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
            d.AcceptButton = ok; d.CancelButton = cancel;
            return d.ShowDialog() == DialogResult.OK && tb.Text.Length > 0 ? tb.Text : null;
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-enable")
        {
            bool ok = WTSEnableChildSessions(true);
            int err = Marshal.GetLastWin32Error();
            bool en1;
            bool checkedState = WTSIsChildSessionsEnabled(out en1);
            Log("WTSEnableChildSessions(true) => " + ok + " (gle=" + err + "), enabled now = " + en1);
            return ok && checkedState && en1 ? 0 : 1;
        }
        if (args.Length > 0 && args[0] == "-check")
        {
            bool en2;
            bool checkedState = WTSIsChildSessionsEnabled(out en2);
            Log("child sessions enabled = " + en2);
            return checkedState && en2 ? 0 : 1;
        }
        if (args.Length > 0 && args[0] == "-resetpass") return ClearPassword() ? 0 : 1;

        bool en;
        bool stateKnown = WTSIsChildSessionsEnabled(out en);
        Log("startup: child sessions enabled = " + en);
        if (!stateKnown || !en)
        {
            MessageBox.Show("Child sessions are not enabled.\nRun once from an elevated prompt:  ChildStream.exe -enable", "ChildStream");
            return 1;
        }

        Application.EnableVisualStyles();

        string account = Environment.MachineName + @"\" + Environment.UserName;
        string pw = LoadPassword();
        if (pw == null)
        {
            pw = PromptPassword(account);
            if (pw == null) return 1;
            SavePassword(pw);
        }

        bool endRequested = false;
        var f = new Form { Text = "Child Session", Width = 1360, Height = 820 };
        Icon appIcon = SystemIcons.Application;
        try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        f.Icon = appIcon;
        var tray = new NotifyIcon { Icon = appIcon, Visible = true, Text = "Child Session" };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (s, e) => { f.Show(); f.WindowState = FormWindowState.Normal; f.Activate(); });
        ToolStripMenuItem reconnectItem = new ToolStripMenuItem("Reconnect now");
        menu.Items.Add(reconnectItem);
        menu.Items.Add("Forget saved password", null, (s, e) => {
            bool cleared = ClearPassword();
            tray.ShowBalloonTip(2000, "Child Session", cleared ? "Password cleared. Restart the app." : "Could not clear the password. Check file permissions and launcher.log.", cleared ? ToolTipIcon.Info : ToolTipIcon.Error);
        });
        menu.Items.Add("End session (sign out, closes games)", null, (s, e) =>
        {
            if (MessageBox.Show("Sign out the child session? All apps running in it will close.", "Child Session", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (endRequested) return;
            endRequested = true;
            try
            {
                var sessions = new ChildSessionController(GetChildSessionId, LogoffChildSession);
                using (var process = Process.GetCurrentProcess())
                    sessions.EndSession((uint)process.SessionId, WTSGetActiveConsoleSessionId());
            }
            catch (Exception ex)
            {
                endRequested = false;
                Log("signout failed: " + ex.Message);
                MessageBox.Show("Could not sign out the child session.\n" + ex.Message, "Child Session", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            reconnectItem.Enabled = false;
            var quitTimer = new Timer { Interval = 3000 };
            quitTimer.Tick += delegate { quitTimer.Stop(); quitTimer.Dispose(); tray.Visible = false; Application.Exit(); };
            quitTimer.Start();
        });
        menu.Items.Add("Exit", null, (s, e) => { tray.Visible = false; Application.Exit(); });
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (s, e) => { f.Show(); f.WindowState = FormWindowState.Normal; f.Activate(); };

        f.Resize += (s, e) => { if (f.WindowState == FormWindowState.Minimized) { f.Hide(); tray.ShowBalloonTip(1500, "Child Session", "Still running - session stays alive. Double-click to restore.", ToolTipIcon.Info); } };
        f.Show();

        var rdp = new RdpBox { Dock = DockStyle.Fill };
        f.Controls.Add(rdp);
        rdp.CreateControl();
        dynamic ocx = rdp.Ocx;
        var displayScale = new RdpScaleSettings(
            name => {
                object value;
                ((IMsRdpExtendedSettings)rdp.Ocx).get_Property(name, out value);
                return value;
            },
            (name, value) => { ((IMsRdpExtendedSettings)rdp.Ocx).put_Property(name, ref value); });

        bool wantReconnect = true;
        Action connect = () =>
        {
            try
            {
                ocx.Server = "localhost";
                ocx.UserName = account;
                ocx.AdvancedSettings2.ClearTextPassword = pw;
                DisplaySettings display = DisplaySettings.Parse(null);
                try
                {
                    string cfg = Path.Combine(baseDir, "display.cfg");
                    if (File.Exists(cfg))
                    {
                        display = DisplaySettings.Parse(File.ReadAllText(cfg));
                    }
                }
                catch (Exception ex) { Log("display.cfg: " + ex.Message); }
                ocx.DesktopWidth = display.Width;
                ocx.DesktopHeight = display.Height;
                try { ocx.AdvancedSettings2.SmartSizing = true; } catch { }
                try
                {
                    displayScale.Apply(display.Scale);
                    if (display.Scale > 0) Log("scale " + display.Scale + "% applied");
                }
                catch (Exception ex) { Log("scale: " + ex.Message); }
                try { ocx.ColorDepth = 32; } catch { }
                try { ocx.AdvancedSettings7.EnableCredSspSupport = true; } catch { }
                object v = true;
                var ext = (IMsRdpExtendedSettings)rdp.Ocx;
                ext.put_Property("ConnectToChildSession", ref v);
                Log("connecting as " + account + "...");
                ocx.Connect();
            }
            catch (Exception ex) { Log("connect failed: " + ex.Message); }
        };

        int last = -1;
        DateTime lastAttempt = DateTime.MinValue;
        reconnectItem.Click += (s, e) => {
            if (endRequested) return;
            try
            {
                if ((int)ocx.Connected != 0) ocx.Disconnect();
                else { lastAttempt = DateTime.UtcNow; connect(); }
            }
            catch (Exception ex) { Log("reconnect: " + ex.Message); }
        };
        var timer = new Timer { Interval = 2000 };
        timer.Tick += delegate
        {
            try
            {
                int state = (int)ocx.Connected;
                if (state != last) { last = state; Log("state -> " + state); f.Text = state == 1 ? "Child Session - connected" : "Child Session - state " + state; }
                if (state == 0 && wantReconnect && !endRequested && (DateTime.UtcNow - lastAttempt).TotalSeconds > 5)
                {
                    lastAttempt = DateTime.UtcNow;
                    Log("auto-reconnect");
                    connect();
                }
            }
            catch (Exception ex) { Log("poll: " + ex.Message); }
        };
        timer.Start();

        f.FormClosing += (s, e) => { wantReconnect = false; tray.Visible = false; };
        lastAttempt = DateTime.UtcNow;
        connect();
        Application.Run(f);
        timer.Dispose();
        tray.Dispose();
        menu.Dispose();
        return 0;
    }
}



