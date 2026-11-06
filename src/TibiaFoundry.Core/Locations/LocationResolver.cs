using System.Text;
using System.Text.Json;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Locations;

/// <summary>Whether a location is a file or a folder (decides which picker is shown).</summary>
public enum LocationKind { File, Folder }

/// <summary>Which client a location belongs to. The packages folder is shared by both.</summary>
public enum ClientScope { Live, TestServer }

/// <summary>Where the path of a location came from.</summary>
public enum LocationSource
{
    /// <summary>The user chose this file or folder (Settings or the Compatibility page).</summary>
    ChosenByYou,
    /// <summary>Found inside a folder the user chose (for example the assets folder of a chosen package folder).</summary>
    FromChosenFolder,
    /// <summary>Found by the automatic search (the installed client).</summary>
    FoundAutomatically,
    /// <summary>Not found; <see cref="ResolvedLocation.ExpectedPath"/> says where it was looked for.</summary>
    NotFound,
    /// <summary>From an imported version in the client store (7.3): read through its manifest.</summary>
    ImportedVersion,
}

/// <summary>Stable names of the client and data locations. They are keys in the settings file: never rename one.</summary>
public static class LocationIds
{
    public const string PackagesFolder = "packages";
    public const string Package = "package";
    public const string PackageManifest = "package-manifest";
    public const string Assets = "assets";
    public const string Appearances = AssetTypes.Appearances;
    public const string StaticData = AssetTypes.StaticData;
    public const string StaticMapData = AssetTypes.StaticMapData;
    public const string Map = AssetTypes.Map;
    public const string SoundsFolder = "sounds-folder";
    public const string Sounds = AssetTypes.Sounds;
    public const string ResourceFile = "rcc";
    public const string SpritesFolder = "sprites";

    /// <summary>The client data files, by data type (the same names the catalog uses).</summary>
    public static IReadOnlyList<string> DataFiles { get; } = [Appearances, StaticData, StaticMapData, Map, Sounds];
}

/// <summary>Description of one file or folder Tibia Foundry looks for.</summary>
public sealed record LocationDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required LocationKind Kind { get; init; }

    /// <summary>One sentence: what it is and where it normally is.</summary>
    public required string Description { get; init; }

    /// <summary>Required locations make the client data unusable when they are missing; optional ones only affect a feature.</summary>
    public bool Required { get; init; }

    /// <summary>False for the packages folder, which the live client and the test server share.</summary>
    public bool PerClient { get; init; } = true;

    /// <summary>Location this one is normally found in (its default is derived from the parent's path).</summary>
    public string? ParentId { get; init; }

    /// <summary>Filter of the file picker (files only).</summary>
    public string? FileFilter { get; init; }

    /// <summary>Compatibility component that reports this location, or null.</summary>
    public string? Component { get; init; }
}

