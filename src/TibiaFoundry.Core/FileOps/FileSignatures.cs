using System.Buffers.Binary;
using System.Text;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.FileOps;

/// <summary>How a file's format was established, from weakest to strongest evidence.</summary>
public enum FormatEvidence
{
    /// <summary>Only the file name or extension suggests the format.</summary>
    Name,
    /// <summary>The first bytes match the format's signature ("magic number").</summary>
    Signature,
    /// <summary>The file was parsed completely by a reader for the format.</summary>
    Parsed,
}

/// <summary>A file format Tibia Foundry can recognize.</summary>
/// <param name="Id">Short stable identifier, e.g. "png".</param>
/// <param name="Name">Human-readable name.</param>
/// <param name="Extensions">Usual extensions (lower case, with the dot); the first is preferred.</param>
/// <param name="Category">Image, Archive, Audio, Document, Program, Text, Tibia client data or Qt resources.</param>
public sealed record FileFormat(string Id, string Name, IReadOnlyList<string> Extensions, string Category)
{
    public bool ExtensionMatches(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());
}

/// <summary>Result of identifying one file.</summary>
public sealed record FormatIdentification(FileFormat? Format, FormatEvidence Evidence, string Detail)
{
    public bool IsKnown => Format != null;
}

/// <summary>
/// Identifies files by their content (signature bytes), not by their extension, so renamed or
/// mislabeled files are recognized. A signature only shows what a file claims to be: it does not
/// prove that the rest of the file is valid. Full validation needs a reader for the format.
/// </summary>
public static class FileSignatures
{
    public static readonly FileFormat Png = new("png", L.T("FileSignatures.PNGImage"), [".png"], "Image");
    public static readonly FileFormat Gif = new("gif", L.T("FileSignatures.GIFImage"), [".gif"], "Image");
    public static readonly FileFormat Jpeg = new("jpeg", L.T("FileSignatures.JPEGImage"), [".jpg", ".jpeg", ".jpe", ".jfif"], "Image");
    public static readonly FileFormat Bmp = new("bmp", L.T("FileSignatures.BMPImage"), [".bmp", ".dib"], "Image");
    public static readonly FileFormat WebP = new("webp", L.T("FileSignatures.WebPImage"), [".webp"], "Image");
    public static readonly FileFormat Tiff = new("tiff", L.T("FileSignatures.TIFFImage"), [".tif", ".tiff"], "Image");
    public static readonly FileFormat Ico = new("ico", L.T("FileSignatures.WindowsIcon"), [".ico"], "Image");
    public static readonly FileFormat Svg = new("svg", L.T("FileSignatures.SVGDrawing"), [".svg"], "Image");
    public static readonly FileFormat Zip = new("zip", L.T("FileSignatures.ZIPArchive"), [".zip", ".docx", ".xlsx", ".pptx", ".jar", ".apk", ".nupkg"], "Archive");
    public static readonly FileFormat GZip = new("gzip", L.T("FileSignatures.GZIPCompressedFile"), [".gz", ".tgz"], "Archive");
    public static readonly FileFormat SevenZip = new("7z", L.T("FileSignatures.7ZipArchive"), [".7z"], "Archive");
    public static readonly FileFormat Rar = new("rar", L.T("FileSignatures.RARArchive"), [".rar"], "Archive");
    public static readonly FileFormat Tar = new("tar", L.T("FileSignatures.TARArchive"), [".tar"], "Archive");
    public static readonly FileFormat XzLzma = new("xz", L.T("FileSignatures.XZCompressedFile"), [".xz"], "Archive");
    public static readonly FileFormat Pdf = new("pdf", L.T("FileSignatures.PDFDocument"), [".pdf"], "Document");
    public static readonly FileFormat Executable = new("pe", L.T("FileSignatures.WindowsProgramOrLibrary"), [".exe", ".dll", ".sys", ".ocx", ".scr"], "Program");
    public static readonly FileFormat Ogg = new("ogg", L.T("FileSignatures.OggAudio"), [".ogg", ".oga"], "Audio");
    public static readonly FileFormat Wav = new("wav", L.T("FileSignatures.WAVAudio"), [".wav"], "Audio");
    public static readonly FileFormat Mp3 = new("mp3", L.T("FileSignatures.MP3Audio"), [".mp3"], "Audio");
    public static readonly FileFormat Flac = new("flac", L.T("FileSignatures.FLACAudio"), [".flac"], "Audio");
    public static readonly FileFormat QtResource = new("rcc", L.T("FileSignatures.QtResourceFileRcc"), [".rcc"], L.T("FileSignatures.QtResources"));
    public static readonly FileFormat CipLzma = new("cip-lzma", L.T("FileSignatures.TibiaLZMAContainerSpriteSheetMap"), [".lzma"], L.T("FileSignatures.TibiaClientData"));
    public static readonly FileFormat LegacySpr = new("tibia-spr", L.T("FileSignatures.TibiaSprSpriteFileLegacyClient"), [".spr"], L.T("FileSignatures.TibiaClientData"));
    public static readonly FileFormat LegacyDat = new("tibia-dat", L.T("FileSignatures.TibiaDatObjectFileLegacyClient"), [".dat"], L.T("FileSignatures.TibiaClientData"));
    public static readonly FileFormat Json = new("json", L.T("FileSignatures.JSONText"), [".json"], "Text");
    public static readonly FileFormat Xml = new("xml", L.T("FileSignatures.XMLText"), [".xml", ".qrc", ".otml"], "Text");
    public static readonly FileFormat Text = new("text", L.T("FileSignatures.PlainText"), [".txt", ".log", ".csv", ".lua", ".md", ".css", ".fnt", ".ini", ".otml", ".hints"], "Text");

