using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Library;

/// <summary>How names are written in file names.</summary>
public enum NameCase { AsIs, Lower, Upper, Title }

/// <summary>What happens to animated assets when a profile exports them.</summary>
public enum AnimatedOutput { Gif, FirstFramePng }

/// <summary>
/// A publishing profile (8.8): how one website wants its images. Profiles are plain JSON in the
/// library's <c>profiles</c> folder, editable in Content Studio, and nothing in them assumes any
/// particular site: the built-in ones are editable starting points.
/// </summary>
public sealed record ExportProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>The website it is for ("TibiaWiki", "My fansite"), shown in manifests and galleries.</summary>
    public string Website { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>"png", "gif" or "keep" (still images as PNG, animations as GIF).</summary>
    public string ImageFormat { get; init; } = "keep";
    public AnimatedOutput Animated { get; init; } = AnimatedOutput.Gif;
    /// <summary>Whole-number nearest-neighbor enlargement (1–8).</summary>
    public int Scale { get; init; } = 1;
    /// <summary>Fixed canvas size (0 = the image's own size).</summary>
    public int CanvasWidth { get; init; }
    public int CanvasHeight { get; init; }
    public int Padding { get; init; }
    /// <summary>"transparent" or a color "#RRGGBB".</summary>
    public string Background { get; init; } = "transparent";
    /// <summary>Remove fully transparent borders before the canvas is applied.</summary>
    public bool TrimTransparent { get; init; }
    public string FileNameTemplate { get; init; } = "{name}";
    public string FolderTemplate { get; init; } = "{category}";
    public NameCase Case { get; init; } = NameCase.AsIs;
    /// <summary>What replaces spaces in file and folder names ("" keeps them).</summary>
    public string SpaceReplacement { get; init; } = "";
    public bool WriteJsonManifest { get; init; } = true;
    public bool WriteCsvManifest { get; init; }
    public bool WriteMarkdownIndex { get; init; }
    public bool WriteGallery { get; init; }
    /// <summary>Credit line written into manifests, galleries and documentation ({creator}, {name}…).</summary>
    public string AttributionTemplate { get; init; } = L.T("ExportProfiles.NameTibiaGraphicsCipSoftGmbHImage");
    /// <summary>A text snippet per asset (wiki markup, HTML, BBCode…), collected in <see cref="DocumentationFileName"/>; empty = none.</summary>
    public string DocumentationTemplate { get; init; } = "";
    public string DocumentationFileName { get; init; } = "snippets.txt";
    public bool BuiltIn { get; init; }

    /// <summary>The output format of an asset: "png" or "gif".</summary>
    public string FormatFor(bool animated) => ImageFormat switch
    {
        "png" => "png",
        "gif" => "gif",
        _ => animated && Animated == AnimatedOutput.Gif ? "gif" : "png",
    };
}

/// <summary>The built-in profiles and the profile files of a library.</summary>
public static class ExportProfiles
{
    public static ExportProfile TibiaWiki { get; } = new()
    {
        Id = "tibiawiki", Name = "TibiaWiki", Website = L.T("ExportProfiles.TibiaWikiTibiaFandomCom"), BuiltIn = true,
        Description = L.T("ExportProfiles.ClientSize1StillImagesAs"),
        Scale = 1, ImageFormat = "keep", FileNameTemplate = "{name}", FolderTemplate = "{category}", Case = NameCase.AsIs,
        WriteJsonManifest = true, WriteCsvManifest = true,
        AttributionTemplate = L.T("ExportProfiles.NameTibiaGraphicsCipSoftGmbHUploaded"),
        DocumentationTemplate = "[[File:{file}|{name}]]", DocumentationFileName = "wiki-file-links.txt",
    };

