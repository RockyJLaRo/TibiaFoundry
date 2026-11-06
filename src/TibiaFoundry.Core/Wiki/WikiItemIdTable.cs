using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Net;

namespace TibiaFoundry.Core.Wiki;

/// <summary>Where a wiki item table came from (stored as a number in the cache: add new values at the end).</summary>
public enum WikiTableSource { Api, RenderPage, CustomUrl, Cache, Articles }

/// <summary>Object id → article name, as TibiaWiki's "Item IDs" page lists them.</summary>
/// <param name="FetchedAtUtc">When the table was downloaded (for cached tables: the original download time).</param>
/// <param name="Notice">Set when the table is usable but the user should know something (e.g. offline, cache used).</param>
public sealed record WikiItemIds(IReadOnlyDictionary<uint, string> Names, IReadOnlyList<string> Log, WikiTableSource Source,
    DateTime FetchedAtUtc, string? Notice = null);

/// <summary>
/// Downloads TibiaWiki's "Item IDs" table (used to name items in the all-NPC Lua export).
/// </summary>
/// <remarks>
/// Resilience (audit finding A16):
/// <list type="bullet">
/// <item>The documented MediaWiki API is used: first the item articles the page lists (since 7.0.1: the wiki can
///   no longer build the page in time), then the page itself; the classic render URL is the last fallback.</item>
/// <item>When TibiaWiki does not answer at all, the other addresses (on the same site) are not tried.</item>
/// <item>Parsing locates rows and cells by structure (any attributes, any column order) and the
///   result must be plausible (<see cref="MinimumExpectedIds"/>); a redesigned page is reported
///   instead of silently producing a partial table.</item>
/// <item>A successful download is cached; a fresh cache avoids repeated downloads, and a stale
///   cache is used (and announced) when the wiki cannot be reached.</item>
/// </list>
/// The original blocked the UI thread with <c>.Result</c> and crashed on any network error.
/// </remarks>
public static partial class WikiItemIdTable
{
    public const string DefaultUrl = ExternalEndpoints.ItemIdsRenderUrl;

    /// <summary>The live page lists well over 10,000 IDs; far fewer means the parser no longer understands the page.</summary>
    public const int MinimumExpectedIds = 1000;

    /// <summary>A cached table younger than this is used without asking the wiki again.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    public const string CacheFileName = "wiki-item-ids.json";

