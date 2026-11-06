using TibiaFoundry.Core.Metadata;
using System.Windows.Threading;
using ImageMagick;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.StaticData;

namespace TibiaFoundry.Tests;

/// <summary>
/// Runs test code on a private STA thread with its own WPF dispatcher (view-models create
/// brushes and bitmaps, which belong to a dispatcher). The dispatcher is shut down afterwards so
/// its hidden window and window class are released.
/// </summary>
internal static class Sta
{
    private const string EnvironmentAdvice =
        "This happens when the Windows session's window-class (user atom) table is exhausted; sign out or restart Windows and rerun.";

    // The dispatcher needs a hidden window. On a Windows session whose window-class table is
    // nearly exhausted, creating it fails intermittently. A failed attempt leaves a half-built
    // dispatcher attached to its thread (asking that thread again returns it, and it never runs
    // queued work), so every attempt uses a new thread. If the test body still cannot start, the
    // environment problem is reported as a skip with the reason, never as a pass.
    public static async Task Run(Func<Task> body)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => RunOnThisThread(body, started, done));
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            // The body is queued as the dispatcher's first item, so it starts at once when the
            // dispatcher works. A dispatcher that has not started it after 30 s never will.
            var first = await Task.WhenAny(started.Task, done.Task, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            if (first == started.Task || (first == done.Task && done.Task.Exception?.InnerException is not System.ComponentModel.Win32Exception))
            {
                if (attempt > 0)
                    TestContext.Current.AddWarning($"Environment: the WPF dispatcher could not be created {attempt} time(s) ({lastError?.Message}); the test ran on a new thread.");
                await done.Task;
                return;
            }
            lastError = done.Task.IsFaulted ? done.Task.Exception!.InnerException : new TimeoutException("the dispatcher did not start the test within 30 s");
            await Task.Delay(500, TestContext.Current.CancellationToken);
        }
        throw Xunit.Sdk.SkipException.ForSkip(
            "Environment: Windows refused to create the hidden window a WPF dispatcher needs (" + lastError?.Message + "). " + EnvironmentAdvice);
    }

    private static void RunOnThisThread(Func<Task> body, TaskCompletionSource started, TaskCompletionSource done)
    {
        Dispatcher dispatcher;
        try
        {
            dispatcher = Dispatcher.CurrentDispatcher;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            done.TrySetException(ex);
            return;
        }
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        dispatcher.InvokeAsync(async () =>
        {
            started.TrySetResult();
            try
            {
                await body();
                done.TrySetResult();
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
            finally
            {
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        });
        try
        {
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            done.TrySetException(ex);
        }
    }

    /// <summary>Waits (while pumping) until the condition holds.</summary>
    public static async Task Until(Func<bool> condition, string what, int timeoutMs = 60000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(25);
        }
    }
}

/// <summary>End-to-end flows through the view-models against the installed client.</summary>
public sealed class UiFlowTests(ITestOutputHelper output)
{
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private void Step(string what) => output.WriteLine($"{_clock.Elapsed.TotalSeconds,6:0.0}s  {what}");

    internal static (AppServices Services, FakeDialogs Dialogs, string Dir) CreateServices()
    {
        string dir = TestData.NewTempDirectory("flow");
        var settings = new AppSettings { OutputFolder = dir, LoadDataAutomatically = false, SpriteSource = SpriteSourceMode.ClientOnly, CreatorName = "FlowTest" };
        settings.Normalize();
        var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var dialogs = new FakeDialogs();
        services.Dialogs = dialogs;
        return (services, dialogs, dir);
    }

