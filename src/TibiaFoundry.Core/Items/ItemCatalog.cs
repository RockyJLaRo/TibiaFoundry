using System.Globalization;
using System.Text;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Items;

/// <summary>An identifier together with what it means (two IDs are the same only if both match).</summary>
public readonly record struct ItemKey(IdType Type, uint Id)
{
    public override string ToString() => Type == IdType.TibiaObject ? Id.ToString(CultureInfo.InvariantCulture) : $"{Type} {Id}";
}

/// <summary>Everything a catalog knows about one identifier, and the name it settles on (with the reason).</summary>
public sealed class CatalogEntry
{
    internal CatalogEntry(ItemKey key, List<ItemObservation> observations, IReadOnlyDictionary<string, ItemSource> sources)
    {
        Key = key;
        Observations = observations;
        // Names observed in the client are added one by one: a later record of the same source (your
        // confirmation, correction or rejection, or a new reading) replaces the earlier ones.
        Current = observations.Where((o, i) => !(sources.TryGetValue(o.SourceId, out var s) && s.Kind == SourceKind.ClientObservation
                                                 && observations.Skip(i + 1).Any(later => later.SourceId == o.SourceId))).ToList();
        InOfficialClient = observations.Any(o => sources.TryGetValue(o.SourceId, out var s) && s.Kind == SourceKind.OfficialClient);
        Official = Current.LastOrDefault(o => o.HasName && o.Status == ObservationStatus.Verified && sources.TryGetValue(o.SourceId, out var s) && s.IsOfficial);
        Decision = observations.LastOrDefault(o => o.Method == ObservationMethod.UserDecision);
        NameGroups = Current.Where(o => o.HasName && o.Method != ObservationMethod.UserDecision && o.Status != ObservationStatus.Failed)
            .GroupBy(o => ItemNames.Key(o.Name)).Select(g => (IReadOnlyList<ItemObservation>)g.ToList()).ToList();
        int distinctSources = NameGroups.SelectMany(g => g).Select(o => o.SourceId).Distinct().Count();

        if (Decision != null && Decision.HasName)
        {
            Agreement = Agreement.Decided;
            BestObservation = Decision;
            BestNameReason = L.T("ItemCatalog.ChosenByYou");
        }
        else if (Official != null)
        {
            Agreement = Agreement.VerifiedOfficial;
            BestObservation = Official;
            BestNameReason = sources.TryGetValue(Official.SourceId, out var s) ? s.Name : Official.SourceId;
        }
        else if (NameGroups.Count == 0)
        {
            Agreement = Agreement.NoName;
        }
        else if (NameGroups.Count == 1)
        {
            Agreement = distinctSources >= 2 ? Agreement.Corroborated : Agreement.SingleSource;
            // A real name is preferred over a TibiaWiki article title with the same spelling.
            BestObservation = NameGroups[0].FirstOrDefault(o => !o.NameIsTitle) ?? NameGroups[0][0];
            BestNameReason = distinctSources >= 2 ? L.F("ItemCatalog.0SourcesAgree", distinctSources) : (sources.TryGetValue(BestObservation.SourceId, out var s) ? s.Name : BestObservation.SourceId);
        }
        else
        {
            Agreement = Agreement.Conflict;
            BestNameReason = L.F("ItemCatalog.0DifferentNames", NameGroups.Count);
        }
        string? bestKey = BestName == null ? null : ItemNames.Key(BestName);
        HasDisagreement = NameGroups.Count > 1 || bestKey != null && NameGroups.Any(g => ItemNames.Key(g[0].Name) != bestKey);
    }

    public ItemKey Key { get; }
    public IdType IdType => Key.Type;
    public uint Id => Key.Id;

    /// <summary>All records of this identifier, in source order (journal records last, oldest first).</summary>
    public IReadOnlyList<ItemObservation> Observations { get; }

    /// <summary>The records that count: like <see cref="Observations"/>, but of names observed in the client only the latest per source.</summary>
    public IReadOnlyList<ItemObservation> Current { get; }

    /// <summary>The official named record (client data or a name observed in the official client), if any.</summary>
    public ItemObservation? Official { get; }

    /// <summary>The user's latest decision in the conflict review, if any.</summary>
    public ItemObservation? Decision { get; }

    /// <summary>Named records grouped by name (case and a leading article do not matter).</summary>
    public IReadOnlyList<IReadOnlyList<ItemObservation>> NameGroups { get; }

    public Agreement Agreement { get; }

    /// <summary>The record whose name the catalog settles on (null when there is none or the sources disagree).</summary>
    public ItemObservation? BestObservation { get; }

