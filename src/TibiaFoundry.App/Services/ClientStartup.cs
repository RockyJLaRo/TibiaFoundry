using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

/// <summary>A client that can be used at start: an installed client, a folder you added, or an archived revision.</summary>
/// <param name="ActiveId">What <see cref="AppSettings.SetActiveClient"/> stores: null for the installed client, a registration ID, or a revision key.</param>
public sealed record StartupSource(ClientScope Scope, string? ActiveId, string Group, string Title, string Detail);

/// <summary>
/// Which client is used when the program starts (8.0), before the first page loads: first-run setup
/// for a new installation, the selection dialog when "At startup: ask which client" is chosen, and
/// the recovery dialog when the remembered client cannot be used. Nothing here deletes or moves
/// anything; a choice that cannot be used is kept until you replace it.
/// </summary>
public sealed class ClientStartup(AppServices services)
{
    public static string LiveGroup => L.T("ClientStartup.LiveInstallation");
    public static string TestGroup => L.T("ClientStartup.TestInstallation");
    public static string FolderGroup => L.T("ClientStartup.StandaloneFolders");
    public static string RevisionGroup => L.T("ClientStartup.ArchivedRevisions");
    public static string RebuiltGroup => L.T("ClientStartup.ReconstructedInstallations");

    /// <summary>Where setup looks for installations; null = this PC's usual places (tests give their own folders).</summary>
    public Func<IReadOnlyList<InstallationRoot>>? FindRoots { get; set; }

    private AppSettings S => services.Settings;
    private IDialogService Dialogs => services.Dialogs;
    private ClientLibrary Library => services.Clients;

    /// <summary>Runs what the start needs and says what happened (for the log).</summary>
    public async Task<string> RunAsync()
    {
        if (!Library.IsConfigured) return L.T("ClientStartup.NoClientArchiveHere");
        if (!S.ClientSetupCompleted) return await SetupAsync();
        if (S.StartupClientMode == StartupClientMode.Ask) return AskWhichClient();
        return CheckRemembered();
    }

    // ------------------------------------------------------------------ the sources

    /// <summary>Every client that can be used now, grouped as the selection dialog shows them.</summary>
    public IReadOnlyList<StartupSource> UsableSources()
    {
        var list = new List<StartupSource>();
        var known = Library.KnownContent();
        foreach (var scope in new[] { ClientScope.Live, ClientScope.TestServer })
        {
            string folder = Library.InstalledPackageFolder(scope);
            if (!Directory.Exists(folder)) continue;
            var inspection = FolderInspector.Inspect(folder, known);
            if (!inspection.IsUsable) continue;
            list.Add(new StartupSource(scope, null, scope == ClientScope.Live ? LiveGroup : TestGroup, DefaultClients.LabelOf(scope), Describe(inspection, folder)));
        }
        ClientStore? store;
        try { store = Library.ExistingStore; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Warn("Client archive: " + ex.Message); store = null; }
        if (store == null) return list;
        foreach (var r in store.Registry.Registrations.Where(r => !r.IsAutomatic))
        {
            string? folder = r.ResolveSource(Library.ProgramDirectory);
            if (folder == null || !Directory.Exists(folder)) continue;
            var inspection = FolderInspector.Inspect(folder, known);
            if (!inspection.IsUsable) continue;
            list.Add(new StartupSource(ClientServers.ScopeOf(r.Server), r.Id, r.EffectiveSourceType == SourceTypes.Reconstructed ? RebuiltGroup : FolderGroup,
                $"{r.Label} ({ClientServers.Display(r.Server)})", Describe(inspection, folder)));
        }
        foreach (var v in store.Registry.Versions.Where(v => v.IsUsable).OrderByDescending(v => v.ImportedAtUtc))
            list.Add(new StartupSource(ClientServers.ScopeOf(v.Server), v.Key, RevisionGroup, $"{v.Label} ({ClientServers.Display(v.Server)})",
                L.F("ClientStartup.0Version1Archived2Yyyy", ManifestScopes.Display(v.EffectiveScope), v.ClientVersion ?? "unknown", v.ImportedAtUtc.ToLocalTime())));
        return list;
    }

    private static string Describe(FolderInspection inspection, string folder)
    {
        int tools = inspection.Capabilities.Count(c => c.Available && c.Id is not (ClientCapabilities.ArchiveFolder or ClientCapabilities.ArchiveInstallation));
        return L.F("ClientStartup.012CanUseIt", inspection.ClassDisplay, inspection.Version.Display, L.Count(tools, "Noun.tool"), folder);
    }

    private bool IsCurrent(StartupSource s) => s.Scope == S.ActiveScope && s.ActiveId == S.ActiveClient(s.Scope);

    /// <summary>Makes a source the client in use (and its server the active one).</summary>
    public void Use(StartupSource source)
    {
        S.UseTestServer = source.Scope == ClientScope.TestServer;
        S.SetActiveClient(source.Scope, source.ActiveId);
        services.SaveSettings();
        services.Data.SwitchClient();
    }

