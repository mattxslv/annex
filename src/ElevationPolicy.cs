using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

// A host is started elevated only when the current user cannot change what gets elevated:
// its executable, its folder, its config file and the config folder (prep/app commands).
// Otherwise any program running as the user could swap them and ride the elevation prompt.
static class ElevationPolicy
{
    public static string LastLaunchNote;
    public const string UnelevatedNote = " Started without administrator rights because its files can be changed without admin approval; input into games running as administrator will not work. Run scripts\\setup.ps1 to protect the managed Sunshine folder.";

    public static bool CanElevate(string executable, string configPath)
    {
        try {
            return IsProtectedFile(executable) && IsProtectedFolder(Path.GetDirectoryName(executable))
                && IsProtectedFile(configPath) && IsProtectedFolder(Path.GetDirectoryName(configPath));
        } catch (Exception ex) { Program.Log("elevation check failed: " + ex.Message); return false; }
    }

    static bool OwnedByCurrentUser(FileSystemSecurity security)
    {
        using (var identity = WindowsIdentity.GetCurrent())
            return identity.User.Equals(security.GetOwner(typeof(SecurityIdentifier)));
    }

    internal static bool IsProtectedFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || OwnedByCurrentUser(File.GetAccessControl(path, AccessControlSections.Owner))) return false;
        try { using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { } return false; }
        catch (UnauthorizedAccessException) { return true; }
        catch (IOException) { return false; }
    }

    internal static bool IsProtectedFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path) || OwnedByCurrentUser(Directory.GetAccessControl(path, AccessControlSections.Owner))) return false;
        string probe = Path.Combine(path, ".annex-write-" + Guid.NewGuid().ToString("N"));
        try { using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { } return false; }
        catch (UnauthorizedAccessException) { return true; }
        catch (IOException) { return false; }
    }
}
