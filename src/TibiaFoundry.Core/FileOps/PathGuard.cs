using TibiaFoundry.Core.Localization;
namespace TibiaFoundry.Core.FileOps;

/// <summary>
/// Path checks shared by every tool that writes, extracts, moves or restores files. They stop a
/// file name taken from an archive, a resource file, a manifest or user input from escaping the
/// folder it belongs in (path traversal), and recognize links and other unusual entries.
/// </summary>
public static class PathGuard
{
    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>
    /// Validates a relative path made of '/' or '\' separated names and returns it with Windows
    /// separators, or null with the reason when it is unsafe: rooted or drive paths, "." or ".."
    /// segments, empty segments, characters Windows does not allow (including ':' for alternate data
    /// streams), device names such as NUL or COM1, and names ending in a dot or space.
    /// </summary>
    public static string? SafeRelativePath(string? relative, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(relative)) { problem = L.T("PathGuard.TheNameIsEmpty"); return null; }
        if (relative.Length > 1024) { problem = L.T("PathGuard.TheNameIsLongerThan1"); return null; }
        string trimmed = relative.TrimStart('/', '\\');
        if (trimmed.Length != relative.Length && relative.StartsWith(@"\\", StringComparison.Ordinal)) { problem = L.T("PathGuard.NetworkPathsAreNotAllowed"); return null; }
        if (Path.IsPathRooted(trimmed) || (trimmed.Length >= 2 && trimmed[1] == ':')) { problem = L.T("PathGuard.AbsolutePathsAreNotAllowed"); return null; }
        var parts = trimmed.Split(Separators);
        foreach (string part in parts)
        {
            if (part.Length == 0) { problem = L.T("PathGuard.ThePathContainsAnEmptyFolder"); return null; }
            if (part is "." or "..") { problem = L.T("PathGuard.AndAreNotAllowedInPaths"); return null; }
            if (WindowsNames.HasInvalidChars(part)) { problem = L.F("PathGuard.0ContainsCharactersWindowsDoesNot", part); return null; }
            if (part.EndsWith('.') || part.EndsWith(' ')) { problem = L.F("PathGuard.0EndsWithADotOr", part); return null; }
            if (WindowsNames.IsDeviceName(part)) { problem = L.F("PathGuard.0IsAReservedDeviceName", part); return null; }
        }
        return string.Join('\\', parts);
    }

    public static string? SafeRelativePath(string? relative) => SafeRelativePath(relative, out _);

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> itself or lies inside it (after full-path normalization).</summary>
    public static bool IsSameOrInside(string root, string path)
    {
        string r = Normalize(root);
        string p = Normalize(path);
        return string.Equals(r, p, StringComparison.OrdinalIgnoreCase) ||
               p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when <paramref name="path"/> lies strictly inside <paramref name="root"/>.</summary>
    public static bool IsInside(string root, string path) =>
        Normalize(path).StartsWith(Normalize(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Combines a root folder and an untrusted relative path. Throws <see cref="UnsafePathException"/>
    /// when the relative path is unsafe or the result would lie outside the root.
    /// </summary>
    public static string ResolveInside(string root, string relative)
    {
        string? safe = SafeRelativePath(relative, out string? problem)
                       ?? throw new UnsafePathException($"\"{relative}\" was refused: {problem}.");
        string full = Path.GetFullPath(Path.Combine(root, safe));
        if (!IsInside(root, full)) throw new UnsafePathException($"\"{relative}\" would be written outside {root}.");
        return full;
    }

    /// <summary>Full path without a trailing separator (drive roots keep theirs).</summary>
    public static string Normalize(string path)
    {
        string full = Path.GetFullPath(path);
        string trimmed = full.TrimEnd(Separators);
        return trimmed.Length == 0 || trimmed.EndsWith(':') ? full : trimmed;
    }

    /// <summary>Symbolic links, junctions and other reparse points (followed by many tools, so a scan could leave the folder or loop).</summary>
    public static bool IsReparsePoint(FileSystemInfo info) => (info.Attributes & FileAttributes.ReparsePoint) != 0;

    /// <summary>
    /// True when the two paths name the same location, or one contains the other. Used to refuse,
    /// for example, extracting an archive into the folder being scanned or backing a folder up into itself.
    /// </summary>
    public static bool Overlaps(string a, string b) => IsSameOrInside(a, b) || IsSameOrInside(b, a);

    /// <summary>A relative path with '/' separators, for reports and manifests (stable across machines).</summary>
    public static string ToPortable(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}

/// <summary>A path from a file, an archive or user input was refused because it is unsafe.</summary>
public sealed class UnsafePathException(string message) : IOException(message);