    // ------------------------------------------------------------------ "ask which client"

    /// <summary>The selection dialog (At startup: ask which client). "Remember my choice" switches to using the remembered client.</summary>
    public string AskWhichClient()
    {
        var sources = UsableSources();
        if (sources.Count == 0) return L.T("ClientStartup.NoUsableClient");
        if (sources.Count == 1)
        {
            if (!IsCurrent(sources[0])) Use(sources[0]);
            return L.T("ClientStartup.TheOnlyUsableClientWasSelected") + sources[0].Title;
        }
        int current = sources.ToList().FindIndex(IsCurrent);
        var answer = Dialogs.ChooseFrom(L.T("ClientStartup.WhichTibiaClient"), L.T("ClientStartup.ChooseTheClientTibiaFoundryUses"),
            sources.Select(s => new ChoiceItem(s.Title, s.Detail, s.Group)).ToList(),
            new ChoiceOptions
            {
                OkText = L.T("ClientStartup.UseThisClient"), CancelText = L.T("ClientStartup.UseTheRememberedOne"), Selected = current >= 0 ? current : 0,
                CheckBoxText = L.T("ClientStartup.RememberMyChoiceDonTAsk"), ExtraButtons = [("browse", L.T("ClientStartup.BrowseForAClientFolder"))],
            });
        if (answer == null) return L.T("ClientStartup.KeptTheRememberedClient");
        if (answer.Checked)
        {
            S.StartupClientMode = StartupClientMode.Remembered;
            services.SaveSettings();
        }
        if (answer.Button == "browse") return BrowseAndUse() ?? L.T("ClientStartup.KeptTheRememberedClient");
        if (answer.Index < 0 || answer.Index >= sources.Count) return L.T("ClientStartup.KeptTheRememberedClient");
        if (!IsCurrent(sources[answer.Index])) Use(sources[answer.Index]);
        return "chose " + sources[answer.Index].Title;
    }

    /// <summary>Browse for a folder, check what it is, add it and use it. Returns what happened, or null when nothing was chosen.</summary>
    public string? BrowseAndUse()
    {
        string? picked = Dialogs.PickFolder(L.T("ClientStartup.ATibiaClientFolderAnInstallation"));
        if (picked == null) return null;
        var inspection = FolderInspector.Inspect(picked, Library.KnownContent());
        if (!inspection.IsUsable)
        {
            Dialogs.ShowReport(NotAClientReport(L.T("ClientStartup.BrowseForAClientFolder2"), picked, inspection));
            return null;
        }
        var source = AddFolder(inspection, picked);
        if (source == null) return null;
        Use(source);
        return L.T("ClientStartup.AddedAndChose") + source.Title;
    }

    /// <summary>The source for a folder: the installed client itself when it is one, else a folder added to the Clients page.</summary>
    private StartupSource? AddFolder(FolderInspection inspection, string picked)
    {
        string folder = inspection.Root ?? picked;
        foreach (var scope in new[] { ClientScope.Live, ClientScope.TestServer })
            if (string.Equals(PathGuard.Normalize(Library.InstalledPackageFolder(scope)), PathGuard.Normalize(folder), StringComparison.OrdinalIgnoreCase))
                return new StartupSource(scope, null, scope == ClientScope.Live ? LiveGroup : TestGroup, DefaultClients.LabelOf(scope), folder);
        try
        {
            var r = Library.Store.RegisterFolder(folder, inspection.SuggestedServer, null, SourceTypes.StandaloneFolder);
            return new StartupSource(ClientServers.ScopeOf(r.Server), r.Id, FolderGroup, r.Label, folder);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Dialogs.Show(ex.Message, MessageKind.Warning, L.T("ClientStartup.BrowseForAClientFolder2"));
            return null;
        }
    }

    // ------------------------------------------------------------------ the remembered client

