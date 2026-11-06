using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Tools;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>A queued or running operation on the Diagnostics page.</summary>
public sealed partial class OperationRow(OperationRecord record, DiagnosticsViewModel owner) : ObservableObject
{
    public OperationRecord Record { get; } = record;
    public string Id => Record.Id;
    public string Title => Record.Title;
    public string Tool => Record.ToolName;
    public bool CanCancel => Record.CanCancel && !Record.IsFinished;
    public string CancelHint => Record.CanCancel ? L.T("Diagnostics.StopThisOperationTemporaryFilesAre") : L.T("Diagnostics.ThisOperationCannotBeStoppedSafely");

    public string State => OperationTracker.Describe(Record.State);
    public string Stage => Record.Stage;
    public string Output => Record.Output ?? "";
    public bool HasOutput => !string.IsNullOrEmpty(Record.Output);
    public bool IsIndeterminate => Record.Fraction == null;
    public double Percent => (Record.Fraction ?? 0) * 100;

    /// <summary>Measured progress: items done of the total the operation reported (never estimated).</summary>
    public string ProgressText => Record.State == OperationState.Queued
        ? L.F("Diagnostics.WaitingForAFreeSlot0", Record.Waited.TotalSeconds)
        : Record.Fraction is { } f ? L.F("Diagnostics.0N0Of1N02", Record.Done, Record.Total, f * 100) : L.T("Diagnostics.RunningThisOperationReportsNoTotal");

    public string ElapsedText => Record.State == OperationState.Queued ? "—" : Format(Record.Elapsed);

    /// <summary>Remaining time from the measured rate, labeled as an estimate; "—" when it cannot be estimated reliably.</summary>
    public string RemainingText => Record.EstimatedRemaining is { } r ? L.F("Diagnostics.About0Estimate", Format(r)) : "—";

    public string ThroughputText => Record.Throughput is { } t ? L.F("Diagnostics.0N1ItemsSMeasured", t) : "—";

    public string Problems => Record.Errors.Count + Record.Warnings.Count == 0 ? "" : $"{L.Count(Record.Errors.Count, "Noun.error")}, {L.Count(Record.Warnings.Count, "Noun.warning")}";

    public void Refresh() => OnPropertyChanged(string.Empty);

    [RelayCommand]
    private void Cancel()
    {
        if (owner.Cancel(Record)) OnPropertyChanged(nameof(CanCancel));
    }

    [RelayCommand] private void OpenOutput() => owner.OpenPath(Record.Output);
    [RelayCommand] private void ShowLog() => owner.ShowLogOf(Record.Id);

    internal static string Format(TimeSpan t) => t.TotalHours >= 1 ? L.F("Diagnostics.0H1D2Min", (int)t.TotalHours, t.Minutes) : t.TotalMinutes >= 1 ? L.F("Diagnostics.0Min1D2S", (int)t.TotalMinutes, t.Seconds) : $"{t.TotalSeconds:0.0} s";
}

/// <summary>A finished operation from the history.</summary>
public sealed partial class HistoryRow(OperationSummary s, DiagnosticsViewModel owner) : ObservableObject
{
    public OperationSummary Summary { get; } = s;
    public string Started => Summary.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string Tool => Summary.ToolName;
    public string Title => Summary.Title;
    public string State => OperationTracker.Describe(Summary.State);
    public bool Failed => Summary.State == OperationState.Failed;
    public string Duration => OperationRow.Format(TimeSpan.FromSeconds(Summary.Seconds)) + (Summary.WaitedSeconds >= 1 ? L.F("Diagnostics.00SWaiting", Summary.WaitedSeconds) : "");
    public string Counts => Summary.Total > 0 ? $"{Summary.Done:N0} / {Summary.Total:N0}" : "";
    public string Result => Summary.FirstProblem ?? Summary.Summary ?? "";
    public bool HasOutput => !string.IsNullOrEmpty(Summary.Output);

    [RelayCommand] private void OpenOutput() => owner.OpenPath(Summary.Output);
    [RelayCommand] private void ShowLog() => owner.ShowLogOf(Summary.Id);
}

