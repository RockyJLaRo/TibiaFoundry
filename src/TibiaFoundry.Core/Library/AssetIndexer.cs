using System.Text.RegularExpressions;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Library;

/// <summary>How files are added to the library.</summary>
public sealed record IndexOptions
{
    /// <summary>Category for every file; null = inferred from the folder names and the file (see <see cref="AssetIndexer.InferCategory"/>).</summary>
    public string? Category { get; init; }
    public string? Source { get; init; }
    /// <summary>The Tibia Foundry tool that wrote the files (exports added automatically).</summary>
    public string? SourceTool { get; init; }
    /// <summary>Client version of the files; null = taken from a version in the folder names, if any.</summary>
    public string? ClientVersion { get; init; }
    public bool Recursive { get; init; } = true;
    /// <summary>Also index data files (JSON, CSV, text, Lua, XML).</summary>
    public bool IncludeData { get; init; }
    /// <summary>Read every file again even when its size and time did not change.</summary>
    public bool Force { get; init; }
    /// <summary>Copy the files into the library's own folder and index the copies (for files from temporary or removable locations).</summary>
    public bool CopyIntoLibrary { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    /// <summary>A collection the indexed files are added to, or null.</summary>
    public string? Collection { get; init; }
    /// <summary>Stop after this many files (protects against indexing a whole disk by accident).</summary>
    public int MaxFiles { get; init; } = 500_000;
    /// <summary>Files read and inspected at the same time (8.8.1); the database is still written by one thread, in file order.</summary>
    public int MaxDegreeOfParallelism { get; init; } = Math.Clamp(Environment.ProcessorCount - 1, 1, 8);
}

/// <summary>What an indexing run did.</summary>
public sealed record IndexResult(int Seen, int Added, int Updated, int Unchanged, int ContentChanged, int MarkedMissing, int FoundAgain, IReadOnlyList<string> Problems, bool Truncated)
{
    public string Summary =>
        L.F("AssetIndexer.0Checked1N0Added2", L.Count(Seen, "Noun.file"), Added, Updated, Unchanged) +
        (ContentChanged > 0 ? L.F("AssetIndexer.0RecordedAsNewVersions", L.Count(ContentChanged, "Noun.changed_content")) : "") +
        (MarkedMissing > 0 ? L.F("AssetIndexer.0N0NoLongerFoundKept", MarkedMissing) : "") +
        (FoundAgain > 0 ? L.F("AssetIndexer.0N0FoundAgain", FoundAgain) : "") +
        (Problems.Count > 0 ? $", {L.Count(Problems.Count, "Noun.problem")}" : "") + (Truncated ? L.T("AssetIndexer.StoppedAtTheFileLimit") : "") + ".";
}

/// <summary>
/// Adds files to the <see cref="AssetLibrary"/> (8.8). Indexing is incremental: a file whose size and
/// modified time did not change since the last scan is not read again, so rescanning thousands of
/// assets takes seconds. Files that disappeared are marked missing, never removed; a changed file
/// keeps its metadata and its previous content is recorded as a version. Files are only read
/// (except with <see cref="IndexOptions.CopyIntoLibrary"/>, which copies them into the library).
/// </summary>
public static class AssetIndexer
{
    private const int BatchSize = 200;

    /// <summary>Indexes a folder registered as a library root (see <see cref="AssetLibrary.AddRoot"/>), incrementally.</summary>
    public static IndexResult IndexRoot(AssetLibrary library, LibraryRoot root, IndexOptions? options = null, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        options ??= new IndexOptions();
        options = options with { Recursive = root.Recursive, Category = options.Category ?? root.Category, Source = options.Source ?? root.Source ?? L.F("AssetIndexer.Folder0", root.Path) };
        if (!Directory.Exists(root.Path)) throw new DirectoryNotFoundException($"{root.Path} does not exist (moved, renamed or on a drive that is not connected). Its assets stay in the library, marked missing after a scan that can see the folder.");
        var files = EnumerateFiles(root.Path, options, ct, out bool truncated);
        var result = IndexFiles(library, files.ConvertAll(f => f.FullName), files, options, root.Id, progress, root.Path, truncated, ct);
        library.MarkScanned(root.Id);
        return result;
    }

