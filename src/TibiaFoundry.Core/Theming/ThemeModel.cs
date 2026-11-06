using System.Text.Json;
using System.Text.Json.Serialization;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Theming;

/// <summary>The two built-in palettes a custom theme can start from.</summary>
public enum ThemeBase { Light, Dark }

/// <summary>
/// A theme the user made: a built-in palette (<see cref="Base"/>) plus the colors the user changed.
/// Only changed colors are stored, so a custom theme keeps following improvements of its base.
/// </summary>
public sealed class CustomTheme
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ThemeBase Base { get; set; } = ThemeBase.Light;

    /// <summary>Changed colors: token key (e.g. "Accent") → "#RRGGBB".</summary>
    public Dictionary<string, string> Colors { get; set; } = new(StringComparer.Ordinal);

    public DateTime ModifiedUtc { get; set; }

    public CustomTheme Clone() => new()
    {
        Id = Id, Name = Name, Base = Base, ModifiedUtc = ModifiedUtc,
        Colors = new Dictionary<string, string>(Colors, StringComparer.Ordinal),
    };

    /// <summary>The changed colors that are valid tokens with valid values (others are ignored).</summary>
    public IReadOnlyDictionary<string, ThemeColor> ValidOverrides()
    {
        var result = new Dictionary<string, ThemeColor>(StringComparer.Ordinal);
        foreach (var (key, value) in Colors)
            if (ThemeTokens.IsToken(key) && ThemeColor.TryParse(value, out var c)) result[key] = c;
        return result;
    }
}

/// <summary>Settings → Appearance &amp; Themes: custom themes and which one is active.</summary>
public sealed class ThemeSettings
{
    public const int MaxNameLength = 40;
    public const int MaxCustomThemes = 50;

    /// <summary>Id of the active custom theme; null when a built-in theme (<c>AppSettings.Theme</c>) is active.</summary>
    public string? ActiveCustomTheme { get; set; }

    public List<CustomTheme> Custom { get; set; } = [];

    public CustomTheme? Find(string? id) => id == null ? null : Custom.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Repairs what was loaded from disk: invalid or duplicate entries and colors that are not
    /// valid are dropped (and logged), names are made unique, and an active theme that no longer
    /// exists falls back to the built-in theme. Colors of tokens this version does not know (written
    /// by a newer version) are kept unchanged.
    /// </summary>
    public void Normalize()
    {
        Custom ??= [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<CustomTheme>();
        foreach (var theme in Custom)
        {
            if (theme == null) continue;
            if (string.IsNullOrWhiteSpace(theme.Id) || !ids.Add(theme.Id)) theme.Id = NewId(ids);
            if (!Enum.IsDefined(theme.Base)) theme.Base = ThemeBase.Light;
            theme.Name = UniqueName(CleanName(theme.Name), names);
            names.Add(theme.Name);
            theme.Colors ??= new(StringComparer.Ordinal);
            foreach (var (key, value) in theme.Colors.ToList())
            {
                if (!ThemeTokens.IsToken(key)) continue; // a newer version's color: kept as it is
                if (ThemeColor.TryParse(value, out var color)) theme.Colors[key] = color.Hex;
                else
                {
                    theme.Colors.Remove(key);
                    AppLog.Warn($"Theme \"{theme.Name}\": the color of {key} (\"{value}\") is not valid and was ignored.");
                }
            }
            kept.Add(theme);
            if (kept.Count == MaxCustomThemes) break;
        }
        Custom = kept;
        if (ActiveCustomTheme != null && Find(ActiveCustomTheme) == null) ActiveCustomTheme = null;
    }

    public static string CleanName(string? name)
    {
        string n = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        n = new string(n.Where(c => !char.IsControl(c)).ToArray());
        if (n.Length > MaxNameLength) n = n[..MaxNameLength].TrimEnd();
        return n.Length == 0 ? L.T("ThemeModel.CustomTheme") : n;
    }

    /// <summary><paramref name="name"/>, or "name (2)", "name (3)" … when it is already used.</summary>
    public static string UniqueName(string name, ICollection<string> taken)
    {
        bool Taken(string n) => taken.Any(t => string.Equals(t, n, StringComparison.OrdinalIgnoreCase));
        if (!Taken(name)) return name;
        for (int i = 2; ; i++)
        {
            string suffix = $" ({i})";
            string candidate = (name.Length + suffix.Length > MaxNameLength ? name[..(MaxNameLength - suffix.Length)] : name) + suffix;
            if (!Taken(candidate)) return candidate;
        }
    }

    public static string NewId(ICollection<string>? taken = null)
    {
        string id;
        do id = "custom-" + Guid.NewGuid().ToString("N")[..12]; while (taken?.Contains(id) == true);
        return id;
    }
}

/// <summary>
/// Resolves a theme into a full palette: the built-in base palette, the user's changed colors, and
/// derived colors recomputed only where a color they are made from was changed. A theme without
/// changes therefore reproduces its built-in palette exactly.
/// </summary>
public static class ThemeResolver
{
    public static Dictionary<string, ThemeColor> Resolve(IReadOnlyDictionary<string, ThemeColor> basePalette, IReadOnlyDictionary<string, ThemeColor> overrides)
    {
        var resolved = new Dictionary<string, ThemeColor>(basePalette, StringComparer.Ordinal);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in ThemeTokens.All)
        {
            if (!basePalette.TryGetValue(token.Key, out var original)) throw new InvalidOperationException($"The base palette has no color for {token.Key}.");
            ThemeColor value;
            if (overrides.TryGetValue(token.Key, out var custom)) value = custom;
            else if (token.Derive != null && token.Sources.Any(changed.Contains)) value = token.Derive(resolved);
            else continue;
            resolved[token.Key] = value;
            if (value != original) changed.Add(token.Key);
        }
        return resolved;
    }

