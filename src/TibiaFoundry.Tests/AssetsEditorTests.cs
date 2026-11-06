using Google.Protobuf;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Editing;
using TibiaFoundry.Core.Editing.Model;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;

namespace TibiaFoundry.Tests;

/// <summary>The Assets Editor's project model: faithful round trips, object and sprite edits, validated saves and object packages.</summary>
public class AssetsEditorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void ClientAppearancesRoundTripByteForByte()
    {
        string assets = TestData.RequireClientAssets();
        string file = Path.Combine(assets, AssetCatalog.Load(assets).Files["appearances"]);
        byte[] original = File.ReadAllBytes(file);
        var parsed = EditableAppearances.Parser.ParseFrom(original);
        Assert.Equal(original, parsed.ToByteArray());
        // Latin-1 text stays Latin-1 (the viewer's UTF-8 schema would rewrite it): the client has such descriptions.
        Assert.Contains(parsed.Object, a => a.DescriptionText().Contains('î'));
    }

    [Fact]
    public void ClientCatalogRoundTripsByteForByte()
    {
        string assets = TestData.RequireClientAssets();
        byte[] original = File.ReadAllBytes(Path.Combine(assets, "catalog-content.json"));
        Assert.Equal(original, CatalogWriter.Write(CatalogWriter.Read(original)));
    }

    private static (string Root, AssetsProject Project) Open()
    {
        string root = TestData.NewTempDirectory("editor");
        string assets = SyntheticAssets.Create(Path.Combine(root, "assets"));
        return (root, AssetsProject.Open(assets));
    }

    private static OutputSession Session(BackupStore? store = null) =>
        new("save", FileSafetyOptions.Default with { WhenExists = ExistingFilePolicy.Overwrite }, store);

    [Fact]
    public void ObjectsCanBeCreatedDuplicatedRenumberedAndDeleted()
    {
        var (_, p) = Open();
        var created = p.CreateObject(AppearanceKind.Object);
        Assert.Equal(104u, created.Id);
        Assert.True(p.IsNewSprite(created.PreviewSprite()!.Value));
        Assert.True(p.GetSprite(created.PreviewSprite()!.Value)!.Pixels.All(px => px >> 24 == 0));
        var copy = p.Duplicate(AppearanceKind.Object, 101);
        Assert.Equal(105u, copy.Id);
        Assert.Equal(p.Find(AppearanceKind.Object, 101)!.FrameGroup, copy.FrameGroup);
        Assert.Equal("Object 101 already exists.", p.ChangeId(AppearanceKind.Object, 105, 101));
        Assert.Equal("Object IDs start at 100.", p.ChangeId(AppearanceKind.Object, 105, 50));
        Assert.Null(p.ChangeId(AppearanceKind.Object, 105, 102));
        Assert.Equal([100u, 101u, 102u, 103u, 104u], p.List(AppearanceKind.Object).Select(a => a.Id));
        Assert.True(p.Delete(AppearanceKind.Object, 104));
        Assert.True(p.IsModified);
    }

    [Fact]
    public void SpritesMustHaveAClientSize()
    {
        var (_, p) = Open();
        Assert.Throws<ArgumentException>(() => p.AddSprite(new RgbaImage(40, 40)));
        Assert.Throws<ArgumentException>(() => p.ReplaceSprite(18, new RgbaImage(32, 32)));
        Assert.Equal((64, 32), p.SpriteSize(14));
        Assert.Null(p.SpriteSize(999));
    }

    [Fact]
    public void SaveAsWritesAValidatedCompleteFolder()
    {
        var (root, p) = Open();
        string original = p.Folder;
        var outfit = p.Find(AppearanceKind.Outfit, 1)!;
        outfit.Flags.Translucent = true;
        p.MarkModified(AppearanceKind.Outfit, 1);
        var big = SyntheticAssets.Sprite(77, 64, 64);
        uint newBig = p.AddSprite(big);
        var small = SyntheticAssets.Sprite(78, 32, 32);
        uint newSmall = p.AddSprite(small);
        var item = p.Duplicate(AppearanceKind.Object, 101);
        item.FrameGroup[0].SpriteInfo.SpriteId[0] = newBig;
        item.Name = EditableAppearanceExtensions.EncodeClientText("Prüfstück");
        var replacement = SyntheticAssets.Sprite(99, 32, 32);
        p.ReplaceSprite(5, replacement);

        string target = Path.Combine(root, "saved");
        using (var session = Session())
        {
            var result = p.Save(target, session, ct: Ct);
            Assert.NotNull(result.Validation);
            Assert.Equal(3, result.SheetsWritten.Count); // one re-encoded, two new (64×64 and 32×32)
            Assert.True(result.FilesCopied > 0);
        }
        Assert.Equal(target, p.Folder);
        Assert.False(p.IsModified);

        var reopened = AssetsProject.Open(target);
        Assert.Empty(reopened.Catalog.Problems);
        var savedItem = reopened.Find(AppearanceKind.Object, item.Id)!;
        Assert.Equal("Prüfstück", savedItem.NameText());
        uint finalBig = savedItem.FrameGroup[0].SpriteInfo.SpriteId[0];
        // New IDs follow the highest existing one (21), grouped by size: the 32×32 sprite gets 22, the 64×64 one 23.
        Assert.Equal(23u, finalBig);
        Assert.True(Normalized(big).PixelEquals(reopened.GetSprite(finalBig)!));
        Assert.True(Normalized(replacement).PixelEquals(reopened.GetSprite(5)!));
        Assert.True(reopened.Find(AppearanceKind.Outfit, 1)!.Flags.Translucent);
        // Untouched objects are byte-identical, and the source folder is unchanged.
        Assert.Equal(AssetsProject.Open(original).Find(AppearanceKind.Object, 100)!.ToByteArray(), reopened.Find(AppearanceKind.Object, 100)!.ToByteArray());
        Assert.False(AssetsProject.Open(original).IsModified);
        Assert.Null(AssetsProject.Open(original).Find(AppearanceKind.Object, item.Id));
    }

    private static RgbaImage Normalized(RgbaImage image)
    {
        var copy = image.Clone();
        for (int i = 0; i < copy.Pixels.Length; i++) if (copy.Pixels[i] >> 24 == 0) copy.Pixels[i] = 0x00FF00FF;
        return copy;
    }

    [Fact]
    public void SavingInPlaceBacksUpTheCatalogAndKeepsOldFiles()
    {
        var (root, p) = Open();
        string folder = p.Folder;
        string oldAppearances = p.AppearancesFile;
        p.Find(AppearanceKind.Object, 100)!.Flags.Bottom = true;
        p.MarkModified(AppearanceKind.Object, 100);
        var store = new BackupStore(Path.Combine(root, "backups"));
        using (var session = Session(store)) p.Save(folder, session, ct: Ct);
        Assert.NotEqual(oldAppearances, p.AppearancesFile);
        Assert.True(File.Exists(Path.Combine(folder, oldAppearances)));
        var set = Assert.Single(store.List());
        Assert.EndsWith("catalog-content.json", set.Manifest.Entries.Single().Original);
        Assert.True(AssetsProject.Open(folder).Find(AppearanceKind.Object, 100)!.Flags.Bottom);
    }

    [Fact]
    public void SaveRefusesUnsafeTargets()
    {
        var (root, p) = Open();
        Assert.Throws<InvalidOperationException>(() => p.Save(Path.Combine(p.Folder, "inside"), Session(), ct: Ct));
        string nonEmpty = Path.Combine(root, "busy");
        Directory.CreateDirectory(nonEmpty);
        File.WriteAllText(Path.Combine(nonEmpty, "x.txt"), "x");
        Assert.Throws<InvalidOperationException>(() => p.Save(nonEmpty, Session(), ct: Ct));
        Assert.True(AssetsProject.IsInsideClientInstallation(Path.Combine(TibiaClientPaths.PackagesRoot, "Tibia", "assets")) || !Directory.Exists(TibiaClientPaths.PackagesRoot));
    }

    [Fact]
    public void ACancelledSaveAsLeavesNoPartialCopyAndTheFolderCanBeUsedAgain()
    {
        var (root, p) = Open();
        p.Find(AppearanceKind.Object, 100)!.Flags.Bottom = true;
        p.MarkModified(AppearanceKind.Object, 100);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        string fresh = Path.Combine(root, "new-copy");   // created by the save: removed again
        Assert.ThrowsAny<OperationCanceledException>(() => p.Save(fresh, Session(), ct: cancelled.Token));
        Assert.False(Directory.Exists(fresh));

        string empty = Path.Combine(root, "empty-copy");   // chosen empty: kept, and empty again
        Directory.CreateDirectory(empty);
        Assert.ThrowsAny<OperationCanceledException>(() => p.Save(empty, Session(), ct: cancelled.Token));
        Assert.True(Directory.Exists(empty));
        Assert.Empty(Directory.EnumerateFileSystemEntries(empty));

        p.Save(empty, Session(), ct: Ct);
        Assert.True(AssetsProject.Open(empty).Find(AppearanceKind.Object, 100)!.Flags.Bottom);
    }

    /// <summary>
    /// Saving in place is refused for an installed client, before anything is written. A temporary
    /// folder stands in for the client (<see cref="AssetsProject.ExtraClientRootForTests"/>).
    /// </summary>
    [Fact]
    public void SavingIntoTheInstalledClientIsRefusedBeforeAnythingIsWritten()
    {
        var (root, p) = Open();
        AssetsProject.ExtraClientRootForTests = root;
        try
        {
            Assert.True(p.IsClientInstallation);
            p.Duplicate(AppearanceKind.Object, 101);
            var before = Directory.EnumerateFiles(p.Folder, "*", SearchOption.AllDirectories)
                .ToDictionary(f => f, f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));
            var error = Assert.Throws<InvalidOperationException>(() => p.Save(p.Folder, Session(), ct: Ct));
            Assert.Contains("installed Tibia client", error.Message);
            var after = Directory.EnumerateFiles(p.Folder, "*", SearchOption.AllDirectories)
                .ToDictionary(f => f, f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));
            Assert.Equal(before, after);
            Assert.True(p.IsModified);                                                        // the edit is still there to save elsewhere
        }
        finally
        {
            AssetsProject.ExtraClientRootForTests = null;
        }
    }

    [Fact]
    public void ObjectPackagesRoundTripAndShareSprites()
    {
        var (root, p) = Open();
        byte[] package = AecPackage.Export(p, [(AppearanceKind.Object, 101), (AppearanceKind.Outfit, 1)]);
        // Field 7 carries one PNG per sprite reference (3 for the object, 6 for the outfit).
        var (_, p2) = Open();
        int objectsBefore = p2.List(AppearanceKind.Object).Count;
        var result = AecPackage.Import(p2, package, keepIds: true);
        Assert.Empty(result.Problems);
        Assert.Equal(2, result.Imported.Count);
        Assert.Equal(objectsBefore + 1, p2.List(AppearanceKind.Object).Count);
        Assert.Equal(104u, result.Imported.Single(i => i.Kind == AppearanceKind.Object).NewId); // 101 is taken
        Assert.Equal(4, result.SpritesAdded);   // 18, 19, 20 and 21; repeats reused
        Assert.True(result.SpritesReused > 0);
        var imported = p2.Find(AppearanceKind.Object, 104)!;
        Assert.True(p2.GetSprite(imported.FrameGroup[0].SpriteInfo.SpriteId[1])!.PixelEquals(p2.GetSprite(19)!));
        // The sprite images (field 7) never end up in the client's appearances file.
        Assert.DoesNotContain(7, TopLevelFields(imported.ToByteArray()));
        using (var session = Session()) p2.Save(Path.Combine(root, "with-import"), session, ct: Ct);
        var reopened = AssetsProject.Open(Path.Combine(root, "with-import"));
        Assert.DoesNotContain(7, TopLevelFields(reopened.Find(AppearanceKind.Object, 104)!.ToByteArray()));
        Assert.Empty(reopened.MissingSprites());
    }

    private static List<int> TopLevelFields(byte[] message)
    {
        var fields = new List<int>();
        var input = new CodedInputStream(message);
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            fields.Add(WireFormat.GetTagFieldNumber(tag));
            input.SkipLastField();
        }
        return fields;
    }

    [Fact]
    public void RevertRestoresTheSavedObject()
    {
        var (_, p) = Open();
        p.Find(AppearanceKind.Object, 100)!.Flags.Bank.Waypoints = 999;
        p.MarkModified(AppearanceKind.Object, 100);
        Assert.True(p.Revert(AppearanceKind.Object, 100));
        Assert.Equal(150u, p.Find(AppearanceKind.Object, 100)!.Flags.Bank.Waypoints);
        Assert.False(p.IsObjectModified(AppearanceKind.Object, 100));
    }

    [Fact]
    public void EditedAppearancesRenderWithTheViewersTypes()
    {
        var (_, p) = Open();
        var viewer = p.Find(AppearanceKind.Object, 103)!.ToViewerAppearance();
        Assert.Equal("djinn maître recipe book of the long names", viewer.Name);
        Assert.Null(EditableAppearanceExtensions.EncodeClientText("emoji 🙂"));
    }
}
