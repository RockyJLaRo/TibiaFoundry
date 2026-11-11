using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Items;

/// <summary>
/// The names of the item properties ("flags") recorded in a catalog. They are data: they are saved in the
/// catalog, written to exports and compared between sources, so they never change with the interface language.
/// </summary>
/// <remarks>
/// 9.0.4: versions 9.0 to 9.0.3 wrote four of them (<see cref="LiquidContainer"/>, <see cref="NpcTrade"/>,
/// <see cref="MagicField"/>, <see cref="TrashHolder"/>) in the interface language. A catalog imported in German
/// then held "Magiefeld" where one imported in English held "magic field": the same client compared as changed,
/// exports mixed languages, and the behavior-sensitive properties were not recognized. <see cref="Canonical(string)"/>
/// maps those translated names back when a catalog is read, so existing catalogs need no new import.
/// </remarks>
public static class ItemFlags
{
    public const string LiquidContainer = "liquid container";
    public const string NpcTrade = "npc trade";
    public const string MagicField = "magic field";
    public const string TrashHolder = "trash holder";
    public const string FloorChange = "floor change";

    /// <summary>The string-table keys whose translations 9.0–9.0.3 wrote into catalogs, with the name they stand for.</summary>
    private static readonly (string Key, string Flag)[] TranslatedKeys =
    [
        ("ItemImporters.LiquidContainer", LiquidContainer), ("ItemImporters.NpcTrade", NpcTrade),
        ("ItemImporters.MagicField", MagicField), ("ItemImporters.TrashHolder", TrashHolder),
    ];

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Translated = new(() =>
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var language in Languages.All)
        {
            var table = L.Table(language.Code);
            foreach (var (key, flag) in TranslatedKeys)
                if (table.TryGetValue(key, out string? text) && text.Length > 0 && text != flag) map[text] = flag;
        }
        return map;
    });

    /// <summary>The language-independent name of a flag (a name written in an interface language by 9.0–9.0.3 is mapped back).</summary>
    public static string Canonical(string flag) => Translated.Value.TryGetValue(flag, out string? canonical) ? canonical : flag;

    /// <summary>The flags with language-independent names; the same list when nothing had to change.</summary>
    public static IReadOnlyList<string>? Canonical(IReadOnlyList<string>? flags)
    {
        if (flags == null) return null;
        var translated = Translated.Value;
        bool any = false;
        foreach (string f in flags)
            if (translated.ContainsKey(f)) { any = true; break; }
        if (!any) return flags;
        var result = new List<string>(flags.Count);
        foreach (string f in flags)
        {
            string c = Canonical(f);
            if (!result.Contains(c)) result.Add(c);
        }
        return result;
    }

    /// <summary>The observation with language-independent flag names (the same instance when nothing had to change).</summary>
    public static ItemObservation Canonical(ItemObservation observation)
    {
        var flags = Canonical(observation.Flags);
        return ReferenceEquals(flags, observation.Flags) ? observation : observation with { Flags = flags };
    }
}
