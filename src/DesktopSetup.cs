using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

interface IDesktopSetupActions
{
    bool EnableChildSessions();
    void WriteRegistryDword(string path, string name, int value);
    void WriteInstallationRecord(string installRoot);
}

sealed class DesktopSetupPlan
{
    public string InstallRoot, Executable;
}

sealed class DesktopSetupResult
{
    public bool Success { get; private set; }
    public string Message { get; private set; }
    public DesktopSetupResult(bool success, string message) { Success = success; Message = message.Length > 512 ? message.Substring(0, 512) : message; }
}

static class InstallationIdentity
{
    public static string RecordPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Annex", "installation.txt"); } }
    public static string Normalize(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root)) throw new IOException("Choose an absolute installation folder.");
        string full = Path.GetFullPath(root);
        if (full.Length <= Path.GetPathRoot(full).Length) throw new IOException("Choose a private installation folder, not a drive root.");
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
    public static string Load() { return Load(RecordPath); }
    public static bool SameRoot(string left, string right) {
        try { return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
        catch (IOException) { return false; }
        catch (NotSupportedException) { return false; }
    }
    public static string Load(string path)
    {
        try {
            var file = new FileInfo(path); if (!file.Exists || file.Length > 32768) return null;
            string root = Normalize(File.ReadAllText(path, Encoding.UTF8).Trim());
            return File.Exists(Path.Combine(root, "Annex.exe")) ? root : null;
        } catch (Exception) { return null; }
    }
    public static void Save(string path, string root)
    {
        root = Normalize(root);
        if (!File.Exists(Path.Combine(root, "Annex.exe"))) throw new IOException("Annex.exe is missing from the installation folder.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, root, new UTF8Encoding(false));
    }
}

static class DesktopSetup
{
    public static DesktopSetupPlan Create(string installRoot)
    {
        string root = InstallationIdentity.Normalize(installRoot);
        string executable = Path.Combine(root, "Annex.exe");
        if (!File.Exists(executable)) throw new IOException("Annex.exe is missing. Keep the executable in its installation folder.");
        return new DesktopSetupPlan { InstallRoot = root, Executable = executable };
    }
    public static DesktopSetupResult Apply(DesktopSetupPlan plan, IDesktopSetupActions actions)
    {
        try {
            if (!actions.EnableChildSessions()) return new DesktopSetupResult(false, "Windows could not enable child sessions. No installation was registered.");
            actions.WriteRegistryDword(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", 0);
            actions.WriteRegistryDword(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations", "DWMFRAMEINTERVAL", 8);
            actions.WriteRegistryDword(@"HKEY_CURRENT_USER\Software\Microsoft\Terminal Server Client", "RemoteDesktop_SuppressWhenMinimized", 2);
            // "Use hardware graphics adapters for all Remote Desktop Services sessions". Without it Windows 10
            // renders remote sessions, and so the Annex desktop, on the CPU (Microsoft Basic Render Driver).
            actions.WriteRegistryDword(HardwareGraphics.PolicyKey, HardwareGraphics.ValueName, 1);
            actions.WriteInstallationRecord(plan.InstallRoot);
            return new DesktopSetupResult(true, "Windows desktop setup is ready. If the Annex helper is not registered yet, run scripts\\setup.ps1 once. A streaming host is optional.");
        } catch (Exception ex) { return new DesktopSetupResult(false, "Windows setup did not finish: " + ex.Message + " Some earlier settings may have changed; repair explicitly after resolving the error."); }
    }
    public static int Run(string root, string expectedUser)
    {
        using (var identity = WindowsIdentity.GetCurrent()) {
            string sid = identity.User.Value;
            if (!string.IsNullOrEmpty(expectedUser) && !string.Equals(expectedUser, sid, StringComparison.Ordinal)) {
                MessageBox.Show("Run setup as the same Windows user who runs Annex.", "Annex setup", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1;
            }
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) {
                try {
                    using (var process = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "-setup " + sid) { UseShellExecute = true, Verb = "runas", WorkingDirectory = root })) {
                        if (process == null) return 1; process.WaitForExit(); return process.ExitCode;
                    }
                } catch (Exception ex) { Program.Log("setup elevation failed: " + ex.Message); return 1; }
            }
        }
        try {
            var plan = Create(root);
            // Setup / repair can be the first setup: remember the original values for uninstall before changing them.
            try { SetupState.RecordMissing(Path.Combine(plan.InstallRoot, "setup-state.json"), SetupState.NativeOriginals()); }
            catch (Exception ex) { Program.Log("setup state: " + ex.Message); }
            var result = Apply(plan, new NativeDesktopSetupActions());
            Program.Log("desktop setup: " + result.Message);
            MessageBox.Show(result.Message, "Annex setup", MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            return result.Success ? 0 : 1;
        } catch (Exception ex) { Program.Log("setup failed: " + ex.Message); MessageBox.Show(ex.Message, "Annex setup", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
}

sealed class NativeDesktopSetupActions : IDesktopSetupActions
{
    public bool EnableChildSessions() { return Program.EnableChildSessions(); }
    public void WriteRegistryDword(string path, string name, int value) { Registry.SetValue(path, name, value, RegistryValueKind.DWord); }
    public void WriteInstallationRecord(string root) { InstallationIdentity.Save(InstallationIdentity.RecordPath, root); }
}

static class HardwareGraphics
{
    public const string PolicyKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";
    public const string ValueName = "bEnumerateHWBeforeSW";
    public static bool Enabled()
    {
        try { object value = Registry.GetValue(PolicyKey, ValueName, null); return value is int && (int)value == 1; }
        catch (Exception) { return false; }
    }
}

// setup-state.json: Windows settings as they were before Annex, restored by uninstall (setup-admin.ps1 -Uninstall).
// Each value is recorded once; scripts\setup.ps1 records the same names plus the firewall rules.
static class SetupState
{
    public static void RecordMissing(string path, IDictionary<string, Func<object>> originals)
    {
        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
        Dictionary<string, object> state = null;
        if (File.Exists(path)) state = serializer.DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
        if (state == null) state = new Dictionary<string, object>();
        bool changed = false;
        foreach (var original in originals) if (!state.ContainsKey(original.Key)) { state[original.Key] = original.Value(); changed = true; }
        if (changed) File.WriteAllText(path, serializer.Serialize(state), new UTF8Encoding(false));
    }
    public static IDictionary<string, Func<object>> NativeOriginals()
    {
        const string ts = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Terminal Server";
        return new Dictionary<string, Func<object>> {
            { "fDenyTSConnections", () => Registry.GetValue(ts, "fDenyTSConnections", null) },
            { "DWMFRAMEINTERVAL", () => Registry.GetValue(ts + @"\WinStations", "DWMFRAMEINTERVAL", null) },
            { "SuppressWhenMinimized", () => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Terminal Server Client", "RemoteDesktop_SuppressWhenMinimized", null) },
            { "ChildSessions", () => { bool enabled; return Program.ChildSessionsEnabled(out enabled) && enabled; } },
            { HardwareGraphics.ValueName, () => Registry.GetValue(HardwareGraphics.PolicyKey, HardwareGraphics.ValueName, null) },
        };
    }
}
