using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Library;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>One setting of a workflow step, edited in place.</summary>
public sealed partial class StepParameterRow : ObservableObject
{
    private readonly WorkflowStep _step;
    private readonly Action _changed;

    public StepParameterRow(WorkflowStep step, StepParameter definition, Action changed)
    {
        _step = step;
        Definition = definition;
        _changed = changed;
    }

    public StepParameter Definition { get; }
    public string Label => Definition.Label;
    public string Help => Definition.Help;
    public IReadOnlyList<string> Choices => Definition.Choices;
    public bool IsText => Definition.Kind is ParameterKind.Text or ParameterKind.Integer;
    public bool IsMultiline => Definition.Kind == ParameterKind.MultilineText;
    public bool IsBoolean => Definition.Kind == ParameterKind.Boolean;
    public bool IsChoice => Definition.Kind == ParameterKind.Choice;
    public bool IsPath => Definition.Kind is ParameterKind.Folder or ParameterKind.File;
    public bool IsFolder => Definition.Kind == ParameterKind.Folder;

    public string Value
    {
        get => _step.Get(Definition.Key);
        set
        {
            if (Value == value) return;
            _step.Parameters[Definition.Key] = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(Flag));
            _changed();
        }
    }

    public bool Flag
    {
        get => _step.GetBool(Definition.Key);
        set => Value = value ? "true" : "false";
    }
}

/// <summary>A step of the workflow being edited.</summary>
public sealed partial class StepRow : ObservableObject
{
    private readonly Action _changed;

    public StepRow(WorkflowStep step, Action changed)
    {
        Step = step;
        _changed = changed;
        Parameters = step.Definition.Parameters.Select(p => new StepParameterRow(step, p, () => { OnPropertyChanged(nameof(Summary)); changed(); })).ToList();
    }

    public WorkflowStep Step { get; }
    public IReadOnlyList<StepParameterRow> Parameters { get; }
    public string Title => Step.Definition.Title;
    public string Description => Step.Definition.Description;
    public string Summary => Step.Summary;
    public string GroupName => Step.Definition.Group switch { StepGroup.Source => L.T("Common.Source"), StepGroup.Process => L.T("ContentStudio.EachImage"), _ => L.T("Diagnostics.Output2") };
    public string Glyph => Step.Definition.Group switch { StepGroup.Source => "", StepGroup.Process => "", _ => "" };

    public bool Enabled
    {
        get => Step.Enabled;
        set
        {
            if (Step.Enabled == value) return;
            Step.Enabled = value;
            OnPropertyChanged();
            _changed();
        }
    }
}

/// <summary>An entry of the creature, outfit or object picker.</summary>
public sealed record PickerItem(string Name, string Value, string Detail)
{
    public override string ToString() => Name;
}

/// <summary>A queue job as listed.</summary>
public sealed partial class QueueRow(QueueJob job) : ObservableObject
{
    public QueueJob Job { get; } = job;
    public string When => Job.CreatedUtc.ToLocalTime().ToString("HH:mm:ss");
}

/// <summary>An export profile being edited (a copy; saved explicitly).</summary>
public sealed partial class ProfileEditor : ObservableObject
{
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _website = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _imageFormat = "keep";
    [ObservableProperty] private AnimatedOutput _animated;
    [ObservableProperty] private int _scale = 1;
    [ObservableProperty] private int _canvasWidth;
    [ObservableProperty] private int _canvasHeight;
    [ObservableProperty] private int _padding;
    [ObservableProperty] private string _background = "transparent";
    [ObservableProperty] private bool _trimTransparent;
    [ObservableProperty] private string _fileNameTemplate = "{name}";
    [ObservableProperty] private string _folderTemplate = "{category}";
    [ObservableProperty] private NameCase _case;
    [ObservableProperty] private string _spaceReplacement = "";
    [ObservableProperty] private bool _writeJsonManifest;
    [ObservableProperty] private bool _writeCsvManifest;
    [ObservableProperty] private bool _writeMarkdownIndex;
    [ObservableProperty] private bool _writeGallery;
    [ObservableProperty] private string _attributionTemplate = "";
    [ObservableProperty] private string _documentationTemplate = "";
    [ObservableProperty] private string _documentationFileName = "snippets.txt";
    [ObservableProperty] private bool _builtIn;

    public static ProfileEditor From(ExportProfile p) => new()
    {
        Id = p.Id, Name = p.Name, Website = p.Website, Description = p.Description, ImageFormat = p.ImageFormat, Animated = p.Animated, Scale = p.Scale,
        CanvasWidth = p.CanvasWidth, CanvasHeight = p.CanvasHeight, Padding = p.Padding, Background = p.Background, TrimTransparent = p.TrimTransparent,
        FileNameTemplate = p.FileNameTemplate, FolderTemplate = p.FolderTemplate, Case = p.Case, SpaceReplacement = p.SpaceReplacement,
        WriteJsonManifest = p.WriteJsonManifest, WriteCsvManifest = p.WriteCsvManifest, WriteMarkdownIndex = p.WriteMarkdownIndex, WriteGallery = p.WriteGallery,
        AttributionTemplate = p.AttributionTemplate, DocumentationTemplate = p.DocumentationTemplate, DocumentationFileName = p.DocumentationFileName, BuiltIn = p.BuiltIn,
    };