    public static ExportProfile CustomFansite { get; } = new()
    {
        Id = "custom-fansite", Name = L.T("ExportProfiles.CustomFansite"), Website = L.T("ExportProfiles.MyFansite"), BuiltIn = true,
        Description = L.T("ExportProfiles.2EnlargementLowerCaseFileNames"),
        Scale = 2, ImageFormat = "keep", FileNameTemplate = "{name}", FolderTemplate = "{category}", Case = NameCase.Lower, SpaceReplacement = "-",
        WriteJsonManifest = true, WriteCsvManifest = true,
        DocumentationTemplate = "<img src=\"{folder}/{file}\" alt=\"{name}\" width=\"{width}\" height=\"{height}\" style=\"image-rendering: pixelated\">",
        DocumentationFileName = "html-snippets.txt",
    };

    public static ExportProfile Gallery { get; } = new()
    {
        Id = "gallery", Name = L.T("ExportProfiles.GalleryWebsite"), Website = L.T("ExportProfiles.ImageGallery"), BuiltIn = true,
        Description = L.T("ExportProfiles.2OnAFixed128128"),
        Scale = 2, CanvasWidth = 128, CanvasHeight = 128, ImageFormat = "keep", FileNameTemplate = "{name}", FolderTemplate = "{category}",
        Case = NameCase.Lower, SpaceReplacement = "_", WriteJsonManifest = true, WriteCsvManifest = true, WriteGallery = true,
    };

    public static ExportProfile Article { get; } = new()
    {
        Id = "article", Name = L.T("ExportProfiles.ArticlePublishing"), Website = L.T("ExportProfiles.NewsAndGuideArticles"), BuiltIn = true,
        Description = L.T("ExportProfiles.3EnlargementForIllustrationsInArticles"),
        Scale = 3, ImageFormat = "keep", FileNameTemplate = "{name}_{scale}x", FolderTemplate = "", Case = NameCase.Lower, SpaceReplacement = "-",
        WriteJsonManifest = false, WriteMarkdownIndex = true,
        DocumentationTemplate = "![{name}]({file})\n*{attribution}*\n", DocumentationFileName = "article-images.md",
    };

    public static ExportProfile PersonalArchive { get; } = new()
    {
        Id = "archive", Name = L.T("ExportProfiles.PersonalArchive"), Website = L.T("ExportProfiles.LocalArchive"), BuiltIn = true,
        Description = L.T("ExportProfiles.OriginalSizeFilesSortedByClient"),
        Scale = 1, ImageFormat = "keep", FileNameTemplate = L.T("ExportProfiles.IdName"), FolderTemplate = "{version}/{category}", WriteJsonManifest = true, WriteCsvManifest = true, WriteMarkdownIndex = true,
    };

    public static IReadOnlyList<ExportProfile> BuiltIns { get; } = [TibiaWiki, CustomFansite, Gallery, Article, PersonalArchive];

    /// <summary>The library's profiles: the built-in ones (or the user's edited copies of them) and the user's own.</summary>
    public static IReadOnlyList<ExportProfile> Load(string folder)
    {
        var byId = BuiltIns.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(folder))
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*.json"))
            {
                try
                {
                    if (new FileInfo(file).Length > 1 << 20) continue;
                    var p = JsonSerializer.Deserialize<ExportProfile>(File.ReadAllText(file), LibraryJson.Options);
                    if (p != null && !string.IsNullOrWhiteSpace(p.Id)) byId[p.Id] = Normalize(p) with { BuiltIn = BuiltIns.Any(b => b.Id == p.Id) };
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
            }
        }
        return byId.Values.OrderBy(p => p.BuiltIn ? 0 : 1).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static ExportProfile Find(string folder, string? id) =>
        Load(folder).FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? TibiaWiki;

    public static void Save(string folder, ExportProfile profile)
    {
        Directory.CreateDirectory(folder);
        var problems = Validate(profile);
        if (problems.Count > 0) throw new ArgumentException(string.Join(" ", problems));
        AtomicFile.WriteAllText(Path.Combine(folder, FileNames.Sanitize(profile.Id) + ".json"), JsonSerializer.Serialize(Normalize(profile), LibraryJson.Options));
    }

    /// <summary>Deletes a user profile, or resets an edited built-in profile to its original.</summary>
    public static void Delete(string folder, string id)
    {
        string file = Path.Combine(folder, FileNames.Sanitize(id) + ".json");
        if (File.Exists(file)) File.Delete(file);
    }