    [Fact(Timeout = 300_000)]
    public async Task AppearancesSearchOutfitterAndExports()
    {
        TestData.RequireClientAssets();
        await Sta.Run(async () =>
        {
            var (services, dialogs, dir) = CreateServices();
            using var _ = services;
            Step("creating the appearances view-model");
            var vm = new AppearancesViewModel(services);
            await vm.OnNavigatedToAsync();
            Assert.False(vm.IsLoaded);

            Step("loading appearances");
            await vm.LoadCommand.ExecuteAsync(null);
            Step("loaded");
            Assert.True(vm.IsLoaded);
            Assert.Empty(dialogs.Errors);
            var db = vm.Database!;
            Assert.Equal(db.List(AppearanceKind.Object).Count, vm.ObjectCount);
            Assert.Equal(db.List(AppearanceKind.Object).Count, vm.VisibleItems.Count);

            // Search by name filters the list; clearing restores it.
            vm.SearchMode = SearchMode.NameContains;
            vm.SearchText = "sword";
            await vm.SearchCommand.ExecuteAsync(null);
            Assert.NotEmpty(vm.VisibleItems);
            Assert.All(vm.VisibleItems, i => Assert.Contains("sword", i.Name, StringComparison.OrdinalIgnoreCase));
            vm.ClearFilters();
            Assert.Equal(vm.ObjectCount, vm.VisibleItems.Count);

            // Go to ID selects the object and fills the data panel and preview.
            vm.SearchMode = SearchMode.GoToId;
            var requested = new List<AppearanceItem>();
            vm.SelectRequested += items => requested.AddRange(items);
            vm.SearchText = "3031";
            await vm.SearchCommand.ExecuteAsync(null);
            var gold = Assert.Single(requested);
            Assert.Equal(3031u, gold.Id);
            vm.SelectedItem = gold;
            await Sta.Until(() => vm.PreviewFrames.Count > 0, "the preview of object 3031");
            Assert.Equal("gold coin", vm.DetailName);
            Assert.Contains(vm.Properties, p => p.Label == "Flags.Cumulative" && p.Value == "True");
            Assert.False(string.IsNullOrEmpty(vm.SpriteIdsText));

            Step("outfitter");
            // Outfitter renders exactly what the core renderer produces for the same options.
            vm.Kind = AppearanceKind.Outfit;
            vm.SelectedItem = vm.VisibleItems.First(i => i.Id == 128);
            await Sta.Until(() => vm.Outfitter.Frames.Count > 0, "outfit 128 frames");
            var expected = OutfitRenderer.Render(db, services.Data.Sprites!, 128, new OutfitRenderOptions
            {
                FrameGroup = 0, Direction = (int)Direction.South, Colors = (int[])GameTables.DefaultOutfitColors.Clone(),
                MountColors = (int[])GameTables.DefaultOutfitColors.Clone(),
            });
            Assert.Equal(expected.Count, vm.Outfitter.Frames.Count);
            Assert.True(expected[0].PixelEquals(Images.FromBitmapSource(vm.Outfitter.Frames[0])), "outfitter frame differs from the core renderer");

            // Turning and addons re-render.
            var before = vm.Outfitter.Frames;
            vm.Outfitter.RotateRightCommand.Execute(null);
            await Sta.Until(() => !ReferenceEquals(before, vm.Outfitter.Frames), "re-render after turning");
            Assert.Equal(Direction.West, vm.Outfitter.Facing);

            Step("saving the outfit GIF");
            // Save the outfit as an animated GIF with the author comment.
            string gif = Path.Combine(dir, "outfit.gif");
            dialogs.NextFile = gif;
            vm.Outfitter.SaveGifCommand.Execute(null);
            Assert.Empty(dialogs.Errors);
            using (var images = new MagickImageCollection(gif))
            {
                Assert.Equal(vm.Outfitter.Frames.Count, images.Count);
                // Every exported GIF carries the creator attribution (see AttributionTests for the details).
                Assert.Contains(Attribution.TextFor(Attribution.CurrentCreator), images[0].Comment);
            }

            Step("exporting JSON");
            // Export the loaded appearances as JSON.
            string json = Path.Combine(dir, "appearances.json");
            dialogs.NextFile = json;
            await vm.ExportJsonCommand.ExecuteAsync(null);
            Assert.Empty(dialogs.Errors);
            Assert.True(new FileInfo(json).Length > 1_000_000);
            using (var reader = new StreamReader(json))
            {
                Assert.Equal('{', (char)reader.Read());
            }
        });
    }

    /// <summary>Regression for B16: "PNG File" wrote an empty file and saved the image as mgk.png.</summary>
    [Fact(Timeout = 300_000)]
    public async Task OutfitSheetPngFileMode_WritesChosenFile()
    {
        TestData.RequireClientAssets();
        await Sta.Run(async () =>
        {
            var (services, dialogs, dir) = CreateServices();
            using var _ = services;
            var vm = new AppearancesViewModel(services);
            await vm.OnNavigatedToAsync();
            await vm.LoadCommand.ExecuteAsync(null);
            vm.Kind = AppearanceKind.Outfit;
            vm.SelectedItem = vm.VisibleItems.First(i => i.Id == 128);

            string file = Path.Combine(dir, "Outfit 128.png");
            string strayFile = Path.Combine(Environment.CurrentDirectory, "mgk.png");
            bool strayBefore = File.Exists(strayFile);
            dialogs.NextFile = file;
            vm.Export.SheetOutput = SheetOutput.PngFile;
            vm.Export.SheetMagenta = true;
            vm.Export.GenerateSheetCommand.Execute(null);

            Assert.Empty(dialogs.Errors);
            var expected = SheetComposer.OutfitSheet(vm.SelectedItem!.Appearance, services.Data.Sprites!, magentaBackground: true);
            var written = ImageIO.Load(file);
            Assert.True(expected.PixelEquals(written), "the saved sheet differs from the composed sheet");
            Assert.Equal(strayBefore, File.Exists(strayFile));
        });
    }

