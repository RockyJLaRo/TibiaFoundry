using System.IO.Compression;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Library;
using TibiaFoundry.Core.Metadata;

namespace TibiaFoundry.Tests;

/// <summary>
/// The Asset Library (8.8): the SQLite index, incremental indexing, metadata, categories, collections
/// and their files, duplicate detection, versions, thumbnails, integrity, backup and restore. Every test
/// works in its own temporary folder; no user data is touched.
/// </summary>
public sealed class AssetLibraryTests
{
    /// <summary>A small test image: a colored square with a transparent border.</summary>
    internal static RgbaImage Square(uint color, int size = 32, int border = 4)
    {
        var img = new RgbaImage(size, size);
        for (int y = border; y < size - border; y++)
            for (int x = border; x < size - border; x++) img[x, y] = color;
        img[border, border] = 0xFF000000; // one dark pixel, so the picture has structure
        return img;
    }

    internal static string Png(string folder, string name, RgbaImage image)
    {
        string path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ImageIO.SavePng(image, path, Attribution.ForNewAsset("Tester"));
        return path;
    }

    private static (AssetLibrary Library, string Files) NewLibrary()
    {
        string dir = TestData.NewTempDirectory("library");
        return (AssetLibrary.Open(Path.Combine(dir, "Library")), Path.Combine(dir, "Files"));
    }

    [Fact]
    public void IndexingIsIncrementalAndKeepsMetadataAndVersions()
    {
        var (lib, files) = NewLibrary();
        string a = Png(files, "Outfits/128_Idle_South.png", Square(0xFFFF0000));
        Png(files, "Creatures/Dragon_Lord_2x.png", Square(0xFF00FF00, 64, 8));
        File.WriteAllText(Path.Combine(files, "notes.txt"), "not indexed without IncludeData");

        var first = AssetIndexer.IndexFolder(lib, files, ct: TestContext.Current.CancellationToken);
        Assert.Equal((2, 2, 0), (first.Seen, first.Added, first.Unchanged));
        var outfit = lib.FindByPath(a)!;
        Assert.Equal(AssetCategories.Outfits, outfit.Category);                        // from the folder name
        Assert.Equal("128", outfit.OriginalId);                                         // from the file name
        Assert.Equal((32, 32, 1), (outfit.Width, outfit.Height, outfit.Frames));
        Assert.Equal("Tester", outfit.Creator);                                          // embedded attribution read back
        Assert.Equal("Dragon Lord", lib.Search(new AssetQuery { Text = "dragon" }).Assets.Single().Name);

        lib.Update([outfit.Id], new AssetEdit { Name = "Citizen", Description = "Red test", Tags = ["red", "test"], Fields = new Dictionary<string, string> { ["Source page"] = "wiki" } });

        var second = AssetIndexer.IndexFolder(lib, files, ct: TestContext.Current.CancellationToken);
        Assert.Equal((2, 0, 2), (second.Seen, second.Added, second.Unchanged));        // nothing read again

        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddMinutes(1));
        ImageIO.SavePng(Square(0xFF0000FF), a, Attribution.ForNewAsset("Tester"));
        var third = AssetIndexer.IndexFolder(lib, files, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, third.ContentChanged);
        var changed = lib.Get(outfit.Id)!;
        Assert.Equal("Citizen", changed.Name);                                           // the user's edits stay
        Assert.Equal(["red", "test"], changed.Tags);
        Assert.Equal("wiki", changed.Fields["Source page"]);
        Assert.Single(lib.Versions(outfit.Id));                                          // the earlier content is recorded
        Assert.NotEqual(outfit.Sha256, changed.Sha256);

