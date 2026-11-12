using System.Text.Json;
using Google.Protobuf;
using NAudio.Wave;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Editing;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.Tests;

/// <summary>
/// 7.3 integration: an imported version is used by the existing readers (catalog, sprites,
/// appearances, sounds, the RCC file, the compatibility check) through its manifest; the installed
/// (automatic) client keeps following the launcher; a launcher update never changes a stored version.
/// </summary>
public sealed class ClientIntegrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A package with real client formats (catalog, appearances, LZMA sheets, sounds data) and the client's own file lists.</summary>
    internal static string RealisticPackage(string folder, string version, string rccText = "interface")
    {
        SyntheticAssets.Create(Path.Combine(folder, TibiaFormat.AssetsFolder));
        string sounds = Path.Combine(folder, TibiaFormat.SoundsFolder);
        Directory.CreateDirectory(sounds);
        var data = new Tibia.Protobuf.Sound.Sounds();
        data.Sound.Add(new Tibia.Protobuf.Sound.Sound { Id = 1, Filename = "sound-0001.wav" });
        File.WriteAllBytes(Path.Combine(sounds, "sounds-0001.dat"), data.ToByteArray());
        File.WriteAllText(Path.Combine(sounds, TibiaFormat.SoundCatalogFile), """[ { "type": "sounds", "file": "sounds-0001.dat" } ]""");
        WriteWav(Path.Combine(sounds, "sound-0001.wav"));
        Directory.CreateDirectory(Path.Combine(folder, "bin"));
        File.WriteAllBytes(Path.Combine(folder, "bin", "graphics_resources.rcc"), FakeClient.Rcc(rccText));
        Directory.CreateDirectory(Path.Combine(folder, "conf"));
        File.WriteAllText(Path.Combine(folder, "conf", "clientoptions.json"), "{}");
        WriteLists(folder, version);
        return folder;
    }

    private static void WriteWav(string path)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(8000, 16, 1));
        writer.Write(new byte[1600], 0, 1600);
    }

    /// <summary>assets.json (every file of assets and sounds, packed hashes) and package.json (version, RCC), like the launcher writes them.</summary>
    internal static void WriteLists(string folder, string version)
    {
        var list = new[] { TibiaFormat.AssetsFolder, TibiaFormat.SoundsFolder }
            .SelectMany(sub => Directory.EnumerateFiles(Path.Combine(folder, sub)))
            .Select(f => (Path: Path.GetRelativePath(folder, f).Replace('\\', '/'), Bytes: File.ReadAllBytes(f)))
            .Select(f => new Dictionary<string, object> { ["unpack"] = false, ["packedhash"] = FakeClient.Sha(f.Bytes), ["packedsize"] = f.Bytes.Length, ["localfile"] = f.Path })
            .ToList();
        byte[] assetsJson = JsonSerializer.SerializeToUtf8Bytes(new { files = list });
        File.WriteAllBytes(Path.Combine(folder, "assets.json"), assetsJson);
        File.WriteAllText(Path.Combine(folder, "assets.json.sha256"), FakeClient.Sha(assetsJson));
        byte[] rcc = File.ReadAllBytes(Path.Combine(folder, "bin", "graphics_resources.rcc"));
        File.WriteAllBytes(Path.Combine(folder, TibiaFormat.PackageManifest), JsonSerializer.SerializeToUtf8Bytes(new
        {
            version,
            files = new[] { new Dictionary<string, object> { ["localfile"] = "bin/graphics_resources.rcc", ["unpackedhash"] = FakeClient.Sha(rcc), ["unpackedsize"] = rcc.Length } },
        }));
    }

    private sealed class Env : IDisposable
    {
        public string Dir { get; } = TestData.NewTempDirectory("clientint");
        public string Packages => Path.Combine(Dir, "packages");
        public string Live => Path.Combine(Packages, "Tibia");
        public AppSettings Settings { get; }
        public AppServices Services { get; }

        public Env()
        {
            RealisticPackage(Live, "15.33.9d148f");
            Settings = new AppSettings { LoadDataAutomatically = false };
            Settings.Normalize();
            // Never the installed client: the packages folder is in the test folder.
            Settings.SetChosenLocation(LocationIds.PackagesFolder, ClientScope.Live, Packages);
            Services = new AppServices(new SettingsStore(Path.Combine(Dir, "data", "settings.json")), Settings) { Paths = TestData.PathsIn(Path.Combine(Dir, "data")) };
        }

        public ClientStore Store => Services.Clients.Store;

        public ImportResult ImportInstalled(ClientScope scope = ClientScope.Live)
        {
            var scan = ClientScanner.Scan(Services.Clients.InstalledPackageFolder(scope), ct: Ct);
            Assert.True(scan.CanImport, string.Join(" ", scan.Problems));
            return Store.Import(scan, ClientServers.Of(scope), null, scope == ClientScope.Live ? ClientRegistration.InstalledLive : ClientRegistration.InstalledTest, ct: Ct);
        }

        public void Use(string? key)
        {
            Settings.SetActiveClient(ClientScope.Live, key);
            Services.Data.SwitchClient();
        }

        public void Dispose() => Services.Dispose();
    }

    private static void DeleteTree(string folder)
    {
        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void ThePortableStoreIsInTheDataFolderAndTheInstalledOneInLocalAppData()
    {
        var settings = new AppSettings();
        var portable = TestData.PathsIn(Path.Combine(Path.GetTempPath(), "x", "TibiaFoundry-Data"));
        Assert.Equal(Path.Combine(portable.DataDirectory, "Clients"), ClientLibrary.RootFor(portable, settings));
        var installed = portable with { IsPortable = false };
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TibiaFoundry", "Clients"), ClientLibrary.RootFor(installed, settings));
        settings.ClientStoreFolder = @"D:\Elsewhere\Clients";
        Assert.Equal(@"D:\Elsewhere\Clients", ClientLibrary.RootFor(installed, settings));
        Assert.Equal(Path.Combine(portable.DataDirectory, "Clients"), ClientLibrary.RootFor(portable, settings)); // portable ignores it
    }

    [Fact]
    public void WhileTheInstalledClientIsUsedNoStoreIsCreated()
    {
        using var env = new Env();
        Assert.Null(env.Services.Data.ImportedVersion);
        Assert.False(env.Services.Data.Locations.Resolve(LocationIds.Assets).IsImported);
        Assert.False(Directory.Exists(env.Services.Clients.Root));
        // The launch check only leaves its baseline: the archive itself is not created.
        Assert.Equal(LaunchCheckOutcome.New, env.Services.Clients.CheckInstalled(ClientScope.Live, ct: Ct)!.Result.Outcome);
        Assert.False(env.Services.Clients.StoreExists);
        Assert.Equal([ClientBaselines.FolderName], Directory.EnumerateFileSystemEntries(env.Services.Clients.Root!).Select(Path.GetFileName));
    }

    [Fact]
    public async Task AnImportedVersionIsReadByTheExistingReadersEvenWithoutItsSource()
    {
        using var env = new Env();
        var installedCatalog = AssetCatalog.Load(Path.Combine(env.Live, "assets"));
        var installedSprites = new SpriteRepository(installedCatalog, null, SpriteSourceMode.ClientOnly);
        var expected = Enumerable.Range(0, 22).Select(id => installedSprites.GetSprite((uint)id)!.Pixels.ToArray()).ToList();
        var v = env.ImportInstalled();
        env.Use(v.Version.Key);

        // The source disappears: the version is read from the store alone.
        DeleteTree(env.Live);
        var data = env.Services.Data;
        Assert.NotNull(data.ImportedVersion);
        Assert.Null(data.ClientChoiceProblem);
        Assert.Equal("15.33.9d148f", data.ClientVersion);
        var locations = data.Locations;
        foreach (string id in new[] { LocationIds.Package, LocationIds.PackageManifest, LocationIds.Assets, LocationIds.Appearances, LocationIds.SoundsFolder, LocationIds.Sounds, LocationIds.ResourceFile })
        {
            var r = locations.Resolve(id);
            Assert.True(r.Found, $"{id}: {r.Problem}");
            Assert.Equal(LocationSource.ImportedVersion, r.Source);
            Assert.True(r.Path!.StartsWith(env.Services.Clients.Root!, StringComparison.OrdinalIgnoreCase), id);
        }
        Assert.True(data.AssetsAvailable);
        Assert.Equal(installedCatalog.ContentSha256, data.Catalog!.ContentSha256);
        for (int id = 0; id < 22; id++) Assert.Equal(expected[id], data.Sprites!.GetSprite((uint)id)!.Pixels.ToArray());
        var appearances = await data.EnsureAppearancesAsync();
        Assert.NotNull(appearances);
        Assert.True(appearances!.Count(AppearanceKind.Object) > 0);
        var sounds = await data.LoadSoundsAsync(data.DefaultFile(AssetTypes.Sounds)!);
        string? playable = sounds.ResolvePlayableFile(1, null);
        Assert.NotNull(playable);
        Assert.Equal("", Path.GetExtension(playable)); // a stored object, without its name…
        using (var reader = AudioPlayer.OpenReader(playable!)) Assert.Equal(8000, reader.WaveFormat.SampleRate); // …still opens as WAV by its header
        Assert.Equal(FakeClient.Rcc("interface"), File.ReadAllBytes(data.ResourceFile!));
        Assert.Equal([("bin/graphics_resources.rcc", data.ResourceFile!)], data.ResourceFiles);
        Assert.Contains("imported", data.PackageDisplayName);

        // The compatibility check reads the version the same way.
        var report = await data.CheckCompatibilityAsync(ct: Ct);
        Assert.DoesNotContain(report.Components, c => c.Status == CompatibilityStatus.Missing && c.Component is CompatibilityChecker.SpriteCatalogComponent or TibiaFormat.CatalogTypeAppearances or TibiaFormat.CatalogTypeSounds);
        var sprites = report.Components.Single(c => c.Component == CompatibilityChecker.SpriteCatalogComponent);
        Assert.DoesNotContain(sprites.Details, d => d.Contains("missing on disk", StringComparison.Ordinal));
        var soundCheck = report.Components.Single(c => c.Component == TibiaFormat.CatalogTypeSounds);
        Assert.DoesNotContain(soundCheck.Details, d => d.Contains("missing on disk", StringComparison.Ordinal));
    }

    [Fact]
    public void AnImportedVersionInUseIsDescribedAsImported()
    {
        using var env = new Env();
        var v = env.ImportInstalled();
        env.Use(v.Version.Key);
        var locations = env.Services.Data.Locations;
        Assert.Contains("imported", locations.Describe(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ALauncherUpdateNeverChangesASnapshotAndTheAutomaticEntryFollowsIt()
    {
        using var env = new Env();
        var v1 = env.ImportInstalled();
        byte[] manifest = File.ReadAllBytes(v1.Version.ManifestPath(env.Store.Root));
        var objectsBefore = env.Store.Content.EnumerateObjects().Select(o => o.Sha256).ToHashSet();
        var check = env.Services.Clients.CheckInstalled(ClientScope.Live, ct: Ct)!;
        Assert.Equal(LaunchCheckOutcome.New, check.Result.Outcome);
        Assert.Equal(LaunchCheckOutcome.Unchanged, env.Services.Clients.CheckInstalled(ClientScope.Live, ct: Ct)!.Result.Outcome);

        // The launcher updates the installed client (other RCC, new version).
        File.WriteAllBytes(Path.Combine(env.Live, "bin", "graphics_resources.rcc"), FakeClient.Rcc("interface 15.34"));
        WriteLists(env.Live, "15.34.0a0a0a");

        // The automatic entry (in use) follows the launcher, as before 7.3…
        env.Use(null);
        Assert.Equal("15.34.0a0a0a", env.Services.Data.ClientVersion);
        Assert.Equal(FakeClient.Rcc("interface 15.34"), File.ReadAllBytes(env.Services.Data.ResourceFile!));
        // …the stored snapshot is untouched…
        Assert.Equal(manifest, File.ReadAllBytes(v1.Version.ManifestPath(env.Store.Root)));
        Assert.True(objectsBefore.All(h => File.Exists(env.Store.Content.ObjectPath(h))));
        env.Use(v1.Version.Key);
        Assert.Equal("15.33.9d148f", env.Services.Data.ClientVersion);
        Assert.Equal(FakeClient.Rcc("interface"), File.ReadAllBytes(env.Services.Data.ResourceFile!));
        Assert.Null(env.Services.Data.CheckForClientChange()); // a snapshot never "changes"
        // …and the launch check notices the change and offers it for archiving.
        check = env.Services.Clients.CheckInstalled(ClientScope.Live, ct: Ct)!;
        Assert.Equal(LaunchCheckOutcome.Changed, check.Result.Outcome);
        Assert.Equal("15.34.0a0a0a", check.Result.Version.Version);
        Assert.Contains("changed", check.Result.ChangeSummary(), StringComparison.Ordinal);
        var status = env.Services.Clients.InstalledStatusOf(ClientScope.Live)!;
        Assert.True(status.NeedsArchive);
        env.Services.Clients.DismissChange(ClientScope.Live, status.Fingerprint!, status.Version);
        Assert.False(env.Services.Clients.InstalledStatusOf(ClientScope.Live)!.NeedsArchive);
        Assert.Equal("15.34.0a0a0a", env.Store.Registry.Registration(ClientRegistration.InstalledLive)!.ReminderDismissedFor);   // 7.3 reads this

        var v2 = env.ImportInstalled();
        Assert.Equal("Live_Server/Client_15.34.0a0a0a", v2.Version.Key);
        Assert.Equal(2, v2.NewObjects); // only the changed RCC file and package.json are new; everything else is shared
        // This package has only files Tibia Foundry reads (and personal data), so the 7.3 revision holds exactly its content:
        // it counts as archived, and archiving it again stores nothing.
        Assert.Equal(v2.Version.Key, env.Services.Clients.InstalledStatusOf(ClientScope.Live)!.ArchivedAs!.Key);
        var again = env.Services.Clients.ArchiveChecked(check, ct: Ct);
        Assert.True(again.AlreadyStored);
        Assert.Equal(v2.Version.Key, ClientBaselines.Read(env.Store.Root, ClientRegistration.InstalledLive)!.ArchivedKey);
        Assert.Equal(manifest, File.ReadAllBytes(v1.Version.ManifestPath(env.Store.Root)));
    }

    [Fact]
    public void AnUnusableChoiceIsReportedAndTheInstalledClientIsUsedMeanwhile()
    {
        using var env = new Env();
        var v = env.ImportInstalled();
        env.Use(v.Version.Key);
        Assert.NotNull(env.Services.Data.ImportedVersion);
        env.Store.Content.Files.DeleteFile(env.Store.Content.ObjectPath(env.Store.OpenVersion(v.Version.Key).Manifest.Find("assets/catalog-content.json")!.Sha256));
        env.Services.Clients.Configure(env.Services.Clients.Root!, null); // a fresh start
        env.Services.Data.SwitchClient();
        Assert.Null(env.Services.Data.ImportedVersion);
        Assert.Contains("cannot be used", env.Services.Data.ClientChoiceProblem);
        Assert.Equal(v.Version.Key, env.Settings.ActiveClientLive); // the choice is kept
        Assert.True(env.Services.Data.AssetsAvailable);
    }

    [Fact]
    public void ARegisteredFolderIsUsedAsItIsWithoutTheInstalledClientsChoices()
    {
        using var env = new Env();
        string copy = RealisticPackage(Path.Combine(env.Dir, "copies", "15.20"), "15.20.111111");
        env.Settings.SetChosenLocation(LocationIds.Assets, ClientScope.Live, Path.Combine(env.Live, "assets"));
        var r = env.Store.RegisterFolder(copy, ClientServers.Live, "Old copy");
        env.Use(r.Id);
        var locations = env.Services.Data.Locations;
        Assert.Equal(copy, locations.Resolve(LocationIds.Package).Path);
        Assert.Equal(Path.Combine(copy, "assets"), locations.Resolve(LocationIds.Assets).Path);
        Assert.Equal("15.20.111111", env.Services.Data.ClientVersion);
        Assert.Contains("Old copy (folder)", env.Services.Clients.Describe(ClientScope.Live, null));
        // Registering copied nothing.
        Assert.Empty(env.Store.Content.EnumerateObjects());
    }

    [Fact]
    public void NoEditorWritesIntoTheStoreOrARegisteredFolder()
    {
        using var env = new Env();
        string copy = RealisticPackage(Path.Combine(env.Dir, "copies", "15.20"), "15.20.111111");
        env.Store.RegisterFolder(copy, ClientServers.Live, null);
        var v = env.ImportInstalled();
        Assert.True(AssetsProject.IsInsideClientInstallation(Path.Combine(env.Store.Root, "Live_Server")));
        Assert.True(AssetsProject.IsInsideClientInstallation(env.Store.OpenVersion(v.Version.Key).PathOf("bin/graphics_resources.rcc")!));
        Assert.True(AssetsProject.IsInsideClientInstallation(Path.Combine(copy, "assets")));
        Assert.False(AssetsProject.IsInsideClientInstallation(Path.Combine(env.Dir, "exports")));
    }

    [Fact]
    public void SettingsOfFormat5GainTheStoreSettingsAndKeepTheirMeaning()
    {
        string dir = TestData.NewTempDirectory("settings6");
        string file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, """{ "Version": 5, "UseTestServer": true, "TestServerPackage": "TibiaExternal" }""");
        var loaded = new SettingsStore(file).Load().Settings;
        Assert.Equal(AppSettings.CurrentVersion, loaded.Version);
        Assert.Equal(5, loaded.LoadedVersion);
        Assert.Null(loaded.ActiveClientLive);
        Assert.Null(loaded.ActiveClientTest);
        Assert.True(loaded.ClientImportReminders);
        Assert.Empty(loaded.MigrationNotes);
        loaded.SetActiveClient(ClientScope.TestServer, "Test_Server/Client_15.25.0e418e");
        new SettingsStore(file).Save(loaded);
        Assert.Equal("Test_Server/Client_15.25.0e418e", new SettingsStore(file).Load().Settings.ActiveClient(ClientScope.TestServer));
        Assert.DoesNotContain(dir.Replace("\\", "\\\\"), File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ the real client (opt-in)

    /// <summary>
    /// TF_CLIENT_STORE_MEASURE=1: imports the installed live and test clients into a temporary store
    /// (the clients are only read), uses each through the readers, and records the figures in
    /// TF_CLIENT_STORE_REPORT. About 270 MB are written to the temporary folder and deleted afterwards.
    /// </summary>
    [Fact]
    public void TheInstalledClientsImportAndReadBackExactly()
    {
        Assert.SkipUnless(TestData.Setting("CLIENT_STORE_MEASURE") == "1", "Set TF_CLIENT_STORE_MEASURE=1 to import the installed clients into a temporary store.");
        string dir = TestData.NewTempDirectory("realstore");
        var lines = new List<string>();
        try
        {
            var store = ClientStore.Open(Path.Combine(dir, "Clients"));
            foreach (var (package, scope) in new[] { (TibiaClientPaths.LivePackageName, ClientScope.Live), (TibiaClientPaths.DefaultTestPackageName, ClientScope.TestServer) })
            {
                string folder = Path.Combine(TibiaClientPaths.PackagesRoot, package);
                if (!Directory.Exists(folder)) continue;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var scan = ClientScanner.Scan(folder, ct: Ct);
                Assert.True(scan.CanImport, string.Join(" ", scan.Problems.Take(3)));
                var result = store.Import(scan, ClientServers.Of(scope), null, scope == ClientScope.Live ? ClientRegistration.InstalledLive : ClientRegistration.InstalledTest, ct: Ct);
                lines.Add($"{package} {scan.ClientVersion}: scan {scan.Duration.TotalSeconds:N1} s, import {result.Duration.TotalSeconds:N1} s, {result.NewObjects:N0} new objects, {result.NewBytes / 1048576.0:N1} MB written");
                var rescan = ClientScanner.Scan(folder, ct: Ct);
                var again = store.Import(rescan, ClientServers.Of(scope), null, scope == ClientScope.Live ? ClientRegistration.InstalledLive : ClientRegistration.InstalledTest, ct: Ct);
                Assert.True(again.AlreadyStored);
                lines.Add($"  unchanged rescan: {rescan.Duration.TotalSeconds:N1} s, nothing written");
                var files = store.OpenVersion(result.Version.Key);
                var catalog = AssetCatalog.Load(files.Folder("assets"));
                Assert.Equal(AssetCatalog.Load(Path.Combine(folder, "assets")).ContentSha256, catalog.ContentSha256);
                var sprites = new SpriteRepository(catalog, null, SpriteSourceMode.ClientOnly);
                var installed = new SpriteRepository(AssetCatalog.Load(Path.Combine(folder, "assets")), null, SpriteSourceMode.ClientOnly);
                foreach (int id in new[] { 1, 100, 5000, 50000, catalog.HighestSpriteId })
                    Assert.Equal(installed.GetSprite((uint)id)?.Pixels.ToArray(), sprites.GetSprite((uint)id)?.Pixels.ToArray());
                var paths = files.Manifest.Files.Select(f => f.Path).ToList();
                var lookup = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 100_000; i++) _ = files.PathOf(paths[i % paths.Count]);
                lines.Add($"  100,000 lookups: {lookup.Elapsed.TotalMilliseconds:N0} ms; total {watch.Elapsed.TotalSeconds:N1} s");
            }
            var usage = store.Usage();
            lines.Add($"Store: {usage.Versions} versions, {usage.LogicalBytes / 1048576.0:N1} MB as separate copies, {usage.StoredBytes / 1048576.0:N1} MB stored ({usage.Objects:N0} objects), {usage.SharedPercent:N0}% saved");
            lines.AddRange(usage.ByKind.Select(k => $"  {k.Kind}: {k.Objects:N0} objects, {k.Bytes / 1048576.0:N1} MB"));
            Assert.Empty(store.Verify(null, null, Ct).Problems);
        }
        finally
        {
            if (TestData.Setting("CLIENT_STORE_REPORT") is { } report) File.WriteAllLines(report, lines);
            TestContext.Current.SendDiagnosticMessage(string.Join(Environment.NewLine, lines));
            DeleteTree(dir);
        }
    }
}
