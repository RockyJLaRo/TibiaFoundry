using System.Buffers.Binary;

namespace TibiaFoundry.Core.Rcc;

/// <summary>How a resource's bytes are stored in an .rcc file.</summary>
public enum RccCompression
{
    None,
    /// <summary>zlib, in Qt's qCompress layout (4-byte big-endian original size, then a zlib stream).</summary>
    Zlib,
    /// <summary>Zstandard (Qt 5.13+ with zstd support). Kept unchanged; this version cannot decompress it.</summary>
    Zstd,
}

/// <summary>
/// Facts about the binary Qt resource format (what <c>rcc --binary</c> writes and
/// <c>QResource::registerResource</c> reads). Everything is big-endian.
/// </summary>
/// <remarks>
/// <code>
/// header   "qres", version (1-3), tree offset, data offset, names offset, [version 3: overall flags]
/// data     per file: uint32 length, then the stored bytes
/// names    per name: uint16 length (UTF-16 units), uint32 qt_hash(name), UTF-16BE characters
/// tree     nodes of 14 bytes (version 1) or 22 bytes (version 2+, + uint64 modification time in ms):
///          directory: uint32 name offset, uint16 flags (2), uint32 child count, uint32 first child index
///          file:      uint32 name offset, uint16 flags, uint16 territory, uint16 language, uint32 data offset
/// </code>
/// Node 0 is the root directory. A directory's children are consecutive nodes sorted by the hash
/// of their names, which Qt searches with a binary search; files with equal hashes are compared by
/// name.
/// </remarks>
public static class RccFormat
{
    public static ReadOnlySpan<byte> Magic => "qres"u8;

    public const ushort FlagCompressed = 0x01;
    public const ushort FlagDirectory = 0x02;
    public const ushort FlagCompressedZstd = 0x04;

    public const int MinVersion = 1;
    public const int MaxVersion = 3;

    /// <summary>Size of a tree node for a format version.</summary>
    public static int NodeSize(int version) => version >= 2 ? 22 : 14;

    /// <summary>Size of the file header for a format version.</summary>
    public static int HeaderSize(int version) => version >= 3 ? 24 : 20;

    /// <summary>The territory/language Qt writes for resources without a locale (QLocale::AnyTerritory, QLocale::C).</summary>
    public const ushort DefaultTerritory = 0;
    public const ushort DefaultLanguage = 1;

    /// <summary>Qt's <c>qt_hash</c> over UTF-16 code units (used to sort and find names).</summary>
    public static uint QtHash(string name)
    {
        uint h = 0;
        foreach (char c in name)
        {
            h = (h << 4) + c;
            h ^= (h & 0xf0000000) >> 23;
            h &= 0x0fffffff;
        }
        return h;
    }

    /// <summary>
    /// Normalizes a resource path: '/' separators, one leading '/', no empty, "." or ".." segments.
    /// Returns null for paths that cannot be stored.
    /// </summary>
    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        foreach (var p in parts)
        {
            if (p is "." or "..") return null;
            if (p.Length > ushort.MaxValue || p.Any(char.IsControl)) return null;
        }
        return "/" + string.Join('/', parts);
    }

    /// <summary>Qt's qCompress layout: original size (big-endian) followed by a zlib stream.</summary>
    public static byte[] ZlibCompress(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.Length / 2 + 16);
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)data.Length);
        ms.Write(size);
        using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data);
        return ms.ToArray();
    }

    /// <summary>Inverse of <see cref="ZlibCompress"/>; checks the stored original size.</summary>
    public static byte[] ZlibDecompress(ReadOnlySpan<byte> stored, int maxSize = 512 * 1024 * 1024)
    {
        if (stored.Length < 4) throw new InvalidDataException("A compressed resource is shorter than its 4-byte size header.");
        uint expected = BinaryPrimitives.ReadUInt32BigEndian(stored);
        if (expected > maxSize) throw new InvalidDataException($"A compressed resource declares {expected:N0} bytes, more than the limit of {maxSize:N0}.");
        var result = new byte[expected];
        using var input = new MemoryStream(stored[4..].ToArray());
        using var z = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
        int read = z.ReadAtLeast(result, result.Length, throwOnEndOfStream: false);
        if (read != expected || z.ReadByte() != -1)
            throw new InvalidDataException($"A compressed resource decompressed to a different size than its header says ({read:N0} instead of {expected:N0} bytes).");
        return result;
    }
}
