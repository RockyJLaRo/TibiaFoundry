using System.Collections;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using TibiaFoundry.Core.Editing;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>How a property row is shown.</summary>
public enum PropertyNodeKind { Header, Bool, Number, Enum, Text, Presence, RepeatedValues, ListHeader, ItemHeader, Note }

/// <summary>A named value of an enum field.</summary>
public sealed record EnumOption(int Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// One row of the Assets Editor's property list, bound to one field of a protobuf message. Rows are
/// built from the message descriptors, so every field of the client's format can be edited —
/// including fields added in later versions of the schema.
/// </summary>
public sealed partial class PropertyNode : ObservableObject
{
    private readonly ProtoPropertyEditor _editor;

    internal PropertyNode(ProtoPropertyEditor editor, PropertyNodeKind kind, string label, int depth, IMessage? owner = null, FieldDescriptor? field = null, int index = -1)
    {
        _editor = editor;
        Kind = kind;
        Label = label;
        Depth = depth;
        Owner = owner;
        Field = field;
        Index = index;
    }

    public PropertyNodeKind Kind { get; }
    public string Label { get; }
    public int Depth { get; }
    public string Help { get; init; } = "";
    public double Indent => Depth * 16;
    internal IMessage? Owner { get; }
    internal FieldDescriptor? Field { get; }
    internal int Index { get; }
    public IReadOnlyList<EnumOption>? Options { get; init; }
    public bool IsReadOnly { get; init; }
    public bool CanRemove { get; init; }
    public bool CanAdd { get; init; }

    [ObservableProperty] private string? _error;

    // ------------------------------------------------------------------ values

    public bool BoolValue
    {
        get => Field != null && Owner != null && (Kind == PropertyNodeKind.Presence
            ? Field.Accessor.HasValue(Owner)
            : Field.Accessor.HasValue(Owner) && (bool)Field.Accessor.GetValue(Owner));
        set
        {
            if (Field == null || Owner == null || IsReadOnly) return;
            if (Kind == PropertyNodeKind.Presence)
            {
                if (value == Field.Accessor.HasValue(Owner)) return;
                if (value) Field.Accessor.SetValue(Owner, Field.MessageType.Parser.ParseFrom(ByteString.Empty));
                else Field.Accessor.Clear(Owner);
                _editor.Changed(structural: true);
                return;
            }
            // A flag is stored only while it is set, as the client does.
            if (value) Field.Accessor.SetValue(Owner, true);
            else Field.Accessor.Clear(Owner);
            OnPropertyChanged();
            _editor.Changed(structural: false);
        }
    }

    public string TextValue
    {
        get
        {
            if (Field == null || Owner == null) return "";
            if (Kind == PropertyNodeKind.RepeatedValues)
                return string.Join(", ", ((IList)Field.Accessor.GetValue(Owner)).Cast<object>().Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));
            if (!Field.Accessor.HasValue(Owner)) return "";
            object raw = Field.Accessor.GetValue(Owner);
            return raw is ByteString bytes ? EditableAppearanceExtensions.ClientText.GetString(bytes.Span) : Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "";
        }
        set
        {
            if (Field == null || Owner == null || IsReadOnly) return;
            string text = value?.Trim() ?? "";
            try
            {
                if (Kind == PropertyNodeKind.RepeatedValues)
                {
                    var list = (IList)Field.Accessor.GetValue(Owner);
                    var parsed = text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(t => Parse(t)).ToList();
                    list.Clear();
                    foreach (var v in parsed) list.Add(v);
                }
                else if (text.Length == 0)
                {
                    Field.Accessor.Clear(Owner);
                }
                else if (Field.FieldType == FieldType.Bytes)
                {
                    Field.Accessor.SetValue(Owner, EditableAppearanceExtensions.EncodeClientText(value!)
                        ?? throw new FormatException("The client stores text as Latin-1: this text contains characters it cannot show."));
                }
                else
                {
                    Field.Accessor.SetValue(Owner, Parse(text));
                }
                Error = null;
                _editor.Changed(structural: false);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                Error = ex is OverflowException ? L.T("ProtoPropertyEditor.TheNumberIsOutOfRange") : ex.Message;
            }
            OnPropertyChanged();
        }
    }

    private object Parse(string text) => Field!.FieldType switch
    {
        FieldType.UInt32 => uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
        FieldType.Int32 => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
        FieldType.UInt64 => ulong.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
        FieldType.Int64 => long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
        _ => throw new FormatException("This field cannot be edited as text."),
    };

    public EnumOption? SelectedOption
    {
        get
        {
            if (Field == null || Owner == null || !Field.Accessor.HasValue(Owner)) return Options?.FirstOrDefault(o => o.Value == int.MinValue);
            int v = Convert.ToInt32(Field.Accessor.GetValue(Owner), CultureInfo.InvariantCulture);
            return Options?.FirstOrDefault(o => o.Value == v) ?? new EnumOption(v, L.F("ProtoPropertyEditor.0Unknown", v));
        }
        set
        {
            if (Field == null || Owner == null || value == null || IsReadOnly) return;
            if (value.Value == int.MinValue) Field.Accessor.Clear(Owner);
            else Field.Accessor.SetValue(Owner, value.Value);
            OnPropertyChanged();
            _editor.Changed(structural: false);
        }
    }

    [RelayCommand]
    private void Add()
    {
        if (Field == null || Owner == null) return;
        ((IList)Field.Accessor.GetValue(Owner)).Add(Field.MessageType.Parser.ParseFrom(ByteString.Empty));
        _editor.Changed(structural: true);
    }

    [RelayCommand]
    private void Remove()
    {
        if (Field == null || Owner == null || Index < 0) return;
        var list = (IList)Field.Accessor.GetValue(Owner);
        if (Index < list.Count) list.RemoveAt(Index);
        _editor.Changed(structural: true);
    }
}

