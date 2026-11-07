using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.Tests;

public class SpriteDecodingTests(ITestOutputHelper output)
{
    [Fact]
    public void CipHeader_IsParsedForEveryAssetKind()
    {
        string assets = TestData.RequireClientAssets();
        foreach (var pattern in new[] { "sprites-*.bmp.lzma", "minimap-*.bmp.lzma", "satellite-*.bmp.lzma", "subarea-*.bmp.lzma" })
        {
            string file = Directory.EnumerateFiles(assets, pattern).First();
            byte[] data = File.ReadAllBytes(file);
            Assert.True(CipLzma.TryReadHeader(data, out var header), pattern);
            Assert.Equal(data.Length - 32, header.CompressedSize);
            var bmp = CipLzma.Decompress(data);
            Assert.Equal(header.DecompressedSize, bmp.Length);
            Assert.Equal((byte)'B', bmp[0]);
            Assert.Equal((byte)'M', bmp[1]);
        }
    }

    [Fact]
    public void Catalog_LoadsAllSheetsSorted()
    {
        string assets = TestData.RequireClientAssets();
        var catalog = AssetCatalog.Load(assets);
        Assert.True(catalog.SheetCount > 1000);
        Assert.Equal(catalog.Sheets.OrderBy(s => s.FirstSpriteId).Select(s => s.FirstSpriteId), catalog.Sheets.Select(s => s.FirstSpriteId));
        Assert.True(catalog.Files.ContainsKey(AssetTypes.Appearances));
        var first = catalog.Sheets[0];
        Assert.Same(first, catalog.FindSheet(first.FirstSpriteId));
        Assert.Same(first, catalog.FindSheet(first.LastSpriteId));
        Assert.Null(catalog.FindSheet(-5));
        output.WriteLine($"{catalog.SheetCount} sheets, {catalog.TotalSprites} sprites, highest id {catalog.HighestSpriteId}");
    }

    [Fact]
    public void DecodedSheet_MatchesOriginalLzmaOutput()
    {
        string assets = TestData.RequireClientAssets();
        string refDir = TestData.RequireReference("original", "lzma");
        var catalog = AssetCatalog.Load(assets);
        foreach (var bmpPath in Directory.EnumerateFiles(refDir, "sheet_type*.bmp"))
        {
            // file name: sheet_type{T}_{firstId}.bmp
            int firstId = int.Parse(Path.GetFileNameWithoutExtension(bmpPath).Split('_')[2]);
            var sheet = catalog.FindSheet(firstId)!;
            byte[] mine = CipLzma.DecompressFile(catalog.GetSheetPath(sheet));
            byte[] original = File.ReadAllBytes(bmpPath);
            // The original decoded with a 1 MiB output cap and no size check, so it may have
            // trailing bytes after the image; the image itself must be identical.
            Assert.True(original.Length >= mine.Length);
            Assert.True(original.AsSpan(0, mine.Length).SequenceEqual(mine), $"{Path.GetFileName(bmpPath)} differs");
        }
    }

    [Fact]
    public void SlicedSprites_MatchOriginalSplitSprites()
    {
        string assets = TestData.RequireClientAssets();
        string refDir = TestData.RequireReference("original", "lzma");
        var catalog = AssetCatalog.Load(assets);
        int compared = 0, identical = 0, maxPremultiplied = 0;
        foreach (var dir in Directory.EnumerateDirectories(refDir, "split_type*"))
        {
            foreach (var png in Directory.EnumerateFiles(dir, "*.png"))
            {
                int id = int.Parse(Path.GetFileNameWithoutExtension(png));
                var sheet = catalog.FindSheet(id)!;
                var mine = SpriteSheetDecoder.DecodeSprites(catalog, sheet)[id - sheet.FirstSpriteId];
                var original = ImageIO.Load(png);
                var diff = ImageCompare.Compare(original, mine);
                compared++;
                if (diff.Identical) identical++;
                else
                {
                    Assert.Null(diff.SizeMismatch);
                    Assert.Equal(0, diff.DifferentOpaquePixels);
                    maxPremultiplied = Math.Max(maxPremultiplied, ImageCompare.MaxPremultipliedDelta(original, mine));
                }
            }
        }
        output.WriteLine($"{compared} sprites compared, {identical} pixel-identical, max premultiplied delta of the rest: {maxPremultiplied}");
        Assert.True(compared > 100);
        Assert.True(maxPremultiplied <= 2, "Non-identical sprites differ by more than GDI+ premultiplication rounding.");
    }

