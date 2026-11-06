using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using TibiaFoundry.Core.Diagnostics;

namespace TibiaFoundry.Core.Localization;

/// <summary>
/// The translated interface text. Every user-facing string is looked up by key in the string
/// table of the selected language (Localization/Resources/Strings.{code}.xaml, embedded in this
/// assembly); English is the authoritative fallback, so a missing translation shows the English text,
/// never an empty element. Missing keys are logged once and collected in <see cref="MissingKeys"/>.
/// </summary>
/// <remarks>
/// The same files are WPF resource dictionaries: the application merges the active table into its
/// resources, so XAML uses <c>{DynamicResource Key}</c> and switches language without a restart.
/// Keys are internal identifiers and never change with the language; program logic never compares
/// translated text.
/// <para>Plural forms are separate keys with the CLDR category as suffix (<c>Key.one</c>,
/// <c>Key.few</c>, <c>Key.many</c>, <c>Key.other</c>); see <see cref="Count"/>.</para>
/// </remarks>
public static class L
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Tables = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> Missing = new(StringComparer.Ordinal);
    private static volatile IReadOnlyDictionary<string, string>? _current;

    /// <summary>The active language.</summary>
    public static LanguageDefinition Language { get; private set; } = Languages.English;

    /// <summary>The culture of the active language (numbers and dates in translated text).</summary>
    public static CultureInfo Culture => Language.Culture;

    /// <summary>Raised after <see cref="SetLanguage"/> changed the language.</summary>
    public static event Action? LanguageChanged;

    /// <summary>Keys that were requested but exist in no table (not even English), for the completeness report.</summary>
    public static IReadOnlyCollection<string> MissingKeys => Missing.Keys.ToList();

    /// <summary>The English table (the fallback and the list of every key).</summary>
    public static IReadOnlyDictionary<string, string> English => Table(Languages.DefaultCode);

    private static IReadOnlyDictionary<string, string> Current => _current ??= English;

    /// <summary>Switches the language (unknown codes select English). Returns the language now in effect.</summary>
    public static LanguageDefinition SetLanguage(string? code)
    {
        var language = Languages.Find(code);
        bool changed = language != Language;
        _current = language == Languages.English ? English : Merge(English, Table(language.Code));
        Language = language;
        if (changed) LanguageChanged?.Invoke();
        return language;
    }

    /// <summary>The text of a key in the active language (English when not translated).</summary>
    public static string T(string key)
    {
        if (Current.TryGetValue(key, out string? text)) return text;
        if (Missing.TryAdd(key, 0)) AppLog.Warn($"Localization: no text for the key \"{key}\".", source: "Localization");
        return key;
    }

    /// <summary>
    /// The text of a data-driven key (a tool's name, a setting's label…), or <paramref name="english"/>
    /// when the table has no entry: the English text written in the data is the fallback. The pair is
    /// recorded in <see cref="DataKeys"/> for the completeness report.
    /// </summary>
    public static string TOr(string key, string english)
    {
        DataTexts.TryAdd(key, english);
        return Current.TryGetValue(key, out string? text) ? text : english;
    }

    private static readonly ConcurrentDictionary<string, string> DataTexts = new(StringComparer.Ordinal);

    /// <summary>Data-driven keys requested so far, with their English text.</summary>
    public static IReadOnlyDictionary<string, string> DataKeys => DataTexts;

    /// <summary>True when the active table (with fallback) has the key.</summary>
    public static bool Has(string key) => Current.ContainsKey(key);

    /// <summary>The text of a key with <see cref="string.Format(IFormatProvider, string, object[])"/> placeholders filled in the active culture.</summary>
    public static string F(string key, params object?[] args)
    {
        string format = T(key);
        try
        {
            return string.Format(Culture, format, args);
        }
        catch (FormatException)
        {
            AppLog.Warn($"Localization: the text of \"{key}\" in {Language.Code} has invalid placeholders.", source: "Localization");
            return string.Format(Culture, English.TryGetValue(key, out string? en) ? en : key, args);
        }
    }

    /// <summary>
    /// A counted phrase: picks the plural form of <paramref name="key"/> for <paramref name="n"/>
    /// (<c>key.one</c>, <c>key.few</c>, <c>key.many</c> or <c>key.other</c>, by the rules of the active
    /// language) and fills <c>{0}</c> with the formatted number and <c>{1}</c>… with <paramref name="args"/>.
    /// </summary>
    public static string Count(long n, string key, params object?[] args)
    {
        string category = PluralCategory(Language.Code, n);
        string form = key + "." + category;
        if (!Current.ContainsKey(form)) form = Current.ContainsKey(key + ".other") ? key + ".other" : key + ".one";
        var all = new object?[args.Length + 1];
        all[0] = n.ToString("N0", Culture);
        args.CopyTo(all, 1);
        return F(form, all);
    }

    /// <summary>The CLDR plural category of an integer in a language.</summary>
    public static string PluralCategory(string languageCode, long n)
    {
        long abs = Math.Abs(n);
        switch (languageCode)
        {
            case "pl":
                if (abs == 1) return "one";
                long mod10 = abs % 10, mod100 = abs % 100;
                return mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? "few" : "many";
            case "pt-BR":
                return abs is 0 or 1 ? "one" : "other";
            default:
                return abs == 1 ? "one" : "other";
        }
    }

    /// <summary>The string table of one language as written in its file (no fallback), or empty when the file does not exist.</summary>
    public static IReadOnlyDictionary<string, string> Table(string code) => Tables.GetOrAdd(code, Load);

    /// <summary>The file name of a language's table inside the assembly.</summary>
    public static string ResourceName(string code) => $"TibiaFoundry.Core.Localization.Strings.{code}.xaml";

    private static Dictionary<string, string> Load(string code)
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream(ResourceName(code));
        if (stream == null) return new Dictionary<string, string>(StringComparer.Ordinal);
        return Parse(XDocument.Load(stream, LoadOptions.PreserveWhitespace));
    }

    /// <summary>Reads a string table (a XAML resource dictionary of <c>sys:String</c> entries).</summary>
    public static Dictionary<string, string> Parse(XDocument document)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in document.Root!.Elements())
        {
            if (element.Attribute(X + "Key")?.Value is not { } key) continue;
            table[key] = element.Value;
        }
        return table;
    }

    private static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> fallback, IReadOnlyDictionary<string, string> language)
    {
        var merged = new Dictionary<string, string>(fallback, StringComparer.Ordinal);
        foreach (var (key, text) in language)
            if (!string.IsNullOrEmpty(text)) merged[key] = text;
        return merged;
    }

    /// <summary>The active table with the English fallback applied (what the interface shows).</summary>
    public static IReadOnlyDictionary<string, string> Effective => Current;

    /// <summary>The assembly that holds the string tables.</summary>
    public static Assembly ResourceAssembly => typeof(L).Assembly;
}
