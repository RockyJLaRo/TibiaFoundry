using Tibia.Protobuf.Staticmapdata;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Protobuf;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Houses;

/// <summary>Background choices of the House Images page (original order).</summary>
public enum HouseBackground { Gray, Magenta, Black, White, Transparent }

/// <summary>Per-floor images of one house plus its floor range.</summary>
public sealed class HouseFloors
{
    public required uint HouseId { get; init; }
    /// <summary>z of the highest floor (smallest z).</summary>
    public required int TopFloor { get; init; }
    public required int FloorCount { get; init; }
    public required int Columns { get; init; }
    public required int Rows { get; init; }
    /// <summary>Index 0 = <see cref="TopFloor"/>.</summary>
    public required IReadOnlyList<RgbaImage> Floors { get; init; }
    public int BottomFloor => TopFloor + FloorCount - 1;
    public List<string> Warnings { get; } = new();
}

/// <summary>Loaded <c>staticmapdata-*.dat</c>.</summary>
public sealed class StaticMapDatabase
{
    private readonly Dictionary<uint, HouseMap> _byId = new();

    public StaticMapDatabase(StaticMapData data)
    {
        Data = data;
        foreach (var h in data.HouseMaps) _byId.TryAdd(h.HouseId, h);
    }

    public StaticMapData Data { get; }

    public static StaticMapDatabase Load(string path) => new(ProtoLoader.Load(path, StaticMapData.Parser));

    public HouseMap? Find(uint houseId) => _byId.GetValueOrDefault(houseId);
}

/// <summary>
/// Renders houses from <c>staticmapdata</c> (port of <c>StaticMap_GenerateFloorImages</c> and
/// <c>StaticMap_JoinFloors</c>).
/// </summary>
/// <remarks>
/// Tiles are stored column by column (x outer, y inner), floor after floor; each entry lists the
/// objects on one tile and how many following tiles are empty ("skip"). Objects are drawn in
/// order with their pattern variant chosen from the absolute map position, offset by their
/// shift (displacement) flag and anchored at the bottom-right of the tile. Floors are stacked
/// with the classic one-tile up-left perspective shift.
/// Fixed: houses whose objects have fewer pattern sprites than their pattern size, or that
/// reference unknown objects/sprites, crashed the original (e.g. houses 10004 and 11015).
/// </remarks>
public static class HouseRenderer
{
    public static HouseFloors RenderFloors(HouseMap house, AppearanceDatabase appearances, ISpriteSource sprites)
    {
        var info = house.MapInfo ?? throw new InvalidDataException($"House {house.HouseId} has no map data.");
        int cols = (int)info.Dimension.TilesX + 1;
        int rows = (int)info.Dimension.TilesY + 1;
        int floors = (int)Math.Max(1, info.Dimension.TilesZ);
        int coordX = (int)info.HouseCoords.X, coordY = (int)info.HouseCoords.Y;
        var tiles = info.ObjectListA?.ObjectListB?.ObjectListC ?? new Google.Protobuf.Collections.RepeatedField<ObjectListC>();
        var result = new List<RgbaImage>();
        var warnings = new HashSet<string>();
        int tileCounter = 0, skip = 0;
        bool finished = tiles.Count == 0;

        for (int floor = 0; floor < floors; floor++)
        {
            var image = new RgbaImage(cols * 32, rows * 32);
            int currX = coordX;
            for (int col = 1; col < cols && !finished; col++)
            {
                int currY = coordY;
                for (int row = 1; row < rows; row++)
                {
                    if (skip > 0)
                    {
                        skip--;
                        currY++;
                        continue;
                    }
                    var tile = tiles[tileCounter];
                    foreach (var entry in tile.Tile)
                        DrawObject(image, entry.ObjectId, col, row, currX, currY, appearances, sprites, warnings);
                    skip = (int)tile.Skip;
                    tileCounter++;
                    if (tileCounter >= tiles.Count) { finished = true; break; }
                    currY++;
                }
                currX++;
            }
            result.Add(image);
        }
        var house_ = new HouseFloors
        {
            HouseId = house.HouseId, TopFloor = (int)info.HouseCoords.Z, FloorCount = floors, Columns = cols, Rows = rows, Floors = result,
        };
        house_.Warnings.AddRange(warnings);
        foreach (var w in warnings) AppLog.Warn($"House {house.HouseId}: {w}");
        return house_;
    }

