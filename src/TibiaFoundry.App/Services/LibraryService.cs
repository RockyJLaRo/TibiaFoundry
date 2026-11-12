using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Library;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.StaticData;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

/// <summary>
/// The Asset Library and the processing queue for the whole program (8.8). Pages share one library;
/// every file or folder a tool saves is added to it in the background (Settings: Library → "Add
/// exports automatically"), which is how Extract Sprites, Appearances, the Outfitter, Static Data,
/// Map Tools, House Images, Sounds and the scripts feed the library without extra steps.
/// </summary>
public sealed class LibraryService : IDisposable
{
    private readonly AppServices _services;
    private readonly SemaphoreSlim _autoAdd = new(1, 1);
    private readonly string? _sessionFolder;
    private AssetLibrary? _library;
    private string? _openedFolder;

    public LibraryService(AppServices services)
    {
        _services = services;
        // Without program folders (tests, previews) the library lives in a temporary folder of this session, never in the user's data.
        if (services.Paths == null) _sessionFolder = Path.Combine(Path.GetTempPath(), AppIdentity.Id + "-session-library-" + Guid.NewGuid().ToString("N")[..8]);
        Queue = new ProcessingQueue(() => services.Settings.Library.QueueConcurrency,
            services.Paths == null ? null : Path.Combine(services.Paths.DataDirectory, "queue-history.jsonl"));
        Queue.Changed += job => QueueChanged?.Invoke(job);
        Queue.Finished += job =>
        {
            if (job.State == QueueJobState.Failed) AppLog.Warn($"Queue: \"{job.Title}\" failed: {job.Error}");
            else AppLog.Info($"Queue: \"{job.Title}\" {job.State.ToString().ToLowerInvariant()}" + (job.Result != null ? ": " + job.Result : "."));
        };
    }

    /// <summary>The library folder in use (the setting, or the default for this edition).</summary>
    public string Folder => _services.Settings.Library.Folder ?? _sessionFolder ?? Path.Combine(_services.Paths is { IsPortable: true } p ? p.DataDirectory : _services.LocalDataDirectory, "Library");

    public string DefaultFolder => _sessionFolder ?? Path.Combine(_services.Paths is { IsPortable: true } p ? p.DataDirectory : _services.LocalDataDirectory, "Library");

    /// <summary>Where content projects write by default ("Content Projects" under the output folder).</summary>
    public string ContentOutputFolder => _services.Settings.Library.ContentOutputFolder
                                         ?? Path.Combine(string.IsNullOrWhiteSpace(_services.Settings.OutputFolder) ? _services.LocalDataDirectory : _services.Settings.OutputFolder, "Content Projects");

    /// <summary>The library, opened on first use (and again after the folder changed). Throws when it cannot be opened.</summary>
    public AssetLibrary Library
    {
        get
        {
            string folder = Folder;
            if (_library != null && string.Equals(_openedFolder, folder, StringComparison.OrdinalIgnoreCase)) return _library;
            _library = AssetLibrary.Open(folder);
            _openedFolder = folder;
            return _library;
        }
    }

    /// <summary>The library, or null with the reason logged (for callers that can do without it).</summary>
    public AssetLibrary? TryLibrary(out string? problem)
    {
        try
        {
            problem = null;
            return Library;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            problem = ex.Message;
            AppLog.Warn("Asset Library: " + ex.Message);
            return null;
        }
    }

    /// <summary>Forgets the open library (after a restore or a change of folder).</summary>
    public void Reopen()
    {
        _library = null;
        _openedFolder = null;
        AssetLibrary.ReleaseConnections();
        LibraryChanged?.Invoke();
    }

    public ProcessingQueue Queue { get; }

    /// <summary>Raised (on a worker thread or the UI thread) when a queue job changed.</summary>
    public event Action<QueueJob>? QueueChanged;

    /// <summary>Raised when assets were added or changed outside the Asset Library page (automatic adds, workflows, restores).</summary>
    public event Action? LibraryChanged;

    public void RaiseLibraryChanged() => LibraryChanged?.Invoke();

    /// <summary>The name of the page in use, for the "source tool" of automatically added exports (set by the main window).</summary>
    public Func<string?>? CurrentTool { get; set; }

