using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Text.Json;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileMetadata;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>A file in the metadata tool's list.</summary>
public sealed partial class MetadataFileItem(MetadataSnapshot snapshot) : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Format), nameof(Reader), nameof(Size), nameof(StatusText), nameof(StatusGlyph), nameof(StatusBrush), nameof(Access))]
    private MetadataSnapshot _snapshot = snapshot;

    /// <summary>Changes not saved yet: field key → new value (null = remove).</summary>
    public Dictionary<string, string?> Pending { get; } = new(StringComparer.Ordinal);

    /// <summary>The result of the last save or batch for this file, or null.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusGlyph), nameof(StatusBrush))]
    private MetadataSaveResult? _lastResult;

    public string Path => Snapshot.Path;
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string Format => Snapshot.Format;
    public string Reader => Snapshot.ProviderName;
    public long Size => Snapshot.Stamp?.Length ?? 0;
    public string SizeText => ReportWriter.FormatSize(Size);
    public string Access => Snapshot.Errors.Count > 0 ? L.T("Metadata.NotReadable") : Snapshot.CanEdit ? "Editable" : L.T("Metadata.ReadOnly2");
    public bool HasPending => Pending.Count > 0;

    public string StatusText =>
        Snapshot.Errors.Count > 0 ? Snapshot.Errors[0]
        : HasPending ? L.Count(Pending.Count, "Metadata.UnsavedChanges")
        : LastResult is { } r ? $"{r.Status}: {r.Message}"
        : Snapshot.Warnings.Count > 0 ? Snapshot.Warnings[0]
        : Access;

    /// <summary>Segoe Fluent glyph of the status: error, unsaved, failed, saved, warning, read only; nothing for an editable file without news.</summary>
    public string StatusGlyph =>
        Snapshot.Errors.Count > 0 ? ""
        : HasPending ? ""
        : LastResult?.Status == MetadataSaveStatus.Failed ? ""
        : LastResult?.Status == MetadataSaveStatus.Saved ? ""
        : Snapshot.Warnings.Count > 0 ? ""
        : Snapshot.CanEdit ? "" : "";

    public string StatusBrush =>
        Snapshot.Errors.Count > 0 || LastResult?.Status == MetadataSaveStatus.Failed ? "App.Danger"
        : HasPending ? "App.Accent"
        : LastResult?.Status == MetadataSaveStatus.Saved ? "App.Success"
        : Snapshot.Warnings.Count > 0 ? "App.Warning" : "App.TextSecondary";

    public void Changed()
    {
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(StatusBrush));
    }
}

/// <summary>
/// A field in the inspector, for one file or for all selected files. Its value is edited as text
/// (check box for yes/no, list for choices); the original value, "different values" of a multiple
/// selection, read-only fields and unsaved changes are shown distinctly.
/// </summary>
public sealed partial class MetadataFieldRow : ObservableObject
{
    private readonly Action<MetadataFieldRow> _changed;
    private bool _loading;

    public MetadataFieldRow(MetadataFieldDefinition definition, string? original, bool mixed, bool editable, bool removable, string? pending, bool hasPending, Action<MetadataFieldRow> changed,
        int editableIn, int selected)
    {
        Definition = definition;
        Original = original;
        IsMixed = mixed;
        IsEditable = editable;
        IsRemovable = removable;
        _changed = changed;
        EditableIn = editableIn;
        Selected = selected;
        _loading = true;
        _pendingRemoval = hasPending && pending == null;
        Value = hasPending ? pending ?? "" : original ?? "";
        _isModified = hasPending;
        _loading = false;
    }

    public MetadataFieldDefinition Definition { get; }
    public string Key => Definition.Key;
    public string Label => Definition.Label;
    public string Category => Definition.Category;
    public string? Help => Definition.Help;
    public string? Original { get; }
    /// <summary>The selected files have different values (shown as "different values" until you type one).</summary>
    public bool IsMixed { get; }
    public bool IsEditable { get; }
    public bool IsRemovable { get; }
    public bool IsReadOnly => !IsEditable;
    /// <summary>How many of the selected files can store this field.</summary>
    public int EditableIn { get; }
    public int Selected { get; }

    public bool IsBoolean => Definition.Kind == MetadataValueKind.Boolean;
    public bool IsChoice => Definition.Kind == MetadataValueKind.Choice;
    public bool IsMultiline => Definition.Kind is MetadataValueKind.MultilineText || (Definition.Kind == MetadataValueKind.Text && !IsEditable && (Original?.Contains('\n') ?? false));
    public bool IsText => !IsBoolean && !IsChoice;
    public IReadOnlyList<string> Choices => Definition.Choices ?? [];

    public string Placeholder =>
        IsMixed ? L.T("Metadata.DifferentValues") : Definition.Kind switch
        {
            MetadataValueKind.List => L.T("Metadata.ValueValue"),
            MetadataValueKind.Date => "yyyy-MM-dd HH:mm:ss",
            MetadataValueKind.Decimal => L.T("Metadata.ForExample525163"),
            _ => IsEditable ? "(empty)" : "",
        };

    public string Tooltip
    {
        get
        {
            var lines = new List<string>();
            if (Help != null) lines.Add(Help);
            if (IsMixed) lines.Add(L.T("Metadata.TheSelectedFilesHaveDifferentValues"));
            else if (IsModified) lines.Add(L.F("Metadata.Original0", (string.IsNullOrEmpty(Original) ? "(empty)" : Original)));
            if (IsReadOnly) lines.Add(L.T("Metadata.ReadOnlyInThisFormat"));
            else if (Selected > 1 && EditableIn < Selected) lines.Add(L.F("Metadata.Only0OfThe1Selected", EditableIn, Selected));
            return string.Join(Environment.NewLine, lines);
        }
    }

