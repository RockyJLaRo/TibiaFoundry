using System.Buffers.Binary;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Legacy;

namespace TibiaFoundry.Tests;

/// <summary>Legacy Tibia.dat/Tibia.spr formats and the Client Converter.</summary>
public class LegacyAndConverterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(LegacyDatLayout.V1010, LegacyAttribute.NoMoveAnimation, 16)]
    [InlineData(LegacyDatLayout.V1010, LegacyAttribute.Pickupable, 17)]
    [InlineData(LegacyDatLayout.V1010, LegacyAttribute.Market, 34)]
    [InlineData(LegacyDatLayout.V1010, LegacyAttribute.DefaultAction, 35)]
    [InlineData(LegacyDatLayout.V1010, LegacyAttribute.TopEffect, 38)]
    [InlineData(LegacyDatLayout.V1010, LegacyAttribute.Usable, 0xFE)]
    [InlineData(LegacyDatLayout.V1010, LegacyAttribute.ExpireStop, 51)]
    [InlineData(LegacyDatLayout.V860, LegacyAttribute.Pickupable, 16)]
    [InlineData(LegacyDatLayout.V860, LegacyAttribute.Market, 33)]
    [InlineData(LegacyDatLayout.V780, LegacyAttribute.Chargeable, 8)]
    [InlineData(LegacyDatLayout.V780, LegacyAttribute.Writable, 9)]
    [InlineData(LegacyDatLayout.V755, LegacyAttribute.FloorChange, 23)]
    [InlineData(LegacyDatLayout.V740, LegacyAttribute.MultiUse, 5)]
    [InlineData(LegacyDatLayout.V740, LegacyAttribute.Light, 16)]
    public void AttributeBytesFollowTheClientLayouts(LegacyDatLayout layout, LegacyAttribute attribute, int expected)
    {
        Assert.Equal((byte)expected, LegacyAttributes.ToByte(layout, attribute));
        Assert.Equal(attribute, LegacyAttributes.FromByte(layout, (byte)expected));
    }

    [Fact]
    public void LayoutsCannotStoreAttributesTheyDoNotHave()
    {
        Assert.Null(LegacyAttributes.ToByte(LegacyDatLayout.V860, LegacyAttribute.TopEffect));
        Assert.Null(LegacyAttributes.ToByte(LegacyDatLayout.V755, LegacyAttribute.Market));
        Assert.Equal(LegacyPayload.None, LegacyAttributes.PayloadOf(LegacyAttribute.Displacement, LegacyDatLayout.V740));
        Assert.Equal(LegacyPayload.TwoU16, LegacyAttributes.PayloadOf(LegacyAttribute.Displacement));
    }

    private static LegacyDatFile SampleDat()
    {
        var dat = new LegacyDatFile();
        var item = new LegacyThing(LegacyCategory.Item, 100);
        item.Attributes.Add(new(LegacyAttribute.Ground, 120));
        item.Attributes.Add(new(LegacyAttribute.Light, 3, 215));
        item.Attributes.Add(new(LegacyAttribute.Market, Market: new LegacyMarket(9, 100, 100, System.Text.Encoding.Latin1.GetBytes("maître"), 3, 8)));
        item.FrameGroups.Add(new LegacyFrameGroup { Width = 2, Height = 2, ExactSize = 64, Phases = 2, Animation = new LegacyAnimation { Synchronized = true, LoopCount = -1, StartPhase = -1, Durations = [(100, 100), (200, 300)] }, Sprites = [1, 2, 3, 4, 5, 6, 7, 8] });
        dat.Items.Add(item);
        var outfit = new LegacyThing(LegacyCategory.Outfit, 1);
        outfit.FrameGroups.Add(new LegacyFrameGroup { Type = 0, Layers = 2, Sprites = [9, 10] });
        outfit.FrameGroups.Add(new LegacyFrameGroup { Type = 1, Layers = 2, Phases = 2, Animation = new LegacyAnimation { LoopCount = 0, Durations = [(50, 50), (50, 50)] }, Sprites = [9, 10, 11, 0] });
        dat.Outfits.Add(outfit);
        return dat;
    }

    [Fact]
    public void DatFilesRoundTripAndAreDetected()
    {
        var dat = SampleDat();
        byte[] bytes = LegacyDatCodec.Write(dat, LegacyClientProfiles.Client11);
        Assert.Equal(0x4A10u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        var read = LegacyDatCodec.Read(bytes, LegacyClientProfiles.Client11);
        Assert.True(read.Items[0].SameAs(dat.Items[0]));
        Assert.True(read.Outfits[0].SameAs(dat.Outfits[0]));
        Assert.Equal("maître", read.Items[0].Get(LegacyAttribute.Market)!.Value.Market!.NameText);
        Assert.Equal(bytes, LegacyDatCodec.Write(read, LegacyClientProfiles.Client11));
        var detected = LegacyDatCodec.Detect(bytes);
        Assert.NotNull(detected);
        Assert.Equal(LegacyClientProfiles.Client11.Id, detected.Value.Profile.Id);
        // Wrong profiles never read a file silently.
        Assert.Throws<InvalidDataException>(() => LegacyDatCodec.Read(bytes, LegacyClientProfiles.Tibia860));
        Assert.Throws<InvalidDataException>(() => LegacyDatCodec.Read(bytes[..^3], LegacyClientProfiles.Client11));
    }

    [Fact]
    public void WritingRefusesWhatTheProfileCannotHold()
    {
        var dat = SampleDat();
        dat.Items[0].Attributes.Add(new(LegacyAttribute.Chargeable));
        Assert.Throws<InvalidDataException>(() => LegacyDatCodec.Write(dat, LegacyClientProfiles.Client11));
        var bad = SampleDat();
        bad.Items[0].FrameGroups[0].Sprites = [1, 2];
        Assert.Throws<InvalidDataException>(() => LegacyDatCodec.Write(bad, LegacyClientProfiles.Client11));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpritesRoundTripWithAndWithoutTransparency(bool transparency)
    {
        string dir = TestData.NewTempDirectory("spr");
        var profile = LegacyClientProfiles.Client11.WithTransparency(transparency);
        var sprites = Enumerable.Range(1, 5).Select(i => SyntheticAssets.Sprite(i, 32, 32)).ToList();
        sprites.Add(new RgbaImage(32, 32)); // fully transparent
        string path = Path.Combine(dir, "Tibia.spr");
        using (var writer = new LegacySprWriter(Path.Combine(dir, "data.tmp"), profile))
        {
            foreach (var s in sprites) writer.Add(s);
            writer.AddEmpty();
            Assert.Equal(transparency ? 0 : sprites.Sum(s => s.Pixels.Count(p => p >> 24 is > 0 and < 255)), writer.FlattenedPixels);
            writer.Finish(path, Ct);
        }
        Assert.False(File.Exists(Path.Combine(dir, "data.tmp")));
        using var reader = new LegacySprReader(path, profile);
        Assert.Equal(7u, reader.Count);
        Assert.Equal(0x59E48E02u, reader.Signature);
        for (int i = 0; i < sprites.Count; i++)
            Assert.True(LegacySprWriter.AsStored(sprites[i], transparency).PixelEquals(reader.Read((uint)i + 1)!), $"sprite {i + 1}");
        Assert.Null(reader.Read(7));
        Assert.Null(reader.Read(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(8));
    }

    [Fact]
    public void DamagedSpriteDataIsReported()
    {
        Assert.Throws<InvalidDataException>(() => LegacySprReader.Decode([0x00, 0x00, 0xFF, 0x04, 1, 2, 3], alpha: false));
        Assert.Throws<InvalidDataException>(() => LegacySprReader.Decode([0x01], alpha: false));
    }

    private static ClientConversionResult Convert(string assets, string output, LegacyClientProfile profile, bool skipEmpty = true, IReadOnlySet<LegacyAttribute>? ext = null, int marketLimit = 0, int maxCategory = 0)
    {
        var temp = new TempWorkspace(Path.Combine(output, "_tmp"));
        using var session = new OutputSession("convert", FileSafetyOptions.Default with { WhenExists = ExistingFilePolicy.Overwrite });
        return ClientConverter.Run(new ClientConversionOptions
        {
            AssetsFolder = assets, OutputFolder = output, Profile = profile, SkipEmptySprites = skipEmpty, WorkerThreads = 2,
            ExtensionAttributes = ext ?? new HashSet<LegacyAttribute>(), MaxMarketNameLength = marketLimit, MaxMarketCategory = maxCategory,
        }, session, temp);
    }

    /// <summary>
    /// Market categories that 10.98-era clients do not know are listed under "Others" only when asked
    /// (and reported); restrictions stored at their older place inside the market data are used too.
    /// </summary>
    [Fact]
    public void NewerMarketCategoriesAreListedUnderOthersOnlyWhenAsked()
    {
        string root = TestData.NewTempDirectory("convert-market");
        string assets = SyntheticAssets.Create(Path.Combine(root, "assets"), all =>
        {
            var flags = all.Object.Single(o => o.Id == 103).Flags;
            flags.Market.Category = 26;                                                    // soul cores
            flags.ClearMinimumLevel();
            flags.RestrictToVocation.Clear();
            flags.Market.MinimumLevel = 20;                                                // the older place of the restrictions
            flags.Market.RestrictToVocation.Add(2);                                        // paladin
        });

        var mapped = Convert(assets, Path.Combine(root, "mapped"), LegacyClientProfiles.Client11, maxCategory: ClientConversionOptions.LegacyMarketCategoryLimit);
        var market = LegacyDatCodec.Read(mapped.DatPath!, LegacyClientProfiles.Client11).Find(LegacyCategory.Item, 103)!.Get(LegacyAttribute.Market)!.Value.Market!;
        Assert.Equal(ClientConversionOptions.MarketCategoryOthers, market.Category);
        Assert.Equal(1, mapped.MarketCategoriesMapped);
        Assert.Contains(mapped.Notes, n => n.Contains("\"Others\"", StringComparison.Ordinal));
        Assert.Equal((ushort)2, market.Vocation);
        Assert.Equal((ushort)20, market.Level);

        var kept = Convert(assets, Path.Combine(root, "kept"), LegacyClientProfiles.Client11);
        Assert.Equal((ushort)26, LegacyDatCodec.Read(kept.DatPath!, LegacyClientProfiles.Client11).Find(LegacyCategory.Item, 103)!.Get(LegacyAttribute.Market)!.Value.Market!.Category);
        Assert.Equal(0, kept.MarketCategoriesMapped);
    }

    [Fact]
    public void ConverterWritesAValidatedPairFromModernAssets()
    {
        string root = TestData.NewTempDirectory("convert");
        string assets = SyntheticAssets.Create(Path.Combine(root, "assets"));
        var result = Convert(assets, Path.Combine(root, "out"), LegacyClientProfiles.Client11, marketLimit: 29);
        Assert.Empty(result.Failures);
        Assert.NotNull(result.Validation);
        Assert.Contains("decode to exactly the pixels written", result.Validation);
        Assert.Equal(4, result.Items); // 100–103, with 102 written empty
        Assert.Equal(1, result.MarketNamesShortened);

        var dat = LegacyDatCodec.Read(result.DatPath!, LegacyClientProfiles.Client11);
        var ground = dat.Find(LegacyCategory.Item, 100)!;
        Assert.Equal(new LegacyAttributeValue(LegacyAttribute.Ground, 150), ground.Get(LegacyAttribute.Ground));
        Assert.True(ground.Has(LegacyAttribute.FullGround));
        Assert.Empty(dat.Find(LegacyCategory.Item, 102)!.Attributes);

        // Animation timing is written as stored (ClientConverter wrote the loop type instead of the count).
        var animated = dat.Find(LegacyCategory.Item, 101)!.FrameGroups[0];
        Assert.Equal((2, 2, 60, 3), ((int)animated.Width, (int)animated.Height, (int)animated.ExactSize, (int)animated.Phases));
        Assert.Equal(-1, animated.Animation!.LoopCount);
        Assert.True(animated.Animation.Synchronized);
        Assert.Equal([(100u, 100u), (150u, 200u), (100u, 300u)], animated.Animation.Durations);
        var moving = dat.Find(LegacyCategory.Outfit, 1)!.FrameGroups[1];
        Assert.Equal(4, moving.Animation!.LoopCount);
        Assert.Equal(2, dat.Find(LegacyCategory.Outfit, 1)!.FrameGroups.Count);

        // Market: Latin-1 name, shortened to 29 bytes; vocations as a bit mask (knight 1, druid 8); level.
        var market = dat.Find(LegacyCategory.Item, 103)!.Get(LegacyAttribute.Market)!.Value.Market!;
        Assert.Equal(29, market.Name.Length);
        Assert.StartsWith("djinn maître", market.NameText);
        Assert.Equal((ushort)9, market.Vocation);
        Assert.Equal((ushort)8, market.Level);

        // Tiles: legacy (0,0) is the bottom-right 32×32 of the 64×64 sprite 18.
        using var spr = new LegacySprReader(result.SprPath!, LegacyClientProfiles.Client11);
        var source = SyntheticAssets.Sprite(18, 64, 64);
        var bottomRight = LegacySprWriter.AsStored(source.Crop(32, 32, 32, 32), false);
        var topLeft = LegacySprWriter.AsStored(source.Crop(0, 0, 32, 32), false);
        Assert.True(bottomRight.PixelEquals(spr.Read(animated.Sprites[0])!));
        Assert.True(topLeft.PixelEquals(spr.Read(animated.Sprites[3])!));
    }

    [Fact]
    public void TallAndWideSpritesKeepTheirTileOrder()
    {
        string root = TestData.NewTempDirectory("convert-tiles");
        string assets = SyntheticAssets.Create(Path.Combine(root, "assets"));
        var result = Convert(assets, Path.Combine(root, "out"), LegacyClientProfiles.Tibia1098, skipEmpty: false);
        var dat = LegacyDatCodec.Read(result.DatPath!, LegacyClientProfiles.Tibia1098);
        using var spr = new LegacySprReader(result.SprPath!, LegacyClientProfiles.Tibia1098);
        var effect = dat.Find(LegacyCategory.Effect, 1)!.FrameGroups[0];   // sprite 10: 32 wide, 64 tall
        Assert.Equal((1, 2), ((int)effect.Width, (int)effect.Height));
        var tall = SyntheticAssets.Sprite(10, 32, 64);
        Assert.True(LegacySprWriter.AsStored(tall.Crop(0, 32, 32, 32), false).PixelEquals(spr.Read(effect.Sprites[0])!)); // bottom first
        Assert.True(LegacySprWriter.AsStored(tall.Crop(0, 0, 32, 32), false).PixelEquals(spr.Read(effect.Sprites[1])!));
        var missile = dat.Find(LegacyCategory.Missile, 1)!.FrameGroups[0]; // sprite 14: 64 wide, 32 tall
        Assert.Equal((2, 1), ((int)missile.Width, (int)missile.Height));
        var wide = SyntheticAssets.Sprite(14, 64, 32);
        Assert.True(LegacySprWriter.AsStored(wide.Crop(32, 0, 32, 32), false).PixelEquals(spr.Read(missile.Sprites[0])!)); // right first
        Assert.Equal(0x42A3u, dat.Signature);
    }

    [Fact]
    public void ExtensionAttributesAreWrittenOnlyWhenChosen()
    {
        string root = TestData.NewTempDirectory("convert-ext");
        string assets = SyntheticAssets.Create(Path.Combine(root, "assets"));
        var plain = Convert(assets, Path.Combine(root, "plain"), LegacyClientProfiles.Client11);
        Assert.DoesNotContain(LegacyDatCodec.Read(plain.DatPath!, LegacyClientProfiles.Client11).All.SelectMany(t => t.Attributes), a => LegacyAttributes.IsExtension(a.Attribute));
        Assert.Equal(1, plain.NotStored["corpse"]);
        var withCorpse = Convert(assets, Path.Combine(root, "ext"), LegacyClientProfiles.Client11, ext: new HashSet<LegacyAttribute> { LegacyAttribute.Corpse });
        Assert.True(LegacyDatCodec.Read(withCorpse.DatPath!, LegacyClientProfiles.Client11).Find(LegacyCategory.Item, 101)!.Has(LegacyAttribute.Corpse));
        Assert.False(withCorpse.NotStored.ContainsKey("corpse"));
    }

    [Fact]
    public void ExistingOutputIsKeptWhenTheUserDeclines()
    {
        string root = TestData.NewTempDirectory("convert-keep");
        string assets = SyntheticAssets.Create(Path.Combine(root, "assets"));
        string output = Path.Combine(root, "out");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "Tibia.spr"), "mine");
        var temp = new TempWorkspace(Path.Combine(root, "_tmp"));
        using var session = new OutputSession("convert", FileSafetyOptions.Default with { WhenExists = ExistingFilePolicy.Skip });
        var result = ClientConverter.Run(new ClientConversionOptions { AssetsFolder = assets, OutputFolder = output, WorkerThreads = 1 }, session, temp, ct: Ct);
        Assert.Null(result.SprPath);
        Assert.Null(result.DatPath);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(output, "Tibia.spr")));
        Assert.False(File.Exists(Path.Combine(output, "Tibia.dat")));
        Assert.Empty(Directory.GetDirectories(temp.Root));
    }

    [Fact]
    public void FullClientConversionValidates()
    {
        Assert.SkipUnless(TestData.Setting("FULL_SPRITES") == "1", "Set TF_FULL_SPRITES=1 to convert the whole client (about a minute).");
        string assets = TestData.RequireClientAssets();
        string output = TestData.NewTempDirectory("convert-full");
        try
        {
            var result = Convert(assets, output, LegacyClientProfiles.Client11, marketLimit: 29);
            Assert.Empty(result.Failures);
            Assert.Contains("all", result.Validation);
            var catalog = AssetCatalog.Load(assets);
            Assert.Equal(catalog.Sheets.Sum(s => s.SpriteCount * (s.SpriteWidth / 32) * (s.SpriteHeight / 32)), result.SpritesWritten + result.EmptyTilesSkipped);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }
}
