using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TibiaFoundry.Core.FileOps;
using F = TibiaFoundry.Core.FileMetadata.MetadataFields;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.FileMetadata.Providers;

/// <summary>Shared XML helpers: safe reading (no DTDs, no external entities) and writing back without a byte-order mark change.</summary>
internal static class SafeXml
{
    public static XDocument Load(Stream stream)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = false };
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    public static void Save(XDocument doc, Stream stream)
    {
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, OmitXmlDeclaration = doc.Declaration == null };
        using var writer = XmlWriter.Create(stream, settings);
        doc.Save(writer);
    }

    /// <summary>Replaces an entry's content in an archive opened for update (the other entries are copied unchanged).</summary>
    public static void Replace(ZipArchiveEntry entry, XDocument doc)
    {
        using var stream = entry.Open();
        stream.SetLength(0);
        Save(doc, stream);
    }

    public static bool IsZip(string path)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return s.ReadByte() == 'P' && s.ReadByte() == 'K';
    }
}

/// <summary>
/// Word, Excel and PowerPoint files (Office Open XML: .docx, .xlsx, .pptx and their macro and
/// template forms): the document properties in docProps/core.xml (editable) and docProps/app.xml
/// (read only). Only core.xml is rewritten; every other part of the package is copied unchanged.
/// </summary>
public sealed class OpenXmlMetadataProvider : IMetadataProvider
{
    private static readonly XNamespace Cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Dcterms = "http://purl.org/dc/terms/";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace Ep = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
    private const string CoreRelationship = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    private const string AppRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";

    private static readonly (string Key, XNamespace Ns, string Element)[] CoreFields =
    [
        ("title", Dc, "title"), ("subject", Dc, "subject"), ("authors", Dc, "creator"), ("keywords", Cp, "keywords"), ("description", Dc, "description"),
        ("category", Cp, "category"), ("lastModifiedBy", Cp, "lastModifiedBy"), ("contentStatus", Cp, "contentStatus"), ("language", Dc, "language"),
        ("revision", Cp, "revision"), ("identifier", Dc, "identifier"), ("created", Dcterms, "created"), ("modified", Dcterms, "modified"),
    ];

    public string Id => "openxml";
    public string Name => L.T("DocumentProviders.WordExcelAndPowerPointOfficeOpen");
    public IReadOnlyList<string> Extensions { get; } = [".docx", ".docm", ".dotx", ".xlsx", ".xlsm", ".xltx", ".pptx", ".pptm", ".potx"];
    public MetadataCapabilities Capabilities => MetadataCapabilities.Editable;
    public string Limitations => L.T("DocumentProviders.DocumentPropertiesOnlyTitleAuthorsKeywords");

