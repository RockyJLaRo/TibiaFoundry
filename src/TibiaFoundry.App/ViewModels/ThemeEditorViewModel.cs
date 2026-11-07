using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.Themes;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Theming;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>One theme in the list: a built-in theme or a custom one.</summary>
public sealed partial class ThemeEntry(string id, string name, string description, bool isBuiltIn) : ObservableObject
{
    public const string SystemId = "builtin.system";
    public const string LightId = "builtin.light";
    public const string DarkId = "builtin.dark";

    public string Id { get; } = id;
    [ObservableProperty] private string _name = name;
    [ObservableProperty] private string _description = description;
    public bool IsBuiltIn { get; } = isBuiltIn;

    [ObservableProperty] private bool _isActive;

    /// <summary>"Built-in" or "Custom" (with "unsaved changes" while the active custom theme has a draft).</summary>
    [ObservableProperty] private string _badge = isBuiltIn ? L.T("ThemeEditor.BuiltIn") : L.T("ThemeEditor.Custom");
}

/// <summary>One color of the palette in the editor.</summary>
public sealed partial class ThemeColorRow(ThemeEditorViewModel owner, ThemeToken token) : ObservableObject
{
    public ThemeToken Token { get; } = token;
    public string Key => Token.Key;
    public string Label => Token.Label;
    public string Description => Token.Description;
    public string Group => ThemeTokens.GroupName(Token.Group);
    public bool IsPrimary => Token.Primary;

    [ObservableProperty] private string _hex = "";
    [ObservableProperty] private Brush _swatch = Brushes.Transparent;

    /// <summary>The user changed this color (it does not follow the base or the colors it is derived from).</summary>
    [ObservableProperty] private bool _isCustomized;

    /// <summary>Differs from the base palette because a color it is derived from was changed.</summary>
    [ObservableProperty] private bool _isDerivedChange;

    public string StateText => IsCustomized ? "changed" : IsDerivedChange ? "follows" : "";

    public string ToolTip => $"{Label}: {Description}" + (Token.Sources.Count > 0 ? L.F("ThemeEditor.Follows0", string.Join(", ", Token.Sources.Select(s => ThemeTokens.Find(s)?.Label ?? s))) : "");

    partial void OnIsCustomizedChanged(bool value) => OnPropertyChanged(nameof(StateText));
    partial void OnIsDerivedChangeChanged(bool value) => OnPropertyChanged(nameof(StateText));

    internal void Set(ThemeColor color, bool customized, bool derivedChange)
    {
        Hex = color.Hex;
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        Swatch = brush;
        IsCustomized = customized;
        IsDerivedChange = derivedChange;
    }

    [RelayCommand] private void Select() => owner.SelectedColor = this;
}

