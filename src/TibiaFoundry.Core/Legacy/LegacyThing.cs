namespace TibiaFoundry.Core.Legacy;

/// <summary>Object categories of a legacy Tibia.dat, in file order.</summary>
public enum LegacyCategory { Item, Outfit, Effect, Missile }

/// <summary>
/// Object attributes of a legacy Tibia.dat, independent of the client version (each
/// <see cref="LegacyDatLayout"/> maps them to its own byte values).
/// </summary>
public enum LegacyAttribute
{
    Ground, GroundBorder, OnBottom, OnTop, Container, Stackable, ForceUse, MultiUse, Writable, WritableOnce,
    FluidContainer, Splash, NotWalkable, NotMoveable, BlockProjectile, NotPathable, NoMoveAnimation, Pickupable, Hangable,
    HookSouth, HookEast, Rotateable, Light, DontHide, Translucent, Displacement, Elevation, LyingCorpse, AnimateAlways,
    MinimapColor, LensHelp, FullGround, IgnoreLook, Cloth, Market, DefaultAction, Wrappable, Unwrappable, TopEffect,

    /// <summary>"Usable" (byte 0xFE in the 10.10 layout).</summary>
    Usable,
    /// <summary>"Charges" (7.80–8.54 only).</summary>
    Chargeable,
    /// <summary>"Floor change" (7.40–7.72 only).</summary>
    FloorChange,

    // Extended attributes read by some third-party legacy-format tools (10.10 layout, bytes 39–51). They are not part
    // of CipSoft's formats; ClientConverter writes them when the matching options are selected.
    ChangedToExpire, Corpse, PlayerCorpse, CyclopediaItem, Ammo, ShowOffSocket, Reportable, UpgradeClassification,
    WearOut, ClockExpire, Expire, ExpireStop,
}

/// <summary>Market data of a legacy object (attribute Market).</summary>
/// <param name="Name">Name bytes as stored (Latin-1).</param>
public sealed record LegacyMarket(ushort Category, ushort TradeAs, ushort ShowAs, byte[] Name, ushort Vocation, ushort Level)
{
    public string NameText => System.Text.Encoding.Latin1.GetString(Name);

    public bool Equals(LegacyMarket? other) => other != null && Category == other.Category && TradeAs == other.TradeAs && ShowAs == other.ShowAs &&
                                              Name.AsSpan().SequenceEqual(other.Name) && Vocation == other.Vocation && Level == other.Level;

    public override int GetHashCode() => HashCode.Combine(Category, TradeAs, ShowAs, Name.Length, Vocation, Level);
}

/// <summary>One attribute with its value (A and B are the 16-bit values; Market for the market attribute).</summary>
public readonly record struct LegacyAttributeValue(LegacyAttribute Attribute, ushort A = 0, ushort B = 0, LegacyMarket? Market = null)
{
    public override string ToString() => LegacyAttributes.PayloadOf(Attribute) switch
    {
        LegacyPayload.None => Attribute.ToString(),
        LegacyPayload.U16 => $"{Attribute}({A})",
        LegacyPayload.TwoU16 => $"{Attribute}({A}, {B})",
        _ => $"{Attribute}({Market?.NameText})",
    };
}

/// <summary>Animation timing of a frame group (clients with enhanced animations).</summary>
public sealed class LegacyAnimation
{
    /// <summary>True when all objects of this type animate in step (stored as 1); false = each on its own (0).</summary>
    public bool Synchronized { get; set; }

    /// <summary>0 = forever, -1 = back and forth (ping-pong), n &gt; 0 = n times.</summary>
    public int LoopCount { get; set; }

    /// <summary>Phase the animation starts with; -1 = a random phase.</summary>
    public sbyte StartPhase { get; set; }

    /// <summary>Minimum and maximum duration of each phase in milliseconds.</summary>
    public List<(uint Min, uint Max)> Durations { get; set; } = [];

    public bool SameAs(LegacyAnimation? other) => other != null && Synchronized == other.Synchronized && LoopCount == other.LoopCount &&
                                                  StartPhase == other.StartPhase && Durations.SequenceEqual(other.Durations);
}

