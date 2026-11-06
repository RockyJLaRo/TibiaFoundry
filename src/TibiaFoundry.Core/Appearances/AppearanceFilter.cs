using Google.Protobuf.Reflection;
using Tibia.Protobuf.Appearances;
using Tibia.Protobuf.Shared;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Appearances;

/// <summary>How the active criteria are combined (the original "Filter Mode" box).</summary>
public enum FilterMode
{
    /// <summary>Include AND – keep appearances matching every criterion.</summary>
    IncludeAll,
    /// <summary>Include OR – keep appearances matching at least one criterion.</summary>
    IncludeAny,
    /// <summary>Exclude AND – drop appearances matching every criterion.</summary>
    ExcludeAll,
    /// <summary>Exclude OR – drop appearances matching any criterion.</summary>
    ExcludeAny,
}

public enum NumberComparison { Equal, AtLeast, AtMost }

/// <summary>One filter condition.</summary>
public sealed record AppearanceCriterion(string Description, Func<Appearance, bool> Matches)
{
    public override string ToString() => Description;
}

/// <summary>A boolean (or presence) flag that can be ticked in the "Flags" filter list.</summary>
public sealed record FlagDefinition(string Name, Func<Appearance, bool> IsSet);

/// <summary>
/// Appearance filtering. Each criterion is a pure predicate; the mode is applied uniformly.
/// The original mixed <c>^ Include</c> into some predicates only, so several comparisons
/// ignored exclude mode, and several criteria crashed (see docs/Bug_Report.md).
/// </summary>
public static class AppearanceFilter
{
    public static IReadOnlyList<Appearance> Apply(IEnumerable<Appearance> source, IReadOnlyList<AppearanceCriterion> criteria, FilterMode mode)
    {
        if (criteria.Count == 0) return source.ToList();
        return source.Where(a => Evaluate(a, criteria, mode)).ToList();
    }

    public static bool Evaluate(Appearance a, IReadOnlyList<AppearanceCriterion> criteria, FilterMode mode)
    {
        bool Safe(AppearanceCriterion c)
        {
            try { return c.Matches(a); }
            catch (Exception ex) when (ex is NullReferenceException or ArgumentOutOfRangeException or InvalidOperationException) { return false; }
        }
        return mode switch
        {
            FilterMode.IncludeAll => criteria.All(Safe),
            FilterMode.IncludeAny => criteria.Any(Safe),
            FilterMode.ExcludeAll => !criteria.All(Safe),
            _ => !criteria.Any(Safe),
        };
    }

    /// <summary>
    /// Every boolean field of <c>AppearanceFlags</c> (taken from the protobuf descriptor, so new
    /// client flags appear automatically) plus the presence flags of the original "Basic Flags" tab.
    /// </summary>
    public static IReadOnlyList<FlagDefinition> FlagDefinitions { get; } = BuildFlagDefinitions();

    private static List<FlagDefinition> BuildFlagDefinitions()
    {
        var list = new List<FlagDefinition>();
        foreach (var field in AppearanceFlags.Descriptor.Fields.InFieldNumberOrder())
        {
            if (field.FieldType != FieldType.Bool) continue;
            var accessor = field.Accessor;
            list.Add(new FlagDefinition(field.Name, a => a.Flags != null && (bool)accessor.GetValue(a.Flags)));
        }
        list.Add(new FlagDefinition("changedtoexpire", a => a.Flags?.Changedtoexpire != null));
        list.Add(new FlagDefinition("cyclopedia", a => a.Flags?.Cyclopediaitem != null));
        list.Add(new FlagDefinition("npcsaledata", a => a.Flags?.Npcsaledata.Count > 0));
        list.Add(new FlagDefinition("name", a => !string.IsNullOrEmpty(a.Name)));
        return list.OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
    }

    public static AppearanceCriterion Flag(FlagDefinition flag) => new(flag.Name, flag.IsSet);

    // ------------------------------------------------------------------ helpers

    public static bool Compare(long value, long target, NumberComparison cmp) => cmp switch
    {
        NumberComparison.AtLeast => value >= target,
        NumberComparison.AtMost => value <= target,
        _ => value == target,
    };

    public static string Symbol(NumberComparison cmp) => cmp switch { NumberComparison.AtLeast => "≥", NumberComparison.AtMost => "≤", _ => "=" };

    private static SpriteInfo? Info(Appearance a, int frameGroup) =>
        frameGroup < a.FrameGroup.Count ? a.FrameGroup[frameGroup].SpriteInfo : null;

