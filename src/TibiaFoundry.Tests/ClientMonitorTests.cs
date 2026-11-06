using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.Tests;

/// <summary>
/// 8.0 launch check: baselines, Full and Quick checks, checks that are not accepted (files changing,
/// damaged files, the launcher running, a missing folder), and what follows from the change handling
/// (Automatic, Ask with Later and Don't ask, Ask quietly, monitoring off). The client is only read.
/// </summary>
public sealed class ClientMonitorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeClient NewClient(string name = "monitor")
    {
        string dir = TestData.NewTempDirectory(name);
        var c = FakeClient.Standard(Path.Combine(dir, "packages", "Tibia")).Write();
        c.WriteFile("Auction_Id_1.html", FakeClient.Bytes("<html>a saved page</html>"));
        return c;
    }

    private static ClientBaseline Accept(LaunchCheckResult result) =>
        LaunchCheck.BaselineOf(result, ClientRegistration.InstalledLive, ClientServers.Live, false, DateTime.UtcNow, null);

    [Fact]
    public void TheFirstCheckIsNewTheNextUnchangedAndTheClientIsOnlyRead()
    {
        var client = NewClient();
        var before = FakeClient.Snapshot(client.Folder);
        var first = LaunchCheck.Run(client.Folder, null, quick: false, new InventoryOptions(), ct: Ct);
        Assert.Equal(LaunchCheckOutcome.New, first.Outcome);
        Assert.Equal("15.33.9d148f", first.Version.Version);
        var baseline = Accept(first);
        Assert.Equal(first.Scan!.Files.Count, baseline.Files.Count);
        Assert.Equal(first.Fingerprint, baseline.Fingerprint);

        var second = LaunchCheck.Run(client.Folder, baseline, quick: false, new InventoryOptions(), ct: Ct);
        Assert.Equal(LaunchCheckOutcome.Unchanged, second.Outcome);
        Assert.Equal(second.Scan!.Files.Count, second.Scan.HashedFiles);   // Full reads every file
        Assert.Equal(before, FakeClient.Snapshot(client.Folder));
    }

    [Fact]
    public void FullFindsEveryChangeAndQuickTrustsOnlyUnchangedSizeAndTime()
    {
        var client = NewClient();
        var baseline = Accept(LaunchCheck.Run(client.Folder, null, quick: false, new InventoryOptions(), ct: Ct));

        // Other bytes, same size and time, in a file no client list names.
        string page = client.PathOf("Auction_Id_1.html");
        var time = File.GetLastWriteTimeUtc(page);
        File.WriteAllBytes(page, FakeClient.Bytes("<html>b saved page</html>"));
        File.SetLastWriteTimeUtc(page, time);
        var full = LaunchCheck.Run(client.Folder, baseline, quick: false, new InventoryOptions(), ct: Ct);
        Assert.Equal(LaunchCheckOutcome.Changed, full.Outcome);
        Assert.Equal(1, full.ChangedFiles);
        var quick = LaunchCheck.Run(client.Folder, baseline, quick: true, new InventoryOptions(), ct: Ct);
        Assert.Equal(LaunchCheckOutcome.Unchanged, quick.Outcome);   // documented: Quick can miss a change that keeps size and time
        Assert.True(quick.Scan!.HashedFiles < quick.Scan.Files.Count / 2, $"{quick.Scan.HashedFiles} of {quick.Scan.Files.Count} read");

        // A change of size, an added and a removed file: Quick sees them too.
        File.WriteAllBytes(page, FakeClient.Bytes("<html>a longer saved page</html>"));
        client.WriteFile("Auction_Id_2.html", FakeClient.Bytes("<html>another</html>"));
        File.Delete(client.PathOf("package.json.version"));
        quick = LaunchCheck.Run(client.Folder, baseline, quick: true, new InventoryOptions(), ct: Ct);
        Assert.Equal(LaunchCheckOutcome.Changed, quick.Outcome);
        Assert.Equal((1, 1, 1), (quick.ChangedFiles, quick.AddedFiles, quick.RemovedFiles));
        Assert.Equal("1 file changed, 1 added, 1 removed, +" + Core.FileOps.ReportWriter.FormatSize(quick.BytesDelta), quick.ChangeSummary());
    }

    [Fact]
    public void ADamagedListedFileTheLauncherOrAMissingFolderIsNeverAccepted()
    {
        var client = NewClient();
        var baseline = Accept(LaunchCheck.Run(client.Folder, null, quick: false, new InventoryOptions(), ct: Ct));

        var deferred = LaunchCheck.Run(client.Folder, baseline, quick: false, new InventoryOptions(), launcherActive: _ => true, ct: Ct);
        Assert.Equal(LaunchCheckOutcome.Deferred, deferred.Outcome);
        Assert.Null(deferred.Scan);
        Assert.False(deferred.Accepted);

        // The launcher is half-way through: a file differs from the client's own list.
        File.WriteAllBytes(client.PathOf("assets/sprites-bbbb.bmp.lzma"), FakeClient.Bytes("sheet 11-20 SHARED, half written"));
        var damaged = LaunchCheck.Run(client.Folder, baseline, quick: true, new InventoryOptions(), ct: Ct);
        Assert.Equal(LaunchCheckOutcome.Inconsistent, damaged.Outcome);
        Assert.Contains(damaged.Problems, p => p.Contains("assets/sprites-bbbb.bmp.lzma", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => Accept(damaged));

        var missing = LaunchCheck.Run(Path.Combine(client.Folder, "gone"), baseline, quick: false, new InventoryOptions(), ct: Ct);
        Assert.Equal(LaunchCheckOutcome.Unavailable, missing.Outcome);
    }

    [Fact]
    public void BaselinesAreKeptNextToTheArchiveWithoutCreatingIt()
    {
        var client = NewClient();
        string root = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(client.Folder))!, "data", ClientStore.FolderName);
        Assert.Null(ClientBaselines.Read(root, ClientRegistration.InstalledLive));
        var baseline = Accept(LaunchCheck.Run(client.Folder, null, quick: false, new InventoryOptions(), ct: Ct));
        ClientBaselines.Write(root, baseline);
        var read = ClientBaselines.Read(root, ClientRegistration.InstalledLive)!;
        Assert.Equal(baseline.Fingerprint, read.Fingerprint);
        Assert.Equal(baseline.Files, read.Files);
        Assert.False(File.Exists(Path.Combine(root, ClientRegistry.FileName)));
        // A damaged baseline is ignored (the next check starts afresh); one with the other personal-data rule too.
        File.WriteAllText(ClientBaselines.PathFor(root, ClientRegistration.InstalledLive), "{ not json");
        Assert.Null(ClientBaselines.Read(root, ClientRegistration.InstalledLive));
        Assert.Equal(LaunchCheckOutcome.New, LaunchCheck.Run(client.Folder, baseline, quick: false, new InventoryOptions { IncludePersonalData = true }, ct: Ct).Outcome);
    }

    // ------------------------------------------------------------------ what follows (the main window and the Clients page)

    private static (AppServices Services, MainViewModel Main, ClientsViewModel Page) Create(ClientChangeHandling handling)
    {
        string dir = TestData.NewTempDirectory("monitorapp");
        string packages = Path.Combine(dir, "packages");
        ClientIntegrationTests.RealisticPackage(Path.Combine(packages, "Tibia"), "15.33.9d148f");
        var settings = new AppSettings { LoadDataAutomatically = false };
        settings.Normalize();
        settings.ClientChangeHandling = handling;
        settings.SetChosenLocation(LocationIds.PackagesFolder, ClientScope.Live, packages);
        var services = new AppServices(new SettingsStore(Path.Combine(dir, "data", "settings.json")), settings) { Paths = TestData.PathsIn(Path.Combine(dir, "data")) };
        services.Dialogs = new FakeDialogs();
        services.Clients.LauncherActive = _ => false;
        var main = new MainViewModel(services);
        return (services, main, main.Pages.OfType<ClientsViewModel>().Single());
    }

    [Fact]
    public async Task AskShowsANoticeThatLaterBringsBackAndDontAskEnds()
    {
        await Sta.Run(async () =>
        {
            var (services, main, page) = Create(ClientChangeHandling.Ask);
            using var _ = services;
            await main.CheckInstalledClientsAsync();
            Assert.Contains("15.33.9d148f is not archived yet", main.ClientChange!.Text);
            Assert.False(services.Clients.StoreExists);   // asking never creates the archive

            main.LaterClientChangeCommand.Execute(null);
            Assert.Null(main.ClientChange);
            await main.CheckInstalledClientsAsync();   // the next start
            Assert.NotNull(main.ClientChange);

            main.DontAskClientChangeCommand.Execute(null);
            Assert.Null(main.ClientChange);
            await main.CheckInstalledClientsAsync();
            Assert.Null(main.ClientChange);
            await page.OnNavigatedToAsync();
            Assert.Null(page.Reminder);
            Assert.Empty(services.Clients.Store.Registry.Versions);   // nothing was archived
        });
    }

    [Fact]
    public async Task ArchiveOnTheNoticeAndAutomaticArchiveInTheBackgroundOnce()
    {
        await Sta.Run(async () =>
        {
            var (services, main, page) = Create(ClientChangeHandling.Ask);
            using var _ = services;
            await main.CheckInstalledClientsAsync();
            await main.ArchiveClientChangeCommand.ExecuteAsync(null);
            var revision = Assert.Single(services.Clients.Store.Registry.Versions);
            Assert.Equal(ManifestScopes.CompleteInstallation, revision.EffectiveScope);
            Assert.Contains("Archived the installed live client", main.Status.Message);

            var (services2, main2, page2) = Create(ClientChangeHandling.Automatic);
            using var __ = services2;
            await main2.CheckInstalledClientsAsync();
            Assert.Null(main2.ClientChange);
            Assert.Single(services2.Clients.Store.Registry.Versions);
            await page2.OnNavigatedToAsync();
            Assert.Contains("archived as", page2.Sources[0].Status);
            Assert.Null(page2.Reminder);
            // The next start: unchanged and archived, so nothing more is written.
            await main2.CheckInstalledClientsAsync();
            Assert.Equal(LaunchCheckOutcome.Unchanged, services2.Clients.LastCheck(ClientScope.Live)!.Result.Outcome);
            Assert.Single(services2.Clients.Store.Registry.Versions);
        });
    }

    [Fact]
    public async Task AskQuietlyOnlyMarksTheClientsPageAndMonitoringOffChecksNothing()
    {
        await Sta.Run(async () =>
        {
            var (services, main, page) = Create(ClientChangeHandling.AskQuietly);
            using var _ = services;
            await main.CheckInstalledClientsAsync();
            Assert.Null(main.ClientChange);
            await page.OnNavigatedToAsync();
            Assert.NotNull(page.Reminder);

            var (off, offMain, offPage) = Create(ClientChangeHandling.Ask);
            using var __ = off;
            off.Settings.MonitorInstalledLive = false;
            await offMain.CheckInstalledClientsAsync();
            Assert.Null(offMain.ClientChange);
            Assert.Null(off.Clients.LastCheck(ClientScope.Live));
            Assert.False(Directory.Exists(off.Clients.Root));
        });
    }

    [Fact]
    public async Task WhileTheLauncherRunsNothingIsAskedAndTheBaselineStays()
    {
        await Sta.Run(async () =>
        {
            var (services, main, page) = Create(ClientChangeHandling.Ask);
            using var _ = services;
            await main.CheckInstalledClientsAsync();
            main.LaterClientChangeCommand.Execute(null);
            string baselineFile = ClientBaselines.PathFor(services.Clients.Root!, ClientRegistration.InstalledLive);
            byte[] before = File.ReadAllBytes(baselineFile);

            services.Clients.LauncherActive = _ => true;
            await main.CheckInstalledClientsAsync();
            Assert.Null(main.ClientChange);
            Assert.Equal(before, File.ReadAllBytes(baselineFile));
            await page.OnNavigatedToAsync();
            Assert.Contains("launcher", page.Sources[0].Problem, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(page.Reminder);   // what the last accepted check found still holds
        });
    }

    /// <summary>
    /// TF_LAUNCH_CHECK_MEASURE=1: runs the launch check on the installed clients (read only): a first
    /// Full check, a second Full and a Quick check against its baseline (kept in a temporary folder),
    /// and the launcher probe. With TF_LAUNCH_CHECK_REPORT=&lt;file&gt; the figures are written. Only
    /// counts and outcomes are written, never a file name.
    /// </summary>
    [Fact]
    public void TheInstalledClientsAreCheckedReadOnly()
    {
        Assert.SkipUnless(TestData.Setting("LAUNCH_CHECK_MEASURE") == "1", "Set TF_LAUNCH_CHECK_MEASURE=1 to run the launch check on the installed clients (read only).");
        var lines = new List<string>();
        string root = TestData.NewTempDirectory("launchcheck");
        foreach (string package in Core.Assets.TibiaClientPaths.FindInstalledPackages())
        {
            string folder = Path.Combine(Core.Assets.TibiaClientPaths.PackagesRoot, package);
            bool launcher = LauncherActivity.IsActive(folder);
            var first = LaunchCheck.Run(folder, null, quick: false, new InventoryOptions(), ct: Ct);
            lines.Add($"{package}: launcher running: {launcher}; first check {first.Outcome}" + (first.Scan is { } s1 ? $", {s1.Files.Count:N0} files, {s1.HashedFiles:N0} read, {s1.Duration.TotalSeconds:N1} s" : "") +
                      $"; problems: {first.Problems.Count}");
            if (!first.Accepted) continue;
            var baseline = LaunchCheck.BaselineOf(first, "measure-" + package, ClientServers.Live, false, DateTime.UtcNow, null);
            ClientBaselines.Write(root, baseline);
            long baselineBytes = new FileInfo(ClientBaselines.PathFor(root, baseline.RegistrationId)).Length;
            var read = ClientBaselines.Read(root, baseline.RegistrationId)!;
            var full = LaunchCheck.Run(folder, read, quick: false, new InventoryOptions(), ct: Ct);
            var quick = LaunchCheck.Run(folder, read, quick: true, new InventoryOptions(), ct: Ct);
            lines.Add($"  baseline: {baselineBytes / 1024.0:N0} KB; Full again: {full.Outcome}, {full.Scan?.HashedFiles:N0} read, {full.Scan?.Duration.TotalSeconds:N1} s; " +
                      $"Quick: {quick.Outcome}, {quick.Scan?.HashedFiles:N0} read, {quick.Scan?.Duration.TotalSeconds:N1} s" +
                      (full.Outcome == LaunchCheckOutcome.Changed ? $"; changed: {full.ChangeSummary()}" : ""));
        }
        Assert.NotEmpty(lines);
        if (TestData.Setting("LAUNCH_CHECK_REPORT") is { } report) File.WriteAllLines(report, lines);
        TestContext.Current.SendDiagnosticMessage(string.Join(Environment.NewLine, lines));
    }
}
