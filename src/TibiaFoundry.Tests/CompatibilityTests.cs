using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Tibia.Protobuf.Appearances;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Protobuf;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Sounds;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.Tests;

/// <summary>
/// The compatibility layer (audit A06, A09, A10, A17, A18, A22, A23): schema drift detection,
/// classified load errors, data validation, the full checker against real and deliberately
/// altered client folders, settings from newer versions, sprite-folder provenance and client
/// change detection. Synthetic changes simulate plausible future updates; they do not claim to
/// reproduce any real future format.
/// </summary>
public sealed class CompatibilityTests(ITestOutputHelper output)
{
    // ------------------------------------------------------------------ real client

    [Fact]
    public void ClientDataInUseIsReportedAsCompatible()
    {
        TestData.RequireClientAssets();
        var report = CompatibilityChecker.Check(TestData.Client, ct: TestContext.Current.CancellationToken);
        output.WriteLine(report.ToText());
        Assert.NotNull(report.ClientVersion);
        Assert.All(report.Components, c => Assert.True(c.Status <= CompatibilityStatus.Unfamiliar, $"{c.Component}: {c.Status} {c.Summary}"));
        if (report.VersionValidated) Assert.Equal(CompatibilityStatus.Supported, report.Overall);
    }

    [Fact]
    public void RealAppearancesHaveNoUnknownSchemaFields()
    {
        TestData.RequireClientAssets();
        byte[] data = ProtoLoader.ReadShared(TestData.Client.FindAssetFile(AssetTypes.Appearances)!);
        var report = SchemaInspector.Inspect(Appearances.Descriptor, data);
        Assert.True(report.IsExact, string.Join("; ", report.Drift));
    }

    // ------------------------------------------------------------------ schema drift (synthetic)

    private static byte[] SmallAppearances()
    {
        var a = new Appearances();
        a.Object.Add(new Appearance
        {
            Id = 100, Name = "test object",
            FrameGroup = { new FrameGroup { SpriteInfo = new SpriteInfo { PatternWidth = 1, PatternHeight = 1, PatternDepth = 1, Layers = 1, SpriteId = { 0 } } } },
        });
        a.Outfit.Add(new Appearance
        {
            Id = 1,
            FrameGroup = { new FrameGroup { SpriteInfo = new SpriteInfo { PatternWidth = 4, PatternHeight = 1, PatternDepth = 1, Layers = 1, SpriteId = { 0, 1, 2, 3 } } } },
        });
        return a.ToByteArray();
    }

    [Fact]
    public void NewFieldsAddedByAnUpdateAreDetectedAndIgnoredSafely()
    {
        // Simulated update: the client adds field 99 (varint) and field 98 (text) to the root message.
        // Tags are varints: field 99/varint = (99 << 3) | 0 = 792 = 0x98 0x06; field 98/length-delimited = 786 = 0x92 0x06.
        byte[] data = [.. SmallAppearances(), 0x98, 0x06, 42, 0x92, 0x06, 3, (byte)'n', (byte)'e', (byte)'w'];
        var report = SchemaInspector.Inspect(Appearances.Descriptor, data);
        Assert.True(report.HasUnknownFields);
        Assert.Contains(report.Drift, d => d.FieldNumber == 99 && d.Sample == "42");
        Assert.Contains(report.Drift, d => d.FieldNumber == 98 && d.Sample == "'new'");
        // Parsing still works and keeps the known data.
        var parsed = Appearances.Parser.ParseFrom(data);
        Assert.Equal(100u, parsed.Object[0].Id);
    }

    [Fact]
    public void FieldsWhoseTypeChangedAreDetected()
    {
        // Simulated change: field 1 ("object", a message) arrives as a varint. Google.Protobuf
        // silently treats it as unknown, so only the inspector notices that data went missing.
        byte[] data = [.. SmallAppearances(), (1 << 3) | 0, 7];
        var report = SchemaInspector.Inspect(Appearances.Descriptor, data);
        Assert.True(report.HasMismatches);
        Assert.Contains(report.Drift, d => d.Mismatch && d.FieldNumber == 1);
        Assert.Single(Appearances.Parser.ParseFrom(data).Object); // the parser alone would not have complained
    }

