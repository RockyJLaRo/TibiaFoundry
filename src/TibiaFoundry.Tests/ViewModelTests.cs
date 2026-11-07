using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Sounds;

namespace TibiaFoundry.Tests;

/// <summary>Records dialogs instead of showing them, and answers prompts with canned values.</summary>
internal sealed class FakeDialogs : IDialogService
{
    public List<(MessageKind Kind, string Message)> Messages { get; } = new();
    public List<(string Message, Exception? Exception)> Errors { get; } = new();
    public Queue<string?> PromptAnswers { get; } = new();
    public string? NextFile { get; set; }
    public bool ConfirmAnswer { get; set; }

    public string? OpenFile(string title, string filter, string? initialDirectory = null) => NextFile;
    public string[]? OpenFiles(string title, string filter, string? initialDirectory = null) => NextFile == null ? null : [NextFile];
    public string? SaveFile(string title, string filter, string defaultFileName, string? initialDirectory = null) => NextFile;
    public string? PickFolder(string title, string? initialDirectory = null) => NextFile;
    public void Show(string message, MessageKind kind = MessageKind.Info, string? title = null) => Messages.Add((kind, message));
    public void ShowError(string message, Exception? exception = null, bool log = true) => Errors.Add((message, exception));
    /// <summary>Every confirmation asked (the answer is <see cref="ConfirmAnswer"/>).</summary>
    public List<string> ConfirmQuestions { get; } = new();

    public bool Confirm(string message, string title, string? confirmText = null, string? cancelText = null)
    {
        ConfirmQuestions.Add(message);
        return ConfirmAnswer;
    }

    /// <summary>Answer to two-choice questions (0, 1 or -1 = cancel); every question asked is recorded.</summary>
    public int ChooseAnswer { get; set; } = -1;
    /// <summary>Answers for the next two-choice questions, in order; when none is left, <see cref="ChooseAnswer"/>.</summary>
    public Queue<int> ChooseAnswers { get; } = new();
    public List<string> ChoiceQuestions { get; } = new();

    public int Choose(string message, string title, string first, string second, string? cancelText = null)
    {
        ChoiceQuestions.Add(message);
        return ChooseAnswers.Count > 0 ? ChooseAnswers.Dequeue() : ChooseAnswer;
    }
    public string? Prompt(string title, string message, string initialText = "", bool multiline = false) => PromptAnswers.Count > 0 ? PromptAnswers.Dequeue() : null;

    /// <summary>Answers to list choices, in order (none left = cancel); every list asked is recorded with its items.</summary>
    public Queue<ChoiceResult?> ChoiceAnswers { get; } = new();
    public List<(string Title, string Message, IReadOnlyList<ChoiceItem> Items, ChoiceOptions Options)> ChoiceLists { get; } = new();

    public ChoiceResult? ChooseFrom(string title, string message, IReadOnlyList<ChoiceItem> items, ChoiceOptions? options = null)
    {
        ChoiceLists.Add((title, message, items, options ?? new ChoiceOptions()));
        return ChoiceAnswers.Count > 0 ? ChoiceAnswers.Dequeue() : null;
    }

    /// <summary>Every structured message shown (8.6), and the answers to give, in order (none left = <see cref="ReportAnswer.Primary"/> for a message without a cancel button, else Cancel).</summary>
    public List<DialogReport> Reports { get; } = new();
    public Queue<ReportAnswer> ReportAnswers { get; } = new();

    public ReportAnswer ShowReport(DialogReport report)
    {
        Reports.Add(report);
        // Structured messages are messages too: tests that read Messages see their text.
        if (report.CancelText == null && report.SecondaryText == null) Messages.Add((report.Kind, report.ToPlainText()));
        if (ReportAnswers.Count > 0) return ReportAnswers.Dequeue();
        if (report.CancelText == null && report.SecondaryText == null) return ReportAnswer.Primary;
        // A structured question answers like the plain one it replaced: two answers as Confirm, three as Choose.
        if (report.SecondaryText == null)
        {
            ConfirmQuestions.Add(report.ToPlainText());
            return ConfirmAnswer ? ReportAnswer.Primary : ReportAnswer.Cancel;
        }
        ChoiceQuestions.Add(report.ToPlainText());
        return (ChooseAnswers.Count > 0 ? ChooseAnswers.Dequeue() : ChooseAnswer) switch { 0 => ReportAnswer.Primary, 1 => ReportAnswer.Secondary, _ => ReportAnswer.Cancel };
    }

    /// <summary>Answer to the archive confirmation: 0 archives what is shown, 1 includes personal data first (read again), -1 cancels; every confirmation is recorded.</summary>
    public int ArchiveAnswer { get; set; } = -1;
    public List<ArchiveConfirmation> ArchiveConfirmations { get; } = new();
    /// <summary>The preview the last confirmation archived (after reading the folder again for answer 1).</summary>
    public Core.Clients.ArchivePreview? ArchivedPreview { get; private set; }

