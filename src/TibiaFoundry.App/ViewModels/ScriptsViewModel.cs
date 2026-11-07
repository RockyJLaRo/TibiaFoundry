using TibiaFoundry.Core.Common;
using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Scripts;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>A script in the list.</summary>
public sealed record ScriptItem(Script Script)
{
    public string Name => Script.Name;
    public string Category => Core.Scripts.Script.CategoryName(Script.Category);
    public string Description => Script.Description;
    public bool ChangesFiles => Script.ChangesFiles;
}

/// <summary>One input of the selected script.</summary>
public sealed partial class ScriptParameterItem : ObservableObject
{
    private readonly ScriptsViewModel _owner;

    public ScriptParameterItem(ScriptsViewModel owner, ScriptParameter parameter, string? value)
    {
        _owner = owner;
        Parameter = parameter;
        _value = value ?? parameter.Default ?? "";
        Choices = parameter.ChoicesProvider?.Invoke() ?? parameter.Choices;
    }

    public ScriptParameter Parameter { get; }
    public string Key => Parameter.Key;
    public string Label => Parameter.Label;
    public string Help => Parameter.Help;
    public bool IsAdvanced => Parameter.Advanced;
    public ScriptParameterKind Kind => Parameter.Kind;
    public bool IsBoolean => Kind == ScriptParameterKind.Boolean;
    public bool IsChoice => Kind == ScriptParameterKind.Choice;
    public bool IsText => Kind is ScriptParameterKind.Text or ScriptParameterKind.Integer;
    public bool IsPath => Kind is ScriptParameterKind.Folder or ScriptParameterKind.File or ScriptParameterKind.Path or ScriptParameterKind.OutputFile or ScriptParameterKind.OutputFolder;
    public bool CanPickFile => Kind is ScriptParameterKind.File or ScriptParameterKind.Path or ScriptParameterKind.OutputFile;
    public bool CanPickFolder => Kind is ScriptParameterKind.Folder or ScriptParameterKind.Path or ScriptParameterKind.OutputFolder;

    [ObservableProperty] private string _value;
    [ObservableProperty] private IReadOnlyList<ScriptChoice> _choices;
    [ObservableProperty] private bool _isVisible = true;

    public bool BoolValue
    {
        get => bool.TryParse(Value, out bool b) && b;
        set => Value = value ? "true" : "false";
    }

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(BoolValue));
        _owner.ParametersChanged();
    }

    public void RefreshChoices()
    {
        if (Parameter.ChoicesProvider != null) Choices = Parameter.ChoicesProvider();
    }

    [RelayCommand]
    private void PickFile() => _owner.PickFile(this);

    [RelayCommand]
    private void PickFolder() => _owner.PickFolder(this);
}

/// <summary>A proposed change in a preview (with a checkbox).</summary>
public sealed partial class PlanItem(PlannedChange change) : ObservableObject
{
    public PlannedChange Change { get; } = change;
    public string Action => Change.Action;
    public string Source => Change.Source;
    public string? Target => Change.Target;
    public string Detail => Change.Detail;
    public bool IsEnabled => !Change.Blocked;

    public bool Selected
    {
        get => Change.Selected;
        set
        {
            if (Change.Blocked) return;
            Change.Selected = value;
            OnPropertyChanged();
        }
    }
}

/// <summary>
/// Utilities → Scripts: the built-in file utilities. A script never runs by itself: the user fills in
/// its inputs and starts it; scripts that change files show the proposed changes first and apply
/// only what the user confirms.
/// </summary>
public sealed partial class ScriptsViewModel : PageViewModel
{
    private const string StatePrefix = "Scripts.";
    private ScriptArguments? _previewArgs;
    private ScriptResult? _lastResult;

    public ScriptsViewModel(AppServices services) : base(services, BuiltInTools.Scripts)
    {
        All = ScriptRegistry.CreateAll(() => Services.Backups, Services.RenameRecordFolder).Select(s => new ScriptItem(s)).ToList();
        // Each script is a tool of its own in the registry (capabilities, dependencies, Diagnostics).
        foreach (var item in All.Where(i => services.Tools.Find(i.Script.Manifest.Id) == null)) services.Tools.TryRegister(item.Script.Manifest, out _);
        Categories = ["All categories", .. All.Select(s => s.Category).Distinct()]; // loc: identity (shown through loc:Choice.Prefix)
        _category = Categories[0];
        _showAdvanced = services.Settings.Scripts.ShowAdvancedOptions;
        ApplyFilter();
    }


