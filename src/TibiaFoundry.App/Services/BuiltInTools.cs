using TibiaFoundry.App.ViewModels;
using TibiaFoundry.App.Views.Pages;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Legacy;
using TibiaFoundry.Core.Tools;

namespace TibiaFoundry.App.Services;

/// <summary>
/// A page of the application: its manifest, its view-model and its view. The navigation, the
/// view-model → view templates, the tool registry and Diagnostics are all built from these
/// registrations, so adding a tool means writing its manifest, view-model and view and adding one
/// line to <see cref="BuiltInTools.Pages"/> (see docs/16_Adding_a_Tool.md).
/// </summary>
public sealed record PageRegistration(ToolManifest Manifest, Type ViewModelType, Type ViewType, Func<AppServices, MainViewModel, PageViewModel> Create)
{
    public static PageRegistration For<TViewModel, TView>(ToolManifest manifest, Func<AppServices, MainViewModel, TViewModel> create)
        where TViewModel : PageViewModel where TView : System.Windows.FrameworkElement =>
        new(manifest, typeof(TViewModel), typeof(TView), (s, m) => create(s, m));
}

/// <summary>Manifests of the built-in pages and tools, in navigation order.</summary>
public static class BuiltInTools
{
    // Navigation groups, in the order of the rail (the Home dashboard uses the same groups).
    public const string StartGroup = "Start";
    public const string ContentGroup = "Content Production";
    public const string GraphicsGroup = "Graphics & Sprites";
    public const string GameDataGroup = "Game Data";
    public const string MapsGroup = "Maps & Houses";
    public const string MediaGroup = "Sound & Media";
    public const string AssetsGroup = "Asset Management";
    public const string ClientLibraryGroup = "Client Library";
    public const string UtilitiesGroup = "Utilities";
    public const string ApplicationGroup = "Application";

    private static readonly ToolLicense Own = new("Tibia Foundry", "Part of Tibia Foundry © RockyLaRo (TibiaWiki Admin). Tibia game data and graphics © CipSoft GmbH.");

    private static readonly ToolDependency Client = new("tibia-client", "Installed Tibia client", DependencyKind.ClientData, Required: true,
        "Install or update the Tibia client, or choose its assets folder in Settings → Tibia client.");

    /// <summary>For pages that can also open files chosen by hand ("Change file"): without the client they still work, with less.</summary>
    private static readonly ToolDependency ClientOptional = Client with
    {
        Required = false,
        Resolution = "Install the Tibia client, choose its assets folder in Settings → Tibia client, or open a file with \"Change file\".",
    };

    private static ToolDependency ImageMagick(params string[] operations) => new("imagemagick", "ImageMagick (Magick.NET, bundled)", DependencyKind.Library, Required: true,
        "ImageMagick is part of Tibia Foundry; reinstall or re-extract the program if its files are damaged.") { MinimumVersion = "14.0", RequiredFor = operations };

    private static readonly ToolDependency TibiaWiki = new("tibiawiki", "TibiaWiki (tibia.fandom.com)", DependencyKind.Service, Required: false,
        "Check your internet connection; a cached copy of TibiaWiki's item list is used when the site cannot be reached.") { RequiredFor = ["npc-trades"] };

    private static readonly ToolCapabilities LongRunning = ToolCapabilities.Progress | ToolCapabilities.Cancellation;

    private static readonly string[] SpriteSheet = [".bmp.lzma"];

    public static ToolManifest Home { get; } = new()
    {
        Id = "home", Name = "Home", Type = ToolType.Application, Version = "8.8", Category = StartGroup, Glyph = "\uE80F",
        Description = "Start here: what Tibia Foundry does, every tool by category, recent tools and exports, and common fansite workflows",
        License = Own, Documentation = ["docs/23_Fansite_Toolkit.md"],
    };

