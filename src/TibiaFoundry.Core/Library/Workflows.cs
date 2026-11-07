using System.Globalization;
using System.Text.Json;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Library;

/// <summary>The operations a workflow can combine (8.8).</summary>
public enum WorkflowStepType
{
    // Where the images come from.
    SelectLibraryAssets, ImportFolder, RenderCreatures, RenderOutfits, RenderObjects, RenderChangedAppearances, ExtractSprites,
    // What is done to each image.
    Trim, Resize, Canvas, ConvertFormat, ApplyMetadata, Rename, Organize,
    // What is made from all of them.
    ContactSheet, ComparisonSheets, Manifest, Gallery, Documentation, Zip, AddToLibrary,
}

public enum StepGroup { Source, Process, Output }

public enum ParameterKind { Text, MultilineText, Integer, Boolean, Choice, Folder, File }

/// <summary>A setting of a step, as Content Studio shows it.</summary>
public sealed record StepParameter(string Key, string Label, ParameterKind Kind, string Default, string Help = "")
{
    public IReadOnlyList<string> Choices { get; init; } = [];
    public int Min { get; init; }
    public int Max { get; init; } = 100_000;
}

/// <summary>What a step type is, does and needs.</summary>
public sealed record StepDefinition(WorkflowStepType Type, StepGroup Group, string Title, string Description, IReadOnlyList<StepParameter> Parameters)
{
    /// <summary>Needs the client's appearances and sprites (a client must be set).</summary>
    public bool NeedsClient { get; init; }
    /// <summary>Needs the client's static data (creatures).</summary>
    public bool NeedsStaticData { get; init; }
}

/// <summary>One step of a workflow and its settings (stored as text, so workflow files stay readable and editable).</summary>
public sealed class WorkflowStep
{
    public WorkflowStepType Type { get; set; }
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public WorkflowStep() { }

    public WorkflowStep(WorkflowStepType type, params (string Key, string Value)[] parameters)
    {
        Type = type;
        foreach (var (k, v) in parameters) Parameters[k] = v;
    }

    public StepDefinition Definition => WorkflowSteps.Get(Type);

    public string Get(string key)
    {
        if (Parameters.TryGetValue(key, out var v)) return v;
        return Definition.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))?.Default ?? "";
    }

    public int GetInt(string key)
    {
        var def = Definition.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        int value = int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : int.TryParse(def?.Default, out int d) ? d : 0;
        return def == null ? value : Math.Clamp(value, def.Min, def.Max);
    }

    public bool GetBool(string key) => bool.TryParse(Get(key), out bool b) && b;

    public WorkflowStep Clone() => new() { Type = Type, Enabled = Enabled, Parameters = new Dictionary<string, string>(Parameters, StringComparer.OrdinalIgnoreCase) };

    /// <summary>A one-line summary of the settings that differ from the defaults ("Scale 2×, Nearest").</summary>
    public string Summary
    {
        get
        {
            // The settings the step sets (templates and the user), at most four, so the list shows what each step does.
            var parts = Definition.Parameters.Where(p => Parameters.TryGetValue(p.Key, out var v) && v.Length > 0)
                .Select(p => p.Kind == ParameterKind.Boolean ? (GetBool(p.Key) ? p.Label : "not " + p.Label.ToLowerInvariant()) : $"{p.Label}: {Shorten(Parameters[p.Key])}").Take(4);
            string text = string.Join(" · ", parts);
            return text.Length == 0 ? L.T("Workflows.DefaultSettings") : text;
        }
    }

    private static string Shorten(string s)
    {
        s = s.ReplaceLineEndings(", ");
        return s.Length > 48 ? s[..45] + "…" : s;
    }
}

/// <summary>A reusable sequence of steps (8.8). Saved as JSON; can be exported, shared and imported.</summary>
public sealed class WorkflowDefinition
{
    public const string FormatName = "tibiafoundry-workflow";
    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Workflow";
    public string Description { get; set; } = "";
    public List<WorkflowStep> Steps { get; set; } = [];

    public WorkflowDefinition Clone() => new() { Name = Name, Description = Description, Steps = Steps.Select(s => s.Clone()).ToList() };

    public string ToJson() => JsonSerializer.Serialize(this, LibraryJson.Options);