    /// <summary>Registers <paramref name="folder"/> as a library root and indexes it.</summary>
    public static IndexResult IndexFolder(AssetLibrary library, string folder, IndexOptions? options = null, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        options ??= new IndexOptions();
        if (options.CopyIntoLibrary)
        {
            var files = EnumerateFiles(folder, options, ct, out bool truncated);
            return IndexFiles(library, files.ConvertAll(f => f.FullName), files, options, null, progress, null, truncated, ct);
        }
        var root = library.AddRoot(folder, options.Recursive, options.Category, options.Source);
        return IndexRoot(library, root, options, progress, ct);
    }

    /// <summary>Lists the files of a folder the library indexes (skips the library's own folder, hidden system folders and links).</summary>
    public static List<string> Enumerate(string folder, IndexOptions options, CancellationToken ct, out bool truncated) =>
        EnumerateFiles(folder, options, ct, out truncated).ConvertAll(f => f.FullName);

    /// <summary>
    /// The files with the size and time the directory listing reported (8.8.1): a rescan compares them
    /// with the library without asking the file system about each file again, which was nearly all the
    /// time of rescanning an unchanged folder.
    /// </summary>
    private static List<FileInfo> EnumerateFiles(string folder, IndexOptions options, CancellationToken ct, out bool truncated)
    {
        var list = new List<FileInfo>();
        truncated = false;
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(folder));
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            string dir = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var e in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (PathGuard.IsReparsePoint(e)) continue;
                if (e is DirectoryInfo d)
                {
                    if (!options.Recursive || d.Name.StartsWith('.') || d.Name is "thumbnails" or "$RECYCLE.BIN" or "System Volume Information" || (d.Attributes & FileAttributes.System) != 0) continue;
                    pending.Push(d.FullName);
                }
                else if (e is FileInfo f && MediaTypes.IsIndexed(f.Extension, options.IncludeData) && !f.Name.StartsWith(".tf-", StringComparison.Ordinal))
                {
                    list.Add(f);
                    if (list.Count >= options.MaxFiles)
                    {
                        truncated = true;
                        return list;
                    }
                }
            }
        }
        return list;
    }

    /// <summary>
    /// Indexes individual files (exports added automatically, "Add files…"). With
    /// <paramref name="scanFolder"/>, assets under that folder that were not seen are marked missing.
    /// </summary>
    public static IndexResult IndexFiles(AssetLibrary library, IReadOnlyList<string> files, IndexOptions? options = null, long? rootId = null,
        IProgress<OperationProgress>? progress = null, string? scanFolder = null, bool truncated = false, CancellationToken ct = default) =>
        IndexFiles(library, files, null, options, rootId, progress, scanFolder, truncated, ct);

    /// <param name="listed">The files as a directory listing reported them (same order as <paramref name="files"/>), or null to look at each file.</param>
    private static IndexResult IndexFiles(AssetLibrary library, IReadOnlyList<string> files, IReadOnlyList<FileInfo>? listed, IndexOptions? options, long? rootId,
        IProgress<OperationProgress>? progress, string? scanFolder, bool truncated, CancellationToken ct)
    {
        options ??= new IndexOptions();
        var known = library.FileFacts(scanFolder);
        var problems = new List<string>();
        var batch = new List<AssetRecord>();
        var seenIds = new HashSet<long>();
        var touched = new List<long>();
        int added = 0, updated = 0, unchanged = 0, contentChanged = 0, foundAgain = 0;
        var foundAgainIds = new List<long>();

        // Files to describe, in file order. 8.8.1: they are read and inspected in parallel, a batch at a
        // time (reading, hashing and decoding thousands of small images was strictly one after another);
        // the results, problems and database writes keep the order of the files.
        var pending = new List<string>();

        void Flush()
        {
            if (pending.Count > 0)
            {
                var records = new AssetRecord?[pending.Count];
                var errors = new Exception?[pending.Count];
                Parallel.For(0, pending.Count, new ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism, CancellationToken = ct }, i =>
                {
                    try { records[i] = Describe(pending[i], options, library); }
                    catch (Exception ex) { errors[i] = ex; }
                });
                for (int i = 0; i < pending.Count; i++)
                {
                    if (errors[i] is { } error)
                    {
                        if (error is IOException or UnauthorizedAccessException or InvalidDataException) problems.Add($"{pending[i]}: {error.Message}");
                        else System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                    }
                    else batch.Add(records[i]!);
                }
                pending.Clear();
            }
            if (batch.Count == 0) return;
            foreach (var (id, isNew, changed) in library.Upsert(batch, rootId))
            {
                seenIds.Add(id);
                touched.Add(id);
                if (isNew) added++;
                else updated++;
                if (changed) contentChanged++;
            }
            batch.Clear();
        }

        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i % 25 == 0) progress?.Report(new OperationProgress(i, files.Count, L.F("AssetIndexer.Indexing0", Path.GetFileName(files[i]))));
            string path = Path.GetFullPath(files[i]);
            try
            {
                var info = listed?[i] ?? new FileInfo(path);
                if (!info.Exists) { problems.Add(L.F("AssetIndexer.0NotFound", path)); continue; }
                if (options.CopyIntoLibrary && !PathGuard.IsInside(library.FilesFolder, path))
                {
                    path = CopyIntoLibrary(library, path);
                    info = new FileInfo(path);
                }
                if (!options.Force && known.TryGetValue(path, out var facts) && facts.Size == info.Length && Math.Abs((facts.FileModifiedUtc - info.LastWriteTimeUtc).TotalSeconds) < 2.1)
                {
                    seenIds.Add(facts.Id);
                    if (facts.Missing) foundAgainIds.Add(facts.Id);
                    else unchanged++;
                    continue;
                }
                pending.Add(path);
                if (pending.Count >= BatchSize) Flush();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                problems.Add($"{path}: {ex.Message}");
            }
        }
        Flush();
        if (foundAgainIds.Count > 0)
        {
            library.SetMissing(foundAgainIds, false);
            foundAgain = foundAgainIds.Count;
        }
        int missing = 0;
        if (scanFolder != null && !truncated)
        {
            var gone = known.Where(k => !k.Value.Missing && !seenIds.Contains(k.Value.Id) && !File.Exists(k.Key)).Select(k => k.Value.Id).ToList();
            library.SetMissing(gone, true);
            missing = gone.Count;
        }
        if (options.Tags.Count > 0) library.AddTags(touched, options.Tags);
        if (!string.IsNullOrWhiteSpace(options.Collection))
        {
            var ids = touched.Concat(seenIds).Distinct().ToList();
            if (ids.Count > 0) library.AddToCollection(library.EnsureCollection(options.Collection.Trim()), ids);
        }
        progress?.Report(new OperationProgress(files.Count, files.Count, L.T("AssetIndexer.Indexed")));
        return new IndexResult(files.Count, added, updated, unchanged, contentChanged, missing, foundAgain, problems, truncated);
    }

    /// <summary>The record of a file: its facts, and name, category, version and ID inferred from its path.</summary>
    public static AssetRecord Describe(string path, IndexOptions options, AssetLibrary? library = null)
    {
        var facts = MediaInspector.Inspect(path);
        string name = NameFromFile(path);
        string category = options.Category ?? InferCategory(path, facts);
        var attribution = facts.Attribution;
        var tags = new List<string>(options.Tags);
        var fields = new Dictionary<string, string>();
        if (facts.Problem != null) fields["Problem"] = facts.Problem;
        return new AssetRecord
        {
            Path = path, Name = name, Category = category, Source = options.Source, SourceTool = options.SourceTool,
            ClientVersion = options.ClientVersion ?? VersionFromPath(path), OriginalId = IdFromName(Path.GetFileNameWithoutExtension(path)),
            Format = facts.Format, Width = facts.Width, Height = facts.Height, Frames = facts.Frames, SizeBytes = facts.SizeBytes, Sha256 = facts.Sha256,
            PixelHash = facts.PixelHash, PixelKey = facts.PixelKey, FileModifiedUtc = facts.FileModifiedUtc,
            Creator = attribution?.Creators.Count > 0 ? string.Join("; ", attribution.Creators) : null,
            Attribution = attribution?.Text, Copyright = attribution?.Copyright,
            Managed = library != null && PathGuard.IsInside(library.FilesFolder, path),
            Tags = tags, Fields = fields,
        };
    }

    /// <summary>Copies a file into the library's <c>files</c> folder (by date), never replacing another file. Returns the copy's path.</summary>
    public static string CopyIntoLibrary(AssetLibrary library, string source)
    {
        string folder = Path.Combine(library.FilesFolder, DateTime.Now.ToString("yyyy-MM"));
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, Path.GetFileName(source));
        string stem = Path.GetFileNameWithoutExtension(source), ext = Path.GetExtension(source);
        for (int n = 2; File.Exists(target); n++)
        {
            // The same content is already there: reuse it instead of storing a second copy.
            if (new FileInfo(target).Length == new FileInfo(source).Length && File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(source))) return target;
            target = Path.Combine(folder, $"{stem} ({n}){ext}");
        }
        AtomicFile.Copy(source, target);
        return target;
    }

    /// <summary>A readable name from a file name: "Dragon_Lord_2x.gif" → "Dragon Lord", "1234_Idle_South" stays.</summary>
    public static string NameFromFile(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        stem = Regex.Replace(stem, @"_(\d)x$", "");
        string name = stem.Replace('_', ' ').Trim();
        return name.Length == 0 ? Path.GetFileName(path) : name;
    }

    private static readonly Regex Version = new(@"(?<![\d.])(\d{1,2}\.\d{2}(?:\.[0-9a-f]{4,8})?)(?![\d])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A client version in the folder names ("…\15.33.8791c6\…", "Tibia 10.98"), or null.</summary>
    public static string? VersionFromPath(string path)
    {
        foreach (string part in (Path.GetDirectoryName(path) ?? "").Split(Path.DirectorySeparatorChar).Reverse())
            if (Version.Match(part) is { Success: true } m) return m.Groups[1].Value;
        return null;
    }

    /// <summary>A leading number of the file name ("1234_Idle_South" → 1234, "Sprite 52" → 52), or null.</summary>
    public static string? IdFromName(string stem)
    {
        var m = Regex.Match(stem, @"^(\d{1,9})(?:\D|$)");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(stem, @"(?i)^(?:sprite|object|outfit|item|effect|missile)[ _-]*(\d{1,9})$");
        return m.Success ? m.Groups[1].Value : null;
    }

    // Folder names are matched as written on disk (lower case, "_" as a space), never in the interface language:
    // 9.0–9.0.3 translated four of them, so "Sprite_Sheets" or "Map_Exports" were not recognized outside English.
    private static readonly (string Folder, string Category)[] FolderCategories =
    [
        ("outfits", AssetCategories.Outfits), ("outfit", AssetCategories.Outfits), ("addons", AssetCategories.OutfitAddons), ("mounts", AssetCategories.Mounts),
        ("creatures", AssetCategories.Creatures), ("monsters", AssetCategories.Creatures), ("bosses", AssetCategories.Creatures), ("npcs", AssetCategories.Creatures),
        ("objects", AssetCategories.Items), ("items", AssetCategories.Items), ("effects", AssetCategories.Effects), ("missiles", AssetCategories.Projectiles),
        ("projectiles", AssetCategories.Projectiles), ("sheets", AssetCategories.SpriteSheets), ("sprite sheets", AssetCategories.SpriteSheets),
        ("sprites", AssetCategories.Sprites), ("houses", AssetCategories.Houses), ("house images", AssetCategories.Houses), ("map sections", AssetCategories.MapSections),
        ("maps", AssetCategories.Maps), ("map", AssetCategories.Maps), ("map exports", AssetCategories.Maps), ("sounds", AssetCategories.Sounds), ("music", AssetCategories.Sounds),
        ("environment", AssetCategories.Environment), ("artwork", AssetCategories.Artwork),
    ];

    /// <summary>
    /// The category of a file: from the nearest folder name that names one ("…\Outfits\…" → Outfits),
    /// then from the file itself (audio → Sounds, a sprite sheet's file name, animations, images
    /// Tibia Foundry made → Generated Images, images attributed to someone → Custom Artwork).
    /// </summary>
    public static string InferCategory(string path, MediaFacts facts)
    {
        foreach (string part in (Path.GetDirectoryName(path) ?? "").Split(Path.DirectorySeparatorChar).Reverse().Take(4))
        {
            string p = part.Trim().ToLowerInvariant().Replace('_', ' ');
            foreach (var (folder, category) in FolderCategories)
                if (p == folder) return category;
        }
        string file = Path.GetFileName(path).ToLowerInvariant();
        if (MediaTypes.Audio.Contains(facts.Format)) return AssetCategories.Sounds;
        if (file.StartsWith("sprites-", StringComparison.Ordinal) || file.Contains("sheet", StringComparison.Ordinal)) return AssetCategories.SpriteSheets;
        if (file.StartsWith("map", StringComparison.Ordinal) || facts.Width >= 512 && facts.Height >= 512) return AssetCategories.Maps;
        if (facts.Frames > 1) return AssetCategories.Animations;
        if (facts.Attribution is { } a)
            return a.Software?.Contains("Tibia Foundry", StringComparison.OrdinalIgnoreCase) == true || a.Text?.Contains("TibiaFoundry", StringComparison.OrdinalIgnoreCase) == true
                ? AssetCategories.Generated : AssetCategories.Artwork;
        return AssetCategories.Other;
    }
}
