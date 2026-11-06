using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Clients;

/// <summary>What was found at a default client location (8.6).</summary>
public enum DefaultClientState
{
    /// <summary>A complete installation: every tool can use it.</summary>
    Available,
    /// <summary>Usable, but not a complete installation (a copied client or an assets folder).</summary>
    Partial,
    /// <summary>Some client files are there, but not enough for any tool.</summary>
    Incomplete,
    /// <summary>The folder exists but holds no Tibia client.</summary>
    NotAClient,
    /// <summary>The folder does not exist.</summary>
    Missing,
    /// <summary>The folder exists but cannot be read (permissions, a disconnected drive).</summary>
    Inaccessible,
}

/// <summary>The default live or test client: where it is expected, and what is there.</summary>
public sealed record DefaultClientStatus(ClientScope Scope, string Label, string Folder, DefaultClientState State, FolderInspection? Inspection, string Explanation)
{
    /// <summary>Can it be chosen as the client? Only folders a tool can read; a missing or insufficient one never is.</summary>
    public bool IsUsable => State is DefaultClientState.Available or DefaultClientState.Partial;

    /// <summary>"Live server" or "Test server".</summary>
    public string Server => ClientServers.Display(ClientServers.Of(Scope));

    /// <summary>The detected version ("15.33.c9e45e"), or null when it is unknown.</summary>
    public string? Version => Inspection?.Version.Version;

    public string StateDisplay => DefaultClients.Display(State);
}

/// <summary>
/// The two default clients of the Tibia launcher (8.6): the Default Live Client in
/// <c>&lt;packages&gt;\Tibia</c> and the Default Test Client in <c>&lt;packages&gt;\TibiaExternal</c>
/// (or the test-server package chosen in Settings), where <c>&lt;packages&gt;</c> is
/// <c>%LOCALAPPDATA%\Tibia\packages</c> of the signed-in Windows user unless another packages
/// folder was chosen. Paths are never built from a user name. Each folder is classified by its files
/// (never by its name) and only read.
/// </summary>
public static class DefaultClients
{
    public const string LiveLabel = "Default Live Client";
    public const string TestLabel = "Default Test Client";

    public static string LabelOf(ClientScope scope) => scope == ClientScope.Live ? LiveLabel : TestLabel;

    /// <summary>The default package folder of a server inside a packages folder.</summary>
    public static string FolderOf(ClientScope scope, string packagesFolder, string? testPackage = null) =>
        Path.Combine(packagesFolder, scope == ClientScope.Live ? TibiaFormat.LivePackage : string.IsNullOrWhiteSpace(testPackage) ? TibiaFormat.DefaultTestPackage : testPackage);

    /// <summary>Inspects both default clients (live first).</summary>
    public static IReadOnlyList<DefaultClientStatus> Inspect(Func<ClientScope, string> folderOf, IReadOnlyDictionary<string, string>? knownContent = null) =>
        [Inspect(ClientScope.Live, folderOf(ClientScope.Live), knownContent), Inspect(ClientScope.TestServer, folderOf(ClientScope.TestServer), knownContent)];

    /// <summary>What is at <paramref name="folder"/>, the expected folder of one default client.</summary>
    public static DefaultClientStatus Inspect(ClientScope scope, string folder, IReadOnlyDictionary<string, string>? knownContent = null)
    {
        string label = LabelOf(scope);
        string path;
        try { path = PathGuard.Normalize(folder); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(scope, label, folder, DefaultClientState.Inaccessible, null, L.F("DefaultClients.ThePathIsNotValid0", ex.Message));
        }
        if (!Directory.Exists(path)) return new(scope, label, path, DefaultClientState.Missing, null, L.T("DefaultClients.NotInstalledTheFolderDoesNot"));
        try
        {
            // Listing proves the folder can be read (Directory.Exists is true for a folder without permission to list it).
            using (var e = Directory.EnumerateFileSystemEntries(path).GetEnumerator()) e.MoveNext();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return new(scope, label, path, DefaultClientState.Inaccessible, null, L.F("DefaultClients.TheFolderCannotBeRead0", ex.Message));
        }
        FolderInspection inspection;
        try { inspection = FolderInspector.Inspect(path, knownContent); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidDataException)
        {
            return new(scope, label, path, DefaultClientState.Inaccessible, null, L.F("DefaultClients.TheFolderCouldNotBeRead", ex.Message));
        }
        var state = inspection.Class switch
        {
            FolderClass.CompleteInstallation when inspection.IsUsable => DefaultClientState.Available,
            FolderClass.CompleteInstallation or FolderClass.StandaloneClient or FolderClass.AssetsOnly => inspection.IsUsable ? DefaultClientState.Partial : DefaultClientState.Incomplete,
            FolderClass.Insufficient => DefaultClientState.Incomplete,
            _ => DefaultClientState.NotAClient,
        };
        string explanation = state switch
        {
            DefaultClientState.Available => L.F("DefaultClients.CompleteInstallation0", inspection.Version.Display),
            DefaultClientState.Partial => L.F("DefaultClients.0NotACompleteInstallation1", inspection.ClassDisplay, inspection.Version.Display),
            DefaultClientState.Incomplete => L.T("DefaultClients.IncompleteSomeClientFilesAreMissing") + string.Join(" ", inspection.Notes),
            _ => L.T("DefaultClients.NoTibiaClientInThisFolder") + string.Join(" ", inspection.Notes),
        };
        return new(scope, label, path, state, inspection, explanation.Trim());
    }

    public static string Display(DefaultClientState state) => state switch
    {
        DefaultClientState.Available => "Available",
        DefaultClientState.Partial => L.T("DefaultClients.UsableNotACompleteInstallation"),
        DefaultClientState.Incomplete => "Incomplete",
        DefaultClientState.NotAClient => L.T("DefaultClients.NoClientFound"),
        DefaultClientState.Missing => L.T("DefaultClients.NotInstalled"),
        _ => L.T("DefaultClients.CannotBeRead"),
    };
}