    public static ToolManifest AssetLibrary { get; } = new()
    {
        Id = "asset-library", Name = "Asset Library", Type = ToolType.Viewer, Version = "8.8", Category = ContentGroup, Glyph = "",
        Description = "Search, browse, tag and organize every image, animation and sound you work with; collections, duplicates and versions, all local",
        Capabilities = LongRunning | ToolCapabilities.Batch | ToolCapabilities.Backups | ToolCapabilities.Preview,
        Operations =
        [
            new("index", "Index folders and files", LongRunning | ToolCapabilities.Batch),
            new("duplicates", "Find duplicates", LongRunning),
            new("verify", "Verify the library", LongRunning),
            new("backup", "Back up and restore", ToolCapabilities.Progress | ToolCapabilities.Backups),
        ],
        Inputs = [new("Images", [".png", ".gif", ".bmp", ".jpg", ".webp", ".tif"]), new("Sounds", [".ogg", ".wav", ".mp3", ".flac"]), new("Collection", [".tfcollection.json"])],
        Outputs = [new("Library index", ["library.db"], "SQLite"), new("Collection", [".tfcollection.json"]), new("Library backup", [".zip"])],
        License = Own with { Attribution = Own.Attribution + " Index stored with SQLite (public domain) through Microsoft.Data.Sqlite (MIT)." },
        Documentation = ["docs/24_Asset_Library_and_Content_Studio.md"],
        Limitations = "Files are indexed where they are and only read; removing an asset from the library never deletes a file. Visual similarity is a hint, never a reason to delete.",
    };

    public static ToolManifest ContentStudio { get; } = new()
    {
        Id = "content-studio", Name = "Content Studio", Type = ToolType.Exporter, Version = "8.8", Category = ContentGroup, Glyph = "",
        Description = "Reusable projects and workflows that render creatures, outfits and items in bulk and publish galleries, manifests and packages for any website",
        Capabilities = LongRunning | ToolCapabilities.Batch,
        Operations =
        [
            new("run", "Run a content project", LongRunning | ToolCapabilities.Batch),
            new("profiles", "Edit publishing profiles", ToolCapabilities.None),
        ],
        Inputs = [new("Workflow", [".tfworkflow.json"]), new("Client data", ["appearances-*.dat", "staticdata-*.dat"]), new("Library assets", [])],
        Outputs = [new("Images", [".png", ".gif"]), new("Manifests", [".json", ".csv", ".md"]), new("HTML gallery", ["index.html"]), new("Package", [".zip"])],
        Dependencies = [ClientOptional with { Resolution = "Install the Tibia client or choose it on the Clients page; projects that only use library assets work without it." }, ImageMagick("run")],
        License = Own, Documentation = ["docs/24_Asset_Library_and_Content_Studio.md"],
        Limitations = "Map images are made in Map Tools and House Images; Content Studio collects them from the library.",
    };

    public static ToolManifest Appearances { get; } = new()
    {
        Id = "appearances", Name = "Appearances", Type = ToolType.Viewer, Version = "7.0", Category = GraphicsGroup, Glyph = "",
        Description = "Browse the official objects, outfits, effects and missiles; render outfits and save animated GIFs with fansite presets for articles and galleries",
        Capabilities = LongRunning | ToolCapabilities.Batch,
        Operations =
        [
            new("load", "Load appearances", ToolCapabilities.Progress | ToolCapabilities.Cancellation),
            new("export-gif", "Save as GIF", LongRunning | ToolCapabilities.Batch),
            new("export-json", "Export JSON", LongRunning),
            new("npc-trades", "Generate NPC trade data", LongRunning),
        ],
        Inputs = [new("Appearances", ["appearances-*.dat"], "protobuf, from the client"), new("Sprite sheets", SpriteSheet, "CIP LZMA"), new("Extracted sprites", [".png"])],
        Outputs = [new("Animated GIF", [".gif"]), new("PNG image", [".png"]), new("JSON", [".json"]), new("ID table", [".txt"]), new("Lua module", [".lua"])],
        DefaultOutput = OutputKind.GifExports,
        Dependencies = [ClientOptional, ImageMagick("export-gif"), TibiaWiki with { RequiredFor = ["npc-trades"] }],
        License = Own, Documentation = ["docs/02_Functional_Specification.md (Appearances)", "docs/15_Themes_Outputs_Tools_and_Diagnostics.md §4 (export messages)", "docs/23_Fansite_Toolkit.md §4 (export presets)"],
    };

