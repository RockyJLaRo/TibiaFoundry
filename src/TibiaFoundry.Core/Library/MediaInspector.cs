using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using ImageMagick;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Library;

/// <summary>What a media file is: format, size, frames, content hash, image signatures and embedded attribution.</summary>
public sealed record MediaFacts(string Format, int Width, int Height, int Frames, long SizeBytes, DateTime FileModifiedUtc, string Sha256,
    long? PixelHash, string? PixelKey, FoundAttribution? Attribution, string? Problem);

/// <summary>
/// Reads the facts the library stores about a file (8.8). Dimensions and frame counts come from the
/// file headers (PNG, GIF, BMP, JPEG; other images through ImageMagick's "ping", which does not
/// decode pixels); the image signatures from the first frame. Files are only read.
/// </summary>
public static class MediaInspector
{
    /// <summary>Images with more pixels than this get no visual signature (it would cost memory, and such images are maps, not sprites).</summary>
    public const long MaxSignaturePixels = 4096L * 4096;

    public static MediaFacts Inspect(string path, bool signatures = true)
    {
        var info = new FileInfo(path);
        string format = info.Extension.TrimStart('.').ToLowerInvariant();
        if (format == "jpeg") format = "jpg";
        if (format == "tiff") format = "tif";
        byte[] bytes = File.ReadAllBytes(path);
        string sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        int w = 0, h = 0, frames = 0;
        long? dhash = null;
        string? pixelKey = null;
        FoundAttribution? attribution = null;
        string? problem = null;
        if (MediaTypes.Images.Contains(format))
        {
            try
            {
                (w, h, frames) = Dimensions(bytes, format);
                if (signatures && (long)w * h <= MaxSignaturePixels && w > 0)
                {
                    var image = FirstFrame(bytes, format);
                    dhash = ImageSignature.DifferenceHash(image);
                    pixelKey = ImageSignature.PixelKey(image);
                }
                if (format is "png" or "gif" or "jpg") attribution = AttributionReader.Read(bytes);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or MagickException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException or IndexOutOfRangeException)
            {
                problem = L.T("MediaInspector.TheImageCouldNotBeRead") + ex.Message;
            }
        }
        return new MediaFacts(format, w, h, frames, info.Length, info.LastWriteTimeUtc, sha, dhash, pixelKey, attribution is { IsEmpty: false } ? attribution : null, problem);
    }