    /// <summary>
    /// The remembered client is used as it is when it can be used. When it cannot (its folder moved,
    /// the revision is damaged, the archive is missing), the recovery dialog offers to locate it,
    /// to browse for another, to choose another client, or to use the archived revision of the same
    /// version. Cancelling keeps the choice (the installed client is used meanwhile).
    /// </summary>
    public string CheckRemembered()
    {
        var scope = S.ActiveScope;
        string? id = S.ActiveClient(scope);
        if (id == null) return L.T("ClientStartup.TheInstalledClientRemembered");
        _ = Library.LocationsFor(scope, out string? problem);
        if (problem == null) return L.T("ClientStartup.TheRememberedClient");

        ClientStore? store = null;
        try { store = Library.ExistingStore; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Warn("Client archive: " + ex.Message); }
        var registration = id.Contains('/') ? null : store?.Registry.Registration(id);
        var sources = UsableSources().Where(s => !IsCurrent(s)).ToList();
        // The archived revision of the same version: the newest of the folder's revisions, or another revision with the damaged one's version.
        StartupSource? sameVersion = null;
        if (store != null)
        {
            ClientVersionEntry? match = registration != null
                ? store.Registry.VersionsOf(registration).Where(v => v.IsUsable).OrderByDescending(v => v.ImportedAtUtc).FirstOrDefault()
                : store.Registry.Version(id) is { } damaged
                    ? store.Registry.Versions.Where(v => v.IsUsable && v.Key != id && v.Server == damaged.Server && (v.Fingerprint == damaged.Fingerprint || v.ClientVersion == damaged.ClientVersion))
                        .OrderByDescending(v => v.Fingerprint == damaged.Fingerprint).FirstOrDefault()
                    : null;
            if (match != null) sameVersion = sources.FirstOrDefault(s => s.ActiveId == match.Key);
        }
        var items = new List<ChoiceItem>();
        var targets = new List<StartupSource>();
        if (sameVersion != null)
        {
            items.Add(new ChoiceItem(L.T("ClientStartup.UseTheArchivedRevisionOfThe") + sameVersion.Title, sameVersion.Detail, L.T("ClientStartup.SameVersion")));
            targets.Add(sameVersion);
        }
        foreach (var s in sources.Where(s => s != sameVersion))
        {
            items.Add(new ChoiceItem(s.Title, s.Detail, s.Group));
            targets.Add(s);
        }
        var buttons = new List<(string, string)>();
        if (registration != null) buttons.Add(("locate", L.T("ClientStartup.LocateTheFolder")));
        buttons.Add(("browse", L.T("ClientStartup.BrowseForAnother")));
        var answer = Dialogs.ChooseFrom(L.T("ClientStartup.TheRememberedClientCannotBeUsed"), problem + Environment.NewLine + Environment.NewLine +
                                                                                  L.T("ClientStartup.ChooseWhatToUseInsteadNothing"),
            items, new ChoiceOptions { OkText = L.T("ClientStartup.UseThisClient"), CancelText = L.T("ClientStartup.KeepMyChoice"), Selected = items.Count > 0 ? 0 : null, ExtraButtons = buttons });
        if (answer == null) return L.T("ClientStartup.KeptTheRememberedClientNotUsable") + problem;
        if (answer.Button == "locate" && registration != null)
        {
            string? folder = Dialogs.PickFolder(L.F("ClientStartup.WhereIs0Now", registration.Label));
            if (folder == null) return L.T("ClientStartup.KeptTheRememberedClientNotUsable2");
            try
            {
                Library.Store.Relocate(registration.Id, folder);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                Dialogs.Show(ex.Message, MessageKind.Warning, L.T("ClientStartup.LocateTheFolder2"));
                return L.T("ClientStartup.KeptTheRememberedClientNotUsable2");
            }
            services.Data.SwitchClient();
            return L.T("ClientStartup.LocatedTheRememberedFolderAgain");
        }
        if (answer.Button == "browse") return BrowseAndUse() ?? L.T("ClientStartup.KeptTheRememberedClientNotUsable2");
        if (answer.Index < 0 || answer.Index >= targets.Count) return L.T("ClientStartup.KeptTheRememberedClientNotUsable2");
        Use(targets[answer.Index]);
        return "chose " + targets[answer.Index].Title;
    }

    // ------------------------------------------------------------------ first-run setup

    /// <summary>The Default Live Client and Default Test Client (8.6): where each is expected on this PC, and what is there.</summary>
    public IReadOnlyList<DefaultClientStatus> DefaultClientStatuses(IReadOnlyDictionary<string, string>? known = null) =>
        DefaultClients.Inspect(Library.InstalledPackageFolder, known ?? Library.KnownContent());

    /// <summary>A client setup can offer: an entry of the list, and the folder it stands for (no inspection: shown, but it cannot be chosen).</summary>
    private sealed record SetupOption(ChoiceItem Item, string Folder, FolderInspection? Inspection);

    /// <summary>The list of step 1: both default clients first, whatever their state, then every other client found.</summary>
    private static List<SetupOption> SetupOptions(IReadOnlyList<DefaultClientStatus> defaults, FindResult found)
    {
        string DefaultGroup = L.T("ClientStartup.DefaultClientsGroup");
        string OtherGroup = L.T("ClientStartup.OtherClientsGroup");
        var options = new List<SetupOption>();
        foreach (var d in defaults)
        {
            string title = d.IsUsable ? $"{d.Label} — {d.Version ?? "version unknown"}" : $"{d.Label} — {d.StateDisplay.ToLowerInvariant()}";
            options.Add(new SetupOption(new ChoiceItem(title, $"{d.Server} · {d.Explanation}{Environment.NewLine}{d.Folder}", DefaultGroup, d.IsUsable), d.Folder, d.IsUsable ? d.Inspection : null));
        }
        var taken = new HashSet<string>(defaults.Select(d => d.Folder), StringComparer.OrdinalIgnoreCase);
        foreach (var c in found.Candidates.Where(c => !taken.Contains(PathGuard.Normalize(c.Path))))
            options.Add(new SetupOption(new ChoiceItem(c.Path, L.F("ClientStartup.01FoundIn2", c.Inspection.ClassDisplay, c.Inspection.Version.Display, c.Source), OtherGroup), c.Path, c.Inspection));
        return options;
    }

