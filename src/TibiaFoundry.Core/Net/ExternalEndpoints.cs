using System.Globalization;

namespace TibiaFoundry.Core.Net;

/// <summary>
/// Every external address the program uses, in one place. If a site moves or changes its URL
/// scheme, this is the file to update (see docs/11_Maintenance_Guide.md §6).
/// </summary>
public static class ExternalEndpoints
{
    /// <summary>MediaWiki API of TibiaWiki (documented interface; preferred over scraping pages).</summary>
    public const string TibiaWikiApi = "https://tibia.fandom.com/api.php";

    /// <summary>Title of the wiki page that lists item names and their object IDs.</summary>
    public const string ItemIdsPageTitle = "Item_IDs";

    /// <summary>
    /// The category the "Item IDs" page lists its articles from. The page is a query over the item
    /// articles (a DPL table, checked 2026-10-15): articles in this category and in
    /// <see cref="ObjectsCategory"/>, not in <see cref="DeprecatedCategory"/> or <see cref="ListsCategory"/>,
    /// with the <c>itemid</c> of their Infobox Object.
    /// </summary>
    public const string ItemArticlesCategory = "Category:Objects with Object IDs";
    public const string ObjectsCategory = "Category:Objects";
    public const string DeprecatedCategory = "Category:Deprecated";
    public const string ListsCategory = "Category:Lists";

    /// <summary>The number of articles in <see cref="ItemArticlesCategory"/> (for progress).</summary>
    public static Uri ItemArticlesCountUrl => new($"{TibiaWikiApi}?action=query&prop=categoryinfo&titles={Uri.EscapeDataString(ItemArticlesCategory)}&format=json&formatversion=2");

    /// <summary>
    /// One batch of item articles with their wikitext and those of their categories that the page's query
    /// tests; 50 per request, the API's limit for page content. <paramref name="continuation"/> holds the
    /// API's "continue" values from the previous batch (none for the first).
    /// </summary>
    public static Uri ItemArticlesUrl(IEnumerable<KeyValuePair<string, string>> continuation)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new("action", "query"), new("format", "json"), new("formatversion", "2"),
            new("generator", "categorymembers"), new("gcmtitle", ItemArticlesCategory), new("gcmnamespace", "0"), new("gcmlimit", "50"),
            new("prop", "revisions|categories"), new("rvprop", "content"), new("rvslots", "main"),
            new("clcategories", $"{ObjectsCategory}|{DeprecatedCategory}|{ListsCategory}"), new("cllimit", "max"),
        };
        query.AddRange(continuation);
        return new(TibiaWikiApi + "?" + string.Join("&", query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))));
    }

    /// <summary>
    /// The "Item IDs" page's HTML via the API (JSON). The fallback when the articles cannot be read; then
    /// <see cref="ItemIdsRenderUrl"/>. Since 2026-10-15 the page is too slow for the wiki: building its
    /// table takes longer than the wiki's 16 s limit, and every request ends in "503 Backend fetch failed".
    /// </summary>
    public static Uri ItemIdsApiUrl => new($"{TibiaWikiApi}?action=parse&page={ItemIdsPageTitle}&prop=text&format=json&formatversion=2");

    /// <summary>
    /// The page's rendered HTML via the classic action=render URL (what the original 1.x program used).
    /// Kept as a fallback only: on 2026-10-12 it answered non-browser clients with a Cloudflare
    /// browser check (403, "cf-mitigated: challenge"), while the API above worked.
    /// </summary>
    public const string ItemIdsRenderUrl = "https://tibia.fandom.com/wiki/Item%20IDs?action=render";

    /// <summary>TibiaWiki, the canonical website of Tibia Foundry (also used in the attribution text).</summary>
    public const string TibiaWikiHome = Common.AppIdentity.Website;

    /// <summary>Tibia Foundry's own website (Home dashboard).</summary>
    public const string ProjectWebsite = "https://tibiafoundry.com/";

    /// <summary>The official list of Tibia fansites and the Fansite Programme on tibia.com (Home dashboard).</summary>
    public const string TibiaFansites = "https://www.tibia.com/community/?subtopic=fansites";

    /// <summary>tibiamaps.io map viewer at a world position (used for area label coordinates).</summary>
    public static string TibiaMapsUrl(long x, long y, long z) => string.Create(CultureInfo.InvariantCulture, $"https://tibiamaps.io/map#{x},{y},{z}:0");

    /// <summary>Default base address of the test-server asset files (from the original's hidden LZMA tool).</summary>
    public const string TestServerAssetsBase = "https://static.test.tibia.com/launcher/assets-test/assets/";

    // ------------------------------------------------------------------ About > Credits (exactly these three)
    public const string CreditTibia = "http://tibia.com/";
    public const string CreditCipSoft = "https://www.cipsoft.com/en/";
    public const string CreditTibiaWiki = "https://tibia.fandom.com/wiki/Main_Page";

    // ------------------------------------------------------------------ creator profile (About page)
    public const string CreatorTibiaWikiProfile = "https://tibia.fandom.com/wiki/User:RockyLaRo";
    public const string CreatorTibiaCharacter = "https://www.tibia.com/community/?name=Roc+ky";
    public const string CreatorYouTube = "https://www.youtube.com/@Roc_ky";
    public const string CreatorInstagram = "https://www.instagram.com/rocky_tibia/";
    public const string CreatorGitHub = "https://github.com/RockyJLaRo";
    public const string CreatorReddit = "https://www.reddit.com/user/Roc-ky/";

    // ------------------------------------------------------------------ About > Open-source acknowledgments
    public const string NekiroProfile = "https://github.com/nekiro";
    public const string NekiroRccEditor = "https://github.com/nekiro/Nekiro-Rcc-Editor";
    public const string ArchMinaProfile = "https://github.com/Arch-Mina";
    public const string ArchMinaClientConverter = "https://github.com/Arch-Mina/ClientConverter";
    public const string ArchMinaAssetsEditor = "https://github.com/Arch-Mina/Assets-Editor";
}
