using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

static class StreamingHostTests
{
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "annex-host-" + Guid.NewGuid().ToString("N"));
        public readonly HostConfiguration Config;
        public HostProcess[] Processes = new HostProcess[0];
        public int Starts;
        public Action Launch;
        public Fixture()
        {
            Config = HostConfiguration.Load(Root);
            Directory.CreateDirectory(Path.GetDirectoryName(Config.ExecutablePath));
            Directory.CreateDirectory(Path.GetDirectoryName(Config.ConfigPath));
            File.WriteAllText(Config.ExecutablePath, "never executed by tests");
            File.WriteAllText(Config.ConfigPath, "port = 48989\ncapture = wgc\n");
            Launch = delegate { Interlocked.Increment(ref Starts); Processes = new[] { new HostProcess(7, Config.ExecutablePath, false) }; };
        }
        public HostController Controller()
        {
            return new HostController(Config, 7, delegate { return Processes; }, delegate { Launch(); });
        }
        public void Dispose() { Directory.Delete(Root, true); }
    }

    public static void Run(Action<string, Action> check)
    {
        check("host port uses Sunshine default when absent", delegate { Assert(HostConfiguration.ParsePort("capture = wgc") == 47989, "wrong default"); });
        check("host port reads managed port and comments", delegate { Assert(HostConfiguration.ParsePort("# port = 47989\n port = 48989 # child\n") == 48989, "wrong managed port"); });
        check("host port permits full derived port range", delegate { Assert(HostConfiguration.ParsePort("port=6") == 6 && HostConfiguration.ParsePort("port=65514") == 65514, "valid boundary rejected"); });
        string[] invalid = { "port=0", "port=5", "port=65515", "port=-1", "port=+48989", "port=abc", "port=", "port 48989", "port=999999999999", "port=48989\nport=50000" };
        foreach (string input in invalid)
        {
            string value = input;
            check("reject invalid host port: " + value.Replace('\n', ' '), delegate {
                bool refused = false;
                try { HostConfiguration.ParsePort(value); } catch (InvalidDataException) { refused = true; }
                Assert(refused, "malformed/out-of-range port accepted");
            });
        }
        check("host stopped when no matching process", delegate { using (var f = new Fixture()) Assert(f.Controller().GetStatus().State == HostState.Stopped, "not stopped"); });
        check("host running only at expected path and session", delegate {
            using (var f = new Fixture()) {
                f.Processes = new[] { new HostProcess(7, f.Config.ExecutablePath.ToUpperInvariant(), false) };
                Assert(f.Controller().GetStatus().State == HostState.Running, "matching host not detected");
            }
        });
        check("console host does not count as child host", delegate {
            using (var f = new Fixture()) {
                f.Processes = new[] { new HostProcess(1, f.Config.ExecutablePath, false) };
                Assert(f.Controller().GetStatus().State == HostState.Stopped, "wrong-session host accepted");
            }
        });
        check("different installation does not count as child host", delegate {
            using (var f = new Fixture()) {
                f.Processes = new[] { new HostProcess(7, Path.Combine(f.Root, "other", "sunshine.exe"), false) };
                Assert(f.Controller().GetStatus().State == HostState.Stopped, "wrong executable accepted");
            }
        });
        check("denied process inspection prevents duplicate start", delegate {
            using (var f = new Fixture()) {
                f.Processes = new[] { new HostProcess(7, null, true) };
                var host = f.Controller();
                Assert(host.GetStatus().State == HostState.Unknown, "inaccessible host treated as stopped");
                host.RequestStart();
                Assert(f.Starts == 0, "host started despite denied inspection");
            }
        });
        check("denied inspection in another known session is ignored", delegate {
            using (var f = new Fixture()) {
                f.Processes = new[] { new HostProcess(1, null, true) };
                Assert(f.Controller().GetStatus().State == HostState.Stopped, "other session blocked this session");
            }
        });
        check("missing executable never starts a host", delegate {
            using (var f = new Fixture()) {
                File.Delete(f.Config.ExecutablePath);
                Assert(f.Controller().RequestStart().State == HostState.Missing && f.Starts == 0, "missing host started");
            }
        });
        check("missing config never starts a host", delegate {
            using (var f = new Fixture()) {
                File.Delete(f.Config.ConfigPath);
                Assert(f.Controller().RequestStart().State == HostState.Missing && f.Starts == 0, "missing config accepted");
            }
        });
        check("invalid config never starts or advertises a bad port", delegate {
            using (var f = new Fixture()) {
                File.WriteAllText(f.Config.ConfigPath, "port = 65535");
                HostSnapshot status = f.Controller().RequestStart();
                Assert(status.State == HostState.Error && status.BasePort == 0 && f.Starts == 0, "invalid config started or exposed URL");
            }
        });
        check("stopped host starts once and becomes running", delegate {
            using (var f = new Fixture()) {
                var host = f.Controller();
                host.RequestStart();
                Assert(SpinWait.SpinUntil(delegate { return host.GetStatus().State == HostState.Running; }, 3000), "host did not start");
                host.RequestStart();
                Assert(f.Starts == 1, "running host launched twice");
            }
        });
        check("concurrent requests and delayed elevation launch once", delegate {
            using (var f = new Fixture())
            using (var entered = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false)) {
                f.Launch = delegate { Interlocked.Increment(ref f.Starts); entered.Set(); release.WaitOne(); f.Processes = new[] { new HostProcess(7, f.Config.ExecutablePath, false) }; };
                var host = f.Controller();
                host.RequestStart();
                Assert(entered.WaitOne(3000), "launch worker did not start");
                try {
                    Parallel.For(0, 10, delegate(int i) { Assert(host.RequestStart().State == HostState.Starting, "delayed launch not in progress"); });
                    Assert(f.Starts == 1, "duplicate start during pending elevation");
                }
                finally { release.Set(); }
                Assert(SpinWait.SpinUntil(delegate { return host.GetStatus().State == HostState.Running; }, 3000), "worker did not finish");
            }
        });
        check("canceled elevation is reported and can be retried", delegate {
            using (var f = new Fixture()) {
                f.Launch = delegate { Interlocked.Increment(ref f.Starts); throw new Win32Exception(1223); };
                var host = f.Controller(); host.RequestStart();
                Assert(SpinWait.SpinUntil(delegate { return host.GetStatus().State == HostState.Error; }, 3000), "cancellation not reported");
                Assert(host.GetStatus().Message.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0, "cancellation not actionable");
                f.Launch = delegate { Interlocked.Increment(ref f.Starts); f.Processes = new[] { new HostProcess(7, f.Config.ExecutablePath, false) }; };
                host.RequestStart();
                Assert(SpinWait.SpinUntil(delegate { return host.GetStatus().State == HostState.Running; }, 3000), "retry did not recover");
                Assert(f.Starts == 2, "unexpected launch count");
            }
        });
        check("early host exit does not become running", delegate {
            using (var f = new Fixture()) {
                f.Launch = delegate { throw new IOException("Sunshine exited during startup"); };
                var host = f.Controller(); host.RequestStart();
                Assert(SpinWait.SpinUntil(delegate { return host.GetStatus().State == HostState.Error; }, 3000), "early exit hidden");
            }
        });
    }
}
