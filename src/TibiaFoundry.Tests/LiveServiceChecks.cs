using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Net;
using TibiaFoundry.Core.Wiki;

namespace TibiaFoundry.Tests;

/// <summary>
/// Optional checks against the live TibiaWiki. Never part of the routine run (they depend on the
/// site being reachable): set TF_LIVE_WIKI=1 to run them before a release.
/// </summary>
public sealed class LiveServiceChecks(ITestOutputHelper output)
{
    [Fact]
    public async Task TibiaWikiItemIdsCanBeDownloadedAndParsed()
    {
        Assert.SkipUnless(TestData.Setting("LIVE_WIKI") == "1", "Live service check (set TF_LIVE_WIKI=1).");
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };   // as in the app: per-request limits only
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppIdentity.Id}/{AppIdentity.Version} (+{AppIdentity.Website}; release check)");
        string cache = TestData.NewTempDirectory("wikicache");
        var table = await WikiItemIdTable.GetAsync(http, cache, forceRefresh: true, ct: TestContext.Current.CancellationToken);
        output.WriteLine($"Source {table.Source}, {table.Names.Count:N0} IDs, {table.Log.Count} log lines, notice: {table.Notice}");
        Assert.True(table.Names.Count >= WikiItemIdTable.MinimumExpectedIds);
        Assert.Equal(WikiTableSource.Articles, table.Source);
        Assert.True(File.Exists(Path.Combine(cache, WikiItemIdTable.CacheFileName)));

        if (TestData.Setting("SAVE_WIKI_HTML") is { Length: > 0 } save)
        {
            string json = await HttpFetcher.GetStringAsync(http, ExternalEndpoints.ItemIdsApiUrl, ct: TestContext.Current.CancellationToken);
            File.WriteAllText(save, WikiItemIdTable.HtmlFromApiResponse(json));
        }
    }

    /// <summary>
    /// The table from the item articles against the "Item IDs" page: the live page when the wiki can
    /// build it, otherwise a saved API answer for it (TF_WIKI_PAGE_JSON). The comparison is written to
    /// TF_WIKI_COMPARE_REPORT when set (release evidence).
    /// </summary>
    [Fact]
    public async Task TheItemArticlesGiveTheTableOfTheItemIdsPage()
    {
        Assert.SkipUnless(TestData.Setting("LIVE_WIKI") == "1", "Live service check (set TF_LIVE_WIKI=1).");
        var ct = TestContext.Current.CancellationToken;
        var counter = new CountingHandler();
        using var http = new HttpClient(counter) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppIdentity.Id}/{AppIdentity.Version} (+{AppIdentity.Website}; release check)");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var articles = await WikiItemIdTable.FromArticlesAsync(http, ct: ct);
        var took = watch.Elapsed;
        int requests = counter.Requests;

        WikiItemIds page;
        string pageFrom;
        try
        {
            watch.Restart();
            string json = await HttpFetcher.GetStringAsync(http, ExternalEndpoints.ItemIdsApiUrl, WikiItemIdTable.WikiFetchOptions with { MaxAttempts = 1 }, ct);
            page = WikiItemIdTable.Parse(WikiItemIdTable.HtmlFromApiResponse(json));
            pageFrom = FormattableString.Invariant($"the live page ({watch.Elapsed.TotalSeconds:0.0} s)");
        }
        catch (HttpFetchException ex)
        {
            string? saved = TestData.Setting("WIKI_PAGE_JSON");
            if (string.IsNullOrEmpty(saved)) { Assert.Skip($"The page could not be read ({ex.Message}) and TF_WIKI_PAGE_JSON is not set."); return; }
            page = WikiItemIdTable.Parse(WikiItemIdTable.HtmlFromApiResponse(File.ReadAllText(saved)));
            pageFrom = FormattableString.Invariant($"the page as saved on {File.GetLastWriteTimeUtc(saved):yyyy-MM-dd HH:mm}Z; the live page failed after {watch.Elapsed.TotalSeconds:0.0} s: {ex.Message.TrimEnd('.')}");
        }

        int same = articles.Names.Count(kv => page.Names.TryGetValue(kv.Key, out var name) && name == kv.Value);
        var onlyArticles = articles.Names.Keys.Except(page.Names.Keys).Order().ToList();
        var onlyPage = page.Names.Keys.Except(articles.Names.Keys).Order().ToList();
        var renamed = articles.Names.Where(kv => page.Names.TryGetValue(kv.Key, out var name) && name != kv.Value).OrderBy(kv => kv.Key).ToList();
        static int Lines(WikiItemIds table, string text) => table.Log.Count(l => l.Contains(text, StringComparison.Ordinal));
        var report = new System.Text.StringBuilder();
        void Line(FormattableString text) => report.Append(FormattableString.Invariant(text)).Append("\r\n");
        Line($"TibiaWiki item IDs read from the item articles on {DateTime.UtcNow:yyyy-MM-dd HH:mm}Z: {requests} requests, {took.TotalSeconds:0} s.");
        Line($"Compared with {pageFrom}.");
        Line($"");
        Line($"Articles: {articles.Names.Count:N0} IDs; IDs claimed twice {Lines(articles, "appears more than once")}, ranges {Lines(articles, "expanded ID range")}, irregular {Lines(articles, "Irregular")}.");
        Line($"Page:     {page.Names.Count:N0} IDs; IDs claimed twice {Lines(page, "appears more than once")}, ranges {Lines(page, "expanded ID range")}, irregular {Lines(page, "Irregular")}.");
        Line($"Same ID and name: {same:N0}. Only from the articles: {onlyArticles.Count}. Only on the page: {onlyPage.Count}. Different name: {renamed.Count}.");
        foreach (uint id in onlyArticles.Take(50)) Line($"  only from the articles: {id} {articles.Names[id]}");
        foreach (uint id in onlyPage.Take(50)) Line($"  only on the page: {id} {page.Names[id]}");
        foreach (var (id, name) in renamed.Take(50)) Line($"  {id}: \"{page.Names[id]}\" on the page, \"{name}\" from the articles");
        output.WriteLine(report.ToString());
        if (TestData.Setting("WIKI_COMPARE_REPORT") is { Length: > 0 } file) File.WriteAllText(file, report.ToString());
        // The wiki is edited every day: a saved page may differ a little, never much.
        Assert.True(same >= page.Names.Count * 0.99, $"{same} of {page.Names.Count} agree");
    }

    private sealed class CountingHandler() : DelegatingHandler(new HttpClientHandler())
    {
        private int _requests;
        public int Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// A real catalog in a temporary folder: every object of the client in use and TibiaWiki's live
    /// item list, with the coverage report (written to TF_CATALOG_REPORT when set, as release evidence).
    /// </summary>
    [Fact]
    public async Task ACatalogOfTheClientAndTibiaWikiReportsItsCoverage()
    {
        Assert.SkipUnless(TestData.Setting("LIVE_WIKI") == "1", "Live service check (set TF_LIVE_WIKI=1).");
        TestData.RequireClientAssets();
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppIdentity.Id}/{AppIdentity.Version} (+{AppIdentity.Website}; release check)");
        var table = await WikiItemIdTable.GetAsync(http, TestData.NewTempDirectory("wikicache"), forceRefresh: true, ct: TestContext.Current.CancellationToken);
        var db = SharedData.Appearances;
        var catalog = Core.Items.ItemCatalogStore.Create(Path.Combine(TestData.NewTempDirectory("realcatalog"), "Real"), "Real client and TibiaWiki");
        Core.Items.ItemCatalogStore.Commit(catalog, Core.Items.ItemImporters.FromClient(db, TestData.Client.ReadClientVersion(), db.SourcePath));
        Core.Items.ItemCatalogStore.Commit(catalog, Core.Items.ItemImporters.FromTibiaWiki(table));
        var coverage = Core.Items.CatalogCoverage.Of(catalog);
        string text = coverage.ToText();
        output.WriteLine(text);
        if (TestData.Setting("CATALOG_REPORT") is { Length: > 0 } report) File.WriteAllText(report, $"TibiaWiki fetched {table.FetchedAtUtc:u} ({table.Source})\r\n\r\n" + text.ReplaceLineEndings("\r\n"));
        Assert.Equal(db.Count(Core.Appearances.AppearanceKind.Object), coverage.ClientObjects);
        Assert.True(coverage.Sources.Single(s => s.Source.Kind == Core.Items.SourceKind.TibiaWiki).NamesForUnnamed > 0);
        Assert.Equal(coverage.ClientUnnamed, coverage.UnnamedNamedElsewhere + coverage.UnnamedNamedNowhere);
    }
}
