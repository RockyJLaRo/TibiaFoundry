using System.Buffers.Binary;
using System.Text;

namespace TibiaFoundry.Core.Rcc;

/// <summary>One file stored in an .rcc archive.</summary>
public sealed class RccEntry
{
    internal RccEntry(string path, byte[] stored, RccCompression compression, ushort territory, ushort language, ulong lastModified, int order)
    {
        Path = path;
        Stored = stored;
        Compression = compression;
        Territory = territory;
        Language = language;
        LastModified = lastModified;
        Order = order;
    }

    /// <summary>Resource path, e.g. "/images/skin/button.png" (shown by Qt as ":/images/skin/button.png").</summary>
    public string Path { get; internal set; }

    public string Name => Path[(Path.LastIndexOf('/') + 1)..];
    public string Folder => Path[..Math.Max(1, Path.LastIndexOf('/'))];
    public string Extension => System.IO.Path.GetExtension(Name).ToLowerInvariant();

    /// <summary>Bytes exactly as stored (compressed when <see cref="Compression"/> is not None).</summary>
    public byte[] Stored { get; internal set; }

    public RccCompression Compression { get; internal set; }
    public ushort Territory { get; }
    public ushort Language { get; }

    /// <summary>Modification time in milliseconds since 1970 (format 2 and later; 0 when unknown).</summary>
    public ulong LastModified { get; internal set; }

    /// <summary>True when replaced, added, renamed or moved since the archive was opened.</summary>
    public bool IsModified { get; internal set; }

    /// <summary>Position of the data in the original file (new entries come last); keeps saved files close to the original.</summary>
    internal int Order { get; set; }

    /// <summary>Size of the resource's content in bytes (null for Zstandard, which this version cannot decompress).</summary>
    public long? Size => Compression switch
    {
        RccCompression.None => Stored.Length,
        RccCompression.Zlib => Stored.Length >= 4 ? BinaryPrimitives.ReadUInt32BigEndian(Stored) : 0,
        _ => null,
    };

    public long StoredSize => Stored.Length;

    /// <summary>The resource's content. Throws <see cref="NotSupportedException"/> for Zstandard-compressed resources.</summary>
    public byte[] GetContent() => Compression switch
    {
        RccCompression.None => Stored,
        RccCompression.Zlib => RccFormat.ZlibDecompress(Stored),
        _ => throw new NotSupportedException($"{Path} is compressed with Zstandard, which this version cannot decompress. It is kept unchanged when the file is saved."),
    };

    public bool CanReadContent => Compression != RccCompression.Zstd;

    public DateTime? LastModifiedUtc => LastModified == 0 || LastModified > 253402300799999 ? null : DateTime.UnixEpoch.AddMilliseconds(LastModified);
}

/// <summary>How new or replaced resources are compressed.</summary>
public enum RccCompressionChoice
{
    /// <summary>Replaced resources keep the compression of the resource they replace; new ones follow Qt's rule.</summary>
    Automatic,
    /// <summary>Qt's rcc rule: zlib when it saves at least 70 %, otherwise stored as is.</summary>
    QtDefault,
    Never,
    Always,
}

/// <summary>
/// A Qt binary resource file (.rcc) held in memory: read, edited (replace, add, remove, rename) and
/// written back. Resources that were not changed keep their stored bytes, compression, locale and
/// time stamp exactly.
/// </summary>
public sealed class RccArchive
{
    private readonly List<RccEntry> _entries;

    private RccArchive(int version, List<RccEntry> entries)
    {
        FormatVersion = version;
        _entries = entries;
    }

    /// <summary>Format version of the file (1–3); kept when saving.</summary>
    public int FormatVersion { get; }

    public IReadOnlyList<RccEntry> Entries => _entries;

    public bool IsModified { get; private set; }

    public static RccArchive Create(int version = 3)
    {
        if (version is < RccFormat.MinVersion or > RccFormat.MaxVersion) throw new ArgumentOutOfRangeException(nameof(version));
        return new RccArchive(version, []);
    }