/// <summary>A location after resolution: the path in use, where it came from, and any problem.</summary>
/// <param name="Path">The usable path, or null when nothing usable was found.</param>
/// <param name="ExpectedPath">Where the location was looked for (shown when it is missing).</param>
/// <param name="Problem">Why nothing usable was found (null when <paramref name="Path"/> is set).</param>
/// <param name="ChosenPath">The path the user chose, also when it is unavailable; null when the location is automatic.</param>
/// <param name="ChosenProblem">Why the chosen path cannot be used right now (null when it is used).</param>
public sealed record ResolvedLocation(LocationDefinition Definition, string? Path, string ExpectedPath, LocationSource Source, string? Problem,
    string? ChosenPath, string? ChosenProblem)
{
    public string Id => Definition.Id;
    public bool Found => Path != null;
    public bool IsChosen => ChosenPath != null;
    public bool ChosenUnavailable => ChosenPath != null && ChosenProblem != null;
    public string DisplayPath => Path ?? ExpectedPath;

    /// <summary>For an imported version's folders (assets, sounds): how their files are read. Null for real folders (use <see cref="ClientFolder"/>).</summary>
    public IClientFolder? Folder { get; init; }

    /// <summary>The files of a folder location (a real folder or an imported version's), or null when it is not found or not a folder.</summary>
    public IClientFolder? ClientFolder => Folder ?? (Path != null && Definition.Kind == LocationKind.Folder ? new PhysicalClientFolder(Path) : null);

    public bool IsImported => Source == LocationSource.ImportedVersion;

    /// <summary>"Chosen by you", "Found automatically", …</summary>
    public string SourceText => Source switch
    {
        LocationSource.ChosenByYou => L.T("LocationResolver.ChosenByYou"),
        LocationSource.FromChosenFolder => L.T("LocationResolver.FoundInTheFolderYouChose"),
        LocationSource.FoundAutomatically => L.T("LocationResolver.FoundAutomatically"),
        LocationSource.ImportedVersion => L.T("LocationResolver.FromTheImportedVersion"),
        _ => Definition.Required ? L.T("LocationResolver.NotFound") : L.T("LocationResolver.NotFoundOptional"),
    };

    /// <summary>A notice when the user's choice cannot be used and the automatic location is used instead; otherwise null.</summary>
    public string? Notice => ChosenUnavailable
        ? L.F("LocationResolver.The0YouChoseIsUnavailable", (Definition.Kind == LocationKind.File ? "file" : "folder"), ChosenPath, ChosenProblem) +
          (Found ? L.T("LocationResolver.TheOneFoundAutomaticallyIsUsed") : L.T("LocationResolver.LocateItAgainOrResetTo"))
        : null;
}

