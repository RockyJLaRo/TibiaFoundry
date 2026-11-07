using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using TibiaFoundry.Core.FileMetadata;
using TibiaFoundry.Core.FileMetadata.Providers;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Metadata;

namespace TibiaFoundry.Tests;

/// <summary>
/// 8.6: the Universal File Metadata tool's engine, against real files made by ffmpeg (Fixtures\Metadata)
/// and by the formats' own rules: reading, editing, verification after saving, preservation of every
/// other field, backups, read-only and changed files, locks, batch edits of mixed formats with
/// cancellation and partial failure, presets, comparison and CSV/JSON export.
/// </summary>
public sealed class FileMetadataTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly MetadataEngine Engine = new();
    private static readonly MetadataSaveOptions NoBackup = new();
    private const string Unicode = "Ünïcödé 日本語 Tibia — «test» 🎮";

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Metadata", name);

    /// <summary>A copy of a fixture in a new temporary folder (the fixtures themselves are never written).</summary>
    private static string Copy(string name, string? folder = null)
    {
        string dir = folder ?? TestData.NewTempDirectory("metadata");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.Copy(Fixture(name), path);
        return path;
    }

    private static byte[] Pixels(string path)
    {
        using var image = new ImageMagick.MagickImage(path);
        return image.GetPixels().ToByteArray(ImageMagick.PixelMapping.RGBA)!;
    }

    private static MetadataSaveResult Save(string path, params (string Key, string? Value)[] edits) =>
        Engine.Save(Engine.Load(path, Ct), edits.Select(e => new MetadataEdit(e.Key, e.Value)).ToList(), NoBackup, Ct);

    // ------------------------------------------------------------------ formats

    public static TheoryData<string> AudioFiles => new() { "sample.mp3", "sample.flac", "sample.ogg", "sample.opus", "sample.m4a", "sample.wav", "sample.aiff", "sample.wma", "sample.wv" };
    public static TheoryData<string> VideoFiles => new() { "sample.mp4", "sample.mkv", "sample.avi", "sample.mov", "sample.webm", "sample.wmv" };

    /// <summary>Other extensions of the same formats (each one is in the editable list, so each one is tested).</summary>
    public static TheoryData<string, string> AliasFiles => new()
    {
        { "sample.m4a", ".m4b" }, { "sample.mp4", ".m4v" }, { "sample.ogg", ".oga" }, { "sample.aiff", ".aif" }, { "sample.jpg", ".jpeg" },
        { "sample.jpg", ".jpe" }, { "sample.tif", ".tiff" }, { "sample.wma", ".asf" }, { "sample.webm", ".mka" },
    };

    [Theory]
    [MemberData(nameof(AliasFiles))]
    public void OtherExtensionsOfTheSameFormatsAreWrittenToo(string fixture, string extension)
    {
        string dir = TestData.NewTempDirectory("metadata");
        string path = Path.Combine(dir, "sample" + extension);
        File.Copy(Fixture(fixture), path);
        var before = Engine.Load(path, Ct);
        Assert.True(before.CanEdit, string.Join(" ", before.Errors.Concat(before.Warnings)));
        var result = Engine.Save(before, [new("title", "alias " + extension), new("copyright", "RockyLaRo")], NoBackup, Ct);
        Assert.True(result.Status == MetadataSaveStatus.Saved, result.Message);
        Assert.Equal("alias " + extension, Engine.Load(path, Ct).Value("title"));
    }
    public static TheoryData<string> ImageFiles => new() { "sample.jpg", "sample.png", "sample.gif", "sample.tif" };

    [Theory]
    [MemberData(nameof(AudioFiles))]
    public void AudioTagsAreWrittenReadBackAndTheAudioIsKept(string name)
    {
        string path = Copy(name);
        var before = Engine.Load(path, Ct);
        Assert.Empty(before.Errors);
        Assert.Equal("taglib", before.ProviderId);
        Assert.True(before.CanEdit, string.Join(" ", before.Warnings));
        string? duration = before.Value("duration");
        Assert.NotNull(duration);

        var result = Engine.Save(before,
        [
            new("title", Unicode), new("authors", "Roc ky; RockyLaRo"), new("album", "Tibia Sounds"), new("year", "2026"), new("track", "7"),
            new("genres", "Soundtrack"), new("copyright", "© 2026 RockyLaRo"), new("comment", "Line one\nLine two"),
        ], NoBackup, Ct);
        Assert.True(result.Status == MetadataSaveStatus.Saved, result.Message + " " + string.Join(" ", result.SkippedFields));

        var after = Engine.Load(path, Ct);
        Assert.Equal(Unicode, after.Value("title"));
        Assert.Equal(["Roc ky", "RockyLaRo"], MetadataValues.SplitList(after.Value("authors")));
        Assert.Equal("Tibia Sounds", after.Value("album"));
        Assert.Equal("2026", after.Value("year"));
        Assert.Equal("© 2026 RockyLaRo", after.Value("copyright"));
        Assert.Equal(duration, after.Value("duration"));   // the audio itself is untouched
        Assert.Equal(before.Value("sampleRate"), after.Value("sampleRate"));
    }

    [Theory]
    [MemberData(nameof(VideoFiles))]
    public void VideoTagsAreWrittenAndTheVideoIsKept(string name)
    {
        string path = Copy(name);
        var before = Engine.Load(path, Ct);
        Assert.True(before.CanEdit, string.Join(" ", before.Errors.Concat(before.Warnings)));
        var result = Engine.Save(before, [new("title", "Rotworm walk " + name), new("authors", "RockyLaRo")], NoBackup, Ct);
        Assert.True(result.Status == MetadataSaveStatus.Saved, result.Message);
        var after = Engine.Load(path, Ct);
        Assert.Equal("Rotworm walk " + name, after.Value("title"));
        Assert.Equal("RockyLaRo", after.Value("authors"));
        Assert.Equal(before.Value("width"), after.Value("width"));
        Assert.Equal(before.Value("height"), after.Value("height"));
        Assert.Equal(before.Value("duration"), after.Value("duration"));
    }

    [Theory]
    [MemberData(nameof(ImageFiles))]
    public void ImageMetadataIsWrittenWithoutReencoding(string name)
    {
        string path = Copy(name);
        var before = Engine.Load(path, Ct);
        Assert.True(before.CanEdit, string.Join(" ", before.Errors.Concat(before.Warnings)));
        var pixels = Pixels(path);
        var result = Engine.Save(before,
        [
            new("title", Unicode), new("description", "A rotworm, from the client."), new("keywords", "tibia; creature; rotworm"),
            new("authors", "RockyLaRo"), new("copyright", "Tibia graphics © CipSoft GmbH"),
        ], NoBackup, Ct);
        Assert.True(result.Status == MetadataSaveStatus.Saved, result.Message);
        var after = Engine.Load(path, Ct);
        Assert.Equal(Unicode, after.Value("title"));
        Assert.Equal(["tibia", "creature", "rotworm"], MetadataValues.SplitList(after.Value("keywords")));
        Assert.Equal("Tibia graphics © CipSoft GmbH", after.Value("copyright"));
        Assert.Equal(pixels, Pixels(path));   // identical pixels: only metadata was written
    }

    [Fact]
    public void JpegCameraFieldsDatesGpsAndRatingRoundTrip()
    {
        string path = Copy("sample.jpg");
        var result = Save(path, ("dateTaken", "2026-10-25 13:45:10"), ("rating", "4"), ("latitude", "52.520008"), ("longitude", "-13.404954"), ("orientation", "6"));
        Assert.True(result.Status == MetadataSaveStatus.Saved, result.Message);
        var after = Engine.Load(path, Ct);
        Assert.Equal("2026-10-25 13:45:10", after.Value("dateTaken"));
        Assert.Equal("4", after.Value("rating"));
        Assert.True(MetadataEngine.SameValue(MetadataFields.Latitude, "52.520008", after.Value("latitude")));
        Assert.True(MetadataEngine.SameValue(MetadataFields.Longitude, "-13.404954", after.Value("longitude")));
        Assert.Equal("6", after.Value("orientation"));

        // Removing GPS data is never silent: it is reported as a warning of the save.
        var removal = Save(path, ("latitude", null), ("longitude", null));
        Assert.Equal(MetadataSaveStatus.Saved, removal.Status);
        Assert.Contains(removal.Warnings, w => w.Contains("Latitude is removed", StringComparison.Ordinal));
        Assert.Null(Engine.Load(path, Ct).Value("latitude"));
    }

    [Theory]
    [InlineData("sample.bmp")]
    [InlineData("sample.webp")]
    [InlineData("sample.ico")]
    public void FormatsThatCannotBeWrittenSafelyAreReadOnly(string name)
    {
        string path = Copy(name);
        var s = Engine.Load(path, Ct);
        Assert.Empty(s.Errors);
        Assert.False(s.CanEdit);
        Assert.Equal("32", s.Value("width"));
        Assert.NotEmpty(s.Warnings);
        var result = Save(path, ("title", "x"));
        Assert.Equal(MetadataSaveStatus.Skipped, result.Status);
        Assert.Contains(result.SkippedFields, f => f.Contains("Title", StringComparison.Ordinal));
        Assert.Equal(File.ReadAllBytes(Fixture(name)), File.ReadAllBytes(path));
    }

    [Fact]
    public void TibiaFoundryAttributionInAPngSurvivesAnEdit()
    {
        string dir = TestData.NewTempDirectory("metadata");
        string path = Path.Combine(dir, "rotworm.png");
        File.WriteAllBytes(path, PngMetadata.Embed(File.ReadAllBytes(Fixture("sample.png")), Attribution.ForNewAsset("RockyLaRo")));
        var before = Engine.Load(path, Ct);
        string? attribution = before.Value(TagLibMetadataProvider.AttributionField.Key);
        Assert.Contains("RockyLaRo", attribution, StringComparison.Ordinal);
        Assert.Equal(MetadataSaveStatus.Saved, Save(path, ("title", "Rotworm"), ("keywords", "creature")).Status);
        Assert.Equal(attribution, Engine.Load(path, Ct).Value(TagLibMetadataProvider.AttributionField.Key));
        Assert.Contains("RockyLaRo", AttributionReader.Read(path)!.Creators);
    }

    // ------------------------------------------------------------------ documents, archives, programs

    [Fact]
    public void OfficeDocumentPropertiesAreEditedAndTheContentIsCopiedUnchanged()
    {
        string dir = TestData.NewTempDirectory("metadata");
        string path = Path.Combine(dir, "Item list, 2026.xlsx");
        var table = new ReportTable("Items", ["ID", "Name"]);
        table.Rows.Add(["3031", "gold coin"]);
        XlsxWriter.Write(path, [("Items", table)]);
        var hashes = EntryHashes(path);
        var before = Engine.Load(path, Ct);
        Assert.Equal("openxml", before.ProviderId);
        Assert.Equal("Excel workbook", before.Format);
        var result = Engine.Save(before, [new("title", Unicode), new("authors", "RockyLaRo"), new("keywords", "tibia; items"), new("created", "2026-01-02 03:04:05"), new("category", "Wiki")], NoBackup, Ct);
        Assert.True(result.Status == MetadataSaveStatus.Saved, result.Message);
        var after = Engine.Load(path, Ct);
        Assert.Equal(Unicode, after.Value("title"));
        Assert.Equal("tibia; items", after.Value("keywords"));
        Assert.Equal("2026-01-02 03:04:05", after.Value("created"));
        foreach (var (entry, hash) in EntryHashes(path).Where(e => !e.Key.Contains("core.xml", StringComparison.Ordinal)))
            Assert.Equal(hashes[entry], hash);
        Assert.Contains("gold coin", ReadEntry(path, "xl/worksheets/sheet1.xml") + ReadEntry(path, "xl/sharedStrings.xml"), StringComparison.Ordinal);
    }

    private static Dictionary<string, string> EntryHashes(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var s = e.Open();
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s));
        });
    }

    private static string ReadEntry(string path, string name)
    {
        using var zip = ZipFile.OpenRead(path);
        var e = zip.GetEntry(name);
        if (e == null) return "";
        using var r = new StreamReader(e.Open());
        return r.ReadToEnd();
    }

    private static string MakeEpub(string dir)
    {
        string path = Path.Combine(dir, "book.epub");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var w = new StreamWriter(mime.Open(), new UTF8Encoding(false))) w.Write("application/epub+zip");
            void Text(string name, string text)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(text);
            }
            Text("META-INF/container.xml", """<?xml version="1.0"?><container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles></container>""");
            Text("OEBPS/content.opf", """
                <?xml version="1.0" encoding="UTF-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="uid">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:identifier id="uid">urn:uuid:12345678-1234-1234-1234-123456789abc</dc:identifier>
                    <dc:title>Old title</dc:title>
                    <dc:creator id="c1">First Author</dc:creator>
                    <meta refines="#c1" property="role">aut</meta>
                    <dc:creator id="c2">Second Author</dc:creator>
                    <meta refines="#c2" property="role">aut</meta>
                    <dc:language>en</dc:language>
                  </metadata>
                  <manifest><item id="t" href="text.xhtml" media-type="application/xhtml+xml"/></manifest>
                  <spine><itemref idref="t"/></spine>
                </package>
                """);
            Text("OEBPS/text.xhtml", "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><p>Hello</p></body></html>");
        }
        return path;
    }

    [Fact]
    public void EpubMetadataIsEditedAndTheBookStaysValid()
    {
        string path = MakeEpub(TestData.NewTempDirectory("metadata"));
        var before = Engine.Load(path, Ct);
        Assert.Equal("First Author; Second Author", before.Value("authors"));
        Assert.False(before.Field("identifier")!.Editable);
        var result = Engine.Save(before, [new("title", Unicode), new("authors", "RockyLaRo"), new("keywords", "Tibia; Guide"), new("date", "2026-10-25")], NoBackup, Ct);
        Assert.True(result.Status == MetadataSaveStatus.Saved, result.Message);
        var after = Engine.Load(path, Ct);
        Assert.Equal(Unicode, after.Value("title"));
        Assert.Equal("RockyLaRo", after.Value("authors"));
        Assert.Equal("Tibia; Guide", after.Value("keywords"));
        Assert.Equal(before.Value("identifier"), after.Value("identifier"));
        string opf = ReadEntry(path, "OEBPS/content.opf");
        Assert.DoesNotContain("refines=\"#c2\"", opf, StringComparison.Ordinal);   // the removed creator's refinement went with it
        Assert.Contains("refines=\"#c1\"", opf, StringComparison.Ordinal);
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        Assert.Equal(zip.Entries[0].Length, zip.Entries[0].CompressedLength);
    }

    [Fact]
    public void PdfDocumentInformationIsReadButNeverWritten()
    {
        string path = Path.Combine(TestData.NewTempDirectory("metadata"), "guide.pdf");
        // Title in UTF-16 (hex), author with an octal escape (ü = \374), keywords separated by commas.
        string pdf = "%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
                     "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] >>\nendobj\n" +
                     "4 0 obj\n<< /Title <FEFF00540069006200690061> /Author (J\\374rgen \\(Roc ky\\)) /Keywords (tibia, wiki) /CreationDate (D:20260102030405+01'00') /Producer (Test) >>\nendobj\n" +
                     "trailer\n<< /Root 1 0 R /Info 4 0 R >>\n%%EOF\n";
        File.WriteAllText(path, pdf, Encoding.Latin1);
        var s = Engine.Load(path, Ct);
        Assert.Equal("pdf", s.ProviderId);
        Assert.Equal("Tibia", s.Value("title"));
        Assert.Equal("Jürgen (Roc ky)", s.Value("authors"));
        Assert.Equal("tibia; wiki", s.Value("keywords"));
        Assert.Equal("1", s.Value("pages"));
        Assert.Equal("1.4", s.Value("pdfVersion"));
        Assert.NotNull(s.Value("created"));
        Assert.False(s.CanEdit);
        Assert.Equal(MetadataSaveStatus.Skipped, Save(path, ("title", "x")).Status);
    }

    [Fact]
    public void SvgTitleAndDescriptionAreEditedAndTheDrawingIsKept()
    {
        string path = Path.Combine(TestData.NewTempDirectory("metadata"), "icon.svg");
        File.WriteAllText(path, """<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16"><rect x="1" y="2" width="3" height="4" fill="#123456"/></svg>""");
        var result = Save(path, ("title", "Gold coin"), ("description", Unicode));
        Assert.True(result.Status == MetadataSaveStatus.Saved, result.Message);
        var s = Engine.Load(path, Ct);
        Assert.Equal("Gold coin", s.Value("title"));
        Assert.Equal(Unicode, s.Value("description"));
        Assert.Equal("0 0 16 16", s.Value("viewBox"));
        Assert.Contains("fill=\"#123456\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void ZipCommentsAreEditedAndTheEntriesKept()
    {
        string dir = TestData.NewTempDirectory("metadata");
        string path = Path.Combine(dir, "export.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(zip.CreateEntry("sprites/1.txt").Open());
            w.Write("one");
        }
        var before = EntryHashes(path);
        var result = Save(path, ("archiveComment", "Exported for TibiaWiki"));
        Assert.True(result.Status == MetadataSaveStatus.Saved, result.Message);
        var s = Engine.Load(path, Ct);
        Assert.Equal("Exported for TibiaWiki", s.Value("archiveComment"));
        Assert.Equal("1", s.Value("entries"));
        Assert.Equal(before, EntryHashes(path));
    }

    [Fact]
    public void TarArchivesAndProgramsAreRead()
    {
        string dir = TestData.NewTempDirectory("metadata");
        Directory.CreateDirectory(Path.Combine(dir, "content"));
        File.WriteAllText(Path.Combine(dir, "content", "a.txt"), "aaa");
        string tar = Path.Combine(dir, "content.tar");
        TarFile.CreateFromDirectory(Path.Combine(dir, "content"), tar, includeBaseDirectory: false);
        var t = Engine.Load(tar, Ct);
        Assert.Equal("tar", t.ProviderId);
        Assert.Equal("1", t.Value("entries"));
        Assert.False(t.CanEdit);

        string dll = typeof(MetadataEngine).Assembly.Location;
        var pe = Engine.Load(dll, Ct);
        Assert.Equal("pe", pe.ProviderId);
        Assert.Equal("Yes", pe.Value("dotnet"));
        Assert.Contains("RockyLaRo", pe.Value("copyright"), StringComparison.Ordinal);
        Assert.False(pe.CanEdit);
        Assert.Contains(pe.Warnings, w => w.Contains("digital signature", StringComparison.Ordinal));
    }

    [Fact]
    public void TibiaClientFilesAreReadThroughTheExistingReaders()
    {
        string path = Path.Combine(TestData.NewTempDirectory("metadata"), "graphics_resources.rcc");
        File.WriteAllBytes(path, FakeClient.Rcc("interface"));
        var s = Engine.Load(path, Ct);
        Assert.Equal("tibia", s.ProviderId);
        Assert.Contains(s.Fields, f => f.Category == MetadataFields.Tibia);
        Assert.False(s.CanEdit);
    }

    [Fact]
    public void UnknownAndDamagedFilesAreReportedNotWritten()
    {
        string dir = TestData.NewTempDirectory("metadata");
        string unknown = Path.Combine(dir, "data.xyz");
        File.WriteAllBytes(unknown, [1, 2, 3, 4, 5]);
        var u = Engine.Load(unknown, Ct);
        Assert.Empty(u.Errors);
        Assert.Equal("File system only", u.ProviderName);
        Assert.Contains(u.Warnings, w => w.Contains("only the file system properties", StringComparison.OrdinalIgnoreCase));
        // The file system part still works.
        Assert.Equal(MetadataSaveStatus.Saved, Engine.Save(u, [new("fs.hidden", "true")], NoBackup, Ct).Status);
        Assert.True(File.GetAttributes(unknown).HasFlag(FileAttributes.Hidden));

        string broken = Path.Combine(dir, "broken.flac");
        File.WriteAllBytes(broken, [.. "fLaC"u8.ToArray(), 0xFF, 0xFF, 0xFF]);
        var b = Engine.Load(broken, Ct);
        Assert.NotEmpty(b.Errors);
        Assert.False(b.CanEdit);
        var result = Engine.Save(b, [new("title", "x")], NoBackup, Ct);
        Assert.Equal(MetadataSaveStatus.Skipped, result.Status);
        Assert.Equal([(byte)'f', (byte)'L', (byte)'a', (byte)'C', 0xFF, 0xFF, 0xFF], File.ReadAllBytes(broken));

        var missing = Engine.Load(Path.Combine(dir, "gone.mp3"), Ct);
        Assert.Contains(missing.Errors, e => e.Contains("does not exist", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ safety

    [Fact]
    public void InvalidValuesAreRefusedWithTheReason()
    {
        string path = Copy("sample.jpg");
        var s = Engine.Load(path, Ct);
        var (accepted, refused) = MetadataEngine.Check(s, [new("rating", "9"), new("dateTaken", "yesterday"), new("latitude", "123"), new("make", "Canon"), new("title", "ok")]);
        Assert.Equal(["title"], accepted.Select(a => a.Key));
        Assert.Contains(refused, r => r.StartsWith("Rating: must be between 0 and 5", StringComparison.Ordinal));
        Assert.Contains(refused, r => r.Contains("is not a date", StringComparison.Ordinal));
        Assert.Contains(refused, r => r.StartsWith("Latitude: must be between", StringComparison.Ordinal));
        Assert.Contains(refused, r => r.Contains("read-only", StringComparison.Ordinal));
        Assert.Equal("1.5", MetadataValues.Normalize(MetadataFields.Latitude, "1,5"));
        Assert.Equal("a; b", MetadataValues.Normalize(MetadataFields.Keywords, " a ;b; a "));
    }

    [Fact]
    public void AFileChangedAfterItWasReadIsNotOverwritten()
    {
        string path = Copy("sample.mp3");
        var s = Engine.Load(path, Ct);
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0x55;   // another program changes the file
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, s.Stamp!.LastWriteUtc);   // even with the old time stamp
        var result = Engine.Save(s, [new("title", "x")], NoBackup, Ct);
        Assert.Equal(MetadataSaveStatus.Failed, result.Status);
        Assert.Contains("changed by another program", result.Message, StringComparison.Ordinal);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void ReadOnlyFilesNeedTheAttributeClearedInTheSameSave()
    {
        string path = Copy("sample.flac");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var refused = Save(path, ("title", "x"));
        Assert.Equal(MetadataSaveStatus.Failed, refused.Status);
        Assert.Contains("read-only", refused.Message, StringComparison.Ordinal);
        var saved = Save(path, ("title", "x"), ("fs.readOnly", "false"));
        Assert.Equal(MetadataSaveStatus.Saved, saved.Status);
        Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal("x", Engine.Load(path, Ct).Value("title"));
    }

    [Fact]
    public void BackupsAreVerifiedCopiesAndModifiedTimesAreKeptOnRequest()
    {
        string path = Copy("sample.ogg");
        var original = File.ReadAllBytes(path);
        var modified = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Local);
        File.SetLastWriteTime(path, modified);
        var backups = new BackupStore(TestData.NewTempDirectory("backups"));
        var result = Engine.Save(Engine.Load(path, Ct), [new("title", "with backup")], new MetadataSaveOptions { Backups = backups }, Ct);
        Assert.Equal(MetadataSaveStatus.Saved, result.Status);
        Assert.Equal(original, File.ReadAllBytes(result.BackupPath!));
        Assert.Single(backups.List());
        Assert.Equal(modified, File.GetLastWriteTime(path));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*", new EnumerationOptions { AttributesToSkip = 0 }));   // no temporary file left

        var touched = Engine.Save(Engine.Load(path, Ct), [new("title", "new time")], new MetadataSaveOptions { KeepModifiedTime = false }, Ct);
        Assert.NotEqual(modified, File.GetLastWriteTime(path));
        Assert.Contains(touched.Warnings, w => w.Contains("modified time", StringComparison.Ordinal));
    }

    [Fact]
    public void AFileOpenInAnotherProgramFailsAndStaysUnchanged()
    {
        string path = Copy("sample.mp3");
        var s = Engine.Load(path, Ct);
        var original = File.ReadAllBytes(path);
        MetadataSaveResult result;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))   // shared for reading, but not for replacing
            result = Engine.Save(s, [new("title", "locked")], NoBackup, Ct);
        Assert.Equal(MetadataSaveStatus.Failed, result.Status);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*", new EnumerationOptions { AttributesToSkip = 0 }));
    }

    [Fact]
    public void LongPathsAndLargeFilesWork()
    {
        string dir = TestData.NewTempDirectory("metadata");
        string deep = dir;
        while (deep.Length < 300) deep = Path.Combine(deep, "a folder with a long name ÄÖÜ");
        string mp3 = Copy("sample.mp3", deep);
        Assert.True(mp3.Length > 260);
        Assert.Equal(MetadataSaveStatus.Saved, Save(mp3, ("title", "deep")).Status);
        Assert.Equal("deep", Engine.Load(mp3, Ct).Value("title"));

        // A 40 MB WAV: only the tag changes, the samples are copied.
        string wav = Path.Combine(dir, "large.wav");
        using (var writer = new NAudio.Wave.WaveFileWriter(wav, new NAudio.Wave.WaveFormat(44100, 16, 2)))
        {
            byte[] block = new byte[1 << 20];
            for (int i = 0; i < 40; i++) writer.Write(block, 0, block.Length);
        }
        var before = Engine.Load(wav, Ct);
        Assert.Equal(MetadataSaveStatus.Saved, Engine.Save(before, [new("title", "large")], NoBackup, Ct).Status);
        var after = Engine.Load(wav, Ct);
        Assert.Equal("large", after.Value("title"));
        Assert.Equal(before.Value("duration"), after.Value("duration"));
    }

    // ------------------------------------------------------------------ batch, presets, comparison, export

    [Fact]
    public void ABatchOfMixedFormatsChangesWhatEachCanStoreAndSaysWhatItSkipped()
    {
        string dir = TestData.NewTempDirectory("metadata");
        string mp3 = Copy("sample.mp3", dir), jpg = Copy("sample.jpg", dir), bmp = Copy("sample.bmp", dir);
        var snapshots = new[] { mp3, jpg, bmp }.Select(p => Engine.Load(p, Ct)).ToList();
        var plans = MetadataBatch.Plan(snapshots, [new BatchOperation(BatchAction.Set, "copyright", "© RockyLaRo"), new BatchOperation(BatchAction.AddItems, "keywords", "tibia; wiki")]);
        Assert.Equal(["copyright"], plans[0].Edits.Select(e => e.Key));
        Assert.Contains(plans[0].Skipped, s => s.StartsWith("Keywords:", StringComparison.Ordinal));   // MP3 has no keywords: said, not silent
        Assert.Equal(["copyright", "keywords"], plans[1].Edits.Select(e => e.Key).Order());
        Assert.Empty(plans[2].Edits);
        Assert.Equal(2, plans[2].Skipped.Count);

        var results = Engine.Run(plans, NoBackup, ct: Ct);
        Assert.Equal([MetadataSaveStatus.Saved, MetadataSaveStatus.Saved, MetadataSaveStatus.Skipped], results.Select(r => r.Status));
        Assert.Equal("© RockyLaRo", Engine.Load(mp3, Ct).Value("copyright"));
        Assert.Equal("tibia; wiki", Engine.Load(jpg, Ct).Value("keywords"));
        Assert.NotEmpty(results[0].SkippedFields);
        Assert.Equal(File.ReadAllBytes(Fixture("sample.bmp")), File.ReadAllBytes(bmp));
    }

    [Fact]
    public void KeywordsAreAddedRemovedAndReplacedAndOtherFieldsAreUntouched()
    {
        string path = Copy("sample.jpg");
        var first = Save(path, ("keywords", "a; b; c"), ("title", "keep me"));
        Assert.True(first.Status == MetadataSaveStatus.Saved, first.Message);
        var s = Engine.Load(path, Ct);
        Assert.Empty(s.Warnings);   // TagLib# must not find the file it wrote damaged
        var plan = MetadataBatch.Plan(s,
        [
            new BatchOperation(BatchAction.AddItems, "keywords", "d"), new BatchOperation(BatchAction.RemoveItems, "keywords", "B"),
            new BatchOperation(BatchAction.ReplaceItem, "keywords", "c", "see"),
        ]);
        Assert.Equal(["keywords"], plan.Edits.Select(e => e.Key));
        var run = Engine.Run([plan], NoBackup, ct: Ct)[0];
        Assert.True(run.Status == MetadataSaveStatus.Saved, run.Message);
        var after = Engine.Load(path, Ct);
        Assert.Equal("a; see; d", after.Value("keywords"));
        Assert.Equal("keep me", after.Value("title"));
    }

    [Fact]
    public void FieldsAreCopiedFromAReferenceFileAndCancellingStopsBetweenFiles()
    {
        string dir = TestData.NewTempDirectory("metadata");
        string reference = Copy("sample.flac", dir);
        Assert.Equal(MetadataSaveStatus.Saved, Save(reference, ("album", "Tibia OST"), ("authors", "CipSoft")).Status);
        string a = Path.Combine(dir, "a.mp3"), b = Path.Combine(dir, "b.mp3");
        File.Copy(Fixture("sample.mp3"), a);
        File.Copy(Fixture("sample.mp3"), b);
        var refSnap = Engine.Load(reference, Ct);
        var plans = MetadataBatch.Plan([Engine.Load(a, Ct), Engine.Load(b, Ct)],
            [new BatchOperation(BatchAction.CopyFromReference, "album"), new BatchOperation(BatchAction.CopyFromReference, "authors"), new BatchOperation(BatchAction.CopyFromReference, "title")], refSnap);
        Assert.Contains(plans[0].Skipped, s => s.Contains("empty in the reference file", StringComparison.Ordinal));

        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress(() => cts.Cancel());   // cancel as soon as the first file starts
        var results = Engine.Run(plans, NoBackup, progress, cts.Token);
        Assert.Equal(MetadataSaveStatus.Cancelled, results[1].Status);
        Assert.Equal(File.ReadAllBytes(Fixture("sample.mp3")), File.ReadAllBytes(b));
        Assert.True(results[0].Status is MetadataSaveStatus.Saved or MetadataSaveStatus.Cancelled);
        if (results[0].Status == MetadataSaveStatus.Saved) Assert.Equal("Tibia OST", Engine.Load(a, Ct).Value("album"));
        else Assert.Equal(File.ReadAllBytes(Fixture("sample.mp3")), File.ReadAllBytes(a));

        var again = Engine.Run(MetadataBatch.Plan([Engine.Load(b, Ct)], [new BatchOperation(BatchAction.CopyFromReference, "album")], refSnap), NoBackup, ct: Ct);
        Assert.Equal(MetadataSaveStatus.Saved, again[0].Status);
        Assert.Equal("Tibia OST", Engine.Load(b, Ct).Value("album"));
    }

    private sealed class SynchronousProgress(Action onFirst) : IProgress<Core.Common.OperationProgress>
    {
        public void Report(Core.Common.OperationProgress value)
        {
            if (value.Done == 0) onFirst();
        }
    }

    [Fact]
    public void PresetsAreValidatedAndOnlySetTheirOwnFields()
    {
        var bad = new MetadataPreset { Name = "Bad", Values = { ["fs.modified"] = "2020-01-01" } };
        Assert.Contains("cannot be part of a preset", bad.Validate(), StringComparison.Ordinal);
        Assert.Contains("must be between", new MetadataPreset { Name = "Bad", Values = { ["rating"] = "7" } }.Validate(), StringComparison.Ordinal);
        Assert.Equal("A preset needs a name.", new MetadataPreset { Values = { ["title"] = "x" } }.Validate());

        var preset = new MetadataPreset { Name = "TibiaWiki upload", Values = { ["copyright"] = "Tibia graphics © CipSoft GmbH", ["authors"] = "RockyLaRo", ["keywords"] = "tibia" } };
        Assert.Null(preset.Validate());
        string path = Copy("sample.png");
        Assert.Equal(MetadataSaveStatus.Saved, Save(path, ("title", "Rotworm"), ("keywords", "creature")).Status);
        var plan = MetadataBatch.Plan(Engine.Load(path, Ct), preset.ToOperations());
        Assert.Equal(MetadataSaveStatus.Saved, Engine.Run([plan], NoBackup, ct: Ct)[0].Status);
        var after = Engine.Load(path, Ct);
        Assert.Equal("Rotworm", after.Value("title"));                       // not in the preset: untouched
        Assert.Equal("creature; tibia", after.Value("keywords"));            // added, not replaced
        Assert.Equal("Tibia graphics © CipSoft GmbH", after.Value("copyright"));
        var json = JsonSerializer.Serialize(preset);
        Assert.Equal("RockyLaRo", JsonSerializer.Deserialize<MetadataPreset>(json)!.Values["authors"]);
    }

    [Fact]
    public void ComparisonAndExportsHandleUnicodeFormulasAndMissingValues()
    {
        string dir = TestData.NewTempDirectory("metadata");
        string a = Copy("sample.mp3", dir), b = Copy("sample.flac", dir);
        Assert.Equal(MetadataSaveStatus.Saved, Save(a, ("title", Unicode + ", \"quoted\""), ("comment", "=HYPERLINK(\"x\")")).Status);
        var snaps = new[] { Engine.Load(a, Ct), Engine.Load(b, Ct) };
        var rows = MetadataBatch.Compare(snaps);
        var title = rows.Single(r => r.Key == "title");
        Assert.True(title.Differs);
        Assert.Null(title.Values[1]);
        Assert.DoesNotContain(rows, r => r.Key.StartsWith("fs.", StringComparison.Ordinal));
        Assert.Contains(MetadataBatch.Compare(snaps, includeFileSystem: true), r => r.Key == "fs.size");

        string csv = MetadataBatch.ToCsv(snaps);
        Assert.Contains("\"" + Unicode + ", \"\"quoted\"\"\"", csv, StringComparison.Ordinal);
        Assert.Contains("'=HYPERLINK", csv, StringComparison.Ordinal);   // a formula is never run by a spreadsheet
        Assert.Equal(3, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);

        using var json = JsonDocument.Parse(MetadataBatch.ToJson(snaps));
        var files = json.RootElement.GetProperty("files");
        Assert.Equal(2, files.GetArrayLength());
        Assert.Equal(Unicode + ", \"quoted\"", files[0].GetProperty("fields").GetProperty("title").GetProperty("value").GetString());
        Assert.False(files[1].GetProperty("fields").TryGetProperty("title", out _));
    }

    [Fact]
    public void EveryProviderDeclaresItsCapabilitiesAndLimitations()
    {
        foreach (var p in Engine.Providers)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Limitations), p.Id);
            Assert.NotEmpty(p.Extensions);
            Assert.True(p.Capabilities.HasFlag(MetadataCapabilities.Read), p.Id);
            if (!p.Capabilities.HasFlag(MetadataCapabilities.Edit)) Assert.Throws<NotSupportedException>(() => p.Write("x", [], Ct));
        }
        Assert.Equal(Engine.Providers.Count, Engine.Providers.Select(p => p.Id).Distinct().Count());
    }
}
