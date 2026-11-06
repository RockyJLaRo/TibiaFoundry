using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Clients;

/// <summary>One file found by an inventory: its path in the folder ('/' separated) and what the file system says about it.</summary>
/// <param name="ListedBy">The client list that names it ("package.json", "assets.json"), "version file", or null.</param>
/// <param name="ExpectedSha256">The client's own hash of the file, when its list gives one.</param>
public sealed record InventoryFile(string Path, long Size, DateTime LastWriteUtc, FileAttributes Attributes, string? ListedBy, string? ExpectedSha256);

/// <summary>Everything an inventory found in a folder, before anything is hashed.</summary>
public sealed class FolderInventory
{
    public required string Root { get; init; }
    /// <summary>The files to archive, sorted by path.</summary>
    public required IReadOnlyList<InventoryFile> Files { get; init; }
    /// <summary>Empty folders to archive ('/' separated), sorted.</summary>
    public required IReadOnlyList<string> Directories { get; init; }
    /// <summary>What was left out on purpose (personal data), with counts; never the paths themselves.</summary>
    public required IReadOnlyList<ManifestExclusion> Exclusions { get; init; }
    /// <summary>Entries that could not be listed or are links (not followed).</summary>
    public required IReadOnlyList<string> Problems { get; init; }
    public long TotalBytes => Files.Sum(f => f.Size);
}

/// <summary>Options of an inventory.</summary>
public sealed class InventoryOptions
{
    /// <summary>Include hotkeys, character data, the explored minimap, screenshots, logs and caches (left out, visibly, by default).</summary>
    public bool IncludePersonalData { get; init; }

    /// <summary>How many files are read at once (null = all processors but one). The launch check reads with two, in the background.</summary>
    public int? Parallelism { get; init; }

    /// <summary>A file's hash known from an earlier check (the quick launch check), or null: that file is then not read.</summary>
    public Func<InventoryFile, string?>? KnownHash { get; init; }
}

/// <summary>A hashed inventory: the candidate content of a revision, and whether it may be published as it is.</summary>
public sealed class FolderScan
{
    public required FolderInventory Inventory { get; init; }
    /// <summary>The files with the hashes of their bytes, sorted by path.</summary>
    public required IReadOnlyList<ManifestFile> Files { get; init; }
    /// <summary>Why the scan is not a consistent picture of the folder (unreadable, changed during the scan, different from the client's own list). Empty when it is.</summary>
    public required IReadOnlyList<string> Problems { get; init; }
    public required string Fingerprint { get; init; }
    public TimeSpan Duration { get; init; }
    /// <summary>Files whose bytes were read (a quick launch check trusts the others' recorded hashes).</summary>
    public int HashedFiles { get; init; }

