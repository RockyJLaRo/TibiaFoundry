using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.Themes;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Theming;

namespace TibiaFoundry.Tests;

[CollectionDefinition(nameof(ThemeCollection), DisableParallelization = true)]
public sealed class ThemeCollection;

/// <summary>
/// Theme customization: colors, HSL and hex input, derived colors, accessibility checks, custom
/// themes and their persistence, and the live update of the interface's brushes. The theme
/// manager is process-wide, so these tests never run in parallel with others.
/// </summary>
[Collection(nameof(ThemeCollection))]
public sealed class ThemeTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ThemesDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "src", "TibiaFoundry.App", "Themes");
            if (Directory.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "TibiaFoundry.App", "Themes");
            if (Directory.Exists(candidate)) return candidate;
        }
        Assert.Skip("The XAML sources were not found.");
        return "";
    }

    /// <summary>The token colors of a palette file, read as XML (no WPF needed).</summary>
    private static Dictionary<string, ThemeColor> PaletteFile(string name) =>
        XDocument.Load(Path.Combine(ThemesDir(), name)).Root!.Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush" && e.Attribute(X + "Key")!.Value.StartsWith("App.", StringComparison.Ordinal))
            .Where(e => ThemeTokens.IsToken(e.Attribute(X + "Key")!.Value[4..]))
            .ToDictionary(e => e.Attribute(X + "Key")!.Value[4..], e => ThemeColor.Parse(e.Attribute("Color")!.Value));

    // ------------------------------------------------------------------ colors

    [Theory]
    [InlineData("#2F6FDB", "#2F6FDB")]
    [InlineData("2f6fdb", "#2F6FDB")]
    [InlineData("  #abc ", "#AABBCC")]
    [InlineData("#FF102030", "#102030")]
    public void ValidHexColorsAreAcceptedAndNormalized(string input, string expected)
    {
        Assert.True(ThemeColor.TryParse(input, out var color, out string? problem), problem);
        Assert.Equal(expected, color.Hex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGHHII")]
    [InlineData("blue")]
    [InlineData("#802F6FDB")]
    [InlineData("#2F6FDB0")]
    public void InvalidHexColorsAreRefusedWithAnExplanation(string input)
    {
        Assert.False(ThemeColor.TryParse(input, out _, out string? problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Fact]
    public void HueSaturationAndLightnessRoundTrip()
    {
        foreach (string hex in new[] { "#2F6FDB", "#1C2530", "#FFFFFF", "#000000", "#C42B2B", "#82D38E", "#9A6A12", "#7F7F7F" })
        {
            var c = ThemeColor.Parse(hex);
            var (h, s, l) = c.ToHsl();
            var back = ThemeColor.FromHsl(h, s, l);
            Assert.True(Math.Abs(back.R - c.R) <= 1 && Math.Abs(back.G - c.G) <= 1 && Math.Abs(back.B - c.B) <= 1, $"{hex} → {back.Hex}");
        }
        Assert.Equal("#FF0000", ThemeColor.FromHsl(0, 1, 0.5).Hex);
        Assert.Equal("#00FF00", ThemeColor.FromHsl(120, 1, 0.5).Hex);
        Assert.Equal("#0000FF", ThemeColor.FromHsl(240, 1, 0.5).Hex);
        Assert.Equal(21, ThemeColor.Contrast(ThemeColor.White, ThemeColor.Black), 2);
    }

    // ------------------------------------------------------------------ palettes and derivation

    [Fact]
    public void BothPalettesDefineExactlyTheThemeTokens()
    {
        foreach (string file in new[] { "Colors.Light.xaml", "Colors.Dark.xaml" })
        {
            var keys = XDocument.Load(Path.Combine(ThemesDir(), file)).Root!.Elements()
                .Where(e => e.Name.LocalName == "SolidColorBrush").Select(e => e.Attribute(X + "Key")!.Value[4..]).ToHashSet();
            keys.Remove("Shadow"); // a shadow color, not customizable
            Assert.Equal(ThemeTokens.All.Select(t => t.Key).OrderBy(k => k), keys.OrderBy(k => k));
        }
        // A token's sources come before it (the resolver relies on the order).
        var seen = new HashSet<string>();
        foreach (var token in ThemeTokens.All)
        {
            Assert.All(token.Sources, s => Assert.Contains(s, seen));
            seen.Add(token.Key);
        }
    }

    [Theory]
    [InlineData("Colors.Light.xaml")]
    [InlineData("Colors.Dark.xaml")]
    public void BuiltInPalettesPassTheReadabilityChecksAndResolveToThemselves(string file)
    {
        var palette = PaletteFile(file);
        Assert.Empty(ThemeAccessibility.Check(palette));
        var resolved = ThemeResolver.Resolve(palette, new Dictionary<string, ThemeColor>());
        Assert.All(ThemeTokens.All, t => Assert.Equal(palette[t.Key], resolved[t.Key]));      // the default look is preserved exactly
    }

    [Fact]
    public void DerivedColorsFollowAChangedColorUnlessTheyAreChangedThemselves()
    {
        var light = PaletteFile("Colors.Light.xaml");
        var accent = ThemeColor.Parse("#B03A2E");
        var resolved = ThemeResolver.Resolve(light, new Dictionary<string, ThemeColor> { ["Accent"] = accent });
        Assert.Equal(accent, resolved["Accent"]);
        foreach (string follower in new[] { "AccentHover", "AccentPressed", "AccentSoft", "Selection", "Link", "Focus" })
            Assert.NotEqual(light[follower], resolved[follower]);
        Assert.Equal(light["Text"], resolved["Text"]);                     // unrelated colors keep their built-in values
        Assert.Equal(light["WindowBackground"], resolved["WindowBackground"]);
        Assert.True(ThemeColor.Contrast(resolved["Link"], resolved["Surface"]) >= 4.5);   // derived links stay readable

        var pinned = ThemeColor.Parse("#123456");
        var withPinned = ThemeResolver.Resolve(light, new Dictionary<string, ThemeColor> { ["Accent"] = accent, ["Selection"] = pinned });
        Assert.Equal(pinned, withPinned["Selection"]);                     // an individually changed color is not overridden
    }

    [Fact]
    public void ReadabilityProblemsAreReported()
    {
        var palette = PaletteFile("Colors.Light.xaml");
        var bad = new Dictionary<string, ThemeColor> { ["Text"] = ThemeColor.Parse("#E8EDF2"), ["Hover"] = palette["Surface"] };
        var warnings = ThemeAccessibility.Check(ThemeResolver.Resolve(palette, bad));
        Assert.Contains(warnings, w => w.Foreground == "Text" && w.Background == "Surface" && w.Ratio < 4.5);
        Assert.Contains(warnings, w => w.Foreground == "Hover");          // hover no longer distinguishable
    }

    // ------------------------------------------------------------------ settings and files

    [Fact]
    public void LoadedThemeSettingsAreRepairedGracefully()
    {
        var json = """
            { "Version": 4, "Themes": { "ActiveCustomTheme": "gone", "Custom": [
                { "Id": "a", "Name": "  Mine  ", "Base": "Dark", "Colors": { "Accent": "#AA0000", "Text": "not-a-color", "FutureToken": "#010203" } },
                { "Id": "a", "Name": "mine", "Base": "Light", "Colors": null },
                { "Id": "", "Name": "", "Base": "Light" } ] } }
            """;
        string dir = TestData.NewTempDirectory("themes");
        string file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, json);
        var (s, status, _) = new SettingsStore(file).Load();
        Assert.Equal(SettingsLoadStatus.Loaded, status);
        var themes = s.Themes;
        Assert.Null(themes.ActiveCustomTheme);                                        // the active theme no longer exists → built-in
        Assert.Equal(3, themes.Custom.Count);
        Assert.Equal(3, themes.Custom.Select(t => t.Id).Distinct().Count());          // duplicate and empty ids replaced
        Assert.Equal(["Mine", "mine (2)", "Custom theme"], themes.Custom.Select(t => t.Name));
        Assert.Equal("#AA0000", themes.Custom[0].Colors["Accent"]);
        Assert.False(themes.Custom[0].Colors.ContainsKey("Text"));                    // invalid value dropped
        Assert.Equal("#010203", themes.Custom[0].Colors["FutureToken"]);              // a newer version's color is kept
        Assert.Single(themes.Custom[0].ValidOverrides());
    }

    [Fact]
    public void ExportedThemesImportWithEveryColorChecked()
    {
        var theme = new CustomTheme { Id = "x", Name = "Ruby", Base = ThemeBase.Dark, Colors = new() { ["Accent"] = "#B03A2E", ["Link"] = "#FF8877" } };
        var (back, ignored) = ThemeFile.Parse(ThemeFile.Serialize(theme));
        Assert.Empty(ignored);
        Assert.Equal("Ruby", back.Name);
        Assert.Equal(ThemeBase.Dark, back.Base);
        Assert.Equal(theme.Colors, back.Colors);
        Assert.NotEqual("x", back.Id);                                                 // imported themes get a new id

        string tampered = """{ "Format": "TibiaFoundry.Theme", "Version": 1, "Name": "T", "Base": "Light", "Colors": { "Accent": "#zz0000", "Bogus": "#000000", "Text": "#101010" } }""";
        var (t2, ignored2) = ThemeFile.Parse(tampered);
        Assert.Equal(2, ignored2.Count);
        Assert.Equal(["Text"], t2.Colors.Keys);
        Assert.Throws<InvalidDataException>(() => ThemeFile.Parse("""{ "Version": 4, "Theme": "Dark" }"""));
        Assert.Throws<InvalidDataException>(() => ThemeFile.Parse("not json"));
    }

    // ------------------------------------------------------------------ the live engine

    [Fact(Timeout = 120_000)]
    public async Task ChangingAColorRecolorsOnlyTheAffectedBrushesInPlace()
    {
        await Sta.Run(() =>
        {
            var host = new ResourceDictionary();
            var engine = new ThemeEngine(host);
            engine.Apply(ThemeBase.Light, null);
            var palette = engine.Palette!;
            Assert.Same(palette, host.MergedDictionaries[0]);
            var light = PaletteFile("Colors.Light.xaml");
            Assert.All(ThemeTokens.All, t => Assert.Equal(light[t.Key].Hex, Hex(((SolidColorBrush)palette["App." + t.Key]).Color)));
            Assert.NotNull(palette["App.Checkerboard"]);                                 // non-color resources are kept

            var text = (SolidColorBrush)palette["App.Text"];
            var accent = (SolidColorBrush)palette["App.Accent"];
            var theme = new CustomTheme { Id = "t", Name = "T", Base = ThemeBase.Light };
            theme.Colors["Accent"] = "#B03A2E";
            engine.UpdateColors(theme);
            Assert.Same(palette, engine.Palette);                                         // no dictionary was replaced
            Assert.Same(accent, palette["App.Accent"]);                                   // the same brush, recolored
            Assert.Equal("#B03A2E", Hex(accent.Color));
            Assert.Equal(light["Text"].Hex, Hex(text.Color));                             // unrelated brushes untouched
            Assert.InRange(engine.LastUpdatedBrushes, 2, ThemeTokens.All.Count - 10);    // the accent and the colors that follow it

            engine.UpdateColors(new CustomTheme { Id = "t", Name = "T", Base = ThemeBase.Dark });
            Assert.NotSame(palette, engine.Palette);                                      // another base: a full palette switch
            Assert.True(engine.IsDark);
            return Task.CompletedTask;
        });
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static (AppServices Services, FakeDialogs Dialogs, string Dir) CreateServices()
    {
        string dir = TestData.NewTempDirectory("themes");
        var settings = new AppSettings { OutputFolder = Path.Combine(dir, "out"), LoadDataAutomatically = false, CreatorName = "ThemeTest" };
        settings.Normalize();
        var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var dialogs = new FakeDialogs();
        services.Dialogs = dialogs;
        return (services, dialogs, dir);
    }

    [Fact(Timeout = 120_000)]
    public async Task CustomThemesAreCreatedEditedSavedRestoredAndDeleted()
    {
        await Sta.Run(() =>
        {
            var host = new ResourceDictionary();
            ThemeManager.UseEngineForTests(new ThemeEngine(host));
            try
            {
                var (services, dialogs, dir) = CreateServices();
                using var disposeServices = services;
                services.Settings.Safety.BackupRetentionDays = 7;                         // unrelated settings must survive theme operations
                services.Settings.Theme = ThemePreference.Light;
                services.SaveSettings();
                ThemeManager.Initialize(ThemePreference.Light);
                var main = new MainViewModel(services);
                var editor = main.Pages.OfType<SettingsViewModel>().Single().Themes;
                Assert.Equal(["System", "Light", "Dark"], editor.Themes.Select(t => t.Name));
                Assert.True(editor.Themes.Single(t => t.Name == "Light").IsActive);
                Assert.False(editor.CanEdit);                                             // built-in themes are protected
                editor.RenameThemeCommand.Execute(null);
                editor.DeleteThemeCommand.Execute(null);
                Assert.Equal(3, editor.Themes.Count);

                // Create a theme from the light palette.
                dialogs.PromptAnswers.Enqueue("Ruby");
                editor.NewThemeCommand.Execute(null);
                var ruby = Assert.Single(services.Settings.Themes.Custom);
                Assert.Equal("Ruby", ruby.Name);
                Assert.Equal(ThemeBase.Light, ruby.Base);
                Assert.Equal(ruby.Id, services.Settings.Themes.ActiveCustomTheme);
                Assert.True(editor.CanEdit);
                Assert.True(editor.Themes.Single(t => t.Name == "Ruby").IsActive);

                // Hex input: invalid text is explained and not applied; valid text applies at once.
                var accentBrush = (SolidColorBrush)host.MergedDictionaries[0]["App.Accent"];
                string before = Hex(accentBrush.Color);
                editor.SelectedColor = editor.Colors.Single(c => c.Key == "Accent");
                editor.HexInput = "#12";
                Assert.NotNull(editor.HexProblem);
                Assert.Equal(before, Hex(accentBrush.Color));
                editor.HexInput = "#B03A2E";
                Assert.Null(editor.HexProblem);
                Assert.Equal("#B03A2E", Hex(accentBrush.Color));                          // the real interface brush changed
                Assert.Equal("#B03A2E", editor.SelectedColor.Hex);                        // swatch row updated
                Assert.True(editor.HasUnsavedChanges);
                Assert.NotNull(main.Pages.OfType<SettingsViewModel>().Single().UnsavedChanges); // asked about when closing

                // Sliders: hue moves the color around the wheel, keeping saturation and lightness.
                editor.Hue = 120;
                var (h, s, l) = ThemeColor.Parse(editor.HexInput).ToHsl();
                Assert.InRange(h, 118, 122);
                Assert.Equal(Hex(accentBrush.Color), editor.HexInput);
                editor.Lightness = 30;
                Assert.InRange(ThemeColor.Parse(editor.HexInput).ToHsl().L, 0.29, 0.31);

                // Unsaved: the file still has the theme without colors; Revert brings back the saved look.
                var onDisk = new SettingsStore(services.SettingsStore.FilePath).Load().Settings;
                Assert.Empty(onDisk.Themes.Custom.Single().Colors);
                editor.RevertCommand.Execute(null);
                Assert.Equal(before, Hex(accentBrush.Color));
                Assert.False(editor.HasUnsavedChanges);

                // Edit again and save.
                Assert.True(editor.TrySetColor("Accent", "#B03A2E", out _));
                Assert.True(editor.TrySetColor("WindowBackground", "#F4ECE6", out _));
                editor.SaveCommand.Execute(null);
                Assert.False(editor.HasUnsavedChanges);

                // "Restart": a new program reads the settings, restores the custom theme and its colors.
                var reloaded = new SettingsStore(services.SettingsStore.FilePath).Load().Settings;
                Assert.Equal(7, reloaded.Safety.BackupRetentionDays);
                Assert.Equal("ThemeTest", reloaded.CreatorName);
                var saved = reloaded.Themes.Find(reloaded.Themes.ActiveCustomTheme)!;
                Assert.Equal("Ruby", saved.Name);
                Assert.Equal("#B03A2E", saved.Colors["Accent"]);
                var restartHost = new ResourceDictionary();
                var restarted = new ThemeEngine(restartHost);
                restarted.Apply(saved.Base, saved);
                Assert.Equal("#F4ECE6", Hex(((SolidColorBrush)restartHost.MergedDictionaries[0]["App.WindowBackground"]).Color));

                // Readability warning for an unreadable choice.
                Assert.True(editor.TrySetColor("Text", "#F5F5F5", out _));
                Assert.True(editor.HasWarnings);
                editor.ResetColorCommand.Execute(null);                                     // the selected color (Text) back to its base value
                Assert.False(editor.HasWarnings);

                // Switching to a built-in theme with unsaved changes asks first; "Discard" leaves the saved theme intact.
                Assert.True(editor.TrySetColor("Accent", "#00AA00", out _));
                dialogs.ChooseAnswer = 1;
                editor.SelectedTheme = editor.Themes.Single(t => t.Name == "Dark");
                Assert.Single(dialogs.ChoiceQuestions);
                Assert.Null(services.Settings.Themes.ActiveCustomTheme);
                Assert.Equal(ThemePreference.Dark, services.Settings.Theme);
                Assert.Equal("#B03A2E", services.Settings.Themes.Custom.Single().Colors["Accent"]);

                // Back to Ruby, then delete it (confirmed); the built-in theme becomes active.
                editor.SelectedTheme = editor.Themes.Single(t => t.Name == "Ruby");
                Assert.Equal(services.Settings.Themes.Custom.Single().Id, services.Settings.Themes.ActiveCustomTheme);
                dialogs.ConfirmAnswer = true;
                editor.DeleteThemeCommand.Execute(null);
                Assert.Empty(services.Settings.Themes.Custom);
                Assert.Null(services.Settings.Themes.ActiveCustomTheme);
                Assert.Equal(3, editor.Themes.Count);

                // Restore original appearance: the System theme.
                editor.ResetAllCommand.Execute(null);
                Assert.Equal(ThemePreference.System, services.Settings.Theme);
                var final = new SettingsStore(services.SettingsStore.FilePath).Load().Settings;
                Assert.Equal(ThemePreference.System, final.Theme);
                Assert.Equal(7, final.Safety.BackupRetentionDays);                         // still untouched
            }
            finally
            {
                ThemeManager.UseEngineForTests(null);
            }
            return Task.CompletedTask;
        });
    }

    [Fact(Timeout = 120_000)]
    public async Task ImportedThemesAreValidatedAndExportedThemesNameTheFile()
    {
        await Sta.Run(() =>
        {
            ThemeManager.UseEngineForTests(new ThemeEngine(new ResourceDictionary()));
            try
            {
                var (services, dialogs, dir) = CreateServices();
                using var disposeServices = services;
                ThemeManager.Initialize(ThemePreference.Dark);
                var editor = new ThemeEditorViewModel(services, new MainViewModel(services));
                string file = Path.Combine(dir, "My theme ü.tftheme.json");
                File.WriteAllText(file, """{ "Format": "TibiaFoundry.Theme", "Version": 1, "Name": "Imported", "Base": "Dark", "Colors": { "Accent": "#336699", "Text": "nope" } }""");
                dialogs.NextFile = file;
                editor.ImportThemeCommand.Execute(null);
                var imported = Assert.Single(services.Settings.Themes.Custom);
                Assert.Equal("Imported", imported.Name);
                Assert.Equal(["Accent"], imported.Colors.Keys);
                Assert.Contains(dialogs.Messages, m => m.Message.Contains("Text", StringComparison.Ordinal));     // the invalid color is named

                string export = Path.Combine(dir, "export.tftheme.json");
                dialogs.NextFile = export;
                editor.ExportThemeCommand.Execute(null);
                Assert.True(File.Exists(export));
                Assert.Equal(export, services.Status.Link?.Path);
                Assert.Equal("#336699", ThemeFile.Parse(File.ReadAllText(export)).Theme.Colors["Accent"]);
            }
            finally
            {
                ThemeManager.UseEngineForTests(null);
            }
            return Task.CompletedTask;
        });
    }

    [Fact(Timeout = 120_000)]
    public async Task ResettingAllSettingsKeepsCustomThemesButActivatesTheDefaultTheme()
    {
        await Sta.Run(() =>
        {
            ThemeManager.UseEngineForTests(new ThemeEngine(new ResourceDictionary()));
            try
            {
                var (services, dialogs, _) = CreateServices();
                using var disposeServices = services;
                var theme = new CustomTheme { Id = "mine", Name = "Mine", Base = ThemeBase.Dark, Colors = new() { ["Accent"] = "#AA3311" } };
                services.Settings.Themes.Custom.Add(theme);
                services.Settings.Themes.ActiveCustomTheme = "mine";
                services.Settings.Safety.BackupRetentionDays = 5;
                var settings = new MainViewModel(services).Pages.OfType<SettingsViewModel>().Single();
                dialogs.ConfirmAnswer = true;
                settings.ResetDefaultsCommand.Execute(null);
                Assert.Equal(30, services.Settings.Safety.BackupRetentionDays);              // reset
                Assert.Equal("#AA3311", Assert.Single(services.Settings.Themes.Custom).Colors["Accent"]);   // the user's theme is kept
                Assert.Null(services.Settings.Themes.ActiveCustomTheme);                    // the default appearance is active
                Assert.Equal(ThemePreference.System, services.Settings.Theme);
            }
            finally
            {
                ThemeManager.UseEngineForTests(null);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void ThemeColorsAreNotUsedByExports()
    {
        // Exports are produced by the core library, which has no access to the interface's palette:
        // no file of the core project refers to a theme resource key.
        string core = Path.Combine(Path.GetDirectoryName(ThemesDir())!, "..", "TibiaFoundry.Core");
        var offenders = Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}Theming{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("\"App.", StringComparison.Ordinal)).ToList();
        Assert.Empty(offenders);
    }
}
