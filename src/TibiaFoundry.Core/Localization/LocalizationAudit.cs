using System.Text;
using System.Text.RegularExpressions;

namespace TibiaFoundry.Core.Localization;

/// <summary>The state of one language's table compared with the English one.</summary>
public sealed record LanguageAudit(
    LanguageDefinition Language,
    int Total,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Empty,
    IReadOnlyList<string> SameAsEnglish,
    IReadOnlyList<string> PlaceholderMismatches,
    IReadOnlyList<string> MissingPluralForms,
    IReadOnlyList<string> Extra)
{
    public int Translated => Total - Missing.Count - Empty.Count;

    public double Coverage => Total == 0 ? 1 : (double)Translated / Total;

    /// <summary>Problems that make the language incomplete (missing or empty texts, broken placeholders, missing plural forms).</summary>
    public int Problems => Missing.Count + Empty.Count + PlaceholderMismatches.Count + MissingPluralForms.Count;
}

/// <summary>
/// Checks the string tables for completeness: every English key translated, placeholders kept,
/// plural forms complete for each language's rules. Used by the tests and by
/// <c>TibiaFoundry.exe --localization-report &lt;file&gt;</c>.
/// </summary>
public static partial class LocalizationAudit
{
    /// <summary>
    /// Texts that are correctly the same in every language: names, formats and symbols of the glossary
    /// (docs/Localization.md). Matched against the whole text.
    /// </summary>
    private static readonly HashSet<string> SameEverywhere = new(StringComparer.Ordinal)
    {
        "Tibia", "Tibia Foundry", "TibiaWiki", "CipSoft", "GitHub", "OK", "ID", "JSON", "XML", "PNG", "GIF", "CSV", "Lua", "HTML",
        "RCC", "DAT", "SPR", "OTB", "SHA-256", "API", "URL", "Markdown", "ZIP", "SQLite", "ImageMagick", "NAudio", "TagLib#",
    };

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    public static IEnumerable<string> Placeholders(string text) => Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order();

    public static LanguageAudit Check(LanguageDefinition language)
    {
        var english = L.English;
        var table = L.Table(language.Code);
        var missing = new List<string>();
        var empty = new List<string>();
        var same = new List<string>();
        var mismatched = new List<string>();
        foreach (var (key, text) in english)
        {
            if (IsPluralForm(key)) continue; // checked per language below
            if (!table.TryGetValue(key, out string? translated)) { missing.Add(key); continue; }
            if (string.IsNullOrWhiteSpace(translated) && !string.IsNullOrWhiteSpace(text)) { empty.Add(key); continue; }
            if (language != Languages.English && translated == text && LooksTranslatable(text)) same.Add(key);
            if (!Placeholders(text).SequenceEqual(Placeholders(translated))) mismatched.Add(key);
        }
        var plurals = new List<string>();
        foreach (string baseKey in english.Keys.Where(IsPluralForm).Select(k => k[..k.LastIndexOf('.')]).Distinct())
        {
            foreach (string category in Categories(language.Code))
            {
                string key = baseKey + "." + category;
                if (!table.TryGetValue(key, out string? form) || string.IsNullOrWhiteSpace(form)) plurals.Add(key);
                else if (english.TryGetValue(baseKey + ".other", out string? en) && !Placeholders(en).SequenceEqual(Placeholders(form))) mismatched.Add(key);
            }
        }
        var extra = table.Keys.Where(k => !english.ContainsKey(k) && !(IsPluralForm(k) && english.ContainsKey(k[..k.LastIndexOf('.')] + ".other"))).ToList();
        int total = english.Keys.Count(k => !IsPluralForm(k)) + english.Keys.Where(IsPluralForm).Select(k => k[..k.LastIndexOf('.')]).Distinct().Count() * Categories(language.Code).Count;
        return new LanguageAudit(language, total, missing, empty, same, mismatched, plurals, extra);
    }

    /// <summary>The plural categories a language needs.</summary>
    public static IReadOnlyList<string> Categories(string code) => code == "pl" ? ["one", "few", "many"] : ["one", "other"];

    public static bool IsPluralForm(string key) =>
        key.EndsWith(".one", StringComparison.Ordinal) || key.EndsWith(".few", StringComparison.Ordinal) ||
        key.EndsWith(".many", StringComparison.Ordinal) || key.EndsWith(".other", StringComparison.Ordinal);

    private static bool LooksTranslatable(string text) =>
        !SameEverywhere.Contains(text.Trim()) && text.Count(char.IsLetter) >= 4 && !text.Contains("://", StringComparison.Ordinal)
        && !Regex.IsMatch(text, @"^[\p{Lu}\d\W]+$") && !Regex.IsMatch(text, @"^\{\d+\}");

    /// <summary>The report as Markdown.</summary>
    public static string Report(IEnumerable<KeyValuePair<string, string>> dataKeysWithoutEntry)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Tibia Foundry translation completeness report");
        sb.AppendLine();
        sb.AppendLine($"English keys: {L.English.Count:N0}. English is the authoritative text and the fallback: a missing translation shows the English text, never an empty element.");
        sb.AppendLine();
        sb.AppendLine("| Language | Code | Translated | Coverage | Missing | Empty | Placeholder problems | Missing plural forms | Same as English (review) |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");
        var audits = Languages.All.Select(Check).ToList();
        foreach (var a in audits)
            sb.AppendLine($"| {a.Language.EnglishName} | {a.Language.Code} | {a.Translated:N0} / {a.Total:N0} | {a.Coverage:P1} | {a.Missing.Count} | {a.Empty.Count} | {a.PlaceholderMismatches.Count} | {a.MissingPluralForms.Count} | {a.SameAsEnglish.Count} |");
        var data = dataKeysWithoutEntry.ToList();
        sb.AppendLine();
        sb.AppendLine($"Data-driven texts (tool manifests) without an entry in the English table: {data.Count}.");
        foreach (var (key, text) in data) sb.AppendLine($"- `{key}`: {text}");
        foreach (var a in audits.Where(a => a.Language != Languages.English))
        {
            sb.AppendLine();
            sb.AppendLine($"## {a.Language.EnglishName} ({a.Language.Code})");
            List("Missing", a.Missing);
            List("Empty", a.Empty);
            List("Placeholders differ from English", a.PlaceholderMismatches);
            List("Missing plural forms", a.MissingPluralForms);
            List("Not in the English table (unused)", a.Extra);
            List("Same as English (fine for names and formats; review the rest)", a.SameAsEnglish);
        }
        return sb.ToString();

        void List(string title, IReadOnlyList<string> keys)
        {
            if (keys.Count == 0) return;
            sb.AppendLine();
            sb.AppendLine($"### {title} ({keys.Count})");
            foreach (string key in keys.Take(500)) sb.AppendLine($"- `{key}`: {(L.English.TryGetValue(key, out string? en) ? en.ReplaceLineEndings(" ") : "")}");
            if (keys.Count > 500) sb.AppendLine($"- … and {keys.Count - 500} more");
        }
    }
}
