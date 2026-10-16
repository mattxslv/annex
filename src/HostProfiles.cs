using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

sealed class NetworkPort
{
    public readonly bool Tcp; public readonly int Port;
    public NetworkPort(bool tcp, int port) { Tcp = tcp; Port = port; }
}

static class HostPorts
{
    public static NetworkPort[] Family(int port) {
        if (port < 1029 || port > 65514) throw new ArgumentOutOfRangeException("port", "The complete host port family must fit from 1029 to 65514.");
        return new[] { new NetworkPort(true, port - 5), new NetworkPort(true, port), new NetworkPort(true, port + 1), new NetworkPort(true, port + 21),
            new NetworkPort(false, port + 9), new NetworkPort(false, port + 10), new NetworkPort(false, port + 11) };
    }
    public static int FindAvailable(NetworkPort[] occupied, int first, int last) {
        Family(first); Family(last); if (last < first) throw new ArgumentOutOfRangeException("last");
        for (int candidate = first; candidate <= last; candidate++) if (!Collides(candidate, occupied)) return candidate;
        throw new IOException("No free child-host port family is available. Close the conflicting listener or choose another child configuration.");
    }
    public static bool Collides(int port, NetworkPort[] occupied) { return Family(port).Any(p => (occupied ?? new NetworkPort[0]).Any(o => p.Tcp == o.Tcp && p.Port == o.Port)); }
    // Both IPv4 and IPv6 endpoints are deliberately reduced to protocol/port: binding either can block a wildcard host listener.
    public static NetworkPort[] Occupied() {
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        return properties.GetActiveTcpListeners().Select(e => new NetworkPort(true, e.Port))
            .Concat(properties.GetActiveUdpListeners().Select(e => new NetworkPort(false, e.Port))).ToArray();
    }
}

sealed class HostLaunchSpec
{
    public readonly string Executable, WorkingDirectory; public readonly string[] Arguments;
    public HostLaunchSpec(string executable, string[] arguments, string directory) { Executable = executable; Arguments = (string[])arguments.Clone(); WorkingDirectory = directory; }
}

