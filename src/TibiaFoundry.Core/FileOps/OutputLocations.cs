using TibiaFoundry.Core.Localization;
namespace TibiaFoundry.Core.FileOps;

/// <summary>The kinds of files Tibia Foundry writes; each has its own default folder.</summary>
public enum OutputKind
{
    GifExports,
    ImageExports,
    SpriteExports,
    SoundExports,
    MapExports,
    DataExports,
    ItemCatalogs,
    RccExports,
    ClientConverter,
    AssetsEditor,
    Scripts,
    Reports,
    Manifests,
    Backups,
    Logs,
    Temporary,
}

/// <summary>Description of an <see cref="OutputKind"/>: its default subfolder and what goes there.</summary>
/// <param name="UnderRoot">
/// True when the default lies under the output root (Documents\Tibia_Foundry). Logs, backups and
/// temporary files of the portable edition stay next to the program, and temporary files of the
/// installed edition stay in the Windows temporary folder (see <c>AppServices</c>).
/// </param>
public sealed record OutputKindInfo(OutputKind Kind, string Subfolder, string DisplayName, string Description, bool UnderRoot = true);

/// <summary>Result of checking or preparing a folder before files are written into it.</summary>
public sealed record FolderState(string Path, bool Exists, bool Usable, string? Problem)
{
    public static FolderState Ok(string path, bool exists) => new(path, exists, true, null);
    public static FolderState Fail(string path, string problem) => new(path, false, false, problem);
}

/// <summary>
/// Default output locations. Everything Tibia Foundry generates is organized under one root,
/// <c>&lt;Documents&gt;\Tibia_Foundry</c>, with one subfolder per kind of output. Documents is
/// resolved through Windows (the known-folder API behind <see cref="Environment.SpecialFolder.MyDocuments"/>),
/// so a Documents folder moved to another drive or into OneDrive is followed. Folders are created
/// only when something is written into them, and a folder that cannot be used is reported, never
/// silently replaced by another location.
/// </summary>
public static class OutputLocations
{
    /// <summary>Name of the root folder inside Documents.</summary>
    public const string RootFolderName = "Tibia_Foundry";

    public static IReadOnlyList<OutputKindInfo> Kinds { get; } =
    [
        new(OutputKind.GifExports, "GIF_Exports", L.T("OutputLocations.GIFExports"), L.T("OutputLocations.AnimatedGIFsOfObjectsOutfitsEffects")),
        new(OutputKind.ImageExports, "Image_Exports", L.T("OutputLocations.ImageExports"), L.T("OutputLocations.PNGImagesOutfitSheetsHouseAnd")),
        new(OutputKind.SpriteExports, "Sprite_Exports", L.T("OutputLocations.SpriteExports"), L.T("OutputLocations.ExtractSpritesAndTheSpriteGallery")),
        new(OutputKind.SoundExports, "Sound_Exports", L.T("OutputLocations.SoundExports"), L.T("OutputLocations.ExtractedSoundFiles")),
        new(OutputKind.MapExports, "Map_Exports", L.T("OutputLocations.MapExports"), L.T("OutputLocations.MapTilesExtractedFromTheClient")),
        new(OutputKind.DataExports, "Data_Exports", L.T("OutputLocations.DataExports"), L.T("OutputLocations.JSONExportsIDTablesAndLua")),
        new(OutputKind.ItemCatalogs, "Item_Catalogs", L.T("OutputLocations.ItemCatalogs"), L.T("OutputLocations.ItemNameAndIDCatalogsTheir")),
        new(OutputKind.RccExports, "RCC_Exports", L.T("OutputLocations.RCCEditor"), L.T("OutputLocations.ResourcesExtractedFromQtResourceFiles")),
        new(OutputKind.ClientConverter, "Client_Converter", L.T("OutputLocations.ClientConverter"), L.T("OutputLocations.ConvertedTibiaDatAndTibiaSpr")),
        new(OutputKind.AssetsEditor, "Assets_Editor", L.T("OutputLocations.AssetsEditor"), L.T("OutputLocations.SavedCopiesOfAssetsFoldersExported")),
        new(OutputKind.Scripts, "Scripts", "Scripts", L.T("OutputLocations.FilesWrittenByUtilitiesScriptsConverted")),
        new(OutputKind.Reports, "Reports", "Reports", L.T("OutputLocations.DiagnosticsBundlesComparisonReportsAndScript")),
        new(OutputKind.Manifests, "Manifests", "Manifests", L.T("OutputLocations.FileInventoriesAndChecksumManifests")),
        new(OutputKind.Backups, "Backups", "Backups", L.T("OutputLocations.CopiesOfFilesAToolReplaced")),
        new(OutputKind.Logs, "Logs", "Logs", L.T("OutputLocations.TheProgramSLogFiles")),
        new(OutputKind.Temporary, "Temporary", L.T("OutputLocations.TemporaryFiles"), L.T("OutputLocations.WorkingFilesOfRunningOperations"), UnderRoot: false),
    ];

    public static OutputKindInfo Info(OutputKind kind) => Kinds.First(k => k.Kind == kind);

    /// <summary>Override for tests (per async flow): a folder used instead of the real Documents folder; "" = none found.</summary>
    internal static readonly AsyncLocal<string?> DocumentsOverrideForTests = new();