    public bool CanHandle(string path, FormatIdentification identification) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()) && SafeXml.IsZip(path);

    private static string? PartOf(ZipArchive zip, string relationshipType)
    {
        var rels = zip.GetEntry("_rels/.rels");
        if (rels == null) return null;
        using var s = rels.Open();
        var doc = SafeXml.Load(s);
        string? target = doc.Root?.Elements().FirstOrDefault(e => (string?)e.Attribute("Type") == relationshipType)?.Attribute("Target")?.Value;
        return target?.TrimStart('/');
    }

    public MetadataReadResult Read(string path, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(path);
        var warnings = new List<string>();
        var fields = new List<MetadataField>();
        string? corePath = PartOf(zip, CoreRelationship);
        var core = corePath == null ? null : zip.GetEntry(corePath);
        XElement? root = null;
        if (core != null)
            using (var s = core.Open()) root = SafeXml.Load(s).Root;
        else warnings.Add(L.T("DocumentProviders.ThisDocumentHasNoDocumentProperties"));
        bool editable = root != null;
        foreach (var (key, ns, element) in CoreFields)
        {
            var definition = F.All[key];
            string? raw = root?.Element(ns + element)?.Value;
            string? value = definition.Kind == MetadataValueKind.Date ? W3cToLocal(raw) : definition.Kind == MetadataValueKind.List ? MetadataValues.JoinList(MetadataValues.SplitList(raw?.Replace(',', ';'))) : Clean(raw);
            fields.Add(new(definition, value, editable && key != "identifier", editable));
        }
        string? appPath = PartOf(zip, AppRelationship);
        if (appPath != null && zip.GetEntry(appPath) is { } app)
        {
            using var s = app.Open();
            var a = SafeXml.Load(s).Root;
            fields.Add(new(F.Software, Clean(string.Join(" ", new[] { a?.Element(Ep + "Application")?.Value, a?.Element(Ep + "AppVersion")?.Value }.Where(x => !string.IsNullOrWhiteSpace(x)))), false));
            fields.Add(new(F.Company, Clean(a?.Element(Ep + "Company")?.Value), false));
            fields.Add(new(F.Pages, Clean(a?.Element(Ep + "Pages")?.Value ?? a?.Element(Ep + "Slides")?.Value), false));
            fields.Add(new(F.Words, Clean(a?.Element(Ep + "Words")?.Value), false));
        }
        fields.Add(new(F.Info("parts", L.T("DocumentProviders.PackageParts"), F.Technical), zip.Entries.Count.ToString(CultureInfo.InvariantCulture), false));
        string kind = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".docx" or ".docm" or ".dotx" => L.T("DocumentProviders.WordDocument"),
            ".xlsx" or ".xlsm" or ".xltx" => L.T("DocumentProviders.ExcelWorkbook"),
            _ => L.T("DocumentProviders.PowerPointPresentation"),
        };
        return new MetadataReadResult(kind, fields, warnings);
    }

    public IReadOnlyList<string> WriteWarnings(string path, IReadOnlyList<MetadataEdit> edits) =>
        [L.T("DocumentProviders.OnlyTheDocumentPropertiesPartIs")];

    public void Write(string path, IReadOnlyList<MetadataEdit> edits, CancellationToken ct)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        string corePath = PartOf(zip, CoreRelationship) ?? throw new InvalidDataException("The document has no document-properties part.");
        var entry = zip.GetEntry(corePath) ?? throw new InvalidDataException("The document-properties part is missing.");
        XDocument doc;
        using (var s = entry.Open()) doc = SafeXml.Load(s);
        var root = doc.Root ?? throw new InvalidDataException("The document-properties part is empty.");
        foreach (var e in edits)
        {
            var (_, ns, name) = CoreFields.FirstOrDefault(c => c.Key == e.Key);
            if (name == null) throw new NotSupportedException($"\"{e.Key}\" is not a document property.");
            var element = root.Element(ns + name);
            if (e.Value == null) { element?.Remove(); continue; }
            string value = F.All[e.Key].Kind switch
            {
                MetadataValueKind.Date => LocalToW3c(e.Value),
                MetadataValueKind.List => string.Join("; ", MetadataValues.SplitList(e.Value)),
                _ => e.Value,
            };
            if (element == null)
            {
                element = new XElement(ns + name);
                if (ns == Dcterms) element.SetAttributeValue(Xsi + "type", "dcterms:W3CDTF");
                root.Add(element);
            }
            element.Value = value;
        }
        if (root.GetNamespaceOfPrefix("xsi") == null && root.Descendants().Any(d => d.Attribute(Xsi + "type") != null)) root.SetAttributeValue(XNamespace.Xmlns + "xsi", Xsi.NamespaceName);
        if (root.GetNamespaceOfPrefix("dcterms") == null) root.SetAttributeValue(XNamespace.Xmlns + "dcterms", Dcterms.NamespaceName);
        SafeXml.Replace(entry, doc);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? W3cToLocal(string? raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? MetadataValues.FormatDate(d.LocalDateTime) : Clean(raw);

    private static string LocalToW3c(string value) =>
        MetadataValues.TryParseDate(value, out var local) ? local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : throw new FormatException($"\"{value}\" is not a date.");
}