    public static IReadOnlyList<FileFormat> All { get; } =
    [
        Png, Gif, Jpeg, Bmp, WebP, Tiff, Ico, Svg, Zip, GZip, SevenZip, Rar, Tar, XzLzma, Pdf, Executable, Ogg, Wav, Mp3, Flac,
        QtResource, CipLzma, LegacySpr, LegacyDat, Json, Xml, Text,
    ];

    /// <summary>Bytes read from the start of a file for identification (TAR needs 262).</summary>
    public const int HeaderLength = 512;

    /// <summary>Legacy Tibia.spr signatures of known client versions (the files have no other marker).</summary>
    public static readonly IReadOnlyDictionary<uint, string> KnownSprSignatures = new Dictionary<uint, string>
    {
        [0x4C220594] = "8.60", [0x53159CA9] = "10.98", [0x59E48E02] = "11 (for 15.x assets)",
    };

    /// <summary>Legacy Tibia.dat signatures of known client versions.</summary>
    public static readonly IReadOnlyDictionary<uint, string> KnownDatSignatures = new Dictionary<uint, string>
    {
        [0x4C2C7993] = "8.60", [0x000042A3] = "10.98", [0x00004A10] = "11 (for 15.x assets)",
    };

    /// <summary>Identifies a file from its first bytes (and, for formats without a signature, its name).</summary>
    public static FormatIdentification Identify(string path)
    {
        byte[] head;
        long length;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            length = fs.Length;
            head = new byte[(int)Math.Min(HeaderLength, length)];
            fs.ReadExactly(head);
        }
        return Identify(head, Path.GetFileName(path), length);
    }

    public static FormatIdentification Identify(ReadOnlySpan<byte> head, string? fileName = null, long length = -1)
    {
        if (head.Length == 0) return new(null, FormatEvidence.Signature, L.T("FileSignatures.TheFileIsEmpty"));
        if (StartsWith(head, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return Sig(Png);
        if (StartsWith(head, "GIF87a"u8) || StartsWith(head, "GIF89a"u8)) return Sig(Gif);
        if (StartsWith(head, [0xFF, 0xD8, 0xFF])) return Sig(Jpeg);
        if (head.Length >= 14 && head[0] == 'B' && head[1] == 'M' && (length < 0 || BinaryPrimitives.ReadUInt32LittleEndian(head[2..]) <= length + 16)) return Sig(Bmp);
        if (head.Length >= 12 && StartsWith(head, "RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return Sig(WebP);
        if (head.Length >= 12 && StartsWith(head, "RIFF"u8) && head[8..12].SequenceEqual("WAVE"u8)) return Sig(Wav);
        if (StartsWith(head, [0x49, 0x49, 0x2A, 0x00]) || StartsWith(head, [0x4D, 0x4D, 0x00, 0x2A])) return Sig(Tiff);
        if (StartsWith(head, [0x00, 0x00, 0x01, 0x00]) && head.Length >= 6 && head[4] + head[5] > 0) return Sig(Ico);
        if (StartsWith(head, [0x50, 0x4B, 0x03, 0x04]) || StartsWith(head, [0x50, 0x4B, 0x05, 0x06])) return Sig(Zip);
        if (StartsWith(head, [0x1F, 0x8B])) return Sig(GZip);
        if (StartsWith(head, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C])) return Sig(SevenZip);
        if (StartsWith(head, "Rar!\x1A\x07"u8)) return Sig(Rar);
        if (StartsWith(head, [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00])) return Sig(XzLzma);
        if (head.Length >= 262 && head[257..262].SequenceEqual("ustar"u8)) return Sig(Tar);
        if (StartsWith(head, "%PDF-"u8)) return Sig(Pdf);
        if (StartsWith(head, "MZ"u8)) return Sig(Executable);
        if (StartsWith(head, "OggS"u8)) return Sig(Ogg);
        if (StartsWith(head, "fLaC"u8)) return Sig(Flac);
        if (StartsWith(head, "ID3"u8) || (head.Length >= 2 && head[0] == 0xFF && (head[1] & 0xE0) == 0xE0 && (head[1] & 0x06) != 0)) return Sig(Mp3);
        if (StartsWith(head, "qres"u8)) return Sig(QtResource);
        if (IsCipLzma(head)) return Sig(CipLzma);

        string name = fileName ?? "";
        string ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext == ".spr" && head.Length >= 8)
        {
            uint signature = BinaryPrimitives.ReadUInt32LittleEndian(head);
            return new(LegacySpr, KnownSprSignatures.ContainsKey(signature) ? FormatEvidence.Signature : FormatEvidence.Name,
                KnownSprSignatures.TryGetValue(signature, out var v) ? L.F("FileSignatures.Signature0x0X8Client1", signature, v) : L.F("FileSignatures.Signature0x0X8NotA", signature));
        }
        if (ext == ".dat" && head.Length >= 12 && name.StartsWith("Tibia", StringComparison.OrdinalIgnoreCase))
        {
            uint signature = BinaryPrimitives.ReadUInt32LittleEndian(head);
            return new(LegacyDat, KnownDatSignatures.ContainsKey(signature) ? FormatEvidence.Signature : FormatEvidence.Name,
                KnownDatSignatures.TryGetValue(signature, out var v) ? L.F("FileSignatures.Signature0x0X8Client1", signature, v) : L.F("FileSignatures.Signature0x0X8NotA", signature));
        }

        // Text formats have no signature: decide by content, then by extension.
        if (LooksLikeText(head, out var text))
        {
            string t = text.TrimStart((char)0xFEFF, ' ', '\t', '\r', '\n');
            if (t.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || (t.StartsWith("<?xml", StringComparison.Ordinal) && t.Contains("<svg", StringComparison.OrdinalIgnoreCase)))
                return new(Svg, FormatEvidence.Signature, L.T("FileSignatures.StartsLikeAnSVGDrawing"));
            if (t.StartsWith('{') || t.StartsWith('['))
                return new(Json, FormatEvidence.Signature, L.T("FileSignatures.TextThatStartsLikeJSONNot"));
            if (t.StartsWith('<'))
                return new(Xml, FormatEvidence.Signature, L.T("FileSignatures.TextThatStartsLikeXMLNot"));
            var byExt = All.FirstOrDefault(f => f.Category == "Text" && f.Extensions.Contains(ext));
            return new(byExt ?? Text, FormatEvidence.Signature, L.T("FileSignatures.ReadableText"));
        }
        var byName = All.FirstOrDefault(f => f.Extensions.Contains(ext));
        return byName != null
            ? new(byName, FormatEvidence.Name, L.T("FileSignatures.NoKnownSignatureWasFoundOnly"))
            : new(null, FormatEvidence.Signature, L.T("FileSignatures.UnknownBinaryFormat"));

        static FormatIdentification Sig(FileFormat f) => new(f, FormatEvidence.Signature, L.T("FileSignatures.RecognizedByItsSignatureBytes"));
    }

    /// <summary>The Tibia LZMA container: zero padding, then the bytes 70 0A (see <c>CipLzma</c>).</summary>
    private static bool IsCipLzma(ReadOnlySpan<byte> head)
    {
        int i = 0;
        while (i < head.Length && i < 64 && head[i] == 0) i++;
        return i > 0 && i + 1 < head.Length && head[i] == 0x70 && head[i + 1] == 0x0A;
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) => data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>UTF-8 (or ASCII) without control characters other than tab, CR and LF.</summary>
    private static bool LooksLikeText(ReadOnlySpan<byte> head, out string text)
    {
        text = "";
        var span = head;
        // A cut multi-byte character at the end of the sample is not an error.
        for (int cut = 0; cut < 4 && span.Length > 0; cut++)
        {
            try
            {
                text = StrictUtf8.GetString(span);
                break;
            }
            catch (DecoderFallbackException)
            {
                if (cut == 3) return false;
                span = span[..^1];
            }
        }
        if (text.Length == 0) return false;
        foreach (char c in text)
            if (c < 0x20 && c is not ('\t' or '\r' or '\n' or '\f')) return false;
        return true;
    }
}