    /// <summary>Reads a workflow file. Only the known step types and their text settings are read; nothing in a workflow file is executed.</summary>
    public static WorkflowDefinition FromJson(string json)
    {
        WorkflowDefinition? w;
        try
        {
            w = JsonSerializer.Deserialize<WorkflowDefinition>(json, LibraryJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("This is not a Tibia Foundry workflow: " + ex.Message, ex);
        }
        if (w == null || w.Format != FormatName) throw new InvalidDataException("This is not a Tibia Foundry workflow file.");
        w.Steps ??= [];
        foreach (var s in w.Steps)
        {
            s.Parameters = new Dictionary<string, string>((s.Parameters ?? []).Where(p => p.Key != null && p.Value != null && p.Value.Length <= 100_000), StringComparer.OrdinalIgnoreCase);
            if (!Enum.IsDefined(s.Type)) throw new InvalidDataException($"The workflow uses an unknown step ({(int)s.Type}).");
        }
        return w;
    }

    /// <summary>Problems that stop the workflow from running (empty when it can run).</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        var active = Steps.Where(s => s.Enabled).ToList();
        if (!active.Any(s => s.Definition.Group == StepGroup.Source)) problems.Add(L.T("Workflows.AddAStepThatChoosesThe"));
        foreach (var s in active)
            foreach (var p in s.Definition.Parameters.Where(p => p.Kind is ParameterKind.Text or ParameterKind.Folder or ParameterKind.File or ParameterKind.MultilineText))
                foreach (string v in TemplateText.UnknownVariables(s.Get(p.Key)).Where(_ => p.Key.Contains("template", StringComparison.OrdinalIgnoreCase)))
                    problems.Add(L.F("Workflows.0UnknownVariable1In2", s.Definition.Title, v, p.Label));
        return problems;
    }
}

/// <summary>The catalog of step types: titles, descriptions and settings.</summary>
public static class WorkflowSteps
{
    private static readonly string[] Directions = ["South", "East", "North", "West"];
    private static readonly string[] Animations = ["Moving", "Idle"];

