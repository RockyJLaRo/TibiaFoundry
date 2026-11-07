using TibiaFoundry.Core.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Clients;

/// <summary>The two client categories of the store. The values are folder names and keys in the store's files: never rename one.</summary>
public static class ClientServers
{
    public const string Live = "Live_Server";
    public const string Test = "Test_Server";

    public static bool IsKnown(string? server) => server is Live or Test;

    public static string Of(ClientScope scope) => scope == ClientScope.Live ? Live : Test;

    public static ClientScope ScopeOf(string server) => server == Test ? ClientScope.TestServer : ClientScope.Live;

    public static string Display(string server) => server == Test ? L.T("Common.TestServer") : L.T("ClientManifest.LiveServer");
}

/// <summary>What a file of a client version is. Stored in the manifest as metadata; it never decides where an object is stored.</summary>
public static class ClientFileKinds
{
    public const string Asset = "asset";
    public const string SpriteSheet = "sprite-sheet";
    public const string Sound = "sound";
    public const string Data = "data";
    public const string Rcc = "rcc";
    public const string Meta = "meta";

    /// <summary>The kind of a client file from its path (relative to the package, '/' separated).</summary>
    public static string Of(string path)
    {
        string name = path[(path.LastIndexOf('/') + 1)..];
        if (name.EndsWith(".rcc", StringComparison.OrdinalIgnoreCase)) return Rcc;
        if (!path.Contains('/')) return Meta;
        if (path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) && name.StartsWith("sprites-", StringComparison.OrdinalIgnoreCase)) return SpriteSheet;
        if (name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)) return Data;
        if (path.StartsWith("sounds/", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return Sound;
        return Asset;
    }

    /// <summary>Name for summaries ("1 sprite sheet", "2 RCC files", …).</summary>
    public static string Count(string kind, int count)
    {
        string name = kind switch
        {
            SpriteSheet => L.T("ClientManifest.SpriteSheet"),
            Sound => "sound",
            Data => L.T("ClientManifest.DataFile"),
            Rcc => L.T("ClientManifest.RCCFile"),
            Meta => L.T("ClientManifest.VersionFile"),
            _ => L.T("ClientManifest.OtherAssetFile"),
        };
        return $"{count:N0} {name}{(count == 1 ? "" : "s")}";
    }
}

/// <summary>One file of a client version: its path in the package ('/' separated), kind, content hash and size.</summary>
/// <param name="ListedBy">8.0: the client list that names the file ("package.json", "assets.json"), "version file", or null (not listed by the client).</param>
/// <param name="LastWriteUtc">8.0: the file's last-write time when it was archived (restored by a rebuild; not part of the identity).</param>
/// <param name="Attributes">8.0: "readonly", "hidden" or both ("readonly,hidden") when set (restored by a rebuild; not part of the identity).</param>
public sealed record ManifestFile(string Path, string Kind, string Sha256, long Size, string? ListedBy = null, DateTime? LastWriteUtc = null, string? Attributes = null);

/// <summary>What a revision covers (8.0). The values are stored in manifests: never rename one.</summary>
public static class ManifestScopes
{
    /// <summary>Every file of a client package folder (except exclusions it lists).</summary>
    public const string CompleteInstallation = "complete-installation";
    /// <summary>Every file of a folder you chose (a copy, a download, an assets folder…), except exclusions it lists.</summary>
    public const string StandaloneFolder = "standalone-folder";
    /// <summary>7.3 imports: only the files Tibia Foundry reads (assets, sounds, RCC and the client's lists).</summary>
    public const string TibiaFoundryFiles = "tibia-foundry-files";

    public static string Display(string scope) => scope switch
    {
        CompleteInstallation => L.T("ClientManifest.CompleteInstallation"),
        StandaloneFolder => L.T("ClientManifest.StandaloneFolder"),
        _ => L.T("ClientManifest.TibiaFoundryFiles73"),
    };
}

/// <summary>How a revision's content is kept (8.0). The values are stored in manifests and settings: never rename one.</summary>
public static class ManifestStorage
{
    /// <summary>Each file content once in <c>Shared_Assets\Objects</c>, shared with every revision that has it.</summary>
    public const string Shared = "shared";
    /// <summary>A physical copy of every file in the revision's own <c>files</c> folder.</summary>
    public const string Independent = "independent";

    public static string Display(string storage) => storage == Independent ? L.T("ClientManifest.IndependentCopies") : L.T("ClientManifest.SpaceEfficientShared");
}

