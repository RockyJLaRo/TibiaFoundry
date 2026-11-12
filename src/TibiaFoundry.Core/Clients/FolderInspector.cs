using System.Text.Json;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Clients;

/// <summary>What a folder holds, as far as Tibia Foundry can tell without hashing it (8.0).</summary>
public enum FolderClass
{
    /// <summary>package.json and assets.json are readable and every file they list is there.</summary>
    CompleteInstallation,
    /// <summary>The asset catalog and data files are there, but not everything a complete installation has.</summary>
    StandaloneClient,
    /// <summary>The folder is an assets folder (catalog-content.json at its root) or holds only assets (and sounds).</summary>
    AssetsOnly,
    /// <summary>Some client files are there, but not enough for any tool.</summary>
    Insufficient,
    /// <summary>Nothing of a Tibia client.</summary>
    NotAClient,
}

/// <summary>Whether one tool (or operation) can work with a source, and what it misses.</summary>
public sealed record ToolCapability(string Id, string Name, bool Available, IReadOnlyList<string> Missing);

/// <summary>The result of inspecting a folder.</summary>
public sealed class FolderInspection
{
    /// <summary>The folder that was picked.</summary>
    public required string Path { get; init; }
    /// <summary>The folder the result is about: the picked folder, or for a launcher or parent folder null (see <see cref="Candidates"/>).</summary>
    public string? Root { get; init; }
    public required FolderClass Class { get; init; }
    public required VersionReport Version { get; init; }
    public required IReadOnlyList<ToolCapability> Capabilities { get; init; }
    /// <summary>Plain-language findings: what is there, what is missing, what to pick instead.</summary>
    public required IReadOnlyList<string> Notes { get; init; }
    /// <summary>Client folders found inside the picked folder (a launcher folder, a folder of copies), or its package when an assets folder was picked.</summary>
    public IReadOnlyList<string> Candidates { get; init; } = [];
    /// <summary>"Test_Server" when the folder is named like the test-server package, else "Live_Server" (only a default: the user decides).</summary>
    public string SuggestedServer { get; init; } = ClientServers.Live;
    /// <summary>Files the client's own lists name that are not there (complete installations have none).</summary>
    public int ListedMissing { get; init; }

    public bool IsUsable => (Class is FolderClass.CompleteInstallation or FolderClass.StandaloneClient or FolderClass.AssetsOnly) && Capabilities.Any(c => c.Available && c.Id != ClientCapabilities.ArchiveFolder);
    public string ClassDisplay => FolderInspector.Display(Class);
    public IEnumerable<ToolCapability> Available => Capabilities.Where(c => c.Available);
}

/// <summary>Tool and operation IDs of <see cref="ToolCapability"/> (the App maps them to its pages).</summary>
public static class ClientCapabilities
{
    public const string Appearances = "appearances";
    public const string StaticData = "static-data";
    public const string ItemCatalog = "item-catalog";
    public const string MapTools = "map-tools";
    public const string HouseImages = "house-images";
    public const string Sounds = "sounds";
    public const string SpriteExtractor = "sprite-extractor";
    public const string Compare = "compare";
    public const string AssetsEditor = "assets-editor";
    public const string ClientConverter = "client-converter";
    public const string RccEditor = "rcc-editor";
    public const string Compatibility = "compatibility";
    public const string ArchiveInstallation = "archive-installation";
    public const string ArchiveFolder = "archive-folder";
}

/// <summary>
/// Inspects a folder someone picked (8.0): which kind of client folder it is, its version evidence,
/// and per tool whether it can be used and what is missing. Only reads, and never hashes the whole
/// folder, so it is quick enough for a folder picker. Nothing is invented, substituted or downloaded.
/// </summary>
public static class FolderInspector
{
    public static string Display(FolderClass c) => c switch
    {
        FolderClass.CompleteInstallation => L.T("FolderInspector.CompleteInstallation"),
        FolderClass.StandaloneClient => L.T("FolderInspector.StandaloneClientFolder"),
        FolderClass.AssetsOnly => L.T("FolderInspector.AssetsFolder"),
        FolderClass.Insufficient => L.T("FolderInspector.NotEnoughClientFiles"),
        _ => L.T("FolderInspector.NotATibiaClientFolder"),
    };

