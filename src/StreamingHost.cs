using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

enum HostState { Waiting, Checking, Stopped, Starting, Running, Missing, Unknown, Unavailable, Error }

sealed class HostSnapshot
{
    public HostState State { get; private set; }
    public string Message { get; private set; }
    public int BasePort { get; private set; }
    public HostSnapshot(HostState state, string message, int basePort)
    {
        State = state;
        Message = message ?? "";
        if (Message.Length > 512) Message = Message.Substring(0, 512);
        BasePort = basePort;
    }
}

sealed class HostConfiguration
{
    public string ExecutablePath { get; private set; }
    public string ConfigPath { get; private set; }
    public string BaseDirectory { get; private set; }

    public static HostConfiguration Load(string baseDirectory)
    {
        string root = Path.GetFullPath(baseDirectory);
        return new HostConfiguration {
            BaseDirectory = root,
            ExecutablePath = Path.Combine(root, "Sunshine", "Sunshine", "sunshine.exe"),
            ConfigPath = Path.Combine(root, "Sunshine", "Sunshine", "config", "sunshine.conf")
        };
    }

    public static HostConfiguration ForProfile(ChildHostProfile profile) { return new HostConfiguration { BaseDirectory = profile.DataDirectory, ExecutablePath = profile.Installation.ExecutablePath, ConfigPath = profile.ConfigPath }; }

    public static bool IsValidPort(int port) { return port >= 6 && port <= 65514; }

    public static int ParsePort(string text)
    {
        int port = 47989; // Sunshine's own default when its port setting is omitted.
        bool found = false;
        foreach (string source in (text ?? "").Split('\n'))
        {
            string line = source.Split('#')[0].Trim();
            int equals = line.IndexOf('=');
            if (equals < 0)
            {
                if (line == "port" || line.StartsWith("port ", StringComparison.Ordinal))
                    throw new InvalidDataException("Use port = <number> in sunshine.conf.");
                continue;
            }
            if (line.Substring(0, equals).Trim() != "port") continue;
            if (found || !int.TryParse(line.Substring(equals + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port) || !IsValidPort(port))
                throw new InvalidDataException("sunshine.conf must contain one valid base port from 6 to 65514.");
            found = true;
        }
        return port;
    }
}

sealed class HostProcess
{
    public int SessionId { get; private set; }
    public string ExecutablePath { get; private set; }
    public bool InspectionDenied { get; private set; }
    public int ProcessId { get; private set; }
    public long StartTimeUtcTicks { get; private set; }
    public string ConfigPath { get; private set; }
    public HostProcess(int sessionId, string executablePath, bool inspectionDenied)
        : this(sessionId, executablePath, inspectionDenied, 0, 0, null) { }
    public HostProcess(int sessionId, string executablePath, bool inspectionDenied, int pid, long ticks, string configPath)
    {
        SessionId = sessionId; ExecutablePath = executablePath; InspectionDenied = inspectionDenied;
        ProcessId = pid; StartTimeUtcTicks = ticks; ConfigPath = configPath;
    }
}

sealed class HostController
{
    // True once the host's web UI (base port + 1) accepts connections. Replaceable for tests.
    public static Func<int, bool> WebReady = port => {
        try { return System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port + 1); }
        catch (Exception) { return true; }
    };
    // Process ID that listens on a TCP port (0 if none). Replaceable for tests.
    public static Func<int, int> ListenerProcess = TcpListeners.OwnerOf;
    // Sunshine restarts itself only through its Windows service. Started by Annex, "Apply"/"Restart" in its web page
    // stops it, and it can hang half-closed with no listeners. Past this age without a web page it is treated as stalled.
    public static TimeSpan StallAfter = TimeSpan.FromSeconds(150);
    public static Func<DateTime> UtcNow = () => DateTime.UtcNow;
    const string StalledMessage = "Sunshine stopped answering. This happens after Apply or Restart on its web page: Sunshine only comes back by itself when it runs as a Windows service. Choose Start to run it again.";
    // Shared by every controller in this helper, so a rebuilt controller (after a settings save) still knows them.
    static readonly Dictionary<string, HostProcess> stalled = new Dictionary<string, HostProcess>();
    public static Action<HostProcess> EndStalled = HostNative.EndStalledHost;
    int webSeenProcess;
    readonly object gate = new object();
    readonly HostConfiguration config;
    readonly int sessionId;
    readonly Func<HostProcess[]> processes;
    readonly Action launch;
    readonly ChildHostProfile profile;
    readonly Func<HostLaunchReceipt> profileLaunch;
    HostLaunchReceipt receipt;
    bool starting;
    string lastError;

