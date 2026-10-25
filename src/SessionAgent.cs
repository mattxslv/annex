using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

static class PipeIdentity
{
    public static string Name(string root, string sid, uint session)
    {
        string normalized = Path.GetFullPath(root).TrimEnd('\\', '/').ToUpperInvariant();
        using (var hash = SHA256.Create())
            return "Annex." + sid + "." + session.ToString(CultureInfo.InvariantCulture) + "." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(normalized))).Replace("-", "").Substring(0, 20);
    }
}

static class PeerIdentity
{
    public static bool IsTarget(uint target, uint current, uint console) { return target > 0 && target <= int.MaxValue && target != current && target != console; }
    public static bool MatchesChild(uint target, uint reported, bool querySucceeded, uint current, uint console) { return querySucceeded && target == reported && IsTarget(target, current, console); }
    public static bool IsHelperSession(int current, int boundSession, uint console) { return current > 0 && current == boundSession && (uint)current != console; }
    public static bool IsServer(string path, int session, string expectedPath, uint expectedSession)
    {
        return expectedSession > 0 && expectedSession <= int.MaxValue && session == (int)expectedSession && HostController.SamePath(path, expectedPath);
    }
    public static bool IsCaller(string path, int session, string expectedPath, int serverSession)
    {
        return session > 0 && session != serverSession && HostController.SamePath(path, expectedPath);
    }
}

