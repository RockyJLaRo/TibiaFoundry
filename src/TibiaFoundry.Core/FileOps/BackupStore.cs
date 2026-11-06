using System.Text.Json;
using System.Text.Json.Serialization;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.FileOps;

/// <summary>Why a backup set was made.</summary>
public enum BackupKind
{
    /// <summary>Made automatically before a tool replaced or changed files.</summary>
    Automatic,
    /// <summary>Made by the user with the Backup and Restore script.</summary>
    Manual,
    /// <summary>Files moved out of the way by a clean-up (duplicates, empty files): restoring puts them back.</summary>
    Removed,
}

/// <summary>One file (or empty folder) in a backup set.</summary>
public sealed class BackupEntry
{
    /// <summary>Full path the file had when it was backed up (where a restore puts it back).</summary>
    public string Original { get; set; } = "";
    /// <summary>Path relative to the set's source folder, when the set was made from a folder.</summary>
    public string? Relative { get; set; }
    /// <summary>Location inside the set folder ('/' separators).</summary>
    public string Stored { get; set; } = "";
    public long Size { get; set; }
    public string? Sha256 { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public bool IsDirectory { get; set; }
}

/// <summary>Manifest of a backup set (backup.json in the set folder).</summary>
public sealed class BackupManifest
{
    public int Format { get; set; } = 1;
    public string Id { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public string Operation { get; set; } = "";
    public BackupKind Kind { get; set; }
    public string? SourceRoot { get; set; }
    public string Application { get; set; } = AppIdentity.NameAndVersion;
    public bool Complete { get; set; }
    public List<BackupEntry> Entries { get; set; } = [];
}

/// <summary>A backup set as listed to the user.</summary>
public sealed record BackupSetInfo(string Folder, BackupManifest Manifest)
{
    public string Id => Manifest.Id;
    public DateTime Created => Manifest.CreatedUtc.ToLocalTime();
    public long TotalSize => Manifest.Entries.Sum(e => e.Size);
    public int FileCount => Manifest.Entries.Count(e => !e.IsDirectory);
}

/// <summary>Age and size limits for automatic clean-up of old backups.</summary>
/// <param name="MaxAgeDays">Sets older than this are removed (0 = no age limit).</param>
/// <param name="MaxTotalBytes">Oldest sets are removed until the total is below this (0 = no size limit).</param>
public sealed record BackupRetention(int MaxAgeDays, long MaxTotalBytes);

/// <summary>Outcome of restoring files from a backup.</summary>
public sealed class RestoreResult
{
    public List<string> Restored { get; } = [];
    public List<string> Skipped { get; } = [];
    public List<string> Failed { get; } = [];
}

/// <summary>
/// Folder of backup sets shared by all tools: automatic backups before files are replaced, the
/// Backup and Restore script, and files moved away by clean-ups. Every copy is verified with
/// SHA-256 before it counts as backed up, and every set has a manifest saying where each file came
/// from, so it can be restored.
/// </summary>
public sealed class BackupStore(string root)
{
    public const string ManifestName = "backup.json";
    private const string HistoryName = "restore-history.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Root { get; } = Path.GetFullPath(root);

    private readonly HashSet<string> _open = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    /// <summary>Starts a new, empty backup set.</summary>
    /// <param name="sourceRoot">Folder the files come from; entries then remember their relative paths.</param>
    public BackupSet Begin(string operation, BackupKind kind, string? sourceRoot = null)
    {
        string slug = new(operation.Where(c => char.IsLetterOrDigit(c) || c == '-').Take(40).ToArray());
        string id = $"{DateTime.Now:yyyyMMdd-HHmmss}-{(slug.Length == 0 ? "backup" : slug)}-{Guid.NewGuid().ToString("N")[..6]}";
        string folder = Path.Combine(Root, id);
        Directory.CreateDirectory(folder);
        var manifest = new BackupManifest
        {
            Id = id, CreatedUtc = DateTime.UtcNow, Operation = operation, Kind = kind,
            SourceRoot = sourceRoot == null ? null : PathGuard.Normalize(sourceRoot),
        };
        lock (_sync) _open.Add(folder);
        var set = new BackupSet(this, folder, manifest);
        set.SaveManifest();
        return set;
    }

    internal void Closed(string folder)
    {
        lock (_sync) _open.Remove(folder);
    }

    /// <summary>All backup sets, newest first. Damaged manifests are reported in the log and skipped.</summary>
    public IReadOnlyList<BackupSetInfo> List()
    {
        if (!Directory.Exists(Root)) return [];
        var result = new List<BackupSetInfo>();
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            string file = Path.Combine(dir, ManifestName);
            if (!File.Exists(file)) continue;
            try
            {
                var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(file), Json);
                if (manifest != null) result.Add(new BackupSetInfo(dir, manifest));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                AppLog.Warn($"Backup manifest {file} could not be read: {ex.Message}");
            }
        }
        return result.OrderByDescending(s => s.Manifest.CreatedUtc).ToList();
    }

