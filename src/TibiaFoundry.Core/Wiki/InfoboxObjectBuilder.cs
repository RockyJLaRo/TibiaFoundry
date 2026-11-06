using System.Globalization;
using Tibia.Protobuf.Appearances;
using Tibia.Protobuf.Shared;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Wiki;

/// <summary>
/// Builds TibiaWiki's <c>{{Infobox Object}}</c> (https://tibia.fandom.com/wiki/Template:Infobox_Object) for an object
/// of the client's appearances.dat.
/// </summary>
/// <remarks>
/// Only facts the client states are filled in; everything else the template requires (implemented, weight,
/// value, notes…) is left for the editor and marked <see cref="WikiFieldSource.Missing"/>. Combat values
/// (attack, defense, armor, attributes, resistances) are not part of the client's files and are never guessed.
/// Parameter names, their order and value formats follow the template source and TibiaWiki's item pages
/// (checked 2026-11-06 against Magic Plate Armor, Sword, Gold Coin, Bow, Scroll, Spellbook and others).
/// </remarks>
public static class InfoboxObjectBuilder
{
    public const string TemplateName = "Infobox Object";

    /// <summary>The leading arguments every TibiaWiki item page passes (they let lists and Lua modules read single values).</summary>
    public static readonly IReadOnlyList<string> LeadingArguments = ["List={{{1|}}}", "GetValue={{{GetValue|}}}"];

    /// <summary>Required by the template for every object (pages without them land in a "Parameter Required" category).</summary>
    public static readonly IReadOnlySet<string> RequiredAlways = new HashSet<string> { "name", "itemid", "implemented", "objectclass", "primarytype", "pickupable", "immobile", "notes" };

    /// <summary>Additionally required for pickupable objects.</summary>
    public static readonly IReadOnlySet<string> RequiredForItems = new HashSet<string> { "droppedby", "weight", "value", "npcvalue", "npcprice", "buyfrom", "sellto", "marketable" };

    /// <summary>
    /// Every parameter the template reads, in the order TibiaWiki's item pages use (unlisted ones at the end in template order).
    /// The editor can add any of them; the builder fills only those the client knows.
    /// </summary>
    public static readonly IReadOnlyList<string> ParameterOrder =
    [
        "name", "article", "actualname", "plural", "itemid", "objectclass", "primarytype", "secondarytype", "tertiarytype",
        "slot", "flavortext", "sounds", "implemented", "status", "immobile", "walkable", "pickupable", "usable", "rotatable",
        "hangable", "holdsliquid", "volume", "writable", "rewritable", "writechars", "wrappable", "destructible", "unshootable",
        "blockspath", "levelrequired", "mlrequired", "vocrequired", "hands", "weapontype", "range", "atk_mod", "hit_mod",
        "attack", "fire_attack", "earth_attack", "ice_attack", "energy_attack", "death_attack", "elementalbond", "damagetype",
        "damagerange", "defense", "defensemod", "armor", "mantra", "attrib", "resist", "augments", "charges", "manacost",
        "words", "crithit_ch", "critextra_dmg", "manaleech_ch", "manaleech_am", "hpleech_ch", "hpleech_am", "imbueslots",
        "imbuements", "upgradeclass", "enchantable", "enchanted", "duration", "consumable", "regenseconds", "cooldown",
        "inscription", "lightradius", "lightcolor", "mapcolor", "weight", "stackable", "task_item", "marketable", "fansite",
        "droppedby", "value", "storevalue", "npcvalue", "npcprice", "npcvaluerook", "npcpricerook", "pricecurrency",
        "buyfrom", "sellto", "location", "notes", "notes2", "history",
    ];

    private static readonly HashSet<string> MultilineParameters = ["notes", "notes2", "history", "flavortext", "location"];

