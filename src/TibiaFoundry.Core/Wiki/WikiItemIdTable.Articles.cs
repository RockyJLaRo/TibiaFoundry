using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Net;

namespace TibiaFoundry.Core.Wiki;

/// <summary>An item article: its title, its wikitext (null if the API did not send it) and those of its categories that the "Item IDs" page's query tests.</summary>
public sealed record WikiArticle(string Title, string? Wikitext, IReadOnlyCollection<string> Categories);

/// <summary>
/// The "Item IDs" table built from the item articles themselves. The page is a query over the articles
/// (<see cref="ExternalEndpoints.ItemArticlesCategory"/>); since 2026-10-15 the wiki cannot build it within
/// its own time limit, while the articles can be read through the API in about 190 requests of 50.
/// On 2026-10-15 the result was identical to the page as downloaded on 2026-10-14: 13,875 IDs, the same
/// names, the same duplicates and the same irregular entry.
/// </summary>
public static partial class WikiItemIdTable
{
    /// <summary>
    /// Limits for one batch of 50 articles (about 50 KB, under a second on 2026-10-15): the default timeout,
    /// and two more attempts about 5 and 10 seconds later.
    /// </summary>
    public static FetchOptions ArticleFetchOptions { get; } = FetchOptions.Default with { BackoffBase = TimeSpan.FromSeconds(5) };

    /// <summary>More batches than this means the API's continuation does not end (187 were needed on 2026-10-15).</summary>
    public const int MaxArticleRequests = 2000;