    /// <summary>A new ID from a name ("My Site" → "my-site", unique in the folder).</summary>
    public static string NewId(string folder, string name)
    {
        string slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0) slug = "profile";
        string id = slug;
        var existing = Load(folder).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int n = 2; existing.Contains(id); n++) id = $"{slug}-{n}";
        return id;
    }

    public static ExportProfile Normalize(ExportProfile p) => p with
    {
        Scale = Math.Clamp(p.Scale, 1, 8),
        CanvasWidth = Math.Clamp(p.CanvasWidth, 0, 4096),
        CanvasHeight = Math.Clamp(p.CanvasHeight, 0, 4096),
        Padding = Math.Clamp(p.Padding, 0, 512),
        ImageFormat = p.ImageFormat is "png" or "gif" ? p.ImageFormat : "keep",
        SpaceReplacement = p.SpaceReplacement is "_" or "-" ? p.SpaceReplacement : "",
        DocumentationFileName = string.IsNullOrWhiteSpace(p.DocumentationFileName) ? "snippets.txt" : FileNames.Sanitize(p.DocumentationFileName),
    };

    /// <summary>What is wrong with a profile (empty when it can be used).</summary>
    public static IReadOnlyList<string> Validate(ExportProfile p)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(p.Name)) problems.Add(L.T("ExportProfiles.TheProfileNeedsAName"));
        if (string.IsNullOrWhiteSpace(p.FileNameTemplate)) problems.Add(L.T("ExportProfiles.TheFileNameTemplateCannotBe"));
        foreach (string t in new[] { p.FileNameTemplate, p.FolderTemplate, p.AttributionTemplate, p.DocumentationTemplate })
            foreach (string v in TemplateText.UnknownVariables(t)) problems.Add(L.F("ExportProfiles.UnknownVariable0", v));
        if (p.Background != "transparent" && !Regex.IsMatch(p.Background, "^#[0-9A-Fa-f]{6}$")) problems.Add(L.T("ExportProfiles.TheBackgroundMustBeTransparentOr"));
        return problems.Distinct().ToList();
    }
}

/// <summary>
/// File name, folder and text templates (8.8): <c>{name}</c>, <c>{id}</c>, <c>{category}</c>…
/// replaced by the asset's values. In file and folder names, characters Windows does not allow are
/// replaced, and a folder template can never leave the output folder.
/// </summary>
public static partial class TemplateText
{
    /// <summary>The variables and what they stand for (shown in Content Studio).</summary>
    public static IReadOnlyList<(string Name, string Meaning)> Variables { get; } =
    [
        ("name", L.T("ExportProfiles.TheAssetSNameDragonLord")), ("id", L.T("ExportProfiles.TheGameSIDOfThe")), ("category", L.T("ExportProfiles.CategoryCreaturesItems")),
        ("version", L.T("ExportProfiles.ClientVersion")), ("animation", L.T("ExportProfiles.IdleMovingOrStill")), ("direction", L.T("ExportProfiles.NorthEastSouthOrWest")), ("width", L.T("ExportProfiles.WidthInPixelsAfterScaling")),
        ("height", L.T("ExportProfiles.HeightInPixelsAfterScaling")), ("dimensions", "Width×height"), ("scale", L.T("ExportProfiles.Enlargement12")), ("collection", L.T("ExportProfiles.CollectionOrProjectName")),
        ("index", L.T("ExportProfiles.PositionInTheRun12")), ("index3", L.T("ExportProfiles.PositionWithThreeDigits001")), ("date", L.T("ExportProfiles.DateOfTheRunYyyyMM")),
        ("creator", L.T("ExportProfiles.YourCreatorName")), ("format", L.T("ExportProfiles.PngOrGif")), ("file", L.T("ExportProfiles.TheFileNameWrittenInTexts")), ("folder", L.T("ExportProfiles.TheFolderWrittenInTexts")),
        ("attribution", L.T("ExportProfiles.TheCreditLineInTexts")), ("website", L.T("ExportProfiles.TheProfileSWebsite")), ("source", L.T("ExportProfiles.WhereTheAssetCameFrom")), ("group", L.T("ExportProfiles.ComparisonGroupOutfitCreature")),
    ];

