using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Protobuf;

/// <summary>
/// Loads CipSoft protobuf data files.
/// </summary>
/// <remarks>
/// Some client strings are not valid UTF-8 (they are Latin-1, e.g. byte 0xEE = 'î'). Google.Protobuf
/// 3.27 (used by the original) silently replaced such bytes with U+FFFD; current versions reject the
/// whole file. When strict parsing fails, the raw bytes are walked using the message descriptors and
/// every invalid string field is re-encoded from Latin-1 to UTF-8, so names display correctly.
/// </remarks>
public static class ProtoLoader
{
    /// <summary>
    /// Parses a data file. Every failure is reported as a <see cref="DataFileException"/> that names
    /// the file and says whether it is missing, unreadable or could not be decoded, instead of a raw
    /// protobuf or I/O exception.
    /// </summary>
    public static T Load<T>(string path, MessageParser<T> parser) where T : IMessage<T>, new()
    {
        byte[] data;
        try
        {
            data = ReadShared(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new DataFileException(DataProblemKind.Missing, path, $"{Path.GetFileName(path)} was not found.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DataFileException(DataProblemKind.Unreadable, path, $"{Path.GetFileName(path)} could not be read: {ex.Message}", ex);
        }
        if (data.Length == 0)
            throw new DataFileException(DataProblemKind.Undecodable, path, $"{Path.GetFileName(path)} is empty.");
        try
        {
            return parser.ParseFrom(data);
        }
        catch (InvalidProtocolBufferException ex) when (ex.InnerException is DecoderFallbackException)
        {
            try
            {
                var repaired = Utf8Repair.Repair(new T().Descriptor, data, out int fixedStrings);
                AppLog.Info($"{Path.GetFileName(path)}: re-encoded {fixedStrings} Latin-1 string(s) as UTF-8.");
                return parser.ParseFrom(repaired);
            }
            catch (Exception inner) when (inner is InvalidProtocolBufferException or InvalidDataException)
            {
                throw Undecodable(path, inner);
            }
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw Undecodable(path, ex);
        }
    }

    private static DataFileException Undecodable(string path, Exception ex) =>
        new(DataProblemKind.Undecodable, path,
            L.F("ProtoLoader.0CouldNotBeDecodedWith", Path.GetFileName(path), typeof(ProtoLoader).Assembly.GetName().Version?.ToString(2), ex.Message), ex);

    /// <summary>Reads a file without locking it against the Tibia client or other readers.</summary>
    public static byte[] ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[fs.Length];
        fs.ReadExactly(buffer);
        return buffer;
    }
}

/// <summary>Descriptor-driven rewrite of a protobuf message that fixes non-UTF-8 string fields.</summary>
/// <remarks>
/// 8.8.1: two passes over slices of the one input buffer. The first measures the rewritten size of
/// every nested message (in the order they are met), the second writes straight into an output array
/// of the final size. 8.8.0 copied every nested message into a new array and stream on each level,
/// which allocated about 1.8 GB for the 5 MB appearances file on every load. The output is the same:
/// varints in their shortest form, fixed fields as they are, invalid strings re-encoded from Latin-1.
/// </remarks>
public static class Utf8Repair
{
    public static byte[] Repair(MessageDescriptor descriptor, byte[] data, out int fixedStrings)
    {
        var sizes = new List<int>();
        int count = 0, total, pos = 0, next = 0;
        byte[] output;
        try
        {
            total = Measure(descriptor, data, sizes, ref count);
            output = new byte[total];
            Write(descriptor, data, output, ref pos, sizes, ref next);
        }
        catch (ProtoWireException ex)
        {
            // Damaged wire data is reported like the other undecodable content (8.8.0: InvalidProtocolBufferException).
            throw new InvalidDataException(ex.Message, ex);
        }
        if (pos != total) throw new InvalidDataException("The repaired message has an unexpected length.");
        fixedStrings = count;
        return output;
    }

