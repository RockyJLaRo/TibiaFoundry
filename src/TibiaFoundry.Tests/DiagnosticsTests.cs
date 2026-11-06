using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Tools;

namespace TibiaFoundry.Tests;

/// <summary>Operation tracking, measurements, logging, the self-check and diagnostics reports.</summary>
[Collection(nameof(AppLogCollection))] // uses the process-wide log
public sealed class DiagnosticsTests(ITestOutputHelper output)
{
    private static readonly ToolManifest Cancellable = new()
    {
        Id = "test.cancellable", Name = "Cancellable", Description = "test", Type = ToolType.Utility, Version = "1.0",
        Capabilities = ToolCapabilities.Progress | ToolCapabilities.Cancellation, License = new("Test", "Test only"),
    };

    private sealed class Page(AppServices services) : PageViewModel(services, Cancellable)
    {
        public Task<bool> Run(string title, Func<IProgress<OperationProgress>, CancellationToken, Task> work, string? output = null) => RunAsync(title, work, output: output);
    }

    private static (AppServices Services, FakeDialogs Dialogs, string Dir) CreateServices()
    {
        string dir = TestData.NewTempDirectory("diagnostics");
        var settings = new AppSettings { OutputFolder = Path.Combine(dir, "Tibia_Foundry"), LoadDataAutomatically = false, CreatorName = "Diag Creator" };
        settings.Normalize();
        var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var dialogs = new FakeDialogs();
        services.Dialogs = dialogs;
        return (services, dialogs, dir);
    }

    // ------------------------------------------------------------------ operations

    [Fact]
    public void OperationsGoThroughTheirStatesAndAreKeptInTheHistory()
    {
        string file = Path.Combine(TestData.NewTempDirectory("ops"), "operations.json");
        var tracker = new OperationTracker(file, () => 3);
        var changes = 0;
        tracker.Changed += _ => changes++;
        using var cts = new CancellationTokenSource();
        var op = tracker.Create("tool", "Tool", "Exporting", canCancel: true, cts, output: @"C:\out");
        Assert.Equal(OperationState.Queued, op.State);
        Assert.Equal(1, tracker.QueuedCount);
        tracker.Start(op);
        Assert.Equal(OperationState.Running, op.State);
        Assert.Null(op.Fraction);                                                    // no total reported yet: no invented percentage
        tracker.Report(op, new OperationProgress(25, 100, "item 25"));
        Assert.Equal(0.25, op.Fraction);
        Assert.Equal("item 25", op.Stage);
        Assert.Null(op.EstimatedRemaining);                                          // too early to estimate reliably
        Assert.True(tracker.RequestCancel(op.Id));
        Assert.True(cts.IsCancellationRequested);
        tracker.Finish(op, OperationState.Cancelled, "Cancelled by the user.");
        Assert.Empty(tracker.Active);
        Assert.False(tracker.RequestCancel(op.Id));

        var notCancellable = tracker.Create("tool", "Tool", "Saving", canCancel: false, new CancellationTokenSource());
        Assert.False(notCancellable.CanCancel);
        Assert.False(tracker.RequestCancel(notCancellable.Id));                      // only operations that declare it
        tracker.Start(notCancellable);
        tracker.Warn(notCancellable, "one file skipped");
        tracker.Finish(notCancellable, OperationState.Completed);
        Assert.Equal(OperationState.CompletedWithWarnings, notCancellable.State);

        for (int i = 0; i < 5; i++)
        {
            var o = tracker.Create("tool", "Tool", "Run " + i, false);
            tracker.Start(o);
            tracker.Error(o, "boom");
            tracker.Finish(o, OperationState.Failed, "boom");
        }
        Assert.Equal(3, tracker.History.Count);                                      // bounded
        Assert.Equal("Run 4", tracker.History[0].Title);                             // newest first
        Assert.True(changes > 10);
        var reloaded = new OperationTracker(file);
        Assert.Equal(tracker.History.Select(h => h.Id), reloaded.History.Select(h => h.Id));   // survives a restart
        Assert.Equal(OperationState.Failed, reloaded.History[0].State);
        Assert.Equal("boom", reloaded.History[0].FirstProblem);
    }