/// <summary>
/// EPUB books: the Dublin Core metadata of the package document (title, creators, subjects,
/// description, publisher, language, date, rights). Only the package document is rewritten; the
/// "mimetype" entry stays first and uncompressed, as the format requires.
/// </summary>
public sealed class EpubMetadataProvider : IMetadataProvider
{
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";

    private static readonly (string Key, string Element)[] Map =
    [
        ("title", "title"), ("authors", "creator"), ("keywords", "subject"), ("description", "description"), ("publisher", "publisher"),
        ("language", "language"), ("date", "date"), ("copyright", "rights"), ("identifier", "identifier"),
    ];

    public string Id => "epub";
    public string Name => L.T("DocumentProviders.EPUBBooks");
    public IReadOnlyList<string> Extensions { get; } = [".epub"];
    public MetadataCapabilities Capabilities => MetadataCapabilities.Editable;
    public string Limitations => L.T("DocumentProviders.TheBookSDublinCoreMetadata");

    public bool CanHandle(string path, FormatIdentification identification) => Path.GetExtension(path).Equals(".epub", StringComparison.OrdinalIgnoreCase) && SafeXml.IsZip(path);

    private static string PackagePath(ZipArchive zip)
    {
        var container = zip.GetEntry("META-INF/container.xml") ?? throw new InvalidDataException("META-INF/container.xml is missing: not a valid EPUB.");
        using var s = container.Open();
        return SafeXml.Load(s).Descendants(Container + "rootfile").FirstOrDefault()?.Attribute("full-path")?.Value
               ?? throw new InvalidDataException("The EPUB names no package document.");
    }

    public MetadataReadResult Read(string path, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(path);
        var warnings = new List<string>();
        if (zip.GetEntry("META-INF/encryption.xml") != null) warnings.Add(L.T("DocumentProviders.PartsOfThisBookAreEncrypted"));
        var opfEntry = zip.GetEntry(PackagePath(zip)) ?? throw new InvalidDataException("The package document is missing.");
        XElement metadata;
        string version;
        using (var s = opfEntry.Open())
        {
            var doc = SafeXml.Load(s);
            metadata = doc.Root?.Element(Opf + "metadata") ?? throw new InvalidDataException("The package document has no metadata.");
            version = doc.Root?.Attribute("version")?.Value ?? "?";
        }
        var fields = new List<MetadataField>();
        foreach (var (key, element) in Map)
        {
            var definition = F.All[key];
            var values = metadata.Elements(Dc + element).Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList();
            string? value = definition.Kind == MetadataValueKind.List ? MetadataValues.JoinList(values) : values.FirstOrDefault();
            if (definition.Kind == MetadataValueKind.Date && value != null && !MetadataValues.TryParseDate(value, out _) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
                value = MetadataValues.FormatDate(d.LocalDateTime.Date == d.LocalDateTime ? d.Date : d.LocalDateTime);
            fields.Add(new(definition, value, key != "identifier", key != "identifier"));
        }
        fields.Add(new(F.Info("epubVersion", L.T("DocumentProviders.EPUBVersion"), F.Technical), version, false));
        fields.Add(new(F.Info("parts", L.T("DocumentProviders.PackageParts"), F.Technical), zip.Entries.Count.ToString(CultureInfo.InvariantCulture), false));
        return new MetadataReadResult(L.T("DocumentProviders.EPUBBook"), fields, warnings);
    }

    public IReadOnlyList<string> WriteWarnings(string path, IReadOnlyList<MetadataEdit> edits) => [L.T("DocumentProviders.OnlyThePackageDocumentIsRewritten")];

    public void Write(string path, IReadOnlyList<MetadataEdit> edits, CancellationToken ct)
    {
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry(PackagePath(zip)) ?? throw new InvalidDataException("The package document is missing.");
            XDocument doc;
            using (var s = entry.Open()) doc = SafeXml.Load(s);
            var metadata = doc.Root?.Element(Opf + "metadata") ?? throw new InvalidDataException("The package document has no metadata.");
            foreach (var e in edits)
            {
                string element = Map.FirstOrDefault(m => m.Key == e.Key).Element ?? throw new NotSupportedException($"\"{e.Key}\" is not EPUB metadata.");
                if (e.Key == "identifier") throw new NotSupportedException("The identifier is read only.");
                var existing = metadata.Elements(Dc + element).ToList();
                var values = F.All[e.Key].Kind == MetadataValueKind.List ? MetadataValues.SplitList(e.Value) : e.Value == null ? [] : [e.Value];
                // Reuse the existing elements (keeping their attributes and refinements); add or remove the rest.
                XElement? last = existing.LastOrDefault();
                for (int i = 0; i < values.Count; i++)
                {
                    if (i < existing.Count) { existing[i].Value = values[i]; continue; }
                    var added = new XElement(Dc + element, values[i]);
                    if (last != null) last.AddAfterSelf(added);
                    else metadata.Add(added);
                    last = added;
                }
                foreach (var extra in existing.Skip(values.Count))
                {
                    if (extra.Attribute("id")?.Value is { } id)
                        metadata.Elements(Opf + "meta").Where(m => (string?)m.Attribute("refines") == "#" + id).Remove();
                    extra.Remove();
                }
            }
            SafeXml.Replace(entry, doc);
        }
        // The format requires "mimetype" to stay the first entry, stored without compression.
        using var check = ZipFile.OpenRead(path);
        var first = check.Entries.FirstOrDefault();
        if (first != null && first.FullName == "mimetype" && first.CompressedLength != first.Length)
            throw new InvalidDataException("The mimetype entry would have been compressed; the book was not changed.");
    }
}

