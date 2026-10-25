using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

static class ChildAppTests
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static bool Refused(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } catch (IOException) { return true; } }
    public static void Run(Action<string, Action> check) {
        check("native arguments round trip spaces Unicode quotes and final backslashes", delegate {
            string[] expected = { "", "a b", "日本語 & %PATH%", "a\"b", @"C:\directory with spaces\", "back\\\\\"quote" };
            int count; IntPtr memory = CommandLineToArgvW("app.exe " + NativeArguments.Join(expected), out count);
            try { Assert(memory != IntPtr.Zero && count == expected.Length + 1, "wrong argument count"); for (int i = 0; i < expected.Length; i++) Assert(Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, (i + 1) * IntPtr.Size)) == expected[i], "argument changed: " + i); }
            finally { if (memory != IntPtr.Zero) LocalFree(memory); }
        });
        string root = Path.Combine(Path.GetTempPath(), "childstream-apps-" + Guid.NewGuid().ToString("N"));
        check("child browsers use stable independent profiles without importing normal data", delegate {
            Directory.CreateDirectory(root);
            try { foreach (ChildAppKind kind in new[] { ChildAppKind.Chrome, ChildAppKind.Edge }) {
                string exe = Path.Combine(root, kind + ".exe"); File.WriteAllText(exe, "fixture"); var app = new ChildApp(exe, kind);
                var first = ChildAppLauncher.Build(app, root); var second = ChildAppLauncher.Build(app, root);
                Assert(first.Arguments.SequenceEqual(second.Arguments) && first.Arguments.Length == 1 && first.Arguments[0] == "--user-data-dir=" + Path.Combine(root, "ChildStream", kind + "-Child"), "wrong or unstable child profile");
                Assert(!Directory.Exists(Path.Combine(root, "Google")) && !first.Arguments.Any(a => a.Contains("no-sandbox")), "normal browser data or sandbox changed");
            } } finally { Directory.Delete(root, true); }
        });
        check("registered apps reject missing executables shortcuts scripts shells and external flags", delegate {
            Directory.CreateDirectory(root); try {
                foreach (string name in new[] { "absent.exe", "thing.lnk", "script.ps1", "cmd.exe", "powershell.exe" }) { string path = Path.Combine(root, name); if (name != "absent.exe") File.WriteAllText(path, "fixture"); Assert(Refused(() => ChildAppCatalog.Register(path)), "unsafe record accepted: " + name); }
                string exe = Path.Combine(root, "game.exe"); File.WriteAllText(exe, "fixture"); Assert(Refused(() => new ChildApp(exe, ChildAppKind.Chrome, new[] { "--user-data-dir=C:\\normal", "--no-sandbox" })), "external profile/sandbox flags accepted");
                var app = ChildAppCatalog.Register(exe); File.Delete(exe); Assert(Refused(() => ChildAppLauncher.Build(app, root)), "removed game dispatched");
            } finally { Directory.Delete(root, true); }
        });
        check("child app launch rejects stale child identity before dispatch", delegate {
            var spec = new AppLaunchSpec("fixture.exe", new string[0], root, ChildAppKind.Other); bool started = false;
            var result = ChildAppLauncher.Launch(spec, 7, () => 8, s => { started = true; return new AppLaunchResult(AppLaunchState.Dispatched, "unverified"); });
            Assert(!started && result.State == AppLaunchState.Failed, "replacement child received app");
        });
        check("Steam ownership and denied inspection preserve other sessions", delegate {
            string path = Path.Combine(root, "steam.exe"); var spec = new AppLaunchSpec(path, new string[0], root, ChildAppKind.Steam);
            Assert(ChildAppLauncher.Ownership(spec, 7, new[] { new HostProcess(1, path, false) }).State == AppLaunchState.OtherSession, "main Steam ownership ignored");
            Assert(ChildAppLauncher.Ownership(spec, 7, new[] { new HostProcess(-1, null, true) }).State == AppLaunchState.InspectionDenied, "unknown ownership guessed safe");
            var browser = new AppLaunchSpec(path, new string[0], root, ChildAppKind.Chrome); Assert(ChildAppLauncher.Ownership(browser, 7, new[] { new HostProcess(1, path, false) }) == null, "isolated browser blocked by main instance");
        });
        check("app dispatch failures and early exits never claim a usable child window", delegate {
            var spec = new AppLaunchSpec("fixture.exe", new string[0], root, ChildAppKind.Other);
            Assert(ChildAppLauncher.Launch(spec, 7, () => 7, s => { throw new IOException("denied"); }).State == AppLaunchState.Failed, "failure escaped");
            var result = ChildAppLauncher.Launch(spec, 7, () => 7, s => new AppLaunchResult(AppLaunchState.EarlyExit, "exited")); Assert(result.State == AppLaunchState.EarlyExit, "early exit became success");
        });
        check("registered app IDs expire on replacement and helper restart", delegate {
            Directory.CreateDirectory(root); try {
                string exe = Path.Combine(root, "registered game.exe"); File.WriteAllText(exe, "fixture");
                var service = new AgentService(root, 7, new EmptyHosts(), path => new HostProcess[0], profile => { throw new Exception("unused"); }, () => true);
                var reply = service.Handle(new AgentRequest { Command = "REGISTER_APP", ExecutablePath = exe }); Assert(reply.Apps.Any(a => a.Path == exe), "registered app missing: " + reply.Diagnostic + " entries=" + string.Join(",", reply.Apps.Select(a => a.Path))); var app = reply.Apps.First(a => a.Path == exe);
                File.WriteAllText(exe, "replacement fixture");
                reply = service.Handle(new AgentRequest { Command = "LAUNCH_APP", AppId = app.Id }); Assert(reply.Diagnostic.Contains("changed"), "replacement received old app approval");
                app = reply.Apps.First(a => a.Path == exe);
                var restarted = new AgentService(root, 7, new EmptyHosts(), path => new HostProcess[0], profile => { throw new Exception("unused"); }, () => true);
                Assert(restarted.Handle(new AgentRequest { Command = "LAUNCH_APP", AppId = app.Id }).Diagnostic.Contains("changed"), "old helper app ID accepted");
            } finally { Directory.Delete(root, true); }
        });
    }
    sealed class EmptyHosts : IHostDiscoverySource {
        public System.Collections.Generic.IEnumerable<HostCandidate> Candidates(string root) { return new HostCandidate[0]; }
        public bool Exists(string path) { return false; }
        public HostFileMetadata Inspect(string path) { throw new Exception("unused"); }
    }
}
