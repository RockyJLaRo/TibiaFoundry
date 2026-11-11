using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Items;

/// <summary>A catalog found in a folder (for the list of catalogs).</summary>
public sealed record CatalogSummary(string Folder, string Name, int Sources, DateTime CreatedUtc, string? Problem);

/// <summary>
/// Stores item catalogs, one folder each:
/// <list type="bullet">
/// <item><c>catalog.json</c>: name, description, the sources and mappings (written atomically);</item>
/// <item><c>sources\&lt;source&gt;.json.gz</c>: the records of an imported source, never changed in place
///   (replaced as a whole on re-import, after a backup of the old file);</item>
/// <item><c>observations.jsonl</c>: records added one at a time (names collected from the client, decisions in
///   the conflict review), one JSON object per line, flushed after every line so nothing confirmed is lost.</item>
/// </list>
/// A damaged or incomplete last line of the log (the program stopped while writing) is reported and
/// skipped; every complete record before it is kept.
/// </summary>
public static class ItemCatalogStore
{
    public const int CurrentFormat = 1;
    public const string CatalogFile = "catalog.json";
    public const string SourcesFolder = "sources";
    public const string JournalFile = "observations.jsonl";

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions JsonIndented = new(Json) { WriteIndented = true };

    /// <summary>Records of one source in its snapshot file.</summary>
    private sealed record SourceSnapshot(ItemSource Source, List<ItemObservation> Observations, List<IdMapping>? Mappings = null);

    public static bool IsCatalogFolder(string folder) => File.Exists(Path.Combine(folder, CatalogFile));

