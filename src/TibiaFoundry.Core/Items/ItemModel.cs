using System.Text;
using System.Text.Json.Serialization;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Items;

/// <summary>
/// What an item identifier means. Identifiers of different types are never compared, matched or
/// converted into each other implicitly (only through an explicit <see cref="IdMapping"/>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IdType>))]
public enum IdType
{
    /// <summary>
    /// Official object type ID of the modern client: the ID of an object in appearances.dat, in the
    /// game protocol, in hotkeys (<c>useObject</c>) and in TibiaWiki's "itemid".
    /// </summary>
    TibiaObject,
    /// <summary>
    /// Item ID of a third-party items.otb mapping file (meaningful only with that file's version; mapped to
    /// client IDs by the file). The enum name is stored in catalog files and stays unchanged.
    /// </summary>
    OtbServer,
    /// <summary>Client ID stored in a third-party items.otb (the client version of that file).</summary>
    OtbClient,
    /// <summary>Object ID in a legacy Tibia.dat (renumbered between old client versions).</summary>
    LegacyDatClient,
    /// <summary>The meaning of the identifier could not be established.</summary>
    Unknown,
}

/// <summary>Where the records of a source come from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SourceKind>))]
public enum SourceKind
{
    /// <summary>The client's own data (appearances.dat): official CipSoft data.</summary>
    OfficialClient,
    /// <summary>Names shown by the official client and recorded while logged in (by the Item Discovery tool of 7.0–7.3, removed in 8.0; kept so such catalogs still load).</summary>
    ClientObservation,
    /// <summary>TibiaWiki (community data; article titles or infobox values).</summary>
    TibiaWiki,
    /// <summary>A third-party items.xml item list (the enum name is stored in catalog files and stays unchanged).</summary>
    OtItemsXml,
    /// <summary>A third-party items.otb item ID mapping (the enum name is stored in catalog files and stays unchanged).</summary>
    OtItemsOtb,
    /// <summary>A list of "[id] name" lines (for example from GetItemNames or LapisItemEditor).</summary>
    NameList,
    /// <summary>A CSV file.</summary>
    Csv,
    /// <summary>A JSON file (for example an export of another catalog).</summary>
    Json,
    /// <summary>Decisions the user made in the conflict review.</summary>
    UserDecision,
}

/// <summary>How sure a record is.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ObservationStatus>))]
public enum ObservationStatus
{
    /// <summary>Established from official data (the client's files, or a name observed in the official client with evidence).</summary>
    Verified,
    /// <summary>From a source that is not authoritative for this fact (community or third-party data, lists of unknown origin).</summary>
    Unverified,
    /// <summary>The identifier exists, but its name is not known (yet).</summary>
    Unresolved,
    /// <summary>A collection attempt failed (no tooltip, unreadable text…); kept so it is not mistaken for "untested".</summary>
    Failed,
}

/// <summary>How a record was obtained.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ObservationMethod>))]
public enum ObservationMethod
{
    /// <summary>Read from a file or a download.</summary>
    Import,
    /// <summary>Read from the tooltip of the official client by text recognition or accessibility information (Item Discovery, 7.0–7.3; removed in 8.0, kept for existing records).</summary>
    ClientTooltip,
    /// <summary>Typed or confirmed by the user while looking at the official client (Item Discovery, 7.0–7.3; removed in 8.0, kept for existing records).</summary>
    ClientConfirmedByUser,
    /// <summary>Chosen by the user in the conflict review.</summary>
    UserDecision,
    /// <summary>Read by text recognition from the tooltip in a screenshot the official client saved (Item Discovery, 7.1–7.3; removed in 8.0, kept for existing records).</summary>
    ClientScreenshot,
}

