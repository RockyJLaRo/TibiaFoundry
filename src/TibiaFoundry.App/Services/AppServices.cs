using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Scripts;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Tools;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

/// <summary>
/// Composition root: the single set of services shared by all view-models. Created once in
/// <see cref="App"/> at start-up.
/// </summary>
public sealed class AppServices : IDisposable
{
    public AppServices(SettingsStore store, AppSettings settings)
    {
        SettingsStore = store;
        Settings = settings;
        Clients = new ClientLibrary(settings);
        Data = new ClientDataService(settings, Clients);
        Locations = new LocationService(this);
        // All requests go through HttpFetcher, which sets a timeout per request (FetchOptions); a
        // client-wide limit would silently cut longer ones such as the TibiaWiki page (120 s).
        Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        // Identify the tool to the wiki (MediaWiki etiquette); the version comes from the build.
        Http.DefaultRequestHeaders.UserAgent.ParseAdd($"TibiaFoundry/{AppIdentity.Version} (+{TibiaFoundry.Core.Net.ExternalEndpoints.TibiaWikiHome})");
        Audio.Volume = (float)settings.SoundVolume;
        Operations = new OperationGate(() => Settings.Performance.MaxConcurrentOperations);
        ScriptOperations = new OperationGate(() => Math.Min(Settings.Scripts.MaxConcurrentScripts, Settings.Performance.MaxConcurrentOperations));
        Notifications = new NotificationService(settings);
        Outputs = new OutputService(this);
        ToolSettings = new ToolSettingsStore(settings.ToolState);
        // Every saved file or folder is announced through the status bar; the Home dashboard lists the latest.
        _library = new Lazy<LibraryService>(() => new LibraryService(this));
        Status.Saved += link =>
        {
            Settings.RememberExport(link.Path, link.IsFile, DateTime.UtcNow);
            SaveSettings();
            // ...and the Asset Library adds it in the background (8.8; Library settings → Add exports automatically).
            if (Settings.Library.AddExportsAutomatically) Library.OnSaved(link);
        };
        // The history is kept next to the settings file (also when a UI snapshot or a read-only portable
        // folder moves the settings elsewhere); services without program folders (tests) keep it in memory.
        _jobs = new Lazy<OperationTracker>(() => new OperationTracker(Paths != null ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(store.FilePath))!, "operations.json") : null,
            () => Settings.Diagnostics.OperationHistoryEntries));
        RegisterDependencyProbes();
    }

    /// <summary>Settings and log locations (null in tests). Setting them also places the client store (7.3) and reloads the client in use.</summary>
    public AppPaths? Paths
    {
        get => _paths;
        init
        {
            _paths = value;
            if (value == null) return;
            Clients.Configure(ClientLibrary.RootFor(value, Settings), value.IsPortable ? Path.GetDirectoryName(Path.GetFullPath(value.DataDirectory)) : null);
            if (Settings.ActiveClientLive != null || Settings.ActiveClientTest != null) Data.RefreshPaths();
        }
    }

    private readonly AppPaths? _paths;

    /// <summary>The client store and the client in use per server (7.3).</summary>
    public ClientLibrary Clients { get; }

    private readonly Lazy<LibraryService> _library;

    /// <summary>The Asset Library and the processing queue (8.8), created on first use.</summary>
    public LibraryService Library => _library.Value;

    /// <summary>Default folders and file dialogs of every kind of output (Documents\Tibia_Foundry\…).</summary>
    public OutputService Outputs { get; }

    /// <summary>The registered tools: their manifests, capabilities and dependency checks.</summary>
    public ToolCatalog Tools { get; } = new();

    /// <summary>Settings the tools declare in their manifests (validated, per tool).</summary>
    public ToolSettingsStore ToolSettings { get; }

    private readonly Lazy<OperationTracker> _jobs;

    /// <summary>Queued, running and finished long operations (Diagnostics page).</summary>
    public OperationTracker Jobs => _jobs.Value;

    /// <summary>Checks of the dependencies tools declare (run before an operation starts and on the Diagnostics page).</summary>
    private void RegisterDependencyProbes()
    {
        Tools.AddProbe("tibia-client", () => Data.AssetsAvailable
            ? new DependencyProbeResult(DependencyState.Available, Data.ClientVersion, Data.AssetsDirectory)
            : new DependencyProbeResult(DependencyState.Missing, Detail: L.F("AppServices.NoClientFilesIn0", Data.AssetsDirectory)));
        Tools.AddProbe("imagemagick", () =>
        {
            try
            {
                string version = typeof(ImageMagick.MagickImage).Assembly.GetName().Version?.ToString(3) ?? "?";
                _ = ImageMagick.MagickNET.Version; // loads the native library
                return new DependencyProbeResult(DependencyState.Available, version);
            }
            catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or BadImageFormatException or FileNotFoundException)
            {
                return new DependencyProbeResult(DependencyState.Missing, Detail: ex.GetBaseException().Message);
            }
        });
        Tools.AddProbe("naudio", () =>
        {
            try
            {
                return new DependencyProbeResult(DependencyState.Available, typeof(NAudio.Wave.WaveFormat).Assembly.GetName().Version?.ToString(3));
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or TypeInitializationException)
            {
                return new DependencyProbeResult(DependencyState.Missing, Detail: ex.Message);
            }
        });
        Tools.AddProbe("taglib", () =>
        {
            try
            {
                return new DependencyProbeResult(DependencyState.Available, typeof(TagLib.File).Assembly.GetName().Version?.ToString(3));
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or TypeInitializationException)
            {
                return new DependencyProbeResult(DependencyState.Missing, Detail: ex.Message);
            }
        });
        // Web services are not contacted in advance (no network traffic without a user action).
        Tools.AddProbe("tibiawiki", () => new DependencyProbeResult(DependencyState.NotChecked, Detail: L.T("AppServices.ContactedOnlyWhenAnOperationNeeds")));
        Tools.AddProbe("tibiamaps", () => new DependencyProbeResult(DependencyState.NotChecked, Detail: L.T("AppServices.ContactedOnlyWhenYouOpenA")));
    }

    /// <summary>
    /// Folder for downloaded caches (TibiaWiki item list): next to the settings, so portable mode
    /// keeps it on the stick. Falls back to the per-user local data folder.
    /// </summary>
    public string CacheDirectory => Path.Combine(
        Paths?.DataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.Id), "cache");

    /// <summary>Per-machine program data: the portable data folder, or %LOCALAPPDATA%\TibiaFoundry.</summary>
    public string LocalDataDirectory => Paths is { IsPortable: true }
        ? Paths.DataDirectory
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.Id);


    /// <summary>The 5.0 default backup folder of the installed edition (kept when it holds backups; see <see cref="MigrateFolders"/>).</summary>
    public string PreviousDefaultBackupFolder { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.Id, "backups");

    /// <summary>
    /// Backups: the portable edition keeps them next to the program; the installed edition in
    /// <c>Backups</c> under the output root (Documents\Tibia_Foundry), or in %LOCALAPPDATA% when Windows
    /// reports no Documents folder.
    /// </summary>
    public string DefaultBackupFolder => Paths is { IsPortable: true } || string.IsNullOrWhiteSpace(Settings.OutputFolder)
        ? Path.Combine(LocalDataDirectory, "backups")
        : OutputLocations.DefaultFor(OutputKind.Backups, Settings.OutputFolder);

    /// <summary>The system's temporary folder; in portable mode the data folder, so nothing is left on the host.</summary>
    public string DefaultTempFolder => Paths is { IsPortable: true }
        ? Path.Combine(LocalDataDirectory, "temp")
        : Path.Combine(Path.GetTempPath(), AppIdentity.Id);

    /// <summary>Logs: next to the program (portable), or <c>Logs</c> under the output root (installed).</summary>
    public string DefaultLogFolder => Paths is { IsPortable: true } || string.IsNullOrWhiteSpace(Settings.OutputFolder)
        ? Paths?.LogDirectory ?? Path.Combine(LocalDataDirectory, "logs")
        : OutputLocations.DefaultFor(OutputKind.Logs, Settings.OutputFolder);

    public string BackupFolder => string.IsNullOrWhiteSpace(Settings.Locations.BackupFolder) ? DefaultBackupFolder : Settings.Locations.BackupFolder;
    public string TempFolder => string.IsNullOrWhiteSpace(Settings.Locations.TempFolder) ? DefaultTempFolder : Settings.Locations.TempFolder;
    public string LogFolder => string.IsNullOrWhiteSpace(Settings.Locations.LogFolder) ? DefaultLogFolder : Settings.Locations.LogFolder;

    private BackupStore? _backups;
    private TempWorkspace? _temp;

    /// <summary>Backups of replaced and removed files (follows the folder chosen in Settings).</summary>
    public BackupStore Backups
    {
        get
        {
            string folder = PathGuard.Normalize(BackupFolder);
            if (_backups == null || !string.Equals(_backups.Root, folder, StringComparison.OrdinalIgnoreCase)) _backups = new BackupStore(folder);
            return _backups;
        }
    }

    /// <summary>Temporary working folders (follows the folder chosen in Settings; running operations keep theirs).</summary>
    public TempWorkspace Temp
    {
        get
        {
            string folder = PathGuard.Normalize(TempFolder);
            if (_temp == null || !string.Equals(_temp.Root, folder, StringComparison.OrdinalIgnoreCase)) _temp = new TempWorkspace(folder);
            return _temp;
        }
    }

    /// <summary>History of script runs (next to the settings).</summary>
    public ScriptHistory ScriptHistory => new(Path.Combine(Paths?.DataDirectory ?? LocalDataDirectory, "script-history.json"));

    /// <summary>Undo records of the Batch File Renamer (inside the backup folder).</summary>
    public string RenameRecordFolder => Path.Combine(BackupFolder, BatchRenamerScript.RecordFolderName);

    /// <summary>Limits concurrent long operations (Settings → Performance).</summary>
    public OperationGate Operations { get; }

    /// <summary>Limits concurrent scripts (Settings → Scripts; never above the general limit).</summary>
    public OperationGate ScriptOperations { get; }

    public NotificationService Notifications { get; }

    /// <summary>
    /// Settings upgraded from 5.0: backups made by 5.0 stay usable. When the old default backup
    /// folder holds backups, it stays the backup folder (shown as a chosen folder in Settings, where
    /// "Use default" switches to the new location); nothing is moved. Returns what was done.
    /// </summary>
    public string? MigrateFolders()
    {
        if (Settings.LoadedVersion >= 4 || Paths is { IsPortable: true } || Settings.Locations.BackupFolder != null) return null;
        string old = PreviousDefaultBackupFolder;
        try
        {
            if (!Directory.Exists(old) || !Directory.EnumerateFileSystemEntries(old).Any()) return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        Settings.Locations.BackupFolder = old;
        return L.F("AppServices.YourExistingBackupsStayIn0", old, OutputLocations.DefaultFor(OutputKind.Backups, Settings.OutputFolder));
    }

    /// <summary>A new output session with the current file safety settings and the user's existing-file question.</summary>
    public OutputSession CreateOutput(string operation) =>
        new(operation, Settings.FileSafety, Backups, (path, op, _) => Dialogs.AskConflict(path, op, Settings.Safety.BackupBeforeChanges));

    /// <summary>Asks about existing files (for operations that create their own sessions).</summary>
    public ConflictPrompt ConflictPrompt => (path, op, _) => Dialogs.AskConflict(path, op, Settings.Safety.BackupBeforeChanges);

    public SettingsStore SettingsStore { get; }
    public AppSettings Settings { get; }
    public ClientDataService Data { get; }

    /// <summary>Choosing, checking and resetting the client's files and folders.</summary>
    public LocationService Locations { get; }

    public StatusService Status { get; } = new();

    /// <summary>
    /// Why the client in use cannot be switched right now, or null (8.0). An operation that is running
    /// reads the current client's files, so a switch waits until it ends. Open documents with unsaved
    /// changes (Assets Editor, RCC Editor) stay open across a switch and lose nothing.
    /// </summary>
    public string? WhyNotSwitchClient() => Status.IsBusy
        ? L.T("AppServices.AnOperationIsRunningAndReads")
        : null;
    public IDialogService Dialogs { get; set; } = new DialogService();
    public ClipboardService Clipboard { get; } = new();
    public ShellService Shell { get; } = new();

    /// <summary>
    /// Opens the file or folder a status message names (the clickable path in the status bar).
    /// A failure to open it is reported on its own; the message about the saved file stays.
    /// </summary>
    public void RevealSaved(StatusLink link)
    {
        var result = link.IsFile ? Shell.Reveal(link.Path) : Directory.Exists(link.Path) ? Shell.Reveal(link.Path) : RevealResult.Failed(L.F("AppServices.0NoLongerExists", link.Path));
        if (!result.Opened)
        {
            AppLog.Warn("Opening the saved location failed: " + result.Problem, source: "Shell");
            Dialogs.Show(L.F("AppServices.TheFileWasSavedButIts", result.Problem), MessageKind.Warning, L.T("AppServices.OpenFolder"));
        }
        else if (result.Problem != null)
        {
            Dialogs.Show(result.Problem, MessageKind.Info, L.T("AppServices.OpenFolder"));
        }
    }
    public AudioPlayer Audio { get; } = new();
    public HttpClient Http { get; }

    /// <summary>Raised when a page asks to open an appearance in the Appearances viewer.</summary>
    public event Action<Core.Appearances.AppearanceKind, uint>? OpenAppearanceRequested;

    /// <summary>Raised when a page asks to open the outfitter with a given outfit, colors and addons.</summary>
    public event Action<uint, int[], uint>? OpenOutfitterRequested;

    public void RequestOpenAppearance(Core.Appearances.AppearanceKind kind, uint id) => OpenAppearanceRequested?.Invoke(kind, id);

    public void RequestOpenOutfitter(uint outfitId, int[] colors, uint addons) => OpenOutfitterRequested?.Invoke(outfitId, colors, addons);

    /// <summary>Raised when a page asks the shell to show another page (by view-model type).</summary>
    public event Action<Type>? NavigateRequested;

    /// <summary>Raised after the creator name (attribution) changed in Settings.</summary>
    public event Action? CreatorChanged;

    /// <summary>Raised after interface settings changed (scale, navigation, previews), so open views can update.</summary>
    public event Action? InterfaceChanged;

    public void RaiseInterfaceChanged() => InterfaceChanged?.Invoke();

    /// <summary>Raised after a tool's declared settings changed in Settings → Tools (tool id), so its page can update.</summary>
    public event Action<string>? ToolSettingsChanged;

    public void RaiseToolSettingsChanged(string toolId) => ToolSettingsChanged?.Invoke(toolId);

    /// <summary>Stores and applies a new creator name; empty input restores the default.</summary>
    public void SetCreatorName(string? name)
    {
        string normalized = Core.Metadata.Attribution.NormalizeCreator(name) ?? Core.Metadata.Attribution.DefaultCreator;
        if (normalized == Settings.CreatorName && normalized == Core.Metadata.Attribution.CurrentCreator) return;
        Settings.CreatorName = normalized;
        Core.Metadata.Attribution.CurrentCreator = normalized;
        SaveSettings();
        CreatorChanged?.Invoke();
    }

    public void RequestNavigate<TPage>() => NavigateRequested?.Invoke(typeof(TPage));

    /// <summary>Raised when a page hands work to Content Studio (8.8): the shell opens it and runs the action on it.</summary>
    public event Action<Action<ViewModels.ContentStudioViewModel>>? ContentStudioRequested;

    public void RequestContentStudio(Action<ViewModels.ContentStudioViewModel> action) => ContentStudioRequested?.Invoke(action);

    /// <summary>
    /// Session-only switch (the original's "Save Settings after this session" checkbox): when off,
    /// changes made in this session are not written to disk.
    /// </summary>
    public bool PersistSettings { get; set; } = true;

    public void SaveSettings()
    {
        if (!PersistSettings) return;
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status.Show(L.T("AppServices.SettingsCouldNotBeSaved") + ex.Message, StatusKind.Warning);
        }
    }

    /// <summary>Applies the logging settings (level, size, retention, folder) to the running log.</summary>
    public void ApplyLogSettings()
    {
        var n = Settings.Notifications;
        AppLog.MinimumLevel = n.LogLevel;
        AppLog.MaxFileBytes = n.LogMaxMegabytesPerDay * 1024L * 1024;
        AppLog.MaxTotalBytes = Settings.Diagnostics.LogMaxTotalMegabytes * 1024L * 1024;
        if (!string.Equals(AppLog.LogDirectory, LogFolder, StringComparison.OrdinalIgnoreCase) || AppLog.RetentionDays != n.LogRetentionDays)
            AppLog.Initialize(LogFolder, n.LogRetentionDays);
    }

    /// <summary>
    /// Start-up and exit housekeeping: removes old backups beyond the retention limits and
    /// left-over temporary folders (never those of running operations). Returns a summary for the log.
    /// </summary>
    public string Housekeeping(bool atExit)
    {
        var parts = new List<string>();
        try
        {
            if (Settings.Safety.CleanTemporaryFiles)
            {
                int removed = Temp.CleanUp(atExit ? TimeSpan.Zero : TimeSpan.FromHours(12));
                if (removed > 0) parts.Add(L.F("AppServices.0Removed", L.Count(removed, "Noun.temporary_folder")));
            }
            if (!atExit)
            {
                var removed = Backups.ApplyRetention(new BackupRetention(Settings.Safety.BackupRetentionDays, Settings.Safety.BackupMaxSizeMegabytes * 1024L * 1024));
                if (removed.Count > 0) parts.Add(L.F("AppServices.0Removed", L.Count(removed.Count, "Noun.old_backup")));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Housekeeping failed: " + ex.Message);
        }
        return parts.Count == 0 ? L.T("AppServices.NothingToCleanUp") : string.Join(", ", parts);
    }

    public void Dispose()
    {
        if (_library.IsValueCreated) _library.Value.Dispose();
        Audio.Dispose();
        Http.Dispose();
        Data.Dispose();
    }
}