    /// <summary>
    /// The user's Documents folder as Windows reports it (follows redirection, for example to
    /// OneDrive), or null when Windows reports none (for example under a service account).
    /// </summary>
    public static string? DocumentsFolder
    {
        get
        {
            if (DocumentsOverrideForTests.Value is { } overridden) return overridden.Length == 0 ? null : overridden;
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify);
            return string.IsNullOrWhiteSpace(documents) ? null : documents;
        }
    }

    /// <summary><c>&lt;Documents&gt;\Tibia_Foundry</c>, or "" when Documents cannot be resolved.</summary>
    public static string DefaultRoot => DocumentsFolder is { } documents ? Path.Combine(documents, RootFolderName) : "";

    /// <summary>The default folder of a kind under the given root.</summary>
    public static string DefaultFor(OutputKind kind, string root) => Path.Combine(root, Info(kind).Subfolder);

    /// <summary>The 5.0 default output folder (<c>Documents\TibiaSprites</c>); used only to recognize it when settings are upgraded.</summary>
    public static string? PreviousDefaultRoot => DocumentsFolder is { } documents ? Path.Combine(documents, "TibiaSprites") : null;

    /// <summary>
    /// Checks a folder without changing anything: whether the path is valid, the drive is available,
    /// and (when the folder exists) whether files can be created in it.
    /// </summary>
    public static FolderState Check(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return FolderState.Fail(path ?? "", L.T("OutputLocations.NoFolderIsSetAndYour"));
        string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (!Path.IsPathFullyQualified(expanded)) return FolderState.Fail(path, L.F("OutputLocations.0IsNotACompleteFolder", path));
        string full;
        try
        {
            full = Path.GetFullPath(expanded);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return FolderState.Fail(path, L.F("OutputLocations.0IsNotAValidFolder", path));
        }
        string? drive = Path.GetPathRoot(full);
        // Windows checks names only when a folder is created; check them now to explain the problem.
        foreach (string name in full[(drive?.Length ?? 0)..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (WindowsNames.HasInvalidChars(name) || WindowsNames.EndsWithDotOrSpace(name) || WindowsNames.IsDeviceName(name))
                return FolderState.Fail(path, L.F("OutputLocations.0IsNotAValidFolder2", path, name));
        }
        if (string.IsNullOrEmpty(drive) || !Directory.Exists(drive))
            return FolderState.Fail(full, L.F("OutputLocations.TheDriveOrNetworkShare0", drive));
        if (File.Exists(full)) return FolderState.Fail(full, L.F("OutputLocations.0IsAFileNotA", full));
        // The one guard for every output: tools never write into a Tibia client, the client archive or a registered client folder.
        if (Editing.AssetsProject.IsInsideClientInstallation(full))
            return FolderState.Fail(full, L.F("OutputLocations.0IsInsideATibiaClient", full));
        if (!Directory.Exists(full)) return FolderState.Ok(full, exists: false);
        return CanWrite(full, out string? problem) ? FolderState.Ok(full, exists: true) : FolderState.Fail(full, problem!);
    }

    /// <summary>
    /// Makes sure a folder can be written to, creating it (and its parents) when it does not exist.
    /// Returns the problem in plain words when it cannot be used. Never chooses another folder.
    /// </summary>
    public static FolderState Prepare(string? path)
    {
        var state = Check(path);
        if (!state.Usable || state.Exists) return state;
        try
        {
            Directory.CreateDirectory(state.Path);
        }
        catch (UnauthorizedAccessException)
        {
            return FolderState.Fail(state.Path, L.F("OutputLocations.0CouldNotBeCreatedYou", state.Path));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return FolderState.Fail(state.Path, L.F("OutputLocations.0CouldNotBeCreated1", state.Path, ex.Message));
        }
        return CanWrite(state.Path, out string? problem) ? FolderState.Ok(state.Path, exists: true) : FolderState.Fail(state.Path, problem!);
    }

    /// <summary>
    /// Whether files can be created in an existing folder, found out by creating (and at once deleting)
    /// a small probe file; <paramref name="problem"/> says why not in plain words. The one probe every
    /// part of the program uses (start-up, Settings, the self-test, output folders).
    /// </summary>
    public static bool CanWrite(string folder, out string? problem)
    {
        problem = null;
        string probe = Path.Combine(folder, $".tf-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            problem = L.F("OutputLocations.YouDoNotHavePermissionTo", folder);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            problem = L.F("OutputLocations.0CannotBeWrittenTo1", folder, ex.Message);
        }
        try { if (File.Exists(probe)) File.Delete(probe); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return false;
    }

    /// <summary>
    /// Shortens a long path for display by replacing middle folders with "…", keeping the drive,
    /// the first folder and the last folders and file name (the full path belongs in a tooltip).
    /// </summary>
    public static string ShortenForDisplay(string path, int maxLength = 90)
    {
        if (path.Length <= maxLength) return path;
        string root = Path.GetPathRoot(path) ?? "";
        var parts = path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count <= 2) return "…" + path[^Math.Max(1, maxLength - 1)..];
        // Keep the file and as many trailing folders as fit, plus the first folder after the root.
        string head = root + parts[0] + Path.DirectorySeparatorChar + "…";
        string tail = parts[^1];
        for (int i = parts.Count - 2; i >= 1; i--)
        {
            string candidate = parts[i] + Path.DirectorySeparatorChar + tail;
            if (head.Length + 1 + candidate.Length > maxLength) break;
            tail = candidate;
        }
        string result = head + Path.DirectorySeparatorChar + tail;
        return result.Length <= maxLength ? result : "…" + path[^Math.Max(1, maxLength - 1)..];
    }
}
