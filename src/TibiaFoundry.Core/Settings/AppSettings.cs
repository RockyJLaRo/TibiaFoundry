using System.Text.Json.Serialization;
using System.Text.Json;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.Theming;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Settings;

public enum ThemePreference { System, Light, Dark }

/// <summary>Saved window placement (device-independent units).</summary>
public sealed class WindowPlacement
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 820;
    public bool Maximized { get; set; }
}

/// <summary>
/// User preferences. Persisted as JSON in %APPDATA%\TibiaFoundry\settings.json (portable: TibiaFoundry-Data).
/// Contains only paths and display preferences — no secrets.
/// <para>
/// Sections added after 5.0 (<see cref="Themes"/>, <see cref="Diagnostics"/>, <see cref="OutputFolders"/>)
/// are top-level properties on purpose: version 5.0 keeps unknown top-level settings unchanged
/// when it saves, so switching back and forth between versions never loses them.
/// </para>
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Settings format written by this build. Increase it (and add a step to <see cref="Migrate"/>)
    /// when the meaning of an existing setting changes. 1 = version 2.x; 2 = versions 3.0 and 4.0
    /// (GIF author became the creator name); 3 = version 5.0 (setting categories for the new tools);
    /// 4 = version 6.0 (output folders under Documents\Tibia_Foundry, custom themes, diagnostics);
    /// 5 = version 7.0 (client and data locations chosen per client, see <see cref="ClientLocations"/>);
    /// 6 = version 7.3 (the client store: the client in use per server and the store folder, see
    /// <see cref="ActiveClientLive"/>). Format 6 only adds settings; earlier versions keep them unchanged.
    /// 7 = version 8.0 (the hotkey tool of 7.0–7.3 is removed with its settings and locations; client
    /// archiving, monitoring and the startup choice add settings).
    /// </summary>
    public const int CurrentVersion = 7;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Format the settings had when they were read (before <see cref="Migrate"/>); not saved.</summary>
    [JsonIgnore]
    public int LoadedVersion { get; set; } = CurrentVersion;

    /// <summary>What <see cref="Migrate"/> changed, in plain words, for the start-up message; not saved.</summary>
    [JsonIgnore]
    public List<string> MigrationNotes { get; } = [];

    /// <summary>
    /// Settings this build does not know (written by a newer version). They are kept and written
    /// back unchanged, so switching between versions never loses options.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownSettings { get; set; }

    // Appearance of the application
    /// <summary>The built-in theme, used when no custom theme is active (<see cref="Themes"/>).</summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    /// <summary>Display language of the interface (a code of <see cref="Localization.Languages.All"/>; English when unknown).</summary>
    public string Language { get; set; } = Localization.Languages.DefaultCode;
    public WindowPlacement MainWindow { get; set; } = new();
    public string? LastPage { get; set; }
    /// <summary>Pages opened recently, newest first (view-model type names), for the Home dashboard.</summary>
    public List<string> RecentPages { get; set; } = [];
    /// <summary>Files and folders saved recently, newest first, for the Home dashboard.</summary>
    public List<RecentExport> RecentExports { get; set; } = [];
    public bool ShowDefaultValues { get; set; }

    // Client data
    /// <summary>Use the test-server package instead of the live client (original "Switch to TS").</summary>
    public bool UseTestServer { get; set; }
    public string TestServerPackage { get; set; } = TibiaClientPaths.DefaultTestPackageName;
    /// <summary>
    /// Assets folder chosen for the live client (empty = the package's assets folder). Versions
    /// before 7.0 used it for the test server too; since settings format 5 the test server has its
    /// own (<see cref="ClientLocations"/>). Kept under this name so earlier versions still find it.
    /// </summary>
    public string? AssetsFolderOverride { get; set; }
    public bool LoadDataAutomatically { get; set; } = true;

    // Client store (format 6)
    /// <summary>
    /// The live client in use: null = the installed client (automatic, as before 7.3); a version key
    /// such as "Live_Server/Client_15.33.9d148f" = an imported version; any other value = the ID of a
    /// registered client folder. An ID, never a path, so the portable edition stays portable.
    /// </summary>
    public string? ActiveClientLive { get; set; }

    /// <summary>The test-server client in use (same values as <see cref="ActiveClientLive"/>).</summary>
    public string? ActiveClientTest { get; set; }

    /// <summary>Folder edition: where the client store is kept (null = %LOCALAPPDATA%\TibiaFoundry\Clients). The portable edition always uses TibiaFoundry-Data\Clients.</summary>
    public string? ClientStoreFolder { get; set; }

    /// <summary>
    /// Remind me when the installed client's version is not imported yet (it is lost when the launcher
    /// updates). 8.0 keeps it in step with <see cref="ClientChangeHandling"/> (false = "ask quietly"), so 7.3 reads the same choice.
    /// </summary>
    public bool ClientImportReminders { get; set; } = true;

    // Client archive (format 7, 8.0)
    /// <summary>How future archives are kept: "shared" (space-efficient, each content once) or "independent" (a physical copy of every file per revision). Existing revisions keep theirs.</summary>
    public string ClientStorageMode { get; set; } = "shared";

    /// <summary>Include hotkeys, character data, the explored minimap, screenshots, logs and caches in archives (left out, visibly, by default).</summary>
    public bool ArchivePersonalData { get; set; }

    /// <summary>What happens when the launch check finds that a monitored installation changed.</summary>
    public ClientChangeHandling ClientChangeHandling { get; set; } = ClientChangeHandling.Ask;

    /// <summary>How the launch check reads a monitored installation: every file (Full) or only files whose size or time changed (Quick).</summary>
    public ClientLaunchCheck ClientLaunchCheck { get; set; } = ClientLaunchCheck.Full;

    /// <summary>Check the installed live client at every start (8.0).</summary>
    public bool MonitorInstalledLive { get; set; } = true;

    /// <summary>Check the installed test-server client at every start (8.0).</summary>
    public bool MonitorInstalledTest { get; set; } = true;

    /// <summary>At startup: use the remembered client (the active server and its client), or ask every time.</summary>
    public StartupClientMode StartupClientMode { get; set; } = StartupClientMode.Remembered;

    /// <summary>
    /// First-run setup was completed or skipped. False only for a new installation (settings written
    /// by 7.x or earlier count as set up, so upgrading changes nothing at the first start).
    /// </summary>
    public bool ClientSetupCompleted { get; set; }

    public bool Monitors(ClientScope scope) => scope == ClientScope.Live ? MonitorInstalledLive : MonitorInstalledTest;

    /// <summary>The client in use for a server (see <see cref="ActiveClientLive"/>).</summary>
    public string? ActiveClient(ClientScope scope) => scope == ClientScope.Live ? ActiveClientLive : ActiveClientTest;

    public void SetActiveClient(ClientScope scope, string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (scope == ClientScope.Live) ActiveClientLive = value;
        else ActiveClientTest = value;
    }

    // Sprites
    public string? SpritesFolder { get; set; }
    public string? SpritesFolderTestServer { get; set; }
    public SpriteSourceMode SpriteSource { get; set; } = SpriteSourceMode.FolderThenClient;
    /// <summary>Write sheets decoded on demand into the sprites folder (original "Extract on the fly").</summary>
    public bool SaveDecodedSheets { get; set; }

    // Output locations
    /// <summary>
    /// Root of all output folders (default <c>Documents\Tibia_Foundry</c>); each kind of output has a
    /// subfolder here unless a folder of its own is set (<see cref="ExplicitFolder"/>).
    /// </summary>
    public string OutputFolder { get; set; } = DefaultOutputFolder;
    public string? SoundsFolder { get; set; }
    public string? GifFolder { get; set; }
    public string? MapTilesFolder { get; set; }
    public string? SpriteExtractionFolder { get; set; }

    // Creator attribution, embedded in every exported image (see Metadata.Attribution)
    public string CreatorName { get; set; } = Attribution.DefaultCreator;

    /// <summary>
    /// The GIF comment author of settings format 1 (version 2.x). Read once and moved into
    /// <see cref="CreatorName"/> by <see cref="Migrate"/>; never written again.
    /// </summary>
    [JsonPropertyName("GifAuthor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyGifAuthor { get; set; }

    // GIF export
    public bool GifAutoName { get; set; } = true;
    public GifDelayMode GifDelayMode { get; set; } = GifDelayMode.Average;
    public int GifNoAnimationDelay { get; set; } = 1000;
    public int GifExtraDelay { get; set; }
    public bool GifExtraFrame { get; set; }
    /// <summary>Whole-number nearest-neighbor enlargement of exported GIFs (1 = the client's own pixels; 2–4 for galleries and references).</summary>
    public int GifScale { get; set; } = 1;
    /// <summary>Automatically named GIFs go into a subfolder per kind (Objects, Outfits, Effects, Missiles) of the GIF folder.</summary>
    public bool GifKindFolders { get; set; }
    /// <summary>The fansite export preset chosen last on the Appearances page (<see cref="GifExportPresets"/>; null = custom).</summary>
    public string? GifPreset { get; set; }

    // Misc
    public bool SoundAutoplay { get; set; }
    public double SoundVolume { get; set; } = 0.8;

    // Setting categories added in format 3 (Tibia Foundry 5.0)
    public GeneralSettings General { get; set; } = new();
    public InterfaceSettings Interface { get; set; } = new();
    public FileLocationSettings Locations { get; set; } = new();
    public SafetySettings Safety { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public ScriptSettings Scripts { get; set; } = new();

    /// <summary>
    /// Options each tool remembers between sessions and the settings tools declare in their manifests
    /// ("tool.key" → value, see <c>Tools.ToolSettingsStore</c>), for example the last conversion profile.
    /// </summary>
    public Dictionary<string, string> ToolState { get; set; } = new(StringComparer.Ordinal);

    // Sections added in format 4 (top level, so version 5.0 keeps them; see the class summary)
    /// <summary>Custom themes and the active one (Settings → Appearance &amp; Themes).</summary>
    public ThemeSettings Themes { get; set; } = new();

    /// <summary>Monitoring, operation history and diagnostics reports (Diagnostics page).</summary>
    public DiagnosticsSettings Diagnostics { get; set; } = new();

    /// <summary>The Asset Library and Content Studio (8.8; Asset Library → Library settings).</summary>
    public LibrarySettings Library { get; set; } = new();

    /// <summary>
    /// Folders chosen for the kinds of output that have no older setting of their own (kind name →
    /// folder); the other kinds keep their 5.0 settings (<see cref="ExplicitFolder"/>).
    /// </summary>
    public Dictionary<string, string> OutputFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Section added in format 5 (top level, so versions 5.0 and 6.0 keep it)
    /// <summary>
    /// Client files and folders the user chose (Settings → Client and data locations, or "Locate…" on
    /// the Compatibility page): <c>"live:&lt;location&gt;"</c>, <c>"test:&lt;location&gt;"</c>, or the plain
    /// location ID for the packages folder both clients share (see <c>Locations.LocationIds</c>) → path.
    /// The live assets folder and the sprites folders keep their older settings
    /// (<see cref="AssetsFolderOverride"/>, <see cref="SpritesFolder"/>, <see cref="SpritesFolderTestServer"/>).
    /// </summary>
    public Dictionary<string, string> ClientLocations { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The client the program reads now.</summary>
    [JsonIgnore]
    public ClientScope ActiveScope => UseTestServer ? ClientScope.TestServer : ClientScope.Live;

    /// <summary>Package folder name of a client ("Tibia" or the test-server package).</summary>
    public string PackageNameFor(ClientScope scope) => scope == ClientScope.Live ? TibiaClientPaths.LivePackageName : TestServerPackage;

    private static string LocationKey(string id, ClientScope scope) =>
        !LocationResolver.Definition(id).PerClient ? id : (scope == ClientScope.Live ? "live:" : "test:") + id;

    /// <summary>The file or folder the user chose for a location of a client, or null (automatic).</summary>
    public string? ChosenLocation(string id, ClientScope scope)
    {
        string? value = (id, scope) switch
        {
            (LocationIds.Assets, ClientScope.Live) => AssetsFolderOverride,
            (LocationIds.SpritesFolder, ClientScope.Live) => SpritesFolder,
            (LocationIds.SpritesFolder, ClientScope.TestServer) => SpritesFolderTestServer,
            _ => ClientLocations.GetValueOrDefault(LocationKey(id, scope)),
        };
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Sets (or with null resets to automatic) the file or folder of a location of a client.</summary>
    public void SetChosenLocation(string id, ClientScope scope, string? path)
    {
        path = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        switch (id, scope)
        {
            case (LocationIds.Assets, ClientScope.Live): AssetsFolderOverride = path; break;
            case (LocationIds.SpritesFolder, ClientScope.Live): SpritesFolder = path; break;
            case (LocationIds.SpritesFolder, ClientScope.TestServer): SpritesFolderTestServer = path; break;
            default:
                string key = LocationKey(id, scope);
                if (path == null) ClientLocations.Remove(key);
                else ClientLocations[key] = path;
                break;
        }
    }

    /// <summary>Every location the user chose for a client (location ID → path).</summary>
    public IReadOnlyDictionary<string, string> ChosenLocations(ClientScope scope)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var def in LocationResolver.All)
            if (ChosenLocation(def.Id, scope) is { } path) result[def.Id] = path;
        return result;
    }

    /// <summary>
    /// Resolver of a client's files and folders with the user's choices. The resource file always
    /// gets a compatibility row; the extracted sprites folder only when one is set.
    /// </summary>
    public LocationResolver LocationsFor(ClientScope scope) => LocationsFor(scope, null, null);

    /// <summary>
    /// Resolver for the client in use of a server (7.3): an imported version (read through its
    /// manifest), a registered client folder (only that folder: choices made for the installed client
    /// do not apply to it), or, when both are null, the installed client with the user's choices.
    /// The extracted sprites folder and the packages folder apply to all three.
    /// </summary>
    public LocationResolver LocationsFor(ClientScope scope, Clients.ManifestClientFiles? imported, string? registeredFolder)
    {
        var chosen = ChosenLocations(scope);
        var optional = new List<string> { LocationIds.ResourceFile };
        if (chosen.ContainsKey(LocationIds.SpritesFolder)) optional.Add(LocationIds.SpritesFolder);
        if (registeredFolder != null)
        {
            var own = chosen.Where(kv => kv.Key is LocationIds.SpritesFolder or LocationIds.PackagesFolder)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            own[LocationIds.Package] = registeredFolder;
            return new LocationResolver(scope, PackageNameFor(scope), own, reportedOptional: optional);
        }
        return new LocationResolver(scope, PackageNameFor(scope), chosen, reportedOptional: optional, imported: imported);
    }

    /// <summary>The default root of the output folders: <c>Documents\Tibia_Foundry</c> ("" when Windows reports no Documents folder).</summary>
    public static string DefaultOutputFolder => OutputLocations.DefaultRoot;

    public string EffectiveSoundsFolder => OutputFolderFor(OutputKind.SoundExports);
    public string EffectiveGifFolder => OutputFolderFor(OutputKind.GifExports);
    public string EffectiveMapTilesFolder => OutputFolderFor(OutputKind.MapExports);
    public string EffectiveSpriteExtractionFolder => OutputFolderFor(OutputKind.SpriteExports);

    /// <summary>The folder chosen for a kind of output in Settings, or null when the kind uses its default.</summary>
    public string? ExplicitFolder(OutputKind kind)
    {
        string? value = kind switch
        {
            OutputKind.GifExports => GifFolder,
            OutputKind.SoundExports => SoundsFolder,
            OutputKind.MapExports => MapTilesFolder,
            OutputKind.SpriteExports => SpriteExtractionFolder,
            OutputKind.RccExports or OutputKind.ClientConverter or OutputKind.AssetsEditor or OutputKind.Scripts =>
                Locations.Tools.TryGetValue(ToolKeys.ForOutput(kind)!, out var t) ? t.Output : null,
            OutputKind.Backups => Locations.BackupFolder,
            OutputKind.Logs => Locations.LogFolder,
            OutputKind.Temporary => Locations.TempFolder,
            _ => OutputFolders.GetValueOrDefault(kind.ToString()),
        };
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Sets (or with null clears) the folder of a kind of output.</summary>
    public void SetExplicitFolder(OutputKind kind, string? folder)
    {
        folder = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim();
        switch (kind)
        {
            case OutputKind.GifExports: GifFolder = folder; break;
            case OutputKind.SoundExports: SoundsFolder = folder; break;
            case OutputKind.MapExports: MapTilesFolder = folder; break;
            case OutputKind.SpriteExports: SpriteExtractionFolder = folder; break;
            case OutputKind.RccExports or OutputKind.ClientConverter or OutputKind.AssetsEditor or OutputKind.Scripts:
                string tool = ToolKeys.ForOutput(kind)!;
                if (!Locations.Tools.TryGetValue(tool, out var t)) Locations.Tools[tool] = t = new ToolFolderSettings();
                t.Output = folder;
                if (t.Input == null && t.Output == null) Locations.Tools.Remove(tool);
                break;
            case OutputKind.Backups: Locations.BackupFolder = folder; break;
            case OutputKind.Logs: Locations.LogFolder = folder; break;
            case OutputKind.Temporary: Locations.TempFolder = folder; break;
            default:
                if (folder == null) OutputFolders.Remove(kind.ToString());
                else OutputFolders[kind.ToString()] = folder;
                break;
        }
    }

    /// <summary>
    /// Folder a kind of output is written to (for the kinds under the output root). Precedence: the
    /// folder set for it in Settings, then the folder last used with it (when "remember last
    /// folders" is on), then its subfolder of <see cref="OutputFolder"/>. Logs, backups and temporary
    /// files depend on the edition and are resolved by the application.
    /// </summary>
    public string OutputFolderFor(OutputKind kind)
    {
        if (ExplicitFolder(kind) is { } explicitFolder) return explicitFolder;
        if (General.RememberLastFolders && General.LastFolders.TryGetValue(LastFolderKey(kind), out var last) && !string.IsNullOrWhiteSpace(last)) return last;
        return DefaultFolderFor(kind);
    }

    /// <summary>The default folder of a kind: its subfolder of the output root.</summary>
    public string DefaultFolderFor(OutputKind kind) => string.IsNullOrWhiteSpace(OutputFolder) ? "" : OutputLocations.DefaultFor(kind, OutputFolder);

    /// <summary>Key of the folder last used with a kind of output (the tools' 5.0 keys are kept).</summary>
    public static string LastFolderKey(OutputKind kind) => (ToolKeys.ForOutput(kind) ?? kind.ToString()) + ".output";

    /// <summary>Remembers the folder a file of this kind was last saved in (when remembering is on).</summary>
    public void RememberOutputFolder(OutputKind kind, string? folder)
    {
        if (!General.RememberLastFolders || string.IsNullOrWhiteSpace(folder) || ExplicitFolder(kind) != null) return;
        General.LastFolders[LastFolderKey(kind)] = folder;
    }

    /// <summary>Sprites folder of the active package (live/test), or null.</summary>
    public string? ActiveSpritesFolder => UseTestServer ? SpritesFolderTestServer : SpritesFolder;

    public string ActivePackage => UseTestServer ? TestServerPackage : TibiaClientPaths.LivePackageName;

    /// <summary>
    /// Folder a tool writes to. Precedence: the tool's own folder set in Settings, then the folder
    /// last used with that tool (when "remember last folders" is on), then a subfolder of the default
    /// output folder.
    /// </summary>
    public string OutputFolderFor(string tool) => OutputFolderFor(ToolKeys.OutputKindOf(tool));

    /// <summary>Folder a tool's pickers start in: the tool's own input folder, then the folder last used; null = the tool's own default.</summary>
    public string? InputFolderFor(string tool)
    {
        if (Locations.Tools.TryGetValue(tool, out var t) && !string.IsNullOrWhiteSpace(t.Input)) return t.Input;
        if (General.RememberLastFolders && General.LastFolders.TryGetValue(tool + ".input", out var last) && !string.IsNullOrWhiteSpace(last)) return last;
        return null;
    }

    /// <summary>Remembers a folder chosen in a tool ("input" or "output"); does nothing when remembering is off.</summary>
    public void RememberFolder(string tool, string kind, string? folder)
    {
        if (!General.RememberLastFolders || string.IsNullOrWhiteSpace(folder)) return;
        General.LastFolders[tool + "." + kind] = folder;
    }

    /// <summary>The file safety options in effect for an operation that starts now.</summary>
    public FileSafetyOptions FileSafety => new()
    {
        WhenExists = Safety.WhenOutputExists,
        BackupBeforeOverwrite = Safety.BackupBeforeChanges,
        ValidateOutputs = Safety.ValidateOutputs,
    };

    /// <summary>Threads for parallel work inside one operation (Settings → Performance; 0 = one per processor core).</summary>
    public int EffectiveWorkerThreads => Performance.WorkerThreads > 0 ? Performance.WorkerThreads : Environment.ProcessorCount;

    /// <summary>Clamps values loaded from disk into valid ranges.</summary>
    public void Normalize()
    {
        MainWindow ??= new WindowPlacement();
        Language = Localization.Languages.Find(Language).Code;
        if (!(MainWindow.Width is >= 400 and <= 20000)) MainWindow.Width = 1280;
        if (!(MainWindow.Height is >= 300 and <= 20000)) MainWindow.Height = 820;
        if (string.IsNullOrWhiteSpace(OutputFolder)) OutputFolder = DefaultOutputFolder;
        OutputFolders ??= new(StringComparer.OrdinalIgnoreCase);
        foreach (var key in OutputFolders.Keys.ToList())
            if (string.IsNullOrWhiteSpace(OutputFolders[key])) OutputFolders.Remove(key);
        ClientLocations = ClientLocations == null ? new(StringComparer.OrdinalIgnoreCase) : new(ClientLocations, StringComparer.OrdinalIgnoreCase);
        foreach (var key in ClientLocations.Keys.ToList())
            if (string.IsNullOrWhiteSpace(ClientLocations[key])) ClientLocations.Remove(key);
            else ClientLocations[key] = ClientLocations[key].Trim();
        if (string.IsNullOrWhiteSpace(TestServerPackage)) TestServerPackage = TibiaClientPaths.DefaultTestPackageName;
        SetActiveClient(ClientScope.Live, ActiveClientLive);
        SetActiveClient(ClientScope.TestServer, ActiveClientTest);
        if (string.IsNullOrWhiteSpace(ClientStoreFolder)) ClientStoreFolder = null;
        GifNoAnimationDelay = Math.Clamp(GifNoAnimationDelay, 0, 60000);
        GifExtraDelay = Math.Clamp(GifExtraDelay, 0, 60000);
        GifScale = Math.Clamp(GifScale, 1, GifExportOptions.MaxScale);
        if (GifPreset != null && GifExportPresets.Find(GifPreset) == null) GifPreset = null;
        RecentPages = (RecentPages ?? []).Where(p => !string.IsNullOrWhiteSpace(p) && !RemovedPages.Contains(p, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal).Take(MaxRecentPages).ToList();
        RecentExports = (RecentExports ?? []).Where(e => e != null && !string.IsNullOrWhiteSpace(e.Path))
            .DistinctBy(e => e.Path, StringComparer.OrdinalIgnoreCase).Take(MaxRecentExports).ToList();
        SoundVolume = double.IsFinite(SoundVolume) ? Math.Clamp(SoundVolume, 0, 1) : 0.8;
        if (!Enum.IsDefined(Theme)) Theme = ThemePreference.System;
        if (!Enum.IsDefined(SpriteSource)) SpriteSource = SpriteSourceMode.FolderThenClient;
        if (!Enum.IsDefined(GifDelayMode)) GifDelayMode = GifDelayMode.Average;
        if (ClientStorageMode is not ("shared" or "independent")) ClientStorageMode = "shared";
        if (!Enum.IsDefined(ClientChangeHandling)) ClientChangeHandling = ClientChangeHandling.Ask;
        if (!Enum.IsDefined(ClientLaunchCheck)) ClientLaunchCheck = ClientLaunchCheck.Full;
        if (!Enum.IsDefined(StartupClientMode)) StartupClientMode = StartupClientMode.Remembered;
        CreatorName = Attribution.NormalizeCreator(CreatorName) ?? Attribution.DefaultCreator;
        (General ??= new()).Normalize();
        (Interface ??= new()).Normalize();
        (Locations ??= new()).Normalize();
        (Safety ??= new()).Normalize();
        (Performance ??= new()).Normalize();
        (Notifications ??= new()).Normalize();
        (Scripts ??= new()).Normalize();
        (Themes ??= new()).Normalize();
        (Diagnostics ??= new()).Normalize();
        (Library ??= new()).Normalize();
        ToolState ??= new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Upgrades settings written by an older format (never downgrades newer ones).</summary>
    public void Migrate()
    {
        LoadedVersion = Version;
        if (Version < 2)
        {
            // Format 1: the GIF author becomes the creator name. The old default author value counts
            // as "not set", so the default creator applies instead.
            if (Attribution.NormalizeCreator(LegacyGifAuthor) is { } author &&
                !Common.AppIdentity.Legacy.IsDefaultGifAuthor(author))
                CreatorName = author;
            Version = 2;
        }
        if (Version < 3)
        {
            // Format 3 only adds the setting categories; their defaults keep the 4.0 behavior.
            Version = 3;
        }
        if (Version < 4)
        {
            MigrateOutputFolders();
            Version = 4;
        }
        if (Version < 5)
        {
            // Format 5: the live client and the test server have their own assets folder. Until 6.0 one
            // chosen folder applied to both, so the test server starts with the same folder.
            if (ChosenLocation(LocationIds.Assets, ClientScope.Live) is { } assets && ChosenLocation(LocationIds.Assets, ClientScope.TestServer) == null)
            {
                SetChosenLocation(LocationIds.Assets, ClientScope.TestServer, assets);
                MigrationNotes.Add(L.F("AppSettings.YourClientAssetsFolder0Is", assets));
            }
            Version = 5;
        }
        if (Version < 6)
        {
            // Format 6 only adds the client store settings; their defaults keep the 7.2 behavior (the installed clients).
            Version = 6;
        }
        if (Version < 7)
        {
            RemoveSettingsOfRemovedTools();
            // Upgrading changes nothing at the first start: no setup, and the reminder choice carries over.
            ClientSetupCompleted = true;
            ClientChangeHandling = ClientImportReminders ? ClientChangeHandling.Ask : ClientChangeHandling.AskQuietly;
            Version = 7;
        }
        LegacyGifAuthor = null;
    }

    /// <summary>How many recently opened pages and saved files the Home dashboard remembers.</summary>
    public const int MaxRecentPages = 8, MaxRecentExports = 10;

    /// <summary>Records a page that was opened (newest first).</summary>
    public void RememberPage(string page)
    {
        RecentPages.Remove(page);
        RecentPages.Insert(0, page);
        if (RecentPages.Count > MaxRecentPages) RecentPages.RemoveRange(MaxRecentPages, RecentPages.Count - MaxRecentPages);
    }

    /// <summary>Records a file or folder that was saved (newest first).</summary>
    public void RememberExport(string path, bool isFile, DateTime savedUtc)
    {
        RecentExports.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        RecentExports.Insert(0, new RecentExport { Path = path, IsFile = isFile, SavedUtc = savedUtc });
        if (RecentExports.Count > MaxRecentExports) RecentExports.RemoveRange(MaxRecentExports, RecentExports.Count - MaxRecentExports);
    }

    /// <summary>Settings prefixes and page names of tools that 8.0 removed.</summary>
    internal static readonly string[] RemovedToolPrefixes = ["ItemDiscovery."];
    internal static readonly string[] RemovedPages = ["ItemDiscoveryViewModel"];
    /// <summary>Location IDs 8.0 removed with the hotkey tool (the client's options file and screenshots folder).</summary>
    internal static readonly string[] RemovedLocations = ["client-options", "screenshots"];

    /// <summary>
    /// Format 7: the settings of the hotkey tool removed in 8.0 are dropped (its options, the two
    /// client locations only it used, and a remembered start page that no longer exists). Nothing
    /// else changes; the file as it was is kept by <see cref="SettingsStore"/> before the first save.
    /// </summary>
    private void RemoveSettingsOfRemovedTools()
    {
        int removed = 0;
        foreach (string key in ToolState.Keys.Where(k => RemovedToolPrefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal))).ToList())
            removed += ToolState.Remove(key) ? 1 : 0;
        foreach (string key in ClientLocations.Keys.Where(k => RemovedLocations.Contains(k[(k.IndexOf(':') + 1)..], StringComparer.Ordinal)).ToList())
            removed += ClientLocations.Remove(key) ? 1 : 0;
        if (LastPage != null && RemovedPages.Contains(LastPage, StringComparer.Ordinal)) LastPage = null;
        if (General.StartupPageName != null && RemovedPages.Contains(General.StartupPageName, StringComparer.Ordinal)) General.StartupPageName = null;
        if (removed > 0) MigrationNotes.Add(L.T("AppSettings.ItemDiscoveryWasRemovedInTibia"));
    }

    /// <summary>
    /// Format 4: output goes under <c>Documents\Tibia_Foundry</c>. A root that was still the old
    /// automatic default (<c>Documents\TibiaSprites</c>) moves to the new root, and remembered
    /// "last used" folders that were only old default subfolders are forgotten. Folders the user
    /// chose are kept. No files are moved: earlier exports stay where they are.
    /// </summary>
    private void MigrateOutputFolders()
    {
        string? oldRoot = OutputLocations.PreviousDefaultRoot;
        string root = OutputFolder;
        if (oldRoot != null && SamePath(root, oldRoot) && OutputLocations.DefaultRoot.Length > 0)
        {
            OutputFolder = OutputLocations.DefaultRoot;
            MigrationNotes.Add(L.F("AppSettings.NewExportsAreSavedUnder0", OutputFolder, oldRoot));
        }
        else if (!string.IsNullOrWhiteSpace(root))
        {
            MigrationNotes.Add(L.F("AppSettings.YourOutputFolder0IsKept", root));
        }
        // 5.0 defaults: <root>\RCC, \ClientConverter, \AssetsEditor, \Scripts.
        var previousSubfolders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ToolKeys.RccEditor] = "RCC", [ToolKeys.ClientConverter] = "ClientConverter", [ToolKeys.AssetsEditor] = "AssetsEditor", [ToolKeys.Scripts] = "Scripts",
        };
        foreach (var (tool, sub) in previousSubfolders)
        {
            string key = tool + ".output";
            if (!General.LastFolders.TryGetValue(key, out var last)) continue;
            if ((!string.IsNullOrWhiteSpace(root) && SamePath(last, Path.Combine(root, sub))) || (oldRoot != null && SamePath(last, Path.Combine(oldRoot, sub))))
                General.LastFolders.Remove(key);
        }
    }

    private static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return string.Equals(PathGuard.Normalize(a), PathGuard.Normalize(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

/// <summary>Keys of the tools that have their own folders and remembered options.</summary>
public static class ToolKeys
{
    public const string RccEditor = "RccEditor";
    public const string ClientConverter = "ClientConverter";
    public const string AssetsEditor = "AssetsEditor";
    public const string Scripts = "Scripts";

    public static IReadOnlyList<string> All { get; } = [RccEditor, ClientConverter, AssetsEditor, Scripts];

    public static string DisplayName(string tool) => tool switch
    {
        RccEditor => L.T("AppSettings.RCCEditor"),
        ClientConverter => L.T("AppSettings.ClientConverter"),
        AssetsEditor => L.T("AppSettings.AssetsEditor"),
        Scripts => "Scripts",
        _ => tool,
    };

    /// <summary>The kind of output a tool writes.</summary>
    public static OutputKind OutputKindOf(string tool) => tool switch
    {
        RccEditor => FileOps.OutputKind.RccExports,
        ClientConverter => FileOps.OutputKind.ClientConverter,
        AssetsEditor => FileOps.OutputKind.AssetsEditor,
        Scripts => FileOps.OutputKind.Scripts,
        _ => throw new ArgumentException($"Unknown tool \"{tool}\".", nameof(tool)),
    };

    /// <summary>The tool whose 5.0 folder settings hold a kind of output, or null.</summary>
    public static string? ForOutput(OutputKind kind) => kind switch
    {
        FileOps.OutputKind.RccExports => RccEditor,
        FileOps.OutputKind.ClientConverter => ClientConverter,
        FileOps.OutputKind.AssetsEditor => AssetsEditor,
        FileOps.OutputKind.Scripts => Scripts,
        _ => null,
    };
}

/// <summary>Which page the program opens on.</summary>
public enum StartupPageMode { LastUsed, Specific }

/// <summary>A file or folder saved by one of the tools (listed on the Home dashboard).</summary>
public sealed class RecentExport
{
    public string Path { get; set; } = "";
    public bool IsFile { get; set; }
    public DateTime SavedUtc { get; set; }
}

/// <summary>What the launch check does when a monitored installation changed (8.0).</summary>
public enum ClientChangeHandling
{
    /// <summary>Archive the new content automatically once a complete, consistent scan shows the change.</summary>
    Automatic,
    /// <summary>Tell me (a status message on the main window) and let me archive, postpone or dismiss.</summary>
    Ask,
    /// <summary>Only mark it on the Clients page (no status message); 7.3's "reminders off".</summary>
    AskQuietly,
}

/// <summary>How much the launch check reads (8.0).</summary>
public enum ClientLaunchCheck
{
    /// <summary>Every file is hashed: no change can be missed.</summary>
    Full,
    /// <summary>Only files whose size or time changed, new files and the client's own lists are hashed; a change that keeps both size and time can be missed.</summary>
    Quick,
}

/// <summary>Which client is used at startup (8.0).</summary>
public enum StartupClientMode
{
    /// <summary>The remembered client (the active server and its client) without asking.</summary>
    Remembered,
    /// <summary>Ask every time (a remembered choice is preselected).</summary>
    Ask,
}

/// <summary>General behavior (Settings → General).</summary>
public sealed class GeneralSettings
{
    public StartupPageMode StartupPage { get; set; } = StartupPageMode.LastUsed;

    /// <summary>Page opened at start-up when <see cref="StartupPage"/> is <see cref="StartupPageMode.Specific"/> (view-model type name).</summary>
    public string? StartupPageName { get; set; }

    /// <summary>File and folder pickers start in the folder last used with each tool.</summary>
    public bool RememberLastFolders { get; set; } = true;

    /// <summary>Folders last used ("tool.input"/"tool.output" → folder).</summary>
    public Dictionary<string, string> LastFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The editors reopen the file or folder they had open when they are next shown.</summary>
    public bool ReopenLastFiles { get; set; }

    /// <summary>Files last opened in the editors (tool → path).</summary>
    public Dictionary<string, string> LastFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Clamps the section's values into valid ranges.</summary>
    public void Normalize()
    {
        LastFolders ??= new(StringComparer.OrdinalIgnoreCase);
        LastFiles ??= new(StringComparer.OrdinalIgnoreCase);
        if (!Enum.IsDefined(StartupPage)) StartupPage = StartupPageMode.LastUsed;
    }
}

/// <summary>How the navigation bar is shown.</summary>
public enum NavigationLayout
{
    /// <summary>With names, collapsing to icons when the window is narrow (the 4.0 behavior).</summary>
    Automatic,
    /// <summary>Always with names.</summary>
    Expanded,
    /// <summary>Always icons only.</summary>
    Compact,
}

/// <summary>Look of the interface (Settings → Appearance; the theme itself is <see cref="AppSettings.Theme"/>).</summary>
public sealed class InterfaceSettings
{
    public const double MinScale = 0.8;
    public const double MaxScale = 1.5;

    /// <summary>Zoom of the whole interface (1 = 100 %).</summary>
    public double Scale { get; set; } = 1.0;

    public NavigationLayout Navigation { get; set; } = NavigationLayout.Automatic;

    /// <summary>Restore the window's size, position and maximized state at start-up.</summary>
    public bool RememberWindowPlacement { get; set; } = true;

    /// <summary>Play animations in previews (sprites, outfits, editor previews).</summary>
    public bool AnimatePreviews { get; set; } = true;

    /// <summary>Size of the thumbnails in the editors' lists, in pixels.</summary>
    public int ThumbnailSize { get; set; } = 32;

    public static IReadOnlyList<int> ThumbnailSizes { get; } = [32, 48, 64];

    /// <summary>Clamps the section's values into valid ranges.</summary>
    public void Normalize()
    {
        Scale = double.IsFinite(Scale) ? Math.Clamp(Math.Round(Scale, 2), MinScale, MaxScale) : 1.0;
        if (!Enum.IsDefined(Navigation)) Navigation = NavigationLayout.Automatic;
        if (!ThumbnailSizes.Contains(ThumbnailSize)) ThumbnailSize = 32;
    }
}

/// <summary>A tool's own input and output folders (empty = use the defaults).</summary>
public sealed class ToolFolderSettings
{
    public string? Input { get; set; }
    public string? Output { get; set; }
}

/// <summary>Where files go (Settings → Files and folders).</summary>
public sealed class FileLocationSettings
{
    /// <summary>Backups of replaced and removed files (empty = the default folder next to the logs).</summary>
    public string? BackupFolder { get; set; }

    /// <summary>Temporary working files (empty = the default temporary folder).</summary>
    public string? TempFolder { get; set; }

    /// <summary>Log files (empty = the default log folder).</summary>
    public string? LogFolder { get; set; }

    /// <summary>Per-tool folders (tool key → folders); they take precedence over the defaults.</summary>
    public Dictionary<string, ToolFolderSettings> Tools { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Clamps the section's values into valid ranges.</summary>
    public void Normalize()
    {
        Tools ??= new(StringComparer.OrdinalIgnoreCase);
        foreach (var key in Tools.Keys.ToList()) Tools[key] ??= new ToolFolderSettings();
    }
}

/// <summary>Protection of files (Settings → File safety).</summary>
public sealed class SafetySettings
{
    public ExistingFilePolicy WhenOutputExists { get; set; } = ExistingFilePolicy.Ask;

    /// <summary>Copy a file to the backup folder before a tool replaces or changes it.</summary>
    public bool BackupBeforeChanges { get; set; } = true;

    /// <summary>Backups older than this are removed at start-up (0 = keep them).</summary>
    public int BackupRetentionDays { get; set; } = 30;

    /// <summary>The oldest backups are removed when all backups together are larger than this (0 = no limit).</summary>
    public int BackupMaxSizeMegabytes { get; set; } = 4096;

    /// <summary>Read generated files back and check them before they replace anything.</summary>
    public bool ValidateOutputs { get; set; } = true;

    /// <summary>Warn when a file's content does not match its extension or its format is not supported.</summary>
    public bool WarnUnsupportedFormats { get; set; } = true;

    /// <summary>Remove left-over temporary files at start-up and exit.</summary>
    public bool CleanTemporaryFiles { get; set; } = true;

    /// <summary>Clamps the section's values into valid ranges.</summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(WhenOutputExists)) WhenOutputExists = ExistingFilePolicy.Ask;
        BackupRetentionDays = Math.Clamp(BackupRetentionDays, 0, 3650);
        BackupMaxSizeMegabytes = Math.Clamp(BackupMaxSizeMegabytes, 0, 1024 * 1024);
    }
}

/// <summary>Speed and memory (Settings → Performance).</summary>
public sealed class PerformanceSettings
{
    public const int MaxOperations = 8;

    /// <summary>Long operations (conversions, scripts, editor saves) that may run at the same time; more wait in a queue.</summary>
    public int MaxConcurrentOperations { get; set; } = 2;

    /// <summary>Threads used inside one operation for parallel work (0 = one per processor core).</summary>
    public int WorkerThreads { get; set; }

    /// <summary>Decoded sprite sheets kept in memory (each about 0.6 MB).</summary>
    public int SpriteSheetCache { get; set; } = 96;

    /// <summary>Stop a batch at the first failed file instead of continuing with the rest.</summary>
    public bool StopBatchOnError { get; set; }

    /// <summary>Show the progress of long operations on the taskbar button.</summary>
    public bool ShowTaskbarProgress { get; set; } = true;

    /// <summary>Files larger than this are not previewed (images, text) to keep the interface responsive.</summary>
    public int PreviewSizeLimitMegabytes { get; set; } = 32;

    /// <summary>Clamps the section's values into valid ranges.</summary>
    public void Normalize()
    {
        MaxConcurrentOperations = Math.Clamp(MaxConcurrentOperations, 1, MaxOperations);
        WorkerThreads = Math.Clamp(WorkerThreads, 0, 64);
        SpriteSheetCache = Math.Clamp(SpriteSheetCache, 16, 1024);
        PreviewSizeLimitMegabytes = Math.Clamp(PreviewSizeLimitMegabytes, 1, 1024);
    }
}

/// <summary>Messages and logs (Settings → Notifications and logging).</summary>
public sealed class NotificationSettings
{
    /// <summary>Flash the taskbar button when a long operation finishes while the window is in the background.</summary>
    public bool FlashTaskbarWhenDone { get; set; } = true;

    /// <summary>Play the Windows notification sound when a long operation finishes.</summary>
    public bool PlaySoundWhenDone { get; set; }

    /// <summary>Only operations that ran at least this long trigger the notices above.</summary>
    public int NoticeAfterSeconds { get; set; } = 10;

    /// <summary>Show a dialog when an operation fails (otherwise the status bar and the log only).</summary>
    public bool ShowErrorDialogs { get; set; } = true;

    /// <summary>Lowest level written to the log file.</summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Info;

    /// <summary>Log files older than this are removed at start-up.</summary>
    public int LogRetentionDays { get; set; } = 14;

    /// <summary>A day's log stops growing at this size.</summary>
    public int LogMaxMegabytesPerDay { get; set; } = 20;

    /// <summary>Clamps the section's values into valid ranges.</summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(LogLevel)) LogLevel = LogLevel.Info;
        NoticeAfterSeconds = Math.Clamp(NoticeAfterSeconds, 0, 3600);
        LogRetentionDays = Math.Clamp(LogRetentionDays, 1, 365);
        LogMaxMegabytesPerDay = Math.Clamp(LogMaxMegabytesPerDay, 1, 1024);
    }
}

/// <summary>Diagnostics page: monitoring, operation history and diagnostics reports.</summary>
public sealed class DiagnosticsSettings
{
    /// <summary>Measure CPU, memory and disk space while the Diagnostics page is open (never while it is closed).</summary>
    public bool PerformanceMonitoring { get; set; } = true;

    /// <summary>Seconds between two measurements.</summary>
    public int SampleIntervalSeconds { get; set; } = 2;

    /// <summary>Finished operations kept in the history (also across sessions).</summary>
    public int OperationHistoryEntries { get; set; } = 200;

    /// <summary>All log files together are kept below this size (oldest removed first).</summary>
    public int LogMaxTotalMegabytes { get; set; } = 200;

    /// <summary>Days of log files put into a diagnostics report.</summary>
    public int ReportLogDays { get; set; } = 3;

    // Including full paths in a report is a per-report choice on the Diagnostics page that starts
    // off every time; it is deliberately not stored, so reports are sanitized unless asked otherwise.

    public static IReadOnlyList<int> SampleIntervals { get; } = [1, 2, 5, 10, 30];

    /// <summary>Clamps the section's values into valid ranges.</summary>
    public void Normalize()
    {
        if (!SampleIntervals.Contains(SampleIntervalSeconds)) SampleIntervalSeconds = 2;
        OperationHistoryEntries = Math.Clamp(OperationHistoryEntries, 10, 2000);
        LogMaxTotalMegabytes = Math.Clamp(LogMaxTotalMegabytes, 10, 10240);
        ReportLogDays = Math.Clamp(ReportLogDays, 1, 14);
    }
}

/// <summary>The Asset Library and Content Studio (8.8). Defaults keep everything local and only add files the user saved.</summary>
public sealed class LibrarySettings
{
    /// <summary>The library folder; null = next to the settings (portable) or %LOCALAPPDATA%\TibiaFoundry\Library.</summary>
    public string? Folder { get; set; }

    /// <summary>Every file or folder a tool saves is added to the library (incrementally, in the background).</summary>
    public bool AddExportsAutomatically { get; set; } = true;

    /// <summary>A collection that exports added automatically also go into (null = none).</summary>
    public string? ExportCollection { get; set; }

    /// <summary>Saved folders with more files than this are not added automatically (add them with Add folder…).</summary>
    public int AutoAddMaxFiles { get; set; } = 20000;

    /// <summary>Largest difference (of 64 bits) between two images still reported as visually similar.</summary>
    public int SimilarityThreshold { get; set; } = 4;

    /// <summary>Content projects that may run at the same time in the processing queue.</summary>
    public int QueueConcurrency { get; set; } = 1;

    /// <summary>Where content projects write their runs; null = "Content Projects" under the output folder.</summary>
    public string? ContentOutputFolder { get; set; }

    /// <summary>The project open last in Content Studio.</summary>
    public string? LastProjectId { get; set; }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Folder)) Folder = null;
        if (string.IsNullOrWhiteSpace(ExportCollection)) ExportCollection = null;
        if (string.IsNullOrWhiteSpace(ContentOutputFolder)) ContentOutputFolder = null;
        AutoAddMaxFiles = Math.Clamp(AutoAddMaxFiles, 100, 1_000_000);
        SimilarityThreshold = Math.Clamp(SimilarityThreshold, 0, 16);
        QueueConcurrency = Math.Clamp(QueueConcurrency, 1, 4);
    }
}

