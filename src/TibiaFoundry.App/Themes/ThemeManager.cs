using System.Collections;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Theming;

namespace TibiaFoundry.App.Themes;

/// <summary>
/// Applies themes at run time. The built-in light and dark palettes (Colors.Light.xaml,
/// Colors.Dark.xaml) are the bases; a custom theme is a base plus the colors the user changed,
/// resolved by <see cref="ThemeResolver"/>. All styles reference palette brushes with
/// DynamicResource, so:
/// <list type="bullet">
/// <item>switching themes replaces the palette dictionary once, and every open window restyles;</item>
/// <item>changing one color while editing (<see cref="UpdateColors"/>) only changes the color of the
/// brushes that differ — no dictionary is replaced and nothing else is re-rendered.</item>
/// </list>
/// Window title bars follow via DWM's immersive dark mode attribute.
/// </summary>
public sealed class ThemeEngine
{
    private readonly ResourceDictionary _host;
    private readonly Dictionary<ThemeBase, (ResourceDictionary Source, Dictionary<string, ThemeColor> Colors)> _bases = new();
    private ResourceDictionary? _palette;

    public ThemeEngine(ResourceDictionary host) => _host = host;

    /// <summary>The base palette the active theme is built on.</summary>
    public ThemeBase ActiveBase { get; private set; } = ThemeBase.Light;

    /// <summary>The colors in effect (every token).</summary>
    public IReadOnlyDictionary<string, ThemeColor> Current { get; private set; } = new Dictionary<string, ThemeColor>();

    /// <summary>True when the main background is dark (title bars follow).</summary>
    public bool IsDark { get; private set; }

    /// <summary>The palette dictionary currently merged into the host (for tests and diagnostics).</summary>
    public ResourceDictionary? Palette => _palette;

    /// <summary>How many brushes the last <see cref="UpdateColors"/> changed.</summary>
    public int LastUpdatedBrushes { get; private set; }

    public static Uri BaseUri(ThemeBase b) =>
        new($"pack://application:,,,/TibiaFoundry;component/Themes/Colors.{(b == ThemeBase.Dark ? "Dark" : "Light")}.xaml", UriKind.Absolute);

    /// <summary>The built-in colors of a base palette, read from its XAML file.</summary>
    public IReadOnlyDictionary<string, ThemeColor> BaseColors(ThemeBase b) => Base(b).Colors;

    private (ResourceDictionary Source, Dictionary<string, ThemeColor> Colors) Base(ThemeBase b)
    {
        if (_bases.TryGetValue(b, out var loaded)) return loaded;
        _ = Application.Current; // registers the pack:// scheme when no Application exists (tests)
        var source = new ResourceDictionary { Source = BaseUri(b) };
        var colors = new Dictionary<string, ThemeColor>(StringComparer.Ordinal);
        foreach (var token in ThemeTokens.All)
        {
            if (source[token.ResourceKey] is not SolidColorBrush brush)
                throw new InvalidOperationException($"{BaseUri(b)} does not define the brush {token.ResourceKey}.");
            colors[token.Key] = new ThemeColor(brush.Color.R, brush.Color.G, brush.Color.B);
        }
        loaded = (source, colors);
        _bases[b] = loaded;
        return loaded;
    }

    /// <summary>Resolves a custom theme against its base.</summary>
    public Dictionary<string, ThemeColor> Resolve(CustomTheme theme) => ThemeResolver.Resolve(BaseColors(theme.Base), theme.ValidOverrides());

    /// <summary>Applies a whole palette: a built-in base (custom = null) or a custom theme.</summary>
    public void Apply(ThemeBase b, CustomTheme? custom)
    {
        var (source, colors) = Base(b);
        var resolved = custom == null ? new Dictionary<string, ThemeColor>(colors, StringComparer.Ordinal) : Resolve(custom);
        var next = new ResourceDictionary();
        foreach (DictionaryEntry entry in source)
        {
            // Token brushes are created here (not frozen), so a later color change can update them in place.
            if (entry.Key is string key && key.StartsWith("App.", StringComparison.Ordinal) && resolved.TryGetValue(key[4..], out var color))
                next[key] = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            else
                next[entry.Key] = entry.Value;
        }
        var merged = _host.MergedDictionaries;
        if (_palette != null) merged.Remove(_palette);
        merged.Insert(0, next);
        _palette = next;
        ActiveBase = b;
        Current = resolved;
        IsDark = !resolved["WindowBackground"].IsLight;
    }