    public static ToolManifest StaticData { get; } = new()
    {
        Id = "static-data", Name = "Static Data", Type = ToolType.Viewer, Version = "7.0", Category = GameDataGroup, Glyph = "",
        Description = "Creatures, bosses, achievements, houses and quests from the client's static data, with creature GIFs and JSON for bestiary pages and databases",
        Capabilities = LongRunning,
        Operations = [new("load", "Load static data", LongRunning), new("export-json", "Export JSON", LongRunning), new("creature-gif", "Save creature GIF", ToolCapabilities.None),
            new("creature-gifs", "Save the GIFs of many creatures", LongRunning)],
        Inputs = [new("Static data", ["staticdata-*.dat"], "protobuf, from the client")],
        Outputs = [new("JSON", [".json"]), new("Animated GIF", [".gif"])],
        DefaultOutput = OutputKind.DataExports,
        Dependencies = [ClientOptional, ImageMagick("creature-gif", "creature-gifs")],
        License = Own, Documentation = ["docs/02_Functional_Specification.md (Static Data)"],
    };

    public static ToolManifest ItemCatalog { get; } = new()
    {
        Id = "item-catalog", Name = "Item Catalog", Type = ToolType.Viewer, Version = "7.2", Category = GameDataGroup, Glyph = "\uE8FD",
        Description = "Item names and IDs from the client, TibiaWiki and other sources, kept apart by source, with coverage and exports for wiki pages and item databases",
        Capabilities = LongRunning | ToolCapabilities.Backups,
        Operations =
        [
            new("open", "Open or create a catalog", ToolCapabilities.Progress),
            new("import", "Import from the client or a file", LongRunning | ToolCapabilities.Backups),
            new("import-wiki", "Import TibiaWiki's item IDs", LongRunning | ToolCapabilities.Backups),
            new("export", "Export", ToolCapabilities.Progress),
        ],
        Inputs =
        [
            new("Appearances", ["appearances-*.dat"], "the client's own names"), new("Name list", [".txt"], "\"[id] name\" lines (GetItemNames, LapisItemEditor)"),
            new("CSV", [".csv"]), new("Catalog export", [".json"]),
        ],
        Outputs = [new("Item catalog", ["catalog.json", "*.json.gz", "observations.jsonl"]), new("Name list", [".txt"]), new("CSV", [".csv"]), new("JSON", [".json"])],
        DefaultOutput = OutputKind.ItemCatalogs,
        SettingsPrefix = "ItemCatalog.",
        Settings = [new("lastCatalog", "Last catalog", ToolSettingKind.Text, "", "The catalog that was open last; it opens again next time.") { Visible = false, MaxLength = 1024 }],
        Dependencies = [ClientOptional with { Resolution = "Install the Tibia client or locate its files in Settings → Tibia client; catalogs and file imports work without it." },
            TibiaWiki with { RequiredFor = ["import-wiki"] }],
        License = Own with { Attribution = Own.Attribution + " TibiaWiki data: TibiaWiki contributors (CC BY-SA)." },
        Documentation = ["docs/17_Item_Catalog.md"],
        Limitations = "TibiaWiki's item list gives article titles, which can differ from in-game names.",
    };

    public static ToolManifest MapTools { get; } = new()
    {
        Id = "map-tools", Name = "Map Tools", Type = ToolType.Exporter, Version = "7.0", Category = MapsGroup, Glyph = "",
        Description = "Map images, areas and NPC positions from the client's map files, and high-resolution maps for guides and location articles",
        Capabilities = LongRunning | ToolCapabilities.Batch,
        Operations = [new("load", "Load map data", LongRunning), new("extract-tiles", "Extract map tiles", LongRunning | ToolCapabilities.Batch), new("hr-map", "Build HR map", LongRunning)],
        Inputs = [new("Map data", ["map-*.dat", "staticmapdata-*.dat"]), new("Map tiles", [".png"])],
        Outputs = [new("PNG image", [".png"]), new("JSON", [".json"])],
        DefaultOutput = OutputKind.MapExports,
        Dependencies = [ClientOptional, new("tibiamaps", "tibiamaps.io", DependencyKind.Service, Required: false, "Only needed to open map links in the browser.")],
        License = Own, Documentation = ["docs/02_Functional_Specification.md (Map Tools)"],
    };

    public static ToolManifest HouseImages { get; } = new()
    {
        Id = "house-images", Name = "House Images", Type = ToolType.Exporter, Version = "7.0", Category = MapsGroup, Glyph = "",
        Description = "Render house maps from the client's data as images for house articles on TibiaWiki and fansite house guides",
        Capabilities = LongRunning,
        Inputs = [new("Static map data", ["staticmapdata-*.dat"])],
        Outputs = [new("PNG image", [".png"]), new("JSON", [".json"])],
        DefaultOutput = OutputKind.ImageExports,
        Dependencies = [ClientOptional],
        License = Own, Documentation = ["docs/02_Functional_Specification.md (House Images)"],
    };