    /// <summary>
    /// First-run setup (a new installation): find or choose the client, see what it is, choose the
    /// storage for archives and what happens when the installed client changes, archive it now or
    /// not, and a summary. Every answer can be changed later; skipping is always possible.
    /// </summary>
    public async Task<string> SetupAsync()
    {
        string Title = L.T("ClientStartup.SetUpTibiaFoundry");
        try
        {
            var known = Library.KnownContent();
            var roots = FindRoots?.Invoke() ?? InstallationFinder.DefaultRoots(S.LocationsFor(ClientScope.Live).Resolve(LocationIds.PackagesFolder).DisplayPath, []);
            var (defaults, found) = await Task.Run(() => (DefaultClientStatuses(known), InstallationFinder.Find(roots, known)));

            // 1. Find or choose: the default clients first (a missing or unusable one is listed with the reason, but cannot be chosen).
            var options = SetupOptions(defaults, found);
            FolderInspection? inspection = null;
            string? folder = null;
            while (inspection == null)
            {
                int usable = options.FindIndex(o => o.Inspection != null);
                // When setup runs again: the installed client in use; else the first usable one.
                int current = S.ActiveClient(S.ActiveScope) == null
                    ? options.FindIndex(o => o.Inspection != null && string.Equals(o.Folder, PathGuard.Normalize(Library.InstalledPackageFolder(S.ActiveScope)), StringComparison.OrdinalIgnoreCase))
                    : -1;
                string message = usable >= 0
                    ? L.T("ClientStartup.ChooseTheClientTibiaFoundryUses2")
                    : L.T("ClientStartup.NoUsableTibiaClientWasFound") +
                      Environment.NewLine + Environment.NewLine + found.Explanation;
                var answer = Dialogs.ChooseFrom(L.F("ClientStartup.01Of4YourTibia", Title),
                    message + Environment.NewLine + Environment.NewLine + L.T("ClientStartup.TibiaFoundryOnlyReadsTheClient"),
                    options.Select(o => o.Item).ToList(),
                    new ChoiceOptions { OkText = L.T("ClientStartup.Next"), CancelText = L.T("ClientStartup.SkipSetup"), Selected = current >= 0 ? current : usable >= 0 ? usable : null, ExtraButtons = [("browse", L.T("ClientStartup.BrowseForAFolder"))] });
                if (answer == null) return Finish("skipped");
                if (answer.Button == "browse")
                {
                    string? picked = Dialogs.PickFolder(L.T("ClientStartup.ATibiaClientFolderAnInstallation"));
                    if (picked == null) continue;
                    var look = FolderInspector.Inspect(picked, known);
                    if (!look.IsUsable)
                    {
                        Dialogs.ShowReport(NotAClientReport(Title, picked, look));
                        continue;
                    }
                    (inspection, folder) = (look, look.Root ?? picked);
                }
                else if (answer.Index >= 0 && answer.Index < options.Count && options[answer.Index].Inspection is { } chosen)
                    (inspection, folder) = (chosen, options[answer.Index].Folder);
            }

            // 2 and 3. What it is, and how archives are kept (with this client's own numbers).
            var inventory = await Task.Run(() => ClientInventory.Take(folder!, new InventoryOptions { IncludePersonalData = S.ArchivePersonalData }));
            string size = ReportWriter.FormatSize(inventory.TotalBytes);
            bool complete = inspection.Class == FolderClass.CompleteInstallation;
            var source = Choose(inspection, folder!);
            if (source == null) return Finish("skipped");
            var missing = inspection.Capabilities.Where(c => !c.Available && c.Missing.Count > 0).Select(c => L.F("ClientStartup.0Needs1", c.Name, string.Join(", ", c.Missing.Take(2)))).ToList();
            var tools = inspection.Available.Where(c => c.Id is not (ClientCapabilities.ArchiveFolder or ClientCapabilities.ArchiveInstallation)).Select(c => c.Name).ToList();
            // The client's facts as a summary card (8.8): labeled values and tool badges, apart from the question.
            var facts = new List<ReportRow>
            {
                new(L.T("ClientStartup.Client"), $"{source.Title} ({ClientServers.Display(ClientServers.Of(source.Scope))})"),
                ReportRow.Path(L.T("Common.Folder"), folder!),
                new(L.T("ClientStartup.WhatItIs"), $"{inspection.ClassDisplay} · {inspection.Version.Display}"),
                new(L.T("Common.Size"), $"{L.Count(inventory.Files.Count, "Noun.file")}, {size}" + (complete ? "" : L.T("ClientStartup.ArchivingKeepsThisFolderAsIt"))),
            };
            if (missing.Count > 0) facts.Add(new ReportRow(L.T("ClientStartup.NotAvailable"), string.Join("; ", missing), ReportTone.Warning));
            var storage = Dialogs.ChooseFrom(L.F("ClientStartup.02Of4KeepingClient", Title),
                L.T("ClientStartup.WhenYouArchiveAClientHow"),
                [
                    new ChoiceItem(L.T("ClientStartup.SpaceEfficientRecommended"), L.F("ClientStartup.TheFirstArchiveTakesAbout0", size)),
                    new ChoiceItem(L.T("ClientStartup.IndependentCopies"), L.F("ClientStartup.EveryArchivedVersionIsAFull", size)),
                ], new ChoiceOptions
                {
                    OkText = L.T("ClientStartup.Next"), CancelText = L.T("ClientStartup.SkipTheRest"), Selected = S.ClientStorageMode == ManifestStorage.Independent ? 1 : 0,
                    FactsHeading = L.T("ClientStartup.YourClient"), Facts = facts, Tags = tools, TagsLabel = tools.Count > 0 ? L.T("ClientStartup.ToolsThatCanUseIt") : null,
                });
            if (storage == null)
            {
                UseWithConsent(source, Title);
                return Finish(L.T("ClientStartup.SetUpTheClientOnly"));
            }
            S.ClientStorageMode = storage.Index == 1 ? ManifestStorage.Independent : ManifestStorage.Shared;

            // 4. When the installed client changes (only the installed packages are checked at start).
            bool installed = source.ActiveId == null;
            if (installed)
            {
                var handling = Dialogs.ChooseFrom(L.F("ClientStartup.03Of4WhenThe", Title),
                    L.T("ClientStartup.AtEveryStartTibiaFoundryChecks"),
                    [
                        new ChoiceItem(L.T("ClientStartup.AskMeRecommended"), L.T("ClientStartup.ANoticeOffersToArchiveIt")),
                        new ChoiceItem(L.T("ClientStartup.ArchiveItAutomatically"), L.T("ClientStartup.ItIsArchivedInTheBackground")),
                    ], new ChoiceOptions { OkText = L.T("ClientStartup.Next"), CancelText = L.T("ClientStartup.SkipTheRest"), Selected = S.ClientChangeHandling == ClientChangeHandling.Automatic ? 1 : 0 });
                if (handling == null)
                {
                    UseWithConsent(source, Title);
                    return Finish(L.T("ClientStartup.SetUpTheClientOnly"));
                }
                S.ClientChangeHandling = handling.Index == 1 ? ClientChangeHandling.Automatic : ClientChangeHandling.Ask;
                S.ClientImportReminders = true;
            }

            // 5. Archive now?
            bool archive = Dialogs.ShowReport(ArchiveQuestion(Title, source, folder!, inspection, inventory, installed)) == ReportAnswer.Primary;
            bool used = UseWithConsent(source, Title);
            var outcome = archive ? await ArchiveAsync(source, folder!, inspection) : SetupArchive.NotNow;

            // 6. Summary.
            Finish("completed");
            Dialogs.ShowReport(Summary(Title, source, folder!, inspection, inventory, installed, used, outcome));
            return "completed: " + source.Title;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            AppLog.Warn("Setup: " + ex.Message);
            Dialogs.ShowReport(new DialogReport
            {
                Title = Title, Kind = MessageKind.Warning, Heading = L.T("ClientStartup.SetupCouldNotFinish"),
                Summary = ex.Message,
                Sections =
                [
                    new ReportSection(L.T("ClientStartup.WhatYouCanDo"))
                    {
                        Bullets = [L.T("ClientStartup.SettingsTibiaClientRunSetupAgain"), L.T("ClientStartup.TheClientsPageCanChooseA")],
                        Note = L.T("ClientStartup.WhatWasChosenBeforeTheProblem"),
                    },
                ],
            });
            return Finish("stopped: " + ex.Message);
        }
    }

