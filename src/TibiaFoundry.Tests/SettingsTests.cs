using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using System.Text.Json;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.Tests;

/// <summary>Settings persistence: defaults, round trip, corruption recovery and 1.x import.</summary>
public sealed class SettingsTests
{
    [Fact]
    public void MissingFileGivesDefaults()
    {
        string dir = TestData.NewTempDirectory();
        var (settings, status, _) = new SettingsStore(Path.Combine(dir, "settings.json")).Load();
        Assert.Equal(SettingsLoadStatus.CreatedDefaults, status);
        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.Equal(1280, settings.MainWindow.Width);
        Assert.True(settings.LoadDataAutomatically);
        Assert.False(File.Exists(Path.Combine(dir, "settings.json")));
    }

    [Fact]
    public void SaveAndLoadRoundTripsPreferences()
    {
        string file = Path.Combine(TestData.NewTempDirectory(), "sub", "settings.json");
        var store = new SettingsStore(file);
        var settings = new AppSettings
        {
            Theme = ThemePreference.Dark,
            MainWindow = new WindowPlacement { Left = 10, Top = 20, Width = 1500, Height = 900, Maximized = true },
            LastPage = "SoundsViewModel",
            UseTestServer = true,
            TestServerPackage = "TibiaExternalTest",
            SpriteSource = SpriteSourceMode.ClientOnly,
            CreatorName = "Someone",
            SoundVolume = 0.25,
        };
        store.Save(settings);

        Assert.True(File.Exists(file));
        Assert.Equal([file], Directory.GetFiles(Path.GetDirectoryName(file)!));   // no temporary files left behind
        var (loaded, status, _) = store.Load();
        Assert.Equal(SettingsLoadStatus.Loaded, status);
        Assert.Equal(ThemePreference.Dark, loaded.Theme);
        Assert.Equal(1500, loaded.MainWindow.Width);
        Assert.Equal(900, loaded.MainWindow.Height);
        Assert.Equal(10, loaded.MainWindow.Left);
        Assert.True(loaded.MainWindow.Maximized);
        Assert.Equal("SoundsViewModel", loaded.LastPage);
        Assert.True(loaded.UseTestServer);
        Assert.Equal("TibiaExternalTest", loaded.TestServerPackage);
        Assert.Equal(SpriteSourceMode.ClientOnly, loaded.SpriteSource);
        Assert.Equal("Someone", loaded.CreatorName);
        Assert.Equal(0.25, loaded.SoundVolume, 6);

        // Enums are stored by name so the file stays readable and stable.
        string json = File.ReadAllText(file);
        Assert.Contains("\"Dark\"", json);
        Assert.Contains("\"ClientOnly\"", json);
    }

    [Fact]
    public void CorruptFileIsBackedUpAndDefaultsAreUsed()
    {
        string dir = TestData.NewTempDirectory();
        string file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, "{ \"Theme\": \"Dark\", this is not json");