/// <summary>
/// Settings → Appearance &amp; Themes. Built-in themes (System, Light, Dark) cannot be changed;
/// custom themes start from the light or dark palette and change any of its colors. Edits apply
/// to the whole interface immediately and are kept as unsaved changes until Save (or Revert);
/// leaving a theme with unsaved changes asks first. Everything is stored in the settings file.
/// </summary>
public sealed partial class ThemeEditorViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MainViewModel _main;
    private CustomTheme? _draft;
    private bool _dirty;
    private bool _updatingEditor;

    public ThemeEditorViewModel(AppServices services, MainViewModel main)
    {
        _services = services;
        _main = main;
        Colors = new ObservableCollection<ThemeColorRow>(ThemeTokens.All.OrderBy(t => t.Group).Select(t => new ThemeColorRow(this, t)));
        ColorView = new System.Windows.Data.ListCollectionView(Colors);
        ColorView.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(ThemeColorRow.Group)));
        ColorView.Filter = o => ShowAllColors || ((ThemeColorRow)o).IsPrimary;
        main.ThemeSwitched += () =>
        {
            _draft = null;
            SetDirty(false);
            Refresh();
        };
        Refresh();
    }

    private AppSettings S => _services.Settings;
    private ThemeSettings T => S.Themes;

    public ObservableCollection<ThemeEntry> Themes { get; } = [];
    public ObservableCollection<ThemeColorRow> Colors { get; }
    public ObservableCollection<string> Warnings { get; } = [];

    [ObservableProperty] private ThemeEntry? _selectedTheme;
    [ObservableProperty] private ThemeColorRow? _selectedColor;
    [ObservableProperty] private bool _showAllColors;
    [ObservableProperty] private string _hexInput = "";
    [ObservableProperty] private string? _hexProblem;
    [ObservableProperty] private double _hue;
    [ObservableProperty] private double _saturation;
    [ObservableProperty] private double _lightness;

    /// <summary>The color the selected token has in the base palette (for comparison).</summary>
    [ObservableProperty] private Brush _baseSwatch = Brushes.Transparent;
    [ObservableProperty] private string _baseHex = "";

    /// <summary>Live preview of the color being edited.</summary>
    [ObservableProperty] private Brush _editSwatch = Brushes.Transparent;

    /// <summary>Hue strip behind the hue slider and saturation/lightness ramps for the current hue.</summary>
    public Brush HueGradient { get; } = MakeHueGradient();
    [ObservableProperty] private Brush _saturationGradient = Brushes.Transparent;
    [ObservableProperty] private Brush _lightnessGradient = Brushes.Transparent;

    /// <summary>True when the active theme is a custom one (its colors can be edited).</summary>
    public bool CanEdit => ActiveCustom != null;
    public bool IsBuiltInActive => ActiveCustom == null;
    public bool HasUnsavedChanges => _dirty;
    public bool HasSelectedColor => SelectedColor != null && CanEdit;
    public bool HasWarnings => Warnings.Count > 0;

    public string ActiveThemeName => ActiveCustom?.Name ?? S.Theme switch
    {
        ThemePreference.Light => L.T("ThemeEditor.Light"),
        ThemePreference.Dark => L.T("ThemeEditor.Dark"),
        _ => L.F("ThemeEditor.System0", (ThemeManager.IsDark ? L.T("ThemeEditor.SystemIsDark") : L.T("ThemeEditor.SystemIsLight"))),
    };

    public string ActiveThemeSummary => ActiveCustom is { } c
        ? L.F("ThemeEditor.CustomThemeBasedOnThe0", c.Base.ToString().ToLowerInvariant(), L.Count(c.Colors.Count(kv => ThemeTokens.IsToken(kv.Key)), "Noun.color"), (_dirty ? L.T("ThemeEditor.UnsavedChanges") : ""))
        : L.T("ThemeEditor.BuiltInThemesCannotBeChanged");

    /// <summary>The custom theme being shown: the draft while editing, otherwise the saved theme.</summary>
    private CustomTheme? ActiveCustom => _draft ?? T.Find(T.ActiveCustomTheme);

    /// <summary>For the closing question: what would be lost.</summary>
    public string? UnsavedDescription => _dirty && _draft != null ? L.F("ThemeEditor.ColorChangesToTheTheme0", _draft.Name) : null;

    // ------------------------------------------------------------------ list and activation

    /// <summary>Rebuilds the theme list and the color rows from the settings and the active palette.</summary>
    public void Refresh()
    {
        string? keep = SelectedTheme?.Id;
        Themes.Clear();
        Themes.Add(new ThemeEntry(ThemeEntry.SystemId, "System", L.T("ThemeEditor.LightOrDarkFollowingWindowsThe"), true));
        Themes.Add(new ThemeEntry(ThemeEntry.LightId, L.T("ThemeEditor.Light"), L.T("ThemeEditor.TheOriginalLightAppearance"), true));
        Themes.Add(new ThemeEntry(ThemeEntry.DarkId, L.T("ThemeEditor.Dark"), L.T("ThemeEditor.TheOriginalDarkAppearance"), true));
        foreach (var c in T.Custom) Themes.Add(new ThemeEntry(c.Id, c.Name, L.F("ThemeEditor.BasedOn0", c.Base.ToString().ToLowerInvariant()), false));
        string activeId = ActiveId();
        foreach (var e in Themes)
        {
            e.IsActive = e.Id == activeId;
            if (e.IsActive && !e.IsBuiltIn && _dirty) e.Badge = L.T("ThemeEditor.CustomUnsavedChanges");
        }
        _selectingFromCode = true;
        SelectedTheme = Themes.FirstOrDefault(t => t.Id == activeId) ?? Themes.FirstOrDefault(t => t.Id == keep);
        _selectingFromCode = false;
        RefreshColors();
        RaiseState();
    }

    private string ActiveId() => T.ActiveCustomTheme ?? S.Theme switch
    {
        ThemePreference.Light => ThemeEntry.LightId,
        ThemePreference.Dark => ThemeEntry.DarkId,
        _ => ThemeEntry.SystemId,
    };

    private bool _selectingFromCode;

    /// <summary>Choosing a theme in the list activates it at once (asking first when the current one has unsaved changes).</summary>
    partial void OnSelectedThemeChanged(ThemeEntry? oldValue, ThemeEntry? newValue)
    {
        if (_selectingFromCode || newValue == null || newValue.Id == ActiveId()) return;
        if (!ConfirmLeavingDraft())
        {
            _selectingFromCode = true;
            SelectedTheme = oldValue;
            _selectingFromCode = false;
            return;
        }
        Activate(newValue.Id);
    }

    /// <summary>Activates a theme by id and saves the choice.</summary>
    public void Activate(string id)
    {
        _draft = null;
        SetDirty(false);
        switch (id)
        {
            case ThemeEntry.SystemId: _main.Theme = ThemePreference.System; break;
            case ThemeEntry.LightId: _main.Theme = ThemePreference.Light; break;
            case ThemeEntry.DarkId: _main.Theme = ThemePreference.Dark; break;
            default:
                if (T.Find(id) is not { } theme) return;
                T.ActiveCustomTheme = theme.Id;
                ThemeManager.ApplyCustom(theme);
                _services.SaveSettings();
                break;
        }
        Refresh();
        _services.Status.Show(L.F("ThemeEditor.Theme0", ActiveThemeName), StatusKind.Success);
    }

    /// <summary>When the active custom theme has unsaved changes: asks to save, discard or stay. False = stay.</summary>
    public bool ConfirmLeavingDraft()
    {
        if (!_dirty || _draft == null) return true;
        // Unattended runs (no person to ask) keep the saved theme: unsaved edits are discarded, never saved.
        int choice = DialogService.Unattended ? 1
            : _services.Dialogs.Choose(L.F("ThemeEditor.TheTheme0HasColorChanges", _draft.Name), L.T("ThemeEditor.UnsavedThemeChanges"), L.T("Common.Save"), L.T("ThemeEditor.Discard"));
        switch (choice)
        {
            case 0: Save(); return true;
            case 1: Revert(); return true;
            default: return false;
        }
    }

    // ------------------------------------------------------------------ colors

    partial void OnShowAllColorsChanged(bool value)
    {
        ColorView.Refresh();
        OnPropertyChanged(nameof(VisibleColors));
    }

    /// <summary>The color list of the editor, grouped, showing the main colors or all of them.</summary>
    public System.Windows.Data.ListCollectionView ColorView { get; }

    /// <summary>The colors shown: the main ones, or all with "Show all colors".</summary>
    public IEnumerable<ThemeColorRow> VisibleColors => ShowAllColors ? Colors : Colors.Where(c => c.IsPrimary);

    private IReadOnlyDictionary<string, ThemeColor> BasePalette(ThemeBase b) => ThemeManager.Engine?.BaseColors(b) ?? new Dictionary<string, ThemeColor>();

    private void RefreshColors()
    {
        var engine = ThemeManager.Engine;
        if (engine == null) return;
        var current = engine.Current;
        if (current.Count == 0) return;
        var custom = ActiveCustom;
        var basePalette = engine.BaseColors(custom?.Base ?? engine.ActiveBase);
        var overrides = custom?.ValidOverrides() ?? new Dictionary<string, ThemeColor>();
        foreach (var row in Colors)
        {
            var value = current[row.Key];
            bool customized = overrides.ContainsKey(row.Key);
            row.Set(value, customized, !customized && value != basePalette[row.Key]);
        }
        Warnings.Clear();
        foreach (var w in ThemeAccessibility.Check(current)) Warnings.Add(w.Message);
        OnPropertyChanged(nameof(HasWarnings));
        UpdateEditor();
    }

    partial void OnSelectedColorChanged(ThemeColorRow? value)
    {
        UpdateEditor();
        OnPropertyChanged(nameof(HasSelectedColor));
    }

    /// <summary>Loads the selected color into the hue, saturation, lightness and hex controls.</summary>
    private void UpdateEditor()
    {
        if (SelectedColor is not { } row || ThemeManager.Engine is not { } engine || engine.Current.Count == 0) return;
        var color = engine.Current[row.Key];
        var basePalette = engine.BaseColors(ActiveCustom?.Base ?? engine.ActiveBase);
        _updatingEditor = true;
        try
        {
            var (h, s, l) = color.ToHsl();
            // Gray colors have no hue: keep the slider where it is instead of jumping to red.
            if (s > 0.001) Hue = Math.Round(h, 1);
            Saturation = Math.Round(s * 100, 1);
            Lightness = Math.Round(l * 100, 1);
            HexInput = color.Hex;
            HexProblem = null;
            BaseHex = basePalette[row.Key].Hex;
            BaseSwatch = Frozen(basePalette[row.Key]);
            EditSwatch = Frozen(color);
            UpdateGradients();
        }
        finally
        {
            _updatingEditor = false;
        }
    }

    partial void OnHueChanged(double value) => FromSliders();
    partial void OnSaturationChanged(double value) => FromSliders();
    partial void OnLightnessChanged(double value) => FromSliders();

    private void FromSliders()
    {
        if (_updatingEditor) return;
        var color = ThemeColor.FromHsl(Hue, Saturation / 100, Lightness / 100);
        _updatingEditor = true;
        HexInput = color.Hex;
        HexProblem = null;
        _updatingEditor = false;
        SetColor(color);
        UpdateGradients();
    }

    /// <summary>Typed hex values apply as soon as they are complete and valid; invalid text is explained and not applied.</summary>
    partial void OnHexInputChanged(string value)
    {
        if (_updatingEditor) return;
        if (!ThemeColor.TryParse(value, out var color, out string? problem))
        {
            HexProblem = problem;
            return;
        }
        HexProblem = null;
        _updatingEditor = true;
        var (h, s, l) = color.ToHsl();
        if (s > 0.001) Hue = Math.Round(h, 1);
        Saturation = Math.Round(s * 100, 1);
        Lightness = Math.Round(l * 100, 1);
        _updatingEditor = false;
        SetColor(color);
        UpdateGradients();
    }

    /// <summary>Changes the selected color of the draft and applies it to the interface immediately.</summary>
    public void SetColor(ThemeColor color)
    {
        if (SelectedColor is not { } row || ActiveCustom is not { } active) return;
        if (_draft == null) _draft = active.Clone();
        if (_draft.Colors.TryGetValue(row.Key, out string? old) && old == color.Hex) return;
        _draft.Colors[row.Key] = color.Hex;
        EditSwatch = Frozen(color);
        ApplyDraft();
    }

    /// <summary>Sets a color by token key (used by tests and the hex box).</summary>
    public bool TrySetColor(string key, string hex, out string? problem)
    {
        problem = null;
        var row = Colors.FirstOrDefault(c => c.Key == key);
        if (row == null) { problem = L.F("ThemeEditor.UnknownColor0", key); return false; }
        if (!ThemeColor.TryParse(hex, out var color, out problem)) return false;
        SelectedColor = row;
        SetColor(color);
        return true;
    }

    private void ApplyDraft()
    {
        if (_draft == null) return;
        _draft.ModifiedUtc = DateTime.UtcNow;
        ThemeManager.UpdateColors(_draft);
        SetDirty(true);
        RefreshColors();
        RaiseState();
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        var active = Themes.FirstOrDefault(t => t.IsActive && !t.IsBuiltIn);
        if (active != null) active.Badge = dirty ? L.T("ThemeEditor.CustomUnsavedChanges") : L.T("ThemeEditor.Custom");
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(UnsavedDescription));
    }

    private void UpdateGradients()
    {
        SaturationGradient = Gradient(ThemeColor.FromHsl(Hue, 0, Lightness / 100), ThemeColor.FromHsl(Hue, 1, Lightness / 100));
        LightnessGradient = Gradient(ThemeColor.FromHsl(Hue, Saturation / 100, 0), ThemeColor.FromHsl(Hue, Saturation / 100, 0.5), ThemeColor.FromHsl(Hue, Saturation / 100, 1));
    }

    private static Brush Gradient(params ThemeColor[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new(0, 0.5), EndPoint = new(1, 0.5) };
        for (int i = 0; i < stops.Length; i++) brush.GradientStops.Add(new GradientStop(Color.FromRgb(stops[i].R, stops[i].G, stops[i].B), i / (double)(stops.Length - 1)));
        brush.Freeze();
        return brush;
    }

    private static Brush MakeHueGradient() => Gradient(Enumerable.Range(0, 7).Select(i => ThemeColor.FromHsl(i * 60, 1, 0.5)).ToArray());

    private static Brush Frozen(ThemeColor c)
    {
        var b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(IsBuiltInActive));
        OnPropertyChanged(nameof(HasSelectedColor));
        OnPropertyChanged(nameof(ActiveThemeName));
        OnPropertyChanged(nameof(ActiveThemeSummary));
        OnPropertyChanged(nameof(VisibleColors));
        SaveCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    // ------------------------------------------------------------------ commands

    /// <summary>Stores the draft as the custom theme (only the theme section of the settings changes).</summary>
    [RelayCommand(CanExecute = nameof(HasUnsavedChanges))]
    private void Save()
    {
        if (_draft == null) return;
        int index = T.Custom.FindIndex(t => t.Id == _draft.Id);
        if (index < 0) return;
        T.Custom[index] = _draft.Clone();
        T.ActiveCustomTheme = _draft.Id;
        string name = _draft.Name;
        _draft = null;
        SetDirty(false);
        _services.SaveSettings();
        AppLog.Info($"Theme \"{name}\" saved.", source: "Themes");
        Refresh();
        _services.Status.Show(L.F("ThemeEditor.Theme0Saved", name), StatusKind.Success);
    }

    /// <summary>Discards unsaved changes and shows the saved theme again.</summary>
    [RelayCommand(CanExecute = nameof(HasUnsavedChanges))]
    private void Revert()
    {
        _draft = null;
        SetDirty(false);
        // Only the colors that differ from the saved theme change back (no full palette switch).
        if (T.Find(T.ActiveCustomTheme) is { } saved) ThemeManager.UpdateColors(saved);
        Refresh();
    }

    /// <summary>Restores the selected color to its base value (it follows its sources again).</summary>
    [RelayCommand]
    private void ResetColor()
    {
        if (SelectedColor is not { } row || ActiveCustom is not { } active) return;
        _draft ??= active.Clone();
        if (!_draft.Colors.Remove(row.Key)) return;
        ApplyDraft();
    }

    /// <summary>Restores every color of the active custom theme to its base palette (unsaved until Save).</summary>
    [RelayCommand]
    private void ResetTheme()
    {
        if (ActiveCustom is not { } active || active.Colors.Count == 0) return;
        if (!_services.Dialogs.Confirm(L.F("ThemeEditor.ResetEveryColorOf0To", active.Name, active.Base.ToString().ToLowerInvariant()),
                L.T("ThemeEditor.ResetTheme"), L.T("ThemeEditor.ResetColors"), L.T("Common.Cancel"))) return;
        _draft ??= active.Clone();
        _draft.Colors.Clear();
        ApplyDraft();
    }

    /// <summary>Back to the original appearance: the System theme. Custom themes stay saved.</summary>
    [RelayCommand]
    private void ResetAll()
    {
        if (!_services.Dialogs.Confirm(L.T("ThemeEditor.RestoreTheOriginalAppearanceTheSystem"),
                L.T("ThemeEditor.RestoreOriginalAppearance"), L.T("ThemeEditor.Restore"), L.T("Common.Cancel"))) return;
        _draft = null;
        SetDirty(false);
        T.ActiveCustomTheme = null;
        S.Theme = ThemePreference.System;
        ThemeManager.Apply(ThemePreference.System);
        _services.SaveSettings();
        Refresh();
        _services.Status.Show(L.T("ThemeEditor.TheOriginalAppearanceWasRestoredSystem"), StatusKind.Success);
    }

    /// <summary>Creates a custom theme from the active one (a copy of a custom theme, or a built-in palette) and activates it.</summary>
    [RelayCommand]
    private void NewTheme()
    {
        if (T.Custom.Count >= ThemeSettings.MaxCustomThemes)
        {
            _services.Dialogs.Show(L.F("ThemeEditor.AtMost0CustomThemesCan", ThemeSettings.MaxCustomThemes), MessageKind.Info, L.T("ThemeEditor.NewTheme"));
            return;
        }
        if (!ConfirmLeavingDraft()) return;
        var source = ActiveCustom;
        string suggested = source != null ? source.Name + " copy" : L.F("ThemeEditor.My0Theme", (ThemeManager.IsDark ? L.T("ThemeEditor.SystemIsDark") : L.T("ThemeEditor.SystemIsLight")));
        string? name = _services.Dialogs.Prompt(L.T("ThemeEditor.NewTheme"), L.T("ThemeEditor.NameOfTheNewTheme"), ThemeSettings.UniqueName(ThemeSettings.CleanName(suggested), T.Custom.Select(c => c.Name).ToList()));
        if (name == null) return;
        var theme = new CustomTheme
        {
            Id = ThemeSettings.NewId(T.Custom.Select(c => c.Id).ToList()),
            Name = ThemeSettings.UniqueName(ThemeSettings.CleanName(name), T.Custom.Select(c => c.Name).ToList()),
            Base = source?.Base ?? (ThemeManager.IsDark ? ThemeBase.Dark : ThemeBase.Light),
            Colors = source != null ? new Dictionary<string, string>(source.Colors, StringComparer.Ordinal) : new(StringComparer.Ordinal),
            ModifiedUtc = DateTime.UtcNow,
        };
        T.Custom.Add(theme);
        AppLog.Info($"Theme \"{theme.Name}\" created (based on {theme.Base}).", source: "Themes");
        Activate(theme.Id);
        SelectedColor ??= Colors.FirstOrDefault(c => c.Key == "Accent");
    }

    [RelayCommand]
    private void RenameTheme()
    {
        if (T.Find(T.ActiveCustomTheme) is not { } theme) return;
        string? name = _services.Dialogs.Prompt(L.T("ThemeEditor.RenameTheme"), L.T("ThemeEditor.NewName"), theme.Name);
        if (name == null) return;
        string clean = ThemeSettings.UniqueName(ThemeSettings.CleanName(name), T.Custom.Where(c => c != theme).Select(c => c.Name).ToList());
        theme.Name = clean;
        if (_draft != null) _draft.Name = clean;
        _services.SaveSettings();
        Refresh();
    }

    /// <summary>Deletes the active custom theme (built-in themes cannot be deleted); the System theme becomes active.</summary>
    [RelayCommand]
    private void DeleteTheme()
    {
        if (T.Find(T.ActiveCustomTheme) is not { } theme) return;
        if (!_services.Dialogs.Confirm(L.F("ThemeEditor.DeleteTheTheme0ThisCannot", theme.Name), L.T("ThemeEditor.DeleteTheme"), L.T("ThemeEditor.Delete"), L.T("Common.Cancel"))) return;
        _draft = null;
        SetDirty(false);
        T.Custom.Remove(theme);
        T.ActiveCustomTheme = null;
        ThemeManager.Apply(S.Theme);
        _services.SaveSettings();
        AppLog.Info($"Theme \"{theme.Name}\" deleted.", source: "Themes");
        Refresh();
        _services.Status.Show(L.F("ThemeEditor.Theme0DeletedThe1Theme", theme.Name, ActiveThemeName), StatusKind.Success);
    }

    [RelayCommand]
    private void ExportTheme()
    {
        if (ActiveCustom is not { } theme) return;
        string? file = _services.Outputs.SaveFile(OutputKind.DataExports, L.T("ThemeEditor.ExportTheme"), L.T("ThemeEditor.TibiaFoundryThemesTfthemeJsonTftheme"),
            FileNames.Sanitize(theme.Name) + ".tftheme.json");
        if (file == null) return;
        try
        {
            AtomicFile.WriteAllText(file, ThemeFile.Serialize(theme));
            _services.Status.ShowSaved(file, isFile: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Dialogs.ShowError(L.T("ThemeEditor.TheThemeCouldNotBeExported"), ex);
        }
    }

    [RelayCommand]
    private void ImportTheme()
    {
        if (T.Custom.Count >= ThemeSettings.MaxCustomThemes)
        {
            _services.Dialogs.Show(L.F("ThemeEditor.AtMost0CustomThemesCan", ThemeSettings.MaxCustomThemes), MessageKind.Info, L.T("ThemeEditor.ImportTheme"));
            return;
        }
        string? file = _services.Dialogs.OpenFile(L.T("ThemeEditor.ImportTheme"), L.T("ThemeEditor.TibiaFoundryThemesTfthemeJsonJson"), _services.Outputs.FolderFor(OutputKind.DataExports));
        if (file == null || !ConfirmLeavingDraft()) return;
        try
        {
            var info = new FileInfo(file);
            if (info.Length > 256 * 1024) throw new InvalidDataException("The file is too large to be a theme.");
            var (theme, ignored) = ThemeFile.Parse(File.ReadAllText(file));
            theme.Name = ThemeSettings.UniqueName(theme.Name, T.Custom.Select(c => c.Name).ToList());
            theme.Id = ThemeSettings.NewId(T.Custom.Select(c => c.Id).ToList());
            T.Custom.Add(theme);
            Activate(theme.Id);
            if (ignored.Count > 0)
                _services.Dialogs.Show(L.F("ThemeEditor.0WasImportedTheseColorsWere", theme.Name, theme.Base.ToString().ToLowerInvariant(), string.Join("\n", ignored.Take(20))),
                    MessageKind.Warning, L.T("ThemeEditor.ImportTheme"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _services.Dialogs.Show(L.F("ThemeEditor.TheThemeCouldNotBeImported", ex.Message), MessageKind.Warning, L.T("ThemeEditor.ImportTheme"));
        }
    }
}
