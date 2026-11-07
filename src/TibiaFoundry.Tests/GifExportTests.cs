using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Common;
using System.Text.RegularExpressions;
using ImageMagick;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Imaging;

namespace TibiaFoundry.Tests;

public class GifExportTests(ITestOutputHelper output)
{
    /// <summary>
    /// Re-creates every GIF of the original program's reference outputs
    /// (<c>tab{kind}_id{id}_{variant}.gif</c>, author <see cref="TestData.ReferenceAuthor"/>) and compares frames,
    /// delays and the comment block.
    /// </summary>
    [Fact]
    public void Exports_MatchOriginalGifs()
    {
        string refDir = TestData.RequireReference("original_run2", "gifs");
        var db = SharedData.Appearances;
        string tempDir = TestData.NewTempDirectory("gifs");
        int compared = 0;
        foreach (var file in Directory.EnumerateFiles(refDir, "tab*.gif"))
        {
            var m = Regex.Match(Path.GetFileNameWithoutExtension(file), @"^tab(\d)_id(\d+)_(.+)$");
            var kind = (AppearanceKind)int.Parse(m.Groups[1].Value);
            uint id = uint.Parse(m.Groups[2].Value);
            string variant = m.Groups[3].Value;
            var options = variant switch
            {
                "pattern0" => new GifExportOptions { Pattern = 0 },
                "idle_south" => new GifExportOptions { FrameGroup = 0, Direction = Direction.South },
                "moving_east" => new GifExportOptions { FrameGroup = 1, Direction = Direction.East },
                _ => new GifExportOptions(),
            };
            var gif = AppearanceGifBuilder.Build(db.Find(kind, id)!, kind, options, SharedData.Sprites);
            string mine = Path.Combine(tempDir, Path.GetFileName(file));
            GifWriter.Write(mine, gif.Frames, gif.Delays, Attribution.ForNewAsset(TestData.ReferenceAuthor));

            using var expected = new MagickImageCollection(file);
            using var actual = new MagickImageCollection(mine);
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].AnimationDelay, actual[i].AnimationDelay);
                Assert.Equal(expected[i].Width, actual[i].Width);
                Assert.Equal(expected[i].Height, actual[i].Height);
                using var e = expected[i].GetPixels();
                using var a = actual[i].GetPixels();
                var ePix = e.ToByteArray(PixelMapping.RGBA)!;
                var aPix = a.ToByteArray(PixelMapping.RGBA)!;
                // Known original defect: an all-transparent frame went through a GDI+ BMP whose alpha
                // ImageMagick ignored, so it became a solid black square. The rebuild keeps it transparent.
                bool originalBlackFrame = Enumerable.Range(0, ePix.Length / 4).All(p => ePix[p * 4] == 0 && ePix[p * 4 + 1] == 0 && ePix[p * 4 + 2] == 0 && ePix[p * 4 + 3] == 255);
                bool mineTransparent = Enumerable.Range(0, aPix.Length / 4).All(p => aPix[p * 4 + 3] == 0);
                if (originalBlackFrame && mineTransparent)
                {
                    output.WriteLine($"{Path.GetFileName(file)} frame {i}: original black frame (bug) is transparent in the rebuild");
                    continue;
                }
                int diff = 0;
                for (int p = 0; p < ePix.Length; p += 4)
                {
                    bool bothTransparent = ePix[p + 3] == 0 && aPix[p + 3] == 0;
                    if (!bothTransparent && !ePix.AsSpan(p, 4).SequenceEqual(aPix.AsSpan(p, 4))) diff++;
                }
                Assert.True(diff == 0, $"{Path.GetFileName(file)} frame {i}: {diff} pixels differ");
            }
            // Deliberate change: the original wrote its own comment; Tibia Foundry embeds the creator attribution instead.
            Assert.StartsWith($"Created by {TestData.ReferenceAuthor} using ", expected[0].Comment);
            Assert.Contains(Attribution.TextFor(TestData.ReferenceAuthor), actual[0].Comment);
            output.WriteLine($"{Path.GetFileName(file)}: {expected.Count} frames identical; suggested name \"{gif.SuggestedName}\"");
            compared++;
        }
        Assert.True(compared >= 10, $"only {compared} reference GIFs found");
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void PingPongObject_NoLongerCrashes()
    {
        var db = SharedData.Appearances;
        var pingPong = db.List(AppearanceKind.Object).First(o => o.FrameGroup.Count > 0 && o.FrameGroup[0].SpriteInfo.Animation?.LoopType == Tibia.Protobuf.Shared.ANIMATION_LOOP_TYPE.Pingpong);
        var gif = AppearanceGifBuilder.Build(pingPong, AppearanceKind.Object, new GifExportOptions { DelayMode = GifDelayMode.Minimum, AddEmptyExtraFrame = true, ExtraDelay = 500 }, SharedData.Sprites);
        Assert.True(gif.Frames.Count > 2);
        Assert.Equal(gif.Frames.Count, gif.Delays.Count);
        Assert.Equal(500, gif.Delays[^1]);
    }

    [Fact]
    public void AttributionTextIsTheRequiredSentence()
    {
        // Replaces the original comment "Created by <author> using <the original name> for Tibia.Fandom.com".
        Assert.Equal("Created by RockyLaRo using TibiaFoundry for TibiaWiki @ https://tibia.fandom.com/", Attribution.TextFor("RockyLaRo"));
    }
}