/// <summary>One data source of a catalog: a client version, a TibiaWiki download, a third-party item file, a list…</summary>
public sealed record ItemSource
{
    /// <summary>Stable within the catalog (also the snapshot file name).</summary>
    public required string Id { get; init; }
    public required SourceKind Kind { get; init; }
    /// <summary>Shown to the user: "Tibia client 15.33.9d148f", "TibiaWiki Item IDs", "Community items.xml"…</summary>
    public required string Name { get; init; }
    /// <summary>What the identifiers of this source mean.</summary>
    public required IdType IdType { get; init; }
    /// <summary>Official CipSoft data (the client's files, names observed in the official client). Third-party and community data never are.</summary>
    public bool IsOfficial { get; init; }
    /// <summary>Who publishes the data: "Tibia", "TibiaWiki", the name the user gave a third-party file…</summary>
    public string? Distribution { get; init; }
    /// <summary>Client, protocol or dataset version (for example the client version an items.otb was made for).</summary>
    public string? Version { get; init; }
    /// <summary>File or address the data came from.</summary>
    public string? Origin { get; init; }
    public string? FileSha256 { get; init; }
    /// <summary>When the data were made or downloaded (file time, download time), if known.</summary>
    public DateTime? SourceTimestampUtc { get; init; }
    public DateTime ImportedAtUtc { get; init; }
    public int RecordCount { get; init; }
    public string? Notes { get; init; }

    /// <summary>"official" / "community" / "third-party" / "unknown origin" (for lists and reports).</summary>
    [JsonIgnore]
    public string Trust => IsOfficial ? "official" : Kind switch
    {
        SourceKind.TibiaWiki => "community",
        SourceKind.OtItemsXml or SourceKind.OtItemsOtb => "third-party",
        SourceKind.UserDecision => L.T("ItemModel.YourDecisions"),
        _ => L.T("ItemModel.UnknownOrigin"),
    };
}

/// <summary>One fact about one identifier from one source. Missing values are null (never guessed).</summary>
public sealed record ItemObservation
{
    public required string SourceId { get; init; }
    public required IdType IdType { get; init; }
    public required uint Id { get; init; }
    public string? Name { get; init; }
    /// <summary>Article that goes before the name ("a", "an"), when the source gives it separately.</summary>
    public string? Article { get; init; }
    public string? Plural { get; init; }
    /// <summary>True when <see cref="Name"/> is a TibiaWiki article title, which need not be the in-game name.</summary>
    public bool NameIsTitle { get; init; }
    /// <summary>Market category (client) or item type/category (third-party item files), as the source names it.</summary>
    public string? Category { get; init; }
    /// <summary>Properties that matter for comparisons: "pickupable", "stackable", "container", "door", "teleport"…</summary>
    public IReadOnlyList<string>? Flags { get; init; }
    public ObservationMethod Method { get; init; }
    public ObservationStatus Status { get; init; }
    public DateTime? ObservedAtUtc { get; init; }
    /// <summary>Supporting evidence: a file inside the catalog folder (for example a tooltip image), relative path.</summary>
    public string? Evidence { get; init; }
    public string? Notes { get; init; }

    [JsonIgnore]
    public bool HasName => !string.IsNullOrWhiteSpace(Name);

    /// <summary>The name with its article, as it would be shown in the game ("a gold coin").</summary>
    [JsonIgnore]
    public string? FullName => !HasName ? null : string.IsNullOrEmpty(Article) ? Name : Article + " " + Name;
}

/// <summary>How sure a mapping between two identifier types is.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MappingConfidence>))]
public enum MappingConfidence
{
    /// <summary>Stated by a file (for example the client ID of an items.otb entry).</summary>
    Exact,
    /// <summary>Follows from other mappings.</summary>
    Derived,
    /// <summary>A guess (for example by matching images); needs confirmation.</summary>
    Inferred,
}

/// <summary>A statement that an identifier of one type corresponds to an identifier of another type.</summary>
public sealed record IdMapping(IdType FromType, uint FromId, string? FromVersion, IdType ToType, uint ToId, string? ToVersion, string SourceId,
    MappingConfidence Confidence, string? Evidence = null);

