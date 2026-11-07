using System.Runtime.InteropServices;
using ImageMagick;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.Tests;

/// <summary>
/// "✓ Saved to &lt;path&gt;": export messages name the file that was actually written, the path is a
/// link that opens File Explorer there, failures never show a success, and a folder that cannot
/// be opened is reported separately from the export.
/// </summary>
public sealed class ExportStatusTests(ITestOutputHelper output)
{
    /// <summary>Records what would have been asked of Windows; can fail on request.</summary>
    private sealed class FakeShell : IShellIntegration
    {
        public List<string> Selected { get; } = [];
        public List<string> Opened { get; } = [];
        public bool FailSelect { get; set; }
        public bool FailOpen { get; set; }

        public void OpenFolderAndSelect(string file)
        {
            if (FailSelect) throw new COMException("selection is not supported here", unchecked((int)0x80004001));
            Selected.Add(file);
        }

        public void OpenFolder(string folder)
        {
            if (FailOpen) throw new System.ComponentModel.Win32Exception(5, "Access is denied");
            Opened.Add(folder);
        }

        public void OpenUrl(string url) { }
    }

    private static (AppServices Services, FakeDialogs Dialogs, FakeShell Shell, string Dir) CreateServices(string gifFolderName = "GIF Exports ü 日本")
    {
        string dir = TestData.NewTempDirectory("exports");
        var settings = new AppSettings { OutputFolder = dir, LoadDataAutomatically = false, SpriteSource = SpriteSourceMode.ClientOnly, CreatorName = "ExportTest" };
        settings.Normalize();
        settings.GifFolder = Path.Combine(dir, gifFolderName);
        var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var dialogs = new FakeDialogs();
        services.Dialogs = dialogs;
        var shell = new FakeShell();
        services.Shell.Shell = shell;
        return (services, dialogs, shell, dir);
    }

    [Fact]
    public void TheStatusNamesTheSavedFileAndMakesItClickable()
    {
        var (services, _, shell, dir) = CreateServices();
        using var _ = services;
        string file = Path.Combine(dir, "GIF Exports ü 日本", "Gold Coin (it's) #1.gif");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, [0x47, 0x49, 0x46]);
        services.Status.ShowSaved(file, isFile: true);
        Assert.Equal(StatusKind.Success, services.Status.Kind);
        Assert.Equal("Saved to " + file, services.Status.FullText);
        Assert.Equal(file, services.Status.Link!.Path);
        Assert.Contains(file, services.Status.Link.ToolTip);

        services.RevealSaved(services.Status.Link);
        Assert.Equal([file], shell.Selected);                                     // the exact file, spaces and Unicode intact
        Assert.Empty(shell.Opened);

