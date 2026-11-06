using Tibia.Protobuf.Shared;

namespace TibiaFoundry.Core.Appearances;

/// <summary>Lookup tables recovered from the original MainForm constructor.</summary>
public static class GameTables
{
    /// <summary>Market category names, indexed like <see cref="ItemCategories"/> (index 0 = "Any").</summary>
    public static readonly IReadOnlyList<string> MarketCategoryNames =
    [
        "Any", "Armors", "Amulets", "Boots", "Containers", "Decoration", "Food", "Helmets and Hats", "Legs", "Others",
        "Potions", "Rings", "Runes", "Shields", "Tools", "Valuables", "Ammunition", "Axes", "Clubs", "Distance Weapons",
        "Swords", "Wands and Rods", "Premium Scrolls", "Tibia Coins", "Creature Products", "Quivers", "Soul Cores", "Fist Weapons",
    ];

    public static readonly IReadOnlyList<ITEM_CATEGORY> ItemCategories = Enumerable.Range(0, 28).Select(i => (ITEM_CATEGORY)i).ToList();

    public static string MarketCategoryName(ITEM_CATEGORY category)
    {
        int i = (int)category;
        return i >= 0 && i < MarketCategoryNames.Count ? MarketCategoryNames[i] : category.ToString();
    }

    /// <summary>Clothes slot names by slot value; the last entry ("All") is the filter wildcard.</summary>
    public static readonly IReadOnlyList<string> ClothesSlotNames =
    [
        "Both Hands", "Head", "Neck", "Container", "Body", "Shield Hand", "Weapon Hand", "Legs", "Feet", "Finger",
        "Extra Slot", "Store Slot", "All",
    ];

    public const int ClothesSlotAll = 12;

    public static string ClothesSlotName(uint slot) => slot < ClothesSlotAll ? ClothesSlotNames[(int)slot] : "Slot " + slot;

    public static readonly IReadOnlyList<(string Name, VOCATION Value)> Vocations =
    [
        ("Any", VOCATION.Any), ("None", VOCATION.None), ("Knight", VOCATION.Knight), ("Paladin", VOCATION.Paladin),
        ("Sorcerer", VOCATION.Sorcerer), ("Druid", VOCATION.Druid), ("Monk", VOCATION.Monk), ("Promoted", VOCATION.Promoted),
    ];

    public static string VocationName(VOCATION v) => Vocations.FirstOrDefault(x => x.Value == v).Name ?? v.ToString();

    /// <summary>Default actions; the last entry ("Any") is the filter wildcard.</summary>
    public static readonly IReadOnlyList<(string Name, PLAYER_ACTION? Value)> PlayerActions =
    [
        ("None", PLAYER_ACTION.None), ("Look", PLAYER_ACTION.Look), ("Use", PLAYER_ACTION.Use), ("Open", PLAYER_ACTION.Open),
        ("Autowalk", PLAYER_ACTION.AutowalkHighlight), ("Any", null),
    ];

    /// <summary>Loop types in the order of the original filter combo box.</summary>
    public static readonly IReadOnlyList<(string Name, ANIMATION_LOOP_TYPE Value)> LoopTypes =
    [
        ("Counted", ANIMATION_LOOP_TYPE.Counted), ("Infinite", ANIMATION_LOOP_TYPE.Infinite), ("PingPong", ANIMATION_LOOP_TYPE.Pingpong),
    ];

    public static readonly IReadOnlyList<(string Name, WEAPON_TYPE Value)> WeaponTypes =
    [
        ("No weapon", WEAPON_TYPE.Noweapon), ("Sword", WEAPON_TYPE.Sword), ("Axe", WEAPON_TYPE.Axe), ("Club", WEAPON_TYPE.Club),
        ("Fist", WEAPON_TYPE.Fist), ("Bow", WEAPON_TYPE.Bow), ("Crossbow", WEAPON_TYPE.Crossbow), ("Wand/Rod", WEAPON_TYPE.WandRod),
        ("Throw", WEAPON_TYPE.Throw),
    ];

