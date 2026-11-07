using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.FileMetadata;

/// <summary>What a batch operation does with its field.</summary>
public enum BatchAction
{
    /// <summary>Sets the same value in every file.</summary>
    Set,
    /// <summary>Removes the field.</summary>
    Remove,
    /// <summary>Adds items to a list (keywords, artists), keeping the existing ones.</summary>
    AddItems,
    /// <summary>Removes items from a list.</summary>
    RemoveItems,
    /// <summary>Replaces one item of a list with another.</summary>
    ReplaceItem,
    /// <summary>Copies the field's value from the reference file.</summary>
    CopyFromReference,
}

/// <summary>One step of a batch edit.</summary>
/// <param name="Value">The value (Set), the items (AddItems/RemoveItems, "; " separated) or the item to replace (ReplaceItem).</param>
/// <param name="Replacement">The new item (ReplaceItem).</param>
public sealed record BatchOperation(BatchAction Action, string Key, string? Value = null, string? Replacement = null)
{
    public string Describe()
    {
        string label = MetadataFields.All.TryGetValue(Key, out var d) ? d.Label : Key;
        return Action switch
        {
            BatchAction.Set => L.F("MetadataBatch.Set0To1", label, Value),
            BatchAction.Remove => L.F("MetadataBatch.Remove0", label),
            BatchAction.AddItems => L.F("MetadataBatch.AddTo01", label, Value),
            BatchAction.RemoveItems => L.F("MetadataBatch.RemoveFrom01", label, Value),
            BatchAction.ReplaceItem => L.F("MetadataBatch.Replace0With1In2", Value, Replacement, label),
            _ => L.F("MetadataBatch.Copy0FromTheReferenceFile", label),
        };
    }
}

/// <summary>What a batch would do to one file: the edits it accepts, and the operations it cannot take with the reason.</summary>
public sealed record MetadataFilePlan(MetadataSnapshot Snapshot, IReadOnlyList<MetadataEdit> Edits, IReadOnlyList<string> Skipped);

/// <summary>A reusable set of values (8.6): author, copyright, description, license, keywords… Applying it sets only its own fields.</summary>
public sealed class MetadataPreset
{
    public string Name { get; set; } = "";
    /// <summary>Field key → value. Only keys of <see cref="MetadataFields.BatchEditable"/>.</summary>
    public Dictionary<string, string> Values { get; set; } = [];
    /// <summary>Keywords are added to the existing ones (true) or replace them (false).</summary>
    public bool AddKeywords { get; set; } = true;

    /// <summary>Why the preset cannot be used, or null.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return L.T("MetadataBatch.APresetNeedsAName");
        if (Values.Count == 0) return L.T("MetadataBatch.APresetNeedsAtLeastOne");
        foreach (var (key, value) in Values)
        {
            var definition = MetadataFields.BatchEditable.FirstOrDefault(d => d.Key == key);
            if (definition == null) return L.F("MetadataBatch.0CannotBePartOfA", key);
            if (MetadataValues.Validate(definition, MetadataValues.Normalize(definition, value)) is { } problem) return problem;
        }
        return null;
    }

    public IReadOnlyList<BatchOperation> ToOperations() =>
        Values.Where(v => !string.IsNullOrWhiteSpace(v.Value))
            .Select(v => v.Key == MetadataFields.Keywords.Key && AddKeywords ? new BatchOperation(BatchAction.AddItems, v.Key, v.Value) : new BatchOperation(BatchAction.Set, v.Key, v.Value))
            .ToList();
}

/// <summary>A field compared across files.</summary>
public sealed record MetadataComparisonRow(string Key, string Label, string Category, IReadOnlyList<string?> Values)
{
    public bool Differs => Values.Distinct(StringComparer.Ordinal).Skip(1).Any();
}

