using System.Text.Json;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.Tests;

/// <summary>
/// Attribution through the application's own workflows: the conflict question when re-exporting
/// someone else's images, the creator setting, settings migration, and the messages of exports
/// whose formats cannot carry metadata.
/// </summary>
[Collection("Attribution")]
public sealed class AttributionWorkflowTests : IDisposable
{
    private readonly string _previousCreator = Attribution.CurrentCreator;

    public void Dispose() => Attribution.CurrentCreator = _previousCreator;

    private static (AppServices Services, FakeDialogs Dialogs, string Dir) CreateServices()
    {
        string dir = TestData.NewTempDirectory("attrflow");
        var settings = new AppSettings { OutputFolder = dir, LoadDataAutomatically = false };
        settings.Normalize();
        var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var dialogs = new FakeDialogs();
        services.Dialogs = dialogs;
        return (services, dialogs, dir);
    }

    private static RgbaImage Cell(uint color)
    {
        var image = new RgbaImage(8, 8);
        image.Clear(color);
        return image;
    }

    [Fact]
    public async Task SheetBuilderKeepsOtherCreatorsUnlessTheUserDecidesOtherwise()
    {
        Attribution.CurrentCreator = "Bob";
        var (services, dialogs, dir) = CreateServices();
        using var _ = services;
        string a = Path.Combine(dir, "a.png"), b = Path.Combine(dir, "b.png");
        ImageIO.SavePng(Cell(0xFFFF0000), a, Attribution.ForNewAsset("Alice"));
        ImageIO.SavePng(Cell(0xFF00FF00), b, Attribution.ForNewAsset("Alice"));
        var vm = new AdvancedToolsViewModel(services) { Files = [a, b], Columns = 2, CellWidth = 8, CellHeight = 8 };

        // Default answer: keep the original creators. The question is asked once for the whole sheet.
        string kept = Path.Combine(dir, "kept.png");
        dialogs.NextFile = kept;
        await vm.CreateSheetCommand.ExecuteAsync(null);
        var question = Assert.Single(dialogs.AttributionQuestions);
        Assert.Equal(["Alice"], question.OtherCreators);
        var found = AttributionReader.Read(kept)!;
        Assert.Equal(["Alice"], found.Creators);
        Assert.Contains("Edited by Bob", found.Text);

        // Cancel: nothing is written.
        string cancelled = Path.Combine(dir, "cancelled.png");
        dialogs.NextFile = cancelled;
        dialogs.AttributionAnswer = null;
        await vm.CreateSheetCommand.ExecuteAsync(null);
        Assert.False(File.Exists(cancelled));

        // Explicit decision: the user takes over; the previous creator stays in the history.
        string mine = Path.Combine(dir, "mine.png");
        dialogs.NextFile = mine;
        dialogs.AttributionAnswer = AttributionChoice.AttributeToMe;
        await vm.CreateSheetCommand.ExecuteAsync(null);
        found = AttributionReader.Read(mine)!;
        Assert.Equal(["Bob"], found.Creators);
        Assert.Contains(found.History, h => h.Contains("previously attributed to Alice", StringComparison.Ordinal));

        // Inputs without attribution (or by the same user) need no question.
        string plain = Path.Combine(dir, "plain.bmp.png");
        ImageIO.SavePng(Cell(0xFF0000FF), plain, Attribution.ForNewAsset("Bob"));
        vm.Files = [plain];
        dialogs.AttributionQuestions.Clear();
        dialogs.NextFile = Path.Combine(dir, "own.png");
        await vm.CreateSheetCommand.ExecuteAsync(null);
        Assert.Empty(dialogs.AttributionQuestions);
        Assert.Equal(["Bob"], AttributionReader.Read(dialogs.NextFile)!.Creators);
        Assert.Empty(dialogs.Errors);
    }