    public static IReadOnlyList<StepDefinition> All { get; } =
    [
        new(WorkflowStepType.SelectLibraryAssets, StepGroup.Source, L.T("Workflows.LibraryAssets"), L.T("Workflows.ImagesFromTheAssetLibraryA"),
        [
            new("collection", "Collection", ParameterKind.Text, "", L.T("Workflows.NameOfACollectionEmptyNot")),
            new("query", "Search", ParameterKind.Text, "", L.T("Workflows.WordsThatMustMatchNameID")),
            new("category", "Category", ParameterKind.Text, "", L.T("Workflows.OnlyThisCategoryEmptyAny")),
            new("assetIds", L.T("Workflows.AssetNumbers"), ParameterKind.Text, "", L.T("Workflows.LibraryAssetNumbersCommaSeparated")),
        ]),
        new(WorkflowStepType.ImportFolder, StepGroup.Source, L.T("Workflows.ImportAFolder"), L.T("Workflows.PNGGIFAndOtherImagesFrom"),
        [
            new("folder", "Folder", ParameterKind.Folder, ""),
            new("recursive", L.T("Workflows.IncludeSubfolders"), ParameterKind.Boolean, "true"),
        ]),
        new(WorkflowStepType.RenderCreatures, StepGroup.Source, L.T("Workflows.RenderCreatures"), L.T("Workflows.CreaturesAndBossesFromTheClient"),
        [
            new("names", "Creatures", ParameterKind.MultilineText, "", L.T("Workflows.OneNamePerLineEveryCreature")),
            new("includeBosses", L.T("Workflows.IncludeBosses"), ParameterKind.Boolean, "true"),
            new("animation", "Animation", ParameterKind.Choice, "Moving") { Choices = Animations },
            new("direction", "Direction", ParameterKind.Choice, "South") { Choices = Directions },
            new("animated", "Animated", ParameterKind.Boolean, "true", L.T("Workflows.OffAStillImageOfThe")),
            new("allDirections", L.T("Workflows.AllFourDirections"), ParameterKind.Boolean, "false", L.T("Workflows.OneImagePerDirectionGroupedFor")),
        ]) { NeedsClient = true, NeedsStaticData = true },
        new(WorkflowStepType.RenderOutfits, StepGroup.Source, L.T("Workflows.RenderOutfits"), L.T("Workflows.OutfitsByIDWithColorsAddons"),
        [
            new("ids", L.T("Workflows.OutfitIDs"), ParameterKind.Text, "", L.T("Workflows.ForExample128134136")),
            new("addons", "Addons", ParameterKind.Choice, "Both") { Choices = ["None", "First", "Second", "Both", L.T("Workflows.EachVariant")] },
            new("colors", "Colors", ParameterKind.Text, "78, 69, 58, 76", L.T("Workflows.HeadPrimarySecondaryDetail0132")),
            new("mount", L.T("Workflows.MountID"), ParameterKind.Integer, "0", L.T("Workflows.0NoMount")) { Max = 100_000 },
            new("animation", "Animation", ParameterKind.Choice, "Moving") { Choices = Animations },
            new("direction", "Direction", ParameterKind.Choice, "South") { Choices = Directions },
            new("animated", "Animated", ParameterKind.Boolean, "true"),
            new("allDirections", L.T("Workflows.AllFourDirections"), ParameterKind.Boolean, "false"),
        ]) { NeedsClient = true },
        new(WorkflowStepType.RenderObjects, StepGroup.Source, L.T("Workflows.RenderItemsAndObjects"), L.T("Workflows.ObjectsByIDOrNameItems"),
        [
            new("kind", "Kind", ParameterKind.Choice, "Object") { Choices = ["Object", "Effect", "Missile"] },
            new("ids", "IDs", ParameterKind.Text, "", L.T("Workflows.ForExample303130353043Or")),
            new("names", "Names", ParameterKind.MultilineText, "", L.T("Workflows.ObjectsWhoseNameContainsOneOf")),
            new("onlyNamed", L.T("Workflows.OnlyObjectsWithAName"), ParameterKind.Boolean, "true"),
            new("animated", "Animated", ParameterKind.Boolean, "true"),
        ]) { NeedsClient = true },
        new(WorkflowStepType.RenderChangedAppearances, StepGroup.Source, L.T("Workflows.NewAndChangedSinceAVersion"), L.T("Workflows.ComparesTheClientWithAnOlder"),
        [
            new("previous", L.T("Workflows.OlderAppearancesFile"), ParameterKind.File, "", "appearances-*.dat of the previous client version"),
            new("objects", "Objects", ParameterKind.Boolean, "true"),
            new("outfits", "Outfits", ParameterKind.Boolean, "true"),
            new("changed", L.T("Workflows.IncludeChangedGraphics"), ParameterKind.Boolean, "true", L.T("Workflows.OffOnlyNewIDs")),
            new("animated", "Animated", ParameterKind.Boolean, "true"),
        ]) { NeedsClient = true },
        new(WorkflowStepType.ExtractSprites, StepGroup.Source, L.T("Workflows.SpritesByID"), L.T("Workflows.SingleSpritesOfTheClientS"),
        [
            new("ids", L.T("Common.SpriteIDs"), ParameterKind.Text, "", L.T("Workflows.ForExample15009000")),
        ]) { NeedsClient = true },

        new(WorkflowStepType.Trim, StepGroup.Process, L.T("Workflows.TrimTransparentBorders"), L.T("Workflows.RemovesFullyTransparentRowsAndColumns"), []),
        new(WorkflowStepType.Resize, StepGroup.Process, "Resize", L.T("Workflows.EnlargesByAWholeNumberWith"),
        [
            new("mode", "Mode", ParameterKind.Choice, "Scale") { Choices = ["Scale", "Fit"] },
            new("scale", "Scale", ParameterKind.Integer, "2") { Min = 1, Max = 8 },
            new("maxWidth", L.T("Workflows.FitWidth"), ParameterKind.Integer, "256") { Min = 1, Max = 8192 },
            new("maxHeight", L.T("Workflows.FitHeight"), ParameterKind.Integer, "256") { Min = 1, Max = 8192 },
            new("filter", L.T("Workflows.FilterWhenFitting"), ParameterKind.Choice, "Nearest") { Choices = ["Nearest", "Smooth"] },
        ]),
        new(WorkflowStepType.Canvas, StepGroup.Process, L.T("Workflows.ConsistentCanvas"), L.T("Workflows.PlacesEveryImageOnACanvas"),
        [
            new("width", "Width", ParameterKind.Integer, "128") { Min = 1, Max = 8192 },
            new("height", "Height", ParameterKind.Integer, "128") { Min = 1, Max = 8192 },
            new("padding", "Padding", ParameterKind.Integer, "0") { Max = 512 },
            new("align", "Alignment", ParameterKind.Choice, "Bottom") { Choices = ["Center", "Bottom", L.T("Workflows.TopLeft")] },
            new("background", "Background", ParameterKind.Text, "transparent", L.T("Workflows.TransparentOrAColorLikeFFFFFF")),
        ]),
        new(WorkflowStepType.ConvertFormat, StepGroup.Process, L.T("Workflows.FileFormat"), L.T("Workflows.PNGGIFOrKeepStillImages"),
        [
            new("format", "Format", ParameterKind.Choice, "keep") { Choices = ["keep", "png", "gif"] },
            new("animatedAs", L.T("Workflows.AnimationsAs"), ParameterKind.Choice, "GIF") { Choices = ["GIF", L.T("Workflows.FirstFramePNG")] },
        ]),
        new(WorkflowStepType.ApplyMetadata, StepGroup.Process, L.T("Workflows.DescriptionAndTags"), L.T("Workflows.AddsADescriptionAndTagsManifests"),
        [
            new("descriptionTemplate", "Description", ParameterKind.Text, "", L.T("Workflows.ForExampleNameCategoryClientVersion")),
            new("tags", "Tags", ParameterKind.Text, "", L.T("Workflows.CommaSeparated")),
        ]),
        new(WorkflowStepType.Rename, StepGroup.Process, L.T("Workflows.FileNames"), L.T("Workflows.FileNamesFromATemplate"),
        [
            new("template", L.T("Workflows.NameTemplate"), ParameterKind.Text, "{name}", L.T("Workflows.VariablesNameIdCategoryVersionAnimation")),
        ]),
        new(WorkflowStepType.Organize, StepGroup.Process, "Folders", L.T("Workflows.SortsTheFilesIntoFoldersFrom"),
        [
            new("template", L.T("Workflows.FolderTemplate"), ParameterKind.Text, "{category}", L.T("Workflows.ForExampleCategoryGroupOrVersion")),
        ]),

        new(WorkflowStepType.ContactSheet, StepGroup.Output, L.T("Workflows.ContactSheet"), L.T("Workflows.OneOverviewImageWithEveryImage"),
        [
            new("columns", "Columns", ParameterKind.Integer, "0", L.T("Workflows.0Automatic")) { Max = 64 },
            new("labels", L.T("Workflows.NamesUnderTheImages"), ParameterKind.Boolean, "true"),
            new("perCategory", L.T("Workflows.OneSheetPerCategory"), ParameterKind.Boolean, "false"),
            new("background", "Background", ParameterKind.Text, "transparent"),
        ]),
        new(WorkflowStepType.ComparisonSheets, StepGroup.Output, L.T("Workflows.ComparisonSheets"), L.T("Workflows.OneSheetPerOutfitOrCreature"),
        [
            new("labels", "Labels", ParameterKind.Boolean, "true"),
        ]),
        new(WorkflowStepType.Manifest, StepGroup.Output, "Manifests", L.T("Workflows.ListsOfTheImagesWithTheir"),
        [
            new("json", "JSON", ParameterKind.Boolean, "true"),
            new("csv", "CSV", ParameterKind.Boolean, "true"),
            new("markdown", "Markdown", ParameterKind.Boolean, "false"),
        ]),
        new(WorkflowStepType.Gallery, StepGroup.Output, L.T("Workflows.HTMLGallery"), L.T("Workflows.ASelfContainedGalleryPageSearch"),
        [
            new("title", "Title", ParameterKind.Text, "{collection}"),
            new("theme", "Theme", ParameterKind.Choice, "Auto") { Choices = ["Auto", "Light", "Dark"] },
            new("tileSize", L.T("Workflows.TileSize"), ParameterKind.Integer, "150") { Min = 80, Max = 400 },
        ]),
        new(WorkflowStepType.Documentation, StepGroup.Output, L.T("Workflows.TextSnippets"), L.T("Workflows.ATextFileWithOneSnippet"),
        [
            new("template", L.T("Workflows.SnippetTemplate"), ParameterKind.Text, ""),
            new("fileName", L.T("Workflows.FileName"), ParameterKind.Text, ""),
        ]),
        new(WorkflowStepType.Zip, StepGroup.Output, L.T("Workflows.ZIPArchive"), L.T("Workflows.PacksEverythingWrittenIntoOneZIP"),
        [
            new("fileName", L.T("Workflows.FileName"), ParameterKind.Text, "{collection}.zip"),
        ]),
        new(WorkflowStepType.AddToLibrary, StepGroup.Output, L.T("Workflows.AddToTheLibrary"), L.T("Workflows.AddsTheImagesToTheAsset"),
        [
            new("collection", "Collection", ParameterKind.Text, "{collection}"),
            new("tags", "Tags", ParameterKind.Text, ""),
        ]),
    ];

    public static StepDefinition Get(WorkflowStepType type) => All.First(d => d.Type == type);
}
