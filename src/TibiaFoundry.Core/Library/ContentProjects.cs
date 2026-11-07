using System.Text.Json;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Library;

/// <summary>The kinds of content projects Content Studio starts from (8.8).</summary>
public enum ContentProjectType { CreatureGallery, OutfitGallery, ItemCollection, MapReference, FansiteUpdate, WikiImagePreparation, CustomExport }

/// <summary>One run of a project, for its history.</summary>
public sealed record ProjectRun(DateTime StartedUtc, double Seconds, string Outcome, int Files, string? OutputFolder, IReadOnlyList<string> Problems);

/// <summary>
/// A reusable content project (8.8): what to make (its workflow, whose source steps hold the chosen
/// creatures, outfits, items or collection), how (the export profile and the output folder) and
/// what happened (its history). Saved as JSON in the library's <c>projects</c> folder, so it can be
/// run again when a new client version comes out.
/// </summary>
public sealed class ContentProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = L.T("ContentProjects.NewProject");
    public ContentProjectType Type { get; set; }
    public string Description { get; set; } = "";
    public string ProfileId { get; set; } = ExportProfiles.TibiaWiki.Id;
    /// <summary>The folder runs are written to (a subfolder per project); empty = the default content folder.</summary>
    public string OutputFolder { get; set; } = "";
    public OutputMode OutputMode { get; set; } = OutputMode.NewFolderPerRun;
    public WorkflowDefinition Workflow { get; set; } = new();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public List<ProjectRun> History { get; set; } = [];

    /// <summary>"Never run", "Completed 2026-10-31 14:05", "Failed …".</summary>
    public string Status => History.Count == 0 ? L.T("ContentProjects.NeverRun") : $"{History[^1].Outcome} {History[^1].StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";

    public static string TypeName(ContentProjectType type) => type switch
    {
        ContentProjectType.CreatureGallery => L.T("ContentProjects.CreatureGallery"),
        ContentProjectType.OutfitGallery => L.T("ContentProjects.OutfitGallery"),
        ContentProjectType.ItemCollection => L.T("ContentProjects.ItemImageCollection"),
        ContentProjectType.MapReference => L.T("ContentProjects.MapReferenceProject"),
        ContentProjectType.FansiteUpdate => L.T("ContentProjects.FansiteUpdateResources"),
        ContentProjectType.WikiImagePreparation => L.T("ContentProjects.WikiImagePreparation"),
        _ => L.T("ContentProjects.CustomAssetExport"),
    };

    public static string TypeDescription(ContentProjectType type) => type switch
    {
        ContentProjectType.CreatureGallery => L.T("ContentProjects.RendersTheCreaturesYouChooseWith"),
        ContentProjectType.OutfitGallery => L.T("ContentProjects.RendersOutfitsWithTheirAddonVariants"),
        ContentProjectType.ItemCollection => L.T("ContentProjects.ItemIconsByIDOrName"),
        ContentProjectType.MapReference => L.T("ContentProjects.CollectsYourMapExportsFromThe"),
        ContentProjectType.FansiteUpdate => L.T("ContentProjects.ComparesTheClientWithAnOlder"),
        ContentProjectType.WikiImagePreparation => L.T("ContentProjects.PreparesImagesOfACollectionFor"),
        _ => L.T("ContentProjects.ExportsAnyLibraryCollectionWithThe"),
    };

    public ContentProject Clone(string? name = null)
    {
        var copy = JsonSerializer.Deserialize<ContentProject>(JsonSerializer.Serialize(this, LibraryJson.Options), LibraryJson.Options)!;
        copy.Id = Guid.NewGuid().ToString("N")[..12];
        copy.Name = name ?? L.F("ContentStudio.0Copy", Name);
        copy.History = [];
        copy.CreatedUtc = copy.ModifiedUtc = DateTime.UtcNow;
        return copy;
    }
}

