using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

enum ChildAppKind { Chrome, Edge, Steam, WallpaperEngine, Other }
enum AppLaunchState { Dispatched, EarlyExit, OtherSession, InspectionDenied, Failed }
sealed class AppLaunchResult
{
    public readonly AppLaunchState State; public readonly string Message;
    public AppLaunchResult(AppLaunchState state, string message) { State = state; Message = HostInstallation.Limit(message, 512); }
}
sealed class ChildApp
{
    public readonly string Id, DisplayName, ExecutablePath; public readonly ChildAppKind Kind; public readonly string[] Arguments;
    public ChildApp(string path, ChildAppKind kind) : this(path, kind, new string[0]) { }
    public ChildApp(string path, ChildAppKind kind, string[] arguments) {
        if (arguments == null || arguments.Length != 0) throw new ArgumentException("Registered apps cannot supply command text or flags.");
        ExecutablePath = ChildAppCatalog.ValidatePath(path); Kind = kind; Arguments = new string[0];
        DisplayName = kind == ChildAppKind.Other ? HostInstallation.Limit(Path.GetFileNameWithoutExtension(path), 80) : kind == ChildAppKind.WallpaperEngine ? "Wallpaper Engine" : kind.ToString();
        var info = new FileInfo(ExecutablePath); Id = HostProfiles.Digest(ExecutablePath.ToUpperInvariant() + "|" + kind + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks);
    }
}
interface IAppDiscoverySource { IEnumerable<ChildApp> Candidates(); }
static class ChildAppCatalog
{
    public static string ValidatePath(string path) {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose an installed .exe file. Shortcuts and scripts are not supported.");
        path = Path.GetFullPath(path);
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.Substring(2).Contains(":")) throw new ArgumentException("Choose an executable on a local drive.");
        string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (new[] { "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "wsl", "bash", "rundll32", "regsvr32" }.Contains(name)) throw new ArgumentException("Choose the app itself, rather than a shell or script runner.");
        if (!File.Exists(path)) throw new FileNotFoundException("The selected app is no longer installed."); return path;
    }
    public static ChildApp Register(string path) { path = ValidatePath(path); return new ChildApp(path, NativeAppDiscovery.Classify(path)); }
    public static ChildApp[] Find(IAppDiscoverySource source) {
        var result = new List<ChildApp>(); foreach (ChildApp app in source.Candidates()) if (app != null && !result.Any(a => HostController.SamePath(a.ExecutablePath, app.ExecutablePath)) && result.Count < 16) result.Add(app); return result.ToArray();
    }
    public static string[] LoadRegistered(string path) {
        try { if (!File.Exists(path) || new FileInfo(path).Length > 65536) return new string[0]; return File.ReadAllLines(path).Take(16).Select(line => Encoding.UTF8.GetString(Convert.FromBase64String(line))).Where(p => p.Length <= 2048).ToArray(); }
        catch (Exception) { return new string[0]; }
    }
    public static void SaveRegistered(string path, IEnumerable<string> paths) { File.WriteAllLines(path, paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(16).Select(p => Convert.ToBase64String(Encoding.UTF8.GetBytes(p))), new UTF8Encoding(false)); }
}
sealed class NativeAppDiscovery : IAppDiscoverySource
{
    public static ChildAppKind Classify(string path) {
        var info = FileVersionInfo.GetVersionInfo(path); string branding = (info.ProductName ?? "") + " " + (info.FileDescription ?? "");
        if (branding.IndexOf("Google Chrome", StringComparison.OrdinalIgnoreCase) >= 0) return ChildAppKind.Chrome;
        if (branding.IndexOf("Microsoft Edge", StringComparison.OrdinalIgnoreCase) >= 0) return ChildAppKind.Edge;
        if (branding.IndexOf("Wallpaper Engine", StringComparison.OrdinalIgnoreCase) >= 0) return ChildAppKind.WallpaperEngine;
        if (string.Equals(Path.GetFileName(path), "steam.exe", StringComparison.OrdinalIgnoreCase) && branding.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0) return ChildAppKind.Steam;
        return ChildAppKind.Other;
    }
    public IEnumerable<ChildApp> Candidates() {
        var paths = new List<string>();
        foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }) {
            paths.Add(Path.Combine(folder, "Google", "Chrome", "Application", "chrome.exe")); paths.Add(Path.Combine(folder, "Microsoft", "Edge", "Application", "msedge.exe"));
        }
        string steam = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\Steam";
        try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) if (key != null && key.GetValue("SteamPath") is string) steam = ((string)key.GetValue("SteamPath")).Replace('/', '\\'); } catch (System.Security.SecurityException) { } catch (UnauthorizedAccessException) { }
        paths.Add(Path.Combine(steam, "steam.exe")); var libraries = new List<string> { steam };
        try { string file = Path.Combine(steam, "steamapps", "libraryfolders.vdf"); if (File.Exists(file) && new FileInfo(file).Length < 1024 * 1024) foreach (Match match in Regex.Matches(File.ReadAllText(file), "\"path\"\\s*\"([^\"]+)\"")) libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\")); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string library in libraries) { paths.Add(Path.Combine(library, "steamapps", "common", "wallpaper_engine", "wallpaper64.exe")); paths.Add(Path.Combine(library, "steamapps", "common", "wallpaper_engine", "wallpaper32.exe")); }
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase)) { ChildApp app = null; try { if (File.Exists(path)) app = ChildAppCatalog.Register(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } if (app != null) yield return app; }
    }
}
sealed class AppLaunchSpec
{
    public readonly string Executable, WorkingDirectory; public readonly string[] Arguments; public readonly ChildAppKind Kind;
    public AppLaunchSpec(string executable, string[] arguments, string directory, ChildAppKind kind) { Executable = executable; Arguments = (string[])arguments.Clone(); WorkingDirectory = directory; Kind = kind; }
}
static class ChildAppLauncher
{
    public static AppLaunchSpec Build(ChildApp app, string localAppData) {
        ChildAppCatalog.ValidatePath(app.ExecutablePath);
        if (new ChildApp(app.ExecutablePath, app.Kind).Id != app.Id) throw new IOException("The selected app changed. Refresh and choose it again.");
        string[] arguments = new string[0];
        if (app.Kind == ChildAppKind.Chrome || app.Kind == ChildAppKind.Edge) {
            string profile = Path.Combine(Path.GetFullPath(localAppData), "Annex", app.Kind + "-Child"); Directory.CreateDirectory(profile); arguments = new[] { "--user-data-dir=" + profile };
        }
        return new AppLaunchSpec(app.ExecutablePath, arguments, Path.GetDirectoryName(app.ExecutablePath), app.Kind);
    }
    public static AppLaunchResult Ownership(AppLaunchSpec spec, int session, HostProcess[] processes) {
        if (spec.Kind != ChildAppKind.Steam) return null;
        foreach (var process in processes) {
            if (process.InspectionDenied) return new AppLaunchResult(AppLaunchState.InspectionDenied, "Windows could not inspect an existing Steam process. Close Steam yourself if needed, then retry. No app was stopped.");
            if (process.SessionId != session && HostController.SamePath(process.ExecutablePath, spec.Executable)) return new AppLaunchResult(AppLaunchState.OtherSession, "Steam is already running on another desktop. Exit Steam there yourself, then open it here. Games, account sign-in and anti-cheat support still depend on Steam and the game.");
        }
        return null;
    }
    public static AppLaunchResult Launch(AppLaunchSpec spec, uint approvedChild, Func<uint> currentChild, Func<AppLaunchSpec, AppLaunchResult> start) {
        try { uint current = currentChild(); if (approvedChild == 0 || approvedChild > int.MaxValue || current != approvedChild) return new AppLaunchResult(AppLaunchState.Failed, "The child desktop changed. Reconnect and choose the app again."); return start(spec); }
        catch (Exception ex) { return new AppLaunchResult(AppLaunchState.Failed, "Could not open the child app: " + ex.Message); }
    }
    public static AppLaunchResult NativeStart(AppLaunchSpec spec) {
        var ownership = Ownership(spec, HostNative.CurrentSession(), HostNative.Processes(spec.Executable)); if (ownership != null) return ownership;
        ChildAppCatalog.ValidatePath(spec.Executable);
        using (var process = Process.Start(new ProcessStartInfo(spec.Executable, NativeArguments.Join(spec.Arguments)) { UseShellExecute = true, WorkingDirectory = spec.WorkingDirectory })) {
            if (process == null) return new AppLaunchResult(AppLaunchState.Failed, "Windows returned no app process. Check the child desktop for an existing window.");
            if (process.WaitForExit(600)) return new AppLaunchResult(AppLaunchState.EarlyExit, "The app launcher exited (code " + process.ExitCode + "). Check for an existing window in the child desktop. If none appears, the app may require its own launcher or may not support this session.");
            if (process.SessionId != HostNative.CurrentSession()) return new AppLaunchResult(AppLaunchState.Failed, "The app did not remain in the child desktop.");
            return new AppLaunchResult(AppLaunchState.Dispatched, spec.Kind == ChildAppKind.WallpaperEngine ? "Wallpaper Engine was launched in the child desktop. Check its UI and RDP monitor mapping; its settings may also affect the main desktop." : "The app was launched in the child desktop. Check its window there; a running process does not verify rendering, sign-in or game compatibility.");
        }
    }
}
