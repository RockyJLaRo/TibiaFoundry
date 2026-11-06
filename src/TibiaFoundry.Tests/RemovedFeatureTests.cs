using System.Reflection;
using System.Text.Json;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Items;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.Tests;

/// <summary>
/// 8.0 removed Item Discovery (the hotkey and tooltip tool of 7.0–7.3) completely: no tool, page,
/// type, setting or location of it remains; settings written by 7.2/7.3 lose only its options;
/// catalogs with records it collected still load; and a copy of the client's options file it left
/// behind is reported, never touched.
/// </summary>
[Collection(nameof(CatalogCollection))]
public sealed class RemovedFeatureTests
{
    [Fact]
    public void NoToolPageOrTypeOfItemDiscoveryRemains()
    {
        Assert.DoesNotContain(BuiltInTools.Pages, p => p.Manifest.Id == "item-discovery" || p.ViewModelType.Name.StartsWith("ItemDiscovery", StringComparison.Ordinal));
        string[] removed = ["ItemDiscovery", "DiscoverySession", "DiscoveryCollector", "AutomaticHover", "ObsWebSocket", "ObsCapture", "PointerMover", "GlobalHotkey", "WindowsOcr", "ClientOptionsDocument"];
        foreach (var assembly in new[] { typeof(AppSettings).Assembly, typeof(BuiltInTools).Assembly })
            foreach (var type in assembly.GetTypes())
            {
                Assert.False(type.Namespace?.EndsWith(".Discovery", StringComparison.Ordinal) == true, type.FullName);
                Assert.DoesNotContain(removed, r => type.Name.StartsWith(r, StringComparison.Ordinal));
            }
        Assert.Null(typeof(IDialogService).GetMethod("PromptSecret"));
        Assert.Null(typeof(AppServices).GetProperty("DiscoveryFolder", BindingFlags.Public | BindingFlags.Instance));
        Assert.DoesNotContain(TibiaFoundry.Core.Locations.LocationResolver.All, d => d.Id is "client-options" or "screenshots");
    }