/// <summary>Files deliberately left out of a revision (8.0): a rule, why, and how many files and bytes it left out. Paths inside are never recorded.</summary>
public sealed record ManifestExclusion(string Rule, string Reason, int Files, long Bytes);

/// <summary>One piece of evidence about a client's version (8.0): where it came from, its value, and whether it is strong.</summary>
/// <param name="Strong">Strong evidence decides the version; weak evidence is shown as "probably".</param>
public sealed record VersionClue(string Source, string Value, bool Strong);

/// <summary>Where a version was imported from (no absolute paths: the manifest must stay portable).</summary>
/// <param name="Registration">The registration it was imported through.</param>
/// <param name="FileList">"assets.json" (the client's own list) or "fallback" (catalogs and map data).</param>
/// <param name="AssetsJsonSha256">SHA-256 of the client's assets.json, when there was one.</param>
public sealed record ManifestSource(string? Registration, string FileList, string? AssetsJsonSha256);

/// <summary>What a new manifest describes (see <see cref="ClientManifest.Create(ManifestContent, DateTime, string?)"/>).</summary>
public sealed class ManifestContent
{
    public required string Server { get; init; }
    public string? ClientVersion { get; init; }
    public required IReadOnlyList<ManifestFile> Files { get; init; }
    public IReadOnlyList<string> Directories { get; init; } = [];
    public string Scope { get; init; } = ManifestScopes.TibiaFoundryFiles;
    public string Storage { get; init; } = ManifestStorage.Shared;
    public string? SourceType { get; init; }
    public IReadOnlyList<ManifestExclusion> Exclusions { get; init; } = [];
    public IReadOnlyList<string> Problems { get; init; } = [];
    public VersionReport? Version { get; init; }
    public ManifestSource? Source { get; init; }
}

/// <summary>
/// The complete file set of one imported client version. Every file the version needs is listed,
/// with the SHA-256 of its exact bytes, so the version resolves on its own: there is no base or
/// parent version, and no other manifest is ever needed. Manifests are written once and never changed.
/// </summary>
public sealed class ClientManifest
{
    public const string FileName = "manifest.json";
    /// <summary>1 = 7.3 (Tibia Foundry's files only); 2 = 8.0 (scope, storage, exclusions, empty folders, file times).</summary>
    public const int CurrentFormat = 2;
    public const string IdPrefix = "sha256:";

    public int Format { get; init; } = CurrentFormat;

    /// <summary>What the revision covers (<see cref="ManifestScopes"/>); null in format 1 (Tibia Foundry's files).</summary>
    public string? Scope { get; init; }

    /// <summary>How the content is kept (<see cref="ManifestStorage"/>); null in format 1 (shared).</summary>
    public string? Storage { get; init; }

    /// <summary>"installed-package", "standalone-folder" or "reconstructed": the kind of folder it was archived from.</summary>
    public string? SourceType { get; init; }

    /// <summary>True when every file of the source was archived: nothing excluded and no problem.</summary>
    public bool Complete { get; init; }

    public IReadOnlyList<ManifestExclusion>? Exclusions { get; init; }

    /// <summary>Entries that could not be archived (published only when you chose to keep an incomplete revision).</summary>
    public IReadOnlyList<string>? Problems { get; init; }

    /// <summary>Empty folders of the source ('/' separated): a rebuild creates them too.</summary>
    public IReadOnlyList<string>? Directories { get; init; }

    public IReadOnlyList<VersionClue>? VersionEvidence { get; init; }

    /// <summary>"Verified", "Conflicting" or "Unknown" (see <see cref="VersionReport"/>).</summary>
    public string? VersionStatus { get; init; }

    [JsonIgnore] public string EffectiveScope => Scope ?? ManifestScopes.TibiaFoundryFiles;
    [JsonIgnore] public string EffectiveStorage => Storage ?? ManifestStorage.Shared;

    /// <summary>A complete installation with nothing excluded and no problem: the only kind a rebuild may call complete.</summary>
    [JsonIgnore] public bool IsCompleteInstallation => EffectiveScope == ManifestScopes.CompleteInstallation && IsContentComplete;

    /// <summary>Nothing left out of the revision's own scope.</summary>
    [JsonIgnore] public bool IsContentComplete => Complete && (Exclusions?.Count ?? 0) == 0 && (Problems?.Count ?? 0) == 0;