    /// <summary>The tokens whose resolved value differs from the base palette.</summary>
    public static IReadOnlyList<string> Differences(IReadOnlyDictionary<string, ThemeColor> basePalette, IReadOnlyDictionary<string, ThemeColor> resolved) =>
        ThemeTokens.All.Where(t => resolved[t.Key] != basePalette[t.Key]).Select(t => t.Key).ToList();
}

/// <summary>A readability or state-visibility problem of a palette.</summary>
public sealed record ThemeWarning(string Message, string Foreground, string Background, double Ratio, double Required);

/// <summary>
/// Accessibility checks of a palette (WCAG 2.1 contrast): body text needs 4.5:1, secondary text
/// and controls 3:1, and hover, selection and focus must remain distinguishable from the surfaces
/// they appear on.
/// </summary>
public static class ThemeAccessibility
{
    private static readonly (string Fg, string Bg, double Min, string What)[] Pairs =
    [
        ("Text", "WindowBackground", 4.5, L.T("ThemeModel.PrimaryTextOnTheMainBackground")),
        ("Text", "Surface", 4.5, L.T("ThemeModel.PrimaryTextOnPanels")),
        ("Text", "SurfaceAlt", 4.5, L.T("ThemeModel.PrimaryTextOnSecondaryPanels")),
        ("Text", "NavBackground", 4.5, L.T("ThemeModel.TextInTheNavigationSidebar")),
        ("Text", "HeaderBackground", 4.5, L.T("ThemeModel.TextInTheHeaderAndStatus")),
        ("TextSecondary", "Surface", 4.5, L.T("ThemeModel.SecondaryTextOnPanels")),
        ("TextSecondary", "WindowBackground", 3.0, L.T("ThemeModel.SecondaryTextOnTheMainBackground")),
        ("TextSecondary", "NavBackground", 3.0, L.T("ThemeModel.GroupNamesInTheNavigationSidebar")),
        ("Link", "Surface", 4.5, L.T("ThemeModel.LinksOnPanels")),
        ("Link", "HeaderBackground", 4.5, L.T("ThemeModel.SavedFileLinksInTheStatus")),
        ("AccentText", "Accent", 4.5, L.T("ThemeModel.TextOnAccentButtons")),
        ("ButtonText", "ButtonBackground", 4.5, L.T("ThemeModel.ButtonText")),
        ("InputText", "InputBackground", 4.5, L.T("ThemeModel.TextInInputFields")),
        ("SelectionText", "Selection", 4.5, L.T("ThemeModel.TextOfSelectedItems")),
        ("Text", "NavItemSelected", 4.5, L.T("ThemeModel.TheOpenPageInTheNavigation")),
        ("ToolTipText", "ToolTipBackground", 4.5, L.T("ThemeModel.TooltipText")),
        ("Success", "Surface", 3.0, L.T("ThemeModel.SuccessIndicators")),
        ("Warning", "Surface", 3.0, L.T("ThemeModel.WarningIndicators")),
        ("Danger", "Surface", 3.0, L.T("ThemeModel.ErrorIndicators")),
        ("Focus", "Surface", 3.0, L.T("ThemeModel.KeyboardFocusOutline")),
        ("InputBorder", "InputBackground", 1.5, L.T("ThemeModel.OutlineOfInputFields")),
    ];