    public string Root => Inventory.Root;
    public IReadOnlyList<string> Directories => Inventory.Directories;
    public IReadOnlyList<ManifestExclusion> Exclusions => Inventory.Exclusions;
    public bool IsConsistent => Problems.Count == 0;
    public long TotalBytes => Files.Sum(f => f.Size);
    public string SourcePath(ManifestFile file) => Path.Combine(Root, file.Path.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>
/// The complete inventory of a folder (8.0): every file and empty folder at any depth, hidden files,
/// files without an extension, backups and files no client list names, independently of the files
/// Tibia Foundry reads. Personal data is left out unless asked for, and every exclusion is recorded
/// with its counts. Links are not followed. Only reads: files are opened for shared reading.
/// </summary>
public static class ClientInventory
{
    /// <summary>Client screenshots, wherever they are: "&lt;date&gt;_&lt;time&gt;_&lt;character&gt;_&lt;kind&gt;.png". The names hold a character's name, so they are never listed.</summary>
    private static readonly Regex ScreenshotName = new(@"^\d{4}-\d{2}-\d{2}_\d{6,9}_.+_[A-Za-z]+\.png$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Why each personal folder is left out (the top-level folders of <see cref="ClientScanner.PersonalFolders"/>).</summary>
    private static string PersonalReason(string folder) => folder.ToLowerInvariant() switch
    {
        "conf" => L.T("ClientInventory.PersonalDataYourOptionsAndHotkeys"),
        "characterdata" => L.T("ClientInventory.PersonalDataYourCharactersData"),
        "minimap" => L.T("ClientInventory.PersonalDataYourExploredMap"),
        "screenshots" => L.T("ClientInventory.PersonalDataYourScreenshots"),
        "log" => L.T("ClientInventory.PersonalDataTheClientSLogs"),
        "cache" => L.T("ClientInventory.PersonalDataTheClientSCache"),
        "crashdump" => L.T("ClientInventory.PersonalDataTheClientSCrash"),
        "storeimages" => L.T("ClientInventory.PersonalDataImagesTheClientDownloaded"),
        "npc_pics" => L.T("ClientInventory.PersonalDataImagesTheClientDownloaded"),
        _ => L.T("ClientInventory.PersonalData"),
    };

    /// <summary>
    /// Lists a folder. Files the client's own lists name (package.json, assets.json) get their
    /// expected hash; inside personal folders they are kept (for example conf\gpublacklist.json).
    /// </summary>
    public static FolderInventory Take(string root, InventoryOptions? options = null, CancellationToken ct = default)
    {
        options ??= new InventoryOptions();
        root = PathGuard.Normalize(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"The folder {root} does not exist.");
        var listed = ClientLists(root);
        var files = new List<InventoryFile>();
        var directories = new List<string>();
        var problems = new List<string>();
        var excluded = new Dictionary<string, (int Files, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        int screenshots = 0;
        long screenshotBytes = 0;

        var pending = new Stack<(DirectoryInfo Dir, string Rel, bool Personal)>();
        pending.Push((new DirectoryInfo(root), "", false));
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, rel, personal) = pending.Pop();
            List<FileSystemInfo> entries;
            try
            {
                entries = dir.EnumerateFileSystemInfos().ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Inside a personal folder that is left out, the folder's name is enough.
                problems.Add(L.F("ClientInventory.0ItCannotBeListed1", (rel.Length == 0 ? L.T("ClientInventory.TheFolderItself") : rel + "/"), ex.Message));
                continue;
            }
            foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                string path = rel.Length == 0 ? entry.Name : rel + "/" + entry.Name;
                bool isPersonalTop = rel.Length == 0 && entry is DirectoryInfo && ClientScanner.PersonalFolders.Contains(entry.Name);
                bool inPersonal = personal || isPersonalTop;
                // A left-out personal folder is recorded even when it is empty, so the revision never looks complete without it.
                if (isPersonalTop && !options.IncludePersonalData) excluded.TryAdd(entry.Name, (0, 0));
                if (PathGuard.IsReparsePoint(entry))
                {
                    if (inPersonal && !options.IncludePersonalData) { Count(TopOf(path), 0); continue; }
                    problems.Add(L.F("ClientInventory.0ItIsALinkJunction", path));
                    continue;
                }
                if (entry is DirectoryInfo sub)
                {
                    pending.Push((sub, path, inPersonal));
                    continue;
                }
                var file = (FileInfo)entry;
                listed.TryGetValue(path, out var list);
                if (!options.IncludePersonalData && list.ListedBy == null)
                {
                    if (inPersonal) { Count(TopOf(path), file.Length); continue; }
                    if (ScreenshotName.IsMatch(file.Name)) { screenshots++; screenshotBytes += file.Length; continue; }
                }
                files.Add(new InventoryFile(path, file.Length, file.LastWriteTimeUtc, file.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden), list.ListedBy, list.Sha256));
            }
            // A folder with nothing in it is archived as an empty folder (unless it is inside left-out personal data).
            if (entries.Count == 0 && rel.Length > 0 && !(personal && !options.IncludePersonalData))
                directories.Add(rel);
        }

        var exclusions = excluded.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .Select(e => new ManifestExclusion(e.Key + "/" + (e.Key.Equals("conf", StringComparison.OrdinalIgnoreCase) ? L.T("ClientInventory.ExceptTheFilesTheClientLists") : ""), PersonalReason(e.Key), e.Value.Files, e.Value.Bytes))
            .ToList();
        if (screenshots > 0)
            exclusions.Add(new ManifestExclusion(L.T("ClientInventory.ClientScreenshotsDateTimeCharacterKind"), L.T("ClientInventory.PersonalDataScreenshotsNamedAfterA"), screenshots, screenshotBytes));
        return new FolderInventory
        {
            Root = root,
            Files = files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList(),
            Directories = directories.Order(StringComparer.Ordinal).ToList(),
            Exclusions = exclusions,
            Problems = problems,
        };

        void Count(string top, long bytes)
        {
            var (n, b) = excluded.GetValueOrDefault(top);
            excluded[top] = (n + (bytes >= 0 ? 1 : 0), b + Math.Max(0, bytes));
        }
    }

    private static string TopOf(string path) => path.Split('/')[0];

    /// <summary>The client's own lists: package.json and assets.json "files" (path → list name and hash), plus the version files.</summary>
    internal static Dictionary<string, (string? ListedBy, string? Sha256)> ClientLists(string root)
    {
        var result = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        foreach (var (file, origin) in new[] { (TibiaFormat.PackageManifest, "package.json"), (ClientScanner.AssetsJson, "assets.json") })
            foreach (var f in ClientScanner.ReadFileList(Path.Combine(root, file), origin, notes, quiet: true) ?? [])
                result.TryAdd(f.Path, (origin, f.ExpectedSha256));
        foreach (string version in new[] { TibiaFormat.PackageManifest, ClientScanner.VersionFile, ClientScanner.AssetsJson, ClientScanner.AssetsJsonHash })
            if (!result.ContainsKey(version) && File.Exists(Path.Combine(root, version))) result[version] = ("version file", null); // loc: identity (stored in manifests, compared by ClientMonitor)
        return result;
    }

