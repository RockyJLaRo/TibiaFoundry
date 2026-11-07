using System.Text;
using System.Text.Json;
using TibiaFoundry.Core.Assets;

namespace TibiaFoundry.Core.Editing;

/// <summary>One entry of catalog-content.json: its properties in their original order (name → raw JSON value).</summary>
public sealed class CatalogEntry
{
    public List<KeyValuePair<string, string>> Properties { get; } = [];

    /// <summary>The entry's text exactly as read (null for new or changed entries, which are formatted like the client's).</summary>
    public string? OriginalText { get; set; }

    /// <summary>The text between this entry and the next one as read (null when there was no next entry).</summary>
    public string? SeparatorAfter { get; set; }

    public string? Get(string name)
    {
        foreach (var p in Properties)
            if (p.Key == name) return p.Value.Length >= 2 && p.Value[0] == '"' ? JsonSerializer.Deserialize<string>(p.Value) : p.Value;
        return null;
    }

    public void Set(string name, string rawJson)
    {
        OriginalText = null;
        for (int i = 0; i < Properties.Count; i++)
        {
            if (Properties[i].Key != name) continue;
            Properties[i] = new(name, rawJson);
            return;
        }
        Properties.Add(new(name, rawJson));
    }

    public void SetString(string name, string value) => Set(name, JsonSerializer.Serialize(value));

    public string? Type => Get("type");
    public string? File => Get("file");

    public CatalogEntry Clone()
    {
        var c = new CatalogEntry { OriginalText = OriginalText, SeparatorAfter = SeparatorAfter };
        c.Properties.AddRange(Properties);
        return c;
    }

    /// <summary>A sprite sheet entry in the client's field order.</summary>
    public static CatalogEntry Sprite(string file, SpriteSheetType type, int first, int last)
    {
        var e = new CatalogEntry();
        e.SetString("type", "sprite");
        e.SetString("file", file);
        e.Set("spritetype", ((int)type).ToString(System.Globalization.CultureInfo.InvariantCulture));
        e.Set("firstspriteid", first.ToString(System.Globalization.CultureInfo.InvariantCulture));
        e.Set("lastspriteid", last.ToString(System.Globalization.CultureInfo.InvariantCulture));
        e.Set("area", "0");
        return e;
    }
}

/// <summary>catalog-content.json as read: the entries and the text around them.</summary>
public sealed class CatalogDocument
{
    public string Prefix { get; set; } = "[\r\n";
    public List<CatalogEntry> Entries { get; } = [];
    public string Suffix { get; set; } = "\r\n]\r\n";

    public CatalogDocument Clone()
    {
        var c = new CatalogDocument { Prefix = Prefix, Suffix = Suffix };
        c.Entries.AddRange(Entries.Select(e => e.Clone()));
        return c;
    }
}

/// <summary>
/// Reads and writes catalog-content.json. Entries the editor does not change keep their original
/// text, and the text between entries is kept too, so an unchanged catalog is reproduced byte for
/// byte (verified in the tests) even where the client's own formatting varies. New and changed
/// entries use the client's usual layout (two-space indent, no space after the colon).
/// </summary>
public static class CatalogWriter
{
    public static CatalogDocument Read(byte[] json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("catalog-content.json is not a list of entries.");
        // Positions of the entries in the original text, to keep everything between them.
        var spans = new List<(int Start, int End)>();
        var reader = new Utf8JsonReader(json);
        int start = -1;
        while (reader.Read())
        {
            if (reader.CurrentDepth != 1) continue;
            if (reader.TokenType == JsonTokenType.StartObject) start = (int)reader.TokenStartIndex;
            else if (reader.TokenType == JsonTokenType.EndObject) spans.Add((start, (int)reader.TokenStartIndex + 1));
        }
        var result = new CatalogDocument();
        int index = 0;
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("catalog-content.json contains an entry that is not an object.");
            var entry = new CatalogEntry { OriginalText = element.GetRawText() };
            foreach (var p in element.EnumerateObject()) entry.Properties.Add(new(p.Name, p.Value.GetRawText()));
            if (index + 1 < spans.Count) entry.SeparatorAfter = Encoding.UTF8.GetString(json, spans[index].End, spans[index + 1].Start - spans[index].End);
            result.Entries.Add(entry);
            index++;
        }
        if (spans.Count > 0)
        {
            result.Prefix = Encoding.UTF8.GetString(json, 0, spans[0].Start);
            result.Suffix = Encoding.UTF8.GetString(json, spans[^1].End, json.Length - spans[^1].End);
        }
        return result;
    }

    public static byte[] Write(CatalogDocument document)
    {
        var entries = document.Entries;
        var sb = new StringBuilder(entries.Count * 200);
        sb.Append(document.Prefix);
        for (int i = 0; i < entries.Count; i++)
        {
            sb.Append(entries[i].OriginalText ?? Format(entries[i]));
            sb.Append(i < entries.Count - 1 ? entries[i].SeparatorAfter ?? ", \r\n" : document.Suffix);
        }
        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    private static string Format(CatalogEntry entry)
    {
        var sb = new StringBuilder("{\r\n");
        var props = entry.Properties;
        for (int p = 0; p < props.Count; p++)
        {
            sb.Append("  ").Append(JsonSerializer.Serialize(props[p].Key)).Append(':').Append(props[p].Value);
            sb.Append(p < props.Count - 1 ? ",\r\n" : "\r\n");
        }
        return sb.Append('}').ToString();
    }
}