/// <summary>
/// PDF documents, read only: the document information (title, author, subject, keywords, creator,
/// producer, dates), the PDF version, the page count, encryption and whether XMP metadata is present.
/// </summary>
public sealed class PdfMetadataProvider : IMetadataProvider
{
    public string Id => "pdf";
    public string Name => L.T("DocumentProviders.PDFDocumentsReadOnly");
    public IReadOnlyList<string> Extensions { get; } = [".pdf"];
    public MetadataCapabilities Capabilities => MetadataCapabilities.ReadOnly;
    public string Limitations => L.T("DocumentProviders.ReadOnlyWritingPDFMetadataSafely");

    public bool CanHandle(string path, FormatIdentification identification) => identification.Format?.Id == "pdf" || Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public MetadataReadResult Read(string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (info.Length > 512L * 1024 * 1024) throw new InvalidDataException("PDF files over 512 MB are not read.");
        byte[] data = File.ReadAllBytes(path);
        string text = Encoding.Latin1.GetString(data);
        if (!text.StartsWith("%PDF-", StringComparison.Ordinal)) throw new InvalidDataException("No PDF header.");
        var warnings = new List<string>();
        var fields = new List<MetadataField>
        {
            new(F.Info("pdfVersion", L.T("DocumentProviders.PDFVersion"), F.Technical), text.Substring(5, Math.Min(3, text.Length - 5)).Trim(), false),
        };
        bool encrypted = Regex.IsMatch(text, @"/Encrypt\s+\d+\s+\d+\s+R");
        if (encrypted) warnings.Add(L.T("DocumentProviders.TheDocumentIsEncryptedItsText"));
        int pages = Regex.Matches(text, @"/Type\s*/Pages\b[^>]*?/Count\s+(\d+)").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
        pages = Math.Max(pages, Regex.Matches(text, @"/Count\s+(\d+)[^>]*?/Type\s*/Pages\b").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max());
        if (pages > 0) fields.Add(new(F.Pages, pages.ToString(CultureInfo.InvariantCulture), false));
        fields.Add(new(F.Info("pdfXmp", L.T("DocumentProviders.XMPMetadata"), F.Technical), text.Contains("<x:xmpmeta", StringComparison.Ordinal) ? L.T("Label.Present") : L.T("Scripts.None"), false));
        fields.Add(new(F.Info("pdfEncrypted", L.T("DocumentProviders.Encrypted"), F.Technical), encrypted ? L.T("Metadata.Yes") : L.T("Label.No"), false));

        // The newest trailer's /Info object (an incremental update appends a newer one).
        var infoRef = Regex.Matches(text, @"/Info\s+(\d+)\s+(\d+)\s+R").LastOrDefault();
        Dictionary<string, string> dict = [];
        if (infoRef != null)
        {
            var obj = Regex.Matches(text, $@"(?<![0-9]){infoRef.Groups[1].Value}\s+{infoRef.Groups[2].Value}\s+obj\s*<<(.*?)>>\s*endobj", RegexOptions.Singleline).LastOrDefault();
            if (obj != null) dict = ParseInfo(obj.Groups[1].Value);
            else warnings.Add(L.T("DocumentProviders.TheDocumentInformationIsStoredIn"));
        }
        else warnings.Add(L.T("DocumentProviders.TheDocumentHasNoDocumentInformation"));
        if (!encrypted)
        {
            fields.Add(new(F.Title, dict.GetValueOrDefault("Title"), false));
            fields.Add(new(F.Authors, MetadataValues.JoinList(MetadataValues.SplitList(dict.GetValueOrDefault("Author"))), false));
            fields.Add(new(F.Subject, dict.GetValueOrDefault("Subject"), false));
            fields.Add(new(F.Keywords, MetadataValues.JoinList(MetadataValues.SplitList(dict.GetValueOrDefault("Keywords")?.Replace(',', ';'))), false));
            fields.Add(new(F.Software, dict.GetValueOrDefault("Creator"), false));
            fields.Add(new(F.Info("producer", L.T("DocumentProviders.Producer"), F.Document), dict.GetValueOrDefault("Producer"), false));
            fields.Add(new(F.Created, PdfDate(dict.GetValueOrDefault("CreationDate")), false));
            fields.Add(new(F.Modified, PdfDate(dict.GetValueOrDefault("ModDate")), false));
        }
        return new MetadataReadResult(L.T("DocumentProviders.PDFDocument"), fields, warnings);
    }

