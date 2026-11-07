using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.Tests;

/// <summary>
/// The Clients page (7.3, 8.0): load folders from anywhere (only read), find installations, archive,
/// use, compare, rename and delete revisions, the reminder, and the Client Installation Rebuilder for
/// tools that need a folder — through the view-models with scripted dialogs.
/// </summary>
public sealed class ClientsPageTests
{
    private static (AppServices Services, FakeDialogs Dialogs, MainViewModel Main, ClientsViewModel Page, string Dir) Create()
    {
        string dir = TestData.NewTempDirectory("clientspage");
        string packages = Path.Combine(dir, "packages");
        ClientIntegrationTests.RealisticPackage(Path.Combine(packages, "Tibia"), "15.33.9d148f");
        var settings = new AppSettings { LoadDataAutomatically = false };
        settings.Normalize();
        settings.SetChosenLocation(LocationIds.PackagesFolder, ClientScope.Live, packages);
        var services = new AppServices(new SettingsStore(Path.Combine(dir, "data", "settings.json")), settings) { Paths = TestData.PathsIn(Path.Combine(dir, "data")) };
        var dialogs = new FakeDialogs();
        services.Dialogs = dialogs;
        var main = new MainViewModel(services);
        var page = main.Pages.OfType<ClientsViewModel>().Single();
        page.FindRoots = () => [new InstallationRoot(packages, "the test's packages folder"), new InstallationRoot(Path.Combine(dir, "Program Files", "Tibia"), "Program Files")];
        return (services, dialogs, main, page, dir);
    }

    [Fact]
    public async Task ThePageListsTheInstalledClientAndRemindsUntilItIsArchived()
    {
        await Sta.Run(async () =>
        {
            var (services, dialogs, main, page, _) = Create();
            using var _ = services;
            Assert.Contains(main.Pages, p => p is ClientsViewModel);
            await page.OnNavigatedToAsync();
            Assert.Contains("not checked yet", page.Sources[0].Status);
            Assert.Null(page.Reminder);
            await main.CheckInstalledClientsAsync();   // the launch check (after the first page at every start)
            page.Refresh();
            var installed = Assert.Single(page.Sources);
            Assert.Equal(ClientRowKind.Installed, installed.Kind);
            Assert.True(installed.IsActive);
            Assert.Equal("15.33.9d148f", installed.Version);
            Assert.Contains("not archived yet", installed.Status);
            Assert.Contains("is not archived yet", page.Reminder);
            Assert.Empty(page.Revisions);
            Assert.Contains("Live · 15.33.9d148f (installed)", main.ClientInUse);

            Assert.NotNull(main.ClientChange);   // the notice above the status bar
            dialogs.ArchiveAnswer = 0;   // archive (personal data left out)
            await page.ArchiveInstalledNowCommand.ExecuteAsync(null);
            Assert.Null(main.ClientChange);      // withdrawn: the content is archived now
            var asked = Assert.Single(dialogs.ArchiveConfirmations);
            Assert.Equal(ManifestScopes.CompleteInstallation, asked.Preview.Scope);
            Assert.Equal("15.33.9d148f", asked.Version);
            Assert.Equal("Live server", asked.Server);
            Assert.False(asked.Preview.IncludesPersonalData);
            Assert.True(asked.Preview.HasExcludedPersonalData);
            Assert.Contains(asked.Preview.Categories, c => c.State == ArchiveCategoryState.Excluded && c.Name == "Options and hotkeys");
            Assert.Equal(asked.Preview.LogicalBytes, asked.Preview.NewBytes);   // nothing archived yet: every content is new
            Assert.Null(page.Reminder);
            var revision = Assert.Single(page.Revisions);
            Assert.Equal("Complete installation", revision.ScopeText);
            Assert.StartsWith("Client content complete — personal data left out", revision.Completeness, StringComparison.Ordinal);
            Assert.False(revision.IsInstallation);
            Assert.Equal("Restore folder…", revision.RebuildText);
            Assert.Contains("archived as", page.Sources[0].Status);
            Assert.Contains("1 revision:", page.StorageSummary);
            Assert.Contains("Left out: conf/", page.RevisionDetails);
            Assert.Contains(services.Clients.Root!, page.StoreLocation);
        });
    }

