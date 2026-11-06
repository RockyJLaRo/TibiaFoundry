using System.Text;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Editing.Model;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Legacy;

/// <summary>Options of a modern-to-legacy conversion.</summary>
public sealed record ClientConversionOptions
{
    /// <summary>Folder with catalog-content.json, the appearances file and the sprite sheets.</summary>
    public required string AssetsFolder { get; init; }

    /// <summary>Folder that receives Tibia.dat and Tibia.spr.</summary>
    public required string OutputFolder { get; init; }

    public LegacyClientProfile Profile { get; init; } = LegacyClientProfiles.Client11;

    /// <summary>Store sprites fully transparent pixels as "empty" (sprite ID 0) instead of writing them.</summary>
    public bool SkipEmptySprites { get; init; } = true;

    /// <summary>Extended attributes to write for third-party legacy-format tools (none by default: CipSoft clients cannot read them).</summary>
    public IReadOnlySet<LegacyAttribute> ExtensionAttributes { get; init; } = new HashSet<LegacyAttribute>();

    /// <summary>Market names longer than this are shortened (0 = no limit).</summary>
    public int MaxMarketNameLength { get; init; }

    /// <summary>
    /// Market categories above this value are stored as "Others" (0 = keep every category). Clients
    /// of the 10.98 era know categories 1–22; later ones added Tibia Coins (23), creature products
    /// (24), quivers (25), soul cores (26) and fist weapons (27).
    /// </summary>
    public int MaxMarketCategory { get; init; }

    /// <summary>Also save every 32×32 slice as PNG into this folder (null = no slices).</summary>
    public string? SlicesFolder { get; init; }

    public int WorkerThreads { get; init; } = Environment.ProcessorCount;

    /// <summary>Stop at the first object that cannot be converted instead of writing it empty.</summary>
    public bool StopOnError { get; init; }

    /// <summary>Market name limit recommended for a profile (client 11 cannot open files with names of 30 or more characters).</summary>
    public static int DefaultMarketNameLimit(LegacyClientProfile profile) => profile.Id == LegacyClientProfiles.Client11.Id ? 29 : 0;

    /// <summary>The highest market category clients of the 10.98 era know.</summary>
    public const int LegacyMarketCategoryLimit = 22;

    /// <summary>Market category "Others", used for categories the target client does not know.</summary>
    public const ushort MarketCategoryOthers = 9;
}

/// <summary>Outcome of a conversion.</summary>
public sealed class ClientConversionResult
{
    public string? DatPath { get; set; }
    public string? SprPath { get; set; }
    public int Items { get; set; }
    public int Outfits { get; set; }
    public int Effects { get; set; }
    public int Missiles { get; set; }
    public int SpritesWritten { get; set; }
    public int EmptyTilesSkipped { get; set; }
    public int SlicesWritten { get; set; }
    public long FlattenedPixels { get; set; }
    public int MarketNamesShortened { get; set; }
    public int MarketCategoriesMapped { get; set; }

    /// <summary>Objects written empty because they could not be converted (with the reason).</summary>
    public List<string> Failures { get; } = [];

    /// <summary>Modern information the legacy format cannot hold: field → number of objects.</summary>
    public SortedDictionary<string, int> NotStored { get; } = new(StringComparer.Ordinal);

    public List<string> Notes { get; } = [];

    /// <summary>What the check of the written files found (empty when validation was off).</summary>
    public string? Validation { get; set; }

    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Converts the modern client's assets (protobuf appearances and LZMA sprite sheets) to a legacy
/// Tibia.dat and Tibia.spr pair. Re-implements the features of ClientConverter by Arch-Mina (and
/// the legacy export of his Assets Editor) and corrects defects found in its source: animation loop
/// counts and start phases are written as stored, single-phase animations no longer produce
/// unreadable data, grounds without a speed get one, market names are written in the client's
/// encoding with the right length, the appearances file is found by type, slices of tall and wide
/// sprites get unique names, and the written files are read back and compared before they replace
/// anything.
/// </summary>
public static class ClientConverter
{
    private const int Tile = 32;