    private static Dictionary<string, string> ParseInfo(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(body, @"/(\w+)\s*(\((?:\\.|[^\\)]|\((?:\\.|[^\\)])*\))*\)|<[0-9A-Fa-f\s]*>)", RegexOptions.Singleline))
        {
            string raw = m.Groups[2].Value;
            string? value = raw.StartsWith('(') ? Literal(raw[1..^1]) : Hex(raw[1..^1]);
            if (!string.IsNullOrWhiteSpace(value)) result[m.Groups[1].Value] = value.Trim();
        }
        return result;
    }

    /// <summary>A PDF literal string: escapes, octal codes, and UTF-16 when it starts with a byte-order mark.</summary>
    private static string Literal(string s)
    {
        var bytes = new List<byte>();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { bytes.Add((byte)c); continue; }
            char n = s[++i];
            switch (n)
            {
                case 'n': bytes.Add(10); break;
                case 'r': bytes.Add(13); break;
                case 't': bytes.Add(9); break;
                case 'b': bytes.Add(8); break;
                case 'f': bytes.Add(12); break;
                case '\r': if (i + 1 < s.Length && s[i + 1] == '\n') i++; break;
                case '\n': break;
                default:
                    if (n is >= '0' and <= '7')
                    {
                        int v = n - '0', k = 1;
                        while (k < 3 && i + 1 < s.Length && s[i + 1] is >= '0' and <= '7') { v = v * 8 + (s[++i] - '0'); k++; }
                        bytes.Add((byte)v);
                    }
                    else bytes.Add((byte)n);
                    break;
            }
        }
        return Decode([.. bytes]);
    }

    private static string? Hex(string s)
    {
        string h = Regex.Replace(s, @"\s", "");
        if (h.Length % 2 == 1) h += "0";
        try { return Decode(Convert.FromHexString(h)); }
        catch (FormatException) { return null; }
    }

    private static string Decode(byte[] b) =>
        b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF ? Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2)
        : b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? Encoding.UTF8.GetString(b, 3, b.Length - 3)
        : Encoding.Latin1.GetString(b);

    /// <summary>"D:20240131120000+01'00'" → local time.</summary>
    internal static string? PdfDate(string? raw)
    {
        if (raw == null) return null;
        var m = Regex.Match(raw, @"^(?:D:)?(\d{4})(\d{2})?(\d{2})?(\d{2})?(\d{2})?(\d{2})?([Zz+\-])?(\d{2})?'?(\d{2})?");
        if (!m.Success) return raw;
        int G(int i, int d) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : d;
        try
        {
            var offset = m.Groups[7].Value switch
            {
                "+" => new TimeSpan(G(8, 0), G(9, 0), 0),
                "-" => -new TimeSpan(G(8, 0), G(9, 0), 0),
                _ => TimeSpan.Zero,
            };
            var dto = new DateTimeOffset(G(1, 1), G(2, 1), G(3, 1), G(4, 0), G(5, 0), G(6, 0), offset);
            return MetadataValues.FormatDate(m.Groups[7].Success ? dto.LocalDateTime : dto.DateTime);
        }
        catch (ArgumentOutOfRangeException) { return raw; }
    }
}