    /// <summary>States that must be visible against the surface they appear on (minimum contrast 1.1:1).</summary>
    private static readonly (string State, string Bg, string What)[] States =
    [
        ("Hover", "Surface", L.T("ThemeModel.HoverHighlight")),
        ("Selection", "Surface", L.T("ThemeModel.SelectedItems")),
        ("NavItemHover", "NavBackground", L.T("ThemeModel.NavigationHover")),
        ("NavItemSelected", "NavBackground", L.T("ThemeModel.SelectedNavigationEntry")),
    ];

    public static IReadOnlyList<ThemeWarning> Check(IReadOnlyDictionary<string, ThemeColor> palette)
    {
        var warnings = new List<ThemeWarning>();
        foreach (var (fg, bg, min, what) in Pairs)
        {
            double ratio = ThemeColor.Contrast(palette[fg], palette[bg]);
            if (ratio < min - 0.005) warnings.Add(new ThemeWarning(L.F("ThemeModel.0Contrast1001", what, ratio, min), fg, bg, ratio, min));
        }
        foreach (var (state, bg, what) in States)
        {
            double ratio = ThemeColor.Contrast(palette[state], palette[bg]);
            if (ratio < 1.1) warnings.Add(new ThemeWarning(L.F("ThemeModel.0CanHardlyBeToldApart", what, ratio), state, bg, ratio, 1.1));
        }
        double disabled = ThemeColor.Contrast(palette["TextDisabled"], palette["Text"]);
        if (disabled < 1.3) warnings.Add(new ThemeWarning(L.F("ThemeModel.DisabledTextLooksAlmostLikeNormal", disabled), "TextDisabled", "Text", disabled, 1.3));
        return warnings;
    }
}

/// <summary>Reads and writes a custom theme as a small JSON file (Export / Import theme).</summary>
public static class ThemeFile
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    private sealed class FileModel
    {
        /// <summary>Must be <see cref="FormatName"/>; missing in files that are not themes.</summary>
        public string Format { get; set; } = "";
        public int Version { get; set; } = 1;
        public string Name { get; set; } = "";
        public ThemeBase Base { get; set; }
        public Dictionary<string, string> Colors { get; set; } = new(StringComparer.Ordinal);
    }

    public const string FormatName = "TibiaFoundry.Theme";

    public static string Serialize(CustomTheme theme) => JsonSerializer.Serialize(new FileModel
    {
        Format = FormatName,
        Name = theme.Name, Base = theme.Base, Colors = new Dictionary<string, string>(theme.Colors.OrderBy(kv => kv.Key, StringComparer.Ordinal), StringComparer.Ordinal),
    }, Json);

    /// <summary>
    /// Parses an exported theme. Every color is validated; the result lists the colors that were
    /// ignored because their name or value is not valid. Throws <see cref="InvalidDataException"/>
    /// when the file is not a theme at all.
    /// </summary>
    public static (CustomTheme Theme, IReadOnlyList<string> Ignored) Parse(string json)
    {
        FileModel model;
        try
        {
            model = JsonSerializer.Deserialize<FileModel>(json, Json) ?? throw new InvalidDataException("The file is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The file is not a Tibia Foundry theme: " + ex.Message, ex);
        }
        if (!string.Equals(model.Format, FormatName, StringComparison.Ordinal)) throw new InvalidDataException("The file is not a Tibia Foundry theme.");
        if (!Enum.IsDefined(model.Base)) throw new InvalidDataException("The theme's base palette is not Light or Dark.");
        var theme = new CustomTheme { Id = ThemeSettings.NewId(), Name = ThemeSettings.CleanName(model.Name), Base = model.Base, ModifiedUtc = DateTime.UtcNow };
        var ignored = new List<string>();
        foreach (var (key, value) in model.Colors ?? [])
        {
            if (!ThemeTokens.IsToken(key)) ignored.Add(L.F("ThemeModel.0NotAColorThisVersion", key));
            else if (!ThemeColor.TryParse(value, out var color, out string? problem)) ignored.Add($"{key}: {problem}");
            else theme.Colors[key] = color.Hex;
        }
        return (theme, ignored);
    }
}