    /// <summary>Total size of all backups in bytes.</summary>
    public long TotalSize() => List().Sum(s => s.TotalSize);

    /// <summary>
    /// Restores the given entries of a set. Files go back to their original paths, or below
    /// <paramref name="targetFolder"/> (keeping their relative paths) when one is given. An existing
    /// file that differs is only replaced when <paramref name="overwrite"/> agrees, and is itself
    /// backed up first, so a restore can be undone.
    /// </summary>
    public RestoreResult Restore(BackupSetInfo set, IEnumerable<BackupEntry> entries, string? targetFolder, Func<string, bool> overwrite, CancellationToken ct = default)
    {
        var result = new RestoreResult();
        BackupSet? safety = null;
        try
        {
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                string target;
                try
                {
                    target = TargetFor(set, entry, targetFolder);
                }
                catch (UnsafePathException ex)
                {
                    result.Failed.Add($"{entry.Original}: {ex.Message}");
                    continue;
                }
                try
                {
                    if (entry.IsDirectory)
                    {
                        Directory.CreateDirectory(target);
                        result.Restored.Add(target);
                        continue;
                    }
                    string stored = PathGuard.ResolveInside(set.Folder, entry.Stored);
                    if (!File.Exists(stored)) { result.Failed.Add(L.F("BackupStore.0TheBackupCopyIsMissing", target)); continue; }
                    if (entry.Sha256 != null && FileHashing.HashFile(stored, ct: ct) != entry.Sha256)
                    {
                        result.Failed.Add(L.F("BackupStore.0TheBackupCopyIsDamaged", target));
                        continue;
                    }
                    if (File.Exists(target))
                    {
                        if (FileHashing.SameContent(stored, target, ct)) { result.Skipped.Add(target + L.T("BackupStore.AlreadyIdentical")); continue; }
                        if (!overwrite(target)) { result.Skipped.Add(target); continue; }
                        safety ??= Begin(L.F("BackupStore.BeforeRestoring0", set.Id), BackupKind.Automatic);
                        safety.Add(target, ct: ct);
                    }
                    AtomicFile.Copy(stored, target);
                    File.SetLastWriteTimeUtc(target, entry.ModifiedUtc);
                    result.Restored.Add(target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Failed.Add($"{target}: {ex.Message}");
                }
            }
        }
        finally
        {
            safety?.Complete();
        }
        AppendHistory(set, targetFolder, result);
        return result;
    }

    /// <summary>Where an entry is restored to.</summary>
    public static string TargetFor(BackupSetInfo set, BackupEntry entry, string? targetFolder)
    {
        if (targetFolder != null)
            return PathGuard.ResolveInside(targetFolder, entry.Relative ?? Path.GetFileName(entry.Original));
        if (!Path.IsPathFullyQualified(entry.Original) || PathGuard.SafeRelativePath(entry.Original[Path.GetPathRoot(entry.Original)!.Length..]) == null)
            throw new UnsafePathException($"The recorded path \"{entry.Original}\" is not a valid full path.");
        return entry.Original;
    }

    private void AppendHistory(BackupSetInfo set, string? targetFolder, RestoreResult result)
    {
        try
        {
            string path = Path.Combine(Root, HistoryName);
            var list = File.Exists(path) ? JsonSerializer.Deserialize<List<RestoreRecord>>(File.ReadAllText(path), Json) ?? [] : [];
            list.Add(new RestoreRecord(DateTime.UtcNow, set.Id, targetFolder, result.Restored.Count, result.Skipped.Count, result.Failed.Count));
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(list.TakeLast(500).ToList(), Json));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Restore history could not be updated: " + ex.Message);
        }
    }

    /// <summary>Restores done so far, newest first.</summary>
    public IReadOnlyList<RestoreRecord> History()
    {
        string path = Path.Combine(Root, HistoryName);
        try
        {
            return File.Exists(path) ? (JsonSerializer.Deserialize<List<RestoreRecord>>(File.ReadAllText(path), Json) ?? []).AsEnumerable().Reverse().ToList() : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Restore history could not be read: " + ex.Message);
            return [];
        }
    }

    /// <summary>Permanently deletes one backup set (the user asked for it).</summary>
    public void Delete(BackupSetInfo set)
    {
        if (!PathGuard.IsInside(Root, set.Folder)) throw new UnsafePathException($"{set.Folder} is not inside the backup folder.");
        lock (_sync)
            if (_open.Contains(set.Folder)) throw new IOException("This backup is still being written.");
        Directory.Delete(set.Folder, recursive: true);
        AppLog.Info($"Backup set {set.Id} deleted by the user.");
    }

    /// <summary>
    /// Removes sets beyond the retention limits, oldest first. Sets that are still being written are
    /// never removed. Returns the removed set ids.
    /// </summary>
    public IReadOnlyList<string> ApplyRetention(BackupRetention policy)
    {
        var removed = new List<string>();
        var sets = List().OrderBy(s => s.Manifest.CreatedUtc).ToList();
        long total = sets.Sum(s => s.TotalSize);
        foreach (var set in sets)
        {
            bool tooOld = policy.MaxAgeDays > 0 && set.Manifest.CreatedUtc < DateTime.UtcNow.AddDays(-policy.MaxAgeDays);
            bool tooBig = policy.MaxTotalBytes > 0 && total > policy.MaxTotalBytes;
            if (!tooOld && !tooBig) continue;
            lock (_sync)
                if (_open.Contains(set.Folder)) continue;
            try
            {
                Directory.Delete(set.Folder, recursive: true);
                total -= set.TotalSize;
                removed.Add(set.Id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn($"Old backup {set.Id} could not be removed: {ex.Message}");
            }
        }
        if (removed.Count > 0) AppLog.Info($"Backup retention removed {L.Count(removed.Count, "Noun.set")}: {string.Join(", ", removed)}.");
        return removed;
    }

    internal static void WriteManifest(string folder, BackupManifest manifest) =>
        AtomicFile.WriteAllText(Path.Combine(folder, ManifestName), JsonSerializer.Serialize(manifest, Json));
}