    // ------------------------------------------------------------------ advanced flag criteria

    public static AppearanceCriterion Write(uint min, uint max) =>
        new(L.F("AppearanceFilter.WriteLength01", min, max), a => a.Flags?.Write is { } w && w.MaxTextLength >= min && w.MaxTextLength <= max);

    public static AppearanceCriterion WriteOnce(uint min, uint max) =>
        new(L.F("AppearanceFilter.WriteOnceLength01", min, max), a => a.Flags?.WriteOnce is { } w && w.MaxTextLengthOnce >= min && w.MaxTextLengthOnce <= max);

    public static AppearanceCriterion Hook(HOOK_TYPE direction) =>
        new(L.F("AppearanceFilter.Hook0", direction.ToString().ToLowerInvariant()), a => a.Flags?.Hook is { } h && h.Direction == direction);

    /// <param name="brightness">-1 = any.</param>
    /// <param name="color">-1 = any.</param>
    public static AppearanceCriterion Light(int brightness, int color) =>
        new(L.F("AppearanceFilter.LightBrightness0Color1", Any(brightness), Any(color)),
            a => a.Flags?.Light is { } l && (brightness < 0 || l.Brightness == brightness) && (color < 0 || l.Color == color));

    /// <summary>
    /// Bank (ground speed). Fixed: the original also required <c>Waypoints != 0</c>, but in the
    /// current client every bank flag has 0 waypoints, so the filter could never match.
    /// </summary>
    public static AppearanceCriterion Bank(int waypoints, NumberComparison cmp) =>
        new(L.F("AppearanceFilter.Bank0", (waypoints < 0 ? L.T("Choice.Common.any") : Symbol(cmp) + " " + waypoints)),
            a => a.Flags?.Bank is { } b && (waypoints < 0 || Compare(b.Waypoints, waypoints, cmp)));

    public static AppearanceCriterion Shift(int x = -1, int y = -1) =>
        new("shift" + (x >= 0 || y >= 0 ? $" ({Any(x)}, {Any(y)})" : ""),
            a => a.Flags?.Shift is { } s && (x < 0 || s.X == x) && (y < 0 || s.Y == y));

    public static AppearanceCriterion Height(uint elevation) =>
        new(L.F("AppearanceFilter.HeightElevation0", elevation), a => a.Flags?.Height is { } h && h.Elevation == elevation);

    public static AppearanceCriterion Automap(int color) =>
        new(L.F("AppearanceFilter.AutomapColor0", Any(color)), a => a.Flags?.Automap is { } m && (color < 0 || m.Color == color));

    public static AppearanceCriterion LensHelp(int id) =>
        new(L.F("AppearanceFilter.LenshelpId0", Any(id)), a => a.Flags?.Lenshelp is { } l && (id < 0 || l.Id == id));

    /// <param name="slot">Slot value, or <see cref="GameTables.ClothesSlotAll"/> for any slot.</param>
    public static AppearanceCriterion Clothes(int slot) =>
        new(L.F("AppearanceFilter.ClothSlot0", (slot == GameTables.ClothesSlotAll ? L.T("Choice.Common.any") : GameTables.ClothesSlotNames[slot])),
            a => a.Flags?.Clothes is { } c && (slot == GameTables.ClothesSlotAll || c.Slot == slot));

    public static AppearanceCriterion DefaultAction(PLAYER_ACTION? action) =>
        new(L.F("AppearanceFilter.DefaultAction0", (action?.ToString() ?? L.T("Choice.Common.any"))), a => a.Flags?.DefaultAction is { } d && (action == null || d.Action == action));

    public static AppearanceCriterion MarketCategory(ITEM_CATEGORY? category) =>
        new(L.F("AppearanceFilter.MarketCategory0", (category is { } c ? GameTables.MarketCategoryName(c) : L.T("Choice.Common.any"))),
            a => a.Flags?.Market is { } m && (category == null || m.Category == category));

    public static AppearanceCriterion Description(string text) =>
        new(L.F("AppearanceFilter.DescriptionContains0", text), a => !string.IsNullOrEmpty(a.Description) && a.Description.Contains(text, StringComparison.OrdinalIgnoreCase));

