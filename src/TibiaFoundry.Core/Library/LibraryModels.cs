namespace TibiaFoundry.Core.Library;

/// <summary>
/// The categories every library starts with (8.8). Users can add their own; built-in categories
/// cannot be renamed or deleted, so the indexer and the content workflows can always rely on them.
/// </summary>
public static class AssetCategories
{
    public const string Items = "Items";
    public const string Creatures = "Creatures";
    public const string Outfits = "Outfits";
    public const string OutfitAddons = "Outfit Addons";
    public const string Mounts = "Mounts";
    public const string Effects = "Effects";
    public const string Projectiles = "Projectiles";
    public const string Environment = "Environment Objects";
    public const string Sprites = "Sprites";
    public const string SpriteSheets = "Sprite Sheets";
    public const string Maps = "Maps";
    public const string MapSections = "Map Sections";
    public const string Houses = "Houses";
    public const string Sounds = "Sounds";
    public const string Animations = "Animations";
    public const string Generated = "Generated Images";
    public const string Artwork = "Custom Artwork";
    public const string Other = "Other Media";

    public static IReadOnlyList<string> BuiltIn { get; } =
        [Items, Creatures, Outfits, OutfitAddons, Mounts, Effects, Projectiles, Environment, Sprites, SpriteSheets, Maps, MapSections, Houses, Sounds, Animations, Generated, Artwork, Other];

