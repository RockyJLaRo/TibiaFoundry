using Google.Protobuf;
using Google.Protobuf.Reflection;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Compatibility;

/// <summary>A field found in a data file that the compiled schema does not describe (or describes differently).</summary>
/// <param name="Message">Full protobuf message name, e.g. "tibia.protobuf.appearances.Appearance".</param>
/// <param name="FieldNumber">Field number on the wire.</param>
/// <param name="WireType">Wire type found.</param>
/// <param name="Occurrences">How often it occurred.</param>
/// <param name="Mismatch">True when the field is known but arrived with an unexpected wire type (a changed field type).</param>
/// <param name="Sample">Short preview of the first occurrence (value or printable text) to help maintainers identify it.</param>
public sealed record SchemaDrift(string Message, int FieldNumber, WireFormat.WireType WireType, int Occurrences, bool Mismatch, string Sample = "")
{
    /// <summary>True when this drift is listed in <see cref="TibiaFormat.KnownSchemaDrift"/> (present in validated data, deliberately ignored).</summary>
    public bool IsKnown => !Mismatch && TibiaFormat.KnownSchemaDrift.Contains((Message, FieldNumber));

    public override string ToString() => (Mismatch
        ? L.F("SchemaInspector.0Field1HasADifferent", Message, FieldNumber, WireType, Occurrences)
        : L.F("SchemaInspector.01Field234", Message, (IsKnown ? L.T("SchemaInspector.KnownUnmapped") : "unknown"), FieldNumber, WireType, Occurrences)) +
        (Sample.Length > 0 ? $", e.g. {Sample}" : "");
}

/// <summary>Result of comparing a data file's wire content with the compiled schema.</summary>
public sealed record SchemaReport(IReadOnlyList<SchemaDrift> Drift, bool Truncated, string? Error)
{
    /// <summary>Fields not described by the schema and not in the known-drift list: new client information.</summary>
    public bool HasUnknownFields => Drift.Any(d => !d.Mismatch && !d.IsKnown);
    public bool HasMismatches => Drift.Any(d => d.Mismatch);
    public bool IsExact => Drift.Count == 0 && !Truncated && Error == null;
}

/// <summary>
/// Walks the raw protobuf wire format of a data file guided by the compiled descriptors and
/// reports fields the schema does not know. Google.Protobuf keeps such fields silently when
/// parsing, so without this check a client update that adds information (or changes a field's
/// type) would go unnoticed. Read-only; never modifies data.
/// </summary>
public static class SchemaInspector
{
    private const int MaxDepth = 32;

    public static SchemaReport Inspect(MessageDescriptor descriptor, byte[] data)
    {
        var counts = new Dictionary<(string, int, WireFormat.WireType, bool), (int Count, string Sample)>();
        try
        {
            Walk(descriptor, data, counts, 0);
            return new SchemaReport(ToList(counts), false, null);
        }
        catch (Protobuf.ProtoWireException ex)
        {
            return new SchemaReport(ToList(counts), true, ex.Message);
        }
        catch (InvalidDataException ex)
        {
            return new SchemaReport(ToList(counts), false, ex.Message);
        }
    }

    private static List<SchemaDrift> ToList(Dictionary<(string Message, int Field, WireFormat.WireType Wire, bool Mismatch), (int Count, string Sample)> counts) =>
        counts.Select(kv => new SchemaDrift(kv.Key.Message, kv.Key.Field, kv.Key.Wire, kv.Value.Count, kv.Key.Mismatch, kv.Value.Sample))
              .OrderBy(d => d.Message, StringComparer.Ordinal).ThenBy(d => d.FieldNumber).ToList();

    private static void Walk(MessageDescriptor descriptor, ReadOnlySpan<byte> data, Dictionary<(string, int, WireFormat.WireType, bool), (int Count, string Sample)> counts, int depth)
    {
        if (depth > MaxDepth) throw new InvalidDataException($"Message nesting deeper than {MaxDepth} levels in {descriptor.FullName}.");
        // 8.8.1: nested messages are slices of the one buffer (8.8.0 copied each into a new array per level).
        var input = new Protobuf.ProtoWireReader(data);
        while (!input.AtEnd)
        {
            uint tag = input.ReadTag();
            var wire = WireFormat.GetTagWireType(tag);
            int number = WireFormat.GetTagFieldNumber(tag);
            var field = descriptor.FindFieldByNumber(number);
            ReadOnlySpan<byte> nested = default;
            bool isNested = false;
            ulong scalar = 0;
            switch (wire)
            {
                case WireFormat.WireType.Varint: scalar = input.ReadVarint64(); break;
                case WireFormat.WireType.Fixed64: scalar = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(input.ReadFixed(8)); break;
                case WireFormat.WireType.Fixed32: scalar = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(input.ReadFixed(4)); break;
                case WireFormat.WireType.LengthDelimited: nested = input.ReadLengthDelimited(); isNested = true; break;
                default: throw new InvalidDataException($"Unsupported wire type {wire} (field {number}) in {descriptor.FullName}.");
            }

            if (field == null)
            {
                Count(counts, descriptor.FullName, number, wire, mismatch: false, isNested, nested, scalar);
            }
            else if (!Accepts(field, wire))
            {
                Count(counts, descriptor.FullName, number, wire, mismatch: true, isNested, nested, scalar);
            }
            else if (field.FieldType == FieldType.Message && isNested)
            {
                Walk(field.MessageType, nested, counts, depth + 1);
            }
        }
    }

    private static void Count(Dictionary<(string, int, WireFormat.WireType, bool), (int Count, string Sample)> counts, string message, int number,
        WireFormat.WireType wire, bool mismatch, bool isBytes, ReadOnlySpan<byte> bytes, ulong scalar)
    {
        var key = (message, number, wire, mismatch);
        if (counts.TryGetValue(key, out var existing)) counts[key] = (existing.Count + 1, existing.Sample);
        else counts[key] = (1, !isBytes ? scalar.ToString(System.Globalization.CultureInfo.InvariantCulture) : Preview(bytes.ToArray()));
    }

    /// <summary>Printable text when the bytes look like text, otherwise the first bytes in hex.</summary>
    private static string Preview(byte[] bytes)
    {
        bool text = bytes.Length > 0 && bytes.All(b => b is >= 0x20 and < 0x7F);
        if (text) return "'" + System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 40)) + (bytes.Length > 40 ? "…" : "") + "'";
        return L.F("SchemaInspector.0Bytes12", bytes.Length, Convert.ToHexString(bytes, 0, Math.Min(bytes.Length, 16)), (bytes.Length > 16 ? "…" : ""));
    }

    /// <summary>Wire types that are valid for a field (repeated scalars may be packed).</summary>
    private static bool Accepts(FieldDescriptor field, WireFormat.WireType wire)
    {
        var expected = field.FieldType switch
        {
            FieldType.String or FieldType.Bytes or FieldType.Message => WireFormat.WireType.LengthDelimited,
            FieldType.Fixed32 or FieldType.SFixed32 or FieldType.Float => WireFormat.WireType.Fixed32,
            FieldType.Fixed64 or FieldType.SFixed64 or FieldType.Double => WireFormat.WireType.Fixed64,
            FieldType.Group => WireFormat.WireType.StartGroup,
            _ => WireFormat.WireType.Varint,
        };
        return wire == expected || (field.IsRepeated && wire == WireFormat.WireType.LengthDelimited && expected != WireFormat.WireType.LengthDelimited);
    }
}
