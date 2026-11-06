using System.Globalization;
using System.Text.RegularExpressions;

namespace TibiaFoundry.Core.Wiki;

/// <summary>TibiaWiki article-style name casing (original <c>Helper.CapitalizeName</c>).</summary>
public static partial class NameFormatter
{
    // Words kept lower-case inside titles. Order matters: longer phrases first (as in the original).
    [GeneratedRegex("( To The | In The | In A | Of The | Of A | Of An | On A | Of | The | From The | From | And | With A | With )")]
    private static partial Regex LowerCaseWords();

    /// <summary>"GOLD COIN OF THE KING" → "Gold Coin of the King".</summary>
    public static string Capitalize(string name)
    {
        // The original used CurrentCulture; invariant casing keeps results identical on every machine.
        string title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
        return LowerCaseWords().Replace(title, m => m.Value.ToLowerInvariant());
    }

    /// <summary>Title case used by the NPC trade export (<c>TextInfo.ToTitleCase</c> without lower-casing first).</summary>
    public static string TitleCase(string name) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
}