    private StartupSource? Choose(FolderInspection inspection, string folder) => AddFolder(inspection, folder);

    /// <summary>
    /// Makes the chosen client the one in use — unless another client (a folder or a revision) was
    /// chosen before and you keep it: setup that runs again never replaces a choice without asking.
    /// </summary>
    private bool UseWithConsent(StartupSource source, string title)
    {
        string? existing = S.ActiveClient(source.Scope);
        if (existing != null && existing != source.ActiveId)
        {
            string current = Library.Describe(source.Scope, null);
            if (!Dialogs.Confirm(L.F("ClientStartup.YouChoseAnother0ClientBefore", (source.Scope == ClientScope.Live ? "live" : "test-server"), current, Environment.NewLine, Environment.NewLine, source.Title),
                    title, L.T("ClientStartup.Use") + source.Title, L.T("ClientStartup.KeepMyChoice")))
                return false;
        }
        Use(source);
        return true;
    }

    private string Finish(string what)
    {
        S.ClientSetupCompleted = true;
        services.SaveSettings();
        return what;
    }

    /// <summary>What setup's archive step did.</summary>
    private sealed record SetupArchive(bool Archived, string? Label, int Files, string? Error)
    {
        public static readonly SetupArchive NotNow = new(false, null, 0, null);
    }