static class PipeProtocol
{
    const int MaxFrame = 4096;
    static readonly Encoding utf8 = new UTF8Encoding(false, true);
    public static void ValidateCommand(string command)
    {
        if (command != "STATUS" && command != "START") throw new InvalidDataException("Unknown helper command.");
    }
    static void Wait(Task operation, Stream stream, int remaining)
    {
        try {
            if (remaining <= 0 || !operation.Wait(remaining)) {
                stream.Dispose();
                operation.ContinueWith(t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException("The child helper did not respond in time.");
            }
        }
        catch (AggregateException ex) { throw new IOException("Pipe operation failed.", ex.GetBaseException()); }
    }
    static byte[] ReadBytes(Stream stream, int count, Stopwatch clock, int timeout)
    {
        byte[] bytes = new byte[count]; int offset = 0;
        while (offset < count) {
            Task<int> operation = stream.ReadAsync(bytes, offset, count - offset);
            Wait(operation, stream, timeout - (int)clock.ElapsedMilliseconds);
            int read = operation.Result;
            if (read == 0) throw new EndOfStreamException("The child helper disconnected.");
            offset += read;
        }
        return bytes;
    }
    public static string ReadText(Stream stream, int timeout)
    {
        var clock = Stopwatch.StartNew();
        int length = BitConverter.ToInt32(ReadBytes(stream, 4, clock, timeout), 0);
        if (length < 0 || length > MaxFrame) throw new InvalidDataException("Invalid helper frame length.");
        try { return utf8.GetString(ReadBytes(stream, length, clock, timeout)); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Invalid helper text.", ex); }
    }
    public static void WriteText(Stream stream, string text, int timeout)
    {
        byte[] body;
        try { body = utf8.GetBytes(text ?? ""); }
        catch (EncoderFallbackException ex) { throw new InvalidDataException("Invalid helper text.", ex); }
        if (body.Length > MaxFrame) throw new InvalidDataException("Helper message is too large.");
        byte[] packet = new byte[body.Length + 4];
        Buffer.BlockCopy(BitConverter.GetBytes(body.Length), 0, packet, 0, 4);
        Buffer.BlockCopy(body, 0, packet, 4, body.Length);
        Wait(stream.WriteAsync(packet, 0, packet.Length), stream, timeout);
    }
    public static string Encode(HostSnapshot status)
    {
        return "1|" + status.State + "|" + status.BasePort.ToString(CultureInfo.InvariantCulture) + "|" + Convert.ToBase64String(utf8.GetBytes(status.Message));
    }
    public static HostSnapshot Decode(string reply)
    {
        string[] fields = (reply ?? "").Split('|'); HostState state; int port;
        if (fields.Length != 4 || fields[0] != "1" || !Enum.TryParse(fields[1], out state) || state.ToString() != fields[1] || !Enum.IsDefined(typeof(HostState), state) ||
            !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out port) || (port != 0 && !HostConfiguration.IsValidPort(port)) ||
            ((state == HostState.Running || state == HostState.Stopped || state == HostState.Starting) && port == 0)) throw new InvalidDataException("Invalid child helper response.");
        try { return new HostSnapshot(state, utf8.GetString(Convert.FromBase64String(fields[3])), port); }
        catch (FormatException ex) { throw new InvalidDataException("Invalid child helper response.", ex); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Invalid child helper response.", ex); }
    }
}

static class HostNative
{
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint length);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint id);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint id);
    [DllImport("kernel32.dll")] public static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("wtsapi32.dll", SetLastError = true, CallingConvention = CallingConvention.Cdecl)] public static extern bool WTSGetChildSessionId(out uint session);
    public static int CurrentSession() { using (var process = Process.GetCurrentProcess()) return process.SessionId; }
    public static string ProcessPath(uint pid)
    {
        IntPtr handle = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            var path = new StringBuilder(32768); uint size = (uint)path.Capacity;
            if (!QueryFullProcessImageName(handle, 0, path, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return path.ToString();
        }
        finally { CloseHandle(handle); }
    }
    public static HostProcess Peer(uint pid)
    {
        using (var process = Process.GetProcessById(checked((int)pid))) return new HostProcess(process.SessionId, ProcessPath(pid), false);
    }
    public static HostProcess[] Processes(string executablePath)
    {
        var result = new List<HostProcess>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath)))
        using (process) {
            int session = -1;
            try { session = process.SessionId; result.Add(new HostProcess(session, ProcessPath((uint)process.Id), false, process.Id, ProcessStartTicks(process.Id), null)); }
            catch (InvalidOperationException) { } // Process exited during enumeration.
            catch (ArgumentException) { }
            catch (Win32Exception) { result.Add(new HostProcess(session, null, true)); }
        }
        return result.ToArray();
    }
    static long ProcessStartTicks(int pid) {
        IntPtr handle = OpenProcess(0x1000, false, (uint)pid);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try { long created, exited, kernel, user; if (!GetProcessTimes(handle, out created, out exited, out kernel, out user)) throw new Win32Exception(Marshal.GetLastWin32Error()); return DateTime.FromFileTimeUtc(created).Ticks; }
        finally { CloseHandle(handle); }
    }
    public static HostLaunchReceipt StartHost(ChildHostProfile profile) {
        if (profile.CurrentIdentity() != profile.Identity) throw new IOException("The child host configuration changed. Refresh and confirm startup again.");
        if (HostPorts.Collides(profile.BasePort, HostPorts.Occupied())) throw new IOException("A child-host TCP or UDP port is already in use. No existing listener was stopped. Close the conflicting app or change the child configuration.");
        HostLaunchSpec spec = HostProfiles.Adapter(profile.Installation).BuildLaunch(profile);
        var start = new ProcessStartInfo(spec.Executable, NativeArguments.Join(spec.Arguments)) {
            UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = spec.WorkingDirectory
        };
        using (var process = Process.Start(start)) {
            if (process == null) throw new IOException("Windows did not return a child host process.");
            long ticks = ProcessStartTicks(process.Id);
            if (process.WaitForExit(1200)) throw new IOException("The child host exited during startup (exit code " + process.ExitCode + "). Check " + Path.Combine(profile.DataDirectory, "sunshine.log") + ".");
            if (process.SessionId != CurrentSession() || !HostController.SamePath(ProcessPath((uint)process.Id), spec.Executable)) throw new IOException("The host did not start in this child desktop.");
            return new HostLaunchReceipt(process.Id, ticks, profile.Identity);
        }
    }
    public static void StartHost(HostConfiguration config)
    {
        var start = new ProcessStartInfo(config.ExecutablePath, "\"" + config.ConfigPath + "\"") {
            UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(config.ExecutablePath)
        };
        using (var process = Process.Start(start)) {
            if (process == null) throw new IOException("Windows did not return a Sunshine process.");
            if (process.WaitForExit(1200)) throw new IOException("Sunshine exited during startup (exit code " + process.ExitCode + "). Check its configuration and logs.");
        }
    }
}

