using System.Text.Json;
using System.Text.Json.Serialization;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Clients;

/// <summary>One file as the last successful launch check saw it.</summary>
public sealed record BaselineFile(string Path, long Size, DateTime LastWriteUtc, string Sha256);

/// <summary>
/// What a monitored installation looked like at its last successful launch check (8.0). A baseline
/// is not an archive: it only says what changed since then and whether that content was archived.
/// </summary>
public sealed class ClientBaseline
{
    public const int CurrentFormat = 1;

    public int Format { get; init; } = CurrentFormat;
    public required string RegistrationId { get; init; }
    public required string Server { get; init; }
    public required string Path { get; init; }
    public string? Version { get; init; }
    public string VersionStatus { get; init; } = VersionReport.Unknown;
    public required string Fingerprint { get; init; }
    /// <summary>Personal data was part of the check (the fingerprint differs with and without it).</summary>
    public bool IncludesPersonalData { get; init; }
    public IReadOnlyList<BaselineFile> Files { get; init; } = [];
    public IReadOnlyList<string> Directories { get; init; } = [];
    public IReadOnlyList<ManifestExclusion> Exclusions { get; init; } = [];
    public DateTime VerifiedAtUtc { get; init; }
    /// <summary>The archived revision with exactly this content when the check ran, or null.</summary>
    public string? ArchivedKey { get; init; }

    [JsonIgnore] public long TotalBytes => Files.Sum(f => f.Size);
}

/// <summary>Where baselines are kept: <c>Clients\Baselines\&lt;registration&gt;.json</c>, next to the archive (they never create it).</summary>
public static class ClientBaselines
{
    public const string FolderName = "Baselines";

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string PathFor(string storeRoot, string registrationId) =>
        System.IO.Path.Combine(storeRoot, FolderName, FileNames.Sanitize(registrationId) + ".json");