/// <summary>How the records of one identifier relate to each other.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Agreement>))]
public enum Agreement
{
    /// <summary>The name comes from official data.</summary>
    VerifiedOfficial,
    /// <summary>The user chose the name in the conflict review.</summary>
    Decided,
    /// <summary>No official name, but two or more sources give the same name.</summary>
    Corroborated,
    /// <summary>Only one source gives a name.</summary>
    SingleSource,
    /// <summary>Sources give different names and none is official.</summary>
    Conflict,
    /// <summary>No source gives a name.</summary>
    NoName,
}

/// <summary>Comparing names: case, surrounding spaces, repeated spaces and a leading article do not matter.</summary>
public static class ItemNames
{
    private static readonly string[] Articles = ["a ", "an ", "the "];

    /// <summary>The comparison key of a name ("The Holy Tible" and "holy tible" have the same key).</summary>
    public static string Key(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var sb = new StringBuilder(name.Length);
        bool space = false;
        foreach (char c in name.Trim())
        {
            if (char.IsWhiteSpace(c)) { space = true; continue; }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(char.ToLowerInvariant(c));
        }
        string key = sb.ToString();
        foreach (string article in Articles)
            if (key.StartsWith(article, StringComparison.Ordinal) && key.Length > article.Length) return key[article.Length..];
        return key;
    }

    public static bool Same(string? a, string? b) => Key(a) == Key(b);

    /// <summary>The longest name accepted when you type one (a rename).</summary>
    public const int MaxTypedLength = 100;

    /// <summary>
    /// Checks a name you typed: surrounding spaces are removed and runs of spaces become one. Returns
    /// the cleaned name, or null with <paramref name="problem"/> saying why it cannot be used (nothing
    /// typed, a line break, tab or other control character, or more than <see cref="MaxTypedLength"/>
    /// characters).
    /// </summary>
    public static string? CleanTyped(string? text, out string? problem)
    {
        problem = null;
        if (text != null && text.Any(char.IsControl))
        {
            problem = L.T("ItemModel.ANameCannotContainLineBreaks");
            return null;
        }
        string cleaned = string.Join(' ', (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(w => w.Length > 0));
        if (cleaned.Length == 0)
        {
            problem = L.T("ItemModel.TypeAName");
            return null;
        }
        if (cleaned.Length > MaxTypedLength)
        {
            problem = L.F("ItemModel.ANameCanHaveAtMost", MaxTypedLength, cleaned.Length);
            return null;
        }
        return cleaned;
    }

    /// <summary>Splits a leading "a "/"an " from a name (the convention of "[id] name" lists). "the" stays part of the name.</summary>
    public static (string? Article, string Name) SplitArticle(string name)
    {
        string trimmed = name.Trim();
        foreach (string article in new[] { "a ", "an " })
            if (trimmed.Length > article.Length && trimmed.StartsWith(article, StringComparison.OrdinalIgnoreCase))
                return (trimmed[..(article.Length - 1)].ToLowerInvariant(), trimmed[article.Length..].TrimStart());
        return (null, trimmed);
    }

    /// <summary>Description of an identifier type for headers and reports.</summary>
    public static string Describe(IdType type, string? version = null) => type switch
    {
        IdType.TibiaObject => L.T("ItemModel.TibiaObjectIDTheClientS"),
        IdType.OtbServer => L.T("ItemModel.ItemsOtbItemID") + (version != null ? L.F("ItemModel.MappingForClient0", version) : ""),
        IdType.OtbClient => L.T("ItemModel.ClientIDStoredInAnItems") + (version != null ? L.F("ItemModel.Client0", version) : ""),
        IdType.LegacyDatClient => L.T("ItemModel.LegacyTibiaDatObjectID") + (version != null ? L.F("ItemModel.Client0", version) : ""),
        _ => L.T("ItemModel.IdentifierOfUnknownMeaning"),
    };
}