    [ObservableProperty] private string _value = "";
    [ObservableProperty] private bool _isModified;
    [ObservableProperty] private bool _pendingRemoval;
    [ObservableProperty] private string? _error;

    public bool BoolValue
    {
        get => Value == "true";
        set => Value = value ? "true" : "false";
    }

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(BoolValue));
        if (_loading) return;
        PendingRemoval = false;
        string? normalized = MetadataValues.Normalize(Definition, value);
        Error = MetadataValues.Validate(Definition, normalized);
        IsModified = !MetadataEngine.SameValue(Definition, normalized, Original) || IsMixed && normalized != null;
        OnPropertyChanged(nameof(Tooltip));
        _changed(this);
    }

    /// <summary>The value to save: null for a removal.</summary>
    public string? PendingValue => PendingRemoval ? null : MetadataValues.Normalize(Definition, Value);

    [RelayCommand]
    private void Remove()
    {
        if (!IsRemovable) return;
        _loading = true;
        Value = "";
        _loading = false;
        PendingRemoval = true;
        IsModified = true;
        Error = null;
        _changed(this);
    }

    [RelayCommand]
    private void Revert()
    {
        _loading = true;
        Value = Original ?? "";
        _loading = false;
        PendingRemoval = false;
        IsModified = false;
        Error = null;
        _changed(this);
    }
}

/// <summary>A group of the inspector ("Description", "Camera"…).</summary>
public sealed record MetadataFieldGroup(string Category, IReadOnlyList<MetadataFieldRow> Rows);

/// <summary>An operation of the batch list, with its description.</summary>
public sealed record BatchOperationItem(BatchOperation Operation)
{
    public string Text => Operation.Describe();
}

/// <summary>One file of a batch preview.</summary>
public sealed record BatchPlanRow(string File, string Format, string Changes, string Skipped);

/// <summary>
/// The Universal File Metadata Viewer and Editor (8.6): add files and folders (also by dropping
/// them), see their metadata grouped by category, edit one file or many at once, run batch
/// operations with a preview, use presets, compare files and export reports. Saving goes through
/// <see cref="MetadataEngine"/>: validated, verified, optionally backed up, never half-written.
/// </summary>
public sealed partial class MetadataViewModel : PageViewModel
{
    private readonly MetadataEngine _engine = new();
    private List<MetadataFileItem> _selection = [];

    public MetadataViewModel(AppServices services) : base(services, BuiltInTools.FileMetadata)
    {
        FilesView = CollectionViewSource.GetDefaultView(Files);
        FilesView.Filter = o => o is MetadataFileItem f && Matches(f);
        LoadOptions();
        Files.CollectionChanged += (_, _) => RefreshSummary();
    }

    public ObservableCollection<MetadataFileItem> Files { get; } = [];
    public ICollectionView FilesView { get; }

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _formatFilter = AllFormats;
    [ObservableProperty] private IReadOnlyList<string> _formats = [AllFormats];
    [ObservableProperty] private bool _includeSubfolders = true;
    [ObservableProperty] private bool _backups = true;
    [ObservableProperty] private bool _keepModifiedTime = true;
    [ObservableProperty] private string _summary = L.T("Metadata.NoFilesYet");
    [ObservableProperty] private string _selectionTitle = L.T("Metadata.NothingSelected");
    [ObservableProperty] private string _selectionDetails = L.T("Metadata.AddFilesOrFoldersOrDrop");
    [ObservableProperty] private IReadOnlyList<MetadataFieldGroup> _groups = [];
    [ObservableProperty] private IReadOnlyList<string> _selectionWarnings = [];
    [ObservableProperty] private bool _showEmptyFields;
    [ObservableProperty] private int _pendingCount;

    public const string AllFormats = "All formats"; // loc: identity (shown through Choice.MetadataFilter)
    public const string WithChanges = "With unsaved changes"; // loc: identity
    public const string WithProblems = "With errors or warnings"; // loc: identity

    public IReadOnlyList<MetadataFileItem> Selection => _selection;
    public bool IsEmpty => Files.Count == 0;
    public bool HasSelection => _selection.Count > 0;

    public override string? UnsavedChanges => PendingCount == 0 ? null : L.F("Metadata.FileMetadata0WithUnsavedMetadata", L.Count(PendingCount, "Noun.file"));

    partial void OnSearchChanged(string value) => FilesView.Refresh();
    partial void OnFormatFilterChanged(string value) => FilesView.Refresh();
    partial void OnShowEmptyFieldsChanged(bool value) => BuildInspector();
    partial void OnIncludeSubfoldersChanged(bool value) => SaveOptions();
    partial void OnBackupsChanged(bool value) => SaveOptions();
    partial void OnKeepModifiedTimeChanged(bool value) => SaveOptions();

    private bool _loadingOptions;

    private void LoadOptions()
    {
        _loadingOptions = true;
        var store = Services.ToolSettings;
        IncludeSubfolders = store.GetBool(Manifest, "recursive");
        Backups = store.GetBool(Manifest, "backups");
        KeepModifiedTime = store.GetBool(Manifest, "keepModifiedTime");
        Presets = ReadPresets();
        _loadingOptions = false;
    }

    private void SaveOptions()
    {
        if (_loadingOptions) return;
        var store = Services.ToolSettings;
        store.SetBool(Manifest, "recursive", IncludeSubfolders);
        store.SetBool(Manifest, "backups", Backups);
        store.SetBool(Manifest, "keepModifiedTime", KeepModifiedTime);
        Services.SaveSettings();
    }

