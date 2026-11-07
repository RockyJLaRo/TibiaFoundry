using System.Windows;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Scripts;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Tools;
using DataFormat = TibiaFoundry.Core.Tools.DataFormat;

namespace TibiaFoundry.Tests;

/// <summary>A sample tool exactly as docs/16_Adding_a_Tool.md describes it: manifest, view-model, view, one registration.</summary>
public sealed partial class SampleToolViewModel(AppServices services) : PageViewModel(services, Manifest_)
{
    public static readonly ToolManifest Manifest_ = new()
    {
        Id = "sample.word-counter",
        Name = "Word Counter",
        Description = "Counts the words of text files (sample tool of the documentation)",
        Type = ToolType.Utility,
        Version = "1.2",
        Category = "Tools",
        Capabilities = ToolCapabilities.Progress | ToolCapabilities.Batch,
        Operations = [new ToolOperation("count", "Count words", ToolCapabilities.Progress | ToolCapabilities.Batch)],
        Inputs = [new DataFormat("Text files", [".txt"])],
        Outputs = [new DataFormat("Report", [".csv"])],
        DefaultOutput = OutputKind.Reports,
        SettingsVersion = 2,
        Settings =
        [
            new ToolSettingDefinition("minimumLength", "Shortest word", ToolSettingKind.Integer, "1", "Shorter words are not counted.") { Minimum = 1, Maximum = 50 },
            new ToolSettingDefinition("ignoreCase", "Ignore case", ToolSettingKind.Boolean, "true", "Count \"Tibia\" and \"tibia\" as one word."),
        ],
        SettingsMigrations = [new ToolSettingsMigration(1, "\"minLen\" was renamed to \"minimumLength\"", values =>
        {
            if (values.Remove("minLen", out string? v)) values["minimumLength"] = v;
        })],
        Dependencies =
        [
            new ToolDependency("sample-tokenizer", "Sample tokenizer", DependencyKind.Library, Required: true, "Install the sample tokenizer.") { RequiredFor = ["count"] },
            new ToolDependency("sample-dictionary", "Sample dictionary service", DependencyKind.Service, Required: false, "Connect to the internet to get spelling hints."),
        ],
        License = new ToolLicense("MIT", "Sample © Example Author", "MIT", "https://example.invalid/sample"),
        Documentation = ["docs/16_Adding_a_Tool.md"],
    };

    public int Counted { get; private set; }

    public Task<bool> CountAsync(IReadOnlyList<string> files, Func<CancellationToken, Task>? during = null) => RunAsync("Counting words", async (progress, ct) =>
    {
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            Counted += (await File.ReadAllTextAsync(files[i], ct)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            progress.Report(new OperationProgress(i + 1, files.Count, Path.GetFileName(files[i])));
            if (during != null) await during(ct);
        }
    }, operation: "count", onSuccess: () => Services.Status.Show($"{Counted} words.", StatusKind.Success));
}

public sealed class SampleToolView : System.Windows.Controls.UserControl;

/// <summary>The tool registry: manifests, registration, capabilities, dependencies, settings and their migration.</summary>
public sealed class ToolArchitectureTests
{
    private static (AppServices Services, FakeDialogs Dialogs, string Dir) CreateServices()
    {
        string dir = TestData.NewTempDirectory("toolarch");
        var settings = new AppSettings { OutputFolder = Path.Combine(dir, "out"), LoadDataAutomatically = false };
        settings.Normalize();
        var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var dialogs = new FakeDialogs();
        services.Dialogs = dialogs;
        return (services, dialogs, dir);
    }

    private static readonly PageRegistration Sample = PageRegistration.For<SampleToolViewModel, SampleToolView>(SampleToolViewModel.Manifest_, (s, _) => new SampleToolViewModel(s));