    public ExportProfile ToProfile() => new()
    {
        Id = Id, Name = Name.Trim(), Website = Website.Trim(), Description = Description.Trim(), ImageFormat = ImageFormat, Animated = Animated, Scale = Scale,
        CanvasWidth = CanvasWidth, CanvasHeight = CanvasHeight, Padding = Padding, Background = Background.Trim(), TrimTransparent = TrimTransparent,
        FileNameTemplate = FileNameTemplate, FolderTemplate = FolderTemplate, Case = Case, SpaceReplacement = SpaceReplacement,
        WriteJsonManifest = WriteJsonManifest, WriteCsvManifest = WriteCsvManifest, WriteMarkdownIndex = WriteMarkdownIndex, WriteGallery = WriteGallery,
        AttributionTemplate = AttributionTemplate, DocumentationTemplate = DocumentationTemplate, DocumentationFileName = DocumentationFileName, BuiltIn = BuiltIn,
    };
}

/// <summary>
/// Content Studio (8.8): reusable content projects (creature and outfit galleries, item collections,
/// map references, update resources, wiki preparation, custom exports), the workflow builder that
/// combines steps without programming, the publishing profiles of each website, and the processing
/// queue where projects run in the background with progress, cancellation and retry.
/// </summary>
public sealed partial class ContentStudioViewModel : PageViewModel
{
    public ContentStudioViewModel(AppServices services) : base(services, BuiltInTools.ContentStudio)
    {
        services.Library.QueueChanged += _ => System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshQueue);
    }

    private AssetLibrary? Lib => Services.Library.TryLibrary(out _);

    [ObservableProperty] private int _tab;
    [ObservableProperty] private string? _problem;

    public override async Task OnNavigatedToAsync()
    {
        LoadProfiles();
        LoadProjects(SelectedProject?.Id ?? Services.Settings.Library.LastProjectId);
        RefreshQueue();
        await Task.CompletedTask;
    }

    // ------------------------------------------------------------------ projects

    public ObservableCollection<ContentProject> Projects { get; } = [];
    public ObservableCollection<StepRow> Steps { get; } = [];
    public ObservableCollection<ProjectRun> History { get; } = [];

    [ObservableProperty] private ContentProject? _selectedProject;
    [ObservableProperty] private StepRow? _selectedStep;
    [ObservableProperty] private bool _isDirty;

    public bool HasProject => SelectedProject != null;
    public bool HasProjects => Projects.Count > 0;
    public IReadOnlyList<ExportProfile> ProfileChoices { get; private set; } = ExportProfiles.BuiltIns;
    public static IReadOnlyList<OutputMode> OutputModes { get; } = Enum.GetValues<OutputMode>();

    public string ProjectName
    {
        get => SelectedProject?.Name ?? "";
        set { if (SelectedProject != null && value != SelectedProject.Name) { SelectedProject.Name = value; Changed(); OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveOutput)); } }
    }

    public string ProjectDescription
    {
        get => SelectedProject?.Description ?? "";
        set { if (SelectedProject != null && value != SelectedProject.Description) { SelectedProject.Description = value; Changed(); OnPropertyChanged(); } }
    }

    public ExportProfile? ProjectProfile
    {
        get => SelectedProject == null ? null : ProfileChoices.FirstOrDefault(p => p.Id == SelectedProject.ProfileId) ?? (ProfileChoices.Count > 0 ? ProfileChoices[0] : null);
        set { if (SelectedProject != null && value != null && value.Id != SelectedProject.ProfileId) { SelectedProject.ProfileId = value.Id; Changed(); OnPropertyChanged(); } }
    }

    public string ProjectOutput
    {
        get => SelectedProject == null ? "" : string.IsNullOrWhiteSpace(SelectedProject.OutputFolder) ? "" : SelectedProject.OutputFolder;
        set { if (SelectedProject != null && value != SelectedProject.OutputFolder) { SelectedProject.OutputFolder = value?.Trim() ?? ""; Changed(); OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveOutput)); } }
    }

    public string EffectiveOutput => SelectedProject == null ? "" : Path.Combine(string.IsNullOrWhiteSpace(SelectedProject.OutputFolder) ? Services.Library.ContentOutputFolder : SelectedProject.OutputFolder,
        FileNames.Sanitize(SelectedProject.Name));

    public OutputMode ProjectOutputMode
    {
        get => SelectedProject?.OutputMode ?? OutputMode.NewFolderPerRun;
        set { if (SelectedProject != null && value != SelectedProject.OutputMode) { SelectedProject.OutputMode = value; Changed(); OnPropertyChanged(); } }
    }

    public string ProjectType => SelectedProject == null ? "" : ContentProject.TypeName(SelectedProject.Type);
    public string ValidationText => SelectedProject == null ? "" : string.Join(" ", SelectedProject.Workflow.Validate());

    private void Changed()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(ValidationText));
    }

    partial void OnSelectedProjectChanged(ContentProject? oldValue, ContentProject? newValue)
    {
        if (oldValue != null && IsDirty && Lib is { } lib) Try(L.T("ContentStudio.SavingTheProject"), () => ContentProjects.Save(lib.ProjectsFolder, oldValue));
        IsDirty = false;
        Steps.Clear();
        History.Clear();
        if (newValue != null)
        {
            foreach (var s in newValue.Workflow.Steps) Steps.Add(new StepRow(s, Changed));
            foreach (var r in Enumerable.Reverse(newValue.History)) History.Add(r);
            Services.Settings.Library.LastProjectId = newValue.Id;
        }
        SelectedStep = Steps.FirstOrDefault();
        foreach (string p in new[] { nameof(HasProject), nameof(ProjectName), nameof(ProjectDescription), nameof(ProjectProfile), nameof(ProjectOutput), nameof(EffectiveOutput),
                     nameof(ProjectOutputMode), nameof(ProjectType), nameof(ValidationText) })
            OnPropertyChanged(p);
    }

    partial void OnSelectedStepChanged(StepRow? value) => _ = LoadPickerAsync();

    private void LoadProjects(string? select = null)
    {
        var lib = Services.Library.TryLibrary(out string? problem);
        Problem = problem == null ? null : L.T("ContentStudio.TheAssetLibraryCannotBeOpened") + problem;
        if (lib == null) return;
        string? keep = select ?? SelectedProject?.Id;
        SelectedProject = null;
        Projects.Clear();
        foreach (var p in ContentProjects.Load(lib.ProjectsFolder)) Projects.Add(p);
        OnPropertyChanged(nameof(HasProjects));
        SelectedProject = Projects.FirstOrDefault(p => p.Id == keep) ?? Projects.FirstOrDefault();
    }

    [RelayCommand]
    private void NewProject()
    {
        if (Lib is not { } lib) return;
        var types = Enum.GetValues<ContentProjectType>();
        var answer = Services.Dialogs.ChooseFrom(L.T("ContentStudio.NewContentProject"), L.T("ContentStudio.WhatDoYouWantToMake"),
            types.Select(t => new ChoiceItem(ContentProject.TypeName(t), ContentProject.TypeDescription(t))).ToList(), new ChoiceOptions { OkText = L.T("ContentStudio.Create"), Selected = 0 });
        if (answer is not { Index: >= 0 }) return;
        var project = ContentProjects.Create(types[answer.Index]);
        project.Name = UniqueName(project.Name);
        Add(lib, project);
    }

    private string UniqueName(string name)
    {
        string unique = name;
        for (int i = 2; Projects.Any(p => string.Equals(p.Name, unique, StringComparison.OrdinalIgnoreCase)); i++) unique = $"{name} {i}";
        return unique;
    }

    private void Add(AssetLibrary lib, ContentProject project)
    {
        if (!Try(L.T("ContentStudio.SavingTheProject"), () => ContentProjects.Save(lib.ProjectsFolder, project))) return;
        Projects.Insert(0, project);
        OnPropertyChanged(nameof(HasProjects));
        SelectedProject = project;
        Tab = 0;
    }

    /// <summary>A Custom export project for library assets (from the Asset Library page).</summary>
    public void CreateFromLibrary(string? collection, IReadOnlyList<long> assetIds)
    {
        if (Lib is not { } lib) return;
        LoadProfiles();
        LoadProjects();
        var project = ContentProjects.Create(ContentProjectType.CustomExport, UniqueName(collection != null ? L.F("Library.0Export", collection) : L.T("ContentStudio.LibrarySelectionExport")));
        var source = project.Workflow.Steps[0];
        source.Parameters["collection"] = collection ?? "";
        source.Parameters["assetIds"] = string.Join(", ", assetIds);
        Add(lib, project);
        Info(L.F("ContentStudio.Project0CreatedWith1Add", project.Name, (collection != null ? L.F("ContentStudio.TheCollection0", collection) : L.Count(assetIds.Count, "Noun.selected_asset"))));
    }

    /// <summary>A Creature gallery project for creatures chosen on the Static Data page.</summary>
    public void CreateCreatureGallery(IReadOnlyList<string> names)
    {
        if (Lib is not { } lib || names.Count == 0) return;
        LoadProfiles();
        LoadProjects();
        var project = ContentProjects.Create(ContentProjectType.CreatureGallery, UniqueName(names.Count == 1 ? L.F("ContentStudio.0Gallery", names[0]) : L.T("StaticData.CreatureGallery")));
        project.Workflow.Steps[0].Parameters["names"] = string.Join("\n", names);
        Add(lib, project);
        Info(L.F("ContentStudio.CreatureGalleryWith0CreatedCheck", L.Count(names.Count, "Noun.creature")));
    }

    /// <summary>A Fansite update project comparing the client with an older appearances file (from Compare Files).</summary>
    public void CreateUpdateProject(string previousAppearances)
    {
        if (Lib is not { } lib) return;
        LoadProfiles();
        LoadProjects();
        var project = ContentProjects.Create(ContentProjectType.FansiteUpdate, UniqueName(L.F("ContentStudio.UpdateResources0", Services.Data.ClientVersion ?? "").Trim()));
        project.Workflow.Steps[0].Parameters["previous"] = previousAppearances;
        Add(lib, project);
        Info(L.F("ContentStudio.Project0ComparesTheClientIn", project.Name, Path.GetFileName(previousAppearances)));
    }

    [RelayCommand]
    private void SaveProject()
    {
        if (SelectedProject is not { } p || Lib is not { } lib) return;
        if (Try(L.T("ContentStudio.SavingTheProject"), () => ContentProjects.Save(lib.ProjectsFolder, p)))
        {
            IsDirty = false;
            Info(L.F("ContentStudio.Project0Saved", p.Name));
        }
    }

    [RelayCommand]
    private void DuplicateProject()
    {
        if (SelectedProject is not { } p || Lib is not { } lib) return;
        Add(lib, p.Clone(UniqueName(L.F("ContentStudio.0Copy", p.Name))));
    }

    [RelayCommand]
    private void DeleteProject()
    {
        if (SelectedProject is not { } p || Lib is not { } lib) return;
        if (!Services.Dialogs.Confirm(L.F("ContentStudio.DeleteTheProject0TheProject", p.Name), L.T("ContentStudio.DeleteProject2"), L.T("ContentStudio.Delete"), L.T("Common.Cancel"))) return;
        if (!Try(L.T("ContentStudio.DeletingTheProject"), () => ContentProjects.Delete(lib.ProjectsFolder, p.Id))) return;
        IsDirty = false;
        Projects.Remove(p);
        OnPropertyChanged(nameof(HasProjects));
        SelectedProject = Projects.FirstOrDefault();
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        string? folder = Services.Dialogs.PickFolder(L.T("ContentStudio.FolderForThisProjectSRuns"), ProjectOutput.Length > 0 ? ProjectOutput : Services.Library.ContentOutputFolder);
        if (folder != null) ProjectOutput = folder;
    }

    [RelayCommand]
    private void OpenOutput()
    {
        string folder = SelectedProject?.History.LastOrDefault()?.OutputFolder is { } last && Directory.Exists(last) ? last : EffectiveOutput;
        if (Directory.Exists(folder)) Services.Shell.OpenFolder(folder);
        else Services.Status.Show(L.F("ContentStudio.0DoesNotExistYetIt", folder), StatusKind.Info);
    }

    [RelayCommand]
    private void OpenRun(ProjectRun? run)
    {
        if (run?.OutputFolder is { } f && Directory.Exists(f)) Services.Shell.OpenFolder(f);
        else Services.Status.Show(L.T("ContentStudio.ThatRunSFolderNoLonger"), StatusKind.Warning);
    }

    // ------------------------------------------------------------------ the workflow builder

    [RelayCommand]
    private void AddStep()
    {
        if (SelectedProject is not { } p) return;
        var defs = WorkflowSteps.All;
        var answer = Services.Dialogs.ChooseFrom(L.T("ContentStudio.AddAStep"), L.T("ContentStudio.ChooseAnOperationSourcesChooseThe"),
            defs.Select(d => new ChoiceItem(d.Title, d.Description, d.Group switch { StepGroup.Source => L.T("ItemCatalog.Sources"), StepGroup.Process => L.T("ContentStudio.EachImage"), _ => "Outputs" })).ToList(),
            new ChoiceOptions { OkText = L.T("Common.Add") });
        if (answer is not { Index: >= 0 }) return;
        var step = new WorkflowStep(defs[answer.Index].Type);
        // Sources first, then processing, then outputs: insert at the end of the step's group.
        int index = p.Workflow.Steps.FindLastIndex(s => s.Definition.Group <= step.Definition.Group) + 1;
        p.Workflow.Steps.Insert(index, step);
        var row = new StepRow(step, Changed);
        Steps.Insert(index, row);
        SelectedStep = row;
        Changed();
    }

    [RelayCommand]
    private void RemoveStep(StepRow? row)
    {
        row ??= SelectedStep;
        if (row == null || SelectedProject is not { } p) return;
        int i = Steps.IndexOf(row);
        p.Workflow.Steps.Remove(row.Step);
        Steps.Remove(row);
        SelectedStep = Steps.Count == 0 ? null : Steps[Math.Clamp(i, 0, Steps.Count - 1)];
        Changed();
    }

    [RelayCommand]
    private void MoveStepUp(StepRow? row) => Move(row ?? SelectedStep, -1);

    [RelayCommand]
    private void MoveStepDown(StepRow? row) => Move(row ?? SelectedStep, +1);

    private void Move(StepRow? row, int delta)
    {
        if (row == null || SelectedProject is not { } p) return;
        int i = Steps.IndexOf(row), j = i + delta;
        if (i < 0 || j < 0 || j >= Steps.Count) return;
        Steps.Move(i, j);
        p.Workflow.Steps.RemoveAt(i);
        p.Workflow.Steps.Insert(j, row.Step);
        SelectedStep = row;
        Changed();
    }

    [RelayCommand]
    private void BrowseParameter(StepParameterRow? row)
    {
        if (row == null) return;
        string? path = row.IsFolder ? Services.Dialogs.PickFolder(row.Label, row.Value)
            : Services.Dialogs.OpenFile(row.Label, L.T("ContentStudio.TibiaAppearancesAppearancesDatAppearancesDat"), Services.Data.AssetsDirectory);
        if (path != null) row.Value = path;
    }

    [RelayCommand]
    private void ExportWorkflow()
    {
        if (SelectedProject is not { } p) return;
        string? file = Services.Dialogs.SaveFile(L.T("ContentStudio.ExportTheWorkflow"), L.T("ContentStudio.TibiaFoundryWorkflowTfworkflowJsonTfworkflow"), FileNames.Sanitize(p.Name) + ".tfworkflow.json");
        if (file == null) return;
        p.Workflow.Name = p.Name;
        p.Workflow.Description = p.Description;
        if (Try(L.T("ContentStudio.ExportingTheWorkflow"), () => AtomicFile.WriteAllText(file, p.Workflow.ToJson()))) Saved(file, L.T("ContentStudio.ShareItImportWorkflowReadsIt"));
    }

    [RelayCommand]
    private void ImportWorkflow()
    {
        if (Lib is not { } lib) return;
        string? file = Services.Dialogs.OpenFile(L.T("ContentStudio.ImportAWorkflow"), L.T("ContentStudio.TibiaFoundryWorkflowTfworkflowJsonJson"));
        if (file == null) return;
        WorkflowDefinition? wf = null;
        if (!Try(L.T("ContentStudio.ImportingTheWorkflow"), () =>
            {
                if (new FileInfo(file).Length > 4 << 20) throw new InvalidDataException("The file is too large for a workflow.");
                wf = WorkflowDefinition.FromJson(File.ReadAllText(file));
            })) return;
        var project = ContentProjects.Create(ContentProjectType.CustomExport, UniqueName(string.IsNullOrWhiteSpace(wf!.Name) ? Path.GetFileNameWithoutExtension(file) : wf.Name));
        project.Description = wf.Description;
        project.Workflow = wf;
        Add(lib, project);
        Info(L.F("ContentStudio.WorkflowImportedAsTheProject0", project.Name, L.Count(wf.Steps.Count, "Noun.step")));
    }

    [RelayCommand]
    private void SaveAsTemplate()
    {
        if (SelectedProject is not { } p || Lib is not { } lib) return;
        string? name = Services.Dialogs.Prompt(L.T("ContentStudio.SaveAsATemplate"), L.T("ContentStudio.NameOfTheWorkflowTemplateLoad"), p.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        var wf = p.Workflow.Clone();
        wf.Name = name.Trim();
        if (Try(L.T("ContentStudio.SavingTheTemplate"), () => ContentProjects.SaveWorkflow(lib.WorkflowsFolder, wf))) Info(L.F("ContentStudio.Template0Saved", wf.Name));
    }

    [RelayCommand]
    private void LoadTemplate()
    {
        if (SelectedProject is not { } p || Lib is not { } lib) return;
        var templates = ContentProjects.LoadWorkflows(lib.WorkflowsFolder);
        if (templates.Count == 0)
        {
            Services.Dialogs.Show(L.T("ContentStudio.NoTemplatesYetSaveAsTemplate"), MessageKind.Info, L.T("ContentStudio.LoadTemplate2"));
            return;
        }
        var answer = Services.Dialogs.ChooseFrom(L.T("ContentStudio.LoadTemplate2"), L.F("ContentStudio.ReplaceTheStepsOf0With", p.Name),
            templates.Select(t => new ChoiceItem(t.Workflow.Name, $"{L.Count(t.Workflow.Steps.Count, "Noun.step")}: {string.Join(" → ", t.Workflow.Steps.Select(s => s.Definition.Title))}")).ToList(),
            new ChoiceOptions { OkText = L.T("ContentStudio.ReplaceSteps") });
        if (answer is not { Index: >= 0 }) return;
        p.Workflow = templates[answer.Index].Workflow.Clone();
        Try(L.T("ContentStudio.SavingTheProject"), () => ContentProjects.Save(lib.ProjectsFolder, p));
        var keep = p;
        SelectedProject = null;
        SelectedProject = keep;
    }

    // ------------------------------------------------------------------ picker (creatures, outfits, objects)

    public ObservableCollection<PickerItem> PickerItems { get; } = [];
    [ObservableProperty] private string _pickerFilter = "";
    [ObservableProperty] private string _pickerTitle = "";
    [ObservableProperty] private bool _hasPicker;
    private List<PickerItem> _pickerAll = [];
    private string _pickerKey = "";

    /// <summary>The picked entries (set by the view).</summary>
    public IReadOnlyList<PickerItem> PickedItems { get; set; } = [];

    partial void OnPickerFilterChanged(string value) => FilterPicker();

    private void FilterPicker()
    {
        PickerItems.Clear();
        var words = PickerFilter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var i in _pickerAll.Where(i => words.All(w => i.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || i.Value.Contains(w, StringComparison.Ordinal))).Take(2000))
            PickerItems.Add(i);
    }

    private async Task LoadPickerAsync()
    {
        var type = SelectedStep?.Step.Type;
        HasPicker = type is WorkflowStepType.RenderCreatures or WorkflowStepType.RenderOutfits or WorkflowStepType.RenderObjects;
        _pickerAll = [];
        PickerItems.Clear();
        if (!HasPicker) return;
        try
        {
            if (type == WorkflowStepType.RenderCreatures)
            {
                PickerTitle = L.T("ContentStudio.CreaturesOfTheClientInUse");
                _pickerKey = "names";
                var game = await Services.Library.GameAssetsAsync(needStaticData: true);
                if (game.StaticData is { } sd)
                    _pickerAll = sd.Monsters.Select(c => new PickerItem(c.Name, c.Name, "creature")).Concat(sd.Bosses.Select(b => new PickerItem(b.Name, b.Name, "boss")))
                        .DistinctBy(i => i.Name, StringComparer.OrdinalIgnoreCase).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            else
            {
                var game = await Services.Library.GameAssetsAsync(needStaticData: false);
                bool outfits = type == WorkflowStepType.RenderOutfits;
                PickerTitle = outfits ? L.T("ContentStudio.OutfitsOfTheClientInUse") : L.T("ContentStudio.ObjectsOfTheClientInUse");
                _pickerKey = "ids";
                if (game.Appearances is { } db)
                    _pickerAll = outfits
                        ? db.List(AppearanceKind.Outfit).Select(a => new PickerItem(string.IsNullOrWhiteSpace(a.Name) ? L.F("ContentStudio.Outfit0", a.Id) : $"{Core.Wiki.NameFormatter.Capitalize(a.Name)} ({a.Id})", a.Id.ToString(), "outfit")).ToList()
                        : db.List(AppearanceKind.Object).Where(a => !string.IsNullOrWhiteSpace(a.Name))
                            .Select(a => new PickerItem($"{Core.Wiki.NameFormatter.Capitalize(a.Name)} ({a.Id})", a.Id.ToString(), "object")).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            if (_pickerAll.Count == 0) PickerTitle += L.T("ContentStudio.NoneAvailableSetAClientOn");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Core.Compatibility.DataFileException or InvalidOperationException)
        {
            PickerTitle = L.T("ContentStudio.TheClientSDataCouldNot") + ex.Message;
        }
        FilterPicker();
    }

    [RelayCommand]
    private void AddPicked()
    {
        if (SelectedStep is not { } step || PickedItems.Count == 0) return;
        var row = step.Parameters.FirstOrDefault(p => p.Definition.Key == _pickerKey);
        if (row == null) return;
        var existing = row.Value.Split(_pickerKey == "names" ? ['\n', '\r'] : [',', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        foreach (var i in PickedItems) if (!existing.Contains(i.Value, StringComparer.OrdinalIgnoreCase)) existing.Add(i.Value);
        row.Value = string.Join(_pickerKey == "names" ? "\n" : ", ", existing);
        Info(L.F("ContentStudio.0AddedTo1", L.Count(PickedItems.Count, "Noun.entry"), row.Label));
    }

    // ------------------------------------------------------------------ running

    [RelayCommand]
    private async Task RunProjectAsync()
    {
        if (SelectedProject is not { } project || Lib is not { } lib) return;
        var problems = project.Workflow.Validate();
        if (problems.Count > 0)
        {
            Services.Dialogs.Show(string.Join("\n", problems), MessageKind.Warning, L.T("ContentStudio.Run") + project.Name);
            return;
        }
        if (IsDirty) Try(L.T("ContentStudio.SavingTheProject"), () => ContentProjects.Save(lib.ProjectsFolder, project));
        IsDirty = false;
        bool needsClient = project.Workflow.Steps.Any(s => s.Enabled && s.Definition.NeedsClient);
        bool needsStatic = project.Workflow.Steps.Any(s => s.Enabled && s.Definition.NeedsStaticData);
        IGameAssets game;
        try
        {
            // The client's data is loaded here (it belongs to the program's data service); the run itself happens in the queue.
            game = needsClient || needsStatic ? await Services.Library.GameAssetsAsync(needsStatic) : new NoGame();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Core.Compatibility.DataFileException or InvalidOperationException)
        {
            Services.Dialogs.Show(L.T("ContentStudio.TheClientSDataCouldNot") + ex.Message, MessageKind.Warning, L.T("ContentStudio.Run") + project.Name);
            return;
        }
        // The run works on a snapshot, so editing the project meanwhile does not change a running job.
        var snapshot = project.Clone(project.Name);
        snapshot.Id = project.Id;
        WorkflowContext context;
        try
        {
            context = Services.Library.ContextFor(snapshot, game);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Services.Dialogs.Show(L.T("ContentStudio.TheProjectCannotRun") + ex.Message, MessageKind.Warning, L.T("ContentStudio.Run") + project.Name);
            return;
        }
        string id = project.Id, name = project.Name;
        Services.Library.Queue.Enqueue(name, ContentProject.TypeName(project.Type), (p, ct) => Task.Run(() =>
        {
            var started = DateTime.UtcNow;
            try
            {
                var result = WorkflowRunner.Run(snapshot.Workflow, context, p, ct);
                Record(id, new ProjectRun(started, result.Duration.TotalSeconds, result.Problems.Count > 0 ? L.T("ContentStudio.CompletedWithNotes") : "Completed", result.Files, result.OutputFolder, result.Problems.Take(50).ToList()));
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Services.Status.Show($"{name}: {result.Summary} → {result.OutputFolder}", result.Problems.Count > 0 ? StatusKind.Warning : StatusKind.Success));
                Services.Library.RaiseLibraryChanged();
                return result.Summary + (result.Problems.Count > 0 ? " " + string.Join(" ", result.Problems.Take(3)) : "");
            }
            catch (OperationCanceledException)
            {
                Record(id, new ProjectRun(started, (DateTime.UtcNow - started).TotalSeconds, "Cancelled", 0, null, []));
                throw;
            }
            catch (Exception ex)
            {
                Record(id, new ProjectRun(started, (DateTime.UtcNow - started).TotalSeconds, "Failed", 0, null, [ex.Message]));
                AppLog.Warn($"Content project {name} failed: {ex.Message}");
                throw;
            }
        }, ct));
        Tab = 2;
        Info(L.F("ContentStudio.0IsInTheQueueYou", name));
    }

    private sealed class NoGame : IGameAssets
    {
        public AppearanceDatabase? Appearances => null;
        public Core.Sprites.ISpriteSource? Sprites => null;
        public Core.StaticData.StaticDataDatabase? StaticData => null;
        public string? ClientVersion => null;
    }

    /// <summary>Adds a run to a project's history (from the queue's thread), saving the project file.</summary>
    private void Record(string projectId, ProjectRun run)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        void Apply()
        {
            if (Lib is not { } lib) return;
            var p = Projects.FirstOrDefault(x => x.Id == projectId) ?? ContentProjects.Load(lib.ProjectsFolder).FirstOrDefault(x => x.Id == projectId);
            if (p == null) return;
            p.History.Add(run);
            Try(L.T("ContentStudio.SavingTheProjectHistory"), () => ContentProjects.Save(lib.ProjectsFolder, p));
            if (SelectedProject == p) History.Insert(0, run);
        }
        if (dispatcher == null || dispatcher.CheckAccess()) Apply();
        else dispatcher.Invoke(Apply);
    }

    // ------------------------------------------------------------------ the queue

    public ObservableCollection<QueueRow> QueueRows { get; } = [];
    [ObservableProperty] private QueueRow? _selectedJob;
    [ObservableProperty] private string _queueSummary = "";

    /// <summary>Finished jobs from the history file, newest first (times in local time).</summary>
    public IReadOnlyList<QueueHistoryEntry> QueueHistory => Services.Library.Queue.History(100)
        .Select(h => h with { FinishedUtc = h.FinishedUtc?.ToLocalTime() }).ToList();

    private void RefreshQueue()
    {
        var jobs = Services.Library.Queue.Jobs;
        var keep = SelectedJob?.Job.Id;
        foreach (var gone in QueueRows.Where(r => !jobs.Contains(r.Job)).ToList()) QueueRows.Remove(gone);
        foreach (var j in jobs.Where(j => QueueRows.All(r => r.Job != j))) QueueRows.Insert(0, new QueueRow(j));
        SelectedJob = QueueRows.FirstOrDefault(r => r.Job.Id == keep) ?? SelectedJob;
        var q = Services.Library.Queue;
        QueueSummary = L.F("ContentStudio.0N0Running1N0Waiting", q.Running, q.Pending, jobs.Count(j => j.IsFinished));
        OnPropertyChanged(nameof(QueueHistory));
    }

    [RelayCommand]
    private void CancelJob(QueueRow? row)
    {
        if ((row ?? SelectedJob) is { } r) Services.Library.Queue.Cancel(r.Job);
    }

    [RelayCommand]
    private void RetryJob(QueueRow? row)
    {
        if ((row ?? SelectedJob) is { } r && Services.Library.Queue.Retry(r.Job) == null)
            Services.Status.Show(L.T("ContentStudio.OnlyFailedOrCancelledJobsCan"), StatusKind.Info);
    }

    [RelayCommand]
    private void JobDetails(QueueRow? row)
    {
        if ((row ?? SelectedJob)?.Job is not { } j) return;
        var sections = new List<ReportSection>
        {
            new(L.T("ContentStudio.Job"))
            {
                Rows =
                [
                    new ReportRow(L.T("Common.Kind"), j.Category), new ReportRow(L.T("ContentStudio.Added2"), j.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                    new ReportRow(L.T("ContentStudio.Started"), j.StartedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? L.T("ContentStudio.NotYet")),
                    new ReportRow(L.T("ContentStudio.Duration"), j.Duration is { } d ? $"{d.TotalSeconds:N1} s" : "—"),
                ],
            },
        };
        if (j.Result != null) sections.Add(new ReportSection(L.T("Common.Result")) { Text = j.Result });
        if (j.Error != null)
        {
            sections.Add(new ReportSection(L.T("ContentStudio.Error")) { Text = j.Error, Tone = ReportTone.Danger });
            sections.Add(new ReportSection(L.T("ContentStudio.TechnicalDetails")) { Text = j.ErrorDetails, Collapsed = true });
        }
        Services.Dialogs.ShowReport(new DialogReport
        {
            Title = L.T("ContentStudio.JobDetails"), Heading = j.Title, Summary = $"{j.State} · {j.Message}",
            Kind = j.State == QueueJobState.Failed ? MessageKind.Error : MessageKind.Info, Sections = sections,
        });
    }

    [RelayCommand]
    private void ClearFinished()
    {
        int n = Services.Library.Queue.ClearFinished();
        RefreshQueue();
        Info(L.F("ContentStudio.0RemovedFromTheListThe", L.Count(n, "Noun.finished_job")));
    }

    public int QueueConcurrency
    {
        get => Services.Settings.Library.QueueConcurrency;
        set
        {
            Services.Settings.Library.QueueConcurrency = Math.Clamp(value, 1, 4);
            Services.SaveSettings();
            OnPropertyChanged();
            RefreshQueue();
        }
    }

    public static IReadOnlyList<int> ConcurrencyChoices { get; } = [1, 2, 3, 4];

    /// <summary>Running or waiting jobs (closing the program cancels them) and unsaved project changes.</summary>
    public override string? UnsavedChanges
    {
        get
        {
            var q = Services.Library.Queue;
            int active = q.Running + q.Pending;
            if (active > 0) return L.F("ContentStudio.ContentStudio0InTheQueue", L.Count(active, "Noun.job"));
            return IsDirty && SelectedProject != null ? L.F("ContentStudio.ContentStudioUnsavedChangesToThe", SelectedProject.Name) : null;
        }
    }

    // ------------------------------------------------------------------ profiles

    public ObservableCollection<ExportProfile> Profiles { get; } = [];
    [ObservableProperty] private ExportProfile? _selectedProfile;
    [ObservableProperty] private ProfileEditor? _profile;
    [ObservableProperty] private string _profilePreview = "";

    public static IReadOnlyList<string> ImageFormats { get; } = ["keep", "png", "gif"];
    public static IReadOnlyList<AnimatedOutput> AnimatedChoices { get; } = Enum.GetValues<AnimatedOutput>();
    public static IReadOnlyList<NameCase> CaseChoices { get; } = Enum.GetValues<NameCase>();
    public static IReadOnlyList<string> SpaceChoices { get; } = ["", "_", "-"];
    public static string VariableHelp { get; } = string.Join("   ", TemplateText.Variables.Select(v => $"{{{v.Name}}} {v.Meaning}"));

    private void LoadProfiles(string? select = null)
    {
        if (Lib is not { } lib) return;
        string? keep = select ?? SelectedProfile?.Id;
        Profiles.Clear();
        foreach (var p in ExportProfiles.Load(lib.ProfilesFolder)) Profiles.Add(p);
        ProfileChoices = Profiles.ToList();
        OnPropertyChanged(nameof(ProfileChoices));
        OnPropertyChanged(nameof(ProjectProfile));
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == keep) ?? Profiles.FirstOrDefault();
    }

    partial void OnSelectedProfileChanged(ExportProfile? value)
    {
        if (Profile != null) Profile.PropertyChanged -= OnProfileEdited;
        Profile = value == null ? null : ProfileEditor.From(value);
        if (Profile != null) Profile.PropertyChanged += OnProfileEdited;
        UpdatePreview();
    }

    private void OnProfileEdited(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (Profile == null) { ProfilePreview = ""; return; }
        var p = ExportProfiles.Normalize(Profile.ToProfile());
        var problems = ExportProfiles.Validate(p);
        string folder = TemplateText.Sample(p.FolderTemplate, p, folder: true);
        ProfilePreview = problems.Count > 0 ? string.Join(" ", problems)
            : L.F("ContentStudio.Example0123Px", (folder.Length > 0 ? folder.Replace('\\', '/') + "/" : ""), TemplateText.Sample(p.FileNameTemplate, p), 64 * p.Scale, 64 * p.Scale) +
              (p.CanvasWidth > 0 ? L.F("ContentStudio.OnA01Canvas", p.CanvasWidth, p.CanvasHeight) : "");
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (Profile == null || Lib is not { } lib) return;
        var p = ExportProfiles.Normalize(Profile.ToProfile());
        if (Try(L.T("ContentStudio.SavingTheProfile"), () => ExportProfiles.Save(lib.ProfilesFolder, p)))
        {
            LoadProfiles(p.Id);
            Info(L.F("ContentStudio.Profile0Saved", p.Name));
        }
    }

    [RelayCommand]
    private void NewProfile()
    {
        if (Lib is not { } lib) return;
        string? name = Services.Dialogs.Prompt(L.T("ContentStudio.NewProfile2"), L.T("ContentStudio.NameOfTheWebsiteOrUse"), L.T("ContentStudio.MyFansite"));
        if (string.IsNullOrWhiteSpace(name)) return;
        var basis = SelectedProfile ?? ExportProfiles.CustomFansite;
        var p = basis with { Id = ExportProfiles.NewId(lib.ProfilesFolder, name), Name = name.Trim(), Website = name.Trim(), BuiltIn = false };
        if (Try(L.T("ContentStudio.CreatingTheProfile"), () => ExportProfiles.Save(lib.ProfilesFolder, p))) LoadProfiles(p.Id);
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is not { } p || Lib is not { } lib) return;
        string what = p.BuiltIn ? L.F("ContentStudio.ResetTheBuiltInProfile0", p.Name) : L.F("ContentStudio.DeleteTheProfile0ProjectsThat", p.Name);
        if (!Services.Dialogs.Confirm(what, p.BuiltIn ? L.T("ContentStudio.ResetProfile") : L.T("ContentStudio.DeleteProfile2"), p.BuiltIn ? L.T("ContentStudio.Reset") : L.T("ContentStudio.Delete"), L.T("Common.Cancel"))) return;
        if (Try(L.T("ContentStudio.DeletingTheProfile"), () => ExportProfiles.Delete(lib.ProfilesFolder, p.Id))) LoadProfiles(p.BuiltIn ? p.Id : null);
    }
}
