using System.Buffers.Binary;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Rcc;

namespace TibiaFoundry.Tests;

/// <summary>LZMA encoder, the client's LZMA container and sheet BMPs, and the RCC Editor's Qt resource format.</summary>
public class RccAndCompressionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> LzmaInputs => new() { "empty", "one", "run", "text", "random", "pixels" };

    private static byte[] Input(string kind)
    {
        var random = new Random(1234);
        switch (kind)
        {
            case "empty": return [];
            case "one": return [42];
            case "run": return Enumerable.Repeat((byte)7, 100_000).ToArray();
            case "text": return System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 3000).Select(i => $"line {i} of a repetitive text\n")));
            case "random":
                var r = new byte[70_000];
                random.NextBytes(r);
                return r;
            default:
                return BmpEncoder.Encode(SyntheticAssets.Sprite(3, 64, 64));
        }
    }

    [Theory]
    [MemberData(nameof(LzmaInputs))]
    public void LzmaEncoderOutputDecodesToTheInput(string kind)
    {
        byte[] input = Input(kind);
        byte[] payload = LzmaEncoder.Encode(input, ct: Ct);
        byte[] decoded = LzmaDecoder.Decode(LzmaEncoder.TibiaProperties, payload, input.Length);
        Assert.Equal(input, decoded);
        // The end marker lets decoders without the size stop at the right place.
        Assert.Equal(input, LzmaDecoder.Decode(LzmaEncoder.TibiaProperties, payload, -1));
    }

    [Fact]
    public void ContainerUsesTheClientLayout()
    {
        byte[] input = Input("pixels");
        byte[] container = CipLzma.Compress(input, Ct);
        Assert.True(CipLzma.TryReadHeader(container, out var header));
        Assert.Equal(input.Length, header.DecompressedSize);
        Assert.Equal(container.Length - 32, header.CompressedSize);
        Assert.Equal(45, header.PayloadOffset);
        Assert.Equal(container.Length - 45, BinaryPrimitives.ReadInt64LittleEndian(container.AsSpan(37)));
        Assert.Equal(input, CipLzma.Decompress(container));
        Assert.Equal(FileSignatures.CipLzma, FileSignatures.Identify(container).Format);
    }

    [Fact]
    public void ClientSheetsReEncodeByteForByte()
    {
        string assets = TestData.RequireClientAssets();
        var catalog = Core.Assets.AssetCatalog.Load(assets);
        foreach (var sheet in catalog.Sheets.Where((_, i) => i % 997 == 0).Take(6))
        {
            byte[] bmp = CipLzma.DecompressFile(catalog.GetSheetPath(sheet));
            Assert.Equal(bmp, BmpEncoder.Encode(BmpDecoder.Decode(bmp)));
            Assert.Equal(bmp, CipLzma.Decompress(CipLzma.Compress(bmp, Ct)));
        }
    }

    [Theory]
    [InlineData("", 0u)]
    [InlineData("a", 0x61u)]
    [InlineData("images", 0x7037DC3u)]
    public void QtHashMatchesQt(string name, uint expected) => Assert.Equal(expected, RccFormat.QtHash(name));

    private static RccArchive Sample(int version = 3)
    {
        var a = RccArchive.Create(version);
        a.Add("/images/a.png", ImageIO.EncodePng(new RgbaImage(4, 4)), RccCompressionChoice.Never);
        a.Add("/images/skin/a.png", ImageIO.EncodePng(new RgbaImage(8, 2)), RccCompressionChoice.Never);
        a.Add("/data/messages.json", System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("{\"text\":\"hello\"},", 200))), RccCompressionChoice.QtDefault);
        a.Add("/root.txt", "plain"u8.ToArray(), RccCompressionChoice.Never, lastModified: 946681200000);
        return a;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ArchivesRoundTripInEveryFormatVersion(int version)
    {
        var a = Sample(version);
        byte[] built = a.Build();
        var report = RccValidator.Validate(built);
        Assert.True(report.IsValid, string.Join(" | ", report.Errors));
        Assert.Equal(4, report.FoundByQtLookup);
        var b = RccArchive.Parse(built);
        Assert.Equal(version, b.FormatVersion);
        foreach (var e in a.Entries)
        {
            var r = b.Find(e.Path)!;
            Assert.Equal(e.GetContent(), r.GetContent());
            Assert.Equal(e.Compression, r.Compression);
            Assert.Equal(version >= 2 ? e.LastModified : 0, r.LastModified);
        }
        Assert.Equal(RccCompression.Zlib, b.Find("/data/messages.json")!.Compression);
        Assert.Equal(version >= 3 ? 1u : 0u, version >= 3 ? BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(20)) : 0u);
    }

    [Fact]
    public void ValidatorFindsDamage()
    {
        byte[] good = Sample().Build();
        // Truncation.
        Assert.False(RccValidator.Validate(good[..(good.Length - 10)]).IsValid);
        // A wrong stored name hash makes the entry unfindable for Qt.
        byte[] badHash = (byte[])good.Clone();
        int names = BinaryPrimitives.ReadInt32BigEndian(good.AsSpan(16));
        badHash[names + 5] ^= 0x55;
        var report = RccValidator.Validate(badHash);
        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("hash", StringComparison.Ordinal));
        // Not an rcc file at all.
        Assert.False(RccValidator.Validate("PNG not rcc"u8.ToArray()).IsValid);
    }

    /// <summary>
    /// Qt finds files by a binary search over each folder's children sorted by name hash. Children
    /// in the wrong order leave every name and hash intact, yet Qt cannot find some files: only the
    /// lookup check notices.
    /// </summary>
    [Fact]
    public void ValidatorFindsFilesQtCannotLookUp()
    {
        var archive = RccArchive.Create(3);
        foreach (string name in new[] { "a.txt", "b.txt", "c.txt", "d.txt", "e.txt", "f.txt" })
            archive.Add("/" + name, System.Text.Encoding.ASCII.GetBytes(name), RccCompressionChoice.Never);
        byte[] good = archive.Build();
        Assert.True(RccValidator.Validate(good).IsValid);

        // Reverse the order of the root's children (node records of 22 bytes in format 3).
        byte[] bad = (byte[])good.Clone();
        int tree = BinaryPrimitives.ReadInt32BigEndian(good.AsSpan(8));
        const int nodeSize = 22;
        int count = BinaryPrimitives.ReadInt32BigEndian(good.AsSpan(tree + 6));
        int first = BinaryPrimitives.ReadInt32BigEndian(good.AsSpan(tree + 10));
        Assert.Equal(6, count);
        for (int i = 0; i < count; i++)
            good.AsSpan(tree + (first + count - 1 - i) * nodeSize, nodeSize).CopyTo(bad.AsSpan(tree + (first + i) * nodeSize));

        var report = RccValidator.Validate(bad);
        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("Qt's lookup", StringComparison.Ordinal));
        Assert.True(report.FoundByQtLookup < count);
    }

    [Fact]
    public void EditsAreKeptAndPathsValidated()
    {
        var a = Sample();
        var png = a.Find("/images/a.png")!;
        a.Replace(png, ImageIO.EncodePng(new RgbaImage(6, 6)));
        Assert.Equal(RccCompression.None, png.Compression);
        a.Rename(a.Find("/root.txt")!, "/moved/root.txt");
        Assert.Throws<ArgumentException>(() => a.Add("/images/a.png", [1]));
        Assert.Throws<ArgumentException>(() => a.Add("/images", [1]));
        Assert.Throws<ArgumentException>(() => a.Add("/../x", [1]));
        a.Remove(a.Find("/images/skin/a.png")!);
        var b = RccArchive.Parse(a.Build());
        Assert.Equal(3, b.Entries.Count);
        Assert.Equal((6, 6), RccOperations.ImageSize(b.Find("/images/a.png")!.GetContent()));
        Assert.NotNull(b.Find("/moved/root.txt"));
        Assert.Contains(RccOperations.CheckReplacement(b.Find("/images/a.png")!, ImageIO.EncodePng(new RgbaImage(5, 5))), w => w.Contains("5×5", StringComparison.Ordinal));
        Assert.Contains(RccOperations.CheckReplacement(b.Find("/images/a.png")!, "GIF89a\0\0\0\0"u8.ToArray()), w => w.Contains("GIF", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtractionKeepsFoldersSoEqualNamesNeverCollide()
    {
        var a = Sample();
        string dir = TestData.NewTempDirectory("rcc-extract");
        using var output = new OutputSession("extract", FileSafetyOptions.Default with { WhenExists = ExistingFilePolicy.Overwrite });
        var result = RccOperations.Extract(a.Entries, dir, output, ct: Ct);
        Assert.Equal(4, result.Written);
        Assert.True(File.Exists(Path.Combine(dir, "images", "a.png")));
        Assert.True(File.Exists(Path.Combine(dir, "images", "skin", "a.png")));
        Assert.True(File.Exists(Path.Combine(dir, RccOperations.ManifestFileName)));

        // Editing an extracted file and importing the folder replaces exactly that resource.
        File.WriteAllBytes(Path.Combine(dir, "images", "skin", "a.png"), ImageIO.EncodePng(new RgbaImage(8, 2) { [0, 0] = 0xFF00FF00 }));
        File.WriteAllText(Path.Combine(dir, "new.txt"), "added");
        var import = RccOperations.ImportFolder(a, dir, addNew: false, RccCompressionChoice.Automatic, Ct);
        Assert.Equal(["/images/skin/a.png"], import.Replaced);
        Assert.Equal(3, import.Unchanged);
        Assert.Contains(import.Skipped, s => s.StartsWith("new.txt", StringComparison.Ordinal));
        var added = RccOperations.ImportFolder(a, dir, addNew: true, RccCompressionChoice.Automatic, Ct);
        Assert.Equal(["/new.txt"], added.Added);
    }

    [Fact]
    public void InstalledClientResourceFileRoundTrips()
    {
        // The installed client (read only): the pinned test copy has no program folder.
        string path = Path.Combine(Core.Assets.TibiaClientPaths.ForPackage(Core.Assets.TibiaClientPaths.LivePackageName).PackageDirectory, "bin", "graphics_resources.rcc");
        Assert.SkipUnless(File.Exists(path), "The installed client has no graphics_resources.rcc.");
        byte[] original = File.ReadAllBytes(path);
        var report = RccValidator.Validate(original);
        Assert.True(report.IsValid, string.Join(" | ", report.Errors));
        Assert.Equal(report.Files, report.FoundByQtLookup);
        var archive = RccArchive.Parse(original);
        byte[] rebuilt = archive.Build();
        var rebuiltReport = RccValidator.Validate(rebuilt);
        Assert.True(rebuiltReport.IsValid, string.Join(" | ", rebuiltReport.Errors));
        var again = RccArchive.Parse(rebuilt);
        Assert.Equal(archive.Entries.Count, again.Entries.Count);
        foreach (var e in archive.Entries)
        {
            var r = again.Find(e.Path)!;
            Assert.Equal(e.Stored, r.Stored);
            Assert.Equal((e.Compression, e.LastModified, e.Territory, e.Language), (r.Compression, r.LastModified, r.Territory, r.Language));
        }
    }
}
