using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

/// <summary>
/// The client store of this installation of Tibia Foundry and the client in use per server: the
/// installed client (automatic, as before 7.3), a registered client folder, or an imported version.
/// The store is opened only when it is needed (nothing is created while every server uses its
/// installed client), and it is local only: nothing here talks to a network.
/// </summary>
/// <remarks>
/// Where the store is: the portable edition keeps it in <c>TibiaFoundry-Data\Clients</c> next to the
/// program (found again from the program's folder at every start, so the folder can be moved); the
/// folder edition in <c>%LOCALAPPDATA%\TibiaFoundry\Clients</c>, or the folder chosen in Settings →
/// Tibia client ("Client store folder"), so it survives updates that unpack into a new folder.
/// </remarks>
public sealed class ClientLibrary
{
    private readonly AppSettings _settings;
    private readonly object _gate = new();
    private string? _root;
    private string? _programDirectory;
    private ClientStore? _store;

    public ClientLibrary(AppSettings settings) => _settings = settings;

    /// <summary>The default store folder of the folder edition.</summary>
    public static string DefaultInstalledRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.Id, ClientStore.FolderName);

    /// <summary>The store folder for these program paths and settings.</summary>
    public static string RootFor(AppPaths paths, AppSettings settings) =>
        paths.IsPortable ? Path.Combine(paths.DataDirectory, ClientStore.FolderName)
            : string.IsNullOrWhiteSpace(settings.ClientStoreFolder) ? DefaultInstalledRoot : settings.ClientStoreFolder!;

    /// <summary>Sets where the store is (called once the program's paths are known; again after "Move store…").</summary>
    public void Configure(string root, string? programDirectory)
    {
        lock (_gate)
        {
            _root = Path.GetFullPath(root);
            _programDirectory = programDirectory;
            _store = null;
        }
        // The editors never save into the store or into a registered client folder.
        Core.Editing.AssetsProject.Protect(this, library => ((ClientLibrary)library).ProtectedFolders());
    }

    /// <summary>The store and every registered client folder (never written to by any tool).</summary>
    public IEnumerable<string> ProtectedFolders()
    {
        if (_root == null) yield break;
        yield return _root;
        ClientStore? store;
        try { store = ExistingStore; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        if (store == null) yield break;
        foreach (var r in store.Registry.Registrations)
            if (r.ResolveSource(_programDirectory) is { } folder) yield return folder;
    }

    /// <summary>True once <see cref="Configure"/> ran (tests without program folders never use a store).</summary>
    public bool IsConfigured => _root != null;

    public string? Root => _root;

    /// <summary>The portable program folder (registered folders on its drive are also remembered relative to it), or null.</summary>
    public string? ProgramDirectory => _programDirectory;

    /// <summary>True when a store exists at <see cref="Root"/> (checked without creating anything).</summary>
    public bool StoreExists => _root != null &&
        (File.Exists(Path.Combine(_root, ClientRegistry.FileName)) || Directory.Exists(Path.Combine(_root, ContentStore.SharedFolder)));

    /// <summary>The store, opened (and created) on first use.</summary>
    public ClientStore Store
    {
        get
        {
            lock (_gate)
            {
                if (_root == null) throw new InvalidOperationException("The client store is not available here.");
                if (_store == null)
                {
                    _store = ClientStore.Open(_root, _programDirectory);
                    foreach (string note in _store.OpenNotes) AppLog.Info("Client store: " + note);
                }
                return _store;
            }
        }
    }

    /// <summary>The store when it exists already, else null (for read-only questions such as the reminder).</summary>
    public ClientStore? ExistingStore => StoreExists ? Store : null;

    /// <summary>Version evidence the archive gives (8.0): content of stored revisions → their version; empty without a store.</summary>
    public IReadOnlyDictionary<string, string> KnownContent()
    {
        try
        {
            return ExistingStore?.KnownVersionContent() ?? new Dictionary<string, string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("The client archive could not be read for version evidence: " + ex.Message);
            return new Dictionary<string, string>();
        }
    }

    /// <summary>"shared" or "independent": how new archives are kept (Settings → Tibia client).</summary>
    public string StorageMode => _settings.ClientStorageMode == ManifestStorage.Independent ? ManifestStorage.Independent : ManifestStorage.Shared;

    /// <summary>
    /// The locations of the client in use for a server. An imported version that cannot be used
    /// (missing or damaged files) or a registered folder that is missing is reported in
    /// <paramref name="problem"/>, and the installed client is used meanwhile; the choice is kept.
    /// </summary>
    public LocationResolver LocationsFor(ClientScope scope, out string? problem)
    {
        problem = null;
        string? active = _settings.ActiveClient(scope);
        if (active == null || !IsConfigured) return _settings.LocationsFor(scope);
        try
        {
            if (active.Contains('/'))
            {
                var store = ExistingStore ?? throw new InvalidOperationException("The client store was not found (it may have been moved or deleted).");
                return _settings.LocationsFor(scope, store.OpenVersion(active), null);
            }
            var registration = ExistingStore?.Registry.Registration(active) ?? throw new InvalidOperationException("The registered client folder is no longer in the client store.");
            string? folder = registration.ResolveSource(_programDirectory);
            if (folder == null || !Directory.Exists(folder))
                throw new DirectoryNotFoundException($"The registered folder of \"{registration.Label}\" is not available ({folder}). Use Clients → Locate again….");
            return _settings.LocationsFor(scope, null, folder);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            problem = L.F("ClientLibrary.The0ClientYouChoseCannot", (scope == ClientScope.Live ? "live" : "test-server"), ex.Message);
            AppLog.Warn(problem);
            return _settings.LocationsFor(scope);
        }
    }

    /// <summary>Short description of the client in use, for the navigation bar ("Live · 15.33.9d148f (imported)").</summary>
    public string Describe(ClientScope scope, string? installedVersion)
    {
        string server = scope == ClientScope.Live ? "Live" : "Test";
        string? active = _settings.ActiveClient(scope);
        if (active != null && ExistingStore is { } store)
        {
            if (active.Contains('/') && store.Registry.Version(active) is { } v) return L.F("ClientLibrary.01Archived", server, v.ClientVersion ?? v.Label);
            if (store.Registry.Registration(active) is { } r)
                return $"{server} · {r.Label} ({(r.EffectiveSourceType == SourceTypes.Reconstructed ? "rebuilt installation" : "folder")})";
        }
        return L.F("ClientLibrary.01Installed", server, installedVersion ?? L.T("ClientLibrary.VersionUnknown"));
    }

    /// <summary>The package folder of the installed (automatic) client of a server, as Tibia Foundry finds it today.</summary>
    public string InstalledPackageFolder(ClientScope scope) => _settings.LocationsFor(scope).Resolve(LocationIds.Package).DisplayPath;

    // ------------------------------------------------------------------ launch check (8.0)

    /// <summary>Is the launcher of the installation with this package folder running? (Tests replace it.)</summary>
    public Func<string, bool> LauncherActive { get; set; } = LauncherActivity.IsActive;

    private readonly Dictionary<ClientScope, InstalledCheck> _checks = [];

    /// <summary>The launch check of this session for a server, or null when none ran.</summary>
    public InstalledCheck? LastCheck(ClientScope scope)
    {
        lock (_gate) return _checks.GetValueOrDefault(scope);
    }

    /// <summary>
    /// Checks the installed client of a server (8.0): reads it completely (or, in quick mode, the files
    /// whose size or time changed), compares it with the baseline of the last check and with the
    /// archive, and records a new baseline when the check was consistent. Only reads the client;
    /// writes only the baseline (in <c>Clients\Baselines</c>, without creating the archive). Null
    /// when the server is not monitored or its client was never installed.
    /// </summary>
    public InstalledCheck? CheckInstalled(ClientScope scope, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        if (_root == null || !_settings.Monitors(scope)) return null;
        string folder = InstalledPackageFolder(scope);
        string registrationId = ClientRegistration.InstalledFor(scope);
        var baseline = ClientBaselines.Read(_root, registrationId);
        if (!Directory.Exists(folder) && baseline == null) return null;
        bool personal = _settings.ArchivePersonalData;
        var result = LaunchCheck.Run(folder, baseline, _settings.ClientLaunchCheck == ClientLaunchCheck.Quick, new InventoryOptions { IncludePersonalData = personal, Parallelism = 2 },
            KnownContent(), LauncherActive, progress, ct);
        var check = new InstalledCheck(scope, result, DateTime.UtcNow);
        if (result.Accepted)
        {
            var archived = ExistingStore?.FindStored(ClientServers.Of(scope), result.Fingerprint!);
            RecordBaseline(check, archived?.Key);
        }
        lock (_gate) _checks[scope] = check;
        AppLog.Info($"Launch check of the installed {ClientServers.Of(scope)} client: {result.Outcome}" +
                    (result.Scan is { } s ? L.F("ClientLibrary.0N0Files1N0Read", s.Files.Count, s.HashedFiles, s.Duration.TotalSeconds, (result.Quick ? L.T("ClientLibrary.Quick") : "")) : "") +
                    (result.Problems.Count > 0 ? ": " + result.Problems[0] : "."));
        return check;
    }

    private void RecordBaseline(InstalledCheck check, string? archivedKey)
    {
        try
        {
            ClientBaselines.Write(_root!, LaunchCheck.BaselineOf(check.Result, ClientRegistration.InstalledFor(check.Scope), ClientServers.Of(check.Scope),
                _settings.ArchivePersonalData, check.CheckedUtc, archivedKey));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("The result of the launch check could not be saved: " + ex.Message);
        }
    }

    /// <summary>
    /// What is known about the installed client of a server, without reading it: from this session's
    /// launch check, else from the baseline of the last one. Whether that content is archived is
    /// looked up in the archive every time (an archive made since counts). Null when nothing is known.
    /// </summary>
    public InstalledStatus? InstalledStatusOf(ClientScope scope)
    {
        if (_root == null) return null;
        string registrationId = ClientRegistration.InstalledFor(scope);
        var check = LastCheck(scope);
        bool accepted = check?.Result.Accepted == true;
        var baseline = accepted ? null : ClientBaselines.Read(_root, registrationId);
        string? fingerprint = accepted ? check!.Result.Fingerprint : baseline?.Fingerprint;
        if (fingerprint == null && check == null) return null;
        ClientVersionEntry? archived = null;
        bool dismissed = false;
        try
        {
            if (fingerprint != null && ExistingStore is { } store)
            {
                archived = store.FindStored(ClientServers.Of(scope), fingerprint);
                dismissed = store.Registry.Registration(registrationId)?.DismissedFingerprints.Contains(fingerprint) == true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Client archive: " + ex.Message);
        }
        string? version = accepted ? check!.Result.Version.Version : baseline?.Version;
        DateTime? checkedUtc = accepted ? check!.CheckedUtc : baseline?.VerifiedAtUtc;
        string? problem = check is { Result.Accepted: false } ? (check.Result.Problems.Count > 0 ? check.Result.Problems[0] : null) : null;
        return new InstalledStatus(scope, version, fingerprint, checkedUtc, archived, dismissed, problem, check?.Result);
    }

    /// <summary>"Don't ask for this version": this content is not offered again (8.0, stored per fingerprint).</summary>
    public void DismissChange(ClientScope scope, string fingerprint, string? version) =>
        Store.DismissChange(ClientRegistration.InstalledFor(scope), fingerprint, version);

    /// <summary>Archives what a launch check read (Automatic, or Archive on the notice), and records it in the baseline.</summary>
    public ImportResult ArchiveChecked(InstalledCheck check, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        var result = check.Result;
        if (!result.Accepted || result.Scan == null || result.Inspection == null) throw new InvalidOperationException("Only a consistent check can be archived.");
        string scope = result.Inspection.Class == FolderClass.CompleteInstallation ? ManifestScopes.CompleteInstallation : ManifestScopes.StandaloneFolder;
        var request = new ArchiveRequest(result.Scan, ClientServers.Of(check.Scope), scope, StorageMode, SourceTypes.InstalledPackage,
            ClientRegistry.DefaultAutomatic(check.Scope).Label, ClientRegistration.InstalledFor(check.Scope), result.Version);
        var imported = Store.Archive(request, progress, ct);
        RecordBaseline(check, imported.Version.Key);
        return imported;
    }
}

/// <summary>One launch check of an installed client.</summary>
public sealed record InstalledCheck(ClientScope Scope, LaunchCheckResult Result, DateTime CheckedUtc);

/// <summary>What is known about an installed client (see <see cref="ClientLibrary.InstalledStatusOf"/>).</summary>
/// <param name="Fingerprint">The content last read consistently, or null.</param>
/// <param name="ArchivedAs">The archived revision with exactly that content, or null.</param>
/// <param name="Dismissed">"Don't ask for this version" was chosen for that content.</param>
/// <param name="Problem">Why this session's check was not accepted (launcher running, files changing, folder missing), or null.</param>
public sealed record InstalledStatus(ClientScope Scope, string? Version, string? Fingerprint, DateTime? CheckedUtc, ClientVersionEntry? ArchivedAs, bool Dismissed,
    string? Problem, LaunchCheckResult? Check)
{
    /// <summary>Content that is not archived and not dismissed: worth a notice.</summary>
    public bool NeedsArchive => Fingerprint != null && ArchivedAs == null && !Dismissed;
}
