using TibiaFoundry.Core.Common;
using System.Diagnostics;
using System.Text;

namespace TibiaFoundry.Core.Diagnostics;

/// <summary>Severity of a log entry. <see cref="Critical"/> is used for errors that stop the program or a whole subsystem.</summary>
public enum LogLevel { Debug, Info, Warning, Error, Critical }

/// <summary>
/// The shared, thread-safe file logger used by every tool and script.
/// <list type="bullet">
/// <item>One file per day (<c>TibiaFoundry-yyyyMMdd.log</c>); a session that runs past midnight continues in the next day's file.</item>
/// <item>Each line carries a timestamp, the level, the thread, the source (tool or component) and,
/// inside an operation, the operation's id, so the Diagnostics page can filter by tool and operation.</item>
/// <item>Bounded: a day's file stops growing at <see cref="MaxFileBytes"/>; at start-up files older
/// than <see cref="RetentionDays"/> are removed, then the oldest until all files together are below
/// <see cref="MaxTotalBytes"/>.</item>
/// <item>The same message repeated within a few seconds is written once, followed by a count.</item>
/// <item>Passwords, tokens and keys that appear in messages are masked (<see cref="LogRedactor"/>).</item>
/// <item>Lines written before <see cref="Initialize"/> (while the settings that choose the log folder
/// are read) are kept in memory and written when the log folder is known.</item>
/// </list>
/// Logging never throws.
/// </summary>
public static class AppLog
{
    public const long DefaultMaxFileBytes = 20L * 1024 * 1024;
    public const long DefaultMaxTotalBytes = 200L * 1024 * 1024;
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(10);

    private static readonly object Sync = new();
    private static StreamWriter? _writer;
    private static DateTime _fileDate;
    private static bool _capped;
    private static string? _lastKey;
    private static DateTime _lastAt;
    private static int _repeats;
    private static readonly AsyncLocal<string?> CurrentSource = new();
    private static readonly AsyncLocal<string?> CurrentOperationId = new();
    private static readonly LinkedList<LogEntry> Problems = new();
    private static readonly List<string> Pending = [];

    public static string? LogFilePath { get; private set; }

    /// <summary>Folder of the log files (null before <see cref="Initialize"/>).</summary>
    public static string? LogDirectory { get; private set; }

    /// <summary>Lowest level written to the file (Settings → Notifications and logging).</summary>
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    /// <summary>Size at which a day's file stops growing.</summary>
    public static long MaxFileBytes { get; set; } = DefaultMaxFileBytes;

    /// <summary>All log files together are kept below this size (oldest removed first, at start-up).</summary>
    public static long MaxTotalBytes { get; set; } = DefaultMaxTotalBytes;

    /// <summary>Days log files are kept.</summary>
    public static int RetentionDays { get; private set; } = 14;

    /// <summary>Raised for warnings and errors (used by the UI status bar).</summary>
    public static event Action<LogLevel, string>? MessageLogged;

    /// <summary>
    /// Marks the code that follows (on this thread and the tasks it starts) as part of an
    /// operation: log lines get its source and id. Dispose to end it.
    /// </summary>
    public static IDisposable BeginScope(string source, string? operationId = null)
    {
        var previous = (CurrentSource.Value, CurrentOperationId.Value);
        CurrentSource.Value = source;
        CurrentOperationId.Value = operationId;
        return new Scope(previous);
    }

    private sealed class Scope((string? Source, string? Operation) previous) : IDisposable
    {
        public void Dispose()
        {
            CurrentSource.Value = previous.Source;
            CurrentOperationId.Value = previous.Operation;
        }
    }

    public static void Initialize(string directory, int retentionDays = 14)
    {
        lock (Sync)
        {
            try
            {
                FlushRepeats();
                RetentionDays = Math.Max(1, retentionDays);
                Directory.CreateDirectory(directory);
                LogDirectory = directory;
                ApplyRetention(directory);
                OpenFile(DateTime.Now);
                foreach (string line in Pending) WriteLine(line);
                Pending.Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Log initialization failed: " + ex.Message);
                _writer = null;
            }
        }
    }

