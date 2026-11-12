using TibiaFoundry.Core.Metadata;
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.Themes;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.Tools;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>A tool's input folder in Settings → Files and folders (where its file pickers start).</summary>
public sealed partial class ToolFolderRow(SettingsViewModel owner, string tool) : ObservableObject
{
    public string Tool { get; } = tool;
    public string Name => ToolKeys.DisplayName(Tool);
    public string Input => owner.ToolInput(Tool) ?? L.T("Settings.TheFolderLastUsedOrThe");
    public string? InputStatus => owner.ToolInput(Tool) is { } p ? SettingsViewModel.FolderStatus(p) : null;
    public bool HasInput => owner.ToolInput(Tool) != null;

    public void Refresh() => OnPropertyChanged(string.Empty);

    [RelayCommand] private void ChangeInput() => owner.ChangeToolFolder(Tool, input: true);
    [RelayCommand] private void Clear() => owner.ClearToolInput(Tool);
}

/// <summary>One file or folder of the client in Settings → Tibia client (Client and data locations).</summary>
public sealed partial class LocationRow(SettingsViewModel owner, ResolvedLocation location, ClientScope scope) : ObservableObject
{
    public string Id => location.Id;
    public ClientScope Scope { get; } = scope;
    public string Name => location.Definition.Name;
    public string Description => location.Definition.Description + (location.Definition.Required ? "" : " Optional.");
    public string Path => location.DisplayPath;
    public string Origin => location.SourceText;
    public bool IsChosen => location.IsChosen;
    public bool IsFile => location.Definition.Kind == LocationKind.File;

    /// <summary>
    /// Why it is not used (a required one is missing, or the chosen one is unavailable), or null when
    /// it is fine. An optional location that is simply not there needs no warning: its origin says so.
    /// </summary>
    public string? Status => location.Notice ?? (location.Found || !location.Definition.Required ? null : location.Problem);

    public bool HasProblem => location.ChosenUnavailable || !location.Found && location.Definition.Required;
    public bool CanOpen => location.Found;

    [RelayCommand] private Task Change() => owner.ChangeLocationAsync(Id, Scope);
    [RelayCommand] private Task Reset() => owner.ResetLocationAsync(Id, Scope);
    [RelayCommand] private void Open() => owner.OpenLocation(location);
}

/// <summary>The folder of one kind of output in Settings → Files and folders.</summary>
public sealed partial class OutputFolderRow(SettingsViewModel owner, OutputKindInfo info) : ObservableObject
{
    public OutputKind Kind => info.Kind;
    public string Name => info.DisplayName;
    public string Description => info.Description;
    public string Path => owner.Outputs.FolderFor(Kind);
    public bool IsChosen => owner.Outputs.IsExplicit(Kind);
    public string Origin => IsChosen ? (owner.KeptFromPreviousVersion(Kind) ? L.T("Settings.KeptFromThePreviousVersion") : L.T("Settings.ChosenByYou")) : "default";

    /// <summary>Why the folder cannot be used, "will be created" for a missing default, or null when it is fine.</summary>
    public string? Status
    {
        get
        {
            var state = OutputLocations.Check(Path);
            return !state.Usable ? state.Problem : !state.Exists ? L.T("Settings.DoesNotExistYetItIs") : null;
        }
    }

    public bool HasProblem => !OutputLocations.Check(Path).Usable;

    public void Refresh() => OnPropertyChanged(string.Empty);

    [RelayCommand] private void Change() => owner.ChangeOutputFolder(Kind);
    [RelayCommand] private void UseDefault() => owner.UseDefaultOutputFolder(Kind);
    [RelayCommand] private void Open() => owner.OpenOutputFolder(Kind);
}

/// <summary>One setting a tool declares in its manifest (Settings → Tools).</summary>
public sealed partial class ToolSettingRow(SettingsViewModel owner, ToolManifest tool, ToolSettingDefinition definition) : ObservableObject
{
    public ToolManifest Tool { get; } = tool;
    public ToolSettingDefinition Definition { get; } = definition;
    public string Label => Definition.Label;
    public string Help => Definition.Help;
    public bool IsBoolean => Definition.Kind == ToolSettingKind.Boolean;
    public bool IsChoice => Definition.Kind == ToolSettingKind.Choice;
    public bool IsText => Definition.Kind is ToolSettingKind.Text or ToolSettingKind.Integer;
    public IReadOnlyList<SettingChoice<string>> Choices => Definition.Choices.Select(c => new SettingChoice<string>(c.Value, c.Label)).ToList();

    [ObservableProperty] private string? _problem;

    public string Value
    {
        get => owner.ToolSettings.Get(Tool, Definition.Key);
        set
        {
            if (owner.ToolSettings.TrySet(Tool, Definition.Key, value, out string? problem))
            {
                Problem = null;
                owner.ToolSettingChanged(Tool);
            }
            else Problem = problem;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BoolValue));
        }
    }

    public bool BoolValue
    {
        get => Value == "true";
        set => Value = value ? "true" : "false";
    }

    public void Refresh()
    {
        Problem = null;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(BoolValue));
    }
}

/// <summary>A tool with the settings it declares (Settings → Tools).</summary>
public sealed partial class ToolSettingsGroup(SettingsViewModel owner, ToolManifest tool) : ObservableObject
{
    public ToolManifest Tool { get; } = tool;
    public string Name => Tool.Name;
    public string Version => L.F("Settings.SettingsVersion0", Tool.SettingsVersion);
    public List<ToolSettingRow> Settings { get; } = tool.Settings.Where(s => s.Visible).Select(s => new ToolSettingRow(owner, tool, s)).ToList();

    [RelayCommand]
    private void Reset()
    {
        owner.ToolSettings.Reset(Tool);
        owner.ToolSettingChanged(Tool);
        foreach (var row in Settings) row.Refresh();
    }
}

/// <summary>A choice with a label (for combo boxes).</summary>
public sealed record SettingChoice<T>(T Value, string Label);

