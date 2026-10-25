using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

enum HostKind { Sunshine, Apollo, VibePollo }

sealed class HostCandidate
{
    public string ExecutablePath, Source, Diagnostic;
    public HostKind? Hint;
    public HostCandidate(string path, HostKind? hint, string source) { ExecutablePath = path; Hint = hint; Source = source; }
}

sealed class HostFileMetadata
{
    public string ProductName, Description, Version, Stamp;
    public HostFileMetadata(string product, string description, string version, string stamp) { ProductName = product ?? ""; Description = description ?? ""; Version = version ?? ""; Stamp = stamp ?? ""; }
}

interface IHostDiscoverySource
{
    IEnumerable<HostCandidate> Candidates(string installRoot);
    bool Exists(string path);
    HostFileMetadata Inspect(string path);
}

sealed class HostInstallation
{
    public string Id { get; private set; }
    public HostKind Kind { get; private set; }
    public string DisplayName { get { return Kind.ToString(); } }
    public string ExecutablePath { get; private set; }
    public string Version { get; private set; }
    public string Source { get; private set; }
    public string Diagnostic { get; private set; }
    public bool IsManaged { get; internal set; }
    public HostInstallation(string path, HostKind kind, HostFileMetadata metadata, string source)
    {
        ExecutablePath = Path.GetFullPath(path); Kind = kind; Version = Limit(metadata.Version, 80); Source = Limit(source, 40); Diagnostic = "";
        IsManaged = source == "managed";
        Id = kind + "|" + Encode(ExecutablePath.ToUpperInvariant()) + "|" + Encode(Version) + "|" + Encode(Limit(metadata.Stamp, 120));
    }
    static string Encode(string text) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)); }
    internal static string Limit(string text, int limit) { text = text ?? ""; return text.Length > limit ? text.Substring(0, limit) : text; }
    public static string PathFromId(string id)
    {
        try {
            if (string.IsNullOrEmpty(id) || id.Length > 4096) return null;
            string[] parts = id.Split('|'); HostKind kind;
            if (parts.Length != 4 || !Enum.TryParse(parts[0], out kind) || kind.ToString() != parts[0]) return null;
            var utf8 = new UTF8Encoding(false, true);
            string path = utf8.GetString(Convert.FromBase64String(parts[1]));
            utf8.GetString(Convert.FromBase64String(parts[2])); utf8.GetString(Convert.FromBase64String(parts[3]));
            if (!Path.IsPathRooted(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) return null;
            return Path.GetFullPath(path);
        } catch (Exception) { return null; }
    }
    public override string ToString() { return DisplayName + " — " + ExecutablePath; }
}

sealed class HostDiscoveryResult
{
    public HostInstallation[] Installations;
    public string[] Diagnostics;
}

static class HostDiscovery
{
    public static HostInstallation FromExecutable(string path, IHostDiscoverySource source) {
        if (!Path.IsPathRooted(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) || !source.Exists(path)) throw new InvalidDataException("Choose an installed streaming host EXE.");
        var candidate = new HostCandidate(path, null, "browse"); var metadata = source.Inspect(path); HostKind? kind = Identify(candidate, metadata);
        if (!kind.HasValue) throw new InvalidDataException("Could not identify Sunshine, Apollo or VibePollo in this executable's metadata.");
        return new HostInstallation(path, kind.Value, metadata, "browse");
    }
    static HostKind? Identify(HostCandidate candidate, HostFileMetadata metadata)
    {
        string branding = metadata.ProductName + " " + metadata.Description;
        if (branding.IndexOf("vibepollo", StringComparison.OrdinalIgnoreCase) >= 0) return HostKind.VibePollo;
        if (branding.IndexOf("apollo", StringComparison.OrdinalIgnoreCase) >= 0) return HostKind.Apollo;
        if (branding.IndexOf("sunshine", StringComparison.OrdinalIgnoreCase) >= 0) return candidate.Hint ?? HostKind.Sunshine;
        return null;
    }
    public static HostDiscoveryResult Find(string installRoot, IHostDiscoverySource source)
    {
        var installations = new List<HostInstallation>(); var diagnostics = new List<string>();
        Action<string> diagnose = message => { if (diagnostics.Count < 8) diagnostics.Add(HostInstallation.Limit(message, 240)); };
        try {
            foreach (HostCandidate candidate in source.Candidates(installRoot)) {
                if (candidate == null) continue;
                if (!string.IsNullOrEmpty(candidate.Diagnostic)) { diagnose(candidate.Diagnostic); continue; }
                try {
                    string path = candidate.ExecutablePath;
                    if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) { diagnose("A host candidate has an invalid executable path."); continue; }
                    path = Path.GetFullPath(path);
                    if (!source.Exists(path)) continue;
                    HostFileMetadata metadata = source.Inspect(path); HostKind? kind = Identify(candidate, metadata);
                    if (!kind.HasValue) { diagnose("Could not verify the streaming-host identity at " + path + ". Choose a supported host executable."); continue; }
                    var installation = new HostInstallation(path, kind.Value, metadata, candidate.Source);
                    int existing = installations.FindIndex(h => HostController.SamePath(h.ExecutablePath, path));
                    if (existing >= 0) {
                        bool managed = installations[existing].IsManaged || installation.IsManaged;
                        if (candidate.Source == "registry" && installations[existing].Source != "registry") installations[existing] = installation;
                        installations[existing].IsManaged = managed;
                    } else if (installations.Count < 16) installations.Add(installation);
                    else diagnose("More than 16 host installations were found. Choose an executable explicitly.");
                } catch (Exception ex) { diagnose("A host could not be inspected: " + ex.Message); }
            }
        } catch (Exception ex) { diagnose("Host discovery did not finish: " + ex.Message); }
        return new HostDiscoveryResult { Installations = installations.ToArray(), Diagnostics = diagnostics.ToArray() };
    }
}

sealed class HostSelectionChoice
{
    public HostInstallation Selected;
    public bool RequiresChoice;
}

static class HostSelection
{
    public static HostSelectionChoice Choose(HostInstallation[] candidates, string rememberedId, string runningChildId)
    {
        candidates = candidates ?? new HostInstallation[0];
        HostInstallation selected = candidates.FirstOrDefault(h => h.Id == rememberedId) ?? candidates.FirstOrDefault(h => h.Id == runningChildId) ?? candidates.FirstOrDefault(h => h.IsManaged);
        if (selected == null && candidates.Length == 1) selected = candidates[0];
        return new HostSelectionChoice { Selected = selected, RequiresChoice = selected == null && candidates.Length > 1 };
    }
}

static class HostSelectionStore
{
    public static string Load(string path)
    {
        try { if (!File.Exists(path) || new FileInfo(path).Length > 8192) return null; string id = File.ReadAllText(path, Encoding.UTF8).Trim(); return HostInstallation.PathFromId(id) == null ? null : id; }
        catch (Exception) { return null; }
    }
    public static void Save(string path, string installationId)
    {
        if (HostInstallation.PathFromId(installationId) == null) throw new InvalidDataException("Invalid host selection.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, installationId, new UTF8Encoding(false));
    }
}
