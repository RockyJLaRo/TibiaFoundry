using System.Globalization;
using System.Text;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Items;

/// <summary>
/// One side of a comparison: a catalog, and either one of its sources (that source's records) or
/// all of them (each identifier's settled name).
/// </summary>
public sealed record ComparisonSide(ItemCatalog Catalog, string? SourceId)
{
    public string Label => SourceId == null ? L.F("CatalogComparison.0SettledNames", Catalog.Name) : $"{Catalog.Name}: {Catalog.Source(SourceId)?.Name ?? SourceId}";
}

public sealed record ComparisonOptions
{
    /// <summary>Compare items.otb IDs through the items.otb → client IDs of each side's items.otb.</summary>
    public bool UseMappings { get; init; } = true;

    /// <summary>
    /// Compare the client IDs of an items.otb as Tibia object IDs. This is an assumption (the file was
    /// made for one client version); the result states it and how often names agree under it.
    /// </summary>
    public bool OtbClientIdsAreObjectIds { get; init; }
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ChangeKind>))]
public enum ChangeKind
{
    /// <summary>Only in the second side.</summary>
    Added,
    /// <summary>Only in the first side.</summary>
    Removed,
    Renamed,
    /// <summary>The first side has no name, the second has one.</summary>
    NameAdded,
    /// <summary>The first side has a name, the second has none.</summary>
    NameRemoved,
    CategoryChanged,
    FlagsChanged,
    /// <summary>The same items.otb ID maps to another client ID.</summary>
    Remapped,
}

/// <summary>A difference for one identifier.</summary>
/// <param name="BehaviorSensitive">The object is a container, door, teleport, depot, bed… on either side: a change can break maps or scripts.</param>
public sealed record ComparisonRow(ChangeKind Kind, IdType IdType, uint Id, string? OldName, string? NewName, string? OldValue, string? NewValue,
    bool BehaviorSensitive, string? Note = null);

/// <summary>
/// Compares two catalogs, or two sources: added and removed identifiers, renamed items, names that
/// appear or disappear, category and property changes, items.otb IDs mapped to other client IDs, client
/// IDs claimed by several items.otb IDs, and which changes concern behavior-sensitive objects. Nothing is
/// merged: the comparison only reports.
/// </summary>
public sealed class CatalogComparison
{
    /// <summary>Properties whose change can break maps or scripts (doors, teleports, containers…).</summary>
    public static readonly IReadOnlySet<string> BehaviorFlags = new HashSet<string>(StringComparer.Ordinal)
    {
        "container", "door", "teleport", "depot", "mailbox", L.T("CatalogComparison.TrashHolder"), "bed", L.T("CatalogComparison.FloorChange"), L.T("CatalogComparison.MagicField"), "key", "podium",
    };

    private sealed record SideItem(string? Name, string? Category, IReadOnlySet<string> Flags, ItemKey Original);

    public required string LeftLabel { get; init; }
    public required string RightLabel { get; init; }
    public required IReadOnlyList<ComparisonRow> Rows { get; init; }
    public required int Matched { get; init; }
    public required int Unchanged { get; init; }
    /// <summary>Assumptions and evidence (for example how many names agree when OTB client IDs are compared as object IDs).</summary>
    public required IReadOnlyList<string> Notes { get; init; }
    /// <summary>Client IDs that several items.otb IDs map to, per side ("left: client 3031 ← items.otb 2148, 2149").</summary>
    public required IReadOnlyList<string> MappingConflicts { get; init; }
    /// <summary>Identifiers with no name on either side (incomplete definitions).</summary>
    public required int UnnamedOnBothSides { get; init; }

    public int Count(ChangeKind kind) => Rows.Count(r => r.Kind == kind);
    public int BehaviorSensitive => Rows.Count(r => r.BehaviorSensitive);

