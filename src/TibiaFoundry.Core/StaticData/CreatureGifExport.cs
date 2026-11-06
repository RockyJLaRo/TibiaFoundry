using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Localization;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.Wiki;

namespace TibiaFoundry.Core.StaticData;

/// <summary>A creature GIF to write: the creature and the file it goes to.</summary>
public sealed record CreatureGifJob(Creature Creature, string Path);

/// <summary>
/// Exports the animated GIFs of many monsters and bosses at once (Static Data, 8.8.2): the same
/// animation as the page's preview (moving south, with the creature's colors and addons; creatures
/// that look like an object use the object's animation), named like TibiaWiki articles
/// ("orc" → "Orc.gif", "demon lord" → "Demon Lord.gif"). Existing files are never replaced: a name
/// that is taken gets " 2", " 3"… like every other GIF export.
/// </summary>
public static class CreatureGifExport
{
    /// <summary>The creature's name as shown and as its file is named: TibiaWiki article casing.</summary>
    public static string DisplayName(Creature creature) =>
        string.IsNullOrWhiteSpace(creature.Name) ? L.F("CreatureGifExport.Creature0", creature.Race) : NameFormatter.Capitalize(creature.Name.Trim());

    /// <summary>
    /// The file of each creature in its folder. Names are reserved in order, so two creatures with the
    /// same name in one run, or a file that is already there, get " 2", " 3"… instead of replacing anything.
    /// </summary>
    public static IReadOnlyList<CreatureGifJob> Plan(IEnumerable<(Creature Creature, string Folder)> creatures)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var jobs = new List<CreatureGifJob>();
        foreach (var (creature, folder) in creatures)
        {
            string name = FileNames.Sanitize(DisplayName(creature));
            string path = Path.Combine(folder, name + ".gif");
            for (int n = 2; taken.Contains(path) || File.Exists(path); n++) path = Path.Combine(folder, $"{name} {n}.gif");
            taken.Add(path);
            jobs.Add(new CreatureGifJob(creature, path));
        }
        return jobs;
    }

    /// <summary>
    /// Renders and writes the GIFs, several at a time. A creature without an outfit or whose outfit is
    /// not in the appearances is reported as a failure; the others are still written.
    /// </summary>
    /// <param name="forcedDelayMs">One delay for every frame, or null for each creature's own delays.</param>
    /// <param name="attribution">One attribution for the whole run (the configured creator when null).</param>
    /// <returns>What was written; <paramref name="saved"/> receives every file in the order of <paramref name="jobs"/>.</returns>
    public static BatchResult Export(IReadOnlyList<CreatureGifJob> jobs, AppearanceDatabase appearances, ISpriteSource sprites, int? forcedDelayMs,
        AttributionInfo? attribution, int parallelism, List<string> saved, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        attribution ??= Attribution.ForNewAsset();
        var result = new BatchResult();
        var written = new bool[jobs.Count];
        var failures = new string?[jobs.Count];
        foreach (string folder in jobs.Select(j => Path.GetDirectoryName(j.Path)!).Distinct(StringComparer.OrdinalIgnoreCase)) Directory.CreateDirectory(folder);
        int done = 0;
        var gate = new object();
        var lastReport = DateTime.MinValue;
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism), CancellationToken = ct }, i =>
        {
            var job = jobs[i];
            string name = DisplayName(job.Creature);
            try
            {
                var animation = CreatureImages.Animate(job.Creature, appearances, sprites)
                    ?? throw new InvalidOperationException(L.T("StaticData.ThisCreatureHasNoOutfit"));
                IReadOnlyList<int> delays = forcedDelayMs is { } d ? [d] : animation.Delays;
                GifWriter.Write(job.Path, animation.Frames, delays, attribution);
                written[i] = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or OutfitRenderException or ImageMagick.MagickException)
            {
                failures[i] = $"{name} ({job.Creature.Race}): {ex.Message}";
                AppLog.Warn($"Creature GIF {name} could not be saved: {ex.Message}");
            }
            lock (gate)
            {
                done++;
                // A few updates a second are enough for the status bar, also for 2,000 creatures.
                if (progress != null && (done == jobs.Count || (DateTime.UtcNow - lastReport).TotalMilliseconds >= 100))
                {
                    lastReport = DateTime.UtcNow;
                    progress.Report(new OperationProgress(done, jobs.Count, name));
                }
            }
        });
        for (int i = 0; i < jobs.Count; i++)
        {
            if (written[i])
            {
                result.Succeeded++;
                saved.Add(jobs[i].Path);
            }
            else if (failures[i] is { } failure) result.Failures.Add(failure);
        }
        return result;
    }
}
