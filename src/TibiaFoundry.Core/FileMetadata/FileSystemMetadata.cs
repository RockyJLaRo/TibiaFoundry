using TibiaFoundry.Core.FileOps;
using F = TibiaFoundry.Core.FileMetadata.MetadataFields;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.FileMetadata;

/// <summary>
/// The file system's own properties of a file (8.6): name, folder, size, times and attributes. They
/// are kept by Windows outside the file's content, so they are shown apart ("File system") and
/// changing them never touches the content.
/// </summary>
public static class FileSystemMetadata
{
    public static bool IsFileSystemKey(string key) => key.StartsWith("fs.", StringComparison.Ordinal);

    public static IReadOnlyList<MetadataField> Read(string path)
    {
        var info = new FileInfo(path);
        var a = info.Attributes;
        return
        [
            new(F.FsName, info.Name, false),
            new(F.FsFolder, info.DirectoryName ?? "", false),
            new(F.FsSize, L.F("FileSystemMetadata.01N0Bytes", ReportWriter.FormatSize(info.Length), info.Length), false),
            new(F.FsCreated, MetadataValues.FormatDate(TrimToSeconds(info.CreationTime)), true),
            new(F.FsModified, MetadataValues.FormatDate(TrimToSeconds(info.LastWriteTime)), true),
            new(F.FsAccessed, MetadataValues.FormatDate(TrimToSeconds(info.LastAccessTime)), true),
            new(F.FsReadOnly, a.HasFlag(FileAttributes.ReadOnly) ? "true" : "false", true),
            new(F.FsHidden, a.HasFlag(FileAttributes.Hidden) ? "true" : "false", true),
            new(F.FsArchive, a.HasFlag(FileAttributes.Archive) ? "true" : "false", true),
            new(F.FsSystem, a.HasFlag(FileAttributes.System) ? L.T("Metadata.Yes") : L.T("Label.No"), false),
        ];
    }

    private static DateTime TrimToSeconds(DateTime t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, t.Kind);

    /// <summary>Applies file system changes (times as local time, attributes). Unknown or read-only keys are refused.</summary>
    public static void Apply(string path, IEnumerable<MetadataEdit> edits)
    {
        foreach (var e in edits)
        {
            if (e.Value == null) throw new InvalidOperationException($"{Label(e.Key)} cannot be removed; it can only be changed.");
            switch (e.Key)
            {
                case "fs.created": File.SetCreationTime(path, ParseTime(e)); break;
                case "fs.modified": File.SetLastWriteTime(path, ParseTime(e)); break;
                case "fs.accessed": File.SetLastAccessTime(path, ParseTime(e)); break;
                case "fs.readOnly": SetAttribute(path, FileAttributes.ReadOnly, e.Value == "true"); break;
                case "fs.hidden": SetAttribute(path, FileAttributes.Hidden, e.Value == "true"); break;
                case "fs.archive": SetAttribute(path, FileAttributes.Archive, e.Value == "true"); break;
                default: throw new InvalidOperationException($"{Label(e.Key)} cannot be changed.");
            }
        }
    }

    private static string Label(string key) => F.All.TryGetValue(key, out var d) ? d.Label : key;

    private static DateTime ParseTime(MetadataEdit e) =>
        MetadataValues.TryParseDate(e.Value, out var t) ? t : throw new FormatException($"{Label(e.Key)}: \"{e.Value}\" is not a date.");

    private static void SetAttribute(string path, FileAttributes flag, bool on)
    {
        var a = File.GetAttributes(path);
        var updated = on ? a | flag : a & ~flag;
        if (updated != a) File.SetAttributes(path, updated == 0 ? FileAttributes.Normal : updated);
    }
}