    /// <summary>
    /// Hashes every file of an inventory (in parallel, streaming) and lists the folder again
    /// afterwards. The scan is consistent only when every file was read, every file the client
    /// lists has the client's hash, no file changed while it was read, and the second listing equals
    /// the first. Nothing is written. <see cref="InventoryOptions.KnownHash"/> (the quick launch check)
    /// may give a file's hash from an earlier check; that file is then not read.
    /// </summary>
    public static FolderScan Scan(string root, InventoryOptions? options = null, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var inventory = Take(root, options, ct);
        var hashed = new ConcurrentBag<ManifestFile>();
        var problems = new ConcurrentBag<string>(inventory.Problems);
        options ??= new InventoryOptions();
        long done = 0, total = Math.Max(1, inventory.TotalBytes);
        int count = 0, read = 0;
        var last = DateTime.MinValue;
        object gate = new();
        var knownHash = options.KnownHash;
        ContentStore.ForEachParallel(inventory.Files, options.Parallelism, file =>
        {
            string path = Path.Combine(inventory.Root, file.Path.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (knownHash?.Invoke(file) is { } known)
                {
                    hashed.Add(new ManifestFile(file.Path, ClientFileKinds.Of(file.Path), known, file.Size, file.ListedBy, file.LastWriteUtc, AttributeText(file.Attributes)));
                    Interlocked.Add(ref done, file.Size);
                    Interlocked.Increment(ref count);
                    return;
                }
                Interlocked.Increment(ref read);
                string sha;
                using (var stream = StoreFiles.OpenSource(path)) sha = FileHashing.HashStream(stream, ct: ct);
                var after = new FileInfo(path);
                if (!after.Exists || after.Length != file.Size || after.LastWriteTimeUtc != file.LastWriteUtc)
                    problems.Add(L.F("ClientInventory.0ItChangedWhileItWas", file.Path));
                else if (file.ExpectedSha256 != null && file.ExpectedSha256 != sha)
                    problems.Add(L.F("ClientInventory.0ItsContentDiffersFromThe", file.Path, file.ListedBy));
                else
                    hashed.Add(new ManifestFile(file.Path, ClientFileKinds.Of(file.Path), sha, file.Size, file.ListedBy, file.LastWriteUtc, AttributeText(file.Attributes)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add(L.F("ClientInventory.0ItCannotBeRead1", file.Path, ex.Message));
            }
            long bytes = Interlocked.Add(ref done, file.Size);
            int n = Interlocked.Increment(ref count);
            lock (gate)
            {
                if (progress != null && ((DateTime.UtcNow - last).TotalMilliseconds >= 250 || n == inventory.Files.Count))
                {
                    last = DateTime.UtcNow;
                    progress.Report(new OperationProgress((int)(bytes * 1000 / total), 1000, L.F("ClientInventory.ReadingFiles0N0Of1", n, inventory.Files.Count, ReportWriter.FormatSize(bytes), ReportWriter.FormatSize(inventory.TotalBytes))));
                }
            }
        }, ct);
        ct.ThrowIfCancellationRequested();

        // Second listing: the folder must look exactly as it did before hashing.
        var again = Take(root, options, ct);
        if (!Same(inventory, again))
            problems.Add(L.T("ClientInventory.TheFolderChangedDuringTheScan"));

        var files = hashed.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        return new FolderScan
        {
            Inventory = inventory,
            Files = files,
            Problems = problems.Distinct().Order(StringComparer.Ordinal).ToList(),
            Fingerprint = ClientManifest.ComputeFingerprint(files, inventory.Directories),
            Duration = watch.Elapsed,
            HashedFiles = read,
        };
    }

    private static bool Same(FolderInventory a, FolderInventory b) =>
        a.Files.Count == b.Files.Count && a.Directories.SequenceEqual(b.Directories, StringComparer.Ordinal) &&
        a.Files.Zip(b.Files).All(p => p.First.Path == p.Second.Path && p.First.Size == p.Second.Size && p.First.LastWriteUtc == p.Second.LastWriteUtc);

    /// <summary>"readonly", "hidden", "readonly,hidden" or null.</summary>
    public static string? AttributeText(FileAttributes attributes)
    {
        var parts = new List<string>();
        if ((attributes & FileAttributes.ReadOnly) != 0) parts.Add("readonly");
        if ((attributes & FileAttributes.Hidden) != 0) parts.Add("hidden");
        return parts.Count == 0 ? null : string.Join(",", parts);
    }

    /// <summary>The attributes a manifest records (only read-only and hidden).</summary>
    public static FileAttributes ParseAttributes(string? text)
    {
        var result = FileAttributes.Normal;
        foreach (string part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            result |= part switch { "readonly" => FileAttributes.ReadOnly, "hidden" => FileAttributes.Hidden, _ => 0 };
        return result == FileAttributes.Normal ? FileAttributes.Normal : result & ~FileAttributes.Normal;
    }
}