    /// <summary>The name this catalog settles on, with its article; null when there is none or the sources disagree.</summary>
    public string? BestName => BestObservation?.FullName;

    /// <summary>Why <see cref="BestName"/> was chosen (or why there is none).</summary>
    public string? BestNameReason { get; }

    /// <summary>An official client source lists this identifier (named or not).</summary>
    public bool InOfficialClient { get; }

    /// <summary>The client lists it but gives it no name.</summary>
    public bool UnnamedInClient => InOfficialClient && Official == null;

    /// <summary>Some source gives a name that differs from the others (or from the settled name).</summary>
    public bool HasDisagreement { get; }

    /// <summary>The client's own name, if any (its record is official).</summary>
    public string? ClientName => Official?.FullName;

    public string? Category => Observations.FirstOrDefault(o => o.Category != null && o.Status == ObservationStatus.Verified)?.Category
                               ?? Observations.FirstOrDefault(o => o.Category != null)?.Category;

    public IReadOnlyList<string> Flags => Observations.Where(o => o.Flags != null).SelectMany(o => o.Flags!).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Every distinct name with the sources that give it ("demon helmet (client, TibiaWiki)").</summary>
    public string NamesBySource(IReadOnlyDictionary<string, ItemSource> sources) => string.Join(" | ", NameGroups.Select(g =>
        $"{g[0].FullName} ({string.Join(", ", g.Select(o => sources.TryGetValue(o.SourceId, out var s) ? s.Name : o.SourceId).Distinct())})"));

    /// <summary>Text the search looks in: every name, the category and the notes.</summary>
    internal string SearchText { get; set; } = "";
}

/// <summary>What a catalog list shows (all conditions must hold).</summary>
public sealed record CatalogQuery
{
    /// <summary>
    /// Search text: an ID ("3031"), an ID range ("3031-3035"), an exact name ("=gold coin") or part of
    /// any name, category or note (case does not matter).
    /// </summary>
    public string? Text { get; init; }
    public Agreement? Agreement { get; init; }
    /// <summary>Only identifiers this source has a record of.</summary>
    public string? WithSource { get; init; }
    /// <summary>Only identifiers this source has no record of.</summary>
    public string? WithoutSource { get; init; }
    /// <summary>Only objects the client lists without a name.</summary>
    public bool OnlyUnnamedInClient { get; init; }
    /// <summary>Only identifiers whose sources disagree.</summary>
    public bool OnlyDisagreements { get; init; }
    public IdType? IdType { get; init; }
    public string? Category { get; init; }