    [Fact]
    public void KnownDriftOfValidatedDataIsNotReportedAsNew()
    {
        var drift = new SchemaDrift("tibia.protobuf.staticmapdata.Tile", 101, WireFormat.WireType.LengthDelimited, 3, false);
        Assert.True(drift.IsKnown);
        Assert.False(new SchemaReport([drift], false, null).HasUnknownFields);
    }

    // ------------------------------------------------------------------ classified load errors

    [Fact]
    public void LoadErrorsSayWhatIsWrongWithWhichFile()
    {
        string dir = TestData.NewTempDirectory("loaderrors");
        var missing = Assert.Throws<DataFileException>(() => AppearanceDatabase.Load(Path.Combine(dir, "appearances-x.dat")));
        Assert.Equal(DataProblemKind.Missing, missing.Kind);

        string empty = Path.Combine(dir, "appearances-empty.dat");
        File.WriteAllBytes(empty, Array.Empty<byte>());
        Assert.Equal(DataProblemKind.Undecodable, Assert.Throws<DataFileException>(() => AppearanceDatabase.Load(empty)).Kind);

        string truncated = Path.Combine(dir, "appearances-truncated.dat");
        byte[] good = SmallAppearances();
        File.WriteAllBytes(truncated, good[..(good.Length - 3)]);
        var ex = Assert.Throws<DataFileException>(() => AppearanceDatabase.Load(truncated));
        Assert.Equal(DataProblemKind.Undecodable, ex.Kind);
        Assert.Contains("appearances-truncated.dat", ex.Message);
        Assert.False(string.IsNullOrEmpty(ex.Advice));
    }

    [Fact]
    public void FuzzedDataFilesFailWithDataFileExceptionOnly()
    {
        TestData.RequireClientAssets();
        byte[] original = ProtoLoader.ReadShared(TestData.Client.FindAssetFile(AssetTypes.Map)!);
        string dir = TestData.NewTempDirectory("protofuzz");
        var random = new Random(4242);
        int rejected = 0;
        for (int i = 0; i < 60; i++)
        {
            var data = (byte[])original.Clone();
            for (int k = 0; k < 1 + random.Next(6); k++) data[random.Next(data.Length)] = (byte)random.Next(256);
            if (random.Next(3) == 0) data = data[..random.Next(1, data.Length)];
            string file = Path.Combine(dir, $"map-{i}.dat");
            File.WriteAllBytes(file, data);
            try
            {
                var db = Core.Map.MapDatabase.Load(file);
                DataValidation.Map(db, null); // validation must also cope with whatever was decoded
            }
            catch (DataFileException) { rejected++; }
        }
        output.WriteLine($"{rejected} of 60 corrupted map files were rejected; the rest decoded (protobuf tolerates many changes).");
    }

    // ------------------------------------------------------------------ validation

    [Fact]
    public void ValidationFlagsStructurallyBrokenAppearances()
    {
        var data = Appearances.Parser.ParseFrom(SmallAppearances());
        data.Object.Add(new Appearance { Id = 101 });                                                     // no frame groups
        data.Object.Add(new Appearance { Id = 102, FrameGroup = { new FrameGroup() } });                  // no sprite info
        data.Object.Add(new Appearance { Id = 103, FrameGroup = { new FrameGroup { SpriteInfo = new SpriteInfo { Layers = 2, SpriteId = { 1 } } } } }); // count mismatch
        data.Object.Add(new Appearance { Id = 100, FrameGroup = { new FrameGroup { SpriteInfo = new SpriteInfo { SpriteId = { 999_999_999 } } } } }); // duplicate + unknown sprite
        var catalog = TestCatalog();
        var result = DataValidation.Appearances(new AppearanceDatabase(data), catalog);
        foreach (string line in result.Describe()) output.WriteLine(line);
        Assert.Null(result.Fatal);
        Assert.Equal(1, result.IssueCounts["appearances without frame groups"]);
        Assert.Equal(1, result.IssueCounts["frame groups without sprite info"]);
        Assert.Equal(1, result.IssueCounts["sprite count differs from layers × patterns × phases"]);
        Assert.Equal(1, result.IssueCounts["duplicate IDs"]);
        Assert.Equal(1, result.IssueCounts["sprite IDs not in the sprite catalog"]);
        Assert.Contains((AppearanceKind.Object, 102u), result.InvalidAppearances);

        Assert.NotNull(DataValidation.Appearances(new AppearanceDatabase(new Appearances()), catalog).Fatal);
    }

