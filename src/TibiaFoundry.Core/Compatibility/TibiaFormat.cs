namespace TibiaFoundry.Core.Compatibility;

/// <summary>
/// Every assumption about the Tibia client's file layout that can change with a client update,
/// in one place. When CipSoft changes the layout, this file, the <c>.proto</c> schemas in
/// <c>Protos/</c> and <c>Appearances/GameTables.cs</c> are the places to update (see
/// docs/11_Maintenance_Guide.md). Values here are facts observed in the client; they are not
/// user settings.
/// </summary>
public static class TibiaFormat
{
    // ------------------------------------------------------------------ package layout
    /// <summary>Client packages live in <c>%LOCALAPPDATA%\Tibia\packages\&lt;name&gt;</c>.</summary>
    public const string PackagesFolder = @"Tibia\packages";
    public const string LivePackage = "Tibia";
    public const string DefaultTestPackage = "TibiaExternal";
    public const string AssetsFolder = "assets";
    public const string SoundsFolder = "sounds";

    /// <summary>Package manifest with <c>"version"</c> (e.g. 15.33.b21348) — the client version.</summary>
    public const string PackageManifest = "package.json";
    /// <summary>Asset manifest listing every asset with its size and SHA-256.</summary>
    public const string AssetManifest = "assets.json";
    /// <summary>Index of the asset files (sprite sheets and data files).</summary>
    public const string AssetCatalogFile = "catalog-content.json";
    /// <summary>Index of the sounds folder.</summary>
    public const string SoundCatalogFile = "catalog-sound.json";

    // ------------------------------------------------------------------ catalog entry types
    public const string CatalogTypeSprite = "sprite";
    public const string CatalogTypeAppearances = "appearances";
    public const string CatalogTypeStaticData = "staticdata";
    public const string CatalogTypeStaticMapData = "staticmapdata";
    public const string CatalogTypeMap = "map";
    public const string CatalogTypeSounds = "sounds";

    /// <summary>Catalog types this version reads. Others (e.g. "fullmap", "proficiencies") are reported as unused.</summary>
    public static readonly IReadOnlySet<string> UsedCatalogTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        CatalogTypeSprite, CatalogTypeAppearances, CatalogTypeStaticData, CatalogTypeStaticMapData, CatalogTypeMap, CatalogTypeSounds,
    };

    // ------------------------------------------------------------------ sprite sheets
    /// <summary>Side of a decoded sprite sheet in the current client. Slicing derives the grid from the image, so this is only used for validation and display.</summary>
    public const int SheetSize = 384;

    /// <summary>Sprite size per catalog <c>spritetype</c>. Unknown types are rejected (never guessed).</summary>
    public static readonly IReadOnlyDictionary<int, (int Width, int Height)> SpriteTypes = new Dictionary<int, (int, int)>
    {
        [0] = (32, 32),
        [1] = (32, 64),
        [2] = (64, 32),
        [3] = (64, 64),
    };

    // ------------------------------------------------------------------ Cyclopedia map export
    /// <summary>File name of the client's Cyclopedia map export for one floor (0–7): "floor-0Z-map.png".</summary>
    public static string CyclopediaFloorFile(int floor) => $"floor-0{floor}-map.png";
    /// <summary>World coordinate of the export's left and top edge (from the original program; tiles are 256 px).</summary>
    public const int CyclopediaOriginX = 31744;
    public const int CyclopediaOriginY = 30976;

    // ------------------------------------------------------------------ CIP LZMA container
    public const byte LzmaMarker0 = 0x70;
    public const byte LzmaMarker1 = 0x0A;

    // ------------------------------------------------------------------ data file names
    /// <summary>Data files are named "&lt;type&gt;-&lt;sha256 of content&gt;.&lt;ext&gt;" (used for integrity checks).</summary>
    public const int ContentHashLength = 64;

    /// <summary>
    /// Client versions whose data were validated against this build (full test suite and parity
    /// checks). Other versions are accepted when their structure validates, and are reported as
    /// "unfamiliar but structurally compatible".
    /// </summary>
    /// <remarks>15.33.9d148f (2026-10-13) has the same data files as 15.33.b21348: identical catalog-content.json,
    /// catalog-sound.json and checksum-named files; the full suite passed against it (05_verification/7.0.0).</remarks>
    public static readonly IReadOnlyList<string> ValidatedClientVersions = ["15.33.c9e45e", "15.33.9d148f", "15.33.b21348", "15.25.0e418e"];

    /// <summary>
    /// Fields present in validated client data that the recovered schema does not describe and
    /// that this program deliberately ignores (the original 1.8.0.26867 ignored them too). They
    /// are reported as "known unmapped" instead of marking the data as unfamiliar. Add an entry
    /// only after checking that ignoring the field does not change any output.
    /// </summary>
    public static readonly IReadOnlySet<(string Message, int Field)> KnownSchemaDrift = new HashSet<(string, int)>
    {
        // staticmapdata 15.33.b21348: house-map tiles carry two extra length-delimited fields.
        ("tibia.protobuf.staticmapdata.Tile", 101),
        ("tibia.protobuf.staticmapdata.Tile", 102),
    };

    /// <summary>
    /// Returns the plain file name when <paramref name="name"/> is a safe single file name
    /// (no folders, no "..", not rooted, no invalid characters); otherwise null. File names in
    /// client data must never be able to point outside the folder they are resolved against.
    /// </summary>
    public static string? SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255) return null;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null; // includes \ / : and control characters
        if (name is "." or ".." || name.Trim() != name || name.EndsWith('.')) return null;
        return name;
    }

    /// <summary>
    /// Resolves a data-supplied relative path ("assets/x.dat" style is allowed) under
    /// <paramref name="root"/>, or returns null when it would leave the folder.
    /// </summary>
    public static string? ResolveUnder(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return null;
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        return full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
