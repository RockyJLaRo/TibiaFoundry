using TibiaFoundry.Core.Diagnostics;

namespace TibiaFoundry.Tests;

[CollectionDefinition(nameof(AppLogCollection), DisableParallelization = true)]
public sealed class AppLogCollection;

/// <summary>The logger is process-global, so these tests never run in parallel with others.</summary>
[Collection(nameof(AppLogCollection))]
public sealed class AppLogTests
{
    [Fact]
    public void WritesLevelsAndExceptions()
    {
        string dir = TestData.NewTempDirectory();
        try
        {
            AppLog.Initialize(dir);
            AppLog.Info("hello info");
            AppLog.Warn("careful", new InvalidOperationException("inner problem"));
            AppLog.Shutdown();

            string text = File.ReadAllText(AppLog.LogFilePath!);
            Assert.Contains("[Info    ]", text);
            Assert.Contains("[App] hello info", text);
            Assert.Contains("[Warning ]", text);
            Assert.Contains("System.InvalidOperationException: inner problem", text);
        }
        finally
        {
            AppLog.Shutdown();
        }
    }

    [Fact]
    public void WarningsAndErrorsAreRaisedForTheStatusBar()
    {
        var seen = new List<(LogLevel, string)>();
        void Handler(LogLevel level, string message) { lock (seen) seen.Add((level, message)); }
        AppLog.MessageLogged += Handler;
        try
        {
            AppLog.Info("not raised");
            AppLog.Error("raised error");
        }
        finally
        {
            AppLog.MessageLogged -= Handler;
        }
        lock (seen)
        {
            Assert.DoesNotContain(seen, s => s.Item2 == "not raised");
            Assert.Contains((LogLevel.Error, "raised error"), seen);
        }
    }

    /// <summary>Regression: a repeating UI error once produced a 177 MB log in minutes.</summary>
    [Fact]
    public void LogFileStopsGrowingAtTheSizeLimit()
    {
        string dir = TestData.NewTempDirectory();
        try
        {
            AppLog.Initialize(dir);
            string chunk = new('x', 64 * 1024);
            long writes = AppLog.MaxFileBytes / chunk.Length + 50;
            // Distinct messages: an identical message repeated is written only once (see the next test).
            for (long i = 0; i < writes; i++) AppLog.Error(chunk + i);
            AppLog.Shutdown();

            long length = new FileInfo(AppLog.LogFilePath!).Length;
            Assert.InRange(length, AppLog.MaxFileBytes, AppLog.MaxFileBytes + 2 * chunk.Length);
            string tail = File.ReadLines(AppLog.LogFilePath!).Last();
            Assert.Contains("Log file size limit reached", tail);
        }
        finally
        {
            AppLog.Shutdown();
        }
    }
}
