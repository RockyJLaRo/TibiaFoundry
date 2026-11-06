using System.Runtime.InteropServices;

namespace TibiaFoundry.Core.Imaging;

/// <summary>
/// A simple 32-bit image in BGRA byte order with straight (non-premultiplied) alpha.
/// All image processing in the core library works on this type, which keeps it free of
/// GDI+/WPF threading constraints and makes results deterministic.
/// </summary>
public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Pixels as 0xAARRGGBB values (little-endian BGRA bytes), row-major, top-down.</summary>
    public uint[] Pixels { get; }

    public RgbaImage(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), $"Invalid image size {width}x{height}.");
        Width = width;
        Height = height;
        Pixels = new uint[checked(width * height)];
    }

    public RgbaImage(int width, int height, uint[] pixels)
    {
        if (pixels.Length != width * height) throw new ArgumentException("Pixel buffer size mismatch.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public Span<byte> Bytes => MemoryMarshal.AsBytes(Pixels.AsSpan());

    public uint this[int x, int y]
    {
        get => Pixels[y * Width + x];
        set => Pixels[y * Width + x] = value;
    }

    public static uint Argb(byte a, byte r, byte g, byte b) => (uint)(a << 24 | r << 16 | g << 8 | b);

    public RgbaImage Clone() => new(Width, Height, (uint[])Pixels.Clone());

    public void Clear(uint argb) => Array.Fill(Pixels, argb);

    /// <summary>Copies a rectangle into a new image (areas outside the source become transparent).</summary>
    public RgbaImage Crop(int x, int y, int width, int height)
    {
        var result = new RgbaImage(width, height);
        for (int row = 0; row < height; row++)
        {
            int sy = y + row;
            if (sy < 0 || sy >= Height) continue;
            int sx0 = Math.Max(0, x), sx1 = Math.Min(Width, x + width);
            if (sx1 <= sx0) continue;
            Array.Copy(Pixels, sy * Width + sx0, result.Pixels, row * width + (sx0 - x), sx1 - sx0);
        }
        return result;
    }

    /// <summary>
    /// Draws <paramref name="source"/> at (x, y) with source-over alpha blending, clipped to
    /// this image. Equivalent to GDI+ <c>Graphics.DrawImage</c> at 1:1 scale.
    /// </summary>
    public void DrawImage(RgbaImage source, int x, int y)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(Width, x + source.Width), y1 = Math.Min(Height, y + source.Height);
        for (int ty = y0; ty < y1; ty++)
        {
            int srcRow = (ty - y) * source.Width - x;
            int dstRow = ty * Width;
            for (int tx = x0; tx < x1; tx++)
            {
                uint s = source.Pixels[srcRow + tx];
                uint sa = s >> 24;
                if (sa == 0) continue;
                ref uint d = ref Pixels[dstRow + tx];
                d = sa == 255 ? s : BlendOver(s, d);
            }
        }
    }

    /// <summary>Copies pixels including alpha (no blending), like GDI+ <c>CompositingMode.SourceCopy</c>.</summary>
    public void CopyImage(RgbaImage source, int x, int y)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(Width, x + source.Width), y1 = Math.Min(Height, y + source.Height);
        if (x1 <= x0) return;
        for (int ty = y0; ty < y1; ty++)
            Array.Copy(source.Pixels, (ty - y) * source.Width + (x0 - x), Pixels, ty * Width + x0, x1 - x0);
    }

    /// <summary>Source-over blend of two straight-alpha ARGB values.</summary>
    public static uint BlendOver(uint src, uint dst)
    {
        uint sa = src >> 24;
        uint da = dst >> 24;
        if (sa == 255 || da == 0) return src;
        // out_a = sa + da*(1-sa); out_c = (sc*sa + dc*da*(1-sa)) / out_a   (all in 0..255 space)
        uint inv = 255 - sa;
        uint daw = da * inv; // scaled by 255
        uint oa255 = sa * 255 + daw; // out alpha * 255
        if (oa255 == 0) return 0;
        uint r = (((src >> 16) & 0xFF) * sa * 255 + ((dst >> 16) & 0xFF) * daw + oa255 / 2) / oa255;
        uint g = (((src >> 8) & 0xFF) * sa * 255 + ((dst >> 8) & 0xFF) * daw + oa255 / 2) / oa255;
        uint b = ((src & 0xFF) * sa * 255 + (dst & 0xFF) * daw + oa255 / 2) / oa255;
        uint a = (oa255 + 127) / 255;
        return a << 24 | r << 16 | g << 8 | b;
    }

    /// <summary>
    /// Bounding box of pixels that differ from <paramref name="background"/> (ImageMagick "trim"
    /// semantics on a flattened image). Returns null when the image is uniform.
    /// </summary>
    public (int X, int Y, int Width, int Height)? FindContentBounds(uint background)
    {
        int minX = Width, minY = Height, maxX = -1, maxY = -1;
        for (int y = 0; y < Height; y++)
        {
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                if (Pixels[row + x] == background) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        return maxX < 0 ? null : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>True when both images have identical size and pixels.</summary>
    public bool PixelEquals(RgbaImage other) => Width == other.Width && Height == other.Height && Pixels.AsSpan().SequenceEqual(other.Pixels);
}
