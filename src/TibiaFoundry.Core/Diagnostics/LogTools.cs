using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TibiaFoundry.Core.Diagnostics;

/// <summary>One parsed log entry.</summary>
public sealed record LogEntry(DateTime Time, LogLevel Level, string Source, string? Operation, string Message, string? Details = null)
{
    public string LevelName => Level.ToString();
}

/// <summary>
/// Masks secrets before anything is logged or exported: passwords, tokens, API keys, session ids,
/// authorization headers and secret-looking URL query parameters. Tibia Foundry itself handles
/// no credentials; this protects against values that reach a message by accident (for example a
/// URL or an exception text).
/// </summary>
public static partial class LogRedactor
{
    public const string Mask = "[redacted]";

    [GeneratedRegex(@"(?i)\b(password|passwd|pwd|secret|token|access[_-]?token|refresh[_-]?token|id[_-]?token|api[_-]?key|apikey|access[_-]?key|client[_-]?secret|session[_-]?id|sessionid|sid|auth|authorization|cookie|private[_-]?key)(\s*[=:]\s*)(""[^""]*""|'[^']*'|[^\s&;,""']+)")]
    private static partial Regex KeyValue();

    [GeneratedRegex(@"(?i)\b(bearer|basic)\s+[A-Za-z0-9\-._~+/]{8,}=*")]
    private static partial Regex AuthorizationValue();

    [GeneratedRegex(@"(?i)([?&](?:key|token|access_token|api_key|apikey|sig|signature|code|password|session)=)[^&#\s]+")]
    private static partial Regex QueryValue();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"\b(?:gh[pous]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|xox[abpr]-[A-Za-z0-9-]{10,}|sk-[A-Za-z0-9_-]{20,}|AKIA[0-9A-Z]{16})\b")]
    private static partial Regex KnownTokenFormats();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        string t = PrivateKeyBlock().Replace(text, Mask);
        t = AuthorizationValue().Replace(t, m => m.Groups[1].Value + " " + Mask);
        t = QueryValue().Replace(t, m => m.Groups[1].Value + Mask);
        t = KeyValue().Replace(t, m => m.Groups[1].Value + m.Groups[2].Value + Mask);
        t = KnownTokenFormats().Replace(t, Mask);
        return t;
    }
}

/// <summary>Criteria of the Diagnostics page's log view (all optional).</summary>
public sealed record LogFilter
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public LogLevel MinimumLevel { get; init; } = LogLevel.Debug;
    public string? Source { get; init; }
    public string? Operation { get; init; }
    public string? Text { get; init; }

    public bool Matches(LogEntry e) =>
        e.Level >= MinimumLevel
        && (From == null || e.Time >= From) && (To == null || e.Time <= To)
        && (string.IsNullOrWhiteSpace(Source) || string.Equals(e.Source, Source, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(Operation) || string.Equals(e.Operation, Operation, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(Text) || e.Message.Contains(Text, StringComparison.OrdinalIgnoreCase) || (e.Details?.Contains(Text, StringComparison.OrdinalIgnoreCase) ?? false));
}

/// <summary>Reads the log files back into entries (this version's format and the shorter one of earlier versions).</summary>
public static partial class LogReader
{
    // 2026-10-13 12:00:00.000 [Info    ] [T  1] [Source] [op:abcd] message
    // 2026-10-12 12:00:00.000 [Info   ] [T  1] message            (5.0 and earlier)
    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(\w+)\s*\] \[T\s*\d+\](?: \[([^\]]*)\])?(?: \[op:([^\]]*)\])? ?(.*)$")]
    private static partial Regex LinePattern();

    public static LogEntry? ParseLine(string line)
    {
        var m = LinePattern().Match(line);
        if (!m.Success) return null;
        if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;
        var level = m.Groups[2].Value switch
        {
            "Debug" => LogLevel.Debug,
            "Info" => LogLevel.Info,
            "Warning" => LogLevel.Warning,
            "Error" => LogLevel.Error,
            "Critical" => LogLevel.Critical,
            _ => LogLevel.Info,
        };
        string source = m.Groups[3].Success && m.Groups[3].Value.Length > 0 ? m.Groups[3].Value : "App";
        string? op = m.Groups[4].Success ? m.Groups[4].Value : null;
        return new LogEntry(time, level, source, op, m.Groups[5].Value);
    }

    /// <summary>
    /// Reads entries from log files (oldest file first), attaching indented continuation lines
    /// (exception details) to their entry. Files are read while the logger keeps writing. At most
    /// <paramref name="limit"/> matching entries are returned (the newest ones).
    /// </summary>
    public static IReadOnlyList<LogEntry> Read(IEnumerable<string> files, LogFilter? filter = null, int limit = 5000)
    {
        filter ??= new LogFilter();
        var result = new LinkedList<LogEntry>();
        foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(file)) continue;
            LogEntry? current = null;
            StringBuilder? details = null;
            void Flush()
            {
                if (current == null) return;
                var e = details != null ? current with { Details = details.ToString().TrimEnd() } : current;
                if (filter.Matches(e))
                {
                    result.AddLast(e);
                    if (result.Count > limit) result.RemoveFirst();
                }
                current = null;
                details = null;
            }
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                while (reader.ReadLine() is { } line)
                {
                    if (ParseLine(line) is { } entry)
                    {
                        Flush();
                        current = entry;
                    }
                    else if (current != null)
                    {
                        (details ??= new StringBuilder()).AppendLine(line.StartsWith("    ", StringComparison.Ordinal) ? line[4..] : line);
                    }
                }
                Flush();
            }
            catch (IOException ex)
            {
                AppLog.Verbose($"Log file {Path.GetFileName(file)} could not be read: {ex.Message}");
            }
        }
        return result.ToList();
    }
}