    /// <summary>
    /// Every sprite of the client against the PNG the ORIGINAL program extracted for it
    /// (04_reference\original_sprites, 223,694 files). Opt-in because it reads ~1 GB of PNGs:
    /// set TF_FULL_SPRITES=1.
    /// </summary>
    [Fact]
    public void AllSprites_MatchOriginalExtraction()
    {
        Assert.SkipUnless(TestData.Setting("FULL_SPRITES") == "1", "Full sprite comparison is opt-in (set TF_FULL_SPRITES=1).");
        string assets = TestData.RequireClientAssets();
        string refDir = TestData.RequireReference("original_sprites");
        var catalog = AssetCatalog.Load(assets);
        var referenceIds = Directory.EnumerateFiles(refDir, "*.png").Select(f => int.Parse(Path.GetFileNameWithoutExtension(f))).ToHashSet();
        var catalogIds = catalog.Sheets.SelectMany(s => Enumerable.Range(s.FirstSpriteId, s.SpriteCount)).ToHashSet();
        Assert.Equal(referenceIds.Count, catalogIds.Count);
        Assert.True(referenceIds.SetEquals(catalogIds), "The original extracted a different set of sprite IDs.");

        int identical = 0, rounding = 0, maxDelta = 0;
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(catalog.Sheets, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = TestContext.Current.CancellationToken }, sheet =>
        {
            var sprites = SpriteSheetDecoder.DecodeSprites(catalog, sheet);
            int localIdentical = 0, localRounding = 0, localMax = 0;
            for (int i = 0; i < sheet.SpriteCount; i++)
            {
                int id = sheet.FirstSpriteId + i;
                var original = ImageIO.Load(Path.Combine(refDir, id + ".png"));
                var diff = ImageCompare.Compare(original, sprites[i]);
                if (diff.Identical) { localIdentical++; continue; }
                int delta = diff.SizeMismatch == null ? ImageCompare.MaxPremultipliedDelta(original, sprites[i]) : int.MaxValue;
                if (diff.SizeMismatch == null && diff.DifferentOpaquePixels == 0 && delta <= 2)
                {
                    localRounding++;
                    localMax = Math.Max(localMax, delta);
                }
                else failures.Add($"{id}: {diff}");
            }
            Interlocked.Add(ref identical, localIdentical);
            Interlocked.Add(ref rounding, localRounding);
            int seen;
            while ((seen = Volatile.Read(ref maxDelta)) < localMax && Interlocked.CompareExchange(ref maxDelta, localMax, seen) != seen) { }
        });

        output.WriteLine($"{catalogIds.Count:N0} sprites: {identical:N0} pixel-identical, {rounding:N0} differ only by GDI+ premultiplication rounding (max {maxDelta}), {failures.Count} different");
        foreach (string f in failures.Take(20)) output.WriteLine("  " + f);
        Assert.Empty(failures);
    }

    [Fact]
    public void Repository_FallsBackToClientWhenFileMissing()
    {
        string assets = TestData.RequireClientAssets();
        var catalog = AssetCatalog.Load(assets);
        string emptyFolder = TestData.NewTempDirectory("sprites_empty");
        var repo = new SpriteRepository(catalog, emptyFolder);
        var sheet = catalog.Sheets[10];
        var sprite = repo.GetSprite((uint)sheet.FirstSpriteId);
        Assert.NotNull(sprite);
        Assert.Equal(sheet.SpriteWidth, sprite!.Width);
        var folderOnly = new SpriteRepository(catalog, emptyFolder, SpriteSourceMode.FolderOnly);
        Assert.Null(folderOnly.GetSprite((uint)sheet.FirstSpriteId));
    }

    [Fact]
    public async Task Extractor_WritesOriginalLayout()
    {
        string assets = TestData.RequireClientAssets();
        var catalog = AssetCatalog.Load(assets);
        string outDir = TestData.NewTempDirectory("extract");
        var sheets = new[] { catalog.Sheets[0], catalog.Sheets.First(s => s.Type == SpriteSheetType.Large) };
        var options = new SpriteExtractionOptions { OutputDirectory = outDir, SplitIntoSheetFolders = true };
        var result = await SpriteExtractor.ExtractAsync(catalog, sheets, options, null, TestContext.Current.CancellationToken);
        Assert.False(result.HasFailures);
        Assert.Equal(sheets.Sum(s => s.SpriteCount), result.Succeeded);
        Assert.True(File.Exists(Path.Combine(outDir, $"Sheet {sheets[0].FirstSpriteId}-{sheets[0].LastSpriteId}", sheets[0].FirstSpriteId + ".png")));

        var whole = options with { Mode = SpriteExportMode.WholeSheets, SplitIntoSheetFolders = false };
        await SpriteExtractor.ExtractAsync(catalog, sheets, whole, null, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(Path.Combine(outDir, $"Sprites {sheets[0].FirstSpriteId}-{sheets[0].LastSpriteId}.png")));

        var gif = options with { Format = SpriteImageFormat.Gif, SplitIntoSheetFolders = false, OutputDirectory = Path.Combine(outDir, "gif") };
        var gifResult = await SpriteExtractor.ExtractAsync(catalog, new[] { sheets[0] }, gif, null, TestContext.Current.CancellationToken);
        Assert.Equal(sheets[0].SpriteCount, gifResult.Succeeded);
        Assert.True(File.Exists(Path.Combine(outDir, "gif", sheets[0].FirstSpriteId + ".gif")));
        Directory.Delete(outDir, true);
    }
}
