using System.Text;
using ImageMagick;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Map;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.Tests;

/// <summary>
/// Tests that change the process-wide creator name run in this collection, never in parallel with
/// other tests (which may export files and read the creator at the same time).
/// </summary>
[CollectionDefinition("Attribution", DisableParallelization = true)]
public sealed class AttributionCollection;

/// <summary>
/// Creator attribution embedded in exported files. Metadata is checked with ImageMagick (an
/// independent reader) as well as with Tibia Foundry's own reader, and image content is compared
/// with an export that carries no metadata.
/// </summary>
[Collection("Attribution")]
public sealed class AttributionTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _previousCreator = Attribution.CurrentCreator;

    public void Dispose() => Attribution.CurrentCreator = _previousCreator;

    private static RgbaImage Sample(int w = 12, int h = 9)
    {
        var image = new RgbaImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                uint alpha = (uint)(x == 0 ? 0 : x == 1 ? 128 : 255);   // transparent, half and opaque columns
                image.Pixels[y * w + x] = alpha << 24 | (uint)(x * 20) << 16 | (uint)(y * 25) << 8 | 0x40;
            }
        return image;
    }

    // ------------------------------------------------------------------ PNG

    [Fact]
    public void PngExportCarriesTheConfiguredCreatorReadableByImageMagick()
    {
        Attribution.CurrentCreator = "Alice";
        string file = Path.Combine(TestData.NewTempDirectory("attr"), "a.png");
        ImageIO.SavePng(Sample(), file);

        const string expected = "Created by Alice using TibiaFoundry for TibiaWiki @ https://tibia.fandom.com/";
        using var magick = new MagickImage(file);
        foreach (string name in magick.AttributeNames) output.WriteLine($"{name} = {magick.GetAttribute(name)}");
        Assert.Equal(expected, magick.GetAttribute("Description"));
        Assert.Equal(expected, magick.Comment);
        Assert.Equal("Alice", magick.GetAttribute("Author"));
        Assert.Equal(AppIdentity.NameAndVersion, magick.GetAttribute("Software"));
        Assert.Equal(Attribution.TibiaArtworkNotice, magick.GetAttribute("Copyright"));
        string xmp = Encoding.UTF8.GetString(magick.GetXmpProfile()!.ToByteArray());
        Assert.Contains("<rdf:li>Alice</rdf:li>", xmp);
        Assert.Contains("<tf:Website>https://tibia.fandom.com/</tf:Website>", xmp);
        Assert.Contains("<xmp:CreatorTool>" + AppIdentity.NameAndVersion + "</xmp:CreatorTool>", xmp);
        Assert.Contains("<xmp:CreateDate>", xmp);
        Assert.DoesNotContain("RockyLaRo", File.ReadAllText(file, Encoding.Latin1)); // the configured user, never a hard-coded name
    }

    [Fact]
    public void PngMetadataLeavesPixelsDimensionsAndTransparencyUnchanged()
    {
        var image = Sample();
        string file = Path.Combine(TestData.NewTempDirectory("attr"), "pixels.png");
        ImageIO.SavePng(image, file);
        var back = ImageIO.Load(file);
        Assert.Equal((image.Width, image.Height), (back.Width, back.Height));
        Assert.True(image.PixelEquals(back));
        Assert.Contains(back.Pixels, p => p >> 24 == 0);
        Assert.Contains(back.Pixels, p => p >> 24 == 128);

        // Identical image data chunks: only text chunks were added.
        using var plain = new MagickImage(ImageIO.EncodePngPixels(image));
        using var attributed = new MagickImage(file);
        Assert.Equal(0.0, plain.Compare(attributed, ErrorMetric.Absolute));
    }

    [Fact]
    public void NonLatinCreatorNamesAreStoredAsUtf8()
    {
        Attribution.CurrentCreator = "Rocký Ünïcødé 剣";
        byte[] png = ImageIO.EncodePng(Sample());
        var found = PngMetadata.Read(png)!;
        Assert.Equal(["Rocký Ünïcødé 剣"], found.Creators);
        Assert.Contains(PngMetadata.ReadText(png), t => t.Keyword == "Author" && t.ChunkType == "iTXt");
        using var magick = new MagickImage(png);
        Assert.Equal("Rocký Ünïcødé 剣", magick.GetAttribute("Author"));
    }

    [Fact]
    public void ReEncodingReplacesOldAttributionInsteadOfDuplicatingIt()
    {
        byte[] first = ImageIO.EncodePng(Sample(), Attribution.ForNewAsset("Alice"));
        byte[] second = PngMetadata.Embed(first, Attribution.ForNewAsset("Bob"));
        var texts = PngMetadata.ReadText(second);
        Assert.Single(texts, t => t.Keyword == "Description");
        Assert.Single(texts, t => t.Keyword == PngMetadata.XmpKeyword);
        Assert.Equal(["Bob"], PngMetadata.Read(second)!.Creators);
    }

    [Fact]
    public void ClipboardDataUriCarriesAttribution()
    {
        Attribution.CurrentCreator = "Alice";
        string uri = ImageIO.ToPngDataUri(Sample());
        byte[] png = Convert.FromBase64String(uri["data:image/png;base64,".Length..]);
        Assert.Equal(Attribution.TextFor("Alice"), PngMetadata.Read(png)!.Text);
    }

    // ------------------------------------------------------------------ GIF

    [Fact]
    public void AnimatedGifKeepsFramesTimingAndLoopAndCarriesAttribution()
    {
        Attribution.CurrentCreator = "Alice";
        var frames = new List<RgbaImage> { Sample(), Sample(), Sample() };
        frames[1].Pixels[20] = 0xFFFF0000;
        frames[2].Clear(0x00000000); // a fully transparent frame stays transparent
        int[] delays = [100, 250, 40];
        string file = Path.Combine(TestData.NewTempDirectory("attr"), "anim.gif");
        GifWriter.Write(file, frames, delays);

        using var gif = new MagickImageCollection(file);
        Assert.Equal(3, gif.Count);
        Assert.Equal(new uint[] { 10, 25, 4 }, gif.Select(f => f.AnimationDelay).ToArray());
        Assert.Equal(0u, gif[0].AnimationIterations);       // infinite loop
        Assert.Equal(Attribution.TextFor("Alice"), gif[0].Comment);
        // ImageMagick does not read XMP from GIFs; check it the way MediaWiki (TibiaWiki's software) does.
        string xmp = XmpAsReadByMediaWiki(File.ReadAllBytes(file));
        Assert.Contains("<rdf:li>Alice</rdf:li>", xmp);
        Assert.Equal(["Alice"], XmpPacket.Parse(xmp)!.Creators);

        // Same frames as an encode without any metadata.
        byte[] plain;
        using (var raw = new MagickImageCollection())
        {
            for (int i = 0; i < frames.Count; i++)
            {
                var image = new MagickImage(ImageIO.EncodePngPixels(frames[i]));
                image.AnimationDelay = (uint)(delays[i] / 10);
                image.GifDisposeMethod = GifDisposeMethod.Background;
                raw.Add(image);
            }
            plain = raw.ToByteArray(MagickFormat.Gif);
        }
        using var expected = new MagickImageCollection(plain);
        for (int i = 0; i < 3; i++)
            Assert.Equal(0.0, expected[i].Compare(gif[i], ErrorMetric.Absolute));
        Assert.True(gif[2].GetPixels().ToByteArray(PixelMapping.RGBA)!.Where((_, k) => k % 4 == 3).All(a => a == 0));
    }

    /// <summary>
    /// The algorithm of MediaWiki's GIFMetadataExtractor: after the "XMP DataXMP" identifier, read
    /// the following data as sub-blocks keeping their length bytes, require the 257-byte magic
    /// trailer (01 FF FE … 03 02 01 00) and strip it. Independent of Tibia Foundry's own reader.
    /// </summary>
    private static string XmpAsReadByMediaWiki(byte[] gif)
    {
        int pos = gif.AsSpan().IndexOf("XMP DataXMP"u8);
        Assert.True(pos >= 3 && gif[pos - 3] == 0x21 && gif[pos - 2] == 0xFF && gif[pos - 1] == 11, "XMP application extension missing");
        pos += 11;
        var data = new List<byte>();
        while (gif[pos] != 0)
        {
            int n = gif[pos];
            data.Add((byte)n);
            data.AddRange(gif.AsSpan(pos + 1, n).ToArray());
            pos += 1 + n;
        }
        byte[] bytes = data.ToArray();
        Assert.True(bytes.AsSpan(bytes.Length - 257, 3).SequenceEqual(new byte[] { 0x01, 0xFF, 0xFE }), "magic trailer start");
        Assert.True(bytes.AsSpan(bytes.Length - 4).SequenceEqual(new byte[] { 0x03, 0x02, 0x01, 0x00 }), "magic trailer end");
        return Encoding.UTF8.GetString(bytes, 0, bytes.Length - 257);
    }

    [Fact]
    public void SingleSpriteGifsAreAttributedToo()
    {
        Attribution.CurrentCreator = "Alice";
        byte[] gif = GifWriter.Encode([Sample()], [0]);
        Assert.Equal(Attribution.TextFor("Alice"), GifMetadata.Read(gif)!.Text);
        Assert.Single(GifMetadata.ReadComments(gif));
    }

    // ------------------------------------------------------------------ provenance

    [Fact]
    public void ReExportKeepsTheOriginalCreatorAndRecordsTheEditor()
    {
        string dir = TestData.NewTempDirectory("attr");
        string original = Path.Combine(dir, "alice.png");
        ImageIO.SavePng(Sample(), original, Attribution.ForNewAsset("Alice"));
        var sources = AttributionReader.ReadAll([original]);

        var review = Attribution.Review(sources, "Bob");
        Assert.True(review.HasConflict);
        Assert.Equal(["Alice"], review.OtherCreators);

        var kept = Attribution.ForDerivedAsset(sources, AttributionChoice.KeepOriginalCreators, "Bob");
        Assert.Equal(["Alice"], kept.Creators);
        Assert.StartsWith("Created by Alice using TibiaFoundry", kept.Text);
        Assert.Contains("Edited by Bob using TibiaFoundry for TibiaWiki @ https://tibia.fandom.com/", kept.Text);
        Assert.Equal(2, kept.History.Count);
        Assert.Contains("original creators kept: Alice", kept.History[^1]);

        var mine = Attribution.ForDerivedAsset(sources, AttributionChoice.AttributeToMe, "Bob");
        Assert.Equal(["Bob"], mine.Creators);
        Assert.Equal(Attribution.TextFor("Bob"), mine.Text);
        Assert.Contains("previously attributed to Alice", mine.History[^1]);

        var same = Attribution.ForDerivedAsset(sources, AttributionChoice.KeepOriginalCreators, "alice");
        Assert.False(Attribution.Review(sources, "alice").HasConflict); // names compare case-insensitively
        Assert.Equal(["alice"], same.Creators);
    }

    [Fact]
    public void SlicedSheetsKeepTheSheetAttribution()
    {
        string dir = TestData.NewTempDirectory("attr");
        string sheet = Path.Combine(dir, "sheet.png");
        ImageIO.SavePng(new RgbaImage(12 * 8, 2 * 8), sheet, Attribution.ForNewAsset("Alice"));
        var info = Attribution.ForDerivedAsset(AttributionReader.ReadAll([sheet]), AttributionChoice.KeepOriginalCreators, "Bob");
        int written = SheetComposer.SliceOutfitSheet(ImageIO.Load(sheet), 8, 8, Path.Combine(dir, "cells"), "x", info);
        Assert.Equal(24, written);
        Assert.All(Directory.GetFiles(Path.Combine(dir, "cells")), f => Assert.Equal(["Alice"], AttributionReader.Read(f)!.Creators));
    }

    [Fact]
    public void ConvertingAnAttributedGifToPngKeepsItsCreator()
    {
        string dir = TestData.NewTempDirectory("attr");
        string gif = Path.Combine(dir, "alice.gif");
        GifWriter.Write(gif, [Sample()], [100], Attribution.ForNewAsset("Alice"));
        var found = AttributionReader.Read(gif)!;
        Assert.Equal(["Alice"], found.Creators);
        string png = Path.Combine(dir, "converted.png");
        ImageIO.SavePng(ImageIO.Load(gif), png, Attribution.ForDerivedAsset([found], AttributionChoice.KeepOriginalCreators, "Bob"));
        Assert.Equal(["Alice"], AttributionReader.Read(png)!.Creators);
    }

    [Fact]
    public void GifCommentsOfTheOriginalProgramAreRecognized()
    {
        // GIFs made by the original program carry "Created by <author> using <its name> for Tibia.Fandom.com".
        string refDir = TestData.RequireReference("original_run2", "gifs");
        string? reference = Directory.GetFiles(refDir, "*.gif").FirstOrDefault();
        Assert.SkipWhen(reference == null, "No reference GIF of the original program.");
        var found = AttributionReader.Read(reference!)!;
        Assert.Equal(["ParityHarness"], found.Creators);
        Assert.Contains(AppIdentity.Legacy.Version2Id, found.Text);
    }

    // ------------------------------------------------------------------ batches

    [Fact]
    public async Task BatchSpriteExtractionAttributesEveryFileInBothFormats()
    {
        string assets = TestData.RequireClientAssets();
        Attribution.CurrentCreator = "Alice";
        var catalog = AssetCatalog.Load(assets);
        var sheets = catalog.Sheets.Take(3).ToList();
        foreach (var format in new[] { SpriteImageFormat.Png, SpriteImageFormat.Gif })
        {
            string dir = TestData.NewTempDirectory("attrbatch");
            var result = await SpriteExtractor.ExtractAsync(catalog, sheets, new SpriteExtractionOptions { OutputDirectory = dir, Format = format },
                null, TestContext.Current.CancellationToken);
            var files = Directory.GetFiles(dir, "*." + format.ToString().ToLowerInvariant(), SearchOption.AllDirectories);
            Assert.Equal(sheets.Sum(s => s.SpriteCount), files.Length);
            Assert.Equal(files.Length, result.Succeeded);
            Assert.All(files, f => Assert.Equal(Attribution.TextFor("Alice"), AttributionReader.Read(f)?.Text));
        }
    }

    [Fact]
    public async Task ExtractedMapTilesAreAttributed()
    {
        string assets = TestData.RequireClientAssets();
        Attribution.CurrentCreator = "Alice";
        var map = MapDatabase.Load(TestData.Client.FindAssetFile(AssetTypes.Map)!);
        string dir = TestData.NewTempDirectory("attrmap");
        var result = await MapImageTools.ExtractAsync(map, new MapExtractionOptions(assets, dir, Subareas: true, Satellite: false, Minimap: false),
            null, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded > 100);
        Assert.All(Directory.GetFiles(dir, "*.png"), f => Assert.Equal(["Alice"], AttributionReader.Read(f)?.Creators));
    }

    // ------------------------------------------------------------------ failures and malformed input

    [Fact]
    public void AnExportThatCannotEmbedAttributionWritesNothing()
    {
        string dir = TestData.NewTempDirectory("attrfail");
        string file = Path.Combine(dir, "keep.png");
        ImageIO.SavePng(Sample(), file);
        byte[] before = File.ReadAllBytes(file);

        var broken = Attribution.ForNewAsset("Alice") with { Creators = [] };
        Assert.Throws<AttributionException>(() => ImageIO.SavePng(Sample(4, 4), file, broken));
        Assert.Throws<AttributionException>(() => GifWriter.Write(Path.Combine(dir, "new.gif"), [Sample()], [100], broken with { Creators = ["Alice"], Text = "bad" + (char)1 + "text" }));

        Assert.Equal(before, File.ReadAllBytes(file));                 // existing file untouched
        Assert.Equal([file], Directory.GetFiles(dir));                  // no new or temporary files
    }

    [Fact]
    public void MalformedMetadataIsIgnoredSafely()
    {
        byte[] png = ImageIO.EncodePng(Sample(), Attribution.ForNewAsset("Alice"));

        // Damaged CRC of every text chunk: they are skipped, nothing throws.
        byte[] damaged = (byte[])png.Clone();
        for (int pos = 8; pos + 12 <= damaged.Length;)
        {
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(damaged.AsSpan(pos));
            string type = Encoding.ASCII.GetString(damaged, pos + 4, 4);
            if (type is "tEXt" or "iTXt") damaged[pos + 8 + length] ^= 0xFF;
            pos += 12 + length;
        }
        Assert.Null(PngMetadata.Read(damaged));

        // Malformed XMP (in a chunk with a valid CRC) falls back to the text chunks; a DTD is never processed.
        byte[] brokenXmp = WithChunks(ImageIO.EncodePngPixels(Sample()),
            TextChunk("tEXt", "Author", Encoding.Latin1.GetBytes("Alice")),
            TextChunk("tEXt", "Description", Encoding.Latin1.GetBytes(Attribution.TextFor("Alice"))),
            TextChunk("iTXt", PngMetadata.XmpKeyword, [0, 0, 0, 0, .. Encoding.UTF8.GetBytes("<x:xmpmeta <<")]));
        var fallback = PngMetadata.Read(brokenXmp);
        Assert.NotNull(fallback);
        Assert.Equal(new[] { "Alice" }, fallback.Creators);
        Assert.Equal(Attribution.TextFor("Alice"), fallback.Text);
        Assert.Null(XmpPacket.Parse("<x:xmpmeta <<"));
        Assert.Null(XmpPacket.Parse("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><x>&e;</x>"));

        // Compressed text: a decompression bomb (3 MB of zeros, over the 1 MB limit) and damaged zlib
        // data are skipped; the other chunks are still read.
        byte[] compressed = WithChunks(ImageIO.EncodePngPixels(Sample()),
            TextChunk("zTXt", "Comment", [0, .. ZlibCompress(new byte[3 << 20])]),
            TextChunk("zTXt", "Description", [0, 0x78, 0x9C, 0x07, 0, 0, 0, 0]),
            TextChunk("tEXt", "Author", Encoding.Latin1.GetBytes("Alice")));
        Assert.Equal(new[] { "Author" }, PngMetadata.ReadText(compressed).Select(t => t.Keyword));

        // Truncated files and other formats never throw from the reader.
        Assert.Null(AttributionReader.Read(png.AsSpan(0, 40)));
        Assert.Null(AttributionReader.Read("not an image"u8));
        Assert.Null(AttributionReader.Read(Path.Combine(TestData.NewTempDirectory("attr"), "missing.png")));
        Assert.Throws<InvalidDataException>(() => PngMetadata.Embed([1, 2, 3], Attribution.ForNewAsset("Alice")));
        Assert.Throws<InvalidDataException>(() => GifMetadata.Embed("GIF89a"u8.ToArray(), Attribution.ForNewAsset("Alice")));

        // A truncated GIF: comments that are complete are still read.
        byte[] gif = GifWriter.Encode([Sample(), Sample()], [100]);
        Assert.Equal(Attribution.TextFor(Attribution.CurrentCreator), GifMetadata.Read(gif.AsSpan(0, gif.Length - 30))?.Text);
    }

    [Fact]
    public void FilesExportedByVersion3KeepTheirAttribution()
    {
        // Version 3.0 wrote its identifier in the sentence, a "<identifier> History" text chunk and its own XMP namespace.
        string oldText = $"Created by Erin using {AppIdentity.Legacy.Version3Id} for TibiaWiki @ https://tibia.fandom.com/";
        string oldHistory = "2026-10-12T10:00:00-04:00 Created by Erin with version 3.0.0";
        var forge = Attribution.ForNewAsset("Erin") with { Text = oldText, History = [oldHistory] };
        string forgeXmp = XmpPacket.Build(forge).Replace(XmpPacket.TibiaFoundryNamespace, XmpPacket.LegacyNamespace, StringComparison.Ordinal);
        Assert.Contains(XmpPacket.LegacyNamespace, forgeXmp);
        byte[] png = WithChunks(ImageIO.EncodePngPixels(Sample()),
            TextChunk("tEXt", "Author", Encoding.Latin1.GetBytes("Erin")),
            TextChunk("tEXt", "Description", Encoding.Latin1.GetBytes(oldText)),
            TextChunk("tEXt", PngMetadata.LegacyHistoryKeyword, Encoding.Latin1.GetBytes(oldHistory)),
            TextChunk("iTXt", PngMetadata.XmpKeyword, [0, 0, 0, 0, .. Encoding.UTF8.GetBytes(forgeXmp)]));

        var found = PngMetadata.Read(png);
        Assert.NotNull(found);
        Assert.Equal(new[] { "Erin" }, found.Creators);
        Assert.Equal(oldText, found.Text);
        Assert.Equal(new[] { oldHistory }, found.History);

        // Without dc:creator, the creator still comes from the old namespace's own field.
        var ownFieldsOnly = XmpPacket.Parse(forgeXmp.Replace("dc:creator>", "dc:unused>", StringComparison.Ordinal));
        Assert.NotNull(ownFieldsOnly);
        Assert.Equal(new[] { "Erin" }, ownFieldsOnly.Creators);
        Assert.Equal(new[] { oldHistory }, ownFieldsOnly.History);

        // Re-exported by someone else: Erin stays the creator, the old history is kept, and the old
        // history chunk is replaced instead of duplicated.
        var derived = Attribution.ForDerivedAsset([found], AttributionChoice.KeepOriginalCreators, creator: "Alice");
        byte[] again = PngMetadata.Embed(png, derived);
        var keywords = PngMetadata.ReadText(again).Select(t => t.Keyword).ToList();
        Assert.DoesNotContain(PngMetadata.LegacyHistoryKeyword, keywords);
        Assert.Single(keywords, k => k == "TibiaFoundry History");
        var reread = PngMetadata.Read(again);
        Assert.NotNull(reread);
        Assert.Equal(new[] { "Erin" }, reread.Creators);
        Assert.Contains(oldHistory, reread.History);
        Assert.Contains("Edited by Alice using TibiaFoundry", reread.Text);
    }

    /// <summary>A PNG text chunk with a valid CRC: keyword, NUL, then <paramref name="rest"/>.</summary>
    private static byte[] TextChunk(string type, string keyword, byte[] rest)
    {
        byte[] data = [.. Encoding.Latin1.GetBytes(keyword), 0, .. rest];
        var chunk = new byte[12 + data.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32.Compute(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    /// <summary>The PNG with the chunks inserted after IHDR (8-byte signature + 25-byte IHDR chunk).</summary>
    private static byte[] WithChunks(byte[] png, params byte[][] chunks) => [.. png[..33], .. chunks.SelectMany(c => c), .. png[33..]];

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionLevel.Optimal)) z.Write(data);
        return output.ToArray();
    }

    [Fact]
    public void CreatorNamesAreNormalized()
    {
        Assert.Equal("Rocky La Ro", Attribution.NormalizeCreator("  Rocky\t La\nRo  "));
        Assert.Null(Attribution.NormalizeCreator("   "));
        Assert.Equal(Attribution.MaxCreatorLength, Attribution.NormalizeCreator(new string('x', 200))!.Length);
        Attribution.CurrentCreator = "";
        Assert.Equal(Attribution.DefaultCreator, Attribution.CurrentCreator);
    }

    [Fact]
    public void TextExportsCarryTheAttributionAsAComment()
    {
        var info = Attribution.ForNewAsset("Alice");
        string lua = TextAttribution.LuaHeader(info) + "return {\n}\n";
        Assert.StartsWith("-- " + Attribution.TextFor("Alice"), lua);
        Assert.Equal(Attribution.TextFor("Alice"), TextAttribution.Find(lua));
        Assert.Equal("return {\n}\n", TextAttribution.StripHeader(lua));
        string report = TextAttribution.ReportHeader(info) + "Old: a\n";
        Assert.Equal("Old: a\n", TextAttribution.StripHeader(report));
        Assert.Equal("<!-- " + Attribution.TextFor("Alice") + " -->\n", TextAttribution.WikiComment(info));
    }
}