    public static RccArchive Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>
    /// Reads an archive. Damaged files raise <see cref="InvalidDataException"/> with the reason;
    /// <see cref="RccValidator"/> gives a complete list of problems.
    /// </summary>
    public static RccArchive Parse(byte[] data)
    {
        var header = ReadHeader(data);
        var entries = new List<RccEntry>();
        int nodeSize = RccFormat.NodeSize(header.Version);
        int nodeCount = (data.Length - header.TreeOffset) / nodeSize;
        if (nodeCount < 1) throw new InvalidDataException("The resource tree is empty or truncated.");
        var visited = new bool[nodeCount];
        var pending = new Stack<(int Node, string Path)>();
        pending.Push((0, ""));
        while (pending.Count > 0)
        {
            var (node, parentPath) = pending.Pop();
            if (node < 0 || node >= nodeCount) throw new InvalidDataException($"The resource tree refers to node {node}, but it has only {nodeCount}.");
            if (visited[node]) throw new InvalidDataException($"The resource tree is damaged: node {node} is reached twice.");
            visited[node] = true;
            int o = header.TreeOffset + node * nodeSize;
            uint nameOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(o));
            ushort flags = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o + 4));
            string path = node == 0 ? "" : parentPath + "/" + ReadName(data, header.NamesOffset, nameOffset);
            if ((flags & RccFormat.FlagDirectory) != 0)
            {
                uint count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(o + 6));
                uint first = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(o + 10));
                if (count > nodeCount || first + count > nodeCount)
                    throw new InvalidDataException($"Folder \"{(path.Length == 0 ? "/" : path)}\" lists children outside the tree.");
                for (int c = (int)(first + count) - 1; c >= (int)first; c--) pending.Push((c, path));
            }
            else
            {
                ushort territory = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o + 6));
                ushort language = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o + 8));
                uint dataOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(o + 10));
                ulong modified = header.Version >= 2 ? BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(o + 14)) : 0;
                long at = (long)header.DataOffset + dataOffset;
                if (at + 4 > data.Length) throw new InvalidDataException($"{path}: its data lies outside the file.");
                uint length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)at));
                if (at + 4 + length > data.Length) throw new InvalidDataException($"{path}: its data ({length:N0} bytes) runs past the end of the file.");
                var compression = (flags & RccFormat.FlagCompressedZstd) != 0 ? RccCompression.Zstd
                    : (flags & RccFormat.FlagCompressed) != 0 ? RccCompression.Zlib : RccCompression.None;
                entries.Add(new RccEntry(path, data.AsSpan((int)at + 4, (int)length).ToArray(), compression, territory, language, modified, (int)dataOffset));
            }
        }
        // The original data order (by offset) is the save order, so an unchanged file keeps its layout.
        entries.Sort((a, b) => a.Order.CompareTo(b.Order));
        for (int i = 0; i < entries.Count; i++) entries[i].Order = i;
        return new RccArchive(header.Version, entries);
    }

    internal readonly record struct Header(int Version, int TreeOffset, int DataOffset, int NamesOffset, uint Flags);

    internal static Header ReadHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 20 || !data[..4].SequenceEqual(RccFormat.Magic))
            throw new InvalidDataException("This is not a Qt resource file (it does not start with \"qres\").");
        int version = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
        if (version is < RccFormat.MinVersion or > RccFormat.MaxVersion)
            throw new InvalidDataException($"Qt resource format version {version} is not supported (versions 1 to 3 are).");
        int tree = BinaryPrimitives.ReadInt32BigEndian(data[8..]);
        int dataOffset = BinaryPrimitives.ReadInt32BigEndian(data[12..]);
        int names = BinaryPrimitives.ReadInt32BigEndian(data[16..]);
        uint flags = version >= 3 && data.Length >= 24 ? BinaryPrimitives.ReadUInt32BigEndian(data[20..]) : 0;
        int headerSize = RccFormat.HeaderSize(version);
        foreach (var (name, value) in new[] { ("tree", tree), ("data", dataOffset), ("names", names) })
            if (value < headerSize || value >= data.Length)
                throw new InvalidDataException($"The {name} offset ({value:N0}) lies outside the file ({data.Length:N0} bytes).");
        if ((flags & ~(uint)(RccFormat.FlagCompressed | RccFormat.FlagCompressedZstd)) != 0)
            throw new InvalidDataException($"The file uses features this version does not know (flags 0x{flags:X}).");
        return new Header(version, tree, dataOffset, names, flags);
    }

    internal static string ReadName(ReadOnlySpan<byte> data, int namesOffset, uint nameOffset)
    {
        long at = (long)namesOffset + nameOffset;
        if (at + 6 > data.Length) throw new InvalidDataException($"A resource name lies outside the file (offset {nameOffset:N0}).");
        int length = BinaryPrimitives.ReadUInt16BigEndian(data[(int)at..]);
        if (at + 6 + length * 2L > data.Length) throw new InvalidDataException($"A resource name runs past the end of the file (offset {nameOffset:N0}).");
        return Encoding.BigEndianUnicode.GetString(data.Slice((int)at + 6, length * 2));
    }

    public RccEntry? Find(string path)
    {
        string? normalized = RccFormat.NormalizePath(path);
        return normalized == null ? null : _entries.FirstOrDefault(e => string.Equals(e.Path, normalized, StringComparison.Ordinal));
    }

    /// <summary>Replaces a resource's content.</summary>
    public void Replace(RccEntry entry, byte[] content, RccCompressionChoice compression = RccCompressionChoice.Automatic)
    {
        if (!_entries.Contains(entry)) throw new ArgumentException("The resource is not part of this archive.", nameof(entry));
        var mode = compression == RccCompressionChoice.Automatic
            ? entry.Compression == RccCompression.None ? RccCompressionChoice.Never : RccCompressionChoice.Always
            : compression;
        (entry.Stored, entry.Compression) = Store(content, mode);
        entry.IsModified = true;
        IsModified = true;
    }

    /// <summary>Adds a new resource. Throws when the path is invalid or already used.</summary>
    public RccEntry Add(string path, byte[] content, RccCompressionChoice compression = RccCompressionChoice.QtDefault, ulong lastModified = 0)
    {
        string normalized = RccFormat.NormalizePath(path) ?? throw new ArgumentException($"\"{path}\" is not a valid resource path.", nameof(path));
        CheckFree(normalized);
        var (stored, kind) = Store(content, compression == RccCompressionChoice.Automatic ? RccCompressionChoice.QtDefault : compression);
        int order = _entries.Count == 0 ? 0 : _entries.Max(e => e.Order) + 1;
        var entry = new RccEntry(normalized, stored, kind, RccFormat.DefaultTerritory, RccFormat.DefaultLanguage, lastModified, order) { IsModified = true };
        _entries.Add(entry);
        IsModified = true;
        return entry;
    }

    public void Remove(RccEntry entry)
    {
        if (_entries.Remove(entry)) IsModified = true;
    }

    /// <summary>Gives a resource a new path (rename or move). Throws when the path is invalid or used.</summary>
    public void Rename(RccEntry entry, string newPath)
    {
        string normalized = RccFormat.NormalizePath(newPath) ?? throw new ArgumentException($"\"{newPath}\" is not a valid resource path.", nameof(newPath));
        if (normalized == entry.Path) return;
        CheckFree(normalized);
        entry.Path = normalized;
        entry.IsModified = true;
        IsModified = true;
    }

    private void CheckFree(string path)
    {
        foreach (var e in _entries)
        {
            if (string.Equals(e.Path, path, StringComparison.Ordinal)) throw new ArgumentException($"{path} already exists in this file.");
            // A file and a folder cannot have the same name.
            if (e.Path.StartsWith(path + "/", StringComparison.Ordinal) || path.StartsWith(e.Path + "/", StringComparison.Ordinal))
                throw new ArgumentException($"{path} would clash with {e.Path} (a file and a folder cannot share a name).");
        }
    }

    private static (byte[] Stored, RccCompression Kind) Store(byte[] content, RccCompressionChoice mode)
    {
        if (mode == RccCompressionChoice.Never || content.Length == 0) return (content, RccCompression.None);
        byte[] compressed = RccFormat.ZlibCompress(content);
        if (mode == RccCompressionChoice.Always) return (compressed, RccCompression.Zlib);
        // Qt's default threshold: compress only when it saves at least 70 %.
        int saving = (int)(100.0 * (content.Length - compressed.Length) / content.Length);
        return saving >= 70 ? (compressed, RccCompression.Zlib) : (content, RccCompression.None);
    }

    /// <summary>Marks the archive as saved (after a successful write).</summary>
    public void MarkSaved()
    {
        IsModified = false;
        foreach (var e in _entries) e.IsModified = false;
    }

    /// <summary>
    /// Serializes the archive in its format version. Data is written in the original order (new
    /// resources last); names are shared when equal; children are sorted by name hash as Qt
    /// requires.
    /// </summary>
    public byte[] Build()
    {
        if (_entries.Count == 0) throw new InvalidOperationException("A resource file needs at least one resource.");
        var root = new DirNode("");
        foreach (var e in _entries)
        {
            var parts = e.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var dir = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (dir.Files.ContainsKey(parts[i])) throw new InvalidDataException($"{e.Path}: \"{parts[i]}\" is both a file and a folder.");
                if (!dir.Dirs.TryGetValue(parts[i], out var next)) dir.Dirs[parts[i]] = next = new DirNode(parts[i]);
                dir = next;
            }
            if (dir.Dirs.ContainsKey(parts[^1]) || !dir.Files.TryAdd(parts[^1], e))
                throw new InvalidDataException($"{e.Path} is listed twice or clashes with a folder.");
        }

        int version = FormatVersion;
        using var ms = new MemoryStream();
        var buf = new byte[8];
        ms.Write(RccFormat.Magic);
        ms.Write(new byte[RccFormat.HeaderSize(version) - 4]);

        // Data, in the original order.
        int dataStart = (int)ms.Position;
        var dataOffsets = new Dictionary<RccEntry, uint>();
        foreach (var e in _entries.OrderBy(e => e.Order))
        {
            dataOffsets[e] = (uint)(ms.Position - dataStart);
            BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)e.Stored.Length);
            ms.Write(buf, 0, 4);
            ms.Write(e.Stored);
        }

        // Names (each distinct name once).
        int namesStart = (int)ms.Position;
        var nameOffsets = new Dictionary<string, uint>(StringComparer.Ordinal);
        void WriteName(string name)
        {
            if (nameOffsets.ContainsKey(name)) return;
            nameOffsets[name] = (uint)(ms.Position - namesStart);
            BinaryPrimitives.WriteUInt16BigEndian(buf, (ushort)name.Length);
            ms.Write(buf, 0, 2);
            BinaryPrimitives.WriteUInt32BigEndian(buf, RccFormat.QtHash(name));
            ms.Write(buf, 0, 4);
            ms.Write(Encoding.BigEndianUnicode.GetBytes(name));
        }

        // Tree: breadth-first, each directory's children consecutive and sorted by hash.
        var order = new List<(Node Node, string Name)> { (root, "") };
        var firstChild = new Dictionary<DirNode, int>();
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i].Node is not DirNode dir) continue;
            firstChild[dir] = order.Count;
            foreach (var child in dir.Children()) order.Add(child);
        }
        foreach (var (_, name) in order.Skip(1)) WriteName(name);

        int treeStart = (int)ms.Position;
        int nodeSize = RccFormat.NodeSize(version);
        var node = new byte[nodeSize];
        bool anyZlib = false, anyZstd = false;
        foreach (var (n, name) in order)
        {
            Array.Clear(node);
            uint nameOffset = n == root ? 0 : nameOffsets[name];
            BinaryPrimitives.WriteUInt32BigEndian(node, nameOffset);
            if (n is DirNode dir)
            {
                BinaryPrimitives.WriteUInt16BigEndian(node.AsSpan(4), RccFormat.FlagDirectory);
                BinaryPrimitives.WriteUInt32BigEndian(node.AsSpan(6), (uint)(dir.Dirs.Count + dir.Files.Count));
                BinaryPrimitives.WriteUInt32BigEndian(node.AsSpan(10), (uint)firstChild[dir]);
            }
            else
            {
                var e = ((FileNode)n).Entry;
                ushort flags = e.Compression switch { RccCompression.Zlib => RccFormat.FlagCompressed, RccCompression.Zstd => RccFormat.FlagCompressedZstd, _ => 0 };
                anyZlib |= e.Compression == RccCompression.Zlib;
                anyZstd |= e.Compression == RccCompression.Zstd;
                BinaryPrimitives.WriteUInt16BigEndian(node.AsSpan(4), flags);
                BinaryPrimitives.WriteUInt16BigEndian(node.AsSpan(6), e.Territory);
                BinaryPrimitives.WriteUInt16BigEndian(node.AsSpan(8), e.Language);
                BinaryPrimitives.WriteUInt32BigEndian(node.AsSpan(10), dataOffsets[e]);
                if (version >= 2) BinaryPrimitives.WriteUInt64BigEndian(node.AsSpan(14), e.LastModified);
            }
            ms.Write(node);
        }

        byte[] result = ms.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4), version);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(8), treeStart);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(12), dataStart);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(16), namesStart);
        if (version >= 3)
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(20), (anyZlib ? RccFormat.FlagCompressed : 0u) | (anyZstd ? RccFormat.FlagCompressedZstd : 0u));
        return result;
    }

    private abstract class Node;

    private sealed class FileNode(RccEntry entry) : Node
    {
        public RccEntry Entry { get; } = entry;
    }

    private sealed class DirNode(string name) : Node
    {
        public string Name { get; } = name;
        public SortedDictionary<string, DirNode> Dirs { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, RccEntry> Files { get; } = new(StringComparer.Ordinal);

        /// <summary>Children sorted by qt_hash (ties by name, for a stable result).</summary>
        public IEnumerable<(Node, string)> Children() =>
            Dirs.Select(d => ((Node)d.Value, d.Key))
                .Concat(Files.Select(f => ((Node)new FileNode(f.Value), f.Key)))
                .OrderBy(c => RccFormat.QtHash(c.Item2))
                .ThenBy(c => c.Item2, StringComparer.Ordinal);
    }
}