    /// <summary>The catalogs in a folder (each in its own subfolder), by name.</summary>
    public static IReadOnlyList<CatalogSummary> List(string root)
    {
        if (!Directory.Exists(root)) return [];
        var result = new List<CatalogSummary>();
        foreach (string dir in Directory.EnumerateDirectories(root))
        {
            if (!IsCatalogFolder(dir)) continue;
            try
            {
                var info = ReadInfo(dir);
                result.Add(new CatalogSummary(dir, info.Name, info.Sources.Count, info.CreatedUtc, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                result.Add(new CatalogSummary(dir, Path.GetFileName(dir), 0, DateTime.MinValue, ex.Message));
            }
        }
        return result.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>A folder name for a new catalog: the name without characters Windows does not allow, never a device name (CON, NUL…), made unique.</summary>
    public static string FolderFor(string root, string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name.Trim()) sb.Append(WindowsNames.IsInvalidChar(c) ? '_' : c);
        string baseName = sb.ToString().Trim('.', ' ');
        if (baseName.Length == 0) baseName = "Catalog";
        if (baseName.Length > 60) baseName = baseName[..60].TrimEnd('.', ' ');
        if (WindowsNames.IsDeviceName(baseName)) baseName = "_" + baseName;
        string folder = Path.Combine(root, baseName);
        for (int i = 2; Directory.Exists(folder) || File.Exists(folder); i++) folder = Path.Combine(root, $"{baseName} ({i})");
        return folder;
    }

    /// <summary>Creates an empty catalog in a new folder.</summary>
    public static ItemCatalog Create(string folder, string name, string? description = null)
    {
        if (IsCatalogFolder(folder)) throw new IOException($"There is already a catalog in {folder}.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A catalog needs a name.", nameof(name));
        Directory.CreateDirectory(folder);
        var info = new CatalogInfo { Name = name.Trim(), Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(), CreatedUtc = DateTime.UtcNow };
        WriteInfo(folder, info);
        AppLog.Info($"Item catalog \"{info.Name}\" created in {folder}.");
        return new ItemCatalog(folder, info, [], [], []);
    }

    public static ItemCatalog Load(string folder)
    {
        var info = ReadInfo(folder);
        var problems = new List<string>();
        var observations = new List<ItemObservation>();
        var mappings = new List<IdMapping>();
        foreach (var source in info.Sources)
        {
            string file = SnapshotPath(folder, source.Id);
            if (IsJournalSource(source)) continue;
            if (!File.Exists(file))
            {
                problems.Add(L.F("ItemCatalogStore.TheRecordsOf0AreMissing", source.Name, Path.GetFileName(file)));
                continue;
            }
            try
            {
                var snapshot = ReadSnapshot(file);
                // 9.0.4: flag names written in an interface language by 9.0–9.0.3 are read as their language-independent names.
                observations.AddRange(snapshot.Observations.Where(o => o.SourceId == source.Id).Select(ItemFlags.Canonical));
                mappings.AddRange((snapshot.Mappings ?? []).Where(m => m.SourceId == source.Id));
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            {
                problems.Add(L.F("ItemCatalogStore.TheRecordsOf0CannotBe", source.Name, ex.Message));
            }
        }
        observations.AddRange(ReadJournal(folder, info, problems));
        foreach (string p in problems) AppLog.Warn($"Item catalog \"{info.Name}\": {p}");
        return new ItemCatalog(folder, info, observations, mappings, problems);
    }

    /// <summary>
    /// Adds a source or replaces the source with the same ID (its old records are backed up first when
    /// <paramref name="backups"/> is given). The snapshot is written before catalog.json, so an
    /// interruption never leaves catalog.json pointing to missing records.
    /// </summary>
    public static void Commit(ItemCatalog catalog, ItemImport import, BackupStore? backups = null)
    {
        var source = import.Source with { RecordCount = import.Observations.Count, ImportedAtUtc = import.Source.ImportedAtUtc == default ? DateTime.UtcNow : import.Source.ImportedAtUtc };
        if (IsJournalSource(source)) throw new InvalidOperationException($"\"{source.Name}\" records are added one by one, not imported.");
        if (import.Observations.Any(o => o.SourceId != source.Id)) throw new ArgumentException("Every record must belong to the imported source.", nameof(import));
        if (import.Mappings.Any(m => m.SourceId != source.Id)) throw new ArgumentException("Every mapping must belong to the imported source.", nameof(import));
        string file = SnapshotPath(catalog.Folder, source.Id);
        if (File.Exists(file) && backups != null)
        {
            using var set = backups.Begin(L.F("ItemCatalogStore.ItemCatalog0Replace1", catalog.Name, source.Name), BackupKind.Automatic, catalog.Folder);
            set.Add(file);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var snapshot = new SourceSnapshot(source, import.Observations.ToList(), import.Mappings.Count > 0 ? import.Mappings.ToList() : null);
        AtomicFile.Write(file, temp =>
        {
            using var stream = File.Create(temp);
            using var gzip = new GZipStream(stream, CompressionLevel.Optimal);
            JsonSerializer.Serialize(gzip, snapshot, Json);
        });
        var sources = catalog.Sources.Where(s => s.Id != source.Id).Append(source).ToList();
        var info = WriteInfo(catalog.Folder, catalog.Info with { Sources = sources });
        catalog.Replace(info, catalog.Observations.Where(o => o.SourceId != source.Id).Concat(snapshot.Observations),
            catalog.Mappings.Where(m => m.SourceId != source.Id).Concat(import.Mappings));
        AppLog.Info($"Item catalog \"{catalog.Name}\": {source.Name} imported ({source.RecordCount:N0} records).");
    }

    /// <summary>Removes a source and its records (the records file is moved to the backups when <paramref name="backups"/> is given).</summary>
    public static void RemoveSource(ItemCatalog catalog, string sourceId, BackupStore? backups = null)
    {
        var source = catalog.Source(sourceId) ?? throw new ArgumentException($"The catalog has no source \"{sourceId}\".", nameof(sourceId));
        if (IsJournalSource(source)) throw new InvalidOperationException($"\"{source.Name}\" cannot be removed as a whole; it holds records added one by one.");
        var info = WriteInfo(catalog.Folder, catalog.Info with { Sources = catalog.Sources.Where(s => s.Id != sourceId).ToList() });
        string file = SnapshotPath(catalog.Folder, sourceId);
        if (File.Exists(file))
        {
            if (backups != null)
            {
                using var set = backups.Begin(L.F("ItemCatalogStore.ItemCatalog0Remove1", catalog.Name, source.Name), BackupKind.Automatic, catalog.Folder);
                set.Add(file, move: true);
            }
            else
            {
                File.Delete(file);
            }
        }
        catalog.Replace(info, catalog.Observations.Where(o => o.SourceId != sourceId), catalog.Mappings.Where(m => m.SourceId != sourceId));
        AppLog.Info($"Item catalog \"{catalog.Name}\": source {source.Name} removed.");
    }

    /// <summary>
    /// Appends records of a journal source (client observations or decisions) one per line, flushed to
    /// disk before this returns. The source is registered first when it is new.
    /// </summary>
    public static void Append(ItemCatalog catalog, ItemSource source, IReadOnlyList<ItemObservation> observations)
    {
        if (!IsJournalSource(source)) throw new ArgumentException($"Only client observations and decisions are added one by one (\"{source.Name}\" is imported).", nameof(source));
        if (observations.Any(o => o.SourceId != source.Id)) throw new ArgumentException("Every record must belong to the given source.", nameof(observations));
        var info = catalog.Info;
        if (catalog.Source(source.Id) == null)
            info = WriteInfo(catalog.Folder, info with { Sources = info.Sources.Append(source with { RecordCount = 0 }).ToList() });
        string journal = Path.Combine(catalog.Folder, JournalFile);
        // After an interrupted write the file can end in the middle of a record: start on a new line,
        // so the new records stay readable (the unfinished one is reported and skipped when reading).
        bool needsNewLine = false;
        if (File.Exists(journal) && new FileInfo(journal).Length > 0)
        {
            using var check = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            check.Seek(-1, SeekOrigin.End);
            needsNewLine = check.ReadByte() != '\n';
        }
        using (var stream = new FileStream(journal, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            if (needsNewLine) stream.WriteByte((byte)'\n');
            foreach (var o in observations)
            {
                byte[] line = JsonSerializer.SerializeToUtf8Bytes(o, Json);
                stream.Write(line);
                stream.WriteByte((byte)'\n');
            }
            stream.Flush(flushToDisk: true);
        }
        catalog.AppendObservations(observations, info);
    }

    /// <summary>The source that holds the user's decisions in the conflict review.</summary>
    public const string DecisionsSourceId = "decisions";

    /// <summary>
    /// Records the user's decision for an identifier: the name to use (with its article), or null to
    /// withdraw an earlier decision. Decisions are kept as records of their own (never by changing
    /// another source), so what each source said stays visible.
    /// </summary>
    public static void Decide(ItemCatalog catalog, CatalogEntry entry, string? name, string? article = null, string? note = null)
    {
        var source = catalog.Source(DecisionsSourceId) ?? new ItemSource
        {
            Id = DecisionsSourceId, Kind = SourceKind.UserDecision, Name = L.T("ItemCatalogStore.YourDecisions"), IdType = IdType.Unknown, ImportedAtUtc = DateTime.UtcNow,
            Notes = L.T("ItemCatalogStore.NamesChosenInTheConflictReview"),
        };
        name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var record = new ItemObservation
        {
            SourceId = DecisionsSourceId, IdType = entry.IdType, Id = entry.Id, Name = name, Article = name == null || string.IsNullOrWhiteSpace(article) ? null : article.Trim(),
            Method = ObservationMethod.UserDecision, Status = name == null ? ObservationStatus.Unresolved : ObservationStatus.Verified, ObservedAtUtc = DateTime.UtcNow,
            Notes = note ?? (name == null ? L.T("ItemCatalogStore.EarlierDecisionWithdrawn") : null),
        };
        Append(catalog, source, [record]);
        AppLog.Info($"Item catalog \"{catalog.Name}\": {(name == null ? "decision withdrawn" : $"\"{record.FullName}\" chosen")} for {entry.Key}.");
    }

    /// <summary>
    /// Renames an identifier in this catalog. Your name is kept as a decision of its own (see
    /// <see cref="Decide"/>), so what each source says stays unchanged and visible, and
    /// <see cref="RestoreOriginalName"/> undoes it. Only a name and its article are taken from
    /// <paramref name="text"/> ("a"/"an" in front become the article): the identifier, its type and every
    /// other record are left as they are. Throws <see cref="ArgumentException"/> with the reason when the
    /// name cannot be used or is already the identifier's name.
    /// </summary>
    public static void Rename(ItemCatalog catalog, CatalogEntry entry, string text)
    {
        string cleaned = ItemNames.CleanTyped(text, out string? problem) ?? throw new ArgumentException(problem, nameof(text));
        if (cleaned == entry.BestName) throw new ArgumentException("This is already the identifier's name.", nameof(text));
        var (article, name) = ItemNames.SplitArticle(cleaned);
        string before = entry.BestName != null ? L.F("ItemCatalogStore.Was0", entry.BestName) : L.T("ItemCatalogStore.ItHadNoSettledName");
        Decide(catalog, entry, name, article, L.F("ItemCatalogStore.RenamedInTheItemCatalog0", before));
    }

    /// <summary>Removes your name for an identifier (a rename or a choice in the conflict review): it settles again on what the sources give.</summary>
    public static void RestoreOriginalName(ItemCatalog catalog, CatalogEntry entry)
    {
        if (entry.Decision?.HasName != true) throw new InvalidOperationException($"{entry.Key} has no name of yours to remove.");
        Decide(catalog, entry, null, note: L.T("ItemCatalogStore.OriginalNameRestored"));
    }

    /// <summary>Sources whose records are added one by one (journal), not imported as a whole.</summary>
    public static bool IsJournalSource(ItemSource source) => source.Kind is SourceKind.ClientObservation or SourceKind.UserDecision;

    // ------------------------------------------------------------------ files

    public static string SnapshotPath(string folder, string sourceId) => Path.Combine(folder, SourcesFolder, SafeId(sourceId) + ".json.gz");

    /// <summary>A source ID as a file name: letters, digits, '-', '_' and '.' only.</summary>
    public static string SafeId(string id)
    {
        var sb = new StringBuilder();
        foreach (char c in id) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? char.ToLowerInvariant(c) : '-');
        string safe = sb.ToString().Trim('.');
        return safe.Length == 0 ? "source" : safe;
    }

    private static CatalogInfo ReadInfo(string folder)
    {
        string file = Path.Combine(folder, CatalogFile);
        CatalogInfo info;
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            info = JsonSerializer.Deserialize<CatalogInfo>(stream, Json) ?? throw new InvalidDataException("catalog.json is empty.");
        if (info.FormatVersion > CurrentFormat)
            throw new InvalidDataException($"This catalog was written by a newer Tibia Foundry (format {info.FormatVersion}; this version reads {CurrentFormat}).");
        if (string.IsNullOrWhiteSpace(info.Name)) info = info with { Name = Path.GetFileName(folder) };
        return info with { Sources = info.Sources ?? [] };
    }

    /// <summary>
    /// Writes catalog.json. Sources whose records are added one by one and that another copy of the
    /// catalog registered meanwhile (for example another Tibia Foundry window with the same catalog
    /// open) are kept, so their records are never orphaned. Returns what was written.
    /// </summary>
    private static CatalogInfo WriteInfo(string folder, CatalogInfo info)
    {
        string file = Path.Combine(folder, CatalogFile);
        if (File.Exists(file))
        {
            try
            {
                var missing = ReadInfo(folder).Sources.Where(s => IsJournalSource(s) && info.Sources.All(x => x.Id != s.Id)).ToList();
                if (missing.Count > 0) info = info with { Sources = info.Sources.Concat(missing).ToList() };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                AppLog.Warn($"Item catalog in {folder}: catalog.json could not be read before writing it ({ex.Message}).");
            }
        }
        info = info with { FormatVersion = CurrentFormat };
        AtomicFile.WriteAllBytes(file, JsonSerializer.SerializeToUtf8Bytes(info, JsonIndented));
        return info;
    }

    private static SourceSnapshot ReadSnapshot(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<SourceSnapshot>(gzip, Json) ?? throw new InvalidDataException("The records file is empty.");
    }

    private static List<ItemObservation> ReadJournal(string folder, CatalogInfo info, List<string> problems)
    {
        var result = new List<ItemObservation>();
        string file = Path.Combine(folder, JournalFile);
        if (!File.Exists(file)) return result;
        var known = info.Sources.Where(IsJournalSource).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        string[] lines;
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            lines = reader.ReadToEnd().Split('\n');
        // A file ending in '\n' gives an empty last element; anything else after the last '\n' is an unfinished record.
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Length == 0) continue;
            bool last = i == lines.Length - 1;
            try
            {
                var o = JsonSerializer.Deserialize<ItemObservation>(line, Json);
                if (o == null || !known.Contains(o.SourceId))
                {
                    problems.Add(L.F("ItemCatalogStore.Record0Of1BelongsTo", i + 1, JournalFile));
                    continue;
                }
                result.Add(ItemFlags.Canonical(o));
            }
            catch (JsonException)
            {
                problems.Add(last
                    ? L.F("ItemCatalogStore.TheLastRecordOf0Was", JournalFile)
                    : L.F("ItemCatalogStore.Record0Of1IsDamaged", i + 1, JournalFile));
            }
        }
        return result;
    }
}