    /// <summary>Width, height and frame count from the header (no pixel decoding).</summary>
    public static (int Width, int Height, int Frames) Dimensions(ReadOnlySpan<byte> b, string format)
    {
        // PNG: IHDR right after the signature; animated PNGs (acTL) report their frame count.
        if (b.Length >= 24 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G')
        {
            int w = BinaryPrimitives.ReadInt32BigEndian(b[16..]), h = BinaryPrimitives.ReadInt32BigEndian(b[20..]);
            int frames = 1;
            for (int pos = 8; pos + 8 <= b.Length;)
            {
                int len = BinaryPrimitives.ReadInt32BigEndian(b[pos..]);
                if (len < 0 || pos + 12 + (long)len > b.Length) break;
                if (b.Slice(pos + 4, 4).SequenceEqual("acTL"u8) && len >= 8) frames = Math.Max(1, BinaryPrimitives.ReadInt32BigEndian(b[(pos + 8)..]));
                if (b.Slice(pos + 4, 4).SequenceEqual("IDAT"u8)) break;
                pos += 12 + len;
            }
            return (w, h, frames);
        }
        if (b.Length >= 10 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F')
            return (BinaryPrimitives.ReadUInt16LittleEndian(b[6..]), BinaryPrimitives.ReadUInt16LittleEndian(b[8..]), GifFrameCount(b));
        if (b.Length >= 26 && b[0] == (byte)'B' && b[1] == (byte)'M')
            return (BinaryPrimitives.ReadInt32LittleEndian(b[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(b[22..])), 1);
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            for (int pos = 2; pos + 9 < b.Length;)
            {
                if (b[pos] != 0xFF) { pos++; continue; }
                byte marker = b[pos + 1];
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    return (BinaryPrimitives.ReadUInt16BigEndian(b[(pos + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(b[(pos + 5)..]), 1);
                if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { pos += 2; continue; }
                pos += 2 + BinaryPrimitives.ReadUInt16BigEndian(b[(pos + 2)..]);
            }
        }
        var ping = new MagickImageInfo(b.ToArray());
        return ((int)ping.Width, (int)ping.Height, 1);
    }

    /// <summary>Counts the image descriptors of a GIF by walking its blocks (no decoding).</summary>
    public static int GifFrameCount(ReadOnlySpan<byte> b)
    {
        if (b.Length < 13) return 0;
        int pos = 13;
        if ((b[10] & 0x80) != 0) pos += 3 * (1 << ((b[10] & 0x07) + 1));
        int frames = 0;
        while (pos < b.Length)
        {
            byte block = b[pos++];
            if (block == 0x3B) break;
            if (block == 0x21)
            {
                pos++; // label
                if (!SkipSubBlocks(b, ref pos)) break;
            }
            else if (block == 0x2C)
            {
                if (pos + 9 > b.Length) break;
                frames++;
                byte flags = b[pos + 8];
                pos += 9;
                if ((flags & 0x80) != 0) pos += 3 * (1 << ((flags & 0x07) + 1));
                pos++; // LZW minimum code size
                if (!SkipSubBlocks(b, ref pos)) break;
            }
            else break;
        }
        return Math.Max(frames, 1);
    }

    private static bool SkipSubBlocks(ReadOnlySpan<byte> b, ref int pos)
    {
        while (pos < b.Length)
        {
            int size = b[pos++];
            if (size == 0) return true;
            pos += size;
        }
        return false;
    }

    /// <summary>The first frame as pixels (GDI+ for PNG, GIF, BMP and JPEG; ImageMagick for the rest).</summary>
    public static RgbaImage FirstFrame(byte[] bytes, string format)
    {
        if (format is "png" or "gif" or "bmp" or "jpg")
        {
            try { return ImageIO.LoadFromBytes(bytes); }
            catch (ArgumentException) { }
        }
        using var image = new MagickImage(bytes);
        image.Format = MagickFormat.Png32;
        return ImageIO.LoadFromBytes(image.ToByteArray());
    }

    /// <summary>
    /// Every frame of an image with its delay in milliseconds (GIF animations are coalesced, so each
    /// frame is complete). Single images give one frame with delay 0.
    /// </summary>
    public static (IReadOnlyList<RgbaImage> Frames, IReadOnlyList<int> Delays) LoadFrames(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string format = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (format != "gif" || GifFrameCount(bytes) <= 1) return ([FirstFrame(bytes, format)], [0]);
        using var collection = new MagickImageCollection(bytes);
        collection.Coalesce();
        var frames = new List<RgbaImage>();
        var delays = new List<int>();
        foreach (var frame in collection)
        {
            frame.Format = MagickFormat.Png32;
            frames.Add(ImageIO.LoadFromBytes(frame.ToByteArray()));
            delays.Add((int)frame.AnimationDelay * 10);
        }
        return (frames, delays);
    }
}

/// <summary>
/// Image signatures for duplicate detection (8.8).
/// <list type="bullet">
/// <item><see cref="PixelKey"/>: identical pixels, ignoring nearest-neighbor enlargement and
/// transparent borders, so a sprite and its 2× export or a padded copy have the same key.</item>
/// <item><see cref="DifferenceHash"/>: a 64-bit perceptual hash; a small Hamming distance means
/// the images look alike (never proof that they are the same).</item>
/// </list>
/// </summary>
public static class ImageSignature
{
    public static string PixelKey(RgbaImage image)
    {
        var reduced = Unscale(TrimTransparent(Normalize(image)));
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header, reduced.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], reduced.Height);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(header);
        sha.AppendData(reduced.Bytes);
        return Convert.ToHexStringLower(sha.GetHashAndReset())[..32];
    }

    /// <summary>Fully transparent pixels all become 0, whatever color they carried.</summary>
    private static RgbaImage Normalize(RgbaImage image)
    {
        var copy = image.Clone();
        var p = copy.Pixels;
        for (int i = 0; i < p.Length; i++) if (p[i] >> 24 == 0) p[i] = 0;
        return copy;
    }

    private static RgbaImage TrimTransparent(RgbaImage image) =>
        image.FindContentBounds(0) is { } b && (b.Width != image.Width || b.Height != image.Height) ? image.Crop(b.X, b.Y, b.Width, b.Height) : image;

    /// <summary>Undoes a whole-number nearest-neighbor enlargement (the largest factor whose blocks are all uniform).</summary>
    public static RgbaImage Unscale(RgbaImage image)
    {
        int g = (int)BigInteger.GreatestCommonDivisor(image.Width, image.Height);
        for (int k = g; k > 1; k--)
        {
            if (image.Width % k != 0 || image.Height % k != 0 || !UniformBlocks(image, k)) continue;
            var small = new RgbaImage(image.Width / k, image.Height / k);
            for (int y = 0; y < small.Height; y++)
                for (int x = 0; x < small.Width; x++) small[x, y] = image[x * k, y * k];
            return small;
        }
        return image;
    }

    private static bool UniformBlocks(RgbaImage image, int k)
    {
        for (int y = 0; y < image.Height; y++)
        {
            int by = y - y % k;
            for (int x = 0; x < image.Width; x++)
                if (image[x, y] != image[x - x % k, by]) return false;
        }
        return true;
    }

    /// <summary>dHash of the image on a neutral background (9×8 box-filtered luminance, one bit per clear horizontal gradient).</summary>
    public static long DifferenceHash(RgbaImage image)
    {
        const int W = 9, H = 8;
        Span<double> lum = stackalloc double[W * H];
        for (int gy = 0; gy < H; gy++)
        {
            int y0 = gy * image.Height / H, y1 = Math.Max(y0 + 1, (gy + 1) * image.Height / H);
            for (int gx = 0; gx < W; gx++)
            {
                int x0 = gx * image.Width / W, x1 = Math.Max(x0 + 1, (gx + 1) * image.Width / W);
                double sum = 0;
                int n = 0;
                for (int y = y0; y < y1 && y < image.Height; y++)
                    for (int x = x0; x < x1 && x < image.Width; x++)
                    {
                        uint p = image[x, y];
                        double a = (p >> 24) / 255.0;
                        double l = 0.299 * ((p >> 16) & 0xFF) + 0.587 * ((p >> 8) & 0xFF) + 0.114 * (p & 0xFF);
                        sum += l * a + 128 * (1 - a);
                        n++;
                    }
                lum[gy * W + gx] = n == 0 ? 128 : sum / n;
            }
        }
        long hash = 0;
        int bit = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W - 1; x++, bit++)
                if (lum[y * W + x] > lum[y * W + x + 1] + 2.0) hash |= 1L << bit; // a margin: flat areas give stable bits, not noise
        return hash;
    }

    public static int Distance(long a, long b) => BitOperations.PopCount((ulong)(a ^ b));
}
