using TibiaFoundry.Core.Common;
using System.IO.Enumeration;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.FileOps;

/// <summary>What a folder scan found and what it left out.</summary>
public sealed class ScanResult
{
    public List<FileInfo> Files { get; } = [];
    public List<DirectoryInfo> Directories { get; } = [];

    /// <summary>Links and junctions that were not followed (a scan never leaves the selected folder).</summary>
    public List<string> SkippedLinks { get; } = [];

    /// <summary>Entries excluded by the user's patterns.</summary>
    public int Excluded { get; set; }

    /// <summary>Folders that could not be read (access denied, removed during the scan).</summary>
    public List<string> Inaccessible { get; } = [];
}

/// <summary>
/// Folder scanning shared by the scripts: does not follow symbolic links or junctions (so a scan
/// cannot loop or reach outside the chosen folder), skips what it cannot read and reports it,
/// honors exclusion patterns and cancellation.
/// </summary>
public static class FileScanner
{
    /// <param name="excludePatterns">Names or wildcard patterns (e.g. "*.tmp", ".git") of files and folders to leave out.</param>
    public static ScanResult Scan(string root, bool recursive, IReadOnlyList<string>? excludePatterns = null, bool includeDirectories = false, CancellationToken ct = default)
    {
        var result = new ScanResult();
        var patterns = excludePatterns ?? [];
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        bool first = true;
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            if (!first && includeDirectories) result.Directories.Add(dir);
            first = false;
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = dir.EnumerateFileSystemInfos().ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                result.Inaccessible.Add(dir.FullName);
                continue;
            }
            foreach (var entry in entries)
            {
                if (patterns.Count > 0 && patterns.Any(p => FileSystemName.MatchesSimpleExpression(p, entry.Name)))
                {
                    result.Excluded++;
                    continue;
                }
                if (PathGuard.IsReparsePoint(entry))
                {
                    result.SkippedLinks.Add(entry.FullName);
                    continue;
                }
                if (entry is DirectoryInfo sub)
                {
                    if (recursive) pending.Push(sub);
                    else if (includeDirectories) result.Directories.Add(sub);
                }
                else if (entry is FileInfo file)
                {
                    result.Files.Add(file);
                }
            }
        }
        result.Files.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));
        result.Directories.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>Splits "*.tmp; .git, Thumbs.db" into patterns.</summary>
    public static IReadOnlyList<string> ParsePatterns(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : text.Split([';', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Notes for a result summary about links and unreadable folders.</summary>
    public static IEnumerable<string> Notes(ScanResult scan)
    {
        if (scan.SkippedLinks.Count > 0) yield return L.F("FileScanner.0OrJunctionSWereNot", L.Count(scan.SkippedLinks.Count, "Noun.link"));
        if (scan.Inaccessible.Count > 0) yield return L.F("FileScanner.0CouldNotBeRead1", L.Count(scan.Inaccessible.Count, "Noun.folder"), string.Join(", ", scan.Inaccessible.Take(3)), (scan.Inaccessible.Count > 3 ? " …" : ""));
        if (scan.Excluded > 0) yield return L.F("FileScanner.0N0EntrIesWereExcluded", scan.Excluded);
    }
}
