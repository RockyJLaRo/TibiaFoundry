using TibiaFoundry.Core.Localization;
namespace TibiaFoundry.Core.Legacy;

/// <summary>
/// Attribute numbering of a legacy Tibia.dat. The byte values of the object attributes changed a
/// few times between client versions; within one layout they are fixed.
/// </summary>
public enum LegacyDatLayout
{
    /// <summary>7.40–7.50: no ground border, several attributes in other places, no pattern Z.</summary>
    V740,
    /// <summary>7.55–7.72: attribute 23 is "floor change".</summary>
    V755,
    /// <summary>7.80–8.54: attribute 8 is "charges"; later ones shifted by one.</summary>
    V780,
    /// <summary>8.60–9.86.</summary>
    V860,
    /// <summary>10.10 and later (including client 11): attribute 16 is "no movement animation".</summary>
    V1010,
}

/// <summary>A legacy client version: dat layout, file signatures and optional features.</summary>
/// <param name="ExtendedSprites">Sprite IDs and the sprite count are 32-bit (9.60+, or "extended" clients).</param>
/// <param name="Transparency">Sprite pixels carry an alpha channel (RGBA instead of RGB).</param>
/// <param name="FrameGroups">Outfits store idle and moving frame groups separately (10.57+).</param>
/// <param name="EnhancedAnimations">Animated objects store per-phase durations (10.50+).</param>
public sealed record LegacyClientProfile(
    string Id,
    string Name,
    string Description,
    LegacyDatLayout Layout,
    uint DatSignature,
    uint SprSignature,
    bool ExtendedSprites,
    bool Transparency,
    bool FrameGroups,
    bool EnhancedAnimations)
{
    /// <summary>Pattern Z (depth) exists from 7.55.</summary>
    public bool HasPatternZ => Layout != LegacyDatLayout.V740;

    public LegacyClientProfile WithSignatures(uint dat, uint spr) => this with { DatSignature = dat, SprSignature = spr };
    public LegacyClientProfile WithTransparency(bool transparency) => this with { Transparency = transparency };
}

/// <summary>Known legacy client versions (used to read files and as conversion targets).</summary>
public static class LegacyClientProfiles
{
    /// <summary>
    /// The 10/11 layout used with 15.x assets by the extended client 11 executable: the target of
    /// ClientConverter and of the Assets Editor's "client11-15x" export profile.
    /// </summary>
    public static readonly LegacyClientProfile Client11 = new(
        "client11", L.T("LegacyFormat.Client11For15XAssets"),
        L.T("LegacyFormat.1010ObjectLayoutWithIdle"),
        LegacyDatLayout.V1010, 0x00004A10, 0x59E48E02, ExtendedSprites: true, Transparency: false, FrameGroups: true, EnhancedAnimations: true);

    public static readonly LegacyClientProfile Tibia1098 = new(
        "1098", L.T("LegacyFormat.Tibia1098"),
        L.T("LegacyFormat.1098ObjectLayoutWithFrame"),
        LegacyDatLayout.V1010, 0x000042A3, 0x53159CA9, ExtendedSprites: true, Transparency: false, FrameGroups: true, EnhancedAnimations: true);

    public static readonly LegacyClientProfile Tibia860 = new(
        "860", L.T("LegacyFormat.Tibia860"),
        L.T("LegacyFormat.860ObjectLayoutNoFrame"),
        LegacyDatLayout.V860, 0x4C2C7993, 0x4C220594, ExtendedSprites: false, Transparency: false, FrameGroups: false, EnhancedAnimations: false);

    /// <summary>Profiles the Client Converter can write. Older layouts can be read but not written.</summary>
    public static IReadOnlyList<LegacyClientProfile> Writable { get; } = [Client11, Tibia1098];

    public static IReadOnlyList<LegacyClientProfile> Known { get; } = [Client11, Tibia1098, Tibia860];

    /// <summary>The profile whose signatures match, or null.</summary>
    public static LegacyClientProfile? FromSignatures(uint dat, uint spr) =>
        Known.FirstOrDefault(p => p.DatSignature == dat && p.SprSignature == spr) ?? Known.FirstOrDefault(p => p.DatSignature == dat);

    /// <summary>A reading profile for a client version number such as 760, 860 or 1098.</summary>
    public static LegacyClientProfile ForVersion(int version, bool extended = false, bool transparency = false)
    {
        var layout = version switch
        {
            < 755 => LegacyDatLayout.V740,
            < 780 => LegacyDatLayout.V755,
            < 860 => LegacyDatLayout.V780,
            < 1010 => LegacyDatLayout.V860,
            _ => LegacyDatLayout.V1010,
        };
        return new LegacyClientProfile($"v{version}", L.F("LegacyFormat.Tibia0100", version / 100, version % 100), "", layout, 0, 0,
            ExtendedSprites: extended || version >= 960, Transparency: transparency, FrameGroups: version >= 1057, EnhancedAnimations: version >= 1050);
    }
}
