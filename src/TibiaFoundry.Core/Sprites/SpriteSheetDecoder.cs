using System.Security.Cryptography;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.Imaging;

namespace TibiaFoundry.Core.Sprites;

/// <summary>Decodes <c>sprites-*.bmp.lzma</c> sheets and slices them into individual sprites.</summary>
public static class SpriteSheetDecoder
{
    /// <summary>Decodes a whole 384×384 sheet.</summary>
    public static RgbaImage DecodeSheet(string sheetPath)
    {
        byte[] bmp = CipLzma.DecompressFile(sheetPath);
        // Sheet files are named after the SHA-256 of their decompressed bitmap; a mismatch means a
        // damaged or half-written sheet, which must not silently produce wrong sprites.
        if (CompatibilityChecker.ContentHashFromName(Path.GetFileName(sheetPath).Replace(".bmp.lzma", ".bmp", StringComparison.OrdinalIgnoreCase)) is { } expected &&
            !string.Equals(Convert.ToHexString(SHA256.HashData(bmp)), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{Path.GetFileName(sheetPath)}: the sheet content does not match the checksum in its name (damaged or incomplete file).");
        try
        {
            return BmpDecoder.Decode(bmp);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"{Path.GetFileName(sheetPath)}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Slices a decoded sheet. Sprites are laid out row by row, left to right, starting at
    /// <see cref="SpriteSheetInfo.FirstSpriteId"/> (same order as the original DumpSpriteSheet).
    /// The grid is derived from the decoded image (not assumed to be 384×384), and the result
    /// always has exactly <see cref="SpriteSheetInfo.SpriteCount"/> sprites: a sheet that cannot
    /// hold the catalog's sprite range is reported instead of being cut silently.
    /// </summary>
    public static RgbaImage[] Slice(RgbaImage sheet, SpriteSheetInfo info)
    {
        int w = info.SpriteWidth, h = info.SpriteHeight;
        if (sheet.Width % w != 0 || sheet.Height % h != 0)
            throw new InvalidDataException($"Sprite sheet {info.FileName} is {sheet.Width}×{sheet.Height}, which is not a grid of {w}×{h} sprites (sprite type {(int)info.Type}). The sheet layout may have changed.");
        int columns = sheet.Width / w, rows = sheet.Height / h;
        if (info.SpriteCount <= 0 || info.SpriteCount > columns * rows)
            throw new InvalidDataException($"Sprite sheet {info.FileName} holds {columns * rows} sprites of {w}×{h}, but the catalog assigns it {info.SpriteCount} (IDs {info.FirstSpriteId}–{info.LastSpriteId}).");
        var sprites = new RgbaImage[info.SpriteCount];
        for (int i = 0; i < sprites.Length; i++)
        {
            int col = i % columns, row = i / columns;
            sprites[i] = sheet.Crop(col * w, row * h, w, h);
        }
        return sprites;
    }

    public static RgbaImage[] DecodeSprites(AssetCatalog catalog, SpriteSheetInfo info) =>
        Slice(DecodeSheet(catalog.GetSheetPath(info)), info);
}
