using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Library;

public enum QueueJobState { Pending, Running, Completed, Failed, Cancelled }

/// <summary>
/// One job of the <see cref="ProcessingQueue"/>: its state, progress, result or error. Properties
/// change on the worker thread; the application marshals <see cref="PropertyChanged"/> to its UI.
/// </summary>
public sealed class QueueJob : INotifyPropertyChanged
{
    internal QueueJob(string title, string category, Func<IProgress<OperationProgress>, CancellationToken, Task<string>> work)
    {
        Title = title;
        Category = category;
        Work = work;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; }
    /// <summary>What kind of job it is ("Content project", "Indexing", "Duplicates").</summary>
    public string Category { get; }
    internal Func<IProgress<OperationProgress>, CancellationToken, Task<string>> Work { get; }
    internal CancellationTokenSource Cancellation { get; } = new();

    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; private set; }
    public DateTime? FinishedUtc { get; private set; }

    private QueueJobState _state;
    public QueueJobState State { get => _state; private set => Set(ref _state, value); }

    private double _progress;
    /// <summary>0–1, or −1 while the total is unknown.</summary>
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    private string _message = "Waiting";
    public string Message { get => _message; private set => Set(ref _message, value); }

    private string? _result;
    /// <summary>What the job did (its summary), when completed.</summary>
    public string? Result { get => _result; private set => Set(ref _result, value); }

    private string? _error;
    public string? Error { get => _error; private set => Set(ref _error, value); }

    /// <summary>Technical details of a failure (exception type and stack), for "Error details".</summary>
    public string? ErrorDetails { get; private set; }

    public bool IsFinished => State is QueueJobState.Completed or QueueJobState.Failed or QueueJobState.Cancelled;
    public bool CanCancel => State is QueueJobState.Pending or QueueJobState.Running;
    public bool CanRetry => State is QueueJobState.Failed or QueueJobState.Cancelled;
    public TimeSpan? Duration => StartedUtc is { } s ? (FinishedUtc ?? DateTime.UtcNow) - s : null;

    internal void Start()
    {
        StartedUtc = DateTime.UtcNow;
        State = QueueJobState.Running;
        Message = L.T("ProcessingQueue.Starting");
        Raise(nameof(CanCancel));
    }

    internal void Report(OperationProgress p)
    {
        Progress = p.Total > 0 ? p.Fraction : -1;
        if (!string.IsNullOrEmpty(p.Message)) Message = p.Message;
    }

    internal void Finish(QueueJobState state, string message, string? result = null, Exception? error = null)
    {
        FinishedUtc = DateTime.UtcNow;
        Result = result;
        Error = error?.Message;
        ErrorDetails = error?.ToString();
        if (state == QueueJobState.Completed) Progress = 1;
        Message = message;
        State = state;
        Raise(nameof(IsFinished));
        Raise(nameof(CanCancel));
        Raise(nameof(CanRetry));
        Raise(nameof(Duration));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name!);
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// The central processing queue (8.8): content projects, indexing and other long jobs run here in the
/// background, at most <see cref="MaxConcurrency"/> at a time, with progress, cancellation, retry,
/// error details and a history of finished jobs (also written to a JSON-lines file when a path is set).
/// </summary>
public sealed class ProcessingQueue : IDisposable
{
    private readonly object _lock = new();
    private readonly List<QueueJob> _jobs = [];
    private readonly Func<int> _limit;
    private readonly string? _historyFile;
    private int _running;
    private bool _disposed;

    /// <param name="maxConcurrency">How many jobs may run at once (read each time a job could start, so a changed setting applies at once).</param>
    /// <param name="historyFile">A JSON-lines file the finished jobs are appended to, or null.</param>
    public ProcessingQueue(Func<int> maxConcurrency, string? historyFile = null)
    {
        _limit = maxConcurrency;
        _historyFile = historyFile;
    }

    public int MaxConcurrency => Math.Clamp(_limit(), 1, 16);

    /// <summary>Raised when a job was added, started, finished or removed (on the thread where it happened).</summary>
    public event Action<QueueJob>? Changed;

    /// <summary>Raised once when a job completed, failed or was cancelled (on the thread where it happened).</summary>
    public event Action<QueueJob>? Finished;

    public IReadOnlyList<QueueJob> Jobs
    {
        get { lock (_lock) return _jobs.ToList(); }
    }

    public int Pending => Jobs.Count(j => j.State == QueueJobState.Pending);
    public int Running => Jobs.Count(j => j.State == QueueJobState.Running);