    public static ToolManifest Sounds { get; } = new()
    {
        Id = "sounds", Name = "Sounds", Type = ToolType.Viewer, Version = "7.0", Category = MediaGroup, Glyph = "",
        Description = "Listen to and extract the client's sounds, effects, ambience and music for media pages and references",
        Capabilities = LongRunning | ToolCapabilities.Batch,
        Operations = [new("load", "Load sounds", LongRunning), new("extract", "Extract sounds", LongRunning | ToolCapabilities.Batch), new("play", "Play", ToolCapabilities.None)],
        Inputs = [new("Sound catalog", ["sounds-*.dat"]), new("Sound files", [".ogg"])],
        Outputs = [new("Sound files", [".ogg"]), new("JSON", [".json"])],
        DefaultOutput = OutputKind.SoundExports,
        Dependencies = [ClientOptional, new("naudio", "NAudio (bundled)", DependencyKind.Library, Required: true, "NAudio is part of Tibia Foundry; reinstall or re-extract the program.") { RequiredFor = ["play"] }],
        License = Own, Documentation = ["docs/02_Functional_Specification.md (Sounds)"],
    };

    public static ToolManifest SpriteExtractor { get; } = new()
    {
        Id = "sprite-extractor", Name = "Extract Sprites", Type = ToolType.Exporter, Version = "7.0", Category = GraphicsGroup, Glyph = "",
        Description = "Save the client's sprite sheets as individual transparent sprites or whole sheets for galleries, wiki images and artwork references",
        Capabilities = LongRunning | ToolCapabilities.Batch,
        Inputs = [new("Sprite sheets", SpriteSheet, "CIP LZMA, from the client")],
        Outputs = [new("PNG sprites or sheets", [".png"]), new("Provenance manifest", ["TibiaFoundry-sprites.json"])],
        DefaultOutput = OutputKind.SpriteExports,
        Dependencies = [Client],
        License = Own, Documentation = ["docs/02_Functional_Specification.md (Extract Sprites)"],
    };

    public static ToolManifest Compare { get; } = new()
    {
        Id = "compare", Name = "Compare Files", Type = ToolType.Validator, Version = "6.0", Category = GameDataGroup, Glyph = "",
        Description = "Find what changed between two official client versions (appearances, map, staticdata, staticmapdata) for update articles and change logs",
        Capabilities = LongRunning,
        Inputs = [new("Client data files", ["appearances-*.dat", "map-*.dat", "staticdata-*.dat", "staticmapdata-*.dat"])],
        Outputs = [new("Comparison report", [".txt"])],
        DefaultOutput = OutputKind.Reports,
        License = Own, Documentation = ["docs/02_Functional_Specification.md (Compare)"],
    };

    public static ToolManifest AdvancedTools { get; } = new()
    {
        Id = "advanced-tools", Name = "Advanced Tools", Type = ToolType.Utility, Version = "6.0", Category = GraphicsGroup, Glyph = "",
        Description = "Build and slice sprite sheets for galleries and artwork, and decompress or download official client asset files",
        Capabilities = LongRunning | ToolCapabilities.Batch,
        Operations = [new("sheet", "Build or slice sheets", ToolCapabilities.None), new("decompress", "Decompress", LongRunning | ToolCapabilities.Batch), new("download", "Download files", LongRunning | ToolCapabilities.Batch)],
        Inputs = [new("Images", [".png", ".gif", ".bmp"]), new("Compressed assets", [".lzma"]), new("File list", [".json", ".txt"])],
        Outputs = [new("PNG image", [".png"]), new("Decompressed files", [])],
        DefaultOutput = OutputKind.ImageExports,
        Dependencies = [new("tibia-test-server", "Tibia test-server download site", DependencyKind.Service, Required: false,
            "Check your internet connection; nothing else depends on it.") { RequiredFor = ["download"] }],
        License = Own, Documentation = ["docs/02_Functional_Specification.md (Advanced Tools)"],
    };