    private static DialogReport NotAClientReport(string title, string picked, FolderInspection look) => new()
    {
        Title = title, Kind = MessageKind.Warning, Heading = L.T("ClientStartup.ThisFolderIsNotAClient"),
        Summary = L.F("ClientStartup.ItWasClassifiedAs0", look.ClassDisplay),
        Sections =
        [
            new ReportSection(L.T("Common.Folder")) { Rows = [ReportRow.Path("Chosen", picked)] },
            new ReportSection(L.T("ClientStartup.Why")) { Bullets = look.Notes.Count > 0 ? look.Notes : [L.T("ClientStartup.NoPackageJsonAssetsCatalogContent")] },
        ],
    };

    /// <summary>Step 4 (8.6): what archiving does, the client with its figures, the personal data left out, the storage, and what "Not now" means.</summary>
    private DialogReport ArchiveQuestion(string title, StartupSource source, string folder, FolderInspection inspection, FolderInventory inventory, bool installed)
    {
        bool complete = inspection.Class == FolderClass.CompleteInstallation;
        string size = ReportWriter.FormatSize(inventory.TotalBytes);
        bool shared = S.ClientStorageMode != ManifestStorage.Independent;
        var sections = new List<ReportSection>();

        var clientRows = new List<ReportRow>
        {
            new(L.T("ClientStartup.Client"), source.Title),
            new(L.T("Common.Server"), ClientServers.Display(ClientServers.Of(source.Scope))),
            new(L.T("Common.Version"), inspection.Version.Version ?? L.T("ClientStartup.Unknown"), inspection.Version.Version == null ? ReportTone.Warning : ReportTone.Normal),
            new(L.T("ClientStartup.WhatItIs"), complete ? L.T("ClientStartup.CompleteInstallation") : inspection.ClassDisplay + L.T("ClientStartup.KeptAsItIsNotCalled")),
            new(L.T("ClientStartup.ToArchive"), $"{L.Count(inventory.Files.Count, "Noun.file")}, {size}"),
            ReportRow.Path(L.T("Common.Folder"), folder),
        };
        if (inventory.Problems.Count > 0)
            clientRows.Add(new(L.T("ClientStartup.CouldNotBeRead"), L.F("ClientStartup.0ListedInTheArchiveWhich", L.Count(inventory.Problems.Count, "Noun.entry")), ReportTone.Warning));
        sections.Add(new ReportSection(L.T("ClientStartup.ClientToArchive")) { Rows = clientRows });

        if (S.ArchivePersonalData)
            sections.Add(new ReportSection(L.T("ClientStartup.PersonalData")) { Text = L.T("ClientStartup.IncludedYourOptionsHotkeysCharacterData") });
        else
        {
            // One row per category, in plain words; a category with nothing in it says so.
            var rows = inventory.Exclusions
                .GroupBy(e => ArchivePreview.CategoryOfRule(e.Rule), StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.Key, Files: g.Sum(e => e.Files), Bytes: g.Sum(e => e.Bytes)))
                .OrderByDescending(r => r.Files).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(r => (IReadOnlyList<string>)[r.Name, r.Files == 0 ? "none" : r.Files.ToString("N0"), r.Files == 0 ? "—" : ReportWriter.FormatSize(r.Bytes)])
                .ToList();
            long excluded = inventory.Exclusions.Sum(e => (long)e.Files);
            sections.Add(new ReportSection(L.T("ClientStartup.LeftOutPersonalData"))
            {
                Text = excluded == 0
                    ? L.T("ClientStartup.ThisFolderHasNoPersonalData")
                    : L.Count(excluded, "ClientStartup.PersonalDataNotArchived"),
                Table = excluded > 0 ? new DialogTable([L.T("Common.Category"), L.T("Common.Files"), L.T("Common.Size")], rows, NumericColumns: 2) : null,
                Note = excluded > 0 ? L.T("ClientStartup.ToArchiveThemTooSettingsTibia") : null,
            });
        }

        var storageRows = new List<ReportRow>
        {
            new(L.T("Common.Storage"), shared ? L.T("ClientStartup.SpaceEfficientEachFileContentIs") : L.T("ClientStartup.IndependentCopiesAFullReadOnly")),
            new(L.T("ClientStartup.DiskSpaceNeeded"), shared ? L.F("ClientStartup.UpTo0ContentsAlreadyArchived", size) : L.F("ClientStartup.About0", size)),
        };
        if (Library.Root is { } root)
        {
            storageRows.Add(ReportRow.Path(L.T("ClientStartup.ArchiveFolder"), root));
            if (FreeSpace(root) is { } free) storageRows.Add(new(L.T("ClientStartup.FreeOnThatDrive"), ReportWriter.FormatSize(free), free < inventory.TotalBytes ? ReportTone.Danger : ReportTone.Normal));
        }
        sections.Add(new ReportSection(L.T("Common.Storage")) { Rows = storageRows });