    public static FolderInspection Inspect(string picked, IReadOnlyDictionary<string, string>? knownContent = null)
    {
        string path = PathGuard.Normalize(picked);
        var notes = new List<string>();
        if (!Directory.Exists(path))
            return new FolderInspection { Path = path, Class = FolderClass.NotAClient, Version = VersionReport.None, Capabilities = [], Notes = [L.T("FolderInspector.ThisFolderDoesNotExistOr")] };

        bool isAssetsFolder = File.Exists(System.IO.Path.Combine(path, TibiaFormat.AssetCatalogFile));
        bool isPackage = File.Exists(System.IO.Path.Combine(path, TibiaFormat.PackageManifest)) || File.Exists(System.IO.Path.Combine(path, TibiaFormat.AssetsFolder, TibiaFormat.AssetCatalogFile));
        string server = System.IO.Path.GetFileName(path).Equals(TibiaFormat.DefaultTestPackage, StringComparison.OrdinalIgnoreCase) ? ClientServers.Test : ClientServers.Live;

        if (!isAssetsFolder && !isPackage)
        {
            // A launcher folder, a folder of copies, or something else: look two levels down.
            var found = FindClientFolders(path, depth: 2);
            if (Directory.Exists(System.IO.Path.Combine(path, "packages")))
                notes.Add(L.T("FolderInspector.ThisLooksLikeTheTibiaLauncher"));
            else if (found.Count > 0)
                notes.Add(L.F("FolderInspector.ThisFolderHolds0ChooseOne", L.Count(found.Count, "Noun.client_folder")));
            bool someClientFile = found.Count == 0 && Directory.EnumerateFiles(path, "*.dat").Any(f => new[] { AssetTypes.Appearances, AssetTypes.StaticData, AssetTypes.StaticMapData, AssetTypes.Map }.Any(t => System.IO.Path.GetFileName(f).StartsWith(t + "-", StringComparison.OrdinalIgnoreCase)));
            if (someClientFile) notes.Add(L.T("FolderInspector.ItHasClientDataFilesBut"));
            else if (found.Count == 0) notes.Add(L.T("FolderInspector.NoPackageJsonAssetsCatalogContent"));
            return new FolderInspection
            {
                Path = path, Root = null, Class = someClientFile ? FolderClass.Insufficient : FolderClass.NotAClient, Version = VersionReport.None,
                Capabilities = [], Notes = notes, Candidates = found, SuggestedServer = server,
            };
        }

        string root = path;
        string assets = isAssetsFolder && !isPackage ? path : System.IO.Path.Combine(path, TibiaFormat.AssetsFolder);
        string sounds = isAssetsFolder && !isPackage ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? path, TibiaFormat.SoundsFolder) : System.IO.Path.Combine(path, TibiaFormat.SoundsFolder);
        var candidates = new List<string>();
        if (isAssetsFolder && !isPackage && System.IO.Path.GetDirectoryName(path) is { } parent && File.Exists(System.IO.Path.Combine(parent, TibiaFormat.PackageManifest)))
        {
            candidates.Add(parent);
            notes.Add(L.F("FolderInspector.ThisIsTheAssetsFolderOf", parent));
        }

        // Completeness: every file the client's own lists name must be there with the listed size (hashes are checked when archiving).
        int listed = 0, missing = 0;
        bool hasLists = File.Exists(System.IO.Path.Combine(root, TibiaFormat.PackageManifest)) && File.Exists(System.IO.Path.Combine(root, ClientScanner.AssetsJson));
        if (isPackage && hasLists)
        {
            var lists = new List<string>();
            foreach (var (file, origin) in new[] { (TibiaFormat.PackageManifest, "package.json"), (ClientScanner.AssetsJson, "assets.json") })
            {
                var entries = ClientScanner.ReadFileList(System.IO.Path.Combine(root, file), origin, lists, quiet: true);
                if (entries == null) { hasLists = false; continue; }
                var sizes = Sizes(root);
                foreach (var e in entries)
                {
                    listed++;
                    if (!sizes.TryGetValue(e.Path, out long length) || (e.ExpectedSize is { } size && size != length)) missing++;
                }
            }
        }

        var capabilities = Capabilities(isPackage ? root : null, assets, sounds, out var catalogProblem);
        if (catalogProblem != null) notes.Add(catalogProblem);
        FolderClass cls;
        if (isPackage && hasLists && missing == 0 && listed > 0) cls = FolderClass.CompleteInstallation;
        else if (isAssetsFolder && !isPackage) cls = FolderClass.AssetsOnly;
        else if (capabilities.Any(c => c.Available && c.Id is not (ClientCapabilities.ArchiveFolder or ClientCapabilities.Compatibility)))
            cls = Directory.EnumerateFileSystemEntries(root).All(e => System.IO.Path.GetFileName(e) is TibiaFormat.AssetsFolder or TibiaFormat.SoundsFolder) ? FolderClass.AssetsOnly : FolderClass.StandaloneClient;
        else cls = FolderClass.Insufficient;

