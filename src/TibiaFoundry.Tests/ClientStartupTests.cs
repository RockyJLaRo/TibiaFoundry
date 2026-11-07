using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.Tests;

/// <summary>
/// 8.0 start: first-run setup (only for a new installation; skippable), "ask which client" with
/// Remember my choice, and the recovery dialog when the remembered client cannot be used — with
/// scripted dialogs. Upgrading from 7.x changes nothing at the first start.
/// </summary>
public sealed class ClientStartupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Env : IDisposable
    {
        public string Dir { get; } = TestData.NewTempDirectory("startup");
        public string Packages => Path.Combine(Dir, "packages");
        public string Live => Path.Combine(Packages, "Tibia");
        public AppSettings Settings { get; }
        public AppServices Services { get; }
        public FakeDialogs Dialogs { get; } = new();
        public ClientStartup Startup { get; }

        public Env(bool setupDone = true)
        {
            ClientIntegrationTests.RealisticPackage(Live, "15.33.9d148f");
            Settings = new AppSettings { LoadDataAutomatically = false };
            Settings.Normalize();
            Settings.ClientSetupCompleted = setupDone;
            Settings.SetChosenLocation(LocationIds.PackagesFolder, ClientScope.Live, Packages);
            Services = new AppServices(new SettingsStore(Path.Combine(Dir, "data", "settings.json")), Settings) { Paths = TestData.PathsIn(Path.Combine(Dir, "data")) };
            Services.Dialogs = Dialogs;
            Startup = new ClientStartup(Services) { FindRoots = () => [new InstallationRoot(Packages, "the test's packages folder")] };
        }

        /// <summary>A copy of a client somewhere else (a standalone folder).</summary>
        public string Copy(string name, string version = "15.32.aaaaaa") => ClientIntegrationTests.RealisticPackage(Path.Combine(Dir, "elsewhere", name), version, "copy " + name);

        public void Dispose() => Services.Dispose();
    }

    [Fact]
    public async Task ANewInstallationIsSetUpFoundChosenAndArchived()
    {
        using var env = new Env(setupDone: false);
        env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(0, false, null));   // 1: the installation found
        env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(1, false, null));   // 2: independent copies
        env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(0, false, null));   // 3: ask when it changes
        env.Dialogs.ReportAnswers.Enqueue(ReportAnswer.Primary);               // 4: archive now
        string what = await env.Startup.RunAsync();
        Assert.StartsWith("completed", what, StringComparison.Ordinal);
        Assert.Contains("Space-efficient", string.Join(" ", env.Dialogs.ChoiceLists[1].Items.Select(i => i.Title)));
        Assert.Contains(env.Dialogs.ChoiceLists[1].Options.Facts, f => f is { Label: "What it is" } && f.Value.StartsWith("Complete installation", StringComparison.Ordinal)); // the summary card (8.8)
        var saved = env.Services.SettingsStore.Load().Settings;
        Assert.True(saved.ClientSetupCompleted);
        Assert.Equal(ManifestStorage.Independent, saved.ClientStorageMode);
        Assert.Equal(ClientChangeHandling.Ask, saved.ClientChangeHandling);
        Assert.Null(saved.ActiveClientLive);   // the installed client, followed as before
        var revision = Assert.Single(env.Services.Clients.Store.Registry.Versions);
        Assert.Equal(ManifestStorage.Independent, revision.EffectiveStorage);
        Assert.Equal(ManifestScopes.CompleteInstallation, revision.EffectiveScope);
        // Step 1 lists both default clients; the missing test client is shown but cannot be chosen.
        var first = env.Dialogs.ChoiceLists[0].Items;
        Assert.StartsWith(DefaultClients.LiveLabel + " — 15.33.9d148f", first[0].Title, StringComparison.Ordinal);
        Assert.True(first[0].Enabled);
        Assert.StartsWith(DefaultClients.TestLabel + " — not installed", first[1].Title, StringComparison.Ordinal);
        Assert.False(first[1].Enabled);
        // Step 4: the archive question in sections, with the real figures.
        var question = env.Dialogs.Reports[0];
        Assert.Equal("Archive this installation now?", question.Heading);
        Assert.Equal(["Client to archive", "Left out: personal data", "Storage", "If you choose Not now"], question.Sections.Select(s => s.Heading));
        Assert.Contains(question.Sections[0].Rows, r => r.Label == "Version" && r.Value == "15.33.9d148f");
        Assert.Contains(question.Sections[0].Rows, r => r.IsPath && r.Value == env.Live);
        Assert.Equal("Not now", question.CancelText);
        // Step 6: the summary, with what actually happened.
        var summary = env.Dialogs.Reports[^1];
        Assert.Equal("Tibia Foundry is set up", summary.Heading);
        Assert.Equal(["Active client", "Installation", "Archive and storage", "Other default clients", "Next steps"], summary.Sections.Select(s => s.Heading));
        Assert.Contains(summary.Sections[0].Rows, r => r.Label == "Client" && r.Value == DefaultClients.LiveLabel);
        Assert.Contains(summary.Sections[2].Rows, r => r.Label == "Archive" && r.Value.StartsWith("Archived as", StringComparison.Ordinal) && r.Tone == ReportTone.Success);
        Assert.Contains(summary.Sections[3].Rows, r => r.Label == DefaultClients.TestLabel && r.Value.StartsWith("Not installed", StringComparison.Ordinal));
        Assert.Contains(env.Dialogs.Messages, m => m.Message.Contains("Tibia Foundry is set up", StringComparison.Ordinal));
        // Setup runs once.
        Assert.DoesNotContain("completed", await env.Startup.RunAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetupCanBeSkippedAndUpgradingNeverShowsIt()
    {
        using var env = new Env(setupDone: false);
        env.Dialogs.ChoiceAnswers.Enqueue(null);
        Assert.Equal("skipped", await env.Startup.RunAsync());
        Assert.True(env.Services.SettingsStore.Load().Settings.ClientSetupCompleted);
        Assert.False(env.Services.Clients.StoreExists);

        // Settings written by 7.3 (format 6) count as set up: the first 8.0 start asks nothing.
        File.WriteAllText(Path.Combine(env.Dir, "v6.json"), """{ "Version": 6, "ClientImportReminders": false }""");
        var (upgraded, _, _) = new SettingsStore(Path.Combine(env.Dir, "v6.json")).Load();
        Assert.True(upgraded.ClientSetupCompleted);
        Assert.Equal(StartupClientMode.Remembered, upgraded.StartupClientMode);
        Assert.Equal(ClientChangeHandling.AskQuietly, upgraded.ClientChangeHandling);
        using var env2 = new Env(setupDone: true);
        Assert.Equal("the installed client (remembered)", await env2.Startup.RunAsync());
        Assert.Empty(env2.Dialogs.ChoiceLists);
    }

    [Fact]
    public async Task AskWhichClientGroupsTheSourcesAndRememberMyChoiceStopsAsking()
    {
        using var env = new Env();
        var folder = env.Services.Clients.Store.RegisterFolder(env.Copy("copy"), ClientServers.Live, "My copy", SourceTypes.StandaloneFolder);
        env.Settings.StartupClientMode = StartupClientMode.Ask;
        var sources = env.Startup.UsableSources();
        Assert.Equal([ClientStartup.LiveGroup, ClientStartup.FolderGroup], sources.Select(s => s.Group));
        int copy = sources.ToList().FindIndex(s => s.ActiveId == folder.Id);

        env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(copy, false, null));
        await env.Startup.RunAsync();
        Assert.Equal(folder.Id, env.Settings.ActiveClientLive);
        Assert.Equal(StartupClientMode.Ask, env.Settings.StartupClientMode);   // a choice never turns "ask" off by itself
        Assert.Equal(0, env.Dialogs.ChoiceLists[0].Options.Selected);   // the client in use was preselected

        env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(0, true, null));   // the installed client, and remember it
        await env.Startup.RunAsync();
        Assert.Null(env.Settings.ActiveClientLive);
        Assert.Equal(StartupClientMode.Remembered, env.Services.SettingsStore.Load().Settings.StartupClientMode);
        await env.Startup.RunAsync();
        Assert.Equal(2, env.Dialogs.ChoiceLists.Count);   // not asked again
    }

    [Fact]
    public async Task AMovedFolderOffersItsArchivedRevisionOrToLocateIt()
    {
        using var env = new Env();
        string copy = env.Copy("copy");
        var store = env.Services.Clients.Store;
        var folder = store.RegisterFolder(copy, ClientServers.Live, "My copy", SourceTypes.StandaloneFolder);
        var scan = ClientInventory.Scan(copy, ct: Ct);
        var revision = store.Archive(new ArchiveRequest(scan, ClientServers.Live, ManifestScopes.StandaloneFolder, ManifestStorage.Shared, SourceTypes.StandaloneFolder, "My copy",
            folder.Id, VersionEvidence.Collect(copy)), ct: Ct).Version;
        env.Settings.SetActiveClient(ClientScope.Live, folder.Id);
        Assert.Equal("the remembered client", env.Startup.CheckRemembered());

        string moved = copy + " moved";
        Directory.Move(copy, moved);
        // Keep my choice: nothing changes.
        env.Dialogs.ChoiceAnswers.Enqueue(null);
        Assert.StartsWith("kept the remembered client", env.Startup.CheckRemembered(), StringComparison.Ordinal);
        Assert.Equal(folder.Id, env.Settings.ActiveClientLive);
        var dialog = env.Dialogs.ChoiceLists[^1];
        Assert.StartsWith("Use the archived revision of the same version", dialog.Items[0].Title, StringComparison.Ordinal);
        Assert.Contains(dialog.Options.ExtraButtons, b => b.Id == "locate");

        // Locate the folder: the registration follows it.
        env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(-1, false, "locate"));
        env.Dialogs.NextFile = moved;
        Assert.Equal("located the remembered folder again", env.Startup.CheckRemembered());
        Assert.Equal("the remembered client", env.Startup.CheckRemembered());

        // Gone again: use the archived revision of the same version.
        Directory.Move(moved, moved + " again");
        env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(0, false, null));
        env.Startup.CheckRemembered();
        Assert.Equal(revision.Key, env.Settings.ActiveClientLive);
        Assert.NotNull(env.Services.Data.ImportedVersion);
    }

    [Fact]
    public async Task SettingsChangeTheArchiveAndStartupOptionsAndTheRememberedClient()
    {
        await Sta.Run(() =>
        {
            using var env = new Env();
            var main = new App.ViewModels.MainViewModel(env.Services);
            var page = main.Pages.OfType<App.ViewModels.SettingsViewModel>().Single();
            page.ClientChangeHandling = ClientChangeHandling.AskQuietly;
            page.ClientStorageMode = ManifestStorage.Independent;
            page.ClientLaunchCheck = ClientLaunchCheck.Quick;
            page.MonitorInstalledTest = false;
            page.StartupClientMode = StartupClientMode.Ask;
            var saved = env.Services.SettingsStore.Load().Settings;
            Assert.Equal(ClientChangeHandling.AskQuietly, saved.ClientChangeHandling);
            Assert.False(saved.ClientImportReminders);   // kept in step for 7.3
            Assert.Equal(ManifestStorage.Independent, saved.ClientStorageMode);
            Assert.Equal(ClientLaunchCheck.Quick, saved.ClientLaunchCheck);
            Assert.False(saved.MonitorInstalledTest);
            Assert.Equal(StartupClientMode.Ask, saved.StartupClientMode);

            var folder = env.Services.Clients.Store.RegisterFolder(env.Copy("copy"), ClientServers.Live, "My copy", SourceTypes.StandaloneFolder);
            env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(1, false, null));
            page.SwitchClientCommand.Execute(null);
            Assert.Equal(folder.Id, env.Settings.ActiveClientLive);
            Assert.Contains("My copy", page.RememberedClientText);
            page.ClearRememberedClientCommand.Execute(null);
            Assert.Null(env.Settings.ActiveClientLive);
            Assert.Contains("(installed)", page.RememberedClientText);
            return Task.CompletedTask;
        });
    }
}