    public IReadOnlyList<ScriptItem> All { get; }
    public IReadOnlyList<string> Categories { get; }
    public ObservableCollection<ScriptItem> Visible { get; } = [];
    public ObservableCollection<ScriptParameterItem> Inputs { get; } = [];
    public ObservableCollection<PlanItem> Plan { get; } = [];
    public ObservableCollection<string> Messages { get; } = [];
    public ObservableCollection<ScriptHistoryEntry> History { get; } = [];

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _category;
    [ObservableProperty] private ScriptItem? _selected;
    [ObservableProperty] private bool _showAdvanced;
    [ObservableProperty] private string _resultSummary = "";
    [ObservableProperty] private DataView? _resultTable;
    [ObservableProperty] private string _problems = "";
    [ObservableProperty] private bool _hasPlan;
    [ObservableProperty] private string? _outputFolder;
    [ObservableProperty] private string? _backupFolder;

    public bool HasSelection => Selected != null;
    public bool HasAdvanced => Inputs.Any(i => i.IsAdvanced);
    public bool HasResult => _lastResult != null;
    public string RunLabel => Selected?.Script is { } s && s.WillChangeFiles(CurrentArguments()) ? L.T("Scripts.PreviewChanges") : L.T("Common.Run");
    public string Limitations => Selected?.Script.Limitations ?? "";
    public string SupportedInputs => Selected?.Script.SupportedInputs ?? "";
    public string PlanSummary => L.F("Scripts.0N0Of1Selected", Plan.Count(p => p.Selected), L.Count(Plan.Count, "Noun.change"));

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnCategoryChanged(string value) => ApplyFilter();
    partial void OnShowAdvancedChanged(bool value) => ParametersChanged();

    private void ApplyFilter()
    {
        string s = Search.Trim();
        var shown = All.Where(i => (Category == Categories[0] || i.Category == Category) &&
                                   (s.Length == 0 || i.Name.Contains(s, StringComparison.OrdinalIgnoreCase) || i.Description.Contains(s, StringComparison.OrdinalIgnoreCase)));
        var keep = Selected;
        Visible.Clear();
        foreach (var item in shown.OrderBy(i => i.Category).ThenBy(i => i.Name)) Visible.Add(item);
        if (keep != null && Visible.Contains(keep)) Selected = keep;
    }

