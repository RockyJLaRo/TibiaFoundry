using System.Windows;
using System.Windows.Threading;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.Themes;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.App.Views;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App;

/// <summary>
/// Start-up: logging, crash handling, settings (with legacy import and corruption recovery),
/// theme, services and the main window.
/// </summary>
/// <remarks>
/// Command line (for automated verification):
/// <c>--ui-snapshot &lt;folder&gt; [--theme light|dark] [--size 1280x820]</c> renders every page to PNG and exits; <c>--site-screenshots &lt;folder&gt;</c> captures the website screenshots with the normal data;
/// <c>--self-test &lt;folder&gt;</c> checks the bundled components headlessly and exits (0 = no failures);
/// <c>--language &lt;code&gt;</c> starts in a language without saving it (en, pt-BR, pl, es, de);
/// <c>--localization-report &lt;file&gt;</c> writes the translation completeness report and exits.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Services are disposed in OnExit; Application has no Dispose.")]
public partial class App : Application
{
    private AppServices? _services;
    private Timer? _snapshotWatchdog;

    /// <summary>Settings and log locations (per-user, or next to the program in portable mode).</summary>
    public static AppPaths Paths { get; private set; } = AppPaths.Resolve(AppContext.BaseDirectory, AppPaths.IsPortableBuild);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // The log file is opened once the settings (which may choose the log folder) are read; lines
        // written before that are kept in memory and written then.
        AppLog.Info($"{AppIdentity.NameAndVersion} starting (options: {string.Join(' ', e.Args.Where(a => a.StartsWith("--", StringComparison.Ordinal)))}).");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Critical("Unhandled exception (process terminating).", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        var options = StartupOptions.Parse(e.Args);
        if (options.SelfTestFolder != null)
        {
            AppLog.Initialize(Paths.LogDirectory);
            int failures = await SelfTest.RunAsync(options.SelfTestFolder, Paths);
            Shutdown(failures == 0 ? 0 : 1);
            return;
        }
        if (options.LocalizationReport != null)
        {
            AppLog.Initialize(Paths.LogDirectory);
            int incomplete = Localization.LocalizationReport.Write(options.LocalizationReport);
            Shutdown(incomplete == 0 ? 0 : 1);
            return;
        }
        DialogService.Unattended = options.SnapshotFolder != null || options.SiteScreenshots != null;
        if (options.SnapshotFolder != null)
        {
            // An unattended run must always end, even if a page never finishes loading.
            _snapshotWatchdog = new Timer(_ =>
            {
                AppLog.Error("UI snapshot did not finish within 10 minutes; exiting.");
                AppLog.Shutdown();
                Environment.Exit(3);
            }, null, TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);
        }
        // An unattended UI snapshot keeps what it writes (settings, log, operation history) in a temporary
        // folder of its own, so a verification run leaves the user's folders untouched.
        string? snapshotData = options.SnapshotFolder != null ? Path.Combine(Path.GetTempPath(), AppIdentity.Id + "-ui-snapshot") : null;
        var store = snapshotData != null
            ? new SettingsStore(Path.Combine(snapshotData, "settings.json"))
            : new SettingsStore(Paths.SettingsFile);
        var legacy = new[] { Path.Combine(AppContext.BaseDirectory, "settings.json") };
        var (settings, status, detail) = store.Load(legacy, options.SnapshotFolder != null ? null : Paths.PreviousSettingsFiles);
        Attribution.CurrentCreator = settings.CreatorName;
        // ...and its Asset Library too (8.8): files saved during a snapshot never reach the user's library.
        if (snapshotData != null) settings.Library.Folder = Path.Combine(snapshotData, "Library");
        if (options.Theme is { } forcedTheme)
        {
            settings.Theme = forcedTheme;
            settings.Themes.ActiveCustomTheme = null;
        }

        MainViewModel vm;
        MainWindow window;
        string? folderNote = null;
        try
        {
            _services = new AppServices(store, settings) { Paths = Paths };
            if (options.SnapshotFolder == null)
            {
                folderNote = _services.MigrateFolders();
                _services.ApplyLogSettings();
            }
            else AppLog.Initialize(Path.Combine(snapshotData!, "logs"));
            AppLog.Info(snapshotData != null ? $"UI snapshot: settings, log and operation history in {snapshotData}."
                : Paths.IsPortable ? L.F("App.PortableModeDataIn0", Paths.DataDirectory) : L.F("App.Settings0", Paths.SettingsFile));
            // Interface language: before any window or page exists, so everything starts in it.
            Localization.LocalizationManager.Apply(options.Language ?? settings.Language);
            ThemeManager.Initialize(settings.Theme, settings.Themes.Find(settings.Themes.ActiveCustomTheme));
            Controls.AnimatedSprite.AnimationsEnabled = settings.Interface.AnimatePreviews;
            vm = new MainViewModel(_services);
            PageTemplates.Register(Resources, vm.Registrations);
            window = new MainWindow(vm);
            MainWindow = window;
            window.RestorePlacement(settings.Interface.RememberWindowPlacement ? settings.MainWindow : new WindowPlacement(), options.Size);
            window.Show();
        }
        catch (Exception ex)
        {
            // Without a main window the process would keep running invisibly; report and exit.
            // (Seen in practice: Windows refusing new windows with "Not enough memory resources"
            // when the session's window-class atom table is exhausted.)
            if (AppLog.LogDirectory == null) AppLog.Initialize(Paths.LogDirectory);
            AppLog.Critical("Start-up failed.", ex);
            if (!DialogService.Unattended)
            {
                MessageBox.Show(
                    AppIdentity.Name + L.T("App.CouldNotStart") + (ex.InnerException ?? ex).Message +
                    (AppLog.LogFilePath != null ? L.T("App.TechnicalDetailsWereSavedInThe") + AppLog.LogFilePath : ""),
                    AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown(2);
            return;
        }

        if (Paths.Notice != null) _services.Status.Show(Paths.Notice, StatusKind.Warning);
        foreach (string note in settings.MigrationNotes) AppLog.Info("Settings upgrade: " + note);
        if (folderNote != null) AppLog.Info("Settings upgrade: " + folderNote);
        switch (status)
        {
            case SettingsLoadStatus.ImportedLegacy:
                _services.Status.Show(L.F("App.SettingsWereImportedFromTheOriginal", detail, settings.CreatorName), StatusKind.Success);
                _services.SaveSettings();
                break;
            case SettingsLoadStatus.MigratedFromPreviousName:
                // Settings of version 3.0 or 2.x (stored under the folder names those versions used): copied, the old file stays as it was.
                _services.Status.Show(L.F("App.SettingsWereCarriedOverFromAn", detail, settings.CreatorName), StatusKind.Success);
                _services.SaveSettings();
                break;
            case SettingsLoadStatus.Loaded when settings.LoadedVersion < AppSettings.CurrentVersion && options.SnapshotFolder == null:
                // Upgraded from 5.0: where new files go now, and that nothing was moved.
                _services.Status.Show(string.Join(" ", settings.MigrationNotes.Append(folderNote).OfType<string>()), StatusKind.Info);
                _services.SaveSettings();
                break;
            case SettingsLoadStatus.CreatedDefaults when options.SnapshotFolder == null:
                _services.Status.Show(L.F("App.ExportedImagesAreAttributedTo0", settings.CreatorName), StatusKind.Info);
                break;
            case SettingsLoadStatus.RecoveredFromCorruption:
                _services.Status.Show(L.F("App.TheSettingsFileWasDamagedSo", detail), StatusKind.Warning);
                break;
            case SettingsLoadStatus.LoadedFromNewerVersion:
                _services.Status.Show(L.F("App.YourSettingsWereSavedByA", detail), StatusKind.Warning);
                break;
        }

        if (options.SnapshotFolder != null)
        {
            await UiSnapshot.RunAsync(window, vm, options.SnapshotFolder, bothThemes: options.Theme == null);
            _snapshotWatchdog?.Dispose();
            Shutdown();
            return;
        }
        var services = _services;
        _ = Task.Run(() => AppLog.Info("Start-up housekeeping: " + services.Housekeeping(atExit: false)));
        // Which client to use (8.0): first-run setup, "ask which client", or the recovery dialog — before the first page loads.
        try
        {
            AppLog.Info("Client at start: " + await vm.Startup.RunAsync());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            AppLog.Warn("The client could not be chosen at start: " + ex.Message);
        }
        vm.ClientChangedElsewhere();
        await vm.StartAsync();
        if (options.SiteScreenshots != null)
        {
            await Views.SiteScreenshots.RunAsync(window, vm, options.SiteScreenshots);
            Shutdown();
        }
    }

    private bool _showingError;
    private readonly Queue<DateTime> _recentErrors = new();

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true; // keep the application alive; the user can continue working
        var now = DateTime.UtcNow;
        _recentErrors.Enqueue(now);
        while (_recentErrors.Count > 0 && now - _recentErrors.Peek() > TimeSpan.FromSeconds(5)) _recentErrors.Dequeue();

        // An exception thrown during layout or rendering repeats on every frame. Instead of
        // flooding the log and stacking dialogs, stop after a burst with a clear message.
        if (_recentErrors.Count > 8)
        {
            if (_recentErrors.Count == 9)
            {
                AppLog.Critical("Repeated unhandled UI exceptions; shutting down.", e.Exception);
                _services?.SaveSettings();
                if (!DialogService.Unattended) System.Windows.MessageBox.Show(
                    AppIdentity.Name + L.T("App.HitARepeatingErrorAndHas") + e.Exception.Message + L.T("App.TechnicalDetailsWereSavedInThe") + AppLog.LogFilePath,
                    AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }

        AppLog.Error("Unhandled UI exception.", e.Exception);
        if (_showingError) return; // an error while the error dialog itself is shown
        _showingError = true;
        try
        {
            (_services?.Dialogs ?? new DialogService()).ShowError(L.T("App.AnUnexpectedErrorOccurredTheApplication"), e.Exception, log: false);
        }
        catch (Exception dialogError)
        {
            AppLog.Error("Could not show the error dialog.", dialogError);
        }
        finally
        {
            _showingError = false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _services?.SaveSettings();
            if (_services != null && !DialogService.Unattended) AppLog.Info("Exit housekeeping: " + _services.Housekeeping(atExit: true));
            _services?.Dispose();
        }
        finally
        {
            AppLog.Info(AppIdentity.Name + " exited.");
            AppLog.Shutdown();
            base.OnExit(e);
        }
    }
}

/// <summary>Parsed command line.</summary>
public sealed record StartupOptions(string? SnapshotFolder, ThemePreference? Theme, Size? Size, string? SelfTestFolder = null, string? Language = null, string? LocalizationReport = null, string? SiteScreenshots = null)
{
    public static StartupOptions Parse(string[] args)
    {
        string? folder = null;
        string? selfTest = null;
        string? language = null, report = null, site = null;
        ThemePreference? theme = null;
        Size? size = null;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].ToLowerInvariant();
            string? next = i + 1 < args.Length ? args[i + 1] : null;
            if (a == "--ui-snapshot" && next != null) { folder = next; i++; }
            else if (a == "--self-test" && next != null) { selfTest = next; i++; }
            else if (a == "--language" && next != null) { language = next; i++; }
            else if (a == "--localization-report" && next != null) { report = next; i++; }
            else if (a == "--site-screenshots" && next != null) { site = next; i++; }
            else if (a == "--theme" && next != null) { theme = Enum.TryParse<ThemePreference>(next, true, out var t) ? t : null; i++; }
            else if (a == "--size" && next != null)
            {
                var parts = next.ToLowerInvariant().Split('x');
                if (parts.Length == 2 && double.TryParse(parts[0], out double w) && double.TryParse(parts[1], out double h)) size = new Size(w, h);
                i++;
            }
        }
        return new StartupOptions(folder, theme, size, selfTest, language, report, site);
    }
}
