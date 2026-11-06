using System.Text;
using System.Text.RegularExpressions;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Metadata;

/// <summary>
/// Attribution in GIF files (static and animated), without touching any frame:
/// <list type="bullet">
/// <item>A comment extension with exactly the attribution sentence (what TibiaWiki and viewers show).</item>
/// <item>Application extension "XMP DataXMP": the XMP packet followed by the 258-byte magic
/// trailer of the XMP specification, so GIF readers that do not know XMP skip it safely.</item>
/// </list>
/// The blocks are placed after the global color table and any leading application extension
/// (the NETSCAPE loop block stays first). Existing comment and XMP blocks are replaced.
/// </summary>
public static partial class GifMetadata
{
    private static readonly byte[] XmpAppId = "XMP DataXMP"u8.ToArray();

    public static bool IsGif(ReadOnlySpan<byte> data) =>
        data.Length >= 13 && (data[..6].SequenceEqual("GIF89a"u8) || data[..6].SequenceEqual("GIF87a"u8));

    /// <summary>Returns a copy of <paramref name="gif"/> with the attribution embedded and verified.</summary>
    public static byte[] Embed(byte[] gif, AttributionInfo info)
    {
        bool verified = VerifiedBlocks.TryGetValue(info, out byte[]? own);
        if (!verified)
        {
            Attribution.Validate(info);
            using var ms = new MemoryStream();
            WriteOwnBlocks(ms, info);
            own = ms.ToArray();
        }
        var layout = Parse(gif, strict: true);
        using var output = new MemoryStream(gif.Length + own!.Length);
        output.Write(gif, 0, layout.PrefixLength);

        var kept = layout.Blocks.Where(b => !(b.Kind == BlockKind.Comment || b.Kind == BlockKind.Xmp)).ToList();
        int insertAt = kept.TakeWhile(b => b.Kind == BlockKind.Application).Count();
        for (int i = 0; i < kept.Count; i++)
        {
            if (i == insertAt) output.Write(own);
            output.Write(gif, kept[i].Start, kept[i].Length);
        }
        if (insertAt >= kept.Count) output.Write(own);
        output.WriteByte(0x3B);

        byte[] result = output.ToArray();
        // Extensions need GIF89a; a GIF87a file is upgraded (the frames are identical in both versions).
        if (result[4] == (byte)'7') result[4] = (byte)'9';
        if (!verified)
        {
            Verify(result, info);
            VerifiedBlocks.AddOrUpdate(info, own);
        }
        return result;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AttributionInfo, byte[]> VerifiedBlocks = new();

    /// <summary>Comment extension texts, in file order.</summary>
    public static IReadOnlyList<string> ReadComments(ReadOnlySpan<byte> gif)
    {
        byte[] data = gif.ToArray();
        return Parse(data, strict: false).Blocks.Where(b => b.Kind == BlockKind.Comment)
            .Select(b => Encoding.UTF8.GetString(SubBlocks(data, b.Start + 2))).ToList();
    }

    /// <summary>The XMP packet of the file, or null.</summary>
    public static string? ReadXmp(ReadOnlySpan<byte> gif)
    {
        byte[] data = gif.ToArray();
        var block = Parse(data, strict: false).Blocks.FirstOrDefault(b => b.Kind == BlockKind.Xmp);
        if (block.Length == 0) return null;
        int start = block.Start + 14; // 0x21 0xFF 0x0B + 11-byte identifier
        int end = block.Start + block.Length;
        for (int i = start; i + 3 < end; i++)
            if (data[i] == 0x01 && data[i + 1] == 0xFF && data[i + 2] == 0xFE && data[i + 3] == 0xFD)
                return Encoding.UTF8.GetString(data, start, i - start);
        return null;
    }

    /// <summary>Attribution in a GIF (XMP preferred, comments as fallback); null when there is none.</summary>
    public static FoundAttribution? Read(ReadOnlySpan<byte> gif)
    {
        if (!IsGif(gif)) return null;
        var xmp = ReadXmp(gif) is { } packet ? XmpPacket.Parse(packet) : null;
        var comments = ReadComments(gif);
        var fields = comments.SelectMany(c => c.Split('\n')).Select(l => l.Trim())
            .Select(l => l.Split(':', 2)).Where(p => p.Length == 2).Select(p => (Key: p[0].Trim(), Value: p[1].Trim())).ToList();
        string? Field(string key) => fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

        string? text = xmp?.Text ?? comments.FirstOrDefault(c => c.StartsWith("Created", StringComparison.OrdinalIgnoreCase)) ?? (comments.Count > 0 ? comments[0] : null);
        IReadOnlyList<string> creators = xmp?.Creators.Count > 0 ? xmp.Creators
            : Field("Creator") is { } c ? c.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : text != null && CreatedBy().Match(text) is { Success: true } m ? [m.Groups[1].Value.Trim()] : [];
        IReadOnlyList<string> history = xmp?.History.Count > 0 ? xmp.History
            : fields.Where(f => string.Equals(f.Key, "History", StringComparison.OrdinalIgnoreCase)).Select(f => f.Value).ToList();
        var found = new FoundAttribution(creators, text?.Trim(), xmp?.Copyright ?? Field("Copyright"), xmp?.Software ?? Field("Application"), history);
        return found.IsEmpty ? null : found;
    }

    /// <summary>"Created by NAME using …" (also the comment of GIFs made by the original 1.x program).</summary>
    [GeneratedRegex(@"^Created by (.+?) using ", RegexOptions.IgnoreCase)]
    private static partial Regex CreatedBy();

    private static void WriteOwnBlocks(Stream output, AttributionInfo info)
    {
        // One comment with exactly the attribution sentence: readers differ in whether they show the
        // first or the last comment block, so a single block is the only reliable choice (and it is
        // the one-line convention TibiaWiki GIFs have always used). Structured fields go into XMP.
        WriteComment(output, info.Text);

        // XMP: identifier block, the raw packet (no sub-block framing), then the magic trailer.
        output.Write([0x21, 0xFF, 0x0B]);
        output.Write(XmpAppId);
        output.Write(Encoding.UTF8.GetBytes(XmpPacket.Build(info)));
        output.WriteByte(0x01);
        for (int v = 0xFF; v >= 0; v--) output.WriteByte((byte)v);
        output.WriteByte(0x00);
    }

    private static void WriteComment(Stream output, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        output.Write([0x21, 0xFE]);
        for (int i = 0; i < bytes.Length; i += 255)
        {
            int n = Math.Min(255, bytes.Length - i);
            output.WriteByte((byte)n);
            output.Write(bytes, i, n);
        }
        output.WriteByte(0x00);
    }

    private static void Verify(byte[] gif, AttributionInfo info)
    {
        var found = Read(gif);
        if (found == null || found.Text != info.Text || !found.Creators.SequenceEqual(info.Creators))
            throw new AttributionException("The attribution could not be verified in the encoded GIF.");
    }

    // ------------------------------------------------------------------ structure

    private enum BlockKind { Image, GraphicControl, Comment, Application, Xmp, PlainText, OtherExtension }

    private readonly record struct Block(BlockKind Kind, int Start, int Length);

    private sealed record Layout(int PrefixLength, List<Block> Blocks);

    private static Layout Parse(byte[] gif, bool strict)
    {
        if (!IsGif(gif))
        {
            if (strict) throw new InvalidDataException("The data is not a GIF image.");
            return new Layout(0, []);
        }
        int pos = 13;
        byte packed = gif[10];
        if ((packed & 0x80) != 0) pos += 3 * (1 << ((packed & 0x07) + 1));
        int prefix = pos;
        var blocks = new List<Block>();
        try
        {
            while (pos < gif.Length)
            {
                byte b = gif[pos];
                if (b == 0x3B) return new Layout(prefix, blocks);
                int start = pos;
                if (b == 0x21)
                {
                    byte label = gif[pos + 1];
                    var kind = label switch
                    {
                        0xF9 => BlockKind.GraphicControl,
                        0xFE => BlockKind.Comment,
                        0x01 => BlockKind.PlainText,
                        0xFF => gif[pos + 2] == 11 && gif.AsSpan(pos + 3, 11).SequenceEqual(XmpAppId) ? BlockKind.Xmp : BlockKind.Application,
                        _ => BlockKind.OtherExtension,
                    };
                    pos = SkipSubBlocks(gif, pos + 2);
                    blocks.Add(new Block(kind, start, pos - start));
                }
                else if (b == 0x2C)
                {
                    byte imagePacked = gif[pos + 9];
                    pos += 10;
                    if ((imagePacked & 0x80) != 0) pos += 3 * (1 << ((imagePacked & 0x07) + 1));
                    pos = SkipSubBlocks(gif, pos + 1); // LZW minimum code size, then image data
                    blocks.Add(new Block(BlockKind.Image, start, pos - start));
                }
                else
                {
                    throw new InvalidDataException($"Unexpected byte 0x{b:X2} in the GIF block stream.");
                }
            }
        }
        catch (IndexOutOfRangeException)
        {
            if (strict) throw new InvalidDataException("The GIF is truncated or damaged.");
            return new Layout(prefix, blocks);
        }
        catch (InvalidDataException) when (!strict)
        {
            return new Layout(prefix, blocks);
        }
        if (strict) throw new InvalidDataException("The GIF has no trailer (truncated).");
        return new Layout(prefix, blocks);
    }

    private static int SkipSubBlocks(byte[] gif, int pos)
    {
        while (true)
        {
            int size = gif[pos];
            pos += 1 + size;
            if (size == 0) return pos;
            if (pos > gif.Length) throw new IndexOutOfRangeException();
        }
    }

    private static byte[] SubBlocks(byte[] gif, int pos)
    {
        using var ms = new MemoryStream();
        while (pos < gif.Length)
        {
            int size = gif[pos];
            if (size == 0 || pos + 1 + size > gif.Length) break;
            ms.Write(gif, pos + 1, size);
            pos += 1 + size;
        }
        return ms.ToArray();
    }
}
