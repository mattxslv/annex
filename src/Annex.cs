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
    [DllImport("user32.dll")] static extern void DisableProcessWindowsGhosting();
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

    internal static void Log(string msg)
    {
        try { File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine); } catch { }
    }

    internal static string LoadPassword()
    {
        try
        {
            if (File.Exists(credPath))
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(credPath), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) { Log("cred load failed: " + ex.Message); }
        return null;
    }

    internal static void SavePassword(string pw)
    {
        try { File.WriteAllBytes(credPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(pw), null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) { Log("cred save failed: " + ex.Message); }
    }

    internal static bool ClearPassword()
    {
        try { File.Delete(credPath); return true; }
        catch (Exception ex) { Log("cred delete failed: " + ex.Message); return false; }
    }

    internal static uint GetChildSessionId()
    {
        uint sessionId;
        if (!WTSGetChildSessionId(out sessionId)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return sessionId;
    }

    internal static void LogoffChildSession(uint sessionId)
    {
        if (!WTSLogoffSession(IntPtr.Zero, sessionId, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
        Log("signout requested for child session " + sessionId);
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool LogonUser(string user, string domain, string password, int logonType, int provider, out IntPtr token);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    // Windows' own check of the typed password, so a typo is caught before connecting (and never saved).
    // Only a definite "wrong user name or password" counts; anything else is left to the real sign-in.
    internal static bool PasswordRejected(string password)
    {
        IntPtr token;
        if (LogonUser(Environment.UserName, Environment.MachineName, password, 3 /* network */, 0, out token)) { CloseHandle(token); return false; }
        int error = Marshal.GetLastWin32Error();
        Log("password check: error " + error);
        return error == 1326;
    }

    internal static string PromptPassword(string account)
    {
        TextBox tb;
        using (var d = PasswordForm(account, out tb, PasswordRejected)) return d.ShowDialog() == DialogResult.OK && tb.Text.Length > 0 ? tb.Text : null;
    }

    internal static Form PasswordForm(string account, out TextBox tb, Func<string, bool> rejected = null)
    {
        var d = new Form { Text = "Annex - one-time setup", ClientSize = new Size(460, 222), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false, BackColor = Theme.Background, ForeColor = Theme.Text, Font = new Font("Segoe UI", 9.5f), AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96) };
        Theme.DarkChrome(d);
        var mark = new LogoMark { Left = 20, Top = 20, Size = new Size(40, 40) };
        var title = new Label { Text = "Sign in to the Annex desktop", Left = 72, Top = 20, AutoSize = true, Font = new Font("Segoe UI Semibold", 13), ForeColor = Theme.Text };
        var lbl = new Label { Text = "Windows password for " + account + ". Stored DPAPI-encrypted for this user only.", Left = 72, Top = 50, Width = 370, Height = 40, ForeColor = Theme.Muted };
        tb = new TextBox { UseSystemPasswordChar = true, AccessibleName = "Windows password" }; var box = tb;
        var field = new FieldBox(tb) { Left = 72, Top = 96, Width = 368 };
        var error = new Label { Left = 72, Top = 134, Width = 368, Height = 20, ForeColor = Theme.Bad, UseMnemonic = false };
        var ok = new AnnexButton { Text = "Save", Kind = ButtonKind.Primary, Left = 260, Top = 164, Size = new Size(88, 36) };
        var cancel = new AnnexButton { Text = "Cancel", Kind = ButtonKind.Secondary, Left = 352, Top = 164, Size = new Size(88, 36), DialogResult = DialogResult.Cancel };
        string refused = null;
        ok.Click += delegate {
            if (box.Text.Length == 0) { error.Text = "Enter your Windows password."; return; }
            // Saving the same text again overrides the check, in case it misjudges an account type.
            if (rejected != null && box.Text != refused && rejected(box.Text)) {
                refused = box.Text; error.Text = "Windows rejected that password. Fix it, or press Save again to try it."; box.Focus(); return;
            }
            d.DialogResult = DialogResult.OK;
        };
        box.TextChanged += delegate { if (box.Text != refused) error.Text = ""; };
        d.Controls.AddRange(new Control[] { mark, title, lbl, field, error, ok, cancel });
        d.AcceptButton = ok; d.CancelButton = cancel; d.Shown += delegate { box.Focus(); };
        return d;
    }

    internal static bool ChildSessionsEnabled(out bool enabled) { return WTSIsChildSessionsEnabled(out enabled); }
    internal static bool EnableChildSessions()
    {
        bool ok = WTSEnableChildSessions(true); int error = Marshal.GetLastWin32Error(); bool enabled;
        bool queried = WTSIsChildSessionsEnabled(out enabled);
        Log("enable: result=" + ok + ", error=" + error + ", enabled=" + enabled);
        return ok && queried && enabled;
    }

    [STAThread]
    static int Main(string[] args)
    {
        string command = args.Length == 0 ? "" : args[0].ToLowerInvariant();
        if (command == "-setup") return DesktopSetup.Run(baseDir, args.Length > 1 ? args[1] : null);
        if (command == "-agent") return SessionAgent.Run(baseDir);
        if (command == "-resetpass") return ClearPassword() ? 0 : 1;
        if (command == "-enable")
        {
            return EnableChildSessions() ? 0 : 1;
        }
        if (command == "-check") { bool enabled; return WTSIsChildSessionsEnabled(out enabled) && enabled ? 0 : 1; }
        // The RDP control can block the UI thread for a few seconds while the Annex desktop signs in;
        // without this Windows swaps in a white "Not Responding" ghost window meanwhile.
        DisableProcessWindowsGhosting();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            if (command == "-capture-preview" && args.Length == 2) return LauncherPreview.Capture(args[1]);
            if (command == "-preview")
            {
                HostState state = HostState.Stopped;
                if (args.Length > 1) Enum.TryParse(args[1], true, out state);
                using (var preview = LauncherPreview.Create(state)) Application.Run(preview);
                return 0;
            }
            if (command.Length != 0) { Log("Unknown command: " + command); return 2; }
            string sid;
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent()) sid = identity.User.Value;
            bool created;
            using (var singleton = new System.Threading.Mutex(false, "Local\\Annex.Launcher." + sid, out created))
            {
                if (!created) { MessageBox.Show("Annex is already running in this Windows session. Open it from its tray icon.", "Annex"); return 0; }
                using (var app = new LauncherApplication(baseDir)) return app.Run();
            }
        }
        catch (Exception ex) { Log("startup failed: " + ex); MessageBox.Show("Annex could not open.\n" + ex.Message, "Annex", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
}
