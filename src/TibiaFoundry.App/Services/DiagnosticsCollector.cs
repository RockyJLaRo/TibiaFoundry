using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TibiaFoundry.App.Themes;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Tools;

namespace TibiaFoundry.App.Services;

/// <summary>What to put into a diagnostics report.</summary>
/// <param name="Categories">Ids of <see cref="DiagnosticsReport.Categories"/> to include (the required ones are always included).</param>
/// <param name="IncludeFullPaths">Opt-in: keep folder paths, the user name and the computer name (secrets are still masked).</param>
public sealed record ReportOptions(IReadOnlySet<string> Categories, bool IncludeFullPaths, int LogDays);

/// <summary>
/// Collects the contents of a diagnostics report. Everything is sanitized here, before it is shown
/// in the preview, so the preview is exactly what the saved report contains. File contents, exported
/// graphics, the creator name, environment variables and command-line values are never included.
/// </summary>
public static class DiagnosticsCollector
{
    /// <summary>Largest amount of log text put into one report (the newest lines are kept).</summary>
    public const int MaxLogBytes = 16 * 1024 * 1024;

    public static DiagnosticsSanitizer SanitizerFor(AppServices services, bool includeFullPaths) =>
        DiagnosticsSanitizer.ForCurrentUser(includeFullPaths, AppContext.BaseDirectory,
            string.IsNullOrWhiteSpace(services.Settings.OutputFolder) ? null : [(services.Settings.OutputFolder, "%OUTPUT%")]);

