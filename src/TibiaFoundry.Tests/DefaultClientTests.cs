using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.Tests;

/// <summary>
/// 8.6: the Default Live Client (packages\Tibia) and Default Test Client (packages\TibiaExternal):
/// found from the Windows profile (never a user name), classified by their files, shown as they are
/// in setup (a missing or unusable one is never available), and a client chosen before is only
/// replaced with consent.
/// </summary>
public sealed class DefaultClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Env : IDisposable
    {
        public string Dir { get; }
        public string Packages { get; }
        public string Live => Path.Combine(Packages, "Tibia");
        public string Test => Path.Combine(Packages, "TibiaExternal");
        public AppSettings Settings { get; }
        public AppServices Services { get; }
        public FakeDialogs Dialogs { get; } = new();
        public ClientStartup Startup { get; }

        /// <param name="profile">A folder that stands for another Windows profile (spaces and non-ASCII characters on purpose).</param>
        public Env(bool live, bool test, string profile = "Some Other User")
        {
            Dir = TestData.NewTempDirectory("defaults");
            Packages = Path.Combine(Dir, "Users", profile, "AppData", "Local", "Tibia", "packages");
            Directory.CreateDirectory(Packages);
            if (live) ClientIntegrationTests.RealisticPackage(Live, "15.33.9d148f");
            if (test) ClientIntegrationTests.RealisticPackage(Test, "15.40.aaaaaa", "test interface");
            Settings = new AppSettings { LoadDataAutomatically = false };
            Settings.Normalize();
            Settings.ClientSetupCompleted = false;
            Settings.SetChosenLocation(LocationIds.PackagesFolder, ClientScope.Live, Packages);
            Services = new AppServices(new SettingsStore(Path.Combine(Dir, "data", "settings.json")), Settings) { Paths = TestData.PathsIn(Path.Combine(Dir, "data")) };
            Services.Dialogs = Dialogs;
            Startup = new ClientStartup(Services) { FindRoots = () => [new InstallationRoot(Packages, "the test's packages folder")] };
        }

        public IReadOnlyList<DefaultClientStatus> Defaults() => Startup.DefaultClientStatuses();

        public void Dispose() => Services.Dispose();
    }

    [Fact]
    public void TheDefaultPackagesFolderComesFromTheWindowsProfileNotAUserName()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal(Path.Combine(local, "Tibia", "packages"), TibiaClientPaths.PackagesRoot);
        Assert.Equal(Path.Combine(local, "Tibia", "packages", "Tibia"), DefaultClients.FolderOf(ClientScope.Live, TibiaClientPaths.PackagesRoot));
        Assert.Equal(Path.Combine(local, "Tibia", "packages", "TibiaExternal"), DefaultClients.FolderOf(ClientScope.TestServer, TibiaClientPaths.PackagesRoot));
        // On the release PC the defaults are exactly the requested folders.
        if (string.Equals(local, @"C:\Users\Rocky\AppData\Local", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Equal(@"C:\Users\Rocky\AppData\Local\Tibia\packages\Tibia", DefaultClients.FolderOf(ClientScope.Live, TibiaClientPaths.PackagesRoot));
            Assert.Equal(@"C:\Users\Rocky\AppData\Local\Tibia\packages\TibiaExternal", DefaultClients.FolderOf(ClientScope.TestServer, TibiaClientPaths.PackagesRoot));
        }
        // The resolver of a fresh installation uses the same folders.
        var settings = new AppSettings();
        settings.Normalize();
        Assert.Equal(Path.Combine(local, "Tibia", "packages", "Tibia"), settings.LocationsFor(ClientScope.Live).Resolve(LocationIds.Package).DisplayPath);
        Assert.Equal(Path.Combine(local, "Tibia", "packages", "TibiaExternal"), settings.LocationsFor(ClientScope.TestServer).Resolve(LocationIds.Package).DisplayPath);
        // No source file builds a client path from a user name.
        foreach (string file in Directory.EnumerateFiles(ClientStoreTests.SourceRoot(), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.EndsWith("DefaultClientTests.cs", StringComparison.Ordinal)))
            Assert.DoesNotContain(@"Users\Rocky", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BothDefaultClientsAreFoundLabeledAndVersioned()
    {
        using var env = new Env(live: true, test: true);
        var d = env.Defaults();
        Assert.Equal([DefaultClients.LiveLabel, DefaultClients.TestLabel], d.Select(x => x.Label));
        Assert.All(d, x => Assert.Equal(DefaultClientState.Available, x.State));
        Assert.Equal("15.33.9d148f", d[0].Version);
        Assert.Equal("15.40.aaaaaa", d[1].Version);
        Assert.Equal(env.Live, d[0].Folder);
        Assert.Equal(env.Test, d[1].Folder);
        Assert.Equal("Live server", d[0].Server);
        Assert.Equal("Test server", d[1].Server);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task AMissingDefaultClientIsShownButNeverAvailable(bool live, bool test)
    {
        using var env = new Env(live, test);
        var d = env.Defaults();
        Assert.Equal(live ? DefaultClientState.Available : DefaultClientState.Missing, d[0].State);
        Assert.Equal(test ? DefaultClientState.Available : DefaultClientState.Missing, d[1].State);
        Assert.Equal(live, d[0].IsUsable);
        Assert.Equal(test, d[1].IsUsable);

        await env.Startup.RunAsync();   // no answers: step 1 is cancelled (setup skipped)
        var items = env.Dialogs.ChoiceLists[0].Items;
        Assert.Equal(live, items[0].Enabled);
        Assert.Equal(test, items[1].Enabled);
        if (!live) Assert.Contains("not installed", items[0].Title, StringComparison.Ordinal);
        if (!live && !test)
        {
            Assert.Null(env.Dialogs.ChoiceLists[0].Options.Selected);
            Assert.Contains("No usable Tibia client was found", env.Dialogs.ChoiceLists[0].Message, StringComparison.Ordinal);
        }
        else Assert.Equal(live ? 0 : 1, env.Dialogs.ChoiceLists[0].Options.Selected);
    }

    [Fact]
    public async Task OnlyTheTestClientCanBeSetUpAsTheTestServer()
    {
        using var env = new Env(live: false, test: true);
        env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(1, false, null));   // 1: the Default Test Client
        env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(0, false, null));   // 2: space-efficient
        env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(0, false, null));   // 3: ask
        env.Dialogs.ReportAnswers.Enqueue(ReportAnswer.Cancel);                // 4: not now
        Assert.StartsWith("completed: " + DefaultClients.TestLabel, await env.Startup.RunAsync(), StringComparison.Ordinal);
        Assert.True(env.Settings.UseTestServer);
        Assert.Null(env.Settings.ActiveClient(ClientScope.TestServer));   // the installed test client
        var summary = env.Dialogs.Reports[^1];
        Assert.Contains(summary.Sections[0].Rows, r => r.Label == "Server" && r.Value == "Test server");
        Assert.Contains(summary.Sections[2].Rows, r => r.Label == "Archive" && r.Value.StartsWith("Not archived yet", StringComparison.Ordinal));
        Assert.Contains(summary.Sections[3].Rows, r => r.Label == DefaultClients.LiveLabel && r.Value.StartsWith("Not installed", StringComparison.Ordinal));
        Assert.False(env.Services.Clients.StoreExists);   // "Not now" wrote nothing
    }

    [Fact]
    public void AnIncompleteOrForeignFolderIsNotUsable()
    {
        using var env = new Env(live: false, test: false);
        // Client data files without a catalog: something of a client, not enough for any tool.
        Directory.CreateDirectory(env.Live);
        File.WriteAllBytes(Path.Combine(env.Live, "appearances-0001.dat"), [1, 2, 3]);
        // A folder named like the test client that holds something else.
        Directory.CreateDirectory(env.Test);
        File.WriteAllText(Path.Combine(env.Test, "readme.txt"), "not a client");
        var d = env.Defaults();
        Assert.Equal(DefaultClientState.Incomplete, d[0].State);
        Assert.Equal(DefaultClientState.NotAClient, d[1].State);
        Assert.All(d, x => Assert.False(x.IsUsable));
    }

    [Fact]
    public void AClientWithoutVersionInformationIsUsableWithAnUnknownVersion()
    {
        using var env = new Env(live: true, test: false);
        File.Delete(Path.Combine(env.Live, "package.json"));
        var live = env.Defaults()[0];
        Assert.True(live.IsUsable);
        Assert.Null(live.Version);
        Assert.Equal(DefaultClientState.Partial, live.State);   // without package.json it is not a complete installation
    }

    [Fact]
    public async Task ACustomFolderWithSpacesCanBeChosenAndIsRemembered()
    {
        using var env = new Env(live: true, test: false, profile: "Jürgen Müller (2)");
        string custom = ClientIntegrationTests.RealisticPackage(Path.Combine(env.Dir, "My Tibia copies", "client #1 [old]"), "15.20.111111");
        env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(-1, false, "browse"));
        env.Dialogs.NextFile = custom;
        env.Dialogs.ChoiceAnswers.Enqueue(null);   // 2: skip the rest (the client is set up)
        Assert.Equal("set up the client only", await env.Startup.RunAsync());
        string? id = env.Settings.ActiveClient(ClientScope.Live);
        Assert.NotNull(id);
        Assert.Equal(custom, env.Services.Clients.Store.Registry.Registration(id)!.ResolveSource(null));
        Assert.Equal(id, env.Services.SettingsStore.Load().Settings.ActiveClientLive);
        Assert.Contains("Jürgen Müller (2)", env.Defaults()[0].Folder, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetupAgainOnlyReplacesAnEarlierChoiceWithConsent(bool replace)
    {
        using var env = new Env(live: true, test: false);
        string custom = ClientIntegrationTests.RealisticPackage(Path.Combine(env.Dir, "copy"), "15.20.111111");
        var registration = env.Services.Clients.Store.RegisterFolder(custom, ClientServers.Live, "My copy", SourceTypes.StandaloneFolder);
        env.Settings.SetActiveClient(ClientScope.Live, registration.Id);
        env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(0, false, null));   // 1: the Default Live Client
        env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(0, false, null));   // 2
        env.Dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(0, false, null));   // 3
        env.Dialogs.ReportAnswers.Enqueue(ReportAnswer.Cancel);                // 4: not now
        env.Dialogs.ConfirmAnswer = replace;
        await env.Startup.SetupAsync();
        Assert.Single(env.Dialogs.ConfirmQuestions);
        Assert.Contains("My copy", env.Dialogs.ConfirmQuestions[0], StringComparison.Ordinal);
        Assert.Equal(replace ? null : registration.Id, env.Settings.ActiveClient(ClientScope.Live));
        var client = env.Dialogs.Reports[^1].Sections[0].Rows[0];
        Assert.Equal(replace ? ReportTone.Normal : ReportTone.Warning, client.Tone);
    }

    [Fact]
    public void AnUnreadableOrInvalidPathIsReportedNotThrown()
    {
        var status = DefaultClients.Inspect(ClientScope.Live, "C:\\bad\0path");
        Assert.Equal(DefaultClientState.Inaccessible, status.State);
        Assert.False(status.IsUsable);
        var missing = DefaultClients.Inspect(ClientScope.TestServer, Path.Combine(TestData.NewTempDirectory("none"), "TibiaExternal"));
        Assert.Equal(DefaultClientState.Missing, missing.State);
        Assert.Equal("Not installed", missing.StateDisplay);
    }

    [Fact]
    public void ReportsReadAsPlainText()
    {
        var report = new DialogReport
        {
            Title = "t", Heading = "Heading", Summary = "Summary",
            Sections =
            [
                new ReportSection("Rows") { Rows = [new("A", "1"), ReportRow.Path("Folder", @"C:\x y\z")] },
                new ReportSection("Table") { Table = new DialogTable(["Name", "Files"], [["Logs", "6"]], 1), Bullets = ["one"], Note = "note" },
            ],
        };
        string text = report.ToPlainText();
        Assert.Contains("  Folder: C:\\x y\\z", text, StringComparison.Ordinal);
        Assert.Contains("  Logs | 6", text, StringComparison.Ordinal);
        Assert.Contains("  • one", text, StringComparison.Ordinal);
        Assert.Equal("1 file", Plural.Count(1, "file"));
        Assert.Equal("3,373 files", Plural.Count(3373, "file"));
        Assert.Equal("2 entries", Plural.Count(2, "entry", "entries"));
        _ = Ct;
    }
}
