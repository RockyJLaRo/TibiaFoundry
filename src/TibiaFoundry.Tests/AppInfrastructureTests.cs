using System.Globalization;
using System.Windows;
using System.Windows.Data;
using TibiaFoundry.App;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.Tests;

/// <summary>Value converters and command-line parsing of the WPF application.</summary>
public sealed class AppInfrastructureTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void EnumEqualsConverterDrivesRadioButtons()
    {
        var c = new EnumEqualsConverter();
        Assert.Equal(true, c.Convert(SpriteSourceMode.ClientOnly, typeof(bool), "ClientOnly", En));
        Assert.Equal(false, c.Convert(SpriteSourceMode.FolderOnly, typeof(bool), "ClientOnly", En));
        Assert.Equal(false, c.Convert(null!, typeof(bool), "ClientOnly", En));
        Assert.Equal(SpriteSourceMode.FolderOnly, c.ConvertBack(true, typeof(SpriteSourceMode), "FolderOnly", En));
        // Unchecking a radio button must not change the bound value.
        Assert.Same(Binding.DoNothing, c.ConvertBack(false, typeof(SpriteSourceMode), "FolderOnly", En));
    }

    [Fact]
    public void BoolAndNullVisibilityConverters()
    {
        var b = new BoolToVisibilityConverter();
        Assert.Equal(Visibility.Visible, b.Convert(true, typeof(Visibility), null!, En));
        Assert.Equal(Visibility.Collapsed, b.Convert(false, typeof(Visibility), null!, En));
        Assert.Equal(Visibility.Collapsed, b.Convert(true, typeof(Visibility), "Invert", En));
        Assert.Equal(Visibility.Visible, b.Convert(null!, typeof(Visibility), "Invert", En));

        var n = new NullToVisibilityConverter();
        Assert.Equal(Visibility.Collapsed, n.Convert(null!, typeof(Visibility), null!, En));
        Assert.Equal(Visibility.Collapsed, n.Convert("", typeof(Visibility), null!, En));
        Assert.Equal(Visibility.Visible, n.Convert("x", typeof(Visibility), null!, En));
        Assert.Equal(Visibility.Collapsed, n.Convert(new List<int>(), typeof(Visibility), null!, En));
        Assert.Equal(Visibility.Visible, n.Convert(new List<int> { 1 }, typeof(Visibility), null!, En));
        Assert.Equal(Visibility.Collapsed, n.Convert(0, typeof(Visibility), null!, En));
        Assert.Equal(Visibility.Visible, n.Convert(0, typeof(Visibility), "Invert", En));
    }

    [Fact]
    public void ValuesEqualAndNumberConverters()
    {
        var eq = new ValuesEqualConverter();
        Assert.Equal(true, eq.Convert([SpriteSourceMode.ClientOnly, SpriteSourceMode.ClientOnly], typeof(bool), null!, En));
        Assert.Equal(false, eq.Convert([SpriteSourceMode.ClientOnly, SpriteSourceMode.FolderOnly], typeof(bool), null!, En));
        Assert.Equal(false, eq.Convert([SpriteSourceMode.ClientOnly], typeof(bool), null!, En));

        var num = new NumberConverter();
        Assert.Equal("223,694", num.Convert(223694, typeof(string), null!, En));
        Assert.Equal("", num.Convert(null!, typeof(string), null!, En));
    }

    [Fact]
    public void SelfTestOptionIsParsed()
    {
        var o = StartupOptions.Parse(["--self-test", "self-test-output"]);
        Assert.Equal("self-test-output", o.SelfTestFolder);
        Assert.Null(o.SnapshotFolder);
    }

    [Fact]
    public void InstalledModeUsesUserProfileFolders()
    {
        string program = TestData.NewTempDirectory("program");
        var paths = AppPaths.Resolve(program, portableBuild: false);
        Assert.False(paths.IsPortable);
        Assert.Null(paths.Notice);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), paths.SettingsFile, StringComparison.OrdinalIgnoreCase);
        // 6.0: the start-up log goes to Documents\Tibia_Foundry\Logs (Windows' Documents folder, wherever it is redirected).
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tibia_Foundry", "Logs"), paths.LogDirectory, ignoreCase: true);
        Assert.False(Directory.Exists(Path.Combine(program, AppPaths.PortableDataFolder)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void PortableModeKeepsDataNextToTheProgram(bool portableBuild, bool markerFile)
    {
        string program = TestData.NewTempDirectory("program");
        if (markerFile) File.WriteAllText(Path.Combine(program, AppPaths.PortableMarkerFile), "");
        var paths = AppPaths.Resolve(program, portableBuild);
        string data = Path.Combine(program, AppPaths.PortableDataFolder);
        Assert.True(paths.IsPortable);
        Assert.Null(paths.Notice);
        Assert.Equal(Path.Combine(data, "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine(data, "logs"), paths.LogDirectory);
        Assert.Equal(data, paths.DataDirectory);
        Assert.True(Directory.Exists(data));
        Assert.Empty(Directory.GetFiles(data)); // the write probe is removed again
    }

    [Fact]
    public void PortableModeFallsBackWhenTheProgramFolderIsNotWritable()
    {
        string program = TestData.NewTempDirectory("program");
        // A file where the data folder should be makes the folder impossible to create.
        File.WriteAllText(Path.Combine(program, AppPaths.PortableDataFolder), "blocked");
        var paths = AppPaths.Resolve(program, portableBuild: true);
        Assert.False(paths.IsPortable);
        Assert.NotNull(paths.Notice);
        Assert.Contains("read-only", paths.Notice);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), paths.SettingsFile, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(new string[0], null, null, null)]
    [InlineData(new[] { "--ui-snapshot", @"C:\out" }, @"C:\out", null, null)]
    [InlineData(new[] { "--UI-SNAPSHOT", "out", "--theme", "dark", "--size", "940x620" }, "out", ThemePreference.Dark, "940x620")]
    [InlineData(new[] { "--theme", "bogus", "--size", "abc" }, null, null, null)]
    [InlineData(new[] { "--theme" }, null, null, null)]
    public void StartupOptionsParse(string[] args, string? folder, ThemePreference? theme, string? size)
    {
        var o = StartupOptions.Parse(args);
        Assert.Equal(folder, o.SnapshotFolder);
        Assert.Equal(theme, o.Theme);
        Assert.Equal(size, o.Size is { } s ? $"{s.Width}x{s.Height}" : null);
    }
}
