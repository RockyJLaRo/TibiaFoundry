using System.Text.Json;
using System.Text.Json.Serialization;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Diagnostics;

/// <summary>Life cycle of an operation.</summary>
public enum OperationState { Queued, Running, Completed, CompletedWithWarnings, Failed, Cancelled }

/// <summary>
/// One long operation (an export, conversion, save or script run) as shown on the Diagnostics
/// page. Progress is what the operation itself reports — nothing is estimated or invented except
/// the remaining time, which is only offered when it can be computed from measured progress
/// (<see cref="EstimatedRemaining"/>).
/// </summary>
public sealed class OperationRecord
{
    private readonly object _sync = new();
    private readonly List<string> _warnings = [];
    private readonly List<string> _errors = [];

    public string Id { get; init; } = "";
    public string ToolId { get; init; } = "";
    public string ToolName { get; init; } = "";
    public string Title { get; init; } = "";
    public bool CanCancel { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    public OperationState State { get; private set; } = OperationState.Queued;
    public DateTime? StartedUtc { get; private set; }
    public DateTime? EndedUtc { get; private set; }

    /// <summary>The current input (file, folder or item), when the operation reports one.</summary>
    public string? Input { get; set; }

    /// <summary>Where the result goes (file or folder).</summary>
    public string? Output { get; set; }

    public long Done { get; private set; }
    public long Total { get; private set; }

    /// <summary>The operation's own description of its current step.</summary>
    public string Stage { get; private set; } = "";

    public string? Summary { get; private set; }

    public IReadOnlyList<string> Warnings { get { lock (_sync) return _warnings.ToList(); } }
    public IReadOnlyList<string> Errors { get { lock (_sync) return _errors.ToList(); } }

    [JsonIgnore]
    internal CancellationTokenSource? Cancellation { get; set; }

    public bool IsFinished => State is OperationState.Completed or OperationState.CompletedWithWarnings or OperationState.Failed or OperationState.Cancelled;

    /// <summary>Time spent running (waiting in the queue is not counted).</summary>
    public TimeSpan Elapsed => StartedUtc is { } s ? (EndedUtc ?? DateTime.UtcNow) - s : TimeSpan.Zero;

    /// <summary>Time spent waiting for a free slot.</summary>
    public TimeSpan Waited => ((StartedUtc ?? EndedUtc ?? DateTime.UtcNow) - CreatedUtc) is var w && w > TimeSpan.Zero ? w : TimeSpan.Zero;

    /// <summary>Done ÷ total, or null when the operation does not report a total.</summary>
    public double? Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : null;

    /// <summary>Items per second over the running time (measured), or null before there is enough to measure.</summary>
    public double? Throughput => Total > 0 && Done > 0 && Elapsed.TotalSeconds >= 1 ? Done / Elapsed.TotalSeconds : null;

    /// <summary>
    /// Remaining time from the measured rate, only while running, after at least 3 seconds and 2 %
    /// of the work, so that a first slow or fast step does not produce a misleading figure.
    /// </summary>
    public TimeSpan? EstimatedRemaining
    {
        get
        {
            if (State != OperationState.Running || Total <= 0 || Done <= 0 || Done >= Total) return null;
            var elapsed = Elapsed;
            if (elapsed.TotalSeconds < 3 || Done < Math.Max(1, Total / 50)) return null;
            double seconds = elapsed.TotalSeconds / Done * (Total - Done);
            return seconds is > 0 and < 7 * 24 * 3600 ? TimeSpan.FromSeconds(seconds) : null;
        }
    }

    internal void MarkRunning(DateTime? startedUtc = null)
    {
        State = OperationState.Running;
        StartedUtc = startedUtc ?? DateTime.UtcNow;
    }

    internal void Report(OperationProgress p)
    {
        if (IsFinished) return;
        Done = p.Done;
        Total = p.Total;
        if (!string.IsNullOrEmpty(p.Message)) Stage = p.Message;
    }

    internal void AddWarning(string text)
    {
        lock (_sync) if (_warnings.Count < 500) _warnings.Add(text);
    }

    internal void AddError(string text)
    {
        lock (_sync) if (_errors.Count < 500) _errors.Add(text);
    }

    internal void Finish(OperationState state, string? summary)
    {
        State = state == OperationState.Completed && Warnings.Count > 0 ? OperationState.CompletedWithWarnings : state;
        EndedUtc = DateTime.UtcNow;
        StartedUtc ??= EndedUtc;
        Summary = summary;
        Cancellation = null;
    }

    private string? FirstProblem()
    {
        var errors = Errors;
        if (errors.Count > 0) return errors[0];
        var warnings = Warnings;
        return warnings.Count > 0 ? warnings[0] : null;
    }

    /// <summary>A copy for the persisted history.</summary>
    internal OperationSummary ToSummary() => new()
    {
        Id = Id, ToolId = ToolId, ToolName = ToolName, Title = Title, State = State, StartedUtc = StartedUtc ?? CreatedUtc,
        Seconds = Math.Round(Elapsed.TotalSeconds, 3), WaitedSeconds = Math.Round(Waited.TotalSeconds, 3), Done = Done, Total = Total,
        Output = Output, Summary = Summary, Warnings = Warnings.Count, Errors = Errors.Count,
        FirstProblem = FirstProblem(),
    };
}

/// <summary>A finished operation as kept in the history file.</summary>
public sealed class OperationSummary
{
    public string Id { get; set; } = "";
    public string ToolId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string Title { get; set; } = "";
    public OperationState State { get; set; }
    public DateTime StartedUtc { get; set; }
    public double Seconds { get; set; }
    public double WaitedSeconds { get; set; }
    public long Done { get; set; }
    public long Total { get; set; }
    public string? Output { get; set; }
    public string? Summary { get; set; }
    public int Warnings { get; set; }
    public int Errors { get; set; }
    public string? FirstProblem { get; set; }
}

/// <summary>
/// Central registry of long operations: queued, running and recently finished. Operations are
/// added by the shared operation runner (so every tool reports the same way), the Diagnostics page
/// shows them and can cancel those that declare safe cancellation. Finished operations are kept in
/// a bounded history that survives restarts (operation summaries only: never file contents).
/// </summary>
public sealed class OperationTracker
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly object _sync = new();
    private readonly List<OperationRecord> _active = [];
    private readonly List<OperationSummary> _history = [];
    private readonly string? _historyFile;
    private readonly Func<int> _maxHistory;
    private int _counter;