    /// <summary>"sha256:" + <see cref="Fingerprint"/>: the identity of the content.</summary>
    public required string Id { get; init; }

    public required string Server { get; init; }

    /// <summary>The "version" of package.json (for example "15.33.9d148f"), or null when unknown.</summary>
    public string? ClientVersion { get; init; }

    public string? DisplayVersion { get; init; }
    public string? Build { get; init; }
    public DateTime ImportedAtUtc { get; init; }
    public string? ImportedBy { get; init; }
    public ManifestSource? Source { get; init; }
    public required IReadOnlyList<ManifestFile> Files { get; init; }

    [JsonIgnore] public string Fingerprint => Id.StartsWith(IdPrefix, StringComparison.Ordinal) ? Id[IdPrefix.Length..] : Id;
    [JsonIgnore] public long TotalBytes => Files.Sum(f => f.Size);

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// SHA-256 over the sorted lines "path NUL sha256 NUL size", and (8.0) one line "folder/ NUL dir"
    /// per empty folder. Two revisions with the same fingerprint have the same files with the same
    /// bytes and the same empty folders (kind, times and attributes are metadata and not included).
    /// Without empty folders the result is the 7.3 fingerprint.
    /// </summary>
    public static string ComputeFingerprint(IEnumerable<ManifestFile> files, IEnumerable<string>? directories = null)
    {
        var lines = files.Select(f => (f.Path, Line: f.Path + "\0" + f.Sha256 + "\0" + f.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Concat((directories ?? []).Select(d => (Path: d + "/", Line: d + "/\0dir")))
            .OrderBy(l => l.Path, StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var (_, line) in lines) sb.Append(line).Append('\n');
        return FileHashing.HashBytes(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    /// <summary>"15.33.9d148f" → ("15.33", "9d148f"); without a dot the whole text is the display version.</summary>
    public static (string? Display, string? Build) SplitVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return (null, null);
        int dot = version.LastIndexOf('.');
        return dot <= 0 || dot == version.Length - 1 ? (version, null) : (version[..dot], version[(dot + 1)..]);
    }

    public static ClientManifest Create(string server, string? clientVersion, IEnumerable<ManifestFile> files, ManifestSource? source, DateTime now, string? importedBy) =>
        Create(new ManifestContent { Server = server, ClientVersion = clientVersion, Files = files.ToList(), Source = source }, now, importedBy);

    /// <summary>A new manifest (always the current format) for archived content.</summary>
    public static ClientManifest Create(ManifestContent content, DateTime now, string? importedBy)
    {
        var sorted = content.Files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        var directories = content.Directories.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var (display, build) = SplitVersion(content.ClientVersion);
        return new ClientManifest
        {
            Id = IdPrefix + ComputeFingerprint(sorted, directories),
            Server = content.Server,
            Scope = content.Scope,
            Storage = content.Storage,
            SourceType = content.SourceType,
            Complete = content.Exclusions.Count == 0 && content.Problems.Count == 0,
            Exclusions = content.Exclusions.Count > 0 ? content.Exclusions : null,
            Problems = content.Problems.Count > 0 ? content.Problems : null,
            Directories = directories.Count > 0 ? directories : null,
            ClientVersion = content.ClientVersion,
            DisplayVersion = display,
            Build = build,
            VersionEvidence = content.Version?.Clues.Count > 0 ? content.Version.Clues : null,
            VersionStatus = content.Version?.Status,
            ImportedAtUtc = now,
            ImportedBy = importedBy,
            Source = content.Source,
            Files = sorted,
        };
    }

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    /// <summary>Reads and fully validates a manifest. Throws <see cref="InvalidDataException"/> when it cannot be trusted.</summary>
    public static ClientManifest Read(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"The manifest {path} cannot be read: {ex.Message}", ex);
        }
        return Parse(bytes, path);
    }

    public static ClientManifest Parse(byte[] bytes, string what)
    {
        ClientManifest? manifest;
        try
        {
            using (var doc = JsonDocument.Parse(bytes))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"The manifest {what} is not a JSON object.");
                if (doc.RootElement.TryGetProperty("format", out var f) && f.TryGetInt32(out int format) && format > CurrentFormat)
                    throw new InvalidDataException($"The manifest {what} was written by a newer version of Tibia Foundry (format {format}).");
            }
            manifest = JsonSerializer.Deserialize<ClientManifest>(bytes, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The manifest {what} is damaged: {ex.Message}", ex);
        }
        if (manifest == null) throw new InvalidDataException($"The manifest {what} is empty.");
        if (manifest.Validate() is { } problem) throw new InvalidDataException($"The manifest {what} cannot be used: {problem}");
        return manifest;
    }

