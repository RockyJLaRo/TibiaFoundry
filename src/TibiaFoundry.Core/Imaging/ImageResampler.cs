namespace TibiaFoundry.Core.Imaging;

/// <summary>How pixels are calculated when an image is resized.</summary>
public enum ResampleFilter
{
    /// <summary>Each pixel copies the nearest source pixel: keeps pixel art sharp (no new colors).</summary>
    Nearest,
    Bilinear,
    /// <summary>Catmull-Rom cubic.</summary>
    Bicubic,
    /// <summary>Lanczos with three lobes: sharpest for photos and smooth artwork.</summary>
    Lanczos3,
}

/// <summary>
/// Deterministic image resizing on <see cref="RgbaImage"/>. Smooth filters work on premultiplied
/// alpha, so transparent pixels never bleed their (invisible) color into visible edges.
/// </summary>
public static class ImageResampler
{
    public static RgbaImage Resize(RgbaImage source, int width, int height, ResampleFilter filter)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "The new size must be at least 1×1.");
        if (width == source.Width && height == source.Height) return source.Clone();
        return filter == ResampleFilter.Nearest ? Nearest(source, width, height) : Convolve(source, width, height, filter);
    }

    private static RgbaImage Nearest(RgbaImage source, int width, int height)
    {
        var result = new RgbaImage(width, height);
        var xs = new int[width];
        for (int x = 0; x < width; x++) xs[x] = Math.Min(source.Width - 1, (int)((x + 0.5) * source.Width / width));
        for (int y = 0; y < height; y++)
        {
            int sy = Math.Min(source.Height - 1, (int)((y + 0.5) * source.Height / height));
            int srcRow = sy * source.Width, dstRow = y * width;
            for (int x = 0; x < width; x++) result.Pixels[dstRow + x] = source.Pixels[srcRow + xs[x]];
        }
        return result;
    }

    private static double Kernel(ResampleFilter filter, double x)
    {
        x = Math.Abs(x);
        switch (filter)
        {
            case ResampleFilter.Bilinear:
                return x < 1 ? 1 - x : 0;
            case ResampleFilter.Bicubic:
                const double a = -0.5;
                if (x < 1) return ((a + 2) * x - (a + 3)) * x * x + 1;
                if (x < 2) return ((a * x - 5 * a) * x + 8 * a) * x - 4 * a;
                return 0;
            default:
                if (x < 1e-9) return 1;
                if (x >= 3) return 0;
                double px = Math.PI * x;
                return 3 * Math.Sin(px) * Math.Sin(px / 3) / (px * px);
        }
    }

    private static double Support(ResampleFilter filter) => filter switch
    {
        ResampleFilter.Bilinear => 1,
        ResampleFilter.Bicubic => 2,
        _ => 3,
    };

    /// <summary>For each output position: first source index and normalized weights.</summary>
    private static (int[] Start, double[][] Weights) Weights(int sourceSize, int targetSize, ResampleFilter filter)
    {
        double scale = (double)sourceSize / targetSize;
        double filterScale = Math.Max(1, scale);
        double support = Support(filter) * filterScale;
        var starts = new int[targetSize];
        var weights = new double[targetSize][];
        for (int i = 0; i < targetSize; i++)
        {
            double center = (i + 0.5) * scale - 0.5;
            int first = (int)Math.Ceiling(center - support);
            int last = (int)Math.Floor(center + support);
            var w = new double[last - first + 1];
            double sum = 0;
            for (int j = first; j <= last; j++)
            {
                double v = Kernel(filter, (j - center) / filterScale);
                w[j - first] = v;
                sum += v;
            }
            if (sum != 0)
                for (int k = 0; k < w.Length; k++) w[k] /= sum;
            starts[i] = first;
            weights[i] = w;
        }
        return (starts, weights);
    }

    private static RgbaImage Convolve(RgbaImage source, int width, int height, ResampleFilter filter)
    {
        int sw = source.Width, sh = source.Height;
        // Premultiplied channels as doubles: [a, r*a, g*a, b*a].
        var src = new double[sw * sh * 4];
        for (int i = 0; i < source.Pixels.Length; i++)
        {
            uint p = source.Pixels[i];
            double a = (p >> 24) / 255.0;
            src[i * 4] = a;
            src[i * 4 + 1] = ((p >> 16) & 0xFF) * a;
            src[i * 4 + 2] = ((p >> 8) & 0xFF) * a;
            src[i * 4 + 3] = (p & 0xFF) * a;
        }
        var (xStart, xWeights) = Weights(sw, width, filter);
        var horizontal = new double[width * sh * 4];
        Parallel.For(0, sh, y =>
        {
            for (int x = 0; x < width; x++)
            {
                double a = 0, r = 0, g = 0, b = 0;
                var w = xWeights[x];
                for (int k = 0; k < w.Length; k++)
                {
                    int sx = Math.Clamp(xStart[x] + k, 0, sw - 1);
                    int o = (y * sw + sx) * 4;
                    a += src[o] * w[k];
                    r += src[o + 1] * w[k];
                    g += src[o + 2] * w[k];
                    b += src[o + 3] * w[k];
                }
                int d = (y * width + x) * 4;
                horizontal[d] = a;
                horizontal[d + 1] = r;
                horizontal[d + 2] = g;
                horizontal[d + 3] = b;
            }
        });
        var (yStart, yWeights) = Weights(sh, height, filter);
        var result = new RgbaImage(width, height);
        Parallel.For(0, height, y =>
        {
            var w = yWeights[y];
            for (int x = 0; x < width; x++)
            {
                double a = 0, r = 0, g = 0, b = 0;
                for (int k = 0; k < w.Length; k++)
                {
                    int sy = Math.Clamp(yStart[y] + k, 0, sh - 1);
                    int o = (sy * width + x) * 4;
                    a += horizontal[o] * w[k];
                    r += horizontal[o + 1] * w[k];
                    g += horizontal[o + 2] * w[k];
                    b += horizontal[o + 3] * w[k];
                }
                a = Math.Clamp(a, 0, 1);
                uint pa = (uint)Math.Round(a * 255);
                uint pr = 0, pg = 0, pb = 0;
                if (pa > 0)
                {
                    pr = (uint)Math.Clamp(Math.Round(r / a), 0, 255);
                    pg = (uint)Math.Clamp(Math.Round(g / a), 0, 255);
                    pb = (uint)Math.Clamp(Math.Round(b / a), 0, 255);
                }
                result.Pixels[y * width + x] = pa << 24 | pr << 16 | pg << 8 | pb;
            }
        });
        return result;
    }
}
