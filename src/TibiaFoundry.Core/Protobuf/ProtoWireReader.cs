using Google.Protobuf;

namespace TibiaFoundry.Core.Protobuf;

/// <summary>The wire data ended inside a field, or a tag or length is malformed.</summary>
public sealed class ProtoWireException(string message) : IOException(message);

/// <summary>
/// Allocation-free reader of the protobuf wire format over a span (8.8.1). The schema inspection and
/// the Latin-1 repair walk every field of a data file; reading nested messages as slices of the one
/// buffer, instead of copying each into a new array and stream per nesting level, keeps a 5 MB
/// appearances file from allocating gigabytes.
/// </summary>
public ref struct ProtoWireReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _pos;

    public readonly bool AtEnd => _pos >= _data.Length;

    /// <summary>The next tag (never 0; check <see cref="AtEnd"/> first).</summary>
    public uint ReadTag()
    {
        uint tag = (uint)ReadVarint64();
        if (WireFormat.GetTagFieldNumber(tag) == 0) throw new ProtoWireException("Protocol message contained an invalid tag (zero).");
        return tag;
    }

    public ulong ReadVarint64()
    {
        ulong result = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (_pos >= _data.Length) throw Truncated();
            byte b = _data[_pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new ProtoWireException("Protocol message contained a malformed varint.");
    }

    public ReadOnlySpan<byte> ReadFixed(int size)
    {
        if (_data.Length - _pos < size) throw Truncated();
        var span = _data.Slice(_pos, size);
        _pos += size;
        return span;
    }

    /// <summary>The content of a length-delimited field (a slice of the input, not a copy).</summary>
    public ReadOnlySpan<byte> ReadLengthDelimited()
    {
        ulong length = ReadVarint64();
        if (length > int.MaxValue) throw new ProtoWireException("Protocol message contained a negative or oversized length.");
        return ReadFixed((int)length);
    }

    private static ProtoWireException Truncated() =>
        new("While parsing a protocol message, the input ended unexpectedly in the middle of a field.");

    /// <summary>Bytes of the canonical (shortest) varint encoding of a value.</summary>
    public static int VarintSize(ulong value)
    {
        int size = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            size++;
        }
        return size;
    }

    /// <summary>Writes a canonical varint at <paramref name="pos"/>.</summary>
    public static void WriteVarint(Span<byte> output, ref int pos, ulong value)
    {
        while (value >= 0x80)
        {
            output[pos++] = (byte)(value | 0x80);
            value >>= 7;
        }
        output[pos++] = (byte)value;
    }
}
