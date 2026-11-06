using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Compatibility;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.Themes;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Localization;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Tools;

namespace TibiaFoundry.App.ViewModels;

/// <summary>Shell: navigation, theme switching, client package switching and the status bar.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;

    /// <param name="pages">
    /// The pages to offer (default: <see cref="BuiltInTools.Pages"/>). Each page's manifest is
    /// validated and registered first; a page whose manifest is invalid, or whose view-model cannot
    /// be created, is left out (and reported in the log and on the Diagnostics page) while the rest
    /// of the program starts normally.
    /// </param>
    public MainViewModel(AppServices services, IEnumerable<PageRegistration>? pages = null)
    {
        _services = services;
        Pages = new ObservableCollection<PageViewModel>();
        foreach (var registration in pages ?? BuiltInTools.Pages)
        {
            if (!services.Tools.TryRegister(registration.Manifest, out _)) continue;
            Registrations.Add(registration);
            // Settings a tool stored with an older settings version are upgraded before the tool reads them.
            services.ToolSettings.Migrate(registration.Manifest);
            try
            {
                Pages.Add(registration.Create(services, this));
            }
            catch (Exception ex)
            {
                AppLog.Error($"The page \"{registration.Manifest.Name}\" could not be created; it is not shown.", ex, source: "Tools");
                LoadFailures.Add($"{registration.Manifest.Name}: {ex.GetBaseException().Message}");
            }
        }
        if (Pages.Count == 0) throw new InvalidOperationException("No page could be created.");
        Appearances = Pages.OfType<AppearancesViewModel>().FirstOrDefault();
        // The line under the server switch follows the client in use (7.3).
        services.Data.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ClientDataService.ClientVersion) or nameof(ClientDataService.ImportedVersion) or nameof(ClientDataService.PackageDisplayName))
                OnPropertyChanged(nameof(ClientInUse));
        };
        // Settings → General: the page used last, or a fixed start page.
        string? start = services.Settings.General.StartupPage == StartupPageMode.Specific ? services.Settings.General.StartupPageName : services.Settings.LastPage;
        _selectedPage = Pages.FirstOrDefault(p => p.GetType().Name == start) ?? Pages[0];
        if (Pages.OfType<ClientsViewModel>().FirstOrDefault() is { } clients && Pages.OfType<RebuilderViewModel>().FirstOrDefault() is { } rebuilder)
            clients.OpenRebuilder = key =>
            {
                SelectedPage = rebuilder;
                rebuilder.Select(key);
            };
        if (Pages.OfType<ClientsViewModel>().FirstOrDefault() is { } clientsPage) clientsPage.Archived = WithdrawSettledClientChanges;
        if (Pages.OfType<AssetsEditorViewModel>().FirstOrDefault() is { } assetsEditor && Pages.OfType<ClientConverterViewModel>().FirstOrDefault() is { } converter)
        {
            assetsEditor.OpenInConverter = folder =>
            {
                converter.UseAssetsFolder(folder);
                SelectedPage = converter;
            };
        }
        services.OpenAppearanceRequested += (kind, id) =>
        {
            if (Appearances == null) return;
            SelectedPage = Appearances;
            _ = Appearances.RevealAsync(kind, id);
        };
        services.NavigateRequested += type =>
        {
            if (Pages.FirstOrDefault(p => p.GetType() == type) is { } page) SelectedPage = page;
        };
        services.OpenOutfitterRequested += (outfitId, colors, addons) =>
        {
            if (Appearances == null) return;
            SelectedPage = Appearances;
            _ = Appearances.OpenInOutfitterAsync(outfitId, colors, addons);
        };
        // The Asset Library records which tool an export came from, and reports from its background work (8.8).
        // (The tool's English name: it is stored in the library and compared, so it never depends on the interface language.)
        services.Library.CurrentTool = () => SelectedPage?.Manifest.Name;
        services.ContentStudioRequested += action =>
        {
            if (Pages.OfType<ContentStudioViewModel>().FirstOrDefault() is not { } studio) return;
            SelectedPage = studio;
            action(studio);
        };
        services.Library.Notice += message => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => _services.Status.Show(message, StatusKind.Info));
        services.Status.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StatusService.Link)) RevealStatusLinkCommand.NotifyCanExecuteChanged();
        };
        services.Data.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ClientDataService.Compatibility)) UpdateCompatibilityNotices(services.Data.Compatibility);
        };
        AppLog.MessageLogged += (level, message) =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (level == LogLevel.Error) _services.Status.Show(message, StatusKind.Error);
            });
    }

    public AppServices Services => _services;
    public StatusService Status => _services.Status;
    public ClientDataService Data => _services.Data;
    public AppearancesViewModel? Appearances { get; }
    public ObservableCollection<PageViewModel> Pages { get; }

    /// <summary>Pages whose view-model could not be created (their manifests were valid).</summary>
    public List<string> LoadFailures { get; } = [];

    /// <summary>The registrations whose manifests were accepted (their views are connected by the application).</summary>
    public List<PageRegistration> Registrations { get; } = [];

    [ObservableProperty] private PageViewModel _selectedPage;

    /// <summary>False when the window is narrow and the navigation rail shows icons only.</summary>
    [ObservableProperty] private bool _isNavExpanded = true;

    partial void OnSelectedPageChanged(PageViewModel? oldValue, PageViewModel newValue)
    {
        _services.Settings.LastPage = newValue.GetType().Name;
        if (newValue is not HomeViewModel) _services.Settings.RememberPage(newValue.GetType().Name);
        CheckClientChange();
        newValue.RefreshAvailability();
        if (oldValue is DiagnosticsViewModel diagnostics) diagnostics.OnNavigatedFrom();
        _ = newValue.OnNavigatedToAsync();
    }

    /// <summary>The client every page reads, for the line under the server switch ("Live · 15.33.9d148f (imported)").</summary>
    public string ClientInUse
    {
        get
        {
            try
            {
                return _services.Clients.Describe(_services.Settings.ActiveScope, _services.Data.ClientVersion);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return _services.Data.PackageDisplayName;
            }
        }
    }

    /// <summary>Opens the Clients page (from the line under the server switch).</summary>
    [RelayCommand]
    private void OpenClients()
    {
        if (Pages.OfType<ClientsViewModel>().FirstOrDefault() is { } page) SelectedPage = page;
    }

    /// <summary>The window got focus: the user may have just updated the Tibia client.</summary>
    public void OnWindowActivated() => CheckClientChange();

    /// <summary>Detects a client update while the program runs and discards stale data (never during a running operation).</summary>
    private void CheckClientChange()
    {
        if (Pages.Any(p => p.IsBusy)) return;
        string? message = _services.Data.CheckForClientChange();
        if (message == null) return;
        Status.Show(message, StatusKind.Warning);
        UpdateCompatibilityNotices(null);
        _ = SelectedPage.OnNavigatedToAsync();
        _ = RunCompatibilityCheckAsync();
    }

    /// <summary>Background check of the installed client's data; marks affected pages.</summary>
    public async Task RunCompatibilityCheckAsync()
    {
        if (_services.Data.IsCheckingCompatibility) return;
        try
        {
            var report = await _services.Data.CheckCompatibilityAsync();
            UpdateCompatibilityNotices(report);
            if (report.Overall > CompatibilityStatus.Unfamiliar)
            {
                var worst = report.Components.Where(c => c.Status > CompatibilityStatus.Unfamiliar).Select(c => $"{c.Component} ({CompatibilityReport.Describe(c.Status).ToLowerInvariant()})");
                Status.Show(L.F("Main.SomeClientFilesAreNotFully", string.Join(", ", worst)), StatusKind.Warning);
            }
            else if (_services.Data.SpriteFolderNotice is { } folder)
            {
                Status.Show(folder, StatusKind.Warning);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Error("The compatibility check failed.", ex);
        }
    }

    private void UpdateCompatibilityNotices(CompatibilityReport? report)
    {
        foreach (var page in Pages)
        {
            var affected = report?.Components.Where(c => c.Status > CompatibilityStatus.Unfamiliar && page.DataComponents.Contains(c.Component)).ToList();
            page.CompatibilityNotice = affected is { Count: > 0 }
                ? string.Join("   ", affected.Select(c => $"{c.Component}: {CompatibilityReport.Describe(c.Status)} — {c.Summary}"))
                : null;
            // A missing or unreadable file can be located from the banner.
            page.CompatibilityLocation = affected?.FirstOrDefault(c => c.Status is CompatibilityStatus.Missing or CompatibilityStatus.Unsupported && c.LocationId != null)?.LocationId;
        }
    }

    /// <summary>"Locate…" in a page's banner: picks the file or folder the page is missing.</summary>
    [RelayCommand]
    private async Task LocateForPageAsync(string? locationId)
    {
        if (locationId == null) return;
        await _services.Locations.LocateAsync(locationId);
        SelectedPage.RefreshAvailability();
        if (_services.Data.AssetsAvailable) _ = SelectedPage.OnNavigatedToAsync();
    }

    [RelayCommand]
    private void OpenCompatibility() => SelectedPage = Pages.First(p => p is CompatibilityViewModel);

    public bool IsDarkTheme => ThemeManager.IsDark;

    /// <summary>The built-in theme. Choosing one also leaves an active custom theme.</summary>
    public ThemePreference Theme
    {
        get => _services.Settings.Theme;
        set
        {
            if (_services.Settings.Theme == value && _services.Settings.Themes.ActiveCustomTheme == null) return;
            _services.Settings.Theme = value;
            _services.Settings.Themes.ActiveCustomTheme = null;
            ThemeManager.Apply(value);
            _services.SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDarkTheme));
            ThemeSwitched?.Invoke();
        }
    }

    /// <summary>The interface languages for the language menu next to the theme switch.</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions => _languageOptions ??= Languages.All.Select(l => new LanguageOption(this, l)).ToList();
    private List<LanguageOption>? _languageOptions;

    /// <summary>The language in effect.</summary>
    public LanguageDefinition CurrentLanguage => L.Language;

    /// <summary>The language button's tooltip: what it does and the language in effect.</summary>
    public string LanguageToolTip => L.F("Main.LanguageToolTip", L.Language.NativeName);

    /// <summary>
    /// Switches the interface language at once (no restart): the text of every window, page, menu
    /// and dialog follows, the current page, its settings and anything entered stay as they are, and
    /// the choice is saved for the next start.
    /// </summary>
    public void ChangeLanguage(string code)
    {
        var before = L.Language;
        var language = Localization.LocalizationManager.Apply(code);
        _services.Settings.Language = language.Code;
        _services.SaveSettings();
        if (language == before) return;
        RefreshLanguage();
        Status.Show(L.F("Main.LanguageChanged", language.NativeName), StatusKind.Info);
    }

    /// <summary>Makes the shell and every page re-read their text after a language switch.</summary>
    public void RefreshLanguage()
    {
        foreach (var option in LanguageOptions) option.Refresh();
        foreach (var page in Pages) page.RefreshLanguage();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Raised after the theme was switched from outside Settings (F6, the navigation bar button).</summary>
    public event Action? ThemeSwitched;

    /// <summary>
    /// Header button and F6: switches to the built-in theme opposite to the current brightness. A
    /// custom theme stays saved and can be chosen again in Settings → Appearance &amp; Themes.
    /// </summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        if (Pages.OfType<SettingsViewModel>().FirstOrDefault()?.Themes.ConfirmLeavingDraft() == false) return;
        bool wasCustom = _services.Settings.Themes.ActiveCustomTheme != null;
        Theme = ThemeManager.IsDark ? ThemePreference.Light : ThemePreference.Dark;
        if (wasCustom) Status.Show(L.F("Main.SwitchedToTheBuiltIn0", Theme.ToString().ToLowerInvariant()), StatusKind.Info);
    }

    /// <summary>Setup, the startup choice and the recovery dialog (8.0); also run from Settings → Tibia client.</summary>
    public ClientStartup Startup => _startup ??= new ClientStartup(_services);
    private ClientStartup? _startup;

    /// <summary>The server or client was changed outside this window's switch (setup, the startup choice, Settings): the window follows.</summary>
    public void ClientChangedElsewhere()
    {
        OnPropertyChanged(nameof(UseTestServer));
        OnPropertyChanged(nameof(ClientInUse));
    }

    public bool UseTestServer
    {
        get => _services.Settings.UseTestServer;
        set
        {
            if (_services.Settings.UseTestServer == value) return;
            if (_services.WhyNotSwitchClient() is { } why)
            {
                _services.Status.Show(why, StatusKind.Warning);
                OnPropertyChanged();   // the switch in the window goes back
                return;
            }
            _services.Settings.UseTestServer = value;
            _services.Data.SwitchClient();
            _services.SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ClientInUse));
            if (!_services.Data.AssetsAvailable)
            {
                _services.Status.Show(L.F("Main.NoClientFilesWereFoundFor", _services.Settings.ActivePackage, _services.Data.AssetsDirectory), StatusKind.Warning);
            }
            else
            {
                _services.Status.Show(L.F("Main.SwitchedTo0", _services.Data.PackageDisplayName), StatusKind.Success);
                _ = SelectedPage.OnNavigatedToAsync();
            }
        }
    }

    [RelayCommand]
    private void CancelOperation() => SelectedPage.CancelOperation();

    [RelayCommand]
    private void OpenLog() => _services.Shell.OpenFolder(AppLog.LogFilePath);

    private bool HasStatusLink => Status.Link != null;

    /// <summary>The saved file or folder named in the status bar: opens File Explorer there.</summary>
    [RelayCommand(CanExecute = nameof(HasStatusLink))]
    private void RevealStatusLink()
    {
        if (Status.Link is { } link) _services.RevealSaved(link);
    }

    [RelayCommand]
    private void OpenDiagnostics()
    {
        if (Pages.OfType<DiagnosticsViewModel>().FirstOrDefault() is { } page) SelectedPage = page;
    }

    /// <summary>The link in the tagline next to the logo.</summary>
    [RelayCommand]
    private void OpenTibiaWiki() => _services.Shell.OpenUrl(Core.Net.ExternalEndpoints.CreditTibiaWiki);

    /// <summary>Pages with unsaved changes, described for the closing question (empty when nothing would be lost).</summary>
    public IReadOnlyList<string> UnsavedChanges => Pages.Select(p => p.UnsavedChanges).OfType<string>().ToList();

    public async Task StartAsync()
    {
        await SelectedPage.OnNavigatedToAsync();
        await RunCompatibilityCheckAsync();
        RemindOfLeftoverHotkeyBackups();
        RemindOfLeftoverRebuilds();
        await CheckInstalledClientsAsync();
    }

    // ------------------------------------------------------------------ launch check (8.0)

    private readonly Queue<ClientChangeNotice> _clientChanges = new();

    /// <summary>"The installed client changed / is not archived yet": Archive, Later or Don't ask (non-modal, above the status bar), or null.</summary>
    [ObservableProperty] private ClientChangeNotice? _clientChange;

    /// <summary>The background archive started by the launch check (tests wait for it), or null.</summary>
    public Task? BackgroundArchive { get; private set; }

    /// <summary>
    /// Checks the monitored installed clients after the first page shows (Settings → Tibia client):
    /// in the background, never with a dialog. What follows depends on the change handling:
    /// Automatic archives at once, Ask shows a notice, Ask quietly only marks the Clients page.
    /// Content that is archived already, or that was answered "Don't ask", is left alone.
    /// </summary>
    public async Task CheckInstalledClientsAsync()
    {
        if (!_services.Clients.IsConfigured) return;
        foreach (var scope in new[] { ClientScope.Live, ClientScope.TestServer })
        {
            if (!_services.Settings.Monitors(scope)) continue;
            InstalledCheck? check;
            try
            {
                check = await Task.Run(() => _services.Clients.CheckInstalled(scope));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            {
                AppLog.Warn("The installed client could not be checked: " + ex.Message);
                continue;
            }
            if (check == null) continue;
            string which = scope == ClientScope.Live ? L.T("Main.LiveClient") : L.T("Main.TestServerClient");
            if (check.Result.Outcome == Core.Clients.LaunchCheckOutcome.Unavailable)
            {
                Status.Show(L.F("Main.TheInstalled0IsNotAvailable", which, check.Result.Folder), StatusKind.Warning);
                continue;
            }
            if (!check.Result.Accepted) continue;   // launcher running or files changing: logged, shown on the Clients page, checked again next time
            var status = _services.Clients.InstalledStatusOf(scope);
            if (status is not { NeedsArchive: true }) continue;
            switch (_services.Settings.ClientChangeHandling)
            {
                case ClientChangeHandling.Automatic:
                    await ArchiveCheckedAsync(check);
                    break;
                case ClientChangeHandling.Ask:
                    _clientChanges.Enqueue(new ClientChangeNotice(check, DescribeChange(check)));
                    ClientChange ??= _clientChanges.Dequeue();
                    break;
                default:
                    break;   // Ask quietly: the Clients page shows it.
            }
        }
        RefreshClientsPage();
    }

    private static string DescribeChange(InstalledCheck check)
    {
        var r = check.Result;
        string which = check.Scope == ClientScope.Live ? L.T("Main.LiveClient") : L.T("Main.TestServerClient");
        string version = r.Version.Version ?? L.T("Main.VersionUnknown");
        if (r.Outcome == Core.Clients.LaunchCheckOutcome.Changed)
            return L.F("Main.TheInstalled0Changed12", which, r.Previous?.Version ?? "unknown", version, r.ChangeSummary());
        return L.F("Main.TheInstalled01IsNot", which, version);
    }

    private async Task ArchiveCheckedAsync(InstalledCheck check)
    {
        string which = check.Scope == ClientScope.Live ? L.T("Main.LiveClient") : L.T("Main.TestServerClient");
        Status.Show(L.F("Main.ArchivingTheInstalled0InThe", which), StatusKind.Info);
        var work = Task.Run(() => _services.Clients.ArchiveChecked(check));
        BackgroundArchive = work;
        try
        {
            var result = await work;
            Status.Show(result.AlreadyStored ? L.F("Main.TheInstalled0IsAlreadyArchived", which, result.Version.Label)
                : L.F("Main.ArchivedTheInstalled0As1", which, result.Version.Label, result.Version.Files, Core.FileOps.ReportWriter.FormatSize(result.NewBytes)), StatusKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            AppLog.Warn($"The installed {which} could not be archived: {ex.Message}");
            Status.Show(L.F("Main.TheInstalled0CouldNotBe", which, ex.Message), StatusKind.Warning);
        }
        RefreshClientsPage();
    }

    private void RefreshClientsPage()
    {
        if (Pages.OfType<ClientsViewModel>().FirstOrDefault() is { } clients && ReferenceEquals(SelectedPage, clients)) clients.Refresh();
    }

    private void NextClientChange() => ClientChange = _clientChanges.Count > 0 ? _clientChanges.Dequeue() : null;

    /// <summary>
    /// After an archive made elsewhere (the Clients page): notices about installed content that is no
    /// longer waiting to be archived are withdrawn, so the notice never says "not archived yet" about
    /// content that is archived now (8.5).
    /// </summary>
    public void WithdrawSettledClientChanges()
    {
        bool Pending(ClientChangeNotice n)
        {
            try
            {
                return _services.Clients.InstalledStatusOf(n.Check.Scope) is { NeedsArchive: true };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return true;   // cannot tell: keep the notice
            }
        }
        var waiting = _clientChanges.Where(Pending).ToList();
        _clientChanges.Clear();
        foreach (var n in waiting) _clientChanges.Enqueue(n);
        if (ClientChange is { } shown && !Pending(shown)) NextClientChange();
    }

    [RelayCommand]
    private async Task ArchiveClientChangeAsync()
    {
        if (ClientChange is not { } notice) return;
        NextClientChange();
        await ArchiveCheckedAsync(notice.Check);
    }

    /// <summary>Later: asked again at the next start.</summary>
    [RelayCommand]
    private void LaterClientChange() => NextClientChange();

    [RelayCommand]
    private void DontAskClientChange()
    {
        if (ClientChange is not { } notice) return;
        NextClientChange();
        try
        {
            _services.Clients.DismissChange(notice.Check.Scope, notice.Check.Result.Fingerprint!, notice.Check.Result.Version.Version);
            Status.Show(L.T("Main.NotAskedAgainForThisContent"), StatusKind.Info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status.Show(L.T("Main.TheAnswerCouldNotBeSaved") + ex.Message, StatusKind.Warning);
        }
        RefreshClientsPage();
    }

    /// <summary>A rebuild that was interrupted left a staging folder next to its target (see Diagnostics).</summary>
    private void RemindOfLeftoverRebuilds()
    {
        try
        {
            if (!_services.Clients.StoreExists) return;
            var leftovers = _services.Clients.Store.LeftoverRebuilds();
            if (leftovers.Count == 0) return;
            Status.Show(L.F("Main.AnInterruptedRebuildLeft0For", L.Count(leftovers.Count, "Noun.unfinished_folder"), leftovers[0].StagingFolder), StatusKind.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppLog.Warn("Client archive: " + ex.Message);
        }
    }

    /// <summary>A copy of the client's options file that the hotkey tool removed in 8.0 never put back (see <see cref="LeftoverHotkeyBackups"/>).</summary>
    private void RemindOfLeftoverHotkeyBackups()
    {
        var leftovers = LeftoverHotkeyBackups.Find(_services.LocalDataDirectory);
        if (leftovers.Count == 0) return;
        Status.Show(LeftoverHotkeyBackups.Describe(leftovers[0]) + (leftovers.Count > 1 ? L.F("Main.0SuchCopiesSeeDiagnostics", leftovers.Count) : ""), StatusKind.Warning);
        AppLog.Warn($"{leftovers.Count} copy(ies) of the client's options file left by the removed hotkey tool were found.");
    }
}

/// <summary>A launch-check result waiting for an answer (see <see cref="MainViewModel.ClientChange"/>).</summary>
public sealed record ClientChangeNotice(InstalledCheck Check, string Text);

/// <summary>One entry of the language menu.</summary>
public sealed partial class LanguageOption(MainViewModel owner, LanguageDefinition definition) : ObservableObject
{
    public LanguageDefinition Definition { get; } = definition;

    /// <summary>The resource key of the flag image (Themes/Flags.xaml).</summary>
    public string FlagKey => "Flag." + Definition.Code;

    public bool IsSelected => L.Language == Definition;

    public void Refresh() => OnPropertyChanged(nameof(IsSelected));

    [RelayCommand]
    private void Select() => owner.ChangeLanguage(Definition.Code);
}
