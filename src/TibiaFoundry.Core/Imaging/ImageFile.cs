using ImageMagick;

namespace TibiaFoundry.Core.Imaging;

/// <summary>A decoded image file: one frame, or several for animations.</summary>
public sealed record DecodedImage(IReadOnlyList<RgbaImage> Frames, IReadOnlyList<int> DelaysMs, string Format)
{
    public bool IsAnimated => Frames.Count > 1;
    public RgbaImage First => Frames[0];
}

/// <summary>Formats the image converter can write.</summary>
public enum ImageOutputFormat { Png, Gif, Bmp, WebPLossless, Jpeg }

/// <summary>
/// Decoding and encoding of common image formats (PNG, GIF, BMP, JPEG, TIFF, WebP, ICO) through
/// ImageMagick, for the image scripts. PNG and GIF outputs carry the creator attribution.
/// </summary>
public static class ImageFile
{
    /// <summary>Extensions the converter reads.</summary>
    public static readonly IReadOnlySet<string> ReadableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".gif", ".bmp", ".jpg", ".jpeg", ".tif", ".tiff", ".webp", ".ico",
    };

    public static string ExtensionOf(ImageOutputFormat format) => format switch
    {
        ImageOutputFormat.Gif => ".gif",
        ImageOutputFormat.Bmp => ".bmp",
        ImageOutputFormat.WebPLossless => ".webp",
        ImageOutputFormat.Jpeg => ".jpg",
        _ => ".png",
    };

    public static DecodedImage Decode(byte[] data, int maxPixels = 64 * 1024 * 1024)
    {
        using var collection = new MagickImageCollection();
        collection.Read(data);
        if (collection.Count == 0) throw new InvalidDataException("The file contains no image.");
        string format = collection[0].Format.ToString();
        if (collection.Count > 1 && collection[0].Format == MagickFormat.Gif) collection.Coalesce();
        var frames = new List<RgbaImage>(collection.Count);
        var delays = new List<int>(collection.Count);
        foreach (var image in collection)
        {
            if ((long)image.Width * image.Height > maxPixels) throw new InvalidDataException($"The image is {image.Width}×{image.Height}, larger than the limit of {maxPixels:N0} pixels.");
            image.Alpha(AlphaOption.Set);
            byte[] bgra = image.GetPixels().ToByteArray(PixelMapping.BGRA) ?? throw new InvalidDataException("The image pixels could not be read.");
            var rgba = new RgbaImage((int)image.Width, (int)image.Height);
            System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(bgra.AsSpan()).CopyTo(rgba.Pixels);
            frames.Add(rgba);
            delays.Add((int)image.AnimationDelay * 10);
            if (collection[0].Format != MagickFormat.Gif) break; // other multi-page formats: first page only
        }
        return new DecodedImage(frames, delays, format);
    }

    /// <summary>
    /// Encodes frames. PNG and GIF get <paramref name="attribution"/>; other formats have no place
    /// for it in this version. Only GIF keeps animations.
    /// </summary>
    public static byte[] Encode(IReadOnlyList<RgbaImage> frames, IReadOnlyList<int> delaysMs, ImageOutputFormat format, Metadata.AttributionInfo attribution, int jpegQuality = 92)
    {
        switch (format)
        {
            case ImageOutputFormat.Png:
                return ImageIO.EncodePng(frames[0], attribution);
            case ImageOutputFormat.Gif:
                return GifWriter.Encode(frames, delaysMs, attribution);
            case ImageOutputFormat.Bmp:
                return BmpEncoder.Encode(frames[0]);
        }
        using var image = new MagickImage(ImageIO.EncodePngPixels(frames[0]), MagickFormat.Png);
        if (format == ImageOutputFormat.Jpeg)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
            image.Quality = (uint)Math.Clamp(jpegQuality, 1, 100);
            return image.ToByteArray(MagickFormat.Jpeg);
        }
        image.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
        return image.ToByteArray(MagickFormat.WebP);
    }
}
