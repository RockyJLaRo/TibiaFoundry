using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.Tests;

/// <summary>
/// Malformed, truncated and unexpected input for the low-level decoders, the sprite catalog and
/// file handling (audit findings A01–A08, A14, A21). Every case must end in a controlled
/// <see cref="InvalidDataException"/> (or a recorded problem) — never a crash, hang, huge
/// allocation or a write outside the intended folder.
/// </summary>
public sealed class RobustnessTests(ITestOutputHelper output)
{
    private static byte[] RealSheetBytes(out SpriteSheetInfo sheet)
    {
        string assets = TestData.RequireClientAssets();
        var catalog = AssetCatalog.Load(assets);
        sheet = catalog.Sheets.First(s => s.Type == SpriteSheetType.Large);
        return File.ReadAllBytes(catalog.GetSheetPath(sheet));
    }

    // ------------------------------------------------------------------ LZMA / CIP container

    [Fact]
    public void TruncatedSheetIsReportedNotDecodedToGarbage()
    {
        byte[] data = RealSheetBytes(out _);
        foreach (double keep in new[] { 0.9, 0.5, 0.1 })
        {
            var cut = data.AsSpan(0, (int)(data.Length * keep)).ToArray();
            var ex = Assert.Throws<InvalidDataException>(() => CipLzma.Decompress(cut));
            output.WriteLine($"{keep:P0}: {ex.Message}");
        }
        // A few missing bytes (less than the header length) are caught by the container check too.
        foreach (int missing in new[] { 1, 5, 20 })
        {
            var ex = Assert.Throws<InvalidDataException>(() => CipLzma.Decompress(data.AsSpan(0, data.Length - missing)));
            Assert.Contains("truncated", ex.Message);
        }
    }

    [Fact]
    public void LzmaDecoderDetectsTruncatedInputOnItsOwn()
    {
        // The container check above rejects a cut file before decoding starts. This covers the
        // decoder itself (audit A01): with the output size known, running out of input must be an
        // error, not zero-filled garbage (the range coder used to read zeros past the end).
        byte[] data = RealSheetBytes(out _);
        Assert.True(CipLzma.TryReadHeader(data, out var header));
        byte[] payload = data[header.PayloadOffset..];
        Assert.Equal(header.CompressedSize, payload.Length + 13); // properties + size field + payload
        Assert.Equal(header.DecompressedSize, LzmaDecoder.Decode(header.Properties, payload, header.DecompressedSize).Length);
        foreach (double keep in new[] { 0.9, 0.5, 0.1 })
        {
            byte[] cut = payload[..(int)(payload.Length * keep)];
            var ex = Assert.Throws<InvalidDataException>(() => LzmaDecoder.Decode(header.Properties, cut, header.DecompressedSize));
            Assert.Contains("truncated", ex.Message);
        }
    }