    public static bool IsBuiltIn(string name) => BuiltIn.Contains(name, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Whether an asset was already exported or published (set by content workflows or by hand).</summary>
public static class ExportStates
{
    public const string NotExported = "Not exported";
    public const string Exported = "Exported";
    public const string Published = "Published";

    public static IReadOnlyList<string> All { get; } = [NotExported, Exported, Published];
}

/// <summary>What kind of media a file is (decides the preview and which workflow steps accept it).</summary>
public enum MediaKind { Image, Animation, Audio, Data, Other }

/// <summary>One file in the Asset Library: where it is, what it shows, and everything known about it.</summary>
public sealed record AssetRecord
{
    public long Id { get; init; }
    /// <summary>Full path of the file. The library references files where they are; it never moves them.</summary>
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public string? Description { get; init; }
    /// <summary>Where it came from in words ("Appearances export", "Folder D:\Art").</summary>
    public string? Source { get; init; }
    /// <summary>The Tibia Foundry tool that wrote it, when known.</summary>
    public string? SourceTool { get; init; }
    public string? ClientVersion { get; init; }
    /// <summary>The game's identifier of what it shows (object, outfit or sprite ID), when known.</summary>
    public string? OriginalId { get; init; }
    /// <summary>Lower-case extension without the dot ("png", "gif", "ogg").</summary>
    public required string Format { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Frames { get; init; }
    public long SizeBytes { get; init; }
    public string? Sha256 { get; init; }
    /// <summary>64-bit difference hash of the image (visual similarity), or null.</summary>
    public long? PixelHash { get; init; }
    /// <summary>Hash of the pixels with nearest-neighbor enlargement and transparent borders removed (same graphic at another size).</summary>
    public string? PixelKey { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public DateTime FileModifiedUtc { get; init; }
    public string? Creator { get; init; }
    public string? Attribution { get; init; }
    public string? Copyright { get; init; }
    public string ExportStatus { get; init; } = ExportStates.NotExported;
    /// <summary>The file was not found at the last scan (kept in the library, never removed automatically).</summary>
    public bool Missing { get; init; }
    /// <summary>The file is a copy kept in the library's own folder (imported with "Copy into the library").</summary>
    public bool Managed { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    public bool IsAnimated => Frames > 1;
    public MediaKind Kind => MediaTypes.KindOf(Format, Frames);
    public string Dimensions => Width > 0 && Height > 0 ? $"{Width}×{Height}" : "";
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>Edits of an asset's descriptive metadata (null = unchanged).</summary>
public sealed record AssetEdit
{
    public string? Name { get; init; }
    public string? Category { get; init; }
    public string? Description { get; init; }
    public string? ClientVersion { get; init; }
    public string? OriginalId { get; init; }
    public string? Source { get; init; }
    public string? Creator { get; init; }
    public string? Attribution { get; init; }
    public string? Copyright { get; init; }
    public string? ExportStatus { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public IReadOnlyDictionary<string, string>? Fields { get; init; }
}

public enum AssetSort { Name, Newest, Modified, Category, Size, Id }

/// <summary>A search of the library. Every filter is optional; the text matches names, file names, descriptions, tags, keywords and IDs.</summary>
public sealed record AssetQuery
{
    public string? Text { get; init; }
    public string? Category { get; init; }
    public string? Format { get; init; }
    public string? ClientVersion { get; init; }
    public string? Source { get; init; }
    public string? Tag { get; init; }
    public string? Creator { get; init; }
    public bool? Animated { get; init; }
    public string? ExportStatus { get; init; }
    public long? CollectionId { get; init; }
    public DateTime? CreatedFromUtc { get; init; }
    public DateTime? CreatedToUtc { get; init; }
    public DateTime? ModifiedFromUtc { get; init; }
    public DateTime? ModifiedToUtc { get; init; }
    public int? MinWidth { get; init; }
    public int? MaxWidth { get; init; }
    public int? MinHeight { get; init; }
    public int? MaxHeight { get; init; }
    public MediaKind? Kind { get; init; }
    public bool IncludeMissing { get; init; } = true;
    public AssetSort Sort { get; init; } = AssetSort.Name;
    public int Offset { get; init; }
    public int Limit { get; init; } = 200;
}

/// <summary>One page of search results and the number of all matches.</summary>
public sealed record AssetPage(IReadOnlyList<AssetRecord> Assets, int Total);

/// <summary>A named, ordered set of assets of any category ("Creature Gallery", "Summer Update Resources").</summary>
public sealed record AssetCollection(long Id, string Name, string? Description, long? ThumbnailAssetId, DateTime CreatedUtc, DateTime ModifiedUtc, int Count);

/// <summary>A category with the number of assets in it.</summary>
public sealed record CategoryInfo(string Name, bool IsCustom, string? Description, int Count);

/// <summary>A folder whose files the library indexes (rescanned incrementally).</summary>
public sealed record LibraryRoot(long Id, string Path, bool Recursive, string? Category, string? Source, DateTime AddedUtc, DateTime? LastScanUtc);

/// <summary>An earlier content of a file, recorded when a rescan found it changed.</summary>
public sealed record AssetVersion(long Id, string? Sha256, long SizeBytes, DateTime FileModifiedUtc, DateTime SeenUtc);

/// <summary>Distinct values for the filter lists, with counts.</summary>
public sealed record LibraryFacets(
    IReadOnlyList<(string Value, int Count)> Formats,
    IReadOnlyList<(string Value, int Count)> ClientVersions,
    IReadOnlyList<(string Value, int Count)> Sources,
    IReadOnlyList<(string Value, int Count)> Creators,
    IReadOnlyList<(string Value, int Count)> Tags);

/// <summary>Totals of the library, for the page header and Home.</summary>
public sealed record LibraryStatistics(int Assets, int Missing, int Animated, long Bytes, int Collections, int Roots, DateTime? LastScanUtc);

/// <summary>File extensions the library indexes, and what they are.</summary>
public static class MediaTypes
{
    public static IReadOnlyList<string> Images { get; } = ["png", "gif", "bmp", "jpg", "jpeg", "webp", "tif", "tiff"];
    public static IReadOnlyList<string> Audio { get; } = ["ogg", "wav", "mp3", "flac", "opus", "m4a"];
    public static IReadOnlyList<string> Data { get; } = ["json", "csv", "txt", "md", "lua", "xml"];

    /// <summary>Extensions indexed when a folder is added (data files only when asked for).</summary>
    public static bool IsIndexed(string extension, bool includeData)
    {
        string e = extension.TrimStart('.').ToLowerInvariant();
        return Images.Contains(e) || Audio.Contains(e) || (includeData && Data.Contains(e));
    }

    public static MediaKind KindOf(string format, int frames)
    {
        string f = format.ToLowerInvariant();
        if (Images.Contains(f)) return frames > 1 ? MediaKind.Animation : MediaKind.Image;
        if (Audio.Contains(f)) return MediaKind.Audio;
        if (Data.Contains(f)) return MediaKind.Data;
        return MediaKind.Other;
    }
}