    public static ClientConversionResult Run(ClientConversionOptions options, OutputSession output, TempWorkspace temp,
        IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        var result = new ClientConversionResult();
        var profile = options.Profile;
        var catalog = AssetCatalog.Load(options.AssetsFolder);
        string appearancesFile = TibiaClientPaths.FindAssetFile(options.AssetsFolder, AssetTypes.Appearances)
                                 ?? throw new FileNotFoundException("The assets folder has no appearances file listed in catalog-content.json.");
        var appearances = EditableAppearances.Parser.ParseFrom(File.ReadAllBytes(appearancesFile));
        progress?.Report(new OperationProgress(0, catalog.Sheets.Count, L.T("ClientConverter.ReadingSpriteSheets")));

        using var lease = temp.Begin("convert");
        string sprTemp = lease.PathFor("Tibia.spr.data");
        var tiles = new TileTable(catalog.HighestSpriteId);
        var expected = new List<ulong>(capacity: 1 << 20);
        using (var spr = new LegacySprWriter(sprTemp, profile))
        {
            WriteSprites(catalog, options, spr, tiles, expected, result, output, progress, ct);
            result.SpritesWritten = spr.Count;
            result.FlattenedPixels = spr.FlattenedPixels;

            progress?.Report(new OperationProgress(0, 0, L.T("ClientConverter.ConvertingObjects")));
            var dat = BuildDat(appearances, catalog, tiles, options, result);
            byte[] datBytes = LegacyDatCodec.Write(dat, profile);

            Directory.CreateDirectory(options.OutputFolder);
            progress?.Report(new OperationProgress(0, 0, L.T("ClientConverter.WritingTibiaSpr")));
            result.SprPath = output.WriteFile(Path.Combine(options.OutputFolder, "Tibia.spr"),
                path => spr.Finish(path, ct),
                path => result.Validation = ValidateSpr(path, profile, expected, options.WorkerThreads, progress, ct));
            if (result.SprPath == null)
            {
                result.Notes.Add(L.T("ClientConverter.TibiaSprWasNotWrittenThe"));
                result.Duration = DateTime.UtcNow - started;
                return result;
            }
            result.DatPath = output.WriteFile(Path.Combine(options.OutputFolder, "Tibia.dat"),
                path => File.WriteAllBytes(path, datBytes),
                path => ValidateDat(path, dat, profile, (uint)result.SpritesWritten));
            if (result.Validation != null && output.Options.ValidateOutputs)
                result.Validation += L.F("ClientConverter.TibiaDatReadBackWithThe", profile.Name, dat.All.Count());
        }
        if (result.FlattenedPixels > 0)
            result.Notes.Add(L.F("ClientConverter.0N0PartlyTransparentPixelsBecame", result.FlattenedPixels, profile.Name));
        if (result.MarketNamesShortened > 0)
            result.Notes.Add(L.F("ClientConverter.0N0MarketNamesWereShortened", result.MarketNamesShortened, options.MaxMarketNameLength));
        if (result.MarketCategoriesMapped > 0)
            result.Notes.Add(L.F("ClientConverter.0N0ItemsOfMarketCategories", result.MarketCategoriesMapped, options.MaxMarketCategory));
        result.Duration = DateTime.UtcNow - started;
        AppLog.Info($"Client conversion ({profile.Name}) of {options.AssetsFolder}: {result.Items:N0} items, {result.Outfits:N0} outfits, {result.Effects:N0} effects, {result.Missiles:N0} missiles, {result.SpritesWritten:N0} sprites, {L.Count(result.Failures.Count, "Noun.failure")}, {result.Duration.TotalSeconds:N0} s.");
        return result;
    }

    /// <summary>Legacy sprite IDs of every 32×32 tile of every modern sprite.</summary>
    private sealed class TileTable(int highestSpriteId)
    {
        private readonly int[] _first = Enumerable.Repeat(-1, highestSpriteId + 1).ToArray();
        private readonly List<uint> _ids = new(1 << 20);