    private bool Matches(MetadataFileItem f)
    {
        if (FormatFilter == WithChanges && !f.HasPending) return false;
        if (FormatFilter == WithProblems && f.Snapshot.Errors.Count == 0 && f.Snapshot.Warnings.Count == 0 && f.LastResult?.Status != MetadataSaveStatus.Failed) return false;
        if (FormatFilter is not (AllFormats or WithChanges or WithProblems) && f.Format != FormatFilter) return false;
        if (string.IsNullOrWhiteSpace(Search)) return true;
        string s = Search.Trim();
        return f.Path.Contains(s, StringComparison.OrdinalIgnoreCase) || f.Format.Contains(s, StringComparison.OrdinalIgnoreCase)
               || f.Snapshot.Fields.Any(x => x.Value?.Contains(s, StringComparison.OrdinalIgnoreCase) == true);
    }

    private void RefreshSummary()
    {
        int editable = Files.Count(f => f.Snapshot.CanEdit), errors = Files.Count(f => f.Snapshot.Errors.Count > 0);
        PendingCount = Files.Count(f => f.HasPending);
        Summary = Files.Count == 0 ? L.T("Metadata.NoFilesYet") :
            L.F("Metadata.01N0Editable2N0", L.Count(Files.Count, "Noun.file"), editable, Files.Count - editable - errors) +
            (errors > 0 ? L.F("Metadata.0N0NotReadable", errors) : "") + (PendingCount > 0 ? L.F("Metadata.0N0WithUnsavedChanges", PendingCount) : "");
        var formats = Files.Select(f => f.Format).Distinct().Order(StringComparer.CurrentCulture).ToList();
        Formats = [AllFormats, WithChanges, WithProblems, .. formats];
        OnPropertyChanged(nameof(UnsavedChanges));
        OnPropertyChanged(nameof(IsEmpty));
        SaveCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    // ------------------------------------------------------------------ adding files

    [RelayCommand]
    private Task AddFilesAsync()
    {
        string[]? files = Services.Dialogs.OpenFiles(L.T("Metadata.AddFilesToInspectOrEdit2"), L.T("Metadata.AllFiles"));
        return files == null ? Task.CompletedTask : AddPathsAsync(files);
    }

    [RelayCommand]
    private Task AddFolderAsync()
    {
        string? folder = Services.Dialogs.PickFolder(IncludeSubfolders ? L.T("Metadata.AddEveryFileOfAFolder2") : L.T("Metadata.AddEveryFileOfAFolder"));
        return folder == null ? Task.CompletedTask : AddPathsAsync([folder]);
    }

    /// <summary>Adds files and folders (from the pickers or dropped on the page); folders are expanded, with subfolders when chosen.</summary>
    public async Task AddPathsAsync(IReadOnlyList<string> paths)
    {
        var known = Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loaded = new List<MetadataSnapshot>();
        int skippedFolders = 0;
        await RunAsync(L.T("Metadata.ReadingMetadata"), async (progress, ct) => await Task.Run(() =>
        {
            var list = new List<string>();
            foreach (string p in paths)
            {
                if (File.Exists(p)) list.Add(Path.GetFullPath(p));
                else if (Directory.Exists(p))
                {
                    var options = new EnumerationOptions { RecurseSubdirectories = IncludeSubfolders, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint };
                    try { list.AddRange(Directory.EnumerateFiles(p, "*", options)); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skippedFolders++; AppLog.Warn($"File Metadata: {p} could not be listed: {ex.Message}"); }
                }
            }
            list = list.Where(known.Add).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(new Core.Common.OperationProgress(i, list.Count, L.F("Metadata.Reading01N0Of2", Path.GetFileName(list[i]), i + 1, list.Count)));
                loaded.Add(_engine.Load(list[i], ct));
            }
        }, ct), operation: "read", onSuccess: () => { });
        foreach (var s in loaded) Files.Add(new MetadataFileItem(s));
        RefreshSummary();
        if (loaded.Count > 0 || skippedFolders > 0)
            Services.Status.Show(L.F("Metadata.Added0", L.Count(loaded.Count, "Noun.file")) + (skippedFolders > 0 ? L.F("Metadata.0CouldNotBeListedSee", L.Count(skippedFolders, "Noun.folder")) : "."),
                skippedFolders > 0 ? StatusKind.Warning : StatusKind.Success);
        else if (paths.Count > 0) Services.Status.Show(L.T("Metadata.NoNewFilesTheyAreIn"), StatusKind.Info);
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (_selection.Count == 0) return;
        int pending = _selection.Count(f => f.HasPending);
        if (pending > 0 && !Services.Dialogs.Confirm(L.Count(pending, "Metadata.RemoveWithUnsavedChanges"),
                L.T("Metadata.RemoveFromTheList"), L.T("Metadata.RemoveAndDiscard"), L.T("Common.Cancel"))) return;
        foreach (var f in _selection.ToList()) Files.Remove(f);
        SetSelection([]);
    }

    [RelayCommand]
    private void Clear()
    {
        if (Files.Count == 0) return;
        if (PendingCount > 0 && !Services.Dialogs.Confirm(L.Count(PendingCount, "Metadata.ClearWithUnsavedChanges"),
                L.T("Metadata.ClearTheList"), L.T("Metadata.ClearAndDiscard"), L.T("Common.Cancel"))) return;
        Files.Clear();
        Reference = null;
        SetSelection([]);
    }

