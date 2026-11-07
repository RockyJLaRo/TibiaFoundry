using System.Text.RegularExpressions;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Houses;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Map;

namespace TibiaFoundry.Tests;

public class MapAndHouseParityTests(ITestOutputHelper output)
{
    private static StaticMapDatabase LoadHouses()
    {
        TestData.RequireClientAssets();
        return StaticMapDatabase.Load(TestData.Client.FindAssetFile(AssetTypes.StaticMapData)!);
    }

    /// <summary>
    /// Reference images: house{id}_all_transparent, _all_crop_transparent, _all_crop_magenta and
    /// _floor{z}_black, rendered by the original with the top floor selected.
    /// </summary>
    [Fact]
    public void HouseImages_MatchOriginal()
    {
        string dir = TestData.RequireReference("original", "houses");
        var houses = LoadHouses();
        var db = SharedData.Appearances;
        int compared = 0;
        foreach (var group in Directory.EnumerateFiles(dir, "house*.png").GroupBy(f => Regex.Match(Path.GetFileName(f), @"^house(\d+)_").Groups[1].Value))
        {
            uint id = uint.Parse(group.Key);
            var floors = HouseRenderer.RenderFloors(houses.Find(id)!, db, SharedData.Sprites);
            foreach (var file in group)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                RgbaImage mine = name.EndsWith("_all_transparent") ? HouseRenderer.Compose(floors, floors.TopFloor, true, false, HouseBackground.Transparent)
                    : name.EndsWith("_all_crop_transparent") ? HouseRenderer.Compose(floors, floors.TopFloor, true, true, HouseBackground.Transparent)
                    : name.EndsWith("_all_crop_magenta") ? HouseRenderer.Compose(floors, floors.TopFloor, true, true, HouseBackground.Magenta)
                    : HouseRenderer.Compose(floors, int.Parse(Regex.Match(name, @"_floor(\d+)_").Groups[1].Value), false, false, HouseBackground.Black);
                var expected = ImageIO.Load(file);
                var diff = ImageCompare.Compare(expected, mine);
                if (name.EndsWith("_all_crop_transparent"))
                {
                    // Original flattened onto magenta before trimming, then keyed magenta out again:
                    // semi-transparent edge pixels became opaque magenta-tinted. Only those may differ.
                    Assert.Null(diff.SizeMismatch);
                    Assert.Equal(0, diff.DifferentOpaquePixels > 0 ? CountNonMagentaTint(expected, mine) : 0);
                }
                else
                {
                    Assert.True(diff.Identical, $"{name}: {diff}");
                }
                compared++;
            }
        }
        output.WriteLine($"{compared} house images compared");
        Assert.True(compared >= 12);
    }

    /// <summary>Counts differing pixels that cannot be explained by the original's magenta keying.</summary>
    private static int CountNonMagentaTint(RgbaImage expected, RgbaImage actual)
    {
        int bad = 0;
        for (int i = 0; i < expected.Pixels.Length; i++)
        {
            uint e = expected.Pixels[i], a = actual.Pixels[i];
            if (e == a || (e >> 24 == 0 && a >> 24 == 0)) continue;
            bool semiTransparentInRebuild = a >> 24 is > 0 and < 255;
            if (!semiTransparentInRebuild) bad++;
        }
        return bad;
    }

    [Fact]
    public void HousesThatCrashedTheOriginal_Render()
    {
        var houses = LoadHouses();
        foreach (uint id in new uint[] { 10004, 11015 })
        {
            var floors = HouseRenderer.RenderFloors(houses.Find(id)!, SharedData.Appearances, SharedData.Sprites);
            var image = HouseRenderer.Compose(floors, floors.TopFloor, true, true, HouseBackground.Transparent);
            output.WriteLine($"house {id}: {image.Width}x{image.Height}, warnings: {string.Join("; ", floors.Warnings)}");
            Assert.True(image.Width > 32);
        }
    }

    [Fact]
    public async Task ExtractedMapTiles_MatchOriginal()
    {
        string refDir = TestData.RequireReference("original", "mapextract");
        TestData.RequireClientAssets();
        var map = MapDatabase.Load(TestData.Client.FindAssetFile(AssetTypes.Map)!);
        string outDir = TestData.NewTempDirectory("maptiles");
        var result = await MapImageTools.ExtractAsync(map, new MapExtractionOptions(TestData.Client.AssetsDirectory, outDir), null, TestContext.Current.CancellationToken);
        Assert.False(result.HasFailures, string.Join("\n", result.Failures));
        var expectedNames = Directory.GetFiles(refDir).Select(Path.GetFileName).OrderBy(n => n).ToList();
        var actualNames = Directory.GetFiles(outDir).Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(expectedNames, actualNames);
        int compared = 0;
        foreach (var name in expectedNames)
        {
            var diff = ImageCompare.Compare(ImageIO.Load(Path.Combine(refDir, name!)), ImageIO.Load(Path.Combine(outDir, name!)));
            Assert.True(diff.Identical, $"{name}: {diff}");
            compared++;
        }
        output.WriteLine($"{compared} map tiles pixel-identical (names identical too)");
        Directory.Delete(outDir, true);
    }

    [Fact]
    public void HighResMaps_MatchOriginal()
    {
        string input = TestData.RequireReference("original", "mapextract");
        string refDir = TestData.RequireReference("original", "hrmap");
        var cases = new (string File, HrMapOptions Options)[]
        {
            ("satellite_z1_ground.png", new HrMapOptions { InputDirectory = input, Type = HrMapType.Satellite, Zoom = 1, FloorsAboveGround = 0, Cumulative = false, FloorOffset = true }),
            ("satellite_z1_top3_cumulative_mark.png", new HrMapOptions { InputDirectory = input, Type = HrMapType.Satellite, Zoom = 1, FloorsAboveGround = 3, Cumulative = true, FloorOffset = true, Watermark = true }),
            ("minimap_z1_ground.png", new HrMapOptions { InputDirectory = input, Type = HrMapType.Minimap, Zoom = 1, FloorsAboveGround = 0, Cumulative = false, FloorOffset = false }),
        };
        foreach (var (file, options) in cases)
        {
            var expected = ImageIO.Load(Path.Combine(refDir, file));
            var mine = MapImageTools.BuildHighResMap(options, ct: TestContext.Current.CancellationToken);
            var diff = ImageCompare.Compare(expected, mine);
            output.WriteLine($"{file}: original {expected.Width}x{expected.Height}, rebuild {mine.Width}x{mine.Height}: {diff}");
            Assert.Null(diff.SizeMismatch);
            if (!options.Watermark) Assert.True(diff.Identical, $"{file}: {diff}");
            else Assert.True(diff.MaxChannelDelta <= 3, $"{file}: watermark blending differs too much ({diff})");
        }
    }
}
