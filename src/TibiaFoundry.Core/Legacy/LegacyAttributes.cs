namespace TibiaFoundry.Core.Legacy;

/// <summary>What follows an attribute byte in the file.</summary>
public enum LegacyPayload { None, U16, TwoU16, Market }

/// <summary>
/// Byte values of the object attributes in each <see cref="LegacyDatLayout"/>. These tables are
/// the published format of the legacy clients (the same mapping third-party legacy-format readers use).
/// </summary>
public static class LegacyAttributes
{
    /// <summary>Byte that ends an object's attribute list.</summary>
    public const byte End = 0xFF;

    private static readonly LegacyAttribute[] Common860 =
    [
        LegacyAttribute.Ground, LegacyAttribute.GroundBorder, LegacyAttribute.OnBottom, LegacyAttribute.OnTop, LegacyAttribute.Container,
        LegacyAttribute.Stackable, LegacyAttribute.ForceUse, LegacyAttribute.MultiUse, LegacyAttribute.Writable, LegacyAttribute.WritableOnce,
        LegacyAttribute.FluidContainer, LegacyAttribute.Splash, LegacyAttribute.NotWalkable, LegacyAttribute.NotMoveable, LegacyAttribute.BlockProjectile,
        LegacyAttribute.NotPathable, LegacyAttribute.Pickupable, LegacyAttribute.Hangable, LegacyAttribute.HookSouth, LegacyAttribute.HookEast,
        LegacyAttribute.Rotateable, LegacyAttribute.Light, LegacyAttribute.DontHide, LegacyAttribute.Translucent, LegacyAttribute.Displacement,
        LegacyAttribute.Elevation, LegacyAttribute.LyingCorpse, LegacyAttribute.AnimateAlways, LegacyAttribute.MinimapColor, LegacyAttribute.LensHelp,
        LegacyAttribute.FullGround, LegacyAttribute.IgnoreLook, LegacyAttribute.Cloth, LegacyAttribute.Market,
    ];

    private static readonly Dictionary<LegacyDatLayout, Dictionary<byte, LegacyAttribute>> ByByte = BuildTables();
    private static readonly Dictionary<LegacyDatLayout, Dictionary<LegacyAttribute, byte>> ByAttribute =
        ByByte.ToDictionary(kv => kv.Key, kv => kv.Value.ToDictionary(p => p.Value, p => p.Key));

