using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Clients;

/// <summary>What to archive (8.0): a consistent scan of a folder, and how to keep it.</summary>
/// <param name="Scope"><see cref="ManifestScopes.CompleteInstallation"/> or <see cref="ManifestScopes.StandaloneFolder"/>.</param>
/// <param name="Storage"><see cref="ManifestStorage.Shared"/> or <see cref="ManifestStorage.Independent"/>.</param>
/// <param name="SourceType">The kind of folder (<see cref="SourceTypes"/>).</param>
/// <param name="KeepIncomplete">Publish even though the scan lists problems; the revision is then never complete and lists them.</param>
public sealed record ArchiveRequest(FolderScan Scan, string Server, string Scope, string Storage, string SourceType, string? Label, string RegistrationId,
    VersionReport Version, bool KeepIncomplete = false);

/// <summary>Not enough free space for an archive or a rebuild; nothing was written.</summary>
public sealed class InsufficientSpaceException(string message, long needed, long available) : IOException(message)
{
    public long Needed { get; } = needed;
    public long Available { get; } = available;
}

/// <summary>Space attributable to one revision.</summary>
/// <param name="LogicalBytes">Its files as restored (the rebuild size).</param>
/// <param name="UniqueBytes">What only this revision uses: deleting it can free this much (an independent revision: all of it).</param>
/// <param name="SharedBytes">Stored content it shares with other revisions (shared storage only).</param>
public sealed record RevisionUsage(string Key, string Storage, long LogicalBytes, long UniqueBytes, long SharedBytes);

public sealed partial class ClientStore
{
    public const string LeasesFolder = "Leases";
    public const string FilesFolder = "files";
    private const string IncomingSuffix = ".incoming-";
    /// <summary>Free space kept on top of what an archive or a rebuild needs.</summary>
    public const long SpaceMargin = 200L * 1024 * 1024;

    /// <summary>
    /// Archives a scanned folder as a new revision (8.0). The scan must be consistent (unless
    /// <see cref="ArchiveRequest.KeepIncomplete"/>). Content already stored for this server is recognized by its
    /// fingerprint and not stored again. Shared storage adds each new file content once; independent
    /// storage copies every file into the revision's own folder. Every copy is read back and checked
    /// before the manifest is published, so a revision is either complete as declared or does not exist.
    /// </summary>
    public ImportResult Archive(ArchiveRequest request, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        var scan = request.Scan;
        if (request.Scope is not (ManifestScopes.CompleteInstallation or ManifestScopes.StandaloneFolder)) throw new ArgumentException($"Unknown scope \"{request.Scope}\".", nameof(request));
        if (request.Storage is not (ManifestStorage.Shared or ManifestStorage.Independent)) throw new ArgumentException($"Unknown storage \"{request.Storage}\".", nameof(request));
        if (scan.Files.Count == 0) throw new InvalidOperationException("The folder has no file to archive.");
        if (!scan.IsConsistent && !request.KeepIncomplete)
            throw new InvalidOperationException("The folder cannot be archived as it is:" + Environment.NewLine + string.Join(Environment.NewLine, scan.Problems.Take(8)) +
                                                (scan.Problems.Count > 8 ? L.F("ClientStore.Archive.0And1More", Environment.NewLine, scan.Problems.Count - 8) : ""));
        var content = new ManifestContent
        {
            Server = request.Server,
            ClientVersion = request.Version.Version,
            Files = scan.Files,
            Directories = scan.Directories,
            Scope = request.Scope,
            Storage = request.Storage,
            SourceType = request.SourceType,
            Exclusions = scan.Exclusions,
            Problems = scan.Problems,
            Version = request.Version,
            Source = new ManifestSource(request.RegistrationId, L.T("ClientStore.Archive.CompleteInventory"), null),
        };
        var snapshot = new SourceSnapshot(request.Version.Version, scan.Files.FirstOrDefault(f => f.Path == ClientScanner.AssetsJson)?.Sha256, scan.Files.Count, scan.TotalBytes,
            scan.Inventory.Files.Count == 0 ? DateTime.MinValue : scan.Inventory.Files.Max(f => f.LastWriteUtc));
        string prefix = request.Scope == ManifestScopes.CompleteInstallation ? "Install_" : "Folder_";
        string? name = request.Scope == ManifestScopes.StandaloneFolder && request.Version.Status == VersionReport.Unknown ? request.Label : request.Version.Version;
        var notes = new List<string>();
        if (scan.Exclusions.Count > 0) notes.Add(L.F("ClientStore.Archive.LeftOutOnPurpose0", string.Join("; ", scan.Exclusions.Select(e => L.F("ClientStore.Archive.01N0Files", e.Rule, e.Files)))));
        return Publish(content, scan.Fingerprint, scan.SourcePath, request.Server, request.Label, request.RegistrationId, snapshot, prefix, name, notes, progress, ct);
    }