        public void Set(int spriteId, ReadOnlySpan<uint> legacyIds)
        {
            _first[spriteId] = _ids.Count;
            foreach (uint id in legacyIds) _ids.Add(id);
        }

        /// <summary>Legacy IDs of the tiles of a sprite in legacy order, or false when the sprite is unknown.</summary>
        public bool TryGet(uint spriteId, int count, Span<uint> into)
        {
            if (spriteId >= _first.Length || _first[spriteId] < 0) return false;
            int start = _first[spriteId];
            for (int i = 0; i < count; i++) into[i] = _ids[start + i];
            return true;
        }
    }

    private static void WriteSprites(AssetCatalog catalog, ClientConversionOptions options, LegacySprWriter spr, TileTable tiles, List<ulong> expected,
        ClientConversionResult result, OutputSession output, IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        var sheets = catalog.Sheets;
        int batch = Math.Max(1, options.WorkerThreads) * 4;
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.WorkerThreads), CancellationToken = ct };
        var attribution = options.SlicesFolder != null ? Attribution.ForNewAsset() : null;
        Span<uint> ids = stackalloc uint[4];
        for (int start = 0; start < sheets.Count; start += batch)
        {
            int count = Math.Min(batch, sheets.Count - start);
            var decoded = new RgbaImage[count][];
            Parallel.For(0, count, parallel, i => decoded[i] = SpriteSheetDecoder.DecodeSprites(catalog, sheets[start + i]));
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var sheet = sheets[start + i];
                int w = sheet.SpriteWidth / Tile, h = sheet.SpriteHeight / Tile;
                for (int s = 0; s < decoded[i].Length; s++)
                {
                    var sprite = decoded[i][s];
                    int spriteId = sheet.FirstSpriteId + s;
                    // Legacy tile (tw, th) counts from the bottom-right corner of the sprite.
                    for (int th = 0; th < h; th++)
                    for (int tw = 0; tw < w; tw++)
                    {
                        var tile = sprite.Crop((w - 1 - tw) * Tile, (h - 1 - th) * Tile, Tile, Tile);
                        if (options.SkipEmptySprites && tile.Pixels.All(p => p >> 24 == 0))
                        {
                            ids[th * w + tw] = 0;
                            result.EmptyTilesSkipped++;
                            continue;
                        }
                        ids[th * w + tw] = spr.Add(tile);
                        expected.Add(PixelHash(LegacySprWriter.AsStored(tile, options.Profile.Transparency).Pixels));
                    }
                    tiles.Set(spriteId, ids[..(w * h)]);
                    if (options.SlicesFolder != null) WriteSlices(options.SlicesFolder, spriteId, sprite, w, h, attribution!, output, result);
                }
                decoded[i] = [];
            }
            progress?.Report(new OperationProgress(start + count, sheets.Count, L.F("ClientConverter.SpriteSheets0N0Sprites", spr.Count)));
        }
    }

    /// <summary>
    /// Slices in reading order: "&lt;id&gt;.png" for 32×32 sprites, "&lt;id&gt;_1.png" … "&lt;id&gt;_4.png"
    /// (top left, top right, bottom left, bottom right) for larger ones.
    /// </summary>
    private static void WriteSlices(string folder, int spriteId, RgbaImage sprite, int w, int h, AttributionInfo attribution, OutputSession output, ClientConversionResult result)
    {
        int n = 0;
        for (int row = 0; row < h; row++)
        for (int col = 0; col < w; col++)
        {
            n++;
            string name = w * h == 1 ? $"{spriteId}.png" : $"{spriteId}_{n}.png";
            byte[] png = ImageIO.EncodePng(sprite.Crop(col * Tile, row * Tile, Tile, Tile), attribution);
            if (output.WriteAllBytes(Path.Combine(folder, name), png) != null) result.SlicesWritten++;
        }
    }

    internal static ulong PixelHash(ReadOnlySpan<uint> pixels)
    {
        ulong h = 0xcbf29ce484222325;
        foreach (uint p in pixels)
        {
            h ^= p;
            h *= 0x100000001b3;
            h ^= h >> 29;
        }
        return h;
    }

    private static LegacyDatFile BuildDat(EditableAppearances appearances, AssetCatalog catalog, TileTable tiles, ClientConversionOptions options, ClientConversionResult result)
    {
        var dat = new LegacyDatFile { Signature = options.Profile.DatSignature };
        Fill(dat.Items, LegacyCategory.Item, appearances.Object, catalog, tiles, options, result);
        Fill(dat.Outfits, LegacyCategory.Outfit, appearances.Outfit, catalog, tiles, options, result);
        Fill(dat.Effects, LegacyCategory.Effect, appearances.Effect, catalog, tiles, options, result);
        Fill(dat.Missiles, LegacyCategory.Missile, appearances.Missile, catalog, tiles, options, result);
        result.Items = dat.Items.Count;
        result.Outfits = dat.Outfits.Count;
        result.Effects = dat.Effects.Count;
        result.Missiles = dat.Missiles.Count;
        return dat;
    }

    private static void Fill(List<LegacyThing> list, LegacyCategory category, IList<EditableAppearance> source, AssetCatalog catalog, TileTable tiles,
        ClientConversionOptions options, ClientConversionResult result)
    {
        uint first = LegacyDatFile.FirstId(category);
        var byId = new Dictionary<uint, EditableAppearance>();
        foreach (var a in source)
        {
            if (a.Id < first) { result.Failures.Add(L.F("ClientConverter.01IDsBelow2Cannot", category, a.Id, first)); continue; }
            if (!byId.TryAdd(a.Id, a)) result.Failures.Add(L.F("ClientConverter.01ListedTwiceTheFirst", category, a.Id));
        }
        uint last = byId.Count == 0 ? first - 1 : byId.Keys.Max();
        if (last > ushort.MaxValue) throw new InvalidDataException($"{category} IDs go up to {last:N0}, more than the legacy format can count (65,535).");
        for (uint id = first; id <= last; id++)
        {
            if (!byId.TryGetValue(id, out var appearance))
            {
                list.Add(Blank(category, id, options.Profile));
                continue;
            }
            try
            {
                list.Add(Convert(appearance, category, catalog, tiles, options, result));
            }
            catch (ConversionException ex)
            {
                if (options.StopOnError) throw new InvalidDataException($"{category} {id}: {ex.Message}", ex);
                result.Failures.Add(L.F("ClientConverter.012WrittenAsAn", category, id, ex.Message));
                list.Add(Blank(category, id, options.Profile));
            }
        }
    }

    private static LegacyThing Blank(LegacyCategory category, uint id, LegacyClientProfile profile) => LegacyThing.Blank(category, id, profile.FrameGroups);

    private sealed class ConversionException(string message) : Exception(message);

    private static LegacyThing Convert(EditableAppearance a, LegacyCategory category, AssetCatalog catalog, TileTable tiles, ClientConversionOptions options, ClientConversionResult result)
    {
        var thing = new LegacyThing(category, a.Id);
        var flags = a.Flags ?? new AppearanceFlags();
        AddAttributes(thing, a, flags, options, result);

        var groups = a.FrameGroup.Where(g => g.SpriteInfo != null && g.SpriteInfo.SpriteId.Count > 0).ToList();
        if (groups.Count == 0)
        {
            thing.FrameGroups.Add(new LegacyFrameGroup());
            return thing;
        }
        bool keepGroups = category == LegacyCategory.Outfit && options.Profile.FrameGroups;
        if (!keepGroups && groups.Count > 1)
        {
            CountNotStored(result, L.T("ClientConverter.FrameGroupsBeyondTheFirstNon"));
            groups = [groups[0]];
        }
        foreach (var g in groups)
            thing.FrameGroups.Add(ConvertGroup(g, keepGroups, catalog, tiles, options));
        return thing;
    }

    private static LegacyFrameGroup ConvertGroup(FrameGroup g, bool keepGroups, AssetCatalog catalog, TileTable tiles, ClientConversionOptions options)
    {
        var info = g.SpriteInfo;
        var firstSheet = catalog.FindSheet((int)info.SpriteId[0]) ?? throw new ConversionException($"sprite {info.SpriteId[0]} is not in any sprite sheet");
        int w = firstSheet.SpriteWidth / Tile, h = firstSheet.SpriteHeight / Tile;
        uint layers = Math.Max(1, info.Layers), px = Math.Max(1, info.PatternWidth), py = Math.Max(1, info.PatternHeight), pz = Math.Max(1, info.PatternDepth);
        uint perPhase = layers * px * py * pz;
        int phases = info.Animation != null && info.Animation.SpritePhase.Count > 0 ? info.Animation.SpritePhase.Count : (int)(info.SpriteId.Count / perPhase);
        if (perPhase * phases != info.SpriteId.Count)
            throw new ConversionException($"{info.SpriteId.Count} sprites do not match {L.Count(layers, "Noun.layer")} × {px}×{py}×{pz} patterns × {L.Count(phases, "Noun.phase")}");
        if (layers > 255 || px > 255 || py > 255 || pz > 255 || phases > 255)
            throw new ConversionException("a pattern or phase count is larger than 255");
        if (!options.Profile.HasPatternZ && pz > 1) throw new ConversionException("pattern Z is not supported by the target client");

        var group = new LegacyFrameGroup
        {
            Type = keepGroups ? (byte)Math.Clamp(g.FixedFrameGroup, 0, 255) : (byte)0,
            Width = (byte)w, Height = (byte)h,
            ExactSize = ExactSize(info, w, h),
            Layers = (byte)layers, PatternX = (byte)px, PatternY = (byte)py, PatternZ = (byte)pz, Phases = (byte)phases,
        };
        if (phases > 1 && options.Profile.EnhancedAnimations)
        {
            var anim = info.Animation ?? throw new ConversionException($"{phases} phases without animation timing");
            group.Animation = new LegacyAnimation
            {
                Synchronized = anim.Synchronized,
                // Modern loop type: 0 forever, -1 ping-pong, 1 counted (loop_count times).
                LoopCount = anim.LoopType switch { -1 => -1, 1 => (int)Math.Min(anim.LoopCount, int.MaxValue), _ => 0 },
                StartPhase = anim.RandomStartPhase ? (sbyte)-1 : (sbyte)Math.Min(anim.DefaultStartPhase, 127),
                Durations = anim.SpritePhase.Select(p => (p.DurationMin, p.DurationMax)).ToList(),
            };
        }
        int tilesPer = w * h;
        group.Sprites = new uint[info.SpriteId.Count * tilesPer];
        Span<uint> ids = stackalloc uint[4];
        for (int k = 0; k < info.SpriteId.Count; k++)
        {
            uint spriteId = info.SpriteId[k];
            var sheet = catalog.FindSheet((int)spriteId) ?? throw new ConversionException($"sprite {spriteId} is not in any sprite sheet");
            if (sheet.Type != firstSheet.Type) throw new ConversionException($"its sprites have different sizes ({firstSheet.SizeLabel} and {sheet.SizeLabel})");
            if (!tiles.TryGet(spriteId, tilesPer, ids)) throw new ConversionException($"sprite {spriteId} could not be read");
            ids[..tilesPer].CopyTo(group.Sprites.AsSpan(k * tilesPer));
        }
        return group;
    }

    /// <summary>The "exact size" byte: the bounding square stored by the client, else the largest bounding box, else the sprite size.</summary>
    private static byte ExactSize(SpriteInfo info, int w, int h)
    {
        uint size = info.BoundingSquare;
        if (size == 0 && info.BoundingBoxPerDirection.Count > 0) size = info.BoundingBoxPerDirection.Max(b => Math.Max(b.Width, b.Height));
        if (size == 0) size = (uint)(Math.Max(w, h) * Tile);
        return (byte)Math.Min(size, 255);
    }

    private static void CountNotStored(ClientConversionResult result, string what) => result.NotStored[what] = result.NotStored.GetValueOrDefault(what) + 1;

    private static void AddAttributes(LegacyThing thing, EditableAppearance a, AppearanceFlags f, ClientConversionOptions options, ClientConversionResult result)
    {
        var list = new List<LegacyAttributeValue>();
        void Add(LegacyAttribute attr, ushort x = 0, ushort y = 0, LegacyMarket? market = null) => list.Add(new LegacyAttributeValue(attr, x, y, market));
        static ushort U16(uint v) => (ushort)Math.Min(v, ushort.MaxValue);

        if (f.Bank != null) Add(LegacyAttribute.Ground, U16(f.Bank.Waypoints));
        if (f.Clip) Add(LegacyAttribute.GroundBorder);
        if (f.Bottom) Add(LegacyAttribute.OnBottom);
        if (f.Top) Add(LegacyAttribute.OnTop);
        if (f.Container) Add(LegacyAttribute.Container);
        if (f.Cumulative) Add(LegacyAttribute.Stackable);
        if (f.Forceuse) Add(LegacyAttribute.ForceUse);
        if (f.Multiuse) Add(LegacyAttribute.MultiUse);
        if (f.Write != null) Add(LegacyAttribute.Writable, U16(f.Write.MaxTextLength));
        if (f.WriteOnce != null) Add(LegacyAttribute.WritableOnce, U16(f.WriteOnce.MaxTextLengthOnce));
        if (f.Liquidcontainer) Add(LegacyAttribute.FluidContainer);
        if (f.Liquidpool) Add(LegacyAttribute.Splash);
        if (f.Unpass) Add(LegacyAttribute.NotWalkable);
        if (f.Unmove) Add(LegacyAttribute.NotMoveable);
        if (f.Unsight) Add(LegacyAttribute.BlockProjectile);
        if (f.Avoid) Add(LegacyAttribute.NotPathable);
        if (f.NoMovementAnimation) Add(LegacyAttribute.NoMoveAnimation);
        if (f.Take) Add(LegacyAttribute.Pickupable);
        if (f.Hang) Add(LegacyAttribute.Hangable);
        if (f.Hook?.Direction == 1) Add(LegacyAttribute.HookSouth);
        if (f.Hook?.Direction == 2) Add(LegacyAttribute.HookEast);
        if (f.Rotate) Add(LegacyAttribute.Rotateable);
        if (f.Light != null) Add(LegacyAttribute.Light, U16(f.Light.Brightness), U16(f.Light.Color));
        if (f.DontHide) Add(LegacyAttribute.DontHide);
        if (f.Translucent) Add(LegacyAttribute.Translucent);
        if (f.Shift != null) Add(LegacyAttribute.Displacement, U16(f.Shift.X), U16(f.Shift.Y));
        if (f.Height != null) Add(LegacyAttribute.Elevation, U16(f.Height.Elevation));
        if (f.LyingObject) Add(LegacyAttribute.LyingCorpse);
        if (f.AnimateAlways) Add(LegacyAttribute.AnimateAlways);
        if (f.Automap != null) Add(LegacyAttribute.MinimapColor, U16(f.Automap.Color));
        if (f.Lenshelp != null) Add(LegacyAttribute.LensHelp, U16(f.Lenshelp.Id));
        if (f.Fullbank) Add(LegacyAttribute.FullGround);
        if (f.IgnoreLook) Add(LegacyAttribute.IgnoreLook);
        if (f.Clothes != null) Add(LegacyAttribute.Cloth, U16(f.Clothes.Slot));
        if (f.Market != null)
        {
            byte[] name = a.HasName ? a.Name.ToByteArray() : [];
            if (options.MaxMarketNameLength > 0 && name.Length > options.MaxMarketNameLength)
            {
                name = name[..options.MaxMarketNameLength];
                result.MarketNamesShortened++;
            }
            // Vocations as the legacy bit mask (knight 1, paladin 2, sorcerer 4, druid 8, monk 16). Current
            // clients keep the restrictions in the flags; older ones inside the market data.
            ushort vocations = 0;
            foreach (int v in f.RestrictToVocation.Count > 0 ? f.RestrictToVocation : f.Market.RestrictToVocation)
                if (v is >= 1 and <= 5) vocations |= (ushort)(1 << (v - 1));
            uint level = f.HasMinimumLevel ? f.MinimumLevel : f.Market.MinimumLevel;
            ushort marketCategory = U16((uint)Math.Max(0, f.Market.Category));
            if (options.MaxMarketCategory > 0 && marketCategory > options.MaxMarketCategory)
            {
                marketCategory = ClientConversionOptions.MarketCategoryOthers;
                result.MarketCategoriesMapped++;
            }
            Add(LegacyAttribute.Market, market: new LegacyMarket(marketCategory, U16(f.Market.TradeAsObjectId), U16(f.Market.ShowAsObjectId),
                name, vocations, U16(level)));
        }
        if (f.DefaultAction != null) Add(LegacyAttribute.DefaultAction, U16((uint)Math.Max(0, f.DefaultAction.Action)));
        if (f.Wrap) Add(LegacyAttribute.Wrappable);
        if (f.Unwrap) Add(LegacyAttribute.Unwrappable);
        if (f.Topeffect) Add(LegacyAttribute.TopEffect);
        if (f.Usable) Add(LegacyAttribute.Usable);

        var ext = options.ExtensionAttributes;
        if (f.Changedtoexpire != null) { if (ext.Contains(LegacyAttribute.ChangedToExpire)) Add(LegacyAttribute.ChangedToExpire, U16(f.Changedtoexpire.FormerObjectTypeid)); else CountNotStored(result, L.T("ClientConverter.ChangedToExpire2")); }
        if (f.Corpse) { if (ext.Contains(LegacyAttribute.Corpse)) Add(LegacyAttribute.Corpse); else CountNotStored(result, "corpse"); }
        if (f.PlayerCorpse) { if (ext.Contains(LegacyAttribute.PlayerCorpse)) Add(LegacyAttribute.PlayerCorpse); else CountNotStored(result, L.T("ClientConverter.PlayerCorpse2")); }
        if (f.Cyclopediaitem != null) { if (ext.Contains(LegacyAttribute.CyclopediaItem)) Add(LegacyAttribute.CyclopediaItem, U16(f.Cyclopediaitem.CyclopediaType)); else CountNotStored(result, L.T("ClientConverter.CyclopediaItem2")); }
        if (f.Ammo) { if (ext.Contains(LegacyAttribute.Ammo)) Add(LegacyAttribute.Ammo); else CountNotStored(result, "ammunition"); }
        if (f.ShowOffSocket) { if (ext.Contains(LegacyAttribute.ShowOffSocket)) Add(LegacyAttribute.ShowOffSocket); else CountNotStored(result, L.T("ClientConverter.ShowOffSocket2")); }
        if (f.Reportable) { if (ext.Contains(LegacyAttribute.Reportable)) Add(LegacyAttribute.Reportable); else CountNotStored(result, "reportable"); }
        if (f.Upgradeclassification != null) { if (ext.Contains(LegacyAttribute.UpgradeClassification)) Add(LegacyAttribute.UpgradeClassification, U16(f.Upgradeclassification.UpgradeClassification)); else CountNotStored(result, L.T("ClientConverter.UpgradeClassification2")); }
        if (f.Wearout) { if (ext.Contains(LegacyAttribute.WearOut)) Add(LegacyAttribute.WearOut); else CountNotStored(result, "wear-out"); }
        if (f.Clockexpire) { if (ext.Contains(LegacyAttribute.ClockExpire)) Add(LegacyAttribute.ClockExpire); else CountNotStored(result, L.T("ClientConverter.ClockExpire2")); }
        if (f.Expire) { if (ext.Contains(LegacyAttribute.Expire)) Add(LegacyAttribute.Expire); else CountNotStored(result, "expire"); }
        if (f.Expirestop) { if (ext.Contains(LegacyAttribute.ExpireStop)) Add(LegacyAttribute.ExpireStop); else CountNotStored(result, L.T("ClientConverter.ExpireStop2")); }

        // Information the legacy format has no place for.
        if (f.Npcsaledata.Count > 0) CountNotStored(result, L.T("ClientConverter.NPCSaleData"));
        if (f.ReverseAddonsEast || f.ReverseAddonsWest || f.ReverseAddonsSouth || f.ReverseAddonsNorth) CountNotStored(result, L.T("ClientConverter.ReversedAddons"));
        if (f.DecoItemKit) CountNotStored(result, L.T("ClientConverter.DecorationKit"));
        if (f.SkillwheelGem != null) CountNotStored(result, L.T("ClientConverter.SkillWheelGem"));
        if (f.DualWielding) CountNotStored(result, L.T("ClientConverter.DualWielding"));
        if (f.Imbueable != null) CountNotStored(result, L.T("ClientConverter.ImbuementSlots"));
        if (f.Proficiency != null) CountNotStored(result, "proficiency");
        if (f.HasWeaponType) CountNotStored(result, L.T("ClientConverter.WeaponType"));
        if ((f.RestrictToVocation.Count > 0 || f.HasMinimumLevel) && f.Market == null) CountNotStored(result, L.T("ClientConverter.VocationAndLevelRestrictionsStoredOnly"));
        if (a.HasName && f.Market == null) CountNotStored(result, L.T("ClientConverter.NamesStoredOnlyWithMarketData"));
        if (a.HasDescription) CountNotStored(result, "descriptions");

        // The client reads attributes in any order; ascending byte order matches CipSoft's files.
        foreach (var v in list.OrderBy(v => LegacyAttributes.ToByte(options.Profile.Layout, v.Attribute) ?? 0)) thing.Attributes.Add(v);
    }

    private static void ValidateDat(string path, LegacyDatFile expected, LegacyClientProfile profile, uint spriteCount)
    {
        var read = LegacyDatCodec.Read(path, profile);
        foreach (var category in Enum.GetValues<LegacyCategory>())
        {
            var a = expected.List(category);
            var b = read.List(category);
            if (a.Count != b.Count) throw new InvalidDataException($"Validation: Tibia.dat has {b.Count:N0} {category} objects after writing, {a.Count:N0} were intended.");
            for (int i = 0; i < a.Count; i++)
                if (!a[i].SameAs(b[i])) throw new InvalidDataException($"Validation: {category} {a[i].Id} reads back differently from what was written.");
        }
        uint max = read.MaxSpriteId;
        if (max > spriteCount) throw new InvalidDataException($"Validation: Tibia.dat uses sprite {max:N0}, but Tibia.spr has only {spriteCount:N0}.");
    }

    private static string ValidateSpr(string path, LegacyClientProfile profile, List<ulong> expected, int threads, IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        using var reader = new LegacySprReader(path, profile);
        if (reader.Signature != profile.SprSignature) throw new InvalidDataException($"Validation: Tibia.spr has signature 0x{reader.Signature:X8}, 0x{profile.SprSignature:X8} was intended.");
        if (reader.Count != expected.Count) throw new InvalidDataException($"Validation: Tibia.spr holds {reader.Count:N0} sprites, {expected.Count:N0} were written.");
        progress?.Report(new OperationProgress(0, expected.Count, L.T("ClientConverter.CheckingTibiaSpr")));
        int done = 0;
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads), CancellationToken = ct };
        Parallel.For(1, expected.Count + 1, parallel, () => 0, (id, _, local) =>
        {
            var image = reader.Read((uint)id) ?? new RgbaImage(32, 32);
            if (PixelHash(image.Pixels) != expected[id - 1]) throw new InvalidDataException($"Validation: sprite {id} in Tibia.spr does not match the source image.");
            if (++local % 50000 == 0) progress?.Report(new OperationProgress(Interlocked.Add(ref done, 50000), expected.Count, L.T("ClientConverter.CheckingTibiaSpr")));
            return local;
        }, _ => { });
        return L.F("ClientConverter.TibiaSprReadBackAll0", expected.Count);
    }

    /// <summary>Text of a legacy market name (Latin-1, as the client stores it).</summary>
    public static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);
}