    private static Dictionary<LegacyDatLayout, Dictionary<byte, LegacyAttribute>> BuildTables()
    {
        var v860 = new Dictionary<byte, LegacyAttribute>();
        for (int i = 0; i < Common860.Length; i++) v860[(byte)i] = Common860[i];

        // 7.55–7.72: 8.60's first 32 attributes, with "floor change" at 23; no cloth or market.
        var v755 = v860.Where(kv => kv.Key < 32).ToDictionary(kv => kv.Key, kv => kv.Value);
        v755[23] = LegacyAttribute.FloorChange;

        // 7.80–8.54: "charges" at 8, the following attributes one higher; no cloth or market.
        var v780 = new Dictionary<byte, LegacyAttribute>();
        foreach (var (b, a) in v860.Where(kv => kv.Key < 32))
            v780[b < 8 ? b : (byte)(b + 1)] = a;
        v780[8] = LegacyAttribute.Chargeable;

        // 10.10+: "no movement animation" at 16, the following attributes one higher, then the
        // 10.x additions, the "usable" flag at 0xFE and the extended attributes of third-party legacy-format tools.
        var v1010 = new Dictionary<byte, LegacyAttribute>();
        foreach (var (b, a) in v860)
            v1010[b < 16 ? b : (byte)(b + 1)] = a;
        v1010[16] = LegacyAttribute.NoMoveAnimation;
        v1010[35] = LegacyAttribute.DefaultAction;
        v1010[36] = LegacyAttribute.Wrappable;
        v1010[37] = LegacyAttribute.Unwrappable;
        v1010[38] = LegacyAttribute.TopEffect;
        v1010[40] = LegacyAttribute.ChangedToExpire;
        v1010[41] = LegacyAttribute.Corpse;
        v1010[42] = LegacyAttribute.PlayerCorpse;
        v1010[43] = LegacyAttribute.CyclopediaItem;
        v1010[44] = LegacyAttribute.Ammo;
        v1010[45] = LegacyAttribute.ShowOffSocket;
        v1010[46] = LegacyAttribute.Reportable;
        v1010[47] = LegacyAttribute.UpgradeClassification;
        v1010[48] = LegacyAttribute.WearOut;
        v1010[49] = LegacyAttribute.ClockExpire;
        v1010[50] = LegacyAttribute.Expire;
        v1010[51] = LegacyAttribute.ExpireStop;
        v1010[0xFE] = LegacyAttribute.Usable;

        // 7.40–7.50: no ground border; "multi use" and "force use" are swapped; light, floor change,
        // full ground, elevation and displacement come earlier.
        var v740 = new Dictionary<byte, LegacyAttribute>
        {
            [0] = LegacyAttribute.Ground, [1] = LegacyAttribute.OnBottom, [2] = LegacyAttribute.OnTop, [3] = LegacyAttribute.Container,
            [4] = LegacyAttribute.Stackable, [5] = LegacyAttribute.MultiUse, [6] = LegacyAttribute.ForceUse, [7] = LegacyAttribute.Writable,
            [8] = LegacyAttribute.WritableOnce, [9] = LegacyAttribute.FluidContainer, [10] = LegacyAttribute.Splash, [11] = LegacyAttribute.NotWalkable,
            [12] = LegacyAttribute.NotMoveable, [13] = LegacyAttribute.BlockProjectile, [14] = LegacyAttribute.NotPathable, [15] = LegacyAttribute.Pickupable,
            [16] = LegacyAttribute.Light, [17] = LegacyAttribute.FloorChange, [18] = LegacyAttribute.FullGround, [19] = LegacyAttribute.Elevation,
            [20] = LegacyAttribute.Displacement, [22] = LegacyAttribute.MinimapColor, [23] = LegacyAttribute.Rotateable, [24] = LegacyAttribute.LyingCorpse,
            [25] = LegacyAttribute.Hangable, [26] = LegacyAttribute.HookSouth, [27] = LegacyAttribute.HookEast, [28] = LegacyAttribute.AnimateAlways,
        };

        return new()
        {
            [LegacyDatLayout.V740] = v740,
            [LegacyDatLayout.V755] = v755,
            [LegacyDatLayout.V780] = v780,
            [LegacyDatLayout.V860] = v860,
            [LegacyDatLayout.V1010] = v1010,
        };
    }

    /// <summary>The attribute a byte stands for in a layout, or null when the byte is not defined there.</summary>
    public static LegacyAttribute? FromByte(LegacyDatLayout layout, byte value) => ByByte[layout].TryGetValue(value, out var a) ? a : null;

    /// <summary>The byte of an attribute in a layout, or null when the layout cannot store it.</summary>
    public static byte? ToByte(LegacyDatLayout layout, LegacyAttribute attribute) => ByAttribute[layout].TryGetValue(attribute, out var b) ? b : null;

    public static LegacyPayload PayloadOf(LegacyAttribute attribute, LegacyDatLayout layout = LegacyDatLayout.V1010) => attribute switch
    {
        LegacyAttribute.Ground or LegacyAttribute.Writable or LegacyAttribute.WritableOnce or LegacyAttribute.Elevation or LegacyAttribute.MinimapColor
            or LegacyAttribute.LensHelp or LegacyAttribute.Cloth or LegacyAttribute.DefaultAction or LegacyAttribute.ChangedToExpire
            or LegacyAttribute.CyclopediaItem or LegacyAttribute.UpgradeClassification => LegacyPayload.U16,
        LegacyAttribute.Light => LegacyPayload.TwoU16,
        // 7.40–7.50 store no offset: the client always uses 8, 8.
        LegacyAttribute.Displacement => layout == LegacyDatLayout.V740 ? LegacyPayload.None : LegacyPayload.TwoU16,
        LegacyAttribute.Market => LegacyPayload.Market,
        _ => LegacyPayload.None,
    };

    /// <summary>True for the extended attributes (third-party legacy-format tools) that CipSoft clients do not read.</summary>
    public static bool IsExtension(LegacyAttribute attribute) => attribute >= LegacyAttribute.ChangedToExpire;
}
