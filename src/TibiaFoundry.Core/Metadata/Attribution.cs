using System.Globalization;
using System.Text;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Metadata;

/// <summary>Attribution found in an existing file. Every part may be missing.</summary>
public sealed record FoundAttribution(IReadOnlyList<string> Creators, string? Text, string? Copyright, string? Software, IReadOnlyList<string> History)
{
    public static FoundAttribution Empty { get; } = new([], null, null, null, []);

    public bool IsEmpty => Creators.Count == 0 && Text == null && Copyright == null && History.Count == 0;
}

/// <summary>What Tibia Foundry embeds into an exported file.</summary>
public sealed record AttributionInfo
{
    /// <summary>
    /// Creator(s) of the content (XMP dc:creator, PNG "Author"): the configured user for new
    /// assets; the original creators when an imported, already attributed file is re-exported.
    /// </summary>
    public required IReadOnlyList<string> Creators { get; init; }

    /// <summary>The attribution sentence, e.g. "Created by RockyLaRo using TibiaFoundry for TibiaWiki @ https://tibia.fandom.com/".</summary>
    public required string Text { get; init; }

    /// <summary>Configured user who ran this export.</summary>
    public required string ExportedBy { get; init; }

    public required DateTimeOffset Date { get; init; }

    /// <summary>Rights statement of the depicted artwork (Tibia graphics belong to CipSoft), or null when unknown.</summary>
    public string? Copyright { get; init; }

    /// <summary>Provenance, oldest first; the last entry describes this export.</summary>
    public IReadOnlyList<string> History { get; init; } = [];

    public string Software { get; init; } = AppIdentity.NameAndVersion;

    public string Application => AppIdentity.Id;

    public string Website => AppIdentity.Website;

    /// <summary>Creators as one line ("Alice; Bob").</summary>
    public string CreatorText => string.Join("; ", Creators);
}

/// <summary>How to attribute a file made from inputs that name another creator.</summary>
public enum AttributionChoice
{
    /// <summary>Keep the original creators and record the current user as editor (default; never misattributes).</summary>
    KeepOriginalCreators,
    /// <summary>The user explicitly takes over the attribution; the previous creators stay in the history.</summary>
    AttributeToMe,
}

/// <summary>The attribution found in the inputs of an export, and which creators differ from the current user.</summary>
public sealed record AttributionReview(IReadOnlyList<FoundAttribution> Sources, IReadOnlyList<string> OtherCreators, string CurrentCreator)
{
    /// <summary>True when the inputs were attributed to someone else, so the user should decide.</summary>
    public bool HasConflict => OtherCreators.Count > 0;

    public string Describe() => HasConflict
        ? L.F("Attribution.TheSelectedImagesName0As", Attribution.JoinNames(OtherCreators), CurrentCreator)
        : L.T("Attribution.NoneOfTheSelectedImagesNames");
}

/// <summary>An export could not embed or verify its attribution. Nothing was written.</summary>
public sealed class AttributionException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>
/// Creator attribution policy, shared by every export:
/// <list type="bullet">
/// <item>New assets (made from client data) are attributed to the configured creator with the
/// required sentence "Created by &lt;user&gt; using TibiaFoundry for TibiaWiki @ https://tibia.fandom.com/".
/// The depicted artwork is credited to CipSoft separately (copyright field).</item>
/// <item>Re-exports of imported files that already name a creator keep that creator unless the
/// user explicitly decides otherwise; the current user is recorded as editor in the history.</item>
/// </list>
/// </summary>
public static class Attribution
{
    /// <summary>Default creator of this distribution (its developer); users change it in Settings.</summary>
    public const string DefaultCreator = AppIdentity.Creator;

    /// <summary>Rights notice for graphics made from Tibia client data.</summary>
    public const string TibiaArtworkNotice = "Tibia game graphics © CipSoft GmbH";

    /// <summary>Shown after data exports, whose formats cannot carry attribution without changing the data.</summary>
    public const string NoMetadataNote = "No attribution was embedded: JSON and tab-separated data files have no metadata field.";

    public const int MaxCreatorLength = 64;
    private const int MaxHistoryEntries = 40;

    private static string _currentCreator = DefaultCreator;

    /// <summary>The creator name used for new exports (set from the settings at start-up and when changed).</summary>
    public static string CurrentCreator
    {
        get => Volatile.Read(ref _currentCreator);
        set => Volatile.Write(ref _currentCreator, NormalizeCreator(value) ?? DefaultCreator);
    }