    [Fact]
    public void ImplausibleDeclaredSizeIsRejectedWithoutAllocating()
    {
        // Marker + varint decompressed size of ~2 GB + compressed size + properties + 8 size bytes + payload.
        var bytes = new List<byte> { 0x70, 0x0A };
        bytes.AddRange(Varint(2_000_000_000));
        bytes.AddRange(Varint(100));
        bytes.AddRange(new byte[] { 0x5D, 0, 0, 0, 2 });
        bytes.AddRange(new byte[8]);
        bytes.AddRange(new byte[100]);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var ex = Assert.Throws<InvalidDataException>(() => CipLzma.Decompress(bytes.ToArray()));
        Assert.Contains("exceeds the limit", ex.Message);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 64 * 1024 * 1024, "no large allocation may happen");
    }

    [Fact]
    public void EmptyAndHeaderOnlyInputsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => CipLzma.Decompress(Array.Empty<byte>()));
        Assert.Throws<InvalidDataException>(() => CipLzma.Decompress(new byte[] { 0, 0, 0x70, 0x0A }));
        Assert.Throws<InvalidDataException>(() => CipLzma.Decompress(Encoding.ASCII.GetBytes("not a container at all")));
    }

    /// <summary>Random corruption of a real sheet: only InvalidDataException or a successful (checksummed) decode is allowed.</summary>
    [Fact]
    public void FuzzedSheetsFailSafely()
    {
        byte[] original = RealSheetBytes(out var sheet);
        var random = new Random(20261012);
        int rejected = 0, decoded = 0;
        for (int i = 0; i < 150; i++)
        {
            var data = (byte[])original.Clone();
            int flips = 1 + random.Next(8);
            for (int f = 0; f < flips; f++) data[random.Next(data.Length)] ^= (byte)(1 << random.Next(8));
            var watch = Stopwatch.StartNew();
            try
            {
                var bmp = CipLzma.Decompress(data);
                BmpDecoder.Decode(bmp);
                decoded++;
            }
            catch (InvalidDataException)
            {
                rejected++;
            }
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"iteration {i} took {watch.Elapsed}");
        }
        output.WriteLine($"{rejected} rejected, {decoded} decoded (bit flips can land in padding or still form a valid stream).");
        // The sheet file name carries the bitmap's SHA-256, so a decode of corrupted data never reaches callers unnoticed:
        string path = Path.Combine(TestData.NewTempDirectory("fuzzsheet"), sheet.FileName);
        var corrupted = (byte[])original.Clone();
        corrupted[corrupted.Length / 2] ^= 0xFF; // inside the payload (the last bytes can be range-coder slack)
        File.WriteAllBytes(path, corrupted);
        Assert.Throws<InvalidDataException>(() => SpriteSheetDecoder.DecodeSheet(path));
    }

    [Fact]
    public void SheetWithWrongChecksumNameIsRejected()
    {
        byte[] original = RealSheetBytes(out _);
        string dir = TestData.NewTempDirectory("checksum");
        string wrongName = Path.Combine(dir, "sprites-" + new string('a', 64) + ".bmp.lzma");
        File.WriteAllBytes(wrongName, original);
        var ex = Assert.Throws<InvalidDataException>(() => SpriteSheetDecoder.DecodeSheet(wrongName));
        Assert.Contains("checksum", ex.Message);
        // A sheet without a hash in its name (e.g. a renamed copy) is decoded normally.
        string plainName = Path.Combine(dir, "sheet.bmp.lzma");
        File.WriteAllBytes(plainName, original);
        Assert.Equal(TibiaFormat.SheetSize, SpriteSheetDecoder.DecodeSheet(plainName).Width);
    }

    private static IEnumerable<byte> Varint(long value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            yield return value != 0 ? (byte)(b | 0x80) : b;
        } while (value != 0);
    }

    // ------------------------------------------------------------------ BMP and sheet layout

    private static byte[] Bmp(int width, int height, int bpp = 32, int compression = 3, int? pixelOffset = null, int? headerSize = null, int pixelBytes = -1)
    {
        // The buffer is sized from the absolute dimensions so even nonsense headers can be written.
        int stride = ((Math.Abs(width) * bpp + 31) / 32) * 4;
        int header = headerSize ?? 108;
        int offset = pixelOffset ?? 14 + header;
        int size = pixelBytes >= 0 ? pixelBytes : (int)Math.Min(1 << 20, (long)stride * Math.Abs(height));
        var data = new byte[Math.Max(54, 14 + header) + size];
        data[0] = (byte)'B'; data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), offset);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), header);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), height);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(28), (ushort)bpp);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(30), compression);
        if (header >= 56)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(54), 0x00FF0000);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(58), 0x0000FF00);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(62), 0x000000FF);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(66), 0xFF000000);
        }
        return data;
    }

    [Fact]
    public void ValidSyntheticBitmapDecodes()
    {
        var image = BmpDecoder.Decode(Bmp(8, 4));
        Assert.Equal(8, image.Width);
        Assert.Equal(4, image.Height);
    }

    [Theory]
    [InlineData(0, 4, 32, 3, null, null, -1, "size")]
    [InlineData(-8, 4, 32, 3, null, null, -1, "size")]
    [InlineData(100000, 4, 32, 3, null, null, -1, "size")]
    [InlineData(8, 4, 16, 3, null, null, -1, "bit depth")]
    [InlineData(8, 4, 32, 1, null, null, -1, "compression")]
    [InlineData(8, 4, 32, 3, -5, null, -1, "offset")]
    [InlineData(8, 4, 32, 3, 9_999_999, null, -1, "offset")]
    [InlineData(8, 4, 32, 3, null, 12, -1, "header")]
    [InlineData(8, 4, 32, 3, null, null, 10, "Truncated")]
    public void MalformedBitmapsAreRejected(int width, int height, int bpp, int compression, int? offset, int? headerSize, int pixelBytes, string expected)
    {
        var ex = Assert.Throws<InvalidDataException>(() => BmpDecoder.Decode(Bmp(width, height, bpp, compression, offset, headerSize, pixelBytes)));
        Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SheetLayoutIsValidatedAgainstTheCatalog()
    {
        var sheet = new SpriteSheetInfo("test.bmp.lzma", SpriteSheetType.Large, 100, 135, 0);
        Assert.Equal(36, SpriteSheetDecoder.Slice(new RgbaImage(384, 384), sheet).Length);
        // Not a multiple of the sprite size: a changed layout must be reported, not sliced wrongly.
        Assert.Throws<InvalidDataException>(() => SpriteSheetDecoder.Slice(new RgbaImage(400, 384), sheet));
        // More sprites in the catalog range than cells in the image.
        Assert.Throws<InvalidDataException>(() => SpriteSheetDecoder.Slice(new RgbaImage(128, 128), sheet));
        // A larger sheet (possible future layout) is sliced by its real grid.
        var bigger = new SpriteSheetInfo("big.bmp.lzma", SpriteSheetType.Small, 0, 255, 0);
        Assert.Equal(256, SpriteSheetDecoder.Slice(new RgbaImage(512, 512), bigger).Length);
    }

    // ------------------------------------------------------------------ catalog-content.json

    private static string WriteCatalog(string json)
    {
        string dir = TestData.NewTempDirectory("catalog");
        File.WriteAllText(Path.Combine(dir, TibiaFormat.AssetCatalogFile), json);
        return dir;
    }

    [Fact]
    public void CatalogSkipsAndReportsEntriesItCannotUnderstand()
    {
        string dir = WriteCatalog("""
            [
              { "type": "appearances", "file": "appearances-1.dat" },
              { "type": "sprite", "file": "a.bmp.lzma", "spritetype": 0, "firstspriteid": 0, "lastspriteid": 143, "area": 1 },
              { "type": "sprite", "file": "b.bmp.lzma", "spritetype": "3", "firstspriteid": "144", "lastspriteid": "179" },
              { "type": "sprite", "file": "c.bmp.lzma", "spritetype": 7, "firstspriteid": 180, "lastspriteid": 200 },
              { "type": "sprite", "file": "d.bmp.lzma", "spritetype": 0, "firstspriteid": 300, "lastspriteid": 200 },
              { "type": "sprite", "file": "e.bmp.lzma", "spritetype": 0, "firstspriteid": 150, "lastspriteid": 160 },
              { "type": "sprite", "file": "..\\escape.bmp.lzma", "spritetype": 0, "firstspriteid": 400, "lastspriteid": 410 },
              { "type": "sprite", "file": "C:\\absolute.bmp.lzma", "spritetype": 0, "firstspriteid": 500, "lastspriteid": 510 },
              { "type": "sprite", "file": "f.bmp.lzma", "spritetype": 0, "firstspriteid": 99999999999, "lastspriteid": 1 },
              { "type": "newthing", "file": "new.bin" },
              { "type": "appearances", "file": "appearances-2.dat" },
              42
            ]
            """);
        var catalog = AssetCatalog.Load(dir);
        foreach (string p in catalog.Problems) output.WriteLine(p);
        Assert.Equal(new[] { 0, 144 }, catalog.Sheets.Select(s => s.FirstSpriteId));
        Assert.Equal(SpriteSheetType.Large, catalog.Sheets[1].Type);
        Assert.Equal("appearances-1.dat", catalog.Files["appearances"]);
        Assert.Equal(1, catalog.UnusedTypes["newthing"]);
        Assert.Contains(catalog.Problems, p => p.Contains("unknown sprite type 7"));
        Assert.Contains(catalog.Problems, p => p.Contains("invalid sprite range"));
        Assert.Contains(catalog.Problems, p => p.Contains("overlaps"));
        Assert.Contains(catalog.Problems, p => p.Contains("unsafe file name") && p.Contains("escape"));
        Assert.Contains(catalog.Problems, p => p.Contains("unsafe file name") && p.Contains("absolute"));
        Assert.Contains(catalog.Problems, p => p.Contains("listed more than once"));
        Assert.Contains(catalog.Problems, p => p.Contains("not an object"));
        Assert.Equal("b.bmp.lzma", catalog.FindSheet(155)!.FileName); // the overlapping sheet "e" was rejected; "b" keeps its range
        Assert.Equal(64, catalog.ContentSha256.Length);
    }

    [Theory]
    [InlineData("{ \"sprites\": [] }", "unsupported structure")]
    [InlineData("[ { \"type\": ", "not valid JSON")]
    [InlineData("", "not valid JSON")]
    public void CatalogWithChangedStructureIsRejectedClearly(string json, string expected)
    {
        var ex = Assert.Throws<InvalidDataException>(() => AssetCatalog.Load(WriteCatalog(json)));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void ClientPathsSurviveAChangedCatalogAndManifest()
    {
        string package = TestData.NewTempDirectory("package");
        string assets = Path.Combine(package, TibiaFormat.AssetsFolder);
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(assets, TibiaFormat.AssetCatalogFile), "{ \"not\": \"an array\" }");
        File.WriteAllText(Path.Combine(assets, "map-abc.dat"), "x");
        var client = new TibiaClientPaths(package);
        // Falls back to the file scan instead of throwing.
        Assert.EndsWith("map-abc.dat", client.FindAssetFile(TibiaFormat.CatalogTypeMap));
        Assert.Null(client.ReadClientVersion());
        File.WriteAllText(Path.Combine(package, TibiaFormat.PackageManifest), "{ broken");
        Assert.Null(client.ReadClientVersion());
        File.WriteAllText(Path.Combine(package, TibiaFormat.PackageManifest), "{ \"version\": 15 }");
        Assert.Null(client.ReadClientVersion());
        File.WriteAllText(Path.Combine(package, TibiaFormat.PackageManifest), "{ \"version\": \"16.00.test\", \"files\": [] }");
        Assert.Equal("16.00.test", client.ReadClientVersion());
    }

    /// <summary>Random mutations of a valid catalog: only InvalidDataException or a parsed catalog (with problems) is allowed.</summary>
    [Fact]
    public void FuzzedCatalogsFailSafely()
    {
        string valid = """[{"type":"sprite","file":"a.bmp.lzma","spritetype":0,"firstspriteid":0,"lastspriteid":143},{"type":"map","file":"map-1.dat"}]""";
        var random = new Random(7);
        const string alphabet = "[]{}\":,0123456789-abcdefgnull ";
        for (int i = 0; i < 300; i++)
        {
            var chars = valid.ToCharArray();
            for (int k = 0; k < 1 + random.Next(4); k++) chars[random.Next(chars.Length)] = alphabet[random.Next(alphabet.Length)];
            string dir = WriteCatalog(new string(chars));
            try { AssetCatalog.Load(dir); }
            catch (InvalidDataException) { }
        }
    }

    [Fact]
    public void LargeCatalogParsesQuickly()
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < 200_000; i++)
            sb.Append(i == 0 ? "" : ",").Append($"{{\"type\":\"sprite\",\"file\":\"s{i}.bmp.lzma\",\"spritetype\":3,\"firstspriteid\":{i * 36},\"lastspriteid\":{i * 36 + 35}}}");
        sb.Append(']');
        string dir = WriteCatalog(sb.ToString());
        var watch = Stopwatch.StartNew();
        var catalog = AssetCatalog.Load(dir);
        output.WriteLine($"200,000 entries parsed in {watch.ElapsedMilliseconds} ms");
        Assert.Equal(200_000, catalog.SheetCount);
        Assert.Equal(199_999 * 36 + 35, catalog.FindSheet(7_199_999)!.LastSpriteId);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    // ------------------------------------------------------------------ file safety

    [Theory]
    [InlineData("map-1.dat", true)]
    [InlineData("sprites-abc.bmp.lzma", true)]
    [InlineData("..", false)]
    [InlineData("..\\x.dat", false)]
    [InlineData("a/b.dat", false)]
    [InlineData("C:\\x.dat", false)]
    [InlineData("x.dat ", false)]
    [InlineData("name.", false)]
    [InlineData("", false)]
    public void DataSuppliedFileNamesAreValidated(string name, bool safe) =>
        Assert.Equal(safe, TibiaFormat.SafeFileName(name) != null);

    [Fact]
    public void ResolveUnderNeverLeavesTheRoot()
    {
        string root = TestData.NewTempDirectory("root");
        Assert.NotNull(TibiaFormat.ResolveUnder(root, Path.Combine("assets", "x.dat")));
        Assert.Null(TibiaFormat.ResolveUnder(root, Path.Combine("..", "x.dat")));
        Assert.Null(TibiaFormat.ResolveUnder(root, Path.Combine(root, "x.dat")));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("nul.gif", "_nul.gif")]
    [InlineData("com1.txt", "_com1.txt")]
    [InlineData("Console", "Console")]
    [InlineData("a:b*c?", "a_b_c_")]
    [InlineData("  trailing.  ", "trailing")]
    [InlineData("CONIN$", "_CONIN$")]
    public void FileNamesAvoidWindowsDeviceNames(string input, string expected) => Assert.Equal(expected, FileNames.Sanitize(input));

    [Fact]
    public void WindowsNameRulesAreTheSameForFilesFoldersPathsAndCatalogs()
    {
        // 7.2: one set of rules (WindowsNames) for file names, output folders, paths from archives and catalog folders.
        string root = TestData.NewTempDirectory("names");
        foreach (string device in new[] { "CON", "nul", "Com1", "lpt9.txt", "CONIN$", "conout$.log" })
        {
            Assert.StartsWith("_", FileNames.Sanitize(device));
            Assert.Null(PathGuard.SafeRelativePath(device));
            Assert.False(OutputLocations.Check(Path.Combine(root, device)).Usable, device);
            string folder = Core.Items.ItemCatalogStore.FolderFor(root, device);
            Assert.Equal("_" + device, Path.GetFileName(folder));
            Core.Items.ItemCatalogStore.Create(folder, device);   // a catalog named like a device gets a usable folder
            Assert.Equal(device, Core.Items.ItemCatalogStore.Load(folder).Name);
        }
        foreach (string fine in new[] { "Console", "Tibia 15.33", "comic", "con-artist" })
        {
            Assert.Equal(fine, FileNames.Sanitize(fine));
            Assert.NotNull(PathGuard.SafeRelativePath(fine));
            Assert.True(OutputLocations.Check(Path.Combine(root, fine)).Usable, fine);
            Assert.Equal(fine, Path.GetFileName(Core.Items.ItemCatalogStore.FolderFor(root, fine)));
        }
    }

    [Fact]
    public void LongFileNamesAreShortened() => Assert.Equal(FileNames.MaxLength, FileNames.Sanitize(new string('x', 400)).Length);

    [Fact]
    public void MapTilesWithImpossibleCoordinatesAreSkipped()
    {
        // Audit A20: tile names with digit runs too long for an int used to crash Build HR.
        string dir = TestData.NewTempDirectory("tiles");
        foreach (string name in new[] { "minimap_16_31744_30976_7.png", "minimap_16_99999999999_30976_7.png", "minimap_16_31744_30976_99999999999.png", "minimap_8_31744_30976_7.png", "satellite_16_32000_31000_6.png", "notes.png" })
            File.WriteAllBytes(Path.Combine(dir, name), []);
        var tiles = TibiaFoundry.Core.Map.MapImageTools.FindTiles(dir, TibiaFoundry.Core.Map.HrMapType.Minimap, 16);
        var tile = Assert.Single(tiles);
        Assert.Equal((31744, 30976, 7), (tile.X, tile.Y, tile.Z));
    }

    [Fact]
    public void AtomicWriteNeverLeavesPartialFiles()
    {
        string dir = TestData.NewTempDirectory("atomic");
        string target = Path.Combine(dir, "out.txt");
        File.WriteAllText(target, "original");
        Assert.Throws<IOException>(() => AtomicFile.Write(target, temp =>
        {
            File.WriteAllText(temp, "half written");
            throw new IOException("disk full (simulated)");
        }));
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(dir)); // no temporary file left behind

        AtomicFile.WriteAllText(target, "new content");
        Assert.Equal("new content", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(dir));
    }
}