/// <summary>One measured value or estimate on the Diagnostics page.</summary>
public sealed record MetricRow(string Name, string Value, string Kind, string Note = "");

/// <summary>A registered tool on the Diagnostics page.</summary>
public sealed record ToolRow(string Name, string Type, string Version, string Capabilities, string Status, bool Available, string Dependencies, string Formats, string License, string Limitations);

/// <summary>A category of the diagnostics report, with its check box.</summary>
public sealed partial class ReportCategoryOption(ReportCategory category, DiagnosticsViewModel owner) : ObservableObject
{
    public ReportCategory Category { get; } = category;
    public string Name => Category.Name;
    public string Description => Category.Description;
    public bool CanChange => Category.Optional;

    [ObservableProperty] private bool _isIncluded = category.IncludedByDefault;

    partial void OnIsIncludedChanged(bool value) => owner.InvalidatePreview();
}

/// <summary>A self-check finding with its (confirmed) fix.</summary>
public sealed partial class HealthRow(HealthResult result, DiagnosticsViewModel owner) : ObservableObject
{
    public HealthResult Result { get; } = result;
    public string Area => Result.Area;
    public string Check => Result.Check;
    public string Severity => Result.SeverityText;
    public string Message => Result.Message;
    public string Remedy => Result.Remedy ?? "";
    public bool HasFix => Result.Fix != HealthFix.None;
    public string FixLabel => Result.Fix switch { HealthFix.CreateFolder => L.T("Diagnostics.CreateFolder"), HealthFix.UseDefaultFolder => L.T("Diagnostics.UseTheDefaultFolder"), _ => "" };

    [RelayCommand] private void Fix() => owner.ApplyFix(Result);
}

/// <summary>
/// Diagnostics: running and recent operations, process measurements, the log viewer, the
/// self-check, the registered tools and the diagnostics report. Measurements are taken only while
/// this page is open (and can be turned off); nothing here sends data anywhere.
/// </summary>
public sealed partial class DiagnosticsViewModel : PageViewModel
{
    private readonly MainViewModel _main;
    private readonly ResourceMonitor _monitor = new();
    private DispatcherTimer? _timer;
    private bool _visible;
    private IReadOnlyList<ReportEntry>? _preview;
    private IReadOnlyList<HealthResult>? _lastHealth;
    private ResourceSample? _lastSample;
    private DateTime _lastJobRefresh;
    private bool _refreshPending;

    public DiagnosticsViewModel(AppServices services, MainViewModel main) : base(services, BuiltInTools.Diagnostics)
    {
        _main = main;
        ReportCategories = DiagnosticsReport.Categories.Select(c => new ReportCategoryOption(c, this)).ToList();
        // Progress can be reported thousands of times per operation: refresh only while the page is
        // visible, with at most one refresh waiting in the dispatcher queue at a time.
        services.Jobs.Changed += _ =>
        {
            if (!_visible || _refreshPending || System.Windows.Application.Current?.Dispatcher is not { } dispatcher) return;
            _refreshPending = true;
            dispatcher.BeginInvoke(() =>
            {
                _refreshPending = false;
                OnJobsChanged();
            }, DispatcherPriority.Background);
        };
    }

    public ObservableCollection<OperationRow> ActiveOperations { get; } = [];
    public ObservableCollection<HistoryRow> History { get; } = [];
    public ObservableCollection<MetricRow> Metrics { get; } = [];
    public ObservableCollection<DiskSpace> Disks { get; } = [];
    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public ObservableCollection<HealthRow> HealthResults { get; } = [];
    public ObservableCollection<ToolRow> ToolRows { get; } = [];
    public ObservableCollection<ReportEntry> PreviewEntries { get; } = [];
    public IReadOnlyList<ReportCategoryOption> ReportCategories { get; }

    public bool HasActiveOperations => ActiveOperations.Count > 0;
    public string OperationsSummary => L.F("Diagnostics.0Running1WaitingUpTo", Services.Jobs.RunningCount, Services.Jobs.QueuedCount, Services.Operations.Limit);

    // ================================================================== operations

