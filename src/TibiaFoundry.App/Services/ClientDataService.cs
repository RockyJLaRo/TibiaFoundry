using CommunityToolkit.Mvvm.ComponentModel;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Houses;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Map;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Sounds;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.StaticData;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

/// <summary>
/// Owns the client data shared by all pages: the active package (live or test server), the
/// asset catalog, the sprite repository and every loaded <c>.dat</c> database. Replaces the
/// original's public static fields (<c>MainForm.datfile</c>, <c>DatSpritesDir</c>, …).
/// Loading happens on worker threads; results are published on the caller's (UI) thread.
/// </summary>
/// <remarks>
/// Every file and folder comes from <see cref="Locations"/>: the client in use for the server (7.3:
/// the installed client, a registered folder or an imported version, see <see cref="ClientLibrary"/>),
/// with the user's choices (Settings → Client and data locations, or "Locate…" on the Compatibility
/// page) for the installed client. An imported version is read through its manifest; it never changes.
/// <para>
/// Update resilience: the service remembers which client files the loaded data came from
/// (<see cref="CheckForClientChange"/>). When the Tibia launcher updates the client while the
/// program runs, stale data are discarded instead of being mixed with the new sprite sheets
/// (sprite IDs are reassigned between versions).
/// </para>
/// </remarks>
public sealed partial class ClientDataService : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly ClientLibrary? _library;
    private readonly object _inFlightLock = new();
    private readonly Dictionary<string, Task> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private AssetCatalog? _catalog;
    private ClientSnapshot? _snapshot;
    private LocationResolver _locations;

    /// <summary>Size and time of the files whose change means "the client was updated".</summary>
    private sealed record ClientSnapshot(FileFingerprint PackageManifest, FileFingerprint Catalog);

    public ClientDataService(AppSettings settings, ClientLibrary? library = null)
    {
        _settings = settings;
        _library = library;
        _locations = settings.LocationsFor(settings.ActiveScope);
        RefreshPaths();
    }

    /// <summary>Why the client chosen for this server cannot be used right now (the installed client is used meanwhile), or null.</summary>
    [ObservableProperty] private string? _clientChoiceProblem;

    /// <summary>The imported version in use, or null (installed client or registered folder).</summary>
    public ManifestClientFiles? ImportedVersion => _locations.Imported;

    /// <summary>The Qt resource files of the client in use (an imported version may have several; the installed client has one).</summary>
    public IReadOnlyList<(string Name, string Path)> ResourceFiles => ImportedVersion is { } v
        ? v.ResourceFiles.Select(f => (f.Path, v.PathOf(f.Path)!)).ToList()
        : ResourceFile is { } file ? [(Path.GetFileName(file), file)] : [];

    /// <summary>The client's locations for a server: the library's choice when there is one.</summary>
    private LocationResolver ResolveLocations(ClientScope scope, out string? problem)
    {
        problem = null;
        return _library != null ? _library.LocationsFor(scope, out problem) : _settings.LocationsFor(scope);
    }

    public SpriteImageCache Thumbnails { get; } = new();

    [ObservableProperty] private TibiaClientPaths _client = TibiaClientPaths.ForPackage(TibiaClientPaths.LivePackageName);
    [ObservableProperty] private SpriteRepository? _sprites;
    [ObservableProperty] private AppearanceDatabase? _appearances;
    [ObservableProperty] private StaticDataDatabase? _staticData;
    [ObservableProperty] private MapDatabase? _map;
    [ObservableProperty] private StaticMapDatabase? _staticMap;
    [ObservableProperty] private SoundDatabase? _sounds;

    /// <summary>Version from the package manifest (e.g. "15.33.b21348"), or null.</summary>
    [ObservableProperty] private string? _clientVersion;

    /// <summary>Why the sprite catalog could not be used (null when fine).</summary>
    [ObservableProperty] private string? _catalogError;

    /// <summary>Latest compatibility report (null until checked, or after the client changed).</summary>
    [ObservableProperty] private CompatibilityReport? _compatibility;

    [ObservableProperty] private bool _isCheckingCompatibility;

    /// <summary>
    /// Raised (on the caller's thread) when client files in use changed: the data types affected
    /// (see <see cref="LocationIds.DataFiles"/>), or null when all of them may have changed (another
    /// client, another package or assets folder, or a client update). Pages forget the file they
    /// would load by default, so the next load uses the new one.
    /// </summary>
    public event Action<IReadOnlySet<string>?>? ClientFilesChanged;

    /// <summary>Files and folders of the client in use (the user's choices, else the installed client).</summary>
    public LocationResolver Locations => _locations;

    /// <summary>Files and folders of a client; the one in use, or a fresh resolver for the other client.</summary>
    public LocationResolver LocationsFor(ClientScope scope) => scope == _settings.ActiveScope ? _locations : ResolveLocations(scope, out _);

    /// <summary>Assets folder in use (chosen, or the package's assets folder; where it was expected when missing).</summary>
    public string AssetsDirectory => _locations.Resolve(LocationIds.Assets).DisplayPath;

    public bool AssetsAvailable => _locations.Resolve(LocationIds.Assets).Found;

    /// <summary>Sounds folder in use (chosen, or the package's sounds folder).</summary>
    public string SoundsDirectory => _locations.Resolve(LocationIds.SoundsFolder).DisplayPath;

    /// <summary>The client's Qt resource file (graphics_resources.rcc), or null when there is none.</summary>
    public string? ResourceFile => _locations.Resolve(LocationIds.ResourceFile).Path;

    public string PackageDisplayName => ImportedVersion is { } v
        ? L.F("ClientData.0Imported1", (_settings.UseTestServer ? L.T("Common.TestServer") : L.T("ClientData.LiveClient")), v.Entry.ClientVersion ?? v.Entry.Label)
        : _settings.UseTestServer ? L.F("ClientData.TestServer0", _settings.TestServerPackage) : L.T("ClientData.LiveClient");

    /// <summary>Warning about the extracted sprites folder (from another client version), or null.</summary>
    public string? SpriteFolderNotice => Sprites?.FolderNotice;

    /// <summary>Catalog of the current assets folder (null when unavailable; see <see cref="CatalogError"/>).</summary>
    public AssetCatalog? Catalog
    {
        get
        {
            if (_catalog == null && CatalogError == null && AssetsAvailable)
            {
                try
                {
                    var assets = _locations.Resolve(LocationIds.Assets);
                    _catalog = AssetCatalog.Load(assets.ClientFolder ?? new PhysicalClientFolder(AssetsDirectory));
                    foreach (string problem in _catalog.Problems.Take(20)) AppLog.Warn("Sprite catalog: " + problem);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    // Never let a changed or damaged catalog break a binding: remember why and report it.
                    CatalogError = ex.Message;
                    AppLog.Warn("catalog-content.json could not be used.", ex);
                }
            }
            return _catalog;
        }
    }

    /// <summary>Re-evaluates the package and sprite source from settings (after "Switch to test server", folder changes, client updates…).</summary>
    public void RefreshPaths()
    {
        _locations = ResolveLocations(_settings.ActiveScope, out string? problem);
        ClientChoiceProblem = problem;
        Client = new TibiaClientPaths(_locations.Resolve(LocationIds.Package).DisplayPath);
        _catalog = null;
        CatalogError = null;
        var manifest = _locations.Resolve(LocationIds.PackageManifest);
        ClientVersion = manifest.Found ? TibiaClientPaths.ReadVersion(manifest.Path!) : null;
        RebuildSprites();
        _snapshot = TakeSnapshot();
        OnPropertyChanged(nameof(Locations));
        OnPropertyChanged(nameof(AssetsDirectory));
        OnPropertyChanged(nameof(AssetsAvailable));
        OnPropertyChanged(nameof(SoundsDirectory));
        OnPropertyChanged(nameof(ResourceFile));
        OnPropertyChanged(nameof(PackageDisplayName));
        OnPropertyChanged(nameof(ImportedVersion));
        OnPropertyChanged(nameof(ResourceFiles));
        OnPropertyChanged(nameof(Catalog));
    }

    /// <summary>
    /// Switches to the other client (live or test server): everything loaded is discarded, the
    /// previous compatibility report no longer applies, and pages forget their default files.
    /// </summary>
    public void SwitchClient()
    {
        UnloadAll();
        Compatibility = null;
        RefreshPaths();
        ClientFilesChanged?.Invoke(null);
    }

    /// <summary>The files that decide which data is in use (a change means loaded data are stale).</summary>
    private static string Signature(LocationResolver locations) => string.Join("|",
        new[] { LocationIds.Package, LocationIds.PackageManifest, LocationIds.Assets }.Concat(LocationIds.DataFiles)
            .Select(id => locations.Resolve(id).DisplayPath));

    /// <summary>
    /// Looks at the disk again (a chosen drive came back, the launcher finished installing…). When
    /// the files in use changed, loaded data are discarded and pages are told. Returns true when they changed.
    /// </summary>
    public bool RefreshLocations()
    {
        var fresh = ResolveLocations(_settings.ActiveScope, out _);
        if (Signature(fresh) == Signature(_locations))
        {
            _locations = fresh;
            OnPropertyChanged(nameof(Locations));
            OnPropertyChanged(nameof(ResourceFile));
            return false;
        }
        AppLog.Info("The client files in use changed (a chosen location became available or unavailable); loaded data were discarded.");
        UnloadAll();
        RefreshPaths();
        ClientFilesChanged?.Invoke(null);
        return true;
    }

    /// <summary>
    /// Applies a location the user chose or reset (the settings are already changed): discards what
    /// was loaded from the affected files and tells the pages.
    /// </summary>
    public void ApplyLocationChange(string locationId)
    {
        var affected = LocationResolver.AffectedBy(locationId);
        var dataTypes = LocationIds.DataFiles.Where(affected.Contains).ToHashSet(StringComparer.Ordinal);
        bool everything = affected.Contains(LocationIds.Assets) || affected.Contains(LocationIds.PackageManifest);
        if (everything) UnloadAll();
        else
        {
            foreach (string type in dataTypes) Unload(type);
        }
        RefreshPaths();
        if (everything) ClientFilesChanged?.Invoke(null);
        else if (dataTypes.Count > 0) ClientFilesChanged?.Invoke(dataTypes);
    }

    public void RebuildSprites()
    {
        var repo = new SpriteRepository(Catalog, _settings.ActiveSpritesFolder, _settings.SpriteSource, ClientVersion, _settings.Performance.SpriteSheetCache)
        {
            SaveDecodedSheetsToFolder = _settings.SaveDecodedSheets,
        };
        Sprites = repo;
        Thumbnails.Source = repo;
        OnPropertyChanged(nameof(SpriteFolderNotice));
    }

    private ClientSnapshot TakeSnapshot() => new(
        FileFingerprint.Of(_locations.Resolve(LocationIds.PackageManifest).DisplayPath),
        FileFingerprint.Of(Path.Combine(AssetsDirectory, TibiaFormat.AssetCatalogFile)));

    /// <summary>
    /// Detects a client update since the data were loaded (cheap: two file stats). When the client
    /// changed, every loaded database and cache is discarded so nothing mixes old data with new
    /// sprites, and a description for the user is returned; otherwise null.
    /// </summary>
    public string? CheckForClientChange()
    {
        if (_snapshot == null) return null;
        var now = TakeSnapshot();
        if (now == _snapshot) return null;
        string? before = ClientVersion;
        var manifest = _locations.Resolve(LocationIds.PackageManifest);
        string? after = TibiaClientPaths.ReadVersion(manifest.DisplayPath);
        string message = before != after
            ? L.F("ClientData.TheTibiaClientWasUpdated0", before ?? "unknown", after ?? "unknown")
            : L.T("ClientData.TheTibiaClientSDataFiles");
        AppLog.Info(message);
        UnloadAll();
        Compatibility = null;
        RefreshPaths();
        ClientFilesChanged?.Invoke(null);
        return message;
    }

    /// <summary>File of a data type for the client in use (see <see cref="LocationIds.DataFiles"/>); null when missing.</summary>
    public string? DefaultFile(string assetType) => _locations.Resolve(assetType).Path;

    /// <summary>
    /// Runs one load per file at a time: when two pages ask for the same file concurrently (e.g.
    /// Static Data and House Images both need appearances), they share a single load.
    /// </summary>
    private Task<T> Shared<T>(string kind, string path, Func<Task<T>> load)
    {
        string key = kind + "|" + Path.GetFullPath(path);
        lock (_inFlightLock)
        {
            if (_inFlight.TryGetValue(key, out var running)) return (Task<T>)running;
            var task = load();
            if (!task.IsCompleted)
            {
                _inFlight[key] = task;
                _ = ForgetWhenDone(key, task);
            }
            return task;
        }
    }

    private async Task ForgetWhenDone(string key, Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception) { /* the caller observes and reports the failure */ }
        lock (_inFlightLock) _inFlight.Remove(key);
    }

    public Task<AppearanceDatabase> LoadAppearancesAsync(string path) => Shared("appearances", path, async () =>
    {
        var db = await Task.Run(() => AppearanceDatabase.Load(path));
        Appearances = db;
        AppLog.Info($"Loaded {Path.GetFileName(path)}: {db.Count(AppearanceKind.Object)} objects, {db.Count(AppearanceKind.Outfit)} outfits.");
        return db;
    });

    public Task<StaticDataDatabase> LoadStaticDataAsync(string path) => Shared("staticdata", path, async () =>
    {
        var db = await Task.Run(() => StaticDataDatabase.Load(path));
        StaticData = db;
        return db;
    });

    public Task<MapDatabase> LoadMapAsync(string path) => Shared("map", path, async () =>
    {
        var db = await Task.Run(() => MapDatabase.Load(path));
        Map = db;
        return db;
    });

    public Task<StaticMapDatabase> LoadStaticMapAsync(string path) => Shared("staticmapdata", path, async () =>
    {
        var db = await Task.Run(() => StaticMapDatabase.Load(path));
        StaticMap = db;
        return db;
    });

    public Task<SoundDatabase> LoadSoundsAsync(string path) => Shared("sounds", path, async () =>
    {
        // An imported version's sound files are found through its manifest, not next to the sounds file.
        var folder = _locations.Resolve(LocationIds.SoundsFolder) is { IsImported: true } imported ? imported.Folder : null;
        var db = await Task.Run(() => SoundDatabase.Load(path, folder));
        Sounds = db;
        return db;
    });

    /// <summary>Loads appearances if not loaded yet (pages that need them call this).</summary>
    public async Task<AppearanceDatabase?> EnsureAppearancesAsync()
    {
        if (Appearances != null) return Appearances;
        string? path = DefaultFile(AssetTypes.Appearances);
        return path == null ? null : await LoadAppearancesAsync(path);
    }

    /// <summary>
    /// Checks the client's data (version, integrity, decoding, schema drift, validation) on a worker
    /// thread and keeps the result in <see cref="Compatibility"/>. The disk is looked at again first,
    /// so files that appeared or disappeared since start-up are seen.
    /// </summary>
    public async Task<CompatibilityReport> CheckCompatibilityAsync(IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        IsCheckingCompatibility = true;
        try
        {
            RefreshLocations();
            var locations = _locations;
            // Only re-use appearances that were loaded from the file being checked (others are small).
            var loaded = new LoadedData(Appearances);
            var report = await Task.Run(() => CompatibilityChecker.Check(locations, loaded, progress, ct: ct), ct);
            Compatibility = report;
            return report;
        }
        finally
        {
            IsCheckingCompatibility = false;
        }
    }

    /// <summary>
    /// Checks again what a changed location affects and merges it into the current report (the
    /// whole check runs when the package, its version or the assets folder changed, or when there is
    /// no report yet).
    /// </summary>
    public async Task<CompatibilityReport> RecheckAsync(string locationId, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        var components = LocationResolver.ComponentsAffectedBy(locationId);
        var previous = Compatibility;
        if (components == null || previous == null) return await CheckCompatibilityAsync(progress, ct);
        IsCheckingCompatibility = true;
        try
        {
            var locations = _locations;
            var loaded = new LoadedData(Appearances);
            var partial = await Task.Run(() => CompatibilityChecker.Check(locations, loaded, progress, components, ct), ct);
            Compatibility = previous.WithComponents(partial);
            return Compatibility;
        }
        finally
        {
            IsCheckingCompatibility = false;
        }
    }

    /// <summary>Forgets the loaded database of one data type.</summary>
    private void Unload(string type)
    {
        switch (type)
        {
            case LocationIds.Appearances: Appearances = null; Thumbnails.Clear(); break;
            case LocationIds.StaticData: StaticData = null; break;
            case LocationIds.StaticMapData: StaticMap = null; break;
            case LocationIds.Map: Map = null; break;
            case LocationIds.Sounds: Sounds = null; break;
        }
    }

    /// <summary>Forgets all loaded databases (used when switching between live and test server, or after a client update).</summary>
    public void UnloadAll()
    {
        Appearances = null;
        StaticData = null;
        Map = null;
        StaticMap = null;
        Sounds = null;
        Thumbnails.Clear();
    }

    public void Dispose() => Thumbnails.Dispose();
}