    [Fact]
    public void SettingsOfTheRemovedToolAreDroppedAndEverythingElseIsKept()
    {
        string dir = TestData.NewTempDirectory();
        string file = Path.Combine(dir, "settings.json");
        string original = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Version"] = 6,
            ["LastPage"] = "ItemDiscoveryViewModel",
            ["CreatorName"] = "Someone",
            ["ToolState"] = new Dictionary<string, string>
            {
                ["ItemDiscovery.captureSource"] = "obs", ["ItemDiscovery.$settingsVersion"] = "1", ["ItemCatalog.lastCatalog"] = @"C:\Catalogs\Mine",
            },
            ["ClientLocations"] = new Dictionary<string, string>
            {
                ["live:client-options"] = @"C:\Tibia\conf\clientoptions.json", ["test:screenshots"] = @"C:\Tibia\screenshots", ["test:assets"] = @"C:\Tibia\assets",
            },
        });
        File.WriteAllText(file, original);

        var (settings, status, _) = new SettingsStore(file).Load();

        Assert.Equal(SettingsLoadStatus.Loaded, status);
        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.Null(settings.LastPage);
        Assert.Equal("Someone", settings.CreatorName);
        Assert.Equal(["ItemCatalog.lastCatalog"], settings.ToolState.Keys);
        Assert.Equal(["test:assets"], settings.ClientLocations.Keys);
        Assert.Contains(settings.MigrationNotes, n => n.Contains("Item Discovery was removed", StringComparison.Ordinal));
        // The file as it was is kept before this version saves over it.
        Assert.Equal(original, File.ReadAllText(file + ".v6.bak"));
    }

    [Fact]
    public void CatalogsWithRecordsCollectedByTheRemovedToolStillLoadAndExport()
    {
        var catalog = ItemCatalogStore.Create(Path.Combine(TestData.NewTempDirectory("catalog"), "Legacy"), "Legacy");
        var collected = new ItemSource { Id = "collected", Kind = SourceKind.ClientObservation, Name = "Collected in the client", IdType = IdType.TibiaObject, IsOfficial = true };
        ItemObservation Record(uint id, string name, ObservationMethod method) => new()
        {
            SourceId = "collected", IdType = IdType.TibiaObject, Id = id, Name = name, Status = ObservationStatus.Verified, Method = method, ObservedAtUtc = DateTime.UtcNow,
        };
        ItemCatalogStore.Append(catalog, collected,
            [Record(101, "earth", ObservationMethod.ClientTooltip), Record(102, "stone floor", ObservationMethod.ClientScreenshot), Record(103, "grass", ObservationMethod.ClientConfirmedByUser)]);

        var reloaded = ItemCatalogStore.Load(catalog.Folder);

        Assert.Empty(reloaded.LoadProblems);
        Assert.Equal(3, reloaded.CountOf("collected"));
        Assert.Equal(["earth", "stone floor", "grass"], new uint[] { 101, 102, 103 }.Select(id => reloaded.Find(IdType.TibiaObject, id)!.BestName));
        string json = ItemExports.ToJson(reloaded, reloaded.Entries, "all");
        Assert.Contains("stone floor", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ACopyOfTheClientOptionsLeftBehindIsReportedAndNeverTouched()
    {
        string data = TestData.NewTempDirectory();
        string root = Path.Combine(data, LeftoverHotkeyBackups.FolderName);
        void Session(string name, string state, bool backup)
        {
            string folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
            File.WriteAllText(Path.Combine(folder, "session.json"),
                $$"""{"formatVersion":2,"id":"{{name}}","state":"{{state}}","createdUtc":"2026-10-14T10:00:00Z","optionsPath":"C:/Tibia/conf/clientoptions.json"}""");
            if (backup) File.WriteAllText(Path.Combine(folder, "clientoptions.original.json"), "{\"original\":true}");
        }
        Assert.Empty(LeftoverHotkeyBackups.Find(data));          // no folder at all
        Session("a", "Applied", backup: true);
        Session("b", "Restored", backup: true);
        Session("c", "Prepared", backup: false);
        Directory.CreateDirectory(Path.Combine(root, "d"));
        File.WriteAllText(Path.Combine(root, "d", "session.json"), "{ not json");
        File.WriteAllText(Path.Combine(root, "d", "clientoptions.original.json"), "{}");
        var before = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText);

        var found = LeftoverHotkeyBackups.Find(data);

        var only = Assert.Single(found);
        Assert.Equal(Path.Combine(root, "a", "clientoptions.original.json"), only.BackupPath);
        Assert.Equal("C:/Tibia/conf/clientoptions.json", only.OptionsPath);
        Assert.Contains(only.BackupPath, LeftoverHotkeyBackups.Describe(only), StringComparison.Ordinal);
        Assert.Equal(before, Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText));
    }

    /// <summary>
    /// Catalogs with records Item Discovery collected keep their rule: a later record of the same
    /// client-observation source replaces the earlier ones, and the history stays. (Replaces a test
    /// that was removed with the tool.)
    /// </summary>
    [Fact]
    public void LegacyClientObservationsKeepOnlyTheLatestRecordOfTheirSource()
    {
        var catalog = ItemCatalogStore.Create(Path.Combine(TestData.NewTempDirectory("catalog"), "C"), "C");
        var source = new ItemSource { Id = "client-15.33.test", Kind = SourceKind.ClientObservation, Name = "Names read in the client (15.33)", IdType = IdType.TibiaObject };
        ItemObservation O(string name, ObservationStatus status) => new() { SourceId = source.Id, IdType = IdType.TibiaObject, Id = 42, Name = name, Status = status };
        ItemCatalogStore.Append(catalog, source, [O("gold coln", ObservationStatus.Unverified)]);
        ItemCatalogStore.Append(catalog, source, [O("gold coin", ObservationStatus.Verified)]);
        var entry = ItemCatalogStore.Load(catalog.Folder).Find(IdType.TibiaObject, 42)!;
        Assert.Single(entry.NameGroups);
        Assert.Equal("gold coin", entry.BestName);
        Assert.Equal(2, entry.Observations.Count);   // the history stays
        Assert.Single(entry.Current);
    }

    /// <summary>
    /// Two copies of one catalog (two windows): decisions recorded in one are kept when the other,
    /// which does not know them, imports something. (Replaces a test removed with Item Discovery.)
    /// </summary>
    [Fact]
    public void ACatalogWrittenFromTwoCopiesKeepsTheDecisionsOfBoth()
    {
        string folder = Path.Combine(TestData.NewTempDirectory("catalog"), "Shared");
        var first = ItemCatalogStore.Create(folder, "Shared");
        ItemCatalogStore.Commit(first, new ItemImport
        {
            Source = new ItemSource { Id = "list", Kind = SourceKind.NameList, Name = "list", IdType = IdType.TibiaObject },
            Observations = [new ItemObservation { SourceId = "list", IdType = IdType.TibiaObject, Id = 7, Name = "seven" }],
        });
        var second = ItemCatalogStore.Load(folder);
        ItemCatalogStore.Decide(second, second.Find(IdType.TibiaObject, 7)!, "lucky seven");
        // The first copy, which does not know the decisions, imports something.
        ItemCatalogStore.Commit(first, new ItemImport
        {
            Source = new ItemSource { Id = "list2", Kind = SourceKind.NameList, Name = "list 2", IdType = IdType.TibiaObject },
            Observations = [new ItemObservation { SourceId = "list2", IdType = IdType.TibiaObject, Id = 8, Name = "eight" }],
        });
        var reloaded = ItemCatalogStore.Load(folder);
        Assert.Contains(reloaded.Sources, x => x.Kind == SourceKind.UserDecision);
        Assert.Contains(reloaded.Sources, x => x.Id == "list2");
        Assert.Equal("lucky seven", reloaded.Find(IdType.TibiaObject, 7)!.BestName);
    }
}