    /// <param name="currency">Currency name (empty = any non-gold currency).</param>
    public static AppearanceCriterion Currency(string currency, AppearanceDatabase db)
    {
        string text = currency.Trim();
        if (text.Length == 0)
            return new(L.T("AppearanceFilter.CurrencyAny"), a => a.Flags?.Npcsaledata.Any(n => n.CurrencyObjectTypeId != 0 || n.CurrencyQuestFlagDisplayName.Length > 0) == true);
        var known = db.FindCurrency(text);
        return new(L.F("AppearanceFilter.Currency0", text), a => a.Flags?.Npcsaledata.Any(n =>
            known != null && ((known.ObjectId is { } id && n.CurrencyObjectTypeId == id) ||
                              (known.ObjectId == null && string.Equals(n.CurrencyQuestFlagDisplayName, known.Key, StringComparison.OrdinalIgnoreCase)))) == true);
    }

    /// <summary>Imbueable with the ticked slot counts (none ticked = any slot count). Fixed a NullReferenceException.</summary>
    public static AppearanceCriterion Imbueable(bool oneSlot, bool twoSlots, bool threeSlots)
    {
        bool any = !oneSlot && !twoSlots && !threeSlots;
        return new("imbueable" + (any ? "" : L.F("AppearanceFilter.Slots012", (oneSlot ? "1 " : ""), (twoSlots ? "2 " : ""), (threeSlots ? "3" : "")).TrimEnd()),
            a => a.Flags?.Imbueable is { } i && (any || (i.SlotCount == 1 && oneSlot) || (i.SlotCount == 2 && twoSlots) || (i.SlotCount == 3 && threeSlots)));
    }

    public static AppearanceCriterion Proficiency(int id) =>
        new(L.F("AppearanceFilter.ProficiencyId0", Any(id)), a => a.Flags?.Proficiency is { } p && (id < 0 || p.ProficiencyId == id));

    /// <summary>Fixed: "Any" matched every object because a repeated field is never null.</summary>
    public static AppearanceCriterion Vocation(VOCATION? vocation) =>
        new(L.F("AppearanceFilter.RestrictedTo0", (vocation is { } v ? GameTables.VocationName(v) : L.T("AppearanceFilter.AnyVocation"))),
            a => a.Flags != null && a.Flags.RestrictToVocation.Count > 0 && (vocation == null || a.Flags.RestrictToVocation.Contains(vocation.Value)));

    public static AppearanceCriterion WeaponType(WEAPON_TYPE type) =>
        new(L.F("AppearanceFilter.WeaponType0", type), a => a.Flags != null && a.Flags.WeaponType == type);

    public static AppearanceCriterion MinimumLevel(int level, NumberComparison cmp) =>
        new(L.F("AppearanceFilter.MinLevel0", (level < 0 ? L.T("Choice.Common.any") : Symbol(cmp) + " " + level)),
            a => a.Flags != null && a.Flags.MinimumLevel != 0 && (level < 0 || Compare(a.Flags.MinimumLevel, level, cmp)));

    /// <param name="classification">0 = any.</param>
    public static AppearanceCriterion UpgradeClassification(int classification) =>
        new(L.F("AppearanceFilter.UpgradeClassification0", (classification == 0 ? L.T("Choice.Common.any") : classification.ToString())),
            a => a.Flags?.Upgradeclassification is { } u && (classification == 0 || u.UpgradeClassification == classification));

    // ------------------------------------------------------------------ frame group / sprite info

    public static AppearanceCriterion FrameGroupCount(int count, NumberComparison cmp) =>
        new(L.F("AppearanceFilter.FrameGroups01", Symbol(cmp), count), a => a.FrameGroup.Count > 0 && Compare(a.FrameGroup.Count, count, cmp));

    public static AppearanceCriterion IsOpaque(int fg) => new(L.F("AppearanceFilter.FG0IsOpaque", fg), a => Info(a, fg)?.IsOpaque == true);

    public static AppearanceCriterion Layers(int fg, int n, NumberComparison cmp) =>
        new(L.F("AppearanceFilter.FG0Layers12", fg, Symbol(cmp), n), a => Info(a, fg) is { } i && Compare(i.Layers, n, cmp));

    public static AppearanceCriterion SpriteCount(int fg, int n, NumberComparison cmp) =>
        new(L.F("AppearanceFilter.FG0Sprites12", fg, Symbol(cmp), n), a => Info(a, fg) is { } i && Compare(i.SpriteId.Count, n, cmp));

    public static AppearanceCriterion PatternWidth(int fg, int n, NumberComparison cmp) =>
        new(L.F("AppearanceFilter.FG0PatternWidth12", fg, Symbol(cmp), n), a => Info(a, fg) is { } i && Compare(i.PatternWidth, n, cmp));

