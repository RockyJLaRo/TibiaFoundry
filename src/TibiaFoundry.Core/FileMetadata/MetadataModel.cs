using System.Globalization;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.FileMetadata;

/// <summary>How a metadata value is entered and checked. Every value is kept as text in an invariant form (see <see cref="MetadataValues"/>).</summary>
public enum MetadataValueKind
{
    Text,
    MultilineText,
    /// <summary>A whole number (track, year, rating).</summary>
    Integer,
    /// <summary>A number with decimals, written with a dot (GPS coordinates).</summary>
    Decimal,
    /// <summary>"yyyy-MM-dd" or "yyyy-MM-dd HH:mm:ss" (local time).</summary>
    Date,
    /// <summary>"true" or "false".</summary>
    Boolean,
    /// <summary>One of <see cref="MetadataFieldDefinition.Choices"/>.</summary>
    Choice,
    /// <summary>Several values separated by "; " (keywords, artists, genres).</summary>
    List,
}

/// <summary>What a format supports (8.6). A format declares only what is implemented and tested for it.</summary>
[Flags]
public enum MetadataCapabilities
{
    None = 0,
    /// <summary>The format is recognized (from the file's content, not only its extension).</summary>
    Identify = 1,
    /// <summary>Embedded metadata can be read.</summary>
    Read = 2,
    /// <summary>File system properties (times, attributes) can be read and changed.</summary>
    FileSystem = 4,
    /// <summary>Embedded metadata can be changed.</summary>
    Edit = 8,
    /// <summary>The same change can be applied to many files.</summary>
    Batch = 16,
    /// <summary>Single fields can be removed.</summary>
    Remove = 32,
    /// <summary>Values can be compared with other files.</summary>
    Compare = 64,
    /// <summary>Values can be exported (CSV, JSON).</summary>
    Export = 128,
    /// <summary>Values are checked before they are written.</summary>
    Validate = 256,
    /// <summary>Written to a copy, verified, then swapped in: the original is never left half-written.</summary>
    SafeSave = 512,

    ReadOnly = Identify | Read | FileSystem | Compare | Export,
    Editable = ReadOnly | Edit | Batch | Remove | Validate | SafeSave,
}

/// <summary>A metadata field: its key (shared by every format that has it, so batch edits work across formats), label, category and kind.</summary>
/// <param name="Key">Stable identifier: "title", "keywords", "fs.modified"…</param>
/// <param name="Category">The group it is shown in ("Description", "Rights", "Camera"…).</param>
public sealed record MetadataFieldDefinition(string Key, string Label, string Category, MetadataValueKind Kind)
{
    public string? Help { get; init; }
    public IReadOnlyList<string>? Choices { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    /// <summary>0: no limit.</summary>
    public int MaxLength { get; init; }
    /// <summary>Location, authorship or rights: never removed silently (removal is confirmed and reported).</summary>
    public bool Sensitive { get; init; }
}

/// <summary>A field of one file: its value (null = not set) and what may be done with it there.</summary>
public sealed record MetadataField(MetadataFieldDefinition Definition, string? Value, bool Editable, bool Removable = false)
{
    public string Key => Definition.Key;
    public string Label => Definition.Label;
    public string Category => Definition.Category;
    public bool HasValue => !string.IsNullOrEmpty(Value);
}

/// <summary>A change of one field: a new value, or null to remove it.</summary>
public sealed record MetadataEdit(string Key, string? Value)
{
    public bool IsRemoval => Value == null;
}

/// <summary>
/// What identifies the content of a file when it was read: size, last write time and the SHA-256 of
/// its first and last megabyte. Saving refuses a file whose stamp changed since (another program
/// changed it), so a newer version is never overwritten with values read from an older one.
/// </summary>
public sealed record FileStamp(long Length, DateTime LastWriteUtc, string Sample)
{
    private const int SampleBytes = 1 << 20;

