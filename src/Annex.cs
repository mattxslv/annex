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

// Non-scriptable RDP client settings. Only AllowPromptingForCredentials is used; the placeholders keep
// the vtable layout of mstscax.dll's typelib (slots exclude IUnknown's three).
[ComImport, Guid("4F6996D5-D7B1-412C-B0FF-063718566907"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMsRdpClientNonScriptable5
{
    void _00(); void _01(); void _02(); void _03(); void _04(); void _05(); void _06(); void _07(); void _08(); void _09();
    void _10(); void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17(); void _18(); void _19();
    void _20(); void _21(); void _22(); void _23(); void _24(); void _25(); void _26(); void _27(); void _28(); void _29();
    void _30(); void _31(); void _32(); void _33(); void _34(); void _35(); void _36(); void _37(); void _38(); void _39();
    void _40(); void _41(); void _42(); void _43(); void _44(); void _45(); void _46(); void _47(); void _48(); void _49();
    void _50(); void _51(); void _52(); void _53(); void _54(); void _55(); void _56(); void _57(); void _58(); void _59();
    void put_AllowPromptingForCredentials([MarshalAs(UnmanagedType.VariantBool)] bool value);
    void get_AllowPromptingForCredentials([MarshalAs(UnmanagedType.VariantBool)] out bool value);
}

// IMsTscAxEvents: only OnDisconnected is needed, for the reason a sign-in failed.
[ComImport, Guid("336D5562-EFA8-482E-8CB3-C5C0FC7A7DB6"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
public interface IRdpClientEvents { [DispId(4)] void OnDisconnected(int reason); }

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class RdpDisconnectSink : IRdpClientEvents
{
    readonly Action<int> handler;
    public RdpDisconnectSink(Action<int> handler) { this.handler = handler; }
    public void OnDisconnected(int reason) { handler(reason); }
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
        using (var d = PasswordForm(account, out tb, PasswordRejected, SignInAccount.Current())) return d.ShowDialog() == DialogResult.OK ? tb.Text : null;
    }

    internal static Form PasswordForm(string account, out TextBox tb, Func<string, bool> rejected = null, SignInAccount info = null)
    {
        if (info == null) info = new SignInAccount(false, null, false);
        int extra = info.PasswordlessOnly ? 40 : 0;
        var d = new Form { Text = "Annex - one-time setup", ClientSize = new Size(460, 236 + extra), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false, BackColor = Theme.Background, ForeColor = Theme.Text, Font = new Font("Segoe UI", 9.5f), AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96) };
        Theme.DarkChrome(d);
        var mark = new LogoMark { Left = 20, Top = 20, Size = new Size(40, 40) };
        var title = new Label { Text = "Sign in to the Annex desktop", Left = 72, Top = 20, AutoSize = true, Font = new Font("Segoe UI Semibold", 13), ForeColor = Theme.Text };
        var lbl = new Label { Text = info.PasswordHint(account) + " Stored DPAPI-encrypted for this user only.", Left = 72, Top = 50, Width = 370, Height = 54, ForeColor = Theme.Muted, UseMnemonic = false };
        tb = new TextBox { UseSystemPasswordChar = true, AccessibleName = info.MicrosoftAccount ? "Microsoft account password" : "Windows password" }; var box = tb;
        var field = new FieldBox(tb) { Left = 72, Top = 110, Width = 368 };
        var error = new Label { Left = 72, Top = 148, Width = 368, Height = 20, ForeColor = Theme.Bad, UseMnemonic = false };
        var controls = new System.Collections.Generic.List<Control> { mark, title, lbl, field, error };
        if (info.PasswordlessOnly) {
            // This Windows setting blocks every password sign-in, including the Annex desktop's.
            const string link = "Turn it off in Sign-in options";
            var hello = new LinkLabel { Text = "Windows allows only Windows Hello sign-in for Microsoft accounts on this PC, which blocks passwords. " + link + ".", Left = 72, Top = 172, Width = 368, Height = 36, ForeColor = Theme.Muted, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent, UseMnemonic = false };
            hello.LinkArea = new LinkArea(hello.Text.IndexOf(link, StringComparison.Ordinal), link.Length);
            hello.LinkClicked += delegate { SignInAccount.OpenSignInOptions(); };
            controls.Add(hello);
        }
        var ok = new AnnexButton { Text = "Save", Kind = ButtonKind.Primary, Left = 260, Top = 178 + extra, Size = new Size(88, 36) };
        var cancel = new AnnexButton { Text = "Cancel", Kind = ButtonKind.Secondary, Left = 352, Top = 178 + extra, Size = new Size(88, 36), DialogResult = DialogResult.Cancel };
        string refused = null; bool blankConfirmed = false;
        ok.Click += delegate {
            // Accounts without a password exist; an empty field needs a second, deliberate Save.
            if (box.Text.Length == 0 && !blankConfirmed) { blankConfirmed = true; error.Text = "No password? Press Save again to sign in without one."; return; }
            // Saving the same text again overrides the check, in case it misjudges an account type.
            if (rejected != null && box.Text.Length > 0 && box.Text != refused && rejected(box.Text)) {
                refused = box.Text; error.Text = "Windows rejected that password. Fix it, or press Save again to try it."; box.Focus(); return;
            }
            d.DialogResult = DialogResult.OK;
        };
        box.TextChanged += delegate { blankConfirmed = false; if (box.Text != refused) error.Text = ""; };
        controls.Add(ok); controls.Add(cancel);
        d.Controls.AddRange(controls.ToArray());
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
        // Unexpected UI errors: log them and keep running with a short Annex dialog, not the .NET crash box.
        bool reporting = false;
        Application.ThreadException += (s, e) => {
            Log("unexpected error: " + e.Exception);
            if (reporting) return;
            reporting = true;
            try { AnnexDialog.Tell(null, "Something went wrong", e.Exception.Message + " Annex keeps running; details are in launcher.log.", true); }
            catch (Exception) { }
            finally { reporting = false; }
        };
        try
        {
            if (command == "-capture-preview" && args.Length == 2) return LauncherPreview.Capture(args[1]);
            if (command == "-capture-screens" && args.Length >= 2) return LauncherPreview.CaptureScreens(args[1], args.Length > 2 ? args[2] : null);
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

// What kind of Windows account signs in to the Annex desktop, so sign-in help can be specific.
sealed class SignInAccount
{
    public bool MicrosoftAccount { get; private set; }
    public string Principal { get; private set; }
    public bool PasswordlessOnly { get; private set; }
    public SignInAccount(bool microsoftAccount, string principal, bool passwordlessOnly)
    {
        MicrosoftAccount = microsoftAccount; Principal = string.IsNullOrEmpty(principal) ? null : principal; PasswordlessOnly = passwordlessOnly;
    }

    public string PasswordHint(string account)
    {
        if (!MicrosoftAccount) return "Windows password for " + account + ", not your Windows Hello PIN.";
        return "Your Microsoft account password" + (Principal != null ? " (" + Principal + ")" : "") + ", not your Windows Hello PIN.";
    }

    // Guidance after Windows refused the sign-in (RDP disconnect reason; 0 when unknown).
    public string FailureHelp(string account, int reason)
    {
        if (reason == SignInFailure.AccountLockedOut) return "Windows locked this account after too many wrong passwords. Wait until the lockout ends (usually 10 minutes), then choose Start desktop.";
        if (reason == SignInFailure.AccountRestricted) return "Windows refused this account for this kind of sign-in. Accounts without a password can't sign in to the Annex desktop: set a password in Settings > Accounts > Sign-in options, then choose Start desktop.";
        if (reason == SignInFailure.PasswordExpired || reason == SignInFailure.MustChangePassword) return "Windows wants a new password for this account. Change it (Ctrl+Alt+Del > Change a password), then choose Start desktop.";
        if (reason == SignInFailure.AccountDisabled || reason == SignInFailure.AccountExpired) return "Windows reports this account as disabled or expired.";
        string text = PasswordHint(account);
        if (PasswordlessOnly) text += " Windows Hello-only sign-in is on for this PC, which blocks passwords: turn off \"only allow Windows Hello sign-in for Microsoft accounts\" in Settings > Accounts > Sign-in options.";
        else if (MicrosoftAccount) text += " If you changed it online or have only used a PIN on this PC, lock the PC and sign in once with the password so Windows learns it.";
        return text + " Choose Start desktop to try again.";
    }

    public static void OpenSignInOptions()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:signinoptions") { UseShellExecute = true }); }
        catch (Exception ex) { Program.Log("open sign-in options: " + ex.Message); }
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] static extern int NetUserGetInfo(string server, string user, int level, out IntPtr buffer);
    [DllImport("netapi32.dll")] static extern int NetApiBufferFree(IntPtr buffer);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct USER_INFO_24 { [MarshalAs(UnmanagedType.Bool)] public bool InternetIdentity; public uint Flags; public string Provider; public string Principal; public IntPtr Sid; }

    public static SignInAccount Current()
    {
        bool microsoft = false; string principal = null; bool passwordless = false;
        try {
            IntPtr buffer;
            if (NetUserGetInfo(null, Environment.UserName, 24, out buffer) == 0) {
                try { var info = (USER_INFO_24)Marshal.PtrToStructure(buffer, typeof(USER_INFO_24)); microsoft = info.InternetIdentity; principal = info.Principal; }
                finally { NetApiBufferFree(buffer); }
            }
        } catch (Exception ex) { Program.Log("account type: " + ex.Message); }
        try {
            object value = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device", "DevicePasswordLessBuildVersion", null);
            passwordless = value is int && (int)value == 2;
        } catch (Exception ex) { Program.Log("passwordless setting: " + ex.Message); }
        return new SignInAccount(microsoft, principal, passwordless);
    }
}

// RDP disconnect reasons meaning Windows refused the account or password (not a dropped connection).
static class SignInFailure
{
    public const int WrongPassword = 2055, UnknownUser = 2567, AccountDisabled = 2823, AccountRestricted = 3079,
        AccountLockedOut = 3335, AccountExpired = 3591, MustChangePassword = 4615, PasswordExpired = 3847;
    public static bool IsSignInFailure(int reason)
    {
        switch (reason) {
            case WrongPassword: case UnknownUser: case AccountDisabled: case AccountRestricted:
            case AccountLockedOut: case AccountExpired: case MustChangePassword: case PasswordExpired: return true;
            default: return false;
        }
    }
    // A saved password Windows refused must not be retried: repeated attempts can lock the account.
    public static bool ForgetPassword(int reason) { return reason == WrongPassword || reason == UnknownUser || reason == AccountRestricted || reason == PasswordExpired || reason == MustChangePassword; }
}