    public static ToolManifest AssetsEditor { get; } = new()
    {
        Id = "assets-editor", Name = "Assets Editor", Type = ToolType.Editor, Version = "6.0", Category = AssetsGroup, Glyph = "",
        Description = "Inspect and prepare the official objects, outfits, effects and missiles of an assets folder with their sprites: export images, edit a copy and save it validated",
        Capabilities = LongRunning | ToolCapabilities.Validation | ToolCapabilities.Backups,
        Operations = [new("open", "Open assets", LongRunning), new("save", "Save", ToolCapabilities.Progress | ToolCapabilities.Validation | ToolCapabilities.Backups), new("export-images", "Export images", LongRunning | ToolCapabilities.Batch)],
        Inputs = [new("Assets folder", ["catalog-content.json", "appearances-*.dat", "*.bmp.lzma"]), new("Object package", [".aec"]), new("Images", [".png", ".bmp", ".gif"])],
        Outputs = [new("Assets folder (validated copy)", []), new("Object package", [".aec"]), new("PNG image", [".png"])],
        DefaultOutput = OutputKind.AssetsEditor,
        License = Own with { Attribution = Own.Attribution + " Feature set informed by Arch-Mina's Assets Editor (https://github.com/Arch-Mina/Assets-Editor); no code of it is included." },
        Documentation = ["docs/14_Tools_Settings_and_Scripts.md §4"],
        Limitations = "Legacy dat/spr editing, large sprite sheets, Lua and third-party item and object formats are not supported.",
    };

    public static ToolManifest RccEditor { get; } = new()
    {
        Id = "rcc-editor", Name = "RCC Editor", Type = ToolType.Editor, Version = "7.0", Category = AssetsGroup, Glyph = "",
        Description = "Preview, extract, replace and rebuild the resources of Qt .rcc files such as the client's graphics_resources.rcc (interface artwork, icons and backgrounds)",
        Capabilities = ToolCapabilities.Progress | ToolCapabilities.Validation | ToolCapabilities.Backups | ToolCapabilities.Batch,
        Operations = [new("extract", "Extract", ToolCapabilities.Progress | ToolCapabilities.Batch), new("save", "Save", ToolCapabilities.Validation | ToolCapabilities.Backups)],
        Inputs = [new("Qt resource file", [".rcc"], "formats 1–3, zlib; Zstandard entries are kept but not decompressed"), new("Folder of resources", [])],
        Outputs = [new("Qt resource file", [".rcc"]), new("Extracted resources", [])],
        DefaultOutput = OutputKind.RccExports,
        License = Own with { Attribution = Own.Attribution + " Inspired by Nekiro's RCC Editor (MIT, https://github.com/nekiro/Nekiro-Rcc-Editor); no code of it is included." },
        Documentation = ["docs/14_Tools_Settings_and_Scripts.md §2"],
    };

    public static ToolManifest ClientConverter { get; } = new()
    {
        Id = "client-converter", Name = "Client Converter", Type = ToolType.Converter, Version = "6.0", Category = AssetsGroup, Glyph = "",
        Description = "Write the official modern assets as a classic Tibia.dat and Tibia.spr for historical comparisons and legacy-format tools, and identify legacy files",
        Capabilities = LongRunning | ToolCapabilities.Validation | ToolCapabilities.Batch,
        Operations = [new("convert", "Convert", LongRunning | ToolCapabilities.Validation), new("identify", "Identify a legacy Tibia.dat", ToolCapabilities.None)],
        Inputs = [new("Assets folder", ["catalog-content.json", "appearances-*.dat", "*.bmp.lzma"])],
        Outputs = [new("Legacy object file", ["Tibia.dat"]), new("Legacy sprite file", ["Tibia.spr"]), new("Sprite slices", [".png"])],
        DefaultOutput = OutputKind.ClientConverter,
        SettingsPrefix = "ClientConverter.",
        Settings =
        [
            new("profile", "Target client", ToolSettingKind.Choice, LegacyClientProfiles.Client11.Id, "Layout and signatures of the files written.")
                { Choices = LegacyClientProfiles.Writable.Select(p => (p.Id, p.Name)).ToList() },
            new("transparency", "Keep transparency", ToolSettingKind.Boolean, "false", "Write sprites with an alpha channel (for readers that support transparent sprites)."),
            new("skipEmpty", "Skip empty sprites", ToolSettingKind.Boolean, "true", "Fully transparent sprites become sprite 0 instead of being stored."),
            new("shortenNames", "Shorten long names", ToolSettingKind.Boolean, "true", "Names longer than the legacy limit are shortened (counted in the result)."),
            new("mapMarketCategories", "List new market categories under \"Others\"", ToolSettingKind.Boolean, "true", "Files of the 10.98 era know market categories 1–22 only."),
            new("slices", "Also write sprite slices", ToolSettingKind.Boolean, "false", "PNG slices of the sprite sheets next to the converted files."),
            new("extensions", "Extended attributes", ToolSettingKind.Text, "", "Attributes of newer clients written as extensions for third-party legacy-format tools (chosen on the page).") { Visible = false, MaxLength = 1024 },
            new("dat", "Custom .dat signature", ToolSettingKind.Text, "", "Hexadecimal signature typed on the page.") { Visible = false, MaxLength = 16 },
            new("spr", "Custom .spr signature", ToolSettingKind.Text, "", "Hexadecimal signature typed on the page.") { Visible = false, MaxLength = 16 },
        ],
        Dependencies = [new("tibia-client", "Installed Tibia client", DependencyKind.ClientData, Required: false,
            "Only needed to convert the installed client; any other assets folder can be chosen.")],
        License = Own with { Attribution = Own.Attribution + " Output checked against Arch-Mina's ClientConverter (https://github.com/Arch-Mina/ClientConverter); no code of it is included." },
        Documentation = ["docs/14_Tools_Settings_and_Scripts.md §3"],
        Limitations = "Writes Client 11 and Tibia 10.98 layouts; 8.60 files can be identified but not written.",
    };