    /// <summary>Bytes an archive would add to the store: new file contents (shared) or every file (independent).</summary>
    public long NeededBytes(IReadOnlyList<ManifestFile> files, string storage) =>
        storage == ManifestStorage.Independent
            ? files.Sum(f => f.Size)
            : files.GroupBy(f => f.Sha256).Where(g => !Content.Contains(g.Key, g.First().Size)).Sum(g => g.First().Size);

    /// <summary>What archiving a scan would do (8.5): totals, what is written and reused, and every category included or left out. Nothing is written.</summary>
    public ArchivePreview PreviewArchive(FolderScan scan, string scope, string storage, bool includesPersonalData) =>
        ArchivePreview.Create(scan, scope, storage, includesPersonalData, Content.Contains);

    /// <summary>Throws <see cref="InsufficientSpaceException"/> when the drive of <paramref name="folder"/> has less than <paramref name="needed"/> plus the margin free.</summary>
    public static void EnsureSpace(string folder, long needed, string what)
    {
        long available;
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root)) return;
            available = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return;   // the drive cannot be asked (network share): the copy itself reports a full disk
        }
        if (available < needed + SpaceMargin)
            throw new InsufficientSpaceException($"Not enough free space for {what}: {ReportWriter.FormatSize(needed)} is needed (plus {ReportWriter.FormatSize(SpaceMargin)} kept free), but the drive has {ReportWriter.FormatSize(available)} free. Nothing was written.", needed, available);
    }

    /// <summary>The common publish path of 7.3 imports and 8.0 archives (under the store lock).</summary>
    private ImportResult Publish(ManifestContent content, string fingerprint, Func<ManifestFile, string> sourcePath, string server, string? label, string registrationId,
        SourceSnapshot snapshot, string keyPrefix, string? keyName, IReadOnlyList<string> notes, IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        if (!ClientServers.IsKnown(server)) throw new ArgumentException($"Unknown server \"{server}\".", nameof(server));
        var watch = Stopwatch.StartNew();
        using var held = Content.Lock(LockWait);
        var registration = Registry.Registration(registrationId) ?? throw new InvalidOperationException("The registration of this client no longer exists.");
        if (FindStored(server, fingerprint) is { } existing)
        {
            if (!registration.Versions.Contains(existing.Key, StringComparer.OrdinalIgnoreCase)) registration.Versions.Add(existing.Key);
            existing.RegistrationId ??= registration.Id;
            registration.LastScanUtc = _clock();
            registration.LastSource = snapshot;
            SaveRegistry();
            return new ImportResult(existing, true, 0, 0, watch.Elapsed, [L.T("ClientStore.Archive.ThisContentIsAlreadyStoredAs") + existing.Label + L.T("ClientStore.Archive.NothingNewWasWritten")]);
        }
        EnsureSpace(Root, NeededBytes(content.Files, content.Storage), L.T("ClientStore.Archive.ThisArchive"));

        string key = NewKey(server, keyPrefix, keyName, fingerprint);
        string versionDir = Path.Combine(Root, key.Replace('/', Path.DirectorySeparatorChar));
        var (manifest, newObjects, newBytes) = content.Storage == ManifestStorage.Independent
            ? PublishIndependent(content, sourcePath, versionDir, progress, ct)
            : PublishShared(content, sourcePath, versionDir, progress, ct);

        var entry = ClientRegistry.EntryFor(key, manifest, registration.Id, newBytes, label);
        Registry.Versions.Add(entry);
        registration.Versions.Add(key);
        registration.LastScanUtc = _clock();
        registration.LastSource = snapshot;
        SaveRegistry();
        return new ImportResult(entry, false, newObjects, newBytes, watch.Elapsed, notes);
    }

    /// <summary>Shared storage: new contents through the incoming journal, then the manifest.</summary>
    private (ClientManifest, int, long) PublishShared(ManifestContent content, Func<ManifestFile, string> sourcePath, string versionDir, IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        string id = _clock().ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string incoming = Content.BeginIncoming(id, content.Files.Select(f => f.Sha256));
        var added = new ConcurrentBag<StoredObject>();
        bool published = false;
        try
        {
            int done = 0;
            long newBytes = 0;
            ContentStore.ForEachParallel(content.Files, null, file =>
            {
                var stored = Content.Add(sourcePath(file), file.Sha256, file.Size, incoming, ct);
                if (stored.IsNew) { added.Add(stored); Interlocked.Add(ref newBytes, stored.Size); }
                int n = Interlocked.Increment(ref done);
                if (n % 64 == 0 || n == content.Files.Count)
                    progress?.Report(new OperationProgress(n, content.Files.Count, L.F("ClientStore.Archive.StoringFiles0N0Of1", n, content.Files.Count, ReportWriter.FormatSize(Interlocked.Read(ref newBytes)))));
            }, ct);
            ct.ThrowIfCancellationRequested();
            var manifest = ClientManifest.Create(content, _clock(), ImportedBy);
            foreach (var f in manifest.Files)
                if (!Content.Contains(f.Sha256, f.Size)) throw new IOException($"{f.Path} is not in the store after it was copied; nothing was published.");
            WriteManifest(versionDir, manifest);
            published = true;
            Content.EndIncoming(incoming);
            return (manifest, added.Count, Interlocked.Read(ref newBytes));
        }
        catch
        {
            if (!published)
            {
                // Under the lock nothing else can reference objects this import created: remove them.
                foreach (var o in added) TryQuietly(() => Content.Files.DeleteFile(Content.ObjectPath(o.Sha256)));
                TryQuietly(() => Content.Files.DeleteDirectory(versionDir));
            }
            TryQuietly(() => Content.EndIncoming(incoming));
            throw;
        }
    }

    /// <summary>
    /// Independent storage: every file is copied into a staging folder next to the revision's folder,
    /// read back and checked, made read-only, and the folder is renamed into place; the manifest comes last.
    /// </summary>
    private (ClientManifest, int, long) PublishIndependent(ManifestContent content, Func<ManifestFile, string> sourcePath, string versionDir, IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        string staging = versionDir + IncomingSuffix + Guid.NewGuid().ToString("N")[..8];
        string stagingFiles = Path.Combine(staging, FilesFolder);
        bool moved = false, published = false;
        try
        {
            Content.Files.CreateDirectory(stagingFiles);
            int done = 0;
            long bytes = 0;
            ContentStore.ForEachParallel(content.Files, null, file =>
            {
                string target = PathGuard.ResolveInside(stagingFiles, file.Path);
                string written = Content.Files.CopyIn(sourcePath(file), target, ct);
                if (written != file.Sha256 || new FileInfo(target).Length != file.Size)
                    throw new ContentMismatchException($"{file.Path} changed after it was checked (its content is not the one scanned). The client may be updating; scan it again.");
                long b = Interlocked.Add(ref bytes, file.Size);
                int n = Interlocked.Increment(ref done);
                if (n % 64 == 0 || n == content.Files.Count)
                    progress?.Report(new OperationProgress(n, content.Files.Count * 2, L.F("ClientStore.Archive.CopyingFiles0N0Of1", n, content.Files.Count, ReportWriter.FormatSize(b))));
            }, ct);
            foreach (string dir in content.Directories) Content.Files.CreateDirectory(PathGuard.ResolveInside(stagingFiles, dir));
            // Read every copy back before anything is published.
            done = 0;
            ContentStore.ForEachParallel(content.Files, null, file =>
            {
                string target = PathGuard.ResolveInside(stagingFiles, file.Path);
                string reread;
                using (var stream = StoreFiles.OpenSource(target)) reread = FileHashing.HashStream(stream, ct: ct);
                if (reread != file.Sha256) throw new IOException($"The copy of {file.Path} reads back differently (disk error?). Nothing was archived.");
                Content.Files.SetReadOnly(target);
                int n = Interlocked.Increment(ref done);
                if (n % 64 == 0 || n == content.Files.Count)
                    progress?.Report(new OperationProgress(content.Files.Count + n, content.Files.Count * 2, L.F("ClientStore.Archive.CheckingTheCopies0N0Of", n, content.Files.Count)));
            }, ct);
            ct.ThrowIfCancellationRequested();
            Content.Files.MoveDirectory(staging, versionDir);
            moved = true;
            var manifest = ClientManifest.Create(content, _clock(), ImportedBy);
            WriteManifest(versionDir, manifest);
            published = true;
            return (manifest, content.Files.Count, Interlocked.Read(ref bytes));
        }
        finally
        {
            if (!published)
            {
                TryQuietly(() => Content.Files.DeleteDirectory(staging));
                if (moved) TryQuietly(() => Content.Files.DeleteDirectory(versionDir));
            }
        }
    }

    private void WriteManifest(string versionDir, ClientManifest manifest)
    {
        string manifestPath = Path.Combine(versionDir, ClientManifest.FileName);
        Content.Files.WriteAtomic(manifestPath, manifest.ToBytes());
        if (ClientManifest.Read(manifestPath).Id != manifest.Id) throw new IOException("The manifest reads back differently (disk error?).");
    }

    /// <summary>Leftovers of interrupted independent archives (staging folders): removed at the next start, under the lock.</summary>
    private List<string> RecoverIndependent()
    {
        var notes = new List<string>();
        foreach (string server in new[] { ClientServers.Live, ClientServers.Test })
        {
            string dir = Path.Combine(Root, server);
            if (!Directory.Exists(dir)) continue;
            foreach (string sub in Directory.EnumerateDirectories(dir))
            {
                string name = Path.GetFileName(sub);
                bool staging = name.Contains(IncomingSuffix, StringComparison.Ordinal);
                bool unpublished = !staging && !File.Exists(Path.Combine(sub, ClientManifest.FileName)) && Directory.Exists(Path.Combine(sub, FilesFolder));
                if (!staging && !unpublished) continue;
                Content.Files.DeleteDirectory(sub);
                notes.Add(L.F("ClientStore.Archive.AnInterruptedArchive0WasRemoved", name));
            }
        }
        return notes;
    }

    // ------------------------------------------------------------------ leases (8.0)

    private sealed record LeaseInfo(string Key, string Purpose, int ProcessId, DateTime ProcessStartUtc, DateTime StartedUtc);

    /// <summary>
    /// Marks a revision as in use (a rebuild, a verification): until the returned object is disposed it
    /// cannot be removed, and its content counts as referenced. Leases of a process that ended are ignored.
    /// </summary>
    public IDisposable Lease(string key, string purpose)
    {
        using var process = Process.GetCurrentProcess();
        string file = Path.Combine(Root, LeasesFolder, Guid.NewGuid().ToString("N") + ".json");
        var info = new LeaseInfo(key, purpose, process.Id, process.StartTime.ToUniversalTime(), _clock());
        Content.Files.CreateDirectory(Path.GetDirectoryName(file)!);
        Content.Files.WriteAtomic(file, JsonSerializer.SerializeToUtf8Bytes(info, ClientManifest.Json));
        return new LeaseHandle(() => TryQuietly(() => Content.Files.DeleteFile(file)));
    }

    private sealed class LeaseHandle(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    /// <summary>Keys of revisions with a live lease (stale lease files are removed when possible).</summary>
    public IReadOnlySet<string> LeasedKeys()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string dir = Path.Combine(Root, LeasesFolder);
        if (!Directory.Exists(dir)) return result;
        foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
        {
            LeaseInfo? info = null;
            try { info = JsonSerializer.Deserialize<LeaseInfo>(File.ReadAllBytes(file), ClientManifest.Json); }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { /* unreadable: treated as stale */ }
            if (info != null && IsAlive(info)) result.Add(info.Key);
            else TryQuietly(() => Content.Files.DeleteFile(file));
        }
        return result;

        static bool IsAlive(LeaseInfo info)
        {
            try
            {
                using var p = Process.GetProcessById(info.ProcessId);
                return Math.Abs((p.StartTime.ToUniversalTime() - info.ProcessStartUtc).TotalSeconds) < 2;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }

    // ------------------------------------------------------------------ usage per revision (8.0)

    /// <summary>
    /// Space per usable revision: logical size, what only it uses (what deleting it can free) and what
    /// it shares. Objects are counted once however many revisions use them.
    /// </summary>
    public IReadOnlyList<RevisionUsage> RevisionUsages()
    {
        var manifests = new List<(ClientVersionEntry Entry, ClientManifest Manifest)>();
        foreach (var v in Registry.Versions.Where(v => v.IsUsable))
        {
            try { manifests.Add((v, ClientManifest.Read(v.ManifestPath(Root)))); }
            catch (InvalidDataException) { /* counted as damaged by Verify */ }
        }
        var users = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, m) in manifests.Where(x => x.Manifest.EffectiveStorage == ManifestStorage.Shared))
            foreach (string sha in m.Files.Select(f => f.Sha256).Distinct(StringComparer.Ordinal))
                users[sha] = users.GetValueOrDefault(sha) + 1;
        return manifests.Select(x =>
        {
            long logical = x.Manifest.TotalBytes;
            if (x.Manifest.EffectiveStorage == ManifestStorage.Independent) return new RevisionUsage(x.Entry.Key, ManifestStorage.Independent, logical, logical, 0);
            var distinct = x.Manifest.Files.GroupBy(f => f.Sha256, StringComparer.Ordinal).Select(g => (Sha: g.Key, Size: g.First().Size)).ToList();
            long unique = distinct.Where(d => users.GetValueOrDefault(d.Sha) == 1).Sum(d => d.Size);
            long shared = distinct.Where(d => users.GetValueOrDefault(d.Sha) > 1).Sum(d => d.Size);
            return new RevisionUsage(x.Entry.Key, ManifestStorage.Shared, logical, unique, shared);
        }).ToList();
    }
}