        var later = new List<string>
        {
            L.T("ClientStartup.NothingIsArchivedAndNothingIs"),
            L.T("ClientStartup.TheClientStaysUsableTibiaFoundry"),
            L.T("ClientStartup.ArchiveItAtAnyTimeFrom"),
        };
        if (installed)
            later.Add(S.ClientChangeHandling == ClientChangeHandling.Automatic
                ? L.T("ClientStartup.AtTheNextStartTheBackground")
                : L.T("ClientStartup.AtTheNextStartTheBackground2"));
        else later.Add(L.T("ClientStartup.AFolderYouChoseIsNot"));
        sections.Add(new ReportSection(L.T("ClientStartup.IfYouChooseNotNow")) { Bullets = later });

        return new DialogReport
        {
            Title = L.F("ClientStartup.04Of4", title), Kind = MessageKind.Question,
            Heading = complete ? L.T("ClientStartup.ArchiveThisInstallationNow") : L.T("ClientStartup.ImportThisFolderIntoTheArchive"),
            Summary = complete
                ? L.F("ClientStartup.AnArchiveKeepsVersion0Exactly", inspection.Version.Version ?? L.T("ClientStartup.Unknown2"))
                : L.T("ClientStartup.TheArchiveKeepsThisFolderExactly"),
            Sections = sections,
            PrimaryText = complete ? L.T("ClientStartup.ArchiveNow") : L.T("ClientStartup.ImportThisFolder"),
            CancelText = L.T("ClientStartup.NotNow"),
        };
    }

    private static long? FreeSpace(string folder)
    {
        try
        {
            string? drive = Path.GetPathRoot(Path.GetFullPath(folder));
            return string.IsNullOrEmpty(drive) || drive.StartsWith(@"\\", StringComparison.Ordinal) ? null : new DriveInfo(drive).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Step 6 (8.6): the client in use and its state, the installation folder, the archive and its storage, the other default clients, and where to change it all.</summary>
    private DialogReport Summary(string title, StartupSource source, string folder, FolderInspection inspection, FolderInventory inventory, bool installed, bool used, SetupArchive outcome)
    {
        bool complete = inspection.Class == FolderClass.CompleteInstallation;
        var sections = new List<ReportSection>
        {
            new(L.T("ClientStartup.ActiveClient"))
            {
                Rows =
                [
                    new(L.T("ClientStartup.Client"), used ? source.Title : L.F("ClientStartup.0NotInUseYouKept", source.Title), used ? ReportTone.Normal : ReportTone.Warning),
                    new(L.T("Common.Server"), ClientServers.Display(ClientServers.Of(source.Scope))),
                    new(L.T("Common.Version"), inspection.Version.Display, inspection.Version.Version == null ? ReportTone.Warning : ReportTone.Normal),
                    new(L.T("Common.Status"), complete ? L.T("ClientStartup.CompleteInstallationReadyToUse") : L.F("ClientStartup.0UsableNotACompleteInstallation", inspection.ClassDisplay),
                        complete ? ReportTone.Success : ReportTone.Warning),
                    new(L.T("ClientStartup.CheckedAtEveryStart"), installed ? L.T("ClientStartup.YesInTheBackgroundOnlyRead") : L.T("ClientStartup.NoOnlyTheInstalledDefaultClients")),
                ],
            },
            new(L.T("ClientStartup.Installation")) { Rows = [ReportRow.Path(L.T("Common.Folder"), folder), new(L.T("ClientStartup.Contents"), $"{L.Count(inventory.Files.Count, "Noun.file")}, {ReportWriter.FormatSize(inventory.TotalBytes)}")] },
        };

        var archiveRows = new List<ReportRow>
        {
            outcome.Archived ? new(L.T("Common.Archive"), L.F("ClientStartup.ArchivedAs01", outcome.Label, L.Count(outcome.Files, "Noun.file")), ReportTone.Success)
            : outcome.Error != null ? new(L.T("Common.Archive"), L.T("ClientStartup.NotArchived") + outcome.Error + L.T("ClientStartup.TheClientsPageCanArchiveIt"), ReportTone.Danger)
            : new(L.T("Common.Archive"), L.T("ClientStartup.NotArchivedYetClientsPageArchive"), ReportTone.Warning),
            new(L.T("ClientStartup.NewArchives"), ManifestStorage.Display(S.ClientStorageMode)),
            new(L.T("ClientStartup.PersonalData"), S.ArchivePersonalData ? L.T("ClientStartup.IncludedInArchives") : L.T("ClientStartup.LeftOutOfArchivesStaysIn")),
        };
        if (Library.Root is { } root) archiveRows.Add(ReportRow.Path(L.T("ClientStartup.ArchiveFolder"), root));
        sections.Add(new ReportSection(L.T("ClientStartup.ArchiveAndStorage")) { Rows = archiveRows, Tone = outcome.Error != null ? ReportTone.Danger : ReportTone.Normal });

        // The other default client, as it is: never shown as available when it is not.
        var others = DefaultClientStatuses().Where(d => !string.Equals(d.Folder, PathGuard.Normalize(folder), StringComparison.OrdinalIgnoreCase)).ToList();
        if (others.Count > 0)
            sections.Add(new ReportSection(L.T("ClientStartup.OtherDefaultClients"))
            {
                Rows = others.Select(d => new ReportRow(d.Label, d.IsUsable ? $"{d.StateDisplay} · {d.Version ?? "version unknown"} · {d.Folder}" : $"{d.StateDisplay} · {d.Folder}",
                    d.IsUsable ? ReportTone.Normal : ReportTone.Muted)).ToList(),
                Note = L.T("ClientStartup.TheServerSwitchInTheNavigation"),
            });

        sections.Add(new ReportSection(L.T("ClientStartup.NextSteps"))
        {
            Bullets =
            [
                L.T("ClientStartup.ChooseAnotherClientBrowseForA"),
                L.T("ClientStartup.StorageTheCheckAtStartAnd"),
                L.T("ClientStartup.TheseQuestionsAgainSettingsTibiaClient"),
            ],
        });

        return new DialogReport
        {
            Title = title, Kind = outcome.Error != null ? MessageKind.Warning : MessageKind.Info,
            Heading = L.T("ClientStartup.TibiaFoundryIsSetUp"),
            Summary = used ? L.F("ClientStartup.0IsTheClientInUse", source.Title) : L.T("ClientStartup.YourEarlierClientChoiceWasKept"),
            Sections = sections,
        };
    }

    /// <summary>
    /// The archive question and the summary of setup for the first usable default client of this PC,
    /// with its real figures, for the UI snapshot. Only reads the client; asks nothing and archives
    /// nothing (the summary shows both outcomes: not archived, and archived as the newest revision when there is one).
    /// </summary>
    internal async Task<IReadOnlyList<(string Name, DialogReport Report)>> SnapshotReportsAsync()
    {
        string Title = L.T("ClientStartup.SetUpTibiaFoundry");
        var defaults = await Task.Run(() => DefaultClientStatuses());
        if (defaults.FirstOrDefault(d => d.IsUsable) is not { Inspection: { } inspection } chosen) return [];
        var inventory = await Task.Run(() => ClientInventory.Take(chosen.Folder, new InventoryOptions { IncludePersonalData = S.ArchivePersonalData }));
        var source = new StartupSource(chosen.Scope, null, chosen.Scope == ClientScope.Live ? LiveGroup : TestGroup, chosen.Label, chosen.Folder);
        var newest = Library.ExistingStore?.Registry.Versions.Where(v => v.Server == ClientServers.Of(chosen.Scope)).OrderByDescending(v => v.ImportedAtUtc).FirstOrDefault();
        // The UI snapshot runs with its own empty archive: the "archived" outcome is then a sample with this client's own figures.
        var archived = newest != null ? new SetupArchive(true, newest.Label, newest.Files, null)
            : new SetupArchive(true, $"{chosen.Label} {inspection.Version.Version ?? "(version unknown)"}", inventory.Files.Count, null);
        return
        [
            ("setup-archive-question", ArchiveQuestion(Title, source, chosen.Folder, inspection, inventory, installed: true)),
            ("setup-summary", Summary(Title, source, chosen.Folder, inspection, inventory, installed: true, used: true, archived)),
            ("setup-summary-not-archived", Summary(Title, source, chosen.Folder, inspection, inventory, installed: true, used: true, SetupArchive.NotNow)),
        ];
    }

    private async Task<SetupArchive> ArchiveAsync(StartupSource source, string folder, FolderInspection inspection)
    {
        using var operation = services.Status.BeginOperation(L.T("ClientStartup.ArchivingTheClientReadingEveryFile"), canCancel: false);
        try
        {
            bool personal = S.ArchivePersonalData;
            string registration = source.ActiveId ?? ClientRegistration.InstalledFor(source.Scope);
            var result = await Task.Run(() =>
            {
                var scan = ClientInventory.Scan(folder, new InventoryOptions { IncludePersonalData = personal });
                string scope = inspection.Class == FolderClass.CompleteInstallation ? ManifestScopes.CompleteInstallation : ManifestScopes.StandaloneFolder;
                var store = Library.Store;
                return store.Archive(new ArchiveRequest(scan, ClientServers.Of(source.Scope), scope, Library.StorageMode,
                    source.ActiveId == null ? SourceTypes.InstalledPackage : SourceTypes.StandaloneFolder, source.Title, registration, inspection.Version));
            });
            services.Status.Show(L.F("ClientStartup.Archived01", result.Version.Label, L.Count(result.Version.Files, "Noun.file")), StatusKind.Success);
            return new SetupArchive(true, result.Version.Label, result.Version.Files, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            AppLog.Warn("Setup archive: " + ex.Message);
            return new SetupArchive(false, null, 0, ex.Message);
        }
    }
}
