using System.Net;
using System.Text;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.Net;
using TibiaFoundry.Core.Wiki;

namespace TibiaFoundry.Tests;

/// <summary>Answers requests from a script of responses (or exceptions); records every request.</summary>
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _script = new();
    public List<Uri> Requests { get; } = new();

    public ScriptedHandler Respond(HttpStatusCode code, string body = "", Action<HttpResponseMessage>? configure = null)
    {
        _script.Enqueue((_, _) =>
        {
            var r = new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8) };
            configure?.Invoke(r);
            return Task.FromResult(r);
        });
        return this;
    }

    public ScriptedHandler Throw(Exception ex)
    {
        _script.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(ex));
        return this;
    }

    public ScriptedHandler Hang()
    {
        _script.Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        if (_script.Count == 0) throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        return _script.Dequeue()(request, cancellationToken);
    }
}

/// <summary>
/// Network resilience (audit A15, A16) with scripted responses — routine runs never depend on a
/// live site. The optional live check is <see cref="LiveServiceChecks"/>.
/// </summary>
public sealed class NetworkTests
{
    private static readonly FetchOptions Fast = new() { BackoffBase = TimeSpan.FromMilliseconds(1), Timeout = TimeSpan.FromSeconds(5), MaxRetryAfter = TimeSpan.FromSeconds(2) };
    private static readonly Uri Url = new("https://example.test/file");

