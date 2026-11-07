using Tibia.Protobuf.Appearances;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.StaticData;

namespace TibiaFoundry.Tests;

/// <summary>Static Data (8.8.2): GIFs of many monsters and bosses at once, named like TibiaWiki articles.</summary>
public sealed class CreatureGifExportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Sprites : ISpriteSource
    {
        public RgbaImage? GetSprite(uint spriteId) => SyntheticAssets.Sprite((int)spriteId, 32, 32);
    }

    /// <summary>A one-layer outfit: four directions, idle (1 frame) and moving (2 frames).</summary>
    private static Appearance Outfit(uint id, uint firstSprite)
    {
        FrameGroup Group(int frames, uint start)
        {
            var info = new SpriteInfo { PatternWidth = 4, PatternHeight = 1, PatternDepth = 1, Layers = 1 };
            for (uint i = 0; i < 4 * frames; i++) info.SpriteId.Add(start + i);
            return new FrameGroup { SpriteInfo = info };
        }
        return new Appearance { Id = id, FrameGroup = { Group(1, firstSprite), Group(2, firstSprite + 10) } };
    }

    private static AppearanceDatabase Database()
    {
        var data = new Tibia.Protobuf.Appearances.Appearances();
        data.Outfit.Add(Outfit(128, 20));
        var info = new SpriteInfo { PatternWidth = 1, PatternHeight = 1, PatternDepth = 1, Layers = 1, SpriteId = { 1, 2 } };
        info.Animation = new SpriteAnimation { SpritePhase = { new SpritePhase { DurationMin = 200, DurationMax = 200 }, new SpritePhase { DurationMin = 200, DurationMax = 200 } } };
        data.Object.Add(new Appearance { Id = 3031, Name = "gold coin", FrameGroup = { new FrameGroup { SpriteInfo = info } } });
        return new AppearanceDatabase(data);
    }

    private static Creature Monster(string name, uint race, uint outfitId = 128, uint objectId = 0) =>
        new(name, race, new Tibia.Protobuf.Staticdata.Outfit { OutfitId = outfitId, ObjectAppearanceTypeId = objectId }, new Tibia.Protobuf.Staticdata.Monster { Race = race, Name = name }, false);

    [Theory]
    [InlineData("orc", "Orc")]
    [InlineData("demon lord", "Demon Lord")]
    [InlineData("DEMON LORD", "Demon Lord")]
    [InlineData("lord of the elements", "Lord of the Elements")]
    [InlineData("the old widow", "The Old Widow")]
    [InlineData("  orc warlord ", "Orc Warlord")]
    public void NamesUseTibiaWikiArticleCasing(string name, string expected) =>
        Assert.Equal(expected, CreatureGifExport.DisplayName(Monster(name, 1)));

    [Fact]
    public void CreaturesWithoutANameAreNamedAfterTheirRace() =>
        Assert.Contains("42", CreatureGifExport.DisplayName(Monster("", 42)));

    [Fact]
    public void FilesAreNamedAfterTheCreaturesAndNeverReplaced()
    {
        string dir = TestData.NewTempDirectory("creature-gifs");
        File.WriteAllText(Path.Combine(dir, "Orc.gif"), "an earlier file");
        var jobs = CreatureGifExport.Plan([(Monster("orc", 1), dir), (Monster("demon lord", 2), dir), (Monster("Demon Lord", 3), dir), (Monster("orc", 4), Path.Combine(dir, "Bosses"))]);
        Assert.Equal(["Orc 2.gif", "Demon Lord.gif", "Demon Lord 2.gif", "Orc.gif"], jobs.Select(j => Path.GetFileName(j.Path)));
        Assert.Equal(Path.Combine(dir, "Bosses", "Orc.gif"), jobs[3].Path);
    }

    [Fact]
    public void EveryCreatureIsWrittenAndFailuresAreReported()
    {
        string dir = TestData.NewTempDirectory("creature-gifs");
        var creatures = new[]
        {
            Monster("orc", 1),
            Monster("demon lord", 2),
            Monster("golden statue", 3, outfitId: 0, objectId: 3031),   // looks like an object
            Monster("ghost of nothing", 4, outfitId: 999),               // outfit not in the appearances
        };
        var jobs = CreatureGifExport.Plan(creatures.Select(c => (c, dir)));
        var saved = new List<string>();
        var reports = new List<Core.Common.OperationProgress>();
        var result = CreatureGifExport.Export(jobs, Database(), new Sprites(), null, Attribution.ForNewAsset("Tester"), 3, saved,
            new SyncProgress(reports.Add), Ct);
        Assert.Equal(3, result.Succeeded);
        Assert.Single(result.Failures);
        Assert.StartsWith("Ghost of Nothing (4)", result.Failures[0], StringComparison.Ordinal);
        Assert.Equal(["Orc.gif", "Demon Lord.gif", "Golden Statue.gif"], saved.Select(Path.GetFileName));
        Assert.All(saved, f => Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(f), 0, 6)));
        Assert.Equal(2, MediaFrames(saved[0]));                                // the moving animation, as in the preview
        Assert.Equal(4, reports[^1].Total);
        Assert.Equal(4, reports[^1].Done);
        Assert.False(File.Exists(Path.Combine(dir, "Ghost of Nothing.gif")));
    }

    [Fact]
    public void AForcedDelayIsUsedForEveryFrame()
    {
        string dir = TestData.NewTempDirectory("creature-gifs");
        var saved = new List<string>();
        CreatureGifExport.Export(CreatureGifExport.Plan([(Monster("orc", 1), dir)]), Database(), new Sprites(), 470, null, 1, saved, ct: Ct);
        using var gif = new ImageMagick.MagickImageCollection(saved[0]);
        Assert.All(gif, frame => Assert.Equal(47u, frame.AnimationDelay));      // centiseconds
    }

    private static int MediaFrames(string path) => Core.Library.MediaInspector.GifFrameCount(File.ReadAllBytes(path));

    private sealed class SyncProgress(Action<Core.Common.OperationProgress> report) : IProgress<Core.Common.OperationProgress>
    {
        public void Report(Core.Common.OperationProgress value) => report(value);
    }
}
