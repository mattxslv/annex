using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
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
    public HostProcess(int sessionId, string executablePath, bool inspectionDenied)
    {
        SessionId = sessionId; ExecutablePath = executablePath; InspectionDenied = inspectionDenied;
    }
}

sealed class HostController
{
    readonly object gate = new object();
    readonly HostConfiguration config;
    readonly int sessionId;
    readonly Func<HostProcess[]> processes;
    readonly Action launch;
    bool starting;
    string lastError;

    public HostController(HostConfiguration config, int sessionId, Func<HostProcess[]> processes, Action launch)
    {
        if (sessionId <= 0) throw new ArgumentOutOfRangeException("sessionId");
        this.config = config; this.sessionId = sessionId; this.processes = processes; this.launch = launch;
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

    HostSnapshot Inspect()
    {
        if (!File.Exists(config.ExecutablePath))
            return new HostSnapshot(HostState.Missing, "Sunshine is not installed here. Run scripts\\setup.ps1 from the portable ZIP as administrator.", 0);
        if (!File.Exists(config.ConfigPath))
            return new HostSnapshot(HostState.Missing, "Sunshine configuration is missing. Run setup to create the child host configuration.", 0);
        int port;
        try { port = HostConfiguration.ParsePort(File.ReadAllText(config.ConfigPath)); }
        catch (Exception ex) { return new HostSnapshot(HostState.Error, "Cannot read Sunshine settings: " + ex.Message, 0); }
        bool unknown = false;
        try
        {
            foreach (HostProcess process in processes())
            {
                if (process.SessionId >= 0 && process.SessionId != sessionId) continue;
                if (process.InspectionDenied) { unknown = true; continue; }
                if (process.SessionId == sessionId && SamePath(process.ExecutablePath, config.ExecutablePath))
                {
                    lastError = null;
                    return new HostSnapshot(HostState.Running, "Sunshine is running in the child desktop. Pairing and capture depend on your host settings.", port);
                }
            }
        }
        catch (Exception ex) { return new HostSnapshot(HostState.Unknown, "Cannot inspect the host process: " + ex.Message, port); }
        if (unknown) return new HostSnapshot(HostState.Unknown, "Windows denied host-process inspection. A host may already be running; another will not be started.", port);
        if (starting) return new HostSnapshot(HostState.Starting, "Starting Sunshine. Approve Windows elevation inside the child desktop if asked.", port);
        if (lastError != null) return new HostSnapshot(HostState.Error, lastError, port);
        return new HostSnapshot(HostState.Stopped, "Sunshine is off in the child desktop. Start it to make this desktop available to your client.", port);
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
                try { launch(); }
                catch (Win32Exception ex) {
                    error = ex.NativeErrorCode == 1223 ? "Windows canceled Sunshine startup. You can try again and approve elevation inside the child desktop." : "Could not start Sunshine: " + ex.Message;
                }
                catch (Exception ex) { error = "Could not start Sunshine: " + ex.Message; }
                finally { lock (gate) { starting = false; lastError = error; } }
            });
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            return new HostSnapshot(HostState.Starting, "Starting Sunshine. Check the child desktop for the Windows elevation prompt.", current.BasePort);
        }
    }
}