/// <summary>
/// Builds <see cref="PropertyNode"/> rows for an editable appearance. Fields that must change
/// together with the sprite list (layers, patterns, sprite IDs, number of animation phases) are
/// edited in the sprite layout panel instead, so the data always stays consistent.
/// </summary>
public sealed class ProtoPropertyEditor(Action<bool> onChanged)
{
    /// <summary>Fields edited in the layout panel, not as rows.</summary>
    private static readonly HashSet<string> LayoutFields = ["pattern_width", "pattern_height", "pattern_depth", "layers", "sprite_id"];

    internal void Changed(bool structural) => onChanged(structural);

    public List<PropertyNode> Build(Core.Editing.Model.EditableAppearance appearance)
    {
        var rows = new List<PropertyNode>();
        var d = Core.Editing.Model.EditableAppearance.Descriptor;
        rows.Add(new PropertyNode(this, PropertyNodeKind.Header, L.T("AppearanceKind.Object"), 0));
        rows.Add(new PropertyNode(this, PropertyNodeKind.Number, "ID", 1, appearance, d.FindFieldByName("id")) { IsReadOnly = true, Help = L.T("ProtoPropertyEditor.ChangeItWithChangeIDIDs") });
        rows.Add(new PropertyNode(this, PropertyNodeKind.Text, L.T("Common.Name"), 1, appearance, d.FindFieldByName("name")) { Help = L.T("ProtoPropertyEditor.ShownInTheClientLatin1") });
        rows.Add(new PropertyNode(this, PropertyNodeKind.Text, L.T("Common.Description"), 1, appearance, d.FindFieldByName("description")));
        appearance.Flags ??= new Core.Editing.Model.AppearanceFlags();
        rows.Add(new PropertyNode(this, PropertyNodeKind.Header, L.T("AppearanceFilter.Flags"), 0));
        AddFields(rows, appearance.Flags, 1);
        for (int g = 0; g < appearance.FrameGroup.Count; g++)
        {
            var group = appearance.FrameGroup[g];
            string type = group.FixedFrameGroup switch { 0 => "idle", 1 => "moving", 2 => "object", _ => L.F("ProtoPropertyEditor.Type0", group.FixedFrameGroup) };
            rows.Add(new PropertyNode(this, PropertyNodeKind.Header, L.F("ProtoPropertyEditor.FrameGroup01", g + 1, type), 0));
            AddFields(rows, group, 1);
        }
        return rows;
    }