    public static CatalogComparison Compare(ComparisonSide left, ComparisonSide right, ComparisonOptions? options = null)
    {
        options ??= new ComparisonOptions();
        var notes = new List<string>();
        var conflicts = new List<string>();
        var a = Collect(left, options, conflicts, "left");
        var b = Collect(right, options, conflicts, "right");
        if (options.OtbClientIdsAreObjectIds)
            notes.Add(L.T("CatalogComparison.AssumptionTheClientIDsOfEach"));

        // Properties are compared only where both sides can express them (a client has no "door" property, an items.otb no "market").
        var comparable = a.Values.SelectMany(i => i.Flags).ToHashSet(StringComparer.Ordinal);
        comparable.IntersectWith(b.Values.SelectMany(i => i.Flags));

        var rows = new List<ComparisonRow>();
        int matched = 0, unchanged = 0, unnamedBoth = 0, namedBoth = 0, sameName = 0;
        foreach (var key in a.Keys.Union(b.Keys).OrderBy(k => k.Type).ThenBy(k => k.Id))
        {
            bool inA = a.TryGetValue(key, out var x), inB = b.TryGetValue(key, out var y);
            bool sensitive = (x?.Flags ?? new HashSet<string>()).Overlaps(BehaviorFlags) || (y?.Flags ?? new HashSet<string>()).Overlaps(BehaviorFlags);
            if (!inA) { rows.Add(new ComparisonRow(ChangeKind.Added, key.Type, key.Id, null, y!.Name, null, Describe(y), sensitive, OriginalNote(key, y.Original))); continue; }
            if (!inB) { rows.Add(new ComparisonRow(ChangeKind.Removed, key.Type, key.Id, x!.Name, null, Describe(x), null, sensitive, OriginalNote(key, x.Original))); continue; }
            matched++;
            bool changed = false;
            if (x!.Name == null && y!.Name == null) unnamedBoth++;
            else if (x.Name == null) { rows.Add(new ComparisonRow(ChangeKind.NameAdded, key.Type, key.Id, null, y!.Name, null, null, false)); changed = true; }
            else if (y!.Name == null) { rows.Add(new ComparisonRow(ChangeKind.NameRemoved, key.Type, key.Id, x.Name, null, null, null, false)); changed = true; }
            else
            {
                namedBoth++;
                if (ItemNames.Same(x.Name, y.Name)) sameName++;
                else { rows.Add(new ComparisonRow(ChangeKind.Renamed, key.Type, key.Id, x.Name, y.Name, null, null, sensitive)); changed = true; }
            }
            if (x.Category != null && y!.Category != null && !string.Equals(x.Category, y.Category, StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(new ComparisonRow(ChangeKind.CategoryChanged, key.Type, key.Id, x.Name, y.Name, x.Category, y.Category, false));
                changed = true;
            }
            var fx = x.Flags.Where(comparable.Contains).ToHashSet(StringComparer.Ordinal);
            var fy = y!.Flags.Where(comparable.Contains).ToHashSet(StringComparer.Ordinal);
            if (!fx.SetEquals(fy))
            {
                rows.Add(new ComparisonRow(ChangeKind.FlagsChanged, key.Type, key.Id, x.Name, y.Name, string.Join(", ", fx.Except(fy).Order()), string.Join(", ", fy.Except(fx).Order()),
                    fx.Union(fy).Any(BehaviorFlags.Contains), L.T("CatalogComparison.LeftValueOnlyOnTheLeft")));
                changed = true;
            }
            if (!changed) unchanged++;
        }

        // Server IDs present on both sides whose client ID differs (two items.otb versions).
        if (options.UseMappings)
        {
            var mapA = ServerToClient(left);
            var mapB = ServerToClient(right);
            foreach (var (server, clientA) in mapA.OrderBy(kv => kv.Key))
            {
                if (!mapB.TryGetValue(server, out uint clientB) || clientA == clientB) continue;
                var flags = FlagsOfServer(left, server).Union(FlagsOfServer(right, server));
                rows.Add(new ComparisonRow(ChangeKind.Remapped, IdType.OtbServer, server, NameOfServer(left, server), NameOfServer(right, server),
                    "client " + clientA.ToString(CultureInfo.InvariantCulture), "client " + clientB.ToString(CultureInfo.InvariantCulture), flags.Any(BehaviorFlags.Contains)));
            }
        }

        if (options.OtbClientIdsAreObjectIds && namedBoth > 0)
            notes.Add(L.F("CatalogComparison.UnderThisAssumptionNamesAgreeFor", sameName, namedBoth, 100.0 * sameName / namedBoth));
        if (conflicts.Count > 0)
            notes.Add(L.F("CatalogComparison.0N0ClientIDsAreClaimed", conflicts.Count));
        return new CatalogComparison
        {
            LeftLabel = left.Label, RightLabel = right.Label, Rows = rows, Matched = matched, Unchanged = unchanged, Notes = notes, MappingConflicts = conflicts,
            UnnamedOnBothSides = unnamedBoth,
        };
    }

    private static string? OriginalNote(ItemKey key, ItemKey original) => original == key ? null : L.F("CatalogComparison.From01", original.Type, original.Id);

    private static string? Describe(SideItem item)
    {
        var parts = new List<string>();
        if (item.Category != null) parts.Add(item.Category);
        if (item.Flags.Count > 0) parts.Add(string.Join(", ", item.Flags.Order()));
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    /// <summary>The side's items keyed by the identifier they are compared under.</summary>
    private static Dictionary<ItemKey, SideItem> Collect(ComparisonSide side, ComparisonOptions options, List<string> conflicts, string label)
    {
        var catalog = side.Catalog;
        var items = new List<(ItemKey Key, SideItem Item)>();
        if (side.SourceId != null)
        {
            foreach (var o in catalog.Observations.Where(o => o.SourceId == side.SourceId))
                items.Add((new ItemKey(o.IdType, o.Id), new SideItem(o.FullName, o.Category, (o.Flags ?? []).ToHashSet(StringComparer.Ordinal), new ItemKey(o.IdType, o.Id))));
        }
        else
        {
            foreach (var e in catalog.Entries)
                items.Add((e.Key, new SideItem(e.BestName, e.Category, e.Flags.ToHashSet(StringComparer.Ordinal), e.Key)));
        }

        var result = new Dictionary<ItemKey, SideItem>();
        var claimed = new Dictionary<ItemKey, List<uint>>();
        foreach (var (key, item) in items.OrderBy(i => i.Key.Type).ThenBy(i => i.Key.Id))
        {
            var target = key;
            if (options.UseMappings && key.Type == IdType.OtbServer && catalog.MappingsFrom(IdType.OtbServer, key.Id).FirstOrDefault(m => m.ToType == IdType.OtbClient) is { } mapping)
                target = new ItemKey(IdType.OtbClient, mapping.ToId);
            if (options.OtbClientIdsAreObjectIds && target.Type == IdType.OtbClient) target = new ItemKey(IdType.TibiaObject, target.Id);
            if (target != key)
            {
                if (!claimed.TryGetValue(target, out var servers)) claimed[target] = servers = [];
                servers.Add(key.Id);
            }
            if (!result.TryAdd(target, item)) continue;   // a client ID claimed again: the lowest items.otb ID stays
        }
        foreach (var (target, servers) in claimed.Where(kv => kv.Value.Count > 1).OrderBy(kv => kv.Key.Id))
            conflicts.Add(L.F("CatalogComparison.0Client1ItemsOtb2", label, target.Id, string.Join(", ", servers)));
        return result;
    }

    private static Dictionary<uint, uint> ServerToClient(ComparisonSide side)
    {
        var map = new Dictionary<uint, uint>();
        foreach (var m in side.Catalog.Mappings.Where(m => m.FromType == IdType.OtbServer && m.ToType == IdType.OtbClient && (side.SourceId == null || m.SourceId == side.SourceId)))
            map.TryAdd(m.FromId, m.ToId);
        return map;
    }

    private static IEnumerable<string> FlagsOfServer(ComparisonSide side, uint server) =>
        side.Catalog.Find(IdType.OtbServer, server)?.Flags ?? [];

    private static string? NameOfServer(ComparisonSide side, uint server) => side.Catalog.Find(IdType.OtbServer, server)?.BestName;

    public static string DescribeKind(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "added",
        ChangeKind.Removed => "removed",
        ChangeKind.Renamed => "renamed",
        ChangeKind.NameAdded => L.T("CatalogComparison.NameAdded"),
        ChangeKind.NameRemoved => L.T("CatalogComparison.NameRemoved"),
        ChangeKind.CategoryChanged => L.T("CatalogComparison.CategoryChanged"),
        ChangeKind.FlagsChanged => L.T("CatalogComparison.PropertiesChanged"),
        _ => "remapped",
    };

    public ReportTable ToTable()
    {
        var table = new ReportTable(L.F("CatalogComparison.Comparison01", LeftLabel, RightLabel),
            ["Change", L.T("CatalogComparison.IDType"), "ID", L.T("CatalogComparison.LeftName"), L.T("CatalogComparison.RightName"), L.T("CatalogComparison.LeftValue"), L.T("CatalogComparison.RightValue"), "Behavior-sensitive", "Note"]);
        table.Summary.AddRange(SummaryLines());
        foreach (var r in Rows)
            table.Add(DescribeKind(r.Kind), r.IdType.ToString(), r.Id.ToString(CultureInfo.InvariantCulture), r.OldName ?? "", r.NewName ?? "", r.OldValue ?? "", r.NewValue ?? "",
                r.BehaviorSensitive ? "yes" : "", r.Note ?? "");
        return table;
    }

    public IEnumerable<string> SummaryLines()
    {
        yield return L.F("CatalogComparison.Left0", LeftLabel);
        yield return L.F("CatalogComparison.Right0", RightLabel);
        yield return
            L.F("CatalogComparison.0N0IdentifiersOnBothSides", Matched, Unchanged, Count(ChangeKind.Added), Count(ChangeKind.Removed), Count(ChangeKind.Renamed), Count(ChangeKind.NameAdded), Count(ChangeKind.NameRemoved), Count(ChangeKind.CategoryChanged), Count(ChangeKind.FlagsChanged), Count(ChangeKind.Remapped));
        yield return L.F("CatalogComparison.0N0ChangesConcernContainersDoors", BehaviorSensitive, UnnamedOnBothSides);
        foreach (string note in Notes) yield return note;
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (string line in SummaryLines()) sb.AppendLine(line);
        if (MappingConflicts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(L.T("CatalogComparison.ClientIDsClaimedBySeveralItems"));
            foreach (string c in MappingConflicts.Take(200)) sb.AppendLine("  " + c);
            if (MappingConflicts.Count > 200) sb.AppendLine(L.F("CatalogComparison.And0N0More", MappingConflicts.Count - 200));
        }
        return sb.ToString();
    }
}
