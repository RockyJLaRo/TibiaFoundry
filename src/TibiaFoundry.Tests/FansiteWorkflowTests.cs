using Tibia.Protobuf.Appearances;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.Tests;

/// <summary>
/// The fansite workflows: pixel-exact GIF enlargement, export presets, sorting GIFs by kind, the Home
/// dashboard (every tool by category, recent tools and exports) and the settings behind them, which
/// older settings files load without.
/// </summary>
public sealed class FansiteWorkflowTests
{
    private sealed class Sprites : ISpriteSource
    {
        public RgbaImage? GetSprite(uint spriteId) => SyntheticAssets.Sprite((int)spriteId, 32, 32);
    }

    /// <summary>An animated 32×32 object with three phases.</summary>
    private static Appearance Animated()
    {
        var info = new SpriteInfo { PatternWidth = 1, PatternHeight = 1, PatternDepth = 1, Layers = 1, SpriteId = { 1, 2, 3 } };
        info.Animation = new SpriteAnimation();
        info.Animation.SpritePhase.Add(new SpritePhase { DurationMin = 100, DurationMax = 100 });
        info.Animation.SpritePhase.Add(new SpritePhase { DurationMin = 200, DurationMax = 300 });
        info.Animation.SpritePhase.Add(new SpritePhase { DurationMin = 100, DurationMax = 500 });
        return new Appearance { Id = 3031, Name = "gold coin", FrameGroup = { new FrameGroup { SpriteInfo = info } } };
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EnlargedGifsArePixelExactNearestNeighborCopies(int scale)
    {
        var original = AppearanceGifBuilder.Build(Animated(), AppearanceKind.Object, new GifExportOptions(), new Sprites());
        var enlarged = AppearanceGifBuilder.Build(Animated(), AppearanceKind.Object, new GifExportOptions { Scale = scale }, new Sprites());

        Assert.Equal(original.Delays, enlarged.Delays);                                   // timing unchanged
        Assert.Equal(original.Frames.Count, enlarged.Frames.Count);
        for (int f = 0; f < original.Frames.Count; f++)
        {
            var (small, big) = (original.Frames[f], enlarged.Frames[f]);
            Assert.Equal((small.Width * scale, small.Height * scale), (big.Width, big.Height));
            for (int y = 0; y < big.Height; y++)
            for (int x = 0; x < big.Width; x++)
                Assert.Equal(small[x / scale, y / scale], big[x, y]);                     // every pixel a sharp square, transparency kept
            Assert.Subset(small.Pixels.ToHashSet(), big.Pixels.ToHashSet());              // no new colors
        }
        Assert.Equal("Gold Coin", original.SuggestedName);                               // 1×: unchanged names
        Assert.Equal($"Gold Coin_{scale}x", enlarged.SuggestedName);
    }

    [Fact]
    public void ScaleOneLeavesTheExportUnchangedAndLargeScalesAreClamped()
    {
        var plain = AppearanceGifBuilder.Build(Animated(), AppearanceKind.Object, new GifExportOptions(), new Sprites());
        var one = AppearanceGifBuilder.Build(Animated(), AppearanceKind.Object, new GifExportOptions { Scale = 1 }, new Sprites());
        Assert.All(plain.Frames.Zip(one.Frames), p => Assert.True(p.First.PixelEquals(p.Second)));
        var huge = AppearanceGifBuilder.Build(Animated(), AppearanceKind.Object, new GifExportOptions { Scale = 50 }, new Sprites());
        Assert.Equal(32 * GifExportOptions.MaxScale, huge.Frames[0].Width);
    }

    [Fact]
    public void PresetsHaveUniqueIdsAndDescribeTheirUse()
    {
        Assert.Equal(GifExportPresets.All.Count, GifExportPresets.All.Select(p => p.Id).Distinct().Count());
        Assert.All(GifExportPresets.All, p =>
        {
            Assert.InRange(p.Scale, 1, GifExportOptions.MaxScale);
            Assert.False(string.IsNullOrWhiteSpace(p.Description));
            Assert.Same(p, GifExportPresets.Find(p.Id));
        });
        Assert.Null(GifExportPresets.Find("no-such-preset"));
        Assert.Equal(1, GifExportPresets.TibiaWikiImage.Scale);                           // wiki images keep the client's own size
    }

    private static (AppServices Services, string Dir) CreateServices()
    {
        string dir = TestData.NewTempDirectory("fansite");
        var settings = new AppSettings { OutputFolder = dir, LoadDataAutomatically = false };
        settings.Normalize();
        return (new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) }, dir);
    }

    [Fact(Timeout = 120_000)]
    public async Task APresetFillsInTheOptionsAndAChangeByHandShowsCustom()
    {
        await Sta.Run(() =>
        {
            var (services, _) = CreateServices();
            using var _ = services;
            var export = new AppearancesViewModel(services).Export;
            Assert.Null(export.SelectedPreset.Preset);                                    // new installations start with Custom

            export.SelectedPreset = export.Presets.Single(p => p.Preset == GifExportPresets.CreatureGallery);
            Assert.Equal((2, 1, Direction.South, GifDelayMode.Average, true), (export.Scale, export.MovementIndex, export.Direction, export.DelayMode, export.KindFolders));
            Assert.Equal((2, true, "creature-gallery"), (services.Settings.GifScale, services.Settings.GifKindFolders, services.Settings.GifPreset));

            export.Scale = 3;                                                             // changed by hand
            Assert.Null(export.SelectedPreset.Preset);
            Assert.Null(services.Settings.GifPreset);
            Assert.Equal(3, export.Scale);                                                // the change itself is kept

            export.SelectedPreset = export.Presets.Single(p => p.Preset == GifExportPresets.HighResolutionReference);
            export.Direction = Direction.West;                                            // the preset leaves the direction open
            Assert.Same(GifExportPresets.HighResolutionReference, export.SelectedPreset.Preset);

            // The choice is remembered by the next page.
            var again = new AppearancesViewModel(services).Export;
            Assert.Same(GifExportPresets.HighResolutionReference, again.SelectedPreset.Preset);
            Assert.Equal(4, again.Scale);
            return Task.CompletedTask;
        });
    }

    [Fact(Timeout = 120_000)]
    public async Task HomeListsEveryToolByCategoryAndRecentToolsAndExports()
    {
        await Sta.Run(async () =>
        {
            var (services, dir) = CreateServices();
            using var _ = services;
            var main = new MainViewModel(services);
            var home = Assert.IsType<HomeViewModel>(main.SelectedPage);
            await home.OnNavigatedToAsync();

            // Every page except Home, in navigation order and grouped as in the rail.
            Assert.Equal(main.Pages.Where(p => p != home), home.Sections.SelectMany(s => s.Tools).Select(t => t.Page));
            Assert.Equal([BuiltInTools.ContentGroup, BuiltInTools.GraphicsGroup, BuiltInTools.GameDataGroup, BuiltInTools.MapsGroup, BuiltInTools.MediaGroup, BuiltInTools.AssetsGroup,
                          BuiltInTools.ClientLibraryGroup, BuiltInTools.UtilitiesGroup, BuiltInTools.ApplicationGroup], home.Sections.Select(s => s.Name));
            Assert.All(home.Sections, s => Assert.False(string.IsNullOrWhiteSpace(s.Summary)));
            Assert.All(home.Workflows, w => Assert.NotNull(w.Start));
            Assert.False(home.HasRecentTools);
            Assert.False(home.HasRecentExports);

            // Opening tools and saving files fills the lists (newest first).
            var sounds = main.Pages.OfType<SoundsViewModel>().Single();
            home.OpenToolCommand.Execute(sounds);
            Assert.Same(sounds, main.SelectedPage);
            string file = Path.Combine(dir, "Dragon.gif");
            File.WriteAllBytes(file, [1]);
            services.Status.ShowSaved(file, isFile: true);
            main.SelectedPage = home;
            await home.OnNavigatedToAsync();
            Assert.Equal([sounds], home.RecentTools.Select(t => t.Page));
            var export = Assert.Single(home.RecentExports);
            Assert.Equal(("Dragon.gif", true), (export.Name, export.Exists));

            // They are kept in the settings file.
            var (reloaded, _, _) = new SettingsStore(Path.Combine(dir, "settings.json")).Load();
            Assert.Equal(file, Assert.Single(reloaded.RecentExports).Path);
        });
    }

    [Fact]
    public void SettingsWithoutTheNewOptionsLoadWithDefaultsAndBadValuesAreRepaired()
    {
        string dir = TestData.NewTempDirectory("fansite-settings");
        string path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, """{ "Version": 7, "GifAutoName": false, "LastPage": "SoundsViewModel" }""");
        var (old, _, _) = new SettingsStore(path).Load();
        Assert.Equal((1, false, null, 0, 0), (old.GifScale, old.GifKindFolders, old.GifPreset, old.RecentPages.Count, old.RecentExports.Count));
        Assert.False(old.GifAutoName);                                                    // existing values are kept
        Assert.Equal("SoundsViewModel", old.LastPage);

        var bad = new AppSettings
        {
            GifScale = 99, GifPreset = "unknown",
            RecentPages = ["A", "A", "", "ItemDiscoveryViewModel", "B", "C", "D", "E", "F", "G", "H", "I"],
            RecentExports = [new() { Path = "" }, new() { Path = @"C:\x.gif" }, new() { Path = @"c:\X.GIF" }],
        };
        bad.Normalize();
        Assert.Equal(GifExportOptions.MaxScale, bad.GifScale);
        Assert.Null(bad.GifPreset);
        Assert.Equal(["A", "B", "C", "D", "E", "F", "G", "H"], bad.RecentPages);
        Assert.Single(bad.RecentExports);
    }
}
