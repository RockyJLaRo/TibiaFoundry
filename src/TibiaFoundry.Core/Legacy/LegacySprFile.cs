using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;
using TibiaFoundry.Core.Imaging;

namespace TibiaFoundry.Core.Legacy;

/// <summary>
/// Reads sprites from a legacy Tibia.spr without loading the whole file (they can be several
/// hundred megabytes). Thread-safe: reads use positional I/O.
/// </summary>
/// <remarks>
/// <code>
/// uint32 signature, sprite count (uint16, or uint32 for extended clients),
/// count × uint32 file offsets (sprite IDs start at 1; offset 0 = empty sprite),
/// per sprite: 3 bytes color key (unused), uint16 data size, then runs of
///   uint16 transparent pixels, uint16 colored pixels, colored pixels × (R, G, B[, A])
/// </code>
/// Sprites are 32×32 pixels, filled row by row from the top left.
/// </remarks>
public sealed class LegacySprReader : IDisposable
{
    public const int SpriteSize = 32;
    private readonly SafeFileHandle _handle;
    private readonly int _headerSize;

    public LegacySprReader(string path, LegacyClientProfile profile)
    {
        Path = path;
        Profile = profile;
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Length = RandomAccess.GetLength(_handle);
        Span<byte> head = stackalloc byte[8];
        if (RandomAccess.Read(_handle, head, 0) < (profile.ExtendedSprites ? 8 : 6)) throw new InvalidDataException("The sprite file is too short.");
        Signature = BinaryPrimitives.ReadUInt32LittleEndian(head);
        Count = profile.ExtendedSprites ? BinaryPrimitives.ReadUInt32LittleEndian(head[4..]) : BinaryPrimitives.ReadUInt16LittleEndian(head[4..]);
        _headerSize = profile.ExtendedSprites ? 8 : 6;
        if (_headerSize + (long)Count * 4 > Length)
            throw new InvalidDataException($"The sprite file declares {Count:N0} sprites, but it is too short for their offset table ({(profile.ExtendedSprites ? "32-bit" : "16-bit")} count assumed).");
    }

    public string Path { get; }
    public LegacyClientProfile Profile { get; }
    public uint Signature { get; }
    public uint Count { get; }
    public long Length { get; }

    /// <summary>File offset of a sprite (0 = empty), or throws for IDs outside 1..Count.</summary>
    public uint OffsetOf(uint id)
    {
        if (id == 0 || id > Count) throw new ArgumentOutOfRangeException(nameof(id), $"Sprite {id} does not exist (the file has {Count:N0}).");
        Span<byte> b = stackalloc byte[4];
        RandomAccess.Read(_handle, b, _headerSize + (id - 1) * 4L);
        return BinaryPrimitives.ReadUInt32LittleEndian(b);
    }

    /// <summary>Decodes a sprite; null for empty sprites (ID 0 or offset 0). Damaged data raises <see cref="InvalidDataException"/>.</summary>
    public RgbaImage? Read(uint id)
    {
        if (id == 0) return null;
        uint offset = OffsetOf(id);
        if (offset == 0) return null;
        if (offset + 5L > Length) throw new InvalidDataException($"Sprite {id}: its offset lies outside the file.");
        Span<byte> head = stackalloc byte[5];
        RandomAccess.Read(_handle, head, offset);
        int size = BinaryPrimitives.ReadUInt16LittleEndian(head[3..]);
        if (offset + 5L + size > Length) throw new InvalidDataException($"Sprite {id}: its data runs past the end of the file.");
        var data = new byte[size];
        RandomAccess.Read(_handle, data, offset + 5L);
        return Decode(data, Profile.Transparency, id);
    }

    /// <summary>Decodes the run-length pixel data of one sprite.</summary>
    public static RgbaImage Decode(ReadOnlySpan<byte> data, bool alpha, uint id = 0)
    {
        var image = new RgbaImage(SpriteSize, SpriteSize);
        var px = image.Pixels;
        int channels = alpha ? 4 : 3;
        int read = 0, write = 0;
        while (read < data.Length)
        {
            if (read + 4 > data.Length) throw new InvalidDataException($"Sprite {id}: truncated pixel run.");
            int transparent = BinaryPrimitives.ReadUInt16LittleEndian(data[read..]);
            int colored = BinaryPrimitives.ReadUInt16LittleEndian(data[(read + 2)..]);
            read += 4;
            write += transparent;
            if (write + colored > px.Length || read + colored * channels > data.Length)
                throw new InvalidDataException($"Sprite {id}: pixel runs exceed the 32×32 sprite.");
            for (int i = 0; i < colored; i++)
            {
                byte r = data[read], g = data[read + 1], b = data[read + 2];
                byte a = alpha ? data[read + 3] : (byte)255;
                px[write++] = (uint)(a << 24 | r << 16 | g << 8 | b);
                read += channels;
            }
        }
        if (write > px.Length) throw new InvalidDataException($"Sprite {id}: pixel runs exceed the 32×32 sprite.");
        return image;
    }

    public void Dispose() => _handle.Dispose();
}

