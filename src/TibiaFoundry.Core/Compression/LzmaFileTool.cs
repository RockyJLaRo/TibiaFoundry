using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Net;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Compression;

/// <summary>
/// The original's hidden "LZMA Decode" page: manual decompression of CipSoft LZMA files and a
/// bulk downloader for a list of asset file names.
/// </summary>
public static class LzmaFileTool
{
    /// <summary>Decompresses <paramref name="inputFile"/> into <paramref name="outputDirectory"/>, dropping the ".lzma" extension.</summary>
    public static string Decode(string inputFile, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string name = Path.GetFileName(inputFile);
        if (name.EndsWith(".lzma", StringComparison.OrdinalIgnoreCase)) name = name[..^5];
        string target = Path.Combine(outputDirectory, FileNames.Sanitize(name));
        AtomicFile.WriteAllBytes(target, CipLzma.DecompressFile(inputFile));
        return target;
    }

    /// <summary>
    /// Downloads every file named in <paramref name="listFile"/> (one per line) from
    /// <paramref name="baseUrl"/>. The original hard-coded both the URL and "F:/Tibia/TS 2020/" and
    /// swallowed every error; here both are configurable and failures are reported.
    /// </summary>
    public static async Task<BatchResult> DownloadListAsync(HttpClient http, string listFile, string baseUrl, string outputDirectory,
        IProgress<OperationProgress>? progress, CancellationToken ct, FetchOptions? options = null)
    {
        var names = File.ReadAllLines(listFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!Uri.TryCreate(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/", UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException($"'{baseUrl}' is not a valid http(s) address.", nameof(baseUrl));
        Directory.CreateDirectory(outputDirectory);
        var result = new BatchResult();
        int verified = 0;
        for (int i = 0; i < names.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            string name = names[i];
            // List entries are relative asset names; never let one point outside the output folder.
            string fileName = FileNames.Sanitize(Path.GetFileName(name.Replace(Path.DirectorySeparatorChar, '/')));
            string target = Path.Combine(outputDirectory, fileName);
            try
            {
                var uri = new Uri(baseUri, string.Join('/', name.Split('/', Path.DirectorySeparatorChar).Select(Uri.EscapeDataString)));
                // Client files carry the SHA-256 of their content in their name: a download that does
                // not match (damaged, truncated, an error page) is rejected before it replaces anything.
                bool hashed = false;
                await HttpFetcher.DownloadFileAsync(http, uri, target, options, temp =>
                {
                    bool? match = CompatibilityChecker.MatchesHashInName(temp, fileName);
                    if (match == false) throw new InvalidDataException("the downloaded content does not match the checksum in its name (damaged or wrong file); not saved.");
                    hashed = match == true;
                }, ct).ConfigureAwait(false);
                result.Succeeded++;
                if (hashed) verified++;
            }
            catch (Exception ex) when (ex is HttpFetchException or IOException or UnauthorizedAccessException or UriFormatException or InvalidDataException)
            {
                result.Failures.Add($"{name}: {ex.Message}");
                AppLog.Warn($"Download of {name} failed: {ex.Message}");
            }
            progress?.Report(new OperationProgress(i + 1, names.Count, name));
        }
        if (result.Succeeded > 0) AppLog.Info($"Downloaded {L.Count(result.Succeeded, "Noun.file")}; {verified} verified against the checksum in their names.");
        return result;
    }
}