    [GeneratedRegex(@"<!--.*?(?:-->|$)", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Reads every article of the category through the API, then builds the table (<see cref="FromArticles"/>).</summary>
    public static async Task<WikiItemIds> FromArticlesAsync(HttpClient http, FetchOptions? options = null, IProgress<OperationProgress>? progress = null,
        int minimumIds = MinimumExpectedIds, CancellationToken ct = default)
    {
        options ??= ArticleFetchOptions;
        progress?.Report(new OperationProgress(0, 0, "Reading TibiaWiki's item articles"));
        int total = CategorySize(await HttpFetcher.GetStringAsync(http, ExternalEndpoints.ItemArticlesCountUrl, options, ct).ConfigureAwait(false));
        var articles = new Dictionary<long, ArticleData>();
        IReadOnlyList<KeyValuePair<string, string>> continuation = [];
        for (int request = 1; ; request++)
        {
            if (request > MaxArticleRequests)
                throw new InvalidDataException($"TibiaWiki's API did not finish listing the item articles after {MaxArticleRequests:N0} requests.");
            string json = await HttpFetcher.GetStringAsync(http, ExternalEndpoints.ItemArticlesUrl(continuation), options, ct).ConfigureAwait(false);
            var next = ReadArticleBatch(json, articles);
            int shown = Math.Max(total, articles.Count);
            progress?.Report(new OperationProgress(articles.Count, shown, $"Reading TibiaWiki's item articles: {articles.Count:N0} of {shown:N0}"));
            if (next == null) break;
            if (next.SequenceEqual(continuation)) throw new InvalidDataException("TibiaWiki's API sent the same batch of item articles twice.");
            continuation = next;
        }
        return FromArticles(articles.Values.Select(a => new WikiArticle(a.Title, a.Wikitext, a.Categories)), minimumIds);
    }

    /// <summary>
    /// The table from the articles, by the page's query: articles in <see cref="ExternalEndpoints.ObjectsCategory"/>
    /// and not in <see cref="ExternalEndpoints.DeprecatedCategory"/> or <see cref="ExternalEndpoints.ListsCategory"/>,
    /// in the page's order (by title, so an ID claimed twice goes to the same article as on the page), with the
    /// <c>itemid</c> of their Infobox Object read like the page's ID cells.
    /// </summary>
    public static WikiItemIds FromArticles(IEnumerable<WikiArticle> articles, int minimumIds = MinimumExpectedIds)
    {
        var names = new Dictionary<uint, string>();
        var log = new List<string>();
        var irregular = new List<string>();
        int read = 0, withoutIds = 0, unread = 0;
        foreach (var article in articles.OrderBy(a => a.Title.Replace(' ', '_'), StringComparer.Ordinal))
        {
            read++;
            if (!article.Categories.Contains(ExternalEndpoints.ObjectsCategory) || article.Categories.Contains(ExternalEndpoints.DeprecatedCategory) ||
                article.Categories.Contains(ExternalEndpoints.ListsCategory)) continue;
            if (article.Wikitext == null) { unread++; continue; }
            string? value = TemplateParameter(article.Wikitext, "Infobox Object", "itemid");
            string plain = value == null ? "" : WebUtility.HtmlDecode(Tag().Replace(value, "")).Trim();
            if (IdList().Match(plain) is not { Success: true } ids) { withoutIds++; continue; }
            if (ids.Groups[2].Value.Trim() is { Length: > 0 } rest) irregular.Add($"\"{plain}\" (ignored \"{rest}\")");
            AddIds(names, log, article.Title, ids.Groups[1].Value);
        }
        if (names.Count == 0)
            throw new InvalidDataException($"TibiaWiki's item articles gave no IDs ({read} articles read); the wiki may have changed how items are described.");
        if (names.Count < minimumIds)
            throw new InvalidDataException($"TibiaWiki's item articles yielded only {names.Count} IDs (at least {minimumIds} expected); the wiki may have changed how items are described, so the result is not used.");
        if (withoutIds > 0) log.Add($"{Plural.Count(withoutIds, "article")} had no item ID and were ignored.");
        if (unread > 0) log.Add($"{Plural.Count(unread, "article")} {(unread == 1 ? "was" : "were")} not sent by the wiki and were ignored.");
        foreach (string entry in irregular) log.Add("Irregular item ID " + entry + ".");
        return new WikiItemIds(names, log, WikiTableSource.Articles, DateTime.UtcNow);
    }

    /// <summary>
    /// The value of <paramref name="parameter"/> in the first call of <paramref name="template"/> in
    /// <paramref name="wikitext"/>, or null. HTML comments are removed first; templates and links inside the
    /// call are kept whole. Template names are compared as the wiki does (" " and "_" alike, the first letter
    /// in either case); parameter names exactly, after trimming.
    /// </summary>
    public static string? TemplateParameter(string wikitext, string template, string parameter)
    {
        string text = Comment().Replace(wikitext, "");
        for (int start = text.IndexOf("{{", StringComparison.Ordinal); start >= 0; start = text.IndexOf("{{", start + 2, StringComparison.Ordinal))
        {
            var parts = TemplateParts(text, start + 2);
            if (parts == null) return null;
            if (!SameTitle(parts[0], template)) continue;
            foreach (string part in parts.Skip(1))
            {
                int equals = part.IndexOf('=');
                if (equals > 0 && part[..equals].Trim() == parameter) return part[(equals + 1)..];
            }
            return null;
        }
        return null;
    }

    /// <summary>The name and the parameters of the template call whose name starts at <paramref name="from"/>; null if it is not closed.</summary>
    private static List<string>? TemplateParts(string text, int from)
    {
        var parts = new List<string>();
        int templates = 0, links = 0, partStart = from;
        for (int i = from; i < text.Length;)
        {
            var rest = text.AsSpan(i);
            if (rest.StartsWith("{{")) { templates++; i += 2; continue; }
            if (rest.StartsWith("}}"))
            {
                if (templates == 0) { parts.Add(text[partStart..i]); return parts; }
                templates--; i += 2; continue;
            }
            if (rest.StartsWith("[[")) { links++; i += 2; continue; }
            if (rest.StartsWith("]]")) { if (links > 0) links--; i += 2; continue; }
            if (text[i] == '|' && templates == 0 && links == 0) { parts.Add(text[partStart..i]); partStart = i + 1; }
            i++;
        }
        return null;
    }

    private static bool SameTitle(string a, string b)
    {
        string x = Title(a), y = Title(b);
        return x.Length == y.Length && x.Length > 0 && char.ToUpperInvariant(x[0]) == char.ToUpperInvariant(y[0]) &&
               string.CompareOrdinal(x, 1, y, 1, x.Length - 1) == 0;

        static string Title(string s)
        {
            s = Whitespace().Replace(s.Replace('_', ' '), " ").Trim();
            return s.StartsWith("Template:", StringComparison.OrdinalIgnoreCase) ? s[9..].TrimStart() : s;
        }
    }

    private sealed class ArticleData(string title)
    {
        public string Title { get; } = title;
        public string? Wikitext { get; set; }
        public HashSet<string> Categories { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>The number of pages in the category, from a "categoryinfo" answer.</summary>
    private static int CategorySize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        ThrowIfApiError(doc.RootElement);
        foreach (var page in Pages(doc.RootElement))
        {
            if (page.TryGetProperty("categoryinfo", out var info) && info.TryGetProperty("pages", out var pages) && pages.TryGetInt32(out int count) && count > 0)
                return count;
        }
        throw new InvalidDataException($"TibiaWiki's \"{ExternalEndpoints.ItemArticlesCategory}\" is empty or no longer exists; the wiki may have changed how items are listed.");
    }

    /// <summary>Adds one batch to <paramref name="articles"/> (an article can come in parts); returns the API's continuation, or null after the last batch.</summary>
    private static IReadOnlyList<KeyValuePair<string, string>>? ReadArticleBatch(string json, Dictionary<long, ArticleData> articles)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        ThrowIfApiError(root);
        foreach (var page in Pages(root))
        {
            if (!page.TryGetProperty("pageid", out var pageId) || !pageId.TryGetInt64(out long id) || !page.TryGetProperty("title", out var title)) continue;
            if (!articles.TryGetValue(id, out var article)) articles[id] = article = new ArticleData(title.GetString() ?? "");
            if (page.TryGetProperty("revisions", out var revisions) && revisions.ValueKind == JsonValueKind.Array && revisions.GetArrayLength() > 0 &&
                revisions[0].TryGetProperty("slots", out var slots) && slots.TryGetProperty("main", out var main) && main.TryGetProperty("content", out var content))
                article.Wikitext = content.GetString();
            if (page.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array)
            {
                foreach (var category in categories.EnumerateArray())
                    if (category.TryGetProperty("title", out var name) && name.GetString() is { } text) article.Categories.Add(text);
            }
        }
        if (!root.TryGetProperty("continue", out var next) || next.ValueKind != JsonValueKind.Object) return null;
        return next.EnumerateObject().Select(p => new KeyValuePair<string, string>(p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText())).ToList();
    }

    /// <summary>The "pages" of a query answer (an array in format version 2, an object in version 1).</summary>
    private static IEnumerable<JsonElement> Pages(JsonElement root)
    {
        if (!root.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return [];
        return pages.ValueKind switch
        {
            JsonValueKind.Array => pages.EnumerateArray().ToList(),
            JsonValueKind.Object => pages.EnumerateObject().Select(p => p.Value).ToList(),
            _ => [],
        };
    }

    private static void ThrowIfApiError(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
            throw new InvalidDataException("TibiaWiki API error: " + (error.TryGetProperty("info", out var info) ? info.GetString() : error.ToString()));
    }
}
