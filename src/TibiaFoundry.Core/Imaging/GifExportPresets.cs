using TibiaFoundry.Core.Localization;
namespace TibiaFoundry.Core.Imaging;

/// <summary>
/// A named set of GIF export options for a common fansite or wiki use. A preset only fills in the
/// options of the "Export GIF" box; every option can still be changed afterwards, and values a preset
/// leaves null are kept as they are. GIFs always keep the client's transparency.
/// </summary>
/// <param name="FrameGroup">0 = Idle, 1 = Moving; null keeps the current choice.</param>
/// <param name="Direction">Outfit direction; null keeps the current choice.</param>
/// <param name="KindFolders">Automatically named files go into Objects, Outfits, Effects and Missiles subfolders.</param>
public sealed record GifExportPreset(
    string Id,
    string Name,
    string Description,
    int Scale,
    int? FrameGroup,
    Direction? Direction,
    GifDelayMode DelayMode,
    bool ExtraFrame,
    int ExtraDelay,
    bool KindFolders)
{
    /// <summary>True when the given options are what this preset sets.</summary>
    public bool Matches(int scale, int frameGroup, Direction direction, GifDelayMode delayMode, bool extraFrame, int extraDelay, bool kindFolders) =>
        Scale == scale && (FrameGroup == null || FrameGroup == frameGroup) && (Direction == null || Direction == direction)
        && DelayMode == delayMode && ExtraFrame == extraFrame && ExtraDelay == extraDelay && KindFolders == kindFolders;
}

/// <summary>The built-in fansite export presets of the Appearances page.</summary>
public static class GifExportPresets
{
    public static GifExportPreset TibiaWikiImage { get; } = new("tibiawiki", L.T("GifExportPresets.TibiaWikiImage"),
        L.T("GifExportPresets.TheClientSOwnPixelSize"),
        1, 1, Imaging.Direction.South, GifDelayMode.Average, false, 0, false);

    public static GifExportPreset CreatureGallery { get; } = new("creature-gallery", L.T("GifExportPresets.CreatureGallery"),
        L.T("GifExportPresets.WalkingSouthAt2NearestNeighbor"),
        2, 1, Imaging.Direction.South, GifDelayMode.Average, false, 0, true);

    public static GifExportPreset OutfitGallery { get; } = new("outfit-gallery", L.T("GifExportPresets.OutfitGallery"),
        L.T("GifExportPresets.StandingSouthAt2SortedInto"),
        2, 0, Imaging.Direction.South, GifDelayMode.Average, false, 0, true);

    public static GifExportPreset ItemIcon { get; } = new("item-icon", L.T("GifExportPresets.ItemIcon"),
        L.T("GifExportPresets.TheIdleSpriteAtTheClient"),
        1, 0, null, GifDelayMode.Average, false, 0, false);

    public static GifExportPreset ArticleIllustration { get; } = new("article-illustration", L.T("GifExportPresets.ArticleIllustration"),
        L.T("GifExportPresets.3EnlargementForGuidesAndNews"),
        3, null, null, GifDelayMode.Average, false, 0, false);

    public static GifExportPreset HighResolutionReference { get; } = new("high-resolution", L.T("GifExportPresets.HighResolutionReference"),
        L.T("GifExportPresets.4EnlargementForArtistsAndDesigners"),
        4, null, null, GifDelayMode.Maximum, false, 0, false);

    /// <summary>The presets in the order they are offered.</summary>
    public static IReadOnlyList<GifExportPreset> All { get; } = [TibiaWikiImage, CreatureGallery, OutfitGallery, ItemIcon, ArticleIllustration, HighResolutionReference];

    public static GifExportPreset? Find(string? id) => id == null ? null : All.FirstOrDefault(p => p.Id == id);

    /// <summary>The subfolder of the GIF folder for a kind when presets sort by kind ("Objects", "Outfits"…).</summary>
    public static string KindFolder(Appearances.AppearanceKind kind) => kind + "s";
}
