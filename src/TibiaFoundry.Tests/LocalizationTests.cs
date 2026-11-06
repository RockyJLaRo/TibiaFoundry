using System.Text.RegularExpressions;
using TibiaFoundry.Core.Localization;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.Tests;

/// <summary>
/// The translated interface: every language's table is complete and keeps the English
/// placeholders, every key the views and code ask for exists, plural rules, the language setting,
/// and switching the language at run time.
/// </summary>
public sealed partial class LocalizationTests
{
    private static string SourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TibiaFoundry.App", "App.xaml"))) return dir.FullName;
            if (File.Exists(Path.Combine(dir.FullName, "src", "TibiaFoundry.App", "App.xaml"))) return Path.Combine(dir.FullName, "src");
            dir = dir.Parent;
        }
        Assert.Skip("The sources were not found next to the test binaries.");
        return "";
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        new[] { "TibiaFoundry.App", "TibiaFoundry.Core" }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(SourceDir(), p), pattern, SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    public static TheoryData<string> TranslatedLanguages() => new(Languages.All.Where(l => l != Languages.English).Select(l => l.Code));

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void EveryLanguageIsCompleteWithMatchingPlaceholders(string code)
    {
        var audit = LocalizationAudit.Check(Languages.Find(code));
        Assert.Empty(audit.Missing);
        Assert.Empty(audit.Empty);
        Assert.Empty(audit.PlaceholderMismatches);
        Assert.Empty(audit.MissingPluralForms);
        Assert.Empty(audit.Extra);
    }

    /// <summary>Texts joined to others ("Saved to " + path) keep the English leading and trailing spaces.</summary>
    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void LeadingAndTrailingSpacesMatchTheEnglish(string code)
    {
        static string Edges(string s) => s[..(s.Length - s.TrimStart(' ').Length)] + "|" + s[s.TrimEnd(' ').Length..];
        var table = L.Table(code);
        var different = L.English.Where(e => table.TryGetValue(e.Key, out string? t) && Edges(t) != Edges(e.Value)).Select(e => e.Key).ToList();
        Assert.Empty(different);
    }

    [Fact]
    public void TheEnglishTableHasNoEmptyTexts()
    {
        Assert.NotEmpty(L.English);
        Assert.DoesNotContain(L.English, e => string.IsNullOrWhiteSpace(e.Value));
    }

    [GeneratedRegex(@"\{loc:Tr\s+(?:Key=)?([\w.\-]+)\s*\}")]
    private static partial Regex XamlKey();

    [Fact]
    public void EveryKeyUsedInXamlExists()
    {
        var missing = SourceFiles("*.xaml")
            .SelectMany(f => XamlKey().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .Where(r => !L.English.ContainsKey(r.Key))
            .Select(r => $"{r.File}: {r.Key}").Distinct().ToList();
        Assert.Empty(missing);
    }

    [GeneratedRegex(@"\bL\.(T|F|Count)\((?:[^,""()]+,\s*)?""([A-Za-z][\w.\-]*)""\s*[,)]")]
    private static partial Regex CodeKey();

    [Fact]
    public void EveryKeyUsedInCodeExists()
    {
        var used = SourceFiles("*.cs")
            .SelectMany(f => CodeKey().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Kind: m.Groups[1].Value, Key: m.Groups[2].Value)))
            .ToList();
        Assert.True(used.Count > 1000, $"only {used.Count} lookups found; the pattern no longer matches the code");
        var missing = used
            .Where(u => u.Kind == "Count" ? !L.English.ContainsKey(u.Key + ".other") : !L.English.ContainsKey(u.Key))
            .Select(u => $"{u.File}: {u.Key}").Distinct().ToList();
        Assert.Empty(missing);
    }

    /// <summary>Names that are never translated (glossary): where the English text has one, every translation keeps it.</summary>
    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void ProtectedNamesAreKept(string code)
    {
        string[] names = ["Tibia Foundry", "TibiaWiki", "CipSoft", "GitHub", "SHA-256"];
        var table = L.Table(code);
        var changed = L.English
            .SelectMany(e => names.Where(n => e.Value.Contains(n, StringComparison.Ordinal)).Select(n => (e.Key, Name: n)))
            .Where(p => table.TryGetValue(p.Key, out string? t) && !t.Contains(p.Name, StringComparison.Ordinal))
            .Select(p => $"{p.Key}: {p.Name}").ToList();
        Assert.Empty(changed);
    }

    [Theory]
    [InlineData("en", 0, "other"), InlineData("en", 1, "one"), InlineData("en", 2, "other")]
    [InlineData("de", 1, "one"), InlineData("de", 7, "other"), InlineData("es", 1, "one"), InlineData("es", 0, "other")]
    [InlineData("pt-BR", 0, "one"), InlineData("pt-BR", 1, "one"), InlineData("pt-BR", 2, "other")]
    [InlineData("pl", 1, "one"), InlineData("pl", 2, "few"), InlineData("pl", 4, "few"), InlineData("pl", 5, "many")]
    [InlineData("pl", 12, "many"), InlineData("pl", 14, "many"), InlineData("pl", 22, "few"), InlineData("pl", 0, "many"), InlineData("pl", 111, "many")]
    public void PluralCategoriesFollowTheLanguageRules(string code, long n, string category) =>
        Assert.Equal(category, L.PluralCategory(code, n));

    [Theory]
    [InlineData(null, "en"), InlineData("", "en"), InlineData("fr", "en"), InlineData("pt", "en")]
    [InlineData("de", "de"), InlineData("PT-br", "pt-BR"), InlineData(" pl ", "pl"), InlineData("es", "es")]
    public void UnknownLanguagesFallBackToEnglish(string? code, string expected) =>
        Assert.Equal(expected, Languages.Find(code).Code);

    [Fact]
    public void SpanishUsesLatinAmericanFormatting() => Assert.Equal("es-419", Languages.Find("es").CultureName);

    [Fact]
    public void TheLanguageIsSavedAndInvalidValuesBecomeEnglish()
    {
        string file = Path.Combine(TestData.NewTempDirectory(), "settings.json");
        var store = new SettingsStore(file);
        store.Save(new AppSettings { Language = "pl" });
        Assert.Equal("pl", store.Load().Settings.Language);

        store.Save(new AppSettings { Language = "xx-YY" });
        Assert.Equal(Languages.DefaultCode, store.Load().Settings.Language);

        Assert.Equal(Languages.DefaultCode, new SettingsStore(Path.Combine(TestData.NewTempDirectory(), "none.json")).Load().Settings.Language);
    }
}