    /// <summary>The baseline of a registration, or null when there is none or it cannot be read (the next check then starts afresh).</summary>
    public static ClientBaseline? Read(string storeRoot, string registrationId)
    {
        string file = PathFor(storeRoot, registrationId);
        if (!File.Exists(file)) return null;
        try
        {
            var baseline = JsonSerializer.Deserialize<ClientBaseline>(File.ReadAllBytes(file), Json);
            return baseline is { Format: ClientBaseline.CurrentFormat } && baseline.RegistrationId == registrationId ? baseline : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Replaces a baseline completely or not at all.</summary>
    public static void Write(string storeRoot, ClientBaseline baseline) =>
        new StoreFiles(storeRoot).WriteAtomic(PathFor(storeRoot, baseline.RegistrationId), JsonSerializer.SerializeToUtf8Bytes(baseline, Json));
}

/// <summary>What the launch check found.</summary>
public enum LaunchCheckOutcome
{
    /// <summary>The folder is not there or cannot be read; nothing changed.</summary>
    Unavailable,
    /// <summary>The launcher of this installation is running (it may be updating); checked at the next start.</summary>
    Deferred,
    /// <summary>Files changed during the check or differ from the client's own lists; the baseline is not advanced.</summary>
    Inconsistent,
    /// <summary>Same content as at the last check.</summary>
    Unchanged,
    /// <summary>The content differs from the last check.</summary>
    Changed,
    /// <summary>No earlier check to compare with.</summary>
    New,
}

/// <summary>The result of one launch check of one folder.</summary>
public sealed class LaunchCheckResult
{
    public required LaunchCheckOutcome Outcome { get; init; }
    public required string Folder { get; init; }
    public bool Quick { get; init; }
    public FolderScan? Scan { get; init; }
    public FolderInspection? Inspection { get; init; }
    public ClientBaseline? Previous { get; init; }
    public int ChangedFiles { get; init; }
    public int AddedFiles { get; init; }
    public int RemovedFiles { get; init; }
    public long BytesDelta { get; init; }
    public IReadOnlyList<string> Problems { get; init; } = [];

    public string? Fingerprint => Scan?.Fingerprint;
    public VersionReport Version => Inspection?.Version ?? VersionReport.None;
    public bool Accepted => Outcome is LaunchCheckOutcome.Unchanged or LaunchCheckOutcome.Changed or LaunchCheckOutcome.New;

    /// <summary>"312 files changed, 4 added, 1 removed, +41 MB" (empty when nothing differs).</summary>
    public string ChangeSummary()
    {
        var parts = new List<string>();
        if (ChangedFiles > 0) parts.Add(L.F("ClientMonitor.0Changed", L.Count(ChangedFiles, "Noun.file")));
        if (AddedFiles > 0) parts.Add(L.F("ClientMonitor.0N0Added", AddedFiles));
        if (RemovedFiles > 0) parts.Add(L.F("ClientMonitor.0N0Removed", RemovedFiles));
        if (parts.Count > 0 && BytesDelta != 0) parts.Add((BytesDelta > 0 ? "+" : "−") + ReportWriter.FormatSize(Math.Abs(BytesDelta)));
        return string.Join(", ", parts);
    }
}

/// <summary>
/// The check of a monitored installation at every start (8.0). It only reads: the folder is listed,
/// hashed (every file, or in quick mode only files whose size or time changed), listed again and
/// compared with the client's own lists. A check that saw files change is not accepted, so the
/// baseline only ever describes a consistent state.
/// </summary>
public static class LaunchCheck
{
    public static LaunchCheckResult Run(string folder, ClientBaseline? baseline, bool quick, InventoryOptions options,
        IReadOnlyDictionary<string, string>? knownContent = null, Func<string, bool>? launcherActive = null,
        IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(folder))
            return new LaunchCheckResult { Outcome = LaunchCheckOutcome.Unavailable, Folder = folder, Quick = quick, Previous = baseline, Problems = [L.F("ClientMonitor.TheFolder0IsNotAvailable", folder)] };
        if (launcherActive?.Invoke(folder) == true)
            return new LaunchCheckResult { Outcome = LaunchCheckOutcome.Deferred, Folder = folder, Quick = quick, Previous = baseline,
                Problems = [L.T("ClientMonitor.TheTibiaLauncherOfThisInstallation")] };
        // A baseline taken with the other personal-data rule describes other files: start afresh.
        if (baseline != null && baseline.IncludesPersonalData != options.IncludePersonalData) baseline = null;
        FolderInspection inspection;
        FolderScan scan;
        try
        {
            inspection = FolderInspector.Inspect(folder, knownContent);
            if (quick && baseline != null)
            {
                var known = baseline.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
                options = new InventoryOptions
                {
                    IncludePersonalData = options.IncludePersonalData,
                    Parallelism = options.Parallelism,
                    KnownHash = file => file.ListedBy != "version file" && known.TryGetValue(file.Path, out var b) && b.Size == file.Size && b.LastWriteUtc == file.LastWriteUtc &&
                                        (file.ExpectedSha256 == null || file.ExpectedSha256 == b.Sha256) ? b.Sha256 : null,
                };
            }
            scan = ClientInventory.Scan(folder, options, progress, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new LaunchCheckResult { Outcome = LaunchCheckOutcome.Unavailable, Folder = folder, Quick = quick, Previous = baseline, Problems = [ex.Message] };
        }
        if (!scan.IsConsistent)
            return new LaunchCheckResult { Outcome = LaunchCheckOutcome.Inconsistent, Folder = folder, Quick = quick, Scan = scan, Inspection = inspection, Previous = baseline, Problems = scan.Problems };
        if (baseline == null)
            return new LaunchCheckResult { Outcome = LaunchCheckOutcome.New, Folder = folder, Quick = quick, Scan = scan, Inspection = inspection };
        if (baseline.Fingerprint == scan.Fingerprint)
            return new LaunchCheckResult { Outcome = LaunchCheckOutcome.Unchanged, Folder = folder, Quick = quick, Scan = scan, Inspection = inspection, Previous = baseline };

        var before = baseline.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        int changed = 0, added = 0;
        foreach (var f in scan.Files)
        {
            if (!before.Remove(f.Path, out var old)) added++;
            else if (old.Sha256 != f.Sha256) changed++;
        }
        return new LaunchCheckResult
        {
            Outcome = LaunchCheckOutcome.Changed, Folder = folder, Quick = quick, Scan = scan, Inspection = inspection, Previous = baseline,
            ChangedFiles = changed, AddedFiles = added, RemovedFiles = before.Count, BytesDelta = scan.TotalBytes - baseline.TotalBytes,
        };
    }

    /// <summary>The baseline an accepted check leaves behind.</summary>
    public static ClientBaseline BaselineOf(LaunchCheckResult result, string registrationId, string server, bool includesPersonalData, DateTime nowUtc, string? archivedKey)
    {
        var scan = result.Scan ?? throw new InvalidOperationException("Only an accepted check has a baseline.");
        if (!result.Accepted) throw new InvalidOperationException("Only an accepted check has a baseline.");
        return new ClientBaseline
        {
            RegistrationId = registrationId,
            Server = server,
            Path = result.Folder,
            Version = result.Version.Version,
            VersionStatus = result.Version.Status,
            Fingerprint = scan.Fingerprint,
            IncludesPersonalData = includesPersonalData,
            Files = scan.Files.Select(f => new BaselineFile(f.Path, f.Size, f.LastWriteUtc ?? default, f.Sha256)).ToList(),
            Directories = scan.Directories,
            Exclusions = scan.Exclusions,
            VerifiedAtUtc = nowUtc,
            ArchivedKey = archivedKey,
        };
    }
}