    [Fact(Timeout = 120_000)]
    public async Task ToolsCompareButtonOpensComparePageAndSelectionIsRemembered()
    {
        await Sta.Run(() =>
        {
            var (services, _, _) = CreateServices();
            using var _ = services;
            var main = new MainViewModel(services);
            Assert.IsType<HomeViewModel>(main.SelectedPage);                     // a new installation starts on the Home dashboard

            main.SelectedPage = main.Appearances!;
            main.Appearances!.OpenCompareCommand.Execute(null);
            Assert.IsType<CompareViewModel>(main.SelectedPage);
            Assert.Equal(nameof(CompareViewModel), services.Settings.LastPage);
            Assert.Equal([nameof(CompareViewModel), nameof(AppearancesViewModel)], services.Settings.RecentPages);   // listed on the Home dashboard

            // The last page is restored by a new shell (settings are shared).
            var again = new MainViewModel(services);
            Assert.IsType<CompareViewModel>(again.SelectedPage);
            return Task.CompletedTask;
        });
    }

    [Fact(Timeout = 120_000)]
    public async Task ResetToDefaultsKeepsOptionsFromNewerVersions()
    {
        // Found during the audit: "Reset to defaults" overwrote the settings format number and
        // dropped options written by a newer TibiaFoundry (a silent downgrade).
        await Sta.Run(() =>
        {
            var (services, dialogs, _) = CreateServices();
            using var _ = services;
            services.Settings.Version = AppSettings.CurrentVersion + 1;
            services.Settings.UnknownSettings = new() { ["FutureOption"] = System.Text.Json.JsonDocument.Parse("true").RootElement.Clone() };
            var main = new MainViewModel(services);
            var settings = main.Pages.OfType<SettingsViewModel>().Single();

            dialogs.ConfirmAnswer = true;
            settings.ResetDefaultsCommand.Execute(null);

            var fresh = new AppSettings();
            fresh.Normalize();
            Assert.Equal(fresh.OutputFolder, services.Settings.OutputFolder);  // the reset happened (a temp folder before)
            Assert.Equal("FlowTest", services.Settings.CreatorName);              // the creator name is identity, not a preference: kept
            Assert.Equal(AppSettings.CurrentVersion + 1, services.Settings.Version);
            Assert.True(services.Settings.UnknownSettings!.ContainsKey("FutureOption"));
            return Task.CompletedTask;
        });
    }

    [Fact(Timeout = 300_000)]
    public async Task StaticDataCreatureGif()
    {
        TestData.RequireClientAssets();
        await Sta.Run(async () =>
        {
            var (services, dialogs, dir) = CreateServices();
            using var _ = services;
            Step("creating the static data view-model");
            var vm = new StaticDataViewModel(services);
            await vm.OnNavigatedToAsync();
            await vm.LoadCommand.ExecuteAsync(null);
            Assert.True(vm.IsLoaded);
            Assert.Empty(dialogs.Errors);
            Assert.Equal(services.Data.StaticData!.Monsters.Count, vm.TotalCount);

            // Exporting before a creature is shown explains what to do.
            vm.SelectedItem = null;
            vm.ExportGifCommand.Execute(null);
            Assert.Contains(dialogs.Messages, m => m.Message.Contains("Select a monster"));

            vm.FilterText = "orc";
            vm.SpriteFilter = "1";
            vm.ClearFiltersCommand.Execute(null);
            Assert.Equal("", vm.FilterText);
            Assert.Equal("", vm.SpriteFilter);
            Assert.Equal(vm.TotalCount, vm.VisibleCount);

            vm.FilterText = "orc warlord";
            vm.SelectedItem = vm.VisibleItems.First(i => i.Source is Creature c && c.Name == "orc warlord");
            Step("waiting for orc warlord");
            await Sta.Until(() => vm.Frames.Count > 0, "orc warlord frames");
            Step("exporting the GIF");

            string gif = Path.Combine(dir, "orc warlord.gif");
            dialogs.NextFile = gif;
            vm.ExportGifCommand.Execute(null);
            Assert.Empty(dialogs.Errors);
            using var images = new MagickImageCollection(gif);
            Assert.Equal(vm.Frames.Count, images.Count);
        });
    }
}

