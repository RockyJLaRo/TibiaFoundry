using System.Text;
using Tibia.Protobuf.Appearances;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Items;
using TibiaFoundry.Core.Library;
using TibiaFoundry.Core.Localization;
using TibiaFoundry.Core.Scripts;

namespace TibiaFoundry.Tests;

/// <summary>
/// Regression tests of the 9.0.4 audit: data and program logic that 9.0–9.0.3 wrote or matched in the
/// interface language, folders directly on a drive, and a queue that a failing listener could stop.
/// </summary>
public sealed class Release904Tests
{
    // ------------------------------------------------------------------ PathGuard: drive roots

    [Fact]
    public void FilesOnADriveRootCountAsInsideIt()
    {
        string drive = Path.GetPathRoot(Path.GetTempPath())!;                      // "C:\"
        string file = Path.Combine(drive, "Folder", "image.png");
        Assert.True(PathGuard.IsInside(drive, file));
        Assert.True(PathGuard.IsSameOrInside(drive, file));
        Assert.True(PathGuard.IsSameOrInside(drive, drive));
        Assert.False(PathGuard.IsInside(drive, drive));                            // strictly inside
        Assert.True(PathGuard.Overlaps(file, drive));
        Assert.Equal(file, PathGuard.ResolveInside(drive, "Folder/image.png"));    // was refused as "outside"
        Assert.Throws<UnsafePathException>(() => PathGuard.ResolveInside(drive, "../image.png"));
        // Another drive, and a folder whose name only starts the same, are still outside.
        Assert.False(PathGuard.IsInside(drive, drive[0] == 'Q' ? @"R:\Folder\image.png" : @"Q:\Folder\image.png"));
        string folder = Path.Combine(drive, "Folder");
        Assert.False(PathGuard.IsInside(folder, folder + "-sibling" + Path.DirectorySeparatorChar + "x.png"));
        Assert.True(PathGuard.IsInside(folder + Path.DirectorySeparatorChar, file));
    }

    // ------------------------------------------------------------------ the queue