    public Core.Clients.ArchivePreview? ConfirmArchive(ArchiveConfirmation request)
    {
        ArchiveConfirmations.Add(request);
        if (ArchiveAnswer < 0) return null;
        var preview = request.Preview;
        if (ArchiveAnswer == 1 && request.Recalculate != null && !preview.IncludesPersonalData)
            preview = request.Recalculate(true, new Progress<OperationProgress>(), CancellationToken.None).GetAwaiter().GetResult();
        return ArchivedPreview = preview;
    }

    /// <summary>Answer to the attribution question (null = the user cancels); every question asked is recorded.</summary>
    public AttributionChoice? AttributionAnswer { get; set; } = AttributionChoice.KeepOriginalCreators;
    public List<AttributionReview> AttributionQuestions { get; } = new();

    public AttributionChoice? ChooseAttribution(AttributionReview review, string operation)
    {
        AttributionQuestions.Add(review);
        return AttributionAnswer;
    }

    /// <summary>Answer to "the file already exists"; every question asked is recorded.</summary>
    public Core.FileOps.ConflictAnswer ConflictAnswer { get; set; } = new(Core.FileOps.ConflictChoice.Skip, false);
    public List<string> ConflictQuestions { get; } = new();

    public Core.FileOps.ConflictAnswer AskConflict(string path, string operation, bool backupsOn)
    {
        ConflictQuestions.Add(path);
        return ConflictAnswer;
    }
}

/// <summary>
/// View-model behavior without a window: long-running operation handling, the sprite extractor
/// queue, file comparison and the sounds list (the latter against the installed client).
/// </summary>
public sealed class ViewModelTests
{
    private static (AppServices Services, FakeDialogs Dialogs) CreateServices(AppSettings? settings = null)
    {
        string dir = TestData.NewTempDirectory();
        settings ??= new AppSettings { OutputFolder = dir, LoadDataAutomatically = false };
        settings.Normalize();
        var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var dialogs = new FakeDialogs();
        services.Dialogs = dialogs;
        return (services, dialogs);
    }

    private static readonly Core.Tools.ToolManifest TestManifest = new()
    {
        Id = "test-page", Name = "Test", Description = "A page for tests", Type = Core.Tools.ToolType.Utility, Version = "1.0",
        Capabilities = Core.Tools.ToolCapabilities.Progress | Core.Tools.ToolCapabilities.Cancellation,
        License = new("Test", "Test only"),
    };

    private sealed class TestPage(AppServices services) : PageViewModel(services, TestManifest)
    {
        public override string Title => "Test";
        public override string Glyph => "";
        public Task<bool> Run(Func<IProgress<OperationProgress>, CancellationToken, Task> work, string? success = null) => RunAsync("Working", work, success);
        public void Report(BatchResult result) => ReportBatch(result, "files", "output folder", "Batch");
    }

    [Fact]
    public void BatchResultsAreReportedHonestly()
    {
        var (services, dialogs) = CreateServices();
        using var _ = services;
        var page = new TestPage(services);

        page.Report(new BatchResult { Succeeded = 5 });
        Assert.Equal(StatusKind.Success, services.Status.Kind);
        Assert.Empty(dialogs.Messages);

        var partial = new BatchResult { Succeeded = 3 };
        partial.Failures.Add("x.png: disk full");
        page.Report(partial);
        Assert.Equal(StatusKind.Warning, services.Status.Kind);         // never a green "success" when something failed
        Assert.Contains(dialogs.Messages, m => m.Kind == MessageKind.Warning && m.Message.Contains("x.png: disk full"));

        var none = new BatchResult();
        none.Failures.Add("y.png: access denied");
        page.Report(none);
        Assert.Equal(StatusKind.Error, services.Status.Kind);
        Assert.Contains("nothing was written", services.Status.Message);

        var noted = new BatchResult { Succeeded = 2 };
        noted.Notes.Add("Existing files were replaced.");
        page.Report(noted);
        Assert.Equal(StatusKind.Warning, services.Status.Kind);
        Assert.Contains("replaced", services.Status.FullText);
    }

    [Fact]
    public async Task DataFileProblemsAreExplainedInPlainWords()
    {
        var (services, dialogs) = CreateServices();
        using var _ = services;
        var page = new TestPage(services);
        bool ok = await page.Run((_, _) => throw new TibiaFoundry.Core.Compatibility.DataFileException(
            TibiaFoundry.Core.Compatibility.DataProblemKind.Undecodable, "appearances-x.dat", "appearances-x.dat could not be decoded."));
        Assert.False(ok);
        Assert.Equal(StatusKind.Error, services.Status.Kind);
        Assert.Contains("is damaged or in a format this version does not recognize", services.Status.Message);
        var message = Assert.Single(dialogs.Messages);
        Assert.Contains("Compatibility page", message.Message);   // advice, not a stack trace
        Assert.Empty(dialogs.Errors);
    }

