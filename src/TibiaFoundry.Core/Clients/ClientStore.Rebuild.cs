using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Clients;

/// <summary>What a rebuild would do, checked before anything is written (8.0).</summary>
public sealed class RebuildPlan
{
    public required string Key { get; init; }
    public required ClientVersionEntry Entry { get; init; }
    public required ClientManifest Manifest { get; init; }
    /// <summary>The folder the revision is rebuilt into (new, or empty).</summary>
    public required string Target { get; init; }
    public required bool TargetExists { get; init; }
    public long LogicalBytes => Manifest.TotalBytes;
    /// <summary>Free space on the target's drive (null when it cannot be asked).</summary>
    public long? AvailableBytes { get; init; }
    /// <summary>A complete installation with nothing left out: only then the result is called a rebuilt installation.</summary>
    public bool IsInstallation => Manifest.IsCompleteInstallation;
    /// <summary>"Rebuild installation" for complete installations, else "Restore folder".</summary>
    public string Kind => IsInstallation ? L.T("ClientStore.Rebuild.RebuildInstallation") : L.T("ClientStore.Rebuild.RestoreFolder");
    /// <summary>Problems that stop the rebuild before it starts (missing content, not enough space); empty when it can start.</summary>
    public required IReadOnlyList<string> Blockers { get; init; }
}

/// <summary>The outcome of a rebuild or restore.</summary>
/// <param name="ContentComplete">Every file and empty folder the revision records was restored and read back with its recorded hash.</param>
/// <param name="InstallationComplete">Content complete, and the revision is a complete installation with nothing left out.</param>
public sealed record RebuildReport(string Key, string Label, string Kind, string Target, bool Success, bool ContentComplete, bool InstallationComplete,
    int Files, int Directories, long Bytes, TimeSpan Duration, IReadOnlyList<string> Problems, IReadOnlyList<string> Notes)
{
    /// <summary>The report as text (for the report file): never a claim that the client will run.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Kind}: {Label} ({Key})");
        sb.AppendLine(L.F("ClientStore.Rebuild.OutputFolder0", Target));
        sb.AppendLine(L.F("ClientStore.Rebuild.Result0", (Success ? "finished" : L.T("ClientStore.Rebuild.FailedNothingWasLeftInThe"))));
        sb.AppendLine(L.F("ClientStore.Rebuild.FilesRestoredAndVerified0N0", Files, ReportWriter.FormatSize(Bytes), Directories, Duration.TotalSeconds));
        sb.AppendLine(L.F("ClientStore.Rebuild.ContentCompleteEveryRecordedFileRestored", (ContentComplete ? "yes" : "no")));
        sb.AppendLine(L.F("ClientStore.Rebuild.CompleteInstallationTheRevisionLeftNothing", (InstallationComplete ? "yes" : "no")));
        sb.AppendLine(L.T("ClientStore.Rebuild.RunnableNotCheckedWhetherAClient"));
        foreach (string n in Notes) sb.AppendLine("Note: " + n);
        foreach (string p in Problems) sb.AppendLine("Problem: " + p);
        return sb.ToString();
    }
}

/// <summary>A rebuild that did not finish (the program stopped): its staging folder is still there.</summary>
public sealed record LeftoverRebuild(string Id, string StagingFolder, string Target, DateTime StartedUtc);

public sealed partial class ClientStore
{
    public const string RebuildsFolder = "Rebuilds";
    private const string PartialSuffix = ".tf-partial-";