/// <summary>A frame group: size, patterns, animation and sprite IDs.</summary>
public sealed class LegacyFrameGroup
{
    /// <summary>0 = idle, 1 = moving (only stored for outfits in clients with frame groups).</summary>
    public byte Type { get; set; }
    public byte Width { get; set; } = 1;
    public byte Height { get; set; } = 1;
    /// <summary>Real size in pixels; stored only when width or height is larger than 1.</summary>
    public byte ExactSize { get; set; } = 32;
    public byte Layers { get; set; } = 1;
    public byte PatternX { get; set; } = 1;
    public byte PatternY { get; set; } = 1;
    public byte PatternZ { get; set; } = 1;
    public byte Phases { get; set; } = 1;
    public LegacyAnimation? Animation { get; set; }
    public uint[] Sprites { get; set; } = [0];

    public int SpriteCount => Width * Height * Layers * PatternX * PatternY * PatternZ * Phases;

    public bool SameAs(LegacyFrameGroup other) =>
        Type == other.Type && Width == other.Width && Height == other.Height && (Width == 1 && Height == 1 || ExactSize == other.ExactSize) &&
        Layers == other.Layers && PatternX == other.PatternX && PatternY == other.PatternY && PatternZ == other.PatternZ && Phases == other.Phases &&
        (Animation == null ? other.Animation == null : Animation.SameAs(other.Animation)) && Sprites.AsSpan().SequenceEqual(other.Sprites);
}

/// <summary>One object (item, outfit, effect or missile) of a legacy Tibia.dat.</summary>
public sealed class LegacyThing
{
    public LegacyThing(LegacyCategory category, uint id)
    {
        Category = category;
        Id = id;
    }

    public LegacyCategory Category { get; }
    public uint Id { get; }

    /// <summary>Attributes in file order.</summary>
    public List<LegacyAttributeValue> Attributes { get; } = [];

    public List<LegacyFrameGroup> FrameGroups { get; } = [];

    public bool Has(LegacyAttribute attribute) => Attributes.Any(a => a.Attribute == attribute);

    public LegacyAttributeValue? Get(LegacyAttribute attribute)
    {
        foreach (var a in Attributes)
            if (a.Attribute == attribute) return a;
        return null;
    }

    /// <summary>An object without attributes and with one empty 1×1 sprite (fills unused IDs).</summary>
    public static LegacyThing Blank(LegacyCategory category, uint id, bool withFrameGroups)
    {
        var thing = new LegacyThing(category, id);
        thing.FrameGroups.Add(new LegacyFrameGroup());
        return thing;
    }

    public bool SameAs(LegacyThing other) =>
        Category == other.Category && Id == other.Id && Attributes.SequenceEqual(other.Attributes) &&
        FrameGroups.Count == other.FrameGroups.Count && FrameGroups.Zip(other.FrameGroups).All(p => p.First.SameAs(p.Second));
}

/// <summary>Contents of a legacy Tibia.dat.</summary>
public sealed class LegacyDatFile
{
    public uint Signature { get; set; }
    public List<LegacyThing> Items { get; } = [];
    public List<LegacyThing> Outfits { get; } = [];
    public List<LegacyThing> Effects { get; } = [];
    public List<LegacyThing> Missiles { get; } = [];

    public List<LegacyThing> List(LegacyCategory category) => category switch
    {
        LegacyCategory.Item => Items,
        LegacyCategory.Outfit => Outfits,
        LegacyCategory.Effect => Effects,
        _ => Missiles,
    };

    public IEnumerable<LegacyThing> All => Items.Concat(Outfits).Concat(Effects).Concat(Missiles);

    /// <summary>First ID of a category (items start at 100).</summary>
    public static uint FirstId(LegacyCategory category) => category == LegacyCategory.Item ? 100u : 1u;

    public LegacyThing? Find(LegacyCategory category, uint id)
    {
        var list = List(category);
        long index = (long)id - FirstId(category);
        return index >= 0 && index < list.Count ? list[(int)index] : null;
    }

    /// <summary>Highest sprite ID referenced by any object.</summary>
    public uint MaxSpriteId => All.SelectMany(t => t.FrameGroups).SelectMany(g => g.Sprites).DefaultIfEmpty(0u).Max();
}