    /// <param name="historyFile">JSON file of the finished operations (null = not persisted).</param>
    public OperationTracker(string? historyFile, Func<int>? maxHistory = null)
    {
        _historyFile = historyFile;
        _maxHistory = maxHistory ?? (() => 200);
        if (historyFile != null && File.Exists(historyFile))
        {
            try
            {
                _history.AddRange(JsonSerializer.Deserialize<List<OperationSummary>>(File.ReadAllText(historyFile), Json) ?? []);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("The operation history could not be read; it starts empty: " + ex.Message, source: "Diagnostics");
            }
        }
    }

    /// <summary>Raised (on the thread that made the change) when an operation was added, progressed or finished.</summary>
    public event Action<OperationRecord>? Changed;

    public IReadOnlyList<OperationRecord> Active { get { lock (_sync) return _active.ToList(); } }

    /// <summary>Finished operations, newest first.</summary>
    public IReadOnlyList<OperationSummary> History { get { lock (_sync) return _history.ToList(); } }

    public int RunningCount { get { lock (_sync) return _active.Count(o => o.State == OperationState.Running); } }
    public int QueuedCount { get { lock (_sync) return _active.Count(o => o.State == OperationState.Queued); } }

    /// <summary>Registers a new operation in the queued state.</summary>
    public OperationRecord Create(string toolId, string toolName, string title, bool canCancel, CancellationTokenSource? cancellation = null, string? output = null)
    {
        var record = new OperationRecord
        {
            Id = $"{DateTime.Now:HHmmss}-{Interlocked.Increment(ref _counter):D3}",
            ToolId = toolId, ToolName = toolName, Title = title, CanCancel = canCancel && cancellation != null, Output = output,
            Cancellation = cancellation,
        };
        lock (_sync) _active.Add(record);
        AppLog.Info($"Operation queued: {title}.", source: toolName);
        Changed?.Invoke(record);
        return record;
    }

    public void Start(OperationRecord op)
    {
        op.MarkRunning();
        AppLog.Info($"Operation started: {op.Title}{(op.Waited.TotalSeconds >= 1 ? $" (waited {op.Waited.TotalSeconds:0.0} s for a free slot)" : "")}.", source: op.ToolName);
        Changed?.Invoke(op);
    }

    public void Report(OperationRecord op, OperationProgress progress)
    {
        op.Report(progress);
        Changed?.Invoke(op);
    }

    public void Warn(OperationRecord op, string warning)
    {
        op.AddWarning(warning);
        Changed?.Invoke(op);
    }

    public void Error(OperationRecord op, string error)
    {
        op.AddError(error);
        Changed?.Invoke(op);
    }

    /// <summary>Ends an operation, logs its outcome with duration and counts, and moves it to the history.</summary>
    public void Finish(OperationRecord op, OperationState state, string? summary = null)
    {
        if (op.IsFinished) return;
        op.Finish(state, summary);
        string counts = op.Total > 0 ? L.F("OperationTracker.0N0Of1", op.Done, L.Count(op.Total, "Noun.item")) : "";
        string message = L.F("OperationTracker.Operation012000", Describe(op.State).ToLowerInvariant(), op.Title, op.Elapsed.TotalSeconds, counts, (summary != null ? " — " + summary : ""));
        if (op.State == OperationState.Failed) AppLog.Error(message, source: op.ToolName);
        else if (op.State is OperationState.Cancelled or OperationState.CompletedWithWarnings) AppLog.Warn(message, source: op.ToolName);
        else AppLog.Info(message, source: op.ToolName);
        lock (_sync)
        {
            _active.Remove(op);
            _history.Insert(0, op.ToSummary());
            int max = Math.Max(1, _maxHistory());
            if (_history.Count > max) _history.RemoveRange(max, _history.Count - max);
        }
        Save();
        Changed?.Invoke(op);
    }

    /// <summary>Requests cancellation of an operation that declares it can be cancelled safely.</summary>
    public bool RequestCancel(string id)
    {
        OperationRecord? op;
        lock (_sync) op = _active.FirstOrDefault(o => o.Id == id);
        if (op == null || !op.CanCancel || op.Cancellation == null) return false;
        try
        {
            op.Cancellation.Cancel();
            AppLog.Info($"Cancellation requested: {op.Title}.", source: op.ToolName);
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void ClearHistory()
    {
        lock (_sync) _history.Clear();
        Save();
    }

    private void Save()
    {
        if (_historyFile == null) return;
        try
        {
            List<OperationSummary> copy;
            lock (_sync) copy = _history.ToList();
            AtomicFile.WriteAllText(_historyFile, JsonSerializer.Serialize(copy, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("The operation history could not be saved: " + ex.Message, source: "Diagnostics");
        }
    }

    public static string Describe(OperationState state) => state switch
    {
        OperationState.Queued => "Queued",
        OperationState.Running => "Running",
        OperationState.Completed => "Completed",
        OperationState.CompletedWithWarnings => L.T("OperationTracker.CompletedWithWarnings"),
        OperationState.Failed => "Failed",
        _ => "Cancelled",
    };
}
