using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Library;

public enum DuplicateKind
{
    /// <summary>Byte-identical files (the same SHA-256), whatever their names.</summary>
    IdenticalFiles,
    /// <summary>The same pixels at another size or with other transparent borders (a 2× export, a padded copy).</summary>
    SameGraphicOtherSize,
    /// <summary>Images that look alike (perceptual hash). A hint for review, never proof.</summary>
    VisuallySimilar,
}

/// <summary>A group of assets that are, or may be, the same resource.</summary>
public sealed record DuplicateGroup(DuplicateKind Kind, IReadOnlyList<AssetRecord> Assets)
{
    public bool DifferentNames => Assets.Select(a => a.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
    public bool DifferentClientVersions => Assets.Select(a => a.ClientVersion).Where(v => v != null).Distinct().Count() > 1;
    public long WastedBytes => Kind == DuplicateKind.IdenticalFiles ? Assets.Skip(1).Sum(a => a.SizeBytes) : 0;

    public string Description => Kind switch
    {
        DuplicateKind.IdenticalFiles => L.T("DuplicateFinder.IdenticalFiles") + (DifferentNames ? L.T("DuplicateFinder.WithDifferentNames") : "") + (DifferentClientVersions ? L.T("DuplicateFinder.FromDifferentClientVersions") : ""),
        DuplicateKind.SameGraphicOtherSize => (Assets.Select(a => (a.Width, a.Height)).Distinct().Count() == 1
            ? L.T("DuplicateFinder.TheSamePixelsInDifferentFiles")
            : L.T("DuplicateFinder.TheSameGraphicAtDifferentSizes")) + (DifferentClientVersions ? L.T("DuplicateFinder.FromDifferentClientVersions") : ""),
        _ => L.T("DuplicateFinder.VisuallySimilarImagesCheckThemSimilar"),
    };
}

public sealed record DuplicateReport(IReadOnlyList<DuplicateGroup> Groups, int AssetsChecked)
{
    public int Count(DuplicateKind kind) => Groups.Count(g => g.Kind == kind);
    public long WastedBytes => Groups.Sum(g => g.WastedBytes);

    public string Summary =>
        L.F("DuplicateFinder.0Checked1OfIdenticalFiles", L.Count(AssetsChecked, "Noun.asset"), L.Count(Count(DuplicateKind.IdenticalFiles), "Noun.group"), Count(DuplicateKind.SameGraphicOtherSize), Count(DuplicateKind.VisuallySimilar));
}

/// <summary>
/// Finds duplicates in the library (8.8): identical files, the same graphic at another size, and
/// visually similar images. It only reports; nothing is deleted, moved or merged automatically.
/// </summary>
public static class DuplicateFinder
{
    /// <summary>Largest Hamming distance between difference hashes still called "similar" (of 64 bits).</summary>
    public const int DefaultSimilarity = 4;

    /// <summary>Images smaller than this (in pixels per side) are not compared visually: tiny sprites are too alike to mean anything.</summary>
    public const int MinSimilaritySize = 24;

    public static DuplicateReport Find(AssetLibrary library, int similarity = DefaultSimilarity, bool includeSimilar = true,
        IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        var all = new List<AssetRecord>();
        for (int offset = 0; ; offset += 5000)
        {
            ct.ThrowIfCancellationRequested();
            var page = library.Search(new AssetQuery { IncludeMissing = false, Sort = AssetSort.Id, Offset = offset, Limit = 5000 });
            all.AddRange(page.Assets);
            progress?.Report(new OperationProgress(all.Count, page.Total, L.T("DuplicateFinder.ReadingTheIndex")));
            if (page.Assets.Count < 5000) break;
        }
        var groups = new List<DuplicateGroup>();
        var inExact = new HashSet<long>();
        foreach (var g in all.Where(a => a.Sha256 != null).GroupBy(a => a.Sha256!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            groups.Add(new DuplicateGroup(DuplicateKind.IdenticalFiles, g.OrderBy(a => a.Path, StringComparer.OrdinalIgnoreCase).ToList()));
            foreach (var a in g) inExact.Add(a.Id);
        }
        var inScaled = new HashSet<long>();
        foreach (var g in all.Where(a => a.PixelKey != null).GroupBy(a => a.PixelKey!))
        {
            // One representative per identical file, so this group is only about different files.
            var distinct = g.GroupBy(a => a.Sha256).Select(x => x.First()).ToList();
            if (distinct.Count < 2) continue;
            groups.Add(new DuplicateGroup(DuplicateKind.SameGraphicOtherSize, distinct.OrderBy(a => a.Width * a.Height).ThenBy(a => a.Path, StringComparer.OrdinalIgnoreCase).ToList()));
            foreach (var a in g) inScaled.Add(a.Id);
        }
        if (includeSimilar)
        {
            var candidates = all.Where(a => a.PixelHash != null && a.Width >= MinSimilaritySize && a.Height >= MinSimilaritySize && !inScaled.Contains(a.Id))
                .GroupBy(a => a.Sha256).Select(g => g.First()).ToList();
            var parent = Enumerable.Range(0, candidates.Count).ToArray();
            int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
            // Pigeonhole: two hashes within `similarity` bits agree completely on at least one of
            // `similarity + 1` slices, so only images sharing a slice are compared (fast for 100,000 images).
            similarity = Math.Clamp(similarity, 0, 16);
            int slices = similarity + 1, sliceBits = (64 + slices - 1) / slices;
            var compared = new HashSet<(int, int)>();
            for (int s = 0; s < slices; s++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new OperationProgress(s, slices, L.T("DuplicateFinder.ComparingImages")));
                int shift = s * sliceBits;
                ulong mask = sliceBits >= 64 ? ulong.MaxValue : ((1UL << sliceBits) - 1) << shift;
                foreach (var bucket in Enumerable.Range(0, candidates.Count).GroupBy(i => (ulong)candidates[i].PixelHash!.Value & mask).Where(b => b.Count() > 1))
                {
                    var members = bucket.ToList();
                    if (members.Count > 2000) continue; // a flat color or empty images: not a meaningful group
                    for (int x = 0; x < members.Count; x++)
                        for (int y = x + 1; y < members.Count; y++)
                        {
                            int i = members[x], j = members[y];
                            if (!compared.Add((i, j))) continue;
                            var a = candidates[i];
                            var b = candidates[j];
                            if (ImageSignature.Distance(a.PixelHash!.Value, b.PixelHash!.Value) > similarity) continue;
                            double ratioA = (double)a.Width / a.Height, ratioB = (double)b.Width / b.Height;
                            if (Math.Abs(ratioA - ratioB) > 0.15 * Math.Max(ratioA, ratioB)) continue;
                            parent[Find(i)] = Find(j);
                        }
                }
            }
            foreach (var g in Enumerable.Range(0, candidates.Count).GroupBy(Find).Where(g => g.Count() > 1 && g.Count() <= 200))
                groups.Add(new DuplicateGroup(DuplicateKind.VisuallySimilar, g.Select(i => candidates[i]).OrderBy(a => a.Path, StringComparer.OrdinalIgnoreCase).ToList()));
        }
        return new DuplicateReport(groups.OrderBy(g => g.Kind).ThenByDescending(g => g.Assets.Count).ToList(), all.Count);
    }
}
