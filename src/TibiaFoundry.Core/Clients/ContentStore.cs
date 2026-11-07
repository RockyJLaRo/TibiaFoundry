using System.Text.Json;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Clients;

/// <summary>Another Tibia Foundry window (or an operation still running) is changing the client store.</summary>
public sealed class StoreBusyException(string message) : IOException(message);

/// <summary>A client file did not have the content it should have (damaged, or changed while it was read).</summary>
public sealed class ContentMismatchException(string message) : IOException(message);

/// <summary>Held by the one operation that changes the store; dispose to release.</summary>
public sealed class StoreLock : IDisposable
{
    private FileStream? _stream;
    internal StoreLock(FileStream stream) => _stream = stream;
    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}

/// <summary>Result of adding one file to the store.</summary>
/// <param name="IsNew">True when the content was not stored yet and was written now.</param>
public readonly record struct StoredObject(string Sha256, long Size, bool IsNew);

/// <summary>What a cleanup removed or found.</summary>
public sealed record CleanupResult(int ObjectsRemoved, long BytesFreed, int ObjectsKept, IReadOnlyList<string> Problems);

/// <summary>
/// Content-addressable storage: every unique file content is stored once, as
/// <c>Shared_Assets\Objects\&lt;first 2 hex&gt;\&lt;64 hex SHA-256&gt;</c>, with its original bytes and the
/// read-only attribute. An object is written to <c>Incoming</c> first, its hash checked twice, and
/// only then renamed into place; an existing object is never overwritten. Objects are removed only
/// by <see cref="Cleanup"/>, which moves unreferenced ones to <c>Trash</c> before deleting them, so
/// an interrupted cleanup can be finished or undone (<see cref="Recover"/>).
/// </summary>
/// <remarks>
/// Readers never lock anything: objects never change once published. Writers hold
/// <see cref="Lock"/> (one at a time, across processes).
/// </remarks>
public sealed class ContentStore
{
    public const string SharedFolder = "Shared_Assets";
    public const string ObjectsFolder = "Objects";
    public const string IncomingFolder = "Incoming";
    public const string TrashFolder = "Trash";
    public const string LockFile = "store.lock";
    public const string JournalFile = "import.json";
    public const string CleanupFile = "cleanup.json";

    private readonly StoreFiles _files;

    public ContentStore(string root)
    {
        _files = new StoreFiles(root);
        Root = _files.Root;
    }

    /// <summary>The <c>Clients</c> folder.</summary>
    public string Root { get; }
    public string ObjectsRoot => Path.Combine(Root, SharedFolder, ObjectsFolder);
    public string IncomingRoot => Path.Combine(Root, SharedFolder, IncomingFolder);
    public string TrashRoot => Path.Combine(Root, SharedFolder, TrashFolder);

    internal StoreFiles Files => _files;