    public static ToolManifest FileMetadata { get; } = new()
    {
        Id = "file-metadata", Name = "File Metadata", Type = ToolType.Editor, Version = "8.6", Category = MediaGroup, Glyph = "",
        Description = "View, edit, compare and batch-edit metadata and creator attribution of images, audio, video, documents and other files before you publish them",
        Capabilities = LongRunning | ToolCapabilities.Batch | ToolCapabilities.Preview | ToolCapabilities.Backups | ToolCapabilities.Validation,
        Operations =
        [
            new("read", "Read metadata", LongRunning),
            new("save", "Save changes", LongRunning | ToolCapabilities.Batch | ToolCapabilities.Backups | ToolCapabilities.Validation),
            new("export", "Export a report", ToolCapabilities.None),
        ],
        Inputs =
        [
            new("Images", [".jpg", ".png", ".gif", ".tif", ".webp", ".bmp", ".ico", ".svg"], "EXIF, XMP, IPTC; WebP, BMP and ICO read only"),
            new("Audio", [".mp3", ".flac", ".ogg", ".opus", ".m4a", ".wav", ".aiff", ".wma"]),
            new("Video", [".mp4", ".mkv", ".webm", ".avi", ".mov", ".wmv"]),
            new("Documents", [".docx", ".xlsx", ".pptx", ".epub", ".pdf"], "PDF read only"),
            new("Archives and programs", [".zip", ".tar", ".tgz", ".exe", ".dll"], "read only except the ZIP comment"),
            new("Tibia client files", [".rcc", ".lzma", ".spr", ".dat"], "read only"),
        ],
        Outputs = [new("Metadata report", [".csv", ".json"]), new("Edited files (in place, verified)", [])],
        DefaultOutput = OutputKind.Reports,
        SettingsPrefix = "FileMetadata.",
        Settings =
        [
            new("recursive", "Include subfolders", ToolSettingKind.Boolean, "true", "Adding a folder also adds the files of its subfolders."),
            new("backups", "Back up files before saving", ToolSettingKind.Boolean, "true", "A verified copy of each file goes to Tibia Foundry's backups before it is replaced."),
            new("keepModifiedTime", "Keep the modified time", ToolSettingKind.Boolean, "true", "Saved files keep their \"modified\" date."),
            new("presets", "Presets", ToolSettingKind.Text, "", "Saved sets of values (author, copyright, keywords…).") { Visible = false, MaxLength = 65536 },
        ],
        Dependencies = [new("taglib", "TagLib# (bundled)", DependencyKind.Library, Required: true,
            "TagLib# is part of Tibia Foundry; reinstall or re-extract the program if its files are damaged.") { RequiredFor = ["save"] }],
        License = Own with { Attribution = Own.Attribution + " Image, audio and video tags through TagLib# (LGPL-2.1)." },
        Documentation = ["docs/22_File_Metadata.md"],
        Limitations = "PDF, programs, TAR, WebP, BMP, ICO and Tibia client files are read only. Fields a format cannot store are reported, never written. Camera details are read only.",
    };

