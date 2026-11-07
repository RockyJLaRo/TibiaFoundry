using System.Collections.Concurrent;
using System.Text.Json;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Clients;

/// <summary>A file a client version consists of, as found in the installation (before it is hashed).</summary>
/// <param name="Path">Relative to the package folder, '/' separated (for example "assets/catalog-content.json").</param>
/// <param name="ExpectedSha256">The client's own hash of the file (assets.json, package.json), when it gives one.</param>
/// <param name="Origin">Where the file was found: "assets.json", "package.json", "header scan", "catalog", "version file".</param>
/// <param name="Optional">A missing optional file is skipped instead of stopping the import.</param>
public sealed record PlannedFile(string Path, string Kind, string? ExpectedSha256, long? ExpectedSize, string Origin, bool Optional = false);

/// <summary>The files of a client installation that make up a version, found from the installation itself.</summary>
public sealed class ClientFileSet
{
    public required string PackageFolder { get; init; }
    public string? ClientVersion { get; init; }
    /// <summary>"assets.json" (the client's own list) or "fallback" (catalog-content.json, catalog-sound.json and map data).</summary>
    public required string FileList { get; init; }
    public string? AssetsJsonSha256 { get; init; }
    public required IReadOnlyList<PlannedFile> Files { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }

    public string SourcePath(PlannedFile file) => System.IO.Path.Combine(PackageFolder, file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
}

/// <summary>A hashed installation: the candidate manifest of its current content.</summary>
public sealed class ClientScan
{
    public required ClientFileSet FileSet { get; init; }
    /// <summary>The files found, sorted by path, with the hashes computed from their bytes.</summary>
    public required IReadOnlyList<ManifestFile> Files { get; init; }
    /// <summary>Why the installation cannot be imported as it is (missing, unreadable or damaged files). Empty when it can.</summary>
    public required IReadOnlyList<string> Problems { get; init; }
    public required string Fingerprint { get; init; }
    public required SourceSnapshot Snapshot { get; init; }
    public TimeSpan Duration { get; init; }