    [Fact]
    public async Task TransientFailuresAreRetriedWithBackoff()
    {
        var handler = new ScriptedHandler().Respond(HttpStatusCode.InternalServerError).Throw(new HttpRequestException("connection reset")).Respond(HttpStatusCode.OK, "payload");
        using var http = new HttpClient(handler);
        Assert.Equal("payload", await HttpFetcher.GetStringAsync(http, Url, Fast, TestContext.Current.CancellationToken));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task PermanentErrorsAreNotRetried()
    {
        var handler = new ScriptedHandler().Respond(HttpStatusCode.NotFound);
        using var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<HttpFetchException>(() => HttpFetcher.GetStringAsync(http, Url, Fast, TestContext.Current.CancellationToken));
        Assert.False(ex.Transient);
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RateLimitingHonorsRetryAfterWithinLimits()
    {
        var handler = new ScriptedHandler()
            .Respond(HttpStatusCode.TooManyRequests, configure: r => r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(50)))
            .Respond(HttpStatusCode.OK, "ok");
        using var http = new HttpClient(handler);
        Assert.Equal("ok", await HttpFetcher.GetStringAsync(http, Url, Fast, TestContext.Current.CancellationToken));

        var tooLong = new ScriptedHandler().Respond(HttpStatusCode.TooManyRequests,
            configure: r => r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10)));
        using var http2 = new HttpClient(tooLong);
        var ex = await Assert.ThrowsAsync<HttpFetchException>(() => HttpFetcher.GetStringAsync(http2, Url, Fast, TestContext.Current.CancellationToken));
        Assert.True(ex.Transient);
        Assert.Contains("rate limited", ex.Message);
        Assert.Single(tooLong.Requests); // does not hammer the server
    }

    [Fact]
    public async Task RetryAfterIsTheShortestWaitNotAReplacementForTheBackoff()
    {
        // TibiaWiki's "503 Backend fetch failed" says Retry-After: 5, too soon for the wiki to build its page
        // (on 2026-10-15 the three attempts came 5 s apart instead of 20 and 40 s).
        var handler = new ScriptedHandler()
            .Respond(HttpStatusCode.ServiceUnavailable, configure: r => r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(10)))
            .Respond(HttpStatusCode.OK, "ok");
        using var http = new HttpClient(handler);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal("ok", await HttpFetcher.GetStringAsync(http, Url, Fast with { BackoffBase = TimeSpan.FromMilliseconds(500) }, TestContext.Current.CancellationToken));
        Assert.True(watch.ElapsedMilliseconds >= 350, $"waited {watch.ElapsedMilliseconds} ms; the backoff is at least 400 ms");
    }

    [Fact]
    public async Task UnresponsiveServersTimeOutAndGiveUp()
    {
        var handler = new ScriptedHandler().Hang().Hang();
        using var http = new HttpClient(handler);
        var options = Fast with { Timeout = TimeSpan.FromMilliseconds(100), MaxAttempts = 2 };
        var ex = await Assert.ThrowsAsync<HttpFetchException>(() => HttpFetcher.GetStringAsync(http, Url, options, TestContext.Current.CancellationToken));
        Assert.True(ex.Transient);
        Assert.Contains("no answer", ex.Message);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task OversizedResponsesAreRejected()
    {
        var handler = new ScriptedHandler().Respond(HttpStatusCode.OK, new string('x', 5000));
        using var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<HttpFetchException>(() => HttpFetcher.GetStringAsync(http, Url, Fast with { MaxBytes = 1000 }, TestContext.Current.CancellationToken));
        Assert.False(ex.Transient);
    }

    [Fact]
    public async Task CancellationStopsImmediately()
    {
        var handler = new ScriptedHandler().Hang();
        using var http = new HttpClient(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HttpFetcher.GetStringAsync(http, Url, Fast with { Timeout = TimeSpan.FromSeconds(30) }, cts.Token));
    }

    [Fact]
    public async Task FailedDownloadsNeverReplaceExistingFiles()
    {
        string dir = TestData.NewTempDirectory("download");
        string target = Path.Combine(dir, "asset.dat");
        File.WriteAllText(target, "previous version");
        var handler = new ScriptedHandler().Respond(HttpStatusCode.OK, new string('x', 5000));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpFetchException>(() => HttpFetcher.DownloadFileAsync(http, Url, target, Fast with { MaxBytes = 100 }, ct: TestContext.Current.CancellationToken));
        Assert.Equal("previous version", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(dir));
    }

    [Fact]
    public async Task DownloadListKeepsEveryFileInsideTheOutputFolder()
    {
        string dir = TestData.NewTempDirectory("dllist");
        string list = Path.Combine(dir, "list.txt");
        File.WriteAllLines(list, ["a.dat", @"..\..\escape.dat", "sub/b.dat", "# comment", "", "a.dat"]);
        string output = Path.Combine(dir, "out");
        var handler = new ScriptedHandler().Respond(HttpStatusCode.OK, "A").Respond(HttpStatusCode.OK, "E").Respond(HttpStatusCode.NotFound);
        using var http = new HttpClient(handler);
        var result = await LzmaFileTool.DownloadListAsync(http, list, "https://example.test/assets", output, null, TestContext.Current.CancellationToken, Fast);
        Assert.Equal(2, result.Succeeded);
        Assert.Single(result.Failures);
        Assert.Equal(3, handler.Requests.Count);          // duplicates and comments are not requested
        Assert.All(Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Where(f => f != list), f => Assert.StartsWith(output, f));
        Assert.True(File.Exists(Path.Combine(output, "escape.dat")));
    }

    [Fact]
    public async Task BrowserChallengesAreReportedAndNotRetried()
    {
        // Seen on 2026-10-12: TibiaWiki's action=render URL answers non-browser clients with a
        // Cloudflare challenge. That is a refusal, not a transient error, and is never worked around.
        var handler = new ScriptedHandler()
            .Respond(HttpStatusCode.Forbidden, "Just a moment...", r => r.Headers.Add("cf-mitigated", "challenge"))
            .Respond(HttpStatusCode.ServiceUnavailable, "Just a moment...", r => r.Headers.Add("cf-mitigated", "challenge"));
        using var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<HttpFetchException>(() => HttpFetcher.GetStringAsync(http, Url, Fast, TestContext.Current.CancellationToken));
        Assert.Contains("browser check", ex.Message);
        Assert.False(ex.Transient);
        Assert.Single(handler.Requests);
        ex = await Assert.ThrowsAsync<HttpFetchException>(() => HttpFetcher.GetStringAsync(http, Url, Fast, TestContext.Current.CancellationToken));
        Assert.False(ex.Transient);                         // even with a 5xx status
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void WikiRequestsAllowForSlowPageParsing()
    {
        Assert.True(WikiItemIdTable.WikiFetchOptions.Timeout >= TimeSpan.FromSeconds(90));
        string dir = TestData.NewTempDirectory("httptimeout");
        var settings = new TibiaFoundry.Core.Settings.AppSettings { LoadDataAutomatically = false };
        settings.Normalize();
        using var services = new TibiaFoundry.App.Services.AppServices(new TibiaFoundry.Core.Settings.SettingsStore(Path.Combine(dir, "settings.json")), settings);
        Assert.Equal(Timeout.InfiniteTimeSpan, services.Http.Timeout);   // per-request limits apply, not a client-wide 60 s
    }

    [Fact]
    public void RequestsIdentifyTheToolAndItsVersion()
    {
        // MediaWiki etiquette: a descriptive User-Agent with a contact address (audit A30: it said "2.0" in every version).
        string dir = TestData.NewTempDirectory("ua");
        var settings = new TibiaFoundry.Core.Settings.AppSettings { LoadDataAutomatically = false };
        settings.Normalize();
        using var services = new TibiaFoundry.App.Services.AppServices(new TibiaFoundry.Core.Settings.SettingsStore(Path.Combine(dir, "settings.json")), settings);
        string version = TibiaFoundry.Core.Common.AppIdentity.Version;
        Assert.Equal($"TibiaFoundry/{version} (+{ExternalEndpoints.TibiaWikiHome})", services.Http.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Matches(@"^\d+\.\d+$", version);   // "9.0", not "9.0.0"
    }

    [Fact]
    public async Task DownloadedClientFilesAreCheckedAgainstTheHashInTheirNames()
    {
        static string Sha(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        string dir = TestData.NewTempDirectory("dlhash");
        string output = Path.Combine(dir, "out");
        Directory.CreateDirectory(output);
        string good = $"map-{Sha("good map")}.dat", bad = $"staticdata-{Sha("expected")}.dat";
        // A previous good copy of the second file must survive a damaged download.
        File.WriteAllText(Path.Combine(output, bad), "expected");
        string list = Path.Combine(dir, "list.txt");
        File.WriteAllLines(list, [good, bad, "catalog-content.json"]);
        var handler = new ScriptedHandler()
            .Respond(HttpStatusCode.OK, "good map")
            .Respond(HttpStatusCode.OK, "<html>Error page</html>")
            .Respond(HttpStatusCode.OK, "[]");
        using var http = new HttpClient(handler);

        var result = await LzmaFileTool.DownloadListAsync(http, list, "https://example.test/assets", output, null, TestContext.Current.CancellationToken, Fast);

        Assert.Equal(2, result.Succeeded);                  // the hashed good file and the unhashed catalog
        var failure = Assert.Single(result.Failures);
        Assert.Contains(bad, failure);
        Assert.Contains("checksum", failure);
        Assert.Equal("expected", File.ReadAllText(Path.Combine(output, bad)));
        Assert.Equal("good map", File.ReadAllText(Path.Combine(output, good)));
        Assert.Empty(Directory.GetFiles(output, "*.tmp"));
    }

    [Fact]
    public void ChecksumInNameIsCheckedOnTheDecompressedContentOfLzmaFiles()
    {
        string assets = TestData.RequireClientAssets();
        var catalog = AssetCatalog.Load(assets);
        string sheet = catalog.GetSheetPath(catalog.Sheets[0]);
        Assert.True(CompatibilityChecker.MatchesHashInName(sheet));
        string renamed = Path.Combine(TestData.NewTempDirectory("lzmahash"), $"sprites-{new string('a', 64)}.bmp.lzma");
        File.Copy(sheet, renamed);
        Assert.False(CompatibilityChecker.MatchesHashInName(renamed));
        Assert.Null(CompatibilityChecker.MatchesHashInName(sheet, "catalog-content.json"));
    }

    // ------------------------------------------------------------------ TibiaWiki item IDs

    private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Wiki", "item_ids_excerpt.html"));

    private static string ApiJson(string html) => System.Text.Json.JsonSerializer.Serialize(new { parse = new { title = "Item IDs", text = html } });

    /// <summary>A page with the excerpt repeated under unique IDs, large enough to pass the plausibility check.</summary>
    private static string FullPage()
    {
        var sb = new StringBuilder("<table><tr><th>Name</th><th>ID</th></tr>");
        for (int i = 0; i < 1500; i++) sb.Append($"<tr><td><a href=\"/wiki/Item_{i}\">Item {i}</a></td><td>{100000 + i}</td></tr>");
        return sb.Append("</table>").ToString();
    }

    [Fact]
    public void RealPageExcerptIsParsedIncludingItsIrregularities()
    {
        var table = WikiItemIdTable.Parse(Fixture, minimumIds: 10);
        // Ranges are expanded, "." and space separators understood, a stray letter handled like the original.
        Assert.Equal("Flower Petals", table.Names[24860]);
        Assert.Equal("Putrid Growth", table.Names[10765]);
        Assert.Equal("Captain Crab", table.Names[42308]);
        Assert.Equal("Statue of a Werefox", table.Names[27350]);
        Assert.False(table.Names.ContainsKey(27351));
        Assert.Contains(table.Log, l => l.Contains("expanded ID range"));
        Assert.Contains(table.Log, l => l.Contains("Irregular ID cell"));
    }

    [Theory]
    [InlineData("<html><body><p>This page has moved.</p></body></html>", "no item rows")]
    [InlineData("<table><tr><td><a href=\"/wiki/A\">A</a></td><td>1</td></tr></table>", "only 1 IDs")]
    public void ChangedPageLayoutsAreReportedNotSilentlyAccepted(string html, string expected)
    {
        var ex = Assert.Throws<InvalidDataException>(() => WikiItemIdTable.Parse(html));
        Assert.Contains(expected, ex.Message);
    }

    /// <summary>The item articles behind <see cref="FullPage"/>: 1,500 articles with an Infobox Object each.</summary>
    private static List<(string Title, string Wikitext, string[] Categories)> FullArticles() =>
        [.. Enumerable.Range(0, 1500).Select(i => ($"Item {i}", $"{{{{Infobox Object\n|name = Item {i}\n|itemid = {100000 + i}\n}}}}", new[] { ExternalEndpoints.ObjectsCategory }))];

    private static string CategoryInfo(int pages) => System.Text.Json.JsonSerializer.Serialize(new
    {
        batchcomplete = true,
        query = new { pages = new[] { new { pageid = 71123, ns = 14, title = ExternalEndpoints.ItemArticlesCategory, categoryinfo = new { size = pages, pages, files = 0, subcats = 0 } } } },
    });

    /// <summary>
    /// Scripts the API's answers for the item articles: the category's size, then batches of
    /// <paramref name="perBatch"/> articles, each but the last with a "continue" naming the next batch.
    /// </summary>
    private static ScriptedHandler RespondWithArticles(ScriptedHandler handler, IReadOnlyList<(string Title, string Wikitext, string[] Categories)> articles, int perBatch = 500)
    {
        handler.Respond(HttpStatusCode.OK, CategoryInfo(articles.Count));
        for (int start = 0; start < articles.Count; start += perBatch)
        {
            int first = start;
            var pages = articles.Skip(start).Take(perBatch).Select((a, i) => new
            {
                pageid = first + i + 1, ns = 0, title = a.Title,
                revisions = new[] { new { slots = new { main = new { content = a.Wikitext } } } },
                categories = a.Categories.Select(c => new { ns = 14, title = c }).ToArray(),
            }).ToArray();
            object answer = start + perBatch < articles.Count
                ? new { @continue = new Dictionary<string, string> { ["gcmcontinue"] = $"page|batch|{start + perBatch}", ["continue"] = "gcmcontinue||" }, query = new { pages } }
                : new { batchcomplete = true, query = new { pages } };
            handler.Respond(HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(answer));
        }
        return handler;
    }

    private sealed class Recorder : IProgress<OperationProgress>
    {
        public List<OperationProgress> Reports { get; } = new();
        public void Report(OperationProgress value) => Reports.Add(value);
    }

    [Fact]
    public async Task TheItemArticlesAreReadFirstBatchByBatchAndTheResultIsCached()
    {
        // Since 2026-10-15 the wiki cannot build the "Item IDs" page in time; its articles are read instead.
        string cache = TestData.NewTempDirectory("wikicache");
        var handler = RespondWithArticles(new ScriptedHandler(), FullArticles());
        using var http = new HttpClient(handler);
        var progress = new Recorder();
        var table = await WikiItemIdTable.GetAsync(http, cache, options: Fast, progress: progress, ct: TestContext.Current.CancellationToken);
        Assert.Equal(WikiTableSource.Articles, table.Source);
        Assert.Equal(1500, table.Names.Count);
        Assert.Equal("Item 7", table.Names[100007]);

        // The category's size, then three batches; each batch is the one the previous answer named.
        var queries = handler.Requests.Select(r => Uri.UnescapeDataString(r.Query)).ToList();
        Assert.Equal(4, queries.Count);
        Assert.All(handler.Requests, r => Assert.StartsWith(ExternalEndpoints.TibiaWikiApi, r.GetLeftPart(UriPartial.Path)));
        Assert.Contains("prop=categoryinfo&titles=Category:Objects with Object IDs", queries[0]);
        Assert.All(queries.Skip(1), q =>
        {
            Assert.Contains("generator=categorymembers&gcmtitle=Category:Objects with Object IDs&gcmnamespace=0&gcmlimit=50", q);
            Assert.Contains("prop=revisions|categories&rvprop=content&rvslots=main", q);
            Assert.Contains("clcategories=Category:Objects|Category:Deprecated|Category:Lists", q);
        });
        Assert.DoesNotContain("gcmcontinue", queries[1]);
        Assert.Contains("gcmcontinue=page|batch|500&continue=gcmcontinue||", queries[2]);
        Assert.Contains("gcmcontinue=page|batch|1000&continue=gcmcontinue||", queries[3]);
        Assert.Equal((1500, 1500), (progress.Reports[^1].Done, progress.Reports[^1].Total));
        Assert.Contains("item articles", progress.Reports[^1].Message);

        // A fresh cache answers without any request.
        var again = await WikiItemIdTable.GetAsync(http, cache, options: Fast, ct: TestContext.Current.CancellationToken);
        Assert.Equal(WikiTableSource.Cache, again.Source);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public void TheArticlesGiveWhatTheItemIdsPageWouldList()
    {
        // The page's query: articles in Objects, not in Deprecated or Lists, by title, with their Infobox Object's itemid.
        string[] item = [ExternalEndpoints.ObjectsCategory];
        WikiArticle A(string title, string? text, params string[] categories) => new(title, text, categories.Length == 0 ? item : categories);
        var articles = new[]
        {
            // In reverse order: the page lists by title, so an ID claimed twice goes to the first title.
            A("Zebra Statue", "{{Infobox Object|name=Zebra Statue|itemid=5}}"),
            A("Apple", "{{Infobox Object\n|name=Apple\n|itemid = 5, 6\n}}"),
            // Comments, nested templates and links neither end nor replace the value.
            A("Flower Petals", "{{Infobox Object|notes={{Hint|a|itemid=1}} see [[Petal|petals]]|itemid=<!-- was 7 -->24845-24847|value=3}}"),
            // Only the Infobox Object counts, in any spelling of its name.
            A("Torch", "{{Other|itemid=99}}\n{{infobox_Object\n|itemid=2920<br/>\n}}"),
            A("Statue of a Werefox", "{{Infobox Object|itemid=27350m 27351}}"),
            A("Unknown Sword", "{{Infobox Object|itemid=?}}"),
            A("Lookalike", "{{Infobox Objects|itemid=14}}"),
            A("Obsolete Item", "{{Infobox Object|itemid=11}}", ExternalEndpoints.ObjectsCategory, ExternalEndpoints.DeprecatedCategory),
            A("List of Statues", "{{Infobox Object|itemid=12}}", ExternalEndpoints.ObjectsCategory, ExternalEndpoints.ListsCategory),
            A("Not An Object", "{{Infobox Object|itemid=13}}", "Category:Creatures"),
            A("Not Sent", null),
        };
        var table = WikiItemIdTable.FromArticles(articles, minimumIds: 1);
        Assert.Equal(WikiTableSource.Articles, table.Source);
        Assert.Equal("Apple", table.Names[5]);
        Assert.Equal("Apple", table.Names[6]);
        Assert.Contains(table.Log, l => l.StartsWith("ID 5 appears more than once") && l.Contains("the first is used"));
        Assert.Equal("Flower Petals", table.Names[24846]);
        Assert.Equal("Torch", table.Names[2920]);
        Assert.Equal("Statue of a Werefox", table.Names[27350]);
        Assert.Contains(table.Log, l => l.StartsWith("Irregular item ID") && l.Contains("27350m 27351"));
        Assert.Equal(new uint[] { 5, 6, 2920, 24845, 24846, 24847, 27350 }, table.Names.Keys.Order());
        Assert.Contains("2 articles had no item ID", string.Join("\n", table.Log));   // "?" and the lookalike template
        Assert.Contains("1 article was not sent", string.Join("\n", table.Log));
        Assert.Equal("24845-24847", WikiItemIdTable.TemplateParameter(articles[2].Wikitext!, "Infobox Object", "itemid"));
    }

    [Fact]
    public async Task AnArticleSentInPartsIsJoinedAndARepeatedBatchIsAnError()
    {
        var handler = new ScriptedHandler()
            .Respond(HttpStatusCode.OK, CategoryInfo(1))
            .Respond(HttpStatusCode.OK, """{"continue":{"rvcontinue":"1|2","continue":"gcmcontinue||"},"query":{"pages":[{"pageid":1,"ns":0,"title":"Apple","categories":[{"ns":14,"title":"Category:Objects"}]}]}}""")
            .Respond(HttpStatusCode.OK, """{"batchcomplete":true,"query":{"pages":[{"pageid":1,"ns":0,"title":"Apple","revisions":[{"slots":{"main":{"content":"{{Infobox Object|itemid=5}}"}}}]}]}}""");
        using var http = new HttpClient(handler);
        var table = await WikiItemIdTable.FromArticlesAsync(http, Fast, minimumIds: 1, ct: TestContext.Current.CancellationToken);
        Assert.Equal("Apple", table.Names[5]);
        Assert.Contains("rvcontinue=1|2", Uri.UnescapeDataString(handler.Requests[2].Query));

        var looping = new ScriptedHandler().Respond(HttpStatusCode.OK, CategoryInfo(1));
        for (int i = 0; i < 2; i++) looping.Respond(HttpStatusCode.OK, """{"continue":{"gcmcontinue":"page|same|1","continue":"gcmcontinue||"},"query":{"pages":[]}}""");
        using var http2 = new HttpClient(looping);
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => WikiItemIdTable.FromArticlesAsync(http2, Fast, minimumIds: 1, ct: TestContext.Current.CancellationToken));
        Assert.Contains("same batch", ex.Message);
    }

    [Fact]
    public async Task TheItemIdsPageIsTheFallbackWhenTheArticlesCannotBeRead()
    {
        // The category is gone (the wiki changed how it lists items): the page, which the wiki keeps up to date, is asked next.
        var handler = new ScriptedHandler()
            .Respond(HttpStatusCode.OK, """{"batchcomplete":true,"query":{"pages":[{"ns":14,"title":"Category:Objects with Object IDs","missing":true}]}}""")
            .Respond(HttpStatusCode.OK, ApiJson(FullPage()));
        using var http = new HttpClient(handler);
        var table = await WikiItemIdTable.GetAsync(http, null, options: Fast, ct: TestContext.Current.CancellationToken);
        Assert.Equal(WikiTableSource.Api, table.Source);
        Assert.Equal(1500, table.Names.Count);
        Assert.Contains("action=parse&page=Item_IDs", handler.Requests[1].Query);
    }

    [Fact]
    public async Task RenderPageIsTheFallbackWhenTheApiFails()
    {
        var handler = new ScriptedHandler()
            .Respond(HttpStatusCode.OK, """{ "error": { "code": "readapidenied", "info": "You need read permission to use this module." } }""")
            .Respond(HttpStatusCode.OK, """{ "error": { "code": "missingtitle", "info": "The page you specified doesn't exist." } }""")
            .Respond(HttpStatusCode.OK, FullPage());
        using var http = new HttpClient(handler);
        var table = await WikiItemIdTable.GetAsync(http, null, options: Fast, ct: TestContext.Current.CancellationToken);
        Assert.Equal(WikiTableSource.RenderPage, table.Source);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task EveryAddressThatFailedIsNamedInTheError()
    {
        // 2026-10-15 without the articles: the page ends in "503 Backend fetch failed", the render address is challenged.
        var handler = new ScriptedHandler();
        for (int i = 0; i < 6; i++) handler.Respond(HttpStatusCode.ServiceUnavailable);
        handler.Respond(HttpStatusCode.Forbidden, "Just a moment...", r => r.Headers.Add("cf-mitigated", "challenge"));
        using var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => WikiItemIdTable.GetAsync(http, null, options: Fast, ct: TestContext.Current.CancellationToken));
        Assert.Contains("could not be obtained: item articles: Could not download", ex.Message);
        Assert.Contains("; Item IDs page: Could not download", ex.Message);
        Assert.Contains("; Item IDs page (render address): tibia.fandom.com refused automated access", ex.Message);
        Assert.Equal(7, handler.Requests.Count);
    }

    [Fact]
    public async Task StaleCacheIsUsedAndAnnouncedWhenTheWikiIsUnreachable()
    {
        string cache = TestData.NewTempDirectory("stalecache");
        using (var http = new HttpClient(RespondWithArticles(new ScriptedHandler(), FullArticles())))
            await WikiItemIdTable.GetAsync(http, cache, options: Fast, ct: TestContext.Current.CancellationToken);

        var offline = new ScriptedHandler();
        for (int i = 0; i < 3; i++) offline.Throw(new HttpRequestException("No such host is known."));
        using var http2 = new HttpClient(offline);
        var table = await WikiItemIdTable.GetAsync(http2, cache, forceRefresh: true, options: Fast, ct: TestContext.Current.CancellationToken);
        Assert.Equal(WikiTableSource.Cache, table.Source);
        Assert.NotNull(table.Notice);
        Assert.Contains("Using the copy downloaded", table.Notice);
    }

    [Fact]
    public async Task NoNetworkAndNoCacheIsAClearError()
    {
        var offline = new ScriptedHandler();
        for (int i = 0; i < 3; i++) offline.Throw(new HttpRequestException("No such host is known."));
        using var http = new HttpClient(offline);
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => WikiItemIdTable.GetAsync(http, null, options: Fast, ct: TestContext.Current.CancellationToken));
        Assert.Contains("could not be obtained", ex.Message);
        // No answer at all: the other addresses, on the same site, are not tried (and waited for) as well.
        Assert.Equal(3, offline.Requests.Count);
    }

    [Fact]
    public async Task DamagedCacheIsIgnored()
    {
        string cache = TestData.NewTempDirectory("badcache");
        File.WriteAllText(Path.Combine(cache, WikiItemIdTable.CacheFileName), "{ not json");
        var handler = RespondWithArticles(new ScriptedHandler(), FullArticles());
        using var http = new HttpClient(handler);
        var table = await WikiItemIdTable.GetAsync(http, cache, options: Fast, ct: TestContext.Current.CancellationToken);
        Assert.Equal(WikiTableSource.Articles, table.Source);
    }

    [Fact]
    public void ApiResponsesInBothFormatVersionsAreUnderstood()
    {
        Assert.Equal("<p>x</p>", WikiItemIdTable.HtmlFromApiResponse("""{"parse":{"text":"<p>x</p>"}}"""));
        Assert.Equal("<p>y</p>", WikiItemIdTable.HtmlFromApiResponse("""{"parse":{"text":{"*":"<p>y</p>"}}}"""));
        Assert.Throws<InvalidDataException>(() => WikiItemIdTable.HtmlFromApiResponse("""{"parse":{}}"""));
    }
}
