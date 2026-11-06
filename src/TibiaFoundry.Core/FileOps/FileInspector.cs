using TibiaFoundry.Core.Common;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.Editing.Model;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Legacy;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Rcc;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.FileOps;

/// <summary>How far a file was checked. Each level includes the ones before it.</summary>
public enum InspectionLevel
{
    /// <summary>Nothing could be established beyond the file system information.</summary>
    None,
    /// <summary>The format was recognized (signature, or for some formats the name).</summary>
    Identified,
    /// <summary>The format's header was read (dimensions, counts, versions).</summary>
    HeaderRead,
    /// <summary>The whole structure was checked (chunks and checksums, offsets, or a complete parse).</summary>
    StructureChecked,
    /// <summary>The content was decoded completely (for example every pixel of an image).</summary>
    Decoded,
}

/// <summary>What <see cref="FileInspector"/> found out about one file.</summary>
public sealed class InspectionResult
{
    public required string Path { get; init; }
    public long Size { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public FileFormat? Format { get; set; }
    public FormatEvidence Evidence { get; set; }
    public InspectionLevel Level { get; set; }

    /// <summary>Format-specific facts (dimensions, entries, versions...), in display order.</summary>
    public List<KeyValuePair<string, string>> Details { get; } = [];

    /// <summary>Attribution embedded in the file (PNG and GIF), if any.</summary>
    public FoundAttribution? Attribution { get; set; }

    public List<string> Problems { get; } = [];

    /// <summary>The extension suggests another format than the content.</summary>
    public bool ExtensionMismatch { get; set; }

    public string LevelText => Level switch
    {
        InspectionLevel.Decoded => L.T("FileInspector.DecodedCompletely"),
        InspectionLevel.StructureChecked => L.T("FileInspector.StructureChecked"),
        InspectionLevel.HeaderRead => L.T("FileInspector.HeaderRead"),
        InspectionLevel.Identified => L.T("FileInspector.IdentifiedOnly"),
        _ => L.T("FileInspector.NotIdentified"),
    };

    public string DetailText => string.Join("; ", Details.Select(d => $"{d.Key}: {d.Value}"));

    internal void Add(string key, object? value) => Details.Add(new(key, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""));
}

/// <summary>
/// Identifies files and checks them as far as Tibia Foundry can: images, archives, Qt resource
/// files, the client's LZMA, protobuf and JSON files, and legacy Tibia.dat/Tibia.spr. The result
/// always says how deeply a file was checked, so a recognized header is never presented as a
/// fully validated file.
/// </summary>
public static class FileInspector
{
    /// <summary>Files larger than this are identified but not parsed (to keep inspection fast).</summary>
    public const long MaxParseBytes = 512L * 1024 * 1024;