    [GeneratedRegex(@"<tr\b[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Row();

    [GeneratedRegex(@"<t([dh])\b[^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Cell();

    [GeneratedRegex(@"<a\b[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Link();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    /// <summary>A cell made only of IDs: "3031", "2160, 2161", "24845-24860", or with "." / ";" separators (typos on the page).</summary>
    [GeneratedRegex(@"^\s*(\d[\d\s,.;\-–]*)(.*)$", RegexOptions.Singleline)]
    private static partial Regex IdList();

    [GeneratedRegex(@"(\d+)\s*[-–]\s*(\d+)|(\d+)")]
    private static partial Regex IdToken();

    /// <summary>
    /// Request limits for the wiki. The Item IDs page is large (about 1.2 MB of HTML) and the API
    /// parses it on demand: a cold parse took about 40 s on 2026-10-12, a cached one 1.5 s. Fandom's
    /// cache can also give up on a cold parse with "503 Backend fetch failed" after about 16 s while the
    /// wiki finishes the parse in the background (twice in a row on 2026-10-14; a request about a
    /// minute later took 1 s). So the timeout is longer than the default, and a failed request is
    /// tried twice more, 20 and 40 seconds later (the operation can be cancelled meanwhile). Since
    /// 2026-10-15 every request fails this way (the page is now only the fallback of the articles).
    /// </summary>
    public static FetchOptions WikiFetchOptions { get; } = FetchOptions.Default with { Timeout = TimeSpan.FromSeconds(120), MaxAttempts = 3, BackoffBase = TimeSpan.FromSeconds(20) };

    /// <summary>Longest ID range accepted from one cell (protects against typos such as "100-100000").</summary>
    public const int MaxRangeLength = 1000;

    /// <summary>
    /// Returns the item table: from a fresh cache, otherwise downloaded (the item articles, then the
    /// page through the API, then the render page; or <paramref name="customUrl"/> when the user set
    /// one), otherwise from a stale cache with a notice. Throws <see cref="InvalidDataException"/>
    /// only when no usable table exists at all.
    /// </summary>
    public static async Task<WikiItemIds> GetAsync(HttpClient http, string? cacheDirectory, string? customUrl = null, bool forceRefresh = false,
        FetchOptions? options = null, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        string? cacheFile = cacheDirectory == null ? null : Path.Combine(cacheDirectory, CacheFileName);
        var cached = cacheFile != null ? ReadCache(cacheFile) : null;
        bool custom = !string.IsNullOrWhiteSpace(customUrl) && !string.Equals(customUrl.Trim(), DefaultUrl, StringComparison.OrdinalIgnoreCase);
        if (!forceRefresh && !custom && cached != null && DateTime.UtcNow - cached.FetchedAtUtc < CacheLifetime) return cached;

        var errors = new List<string>();
        WikiTableSource[] sources = custom ? [WikiTableSource.CustomUrl] : [WikiTableSource.Articles, WikiTableSource.Api, WikiTableSource.RenderPage];
        foreach (var source in sources)
        {
            try
            {
                var parsed = await ReadAsync(http, source, customUrl, options, progress, ct).ConfigureAwait(false) with { Source = source, FetchedAtUtc = DateTime.UtcNow };
                if (cacheFile != null) WriteCache(cacheFile, parsed);
                return parsed;
            }
            catch (Exception ex) when (ex is HttpFetchException or InvalidDataException or JsonException)
            {
                errors.Add($"{Describe(source)}: {ex.Message}");
                AppLog.Warn($"TibiaWiki item IDs via {source} failed: {ex.Message}");
                // No answer at all (offline, unknown host, timeouts): the other addresses are on the same site.
                if (ex is HttpFetchException { Transient: true, StatusCode: null }) break;
            }
        }

        if (cached != null)
        {
            return cached with
            {
                Notice = $"TibiaWiki could not be used ({string.Join("; ", errors)}). Using the copy downloaded on {cached.FetchedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}.",
            };
        }
        throw new InvalidDataException("The TibiaWiki item list could not be obtained: " + string.Join("; ", errors));
    }

    private static async Task<WikiItemIds> ReadAsync(HttpClient http, WikiTableSource source, string? customUrl, FetchOptions? options,
        IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        if (source == WikiTableSource.Articles) return await FromArticlesAsync(http, options, progress, ct: ct).ConfigureAwait(false);
        progress?.Report(new OperationProgress(0, 0, source == WikiTableSource.CustomUrl ? "Downloading the item list" : "Downloading TibiaWiki's Item IDs page"));
        var uri = source switch
        {
            WikiTableSource.CustomUrl => new Uri(customUrl!.Trim()),
            WikiTableSource.Api => ExternalEndpoints.ItemIdsApiUrl,
            _ => new Uri(ExternalEndpoints.ItemIdsRenderUrl),
        };
        string body = await HttpFetcher.GetStringAsync(http, uri, options ?? WikiFetchOptions, ct).ConfigureAwait(false);
        return Parse(source == WikiTableSource.Api ? HtmlFromApiResponse(body) : body);
    }

    private static string Describe(WikiTableSource source) => source switch
    {
        WikiTableSource.Articles => "item articles",
        WikiTableSource.Api => "Item IDs page",
        WikiTableSource.RenderPage => "Item IDs page (render address)",
        _ => "address entered",
    };

    /// <summary>Extracts the page HTML from a MediaWiki "action=parse" JSON response (formatversion 1 or 2).</summary>
    public static string HtmlFromApiResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        ThrowIfApiError(root);
        if (!root.TryGetProperty("parse", out var parse) || !parse.TryGetProperty("text", out var text))
            throw new InvalidDataException("TibiaWiki API answer has no page text (the API format may have changed).");
        string? html = text.ValueKind switch
        {
            JsonValueKind.String => text.GetString(),
            JsonValueKind.Object when text.TryGetProperty("*", out var star) => star.GetString(),
            _ => null,
        };
        return html ?? throw new InvalidDataException("TibiaWiki API answer has page text in an unexpected form.");
    }

    /// <summary>
    /// Parses every table row that has a linked name cell and a cell containing only IDs
    /// ("3031" or "2160, 2161"). Column order, attributes and header rows do not matter.
    /// </summary>
    public static WikiItemIds Parse(string html, int minimumIds = MinimumExpectedIds)
    {
        var names = new Dictionary<uint, string>();
        var log = new List<string>();
        int rows = 0, rowsWithoutIds = 0;
        var irregular = new List<string>();
        foreach (Match row in Row().Matches(html))
        {
            rows++;
            string? name = null;
            string? ids = null;
            foreach (Match cell in Cell().Matches(row.Groups[1].Value))
            {
                if (cell.Groups[1].Value.Equals("h", StringComparison.OrdinalIgnoreCase)) continue;
                string content = cell.Groups[2].Value;
                if (name == null && Link().Match(content) is { Success: true } link)
                {
                    name = WebUtility.HtmlDecode(Tag().Replace(link.Groups[1].Value, "")).Trim();
                    continue;
                }
                string plain = WebUtility.HtmlDecode(Tag().Replace(content, "")).Trim();
                if (ids == null && IdList().Match(plain) is { Success: true } idCell)
                {
                    ids = idCell.Groups[1].Value;
                    // Irregular cells ("27350m 27351"): like the original, use the leading IDs and say what was ignored.
                    if (idCell.Groups[2].Value.Trim() is { Length: > 0 } rest) irregular.Add($"\"{plain}\" (ignored \"{rest}\")");
                }
            }
            if (string.IsNullOrEmpty(name)) continue;
            if (ids == null) { rowsWithoutIds++; continue; }
            AddIds(names, log, name, ids);
        }
        if (names.Count == 0)
            throw new InvalidDataException($"The TibiaWiki \"Item IDs\" page could not be parsed: no item rows found in {rows} table rows (its layout may have changed).");
        if (names.Count < minimumIds)
            throw new InvalidDataException($"The TibiaWiki \"Item IDs\" page yielded only {names.Count} IDs (at least {minimumIds} expected); its layout may have changed, so the result is not used.");
        if (rowsWithoutIds > 0) log.Add($"{Plural.Count(rowsWithoutIds, "linked row")} had no ID cell and were ignored.");
        foreach (string cell in irregular) log.Add("Irregular ID cell " + cell + ".");
        return new WikiItemIds(names, log, WikiTableSource.RenderPage, DateTime.UtcNow);
    }

    /// <summary>Adds the IDs of one entry ("3031", "2160, 2161", "24845-24860"); an ID already taken keeps its first name.</summary>
    private static void AddIds(Dictionary<uint, string> names, List<string> log, string name, string ids)
    {
        foreach (Match token in IdToken().Matches(ids))
        {
            if (token.Groups[3].Success)
            {
                if (uint.TryParse(token.Groups[3].Value, out uint single)) Add(single);
                continue;
            }
            if (!uint.TryParse(token.Groups[1].Value, out uint from) || !uint.TryParse(token.Groups[2].Value, out uint to)) continue;
            if (to < from || to - from >= MaxRangeLength)
            {
                log.Add($"\"{name}\": ignored implausible ID range {token.Value}.");
                continue;
            }
            for (uint id = from; id <= to; id++) Add(id);
            log.Add($"\"{name}\": expanded ID range {token.Value} ({to - from + 1} IDs).");
        }

        void Add(uint id)
        {
            if (!names.TryAdd(id, name)) log.Add($"ID {id} appears more than once (\"{names[id]}\" and \"{name}\"); the first is used.");
        }
    }

    private sealed record CacheFile(DateTime FetchedAtUtc, WikiTableSource Source, Dictionary<uint, string> Names, List<string> Log);

    /// <summary>The cached table in <paramref name="cacheDirectory"/>, whatever its age (null when there is none); never goes online.</summary>
    public static WikiItemIds? TryReadCache(string? cacheDirectory) =>
        cacheDirectory == null ? null : ReadCache(Path.Combine(cacheDirectory, CacheFileName));

    private static WikiItemIds? ReadCache(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<CacheFile>(File.ReadAllBytes(path));
            if (data == null || data.Names.Count < MinimumExpectedIds) return null;
            return new WikiItemIds(data.Names, data.Log, WikiTableSource.Cache, data.FetchedAtUtc);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Warn($"Ignoring unreadable wiki cache {path}: {ex.Message}");
            return null;
        }
    }

    private static void WriteCache(string path, WikiItemIds table)
    {
        try
        {
            var data = new CacheFile(table.FetchedAtUtc, table.Source, table.Names.ToDictionary(kv => kv.Key, kv => kv.Value), table.Log.ToList());
            AtomicFile.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(data));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Could not cache the wiki item table: {ex.Message}");
        }
    }
}
