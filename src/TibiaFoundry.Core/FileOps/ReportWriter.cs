using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TibiaFoundry.Core.Common;

namespace TibiaFoundry.Core.FileOps;

/// <summary>Formats a report can be saved in.</summary>
public enum ReportFormat { Csv, Json, Text }

/// <summary>
/// A table of results (columns and rows of text) with a title and summary lines, which every tool
/// and script can show in the user interface and save as CSV, JSON or text.
/// </summary>
public sealed class ReportTable
{
    public ReportTable(string title, IReadOnlyList<string> columns)
    {
        Title = title;
        Columns = columns;
    }

    public string Title { get; }
    public IReadOnlyList<string> Columns { get; }
    public List<IReadOnlyList<string>> Rows { get; } = [];

    /// <summary>Summary lines shown above the table (counts, totals, notes).</summary>
    public List<string> Summary { get; } = [];

    public void Add(params string[] values)
    {
        if (values.Length != Columns.Count) throw new ArgumentException($"Expected {Columns.Count} values, got {values.Length}.", nameof(values));
        Rows.Add(values);
    }
}

/// <summary>Writes <see cref="ReportTable"/>s. Files are written atomically.</summary>
public static class ReportWriter
{
    public static string Extension(ReportFormat format) => format switch
    {
        ReportFormat.Json => ".json",
        ReportFormat.Text => ".txt",
        _ => ".csv",
    };

    public static ReportFormat FormatFromPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => ReportFormat.Json,
        ".txt" => ReportFormat.Text,
        _ => ReportFormat.Csv,
    };

    /// <param name="header">Optional first lines (for example the attribution header); written as comments in CSV and text.</param>
    public static void Save(ReportTable table, string path, string? header = null) =>
        AtomicFile.Write(path, temp => WriteTo(temp, table, FormatFromPath(path), header));

    /// <summary>Writes a report in the given format to a path (used with temporary files of an <see cref="OutputSession"/>).</summary>
    public static void WriteTo(string path, ReportTable table, ReportFormat format, string? header = null)
    {
        string content = format switch
        {
            ReportFormat.Json => ToJson(table, header),
            ReportFormat.Text => ToText(table, header),
            _ => ToCsv(table, header),
        };
        // CSV with a byte order mark so Excel opens UTF-8 names correctly.
        File.WriteAllText(path, content, new UTF8Encoding(format == ReportFormat.Csv));
    }

    public static string ToCsv(ReportTable table, string? header = null)
    {
        var sb = new StringBuilder();
        if (header != null)
            foreach (var line in header.Split('\n')) sb.Append("# ").Append(line.TrimEnd('\r')).Append("\r\n");
        sb.AppendJoin(',', table.Columns.Select(CsvCell)).Append("\r\n");
        foreach (var row in table.Rows) sb.AppendJoin(',', row.Select(CsvCell)).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// One CSV cell (RFC 4180 quoting). Text that a spreadsheet would run as a formula (starting
    /// with =, +, - or @ and not a number) gets a leading apostrophe, so opening a report of
    /// untrusted file names cannot execute anything.
    /// </summary>
    public static string CsvCell(string value)
    {
        value ??= "";
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' &&
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        bool quote = value.IndexOfAny([',', '"', '\r', '\n']) >= 0 || value.StartsWith(' ') || value.EndsWith(' ');
        return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static string ToJson(ReportTable table, string? header = null)
    {
        var rows = table.Rows.Select(r =>
        {
            var o = new Dictionary<string, string>(table.Columns.Count);
            for (int i = 0; i < table.Columns.Count; i++) o[table.Columns[i]] = r[i];
            return o;
        });
        var doc = new Dictionary<string, object?>
        {
            ["title"] = table.Title,
            ["header"] = header,
            ["summary"] = table.Summary,
            ["columns"] = table.Columns,
            ["rows"] = rows,
        };
        return JsonSerializer.Serialize(doc, JsonOptions);
    }

    public static string ToText(ReportTable table, string? header = null)
    {
        var sb = new StringBuilder();
        if (header != null) sb.Append(header.TrimEnd()).Append("\r\n\r\n");
        sb.Append(table.Title).Append("\r\n");
        foreach (var s in table.Summary) sb.Append(s).Append("\r\n");
        sb.Append("\r\n");
        int[] widths = table.Columns.Select((c, i) => Math.Min(80, Math.Max(c.Length, table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r[i].Length)))).ToArray();
        sb.AppendJoin("  ", table.Columns.Select((c, i) => c.PadRight(widths[i]))).Append("\r\n");
        sb.AppendJoin("  ", widths.Select(w => new string('-', w))).Append("\r\n");
        foreach (var row in table.Rows) sb.AppendJoin("  ", row.Select((v, i) => v.PadRight(widths[i]))).Append("\r\n");
        return sb.ToString();
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Size in human-readable units (1,234 bytes, 2.5 MB...).</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes";
        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = -1;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString(value < 10 ? "0.0#" : value < 100 ? "0.0" : "0", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
