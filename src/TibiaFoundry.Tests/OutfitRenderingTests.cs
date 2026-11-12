using System.Text.RegularExpressions;
using TibiaFoundry.Core.Imaging;

namespace TibiaFoundry.Tests;

public class OutfitRenderingTests(ITestOutputHelper output)
{
    /// <summary>
    /// Compares against frames rendered by the original program's outfit renderer.
    /// Folder names encode the parameters: o{outfit}_fg{fg}_d{dir}_m{mounted}_a{addons}_f{onlyFirst}_mt{mount}.
    /// Colors used by the harness: default (78,69,58,76), or (0,94,114,19) when addons == 2;
    /// the mount always used (0,94,114,19).
    /// </summary>
    [Fact]
    public void RenderedFrames_MatchOriginal()
    {
        string refDir = TestData.RequireReference("original", "outfits");
        var db = SharedData.Appearances;
        var sprites = SharedData.Sprites;
        int[] colors = [78, 69, 58, 76];
        int[] alt = [0, 94, 114, 19];
        int folders = 0, frames = 0, identical = 0, maxDelta = 0;
        foreach (var dir in Directory.EnumerateDirectories(refDir, "o*"))
        {
            var m = Regex.Match(Path.GetFileName(dir), @"^o(\d+)_fg(\d+)_d(\d+)_m(\d)_a(\d)_f(\d)_mt(\d+)$");
            Assert.True(m.Success, dir);
            int V(int g) => int.Parse(m.Groups[g].Value);
            var options = new OutfitRenderOptions
            {
                FrameGroup = V(2), Direction = V(3), Mounted = V(4) == 1, Addons = (uint)V(5),
                OnlyFirstFrame = V(6) == 1, MountId = (uint)V(7),
                Colors = V(5) == 2 ? alt : colors, MountColors = alt,
            };
            var mine = OutfitRenderer.Render(db, sprites, (uint)V(1), options);
            var expectedFiles = Directory.GetFiles(dir, "frame_*.png").OrderBy(f => f).ToList();
            Assert.Equal(expectedFiles.Count, mine.Count);
            for (int i = 0; i < mine.Count; i++)
            {
                var diff = ImageCompare.Compare(ImageIO.Load(expectedFiles[i]), mine[i]);
                Assert.Null(diff.SizeMismatch);
                frames++;
                if (diff.Identical) identical++;
                maxDelta = Math.Max(maxDelta, diff.MaxChannelDelta);
                if (!diff.Identical) output.WriteLine($"{Path.GetFileName(dir)} frame {i}: {diff}");
            }
            folders++;
        }
        output.WriteLine($"{folders} cases, {frames} frames, {identical} pixel-identical, max channel delta {maxDelta}");
        Assert.True(folders >= 9);
        Assert.Equal(frames, identical); // pixel-exact parity with the original renderer
    }

    [Fact]
    public void MissingOutfit_GivesClearError()
    {
        var ex = Assert.Throws<OutfitRenderException>(() => OutfitRenderer.Render(SharedData.Appearances, SharedData.Sprites, 999999, new OutfitRenderOptions()));
        Assert.Contains("999999", ex.Message);
    }

    [Fact]
    public void Lcm_Works()
    {
        Assert.Equal(24, OutfitRenderer.Lcm(8, 6));
        Assert.Equal(8, OutfitRenderer.Lcm(8, 8));
        Assert.Equal(8, OutfitRenderer.Lcm(1, 8));
    }
}