    [Fact]
    public void EveryBuiltInPageAndScriptHasAValidUniqueManifest()
    {
        var catalog = new ToolCatalog();
        foreach (var page in BuiltInTools.Pages)
        {
            Assert.True(catalog.TryRegister(page.Manifest, out var problems), page.Manifest.Id + ": " + string.Join(" ", problems));
            Assert.True(typeof(PageViewModel).IsAssignableFrom(page.ViewModelType));
            Assert.True(typeof(FrameworkElement).IsAssignableFrom(page.ViewType));
        }
        foreach (var script in ScriptRegistry.CreateAll(() => new BackupStore(Path.GetTempPath()), Path.GetTempPath()))
            Assert.True(catalog.TryRegister(script.Manifest, out var problems), script.Id + ": " + string.Join(" ", problems));
        Assert.Empty(catalog.Rejected);
        Assert.Equal(BuiltInTools.Pages.Count + 12, catalog.Tools.Count);
        Assert.Equal(["Assets Editor", "RCC Editor", "Client Converter", "Scripts"],
            catalog.Tools.Where(t => t.Id is "rcc-editor" or "client-converter" or "assets-editor" or "scripts").Select(t => t.Name));
        Assert.Contains("Arch-Mina", BuiltInTools.ClientConverter.License.Attribution);
        Assert.Contains("Nekiro", BuiltInTools.RccEditor.License.Attribution);
        Assert.True(BuiltInTools.Scripts.Supports(ToolCapabilities.Preview | ToolCapabilities.Undo));
        Assert.False(BuiltInTools.RccEditor.CapabilitiesOf("save").HasFlag(ToolCapabilities.Cancellation));
    }

    [Fact(Timeout = 120_000)]
    public async Task ASampleToolIsRegisteredAndRunsThroughTheSharedInfrastructure()
    {
        await Sta.Run(async () =>
        {
            var (services, dialogs, dir) = CreateServices();
            using var _ = services;
            bool tokenizer = false;
            services.Tools.AddProbe("sample-tokenizer", () => tokenizer ? new DependencyProbeResult(DependencyState.Available, "2.0") : new DependencyProbeResult(DependencyState.Missing, Detail: "not installed"));
            services.Tools.AddProbe("sample-dictionary", () => new DependencyProbeResult(DependencyState.Missing, Detail: "offline"));

            // One registration adds the page: navigation, registry, view template.
            var main = new MainViewModel(services, BuiltInTools.Pages.Append(Sample));
            var page = Assert.Single(main.Pages.OfType<SampleToolViewModel>());
            Assert.Equal("Word Counter", page.Title);
            Assert.Equal("Tools", page.Group);
            Assert.Same(SampleToolViewModel.Manifest_, services.Tools.Find("sample.word-counter"));
            var resources = new ResourceDictionary();
            Assert.Equal(BuiltInTools.Pages.Count + 1, PageTemplates.Register(resources, main.Registrations));
            Assert.True(resources.Contains(new DataTemplateKey(typeof(SampleToolViewModel))));

            // A missing optional dependency is shown, but nothing is blocked; the program started normally.
            main.SelectedPage = page;
            Assert.Contains("Sample dictionary service", page.DependencyNotice);

            // A missing required dependency of the operation: explained, nothing starts, no operation recorded.
            string file = Path.Combine(dir, "words.txt");
            File.WriteAllText(file, "Tibia Foundry counts words");
            Assert.False(await page.CountAsync([file]));
            Assert.Equal(StatusKind.Error, services.Status.Kind);
            Assert.Contains("Install the sample tokenizer.", services.Status.Message);
            Assert.Contains(dialogs.Messages, m => m.Message.Contains("Sample tokenizer: missing"));
            Assert.Empty(services.Jobs.History);
            Assert.Equal(0, page.Counted);

            // Available: runs with progress, without a cancel button (the operation does not declare cancellation).
            tokenizer = true;
            bool? cancelVisible = null;
            Assert.True(await page.CountAsync([file], _ => { cancelVisible = services.Status.CanCancel; return Task.CompletedTask; }));
            Assert.False(cancelVisible);
            Assert.Equal(4, page.Counted);
            var run = Assert.Single(services.Jobs.History);
            Assert.Equal(OperationState.Completed, run.State);
            Assert.Equal("sample.word-counter", run.ToolId);
            Assert.Equal((1, 1), ((int)run.Done, (int)run.Total));
            Assert.Equal("4 words.", run.Summary);

            // Its settings appear in Settings → Tools with generated controls.
            var settings = main.Pages.OfType<SettingsViewModel>().Single();
            var group = Assert.Single(settings.ToolSettingGroups, g => g.Tool.Id == "sample.word-counter");
            Assert.Equal(["Shortest word", "Ignore case"], group.Settings.Select(r => r.Label));
            var minimum = group.Settings[0];
            minimum.Value = "500";
            Assert.NotNull(minimum.Problem);                                       // validated: out of range
            Assert.Equal("1", minimum.Value);
            minimum.Value = "3";
            Assert.Null(minimum.Problem);
            Assert.Equal(3, services.ToolSettings.GetLong(SampleToolViewModel.Manifest_, "minimumLength"));
            Assert.Equal("3", new SettingsStore(services.SettingsStore.FilePath).Load().Settings.ToolState["sample.word-counter.minimumLength"]);
            group.ResetCommand.Execute(null);
            Assert.Equal("1", minimum.Value);
        });
    }

