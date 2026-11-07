using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Compatibility;
using System.Globalization;
using System.Text.RegularExpressions;
using Tibia.Protobuf.Map;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Map;

/// <summary>Which map resource types to extract.</summary>
/// <param name="Attribution">Embedded in every extracted tile (the configured creator when null).</param>
public sealed record MapExtractionOptions(string InputDirectory, string OutputDirectory, bool Subareas = true, bool Satellite = true, bool Minimap = true,
    AttributionInfo? Attribution = null)
{
    /// <summary>The assets folder the tiles are read from when it is not a real folder (an imported version, 7.3); null reads <see cref="InputDirectory"/>.</summary>
    public Clients.IClientFolder? Input { get; init; }

    internal string? TilePath(string name) => Input != null ? Input.PathOf(name) : File.Exists(Path.Combine(InputDirectory, name)) ? Path.Combine(InputDirectory, name) : null;
}

public enum HrMapType { Satellite, Minimap }

/// <summary>Options of "Build HR Map".</summary>
public sealed record HrMapOptions
{
    public required string InputDirectory { get; init; }
    public HrMapType Type { get; init; } = HrMapType.Satellite;
    /// <summary>Zoom level in the file names: satellite 1–3 (50 %, 100 %, 200 %), minimap 1–2 (50 %, 100 %).</summary>
    public int Zoom { get; init; } = 3;
    /// <summary>Highest floor drawn, 0 (ground, z = 7) to 7 (z = 0).</summary>
    public int FloorsAboveGround { get; init; }
    public bool Cumulative { get; init; } = true;
    public bool FloorOffset { get; init; } = true;
    public bool Watermark { get; init; }
}

/// <summary>
/// Map tile extraction and high-resolution map assembly (Map Tools → Map Tools tab).
/// </summary>
public static partial class MapImageTools
{
    /// <summary>Scale factors in the order of the original "zoom" index (1 = 1/64, 2 = 1/32, 3 = 1/16).</summary>
    private static readonly double[] ScaleFactors = [0.0, 1.0 / 64.0, 1.0 / 32.0, 0.0625];

    /// <summary>Pixels per map field for each zoom index used by the HR builder.</summary>
    private static readonly double[] PixelsPerField = [0.0, 0.5, 1.0, 2.0];

    public static readonly uint WaterColor = 0xFF284DA6;   // (40, 77, 166)

    /// <summary>
    /// Output name "&lt;type&gt;_&lt;zoomOrId&gt;_X_Y_Z.png": zoom index for satellite/minimap tiles,
    /// area id for subarea images (the original format).
    /// </summary>
    public static string OutputName(MapFile file)
    {
        string type = MapDatabase.FileTypeName(file.FileType).ToLowerInvariant();
        string zoomOrId = file.FileType == MAP_FILE_TYPE.Subarea
            ? file.AreaId.ToString()
            : Array.IndexOf(ScaleFactors, file.ScaleFactor).ToString(CultureInfo.InvariantCulture);
        var c = file.TopLeftCoordinate;
        return $"{type}_{zoomOrId}_{c?.X ?? 0}_{c?.Y ?? 0}_{c?.Z ?? 0}.png";
    }