    /// <summary>Why this manifest cannot be trusted, or null when it is consistent.</summary>
    public string? Validate()
    {
        if (Format is < 1 or > CurrentFormat) return L.F("ClientManifest.UnsupportedFormat0", Format);
        if (!ClientServers.IsKnown(Server)) return L.F("ClientManifest.UnknownServer0", Server);
        if (Files == null || Files.Count == 0) return L.T("ClientManifest.ItListsNoFiles");
        if (Format >= 2)
        {
            if (Scope is not (ManifestScopes.CompleteInstallation or ManifestScopes.StandaloneFolder or ManifestScopes.TibiaFoundryFiles)) return L.F("ClientManifest.UnknownScope0", Scope);
            if (Storage is not (ManifestStorage.Shared or ManifestStorage.Independent)) return L.F("ClientManifest.UnknownStorage0", Storage);
            if (Complete && ((Exclusions?.Count ?? 0) > 0 || (Problems?.Count ?? 0) > 0)) return L.T("ClientManifest.ItSaysItIsCompleteBut");
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files)
        {
            if (file == null) return L.T("ClientManifest.ItHasAnEmptyFileEntry");
            if (PathGuard.SafeRelativePath(file.Path, out string? why) == null || file.Path.Contains('\\')) return L.F("ClientManifest.ThePath0IsUnsafe1", file.Path, why ?? L.T("ClientManifest.ItUses"));
            if (!seen.Add(file.Path)) return L.F("ClientManifest.0IsListedTwice", file.Path);
            if (!ContentStore.IsHash(file.Sha256)) return L.F("ClientManifest.0HasNoValidSHA256", file.Path);
            if (file.Size < 0) return L.F("ClientManifest.0HasANegativeSize", file.Path);
            if (string.IsNullOrWhiteSpace(file.Kind)) return L.F("ClientManifest.0HasNoKind", file.Path);
        }
        foreach (string dir in Directories ?? [])
        {
            if (PathGuard.SafeRelativePath(dir, out string? why) == null || dir.Contains('\\')) return L.F("ClientManifest.TheFolder0IsUnsafe1", dir, why ?? L.T("ClientManifest.ItUses"));
            if (seen.Contains(dir) || Files.Any(f => f.Path.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase))) return L.F("ClientManifest.TheFolder0IsListedAs", dir);
        }
        string expected = IdPrefix + ComputeFingerprint(Files, Directories);
        return string.Equals(Id, expected, StringComparison.Ordinal) ? null : L.T("ClientManifest.ItsIDDoesNotMatchIts");
    }

    /// <summary>The file at a package path (case-insensitive, '/' or '\' separators), or null.</summary>
    public ManifestFile? Find(string path)
    {
        string wanted = path.Replace('\\', '/');
        return Files.FirstOrDefault(f => string.Equals(f.Path, wanted, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>How a client's current files differ from a stored version.</summary>
public sealed class ManifestDiff
{
    public required IReadOnlyList<ManifestFile> Added { get; init; }
    public required IReadOnlyList<ManifestFile> Removed { get; init; }
    /// <summary>Same path, different content.</summary>
    public required IReadOnlyList<(ManifestFile Old, ManifestFile New)> Modified { get; init; }
    /// <summary>Removed and added files of the same kind that take each other's place (by sprite range or name stem).</summary>
    public required IReadOnlyDictionary<string, int> ReplacedByKind { get; init; }

    /// <summary>8.0: the same bytes under another path (one removed and one added file with the same SHA-256 and size, and no other file with it).</summary>
    public IReadOnlyList<(ManifestFile Old, ManifestFile New)> Moved { get; init; } = [];

    /// <summary>8.0: files with the same path and bytes on both sides.</summary>
    public int UnchangedCount { get; init; }

    /// <summary>8.0: what could not be compared (different scopes, exclusions or problems on one side), in plain words; empty when everything was.</summary>
    public IReadOnlyList<string> NotCompared { get; init; } = [];

    public bool Unchanged => Added.Count == 0 && Removed.Count == 0 && Modified.Count == 0 && Moved.Count == 0;

    /// <summary>
    /// Compares two revisions (8.0). Only what both scopes cover is compared: a complete installation
    /// against a 7.3 revision compares only the files Tibia Foundry reads; files under a folder one side
    /// excluded are left out of both. What was left out is said in <see cref="NotCompared"/>.
    /// </summary>
    public static ManifestDiff CompareRevisions(ClientManifest before, ClientManifest after)
    {
        var notes = new List<string>();
        IEnumerable<ManifestFile> a = before.Files, b = after.Files;
        if (before.EffectiveScope != after.EffectiveScope)
        {
            notes.Add(L.F("ClientManifest.TheTwoRevisionsCoverDifferentScopes", ManifestScopes.Display(before.EffectiveScope), ManifestScopes.Display(after.EffectiveScope)));
            if (before.EffectiveScope == ManifestScopes.TibiaFoundryFiles || after.EffectiveScope == ManifestScopes.TibiaFoundryFiles)
            {
                bool Covered(ManifestFile f) => ClientScanner.IsManaged(f.Path, f.Kind) && (f.Kind != ClientFileKinds.Meta || f.Path is ClientScanner.AssetsJson or ClientScanner.AssetsJsonHash or ClientScanner.VersionFile or Compatibility.TibiaFormat.PackageManifest);
                int left = a.Count(f => !Covered(f)) + b.Count(f => !Covered(f));
                a = a.Where(Covered);
                b = b.Where(Covered);
                if (left > 0) notes.Add(L.F("ClientManifest.0OutsideTheFilesTibiaFoundry", L.Count(left, "Noun.file")));
            }
        }
        var excludedTops = (before.Exclusions ?? []).Concat(after.Exclusions ?? []).Select(e => TopFolderOf(e.Rule)).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (excludedTops.Count > 0)
        {
            bool Excluded(ManifestFile f) => excludedTops.Contains(f.Path.Split('/')[0]) && f.ListedBy == null;
            int left = a.Count(Excluded) + b.Count(Excluded);
            a = a.Where(f => !Excluded(f));
            b = b.Where(f => !Excluded(f));
            notes.Add(L.F("ClientManifest.FoldersExcludedOnEitherSideWere", string.Join(", ", excludedTops.Order(StringComparer.OrdinalIgnoreCase)), (left > 0 ? L.F("ClientManifest.0LeftOut", L.Count(left, "Noun.file")) : "")));
        }
        if ((before.Problems?.Count ?? 0) + (after.Problems?.Count ?? 0) > 0)
            notes.Add(L.T("ClientManifest.ARevisionListsEntriesThatCould"));
        var diff = Compare(a.ToList(), b.ToList());
        return new ManifestDiff
        {
            Added = diff.Added, Removed = diff.Removed, Modified = diff.Modified, ReplacedByKind = diff.ReplacedByKind,
            Moved = diff.Moved, UnchangedCount = diff.UnchangedCount, NotCompared = notes,
        };
    }

    /// <summary>"characterdata/ (personal data)" → "characterdata"; null for rules that are not a top folder.</summary>
    private static string? TopFolderOf(string rule)
    {
        int slash = rule.IndexOf('/');
        return slash > 0 && !rule[..slash].Contains(' ') ? rule[..slash] : null;
    }

    /// <summary>
    /// Compares a stored version with current files. Sprite sheets carry a hash in their names, so a
    /// changed sheet looks like one removed and one added file: they count as replaced when their
    /// catalog sprite ranges match (<paramref name="oldRanges"/>, <paramref name="newRanges"/>: file
    /// name → range). Data and RCC files are paired by their name stem ("appearances-…"); sounds and
    /// map tiles, whose stems are all alike, count as added and removed.
    /// </summary>
    public static ManifestDiff Compare(IReadOnlyList<ManifestFile> before, IReadOnlyList<ManifestFile> after,
        IReadOnlyDictionary<string, (int First, int Last)>? oldRanges = null, IReadOnlyDictionary<string, (int First, int Last)>? newRanges = null)
    {
        var old = before.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var now = after.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var added = after.Where(f => !old.ContainsKey(f.Path)).ToList();
        var removed = before.Where(f => !now.ContainsKey(f.Path)).ToList();
        var modified = after.Where(f => old.TryGetValue(f.Path, out var o) && (o.Sha256 != f.Sha256 || o.Size != f.Size)).Select(f => (old[f.Path], f)).ToList();
        int unchanged = after.Count(f => old.TryGetValue(f.Path, out var o) && o.Sha256 == f.Sha256 && o.Size == f.Size);

        // The same bytes under exactly one removed and one added path: a move (or rename), established by the hash alone.
        var addedByHash = added.GroupBy(f => (f.Sha256, f.Size)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var moved = removed.GroupBy(f => (f.Sha256, f.Size)).Where(g => g.Count() == 1 && addedByHash.ContainsKey(g.Key))
            .Select(g => (Old: g.Single(), New: addedByHash[g.Key])).OrderBy(m => m.New.Path, StringComparer.Ordinal).ToList();
        if (moved.Count > 0)
        {
            var movedOld = moved.Select(m => m.Old.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var movedNew = moved.Select(m => m.New.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            removed = removed.Where(f => !movedOld.Contains(f.Path)).ToList();
            added = added.Where(f => !movedNew.Contains(f.Path)).ToList();
        }

        var replaced = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kind in added.Select(f => f.Kind).Intersect(removed.Select(f => f.Kind)))
        {
            var a = added.Where(f => f.Kind == kind).ToList();
            var r = removed.Where(f => f.Kind == kind).ToList();
            int pairs;
            if (kind == ClientFileKinds.SpriteSheet && oldRanges != null && newRanges != null)
            {
                var removedRanges = r.Select(f => oldRanges.TryGetValue(NameOf(f.Path), out var range) ? range : ((int, int)?)null).OfType<(int, int)>().GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
                pairs = 0;
                foreach (var f in a)
                    if (newRanges.TryGetValue(NameOf(f.Path), out var range) && removedRanges.TryGetValue(range, out int left) && left > 0)
                    {
                        removedRanges[range] = left - 1;
                        pairs++;
                    }
            }
            else if (kind is ClientFileKinds.Data or ClientFileKinds.Rcc or ClientFileKinds.Meta)
            {
                // "appearances-<hash>.dat" replaced by another "appearances-<hash>.dat" is one changed file.
                pairs = a.GroupBy(f => Stem(f.Path)).Sum(g => Math.Min(g.Count(), r.Count(x => Stem(x.Path) == g.Key)));
            }
            else
            {
                // Sounds and map tiles all share their stems: report them as added and removed.
                pairs = 0;
            }
            if (pairs > 0) replaced[kind] = pairs;
        }
        return new ManifestDiff { Added = added, Removed = removed, Modified = modified, ReplacedByKind = replaced, Moved = moved, UnchangedCount = unchanged };
    }

    private static string NameOf(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>"assets/satellite-ab12….bmp.lzma" → "assets/satellite".</summary>
    private static string Stem(string path)
    {
        int slash = path.LastIndexOf('/');
        string name = path[(slash + 1)..];
        int cut = name.IndexOfAny(['-', '.']);
        return (slash >= 0 ? path[..(slash + 1)] : "") + (cut > 0 ? name[..cut] : name);
    }

    /// <summary>One line such as "229 sprite sheets replaced, 1 RCC file changed, 2 sounds added, 1 sound removed."</summary>
    public string Summary()
    {
        if (Unchanged) return L.T("ClientManifest.NoChange");
        var parts = new List<string>();
        var kinds = Added.Select(f => f.Kind).Concat(Removed.Select(f => f.Kind)).Concat(Modified.Select(m => m.New.Kind)).Distinct().OrderBy(Order);
        foreach (string kind in kinds)
        {
            int replaced = ReplacedByKind.GetValueOrDefault(kind);
            int changed = Modified.Count(m => m.New.Kind == kind) + replaced;
            int added = Added.Count(f => f.Kind == kind) - replaced;
            int removed = Removed.Count(f => f.Kind == kind) - replaced;
            if (changed > 0) parts.Add($"{ClientFileKinds.Count(kind, changed)} {(kind == ClientFileKinds.SpriteSheet ? "replaced" : "changed")}");
            if (added > 0) parts.Add(L.F("ClientManifest.0Added", ClientFileKinds.Count(kind, added)));
            if (removed > 0) parts.Add(L.F("ClientManifest.0Removed", ClientFileKinds.Count(kind, removed)));
        }
        if (Moved.Count > 0) parts.Add(L.Count(Moved.Count, "ClientManifest.FilesMoved"));
        return string.Join(", ", parts) + ".";
    }

    private static int Order(string kind) => kind switch
    {
        ClientFileKinds.SpriteSheet => 0,
        ClientFileKinds.Data => 1,
        ClientFileKinds.Rcc => 2,
        ClientFileKinds.Asset => 3,
        ClientFileKinds.Sound => 4,
        _ => 5,
    };
}
