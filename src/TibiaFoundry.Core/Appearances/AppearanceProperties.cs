using System.Collections;
using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Tibia.Protobuf.Appearances;
using Tibia.Protobuf.Shared;
using TibiaFoundry.Core.Common;

namespace TibiaFoundry.Core.Appearances;

/// <summary>
/// Builds the "Data" panel rows for an appearance, following the original
/// <c>SelectedAppearanceChanged</c>/<c>AddPropertyToList</c> rules:
/// <list type="bullet">
/// <item>rows are produced for <c>Flags</c> and every frame group, labeled "Flags.X" / "FrameGroupN.X";</item>
/// <item>Market, Animation, Light and SpriteInfo are flattened one level ("FrameGroup0.SpriteInfo.Layers");</item>
/// <item>false booleans, nulls and empty values are omitted;</item>
/// <item>market category, cloth slot, vocations, automap/light colors, sprite phases and NPC sale data get readable values.</item>
/// </list>
/// Fixed: vocations were never formatted (the code matched "RestrictToProfession", a property that
/// does not exist) and NPC currencies crashed when the currency object was missing.
/// </summary>
public static class AppearanceProperties
{
    private static readonly HashSet<string> Flattened = ["Market", "Animation", "Light", "SpriteInfo"];

    /// <param name="hideDefaults">Also hide proto3 default values (0, empty lists, enum zero) that carry no information.</param>
    public static List<PropertyRow> Build(Appearance appearance, AppearanceDatabase? database, bool hideDefaults)
    {
        var rows = new List<PropertyRow>();
        if (!string.IsNullOrEmpty(appearance.Description)) rows.Add(new PropertyRow("Appearance.Description", appearance.Description));
        if (appearance.Flags != null) AddMessage(rows, appearance.Flags, "Flags", "", database, hideDefaults);
        for (int i = 0; i < appearance.FrameGroup.Count; i++)
            AddMessage(rows, appearance.FrameGroup[i], "FrameGroup" + i, "", database, hideDefaults);
        return rows;
    }

    private static void AddMessage(List<PropertyRow> rows, IMessage message, string type, string prefix, AppearanceDatabase? db, bool hideDefaults)
    {
        foreach (var prop in message.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0 || prop.Name.StartsWith("Has", StringComparison.Ordinal)) continue;
            object? value = prop.GetValue(message);
            if (value == null || value is false) continue;
            if (Flattened.Contains(prop.Name) && value is IMessage nested)
            {
                AddMessage(rows, nested, type, prop.Name, db, hideDefaults);
                continue;
            }
            var row = Format(prop.Name, value, db, hideDefaults);
            if (row == null) continue;
            string label = type + "." + (prefix.Length > 0 ? prefix + "." : "") + prop.Name + row.Value.Suffix;
            rows.Add(new PropertyRow(label, row.Value.Text) { Swatch = row.Value.Swatch });
        }
    }

    private static (string Text, string Suffix, uint? Swatch)? Format(string name, object value, AppearanceDatabase? db, bool hideDefaults)
    {
        switch (name)
        {
            case "Category" when value is ITEM_CATEGORY category:
                return (GameTables.MarketCategoryName(category), "", null);
            case "Clothes" when value is AppearanceFlagClothes clothes:
                return (GameTables.ClothesSlotName(clothes.Slot), "", null);
            case "RestrictToVocation" when value is RepeatedField<VOCATION> vocations:
                if (vocations.Count == 0) return null;
                return (string.Join(", ", vocations.Select(GameTables.VocationName)), "", null);
            case "Automap" when value is AppearanceFlagAutomap automap:
                return ("Color: " + automap.Color, "", GameTables.AutomapColor(automap.Color));
            case "Color" when value is uint color:
                return ("Color: " + color, "", GameTables.AutomapColor(color));
            case "SpritePhase" when value is RepeatedField<SpritePhase> phases:
                return (string.Concat(phases.Select(p => $"{p.DurationMin}/{p.DurationMax} - ")), "_min/max", null);
            case "Npcsaledata" when value is RepeatedField<AppearanceFlagNPC> npcs:
                if (npcs.Count == 0) return null;
                return (FormatNpcSaleData(npcs, db), "_sell/buy", null);
        }

        if (hideDefaults && IsDefault(value)) return null;
        string text = value switch
        {
            IMessage m when m.CalculateSize() == 0 => "(set)",
            _ => value.ToString() ?? "",
        };
        return text.Length == 0 ? null : (text, "", null);
    }

    /// <summary>"name/location/sale/buy[/currency], ..." exactly like the original.</summary>
    public static string FormatNpcSaleData(IEnumerable<AppearanceFlagNPC> npcs, AppearanceDatabase? db)
    {
        var parts = npcs.Select(n =>
        {
            string currency = "";
            if (n.CurrencyObjectTypeId != 0)
                currency += "/" + (db?.Find(AppearanceKind.Object, n.CurrencyObjectTypeId)?.Name ?? n.CurrencyObjectTypeId.ToString());
            if (n.CurrencyQuestFlagDisplayName.Length > 0) currency += "/" + n.CurrencyQuestFlagDisplayName;
            return $"{n.Name}/{n.Location}/{n.SalePrice}/{n.BuyPrice}{currency}, ";
        });
        return string.Concat(parts);
    }

    private static bool IsDefault(object value) => value switch
    {
        uint u => u == 0,
        int i => i == 0,
        ulong ul => ul == 0,
        float f => f == 0,
        double d => d == 0,
        string s => s.Length == 0,
        Enum e => Convert.ToInt64(e) == 0,
        ICollection c => c.Count == 0,
        _ => false,
    };
}