    /// <summary>Market category → TibiaWiki classification (objectclass, primarytype, secondarytype), only where the wiki's pages agree.</summary>
    private static readonly Dictionary<ITEM_CATEGORY, (string Class, string? Primary, string? Secondary)> Classification = new()
    {
        [ITEM_CATEGORY.Armors] = ("Body Equipment", "Armors", null),
        [ITEM_CATEGORY.Amulets] = ("Body Equipment", "Amulets and Necklaces", null),
        [ITEM_CATEGORY.Boots] = ("Body Equipment", "Boots", null),
        [ITEM_CATEGORY.HelmetsHats] = ("Body Equipment", "Helmets", null),
        [ITEM_CATEGORY.Legs] = ("Body Equipment", "Legs", null),
        [ITEM_CATEGORY.Rings] = ("Body Equipment", "Rings", null),
        [ITEM_CATEGORY.Shields] = ("Body Equipment", "Shields", null),
        [ITEM_CATEGORY.Quiver] = ("Body Equipment", "Quivers", null),
        [ITEM_CATEGORY.Swords] = ("Weapons", "Sword Weapons", null),
        [ITEM_CATEGORY.Axes] = ("Weapons", "Axe Weapons", null),
        [ITEM_CATEGORY.Clubs] = ("Weapons", "Club Weapons", null),
        [ITEM_CATEGORY.DistanceWeapons] = ("Weapons", "Distance Weapons", null),
        [ITEM_CATEGORY.Ammunition] = ("Weapons", "Ammunition", null),
        [ITEM_CATEGORY.WandsRods] = ("Weapons", null, null),
        [ITEM_CATEGORY.Valuables] = ("Other Items", "Valuables", null),
        [ITEM_CATEGORY.Food] = ("Plants, Animal Products, Food and Drink", "Food", null),
        [ITEM_CATEGORY.Potions] = ("Plants, Animal Products, Food and Drink", "Liquids", "Potions"),
        [ITEM_CATEGORY.Containers] = ("Household Items", "Containers", null),
        [ITEM_CATEGORY.Tools] = ("Tools and other Equipment", "Tools", null),
        [ITEM_CATEGORY.Runes] = ("Runes", null, null),
    };

    /// <summary>Vocation order of TibiaWiki's "vocrequired" ("knights and paladins", "sorcerers and druids").</summary>
    private static readonly (VOCATION Value, string Plural)[] VocationPlurals =
    [
        (VOCATION.Knight, "knights"), (VOCATION.Paladin, "paladins"), (VOCATION.Sorcerer, "sorcerers"), (VOCATION.Druid, "druids"), (VOCATION.Monk, "monks"),
    ];

    /// <summary>Builds the infobox for <paramref name="obj"/>.</summary>
    /// <param name="wikiName">The object's TibiaWiki article name, when known (cached "Item IDs" table).</param>
    public static WikiTemplate Build(Appearance obj, AppearanceDatabase? db = null, string? wikiName = null)
    {
        var fields = new Dictionary<string, WikiField>();
        var warnings = new List<string>();
        var flags = obj.Flags ?? new AppearanceFlags();
        bool pickupable = flags.Take;

        void Set(string name, string? value, WikiFieldSource source, string? hint = null)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            fields[name] = new WikiField(name, value.Trim(), source, IsRequired(name, pickupable), MultilineParameters.Contains(name), hint);
        }
        static string YesNo(bool value) => value ? "yes" : "no";