        File.Delete(a);
        var fourth = AssetIndexer.IndexFolder(lib, files, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, fourth.MarkedMissing);
        Assert.True(lib.Get(outfit.Id)!.Missing);                                        // kept, marked missing, never removed
        Assert.Equal(2, lib.Statistics().Assets);
    }

    [Fact]
    public void SearchFiltersByEveryField()
    {
        var (lib, files) = NewLibrary();
        Png(files, "15.33.8791c6/Items/3031 Gold Coin.png", Square(0xFFFFD700));
        Png(files, "Effects/7.png", Square(0xFF00FFFF, 64, 2));
        AssetIndexer.IndexFolder(lib, files, ct: TestContext.Current.CancellationToken);
        var coin = lib.Search(new AssetQuery { Text = "gold" }).Assets.Single();
        lib.Update([coin.Id], new AssetEdit { Tags = ["currency"], Creator = "Someone", ExportStatus = ExportStates.Published });

        Assert.Equal("15.33.8791c6", coin.ClientVersion);
        Assert.Single(lib.Search(new AssetQuery { Text = "3031" }).Assets);              // the game's ID
        Assert.Single(lib.Search(new AssetQuery { Category = AssetCategories.Items }).Assets);
        Assert.Single(lib.Search(new AssetQuery { ClientVersion = "15.33.8791c6" }).Assets);
        Assert.Single(lib.Search(new AssetQuery { Tag = "currency" }).Assets);
        Assert.Single(lib.Search(new AssetQuery { Creator = "some" }).Assets);
        Assert.Single(lib.Search(new AssetQuery { ExportStatus = ExportStates.Published }).Assets);
        Assert.Single(lib.Search(new AssetQuery { MinWidth = 60 }).Assets);
        Assert.Equal(2, lib.Search(new AssetQuery { Format = "png", Animated = false }).Total);
        Assert.Empty(lib.Search(new AssetQuery { Text = "100%_" }).Assets);             // LIKE wildcards are escaped
        Assert.Single(lib.Search(new AssetQuery { Limit = 1 }).Assets);
        Assert.Equal(2, lib.Search(new AssetQuery { Limit = 1 }).Total);
        var facets = lib.Facets();
        Assert.Contains(facets.Tags, t => t.Value == "currency");
        Assert.Contains(facets.ClientVersions, v => v.Value == "15.33.8791c6");
    }

    [Fact]
    public void CustomCategoriesCanBeAddedRenamedAndDeleted()
    {
        var (lib, files) = NewLibrary();
        Png(files, "a.png", Square(0xFF112233));
        AssetIndexer.IndexFolder(lib, files, ct: TestContext.Current.CancellationToken);
        var asset = lib.Search(new AssetQuery()).Assets.Single();
        lib.AddCategory("Summer Event");
        lib.Update([asset.Id], new AssetEdit { Category = "Summer Event" });
        lib.RenameCategory("Summer Event", "Summer 2026");
        Assert.Equal("Summer 2026", lib.Get(asset.Id)!.Category);
        Assert.Contains(lib.Categories(), c => c is { Name: "Summer 2026", IsCustom: true, Count: 1 });
        Assert.Throws<InvalidOperationException>(() => lib.RenameCategory(AssetCategories.Items, "x"));
        Assert.Equal(1, lib.DeleteCategory("Summer 2026"));
        Assert.Equal(AssetCategories.Other, lib.Get(asset.Id)!.Category);               // moved, not lost
    }

    [Fact]
    public void CollectionsAreCreatedRenamedDuplicatedExportedAndImported()
    {
        var (lib, files) = NewLibrary();
        for (int i = 0; i < 3; i++) Png(files, $"{i}.png", Square(0xFF000000 | (uint)(i * 40 + 20)));
        AssetIndexer.IndexFolder(lib, files, ct: TestContext.Current.CancellationToken);
        var ids = lib.Search(new AssetQuery { Sort = AssetSort.Name }).Assets.Select(a => a.Id).ToList();

        long col = lib.CreateCollection("Creature Gallery", "For the wiki");
        Assert.Equal(3, lib.AddToCollection(col, ids));
        Assert.Equal(0, lib.AddToCollection(col, ids));                                  // no duplicates
        Assert.Equal(ids, lib.CollectionItems(col));                                    // order kept
        Assert.Throws<InvalidOperationException>(() => lib.CreateCollection("creature gallery"));
        lib.RenameCollection(col, "Creature Gallery 2026");
        long copy = lib.DuplicateCollection(col, "Copy");
        Assert.Equal(ids, lib.CollectionItems(copy));
        Assert.Equal(1, lib.RemoveFromCollection(copy, [ids[0]]));
        Assert.Equal(ids[0], lib.Collection(col)!.ThumbnailAssetId);
        Assert.Equal(3, lib.Search(new AssetQuery { CollectionId = col }).Total);

        string exported = Path.Combine(files, "gallery.tfcollection.json");
        lib.ExportCollection(col, exported);
        lib.DeleteCollection(col);
        Assert.Equal(3, lib.Statistics().Assets);                                        // deleting a collection keeps its assets
        var imported = lib.ImportCollection(exported);
        Assert.Equal(("Creature Gallery 2026", 3), (imported.Name, imported.Matched));  // matched by content hash
        Assert.Empty(imported.NotFound);
        Assert.Equal("For the wiki", lib.Collection(imported.CollectionId)!.Description);

        File.WriteAllText(Path.Combine(files, "bad.json"), "{\"format\":\"something-else\"}");
        Assert.Throws<InvalidDataException>(() => lib.ImportCollection(Path.Combine(files, "bad.json")));
    }

    [Fact]
    public void DuplicatesAreFoundAndNeverRemoved()
    {
        var (lib, files) = NewLibrary();
        var sprite = Square(0xFFAA3355, 32, 6);
        Png(files, "15.32/a/Sprite 1.png", sprite);
        File.Copy(Path.Combine(files, "15.32/a/Sprite 1.png"), Path.Combine(Directory.CreateDirectory(Path.Combine(files, "15.33/b")).FullName, "other name.png"));
        Png(files, "big/sprite_2x.png", ImageResampler.Resize(sprite, 64, 64, ResampleFilter.Nearest));   // the same graphic, enlarged
        var similar = Square(0xFFAA3356, 32, 6);                                                             // one channel off by one
        Png(files, "similar.png", similar);
        Png(files, "similar-b.png", Square(0xFFAA3357, 32, 6));
        Png(files, "different.png", Square(0xFF10F010, 48, 0));
        AssetIndexer.IndexFolder(lib, files, ct: TestContext.Current.CancellationToken);

        var report = DuplicateFinder.Find(lib, similarity: 4, ct: TestContext.Current.CancellationToken);
        var identical = report.Groups.Single(g => g.Kind == DuplicateKind.IdenticalFiles);
        Assert.Equal(2, identical.Assets.Count);
        Assert.True(identical.DifferentNames);
        Assert.True(identical.DifferentClientVersions);
        var scaled = report.Groups.Single(g => g.Kind == DuplicateKind.SameGraphicOtherSize);
        Assert.Contains(scaled.Assets, a => a.Width == 64);
        Assert.Contains(report.Groups, g => g.Kind == DuplicateKind.VisuallySimilar && g.Assets.Any(a => a.FileName == "similar.png"));
        Assert.DoesNotContain(report.Groups, g => g.Assets.Any(a => a.FileName == "different.png"));
        Assert.Equal(6, lib.Statistics().Assets);                                        // only reported
        Assert.Equal(6, Directory.GetFiles(files, "*.png", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void SignaturesIgnoreEnlargementAndBordersButNotContent()
    {
        var img = Square(0xFF336699, 16, 2);
        string key = ImageSignature.PixelKey(img);
        Assert.Equal(key, ImageSignature.PixelKey(ImageResampler.Resize(img, 48, 48, ResampleFilter.Nearest)));
        var padded = new RgbaImage(40, 40);
        padded.DrawImage(img, 10, 5);
        Assert.Equal(key, ImageSignature.PixelKey(padded));
        var changed = img.Clone();
        changed[8, 8] = 0xFFFFFFFF;
        Assert.NotEqual(key, ImageSignature.PixelKey(changed));
        Assert.Equal(0, ImageSignature.Distance(ImageSignature.DifferenceHash(img), ImageSignature.DifferenceHash(img.Clone())));
    }

    [Fact]
    public void ThumbnailsKeepPixelArtSharpAndAreCached()
    {
        var (lib, files) = NewLibrary();
        string path = Png(files, "s.png", Square(0xFFCC2200));
        AssetIndexer.IndexFolder(lib, files, ct: TestContext.Current.CancellationToken);
        var asset = lib.FindByPath(path)!;
        string thumb = ThumbnailCache.Get(lib, asset, 96)!;
        var t = ImageIO.Load(thumb);
        Assert.Equal((96, 96), (t.Width, t.Height));                                     // 32 → 96: a whole-number enlargement
        Assert.Equal(Square(0xFFCC2200)[10, 10], t[30, 30]);
        Assert.Equal(0u, t[0, 0] >> 24);                                                 // transparency kept
        Assert.Equal(thumb, ThumbnailCache.Get(lib, asset, 96));
        Assert.Equal(1, ThumbnailCache.Usage(lib).Files);
    }

    [Fact]
    public void CopyIntoLibraryReusesIdenticalContent()
    {
        var (lib, files) = NewLibrary();
        string a = Png(files, "x.png", Square(0xFF445566));
        var r1 = AssetIndexer.IndexFiles(lib, [a], new IndexOptions { CopyIntoLibrary = true }, ct: TestContext.Current.CancellationToken);
        var r2 = AssetIndexer.IndexFiles(lib, [a], new IndexOptions { CopyIntoLibrary = true }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, r1.Added);
        Assert.Equal(0, r2.Added);
        var asset = lib.Search(new AssetQuery()).Assets.Single();
        Assert.True(asset.Managed);
        Assert.StartsWith(lib.FilesFolder, asset.Path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(a));                                                     // the original stays where it was
    }

    [Fact]
    public void BackupAndRestoreRoundTripAndRejectDamagedBackups()
    {
        var (lib, files) = NewLibrary();
        Png(files, "one.png", Square(0xFF123456));
        AssetIndexer.IndexFolder(lib, files, ct: TestContext.Current.CancellationToken);
        long col = lib.CreateCollection("Keep me");
        lib.AddToCollection(col, lib.SearchIds(new AssetQuery()));
        ExportProfiles.Save(lib.ProfilesFolder, ExportProfiles.CustomFansite with { Id = "mine", Name = "Mine", BuiltIn = false });
        Assert.Equal("ok", lib.CheckDatabase());

        string backup = Path.Combine(files, "library-backup.zip");
        lib.Backup(backup);
        lib.DeleteCollection(col);
        lib.Remove(lib.SearchIds(new AssetQuery()));
        Assert.Equal(0, lib.Statistics().Assets);

        var restored = AssetLibrary.Restore(lib.Folder, backup);
        Assert.Equal(1, restored.Statistics().Assets);
        Assert.NotNull(restored.CollectionNamed("Keep me"));
        Assert.Contains(ExportProfiles.Load(restored.ProfilesFolder), p => p.Id == "mine");
        Assert.Contains(Directory.GetFiles(lib.Folder), f => f.Contains(".before-restore-", StringComparison.Ordinal));  // the replaced index is kept

        string bad = Path.Combine(files, "bad.zip");
        using (var zip = ZipFile.Open(bad, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry(AssetLibrary.DatabaseFileName).Open())) w.Write("not a database");
        Assert.ThrowsAny<Exception>(() => AssetLibrary.Restore(lib.Folder, bad));
        Assert.Equal(1, AssetLibrary.Open(lib.Folder).Statistics().Assets);              // a bad backup changes nothing
    }

    [Fact]
    public void ThousandsOfAssetsIndexAndSearchQuickly()
    {
        var (lib, files) = NewLibrary();
        Directory.CreateDirectory(files);
        var records = Enumerable.Range(0, 5000).Select(i => new AssetRecord
        {
            Path = Path.Combine(files, $"asset {i}.png"), Name = $"Asset {i}", Category = i % 2 == 0 ? AssetCategories.Items : AssetCategories.Creatures,
            Format = "png", Width = 32, Height = 32, Frames = 1, Sha256 = i.ToString("x64"), OriginalId = i.ToString(), FileModifiedUtc = DateTime.UtcNow,
            Tags = i % 10 == 0 ? ["tenth"] : [],
        }).ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        lib.Upsert(records);
        var insert = sw.Elapsed;
        sw.Restart();
        var page = lib.Search(new AssetQuery { Text = "asset 4", Category = AssetCategories.Items, Limit = 200 });
        var tagged = lib.Search(new AssetQuery { Tag = "tenth" });
        var search = sw.Elapsed;
        Assert.Equal(5000, lib.Statistics().Assets);
        Assert.Equal(500, tagged.Total);
        Assert.True(page.Total > 0);
        Assert.True(insert < TimeSpan.FromSeconds(20), $"inserting 5,000 assets took {insert}");
        Assert.True(search < TimeSpan.FromSeconds(2), $"searching took {search}");
    }

    [Fact]
    public void ANewerSchemaIsNotOpened()
    {
        var (lib, _) = NewLibrary();
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={lib.DatabasePath};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE meta SET value = '99' WHERE key = 'schema'";
            cmd.ExecuteNonQuery();
        }
        AssetLibrary.ReleaseConnections();
        Assert.Throws<InvalidDataException>(() => AssetLibrary.Open(lib.Folder));
    }
}
