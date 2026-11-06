using TibiaFoundry.Core.Localization;
namespace TibiaFoundry.Core.Theming;

/// <summary>Groups of colors in Settings → Appearance &amp; Themes.</summary>
public enum ThemeTokenGroup { Backgrounds, Text, AccentAndButtons, States, Borders, Inputs, Status, Tooltips, Other }

/// <summary>
/// One named interface color (a "token"). Every control style uses tokens through the resource
/// key <c>App.&lt;Key&gt;</c>, so changing a token restyles the whole interface. Tokens with a
/// <see cref="Derive"/> rule follow the colors they are made from when those are customized,
/// unless they are customized themselves.
/// </summary>
public sealed record ThemeToken(string Key, string Label, ThemeTokenGroup Group, string Description)
{
    /// <summary>Shown in the editor's main list (the others appear with "Show all colors").</summary>
    public bool Primary { get; init; }

    /// <summary>Tokens this one is derived from (empty for root colors).</summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>Computes this token from the resolved palette (only used when a source was customized).</summary>
    public Func<IReadOnlyDictionary<string, ThemeColor>, ThemeColor>? Derive { get; init; }

    /// <summary>The resource key used by the interface styles.</summary>
    public string ResourceKey => "App." + Key;
}

/// <summary>
/// The customizable interface colors, in dependency order (a token's sources come before it).
/// The built-in light and dark values live in Themes/Colors.Light.xaml and Colors.Dark.xaml; a test
/// checks that both files define exactly these keys.
/// </summary>
public static class ThemeTokens
{
    private static ThemeToken Root(string key, string label, ThemeTokenGroup group, string description, bool primary = true) =>
        new(key, label, group, description) { Primary = primary };

    private static ThemeToken Derived(string key, string label, ThemeTokenGroup group, string description, bool primary,
        string[] sources, Func<IReadOnlyDictionary<string, ThemeColor>, ThemeColor> derive) =>
        new(key, label, group, description) { Primary = primary, Sources = sources, Derive = derive };

    private static ThemeColor Mix(IReadOnlyDictionary<string, ThemeColor> p, string a, string b, double amount) => p[a].Mix(p[b], amount);

    /// <summary>A text color for <paramref name="background"/>: <paramref name="preferred"/> when it is readable (4.5:1), otherwise black or white.</summary>
    private static ThemeColor Readable(ThemeColor preferred, ThemeColor background) =>
        ThemeColor.Contrast(preferred, background) >= 4.5 ? preferred : background.BestTextColor;

    /// <summary><paramref name="color"/> moved toward <paramref name="text"/> until it reaches <paramref name="ratio"/> against <paramref name="background"/>.</summary>
    private static ThemeColor Ensure(ThemeColor color, ThemeColor background, ThemeColor text, double ratio)
    {
        for (double t = 0; t <= 1.0001; t += 0.05)
        {
            var candidate = color.Mix(text, t);
            if (ThemeColor.Contrast(candidate, background) >= ratio) return candidate;
        }
        return text;
    }