    /// <summary>
    /// Checks a rebuild of a revision into <paramref name="target"/>: the target must be a new or empty
    /// folder outside the store and outside every Tibia client and registered folder; every file must be
    /// in the archive; the drive must have room. Nothing is written.
    /// </summary>
    public RebuildPlan PlanRebuild(string key, string target, IReadOnlyList<string> forbiddenRoots)
    {
        var entry = Registry.Version(key) ?? throw new InvalidOperationException($"The revision {key} is not in the archive.");
        string full = PathGuard.Normalize(target);
        if (PathGuard.Overlaps(full, Root)) throw new InvalidOperationException("Choose a folder outside the client archive.");
        foreach (string root in forbiddenRoots)
            if (!string.IsNullOrWhiteSpace(root) && PathGuard.Overlaps(full, root))
                throw new InvalidOperationException($"Choose a folder outside {root}: Tibia Foundry never writes into a Tibia client's folders or a registered client folder.");
        if (File.Exists(full)) throw new InvalidOperationException($"{full} is a file. Choose a folder.");
        bool exists = Directory.Exists(full);
        if (exists && Directory.EnumerateFileSystemEntries(full).Any())
            throw new InvalidOperationException($"{full} is not empty. A rebuild never replaces files: choose an empty or new folder (for example a new folder inside it).");
        var files = OpenVersionUnchecked(entry, out var blockers);
        long? available = null;
        try { if (Path.GetPathRoot(full) is { Length: > 0 } drive) available = new DriveInfo(drive).AvailableFreeSpace; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { /* unknown */ }
        var list = blockers.ToList();
        if (available is { } free && free < files.Manifest.TotalBytes + SpaceMargin)
            list.Add(L.F("ClientStore.Rebuild.NotEnoughFreeSpace0Is", ReportWriter.FormatSize(files.Manifest.TotalBytes), ReportWriter.FormatSize(SpaceMargin), ReportWriter.FormatSize(free)));
        return new RebuildPlan { Key = key, Entry = entry, Manifest = files.Manifest, Target = full, TargetExists = exists, AvailableBytes = available, Blockers = list };
    }

    /// <summary>
    /// Rebuilds (or restores) a revision from its own manifest and the content it references — never
    /// from an installed client. Files are written into a staging folder next to the target, with their
    /// recorded times and attributes and every empty folder; the staging folder is then listed (exactly
    /// the recorded entries) and every file is hashed again; only then it becomes the target. A failure
    /// or a cancel removes the staging folder and leaves the archive unchanged.
    /// </summary>
    public RebuildReport Rebuild(RebuildPlan plan, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        if (plan.Blockers.Count > 0) throw new InvalidOperationException(string.Join(" ", plan.Blockers));
        var watch = Stopwatch.StartNew();
        string parent = Path.GetDirectoryName(plan.Target) ?? throw new InvalidOperationException("Choose a folder, not a drive's root.");
        string id = Guid.NewGuid().ToString("N")[..8];
        string staging = Path.Combine(parent, Path.GetFileName(plan.Target) + PartialSuffix + id);
        var writer = new StoreFiles(parent);
        var problems = new ConcurrentBag<string>();
        string record = Path.Combine(Root, RebuildsFolder, id + ".json");
        bool published = false;
        using var lease = Lease(plan.Key, "rebuild");
        try
        {
            Content.Files.CreateDirectory(Path.GetDirectoryName(record)!);
            Content.Files.WriteAtomic(record, JsonSerializer.SerializeToUtf8Bytes(new LeftoverRebuild(id, staging, plan.Target, _clock()), ClientManifest.Json));
            var files = OpenVersion(plan.Key);
            var manifest = files.Manifest;
            writer.CreateDirectory(staging);
            int done = 0;
            long bytes = 0;
            int total = manifest.Files.Count * 2;
            ContentStore.ForEachParallel(manifest.Files, null, f =>
            {
                string source = files.PathOf(f.Path) ?? throw new InvalidDataException($"{f.Path} is not in the archive.");
                string target = PathGuard.ResolveInside(staging, f.Path);
                string written = writer.CopyIn(source, target, ct);
                if (written != f.Sha256) throw new ContentMismatchException($"{f.Path} is damaged in the archive (its content is not the recorded one); run Verify store. Nothing was left in the output folder.");
                if (f.LastWriteUtc is { } time) writer.RestoreWriteTime(target, time);
                long b = Interlocked.Add(ref bytes, f.Size);
                int n = Interlocked.Increment(ref done);
                if (n % 64 == 0 || n == manifest.Files.Count)
                    progress?.Report(new OperationProgress(n, total, L.F("ClientStore.Rebuild.RestoringFiles0N0Of1", n, manifest.Files.Count, ReportWriter.FormatSize(b))));
            }, ct);
            foreach (string dir in manifest.Directories ?? []) writer.CreateDirectory(PathGuard.ResolveInside(staging, dir));
            ct.ThrowIfCancellationRequested();

            // Verify: exactly the recorded entries, every file read back with its recorded hash.
            var expected = manifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            var found = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Select(p => PathGuard.ToPortable(staging, p)).ToList();
            foreach (string extra in found.Where(p => !expected.ContainsKey(p))) problems.Add(L.F("ClientStore.Rebuild.0NotInTheRevision", extra));
            foreach (string missing in expected.Keys.Except(found, StringComparer.OrdinalIgnoreCase)) problems.Add(L.F("ClientStore.Rebuild.0NotRestored", missing));
            foreach (string dir in manifest.Directories ?? [])
                if (!Directory.Exists(PathGuard.ResolveInside(staging, dir))) problems.Add(L.F("ClientStore.Rebuild.0TheEmptyFolderWasNot", dir));
            done = 0;
            ContentStore.ForEachParallel(manifest.Files, null, f =>
            {
                string target = PathGuard.ResolveInside(staging, f.Path);
                string reread;
                using (var stream = StoreFiles.OpenSource(target)) reread = FileHashing.HashStream(stream, ct: ct);
                if (reread != f.Sha256) problems.Add(L.F("ClientStore.Rebuild.0ReadsBackDifferentlyDiskError", f.Path));
                var attributes = ClientInventory.ParseAttributes(f.Attributes);
                if (attributes != FileAttributes.Normal) writer.SetAttributes(target, attributes);
                int n = Interlocked.Increment(ref done);
                if (n % 64 == 0 || n == manifest.Files.Count)
                    progress?.Report(new OperationProgress(manifest.Files.Count + n, total, L.F("ClientStore.Rebuild.Verifying0N0Of1N0", n, manifest.Files.Count)));
            }, ct);
            ct.ThrowIfCancellationRequested();
            if (!problems.IsEmpty) throw new IOException("The rebuilt files did not verify: " + problems.Order(StringComparer.Ordinal).First());

            // Publish: the staging folder becomes the target (an existing empty target receives its entries).
            if (!Directory.Exists(plan.Target)) writer.MoveDirectory(staging, plan.Target);
            else
            {
                if (Directory.EnumerateFileSystemEntries(plan.Target).Any()) throw new IOException($"{plan.Target} is no longer empty; nothing was replaced. The rebuilt files are removed.");
                foreach (string entry in Directory.EnumerateFileSystemEntries(staging))
                {
                    string to = Path.Combine(plan.Target, Path.GetFileName(entry));
                    if (Directory.Exists(entry)) writer.MoveDirectory(entry, to);
                    else if (!writer.MoveNoReplace(entry, to)) throw new IOException($"{to} appeared meanwhile; nothing was replaced.");
                }
                writer.DeleteDirectory(staging);
            }
            published = true;
            var notes = new List<string>();
            if (!manifest.IsCompleteInstallation)
            {
                if (manifest.Exclusions is { Count: > 0 } ex) notes.Add(L.F("ClientStore.Rebuild.TheRevisionLeftOut0On", L.Count(ex.Sum(e => e.Files), "Noun.file"), string.Join("; ", ex.Select(e => e.Rule))));
                if (manifest.EffectiveScope != ManifestScopes.CompleteInstallation) notes.Add(L.F("ClientStore.Rebuild.ThisIsA0RevisionThe", ManifestScopes.Display(manifest.EffectiveScope).ToLowerInvariant()));
                if (manifest.Problems is { Count: > 0 } pr) notes.Add(L.F("ClientStore.Rebuild.TheRevisionWasArchivedWithout0", pr.Count));
            }
            return new RebuildReport(plan.Key, plan.Entry.Label, plan.Kind, plan.Target, true, true, manifest.IsCompleteInstallation, manifest.Files.Count,
                manifest.Directories?.Count ?? 0, Interlocked.Read(ref bytes), watch.Elapsed, [], notes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
        {
            if (!published) RemoveStaging(writer, staging);
            if (ex is OperationCanceledException) throw;
            var list = problems.Order(StringComparer.Ordinal).ToList();
            if (list.Count == 0) list.Add(ex.Message);
            return new RebuildReport(plan.Key, plan.Entry.Label, plan.Kind, plan.Target, false, false, false, 0, 0, 0, watch.Elapsed, list, []);
        }
        finally
        {
            TryQuietly(() => Content.Files.DeleteFile(record));
        }
    }

    /// <summary>Removes a staging folder; when it cannot be removed it is renamed ".tf-failed-…" so it never looks like a result.</summary>
    private static void RemoveStaging(StoreFiles writer, string staging)
    {
        try
        {
            writer.DeleteDirectory(staging);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryQuietly(() => writer.MoveDirectory(staging, staging.Replace(PartialSuffix, ".tf-failed-", StringComparison.Ordinal)));
        }
    }

    /// <summary>Rebuilds that did not finish because the program stopped (their staging folder is still there).</summary>
    public IReadOnlyList<LeftoverRebuild> LeftoverRebuilds()
    {
        var result = new List<LeftoverRebuild>();
        string dir = Path.Combine(Root, RebuildsFolder);
        if (!Directory.Exists(dir)) return result;
        foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var r = JsonSerializer.Deserialize<LeftoverRebuild>(File.ReadAllBytes(file), ClientManifest.Json);
                if (r != null && Directory.Exists(r.StagingFolder) && Path.GetFileName(r.StagingFolder).Contains(PartialSuffix, StringComparison.Ordinal)) result.Add(r);
                else if (r == null || !Directory.Exists(r.StagingFolder)) TryQuietly(() => Content.Files.DeleteFile(file));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Unreadable record: left for the user to look at.
            }
        }
        return result;
    }

    /// <summary>Removes the staging folder of an unfinished rebuild (only a folder this store recorded and named ".tf-partial-…").</summary>
    public void RemoveLeftoverRebuild(LeftoverRebuild leftover)
    {
        if (LeftoverRebuilds().FirstOrDefault(r => r.Id == leftover.Id) is not { } known) return;
        RemoveStaging(new StoreFiles(Path.GetDirectoryName(known.StagingFolder)!), known.StagingFolder);
        TryQuietly(() => Content.Files.DeleteFile(Path.Combine(Root, RebuildsFolder, known.Id + ".json")));
    }

    /// <summary>The files of a revision, with what is missing instead of an exception (for a plan).</summary>
    private ManifestClientFiles OpenVersionUnchecked(ClientVersionEntry entry, out IReadOnlyList<string> blockers)
    {
        try
        {
            blockers = [];
            return OpenVersion(entry.Key);
        }
        catch (InvalidDataException ex)
        {
            var manifest = ClientManifest.Read(entry.ManifestPath(Root));
            blockers = [ex.Message];
            return new ManifestClientFiles(entry, manifest, Content, Root);
        }
    }
}