    /// <summary>
    /// The 216-entry web-safe palette used for automap (minimap) and light colors. Index n maps
    /// to (n / 36 * 51, n / 6 % 6 * 51, n % 6 * 51); this formula reproduces the original's table.
    /// </summary>
    public static uint AutomapColor(uint index)
    {
        if (index > 215) return 0xFF000000;
        uint r = index / 36 * 51, g = index / 6 % 6 * 51, b = index % 6 * 51;
        return 0xFF000000 | r << 16 | g << 8 | b;
    }

    /// <summary>The 133 outfit colors (7 rows × 19), indexed by outfit color value.</summary>
    public static readonly IReadOnlyList<string> OutfitPaletteHex =
    [
        "#FFFFFF", "#FFD4BF", "#FFE9BF", "#FFFFBF", "#E9FFBF", "#D4FFBF", "#BFFFBF", "#BFFFD4", "#BFFFE9", "#BFFFFF",
        "#BFE9FF", "#BFD4FF", "#BFBFFF", "#D4BFFF", "#E9BFFF", "#FFBFFF", "#FFBFE9", "#FFBFD4", "#FFBFBF", "#DADADA",
        "#BF9F8F", "#BFAF8F", "#BFBF8F", "#AFBF8F", "#9FBF8F", "#8FBF8F", "#8FBF9F", "#8FBFAF", "#8FBFBF", "#8FAFBF",
        "#8F9FBF", "#8F8FBF", "#9F8FBF", "#AF8FBF", "#BF8FBF", "#BF8FAF", "#BF8F9F", "#BF8F8F", "#B6B6B5", "#BF7F5F",
        "#BF9F5F", "#BFBF5F", "#9FBF5F", "#7FBF5F", "#5FBF5F", "#5FBF7F", "#5FBF9F", "#5FBFBF", "#5F9FBF", "#5F7FBF",
        "#5F5FBF", "#7F5FBF", "#9F5FBF", "#BF5FBF", "#BF5F9F", "#BF5F7F", "#BF5F5F", "#919190", "#BF6A3F", "#BF943F",
        "#BFBF3F", "#94BF3F", "#6ABF3F", "#3FBF3F", "#3FBF6A", "#3FBF94", "#3FBFBF", "#3F94BF", "#3F6ABF", "#3F3FBF",
        "#6A3FBF", "#943FBF", "#BF3FBF", "#BF3F94", "#BF3F6A", "#BF3F3F", "#6D6D6D", "#FF5500", "#FFAA00", "#FFFF00",
        "#AAFF00", "#54FF00", "#00FF00", "#00FF54", "#00FFAA", "#00FFFF", "#00A9FF", "#0055FF", "#0000FF", "#5500FF",
        "#A900FF", "#FE00FF", "#FF00AA", "#FF0055", "#FF0000", "#484844", "#BF3F00", "#BF7F00", "#BFBF00", "#7FBF00",
        "#3FBF00", "#00BF00", "#00BF3F", "#00BF7F", "#00BFBF", "#007FBF", "#003FBF", "#0000BF", "#3F00BF", "#7F00BF",
        "#BF00BF", "#BF007F", "#BF003F", "#BF0000", "#242424", "#7F2A00", "#7F5500", "#7F7F00", "#557F00", "#2A7F00",
        "#007F00", "#007F2A", "#007F55", "#007F7F", "#00547F", "#002A7F", "#00007F", "#2A007F", "#54007F", "#7F007F",
        "#7F0055", "#7F002A", "#7F0000",
    ];

    public static uint OutfitColor(int index) =>
        0xFF000000 | Convert.ToUInt32(OutfitPaletteHex[Math.Clamp(index, 0, OutfitPaletteHex.Count - 1)][1..], 16);

    /// <summary>Default outfitter colors (head, primary, secondary, detail) used by the original.</summary>
    public static readonly int[] DefaultOutfitColors = [78, 69, 58, 76];
}