sealed class AgentClient
{
    readonly string root;
    public AgentClient(string baseDirectory) { root = baseDirectory; }
    public HostSnapshot Request(uint childSessionId, string command)
    {
        if (command != "STATUS") return new HostSnapshot(HostState.Unavailable, "Use the updated host selection flow before confirming startup.", 0);
        return Request(childSessionId, new AgentRequest { Command = "STATUS" }).Status;
    }
    public AgentReply Request(uint childSessionId, AgentRequest request)
    {
        try {
            string encoded = AgentCodec.Encode(request);
            uint reported;
            bool found = HostNative.WTSGetChildSessionId(out reported);
            if (!PeerIdentity.MatchesChild(childSessionId, reported, found, (uint)HostNative.CurrentSession(), HostNative.WTSGetActiveConsoleSessionId())) return new AgentReply { Status = new HostSnapshot(HostState.Unavailable, "Connect the actual child desktop before controlling its host.", 0) };
            string sid; using (var identity = WindowsIdentity.GetCurrent()) sid = identity.User.Value;
            using (var pipe = new NamedPipeClientStream(".", PipeIdentity.Name(root, sid, childSessionId), PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification)) {
                pipe.Connect(750); uint pid;
                if (!HostNative.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out pid)) throw new Win32Exception(Marshal.GetLastWin32Error());
                HostProcess peer = HostNative.Peer(pid);
                if (!PeerIdentity.IsServer(peer.ExecutablePath, peer.SessionId, System.Windows.Forms.Application.ExecutablePath, childSessionId)) throw new UnauthorizedAccessException("The helper's application or Windows session does not match.");
                PipeProtocol.WriteText(pipe, encoded, 2000);
                return AgentCodec.DecodeReply(PipeProtocol.ReadText(pipe, 2500));
            }
        }
        catch (InvalidDataException) { return new AgentReply { Status = new HostSnapshot(HostState.Unavailable, "Child helper upgrade/repair required. Restart this folder's Annex.exe -agent inside the child desktop, or use Setup / repair and reconnect after saving work. Folder: " + root, 0) }; }
        catch (Exception ex) { return new AgentReply { Status = new HostSnapshot(HostState.Unavailable, "Child helper unavailable for " + root + ". Use Setup / repair, then restart this folder's helper inside the child desktop. " + ex.Message, 0) }; }
    }
}

static class SessionAgent
{
    public static PipeSecurity CreatePipeSecurity(SecurityIdentifier user)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }
    static void Log(string root, string message)
    {
        try { File.AppendAllText(Path.Combine(root, "autostart.log"), DateTime.Now.ToString("o") + " " + message + Environment.NewLine); } catch { }
    }
    public static int Run(string baseDirectory)
    {
        try {
            int session = HostNative.CurrentSession(); if (session <= 0) return 1;
            SecurityIdentifier user; using (var identity = WindowsIdentity.GetCurrent()) user = identity.User;
            string name = PipeIdentity.Name(baseDirectory, user.Value, (uint)session); bool created;
            using (var singleton = new Mutex(false, "Local\\" + name + ".Agent", out created)) {
                if (!created) return 0;
                // WTSGetChildSessionId is scoped to the calling session. The parent
                // AgentClient checks its actual child before EACH request, then both
                // peers verify native path/session identity. A child has no child of
                // its own: here validate the helper's bound, non-console session.
                var service = new AgentService(baseDirectory, session, new NativeHostDiscovery(), HostNative.Processes, HostNative.StartHost,
                    () => PeerIdentity.IsHelperSession(HostNative.CurrentSession(), session, HostNative.WTSGetActiveConsoleSessionId()));
                var security = CreatePipeSecurity(user);
                for (;;)
                using (var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security)) {
                    pipe.WaitForConnection();
                    try {
                        uint pid;
                        if (!HostNative.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out pid)) throw new Win32Exception(Marshal.GetLastWin32Error());
                        HostProcess peer = HostNative.Peer(pid);
                        if (!PeerIdentity.IsCaller(peer.ExecutablePath, peer.SessionId, System.Windows.Forms.Application.ExecutablePath, session)) throw new UnauthorizedAccessException("Only this install's launcher in a parent session may control its host.");
                        string command = PipeProtocol.ReadText(pipe, 2000);
                        AgentReply reply;
                        try { reply = service.Handle(AgentCodec.DecodeRequest(command)); }
                        catch (InvalidDataException) { reply = new AgentReply { Status = new HostSnapshot(HostState.Unavailable, "Helper upgrade/repair required. This helper requires protocol version 2.", 0) }; }
                        PipeProtocol.WriteText(pipe, AgentCodec.Encode(reply), 2000);
                    }
                    catch (Exception ex) { Log(baseDirectory, "helper request failed: " + ex.Message); }
                }
            }
        }
        catch (Exception ex) { Log(baseDirectory, "helper failed: " + ex.Message); return 1; }
    }
}