    [Fact]
    public async Task TheArchiveConfirmationIncludesPersonalDataOnlyWhenAskedAndCancelWritesNothing()
    {
        await Sta.Run(async () =>
        {
            var (services, dialogs, _, page, _) = Create();
            using var _ = services;
            await page.OnNavigatedToAsync();

            dialogs.ArchiveAnswer = -1;   // cancel: nothing is archived
            await page.ArchiveInstalledNowCommand.ExecuteAsync(null);
            Assert.Single(dialogs.ArchiveConfirmations);
            Assert.Empty(page.Revisions);
            Assert.False(page.IsBusy);

            dialogs.ArchiveAnswer = 1;   // tick "Include personal data", then Archive
            await page.ArchiveInstalledNowCommand.ExecuteAsync(null);
            var shown = dialogs.ArchiveConfirmations[^1].Preview;
            var archived = dialogs.ArchivedPreview!;
            Assert.False(shown.IncludesPersonalData);
            Assert.True(archived.IncludesPersonalData);
            Assert.False(archived.HasExcludedPersonalData);
            Assert.Equal(shown.Files + shown.ExcludedFiles, archived.Files);   // the figures follow the option
            Assert.Contains(archived.Categories, c => c.State == ArchiveCategoryState.Included && c.IsPersonal);
            var revision = Assert.Single(page.Revisions);
            Assert.Equal("Complete", revision.Completeness);
            Assert.False(services.Settings.ArchivePersonalData);   // a choice for this archive, not a change of the setting
        });
    }

    [Fact]
    public async Task TheReminderCanBeDismissedForThisVersionOrTurnedOff()
    {
        await Sta.Run(async () =>
        {
            var (services, _, main, page, _) = Create();
            using var _ = services;
            await main.CheckInstalledClientsAsync();
            await page.OnNavigatedToAsync();
            Assert.NotNull(page.Reminder);
            Assert.NotNull(main.ClientChange);   // Ask (the default): a notice in the main window too
            page.DismissReminderCommand.Execute(null);
            Assert.Null(page.Reminder);
            Assert.Contains(services.Clients.InstalledStatusOf(ClientScope.Live)!.Fingerprint!, services.Clients.Store.Registry.Registration(ClientRegistration.InstalledLive)!.DismissedFingerprints);
            services.Store_Clear_Dismissal();
            page.Refresh();
            Assert.NotNull(page.Reminder);
            page.DisableRemindersCommand.Execute(null);
            Assert.NotNull(page.Reminder);   // ask quietly: still shown on this page, no notice at start
            var saved = services.SettingsStore.Load().Settings;
            Assert.False(saved.ClientImportReminders);
            Assert.Equal(ClientChangeHandling.AskQuietly, saved.ClientChangeHandling);
        });
    }