/// <summary>One restore operation, for the restore history.</summary>
public sealed record RestoreRecord(DateTime WhenUtc, string SetId, string? TargetFolder, int Restored, int Skipped, int Failed);

/// <summary>A backup set being written. Dispose (or <see cref="Complete"/>) finishes the manifest.</summary>
public sealed class BackupSet : IDisposable
{
    private readonly BackupStore _store;
    private int _counter;
    private bool _closed;

    internal BackupSet(BackupStore store, string folder, BackupManifest manifest)
    {
        _store = store;
        Folder = folder;
        Manifest = manifest;
    }

    public string Folder { get; }
    public BackupManifest Manifest { get; }
    public string Id => Manifest.Id;
    public int Count => Manifest.Entries.Count;

    /// <summary>
    /// Copies (or, with <paramref name="move"/>, moves) a file into the set and verifies the copy.
    /// Returns the stored path. Throws if the file could not be backed up; callers must then not
    /// change the original.
    /// </summary>
    public string Add(string path, bool move = false, CancellationToken ct = default)
    {
        if (_closed) throw new InvalidOperationException("The backup set is already complete.");
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("The file to back up does not exist.", path);
        string? relative = Manifest.SourceRoot != null && PathGuard.IsInside(Manifest.SourceRoot, info.FullName)
            ? Path.GetRelativePath(Manifest.SourceRoot, info.FullName)
            : null;
        string stored = relative != null ? "files/" + relative.Replace('\\', '/') : $"files/{++_counter}/{info.Name}";
        string target = PathGuard.ResolveInside(Folder, stored);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string hash = FileHashing.HashFile(info.FullName, ct: ct);
        var entry = new BackupEntry
        {
            Original = info.FullName, Relative = relative, Stored = stored, Size = info.Length, Sha256 = hash, ModifiedUtc = info.LastWriteTimeUtc,
        };
        // 9.0 (audit F-05): a "move" to another volume is a copy that is verified before the original is
        // deleted; File.Move across volumes deleted the original first, so a bad copy lost the file.
        bool rename = move && SameVolume(info.FullName, target);
        if (rename) File.Move(info.FullName, target);
        else File.Copy(info.FullName, target);
        if (FileHashing.HashFile(target, ct: ct) != hash)
        {
            // A rename within one volume cannot change content; a failed copy is removed again.
            if (!rename) File.Delete(target);
            throw new IOException($"The backup copy of {info.Name} did not match the original.");
        }
        if (move && !rename) File.Delete(info.FullName);
        Manifest.Entries.Add(entry);
        if (Manifest.Entries.Count % 200 == 0) SaveManifest();
        return target;
    }

    /// <summary>Records an empty folder that is about to be removed, so a restore can recreate it.</summary>
    public void AddEmptyDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        string? relative = Manifest.SourceRoot != null && PathGuard.IsInside(Manifest.SourceRoot, info.FullName)
            ? Path.GetRelativePath(Manifest.SourceRoot, info.FullName) : null;
        Manifest.Entries.Add(new BackupEntry { Original = info.FullName, Relative = relative, Stored = "", IsDirectory = true, ModifiedUtc = info.LastWriteTimeUtc });
        SaveManifest();
    }

    internal void SaveManifest() => BackupStore.WriteManifest(Folder, Manifest);

    private static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    public void Complete()
    {
        if (_closed) return;
        _closed = true;
        Manifest.Complete = true;
        SaveManifest();
        _store.Closed(Folder);
    }

    public void Dispose() => Complete();
}