    public bool CanImport => Problems.Count == 0 && Files.Count > 0;
    public long TotalBytes => Files.Sum(f => f.Size);
    public string? ClientVersion => FileSet.ClientVersion;
    public string SourcePath(ManifestFile file) => Path.Combine(FileSet.PackageFolder, file.Path.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>
/// Finds and hashes the files of a client installation. Only reads: files are opened for shared
/// reading and nothing in the installation is ever written.
/// </summary>
/// <remarks>
/// The file set is taken from the installation: every file the client lists in its own assets.json
/// (assets and sounds), the Qt resource files (.rcc) the client lists in package.json plus any file
/// with the Qt resource header found in the package, and the small version files. Personal folders
/// (hotkeys, character data, the explored minimap, screenshots, logs, caches) and the client program
/// itself are never included.
/// </remarks>
public static class ClientScanner
{
    public const string AssetsJson = "assets.json";
    public const string AssetsJsonHash = "assets.json.sha256";
    public const string VersionFile = "package.json.version";

    /// <summary>Top-level folders of a package that hold personal data or caches: never read for a version.</summary>
    public static IReadOnlySet<string> PersonalFolders { get; } =
        new HashSet<string>(["conf", "characterdata", "minimap", "screenshots", "log", "cache", "crashdump", "storeimages", "npc_pics"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a path in the package may belong to a version: never personal data or a backup; in the
    /// program folder (bin) only Qt resource files.
    /// </summary>
    public static bool IsManaged(string path, string kind)
    {
        string first = path.Split('/')[0];
        if (PersonalFolders.Contains(first)) return false;
        if (path.EndsWith("-bak", StringComparison.OrdinalIgnoreCase)) return false;
        if (first.Equals("bin", StringComparison.OrdinalIgnoreCase)) return kind == ClientFileKinds.Rcc;
        return true;
    }

    // ------------------------------------------------------------------ discovery

    public static ClientFileSet Discover(string packageFolder)
    {
        packageFolder = PathGuard.Normalize(packageFolder);
        if (!Directory.Exists(packageFolder)) throw new DirectoryNotFoundException($"The client folder {packageFolder} does not exist.");
        var notes = new List<string>();
        var files = new Dictionary<string, PlannedFile>(StringComparer.OrdinalIgnoreCase);
        void Add(PlannedFile f)
        {
            if (IsManaged(f.Path, f.Kind)) files.TryAdd(f.Path, f);
        }

        string? version = TibiaClientPaths.ReadVersion(Path.Combine(packageFolder, TibiaFormat.PackageManifest));
        string? assetsJsonSha = ReadHashFile(Path.Combine(packageFolder, AssetsJsonHash));
        string fileList;
        var listed = ReadFileList(Path.Combine(packageFolder, AssetsJson), "assets.json", notes);
        if (listed != null)
        {
            fileList = AssetsJson;
            foreach (var f in listed) Add(f);
        }
        else
        {
            fileList = "fallback";
            notes.Add(L.T("ClientScanner.TheClientHasNoReadableAssets"));
            foreach (var f in FallbackFiles(packageFolder, notes)) Add(f);
        }

        // Version files (small, optional; assets.json is checked against its own .sha256 file).
        Add(new PlannedFile(TibiaFormat.PackageManifest, ClientFileKinds.Meta, null, null, L.T("ClientScanner.VersionFile"), Optional: true));
        Add(new PlannedFile(VersionFile, ClientFileKinds.Meta, null, null, L.T("ClientScanner.VersionFile"), Optional: true));
        Add(new PlannedFile(AssetsJson, ClientFileKinds.Meta, assetsJsonSha, null, L.T("ClientScanner.VersionFile"), Optional: true));
        Add(new PlannedFile(AssetsJsonHash, ClientFileKinds.Meta, null, null, L.T("ClientScanner.VersionFile"), Optional: true));

        // Qt resource files: the client's own list, then the header scan for any it does not list.
        var fromPackage = ReadFileList(Path.Combine(packageFolder, TibiaFormat.PackageManifest), "package.json", notes, quiet: true)?
            .Where(f => f.Path.EndsWith(".rcc", StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        foreach (var f in fromPackage) Add(f with { Kind = ClientFileKinds.Rcc, Optional = true });
        foreach (string rcc in FindResourceFiles(packageFolder))
            if (!files.ContainsKey(rcc))
            {
                Add(new PlannedFile(rcc, ClientFileKinds.Rcc, null, null, L.T("ClientScanner.HeaderScan"), Optional: true));
                notes.Add(L.F("ClientScanner.0AQtResourceFileThe", rcc));
            }

        return new ClientFileSet
        {
            PackageFolder = packageFolder,
            ClientVersion = version,
            FileList = fileList,
            AssetsJsonSha256 = assetsJsonSha,
            Files = files.Values.OrderBy(f => f.Path, StringComparer.Ordinal).ToList(),
            Notes = notes,
        };
    }

    private static string? ReadHashFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path).Trim().ToLowerInvariant();
            return ContentStore.IsHash(text) ? text : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The "files" list of assets.json or package.json: localfile, and the hash and size of the file as
    /// it is on disk (packedhash when the client keeps it packed, "unpack": false; otherwise unpackedhash).
    /// Null when the file is missing or not in this shape.
    /// </summary>
    internal static List<PlannedFile>? ReadFileList(string path, string origin, List<string> notes, bool quiet = false)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = StoreFiles.OpenSource(path);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("files", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                if (!quiet) notes.Add(L.F("ClientScanner.0HasAnUnexpectedStructureNo", origin));
                return null;
            }
            var result = new List<PlannedFile>();
            int skipped = 0;
            foreach (var e in list.EnumerateArray())
            {
                string? local = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("localfile", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
                string? safe = local == null ? null : PathGuard.SafeRelativePath(local)?.Replace('\\', '/');
                if (safe == null) { skipped++; continue; }
                bool packed = e.TryGetProperty("unpack", out var u) && u.ValueKind == JsonValueKind.False;
                string? hash = Text(e, packed ? "packedhash" : "unpackedhash")?.ToLowerInvariant();
                long? size = Number(e, packed ? "packedsize" : "unpackedsize");
                result.Add(new PlannedFile(safe, ClientFileKinds.Of(safe), ContentStore.IsHash(hash) ? hash : null, size, origin));
            }
            if (skipped > 0 && !quiet) notes.Add(L.F("ClientScanner.01N0EntriesWithoutA", origin, skipped));
            return result;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            if (!quiet) notes.Add(L.F("ClientScanner.0CannotBeRead1", origin, ex.Message));
            return null;
        }

        static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static long? Number(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : null;
    }

    /// <summary>Without assets.json: the catalogs, every file they name, and the map tiles named by the map data.</summary>
    private static IEnumerable<PlannedFile> FallbackFiles(string package, List<string> notes)
    {
        string assets = Path.Combine(package, TibiaFormat.AssetsFolder);
        string sounds = Path.Combine(package, TibiaFormat.SoundsFolder);
        var result = new List<PlannedFile>();
        foreach (var (folder, catalogName, prefix) in new[] { (assets, TibiaFormat.AssetCatalogFile, "assets/"), (sounds, TibiaFormat.SoundCatalogFile, "sounds/") })
        {
            string catalog = Path.Combine(folder, catalogName);
            if (!File.Exists(catalog)) continue;
            result.Add(new PlannedFile(prefix + catalogName, ClientFileKinds.Asset, null, null, "catalog"));
            try
            {
                using var stream = StoreFiles.OpenSource(catalog);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                foreach (var e in doc.RootElement.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.String && TibiaFormat.SafeFileName(f.GetString()) is { } name)
                        result.Add(new PlannedFile(prefix + name, ClientFileKinds.Of(prefix + name), null, null, "catalog"));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                notes.Add(L.F("ClientScanner.0CannotBeRead1", catalogName, ex.Message));
            }
        }
        if (TibiaClientPaths.FindAssetFile(assets, AssetTypes.Map) is { } map)
        {
            try
            {
                foreach (var tile in Map.MapDatabase.Load(map).Files)
                    if (TibiaFormat.SafeFileName(tile.FileName) is { } name)
                        result.Add(new PlannedFile("assets/" + name, ClientFileKinds.Asset, null, null, L.T("ClientScanner.MapData"), Optional: true));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or Google.Protobuf.InvalidProtocolBufferException)
            {
                notes.Add(L.T("ClientScanner.TheMapDataCannotBeRead") + ex.Message);
            }
        }
        return result;
    }

    /// <summary>Package paths of files with the Qt resource header ("qres"), outside the asset and personal folders.</summary>
    public static IReadOnlyList<string> FindResourceFiles(string package)
    {
        var result = ResourceFiles(package).ToList();
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>True when the package has at least one Qt resource file (see <see cref="FindResourceFiles"/>); stops at the first one (8.8.1).</summary>
    public static bool HasResourceFile(string package) => ResourceFiles(package).Any();

    /// <remarks>
    /// 8.8.1: the header is read without a buffer. A 64 KB buffered, sequential-scan stream read 64 KB of
    /// every file to look at 4 bytes; in a launcher folder with thousands of screenshots below it that was
    /// gigabytes of reads and allocations and most of the time of the setup's installation search.
    /// </remarks>
    private static IEnumerable<string> ResourceFiles(string package)
    {
        var skipTop = new HashSet<string>(PersonalFolders, StringComparer.OrdinalIgnoreCase) { TibiaFormat.AssetsFolder, TibiaFormat.SoundsFolder };
        var pending = new Stack<(DirectoryInfo Dir, int Depth)>();
        pending.Push((new DirectoryInfo(package), 0));
        byte[] magic = new byte[4];
        while (pending.Count > 0)
        {
            var (dir, depth) = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = dir.EnumerateFileSystemInfos().ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                if (PathGuard.IsReparsePoint(entry)) continue;
                if (entry is DirectoryInfo sub)
                {
                    if (depth == 0 && skipTop.Contains(sub.Name)) continue;
                    if (depth < 6) pending.Push((sub, depth + 1));
                    continue;
                }
                var file = (FileInfo)entry;
                if (file.Length < 4 || file.Name.EndsWith("-bak", StringComparison.OrdinalIgnoreCase)) continue;
                bool isResource = false;
                try
                {
                    using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 0);
                    isResource = stream.ReadAtLeast(magic, 4, throwOnEndOfStream: false) == 4 && magic.AsSpan().SequenceEqual("qres"u8);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Locked or unreadable: not a file a version can include.
                }
                if (isResource) yield return PathGuard.ToPortable(package, file.FullName);
            }
        }
    }

    // ------------------------------------------------------------------ scanning

    /// <summary>
    /// Hashes every file of the version (in parallel) and checks each against the client's own hash.
    /// The result is the candidate manifest of what is installed now; nothing is written.
    /// </summary>
    public static ClientScan Scan(string packageFolder, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var set = Discover(packageFolder);
        var hashed = new ConcurrentBag<ManifestFile>();
        var problems = new ConcurrentBag<string>();
        var times = new ConcurrentBag<DateTime>();
        int done = 0;
        var lastReport = DateTime.MinValue;
        object gate = new();
        ContentStore.ForEachParallel(set.Files, null, file =>
        {
            string path = set.SourcePath(file);
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                if (!file.Optional) problems.Add(L.F("ClientScanner.0ListedBy1ButMissing", file.Path, file.Origin));
            }
            else
            {
                try
                {
                    string sha;
                    using (var stream = StoreFiles.OpenSource(path)) sha = FileHashing.HashStream(stream, ct: ct);
                    long size = info.Length;
                    if (file.ExpectedSha256 != null && file.ExpectedSha256 != sha)
                        problems.Add(L.F("ClientScanner.0ItsContentDiffersFromThe", file.Path, file.Origin));
                    else if (file.ExpectedSize is { } expectedSize && expectedSize != size && file.ExpectedSha256 == null)
                        problems.Add(L.F("ClientScanner.0ItsSizeDiffersFromThe", file.Path, file.Origin));
                    else
                    {
                        hashed.Add(new ManifestFile(file.Path, file.Kind, sha, size));
                        times.Add(info.LastWriteTimeUtc);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    problems.Add(L.F("ClientScanner.0ItCannotBeRead1", file.Path, ex.Message));
                }
            }
            int n = Interlocked.Increment(ref done);
            lock (gate)
            {
                if (progress != null && (DateTime.UtcNow - lastReport).TotalMilliseconds >= 250 || n == set.Files.Count)
                {
                    lastReport = DateTime.UtcNow;
                    progress?.Report(new OperationProgress(n, set.Files.Count, L.F("ClientScanner.CheckingFiles0N0Of1", n, set.Files.Count)));
                }
            }
        }, ct);
        var files = hashed.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        if (!files.Any(f => f.Kind != ClientFileKinds.Meta && f.Kind != ClientFileKinds.Rcc))
            problems.Add(L.T("ClientScanner.NoClientAssetFilesWereFound"));
        var sortedProblems = problems.OrderBy(p => p, StringComparer.Ordinal).ToList();
        return new ClientScan
        {
            FileSet = set,
            Files = files,
            Problems = sortedProblems,
            Fingerprint = ClientManifest.ComputeFingerprint(files),
            Snapshot = new SourceSnapshot(set.ClientVersion, set.AssetsJsonSha256, files.Count, files.Sum(f => f.Size), times.IsEmpty ? DateTime.MinValue : times.Max()),
            Duration = watch.Elapsed,
        };
    }

    /// <summary>Sprite ranges by sheet file name from a catalog-content.json (for pairing replaced sheets), or null.</summary>
    public static IReadOnlyDictionary<string, (int First, int Last)>? SheetRanges(string? catalogPath)
    {
        if (catalogPath == null || !File.Exists(catalogPath)) return null;
        try
        {
            byte[] bytes;
            using (var stream = StoreFiles.OpenSource(catalogPath))
            {
                bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
            }
            var catalog = AssetCatalog.Parse(Path.GetDirectoryName(catalogPath)!, bytes, new FileFingerprint(catalogPath, bytes.Length, DateTime.MinValue));
            return catalog.Sheets.GroupBy(s => s.FileName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => (g.First().FirstSpriteId, g.First().LastSpriteId), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
