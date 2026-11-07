namespace TibiaFoundry.Core.Compression;

/// <summary>
/// Reader for CipSoft's LZMA container used by <c>*.bmp.lzma</c> sprite sheets, map tiles
/// (minimap/satellite/subarea) and compressed sounds.
/// </summary>
/// <remarks>
/// Layout (verified against the installed client):
/// <code>
///   00 00 .. 00            zero padding (the header is right-aligned to 32 bytes)
///   70 0A                  marker
///   varint                 decompressed size
///   varint                 compressed size (= file length - 32)
///   5 bytes                LZMA properties (lc/lp/pb, dictionary size)
///   8 bytes                CIP stores the payload length here, NOT the decompressed size
///   ...                    raw LZMA payload
/// </code>
/// The original used two different hard-coded skip patterns for sprites and map files, which
/// only worked because the varints happen to have fixed lengths. This parser reads the fields.
/// When the marker is absent it falls back to the original "scan for 5D 00 00 00 02" approach
/// (used by its sound/LZMA-decode tools).
/// </remarks>
public static class CipLzma
{
    private static ReadOnlySpan<byte> DefaultProperties => [0x5D, 0x00, 0x00, 0x00, 0x02];

    /// <summary>Largest decompressed size accepted from a header (sprite sheets are 576 KB, map tiles smaller).</summary>
    public const int MaxDecompressedSize = 256 * 1024 * 1024;

    /// <summary>Decompresses a file; errors name the file so the user knows which one is damaged.</summary>
    public static byte[] DecompressFile(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        try
        {
            return Decompress(data);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (TryReadHeader(data, out var header))
        {
            // The compressed size counts every byte after its own field (5 property bytes, 8 size
            // bytes, payload); this holds for all sprite, minimap, satellite and subarea files.
            int available = data.Length - (header.PayloadOffset - 13);
            if (header.CompressedSize > available)
                throw new InvalidDataException($"The container declares {header.CompressedSize:N0} compressed bytes but only {available:N0} follow; the file is truncated.");
            var payload = data.Slice(header.PayloadOffset);
            return LzmaDecoder.Decode(header.Properties, payload, header.DecompressedSize, MaxDecompressedSize);
        }
        return DecompressByScanning(data);
    }

    /// <summary>
    /// Builds a container in the client's layout (see the remarks above): the header right-aligned
    /// to 32 bytes, the client's LZMA properties, the payload length in the 8-byte field and an LZMA
    /// stream with an end marker. The result is decoded again and compared with the input before it
    /// is returned, so a container that would not read back is never produced.
    /// </summary>
    public static byte[] Compress(ReadOnlySpan<byte> data, CancellationToken ct = default)
    {
        byte[] payload = LzmaEncoder.Encode(data, ct: ct);
        var head = new List<byte>(16) { 0x70, 0x0A };
        WriteVarint(head, data.Length);
        WriteVarint(head, 13 + payload.Length);
        if (head.Count > 32) throw new InvalidDataException("The container header does not fit into 32 bytes.");
        var result = new byte[32 + 13 + payload.Length];
        head.CopyTo(result, 32 - head.Count);
        LzmaEncoder.TibiaProperties.CopyTo(result.AsSpan(32));
        BitConverter.TryWriteBytes(result.AsSpan(37, 8), (long)payload.Length);
        payload.CopyTo(result, 45);
        if (!Decompress(result).AsSpan().SequenceEqual(data))
            throw new InvalidDataException("The LZMA container did not decode back to the original data.");
        return result;
    }

    private static void WriteVarint(List<byte> output, long value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            output.Add(value != 0 ? (byte)(b | 0x80) : b);
        }
        while (value != 0);
    }

    /// <summary>Parsed CIP container header.</summary>
    public readonly record struct Header(int DecompressedSize, int CompressedSize, byte[] Properties, int PayloadOffset);

    public static bool TryReadHeader(ReadOnlySpan<byte> data, out Header header)
    {
        header = default;
        int i = 0;
        while (i < data.Length && data[i] == 0) i++;
        if (i + 2 > data.Length || data[i] != 0x70 || data[i + 1] != 0x0A) return false;
        i += 2;
        if (!TryReadVarint(data, ref i, out long decompressed) || !TryReadVarint(data, ref i, out long compressed)) return false;
        if (i + 13 > data.Length || decompressed <= 0 || decompressed > int.MaxValue || compressed < 0 || compressed > int.MaxValue) return false;
        var props = data.Slice(i, 5).ToArray();
        header = new Header((int)decompressed, (int)compressed, props, i + 13);
        return true;
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> data, ref int index, out long value)
    {
        value = 0;
        int shift = 0;
        while (index < data.Length && shift < 63)
        {
            byte b = data[index++];
            value |= (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
            shift += 7;
        }
        return false;
    }

    /// <summary>
    /// Fallback used by the original's <c>RydanDecompressFile</c>: locate the standard property
    /// bytes and decode what follows. The 8 bytes after the properties are used as the size when
    /// plausible; otherwise decoding stops at the end marker or when the input runs out.
    /// </summary>
    private static byte[] DecompressByScanning(ReadOnlySpan<byte> data)
    {
        int at = data.IndexOf(DefaultProperties);
        if (at < 0 || at + 13 > data.Length)
            throw new InvalidDataException("The file is not a recognized CipSoft LZMA container.");
        long declared = BitConverter.ToInt64(data.Slice(at + 5, 8));
        var payload = data.Slice(at + 13);
        // CIP writes the payload length into this field, so only trust values clearly larger than it.
        bool plausible = declared > payload.Length + 16 && declared < 512L * 1024 * 1024;
        return LzmaDecoder.Decode(data.Slice(at, 5), payload, plausible ? declared : -1, MaxDecompressedSize);
    }
}
