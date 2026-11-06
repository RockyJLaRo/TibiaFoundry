using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using TibiaFoundry.Core.Common;

namespace TibiaFoundry.Core.FileOps;

/// <summary>
/// Writes <see cref="ReportTable"/>s as an Excel workbook (.xlsx, Office Open XML) without any
/// library: one worksheet per table, a bold and frozen header row with filters, column widths from
/// the content, whole numbers as numbers and everything else as text (inline strings, so nothing in
/// a cell is ever run as a formula). An optional first sheet "About" holds the header lines.
/// </summary>
public static class XlsxWriter
{
    private const string SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Longest text Excel keeps in a cell.</summary>
    public const int MaxCellLength = 32_767;

    /// <param name="about">Lines for a first sheet named "About" (what was exported, from where); none when null.</param>
    public static void Save(string path, IReadOnlyList<(string Name, ReportTable Table)> sheets, string? about = null) =>
        AtomicFile.Write(path, temp => Write(temp, sheets, about));

    public static void Write(string path, IReadOnlyList<(string Name, ReportTable Table)> sheets, string? about = null)
    {
        var all = new List<(string Name, ReportTable Table)>();
        if (about != null)
        {
            var info = new ReportTable("About", ["About this file"]);
            foreach (string line in about.Split('\n')) info.Add(line.TrimEnd('\r'));
            all.Add(("About", info));
        }
        all.AddRange(sheets);
        var names = UniqueSheetNames(all.Select(s => s.Name));

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        Part(zip, "[Content_Types].xml", w =>
        {
            w.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
            Default(w, "rels", "application/vnd.openxmlformats-package.relationships+xml");
            Default(w, "xml", "application/xml");
            Override(w, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
            Override(w, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
            Override(w, "/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml");
            for (int i = 1; i <= all.Count; i++) Override(w, $"/xl/worksheets/sheet{i}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
            w.WriteEndElement();
        });
        Part(zip, "_rels/.rels", w =>
        {
            w.WriteStartElement("Relationships", PackageRelNs);
            Relationship(w, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");
            Relationship(w, "rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml");
            w.WriteEndElement();
        });
        Part(zip, "docProps/core.xml", w =>
        {
            const string cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
            const string dc = "http://purl.org/dc/elements/1.1/";
            const string dcterms = "http://purl.org/dc/terms/";
            const string xsi = "http://www.w3.org/2001/XMLSchema-instance";
            w.WriteStartElement("cp", "coreProperties", cp);
            w.WriteAttributeString("xmlns", "dc", null, dc);
            w.WriteAttributeString("xmlns", "dcterms", null, dcterms);
            w.WriteAttributeString("xmlns", "xsi", null, xsi);
            w.WriteElementString("dc", "title", dc, Clean(sheets.Count > 0 ? sheets[0].Table.Title : "Tibia Foundry export"));
            w.WriteElementString("dc", "creator", dc, AppIdentity.NameAndVersion);
            w.WriteStartElement("dcterms", "created", dcterms);
            w.WriteAttributeString("xsi", "type", xsi, "dcterms:W3CDTF");
            w.WriteString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            w.WriteEndElement();
            w.WriteEndElement();
        });
        Part(zip, "xl/workbook.xml", w =>
        {
            w.WriteStartElement("workbook", SheetNs);
            w.WriteAttributeString("xmlns", "r", null, RelNs);
            w.WriteStartElement("sheets", SheetNs);
            for (int i = 0; i < all.Count; i++)
            {
                w.WriteStartElement("sheet", SheetNs);
                w.WriteAttributeString("name", names[i]);
                w.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("id", RelNs, $"rId{i + 1}");
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        });
        Part(zip, "xl/_rels/workbook.xml.rels", w =>
        {
            w.WriteStartElement("Relationships", PackageRelNs);
            for (int i = 0; i < all.Count; i++)
                Relationship(w, $"rId{i + 1}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", $"worksheets/sheet{i + 1}.xml");
            Relationship(w, $"rId{all.Count + 1}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml");
            w.WriteEndElement();
        });
        Part(zip, "xl/styles.xml", WriteStyles);
        for (int i = 0; i < all.Count; i++)
        {
            var table = all[i].Table;
            bool isAbout = about != null && i == 0;
            Part(zip, $"xl/worksheets/sheet{i + 1}.xml", w => WriteSheet(w, table, header: !isAbout));
        }
    }

    private static void WriteSheet(XmlWriter w, ReportTable table, bool header)
    {
        int columns = table.Columns.Count;
        var rows = new List<IReadOnlyList<string>>();
        if (header) rows.Add(table.Columns);
        rows.AddRange(table.Rows);
        w.WriteStartElement("worksheet", SheetNs);
        if (header && rows.Count > 1)
        {
            w.WriteStartElement("sheetViews", SheetNs);
            w.WriteStartElement("sheetView", SheetNs);
            w.WriteAttributeString("workbookViewId", "0");
            w.WriteStartElement("pane", SheetNs);
            w.WriteAttributeString("ySplit", "1");
            w.WriteAttributeString("topLeftCell", "A2");
            w.WriteAttributeString("activePane", "bottomLeft");
            w.WriteAttributeString("state", "frozen");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }
        w.WriteStartElement("cols", SheetNs);
        for (int c = 0; c < columns; c++)
        {
            int longest = rows.Take(2000).Select(r => c < r.Count ? r[c].Length : 0).DefaultIfEmpty(0).Max();
            double width = Math.Clamp(longest + 2, 6, header ? 60 : 120);
            w.WriteStartElement("col", SheetNs);
            string n = (c + 1).ToString(CultureInfo.InvariantCulture);
            w.WriteAttributeString("min", n);
            w.WriteAttributeString("max", n);
            w.WriteAttributeString("width", width.ToString("0.#", CultureInfo.InvariantCulture));
            w.WriteAttributeString("customWidth", "1");
            w.WriteEndElement();
        }
        w.WriteEndElement();
        w.WriteStartElement("sheetData", SheetNs);
        for (int r = 0; r < rows.Count; r++)
        {
            w.WriteStartElement("row", SheetNs);
            w.WriteAttributeString("r", (r + 1).ToString(CultureInfo.InvariantCulture));
            for (int c = 0; c < columns; c++)
            {
                string value = c < rows[r].Count ? rows[r][c] ?? "" : "";
                if (value.Length == 0) continue;
                w.WriteStartElement("c", SheetNs);
                w.WriteAttributeString("r", CellName(c, r));
                bool bold = header && r == 0;
                if (bold) w.WriteAttributeString("s", "1");
                if (!bold && IsWholeNumber(value))
                {
                    w.WriteElementString("v", SheetNs, value);
                }
                else
                {
                    w.WriteAttributeString("t", "inlineStr");
                    w.WriteStartElement("is", SheetNs);
                    w.WriteStartElement("t", SheetNs);
                    string text = Clean(value);
                    if (text.Length > MaxCellLength) text = text[..(MaxCellLength - 1)] + "…";
                    if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]))) w.WriteAttributeString("xml", "space", null, "preserve");
                    w.WriteString(text);
                    w.WriteEndElement();
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        w.WriteEndElement();
        if (header && rows.Count > 1)
        {
            w.WriteStartElement("autoFilter", SheetNs);
            w.WriteAttributeString("ref", $"A1:{CellName(columns - 1, rows.Count - 1)}");
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    private static void WriteStyles(XmlWriter w)
    {
        w.WriteStartElement("styleSheet", SheetNs);
        w.WriteStartElement("fonts", SheetNs);
        w.WriteAttributeString("count", "2");
        Font(w, bold: false);
        Font(w, bold: true);
        w.WriteEndElement();
        w.WriteStartElement("fills", SheetNs);
        w.WriteAttributeString("count", "2");
        foreach (string pattern in new[] { "none", "gray125" })
        {
            w.WriteStartElement("fill", SheetNs);
            w.WriteStartElement("patternFill", SheetNs);
            w.WriteAttributeString("patternType", pattern);
            w.WriteEndElement();
            w.WriteEndElement();
        }
        w.WriteEndElement();
        w.WriteStartElement("borders", SheetNs);
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("border", SheetNs);
        foreach (string side in new[] { "left", "right", "top", "bottom", "diagonal" }) w.WriteElementString(side, SheetNs, "");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteStartElement("cellStyleXfs", SheetNs);
        w.WriteAttributeString("count", "1");
        Xf(w, font: 0, withXfId: false);
        w.WriteEndElement();
        w.WriteStartElement("cellXfs", SheetNs);
        w.WriteAttributeString("count", "2");
        Xf(w, font: 0, withXfId: true);
        Xf(w, font: 1, withXfId: true);
        w.WriteEndElement();
        w.WriteStartElement("cellStyles", SheetNs);
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("cellStyle", SheetNs);
        w.WriteAttributeString("name", "Normal");
        w.WriteAttributeString("xfId", "0");
        w.WriteAttributeString("builtinId", "0");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();

        static void Font(XmlWriter w, bool bold)
        {
            w.WriteStartElement("font", SheetNs);
            if (bold) w.WriteElementString("b", SheetNs, "");
            w.WriteStartElement("sz", SheetNs);
            w.WriteAttributeString("val", "11");
            w.WriteEndElement();
            w.WriteStartElement("name", SheetNs);
            w.WriteAttributeString("val", "Calibri");
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void Xf(XmlWriter w, int font, bool withXfId)
        {
            w.WriteStartElement("xf", SheetNs);
            w.WriteAttributeString("numFmtId", "0");
            w.WriteAttributeString("fontId", font.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fillId", "0");
            w.WriteAttributeString("borderId", "0");
            if (withXfId) w.WriteAttributeString("xfId", "0");
            if (font > 0) w.WriteAttributeString("applyFont", "1");
            w.WriteEndElement();
        }
    }

    private static void Part(ZipArchive zip, string name, Action<XmlWriter> write)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
        writer.WriteStartDocument(standalone: true);
        write(writer);
        writer.WriteEndDocument();
    }

    private static void Default(XmlWriter w, string extension, string type)
    {
        w.WriteStartElement("Default", "http://schemas.openxmlformats.org/package/2006/content-types");
        w.WriteAttributeString("Extension", extension);
        w.WriteAttributeString("ContentType", type);
        w.WriteEndElement();
    }

    private static void Override(XmlWriter w, string part, string type)
    {
        w.WriteStartElement("Override", "http://schemas.openxmlformats.org/package/2006/content-types");
        w.WriteAttributeString("PartName", part);
        w.WriteAttributeString("ContentType", type);
        w.WriteEndElement();
    }

    private static void Relationship(XmlWriter w, string id, string type, string target)
    {
        w.WriteStartElement("Relationship", PackageRelNs);
        w.WriteAttributeString("Id", id);
        w.WriteAttributeString("Type", type);
        w.WriteAttributeString("Target", target);
        w.WriteEndElement();
    }

    /// <summary>"A1"-style cell name (column and row zero-based).</summary>
    public static string CellName(int column, int row)
    {
        var sb = new StringBuilder();
        for (int c = column + 1; c > 0; c = (c - 1) / 26) sb.Insert(0, (char)('A' + (c - 1) % 26));
        return sb.Append((row + 1).ToString(CultureInfo.InvariantCulture)).ToString();
    }

    /// <summary>Whole numbers (up to 15 digits, no leading zero) are written as numbers; everything else as text.</summary>
    private static bool IsWholeNumber(string value) =>
        value.Length is > 0 and <= 15 && value.All(char.IsAsciiDigit) && (value.Length == 1 || value[0] != '0');

    /// <summary>Removes characters XML cannot hold (control characters other than tab, line feed and carriage return).</summary>
    private static string Clean(string text)
    {
        if (text.All(c => XmlConvert.IsXmlChar(c) || char.IsSurrogate(c))) return text;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { sb.Append(c).Append(text[++i]); continue; }
            if (XmlConvert.IsXmlChar(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Sheet names: at most 31 characters, none of []:*?/\, unique (case-insensitive).</summary>
    private static List<string> UniqueSheetNames(IEnumerable<string> wanted)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (string w in wanted)
        {
            string name = new string(w.Where(c => c is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray()).Trim('\'').Trim();
            if (name.Length == 0) name = "Sheet";
            if (name.Length > 31) name = name[..31];
            string candidate = name;
            for (int i = 2; !used.Add(candidate); i++) candidate = (name.Length > 27 ? name[..27] : name) + $" ({i})";
            result.Add(candidate);
        }
        return result;
    }
}
