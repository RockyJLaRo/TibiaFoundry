using System.Globalization;

namespace TibiaFoundry.Core.Localization;

/// <summary>A display language of the interface.</summary>
/// <param name="Code">The code saved in the settings and used in the resource file name (Strings.{Code}.xaml).</param>
/// <param name="NativeName">The language's name in that language, as the language menu shows it.</param>
/// <param name="Flag">A flag emoji shown next to the name (never the only identification).</param>
/// <param name="ShortLabel">The two letters shown on the language button.</param>
/// <param name="CultureName">The culture used for numbers, dates and plural rules.</param>
public sealed record LanguageDefinition(string Code, string NativeName, string EnglishName, string Flag, string ShortLabel, string CultureName)
{
    public CultureInfo Culture => CultureInfo.GetCultureInfo(CultureName);
}

/// <summary>The languages Tibia Foundry offers. English is the default and the fallback for missing translations.</summary>
public static class Languages
{
    public const string DefaultCode = "en";

    public static LanguageDefinition English { get; } = new("en", "English", "English", "🇬🇧", "EN", "en-US");

    public static IReadOnlyList<LanguageDefinition> All { get; } =
    [
        English,
        new("pt-BR", "Português (Brasil)", "Brazilian Portuguese", "🇧🇷", "PT", "pt-BR"),
        new("pl", "Polski", "Polish", "🇵🇱", "PL", "pl-PL"),
        new("es", "Español", "Spanish", "🇪🇸", "ES", "es-419"),
        new("de", "Deutsch", "German", "🇩🇪", "DE", "de-DE"),
    ];

    /// <summary>The language with this code (case-insensitive), or English when the code is unknown or empty.</summary>
    public static LanguageDefinition Find(string? code) =>
        All.FirstOrDefault(l => string.Equals(l.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? English;
}
