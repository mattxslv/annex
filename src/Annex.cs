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

    internal static string PromptPassword(string account)
    {
        using (var d = new Form { Text = "Annex - one-time setup", Width = 440, Height = 190, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false })
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
