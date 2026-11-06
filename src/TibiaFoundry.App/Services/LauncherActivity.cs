using System.ComponentModel;
using System.Diagnostics;
using TibiaFoundry.Core.FileOps;

namespace TibiaFoundry.App.Services;

/// <summary>
/// Is the Tibia launcher of an installation running (8.0)? While it runs it may be updating the
/// client, so the launch check waits for the next start. The client itself (client.exe) does not
/// count: it writes only into the personal folders, which the check leaves out. Only reads the
/// process list; nothing is changed.
/// </summary>
public static class LauncherActivity
{
    private static readonly string[] LauncherNames = ["Tibia", "client_launcher"];

    /// <summary>The installation a package folder belongs to: the folder above <c>packages</c>, or the folder itself.</summary>
    public static string InstallationRoot(string packageFolder)
    {
        string full = PathGuard.Normalize(packageFolder);
        var parent = Directory.GetParent(full);
        return parent != null && string.Equals(parent.Name, "packages", StringComparison.OrdinalIgnoreCase) && parent.Parent != null ? parent.Parent.FullName : full;
    }

    public static bool IsActive(string packageFolder)
    {
        string root = InstallationRoot(packageFolder);
        foreach (string name in LauncherNames)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch (InvalidOperationException) { continue; }
            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName is { } file && PathGuard.IsSameOrInside(root, file)) return true;
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        // Another user's or an elevated process: not this user's launcher.
                    }
                }
            }
        }
        return false;
    }
}
