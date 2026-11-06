using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Library;
using TibiaFoundry.Core.Protobuf;
using Appearances = Tibia.Protobuf.Appearances.Appearances;

namespace TibiaFoundry.Tests;

/// <summary>
/// The 8.8.1 performance changes give the same results as before: the Latin-1 repair writes the same
/// bytes as the 8.8.0 algorithm, folder listings answer like the file system, verified writes and the
/// resource-file search behave as they did, and a search reports the same number of matches.
/// </summary>
public sealed class PerformanceOptimizationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------ Latin-1 repair

    /// <summary>The repair of 8.8.0 (a new array and stream per nesting level), kept as the reference.</summary>
    private static byte[] ReferenceRepair(MessageDescriptor descriptor, byte[] data, ref int fixedStrings)
    {
        var strict = new UTF8Encoding(false, true);
        var input = new CodedInputStream(data);
        using var ms = new MemoryStream();
        var output = new CodedOutputStream(ms);
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            var field = descriptor.FindFieldByNumber(WireFormat.GetTagFieldNumber(tag));
            output.WriteTag(tag);
            switch (WireFormat.GetTagWireType(tag))
            {
                case WireFormat.WireType.Varint: output.WriteUInt64(input.ReadUInt64()); break;
                case WireFormat.WireType.Fixed64: output.WriteFixed64(input.ReadFixed64()); break;
                case WireFormat.WireType.Fixed32: output.WriteFixed32(input.ReadFixed32()); break;
                case WireFormat.WireType.LengthDelimited:
                    byte[] content = input.ReadBytes().ToByteArray();
                    if (field?.FieldType == FieldType.String)
                    {
                        try { strict.GetString(content); }
                        catch (DecoderFallbackException) { fixedStrings++; content = Encoding.UTF8.GetBytes(Encoding.Latin1.GetString(content)); }
                    }
                    else if (field?.FieldType == FieldType.Message) content = ReferenceRepair(field.MessageType, content, ref fixedStrings);
                    output.WriteBytes(ByteString.CopyFrom(content));
                    break;
                default: throw new InvalidDataException("wire type");
            }
        }
        output.Flush();
        return ms.ToArray();
    }

    /// <summary>An appearances file with objects whose names are Latin-1 (as in the client), nested messages and every wire type.</summary>
    private static byte[] LatinAppearances()
    {
        var file = new Appearances();
        for (uint id = 100; id < 160; id++)
        {
            var a = new Tibia.Protobuf.Appearances.Appearance { Id = id, Name = $"name {id}" };
            a.FrameGroup.Add(new Tibia.Protobuf.Appearances.FrameGroup
            {
                SpriteInfo = new Tibia.Protobuf.Appearances.SpriteInfo { PatternWidth = 1, PatternHeight = 1, PatternDepth = 1, Layers = 1, SpriteId = { id * 3, id * 3 + 1 } },
            });
            file.Object.Add(a);
        }
        byte[] valid = file.ToByteArray();
        // "name 1xy" -> "nam\xEE 1xy": the same length, no longer valid UTF-8 (byte 0xEE is 'î' in Latin-1).
        for (int i = 0; i + 4 < valid.Length; i++)
            if (valid[i] == (byte)'n' && valid[i + 1] == (byte)'a' && valid[i + 2] == (byte)'m' && valid[i + 3] == (byte)'e' && valid[i + 5] == (byte)'1' && valid[i + 7] % 3 == 0)
                valid[i + 3] = 0xEE;
        return valid;
    }

    [Fact]
    public void Latin1RepairWritesTheSameBytesAsBefore()
    {
        byte[] data = LatinAppearances();
        Assert.ThrowsAny<InvalidProtocolBufferException>(() => Appearances.Parser.ParseFrom(data));
        int expectedFixed = 0;
        byte[] expected = ReferenceRepair(Appearances.Descriptor, data, ref expectedFixed);
        byte[] repaired = Utf8Repair.Repair(Appearances.Descriptor, data, out int fixedStrings);
        Assert.True(expectedFixed > 0);
        Assert.Equal(expectedFixed, fixedStrings);
        Assert.Equal(expected, repaired);
        var parsed = Appearances.Parser.ParseFrom(repaired);
        Assert.Contains(parsed.Object, o => o.Name.StartsWith("namî 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Latin1RepairOfTheClientsAppearancesMatchesTheReference()
    {
        TestData.RequireClientAssets();
        string file = Directory.GetFiles(TestData.Client.AssetsDirectory, "appearances-*.dat")[0];
        byte[] data = File.ReadAllBytes(file);
        int expectedFixed = 0;
        byte[] expected = ReferenceRepair(Appearances.Descriptor, data, ref expectedFixed);
        Assert.Equal(expected, Utf8Repair.Repair(Appearances.Descriptor, data, out int fixedStrings));
        Assert.Equal(expectedFixed, fixedStrings);
    }

    [Fact]
    public void DamagedWireDataIsReportedAsUndecodable()
    {
        byte[] data = LatinAppearances();
        byte[] truncated = data[..(data.Length - 3)];
        Assert.Throws<InvalidDataException>(() => Utf8Repair.Repair(Appearances.Descriptor, truncated, out _));
        var schema = SchemaInspector.Inspect(Appearances.Descriptor, truncated);
        Assert.True(schema.Truncated);
        Assert.False(SchemaInspector.Inspect(Appearances.Descriptor, data).Truncated);
    }

    [Fact]
    public void SchemaInspectionStillReportsUnknownFields()
    {
        var file = new Appearances();
        file.Object.Add(new Tibia.Protobuf.Appearances.Appearance { Id = 7 });
        using var ms = new MemoryStream();
        var output = new CodedOutputStream(ms);
        file.WriteTo(output);
        output.WriteTag(900, WireFormat.WireType.LengthDelimited);  // a field the schema does not know
        output.WriteString("new client data");
        output.WriteTag(901, WireFormat.WireType.Fixed32);
        output.WriteFixed32(5);
        output.Flush();
        var report = SchemaInspector.Inspect(Appearances.Descriptor, ms.ToArray());
        Assert.False(report.Truncated);
        Assert.Contains(report.Drift, d => d.FieldNumber == 900 && d.Sample == "'new client data'");
        Assert.Contains(report.Drift, d => d.FieldNumber == 901 && d.Sample == "5");
    }

    // ------------------------------------------------------------------ folder listings

    [Fact]
    public void AListedFolderAnswersLikeTheFileSystem()
    {
        string dir = TestData.NewTempDirectory("listing");
        File.WriteAllText(Path.Combine(dir, "sprites-abc.bmp.lzma"), "x");
        Directory.CreateDirectory(Path.Combine(dir, "folder.dat"));
        var listed = PhysicalClientFolder.WithListing(dir);
        var plain = new PhysicalClientFolder(dir);
        Assert.True(listed.IsListed);
        foreach (string name in new[] { "sprites-abc.bmp.lzma", "SPRITES-ABC.BMP.LZMA", "missing.dat", "folder.dat", "sprites-abc.bmp.lzma." })
            Assert.Equal(plain.PathOf(name), listed.PathOf(name));
        File.WriteAllText(Path.Combine(dir, "later.dat"), "x");
        Assert.Null(listed.PathOf("later.dat"));               // a listing is a moment in time
        Assert.NotNull(plain.PathOf("later.dat"));
        Assert.False(PhysicalClientFolder.WithListing(Path.Combine(dir, "nope")).IsListed);
    }

    // ------------------------------------------------------------------ verified writes

    [Fact]
    public void VerifiedWritesReplaceAtomicallyAndLeaveNoTemporaryFiles()
    {
        string dir = TestData.NewTempDirectory("verified");
        string path = Path.Combine(dir, "sub", "file.png");
        AtomicFile.WriteVerified(path, "first"u8, () => new IOException("mismatch"));
        AtomicFile.WriteVerified(path, "second, longer"u8, () => new IOException("mismatch"));
        AtomicFile.WriteVerified(Path.Combine(dir, "empty.bin"), [], () => new IOException("mismatch"));
        Assert.Equal("second, longer", File.ReadAllText(path));
        Assert.Empty(File.ReadAllBytes(Path.Combine(dir, "empty.bin")));
        Assert.Equal(["file.png"], Directory.GetFiles(Path.Combine(dir, "sub")).Select(Path.GetFileName));
    }

    // ------------------------------------------------------------------ resource files

    [Fact]
    public void TheResourceFileSearchFindsTheSameFiles()
    {
        string package = TestData.NewTempDirectory("rcc");
        Directory.CreateDirectory(Path.Combine(package, "bin", "plugins"));
        Directory.CreateDirectory(Path.Combine(package, "screenshots"));
        File.WriteAllBytes(Path.Combine(package, "bin", "graphics_resources.rcc"), [.. "qres"u8, 0, 0, 0, 3]);
        File.WriteAllBytes(Path.Combine(package, "bin", "plugins", "other.dat"), [.. "qres"u8, 1]);
        File.WriteAllBytes(Path.Combine(package, "bin", "image.png"), [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2]);
        File.WriteAllBytes(Path.Combine(package, "bin", "old.rcc-bak"), [.. "qres"u8, 1]);
        File.WriteAllBytes(Path.Combine(package, "screenshots", "personal.rcc"), [.. "qres"u8, 1]);
        File.WriteAllBytes(Path.Combine(package, "tiny"), "qr"u8.ToArray());
        Assert.Equal(["bin/graphics_resources.rcc", "bin/plugins/other.dat"], ClientScanner.FindResourceFiles(package));
        Assert.True(ClientScanner.HasResourceFile(package));
        File.Delete(Path.Combine(package, "bin", "graphics_resources.rcc"));
        File.Delete(Path.Combine(package, "bin", "plugins", "other.dat"));
        Assert.False(ClientScanner.HasResourceFile(package));
    }

    // ------------------------------------------------------------------ library

    [Fact]
    public void SearchCountsEveryMatchOnEveryPage()
    {
        string dir = TestData.NewTempDirectory("library-count");
        var lib = AssetLibrary.Open(Path.Combine(dir, "Library"));
        string files = Path.Combine(dir, "Files");
        for (int i = 0; i < 12; i++) AssetLibraryTests.Png(files, $"Items/{i}_item.png", AssetLibraryTests.Square(0xFF102030 + (uint)i));
        AssetLibraryTests.Png(files, "Misc/other.png", AssetLibraryTests.Square(0xFF00FF00));
        var result = AssetIndexer.IndexFolder(lib, files, new IndexOptions { MaxDegreeOfParallelism = 4 }, ct: Ct);
        Assert.Equal((13, 13), (result.Seen, result.Added));
        var page = lib.Search(new AssetQuery { Text = "item", Limit = 5, Offset = 5 });
        Assert.Equal(12, page.Total);
        Assert.Equal(5, page.Assets.Count);
        var beyond = lib.Search(new AssetQuery { Text = "item", Limit = 5, Offset = 50 });
        Assert.Equal(12, beyond.Total);
        Assert.Empty(beyond.Assets);
        Assert.Equal(0, lib.Search(new AssetQuery { Text = "nothing-matches" }).Total);
        // Problems and records keep the order of the files although they are inspected in parallel.
        var again = AssetIndexer.IndexFiles(lib, [Path.Combine(files, "Items", "missing.png"), Path.Combine(files, "Misc", "other.png")], new IndexOptions { Force = true }, ct: Ct);
        Assert.Equal((1, 1), (again.Problems.Count, again.Updated));
        AssetLibrary.ReleaseConnections();
    }
}
