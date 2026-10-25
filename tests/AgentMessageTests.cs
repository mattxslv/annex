using System;
using System.IO;
using System.Linq;
using System.Threading;

static class AgentMessageTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static bool Refused(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
    sealed class Source : IHostDiscoverySource {
        public HostCandidate Candidate; public string Version = "2026.914.233613"; public bool Fail;
        public System.Collections.Generic.IEnumerable<HostCandidate> Candidates(string root) { if (Fail) throw new IOException("fixture discovery failed"); return Candidate == null ? new HostCandidate[0] : new[] { Candidate }; }
        public bool Exists(string path) { return Candidate != null && File.Exists(path); }
        public HostFileMetadata Inspect(string path) { return new HostFileMetadata("Sunshine", "Sunshine", Version, Version); }
    }
    public static void Run(Action<string, Action> check) {
        check("v2 request and reply preserve Unicode paths and typed host identity", delegate {
            var request = new AgentRequest { Command = "REGISTER_APP", ExecutablePath = @"C:\Games & tools\日本語 app.exe" };
            Assert(AgentCodec.DecodeRequest(AgentCodec.Encode(request)).ExecutablePath == request.ExecutablePath, "native path changed");
            var reply = new AgentReply { SelectionId = new string('a', 64), Status = new HostSnapshot(HostState.Stopped, "✓ off", 48989), Hosts = new[] { new AgentHost { Id = new string('b', 64), Name = "Sunshine", Path = @"C:\日本語\sunshine.exe", Version = "2026.1" } } };
            var decoded = AgentCodec.DecodeReply(AgentCodec.Encode(reply)); Assert(decoded.Hosts[0].Path == reply.Hosts[0].Path && decoded.Status.Message == "✓ off", "reply changed");
        });
        check("v2 rejects old versions unknown commands fields types and oversized payloads", delegate {
            foreach (string json in new[] { "{}", "{\"Version\":1,\"Command\":\"STATUS\"}", "{\"Version\":2,\"Command\":\"START evil\"}", "{\"Version\":2,\"Command\":\"STATUS\",\"Extra\":1}", "{\"Version\":2,\"Command\":7}", "{\"Version\":2,\"Command\":\"STATUS\",\"SelectionId\":false}", "{\"Version\":2,\"Command\":\"STATUS\"", "[]", "null" }) Assert(Refused(() => AgentCodec.DecodeRequest(json)), "invalid request accepted: " + json);
            Assert(Refused(() => AgentCodec.Encode(new AgentRequest { Command = "REGISTER_APP", ExecutablePath = new string('x', 2049) })), "oversized path accepted");
            Assert(Refused(() => AgentCodec.DecodeReply("1|Missing|0|")), "old helper accepted without upgrade");
            Assert(Refused(() => AgentCodec.DecodeReply("{\"Version\":2,\"State\":\"999\",\"Port\":0}")), "invalid enum accepted");
        });
        check("host selection changes expire confirmation on the same child connection", delegate {
            var gate = new SessionRequestGate(); gate.Update(7, true); gate.Select("first"); var confirmation = new SessionConfirmation(gate, 7); int token = gate.Generation;
            gate.Select("second"); Assert(!gate.Accepts(token, 7) && !confirmation.CanProceed(7), "selection retained old approval");
        });
        check("helper distinguishes a missing host and refuses stale selection or replacement", delegate {
            string root = Path.Combine(Path.GetTempPath(), "annex-agent-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                var source = new Source(); int launches = 0;
                var service = new AgentService(root, 7, source, path => new HostProcess[0], profile => { launches++; throw new IOException("fixture"); }, () => true);
                Assert(service.Handle(new AgentRequest { Command = "STATUS" }).Status.State == HostState.Missing, "missing host became missing helper");
                string exe = Path.Combine(root, "sunshine.exe"); File.WriteAllText(exe, "fixture"); source.Candidate = new HostCandidate(exe, HostKind.Sunshine, "registry");
                var reply = service.Handle(new AgentRequest { Command = "STATUS" }); Assert(reply.Status.State == HostState.Stopped && reply.SelectionId.Length == 64, "newly installed host ignored");
                var restarted = new AgentService(root, 7, source, path => new HostProcess[0], profile => { launches++; throw new IOException("fixture"); }, () => true);
                Assert(restarted.Handle(new AgentRequest { Command = "START_HOST", SelectionId = reply.SelectionId }).Diagnostic.Contains("changed"), "old helper selection accepted");
                source.Version = "2027.1"; var replaced = service.Handle(new AgentRequest { Command = "START_HOST", SelectionId = reply.SelectionId });
                Assert(launches == 0 && replaced.Diagnostic.Contains("changed"), "replacement launched under old approval");
                source.Candidate = null; Assert(service.Handle(new AgentRequest { Command = "START_HOST", SelectionId = replaced.SelectionId }).Status.State == HostState.Missing && launches == 0, "removed host launched");
            } finally { Directory.Delete(root, true); }
        });
        check("helper launch boundary refuses a non-child context", delegate {
            string root = Path.Combine(Path.GetTempPath(), "annex-agent-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try { string exe = Path.Combine(root, "sunshine.exe"); File.WriteAllText(exe, "fixture"); var source = new Source { Candidate = new HostCandidate(exe, HostKind.Sunshine, "registry") }; int launches = 0;
                var service = new AgentService(root, 7, source, path => new HostProcess[0], profile => { launches++; throw new Exception("must not launch"); }, () => false);
                var status = service.Handle(new AgentRequest { Command = "STATUS" }); var result = service.Handle(new AgentRequest { Command = "START_HOST", SelectionId = status.SelectionId }); Assert(result.Status.State == HostState.Unavailable && launches == 0, "non-child start authorized");
            } finally { Directory.Delete(root, true); }
        });
        check("edited child profile expires the previous host startup approval", delegate {
            string root = Path.Combine(Path.GetTempPath(), "annex-review-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try {
                string exe = Path.Combine(root, "sunshine.exe"); File.WriteAllText(exe, "fixture"); var source = new Source { Candidate = new HostCandidate(exe, HostKind.Sunshine, "registry") }; int starts = 0;
                var service = new AgentService(root, 7, source, path => new HostProcess[0], profile => { Interlocked.Increment(ref starts); throw new IOException("fixture launch"); }, () => true);
                var before = service.Handle(new AgentRequest { Command = "STATUS" }); string config = Directory.GetFiles(Path.Combine(root, "Hosts"), "sunshine.conf", SearchOption.AllDirectories).Single();
                File.WriteAllText(config, "port = 49099\ncapture = wgc\n");
                var after = service.Handle(new AgentRequest { Command = "START_HOST", SelectionId = before.SelectionId });
                Assert(after.Status.State != HostState.Starting && after.SelectionId != before.SelectionId && starts == 0, "edited config launched using old approval");
            } finally { Directory.Delete(root, true); }
        });
        check("pending host launch survives config refresh without a second dispatch", delegate {
            string root = Path.Combine(Path.GetTempPath(), "annex-review-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            using (var entered = new ManualResetEvent(false)) using (var release = new ManualResetEvent(false)) {
                AgentService service = null;
                try {
                    string exe = Path.Combine(root, "sunshine.exe"); File.WriteAllText(exe, "fixture"); var source = new Source { Candidate = new HostCandidate(exe, HostKind.Sunshine, "registry") }; int starts = 0;
                    service = new AgentService(root, 7, source, path => new HostProcess[0], profile => { Interlocked.Increment(ref starts); entered.Set(); release.WaitOne(); return new HostLaunchReceipt(42, 123, profile.Identity); }, () => true);
                    var before = service.Handle(new AgentRequest { Command = "STATUS" }); service.Handle(new AgentRequest { Command = "START_HOST", SelectionId = before.SelectionId }); Assert(entered.WaitOne(3000), "fixture launch not entered");
                    string config = Directory.GetFiles(Path.Combine(root, "Hosts"), "sunshine.conf", SearchOption.AllDirectories).Single(); File.WriteAllText(config, "port = 49199\ncapture = wgc\n");
                    var refresh = service.Handle(new AgentRequest { Command = "STATUS" }); Assert(refresh.Status.State == HostState.Starting, "refresh lost pending launch ownership");
                    service.Handle(new AgentRequest { Command = "START_HOST", SelectionId = refresh.SelectionId });
                    Assert(!SpinWait.SpinUntil(() => Interlocked.CompareExchange(ref starts, 0, 0) > 1, 150), "second launch dispatched during pending elevation");
                } finally { release.Set(); if (service != null) SpinWait.SpinUntil(() => service.Handle(new AgentRequest { Command = "STATUS" }).Status.State != HostState.Starting, 3000); Directory.Delete(root, true); }
            }
        });
        check("helper discovery failure remains unknown rather than confirmed host absence", delegate {
            var service = new AgentService(Path.GetTempPath(), 7, new Source { Fail = true }, path => new HostProcess[0], profile => { throw new Exception("must not launch"); }, () => true);
            var reply = service.Handle(new AgentRequest { Command = "STATUS" }); Assert(reply.Status.State == HostState.Unknown && reply.Diagnostic.Contains("fixture discovery failed"), "failed inspection labeled no host");
        });
    }
}
