using TibiaFoundry.Core.Clients;

namespace TibiaFoundry.Tests;

/// <summary>
/// 8.0 source model: the complete inventory of a folder (every file, visible exclusions, links,
/// changes during a scan), the folder inspector (complete, standalone, assets only, not a client;
/// tools per folder), version evidence from the files only, and Find Installation. Every folder is a
/// temporary fake; nothing reaches a real client.
/// </summary>
public sealed class ClientInventoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeClient Client(string? version = "15.33.9d148f")
    {
        var c = FakeClient.Standard(Path.Combine(TestData.NewTempDirectory("inventory"), "Tibia"), version ?? "unused");
        c.Write();
        // Unknown files, nested folders, hidden and extension-less files, an empty folder, a listed file inside conf.
        c.WriteFile("Auction_Id_1.html", FakeClient.Bytes("<html>saved page</html>"));
        c.WriteFile("default", []);
        c.WriteFile("bin/plugins/deep/nested/thing", FakeClient.Bytes("no extension"));
        c.WriteFile("2025-06-14_005004537_Someone_Hotkey.png", FakeClient.Bytes("a screenshot at the root"));
        Directory.CreateDirectory(c.PathOf("crashdump"));
        Directory.CreateDirectory(c.PathOf("bin/empty"));
        File.SetAttributes(c.PathOf("default"), FileAttributes.Hidden);
        return c;
    }

    [Fact]
    public void TheInventoryListsEveryFileAndLeavesPersonalDataOutVisibly()
    {
        var c = Client();
        var inv = ClientInventory.Take(c.Folder, ct: Ct);
        var paths = inv.Files.Select(f => f.Path).ToHashSet();

        foreach (string expected in new[] { "bin/client.exe", "bin/graphics_resources.rcc", "bin/plugins/deep/nested/thing", "Auction_Id_1.html", "default",
                     "assets/appearances-old.dat-bak", "package.json", "package.json.version", "assets.json", "assets.json.sha256", "sounds/sound-0001.ogg", "assets/catalog-content.json" })
            Assert.Contains(expected, paths);
        Assert.DoesNotContain(paths, p => p.StartsWith("conf/", StringComparison.Ordinal) || p.StartsWith("characterdata/", StringComparison.Ordinal) ||
                                            p.StartsWith("minimap/", StringComparison.Ordinal) || p.StartsWith("screenshots/", StringComparison.Ordinal) ||
                                            p.StartsWith("log/", StringComparison.Ordinal) || p.StartsWith("cache/", StringComparison.Ordinal) || p.EndsWith("_Hotkey.png", StringComparison.Ordinal));
        Assert.Equal(["bin/empty"], inv.Directories);
        // Each left-out folder is recorded with its counts (an empty one too), and the screenshot by its rule, never by its name.
        Assert.Equal(["cache/", "characterdata/", "conf/ (except the files the client lists)", "crashdump/", "log/", "minimap/", "screenshots/"],
            inv.Exclusions.Where(e => e.Rule.Contains('/', StringComparison.Ordinal) && !e.Rule.StartsWith("client screenshots", StringComparison.Ordinal)).Select(e => e.Rule));
        Assert.Equal(1, inv.Exclusions.Single(e => e.Rule.StartsWith("client screenshots", StringComparison.Ordinal)).Files);
        Assert.Equal(0, inv.Exclusions.Single(e => e.Rule == "crashdump/").Files);
        Assert.DoesNotContain(inv.Exclusions, e => e.Rule.Contains("Someone", StringComparison.Ordinal) || e.Reason.Contains("Someone", StringComparison.Ordinal));
        Assert.Equal("package.json", inv.Files.Single(f => f.Path == "bin/client.exe").ListedBy);
        Assert.Null(inv.Files.Single(f => f.Path == "Auction_Id_1.html").ListedBy);
        Assert.Equal(FileAttributes.Hidden, inv.Files.Single(f => f.Path == "default").Attributes);
    }

    [Fact]
    public void WithPersonalDataEverythingIsListedAndNothingExcluded()
    {
        var c = Client();
        var inv = ClientInventory.Take(c.Folder, new InventoryOptions { IncludePersonalData = true }, Ct);
        Assert.Empty(inv.Exclusions);
        Assert.Contains(inv.Files, f => f.Path == "conf/clientoptions.json");
        Assert.Contains(inv.Files, f => f.Path == "2025-06-14_005004537_Someone_Hotkey.png");
        Assert.Contains("crashdump", inv.Directories);
        Assert.Equal(Directory.EnumerateFiles(c.Folder, "*", SearchOption.AllDirectories).Count(), inv.Files.Count);
    }

    [Fact]
    public void AScanHashesEveryFileReadOnlyAndIsConsistent()
    {
        var c = Client();
        var before = FakeClient.Snapshot(c.Folder);
        var scan = ClientInventory.Scan(c.Folder, ct: Ct);

        Assert.True(scan.IsConsistent, string.Join(" ", scan.Problems));
        Assert.Equal(scan.Inventory.Files.Count, scan.Files.Count);
        Assert.Equal(FakeClient.Sha(File.ReadAllBytes(c.PathOf("Auction_Id_1.html"))), scan.Files.Single(f => f.Path == "Auction_Id_1.html").Sha256);
        Assert.Equal(ClientManifest.ComputeFingerprint(scan.Files, ["bin/empty"]), scan.Fingerprint);
        Assert.Equal(before, FakeClient.Snapshot(c.Folder));   // nothing in the folder changed, not even a time or an attribute
    }

    [Fact]
    public void AFileThatDiffersFromTheClientsListOrALinkMakesTheScanInconsistent()
    {
        var c = Client();
        File.WriteAllBytes(c.PathOf("sounds/sound-0001.ogg"), FakeClient.Bytes("OggS changed by an update"));
        var scan = ClientInventory.Scan(c.Folder, ct: Ct);
        Assert.False(scan.IsConsistent);
        Assert.Contains(scan.Problems, p => p.StartsWith("sounds/sound-0001.ogg: its content differs from the client's own list", StringComparison.Ordinal));
        Assert.DoesNotContain(scan.Files, f => f.Path == "sounds/sound-0001.ogg");

        var d = Client();
        string link = d.PathOf("linked");
        try { Directory.CreateSymbolicLink(link, d.PathOf("bin")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }   // links need a privilege on some PCs
        var withLink = ClientInventory.Scan(d.Folder, ct: Ct);
        Assert.Contains(withLink.Problems, p => p.StartsWith("linked: it is a link", StringComparison.Ordinal));
        Assert.DoesNotContain(withLink.Files, f => f.Path.StartsWith("linked/", StringComparison.Ordinal));
    }

    [Fact]
    public void FilesWithTheSameNameButOtherBytesHaveAnotherFingerprint()
    {
        var a = Client();
        var b = Client();
        File.WriteAllBytes(b.PathOf("Auction_Id_1.html"), FakeClient.Bytes("<html>other page</html>"));
        var sa = ClientInventory.Scan(a.Folder, ct: Ct);
        var sb = ClientInventory.Scan(b.Folder, ct: Ct);
        Assert.NotEqual(sa.Fingerprint, sb.Fingerprint);
        var diff = ManifestDiff.Compare(sa.Files, sb.Files);
        Assert.Equal("Auction_Id_1.html", Assert.Single(diff.Modified).New.Path);

        // A file moved to another folder (same bytes) is a move, not one removed and one added file.
        File.Move(b.PathOf("bin/plugins/deep/nested/thing"), b.PathOf("bin/plugins/thing"));
        var moved = ManifestDiff.Compare(sa.Files, ClientInventory.Scan(b.Folder, ct: Ct).Files);
        var m = Assert.Single(moved.Moved);
        Assert.Equal(("bin/plugins/deep/nested/thing", "bin/plugins/thing"), (m.Old.Path, m.New.Path));
        Assert.DoesNotContain(moved.Added, f => f.Path == "bin/plugins/thing");
        Assert.Contains("1 file moved or renamed", moved.Summary(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheInspectorTellsCompleteStandaloneAssetsOnlyAndForeignFoldersApart()
    {
        var c = Client();
        var complete = FolderInspector.Inspect(c.Folder);
        Assert.Equal(FolderClass.CompleteInstallation, complete.Class);
        // The fake client has no static data or map files: exactly those tools say so.
        Assert.Equal([ClientCapabilities.StaticData, ClientCapabilities.MapTools, ClientCapabilities.HouseImages], complete.Capabilities.Where(x => !x.Available).Select(x => x.Id));
        Assert.Contains("staticdata-*.dat", complete.Capabilities.Single(x => x.Id == ClientCapabilities.StaticData).Missing.Single(), StringComparison.Ordinal);

        // Without the program: still loadable for every asset tool, but not a complete installation.
        File.Delete(c.PathOf("bin/client.exe"));
        var partial = FolderInspector.Inspect(c.Folder);
        Assert.Equal(FolderClass.StandaloneClient, partial.Class);
        Assert.Equal(1, partial.ListedMissing);
        Assert.False(partial.Capabilities.Single(x => x.Id == ClientCapabilities.ArchiveInstallation).Available);
        Assert.True(partial.Capabilities.Single(x => x.Id == ClientCapabilities.Appearances).Available);
        Assert.True(partial.IsUsable);

        // The assets folder alone: the asset tools work; sounds and the RCC Editor say what they miss.
        string assetsOnly = Path.Combine(TestData.NewTempDirectory("assets-only"), "my 15.33 assets");
        CopyDirectory(c.PathOf("assets"), assetsOnly);
        var assets = FolderInspector.Inspect(assetsOnly);
        Assert.Equal(FolderClass.AssetsOnly, assets.Class);
        Assert.True(assets.Capabilities.Single(x => x.Id == ClientCapabilities.AssetsEditor).Available);
        Assert.Contains(assets.Capabilities.Single(x => x.Id == ClientCapabilities.Sounds).Missing, m => m.Contains("catalog-sound.json", StringComparison.Ordinal));
        Assert.False(assets.Capabilities.Single(x => x.Id == ClientCapabilities.RccEditor).Available);
        Assert.Equal(VersionReport.Unknown, assets.Version.Status);   // "15.33" in the folder name is not evidence
        Assert.Null(assets.Version.Version);

        // A folder named Tibia with unrelated files is not a client.
        string foreign = Path.Combine(TestData.NewTempDirectory("foreign"), "Tibia");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "readme.txt"), "not a client");
        var notClient = FolderInspector.Inspect(foreign);
        Assert.Equal(FolderClass.NotAClient, notClient.Class);
        Assert.False(notClient.IsUsable);
        Assert.Contains(notClient.Notes, n => n.Contains("is not enough", StringComparison.Ordinal));

        // A sprite sheet missing: the sprite tools say how many, the data tools still work.
        File.Delete(Path.Combine(assetsOnly, "sprites-aaaa.bmp.lzma"));
        var sheetMissing = FolderInspector.Inspect(assetsOnly);
        Assert.Contains(sheetMissing.Capabilities.Single(x => x.Id == ClientCapabilities.Appearances).Missing, m => m.StartsWith("1 sprite sheet file", StringComparison.Ordinal));
        Assert.True(sheetMissing.Capabilities.Single(x => x.Id == ClientCapabilities.ItemCatalog).Available);
    }

    [Fact]
    public void ALauncherFolderPointsToItsPackagesAndInspectionNeverWrites()
    {
        string launcher = TestData.NewTempDirectory("launcher");
        var live = FakeClient.Standard(Path.Combine(launcher, "packages", "Tibia")).Write();
        var test = FakeClient.Standard(Path.Combine(launcher, "packages", "TibiaExternal"), "15.40.aaaaaa", "test").Write();
        File.WriteAllBytes(Path.Combine(launcher, "packages", "archive.rar"), FakeClient.Bytes("someone's own file"));
        var before = FakeClient.Snapshot(launcher);

        var inspection = FolderInspector.Inspect(launcher);
        Assert.Equal(FolderClass.NotAClient, inspection.Class);
        Assert.Equal([live.Folder, test.Folder], inspection.Candidates);
        Assert.Equal(ClientServers.Test, FolderInspector.Inspect(test.Folder).SuggestedServer);
        Assert.Equal(before, FakeClient.Snapshot(launcher));
    }

    [Fact]
    public void VersionEvidenceComesFromTheFilesAndConflictsAreReported()
    {
        var c = Client("15.33.9d148f");
        var verified = VersionEvidence.Collect(c.Folder);
        Assert.Equal((VersionReport.Verified, "15.33.9d148f"), (verified.Status, verified.Version));
        Assert.Equal([VersionEvidence.FromPackageJson, VersionEvidence.FromVersionFile], verified.Clues.Select(x => x.Source));

        c.WriteFile("package.json.version", FakeClient.Bytes("15.34.000000"));
        var conflicting = VersionEvidence.Collect(c.Folder);
        Assert.Equal(VersionReport.Conflicting, conflicting.Status);
        Assert.Contains("conflicting", conflicting.Display, StringComparison.Ordinal);

        // No package files: unknown, unless the content matches a stored revision.
        string assetsOnly = Path.Combine(TestData.NewTempDirectory("evidence"), "15.33.9d148f backup");
        CopyDirectory(c.PathOf("assets"), assetsOnly);
        Assert.Equal(VersionReport.Unknown, VersionEvidence.Collect(assetsOnly).Status);
        var known = new Dictionary<string, string> { [FakeClient.Sha(File.ReadAllBytes(c.PathOf("assets/catalog-content.json")))] = "15.33.9d148f" };
        var matched = VersionEvidence.Collect(assetsOnly, known);
        Assert.Equal((VersionReport.Verified, "15.33.9d148f"), (matched.Status, matched.Version));
        Assert.Contains(matched.Clues, x => x.Source.StartsWith("catalog-content.json identical", StringComparison.Ordinal));

        // An executable's version resource only has to agree on major.minor.
        Assert.Equal(VersionReport.Verified, VersionEvidence.Judge([new(VersionEvidence.FromPackageJson, "15.33.9d148f", true), new(VersionEvidence.FromExecutable, "15.33.0.4711", true)]).Status);
        Assert.Equal(VersionReport.Conflicting, VersionEvidence.Judge([new(VersionEvidence.FromPackageJson, "15.33.9d148f", true), new(VersionEvidence.FromExecutable, "15.40.0.1", true)]).Status);
        Assert.Equal(VersionReport.Unknown, VersionEvidence.Judge([new("appearances file name of a stored revision", "15.33.9d148f", false)]).Status);
    }

    [Fact]
    public void FindInstallationValidatesCandidatesByTheirFiles()
    {
        string launcher = TestData.NewTempDirectory("find");
        var live = FakeClient.Standard(Path.Combine(launcher, "packages", "Tibia")).Write();
        Directory.CreateDirectory(Path.Combine(launcher, "packages", "Tibia old"));
        File.WriteAllText(Path.Combine(launcher, "packages", "Tibia old", "notes.txt"), "a folder named Tibia");
        string elsewhere = Path.Combine(TestData.NewTempDirectory("find-other"), "Games", "Tibia");

        var result = InstallationFinder.Find([new(Path.Combine(launcher, "packages"), "packages"), new(launcher, "uninstall entry"), new(elsewhere, "Program Files")], ct: Ct);
        var found = Assert.Single(result.Candidates);
        Assert.Equal((live.Folder, FolderClass.CompleteInstallation), (found.Path, found.Inspection.Class));
        Assert.Contains(result.RootsLookedAt, r => r.Contains("not there", StringComparison.Ordinal));

        var none = InstallationFinder.Find([new(elsewhere, "Program Files")], ct: Ct);
        Assert.Empty(none.Candidates);
        Assert.Contains("Browse for folder", none.Explanation, StringComparison.Ordinal);

        Assert.Equal(@"C:\Users\x\AppData\Local\Tibia\uninst.exe", InstallationFinder.ExecutableOf(@"""C:\Users\x\AppData\Local\Tibia\uninst.exe"" /S"));
        Assert.Equal(@"C:\Tibia\uninst.exe", InstallationFinder.ExecutableOf(@"C:\Tibia\uninst.exe /S"));
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"],
            InstallationFinder.ParseLibraryFolders("\"libraryfolders\"\n{\n \"0\"\n {\n  \"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n }\n \"1\" { \"path\" \"D:\\\\SteamLibrary\" }\n}"));
    }

    /// <summary>
    /// Opt-in (TF_CLIENT_ARCHIVE_MEASURE=1): inspects and scans the installed client packages as a
    /// complete installation would be archived (read only) and, with TF_CLIENT_ARCHIVE_REPORT=&lt;file&gt;,
    /// writes the figures. Only counts and rules are written: never a file name (some hold a character's name).
    /// </summary>
    [Fact]
    public void TheInstalledPackagesInventoryCompletely()
    {
        Assert.SkipUnless(TestData.Setting("CLIENT_ARCHIVE_MEASURE") == "1", "Set TF_CLIENT_ARCHIVE_MEASURE=1 to scan the installed clients completely (read only).");
        var lines = new List<string>();
        var scans = new List<FolderScan>();
        foreach (string package in Core.Assets.TibiaClientPaths.FindInstalledPackages())
        {
            string folder = Path.Combine(Core.Assets.TibiaClientPaths.PackagesRoot, package);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            _ = FolderInspector.Inspect(folder);
            var firstInspection = watch.Elapsed;
            watch.Restart();
            var inspection = FolderInspector.Inspect(folder);
            var inspected = watch.Elapsed;
            var first = ClientInventory.Scan(folder, ct: TestContext.Current.CancellationToken);
            var second = ClientInventory.Scan(folder, ct: TestContext.Current.CancellationToken);
            scans.Add(second);
            var inv = second.Inventory;
            lines.Add($"{package}: {inspection.ClassDisplay}, version {inspection.Version.Display} ({inspection.Version.Status}; {inspection.Version.Clues.Count} clue(s)); inspected in {firstInspection.TotalMilliseconds:N0} ms (again: {inspected.TotalMilliseconds:N0} ms)");
            lines.Add($"  archived: {inv.Files.Count:N0} files, {inv.TotalBytes / 1048576.0:N1} MB, {inv.Directories.Count} empty folder(s); listed by package.json {inv.Files.Count(f => f.ListedBy == "package.json"):N0}, " +
                      $"by assets.json {inv.Files.Count(f => f.ListedBy == "assets.json"):N0}, version files {inv.Files.Count(f => f.ListedBy == "version file"):N0}, not listed {inv.Files.Count(f => f.ListedBy == null):N0}");
            foreach (var e in inv.Exclusions) lines.Add($"  excluded: {e.Rule}: {e.Files:N0} files, {e.Bytes / 1048576.0:N1} MB ({e.Reason})");
            lines.Add($"  scan: first {first.Duration.TotalSeconds:N1} s, second {second.Duration.TotalSeconds:N1} s; consistent: {second.IsConsistent}; problems: {second.Problems.Count}; same fingerprint both times: {first.Fingerprint == second.Fingerprint}");
            foreach (string p in second.Problems.Where(p => !p.Contains(".png", StringComparison.OrdinalIgnoreCase)).Take(5)) lines.Add("  problem: " + p);
            lines.Add("  tools: " + string.Join(", ", inspection.Capabilities.Select(c => $"{c.Name} {(c.Available ? "yes" : "no")}")));
        }
        Assert.NotEmpty(scans);
        var union = scans.SelectMany(s => s.Files).GroupBy(f => f.Sha256).ToDictionary(g => g.Key, g => g.First().Size);
        lines.Add($"Both packages as independent copies: {scans.Sum(s => s.TotalBytes) / 1048576.0:N1} MB; shared storage: {union.Count:N0} objects, {union.Values.Sum() / 1048576.0:N1} MB.");
        if (TestData.Setting("CLIENT_ARCHIVE_REPORT") is { } report) File.WriteAllLines(report, lines);
        TestContext.Current.SendDiagnosticMessage(string.Join(Environment.NewLine, lines));
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