    /// <summary>Removes files older than the retention, then the oldest until the total fits <see cref="MaxTotalBytes"/> (today's file is kept).</summary>
    private static void ApplyRetention(string directory)
    {
        var files = new DirectoryInfo(directory).GetFiles(AppIdentity.Id + "-*.log").OrderBy(f => f.LastWriteTimeUtc).ToList();
        foreach (var old in files.Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-RetentionDays)).ToList())
        {
            TryDelete(old);
            files.Remove(old);
        }
        string today = $"{AppIdentity.Id}-{DateTime.Now:yyyyMMdd}.log";
        long total = files.Sum(f => f.Length);
        foreach (var f in files.Where(f => !f.Name.Equals(today, StringComparison.OrdinalIgnoreCase)))
        {
            if (total <= MaxTotalBytes) break;
            total -= f.Length;
            TryDelete(f);
        }
    }

    private static void TryDelete(FileInfo file)
    {
        try { file.Delete(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine("Old log not removed: " + ex.Message); }
    }

    private static void OpenFile(DateTime now)
    {
        if (LogDirectory == null) return;
        _writer?.Dispose();
        _fileDate = now.Date;
        LogFilePath = Path.Combine(LogDirectory, $"{AppIdentity.Id}-{now:yyyyMMdd}.log");
        _writer = new StreamWriter(new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
        _capped = false;
    }

    public static void Verbose(string message, string? source = null) => Write(LogLevel.Debug, message, null, source);
    public static void Info(string message, string? source = null) => Write(LogLevel.Info, message, null, source);
    public static void Warn(string message, Exception? ex = null, string? source = null) => Write(LogLevel.Warning, message, ex, source);
    public static void Error(string message, Exception? ex = null, string? source = null) => Write(LogLevel.Error, message, ex, source);
    public static void Critical(string message, Exception? ex = null, string? source = null) => Write(LogLevel.Critical, message, ex, source);

    public static void Write(LogLevel level, string message, Exception? ex, string? source = null)
    {
        var now = DateTime.Now;
        string src = Clean(source ?? CurrentSource.Value ?? "App");
        string? op = CurrentOperationId.Value;
        message = LogRedactor.Redact(message);
        string line = Format(now, level, Environment.CurrentManagedThreadId, src, op, message);
        if (ex != null) line += Environment.NewLine + "    " + LogRedactor.Redact(ex.ToString()).Replace(Environment.NewLine, Environment.NewLine + "    ");
        Debug.WriteLine(line);
        if (level >= LogLevel.Warning) Remember(new LogEntry(now, level, src, op, message, ex?.GetType().Name));
        if (level >= MinimumLevel)
        {
            lock (Sync)
            {
                try
                {
                    if (LogDirectory == null)
                    {
                        if (Pending.Count < 1000) Pending.Add(line);
                        return;
                    }
                    if (_writer != null && now.Date != _fileDate) { FlushRepeats(); OpenFile(now); }
                    string key = $"{level}|{src}|{message}";
                    if (ex == null && key == _lastKey && now - _lastAt < RepeatWindow)
                    {
                        _repeats++;
                        _lastAt = now;
                    }
                    else
                    {
                        FlushRepeats();
                        WriteLine(line);
                        _lastKey = ex == null ? key : null;
                        _lastAt = now;
                    }
                }
                catch (IOException) { /* logging must never break the app */ }
                catch (ObjectDisposedException) { }
            }
        }
        if (level >= LogLevel.Warning)
        {
            try { MessageLogged?.Invoke(level, message); }
            catch (Exception handlerEx) { Debug.WriteLine("Log handler failed: " + handlerEx.Message); }
        }
    }

    /// <summary>The line format; <see cref="LogReader"/> parses it (and the shorter format of earlier versions).</summary>
    public static string Format(DateTime time, LogLevel level, int thread, string source, string? operation, string message) =>
        $"{time:yyyy-MM-dd HH:mm:ss.fff} [{level,-8}] [T{thread,3}] [{source}]{(operation != null ? $" [op:{operation}]" : "")} {message}";

    private static string Clean(string source) => new string(source.Where(c => c is not ('[' or ']' or '\r' or '\n')).ToArray()).Trim() is { Length: > 0 } s ? s : "App";

    private static void WriteLine(string line)
    {
        if (_writer == null || _capped) return;
        if (_writer.BaseStream.Length < MaxFileBytes) _writer.WriteLine(line);
        else
        {
            _writer.WriteLine(Format(DateTime.Now, LogLevel.Warning, Environment.CurrentManagedThreadId, "Log", null, "Log file size limit reached; further messages are not written today."));
            _capped = true;
        }
    }

    private static void FlushRepeats()
    {
        if (_repeats > 0 && _lastKey != null)
            WriteLine(Format(_lastAt, LogLevel.Info, Environment.CurrentManagedThreadId, "Log", null, $"The previous message was repeated {Plural.Count(_repeats, "more time")}."));
        _repeats = 0;
        _lastKey = null;
    }

    private static void Remember(LogEntry entry)
    {
        lock (Problems)
        {
            Problems.AddLast(entry);
            while (Problems.Count > 200) Problems.RemoveFirst();
        }
    }

    /// <summary>The most recent warnings and errors of this session (at most 200), oldest first.</summary>
    public static IReadOnlyList<LogEntry> RecentProblems()
    {
        lock (Problems) return Problems.ToList();
    }

    /// <summary>Log files in the log folder, newest first.</summary>
    public static IReadOnlyList<FileInfo> Files()
    {
        if (LogDirectory == null || !Directory.Exists(LogDirectory)) return [];
        return new DirectoryInfo(LogDirectory).GetFiles(AppIdentity.Id + "-*.log").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
    }

    public static void Shutdown()
    {
        lock (Sync)
        {
            try { FlushRepeats(); } catch (IOException) { }
            _writer?.Dispose();
            _writer = null;
        }
    }
}