    /// <summary>Reads the selected files again (after another program changed them); unsaved changes of those files are discarded after asking.</summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        var items = _selection.Count > 0 ? _selection.ToList() : Files.ToList();
        if (items.Count == 0) return;
        int pending = items.Count(f => f.HasPending);
        if (pending > 0 && !Services.Dialogs.Confirm(L.F("Metadata.ReadingTheFilesAgainDiscardsThe", L.Count(pending, "Noun.file")), L.T("Metadata.ReadAgain"), L.T("Metadata.ReadAgain"), L.T("Common.Cancel"))) return;
        var snapshots = new List<MetadataSnapshot>();
        await RunAsync(L.T("Metadata.ReadingMetadataAgain"), async (progress, ct) => await Task.Run(() =>
        {
            for (int i = 0; i < items.Count; i++)
            {
                progress.Report(new Core.Common.OperationProgress(i, items.Count, Path.GetFileName(items[i].Path)));
                snapshots.Add(_engine.Load(items[i].Path, ct));
            }
        }, ct), operation: "read", onSuccess: () => Services.Status.Show(L.F("Metadata.Read0Again", L.Count(items.Count, "Noun.file")), StatusKind.Success));
        for (int i = 0; i < snapshots.Count; i++)
        {
            items[i].Pending.Clear();
            items[i].LastResult = null;
            items[i].Snapshot = snapshots[i];
            items[i].Changed();
        }
        RefreshSummary();
        BuildInspector();
    }

    // ------------------------------------------------------------------ selection and inspector

    public void SetSelection(IReadOnlyList<MetadataFileItem> items)
    {
        _selection = items.ToList();
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(HasSelection));
        BuildInspector();
        RevertCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        UseAsReferenceCommand.NotifyCanExecuteChanged();
        PreviewBatchCommand.NotifyCanExecuteChanged();
    }

    private void BuildInspector()
    {
        if (_selection.Count == 0)
        {
            SelectionTitle = L.T("Metadata.NothingSelected");
            SelectionDetails = Files.Count == 0 ? L.T("Metadata.AddFilesOrFoldersOrDrop") : L.T("Metadata.SelectOneFileToSeeIts");
            SelectionWarnings = [];
            Groups = [];
            return;
        }
        if (_selection.Count == 1)
        {
            var s = _selection[0].Snapshot;
            SelectionTitle = s.FileName;
            SelectionDetails = $"{s.Format} · {s.ProviderName} · {_selection[0].Access}";
        }
        else
        {
            SelectionTitle = L.F("Metadata.0N0FilesSelected", _selection.Count);
            var formats = _selection.GroupBy(f => f.Format).Select(g => $"{g.Key} ({g.Count()})");
            SelectionDetails = string.Join(" · ", formats) + L.T("Metadata.AValueTypedHereIsSet");
        }
        SelectionWarnings = _selection.Count == 1
            ? [.. _selection[0].Snapshot.Errors, .. _selection[0].Snapshot.Warnings]
            : _selection.SelectMany(f => f.Snapshot.Errors.Select(e => $"{f.Name}: {e}")).Take(10).ToList();

        var keys = _selection.SelectMany(f => f.Snapshot.Fields).GroupBy(f => f.Key).Select(g => g.First().Definition).ToList();
        var rows = new List<MetadataFieldRow>();
        foreach (var definition in keys)
        {
            var fields = _selection.Select(f => (Item: f, Field: f.Snapshot.Field(definition.Key))).Where(x => x.Field != null).ToList();
            var values = fields.Select(x => x.Field!.Value).Distinct().ToList();
            bool mixed = fields.Count < _selection.Count ? values.Any(v => v != null) && fields.Count > 0 && _selection.Count > 1 : values.Count > 1;
            int editableIn = fields.Count(x => x.Field!.Editable && x.Item.Snapshot.CanEdit || FileSystemMetadata.IsFileSystemKey(definition.Key) && x.Field!.Editable && x.Item.Snapshot.Errors.Count == 0);
            bool editable = editableIn > 0;
            bool removable = fields.Any(x => x.Field!.Removable && x.Field.HasValue && x.Item.Snapshot.CanEdit);
            string? original = values.Count == 1 ? values[0] : null;
            if (!ShowEmptyFields && !editable && values.All(string.IsNullOrEmpty)) continue;
            if (!ShowEmptyFields && editable && values.All(string.IsNullOrEmpty) && !IsCommonField(definition)) continue;
            // Unsaved changes: the same pending value in every file that has one.
            var pendings = _selection.Where(f => f.Pending.ContainsKey(definition.Key)).Select(f => f.Pending[definition.Key]).Distinct().ToList();
            bool hasPending = pendings.Count == 1;
            if (_selection.Count > 1 && fields.Count > 1 && definition.Key is "fs.name" or "fs.folder" or "fs.size") continue;
            rows.Add(new MetadataFieldRow(definition, original, mixed && !hasPending, editable, removable, hasPending ? pendings[0] : null, hasPending, OnRowChanged, editableIn, _selection.Count));
        }
        Groups = rows.GroupBy(r => r.Category).OrderBy(g => MetadataBatch.CategoryIndex(g.Key))
            .Select(g => new MetadataFieldGroup(g.Key, g.ToList())).ToList();
    }

    /// <summary>Descriptive fields shown even when empty (so they can be filled in): title, description, keywords, authors, copyright…</summary>
    private static bool IsCommonField(MetadataFieldDefinition d) => d.Category is MetadataFields.Description or MetadataFields.Rights or MetadataFields.File
        || d.Key is "album" or "genres" or "year" or "track" or "dateTaken" or "archiveComment";

    private void OnRowChanged(MetadataFieldRow row)
    {
        foreach (var item in _selection)
        {
            var field = item.Snapshot.Field(row.Key);
            bool canStore = field != null && (field.Editable || row.PendingRemoval && field.Removable)
                            && (item.Snapshot.CanEdit || FileSystemMetadata.IsFileSystemKey(row.Key) && item.Snapshot.Errors.Count == 0);
            if (!row.IsModified || !canStore || row.Error != null) item.Pending.Remove(row.Key);
            else if (row.PendingRemoval) { if (field!.HasValue) item.Pending[row.Key] = null; else item.Pending.Remove(row.Key); }
            else if (MetadataEngine.SameValue(row.Definition, field!.Value, row.PendingValue)) item.Pending.Remove(row.Key);
            else item.Pending[row.Key] = row.PendingValue;
            item.Changed();
        }
        RefreshSummary();
    }

    // ------------------------------------------------------------------ saving

    private MetadataSaveOptions SaveOptionsNow() => new()
    {
        Backups = Backups ? Services.Backups : null,
        KeepModifiedTime = KeepModifiedTime,
        Operation = L.T("Metadata.FileMetadataSave"),
    };

    private bool CanSave() => Files.Any(f => f.HasPending);

    /// <summary>Saves the unsaved changes (of the selected files when some have changes, else of every file), after a confirmation that lists what can happen.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var items = (_selection.Any(f => f.HasPending) ? _selection : Files.ToList()).Where(f => f.HasPending).ToList();
        if (items.Count == 0) return;
        var plans = items.Select(f => new MetadataFilePlan(f.Snapshot, f.Pending.Select(p => new MetadataEdit(p.Key, p.Value)).ToList(), [])).ToList();
        if (!ConfirmApply(L.T("Metadata.SaveChanges"), L.F("Metadata.SaveTheChangesTo0", L.Count(items.Count, "Noun.file")), plans, L.T("Common.Save"))) return;
        await ApplyAsync(L.T("Metadata.SavingMetadata"), plans, items);
    }

    private bool ConfirmApply(string title, string heading, IReadOnlyList<MetadataFilePlan> plans, string action)
    {
        var withEdits = plans.Where(p => p.Edits.Count > 0).ToList();
        var warnings = withEdits.SelectMany(p => _engine.Warnings(p.Snapshot, p.Edits)).Distinct().ToList();
        var removals = withEdits.SelectMany(p => p.Edits.Where(e => e.IsRemoval).Select(e => p.Snapshot.Field(e.Key)?.Label ?? e.Key)).Distinct().ToList();
        var byFormat = withEdits.GroupBy(p => p.Snapshot.Format)
            .Select(g => (IReadOnlyList<string>)[g.Key, g.Count().ToString("N0"), string.Join(", ", g.SelectMany(p => p.Edits).Select(e => e.Key).Distinct().Select(k => MetadataFields.All.TryGetValue(k, out var d) ? d.Label : k))])
            .ToList();
        int notApplicable = plans.Count - withEdits.Count;
        var sections = new List<ReportSection>
        {
            new(L.T("Metadata.WhatChanges")) { Table = new DialogTable([L.T("Common.Format"), L.T("Common.Files"), L.T("Metadata.Fields")], byFormat, NumericColumns: 0) },
            new(L.T("Metadata.HowItIsSaved"))
            {
                Bullets =
                [
                    L.T("Metadata.EachFileIsWrittenToA"),
                    Backups ? L.F("Metadata.AVerifiedBackupOfEachFile", Services.Backups.Root) : L.T("Metadata.NoBackupsAreMadeTheOption"),
                    KeepModifiedTime ? L.T("Metadata.TheFilesKeepTheirModifiedTime") : L.T("Metadata.TheFilesModifiedTimeBecomesThe"),
                    L.T("Metadata.FilesChangedByAnotherProgramSince"),
                ],
            },
        };
        if (removals.Count > 0) sections.Add(new ReportSection(L.T("Metadata.Removed")) { Text = L.T("Metadata.TheseFieldsAreDeletedFromThe") + string.Join(", ", removals) + ".", Tone = ReportTone.Warning });
        if (warnings.Count > 0) sections.Add(new ReportSection(L.T("Metadata.GoodToKnow")) { Bullets = warnings });
        if (notApplicable > 0) sections.Add(new ReportSection(L.T("Metadata.NotChanged")) { Text = L.F("Metadata.0CannotTakeAnyOfThese", L.Count(notApplicable, "Noun.file")) });
        return Services.Dialogs.ShowReport(new DialogReport
        {
            Title = title, Kind = removals.Count > 0 ? MessageKind.Warning : MessageKind.Question, Heading = heading, Sections = sections, PrimaryText = action, CancelText = L.T("Common.Cancel"),
        }) == ReportAnswer.Primary;
    }

    private async Task ApplyAsync(string title, IReadOnlyList<MetadataFilePlan> plans, IReadOnlyList<MetadataFileItem> items)
    {
        IReadOnlyList<MetadataSaveResult> results = [];
        var options = SaveOptionsNow();
        bool ran = await RunAsync(title, async (progress, ct) => results = await Task.Run(() => _engine.Run(plans, options, progress, ct), CancellationToken.None),
            operation: "save", onSuccess: () => { });
        if (results.Count == 0) return;
        for (int i = 0; i < results.Count; i++)
        {
            var item = items[i];
            var r = results[i];
            item.LastResult = r;
            if (r.Status == MetadataSaveStatus.Saved && r.Snapshot != null)
            {
                item.Snapshot = r.Snapshot;
                item.Pending.Clear();
            }
            else if (r.Status is MetadataSaveStatus.Unchanged or MetadataSaveStatus.Skipped) item.Pending.Clear();
            item.Changed();
        }
        RefreshSummary();
        BuildInspector();
        ShowResults(title, results, cancelled: !ran);
    }

    private void ShowResults(string title, IReadOnlyList<MetadataSaveResult> results, bool cancelled)
    {
        int saved = results.Count(r => r.Status == MetadataSaveStatus.Saved), failed = results.Count(r => r.Status == MetadataSaveStatus.Failed);
        int unchanged = results.Count(r => r.Status == MetadataSaveStatus.Unchanged), skipped = results.Count(r => r.Status == MetadataSaveStatus.Skipped);
        int stopped = results.Count(r => r.Status == MetadataSaveStatus.Cancelled);
        var sections = new List<ReportSection>
        {
            new(L.T("Common.Result"))
            {
                Rows =
                [
                    new(L.T("Metadata.Saved"), saved.ToString("N0"), saved > 0 ? ReportTone.Success : ReportTone.Normal),
                    new(L.T("Metadata.AlreadyAsRequested"), unchanged.ToString("N0")),
                    new(L.T("Metadata.NotApplicable"), skipped.ToString("N0"), skipped > 0 ? ReportTone.Warning : ReportTone.Normal),
                    new(L.T("Metadata.FailedLeftUnchanged"), failed.ToString("N0"), failed > 0 ? ReportTone.Danger : ReportTone.Normal),
                    new(L.T("Metadata.NotStartedCancelled"), stopped.ToString("N0"), stopped > 0 ? ReportTone.Warning : ReportTone.Normal),
                ],
            },
        };
        var problems = results.Where(r => r.Status is MetadataSaveStatus.Failed or MetadataSaveStatus.Skipped).Take(30)
            .Select(r => (IReadOnlyList<string>)[Path.GetFileName(r.Path), r.Message + (r.SkippedFields.Count > 0 ? " " + string.Join(" ", r.SkippedFields.Take(3)) : "")]).ToList();
        if (problems.Count > 0) sections.Add(new ReportSection(L.T("Metadata.FilesThatWereNotChanged")) { Table = new DialogTable([L.T("Common.File"), L.T("ClientStartup.Why")], problems), Tone = failed > 0 ? ReportTone.Danger : ReportTone.Normal });
        var partial = results.Where(r => r.Status == MetadataSaveStatus.Saved && r.SkippedFields.Count > 0).Take(30)
            .Select(r => (IReadOnlyList<string>)[Path.GetFileName(r.Path), string.Join(" ", r.SkippedFields)]).ToList();
        if (partial.Count > 0) sections.Add(new ReportSection(L.T("Metadata.SavedWithFieldsLeftOut")) { Table = new DialogTable([L.T("Common.File"), L.T("Metadata.NotChangedInThisFile")], partial), Collapsed = true });
        if (results.FirstOrDefault(r => r.BackupPath != null) is { } b) sections.Add(new ReportSection(L.T("Metadata.Backups")) { Rows = [ReportRow.Path(L.T("Metadata.BackupSet"), Path.GetDirectoryName(Path.GetDirectoryName(b.BackupPath!))!)] , Note = L.T("Metadata.ScriptsBackupAndRestorePutsAny") });
        Services.Dialogs.ShowReport(new DialogReport
        {
            Title = title, Kind = failed > 0 ? MessageKind.Warning : MessageKind.Info,
            Heading = cancelled ? L.T("Metadata.StoppedBeforeEveryFileWasDone") : failed > 0 ? L.T("Metadata.DoneWithFailures") : "Done",
            Summary = L.F("Metadata.01N0Saved2N0", L.Count(results.Count, "Noun.file"), saved, failed),
            Sections = sections,
        });
        Services.Status.Show(L.F("Metadata.01N0Saved2N02", title, saved, unchanged + skipped, failed), failed > 0 ? StatusKind.Warning : StatusKind.Success);
    }

    private bool CanRevert() => _selection.Any(f => f.HasPending);

    [RelayCommand(CanExecute = nameof(CanRevert))]
    private void Revert()
    {
        foreach (var f in _selection)
        {
            f.Pending.Clear();
            f.Changed();
        }
        RefreshSummary();
        BuildInspector();
    }

    // ------------------------------------------------------------------ batch

    public IReadOnlyList<MetadataFieldDefinition> BatchFields { get; } = MetadataFields.BatchEditable;

    public IReadOnlyList<KeyValuePair<BatchAction, string>> BatchActions { get; } =
    [
        new(BatchAction.Set, L.T("Metadata.SetTo")), new(BatchAction.AddItems, L.T("Metadata.AddItems")), new(BatchAction.RemoveItems, L.T("Metadata.RemoveItems")),
        new(BatchAction.ReplaceItem, L.T("Metadata.ReplaceItem")), new(BatchAction.Remove, L.T("Metadata.RemoveField")), new(BatchAction.CopyFromReference, L.T("Metadata.CopyFromReference")),
    ];

    public ObservableCollection<BatchOperationItem> Operations { get; } = [];

    [ObservableProperty] private BatchAction _batchAction = BatchAction.Set;
    [ObservableProperty] private MetadataFieldDefinition? _batchField = MetadataFields.Copyright;
    [ObservableProperty] private string _batchValue = "";
    [ObservableProperty] private string _batchReplacement = "";
    [ObservableProperty] private IReadOnlyList<BatchPlanRow> _planRows = [];
    [ObservableProperty] private string _planSummary = L.T("Metadata.AddOperationsThenPreviewThemFor");
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ReferenceText))] private MetadataFileItem? _reference;

    public string ReferenceText => Reference == null ? L.T("Metadata.NoReferenceFileNeededByCopy") : "Reference: " + Reference.Path;
    public bool NeedsReplacement => BatchAction == BatchAction.ReplaceItem;
    public bool NeedsValue => BatchAction is not (BatchAction.Remove or BatchAction.CopyFromReference);

    partial void OnBatchActionChanged(BatchAction value)
    {
        OnPropertyChanged(nameof(NeedsReplacement));
        OnPropertyChanged(nameof(NeedsValue));
    }

    private List<MetadataFilePlan> _plans = [];

    [RelayCommand]
    private void AddOperation()
    {
        if (BatchField == null) return;
        bool list = BatchField.Kind == MetadataValueKind.List;
        if (BatchAction is BatchAction.AddItems or BatchAction.RemoveItems or BatchAction.ReplaceItem && !list)
        {
            Services.Dialogs.Show(L.F("Metadata.0IsNotAListItems", BatchField.Label), MessageKind.Warning, L.T("Metadata.BatchEdit"));
            return;
        }
        string? value = NeedsValue ? MetadataValues.Normalize(BatchField, BatchValue) : null;
        if (NeedsValue && value == null)
        {
            Services.Dialogs.Show(L.T("Metadata.TypeTheValueFirstToDelete"), MessageKind.Warning, L.T("Metadata.BatchEdit"));
            return;
        }
        if (BatchAction == BatchAction.Set && MetadataValues.Validate(BatchField, value) is { } problem)
        {
            Services.Dialogs.Show(problem, MessageKind.Warning, L.T("Metadata.BatchEdit"));
            return;
        }
        Operations.Add(new BatchOperationItem(new BatchOperation(BatchAction, BatchField.Key, value, NeedsReplacement ? BatchReplacement.Trim() : null)));
        BatchValue = "";
        BatchReplacement = "";
        PreviewBatchCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemoveOperation(BatchOperationItem? item)
    {
        if (item != null) Operations.Remove(item);
        PreviewBatchCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ClearOperations()
    {
        Operations.Clear();
        PlanRows = [];
        _plans = [];
        PlanSummary = L.T("Metadata.AddOperationsThenPreviewThemFor");
        PreviewBatchCommand.NotifyCanExecuteChanged();
        ApplyBatchCommand.NotifyCanExecuteChanged();
    }

    private bool CanUseAsReference() => _selection.Count == 1 && _selection[0].Snapshot.Errors.Count == 0;

    [RelayCommand(CanExecute = nameof(CanUseAsReference))]
    private void UseAsReference() => Reference = _selection[0];

    private bool CanPreview() => Operations.Count > 0 && _selection.Count > 0;

    /// <summary>What the operations would do to each selected file: the changes, and what a file cannot take (never silently skipped).</summary>
    [RelayCommand(CanExecute = nameof(CanPreview))]
    private void PreviewBatch()
    {
        var ops = Operations.Select(o => o.Operation).ToList();
        _plans = _selection.Select(f => MetadataBatch.Plan(f.Snapshot, ops, Reference?.Snapshot)).ToList();
        PlanRows = _plans.Select(p => new BatchPlanRow(p.Snapshot.FileName, p.Snapshot.Format,
            p.Edits.Count == 0 ? "—" : string.Join("; ", p.Edits.Select(e => $"{Label(e.Key)} → {(e.Value == null ? "(removed)" : Shorten(e.Value))}")),
            string.Join(" ", p.Skipped))).ToList();
        int changing = _plans.Count(p => p.Edits.Count > 0);
        PlanSummary = L.F("Metadata.01N0WouldChange2", L.Count(_plans.Count, "Noun.selected_file"), changing, _plans.Count - changing) +
                      (_plans.Any(p => p.Skipped.Count > 0) ? L.T("Metadata.TheNotAppliedColumnSaysWhy") : ".");
        ApplyBatchCommand.NotifyCanExecuteChanged();
    }

    private static string Label(string key) => MetadataFields.All.TryGetValue(key, out var d) ? d.Label : key;
    private static string Shorten(string s) => s.Length > 60 ? s[..57] + "…" : s.ReplaceLineEndings(" ");

    private bool CanApplyBatch() => _plans.Any(p => p.Edits.Count > 0);

    [RelayCommand(CanExecute = nameof(CanApplyBatch))]
    private async Task ApplyBatchAsync()
    {
        var items = _plans.Select(p => Files.FirstOrDefault(f => f.Path == p.Snapshot.Path)).ToList();
        if (items.Any(i => i == null) || _plans.Any(p => items.First(i => i!.Path == p.Snapshot.Path)!.Snapshot != p.Snapshot))
        {
            Services.Dialogs.Show(L.T("Metadata.TheFilesChangedSinceThePreview"), MessageKind.Warning, L.T("Metadata.BatchEdit"));
            return;
        }
        if (items.Any(i => i!.HasPending) && !Services.Dialogs.Confirm(L.T("Metadata.SomeOfTheseFilesHaveUnsaved"), L.T("Metadata.BatchEdit"), L.T("Metadata.Continue"), L.T("Common.Cancel")))
            return;
        if (!ConfirmApply(L.T("Metadata.BatchEdit"), L.F("Metadata.Apply0To1", L.Count(Operations.Count, "Noun.operation"), L.Count(_plans.Count(p => p.Edits.Count > 0), "Noun.file")), _plans, L.T("Metadata.Apply2"))) return;
        var plans = _plans;
        _plans = [];
        ApplyBatchCommand.NotifyCanExecuteChanged();
        await ApplyAsync(L.T("Metadata.BatchEdit"), plans, items!);
        PlanRows = [];
        PlanSummary = L.T("Metadata.AppliedPreviewAgainToCheckThe");
    }

    // ------------------------------------------------------------------ presets

    [ObservableProperty] private IReadOnlyList<MetadataPreset> _presets = [];
    [ObservableProperty] private MetadataPreset? _selectedPreset;

    private IReadOnlyList<MetadataPreset> ReadPresets()
    {
        string json = Services.ToolSettings.Get(Manifest, "presets");
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<List<MetadataPreset>>(json) ?? []).Where(p => p.Validate() == null).ToList();
        }
        catch (JsonException ex)
        {
            AppLog.Warn("File Metadata: the presets could not be read: " + ex.Message);
            return [];
        }
    }

    private bool WritePresets(IReadOnlyList<MetadataPreset> presets)
    {
        if (!Services.ToolSettings.TrySet(Manifest, "presets", JsonSerializer.Serialize(presets), out string? problem))
        {
            Services.Dialogs.Show(L.T("Metadata.ThePresetsCouldNotBeSaved") + problem, MessageKind.Warning, L.T("Common.Presets"));
            return false;
        }
        Services.SaveSettings();
        Presets = presets;
        return true;
    }

    /// <summary>Adds the preset's values to the batch operations (then Preview and Apply as usual).</summary>
    [RelayCommand]
    private void UsePreset()
    {
        if (SelectedPreset == null) return;
        foreach (var op in SelectedPreset.ToOperations()) Operations.Add(new BatchOperationItem(op));
        PreviewBatchCommand.NotifyCanExecuteChanged();
        Services.Status.Show(L.F("Metadata.Preset0AddedToTheOperations", SelectedPreset.Name), StatusKind.Info);
    }

    /// <summary>Saves the current "Set" operations (and added keywords) as a preset.</summary>
    [RelayCommand]
    private void SavePreset()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool addKeywords = true;
        foreach (var op in Operations.Select(o => o.Operation))
        {
            if (op.Action == BatchAction.Set && op.Value != null) { values[op.Key] = op.Value; if (op.Key == MetadataFields.Keywords.Key) addKeywords = false; }
            else if (op.Action == BatchAction.AddItems && op.Key == MetadataFields.Keywords.Key && op.Value != null) values[op.Key] = op.Value;
        }
        if (values.Count == 0)
        {
            Services.Dialogs.Show(L.T("Metadata.APresetIsMadeFromSet"), MessageKind.Info, L.T("Common.Presets"));
            return;
        }
        string? name = Services.Dialogs.Prompt(L.T("Metadata.SaveAsPreset"), L.T("Metadata.NameOfThePresetAnExisting"), SelectedPreset?.Name ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var preset = new MetadataPreset { Name = name.Trim(), Values = values, AddKeywords = addKeywords };
        if (preset.Validate() is { } problem)
        {
            Services.Dialogs.Show(problem, MessageKind.Warning, L.T("Common.Presets"));
            return;
        }
        if (WritePresets([.. Presets.Where(p => !string.Equals(p.Name, preset.Name, StringComparison.CurrentCultureIgnoreCase)), preset]))
            SelectedPreset = Presets[^1];
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedPreset == null) return;
        if (!Services.Dialogs.Confirm(L.F("Metadata.DeleteThePreset0FilesAre", SelectedPreset.Name), L.T("Common.Presets"), L.T("Metadata.Delete"), L.T("Common.Cancel"))) return;
        WritePresets(Presets.Where(p => p != SelectedPreset).ToList());
        SelectedPreset = null;
    }

    // ------------------------------------------------------------------ comparison and export

    [ObservableProperty] private DataView? _comparison;
    [ObservableProperty] private bool _onlyDifferences = true;
    [ObservableProperty] private bool _compareFileSystem;
    [ObservableProperty] private string _comparisonSummary = L.T("Metadata.SelectTwoOrMoreFilesThen");

    partial void OnOnlyDifferencesChanged(bool value) => Compare();
    partial void OnCompareFileSystemChanged(bool value) => Compare();

    [RelayCommand]
    private void Compare()
    {
        if (_selection.Count < 2) { Comparison = null; ComparisonSummary = L.T("Metadata.SelectTwoOrMoreFilesThen"); return; }
        var files = _selection.Take(20).Select(f => f.Snapshot).ToList();
        var rows = MetadataBatch.Compare(files, CompareFileSystem);
        var table = new DataTable();
        table.Columns.Add("Field");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Field" };
        foreach (var f in files)
        {
            string name = f.FileName;
            for (int i = 2; !names.Add(name); i++) name = $"{f.FileName} ({i})";
            table.Columns.Add(name);
        }
        int shown = 0;
        foreach (var r in rows.Where(r => !OnlyDifferences || r.Differs))
        {
            table.Rows.Add([$"{r.Label} ({r.Category})", .. r.Values.Select(v => (object)(v ?? "—"))]);
            shown++;
        }
        Comparison = table.DefaultView;
        ComparisonSummary = L.F("Metadata.01N0Different", L.Count(rows.Count, "Noun.field"), rows.Count(r => r.Differs)) + (OnlyDifferences ? L.F("Metadata.Showing0N0", shown) : "") +
                            (_selection.Count > 20 ? L.T("Metadata.TheFirst20SelectedFilesAre") : ".");
    }

    [RelayCommand]
    private void ExportCsv() => Export(ReportFormat.Csv);

    [RelayCommand]
    private void ExportJson() => Export(ReportFormat.Json);

    private void Export(ReportFormat format)
    {
        var items = _selection.Count > 0 ? _selection : Files.ToList();
        if (items.Count == 0) return;
        string ext = format == ReportFormat.Csv ? "csv" : "json";
        string? path = Services.Outputs.SaveFile(OutputKind.Reports, L.T("Metadata.ExportMetadata"), format == ReportFormat.Csv ? L.T("Metadata.CSVCsvCsv") : L.T("Metadata.JSONJsonJson"),
            $"metadata-{DateTime.Now:yyyy-MM-dd-HHmm}.{ext}");
        if (path == null) return;
        try
        {
            var snapshots = items.Select(i => i.Snapshot).ToList();
            var results = items.Where(i => i.LastResult != null).ToDictionary(i => i.Path, i => i.LastResult!);
            string text = format == ReportFormat.Csv ? MetadataBatch.ToCsv(snapshots, results) : MetadataBatch.ToJson(snapshots, results);
            File.WriteAllText(path, text, new System.Text.UTF8Encoding(format == ReportFormat.Csv));
            Services.Status.ShowSaved(path, isFile: true, L.F("Metadata.ExportedTheMetadataOf0To", L.Count(items.Count, "Noun.file")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Services.Dialogs.ShowError(L.T("Metadata.TheReportCouldNotBeSaved") + ex.Message, ex);
        }
    }
}
