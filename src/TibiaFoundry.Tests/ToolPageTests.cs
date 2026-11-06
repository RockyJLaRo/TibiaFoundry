using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Rcc;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.Tests;

/// <summary>
/// The 5.0 pages through their view-models: Scripts (preview, confirmation, apply, history), RCC
/// Editor (open, edit, validated save with backup), the operation limit, completion notices and the
/// categorized Settings page (immediate effects, per-category reset, export and import).
/// Everything runs in temporary folders; nothing is written to the user's data folders.
/// </summary>
public sealed class ToolPageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (AppServices Services, FakeDialogs Dialogs, string Dir) CreateServices()
    {
        string dir = TestData.NewTempDirectory("tools");
        var settings = new AppSettings { OutputFolder = Path.Combine(dir, "out"), LoadDataAutomatically = false, CreatorName = "ToolTest" };
        settings.Normalize();
        var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var dialogs = new FakeDialogs();
        services.Dialogs = dialogs;
        return (services, dialogs, dir);
    }

    [Fact]
    public void ServicesOfTestsStayInTheTestFolder()
    {
        var (services, _, dir) = CreateServices();
        using var _ = services;
        Assert.StartsWith(dir, services.BackupFolder, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(dir, services.TempFolder, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(dir, services.ScriptHistory.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(dir, services.RenameRecordFolder, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OperationGateQueuesBeyondTheLimitAndHonorsCancellation()
    {
        int limit = 1;
        var gate = new OperationGate(() => limit);
        var first = await gate.EnterAsync(Ct);
        Assert.True(gate.IsFull);

        var second = gate.EnterAsync(Ct);
        await Task.Delay(50, Ct);
        Assert.False(second.IsCompleted);                                                  // waits for a free slot
        Assert.Equal(1, gate.Waiting);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var third = gate.EnterAsync(cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
        Assert.Equal(1, gate.Waiting);                                                    // the cancelled one left the queue

        first.Dispose();
        using (await second.WaitAsync(TimeSpan.FromSeconds(10), Ct))
        {
            Assert.Equal(1, gate.Running);
            Assert.Equal(0, gate.Waiting);
            limit = 2;                                                                    // the setting is read live
            Assert.False(gate.IsFull);
        }
        Assert.Equal(0, gate.Running);
    }

    [Fact]
    public void CompletionNoticesRespectTheThreshold()
    {
        var settings = new AppSettings();
        settings.Notifications.NoticeAfterSeconds = 10;
        settings.Notifications.PlaySoundWhenDone = false;
        settings.Notifications.FlashTaskbarWhenDone = true;                                // no window in tests: nothing flashes
        var notices = new NotificationService(settings);

        notices.OperationFinished("Quick job", TimeSpan.FromSeconds(3), success: true);
        Assert.Null(notices.LastNotice);
        notices.OperationFinished("Long job", TimeSpan.FromSeconds(30), success: false);
        Assert.Equal("Long job: failed after 30 s", notices.LastNotice);
    }

    [Fact(Timeout = 120_000)]
    public async Task ScriptsPreviewFirstApplyOnlyAfterConfirmationAndRecordHistory()
    {
        await Sta.Run(async () =>
        {
            var (services, dialogs, dir) = CreateServices();
            using var _ = services;
            string data = Path.Combine(dir, "data");
            Directory.CreateDirectory(Path.Combine(data, "empty folder"));
            File.WriteAllBytes(Path.Combine(data, "empty.txt"), []);
            File.WriteAllText(Path.Combine(data, "kept.txt"), "content");

            var vm = new ScriptsViewModel(services);
            await vm.OnNavigatedToAsync();
            Assert.Equal(12, vm.All.Count);

            // Search and categories filter the list.
            vm.Search = "checksum";
            Assert.Contains(vm.Visible, i => i.Script.Id == "checksums");
            Assert.DoesNotContain(vm.Visible, i => i.Script.Id == "empty-finder");
            vm.Search = "";
            vm.Category = "File management";
            Assert.All(vm.Visible, i => Assert.Equal("File management", i.Category));
            vm.Category = vm.Categories[0];

            vm.Selected = vm.All.Single(i => i.Script.Id == "empty-finder");
            vm.Inputs.Single(i => i.Key == "folder").Value = data;
            vm.Inputs.Single(i => i.Key == "action").Value = "move";
            vm.ParametersChanged();
            Assert.Equal("Preview changes", vm.RunLabel);

            // The run only previews: nothing changes yet.
            await vm.RunCommand.ExecuteAsync(null);
            Assert.True(vm.HasPlan);
            Assert.Equal(2, vm.Plan.Count);
            Assert.True(File.Exists(Path.Combine(data, "empty.txt")));
            Assert.True(Directory.Exists(Path.Combine(data, "empty folder")));

            // Declining the confirmation changes nothing.
            dialogs.ConfirmAnswer = false;
            await vm.ApplyCommand.ExecuteAsync(null);
            Assert.True(File.Exists(Path.Combine(data, "empty.txt")));

            // Confirmed: the empty entries are moved into a backup set, the other file stays.
            dialogs.ConfirmAnswer = true;
            await vm.ApplyCommand.ExecuteAsync(null);
            Assert.False(File.Exists(Path.Combine(data, "empty.txt")));
            Assert.False(Directory.Exists(Path.Combine(data, "empty folder")));
            Assert.True(File.Exists(Path.Combine(data, "kept.txt")));
            var set = Assert.Single(services.Backups.List());
            Assert.Equal(BackupKind.Removed, set.Manifest.Kind);
            Assert.StartsWith(dir, set.Folder, StringComparison.OrdinalIgnoreCase);

            // History: the preview and the applied run, without file contents.
            Assert.Equal(["Apply", "Preview"], vm.History.Select(h => h.Kind));
            Assert.True(vm.History[0].ChangesApplied);
            string historyJson = File.ReadAllText(services.ScriptHistory.FilePath);
            Assert.DoesNotContain("content", historyJson);

            // Clearing needs a confirmation.
            dialogs.ConfirmAnswer = false;
            vm.ClearHistoryCommand.Execute(null);
            Assert.Equal(2, vm.History.Count);
            dialogs.ConfirmAnswer = true;
            vm.ClearHistoryCommand.Execute(null);
            Assert.Empty(vm.History);

            // Input values are remembered per script.
            Assert.Equal(data, services.Settings.ToolState["Scripts.empty-finder.folder"]);
            Assert.Empty(dialogs.Errors);
        });
    }

    [Fact(Timeout = 120_000)]
    public async Task ScriptsDoNotRunWithInvalidInputs()
    {
        await Sta.Run(async () =>
        {
            var (services, _, dir) = CreateServices();
            using var _ = services;
            var vm = new ScriptsViewModel(services);
            vm.Selected = vm.All.Single(i => i.Script.Id == "empty-finder");
            vm.Inputs.Single(i => i.Key == "folder").Value = Path.Combine(dir, "does not exist");
            await vm.RunCommand.ExecuteAsync(null);
            Assert.NotEqual("", vm.Problems);
            Assert.False(vm.HasResult);
            Assert.Empty(vm.History);
        });
    }

    [Fact(Timeout = 120_000)]
    public async Task RccEditorEditsAreSavedValidatedWithABackup()
    {
        await Sta.Run(async () =>
        {
            var (services, dialogs, dir) = CreateServices();
            using var _ = services;
            var archive = RccArchive.Create(3);
            archive.Add("/images/icon.png", ImageIO.EncodePng(new RgbaImage(4, 4)), RccCompressionChoice.Never);
            archive.Add("/text/readme.txt", "first"u8.ToArray(), RccCompressionChoice.Never);
            string file = Path.Combine(dir, "resources.rcc");
            File.WriteAllBytes(file, archive.Build());
            byte[] original = File.ReadAllBytes(file);

            var vm = new RccEditorViewModel(services);
            await vm.OpenFileAsync(file);
            Assert.True(vm.HasFile);
            Assert.True(vm.ValidationOk);
            Assert.Equal(2, vm.Entries.Count);
            Assert.Null(vm.UnsavedChanges);

            // Replace a resource with a file, then rename another.
            string replacement = Path.Combine(dir, "readme.txt");
            File.WriteAllText(replacement, "second version");
            vm.ReplaceWith(vm.Entries.Single(e => e.Path == "/text/readme.txt"), replacement);
            vm.Selected = vm.Entries.Single(e => e.Path == "/images/icon.png");
            dialogs.PromptAnswers.Enqueue("/images/renamed.png");
            vm.RenameCommand.Execute(null);
            Assert.NotNull(vm.UnsavedChanges);
            Assert.Equal(original, File.ReadAllBytes(file));                                // nothing written before saving

            await vm.SaveCommand.ExecuteAsync(null);
            Assert.Empty(dialogs.Errors);
            Assert.Null(vm.UnsavedChanges);
            Assert.True(vm.ValidationOk);

            var saved = RccArchive.Load(file);
            Assert.True(RccValidator.Validate(File.ReadAllBytes(file)).IsValid);
            Assert.Equal("second version", System.Text.Encoding.UTF8.GetString(saved.Find("/text/readme.txt")!.Stored));
            Assert.NotNull(saved.Find("/images/renamed.png"));
            Assert.Null(saved.Find("/images/icon.png"));

            // The previous version was backed up, byte for byte.
            var set = Assert.Single(services.Backups.List());
            var entry = Assert.Single(set.Manifest.Entries);
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(set.Folder, entry.Stored.Replace('/', Path.DirectorySeparatorChar))));
        });
    }

    [Fact(Timeout = 120_000)]
    public async Task SettingsCategoriesApplyImmediatelyResetSeparatelyAndRoundTrip()
    {
        await Sta.Run(() =>
        {
            var (services, dialogs, dir) = CreateServices();
            using var _ = services;
            var main = new MainViewModel(services);
            var vm = main.Pages.OfType<SettingsViewModel>().Single();
            Assert.Equal(["General", "Appearance & Themes", "Tibia client", "Files and folders", "File safety", "Performance", "Attribution", "Notifications and logging", "Tools", "Scripts", "Diagnostics", "Advanced"], vm.Categories);

            // Immediate effects, saved at once.
            vm.MaxConcurrentOperations = 3;
            Assert.Equal(3, services.Operations.Limit);
            vm.WhenOutputExists = ExistingFilePolicy.KeepBoth;
            Assert.Equal(ExistingFilePolicy.KeepBoth, services.Settings.FileSafety.WhenExists);
            Assert.Equal(ExistingFilePolicy.KeepBoth, new SettingsStore(Path.Combine(dir, "settings.json")).Load().Settings.Safety.WhenOutputExists);

            // Turning backups off needs a confirmation.
            dialogs.ConfirmAnswer = false;
            vm.BackupBeforeChanges = false;
            Assert.True(services.Settings.Safety.BackupBeforeChanges);

            // A tool's own output folder wins over the default; clearing it restores the default.
            string own = Path.Combine(dir, "rcc-output");
            dialogs.NextFile = own;
            vm.ChangeToolFolder(ToolKeys.RccEditor, input: false);
            Assert.Equal(own, services.Settings.OutputFolderFor(ToolKeys.RccEditor));
            vm.UseDefaultOutputFolder(ToolKeys.OutputKindOf(ToolKeys.RccEditor));
            Assert.NotEqual(own, services.Settings.OutputFolderFor(ToolKeys.RccEditor));

            // Export, change, import (with confirmation).
            string exported = Path.Combine(dir, "exported-settings.json");
            dialogs.NextFile = exported;
            vm.ExportSettingsCommand.Execute(null);
            Assert.True(File.Exists(exported));
            vm.ResetSafetyCommand.Execute(null);
            vm.ResetPerformanceCommand.Execute(null);
            Assert.Equal(new SafetySettings().WhenOutputExists, services.Settings.Safety.WhenOutputExists);
            Assert.Equal(new PerformanceSettings().MaxConcurrentOperations, services.Settings.Performance.MaxConcurrentOperations);
            Assert.Equal("ToolTest", services.Settings.CreatorName);                         // other categories untouched

            dialogs.NextFile = exported;
            dialogs.ConfirmAnswer = false;
            vm.ImportSettingsCommand.Execute(null);
            Assert.Equal(new SafetySettings().WhenOutputExists, services.Settings.Safety.WhenOutputExists);
            dialogs.ConfirmAnswer = true;
            vm.ImportSettingsCommand.Execute(null);
            Assert.Equal(ExistingFilePolicy.KeepBoth, services.Settings.Safety.WhenOutputExists);
            Assert.Equal(3, services.Settings.Performance.MaxConcurrentOperations);
            Assert.Empty(dialogs.Errors);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void ImportAndResetNeverMoveThisPcsClientArchive()
    {
        var mine = new AppSettings { ClientStoreFolder = @"D:\Archive", ActiveClientLive = "Live_Server/Install_15.33", ClientSetupCompleted = true, CreatorName = "Me" };
        mine.Normalize();
        var other = new AppSettings { ClientStoreFolder = @"E:\Elsewhere", ActiveClientLive = "Live_Server/Install_14.00", ClientSetupCompleted = false, CreatorName = "Other" };
        other.Normalize();

        SettingsViewModel.CopyAll(other, mine, keepUserContent: false);   // import from another PC
        Assert.Equal((@"D:\Archive", "Live_Server/Install_15.33", true), (mine.ClientStoreFolder, mine.ActiveClientLive, mine.ClientSetupCompleted));
        Assert.Equal("Other", mine.CreatorName);                          // everything else is imported

        var fresh = new AppSettings();
        fresh.Normalize();
        SettingsViewModel.CopyAll(fresh, mine, keepUserContent: true);    // reset to defaults
        Assert.Equal(@"D:\Archive", mine.ClientStoreFolder);
        Assert.True(mine.ClientSetupCompleted);
    }

    [Fact(Timeout = 120_000)]
    public async Task ImportingADamagedSettingsFileChangesNothing()
    {
        await Sta.Run(() =>
        {
            var (services, dialogs, dir) = CreateServices();
            using var _ = services;
            var vm = new MainViewModel(services).Pages.OfType<SettingsViewModel>().Single();
            vm.WhenOutputExists = ExistingFilePolicy.Skip;
            string damaged = Path.Combine(dir, "damaged.json");
            File.WriteAllText(damaged, "{ this is not json");
            dialogs.NextFile = damaged;
            dialogs.ConfirmAnswer = true;
            vm.ImportSettingsCommand.Execute(null);
            Assert.Equal(ExistingFilePolicy.Skip, services.Settings.Safety.WhenOutputExists);
            Assert.NotEmpty(dialogs.Errors);                                                // explained to the user
            return Task.CompletedTask;
        });
    }
}