static class NativeArguments
{
    // CommandLineToArgvW/CRT quoting: backslashes preceding a quote or final delimiter must be doubled.
    public static string Quote(string argument) {
        if (argument == null || argument.IndexOf('\0') >= 0) throw new ArgumentException("Invalid native argument.");
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (char character in argument) {
            if (character == '\\') { slashes++; continue; }
            if (character == '"') result.Append('\\', slashes * 2 + 1); else result.Append('\\', slashes);
            result.Append(character); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
    public static string Join(string[] arguments) { return string.Join(" ", arguments.Select(Quote)); }
}

interface IHostAdapter
{
    HostKind Kind { get; }
    string Validate(HostInstallation installation);
    string BuildConfiguration(HostInstallation installation, string directory, int basePort);
    HostLaunchSpec BuildLaunch(ChildHostProfile profile);
}

sealed class SunshineFamilyAdapter : IHostAdapter
{
    public HostKind Kind { get; private set; }
    public SunshineFamilyAdapter(HostKind kind) { Kind = kind; }
    public string Validate(HostInstallation installation) {
        if (installation.Kind != Kind) return "The selected host does not match its launch adapter.";
        if (string.IsNullOrWhiteSpace(installation.Version) || !char.IsDigit(installation.Version[0])) return "This host has no recognized version metadata. Choose a released Sunshine or Apollo build.";
        if (Kind == HostKind.VibePollo) return "VibePollo was detected, but independent child startup is not supported yet: its shared helper/display state and simultaneous instances need verification. Install Sunshine or Apollo for the child desktop; your main host can stay installed.";
        return null;
    }
    public string BuildConfiguration(HostInstallation installation, string directory, int basePort) {
        HostPorts.Family(basePort);
        if (directory.IndexOfAny(new[] { '#', '\r', '\n', '"' }) >= 0) throw new NotSupportedException("The child-host folder contains characters that the host configuration cannot represent. Move ChildStream to a folder without # or quotes.");
        var config = new StringBuilder();
        config.AppendLine("# ChildStream child desktop. Independent from the main host.");
        config.AppendLine("sunshine_name = " + Environment.MachineName + " Child Desktop");
        config.AppendLine("port = " + basePort.ToString(CultureInfo.InvariantCulture));
        config.AppendLine("capture = wgc"); config.AppendLine("dd_configuration_option = disabled");
        if (Kind == HostKind.Apollo) config.AppendLine("isolated_virtual_display_option = disabled");
        config.AppendLine("install_steam_audio_drivers = disabled"); config.AppendLine("upnp = disabled");
        string[] keys = { "file_state", "credentials_file", "pkey", "cert", "file_apps", "log_path" };
        string[] files = { "sunshine_state.json", "credentials.json", "private-key.pem", "certificate.pem", "apps.json", "sunshine.log" };
        for (int i = 0; i < keys.Length; i++) config.AppendLine(keys[i] + " = " + Path.Combine(directory, files[i]));
        return config.ToString();
    }
    public HostLaunchSpec BuildLaunch(ChildHostProfile profile) { return new HostLaunchSpec(profile.Installation.ExecutablePath, profile.Arguments, Path.GetDirectoryName(profile.Installation.ExecutablePath)); }
}

sealed class ChildHostProfile
{
    public readonly HostInstallation Installation; public readonly string ConfigPath, DataDirectory, Identity; public readonly int BasePort;
    public readonly string[] Arguments;
    public string ReceiptPath { get { return Path.Combine(DataDirectory, "childstream-launch.txt"); } }
    public ChildHostProfile(HostInstallation installation, string config, string directory, int port) {
        Installation = installation; ConfigPath = config; DataDirectory = directory; BasePort = port; Arguments = new[] { config }; Identity = CurrentIdentity();
    }
    public string CurrentIdentity() { return HostProfiles.Digest(Installation.Id + "|" + ConfigPath.ToUpperInvariant() + "|" + HostProfiles.ReadConfiguration(ConfigPath)); }
}

static class HostProfiles
{
    internal static string Digest(string text) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
    internal static string ReadConfiguration(string path) { if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("The child host configuration exceeds 1 MiB."); return File.ReadAllText(path); }
    public static IHostAdapter Adapter(HostInstallation host) { return new SunshineFamilyAdapter(host.Kind); }
    public static ChildHostProfile Prepare(string installRoot, HostInstallation host, NetworkPort[] occupied) {
        if (host == null) throw new ArgumentNullException("host");
        string error = Adapter(host).Validate(host); if (error != null) throw new NotSupportedException(error);
        if (!File.Exists(host.ExecutablePath)) throw new FileNotFoundException("The selected host executable is no longer installed.");
        var legacy = HostConfiguration.Load(installRoot);
        bool managed = host.Kind == HostKind.Sunshine && HostController.SamePath(host.ExecutablePath, legacy.ExecutablePath);
        string directory = managed ? Path.GetDirectoryName(legacy.ConfigPath) : Path.Combine(Path.GetFullPath(installRoot), "Hosts", Digest(host.Kind + "|" + host.ExecutablePath.ToUpperInvariant()));
        string config = managed ? legacy.ConfigPath : Path.Combine(directory, "sunshine.conf");
        Directory.CreateDirectory(directory);
        int port;
        if (File.Exists(config)) {
            port = HostConfiguration.ParsePort(ReadConfiguration(config)); HostPorts.Family(port);
            // Check write access without changing a byte of existing user settings or pairing state.
            using (var writable = new FileStream(config, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { }
        } else {
            port = HostPorts.FindAvailable(occupied, 48989, 65514);
            string content = Adapter(host).BuildConfiguration(host, directory, port);
            using (var output = new FileStream(config, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
                byte[] bytes = new UTF8Encoding(false).GetBytes(content); output.Write(bytes, 0, bytes.Length);
            }
            string apps = Path.Combine(directory, "apps.json");
            if (!File.Exists(apps)) File.WriteAllText(apps, "{\"env\":{},\"apps\":[{\"name\":\"Desktop\",\"image-path\":\"desktop.png\"}]}", new UTF8Encoding(false));
        }
        string probe = Path.Combine(directory, ".write-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        return new ChildHostProfile(host, config, directory, port);
    }
}

sealed class HostLaunchReceipt
{
    public readonly int ProcessId; public readonly long StartTimeUtcTicks; public readonly string ProfileIdentity;
    public HostLaunchReceipt(int pid, long ticks, string identity) { ProcessId = pid; StartTimeUtcTicks = ticks; ProfileIdentity = identity; }
    public bool Matches(HostProcess process, string identity) { return ProcessId > 0 && StartTimeUtcTicks > 0 && ProcessId == process.ProcessId && StartTimeUtcTicks == process.StartTimeUtcTicks && ProfileIdentity == identity; }
    public void Save(string path) { File.WriteAllText(path, ProcessId.ToString(CultureInfo.InvariantCulture) + "|" + StartTimeUtcTicks.ToString(CultureInfo.InvariantCulture) + "|" + ProfileIdentity, new UTF8Encoding(false)); }
    public static HostLaunchReceipt Load(string path) {
        try { if (new FileInfo(path).Length > 256) return null; string[] parts = File.ReadAllText(path).Split('|'); int pid; long ticks;
            if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out pid) || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ticks) || pid <= 0 || ticks <= 0 || parts[2].Length != 64) return null;
            return new HostLaunchReceipt(pid, ticks, parts[2]);
        } catch (Exception) { return null; }
    }
}
