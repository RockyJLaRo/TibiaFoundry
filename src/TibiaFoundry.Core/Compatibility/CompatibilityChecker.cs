using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf.Reflection;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Houses;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Map;
using TibiaFoundry.Core.Protobuf;
using TibiaFoundry.Core.Sounds;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.StaticData;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Compatibility;

/// <summary>How well a part of the client data is understood. Ordered from best to worst.</summary>
public enum CompatibilityStatus
{
    /// <summary>Validated client version; every check passed.</summary>
    Supported,
    /// <summary>Every check passed, but the version was not validated with this build, or the file contains new information this build ignores.</summary>
    Unfamiliar,
    /// <summary>Usable, but some records failed checks; affected features may show gaps.</summary>
    Partial,
    /// <summary>Required file is missing.</summary>
    Missing,
    /// <summary>Could not be read or lacks required content; affected features cannot work.</summary>
    Unsupported,
}

/// <summary>Result for one part of the client data (catalog or a data file).</summary>
public sealed record ComponentReport(string Component, string? FileName, CompatibilityStatus Status, string Summary,
    IReadOnlyList<string> Details, IReadOnlyList<string> AffectedFeatures)
{
    /// <summary>The location (see <c>Locations.LocationIds</c>) to locate when this component has a problem, or null.</summary>
    public string? LocationId { get; init; }

    /// <summary>Full path of the file or folder that was checked, or where it was expected.</summary>
    public string? Path { get; init; }

    /// <summary>False for optional files: their status never changes the overall result.</summary>
    public bool Required { get; init; } = true;
}

/// <summary>Full compatibility report of one client package.</summary>
public sealed record CompatibilityReport(DateTime CheckedAtUtc, string PackageDirectory, string? ClientVersion, bool VersionValidated,
    CompatibilityStatus Overall, IReadOnlyList<ComponentReport> Components, TimeSpan Duration)
{
    /// <summary>The overall result: the worst required component (optional ones never count).</summary>
    public static CompatibilityStatus OverallOf(IEnumerable<ComponentReport> components) =>
        components.Where(c => c.Required).Select(c => c.Status).DefaultIfEmpty(CompatibilityStatus.Missing).Max();

    /// <summary>
    /// This report with the components of <paramref name="partial"/> (a check of some components only)
    /// replacing the ones of the same name; the overall result is recomputed.
    /// </summary>
    public CompatibilityReport WithComponents(CompatibilityReport partial)
    {
        var replaced = partial.Components.ToDictionary(c => c.Component, StringComparer.Ordinal);
        var components = Components.Select(c => replaced.TryGetValue(c.Component, out var r) ? r : c).ToList();
        components.AddRange(partial.Components.Where(c => Components.All(old => old.Component != c.Component)));
        return this with { CheckedAtUtc = partial.CheckedAtUtc, Components = components, Overall = OverallOf(components), Duration = partial.Duration };
    }

    public static string Describe(CompatibilityStatus status) => status switch
    {
        CompatibilityStatus.Supported => "Supported",
        CompatibilityStatus.Unfamiliar => L.T("CompatibilityChecker.UnfamiliarButCompatible"),
        CompatibilityStatus.Partial => L.T("CompatibilityChecker.PartiallyCompatible"),
        CompatibilityStatus.Missing => "Missing",
        _ => L.T("CompatibilityChecker.NotSupported"),
    };

    /// <summary>Plain-text report (copied to the clipboard or attached to a bug report).</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(L.F("CompatibilityChecker.0CompatibilityReport1YyyyMM", AppIdentity.Name, CheckedAtUtc.ToLocalTime(), Duration.TotalSeconds));
        sb.AppendLine(L.F("CompatibilityChecker.ClientPackage0", PackageDirectory));
        sb.AppendLine(L.F("CompatibilityChecker.ClientVersion01", ClientVersion ?? "unknown", (VersionValidated ? L.T("CompatibilityChecker.ValidatedWithThisBuild") : L.T("CompatibilityChecker.NotValidatedWithThisBuild"))));
        sb.AppendLine(L.F("CompatibilityChecker.Overall0", Describe(Overall)));
        foreach (var c in Components)
        {
            sb.AppendLine();
            sb.AppendLine($"[{Describe(c.Status)}{(c.Required ? "" : ", optional")}] {c.Component}{(c.FileName != null ? " — " + c.FileName : "")}");
            sb.AppendLine("  " + c.Summary);
            foreach (string d in c.Details) sb.AppendLine("  - " + d);
            if (c.Status > CompatibilityStatus.Unfamiliar && c.AffectedFeatures.Count > 0) sb.AppendLine("  Affected: " + string.Join(", ", c.AffectedFeatures));
        }
        return sb.ToString();
    }
}