/// <summary>
/// Writes a legacy Tibia.spr. Sprite data is collected in a temporary file next to the output and
/// assembled behind the header and offset table when the writer is finished, so files of any size
/// are written without holding them in memory.
/// </summary>
public sealed class LegacySprWriter : IDisposable
{
    private readonly LegacyClientProfile _profile;
    private readonly string _dataPath;
    private readonly FileStream _data;
    private readonly List<uint> _offsets = [];
    private readonly byte[] _buffer = new byte[5 + 4 + 32 * 32 * 8];
    private bool _finished;

    public LegacySprWriter(string dataTempPath, LegacyClientProfile profile)
    {
        _profile = profile;
        _dataPath = dataTempPath;
        _data = new FileStream(dataTempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 20);
    }

    public int Count => _offsets.Count;

    /// <summary>Pixels with partial transparency that became fully opaque because the profile stores no alpha.</summary>
    public long FlattenedPixels { get; private set; }

    /// <summary>Encodes one 32×32 sprite and returns its ID (1-based).</summary>
    public uint Add(RgbaImage sprite)
    {
        if (sprite.Width != LegacySprReader.SpriteSize || sprite.Height != LegacySprReader.SpriteSize)
            throw new ArgumentException("Legacy sprites are 32×32 pixels.", nameof(sprite));
        int length = Encode(sprite.Pixels, _profile.Transparency, _buffer, out long flattened);
        FlattenedPixels += flattened;
        uint offset = checked((uint)_data.Position);
        _data.Write(_buffer, 0, length);
        _offsets.Add(offset + 1);  // +1: 0 is reserved for "empty"; corrected in Finish
        return (uint)_offsets.Count;
    }

    /// <summary>Adds an empty sprite (offset 0) and returns its ID.</summary>
    public uint AddEmpty()
    {
        _offsets.Add(0);
        return (uint)_offsets.Count;
    }

    /// <summary>Encodes pixel data with color key, size and runs. Returns the number of bytes written.</summary>
    public static int Encode(ReadOnlySpan<uint> pixels, bool alpha, byte[] output, out long flattened)
    {
        flattened = 0;
        // Color key bytes (unused by the clients; CipSoft writes magenta).
        output[0] = 0xFF;
        output[1] = 0x00;
        output[2] = 0xFF;
        int pos = 5;
        int i = 0;
        while (i < pixels.Length)
        {
            int transparent = 0;
            while (i < pixels.Length && pixels[i] >> 24 == 0)
            {
                transparent++;
                i++;
            }
            if (i == pixels.Length) break; // trailing transparency is implied
            int countAt = pos + 2;
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(pos), (ushort)transparent);
            pos += 4;
            int colored = 0;
            while (i < pixels.Length && pixels[i] >> 24 != 0)
            {
                uint p = pixels[i++];
                output[pos++] = (byte)(p >> 16);
                output[pos++] = (byte)(p >> 8);
                output[pos++] = (byte)p;
                if (alpha) output[pos++] = (byte)(p >> 24);
                else if (p >> 24 != 255) flattened++;
                colored++;
            }
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(countAt), (ushort)colored);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(3), (ushort)(pos - 5));
        return pos;
    }

    /// <summary>
    /// The image a sprite becomes when written with this profile: fully transparent pixels lose
    /// their (invisible) color, and without an alpha channel every other pixel becomes opaque.
    /// </summary>
    public static RgbaImage AsStored(RgbaImage sprite, bool alpha)
    {
        var copy = sprite.Clone();
        for (int i = 0; i < copy.Pixels.Length; i++)
        {
            uint p = copy.Pixels[i];
            copy.Pixels[i] = p >> 24 == 0 ? 0 : alpha ? p : p | 0xFF000000;
        }
        return copy;
    }

    /// <summary>Writes the complete file to <paramref name="path"/> and deletes the temporary data.</summary>
    public void Finish(string path, CancellationToken ct = default)
    {
        if (_finished) throw new InvalidOperationException("The sprite file is already finished.");
        _finished = true;
        int headerSize = _profile.ExtendedSprites ? 8 : 6;
        if (!_profile.ExtendedSprites && _offsets.Count > ushort.MaxValue)
            throw new InvalidDataException($"{_offsets.Count:N0} sprites do not fit the 16-bit count of {_profile.Name}; an extended client is needed.");
        long dataStart = headerSize + 4L * _offsets.Count;
        if (dataStart + _data.Length > uint.MaxValue) throw new InvalidDataException("The sprite file would be larger than 4 GB, which the format cannot address.");
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        var header = new byte[headerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, _profile.SprSignature);
        if (_profile.ExtendedSprites) BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)_offsets.Count);
        else BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), (ushort)_offsets.Count);
        output.Write(header);
        var table = new byte[4 * _offsets.Count];
        for (int i = 0; i < _offsets.Count; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(i * 4), _offsets[i] == 0 ? 0 : (uint)(dataStart + _offsets[i] - 1));
        output.Write(table);
        _data.Position = 0;
        var copy = new byte[1 << 20];
        int n;
        while ((n = _data.Read(copy, 0, copy.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            output.Write(copy, 0, n);
        }
    }

    public void Dispose()
    {
        _data.Dispose();
        try { File.Delete(_dataPath); } catch (IOException) { /* temporary file; removed by the temp clean-up */ }
    }
}