    [Fact]
    public void RemainingTimeIsOnlyEstimatedFromMeasuredProgress()
    {
        var tracker = new OperationTracker(null);
        var op = tracker.Create("t", "T", "Long", false);
        op.MarkRunning(DateTime.UtcNow.AddSeconds(-10));
        tracker.Report(op, new OperationProgress(50, 100, ""));
        var remaining = op.EstimatedRemaining!.Value;
        Assert.InRange(remaining.TotalSeconds, 9, 11);                               // 10 s for half → about 10 s left
        Assert.InRange(op.Throughput!.Value, 4.5, 5.5);
        tracker.Report(op, new OperationProgress(1, 1000, ""));
        Assert.Null(op.EstimatedRemaining);                                          // less than 2 % done: no estimate
        tracker.Report(op, new OperationProgress(5, 0, ""));
        Assert.Null(op.EstimatedRemaining);                                          // no total: no estimate
    }

    [Fact(Timeout = 120_000)]
    public async Task RunningOperationsReportProgressCanBeCancelledCleanUpAndKeepTheWindowResponsive()
    {
        await Sta.Run(async () =>
        {
            var (services, _, _) = CreateServices();
            using var _ = services;
            var page = new Page(services);
            string? leaseFolder = null;
            bool dispatcherRan = false;
            var started = new TaskCompletionSource();
            var run = page.Run("Long work", async (progress, ct) =>
            {
                using var lease = services.Temp.Begin("Long work");
                leaseFolder = lease.Folder;
                File.WriteAllText(Path.Combine(lease.Folder, "partial.tmp"), "half done");
                progress.Report(new OperationProgress(1, 4, "step 1"));
                started.SetResult();
                await Task.Run(() => Task.Delay(Timeout.Infinite, ct), ct);
            }, output: "somewhere");
            await started.Task;
            // The dispatcher keeps working while the operation runs (the window stays responsive).
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => dispatcherRan = true);
            Assert.True(dispatcherRan);
            await Sta.Until(() => services.Jobs.Active.Count == 1 && services.Jobs.Active[0].Done == 1, "progress");
            var job = services.Jobs.Active[0];
            Assert.Equal(OperationState.Running, job.State);
            Assert.True(job.CanCancel);
            Assert.True(services.Status.CanCancel);
            Assert.Equal(0.25, job.Fraction);
            Assert.True(Directory.Exists(leaseFolder));

            Assert.True(services.Jobs.RequestCancel(job.Id));
            Assert.False(await run);
            Assert.Equal(OperationState.Cancelled, services.Jobs.History[0].State);
            Assert.False(Directory.Exists(leaseFolder));                             // temporary files of the cancelled run are gone
            Assert.Equal(StatusKind.Warning, services.Status.Kind);
            Assert.Contains("cancelled", services.Status.Message);
        });
    }

    [Fact(Timeout = 120_000)]
    public async Task ConcurrentOperationsQueueByTheLimitAndAreShownAsQueued()
    {
        await Sta.Run(async () =>
        {
            var (services, _, _) = CreateServices();
            using var _ = services;
            services.Settings.Performance.MaxConcurrentOperations = 1;
            var a = new Page(services);
            var b = new Page(services);
            var release = new TaskCompletionSource();
            var first = a.Run("First", async (_, ct) => await release.Task.WaitAsync(ct));
            var second = b.Run("Second", (_, _) => Task.CompletedTask);
            await Sta.Until(() => services.Jobs.Active.Count == 2, "two operations");
            Assert.Equal(OperationState.Running, services.Jobs.Active.Single(j => j.Title == "First").State);
            Assert.Equal(OperationState.Queued, services.Jobs.Active.Single(j => j.Title == "Second").State);
            release.SetResult();
            Assert.True(await first);
            Assert.True(await second);
            Assert.All(services.Jobs.History, h => Assert.Equal(OperationState.Completed, h.State));
            Assert.True(services.Jobs.History.Single(h => h.Title == "Second").WaitedSeconds >= 0);
        });
    }

    // ------------------------------------------------------------------ logging

    [Fact]
    public void LogLinesCarryLevelSourceAndOperationAndCanBeFiltered()
    {
        string dir = TestData.NewTempDirectory("log");
        try
        {
            AppLog.Initialize(dir);
            AppLog.MinimumLevel = LogLevel.Debug;
            AppLog.Info("plain start qx1");
            using (AppLog.BeginScope("RCC Editor", "120000-qx1"))
            {
                AppLog.Warn("file skipped qx1");
                AppLog.Error("write failed qx1", new IOException("disk full"));
            }
            AppLog.Critical("process stops qx1");
            AppLog.Shutdown();
            var files = AppLog.Files().Select(f => f.FullName).ToList();
            var entries = LogReader.Read(files);
            Assert.Contains(entries, e => e.Source == "App" && e.Message == "plain start qx1" && e.Level == LogLevel.Info);
            var failed = Assert.Single(entries, e => e.Message == "write failed qx1");
            Assert.Equal(("RCC Editor", "120000-qx1", LogLevel.Error), (failed.Source, failed.Operation, failed.Level));
            Assert.Contains("disk full", failed.Details);
            Assert.Contains(entries, e => e.Level == LogLevel.Critical && e.Message == "process stops qx1");
            Assert.Equal(2, LogReader.Read(files, new LogFilter { Operation = "120000-qx1" }).Count);
            Assert.Equal(["write failed qx1"], LogReader.Read(files, new LogFilter { MinimumLevel = LogLevel.Error, Source = "rcc editor" }).Select(e => e.Message));
            Assert.Single(LogReader.Read(files, new LogFilter { Text = "SKIPPED QX1" }));
            Assert.Empty(LogReader.Read(files, new LogFilter { From = DateTime.Now.AddHours(1) }));
            // Lines of earlier versions are read too.
            var old = LogReader.ParseLine("2026-10-12 09:15:01.123 [Warning] [T  7] Settings file was unreadable; defaults used.")!;
            Assert.Equal(("App", LogLevel.Warning), (old.Source, old.Level));
        }
        finally
        {
            AppLog.Shutdown();
        }
    }

    [Fact]
    public void SecretsNeverReachTheLog()
    {
        string dir = TestData.NewTempDirectory("log");
        try
        {
            AppLog.Initialize(dir);
            AppLog.Info("login password=hunter2 then token: abc123def456 and https://example.invalid/x?api_key=SECRETVALUE&page=2");
            AppLog.Warn("request failed", new InvalidOperationException("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature"));
            AppLog.Shutdown();
            string text = File.ReadAllText(AppLog.LogFilePath!);
            foreach (string secret in new[] { "hunter2", "abc123def456", "SECRETVALUE", "eyJhbGciOiJIUzI1NiJ9" }) Assert.DoesNotContain(secret, text);
            Assert.Contains("page=2", text);                                         // ordinary text is kept
            Assert.Contains(LogRedactor.Mask, text);
            Assert.Equal("First pass: 12 items, skippedSpritesCountTotal=3", LogRedactor.Redact("First pass: 12 items, skippedSpritesCountTotal=3"));
        }
        finally
        {
            AppLog.Shutdown();
        }
    }

    [Fact]
    public void RepeatedMessagesAreWrittenOnceWithACount()
    {
        string dir = TestData.NewTempDirectory("log");
        try
        {
            AppLog.Initialize(dir);
            for (int i = 0; i < 500; i++) AppLog.Error("the same rendering error");
            AppLog.Info("something else");
            AppLog.Shutdown();
            var lines = File.ReadAllLines(AppLog.LogFilePath!);
            Assert.Single(lines, l => l.EndsWith("the same rendering error", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("repeated 499 more times"));
        }
        finally
        {
            AppLog.Shutdown();
        }
    }

    [Fact]
    public void OldAndExcessLogFilesAreRemovedButTodaysIsKept()
    {
        string dir = TestData.NewTempDirectory("log");
        long previous = AppLog.MaxTotalBytes;
        try
        {
            string Old(int daysAgo, int kb)
            {
                string f = Path.Combine(dir, $"{AppIdentity.Id}-{DateTime.Now.AddDays(-daysAgo):yyyyMMdd}.log");
                File.WriteAllText(f, new string('x', kb * 1024));
                File.SetLastWriteTime(f, DateTime.Now.AddDays(-daysAgo));
                return f;
            }
            string ancient = Old(40, 1);
            string older = Old(5, 300);
            string recent = Old(1, 300);
            string unrelated = Path.Combine(dir, "notes.txt");
            File.WriteAllText(unrelated, "mine");
            AppLog.MaxTotalBytes = 400 * 1024;
            AppLog.Initialize(dir, retentionDays: 14);
            AppLog.Info("today");
            AppLog.Shutdown();
            Assert.False(File.Exists(ancient));                                       // past the retention
            Assert.False(File.Exists(older));                                         // oldest removed until the total fits
            Assert.True(File.Exists(recent));
            Assert.True(File.Exists(AppLog.LogFilePath));                             // today's log is never removed
            Assert.True(File.Exists(unrelated));                                      // only log files are touched
        }
        finally
        {
            AppLog.MaxTotalBytes = previous;
            AppLog.Shutdown();
        }
    }

    // ------------------------------------------------------------------ sanitizing and reports

    [Fact]
    public void ReportTextIsSanitizedByDefault()
    {
        var sanitizer = new DiagnosticsSanitizer(false,
            [(@"C:\Users\Dana", "%USERPROFILE%"), (@"C:\Users\Dana\OneDrive\Documents", "%DOCUMENTS%")], "Dana", "DANA-PC");
        string text = @"Saved C:\Users\Dana\OneDrive\Documents\Tibia_Foundry\GIF_Exports\x.gif; client D:\Games\Tibia\packages\Tibia\assets\catalog-content.json; " +
                      @"user Dana on DANA-PC, mail dana@example.com, password=hunter2, \\server\share\private\file.txt";
        string clean = sanitizer.Sanitize(text);
        output.WriteLine(clean);
        Assert.Contains(@"%DOCUMENTS%\Tibia_Foundry\GIF_Exports\x.gif", clean);
        Assert.DoesNotContain("Dana", clean);
        Assert.DoesNotContain("DANA-PC", clean);
        Assert.DoesNotContain("dana@example.com", clean);
        Assert.DoesNotContain("hunter2", clean);
        Assert.DoesNotContain(@"Games\Tibia\packages", clean);                       // other paths keep only their end
        Assert.Contains(@"assets\catalog-content.json", clean);
        Assert.DoesNotContain("server", clean);

        string json = JsonSerializer.Serialize(new { folder = @"C:\Users\Dana\Desktop\a.png", note = "Dana was here" });
        string cleanJson = sanitizer.SanitizeJson(json);
        Assert.DoesNotContain("Dana", cleanJson);                                    // escaped paths inside JSON too
        Assert.Contains("%USERPROFILE%", JsonDocument.Parse(cleanJson).RootElement.GetProperty("folder").GetString());

        var keep = new DiagnosticsSanitizer(true, [(@"C:\Users\Dana", "%USERPROFILE%")], "Dana", "DANA-PC");
        string kept = keep.Sanitize(text);
        Assert.Contains(@"C:\Users\Dana\OneDrive", kept);                             // opt-in keeps paths…
        Assert.DoesNotContain("hunter2", kept);                                       // …but never secrets
    }

    [Fact(Timeout = 180_000)]
    public async Task TheDiagnosticsReportContainsWhatThePreviewShowedAndNothingPersonal()
    {
        await Sta.Run(async () =>
        {
            var (services, dialogs, dir) = CreateServices();
            using var _ = services;
            string logs = Path.Combine(dir, "logs");
            AppLog.Initialize(logs);
            try
            {
                AppLog.Info($"Loaded {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\\Documents\\x.dat for {Environment.UserName} on {Environment.MachineName}; api_key=PLANTEDSECRET");
                services.Settings.ToolState["Scripts.checksums.folder"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
                var main = new MainViewModel(services);
                var vm = main.Pages.OfType<DiagnosticsViewModel>().Single();
                await vm.RunSelfCheckAsync();
                await vm.PreviewReportAsync();
                var preview = vm.PreviewEntries.ToList();
                Assert.Equal(["summary.txt", "system.json", "settings.json", "tools.json", "selfcheck.json", "operations.json", "metrics.json"],
                    preview.Select(e => e.Name).Where(n => !n.StartsWith("logs/", StringComparison.Ordinal)));
                Assert.Contains(preview, e => e.Name.StartsWith("logs/", StringComparison.Ordinal));
                string all = string.Concat(preview.Select(e => Encoding.UTF8.GetString(e.Content)));
                Assert.DoesNotContain("PLANTEDSECRET", all);
                Assert.DoesNotContain("Diag Creator", all);                         // the creator name is left out
                Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), all, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotMatch($@"(?i)(?<![A-Za-z0-9]){System.Text.RegularExpressions.Regex.Escape(Environment.UserName)}(?![A-Za-z0-9])", all);
                Assert.DoesNotMatch($@"(?i)(?<![A-Za-z0-9]){System.Text.RegularExpressions.Regex.Escape(Environment.MachineName)}(?![A-Za-z0-9])", all);
                Assert.Contains("Nothing was uploaded or sent", Encoding.UTF8.GetString(preview[0].Content));

                // Opting in to full paths needs a confirmation (declined here) and invalidates nothing then.
                dialogs.ConfirmAnswer = false;
                vm.IncludeFullPaths = true;
                Assert.False(vm.IncludeFullPaths);
                Assert.True(vm.HasPreview);

                // Saved into the Reports folder, exactly the previewed files plus the manifest.
                vm.SaveReportCommand.Execute(null);
                string zip = vm.LastReport!;
                Assert.Equal(Path.Combine(services.Settings.OutputFolder, "Reports"), Path.GetDirectoryName(zip));
                Assert.Equal(zip, services.Status.Link!.Path);
                using var archive = ZipFile.OpenRead(zip);
                Assert.Equal(preview.Select(e => e.Name).Append("manifest.json"), archive.Entries.Select(e => e.FullName));
                foreach (var entry in preview)
                {
                    using var s = archive.GetEntry(entry.Name)!.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    Assert.Equal(entry.Content, ms.ToArray());
                }
                using var manifestStream = archive.GetEntry("manifest.json")!.Open();
                var manifest = JsonDocument.Parse(manifestStream).RootElement;
                Assert.True(manifest.GetProperty("sanitized").GetBoolean());
                Assert.False(manifest.GetProperty("transmitted").GetBoolean());
                Assert.Equal(preview.Count, manifest.GetProperty("files").GetArrayLength());
                Assert.Equal(preview[1].Sha256, manifest.GetProperty("files")[1].GetProperty("sha256").GetString());

                // Changing an option after the preview requires a new preview before saving.
                vm.ReportCategories.Single(c => c.Category.Id == "logs").IsIncluded = false;
                Assert.False(vm.HasPreview);
                Assert.False(vm.SaveReportCommand.CanExecute(null));
            }
            finally
            {
                AppLog.Shutdown();
            }
        });
    }

    [Fact]
    public void DiagnosticsCollectionHasNoNetworkAccess()
    {
        // Reports are only saved locally: the code that builds them uses no network API.
        string src = Path.Combine(AppContext.BaseDirectory);
        string? root = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null && root == null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "src", "TibiaFoundry.App"))) root = Path.Combine(d.FullName, "src");
        Assert.SkipWhen(root == null, "Sources not found.");
        foreach (string file in new[] { @"TibiaFoundry.App\Services\DiagnosticsCollector.cs", @"TibiaFoundry.Core\Diagnostics\DiagnosticsReport.cs", @"TibiaFoundry.App\ViewModels\DiagnosticsViewModel.cs", @"TibiaFoundry.App\Services\HealthCheck.cs" })
        {
            string text = File.ReadAllText(Path.Combine(root!, file));
            foreach (string api in new[] { "HttpClient", "Services.Http", "WebRequest", "Socket", "HttpFetcher", "Upload" })
                Assert.DoesNotContain(api, text);
        }
    }

    // ------------------------------------------------------------------ self-check and measurements

    [Fact(Timeout = 120_000)]
    public async Task TheSelfCheckFindsFolderProblemsAndRepairsOnlyWhenAsked()
    {
        await Sta.Run(async () =>
        {
            var (services, dialogs, dir) = CreateServices();
            using var _ = services;
            string blocker = Path.Combine(dir, "not-a-folder");
            File.WriteAllText(blocker, "");
            services.Settings.SetExplicitFolder(OutputKind.Reports, blocker);
            var results = HealthCheck.Run(services);
            var reports = results.Single(r => r.Area == "Folders" && r.Check == "Reports");
            Assert.Equal(HealthSeverity.Error, reports.Severity);
            Assert.Equal(HealthFix.UseDefaultFolder, reports.Fix);
            var gif = results.Single(r => r.Area == "Folders" && r.Check == "GIF exports");
            Assert.Equal(HealthSeverity.Info, gif.Severity);                            // missing default: created when needed
            Assert.Equal(HealthFix.CreateFolder, gif.Fix);
            Assert.All(results.Where(r => r.Area == "Subsystems"), r => Assert.Equal(HealthSeverity.Ok, r.Severity));
            Assert.False(Directory.Exists(Path.Combine(services.Settings.OutputFolder, "GIF_Exports")));   // checking repairs nothing
            services.PersistSettings = false;                                           // the user turned saving off: worth a warning…
            Assert.Contains(HealthCheck.Run(services), r => r.Check == "Saving settings" && r.Severity == HealthSeverity.Warning);
            Assert.DoesNotContain(HealthCheck.Run(services, readOnlyRun: true), r => r.Check == "Saving settings");   // …the self-test's own read-only run is not
            services.PersistSettings = true;

            var main = new MainViewModel(services);
            var vm = main.Pages.OfType<DiagnosticsViewModel>().Single();
            await vm.RunSelfCheckAsync();
            var fixGif = vm.HealthResults.Single(r => r.Check == "GIF exports");
            dialogs.ConfirmAnswer = false;
            fixGif.FixCommand.Execute(null);
            Assert.False(Directory.Exists(Path.Combine(services.Settings.OutputFolder, "GIF_Exports")));   // declined: nothing done
            dialogs.ConfirmAnswer = true;
            fixGif.FixCommand.Execute(null);
            Assert.True(Directory.Exists(Path.Combine(services.Settings.OutputFolder, "GIF_Exports")));
            vm.HealthResults.First(r => r.Check == "Reports").FixCommand.Execute(null);
            Assert.Null(services.Settings.ExplicitFolder(OutputKind.Reports));
            Assert.True(File.Exists(blocker));                                          // nothing deleted
        });
    }

    [Fact(Timeout = 120_000)]
    public async Task MeasurementsAreTakenOnlyWhileThePageIsOpenAndUnavailableValuesAreLabeled()
    {
        await Sta.Run(async () =>
        {
            var monitor = new ResourceMonitor();
            var first = monitor.Sample();
            Assert.Null(first.CpuPercent);                                              // nothing to compare with yet
            Thread.SpinWait(5_000_000);
            var second = monitor.Sample();
            Assert.InRange(second.CpuPercent!.Value, 0, 100);
            Assert.True(second.WorkingSet > 0 && second.Threads > 0);
            char? unused = Enumerable.Range('D', 23).Select(c => (char)c).Cast<char?>().FirstOrDefault(c => !Directory.Exists($"{c}:\\"));
            if (unused is { } letter)
                Assert.NotNull(ResourceMonitor.Disks([("Somewhere", $@"{letter}:\x")])[0].Problem);   // reported, not invented

            var (services, _, _) = CreateServices();
            using var _ = services;
            var main = new MainViewModel(services);
            var vm = main.Pages.OfType<DiagnosticsViewModel>().Single();
            Assert.False(vm.IsSampling);
            main.SelectedPage = vm;
            await Sta.Until(() => vm.IsSampling && vm.Metrics.Count > 0, "measurements");
            Assert.Contains(vm.Metrics, m => m.Kind == "not available" && m.Name.StartsWith("Per-operation", StringComparison.Ordinal));
            Assert.All(vm.Metrics.Where(m => m.Kind == "measured"), m => Assert.False(string.IsNullOrWhiteSpace(m.Value)));
            main.SelectedPage = main.Pages.OfType<AboutViewModel>().Single();
            Assert.False(vm.IsSampling);                                                // stops when the page is left
            int samples = vm.SampleCount;
            await Task.Delay(2500);
            Assert.Equal(samples, vm.SampleCount);
            main.SelectedPage = vm;
            vm.MonitoringEnabled = false;
            Assert.False(vm.IsSampling);                                                // can be turned off
            Assert.Empty(vm.Metrics);
        });
    }
}
