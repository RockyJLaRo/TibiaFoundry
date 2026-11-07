using System.Globalization;
using System.Text;
using System.Text.Json;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;

namespace TibiaFoundry.Core.Items;

/// <summary>
/// Catalog exports. Every format names the identifier type and the selection, so a number in an
/// export is never ambiguous; CSV and JSON keep the provenance of every record.
/// </summary>
public static class ItemExports
{
    public const string JsonFormatName = "tibia-foundry-item-catalog";
    public const int JsonFormatVersion = 1;

    /// <summary>The JSON export (also read by <see cref="ItemImporters.FromCatalogJson"/>).</summary>
    public sealed record CatalogExport
    {
        /// <summary>Always <see cref="JsonFormatName"/> in an export; no default, so other JSON files are recognized as such.</summary>
        public string? Format { get; init; }
        public int FormatVersion { get; init; }
        public string? Catalog { get; init; }
        public DateTime ExportedUtc { get; init; }
        public string? Selection { get; init; }
        public string? Exporter { get; init; }
        public List<ItemSource>? Sources { get; init; }
        public List<ItemObservation>? Observations { get; init; }
        public List<IdMapping>? Mappings { get; init; }
    }

    private static readonly JsonSerializerOptions JsonIndented = new(ItemCatalogStore.Json) { WriteIndented = true };