    private void OnJobsChanged()
    {
        if (!_visible) return;
        // Progress can be reported many times a second: refresh at most four times per second.
        if ((DateTime.UtcNow - _lastJobRefresh).TotalMilliseconds < 250 && Services.Jobs.Active.Count == ActiveOperations.Count) return;
        _lastJobRefresh = DateTime.UtcNow;
        RefreshOperations();
    }

    private void RefreshOperations()
    {
        var active = Services.Jobs.Active;
        foreach (var gone in ActiveOperations.Where(r => !active.Contains(r.Record)).ToList()) ActiveOperations.Remove(gone);
        foreach (var record in active.Where(a => ActiveOperations.All(r => r.Record != a))) ActiveOperations.Add(new OperationRow(record, this));
        foreach (var row in ActiveOperations) row.Refresh();
        var history = Services.Jobs.History;
        if (History.Count != Math.Min(history.Count, 200) || (history.Count > 0 && History.FirstOrDefault()?.Summary.Id != history[0].Id))
        {
            History.Clear();
            foreach (var h in history.Take(200)) History.Add(new HistoryRow(h, this));
        }
        OnPropertyChanged(nameof(HasActiveOperations));
        OnPropertyChanged(nameof(OperationsSummary));
    }

    internal bool Cancel(OperationRecord record)
    {
        if (!Services.Dialogs.Confirm(L.F("Diagnostics.Stop0FilesItWasWriting", record.Title), L.T("Diagnostics.CancelOperation"), L.T("Common.Stop"), L.T("Diagnostics.KeepRunning")))
            return false;
        bool requested = Services.Jobs.RequestCancel(record.Id);
        if (!requested) Services.Status.Show(L.F("Diagnostics.0CannotBeCancelled", record.Title), StatusKind.Warning);
        return requested;
    }

