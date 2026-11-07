using TibiaFoundry.Core.Imaging;

namespace TibiaFoundry.Tests;

/// <summary>Pixel comparison helpers for parity tests.</summary>
internal static class ImageCompare
{
    public sealed record Diff(int DifferentPixels, int MaxChannelDelta, int DifferentOpaquePixels, string? SizeMismatch)
    {
        public bool Identical => SizeMismatch == null && DifferentPixels == 0;
        public override string ToString() => SizeMismatch ?? $"{DifferentPixels} px differ (opaque: {DifferentOpaquePixels}), max channel delta {MaxChannelDelta}";
    }

    /// <summary>
    /// Compares two images. Fully transparent pixels are considered equal regardless of their
    /// color bytes (GDI+ and ImageMagick normalize those differently).
    /// </summary>
    public static Diff Compare(RgbaImage expected, RgbaImage actual)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
            return new Diff(-1, -1, -1, $"size {expected.Width}x{expected.Height} vs {actual.Width}x{actual.Height}");
        int diff = 0, maxDelta = 0, opaqueDiff = 0;
        for (int i = 0; i < expected.Pixels.Length; i++)
        {
            uint a = expected.Pixels[i], b = actual.Pixels[i];
            if (a == b || (a >> 24 == 0 && b >> 24 == 0)) continue;
            diff++;
            if (a >> 24 == 255 && b >> 24 == 255) opaqueDiff++;
            for (int shift = 0; shift < 32; shift += 8)
                maxDelta = Math.Max(maxDelta, Math.Abs((int)((a >> shift) & 0xFF) - (int)((b >> shift) & 0xFF)));
        }
        return new Diff(diff, maxDelta, opaqueDiff, null);
    }

    /// <summary>Compares premultiplied colors, which removes GDI+'s precision loss on low-alpha pixels.</summary>
    public static int MaxPremultipliedDelta(RgbaImage expected, RgbaImage actual)
    {
        int max = 0;
        for (int i = 0; i < expected.Pixels.Length; i++)
        {
            uint a = expected.Pixels[i], b = actual.Pixels[i];
            if (a == b) continue;
            int aa = (int)(a >> 24), ba = (int)(b >> 24);
            max = Math.Max(max, Math.Abs(aa - ba));
            for (int shift = 0; shift < 24; shift += 8)
            {
                int ca = (int)((a >> shift) & 0xFF) * aa / 255;
                int cb = (int)((b >> shift) & 0xFF) * ba / 255;
                max = Math.Max(max, Math.Abs(ca - cb));
            }
        }
        return max;
    }
}
