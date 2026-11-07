using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileMetadata.Providers;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.FileMetadata;

/// <summary>How changes are saved.</summary>
public sealed record MetadataSaveOptions
{
    /// <summary>Where a verified copy of each original goes before it is replaced (Tibia Foundry's backups; restorable with Scripts → Backup and Restore). Null: no backup.</summary>
    public BackupStore? Backups { get; init; }
    /// <summary>Keep the file's "modified" time as it was (unless it is one of the changes).</summary>
    public bool KeepModifiedTime { get; init; } = true;
    /// <summary>The operation named in the backup ("File Metadata: save").</summary>
    public string Operation { get; init; } = L.T("MetadataEngine.FileMetadata");
}

public enum MetadataSaveStatus { Saved, Unchanged, Skipped, Failed, Cancelled }

/// <summary>What happened to one file.</summary>
public sealed record MetadataSaveResult(string Path, MetadataSaveStatus Status, string Message)
{
    public IReadOnlyList<string> Changed { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    /// <summary>Fields that were not changed in this file, with the reason (batch edits of mixed formats).</summary>
    public IReadOnlyList<string> SkippedFields { get; init; } = [];
    public string? BackupPath { get; init; }
    /// <summary>The file as it is now (after a save), or null.</summary>
    public MetadataSnapshot? Snapshot { get; init; }
}

/// <summary>
/// The metadata tool's engine (8.6): picks the provider of a file, reads it (embedded and file
/// system metadata), and saves changes safely:
/// <list type="number">
/// <item>every value is normalized and validated; only fields the file can store are written;</item>
/// <item>a file changed by another program since it was read is refused;</item>
/// <item>the changes are written to a hidden copy next to the file, which is read back: every
/// changed field must read as intended and every other field must be unchanged;</item>
/// <item>optionally the original is backed up (verified copy), then the copy replaces it in one
/// step; the original is never half-written, and a failure leaves it as it was.</item>
/// </list>
/// </summary>
public sealed class MetadataEngine
{
    public MetadataEngine(IReadOnlyList<IMetadataProvider>? providers = null) => Providers = providers ?? DefaultProviders();

    /// <summary>In the order they are asked; the first that handles a file reads it.</summary>
    public IReadOnlyList<IMetadataProvider> Providers { get; }

    public static IReadOnlyList<IMetadataProvider> DefaultProviders() =>
    [
        new TibiaMetadataProvider(), new OpenXmlMetadataProvider(), new EpubMetadataProvider(), new PdfMetadataProvider(), new SvgMetadataProvider(),
        new ZipMetadataProvider(), new TarMetadataProvider(), new PortableExecutableProvider(), new TagLibMetadataProvider(), new RasterImageProvider(),
    ];

    /// <summary>The provider of a file, or null (then only the file system part and the format identification are available).</summary>
    public IMetadataProvider? ProviderFor(string path, FormatIdentification id) => Providers.FirstOrDefault(p => p.CanHandle(path, id));