        // Identity
        string clientName = obj.Name?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(wikiName)) Set("name", wikiName, WikiFieldSource.TibiaWiki, L.T("Infobox.HintWikiName"));
        else if (clientName.Length > 0) Set("name", NameFormatter.Capitalize(clientName), WikiFieldSource.Derived, L.T("Infobox.HintClientName"));
        else warnings.Add(L.T("Infobox.WarnNoName"));
        if (clientName.Length > 0) Set("actualname", clientName, WikiFieldSource.Client, L.T("Infobox.HintActualName"));
        Set("itemid", obj.Id.ToString(CultureInfo.InvariantCulture), WikiFieldSource.Client);
        if (db != null && clientName.Length > 0)
        {
            var sameName = db.List(AppearanceKind.Object).Where(o => o.Id != obj.Id && string.Equals(o.Name, clientName, StringComparison.OrdinalIgnoreCase)).Select(o => o.Id).ToList();
            if (sameName.Count > 0)
                warnings.Add(L.F("Infobox.WarnSameName", clientName, string.Join(", ", sameName.Take(20)) + (sameName.Count > 20 ? "…" : "")));
        }

        // The client's look text (Appearance.Description) is what TibiaWiki calls "flavortext". The client stores it
        // without the closing full stop the game adds on Look; TibiaWiki writes it (checked 2026-11-06 on 107 item pages).
        Set("flavortext", FlavorText(obj.Description), WikiFieldSource.Client);

        // Classification (from the market category)
        if (flags.Market is { } market && Classification.TryGetValue(market.Category, out var cls))
        {
            string hint = L.T("Infobox.HintClassification");
            Set("objectclass", cls.Class, WikiFieldSource.Derived, hint);
            string? primary = cls.Primary;
            if (market.Category == ITEM_CATEGORY.WandsRods)
            {
                var vocs = flags.RestrictToVocation.ToHashSet();
                if (vocs.SetEquals([VOCATION.Sorcerer])) primary = "Wands";
                else if (vocs.SetEquals([VOCATION.Druid])) primary = "Rods";
            }
            if (market.Category == ITEM_CATEGORY.Shields && flags.RestrictToVocation.Count > 0
                && flags.RestrictToVocation.All(v => v is VOCATION.Sorcerer or VOCATION.Druid))
            {
                primary = "Spellbooks";
                Set("secondarytype", "Shields", WikiFieldSource.Derived, hint);
            }
            Set("primarytype", primary, WikiFieldSource.Derived, hint);
            Set("secondarytype", cls.Secondary, WikiFieldSource.Derived, hint);
            if (flags.WeaponType is WEAPON_TYPE.Bow) Set("secondarytype", "Bows", WikiFieldSource.Derived, hint);
            if (flags.WeaponType is WEAPON_TYPE.Crossbow) Set("secondarytype", "Crossbows", WikiFieldSource.Derived, hint);
            if (flags.WeaponType is WEAPON_TYPE.Throw) Set("secondarytype", "Throwing Weapons", WikiFieldSource.Derived, hint);
        }

        // Equipment
        if (flags.Clothes is { } clothes && clothes.Slot < 11)
            Set("slot", GameTables.ClothesSlotName(clothes.Slot), WikiFieldSource.Client);
        if (flags.MinimumLevel > 0) Set("levelrequired", flags.MinimumLevel.ToString(CultureInfo.InvariantCulture), WikiFieldSource.Client);
        string? vocations = VocationText(flags.RestrictToVocation, warnings);
        Set("vocrequired", vocations, WikiFieldSource.Client);
        string? weaponType = flags.WeaponType switch
        {
            WEAPON_TYPE.Sword => "Sword",
            WEAPON_TYPE.Axe => "Axe",
            WEAPON_TYPE.Club => "Club",
            WEAPON_TYPE.Bow or WEAPON_TYPE.Crossbow or WEAPON_TYPE.Throw => "Distance",
            _ => null,
        };
        if (weaponType != null)
        {
            Set("weapontype", weaponType, WikiFieldSource.Client);
            if (flags.Clothes is { } c) Set("hands", c.Slot == 0 ? "Two" : c.Slot == 6 ? "One" : null, WikiFieldSource.Client);
        }
        if (flags.Imbueable is { SlotCount: > 0 } imbue) Set("imbueslots", imbue.SlotCount.ToString(CultureInfo.InvariantCulture), WikiFieldSource.Client);
        if (flags.Upgradeclassification is { UpgradeClassification: > 0 } upgrade)
            Set("upgradeclass", upgrade.UpgradeClassification.ToString(CultureInfo.InvariantCulture), WikiFieldSource.Client);

        // General and field properties
        Set("immobile", YesNo(flags.Unmove), WikiFieldSource.Client);
        Set("walkable", YesNo(!flags.Unpass), WikiFieldSource.Client);
        Set("pickupable", YesNo(pickupable), WikiFieldSource.Client);
        if (flags.Usable) Set("usable", "yes", WikiFieldSource.Client);
        if (flags.Rotate) Set("rotatable", "yes", WikiFieldSource.Client);
        if (flags.Hang) Set("hangable", "yes", WikiFieldSource.Client);
        if (flags.Liquidcontainer) Set("holdsliquid", "yes", WikiFieldSource.Client);
        if (flags.Write != null || flags.WriteOnce != null)
        {
            Set("writable", "yes", WikiFieldSource.Client);
            if (flags.Write != null) Set("rewritable", "yes", WikiFieldSource.Client);
            uint chars = flags.Write?.MaxTextLength ?? flags.WriteOnce?.MaxTextLengthOnce ?? 0;
            if (chars > 0) Set("writechars", chars.ToString(CultureInfo.InvariantCulture), WikiFieldSource.Client);
        }
        if (flags.Wrap) Set("wrappable", "yes", WikiFieldSource.Client);
        if (!pickupable && flags.Unsight) Set("unshootable", "yes", WikiFieldSource.Client);
        if (flags.Light is { Brightness: > 0 } light)
        {
            Set("lightradius", light.Brightness.ToString(CultureInfo.InvariantCulture), WikiFieldSource.Client);
            Set("lightcolor", light.Color.ToString(CultureInfo.InvariantCulture), WikiFieldSource.Client);
        }
        if (flags.Automap is { } automap) Set("mapcolor", automap.Color.ToString(CultureInfo.InvariantCulture), WikiFieldSource.Client);

        // Item (pickupable) properties
        if (pickupable)
        {
            Set("stackable", YesNo(flags.Cumulative), WikiFieldSource.Client);
            Set("marketable", YesNo(flags.Market != null), WikiFieldSource.Client);
            Set("droppedby", "{{Dropped By|}}", WikiFieldSource.Placeholder, L.T("Infobox.HintDroppedBy"));
            AddTrade(flags, db, Set, warnings);
        }

        // Required parameters nobody knows yet: listed empty so the editor sees them.
        foreach (string name in ParameterOrder)
            if (IsRequired(name, pickupable) && !fields.ContainsKey(name))
                fields[name] = new WikiField(name, "", WikiFieldSource.Missing, true, MultilineParameters.Contains(name), MissingHint(name));

        var ordered = ParameterOrder.Where(fields.ContainsKey).Select(n => fields[n]).ToList();
        return new WikiTemplate(TemplateName, LeadingArguments, ordered, warnings);
    }

    /// <summary>Whether the template requires <paramref name="name"/> for an object that is (or is not) pickupable.</summary>
    public static bool IsRequired(string name, bool pickupable) => RequiredAlways.Contains(name) || (pickupable && RequiredForItems.Contains(name));

    /// <summary>A file name for the exported markup: "Magic Plate Armor (3366).txt".</summary>
    public static string FileNameFor(WikiTemplate template, uint id)
    {
        string name = template.Field("name")?.Value is { Length: > 0 } n ? n : "Object";
        string safe = FileNames.Sanitize(name);
        if (safe.Length > 100) safe = safe[..100];
        return $"{(safe.Length == 0 ? "Object" : safe)} ({id}).txt";
    }

    /// <summary>
    /// A path in <paramref name="folder"/> that is neither an existing file nor already in <paramref name="taken"/>
    /// ("name.txt", then "name 2.txt"…): batch exports never replace anything. The path is added to <paramref name="taken"/>.
    /// </summary>
    public static string UniquePath(string folder, string fileName, ISet<string> taken)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName), ext = Path.GetExtension(fileName);
        string path = Path.Combine(folder, fileName);
        for (int n = 2; taken.Contains(path) || File.Exists(path); n++) path = Path.Combine(folder, $"{stem} {n}{ext}");
        taken.Add(path);
        return path;
    }

    /// <summary>The client's look text as TibiaWiki writes it: trimmed, ending with a full stop unless it already ends a sentence.</summary>
    public static string? FlavorText(string? description)
    {
        string text = description?.Trim() ?? "";
        if (text.Length == 0) return null;
        return text[^1] is '.' or '!' or '?' ? text : text + ".";
    }

    private static string? VocationText(IReadOnlyCollection<VOCATION> restriction, List<string> warnings)
    {
        if (restriction.Count == 0 || restriction.Contains(VOCATION.Any)) return null;
        if (restriction.Contains(VOCATION.Promoted)) warnings.Add(L.T("Infobox.WarnPromoted"));
        var names = VocationPlurals.Where(v => restriction.Contains(v.Value)).Select(v => v.Plural).ToList();
        return names.Count switch
        {
            0 => null,
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        };
    }

    /// <summary>
    /// NPC trade from the client's sale data: "npcvalue" is the highest price an NPC pays, "npcprice" the lowest price an NPC asks;
    /// "sellto"/"buyfrom" list the NPCs (price added when it differs from that value), "--" when there are none.
    /// Trades in other currencies than gold are left out and reported.
    /// </summary>
    private static void AddTrade(AppearanceFlags flags, AppearanceDatabase? db, Action<string, string?, WikiFieldSource, string?> set, List<string> warnings)
    {
        var trades = flags.Npcsaledata;
        var other = trades.Where(t => !IsGold(t, db)).ToList();
        if (other.Count > 0)
            warnings.Add(L.F("Infobox.WarnOtherCurrency", string.Join(", ", other.Select(t => t.Name).Distinct())));
        var gold = trades.Where(t => IsGold(t, db)).ToList();
        var buyers = gold.Where(t => t.BuyPrice > 0).GroupBy(t => t.Name).Select(g => (Name: g.Key, Price: g.Max(t => t.BuyPrice))).OrderBy(t => t.Name, WikiNameComparer.Instance).ToList();
        var sellers = gold.Where(t => t.SalePrice > 0).GroupBy(t => t.Name).Select(g => (Name: g.Key, Price: g.Min(t => t.SalePrice))).OrderBy(t => t.Name, WikiNameComparer.Instance).ToList();
        uint npcValue = buyers.Count == 0 ? 0 : buyers.Max(b => b.Price);
        uint npcPrice = sellers.Count == 0 ? 0 : sellers.Min(s => s.Price);
        string hint = L.T("Infobox.HintTrade");
        set("npcvalue", npcValue.ToString(CultureInfo.InvariantCulture), WikiFieldSource.Client, hint);
        set("npcprice", npcPrice.ToString(CultureInfo.InvariantCulture), WikiFieldSource.Client, hint);
        set("buyfrom", sellers.Count == 0 ? "--" : string.Join(", ", sellers.Select(s => s.Price == npcPrice ? s.Name : $"{s.Name}: {s.Price}")), WikiFieldSource.Client, hint);
        set("sellto", buyers.Count == 0 ? "--" : string.Join(", ", buyers.Select(b => b.Price == npcValue ? b.Name : $"{b.Name}: {b.Price}")), WikiFieldSource.Client, hint);
    }

    private static bool IsGold(AppearanceFlagNPC trade, AppearanceDatabase? db)
    {
        if (trade.CurrencyQuestFlagDisplayName.Length > 0) return false;
        if (trade.CurrencyObjectTypeId == 0) return true;
        string? name = db?.Find(AppearanceKind.Object, trade.CurrencyObjectTypeId)?.Name;
        return string.Equals(name, "gold coin", StringComparison.OrdinalIgnoreCase);
    }

    private static string MissingHint(string name) => name switch
    {
        "implemented" => L.T("Infobox.HintImplemented"),
        "notes" => L.T("Infobox.HintNotes"),
        "weight" => L.T("Infobox.HintWeight"),
        "value" => L.T("Infobox.HintValue"),
        "objectclass" => L.T("Infobox.HintObjectClass"),
        "primarytype" => L.T("Infobox.HintPrimaryType"),
        _ => L.T("Infobox.HintRequired"),
    };
}
