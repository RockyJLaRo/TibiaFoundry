using System.Globalization;

namespace TibiaFoundry.Core.Wiki;

/// <summary>
/// Orders names the way the original (on .NET Framework / Windows NLS) did: a culture-aware
/// "word sort" in which apostrophes and hyphens carry no weight ("Catnip" &lt; "Cat's Paw").
/// Modern .NET uses ICU collation, which treats those characters as significant, so the rule is
/// reproduced explicitly instead of depending on the platform's collation library.
/// </summary>
public sealed class WikiNameComparer : IComparer<string>
{
    public static readonly WikiNameComparer Instance = new();

    private static readonly CompareInfo Culture = CultureInfo.InvariantCulture.CompareInfo;

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        int primary = Culture.Compare(Strip(x), Strip(y), CompareOptions.None);
        return primary != 0 ? primary : Culture.Compare(x, y, CompareOptions.None) is var c && c != 0 ? c : string.CompareOrdinal(x, y);
    }

    private static string Strip(string s) => s.IndexOfAny(['\'', '-']) < 0 ? s : s.Replace("'", "").Replace("-", "");
}