        if (cls == FolderClass.CompleteInstallation) notes.Add(L.F("FolderInspector.All0N0FilesTheClient", listed));
        else if (isPackage && hasLists) notes.Add(L.F("FolderInspector.0N0OfThe1N0", missing, listed));
        else if (isPackage) notes.Add(L.T("FolderInspector.TheClientSOwnFileLists"));

        var allCapabilities = capabilities.Append(cls == FolderClass.CompleteInstallation
            ? new ToolCapability(ClientCapabilities.ArchiveInstallation, L.T("FolderInspector.ArchiveAsACompleteInstallation"), true, [])
            : new ToolCapability(ClientCapabilities.ArchiveInstallation, L.T("FolderInspector.ArchiveAsACompleteInstallation"), false, [L.T("FolderInspector.ACompleteInstallationPackageJsonAssets")])).ToList();
        return new FolderInspection
        {
            Path = path, Root = root, Class = cls, Version = VersionEvidence.Collect(root, knownContent), Capabilities = allCapabilities, Notes = notes,
            Candidates = candidates, SuggestedServer = server, ListedMissing = missing,
        };
    }

    /// <summary>Size of every file below a folder ('/' separated paths), outside personal folders, from one listing (checking each file on its own is much slower).</summary>
    private static Dictionary<string, long> Sizes(string root)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var pending = new Stack<(DirectoryInfo Dir, string Rel)>();
        pending.Push((new DirectoryInfo(root), ""));
        while (pending.Count > 0)
        {
            var (dir, rel) = pending.Pop();
            try
            {
                foreach (var entry in dir.EnumerateFileSystemInfos("*", options))
                {
                    string path = rel.Length == 0 ? entry.Name : rel + "/" + entry.Name;
                    if (entry is FileInfo file) result[path] = file.Length;
                    else if (!(rel.Length == 0 && ClientScanner.PersonalFolders.Contains(entry.Name) && !entry.Name.Equals("conf", StringComparison.OrdinalIgnoreCase)))
                        pending.Push(((DirectoryInfo)entry, path));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not listable: its files count as missing.
            }
        }
        return result;
    }

    /// <summary>Package-like or assets folders below <paramref name="folder"/> (up to <paramref name="depth"/> levels), sorted.</summary>
    public static IReadOnlyList<string> FindClientFolders(string folder, int depth)
    {
        var result = new List<string>();
        var pending = new Stack<(string Dir, int Level)>();
        pending.Push((folder, 0));
        while (pending.Count > 0)
        {
            var (dir, level) = pending.Pop();
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(dir).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (string sub in subs)
            {
                if (ClientScanner.PersonalFolders.Contains(System.IO.Path.GetFileName(sub))) continue;
                try { if (PathGuard.IsReparsePoint(new DirectoryInfo(sub))) continue; } catch (IOException) { continue; }
                if (File.Exists(System.IO.Path.Combine(sub, TibiaFormat.PackageManifest)) || File.Exists(System.IO.Path.Combine(sub, TibiaFormat.AssetsFolder, TibiaFormat.AssetCatalogFile))
                    || File.Exists(System.IO.Path.Combine(sub, TibiaFormat.AssetCatalogFile)))
                    result.Add(sub);
                else if (level + 1 < depth) pending.Push((sub, level + 1));
            }
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>Per tool, whether the files it needs are there (from the tools' actual inputs).</summary>
    private static List<ToolCapability> Capabilities(string? package, string assets, string sounds, out string? catalogProblem)
    {
        catalogProblem = null;
        var result = new List<ToolCapability>();
        bool catalogFile = File.Exists(System.IO.Path.Combine(assets, TibiaFormat.AssetCatalogFile));
        int sheetsMissing = 0;
        bool sheetsKnown = false;
        if (catalogFile)
        {
            try
            {
                var catalog = AssetCatalog.Load(assets);
                sheetsKnown = catalog.SheetCount > 0;
                var present = Directory.EnumerateFiles(assets).Select(System.IO.Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                sheetsMissing = catalog.Sheets.Select(s => s.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count(n => !present.Contains(n));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
            {
                catalogProblem = L.T("FolderInspector.CatalogContentJsonCannotBeRead") + ex.Message;
            }
        }
        string? DataFile(string type) => catalogFile || Directory.Exists(assets) ? TibiaClientPaths.FindAssetFile(assets, type) : null;
        string? appearances = DataFile(AssetTypes.Appearances), staticData = DataFile(AssetTypes.StaticData), staticMap = DataFile(AssetTypes.StaticMapData), map = DataFile(AssetTypes.Map);
        string? soundsFile = Directory.Exists(sounds) ? TibiaClientPaths.FindSoundsFile(sounds) : null;

        var catalogNeed = new List<string>();
        if (!catalogFile) catalogNeed.Add(@"assets\catalog-content.json");
        else if (!sheetsKnown) catalogNeed.Add(L.T("FolderInspector.SpriteSheetsListedInCatalogContent"));
        else if (sheetsMissing > 0) catalogNeed.Add(L.F("FolderInspector.0NamedByCatalogContentJson", L.Count(sheetsMissing, "Noun.sprite_sheet_file")));
        List<string> Need(params (bool Ok, string What)[] items) => items.Where(i => !i.Ok).Select(i => i.What).ToList();

        void Add(string id, string name, List<string> missing) => result.Add(new ToolCapability(id, name, missing.Count == 0, missing));
        Add(ClientCapabilities.Appearances, "Appearances", [.. catalogNeed, .. Need((appearances != null, L.T("FolderInspector.TheAppearancesDataFileAppearancesDat")))]);
        Add(ClientCapabilities.SpriteExtractor, L.T("FolderInspector.ExtractSprites"), [.. catalogNeed]);
        Add(ClientCapabilities.StaticData, L.T("FolderInspector.StaticData"), Need((staticData != null, L.T("FolderInspector.TheStaticDataFileStaticdataDat"))));
        Add(ClientCapabilities.ItemCatalog, L.T("FolderInspector.ItemCatalogImportFromTheClient"), Need((appearances != null, L.T("FolderInspector.TheAppearancesDataFileAppearancesDat"))));
        Add(ClientCapabilities.MapTools, L.T("FolderInspector.MapTools"), Need((map != null, L.T("FolderInspector.TheMapDataFileMapDat")), (staticMap != null, L.T("FolderInspector.TheStaticMapDataFileStaticmapdata"))));
        Add(ClientCapabilities.HouseImages, L.T("FolderInspector.HouseImages"), Need((staticMap != null, L.T("FolderInspector.TheStaticMapDataFileStaticmapdata"))));
        Add(ClientCapabilities.Sounds, "Sounds", Need((soundsFile != null, L.T("FolderInspector.SoundsCatalogSoundJsonOrA"))));
        Add(ClientCapabilities.Compare, L.T("FolderInspector.CompareFiles"), Need((appearances != null || staticData != null || staticMap != null || map != null, L.T("FolderInspector.AClientDataFileAppearancesStatic"))));
        var editorNeed = new List<string>([.. catalogNeed, .. Need((appearances != null, L.T("FolderInspector.TheAppearancesDataFileAppearancesDat")))]);
        Add(ClientCapabilities.AssetsEditor, L.T("FolderInspector.AssetsEditor"), editorNeed);
        Add(ClientCapabilities.ClientConverter, L.T("FolderInspector.ClientConverter"), [.. editorNeed]);
        // The client's own list first (quick); the header scan only when it names no resource file that is there.
        bool rcc = package != null &&
                   ((ClientScanner.ReadFileList(System.IO.Path.Combine(package, TibiaFormat.PackageManifest), "package.json", [], quiet: true) ?? [])
                        .Any(f => f.Path.EndsWith(".rcc", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(System.IO.Path.Combine(package, f.Path.Replace('/', System.IO.Path.DirectorySeparatorChar))))
                    || ClientScanner.HasResourceFile(package));
        Add(ClientCapabilities.RccEditor, L.T("FolderInspector.RCCEditor"), Need((rcc, L.T("FolderInspector.AQtResourceFileRccFor"))));
        Add(ClientCapabilities.Compatibility, L.T("FolderInspector.CompatibilityCheck"), []);
        Add(ClientCapabilities.ArchiveFolder, L.T("FolderInspector.ArchiveThisFolderAsItIs"), []);
        return result;
    }
}
