using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.AccessControl;
using System.Security.Principal;

// A host is started elevated only when the current user cannot change what gets elevated:
// its executable, its folder, its config file and the config folder (prep/app commands).
// Otherwise any program running as the user could swap them and ride the elevation prompt.
static class ElevationPolicy
{
    public static string LastLaunchNote;
    public const string UnelevatedNote = " Started without administrator rights because its files can be changed without admin approval; input into games running as administrator will not work. Run scripts\\setup.ps1 to protect the managed Sunshine folder.";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    // Holds the executable, the config and their folders open without delete sharing from the check until the
    // elevated start: nothing can rename or replace them (or any parent folder) in between. Writes are still
    // governed by the permissions CanElevate verifies. Null if they cannot all be held.
    public static IDisposable Hold(string executable, string configPath)
    {
        var handles = new List<SafeFileHandle>();
        try {
            foreach (string folder in new[] { Path.GetDirectoryName(executable), Path.GetDirectoryName(configPath) })
                handles.Add(Open(folder, 0x1 /* FILE_LIST_DIRECTORY */, 0x02000000 /* FILE_FLAG_BACKUP_SEMANTICS */));
            foreach (string file in new[] { executable, configPath })
                handles.Add(Open(file, 0x80000000 /* GENERIC_READ */, 0));
            return new HeldPaths(handles);
        } catch (Exception ex) {
            foreach (var handle in handles) handle.Dispose();
            Program.Log("could not hold host files for an elevated start: " + ex.Message); return null;
        }
    }
    static SafeFileHandle Open(string path, uint access, uint flags)
    {
        var handle = CreateFile(Path.GetFullPath(path), access, 0x1 | 0x2 /* read | write, never delete */, IntPtr.Zero, 3 /* OPEN_EXISTING */, flags, IntPtr.Zero);
        if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new System.ComponentModel.Win32Exception(error); }
        return handle;
    }
    sealed class HeldPaths : IDisposable
    {
        readonly List<SafeFileHandle> handles;
        public HeldPaths(List<SafeFileHandle> handles) { this.handles = handles; }
        public void Dispose() { foreach (var handle in handles) handle.Dispose(); }
    }

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