    [Fact(Timeout = 120_000)]
    public async Task InvalidManifestsAndBrokenToolsDoNotStopTheProgram()
    {
        await Sta.Run(() =>
        {
            var (services, _, _) = CreateServices();
            using var _ = services;
            var bad = SampleToolViewModel.Manifest_ with { Id = "Bad Id!", Version = "one", Name = "" };
            var duplicate = SampleToolViewModel.Manifest_ with { Name = "Second" };
            var throwing = SampleToolViewModel.Manifest_ with { Id = "sample.broken", Name = "Broken" };
            var main = new MainViewModel(services,
            [
                .. BuiltInTools.Pages, Sample,
                PageRegistration.For<SampleToolViewModel, SampleToolView>(bad, (s, _) => new SampleToolViewModel(s)),
                PageRegistration.For<SampleToolViewModel, SampleToolView>(duplicate, (s, _) => new SampleToolViewModel(s)),
                PageRegistration.For<SampleToolViewModel, SampleToolView>(throwing, (s, _) => throw new InvalidOperationException("the tool's data is missing")),
            ]);
            Assert.Equal(BuiltInTools.Pages.Count + 1, main.Pages.Count);                 // only the valid sample was added
            Assert.Equal(2, services.Tools.Rejected.Count);
            var rejectedBad = services.Tools.Rejected.Single(r => r.Id == "Bad Id!");
            Assert.Contains(rejectedBad.Problems, p => p.Contains("id"));
            Assert.Contains(rejectedBad.Problems, p => p.Contains("version"));
            Assert.Contains(rejectedBad.Problems, p => p.Contains("name"));
            Assert.Contains(services.Tools.Rejected.Single(r => r.Name == "Second").Problems, p => p.Contains("already used"));
            Assert.Contains(main.LoadFailures, f => f.Contains("Broken") && f.Contains("data is missing"));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void ManifestValidationExplainsEachProblem()
    {
        var m = SampleToolViewModel.Manifest_;
        Assert.Empty(ToolCatalog.Validate(m));
        Assert.Contains(ToolCatalog.Validate(m with { License = new ToolLicense("", "") }), p => p.Contains("License"));
        Assert.Contains(ToolCatalog.Validate(m with { Settings = [new ToolSettingDefinition("level", "Level", ToolSettingKind.Integer, "99", "") { Maximum = 10 }] }), p => p.Contains("default"));
        Assert.Contains(ToolCatalog.Validate(m with { Settings = [new ToolSettingDefinition("mode", "Mode", ToolSettingKind.Choice, "a", "")] }), p => p.Contains("no choices"));
        Assert.Contains(ToolCatalog.Validate(m with { SettingsMigrations = [new ToolSettingsMigration(2, "x", _ => { })] }), p => p.Contains("migration"));
        Assert.Contains(ToolCatalog.Validate(m with { Dependencies = [new ToolDependency("d", "D", DependencyKind.Library, true, "Fix it.") { RequiredFor = ["unknown"] }] }), p => p.Contains("unknown"));
        Assert.Contains(ToolCatalog.Validate(m with { Dependencies = [new ToolDependency("d", "D", DependencyKind.Library, true, "")] }), p => p.Contains("resolve"));
        Assert.Contains(ToolCatalog.Validate(m with { Operations = [new ToolOperation("a", "A", 0), new ToolOperation("a", "B", 0)] }), p => p.Contains("twice"));
        Assert.Contains(ToolCatalog.Validate(null), p => p.Contains("missing"));
    }

    [Fact]
    public void DependencyChecksNeverCrashAndRespectMinimumVersions()
    {
        var catalog = new ToolCatalog();
        var m = SampleToolViewModel.Manifest_ with
        {
            Dependencies = [new ToolDependency("lib", "Library", DependencyKind.Library, true, "Update it.") { MinimumVersion = "3.0", RequiredFor = ["count"] }],
        };
        catalog.AddProbe("lib", () => new DependencyProbeResult(DependencyState.Available, "2.5.1"));
        var old = catalog.Availability(m, "count");
        Assert.False(old.CanRun);
        Assert.Contains("older than the required 3.0", old.Problem);
        Assert.True(catalog.Availability(m).CanRun);                                 // the tool itself is usable; only "count" is blocked
        catalog.AddProbe("lib", () => throw new InvalidOperationException("probe crashed"));
        var crashed = catalog.Availability(m, "count");
        Assert.False(crashed.CanRun);
        Assert.Contains("probe crashed", crashed.Problem);
        var unprobed = new ToolCatalog().CheckDependencies(m, "count").Single();
        Assert.Equal(DependencyState.NotChecked, unprobed.State);                     // unknown is not reported as available
    }

    [Fact]
    public void ToolSettingsAreValidatedResetAndMigratedPerTool()
    {
        var state = new Dictionary<string, string>
        {
            ["sample.word-counter.minLen"] = "4",                                        // stored by settings version 1
            ["sample.word-counter.ignoreCase"] = "maybe",                               // invalid
            ["ClientConverter.profile"] = "1098",                                        // another tool: untouched
        };
        var store = new ToolSettingsStore(state);
        var m = SampleToolViewModel.Manifest_;
        Assert.Equal(1, store.StoredVersion(m));
        var done = store.Migrate(m);
        Assert.Single(done);
        Assert.Equal("4", store.Get(m, "minimumLength"));
        Assert.False(state.ContainsKey("sample.word-counter.minLen"));
        Assert.Equal(2, store.StoredVersion(m));
        Assert.Empty(store.Migrate(m));                                                  // runs once
        Assert.Equal("true", store.Get(m, "ignoreCase"));                                // invalid stored value reads as the default
        Assert.False(store.TrySet(m, "minimumLength", "0", out string? problem));
        Assert.Contains("from 1", problem);
        Assert.True(store.TrySet(m, "minimumLength", " 7 ", out _));
        Assert.Equal(7, store.GetLong(m, "minimumLength"));
        store.Reset(m);
        Assert.Equal("1", store.Get(m, "minimumLength"));
        Assert.Equal("1098", state["ClientConverter.profile"]);

        state["sample.word-counter.$settingsVersion"] = "9";                             // written by a newer version
        state["sample.word-counter.future"] = "x";
        Assert.Empty(store.Migrate(m));
        Assert.Equal("x", state["sample.word-counter.future"]);
    }

    [Fact(Timeout = 120_000)]
    public async Task TheClientConverterKeepsItsOptionsAsDeclaredToolSettings()
    {
        await Sta.Run(() =>
        {
            var (services, _, _) = CreateServices();
            using var _ = services;
            services.Settings.ToolState["ClientConverter.profile"] = "1098";             // stored by 5.0 under its old key
            services.Settings.ToolState["ClientConverter.skipEmpty"] = "false";
            var main = new MainViewModel(services);
            var converter = main.Pages.OfType<ClientConverterViewModel>().Single();
            Assert.Equal("1098", converter.ProfileId);
            Assert.False(converter.SkipEmpty);

            // Changed in Settings → Tools: the page follows at once.
            var group = main.Pages.OfType<SettingsViewModel>().Single().ToolSettingGroups.Single(g => g.Tool.Id == "client-converter");
            Assert.Equal(6, group.Settings.Count);                                       // internal values (signatures, extension list) are not shown
            group.Settings.Single(r => r.Label == "Skip empty sprites").BoolValue = true;
            Assert.True(converter.SkipEmpty);
            group.Settings.Single(r => r.Label == "Target client").Value = "client11";
            Assert.Equal("client11", converter.ProfileId);
            group.Settings.Single(r => r.Label == "Target client").Value = "860";          // not writable: refused
            Assert.Equal("client11", converter.ProfileId);
            return Task.CompletedTask;
        });
    }
}
