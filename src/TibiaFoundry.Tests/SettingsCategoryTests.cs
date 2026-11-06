using System.Text.Json;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.Tests;

/// <summary>Settings format 3: categories, migration from format 2, validation and folder precedence.</summary>
public class SettingsCategoryTests
{
    [Fact]
    public void Format2SettingsAreUpgradedWithoutLosingAnything()
    {
        string dir = TestData.NewTempDirectory("settings3");
        string file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, """
            { "Version": 2, "Theme": "Dark", "OutputFolder": "D:\\Out", "CreatorName": "Someone", "GifExtraDelay": 50, "FutureOption": { "x": 1 } }
            """);
        var (settings, status, _) = new SettingsStore(file).Load();
        Assert.Equal(SettingsLoadStatus.Loaded, status);
        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.Equal(ThemePreference.Dark, settings.Theme);
        Assert.Equal(@"D:\Out", settings.OutputFolder);
        Assert.Equal("Someone", settings.CreatorName);
        Assert.Equal(50, settings.GifExtraDelay);
        Assert.Equal(ExistingFilePolicy.Ask, settings.Safety.WhenOutputExists);
        Assert.True(settings.Safety.BackupBeforeChanges);
        Assert.True(settings.Scripts.PreviewFirst);
        new SettingsStore(file).Save(settings);
        string saved = File.ReadAllText(file);
        Assert.Contains("\"FutureOption\"", saved);
        Assert.Contains("\"Safety\"", saved);
        Assert.Contains($"\"Version\": {AppSettings.CurrentVersion}", saved);
    }

    [Fact]
    public void NumericValuesAreClamped()
    {
        var s = new AppSettings();
        s.Interface.Scale = 9;
        s.Interface.ThumbnailSize = 33;
        s.Performance.MaxConcurrentOperations = 0;
        s.Performance.SpriteSheetCache = 5;
        s.Performance.WorkerThreads = -3;
        s.Safety.BackupRetentionDays = -1;
        s.Notifications.LogRetentionDays = 0;
        s.Notifications.LogLevel = (LogLevel)42;
        s.Scripts.MaxConcurrentScripts = 99;
        s.Scripts.MaxHistoryEntries = 1;
        s.Safety.WhenOutputExists = (ExistingFilePolicy)9;
        s.Normalize();
        Assert.Equal(InterfaceSettings.MaxScale, s.Interface.Scale);
        Assert.Equal(32, s.Interface.ThumbnailSize);
        Assert.Equal(1, s.Performance.MaxConcurrentOperations);
        Assert.Equal(16, s.Performance.SpriteSheetCache);
        Assert.Equal(0, s.Performance.WorkerThreads);
        Assert.Equal(0, s.Safety.BackupRetentionDays);
        Assert.Equal(1, s.Notifications.LogRetentionDays);
        Assert.Equal(LogLevel.Info, s.Notifications.LogLevel);
        Assert.Equal(PerformanceSettings.MaxOperations, s.Scripts.MaxConcurrentScripts);
        Assert.Equal(10, s.Scripts.MaxHistoryEntries);
        Assert.Equal(ExistingFilePolicy.Ask, s.Safety.WhenOutputExists);
    }

    [Fact]
    public void MissingCategoriesGetDefaults()
    {
        var s = JsonSerializer.Deserialize<AppSettings>("""{ "General": null, "Safety": null }""")!;
        s.Normalize();
        Assert.NotNull(s.General);
        Assert.NotNull(s.Safety);
        Assert.True(s.Safety.ValidateOutputs);
    }

    [Fact]
    public void ToolFoldersFollowTheDocumentedPrecedence()
    {
        var s = new AppSettings { OutputFolder = @"C:\Out" };
        Assert.Equal(@"C:\Out\RCC_Exports", s.OutputFolderFor(ToolKeys.RccEditor));
        s.RememberFolder(ToolKeys.RccEditor, "output", @"C:\Last");
        Assert.Equal(@"C:\Last", s.OutputFolderFor(ToolKeys.RccEditor));
        s.Locations.Tools[ToolKeys.RccEditor] = new ToolFolderSettings { Output = @"C:\Fixed" };
        Assert.Equal(@"C:\Fixed", s.OutputFolderFor(ToolKeys.RccEditor));
        s.General.RememberLastFolders = false;
        s.Locations.Tools.Clear();
        Assert.Equal(@"C:\Out\RCC_Exports", s.OutputFolderFor(ToolKeys.RccEditor));
        s.RememberFolder(ToolKeys.Scripts, "input", @"C:\NotKept");
        Assert.Null(s.InputFolderFor(ToolKeys.Scripts));
    }

    [Fact]
    public void FileSafetyReflectsTheSettings()
    {
        var s = new AppSettings();
        s.Safety.WhenOutputExists = ExistingFilePolicy.KeepBoth;
        s.Safety.BackupBeforeChanges = false;
        s.Safety.ValidateOutputs = false;
        var options = s.FileSafety;
        Assert.Equal(ExistingFilePolicy.KeepBoth, options.WhenExists);
        Assert.False(options.BackupBeforeOverwrite);
        Assert.False(options.ValidateOutputs);
        s.Performance.WorkerThreads = 3;
        Assert.Equal(3, s.EffectiveWorkerThreads);
    }
}
