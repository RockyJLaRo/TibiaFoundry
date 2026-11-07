using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace TibiaFoundry.Core.Imaging;

/// <summary>
/// Decoder for the uncompressed 24/32-bit BMP images found inside CipSoft's LZMA containers
/// (BITMAPV4HEADER, BI_BITFIELDS, masks R=00FF0000 G=0000FF00 B=000000FF A=FF000000).
/// </summary>
public static class BmpDecoder
{
    /// <summary>Largest accepted image side; client sheets are 384 px, map tiles at most a few thousand.</summary>
    public const int MaxDimension = 16384;

    public static RgbaImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 54 || data[0] != (byte)'B' || data[1] != (byte)'M')
            throw new InvalidDataException("Not a BMP image.");
        int pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(data[10..]);
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(data[14..]);
        int width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
        int bpp = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(data[30..]);
        bool bottomUp = rawHeight > 0;
        int height = Math.Abs(rawHeight);

        if (bpp != 32 && bpp != 24) throw new InvalidDataException($"Unsupported BMP bit depth {bpp} (expected 24 or 32).");
        if (compression != 0 && compression != 3) throw new InvalidDataException($"Unsupported BMP compression {compression} (expected uncompressed or bitfields).");
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension || rawHeight == int.MinValue)
            throw new InvalidDataException($"Implausible BMP size {width}×{rawHeight}.");
        if (headerSize < 40 || 14L + headerSize > data.Length)
            throw new InvalidDataException($"Invalid BMP header size {headerSize}.");
        if (pixelOffset < 14 + 40 || pixelOffset > data.Length)
            throw new InvalidDataException($"Invalid BMP pixel data offset {pixelOffset}.");

        uint rMask = 0x00FF0000, gMask = 0x0000FF00, bMask = 0x000000FF, aMask = bpp == 32 ? 0xFF000000 : 0;
        if (compression == 3)
        {
            // Masks follow a 40-byte header, or are part of a V4/V5 header.
            int maskPos = 14 + 40;
            if (maskPos + (headerSize >= 56 ? 16 : 12) > data.Length) throw new InvalidDataException("Truncated BMP color masks.");
            rMask = BinaryPrimitives.ReadUInt32LittleEndian(data[maskPos..]);
            gMask = BinaryPrimitives.ReadUInt32LittleEndian(data[(maskPos + 4)..]);
            bMask = BinaryPrimitives.ReadUInt32LittleEndian(data[(maskPos + 8)..]);
            aMask = headerSize >= 56 ? BinaryPrimitives.ReadUInt32LittleEndian(data[(maskPos + 12)..]) : 0;
        }
        else if (bpp == 32)
        {
            // Plain BI_RGB 32-bit: the 4th byte is officially unused. CipSoft files always use
            // bitfields; treat it as alpha only if any pixel is non-zero there (GDI+ heuristic).
            aMask = 0xFF000000;
        }

        int stride = ((width * bpp + 31) / 32) * 4;
        if (pixelOffset + (long)stride * height > data.Length) throw new InvalidDataException("Truncated BMP pixel data.");
        var image = new RgbaImage(width, height);
        var px = image.Pixels;
        int bytesPerPixel = bpp / 8;
        bool anyAlpha = false;
        if (bpp == 32 && rMask == 0x00FF0000 && gMask == 0x0000FF00 && bMask == 0x000000FF && aMask == 0xFF000000 && BitConverter.IsLittleEndian)
        {
            // 8.8.1: the client's layout (every sprite sheet) already is 0xAARRGGBB in little-endian
            // order, so each row is copied as a block instead of being unpacked pixel by pixel.
            for (int y = 0; y < height; y++)
            {
                int srcRow = pixelOffset + (bottomUp ? height - 1 - y : y) * stride;
                var row = MemoryMarshal.Cast<byte, uint>(data.Slice(srcRow, width * 4));
                row.CopyTo(px.AsSpan(y * width, width));
                if (compression == 0 && !anyAlpha) anyAlpha = HasAlpha(row);
            }
            if (compression == 0 && !anyAlpha)
                for (int i = 0; i < px.Length; i++) px[i] |= 0xFF000000; // undefined alpha => opaque
            return image;
        }
        for (int y = 0; y < height; y++)
        {
            int srcRow = pixelOffset + (bottomUp ? height - 1 - y : y) * stride;
            int dstRow = y * width;
            for (int x = 0; x < width; x++)
            {
                int o = srcRow + x * bytesPerPixel;
                uint v = bpp == 32
                    ? BinaryPrimitives.ReadUInt32LittleEndian(data[o..])
                    : (uint)(data[o] | data[o + 1] << 8 | data[o + 2] << 16);
                uint r = Extract(v, rMask), g = Extract(v, gMask), b = Extract(v, bMask);
                uint a = aMask == 0 ? 255u : Extract(v, aMask);
                if (aMask != 0 && a != 0) anyAlpha = true;
                px[dstRow + x] = a << 24 | r << 16 | g << 8 | b;
            }
        }
        if (compression == 0 && bpp == 32 && !anyAlpha)
            for (int i = 0; i < px.Length; i++) px[i] |= 0xFF000000; // undefined alpha => opaque
        return image;
    }

    private static bool HasAlpha(ReadOnlySpan<uint> pixels)
    {
        foreach (uint v in pixels)
            if ((v & 0xFF000000) != 0) return true;
        return false;
    }

    private static uint Extract(uint value, uint mask)
    {
        if (mask == 0) return 0;
        int shift = BitOperations.TrailingZeroCount(mask);
        uint max = mask >> shift;
        uint v = (value & mask) >> shift;
        return max == 255 ? v : (uint)((v * 255 + max / 2) / max);
    }
}
