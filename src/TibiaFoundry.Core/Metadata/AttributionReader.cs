using TibiaFoundry.Core.Diagnostics;

namespace TibiaFoundry.Core.Metadata;

/// <summary>
/// Reads existing creator information from files before they are re-exported, so the original
/// creator is preserved (PNG text chunks and XMP, GIF comments and XMP). Formats without
/// metadata (BMP) and unreadable or malformed files yield null; they never stop an export.
/// </summary>
public static class AttributionReader
{
    /// <summary>Largest file inspected; bigger files are treated as unattributed.</summary>
    private const long MaxFileBytes = 256L * 1024 * 1024;

    public static FoundAttribution? Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes) return null;
            return Read(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Attribution of {Path.GetFileName(path)} could not be read: {ex.Message}");
            return null;
        }
    }

    public static FoundAttribution? Read(ReadOnlySpan<byte> data)
    {
        if (PngMetadata.IsPng(data)) return PngMetadata.Read(data);
        if (GifMetadata.IsGif(data)) return GifMetadata.Read(data);
        return null;
    }

    /// <summary>The attribution of each file (nulls for files without any).</summary>
    public static IReadOnlyList<FoundAttribution?> ReadAll(IEnumerable<string> paths) => paths.Select(Read).ToList();
}

/// <summary>
/// Attribution for text exports, as a comment in the format's own syntax so the content keeps
/// working unchanged: Lua modules (<c>--</c>), wiki text (<c>&lt;!-- --&gt;</c>) and plain reports.
/// Data formats without comments (JSON, tab-separated tables) and verbatim client files are left
/// untouched; see docs/13_Branding_and_Attribution.md.
/// </summary>
public static class TextAttribution
{
    public static string LuaHeader(AttributionInfo info) =>
        $"-- {info.Text}\n-- {info.Software}, {Date(info)}\n";

    public static string WikiComment(AttributionInfo info) =>
        $"<!-- {info.Text.Replace("--", "- -", StringComparison.Ordinal)} -->\n";

    public static string ReportHeader(AttributionInfo info) =>
        $"{info.Text}\n{info.Software}, {Date(info)}\n\n";

    /// <summary>The attribution sentence in the first lines of a text export, or null.</summary>
    public static string? Find(string text)
    {
        foreach (string raw in text.Split('\n').Take(3))
        {
            string line = raw.Trim().TrimStart('-', '<', '!', ' ').TrimEnd('>', '-', ' ').Trim();
            if (line.StartsWith("Created by ", StringComparison.Ordinal)) return line;
        }
        return null;
    }

    /// <summary>Removes a header written by <see cref="LuaHeader"/> or <see cref="ReportHeader"/> (for comparisons).</summary>
    public static string StripHeader(string text)
    {
        var lines = text.Split('\n').ToList();
        int skip = 0;
        while (skip < lines.Count && skip < 3 &&
               (lines[skip].TrimStart('-', ' ').StartsWith("Created by ", StringComparison.Ordinal) || lines[skip].TrimStart('-', ' ').StartsWith(Common.AppIdentity.Name, StringComparison.Ordinal)))
            skip++;
        if (skip > 0 && skip < lines.Count && lines[skip].Length == 0 && !text.StartsWith("--", StringComparison.Ordinal)) skip++;
        return string.Join('\n', lines.Skip(skip));
    }

    private static string Date(AttributionInfo info) => info.Date.ToString("yyyy-MM-dd HH:mm zzz", System.Globalization.CultureInfo.InvariantCulture);
}