    [Fact]
    public async Task AFolderFromAnywhereIsLoadedArchivedUsedAndForgottenWithoutTouchingIt()
    {
        await Sta.Run(async () =>
        {
            var (services, dialogs, main, page, dir) = Create();
            using var _ = services;
            string copy = ClientIntegrationTests.RealisticPackage(Path.Combine(dir, "copies", "old"), "15.20.111111", "old interface");
            var before = FakeClient.Snapshot(copy);
            await page.OnNavigatedToAsync();

            // Browse: picked inside its assets folder; the whole client folder, use it now, live server.
            dialogs.NextFile = Path.Combine(copy, "assets");
            dialogs.PromptAnswers.Enqueue("Old copy");
            dialogs.ChooseAnswer = 0;
            await page.BrowseFolderCommand.ExecuteAsync(null);
            Assert.Contains(dialogs.ChoiceQuestions, q => q.Contains("is the assets folder of the client folder", StringComparison.Ordinal));
            Assert.Contains(dialogs.ChoiceQuestions, q => q.Contains("Complete installation · version 15.20.111111", StringComparison.Ordinal));
            var folder = Assert.Single(page.Sources, r => r.Kind == ClientRowKind.Folder);
            Assert.Equal(("Old copy", copy, "Standalone folder"), (folder.Label, folder.Path, folder.TypeText));
            Assert.True(folder.IsActive);
            Assert.Equal("15.20.111111", services.Data.ClientVersion);
            Assert.Contains("(folder)", main.ClientInUse);
            Assert.Empty(page.Revisions);   // loading never archives

            // Archive it, then use the revision: every page reads it.
            page.SelectedSource = page.Sources.Single(r => r.Kind == ClientRowKind.Folder);
            dialogs.ArchiveAnswer = 0;
            await page.ArchiveSourceCommand.ExecuteAsync(null);
            var revision = Assert.Single(page.Revisions);
            Assert.Equal("15.20.111111", revision.Version);
            page.SelectedRevision = revision;
            page.UseRevisionCommand.Execute(null);
            Assert.Equal(revision.Key, services.Settings.ActiveClientLive);
            Assert.NotNull(services.Data.ImportedVersion);
            Assert.Contains("(archived)", main.ClientInUse);

            // Rename changes only the label.
            byte[] manifest = File.ReadAllBytes(services.Clients.Store.Registry.Version(revision.Key)!.ManifestPath(services.Clients.Root!));
            dialogs.PromptAnswers.Enqueue("Summer 2026");
            page.SelectedRevision = page.Revisions.Single();
            page.RenameRevisionCommand.Execute(null);
            Assert.Equal("Summer 2026", page.Revisions.Single().Label);
            Assert.Equal(manifest, File.ReadAllBytes(services.Clients.Store.Registry.Version(revision.Key)!.ManifestPath(services.Clients.Root!)));

            // The revision in use cannot be deleted.
            page.SelectedRevision = page.Revisions.Single();
            page.DeleteRevisionCommand.Execute(null);
            Assert.Contains(dialogs.Messages, m => m.Message.Contains("in use", StringComparison.Ordinal));
            Assert.Single(page.Revisions);

            // Check an unchanged folder: nothing new.
            page.SelectedSource = page.Sources.Single(r => r.Kind == ClientRowKind.Folder);
            await page.RescanCommand.ExecuteAsync(null);
            Assert.Contains("Unchanged", services.Status.Message);

            // Changed: Check for changes offers to archive it, and archives what it just read (the folder is not read twice).
            File.AppendAllText(Path.Combine(copy, "notes.txt"), "a change");
            dialogs.ConfirmAnswer = true;
            dialogs.ArchiveAnswer = 0;
            int reads = services.Jobs.History.Count(h => h.Title == "Reading the client folder");
            await page.RescanCommand.ExecuteAsync(null);
            Assert.Equal(reads + 1, services.Jobs.History.Count(h => h.Title == "Reading the client folder"));
            Assert.Contains(dialogs.ConfirmQuestions, q => q.Contains("Archive it as a new revision?", StringComparison.Ordinal));
            Assert.Equal(2, page.Revisions.Count);
            File.Delete(Path.Combine(copy, "notes.txt"));
            page.SelectedRevision = page.Revisions.First(r => !r.IsActive);
            dialogs.ChooseAnswer = 0;
            page.DeleteRevisionCommand.Execute(null);
            Assert.Single(page.Revisions);

            // Back to the installed client, then delete the revision and forget the folder.
            page.SelectedSource = page.Sources.Single(r => r.Kind == ClientRowKind.Installed);
            page.UseSourceCommand.Execute(null);
            Assert.Null(services.Data.ImportedVersion);
            page.SelectedRevision = page.Revisions.Single();
            dialogs.ChooseAnswer = 0;   // delete and free its space
            page.DeleteRevisionCommand.Execute(null);
            Assert.Empty(page.Revisions);
            page.SelectedSource = page.Sources.Single(r => r.Kind == ClientRowKind.Folder);
            page.RemoveSourceCommand.Execute(null);
            Assert.DoesNotContain(page.Sources, r => r.Kind == ClientRowKind.Folder);

            Assert.Equal(before, FakeClient.Snapshot(copy));
        });
    }