    private static readonly HashSet<string> Known = Variables.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\{([A-Za-z0-9]+)\}")]
    private static partial Regex Placeholder();

    public static IEnumerable<string> UnknownVariables(string? template) =>
        string.IsNullOrEmpty(template) ? [] : Placeholder().Matches(template).Select(m => m.Groups[1].Value).Where(v => !Known.Contains(v)).Distinct();

    /// <summary>Replaces the variables of a text template (unknown and missing values become empty).</summary>
    public static string Expand(string template, IReadOnlyDictionary<string, string?> values) =>
        Placeholder().Replace(template, m => values.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var v) ? v ?? "" : "");

    /// <summary>A file name (without extension) from a template, following the profile's casing and spaces.</summary>
    public static string FileName(string template, IReadOnlyDictionary<string, string?> values, NameCase nameCase = NameCase.AsIs, string spaces = "")
    {
        string name = Clean(Expand(template, values), nameCase, spaces);
        return name.Length == 0 || name == "_" ? Clean(values.GetValueOrDefault("name") ?? "asset", nameCase, spaces) : name;
    }

    /// <summary>A relative folder from a template ("{version}/{category}" → "15.33/Creatures"); never absolute, never "..".</summary>
    public static string Folder(string template, IReadOnlyDictionary<string, string?> values, NameCase nameCase = NameCase.AsIs, string spaces = "")
    {
        var parts = Expand(template ?? "", values).Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => Clean(p, nameCase, spaces)).Where(p => p.Length > 0 && p != "_" && p != "." && p != "..");
        return string.Join(Path.DirectorySeparatorChar, parts);
    }

    private static string Clean(string text, NameCase nameCase, string spaces)
    {
        text = Regex.Replace(text.Trim(), @"\s+", " ");
        text = nameCase switch
        {
            NameCase.Lower => text.ToLowerInvariant(),
            NameCase.Upper => text.ToUpperInvariant(),
            NameCase.Title => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant()),
            _ => text,
        };
        if (spaces.Length > 0) text = text.Replace(" ", spaces);
        text = FileNames.Sanitize(text).Replace("..", ".");
        return text.Trim('.', ' ');
    }

    /// <summary>The standard values of an item of a run.</summary>
    public static Dictionary<string, string?> Values(string name, string? id, string category, string? version, string? animation, string? direction,
        int width, int height, int scale, string? collection, int index, string? creator, string format, string? website = null, string? source = null, string? group = null) => new()
    {
        ["name"] = name, ["id"] = id, ["category"] = category, ["version"] = version, ["animation"] = animation, ["direction"] = direction,
        ["width"] = width.ToString(CultureInfo.InvariantCulture), ["height"] = height.ToString(CultureInfo.InvariantCulture),
        ["dimensions"] = $"{width}×{height}", ["scale"] = scale.ToString(CultureInfo.InvariantCulture), ["collection"] = collection,
        ["index"] = index.ToString(CultureInfo.InvariantCulture), ["index3"] = index.ToString("000", CultureInfo.InvariantCulture),
        ["date"] = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["creator"] = creator, ["format"] = format, ["website"] = website,
        ["source"] = source, ["group"] = group,
    };

    /// <summary>A short preview of a template with sample values (Content Studio).</summary>
    public static string Sample(string template, ExportProfile profile, bool folder = false)
    {
        var values = Values(L.T("ExportProfiles.DragonLord"), "39", "Creatures", "15.33", "Moving", "South", 64 * profile.Scale, 64 * profile.Scale, profile.Scale, L.T("ExportProfiles.CreatureGallery"), 1, "RockyLaRo", "gif",
            profile.Website);
        return folder ? Folder(template, values, profile.Case, profile.SpaceReplacement) : FileName(template, values, profile.Case, profile.SpaceReplacement) + ".gif";
    }
}