    private static AssetCatalog TestCatalog()
    {
        string dir = TestData.NewTempDirectory("vcatalog");
        File.WriteAllText(Path.Combine(dir, TibiaFormat.AssetCatalogFile),
            """[{"type":"sprite","file":"s.bmp.lzma","spritetype":0,"firstspriteid":0,"lastspriteid":143}]""");
        return AssetCatalog.Load(dir);
    }

    [Fact]
    public void RenderersFailClearlyOnStructurallyBrokenData()
    {
        var data = new Appearances();
        data.Outfit.Add(new Appearance { Id = 7, FrameGroup = { new FrameGroup() } });
        var db = new AppearanceDatabase(data);
        var sprites = new SpriteRepository(null, null, SpriteSourceMode.ClientOnly);
        var ex = Assert.Throws<OutfitRenderException>(() => OutfitRenderer.Render(db, sprites, 7, new OutfitRenderOptions()));
        Assert.Contains("no sprite information", ex.Message);
        Assert.Throws<InvalidOperationException>(() => AppearanceGifBuilder.Build(data.Outfit[0], AppearanceKind.Outfit, new GifExportOptions(), sprites));
        Assert.Throws<InvalidOperationException>(() => SheetComposer.OutfitSheet(data.Outfit[0], sprites, true));
        Assert.Equal(0, AppearanceGifBuilder.PatternCount(data.Outfit[0]));
    }

    // ------------------------------------------------------------------ full checker on altered client folders

    /// <summary>A package folder built from copies of the real client files (the client itself is never touched); deleted on dispose.</summary>
    private sealed class ClientCopy : IDisposable
    {
        public ClientCopy(string version)
        {
            string realAssets = TestData.RequireClientAssets();
            var real = AssetCatalog.Load(realAssets);
            Package = TestData.NewTempDirectory("pkg");
            Assets = Path.Combine(Package, TibiaFormat.AssetsFolder);
            Directory.CreateDirectory(Assets);
            File.WriteAllText(Path.Combine(Package, TibiaFormat.PackageManifest), $$"""{ "version": "{{version}}", "files": [] }""");
            var entries = new JsonArray();
            foreach (var sheet in real.Sheets)
            {
                File.Copy(real.GetSheetPath(sheet), Path.Combine(Assets, sheet.FileName));
                entries.Add(new JsonObject { ["type"] = "sprite", ["file"] = sheet.FileName, ["spritetype"] = (int)sheet.Type, ["firstspriteid"] = sheet.FirstSpriteId, ["lastspriteid"] = sheet.LastSpriteId });
            }
            foreach (string type in new[] { TibiaFormat.CatalogTypeAppearances, TibiaFormat.CatalogTypeStaticData, TibiaFormat.CatalogTypeStaticMapData, TibiaFormat.CatalogTypeMap })
            {
                string file = real.Files[type];
                File.Copy(Path.Combine(realAssets, file), Path.Combine(Assets, file));
                entries.Add(new JsonObject { ["type"] = type, ["file"] = file });
            }
            // Map image tiles referenced by map.dat.
            foreach (string tile in Directory.EnumerateFiles(realAssets, "*.bmp.lzma").Where(f => !Path.GetFileName(f).StartsWith("sprites-", StringComparison.Ordinal)))
                File.Copy(tile, Path.Combine(Assets, Path.GetFileName(tile)));
            File.WriteAllText(Path.Combine(Assets, TibiaFormat.AssetCatalogFile), entries.ToJsonString());
            Client = new TibiaClientPaths(Package);
        }

        public string Package { get; }
        public string Assets { get; }
        public TibiaClientPaths Client { get; }

        public void Dispose()
        {
            try { Directory.Delete(Package, recursive: true); }
            catch (IOException) { /* temp folder; removed with the test temp directory */ }
        }
    }