    [Fact]
    public async Task FindInstallationValidatesWhatItFindsAndUsesTheChosenOne()
    {
        await Sta.Run(async () =>
        {
            var (services, dialogs, _, page, dir) = Create();
            using var _ = services;
            string other = ClientIntegrationTests.RealisticPackage(Path.Combine(dir, "Program Files", "Tibia", "packages", "Tibia"), "15.10.222222");
            Directory.CreateDirectory(Path.Combine(dir, "Program Files", "Tibia", "packages", "Tibia notes"));   // named Tibia, but not a client
            await page.OnNavigatedToAsync();

            dialogs.ChoiceAnswers.Enqueue(null);   // look, then cancel
            await page.FindInstallationCommand.ExecuteAsync(null);
            var list = Assert.Single(dialogs.ChoiceLists);
            Assert.Equal(2, list.Items.Count);   // the installed package and the other installation; never the notes folder
            Assert.DoesNotContain(list.Items, i => i.Title.EndsWith("Tibia notes", StringComparison.Ordinal));

            int index = list.Items.ToList().FindIndex(i => i.Title == other);
            dialogs.ChoiceAnswers.Enqueue(new ChoiceResult(index, false, null));
            dialogs.ChooseAnswer = 0;
            dialogs.PromptAnswers.Enqueue("Other install");
            await page.FindInstallationCommand.ExecuteAsync(null);
            var added = Assert.Single(page.Sources, r => r.Kind == ClientRowKind.Folder);
            Assert.Equal("Installed client", added.TypeText);   // a package of a packages folder
            Assert.True(added.IsActive);
            Assert.Equal("15.10.222222", services.Data.ClientVersion);
        });
    }