/// <summary>Project templates and the project files of a library.</summary>
public static class ContentProjects
{
    /// <summary>A new project of <paramref name="type"/> with its typical workflow (every step can be changed afterwards).</summary>
    public static ContentProject Create(ContentProjectType type, string? name = null)
    {
        var p = new ContentProject { Type = type, Name = name ?? ContentProject.TypeName(type), Description = ContentProject.TypeDescription(type) };
        var steps = p.Workflow.Steps;
        switch (type)
        {
            case ContentProjectType.CreatureGallery:
                p.ProfileId = ExportProfiles.Gallery.Id;
                steps.Add(new(WorkflowStepType.RenderCreatures, ("names", L.T("ContentProjects.DragonDragonLordDemon")), ("animation", "Moving"), ("animated", "true")));
                steps.Add(new(WorkflowStepType.Resize, ("mode", "Scale"), ("scale", "2")));
                steps.Add(new(WorkflowStepType.Canvas, ("width", "128"), ("height", "128"), ("align", "Bottom")));
                steps.Add(new(WorkflowStepType.ApplyMetadata, ("descriptionTemplate", L.T("ContentProjects.NameAnimatedAnimationClientVersion")), ("tags", "creature")));
                steps.Add(new(WorkflowStepType.Organize, ("template", "Creatures")));
                steps.Add(new(WorkflowStepType.ContactSheet, ("labels", "true")));
                steps.Add(new(WorkflowStepType.Manifest, ("json", "true"), ("csv", "true")));
                steps.Add(new(WorkflowStepType.Gallery, ("title", "{collection}")));
                steps.Add(new(WorkflowStepType.AddToLibrary, ("collection", "{collection}")));
                break;
            case ContentProjectType.OutfitGallery:
                p.ProfileId = ExportProfiles.Gallery.Id;
                steps.Add(new(WorkflowStepType.RenderOutfits, ("ids", "128-131"), ("addons", L.T("ContentProjects.EachVariant")), ("animation", "Moving")));
                steps.Add(new(WorkflowStepType.Resize, ("mode", "Scale"), ("scale", "2")));
                steps.Add(new(WorkflowStepType.Canvas, ("width", "128"), ("height", "128"), ("align", "Bottom")));
                steps.Add(new(WorkflowStepType.Organize, ("template", "Outfits/{group}")));
                steps.Add(new(WorkflowStepType.ComparisonSheets, ("labels", "true")));
                steps.Add(new(WorkflowStepType.Manifest, ("json", "true"), ("csv", "true")));
                steps.Add(new(WorkflowStepType.Gallery, ("title", "{collection}")));
                steps.Add(new(WorkflowStepType.AddToLibrary, ("collection", "{collection}")));
                break;
            case ContentProjectType.ItemCollection:
                p.ProfileId = ExportProfiles.CustomFansite.Id;
                steps.Add(new(WorkflowStepType.RenderObjects, ("kind", "Object"), ("names", "sword\naxe"), ("onlyNamed", "true"), ("animated", "true")));
                steps.Add(new(WorkflowStepType.Canvas, ("width", "32"), ("height", "32"), ("align", "Center")));
                steps.Add(new(WorkflowStepType.Rename, ("template", "{id}-{name}")));
                steps.Add(new(WorkflowStepType.Organize, ("template", "Items")));
                steps.Add(new(WorkflowStepType.ContactSheet, ("labels", "true")));
                steps.Add(new(WorkflowStepType.Manifest, ("json", "true"), ("csv", "true"), ("markdown", "true")));
                steps.Add(new(WorkflowStepType.Zip, ("fileName", "{collection}.zip")));
                steps.Add(new(WorkflowStepType.AddToLibrary, ("collection", "{collection}")));
                break;
            case ContentProjectType.MapReference:
                p.ProfileId = ExportProfiles.PersonalArchive.Id;
                steps.Add(new(WorkflowStepType.SelectLibraryAssets, ("category", AssetCategories.Maps)));
                steps.Add(new(WorkflowStepType.Resize, ("mode", "Fit"), ("maxWidth", "2048"), ("maxHeight", "2048"), ("filter", "Nearest")));
                steps.Add(new(WorkflowStepType.Organize, ("template", "Maps")));
                steps.Add(new(WorkflowStepType.Manifest, ("json", "true"), ("csv", "true")));
                steps.Add(new(WorkflowStepType.Gallery, ("title", "{collection}"), ("tileSize", "240")));
                break;
            case ContentProjectType.FansiteUpdate:
                p.ProfileId = ExportProfiles.CustomFansite.Id;
                steps.Add(new(WorkflowStepType.RenderChangedAppearances, ("objects", "true"), ("outfits", "true"), ("changed", "true")));
                steps.Add(new(WorkflowStepType.Trim));
                steps.Add(new(WorkflowStepType.Resize, ("mode", "Scale"), ("scale", "2")));
                steps.Add(new(WorkflowStepType.Organize, ("template", "{group}/{category}")));
                steps.Add(new(WorkflowStepType.ContactSheet, ("labels", "true"), ("perCategory", "true")));
                steps.Add(new(WorkflowStepType.Manifest, ("json", "true"), ("csv", "true"), ("markdown", "true")));
                steps.Add(new(WorkflowStepType.Gallery, ("title", "{collection}")));
                steps.Add(new(WorkflowStepType.AddToLibrary, ("collection", "{collection}"), ("tags", "update")));
                break;
            case ContentProjectType.WikiImagePreparation:
                p.ProfileId = ExportProfiles.TibiaWiki.Id;
                steps.Add(new(WorkflowStepType.SelectLibraryAssets, ("collection", L.T("ContentProjects.TibiaWikiItemImages"))));
                steps.Add(new(WorkflowStepType.Trim));
                steps.Add(new(WorkflowStepType.ConvertFormat, ("format", "keep")));
                steps.Add(new(WorkflowStepType.Documentation));
                steps.Add(new(WorkflowStepType.Manifest, ("json", "true"), ("csv", "true")));
                break;
            default:
                p.ProfileId = ExportProfiles.CustomFansite.Id;
                steps.Add(new(WorkflowStepType.SelectLibraryAssets, ("collection", "")));
                steps.Add(new(WorkflowStepType.Manifest, ("json", "true"), ("csv", "true")));
                steps.Add(new(WorkflowStepType.Zip, ("fileName", "{collection}.zip")));
                break;
        }
        p.Workflow.Name = p.Name;
        p.Workflow.Description = p.Description;
        return p;
    }

