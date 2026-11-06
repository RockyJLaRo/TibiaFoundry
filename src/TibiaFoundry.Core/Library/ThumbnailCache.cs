using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Imaging;

namespace TibiaFoundry.Core.Library;

/// <summary>
/// Thumbnails of library images (8.8), cached as PNG files under the library's <c>thumbnails</c>
/// folder by content hash and size, so the same picture is only shrunk once and a changed file gets
/// a new thumbnail. Small pixel art is enlarged by a whole number with nearest-neighbor sampling
/// (sharp, no new colors); larger images are reduced smoothly. Transparency is kept. The cache can
/// be deleted at any time.
/// </summary>
public static class ThumbnailCache
{
    /// <summary>The cached thumbnail's path, creating it when needed; null for files that are not images or cannot be read.</summary>
    public static string? Get(AssetLibrary library, AssetRecord asset, int size = 96)
    {
        if (asset.Kind is not (MediaKind.Image or MediaKind.Animation) || asset.Sha256 == null || asset.Missing) return null;
        string folder = Path.Combine(library.ThumbnailFolder, asset.Sha256[..2]);
        string path = Path.Combine(folder, $"{asset.Sha256}_{size}.png");
        if (File.Exists(path)) return path;
        if (!File.Exists(asset.Path)) return null;
        try
        {
            if ((long)asset.Width * asset.Height > 8192L * 8192) return null;
            var image = MediaInspector.FirstFrame(File.ReadAllBytes(asset.Path), asset.Format);
            var thumb = Fit(image, size);
            Directory.CreateDirectory(folder);
            // Pixels only: thumbnails are a private cache, never published, so no attribution is embedded.
            AtomicFile.WriteAllBytes(path, ImageIO.EncodePngPixels(thumb), durable: false);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or ImageMagick.MagickException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>The image fitted into a square of <paramref name="size"/>: whole-number nearest-neighbor enlargement, or a smooth reduction.</summary>
    public static RgbaImage Fit(RgbaImage image, int size)
    {
        int longest = Math.Max(image.Width, image.Height);
        if (longest <= size)
        {
            int k = Math.Max(1, size / longest);
            return k == 1 ? image : ImageResampler.Resize(image, image.Width * k, image.Height * k, ResampleFilter.Nearest);
        }
        double f = (double)size / longest;
        return ImageResampler.Resize(image, Math.Max(1, (int)Math.Round(image.Width * f)), Math.Max(1, (int)Math.Round(image.Height * f)), ResampleFilter.Bilinear);
    }

    /// <summary>Size of the cache in bytes, and the number of files.</summary>
    public static (long Bytes, int Files) Usage(AssetLibrary library)
    {
        if (!Directory.Exists(library.ThumbnailFolder)) return (0, 0);
        var files = Directory.EnumerateFiles(library.ThumbnailFolder, "*.png", SearchOption.AllDirectories).Select(f => new FileInfo(f)).ToList();
        return (files.Sum(f => f.Length), files.Count);
    }

    /// <summary>Deletes every cached thumbnail (they are made again when shown).</summary>
    public static int Clear(AssetLibrary library)
    {
        int n = 0;
        if (!Directory.Exists(library.ThumbnailFolder)) return 0;
        foreach (string f in Directory.EnumerateFiles(library.ThumbnailFolder, "*.png", SearchOption.AllDirectories))
        {
            try { File.Delete(f); n++; }
            catch (IOException) { }
        }
        return n;
    }
}