        var (settings, status, backup) = new SettingsStore(file).Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruption, status);
        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.NotNull(backup);
        Assert.True(File.Exists(backup));
        Assert.Equal("{ \"Theme\": \"Dark\", this is not json", File.ReadAllText(backup!));
    }

    [Fact]
    public void OutOfRangeValuesAreClamped()
    {
        string file = Path.Combine(TestData.NewTempDirectory(), "settings.json");
        File.WriteAllText(file, """
            {
              "Theme": 42,
              "MainWindow": { "Width": 5, "Height": 999999 },
              "OutputFolder": "  ",
              "TestServerPackage": "",
              "SoundVolume": "NaN",
              "GifNoAnimationDelay": -5,
              "GifExtraDelay": 999999
            }
            """);

        var (s, status, _) = new SettingsStore(file).Load();

        Assert.Equal(SettingsLoadStatus.Loaded, status);
        Assert.Equal(ThemePreference.System, s.Theme);
        Assert.Equal(1280, s.MainWindow.Width);
        Assert.Equal(820, s.MainWindow.Height);
        Assert.Equal(AppSettings.DefaultOutputFolder, s.OutputFolder);
        Assert.Equal("TibiaExternal", s.TestServerPackage);
        Assert.Equal(0.8, s.SoundVolume);
        Assert.Equal(0, s.GifNoAnimationDelay);
        Assert.Equal(60000, s.GifExtraDelay);
    }

    [Fact]
    public void LegacySettingsAreImportedWhenNoNewFileExists()
    {
        string dir = TestData.NewTempDirectory();
        string legacy = Path.Combine(dir, "legacy-settings.json");
        File.WriteAllText(legacy, JsonSerializer.Serialize(new
        {
            OutputPath = @"D:\Wiki\Output\",
            AllSpritesPath = @"D:\Wiki\Sprites",
            AllSoundsPath = @"D:\Wiki\Sounds",
            AllSpritesPathTS = @"D:\Wiki\SpritesTS",
            TSName = "TibiaExternal",
            Author = "Dana",
            GifDir = @"D:\Wiki\Gifs",
        }));

        var (s, status, detail) = new SettingsStore(Path.Combine(dir, "settings.json")).Load([legacy]);

        Assert.Equal(SettingsLoadStatus.ImportedLegacy, status);
        Assert.Equal(legacy, detail);
        Assert.Equal(@"D:\Wiki\Output", s.OutputFolder);
        Assert.Equal(@"D:\Wiki\Sprites", s.SpritesFolder);
        Assert.Equal(@"D:\Wiki\SpritesTS", s.SpritesFolderTestServer);
        Assert.Equal(@"D:\Wiki\Sounds", s.SoundsFolder);
        Assert.Equal(@"D:\Wiki\Gifs", s.GifFolder);
        Assert.Equal("Dana", s.CreatorName);                 // the 1.x author becomes the creator name
        Assert.False(AppIdentity.Legacy.IsDefaultGifAuthor("Dana"));
        Assert.False(AppIdentity.Legacy.IsDefaultGifAuthor(""));
        Assert.False(AppIdentity.Legacy.IsDefaultGifAuthor(null));
    }

    [Fact]
    public void ExistingSettingsWinOverLegacyFile()
    {
        string dir = TestData.NewTempDirectory();
        string file = Path.Combine(dir, "settings.json");
        new SettingsStore(file).Save(new AppSettings { CreatorName = "Current" });
        string legacy = Path.Combine(dir, "legacy.json");
        File.WriteAllText(legacy, "{\"Author\":\"Old\"}");

        var (s, status, _) = new SettingsStore(file).Load([legacy]);

        Assert.Equal(SettingsLoadStatus.Loaded, status);
        Assert.Equal("Current", s.CreatorName);
    }

    [Fact]
    public void SettingsFileContainsOnlyPathsAndPreferences()
    {
        // The spec forbids secrets in plain-text configuration: verify the persisted shape.
        string file = Path.Combine(TestData.NewTempDirectory(), "settings.json");
        new SettingsStore(file).Save(new AppSettings());
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            Assert.DoesNotContain("password", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("key", property.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EachLogLimitIsResetWithTheSectionThatShowsIt()
    {
        // 7.2: the total size of the log files is shown under "Notifications and logging", next to the other two limits.
        string dir = TestData.NewTempDirectory("resets");
        var settings = new AppSettings { LoadDataAutomatically = false };
        settings.Normalize();
        using var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var vm = new SettingsViewModel(services, new MainViewModel(services));
        vm.LogMaxTotalMegabytes = 500;
        vm.LogRetentionDays = 30;
        vm.SampleIntervalSeconds = 10;
        vm.ResetDiagnosticsCommand.Execute(null);
        Assert.Equal((500, 2), (services.Settings.Diagnostics.LogMaxTotalMegabytes, services.Settings.Diagnostics.SampleIntervalSeconds));
        Assert.Equal(30, services.Settings.Notifications.LogRetentionDays);
        vm.ResetNotificationsCommand.Execute(null);
        Assert.Equal((200, 14), (services.Settings.Diagnostics.LogMaxTotalMegabytes, services.Settings.Notifications.LogRetentionDays));
    }

    [Fact]
    public void AFolderNextToAPathSaysWhetherItCanBeUsed()
    {
        string dir = TestData.NewTempDirectory("status");
        Assert.Equal("", SettingsViewModel.FolderStatus(dir));
        Assert.Contains("does not exist", SettingsViewModel.FolderStatus(Path.Combine(dir, "missing")));
        Assert.Empty(Directory.GetFileSystemEntries(dir));   // the probe file is gone again
        Assert.True(OutputLocations.CanWrite(dir, out string? problem));
        Assert.Null(problem);
        Assert.False(OutputLocations.CanWrite(Path.Combine(dir, "missing"), out problem));
        Assert.Contains("cannot be written to", problem);
    }
}