    internal void OpenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Services.RevealSaved(new StatusLink(path, File.Exists(path)));
    }

    [RelayCommand]
    private void ClearHistory()
    {
        if (!Services.Dialogs.Confirm(L.T("Diagnostics.ClearTheOperationHistoryFilesThe"), L.T("Diagnostics.ClearHistory"), L.T("Common.Clear"), L.T("Common.Cancel"))) return;
        Services.Jobs.ClearHistory();
        History.Clear();
    }

    // ================================================================== monitoring

    public bool MonitoringEnabled
    {
        get => Services.Settings.Diagnostics.PerformanceMonitoring;
        set
        {
            Services.Settings.Diagnostics.PerformanceMonitoring = value;
            Services.SaveSettings();
            OnPropertyChanged();
            UpdateTimer();
            if (!value) Metrics.Clear();
            else Sample();
        }
    }

    public IReadOnlyList<int> SampleIntervals => Core.Settings.DiagnosticsSettings.SampleIntervals;

    public int SampleIntervalSeconds
    {
        get => Services.Settings.Diagnostics.SampleIntervalSeconds;
        set
        {
            Services.Settings.Diagnostics.SampleIntervalSeconds = value;
            Services.Settings.Diagnostics.Normalize();
            Services.SaveSettings();
            OnPropertyChanged();
            UpdateTimer();
        }
    }

    /// <summary>True while the timer runs (only when the page is open and monitoring is on).</summary>
    public bool IsSampling => _timer?.IsEnabled == true;

    /// <summary>How many samples were taken (for the overhead check in tests).</summary>
    public int SampleCount { get; private set; }

    private void UpdateTimer()
    {
        bool run = _visible && (MonitoringEnabled || ActiveOperations.Count > 0);
        if (run)
        {
            _timer ??= new DispatcherTimer(DispatcherPriority.Background, System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
            _timer.Interval = TimeSpan.FromSeconds(MonitoringEnabled ? SampleIntervalSeconds : 1);
            _timer.Tick -= OnTick;
            _timer.Tick += OnTick;
            _timer.Start();
        }
        else _timer?.Stop();
        OnPropertyChanged(nameof(IsSampling));
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_visible) { _timer?.Stop(); return; }
        RefreshOperations();
        if (MonitoringEnabled) Sample();
    }

    /// <summary>Takes one measurement and shows it (measured values are labeled; nothing is estimated).</summary>
    public void Sample()
    {
        var s = _monitor.Sample();
        _lastSample = s;
        SampleCount++;
        var history = Services.Jobs.History;
        Metrics.Clear();
        Metrics.Add(new MetricRow(L.T("Diagnostics.ProcessorUseWholeProgram"), s.CpuPercent is { } c ? L.F("Diagnostics.000OfAllCores", c) : "measuring…", "measured", L.T("Diagnostics.AverageSinceThePreviousMeasurement")));
        Metrics.Add(new MetricRow(L.T("Diagnostics.MemoryInUseWorkingSet"), ReportWriter.FormatSize(s.WorkingSet), "measured"));
        Metrics.Add(new MetricRow(L.T("Diagnostics.PrivateMemory"), ReportWriter.FormatSize(s.PrivateBytes), "measured"));
        Metrics.Add(new MetricRow(L.T("Diagnostics.NETManagedMemory"), ReportWriter.FormatSize(s.ManagedHeap), "measured", L.F("Diagnostics.GarbageCollections0N01N0", s.Gen0, s.Gen1, s.Gen2)));
        Metrics.Add(new MetricRow(L.T("Diagnostics.ThreadsAndHandles"), L.F("Diagnostics.0N0Threads1N0Handles", s.Threads, s.Handles), "measured"));
        Metrics.Add(new MetricRow(L.T("Diagnostics.RunningTime"), OperationRow.Format(s.Uptime), "measured"));
        Metrics.Add(new MetricRow(L.T("Common.Operations"), L.F("Diagnostics.0Running1Waiting", Services.Jobs.RunningCount, Services.Jobs.QueuedCount + Services.Operations.Waiting), "measured"));
        var recent = history.Take(100).ToList();
        Metrics.Add(new MetricRow(L.T("Diagnostics.RecentFailures"), L.F("Diagnostics.0OfTheLast1", recent.Count(h => h.State == OperationState.Failed), L.Count(recent.Count, "Noun.operation")), "measured"));
        var rates = recent.Where(h => h.Total > 0 && h.Seconds > 0.5 && h.State is OperationState.Completed or OperationState.CompletedWithWarnings).Take(10)
            .Select(h => h.Done / h.Seconds).ToList();
        double? throughput = rates.Count > 0 ? rates.Average() : null;
        Metrics.Add(new MetricRow(L.T("Diagnostics.ThroughputOfRecentOperations"), throughput is { } t ? L.F("Diagnostics.0N0ItemsSOnAverage", t) : L.T("Diagnostics.NotAvailableNoCompletedOperationReported"), throughput is null ? L.T("Diagnostics.NotAvailable") : "measured"));
        Metrics.Add(new MetricRow(L.T("Diagnostics.PerOperationProcessorAndMemoryUse"), L.T("Diagnostics.NotMeasured"), L.T("Diagnostics.NotAvailable"), L.T("Diagnostics.OperationsShareOneProcessSoTheir")));
        Disks.Clear();
        foreach (var d in ResourceMonitor.Disks(OutputLocations.Kinds.Select(k => (k.DisplayName, Services.Outputs.FolderFor(k.Kind))))) Disks.Add(d);
    }

    // ================================================================== logs

    /// <summary>Days back from today (0 = all log files that are kept).</summary>
    [ObservableProperty] private int _logPeriodDays = 2;
    [ObservableProperty] private LogLevel _logMinimumLevel = LogLevel.Info;
    [ObservableProperty] private string _logSource = "";
    [ObservableProperty] private string _logOperation = "";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private LogEntry? _selectedLogEntry;
    [ObservableProperty] private string _logSummary = "";
    [ObservableProperty] private int _selectedTab;

    public IReadOnlyList<LogLevel> LogLevels { get; } = Enum.GetValues<LogLevel>();

    public IReadOnlyList<SettingChoice<int>> LogPeriods { get; } =
        [new(1, L.T("Choice.Date.Today")), new(2, L.T("Diagnostics.TodayAndYesterday")), new(7, L.T("AssetLibrary.Last7Days")), new(30, L.T("AssetLibrary.Last30Days")), new(0, L.T("Diagnostics.AllKeptLogs"))];

    private DateTime? LogFrom => LogPeriodDays > 0 ? DateTime.Today.AddDays(1 - LogPeriodDays) : null;
    public ObservableCollection<string> LogSources { get; } = [];

    /// <summary>Reads the log files with the filter (in the background; at most 2,000 entries, the newest).</summary>
    [RelayCommand]
    public async Task SearchLogAsync()
    {
        var filter = new LogFilter
        {
            From = LogFrom, MinimumLevel = LogMinimumLevel,
            Source = string.IsNullOrWhiteSpace(LogSource) ? null : LogSource.Trim(), Operation = string.IsNullOrWhiteSpace(LogOperation) ? null : LogOperation.Trim(),
            Text = string.IsNullOrWhiteSpace(LogText) ? null : LogText.Trim(),
        };
        var from = LogFrom;
        var files = AppLog.Files().Where(f => from == null || f.LastWriteTime >= from.Value).Select(f => f.FullName).ToList();
        var entries = await Task.Run(() => LogReader.Read(files, filter, 2000));
        LogEntries.Clear();
        foreach (var e in entries.Reverse()) LogEntries.Add(e);
        var sources = entries.Select(e => e.Source).Concat(LogSources).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();
        LogSources.Clear();
        foreach (var s in sources) LogSources.Add(s);
        LogSummary = L.Count(entries.Count, "Diagnostics.LogEntriesIn", L.Count(files.Count, "Noun.file"), entries.Count == 2000 ? L.T("Diagnostics.TheNewest2000AreShown") : "");
    }

    [RelayCommand]
    private Task ClearLogFilter()
    {
        LogPeriodDays = 2;
        LogMinimumLevel = LogLevel.Info;
        LogSource = LogOperation = LogText = "";
        return SearchLogAsync();
    }

    /// <summary>Shows the log lines of one operation.</summary>
    public void ShowLogOf(string operationId)
    {
        LogOperation = operationId;
        LogPeriodDays = 0;
        LogMinimumLevel = LogLevel.Debug;
        LogSource = LogText = "";
        SelectedTab = 2;
        _ = SearchLogAsync();
    }

    [RelayCommand] private void OpenLogFolder() => Services.Shell.OpenFolder(AppLog.LogFilePath);

    // ================================================================== self-check

    [ObservableProperty] private string _healthSummary = L.T("Diagnostics.NotRunYet");

    [RelayCommand]
    public async Task RunSelfCheckAsync()
    {
        HealthSummary = "Checking…";
        var results = await Task.Run(() => HealthCheck.Run(Services));
        _lastHealth = results;
        HealthResults.Clear();
        foreach (var r in results.OrderByDescending(r => r.Severity).ThenBy(r => r.Area)) HealthResults.Add(new HealthRow(r, this));
        int errors = results.Count(r => r.Severity == HealthSeverity.Error), warnings = results.Count(r => r.Severity == HealthSeverity.Warning);
        HealthSummary = errors + warnings == 0 ? L.F("Diagnostics.NoProblemsFound0Checks", results.Count) : L.F("Diagnostics.0ThatPreventOperations1Of", L.Count(errors, "Noun.error"), L.Count(warnings, "Noun.warning"), results.Count);
        AppLog.Info("Self-check: " + HealthSummary, source: "Diagnostics");
        InvalidatePreview();
    }

    /// <summary>Applies a fix the user confirmed. Nothing is repaired without asking.</summary>
    internal void ApplyFix(HealthResult result)
    {
        if (result.Folder is not { } kind) return;
        string folder = Services.Outputs.FolderFor(kind);
        switch (result.Fix)
        {
            case HealthFix.CreateFolder:
                if (!Services.Dialogs.Confirm(L.F("Diagnostics.CreateTheFolder0", folder), L.T("Diagnostics.SelfCheck"), L.T("Diagnostics.Create"), L.T("Common.Cancel"))) return;
                var state = OutputLocations.Prepare(folder);
                if (state.Usable) Info(L.T("Diagnostics.Created") + state.Path);
                else Services.Dialogs.Show(state.Problem!, MessageKind.Warning, L.T("Diagnostics.SelfCheck"));
                break;
            case HealthFix.UseDefaultFolder:
                string fallback = Services.Outputs.DefaultFolderFor(kind);
                if (!Services.Dialogs.Confirm(L.F("Diagnostics.StopUsing0For1And", folder, OutputLocations.Info(kind).DisplayName, fallback), L.T("Diagnostics.SelfCheck"), L.T("Diagnostics.UseDefault"), L.T("Common.Cancel"))) return;
                Services.Settings.SetExplicitFolder(kind, null);
                if (kind == OutputKind.Logs) Services.ApplyLogSettings();
                Services.SaveSettings();
                Info(L.F("Diagnostics.0NowUse1", OutputLocations.Info(kind).DisplayName, fallback));
                break;
        }
        _ = RunSelfCheckAsync();
    }

    // ================================================================== tools

    private void RefreshTools()
    {
        ToolRows.Clear();
        foreach (var t in Services.Tools.Tools)
        {
            var a = Services.Tools.Availability(t);
            string deps = a.Dependencies.Count == 0 ? "none" : string.Join("; ", a.Dependencies.Select(d => $"{d.Dependency.Name} ({(d.Dependency.Required ? "required" : "optional")}): {d.State switch { DependencyState.Available => "available", DependencyState.NotChecked => "checked when used", DependencyState.Missing => "missing", _ => "not compatible" }}"));
            string formats = (t.Inputs.Count > 0 ? "Reads " + string.Join(", ", t.Inputs.Select(f => f.ToString())) : "") + (t.Outputs.Count > 0 ? (t.Inputs.Count > 0 ? ". " : "") + "Writes " + string.Join(", ", t.Outputs.Select(f => f.ToString())) : "");
            ToolRows.Add(new ToolRow(t.Name, t.Type.ToString(), t.Version, ToolManifest.CapabilityNames(t.Capabilities),
                a.CanRun ? (a.Degraded.Count > 0 ? L.T("Diagnostics.AvailableWithLimits") : "available") : "unavailable: " + a.Problem, a.CanRun, deps, formats, t.License.Name, t.Limitations));
        }
        foreach (var r in Services.Tools.Rejected)
            ToolRows.Add(new ToolRow(r.Name, "(rejected)", "", "", L.T("Diagnostics.NotLoaded") + string.Join(" ", r.Problems), false, "", "", "", ""));
    }

    // ================================================================== report

    [ObservableProperty] private bool _includeFullPaths;
    [ObservableProperty] private ReportEntry? _selectedPreviewEntry;
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private string _reportStatus = L.T("Diagnostics.ChooseWhatToInclude");

    public bool HasPreview => _preview != null;

    private bool _revertingFullPaths;

    partial void OnIncludeFullPathsChanged(bool value)
    {
        if (_revertingFullPaths) return;
        if (value && !Services.Dialogs.Confirm(L.T("Diagnostics.IncludeFullFolderPathsYourWindows"),
                L.T("Diagnostics.IncludeFullPaths"), L.T("Diagnostics.IncludeThem"), L.T("Diagnostics.KeepThemHidden")))
        {
            _revertingFullPaths = true;
            IncludeFullPaths = false;
            _revertingFullPaths = false;
            return;
        }
        InvalidatePreview();
    }

    internal void InvalidatePreview()
    {
        if (_preview == null) return;
        _preview = null;
        PreviewEntries.Clear();
        PreviewText = "";
        ReportStatus = L.T("Diagnostics.TheOptionsChangedPreviewTheReport");
        OnPropertyChanged(nameof(HasPreview));
        SaveReportCommand.NotifyCanExecuteChanged();
    }

    private ReportOptions Options() => new(ReportCategories.Where(c => c.IsIncluded || !c.CanChange).Select(c => c.Category.Id).ToHashSet(), IncludeFullPaths, Services.Settings.Diagnostics.ReportLogDays);

    /// <summary>Collects the report's files exactly as they will be saved and lists them for review.</summary>
    [RelayCommand]
    public async Task PreviewReportAsync()
    {
        var options = Options();
        ReportStatus = "Collecting…";
        if (_lastSample == null) _lastSample = _monitor.Sample();
        var sample = _lastSample;
        var health = _lastHealth;
        try
        {
            _preview = await Task.Run(() => DiagnosticsCollector.Collect(Services, options, health, sample));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            _preview = null;
            AppLog.Error("The diagnostics report could not be collected.", ex, source: "Diagnostics");
            ReportStatus = L.T("Diagnostics.TheReportCouldNotBeCollected") + ex.Message;
            OnPropertyChanged(nameof(HasPreview));
            SaveReportCommand.NotifyCanExecuteChanged();
            return;
        }
        PreviewEntries.Clear();
        foreach (var e in _preview) PreviewEntries.Add(e);
        SelectedPreviewEntry = PreviewEntries.FirstOrDefault();
        ReportStatus = L.F("Diagnostics.01BeforeCompressionPlusManifest", L.Count(_preview.Count + 1, "Noun.file"), ReportWriter.FormatSize(_preview.Sum(e => e.Size))) +
                       (options.IncludeFullPaths ? L.T("Diagnostics.FullPathsAreINCLUDED") : L.T("Diagnostics.PathsUserNameAndComputerName")) + L.T("Diagnostics.SelectAFileToReadIt");
        OnPropertyChanged(nameof(HasPreview));
        SaveReportCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPreviewEntryChanged(ReportEntry? value)
    {
        if (value == null) { PreviewText = ""; return; }
        string text = System.Text.Encoding.UTF8.GetString(value.Content, 0, (int)Math.Min(value.Content.Length, 256 * 1024));
        PreviewText = value.Content.Length > 256 * 1024 ? text + L.F("Diagnostics.0MoreInTheSavedReport", ReportWriter.FormatSize(value.Content.Length - 256 * 1024)) : text;
    }

    /// <summary>Path of the last report saved in this session.</summary>
    public string? LastReport { get; private set; }

    /// <summary>Saves the previewed files as a ZIP in the Reports folder (Documents\Tibia_Foundry\Reports). Nothing is sent anywhere.</summary>
    [RelayCommand(CanExecute = nameof(HasPreview))]
    private void SaveReport()
    {
        if (_preview == null) return;
        string? folder = Services.Outputs.PrepareOrAsk(OutputKind.Reports, L.T("Diagnostics.DiagnosticsReport"));
        if (folder == null) return;
        string file = FileNames.Unique(folder, $"{Core.Common.AppIdentity.Id}-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}", ".zip");
        try
        {
            DiagnosticsReport.WriteZip(file, _preview, sanitized: !IncludeFullPaths, DateTime.UtcNow);
            LastReport = file;
            AppLog.Info($"Diagnostics report saved ({_preview.Count + 1} files, {(IncludeFullPaths ? "full paths included" : "sanitized")}).", source: "Diagnostics");
            Services.Status.ShowSaved(file, isFile: true, before: L.T("Diagnostics.DiagnosticsReportSavedTo"), after: L.T("Diagnostics.NotSentAnywhere"));
            ReportStatus = L.T("Diagnostics.SavedClickThePathInThe");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("The diagnostics report could not be saved.", ex, source: "Diagnostics");
            Services.Status.Show(L.T("Diagnostics.TheDiagnosticsReportCouldNotBe") + ex.Message, StatusKind.Error);
            Services.Dialogs.ShowError(L.T("Diagnostics.TheDiagnosticsReportCouldNotBe2"), ex, log: false);
        }
    }

    [RelayCommand] private void OpenReportsFolder() => OpenPath(LastReport ?? Services.Outputs.FolderFor(OutputKind.Reports));

    // ================================================================== navigation

    public override async Task OnNavigatedToAsync()
    {
        _visible = true;
        RefreshOperations();
        RefreshTools();
        if (MonitoringEnabled) Sample();
        UpdateTimer();
        if (LogEntries.Count == 0) await SearchLogAsync();
        if (_lastHealth == null) await RunSelfCheckAsync();
    }

    /// <summary>Called when another page is shown: measurements stop.</summary>
    public void OnNavigatedFrom()
    {
        _visible = false;
        _timer?.Stop();
        OnPropertyChanged(nameof(IsSampling));
    }

    [RelayCommand] private void OpenSettings() => _main.SelectedPage = _main.Pages.OfType<SettingsViewModel>().First();
}