/// <summary>Static Data (8.8.2): select all creatures and export their GIFs at once, named like TibiaWiki articles.</summary>
/// <remarks>It renders all ~1,300 creatures on every core; run on its own so timing tests elsewhere are not starved.</remarks>
[Collection(nameof(BatchExportCollection))]
public sealed class StaticDataBatchGifTests(ITestOutputHelper output)
{
    [Fact(Timeout = 600_000)]
    public async Task SelectedAndAllCreatureGifsAreExportedAtOnce()
    {
        TestData.RequireClientAssets();
        await Sta.Run(async () =>
        {
            var (services, dialogs, _) = UiFlowTests.CreateServices();
            using var _s = services;
            var vm = new StaticDataViewModel(services);
            await vm.OnNavigatedToAsync();
            await vm.LoadCommand.ExecuteAsync(null);
            Assert.True(vm.IsLoaded);

            // Names are shown in article casing; the data keeps the client's own text.
            var warlord = vm.VisibleItems.First(i => i.Source is Creature { Name: "orc warlord" });
            Assert.Equal("Orc Warlord", warlord.Name);

            vm.FilterText = "orc";
            vm.SelectAllCommand.Execute(null);
            int selected = vm.SelectedCount;
            Assert.Equal(vm.VisibleCount, selected);
            Assert.True(selected > 3);
            dialogs.ConfirmAnswer = true;
            await vm.ExportGifCommand.ExecuteAsync(null);
            string folder = services.Outputs.FolderFor(Core.FileOps.OutputKind.GifExports);
            var files = Directory.GetFiles(folder, "*.gif").Select(Path.GetFileName).ToList();
            Assert.Equal(selected, files.Count);
            Assert.Contains("Orc.gif", files);
            Assert.Contains("Orc Warlord.gif", files);
            Assert.DoesNotContain(files, f => char.IsLower(f![0]));
            Assert.Empty(dialogs.Errors);

            // A second export keeps the first files and numbers the new ones.
            await vm.ExportGifCommand.ExecuteAsync(null);
            Assert.Contains("Orc 2.gif", Directory.GetFiles(folder, "*.gif").Select(Path.GetFileName));

            var watch = System.Diagnostics.Stopwatch.StartNew();
            await vm.ExportAllCreatureGifsCommand.ExecuteAsync(null);
            var db = services.Data.StaticData!;
            int monsters = Directory.GetFiles(Path.Combine(folder, "Monsters"), "*.gif").Length;
            int bosses = Directory.GetFiles(Path.Combine(folder, "Bosses"), "*.gif").Length;
            output.WriteLine($"all creatures: {monsters} of {db.Monsters.Count} monsters, {bosses} of {db.Bosses.Count} bosses in {watch.Elapsed.TotalSeconds:0.0} s; status: {services.Status.Message}");
            Assert.True(monsters >= db.Monsters.Count * 0.95, $"{monsters} of {db.Monsters.Count} monster GIFs");
            Assert.True(bosses >= db.Bosses.Count * 0.9, $"{bosses} of {db.Bosses.Count} boss GIFs");
            Assert.True(File.Exists(Path.Combine(folder, "Monsters", "Demon Lord.gif")) || !db.Monsters.Any(c => c.Name == "demon lord"));
        });
    }
}

[CollectionDefinition(nameof(BatchExportCollection), DisableParallelization = true)]
public sealed class BatchExportCollection;

/// <summary>Dialogs on small screens and at high display scaling (8.5).</summary>
public sealed class DialogSizingTests
{
    [Fact]
    public async Task DialogsAreLimitedToTheWorkAreaAndNeverBelowTheirMinimum()
    {
        await Sta.Run(() =>
        {
            // 1366×768 at 125 %, less the taskbar: 1093×576 device-independent units.
            var small = new System.Windows.Rect(0, 0, 1093, 576);
            var dialog = new System.Windows.Window { Width = 600, MinWidth = 480, MinHeight = 300, MaxHeight = 780 };
            App.Views.Dialogs.DialogSizing.FitTo(dialog, small);
            Assert.Equal(576 - 48, dialog.MaxHeight);   // the buttons stay on the screen; the content scrolls
            Assert.Equal(600, dialog.Width);            // wide enough already

            var unlimited = new System.Windows.Window { Width = 1200, MinWidth = 480, MinHeight = 300 };
            App.Views.Dialogs.DialogSizing.FitTo(unlimited, small);
            Assert.Equal(576 - 48, unlimited.MaxHeight);
            Assert.Equal(1093 - 48, unlimited.Width);

            var tiny = new System.Windows.Window { Width = 600, MinWidth = 480, MinHeight = 300, MaxHeight = 780 };
            App.Views.Dialogs.DialogSizing.FitTo(tiny, new System.Windows.Rect(0, 0, 400, 200));
            Assert.Equal(300, tiny.MaxHeight);          // never below the minimum
            Assert.Equal(480, tiny.Width);

            var large = new System.Windows.Window { Width = 600, MaxHeight = 780 };
            App.Views.Dialogs.DialogSizing.FitTo(large, new System.Windows.Rect(0, 0, 2560, 1400));
            Assert.Equal(780, large.MaxHeight);         // unchanged where there is room
            return Task.CompletedTask;
        });
    }
}