/// <summary>Batch planning, comparison and export of the metadata tool (8.6).</summary>
public static class MetadataBatch
{
    /// <summary>
    /// What the operations do to one file: each operation gives the field a new value (lists are
    /// merged), then the file's own rules decide what it accepts. Fields that are not named are never
    /// touched; operations a format cannot take are listed with the reason.
    /// </summary>
    public static MetadataFilePlan Plan(MetadataSnapshot snapshot, IReadOnlyList<BatchOperation> operations, MetadataSnapshot? reference = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var notes = new List<string>();
        string? Current(string key) => values.TryGetValue(key, out var v) ? v : snapshot.Value(key);
        foreach (var op in operations)
        {
            var field = snapshot.Field(op.Key);
            string label = field?.Label ?? (MetadataFields.All.TryGetValue(op.Key, out var d) ? d.Label : op.Key);
            if (field == null) { notes.Add(L.F("MetadataBatch.01CannotStoreIt", label, snapshot.Format)); continue; }
            bool list = field.Definition.Kind == MetadataValueKind.List;
            switch (op.Action)
            {
                case BatchAction.Set:
                    values[op.Key] = op.Value;
                    break;
                case BatchAction.Remove:
                    values[op.Key] = null;
                    break;
                case BatchAction.AddItems when list:
                    values[op.Key] = MetadataValues.JoinList(MetadataValues.SplitList(Current(op.Key)).Concat(MetadataValues.SplitList(op.Value)));
                    break;
                case BatchAction.RemoveItems when list:
                    {
                        var remove = MetadataValues.SplitList(op.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        values[op.Key] = MetadataValues.JoinList(MetadataValues.SplitList(Current(op.Key)).Where(i => !remove.Contains(i)));
                        break;
                    }
                case BatchAction.ReplaceItem when list:
                    values[op.Key] = MetadataValues.JoinList(MetadataValues.SplitList(Current(op.Key))
                        .Select(i => string.Equals(i, op.Value?.Trim(), StringComparison.OrdinalIgnoreCase) ? op.Replacement ?? "" : i));
                    break;
                case BatchAction.CopyFromReference:
                    if (reference == null) { notes.Add(L.F("MetadataBatch.0NoReferenceFileWasChosen", label)); break; }
                    if (string.Equals(reference.Path, snapshot.Path, StringComparison.OrdinalIgnoreCase)) break;
                    string? copied = reference.Value(op.Key);
                    if (copied == null) notes.Add(L.F("MetadataBatch.0EmptyInTheReferenceFile", label));
                    else values[op.Key] = copied;
                    break;
                default:
                    notes.Add(L.F("MetadataBatch.0NotAListSoItems", label));
                    break;
            }
        }
        var (accepted, refused) = MetadataEngine.Check(snapshot, values.Select(v => new MetadataEdit(v.Key, v.Value)));
        return new MetadataFilePlan(snapshot, accepted, [.. notes, .. refused]);
    }

    public static IReadOnlyList<MetadataFilePlan> Plan(IEnumerable<MetadataSnapshot> snapshots, IReadOnlyList<BatchOperation> operations, MetadataSnapshot? reference = null) =>
        snapshots.Select(s => Plan(s, operations, reference)).ToList();

    /// <summary>Every field of the files side by side (fields no file has a value for are left out).</summary>
    public static IReadOnlyList<MetadataComparisonRow> Compare(IReadOnlyList<MetadataSnapshot> snapshots, bool includeFileSystem = false)
    {
        var keys = snapshots.SelectMany(s => s.Fields).Where(f => includeFileSystem || !FileSystemMetadata.IsFileSystemKey(f.Key))
            .GroupBy(f => f.Key).Select(g => g.First()).ToList();
        return keys
            .Select(f => new MetadataComparisonRow(f.Key, f.Label, f.Category, snapshots.Select(s => s.Value(f.Key)).ToList()))
            .Where(r => r.Values.Any(v => !string.IsNullOrEmpty(v)))
            .OrderBy(r => CategoryIndex(r.Category)).ThenBy(r => r.Label, StringComparer.CurrentCulture)
            .ToList();
    }

    public static int CategoryIndex(string category)
    {
        int i = MetadataFields.CategoryOrder.ToList().IndexOf(category);
        return i < 0 ? MetadataFields.CategoryOrder.Count : i;
    }

    // ------------------------------------------------------------------ export

    /// <summary>Columns of an export: every field that has a value in at least one file, in category order.</summary>
    private static List<MetadataField> ExportFields(IReadOnlyList<MetadataSnapshot> snapshots) =>
        snapshots.SelectMany(s => s.Fields).Where(f => f.HasValue).GroupBy(f => f.Key).Select(g => g.First())
            .OrderBy(f => CategoryIndex(f.Category)).ThenBy(f => f.Label, StringComparer.Ordinal).ToList();

    /// <summary>A CSV report (UTF-8 with BOM, so spreadsheet programs read accents and other scripts; cells that would start a formula are protected).</summary>
    public static string ToCsv(IReadOnlyList<MetadataSnapshot> snapshots, IReadOnlyDictionary<string, MetadataSaveResult>? results = null)
    {
        var fields = ExportFields(snapshots);
        var sb = new StringBuilder();
        var header = new List<string> { "Path", "Format", "Reader", "Status", "Errors", "Warnings" };
        header.AddRange(fields.Select(f => $"{f.Category}: {f.Label}"));
        sb.AppendJoin(',', header.Select(ReportWriter.CsvCell)).Append("\r\n");
        foreach (var s in snapshots)
        {
            var row = new List<string> { s.Path, s.Format, s.ProviderName, StatusOf(s, results), string.Join(" | ", s.Errors), string.Join(" | ", Warnings(s, results)) };
            row.AddRange(fields.Select(f => s.Value(f.Key) ?? ""));
            sb.AppendJoin(',', row.Select(ReportWriter.CsvCell)).Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>A JSON report: one object per file with its fields (label, category, value, editable), status, errors and warnings.</summary>
    public static string ToJson(IReadOnlyList<MetadataSnapshot> snapshots, IReadOnlyDictionary<string, MetadataSaveResult>? results = null)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("application", AppIdentity.NameAndVersion);
            w.WriteString("exportedUtc", DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            w.WriteNumber("fileCount", snapshots.Count);
            w.WriteStartArray("files");
            foreach (var s in snapshots)
            {
                w.WriteStartObject();
                w.WriteString("path", s.Path);
                w.WriteString("format", s.Format);
                w.WriteString("reader", s.ProviderName);
                w.WriteString("status", StatusOf(s, results));
                w.WriteStartArray("errors");
                foreach (string e in s.Errors) w.WriteStringValue(e);
                w.WriteEndArray();
                w.WriteStartArray("warnings");
                foreach (string e in Warnings(s, results)) w.WriteStringValue(e);
                w.WriteEndArray();
                w.WriteStartObject("fields");
                foreach (var f in s.Fields.Where(f => f.HasValue))
                {
                    w.WriteStartObject(f.Key);
                    w.WriteString("label", f.Label);
                    w.WriteString("category", f.Category);
                    w.WriteString("value", f.Value);
                    w.WriteBoolean("editable", f.Editable);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string StatusOf(MetadataSnapshot s, IReadOnlyDictionary<string, MetadataSaveResult>? results) =>
        results != null && results.TryGetValue(s.Path, out var r) ? $"{r.Status}: {r.Message}" : s.Errors.Count > 0 ? L.T("MetadataBatch.NotReadable") : "Read";

    private static IEnumerable<string> Warnings(MetadataSnapshot s, IReadOnlyDictionary<string, MetadataSaveResult>? results) =>
        results != null && results.TryGetValue(s.Path, out var r) ? s.Warnings.Concat(r.Warnings).Concat(r.SkippedFields) : s.Warnings;
}