    /// <summary>Reads a file. Never throws for a damaged or unsupported file: the snapshot then carries the errors.</summary>
    public MetadataSnapshot Load(string path, CancellationToken ct = default)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
            return new MetadataSnapshot { Path = path, Format = "Missing", ProviderId = "", ProviderName = "", Capabilities = MetadataCapabilities.None, Fields = [], Errors = [L.T("MetadataEngine.TheFileDoesNotExistIt")] };
        var errors = new List<string>();
        var warnings = new List<string>();
        IReadOnlyList<MetadataField> fsFields = [];
        FileStamp? stamp = null;
        FormatIdentification id = new(null, FormatEvidence.Name, "");
        try
        {
            fsFields = FileSystemMetadata.Read(path);
            stamp = FileStamp.Of(path);
            id = FileSignatures.Identify(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(L.T("MetadataEngine.TheFileCannotBeRead") + ex.Message);
        }
        var provider = errors.Count == 0 ? ProviderFor(path, id) : null;
        string format = id.Format?.Name ?? (Path.GetExtension(path) is { Length: > 0 } ext ? ext.TrimStart('.').ToUpperInvariant() + " file" : "Unknown");
        var fields = new List<MetadataField>();
        var capabilities = errors.Count > 0 ? MetadataCapabilities.None : MetadataCapabilities.Identify | MetadataCapabilities.FileSystem | MetadataCapabilities.Compare | MetadataCapabilities.Export;
        if (provider != null)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var read = provider.Read(path, ct);
                format = read.Format;
                fields.AddRange(read.Fields);
                warnings.AddRange(read.Warnings);
                capabilities = provider.CapabilitiesFor(path);
                if (!fields.Any(f => f.Editable || f.Removable)) capabilities &= ~(MetadataCapabilities.Edit | MetadataCapabilities.Batch | MetadataCapabilities.Remove);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or FormatException
                                           or ArgumentException or System.Xml.XmlException or InvalidOperationException or IndexOutOfRangeException or OverflowException
                                           or ImageMagick.MagickException or BadImageFormatException or System.Text.Json.JsonException)
            {
                errors.Add(L.F("MetadataEngine.ItsMetadataCouldNotBeRead", provider.Name, ex.Message));
            }
        }
        else if (errors.Count == 0)
        {
            warnings.Add(id.Format != null
                ? L.F("MetadataEngine.0IdentifiedButNoMetadataReader", id.Format.Name)
                : L.T("MetadataEngine.UnknownFormatOnlyTheFileSystem"));
        }
        if (id.Evidence == FormatEvidence.Signature && id.Format != null && !id.Format.ExtensionMatches(path) && id.Format.Category != "Text" && Path.GetExtension(path).Length > 0
            && provider is not (OpenXmlMetadataProvider or EpubMetadataProvider))
            warnings.Add(L.F("MetadataEngine.TheExtension0DoesNotMatch", Path.GetExtension(path), id.Format.Name));
        if (errors.Count == 0) capabilities |= MetadataCapabilities.FileSystem;
        fields.AddRange(fsFields);
        return new MetadataSnapshot
        {
            Path = path, Format = format, ProviderId = provider?.Id ?? "", ProviderName = provider?.Name ?? L.T("MetadataEngine.FileSystemOnly"),
            Capabilities = capabilities, Fields = fields, Warnings = warnings, Errors = errors, Stamp = stamp,
        };
    }

    /// <summary>
    /// Sorts edits into those this file accepts (normalized) and those it does not, with the reason.
    /// A value equal to the current one is dropped (nothing to do).
    /// </summary>
    public static (List<MetadataEdit> Accepted, List<string> Refused) Check(MetadataSnapshot snapshot, IEnumerable<MetadataEdit> edits)
    {
        var accepted = new List<MetadataEdit>();
        var refused = new List<string>();
        foreach (var edit in edits.GroupBy(e => e.Key).Select(g => g.Last()))
        {
            var field = snapshot.Field(edit.Key);
            string label = field?.Label ?? (MetadataFields.All.TryGetValue(edit.Key, out var d) ? d.Label : edit.Key);
            if (field == null) { refused.Add(L.F("MetadataEngine.01CannotStoreIt", label, snapshot.Format)); continue; }
            if (edit.IsRemoval)
            {
                if (!field.Removable) { refused.Add(L.F("MetadataEngine.0CannotBeRemovedFrom1", label, snapshot.Format)); continue; }
                if (!field.HasValue) continue;
                accepted.Add(edit);
                continue;
            }
            if (!field.Editable) { refused.Add(L.F("MetadataEngine.0ReadOnlyIn1", label, snapshot.Format)); continue; }
            string? value = MetadataValues.Normalize(field.Definition, edit.Value);
            if (value == null)
            {
                if (!field.HasValue) continue;
                if (!field.Removable) { refused.Add(L.F("MetadataEngine.0CannotBeEmptiedIn1", label, snapshot.Format)); continue; }
                accepted.Add(new MetadataEdit(edit.Key, null));
                continue;
            }
            if (MetadataValues.Validate(field.Definition, value) is { } problem) { refused.Add(problem); continue; }
            if (SameValue(field.Definition, field.Value, value)) continue;
            accepted.Add(new MetadataEdit(edit.Key, value));
        }
        return (accepted, refused);
    }

    /// <summary>Two values of a field are the same (lists in any order, numbers within rounding, text trimmed).</summary>
    public static bool SameValue(MetadataFieldDefinition definition, string? a, string? b)
    {
        a = string.IsNullOrWhiteSpace(a) ? null : a.Trim();
        b = string.IsNullOrWhiteSpace(b) ? null : b.Trim();
        if (a == null || b == null) return a == b;
        return definition.Kind switch
        {
            MetadataValueKind.List => MetadataValues.SplitList(a).ToHashSet(StringComparer.Ordinal).SetEquals(MetadataValues.SplitList(b)),
            MetadataValueKind.Decimal => MetadataValues.TryParseNumber(a, out double x) && MetadataValues.TryParseNumber(b, out double y) ? Math.Abs(x - y) < 1e-4 : a == b,
            MetadataValueKind.Date => MetadataValues.TryParseDate(a, out var da) && MetadataValues.TryParseDate(b, out var db) ? Math.Abs((da - db).TotalSeconds) < 1 : a == b,
            MetadataValueKind.MultilineText => a.ReplaceLineEndings("\n") == b.ReplaceLineEndings("\n"),
            _ => string.Equals(a, b, StringComparison.Ordinal),
        };
    }

    /// <summary>What saving these edits can do beyond changing them (for the confirmation).</summary>
    public IReadOnlyList<string> Warnings(MetadataSnapshot snapshot, IReadOnlyList<MetadataEdit> edits)
    {
        var w = new List<string>();
        var embedded = edits.Where(e => !FileSystemMetadata.IsFileSystemKey(e.Key)).ToList();
        if (embedded.Count > 0 && Providers.FirstOrDefault(p => p.Id == snapshot.ProviderId) is { } provider) w.AddRange(provider.WriteWarnings(snapshot.Path, embedded));
        foreach (var e in edits.Where(e => e.IsRemoval))
            if (snapshot.Field(e.Key) is { Definition.Sensitive: true } f) w.Add(L.F("MetadataEngine.0IsRemoved", f.Label));
        return w;
    }

    /// <summary>Saves edits to one file (see the class description). Never throws for file problems: the result says what happened.</summary>
    public MetadataSaveResult Save(MetadataSnapshot snapshot, IReadOnlyList<MetadataEdit> edits, MetadataSaveOptions options, CancellationToken ct = default)
    {
        string path = snapshot.Path;
        if (snapshot.Errors.Count > 0) return new(path, MetadataSaveStatus.Skipped, L.T("MetadataEngine.NotSavedTheFileWasNot") + snapshot.Errors[0] + ")");
        var (accepted, refused) = Check(snapshot, edits);
        if (accepted.Count == 0)
            return refused.Count > 0
                ? new(path, MetadataSaveStatus.Skipped, L.T("MetadataEngine.NothingCouldBeChangedInThis")) { SkippedFields = refused }
                : new(path, MetadataSaveStatus.Unchanged, L.T("MetadataEngine.NoChanges"));
        var fsEdits = accepted.Where(e => FileSystemMetadata.IsFileSystemKey(e.Key)).ToList();
        var embedded = accepted.Where(e => !FileSystemMetadata.IsFileSystemKey(e.Key)).ToList();
        var provider = Providers.FirstOrDefault(p => p.Id == snapshot.ProviderId);
        if (embedded.Count > 0 && (provider == null || !snapshot.Capabilities.HasFlag(MetadataCapabilities.Edit)))
            return new(path, MetadataSaveStatus.Skipped, L.F("MetadataEngine.0EmbeddedMetadataCannotBeWritten", snapshot.Format)) { SkippedFields = refused };

        string? temp = null;
        string? backup = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path)) return new(path, MetadataSaveStatus.Failed, L.T("MetadataEngine.TheFileNoLongerExistsMoved"));
            if (snapshot.Stamp != null && FileStamp.Of(path) != snapshot.Stamp)
                return new(path, MetadataSaveStatus.Failed, L.T("MetadataEngine.TheFileWasChangedByAnother"));

            var info = new FileInfo(path);
            DateTime modified = info.LastWriteTime;
            if (embedded.Count > 0)
            {
                bool clearReadOnly = fsEdits.Any(e => e.Key == "fs.readOnly" && e.Value == "false");
                if (info.IsReadOnly && !clearReadOnly)
                    return new(path, MetadataSaveStatus.Failed, L.T("MetadataEngine.TheFileIsReadOnlyClear")) { SkippedFields = refused };
                if (info.IsReadOnly) FileSystemMetadata.Apply(path, [new MetadataEdit("fs.readOnly", "false")]);

                // 1. Write to a hidden copy in the same folder (same drive: the swap is one step).
                // The copy keeps the extension: readers recognize formats by it.
                temp = Path.Combine(info.DirectoryName!, $".{Path.GetFileNameWithoutExtension(info.Name)}.tf-{Guid.NewGuid().ToString("N")[..8]}{info.Extension}");
                File.Copy(path, temp);
                File.SetAttributes(temp, FileAttributes.Normal);
                provider!.Write(temp, embedded, ct);

                // 2. Read the copy back: the changes must be there, and nothing else may have changed.
                var after = provider.Read(temp, ct);
                var problems = Verify(snapshot, after, embedded);
                if (problems.Count > 0)
                    return new(path, MetadataSaveStatus.Failed, L.T("MetadataEngine.NotSavedTheResultDidNot") + string.Join(" ", problems)) { SkippedFields = refused };

                // 3. Back up the original (verified copy), then swap.
                ct.ThrowIfCancellationRequested();
                if (options.Backups != null)
                {
                    using var set = options.Backups.Begin(options.Operation, BackupKind.Automatic, info.DirectoryName);
                    backup = set.Add(path, ct: ct);
                }
                File.Replace(temp, path, null, ignoreMetadataErrors: true);
                temp = null;
                if (options.KeepModifiedTime && !fsEdits.Any(e => e.Key == "fs.modified")) File.SetLastWriteTime(path, modified);
            }
            // 4. File system properties last (a "modified" time set here is the final one).
            FileSystemMetadata.Apply(path, fsEdits.OrderBy(e => e.Key == "fs.readOnly" ? 1 : 0));
            var now = Load(path, ct);
            var warnings = Warnings(snapshot, accepted).ToList();
            if (!options.KeepModifiedTime && embedded.Count > 0 && !fsEdits.Any(e => e.Key == "fs.modified")) warnings.Add(L.T("MetadataEngine.TheFileSModifiedTimeIs"));
            AppLog.Info($"File Metadata: saved {L.Count(accepted.Count, "Noun.change")} to {path}" + (backup != null ? L.F("MetadataEngine.Backup0", backup) : ""));
            return new(path, MetadataSaveStatus.Saved, L.Count(accepted.Count, "MetadataEngine.SavedChanges") + (refused.Count > 0 ? L.F("MetadataEngine.0NotApplicableHere", refused.Count) : ""))
            {
                Changed = accepted.Select(e => e.Key).ToList(), Warnings = warnings, SkippedFields = refused, BackupPath = backup, Snapshot = now,
            };
        }
        catch (OperationCanceledException)
        {
            return new(path, MetadataSaveStatus.Cancelled, L.T("MetadataEngine.CancelledBeforeTheFileWasReplaced"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or FormatException or ArgumentException
                                       or InvalidOperationException or System.Xml.XmlException or OverflowException)
        {
            string reason = ex switch
            {
                IOException io when IsDiskFull(io) => L.T("MetadataEngine.ThereIsNotEnoughFreeSpace"),
                IOException io when IsLocked(io) => L.T("MetadataEngine.TheFileIsOpenInAnother"),
                UnauthorizedAccessException => L.T("MetadataEngine.WindowsDeniedAccessToTheFile"),
                _ => ex.Message,
            };
            AppLog.Warn($"File Metadata: {path} was not saved: {ex.Message}");
            return new(path, MetadataSaveStatus.Failed, L.T("MetadataEngine.NotSavedTheOriginalIsUnchanged") + reason) { SkippedFields = refused, BackupPath = backup };
        }
        finally
        {
            if (temp != null) TryDelete(temp);
        }
    }

    private static List<string> Verify(MetadataSnapshot before, MetadataReadResult after, IReadOnlyList<MetadataEdit> edits)
    {
        var problems = new List<string>();
        var changed = edits.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var e in edits)
        {
            var field = after.Fields.FirstOrDefault(f => f.Key == e.Key);
            string? actual = field?.Value;
            var definition = before.Field(e.Key)!.Definition;
            if (!SameValue(definition, actual, e.Value))
                problems.Add(e.IsRemoval
                    ? L.F("MetadataEngine.0IsStillThereAfterRemoving", definition.Label)
                    : L.F("MetadataEngine.0CannotBeStoredInThis", definition.Label, e.Value, actual ?? "empty"));
        }
        // A reader that now finds the file damaged (and did not before) means the write went wrong.
        foreach (string w in after.Warnings.Where(w => w.StartsWith("Possibly damaged", StringComparison.Ordinal) && !before.Warnings.Contains(w)))
            problems.Add(L.T("MetadataEngine.TheResultWouldBeDamaged") + w["Possibly damaged: ".Length..]);
        foreach (var f in before.Fields.Where(f => !FileSystemMetadata.IsFileSystemKey(f.Key) && !changed.Contains(f.Key) && f.Key != MetadataFields.TagTypes.Key))
        {
            string? now = after.Fields.FirstOrDefault(a => a.Key == f.Key)?.Value;
            if (!SameValue(f.Definition, f.Value, now)) problems.Add(L.F("MetadataEngine.0WouldHaveChangedFrom1", f.Label, f.Value ?? "empty", now ?? "empty"));
        }
        return problems;
    }

    private static bool IsDiskFull(IOException ex) => (ex.HResult & 0xFFFF) is 112 or 39;
    private static bool IsLocked(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"File Metadata: the temporary file {path} could not be removed: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ batch

    /// <summary>Applies the plans one file after another. Cancelling stops between files; a file is never left half-written.</summary>
    public IReadOnlyList<MetadataSaveResult> Run(IReadOnlyList<MetadataFilePlan> plans, MetadataSaveOptions options, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        var results = new List<MetadataSaveResult>();
        for (int i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            if (ct.IsCancellationRequested)
            {
                results.Add(new(plan.Snapshot.Path, MetadataSaveStatus.Cancelled, L.T("MetadataEngine.NotStartedTheBatchWasCancelled")));
                continue;
            }
            progress?.Report(new OperationProgress(i, plans.Count, plan.Snapshot.FileName));
            if (plan.Edits.Count == 0)
            {
                results.Add(new(plan.Snapshot.Path, plan.Skipped.Count > 0 ? MetadataSaveStatus.Skipped : MetadataSaveStatus.Unchanged,
                    plan.Skipped.Count > 0 ? L.T("MetadataEngine.NothingApplicableToThisFile") : L.T("MetadataEngine.NoChanges")) { SkippedFields = plan.Skipped });
                continue;
            }
            var result = Save(plan.Snapshot, plan.Edits, options, ct);
            results.Add(plan.Skipped.Count > 0 ? result with { SkippedFields = [.. plan.Skipped, .. result.SkippedFields] } : result);
        }
        progress?.Report(new OperationProgress(plans.Count, plans.Count, "Done"));
        return results;
    }
}