/// <summary>Switching the language changes global state, so these tests do not run alongside others.</summary>
[Collection(nameof(LanguageSwitchCollection))]
public sealed class LanguageSwitchTests
{
    [Fact]
    public void SwitchingTheLanguageChangesTextNumbersAndPlurals()
    {
        int raised = 0;
        void OnChanged() => raised++;
        L.LanguageChanged += OnChanged;
        try
        {
            Assert.Equal("de", L.SetLanguage("de").Code);
            Assert.Equal(1, raised);
            Assert.Equal("Abbrechen", L.T("Common.Cancel"));
            Assert.Equal("1 Datei", L.Count(1, "Noun.file"));
            Assert.Equal("1.234 Dateien", L.Count(1234, "Noun.file"));

            L.SetLanguage("pl");
            Assert.Equal("2 pliki", L.Count(2, "Noun.file"));
            Assert.Equal("5 plików", L.Count(5, "Noun.file"));

            L.SetLanguage("pt-BR");
            Assert.Equal("0 arquivo", L.Count(0, "Noun.file"));

            // Unknown codes select English; a key no table has shows the key, never an empty text.
            Assert.Equal("en", L.SetLanguage("zz").Code);
            Assert.Equal("Cancel", L.T("Common.Cancel"));
            Assert.Equal("No.Such.Key", L.T("No.Such.Key"));
            Assert.Contains("No.Such.Key", L.MissingKeys);
        }
        finally
        {
            L.LanguageChanged -= OnChanged;
            L.SetLanguage(Languages.DefaultCode);
        }
    }
}

[CollectionDefinition(nameof(LanguageSwitchCollection), DisableParallelization = true)]
public sealed class LanguageSwitchCollection;