    public static ToolManifest Scripts { get; } = new()
    {
        Id = "scripts", Name = "Scripts", Type = ToolType.Script, Version = "6.0", Category = UtilitiesGroup, Glyph = "",
        Description = "File utilities for content production: batch renaming, image conversion and resizing, duplicates, comparisons, checksums, archives, backups and audits",
        Capabilities = LongRunning | ToolCapabilities.Batch | ToolCapabilities.Preview | ToolCapabilities.DryRun | ToolCapabilities.Backups | ToolCapabilities.Undo,
        Inputs = [new("Files and folders", [])],
        Outputs = [new("Reports and manifests", [".csv", ".json", ".txt"]), new("Files written by scripts", [])],
        DefaultOutput = OutputKind.Scripts,
        License = Own, Documentation = ["docs/14_Tools_Settings_and_Scripts.md §6"],
        Limitations = "Only the scripts built into Tibia Foundry can run; there is no way to run arbitrary code.",
    };

    public static ToolManifest Settings { get; } = new()
    {
        Id = "settings", Name = "Settings", Type = ToolType.Application, Version = "7.0", Category = ApplicationGroup, Glyph = "",
        Description = "General behavior, appearance and themes, folders, file safety, performance, attribution, notifications, tools and scripts",
        License = Own, Documentation = ["docs/14_Tools_Settings_and_Scripts.md §5", "docs/15_Themes_Outputs_Tools_and_Diagnostics.md"],
    };

    public static ToolManifest Diagnostics { get; } = new()
    {
        Id = "diagnostics", Name = "Diagnostics", Type = ToolType.Application, Version = "6.0", Category = ApplicationGroup, Glyph = "",
        Description = "Running and recent operations, performance, logs, self-check and diagnostics reports",
        Operations = [new("report", "Generate diagnostics report", ToolCapabilities.Preview)],
        Outputs = [new("Diagnostics report", [".zip"])],
        DefaultOutput = OutputKind.Reports,
        License = Own, Documentation = ["docs/15_Themes_Outputs_Tools_and_Diagnostics.md §6"],
    };

    public static ToolManifest Clients { get; } = new()
    {
        Id = "clients", Name = "Clients", Type = ToolType.Application, Version = "8.0", Category = ClientLibraryGroup, Glyph = "",
        Description = "Your official Tibia clients: installed clients, folders you load from anywhere, and archived revisions for historical reference, live and test server",
        Capabilities = LongRunning,
        Operations =
        [
            new("import", "Read and archive a client folder", LongRunning),
            new("verify", "Verify the client archive", LongRunning),
            new("move-store", "Move the client archive", LongRunning),
        ],
        Outputs = [new(@"Client archive (TibiaFoundry-Data\Clients or %LOCALAPPDATA%\TibiaFoundry\Clients)", [".json"])],
        License = Own, Documentation = ["docs/21_Client_Archiving_and_Rebuilder.md", "docs/20_Client_Management.md"],
        Limitations = "Local only: nothing is uploaded or synchronized. Personal client data (hotkeys, character data, the explored minimap, screenshots, logs) is left out of archives unless you include it, and every revision lists what it left out.",
    };

    public static ToolManifest Rebuilder { get; } = new()
    {
        Id = "client-rebuilder", Name = "Client Installation Rebuilder", Type = ToolType.Utility, Version = "8.0", Category = ClientLibraryGroup, Glyph = "",
        Description = "Make a normal folder from an archived official client revision (for the asset tools or a historical reference copy), verified file by file",
        Capabilities = LongRunning,
        Operations = [new("rebuild", "Rebuild or restore a revision", LongRunning)],
        Inputs = [new("Archived revision", ["manifest.json"], "from the client archive")],
        Outputs = [new("Rebuilt folder", []), new("Report", [".txt"])],
        DefaultOutput = OutputKind.Reports,
        License = Own, Documentation = ["docs/21_Client_Archiving_and_Rebuilder.md"],
        Limitations = "Content-complete means every recorded file was restored with its recorded content; whether a client starts from the folder is not checked.",
    };

