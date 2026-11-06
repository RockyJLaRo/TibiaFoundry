using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Sprites;

public enum SpriteExportMode { IndividualSprites, WholeSheets }

public enum SpriteImageFormat { Png, Gif }

/// <summary>Options of the "Extract Sprites" tool.</summary>
public sealed record SpriteExtractionOptions
{
    public required string OutputDirectory { get; init; }
    public SpriteExportMode Mode { get; init; } = SpriteExportMode.IndividualSprites;
    public SpriteImageFormat Format { get; init; } = SpriteImageFormat.Png;
    /// <summary>Put each sheet's sprites in a "Sheet first-last" sub-folder.</summary>
    public bool SplitIntoSheetFolders { get; init; }
    /// <summary>Name whole-sheet exports after the client's sheet file instead of "Sprites first-last".</summary>
    public bool UseOriginalSheetNames { get; init; }
    /// <summary>Leave files that already exist untouched (new option; speeds up re-extraction).</summary>
    public bool SkipExisting { get; init; }
    public int MaxDegreeOfParallelism { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);
    /// <summary>Client version recorded in the folder's provenance manifest.</summary>
    public string? ClientVersion { get; init; }
    /// <summary>Attribution embedded in every file of the run (the configured creator when null).</summary>
    public AttributionInfo? Attribution { get; init; }
}

/// <summary>
/// Extracts sprite sheets from the client, reproducing the original <c>DumpSpriteSheet</c>
/// file layout. Fixed defects: the ".gif" option was ignored (always PNG), paths without a
/// trailing separator produced names like "AllSpritesSheet 0-143", and the work ran with
/// unguarded cross-thread UI access and no cancellation.
/// </summary>
public static class SpriteExtractor
{
    public static string IndividualSpritePath(SpriteExtractionOptions options, SpriteSheetInfo sheet, int spriteId)
    {
        string dir = options.SplitIntoSheetFolders
            ? Path.Combine(options.OutputDirectory, $"Sheet {sheet.FirstSpriteId}-{sheet.LastSpriteId}")
            : options.OutputDirectory;
        return Path.Combine(dir, spriteId + Extension(options.Format));
    }

    public static string WholeSheetPath(SpriteExtractionOptions options, SpriteSheetInfo sheet)
    {
        // The catalog name is validated on load; sanitising again keeps the output inside the target folder regardless.
        string name = options.UseOriginalSheetNames
            ? FileNames.Sanitize(StripExtensions(Path.GetFileName(sheet.FileName)))
            : L.F("SpriteExtractor.Sprites01", sheet.FirstSpriteId, sheet.LastSpriteId);
        return Path.Combine(options.OutputDirectory, name + Extension(options.Format));
    }

    private static string Extension(SpriteImageFormat format) => format == SpriteImageFormat.Gif ? ".gif" : ".png";

    private static string StripExtensions(string fileName)
    {
        // "sprites-<hash>.bmp.lzma" -> "sprites-<hash>"
        string name = fileName;
        foreach (var ext in new[] { ".lzma", ".bmp" })
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name = name[..^ext.Length];
        return name;
    }

    public static async Task<BatchResult> ExtractAsync(AssetCatalog catalog, IReadOnlyCollection<SpriteSheetInfo> sheets,
        SpriteExtractionOptions options, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        var result = new BatchResult();
        // One attribution for the whole run: built and verified once, embedded in every file.
        if (options.Attribution == null) options = options with { Attribution = Metadata.Attribution.ForNewAsset() };
        // Individual sprites in one folder are what the sprite repository reads back. If the folder
        // holds another client's sprites, "skip existing" would keep outdated images: overwrite them.
        bool flatSprites = options.Mode == SpriteExportMode.IndividualSprites && !options.SplitIntoSheetFolders;
        if (flatSprites && options.SkipExisting &&
            SpriteFolderManifest.StateOf(options.OutputDirectory, catalog.ContentSha256, out var previous) == SpriteFolderState.Stale)
        {
            options = options with { SkipExisting = false };
            result.Notes.Add(L.F("SpriteExtractor.TheFolderHeldSpritesOfClient", previous?.ClientVersion ?? L.T("ClientStartup.Unknown2")));
        }
        int done = 0;
        var gate = new object();
        await Parallel.ForEachAsync(sheets, new ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism, CancellationToken = cancellationToken },
            (sheet, ct) =>
            {
                int written = 0, skipped = 0;
                string? failure = null;
                try
                {
                    written = ExtractSheet(catalog, sheet, options, ct, out skipped);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failure = $"{sheet}: {ex.Message}";
                    AppLog.Error($"Sprite sheet {sheet.FileName} failed", ex);
                }
                lock (gate)
                {
                    result.Succeeded += written;
                    result.Skipped += skipped;
                    if (failure != null) result.Failures.Add(failure);
                    done++;
                    progress?.Report(new OperationProgress(done, sheets.Count, L.F("SpriteExtractor.Sheet01", sheet.FirstSpriteId, sheet.LastSpriteId)));
                }
                return ValueTask.CompletedTask;
            });
        if (flatSprites && result.Succeeded + result.Skipped > 0)
        {
            try
            {
                new SpriteFolderManifest(AppIdentity.Id, options.ClientVersion, catalog.ContentSha256, DateTime.UtcNow, result.Succeeded + result.Skipped)
                    .Write(options.OutputDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Notes.Add(L.T("SpriteExtractor.TheFolderSVersionInformationCould") + ex.Message);
            }
        }
        return result;
    }

    /// <returns>Number of files written.</returns>
    public static int ExtractSheet(AssetCatalog catalog, SpriteSheetInfo sheet, SpriteExtractionOptions options, CancellationToken ct, out int skipped)
    {
        skipped = 0;
        string sheetPath = catalog.GetSheetPath(sheet);
        if (!File.Exists(sheetPath)) throw new FileNotFoundException("Sprite sheet file is missing.", sheetPath);

        if (options.Mode == SpriteExportMode.WholeSheets)
        {
            string target = WholeSheetPath(options, sheet);
            if (options.SkipExisting && File.Exists(target)) { skipped = 1; return 0; }
            Save(SpriteSheetDecoder.DecodeSheet(sheetPath), target, options);
            return 1;
        }

        var sprites = SpriteSheetDecoder.Slice(SpriteSheetDecoder.DecodeSheet(sheetPath), sheet);
        int written = 0;
        for (int i = 0; i < sprites.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            string target = IndividualSpritePath(options, sheet, sheet.FirstSpriteId + i);
            if (options.SkipExisting && File.Exists(target)) { skipped++; continue; }
            Save(sprites[i], target, options);
            written++;
        }
        return written;
    }

    private static void Save(RgbaImage image, string path, SpriteExtractionOptions options)
    {
        // Both formats carry the creator attribution (the original left single-sprite GIFs without a comment).
        if (options.Format == SpriteImageFormat.Gif) GifWriter.Write(path, new[] { image }, new[] { 0 }, options.Attribution);
        else ImageIO.SavePng(image, path, options.Attribution);
    }
}