/// <summary>Backups made by scripts.</summary>
public enum ScriptBackupMode
{
    /// <summary>Follow Settings → File safety.</summary>
    FollowGlobal,
    /// <summary>Always back up files a script changes, even when general backups are off.</summary>
    Always,
}

/// <summary>Utilities → Scripts (Settings → Scripts). Scripts never run automatically.</summary>
public sealed class ScriptSettings
{
    /// <summary>Scripts that change files start in preview mode: nothing changes until you apply the previewed changes.</summary>
    public bool PreviewFirst { get; set; } = true;

    public ScriptBackupMode Backups { get; set; } = ScriptBackupMode.FollowGlobal;

    public bool KeepHistory { get; set; } = true;

    public int MaxHistoryEntries { get; set; } = 200;

    /// <summary>Scripts that may run at the same time (never more than the general limit).</summary>
    public int MaxConcurrentScripts { get; set; } = 1;

    /// <summary>Show every script's advanced options without expanding them first.</summary>
    public bool ShowAdvancedOptions { get; set; }

    /// <summary>Clamps the section's values into valid ranges.</summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(Backups)) Backups = ScriptBackupMode.FollowGlobal;
        MaxHistoryEntries = Math.Clamp(MaxHistoryEntries, 10, 5000);
        MaxConcurrentScripts = Math.Clamp(MaxConcurrentScripts, 1, PerformanceSettings.MaxOperations);
    }
}
