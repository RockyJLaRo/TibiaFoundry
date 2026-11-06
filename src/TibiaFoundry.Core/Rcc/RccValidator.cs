using TibiaFoundry.Core.Common;
using System.Buffers.Binary;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Rcc;

/// <summary>Result of checking an .rcc file's structure.</summary>
public sealed class RccValidationReport
{
    public int FormatVersion { get; internal set; }
    public int Files { get; internal set; }
    public int Folders { get; internal set; }
    public int CompressedZlib { get; internal set; }
    public int CompressedZstd { get; internal set; }

    /// <summary>Files that Qt's own lookup algorithm (binary search by name hash) finds at the right node.</summary>
    public int FoundByQtLookup { get; internal set; }

    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool IsValid => Errors.Count == 0;

    public string Summary => IsValid
        ? L.F("RccValidator.ValidQtResourceFileFormat0", FormatVersion, Files, Folders, CompressedZlib, (CompressedZstd > 0 ? L.F("RccValidator.0N0ZstandardCompressedNotDecompressed", CompressedZstd) : ""))
        : L.F("RccValidator.0Found1", L.Count(Errors.Count, "Noun.problem"), Errors[0]);
}

/// <summary>
/// Structural check of an .rcc file, independent of <see cref="RccArchive"/>: header and offsets,
/// every tree node reached exactly once, children sorted by hash, stored name hashes equal to
/// qt_hash, data inside the file, zlib data that decompresses to its declared size, and finally
/// every file looked up with the same binary search Qt uses at run time. Run on every file the RCC
/// Editor writes, before it replaces anything.
/// </summary>
public static class RccValidator
{
    public static RccValidationReport Validate(byte[] data, int maxErrors = 50)
    {
        var report = new RccValidationReport();
        RccArchive.Header header;
        try
        {
            header = RccArchive.ReadHeader(data);
        }
        catch (InvalidDataException ex)
        {
            report.Errors.Add(ex.Message);
            return report;
        }
        report.FormatVersion = header.Version;
        int nodeSize = RccFormat.NodeSize(header.Version);
        int nodeCount = (data.Length - header.TreeOffset) / nodeSize;
        if (nodeCount < 1)
        {
            report.Errors.Add(L.T("RccValidator.TheResourceTreeIsEmptyOr"));
            return report;
        }
        if (header.TreeOffset + nodeCount * nodeSize != data.Length)
            report.Warnings.Add(L.T("RccValidator.ThereAreBytesAfterTheResource"));

        var visited = new bool[nodeCount];
        var files = new List<(int Node, string Path)>();
        var pending = new Stack<(int Node, string Path)>();
        pending.Push((0, ""));
        bool flagZlib = false, flagZstd = false;
        void Error(string message)
        {
            if (report.Errors.Count < maxErrors) report.Errors.Add(message);
        }

        uint StoredHash(int node)
        {
            uint nameOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(header.TreeOffset + node * nodeSize));
            long at = (long)header.NamesOffset + nameOffset + 2;
            return at + 4 <= data.Length ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)at)) : 0;
        }

        while (pending.Count > 0)
        {
            var (node, parentPath) = pending.Pop();
            if (node < 0 || node >= nodeCount) { Error(L.F("RccValidator.AFolderRefersToNode0", node)); continue; }
            if (visited[node]) { Error(L.F("RccValidator.Node0IsReachedMoreThan", node)); continue; }
            visited[node] = true;
            int o = header.TreeOffset + node * nodeSize;
            uint nameOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(o));
            ushort flags = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o + 4));
            string path = parentPath;
            if (node != 0)
            {
                string name;
                try
                {
                    name = RccArchive.ReadName(data, header.NamesOffset, nameOffset);
                }
                catch (InvalidDataException ex)
                {
                    Error(L.F("RccValidator.Node01", node, ex.Message));
                    continue;
                }
                path = parentPath + "/" + name;
                if (StoredHash(node) != RccFormat.QtHash(name)) Error(L.F("RccValidator.0TheStoredNameHashIs", path));
                if (name.Length == 0 || name.Contains('/')) Error(L.F("RccValidator.0InvalidName", path));
            }
            if ((flags & RccFormat.FlagDirectory) != 0)
            {
                report.Folders++;
                uint count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(o + 6));
                uint first = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(o + 10));
                if (first + (long)count > nodeCount) { Error(L.F("RccValidator.Folder0ListsChildrenOutsideThe", (path.Length == 0 ? "/" : path))); continue; }
                uint previous = 0;
                var names = new HashSet<string>(StringComparer.Ordinal);
                for (int c = (int)first; c < first + count; c++)
                {
                    uint h = StoredHash(c);
                    if (h < previous) Error(L.F("RccValidator.Folder0ChildrenAreNotSorted", (path.Length == 0 ? "/" : path)));
                    previous = h;
                    try
                    {
                        string childName = RccArchive.ReadName(data, header.NamesOffset, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(header.TreeOffset + c * nodeSize)));
                        if (!names.Add(childName)) Error(L.F("RccValidator.Folder0Contains1Twice", (path.Length == 0 ? "/" : path), childName));
                    }
                    catch (InvalidDataException) { /* reported when the child is visited */ }
                }
                for (int c = (int)(first + count) - 1; c >= (int)first; c--) pending.Push((c, path));
            }
            else
            {
                report.Files++;
                files.Add((node, path));
                uint dataOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(o + 10));
                long at = (long)header.DataOffset + dataOffset;
                if (at + 4 > data.Length) { Error(L.F("RccValidator.0ItsDataLiesOutsideThe", path)); continue; }
                uint length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)at));
                if (at + 4 + length > data.Length) { Error(L.F("RccValidator.0ItsDataRunsPastThe", path)); continue; }
                if ((flags & RccFormat.FlagCompressedZstd) != 0)
                {
                    report.CompressedZstd++;
                    flagZstd = true;
                    // A Zstandard frame starts with 28 B5 2F FD.
                    if (length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)at + 4)) != 0xFD2FB528)
                        Error(L.F("RccValidator.0MarkedAsZstandardCompressedBut", path));
                }
                else if ((flags & RccFormat.FlagCompressed) != 0)
                {
                    report.CompressedZlib++;
                    flagZlib = true;
                    try
                    {
                        RccFormat.ZlibDecompress(data.AsSpan((int)at + 4, (int)length));
                    }
                    catch (InvalidDataException ex)
                    {
                        Error($"{path}: {ex.Message}");
                    }
                }
            }
        }

        if (header.Version >= 3)
        {
            if (flagZlib && (header.Flags & RccFormat.FlagCompressed) == 0) Error(L.T("RccValidator.ZlibCompressedFilesArePresentBut"));
            if (flagZstd && (header.Flags & RccFormat.FlagCompressedZstd) == 0) Error(L.T("RccValidator.ZstandardCompressedFilesArePresentBut"));
        }

        // Look every file up exactly as QResourceRoot::findNode does.
        foreach (var (node, path) in files)
        {
            int found = QtFindNode(data, header, nodeSize, nodeCount, path);
            if (found == node) report.FoundByQtLookup++;
            else Error(L.F("RccValidator.0QtSLookupDoesNot", path, (found < 0 ? L.T("RccValidator.NotFound") : L.F("RccValidator.FindsNode0Instead", found))));
        }
        return report;
    }

    /// <summary>Port of the lookup in QResourceRoot::findNode (Qt 5/6): binary search by stored hash, then name compare.</summary>
    internal static int QtFindNode(byte[] data, RccArchive.Header header, int nodeSize, int nodeCount, string path)
    {
        int NodeOffset(int n) => header.TreeOffset + n * nodeSize;
        uint Hash(int n)
        {
            uint nameOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(NodeOffset(n)));
            return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(header.NamesOffset + (int)nameOffset + 2));
        }
        string Name(int n) => RccArchive.ReadName(data, header.NamesOffset, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(NodeOffset(n))));

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int childCount = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(NodeOffset(0) + 6));
        int child = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(NodeOffset(0) + 10));
        for (int s = 0; s < segments.Length && childCount > 0; s++)
        {
            string segment = segments[s];
            uint h = RccFormat.QtHash(segment);
            int l = 0, r = childCount - 1;
            int sub = (l + r + 1) / 2;
            while (r != l)
            {
                uint subHash = Hash(child + sub);
                if (h == subHash) break;
                if (h < subHash) r = sub - 1;
                else l = sub;
                sub = (l + r + 1) / 2;
            }
            sub += child;
            bool found = false;
            if (sub < nodeCount && Hash(sub) == h)
            {
                while (sub > child && Hash(sub - 1) == h) --sub;
                for (; sub < child + childCount && Hash(sub) == h; ++sub)
                {
                    if (Name(sub) != segment) continue;
                    found = true;
                    ushort flags = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(NodeOffset(sub) + 4));
                    bool last = s == segments.Length - 1;
                    if (last) return (flags & RccFormat.FlagDirectory) == 0 ? sub : -1;
                    if ((flags & RccFormat.FlagDirectory) == 0) return -1;
                    childCount = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(NodeOffset(sub) + 6));
                    child = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(NodeOffset(sub) + 10));
                    break;
                }
            }
            if (!found) return -1;
        }
        return -1;
    }
}