    private static void DrawObject(RgbaImage image, uint objectId, int col, int row, int mapX, int mapY,
        AppearanceDatabase appearances, ISpriteSource sprites, HashSet<string> warnings)
    {
        var obj = appearances.Find(AppearanceKind.Object, objectId);
        if (obj == null || obj.FrameGroup.Count == 0) { warnings.Add(L.F("HouseRenderer.Object0NotFoundInAppearances", objectId)); return; }
        var info = obj.FrameGroup[0].SpriteInfo;
        if (info == null) { warnings.Add(L.F("HouseRenderer.Object0HasNoSpriteInformation", objectId)); return; }
        if (info.SpriteId.Count == 0) return;
        int index = 0;
        if (info.PatternWidth > 1)
        {
            int pW = (int)info.PatternWidth, pH = (int)Math.Max(1, info.PatternHeight);
            index = (mapY % pH) * pW + mapX % pW;
            if (index >= info.SpriteId.Count)
            {
                warnings.Add(L.F("HouseRenderer.Object0PatternSprite1Missing", objectId, index, info.SpriteId.Count, index % info.SpriteId.Count));
                index %= info.SpriteId.Count;
            }
        }
        var sprite = sprites.GetSprite(info.SpriteId[index]);
        if (sprite == null) { warnings.Add(L.F("HouseRenderer.Sprite0Object1NotFound", info.SpriteId[index], objectId)); return; }
        int offX = (int)(obj.Flags?.Shift?.X ?? 0), offY = (int)(obj.Flags?.Shift?.Y ?? 0);
        int x = 32 * col - (sprite.Width - 32) - offX;
        int y = 32 * row - (sprite.Height - 32) - offY;
        image.DrawImage(sprite, x, y);
    }

    public static uint BackgroundColor(HouseBackground bg) => bg switch
    {
        HouseBackground.Gray => 0xFFF0F0F0, // SystemColors.Control, as in the original
        HouseBackground.Magenta => 0xFFFF00FF,
        HouseBackground.Black => 0xFF000000,
        HouseBackground.White => 0xFFFFFFFF,
        _ => 0x00000000,
    };

    /// <summary>
    /// Stacks floors (original <c>StaticMap_JoinFloors</c>). With <paramref name="allFloors"/>,
    /// everything from the lowest relevant floor up to <paramref name="selectedFloor"/> is drawn;
    /// otherwise only the selected floor.
    /// </summary>
    /// <param name="selectedFloor">Absolute floor z.</param>
    /// <param name="crop">Trim borders of background color (transparent: trim fully transparent borders).</param>
    public static RgbaImage Compose(HouseFloors house, int selectedFloor, bool allFloors, bool crop, HouseBackground background)
    {
        int floors = house.FloorCount;
        var canvas = new RgbaImage((house.Columns + floors) * 32, (house.Rows + floors) * 32);
        uint bg = BackgroundColor(background);
        canvas.Clear(bg);
        int selectedIndex = Math.Clamp(selectedFloor - house.TopFloor, 0, floors - 1);
        int bottom, top;
        if (!allFloors)
        {
            bottom = top = selectedIndex;
        }
        else if (selectedFloor > 7)
        {
            bottom = floors - 1;
            top = selectedIndex;
        }
        else
        {
            // Above ground: from the ground floor (z = 7) or the house's lowest floor, up to the selection.
            bottom = Math.Clamp(8 - house.TopFloor - Math.Max(0, 8 - house.TopFloor - floors) - 1, 0, floors - 1);
            top = selectedIndex;
        }
        for (int f = bottom; f >= top; f--)
            canvas.DrawImage(house.Floors[f], (f + 1) * 32, (f + 1) * 32);

        if (!crop) return canvas;
        var bounds = background == HouseBackground.Transparent
            ? AlphaBounds(canvas)
            : canvas.FindContentBounds(canvas[0, 0]);
        return bounds is { } b ? canvas.Crop(b.X, b.Y, b.Width, b.Height) : canvas;
    }

    private static (int X, int Y, int Width, int Height)? AlphaBounds(RgbaImage image)
    {
        int minX = image.Width, minY = image.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                if (image[x, y] >> 24 == 0) continue;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
        return maxX < 0 ? null : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
