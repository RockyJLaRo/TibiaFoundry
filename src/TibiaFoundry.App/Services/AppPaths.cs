using System.Reflection;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

/// <summary>
/// Where settings and logs are stored.
/// <list type="bullet">
/// <item>Installed use: <c>%APPDATA%\TibiaFoundry\settings.json</c>; logs in <c>Documents\Tibia_Foundry\Logs</c>
/// (or <c>%LOCALAPPDATA%\TibiaFoundry\logs</c> when Windows reports no Documents folder).</item>
/// <item>Portable use (portable build, or a <c>TibiaFoundry.portable</c> file next to the program):
/// settings and logs go to a <c>TibiaFoundry-Data</c> folder next to the program, so the whole
/// application can live on a USB stick.</item>
/// </list>
/// If the program folder is read-only in portable mode, the per-user folders are used instead and
/// <see cref="Notice"/> explains why.
/// <para>
/// Migration from versions 3.0 and 2.x: their portable markers still select portable mode, and
/// <see cref="PreviousSettingsFiles"/> names their settings files (newest first) so they are carried
/// over on the first start. The old files are only read.
/// </para>
/// </summary>
public sealed record AppPaths(string SettingsFile, string LogDirectory, bool IsPortable, string? Notice, IReadOnlyList<string> PreviousSettingsFiles)
{
    public const string PortableMarkerFile = AppIdentity.Id + ".portable";
    public const string PortableDataFolder = AppIdentity.Id + "-Data";

    /// <summary>True for the portable single-file build (<c>-p:PortableBuild=true</c>).</summary>
    public static bool IsPortableBuild { get; } = typeof(AppPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(a => a.Key == "PortableBuild" && string.Equals(a.Value, "true", StringComparison.OrdinalIgnoreCase));

    public string DataDirectory => Path.GetDirectoryName(SettingsFile)!;

    public static AppPaths Resolve(string programDirectory, bool portableBuild)
    {
        string? notice = null;
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] portablePrevious = AppIdentity.Legacy.PortableDataFolders.Select(f => Path.Combine(programDirectory, f, "settings.json")).ToArray();
        string[] installedPrevious = AppIdentity.Legacy.AppFolders.Select(f => Path.Combine(roaming, f, "settings.json")).ToArray();
        bool portable = portableBuild
                        || File.Exists(Path.Combine(programDirectory, PortableMarkerFile))
                        || AppIdentity.Legacy.PortableMarkerFiles.Any(m => File.Exists(Path.Combine(programDirectory, m)));
        if (portable)
        {
            string data = Path.Combine(programDirectory, PortableDataFolder);
            if (CanWrite(data))
                return new AppPaths(Path.Combine(data, "settings.json"), Path.Combine(data, "logs"), true, null, portablePrevious);
            notice = L.F("AppPaths.PortableModeIsNotPossibleBecause", programDirectory);
        }
        // The start-up log goes where the default log folder is; a folder chosen in Settings applies once settings are read.
        string root = OutputLocations.DefaultRoot;
        string logs = root.Length > 0 ? OutputLocations.DefaultFor(OutputKind.Logs, root) : Path.Combine(local, AppIdentity.Id, "logs");
        return new AppPaths(Path.Combine(roaming, AppIdentity.Id, "settings.json"), logs, false, notice, installedPrevious);
    }

    /// <summary>Whether the folder exists or can be created, and files can be written in it (the shared probe of <see cref="OutputLocations.CanWrite"/>).</summary>
    private static bool CanWrite(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
        return OutputLocations.CanWrite(directory, out _);
    }
}
