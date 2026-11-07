using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;

namespace TibiaFoundry.Tests;

/// <summary>Shared file-operation infrastructure: path safety, format detection, hashing, reports, output policy, backups, temporary folders.</summary>
public class FileOpsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("images/a.png", @"images\a.png")]
    [InlineData("/images/a.png", @"images\a.png")]
    [InlineData(@"a\b\c.txt", @"a\b\c.txt")]
    public void SafeRelativePathAcceptsNormalNames(string input, string expected) => Assert.Equal(expected, PathGuard.SafeRelativePath(input));

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("a/../../evil.txt")]
    [InlineData(@"C:\Windows\evil.txt")]
    [InlineData("C:evil.txt")]
    [InlineData(@"\\server\share\x")]
    [InlineData("a//b")]
    [InlineData("NUL.txt")]
    [InlineData("com1")]
    [InlineData("file.txt:stream")]
    [InlineData("trailing.")]
    [InlineData("")]
    public void SafeRelativePathRefusesTraversalAndDeviceNames(string input) => Assert.Null(PathGuard.SafeRelativePath(input));

    [Fact]
    public void ResolveInsideNeverLeavesTheRoot()
    {
        string root = TestData.NewTempDirectory("guard");
        Assert.StartsWith(root, PathGuard.ResolveInside(root, "x/y.txt"));
        Assert.Throws<UnsafePathException>(() => PathGuard.ResolveInside(root, "../x.txt"));
        Assert.False(PathGuard.IsInside(root, root + "-sibling"));
        Assert.True(PathGuard.IsSameOrInside(root, root));
    }

    [Fact]
    public void FormatsAreRecognizedByContentNotName()
    {
        string dir = TestData.NewTempDirectory("sig");
        var png = ImageIO.EncodePng(new RgbaImage(2, 2));
        File.WriteAllBytes(Path.Combine(dir, "picture.jpg"), png);
        var id = FileSignatures.Identify(Path.Combine(dir, "picture.jpg"));
        Assert.Equal(FileSignatures.Png, id.Format);
        Assert.Equal(FormatEvidence.Signature, id.Evidence);
        Assert.False(id.Format!.ExtensionMatches(Path.Combine(dir, "picture.jpg")));
        Assert.Equal(FileSignatures.Zip, FileSignatures.Identify([0x50, 0x4B, 0x03, 0x04, 0, 0]).Format);
        Assert.Equal(FileSignatures.QtResource, FileSignatures.Identify("qres\0\0\0\u0003"u8).Format);
        Assert.Equal(FileSignatures.Json, FileSignatures.Identify("  [ {\"a\":1} ]"u8).Format);
        Assert.Null(FileSignatures.Identify(new byte[] { 0, 1, 2, 3, 0xFE, 0xFF }, "blob.bin").Format);
    }

    [Fact]
    public void HashesMatchPublishedTestVectors()
    {
        // FIPS 180-2 test vector for "abc".
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", FileHashing.HashBytes("abc"u8));
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", FileHashing.HashBytes("abc"u8, HashAlgorithmKind.Md5));
        string dir = TestData.NewTempDirectory("hash");
        File.WriteAllText(Path.Combine(dir, "a.txt"), "abc");
        Assert.Equal(FileHashing.HashBytes("abc"u8), FileHashing.HashFile(Path.Combine(dir, "a.txt"), ct: Ct));
        File.WriteAllText(Path.Combine(dir, "b.txt"), "abd");
        Assert.False(FileHashing.SameContent(Path.Combine(dir, "a.txt"), Path.Combine(dir, "b.txt"), Ct));
    }

    [Fact]
    public void CsvCellsAreQuotedAndFormulaSafe()
    {
        Assert.Equal("plain", ReportWriter.CsvCell("plain"));
        Assert.Equal("\"a,b\"", ReportWriter.CsvCell("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", ReportWriter.CsvCell("say \"hi\""));
        Assert.Equal("'=cmd|' /c calc'!A1", ReportWriter.CsvCell("=cmd|' /c calc'!A1"));
        Assert.Equal("-12.5", ReportWriter.CsvCell("-12.5"));
        var table = new ReportTable("t", ["A", "B"]);
        table.Add("1", "x,y");
        Assert.Contains("A,B\r\n1,\"x,y\"\r\n", ReportWriter.ToCsv(table));
        Assert.Contains("\"B\": \"x,y\"", ReportWriter.ToJson(table));
    }

    private static (string Dir, BackupStore Store) Setup()
    {
        string dir = TestData.NewTempDirectory("out");
        return (dir, new BackupStore(Path.Combine(dir, "_backups")));
    }

    [Fact]
    public void AskWithoutAPromptKeepsTheExistingFile()
    {
        var (dir, store) = Setup();
        string file = Path.Combine(dir, "a.txt");
        File.WriteAllText(file, "old");
        using var session = new OutputSession("test", FileSafetyOptions.Default, store, prompt: null);
        Assert.Null(session.WriteAllBytes(file, "new"u8.ToArray()));
        Assert.Equal("old", File.ReadAllText(file));
        Assert.Equal(1, session.Skipped);
    }

    [Fact]
    public void KeepBothWritesANewNameAndOverwriteBacksUpFirst()
    {
        var (dir, store) = Setup();
        string file = Path.Combine(dir, "a.txt");
        File.WriteAllText(file, "old");
        using (var keep = new OutputSession("keep", FileSafetyOptions.Default with { WhenExists = ExistingFilePolicy.KeepBoth }, store))
            Assert.Equal(Path.Combine(dir, "a (2).txt"), keep.WriteAllBytes(file, "new"u8.ToArray()));
        Assert.Equal("old", File.ReadAllText(file));
        using (var replace = new OutputSession("replace", FileSafetyOptions.Default with { WhenExists = ExistingFilePolicy.Overwrite }, store))
        {
            Assert.Equal(file, replace.WriteAllBytes(file, "newer"u8.ToArray()));
            Assert.Single(replace.BackedUp);
        }
        Assert.Equal("newer", File.ReadAllText(file));
        var set = Assert.Single(store.List());
        Assert.Equal("old", File.ReadAllText(Path.Combine(set.Folder, set.Manifest.Entries[0].Stored)));
        Assert.True(set.Manifest.Complete);
    }

    [Fact]
    public void AFailedValidationLeavesTheOriginalUntouched()
    {
        var (dir, store) = Setup();
        string file = Path.Combine(dir, "a.txt");
        File.WriteAllText(file, "old");
        using var session = new OutputSession("validate", FileSafetyOptions.Default with { WhenExists = ExistingFilePolicy.Overwrite }, store);
        Assert.Throws<InvalidDataException>(() => session.WriteAllBytes(file, "bad"u8.ToArray(), _ => throw new InvalidDataException("rejected")));
        Assert.Equal("old", File.ReadAllText(file));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        Assert.Empty(session.BackedUp);
    }

    [Fact]
    public void TwoOutputsWithTheSameNameNeverOverwriteEachOther()
    {
        var (dir, store) = Setup();
        using var session = new OutputSession("dup", FileSafetyOptions.Default with { WhenExists = ExistingFilePolicy.Overwrite }, store);
        string target = Path.Combine(dir, "same.png");
        Assert.Equal(target, session.WriteAllBytes(target, [1]));
        Assert.Equal(Path.Combine(dir, "same (2).png"), session.WriteAllBytes(target, [2]));
        Assert.Equal([1], File.ReadAllBytes(target));
    }

    [Fact]
    public void InputFilesAreProtected()
    {
        var (dir, store) = Setup();
        string input = Path.Combine(dir, "in.png");
        File.WriteAllBytes(input, [1]);
        using var session = new OutputSession("protect", FileSafetyOptions.Default with { WhenExists = ExistingFilePolicy.Overwrite }, store);
        session.Protect([input]);
        Assert.Throws<IOException>(() => session.WriteAllBytes(input, [2]));
        Assert.Equal([1], File.ReadAllBytes(input));
    }

    [Fact]
    public void ThePromptIsAskedOnceWithApplyToAll()
    {
        var (dir, store) = Setup();
        int asked = 0;
        for (int i = 0; i < 3; i++) File.WriteAllText(Path.Combine(dir, $"{i}.txt"), "old");
        using var session = new OutputSession("ask", FileSafetyOptions.Default, store, (_, _, _) => { asked++; return new ConflictAnswer(ConflictChoice.Skip, ApplyToAll: true); });
        for (int i = 0; i < 3; i++) Assert.Null(session.WriteAllBytes(Path.Combine(dir, $"{i}.txt"), [9]));
        Assert.Equal(1, asked);
        using var cancel = new OutputSession("cancel", FileSafetyOptions.Default, store, (_, _, _) => new ConflictAnswer(ConflictChoice.Cancel, false));
        Assert.Throws<OperationCanceledException>(() => cancel.WriteAllBytes(Path.Combine(dir, "0.txt"), [9]));
    }

    [Fact]
    public void BackupsRestoreToTheOriginalPlaceAndDetectDamage()
    {
        var (dir, store) = Setup();
        string src = Path.Combine(dir, "src");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "a.txt"), "A");
        File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "B");
        using (var set = store.Begin("manual test", BackupKind.Manual, src))
        {
            set.Add(Path.Combine(src, "a.txt"), ct: Ct);
            set.Add(Path.Combine(src, "sub", "b.txt"), ct: Ct);
        }
        File.WriteAllText(Path.Combine(src, "a.txt"), "changed");
        File.Delete(Path.Combine(src, "sub", "b.txt"));
        var info = Assert.Single(store.List());
        Assert.Equal("sub/b.txt", info.Manifest.Entries[1].Stored["files/".Length..]);
        var result = store.Restore(info, info.Manifest.Entries, null, _ => true, Ct);
        Assert.Equal(2, result.Restored.Count);
        Assert.Equal("A", File.ReadAllText(Path.Combine(src, "a.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(src, "sub", "b.txt")));
        // The overwritten "changed" file was itself backed up first.
        Assert.Equal(2, store.List().Count);

        // A damaged backup copy is reported, never restored.
        var original = store.List().Single(s => s.Manifest.Kind == BackupKind.Manual);
        File.WriteAllText(Path.Combine(original.Folder, original.Manifest.Entries[0].Stored), "tampered");
        File.WriteAllText(Path.Combine(src, "a.txt"), "x");
        var damaged = store.Restore(original, [original.Manifest.Entries[0]], null, _ => true, Ct);
        Assert.Single(damaged.Failed);
        Assert.Equal("x", File.ReadAllText(Path.Combine(src, "a.txt")));

        // Restoring into another folder keeps the relative structure.
        string other = Path.Combine(dir, "restored");
        var copy = store.Restore(original, [original.Manifest.Entries[1]], other, _ => true, Ct);
        Assert.Single(copy.Restored);
        Assert.True(File.Exists(Path.Combine(other, "sub", "b.txt")));
        Assert.NotEmpty(store.History());
    }

    [Fact]
    public void RetentionRemovesOldSetsButNeverOpenOnes()
    {
        var (dir, store) = Setup();
        string file = Path.Combine(dir, "f.txt");
        File.WriteAllText(file, new string('x', 1000));
        using (var a = store.Begin("first", BackupKind.Automatic)) a.Add(file, ct: Ct);
        using (var b = store.Begin("second", BackupKind.Automatic)) b.Add(file, ct: Ct);
        var open = store.Begin("open", BackupKind.Automatic);
        open.Add(file, ct: Ct);
        var removed = store.ApplyRetention(new BackupRetention(0, 1500));
        Assert.Single(removed);
        Assert.Contains(store.List(), s => s.Manifest.Operation == "open");
        open.Complete();
    }

    [Fact]
    public void TemporaryFoldersOfRunningOperationsSurviveCleanUp()
    {
        var temp = new TempWorkspace(TestData.NewTempDirectory("temp"));
        using var running = temp.Begin("running");
        string stale = Path.Combine(temp.Root, "stale");
        Directory.CreateDirectory(stale);
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
        Directory.SetLastWriteTimeUtc(running.Folder, DateTime.UtcNow.AddDays(-2));
        Assert.Equal(1, temp.CleanUp(TimeSpan.FromHours(1)));
        Assert.True(Directory.Exists(running.Folder));
        Assert.False(Directory.Exists(stale));
        string folder = running.Folder;
        running.Dispose();
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void ScansDoNotFollowLinks()
    {
        string dir = TestData.NewTempDirectory("scan");
        Directory.CreateDirectory(Path.Combine(dir, "real"));
        File.WriteAllText(Path.Combine(dir, "real", "a.txt"), "a");
        File.WriteAllText(Path.Combine(dir, "skip.tmp"), "t");
        bool linked = true;
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(dir, "loop"), dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            linked = false; // creating links needs Developer Mode or elevation
        }
        var scan = FileScanner.Scan(dir, recursive: true, FileScanner.ParsePatterns("*.tmp"), ct: Ct);
        Assert.Single(scan.Files);
        Assert.Equal(1, scan.Excluded);
        if (linked) Assert.Single(scan.SkippedLinks);
    }
}