    [Fact]
    public void UnknownClientVersionWithValidDataIsUnfamiliarButCompatible()
    {
        using var copy = new ClientCopy("99.0.synthetic");
        var report = CompatibilityChecker.Check(copy.Client, ct: TestContext.Current.CancellationToken);
        output.WriteLine(report.ToText());
        Assert.False(report.VersionValidated);
        var data = report.Components.Where(c => c.Component is not CompatibilityChecker.PackageComponent && c.Component != TibiaFormat.CatalogTypeSounds).ToList();
        Assert.All(data, c => Assert.Equal(CompatibilityStatus.Unfamiliar, c.Status));
        // No sounds folder in the copy: reported as missing, and only Sounds is affected.
        var sounds = report.Components.Single(c => c.Component == TibiaFormat.CatalogTypeSounds);
        Assert.Equal(CompatibilityStatus.Missing, sounds.Status);
        Assert.Contains(sounds.AffectedFeatures, f => f.Contains("Sounds"));
    }

    [Fact]
    public void DamagedAndChangedFilesAreIsolatedToTheirFeatures()
    {
        using var copy = new ClientCopy("99.0.synthetic");
        string assets = copy.Assets;
        // 1. appearances: one flipped byte (the name still carries the original checksum).
        string appearances = Directory.GetFiles(assets, "appearances-*.dat").Single();
        var bytes = File.ReadAllBytes(appearances);
        bytes[bytes.Length / 2] ^= 0x01;
        File.WriteAllBytes(appearances, bytes);
        // 2. staticmapdata: removed (incomplete update).
        File.Delete(Directory.GetFiles(assets, "staticmapdata-*.dat").Single());
        // 3. catalog: one entry with a sprite type this version does not know.
        string catalogPath = Path.Combine(assets, TibiaFormat.AssetCatalogFile);
        var catalog = JsonNode.Parse(File.ReadAllText(catalogPath))!.AsArray();
        catalog.Add(new JsonObject { ["type"] = "sprite", ["file"] = "future.bmp.lzma", ["spritetype"] = 9, ["firstspriteid"] = 900_000, ["lastspriteid"] = 900_010 });
        File.WriteAllText(catalogPath, catalog.ToJsonString());
        // 4. map: replaced by bytes that are not protobuf, under a name without checksum.
        string map = Directory.GetFiles(assets, "map-*.dat").Single();
        File.Delete(map);
        File.WriteAllBytes(Path.Combine(assets, "map-new.dat"), [0xFF, 0xFF, 0xFF, 0xFF, 0x0F, 0x01]);
        File.WriteAllText(catalogPath, File.ReadAllText(catalogPath).Replace(Path.GetFileName(map), "map-new.dat"));

        var report = CompatibilityChecker.Check(copy.Client, ct: TestContext.Current.CancellationToken);
        output.WriteLine(report.ToText());
        CompatibilityStatus StatusOf(string component) => report.Components.Single(c => c.Component == component).Status;
        Assert.Equal(CompatibilityStatus.Unsupported, StatusOf(TibiaFormat.CatalogTypeAppearances));
        Assert.Contains("checksum", report.Components.Single(c => c.Component == TibiaFormat.CatalogTypeAppearances).Summary);
        Assert.Equal(CompatibilityStatus.Missing, StatusOf(TibiaFormat.CatalogTypeStaticMapData));
        Assert.Equal(CompatibilityStatus.Partial, StatusOf(CompatibilityChecker.SpriteCatalogComponent));
        Assert.Equal(CompatibilityStatus.Unsupported, StatusOf(TibiaFormat.CatalogTypeMap));
        Assert.True(StatusOf(TibiaFormat.CatalogTypeStaticData) <= CompatibilityStatus.Unfamiliar, "an unrelated file stays usable");
        Assert.Equal(CompatibilityStatus.Unsupported, report.Overall);
    }

