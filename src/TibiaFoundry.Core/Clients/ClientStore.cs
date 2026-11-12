using System.Collections.Concurrent;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Clients;

/// <summary>Result of an import.</summary>
/// <param name="AlreadyStored">The same content was stored before: no new version was created.</param>
public sealed record ImportResult(ClientVersionEntry Version, bool AlreadyStored, int NewObjects, long NewBytes, TimeSpan Duration, IReadOnlyList<string> Notes);

/// <summary>Space used by the store.</summary>
/// <param name="LogicalBytes">What every usable version would take as separate copies.</param>
/// <param name="StoredBytes">What the shared objects take on disk.</param>
/// <param name="ReclaimableBytes">Objects no manifest references (freed by "Reclaim space").</param>
public sealed record StoreUsage(int Versions, long LogicalBytes, int Objects, long StoredBytes, long ReclaimableBytes,
    IReadOnlyList<(string Kind, int Objects, long Bytes)> ByKind)
{
    /// <summary>8.0: what the independent copies take (each revision's own files; never shared).</summary>
    public long IndependentBytes { get; init; }

    /// <summary>Everything the revisions take on disk: the shared objects in use, counted once, plus the independent copies.</summary>
    public long PhysicalBytes => StoredBytes - ReclaimableBytes + IndependentBytes;

    public double SharedPercent => LogicalBytes == 0 ? 0 : Math.Max(0, 100.0 * (LogicalBytes - PhysicalBytes) / LogicalBytes);
}

/// <summary>Result of verifying versions against their objects.</summary>
public sealed record VerifyResult(int Versions, int Objects, IReadOnlyList<string> Problems);

/// <summary>
/// The client store: registrations, imported versions (one complete manifest each) and the shared
/// content-addressed objects. Every change holds the store lock; an import publishes its manifest only
/// after every object it needs is stored and verified, so a version is either complete or does not
/// exist. Removing a version never removes objects; <see cref="ReclaimSpace"/> removes only objects
/// that no remaining manifest references. The client installations are only ever read.
/// </summary>
public sealed partial class ClientStore
{
    public const string FolderName = "Clients";