/// <summary>Data already loaded by the application (re-used so the check does not parse files twice).</summary>
public sealed record LoadedData(AppearanceDatabase? Appearances = null, StaticDataDatabase? StaticData = null, StaticMapDatabase? StaticMap = null,
    MapDatabase? Map = null, SoundDatabase? Sounds = null);

/// <summary>
/// Determines whether the installed client's data can be processed by this build:
/// client version, required files, file integrity (content hashes in file names), decoding,
/// schema drift (fields this build does not know) and structural/cross-file validation.
/// Read-only: never modifies client files.
/// </summary>
public static class CompatibilityChecker
{
    public const string PackageComponent = "Client package";
    public const string SpriteCatalogComponent = "Sprite catalog";
    public const string ResourceFileComponent = "Client resource file";
    public const string SpritesFolderComponent = "Extracted sprites folder";

    /// <summary>The features each component serves, in the active language (built on each use, so a language switch applies).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Features => new Dictionary<string, IReadOnlyList<string>>
    {
        [PackageComponent] = [L.T("Compatibility.Feature.Versiondetection")],
        [SpriteCatalogComponent] = [L.T("Compatibility.Feature.Allspritepreviews"), L.T("Tool.sprite-extractor.Name"), L.T("Compatibility.Feature.GIFexport"), L.T("Appearances.Outfitter"), L.T("Tool.house-images.Name")],
        [TibiaFormat.CatalogTypeAppearances] = [L.T("Compatibility.Feature.Appearancesviewer"), L.T("Compatibility.Feature.Filtersandexports"), L.T("Compatibility.Feature.GIFexport"), L.T("Appearances.Outfitter"), L.T("Compatibility.Feature.StaticDatapreviews"), L.T("Tool.house-images.Name"), L.T("Compatibility.Feature.NPCtradelists"), L.T("Compatibility.Feature.Compareappearances")],
        [TibiaFormat.CatalogTypeStaticData] = [L.T("Tool.static-data.Name"), L.T("Compatibility.Feature.Housenames")],
        [TibiaFormat.CatalogTypeStaticMapData] = [L.T("Tool.house-images.Name")],
        [TibiaFormat.CatalogTypeMap] = [L.T("Compatibility.Feature.MapToolsdatamapimageextractionwikilists")],
        [TibiaFormat.CatalogTypeSounds] = [L.T("Compatibility.Feature.Soundslistsplaybackextraction")],
        [ResourceFileComponent] = [L.T("Compatibility.Feature.RCCEditoropeningtheclientsfiledirectly")],
        [SpritesFolderComponent] = [L.T("Compatibility.Feature.Readingspritesfromtheextractedfolderthey")],
    };

    /// <summary>Checks the client in a package folder (with an optional assets folder from elsewhere).</summary>
    public static CompatibilityReport Check(TibiaClientPaths client, string? assetsDirectoryOverride = null, LoadedData? loaded = null,
        IProgress<OperationProgress>? progress = null, CancellationToken ct = default) =>
        Check(LocationResolver.ForClient(client, assetsDirectoryOverride), loaded, progress, ct: ct);