    private void AddFields(List<PropertyNode> rows, IMessage message, int depth)
    {
        foreach (var field in message.Descriptor.Fields.InDeclarationOrder())
        {
            if (LayoutFields.Contains(field.Name)) continue;
            string label = PropertyLabels.Label(field.Name);
            string help = PropertyLabels.Help(field.Name);
            var options = EnumOptions(field);
            if (field.IsRepeated)
            {
                if (field.FieldType == FieldType.Message)
                {
                    var list = (IList)field.Accessor.GetValue(message);
                    bool fixedCount = field.Name == "sprite_phase";
                    rows.Add(new PropertyNode(this, PropertyNodeKind.ListHeader, $"{label} ({list.Count})", depth, message, field) { CanAdd = !fixedCount, Help = fixedCount ? L.T("ProtoPropertyEditor.OneEntryPerAnimationPhaseChange") : help });
                    for (int i = 0; i < list.Count; i++)
                    {
                        rows.Add(new PropertyNode(this, PropertyNodeKind.ItemHeader, $"{label} {i + 1}", depth + 1, message, field, i) { CanRemove = !fixedCount });
                        AddFields(rows, (IMessage)list[i]!, depth + 2);
                    }
                }
                else
                {
                    string listHelp = options != null ? help + " Values: " + string.Join(", ", options.Where(o => o.Value != int.MinValue).Select(o => $"{o.Value} = {o.Label}")) : help;
                    rows.Add(new PropertyNode(this, PropertyNodeKind.RepeatedValues, label, depth, message, field) { Help = (listHelp + L.T("ProtoPropertyEditor.SeparateValuesWithCommas")).Trim() });
                }
                continue;
            }
            switch (field.FieldType)
            {
                case FieldType.Message:
                    rows.Add(new PropertyNode(this, PropertyNodeKind.Presence, label, depth, message, field) { Help = help });
                    if (field.Accessor.HasValue(message)) AddFields(rows, (IMessage)field.Accessor.GetValue(message), depth + 1);
                    break;
                case FieldType.Bool:
                    rows.Add(new PropertyNode(this, PropertyNodeKind.Bool, label, depth, message, field) { Help = help });
                    break;
                case FieldType.Bytes or FieldType.String:
                    rows.Add(new PropertyNode(this, PropertyNodeKind.Text, label, depth, message, field) { Help = help });
                    break;
                default:
                    rows.Add(options != null
                        ? new PropertyNode(this, PropertyNodeKind.Enum, label, depth, message, field) { Options = options, Help = help }
                        : new PropertyNode(this, PropertyNodeKind.Number, label, depth, message, field) { Help = help });
                    break;
            }
        }
    }

    /// <summary>Named values for the int32 fields that hold one of the client's enums.</summary>
    private static IReadOnlyList<EnumOption>? EnumOptions(FieldDescriptor field)
    {
        EnumDescriptor? e = (field.ContainingType.Name, field.Name) switch
        {
            ("FlagHook", "direction") => Tibia.Protobuf.Shared.SharedReflection.Descriptor.FindTypeByName<EnumDescriptor>("HOOK_TYPE"),
            ("FlagDefaultAction", "action") => Tibia.Protobuf.Shared.SharedReflection.Descriptor.FindTypeByName<EnumDescriptor>("PLAYER_ACTION"),
            ("FlagMarket", "category") => Tibia.Protobuf.Shared.SharedReflection.Descriptor.FindTypeByName<EnumDescriptor>("ITEM_CATEGORY"),
            ("AppearanceFlags", "weapon_type") => Tibia.Protobuf.Shared.SharedReflection.Descriptor.FindTypeByName<EnumDescriptor>("WEAPON_TYPE"),
            ("AppearanceFlags", "restrict_to_vocation") => Tibia.Protobuf.Shared.SharedReflection.Descriptor.FindTypeByName<EnumDescriptor>("VOCATION"),
            ("SpriteAnimation", "loop_type") => Tibia.Protobuf.Shared.SharedReflection.Descriptor.FindTypeByName<EnumDescriptor>("ANIMATION_LOOP_TYPE"),
            ("FrameGroup", "fixed_frame_group") => Tibia.Protobuf.Appearances.AppearancesReflection.Descriptor.FindTypeByName<EnumDescriptor>("FIXED_FRAME_GROUP"),
            _ => null,
        };
        if (e == null) return null;
        string prefix = e.Name + "_";
        var list = new List<EnumOption> { new(int.MinValue, L.T("ProtoPropertyEditor.NotSet")) };
        list.AddRange(e.Values.Select(v => new EnumOption(v.Number, PropertyLabels.Humanize(v.Name.StartsWith(prefix, StringComparison.Ordinal) ? v.Name[prefix.Length..] : v.Name))));
        return list;
    }
}

