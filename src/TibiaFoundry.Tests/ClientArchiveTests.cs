using TibiaFoundry.Core.Clients;

namespace TibiaFoundry.Tests;

/// <summary>
/// 8.0 archive: complete installations and standalone folders, in shared (deduplicated) or
/// independent storage; completeness and exclusions; deletion, leases and storage accounting;
/// interruptions; 7.3 revisions kept as they are. Every client is a <see cref="FakeClient"/> in a
/// temporary folder, and the sources are checked to be unchanged.
/// </summary>
public sealed class ClientArchiveTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Env
    {
        public string Dir { get; } = TestData.NewTempDirectory("archive");
        public string StoreRoot => Path.Combine(Dir, "data", ClientStore.FolderName);
        public DateTime Now { get; set; } = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        public ClientStore Open() => ClientStore.Open(StoreRoot, null, () => Now);

        public FakeClient Client(string name, string version = "15.33.9d148f", string seed = "base") =>
            Rewrite(FakeClient.Standard(Path.Combine(Dir, "packages", name), version, seed));

        /// <summary>Writes the client again (as a launcher update would) with files no client list names, a nested extension-less file and an empty folder.</summary>
        public static FakeClient Rewrite(FakeClient c)
        {
            c.Write();
            c.WriteFile("Auction_Id_1.html", FakeClient.Bytes("<html>a saved page</html>"));
            c.WriteFile("bin/qt/plugins/platforms/qwindows", FakeClient.Bytes("no extension, nested"));
            Directory.CreateDirectory(c.PathOf("bin/empty"));
            return c;
        }
    }

    private static ImportResult Archive(ClientStore store, FakeClient client, string storage = ManifestStorage.Shared, string scope = ManifestScopes.CompleteInstallation,
        bool personal = false, string? registration = null)
    {
        var scan = ClientInventory.Scan(client.Folder, new InventoryOptions { IncludePersonalData = personal }, ct: Ct);
        Assert.True(scan.IsConsistent, string.Join(" ", scan.Problems));
        return store.Archive(new ArchiveRequest(scan, ClientServers.Live, scope, storage, SourceTypes.InstalledPackage, null, registration ?? ClientRegistration.InstalledLive,
            VersionEvidence.Collect(client.Folder)), ct: Ct);
    }

    /// <summary>Every file of the revision resolves to exactly the source's bytes, and nothing else is listed.</summary>
    private static void AssertExactly(ClientStore store, string key, string folder, bool personal = false)
    {
        var files = store.OpenVersion(key);
        var inventory = ClientInventory.Take(folder, new InventoryOptions { IncludePersonalData = personal });
        Assert.Equal(inventory.Files.Select(f => f.Path), files.Manifest.Files.Select(f => f.Path));
        foreach (var f in files.Manifest.Files)
            Assert.Equal(File.ReadAllBytes(Path.Combine(folder, f.Path)), File.ReadAllBytes(files.PathOf(f.Path)!));
        Assert.Equal(inventory.Directories, files.Manifest.Directories ?? []);
    }

    [Fact]
    public void ThePreviewSeparatesTheSizeAsRestoredFromWhatIsWrittenAndListsEveryCategory()
    {
        var env = new Env();
        var store = env.Open();
        var client = env.Client("Tibia");
        byte[] twice = FakeClient.Bytes("identical content, twice in the folder");
        client.WriteFile("notes/a.txt", twice);
        client.WriteFile("notes/b.txt", twice);
        var scan = ClientInventory.Scan(client.Folder, new InventoryOptions(), ct: Ct);

        var first = store.PreviewArchive(scan, ManifestScopes.CompleteInstallation, ManifestStorage.Shared, includesPersonalData: false);
        Assert.Equal(store.NeededBytes(scan.Files, ManifestStorage.Shared), first.NewBytes);   // the same figure as the free-space check
        Assert.Equal(twice.Length, first.ReusedBytes);   // the duplicate is stored once
        Assert.Equal(first.LogicalBytes, first.Categories.Where(c => c.State == ArchiveCategoryState.Included).Sum(c => c.Bytes));
        Assert.Equal(first.NewBytes, first.Categories.Sum(c => c.NewBytes));
        Assert.Equal(first.Files, first.Categories.Where(c => c.State == ArchiveCategoryState.Included).Sum(c => c.Files));
        Assert.All(first.Categories.Where(c => c.State == ArchiveCategoryState.Excluded), c => Assert.True(c.IsPersonal));
        Assert.Contains(first.Categories, c => c is { Name: "Explored map", State: ArchiveCategoryState.Excluded, Files: 1 });
        Assert.Contains(first.Categories, c => c is { Name: "Options and hotkeys", State: ArchiveCategoryState.Excluded });
        Assert.Equal(scan.Exclusions.Sum(e => e.Files), first.ExcludedFiles);
        Assert.True(first.HasExcludedPersonalData);

        var independent = store.PreviewArchive(scan, ManifestScopes.CompleteInstallation, ManifestStorage.Independent, includesPersonalData: false);
        Assert.Equal(independent.LogicalBytes, independent.NewBytes);   // every file is copied
        Assert.Equal(0, independent.ReusedBytes);

        Archive(store, client);
        var again = store.PreviewArchive(ClientInventory.Scan(client.Folder, new InventoryOptions(), ct: Ct), ManifestScopes.CompleteInstallation, ManifestStorage.Shared, false);
        Assert.Equal(0, again.NewBytes);   // everything is archived already
        Assert.Equal(again.LogicalBytes, again.ReusedBytes);

        var personal = ClientInventory.Scan(client.Folder, new InventoryOptions { IncludePersonalData = true }, ct: Ct);
        var withPersonal = store.PreviewArchive(personal, ManifestScopes.CompleteInstallation, ManifestStorage.Shared, true);
        Assert.False(withPersonal.HasExcludedPersonalData);
        Assert.Contains(withPersonal.Categories, c => c is { Name: "Logs", State: ArchiveCategoryState.Included, IsPersonal: true });
        Assert.Equal(first.Files + first.ExcludedFiles, withPersonal.Files);
    }

    [Fact]
    public void SharedStorageKeepsEachContentOnceAndEveryRevisionComplete()
    {
        var env = new Env();
        var store = env.Open();
        var v1 = env.Client("Tibia");
        var before = FakeClient.Snapshot(v1.Folder);
        var a = Archive(store, v1, personal: true);
        Assert.Equal(before, FakeClient.Snapshot(v1.Folder));
        Assert.StartsWith("Live_Server/Install_15.33.9d148f", a.Version.Key, StringComparison.Ordinal);
        Assert.True(a.Version.IsCompleteInstallation);
        AssertExactly(store, a.Version.Key, v1.Folder, personal: true);
        int objects = store.Content.EnumerateObjects().Count();

        // The launcher updates the client: one file changes, one is added. Only those are stored again.
        v1.Version = "15.34.aaaaaa";
        v1.Files["sounds/sound-0003.ogg"] = FakeClient.Bytes("OggS a new sound");
        v1.Files["assets/appearances-1111.dat"] = FakeClient.Bytes("appearances changed");
        Env.Rewrite(v1);
        var b = Archive(store, v1, personal: true);
        Assert.False(b.AlreadyStored);
        Assert.InRange(b.NewObjects, 1, 8);   // the changed and added files and the client's lists, nothing else
        Assert.Equal(objects + b.NewObjects, store.Content.EnumerateObjects().Count());
        AssertExactly(store, b.Version.Key, v1.Folder, personal: true);
        // The first revision still resolves exactly as it was (no chain of revisions).
        Assert.Equal(FakeClient.Sha(FakeClient.Bytes("appearances base")), store.OpenVersion(a.Version.Key).Manifest.Find("assets/appearances-1111.dat")!.Sha256);

        // The same content again is recognized; nothing new is written.
        var again = Archive(store, v1, personal: true);
        Assert.True(again.AlreadyStored);
        Assert.Equal(b.Version.Key, again.Version.Key);
    }

    [Fact]
    public void IndependentCopiesArePhysicallySeparateAndSurviveEachOthersRemoval()
    {
        var env = new Env();
        var store = env.Open();
        var v1 = env.Client("Tibia");
        var a = Archive(store, v1, ManifestStorage.Independent);
        v1.Files["sounds/sound-0003.ogg"] = FakeClient.Bytes("OggS another sound");
        Env.Rewrite(v1);
        var b = Archive(store, v1, ManifestStorage.Independent);

        Assert.Empty(store.Content.EnumerateObjects());   // nothing shared
        foreach (var r in new[] { a, b })
        {
            Assert.Equal(ManifestStorage.Independent, r.Version.EffectiveStorage);
            string files = Path.Combine(store.Root, r.Version.Key.Replace('/', Path.DirectorySeparatorChar), ClientStore.FilesFolder);
            Assert.Equal(r.Version.Files, Directory.EnumerateFiles(files, "*", SearchOption.AllDirectories).Count());
            Assert.All(Directory.EnumerateFiles(files, "*", SearchOption.AllDirectories), f => Assert.True(File.GetAttributes(f).HasFlag(FileAttributes.ReadOnly)));
            Assert.True(Directory.Exists(Path.Combine(files, "bin", "empty")));
        }
        string sameFileA = store.OpenVersion(a.Version.Key).PathOf("assets/catalog-content.json")!;
        string sameFileB = store.OpenVersion(b.Version.Key).PathOf("assets/catalog-content.json")!;
        Assert.NotEqual(sameFileA, sameFileB);
        Assert.Equal(File.ReadAllBytes(sameFileA), File.ReadAllBytes(sameFileB));

        store.RemoveVersion(a.Version.Key, new HashSet<string>());
        Assert.False(File.Exists(sameFileA));
        AssertExactly(store, b.Version.Key, v1.Folder);
        Assert.Empty(store.Verify(ct: Ct).Problems);
    }

    [Fact]
    public void PersonalDataIsExcludedVisiblyAndTheRevisionIsNotCalledComplete()
    {
        var env = new Env();
        var store = env.Open();
        var v1 = env.Client("Tibia");
        var r = Archive(store, v1);
        var m = store.OpenVersion(r.Version.Key).Manifest;
        Assert.Equal(ManifestScopes.CompleteInstallation, m.Scope);
        Assert.False(m.Complete);
        Assert.False(r.Version.IsCompleteInstallation);
        Assert.True(r.Version.ExcludedFiles > 0);
        Assert.Contains(m.Exclusions!, e => e.Rule == "characterdata/");
        Assert.DoesNotContain(m.Files, f => f.Path.StartsWith("conf/clientoptions", StringComparison.Ordinal));
        Assert.Contains(r.Notes, n => n.StartsWith("Left out on purpose", StringComparison.Ordinal));
    }

    [Fact]
    public void AnInconsistentScanIsRefusedUnlessKeptAsIncomplete()
    {
        var env = new Env();
        var store = env.Open();
        var v1 = env.Client("Tibia");
        File.WriteAllBytes(v1.PathOf("sounds/sound-0001.ogg"), FakeClient.Bytes("OggS half updated"));
        var scan = ClientInventory.Scan(v1.Folder, ct: Ct);
        Assert.False(scan.IsConsistent);
        var request = new ArchiveRequest(scan, ClientServers.Live, ManifestScopes.CompleteInstallation, ManifestStorage.Shared, SourceTypes.InstalledPackage, null,
            ClientRegistration.InstalledLive, VersionEvidence.Collect(v1.Folder));
        Assert.Throws<InvalidOperationException>(() => store.Archive(request, ct: Ct));
        Assert.Empty(store.Registry.Versions);
        Assert.Empty(store.Content.EnumerateObjects());

        var kept = store.Archive(request with { KeepIncomplete = true }, ct: Ct);
        var m = store.OpenVersion(kept.Version.Key).Manifest;
        Assert.False(m.IsContentComplete);
        Assert.Contains(m.Problems!, p => p.StartsWith("sounds/sound-0001.ogg", StringComparison.Ordinal));
        Assert.Null(m.Find("sounds/sound-0001.ogg"));
    }

    [Fact]
    public void AStandaloneFolderIsArchivedAsSuchWithItsUnknownFiles()
    {
        var env = new Env();
        var store = env.Open();
        var v1 = env.Client("Tibia");
        string folder = Path.Combine(env.Dir, "kept", "my 15.33 assets");
        foreach (string f in Directory.EnumerateFiles(v1.PathOf("assets"), "*", SearchOption.AllDirectories))
        {
            string t = Path.Combine(folder, Path.GetRelativePath(v1.PathOf("assets"), f));
            Directory.CreateDirectory(Path.GetDirectoryName(t)!);
            File.Copy(f, t);
        }
        File.WriteAllText(Path.Combine(folder, "my notes.txt"), "kept with the assets");
        var registration = store.RegisterFolder(folder, ClientServers.Live, "My assets");
        var scan = ClientInventory.Scan(folder, ct: Ct);
        var r = store.Archive(new ArchiveRequest(scan, ClientServers.Live, ManifestScopes.StandaloneFolder, ManifestStorage.Shared, SourceTypes.StandaloneFolder,
            "My assets", registration.Id, VersionEvidence.Collect(folder)), ct: Ct);
        Assert.StartsWith("Live_Server/Folder_My_assets", r.Version.Key, StringComparison.Ordinal);   // unknown version: named after the label
        Assert.Equal(ManifestScopes.StandaloneFolder, r.Version.EffectiveScope);
        Assert.False(r.Version.IsCompleteInstallation);
        Assert.NotNull(store.OpenVersion(r.Version.Key).Manifest.Find("my notes.txt"));
        AssertExactly(store, r.Version.Key, folder);
    }

    [Fact]
    public void RemovingASharedRevisionKeepsWhatOthersUseAndLeasesBlockRemoval()
    {
        var env = new Env();
        var store = env.Open();
        var v1 = env.Client("Tibia");
        var a = Archive(store, v1);
        v1.Files["assets/appearances-1111.dat"] = FakeClient.Bytes("appearances only in b");
        Env.Rewrite(v1);
        var b = Archive(store, v1);

        using (store.Lease(a.Version.Key, "rebuild"))
        {
            Assert.Contains(a.Version.Key, store.LeasedKeys());
            Assert.Throws<InvalidOperationException>(() => store.RemoveVersion(a.Version.Key, new HashSet<string>()));
        }
        Assert.DoesNotContain(a.Version.Key, store.LeasedKeys());

        var usages = store.RevisionUsages().ToDictionary(u => u.Key);
        Assert.Equal(usages[a.Version.Key].LogicalBytes, usages[a.Version.Key].UniqueBytes + usages[a.Version.Key].SharedBytes);
        long uniqueOfA = usages[a.Version.Key].UniqueBytes;
        Assert.True(uniqueOfA > 0);

        store.RemoveVersion(a.Version.Key, new HashSet<string>());
        env.Now = DateTime.UtcNow.AddHours(2);   // past the minimum age of the objects (their times are the file system's)
        store = env.Open();
        var freed = store.ReclaimSpace(Ct);
        Assert.Equal(uniqueOfA, freed.BytesFreed);
        AssertExactly(store, b.Version.Key, v1.Folder);
        Assert.Empty(store.Verify(ct: Ct).Problems);
    }

    [Fact]
    public void StorageAccountingCountsSharedContentOnce()
    {
        var env = new Env();
        var store = env.Open();
        var v1 = env.Client("Tibia");
        var a = Archive(store, v1);
        var b = Archive(store, env.Client("Copy"), registration: store.RegisterFolder(Path.Combine(env.Dir, "packages", "Copy"), ClientServers.Live, "Copy").Id);
        Assert.True(b.AlreadyStored);   // identical content: one revision
        v1.Files["sounds/sound-0003.ogg"] = FakeClient.Bytes("OggS c");
        Env.Rewrite(v1);
        var c = Archive(store, v1, ManifestStorage.Independent);

        var usage = store.Usage();
        long objects = store.Content.EnumerateObjects().Sum(o => o.Size);
        Assert.Equal(objects + c.Version.Bytes, usage.PhysicalBytes);
        Assert.Equal(a.Version.Bytes + c.Version.Bytes, usage.LogicalBytes);
        Assert.Equal(c.Version.Bytes, usage.IndependentBytes);
        var per = store.RevisionUsages().ToDictionary(u => u.Key);
        Assert.Equal(c.Version.Bytes, per[c.Version.Key].UniqueBytes);
        Assert.Equal(0, per[a.Version.Key].SharedBytes);   // independent copies share nothing
    }

    [Fact]
    public void ChangingTheModeNeverRewritesExistingRevisions()
    {
        var env = new Env();
        var store = env.Open();
        var v1 = env.Client("Tibia");
        var a = Archive(store, v1);
        byte[] manifest = File.ReadAllBytes(a.Version.ManifestPath(store.Root));
        v1.Files["sounds/sound-0003.ogg"] = FakeClient.Bytes("OggS later");
        Env.Rewrite(v1);
        Archive(store, v1, ManifestStorage.Independent);
        Assert.Equal(manifest, File.ReadAllBytes(a.Version.ManifestPath(store.Root)));
        Assert.Equal(ManifestStorage.Shared, store.Registry.Version(a.Version.Key)!.EffectiveStorage);
    }

    [Fact]
    public void AnInterruptedIndependentArchiveLeavesNothingPublished()
    {
        var env = new Env();
        var store = env.Open();
        var v1 = env.Client("Tibia");
        var scan = ClientInventory.Scan(v1.Folder, ct: Ct);
        using var cts = new CancellationTokenSource();
        int reports = 0;
        var progress = new Progress<Core.Common.OperationProgress>(_ => { if (++reports == 1) cts.Cancel(); });
        var request = new ArchiveRequest(scan, ClientServers.Live, ManifestScopes.CompleteInstallation, ManifestStorage.Independent, SourceTypes.InstalledPackage, null,
            ClientRegistration.InstalledLive, VersionEvidence.Collect(v1.Folder));
        try { store.Archive(request, new SyncProgress(() => cts.Cancel()), cts.Token); }
        catch (OperationCanceledException) { }
        Assert.Empty(store.Registry.Versions);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(store.Root, ClientServers.Live)));

        // A staging folder left by a crash is removed at the next start.
        string leftover = Path.Combine(store.Root, ClientServers.Live, "Install_15.33.9d148f.incoming-deadbeef", ClientStore.FilesFolder, "assets");
        Directory.CreateDirectory(leftover);
        File.WriteAllText(Path.Combine(leftover, "x.dat"), "half copied");
        var reopened = env.Open();
        Assert.False(Directory.Exists(Path.Combine(store.Root, ClientServers.Live, "Install_15.33.9d148f.incoming-deadbeef")));
        Assert.Contains(reopened.OpenNotes, n => n.Contains("interrupted archive", StringComparison.Ordinal));
    }

    [Fact]
    public void A73RevisionKeepsItsScopeAndIsComparedOnlyWhereBothCoverFiles()
    {
        var env = new Env();
        var store = env.Open();
        var v1 = env.Client("Tibia");
        var legacy = store.Import(ClientScanner.Scan(v1.Folder, ct: Ct), ClientServers.Live, null, ClientRegistration.InstalledLive, ct: Ct);
        var complete = Archive(store, v1);
        Assert.Equal(ManifestScopes.TibiaFoundryFiles, legacy.Version.EffectiveScope);
        Assert.False(legacy.Version.IsCompleteInstallation);
        Assert.NotEqual(legacy.Version.Key, complete.Version.Key);

        var diff = ManifestDiff.CompareRevisions(store.OpenVersion(legacy.Version.Key).Manifest, store.OpenVersion(complete.Version.Key).Manifest);
        Assert.True(diff.Unchanged, diff.Summary());
        Assert.Contains(diff.NotCompared, n => n.Contains("different scopes", StringComparison.Ordinal));
        Assert.Contains(diff.NotCompared, n => n.Contains("outside the files Tibia Foundry reads", StringComparison.Ordinal));
    }

    [Fact]
    public void AFormat1ManifestFrom73StillOpens()
    {
        var env = new Env();
        var store = env.Open();
        var v1 = env.Client("Tibia");
        var legacy = store.Import(ClientScanner.Scan(v1.Folder, ct: Ct), ClientServers.Live, null, ClientRegistration.InstalledLive, ct: Ct);
        // Rewrite the manifest exactly as 7.3 wrote it: format 1, no scope, storage or completeness.
        var m = store.OpenVersion(legacy.Version.Key).Manifest;
        string json = System.Text.Json.JsonSerializer.Serialize(new
        {
            format = 1, id = m.Id, server = m.Server, clientVersion = m.ClientVersion, displayVersion = m.DisplayVersion, build = m.Build,
            importedAtUtc = m.ImportedAtUtc, importedBy = "Tibia Foundry 7.3.0", files = m.Files.Select(f => new { path = f.Path, kind = f.Kind, sha256 = f.Sha256, size = f.Size }),
        });
        string path = legacy.Version.ManifestPath(store.Root);
        File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllText(path, json);
        var reopened = env.Open();
        var entry = reopened.Registry.Version(legacy.Version.Key)!;
        Assert.True(entry.IsUsable);
        Assert.Equal(ManifestScopes.TibiaFoundryFiles, entry.EffectiveScope);
        Assert.Equal(ManifestStorage.Shared, entry.EffectiveStorage);
        Assert.Equal(m.Fingerprint, reopened.OpenVersion(legacy.Version.Key).Manifest.Fingerprint);
    }

    [Fact]
    public void TooLittleFreeSpaceStopsBeforeAnythingIsWritten()
    {
        var env = new Env();
        var ex = Assert.Throws<InsufficientSpaceException>(() => ClientStore.EnsureSpace(env.Dir, long.MaxValue / 4, "this test"));
        Assert.Contains("Nothing was written", ex.Message, StringComparison.Ordinal);
        ClientStore.EnsureSpace(env.Dir, 1, "this test");
    }

    /// <summary>Reports synchronously (Progress&lt;T&gt; posts later), so a test can cancel at a known point.</summary>
    private sealed class SyncProgress(Action onReport) : IProgress<Core.Common.OperationProgress>
    {
        public void Report(Core.Common.OperationProgress value) => onReport();
    }

    [Fact]
    public void AFileThatChangesAfterTheScanIsNeverPublishedAsAnIndependentCopy()
    {
        var env = new Env();
        var store = env.Open();
        var v1 = env.Client("Tibia");
        var scan = ClientInventory.Scan(v1.Folder, ct: Ct);
        // Same size, other bytes: only the copy's hash can tell.
        string page = v1.PathOf("Auction_Id_1.html");
        File.WriteAllBytes(page, FakeClient.Bytes("<html>b saved page</html>"));
        var request = new ArchiveRequest(scan, ClientServers.Live, ManifestScopes.CompleteInstallation, ManifestStorage.Independent, SourceTypes.InstalledPackage, null,
            ClientRegistration.InstalledLive, VersionEvidence.Collect(v1.Folder));
        Assert.ThrowsAny<IOException>(() => store.Archive(request, ct: Ct));
        Assert.Empty(store.Registry.Versions);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(store.Root, ClientServers.Live)));
    }

    [Fact]
    public void ADamagedArchiveFileStopsTheRebuildAndLeavesNothing()
    {
        var env = new Env();
        var store = env.Open();
        var v1 = env.Client("Tibia");
        var a = Archive(store, v1);
        // One stored file is damaged (same size, other bytes).
        var file = store.OpenVersion(a.Version.Key).Manifest.Files.First(f => f.Path == "Auction_Id_1.html");
        string obj = store.Content.ObjectPath(file.Sha256);
        File.SetAttributes(obj, FileAttributes.Normal);
        File.WriteAllBytes(obj, FakeClient.Bytes("<html>X saved page</html>"));
        string target = Path.Combine(env.Dir, "rebuilt", "Tibia");
        var report = store.Rebuild(store.PlanRebuild(a.Version.Key, target, []), ct: Ct);
        Assert.False(report.Success);
        Assert.Contains(report.Problems, p => p.Contains("Auction_Id_1.html", StringComparison.Ordinal));
        Assert.False(Directory.Exists(target));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(env.Dir, "rebuilt")));   // no staging folder either
    }
}
