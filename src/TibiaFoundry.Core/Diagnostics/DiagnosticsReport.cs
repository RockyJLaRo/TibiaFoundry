using TibiaFoundry.Core.Localization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TibiaFoundry.Core.Common;

namespace TibiaFoundry.Core.Diagnostics;

/// <summary>
/// Removes personal information from text that goes into a diagnostics report: known personal
/// folders become placeholders (%USERPROFILE%, %DOCUMENTS%, %APPDATA%, %LOCALAPPDATA%, %TEMP%,
/// %PROGRAM%), the Windows user name and computer name become &lt;user&gt; and &lt;computer&gt;, other
/// absolute paths keep only their drive and last two names, e-mail addresses are masked, and
/// secrets are masked by <see cref="LogRedactor"/>. With <see cref="KeepPaths"/> (an explicit opt-in)
/// only secrets are masked.
/// </summary>
public sealed partial class DiagnosticsSanitizer
{
    private readonly List<(string Path, string Token)> _folders = [];
    private readonly string? _user;
    private readonly string? _machine;

    public bool KeepPaths { get; }

    /// <param name="folders">Folders to replace (path → placeholder), longest first after sorting.</param>
    public DiagnosticsSanitizer(bool keepPaths, IEnumerable<(string Path, string Token)> folders, string? userName, string? machineName)
    {
        KeepPaths = keepPaths;
        foreach (var (path, token) in folders)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length < 4) continue;
            string trimmed = path.TrimEnd('\\', '/');
            _folders.Add((trimmed, token));
            _folders.Add((trimmed.Replace('\\', '/'), token));
        }
        _folders.Sort((a, b) => b.Path.Length.CompareTo(a.Path.Length));
        _user = userName is { Length: >= 2 } ? userName : null;
        _machine = machineName is { Length: >= 2 } ? machineName : null;
    }

    /// <summary>The sanitizer for this machine and user.</summary>
    public static DiagnosticsSanitizer ForCurrentUser(bool keepPaths, string programFolder, IEnumerable<(string Path, string Token)>? extra = null)
    {
        var folders = new List<(string, string)>
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "%DOCUMENTS%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "%DESKTOP%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Path.GetTempPath(), "%TEMP%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            (programFolder, "%PROGRAM%"),
        };
        if (Environment.GetEnvironmentVariable("OneDrive") is { Length: > 3 } oneDrive) folders.Add((oneDrive, "%ONEDRIVE%"));
        if (extra != null) folders.AddRange(extra);
        return new DiagnosticsSanitizer(keepPaths, folders, Environment.UserName, Environment.MachineName);
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    /// <summary>Absolute Windows paths (drive or UNC) up to a character that cannot be part of one in a message.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:[A-Za-z]:\\|\\\\[^\\\s""'<>|]+\\)[^""'<>|\r\n\t]*?(?=[""'<>|\r\n\t]|[,;)\]](?:\s|$)|\.(?:\s|$)|\s[-—(]|$)")]
    private static partial Regex AbsolutePath();

    public string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        string t = LogRedactor.Redact(text);
        if (KeepPaths) return t;
        foreach (var (path, token) in _folders) t = t.Replace(path, token, StringComparison.OrdinalIgnoreCase);
        t = AbsolutePath().Replace(t, m => ShortenPath(m.Value));
        t = Email().Replace(t, "<e-mail>");
        // The computer name before the user name: it often contains it ("DANA-PC").
        if (_machine != null) t = Regex.Replace(t, $@"(?<![A-Za-z0-9]){Regex.Escape(_machine)}(?![A-Za-z0-9])", "<computer>", RegexOptions.IgnoreCase);
        if (_user != null) t = Regex.Replace(t, $@"(?<![A-Za-z0-9]){Regex.Escape(_user)}(?![A-Za-z0-9])", "<user>", RegexOptions.IgnoreCase);
        return t;
    }

    /// <summary>
    /// Sanitizes every string value of a JSON document (paths inside JSON are escaped, so plain text
    /// replacement would miss them). Property names are kept.
    /// </summary>
    public string SanitizeJson(string json)
    {
        var node = JsonNode.Parse(json);
        return Walk(node)?.ToJsonString(DiagnosticsReport.Json) ?? "null";
    }

    private JsonNode? Walk(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                var obj = new JsonObject();
                foreach (var (key, value) in o) obj[key] = Walk(value?.DeepClone());
                return obj;
            case JsonArray a:
                var arr = new JsonArray();
                foreach (var item in a) arr.Add(Walk(item?.DeepClone()));
                return arr;
            case JsonValue v when v.TryGetValue(out string? s):
                return JsonValue.Create(Sanitize(s));
            default:
                return node?.DeepClone();
        }
    }

    /// <summary>"D:\Games\Tibia\packages\Tibia\assets" → "D:\…\Tibia\assets" (drive kept, the middle removed).</summary>
    private static string ShortenPath(string path)
    {
        string p = path.TrimEnd();
        string trailing = path[p.Length..];
        var parts = p.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 3) return path;
        string root = p.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\<network>" : parts[0];
        return $@"{root}\…\{parts[^2]}\{parts[^1]}" + trailing;
    }
}

