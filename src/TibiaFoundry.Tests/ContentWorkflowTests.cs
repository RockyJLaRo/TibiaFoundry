using System.IO.Compression;
using System.Text.Json;
using Tibia.Protobuf.Appearances;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Library;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.StaticData;

namespace TibiaFoundry.Tests;

/// <summary>
/// Content production (8.8): workflows rendering creatures, outfits and items, processing steps,
/// manifests, galleries, snippets and archives, the staged (all-or-nothing) output, cancellation,
/// export profiles and templates, project files, workflow files and the processing queue.
/// </summary>
public sealed class ContentWorkflowTests
{
    private sealed class Sprites : ISpriteSource
    {
        public RgbaImage? GetSprite(uint spriteId) => spriteId == 999 ? null : SyntheticAssets.Sprite((int)spriteId, 32, 32);
    }

    private sealed class Game : IGameAssets
    {
        public AppearanceDatabase? Appearances { get; init; }
        public ISpriteSource? Sprites { get; init; }
        public StaticDataDatabase? StaticData { get; init; }
        public string? ClientVersion { get; init; } = "15.33.test";
    }

    private static Appearance Item(uint id, string name, params uint[] sprites)
    {
        var info = new SpriteInfo { PatternWidth = 1, PatternHeight = 1, PatternDepth = 1, Layers = 1 };
        info.SpriteId.AddRange(sprites);
        if (sprites.Length > 1)
        {
            info.Animation = new SpriteAnimation();
            foreach (var _ in sprites) info.Animation.SpritePhase.Add(new SpritePhase { DurationMin = 150, DurationMax = 250 });
        }
        return new Appearance { Id = id, Name = name, FrameGroup = { new FrameGroup { SpriteInfo = info } } };
    }

    /// <summary>A one-layer outfit: four directions, idle (1 frame) and moving (2 frames).</summary>
    private static Appearance Outfit(uint id, string name, uint firstSprite)
    {
        FrameGroup Group(int frames, uint start)
        {
            var info = new SpriteInfo { PatternWidth = 4, PatternHeight = 1, PatternDepth = 1, Layers = 1 };
            for (uint i = 0; i < 4 * frames; i++) info.SpriteId.Add(start + i);
            return new FrameGroup { SpriteInfo = info };
        }
        return new Appearance { Id = id, Name = name, FrameGroup = { Group(1, firstSprite), Group(2, firstSprite + 10) } };
    }

    private static Game NewGame(bool withBrokenItem = false)
    {
        var data = new Tibia.Protobuf.Appearances.Appearances();
        data.Object.Add(Item(3031, "gold coin", 1, 2, 3));
        data.Object.Add(Item(3035, "platinum coin", 4));
        data.Object.Add(Item(100, "", 5));
        if (withBrokenItem) data.Object.Add(Item(3043, "crystal coin", 999));
        data.Outfit.Add(Outfit(128, "citizen", 20));
        var statics = new Tibia.Protobuf.Staticdata.StaticData();
        statics.Monster.Add(new Tibia.Protobuf.Staticdata.Monster { Race = 39, Name = "Dragon Lord", Outfit = new Tibia.Protobuf.Staticdata.Outfit { OutfitId = 128 } });
        statics.Bosses.Add(new Tibia.Protobuf.Staticdata.Boss { Race = 900, Name = "Golden Statue", Outfit = new Tibia.Protobuf.Staticdata.Outfit { ObjectAppearanceTypeId = 3031 } });
        return new Game { Appearances = new AppearanceDatabase(data), Sprites = new Sprites(), StaticData = new StaticDataDatabase(statics) };
    }

    private static WorkflowContext Context(string output, IGameAssets? game = null, AssetLibrary? lib = null, ExportProfile? profile = null) => new()
    {
        OutputFolder = output, RunName = "Test Gallery", Game = game, Library = lib, Profile = profile ?? ExportProfiles.TibiaWiki, Creator = "Tester",
    };