    public static IReadOnlyList<ReportEntry> Collect(AppServices services, ReportOptions options, IReadOnlyList<HealthResult>? selfCheck, ResourceSample? sample)
    {
        var sanitizer = SanitizerFor(services, options.IncludeFullPaths);
        bool Include(string id) => DiagnosticsReport.Categories.First(c => c.Id == id) is { Optional: false } || options.Categories.Contains(id);
        var entries = new List<ReportEntry>();
        var tools = services.Tools.Tools;
        var availability = tools.ToDictionary(t => t.Id, t => services.Tools.Availability(t));
        selfCheck ??= Include("selfcheck") ? HealthCheck.Run(services) : null;
        var history = services.Jobs.History;
        var problems = AppLog.RecentProblems();

        // ---------------------------------------------------------------- system.json
        var system = new
        {
            application = AppIdentity.NameAndVersion,
            fileVersion = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version,
            edition = services.Paths?.IsPortable == true ? "portable" : "installed",
            singleFile = !File.Exists(Path.Combine(AppContext.BaseDirectory, "TibiaFoundry.dll")),
            windows = RuntimeInformation.OSDescription,
            windowsVersion = Environment.OSVersion.Version.ToString(),
            runtime = RuntimeInformation.FrameworkDescription,
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            processors = Environment.ProcessorCount,
            memoryAvailableToProcess = ReportWriter.FormatSize(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes),
            libraries = new Dictionary<string, string?>
            {
                ["Magick.NET"] = typeof(ImageMagick.MagickImage).Assembly.GetName().Version?.ToString(3),
                ["Google.Protobuf"] = typeof(Google.Protobuf.IMessage).Assembly.GetName().Version?.ToString(3),
                ["NAudio"] = typeof(NAudio.Wave.WaveFormat).Assembly.GetName().Version?.ToString(3),
                ["CommunityToolkit.Mvvm"] = typeof(CommunityToolkit.Mvvm.ComponentModel.ObservableObject).Assembly.GetName().Version?.ToString(3),
            },
            // Only the names of the options the program was started with; their values may contain paths.
            commandLineOptions = Environment.GetCommandLineArgs().Skip(1).Where(a => a.StartsWith("--", StringComparison.Ordinal)).Distinct().ToList(),
            environmentVariables = "not included",
            client = new { package = services.Data.PackageDisplayName, version = services.Data.ClientVersion, assetsFound = services.Data.AssetsAvailable },
            settingsFormat = services.Settings.Version,
            theme = ThemeManager.ActiveCustom != null ? "custom" : services.Settings.Theme.ToString(),
        };
        entries.Add(ReportEntry.Text("system.json", "system", "Versions of Tibia Foundry, Windows, .NET and the bundled libraries",
            sanitizer.SanitizeJson(JsonSerializer.Serialize(system, DiagnosticsReport.Json))));

        // ---------------------------------------------------------------- settings.json
        if (Include("settings"))
            entries.Add(ReportEntry.Text("settings.json", "settings", "Settings (creator name left out, paths sanitized)", SanitizedSettings(services.Settings, sanitizer)));

        // ---------------------------------------------------------------- tools.json
        if (Include("tools"))
        {
            var list = tools.Select(t => new
            {
                id = t.Id, name = t.Name, type = t.Type, version = t.Version, category = t.Category,
                capabilities = ToolManifest.CapabilityNames(t.Capabilities),
                operations = t.Operations.Select(o => new { o.Id, o.Name, capabilities = ToolManifest.CapabilityNames(o.Capabilities) }),
                inputs = t.Inputs.Select(f => f.ToString()), outputs = t.Outputs.Select(f => f.ToString()),
                defaultOutput = t.DefaultOutput?.ToString(),
                canRun = availability[t.Id].CanRun,
                problem = availability[t.Id].Problem,
                dependencies = availability[t.Id].Dependencies.Select(d => new { d.Dependency.Id, d.Dependency.Name, kind = d.Dependency.Kind, d.Dependency.Required, state = d.State, d.Version }),
                license = t.License.Name,
            });
            var json = JsonSerializer.Serialize(new { tools = list, rejected = services.Tools.Rejected }, DiagnosticsReport.Json);
            entries.Add(ReportEntry.Text("tools.json", "tools", "Registered tools, capabilities and dependency checks", sanitizer.SanitizeJson(json)));
        }

        // ---------------------------------------------------------------- selfcheck.json
        if (Include("selfcheck") && selfCheck != null)
            entries.Add(ReportEntry.Text("selfcheck.json", "selfcheck", "Results of the self-check",
                sanitizer.SanitizeJson(JsonSerializer.Serialize(selfCheck.Select(r => new { r.Area, r.Check, severity = r.SeverityText, r.Message, r.Remedy }), DiagnosticsReport.Json))));

        // ---------------------------------------------------------------- operations.json
        if (Include("operations"))
        {
            var active = services.Jobs.Active.Select(o => new
            {
                o.Id, o.ToolName, o.Title, state = OperationTracker.Describe(o.State), seconds = Math.Round(o.Elapsed.TotalSeconds, 1), o.Done, o.Total, o.Stage, o.Output,
                warnings = o.Warnings.Count, errors = o.Errors.Count,
            });
            entries.Add(ReportEntry.Text("operations.json", "operations", "Running and recent operations (names, durations, counts, outcomes)",
                sanitizer.SanitizeJson(JsonSerializer.Serialize(new { active, recent = history.Take(100) }, DiagnosticsReport.Json))));
        }

        // ---------------------------------------------------------------- metrics.json
        if (Include("metrics"))
        {
            var disks = ResourceMonitor.Disks(OutputLocations.Kinds.Select(k => (k.DisplayName, services.Outputs.FolderFor(k.Kind))));
            var metrics = new
            {
                measured = sample == null ? null : new
                {
                    cpuPercent = sample.CpuPercent is { } c ? Math.Round(c, 1) : (double?)null,
                    workingSet = ReportWriter.FormatSize(sample.WorkingSet), privateBytes = ReportWriter.FormatSize(sample.PrivateBytes),
                    managedHeap = ReportWriter.FormatSize(sample.ManagedHeap), gcCollections = new[] { sample.Gen0, sample.Gen1, sample.Gen2 },
                    sample.Threads, sample.Handles, uptimeMinutes = Math.Round(sample.Uptime.TotalMinutes, 1),
                },
                note = "CPU and memory are measured for the whole process; per-operation use is not measured.",
                operations = new { running = services.Jobs.RunningCount, queued = services.Jobs.QueuedCount, failedRecently = history.Take(50).Count(h => h.State == OperationState.Failed) },
                disks = disks.Select(d => new { d.Drive, d.Folders, free = d.FreeBytes, total = d.TotalBytes, d.Problem }),
            };
            entries.Add(ReportEntry.Text("metrics.json", "metrics", "Processor and memory use, operations, free disk space",
                sanitizer.SanitizeJson(JsonSerializer.Serialize(metrics, DiagnosticsReport.Json))));
        }

        // ---------------------------------------------------------------- logs
        if (Include("logs"))
        {
            long budget = MaxLogBytes;
            var since = DateTime.Now.Date.AddDays(1 - options.LogDays);
            foreach (var file in AppLog.Files().Where(f => f.LastWriteTime >= since).OrderByDescending(f => f.Name))
            {
                if (budget <= 0) break;
                string text = ReadShared(file.FullName);
                if (text.Length > budget) text = "[older lines left out to keep the report small]" + Environment.NewLine + text[^(int)budget..];
                string clean = sanitizer.Sanitize(text);
                budget -= clean.Length;
                entries.Add(ReportEntry.Text("logs/" + file.Name, "logs", "Log file (sanitized)", clean));
            }
        }

        // ---------------------------------------------------------------- summary.txt (first in the archive)
        entries.Insert(0, ReportEntry.Text("summary.txt", "summary", "Readable overview",
            sanitizer.Sanitize(Summary(services, options, selfCheck, history, problems, entries, availability))));
        return entries;
    }

