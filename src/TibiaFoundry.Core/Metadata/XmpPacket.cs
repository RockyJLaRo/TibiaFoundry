using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace TibiaFoundry.Core.Metadata;

/// <summary>
/// XMP packet (Adobe XMP / ISO 16684-1) carrying the attribution as structured fields:
/// <c>dc:creator</c>, <c>dc:description</c>, <c>dc:rights</c>, <c>xmp:CreatorTool</c>,
/// <c>xmp:CreateDate</c>, <c>xmpRights:WebStatement</c>, <c>photoshop:Credit</c>, and Tibia Foundry
/// fields (<c>tf:Attribution</c>, <c>tf:Creator</c>, <c>tf:ExportedBy</c>, <c>tf:Application</c>,
/// <c>tf:Website</c>, <c>tf:History</c>). Embedded in PNG (iTXt) and GIF (application extension).
/// </summary>
public static class XmpPacket
{
    public const string TibiaFoundryNamespace = "urn:tibiafoundry:attribution:1.0/";
    /// <summary>Namespace written by version 3.0; read so its files keep their attribution.</summary>
    public const string LegacyNamespace = "urn:tibiaforge:attribution:1.0/";

    private static readonly XNamespace X = "adobe:ns:meta/";
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Xmp = "http://ns.adobe.com/xap/1.0/";
    private static readonly XNamespace XmpRights = "http://ns.adobe.com/xap/1.0/rights/";
    private static readonly XNamespace Photoshop = "http://ns.adobe.com/photoshop/1.0/";
    private static readonly XNamespace Tf = TibiaFoundryNamespace;
    private static readonly XNamespace TfLegacy = LegacyNamespace;

    /// <summary>Serializes the attribution as an XMP packet (UTF-8 text without NUL bytes).</summary>
    public static string Build(AttributionInfo info)
    {
        string date = info.Date.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        var description = new XElement(Rdf + "Description",
            new XAttribute(Rdf + "about", ""),
            new XAttribute(XNamespace.Xmlns + "dc", Dc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xmp", Xmp.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xmpRights", XmpRights.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "photoshop", Photoshop.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "tf", Tf.NamespaceName),
            Seq(Dc + "creator", info.Creators),
            Alt(Dc + "description", info.Text),
            string.IsNullOrWhiteSpace(info.Copyright) ? null : Alt(Dc + "rights", info.Copyright),
            new XElement(Xmp + "CreatorTool", info.Software),
            new XElement(Xmp + "CreateDate", date),
            new XElement(Xmp + "MetadataDate", date),
            new XElement(XmpRights + "WebStatement", info.Website),
            new XElement(Photoshop + "Credit", info.Text),
            new XElement(Tf + "Attribution", info.Text),
            new XElement(Tf + "Creator", info.CreatorText),
            new XElement(Tf + "ExportedBy", info.ExportedBy),
            new XElement(Tf + "Application", info.Application),
            new XElement(Tf + "Website", info.Website),
            info.History.Count > 0 ? Seq(Tf + "History", info.History) : null);
        var root = new XElement(X + "xmpmeta",
            new XAttribute(XNamespace.Xmlns + "x", X.NamespaceName),
            new XElement(Rdf + "RDF", new XAttribute(XNamespace.Xmlns + "rdf", Rdf.NamespaceName), description));
        string body;
        try
        {
            body = root.ToString();
        }
        catch (ArgumentException ex)
        {
            throw new AttributionException("The attribution contains characters that XMP cannot store.", ex);
        }
        // The packet wrapper lets XMP-aware tools find and update the packet in place.
        return $"<?xpacket begin=\"{(char)0xFEFF}\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n{body}\n<?xpacket end=\"w\"?>";
    }

    /// <summary>Reads the attribution fields from an XMP packet; null for malformed XML.</summary>
    public static FoundAttribution? Parse(string packet)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 4_000_000,
                IgnoreComments = true,
            };
            using var reader = XmlReader.Create(new StringReader(packet), settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        var descriptions = doc.Descendants(Rdf + "Description").ToList();
        string? Simple(XName name) =>
            descriptions.Select(d => (string?)d.Attribute(name) ?? d.Element(name)?.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
        List<string> Items(XName name) =>
            descriptions.Select(d => d.Element(name)).Where(e => e != null)
                .SelectMany(e => e!.Descendants(Rdf + "li").Select(li => li.Value.Trim()))
                .Where(v => v.Length > 0).ToList();
        string? AltValue(XName name)
        {
            var items = descriptions.Select(d => d.Element(name)).Where(e => e != null).SelectMany(e => e!.Descendants(Rdf + "li")).ToList();
            var preferred = items.FirstOrDefault(li => (string?)li.Attribute(XNamespace.Xml + "lang") == "x-default") ?? items.FirstOrDefault();
            return preferred?.Value.Trim() is { Length: > 0 } v ? v : null;
        }

        // Own fields in the current namespace first, then in the namespace of version 3.0.
        string? Own(string local) => Simple(Tf + local) ?? Simple(TfLegacy + local);
        var creators = Items(Dc + "creator");
        if (creators.Count == 0 && Own("Creator") is { } c)
            creators = c.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var history = Items(Tf + "History");
        if (history.Count == 0) history = Items(TfLegacy + "History");
        var found = new FoundAttribution(
            creators,
            Own("Attribution") ?? AltValue(Dc + "description") ?? Simple(Photoshop + "Credit"),
            AltValue(Dc + "rights"),
            Simple(Xmp + "CreatorTool"),
            history);
        return found.IsEmpty ? null : found;
    }

    private static XElement Seq(XName name, IEnumerable<string> items) =>
        new(name, new XElement(Rdf + "Seq", items.Select(i => new XElement(Rdf + "li", i))));

    private static XElement Alt(XName name, string value) =>
        new(name, new XElement(Rdf + "Alt", new XElement(Rdf + "li", new XAttribute(XNamespace.Xml + "lang", "x-default"), value)));
}