    /// <summary>
    /// Adds a saved file or folder to the library in the background. Folders with more than
    /// <see cref="Core.Settings.LibrarySettings.AutoAddMaxFiles"/> files are not added automatically; the status
    /// bar says how to add them. Failures are logged and never interrupt the tool that saved.
    /// </summary>
    public void OnSaved(StatusLink link)
    {
        var settings = _services.Settings.Library;
        if (!settings.AddExportsAutomatically) return;
        string tool = CurrentTool?.Invoke() ?? "Tibia Foundry"; // loc: identity (the tool's English name is stored)
        if (tool is "Asset Library" or "Content Studio") return; // they add their own files
        _ = Task.Run(async () =>
        {
            await _autoAdd.WaitAsync().ConfigureAwait(false);
            try
            {
                var lib = TryLibrary(out _);
                if (lib == null) return;
                // The library's own files (backups, collection exports) are not assets.
                if (PathGuard.IsSameOrInside(lib.Folder, link.Path)) return;
                var options = new IndexOptions
                {
                    Source = L.F("Library.0Export", tool), SourceTool = tool, ClientVersion = _services.Data.ClientVersion, Collection = settings.ExportCollection,
                    MaxFiles = settings.AutoAddMaxFiles + 1,
                };
                IReadOnlyList<string> files;
                if (link.IsFile)
                {
                    if (!MediaTypes.IsIndexed(Path.GetExtension(link.Path), includeData: false)) return;
                    files = [link.Path];
                }
                else
                {
                    if (!Directory.Exists(link.Path)) return;
                    var found = AssetIndexer.Enumerate(link.Path, options, CancellationToken.None, out bool truncated);
                    if (truncated || found.Count > settings.AutoAddMaxFiles)
                    {
                        AppLog.Info($"Asset Library: {link.Path} was not added automatically (more than {settings.AutoAddMaxFiles:N0} files).");
                        Notice?.Invoke(L.F("Library.0HasMoreThan1N0", link.Path, settings.AutoAddMaxFiles));
                        return;
                    }
                    files = found;
                }
                if (files.Count == 0) return;
                var result = AssetIndexer.IndexFiles(lib, files, options);
                AppLog.Info($"Asset Library: {tool} export added: {result.Summary}");
                if (result.Added + result.Updated > 0) LibraryChanged?.Invoke();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
            {
                AppLog.Warn($"Asset Library: adding {link.Path} failed: {ex.Message}");
            }
            finally
            {
                _autoAdd.Release();
            }
        });
    }

    /// <summary>A message for the status bar from background work (set by the main window).</summary>
    public event Action<string>? Notice;

    /// <summary>The client in use as workflows see it (appearances, sprites and static data are loaded on demand).</summary>
    public async Task<IGameAssets> GameAssetsAsync(bool needStaticData)
    {
        var data = _services.Data;
        var appearances = await data.EnsureAppearancesAsync().ConfigureAwait(true);
        StaticDataDatabase? statics = data.StaticData;
        if (needStaticData && statics == null && data.DefaultFile(AssetTypes.StaticData) is { } path) statics = await data.LoadStaticDataAsync(path).ConfigureAwait(true);
        return new Game(appearances, data.Sprites, statics, data.ClientVersion);
    }

    private sealed record Game(AppearanceDatabase? Appearances, ISpriteSource? Sprites, StaticDataDatabase? StaticData, string? ClientVersion) : IGameAssets;

    /// <summary>The context of a project run with the current settings.</summary>
    public WorkflowContext ContextFor(ContentProject project, IGameAssets game) => new()
    {
        OutputFolder = string.IsNullOrWhiteSpace(project.OutputFolder) ? ContentOutputFolder : project.OutputFolder,
        RunName = project.Name, Profile = ExportProfiles.Find(Library.ProfilesFolder, project.ProfileId), Mode = project.OutputMode, Library = Library, Game = game,
        Creator = Attribution.CurrentCreator, LoadAppearances = AppearanceDatabase.Load,
    };

    public void Dispose()
    {
        Queue.Dispose();
        _autoAdd.Dispose();
        AssetLibrary.ReleaseConnections();
        if (_sessionFolder != null && Directory.Exists(_sessionFolder))
        {
            try { Directory.Delete(_sessionFolder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