    [Fact]
    public void MissingPackageIsReportedNotThrown()
    {
        var report = CompatibilityChecker.Check(new TibiaClientPaths(Path.Combine(TestData.NewTempDirectory("none"), "nothing")), ct: TestContext.Current.CancellationToken);
        Assert.Equal(CompatibilityStatus.Missing, report.Components[0].Status);
        Assert.All(report.Components, c => Assert.True(c.Status >= CompatibilityStatus.Missing));
    }

    [Fact]
    public void ContentHashIsReadFromClientFileNames()
    {
        string hash = new('a', 64);
        Assert.Equal(hash, CompatibilityChecker.ContentHashFromName($"appearances-{hash}.dat"));
        Assert.Equal(hash, CompatibilityChecker.ContentHashFromName($"sprites-{hash}.bmp"));
        Assert.Null(CompatibilityChecker.ContentHashFromName("appearances-1234.dat"));
        Assert.Null(CompatibilityChecker.ContentHashFromName("appearances.dat"));
    }

    // ------------------------------------------------------------------ settings across versions

    [Fact]
    public void SettingsFromANewerVersionKeepUnknownOptions()
    {
        string file = Path.Combine(TestData.NewTempDirectory("newersettings"), "settings.json");
        File.WriteAllText(file, """{ "Version": 99, "Theme": "Dark", "FutureOption": { "enabled": true, "level": 5 }, "AnotherNewThing": [1, 2] }""");
        var store = new SettingsStore(file);
        var (settings, status, backup) = store.Load();
        Assert.Equal(SettingsLoadStatus.LoadedFromNewerVersion, status);
        Assert.True(File.Exists(backup));
        Assert.Equal(ThemePreference.Dark, settings.Theme);

        settings.CreatorName = "changed by the older version";
        store.Save(settings);
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal(99, doc.RootElement.GetProperty("Version").GetInt32());                      // never downgraded
        Assert.Equal(5, doc.RootElement.GetProperty("FutureOption").GetProperty("level").GetInt32()); // kept verbatim
        Assert.Equal(2, doc.RootElement.GetProperty("AnotherNewThing").GetArrayLength());
        Assert.Equal("changed by the older version", doc.RootElement.GetProperty("CreatorName").GetString());
    }

    // ------------------------------------------------------------------ sprite folder provenance

    [Fact]
    public void SpriteFoldersFromAnotherClientVersionAreNotTrusted()
    {
        string assets = TestData.RequireClientAssets();
        var catalog = AssetCatalog.Load(assets);
        var sheet = catalog.Sheets.First(s => s.Type == SpriteSheetType.Small);
        uint id = (uint)sheet.FirstSpriteId + 5;
        var clientSprite = new SpriteRepository(catalog, null, SpriteSourceMode.ClientOnly).GetSprite(id)!;

        string folder = TestData.NewTempDirectory("stalefolder");
        var fake = new RgbaImage(32, 32);
        fake.Clear(0xFFFF00FF);
        ImageIO.SavePng(fake, Path.Combine(folder, id + ".png"));

        // No manifest (1.x folder): same size, so it is used, but provenance is unknown.
        var unknown = new SpriteRepository(catalog, folder, SpriteSourceMode.FolderThenClient);
        Assert.Equal(SpriteFolderState.Unknown, unknown.FolderState);
        Assert.True(fake.PixelEquals(unknown.GetSprite(id)!));

        // Manifest of another sprite catalog: the client's sprite wins and the user is told.
        new SpriteFolderManifest("TibiaFoundry", "14.00.old", new string('0', 64), DateTime.UtcNow, 1).Write(folder);
        var stale = new SpriteRepository(catalog, folder, SpriteSourceMode.FolderThenClient);
        Assert.Equal(SpriteFolderState.Stale, stale.FolderState);
        Assert.NotNull(stale.FolderNotice);
        Assert.True(clientSprite.PixelEquals(stale.GetSprite(id)!));

        // "Extracted folder only" still honors the user's choice, with a warning.
        var folderOnly = new SpriteRepository(catalog, folder, SpriteSourceMode.FolderOnly);
        Assert.True(fake.PixelEquals(folderOnly.GetSprite(id)!));
        Assert.Contains("outdated", folderOnly.FolderNotice);

        // Manifest of this catalog: trusted.
        new SpriteFolderManifest("TibiaFoundry", "current", catalog.ContentSha256, DateTime.UtcNow, 1).Write(folder);
        Assert.Equal(SpriteFolderState.Current, new SpriteRepository(catalog, folder).FolderState);
    }