    public static FileStamp Of(string path)
    {
        var info = new FileInfo(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] buffer = new byte[(int)Math.Min(SampleBytes, stream.Length)];
        stream.ReadExactly(buffer);
        sha.TransformBlock(buffer, 0, buffer.Length, null, 0);
        if (stream.Length > SampleBytes)
        {
            long tail = Math.Max(SampleBytes, stream.Length - SampleBytes);
            stream.Position = tail;
            byte[] end = new byte[(int)(stream.Length - tail)];
            stream.ReadExactly(end);
            sha.TransformBlock(end, 0, end.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return new FileStamp(info.Length, info.LastWriteTimeUtc, Convert.ToHexString(sha.Hash!));
    }
}

/// <summary>Everything read from one file: format, provider, capabilities, fields (embedded and file system), warnings and errors.</summary>
public sealed class MetadataSnapshot
{
    public required string Path { get; init; }
    /// <summary>"JPEG image", "MP3 audio"… or "Unknown".</summary>
    public required string Format { get; init; }
    public required string ProviderId { get; init; }
    public required string ProviderName { get; init; }
    public required MetadataCapabilities Capabilities { get; init; }
    public required IReadOnlyList<MetadataField> Fields { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    /// <summary>Why reading failed (partly or completely). A file with errors is never written.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
    public FileStamp? Stamp { get; init; }
    public DateTime ReadUtc { get; init; } = DateTime.UtcNow;

    public bool CanEdit => Errors.Count == 0 && Capabilities.HasFlag(MetadataCapabilities.Edit);
    public MetadataField? Field(string key) => Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));
    public string? Value(string key) => Field(key)?.Value;
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>Canonical text forms of values, and their validation.</summary>
public static class MetadataValues
{
    public const string ListSeparator = "; ";
    public const string DateFormat = "yyyy-MM-dd";
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    private static readonly string[] DateFormats = [DateTimeFormat, "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm", DateFormat];

    public static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : value.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();

    public static string? JoinList(IEnumerable<string>? items)
    {
        var list = items?.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct(StringComparer.Ordinal).ToList();
        return list == null || list.Count == 0 ? null : string.Join(ListSeparator, list);
    }

    public static string FormatDate(DateTime value) =>
        value.TimeOfDay == TimeSpan.Zero ? value.ToString(DateFormat, CultureInfo.InvariantCulture) : value.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    public static bool TryParseDate(string? text, out DateTime value) =>
        DateTime.TryParseExact(text?.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out value);

    public static string FormatNumber(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public static bool TryParseNumber(string? text, out double value) =>
        double.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    /// <summary>Brings a typed value into its canonical form ("1,5" → "1.5", "2026-1-5" stays invalid); null and blank mean "not set".</summary>
    public static string? Normalize(MetadataFieldDefinition definition, string? value)
    {
        if (value == null) return null;
        string v = definition.Kind is MetadataValueKind.MultilineText ? value.Replace("\r\n", "\n").TrimEnd() : value.Trim();
        if (v.Length == 0) return null;
        return definition.Kind switch
        {
            MetadataValueKind.List => JoinList(SplitList(v)),
            MetadataValueKind.Boolean => v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" || v.Equals("yes", StringComparison.OrdinalIgnoreCase) ? "true"
                : v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0" || v.Equals("no", StringComparison.OrdinalIgnoreCase) ? "false" : v,
            MetadataValueKind.Decimal => TryParseNumber(v, out double d) ? FormatNumber(d) : v,
            MetadataValueKind.Integer => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n.ToString(CultureInfo.InvariantCulture) : v,
            MetadataValueKind.Date => TryParseDate(v, out var date) ? FormatDate(date) : v,
            _ => v,
        };
    }

    /// <summary>Why a value cannot be written to this field, or null when it can. Null values (removal) are always valid here.</summary>
    public static string? Validate(MetadataFieldDefinition definition, string? value)
    {
        if (value == null) return null;
        if (definition.MaxLength > 0 && value.Length > definition.MaxLength) return L.F("MetadataModel.0AtMost1N0Characters", definition.Label, definition.MaxLength);
        if (value.Contains('\0')) return L.F("MetadataModel.0ContainsANullCharacter", definition.Label);
        switch (definition.Kind)
        {
            case MetadataValueKind.Integer:
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)) return L.F("MetadataModel.01IsNotAWhole", definition.Label, value);
                if (definition.Minimum is { } min && n < min || definition.Maximum is { } max && n > max) return L.F("MetadataModel.0MustBeBetween1And", definition.Label, definition.Minimum, definition.Maximum);
                break;
            case MetadataValueKind.Decimal:
                if (!TryParseNumber(value, out double d)) return L.F("MetadataModel.01IsNotANumber", definition.Label, value);
                if (definition.Minimum is { } dmin && d < dmin || definition.Maximum is { } dmax && d > dmax) return L.F("MetadataModel.0MustBeBetween1And", definition.Label, definition.Minimum, definition.Maximum);
                break;
            case MetadataValueKind.Date:
                if (!TryParseDate(value, out _)) return L.F("MetadataModel.01IsNotADate", definition.Label, value, DateFormat, DateTimeFormat);
                break;
            case MetadataValueKind.Boolean:
                if (value is not ("true" or "false")) return L.F("MetadataModel.0UseTrueOrFalse", definition.Label);
                break;
            case MetadataValueKind.Choice:
                if (definition.Choices != null && !definition.Choices.Contains(value)) return L.F("MetadataModel.01IsNotOneOf", definition.Label, value, string.Join(", ", definition.Choices));
                break;
        }
        return null;
    }
}