    /// <summary>Header lines (without comment markers): what was exported, from where, and what the numbers mean.</summary>
    public static string Header(ItemCatalog catalog, IdType idType, string selection)
    {
        string version = catalog.ClientSource?.Version ?? "unknown";
        var lines = new List<string>
        {
            $"Tibia Foundry {AppIdentity.Version} item catalog \"{catalog.Name}\", exported {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}",
            $"Identifiers: {ItemNames.Describe(idType, idType == IdType.TibiaObject ? null : version)}" + (idType == IdType.TibiaObject && catalog.ClientSource != null ? $"; client {version}" : ""),
            $"Selection: {selection}",
            "Official names come from the client's own data; other names keep their source (community and third-party data are not verified).",
        };
        // TibiaWiki's content is licensed CC BY-SA: what is published with it must say so and carry the same license.
        if (catalog.Sources.Any(s => s.Kind == SourceKind.TibiaWiki))
            lines.Add("Contains data from TibiaWiki (https://tibia.fandom.com/) by TibiaWiki contributors, licensed CC BY-SA; publish it under the same license.");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// "[id] name" lines (the format GetItemNames writes and LapisItemEditor reads, where the number is
    /// a client object ID) for entries with a settled name, preceded by '#' comment lines that say what
    /// the numbers mean. Entries without a settled name are left out and counted in the header.
    /// </summary>
    public static string ToNameList(ItemCatalog catalog, IEnumerable<CatalogEntry> entries, IdType idType, string selection)
    {
        var list = entries.Where(e => e.IdType == idType).ToList();
        var named = list.Where(e => e.BestName != null).ToList();
        var sb = new StringBuilder();
        // '#' lines are comments for Tibia Foundry; LapisItemEditor skips lines without ']', so the header must not contain one.
        foreach (string line in Header(catalog, idType, selection).Split('\n')) sb.Append("# ").Append(line.Replace(']', ')')).Append("\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"# {named.Count:N0} names; {list.Count - named.Count:N0} selected identifiers without a settled name are left out.\r\n");
        foreach (var e in named) sb.Append('[').Append(e.Id.ToString(CultureInfo.InvariantCulture)).Append("] ").Append(e.BestName).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>One row per identifier: the settled name, how sure it is, and every other name with its sources.</summary>
    public static ReportTable EntriesTable(ItemCatalog catalog, IEnumerable<CatalogEntry> entries)
    {
        var table = new ReportTable($"Item catalog {catalog.Name}",
            ["ID type", "ID", "Name", "Article", "Agreement", "Reason", "Client name", "Names by source", "Category", "Flags", "Sources"]);
        foreach (var e in entries)
        {
            var best = e.BestObservation;
            table.Add(e.IdType.ToString(), e.Id.ToString(CultureInfo.InvariantCulture), best?.Name ?? "", best?.Article ?? "", e.Agreement.ToString(), e.BestNameReason ?? "",
                e.ClientName ?? "", e.NamesBySource(catalog.SourcesById), e.Category ?? "", string.Join(";", e.Flags),
                string.Join(";", e.Observations.Select(o => o.SourceId).Distinct()));
        }
        return table;
    }

    /// <summary>One row per record of the selected identifiers, with its full provenance.</summary>
    public static ReportTable ObservationsTable(ItemCatalog catalog, IEnumerable<CatalogEntry> entries)
    {
        var table = new ReportTable($"Item catalog {catalog.Name}: records",
            ["Source", "Source name", "Trust", "Distribution", "Version", "ID type", "ID", "Name", "Article", "Plural", "Name is title", "Category", "Flags",
             "Status", "Method", "Observed", "Evidence", "Notes"]);
        foreach (var o in entries.SelectMany(e => e.Observations))
        {
            var s = catalog.Source(o.SourceId);
            table.Add(o.SourceId, s?.Name ?? "", s?.Trust ?? "", s?.Distribution ?? "", s?.Version ?? "", o.IdType.ToString(), o.Id.ToString(CultureInfo.InvariantCulture),
                o.Name ?? "", o.Article ?? "", o.Plural ?? "", o.NameIsTitle ? "yes" : "", o.Category ?? "", o.Flags == null ? "" : string.Join(";", o.Flags),
                o.Status.ToString(), o.Method.ToString(), o.ObservedAtUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "", o.Evidence ?? "", o.Notes ?? "");
        }
        return table;
    }

    /// <summary>The catalog's sources: what each is, how far it can be trusted, and where it came from.</summary>
    public static ReportTable SourcesTable(ItemCatalog catalog)
    {
        var table = new ReportTable($"Item catalog {catalog.Name}: sources",
            ["Source", "Name", "Kind", "Trust", "Identifiers", "Distribution", "Version", "Records", "Imported", "Data from", "Origin", "SHA-256", "Notes"]);
        foreach (var s in catalog.Sources)
            table.Add(s.Id, s.Name, s.Kind.ToString(), s.Trust, ItemNames.Describe(s.IdType, s.Version), s.Distribution ?? "", s.Version ?? "",
                catalog.CountOf(s.Id).ToString(CultureInfo.InvariantCulture), s.ImportedAtUtc == default ? "" : s.ImportedAtUtc.ToString("u", CultureInfo.InvariantCulture),
                s.SourceTimestampUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "", s.Origin ?? "", s.FileSha256 ?? "", s.Notes ?? "");
        return table;
    }

    /// <summary>The selected identifiers with all their records and the sources they come from (re-importable).</summary>
    public static string ToJson(ItemCatalog catalog, IEnumerable<CatalogEntry> entries, string selection)
    {
        var observations = entries.SelectMany(e => e.Observations).ToList();
        var used = observations.Select(o => o.SourceId).ToHashSet(StringComparer.Ordinal);
        var export = new CatalogExport
        {
            Format = JsonFormatName, FormatVersion = JsonFormatVersion,
            Catalog = catalog.Name, ExportedUtc = DateTime.UtcNow, Selection = selection, Exporter = AppIdentity.NameAndVersion,
            Sources = catalog.Sources.Where(s => used.Contains(s.Id)).Select(s => s with { RecordCount = observations.Count(o => o.SourceId == s.Id) }).ToList(),
            Observations = observations,
            Mappings = catalog.Mappings.Where(m => used.Contains(m.SourceId)).ToList(),
        };
        return JsonSerializer.Serialize(export, JsonIndented);
    }
}

/// <summary>Coverage of one source: how much it names, and how it relates to the client.</summary>
/// <param name="InClient">Identifiers the client lists (null without a client source; also for the client itself).</param>
public sealed record SourceCoverage(ItemSource Source, int Records, int Named, int Titles, int? InClient, int? NotInClient, int? SameAsClient,
    int? DifferentFromClient, int? NamesForUnnamed);

/// <summary>
/// How complete and how consistent a catalog is, per source and overall. Completeness is only ever
/// stated relative to a defined source: "the client lists N objects, of which M have a name".
/// </summary>
public sealed class CatalogCoverage
{
    public int Entries { get; private init; }
    public IReadOnlyDictionary<Agreement, int> ByAgreement { get; private init; } = new Dictionary<Agreement, int>();
    public int Disagreements { get; private init; }
    /// <summary>Identifiers named by two or more sources.</summary>
    public int CrossChecked { get; private init; }
    public string? ClientVersion { get; private init; }
    public int? ClientObjects { get; private init; }
    public int? ClientNamed { get; private init; }
    public int? ClientUnnamed { get; private init; }
    /// <summary>Objects the client does not name that another source names.</summary>
    public int? UnnamedNamedElsewhere { get; private init; }
    /// <summary>Objects the client does not name and no source names.</summary>
    public int? UnnamedNamedNowhere { get; private init; }
    public IReadOnlyList<SourceCoverage> Sources { get; private init; } = [];

    public static CatalogCoverage Of(ItemCatalog catalog)
    {
        var client = catalog.ClientSource;
        var entries = catalog.Entries;
        var clientEntries = client == null ? null : entries.Where(e => e.InOfficialClient).ToList();
        var sources = new List<SourceCoverage>();
        foreach (var source in catalog.Sources)
        {
            var records = catalog.Observations.Where(o => o.SourceId == source.Id).ToList();
            int? inClient = null, notInClient = null, same = null, different = null, forUnnamed = null;
            if (client != null && source.Id != client.Id && source.IdType == IdType.TibiaObject)
            {
                inClient = notInClient = same = different = forUnnamed = 0;
                foreach (var o in records)
                {
                    var e = catalog.Find(o.IdType, o.Id)!;
                    if (!e.InOfficialClient) { notInClient++; continue; }
                    inClient++;
                    if (!o.HasName) continue;
                    if (e.Official == null) forUnnamed++;
                    else if (ItemNames.Same(e.Official.Name, o.Name)) same++;
                    else different++;
                }
            }
            sources.Add(new SourceCoverage(source, records.Count, records.Count(o => o.HasName), records.Count(o => o.NameIsTitle), inClient, notInClient, same, different, forUnnamed));
        }
        return new CatalogCoverage
        {
            Entries = entries.Count,
            ByAgreement = Enum.GetValues<Agreement>().ToDictionary(a => a, a => entries.Count(e => e.Agreement == a)),
            Disagreements = entries.Count(e => e.HasDisagreement),
            CrossChecked = entries.Count(e => e.Observations.Where(o => o.HasName).Select(o => o.SourceId).Distinct().Count() >= 2),
            ClientVersion = client?.Version,
            ClientObjects = clientEntries?.Count,
            ClientNamed = clientEntries?.Count(e => e.Official != null),
            ClientUnnamed = clientEntries?.Count(e => e.UnnamedInClient),
            UnnamedNamedElsewhere = clientEntries?.Count(e => e.UnnamedInClient && e.NameGroups.Count > 0),
            UnnamedNamedNowhere = clientEntries?.Count(e => e.UnnamedInClient && e.NameGroups.Count == 0),
            Sources = sources,
        };
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        string N(int? n) => n?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
        sb.AppendLine($"{Entries:N0} identifiers in the catalog.");
        if (ClientObjects != null)
        {
            sb.AppendLine($"Client {ClientVersion ?? "(version unknown)"}: {N(ClientObjects)} objects, {N(ClientNamed)} named by the client, {N(ClientUnnamed)} without a name in the client.");
            sb.AppendLine($"  Of the objects without a client name: {N(UnnamedNamedElsewhere)} are named by another source (not verified), {N(UnnamedNamedNowhere)} by no source.");
            sb.AppendLine("  Completeness relative to this client: every object it lists is in the catalog; verified names exist for the objects the client names.");
        }
        else
        {
            sb.AppendLine("No client source: completeness cannot be stated (import the client's data to compare against it).");
        }
        sb.AppendLine($"Settled names: {ByAgreement[Agreement.VerifiedOfficial]:N0} official, {ByAgreement[Agreement.Decided]:N0} chosen by you, " +
                      $"{ByAgreement[Agreement.Corroborated]:N0} agreed by two or more sources, {ByAgreement[Agreement.SingleSource]:N0} from a single source; " +
                      $"{ByAgreement[Agreement.Conflict]:N0} conflicts, {ByAgreement[Agreement.NoName]:N0} without any name.");
        sb.AppendLine($"{Disagreements:N0} identifiers have differing names; {CrossChecked:N0} are named by two or more sources.");
        sb.AppendLine();
        foreach (var s in Sources)
        {
            sb.AppendLine($"{s.Source.Name} ({s.Source.Trust}{(s.Source.Version != null ? ", " + s.Source.Version : "")}): {s.Records:N0} records, {s.Named:N0} named" +
                          (s.Titles > 0 ? $" ({s.Titles:N0} are article titles)" : "") + ".");
            if (s.InClient != null)
                sb.AppendLine($"  {N(s.InClient)} listed by the client, {N(s.NotInClient)} not listed by it; names: {N(s.SameAsClient)} same as the client's, " +
                              $"{N(s.DifferentFromClient)} different, {N(s.NamesForUnnamed)} for objects the client does not name.");
        }
        return sb.ToString();
    }
}