    /// <summary>Adds a job; it starts as soon as a slot is free. The work returns a one-line summary of what it did.</summary>
    public QueueJob Enqueue(string title, string category, Func<IProgress<OperationProgress>, CancellationToken, Task<string>> work)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var job = new QueueJob(title, category, work);
        lock (_lock) _jobs.Add(job);
        Changed?.Invoke(job);
        Pump();
        return job;
    }

    /// <summary>Cancels a pending job at once, or asks a running job to stop.</summary>
    public void Cancel(QueueJob job)
    {
        if (job.State == QueueJobState.Pending)
        {
            bool cancelled = false;
            lock (_lock)
            {
                if (job.State == QueueJobState.Pending)
                {
                    job.Finish(QueueJobState.Cancelled, L.T("ProcessingQueue.CancelledBeforeItStarted"));
                    cancelled = true;
                }
            }
            if (cancelled)
            {
                Record(job);
                Changed?.Invoke(job);
                return;
            }
        }
        if (job.State == QueueJobState.Running) job.Cancellation.Cancel();
    }

    /// <summary>Adds a failed or cancelled job again (a new job with the same work).</summary>
    public QueueJob? Retry(QueueJob job) => job.CanRetry ? Enqueue(job.Title, job.Category, job.Work) : null;

    /// <summary>Removes finished jobs from the list (the history file keeps them).</summary>
    public int ClearFinished()
    {
        List<QueueJob> removed;
        lock (_lock)
        {
            removed = _jobs.Where(j => j.IsFinished).ToList();
            _jobs.RemoveAll(j => j.IsFinished);
        }
        foreach (var j in removed) Changed?.Invoke(j);
        return removed.Count;
    }

    /// <summary>Cancels everything (when the program closes).</summary>
    public void CancelAll()
    {
        foreach (var j in Jobs.Where(j => j.CanCancel)) Cancel(j);
    }

    private void Pump()
    {
        while (true)
        {
            QueueJob? next;
            lock (_lock)
            {
                if (_disposed || _running >= MaxConcurrency) return;
                next = _jobs.FirstOrDefault(j => j.State == QueueJobState.Pending);
                if (next == null) return;
                _running++;
                next.Start();
            }
            Changed?.Invoke(next);
            _ = RunAsync(next);
        }
    }

    private async Task RunAsync(QueueJob job)
    {
        var progress = new Progress(job);
        try
        {
            string summary = await Task.Run(() => job.Work(progress, job.Cancellation.Token), job.Cancellation.Token).ConfigureAwait(false);
            job.Finish(QueueJobState.Completed, L.T("ContentStudio.Finished"), summary);
        }
        catch (OperationCanceledException)
        {
            job.Finish(QueueJobState.Cancelled, L.T("ProcessingQueue.CancelledNothingPartialWasKept"));
        }
        catch (Exception ex)
        {
            job.Finish(QueueJobState.Failed, "Failed", error: ex);
        }
        finally
        {
            lock (_lock) _running--;
            Record(job);
            Changed?.Invoke(job);
            Pump();
        }
    }

    /// <summary>Progress that goes straight to the job (no synchronization context: the UI binds to the job's properties).</summary>
    private sealed class Progress(QueueJob job) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => job.Report(value);
    }

    private void Record(QueueJob job)
    {
        Finished?.Invoke(job);
        if (_historyFile == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_historyFile)!);
            string line = JsonSerializer.Serialize(new
            {
                job.Title, job.Category, State = job.State.ToString(), job.CreatedUtc, job.StartedUtc, job.FinishedUtc, job.Result, job.Error,
            });
            lock (_lock) File.AppendAllText(_historyFile, line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The latest entries of the history file (newest first).</summary>
    public IReadOnlyList<QueueHistoryEntry> History(int max = 200)
    {
        if (_historyFile == null || !File.Exists(_historyFile)) return [];
        try
        {
            return File.ReadLines(_historyFile).Reverse().Take(max).Select(l =>
            {
                try { return JsonSerializer.Deserialize<QueueHistoryEntry>(l); }
                catch (JsonException) { return null; }
            }).OfType<QueueHistoryEntry>().ToList();
        }
        catch (IOException) { return []; }
    }

    public void Dispose()
    {
        _disposed = true;
        CancelAll();
    }
}

/// <summary>A finished job as recorded in the history file.</summary>
public sealed record QueueHistoryEntry(string Title, string Category, string State, DateTime CreatedUtc, DateTime? StartedUtc, DateTime? FinishedUtc, string? Result, string? Error);