/// <summary>
/// Every file and folder Tibia Foundry reads from a Tibia client, and how each is found: a
/// location the user chose (when it is usable), then a location derived from a folder the user
/// chose, then the automatic search in the installed client. A chosen location that is
/// unavailable (for example on a disconnected drive) is never forgotten: it is reported, the
/// automatic location is used meanwhile, and the choice applies again once it is available.
/// </summary>
/// <remarks>
/// Resolution only uses cheap checks (existence, file type, small headers), so it can run at
/// start-up. Deeper checks of a newly chosen location are done by <see cref="DeepCheck"/> and the
/// compatibility check. Results are cached for the lifetime of the resolver: create a new one to
/// see changes on disk. Thread-safe.
/// </remarks>
public sealed class LocationResolver
{
    private readonly IReadOnlyDictionary<string, string> _chosen;
    private readonly string _packagesRootDefault;
    private readonly Dictionary<string, ResolvedLocation> _cache = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <param name="scope">Which client this resolver describes.</param>
    /// <param name="packageName">Folder name of the package inside the packages folder ("Tibia", or the test-server package).</param>
    /// <param name="chosen">Locations the user chose (location ID → path).</param>
    /// <param name="packagesRootDefault">Automatic packages folder (default <c>%LOCALAPPDATA%\Tibia\packages</c>).</param>
    /// <param name="reportedOptional">Optional locations the compatibility check reports (the application passes the resource file and a chosen sprites folder).</param>
    /// <param name="imported">An imported version (7.3): its files replace the package's.</param>
    public LocationResolver(ClientScope scope, string packageName, IReadOnlyDictionary<string, string>? chosen = null, string? packagesRootDefault = null,
        IEnumerable<string>? reportedOptional = null, ManifestClientFiles? imported = null)
    {
        Imported = imported;
        Scope = scope;
        PackageName = string.IsNullOrWhiteSpace(packageName) ? TibiaFormat.LivePackage : packageName.Trim();
        _chosen = chosen?.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value.Trim(), StringComparer.Ordinal)
                  ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _packagesRootDefault = packagesRootDefault ?? TibiaClientPaths.PackagesRoot;
        ReportedOptional = (reportedOptional ?? []).Where(Definitions.ContainsKey).Distinct().ToList();
    }

    public ClientScope Scope { get; }
    public string PackageName { get; }

    /// <summary>The imported version these locations read, or null for an installed or registered client folder.</summary>
    public ManifestClientFiles? Imported { get; }

    /// <summary>Locations an imported version provides (the rest are the installed client's or chosen).</summary>
    private static readonly IReadOnlySet<string> FromImportedVersion = new HashSet<string>(StringComparer.Ordinal)
    {
        LocationIds.Package, LocationIds.PackageManifest, LocationIds.Assets, LocationIds.Appearances, LocationIds.StaticData, LocationIds.StaticMapData,
        LocationIds.Map, LocationIds.SoundsFolder, LocationIds.Sounds, LocationIds.ResourceFile,
    };

    /// <summary>Optional locations that get a row in the compatibility report.</summary>
    public IReadOnlyList<string> ReportedOptional { get; }

    /// <summary>
    /// A resolver for an explicit package folder (tests, self-test, tools): the folder counts as found
    /// automatically, and an assets folder given separately counts as chosen.
    /// </summary>
    public static LocationResolver ForClient(TibiaClientPaths client, string? assetsOverride = null)
    {
        string package = Path.GetFullPath(client.PackageDirectory).TrimEnd(Path.DirectorySeparatorChar);
        string root = Path.GetDirectoryName(package) ?? package;
        var chosen = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(assetsOverride)) chosen[LocationIds.Assets] = assetsOverride;
        return new LocationResolver(ClientScope.Live, Path.GetFileName(package), chosen, root);
    }

    // ------------------------------------------------------------------ definitions

    private const string DataFilter = "Tibia data files (*.dat)|*.dat|All files (*.*)|*.*";

    /// <summary>All locations, in the order they are shown.</summary>
    public static IReadOnlyList<LocationDefinition> All { get; } =
    [
        new()
        {
            Id = LocationIds.PackagesFolder, Name = L.T("LocationResolver.TibiaPackagesFolder"), Kind = LocationKind.Folder, PerClient = false,
            Description = L.T("LocationResolver.TheFolderThatHoldsTheInstalled"),
        },
        new()
        {
            Id = LocationIds.Package, Name = L.T("LocationResolver.ClientPackageFolder"), Kind = LocationKind.Folder, Required = true, ParentId = LocationIds.PackagesFolder,
            Component = CompatibilityChecker.PackageComponent,
            Description = L.T("LocationResolver.TheFolderOfOneClientWith"),
        },
        new()
        {
            Id = LocationIds.PackageManifest, Name = L.T("LocationResolver.ClientVersionFilePackageJson"), Kind = LocationKind.File, ParentId = LocationIds.Package,
            Component = CompatibilityChecker.PackageComponent, FileFilter = L.T("LocationResolver.PackageJsonPackageJsonJSONFiles"),
            Description = L.T("LocationResolver.PackageJsonOfTheClientPackage"),
        },
        new()
        {
            Id = LocationIds.Assets, Name = L.T("Common.ClientAssetsFolder"), Kind = LocationKind.Folder, Required = true, ParentId = LocationIds.Package,
            Component = CompatibilityChecker.SpriteCatalogComponent,
            Description = L.T("LocationResolver.TheClientSAssetsFolderWith"),
        },
        new()
        {
            Id = LocationIds.Appearances, Name = L.T("LocationResolver.AppearancesFile"), Kind = LocationKind.File, Required = true, ParentId = LocationIds.Assets,
            Component = TibiaFormat.CatalogTypeAppearances, FileFilter = L.T("LocationResolver.TibiaAppearancesAppearancesDatAppearancesDat") + DataFilter,
            Description = L.T("LocationResolver.AppearancesChecksumDatEveryObjectOutfit"),
        },
        new()
        {
            Id = LocationIds.StaticData, Name = L.T("LocationResolver.StaticDataFile"), Kind = LocationKind.File, Required = true, ParentId = LocationIds.Assets,
            Component = TibiaFormat.CatalogTypeStaticData, FileFilter = L.T("LocationResolver.TibiaStaticDataStaticdataDatStaticdata") + DataFilter,
            Description = L.T("LocationResolver.StaticdataChecksumDatMonstersBossesAchievements"),
        },
        new()
        {
            Id = LocationIds.StaticMapData, Name = L.T("LocationResolver.StaticMapDataFile"), Kind = LocationKind.File, Required = true, ParentId = LocationIds.Assets,
            Component = TibiaFormat.CatalogTypeStaticMapData, FileFilter = L.T("LocationResolver.TibiaStaticMapDataStaticmapdataDat") + DataFilter,
            Description = L.T("LocationResolver.StaticmapdataChecksumDatTheHouseMaps"),
        },
        new()
        {
            Id = LocationIds.Map, Name = L.T("LocationResolver.MapFile"), Kind = LocationKind.File, Required = true, ParentId = LocationIds.Assets,
            Component = TibiaFormat.CatalogTypeMap, FileFilter = L.T("LocationResolver.TibiaMapDataMapDatMap") + DataFilter,
            Description = L.T("LocationResolver.MapChecksumDatAreasNPCsAnd"),
        },
        new()
        {
            Id = LocationIds.SoundsFolder, Name = L.T("LocationResolver.ClientSoundsFolder"), Kind = LocationKind.Folder, Required = true, ParentId = LocationIds.Package,
            Component = TibiaFormat.CatalogTypeSounds,
            Description = L.T("LocationResolver.TheClientSSoundsFolderWith"),
        },
        new()
        {
            Id = LocationIds.Sounds, Name = L.T("LocationResolver.SoundsFile"), Kind = LocationKind.File, Required = true, ParentId = LocationIds.SoundsFolder,
            Component = TibiaFormat.CatalogTypeSounds, FileFilter = L.T("LocationResolver.TibiaSoundsSoundsDatSoundsDat") + DataFilter,
            Description = L.T("LocationResolver.SoundsChecksumDatTheListOf"),
        },
        new()
        {
            Id = LocationIds.ResourceFile, Name = L.T("LocationResolver.ClientResourceFile"), Kind = LocationKind.File, ParentId = LocationIds.Package,
            Component = CompatibilityChecker.ResourceFileComponent, FileFilter = L.T("LocationResolver.QtResourceFilesRccRccAll"),
            Description = L.T("LocationResolver.BinGraphicsResourcesRccTheImages"),
        },
        new()
        {
            Id = LocationIds.SpritesFolder, Name = L.T("LocationResolver.ExtractedSpritesFolder"), Kind = LocationKind.Folder,
            Component = CompatibilityChecker.SpritesFolderComponent,
            Description = L.T("LocationResolver.AFolderOfSpritesExtractedEarlier"),
        },
    ];

    private static readonly Dictionary<string, LocationDefinition> Definitions = All.ToDictionary(d => d.Id, StringComparer.Ordinal);

    public static LocationDefinition Definition(string id) =>
        Definitions.TryGetValue(id, out var d) ? d : throw new ArgumentException($"Unknown location \"{id}\".", nameof(id));

    public static bool IsKnown(string id) => Definitions.ContainsKey(id);

    // ------------------------------------------------------------------ resolution

    /// <summary>The path the user chose for a location of this resolver's client, or null.</summary>
    public string? ChosenPath(string id) => _chosen.TryGetValue(id, out var path) ? path : null;

    /// <summary>The usable path of a location, or null.</summary>
    public string? PathOf(string id) => Resolve(id).Path;

    public ResolvedLocation Resolve(string id)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(id, out var cached)) return cached;
        }
        var resolved = ResolveUncached(Definition(id));
        lock (_lock) _cache[id] = resolved;
        return resolved;
    }

    private ResolvedLocation ResolveUncached(LocationDefinition def)
    {
        if (Imported != null && FromImportedVersion.Contains(def.Id)) return ResolveImported(def, Imported);
        string? chosen = ChosenPath(def.Id);
        string? chosenProblem = null;
        if (chosen != null)
        {
            chosenProblem = QuickCheck(def.Id, chosen);
            if (chosenProblem == null) return new ResolvedLocation(def, chosen, chosen, LocationSource.ChosenByYou, null, chosen, null);
        }

        var (candidate, expected, fromChosen, why) = Automatic(def);
        string? problem = candidate == null ? why ?? L.T("LocationResolver.ItWasNotFound") : QuickCheck(def.Id, candidate);
        if (problem == null)
            return new ResolvedLocation(def, candidate, candidate!, fromChosen ? LocationSource.FromChosenFolder : LocationSource.FoundAutomatically, null, chosen, chosenProblem);
        return new ResolvedLocation(def, null, candidate ?? expected, LocationSource.NotFound, problem, chosen, chosenProblem);
    }

    /// <summary>True when the location's path depends on a location the user chose.</summary>
    private bool FromChosen(string parentId)
    {
        var parent = Resolve(parentId);
        return parent.Source is LocationSource.ChosenByYou or LocationSource.FromChosenFolder;
    }

    /// <summary>The automatic candidate: (path or null, where it was expected, derived from a chosen folder, why it is missing).</summary>
    private (string? Candidate, string Expected, bool FromChosen, string? Why) Automatic(LocationDefinition def)
    {
        switch (def.Id)
        {
            case LocationIds.PackagesFolder:
                return (_packagesRootDefault, _packagesRootDefault, false, null);
            case LocationIds.Package:
            {
                string root = Resolve(LocationIds.PackagesFolder).DisplayPath;
                string path = Path.Combine(root, PackageName);
                return (path, path, FromChosen(LocationIds.PackagesFolder), null);
            }
            case LocationIds.PackageManifest:
            {
                // A chosen assets folder belongs to its own client: its version is in the package.json
                // next to it, not in the installed client's (which would give a wrong version).
                var assets = Resolve(LocationIds.Assets);
                if (assets.Source == LocationSource.ChosenByYou)
                {
                    string next = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(assets.Path!).TrimEnd(Path.DirectorySeparatorChar)) ?? assets.Path!, TibiaFormat.PackageManifest);
                    return File.Exists(next)
                        ? (next, next, true, null)
                        : (null, next, true, L.T("LocationResolver.TheAssetsFolderYouChoseHas"));
                }
                string path = Path.Combine(Resolve(LocationIds.Package).DisplayPath, TibiaFormat.PackageManifest);
                return (path, path, FromChosen(LocationIds.Package), null);
            }
            case LocationIds.Assets:
            {
                string path = Path.Combine(Resolve(LocationIds.Package).DisplayPath, TibiaFormat.AssetsFolder);
                return (path, path, FromChosen(LocationIds.Package), null);
            }
            case LocationIds.Appearances or LocationIds.StaticData or LocationIds.StaticMapData or LocationIds.Map:
            {
                var assets = Resolve(LocationIds.Assets);
                string expected = Path.Combine(assets.DisplayPath, def.Id + "-<checksum>.dat");
                if (!assets.Found) return (null, expected, false, L.T("LocationResolver.TheAssetsFolderWasNotFound"));
                string? file = TibiaClientPaths.FindAssetFile(assets.Path!, def.Id);
                return file != null
                    ? (file, file, FromChosen(LocationIds.Assets), null)
                    : (null, expected, false, L.F("LocationResolver.TheAssetsFolderHasNo0", def.Id));
            }
            case LocationIds.SoundsFolder:
            {
                string path = Path.Combine(Resolve(LocationIds.Package).DisplayPath, TibiaFormat.SoundsFolder);
                return (path, path, FromChosen(LocationIds.Package), null);
            }
            case LocationIds.Sounds:
            {
                var folder = Resolve(LocationIds.SoundsFolder);
                string expected = Path.Combine(folder.DisplayPath, "sounds-<checksum>.dat");
                if (!folder.Found) return (null, expected, false, L.T("LocationResolver.TheSoundsFolderWasNotFound"));
                string? file = TibiaClientPaths.FindSoundsFile(folder.Path!);
                return file != null
                    ? (file, file, FromChosen(LocationIds.SoundsFolder), null)
                    : (null, expected, false, L.T("LocationResolver.TheSoundsFolderHasNoSounds"));
            }
            case LocationIds.ResourceFile:
            {
                string path = Path.Combine(Resolve(LocationIds.Package).DisplayPath, "bin", "graphics_resources.rcc");
                return (path, path, FromChosen(LocationIds.Package), null);
            }
            case LocationIds.SpritesFolder:
                return (null, L.T("LocationResolver.NotSet"), false, L.T("LocationResolver.NoExtractedSpritesFolderIsSet"));
            default:
                throw new ArgumentException($"Unknown location \"{def.Id}\".");
        }
    }

    /// <summary>A location of an imported version: read through its manifest (objects in the store), never from a client folder.</summary>
    private static ResolvedLocation ResolveImported(LocationDefinition def, ManifestClientFiles files)
    {
        string folder = files.VersionFolder;
        ResolvedLocation Found(string path, IClientFolder? clientFolder = null) =>
            new(def, path, path, LocationSource.ImportedVersion, null, null, null) { Folder = clientFolder };
        ResolvedLocation Missing(string expected, string why) =>
            new(def, null, expected, LocationSource.NotFound, why, null, null);
        var assets = files.Folder(TibiaFormat.AssetsFolder);
        var sounds = files.Folder(TibiaFormat.SoundsFolder);
        switch (def.Id)
        {
            case LocationIds.Package:
                return Found(folder);
            case LocationIds.PackageManifest:
                return files.PathOf(TibiaFormat.PackageManifest) is { } manifest
                    ? Found(manifest)
                    : Missing(Path.Combine(folder, TibiaFormat.PackageManifest), L.T("LocationResolver.ThisImportedVersionHasNoPackage"));
            case LocationIds.Assets:
                return assets.PathOf(TibiaFormat.AssetCatalogFile) != null
                    ? Found(Path.Combine(folder, TibiaFormat.AssetsFolder), assets)
                    : Missing(Path.Combine(folder, TibiaFormat.AssetsFolder), L.T("LocationResolver.ThisImportedVersionHasNoCatalog"));
            case LocationIds.Appearances or LocationIds.StaticData or LocationIds.StaticMapData or LocationIds.Map:
                return TibiaClientPaths.FindAssetFile(assets, def.Id) is { } data
                    ? Found(data)
                    : Missing(Path.Combine(folder, TibiaFormat.AssetsFolder, def.Id + "-<checksum>.dat"), L.F("LocationResolver.ThisImportedVersionHasNo0", def.Id));
            case LocationIds.SoundsFolder:
                return sounds.PathOf(TibiaFormat.SoundCatalogFile) != null || files.Manifest.Files.Any(f => f.Path.StartsWith("sounds/sounds-", StringComparison.OrdinalIgnoreCase))
                    ? Found(Path.Combine(folder, TibiaFormat.SoundsFolder), sounds)
                    : Missing(Path.Combine(folder, TibiaFormat.SoundsFolder), L.T("LocationResolver.ThisImportedVersionHasNoSounds"));
            case LocationIds.Sounds:
                return TibiaClientPaths.FindSoundsFile(sounds) is { } soundsFile
                    ? Found(soundsFile)
                    : Missing(Path.Combine(folder, TibiaFormat.SoundsFolder, "sounds-<checksum>.dat"), L.T("LocationResolver.ThisImportedVersionHasNoSounds2"));
            case LocationIds.ResourceFile:
                return files.ResourceFiles.Count > 0 && files.ResourceFiles[0] is { } rcc && files.PathOf(rcc.Path) is { } rccPath
                    ? Found(rccPath)
                    : Missing(Path.Combine(folder, "bin", "graphics_resources.rcc"), L.T("LocationResolver.ThisImportedVersionHasNoQt"));
            default:
                throw new ArgumentException($"\"{def.Id}\" is not read from imported versions.");
        }
    }

    // ------------------------------------------------------------------ dependencies

    /// <summary>
    /// Locations whose path may change when <paramref name="id"/> changes (itself and everything
    /// derived from it).
    /// </summary>
    public static IReadOnlySet<string> AffectedBy(string id)
    {
        var result = new HashSet<string>(StringComparer.Ordinal) { id };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var def in All)
            {
                bool depends = def.ParentId != null && result.Contains(def.ParentId)
                               // The version file follows a chosen assets folder.
                               || def.Id == LocationIds.PackageManifest && result.Contains(LocationIds.Assets);
                if (depends && result.Add(def.Id)) grew = true;
            }
        }
        return result;
    }

    /// <summary>
    /// Compatibility components to check again after <paramref name="id"/> changed, or null when the
    /// whole check must run again (the package, its version or the assets folder changed).
    /// </summary>
    public static IReadOnlySet<string>? ComponentsAffectedBy(string id)
    {
        var affected = AffectedBy(id);
        if (affected.Contains(LocationIds.PackageManifest) || affected.Contains(LocationIds.Assets)) return null;
        var components = new HashSet<string>(StringComparer.Ordinal);
        foreach (string location in affected)
            if (Definition(location).Component is { } component) components.Add(component);
        // Static data and house maps are validated against the appearances.
        if (affected.Contains(LocationIds.Appearances))
        {
            components.Add(TibiaFormat.CatalogTypeStaticData);
            components.Add(TibiaFormat.CatalogTypeStaticMapData);
        }
        return components;
    }

    // ------------------------------------------------------------------ checks

    /// <summary>
    /// Cheap check that a path can be the location: exists, has the right type and, where it is
    /// cheap, the right content. Returns null when it can, otherwise why not.
    /// </summary>
    public static string? QuickCheck(string id, string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return L.T("LocationResolver.ThisIsNotAValidPath");
            var def = Definition(id);
            if (def.Kind == LocationKind.Folder)
            {
                if (File.Exists(path)) return L.T("LocationResolver.ThisIsAFileButA");
                if (!Directory.Exists(path)) return L.T("LocationResolver.ThisFolderDoesNotExist");
            }
            else
            {
                if (Directory.Exists(path)) return L.T("LocationResolver.ThisIsAFolderButA");
                if (!File.Exists(path)) return L.T("LocationResolver.ThisFileDoesNotExist");
                if (new FileInfo(path).Length == 0) return L.T("LocationResolver.ThisFileIsEmpty");
            }
            switch (id)
            {
                case LocationIds.Package:
                    return File.Exists(Path.Combine(path, TibiaFormat.PackageManifest)) || File.Exists(Path.Combine(path, TibiaFormat.AssetsFolder, TibiaFormat.AssetCatalogFile))
                        ? null
                        : L.T("LocationResolver.ThisIsNotATibiaClient");
                case LocationIds.PackageManifest:
                    return TibiaClientPaths.ReadVersion(path) != null ? null : L.T("LocationResolver.ThisFileDoesNotContainA");
                case LocationIds.Assets:
                    return File.Exists(Path.Combine(path, TibiaFormat.AssetCatalogFile)) ? null : L.T("LocationResolver.ThisFolderHasNoCatalogContent");
                case LocationIds.Appearances or LocationIds.StaticData or LocationIds.StaticMapData or LocationIds.Map or LocationIds.Sounds:
                    return Path.GetExtension(path).Equals(".dat", StringComparison.OrdinalIgnoreCase) ? null : L.T("LocationResolver.TibiaDataFilesHaveTheExtension");
                case LocationIds.SoundsFolder:
                    return File.Exists(Path.Combine(path, TibiaFormat.SoundCatalogFile)) || Directory.EnumerateFiles(path, "sounds-*.dat").Any()
                        ? null
                        : L.T("LocationResolver.ThisFolderHasNeitherCatalogSound");
                case LocationIds.ResourceFile:
                {
                    Span<byte> magic = stackalloc byte[4];
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    return stream.ReadAtLeast(magic, 4, throwOnEndOfStream: false) == 4 && magic.SequenceEqual("qres"u8) ? null : L.T("LocationResolver.ThisIsNotAQtResource");
                }
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return L.T("LocationResolver.ItCannotBeRead") + ex.Message;
        }
    }

    /// <summary>
    /// Thorough check of a folder the user picked (catalog readable). Data files are checked by the
    /// compatibility check instead. Returns null when fine, otherwise why not.
    /// </summary>
    public static string? DeepCheck(string id, string path)
    {
        if (QuickCheck(id, path) is { } problem) return problem;
        try
        {
            switch (id)
            {
                case LocationIds.Assets:
                    var catalog = AssetCatalog.Load(path);
                    return catalog.SheetCount == 0 ? L.T("LocationResolver.ItsCatalogContentJsonListsNo") : null;
                case LocationIds.Package:
                    string assets = Path.Combine(path, TibiaFormat.AssetsFolder);
                    return File.Exists(Path.Combine(assets, TibiaFormat.AssetCatalogFile)) ? DeepCheck(LocationIds.Assets, assets) : null;
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Corrects a common picking mistake: choosing the package folder where its assets or sounds
    /// folder is needed (and the other way round). Returns the path to use and, when it was
    /// corrected, a note for the user.
    /// </summary>
    public static (string Path, string? Note) NormalizeChoice(string id, string path)
    {
        try
        {
            path = Path.GetFullPath(path.Trim());
            string Sub(params string[] parts) => Path.Combine([path, .. parts]);
            switch (id)
            {
                case LocationIds.Assets when !File.Exists(Sub(TibiaFormat.AssetCatalogFile)) && File.Exists(Sub(TibiaFormat.AssetsFolder, TibiaFormat.AssetCatalogFile)):
                    return (Sub(TibiaFormat.AssetsFolder), L.T("LocationResolver.ItsAssetsSubfolderIsUsed"));
                case LocationIds.SoundsFolder when !File.Exists(Sub(TibiaFormat.SoundCatalogFile)) && File.Exists(Sub(TibiaFormat.SoundsFolder, TibiaFormat.SoundCatalogFile)):
                    return (Sub(TibiaFormat.SoundsFolder), L.T("LocationResolver.ItsSoundsSubfolderIsUsed"));
                case LocationIds.Package when QuickCheck(id, path) != null && Path.GetDirectoryName(path) is { } parent
                                              && (File.Exists(Sub(TibiaFormat.AssetCatalogFile)) || File.Exists(Sub(TibiaFormat.SoundCatalogFile)))
                                              && QuickCheck(id, parent) == null:
                    return (parent, L.T("LocationResolver.ThePackageFolderThatContainsIt"));
                case LocationIds.PackagesFolder when Directory.Exists(Sub("packages")) && !File.Exists(Sub(TibiaFormat.PackageManifest)):
                    return (Sub("packages"), L.T("LocationResolver.ItsPackagesSubfolderIsUsed"));
                default:
                    return (path, null);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (path, null);
        }
    }

    /// <summary>Plain-text list of every location (for reports and the diagnostics report).</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        string source = Imported != null ? Imported.Description.ToLowerInvariant() : L.F("LocationResolver.Package0", PackageName);
        sb.AppendLine(L.F("LocationResolver.ClientLocations01", (Scope == ClientScope.Live ? L.T("LocationResolver.LiveClient") : L.T("LocationResolver.TestServer")), source));
        foreach (var def in All)
        {
            var r = Resolve(def.Id);
            sb.AppendLine($"  {def.Name}: {r.DisplayPath} — {r.SourceText}{(r.Problem != null ? $" ({r.Problem})" : "")}");
            if (r.Notice != null) sb.AppendLine("    " + r.Notice);
        }
        return sb.ToString();
    }
}
