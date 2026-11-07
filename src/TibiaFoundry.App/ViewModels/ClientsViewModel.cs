using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>What a row of the Clients page is.</summary>
public enum ClientRowKind { Installed, Folder, Version }

/// <summary>A client folder: the installed client of a server, or a folder you added (a copy, a download, an assets folder, a rebuilt installation).</summary>
public sealed record SourceRow(ClientRowKind Kind, string Id, string Label, string TypeText, string? Version, string Status, bool IsProblem, string Path,
    string Archived, bool IsActive, bool IsMonitored, string? Problem)
{
    public string ActiveText => IsActive ? L.T("Clients.InUse") : "";
    public bool IsFolder => Kind == ClientRowKind.Folder;
    public bool IsAvailable => !IsProblem;
}

/// <summary>A labeled value of the selected client or revision (8.8: shown as a two-column list instead of a text block).</summary>
public sealed record DetailFact(string Label, string Value, bool IsPath = false, bool IsWarning = false);

/// <summary>A category of files a revision left out (personal data), for the revision's details table.</summary>
public sealed record ExclusionRow(string Category, string Files, string Size, string Reason);

/// <summary>An archived revision: a preserved snapshot with its scope, storage and completeness.</summary>
public sealed record RevisionRow(string Key, string Label, string? Version, string VersionText, string ScopeText, string StorageText, string Completeness,
    string Status, bool IsUsable, string Archived, string Files, string Size, string Unique, string Source, bool IsActive, bool IsInstallation, string? Problem)
{
    public string ActiveText => IsActive ? L.T("Clients.InUse") : "";
    public string RebuildText => IsInstallation ? L.T("Clients.RebuildInstallation") : L.T("Clients.RestoreFolder");
}

/// <summary>
/// "Clients" (7.3, extended in 8.0): the client folders Tibia Foundry knows (the installed clients,
/// folders you added) and the archived revisions, for the live server and the test server. Loading a
/// folder only reads it; archiving is always a separate, explicit action; the client in use is chosen
/// here. Tibia client folders are only ever read.
/// </summary>
public sealed partial class ClientsViewModel : PageViewModel
{
    public ClientsViewModel(AppServices services) : base(services, BuiltInTools.Clients)
    {
    }

    private ClientLibrary Library => Services.Clients;

    /// <summary>Opens the Client Installation Rebuilder with a revision (set by the main window).</summary>
    public Action<string>? OpenRebuilder { get; set; }

    /// <summary>Called after a revision was archived (the main window withdraws its "not archived yet" notice).</summary>
    public Action? Archived { get; set; }

    /// <summary>Where Find installation looks; null = this PC's usual places (tests give their own folders).</summary>
    public Func<IReadOnlyList<InstallationRoot>>? FindRoots { get; set; }

    /// <summary>0 = live server, 1 = test server.</summary>
    [ObservableProperty] private int _serverTab;

    [ObservableProperty] private IReadOnlyList<SourceRow> _sources = [];
    [ObservableProperty] private SourceRow? _selectedSource;
    [ObservableProperty] private IReadOnlyList<RevisionRow> _revisions = [];
    [ObservableProperty] private RevisionRow? _selectedRevision;
    [ObservableProperty] private string _sourceDetails = "";
    [ObservableProperty] private string _revisionDetails = "";
    [ObservableProperty] private IReadOnlyList<DetailFact> _sourceFacts = [];
    [ObservableProperty] private IReadOnlyList<DetailFact> _revisionFacts = [];
    [ObservableProperty] private IReadOnlyList<ExclusionRow> _revisionExclusions = [];
    [ObservableProperty] private IReadOnlyList<string> _revisionProblems = [];

    public string SourceCount => L.Count(Sources.Count, "Noun.folder");
    public string RevisionCount => L.Count(Revisions.Count, "Noun.revision");
    public bool HasRevisions => Revisions.Count > 0;
    partial void OnSourcesChanged(IReadOnlyList<SourceRow> value) => OnPropertyChanged(nameof(SourceCount));
    partial void OnRevisionsChanged(IReadOnlyList<RevisionRow> value)
    {
        OnPropertyChanged(nameof(RevisionCount));
        OnPropertyChanged(nameof(HasRevisions));
    }
    [ObservableProperty] private string _storageSummary = "";
    [ObservableProperty] private string _storeLocation = "";
    [ObservableProperty] private string _archiveSettings = "";

    /// <summary>"This installed version is not archived yet…" for the server shown, or null.</summary>
    [ObservableProperty] private string? _reminder;

    private ClientScope Scope => ServerTab == 1 ? ClientScope.TestServer : ClientScope.Live;
    private string Server => ClientServers.Of(Scope);

    public bool IsPortable => Services.Paths?.IsPortable == true;
    public bool CanMoveStore => Library.IsConfigured && !IsPortable;

    /// <summary>The "Live server" segment.</summary>
    public bool ShowLive
    {
        get => ServerTab == 0;
        set { if (value) ServerTab = 0; }
    }

    /// <summary>The "Test server" segment.</summary>
    public bool ShowTest
    {
        get => ServerTab == 1;
        set { if (value) ServerTab = 1; }
    }

    partial void OnServerTabChanged(int value)
    {
        OnPropertyChanged(nameof(ShowLive));
        OnPropertyChanged(nameof(ShowTest));
        Refresh();
    }

    partial void OnSelectedSourceChanged(SourceRow? value)
    {
        SourceDetails = value == null ? "" : DescribeSource(value);
        SourceFacts = value == null ? [] : SourceFactsOf(value);
    }

    partial void OnSelectedRevisionChanged(RevisionRow? value)
    {
        RevisionDetails = value == null ? "" : DescribeRevision(value);
        (RevisionFacts, RevisionExclusions, RevisionProblems) = value == null ? ([], [], []) : RevisionFactsOf(value);
    }

