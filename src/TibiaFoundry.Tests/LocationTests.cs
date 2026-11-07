using System.Text.Json;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Rcc;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.Tests;

/// <summary>
/// Client and data locations (7.0): every file and folder the program reads can be chosen, is
/// checked before it is used, is remembered, and can be reset; the Compatibility page locates a
/// missing file in place. Everything runs on synthetic client folders in temporary folders: the
/// installed client and the user's settings are never touched.
/// </summary>
public sealed class LocationTests
{
    // ------------------------------------------------------------------ synthetic clients

    /// <summary>A packages folder with fake client packages (synthetic assets, sounds, resource file).</summary>
    private sealed class FakePackages
    {
        public string Root { get; } = TestData.NewTempDirectory("packages");

        public string Add(string name, string version, bool assets = true, bool sounds = true, bool resourceFile = true)
        {
            string package = Path.Combine(Root, name);
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, TibiaFormat.PackageManifest), $$"""{ "version": "{{version}}" }""");
            if (assets) SyntheticAssets.Create(Path.Combine(package, TibiaFormat.AssetsFolder));
            if (sounds) AddSounds(Path.Combine(package, TibiaFormat.SoundsFolder));
            if (resourceFile) WriteResourceFile(Path.Combine(package, "bin", "graphics_resources.rcc"));
            return package;
        }
    }

    private static void AddSounds(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "sounds-test.dat"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(folder, TibiaFormat.SoundCatalogFile), """[ { "type": "sounds", "file": "sounds-test.dat" } ]""");
    }

    private static string WriteResourceFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var archive = RccArchive.Create(3);
        archive.Add("/images/ok.txt", "ok"u8.ToArray(), RccCompressionChoice.Never);
        File.WriteAllBytes(path, archive.Build());
        return path;
    }

    private static LocationResolver Resolver(FakePackages packages, Dictionary<string, string>? chosen = null, string package = "Tibia",
        IEnumerable<string>? optional = null) =>
        new(ClientScope.Live, package, chosen, packages.Root, optional);

    // ------------------------------------------------------------------ resolution

    [Fact]
    public void LocationsAreFoundAutomaticallyInTheInstalledPackage()
    {
        var packages = new FakePackages();
        string package = packages.Add("Tibia", "99.1");
        var r = Resolver(packages);

        Assert.Equal(LocationSource.FoundAutomatically, r.Resolve(LocationIds.Package).Source);
        Assert.Equal(Path.Combine(package, "assets"), r.PathOf(LocationIds.Assets));
        Assert.StartsWith(Path.Combine(package, "assets", "appearances-"), r.PathOf(LocationIds.Appearances));
        Assert.Equal(Path.Combine(package, "sounds", "sounds-test.dat"), r.PathOf(LocationIds.Sounds));
        Assert.Equal(Path.Combine(package, "bin", "graphics_resources.rcc"), r.PathOf(LocationIds.ResourceFile));
        Assert.Equal("99.1", TibiaClientPaths.ReadVersion(r.PathOf(LocationIds.PackageManifest)!));
        // Not in the synthetic client: reported with where it was expected, never guessed.
        var map = r.Resolve(LocationIds.Map);
        Assert.False(map.Found);
        Assert.Equal(LocationSource.NotFound, map.Source);
        Assert.Contains("map", map.Problem);
        Assert.Equal("(not set)", r.Resolve(LocationIds.SpritesFolder).DisplayPath);
    }

    [Fact]
    public void AChosenFolderWinsAndEverythingInsideItFollows()
    {
        var packages = new FakePackages();
        packages.Add("Tibia", "99.1");
        var elsewhere = new FakePackages();
        string other = elsewhere.Add("CopyOfAClient", "42.0");
        var r = Resolver(packages, new() { [LocationIds.Package] = other });

        Assert.Equal(LocationSource.ChosenByYou, r.Resolve(LocationIds.Package).Source);
        foreach (string id in new[] { LocationIds.PackageManifest, LocationIds.Assets, LocationIds.Appearances, LocationIds.SoundsFolder, LocationIds.Sounds, LocationIds.ResourceFile })
        {
            var location = r.Resolve(id);
            Assert.Equal(LocationSource.FromChosenFolder, location.Source);
            Assert.StartsWith(other, location.Path, StringComparison.OrdinalIgnoreCase);
        }
        // 6.0 read the sounds and the resource file of the installed client whatever was chosen; now they follow the choice.
        Assert.Equal("42.0", TibiaClientPaths.ReadVersion(r.PathOf(LocationIds.PackageManifest)!));
    }

    [Fact]
    public void TheVersionBelongsToTheChosenAssetsFolderNotToTheInstalledClient()
    {
        var packages = new FakePackages();
        packages.Add("Tibia", "99.1");
        var elsewhere = new FakePackages();
        string copy = elsewhere.Add("Copy", "42.0", sounds: false, resourceFile: false);

        var withManifest = Resolver(packages, new() { [LocationIds.Assets] = Path.Combine(copy, "assets") });
        Assert.Equal(LocationSource.FromChosenFolder, withManifest.Resolve(LocationIds.PackageManifest).Source);
        Assert.Equal("42.0", TibiaClientPaths.ReadVersion(withManifest.PathOf(LocationIds.PackageManifest)!));

        // An assets folder on its own has no version: unknown, not the installed client's 99.1.
        string bare = SyntheticAssets.Create(Path.Combine(TestData.NewTempDirectory("bare"), "assets"));
        var withoutManifest = Resolver(packages, new() { [LocationIds.Assets] = bare });
        var manifest = withoutManifest.Resolve(LocationIds.PackageManifest);
        Assert.False(manifest.Found);
        Assert.Contains("version is unknown", manifest.Problem);
        var report = CompatibilityChecker.Check(withoutManifest, ct: TestContext.Current.CancellationToken);
        Assert.Null(report.ClientVersion);
        var row = report.Components.Single(c => c.Component == CompatibilityChecker.PackageComponent);
        Assert.Equal(CompatibilityStatus.Unfamiliar, row.Status);
        Assert.Equal(LocationIds.PackageManifest, row.LocationId);
    }

    [Fact]
    public void AnUnavailableChoiceIsKeptReportedAndTheAutomaticLocationIsUsedMeanwhile()
    {
        var packages = new FakePackages();
        string package = packages.Add("Tibia", "99.1");
        string gone = Path.Combine(TestData.NewTempDirectory("unplugged"), "Drive", "assets");
        var r = Resolver(packages, new() { [LocationIds.Assets] = gone });

        var assets = r.Resolve(LocationIds.Assets);
        Assert.True(assets.Found);
        Assert.Equal(Path.Combine(package, "assets"), assets.Path);
        Assert.Equal(LocationSource.FoundAutomatically, assets.Source);
        Assert.Equal(gone, assets.ChosenPath);
        Assert.True(assets.ChosenUnavailable);
        Assert.Contains(gone, assets.Notice);

        // Once the folder is back, the choice applies again (a new resolver looks at the disk again).
        SyntheticAssets.Create(gone);
        var later = Resolver(packages, new() { [LocationIds.Assets] = gone });
        Assert.Equal(LocationSource.ChosenByYou, later.Resolve(LocationIds.Assets).Source);
        Assert.Equal(gone, later.PathOf(LocationIds.Assets));
    }

    [Fact]
    public void QuickChecksRefuseTheWrongKindOfFileOrFolder()
    {
        var packages = new FakePackages();
        string package = packages.Add("Tibia", "99.1");
        string assets = Path.Combine(package, "assets");
        string appearances = Directory.EnumerateFiles(assets, "appearances-*.dat").Single();
        string dir = TestData.NewTempDirectory("wrong");
        string text = Path.Combine(dir, "notes.txt");
        File.WriteAllText(text, "hello");
        string empty = Path.Combine(dir, "empty.dat");
        File.WriteAllBytes(empty, []);

        Assert.Null(LocationResolver.QuickCheck(LocationIds.Assets, assets));
        Assert.Null(LocationResolver.QuickCheck(LocationIds.Appearances, appearances));
        Assert.Null(LocationResolver.QuickCheck(LocationIds.Package, package));
        Assert.Contains("a file is needed", LocationResolver.QuickCheck(LocationIds.Appearances, assets));
        Assert.Contains("a folder is needed", LocationResolver.QuickCheck(LocationIds.Assets, appearances));
        Assert.Contains("catalog-content.json", LocationResolver.QuickCheck(LocationIds.Assets, dir));
        Assert.Contains(".dat", LocationResolver.QuickCheck(LocationIds.Appearances, text));
        Assert.Contains("empty", LocationResolver.QuickCheck(LocationIds.Map, empty));
        Assert.Contains("Qt resource file", LocationResolver.QuickCheck(LocationIds.ResourceFile, text));
        Assert.Contains("not a Tibia client package", LocationResolver.QuickCheck(LocationIds.Package, dir));
        Assert.Contains("version", LocationResolver.QuickCheck(LocationIds.PackageManifest, text));
        Assert.Contains("does not exist", LocationResolver.QuickCheck(LocationIds.SoundsFolder, Path.Combine(dir, "nothing")));
        Assert.Contains("sounds", LocationResolver.QuickCheck(LocationIds.SoundsFolder, dir));
        // A catalog that cannot be read passes the quick check (cheap at start-up) but not the check on Locate.
        string broken = Path.Combine(dir, "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, TibiaFormat.AssetCatalogFile), "{ broken");
        Assert.Null(LocationResolver.QuickCheck(LocationIds.Assets, broken));
        Assert.NotNull(LocationResolver.DeepCheck(LocationIds.Assets, broken));
    }

    [Fact]
    public void CommonPickingMistakesAreCorrected()
    {
        var packages = new FakePackages();
        string package = packages.Add("Tibia", "99.1");

        Assert.Equal((Path.Combine(package, "assets"), "its assets subfolder is used"), LocationResolver.NormalizeChoice(LocationIds.Assets, package));
        Assert.Equal((Path.Combine(package, "sounds"), "its sounds subfolder is used"), LocationResolver.NormalizeChoice(LocationIds.SoundsFolder, package));
        Assert.Equal((package, "the package folder that contains it is used"), LocationResolver.NormalizeChoice(LocationIds.Package, Path.Combine(package, "assets")));
        Assert.Equal((Path.Combine(package, "assets"), (string?)null), LocationResolver.NormalizeChoice(LocationIds.Assets, Path.Combine(package, "assets")));
        Assert.Equal((packages.Root, (string?)null), LocationResolver.NormalizeChoice(LocationIds.PackagesFolder, packages.Root));
        // The Tibia folder itself (…\Tibia with "packages" inside) where the packages folder is asked for.
        string tibia = TestData.NewTempDirectory("Tibia");
        Directory.CreateDirectory(Path.Combine(tibia, "packages"));
        Assert.Equal((Path.Combine(tibia, "packages"), "its packages subfolder is used"), LocationResolver.NormalizeChoice(LocationIds.PackagesFolder, tibia));
    }

    [Fact]
    public void AChangeAffectsWhatIsDerivedFromIt()
    {
        var fromPackage = LocationResolver.AffectedBy(LocationIds.Package);
        Assert.True(fromPackage.IsSupersetOf([LocationIds.PackageManifest, LocationIds.Assets, LocationIds.Appearances, LocationIds.Map, LocationIds.SoundsFolder,
            LocationIds.Sounds, LocationIds.ResourceFile]), string.Join(", ", fromPackage));
        Assert.DoesNotContain(LocationIds.SpritesFolder, fromPackage);
        Assert.Contains(LocationIds.PackageManifest, LocationResolver.AffectedBy(LocationIds.Assets));
        Assert.Equal([LocationIds.Appearances], LocationResolver.AffectedBy(LocationIds.Appearances));

        Assert.Null(LocationResolver.ComponentsAffectedBy(LocationIds.Assets));
        Assert.Null(LocationResolver.ComponentsAffectedBy(LocationIds.PackagesFolder));
        Assert.Equal(new HashSet<string> { TibiaFormat.CatalogTypeAppearances, TibiaFormat.CatalogTypeStaticData, TibiaFormat.CatalogTypeStaticMapData },
            LocationResolver.ComponentsAffectedBy(LocationIds.Appearances));
        Assert.Equal(new HashSet<string> { TibiaFormat.CatalogTypeSounds }, LocationResolver.ComponentsAffectedBy(LocationIds.SoundsFolder));
        Assert.Equal(new HashSet<string> { CompatibilityChecker.ResourceFileComponent }, LocationResolver.ComponentsAffectedBy(LocationIds.ResourceFile));
        Assert.Equal(new HashSet<string> { CompatibilityChecker.SpritesFolderComponent }, LocationResolver.ComponentsAffectedBy(LocationIds.SpritesFolder));
    }

    // ------------------------------------------------------------------ compatibility

    [Fact]
    public void OptionalFilesNeverDecideTheOverallResultAndMissingFilesNameTheirLocation()
    {
        var packages = new FakePackages();
        packages.Add("Tibia", "99.1", sounds: false, resourceFile: false);
        var r = Resolver(packages, optional: [LocationIds.ResourceFile]);
        var report = CompatibilityChecker.Check(r, ct: TestContext.Current.CancellationToken);

        var resource = report.Components.Single(c => c.Component == CompatibilityChecker.ResourceFileComponent);
        Assert.False(resource.Required);
        Assert.Equal(CompatibilityStatus.Missing, resource.Status);
        Assert.Equal(LocationIds.ResourceFile, resource.LocationId);
        Assert.Contains("optional", resource.Summary);

        var sounds = report.Components.Single(c => c.Component == TibiaFormat.CatalogTypeSounds);
        Assert.Equal(CompatibilityStatus.Missing, sounds.Status);
        Assert.Equal(LocationIds.SoundsFolder, sounds.LocationId);   // the whole folder is missing: locate the folder
        var map = report.Components.Single(c => c.Component == TibiaFormat.CatalogTypeMap);
        Assert.Equal(LocationIds.Map, map.LocationId);
        Assert.EndsWith("map-<checksum>.dat", map.Path);
        Assert.Equal(CompatibilityStatus.Missing, report.Overall);   // decided by required files only
        Assert.Equal(CompatibilityReport.OverallOf(report.Components.Where(c => c.Required)), report.Overall);
        Assert.Contains("optional", report.ToText());

        // With every required file present except the optional one, the optional row changes nothing.
        var onlyOptional = new[] { report.Components.Single(c => c.Component == CompatibilityChecker.PackageComponent), resource };
        Assert.Equal(CompatibilityStatus.Unfamiliar, CompatibilityReport.OverallOf(onlyOptional));
    }

    [Fact]
    public void APartialCheckReportsOnlyWhatChangedAndMergesIntoTheReport()
    {
        var packages = new FakePackages();
        packages.Add("Tibia", "99.1", resourceFile: false);
        var before = CompatibilityChecker.Check(Resolver(packages, optional: [LocationIds.ResourceFile]), ct: TestContext.Current.CancellationToken);
        Assert.Equal(CompatibilityStatus.Missing, before.Components.Single(c => c.Component == CompatibilityChecker.ResourceFileComponent).Status);

        string rcc = WriteResourceFile(Path.Combine(TestData.NewTempDirectory("rcc"), "graphics_resources.rcc"));
        var after = Resolver(packages, new() { [LocationIds.ResourceFile] = rcc }, optional: [LocationIds.ResourceFile]);
        var partial = CompatibilityChecker.Check(after, ct: TestContext.Current.CancellationToken, only: LocationResolver.ComponentsAffectedBy(LocationIds.ResourceFile));
        var row = Assert.Single(partial.Components);
        Assert.Equal(CompatibilityChecker.ResourceFileComponent, row.Component);
        Assert.Equal(CompatibilityStatus.Unfamiliar, row.Status);   // readable; the fake client version was not validated
        Assert.Equal(rcc, row.Path);

        var merged = before.WithComponents(partial);
        Assert.Equal(before.Components.Select(c => c.Component), merged.Components.Select(c => c.Component));
        Assert.Same(row, merged.Components.Single(c => c.Component == CompatibilityChecker.ResourceFileComponent));
        Assert.Same(before.Components[0], merged.Components[0]);
        Assert.Equal(before.Overall, merged.Overall);
    }

    // ------------------------------------------------------------------ settings

    [Fact]
    public void AnAssetsFolderFromBefore7Point0AppliesToBothClientsAndThenSeparately()
    {
        string dir = TestData.NewTempDirectory("format4");
        string file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, """{ "Version": 4, "AssetsFolderOverride": "D:\\Old client\\assets", "TestServerPackage": "TibiaExternal" }""");
        var (settings, status, _) = new SettingsStore(file).Load();

        Assert.Equal(SettingsLoadStatus.Loaded, status);
        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.Equal(4, settings.LoadedVersion);
        Assert.Equal(@"D:\Old client\assets", settings.ChosenLocation(LocationIds.Assets, ClientScope.Live));
        Assert.Equal(@"D:\Old client\assets", settings.ChosenLocation(LocationIds.Assets, ClientScope.TestServer));
        Assert.Contains(settings.MigrationNotes, n => n.Contains("separately for the live client and the test server"));

        // From now on the two clients are independent.
        settings.SetChosenLocation(LocationIds.Assets, ClientScope.TestServer, @"E:\Test client\assets");
        Assert.Equal(@"D:\Old client\assets", settings.ChosenLocation(LocationIds.Assets, ClientScope.Live));
        Assert.Equal(@"D:\Old client\assets", settings.AssetsFolderOverride);   // where 5.0 and 6.0 look for it
        settings.SetChosenLocation(LocationIds.Assets, ClientScope.Live, null);
        Assert.Null(settings.AssetsFolderOverride);
        Assert.Equal(@"E:\Test client\assets", settings.ChosenLocation(LocationIds.Assets, ClientScope.TestServer));

        // Settings that never chose an assets folder get no note.
        File.WriteAllText(file, """{ "Version": 4 }""");
        Assert.DoesNotContain(new SettingsStore(file).Load().Settings.MigrationNotes, n => n.Contains("assets folder"));
    }

    [Fact]
    public void ChosenLocationsAreSavedWhereEarlierVersionsKeepThem()
    {
        string dir = TestData.NewTempDirectory("saved");
        var store = new SettingsStore(Path.Combine(dir, "settings.json"));
        var settings = new AppSettings();
        settings.Normalize();
        settings.SetChosenLocation(LocationIds.PackagesFolder, ClientScope.TestServer, @"G:\Tibia\packages");  // shared by both clients
        settings.SetChosenLocation(LocationIds.ResourceFile, ClientScope.Live, @"G:\rcc\graphics_resources.rcc");
        settings.SetChosenLocation(LocationIds.SoundsFolder, ClientScope.TestServer, @"G:\test sounds");
        settings.SetChosenLocation(LocationIds.SpritesFolder, ClientScope.TestServer, @"G:\sprites test");
        settings.SetChosenLocation(LocationIds.Assets, ClientScope.Live, @"G:\assets");
        store.Save(settings);

        using var json = JsonDocument.Parse(File.ReadAllText(store.FilePath));
        var root = json.RootElement;
        Assert.Equal(AppSettings.CurrentVersion, root.GetProperty("Version").GetInt32()); // 5 in 7.0; 6 since 7.3 (format 6 only adds the client store settings)
        var locations = root.GetProperty("ClientLocations");   // top level: 5.0 and 6.0 keep unknown top-level settings
        Assert.Equal(@"G:\Tibia\packages", locations.GetProperty("packages").GetString());
        Assert.Equal(@"G:\rcc\graphics_resources.rcc", locations.GetProperty("live:rcc").GetString());
        Assert.Equal(@"G:\test sounds", locations.GetProperty("test:sounds-folder").GetString());
        Assert.Equal(@"G:\assets", root.GetProperty("AssetsFolderOverride").GetString());          // the 6.0 setting
        Assert.Equal(@"G:\sprites test", root.GetProperty("SpritesFolderTestServer").GetString()); // the 6.0 setting

        var loaded = new SettingsStore(store.FilePath).Load().Settings;
        Assert.Equal(@"G:\Tibia\packages", loaded.ChosenLocation(LocationIds.PackagesFolder, ClientScope.Live));
        Assert.Equal(@"G:\rcc\graphics_resources.rcc", loaded.ChosenLocation(LocationIds.ResourceFile, ClientScope.Live));
        Assert.Null(loaded.ChosenLocation(LocationIds.ResourceFile, ClientScope.TestServer));
        Assert.Equal(@"G:\test sounds", loaded.ChosenLocation(LocationIds.SoundsFolder, ClientScope.TestServer));
        Assert.Equal(new[] { LocationIds.PackagesFolder, LocationIds.Assets, LocationIds.ResourceFile }.Order(StringComparer.Ordinal),
            loaded.ChosenLocations(ClientScope.Live).Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new[] { LocationIds.PackagesFolder, LocationIds.SoundsFolder, LocationIds.SpritesFolder }.Order(StringComparer.Ordinal),
            loaded.ChosenLocations(ClientScope.TestServer).Keys.Order(StringComparer.Ordinal));
    }

    // ------------------------------------------------------------------ the application

    private static (AppServices Services, FakeDialogs Dialogs, FakePackages Packages, string Dir) CreateServices(Action<FakePackages>? addPackages = null)
    {
        string dir = TestData.NewTempDirectory("locate");
        var packages = new FakePackages();
        addPackages?.Invoke(packages);
        var settings = new AppSettings { LoadDataAutomatically = false };
        settings.Normalize();
        // Never the installed client: the shared packages folder points into the test folder.
        settings.SetChosenLocation(LocationIds.PackagesFolder, ClientScope.Live, packages.Root);
        var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var dialogs = new FakeDialogs();
        services.Dialogs = dialogs;
        return (services, dialogs, packages, dir);
    }

    [Fact]
    public async Task LocateRefusesWhatCannotBeUsedAndChangesNothing()
    {
        var (services, dialogs, _, dir) = CreateServices();
        using var _ = services;
        Assert.False(services.Data.AssetsAvailable);
        string notAssets = TestData.NewTempDirectory("random");
        dialogs.NextFile = notAssets;

        Assert.False(await services.Locations.LocateAsync(LocationIds.Assets));
        Assert.Contains(dialogs.Messages, m => m.Kind == MessageKind.Warning && m.Message.Contains("catalog-content.json"));
        Assert.Null(services.Settings.AssetsFolderOverride);
        string saved = Path.Combine(dir, "settings.json");
        Assert.True(!File.Exists(saved) || new SettingsStore(saved).Load().Settings.AssetsFolderOverride == null);

        // A data file that cannot be read is refused by the compatibility check of that file.
        string garbage = Path.Combine(notAssets, "appearances-garbage.dat");
        File.WriteAllBytes(garbage, [0xFF, 0xFF, 0xFF, 0xFF, 0x0F, 0x01]);
        dialogs.NextFile = garbage;
        Assert.False(await services.Locations.LocateAsync(LocationIds.Appearances));
        Assert.Null(services.Settings.ChosenLocation(LocationIds.Appearances, ClientScope.Live));

        // Cancelling the picker changes nothing either.
        dialogs.NextFile = null;
        Assert.False(await services.Locations.LocateAsync(LocationIds.Assets));
    }

    [Fact]
    public async Task LocateUsesAValidChoiceAtOnceRemembersItAndResetForgetsIt()
    {
        var (services, dialogs, _, dir) = CreateServices();
        using var _ = services;
        await services.Data.CheckCompatibilityAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(CompatibilityStatus.Missing, services.Data.Compatibility!.Overall);

        // The user picks the package folder although the assets folder is asked for: corrected, checked, used.
        var elsewhere = new FakePackages();
        string copy = elsewhere.Add("Copy", "42.0");
        dialogs.NextFile = copy;
        Assert.True(await services.Locations.LocateAsync(LocationIds.Assets));
        string assets = Path.Combine(copy, "assets");
        Assert.Equal(assets, services.Data.AssetsDirectory);
        Assert.True(services.Data.AssetsAvailable);
        Assert.Equal("42.0", services.Data.ClientVersion);   // the version of that client, not of the installed one
        Assert.Contains("its assets subfolder is used", services.Status.Message);
        // Checked again at once (no restart): the catalog row is no longer missing.
        var catalog = services.Data.Compatibility!.Components.Single(c => c.Component == CompatibilityChecker.SpriteCatalogComponent);
        Assert.True(catalog.Status <= CompatibilityStatus.Unfamiliar, catalog.Summary);
        // Remembered: the settings file on disk has it, and a new start uses it.
        var reloaded = new SettingsStore(Path.Combine(dir, "settings.json")).Load().Settings;
        Assert.Equal(assets, reloaded.ChosenLocation(LocationIds.Assets, ClientScope.Live));
        Assert.Null(reloaded.ChosenLocation(LocationIds.Assets, ClientScope.TestServer));

        await services.Locations.ResetAsync(LocationIds.Assets);
        Assert.Null(services.Settings.AssetsFolderOverride);
        Assert.False(services.Data.AssetsAvailable);
        Assert.Null(new SettingsStore(Path.Combine(dir, "settings.json")).Load().Settings.AssetsFolderOverride);
    }

    [Fact]
    public async Task TheCompatibilityPageLocatesAMissingFileWithoutARestart()
    {
        var (services, dialogs, _, _) = CreateServices(p => p.Add("Tibia", "99.1", resourceFile: false));
        using var _ = services;
        var vm = new CompatibilityViewModel(services);
        await services.Data.CheckCompatibilityAsync(ct: TestContext.Current.CancellationToken);
        var overallBefore = vm.Overall;
        var row = vm.Rows.Single(r => r.Component == CompatibilityChecker.ResourceFileComponent);
        Assert.Equal("Not found (optional)", row.StatusText);
        Assert.Equal("Locate…", row.LocateText);
        Assert.False(row.IsChosen);
        var appearancesBefore = vm.Rows.Single(r => r.Component == TibiaFormat.CatalogTypeAppearances);
        Assert.Equal("found in the folder you chose", appearancesBefore.SourceText);   // the packages folder of the test is a chosen one
        Assert.Equal("Change…", appearancesBefore.LocateText);

        string rcc = WriteResourceFile(Path.Combine(TestData.NewTempDirectory("found"), "graphics_resources.rcc"));
        dialogs.NextFile = rcc;
        await vm.LocateCommand.ExecuteAsync(row);

        var located = vm.Rows.Single(r => r.Component == CompatibilityChecker.ResourceFileComponent);
        Assert.Equal(CompatibilityStatus.Unfamiliar, located.Status);
        Assert.Equal(rcc, located.Path);
        Assert.Equal("chosen by you", located.SourceText);
        Assert.True(located.IsChosen);
        Assert.Equal(rcc, services.Data.ResourceFile);
        Assert.Equal(overallBefore, vm.Overall);   // an optional file never changes the verdict
        Assert.Equal(appearancesBefore.Summary, vm.Rows.Single(r => r.Component == TibiaFormat.CatalogTypeAppearances).Summary);

        await vm.ResetLocationCommand.ExecuteAsync(located);
        var reset = vm.Rows.Single(r => r.Component == CompatibilityChecker.ResourceFileComponent);
        Assert.Equal(CompatibilityStatus.Missing, reset.Status);
        Assert.False(reset.IsChosen);
        Assert.Null(services.Data.ResourceFile);
    }

    [Fact]
    public void SwitchingClientsDiscardsTheReportAndTheFilesPagesWouldLoad()
    {
        var (services, _, _, _) = CreateServices(p =>
        {
            p.Add("Tibia", "99.1");
            p.Add("TibiaExternal", "99.2");
        });
        using var _ = services;
        var sounds = new SoundsViewModel(services);
        sounds.DatPath = services.Data.DefaultFile(LocationIds.Sounds);
        Assert.Contains(Path.Combine("Tibia", "sounds"), sounds.DatPath);
        services.Data.Compatibility = CompatibilityChecker.Check(services.Data.Locations, ct: TestContext.Current.CancellationToken);

        services.Settings.UseTestServer = true;
        services.Data.SwitchClient();
        Assert.Null(services.Data.Compatibility);
        Assert.Null(sounds.DatPath);
        Assert.Contains(Path.Combine("TibiaExternal", "sounds"), services.Data.DefaultFile(LocationIds.Sounds));
        Assert.Equal("99.2", services.Data.ClientVersion);

        // A changed appearances file does not touch the sounds page's file.
        sounds.DatPath = services.Data.DefaultFile(LocationIds.Sounds);
        services.Data.ApplyLocationChange(LocationIds.Appearances);
        Assert.NotNull(sounds.DatPath);
        services.Data.ApplyLocationChange(LocationIds.Sounds);
        Assert.Null(sounds.DatPath);
    }

    [Fact]
    public void SettingsListEveryLocationOfEitherClient()
    {
        var (services, _, packages, _) = CreateServices(p => p.Add("Tibia", "99.1"));
        using var _ = services;
        var settingsVm = new SettingsViewModel(services, new MainViewModel(services));
        Assert.Equal(LocationResolver.All.Select(d => d.Name), settingsVm.LocationRows.Select(r => r.Name));
        var packagesRow = settingsVm.LocationRows.Single(r => r.Id == LocationIds.PackagesFolder);
        Assert.True(packagesRow.IsChosen);
        Assert.Equal(packages.Root, packagesRow.Path);
        Assert.Equal("found in the folder you chose", settingsVm.LocationRows.Single(r => r.Id == LocationIds.Assets).Origin);

        settingsVm.LocationScope = ClientScope.TestServer;
        var assets = settingsVm.LocationRows.Single(r => r.Id == LocationIds.Assets);
        Assert.Equal(ClientScope.TestServer, assets.Scope);
        Assert.Contains("TibiaExternal", assets.Path);
        Assert.NotNull(assets.Status);   // no test-server client in the packages folder
        Assert.Contains("not in use", settingsVm.LocationScopeNote);
    }
}
