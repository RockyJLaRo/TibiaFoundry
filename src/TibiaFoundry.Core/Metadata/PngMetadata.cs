using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Metadata;

/// <summary>One text chunk of a PNG file.</summary>
public readonly record struct PngText(string Keyword, string Text, string ChunkType);

/// <summary>
/// Attribution in PNG files, using the standard text chunks only (no pixel data is touched):
/// <list type="bullet">
/// <item><c>Author</c>, <c>Description</c>, <c>Comment</c>, <c>Copyright</c>, <c>Software</c> and
/// <c>Creation Time</c> — registered PNG keywords shown by most viewers and editors. Written as
/// <c>tEXt</c> when the value is Latin-1, otherwise as UTF-8 <c>iTXt</c>.</item>
/// <item><c>TibiaFoundry History</c> — provenance entries, one per line.</item>
/// <item><c>XML:com.adobe.xmp</c> — an XMP packet (<c>iTXt</c>, as the XMP specification requires)
/// with the same information as structured fields.</item>
/// </list>
/// The chunks go right after <c>IHDR</c>, so readers find them before the image data. Chunks
/// with these keywords that were already in the file are replaced, never duplicated.
/// </summary>
public static class PngMetadata
{
    public const string XmpKeyword = "XML:com.adobe.xmp";
    public const string HistoryKeyword = AppIdentity.Id + " History";
    /// <summary>History keyword written by version 3.0: still read, and replaced on re-export.</summary>
    public const string LegacyHistoryKeyword = AppIdentity.Legacy.Version3Id + " History";

    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly HashSet<string> OwnKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Author", "Description", "Comment", "Copyright", "Software", "Creation Time", HistoryKeyword, LegacyHistoryKeyword, XmpKeyword, // loc: identity (written into files and read back)
    };
    private const int MaxTextBytes = 1 << 20;

    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    /// <summary>
    /// Returns a copy of <paramref name="png"/> with the attribution embedded, after checking that it
    /// can be read back. Throws <see cref="InvalidDataException"/> for data that is not a PNG and
    /// <see cref="AttributionException"/> when the attribution cannot be embedded.
    /// </summary>
    public static byte[] Embed(byte[] png, AttributionInfo info)
    {
        // A batch passes the same AttributionInfo for every file: its chunks are built and fully
        // verified once, then reused (the inserted bytes are identical for every file).
        bool verified = VerifiedChunks.TryGetValue(info, out byte[]? own);
        if (!verified)
        {
            Attribution.Validate(info);
            using var ms = new MemoryStream();
            foreach (var (keyword, text) in TextFields(info)) WriteText(ms, keyword, text);
            WriteChunk(ms, "iTXt", ITxtData(XmpKeyword, XmpPacket.Build(info)));
            own = ms.ToArray();
        }
        var chunks = ParseChunks(png, strict: true);
        if (chunks.Count == 0 || chunks[0].Type != "IHDR") throw new InvalidDataException("The PNG has no IHDR chunk.");

        using var output = new MemoryStream(png.Length + own!.Length);
        output.Write(Signature);
        WriteRaw(output, png, chunks[0]);
        output.Write(own);
        foreach (var chunk in chunks.Skip(1))
        {
            if (chunk.Type is "tEXt" or "zTXt" or "iTXt" && OwnKeywords.Contains(KeywordOf(png, chunk))) continue;
            WriteRaw(output, png, chunk);
        }
        byte[] result = output.ToArray();
        if (!verified)
        {
            Verify(result, info);
            VerifiedChunks.AddOrUpdate(info, own);
        }
        return result;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AttributionInfo, byte[]> VerifiedChunks = new();

    /// <summary>All text chunks with a valid CRC (damaged or unreadable chunks are skipped).</summary>
    public static IReadOnlyList<PngText> ReadText(ReadOnlySpan<byte> png)
    {
        var result = new List<PngText>();
        byte[] data = png.ToArray();
        foreach (var chunk in ParseChunks(data, strict: false))
        {
            if (chunk.Type is not ("tEXt" or "zTXt" or "iTXt") || !chunk.CrcValid) continue;
            if (TryDecodeText(data.AsSpan(chunk.DataOffset, chunk.Length), chunk.Type, out var text)) result.Add(text);
        }
        return result;
    }

    /// <summary>Attribution in a PNG (XMP preferred, text chunks as fallback); null when there is none.</summary>
    public static FoundAttribution? Read(ReadOnlySpan<byte> png)
    {
        if (!IsPng(png)) return null;
        var texts = ReadText(png);
        string? Get(string keyword) => texts.FirstOrDefault(t => string.Equals(t.Keyword, keyword, StringComparison.OrdinalIgnoreCase)).Text;
        var xmp = Get(XmpKeyword) is { } packet ? XmpPacket.Parse(packet) : null;
        var creators = xmp?.Creators.Count > 0 ? xmp.Creators
            : Get("Author") is { } author ? author.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
        var history = xmp?.History.Count > 0 ? xmp.History
            : (Get(HistoryKeyword) ?? Get(LegacyHistoryKeyword)) is { } h ? h.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
        var found = new FoundAttribution(creators, xmp?.Text ?? Get("Description") ?? Get("Comment"), xmp?.Copyright ?? Get("Copyright"),
            xmp?.Software ?? Get("Software"), history);
        return found.IsEmpty ? null : found;
    }

    internal static IEnumerable<(string Keyword, string Text)> TextFields(AttributionInfo info)
    {
        yield return ("Author", info.CreatorText);
        yield return ("Description", info.Text);
        yield return ("Comment", info.Text);
        if (!string.IsNullOrWhiteSpace(info.Copyright)) yield return ("Copyright", info.Copyright);
        yield return ("Software", info.Software);
        // RFC 1123, as the PNG specification recommends for "Creation Time".
        yield return ("Creation Time", info.Date.UtcDateTime.ToString("R", CultureInfo.InvariantCulture)); // loc: identity (written into files and read back)
        if (info.History.Count > 0) yield return (HistoryKeyword, string.Join("\n", info.History));
    }

    private static void Verify(byte[] png, AttributionInfo info)
    {
        var found = Read(png);
        if (found == null || found.Text != info.Text || !found.Creators.SequenceEqual(info.Creators))
            throw new AttributionException("The attribution could not be verified in the encoded PNG.");
    }

    // ------------------------------------------------------------------ chunk structure

    private readonly record struct Chunk(string Type, int Offset, int Length, bool CrcValid)
    {
        public int DataOffset => Offset + 8;
        public int TotalLength => Length + 12;
    }

    private static List<Chunk> ParseChunks(byte[] png, bool strict)
    {
        if (!IsPng(png))
        {
            if (strict) throw new InvalidDataException("The data is not a PNG image.");
            return [];
        }
        var chunks = new List<Chunk>();
        int pos = 8;
        while (pos + 12 <= png.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos));
            if (length > int.MaxValue || pos + 12 + (long)length > png.Length)
            {
                if (strict) throw new InvalidDataException("The PNG is truncated or damaged.");
                break;
            }
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + (int)length));
            bool valid = Crc32.Compute(png.AsSpan(pos + 4, 4 + (int)length)) == crc;
            chunks.Add(new Chunk(type, pos, (int)length, valid));
            pos += 12 + (int)length;
            if (type == "IEND") return chunks;
        }
        if (strict) throw new InvalidDataException("The PNG has no IEND chunk.");
        return chunks;
    }

    private static string KeywordOf(byte[] png, Chunk chunk)
    {
        var data = png.AsSpan(chunk.DataOffset, chunk.Length);
        int nul = data.IndexOf((byte)0);
        return nul <= 0 ? "" : Encoding.Latin1.GetString(data[..nul]);
    }

    private static void WriteRaw(Stream output, byte[] png, Chunk chunk) => output.Write(png, chunk.Offset, chunk.TotalLength);

    private static void WriteText(Stream output, string keyword, string text)
    {
        // tEXt only for plain ASCII. The PNG specification defines tEXt as Latin-1, but common readers
        // (ImageMagick among them) decode it as UTF-8 and garble characters such as "©"; UTF-8 iTXt is
        // read correctly everywhere.
        bool ascii = text.All(c => c == '\n' || (c >= ' ' && c <= '~'));
        if (ascii)
        {
            byte[] k = Encoding.Latin1.GetBytes(keyword), t = Encoding.Latin1.GetBytes(text);
            var data = new byte[k.Length + 1 + t.Length];
            k.CopyTo(data, 0);
            t.CopyTo(data, k.Length + 1);
            WriteChunk(output, "tEXt", data);
        }
        else
        {
            WriteChunk(output, "iTXt", ITxtData(keyword, text));
        }
    }

    /// <summary>Uncompressed iTXt: keyword, NUL, flag 0, method 0, empty language, NUL, empty translation, NUL, UTF-8 text.</summary>
    private static byte[] ITxtData(string keyword, string text)
    {
        byte[] k = Encoding.Latin1.GetBytes(keyword), t = Encoding.UTF8.GetBytes(text);
        var data = new byte[k.Length + 5 + t.Length];
        k.CopyTo(data, 0);
        // data[k.Length] = 0 (separator), flag 0, method 0, language "" + NUL, translated keyword "" + NUL
        t.CopyTo(data, k.Length + 5);
        return data;
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
        Encoding.ASCII.GetBytes(type, header[4..]);
        output.Write(header);
        output.Write(data);
        uint crc = Crc32.Append(Crc32.Compute(header[4..]), data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static bool TryDecodeText(ReadOnlySpan<byte> data, string type, out PngText text)
    {
        text = default;
        int nul = data.IndexOf((byte)0);
        if (nul <= 0 || nul > 79) return false;
        string keyword = Encoding.Latin1.GetString(data[..nul]);
        var rest = data[(nul + 1)..];
        try
        {
            switch (type)
            {
                case "tEXt":
                    text = new PngText(keyword, Encoding.Latin1.GetString(rest), type);
                    return true;
                case "zTXt":
                    if (rest.Length < 1 || rest[0] != 0) return false;
                    text = new PngText(keyword, Encoding.Latin1.GetString(Inflate(rest[1..])), type);
                    return true;
                default: // iTXt
                    if (rest.Length < 2) return false;
                    bool compressed = rest[0] == 1;
                    var r = rest[2..];
                    int langEnd = r.IndexOf((byte)0);
                    if (langEnd < 0) return false;
                    r = r[(langEnd + 1)..];
                    int transEnd = r.IndexOf((byte)0);
                    if (transEnd < 0) return false;
                    r = r[(transEnd + 1)..];
                    byte[] payload = compressed ? Inflate(r) : r.ToArray();
                    text = new PngText(keyword, Encoding.UTF8.GetString(payload), type);
                    return true;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return false; // damaged compressed text: ignore the chunk, never the whole file
        }
    }

    private static byte[] Inflate(ReadOnlySpan<byte> data)
    {
        using var input = new MemoryStream(data.ToArray());
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = z.Read(buffer)) > 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > MaxTextBytes) throw new InvalidDataException("Compressed text chunk is too large.");
        }
        return output.ToArray();
    }
}

/// <summary>CRC-32 (ISO 3309, as used by PNG and zlib).</summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data) => Append(0, data);

    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        uint c = crc ^ 0xFFFFFFFFu;
        foreach (byte b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
