using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

interface IDesktopSetupActions
{
    bool EnableChildSessions();
    void WriteRegistryDword(string path, string name, int value);
    void WriteStartupShortcut(string path, string executable, string arguments);
    void WriteInstallationRecord(string installRoot);
}

sealed class DesktopSetupPlan
{
    public string InstallRoot, Executable, StartupShortcut;
}

sealed class DesktopSetupResult
{
    public bool Success { get; private set; }
    public string Message { get; private set; }
    public DesktopSetupResult(bool success, string message) { Success = success; Message = message.Length > 512 ? message.Substring(0, 512) : message; }
}

static class InstallationIdentity
{
    public static string RecordPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChildStream", "installation.txt"); } }
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
            return File.Exists(Path.Combine(root, "ChildStream.exe")) ? root : null;
        } catch (Exception) { return null; }
    }
    public static void Save(string path, string root)
    {
        root = Normalize(root);
        if (!File.Exists(Path.Combine(root, "ChildStream.exe"))) throw new IOException("ChildStream.exe is missing from the installation folder.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, root, new UTF8Encoding(false));
    }
}

static class DesktopSetup
{
    public static DesktopSetupPlan Create(string installRoot, string startupDirectory)
    {
        string root = InstallationIdentity.Normalize(installRoot);
        string executable = Path.Combine(root, "ChildStream.exe");
        if (!File.Exists(executable)) throw new IOException("ChildStream.exe is missing. Keep the executable in its installation folder.");
        if (string.IsNullOrWhiteSpace(startupDirectory) || !Path.IsPathRooted(startupDirectory)) throw new IOException("Windows could not identify your Startup folder.");
        return new DesktopSetupPlan { InstallRoot = root, Executable = executable, StartupShortcut = Path.Combine(Path.GetFullPath(startupDirectory), "ChildStream helper.lnk") };
    }
    public static DesktopSetupResult Apply(DesktopSetupPlan plan, IDesktopSetupActions actions)
    {
        try {
            if (!actions.EnableChildSessions()) return new DesktopSetupResult(false, "Windows could not enable child sessions. No installation was registered.");
            actions.WriteRegistryDword(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", 0);
            actions.WriteRegistryDword(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations", "DWMFRAMEINTERVAL", 8);
            actions.WriteRegistryDword(@"HKEY_CURRENT_USER\Software\Microsoft\Terminal Server Client", "RemoteDesktop_SuppressWhenMinimized", 2);
            actions.WriteStartupShortcut(plan.StartupShortcut, plan.Executable, "-agent");
            actions.WriteInstallationRecord(plan.InstallRoot);
            return new DesktopSetupResult(true, "Windows desktop setup is ready. Open ChildStream from this folder and start the desktop. A streaming host is optional.");
        } catch (Exception ex) { return new DesktopSetupResult(false, "Windows setup did not finish: " + ex.Message + " Some earlier settings may have changed; repair explicitly after resolving the error."); }
    }
    public static int Run(string root, string expectedUser)
    {
        using (var identity = WindowsIdentity.GetCurrent()) {
            string sid = identity.User.Value;
            if (!string.IsNullOrEmpty(expectedUser) && !string.Equals(expectedUser, sid, StringComparison.Ordinal)) {
                MessageBox.Show("Run setup as the same Windows user who runs ChildStream.", "ChildStream setup", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1;
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
            var plan = Create(root, Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            var result = Apply(plan, new NativeDesktopSetupActions());
            Program.Log("desktop setup: " + result.Message);
            MessageBox.Show(result.Message, "ChildStream setup", MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            return result.Success ? 0 : 1;
        } catch (Exception ex) { Program.Log("setup failed: " + ex.Message); MessageBox.Show(ex.Message, "ChildStream setup", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
}

static class LegacyHelperHook
{
    public static void RemoveIfOwned(string hook, string root)
    {
        if (!File.Exists(hook) || new FileInfo(hook).Length > 32768) return;
        string text = File.ReadAllText(hook);
        Match encoded = Regex.Match(text, @"-EncodedCommand\s+([A-Za-z0-9+/=]+)", RegexOptions.IgnoreCase);
        if (!encoded.Success) return;
        string command;
        try { command = Encoding.Unicode.GetString(Convert.FromBase64String(encoded.Groups[1].Value)); } catch (FormatException) { return; }
        string expected = "& '" + Path.Combine(root, "scripts", "childsession-autostart.ps1").Replace("'", "''") + "'";
        if (string.Equals(command.Trim(), expected, StringComparison.OrdinalIgnoreCase)) File.Delete(hook);
    }
}

sealed class NativeDesktopSetupActions : IDesktopSetupActions
{
    public bool EnableChildSessions() { return Program.EnableChildSessions(); }
    public void WriteRegistryDword(string path, string name, int value) { Registry.SetValue(path, name, value, RegistryValueKind.DWord); }
    public void WriteStartupShortcut(string path, string executable, string arguments)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        object shellObject = null, shortcutObject = null;
        try {
            shellObject = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(path); dynamic shortcut = shortcutObject;
            shortcut.TargetPath = executable; shortcut.Arguments = arguments; shortcut.WorkingDirectory = Path.GetDirectoryName(executable);
            shortcut.Description = "ChildStream child desktop helper"; shortcut.Save();
        } finally {
            if (shortcutObject != null && Marshal.IsComObject(shortcutObject)) Marshal.FinalReleaseComObject(shortcutObject);
            if (shellObject != null && Marshal.IsComObject(shellObject)) Marshal.FinalReleaseComObject(shellObject);
        }
        LegacyHelperHook.RemoveIfOwned(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "childstream-sunshine.cmd"), Path.GetDirectoryName(executable));
    }
    public void WriteInstallationRecord(string root) { InstallationIdentity.Save(InstallationIdentity.RecordPath, root); }
}