    /// <summary>Trims, collapses white space, removes control characters and limits the length; null when nothing is left.</summary>
    public static string? NormalizeCreator(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var sb = new StringBuilder(name.Length);
        bool space = false;
        foreach (char c in name.Trim())
        {
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (char.IsControl(c) || c is (char)0xFEFF or (char)0xFFFE or (char)0xFFFF) continue;
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        string result = sb.Length > MaxCreatorLength ? sb.ToString(0, MaxCreatorLength).TrimEnd() : sb.ToString();
        return result.Length == 0 ? null : result;
    }

    /// <summary>The required attribution sentence for <paramref name="creator"/>.</summary>
    public static string TextFor(string creator) => $"Created by {creator} using {AppIdentity.Id} for TibiaWiki @ {AppIdentity.Website}"; // loc: identity (written into files and read back)

    public static string HistoryEntry(DateTimeOffset date, string what) =>
        $"{date.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)} {what} with {AppIdentity.NameAndVersion}"; // loc: identity (written into files and read back)

    /// <summary>Attribution of a newly created asset.</summary>
    /// <param name="copyright">Rights of the depicted artwork; client-data graphics default to the CipSoft notice.</param>
    public static AttributionInfo ForNewAsset(string? creator = null, string? copyright = TibiaArtworkNotice, DateTimeOffset? date = null)
    {
        string user = NormalizeCreator(creator) ?? CurrentCreator;
        var when = date ?? DateTimeOffset.Now;
        return new AttributionInfo
        {
            Creators = [user],
            Text = TextFor(user),
            ExportedBy = user,
            Date = when,
            Copyright = copyright,
            History = [HistoryEntry(when, $"Created by {user}")], // loc: identity (written into files and read back)
        };
    }

    /// <summary>Which creators of the inputs differ from the current user.</summary>
    public static AttributionReview Review(IEnumerable<FoundAttribution?> sources, string? creator = null)
    {
        string user = NormalizeCreator(creator) ?? CurrentCreator;
        var found = sources.Where(s => s is { IsEmpty: false }).Select(s => s!).ToList();
        var others = DistinctNames(found.SelectMany(s => s.Creators)).Where(c => !SameName(c, user)).ToList();
        return new AttributionReview(found, others, user);
    }

    /// <summary>
    /// Attribution of a file made from imported files (sliced, combined, converted, scaled).
    /// Inputs without attribution make it a new asset of the current user.
    /// </summary>
    /// <param name="copyrightIfUnknown">Rights notice when no input carries one (null for arbitrary user images).</param>
    /// <param name="action">What was done, for the history (e.g. "Converted to PNG"); "Edited" when null.</param>
    public static AttributionInfo ForDerivedAsset(IEnumerable<FoundAttribution?> sources, AttributionChoice choice, string? creator = null,
        string? copyrightIfUnknown = null, DateTimeOffset? date = null, string? action = null)
    {
        var review = Review(sources, creator);
        string user = review.CurrentCreator;
        var when = date ?? DateTimeOffset.Now;
        if (review.Sources.Count == 0)
        {
            var fresh = ForNewAsset(user, copyrightIfUnknown, when);
            // A processed file whose source named nobody: the history says what happened instead of "Created by".
            return action == null ? fresh : fresh with { History = [HistoryEntry(when, $"{action} by {user} (the source named no creator)")] }; // loc: identity (written into files and read back)
        }

        var allCreators = DistinctNames(review.Sources.SelectMany(s => s.Creators)).ToList();
        var copyrights = review.Sources.Select(s => s.Copyright).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal).ToList();
        var history = review.Sources.SelectMany(s => s.History).Distinct(StringComparer.Ordinal).ToList();
        string? copyright = copyrights.Count > 0 ? string.Join("; ", copyrights) : copyrightIfUnknown;

        IReadOnlyList<string> creators;
        string text, entry;
        if (!review.HasConflict)
        {
            creators = [user];
            text = TextFor(user);
            entry = $"{action ?? "Edited"} by {user}"; // loc: identity (written into files and read back)
        }
        else if (choice == AttributionChoice.KeepOriginalCreators)
        {
            creators = allCreators;
            text = OriginalText(review.Sources) + $" | Edited by {user} using {AppIdentity.Id} for TibiaWiki @ {AppIdentity.Website}"; // loc: identity (written into files and read back)
            entry = $"{action ?? "Edited"} by {user}; original creators kept: {JoinNames(review.OtherCreators)}"; // loc: identity (written into files and read back)
        }
        else
        {
            creators = [user];
            text = TextFor(user);
            entry = $"Re-attributed to {user} by {user} (previously attributed to {JoinNames(review.OtherCreators)})"; // loc: identity (written into files and read back)
        }
        history.Add(HistoryEntry(when, entry));
        if (history.Count > MaxHistoryEntries) history = history.Skip(history.Count - MaxHistoryEntries).ToList();
        return new AttributionInfo { Creators = creators, Text = text, ExportedBy = user, Date = when, Copyright = copyright, History = history };
    }

    /// <summary>Throws <see cref="AttributionException"/> when <paramref name="info"/> cannot be embedded.</summary>
    public static void Validate(AttributionInfo info)
    {
        if (info.Creators.Count == 0 || info.Creators.Any(string.IsNullOrWhiteSpace))
            throw new AttributionException("The attribution has no creator name. Set your creator name in Settings.");
        if (string.IsNullOrWhiteSpace(info.Text))
            throw new AttributionException("The attribution text is empty.");
        foreach (string value in info.Creators.Append(info.Text).Append(info.Copyright ?? "").Concat(info.History))
            if (value.Any(c => c == '\0' || (char.IsControl(c) && c is not '\n')))
                throw new AttributionException("The attribution contains control characters and cannot be embedded.");
        if (info.Text.Length > 4000) throw new AttributionException("The attribution text is too long to embed.");
    }

    internal static string JoinNames(IEnumerable<string> names) => string.Join(", ", names);

    internal static bool SameName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> DistinctNames(IEnumerable<string> names) =>
        names.Select(n => NormalizeCreator(n)).Where(n => n != null).Select(n => n!).Distinct(StringComparer.OrdinalIgnoreCase);

    private static string OriginalText(IEnumerable<FoundAttribution> sources)
    {
        var texts = new List<string>();
        foreach (var s in sources)
        {
            string? t = s.Text?.Trim();
            if (string.IsNullOrEmpty(t) && s.Creators.Count > 0) t = "Created by " + JoinNames(s.Creators); // loc: identity (written into files and read back)
            if (!string.IsNullOrEmpty(t) && !texts.Contains(t, StringComparer.Ordinal)) texts.Add(t);
        }
        return string.Join(" | ", texts);
    }
}