    [Fact]
    public void ACreatureGalleryIsRenderedSizedNamedDescribedAndPublished()
    {
        string dir = TestData.NewTempDirectory("workflow");
        var lib = AssetLibrary.Open(Path.Combine(dir, "Library"));
        var project = ContentProjects.Create(ContentProjectType.CreatureGallery, "Test Gallery");
        project.Workflow.Steps[0].Parameters["names"] = "Dragon Lord\nGolden Statue\nNo Such Creature";

        var result = WorkflowRunner.Run(project.Workflow, Context(Path.Combine(dir, "out"), NewGame(), lib, ExportProfiles.Gallery), ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Problems, p => p.Contains("No Such Creature", StringComparison.Ordinal));        // reported, not fatal
        foreach (var item in result.Items)
        {
            Assert.Equal((128, 128), (item.Width, item.Height));                                                 // one canvas for every creature
            Assert.Equal("gif", item.Format);
            Assert.StartsWith("creatures/", item.File, StringComparison.Ordinal);                                     // the gallery profile writes lower-case names
            string file = Path.Combine(result.OutputFolder, item.File);
            Assert.Equal("Tester", AttributionReader.Read(file)!.Creators.Single());                           // attribution embedded
            Assert.Contains("CipSoft", AttributionReader.Read(file)!.Copyright, StringComparison.Ordinal);
        }
        var dragon = result.Items.Single(i => i.Name == "Dragon Lord");
        Assert.True(dragon.Animated);
        Assert.Equal("Dragon Lord, animated (Moving), client 15.33.test", dragon.Description);
        Assert.Equal("dragon_lord.gif", Path.GetFileName(dragon.File));                                         // the gallery profile's naming
        var (frames, delays) = MediaInspector.LoadFrames(Path.Combine(result.OutputFolder, result.Items.Single(i => i.Name == "Golden Statue").File));
        Assert.Equal([150, 150, 150], delays);                                                                   // the same timing as Static Data's creature GIFs

        foreach (string f in new[] { "contact-sheet.png", "manifest.json", "manifest.csv", "index.html" }) Assert.True(File.Exists(Path.Combine(result.OutputFolder, f)), f);
        Assert.DoesNotContain(Directory.GetDirectories(Path.Combine(dir, "out")), d => Path.GetFileName(d).StartsWith(".tf-staging", StringComparison.Ordinal));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.OutputFolder, "manifest.json")));
        Assert.Equal(2, manifest.RootElement.GetProperty("count").GetInt32());

        var col = lib.CollectionNamed("Test Gallery")!;                                                         // added to the library
        Assert.Equal(2, col.Count);
        Assert.All(lib.Get(lib.CollectionItems(col.Id)), a => Assert.Equal(ExportStates.Exported, a.ExportStatus));
        Assert.Contains(lib.Get(lib.CollectionItems(col.Id)), a => a.Name == "Dragon Lord");                // the asset keeps its real name, not the file name
    }

    [Fact]
    public void OutfitVariantsBecomeComparisonSheetsAndDirectionsAreRendered()
    {
        string dir = TestData.NewTempDirectory("workflow");
        var wf = new WorkflowDefinition
        {
            Steps =
            [
                new(WorkflowStepType.RenderOutfits, ("ids", "128, 129"), ("addons", "None"), ("allDirections", "true"), ("animated", "false")),
                new(WorkflowStepType.ComparisonSheets),
                new(WorkflowStepType.Manifest, ("json", "true"), ("csv", "false")),
            ],
        };
        var result = WorkflowRunner.Run(wf, Context(Path.Combine(dir, "out"), NewGame()), ct: TestContext.Current.CancellationToken);
        Assert.Equal(4, result.Items.Count);                                                                     // 128 × four directions; 129 does not exist
        Assert.Contains(result.Problems, p => p.Contains("129", StringComparison.Ordinal));
        Assert.Equal(["East", "North", "South", "West"], result.Items.Select(i => i.Metadata["Direction"]).Order().ToArray());
        Assert.All(result.Items, i => Assert.Equal("png", i.Format));                                          // still images → PNG
        Assert.True(File.Exists(Path.Combine(result.OutputFolder, "comparison", "Citizen.png")));
        var sheet = ImageIO.Load(Path.Combine(result.OutputFolder, "comparison", "Citizen.png"));
        Assert.True(sheet.Width >= 4 * 32);
    }

    [Fact]
    public void ItemsAreTrimmedScaledPaddedAndPackedWithSnippetsAndAZip()
    {
        string dir = TestData.NewTempDirectory("workflow");
        var wf = new WorkflowDefinition
        {
            Steps =
            [
                new(WorkflowStepType.RenderObjects, ("ids", "3031, 3035, 100, 77777"), ("onlyNamed", "true")),
                new(WorkflowStepType.Resize, ("mode", "Scale"), ("scale", "2")),
                new(WorkflowStepType.Canvas, ("width", "80"), ("height", "80"), ("padding", "4"), ("align", "Center"), ("background", "#FFFFFF")),
                new(WorkflowStepType.Rename, ("template", "{id} {name} {scale}x")),
                new(WorkflowStepType.Organize, ("template", "../../{category}")),
                new(WorkflowStepType.Documentation, ("template", "[[File:{file}]] {name}"), ("fileName", "links.txt")),
                new(WorkflowStepType.Zip, ("fileName", "{collection}")),
            ],
        };
        var result = WorkflowRunner.Run(wf, Context(Path.Combine(dir, "out"), NewGame()), ct: TestContext.Current.CancellationToken);
        Assert.Equal(["3031 Gold Coin 2x.gif", "3035 Platinum Coin 2x.png"], result.Items.Select(i => Path.GetFileName(i.File)).Order().ToArray());
        Assert.All(result.Items, i => Assert.StartsWith("Items/", i.File, StringComparison.Ordinal));          // ".." cannot leave the output folder
        var png = ImageIO.Load(Path.Combine(result.OutputFolder, result.Items.Single(i => i.Format == "png").File));
        Assert.Equal((80, 80), (png.Width, png.Height));
        Assert.Equal(0xFFFFFFFFu, png[0, 0]);                                                                    // background color
        Assert.Equal(SyntheticAssets.Sprite(4, 32, 32)[1, 1], png[8 + 2, 8 + 2]);                               // 2× nearest neighbor, centered
        Assert.Contains("[[File:3031 Gold Coin 2x.gif]] Gold Coin", File.ReadAllText(Path.Combine(result.OutputFolder, "links.txt")), StringComparison.Ordinal);
        using var zip = ZipFile.OpenRead(Path.Combine(result.OutputFolder, "Test Gallery.zip"));
        Assert.Contains(zip.Entries, e => e.FullName == "Items/3031 Gold Coin 2x.gif");
        Assert.Contains(zip.Entries, e => e.FullName == "links.txt");
    }

    [Fact]
    public void OneBrokenImageIsReportedAndTheOthersAreWritten()
    {
        string dir = TestData.NewTempDirectory("workflow");
        var wf = new WorkflowDefinition { Steps = [new(WorkflowStepType.RenderObjects, ("ids", "3031, 3043"))] };
        var result = WorkflowRunner.Run(wf, Context(Path.Combine(dir, "out"), NewGame(withBrokenItem: true)), ct: TestContext.Current.CancellationToken);
        Assert.Single(result.Items);
        Assert.Contains(result.Problems, p => p.Contains("999", StringComparison.Ordinal));
    }

    [Fact]
    public void ACancelledRunLeavesNothingBehind()
    {
        string dir = TestData.NewTempDirectory("workflow");
        string output = Path.Combine(dir, "out");
        var wf = new WorkflowDefinition { Steps = [new(WorkflowStepType.RenderObjects, ("ids", "3031, 3035"))] };
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.Done >= 1) cts.Cancel(); });
        Assert.ThrowsAny<OperationCanceledException>(() => WorkflowRunner.Run(wf, Context(output, NewGame()), progress, cts.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(output));                                             // no staging folder, no partial export
    }

    private sealed class SyncProgress(Action<TibiaFoundry.Core.Common.OperationProgress> report) : IProgress<TibiaFoundry.Core.Common.OperationProgress>
    {
        public void Report(TibiaFoundry.Core.Common.OperationProgress value) => report(value);
    }

    [Fact]
    public void LibraryAssetsAreReprocessedWithoutChangingTheOriginalsAndKeepTheirCreators()
    {
        string dir = TestData.NewTempDirectory("workflow");
        var lib = AssetLibrary.Open(Path.Combine(dir, "Library"));
        string art = Path.Combine(dir, "art");
        string original = AssetLibraryTests.Png(art, "Artwork/My Banner.png", AssetLibraryTests.Square(0xFF8040C0, 40, 5));
        byte[] before = File.ReadAllBytes(original);
        AssetIndexer.IndexFolder(lib, art, ct: TestContext.Current.CancellationToken);
        long col = lib.CreateCollection("Banners");
        lib.AddToCollection(col, lib.SearchIds(new AssetQuery()));
        var wf = new WorkflowDefinition { Steps = [new(WorkflowStepType.SelectLibraryAssets, ("collection", "Banners")), new(WorkflowStepType.Trim)] };

        var result = WorkflowRunner.Run(wf, Context(Path.Combine(dir, "out"), lib: lib, profile: ExportProfiles.Article), ct: TestContext.Current.CancellationToken);

        var item = result.Items.Single();
        Assert.Equal((90, 90), (item.Width, item.Height));                                                      // trimmed 30×30, then the profile's 3×
        Assert.Equal("my-banner_3x.png", Path.GetFileName(item.File));
        Assert.Equal(before, File.ReadAllBytes(original));                                                       // the original is untouched
        Assert.Equal("Tester", AttributionReader.Read(Path.Combine(result.OutputFolder, item.File))!.Creators.Single()); // the creator of the source is kept
        Assert.True(File.Exists(Path.Combine(result.OutputFolder, "index.md")));                                 // the article profile's index
    }

    [Fact]
    public void UpdateInPlaceReplacesSameNamedFilesAndKeepsOthers()
    {
        string dir = TestData.NewTempDirectory("workflow");
        string output = Path.Combine(dir, "out");
        var wf = new WorkflowDefinition { Steps = [new(WorkflowStepType.RenderObjects, ("ids", "3035"))] };
        var ctx = Context(output, NewGame()) with { Mode = OutputMode.UpdateInPlace };
        var first = WorkflowRunner.Run(wf, ctx, ct: TestContext.Current.CancellationToken);
        string keep = Path.Combine(first.OutputFolder, "my notes.txt");
        File.WriteAllText(keep, "mine");
        var second = WorkflowRunner.Run(wf, ctx, ct: TestContext.Current.CancellationToken);
        Assert.Equal(first.OutputFolder, second.OutputFolder);
        Assert.True(File.Exists(keep));
        Assert.Single(Directory.GetFiles(second.OutputFolder, "*.png", SearchOption.AllDirectories));
        var newFolder = WorkflowRunner.Run(wf, ctx with { Mode = OutputMode.NewFolderPerRun }, ct: TestContext.Current.CancellationToken);
        Assert.NotEqual(first.OutputFolder, newFolder.OutputFolder);
    }

    [Fact]
    public void ClientStepsExplainWhatIsMissing()
    {
        var wf = new WorkflowDefinition { Steps = [new(WorkflowStepType.RenderCreatures, ("names", "*"))] };
        var ex = Assert.Throws<InvalidOperationException>(() => WorkflowRunner.Run(wf, Context(TestData.NewTempDirectory("workflow")), ct: TestContext.Current.CancellationToken));
        Assert.Contains("Clients page", ex.Message, StringComparison.Ordinal);
        Assert.NotEmpty(new WorkflowDefinition().Validate());
    }

    [Fact]
    public void TemplatesAreSafeForFileNamesAndFolders()
    {
        var values = TemplateText.Values("Gold: Coin?", "3031", "Items", "15.33", "Still", null, 32, 32, 2, "Wiki", 7, "Me", "png");
        Assert.Equal("3031 Gold_ Coin_", TemplateText.FileName("{id} {name}", values));
        Assert.Equal("gold_-coin_", TemplateText.FileName("{name}", values, NameCase.Lower, "-"));
        Assert.Equal(Path.Combine("15.33", "Items"), TemplateText.Folder("{version}/../{category}", values));
        Assert.Equal("", TemplateText.Folder("", values));
        Assert.Equal("007", TemplateText.Expand("{index3}", values));
        Assert.Equal(["nope"], TemplateText.UnknownVariables("{name} {nope}"));
        Assert.Equal("dragon_lord.gif", TemplateText.Sample("{name}", ExportProfiles.Gallery));
    }

    [Fact]
    public void ProfilesAreEditableSavedResetAndValidated()
    {
        string folder = Path.Combine(TestData.NewTempDirectory("profiles"), "profiles");
        Assert.Equal(5, ExportProfiles.Load(folder).Count);                                                     // the built-in examples
        ExportProfiles.Save(folder, ExportProfiles.TibiaWiki with { Scale = 2 });
        Assert.Equal(2, ExportProfiles.Find(folder, "tibiawiki").Scale);                                        // an edited built-in profile
        string id = ExportProfiles.NewId(folder, "My Site!");
        Assert.Equal("my-site", id);
        ExportProfiles.Save(folder, ExportProfiles.CustomFansite with { Id = id, Name = "My site", BuiltIn = false, Scale = 99 });
        Assert.Equal(8, ExportProfiles.Find(folder, id).Scale);                                                 // clamped
        ExportProfiles.Delete(folder, "tibiawiki");
        Assert.Equal(1, ExportProfiles.Find(folder, "tibiawiki").Scale);                                        // reset to the original
        Assert.Throws<ArgumentException>(() => ExportProfiles.Save(folder, ExportProfiles.Gallery with { FileNameTemplate = "{unknown}" }));
        Assert.Equal("gif", ExportProfiles.TibiaWiki.FormatFor(animated: true));
        Assert.Equal("png", ExportProfiles.TibiaWiki.FormatFor(animated: false));
    }

    [Fact]
    public void GalleriesAndManifestsCannotBeUsedToInjectMarkupOrFormulas()
    {
        var items = new[]
        {
            new PublishedItem { Name = "</script><script>alert(1)</script>", File = "a b/#1.png", Category = "<b>x</b>", Description = "=HYPERLINK(\"http://x\")" },
        };
        string html = GalleryGenerator.Html(items, new GalleryOptions { Title = "<img src=x onerror=alert(1)>", Accent = "red;}body{display:none" });
        Assert.DoesNotContain("</script><script>alert", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.Contains("#2D71E1", html, StringComparison.Ordinal);                                              // an invalid accent falls back
        Assert.DoesNotContain("src=\"http", html, StringComparison.Ordinal);                                     // no external resources
        Assert.DoesNotContain("<link", html, StringComparison.Ordinal);
        string csv = ManifestWriter.Csv(items);
        Assert.Contains("\"'=HYPERLINK(\"\"http://x\"\")\"", csv, StringComparison.Ordinal);
        Assert.Contains("\\|", ManifestWriter.Markdown([items[0] with { Name = "a|b" }], "t"), StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectsAndWorkflowsRoundTripAndRejectForeignFiles()
    {
        string dir = TestData.NewTempDirectory("projects");
        foreach (var type in Enum.GetValues<ContentProjectType>())
        {
            var p = ContentProjects.Create(type);
            Assert.NotEmpty(p.Workflow.Steps);
            Assert.Empty(p.Workflow.Validate());                                                                  // every template is runnable
            p.History.Add(new ProjectRun(DateTime.UtcNow, 1.5, "Completed", 3, dir, []));
            ContentProjects.Save(dir, p);
        }
        var loaded = ContentProjects.Load(dir);
        Assert.Equal(Enum.GetValues<ContentProjectType>().Length, loaded.Count);
        var gallery = loaded.Single(p => p.Type == ContentProjectType.CreatureGallery);
        Assert.Equal(WorkflowStepType.RenderCreatures, gallery.Workflow.Steps[0].Type);
        Assert.StartsWith("Completed", gallery.Status, StringComparison.Ordinal);
        var copy = gallery.Clone();
        Assert.NotEqual(gallery.Id, copy.Id);
        Assert.Empty(copy.History);

        string json = gallery.Workflow.ToJson();
        var back = WorkflowDefinition.FromJson(json);
        Assert.Equal(gallery.Workflow.Steps.Count, back.Steps.Count);
        Assert.Equal("Dragon\nDragon Lord\nDemon", back.Steps[0].Get("names"));
        Assert.Throws<InvalidDataException>(() => WorkflowDefinition.FromJson("{\"format\":\"other\"}"));
        Assert.Throws<InvalidDataException>(() => WorkflowDefinition.FromJson("not json"));
        Assert.Equal("Scale: 3", new WorkflowStep(WorkflowStepType.Resize, ("scale", "3")).Summary);
        Assert.Equal(3, WorkflowRunner.ParseIds("5-3, 4", 100).Count);
    }

    [Fact]
    public async Task TheQueueLimitsConcurrencyCancelsRetriesAndRecordsHistory()
    {
        string history = Path.Combine(TestData.NewTempDirectory("queue"), "queue-history.jsonl");
        using var queue = new ProcessingQueue(() => 1, history);
        var gate = new TaskCompletionSource();
        var first = queue.Enqueue("Long", "Test", async (p, ct) => { p.Report(new(1, 2, "Half")); await gate.Task.WaitAsync(ct); return "done"; });
        var second = queue.Enqueue("Waiting", "Test", (_, _) => Task.FromResult("second"));
        int attempts = 0;
        var failing = queue.Enqueue("Failing", "Test", (_, _) => ++attempts == 1 ? throw new IOException("disk full") : Task.FromResult("ok now"));

        await WaitFor(() => first.State == QueueJobState.Running && first.Progress == 0.5);
        Assert.Equal(QueueJobState.Pending, second.State);                                                    // one at a time
        queue.Cancel(second);
        Assert.Equal(QueueJobState.Cancelled, second.State);
        gate.SetResult();
        await WaitFor(() => first.IsFinished && failing.IsFinished);
        Assert.Equal(("done", QueueJobState.Completed), (first.Result, first.State));
        Assert.Equal(QueueJobState.Failed, failing.State);
        Assert.Equal("disk full", failing.Error);
        Assert.Contains("IOException", failing.ErrorDetails, StringComparison.Ordinal);

        var retry = queue.Retry(failing)!;
        await WaitFor(() => retry.IsFinished);
        Assert.Equal("ok now", retry.Result);

        var running = queue.Enqueue("Cancelled while running", "Test", async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return ""; });
        await WaitFor(() => running.State == QueueJobState.Running);
        queue.Cancel(running);
        await WaitFor(() => running.IsFinished);
        Assert.Equal(QueueJobState.Cancelled, running.State);

        Assert.Equal(5, queue.ClearFinished());
        Assert.Empty(queue.Jobs);
        Assert.Equal(5, queue.History().Count);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition(), "timed out");
    }
}