    [Fact]
    public void FolderSpritesOfTheWrongSizeAreReplacedByClientSprites()
    {
        string assets = TestData.RequireClientAssets();
        var catalog = AssetCatalog.Load(assets);
        var sheet = catalog.Sheets.First(s => s.Type == SpriteSheetType.Large);
        uint id = (uint)sheet.FirstSpriteId;
        string folder = TestData.NewTempDirectory("wrongsize");
        ImageIO.SavePng(new RgbaImage(32, 32), Path.Combine(folder, id + ".png"));
        var repo = new SpriteRepository(catalog, folder);
        var sprite = repo.GetSprite(id)!;
        Assert.Equal(64, sprite.Width);
        Assert.Equal(1, repo.SizeMismatches);
    }

    [Fact]
    public async Task ExtractorRecordsProvenanceAndRefreshesStaleFolders()
    {
        string assets = TestData.RequireClientAssets();
        var catalog = AssetCatalog.Load(assets);
        var sheets = new[] { catalog.Sheets[0] };
        string folder = TestData.NewTempDirectory("provenance");
        var options = new SpriteExtractionOptions { OutputDirectory = folder, ClientVersion = "test-version", SkipExisting = true };
        var first = await SpriteExtractor.ExtractAsync(catalog, sheets, options, null, TestContext.Current.CancellationToken);
        var manifest = SpriteFolderManifest.Read(folder)!;
        Assert.Equal(catalog.ContentSha256, manifest.CatalogSha256);
        Assert.Equal("test-version", manifest.ClientVersion);
        Assert.Equal(sheets[0].SpriteCount, first.Succeeded);

        // Pretend the folder came from another client: "skip existing" must not keep outdated files.
        new SpriteFolderManifest("TibiaFoundry", "old", new string('1', 64), DateTime.UtcNow, 1).Write(folder);
        var second = await SpriteExtractor.ExtractAsync(catalog, sheets, options, null, TestContext.Current.CancellationToken);
        Assert.Equal(sheets[0].SpriteCount, second.Succeeded);
        Assert.Equal(0, second.Skipped);
        Assert.Single(second.Notes);
        Assert.Equal(catalog.ContentSha256, SpriteFolderManifest.Read(folder)!.CatalogSha256);
    }

    // ------------------------------------------------------------------ sounds: unsafe names and stale caches

    [Fact]
    public async Task SoundFileNamesFromDataCannotEscapeTheirFolders()
    {
        string sounds = TestData.NewTempDirectory("soundsdir");
        File.WriteAllText(Path.Combine(sounds, "ok.ogg"), "ogg");
        var data = new Tibia.Protobuf.Sound.Sounds();
        data.Sound.Add(new Tibia.Protobuf.Sound.Sound { Id = 1, Filename = "ok.ogg" });
        data.Sound.Add(new Tibia.Protobuf.Sound.Sound { Id = 2, Filename = @"..\..\secret.ogg" });
        data.Sound.Add(new Tibia.Protobuf.Sound.Sound { Id = 3, Filename = "missing.ogg" });
        var db = new SoundDatabase(data, sounds);
        Assert.Null(db.ResolvePlayableFile(2, null));

        string output = TestData.NewTempDirectory("soundsout");
        var result = await db.ExtractAsync(output, null, TestContext.Current.CancellationToken);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(2, result.Failures.Count);            // unsafe name and missing file are failures, not "skipped"
        Assert.Contains(result.Failures, f => f.Contains("unsafe"));
        Assert.Contains(result.Failures, f => f.Contains("missing"));
        Assert.Equal(new[] { "1.ogg" }, Directory.GetFiles(output).Select(Path.GetFileName));
        var validation = DataValidation.Sounds(db);
        Assert.Equal(1, validation.IssueCounts["sounds with unsafe or empty file names"]);
        Assert.Equal(1, validation.IssueCounts["sound files missing on disk"]);
    }