    private static string Summary(AppServices services, ReportOptions options, IReadOnlyList<HealthResult>? selfCheck, IReadOnlyList<OperationSummary> history,
        IReadOnlyList<LogEntry> problems, IReadOnlyList<ReportEntry> entries, Dictionary<string, ToolAvailability> availability)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AppIdentity.NameAndVersion} — diagnostics report");
        sb.AppendLine($"Created {DateTime.Now:yyyy-MM-dd HH:mm:ss} on this computer. Nothing was uploaded or sent; share it only if you want to.");
        sb.AppendLine(options.IncludeFullPaths
            ? "Full folder paths, the user name and the computer name are INCLUDED (chosen when the report was made). Passwords, tokens and keys are masked."
            : "Folder paths, the Windows user name and the computer name were replaced by placeholders such as %USERPROFILE%. Passwords, tokens and keys are masked.");
        sb.AppendLine();
        sb.AppendLine("Application");
        sb.AppendLine($"  Edition: {(services.Paths?.IsPortable == true ? "portable" : "installed")}; settings format {services.Settings.Version}; theme {(ThemeManager.ActiveCustom != null ? "custom" : services.Settings.Theme.ToString())}");
        sb.AppendLine($"  Windows: {RuntimeInformation.OSDescription}; {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.ProcessArchitecture}; {Environment.ProcessorCount} processor cores");
        sb.AppendLine($"  Client: {services.Data.PackageDisplayName}, version {services.Data.ClientVersion ?? "unknown"}, files {(services.Data.AssetsAvailable ? "found" : "not found")}");
        sb.AppendLine($"  Output folder: {services.Settings.OutputFolder}");
        sb.AppendLine();
        // Where every client file was found, and whether it was chosen (paths are sanitized with the rest).
        sb.Append(services.Data.Locations.Describe());
        sb.AppendLine();
        int unavailable = availability.Values.Count(a => !a.CanRun);
        sb.AppendLine($"Tools: {services.Tools.Tools.Count} registered, {unavailable} unavailable, {services.Tools.Rejected.Count} rejected");
        foreach (var t in services.Tools.Tools.Where(t => !availability[t.Id].CanRun)) sb.AppendLine($"  {t.Name}: {availability[t.Id].Problem}");
        sb.AppendLine();
        if (selfCheck != null)
        {
            sb.AppendLine($"Self-check: {Plural.Count(selfCheck.Count(r => r.Severity == HealthSeverity.Error), "error")}, {Plural.Count(selfCheck.Count(r => r.Severity == HealthSeverity.Warning), "warning")}, {selfCheck.Count(r => r.Severity == HealthSeverity.Ok)} OK");
            foreach (var r in selfCheck.Where(r => r.Severity is HealthSeverity.Error or HealthSeverity.Warning)) sb.AppendLine($"  {r.SeverityText}: {r.Area} / {r.Check}: {r.Message}");
            sb.AppendLine();
        }
        sb.AppendLine("Recent operations (newest first)");
        foreach (var h in history.Take(20)) sb.AppendLine($"  {h.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {OperationTracker.Describe(h.State),-24} {h.Seconds,8:0.0} s  {h.ToolName}: {h.Title}");
        if (history.Count == 0) sb.AppendLine("  (none)");
        sb.AppendLine();
        sb.AppendLine($"Warnings and errors of this session: {problems.Count}");
        foreach (var p in problems.TakeLast(30)) sb.AppendLine($"  {p.Time:HH:mm:ss} {p.Level} [{p.Source}] {p.Message}");
        sb.AppendLine();
        sb.AppendLine("Files in this report");
        sb.AppendLine("  summary.txt — this overview");
        foreach (var e in entries) sb.AppendLine($"  {e.Name} — {e.Description} ({ReportWriter.FormatSize(e.Size)})");
        sb.AppendLine("  manifest.json — list of the files with their sizes and SHA-256 checksums");
        return sb.ToString();
    }

    /// <summary>The settings as JSON with the creator name left out and every path sanitized.</summary>
    public static string SanitizedSettings(AppSettings settings, DiagnosticsSanitizer sanitizer)
    {
        var options = new JsonSerializerOptions(DiagnosticsReport.Json);
        var node = JsonNode.Parse(JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        }))!.AsObject();
        node[nameof(AppSettings.CreatorName)] = "(left out)";
        return sanitizer.SanitizeJson(node.ToJsonString(options));
    }

    private static string ReadShared(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (IOException ex)
        {
            return $"(this log file could not be read: {ex.Message})";
        }
    }
}