        // A plain message replaces the link.
        services.Status.Show("Ready");
        Assert.Null(services.Status.Link);
    }

    [Fact]
    public void LongPathsAreShortenedForDisplayOnly()
    {
        string path = @"C:\Users\Someone\OneDrive\Documents\Tibia_Foundry\GIF_Exports\" + string.Concat(Enumerable.Repeat("very long folder name\\", 6)) + "item 3031.gif";
        var link = new StatusLink(path, IsFile: true);
        Assert.True(link.Label.Length <= 80, link.Label);
        Assert.Contains("…", link.Label);
        Assert.EndsWith("item 3031.gif", link.Label);
        Assert.StartsWith(@"C:\Users\…", link.Label);
        Assert.Equal(path, link.Path);                                             // the link itself keeps the exact path
        Assert.Contains(path, link.ToolTip);
    }

    [Fact]
    public void WhenSelectingIsNotPossibleTheFolderOpensAndFailuresAreReportedSeparately()
    {
        var (services, dialogs, shell, dir) = CreateServices();
        using var _ = services;
        string file = Path.Combine(dir, "a.gif");
        File.WriteAllBytes(file, [1]);
        services.Status.ShowSaved(file, isFile: true);

        shell.FailSelect = true;
        var result = services.Shell.Reveal(file);
        Assert.True(result.Opened);
        Assert.False(result.Selected);
        Assert.Equal([dir], shell.Opened);                                        // fell back to the containing folder

        shell.FailOpen = true;
        services.RevealSaved(services.Status.Link!);
        Assert.Equal(StatusKind.Success, services.Status.Kind);                   // the export's success message stays
        Assert.Equal("Saved to " + file, services.Status.FullText);
        var (kind, message) = Assert.Single(dialogs.Messages);
        Assert.Equal(MessageKind.Warning, kind);
        Assert.Contains("could not be opened", message);

        // The file was moved away later: its folder opens, and the user is told why.
        shell.FailSelect = shell.FailOpen = false;
        File.Delete(file);
        services.RevealSaved(new StatusLink(file, IsFile: true));
        Assert.Contains(dialogs.Messages, m => m.Kind == MessageKind.Info && m.Message.Contains("no longer exists"));
    }

    [Fact(Timeout = 120_000)]
    public async Task GifOutcomesAreReportedHonestly()
    {
        await Sta.Run(() =>
        {
            var (services, _, _, dir) = CreateServices();
            using var _ = services;
            var vm = new AppearancesViewModel(services);
            var export = vm.Export;
            string folder = Path.Combine(dir, "gifs");

            var one = new AppearanceExportViewModel.GifExportOutcome(1);
            one.Saved.Add(Path.Combine(folder, "x.gif"));
            export.ShowGifOutcome(one, folder);
            Assert.Equal(StatusKind.Success, services.Status.Kind);
            Assert.True(services.Status.Link!.IsFile);
            Assert.Equal(Path.Combine(folder, "x.gif"), services.Status.Link.Path);

            var many = new AppearanceExportViewModel.GifExportOutcome(3);
            many.Saved.AddRange(new[] { "a", "b", "c" }.Select(n => Path.Combine(folder, n + ".gif")));
            export.ShowGifOutcome(many, folder);
            Assert.Equal("Saved 3 GIFs to " + folder, services.Status.FullText);
            Assert.False(services.Status.Link!.IsFile);

            var partial = new AppearanceExportViewModel.GifExportOutcome(3);
            partial.Saved.Add(Path.Combine(folder, "a.gif"));
            partial.Failures.AddRange(["Object 1: no sprites", "Object 2: no sprites"]);
            export.ShowGifOutcome(partial, folder);
            Assert.Equal(StatusKind.Warning, services.Status.Kind);
            Assert.Contains("1 of 3", services.Status.FullText);

            var failed = new AppearanceExportViewModel.GifExportOutcome(1);
            failed.Failures.Add("Object 5: the object has no frames");
            export.ShowGifOutcome(failed, folder);
            Assert.Equal(StatusKind.Error, services.Status.Kind);                     // never a success checkmark for a failure
            Assert.Null(services.Status.Link);
            Assert.DoesNotContain("Saved", services.Status.FullText);
            Assert.Contains("no frames", services.Status.FullText);
            return Task.CompletedTask;
        });
    }

    [Fact(Timeout = 300_000)]
    public async Task ExportingAGifShowsTheExactSavedFileInAFolderWithSpacesAndUnicode()
    {
        TestData.RequireClientAssets();
        await Sta.Run(async () =>
        {
            var (services, dialogs, shell, dir) = CreateServices();
            using var _ = services;
            var vm = new AppearancesViewModel(services);
            await vm.LoadCommand.ExecuteAsync(null);
            Assert.True(vm.IsLoaded);
            vm.SelectedItem = vm.VisibleItems.First(i => i.Id == 3031);
            vm.Export.AutoName = true;
            string folder = Path.Combine(dir, "GIF Exports ü 日本");
            Assert.Equal(folder, vm.Export.OutputFolder);
            Assert.False(Directory.Exists(folder));                                // created when the first GIF is saved

            await vm.Export.SaveGifCommand.ExecuteAsync(null);
            var saved = Directory.GetFiles(folder, "*.gif");
            string file = Assert.Single(saved);
            Assert.Equal(StatusKind.Success, services.Status.Kind);
            Assert.Equal("Saved to " + file, services.Status.FullText);           // the actual file, not a placeholder
            Assert.Equal(file, services.Status.Link!.Path);
            using (var gif = new MagickImageCollection(file)) Assert.True(gif.Count >= 1);

            // A second export never replaces the first.
            await vm.Export.SaveGifCommand.ExecuteAsync(null);
            Assert.Equal(2, Directory.GetFiles(folder, "*.gif").Length);
            string second = services.Status.Link!.Path;
            Assert.NotEqual(file, second);
            Assert.True(File.Exists(second));

            // Clicking the path shows that file in Explorer.
            services.RevealSaved(services.Status.Link);
            Assert.Equal([second], shell.Selected);

            // The operation is in the history with its output.
            var last = services.Jobs.History[0];
            Assert.Equal(Core.Diagnostics.OperationState.Completed, last.State);
            Assert.Equal("appearances", last.ToolId);

            // A GIF folder that cannot be used: explained, nothing written, no success shown.
            string blocked = Path.Combine(dir, "blocked");
            File.WriteAllText(blocked, "");
            services.Settings.GifFolder = blocked;
            vm.Export.RefreshFolder();
            services.Status.Show("Ready");
            dialogs.ConfirmAnswer = false;
            await vm.Export.SaveGifCommand.ExecuteAsync(null);
            Assert.NotEqual(StatusKind.Success, services.Status.Kind);
            Assert.Null(services.Status.Link);
            output.WriteLine("Saved: " + file);
        });
    }

    /// <summary>
    /// Real Windows Explorer: opens the folder of a file with the file selected, then closes the
    /// window again. Opt-in (TF_SHELL_CHECK=1) because it opens a window on the desktop.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task ExplorerOpensTheFolderWithTheFileSelected()
    {
        Assert.SkipUnless(TestData.Setting("SHELL_CHECK") == "1", "Opens a File Explorer window; set TF_SHELL_CHECK=1 to run it.");
        string dir = Path.Combine(TestData.NewTempDirectory("explorer"), "Saved GIFs ü 日本");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "Gold Coin (3031).gif");
        File.WriteAllBytes(file, [0x47, 0x49, 0x46, 0x38, 0x39, 0x61]);
        await Sta.Run(async () =>
        {
            var shell = new ShellService();
            var result = shell.Reveal(file);
            Assert.True(result.Opened, result.Problem);
            Assert.True(result.Selected);
            dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            dynamic? window = null;
            string? selected = null;
            for (int i = 0; i < 100 && selected == null; i++)
            {
                await Task.Delay(100);
                foreach (dynamic w in app.Windows())
                {
                    try
                    {
                        string? path = (string?)w.Document?.Folder?.Self?.Path;
                        if (!string.Equals(path, dir, StringComparison.OrdinalIgnoreCase)) continue;
                        window = w;
                        foreach (dynamic item in w.Document.SelectedItems()) selected = (string)item.Path;
                    }
                    catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                }
            }
            try
            {
                Assert.NotNull(window);
                Assert.Equal(file, selected);
                output.WriteLine($"Explorer showed {dir} with {Path.GetFileName(selected)} selected.");
            }
            finally
            {
                try { window?.Quit(); } catch (COMException) { }
            }
        });
    }
}
