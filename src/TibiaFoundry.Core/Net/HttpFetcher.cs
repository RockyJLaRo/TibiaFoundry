using System.Net;
using System.Net.Http.Headers;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Net;

/// <summary>Limits and retry policy of one request.</summary>
public sealed record FetchOptions
{
    /// <summary>Total attempts for transient failures (network errors, timeouts, 408/429/5xx).</summary>
    public int MaxAttempts { get; init; } = 3;
    /// <summary>Responses larger than this are rejected (protects memory and disk).</summary>
    public long MaxBytes { get; init; } = 32L * 1024 * 1024;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);
    /// <summary>First backoff delay; doubles per attempt (with jitter).</summary>
    public TimeSpan BackoffBase { get; init; } = TimeSpan.FromSeconds(1);
    /// <summary>Longest wait honored for a server's Retry-After (longer requests fail instead of hanging).</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(30);

    public static FetchOptions Default { get; } = new();
}

/// <summary>A request that failed for good (or after all retries), with the reason classified.</summary>
public sealed class HttpFetchException(Uri uri, string message, HttpStatusCode? status, bool transient, Exception? inner = null)
    : Exception(message, inner)
{
    public Uri Uri { get; } = uri;
    public HttpStatusCode? StatusCode { get; } = status;
    /// <summary>True when retrying later may succeed (network down, server busy, rate limited).</summary>
    public bool Transient { get; } = transient;
}

/// <summary>
/// The single place where TibiaFoundry talks to the network. Every request gets a timeout, a
/// size limit and bounded retries with exponential backoff for transient failures; 429/503
/// responses wait at least as long as their Retry-After (up to a limit); permanent errors (404,
/// 403…) are not retried.
/// Requests are idempotent GETs only, so a retry can never duplicate work on the server.
/// </summary>
public static class HttpFetcher
{
    public static async Task<byte[]> GetBytesAsync(HttpClient http, Uri uri, FetchOptions? options = null, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await SendAsync(http, uri, options ?? FetchOptions.Default, buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    public static async Task<string> GetStringAsync(HttpClient http, Uri uri, FetchOptions? options = null, CancellationToken ct = default) =>
        System.Text.Encoding.UTF8.GetString(await GetBytesAsync(http, uri, options, ct).ConfigureAwait(false));

    /// <summary>
    /// Downloads to <paramref name="path"/> atomically: an interrupted download never replaces an
    /// existing file. <paramref name="verify"/> runs on the complete temporary file first; if it
    /// throws, nothing is replaced either.
    /// </summary>
    public static Task DownloadFileAsync(HttpClient http, Uri uri, string path, FetchOptions? options = null, Action<string>? verify = null,
        CancellationToken ct = default) =>
        AtomicFile.WriteAsync(path, async temp =>
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                await SendAsync(http, uri, options ?? FetchOptions.Default, file, ct).ConfigureAwait(false);
            }
            verify?.Invoke(temp);
        });

    private static async Task SendAsync(HttpClient http, Uri uri, FetchOptions options, Stream destination, CancellationToken ct)
    {
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            throw new HttpFetchException(uri, $"Only http(s) addresses are supported: {uri}", null, transient: false);
        Exception? last = null;
        for (int attempt = 1; attempt <= Math.Max(1, options.MaxAttempts); attempt++)
        {
            ct.ThrowIfCancellationRequested();
            TimeSpan? wait = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(options.Timeout);
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    if (response.Content.Headers.ContentLength > options.MaxBytes)
                        throw new HttpFetchException(uri, $"The response is {FileOps.ReportWriter.FormatSize(response.Content.Headers.ContentLength ?? 0)}, more than the allowed {FileOps.ReportWriter.FormatSize(options.MaxBytes)}.", response.StatusCode, transient: false);
                    destination.SetLength(0);
                    await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                    await CopyLimitedAsync(body, destination, options.MaxBytes, uri, timeout.Token).ConfigureAwait(false);
                    return;
                }
                int code = (int)response.StatusCode;
                // A bot-protection challenge (Cloudflare marks it with "cf-mitigated") needs a real
                // browser; retrying cannot help, and the program does not try to get around it.
                bool challenged = response.Headers.Contains("cf-mitigated");
                bool transient = !challenged && code is (408 or 429 or >= 500);
                last = new HttpFetchException(uri, challenged
                    ? $"{uri.Host} refused automated access (it requires a browser check), so this address cannot be used by the program."
                    : $"{uri.Host} answered {code} {response.ReasonPhrase}.", response.StatusCode, transient);
                if (!transient) throw last;
                wait = RetryAfter(response.Headers.RetryAfter);
                if (wait > options.MaxRetryAfter)
                    throw new HttpFetchException(uri, $"{uri.Host} asked to wait {wait.Value.TotalSeconds:0} s before retrying (rate limited). Try again later.", response.StatusCode, transient: true);
                AppLog.Warn($"HTTP {code} from {uri.Host} (attempt {attempt}/{options.MaxAttempts}).");
            }
            catch (HttpFetchException)
            {
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
            {
                // Network failure or per-request timeout: transient.
                last = ex;
                AppLog.Warn($"Request to {uri.Host} failed (attempt {attempt}/{options.MaxAttempts}): {ex.Message}");
            }
            if (attempt < options.MaxAttempts)
            {
                var backoff = TimeSpan.FromMilliseconds(options.BackoffBase.TotalMilliseconds * Math.Pow(2, attempt - 1) * (0.8 + Random.Shared.NextDouble() * 0.4));
                // Retry-After is the shortest wait; a longer backoff of the options is kept (TibiaWiki's
                // "503 Backend fetch failed" says 5 s, which is too soon for the page to be built).
                if (wait > backoff) backoff = wait.Value;
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }
        }
        string reason = last switch
        {
            HttpFetchException h => h.Message,
            OperationCanceledException => L.F("HttpFetcher.NoAnswerWithin00S", options.Timeout.TotalSeconds),
            _ => last?.Message ?? L.T("HttpFetcher.UnknownError"),
        };
        throw new HttpFetchException(uri, $"Could not download from {uri.Host} after {L.Count(options.MaxAttempts, "Noun.attempt")}: {reason}",
            (last as HttpFetchException)?.StatusCode, transient: true, last);
    }

    private static async Task CopyLimitedAsync(Stream source, Stream destination, long maxBytes, Uri uri, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes) throw new HttpFetchException(uri, $"The response exceeded the allowed {FileOps.ReportWriter.FormatSize(maxBytes)}.", null, transient: false);
            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }

    private static TimeSpan? RetryAfter(RetryConditionHeaderValue? header)
    {
        if (header?.Delta is { } delta) return delta;
        if (header?.Date is { } date) return date - DateTimeOffset.UtcNow is { } d && d > TimeSpan.Zero ? d : TimeSpan.Zero;
        return null;
    }
}
