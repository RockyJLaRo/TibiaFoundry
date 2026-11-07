using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TibiaFoundry.Tests;

/// <summary>
/// Static checks of the XAML resources (parsed as XML, so no window or dispatcher is needed):
/// both palettes must define the same brushes, keys must not collide, every resource used by a
/// view must exist, and text colors must stay readable in both themes.
/// </summary>
public sealed partial class ThemeResourceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string AppSourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "TibiaFoundry.App");
            if (File.Exists(Path.Combine(candidate, "App.xaml"))) return candidate;
            candidate = Path.Combine(dir.FullName, "src", "TibiaFoundry.App");
            if (File.Exists(Path.Combine(candidate, "App.xaml"))) return candidate;
            dir = dir.Parent;
        }
        Assert.Skip("The application's XAML sources were not found next to the test binaries.");
        return "";
    }

    private static Dictionary<string, XElement> Keys(string file) =>
        XDocument.Load(file).Root!.Elements()
            .Where(e => e.Attribute(X + "Key") != null)
            .ToDictionary(e => e.Attribute(X + "Key")!.Value, e => e);

    [Fact]
    public void LightAndDarkPalettesDefineTheSameKeys()
    {
        string themes = Path.Combine(AppSourceDir(), "Themes");
        var light = Keys(Path.Combine(themes, "Colors.Light.xaml"));
        var dark = Keys(Path.Combine(themes, "Colors.Dark.xaml"));
        Assert.Empty(light.Keys.Except(dark.Keys));
        Assert.Empty(dark.Keys.Except(light.Keys));
        foreach (var (key, element) in light) Assert.Equal(element.Name.LocalName, dark[key].Name.LocalName);
    }

    /// <summary>Regression: a Style keyed "App.ScrollThumb" shadowed the brush of the same name.</summary>
    [Fact]
    public void StyleKeysDoNotShadowPaletteKeys()
    {
        string themes = Path.Combine(AppSourceDir(), "Themes");
        var palette = Keys(Path.Combine(themes, "Colors.Light.xaml")).Keys.ToHashSet();
        var styles = Keys(Path.Combine(themes, "Styles.xaml")).Keys.Concat(Keys(Path.Combine(themes, "Shared.xaml")).Keys).ToList();
        foreach (string key in styles) Assert.DoesNotContain(key, palette);
        Assert.Equal(styles.Count, styles.Distinct().Count());
    }

    [GeneratedRegex(@"\{(?:Dynamic|Static)Resource\s+(?:ResourceKey=)?([A-Za-z][\w.]*)\s*\}")]
    private static partial Regex ResourceReference();

    [Fact]
    public void EveryReferencedResourceIsDefined()
    {
        string app = AppSourceDir();
        var global = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(app, "Themes"), "*.xaml")) global.UnionWith(Keys(file).Keys);

        var missing = new List<string>();
        foreach (string file in Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            string text = File.ReadAllText(file);
            // Keys defined anywhere in this file (local resources).
            var local = XDocument.Parse(text).Descendants().Select(e => e.Attribute(X + "Key")?.Value).OfType<string>().ToHashSet();
            foreach (Match m in ResourceReference().Matches(text))
            {
                string key = m.Groups[1].Value;
                if (!global.Contains(key) && !local.Contains(key)) missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }
        Assert.Empty(missing);
    }

    public static TheoryData<string> Palettes => new() { "Colors.Light.xaml", "Colors.Dark.xaml" };

    /// <summary>WCAG 2.1 AA: 4.5:1 for body text, 3:1 for secondary/large text and UI accents.</summary>
    [Theory]
    [MemberData(nameof(Palettes))]
    public void TextColorsAreReadable(string paletteFile)
    {
        var palette = Keys(Path.Combine(AppSourceDir(), "Themes", paletteFile));
        double Contrast(string fg, string bg) => ContrastRatio(palette, fg, bg);

        foreach (string surface in new[] { "App.WindowBackground", "App.Surface", "App.SurfaceAlt", "App.SurfaceSunken", "App.HeaderBackground", "App.NavBackground" })
        {
            Assert.True(Contrast("App.Text", surface) >= 4.5, $"{paletteFile}: App.Text on {surface} = {Contrast("App.Text", surface):0.00}");
            Assert.True(Contrast("App.TextSecondary", surface) >= 4.5, $"{paletteFile}: App.TextSecondary on {surface} = {Contrast("App.TextSecondary", surface):0.00}");
        }
        Assert.True(Contrast("App.AccentText", "App.Accent") >= 4.5, $"{paletteFile}: accent button text = {Contrast("App.AccentText", "App.Accent"):0.00}");
        Assert.True(Contrast("App.SelectionText", "App.Selection") >= 4.5, $"{paletteFile}: selection text = {Contrast("App.SelectionText", "App.Selection"):0.00}");
        Assert.True(Contrast("App.Link", "App.Surface") >= 4.5, $"{paletteFile}: link = {Contrast("App.Link", "App.Surface"):0.00}");
        Assert.True(Contrast("App.Link", "App.AccentSoft") >= 4.5, $"{paletteFile}: badge text = {Contrast("App.Link", "App.AccentSoft"):0.00}");
        Assert.True(Contrast("App.Success", "App.SuccessSoft") >= 4.5, $"{paletteFile}: success badge = {Contrast("App.Success", "App.SuccessSoft"):0.00}");
        Assert.True(Contrast("App.Danger", "App.DangerSoft") >= 4.5, $"{paletteFile}: danger badge = {Contrast("App.Danger", "App.DangerSoft"):0.00}");
        Assert.True(Contrast("App.Danger", "App.Surface") >= 3, $"{paletteFile}: danger = {Contrast("App.Danger", "App.Surface"):0.00}");
        Assert.True(Contrast("App.Accent", "App.Surface") >= 3, $"{paletteFile}: accent = {Contrast("App.Accent", "App.Surface"):0.00}");
        Assert.True(Contrast("App.ToolTipText", "App.ToolTipBackground") >= 4.5, $"{paletteFile}: tooltip = {Contrast("App.ToolTipText", "App.ToolTipBackground"):0.00}");
    }

    /// <summary>
    /// Light theme: sections must be easy to tell apart (3.0 raised these after user feedback; the
    /// 2.x values were 1.15 card/page, 1.42 border/card, 1.10 card header/body, 1.08 rail/page).
    /// </summary>
    [Fact]
    public void LightThemeSectionsAreClearlySeparated()
    {
        var palette = Keys(Path.Combine(AppSourceDir(), "Themes", "Colors.Light.xaml"));
        void AtLeast(string a, string b, double minimum)
        {
            double ratio = ContrastRatio(palette, a, b);
            Assert.True(ratio >= minimum, $"{a} vs {b} = {ratio:0.00}, expected at least {minimum}");
        }
        AtLeast("App.Surface", "App.WindowBackground", 1.25);   // cards stand out from the page
        AtLeast("App.Border", "App.Surface", 1.7);              // card outlines are visible
        AtLeast("App.Border", "App.WindowBackground", 1.35);
        AtLeast("App.CardHeader", "App.Surface", 1.15);         // card titles are a distinct band
        AtLeast("App.NavBackground", "App.Surface", 1.4);       // navigation rail vs. content cards
        AtLeast("App.HeaderBackground", "App.WindowBackground", 1.15); // page header vs. content area
    }

    /// <summary>WCAG contrast ratio of two palette colors.</summary>
    private static double ContrastRatio(Dictionary<string, XElement> palette, string first, string second)
    {
        double L(string key)
        {
            string hex = palette[key].Attribute("Color")!.Value.TrimStart('#');
            if (hex.Length == 8) hex = hex[2..];
            double Channel(int i)
            {
                double c = int.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber) / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
        }
        double a = L(first), b = L(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