    [Fact]
    public async Task ToolsThatNeedAFolderOfferTheRebuilderWhichRestoresAVerifiedCopy()
    {
        await Sta.Run(async () =>
        {
            var (services, dialogs, main, page, dir) = Create();
            using var _ = services;
            await page.OnNavigatedToAsync();
            dialogs.ArchiveAnswer = 0;
            await page.ArchiveInstalledNowCommand.ExecuteAsync(null);
            page.SelectedRevision = page.Revisions.Single();
            page.UseRevisionCommand.Execute(null);

            var editor = main.Pages.OfType<AssetsEditorViewModel>().Single();
            dialogs.ConfirmAnswer = true;
            await editor.OpenClientCommand.ExecuteAsync(null);
            Assert.Contains(dialogs.ConfirmQuestions, q => q.Contains("Client Installation Rebuilder", StringComparison.Ordinal));
            var rebuilder = Assert.IsType<RebuilderViewModel>(main.SelectedPage);
            await rebuilder.OnNavigatedToAsync();
            Assert.Equal(page.Revisions.Single().Key, rebuilder.Selected!.Key);   // the revision in use
            Assert.Equal("Restore folder", rebuilder.Selected.Kind);   // personal data was left out: not a complete installation

            // A folder with files gets a new subfolder: nothing is ever replaced.
            string busy = Path.Combine(dir, "output");
            Directory.CreateDirectory(busy);
            File.WriteAllText(Path.Combine(busy, "keep.txt"), "mine");
            dialogs.NextFile = busy;
            rebuilder.ChooseOutputCommand.Execute(null);
            Assert.StartsWith(Path.Combine(busy, ""), rebuilder.OutputFolder, StringComparison.OrdinalIgnoreCase);
            string target = rebuilder.OutputFolder;
            await rebuilder.RebuildCommand.ExecuteAsync(null);
            Assert.Contains("Runnable: not checked", rebuilder.Report);
            Assert.Contains("Content complete (every recorded file restored with its recorded SHA-256): yes", rebuilder.Report);
            Assert.True(File.Exists(Path.Combine(target, "assets", "catalog-content.json")));
            Assert.True(File.Exists(Path.Combine(target, "bin", "graphics_resources.rcc")));
            Assert.False(File.Exists(Path.Combine(target, "conf", "clientoptions.json")));   // left out on purpose
            Assert.Equal("mine", File.ReadAllText(Path.Combine(busy, "keep.txt")));
            Assert.DoesNotContain(Directory.EnumerateDirectories(busy), d => d.Contains(".tf-partial-", StringComparison.Ordinal));

            // "Add it" registered the result as a rebuilt folder.
            page.Refresh();
            var rebuilt = Assert.Single(page.Sources, r => r.Kind == ClientRowKind.Folder);
            Assert.Equal("Rebuilt installation", rebuilt.TypeText);
        });
    }
    /// <summary>
    /// TF_OWNER_STORE_COPY=&lt;a copy of a 7.3 TibiaFoundry-Data folder&gt;: opens a real 7.3 store with 8.0
    /// (only ever a copy: opening refreshes the registry) and checks the labels of its revisions. With
    /// TF_OWNER_STORE_REPORT=&lt;file&gt; the labels are written. The manifests are never rewritten.
    /// </summary>
    [Fact]
    public async Task ARealStoreOf73OpensWithHonestLabels()
    {
        string? data = TestData.Setting("OWNER_STORE_COPY");
        Assert.SkipUnless(data != null && Directory.Exists(data), "Set TF_OWNER_STORE_COPY to a copy of a 7.3 TibiaFoundry-Data folder.");
        string root = Path.Combine(data!, ClientStore.FolderName);
        var manifests = Directory.EnumerateFiles(root, ClientManifest.FileName, SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);
        await Sta.Run(async () =>
        {
            var settings = new AppSettings { LoadDataAutomatically = false };
            settings.Normalize();
            settings.SetChosenLocation(LocationIds.PackagesFolder, ClientScope.Live, Path.Combine(data!, "no-packages-here"));
            var services = new AppServices(new SettingsStore(Path.Combine(data!, "settings-8.0-test.json")), settings) { Paths = TestData.PathsIn(data!) };
            using var _ = services;
            services.Dialogs = new FakeDialogs();
            var main = new MainViewModel(services);
            var page = main.Pages.OfType<ClientsViewModel>().Single();
            await page.OnNavigatedToAsync();
            Assert.Equal(root, services.Clients.Root);
            Assert.NotEmpty(page.Revisions);
            var lines = new List<string>();
            foreach (var row in page.Revisions)
            {
                lines.Add($"{row.Key}: {row.ScopeText} | {row.Completeness} | {row.StorageText} | version {row.VersionText} | {row.Status} | {row.Files} files, {row.Size} | from {row.Source} | {row.RebuildText}");
                Assert.Equal("Tibia Foundry files only (7.3)", row.Completeness);
                Assert.False(row.IsInstallation);   // never called a complete installation
                Assert.Equal("Restore folder…", row.RebuildText);
                Assert.True(row.IsUsable, row.Problem);
            }
            var verify = services.Clients.Store.Verify(null, null, TestContext.Current.CancellationToken);
            lines.Add($"Verify: {verify.Versions} revision(s), {verify.Objects:N0} files, problems: {verify.Problems.Count}");
            Assert.Empty(verify.Problems);
            lines.Add(page.StorageSummary);
            if (TestData.Setting("OWNER_STORE_REPORT") is { } report) File.WriteAllLines(report, lines);
            TestContext.Current.SendDiagnosticMessage(string.Join(Environment.NewLine, lines));
        });
        foreach (var (file, bytes) in manifests) Assert.Equal(bytes, File.ReadAllBytes(file));
    }

}

internal static class ClientsPageTestExtensions
{
    /// <summary>Forgets a dismissed reminder (as if the launcher installed another version).</summary>
    public static void Store_Clear_Dismissal(this AppServices services)
    {
        services.Clients.Store.Registry.Registration(ClientRegistration.InstalledLive)!.DismissedFingerprints.Clear();
        services.Clients.Store.RecordScan(ClientRegistration.InstalledLive, new SourceSnapshot(null, null, 0, 0, default));   // saves the registry
    }
}