    partial void OnSelectedChanged(ScriptItem? value)
    {
        Inputs.Clear();
        ClearResult();
        if (value != null)
        {
            foreach (var p in value.Script.Parameters)
                Inputs.Add(new ScriptParameterItem(this, p, Services.Settings.ToolState.GetValueOrDefault(StatePrefix + value.Script.Id + "." + p.Key)));
            // A sensible default folder for folder inputs that have never been set.
            string? folder = Services.Settings.InputFolderFor(ToolKeys.Scripts);
            if (folder != null)
                foreach (var input in Inputs.Where(i => i.Kind == ScriptParameterKind.Folder && i.Value.Length == 0)) input.Value = folder;
            string output = Services.Outputs.FolderFor(value.Script.DefaultOutput);
            foreach (var input in Inputs.Where(i => i.Kind == ScriptParameterKind.OutputFolder && i.Value.Length == 0)) input.Value = output;
        }
        ParametersChanged();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasAdvanced));
        OnPropertyChanged(nameof(Limitations));
        OnPropertyChanged(nameof(SupportedInputs));
    }

    private ScriptArguments CurrentArguments()
    {
        var args = new ScriptArguments();
        foreach (var i in Inputs) args[i.Key] = i.Value;
        return args;
    }

    /// <summary>Re-evaluates which inputs are visible (some depend on others) and the run button's label.</summary>
    public void ParametersChanged()
    {
        var args = CurrentArguments();
        foreach (var i in Inputs) i.IsVisible = i.Parameter.IsVisible(args) && (ShowAdvanced || !i.IsAdvanced);
        OnPropertyChanged(nameof(RunLabel));
        Problems = "";
    }

    private void ClearResult()
    {
        Plan.Clear();
        Messages.Clear();
        ResultTable = null;
        ResultSummary = "";
        HasPlan = false;
        OutputFolder = null;
        BackupFolder = null;
        _lastResult = null;
        _previewArgs = null;
        OnPropertyChanged(nameof(HasResult));
    }

    public void PickFile(ScriptParameterItem item)
    {
        string filter = item.Parameter.FileFilter ?? L.T("Scripts.AllFiles");
        string? file;
        if (item.Kind == ScriptParameterKind.OutputFile)
        {
            // Reports and manifests start in their own folder (Documents\Tibia_Foundry\Reports or \Manifests).
            var kind = Selected?.Script.DefaultOutput ?? OutputKind.Scripts;
            file = item.Value.Length > 0
                ? Services.Dialogs.SaveFile(item.Label, filter, Path.GetFileName(item.Value), Path.GetDirectoryName(item.Value))
                : Services.Outputs.SaveFile(kind == OutputKind.Scripts ? OutputKind.Reports : kind, item.Label, filter, "");
        }
        else
        {
            string? start = item.Value.Length > 0 ? Path.GetDirectoryName(item.Value) : Services.Settings.InputFolderFor(ToolKeys.Scripts);
            file = Services.Dialogs.OpenFile(item.Label, filter, start);
            if (file != null) Services.Settings.RememberFolder(ToolKeys.Scripts, "input", Path.GetDirectoryName(file));
        }
        if (file == null) return;
        item.Value = file;
    }

    public void PickFolder(ScriptParameterItem item)
    {
        string? folder = Services.Dialogs.PickFolder(item.Label, item.Value.Length > 0 ? item.Value : Services.Settings.InputFolderFor(ToolKeys.Scripts));
        if (folder == null) return;
        item.Value = folder;
        Services.Settings.RememberFolder(ToolKeys.Scripts, item.Kind == ScriptParameterKind.OutputFolder ? "output" : "input", folder);
    }

    public override Task OnNavigatedToAsync()
    {
        foreach (var i in Inputs) i.RefreshChoices();
        LoadHistory();
        return Task.CompletedTask;
    }

    private void LoadHistory()
    {
        History.Clear();
        foreach (var h in Services.ScriptHistory.Load()) History.Add(h);
    }

    private ScriptContext Context(ScriptArguments args, IProgress<Core.Common.OperationProgress> progress) => new()
    {
        Args = args,
        Progress = progress,
        Safety = Services.Settings.FileSafety,
        Backups = Services.Backups,
        Temp = Services.Temp,
        Prompt = Services.ConflictPrompt,
        WorkerThreads = Services.Settings.EffectiveWorkerThreads,
        StopOnError = Services.Settings.Performance.StopBatchOnError,
        ForceBackups = Services.Settings.Scripts.Backups == ScriptBackupMode.Always,
    };

    /// <summary>Runs the selected script, or produces the preview of a script that changes files.</summary>
    [RelayCommand]
    private async Task RunAsync()
    {
        if (Selected == null) return;
        var script = Selected.Script;
        var args = script.WithDefaults(CurrentArguments());
        var problems = script.Validate(args);
        if (problems.Count > 0)
        {
            Problems = string.Join("\n", problems);
            return;
        }
        SaveValues(script);
        ClearResult();
        ScriptResult? result = null;
        var started = DateTime.UtcNow;
        bool previewing = script.WillChangeFiles(args);
        var availability = Services.Tools.Availability(script.Manifest);
        if (!availability.CanRun)
        {
            Problems = L.F("Scripts.0CannotRun1", script.Name, availability.Problem);
            return;
        }
        bool ok = await base.RunAsync(script.Name + (previewing ? " (preview)" : ""), async (progress, ct) =>
        {
            result = await Task.Run(() => script.Run(Context(args, progress), ct), ct);
        }, gate: Services.ScriptOperations, output: OutputOf(script, args), onSuccess: () => ShowFinished(result, previewing));
        Record(script, args, result, started, script.WillChangeFiles(args) ? "Preview" : "Run", !ok && result == null);
        if (result == null) return;
        Show(result);
        if (result.Plan == null) return;
        _previewArgs = args;
        if (!Services.Settings.Scripts.PreviewFirst && result.Plan.Any(c => c.Selected && !c.Blocked))
        {
            // Preview not shown first (Settings → Scripts): confirmation is still required.
            await ApplyAsync();
        }
    }

    private void Show(ScriptResult result)
    {
        _lastResult = result;
        ResultSummary = result.Summary;
        foreach (var n in result.Notes) Messages.Add(n);
        foreach (var e in result.Errors.Take(200)) Messages.Add("Problem: " + e);
        if (result.Errors.Count > 200) Messages.Add(L.F("Scripts.And0N0MoreProblemsSee", result.Errors.Count - 200));
        ResultTable = ToDataView(result.Table);
        Plan.Clear();
        foreach (var c in result.Plan ?? []) Plan.Add(new PlanItem(c));
        foreach (var p in Plan) p.PropertyChanged += (_, _) => OnPropertyChanged(nameof(PlanSummary));
        HasPlan = Plan.Count > 0;
        OutputFolder = result.OutputFolder ?? result.OutputFiles.Select(Path.GetDirectoryName).FirstOrDefault();
        BackupFolder = result.BackupFolder;
        OnPropertyChanged(nameof(PlanSummary));
        OnPropertyChanged(nameof(HasResult));
        if (result.Errors.Count > 0) AppLog.Warn($"Script {Selected?.Name}: {L.Count(result.Errors.Count, "Noun.problem")}: {string.Join(" | ", result.Errors.Take(30))}");
    }

    private static DataView? ToDataView(ReportTable? table)
    {
        if (table == null) return null;
        var data = new DataTable(table.Title);
        foreach (var c in table.Columns) data.Columns.Add(c.Replace(".", "", StringComparison.Ordinal));
        foreach (var row in table.Rows.Take(20000)) data.Rows.Add(row.Cast<object>().ToArray());
        return data.DefaultView;
    }

    /// <summary>Applies the selected changes of the preview after a confirmation.</summary>
    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (Selected == null || _previewArgs == null || _lastResult?.Plan == null) return;
        var script = Selected.Script;
        var changes = _lastResult.Plan.Where(c => c.Selected && !c.Blocked).ToList();
        if (changes.Count == 0)
        {
            Services.Dialogs.Show(L.T("Scripts.NoChangeIsSelected"), MessageKind.Info, script.Name);
            return;
        }
        string list = string.Join("\n", changes.Take(15).Select(c => c.Target != null ? $"{c.Action}: {Path.GetFileName(c.Source)} → {c.Target}" : $"{c.Action}: {c.Source}"));
        if (changes.Count > 15) list += L.F("Scripts.And0N0More", changes.Count - 15);
        bool backups = Services.Settings.Safety.BackupBeforeChanges || Services.Settings.Scripts.Backups == ScriptBackupMode.Always;
        if (!Services.Dialogs.Confirm(L.F("Scripts.0WillNowChange12", script.Name, L.Count(changes.Count, "Noun.file"), list) +
                                      (backups ? L.T("Scripts.FilesThatAreReplacedOrMoved") : L.T("Scripts.BackupsAreOffReplacedFilesCannot")) +
                                      L.T("Scripts.Continue"), L.T("Scripts.ApplyChanges"), L.T("Scripts.Apply"), L.T("Common.Cancel"))) return;
        var args = _previewArgs;
        ScriptResult? result = null;
        var started = DateTime.UtcNow;
        bool ok = await base.RunAsync(script.Name + L.T("Scripts.Apply2"), async (progress, ct) =>
        {
            result = await Task.Run(() => script.Apply(Context(args, progress), _lastResult.Plan, ct), ct);
        }, gate: Services.ScriptOperations, output: OutputOf(script, args), onSuccess: () => ShowFinished(result, previewing: false));
        Record(script, args, result, started, "Apply", !ok && result == null);
        if (result == null) return;
        ClearResult();
        Show(result);
        ShowFinished(result, previewing: false);
    }

    /// <summary>The status after a script: its summary, with the written file or folder as a clickable path.</summary>
    private void ShowFinished(ScriptResult? result, bool previewing)
    {
        if (result == null) return;
        var kind = result.Failed > 0 || result.Errors.Count > 0 ? StatusKind.Warning : StatusKind.Success;
        string? written = result.OutputFiles.FirstOrDefault(File.Exists);
        if (!previewing && result.OutputFiles.Count == 1 && written != null)
            Services.Status.ShowSaved(written, isFile: true, before: L.T("Scripts.SavedTo"), after: ". " + result.Summary, kind: kind);
        else if (!previewing && result.ChangesApplied && result.OutputFolder != null && Directory.Exists(result.OutputFolder))
            Services.Status.ShowSaved(result.OutputFolder, isFile: false, before: result.Summary.TrimEnd('.') + " → ", kind: kind);
        else
            Services.Status.Show(result.Summary.Length > 0 ? result.Summary : "Finished.", kind);
    }

    /// <summary>Where a script's result goes (for the operation history).</summary>
    private static string? OutputOf(Script script, ScriptArguments args) =>
        script.Parameters.Where(p => p.Kind is ScriptParameterKind.OutputFile or ScriptParameterKind.OutputFolder).Select(p => args.PathValue(p.Key)).FirstOrDefault(v => v != null);

    private void Record(Script script, ScriptArguments args, ScriptResult? result, DateTime started, string kind, bool cancelledOrFailed)
    {
        if (!Services.Settings.Scripts.KeepHistory) return;
        string? input = script.Parameters.Where(p => p.Kind is ScriptParameterKind.Folder or ScriptParameterKind.Path or ScriptParameterKind.File)
            .Select(p => args.Text(p.Key)).FirstOrDefault(v => v.Length > 0);
        Services.ScriptHistory.Add(new ScriptHistoryEntry
        {
            ScriptId = script.Id,
            ScriptName = script.Name,
            StartedUtc = started,
            Seconds = (DateTime.UtcNow - started).TotalSeconds,
            Kind = kind,
            Input = input,
            Output = result?.OutputFolder,
            Processed = result?.Processed ?? 0,
            Succeeded = result?.Succeeded ?? 0,
            Failed = result?.Failed ?? 0,
            ChangesApplied = result?.ChangesApplied ?? false,
            Cancelled = cancelledOrFailed,
            Summary = result?.Summary ?? L.T("Scripts.CancelledOrFailedSeeTheStatus"),
            BackupFolder = result?.BackupFolder,
            OutputFiles = result?.OutputFiles.Take(20).ToList() ?? [],
        }, Services.Settings.Scripts.MaxHistoryEntries);
        LoadHistory();
    }

    private void SaveValues(Script script)
    {
        foreach (var i in Inputs) Services.Settings.ToolState[StatePrefix + script.Id + "." + i.Key] = i.Value;
        Services.SaveSettings();
    }

    [RelayCommand]
    private void SelectAll(string? value)
    {
        bool on = value != "none";
        foreach (var p in Plan.Where(p => p.IsEnabled)) p.Selected = on;
        OnPropertyChanged(nameof(PlanSummary));
    }

    [RelayCommand]
    private void ExportReport()
    {
        if (_lastResult?.Table == null) return;
        string? file = Services.Outputs.SaveFile(OutputKind.Reports, L.T("Scripts.SaveTheReport"), L.T("Scripts.CSVExcelCsvJSONJsonText"), (Selected?.Script.Id ?? "report") + ".csv");
        if (file == null) return;
        var table = _lastResult.Table;
        string? written = null;
        if (Try(L.T("Scripts.SavingTheReport"), () =>
        {
            using var output = Services.CreateOutput(L.T("Output.Scriptreport"));
            written = output.WriteFile(file, temp => ReportWriter.WriteTo(temp, table, ReportWriter.FormatFromPath(file),
                TextAttribution.ReportHeader(Attribution.ForNewAsset(copyright: null)) + Environment.NewLine + _lastResult.Summary));
        }))
        {
            if (written != null) Saved(written);
            else Services.Status.Show(L.T("Scripts.TheReportWasNotSavedThe"), StatusKind.Warning);
        }
    }

    [RelayCommand] private void OpenOutput() { if (OutputFolder != null) Services.RevealSaved(new StatusLink(OutputFolder, IsFile: false)); }
    [RelayCommand] private void OpenBackup() => Services.Shell.OpenFolder(BackupFolder);

    [RelayCommand]
    private void ClearHistory()
    {
        if (!Services.Dialogs.Confirm(L.T("Scripts.ClearTheScriptHistoryTheFiles"), L.T("Scripts.ClearHistory"), L.T("Common.Clear"), L.T("Common.Cancel"))) return;
        Services.ScriptHistory.Clear();
        LoadHistory();
    }
}