    /// <summary>
    /// Live update while a custom theme is edited: recolors only the brushes whose color changed.
    /// Falls back to <see cref="Apply"/> when the theme uses another base than the active palette.
    /// </summary>
    public void UpdateColors(CustomTheme theme)
    {
        if (_palette == null || theme.Base != ActiveBase)
        {
            Apply(theme.Base, theme);
            LastUpdatedBrushes = -1;
            return;
        }
        var resolved = Resolve(theme);
        int changed = 0;
        foreach (var (key, color) in resolved)
        {
            if (Current.TryGetValue(key, out var old) && old == color) continue;
            var wpf = Color.FromRgb(color.R, color.G, color.B);
            if (_palette["App." + key] is SolidColorBrush { IsFrozen: false } brush) brush.Color = wpf;
            else _palette["App." + key] = new SolidColorBrush(wpf);
            changed++;
        }
        Current = resolved;
        IsDark = !resolved["WindowBackground"].IsLight;
        LastUpdatedBrushes = changed;
    }
}

/// <summary>The application's theme: the built-in choice (System, Light, Dark) or a custom theme.</summary>
public static class ThemeManager
{
    private static ThemeEngine? _engine;
    private static ThemePreference _preference = ThemePreference.System;
    private static CustomTheme? _custom;
    private static bool _listening;

    public static bool IsDark => _engine?.IsDark ?? false;

    /// <summary>The engine of the running application (null in tests without an Application).</summary>
    public static ThemeEngine? Engine => _engine;

    /// <summary>The active custom theme (or the draft being edited), or null for a built-in theme.</summary>
    public static CustomTheme? ActiveCustom => _custom;

    public static ThemePreference Preference => _preference;

    public static event Action? ThemeChanged;

    public static void Initialize(ThemePreference preference, CustomTheme? custom = null)
    {
        if (!_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color && _custom == null && _preference == ThemePreference.System)
                    Application.Current?.Dispatcher.BeginInvoke(() => Apply(_preference));
            };
        }
        if (custom != null) ApplyCustom(custom);
        else Apply(preference);
    }

    private static ThemeEngine? _testEngine;

    private static ThemeEngine? EnsureEngine()
    {
        if (_testEngine != null) return _engine = _testEngine;
        var app = Application.Current;
        if (app == null) return null;
        return _engine ??= new ThemeEngine(app.Resources);
    }

    /// <summary>Tests: an engine on a resource dictionary of their own (no Application); null restores the normal behavior.</summary>
    internal static void UseEngineForTests(ThemeEngine? engine)
    {
        _testEngine = engine;
        _engine = engine;
        _custom = null;
        _preference = ThemePreference.System;
    }

    /// <summary>Activates a built-in theme.</summary>
    public static void Apply(ThemePreference preference)
    {
        _preference = preference;
        _custom = null;
        var engine = EnsureEngine();
        if (engine == null) return;
        bool dark = preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => SystemUsesDarkTheme(),
        };
        engine.Apply(dark ? ThemeBase.Dark : ThemeBase.Light, null);
        AfterChange($"{preference} ({(dark ? "dark" : "light")})");
    }

    /// <summary>Activates a custom theme (a full palette switch).</summary>
    public static void ApplyCustom(CustomTheme theme)
    {
        _custom = theme;
        var engine = EnsureEngine();
        if (engine == null) return;
        engine.Apply(theme.Base, theme);
        AfterChange($"custom \"{theme.Name}\" (based on {theme.Base.ToString().ToLowerInvariant()})");
    }

    /// <summary>Applies an edited color of the active custom theme immediately (only the changed brushes).</summary>
    public static void UpdateColors(CustomTheme theme)
    {
        _custom = theme;
        var engine = EnsureEngine();
        if (engine == null) return;
        bool wasDark = engine.IsDark;
        engine.UpdateColors(theme);
        if (engine.IsDark != wasDark && Application.Current is { } app) foreach (Window w in app.Windows) ApplyTitleBar(w);
        ThemeChanged?.Invoke();
    }

    private static void AfterChange(string description)
    {
        if (Application.Current is { } app) foreach (Window w in app.Windows) ApplyTitleBar(w);
        ThemeChanged?.Invoke();
        AppLog.Info($"Theme applied: {description}.", source: "Themes");
    }

    /// <summary>Reads the Windows "app mode" (Settings → Personalization → Colors).</summary>
    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Call from every window (SourceInitialized) so its title bar matches the theme.</summary>
    public static void Attach(Window window)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) ApplyTitleBar(window);
        else window.SourceInitialized += (_, _) => ApplyTitleBar(window);
    }

    private static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int value = IsDark ? 1 : 0;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+); 19 on older builds.
        if (DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