    public static InspectionResult Inspect(string path, bool decodeImages = true, CancellationToken ct = default)
    {
        var info = new FileInfo(path);
        var result = new InspectionResult { Path = info.FullName, Size = info.Length, ModifiedUtc = info.LastWriteTimeUtc };
        if (info.Length == 0)
        {
            result.Problems.Add(L.T("FileInspector.TheFileIsEmpty"));
            return result;
        }
        FormatIdentification id;
        try
        {
            id = FileSignatures.Identify(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Problems.Add(L.T("FileInspector.CouldNotBeRead") + ex.Message);
            return result;
        }
        result.Format = id.Format;
        result.Evidence = id.Evidence;
        result.Level = id.Format == null ? InspectionLevel.None : InspectionLevel.Identified;
        if (id.Format != null && id.Evidence == FormatEvidence.Signature && id.Format.Category != "Text" && !id.Format.ExtensionMatches(path) && info.Extension.Length > 0)
        {
            result.ExtensionMismatch = true;
            result.Problems.Add(L.F("FileInspector.TheExtension0DoesNotMatch", info.Extension, id.Format.Name));
        }
        if (id.Format == null || info.Length > MaxParseBytes) return result;
        try
        {
            ct.ThrowIfCancellationRequested();
            Check(result, id.Format, info, decodeImages);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
                                       or ImageMagick.MagickException or Google.Protobuf.InvalidProtocolBufferException or ArgumentException or NotSupportedException)
        {
            result.Problems.Add(L.F("FileInspector.DamagedOrNotAValid0", id.Format.Name, ex.Message));
        }
        return result;
    }

    private static void Check(InspectionResult r, FileFormat format, FileInfo info, bool decodeImages)
    {
        switch (format.Id)
        {
            case "png":
                {
                    byte[] data = File.ReadAllBytes(info.FullName);
                    CheckPngChunks(r, data);
                    r.Attribution = PngMetadata.Read(data);
                    if (decodeImages)
                    {
                        ImageIO.LoadFromBytes(data);
                        r.Level = InspectionLevel.Decoded;
                    }
                    break;
                }
            case "gif":
                {
                    byte[] data = File.ReadAllBytes(info.FullName);
                    r.Add("size", $"{BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6))}×{BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8))}");
                    r.Level = InspectionLevel.HeaderRead;
                    r.Attribution = GifMetadata.Read(data);
                    if (decodeImages)
                    {
                        var decoded = ImageFile.Decode(data);
                        r.Add("frames", decoded.Frames.Count);
                        r.Level = InspectionLevel.Decoded;
                    }
                    break;
                }
            case "jpeg" or "bmp" or "webp" or "tiff" or "ico":
                {
                    var ping = new ImageMagick.MagickImageInfo(info.FullName);
                    r.Add("size", $"{ping.Width}×{ping.Height}");
                    r.Level = InspectionLevel.HeaderRead;
                    if (decodeImages)
                    {
                        ImageFile.Decode(File.ReadAllBytes(info.FullName));
                        r.Level = InspectionLevel.Decoded;
                    }
                    break;
                }
            case "rcc":
                {
                    var report = RccValidator.Validate(File.ReadAllBytes(info.FullName));
                    r.Add(L.T("FileInspector.FormatVersion"), report.FormatVersion);
                    r.Add("files", report.Files);
                    r.Add("folders", report.Folders);
                    if (report.IsValid) r.Level = InspectionLevel.StructureChecked;
                    r.Problems.AddRange(report.Errors);
                    break;
                }
            case "cip-lzma":
                {
                    byte[] data = File.ReadAllBytes(info.FullName);
                    if (CipLzma.TryReadHeader(data, out var header))
                    {
                        r.Add(L.T("FileInspector.DecompressedSize"), header.DecompressedSize);
                        r.Level = InspectionLevel.HeaderRead;
                    }
                    byte[] content = CipLzma.Decompress(data);
                    r.Level = InspectionLevel.StructureChecked;
                    string? expected = CompatibilityChecker.ContentHashFromName(info.Name.Replace(".lzma", "", StringComparison.OrdinalIgnoreCase));
                    if (expected != null)
                    {
                        bool ok = string.Equals(FileHashing.HashBytes(content), expected, StringComparison.OrdinalIgnoreCase);
                        r.Add(L.T("FileInspector.ChecksumInName"), ok ? "matches" : L.T("FileInspector.DoesNOTMatch"));
                        if (!ok) r.Problems.Add(L.T("FileInspector.TheContentDoesNotMatchThe"));
                    }
                    if (content.Length > 2 && content[0] == 'B' && content[1] == 'M')
                    {
                        var bmp = BmpDecoder.Decode(content);
                        r.Add("image", $"{bmp.Width}×{bmp.Height}");
                        r.Level = InspectionLevel.Decoded;
                    }
                    break;
                }
            case "tibia-dat" when info.Name.StartsWith("appearances", StringComparison.OrdinalIgnoreCase):
                {
                    var appearances = EditableAppearances.Parser.ParseFrom(File.ReadAllBytes(info.FullName));
                    r.Format = ClientProtobuf;
                    r.Add("objects", appearances.Object.Count);
                    r.Add("outfits", appearances.Outfit.Count);
                    r.Add("effects", appearances.Effect.Count);
                    r.Add("missiles", appearances.Missile.Count);
                    r.Level = InspectionLevel.StructureChecked;
                    break;
                }
            case "tibia-dat" when ClientDataPrefixes.Any(p => info.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)):
                r.Format = ClientProtobuf;
                r.Evidence = FormatEvidence.Name;
                r.Add("note", L.T("FileInspector.DataFileOfTheModernClient"));
                break;
            case "tibia-dat":
                {
                    var detected = LegacyDatCodec.Detect(File.ReadAllBytes(info.FullName));
                    if (detected is { } d)
                    {
                        r.Add("layout", d.Profile.Name);
                        r.Add("items", d.File.Items.Count);
                        r.Add("outfits", d.File.Outfits.Count);
                        r.Add("effects", d.File.Effects.Count);
                        r.Add("missiles", d.File.Missiles.Count);
                        r.Level = InspectionLevel.StructureChecked;
                    }
                    else r.Problems.Add(L.T("FileInspector.NoKnownClientLayoutReadsThis"));
                    break;
                }
            case "tibia-spr":
                {
                    using var fs = File.OpenRead(info.FullName);
                    var head = new byte[8];
                    fs.ReadExactly(head);
                    uint signature = BinaryPrimitives.ReadUInt32LittleEndian(head);
                    uint count32 = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4));
                    bool extended = 8 + 4L * count32 <= info.Length;
                    r.Add("signature", $"0x{signature:X8}");
                    r.Add("sprites", extended ? count32 : BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(4)));
                    r.Add(L.T("FileInspector.SpriteIDs"), extended ? L.T("FileInspector.32BitExtended") : "16-bit");
                    r.Level = InspectionLevel.HeaderRead;
                    break;
                }
            case "json":
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(info.FullName));
                    r.Add("root", doc.RootElement.ValueKind);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array) r.Add("entries", doc.RootElement.GetArrayLength());
                    r.Level = InspectionLevel.StructureChecked;
                    if (string.Equals(info.Name, TibiaFormat.AssetCatalogFile, StringComparison.OrdinalIgnoreCase))
                    {
                        var catalog = AssetCatalog.Load(info.DirectoryName!);
                        r.Add(L.T("FileInspector.SpriteSheets"), catalog.Sheets.Count);
                        r.Problems.AddRange(catalog.Problems);
                    }
                    break;
                }
            case "zip":
                {
                    using var zip = ZipFile.OpenRead(info.FullName);
                    r.Add("entries", zip.Entries.Count);
                    r.Add("uncompressed", ReportWriter.FormatSize(zip.Entries.Sum(e => e.Length)));
                    r.Level = InspectionLevel.StructureChecked;
                    break;
                }
        }
    }

    /// <summary>The modern client's protobuf data files (no signature; recognized by name).</summary>
    public static readonly FileFormat ClientProtobuf = new("tibia-protobuf", L.T("FileInspector.TibiaClientDataProtobuf"), [".dat"], L.T("FileInspector.TibiaClientData"));

    private static readonly string[] ClientDataPrefixes = ["staticdata", "staticmapdata", "map-", "sounds", "proficiencies"];

    /// <summary>Walks the PNG chunks and checks their CRC-32 values.</summary>
    private static void CheckPngChunks(InspectionResult r, byte[] data)
    {
        int pos = 8, chunks = 0, textChunks = 0;
        bool sawEnd = false;
        while (pos + 12 <= data.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
            if (length < 0 || pos + 12L + length > data.Length) throw new InvalidDataException($"chunk {chunks + 1} runs past the end of the file");
            string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 8 + length));
            if (Crc32.Compute(data.AsSpan(pos + 4, length + 4)) != crc) throw new InvalidDataException($"chunk {type} has a wrong checksum");
            if (chunks == 0)
            {
                if (type != "IHDR") throw new InvalidDataException("the first chunk is not IHDR");
                r.Add("size", $"{BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos + 8))}×{BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos + 12))}");
                r.Add(L.T("FileInspector.BitDepth"), data[pos + 16]);
                r.Add(L.T("FileInspector.ColorType"), data[pos + 17]);
            }
            if (type is "tEXt" or "iTXt" or "zTXt") textChunks++;
            chunks++;
            pos += 12 + length;
            if (type == "IEND")
            {
                sawEnd = true;
                break;
            }
        }
        if (!sawEnd) throw new InvalidDataException("the IEND chunk is missing (truncated file)");
        if (pos < data.Length) r.Problems.Add(L.F("FileInspector.0AfterTheEndOfThe", L.Count(data.Length - pos, "Noun.byte")));
        r.Add("chunks", chunks);
        r.Add(L.T("FileInspector.TextFields"), textChunks);
        r.Level = InspectionLevel.StructureChecked;
    }
}
