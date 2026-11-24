using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

// Sign-in failures (#3), hardware graphics setup (#4), and recovery paths fixed for 1.0.1.
static class SignInTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static T Find<T>(Control parent, Func<T, bool> match) where T : Control
    {
        foreach (Control control in parent.Controls) {
            var typed = control as T; if (typed != null && match(typed)) return typed;
            var found = Find(control, match); if (found != null) return found;
        }
        return null;
    }
    static Label ErrorLabel(Form form) { return Find<Label>(form, l => !(l is LinkLabel) && l.ForeColor == Theme.Bad); }

    public static void Run(Action<string, Action> check)
    {
        check("RDP sign-in refusals are recognised and dropped connections are not", delegate {
            foreach (int reason in new[] { 2055, 2567, 2823, 3079, 3335, 3591, 3847, 4615 }) Assert(SignInFailure.IsSignInFailure(reason), "refusal missed: " + reason);
            foreach (int reason in new[] { 0, 1, 2, 3, 264, 516, 772, 1032, 2308, 3848, 4871 }) Assert(!SignInFailure.IsSignInFailure(reason), "network/user disconnect treated as refusal: " + reason);
        });
        check("a refused saved password is forgotten but a locked-out one is kept", delegate {
            Assert(SignInFailure.ForgetPassword(SignInFailure.WrongPassword), "wrong saved password kept, would be retried");
            Assert(!SignInFailure.ForgetPassword(SignInFailure.AccountLockedOut), "lockout discarded a password that may be right");
            Assert(SignInFailure.PasswordExpired == 0xF07 && SignInFailure.ForgetPassword(3847), "expired password (0xF07) not recognised, would reconnect forever");
        });
        check("sign-in help names the Microsoft account and the Hello-only setting", delegate {
            var local = new SignInAccount(false, null, false);
            var microsoft = new SignInAccount(true, "player@example.com", false);
            var helloOnly = new SignInAccount(true, "player@example.com", true);
            Assert(local.PasswordHint(@"PC\player").Contains(@"PC\player") && local.PasswordHint(@"PC\player").Contains("PIN"), "local hint lost account or PIN warning");
            Assert(microsoft.PasswordHint(@"PC\player").Contains("Microsoft account password") && microsoft.PasswordHint(@"PC\player").Contains("player@example.com"), "Microsoft account hint missing");
            Assert(helloOnly.FailureHelp(@"PC\player", SignInFailure.WrongPassword).Contains("only allow Windows Hello sign-in"), "Hello-only setting not explained");
            Assert(local.FailureHelp(@"PC\player", SignInFailure.AccountLockedOut).Contains("locked"), "lockout not explained");
            Assert(local.FailureHelp(@"PC\player", SignInFailure.AccountRestricted).Contains("without a password"), "blank-password restriction not explained");
        });
        check("password dialog accepts an empty password only after a second Save", delegate {
            TextBox box;
            using (var form = Program.PasswordForm(@"PC\player", out box, text => { throw new Exception("empty password must not be pre-checked"); })) {
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-20000, -20000);
                form.Show(); Application.DoEvents();
                var save = Find<Button>(form, b => b.Text == "Save");
                save.PerformClick(); Application.DoEvents();
                Assert(form.DialogResult == DialogResult.None && ErrorLabel(form).Text.Contains("Save again"), "first empty Save closed the dialog");
                save.PerformClick(); Application.DoEvents();
                Assert(form.DialogResult == DialogResult.OK, "confirmed empty password refused");
                form.Close();
            }
        });
        check("typing resets the empty-password confirmation and wrong passwords are still pre-checked", delegate {
            TextBox box; int checks = 0;
            using (var form = Program.PasswordForm(@"PC\player", out box, text => { checks++; return true; })) {
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-20000, -20000);
                form.Show(); Application.DoEvents();
                var save = Find<Button>(form, b => b.Text == "Save");
                save.PerformClick(); box.Text = "x"; box.Text = ""; save.PerformClick(); Application.DoEvents();
                Assert(form.DialogResult == DialogResult.None, "stale empty confirmation accepted");
                box.Text = "typo"; save.PerformClick(); Application.DoEvents();
                Assert(checks == 1 && form.DialogResult == DialogResult.None && ErrorLabel(form).Text.Contains("rejected"), "wrong password not caught");
                form.Close();
            }
        });
        check("password dialog links to Sign-in options when Hello-only sign-in blocks passwords", delegate {
            TextBox box;
            using (var form = Program.PasswordForm(@"PC\player", out box, null, new SignInAccount(true, "player@example.com", true))) {
                var link = Find<LinkLabel>(form, l => true);
                Assert(link != null && link.Text.Contains("Sign-in options") && link.LinkArea.Length > 0, "no way to turn off Hello-only sign-in");
                Assert(box.AccessibleName == "Microsoft account password", "field not named for a Microsoft account");
                var save = Find<Button>(form, b => b.Text == "Save");
                Assert(save.Top > link.Bottom, "link overlaps the buttons");
            }
        });
        check("desktop setup enables hardware graphics for the Annex desktop", delegate {
            string root = Path.Combine(Path.GetTempPath(), "annex-gpu-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                File.WriteAllText(Path.Combine(root, "Annex.exe"), "not executed");
                var written = new Dictionary<string, int>();
                var result = DesktopSetup.Apply(DesktopSetup.Create(root), new RecordingActions(written));
                Assert(result.Success && written[HardwareGraphics.PolicyKey + "\\" + HardwareGraphics.ValueName] == 1, "hardware graphics policy not applied");
            } finally { Directory.Delete(root, true); }
        });
        check("setup state records each original value once and keeps earlier ones", delegate {
            string root = Path.Combine(Path.GetTempPath(), "annex-state-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string path = Path.Combine(root, "setup-state.json");
            try {
                File.WriteAllText(path, "{\"fDenyTSConnections\":1,\"RemoteDesktopRulesEnabled\":[\"RemoteDesktop-UserMode-In-TCP\"]}");
                SetupState.RecordMissing(path, new Dictionary<string, Func<object>> {
                    { "fDenyTSConnections", () => 0 }, { "bEnumerateHWBeforeSW", () => null }, { "ChildSessions", () => false } });
                var state = (Dictionary<string, object>)new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(File.ReadAllText(path));
                Assert((int)state["fDenyTSConnections"] == 1, "original value overwritten by Annex's value");
                Assert(state.ContainsKey("bEnumerateHWBeforeSW") && state["bEnumerateHWBeforeSW"] == null, "absent original not recorded as absent");
                Assert((bool)state["ChildSessions"] == false && state.ContainsKey("RemoteDesktopRulesEnabled"), "values lost");
                File.Delete(path);
                SetupState.RecordMissing(path, new Dictionary<string, Func<object>> { { "DWMFRAMEINTERVAL", () => 15 } });
                Assert(File.ReadAllText(path).Contains("\"DWMFRAMEINTERVAL\":15"), "first Setup / repair recorded nothing");
            } finally { Directory.Delete(root, true); }
        });
        check("host saved from its web page or restarted by Apply is still recognised", delegate {
            string root = Path.Combine(Path.GetTempPath(), "annex-own-" + Guid.NewGuid().ToString("N"));
            string exe = Path.Combine(root, "Installed host", "sunshine.exe"); Directory.CreateDirectory(Path.GetDirectoryName(exe)); File.WriteAllText(exe, "fixture");
            string receipts = ChildHostProfile.ReceiptRoot; ChildHostProfile.ReceiptRoot = Path.Combine(root, "receipts");
            var listener = HostController.ListenerProcess;
            try {
                var hostInstall = new HostInstallation(exe, HostKind.Sunshine, new HostFileMetadata("Sunshine", "Sunshine", "2026.914.233613", "fixture"), "registry");
                var p = HostProfiles.Prepare(root, hostInstall, new NetworkPort[0]);
                HostProcess[] processes = new HostProcess[0];
                HostController.ListenerProcess = port => 0;
                var host = new HostController(p, 7, () => processes, () => { processes = new[] { new HostProcess(7, exe, false, 42, 123, null) }; return new HostLaunchReceipt(42, 123, p.Identity); });
                host.RequestStart(); Assert(SpinWait.SpinUntil(() => host.GetStatus().State == HostState.Running, 3000), "launch not verified");
                File.AppendAllText(p.ConfigPath, "\nmin_log_level = info\n"); // the web page saved a setting
                var rebuilt = new HostController(HostProfiles.Prepare(root, hostInstall, new NetworkPort[0]), 7, () => processes, () => { throw new Exception("must not launch"); });
                Assert(rebuilt.GetStatus().State == HostState.Running, "saved settings made the running host unknown");
                processes = new[] { new HostProcess(7, exe, false, 77, 999, null) }; // Apply restarted it
                Assert(rebuilt.GetStatus().State == HostState.Unknown, "unrelated process accepted without proof");
                HostController.ListenerProcess = port => port == p.BasePort + 1 ? 77 : 0;
                Assert(rebuilt.GetStatus().State == HostState.Running, "restarted host serving this profile's port not recognised");
                HostController.ListenerProcess = port => port == p.BasePort + 1 ? 55 : 0;
                Assert(rebuilt.GetStatus().State == HostState.Unknown, "port owned by another process accepted");
            } finally { HostController.ListenerProcess = listener; ChildHostProfile.ReceiptRoot = receipts; Directory.Delete(root, true); }
        });
        check("a host stalled after Apply on its web page can be started again", delegate {
            string root = Path.Combine(Path.GetTempPath(), "annex-stall-" + Guid.NewGuid().ToString("N"));
            string exe = Path.Combine(root, "Installed host", "sunshine.exe"); Directory.CreateDirectory(Path.GetDirectoryName(exe)); File.WriteAllText(exe, "fixture");
            string receipts = ChildHostProfile.ReceiptRoot; ChildHostProfile.ReceiptRoot = Path.Combine(root, "receipts");
            var web = HostController.WebReady; var now = HostController.UtcNow; var listener = HostController.ListenerProcess; var end = HostController.EndStalled;
            var ended = new List<int>(); HostController.EndStalled = old => ended.Add(old.ProcessId);
            try {
                var hostInstall = new HostInstallation(exe, HostKind.Sunshine, new HostFileMetadata("Sunshine", "Sunshine", "2026.914.233613", "fixture"), "registry");
                var p = HostProfiles.Prepare(root, hostInstall, new NetworkPort[0]);
                DateTime clock = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc); HostController.UtcNow = () => clock;
                bool ready = false; HostController.WebReady = port => ready; HostController.ListenerProcess = port => 0;
                HostProcess[] processes = new HostProcess[0]; int launches = 0;
                var release = new ManualResetEvent(true);
                var host = new HostController(p, 7, () => processes, () => {
                    launches++; int pid = 40 + launches; long ticks = clock.Ticks + launches;
                    processes = processes.Concat(new[] { new HostProcess(7, exe, false, pid, ticks, null) }).ToArray();
                    release.WaitOne(); return new HostLaunchReceipt(pid, ticks, p.Identity);
                });
                host.RequestStart(); Assert(SpinWait.SpinUntil(() => !host.StartInProgress, 3000), "launch did not finish");
                Assert(host.GetStatus().State == HostState.Starting, "slow first start not reported as starting");
                clock = clock.AddSeconds(60); ready = true;
                Assert(host.GetStatus().State == HostState.Running, "host not running");
                ready = false; // Apply: Sunshine closed its web page and hangs half-closed
                var stuck = host.GetStatus();
                Assert(stuck.State == HostState.Error && stuck.Message.Contains("Apply") && LauncherPolicy.CanStartHost(stuck, true), "stalled host left in 'starting' with Start blocked");
                release.Reset();
                host.RequestStart();
                Assert(SpinWait.SpinUntil(() => launches == 2, 3000), "Start did not launch a replacement");
                var during = host.GetStatus();
                Assert(during.State == HostState.Starting, "launch window reported " + during.State + ": " + during.Message);
                release.Set(); Assert(SpinWait.SpinUntil(() => !host.StartInProgress, 3000), "relaunch did not finish");
                ready = true;
                Assert(host.GetStatus().State == HostState.Running, "replacement not recognised next to the stalled process");
                Assert(ended.Count == 1 && ended[0] == 41, "stalled host not closed before the replacement started");
                // The replacement crashes after a settings save rebuilt the controller: Start must stay available.
                processes = processes.Where(x => x.ProcessId == 41).ToArray();
                var rebuilt = new HostController(p, 7, () => processes, () => { throw new Exception("must not launch"); });
                var left = rebuilt.GetStatus();
                Assert(left.State == HostState.Error && LauncherPolicy.CanStartHost(left, true), "leftover stalled host made the status unknown: " + left.State);
                processes = new HostProcess[0]; ready = false;
                Assert(host.GetStatus().State == HostState.Stopped, "host that exited not reported as off");
                // A host that never opens its web page also stops blocking Start eventually.
                processes = new[] { new HostProcess(7, exe, false, 90, clock.Ticks, null) };
                var fresh = new HostController(p, 7, () => processes, () => { throw new Exception("must not launch"); });
                HostController.ListenerProcess = port => 90;
                Assert(fresh.GetStatus().State == HostState.Starting, "starting host declared stalled too early");
                clock = clock.Add(HostController.StallAfter).AddSeconds(1);
                Assert(fresh.GetStatus().State == HostState.Error, "never-ready host blocks Start forever");
            } finally { HostController.WebReady = web; HostController.UtcNow = now; HostController.ListenerProcess = listener; HostController.EndStalled = end; ChildHostProfile.ReceiptRoot = receipts; Directory.Delete(root, true); }
        });
        check("host files cannot be swapped between the elevation check and the start", delegate {
            string root = Path.Combine(Path.GetTempPath(), "annex-hold-" + Guid.NewGuid().ToString("N"));
            string exe = Path.Combine(root, "Sunshine", "sunshine.exe"), config = Path.Combine(root, "Sunshine", "config", "sunshine.conf");
            Directory.CreateDirectory(Path.GetDirectoryName(config)); File.WriteAllText(exe, "host"); File.WriteAllText(config, "port = 48989");
            try {
                using (var held = ElevationPolicy.Hold(exe, config)) {
                    Assert(held != null, "files not held");
                    foreach (var swap in new Action[] {
                        () => File.Move(exe, exe + ".old"), () => File.Move(config, config + ".old"),
                        () => Directory.Move(Path.GetDirectoryName(config), Path.GetDirectoryName(config) + "-old"),
                        () => Directory.Move(Path.Combine(root, "Sunshine"), Path.Combine(root, "Other")),
                        () => Directory.Move(root, root + "-old") }) {
                        bool blocked = false; try { swap(); } catch (IOException) { blocked = true; } catch (UnauthorizedAccessException) { blocked = true; }
                        Assert(blocked, "a held path could be renamed");
                    }
                    // The permission check opens the files for writing; holding must not turn that into a sharing error.
                    using (new FileStream(exe, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }
                }
                Directory.Move(Path.Combine(root, "Sunshine"), Path.Combine(root, "Other")); // released
                Assert(ElevationPolicy.Hold(exe, config) == null, "missing files reported as held");
            } finally { Directory.Delete(root, true); }
        });
        check("native TCP listener lookup finds this process's listener", delegate {
            var socket = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); socket.Start();
            try {
                int port = ((System.Net.IPEndPoint)socket.LocalEndpoint).Port;
                Assert(TcpListeners.OwnerOf(port) == Process.GetCurrentProcess().Id, "listener owner not found");
            } finally { socket.Stop(); }
            if (!System.Net.Sockets.Socket.OSSupportsIPv6) return;
            var socket6 = new System.Net.Sockets.TcpListener(System.Net.IPAddress.IPv6Loopback, 0); socket6.Start();
            try {
                int port = ((System.Net.IPEndPoint)socket6.LocalEndpoint).Port;
                Assert(TcpListeners.OwnerOf(port) == Process.GetCurrentProcess().Id, "IPv6 listener owner not found");
            } finally { socket6.Stop(); }
        });
        check("an app waiting for an admin prompt does not stall the helper", delegate {
            string root = Path.Combine(Path.GetTempPath(), "annex-launch-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            int wait = AgentService.LaunchWaitMilliseconds; AgentService.LaunchWaitMilliseconds = 200;
            var release = new ManualResetEvent(false);
            try {
                var clock = Stopwatch.StartNew();
                string message = AgentService.WithinReplyTime(() => { release.WaitOne(); return "launched late"; }, root);
                Assert(clock.ElapsedMilliseconds < 1500 && message.Contains("approval"), "helper blocked on a pending launch");
                release.Set();
                Assert(SpinWait.SpinUntil(() => { try { return File.ReadAllText(Path.Combine(root, "autostart.log")).Contains("launched late"); } catch (IOException) { return false; } }, 3000), "late result not logged");
                Assert(AgentService.WithinReplyTime(() => "quick result", root) == "quick result", "fast launch result lost");
            } finally { AgentService.LaunchWaitMilliseconds = wait; release.Set(); Thread.Sleep(100); Directory.Delete(root, true); }
        });
    }

    sealed class RecordingActions : IDesktopSetupActions
    {
        readonly Dictionary<string, int> written;
        public RecordingActions(Dictionary<string, int> written) { this.written = written; }
        public bool EnableChildSessions() { return true; }
        public void WriteRegistryDword(string path, string name, int value) { written[path + "\\" + name] = value; }
        public void WriteInstallationRecord(string root) { }
    }
}