/// <summary>One file of a diagnostics report.</summary>
public sealed record ReportEntry(string Name, string Category, string Description, byte[] Content)
{
    public static ReportEntry Text(string name, string category, string description, string text) =>
        new(name, category, description, new UTF8Encoding(false).GetBytes(text));

    public long Size => Content.LongLength;
    public string Sha256 => Convert.ToHexString(SHA256.HashData(Content)).ToLowerInvariant();
}

/// <summary>A category the user can include in or leave out of a report.</summary>
public sealed record ReportCategory(string Id, string Name, string Description, bool Optional = true, bool IncludedByDefault = true);

/// <summary>
/// Builds diagnostics reports: a ZIP archive with a readable summary, machine-readable details
/// and sanitized logs, plus a manifest of its contents. Reports are saved locally only; nothing
/// is uploaded or sent anywhere.
/// </summary>
public static class DiagnosticsReport
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, // e.g. a window position that was never set (NaN)
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IReadOnlyList<ReportCategory> Categories { get; } =
    [
        new("summary", L.T("Common.Summary"), L.T("DiagnosticsReport.Section.summary"), Optional: false),
        new("system", L.T("Label.Applicationandsystem"), "Versions of Tibia Foundry, Windows, .NET and the bundled libraries; processor count; memory in use", Optional: false),
        new("settings", L.T("Tool.settings.Name"), L.T("DiagnosticsReport.Section.settings")),
        new("tools", L.T("Label.Toolsanddependencies"), L.T("DiagnosticsReport.Section.tools")),
        new("selfcheck", L.T("Diagnostics.SelfCheck"), L.T("DiagnosticsReport.Section.selfcheck")),
        new("operations", L.T("Diagnostics.RecentOperations"), L.T("DiagnosticsReport.Section.operations")),
        new("metrics", L.T("Common.Performance"), L.T("DiagnosticsReport.Section.metrics")),
        new("logs", L.T("HealthArea.Logs"), L.T("DiagnosticsReport.Section.logs")),
    ];

    /// <summary>The manifest: what the report contains and how it was made.</summary>
    public static ReportEntry Manifest(IReadOnlyList<ReportEntry> entries, bool sanitized, DateTime createdUtc) =>
        ReportEntry.Text("manifest.json", "manifest", "List of the files in this report with their sizes and SHA-256 checksums",
            JsonSerializer.Serialize(new
            {
                format = "TibiaFoundry.DiagnosticsReport",
                formatVersion = 1,
                application = AppIdentity.NameAndVersion,
                created = createdUtc.ToString("u"),
                sanitized,
                sanitizedNote = sanitized
                    ? "Folder paths, the Windows user name and the computer name were replaced by placeholders; passwords, tokens and keys are masked."
                    : "Full folder paths were included at the user's request; passwords, tokens and keys are still masked.",
                transmitted = false,
                files = entries.Select(e => new { name = e.Name, category = e.Category, description = e.Description, bytes = e.Size, sha256 = e.Sha256 }),
            }, Json));

    /// <summary>Writes the report (entries plus manifest) as a ZIP file in one step (written to a temporary file first).</summary>
    public static void WriteZip(string path, IReadOnlyList<ReportEntry> entries, bool sanitized, DateTime createdUtc)
    {
        var all = entries.Append(Manifest(entries, sanitized, createdUtc)).ToList();
        AtomicFile.Write(path, temp =>
        {
            using var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
            foreach (var e in all)
            {
                var item = zip.CreateEntry(e.Name, CompressionLevel.Optimal);
                item.LastWriteTime = createdUtc.ToLocalTime();
                using var s = item.Open();
                s.Write(e.Content);
            }
        });
    }
}
