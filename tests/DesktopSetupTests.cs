using System;
using System.Collections.Generic;
using System.IO;

static class DesktopSetupTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    sealed class Actions : IDesktopSetupActions
    {
        public bool Enabled = true, FailRegistry;
        public string Registered;
        public readonly Dictionary<string, int> Registry = new Dictionary<string, int>();
        public bool EnableChildSessions() { return Enabled; }
        public void WriteRegistryDword(string path, string name, int value) {
            if (FailRegistry) throw new IOException("fixture registry failure");
            Registry[path + "\\" + name] = value;
        }
        public void WriteInstallationRecord(string root) { Registered = root; }
    }
    public static void Run(Action<string, Action> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "annex-setup-" + Guid.NewGuid().ToString("N"));
        string install = Path.Combine(root, "installed & 100% ' λ");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "Annex.exe"), "not executed");
        try {
            check("desktop setup works without a streaming host", delegate {
                var plan = DesktopSetup.Create(install); var actions = new Actions();
                var result = DesktopSetup.Apply(plan, actions);
                Assert(result.Success, result.Message);
                Assert(actions.Registered == install, "working desktop was not registered without Sunshine");
                Assert(!Directory.Exists(Path.Combine(install, "Sunshine")), "setup installed a mandatory host");
                Assert(actions.Registry[@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Terminal Server\fDenyTSConnections"] == 0, "RDP setup omitted");
                Assert(actions.Registry[@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\DWMFRAMEINTERVAL"] == 8, "compositor setting omitted");
                Assert(actions.Registry[@"HKEY_CURRENT_USER\Software\Microsoft\Terminal Server Client\RemoteDesktop_SuppressWhenMinimized"] == 2, "minimized rendering omitted");
            });
            check("failed native enable never registers a usable installation", delegate {
                var actions = new Actions { Enabled = false };
                var result = DesktopSetup.Apply(DesktopSetup.Create(install), actions);
                Assert(!result.Success && actions.Registered == null && actions.Registry.Count == 0, "failed enable looked successful or changed later settings");
            });
            check("partial setup failure is reported without registering success", delegate {
                var actions = new Actions { FailRegistry = true };
                var result = DesktopSetup.Apply(DesktopSetup.Create(install), actions);
                Assert(!result.Success && result.Message.Contains("fixture registry failure") && actions.Registered == null, "partial failure hidden");
            });
            check("setup refuses a missing executable before native changes", delegate {
                bool refused = false;
                try { DesktopSetup.Create(Path.Combine(root, "absent")); } catch (IOException) { refused = true; }
                Assert(refused, "a nonexistent install could be registered");
            });
            check("installation record validates and preserves the canonical folder", delegate {
                string record = Path.Combine(root, "record", "installation.txt");
                InstallationIdentity.Save(record, install + Path.DirectorySeparatorChar);
                Assert(InstallationIdentity.Load(record) == install, "installation path did not round trip");
                File.WriteAllText(record, "not an absolute installation path");
                Assert(InstallationIdentity.Load(record) == null, "corrupt registration accepted");
            });
            check("installation comparison ignores a final separator and path casing", delegate {
                Assert(InstallationIdentity.SameRoot(install, install.ToUpperInvariant() + Path.DirectorySeparatorChar), "correct installed folder produced a wrong-folder warning");
                Assert(!InstallationIdentity.SameRoot(install, Path.Combine(root, "other")), "different installation accepted");
                Assert(!InstallationIdentity.SameRoot(install, "relative"), "invalid registration accepted");
            });
        } finally { Directory.Delete(root, true); }
    }
}