/// <summary>Readable names and help texts for the fields of the appearances format.</summary>
public static class PropertyLabels
{
    private static readonly Dictionary<string, (string Label, string Help)> Known = new(StringComparer.Ordinal)
    {
        ["bank"] = ("Ground", L.T("ProtoPropertyEditor.TheObjectIsAGroundTile")),
        ["waypoints"] = (L.T("ProtoPropertyEditor.SpeedWaypoints"), L.T("ProtoPropertyEditor.WalkingSpeedOnThisGroundLower")),
        ["clip"] = (L.T("ProtoPropertyEditor.GroundBorder"), L.T("ProtoPropertyEditor.DrawnOnTopOfGroundsBelow")),
        ["bottom"] = (L.T("ProtoPropertyEditor.OnBottom"), L.T("ProtoPropertyEditor.DrawnBelowOtherObjectsOnThe")),
        ["top"] = (L.T("ProtoPropertyEditor.OnTop"), L.T("ProtoPropertyEditor.DrawnAboveCreaturesDoorFramesFor")),
        ["container"] = ("Container", L.T("ProtoPropertyEditor.CanHoldOtherItems")),
        ["cumulative"] = ("Stackable", L.T("ProtoPropertyEditor.SeveralItemsStackIntoOneWith")),
        ["usable"] = ("Usable", ""),
        ["forceuse"] = (L.T("ProtoPropertyEditor.ForceUse"), L.T("ProtoPropertyEditor.UsedWithAClickInsteadOf")),
        ["multiuse"] = ("Multi-use", L.T("ProtoPropertyEditor.UsedOnATargetRunesTools")),
        ["write"] = ("Writable", L.T("ProtoPropertyEditor.TextCanBeWrittenOnIt")),
        ["max_text_length"] = (L.T("ProtoPropertyEditor.MaximumTextLength"), ""),
        ["write_once"] = (L.T("ProtoPropertyEditor.WritableOnce"), L.T("ProtoPropertyEditor.TextCanBeWrittenOnIt2")),
        ["max_text_length_once"] = (L.T("ProtoPropertyEditor.MaximumTextLength"), ""),
        ["liquidpool"] = (L.T("ProtoPropertyEditor.LiquidPool"), L.T("ProtoPropertyEditor.ASplashItsColorComesFrom")),
        ["unpass"] = (L.T("ProtoPropertyEditor.NotWalkable"), L.T("ProtoPropertyEditor.CreaturesCannotWalkOntoThisTile")),
        ["unmove"] = (L.T("ProtoPropertyEditor.NotMovable"), L.T("ProtoPropertyEditor.CannotBeMoved")),
        ["unsight"] = (L.T("ProtoPropertyEditor.BlocksSightAndMissiles"), ""),
        ["avoid"] = (L.T("ProtoPropertyEditor.AvoidedByPathfinding"), L.T("ProtoPropertyEditor.WalkableButAutoWalkGoesAround")),
        ["no_movement_animation"] = (L.T("ProtoPropertyEditor.NoMovementAnimation"), ""),
        ["take"] = ("Pickupable", L.T("ProtoPropertyEditor.CanBePickedUp")),
        ["liquidcontainer"] = (L.T("ProtoPropertyEditor.LiquidContainer"), L.T("ProtoPropertyEditor.CanBeFilledWithALiquid")),
        ["hang"] = ("Hangable", L.T("ProtoPropertyEditor.CanBeHungOnWalls")),
        ["hook"] = (L.T("ProtoPropertyEditor.Field.Hook"), L.T("ProtoPropertyEditor.AWallAHangableObjectCan")),
        ["direction"] = (L.T("AssetsEditor.Direction"), ""),
        ["rotate"] = ("Rotatable", L.T("ProtoPropertyEditor.CanBeTurnedFurniture")),
        ["light"] = (L.T("ProtoPropertyEditor.LightSource"), ""),
        ["brightness"] = (L.T("ProtoPropertyEditor.Field.Brightness"), L.T("ProtoPropertyEditor.LightRadius")),
        ["color"] = (L.T("ProtoPropertyEditor.Field.Color"), L.T("ProtoPropertyEditor.IndexInTheClientS216")),
        ["dont_hide"] = (L.T("ProtoPropertyEditor.NeverHidden"), L.T("ProtoPropertyEditor.StaysVisibleWhenTheFloorsAbove")),
        ["translucent"] = ("Translucent", ""),
        ["shift"] = ("Displacement", L.T("ProtoPropertyEditor.DrawnThisManyPixelsToThe")),
        ["height"] = ("Elevation", L.T("ProtoPropertyEditor.ObjectsOnTopAreDrawnThis")),
        ["elevation"] = (L.T("ProtoPropertyEditor.ElevationPixels"), ""),
        ["lying_object"] = (L.T("ProtoPropertyEditor.LyingObject"), L.T("ProtoPropertyEditor.LiesOnTheGroundLikeA")),
        ["animate_always"] = (L.T("ProtoPropertyEditor.AlwaysAnimated"), L.T("ProtoPropertyEditor.AnimatesEvenWhenNotInUse")),
        ["automap"] = (L.T("ProtoPropertyEditor.MinimapColor"), L.T("ProtoPropertyEditor.ShownOnTheMinimapWithThis")),
        ["lenshelp"] = (L.T("ProtoPropertyEditor.LensHelp"), L.T("ProtoPropertyEditor.ExplanationShownByTheHelpLens")),
        ["fullbank"] = (L.T("ProtoPropertyEditor.FullGround"), L.T("ProtoPropertyEditor.TheGroundFillsTheWholeTile")),
        ["ignore_look"] = (L.T("ProtoPropertyEditor.IgnoredByLook"), L.T("ProtoPropertyEditor.LookingAtTheTileLooksAt")),
        ["clothes"] = (L.T("ProtoPropertyEditor.EquipmentSlot"), ""),
        ["slot"] = ("Slot", L.T("ProtoPropertyEditor.EquipmentSlotNumber")),
        ["default_action"] = (L.T("ProtoPropertyEditor.DefaultAction"), L.T("ProtoPropertyEditor.WhatHappensOnALeftClick")),
        ["market"] = ("Market", L.T("ProtoPropertyEditor.CanBeTradedInTheMarket")),
        ["category"] = (L.T("Common.Category"), ""),
        ["trade_as_object_id"] = (L.T("ProtoPropertyEditor.TradeAs"), L.T("ProtoPropertyEditor.ObjectIDUsedInTheMarket")),
        ["show_as_object_id"] = (L.T("ProtoPropertyEditor.ShowAs"), L.T("ProtoPropertyEditor.ObjectIDWhosePictureTheMarket")),
        ["wrap"] = ("Wrappable", L.T("ProtoPropertyEditor.FurnitureThatCanBeWrappedInto")),
        ["unwrap"] = ("Unwrappable", ""),
        ["topeffect"] = (L.T("ProtoPropertyEditor.TopEffect"), L.T("ProtoPropertyEditor.DrawnAboveEverythingElse")),
        ["npcsaledata"] = (L.T("ProtoPropertyEditor.NPCTrade"), L.T("ProtoPropertyEditor.NPCsThatBuyOrSellThis")),
        ["location"] = ("Location", ""),
        ["sale_price"] = (L.T("ProtoPropertyEditor.SalePrice"), ""),
        ["buy_price"] = (L.T("ProtoPropertyEditor.BuyPrice"), ""),
        ["currency_object_type_id"] = (L.T("ProtoPropertyEditor.CurrencyObject"), ""),
        ["currency_quest_flag_display_name"] = (L.T("ProtoPropertyEditor.CurrencyName"), ""),
        ["changedtoexpire"] = (L.T("ProtoPropertyEditor.ChangedToExpire"), L.T("ProtoPropertyEditor.ThisObjectIsTheExpiringForm")),
        ["former_object_typeid"] = (L.T("ProtoPropertyEditor.FormerObject"), ""),
        ["corpse"] = (L.T("ClientConverter.Corpse"), ""),
        ["player_corpse"] = (L.T("ProtoPropertyEditor.PlayerCorpse"), ""),
        ["cyclopediaitem"] = (L.T("ProtoPropertyEditor.CyclopediaItem"), ""),
        ["cyclopedia_type"] = (L.T("ProtoPropertyEditor.CyclopediaType"), L.T("ProtoPropertyEditor.ObjectShownInTheCyclopedia")),
        ["ammo"] = (L.T("ClientConverter.Ammunition"), ""),
        ["show_off_socket"] = (L.T("ProtoPropertyEditor.ShowOffSocket"), ""),
        ["reportable"] = (L.T("ClientConverter.Reportable"), ""),
        ["upgradeclassification"] = (L.T("ProtoPropertyEditor.UpgradeClassification"), ""),
        ["upgrade_classification"] = (L.T("AppearanceFilter.Classification"), ""),
        ["reverse_addons_east"] = (L.T("ProtoPropertyEditor.ReverseAddonsEast"), ""),
        ["reverse_addons_west"] = (L.T("ProtoPropertyEditor.ReverseAddonsWest"), ""),
        ["reverse_addons_south"] = (L.T("ProtoPropertyEditor.ReverseAddonsSouth"), ""),
        ["reverse_addons_north"] = (L.T("ProtoPropertyEditor.ReverseAddonsNorth"), ""),
        ["wearout"] = (L.T("ProtoPropertyEditor.WearsOut"), ""),
        ["clockexpire"] = (L.T("ProtoPropertyEditor.ClockExpire"), ""),
        ["expire"] = ("Expires", ""),
        ["expirestop"] = (L.T("ProtoPropertyEditor.ExpiryStops"), ""),
        ["deco_item_kit"] = (L.T("ProtoPropertyEditor.DecorationKit"), ""),
        ["skillwheel_gem"] = (L.T("ProtoPropertyEditor.SkillWheelGem"), ""),
        ["gem_quality_id"] = (L.T("ProtoPropertyEditor.GemQuality"), ""),
        ["vocation_id"] = (L.T("AppearanceFilter.Vocation"), ""),
        ["dual_wielding"] = (L.T("ProtoPropertyEditor.DualWielding"), ""),
        ["imbueable"] = (L.T("ProtoPropertyEditor.ImbuementSlots"), ""),
        ["slot_count"] = ("Slots", ""),
        ["proficiency"] = ("Proficiency", ""),
        ["proficiency_id"] = (L.T("ProtoPropertyEditor.ProficiencyID"), ""),
        ["restrict_to_vocation"] = ("Vocations", L.T("ProtoPropertyEditor.OnlyTheseVocationsCanUseIt")),
        ["minimum_level"] = (L.T("ProtoPropertyEditor.MinimumLevel"), ""),
        ["weapon_type"] = (L.T("ProtoPropertyEditor.WeaponType"), ""),
        ["fixed_frame_group"] = (L.T("ProtoPropertyEditor.FrameGroupType"), L.T("ProtoPropertyEditor.IdleAndMovingForOutfitsObject")),
        ["sprite_info"] = (L.T("Common.Sprites"), ""),
        ["bounding_square"] = (L.T("ProtoPropertyEditor.BoundingSquare"), L.T("ProtoPropertyEditor.SizeOfTheSquareTheGraphic")),
        ["is_opaque"] = ("Opaque", L.T("ProtoPropertyEditor.TheSpritesHaveNoTransparentPixels")),
        ["bounding_box_per_direction"] = (L.T("ProtoPropertyEditor.BoundingBoxPerDirection"), ""),
        ["animation"] = (L.T("AssetLibrary.Animation"), ""),
        ["default_start_phase"] = (L.T("ProtoPropertyEditor.StartPhase"), ""),
        ["synchronized"] = ("Synchronized", L.T("ProtoPropertyEditor.AllObjectsOfThisTypeAnimate")),
        ["random_start_phase"] = (L.T("ProtoPropertyEditor.RandomStartPhase"), ""),
        ["loop_type"] = ("Loop", ""),
        ["loop_count"] = (L.T("ProtoPropertyEditor.LoopCount"), L.T("ProtoPropertyEditor.UsedWithCounted")),
        ["sprite_phase"] = ("Phase", ""),
        ["duration_min"] = (L.T("ProtoPropertyEditor.MinimumDurationMs"), ""),
        ["duration_max"] = (L.T("ProtoPropertyEditor.MaximumDurationMs"), ""),
    };

    public static string Label(string field) => Known.TryGetValue(field, out var k) ? k.Label : Humanize(field);

    public static string Help(string field) => Known.TryGetValue(field, out var k) ? k.Help : "";

    public static string Humanize(string name)
    {
        string s = name.Replace('_', ' ').ToLowerInvariant();
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }
}