/// <summary>
/// Preferences page, organized in categories. Every option controls real behavior, is applied
/// and saved immediately, and each category can be restored to its defaults.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly MainViewModel _main;

    public SettingsViewModel(AppServices services, MainViewModel main) : base(services, BuiltInTools.Settings)
    {
        _main = main;
        ToolFolders = new ObservableCollection<ToolFolderRow>(new[] { ToolKeys.AssetsEditor, ToolKeys.RccEditor, ToolKeys.ClientConverter }.Select(t => new ToolFolderRow(this, t)));
        OutputFolders = new ObservableCollection<OutputFolderRow>(OutputLocations.Kinds.Select(k => new OutputFolderRow(this, k)));
        Themes = new ThemeEditorViewModel(services, main);
        _locationScope = services.Settings.ActiveScope;
        RebuildLocationRows();
        services.Locations.LocationsChanged += RebuildLocationRows;
    }

    /// <summary>Settings → Appearance &amp; Themes.</summary>
    public ThemeEditorViewModel Themes { get; }

    public override string? UnsavedChanges => Themes.UnsavedDescription;

    internal OutputService Outputs => Services.Outputs;
    internal ToolSettingsStore ToolSettings => Services.ToolSettings;

    private AppSettings S => Services.Settings;

    /// <summary>Categories of the page (the list on the left jumps to them).</summary>
    public IReadOnlyList<string> Categories { get; } =
        // loc: identity — the sections' Tags; shown translated through loc:Choice.Prefix="Choice.SettingsCategory".
        ["General", "Appearance & Themes", "Tibia client", "Files and folders", "File safety", "Performance", "Attribution", "Notifications and logging", "Tools", "Scripts", "Diagnostics", "Advanced"]; // loc: identity

    [ObservableProperty] private string? _selectedCategory;

    private void Changed(bool refreshPaths = false, bool rebuildSprites = false)
    {
        Services.SaveSettings();
        if (refreshPaths) Services.Data.RefreshPaths();
        else if (rebuildSprites) Services.Data.RebuildSprites();
    }

    private void Set<T>(Action<T> apply, T value, Action? after = null, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        apply(value);
        Changed();
        after?.Invoke();
        OnPropertyChanged(name);
    }

    /// <summary>"" when the folder is usable; otherwise why not (shown next to the path).</summary>
    public static string FolderStatus(string path)
    {
        if (!Directory.Exists(path)) return L.T("Settings.ThisFolderDoesNotExistIt");
        return OutputLocations.CanWrite(path, out string? problem) ? "" : L.T("Settings.NotWritable") + problem;
    }

    // ================================================================== General
    public IReadOnlyList<SettingChoice<string>> StartPages => _main.Pages.Select(p => new SettingChoice<string>(p.GetType().Name, p.Title)).ToList();

    public bool StartWithLastPage
    {
        get => S.General.StartupPage == StartupPageMode.LastUsed;
        set => Set(v => S.General.StartupPage = v ? StartupPageMode.LastUsed : StartupPageMode.Specific, value, () => OnPropertyChanged(nameof(StartPageName)));
    }

    public string StartPageName
    {
        get => S.General.StartupPageName ?? nameof(AppearancesViewModel);
        set => Set(v => S.General.StartupPageName = v, value);
    }

    public bool RememberLastFolders
    {
        get => S.General.RememberLastFolders;
        set => Set(v => S.General.RememberLastFolders = v, value);
    }

    public bool ReopenLastFiles
    {
        get => S.General.ReopenLastFiles;
        set => Set(v => S.General.ReopenLastFiles = v, value);
    }

    [RelayCommand]
    private void ForgetFolders()
    {
        S.General.LastFolders.Clear();
        S.General.LastFiles.Clear();
        Changed();
        Info(L.T("Settings.RememberedFoldersAndFilesWereForgotten"));
    }

    /// <summary>Saves all settings to a file (to move them to another computer or keep a copy).</summary>
    [RelayCommand]
    private void ExportSettings()
    {
        string? file = Services.Outputs.SaveFile(OutputKind.DataExports, L.T("Settings.ExportSettings2"), L.T("Settings.SettingsJsonJson"), $"{AppIdentity.Id}-settings.json");
        if (file == null) return;
        if (Try(L.T("Settings.ExportingSettings"), () => new SettingsStore(file).Save(S))) Saved(file);
    }

    /// <summary>Replaces the settings with those of an exported file (validated, then applied).</summary>
    [RelayCommand]
    private void ImportSettings()
    {
        string? file = Services.Dialogs.OpenFile(L.T("Settings.ImportSettings2"), L.T("Settings.SettingsJsonJsonAllFiles"), Services.Outputs.FolderFor(OutputKind.DataExports));
        if (file == null) return;
        Try(L.T("Settings.ImportingSettings"), () =>
        {
            var (imported, status, _) = new SettingsStore(file).Load();
            if (status is SettingsLoadStatus.RecoveredFromCorruption or SettingsLoadStatus.CreatedDefaults)
                throw new InvalidDataException($"{Path.GetFileName(file)} is not a readable settings file.");
            if (!Services.Dialogs.Confirm(L.F("Settings.ReplaceAllCurrentSettingsWithThose", Path.GetFileName(file), imported.CreatorName), L.T("Settings.ImportSettings2"), L.T("Settings.Import2"), L.T("Common.Cancel"))) return;
            CopyAll(imported, S, keepUserContent: false);
            ApplyEverything();
            Info(L.T("Settings.SettingsImportedFrom") + file);
        });
    }

    /// <summary>
    /// Settings that describe this PC's client archive (8.5): where it is, which of its revisions or
    /// folders is in use, and that setup ran. An import from another PC or a reset never changes them;
    /// the archive is only moved with Clients → Move store, so its revisions never seem to vanish.
    /// </summary>
    internal static readonly IReadOnlySet<string> ThisPcOnly = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(AppSettings.ClientStoreFolder), nameof(AppSettings.ActiveClientLive), nameof(AppSettings.ActiveClientTest), nameof(AppSettings.ClientSetupCompleted),
    };

    /// <summary>
    /// Copies every option (not the format number, options of newer versions, or <see cref="ThisPcOnly"/>).
    /// A reset (<paramref name="keepUserContent"/>) keeps the creator name and the custom themes the
    /// user made; only the active theme returns to the built-in default.
    /// </summary>
    internal static void CopyAll(AppSettings from, AppSettings to, bool keepUserContent)
    {
        foreach (var p in typeof(AppSettings).GetProperties().Where(p => p.CanWrite && p.Name is not nameof(AppSettings.UnknownSettings) and not nameof(AppSettings.Version)
                                                                         and not nameof(AppSettings.LegacyGifAuthor) and not nameof(AppSettings.LoadedVersion) && !ThisPcOnly.Contains(p.Name)))
            if (!(keepUserContent && p.Name is nameof(AppSettings.CreatorName) or nameof(AppSettings.Themes))) p.SetValue(to, p.GetValue(from));
        if (keepUserContent) to.Themes.ActiveCustomTheme = null;
        to.Normalize();
    }

    /// <summary>Applies all settings that have a live effect, after an import or reset.</summary>
    private void ApplyEverything()
    {
        if (S.Themes.Find(S.Themes.ActiveCustomTheme) is { } custom) ThemeManager.ApplyCustom(custom);
        else ThemeManager.Apply(S.Theme);
        Themes.Refresh();
        Controls.AnimatedSprite.AnimationsEnabled = S.Interface.AnimatePreviews;
        Services.SetCreatorName(S.CreatorName);
        Services.ApplyLogSettings();
        Services.RaiseInterfaceChanged();
        Changed(refreshPaths: true);
        RaiseAll();
    }

    // ================================================================== Appearance
    public ThemePreference Theme
    {
        get => _main.Theme;
        set { _main.Theme = value; OnPropertyChanged(); }
    }

    public double UiScale
    {
        get => S.Interface.Scale;
        set => Set(v => { S.Interface.Scale = v; S.Interface.Normalize(); }, value, Services.RaiseInterfaceChanged);
    }

    public string UiScaleText => $"{S.Interface.Scale * 100:0} %";

    partial void OnSelectedCategoryChanged(string? value) { }

    public static IReadOnlyList<SettingChoice<NavigationLayout>> NavigationChoices { get; } =
    [
        new(NavigationLayout.Automatic, L.T("Settings.NamesIconsOnlyWhenTheWindow")),
        new(NavigationLayout.Expanded, L.T("Settings.AlwaysWithNames")),
        new(NavigationLayout.Compact, L.T("Settings.AlwaysIconsOnly")),
    ];

    public NavigationLayout Navigation
    {
        get => S.Interface.Navigation;
        set => Set(v => S.Interface.Navigation = v, value, Services.RaiseInterfaceChanged);
    }

    public bool RememberWindowPlacement
    {
        get => S.Interface.RememberWindowPlacement;
        set => Set(v => S.Interface.RememberWindowPlacement = v, value);
    }

    public bool AnimatePreviews
    {
        get => S.Interface.AnimatePreviews;
        set => Set(v => S.Interface.AnimatePreviews = v, value, () => Controls.AnimatedSprite.AnimationsEnabled = value);
    }

    public IReadOnlyList<int> ThumbnailSizes => InterfaceSettings.ThumbnailSizes;

    public int ThumbnailSize
    {
        get => S.Interface.ThumbnailSize;
        set => Set(v => S.Interface.ThumbnailSize = v, value, Services.RaiseInterfaceChanged);
    }

    public bool ShowDefaultValues
    {
        get => S.ShowDefaultValues;
        set => Set(v => S.ShowDefaultValues = v, value);
    }

    /// <summary>
    /// Restores the interface options (size, navigation, window, previews). Themes have their own
    /// reset buttons ("Restore original appearance" also switches back to the System theme).
    /// </summary>
    [RelayCommand]
    private void ResetAppearance()
    {
        S.Interface = new InterfaceSettings();
        Controls.AnimatedSprite.AnimationsEnabled = true;
        Services.RaiseInterfaceChanged();
        Changed();
        RaiseAll();
    }

    // ================================================================== Tibia client
    public IReadOnlyList<string> InstalledPackages => TibiaClientPaths.FindInstalledPackages();

    public bool UseTestServer
    {
        get => S.UseTestServer;
        set { _main.UseTestServer = value; OnPropertyChanged(); }
    }

    /// <summary>Where imported client versions are kept (7.3), and which edition rule applies.</summary>
    public string ClientStoreText => Services.Clients.Root is { } root
        ? root + (Services.Paths?.IsPortable == true ? L.T("Settings.PortableInsideTibiaFoundryDataNextTo") : "")
        : L.T("Settings.NotAvailableHere");

    // ------------------------------------------------------------------ client archive and start-up (8.0)

    public IReadOnlyList<SettingChoice<string>> StorageModes { get; } =
    [
        new(Core.Clients.ManifestStorage.Shared, L.T("Settings.SpaceEfficientEachFileContentIs")),
        new(Core.Clients.ManifestStorage.Independent, L.T("Settings.IndependentCopiesEveryRevisionIsA")),
    ];

    /// <summary>How future archives are kept; existing revisions keep theirs.</summary>
    public string ClientStorageMode
    {
        get => S.ClientStorageMode == Core.Clients.ManifestStorage.Independent ? Core.Clients.ManifestStorage.Independent : Core.Clients.ManifestStorage.Shared;
        set => Set(S.ClientStorageMode, value, v => S.ClientStorageMode = v);
    }

    /// <summary>Include hotkeys, characters' data, the explored map, screenshots and logs in archives.</summary>
    public bool ArchivePersonalData
    {
        get => S.ArchivePersonalData;
        set => Set(S.ArchivePersonalData, value, v => S.ArchivePersonalData = v);
    }

    public IReadOnlyList<SettingChoice<ClientChangeHandling>> ChangeHandlingChoices { get; } =
    [
        new(ClientChangeHandling.Ask, L.T("Settings.AskMeANoticeInThe")),
        new(ClientChangeHandling.Automatic, L.T("Settings.ArchiveItAutomatically")),
        new(ClientChangeHandling.AskQuietly, L.T("Settings.OnlyShowItOnTheClients")),
    ];

    /// <summary>When the check at start finds that an installed client changed and is not archived.</summary>
    public ClientChangeHandling ClientChangeHandling
    {
        get => S.ClientChangeHandling;
        set => Set(S.ClientChangeHandling, value, v =>
        {
            S.ClientChangeHandling = v;
            S.ClientImportReminders = v != ClientChangeHandling.AskQuietly;   // 7.3 reads this
        });
    }

    public IReadOnlyList<SettingChoice<ClientLaunchCheck>> LaunchCheckChoices { get; } =
    [
        new(ClientLaunchCheck.Full, L.T("Settings.ReadEveryFileFindsEveryChange")),
        new(ClientLaunchCheck.Quick, L.T("Settings.OnlyFilesWhoseSizeOrTime")),
    ];

    public ClientLaunchCheck ClientLaunchCheck
    {
        get => S.ClientLaunchCheck;
        set => Set(S.ClientLaunchCheck, value, v => S.ClientLaunchCheck = v);
    }

    public bool MonitorInstalledLive
    {
        get => S.MonitorInstalledLive;
        set => Set(S.MonitorInstalledLive, value, v => S.MonitorInstalledLive = v);
    }

    public bool MonitorInstalledTest
    {
        get => S.MonitorInstalledTest;
        set => Set(S.MonitorInstalledTest, value, v => S.MonitorInstalledTest = v);
    }

    public IReadOnlyList<SettingChoice<StartupClientMode>> StartupModeChoices { get; } =
    [
        new(StartupClientMode.Remembered, L.T("Settings.UseTheRememberedClient")),
        new(StartupClientMode.Ask, L.T("Settings.AskWhichClient")),
    ];

    public StartupClientMode StartupClientMode
    {
        get => S.StartupClientMode;
        set => Set(S.StartupClientMode, value, v => S.StartupClientMode = v);
    }

    /// <summary>The remembered client: the active server and its client.</summary>
    public string RememberedClientText => Services.Clients.Describe(S.ActiveScope, Services.Data.ClientVersion);

    private void Set<T>(T current, T value, Action<T> apply, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        apply(value);
        Services.SaveSettings();
        OnPropertyChanged(name);
    }

    private void AfterClientChoice(string what)
    {
        AppLog.Info("Client choice (Settings): " + what);
        _main.ClientChangedElsewhere();
        OnPropertyChanged(nameof(RememberedClientText));
        OnPropertyChanged(nameof(UseTestServer));
        OnPropertyChanged(nameof(StartupClientMode));
    }

    private bool CanChangeClient()
    {
        if (Services.WhyNotSwitchClient() is not { } why) return true;
        Services.Dialogs.Show(why, MessageKind.Info, L.T("Settings.TibiaClient"));
        return false;
    }

    /// <summary>"Switch client…": the selection dialog, whatever the start-up choice.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SwitchClient()
    {
        if (CanChangeClient()) AfterClientChoice(_main.Startup.AskWhichClient());
    }

    /// <summary>"Browse for a client folder…": checks the folder, adds it to the Clients page and uses it.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void BrowseClient()
    {
        if (CanChangeClient() && _main.Startup.BrowseAndUse() is { } what) AfterClientChoice(what);
    }

    /// <summary>"Clear": the installed client of the active server is used (nothing is removed).</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ClearRememberedClient()
    {
        if (!CanChangeClient() || S.ActiveClient(S.ActiveScope) == null) return;
        S.SetActiveClient(S.ActiveScope, null);
        Services.SaveSettings();
        Services.Data.SwitchClient();
        AfterClientChoice(L.T("Settings.ClearedTheInstalledClientIsUsed"));
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task RunSetupAgainAsync()
    {
        if (CanChangeClient()) AfterClientChoice(await _main.Startup.SetupAsync());
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenClients() => Services.RequestNavigate<ClientsViewModel>();

    public string TestServerPackage
    {
        get => S.TestServerPackage;
        set
        {
            string package = string.IsNullOrWhiteSpace(value) ? TibiaClientPaths.DefaultTestPackageName : value.Trim();
            if (package == S.TestServerPackage) return;
            S.TestServerPackage = package;
            Services.SaveSettings();
            // Another test-server package is another client: nothing loaded from the previous one is kept.
            if (S.UseTestServer) Services.Data.SwitchClient();
            RebuildLocationRows();
            OnPropertyChanged();
        }
    }

    public bool LoadDataAutomatically
    {
        get => S.LoadDataAutomatically;
        set { S.LoadDataAutomatically = value; Changed(); OnPropertyChanged(); }
    }

    // ================================================================== Client and data locations
    public IReadOnlyList<SettingChoice<ClientScope>> LocationScopes { get; } =
        [new(ClientScope.Live, L.T("ClientData.LiveClient")), new(ClientScope.TestServer, L.T("Common.TestServer"))];

    /// <summary>Whose locations the list shows (by default the client in use).</summary>
    [ObservableProperty] private ClientScope _locationScope;

    partial void OnLocationScopeChanged(ClientScope value) => RebuildLocationRows();

    /// <summary>Every file and folder Tibia Foundry reads from the client shown in <see cref="LocationScope"/>.</summary>
    public ObservableCollection<LocationRow> LocationRows { get; } = new();

    public string LocationScopeNote => LocationScope == S.ActiveScope
        ? L.T("Settings.TheseAreTheFilesOfThe")
        : L.F("Settings.TheseAreTheFilesOfThe2", (LocationScope == ClientScope.Live ? L.T("Settings.LiveClient") : L.T("Settings.TestServer")));

    private void RebuildLocationRows()
    {
        var resolver = Services.Data.LocationsFor(LocationScope);
        LocationRows.Clear();
        foreach (var def in LocationResolver.All) LocationRows.Add(new LocationRow(this, resolver.Resolve(def.Id), LocationScope));
        OnPropertyChanged(nameof(LocationScopeNote));
    }

    internal async Task ChangeLocationAsync(string id, ClientScope scope)
    {
        if (await Services.Locations.LocateAsync(id, scope)) RaiseAll();
    }

    internal async Task ResetLocationAsync(string id, ClientScope scope)
    {
        await Services.Locations.ResetAsync(id, scope);
        RaiseAll();
    }

    internal void OpenLocation(ResolvedLocation location)
    {
        if (location.Path is not { } path) return;
        if (location.Definition.Kind == LocationKind.File) Services.Shell.Reveal(path);
        else Services.Shell.OpenFolder(path);
    }

    public SpriteSourceMode SpriteSource
    {
        get => S.SpriteSource;
        set { S.SpriteSource = value; Changed(rebuildSprites: true); OnPropertyChanged(); }
    }

    public bool SaveDecodedSheets
    {
        get => S.SaveDecodedSheets;
        set { S.SaveDecodedSheets = value; Changed(rebuildSprites: true); OnPropertyChanged(); }
    }

    // ================================================================== Files and folders
    /// <summary>Root of the output folders (default Documents\Tibia_Foundry).</summary>
    public string OutputFolder => S.OutputFolder.Length > 0 ? S.OutputFolder : L.T("Settings.NoDocumentsFolderWasFoundChoose");
    public bool OutputFolderIsDefault => string.Equals(S.OutputFolder, AppSettings.DefaultOutputFolder, StringComparison.OrdinalIgnoreCase);
    public string? OutputFolderStatus => OutputLocations.Check(S.OutputFolder) is { Usable: false } state ? state.Problem : null;

    /// <summary>The 5.0-era paths kept for compatibility (bindings of the old rows; same values as the rows below).</summary>
    public string GifFolder => S.EffectiveGifFolder;
    public string BackupFolder => Services.BackupFolder;
    public string TempFolder => Services.TempFolder;
    public string LogFolder => Services.LogFolder;

    public ObservableCollection<OutputFolderRow> OutputFolders { get; }
    public ObservableCollection<ToolFolderRow> ToolFolders { get; }

    /// <summary>Folders the 5.0 → 6.0 upgrade kept (shown as such instead of "chosen by you").</summary>
    internal bool KeptFromPreviousVersion(OutputKind kind) =>
        kind == OutputKind.Backups && string.Equals(S.Locations.BackupFolder, Services.PreviousDefaultBackupFolder, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void ChangeRootFolder()
    {
        string? folder = Services.Dialogs.PickFolder(L.T("Settings.RootFolderForEverythingTibiaFoundry"), S.OutputFolder);
        if (folder == null) return;
        var state = OutputLocations.Prepare(folder);
        if (!state.Usable)
        {
            Services.Dialogs.Show(L.F("Settings.0CannotBeUsed1", folder, state.Problem), MessageKind.Warning, L.T("Common.OutputFolder"));
            return;
        }
        S.OutputFolder = state.Path;
        if (S.Locations.LogFolder == null) Services.ApplyLogSettings();
        Changed();
        RaiseAll();
    }

    [RelayCommand]
    private void DefaultRootFolder()
    {
        if (AppSettings.DefaultOutputFolder.Length == 0)
        {
            Services.Dialogs.Show(L.T("Settings.WindowsReportsNoDocumentsFolderSo"), MessageKind.Info, L.T("Common.OutputFolder"));
            return;
        }
        S.OutputFolder = AppSettings.DefaultOutputFolder;
        if (S.Locations.LogFolder == null) Services.ApplyLogSettings();
        Changed();
        RaiseAll();
    }

    [RelayCommand] private void OpenRootFolder() => OpenOrCreate(S.OutputFolder, L.T("Common.OutputFolder"));

    /// <summary>Chooses the folder of a kind of output; it must be usable (a missing folder is created).</summary>
    public void ChangeOutputFolder(OutputKind kind)
    {
        var info = OutputLocations.Info(kind);
        string? folder = Services.Dialogs.PickFolder(L.F("Settings.FolderFor0", info.DisplayName.ToLowerInvariant()), Services.Outputs.FolderFor(kind));
        if (folder == null) return;
        var state = OutputLocations.Prepare(folder);
        if (!state.Usable)
        {
            Services.Dialogs.Show(L.F("Settings.0CannotBeUsed1", folder, state.Problem), MessageKind.Warning, info.DisplayName);
            return;
        }
        S.SetExplicitFolder(kind, state.Path);
        if (kind == OutputKind.Logs) Services.ApplyLogSettings();
        Changed();
        RaiseAll();
    }

    /// <summary>Goes back to the default folder of a kind (files already saved are not moved).</summary>
    public void UseDefaultOutputFolder(OutputKind kind)
    {
        S.SetExplicitFolder(kind, null);
        S.General.LastFolders.Remove(AppSettings.LastFolderKey(kind));
        if (kind == OutputKind.Logs) Services.ApplyLogSettings();
        Changed();
        RaiseAll();
    }

    public void OpenOutputFolder(OutputKind kind) => OpenOrCreate(Services.Outputs.FolderFor(kind), OutputLocations.Info(kind).DisplayName);

    /// <summary>Opens a folder; a folder that does not exist yet is created first (after asking).</summary>
    private void OpenOrCreate(string folder, string what)
    {
        if (!Directory.Exists(folder))
        {
            if (!Services.Dialogs.Confirm(L.F("Settings.0DoesNotExistYetCreate", folder), what, L.T("Settings.CreateAndOpen"), L.T("Common.Cancel"))) return;
            var state = OutputLocations.Prepare(folder);
            if (!state.Usable)
            {
                Services.Dialogs.Show(state.Problem!, MessageKind.Warning, what);
                return;
            }
            RaiseAll();
        }
        Services.RevealSaved(new StatusLink(folder, IsFile: false));
    }

    public string? ToolInput(string tool) => S.Locations.Tools.TryGetValue(tool, out var t) && !string.IsNullOrWhiteSpace(t.Input) ? t.Input : null;

    public void ChangeToolFolder(string tool, bool input)
    {
        if (!input)
        {
            ChangeOutputFolder(ToolKeys.OutputKindOf(tool));
            return;
        }
        string? folder = Services.Dialogs.PickFolder(L.F("Settings.0InputFolder", ToolKeys.DisplayName(tool)), ToolInput(tool));
        if (folder == null) return;
        if (!S.Locations.Tools.TryGetValue(tool, out var t)) S.Locations.Tools[tool] = t = new ToolFolderSettings();
        t.Input = folder;
        Changed();
        RaiseAll();
    }

    public void ClearToolInput(string tool)
    {
        if (S.Locations.Tools.TryGetValue(tool, out var t))
        {
            t.Input = null;
            if (t.Output == null) S.Locations.Tools.Remove(tool);
        }
        Changed();
        RaiseAll();
    }

    /// <summary>Every folder back to its default under Documents\Tibia_Foundry (no files are moved or deleted).</summary>
    [RelayCommand]
    private void ResetFolders()
    {
        S.Locations = new FileLocationSettings();
        S.OutputFolder = AppSettings.DefaultOutputFolder;
        S.SoundsFolder = S.GifFolder = S.MapTilesFolder = S.SpriteExtractionFolder = null;
        S.OutputFolders.Clear();
        foreach (var info in OutputLocations.Kinds) S.General.LastFolders.Remove(AppSettings.LastFolderKey(info.Kind));
        Services.ApplyLogSettings();
        Changed();
        RaiseAll();
    }

    // ================================================================== File safety
    public static IReadOnlyList<SettingChoice<ExistingFilePolicy>> ExistingFileChoices { get; } =
    [
        new(ExistingFilePolicy.Ask, L.T("Settings.AskMeEachTimeRecommended")),
        new(ExistingFilePolicy.KeepBoth, L.T("Settings.KeepBothSaveTheNewFile")),
        new(ExistingFilePolicy.Overwrite, L.T("Settings.ReplaceTheExistingFile")),
        new(ExistingFilePolicy.Skip, L.T("Settings.SkipKeepTheExistingFile")),
    ];

    public ExistingFilePolicy WhenOutputExists
    {
        get => S.Safety.WhenOutputExists;
        set => Set(v => S.Safety.WhenOutputExists = v, value);
    }

    public bool BackupBeforeChanges
    {
        get => S.Safety.BackupBeforeChanges;
        set
        {
            if (!value && !Services.Dialogs.Confirm(L.T("Settings.WithoutBackupsFilesThatATool"), L.T("Settings.Backups"), L.T("Settings.TurnOff"), L.T("Settings.KeepBackups")))
            {
                OnPropertyChanged();
                return;
            }
            Set(v => S.Safety.BackupBeforeChanges = v, value);
        }
    }

    public string BackupRetentionDays
    {
        get => S.Safety.BackupRetentionDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, out int days) && days is >= 0 and <= 3650) Set(v => S.Safety.BackupRetentionDays = v, days);
            else OnPropertyChanged();
        }
    }

    public string BackupMaxSizeMegabytes
    {
        get => S.Safety.BackupMaxSizeMegabytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, out int mb) && mb is >= 0 and <= 1024 * 1024) Set(v => S.Safety.BackupMaxSizeMegabytes = v, mb);
            else OnPropertyChanged();
        }
    }

    public bool ValidateOutputs
    {
        get => S.Safety.ValidateOutputs;
        set
        {
            if (!value && !Services.Dialogs.Confirm(L.T("Settings.WithoutValidationWrittenFilesAreNot"), L.T("Common.Validation"), L.T("Settings.TurnOff"), L.T("Settings.KeepValidating")))
            {
                OnPropertyChanged();
                return;
            }
            Set(v => S.Safety.ValidateOutputs = v, value);
        }
    }

    public bool WarnUnsupportedFormats
    {
        get => S.Safety.WarnUnsupportedFormats;
        set => Set(v => S.Safety.WarnUnsupportedFormats = v, value);
    }

    public bool CleanTemporaryFiles
    {
        get => S.Safety.CleanTemporaryFiles;
        set => Set(v => S.Safety.CleanTemporaryFiles = v, value);
    }

    [RelayCommand]
    private void ResetSafety()
    {
        S.Safety = new SafetySettings();
        Changed();
        RaiseAll();
    }

    // ================================================================== Performance
    public IReadOnlyList<int> OperationLimits { get; } = Enumerable.Range(1, PerformanceSettings.MaxOperations).ToList();

    public int MaxConcurrentOperations
    {
        get => S.Performance.MaxConcurrentOperations;
        set => Set(v => { S.Performance.MaxConcurrentOperations = v; S.Performance.Normalize(); }, value);
    }

    public IReadOnlyList<SettingChoice<int>> WorkerThreadChoices { get; } =
        [new(0, $"Automatic ({Environment.ProcessorCount} on this computer)"), .. Enumerable.Range(1, Math.Max(1, Environment.ProcessorCount)).Select(n => new SettingChoice<int>(n, n.ToString(System.Globalization.CultureInfo.InvariantCulture)))];

    public int WorkerThreads
    {
        get => S.Performance.WorkerThreads;
        set => Set(v => S.Performance.WorkerThreads = v, value);
    }

    public IReadOnlyList<int> SheetCacheSizes { get; } = [32, 64, 96, 192, 384, 768];

    public int SpriteSheetCache
    {
        get => S.Performance.SpriteSheetCache;
        set => Set(v => S.Performance.SpriteSheetCache = v, value, () => Services.Data.RebuildSprites());
    }

    public string SheetCacheMemory => L.F("Settings.About0N0MBAtMost", S.Performance.SpriteSheetCache * 0.59);

    public bool StopBatchOnError
    {
        get => S.Performance.StopBatchOnError;
        set => Set(v => S.Performance.StopBatchOnError = v, value);
    }

    public bool ShowTaskbarProgress
    {
        get => S.Performance.ShowTaskbarProgress;
        set => Set(v => S.Performance.ShowTaskbarProgress = v, value, Services.RaiseInterfaceChanged);
    }

    public IReadOnlyList<int> PreviewLimits { get; } = [8, 16, 32, 64, 128, 256];

    public int PreviewSizeLimitMegabytes
    {
        get => S.Performance.PreviewSizeLimitMegabytes;
        set => Set(v => S.Performance.PreviewSizeLimitMegabytes = v, value);
    }

    [RelayCommand]
    private void ResetPerformance()
    {
        S.Performance = new PerformanceSettings();
        Services.Data.RebuildSprites();
        Changed();
        RaiseAll();
    }

    // ================================================================== Attribution
    private string? _creatorInput;

    /// <summary>
    /// Creator name embedded in every exported image. Applied while typing (the box is not
    /// rewritten); an empty name restores the default.
    /// </summary>
    public string CreatorName
    {
        get => _creatorInput ?? S.CreatorName;
        set
        {
            _creatorInput = value;
            Services.SetCreatorName(value);
            OnPropertyChanged(nameof(AttributionPreview));
            OnPropertyChanged(nameof(CreatorIsDefault));
        }
    }

    /// <summary>The exact text embedded in exports.</summary>
    public string AttributionPreview => Attribution.TextFor(S.CreatorName);

    public bool CreatorIsDefault => string.Equals(S.CreatorName, Attribution.DefaultCreator, StringComparison.Ordinal);

    // ================================================================== Notifications and logging
    public bool FlashTaskbarWhenDone
    {
        get => S.Notifications.FlashTaskbarWhenDone;
        set => Set(v => S.Notifications.FlashTaskbarWhenDone = v, value);
    }

    public bool PlaySoundWhenDone
    {
        get => S.Notifications.PlaySoundWhenDone;
        set => Set(v => S.Notifications.PlaySoundWhenDone = v, value);
    }

    public IReadOnlyList<int> NoticeDelays { get; } = [0, 5, 10, 30, 60, 300];

    public int NoticeAfterSeconds
    {
        get => S.Notifications.NoticeAfterSeconds;
        set => Set(v => S.Notifications.NoticeAfterSeconds = v, value);
    }

    public bool ShowErrorDialogs
    {
        get => S.Notifications.ShowErrorDialogs;
        set => Set(v => S.Notifications.ShowErrorDialogs = v, value);
    }

    public static IReadOnlyList<SettingChoice<LogLevel>> LogLevels { get; } =
    [
        new(LogLevel.Debug, L.T("Settings.EverythingIncludingDetailsForTroubleshootingDebu")),
        new(LogLevel.Info, L.T("Settings.NormalWhatWasDoneWarningsAnd")),
        new(LogLevel.Warning, L.T("Settings.WarningsAndErrorsOnly")),
        new(LogLevel.Error, L.T("Settings.ErrorsOnly")),
    ];

    public LogLevel LogLevel
    {
        get => S.Notifications.LogLevel;
        set => Set(v => S.Notifications.LogLevel = v, value, Services.ApplyLogSettings);
    }

    public IReadOnlyList<int> LogRetentionChoices { get; } = [1, 7, 14, 30, 90, 365];

    public int LogRetentionDays
    {
        get => S.Notifications.LogRetentionDays;
        set => Set(v => S.Notifications.LogRetentionDays = v, value, Services.ApplyLogSettings);
    }

    public IReadOnlyList<int> LogSizeChoices { get; } = [5, 20, 50, 100, 500];

    public int LogMaxMegabytesPerDay
    {
        get => S.Notifications.LogMaxMegabytesPerDay;
        set => Set(v => S.Notifications.LogMaxMegabytesPerDay = v, value, Services.ApplyLogSettings);
    }

    public string LogFilesInfo
    {
        get
        {
            var files = AppLog.Files();
            return $"{L.Count(files.Count, "Noun.log_file")}, {ReportWriter.FormatSize(files.Sum(f => f.Length))}";
        }
    }

    [RelayCommand]
    private void ResetNotifications()
    {
        S.Notifications = new NotificationSettings();
        // The total size of the log files is shown with the other log limits in this section (it is stored with the diagnostics settings).
        S.Diagnostics.LogMaxTotalMegabytes = new DiagnosticsSettings().LogMaxTotalMegabytes;
        Services.ApplyLogSettings();
        Changed();
        RaiseAll();
    }

    // ================================================================== Scripts
    public bool ScriptsPreviewFirst
    {
        get => S.Scripts.PreviewFirst;
        set => Set(v => S.Scripts.PreviewFirst = v, value);
    }

    public bool ScriptsAlwaysBackUp
    {
        get => S.Scripts.Backups == ScriptBackupMode.Always;
        set => Set(v => S.Scripts.Backups = v ? ScriptBackupMode.Always : ScriptBackupMode.FollowGlobal, value);
    }

    public bool ScriptsKeepHistory
    {
        get => S.Scripts.KeepHistory;
        set => Set(v => S.Scripts.KeepHistory = v, value);
    }

    public IReadOnlyList<int> HistorySizes { get; } = [50, 100, 200, 500, 1000];

    public int ScriptsMaxHistory
    {
        get => S.Scripts.MaxHistoryEntries;
        set => Set(v => S.Scripts.MaxHistoryEntries = v, value);
    }

    public int ScriptsMaxConcurrent
    {
        get => S.Scripts.MaxConcurrentScripts;
        set => Set(v => S.Scripts.MaxConcurrentScripts = v, value);
    }

    public bool ScriptsShowAdvanced
    {
        get => S.Scripts.ShowAdvancedOptions;
        set => Set(v => S.Scripts.ShowAdvancedOptions = v, value);
    }

    public string ScriptInputFolder => ToolInput(ToolKeys.Scripts) ?? L.T("Settings.TheFolderLastUsed");
    public string ScriptOutputFolder => S.ExplicitFolder(OutputKind.Scripts) ?? L.F("Settings.Default0", S.OutputFolderFor(OutputKind.Scripts));

    [RelayCommand] private void ChangeScriptInput() => ChangeToolFolder(ToolKeys.Scripts, input: true);
    [RelayCommand] private void ChangeScriptOutput() => ChangeToolFolder(ToolKeys.Scripts, input: false);

    [RelayCommand]
    private void ResetScripts()
    {
        S.Scripts = new ScriptSettings();
        S.Locations.Tools.Remove(ToolKeys.Scripts);
        Changed();
        RaiseAll();
    }

    // ================================================================== Tools
    private List<ToolSettingsGroup>? _toolGroups;

    /// <summary>Tools that declare settings in their manifests, with generated controls.</summary>
    public IReadOnlyList<ToolSettingsGroup> ToolSettingGroups => _toolGroups ??= Services.Tools.Tools
        .Where(t => t.Settings.Any(s => s.Visible)).Select(t => new ToolSettingsGroup(this, t)).ToList();

    /// <summary>A tool setting changed here: saved, and the tool's page is told.</summary>
    internal void ToolSettingChanged(ToolManifest tool)
    {
        Changed();
        Services.RaiseToolSettingsChanged(tool.Id);
    }

    // ================================================================== Diagnostics
    public bool PerformanceMonitoring
    {
        get => S.Diagnostics.PerformanceMonitoring;
        set => Set(v => S.Diagnostics.PerformanceMonitoring = v, value);
    }

    public IReadOnlyList<int> SampleIntervals => DiagnosticsSettings.SampleIntervals;

    public int SampleIntervalSeconds
    {
        get => S.Diagnostics.SampleIntervalSeconds;
        set => Set(v => S.Diagnostics.SampleIntervalSeconds = v, value);
    }

    public IReadOnlyList<int> OperationHistorySizes { get; } = [50, 100, 200, 500, 1000];

    public int OperationHistoryEntries
    {
        get => S.Diagnostics.OperationHistoryEntries;
        set => Set(v => S.Diagnostics.OperationHistoryEntries = v, value);
    }

    public IReadOnlyList<int> LogTotalSizes { get; } = [50, 100, 200, 500, 1000, 2000];

    public int LogMaxTotalMegabytes
    {
        get => S.Diagnostics.LogMaxTotalMegabytes;
        set => Set(v => S.Diagnostics.LogMaxTotalMegabytes = v, value, Services.ApplyLogSettings);
    }

    public IReadOnlyList<int> ReportLogDayChoices { get; } = [1, 3, 7, 14];

    public int ReportLogDays
    {
        get => S.Diagnostics.ReportLogDays;
        set => Set(v => S.Diagnostics.ReportLogDays = v, value);
    }

    [RelayCommand]
    private void ResetDiagnostics()
    {
        // The total size of the log files belongs to "Notifications and logging" on the page, so its reset keeps it.
        S.Diagnostics = new DiagnosticsSettings { LogMaxTotalMegabytes = S.Diagnostics.LogMaxTotalMegabytes };
        Services.ApplyLogSettings();
        Changed();
        RaiseAll();
    }

    [RelayCommand]
    private void OpenDiagnostics()
    {
        if (_main.Pages.OfType<DiagnosticsViewModel>().FirstOrDefault() is { } page) _main.SelectedPage = page;
    }

    // ================================================================== Advanced
    /// <summary>Original "Save Settings after this session" (not remembered; on at every start).</summary>
    public bool SaveSettingsThisSession
    {
        get => Services.PersistSettings;
        set
        {
            Services.PersistSettings = value;
            if (value) Services.SaveSettings();
            OnPropertyChanged();
        }
    }

    public string SettingsFile => Services.SettingsStore.FilePath;

    /// <summary>Where data is kept: next to the program (portable) or in the user profile.</summary>
    public string StorageMode => Services.Paths switch
    {
        { IsPortable: true } => L.T("Settings.PortableSettingsAndLogsAreKept") + AppPaths.PortableDataFolder + L.T("Settings.FolderNextToTheProgram"),
        { Notice: { } notice } => notice,
        _ => L.T("Settings.InstalledSettingsAndLogsAreKept"),
    };
    public string? LogFile => AppLog.LogFilePath;

    /// <summary>Memory, cache, temporary-file and backup figures for the Advanced section.</summary>
    [ObservableProperty] private string _storageSummary = "";

    [RelayCommand] private void OpenSettingsFolder() => Services.Shell.OpenFolder(SettingsFile);
    [RelayCommand] private void OpenLogFolder() => Services.Shell.OpenFolder(LogFile);
    [RelayCommand] private void OpenBackupFolder() => OpenOutputFolder(OutputKind.Backups);

    private async Task RefreshStorageAsync()
    {
        long temp = await Task.Run(() => Directory.Exists(TempFolder) ? Services.Temp.Size() : 0);
        var backups = await Task.Run(() => Directory.Exists(BackupFolder) ? Services.Backups.List() : []);
        StorageSummary = L.F("Settings.SpriteSheetsInMemory0Of", (Services.Data.Sprites as SpriteRepository)?.CachedSheets ?? 0, S.Performance.SpriteSheetCache, ReportWriter.FormatSize(temp), L.Count(backups.Count, "Noun.set"), ReportWriter.FormatSize(backups.Sum(b => b.TotalSize)));
    }

    [RelayCommand]
    private void ClearCaches()
    {
        (Services.Data.Sprites as SpriteRepository)?.ClearCache();
        Services.Data.Thumbnails.Clear();
        GC.Collect();
        Info(L.T("Settings.SpriteAndThumbnailCachesCleared"));
        _ = RefreshStorageAsync();
    }

    [RelayCommand]
    private void CleanTemp()
    {
        int removed = Services.Temp.CleanUp(TimeSpan.Zero);
        Info(L.F("Settings.0RemovedFoldersOfRunningOperations", L.Count(removed, "Noun.temporary_folder")));
        _ = RefreshStorageAsync();
    }

    /// <summary>Imports paths and the creator name ("Author") from a settings.json of the original 1.x program.</summary>
    [RelayCommand]
    private void ImportLegacy()
    {
        string? file = Services.Dialogs.OpenFile(L.T("Settings.SettingsJsonOfTheOriginal1"), L.T("Settings.SettingsJsonSettingsJsonJSONFiles"));
        if (file == null) return;
        if (Try(L.T("Settings.ImportingSettings"), () => LegacySettings.Import(file, S)))
        {
            S.Normalize();
            _creatorInput = null;
            Services.SetCreatorName(S.CreatorName);
            Changed(refreshPaths: true);
            RaiseAll();
            Info(L.T("Settings.SettingsImportedFrom") + file);
        }
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        if (!Services.Dialogs.Confirm(L.T("Settings.ResetAllSettingsToTheirDefaults"), L.T("Settings.ResetSettings"), L.T("Settings.Reset"), L.T("Common.Cancel"))) return;
        var fresh = new AppSettings();
        fresh.Normalize();
        // Options written by a newer version (and the format number) are kept: a reset must not downgrade them.
        CopyAll(fresh, S, keepUserContent: true);
        _creatorInput = null;
        ApplyEverything();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(string.Empty);
        foreach (var row in ToolFolders) row.Refresh();
        foreach (var row in OutputFolders) row.Refresh();
        foreach (var group in ToolSettingGroups) foreach (var row in group.Settings) row.Refresh();
        RebuildLocationRows();
    }

    public override Task OnNavigatedToAsync()
    {
        RaiseAll();
        Themes.Refresh();
        return RefreshStorageAsync();
    }
}