    [Fact]
    public async Task CreatorNameFromSettingsIsUsedByLaterExports()
    {
        await Sta.Run(() =>
        {
            var (services, _, dir) = CreateServices();
            using var _ = services;
            var main = new MainViewModel(services);
            var settings = main.Pages.OfType<SettingsViewModel>().Single();

            settings.CreatorName = "  Carol  ";
            Assert.Equal("Carol", Attribution.CurrentCreator);
            Assert.Equal("Carol", services.Settings.CreatorName);
            Assert.Equal(Attribution.TextFor("Carol"), settings.AttributionPreview);
            Assert.Equal("Carol", services.SettingsStore.Load().Settings.CreatorName);    // saved for later sessions
            Assert.Equal(Attribution.TextFor("Carol"), main.Pages.OfType<AboutViewModel>().Single().AttributionPreview);

            string file = Path.Combine(dir, "carol.png");
            ImageIO.SavePng(Cell(0xFF123456), file);
            Assert.Equal(["Carol"], AttributionReader.Read(file)!.Creators);

            settings.CreatorName = "";
            Assert.Equal(Attribution.DefaultCreator, Attribution.CurrentCreator);           // empty restores the default
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void SettingsOfVersion2AreMigratedWithoutTouchingTheOldFile()
    {
        string dir = TestData.NewTempDirectory("migrate");
        string old = Path.Combine(dir, AppIdentity.Legacy.Version2Id, "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllText(old, """{ "Version": 1, "Theme": "Dark", "GifAuthor": "Dana", "OutputFolder": "D:\\Wiki" }""");
        byte[] before = File.ReadAllBytes(old);
        var store = new SettingsStore(Path.Combine(dir, "TibiaFoundry", "settings.json"));

        var (settings, status, detail) = store.Load(previousLocations: [old]);
        Assert.Equal(SettingsLoadStatus.MigratedFromPreviousName, status);
        Assert.Equal(old, detail);
        Assert.Equal("Dana", settings.CreatorName);           // the 2.x GIF author is the creator name
        Assert.Equal(ThemePreference.Dark, settings.Theme);
        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        store.Save(settings);

        Assert.Equal(before, File.ReadAllBytes(old));          // the old version keeps working
        using var doc = JsonDocument.Parse(File.ReadAllText(store.FilePath));
        Assert.Equal("Dana", doc.RootElement.GetProperty("CreatorName").GetString());
        Assert.False(doc.RootElement.TryGetProperty("GifAuthor", out _));

        // Next start: the new file is used, the old one is ignored.
        Assert.Equal(SettingsLoadStatus.Loaded, store.Load(previousLocations: [old]).Status);
    }

    [Fact]
    public void SettingsOfVersion3AreMigratedBeforeOlderOnes()
    {
        string dir = TestData.NewTempDirectory("migrate");
        string forge = Path.Combine(dir, AppIdentity.Legacy.Version3Id, "settings.json");
        string wikiTools = Path.Combine(dir, AppIdentity.Legacy.Version2Id, "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(forge)!);
        Directory.CreateDirectory(Path.GetDirectoryName(wikiTools)!);
        File.WriteAllText(forge, """{ "Version": 2, "Theme": "Light", "CreatorName": "Erin" }""");
        File.WriteAllText(wikiTools, """{ "Version": 1, "Theme": "Dark", "GifAuthor": "Dana" }""");
        byte[] before = File.ReadAllBytes(forge);
        var store = new SettingsStore(Path.Combine(dir, "TibiaFoundry", "settings.json"));

        var (settings, status, detail) = store.Load(previousLocations: [forge, wikiTools]);
        Assert.Equal(SettingsLoadStatus.MigratedFromPreviousName, status);
        Assert.Equal(forge, detail);                          // the newest earlier version wins
        Assert.Equal("Erin", settings.CreatorName);
        Assert.Equal(ThemePreference.Light, settings.Theme);
        store.Save(settings);
        Assert.Equal(before, File.ReadAllBytes(forge));        // version 3.0 keeps working
    }

    private const string OldDefaultAuthor = "<default author of version 2.x>";

    [Theory]
    [InlineData(OldDefaultAuthor, Attribution.DefaultCreator)]     // the old default author is not a user choice
    [InlineData("RockyLaRo", "RockyLaRo")]
    [InlineData("Someone Else", "Someone Else")]
    [InlineData("", Attribution.DefaultCreator)]
    public void OldAuthorSettingsBecomeTheCreatorName(string author, string expected)
    {
        // A placeholder in the data, so test names and results do not carry the former default author's name.
        if (author == OldDefaultAuthor) author = AppIdentity.Legacy.DefaultGifAuthor;
        string dir = TestData.NewTempDirectory("migrate");
        string v2 = Path.Combine(dir, "v2.json");
        File.WriteAllText(v2, JsonSerializer.Serialize(new { Version = 1, GifAuthor = author }));
        Assert.Equal(expected, new SettingsStore(Path.Combine(dir, "new1.json")).Load(previousLocations: [v2]).Settings.CreatorName);

        string v1 = Path.Combine(dir, "v1.json");
        File.WriteAllText(v1, JsonSerializer.Serialize(new { OutputPath = dir, Author = author }));
        Assert.Equal(expected, new SettingsStore(Path.Combine(dir, "new2.json")).Load([v1]).Settings.CreatorName);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    public void DataOfEarlierVersionsIsFoundForMigration(int version)
    {
        // By version number, so test names and results do not carry the earlier identifiers.
        string oldMarker = (version == 3 ? AppIdentity.Legacy.Version3Id : AppIdentity.Legacy.Version2Id) + ".portable";
        string program = TestData.NewTempDirectory("portable");
        File.WriteAllText(Path.Combine(program, oldMarker), "");
        var paths = AppPaths.Resolve(program, portableBuild: false);
        Assert.True(paths.IsPortable);                                      // an old marker still selects portable mode
        Assert.Equal(Path.Combine(program, "TibiaFoundry-Data", "settings.json"), paths.SettingsFile);
        Assert.Equal([Path.Combine(program, AppIdentity.Legacy.Version3Id + "-Data", "settings.json"), Path.Combine(program, AppIdentity.Legacy.Version2Id + "-Data", "settings.json")],
            paths.PreviousSettingsFiles);

        var installed = AppPaths.Resolve(TestData.NewTempDirectory("installed"), portableBuild: false);
        Assert.EndsWith(Path.Combine("TibiaFoundry", "settings.json"), installed.SettingsFile);
        Assert.Equal(2, installed.PreviousSettingsFiles.Count);
        Assert.EndsWith(Path.Combine(AppIdentity.Legacy.Version3Id, "settings.json"), installed.PreviousSettingsFiles[0]);
        Assert.EndsWith(Path.Combine(AppIdentity.Legacy.Version2Id, "settings.json"), installed.PreviousSettingsFiles[1]);
    }

    [Fact]
    public async Task DataExportsSayThatTheyCarryNoAttribution()
    {
        var (services, dialogs, dir) = CreateServices();
        using var _ = services;
        var maps = new Tibia.Protobuf.Staticmapdata.StaticMapData();
        maps.HouseMaps.Add(new Tibia.Protobuf.Staticmapdata.HouseMap { HouseId = 1 });
        string data = Path.Combine(dir, "staticmapdata-test.dat");
        File.WriteAllBytes(data, Google.Protobuf.MessageExtensions.ToByteArray(maps));
        await services.Data.LoadStaticMapAsync(data);
        var vm = new HouseImagesViewModel(services);
        dialogs.NextFile = Path.Combine(dir, "houses.json");
        await vm.ExportJsonCommand.ExecuteAsync(null);
        Assert.True(File.Exists(dialogs.NextFile));
        Assert.Contains(Attribution.NoMetadataNote, services.Status.FullText);
        Assert.Equal(dialogs.NextFile, services.Status.Link?.Path);          // the saved file is named (and clickable)
    }

    [Fact]
    public async Task ComparisonReportsStartWithTheAttribution()
    {
        string assets = TestData.RequireClientAssets();
        Attribution.CurrentCreator = "Erin";
        var (services, dialogs, dir) = CreateServices();
        using var _ = services;
        var vm = new CompareViewModel(services);
        string appearances = TibiaClientPaths.FindAssetFile(assets, AssetTypes.Appearances)!;
        vm.OldFile = appearances;
        vm.NewFile = appearances;
        await vm.CompareFilesCommand.ExecuteAsync(null);
        dialogs.NextFile = Path.Combine(dir, "report.txt");
        vm.SaveReportCommand.Execute(null);
        string report = File.ReadAllText(dialogs.NextFile);
        Assert.StartsWith(Attribution.TextFor("Erin"), report);
        Assert.Contains("Old: " + appearances, report);
    }
}