    /// <summary>
    /// Checks the client data at the given locations. With <paramref name="only"/>, only those
    /// components are checked and reported (after a location changed; merge the result into the
    /// previous report with <see cref="CompatibilityReport.WithComponents"/>). The package is always
    /// read for the version, because the verdicts depend on it.
    /// </summary>
    public static CompatibilityReport Check(LocationResolver locations, LoadedData? loaded = null, IProgress<OperationProgress>? progress = null,
        IReadOnlySet<string>? only = null, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        loaded ??= new LoadedData();
        bool Wanted(string component) => only == null || only.Contains(component);
        var package = locations.Resolve(LocationIds.Package);
        var manifest = locations.Resolve(LocationIds.PackageManifest);
        var assetsLocation = locations.Resolve(LocationIds.Assets);
        string assets = assetsLocation.DisplayPath;
        // A real folder, or an imported version's files (7.3). A real folder is listed once for the whole
        // check (8.8.1): the catalog and map checks ask about thousands of its files.
        var assetsFolder = assetsLocation.Folder ?? Clients.PhysicalClientFolder.WithListing(assetsLocation.Path ?? assets);
        var importedSounds = locations.Resolve(LocationIds.SoundsFolder) is { IsImported: true } soundsLocation ? soundsLocation.Folder : null;
        var components = new List<ComponentReport>();
        int steps = 7 + locations.ReportedOptional.Count;
        void Step(int n, string what) => progress?.Report(new OperationProgress(n, steps, what));

        // 1. Package and version
        Step(0, L.T("CompatibilityChecker.ClientVersion"));
        string? version = manifest.Found ? TibiaClientPaths.ReadVersion(manifest.Path!) : null;
        bool validated = version != null && TibiaFormat.ValidatedClientVersions.Contains(version);
        if (Wanted(PackageComponent)) components.Add(PackageReport(package, manifest, assetsLocation, version, validated));

        // 2. Sprite catalog (also needed to validate the appearances)
        ct.ThrowIfCancellationRequested();
        Step(1, L.T("CompatibilityChecker.SpriteCatalog"));
        AssetCatalog? catalog = null;
        if (Wanted(SpriteCatalogComponent))
        {
            var (catalogReport, loadedCatalog) = CheckCatalog(assetsFolder, validated);
            components.Add(catalogReport with
            {
                LocationId = LocationIds.Assets, Path = assets,
                Details = assetsLocation.Notice != null ? [assetsLocation.Notice, .. catalogReport.Details] : catalogReport.Details,
            });
            catalog = loadedCatalog;
        }
        else if (Wanted(TibiaFormat.CatalogTypeAppearances))
        {
            catalog = TryQuietly(() => AssetCatalog.Load(assetsFolder));
        }

        // 3. Data files
        AppearanceDatabase? appearances = loaded.Appearances;
        var specs = new (string Type, MessageDescriptor Descriptor, Func<string, object> Load, Func<object, ValidationResult> Validate)[]
        {
            (TibiaFormat.CatalogTypeAppearances, Tibia.Protobuf.Appearances.Appearances.Descriptor, p => appearances = Reuse(loaded.Appearances, p) ?? AppearanceDatabase.Load(p),
                db => DataValidation.Appearances((AppearanceDatabase)db, catalog)),
            (TibiaFormat.CatalogTypeStaticData, Tibia.Protobuf.Staticdata.StaticData.Descriptor, p => loaded.StaticData ?? (object)StaticDataDatabase.Load(p),
                db => DataValidation.StaticData((StaticDataDatabase)db, appearances)),
            (TibiaFormat.CatalogTypeStaticMapData, Tibia.Protobuf.Staticmapdata.StaticMapData.Descriptor, p => loaded.StaticMap ?? (object)StaticMapDatabase.Load(p),
                db => DataValidation.StaticMap((StaticMapDatabase)db, appearances)),
            (TibiaFormat.CatalogTypeMap, Tibia.Protobuf.Map.Map.Descriptor, p => loaded.Map ?? (object)MapDatabase.Load(p),
                db => DataValidation.MapIn((MapDatabase)db, assetsFolder)),
            (TibiaFormat.CatalogTypeSounds, Tibia.Protobuf.Sound.Sounds.Descriptor, p => loaded.Sounds ?? (object)SoundDatabase.Load(p, importedSounds),
                db => DataValidation.Sounds((SoundDatabase)db)),
        };
        int step = 2;
        foreach (var spec in specs)
        {
            ct.ThrowIfCancellationRequested();
            Step(step++, spec.Type);
            var location = locations.Resolve(spec.Type);
            if (!Wanted(spec.Type))
            {
                // Static data and house maps are validated against the appearances: load them quietly when only those are checked.
                if (spec.Type == TibiaFormat.CatalogTypeAppearances && (Wanted(TibiaFormat.CatalogTypeStaticData) || Wanted(TibiaFormat.CatalogTypeStaticMapData)) && location.Found)
                    appearances ??= TryQuietly(() => Reuse(loaded.Appearances, location.Path!) ?? AppearanceDatabase.Load(location.Path!));
                continue;
            }
            var report = CheckDataFile(spec.Type, location.Path, spec.Descriptor, spec.Load, spec.Validate, validated);
            // A missing sounds folder is located as a folder; a missing data file as a file.
            string locate = spec.Type == TibiaFormat.CatalogTypeSounds && !locations.Resolve(LocationIds.SoundsFolder).Found ? LocationIds.SoundsFolder : spec.Type;
            if (location.Path == null && location.Problem != null) report = report with { Summary = location.Problem };
            var notices = new[] { spec.Type == TibiaFormat.CatalogTypeSounds ? locations.Resolve(LocationIds.SoundsFolder).Notice : null, location.Notice }.OfType<string>().ToList();
            if (notices.Count > 0) report = report with { Details = [.. notices, .. report.Details] };
            components.Add(report with { LocationId = locate, Path = location.DisplayPath });
        }

        // 4. Optional files (they never change the overall result)
        foreach (string id in locations.ReportedOptional)
        {
            ct.ThrowIfCancellationRequested();
            Step(step++, id);
            var location = locations.Resolve(id);
            var def = LocationResolver.Definition(id);
            if (def.Component == null || !Wanted(def.Component)) continue;
            components.Add(OptionalReport(def, location, validated) with { LocationId = id, Path = location.DisplayPath, Required = false });
        }

        var overall = CompatibilityReport.OverallOf(components);
        // The worst required component decides. An unvalidated client version (package component) makes the
        // result at most "unfamiliar but compatible" when every file was understood; it never blocks a feature.
        var result = new CompatibilityReport(DateTime.UtcNow, package.DisplayPath, version, validated, overall, components, watch.Elapsed);
        AppLog.Info($"Compatibility check{(only != null ? " (" + string.Join(", ", only) + ")" : "")}: client {version ?? "unknown"}, overall {CompatibilityReport.Describe(overall)} ({watch.Elapsed.TotalSeconds:0.0} s).");
        foreach (var c in components.Where(c => c.Status > CompatibilityStatus.Unfamiliar))
            AppLog.Warn($"Compatibility: {c.Component} — {CompatibilityReport.Describe(c.Status)}{(c.Required ? "" : " (optional)")}: {c.Summary}");
        return result;
    }

