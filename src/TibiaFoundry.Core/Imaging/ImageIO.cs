using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Common;
using System.Drawing;
using System.Drawing.Imaging;

namespace TibiaFoundry.Core.Imaging;

/// <summary>Loading and saving <see cref="RgbaImage"/> through GDI+ (the same codecs the original used).</summary>
public static class ImageIO
{
    /// <summary>Loads an image file without keeping the file locked (the original leaked file locks).</summary>
    public static RgbaImage Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return LoadFromBytes(bytes);
    }

    public static RgbaImage LoadFromBytes(byte[] bytes)
    {
        if (bytes.Length > 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M') return BmpDecoder.Decode(bytes);
        using var ms = new MemoryStream(bytes);
        using var bmp = new Bitmap(ms);
        return FromBitmap(bmp);
    }

    public static RgbaImage FromBitmap(Bitmap bitmap)
    {
        var image = new RgbaImage(bitmap.Width, bitmap.Height);
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                for (int y = 0; y < bitmap.Height; y++)
                    new ReadOnlySpan<uint>((void*)(data.Scan0 + y * data.Stride), bitmap.Width).CopyTo(image.Pixels.AsSpan(y * bitmap.Width, bitmap.Width));
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return image;
    }

    public static Bitmap ToBitmap(RgbaImage image)
    {
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                for (int y = 0; y < image.Height; y++)
                    image.Pixels.AsSpan(y * image.Width, image.Width).CopyTo(new Span<uint>((void*)(data.Scan0 + y * data.Stride), image.Width));
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    /// <summary>
    /// Saves as PNG with creator attribution embedded (the configured creator unless
    /// <paramref name="attribution"/> is given), creating the directory if needed. The file is
    /// written atomically and checked on disk; if the attribution cannot be embedded or verified,
    /// nothing is written and an <see cref="AttributionException"/> explains why.
    /// </summary>
    /// <param name="attribution">Pass one instance for a whole batch: its metadata is built and verified once.</param>
    public static void SavePng(RgbaImage image, string path, AttributionInfo? attribution = null)
    {
        byte[] png = EncodePng(image, attribution);
        AtomicFile.WriteVerified(path, png, () => new AttributionException($"{Path.GetFileName(path)} could not be verified after writing."));
    }

    /// <summary>PNG bytes with creator attribution (files, data URIs, the clipboard's PNG format).</summary>
    public static byte[] EncodePng(RgbaImage image, AttributionInfo? attribution = null) =>
        PngMetadata.Embed(EncodePngPixels(image), attribution ?? Attribution.ForNewAsset());

    /// <summary>
    /// PNG bytes without any metadata. Only for handing pixels to another encoder inside Tibia Foundry
    /// (GIF frames); every file or clipboard output goes through <see cref="EncodePng"/>.
    /// </summary>
    internal static byte[] EncodePngPixels(RgbaImage image)
    {
        using var bitmap = ToBitmap(image);
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>"data:image/png;base64,..." string, as produced by the original's Base64 sheet export (with attribution).</summary>
    public static string ToPngDataUri(RgbaImage image, AttributionInfo? attribution = null) =>
        "data:image/png;base64," + Convert.ToBase64String(EncodePng(image, attribution));
}
