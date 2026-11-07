using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Metadata;
using ImageMagick;

namespace TibiaFoundry.Core.Imaging;

/// <summary>
/// Writes animated GIFs the way the original <c>Helper.Export_Animated_Gif</c> did: ImageMagick
/// quantisation, per-frame delays in centiseconds (integer division by 10), disposal
/// "background" and an infinite loop for animations. The original wrote one comment
/// ("Created by &lt;author&gt; using &lt;its name&gt; for Tibia.Fandom.com"); Tibia Foundry embeds the
/// full creator attribution instead (comments and XMP, see <see cref="GifMetadata"/>) in every
/// GIF, including single-sprite GIFs, which the original left without a comment.
/// </summary>
public static class GifWriter
{
    /// <param name="frames">Frames; all should share the first frame's size.</param>
    /// <param name="delaysMs">Delays in milliseconds; reused cyclically if shorter than the frame list.</param>
    /// <param name="attribution">Attribution to embed; the configured creator when null. Pass one instance for a whole batch.</param>
    public static void Write(string path, IReadOnlyList<RgbaImage> frames, IReadOnlyList<int> delaysMs, AttributionInfo? attribution = null)
    {
        byte[] gif = Encode(frames, delaysMs, attribution);
        AtomicFile.WriteVerified(path, gif, () => new AttributionException($"{Path.GetFileName(path)} could not be verified after writing."));
    }

    /// <summary>GIF bytes with the attribution embedded.</summary>
    public static byte[] Encode(IReadOnlyList<RgbaImage> frames, IReadOnlyList<int> delaysMs, AttributionInfo? attribution = null)
    {
        if (frames.Count == 0) throw new ArgumentException("A GIF needs at least one frame.", nameof(frames));
        if (delaysMs.Count == 0) delaysMs = new[] { 100 };
        var info = attribution ?? Attribution.ForNewAsset();
        Attribution.Validate(info);

        using var collection = new MagickImageCollection();
        for (int i = 0; i < frames.Count; i++)
        {
            // Pixels only: the attribution is added to the finished GIF, not to the frames.
            var image = new MagickImage(ImageIO.EncodePngPixels(frames[i]), MagickFormat.Png);
            image.AnimationDelay = (uint)Math.Max(0, delaysMs[i % delaysMs.Count] / 10);
            image.GifDisposeMethod = GifDisposeMethod.Background;
            collection.Add(image);
        }
        return GifMetadata.Embed(collection.ToByteArray(MagickFormat.Gif), info);
    }
}
