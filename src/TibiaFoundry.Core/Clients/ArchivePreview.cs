using TibiaFoundry.Core.Localization;
namespace TibiaFoundry.Core.Clients;

/// <summary>How a category of files takes part in an archive.</summary>
public enum ArchiveCategoryState
{
    /// <summary>Archived with the revision.</summary>
    Included,
    /// <summary>Left out on purpose (personal data while it is not included).</summary>
    Excluded,
    /// <summary>Could not be archived (unreadable, a link, changed while read); only kept as an incomplete revision.</summary>
    NotArchived,
}

/// <summary>One line of an archive's file details (8.5).</summary>
/// <param name="Name">"Client files", "Options and hotkeys", "Screenshots"…</param>
/// <param name="Files">Files in the category (for <see cref="ArchiveCategoryState.NotArchived"/>: entries).</param>
/// <param name="Bytes">Their size as restored.</param>
/// <param name="NewBytes">What the category adds to the archive on disk (its contents not stored yet); 0 for files that are not archived.</param>
/// <param name="IsPersonal">Personal data (hotkeys, characters' data, explored map, screenshots, logs…).</param>
public sealed record ArchiveCategory(string Name, ArchiveCategoryState State, int Files, long Bytes, long NewBytes, bool IsPersonal);

/// <summary>
/// What an archive would do (8.5), computed from a consistent-or-not scan and the store before
/// anything is written: the archive confirmation shows it. The size as restored, what is written to
/// the archive and what is reused are separate figures: in space-efficient storage, contents the
/// archive already holds, and identical files within the folder, are stored once.
/// </summary>
public sealed class ArchivePreview
{
    public required FolderScan Scan { get; init; }
    /// <summary><see cref="ManifestScopes.CompleteInstallation"/> or <see cref="ManifestScopes.StandaloneFolder"/>.</summary>
    public required string Scope { get; init; }
    /// <summary><see cref="ManifestStorage.Shared"/> or <see cref="ManifestStorage.Independent"/>.</summary>
    public required string Storage { get; init; }
    /// <summary>Whether the scan included personal data.</summary>
    public required bool IncludesPersonalData { get; init; }
    /// <summary>Every category, included ones first; their files and bytes add up to the totals.</summary>
    public required IReadOnlyList<ArchiveCategory> Categories { get; init; }
    /// <summary>Bytes written to the archive (the same figure the free-space check uses).</summary>
    public required long NewBytes { get; init; }

    public int Files => Scan.Files.Count;
    /// <summary>The archived files as restored (the size of a rebuild).</summary>
    public long LogicalBytes => Scan.TotalBytes;
    /// <summary>Contents already in the archive, or identical within the folder: stored once, not written again.</summary>
    public long ReusedBytes => Math.Max(0, LogicalBytes - NewBytes);
    public int ExcludedFiles => Categories.Where(c => c.State == ArchiveCategoryState.Excluded).Sum(c => c.Files);
    public long ExcludedBytes => Categories.Where(c => c.State == ArchiveCategoryState.Excluded).Sum(c => c.Bytes);
    /// <summary>Whether the folder has personal data that is left out (the confirmation then offers to include it).</summary>
    public bool HasExcludedPersonalData => Categories.Any(c => c.State == ArchiveCategoryState.Excluded && c.IsPersonal);

    /// <summary>The name of a personal folder or exclusion rule in plain words ("minimap" → "Explored map").</summary>
    public static string PersonalCategoryName(string topFolder) => topFolder.ToLowerInvariant() switch
    {
        "conf" => L.T("ArchivePreview.OptionsAndHotkeys"),
        "characterdata" => L.T("ArchivePreview.CharacterData"),
        "minimap" => L.T("ArchivePreview.ExploredMap"),
        "screenshots" => "Screenshots",
        "log" => "Logs",
        "cache" => "Cache",
        "crashdump" => L.T("ArchivePreview.CrashReports"),
        "storeimages" => L.T("ArchivePreview.StoreImages"),
        "npc_pics" => L.T("ArchivePreview.NPCPictures"),
        _ => topFolder,
    };

    /// <summary>What "personal data" holds, for the option that includes it.</summary>
    public const string PersonalDataDescription = "Options and hotkeys, character data, the explored map, screenshots (named after your characters), logs, crash reports, the cache and images the client downloaded.";

    /// <summary>The category of an exclusion rule: "conf/ (except …)" → "Options and hotkeys"; a rule that is not a folder ("client screenshots …") → "Screenshots".</summary>
    public static string CategoryOfRule(string rule)
    {
        int slash = rule.IndexOf('/');
        if (slash > 0 && !rule[..slash].Contains(' ')) return PersonalCategoryName(rule[..slash]);
        return rule.Contains("screenshot", StringComparison.OrdinalIgnoreCase) ? "Screenshots" : rule;
    }

    /// <summary>
    /// Builds the preview. <paramref name="isStored"/> says whether the archive already holds a
    /// content (hash, size); ignored for independent storage, which copies every file.
    /// </summary>
    public static ArchivePreview Create(FolderScan scan, string scope, string storage, bool includesPersonalData, Func<string, long, bool> isStored)
    {
        bool shared = storage != ManifestStorage.Independent;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var included = new Dictionary<string, (int Files, long Bytes, long New, bool Personal)>(StringComparer.Ordinal);
        long newBytes = 0;
        foreach (var file in scan.Files)
        {
            string top = file.Path.Contains('/') ? file.Path[..file.Path.IndexOf('/')] : "";
            bool personal = ClientScanner.PersonalFolders.Contains(top);
            // Without personal data, a personal folder's file is archived only because the client's own lists name it.
            string name = !personal ? L.T("ArchivePreview.ClientFiles") : includesPersonalData ? PersonalCategoryName(top) : PersonalCategoryName(top) + L.T("ArchivePreview.ListedByTheClient");
            // Shared storage writes each content once, and only when the archive does not hold it yet.
            long added = !shared ? file.Size : seen.Add(file.Sha256) && !isStored(file.Sha256, file.Size) ? file.Size : 0;
            newBytes += added;
            var (n, b, a, _) = included.GetValueOrDefault(name);
            included[name] = (n + 1, b + file.Size, a + added, personal);
        }
        var categories = included
            .OrderBy(c => c.Value.Personal).ThenBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => new ArchiveCategory(c.Key, ArchiveCategoryState.Included, c.Value.Files, c.Value.Bytes, c.Value.New, c.Value.Personal))
            .ToList();
        // Several rules can name the same category (a folder and loose screenshots): one line each category.
        categories.AddRange(scan.Exclusions
            .GroupBy(e => CategoryOfRule(e.Rule), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ArchiveCategory(g.Key, ArchiveCategoryState.Excluded, g.Sum(e => e.Files), g.Sum(e => e.Bytes), 0, IsPersonal: true)));
        if (scan.Problems.Count > 0)
            categories.Add(new ArchiveCategory(L.T("ArchivePreview.CouldNotBeRead"), ArchiveCategoryState.NotArchived, scan.Problems.Count, 0, 0, IsPersonal: false));
        return new ArchivePreview
        {
            Scan = scan, Scope = scope, Storage = storage, IncludesPersonalData = includesPersonalData, Categories = categories, NewBytes = newBytes,
        };
    }
}
