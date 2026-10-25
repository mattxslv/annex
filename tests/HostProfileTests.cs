using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Net;
using System.Net.Sockets;

static class HostProfileTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "annex-profile-" + Guid.NewGuid().ToString("N"));
        public HostInstallation Host;
        public Fixture() {
            string path = Path.Combine(Root, "Installed host", "sunshine.exe"); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "fixture");
            Host = new HostInstallation(path, HostKind.Sunshine, new HostFileMetadata("Sunshine", "Sunshine", "2026.914.233613", "fixture"), "registry");
        }
        public ChildHostProfile Prepare() { return HostProfiles.Prepare(Root, Host, new NetworkPort[0]); }
        public void Dispose() { Directory.Delete(Root, true); }
    }
    public static void Run(Action<string, Action> check)
    {
        ChildHostProfile.ReceiptRoot = Path.Combine(Path.GetTempPath(), "annex-receipts-" + Guid.NewGuid().ToString("N"));
        check("hosts and configs the user can modify are never elevated", delegate {
            string dir = Path.Combine(Path.GetTempPath(), "annex-elevation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                string exe = Path.Combine(dir, "sunshine.exe"), conf = Path.Combine(dir, "sunshine.conf");
                File.WriteAllText(exe, "not executed"); File.WriteAllText(conf, "port = 48989");
                Assert(!ElevationPolicy.IsProtectedFile(exe) && !ElevationPolicy.IsProtectedFolder(dir), "user-owned files counted as protected");
                Assert(!ElevationPolicy.CanElevate(exe, conf), "user-writable host would be elevated");
                Assert(!ElevationPolicy.CanElevate(Path.Combine(dir, "missing.exe"), conf), "missing host would be elevated");
            } finally { Directory.Delete(dir, true); }
        });
        check("child profile isolates state credentials certificates apps and logs", delegate {
            using (var f = new Fixture()) {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(f.Host.ExecutablePath), "sunshine.conf"), "global_prep_cmd = secret-main-command");
                var p = f.Prepare(); string text = File.ReadAllText(p.ConfigPath);
                foreach (string key in new[] { "file_state", "credentials_file", "pkey", "cert", "file_apps", "log_path" })
                    Assert(text.Contains(key + " = " + p.DataDirectory), "path not isolated: " + key);
                Assert(!text.Contains("secret-main-command") && !HostController.SamePath(p.DataDirectory, Path.GetDirectoryName(f.Host.ExecutablePath)), "main config reused");
                File.WriteAllText(Path.Combine(p.DataDirectory, "write-test"), "ok");
                Assert(p.Arguments.Length == 1 && p.Arguments[0] == p.ConfigPath, "custom config not first native argument");
            }
        });
        check("managed child upgrades preserve configuration and paired device bytes", delegate {
            using (var f = new Fixture()) {
                var c = HostConfiguration.Load(f.Root); Directory.CreateDirectory(Path.GetDirectoryName(c.ConfigPath)); File.WriteAllText(c.ExecutablePath, "fixture");
                const string original = "# user settings\r\nport = 50123\r\ncapture = wgc\r\n"; File.WriteAllText(c.ConfigPath, original);
                string pairs = Path.Combine(Path.GetDirectoryName(c.ConfigPath), "sunshine_state.json"); File.WriteAllText(pairs, "paired-device-fixture");
                f.Host = new HostInstallation(c.ExecutablePath, HostKind.Sunshine, new HostFileMetadata("Sunshine", "Sunshine", "2026.914.233613", "x"), "managed");
                var p = f.Prepare(); Assert(p.ConfigPath == c.ConfigPath && p.BasePort == 50123, "legacy profile moved or port changed");
                Assert(File.ReadAllText(c.ConfigPath) == original && File.ReadAllText(pairs) == "paired-device-fixture", "config/pairings overwritten");
            }
        });
        check("host binary upgrades retain the same child data but expire launch identity", delegate {
            using (var f = new Fixture()) { var p = f.Prepare(); f.Host = new HostInstallation(f.Host.ExecutablePath, HostKind.Sunshine, new HostFileMetadata("Sunshine", "Sunshine", "2027.1", "new"), "registry"); var updated = f.Prepare(); Assert(p.DataDirectory == updated.DataDirectory && p.Identity != updated.Identity, "upgrade lost data or retained old approval"); }
        });
        check("existing valid child port is preserved even while listening", delegate {
            using (var f = new Fixture()) { var p = f.Prepare(); var again = HostProfiles.Prepare(f.Root, f.Host, HostPorts.Family(p.BasePort)); Assert(again.BasePort == p.BasePort, "running profile port silently reassigned"); }
        });
        check("invalid or locked profile remains an explicit error", delegate {
            using (var f = new Fixture()) {
                var p = f.Prepare(); File.WriteAllText(p.ConfigPath, "port = garbage"); bool bad = false;
                try { f.Prepare(); } catch (InvalidDataException) { bad = true; } Assert(bad, "malformed config replaced");
                using (var locked = new FileStream(p.ConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                    bad = false; try { f.Prepare(); } catch (IOException) { bad = true; } Assert(bad, "unreadable config hidden");
                }
            }
        });
        check("every derived TCP UDP listener collision skips the complete family", delegate {
            foreach (var member in HostPorts.Family(48989)) Assert(HostPorts.FindAvailable(new[] { member }, 48989, 49050) != 48989, "collision missed: " + member.Port);
            Assert(HostPorts.Family(48989).Length == 7, "wrong documented port family");
            Assert(HostPorts.FindAvailable(new[] { new NetworkPort(false, 48984) }, 48989, 49050) == 48989, "TCP and UDP conflated");
        });
        check("port selection rejects exhaustion and derived range overflow", delegate {
            bool bad = false; try { HostPorts.FindAvailable(HostPorts.Family(48989), 48989, 48989); } catch (IOException) { bad = true; } Assert(bad, "exhaustion guessed port");
            foreach (int port in new[] { 0, 1028, 65515, int.MaxValue }) { bad = false; try { HostPorts.Family(port); } catch (ArgumentOutOfRangeException) { bad = true; } Assert(bad, "invalid family accepted"); }
        });
        check("native collision inventory includes IPv4 and IPv6 listeners", delegate {
            foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback }) {
                if (address.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6) continue;
                var listener = new TcpListener(address, 0); listener.Start();
                try { int port = ((IPEndPoint)listener.LocalEndpoint).Port; Assert(HostPorts.Occupied().Any(p => p.Tcp && p.Port == port), "listener address family omitted"); }
                finally { listener.Stop(); }
            }
        });
        check("Apollo disables its verified display options and unknown versions are explicit", delegate {
            using (var f = new Fixture()) {
                f.Host = new HostInstallation(f.Host.ExecutablePath, HostKind.Apollo, new HostFileMetadata("Apollo", "Apollo", "0.4.0", "x"), "registry");
                string text = File.ReadAllText(f.Prepare().ConfigPath); Assert(text.Contains("isolated_virtual_display_option = disabled") && !text.Contains("virtual_display_mode"), "unverified Apollo flag emitted");
                f.Host = new HostInstallation(f.Host.ExecutablePath, HostKind.Sunshine, new HostFileMetadata("Sunshine", "Sunshine", "", "x"), "registry");
                bool bad = false; try { f.Prepare(); } catch (NotSupportedException) { bad = true; } Assert(bad, "unknown build silently supported");
            }
        });
        check("unverified VibePollo child coexistence is explicit rather than shared host state", delegate {
            using (var f = new Fixture()) { f.Host = new HostInstallation(f.Host.ExecutablePath, HostKind.VibePollo, new HostFileMetadata("VibePollo", "VibePollo", "1.0", "x"), "registry"); bool bad = false; try { f.Prepare(); } catch (NotSupportedException ex) { bad = ex.Message.Contains("VibePollo"); } Assert(bad, "unsafe fork silently launched"); }
        });
        check("profile controller never identifies unknown or different child config as running", delegate {
            using (var f = new Fixture()) { var p = f.Prepare();
                foreach (string config in new[] { null, Path.Combine(f.Root, "other.conf") }) {
                    var host = new HostController(p, 7, () => new[] { new HostProcess(7, p.Installation.ExecutablePath, false, 42, 123, config) }, () => { throw new Exception("must not launch"); });
                    Assert(host.GetStatus().State == HostState.Unknown && host.GetStatus().BasePort == 0, "unverified config advertised"); Assert(host.RequestStart().State == HostState.Unknown, "duplicate permitted");
                }
            }
        });
        check("profile process requires matching path session and validated config", delegate {
            using (var f = new Fixture()) { var p = f.Prepare(); HostProcess[] processes = { new HostProcess(1, p.Installation.ExecutablePath, false, 42, 123, p.ConfigPath) };
                var host = new HostController(p, 7, () => processes, () => { throw new Exception("unused"); }); Assert(host.GetStatus().State == HostState.Stopped, "console accepted");
                processes = new[] { new HostProcess(7, Path.Combine(f.Root, "other.exe"), false, 42, 123, p.ConfigPath) }; Assert(host.GetStatus().State == HostState.Stopped, "wrong executable accepted");
                processes = new[] { new HostProcess(7, p.Installation.ExecutablePath, false, 42, 123, p.ConfigPath) }; Assert(host.GetStatus().State == HostState.Running && host.GetStatus().BasePort == p.BasePort, "verified config not accepted");
            }
        });
        check("owned launch receipt survives helper restart but rejects PID reuse and edited config", delegate {
            using (var f = new Fixture()) { var p = f.Prepare(); HostProcess[] processes = new HostProcess[0];
                var host = new HostController(p, 7, () => processes, () => { processes = new[] { new HostProcess(7, p.Installation.ExecutablePath, false, 42, 123, null) }; return new HostLaunchReceipt(42, 123, p.Identity); });
                host.RequestStart(); Assert(SpinWait.SpinUntil(() => host.GetStatus().State == HostState.Running, 3000), "owned launch not verified");
                host = new HostController(f.Prepare(), 7, () => processes, () => { throw new Exception("unused"); }); Assert(host.GetStatus().State == HostState.Running, "receipt lost across restart");
                processes = new[] { new HostProcess(7, p.Installation.ExecutablePath, false, 42, 123, Path.Combine(f.Root, "different.conf")) }; Assert(host.GetStatus().State == HostState.Unknown, "receipt overrode a known different config");
                processes = new[] { new HostProcess(7, p.Installation.ExecutablePath, false, 42, 456, null) }; Assert(host.GetStatus().State == HostState.Unknown, "PID reuse accepted");
                processes = new[] { new HostProcess(7, p.Installation.ExecutablePath, false, 42, 123, null) }; File.AppendAllText(p.ConfigPath, "\nport = 50000"); Assert(host.GetStatus().State != HostState.Running, "edited launch config accepted");
            }
        });
        check("bind failure after preflight remains retryable and never running", delegate {
            using (var f = new Fixture()) { var p = f.Prepare(); var host = new HostController(p, 7, () => new HostProcess[0], () => { throw new IOException("address already in use"); }); host.RequestStart(); Assert(SpinWait.SpinUntil(() => host.GetStatus().State == HostState.Error, 3000), "bind failure hidden"); Assert(host.GetStatus().Message.Contains("address already in use"), "failure reason lost"); }
        });
    }
}