    public override Task OnNavigatedToAsync()
    {
        Refresh();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ the lists

    private ClientStore? SafeStore()
    {
        try
        {
            return Library.ExistingStore;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Client store: " + ex.Message);
            return null;
        }
    }

    /// <summary>Rebuilds both lists of the server shown, the storage figures and the reminder.</summary>
    public void Refresh(string? selectSource = null, string? selectRevision = null)
    {
        selectSource ??= SelectedSource?.Id;
        selectRevision ??= SelectedRevision?.Key;
        string? active = Services.Settings.ActiveClient(Scope);
        var store = SafeStore();

        var sources = new List<SourceRow>();
        var installed = SafeInstalledStatus();
        string installedFolder = Library.InstalledPackageFolder(Scope);
        bool isInstalled = Directory.Exists(installedFolder);
        var installedRegistration = store?.Registry.Registration(ClientRegistration.InstalledFor(Scope));
        sources.Add(new SourceRow(ClientRowKind.Installed, ClientRegistration.InstalledFor(Scope), ClientRegistry.DefaultAutomatic(Scope).Label, L.T("Clients.InstalledClientLauncher"),
            isInstalled ? TibiaClientPaths.ReadVersion(Path.Combine(installedFolder, TibiaFormat.PackageManifest)) : installed?.Version,
            InstalledText(isInstalled, installed), !isInstalled, installedFolder, ArchivedCount(store, installedRegistration), active == null, Services.Settings.Monitors(Scope),
            isInstalled ? installed?.Problem : L.T("Clients.TheInstalledClientWasNotFound")));
        if (store != null)
        {
            foreach (var r in store.Registry.Registrations.Where(r => !r.IsAutomatic && r.Server == Server))
            {
                string? folder = r.ResolveSource(Library.ProgramDirectory);
                bool exists = folder != null && Directory.Exists(folder);
                string? version = exists ? TibiaClientPaths.ReadVersion(Path.Combine(folder!, TibiaFormat.PackageManifest)) : r.LastSource?.Version;
                sources.Add(new SourceRow(ClientRowKind.Folder, r.Id, r.Label, SourceTypes.Display(r.EffectiveSourceType), version,
                    exists ? L.T("Clients.Available") : L.T("Clients.FolderMissing"), !exists, folder ?? L.T("ClientStartup.Unknown2"), ArchivedCount(store, r), active == r.Id, false,
                    exists ? null : L.T("Clients.TheFolderIsNotAvailableMoved")));
            }
        }
        Sources = sources;
        SelectedSource = sources.FirstOrDefault(r => r.Id == selectSource) ?? sources.FirstOrDefault(r => r.IsActive) ?? sources[0];

        var revisions = new List<RevisionRow>();
        if (store != null)
        {
            var usage = SafeUsages(store);
            foreach (var v in store.Registry.Versions.Where(v => v.Server == Server).OrderByDescending(v => v.ImportedAtUtc))
            {
                string source = v.RegistrationId != null && store.Registry.Registration(v.RegistrationId) is { } r ? r.Label : L.T("Clients.ArchivedEarlier");
                string status = v.Status switch
                {
                    VersionStatus.Imported => L.T("Clients.Available"),
                    VersionStatus.Damaged => L.T("Clients.StatusDamaged"),
                    _ => L.T("Clients.StatusMissing"),
                };
                usage.TryGetValue(v.Key, out var u);
                revisions.Add(new RevisionRow(v.Key, v.Label, v.ClientVersion, VersionText(v), ManifestScopes.Display(v.EffectiveScope), ManifestStorage.Display(v.EffectiveStorage),
                    Completeness(v), status, v.IsUsable, v.ImportedAtUtc == default ? "" : v.ImportedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    v.Files > 0 ? v.Files.ToString("N0") : "", v.Bytes > 0 ? ReportWriter.FormatSize(v.Bytes) : "", u != null ? ReportWriter.FormatSize(u.UniqueBytes) : "",
                    source, active == v.Key, v.IsCompleteInstallation, v.Problem));
            }
        }
        Revisions = revisions;
        SelectedRevision = revisions.FirstOrDefault(r => r.Key == selectRevision) ?? revisions.FirstOrDefault(r => r.IsActive) ?? revisions.FirstOrDefault();

        Reminder = installed is { NeedsArchive: true } && isInstalled
            ? L.F("Clients.TheInstalled01IsNot", (Scope == ClientScope.Live ? L.T("Clients.LiveClient") : L.T("Clients.TestServerClient")), installed.Version ?? L.T("Clients.VersionUnknown")) +
              (installed.Check?.Outcome == LaunchCheckOutcome.Changed ? L.F("Clients.ItChangedSinceTheLastCheck", installed.Check.ChangeSummary()) : "") +
              L.T("Clients.WhenTheLauncherUpdatesItThis")
            : null;
        StoreLocation = Library.Root == null ? L.T("Clients.TheClientArchiveIsNotAvailable") : L.F("Clients.ClientArchive0", Library.Root) + (IsPortable ? L.T("Clients.InsideTibiaFoundryDataNextToThe") : "");
        StorageSummary = DescribeUsage(store);
        ArchiveSettings = L.F("Clients.NewArchives0PersonalData1", ManifestStorage.Display(Library.StorageMode).ToLowerInvariant(), (Services.Settings.ArchivePersonalData ? "included" : L.T("Clients.LeftOutListedWithEachRevision")));
    }

    private static IReadOnlyDictionary<string, RevisionUsage> SafeUsages(ClientStore store)
    {
        try
        {
            return store.RevisionUsages().ToDictionary(u => u.Key, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Client archive usage: " + ex.Message);
            return new Dictionary<string, RevisionUsage>();
        }
    }

    private static string ArchivedCount(ClientStore? store, ClientRegistration? r) =>
        store == null || r == null || r.Versions.Count == 0 ? "" : $"{L.Count(r.Versions.Count, "Noun.revision")}";

    private static string VersionText(ClientVersionEntry v) => v.VersionEvidenceStatus switch
    {
        VersionReport.Conflicting => L.F("Clients.0ConflictingEvidence", v.ClientVersion),
        VersionReport.Unknown => v.ClientVersion != null ? L.F("Clients.0Probably", v.ClientVersion) : L.T("Clients.UnknownVersion"),
        _ => v.ClientVersion ?? L.T("Clients.UnknownVersion"),
    };

    /// <summary>"Complete", "Content complete — personal data left out (N files)", "Incomplete" or "Tibia Foundry files only".</summary>
    private static string Completeness(ClientVersionEntry v)
    {
        if (v.EffectiveScope == ManifestScopes.TibiaFoundryFiles) return L.T("Clients.TibiaFoundryFilesOnly73");
        if (v.Complete) return "Complete";
        return v.ExcludedFiles > 0 ? L.F("Clients.ClientContentCompletePersonalDataLeft", v.ExcludedFiles) : L.T("Clients.IncompleteSeeDetails");
    }

    private InstalledStatus? SafeInstalledStatus()
    {
        try
        {
            return Library.InstalledStatusOf(Scope);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppLog.Warn("The installed client could not be looked at: " + ex.Message);
            return null;
        }
    }

    /// <summary>The status column of the installed client: from the launch check (never read again here).</summary>
    private static string InstalledText(bool isInstalled, InstalledStatus? status)
    {
        if (!isInstalled) return L.T("Clients.NotInstalled");
        if (status?.Problem != null && status.Fingerprint == null) return L.T("Clients.AvailableNotCheckedYetSeeDetails");
        if (status?.Fingerprint == null) return L.T("Clients.AvailableNotCheckedYet");
        string text = status.ArchivedAs != null ? L.F("Clients.AvailableArchivedAs0", status.ArchivedAs.Label) : L.T("Clients.AvailableNotArchivedYet");
        return status.Problem != null ? text + L.T("Clients.AtTheLastAcceptedCheck") : text;
    }

    private static string DescribeUsage(ClientStore? store)
    {
        if (store == null) return L.T("Clients.NothingIsArchivedYetInSpace");
        var u = store.Usage();
        if (u.Versions == 0 && u.Objects == 0) return L.T("Clients.NothingIsArchivedYet");
        return L.F("Clients.01AsRestored2On", L.Count(u.Versions, "Noun.revision"), ReportWriter.FormatSize(u.LogicalBytes), ReportWriter.FormatSize(u.PhysicalBytes)) +
               (u.IndependentBytes > 0 ? L.F("Clients.OfWhich0IndependentCopies", ReportWriter.FormatSize(u.IndependentBytes)) : "") +
               L.F("Clients.0N0SavedBySharing", u.SharedPercent) +
               (u.ReclaimableBytes > 0 ? L.F("Clients.0IsNoLongerUsedBy", ReportWriter.FormatSize(u.ReclaimableBytes)) : "");
    }

    private string DescribeSource(SourceRow row)
    {
        var lines = new List<string> { $"{row.TypeText}: {row.Path}" };
        if (row.Version != null) lines.Add(L.F("Clients.VersionPackageJson0", row.Version));
        if (row.Kind == ClientRowKind.Installed)
        {
            lines.Add(row.IsMonitored ? L.T("Clients.CheckedForChangesAtEveryStart") : L.T("Clients.NotCheckedAtStartSettingsTibia"));
            if (SafeInstalledStatus() is { CheckedUtc: { } at } status)
                lines.Add(L.F("Clients.LastConsistentCheck0YyyyMM", at.ToLocalTime()) + (status.Check is { Scan: { } scan } check
                    ? L.F("Clients.0N0Files1N0Read", scan.Files.Count, scan.HashedFiles, scan.Duration.TotalSeconds, (check.Quick ? L.T("Clients.QuickCheckAChangeThatKeeps") : ""))
                    : "") + ".");
            if (row.Problem != null) lines.Add(row.Problem);
        }
        else
            lines.Add(L.T("Clients.LoadedFromWhereItIsTibia"));
        if (row.Archived.Length > 0) lines.Add("Archived: " + row.Archived);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The selected client folder as labeled values (8.8).</summary>
    private List<DetailFact> SourceFactsOf(SourceRow row)
    {
        var facts = new List<DetailFact> { new(L.T("Common.Folder"), row.Path, IsPath: true), new(L.T("Common.Kind"), row.TypeText), new(L.T("Common.Version"), row.Version ?? L.T("Clients.UnknownNoPackageJson")) };
        if (row.Kind == ClientRowKind.Installed)
        {
            facts.Add(new(L.T("Clients.CheckAtStart"), row.IsMonitored ? L.T("Clients.EveryStartInTheBackgroundSettings") : L.T("Clients.OffSettingsTibiaClient")));
            if (SafeInstalledStatus() is { CheckedUtc: { } at } status)
                facts.Add(new(L.T("Clients.LastConsistentCheck"), $"{at.ToLocalTime():yyyy-MM-dd HH:mm}" + (status.Check is { Scan: { } scan } check
                    ? L.F("Clients.0N0Files1N0Read2", scan.Files.Count, scan.HashedFiles, scan.Duration.TotalSeconds, (check.Quick ? L.T("Clients.QuickCheck") : "")) : "")));
        }
        else facts.Add(new(L.T("Clients.Use"), L.T("Clients.ReadWhereItIsNeverMoved")));
        facts.Add(new(L.T("Common.Archived"), row.Archived.Length > 0 ? row.Archived : L.T("Clients.NotYet")));
        if (row.Problem != null) facts.Add(new(L.T("Common.Note"), row.Problem, IsWarning: true));
        return facts;
    }

    /// <summary>The selected revision as labeled values, the table of what it left out, and its problems (8.8).</summary>
    private (List<DetailFact>, List<ExclusionRow>, List<string>) RevisionFactsOf(RevisionRow row)
    {
        var facts = new List<DetailFact>
        {
            new(L.T("Common.Version"), row.VersionText), new(L.T("Clients.Scope"), row.ScopeText), new(L.T("Clients.Completeness"), row.Completeness), new(L.T("Common.Storage"), row.StorageText),
            new(L.T("Common.Archived"), L.F("Clients.0From1", row.Archived, row.Source)),
        };
        var exclusions = new List<ExclusionRow>();
        var problems = new List<string>();
        try
        {
            var store = SafeStore();
            if (store?.Registry.Version(row.Key) is { IsUsable: true } v)
            {
                var m = ClientManifest.Read(v.ManifestPath(store.Root));
                facts.Add(new(L.T("Clients.Contents"), L.F("Clients.0N0Files1", m.Files.Count, ReportWriter.FormatSize(m.TotalBytes)) + (m.Directories is { Count: > 0 } d ? $", {L.Count(d.Count, "Noun.empty_folder")}" : "") +
                                         L.F("Clients.0N0NotListedByThe", m.Files.Count(f => f.ListedBy == null && m.Format >= 2))));
                if (m.VersionEvidence is { Count: > 0 } clues) facts.Add(new(L.T("Clients.VersionEvidence"), string.Join("; ", clues.Select(c => $"{c.Source}: {c.Value}{(c.Strong ? "" : " (weak)")}"))));
                foreach (var e in m.Exclusions ?? [])
                    exclusions.Add(new ExclusionRow(e.Rule, e.Files.ToString("N0"), ReportWriter.FormatSize(e.Bytes), e.Reason.Replace("personal data: ", "", StringComparison.Ordinal)));
                problems.AddRange((m.Problems ?? []).Take(20).Select(p => L.T("Clients.NotArchived") + p));
            }
        }
        catch (InvalidDataException ex)
        {
            problems.Add(L.T("Clients.TheManifestCannotBeRead") + ex.Message);
        }
        if (row.Problem != null) problems.Add(row.Problem);
        return (facts, exclusions, problems);
    }

    private string DescribeRevision(RevisionRow row)
    {
        var store = SafeStore();
        var lines = new List<string> { $"{row.ScopeText} · {row.StorageText} · {row.Completeness}", L.F("Clients.Version0Archived1From2", row.VersionText, row.Archived, row.Source) };
        try
        {
            if (store?.Registry.Version(row.Key) is { IsUsable: true } v)
            {
                var m = ClientManifest.Read(v.ManifestPath(store.Root));
                if (m.VersionEvidence is { Count: > 0 } clues) lines.Add(L.T("Clients.VersionEvidence2") + string.Join("; ", clues.Select(c => $"{c.Source}: {c.Value}{(c.Strong ? "" : " (weak)")}")));
                lines.Add(L.F("Clients.0N0Files1", m.Files.Count, ReportWriter.FormatSize(m.TotalBytes)) + (m.Directories is { Count: > 0 } d ? $", {L.Count(d.Count, "Noun.empty_folder")}" : "") +
                          L.F("Clients.0N0NotListedByThe", m.Files.Count(f => f.ListedBy == null && m.Format >= 2)));
                foreach (var e in m.Exclusions ?? []) lines.Add(L.F("Clients.LeftOut0123", e.Rule, L.Count(e.Files, "Noun.file"), ReportWriter.FormatSize(e.Bytes), e.Reason));
                foreach (string p in (m.Problems ?? []).Take(5)) lines.Add(L.T("Clients.NotArchived") + p);
            }
        }
        catch (InvalidDataException ex)
        {
            lines.Add(L.T("Clients.TheManifestCannotBeRead") + ex.Message);
        }
        if (row.Problem != null) lines.Add(row.Problem);
        return string.Join(Environment.NewLine, lines);
    }

    // ------------------------------------------------------------------ use

    [RelayCommand]
    private void UseSource()
    {
        if (SelectedSource is not { } row) return;
        if (row.IsProblem && row.Kind == ClientRowKind.Folder)
        {
            Services.Dialogs.Show(row.Problem ?? L.T("Clients.TheFolderIsNotAvailable"), MessageKind.Warning, L.T("Clients.UseThisClient"));
            return;
        }
        Use(row.Kind == ClientRowKind.Installed ? null : row.Id, row.Label);
    }

    [RelayCommand]
    private void UseRevision()
    {
        if (SelectedRevision is not { } row) return;
        if (!row.IsUsable)
        {
            Services.Dialogs.Show(L.T("Clients.ThisRevisionCannotBeUsed") + (row.Problem ?? L.T("Clients.ItIsNotInTheArchive")) + L.T("Clients.RunVerifyStoreOrArchiveThe"), MessageKind.Warning, L.T("Clients.UseThisRevision"));
            return;
        }
        Use(row.Key, row.Label);
    }

    private void Use(string? id, string label)
    {
        if (Scope == Services.Settings.ActiveScope && Services.WhyNotSwitchClient() is { } why)
        {
            Services.Dialogs.Show(why, MessageKind.Info, L.T("Clients.UseThisClient"));
            return;
        }
        Services.Settings.SetActiveClient(Scope, id);
        Services.SaveSettings();
        if (Scope == Services.Settings.ActiveScope) Services.Data.SwitchClient();
        Refresh(id ?? ClientRegistration.InstalledFor(Scope), id);
        if (Services.Data.ClientChoiceProblem is { } problem && Scope == Services.Settings.ActiveScope) Services.Status.Show(problem, StatusKind.Warning);
        else Services.Status.Show(L.F("Clients.0ClientInUse1", (Scope == ClientScope.Live ? "Live" : "Test-server"), label), StatusKind.Success);
    }

    // ------------------------------------------------------------------ find, browse, load

    /// <summary>Looks where Tibia installations usually are, validates each by its files, and lets you pick one.</summary>
    [RelayCommand]
    private async Task FindInstallationAsync()
    {
        FindResult? result = null;
        var registered = SafeStore()?.Registry.Registrations.Select(r => r.ResolveSource(Library.ProgramDirectory)).OfType<string>().ToList() ?? [];
        var known = Library.KnownContent();
        string packages = Services.Settings.LocationsFor(Scope).Resolve(LocationIds.PackagesFolder).DisplayPath;
        var roots = FindRoots?.Invoke() ?? InstallationFinder.DefaultRoots(packages, registered);
        if (!await RunAsync(L.T("Clients.LookingForTibiaInstallations"), async (p, ct) => await Task.Run(() => result = InstallationFinder.Find(roots, known, ct), ct)))
            return;
        if (result!.Candidates.Count == 0)
        {
            Services.Dialogs.Show(result.Explanation, MessageKind.Info, L.T("Clients.FindInstallation2"));
            return;
        }
        var items = result.Candidates.Select(c => new ChoiceItem(c.Path, L.F("Clients.01FoundIn2", c.Inspection.ClassDisplay, c.Inspection.Version.Display, c.Source),
            ClientServers.Display(c.Inspection.SuggestedServer))).ToList();
        var choice = Services.Dialogs.ChooseFrom(L.T("Clients.FindInstallation2"), L.F("Clients.0TibiaClientFolderSFound", result.Candidates.Count),
            items, new ChoiceOptions { OkText = L.T("Clients.UseThisClient"), ExtraButtons = [("register", L.T("Clients.AddWithoutUsingIt"))] });
        if (choice == null) return;
        int index = choice.Index;
        if (index < 0 || index >= result.Candidates.Count) return;
        var found = result.Candidates[index];
        await AddSourceAsync(found.Path, found.Inspection, use: choice.Button == null);
    }

    /// <summary>Choose any folder: it is inspected (only read), and can be used where it is, without a conventional installation.</summary>
    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        string? picked = Services.Dialogs.PickFolder(L.T("Clients.ATibiaClientFolderAnInstallation"));
        if (picked == null) return;
        var known = Library.KnownContent();
        FolderInspection? inspection = null;
        if (!await RunAsync(L.T("Clients.LookingAtTheFolder"), async (p, ct) => await Task.Run(() => inspection = FolderInspector.Inspect(picked, known), ct))) return;
        if (inspection!.Root == null || (inspection.Candidates.Count > 0 && !inspection.IsUsable))
        {
            if (inspection.Candidates.Count == 0)
            {
                Services.Dialogs.Show($"{inspection.ClassDisplay}: {inspection.Path}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, inspection.Notes)}", MessageKind.Warning, L.T("Clients.LoadAClientFolder"));
                return;
            }
            var choice = Services.Dialogs.ChooseFrom(L.T("Clients.LoadAClientFolder"), string.Join(" ", inspection.Notes),
                inspection.Candidates.Select(c => new ChoiceItem(c)).ToList(), new ChoiceOptions { OkText = L.T("Clients.LookAtThisFolder") });
            if (choice is not { Index: >= 0 } c2) return;
            string inner = inspection.Candidates[c2.Index];
            if (!await RunAsync(L.T("Clients.LookingAtTheFolder"), async (p, ct) => await Task.Run(() => inspection = FolderInspector.Inspect(inner, known), ct))) return;
        }
        if (inspection!.Class == FolderClass.AssetsOnly && inspection.Candidates.Count == 1)
        {
            // The assets folder of a client folder: the whole folder also brings its sounds, resource file and version.
            int which = Services.Dialogs.Choose(L.F("Clients.0IsTheAssetsFolderOf", inspection.Path, inspection.Candidates[0]),
                L.T("Clients.LoadAClientFolder"), L.T("Clients.TheWholeClientFolder"), L.T("Clients.OnlyTheAssetsFolder"));
            if (which < 0) return;
            if (which == 0)
            {
                string whole = inspection.Candidates[0];
                if (!await RunAsync(L.T("Clients.LookingAtTheFolder"), async (p, ct) => await Task.Run(() => inspection = FolderInspector.Inspect(whole, known), ct))) return;
            }
        }
        if (!inspection!.IsUsable)
        {
            Services.Dialogs.Show($"{inspection.ClassDisplay}: {inspection.Path}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, inspection.Notes)}" +
                                  Environment.NewLine + Environment.NewLine + MissingText(inspection), MessageKind.Warning, L.T("Clients.LoadAClientFolder"));
            return;
        }
        int answer = Services.Dialogs.Choose(Summary(inspection) + Environment.NewLine + Environment.NewLine +
                                             L.T("Clients.UseItNowEveryPageReads"),
            L.T("Clients.LoadAClientFolder"), L.T("Clients.UseItNow"), L.T("Clients.AddOnly"));
        if (answer < 0) return;
        await AddSourceAsync(inspection.Root!, inspection, use: answer == 0);
    }

    /// <summary>The inspection in plain words: kind, version and evidence, what works and what is missing.</summary>
    public static string Summary(FolderInspection inspection)
    {
        var lines = new List<string>
        {
            inspection.Root ?? inspection.Path,
            L.F("Clients.0Version1", inspection.ClassDisplay, inspection.Version.Display),
            L.T("Clients.VersionEvidence2") + inspection.Version.Details,
        };
        lines.AddRange(inspection.Notes);
        lines.Add("");
        lines.Add(L.T("Clients.CanBeUsedFor") + string.Join(", ", inspection.Capabilities.Where(c => c.Available && c.Id is not (ClientCapabilities.ArchiveFolder or ClientCapabilities.ArchiveInstallation)).Select(c => c.Name)) + ".");
        string missing = MissingText(inspection);
        if (missing.Length > 0) lines.Add(missing);
        return string.Join(Environment.NewLine, lines);
    }

    private static string MissingText(FolderInspection inspection) => string.Join(Environment.NewLine, inspection.Capabilities.Where(c => !c.Available && c.Id != ClientCapabilities.ArchiveInstallation)
        .Select(c => L.F("Clients.NotAvailable0Needs1", c.Name, string.Join(", ", c.Missing))));

    /// <summary>Adds a folder (the installed client is only selected), and uses it when asked. Nothing is copied.</summary>
    private async Task AddSourceAsync(string folder, FolderInspection inspection, bool use)
    {
        foreach (var scope in new[] { ClientScope.Live, ClientScope.TestServer })
            if (string.Equals(PathGuard.Normalize(Library.InstalledPackageFolder(scope)), PathGuard.Normalize(folder), StringComparison.OrdinalIgnoreCase))
            {
                ServerTab = scope == ClientScope.TestServer ? 1 : 0;
                Refresh(ClientRegistration.InstalledFor(scope));
                if (use) Use(null, ClientRegistry.DefaultAutomatic(scope).Label);
                else Services.Status.Show(L.T("Clients.ThisIsTheInstalledClientIt"), StatusKind.Info);
                return;
            }
        bool suggestedTest = inspection.SuggestedServer == ClientServers.Test || Scope == ClientScope.TestServer;
        int server = Services.Dialogs.Choose(L.F("Clients.Is0ALiveServerOr", folder), L.T("Clients.LoadAClientFolder"),
            suggestedTest ? L.T("Common.TestServer") : L.T("Clients.LiveServer"), suggestedTest ? L.T("Clients.LiveServer") : L.T("Common.TestServer"));
        if (server < 0) return;
        bool test = (server == 0) == suggestedTest;
        string name = Path.GetFileName(folder);
        string? label = Services.Dialogs.Prompt(L.T("Clients.LoadAClientFolder"), L.T("Clients.ANameForThisClientOnly"),
            inspection.Version.Status == VersionReport.Verified ? $"{name} {inspection.Version.Version}" : name);
        if (label == null) return;
        string type = Path.GetFileName(Path.GetDirectoryName(folder) ?? "").Equals("packages", StringComparison.OrdinalIgnoreCase) && inspection.Class == FolderClass.CompleteInstallation
            ? SourceTypes.InstalledPackage : SourceTypes.StandaloneFolder;
        ClientRegistration registration;
        try
        {
            registration = Library.Store.RegisterFolder(folder, test ? ClientServers.Test : ClientServers.Live, string.IsNullOrWhiteSpace(label) ? null : label, type);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Services.Dialogs.Show(ex.Message, MessageKind.Warning, L.T("Clients.LoadAClientFolder"));
            return;
        }
        ServerTab = test ? 1 : 0;
        Refresh(registration.Id);
        if (use) Use(registration.Id, registration.Label);
        else Services.Status.Show(L.F("Clients.Added0NothingWasCopiedArchive", registration.Label), StatusKind.Success);
        await Task.CompletedTask;
    }

    private string? SourceFolderOf(SourceRow row) => row.Kind switch
    {
        ClientRowKind.Installed => Library.InstalledPackageFolder(Scope),
        ClientRowKind.Folder => Library.Store.Registry.Registration(row.Id)?.ResolveSource(Library.ProgramDirectory),
        _ => null,
    };

    private string RegistrationIdOf(SourceRow row) => row.Kind == ClientRowKind.Installed ? ClientRegistration.InstalledFor(Scope) : row.Id;

    // ------------------------------------------------------------------ archive

    [RelayCommand]
    private async Task ArchiveSourceAsync()
    {
        if (SelectedSource is not { } row) return;
        await ArchiveAsync(row);
    }

    [RelayCommand]
    private async Task ArchiveInstalledNowAsync()
    {
        if (Sources.FirstOrDefault(r => r.Kind == ClientRowKind.Installed) is { } installed) await ArchiveAsync(installed);
    }

    /// <summary>
    /// Scans the folder completely, says what will be archived (scope, storage, what is left out and
    /// why, the space it takes), and archives it on your OK. Content already archived is recognized.
    /// <paramref name="read"/>: a scan just made with the current personal-data setting (Check for changes), used instead of reading every file again.
    /// </summary>
    private async Task ArchiveAsync(SourceRow row, (FolderScan Scan, FolderInspection Inspection)? read = null)
    {
        string? folder = SourceFolderOf(row);
        if (folder == null || !Directory.Exists(folder))
        {
            Services.Dialogs.Show(L.F("Clients.TheClientFolderIsNotAvailable", folder), MessageKind.Warning, L.T("Common.Archive"));
            return;
        }
        _ = Library.Store;   // the installed client's registration exists in every archive: make sure the archive exists
        string registrationId = RegistrationIdOf(row);
        bool personal = Services.Settings.ArchivePersonalData;
        bool keepIncomplete = false;
        var known = Library.KnownContent();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            FolderScan? scan = null;
            FolderInspection? inspection = null;
            if (attempt == 0 && read is { } given)
                (scan, inspection) = given;
            else if (!await RunAsync(L.T("Clients.ReadingTheClientFolder"), async (p, ct) => await Task.Run(() =>
                {
                    inspection = FolderInspector.Inspect(folder, known);
                    scan = ClientInventory.Scan(folder, new InventoryOptions { IncludePersonalData = personal }, p, ct);
                }, ct), operation: "import"))
                return;
            string scope = inspection!.Class == FolderClass.CompleteInstallation ? ManifestScopes.CompleteInstallation : ManifestScopes.StandaloneFolder;
            string storage = Library.StorageMode;
            if (!scan!.IsConsistent && !keepIncomplete)
            {
                var answer = Services.Dialogs.ShowReport(new DialogReport
                {
                    Title = L.T("Common.Archive"), Kind = MessageKind.Warning, Heading = L.T("Clients.TheFolderCannotBeArchivedAs"),
                    Summary = L.T("Clients.IfTheLauncherIsUpdatingThe"),
                    Sections =
                    [
                        new ReportSection(L.F("Clients.WhatWasFound0", L.Count(scan.Problems.Count, "Noun.problem")))
                        {
                            Bullets = [.. scan.Problems.Take(12), .. scan.Problems.Count > 12 ? [$"… and {scan.Problems.Count - 12:N0} more."] : Array.Empty<string>()],
                            Tone = ReportTone.Warning,
                        },
                        new ReportSection(L.T("Clients.YourChoices"))
                        {
                            Bullets =
                            [
                                L.T("Clients.ReadItAgainReadsTheWhole"),
                                L.T("Clients.KeepAsIncompleteRevisionArchivesWhat"),
                                L.T("Clients.CancelNothingIsArchived"),
                            ],
                        },
                    ],
                    PrimaryText = L.T("Clients.ReadItAgain"), SecondaryText = L.T("Clients.KeepAsIncompleteRevision"), CancelText = L.T("Common.Cancel"),
                });
                if (answer == ReportAnswer.Cancel) return;
                if (answer == ReportAnswer.Primary) continue;
                keepIncomplete = true;
            }
            var store = Library.Store;
            // One look-up per file content in the archive: off the UI thread.
            var current = scan;
            var preview = await Task.Run(() => store.PreviewArchive(current, scope, storage, personal));
            var chosen = Services.Dialogs.ConfirmArchive(new ArchiveConfirmation
            {
                Client = row.Label,
                Server = ClientServers.Display(Server),
                Version = inspection.Version.Display,
                Folder = folder,
                Preview = preview,
                KeepIncomplete = keepIncomplete,
                DescribeChanges = p => DescribeChanges(registrationId, p.Scan, scope),
                // Not async: the dialog awaits it on the UI thread, and a test may wait for it directly.
                Recalculate = (include, p, ct) => Task.Run(() =>
                    store.PreviewArchive(ClientInventory.Scan(folder, new InventoryOptions { IncludePersonalData = include }, p, ct), scope, storage, include), ct),
            });
            if (chosen == null) return;
            personal = chosen.IncludesPersonalData;
            // Read again with the other personal-data option while the client changed: the consistency question comes first.
            if (!chosen.Scan.IsConsistent && !keepIncomplete) continue;
            scan = chosen.Scan;
            ImportResult? result = null;
            var request = new ArchiveRequest(scan, Server, scope, storage, row.Kind == ClientRowKind.Installed ? SourceTypes.InstalledPackage :
                Library.Store.Registry.Registration(registrationId)?.EffectiveSourceType ?? SourceTypes.StandaloneFolder, row.Label, registrationId, inspection.Version, keepIncomplete);
            bool ok = await RunAsync(L.T("Clients.ArchivingTheClient"), async (p, ct) => await Task.Run(() => result = Library.Store.Archive(request, p, ct), ct), operation: "import", output: Library.Root,
                onSuccess: () =>
                {
                    if (result!.AlreadyStored) Services.Status.Show(L.F("Clients.NothingNewThisContentIsAlready", result.Version.Label), StatusKind.Success);
                    else Services.Status.Show(L.F("Clients.Archived012N0Files", result.Version.Label, Completeness(result.Version).ToLowerInvariant(), result.Version.Files, ReportWriter.FormatSize(result.NewBytes)), StatusKind.Success);
                    foreach (string note in result.Notes) AppLog.Info("Archive: " + note);
                });
            if (ok || result != null) Refresh(row.Id, result?.Version.Key);
            if (result != null) Archived?.Invoke();
            return;
        }
        Services.Status.Show(L.T("Clients.TheFolderChangedEveryTimeIt"), StatusKind.Warning);
    }

    /// <summary>What changed against the newest revision archived from this folder ("3 files changed, …"), or null.</summary>
    private string? DescribeChanges(string registrationId, FolderScan scan, string scope)
    {
        var store = Library.Store;
        if (store.Registry.Registration(registrationId) is not { } r || store.Registry.NewestVersionOf(r) is not { IsUsable: true } newest) return null;
        try
        {
            var old = store.OpenVersion(newest.Key).Manifest;
            var now = ClientManifest.Create(new ManifestContent { Server = Server, Files = scan.Files, Directories = scan.Directories, Scope = scope, Exclusions = scan.Exclusions }, DateTime.UtcNow, null);
            var diff = ManifestDiff.CompareRevisions(old, now);
            return diff.Unchanged ? L.F("Clients.UnchangedAgainst0", newest.Label) + NotCompared(diff) : L.F("Clients.Against01", newest.Label, diff.Summary()) + NotCompared(diff);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static string NotCompared(ManifestDiff diff) => diff.NotCompared.Count == 0 ? "" : " " + string.Join(" ", diff.NotCompared);

    /// <summary>Reads the folder again and compares it with its newest revision; offers to archive it when it changed.</summary>
    [RelayCommand]
    private async Task RescanAsync()
    {
        if (SelectedSource is not { } row) return;
        string? folder = SourceFolderOf(row);
        if (folder == null || !Directory.Exists(folder))
        {
            Services.Dialogs.Show(L.F("Clients.TheClientFolderIsNotAvailable2", folder), MessageKind.Warning, L.T("Clients.CheckForChanges"));
            return;
        }
        FolderScan? scan = null;
        FolderInspection? inspection = null;
        var known = Library.KnownContent();
        bool personal = Services.Settings.ArchivePersonalData;
        if (!await RunAsync(L.T("Clients.ReadingTheClientFolder"), async (p, ct) => await Task.Run(() =>
            {
                inspection = FolderInspector.Inspect(folder, known);
                scan = ClientInventory.Scan(folder, new InventoryOptions { IncludePersonalData = personal }, p, ct);
            }, ct), operation: "import"))
            return;
        if (!scan!.IsConsistent)
        {
            Services.Dialogs.Show(L.T("Clients.TheFolderCouldNotBeRead") + Environment.NewLine + string.Join(Environment.NewLine, scan.Problems.Take(8)), MessageKind.Warning, L.T("Clients.CheckForChanges"));
            return;
        }
        var stored = Library.StoreExists ? Library.Store.FindStored(Server, scan.Fingerprint) : null;
        if (stored != null)
        {
            Services.Status.Show(L.F("Clients.UnchangedThisContentIsArchivedAs", stored.Label, scan.Files.Count, scan.Duration.TotalSeconds), StatusKind.Success);
            return;
        }
        string scope = inspection!.Class == FolderClass.CompleteInstallation ? ManifestScopes.CompleteInstallation : ManifestScopes.StandaloneFolder;
        string? changes = Library.StoreExists ? DescribeChanges(RegistrationIdOf(row), scan, scope) : null;
        string message = changes != null
            ? L.F("Clients.012ArchiveItAs", changes, Environment.NewLine, Environment.NewLine)
            : L.F("Clients.ThisFolder01N0Files", inspection.Version.Display, scan.Files.Count, ReportWriter.FormatSize(scan.TotalBytes));
        if (Services.Dialogs.Confirm(message, L.T("Clients.CheckForChanges"), L.T("Clients.Archive"), L.T("Clients.NotNow"))) await ArchiveAsync(row, (scan!, inspection));   // the folder was just read: not read again
    }

    // ------------------------------------------------------------------ compare and rebuild

    /// <summary>Compares the selected revision with another revision of this server (content by hash; scopes and exclusions are taken into account).</summary>
    [RelayCommand]
    private void Compare()
    {
        if (SelectedRevision is not { } row) return;
        var others = Revisions.Where(r => r.Key != row.Key && r.IsUsable).ToList();
        if (others.Count == 0)
        {
            Services.Dialogs.Show(L.T("Clients.ThereIsNoOtherRevisionOf"), MessageKind.Info, L.T("Common.Compare"));
            return;
        }
        var choice = Services.Dialogs.ChooseFrom(L.T("Common.Compare"), L.F("Clients.Compare0With", row.Label), others.Select(o => new ChoiceItem(o.Label, $"{o.VersionText} · {o.ScopeText} · {o.Archived}")).ToList(),
            new ChoiceOptions { OkText = L.T("Common.Compare") });
        if (choice is not { Index: >= 0 }) return;
        var other = others[choice.Index];
        try
        {
            var store = Library.Store;
            var older = new[] { row, other }.OrderBy(r => store.Registry.Version(r.Key)!.ImportedAtUtc).ToList();
            var a = store.OpenVersion(older[0].Key).Manifest;
            var b = store.OpenVersion(older[1].Key).Manifest;
            var diff = ManifestDiff.CompareRevisions(a, b);
            string text = L.F("Clients.From0To123", older[0].Label, older[1].Label, Environment.NewLine, (diff.Unchanged ? L.T("Clients.NoChangeInTheFilesCompared") : diff.Summary()), Environment.NewLine, diff.UnchangedCount, diff.Modified.Count, diff.Added.Count, diff.Removed.Count, diff.Moved.Count) +
                          (diff.NotCompared.Count > 0 ? Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, diff.NotCompared) : "");
            Services.Dialogs.Show(text, MessageKind.Info, L.T("Common.Compare"));
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException)
        {
            Services.Dialogs.Show(ex.Message, MessageKind.Warning, L.T("Common.Compare"));
        }
    }

    [RelayCommand]
    private void Rebuild()
    {
        if (SelectedRevision is not { } row) return;
        if (OpenRebuilder == null)
        {
            Services.Dialogs.Show(L.T("Clients.OpenClientLibraryClientInstallationRebuilder"), MessageKind.Info, L.T("Clients.Rebuild"));
            return;
        }
        OpenRebuilder(row.Key);
    }

    // ------------------------------------------------------------------ the reminder

    [RelayCommand]
    private void DismissReminder()
    {
        if (SafeInstalledStatus() is not { Fingerprint: { } fingerprint } status) return;
        Try(L.T("Common.DonTAskForThisVersion"), () => Library.DismissChange(Scope, fingerprint, status.Version));
        Refresh();
        Services.Status.Show(L.T("Clients.NotAskedAgainForThisContent"), StatusKind.Info);
    }

    [RelayCommand]
    private void DisableReminders()
    {
        Services.Settings.ClientImportReminders = false;
        Services.Settings.ClientChangeHandling = ClientChangeHandling.AskQuietly;
        Services.SaveSettings();
        Refresh();
        Services.Status.Show(L.T("Clients.NoNoticeAtStartAnyMore2"), StatusKind.Info);
    }

    // ------------------------------------------------------------------ rename, locate, remove

    [RelayCommand]
    private void RenameSource()
    {
        if (SelectedSource is not { Kind: ClientRowKind.Folder } row) return;
        string? label = Services.Dialogs.Prompt(L.T("Clients.Rename"), L.T("Clients.NewNameOnlyTheLabelChanges"), row.Label);
        if (string.IsNullOrWhiteSpace(label)) return;
        Try(L.T("Clients.Rename"), () => Library.Store.RenameRegistration(row.Id, label));
        Refresh(row.Id);
    }

    [RelayCommand]
    private void RenameRevision()
    {
        if (SelectedRevision is not { } row) return;
        string? label = Services.Dialogs.Prompt(L.T("Clients.Rename"), L.T("Clients.NewNameOnlyTheLabelChanges2"), row.Label);
        if (string.IsNullOrWhiteSpace(label)) return;
        Try(L.T("Clients.Rename"), () => Library.Store.RenameVersion(row.Key, label));
        Refresh(selectRevision: row.Key);
    }

    [RelayCommand]
    private async Task LocateAgainAsync()
    {
        if (SelectedSource is not { Kind: ClientRowKind.Folder } row) return;
        string? picked = Services.Dialogs.PickFolder(L.F("Clients.WhereIs0Now", row.Label));
        if (picked == null) return;
        var known = Library.KnownContent();
        FolderInspection? inspection = null;
        if (!await RunAsync(L.T("Clients.LookingAtTheFolder"), async (p, ct) => await Task.Run(() => inspection = FolderInspector.Inspect(picked, known), ct))) return;
        if (!inspection!.IsUsable)
        {
            Services.Dialogs.Show($"{inspection.ClassDisplay}: {string.Join(" ", inspection.Notes)}", MessageKind.Warning, L.T("Clients.LocateAgain2"));
            return;
        }
        if (!Try(L.T("Clients.LocateAgain2"), () => Library.Store.Relocate(row.Id, inspection.Root!))) return;
        if (Services.Settings.ActiveClient(Scope) == row.Id && Scope == Services.Settings.ActiveScope) Services.Data.SwitchClient();
        Refresh(row.Id);
        Services.Status.Show(L.F("Clients.0IsAt1Again2", row.Label, inspection.Root, inspection.Version.Display), StatusKind.Success);
    }

    private HashSet<string> ActiveKeys() =>
        new(new[] { Services.Settings.ActiveClientLive, Services.Settings.ActiveClientTest }.OfType<string>(), StringComparer.OrdinalIgnoreCase);

    [RelayCommand]
    private void RemoveSource()
    {
        if (SelectedSource is not { } row) return;
        if (row.Kind == ClientRowKind.Installed)
        {
            Services.Dialogs.Show(L.T("Clients.TheInstalledClientsAreFoundAutomatically"), MessageKind.Info, L.T("Common.Remove"));
            return;
        }
        int choice = Services.Dialogs.Choose(L.F("Clients.ForgetTheFolder0TheFolder", row.Label), L.T("Common.Remove"),
            L.T("Clients.ForgetTheFolder"), L.T("Common.Cancel"));
        if (choice != 0) return;
        Try(L.T("Common.Remove"), () => Library.Store.RemoveRegistration(row.Id, alsoVersions: false, ActiveKeys()));
        if (Services.Settings.ActiveClient(Scope) == row.Id)
        {
            Services.Settings.SetActiveClient(Scope, null);
            Services.SaveSettings();
            if (Scope == Services.Settings.ActiveScope) Services.Data.SwitchClient();
        }
        Refresh();
    }

    [RelayCommand]
    private void DeleteRevision()
    {
        if (SelectedRevision is not { } row) return;
        if (ActiveKeys().Contains(row.Key))
        {
            Services.Dialogs.Show(L.T("Clients.ThisRevisionIsInUseChoose"), MessageKind.Warning, L.T("Clients.DeleteRevision"));
            return;
        }
        var usage = SafeStore() is { } store ? SafeUsages(store).GetValueOrDefault(row.Key) : null;
        string effect = usage == null ? "" : usage.Storage == ManifestStorage.Independent
            ? L.F("Clients.ItsOwnCopies0AreDeleted", ReportWriter.FormatSize(usage.UniqueBytes))
            : L.F("Clients.FilesItSharesWithOtherRevisions", ReportWriter.FormatSize(usage.SharedBytes), ReportWriter.FormatSize(usage.UniqueBytes));
        int choice = Services.Dialogs.Choose(L.F("Clients.DeleteTheArchivedRevision0This", row.Label, effect), L.T("Clients.DeleteRevision"), L.T("Clients.DeleteAndFreeItsSpace"), L.T("Clients.DeleteOnly"));
        if (choice < 0) return;
        if (!Try(L.T("Clients.DeleteRevision"), () => Library.Store.RemoveVersion(row.Key, ActiveKeys()))) return;
        if (choice == 0) ReclaimQuietly();
        Refresh();
    }

    private void ReclaimQuietly()
    {
        try
        {
            var r = Library.Store.ReclaimSpace();
            Services.Status.Show(L.F("Clients.0Freed1NoRevisionUses", ReportWriter.FormatSize(r.BytesFreed), L.Count(r.ObjectsRemoved, "Noun.file")) +
                                 (r.Problems.Count > 0 ? L.T("Clients.SomeFilesAreInUseAnd") : "") + (r.ObjectsKept > 0 ? L.Count(r.ObjectsKept, "Clients.RecentFilesKept") : ""), StatusKind.Success);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Services.Dialogs.Show(ex.Message, MessageKind.Warning, L.T("Clients.ReclaimSpace2"));
        }
    }

    // ------------------------------------------------------------------ the archive

    [RelayCommand]
    private async Task VerifyStoreAsync()
    {
        if (!Library.StoreExists) { Services.Status.Show(L.T("Clients.NothingIsArchivedYet"), StatusKind.Info); return; }
        VerifyResult? result = null;
        await RunAsync(L.T("Clients.VerifyingTheClientArchive"), async (p, ct) => await Task.Run(() => result = Library.Store.Verify(null, p, ct), ct), operation: "verify",
            onSuccess: () =>
            {
                if (result!.Problems.Count == 0) Services.Status.Show(L.F("Clients.Verified01N0FilesEvery", L.Count(result.Versions, "Noun.revision"), result.Objects), StatusKind.Success);
                else Services.Dialogs.Show(L.T("Clients.ProblemsWereFound") + Environment.NewLine + string.Join(Environment.NewLine, result.Problems.Take(10)) +
                                           Environment.NewLine + Environment.NewLine + L.T("Clients.ArchiveTheClientAgainToRepair"), MessageKind.Warning, L.T("Clients.VerifyStore"));
            });
        OfferToRemoveLeftoverRebuilds();
        Refresh();
    }

    /// <summary>Unfinished folders of rebuilds the program could not complete (it stopped): offered for removal, never removed silently.</summary>
    private void OfferToRemoveLeftoverRebuilds()
    {
        IReadOnlyList<LeftoverRebuild> leftovers;
        try { leftovers = Library.Store.LeftoverRebuilds(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Warn("Client archive: " + ex.Message); return; }
        if (leftovers.Count == 0) return;
        var question = new DialogReport
        {
            Title = L.T("Clients.VerifyStore"), Kind = MessageKind.Question,
            Heading = L.F("Clients.Remove0", L.Count(leftovers.Count, "Noun.unfinished_rebuild_folder")),
            Summary = L.T("Clients.TheseRebuildsStoppedBeforeTheyFinished"),
            Sections =
            [
                new ReportSection(L.T("Clients.UnfinishedFolders"))
                {
                    Table = new DialogTable([L.T("Clients.UnfinishedFolder"), L.T("Clients.For"), L.T("ContentStudio.Started")],
                        leftovers.Take(20).Select(l => (IReadOnlyList<string>)[l.StagingFolder, l.Target, l.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")]).ToList()),
                    Note = leftovers.Count > 20 ? L.F("Clients.And0N0More", leftovers.Count - 20) : null,
                },
            ],
            PrimaryText = L.T("Common.Remove"), CancelText = L.T("Clients.Keep"),
        };
        if (Services.Dialogs.ShowReport(question) != ReportAnswer.Primary) return;
        foreach (var leftover in leftovers) Try(L.T("Clients.RemovingAnUnfinishedRebuild"), () => Library.Store.RemoveLeftoverRebuild(leftover));
        Services.Status.Show(L.F("Clients.Removed0", L.Count(leftovers.Count, "Noun.unfinished_rebuild_folder")), StatusKind.Success);
    }

    [RelayCommand]
    private void ReclaimSpace()
    {
        if (!Library.StoreExists) return;
        var usage = Library.Store.Usage();
        if (usage.ReclaimableBytes == 0)
        {
            Services.Status.Show(L.T("Clients.EveryArchivedFileIsUsedBy"), StatusKind.Info);
            return;
        }
        if (!Services.Dialogs.Confirm(L.F("Clients.Free0OfFilesNoArchived", ReportWriter.FormatSize(usage.ReclaimableBytes)),
                L.T("Clients.ReclaimSpace2"), L.T("Clients.FreeTheSpace"), L.T("Common.Cancel"))) return;
        ReclaimQuietly();
        Refresh();
    }

    [RelayCommand]
    private void OpenStoreFolder()
    {
        if (Library.Root is { } root && Directory.Exists(root)) Services.Shell.OpenFolder(root);
        else Services.Status.Show(L.T("Clients.TheClientArchiveFolderDoesNot"), StatusKind.Info);
    }

    /// <summary>Folder edition: copies the archive to another folder, verifies it there, switches to it, and removes the old copy.</summary>
    [RelayCommand]
    private async Task MoveStoreAsync()
    {
        if (!CanMoveStore || Library.Root is not { } oldRoot) return;
        string? picked = Services.Dialogs.PickFolder(L.T("Clients.NewClientArchiveFolderEmptyOr"));
        if (picked == null) return;
        string target = Path.GetFullPath(picked);
        if (PathGuard.Overlaps(target, oldRoot) || Core.Editing.AssetsProject.IsInsideClientInstallation(target))
        {
            Services.Dialogs.Show(L.T("Clients.ChooseAFolderOutsideTheCurrent"), MessageKind.Warning, L.T("Clients.MoveStore2"));
            return;
        }
        bool exists = Library.StoreExists;
        if (!Services.Dialogs.Confirm(L.F("Clients.MoveTheClientArchiveTo0", target, Environment.NewLine, Environment.NewLine) +
                                      (exists ? L.T("Clients.EverythingIsCopiedAndVerifiedThere") : L.T("Clients.NothingIsArchivedYetOnlyThe")),
                L.T("Clients.MoveStore2"), L.T("Clients.Move"), L.T("Common.Cancel"))) return;
        if (exists && !await RunAsync(L.T("Clients.MovingTheClientArchive"), async (p, ct) => await Task.Run(() => ClientStore.CopyStore(oldRoot, target, p, ct), ct), operation: "move-store", output: target))
            return;
        Services.Settings.ClientStoreFolder = target;
        Services.SaveSettings();
        Library.Configure(target, Library.ProgramDirectory);
        Services.Data.SwitchClient();
        string note = "";
        if (exists)
        {
            try
            {
                ClientStore.DeleteStore(oldRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                note = L.F("Clients.TheOldFolder0CouldNot", oldRoot, ex.Message);
            }
        }
        OnPropertyChanged(nameof(CanMoveStore));
        Refresh();
        Services.Status.Show(L.F("Clients.TheClientArchiveIsNowIn", target, note), StatusKind.Success);
    }
}