    [Fact]
    public void DecodedSoundCacheFollowsTheClientFileNotTheSoundId()
    {
        // Audit A11: after an update the same sound ID can point to another (content-hashed) file.
        // Compressed client sounds are decoded to a temp cache, which must be keyed by that file name.
        // Two real CIP LZMA containers stand in for the compressed sounds (their content is irrelevant).
        string assets = TestData.RequireClientAssets();
        var catalog = AssetCatalog.Load(assets);
        string tag = Guid.NewGuid().ToString("N")[..8];
        string oldDir = TestData.NewTempDirectory("sounds_old"), newDir = TestData.NewTempDirectory("sounds_new");
        string oldName = $"sound-{tag}a.ogg", newName = $"sound-{tag}b.ogg";
        File.Copy(catalog.GetSheetPath(catalog.Sheets[0]), Path.Combine(oldDir, oldName + ".lzma"));
        File.Copy(catalog.GetSheetPath(catalog.Sheets[1]), Path.Combine(newDir, newName + ".lzma"));
        SoundDatabase Client(string dir, string file)
        {
            var data = new Tibia.Protobuf.Sound.Sounds();
            data.Sound.Add(new Tibia.Protobuf.Sound.Sound { Id = 5, Filename = file });
            return new SoundDatabase(data, dir);
        }

        string? before = Client(oldDir, oldName).ResolvePlayableFile(5, null);
        string? after = Client(newDir, newName).ResolvePlayableFile(5, null);
        try
        {
            Assert.NotNull(before);
            Assert.NotNull(after);
            Assert.NotEqual(before, after);
            Assert.Equal(CipLzma.DecompressFile(Path.Combine(newDir, newName + ".lzma")), File.ReadAllBytes(after!));
        }
        finally
        {
            foreach (string? f in new[] { before, after }) if (f != null && File.Exists(f)) File.Delete(f);
        }
    }

    // ------------------------------------------------------------------ client updates while running

    [Fact]
    public async Task ClientUpdateWhileRunningDiscardsStaleData()
    {
        string realAssets = TestData.RequireClientAssets();
        string assets = TestData.NewTempDirectory("liveassets");
        var real = AssetCatalog.Load(realAssets);
        string appearancesFile = real.Files[TibiaFormat.CatalogTypeAppearances];
        File.Copy(Path.Combine(realAssets, appearancesFile), Path.Combine(assets, appearancesFile));
        File.WriteAllText(Path.Combine(assets, TibiaFormat.AssetCatalogFile), $$"""[{"type":"appearances","file":"{{appearancesFile}}"}]""");
        var settings = new AppSettings { AssetsFolderOverride = assets, LoadDataAutomatically = false };
        settings.Normalize();
        using var data = new ClientDataService(settings);

        // Two pages asking at the same time share one load.
        string path = data.DefaultFile(TibiaFormat.CatalogTypeAppearances)!;
        var a = data.LoadAppearancesAsync(path);
        var b = data.LoadAppearancesAsync(path);
        Assert.Same(a, b);
        await a;
        Assert.NotNull(data.Appearances);
        Assert.Null(data.CheckForClientChange());

        // The launcher rewrites the catalog (update): loaded data are discarded and the user is told.
        File.WriteAllText(Path.Combine(assets, TibiaFormat.AssetCatalogFile), $$"""[ {"type":"appearances","file":"{{appearancesFile}}"} ]""");
        File.SetLastWriteTimeUtc(Path.Combine(assets, TibiaFormat.AssetCatalogFile), DateTime.UtcNow.AddMinutes(1));
        string? message = data.CheckForClientChange();
        Assert.NotNull(message);
        Assert.Null(data.Appearances);
        Assert.Null(data.CheckForClientChange());

        // A damaged catalog after an update never throws from the Catalog property.
        File.WriteAllText(Path.Combine(assets, TibiaFormat.AssetCatalogFile), "{ broken");
        File.SetLastWriteTimeUtc(Path.Combine(assets, TibiaFormat.AssetCatalogFile), DateTime.UtcNow.AddMinutes(2));
        Assert.NotNull(data.CheckForClientChange());
        Assert.Null(data.Catalog);
        Assert.NotNull(data.CatalogError);
    }
}