    /// <summary>"Extract Map Files": decodes every selected resource file of map.dat to PNG.</summary>
    public static async Task<BatchResult> ExtractAsync(MapDatabase map, MapExtractionOptions options, IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        var selected = map.Data.ResourceFiles.Where(f => f.FileType switch
        {
            MAP_FILE_TYPE.Subarea => options.Subareas,
            MAP_FILE_TYPE.Satellite => options.Satellite,
            MAP_FILE_TYPE.Minimap => options.Minimap,
            _ => false,
        }).ToList();
        var result = new BatchResult();
        var attribution = options.Attribution ?? Attribution.ForNewAsset();
        int done = 0;
        var gate = new object();
        await Parallel.ForEachAsync(selected, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, (file, token) =>
        {
            string? failure = null;
            bool skipped = false;
            string? name = TibiaFormat.SafeFileName(file.FileName);
            try
            {
                if (name == null) failure = L.F("MapImageTools.0UnsafeOrEmptyFileName", file.FileName);
                else if (options.TilePath(name) is not { } tile) failure = L.F("MapImageTools.0ListedInTheMapData", name, options.Input?.DisplayPath ?? options.InputDirectory);
                else
                {
                    var image = BmpDecoder.Decode(CipLzma.DecompressFile(tile));
                    ImageIO.SavePng(image, Path.Combine(options.OutputDirectory, OutputName(file)), attribution);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failure = $"{file.FileName}: {ex.Message}";
                AppLog.Warn("Map file extraction failed: " + failure);
            }
            lock (gate)
            {
                if (failure != null) result.Failures.Add(failure);
                else if (skipped) result.Skipped++;
                else result.Succeeded++;
                progress?.Report(new OperationProgress(++done, selected.Count, file.FileName));
            }
            return ValueTask.CompletedTask;
        });
        return result;
    }

    [GeneratedRegex(@"^(satellite|minimap)_(\d+)_(\d+)_(\d+)_(\d+)\.png$", RegexOptions.IgnoreCase)]
    private static partial Regex TileName();

    public sealed record Tile(string Path, int X, int Y, int Z);

    public static List<Tile> FindTiles(string directory, HrMapType type, int zoom)
    {
        var tiles = new List<Tile>();
        if (!Directory.Exists(directory)) return tiles;
        foreach (var file in Directory.EnumerateFiles(directory, "*.png"))
        {
            var m = TileName().Match(Path.GetFileName(file));
            if (!m.Success || !m.Groups[1].Value.Equals(type.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
            // Numbers too large for int are not valid tile coordinates: skip the file instead of crashing.
            if (!int.TryParse(m.Groups[2].Value, out int fileZoom) || fileZoom != zoom ||
                !int.TryParse(m.Groups[3].Value, out int x) || !int.TryParse(m.Groups[4].Value, out int y) || !int.TryParse(m.Groups[5].Value, out int z)) continue;
            tiles.Add(new Tile(file, x, y, z));
        }
        return tiles;
    }

    /// <summary>
    /// "Build HR": stitches extracted tiles into one image. Floors are drawn from the ground up to
    /// the chosen floor (cumulative) or just that floor, each upper floor shifted one field
    /// up-left when "floor offset" is enabled; water blue background for ground/cumulative maps,
    /// black otherwise; optional faint TibiaWiki watermark grid.
    /// </summary>
    /// <remarks>
    /// Fixed: the original hard-coded the world origin (31872, 30976) and sized the canvas from
    /// the first/last ground file names, so tiles outside that box were clipped.
    /// </remarks>
    /// <summary>The tile files <see cref="BuildHighResMap"/> draws (to read their attribution before saving).</summary>
    public static IReadOnlyList<string> TilesUsed(HrMapOptions o)
    {
        int topZ = 7 - Math.Clamp(o.FloorsAboveGround, 0, 7);
        var floors = o.Cumulative ? Enumerable.Range(topZ, 8 - topZ).ToHashSet() : [topZ];
        return FindTiles(o.InputDirectory, o.Type, o.Zoom).Where(t => floors.Contains(t.Z)).Select(t => t.Path).ToList();
    }

    /// <summary>The original Cyclopedia floor exports that <see cref="CropCyclopediaMaps"/> reads.</summary>
    public static IReadOnlyList<string> CyclopediaInputs(string inputDirectory) =>
        Enumerable.Range(0, 8).Select(z => Path.Combine(inputDirectory, TibiaFormat.CyclopediaFloorFile(z))).Where(File.Exists).ToList();

    public static RgbaImage BuildHighResMap(HrMapOptions o, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        if (o.Zoom < 1 || o.Zoom >= PixelsPerField.Length) throw new ArgumentOutOfRangeException(nameof(o), "Invalid zoom level.");
        double scale = PixelsPerField[o.Zoom];
        int topZ = 7 - Math.Clamp(o.FloorsAboveGround, 0, 7);
        var floors = o.Cumulative ? Enumerable.Range(topZ, 8 - topZ).Reverse().ToList() : new List<int> { topZ };
        var all = FindTiles(o.InputDirectory, o.Type, o.Zoom);
        var ground = all.Where(t => t.Z == 7).ToList();
        if (all.Count == 0 || ground.Count == 0)
            throw new FileNotFoundException($"No {o.Type.ToString().ToLowerInvariant()} files with zoom {o.Zoom} were found in {o.InputDirectory}. Extract the map files first.");
        var tiles = floors.SelectMany(z => all.Where(t => t.Z == z)).ToList();

        // Canvas from the ground tiles' extent (world coordinates → pixels).
        int originX = ground.Min(t => t.X), originY = ground.Min(t => t.Y);
        int width = 0, height = 0;
        foreach (var t in ground)
        {
            var size = ReadPngSize(t.Path);
            width = Math.Max(width, (int)((t.X - originX) * scale) + size.Width);
            height = Math.Max(height, (int)((t.Y - originY) * scale) + size.Height);
        }
        var canvas = new RgbaImage(width, height);
        canvas.Clear(topZ == 7 || o.Cumulative ? WaterColor : 0xFF000000);
        int offset = o.FloorOffset ? 1 : 0;
        for (int i = 0; i < tiles.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var t = tiles[i];
            var image = ImageIO.Load(t.Path);
            int px = (int)((t.X - originX - (7 - t.Z) * offset) * scale);
            int py = (int)((t.Y - originY - (7 - t.Z) * offset) * scale);
            canvas.DrawImage(image, px, py);
            progress?.Report(new OperationProgress(i + 1, tiles.Count, Path.GetFileName(t.Path)));
        }
        if (o.Watermark) DrawWatermark(canvas);
        return canvas;
    }

    /// <summary>Tiles the TibiaWiki logo every 500×250 px at 10 % opacity, starting at (50, 50).</summary>
    public static void DrawWatermark(RgbaImage canvas)
    {
        var logo = Watermark.Value;
        var faint = new RgbaImage(logo.Width, logo.Height);
        for (int i = 0; i < logo.Pixels.Length; i++)
        {
            uint p = logo.Pixels[i];
            uint a = (uint)Math.Round((p >> 24) * 0.1);
            faint.Pixels[i] = a << 24 | (p & 0xFFFFFF);
        }
        for (int x = 0; x <= canvas.Width / 500; x++)
            for (int y = 0; y <= canvas.Height / 250; y++)
                canvas.DrawImage(faint, x * 500 + 50, y * 250 + 50);
    }

    private static readonly Lazy<RgbaImage> Watermark = new(() =>
    {
        using var stream = typeof(MapImageTools).Assembly.GetManifestResourceStream("TibiaFoundry.Core.Resources.tibiawiki_watermark.png")
            ?? throw new InvalidOperationException("Watermark resource missing.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ImageIO.LoadFromBytes(ms.ToArray());
    });

    private static (int Width, int Height) ReadPngSize(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using (var fs = File.OpenRead(path))
        {
            if (fs.Read(header) == 24 && header[12] == 'I' && header[13] == 'H' && header[14] == 'D' && header[15] == 'R')
                return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..]), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..]));
        }
        var img = ImageIO.Load(path);
        return (img.Width, img.Height);
    }

    /// <summary>
    /// "Crop Cyclopedia to minimap": cuts "floor-0Z-map.png" files (one per floor) into 256×256
    /// tiles named "Minimap_Color_X_Y_Z.png". Fixed: the original swapped the column/row loop
    /// counts, dropping tiles on non-square maps.
    /// </summary>
    /// <param name="attribution">Embedded in every tile; the configured creator when null.</param>
    public static BatchResult CropCyclopediaMaps(string inputDirectory, string outputDirectory, AttributionInfo? attribution = null,
        IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var result = new BatchResult();
        var info = attribution ?? Attribution.ForNewAsset();
        for (int z = 0; z <= 7; z++)
        {
            string source = Path.Combine(inputDirectory, TibiaFormat.CyclopediaFloorFile(z));
            if (!File.Exists(source)) { result.Failures.Add(L.F("MapImageTools.0NotFound", Path.GetFileName(source))); continue; }
            var full = ImageIO.Load(source);
            int nX = (int)Math.Ceiling(full.Width / 256.0) + 1;
            int nY = (int)Math.Ceiling(full.Height / 256.0) + 1;
            uint background = z == 0 ? 0xFF336699 : 0xFF000000;
            for (int x = 0; x < nX; x++)
            {
                for (int y = 0; y < nY; y++)
                {
                    ct.ThrowIfCancellationRequested();
                    var tile = new RgbaImage(256, 256);
                    tile.Clear(background);
                    tile.DrawImage(full.Crop(x * 256 - 128, y * 256, 256, 256), 0, 0);
                    ImageIO.SavePng(tile, Path.Combine(outputDirectory, $"Minimap_Color_{x * 256 + TibiaFormat.CyclopediaOriginX}_{y * 256 + TibiaFormat.CyclopediaOriginY}_{7 - z}.png"), info);
                    result.Succeeded++;
                }
            }
            progress?.Report(new OperationProgress(z + 1, 8, L.F("MapImageTools.Floor0", 7 - z)));
        }
        return result;
    }
}
