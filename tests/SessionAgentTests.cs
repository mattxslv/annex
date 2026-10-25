using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

static class SessionAgentTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static bool Refused(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
        catch (EndOfStreamException) { return true; }
    }
    public static void Run(Action<string, Action> check)
    {
        check("helper validates its bound session while child lookup belongs to the parent", delegate {
            Assert(PeerIdentity.MatchesChild(7, 7, true, 1, 1), "parent's actual child was rejected");
            Assert(PeerIdentity.IsHelperSession(7, 7, 1), "child helper incorrectly needs its own child session");
            Assert(!PeerIdentity.IsHelperSession(1, 1, 1), "console helper can launch child apps");
            Assert(!PeerIdentity.IsHelperSession(7, 8, 1) && !PeerIdentity.IsHelperSession(0, 0, 1), "unbound/service helper accepted");
        });
        check("agent permits only exact status and start commands", delegate {
            PipeProtocol.ValidateCommand("STATUS"); PipeProtocol.ValidateCommand("START");
            foreach (string command in new[] { "", "start", "START evil.exe", "STATUS\nSTART", "STOP", null })
                Assert(Refused(delegate { PipeProtocol.ValidateCommand(command); }), "command accepted: " + command);
        });
        check("pipe frame round trips Unicode diagnostic text", delegate {
            using (var stream = new MemoryStream()) {
                PipeProtocol.WriteText(stream, "Sunshine: 日本語", 1000); stream.Position = 0;
                Assert(PipeProtocol.ReadText(stream, 1000) == "Sunshine: 日本語", "frame changed text");
            }
        });
        check("pipe rejects oversized outgoing messages", delegate {
            using (var stream = new MemoryStream()) Assert(Refused(delegate { PipeProtocol.WriteText(stream, new string('a', 4097), 1000); }), "oversized write accepted");
        });
        check("pipe rejects oversized and negative frame lengths", delegate {
            foreach (int size in new[] { -1, 4097, int.MaxValue })
                using (var stream = new MemoryStream(BitConverter.GetBytes(size)))
                    Assert(Refused(delegate { PipeProtocol.ReadText(stream, 1000); }), "invalid frame length accepted");
        });
        check("pipe rejects truncated header and body", delegate {
            using (var stream = new MemoryStream(new byte[] { 1, 0 })) Assert(Refused(delegate { PipeProtocol.ReadText(stream, 1000); }), "truncated header accepted");
            using (var stream = new MemoryStream(new byte[] { 4, 0, 0, 0, 65 })) Assert(Refused(delegate { PipeProtocol.ReadText(stream, 1000); }), "truncated body accepted");
        });
        check("host reply preserves state, port, and bounded message", delegate {
            HostSnapshot reply = PipeProtocol.Decode(PipeProtocol.Encode(new HostSnapshot(HostState.Running, "hello\nworld | ✓", 48989)));
            Assert(reply.State == HostState.Running && reply.BasePort == 48989 && reply.Message == "hello\nworld | ✓", "reply changed");
        });
        check("host reply rejects invalid version, enum, port, and encoding", delegate {
            foreach (string reply in new[] { "", "2|Running|48989|", "1|99|48989|", "1|Running|65535|", "1|Running|0|", "1|Error|0|!!!", "1|Running|48989||extra" })
                Assert(Refused(delegate { PipeProtocol.Decode(reply); }), "invalid reply accepted: " + reply);
        });
        check("pipe names separate users, sessions, and installations", delegate {
            string root = Path.GetTempPath();
            string first = PipeIdentity.Name(root, "S-1-5-21-1", 7);
            Assert(first != PipeIdentity.Name(root, "S-1-5-21-2", 7), "users share pipe");
            Assert(first != PipeIdentity.Name(root, "S-1-5-21-1", 8), "sessions share pipe");
            Assert(first != PipeIdentity.Name(Path.Combine(root, "other"), "S-1-5-21-1", 7), "installations share pipe");
            Assert(first == PipeIdentity.Name(root.ToUpperInvariant(), "S-1-5-21-1", 7), "path casing breaks pipe association");
        });
        check("peer validation rejects wrong path or session", delegate {
            string path = Path.Combine(Path.GetTempPath(), "Annex.exe");
            Assert(PeerIdentity.IsServer(path, 7, path, 7), "valid helper refused");
            Assert(!PeerIdentity.IsServer(path, 1, path, 7), "console helper accepted");
            Assert(!PeerIdentity.IsServer(path + ".evil", 7, path, 7), "wrong executable accepted");
            Assert(!PeerIdentity.IsCaller(path, 7, path, 7), "same-session caller accepted");
            Assert(PeerIdentity.IsCaller(path, 1, path, 7), "parent caller refused");
        });
        check("target guard refuses console, self, service, and sentinel", delegate {
            Assert(!PeerIdentity.IsTarget(1, 1, 1), "console/self accepted");
            Assert(!PeerIdentity.IsTarget(0, 1, 1), "service session accepted");
            Assert(!PeerIdentity.IsTarget(uint.MaxValue, 1, 1), "sentinel accepted");
            Assert(!PeerIdentity.IsTarget(3, 3, 1), "own session accepted");
            Assert(PeerIdentity.IsTarget(7, 1, 1), "actual child refused");
        });
        check("requests reject a stale or failed Windows child lookup", delegate {
            Assert(PeerIdentity.MatchesChild(7, 7, true, 1, 1), "actual child refused");
            Assert(!PeerIdentity.MatchesChild(7, 8, true, 1, 1), "stale child target accepted");
            Assert(!PeerIdentity.MatchesChild(7, 7, false, 1, 1), "failed child lookup accepted");
        });
        check("helper ACL allows its user and denies network logons", delegate {
            using (var identity = WindowsIdentity.GetCurrent()) {
                var security = SessionAgent.CreatePipeSecurity(identity.User);
                bool userAllowed = false, networkDenied = false;
                var network = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
                foreach (PipeAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier))) {
                    if (rule.IdentityReference.Equals(identity.User) && rule.AccessControlType == AccessControlType.Allow) userAllowed = true;
                    if (rule.IdentityReference.Equals(network) && rule.AccessControlType == AccessControlType.Deny) networkDenied = true;
                }
                Assert(security.AreAccessRulesProtected && userAllowed && networkDenied, "pipe ACL does not enforce local-user boundary");
            }
        });
        check("real local pipe carries a bounded request and reply", delegate {
            string name = "Annex.Test." + Guid.NewGuid().ToString("N");
            using (var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)) {
                Task peer = Task.Factory.StartNew(delegate {
                    server.WaitForConnection();
                    Assert(PipeProtocol.ReadText(server, 2000) == "STATUS", "wrong request at server");
                    PipeProtocol.WriteText(server, PipeProtocol.Encode(new HostSnapshot(HostState.Stopped, "off", 48989)), 2000);
                });
                using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous)) {
                    client.Connect(2000); PipeProtocol.WriteText(client, "STATUS", 2000);
                    uint pid;
                    Assert(HostNative.GetNamedPipeServerProcessId(client.SafePipeHandle, out pid) && pid == (uint)Process.GetCurrentProcess().Id, "native server PID did not match actual peer");
                    HostProcess actual = HostNative.Peer(pid);
                    Assert(actual.SessionId == HostNative.CurrentSession() && HostController.SamePath(actual.ExecutablePath, typeof(SessionAgentTests).Assembly.Location), "native peer path/session inspection failed");
                    Assert(PipeProtocol.Decode(PipeProtocol.ReadText(client, 2000)).State == HostState.Stopped, "wrong server reply");
                }
                Assert(peer.Wait(3000), "server did not finish");
            }
        });
        check("unresponsive local pipe read times out", delegate {
            string name = "Annex.Test." + Guid.NewGuid().ToString("N");
            using (var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)) {
                var connected = server.BeginWaitForConnection(null, null);
                using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous)) {
                    client.Connect(2000); server.EndWaitForConnection(connected);
                    var clock = Stopwatch.StartNew(); bool timedOut = false;
                    try { PipeProtocol.ReadText(client, 100); } catch (TimeoutException) { timedOut = true; }
                    Assert(timedOut && clock.ElapsedMilliseconds < 5000, "pipe read hung or did not time out");
                }
            }
        });
        check("unavailable helper connect times out", delegate {
            using (var client = new NamedPipeClientStream(".", "Annex.Absent." + Guid.NewGuid().ToString("N"), PipeDirection.InOut, PipeOptions.Asynchronous)) {
                var clock = Stopwatch.StartNew(); bool timedOut = false;
                try { client.Connect(100); } catch (TimeoutException) { timedOut = true; }
                Assert(timedOut && clock.ElapsedMilliseconds < 5000, "absent helper connection hung");
            }
        });
    }
}