    public static AppearanceCriterion PatternHeight(int fg, int n, NumberComparison cmp) =>
        new(L.F("AppearanceFilter.FG0PatternHeight12", fg, Symbol(cmp), n), a => Info(a, fg) is { } i && Compare(i.PatternHeight, n, cmp));

    public static AppearanceCriterion PatternDepth(int fg, int n, NumberComparison cmp) =>
        new(L.F("AppearanceFilter.FG0PatternDepth12", fg, Symbol(cmp), n), a => Info(a, fg) is { } i && Compare(i.PatternDepth, n, cmp));

    /// <summary>
    /// Bounding-box width of the first direction: "32" = up to 32 px, "64" = wider. Fixed: the
    /// original tested <c>&lt; 32</c>, so 32-px-wide boxes matched neither option.
    /// </summary>
    public static AppearanceCriterion BoundingWidth(int fg, bool large) =>
        new(L.F("AppearanceFilter.FG0BoundingWidth1", fg, (large ? "> 32" : "≤ 32")), a => Info(a, fg) is { BoundingBoxPerDirection.Count: > 0 } i && (i.BoundingBoxPerDirection[0].Width > 32) == large);

    public static AppearanceCriterion BoundingHeight(int fg, bool large) =>
        new(L.F("AppearanceFilter.FG0BoundingHeight1", fg, (large ? "> 32" : "≤ 32")), a => Info(a, fg) is { BoundingBoxPerDirection.Count: > 0 } i && (i.BoundingBoxPerDirection[0].Height > 32) == large);

    public static AppearanceCriterion Animated(int fg) => new(L.F("AppearanceFilter.FG0Animated", fg), a => Info(a, fg)?.Animation != null);

    public static AppearanceCriterion Synchronized(int fg) => new(L.F("AppearanceFilter.FG0Synchronized", fg), a => Info(a, fg)?.Animation?.Synchronized == true);

    public static AppearanceCriterion LoopType(int fg, ANIMATION_LOOP_TYPE type) =>
        new(L.F("AppearanceFilter.FG0LoopType1", fg, type), a => Info(a, fg)?.Animation is { } an && an.LoopType == type);

    public static AppearanceCriterion MinDuration(int fg, int ms, NumberComparison cmp) =>
        new(L.F("AppearanceFilter.FG0MinDuration12", fg, Symbol(cmp), ms), a => Info(a, fg)?.Animation is { SpritePhase.Count: > 0 } an && Compare(an.SpritePhase.Min(p => p.DurationMin), ms, cmp));

    public static AppearanceCriterion MaxDuration(int fg, int ms, NumberComparison cmp) =>
        new(L.F("AppearanceFilter.FG0MaxDuration12", fg, Symbol(cmp), ms), a => Info(a, fg)?.Animation is { SpritePhase.Count: > 0 } an && Compare(an.SpritePhase.Max(p => p.DurationMax), ms, cmp));

    // ------------------------------------------------------------------ IDs, names, sprites

    public static AppearanceCriterion IdAtLeast(uint id) => new(L.F("AppearanceFilter.Id0", id), a => a.Id >= id);

    public static AppearanceCriterion IdAtMost(uint id) => new(L.F("AppearanceFilter.Id02", id), a => a.Id <= id);

    public static AppearanceCriterion IdIn(IReadOnlyCollection<uint> ids, string? label = null)
    {
        var set = ids as HashSet<uint> ?? new HashSet<uint>(ids);
        return new(label ?? L.F("AppearanceFilter.IdInList0", set.Count), a => set.Contains(a.Id));
    }

    public static AppearanceCriterion NameContains(string text) =>
        new(L.F("AppearanceFilter.NameContains0", text), a => !string.IsNullOrEmpty(a.Name) && a.Name.Contains(text, StringComparison.OrdinalIgnoreCase));

    /// <summary>Appearances that use the sprite in any frame group (the original only searched the first).</summary>
    public static AppearanceCriterion UsesSprite(uint spriteId) =>
        new(L.F("AppearanceFilter.UsesSprite0", spriteId), a => a.FrameGroup.Any(fg => fg.SpriteInfo?.SpriteId.Contains(spriteId) == true));

    /// <summary>Parses "1, 2,3" into IDs. Returns false (with the bad token) on invalid input.</summary>
    public static bool TryParseIdList(string text, out List<uint> ids, out string? badToken)
    {
        ids = new List<uint>();
        badToken = null;
        foreach (var token in text.Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!uint.TryParse(token, out uint id)) { badToken = token; return false; }
            ids.Add(id);
        }
        return true;
    }

    private static string Any(int value) => value < 0 ? "any" : value.ToString();
}