    private static T? TryQuietly<T>(Func<T> load) where T : class
    {
        try { return load(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or DataFileException) { return null; }
    }

    private static ComponentReport PackageReport(ResolvedLocation package, ResolvedLocation manifest, ResolvedLocation assets, string? version, bool validated)
    {
        var features = Features[PackageComponent];
        var details = new List<string>();
        if (package.Notice != null) details.Add(package.Notice);
        if (manifest.Notice != null) details.Add(manifest.Notice);
        // Without the package and without usable assets there is no client at all.
        if (!package.Found && !assets.Found)
        {
            return new ComponentReport(PackageComponent, TibiaFormat.PackageManifest, CompatibilityStatus.Missing,
                L.F("CompatibilityChecker.PackageFolderNotFound0", package.DisplayPath) + (package.Problem != null && package.Problem != L.T("LocationResolver.ThisFolderDoesNotExist") ? $" ({package.Problem})" : ""),
                details, features) { LocationId = LocationIds.Package, Path = package.DisplayPath };
        }
        if (version == null)
        {
            string why = manifest.Found ? L.T("CompatibilityChecker.PackageJsonHasNoReadableVersion") : manifest.Problem ?? L.T("CompatibilityChecker.PackageJsonIsMissing");
            return new ComponentReport(PackageComponent, TibiaFormat.PackageManifest, CompatibilityStatus.Unfamiliar,
                L.F("CompatibilityChecker.TheClientVersionCouldNotBe", why.TrimEnd('.')), details, features)
                { LocationId = LocationIds.PackageManifest, Path = manifest.DisplayPath };
        }
        return new ComponentReport(PackageComponent, TibiaFormat.PackageManifest, validated ? CompatibilityStatus.Supported : CompatibilityStatus.Unfamiliar,
            validated ? L.F("CompatibilityChecker.Client0WasValidatedWithThis", version) : L.F("CompatibilityChecker.Client0WasNotValidatedWith", version),
            details, features) { LocationId = LocationIds.PackageManifest, Path = manifest.DisplayPath };
    }

    /// <summary>Report of an optional location: the client's resource file or the extracted sprites folder.</summary>
    private static ComponentReport OptionalReport(LocationDefinition def, ResolvedLocation location, bool validated)
    {
        string component = def.Component!;
        var features = Features[component];
        var details = new List<string>();
        if (location.Notice != null) details.Add(location.Notice);
        string name = location.Path != null ? System.IO.Path.GetFileName(location.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar)) : System.IO.Path.GetFileName(location.ExpectedPath);
        if (!location.Found)
            return new ComponentReport(component, name, CompatibilityStatus.Missing, (location.Problem ?? L.T("CompatibilityChecker.NotFound")) + L.T("CompatibilityChecker.ThisIsOptional"), details, features);
        try
        {
            switch (def.Id)
            {
                case LocationIds.ResourceFile:
                {
                    var check = Rcc.RccValidator.Validate(ProtoLoader.ReadShared(location.Path!));
                    if (!check.IsValid) return new ComponentReport(component, name, CompatibilityStatus.Unsupported, L.T("CompatibilityChecker.TheResourceFileCannotBeRead") + check.Errors[0], details, features);
                    return new ComponentReport(component, name, validated ? CompatibilityStatus.Supported : CompatibilityStatus.Unfamiliar,
                        L.F("CompatibilityChecker.Format01N0Files", check.FormatVersion, check.Files), details, features);
                }
                case LocationIds.SpritesFolder:
                {
                    int count = Directory.EnumerateFiles(location.Path!, "*.png").Take(1_000_000).Count();
                    return new ComponentReport(component, name, count > 0 ? CompatibilityStatus.Supported : CompatibilityStatus.Partial,
                        count > 0 ? L.F("CompatibilityChecker.0N0SpriteImages", count) : L.T("CompatibilityChecker.TheFolderContainsNoSpriteImages"), details, features);
                }
                default:
                    return new ComponentReport(component, name, CompatibilityStatus.Supported, "Found.", details, features);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new ComponentReport(component, name, CompatibilityStatus.Unsupported, L.T("CompatibilityChecker.ItCannotBeRead") + ex.Message, details, features);
        }
    }

    private static T? Reuse<T>(T? db, string path) where T : class => db is AppearanceDatabase a && a.SourcePath != null &&
        string.Equals(Path.GetFullPath(a.SourcePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) ? db : null;

    private static (ComponentReport Report, AssetCatalog? Catalog) CheckCatalog(Clients.IClientFolder assets, bool validatedVersion)
    {
        var features = Features[SpriteCatalogComponent];
        if (assets.PathOf(TibiaFormat.AssetCatalogFile) == null)
            return (new ComponentReport(SpriteCatalogComponent, TibiaFormat.AssetCatalogFile, CompatibilityStatus.Missing, L.F("CompatibilityChecker.NotFoundIn0", assets.DisplayPath), Array.Empty<string>(), features), null);
        AssetCatalog catalog;
        try
        {
            catalog = AssetCatalog.Load(assets);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return (new ComponentReport(SpriteCatalogComponent, TibiaFormat.AssetCatalogFile, CompatibilityStatus.Unsupported, ex.Message, Array.Empty<string>(), features), null);
        }

        var details = new List<string>(catalog.Problems.Take(20));
        if (catalog.Problems.Count > 20) details.Add(L.F("CompatibilityChecker.And0MoreCatalogProblems", catalog.Problems.Count - 20));
        int missing = catalog.Sheets.Count(s => !SheetExists(catalog, s));
        if (missing > 0) details.Add(L.F("CompatibilityChecker.0ListedInTheCatalogAre", L.Count(missing, "Noun.sprite_sheet_file")));
        foreach (var (type, count) in catalog.UnusedTypes) details.Add(L.F("CompatibilityChecker.NotUsedByThisVersion0", type, L.Count(count, "Noun.file")));

        // Decode one sheet of every type: proves the container, image format and grid are still understood.
        var decodeErrors = new List<string>();
        foreach (var sheet in catalog.Sheets.Where(s => SheetExists(catalog, s)).GroupBy(s => s.Type).Select(g => g.First()))
        {
            try
            {
                var image = SpriteSheetDecoder.DecodeSheet(catalog.GetSheetPath(sheet));
                SpriteSheetDecoder.Slice(image, sheet);
                if (image.Width != TibiaFormat.SheetSize || image.Height != TibiaFormat.SheetSize)
                    details.Add(L.F("CompatibilityChecker.SheetsAre01InsteadOf", image.Width, image.Height, TibiaFormat.SheetSize, TibiaFormat.SheetSize, TibiaFormat.SheetSize));
            }
            catch (InvalidDataException ex)
            {
                decodeErrors.Add(ex.Message);
            }
        }
        details.AddRange(decodeErrors);

        CompatibilityStatus status;
        string summary = L.F("CompatibilityChecker.0N0Sheets1N0Sprites", catalog.SheetCount, catalog.TotalSprites, catalog.HighestSpriteId);
        if (catalog.SheetCount == 0 || decodeErrors.Count > 0) { status = CompatibilityStatus.Unsupported; summary = L.T("CompatibilityChecker.SpriteSheetsCouldNotBeDecoded") + summary; }
        else if (catalog.Problems.Count > 0 || missing > 0) status = CompatibilityStatus.Partial;
        else status = validatedVersion && catalog.UnusedTypes.Count == 0 ? CompatibilityStatus.Supported : CompatibilityStatus.Unfamiliar;
        // Unused asset types are expected in the validated client (e.g. "fullmap", "proficiencies"): only mark unfamiliar for unknown versions.
        if (status == CompatibilityStatus.Unfamiliar && validatedVersion) status = CompatibilityStatus.Supported;
        return (new ComponentReport(SpriteCatalogComponent, TibiaFormat.AssetCatalogFile, status, summary, details, features), catalog);
    }

    /// <summary>Whether a sheet's file is there (a real folder answers from its listing, see <see cref="Clients.PhysicalClientFolder.WithListing"/>).</summary>
    private static bool SheetExists(AssetCatalog catalog, SpriteSheetInfo sheet) =>
        catalog.Folder is Clients.PhysicalClientFolder physical ? physical.Exists(sheet.FileName) : File.Exists(catalog.GetSheetPath(sheet));

    private static ComponentReport CheckDataFile(string type, string? path, MessageDescriptor descriptor, Func<string, object> load,
        Func<object, ValidationResult> validate, bool validatedVersion)
    {
        var features = Features[type];
        if (path == null)
            return new ComponentReport(type, null, CompatibilityStatus.Missing, L.F("CompatibilityChecker.No0FileWasFound", type), Array.Empty<string>(), features);
        string name = Path.GetFileName(path);
        var details = new List<string>();

        // Integrity: data files are named "<type>-<sha256 of content>.dat".
        byte[] bytes;
        try
        {
            bytes = ProtoLoader.ReadShared(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ComponentReport(type, name, CompatibilityStatus.Unsupported, L.T("CompatibilityChecker.TheFileCouldNotBeRead") + ex.Message, details, features);
        }
        if (ContentHashFromName(name) is { } expectedHash)
        {
            string actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (actual != expectedHash)
                return new ComponentReport(type, name, CompatibilityStatus.Unsupported,
                    L.T("CompatibilityChecker.TheFileIsDamagedOrIncomplete"), details, features);
            details.Add(L.T("CompatibilityChecker.IntegrityContentMatchesTheSHA256"));
        }

        // Schema drift: fields this build does not know (new client information) or with changed types.
        var schema = SchemaInspector.Inspect(descriptor, bytes);
        foreach (var drift in schema.Drift.Take(15)) details.Add(drift.ToString());
        if (schema.Drift.Count > 15) details.Add(L.F("CompatibilityChecker.And0MoreSchemaDifferences", schema.Drift.Count - 15));

        object db;
        try
        {
            db = load(path);
        }
        catch (DataFileException ex)
        {
            return new ComponentReport(type, name, ex.Kind == DataProblemKind.Missing ? CompatibilityStatus.Missing : CompatibilityStatus.Unsupported,
                ex.Message + " " + ex.Advice, details, features);
        }

        var validation = validate(db);
        details.AddRange(validation.Describe().Take(20));
        string summary = L.F("CompatibilityChecker.0N0RecordsChecked", validation.Checked);
        CompatibilityStatus status;
        if (validation.Fatal != null) { status = CompatibilityStatus.Unsupported; summary = L.F("CompatibilityChecker.RequiredContentMissing0", validation.Fatal); }
        else if (schema.HasMismatches || schema.Truncated || validation.IssueTotal > 0)
        {
            status = CompatibilityStatus.Partial;
            summary += $"; {L.Count(validation.IssueTotal, "Noun.issue")}{(schema.HasMismatches ? ", fields with changed types" : "")}.";
        }
        else if (schema.HasUnknownFields)
        {
            status = CompatibilityStatus.Unfamiliar;
            summary += L.T("CompatibilityChecker.TheFileContainsInformationThisVersion");
        }
        else
        {
            status = validatedVersion ? CompatibilityStatus.Supported : CompatibilityStatus.Unfamiliar;
            summary += L.T("CompatibilityChecker.NoProblemsFound");
        }
        return new ComponentReport(type, name, status, summary, details, features);
    }

    /// <summary>The 64-hex-digit content hash embedded in a client file name ("map-&lt;hash&gt;.dat"), or null.</summary>
    public static string? ContentHashFromName(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        int dash = stem.LastIndexOf('-');
        if (dash < 0) return null;
        string hash = stem[(dash + 1)..].ToLowerInvariant();
        return hash.Length == TibiaFormat.ContentHashLength && hash.All(Uri.IsHexDigit) ? hash : null;
    }

    /// <summary>
    /// Checks a client file against the SHA-256 in its name: of the decompressed data for
    /// "*.lzma" files, of the file itself otherwise (true for every hash-named file of clients
    /// 15.25 and 15.33). Null when the name carries no hash. A damaged LZMA file throws
    /// <see cref="InvalidDataException"/>.
    /// </summary>
    public static bool? MatchesHashInName(string path, string? fileName = null)
    {
        fileName ??= Path.GetFileName(path);
        bool compressed = fileName.EndsWith(".lzma", StringComparison.OrdinalIgnoreCase);
        if (ContentHashFromName(compressed ? fileName[..^5] : fileName) is not { } expected) return null;
        byte[] hash;
        if (compressed)
        {
            hash = SHA256.HashData(CipLzma.DecompressFile(path));
        }
        else
        {
            using var file = File.OpenRead(path);
            hash = SHA256.HashData(file);
        }
        return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
