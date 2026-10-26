using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

sealed class NativeHostDiscovery : IHostDiscoverySource
{
    readonly Dictionary<string, HostFileMetadata> cache = new Dictionary<string, HostFileMetadata>(StringComparer.OrdinalIgnoreCase);
    static void AddDirectory(List<HostCandidate> list, string directory, HostKind kind, string source)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        directory = directory.Trim().Trim('"');
        list.Add(new HostCandidate(Path.Combine(directory, "sunshine.exe"), kind, source));
        if (kind != HostKind.Sunshine) list.Add(new HostCandidate(Path.Combine(directory, kind == HostKind.Apollo ? "apollo.exe" : "vibepollo.exe"), kind, source));
    }
    static HostKind? RegistryKind(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (Regex.IsMatch(name, @"^Vibepollo(?:\s|$)", RegexOptions.IgnoreCase)) return HostKind.VibePollo;
        if (Regex.IsMatch(name, @"^Apollo(?:\s|$)", RegexOptions.IgnoreCase)) return HostKind.Apollo;
        if (Regex.IsMatch(name, @"^Sunshine(?:\s|$)", RegexOptions.IgnoreCase)) return HostKind.Sunshine;
        return null;
    }
    static void RegistryCandidates(List<HostCandidate> list, RegistryHive hive, RegistryView view)
    {
        try {
            using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
            using (var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall")) {
                if (uninstall == null) return;
                foreach (string name in uninstall.GetSubKeyNames()) {
                    try {
                        using (var entry = uninstall.OpenSubKey(name)) {
                            if (entry == null) continue; HostKind? kind = RegistryKind(entry.GetValue("DisplayName") as string); if (!kind.HasValue) continue;
                            AddDirectory(list, entry.GetValue("InstallLocation") as string, kind.Value, "registry");
                            string icon = entry.GetValue("DisplayIcon") as string;
                            if (!string.IsNullOrWhiteSpace(icon)) {
                                Match match = Regex.Match(icon.Trim(), "^(?:\"([^\"]+\\.exe)\"|(.+\\.exe))(?:,\\s*-?\\d+)?$", RegexOptions.IgnoreCase);
                                if (match.Success) list.Add(new HostCandidate(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value, kind.Value, "registry"));
                            }
                        }
                    } catch (Exception ex) { list.Add(new HostCandidate(null, null, "registry") { Diagnostic = "An installed-host record was inaccessible: " + ex.Message }); }
                }
            }
        } catch (Exception ex) { list.Add(new HostCandidate(null, null, "registry") { Diagnostic = "An installation registry view could not be read: " + ex.Message }); }
    }
    public IEnumerable<HostCandidate> Candidates(string installRoot)
    {
        var list = new List<HostCandidate>();
        RegistryCandidates(list, RegistryHive.LocalMachine, RegistryView.Registry64); RegistryCandidates(list, RegistryHive.LocalMachine, RegistryView.Registry32);
        RegistryCandidates(list, RegistryHive.CurrentUser, RegistryView.Registry64); RegistryCandidates(list, RegistryHive.CurrentUser, RegistryView.Registry32);
        string saved = HostInstallation.PathFromId(HostSelectionStore.Load(Path.Combine(installRoot, "host-selection.txt")));
        if (saved != null) list.Add(new HostCandidate(saved, null, "saved"));
        AddDirectory(list, Path.Combine(installRoot, "Sunshine", "Sunshine"), HostKind.Sunshine, "managed");
        AddDirectory(list, Path.Combine(installRoot, "Sunshine"), HostKind.Sunshine, "managed");
        foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }) {
            if (string.IsNullOrEmpty(folder)) continue;
            foreach (HostKind kind in Enum.GetValues(typeof(HostKind))) AddDirectory(list, Path.Combine(folder, kind.ToString()), kind, "known");
        }
        foreach (string name in new[] { "sunshine", "apollo", "vibepollo" }) {
            try {
                foreach (var process in Process.GetProcessesByName(name)) using (process) {
                    try { list.Add(new HostCandidate(HostNative.ProcessPath((uint)process.Id), null, "running")); }
                    catch (InvalidOperationException) { }
                    // Access denied = a host running as administrator, typically the main host (e.g. Vibepollo). Not an Annex problem.
                    catch (System.ComponentModel.Win32Exception ex) { if (ex.NativeErrorCode != 5) list.Add(new HostCandidate(null, null, "running") { Diagnostic = "A running host path could not be inspected: " + ex.Message }); }
                    catch (Exception ex) { list.Add(new HostCandidate(null, null, "running") { Diagnostic = "A running host path could not be inspected: " + ex.Message }); }
                }
            } catch (Exception ex) { list.Add(new HostCandidate(null, null, "running") { Diagnostic = "Running host discovery failed: " + ex.Message }); }
        }
        return list;
    }
    public bool Exists(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.Directory) == 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
    public HostFileMetadata Inspect(string path)
    {
        var file = new FileInfo(path); string stamp = file.Length.ToString(CultureInfo.InvariantCulture) + ":" + file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        HostFileMetadata cached;
        lock (cache) { if (cache.TryGetValue(path, out cached) && cached.Stamp == stamp) return cached; }
        var version = FileVersionInfo.GetVersionInfo(path);
        file.Refresh();
        string after = file.Length.ToString(CultureInfo.InvariantCulture) + ":" + file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        if (stamp != after) throw new IOException("The host changed during discovery; refresh its status.");
        var metadata = new HostFileMetadata(version.ProductName, version.FileDescription, version.FileVersion, stamp);
        lock (cache) { cache[path] = metadata; }
        return metadata;
    }
}