    public static IReadOnlyList<ContentProject> Load(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var list = new List<ContentProject>();
        foreach (string file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                if (new FileInfo(file).Length > 8 << 20) continue;
                var p = JsonSerializer.Deserialize<ContentProject>(File.ReadAllText(file), LibraryJson.Options);
                if (p == null || string.IsNullOrWhiteSpace(p.Id)) continue;
                p.Workflow ??= new WorkflowDefinition();
                p.Workflow.Steps ??= [];
                p.History ??= [];
                list.Add(p);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        }
        return list.OrderByDescending(p => p.ModifiedUtc).ToList();
    }

    public static void Save(string folder, ContentProject project)
    {
        Directory.CreateDirectory(folder);
        project.ModifiedUtc = DateTime.UtcNow;
        if (project.History.Count > 100) project.History = project.History.TakeLast(100).ToList();
        AtomicFile.WriteAllText(Path.Combine(folder, FileNames.Sanitize(project.Id) + ".json"), JsonSerializer.Serialize(project, LibraryJson.Options));
    }

    /// <summary>Deletes the project file (its output folders stay).</summary>
    public static void Delete(string folder, string id)
    {
        string file = Path.Combine(folder, FileNames.Sanitize(id) + ".json");
        if (File.Exists(file)) File.Delete(file);
    }

    /// <summary>Saved workflow templates (the library's <c>workflows</c> folder).</summary>
    public static IReadOnlyList<(string File, WorkflowDefinition Workflow)> LoadWorkflows(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var list = new List<(string, WorkflowDefinition)>();
        foreach (string file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                if (new FileInfo(file).Length > 4 << 20) continue;
                list.Add((file, WorkflowDefinition.FromJson(File.ReadAllText(file))));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { }
        }
        return list.OrderBy(w => w.Item2.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string SaveWorkflow(string folder, WorkflowDefinition workflow)
    {
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, FileNames.Sanitize(workflow.Name) + ".json");
        AtomicFile.WriteAllText(file, workflow.ToJson());
        return file;
    }
}
