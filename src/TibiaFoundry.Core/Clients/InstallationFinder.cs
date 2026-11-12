using System.Text.RegularExpressions;
using Microsoft.Win32;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Clients;

/// <summary>A folder where a Tibia installation may be, and why it was looked at.</summary>
public sealed record InstallationRoot(string Path, string Source);

/// <summary>A folder Find Installation looked at, and what it is.</summary>
public sealed record FoundFolder(string Path, string Source, FolderInspection Inspection)
{
    /// <summary>A complete installation or a usable client folder.</summary>
    public bool IsValid => Inspection.Class is FolderClass.CompleteInstallation or FolderClass.StandaloneClient;
}

/// <summary>The result of Find Installation: the valid candidates, and every folder that was looked at with its outcome.</summary>
public sealed class FindResult
{
    public required IReadOnlyList<FoundFolder> Candidates { get; init; }
    public required IReadOnlyList<FoundFolder> Checked { get; init; }
    public required IReadOnlyList<string> RootsLookedAt { get; init; }

    /// <summary>"No installation was found…" with what was looked at, for the message when <see cref="Candidates"/> is empty.</summary>
    public string Explanation => Candidates.Count > 0
        ? L.F("InstallationFinder.0Found", L.Count(Candidates.Count, "Noun.client_folder"))
        : L.T("InstallationFinder.NoTibiaInstallationWasFoundLooked") + string.Join("; ", RootsLookedAt) +
          (Checked.Count > 0 ? L.T("InstallationFinder.FoldersThatWereCheckedButAre") + string.Join("; ", Checked.Where(c => !c.IsValid).Select(c => $"{c.Path} ({c.Inspection.ClassDisplay})")) : "") +
          L.T("InstallationFinder.UseBrowseForFolderToChoose");
}

/// <summary>
/// Find Installation (8.0): looks for Tibia client folders where Windows installations usually are
/// and validates each one by its files (never by its name). Only reads: nothing is modified, moved,
/// repaired or installed. Where it looks:
/// <list type="number">
/// <item>the launcher's packages folder, <c>%LOCALAPPDATA%\Tibia\packages</c>;</item>
/// <item>the folder of the Tibia uninstaller named in Windows' uninstall entries (current user and machine);</item>
/// <item>Steam libraries (<c>steamapps\common\Tibia</c>), from Steam's own library list;</item>
/// <item><c>Program Files\Tibia</c> and <c>Program Files (x86)\Tibia</c>;</item>
/// <item>folders you registered before.</item>
/// </list>
/// </summary>
public static class InstallationFinder
{
    /// <summary>Where to look on this PC (registry and environment are only read).</summary>
    /// <param name="packagesFolder">The launcher's packages folder as Tibia Foundry resolves it (chosen in Settings, else <c>%LOCALAPPDATA%\Tibia\packages</c>).</param>
    public static IReadOnlyList<InstallationRoot> DefaultRoots(string? packagesFolder = null, IEnumerable<string>? registered = null)
    {
        var roots = new List<InstallationRoot> { new(packagesFolder ?? TibiaClientPaths.PackagesRoot, L.T("InstallationFinder.TheTibiaLauncherSPackagesFolder")) };
        foreach (string folder in UninstallFolders()) roots.Add(new InstallationRoot(folder, L.T("InstallationFinder.WindowsUninstallEntryForTibia")));
        foreach (string library in SteamLibraries()) roots.Add(new InstallationRoot(Path.Combine(library, "steamapps", "common", "Tibia"), L.T("InstallationFinder.ASteamLibrary")));
        foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            if (Environment.GetFolderPath(special) is { Length: > 0 } pf) roots.Add(new InstallationRoot(Path.Combine(pf, "Tibia"), L.T("InstallationFinder.ProgramFiles")));
        foreach (string r in registered ?? []) roots.Add(new InstallationRoot(r, L.T("InstallationFinder.AFolderYouRegistered")));
        return roots;
    }

    /// <summary>
    /// Expands each root (the folder itself, its <c>packages</c> subfolders, and the client folders up
    /// to two levels below it) and inspects every folder found. Duplicates are looked at once.
    /// </summary>
    public static FindResult Find(IEnumerable<InstallationRoot> roots, IReadOnlyDictionary<string, string>? knownContent = null, CancellationToken ct = default)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkedFolders = new List<FoundFolder>();
        var looked = new List<string>();
        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            string path;
            try { path = PathGuard.Normalize(root.Path); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            looked.Add($"{path} ({root.Source}{(Directory.Exists(path) ? "" : ": not there")})");
            if (!Directory.Exists(path)) continue;
            var folders = new List<string>();
            if (IsClientFolder(path)) folders.Add(path);
            folders.AddRange(FolderInspector.FindClientFolders(path, depth: 2));
            foreach (string folder in folders)
            {
                if (!seen.Add(folder)) continue;
                var inspection = FolderInspector.Inspect(folder, knownContent);
                checkedFolders.Add(new FoundFolder(folder, root.Source, inspection));
            }
        }
        return new FindResult
        {
            Candidates = checkedFolders.Where(c => c.IsValid).OrderBy(c => c.Inspection.Class).ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            Checked = checkedFolders,
            RootsLookedAt = looked,
        };
    }

    private static bool IsClientFolder(string path) =>
        File.Exists(Path.Combine(path, Compatibility.TibiaFormat.PackageManifest)) || File.Exists(Path.Combine(path, Compatibility.TibiaFormat.AssetsFolder, Compatibility.TibiaFormat.AssetCatalogFile));

    /// <summary>Folders of uninstallers whose entry is named "Tibia" (the launcher's folder: <c>%LOCALAPPDATA%\Tibia</c> on this kind of installation).</summary>
    public static IReadOnlyList<string> UninstallFolders()
    {
        var result = new List<string>();
        foreach (var (hive, key) in new[]
                 {
                     (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                     (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                     (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                 })
        {
            try
            {
                using var uninstall = hive.OpenSubKey(key);
                if (uninstall == null) continue;
                foreach (string name in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(name);
                    if (entry?.GetValue("DisplayName") is not string display || !display.Trim().Equals("Tibia", StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.GetValue("InstallLocation") is string location && location.Trim().Length > 0) result.Add(location.Trim().Trim('"'));
                    if (entry.GetValue("UninstallString") is string uninstaller && ExecutableOf(uninstaller) is { } exe && Path.GetDirectoryName(exe) is { } dir) result.Add(dir);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Not readable for this user: nothing found there.
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The program of a command line ("C:\x\uninst.exe" /S → C:\x\uninst.exe), or null.</summary>
    public static string? ExecutableOf(string commandLine)
    {
        string s = commandLine.Trim();
        if (s.StartsWith('"')) { int end = s.IndexOf('"', 1); return end > 1 ? s[1..end] : null; }
        int exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? s[..(exe + 4)] : null;
    }

    /// <summary>Steam's library folders, from <c>steamapps\libraryfolders.vdf</c> under Steam's folder (read only).</summary>
    public static IReadOnlyList<string> SteamLibraries()
    {
        string? steam = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            steam = key?.GetValue("SteamPath") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
        if (string.IsNullOrWhiteSpace(steam)) return [];
        var result = new List<string> { steam.Replace('/', Path.DirectorySeparatorChar) };
        try
        {
            string vdf = Path.Combine(result[0], "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf)) result.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only Steam's own folder then.
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The "path" values of a libraryfolders.vdf (backslashes are escaped there).</summary>
    public static IReadOnlyList<string> ParseLibraryFolders(string vdf) =>
        Regex.Matches(vdf, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value.Replace(@"\\", @"\")).ToList();
}
