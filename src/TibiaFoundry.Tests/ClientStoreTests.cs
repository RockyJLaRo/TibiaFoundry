using System.Text.Json;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Locations;

namespace TibiaFoundry.Tests;

/// <summary>
/// 7.3 client store: content-addressed objects, one complete manifest per version, registrations,
/// change detection, crash safety and portability. Every installation is a <see cref="FakeClient"/>
/// in a temporary folder, and every test checks that the installations are never changed.
/// </summary>
public sealed class ClientStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Env
    {
        public string Dir { get; } = TestData.NewTempDirectory("clientstore");
        public string StoreRoot => Path.Combine(Dir, "data", ClientStore.FolderName);
        public DateTime Now { get; set; } = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
        public ClientStore Open(string? root = null, string? program = null) => ClientStore.Open(root ?? StoreRoot, program, () => Now);
        public FakeClient Client(string name, string version = "15.33.9d148f", string seed = "base") =>
            FakeClient.Standard(Path.Combine(Dir, "packages", name), version, seed).Write();
    }

    private static ImportResult Import(ClientStore store, FakeClient client, string server, string? registrationId = null)
    {
        string id = registrationId ?? (server == ClientServers.Live ? ClientRegistration.InstalledLive : ClientRegistration.InstalledTest);
        var scan = ClientScanner.Scan(client.Folder, ct: Ct);
        Assert.True(scan.CanImport, string.Join(" ", scan.Problems));
        return store.Import(scan, server, null, id, ct: Ct);
    }

    private static int ObjectCount(ClientStore store) => store.Content.EnumerateObjects().Count();

    /// <summary>Every file of the version resolves, and its bytes are exactly the installation's.</summary>
    private static void AssertResolvesTo(ClientStore store, string key, FakeClient client)
    {
        var files = store.OpenVersion(key);
        foreach (var f in files.Manifest.Files)
        {
            string? path = files.PathOf(f.Path);
            Assert.NotNull(path);
            Assert.Equal(f.Sha256, FakeClient.Sha(File.ReadAllBytes(path)));
        }
        foreach (var (path, data) in client.Files)
            Assert.Equal(data, File.ReadAllBytes(files.PathOf(path)!));
    }

    // ------------------------------------------------------------------ discovery and scanning

    [Fact]
    public void TheVersionIsTheClientsListedFilesAndItsRccButNeverPersonalDataOrTheProgram()
    {
        var env = new Env();
        var client = env.Client("Tibia");
        var set = ClientScanner.Discover(client.Folder);

        Assert.Equal("assets.json", set.FileList);
        Assert.Equal("15.33.9d148f", set.ClientVersion);
        var paths = set.Files.Select(f => f.Path).ToHashSet();
        foreach (string expected in client.Files.Keys) Assert.Contains(expected, paths);
        Assert.Contains("assets/catalog-content.json", paths);
        Assert.Contains("package.json", paths);
        Assert.Contains("assets.json", paths);
        Assert.Equal(ClientFileKinds.Rcc, set.Files.Single(f => f.Path == "bin/graphics_resources.rcc").Kind);
        Assert.Equal(ClientFileKinds.SpriteSheet, set.Files.Single(f => f.Path == "assets/sprites-aaaa.bmp.lzma").Kind);
        foreach (string never in new[] { "bin/client.exe", "conf/", "characterdata/", "minimap/", "screenshots/", "log/", "cache/", "-bak" })
            Assert.DoesNotContain(paths, p => p.Contains(never, StringComparison.Ordinal));
    }

    [Fact]
    public void AResourceFileTheClientDoesNotListIsFoundByItsHeaderAndEveryRccIsIncluded()
    {
        var env = new Env();
        var client = FakeClient.Standard(Path.Combine(env.Dir, "packages", "Tibia"));
        client.Files["bin/extra/other_resources.rcc"] = FakeClient.Rcc("second resource file");
        client.ListRccInPackageJson = false;
        client.Write();
        client.WriteFile("bin/plugins/unnamed.bin", FakeClient.Rcc("resource without the extension"));

        var set = ClientScanner.Discover(client.Folder);
        var rcc = set.Files.Where(f => f.Kind == ClientFileKinds.Rcc).Select(f => (f.Path, f.Origin)).ToList();
        Assert.Contains(("bin/graphics_resources.rcc", "header scan"), rcc);
        Assert.Contains(("bin/extra/other_resources.rcc", "header scan"), rcc);
        Assert.Contains(set.Notes, n => n.Contains("does not list", StringComparison.Ordinal));
        // A Qt resource file without the .rcc extension in bin is also found by its header…
        Assert.Contains(set.Files, f => f.Path == "bin/plugins/unnamed.bin" && f.Kind == ClientFileKinds.Rcc);
        // …but nothing in the personal folders (cache) is read: the header scan does not even open them.
        Assert.DoesNotContain(set.Files, f => f.Path.StartsWith("cache/", StringComparison.Ordinal));
        Assert.DoesNotContain(ClientScanner.FindResourceFiles(client.Folder), p => p.StartsWith("cache/", StringComparison.Ordinal));
    }

    [Fact]
    public void AFileThatDiffersFromTheClientsOwnListStopsTheImport()
    {
        var env = new Env();
        var client = env.Client("Tibia");
        client.WriteFile("assets/sprites-aaaa.bmp.lzma", FakeClient.Bytes("half-updated sheet"));

        var scan = ClientScanner.Scan(client.Folder, ct: Ct);
        Assert.False(scan.CanImport);
        Assert.Contains(scan.Problems, p => p.Contains("sprites-aaaa", StringComparison.Ordinal) && p.Contains("launcher", StringComparison.Ordinal));
        var store = env.Open();
        Assert.Throws<InvalidOperationException>(() => store.Import(scan, ClientServers.Live, null, ClientRegistration.InstalledLive, ct: Ct));
        Assert.Empty(store.Registry.Versions);
        Assert.Equal(0, ObjectCount(store));
    }

    [Fact]
    public void AMissingListedFileStopsTheImport()
    {
        var env = new Env();
        var client = env.Client("Tibia");
        File.Delete(client.PathOf("sounds/sound-0002.ogg"));
        var scan = ClientScanner.Scan(client.Folder, ct: Ct);
        Assert.False(scan.CanImport);
        Assert.Contains(scan.Problems, p => p.Contains("sound-0002.ogg", StringComparison.Ordinal) && p.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutAssetsJsonTheCatalogsGiveTheFileSet()
    {
        var env = new Env();
        var client = FakeClient.Standard(Path.Combine(env.Dir, "packages", "Custom"));
        client.WriteAssetsJson = false;
        client.Write();
        var scan = ClientScanner.Scan(client.Folder, ct: Ct);
        Assert.True(scan.CanImport, string.Join(" ", scan.Problems));
        Assert.Equal("fallback", scan.FileSet.FileList);
        Assert.Contains(scan.FileSet.Notes, n => n.Contains("no readable assets.json", StringComparison.Ordinal));
        var paths = scan.Files.Select(f => f.Path).ToHashSet();
        Assert.Contains("assets/sprites-aaaa.bmp.lzma", paths);
        Assert.Contains("assets/appearances-1111.dat", paths);
        Assert.Contains("sounds/sounds-2222.dat", paths);
        Assert.Contains("bin/graphics_resources.rcc", paths);
    }

    // ------------------------------------------------------------------ deduplication

    [Fact]
    public void IdenticalClientsStoreTheirContentOnceAcrossLiveAndTest()
    {
        var env = new Env();
        var live = env.Client("Tibia");
        var test = env.Client("TibiaExternal");
        var store = env.Open();

        var first = Import(store, live, ClientServers.Live);
        int objects = ObjectCount(store);
        Assert.False(first.AlreadyStored);
        Assert.Equal(first.Version.Files, objects);

        var second = Import(store, test, ClientServers.Test);
        Assert.False(second.AlreadyStored);
        Assert.Equal(0, second.NewObjects);
        Assert.Equal(0, second.NewBytes);
        Assert.Equal(objects, ObjectCount(store));
        Assert.Equal("Live_Server/Client_15.33.9d148f", first.Version.Key);
        Assert.Equal("Test_Server/Client_15.33.9d148f", second.Version.Key);
        Assert.True(File.Exists(Path.Combine(env.StoreRoot, "Live_Server", "Client_15.33.9d148f", "manifest.json")));
        AssertResolvesTo(store, first.Version.Key, live);
        AssertResolvesTo(store, second.Version.Key, test);
        Assert.Equal(store.OpenVersion(first.Version.Key).PathOf("assets/sprites-bbbb.bmp.lzma"), store.OpenVersion(second.Version.Key).PathOf("assets/sprites-bbbb.bmp.lzma"));
    }

    [Fact]
    public void OnlyNewContentIsStoredAndEveryManifestStaysComplete()
    {
        var env = new Env();
        var older = env.Client("Old", "15.32.aaaaaa", "old");
        var newer = FakeClient.Standard(Path.Combine(env.Dir, "packages", "New"), "15.33.9d148f", "new");
        newer.Files.Remove("sounds/sound-0002.ogg");
        newer.Files["sounds/sound-0003.ogg"] = FakeClient.Bytes("OggS brand new sound");
        newer.Write();
        var store = env.Open();

        var a = Import(store, older, ClientServers.Live);
        var b = Import(store, newer, ClientServers.Live);
        var ma = store.OpenVersion(a.Version.Key).Manifest;
        var mb = store.OpenVersion(b.Version.Key).Manifest;
        var newHashes = mb.Files.Select(f => f.Sha256).Except(ma.Files.Select(f => f.Sha256)).ToList();
        Assert.Equal(newHashes.Count, b.NewObjects);
        Assert.Equal(ma.Files.Select(f => f.Sha256).Union(mb.Files.Select(f => f.Sha256)).Count(), ObjectCount(store));
        // Same bytes → same object; different bytes → another object.
        Assert.Equal(ma.Find("assets/sprites-bbbb.bmp.lzma")!.Sha256, mb.Find("assets/sprites-bbbb.bmp.lzma")!.Sha256);
        Assert.NotEqual(ma.Find("assets/appearances-1111.dat")!.Sha256, mb.Find("assets/appearances-1111.dat")!.Sha256);
        // Both manifests list their whole file set: the removed sound only in the older one.
        Assert.NotNull(ma.Find("sounds/sound-0002.ogg"));
        Assert.Null(mb.Find("sounds/sound-0002.ogg"));
        AssertResolvesTo(store, a.Version.Key, older);
        AssertResolvesTo(store, b.Version.Key, newer);
    }

    [Fact]
    public void IdenticalRccFilesAreStoredOnceAndAChangedOneIsKeptForEachVersion()
    {
        var env = new Env();
        var live = env.Client("Tibia", "15.33.9d148f", "same");
        var test = FakeClient.Standard(Path.Combine(env.Dir, "packages", "TibiaExternal"), "15.34.000001", "same");
        test.Files["assets/appearances-1111.dat"] = FakeClient.Bytes("other appearances");
        test.Write();
        var store = env.Open();
        var a = Import(store, live, ClientServers.Live);
        var b = Import(store, test, ClientServers.Test);
        string rccA = store.OpenVersion(a.Version.Key).PathOf("bin/graphics_resources.rcc")!;
        Assert.Equal(rccA, store.OpenVersion(b.Version.Key).PathOf("bin/graphics_resources.rcc"));

        test.Files["bin/graphics_resources.rcc"] = FakeClient.Rcc("changed interface");
        test.Version = "15.34.000002";
        test.Write();
        var c = Import(store, test, ClientServers.Test);
        string rccC = store.OpenVersion(c.Version.Key).PathOf("bin/graphics_resources.rcc")!;
        Assert.NotEqual(rccA, rccC);
        Assert.Equal(FakeClient.Rcc("interface same"), File.ReadAllBytes(store.OpenVersion(b.Version.Key).PathOf("bin/graphics_resources.rcc")!));
        Assert.Equal(FakeClient.Rcc("changed interface"), File.ReadAllBytes(rccC));
        Assert.Equal(["bin/graphics_resources.rcc"], store.OpenVersion(c.Version.Key).ResourceFiles.Select(f => f.Path));
        Assert.Contains(store.Usage().ByKind, k => k.Kind == ClientFileKinds.Rcc && k.Objects == 2);
    }

    // ------------------------------------------------------------------ version independence

    [Fact]
    public void RemovingAVersionNeverBreaksAnotherAndOnlyUnreferencedContentIsReclaimed()
    {
        var env = new Env();
        var a = env.Client("A", "15.32.aaaaaa", "a");
        var b = env.Client("B", "15.33.bbbbbb", "b");
        var store = env.Open();
        var va = Import(store, a, ClientServers.Live);
        var vb = Import(store, b, ClientServers.Live);
        int before = ObjectCount(store);

        store.RemoveVersion(va.Version.Key, new HashSet<string>());
        Assert.Equal(before, ObjectCount(store)); // removal alone never deletes objects
        AssertResolvesTo(store, vb.Version.Key, b);

        // Freshly written objects are protected for an hour…
        Assert.Equal(0, store.ReclaimSpace(Ct).ObjectsRemoved);
        env.Now = env.Now.AddHours(2);
        foreach (var o in store.Content.EnumerateObjects().ToList())
            File.SetLastWriteTimeUtc(store.Content.ObjectPath(o.Sha256), env.Now.AddHours(-1.5));
        // …then exactly the content only the removed version used is freed.
        var stillNeeded = store.OpenVersion(vb.Version.Key).Manifest.Files.Select(f => f.Sha256).ToHashSet();
        var cleanup = store.ReclaimSpace(Ct);
        Assert.Equal(before - stillNeeded.Count, cleanup.ObjectsRemoved);
        Assert.Equal(stillNeeded.Count, ObjectCount(store));
        AssertResolvesTo(store, vb.Version.Key, b);
        Assert.Empty(store.Verify(null, null, Ct).Problems);
    }

    [Fact]
    public void RenamingOrRemovingTheFirstImportLeavesTheOthersByteIdentical()
    {
        var env = new Env();
        var a = env.Client("A", "15.32.aaaaaa", "a");
        var b = env.Client("B", "15.33.bbbbbb", "b");
        var store = env.Open();
        var va = Import(store, a, ClientServers.Live);
        var vb = Import(store, b, ClientServers.Test);
        string manifestB = vb.Version.ManifestPath(store.Root);
        byte[] bytesB = File.ReadAllBytes(manifestB);

        store.RenameVersion(va.Version.Key, "My oldest client");
        Assert.Equal("My oldest client", store.Registry.Version(va.Version.Key)!.Label);
        Assert.Equal(va.Version.Key, store.Registry.Version(va.Version.Key)!.Key);
        Assert.Equal(bytesB, File.ReadAllBytes(manifestB));

        store.RemoveVersion(va.Version.Key, new HashSet<string>());
        Assert.Equal(bytesB, File.ReadAllBytes(manifestB));
        Assert.Empty(store.Verify([vb.Version.Key], null, Ct).Problems);
        Assert.DoesNotContain("parent", File.ReadAllText(manifestB), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheVersionInUseCannotBeRemoved()
    {
        var env = new Env();
        var store = env.Open();
        var v = Import(store, env.Client("A"), ClientServers.Live);
        Assert.Throws<InvalidOperationException>(() => store.RemoveVersion(v.Version.Key, new HashSet<string> { v.Version.Key }));
        Assert.NotNull(store.Registry.Version(v.Version.Key));
    }

    // ------------------------------------------------------------------ change detection

    [Fact]
    public void RescanningAnUnchangedClientCreatesNothing()
    {
        var env = new Env();
        var client = env.Client("Tibia");
        var store = env.Open();
        var first = Import(store, client, ClientServers.Live);
        var manifestBytes = File.ReadAllBytes(first.Version.ManifestPath(store.Root));
        int objects = ObjectCount(store);

        var again = Import(store, client, ClientServers.Live);
        Assert.True(again.AlreadyStored);
        Assert.Equal(first.Version.Key, again.Version.Key);
        Assert.Single(store.Registry.Versions);
        Assert.Equal(objects, ObjectCount(store));
        Assert.Equal(manifestBytes, File.ReadAllBytes(first.Version.ManifestPath(store.Root)));
        Assert.Single(Directory.GetDirectories(Path.Combine(store.Root, ClientServers.Live)));
    }

    [Fact]
    public void ChangesAreSummarizedAndTheOldContentStaysWithTheOldVersion()
    {
        var env = new Env();
        var client = env.Client("Tibia", "15.33.9d148f", "v1");
        var store = env.Open();
        var v1 = Import(store, client, ClientServers.Live);
        var oldManifest = store.OpenVersion(v1.Version.Key).Manifest;
        var oldRanges = ClientScanner.SheetRanges(store.OpenVersion(v1.Version.Key).PathOf("assets/catalog-content.json"));

        // The launcher updates: a sheet is replaced (new name, same sprite range), a sound added, one removed.
        client.Files.Remove("assets/sprites-aaaa.bmp.lzma");
        client.Files["assets/sprites-cccc.bmp.lzma"] = FakeClient.Bytes("sheet 1-10 v2");
        client.SheetFirst["assets/sprites-cccc.bmp.lzma"] = 1;
        client.SheetFirst["assets/sprites-bbbb.bmp.lzma"] = 11;
        client.Files["sounds/sound-0009.ogg"] = FakeClient.Bytes("OggS added");
        client.Files.Remove("sounds/sound-0001.ogg");
        client.Files["bin/graphics_resources.rcc"] = FakeClient.Rcc("interface v2");
        client.Version = "15.34.0aa000";
        client.Write();
        var scan = ClientScanner.Scan(client.Folder, ct: Ct);
        var diff = ManifestDiff.Compare(oldManifest.Files, scan.Files, oldRanges, ClientScanner.SheetRanges(client.PathOf("assets/catalog-content.json")));
        Assert.False(diff.Unchanged);
        string summary = diff.Summary();
        Assert.Contains("1 sprite sheet replaced", summary);
        Assert.Contains("1 RCC file changed", summary);
        Assert.Contains("1 sound added", summary);
        Assert.Contains("1 sound removed", summary);

        var v2 = store.Import(scan, ClientServers.Live, null, ClientRegistration.InstalledLive, ct: Ct);
        Assert.Equal("Live_Server/Client_15.34.0aa000", v2.Version.Key);
        Assert.Equal(FakeClient.Bytes("sheet 1-10 v1"), File.ReadAllBytes(store.OpenVersion(v1.Version.Key).PathOf("assets/sprites-aaaa.bmp.lzma")!));
        Assert.Null(store.OpenVersion(v2.Version.Key).PathOf("assets/sprites-aaaa.bmp.lzma"));
        Assert.NotNull(store.OpenVersion(v1.Version.Key).PathOf("sounds/sound-0001.ogg"));
        Assert.Null(store.OpenVersion(v2.Version.Key).PathOf("sounds/sound-0001.ogg"));
        Assert.Equal(2, store.Registry.Registration(ClientRegistration.InstalledLive)!.Versions.Count);
    }

    [Fact]
    public void TheSameVersionTextWithOtherContentBecomesASeparateBuild()
    {
        var env = new Env();
        var store = env.Open();
        var a = Import(store, env.Client("A", "15.33.9d148f", "first"), ClientServers.Live);
        var b = Import(store, env.Client("B", "15.33.9d148f", "second"), ClientServers.Live);
        Assert.Equal("Live_Server/Client_15.33.9d148f", a.Version.Key);
        Assert.Equal("Live_Server/Client_15.33.9d148f_" + b.Version.Fingerprint[..8], b.Version.Key);
        Assert.Equal(("15.33", "9d148f"), (b.Version.DisplayVersion, b.Version.Build));
    }

    [Fact]
    public void ChangesOutsideTheVersionFilesAreNotAChange()
    {
        var env = new Env();
        var client = env.Client("Tibia");
        var before = ClientScanner.Scan(client.Folder, ct: Ct);
        client.WriteFile("bin/client.exe", FakeClient.Bytes("updated program"));
        client.WriteFile("conf/clientoptions.json", FakeClient.Bytes("{\"changed\":true}"));
        client.WriteFile("assets/appearances-new.dat-bak", FakeClient.Bytes("backup"));
        client.WriteFile("log/client.log", FakeClient.Bytes("more log"));
        var after = ClientScanner.Scan(client.Folder, ct: Ct);
        Assert.Equal(before.Fingerprint, after.Fingerprint);
        Assert.True(ManifestDiff.Compare(before.Files, after.Files).Unchanged);
    }

    // ------------------------------------------------------------------ failures and recovery

    [Fact]
    public void ACancelledImportLeavesNoTraceAndTheStoredVersionsStayUsable()
    {
        var env = new Env();
        var store = env.Open();
        var kept = Import(store, env.Client("A", "15.32.aaaaaa", "a"), ClientServers.Live);
        int objects = ObjectCount(store);
        var other = env.Client("B", "15.33.bbbbbb", "b");
        var scan = ClientScanner.Scan(other.Folder, ct: Ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var progress = new SynchronousProgress(p => { if (p.Done >= 1) cts.Cancel(); });
        // Make the import report progress after every file.
        Assert.ThrowsAny<OperationCanceledException>(() => store.Import(scan, ClientServers.Live, null, ClientRegistration.InstalledLive, progress, cts.Token));

        Assert.Single(store.Registry.Versions);
        Assert.Equal(objects, ObjectCount(store));
        Assert.False(Directory.Exists(Path.Combine(store.Root, "Live_Server", "Client_15.33.bbbbbb")));
        Assert.Empty(Directory.Exists(store.Content.IncomingRoot) ? Directory.GetFileSystemEntries(store.Content.IncomingRoot) : []);
        Assert.Empty(store.Verify(null, null, Ct).Problems);
        AssertResolvesTo(store, kept.Version.Key, env.Client("A", "15.32.aaaaaa", "a"));
    }

    private sealed class SynchronousProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }

    [Fact]
    public void AFileThatChangesBetweenTheScanAndTheImportIsRefused()
    {
        var env = new Env();
        var client = env.Client("Tibia");
        var scan = ClientScanner.Scan(client.Folder, ct: Ct);
        client.WriteFile("sounds/sound-0001.ogg", FakeClient.Bytes("OggS replaced while importing"));
        var store = env.Open();
        Assert.Throws<ContentMismatchException>(() => store.Import(scan, ClientServers.Live, null, ClientRegistration.InstalledLive, ct: Ct));
        Assert.Empty(store.Registry.Versions);
        Assert.Equal(0, ObjectCount(store));
    }

    [Fact]
    public void ACrashDuringAnImportOrACleanupIsRepairedAtTheNextStart()
    {
        var env = new Env();
        var store = env.Open();
        var kept = Import(store, env.Client("A"), ClientServers.Live);
        var keptFiles = store.OpenVersion(kept.Version.Key).Manifest.Files;

        // A crashed import: its journal, a half-written file, and an object it had already published.
        byte[] orphan = FakeClient.Bytes("published, but its manifest never was");
        string orphanSha = FakeClient.Sha(orphan);
        string incoming = Path.Combine(store.Content.IncomingRoot, "crashed");
        Directory.CreateDirectory(incoming);
        File.WriteAllText(Path.Combine(incoming, ContentStore.JournalFile), JsonSerializer.Serialize(new { id = "crashed", startedUtc = env.Now, hashes = new[] { orphanSha, keptFiles[0].Sha256 } }));
        File.WriteAllBytes(Path.Combine(incoming, "x.part"), [1, 2]);
        Directory.CreateDirectory(Path.GetDirectoryName(store.Content.ObjectPath(orphanSha))!);
        File.WriteAllBytes(store.Content.ObjectPath(orphanSha), orphan);
        Directory.CreateDirectory(Path.Combine(store.Root, "Live_Server", "Client_half"));

        // A crashed cleanup: an object still referenced was moved to the trash, and one that is not.
        var referenced = keptFiles[1];
        string trash = Path.Combine(store.Content.TrashRoot, "crashed");
        Directory.CreateDirectory(trash);
        File.SetAttributes(store.Content.ObjectPath(referenced.Sha256), FileAttributes.Normal);
        File.Move(store.Content.ObjectPath(referenced.Sha256), Path.Combine(trash, referenced.Sha256));
        byte[] garbage = FakeClient.Bytes("nobody needs this");
        File.WriteAllBytes(Path.Combine(trash, FakeClient.Sha(garbage)), garbage);

        var reopened = env.Open();
        Assert.Contains(reopened.OpenNotes, n => n.Contains("interrupted import", StringComparison.Ordinal));
        Assert.Contains(reopened.OpenNotes, n => n.Contains("interrupted cleanup", StringComparison.Ordinal));
        Assert.False(File.Exists(reopened.Content.ObjectPath(orphanSha)));
        Assert.True(reopened.Content.Contains(keptFiles[0].Sha256, keptFiles[0].Size)); // named by the journal, but referenced: kept
        Assert.True(reopened.Content.Contains(referenced.Sha256, referenced.Size));    // put back from the trash
        Assert.False(Directory.Exists(trash));
        Assert.False(Directory.Exists(incoming));
        Assert.False(Directory.Exists(Path.Combine(reopened.Root, "Live_Server", "Client_half")));
        Assert.Empty(reopened.Verify(null, null, Ct).Problems);
    }

    [Fact]
    public void NothingIsRemovedWhenAManifestCannotBeRead()
    {
        var env = new Env();
        var store = env.Open();
        var a = Import(store, env.Client("A", "15.32.aaaaaa", "a"), ClientServers.Live);
        var b = Import(store, env.Client("B", "15.33.bbbbbb", "b"), ClientServers.Live);
        store.RemoveVersion(a.Version.Key, new HashSet<string>());
        File.WriteAllText(b.Version.ManifestPath(store.Root), "{ damaged");
        env.Now = env.Now.AddDays(1);
        foreach (var o in store.Content.EnumerateObjects().ToList()) File.SetLastWriteTimeUtc(store.Content.ObjectPath(o.Sha256), env.Now.AddDays(-1));
        int objects = ObjectCount(store);
        var ex = Assert.Throws<InvalidOperationException>(() => store.ReclaimSpace(Ct));
        Assert.Contains("Nothing was removed", ex.Message);
        Assert.Equal(objects, ObjectCount(store));
        Assert.Equal(VersionStatus.Damaged, env.Open().Registry.Version(b.Version.Key)!.Status);
    }

    [Fact]
    public void AnObjectThatIsMissingOrDamagedIsReportedAndTheVersionIsNotUsed()
    {
        var env = new Env();
        var store = env.Open();
        var v = Import(store, env.Client("A"), ClientServers.Live);
        var file = store.OpenVersion(v.Version.Key).Manifest.Find("assets/appearances-1111.dat")!;
        string path = store.Content.ObjectPath(file.Sha256);
        File.SetAttributes(path, FileAttributes.Normal);
        byte[] bytes = File.ReadAllBytes(path);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        var result = store.Verify(null, null, Ct);
        Assert.Single(result.Problems);
        Assert.Equal(VersionStatus.Damaged, store.Registry.Version(v.Version.Key)!.Status);
        File.Delete(path);
        Assert.Throws<InvalidDataException>(() => env.Open().OpenVersion(v.Version.Key));
    }

    [Fact]
    public void ADamagedRegistryIsKeptAsideAndRebuiltFromTheManifests()
    {
        var env = new Env();
        var store = env.Open();
        var v = Import(store, env.Client("A"), ClientServers.Live);
        File.WriteAllText(Path.Combine(store.Root, ClientRegistry.FileName), "not json");
        var reopened = env.Open();
        Assert.Contains(reopened.OpenNotes, n => n.Contains("rebuilt", StringComparison.Ordinal));
        Assert.Single(Directory.GetFiles(reopened.Root, ClientRegistry.FileName + ".damaged-*"));
        Assert.NotNull(reopened.Registry.Version(v.Version.Key));
        Assert.NotNull(reopened.Registry.Registration(ClientRegistration.InstalledLive));
        Assert.NotNull(reopened.OpenVersion(v.Version.Key).PathOf("assets/catalog-content.json"));
    }

    [Fact]
    public void OnlyOneWriterAtATime()
    {
        var env = new Env();
        var store = env.Open();
        using (store.Content.Lock(TimeSpan.Zero))
            Assert.Throws<StoreBusyException>(() => store.Content.Lock(TimeSpan.FromMilliseconds(200)));
        using (store.Content.Lock(TimeSpan.Zero)) { }
    }

    // ------------------------------------------------------------------ registrations and portability

    [Fact]
    public void RegistrationsPersistAndAMovedFolderIsLocatedAgain()
    {
        var env = new Env();
        var client = env.Client("Copy", "15.25.0e418e");
        var store = env.Open();
        var r = store.RegisterFolder(client.Folder, ClientServers.Test, "Old test client");
        Assert.Same(r, store.RegisterFolder(client.Folder, ClientServers.Test, "again"));
        var v = Import(store, client, ClientServers.Test, r.Id);

        var reopened = env.Open();
        var again = reopened.Registry.Registration(r.Id)!;
        Assert.Equal("Old test client", again.Label);
        Assert.Equal(client.Folder, again.ResolveSource(null));
        Assert.Equal([v.Version.Key], again.Versions);
        Assert.Equal(3, reopened.Registry.Registrations.Count); // + the two installed (automatic) entries

        // The folder moves away: the imported version still works without it.
        string moved = client.Folder + "_moved";
        Directory.Move(client.Folder, moved);
        Assert.False(Directory.Exists(again.ResolveSource(null)));
        AssertResolvesTo(reopened, v.Version.Key, FakeClient.Standard(moved, "15.25.0e418e"));
        reopened.Relocate(r.Id, moved);
        Assert.Equal(moved, env.Open().Registry.Registration(r.Id)!.ResolveSource(null));
    }

    [Fact]
    public void ThePortableStoreKeepsWorkingAfterTheProgramFolderIsMovedAndHasNoAbsolutePaths()
    {
        var env = new Env();
        string program = Path.Combine(env.Dir, "usb", "TibiaFoundry");
        string data = Path.Combine(program, "TibiaFoundry-Data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), "{\"existing\":true}");
        var client = FakeClient.Standard(Path.Combine(env.Dir, "usb", "Clients", "15.25"), "15.25.0e418e").Write();
        var store = env.Open(Path.Combine(data, ClientStore.FolderName), program);
        var r = store.RegisterFolder(client.Folder, ClientServers.Test, null);
        Assert.Equal(Path.Combine("..", "Clients", "15.25"), r.RelativeToProgram);
        var v = Import(store, client, ClientServers.Test, r.Id);

        foreach (string file in Directory.EnumerateFiles(store.Root, "*.json", SearchOption.AllDirectories))
            Assert.DoesNotContain(store.Root.Replace("\\", "\\\\"), File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(env.Dir.Replace("\\", "\\\\"), File.ReadAllText(v.Version.ManifestPath(store.Root)), StringComparison.OrdinalIgnoreCase);

        // The whole stick gets another place (another drive letter or folder).
        string elsewhere = Path.Combine(env.Dir, "other-drive");
        CopyTree(Path.Combine(env.Dir, "usb"), elsewhere);
        DeleteTree(Path.Combine(env.Dir, "usb"));
        string newProgram = Path.Combine(elsewhere, "TibiaFoundry");
        var moved = env.Open(Path.Combine(newProgram, "TibiaFoundry-Data", ClientStore.FolderName), newProgram);
        Assert.Empty(moved.Verify(null, null, Ct).Problems);
        AssertResolvesTo(moved, v.Version.Key, FakeClient.Standard(Path.Combine(elsewhere, "Clients", "15.25"), "15.25.0e418e"));
        Assert.Equal(Path.Combine(elsewhere, "Clients", "15.25"), moved.Registry.Registration(r.Id)!.ResolveSource(newProgram));
        Assert.Equal("{\"existing\":true}", File.ReadAllText(Path.Combine(newProgram, "TibiaFoundry-Data", "settings.json")));
    }

    /// <summary>Deletes a folder like Explorer does (stored objects are read-only).</summary>
    private static void DeleteTree(string folder)
    {
        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(folder, recursive: true);
    }

    private static void CopyTree(string from, string to)
    {
        foreach (string dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            File.Copy(file, target);
            File.SetAttributes(target, File.GetAttributes(file));
        }
    }

    [Fact]
    public void TheStoreMovesToAnotherFolderAndAnInterruptedMoveCanBeRepeated()
    {
        var env = new Env();
        var store = env.Open();
        var client = env.Client("A");
        var v = Import(store, client, ClientServers.Live);
        string target = Path.Combine(env.Dir, "elsewhere", ClientStore.FolderName);

        // An earlier attempt stopped half-way (its marker is there): it is cleared and done again.
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, ".move-in-progress"), "");
        File.WriteAllText(Path.Combine(target, "partial.tmp"), "x");
        ClientStore.CopyStore(store.Root, target, ct: Ct);
        Assert.False(File.Exists(Path.Combine(target, ".move-in-progress")));
        Assert.False(File.Exists(Path.Combine(target, "partial.tmp")));
        AssertResolvesTo(env.Open(target), v.Version.Key, client);

        ClientStore.DeleteStore(store.Root);
        Assert.Empty(Directory.EnumerateFileSystemEntries(store.Root));
        // A folder that is not empty and not an interrupted move is refused.
        string busy = Path.Combine(env.Dir, "busy");
        Directory.CreateDirectory(busy);
        File.WriteAllText(Path.Combine(busy, "mine.txt"), "keep");
        Assert.Throws<InvalidOperationException>(() => ClientStore.CopyStore(target, busy, ct: Ct));
        Assert.Throws<InvalidOperationException>(() => ClientStore.DeleteStore(busy));
        Assert.True(File.Exists(Path.Combine(busy, "mine.txt")));
    }

    [Fact]
    public void ARestoreOfA73RevisionIsAPlainCopyAndNeverGoesIntoAClientOrTheStore()
    {
        var env = new Env();
        var store = env.Open();
        var client = env.Client("Tibia");
        var v = Import(store, client, ClientServers.Live);
        string target = Path.Combine(env.Dir, "export");
        var plan = store.PlanRebuild(v.Version.Key, target, [Path.Combine(env.Dir, "packages")]);
        Assert.Equal("Restore folder", plan.Kind);   // a 7.3 revision is not a complete installation
        var report = store.Rebuild(plan, ct: Ct);
        Assert.True(report.ContentComplete);
        Assert.False(report.InstallationComplete);
        foreach (var (path, data) in client.Files)
            Assert.Equal(data, File.ReadAllBytes(Path.Combine(target, path.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(File.Exists(Path.Combine(target, "assets", "catalog-content.json")));
        Assert.False(Directory.Exists(Path.Combine(target, "conf")));

        Assert.Throws<InvalidOperationException>(() => store.PlanRebuild(v.Version.Key, target, [])); // not empty
        Assert.Throws<InvalidOperationException>(() => store.PlanRebuild(v.Version.Key, Path.Combine(env.Dir, "packages", "new"), [Path.Combine(env.Dir, "packages")]));
        Assert.Throws<InvalidOperationException>(() => store.PlanRebuild(v.Version.Key, Path.Combine(store.Root, "x"), []));
    }

    [Fact]
    public void NoOperationEverChangesAClientInstallation()
    {
        var env = new Env();
        var live = env.Client("Tibia", "15.33.9d148f", "a");
        var test = env.Client("TibiaExternal", "15.25.0e418e", "b");
        var before = (FakeClient.Snapshot(live.Folder), FakeClient.Snapshot(test.Folder));

        var store = env.Open();
        var r = store.RegisterFolder(test.Folder, ClientServers.Test, null);
        var a = Import(store, live, ClientServers.Live);
        var b = Import(store, test, ClientServers.Test, r.Id);
        Import(store, test, ClientServers.Test, r.Id);
        _ = LaunchCheck.Run(live.Folder, null, quick: false, new InventoryOptions(), ct: Ct);
        store.Verify(null, null, Ct);
        store.Rebuild(store.PlanRebuild(a.Version.Key, Path.Combine(env.Dir, "export"), []), ct: Ct);
        store.RenameVersion(b.Version.Key, "renamed");
        store.RemoveVersion(a.Version.Key, new HashSet<string>());
        env.Now = env.Now.AddDays(1);
        store.ReclaimSpace(Ct);
        store.RemoveRegistration(r.Id, alsoVersions: true, new HashSet<string>());
        ClientStore.CopyStore(store.Root, Path.Combine(env.Dir, "moved"), ct: Ct);
        ClientStore.DeleteStore(store.Root);

        Assert.Equal(before.Item1, FakeClient.Snapshot(live.Folder));
        Assert.Equal(before.Item2, FakeClient.Snapshot(test.Folder));
    }

    [Fact]
    public void ManifestsAreValidatedAndTheirIdentityIsTheirContent()
    {
        var files = new[] { new ManifestFile("assets/a.dat", ClientFileKinds.Data, new string('a', 64), 3), new ManifestFile("bin/x.rcc", ClientFileKinds.Rcc, new string('b', 64), 4) };
        var m = ClientManifest.Create(ClientServers.Live, "15.33.9d148f", files.Reverse(), null, DateTime.UtcNow, "test");
        Assert.Equal(["assets/a.dat", "bin/x.rcc"], m.Files.Select(f => f.Path));
        Assert.Null(m.Validate());
        Assert.Equal(m.Id, ClientManifest.Parse(m.ToBytes(), "test").Id);
        // The kind is metadata; the content decides the identity.
        Assert.Equal(m.Fingerprint, ClientManifest.ComputeFingerprint(files.Select(f => f with { Kind = "asset" })));
        Assert.NotEqual(m.Fingerprint, ClientManifest.ComputeFingerprint(files.Select(f => f with { Size = f.Size + 1 })));

        string json = System.Text.Encoding.UTF8.GetString(m.ToBytes());
        foreach (string bad in new[]
                 {
                     json.Replace("assets/a.dat", "../a.dat"),
                     json.Replace("assets/a.dat", "C:/a.dat"),
                     json.Replace(new string('a', 64), new string('A', 64)),
                     json.Replace("\"size\": 3", "\"size\": 5"),
                     json.Replace("\"format\": 2", "\"format\": 3"),   // a newer format
                     json.Replace("Live_Server", "Other"),
                 })
            Assert.Throws<InvalidDataException>(() => ClientManifest.Parse(System.Text.Encoding.UTF8.GetBytes(bad), "test"));
        Assert.Equal(("15.33", "9d148f"), ClientManifest.SplitVersion("15.33.9d148f"));
        Assert.Equal(("1533", null), ClientManifest.SplitVersion("1533"));
        Assert.Equal(ClientScope.TestServer, ClientServers.ScopeOf(ClientServers.Test));
    }

    // ------------------------------------------------------------------ architecture

    [Fact]
    public void OnlyStoreFilesWritesAndClientManagementHasNoNetworking()
    {
        string root = SourceRoot();
        var clientFiles = Directory.EnumerateFiles(Path.Combine(root, "TibiaFoundry.Core", "Clients"), "*.cs").ToList();
        Assert.True(clientFiles.Count >= 6);
        string[] writes =
        [
            "File.Write", "File.Create", "File.Copy", "File.Move", "File.Delete", "File.Replace", "File.Append", "File.SetAttributes", "File.Open(",
            "Directory.CreateDirectory", "Directory.Delete", "Directory.Move", "FileMode.Create", "FileMode.Append", "FileMode.OpenOrCreate", "FileMode.Truncate",
            "FileAccess.Write", "FileAccess.ReadWrite", "AtomicFile.", "new StreamWriter", ".Delete()", ".MoveTo(", ".CopyTo(", "SetLastWriteTime",
        ];
        string[] network = ["System.Net", "HttpClient", "WebClient", "WebRequest", "Socket", "Uri(", "Dns.", "WebSocket"];
        foreach (string file in clientFiles)
        {
            string source = File.ReadAllText(file);
            foreach (string n in network) Assert.False(source.Contains(n, StringComparison.Ordinal), $"{Path.GetFileName(file)} uses {n}: client management is local only");
            if (Path.GetFileName(file) == "StoreFiles.cs") continue;
            foreach (string w in writes) Assert.False(source.Contains(w, StringComparison.Ordinal), $"{Path.GetFileName(file)} uses {w}; only StoreFiles.cs may write (inside the store)");
        }
        // Every write of StoreFiles goes through the guard.
        string store = File.ReadAllText(Path.Combine(root, "TibiaFoundry.Core", "Clients", "StoreFiles.cs"));
        Assert.Contains("PathGuard.IsSameOrInside(Root, full)", store);
    }

    internal static string SourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "TibiaFoundry.App"))) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "TibiaFoundry.App"))) return Path.Combine(dir.FullName, "src");
        }
        throw new DirectoryNotFoundException("The source folder was not found.");
    }

    // ------------------------------------------------------------------ the real clients (read only)

    /// <summary>
    /// TF_CLIENT_STORE_MEASURE=1: scans the installed clients (read only) and, with
    /// TF_CLIENT_STORE_REPORT=&lt;file&gt;, writes the figures (time, files, size, overlap). Nothing is imported.
    /// </summary>
    [Fact]
    public void TheInstalledClientsScanWithoutProblems()
    {
        Assert.SkipUnless(TestData.Setting("CLIENT_STORE_MEASURE") == "1", "Set TF_CLIENT_STORE_MEASURE=1 to scan the installed clients (read only).");
        var lines = new List<string>();
        var scans = new List<ClientScan>();
        foreach (string package in Core.Assets.TibiaClientPaths.FindInstalledPackages())
        {
            string folder = Path.Combine(Core.Assets.TibiaClientPaths.PackagesRoot, package);
            var before = FakeClient.Snapshot(Path.Combine(folder, "assets"));
            var scan = ClientScanner.Scan(folder, ct: Ct);
            Assert.True(scan.CanImport, string.Join(" ", scan.Problems.Take(5)));
            Assert.Equal(before, FakeClient.Snapshot(Path.Combine(folder, "assets")));
            scans.Add(scan);
            lines.Add($"{package}: version {scan.ClientVersion}, file list {scan.FileSet.FileList}, {scan.Files.Count:N0} files, {scan.TotalBytes / 1048576.0:N1} MB, " +
                      $"RCC {string.Join(", ", scan.Files.Where(f => f.Kind == ClientFileKinds.Rcc).Select(f => $"{f.Path} {f.Size:N0} bytes"))}, scanned in {scan.Duration.TotalSeconds:N1} s");
        }
        Assert.NotEmpty(scans);
        var union = scans.SelectMany(s => s.Files).GroupBy(f => f.Sha256).ToDictionary(g => g.Key, g => g.First().Size);
        lines.Add($"As separate copies: {scans.Sum(s => s.TotalBytes) / 1048576.0:N1} MB; stored once: {union.Count:N0} objects, {union.Values.Sum() / 1048576.0:N1} MB.");
        if (TestData.Setting("CLIENT_STORE_REPORT") is { } report) File.WriteAllLines(report, lines);
        TestContext.Current.SendDiagnosticMessage(string.Join(Environment.NewLine, lines));
    }
}