    public static IReadOnlyList<ThemeToken> All { get; } =
    [
        // Roots: everything else can be derived from these.
        Root("WindowBackground", L.T("ThemeTokens.MainBackground"), ThemeTokenGroup.Backgrounds, L.T("ThemeTokens.BehindThePagesCardsAndPanels")),
        Root("Surface", L.T("ThemeTokens.PanelsAndCards"), ThemeTokenGroup.Backgrounds, L.T("ThemeTokens.CardsPanelsDialogsAndLists")),
        Root("Text", L.T("ThemeTokens.PrimaryText"), ThemeTokenGroup.Text, L.T("ThemeTokens.MostText")),
        Root("Accent", "Accent", ThemeTokenGroup.AccentAndButtons, L.T("ThemeTokens.MainButtonsCheckMarksProgressAnd")),
        Root("Success", "Success", ThemeTokenGroup.Status, L.T("ThemeTokens.CompletedOperationsAndValidValues")),
        Root("Warning", "Warning", ThemeTokenGroup.Status, L.T("ThemeTokens.WarningsAndThingsToCheck")),
        Root("Danger", "Error", ThemeTokenGroup.Status, L.T("ThemeTokens.ErrorsAndDestructiveActions")),
        Root("ToolTipBackground", L.T("ThemeTokens.TooltipBackground"), ThemeTokenGroup.Tooltips, "Tooltips"),
        Root("ToolTipText", L.T("ThemeTokens.TooltipText"), ThemeTokenGroup.Tooltips, L.T("ThemeTokens.TextOfTooltips")),

        // Backgrounds
        Derived("SurfaceAlt", L.T("ThemeTokens.SecondaryPanels"), ThemeTokenGroup.Backgrounds, L.T("ThemeTokens.AlternatingRowsAndSecondaryAreas"), true,
            ["Surface", "Text"], p => Mix(p, "Surface", "Text", 0.04)),
        Derived("NavBackground", L.T("ThemeTokens.NavigationSidebar"), ThemeTokenGroup.Backgrounds, L.T("ThemeTokens.TheNavigationBarOnTheLeft"), true,
            ["WindowBackground", "Text"], p => Mix(p, "WindowBackground", "Text", 0.05)),
        Derived("HeaderBackground", L.T("ThemeTokens.HeaderAndStatusBar"), ThemeTokenGroup.Backgrounds, L.T("ThemeTokens.PageHeaderAndStatusBar"), true,
            ["Surface", "WindowBackground"], p => Mix(p, "Surface", "WindowBackground", 0.25)),
        Derived("CardHeader", L.T("ThemeTokens.CardTitles"), ThemeTokenGroup.Backgrounds, L.T("ThemeTokens.TitleBandsOfCards"), false,
            ["Surface", "WindowBackground"], p => Mix(p, "Surface", "WindowBackground", 0.6)),
        Derived("SurfaceSunken", L.T("ThemeTokens.ListsAndGrids"), ThemeTokenGroup.Backgrounds, L.T("ThemeTokens.BackgroundsOfListsAndTables"), false,
            ["Surface", "WindowBackground"], p => Mix(p, "Surface", "WindowBackground", 0.2)),
        Derived("SurfaceReadOnly", L.T("ThemeTokens.ReadOnlyFields"), ThemeTokenGroup.Backgrounds, L.T("ThemeTokens.BoxesShowingValuesThatCannotBe"), false,
            ["Surface", "WindowBackground"], p => Mix(p, "Surface", "WindowBackground", 0.4)),

        // Text
        Derived("TextSecondary", L.T("ThemeTokens.SecondaryText"), ThemeTokenGroup.Text, L.T("ThemeTokens.HintsLabelsAndDescriptions"), true,
            ["Text", "Surface"], p => Mix(p, "Text", "Surface", 0.3)),
        Derived("TextDisabled", L.T("ThemeTokens.DisabledText"), ThemeTokenGroup.Text, L.T("ThemeTokens.ControlsThatCannotBeUsedRight"), true,
            ["Text", "Surface"], p => Mix(p, "Text", "Surface", 0.55)),
        Derived("Link", "Links", ThemeTokenGroup.Text, L.T("ThemeTokens.ClickableLinksIncludingSavedFilePaths"), true,
            ["Accent", "Surface", "Text"], p => Ensure(p["Accent"], p["Surface"], p["Text"], 4.5)),

        // Accent and buttons
        Derived("AccentText", L.T("ThemeTokens.TextOnAccent"), ThemeTokenGroup.AccentAndButtons, L.T("ThemeTokens.TextAndCheckMarksOnAccent"), true,
            ["Accent"], p => p["Accent"].BestTextColor),
        Derived("AccentHover", L.T("ThemeTokens.AccentHover"), ThemeTokenGroup.AccentAndButtons, L.T("ThemeTokens.AccentButtonsUnderTheMouse"), false,
            ["Accent"], p => p["Accent"].AdjustLightness(-0.04)),
        Derived("AccentPressed", L.T("ThemeTokens.AccentPressed"), ThemeTokenGroup.AccentAndButtons, L.T("ThemeTokens.AccentButtonsWhilePressed"), false,
            ["Accent"], p => p["Accent"].AdjustLightness(-0.08)),
        Derived("AccentSoft", L.T("ThemeTokens.AccentSoft"), ThemeTokenGroup.AccentAndButtons, L.T("ThemeTokens.BadgesAndSoftHighlights"), false,
            ["Surface", "Accent"], p => Mix(p, "Surface", "Accent", 0.15)),
        Derived("ButtonBackground", L.T("ThemeTokens.ButtonBackground"), ThemeTokenGroup.AccentAndButtons, L.T("ThemeTokens.OrdinaryButtons"), true,
            ["Surface"], p => p["Surface"]),
        Derived("ButtonText", L.T("ThemeTokens.ButtonText"), ThemeTokenGroup.AccentAndButtons, L.T("ThemeTokens.TextOfOrdinaryButtons"), true,
            ["Text", "ButtonBackground"], p => Readable(p["Text"], p["ButtonBackground"])),

        // Borders
        Derived("Border", "Borders", ThemeTokenGroup.Borders, L.T("ThemeTokens.OutlinesOfCardsPanelsAndLists"), true,
            ["Surface", "Text"], p => Mix(p, "Surface", "Text", 0.25)),
        Derived("GridLine", "Separators", ThemeTokenGroup.Borders, L.T("ThemeTokens.LinesBetweenRowsAndSections"), true,
            ["Surface", "Text"], p => Mix(p, "Surface", "Text", 0.1)),
        Derived("BorderStrong", L.T("ThemeTokens.StrongBorders"), ThemeTokenGroup.Borders, L.T("ThemeTokens.SwatchOutlinesAndEmphasizedLines"), false,
            ["Surface", "Text"], p => Mix(p, "Surface", "Text", 0.4)),
        Derived("ButtonBorder", L.T("ThemeTokens.ButtonBorders"), ThemeTokenGroup.Borders, L.T("ThemeTokens.OutlinesOfOrdinaryButtons"), false,
            ["ButtonBackground", "ButtonText"], p => Mix(p, "ButtonBackground", "ButtonText", 0.2)),

        // Inputs
        Derived("InputBackground", L.T("ThemeTokens.InputBackground"), ThemeTokenGroup.Inputs, L.T("ThemeTokens.TextBoxesDropDownListsCheck"), true,
            ["Surface", "WindowBackground"], p => Mix(p, "Surface", "WindowBackground", 0.2)),
        Derived("InputText", L.T("ThemeTokens.InputText"), ThemeTokenGroup.Inputs, L.T("ThemeTokens.TextTypedIntoInputs"), true,
            ["Text", "InputBackground"], p => Readable(p["Text"], p["InputBackground"])),
        Derived("InputBorder", L.T("ThemeTokens.InputBorder"), ThemeTokenGroup.Inputs, L.T("ThemeTokens.OutlinesOfInputs"), true,
            ["InputBackground", "InputText"], p => Mix(p, "InputBackground", "InputText", 0.3)),
        Derived("InputBorderHover", L.T("ThemeTokens.InputBorderHover"), ThemeTokenGroup.Inputs, L.T("ThemeTokens.OutlinesOfInputsUnderTheMouse"), false,
            ["InputBackground", "InputText"], p => Mix(p, "InputBackground", "InputText", 0.5)),

        // States
        Derived("Hover", "Hover", ThemeTokenGroup.States, L.T("ThemeTokens.ButtonsAndListRowsUnderThe"), true,
            ["Surface", "Text"], p => Mix(p, "Surface", "Text", 0.08)),
        Derived("Pressed", "Pressed", ThemeTokenGroup.States, L.T("ThemeTokens.ButtonsWhilePressed"), false,
            ["Surface", "Text"], p => Mix(p, "Surface", "Text", 0.14)),
        Derived("Selection", L.T("ThemeTokens.SelectedItem"), ThemeTokenGroup.States, L.T("ThemeTokens.SelectedRowsAndItems"), true,
            ["Surface", "Accent"], p => Mix(p, "Surface", "Accent", 0.22)),
        Derived("SelectionText", L.T("ThemeTokens.SelectedItemText"), ThemeTokenGroup.States, L.T("ThemeTokens.TextOfSelectedRows"), true,
            ["Text", "Selection"], p => Readable(p["Text"], p["Selection"])),
        Derived("SelectionInactive", L.T("ThemeTokens.SelectedItemInactive"), ThemeTokenGroup.States, L.T("ThemeTokens.SelectionInAListThatDoes"), false,
            ["Surface", "Text"], p => Mix(p, "Surface", "Text", 0.1)),
        Derived("NavItemHover", L.T("ThemeTokens.NavigationHover"), ThemeTokenGroup.States, L.T("ThemeTokens.NavigationEntriesUnderTheMouse"), true,
            ["NavBackground", "Text"], p => Mix(p, "NavBackground", "Text", 0.08)),
        Derived("NavItemSelected", L.T("ThemeTokens.NavigationSelected"), ThemeTokenGroup.States, L.T("ThemeTokens.TheOpenPageInTheNavigation"), true,
            ["Surface"], p => p["Surface"]),
        Derived("Focus", L.T("ThemeTokens.FocusOutline"), ThemeTokenGroup.States, L.T("ThemeTokens.KeyboardFocusRectanglesAndOpenDrop"), true,
            ["Accent", "Link", "Surface"], p => ThemeColor.Contrast(p["Accent"], p["Surface"]) >= 3 ? p["Accent"] : p["Link"]),

        // Status
        Derived("SuccessSoft", L.T("ThemeTokens.SuccessSoft"), ThemeTokenGroup.Status, L.T("ThemeTokens.BackgroundOfSuccessBadges"), false,
            ["Surface", "Success"], p => Mix(p, "Surface", "Success", 0.12)),
        Derived("WarningSoft", L.T("ThemeTokens.WarningSoft"), ThemeTokenGroup.Status, L.T("ThemeTokens.BackgroundOfWarningBanners"), false,
            ["Surface", "Warning"], p => Mix(p, "Surface", "Warning", 0.12)),
        Derived("DangerSoft", L.T("ThemeTokens.ErrorSoft"), ThemeTokenGroup.Status, L.T("ThemeTokens.BackgroundOfErrorBanners"), false,
            ["Surface", "Danger"], p => Mix(p, "Surface", "Danger", 0.12)),

        // Other
        Derived("ScrollThumb", L.T("ThemeTokens.ScrollBars"), ThemeTokenGroup.Other, L.T("ThemeTokens.ScrollBarThumbs"), false,
            ["Surface", "Text"], p => Mix(p, "Surface", "Text", 0.3)),
        Derived("ScrollThumbHover", L.T("ThemeTokens.ScrollBarsHover"), ThemeTokenGroup.Other, L.T("ThemeTokens.ScrollBarThumbsUnderTheMouse"), false,
            ["Surface", "Text"], p => Mix(p, "Surface", "Text", 0.42)),
    ];

    private static readonly Dictionary<string, ThemeToken> ByKey = All.ToDictionary(t => t.Key, StringComparer.Ordinal);

    public static ThemeToken? Find(string key) => ByKey.GetValueOrDefault(key);

    public static bool IsToken(string key) => ByKey.ContainsKey(key);

    public static string GroupName(ThemeTokenGroup group) => group switch
    {
        ThemeTokenGroup.Backgrounds => "Backgrounds",
        ThemeTokenGroup.Text => L.T("ThemeTokens.TextAndLinks"),
        ThemeTokenGroup.AccentAndButtons => L.T("ThemeTokens.AccentAndButtons"),
        ThemeTokenGroup.States => L.T("ThemeTokens.HoverSelectionAndFocus"),
        ThemeTokenGroup.Borders => L.T("ThemeTokens.BordersAndSeparators"),
        ThemeTokenGroup.Inputs => L.T("ThemeTokens.InputFields"),
        ThemeTokenGroup.Status => L.T("ThemeTokens.SuccessWarningAndError"),
        ThemeTokenGroup.Tooltips => "Tooltips",
        _ => "Other",
    };
}