    /// <summary>Rewritten size of a message; the size of each nested message is added to <paramref name="sizes"/> before its own nested ones.</summary>
    private static int Measure(MessageDescriptor descriptor, ReadOnlySpan<byte> data, List<int> sizes, ref int fixedStrings)
    {
        var input = new ProtoWireReader(data);
        long size = 0;
        while (!input.AtEnd)
        {
            uint tag = input.ReadTag();
            var wireType = WireFormat.GetTagWireType(tag);
            size += ProtoWireReader.VarintSize(tag);
            switch (wireType)
            {
                case WireFormat.WireType.Varint:
                    size += ProtoWireReader.VarintSize(input.ReadVarint64());
                    break;
                case WireFormat.WireType.Fixed64:
                    size += input.ReadFixed(8).Length;
                    break;
                case WireFormat.WireType.Fixed32:
                    size += input.ReadFixed(4).Length;
                    break;
                case WireFormat.WireType.LengthDelimited:
                    var content = input.ReadLengthDelimited();
                    var field = descriptor.FindFieldByNumber(WireFormat.GetTagFieldNumber(tag));
                    int length;
                    if (field?.FieldType == FieldType.String)
                    {
                        if (System.Text.Unicode.Utf8.IsValid(content)) length = content.Length;
                        else
                        {
                            fixedStrings++;
                            length = Latin1AsUtf8Length(content);
                        }
                    }
                    else if (field?.FieldType == FieldType.Message)
                    {
                        int slot = sizes.Count;
                        sizes.Add(0);
                        length = Measure(field.MessageType, content, sizes, ref fixedStrings);
                        sizes[slot] = length;
                    }
                    else length = content.Length;
                    size += ProtoWireReader.VarintSize((uint)length) + length;
                    break;
                default:
                    throw new InvalidDataException($"Unsupported wire type {wireType} in {descriptor.FullName}.");
            }
            if (size > int.MaxValue) throw new InvalidDataException($"The repaired {descriptor.FullName} would exceed 2 GB.");
        }
        return (int)size;
    }

    private static void Write(MessageDescriptor descriptor, ReadOnlySpan<byte> data, byte[] output, ref int pos, List<int> sizes, ref int next)
    {
        var input = new ProtoWireReader(data);
        while (!input.AtEnd)
        {
            uint tag = input.ReadTag();
            ProtoWireReader.WriteVarint(output, ref pos, tag);
            switch (WireFormat.GetTagWireType(tag))
            {
                case WireFormat.WireType.Varint:
                    ProtoWireReader.WriteVarint(output, ref pos, input.ReadVarint64());
                    break;
                case WireFormat.WireType.Fixed64:
                    input.ReadFixed(8).CopyTo(output.AsSpan(pos));
                    pos += 8;
                    break;
                case WireFormat.WireType.Fixed32:
                    input.ReadFixed(4).CopyTo(output.AsSpan(pos));
                    pos += 4;
                    break;
                default:
                    var content = input.ReadLengthDelimited();
                    var field = descriptor.FindFieldByNumber(WireFormat.GetTagFieldNumber(tag));
                    if (field?.FieldType == FieldType.Message)
                    {
                        ProtoWireReader.WriteVarint(output, ref pos, (uint)sizes[next++]);
                        Write(field.MessageType, content, output, ref pos, sizes, ref next);
                    }
                    else if (field?.FieldType == FieldType.String && !System.Text.Unicode.Utf8.IsValid(content))
                    {
                        ProtoWireReader.WriteVarint(output, ref pos, (uint)Latin1AsUtf8Length(content));
                        foreach (byte b in content)
                        {
                            if (b < 0x80) output[pos++] = b;
                            else
                            {
                                output[pos++] = (byte)(0xC0 | (b >> 6));
                                output[pos++] = (byte)(0x80 | (b & 0x3F));
                            }
                        }
                    }
                    else
                    {
                        ProtoWireReader.WriteVarint(output, ref pos, (uint)content.Length);
                        content.CopyTo(output.AsSpan(pos));
                        pos += content.Length;
                    }
                    break;
            }
        }
    }

    /// <summary>UTF-8 length of Latin-1 text: one byte below 0x80, two from 0x80.</summary>
    private static int Latin1AsUtf8Length(ReadOnlySpan<byte> latin1)
    {
        int length = latin1.Length;
        foreach (byte b in latin1)
            if (b >= 0x80) length++;
        return length;
    }
}