/// <summary>SVG drawings: the &lt;title&gt; and &lt;desc&gt; of the drawing (editable), its size and view box (read only).</summary>
public sealed class SvgMetadataProvider : IMetadataProvider
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    public string Id => "svg";
    public string Name => L.T("DocumentProviders.SVGDrawings");
    public IReadOnlyList<string> Extensions { get; } = [".svg"];
    public MetadataCapabilities Capabilities => MetadataCapabilities.Editable;
    public string Limitations => L.T("DocumentProviders.TitleAndDescriptionOnlyCompressedSvgz");

    public bool CanHandle(string path, FormatIdentification identification) => Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase);

    private static XDocument Load(string path)
    {
        using var s = File.OpenRead(path);
        var doc = SafeXml.Load(s);
        if (doc.Root?.Name.LocalName != "svg") throw new InvalidDataException("The root element is not <svg>.");
        return doc;
    }

    public MetadataReadResult Read(string path, CancellationToken ct)
    {
        var doc = Load(path);
        var root = doc.Root!;
        var ns = root.Name.Namespace;
        return new MetadataReadResult(L.T("DocumentProviders.SVGDrawing"),
        [
            new(F.Title, root.Element(ns + "title")?.Value.Trim() is { Length: > 0 } t ? t : null, true, true),
            new(F.DescriptionText, root.Element(ns + "desc")?.Value.Trim() is { Length: > 0 } d ? d : null, true, true),
            new(F.Width, (string?)root.Attribute("width"), false),
            new(F.Height, (string?)root.Attribute("height"), false),
            new(F.Info("viewBox", L.T("DocumentProviders.ViewBox"), F.Image), (string?)root.Attribute("viewBox"), false),
        ]);
    }

    public IReadOnlyList<string> WriteWarnings(string path, IReadOnlyList<MetadataEdit> edits) => [L.T("DocumentProviders.TheSVGSXMLTextIs")];

    public void Write(string path, IReadOnlyList<MetadataEdit> edits, CancellationToken ct)
    {
        var doc = Load(path);
        var root = doc.Root!;
        var ns = root.Name.Namespace == XNamespace.None ? XNamespace.None : root.Name.Namespace;
        foreach (var e in edits)
        {
            string name = e.Key switch { "title" => "title", "description" => "desc", _ => throw new NotSupportedException($"\"{e.Key}\" cannot be written to an SVG.") };
            var element = root.Element(ns + name);
            if (e.Value == null) { element?.Remove(); continue; }
            if (element == null)
            {
                element = new XElement(ns + name);
                // <title> first, <desc> after it (where SVG viewers and screen readers look).
                if (name == "title" || root.Element(ns + "title") is not { } title) root.AddFirst(element);
                else title.AddAfterSelf(element);
            }
            element.Value = e.Value;
        }
        using var s = new FileStream(path, FileMode.Create, FileAccess.Write);
        SafeXml.Save(doc, s);
    }
}
