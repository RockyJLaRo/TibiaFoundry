using System.Globalization;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Theming;

/// <summary>An opaque interface color (sRGB, 8 bits per channel) with HSL and contrast helpers.</summary>
public readonly record struct ThemeColor(byte R, byte G, byte B)
{
    /// <summary>"#RRGGBB" (upper case), the form stored in the settings.</summary>
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public override string ToString() => Hex;

    /// <summary>
    /// Parses "#RRGGBB", "RRGGBB", "#RGB" or "RGB" (case-insensitive, surrounding spaces ignored).
    /// "#AARRGGBB" is accepted only when fully opaque (AA = FF), because interface colors must not be
    /// transparent. Returns false with a message that can be shown to the user.
    /// </summary>
    public static bool TryParse(string? text, out ThemeColor color, out string? problem)
    {
        color = default;
        problem = null;
        string t = (text ?? "").Trim();
        if (t.StartsWith('#')) t = t[1..];
        if (t.Length == 8 && t.StartsWith("FF", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        else if (t.Length == 8)
        {
            problem = L.T("ThemeColor.InterfaceColorsCannotBeTransparentUse");
            return false;
        }
        if (t.Length == 3) t = string.Concat(t.Select(c => new string(c, 2)));
        if (t.Length != 6 || !uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
        {
            problem = L.F("ThemeColor.0IsNotAColorEnter", text);
            return false;
        }
        color = new ThemeColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static bool TryParse(string? text, out ThemeColor color) => TryParse(text, out color, out _);

    public static ThemeColor Parse(string text) => TryParse(text, out var c, out string? problem) ? c : throw new FormatException(problem);

    // ------------------------------------------------------------------ HSL

    /// <summary>Hue in degrees [0, 360), saturation and lightness in [0, 1].</summary>
    public (double H, double S, double L) ToHsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2;
        if (max - min < 1e-9) return (0, 0, l);
        double d = max - min;
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60 % 360, s, l);
    }

    public static ThemeColor FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        if (s < 1e-9)
        {
            byte v = ToByte(l);
            return new ThemeColor(v, v, v);
        }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return new ThemeColor(ToByte(HueToRgb(p, q, h + 1.0 / 3)), ToByte(HueToRgb(p, q, h)), ToByte(HueToRgb(p, q, h - 1.0 / 3)));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);

    /// <summary>The same color with a lightness change (−1…1).</summary>
    public ThemeColor AdjustLightness(double delta)
    {
        var (h, s, l) = ToHsl();
        return FromHsl(h, s, l + delta);
    }

    /// <summary>Linear blend: 0 = this color, 1 = <paramref name="other"/>.</summary>
    public ThemeColor Mix(ThemeColor other, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        byte M(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return new ThemeColor(M(R, other.R), M(G, other.G), M(B, other.B));
    }

    // ------------------------------------------------------------------ contrast (WCAG 2.1)

    /// <summary>Relative luminance as defined by WCAG 2.1.</summary>
    public double Luminance
    {
        get
        {
            static double C(byte v)
            {
                double c = v / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * C(R) + 0.7152 * C(G) + 0.0722 * C(B);
        }
    }

    /// <summary>WCAG contrast ratio (1–21).</summary>
    public static double Contrast(ThemeColor a, ThemeColor b)
    {
        double la = a.Luminance, lb = b.Luminance;
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>True for colors on which dark text reads better than light text.</summary>
    public bool IsLight => Contrast(this, Black) >= Contrast(this, White);

    public static ThemeColor White { get; } = new(255, 255, 255);
    public static ThemeColor Black { get; } = new(0, 0, 0);

    /// <summary>White or black, whichever contrasts more with this color.</summary>
    public ThemeColor BestTextColor => IsLight ? Black : White;
}