    /// <summary>
    /// Runs <paramref name="action"/> for every item on several threads (cores − 1) and rethrows the
    /// first real failure as itself (not wrapped in an AggregateException), so callers can tell a
    /// changed file from a cancellation.
    /// </summary>
    /// <param name="parallelism">How many at once (null = cores − 1).</param>
    internal static void ForEachParallel<T>(IEnumerable<T> items, int? parallelism, Action<T> action, CancellationToken ct)
    {
        try
        {
            Parallel.ForEach(items, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = parallelism ?? Math.Max(1, Environment.ProcessorCount - 1) }, action);
        }
        catch (AggregateException ex)
        {
            var inner = ex.Flatten().InnerExceptions;
            var first = inner.FirstOrDefault(e => e is not OperationCanceledException) ?? inner.First();
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
            throw;
        }
    }

    public static bool IsHash(string? text) =>
        text is { Length: 64 } && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>Where the object with this SHA-256 lives (whether or not it exists).</summary>
    public string ObjectPath(string sha256)
    {
        if (!IsHash(sha256)) throw new ArgumentException($"\"{sha256}\" is not a SHA-256 in lower-case hexadecimal.", nameof(sha256));
        return Path.Combine(ObjectsRoot, sha256[..2], sha256);
    }

    /// <summary>True when the object exists with the expected size (cheap; <see cref="VerifyObject"/> checks the bytes).</summary>
    public bool Contains(string sha256, long size)
    {
        var info = new FileInfo(ObjectPath(sha256));
        return info.Exists && info.Length == size;
    }

    /// <summary>Why the object is not exactly the content its name says (missing, other size, other bytes), or null when it is.</summary>
    public string? VerifyObject(string sha256, long size, CancellationToken ct = default)
    {
        string path = ObjectPath(sha256);
        var info = new FileInfo(path);
        if (!info.Exists) return "missing";
        if (info.Length != size) return L.F("ContentStore.Size0N0InsteadOf1", info.Length, size);
        try
        {
            return FileHashing.HashFile(path, ct: ct) == sha256 ? null : L.T("ContentStore.ItsBytesDoNotMatchIts");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return L.T("ContentStore.ItCannotBeRead") + ex.Message;
        }
    }

    /// <summary>Takes the writer lock, waiting up to <paramref name="wait"/>. Throws <see cref="StoreBusyException"/>.</summary>
    public StoreLock Lock(TimeSpan wait)
    {
        _files.CreateDirectory(Root);
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            try
            {
                return new StoreLock(_files.OpenLock(Path.Combine(Root, LockFile)));
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                throw new StoreBusyException("Another Tibia Foundry window (or an operation that is still running) is changing the client store. Try again when it has finished.");
            }
        }
    }

    // ------------------------------------------------------------------ imports

    /// <summary>Starts an import: a folder in Incoming with a journal of what it is about to publish.</summary>
    internal string BeginIncoming(string id, IEnumerable<string> plannedHashes)
    {
        string folder = Path.Combine(IncomingRoot, id);
        _files.CreateDirectory(folder);
        _files.WriteAtomic(Path.Combine(folder, JournalFile), JsonSerializer.SerializeToUtf8Bytes(new Journal(id, DateTime.UtcNow, plannedHashes.Distinct().ToList()), ClientManifest.Json));
        return folder;
    }

    internal void EndIncoming(string folder) => _files.DeleteDirectory(folder);

    private sealed record Journal(string Id, DateTime StartedUtc, List<string> Hashes);

    /// <summary>
    /// Adds a file. When its content is already stored nothing is written. Otherwise the file is copied
    /// into <paramref name="incomingFolder"/> while it is hashed, the copy is read back and hashed again,
    /// and only then renamed into place. A hash that differs from <paramref name="expectedSha256"/>
    /// throws <see cref="ContentMismatchException"/> and nothing is published.
    /// </summary>
    internal StoredObject Add(string sourcePath, string expectedSha256, long expectedSize, string incomingFolder, CancellationToken ct)
    {
        if (Contains(expectedSha256, expectedSize)) return new StoredObject(expectedSha256, expectedSize, false);
        string temp = Path.Combine(incomingFolder, Guid.NewGuid().ToString("N") + ".part");
        try
        {
            string written = _files.CopyIn(sourcePath, temp, ct);
            long size = new FileInfo(temp).Length;
            if (written != expectedSha256 || size != expectedSize)
                throw new ContentMismatchException($"{Path.GetFileName(sourcePath)} changed after it was checked (its content is not the one scanned). The client may be updating; scan it again.");
            string reread = FileHashing.HashFile(temp, ct: ct);
            if (reread != written) throw new IOException($"The copy of {Path.GetFileName(sourcePath)} in the store reads back differently (disk error?). Nothing was stored.");
            string target = ObjectPath(written);
            if (!_files.MoveNoReplace(temp, target))
            {
                // Published meanwhile (or left by an interrupted import): the existing object is kept, never overwritten.
                _files.DeleteFile(temp);
                return new StoredObject(written, size, false);
            }
            _files.SetReadOnly(target);
            return new StoredObject(written, size, true);
        }
        finally
        {
            if (File.Exists(temp)) _files.DeleteFile(temp);
        }
    }

    // ------------------------------------------------------------------ objects

    /// <summary>Every stored object: its hash, size and time it was published.</summary>
    public IEnumerable<(string Sha256, long Size, DateTime WrittenUtc)> EnumerateObjects()
    {
        if (!Directory.Exists(ObjectsRoot)) yield break;
        foreach (var dir in new DirectoryInfo(ObjectsRoot).EnumerateDirectories())
            foreach (var file in dir.EnumerateFiles())
                if (IsHash(file.Name) && file.Name.StartsWith(dir.Name, StringComparison.Ordinal))
                    yield return (file.Name, file.Length, file.LastWriteTimeUtc);
    }

    /// <summary>Hashes named by the journals of imports that did not finish (their objects must not be removed while they exist).</summary>
    public IReadOnlySet<string> PendingImportHashes()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(IncomingRoot)) return result;
        foreach (var dir in Directory.EnumerateDirectories(IncomingRoot))
        {
            string journal = Path.Combine(dir, JournalFile);
            if (!File.Exists(journal)) continue;
            try
            {
                var j = JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(journal), ClientManifest.Json);
                foreach (string h in j?.Hashes ?? []) if (IsHash(h)) result.Add(h);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // An unreadable journal protects nothing; the minimum age still protects fresh objects.
            }
        }
        return result;
    }

    /// <summary>
    /// Removes objects that no manifest references (mark and sweep). The caller holds the lock and has
    /// read every manifest successfully (<paramref name="referenced"/>). Objects younger than
    /// <paramref name="minimumAge"/> and those named by pending import journals are kept. Unreferenced
    /// objects are first moved to <c>Trash\&lt;id&gt;</c> (recorded in cleanup.json), then deleted.
    /// </summary>
    internal CleanupResult Cleanup(IReadOnlySet<string> referenced, TimeSpan minimumAge, DateTime nowUtc, CancellationToken ct)
    {
        var keep = new HashSet<string>(referenced, StringComparer.Ordinal);
        keep.UnionWith(PendingImportHashes());
        var candidates = EnumerateObjects().Where(o => !keep.Contains(o.Sha256) && nowUtc - o.WrittenUtc >= minimumAge).ToList();
        int kept = EnumerateObjects().Count() - candidates.Count;
        if (candidates.Count == 0) return new CleanupResult(0, 0, kept, []);
        string id = nowUtc.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string trash = Path.Combine(TrashRoot, id);
        _files.CreateDirectory(trash);
        _files.WriteAtomic(Path.Combine(trash, CleanupFile), JsonSerializer.SerializeToUtf8Bytes(candidates.Select(c => c.Sha256).ToList(), ClientManifest.Json));
        var problems = new List<string>();
        long freed = 0;
        int removed = 0;
        foreach (var (sha, size, _) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (_files.MoveNoReplace(ObjectPath(sha), Path.Combine(trash, sha))) { freed += size; removed++; }
            }
            catch (IOException ex)
            {
                problems.Add(L.F("ContentStore.01KeptItIsRemoved", sha[..12], ex.Message));
            }
        }
        FinishTrash(trash, referenced, problems);
        return new CleanupResult(removed, freed, kept + candidates.Count - removed, problems);
    }

    /// <summary>
    /// Puts the store in order after a crash (the caller holds the lock, so no import is running):
    /// leftovers of imports that never published a manifest are deleted, and unfinished cleanups are
    /// finished, putting back any object that is referenced again.
    /// </summary>
    internal IReadOnlyList<string> Recover(IReadOnlySet<string> referenced)
    {
        var notes = new List<string>();
        if (Directory.Exists(IncomingRoot))
            foreach (var dir in Directory.EnumerateDirectories(IncomingRoot))
            {
                _files.DeleteDirectory(dir);
                notes.Add(L.F("ContentStore.AnInterruptedImportWasCleanedUp", Path.GetFileName(dir)));
            }
        if (Directory.Exists(TrashRoot))
            foreach (var dir in Directory.EnumerateDirectories(TrashRoot))
            {
                FinishTrash(dir, referenced, notes);
                notes.Add(L.F("ContentStore.AnInterruptedCleanupWasFinished0", Path.GetFileName(dir)));
            }
        return notes;
    }

    private void FinishTrash(string trash, IReadOnlySet<string> referenced, List<string> problems)
    {
        foreach (var file in Directory.EnumerateFiles(trash))
        {
            string name = Path.GetFileName(file);
            if (!IsHash(name)) continue;
            if (referenced.Contains(name))
            {
                // Referenced again (should not happen under the lock, but never delete what is needed).
                if (!_files.MoveNoReplace(file, ObjectPath(name))) _files.DeleteFile(file);
                continue;
            }
            try
            {
                _files.DeleteFile(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add(L.F("ContentStore.01LeftInTheTrash", name[..12], ex.Message));
            }
        }
        if (!Directory.EnumerateFiles(trash).Any(f => IsHash(Path.GetFileName(f)))) _files.DeleteDirectory(trash);
    }
}
