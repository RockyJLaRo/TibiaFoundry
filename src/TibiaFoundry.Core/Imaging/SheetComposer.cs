using TibiaFoundry.Core.Metadata;
using Newtonsoft.Json.Linq;
using Tibia.Protobuf.Appearances;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.Core.Imaging;

/// <summary>Sprite sheet builders used for TibiaWiki's outfiter.</summary>
public static class SheetComposer
{
    public const uint Magenta = 0xFFFF00FF;

    /// <summary>
    /// "Generate Sheet" (Appearances → Export): every sprite of every frame group of an outfit in a
    /// grid of 64×64 cells, <c>patternWidth × layers</c> columns, 32-px sprites anchored at the
    /// bottom-right of their cell. Fixed: in "PNG File" mode the original wrote an empty file and
    /// saved the real image as "mgk.png" in the working directory.
    /// </summary>
    public static RgbaImage OutfitSheet(Appearance outfit, ISpriteSource sprites, bool magentaBackground)
    {
        if (outfit.FrameGroup.Count == 0) throw new InvalidOperationException($"Outfit {outfit.Id} has no frame groups.");
        var ids = outfit.FrameGroup.SelectMany(fg => fg.SpriteInfo?.SpriteId ?? (IEnumerable<uint>)Array.Empty<uint>()).ToList();
        var first = outfit.FrameGroup[0].SpriteInfo
                    ?? throw new InvalidOperationException($"Outfit {outfit.Id} has no sprite information (unsupported or damaged data).");
        int columns = (int)Math.Max(1, first.PatternWidth * first.Layers);
        int rows = Math.Max(1, ids.Count / columns);
        var sheet = new RgbaImage(64 * columns, 64 * rows);
        if (magentaBackground) sheet.Clear(Magenta);
        for (int k = 0; k < ids.Count && k / columns < rows; k++)
        {
            var sprite = sprites.GetSprite(ids[k]) ?? throw new InvalidOperationException($"Sprite {ids[k]} could not be loaded.");
            int x = (k % columns) * 64 + (sprite.Width == 32 ? 32 : 0);
            int y = (k / columns) * 64 + (sprite.Height == 32 ? 32 : 0);
            sheet.DrawImage(sprite, x, y);
        }
        return sheet;
    }

    /// <summary>
    /// Generic grid of images (hidden "Export Sheets" tool): <paramref name="columns"/> per row,
    /// cells of <paramref name="cellWidth"/>×<paramref name="cellHeight"/>, images drawn at the
    /// cell's top-left; rows = images / columns (rounded up — the original silently dropped a
    /// partial last row).
    /// </summary>
    public static RgbaImage Grid(IReadOnlyList<RgbaImage> images, int columns, int cellWidth, int cellHeight, bool magentaBackground)
    {
        if (images.Count == 0) throw new ArgumentException("No images to place.", nameof(images));
        if (columns <= 0 || cellWidth <= 0 || cellHeight <= 0) throw new ArgumentOutOfRangeException(nameof(columns), "Columns and cell size must be positive.");
        int rows = (images.Count + columns - 1) / columns;
        var sheet = new RgbaImage(columns * cellWidth, rows * cellHeight);
        if (magentaBackground) sheet.Clear(Magenta);
        for (int i = 0; i < images.Count; i++)
            sheet.DrawImage(images[i], (i % columns) * cellWidth, (i / columns) * cellHeight);
        return sheet;
    }

    /// <summary>Sprite IDs of an outfit in the legacy Tibia 11 JSON format: <c>root["1"][id]["spr"]</c>.</summary>
    public static List<uint> SpriteIdsFromLegacyJson(string json, string outfitId)
    {
        var root = JObject.Parse(json);
        if (root["1"] is not JObject outfits || outfits[outfitId]?["spr"] is not JArray spr)
            throw new KeyNotFoundException($"ID {outfitId} not found in JSON.");
        return spr.Select(t => (uint)t).ToList();
    }

    /// <summary>All sprite IDs of an outfit appearance (every frame group, in order).</summary>
    public static List<uint> SpriteIdsFromAppearance(AppearanceDatabase db, uint outfitId) =>
        db.Find(AppearanceKind.Outfit, outfitId)?.FrameGroup.SelectMany(fg => fg.SpriteInfo?.SpriteId ?? (IEnumerable<uint>)Array.Empty<uint>()).ToList()
        ?? throw new KeyNotFoundException($"Outfit {outfitId} not found.");

    /// <summary>
    /// Hidden "Sprites from PNG" tool: cuts an outfit sheet (12 cells per row, two rows per frame
    /// group block) into individually named frames "&lt;prefix&gt;&lt;frame&gt;a&lt;n&gt;A/B.png" and
    /// "&lt;prefix&gt;&lt;frame&gt;b&lt;n&gt;C/D.png", reordering columns (1,2,0,3,5,6,4,7,9,10,8,11) exactly
    /// like the original. Returns the number of files written.
    /// </summary>
    /// <param name="attribution">Embedded in every cell; the caller passes the sheet's own attribution when it has one.</param>
    public static int SliceOutfitSheet(RgbaImage sheet, int cellWidth, int cellHeight, string outputDirectory, string prefix, AttributionInfo? attribution = null)
    {
        var info = attribution ?? Attribution.ForNewAsset();
        int[] order = [1, 2, 0, 3, 5, 6, 4, 7, 9, 10, 8, 11];
        int cols = sheet.Width / cellWidth, rows = sheet.Height / cellHeight;
        if (cols < 12) throw new InvalidOperationException($"The sheet must be at least 12 cells wide ({cols} found).");
        var cells = new List<RgbaImage>();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                cells.Add(sheet.Crop(c * cellWidth, r * cellHeight, cellWidth, cellHeight));
        int frames = rows / 2;
        Directory.CreateDirectory(outputDirectory);
        int written = 0;
        void Save(int index, string name)
        {
            if (index < 0 || index >= cells.Count) return;
            ImageIO.SavePng(cells[index], Path.Combine(outputDirectory, name), info);
            written++;
        }
        for (int k = 0; k < frames; k++)
        {
            for (int l = 0; l < 12; l++)
            {
                Save(order[l] + k * 24, $"{prefix}{k}a{l}A.png");
                Save(order[l] + k * 24 + 12, $"{prefix}{k}a{l}B.png");
            }
            for (int m = 0; m < 12; m++)
            {
                Save(order[m] + k * 24 + 24 * frames, $"{prefix}{k}b{m}C.png");
                Save(order[m] + k * 24 + 12 + 24 * frames, $"{prefix}{k}b{m}D.png");
            }
        }
        return written;
    }
}