    public HostController(HostConfiguration config, int sessionId, Func<HostProcess[]> processes, Action launch)
    {
        if (sessionId <= 0) throw new ArgumentOutOfRangeException("sessionId");
        this.config = config; this.sessionId = sessionId; this.processes = processes; this.launch = launch;
    }

    public HostController(ChildHostProfile profile, int sessionId, Func<HostProcess[]> processes, Func<HostLaunchReceipt> launch)
        : this(HostConfiguration.ForProfile(profile), sessionId, processes, (Action)null)
    {
        this.profile = profile; profileLaunch = launch; receipt = HostLaunchReceipt.Load(profile.ReceiptPath);
    }

    public static bool SamePath(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return false;
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (PathTooLongException) { return false; }
    }

    public HostSnapshot GetStatus()
    {
        lock (gate) { return Inspect(); }
    }
    public bool StartInProgress { get { lock (gate) { return starting; } } }

    HostSnapshot Inspect()
    {
        if (!File.Exists(config.ExecutablePath))
            return new HostSnapshot(HostState.Missing, "Sunshine is not installed here. Run scripts\\setup.ps1 from the portable ZIP as administrator.", 0);
        if (!File.Exists(config.ConfigPath))
            return new HostSnapshot(HostState.Missing, "Sunshine configuration is missing. Run setup to create the host configuration.", 0);
        int port;
        try { port = HostConfiguration.ParsePort(File.ReadAllText(config.ConfigPath)); }
        catch (Exception ex) { return new HostSnapshot(HostState.Error, "Cannot read Sunshine settings: " + ex.Message, 0); }
        if (profile != null) {
            try { if (profile.CurrentIdentity() != profile.Identity) return new HostSnapshot(HostState.Unknown, "Host settings changed. Refresh the selected host before starting or pairing.", 0); }
            catch (Exception ex) { return new HostSnapshot(HostState.Error, "Cannot verify host settings: " + ex.Message, 0); }
        }
        bool unknown = false;
        try
        {
            HostProcess[] list = processes();
            lock (stalled) foreach (string key in stalled.Keys.Where(k => k.StartsWith(StallPrefix, StringComparison.Ordinal) && !list.Any(p => StallKey(p) == k)).ToArray()) stalled.Remove(key);
            foreach (HostProcess process in list)
            {
                if (process.SessionId >= 0 && process.SessionId != sessionId) continue;
                if (process.InspectionDenied) { unknown = true; continue; }
                if (process.SessionId == sessionId && SamePath(process.ExecutablePath, config.ExecutablePath))
                {
                    if (IsStalled(process)) continue;
                    if (profile != null && !OwnProcess(process, port)) { unknown = true; continue; }
                    // Sunshine needs ~30 s (encoder probing) before its web UI and pairing answer.
                    if (!WebReady(port)) {
                        bool lostWeb = webSeenProcess == process.ProcessId;
                        bool tooLong = process.StartTimeUtcTicks > 0 && UtcNow().Ticks - process.StartTimeUtcTicks > StallAfter.Ticks;
                        if (!starting && (lostWeb || tooLong)) { lock (stalled) stalled[StallKey(process)] = process; webSeenProcess = 0; return new HostSnapshot(HostState.Error, StalledMessage, port); }
                        lastError = null;
                        return new HostSnapshot(HostState.Starting, "Sunshine is starting up. Settings and pairing are ready in about half a minute.", port);
                    }
                    lastError = null; webSeenProcess = process.ProcessId;
                    return new HostSnapshot(HostState.Running, "The host is running in the Annex desktop. Streaming still depends on host capture and client settings." + (ElevationPolicy.LastLaunchNote ?? ""), port);
                }
            }
        }
        catch (Exception ex) { return new HostSnapshot(HostState.Unknown, "Cannot inspect the host process: " + ex.Message, port); }
        // During our own launch the new process shows up before its receipt is saved.
        if (unknown && starting) return new HostSnapshot(HostState.Starting, "Starting Sunshine. Approve Windows elevation inside the Annex desktop if asked.", port);
        if (StalledHosts().Length > 0 && !starting && lastError == null) return new HostSnapshot(HostState.Error, StalledMessage, port);
        if (unknown) return new HostSnapshot(HostState.Unknown, "A host may already be running, but its process or configuration could not be verified. Another will not be started. Restart that host through Annex to identify its settings.", 0);
        if (starting) return new HostSnapshot(HostState.Starting, "Starting Sunshine. Approve Windows elevation inside the Annex desktop if asked.", port);
        if (lastError != null) return new HostSnapshot(HostState.Error, lastError, port);
        return new HostSnapshot(HostState.Stopped, "Sunshine is off in the Annex desktop. Start it to make this desktop available to your client.", port);
    }