    public bool Matches(CatalogEntry entry)
    {
        if (IdType is { } type && entry.IdType != type) return false;
        if (Agreement is { } agreement && entry.Agreement != agreement) return false;
        if (OnlyUnnamedInClient && !entry.UnnamedInClient) return false;
        if (OnlyDisagreements && !entry.HasDisagreement) return false;
        if (WithSource != null && entry.Observations.All(o => o.SourceId != WithSource)) return false;
        if (WithoutSource != null && entry.Observations.Any(o => o.SourceId == WithoutSource)) return false;
        if (Category != null && !string.Equals(entry.Category, Category, StringComparison.OrdinalIgnoreCase)) return false;
        string text = (Text ?? "").Trim();
        if (text.Length == 0) return true;
        if (uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out uint id)) return entry.Id == id;
        int dash = text.IndexOf('-');
        if (dash > 0 && uint.TryParse(text[..dash].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint from)
                     && uint.TryParse(text[(dash + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint to))
            return entry.Id >= Math.Min(from, to) && entry.Id <= Math.Max(from, to);
        if (text.StartsWith('=')) return entry.Observations.Any(o => o.HasName && ItemNames.Same(o.Name, text[1..]));
        return entry.SearchText.Contains(text, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Descriptive information of a catalog (catalog.json).</summary>
public sealed record CatalogInfo
{
    public int FormatVersion { get; init; } = ItemCatalogStore.CurrentFormat;
    public required string Name { get; init; }
    public string? Description { get; init; }
    public DateTime CreatedUtc { get; init; }
    public List<ItemSource> Sources { get; init; } = [];
}

/// <summary>
/// An item name and ID catalog: sources, their records and the entries built from them. Records of
/// different identifier types are kept apart; official and other data are told apart by their
/// sources; conflicting names are reported, never merged. Read with <see cref="ItemCatalogStore"/>.
/// </summary>
public sealed class ItemCatalog
{
    private readonly List<ItemObservation> _observations;
    private readonly List<IdMapping> _mappings;
    private ILookup<ItemKey, IdMapping> _mappingsFrom = Array.Empty<IdMapping>().ToLookup(m => new ItemKey(m.FromType, m.FromId));
    private ILookup<ItemKey, IdMapping> _mappingsTo = Array.Empty<IdMapping>().ToLookup(m => new ItemKey(m.ToType, m.ToId));
    private Dictionary<ItemKey, CatalogEntry> _index = new();
    private List<CatalogEntry> _entries = [];

    internal ItemCatalog(string folder, CatalogInfo info, List<ItemObservation> observations, List<IdMapping> mappings, List<string> loadProblems)
    {
        Folder = folder;
        Info = info;
        _observations = observations;
        _mappings = mappings;
        LoadProblems = loadProblems;
        Rebuild();
    }

    public string Folder { get; }
    public CatalogInfo Info { get; internal set; }
    public string Name => Info.Name;
    public IReadOnlyList<ItemSource> Sources => Info.Sources;
    public IReadOnlyDictionary<string, ItemSource> SourcesById { get; private set; } = new Dictionary<string, ItemSource>();
    public IReadOnlyList<ItemObservation> Observations => _observations;
    public IReadOnlyList<CatalogEntry> Entries => _entries;

    /// <summary>Correspondences between identifier types stated by the sources (for example an items.otb's own IDs → client IDs).</summary>
    public IReadOnlyList<IdMapping> Mappings => _mappings;

    /// <summary>Mappings that start at an identifier (whatever version they state).</summary>
    public IEnumerable<IdMapping> MappingsFrom(IdType type, uint id) => _mappingsFrom[new ItemKey(type, id)];

    /// <summary>Mappings that lead to an identifier.</summary>
    public IEnumerable<IdMapping> MappingsTo(IdType type, uint id) => _mappingsTo[new ItemKey(type, id)];

    /// <summary>What could not be read when the catalog was opened (for example an incomplete last record).</summary>
    public IReadOnlyList<string> LoadProblems { get; }

    /// <summary>The official client source, if the catalog has one.</summary>
    public ItemSource? ClientSource => Sources.FirstOrDefault(s => s.Kind == SourceKind.OfficialClient);

    public ItemSource? Source(string id) => SourcesById.GetValueOrDefault(id);

    public CatalogEntry? Find(IdType type, uint id) => _index.GetValueOrDefault(new ItemKey(type, id));

    public IEnumerable<CatalogEntry> Query(CatalogQuery query) => _entries.Where(query.Matches);

    internal void Replace(CatalogInfo info, IEnumerable<ItemObservation> observations, IEnumerable<IdMapping>? mappings = null)
    {
        var kept = observations.ToList(); // may be computed from the current records: read them before clearing
        var keptMappings = mappings?.ToList();
        Info = info;
        _observations.Clear();
        _observations.AddRange(kept);
        if (keptMappings != null)
        {
            _mappings.Clear();
            _mappings.AddRange(keptMappings);
        }
        Rebuild();
    }

    /// <summary>Records per source (journal sources have no stored count).</summary>
    public int CountOf(string sourceId) => _observations.Count(o => o.SourceId == sourceId);

    /// <summary>
    /// Adds records one by one (journal records). Only the entries of the identifiers that got records
    /// are built again; the others are kept as they are, so a rename or a reading does not rebuild a
    /// catalog of tens of thousands of entries. New sources are only ever added by a journal write,
    /// and an existing entry has no records of them, so the kept entries stay correct.
    /// </summary>
    internal void AppendObservations(IEnumerable<ItemObservation> observations, CatalogInfo info)
    {
        var added = observations.ToList();
        Info = info;
        SourcesById = Info.Sources.ToDictionary(s => s.Id, StringComparer.Ordinal);
        _observations.AddRange(added);
        var touched = new HashSet<ItemKey>();
        foreach (var o in added)
        {
            var key = new ItemKey(o.IdType, o.Id);
            if (!_groups.TryGetValue(key, out var list)) _groups[key] = list = [];
            list.Add(o);
            touched.Add(key);
        }
        foreach (var key in touched)
        {
            var entry = BuildEntry(key, _groups[key]);
            bool existed = _index.ContainsKey(key);
            _index[key] = entry;
            int at = _entries.BinarySearch(entry, EntryOrder);
            if (existed && at >= 0) _entries[at] = entry;
            else _entries.Insert(at >= 0 ? at : ~at, entry);
        }
    }

    /// <summary>The order of <see cref="Entries"/>: by identifier type, then by identifier.</summary>
    private static readonly Comparer<CatalogEntry> EntryOrder = Comparer<CatalogEntry>.Create((a, b) =>
        a.IdType != b.IdType ? a.IdType.CompareTo(b.IdType) : a.Id.CompareTo(b.Id));

    private Dictionary<ItemKey, List<ItemObservation>> _groups = new();

    private void Rebuild()
    {
        SourcesById = Info.Sources.ToDictionary(s => s.Id, StringComparer.Ordinal);
        _mappingsFrom = _mappings.ToLookup(m => new ItemKey(m.FromType, m.FromId));
        _mappingsTo = _mappings.ToLookup(m => new ItemKey(m.ToType, m.ToId));
        var groups = new Dictionary<ItemKey, List<ItemObservation>>();
        foreach (var o in _observations)
        {
            var key = new ItemKey(o.IdType, o.Id);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(o);
        }
        _groups = groups;
        _index = new Dictionary<ItemKey, CatalogEntry>(groups.Count);
        foreach (var (key, list) in groups) _index[key] = BuildEntry(key, list);
        _entries = _index.Values.OrderBy(e => e.IdType).ThenBy(e => e.Id).ToList();
    }

    private CatalogEntry BuildEntry(ItemKey key, List<ItemObservation> list)
    {
        // The entry gets its own copy of the records: later appends must not change an entry handed out before.
        var entry = new CatalogEntry(key, list.ToList(), SourcesById);
        var sb = new StringBuilder();
        foreach (var o in list)
        {
            if (o.HasName) sb.Append(o.FullName).Append('\n');
            if (o.Category != null) sb.Append(o.Category).Append('\n');
            if (o.Notes != null) sb.Append(o.Notes).Append('\n');
        }
        entry.SearchText = sb.ToString();
        return entry;
    }

    /// <summary>
    /// What importing would change, before anything is written: records added, changed and removed
    /// against the source it replaces (the same source ID), duplicates, problems, and how the new
    /// names relate to the official ones.
    /// </summary>
    public ImportPreview Preview(ItemImport import)
    {
        var replaces = Source(import.Source.Id);
        var before = replaces == null ? new Dictionary<ItemKey, ItemObservation>()
            : _observations.Where(o => o.SourceId == replaces.Id).GroupBy(o => new ItemKey(o.IdType, o.Id)).ToDictionary(g => g.Key, g => g.First());
        int added = 0, changed = 0, unchanged = 0, disagree = 0, notInClient = 0, namesForUnnamed = 0;
        var seen = new HashSet<ItemKey>();
        var changedExamples = new List<string>();
        bool hasClient = ClientSource != null && ClientSource.Id != import.Source.Id;
        foreach (var o in import.Observations)
        {
            var key = new ItemKey(o.IdType, o.Id);
            seen.Add(key);
            if (!before.TryGetValue(key, out var old)) added++;
            else if (Differs(old, o))
            {
                changed++;
                if (changedExamples.Count < 10) changedExamples.Add($"{key}: \"{old.FullName ?? "(no name)"}\" → \"{o.FullName ?? "(no name)"}\"");
            }
            else unchanged++;
            if (!hasClient || o.IdType != IdType.TibiaObject) continue;
            var entry = Find(key.Type, key.Id);
            if (entry == null || !entry.InOfficialClient) notInClient++;
            else if (o.HasName && entry.Official != null && !ItemNames.Same(entry.Official.Name, o.Name)) disagree++;
            else if (o.HasName && entry.UnnamedInClient) namesForUnnamed++;
        }
        int removed = before.Keys.Count(k => !seen.Contains(k));
        var duplicateNames = import.Observations.Where(o => o.HasName).GroupBy(o => ItemNames.Key(o.Name)).Where(g => g.Select(o => o.Id).Distinct().Count() > 1)
            .OrderByDescending(g => g.Count()).ToList();
        return new ImportPreview(import, replaces, import.Observations.Count, import.Observations.Count(o => o.HasName), added, changed, removed, unchanged,
            changedExamples, duplicateNames.Count, duplicateNames.Take(10).Select(g => $"\"{g.First().Name}\": {string.Join(", ", g.Select(o => o.Id).Distinct().Take(8))}{(g.Count() > 8 ? ", …" : "")}").ToList(),
            hasClient ? disagree : null, hasClient ? notInClient : null, hasClient ? namesForUnnamed : null)
        {
            DuplicateNames = duplicateNames.Take(500).Select(g => new DuplicateName(g.First().Name ?? "", g.Select(o => o.Id).Distinct().OrderBy(id => id).ToList())).ToList(),
        };
    }

    private static bool Differs(ItemObservation a, ItemObservation b) =>
        a.Name != b.Name || a.Article != b.Article || a.Plural != b.Plural || a.Category != b.Category || a.NameIsTitle != b.NameIsTitle || a.Status != b.Status ||
        !(a.Flags ?? []).SequenceEqual(b.Flags ?? []);
}

/// <summary>Records read from a file or the client, not yet part of a catalog.</summary>
public sealed class ItemImport
{
    public required ItemSource Source { get; init; }
    public required IReadOnlyList<ItemObservation> Observations { get; init; }
    /// <summary>Lines or records that were not used, and why (malformed, duplicate, out of range…).</summary>
    public IReadOnlyList<ImportProblem> Problems { get; init; } = [];

    /// <summary>Correspondences between identifier types the file states (for example items.otb ID → client ID).</summary>
    public IReadOnlyList<IdMapping> Mappings { get; init; } = [];
}

/// <summary>A line or record that could not be used. Line 0 = not tied to a line.</summary>
public sealed record ImportProblem(int Line, string Message)
{
    public override string ToString() => Line > 0 ? L.F("ItemCatalog.Line01", Line, Message) : Message;
}

/// <summary>A name that several identifiers share ("flower pot": 316, 321, 324…).</summary>
public sealed record DuplicateName(string Name, IReadOnlyList<uint> Ids);

/// <summary>What an import would change; shown before the user confirms.</summary>
/// <param name="DisagreeWithClient">Names that differ from the client's own name (null when the catalog has no client source).</param>
/// <param name="NotInClient">Identifiers the client does not list (null when the catalog has no client source).</param>
/// <param name="NamesForUnnamed">Names for objects the client lists without a name (null when the catalog has no client source).</param>
public sealed record ImportPreview(ItemImport Import, ItemSource? Replaces, int Records, int Named, int Added, int Changed, int Removed, int Unchanged,
    IReadOnlyList<string> ChangedExamples, int DuplicateNameCount, IReadOnlyList<string> DuplicateNameExamples, int? DisagreeWithClient, int? NotInClient, int? NamesForUnnamed)
{
    /// <summary>Nothing usable was read: importing is not offered.</summary>
    public bool IsEmpty => Records == 0;

    /// <summary>Names used by more than one identifier, most used first (at most 500; <see cref="DuplicateNameCount"/> counts all), for a scrollable list.</summary>
    public IReadOnlyList<DuplicateName> DuplicateNames { get; init; } = [];

    public string ToText()
    {
        var s = Import.Source;
        var sb = new StringBuilder();
        sb.AppendLine($"{s.Name} ({s.Trust}{(s.Version != null ? ", version " + s.Version : "")})");
        sb.AppendLine(L.F("ItemCatalog.Identifiers0", ItemNames.Describe(s.IdType, s.Version)));
        if (s.Origin != null) sb.AppendLine(L.F("ItemCatalog.From0", s.Origin));
        sb.AppendLine(L.F("ItemCatalog.0N0Records1N0With", Records, Named));
        if (Replaces != null)
            sb.AppendLine(L.F("ItemCatalog.ReplacesTheSourceImported0Yyyy", Replaces.ImportedAtUtc.ToLocalTime(), Added, Changed, Removed, Unchanged));
        else sb.AppendLine(L.F("ItemCatalog.NewSource0N0RecordsAdded", Added));
        foreach (string e in ChangedExamples) sb.AppendLine("  changed " + e);
        if (DisagreeWithClient != null)
        {
            sb.AppendLine(L.F("ItemCatalog.ComparedWithTheClient0N0", NamesForUnnamed, DisagreeWithClient, NotInClient));
            if (DisagreeWithClient > 0) sb.AppendLine(L.T("ItemCatalog.DifferingNamesAreKeptAsThis2"));
        }
        if (DuplicateNameCount > 0)
        {
            sb.AppendLine(L.F("ItemCatalog.0N0NamesAreUsedBy", DuplicateNameCount));
            foreach (string d in DuplicateNameExamples) sb.AppendLine("  " + d);
        }
        if (Import.Problems.Count > 0)
        {
            sb.AppendLine(L.Count(Import.Problems.Count, "ItemCatalog.LinesNotUsed"));
            foreach (var p in Import.Problems.Take(25)) sb.AppendLine("  " + p);
            if (Import.Problems.Count > 25) sb.AppendLine(L.F("ItemCatalog.And0N0More2", Import.Problems.Count - 25));
        }
        return sb.ToString();
    }
}