    [Fact]
    public async Task RunAsyncReportsSuccess()
    {
        var (services, dialogs) = CreateServices();
        using var _ = services;
        var page = new TestPage(services);
        bool ok = await page.Run(async (p, ct) => { p.Report(new OperationProgress(1, 2, "half")); await Task.Delay(10, ct); }, "All done.");
        Assert.True(ok);
        Assert.False(page.IsBusy);
        Assert.False(services.Status.IsBusy);
        Assert.Equal("All done.", services.Status.Message);
        Assert.Equal(StatusKind.Success, services.Status.Kind);
        Assert.Empty(dialogs.Errors);
    }

    [Fact]
    public async Task RunAsyncReportsFailuresWithoutThrowing()
    {
        var (services, dialogs) = CreateServices();
        using var _ = services;
        var page = new TestPage(services);
        bool ok = await page.Run((_, _) => throw new InvalidDataException("broken file"));
        Assert.False(ok);
        Assert.False(page.IsBusy);
        Assert.Equal(StatusKind.Error, services.Status.Kind);
        Assert.Contains("broken file", services.Status.Message);
        var error = Assert.Single(dialogs.Errors);
        Assert.IsType<InvalidDataException>(error.Exception);
    }

    [Fact]
    public async Task RunAsyncCanBeCancelledAndIsNotReentrant()
    {
        var (services, dialogs) = CreateServices();
        using var _ = services;
        var page = new TestPage(services);
        var started = new TaskCompletionSource();
        var run = page.Run(async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); });
        await started.Task;

        Assert.True(page.IsBusy);
        Assert.False(await page.Run((_, _) => Task.CompletedTask), "a second operation must not start while one is running");

        page.CancelOperation();
        Assert.False(await run);
        Assert.False(page.IsBusy);
        Assert.Equal(StatusKind.Warning, services.Status.Kind);
        Assert.Contains("cancelled", services.Status.Message);
        Assert.Empty(dialogs.Errors);
    }

    [Fact]
    public void SettingsAreNotWrittenWhenSavingIsOffForTheSession()
    {
        var (services, _) = CreateServices();
        using var _ = services;
        services.PersistSettings = false;
        services.Settings.LastPage = "Temporary";
        services.SaveSettings();
        Assert.False(File.Exists(services.SettingsStore.FilePath));

        services.PersistSettings = true;
        services.SaveSettings();
        Assert.Equal("Temporary", services.SettingsStore.Load().Settings.LastPage);
    }

    [Fact]
    public async Task SpriteExtractorQueue()
    {
        TestData.RequireClientAssets();
        var (services, dialogs) = CreateServices();
        using var _ = services;
        var vm = new SpriteExtractorViewModel(services);
        await vm.OnNavigatedToAsync();
        var catalog = services.Data.Catalog!;
        Assert.Equal(catalog.SheetCount, vm.VisibleSheets.Count);
        Assert.Contains($"{catalog.TotalSprites:N0} sprites", vm.Stats);

        // Filtering by a sprite ID shows the sheet that contains it.
        vm.SheetFilter = catalog.HighestSpriteId.ToString();
        Assert.Contains(vm.VisibleSheets, r => r.Sheet.Contains(catalog.HighestSpriteId));
        vm.SheetFilter = "";

        // Starting with an empty queue explains what to do instead of doing nothing.
        await vm.StartExtractionCommand.ExecuteAsync(null);
        Assert.Contains(dialogs.Messages, m => m.Message.Contains("Add at least one sheet"));

        vm.AddTypeCommand.Execute(SpriteSheetType.Large);
        int large = catalog.Sheets.Count(s => s.Type == SpriteSheetType.Large);
        Assert.Equal(large, vm.Queue.Count);
        Assert.All(vm.Queue, r => Assert.Equal(SpriteSheetType.Large, r.Sheet.Type));
        Assert.Equal(vm.Queue.Sum(r => r.Count), vm.QueuedSprites);

        // Adding the same sheets again does not duplicate them.
        vm.AddTypeCommand.Execute(SpriteSheetType.Large);
        Assert.Equal(large, vm.Queue.Count);

        vm.RemoveAllCommand.Execute(null);
        Assert.Empty(vm.Queue);

        dialogs.PromptAnswers.Enqueue("1, 999999999");
        vm.AddBySpriteIdCommand.Execute(null);
        var only = Assert.Single(vm.Queue);
        Assert.True(only.Sheet.Contains(1));
        Assert.Contains(dialogs.Messages, m => m.Kind == MessageKind.Warning && m.Message.Contains("999999999"));

        dialogs.PromptAnswers.Enqueue("12, abc");
        vm.AddBySpriteIdCommand.Execute(null);
        Assert.Contains(dialogs.Messages, m => m.Message.Contains("\"abc\" is not a valid sprite ID"));
    }

    [Fact]
    public async Task CompareValidatesInputAndDetectsIdenticalFiles()
    {
        var (services, dialogs) = CreateServices();
        using var _ = services;
        var vm = new CompareViewModel(services);

        await vm.CompareFilesCommand.ExecuteAsync(null);
        Assert.Contains(dialogs.Messages, m => m.Message.Contains("Select both an old and a new file"));

        vm.OldFile = "a.dat";
        vm.NewFile = "b.dat";
        vm.SwapCommand.Execute(null);
        Assert.Equal("b.dat", vm.OldFile);
        Assert.Equal("a.dat", vm.NewFile);

        string assets = TestData.RequireClientAssets();
        string appearances = TibiaClientPaths.FindAssetFile(assets, AssetTypes.Appearances)!;
        string map = TibiaClientPaths.FindAssetFile(assets, AssetTypes.Map)!;
        vm.OldFile = appearances;
        vm.NewFile = map;
        await vm.CompareFilesCommand.ExecuteAsync(null);
        Assert.Contains(dialogs.Messages, m => m.Kind == MessageKind.Warning && m.Message.Contains("same type"));
        Assert.False(vm.HasResult);

        vm.NewFile = appearances;
        await vm.CompareFilesCommand.ExecuteAsync(null);
        Assert.True(vm.HasResult);
        Assert.Empty(vm.Categories);
        Assert.Contains(dialogs.Messages, m => m.Message.StartsWith("No changes", StringComparison.Ordinal));
        Assert.Empty(dialogs.Errors);
    }

    [Fact]
    public async Task HouseListSurvivesDuplicateHouseIds()
    {
        // Audit A19: a duplicate house ID in staticdata used to crash the House Images page.
        string dir = TestData.NewTempDirectory("houses");
        var houses = new Tibia.Protobuf.Staticdata.StaticData();
        houses.Houses.Add(new Tibia.Protobuf.Staticdata.House { HouseId = 7, Name = "First Street 1", City = "Thais" });
        houses.Houses.Add(new Tibia.Protobuf.Staticdata.House { HouseId = 7, Name = "Duplicate", City = "Venore" });
        var maps = new Tibia.Protobuf.Staticmapdata.StaticMapData();
        maps.HouseMaps.Add(new Tibia.Protobuf.Staticmapdata.HouseMap { HouseId = 7 });
        maps.HouseMaps.Add(new Tibia.Protobuf.Staticmapdata.HouseMap { HouseId = 8 });
        string staticData = Path.Combine(dir, "staticdata-test.dat"), staticMap = Path.Combine(dir, "staticmapdata-test.dat");
        File.WriteAllBytes(staticData, Google.Protobuf.MessageExtensions.ToByteArray(houses));
        File.WriteAllBytes(staticMap, Google.Protobuf.MessageExtensions.ToByteArray(maps));

        var (services, dialogs) = CreateServices();
        using var _ = services;
        var vm = new HouseImagesViewModel(services);
        await services.Data.LoadStaticDataAsync(staticData);
        await services.Data.LoadStaticMapAsync(staticMap);

        Assert.Empty(dialogs.Errors);
        Assert.Equal(2, vm.VisibleCount);
        Assert.Equal(["First Street 1", "House 8"], vm.VisibleItems.Select(i => i.Name));
    }

    [Fact]
    public async Task SoundsListFollowsKindAndFilter()
    {
        TestData.RequireClientAssets();
        var (services, dialogs) = CreateServices();
        using var _ = services;
        var vm = new SoundsViewModel(services);
        await vm.OnNavigatedToAsync();
        Assert.SkipWhen(vm.DatPath == null, "The client has no sounds file.");
        Assert.False(vm.IsLoaded);

        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsLoaded);
        Assert.Empty(dialogs.Errors);
        var db = services.Data.Sounds!;
        Assert.Equal(db.Items(SoundListKind.Sounds).Count, vm.VisibleCount);
        Assert.NotNull(vm.SelectedItem);

        vm.Kind = SoundListKind.MusicTemplates;
        Assert.Equal(db.Items(SoundListKind.MusicTemplates).Count, vm.VisibleCount);

        vm.Kind = SoundListKind.Sounds;
        vm.FilterText = "1";
        Assert.All(vm.VisibleItems, i => Assert.True(i.Matches("1")));
        Assert.True(vm.VisibleCount < db.Items(SoundListKind.Sounds).Count);
        vm.FilterText = "no sound has this text";
        Assert.Equal(0, vm.VisibleCount);
        Assert.Null(vm.SelectedItem);
    }
}