    // Ours if it is the process Annex launched, or if it serves this profile's own web port: saving settings in the
    // host's web page changes the profile identity, and "Apply" restarts the host under a new process ID.
    string StallPrefix { get { return (config.ExecutablePath ?? "").ToUpperInvariant() + "|"; } }
    string StallKey(HostProcess process) { return StallPrefix + process.ProcessId + ":" + process.StartTimeUtcTicks; }
    bool IsStalled(HostProcess process) { lock (stalled) return stalled.ContainsKey(StallKey(process)); }
    HostProcess[] StalledHosts() { lock (stalled) return stalled.Where(e => e.Key.StartsWith(StallPrefix, StringComparison.Ordinal)).Select(e => e.Value).ToArray(); }

    bool OwnProcess(HostProcess process, int port)
    {
        if (process.ConfigPath != null) return SamePath(process.ConfigPath, profile.ConfigPath);
        if (receipt != null && receipt.SameProcess(process)) return true;
        int owner = 0;
        try { if (HostConfiguration.IsValidPort(port)) owner = ListenerProcess(port + 1); } catch (Exception) { }
        return owner > 0 && owner == process.ProcessId;
    }

    public HostSnapshot RequestStart()
    {
        lock (gate)
        {
            HostSnapshot current = Inspect();
            if (starting || (current.State != HostState.Stopped && current.State != HostState.Error) || !HostConfiguration.IsValidPort(current.BasePort)) return current;
            starting = true;
            lastError = null;
            var worker = new Thread(delegate() {
                string error = null;
                try {
                    // Close a host that stopped answering first, so it does not linger next to the new one.
                    foreach (HostProcess old in StalledHosts()) { try { EndStalled(old); } catch (Exception) { } }
                    if (profile == null) launch();
                    else {
                        HostLaunchReceipt launched = profileLaunch();
                        if (launched == null || launched.ProfileIdentity != profile.Identity) throw new IOException("The launched host did not return a matching process identity.");
                        // Persist before publishing, so a restarted helper never misses a launch it reported.
                        try { launched.Save(profile.ReceiptPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                        lock (gate) { receipt = launched; }
                    }
                }
                catch (Win32Exception ex) {
                    error = ex.NativeErrorCode == 1223 ? "Windows canceled Sunshine startup. You can try again and approve elevation inside the Annex desktop." : "Could not start Sunshine: " + ex.Message;
                }
                catch (Exception ex) { error = "Could not start Sunshine: " + ex.Message; }
                finally { lock (gate) { starting = false; lastError = error; } }
            });
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            return new HostSnapshot(HostState.Starting, "Starting Sunshine. Check the Annex desktop for the Windows elevation prompt.", current.BasePort);
        }
    }
}

static class TcpListeners
{
    [System.Runtime.InteropServices.DllImport("iphlpapi.dll")]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    // Owner of an IPv4 or IPv6 TCP listener on the port, 0 if none.
    public static int OwnerOf(int port)
    {
        foreach (int family in new[] { 2, 23 }) {
            int rowSize = family == 2 ? 24 : 56, portOffset = family == 2 ? 8 : 20, pidOffset = family == 2 ? 20 : 52;
            int size = 0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 3 /* TCP_TABLE_OWNER_PID_LISTENER */, 0);
            for (int attempt = 0; attempt < 3 && size > 0; attempt++) {
                IntPtr table = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
                try {
                    uint result = GetExtendedTcpTable(table, ref size, false, family, 3, 0);
                    if (result == 122) continue; // grew meanwhile
                    if (result != 0) break;
                    int count = System.Runtime.InteropServices.Marshal.ReadInt32(table);
                    for (int i = 0; i < count; i++) {
                        IntPtr row = table + 4 + i * rowSize;
                        int raw = System.Runtime.InteropServices.Marshal.ReadInt32(row, portOffset);
                        if (((raw & 0xFF) << 8 | (raw >> 8) & 0xFF) == port) return System.Runtime.InteropServices.Marshal.ReadInt32(row, pidOffset);
                    }
                    break;
                } finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(table); }
            }
        }
        return 0;
    }
}