    [Fact]
    public async Task AFailingListenerDoesNotStopTheQueue()
    {
        using var queue = new ProcessingQueue(() => 1);
        int told = 0;
        queue.Changed += _ => throw new InvalidOperationException("the page was closing");
        queue.Changed += _ => told++;                                              // listeners after the failing one are still told
        queue.Finished += _ => throw new InvalidOperationException("the log was closed");
        var first = queue.Enqueue("First", "Test", (_, _) => Task.FromResult("one"));
        var second = queue.Enqueue("Second", "Test", (_, _) => Task.FromResult("two"));
        for (int i = 0; i < 500 && !(first.IsFinished && second.IsFinished); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal((QueueJobState.Completed, "one"), (first.State, first.Result));
        Assert.Equal((QueueJobState.Completed, "two"), (second.State, second.Result));   // stayed "Pending" for good before 9.0.4
        Assert.True(told >= 6, $"listeners were told {told} times");               // added, started and finished, twice
    }

    [Fact]
    public async Task AFailedJobSaysSoInTheInterfaceLanguage()
    {
        using var queue = new ProcessingQueue(() => 1);
        var job = queue.Enqueue("Failing", "Test", (_, _) => throw new IOException("disk full"));
        for (int i = 0; i < 500 && !job.IsFinished; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(QueueJobState.Failed, job.State);
        Assert.Equal(L.T("ProcessingQueue.Failed"), job.Message);
        foreach (var language in Languages.All) Assert.True(L.Table(language.Code).ContainsKey("ProcessingQueue.Failed"), language.Code);
    }

    // ------------------------------------------------------------------ thumbnails

    [Fact]
    public void TheThumbnailCacheOfAnEmptyOrMissingFolderIsEmpty()
    {
        var library = AssetLibrary.Open(Path.Combine(TestData.NewTempDirectory("thumbs"), "Library"));
        Assert.Equal((0L, 0), ThumbnailCache.Usage(library));
        Assert.Equal(0, ThumbnailCache.Clear(library));
        Directory.CreateDirectory(Path.Combine(library.ThumbnailFolder, "ab"));
        File.WriteAllBytes(Path.Combine(library.ThumbnailFolder, "ab", "abc_96.png"), new byte[10]);
        Assert.Equal((10L, 1), ThumbnailCache.Usage(library));
        Assert.Equal(1, ThumbnailCache.Clear(library));
    }
}

/// <summary>
/// 9.0.4: nothing the program saves, exports or compares may depend on the interface language. These tests
/// switch the language, so they run on their own (<see cref="LanguageSwitchCollection"/>).
/// </summary>
[Collection(nameof(LanguageSwitchCollection))]
public sealed class LanguageIndependentDataTests
{
    private static T InEveryLanguage<T>(Func<T> read)
    {
        try
        {
            T first = read();
            foreach (var language in Languages.All)
            {
                L.SetLanguage(language.Code);
                Assert.Equal(first, read());
            }
            return first;
        }
        finally
        {
            L.SetLanguage(Languages.DefaultCode);
        }
    }

    [Fact]
    public void ItemFlagsOfTheClientAreTheSameInEveryLanguage()
    {
        var appearance = new Appearance { Id = 100, Flags = new AppearanceFlags { Liquidcontainer = true, Container = true } };
        appearance.Flags.Npcsaledata.Add(new AppearanceFlagNPC { Name = "Rashid", BuyPrice = 1 });
        string flags = InEveryLanguage(() => string.Join(";", ItemImporters.ClientFlags(appearance)!));
        Assert.Equal("container;liquid container;npc trade", flags);
    }

    [Fact]
    public void ItemFlagsOfItemsXmlAreTheSameInEveryLanguage()
    {
        const string xml = """
            <items>
              <item id="100" name="fire field"><attribute key="type" value="magicfield"/></item>
              <item id="101" name="dustbin"><attribute key="type" value="trashholder"/><attribute key="floorchange" value="down"/></item>
            </items>
            """;
        var source = new ItemSource { Id = "xml", Kind = SourceKind.OtItemsXml, Name = "items.xml", IdType = IdType.OtbServer, ImportedAtUtc = DateTime.UtcNow };
        string flags = InEveryLanguage(() =>
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var import = ItemImporters.FromItemsXml(stream, source, IdType.OtbServer);
            return string.Join("|", import.Observations.OrderBy(o => o.Id).Select(o => string.Join(";", o.Flags ?? [])));
        });
        Assert.Equal("magic field|trash holder;floor change", flags);
        Assert.True(new[] { ItemFlags.MagicField, ItemFlags.TrashHolder, ItemFlags.FloorChange }.All(CatalogComparison.BehaviorFlags.Contains));
    }

    [Fact]
    public void FlagsACatalogSavedInAnotherLanguageAreReadAsTheirNeutralNames()
    {
        // What 9.0–9.0.3 wrote with a German, Spanish, Polish or Portuguese interface.
        foreach (var language in Languages.All.Where(l => l.Code != Languages.DefaultCode))
        {
            var table = L.Table(language.Code);
            Assert.Equal(ItemFlags.MagicField, ItemFlags.Canonical(table["ItemImporters.MagicField"]));
            Assert.Equal(ItemFlags.TrashHolder, ItemFlags.Canonical(table["ItemImporters.TrashHolder"]));
            Assert.Equal(ItemFlags.LiquidContainer, ItemFlags.Canonical(table["ItemImporters.LiquidContainer"]));
            Assert.Equal(ItemFlags.NpcTrade, ItemFlags.Canonical(table["ItemImporters.NpcTrade"]));
        }
        Assert.Equal("container", ItemFlags.Canonical("container"));
        IReadOnlyList<string> untouched = ["container", "market"];
        Assert.Same(untouched, ItemFlags.Canonical(untouched));                     // nothing to change: no new list

        var catalog = ItemCatalogStore.Create(Path.Combine(TestData.NewTempDirectory("catalog904"), "German"), "German");
        var source = new ItemSource { Id = "client", Kind = SourceKind.OfficialClient, Name = "client", IdType = IdType.TibiaObject, IsOfficial = true, ImportedAtUtc = DateTime.UtcNow };
        string german = L.Table("de")["ItemImporters.MagicField"];
        ItemCatalogStore.Commit(catalog, new ItemImport
        {
            Source = source,
            Observations =
            [
                new ItemObservation { SourceId = "client", IdType = IdType.TibiaObject, Id = 100, Name = "fire field", Flags = [german, "container", ItemFlags.MagicField] },
                new ItemObservation { SourceId = "client", IdType = IdType.TibiaObject, Id = 101, Name = "bag", Flags = ["container"] },
            ],
        });
        var reread = ItemCatalogStore.Load(catalog.Folder);
        Assert.Equal(["magic field", "container"], reread.Find(IdType.TibiaObject, 100)!.Flags);   // translated name mapped back, no duplicate
        Assert.Equal(["container"], reread.Find(IdType.TibiaObject, 101)!.Flags);
    }

    [Fact]
    public void ExportFoldersAreCategorizedTheSameInEveryLanguage()
    {
        var facts = new MediaFacts("png", 64, 64, 1, 100, DateTime.UtcNow, "00", null, null, null, null);
        string Category(string folder) => AssetIndexer.InferCategory(Path.Combine(@"C:\Tibia_Foundry", folder, "Rook", "image.png"), facts);
        string categories = InEveryLanguage(() => string.Join("|", Category("Sprite_Sheets"), Category("House_Images"), Category("Map_Sections"), Category("Map_Exports"), Category("outfits")));
        Assert.Equal(string.Join("|", AssetCategories.SpriteSheets, AssetCategories.Houses, AssetCategories.MapSections, AssetCategories.Maps, AssetCategories.Outfits), categories);
    }

    [Fact]
    public void ProtectedFolderNamesAreNotTranslated() =>
        Assert.True(InEveryLanguage(() => EmptyEntryFinderScript.ProtectedNames.Contains("System Volume Information") && EmptyEntryFinderScript.ProtectedNames.Contains("$RECYCLE.BIN")));
}