    /// <summary>Objects younger than this are never removed by a cleanup (an extra guard next to the lock).</summary>
    public static readonly TimeSpan MinimumObjectAge = TimeSpan.FromHours(1);

    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);
    private readonly Func<DateTime> _clock;
    private readonly ConcurrentDictionary<string, ManifestClientFiles> _opened = new(StringComparer.OrdinalIgnoreCase);

    private ClientStore(string root, string? programDirectory, ClientRegistry registry, IReadOnlyList<string> notes, Func<DateTime> clock)
    {
        Content = new ContentStore(root);
        Root = Content.Root;
        ProgramDirectory = programDirectory;
        Registry = registry;
        OpenNotes = notes;
        _clock = clock;
    }

    /// <summary>The <c>Clients</c> folder.</summary>
    public string Root { get; }
    public ContentStore Content { get; }

    /// <summary>Portable edition: the program folder (folders on its drive are also remembered relative to it). Null for the folder edition.</summary>
    public string? ProgramDirectory { get; }

    public ClientRegistry Registry { get; private set; }

    /// <summary>What opening the store found and repaired (interrupted imports or cleanups, a rebuilt registry).</summary>
    public IReadOnlyList<string> OpenNotes { get; }

    /// <summary>Shown in "importedBy" of new manifests.</summary>
    public string ImportedBy { get; init; } = AppIdentity.NameAndVersion;

    /// <summary>
    /// Opens (or creates) the store. When no other window holds the lock, interrupted imports and
    /// cleanups are put in order first. A damaged registry is kept aside and rebuilt from the manifests.
    /// </summary>
    public static ClientStore Open(string root, string? programDirectory = null, Func<DateTime>? clock = null)
    {
        clock ??= () => DateTime.UtcNow;
        var files = new StoreFiles(root);
        files.CreateDirectory(files.Root);
        var notes = new List<string>();
        var registry = ClientRegistry.Load(files.Root, notes, out bool damaged);
        if (damaged)
        {
            string file = Path.Combine(files.Root, ClientRegistry.FileName);
            files.MoveNoReplace(file, file + ".damaged-" + clock().ToString("yyyyMMddHHmmss"));
        }
        var store = new ClientStore(files.Root, programDirectory, registry, notes, clock);
        try
        {
            using (store.Content.Lock(TimeSpan.Zero))
            {
                if (store.ReadAllManifests(out var references, out _))
                    notes.AddRange(store.RecoverLocked(references));
                if (damaged || notes.Count > 0) store.SaveRegistry();
            }
        }
        catch (StoreBusyException)
        {
            notes.Add(L.T("ClientStore.AnotherTibiaFoundryWindowIsUsing"));
        }
        return store;
    }

    private IReadOnlyList<string> RecoverLocked(IReadOnlySet<string> references)
    {
        // Objects of imports that never published a manifest are removed with them ("no trace").
        var orphans = Content.PendingImportHashes().Where(h => !references.Contains(h)).ToList();
        foreach (string h in orphans) Content.Files.DeleteFile(Content.ObjectPath(h));
        var notes = Content.Recover(references).ToList();
        notes.AddRange(RecoverIndependent());
        // Version folders without a manifest are leftovers of an import interrupted while publishing.
        foreach (string server in new[] { ClientServers.Live, ClientServers.Test })
        {
            string dir = Path.Combine(Root, server);
            if (!Directory.Exists(dir)) continue;
            foreach (string versionDir in Directory.EnumerateDirectories(dir))
                if (!File.Exists(Path.Combine(versionDir, ClientManifest.FileName)) && !Directory.EnumerateFileSystemEntries(versionDir).Any(e => !Path.GetFileName(e).StartsWith('.')))
                    Content.Files.DeleteDirectory(versionDir);
        }
        return notes;
    }

    internal void SaveRegistry() => Content.Files.WriteAtomic(Path.Combine(Root, ClientRegistry.FileName), Registry.ToBytes());

    /// <summary>Reads the store's registry again (after another window changed it).</summary>
    public void Reload()
    {
        var notes = new List<string>();
        Registry = ClientRegistry.Load(Root, notes, out _);
        _opened.Clear();
    }

    /// <summary>
    /// Reads every manifest the registry knows. False (with the reason) when one cannot be read:
    /// then the references are unknown and nothing may be removed.
    /// </summary>
    private bool ReadAllManifests(out HashSet<string> references, out string? problem)
    {
        references = new HashSet<string>(StringComparer.Ordinal);
        problem = null;
        foreach (string server in new[] { ClientServers.Live, ClientServers.Test })
        {
            string dir = Path.Combine(Root, server);
            if (!Directory.Exists(dir)) continue;
            foreach (string versionDir in Directory.EnumerateDirectories(dir))
            {
                string file = Path.Combine(versionDir, ClientManifest.FileName);
                if (!File.Exists(file)) continue;
                try
                {
                    foreach (var f in ClientManifest.Read(file).Files) references.Add(f.Sha256);
                }
                catch (InvalidDataException ex)
                {
                    problem = ex.Message;
                    return false;
                }
            }
        }
        return true;
    }

    // ------------------------------------------------------------------ registrations

    /// <summary>Remembers a client folder (nothing is copied). An already registered folder of the same server is returned as it is.</summary>
    /// <param name="sourceType">8.0: the kind of folder (<see cref="SourceTypes"/>; a standalone folder by default).</param>
    /// <param name="reconstructedFrom">8.0: for a rebuilt installation, the key of the revision it was rebuilt from.</param>
    public ClientRegistration RegisterFolder(string folder, string server, string? label, string? sourceType = null, string? reconstructedFrom = null)
    {
        if (!ClientServers.IsKnown(server)) throw new ArgumentException($"Unknown server \"{server}\".", nameof(server));
        string full = PathGuard.Normalize(folder);
        if (PathGuard.Overlaps(full, Root)) throw new InvalidOperationException("This folder is inside the client store (or contains it). Register the Tibia client's own folder instead.");
        using var held = Content.Lock(LockWait);
        var existing = Registry.Registrations.FirstOrDefault(r => !r.IsAutomatic && r.Server == server && r.ResolveSource(ProgramDirectory) is { } p && string.Equals(PathGuard.Normalize(p), full, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;
        var registration = new ClientRegistration
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = ClientRegistration.Folder,
            Server = server,
            Label = string.IsNullOrWhiteSpace(label) ? Path.GetFileName(full) : label.Trim(),
            SourceType = sourceType ?? SourceTypes.StandaloneFolder,
            ReconstructedFrom = reconstructedFrom,
        };
        registration.SetSource(full, ProgramDirectory);
        Registry.Registrations.Add(registration);
        SaveRegistry();
        return registration;
    }

    /// <summary>Points a registration at its folder again (after it moved). Its versions are not touched.</summary>
    public void Relocate(string registrationId, string folder)
    {
        using var held = Content.Lock(LockWait);
        var r = Registry.Registration(registrationId) ?? throw new InvalidOperationException("This registration no longer exists.");
        if (r.IsAutomatic) throw new InvalidOperationException("The installed clients are found automatically; choose their folders in Settings → Tibia client.");
        r.SetSource(folder, ProgramDirectory);
        SaveRegistry();
    }

    public void RenameRegistration(string registrationId, string label) => Change(() =>
    {
        var r = Registry.Registration(registrationId) ?? throw new InvalidOperationException("This registration no longer exists.");
        r.Label = Label(label);
    });

    /// <summary>Changes only the label: the key, the manifest and every hash stay the same.</summary>
    public void RenameVersion(string key, string label) => Change(() =>
    {
        var v = Registry.Version(key) ?? throw new InvalidOperationException("This version no longer exists.");
        v.Label = Label(label);
    });

    /// <summary>Records what a scan saw (for the "may have changed" hint).</summary>
    public void RecordScan(string registrationId, SourceSnapshot snapshot) => Change(() =>
    {
        if (Registry.Registration(registrationId) is { } r) { r.LastScanUtc = _clock(); r.LastSource = snapshot; }
    });

    /// <summary>"Don't ask for this version" (8.0): this content is not offered for archiving again. The version is also kept for 7.3's reminder.</summary>
    public void DismissChange(string registrationId, string fingerprint, string? clientVersion) => Change(() =>
    {
        if (Registry.Registration(registrationId) is not { } r) return;
        if (!r.DismissedFingerprints.Contains(fingerprint)) r.DismissedFingerprints.Add(fingerprint);
        r.ReminderDismissedFor = clientVersion;
    });

    private void Change(Action change)
    {
        using var held = Content.Lock(LockWait);
        change();
        SaveRegistry();
    }

    private static string Label(string label) =>
        string.IsNullOrWhiteSpace(label) ? throw new ArgumentException("A label cannot be empty.") : label.Trim().Length > 120 ? label.Trim()[..120] : label.Trim();

    /// <summary>Forgets a registered folder. Its imported versions stay unless <paramref name="alsoVersions"/>; the installed (automatic) entries cannot be removed.</summary>
    public void RemoveRegistration(string registrationId, bool alsoVersions, IReadOnlySet<string> activeKeys)
    {
        using var held = Content.Lock(LockWait);
        var r = Registry.Registration(registrationId) ?? throw new InvalidOperationException("This registration no longer exists.");
        if (r.IsAutomatic) throw new InvalidOperationException("The installed (automatic) clients cannot be removed.");
        if (alsoVersions)
        {
            if (r.Versions.FirstOrDefault(activeKeys.Contains) is { } active)
                throw new InvalidOperationException($"{active} is the client in use. Switch to another client first.");
            if (r.Versions.FirstOrDefault(LeasedKeys().Contains) is { } leased)
                throw new InvalidOperationException($"{leased} is being used right now (a rebuild or a verification). Remove it when that has finished.");
            foreach (string key in r.Versions.ToList()) RemoveVersionLocked(key);
        }
        else
        {
            foreach (var v in Registry.VersionsOf(r)) v.RegistrationId = null;
        }
        Registry.Registrations.Remove(r);
        SaveRegistry();
    }

    // ------------------------------------------------------------------ versions

    /// <summary>The usable version with exactly this content in this category, or null.</summary>
    public ClientVersionEntry? FindStored(string server, string fingerprint) =>
        Registry.Versions.FirstOrDefault(v => v.Server == server && v.Fingerprint == fingerprint && v.IsUsable);

    /// <summary>
    /// Imports a scanned installation as a new version (or returns the stored one with the same
    /// content). Objects are copied into the store only when their content is new; each is hashed
    /// while it is copied and again after, and must equal the scan. The manifest is published last.
    /// A failure or cancellation removes everything this import added.
    /// </summary>
    /// <summary>
    /// Version evidence from the store (8.0): the SHA-256 of each usable revision's assets.json,
    /// catalog-content.json and appearances data → its version, and "name:&lt;appearances file name&gt;" →
    /// its version (weak). Revisions without a version add nothing.
    /// </summary>
    public IReadOnlyDictionary<string, string> KnownVersionContent()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in Registry.Versions.Where(v => v.IsUsable && v.ClientVersion != null))
        {
            ClientManifest m;
            try { m = ClientManifest.Read(v.ManifestPath(Root)); }
            catch (InvalidDataException) { continue; }
            foreach (var f in m.Files)
            {
                string name = f.Path[(f.Path.LastIndexOf('/') + 1)..];
                bool appearances = f.Path.StartsWith("assets/appearances-", StringComparison.OrdinalIgnoreCase) && f.Path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
                if (f.Path.Equals(ClientScanner.AssetsJson, StringComparison.OrdinalIgnoreCase) || f.Path.Equals("assets/" + Compatibility.TibiaFormat.AssetCatalogFile, StringComparison.OrdinalIgnoreCase) || appearances)
                    result.TryAdd(f.Sha256, v.ClientVersion!);
                if (appearances) result.TryAdd("name:" + name, v.ClientVersion!);
            }
        }
        return result;
    }

    /// <summary>
    /// Stores a 7.3 scan (the files Tibia Foundry reads) as a revision of that scope. 8.0 archives
    /// whole folders with <see cref="Archive"/>; this keeps 7.3 revisions reproducible (tests and
    /// comparisons with them).
    /// </summary>
    public ImportResult Import(ClientScan scan, string server, string? label, string registrationId, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        if (!scan.CanImport) throw new InvalidOperationException("This installation cannot be imported: " + string.Join(" ", scan.Problems.Take(3)) + (scan.Problems.Count > 3 ? L.F("ClientStore.And0More", scan.Problems.Count - 3) : ""));
        var content = new ManifestContent
        {
            Server = server, ClientVersion = scan.ClientVersion, Files = scan.Files, Scope = ManifestScopes.TibiaFoundryFiles, Storage = ManifestStorage.Shared,
            SourceType = SourceTypes.InstalledPackage, Source = new ManifestSource(registrationId, scan.FileSet.FileList, scan.FileSet.AssetsJsonSha256),
        };
        return Publish(content, scan.Fingerprint, scan.SourcePath, server, label, registrationId, scan.Snapshot, "Client_", scan.ClientVersion, scan.FileSet.Notes, progress, ct);
    }

    private static void TryQuietly(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* left for the recovery at the next start */ }
    }

    /// <summary>
    /// "Live_Server/Install_15.33.9d148f" (prefix "Install_", "Folder_" or the 7.3 "Client_"), with
    /// "_&lt;8 hex of the fingerprint&gt;" when that name is taken by other content.
    /// </summary>
    private string NewKey(string server, string prefix, string? name, string fingerprint)
    {
        name = name == null ? prefix + "unknown_" + fingerprint[..8] : prefix + Sanitize(name);
        foreach (string candidate in new[] { name, name + "_" + fingerprint[..8], name + "_" + fingerprint[..16], name + "_" + fingerprint })
        {
            string key = server + "/" + candidate;
            if (Registry.Version(key) == null && !Directory.Exists(Path.Combine(Root, server, candidate))) return key;
        }
        throw new IOException("No free folder name for this version in the store.");
    }

    private static string Sanitize(string text)
    {
        var chars = text.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray();
        string s = new string(chars).Trim('.', ' ');
        return s.Length == 0 ? "unknown" : s.Length > 60 ? s[..60] : s;
    }

    /// <summary>The files of a stored version (reads its manifest; cached).</summary>
    public ManifestClientFiles OpenVersion(string key)
    {
        if (_opened.TryGetValue(key, out var cached)) return cached;
        var entry = Registry.Version(key) ?? throw new InvalidOperationException($"The version {key} is not in the store.");
        var manifest = ClientManifest.Read(entry.ManifestPath(Root));
        if (manifest.Server != entry.Server) throw new InvalidDataException($"The manifest of {key} belongs to {manifest.Server}.");
        string versionFolder = Path.GetDirectoryName(entry.ManifestPath(Root))!;
        var missing = manifest.EffectiveStorage == ManifestStorage.Independent
            ? manifest.Files.Where(f => new FileInfo(PathGuard.ResolveInside(Path.Combine(versionFolder, FilesFolder), f.Path)) is var i && (!i.Exists || i.Length != f.Size)).ToList()
            : manifest.Files.Where(f => !Content.Contains(f.Sha256, f.Size)).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException($"{L.Count(missing.Count, "Noun.file")} of {entry.Label} are missing in the store (for example {missing[0].Path}). Run Verify store; import the client again to repair it.");
        var files = new ManifestClientFiles(entry, manifest, Content, Root);
        _opened[key] = files;
        return files;
    }

    /// <summary>
    /// Removes a version's manifest and registry entry. Shared objects are not touched: they are freed
    /// only by <see cref="ReclaimSpace"/>, and only when no other manifest references them.
    /// </summary>
    public void RemoveVersion(string key, IReadOnlySet<string> activeKeys)
    {
        if (activeKeys.Contains(key)) throw new InvalidOperationException("This is the client in use. Switch to another client first.");
        if (LeasedKeys().Contains(key)) throw new InvalidOperationException("This revision is being used right now (a rebuild or a verification). Remove it when that has finished.");
        using var held = Content.Lock(LockWait);
        RemoveVersionLocked(key);
        SaveRegistry();
    }

    private void RemoveVersionLocked(string key)
    {
        var entry = Registry.Version(key) ?? throw new InvalidOperationException($"The version {key} is not in the store.");
        string dir = Path.GetDirectoryName(entry.ManifestPath(Root))!;
        Content.Files.DeleteFile(Path.Combine(dir, ClientManifest.FileName));
        Content.Files.DeleteDirectory(dir);
        Registry.Versions.Remove(entry);
        foreach (var r in Registry.Registrations) r.Versions.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        _opened.TryRemove(key, out _);
    }

    /// <summary>
    /// Removes objects no manifest references. Refused when any manifest cannot be read (then it is
    /// unknown what is still needed). Objects written in the last hour are kept.
    /// </summary>
    public CleanupResult ReclaimSpace(CancellationToken ct = default)
    {
        using var held = Content.Lock(LockWait);
        if (!ReadAllManifests(out var references, out string? problem))
            throw new InvalidOperationException("Nothing was removed: a manifest cannot be read, so it is unknown which files are still needed. " + problem);
        return Content.Cleanup(references, MinimumObjectAge, _clock(), ct);
    }

    /// <summary>Rehashes the objects of the given versions (all when null) and marks damaged versions.</summary>
    public VerifyResult Verify(IReadOnlyCollection<string>? keys = null, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        using var held = Content.Lock(LockWait);
        var problems = new List<string>();
        var versions = Registry.Versions.Where(v => keys == null || keys.Contains(v.Key, StringComparer.OrdinalIgnoreCase)).ToList();
        var checkedObjects = new ConcurrentDictionary<string, string?>(StringComparer.Ordinal);
        int total = 0;
        foreach (var v in versions)
        {
            ClientManifest manifest;
            try
            {
                manifest = ClientManifest.Read(v.ManifestPath(Root));
            }
            catch (InvalidDataException ex)
            {
                v.Status = File.Exists(v.ManifestPath(Root)) ? VersionStatus.Damaged : VersionStatus.Missing;
                v.Problem = ex.Message;
                problems.Add($"{v.Label}: {ex.Message}");
                continue;
            }
            int done = 0;
            var bad = new ConcurrentBag<string>();
            string versionFolder = Path.GetDirectoryName(v.ManifestPath(Root))!;
            bool independent = manifest.EffectiveStorage == ManifestStorage.Independent;
            ContentStore.ForEachParallel(manifest.Files, null, f =>
            {
                string? why = independent
                    ? VerifyCopy(PathGuard.ResolveInside(Path.Combine(versionFolder, FilesFolder), f.Path), f, ct)
                    : checkedObjects.GetOrAdd(f.Sha256, h => Content.VerifyObject(h, f.Size, ct));
                if (why != null) bad.Add($"{f.Path}: {why}");
                int n = Interlocked.Increment(ref done);
                if (n % 256 == 0 || n == manifest.Files.Count) progress?.Report(new OperationProgress(n, manifest.Files.Count, L.F("ClientStore.Verifying01N0Of2", v.Label, n, manifest.Files.Count)));
            }, ct);
            total += manifest.Files.Count;
            if (bad.IsEmpty) { v.Status = VersionStatus.Imported; v.Problem = null; }
            else
            {
                v.Status = VersionStatus.Damaged;
                v.Problem = L.Count(bad.Count, "ClientStore.FilesMissingOrDamaged", bad.Order(StringComparer.Ordinal).First());
                problems.Add($"{v.Label}: {v.Problem}");
            }
            _opened.TryRemove(v.Key, out _);
        }
        SaveRegistry();
        return new VerifyResult(versions.Count, total, problems);
    }

    /// <summary>Why an independent copy does not have its recorded content, or null.</summary>
    private static string? VerifyCopy(string path, ManifestFile f, CancellationToken ct)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return "missing";
            if (info.Length != f.Size) return L.T("ClientStore.AnotherSize");
            using var stream = StoreFiles.OpenSource(path);
            return FileHashing.HashStream(stream, ct: ct) == f.Sha256 ? null : L.T("ClientStore.DamagedAnotherContent");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "unreadable: " + ex.Message;
        }
    }

    /// <summary>Space used: what the versions would take as copies, what the objects take, and what is reclaimable.</summary>
    public StoreUsage Usage()
    {
        var objects = Content.EnumerateObjects().ToDictionary(o => o.Sha256, o => o.Size, StringComparer.Ordinal);
        var referenced = new Dictionary<string, string>(StringComparer.Ordinal);
        long logical = 0, independent = 0;
        int versions = 0;
        foreach (var v in Registry.Versions.Where(v => v.IsUsable))
        {
            try
            {
                var m = ClientManifest.Read(v.ManifestPath(Root));
                versions++;
                logical += m.TotalBytes;
                if (m.EffectiveStorage == ManifestStorage.Independent) independent += m.TotalBytes;
                else foreach (var f in m.Files) referenced.TryAdd(f.Sha256, f.Kind);
            }
            catch (InvalidDataException)
            {
                // Counted as damaged by Verify; its objects are not counted as referenced here.
            }
        }
        long stored = objects.Values.Sum();
        long reclaimable = objects.Where(o => !referenced.ContainsKey(o.Key)).Sum(o => o.Value);
        var byKind = referenced.Where(r => objects.ContainsKey(r.Key)).GroupBy(r => r.Value)
            .Select(g => (g.Key, g.Count(), g.Sum(r => objects[r.Key]))).OrderByDescending(x => x.Item3).ToList();
        return new StoreUsage(versions, logical, objects.Count, stored, reclaimable, byKind) { IndependentBytes = independent };
    }

    // ------------------------------------------------------------------ move

    /// <summary>
    /// Copies the whole store to <paramref name="newRoot"/> (empty or new) and verifies every object
    /// there. The old store is unchanged: switch the setting, then call <see cref="DeleteStore"/> on it.
    /// A marker file makes an interrupted copy recognizable, so it can be started again.
    /// </summary>
    public static void CopyStore(string oldRoot, string newRoot, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        string from = PathGuard.Normalize(oldRoot), to = PathGuard.Normalize(newRoot);
        if (PathGuard.Overlaps(from, to)) throw new InvalidOperationException("The new store folder must not be inside the current one (or contain it).");
        const string marker = ".move-in-progress";
        var writer = new StoreFiles(to);
        if (Directory.Exists(to) && Directory.EnumerateFileSystemEntries(to).Any())
        {
            if (!File.Exists(Path.Combine(to, marker))) throw new InvalidOperationException("The new store folder is not empty. Choose an empty or new folder.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(to))
                if (Directory.Exists(entry)) writer.DeleteDirectory(entry); else writer.DeleteFile(entry);
        }
        var source = new ContentStore(from);
        using var held = source.Lock(LockWait);
        writer.CreateDirectory(to);
        writer.WriteAtomic(Path.Combine(to, marker), "Tibia Foundry is moving the client store here."u8.ToArray());
        var all = Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)
            .Where(f => !string.Equals(Path.GetFileName(f), ContentStore.LockFile, StringComparison.OrdinalIgnoreCase)
                        && !PathGuard.IsSameOrInside(source.IncomingRoot, f) && !PathGuard.IsSameOrInside(source.TrashRoot, f)
                        && !PathGuard.IsSameOrInside(Path.Combine(from, LeasesFolder), f)).ToList();
        int done = 0;
        foreach (string file in all)
        {
            ct.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(from, file);
            string destination = Path.Combine(to, relative);
            string written = writer.CopyIn(file, destination, ct);
            string name = Path.GetFileName(file);
            if (ContentStore.IsHash(name) && PathGuard.IsInside(source.ObjectsRoot, file))
            {
                if (written != name) throw new ContentMismatchException($"The object {name[..12]}… is damaged in the current store; run Verify store first.");
                writer.SetReadOnly(destination);
            }
            else if (relative.Split(Path.DirectorySeparatorChar) is [_, _, FilesFolder, ..]) writer.SetReadOnly(destination);   // an independent copy
            if (++done % 256 == 0 || done == all.Count) progress?.Report(new OperationProgress(done, all.Count, L.F("ClientStore.CopyingTheStore0N0Of", done, all.Count)));
        }
        // The copy must open and verify on its own before it is used.
        var copy = Open(to);
        var check = copy.Verify(null, null, ct);
        if (check.Problems.Count > 0) throw new IOException("The copied store did not verify: " + check.Problems[0]);
        writer.DeleteFile(Path.Combine(to, marker));
    }

    /// <summary>Deletes a store folder (after it was copied elsewhere and the setting switched).</summary>
    public static void DeleteStore(string root)
    {
        string full = PathGuard.Normalize(root);
        if (!File.Exists(Path.Combine(full, ClientRegistry.FileName)) && !Directory.Exists(Path.Combine(full, ContentStore.SharedFolder)))
            throw new InvalidOperationException($"{full} does not look like a client store; nothing was deleted.");
        var writer = new StoreFiles(full);
        foreach (var entry in Directory.EnumerateFileSystemEntries(full))
            if (Directory.Exists(entry)) writer.DeleteDirectory(entry); else writer.DeleteFile(entry);
    }
}