    public static ToolManifest Compatibility { get; } = new()
    {
        Id = "compatibility", Name = "Compatibility", Type = ToolType.Validator, Version = "7.0", Category = ApplicationGroup, Glyph = "",
        Description = "Can this version read the installed Tibia client's data?",
        Capabilities = ToolCapabilities.Progress,
        Dependencies = [Client with { Required = false, Resolution = "Install the Tibia client to check it." }],
        License = Own, Documentation = ["docs/10_Tibia_Compatibility_Report.md"],
    };

    public static ToolManifest About { get; } = new()
    {
        Id = "about", Name = "About", Type = ToolType.Application, Version = "6.0", Category = ApplicationGroup, Glyph = "",
        Description = "What Tibia Foundry is for, version, creator, export attribution, credits and acknowledgments",
        License = Own, Documentation = ["docs/13_Branding_and_Attribution.md"],
    };

    /// <summary>The pages in navigation order (the rail shows a group header where the group changes).</summary>
    public static IReadOnlyList<PageRegistration> Pages { get; } =
    [
        PageRegistration.For<HomeViewModel, HomePage>(Home, (s, m) => new HomeViewModel(s, m)),
        // Content Production
        PageRegistration.For<AssetLibraryViewModel, AssetLibraryPage>(AssetLibrary, (s, m) => new AssetLibraryViewModel(s, m)),
        PageRegistration.For<ContentStudioViewModel, ContentStudioPage>(ContentStudio, (s, _) => new ContentStudioViewModel(s)),
        // Graphics & Sprites
        PageRegistration.For<AppearancesViewModel, AppearancesPage>(Appearances, (s, _) => new AppearancesViewModel(s)),
        PageRegistration.For<SpriteExtractorViewModel, SpriteExtractorPage>(SpriteExtractor, (s, _) => new SpriteExtractorViewModel(s)),
        PageRegistration.For<AdvancedToolsViewModel, AdvancedToolsPage>(AdvancedTools, (s, _) => new AdvancedToolsViewModel(s)),
        // Game Data
        PageRegistration.For<StaticDataViewModel, StaticDataPage>(StaticData, (s, _) => new StaticDataViewModel(s)),
        PageRegistration.For<ItemCatalogViewModel, ItemCatalogPage>(ItemCatalog, (s, _) => new ItemCatalogViewModel(s)),
        PageRegistration.For<CompareViewModel, ComparePage>(Compare, (s, _) => new CompareViewModel(s)),
        // Maps & Houses
        PageRegistration.For<MapToolsViewModel, MapToolsPage>(MapTools, (s, _) => new MapToolsViewModel(s)),
        PageRegistration.For<HouseImagesViewModel, HouseImagesPage>(HouseImages, (s, _) => new HouseImagesViewModel(s)),
        // Sound & Media
        PageRegistration.For<SoundsViewModel, SoundsPage>(Sounds, (s, _) => new SoundsViewModel(s)),
        PageRegistration.For<MetadataViewModel, MetadataPage>(FileMetadata, (s, _) => new MetadataViewModel(s)),
        // Asset Management
        PageRegistration.For<AssetsEditorViewModel, AssetsEditorPage>(AssetsEditor, (s, _) => new AssetsEditorViewModel(s)),
        PageRegistration.For<RccEditorViewModel, RccEditorPage>(RccEditor, (s, _) => new RccEditorViewModel(s)),
        PageRegistration.For<ClientConverterViewModel, ClientConverterPage>(ClientConverter, (s, _) => new ClientConverterViewModel(s)),
        // Client Library
        PageRegistration.For<ClientsViewModel, ClientsPage>(Clients, (s, _) => new ClientsViewModel(s)),
        PageRegistration.For<RebuilderViewModel, RebuilderPage>(Rebuilder, (s, _) => new RebuilderViewModel(s)),
        // Utilities
        PageRegistration.For<ScriptsViewModel, ScriptsPage>(Scripts, (s, _) => new ScriptsViewModel(s)),
        // Application
        PageRegistration.For<SettingsViewModel, SettingsPage>(Settings, (s, m) => new SettingsViewModel(s, m)),
        PageRegistration.For<CompatibilityViewModel, CompatibilityPage>(Compatibility, (s, _) => new CompatibilityViewModel(s)),
        PageRegistration.For<DiagnosticsViewModel, DiagnosticsPage>(Diagnostics, (s, m) => new DiagnosticsViewModel(s, m)),
        PageRegistration.For<AboutViewModel, AboutPage>(About, (s, _) => new AboutViewModel(s)),
    ];
}
