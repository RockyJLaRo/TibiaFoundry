# Adding a Tool – Tibia Foundry 6.0

Every page of Tibia Foundry and every built-in script is a **tool**: a manifest that describes it, a view-model that does the work and a view that shows it. The application builds the navigation, the settings controls, the dependency checks, the operation history, the Diagnostics pages and the capability-dependent controls from the manifests. Adding a tool therefore means writing three files and adding **one line**; nothing else in the application has to change.

The example below is the *Word Counter* sample. The tests register the same manifest with a test view-model, run it through the shared infrastructure and check every point of this guide (`src/TibiaFoundry.Tests/ToolArchitectureTests.cs`).

## 1. The parts

| Part | What it is | Where |
|---|---|---|
| Manifest | `ToolManifest`: identity, capabilities, operations, inputs and outputs, settings, dependencies, platform, license, documentation | a static property, usually in `App/Services/BuiltInTools.cs` |
| View-model | a `PageViewModel` subclass that receives the manifest | `App/ViewModels/` |
| View | a WPF `UserControl` | `App/Views/Pages/` |
| Registration | `PageRegistration.For<TViewModel, TView>(manifest, factory)` in `BuiltInTools.Pages` | `App/Services/BuiltInTools.cs` |

Scripts (Utilities → Scripts) are smaller: a `Script` subclass listed in `Core/Scripts/ScriptRegistry.cs`. Their manifests are built from what they declare (`Script.Manifest`), see §8.

## 2. A minimal tool

### Manifest

```csharp
public static ToolManifest WordCounter { get; } = new()
{
    Id = "sample.word-counter",                 // unique, lower case, dots or hyphens
    Name = "Word Counter",
    Description = "Counts the words of text files",
    Type = ToolType.Utility,
    Version = "1.2",
    Category = BuiltInTools.UtilitiesGroup,      // navigation group (see the group constants in BuiltInTools)
    Glyph = "",                            // Segoe Fluent icon
    Capabilities = ToolCapabilities.Progress | ToolCapabilities.Batch,
    Operations = [new ToolOperation("count", "Count words", ToolCapabilities.Progress | ToolCapabilities.Batch)],
    Inputs = [new DataFormat("Text files", [".txt"])],
    Outputs = [new DataFormat("Report", [".csv"])],
    DefaultOutput = OutputKind.Reports,          // Documents\Tibia_Foundry\Reports
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
```

### View-model

```csharp
public sealed partial class WordCounterViewModel(AppServices services) : PageViewModel(services, BuiltInTools.WordCounter)
{
    public int Counted { get; private set; }

    [RelayCommand]
    private async Task CountAsync()
    {
        string? file = Services.Dialogs.OpenFile("Text file", "Text files|*.txt");
        if (file == null) return;
        int minimum = (int)Services.ToolSettings.GetLong(Manifest, "minimumLength");
        await RunAsync("Counting words", async (progress, ct) =>
        {
            string text = await File.ReadAllTextAsync(file, ct);
            Counted = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => w.Length >= minimum);
            progress.Report(new OperationProgress(1, 1, Path.GetFileName(file)));
        }, operation: "count", onSuccess: () => Services.Status.Show($"{Counted} words.", StatusKind.Success));
    }
}
```

### View

```xml
<UserControl x:Class="TibiaFoundry.App.Views.Pages.WordCounterPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="16">
        <Button Content="Count words…" Style="{StaticResource PrimaryButton}" Command="{Binding CountCommand}" HorizontalAlignment="Left" />
        <TextBlock Text="{Binding Counted}" Margin="0,10,0,0" />
    </StackPanel>
</UserControl>
```

Use the shared styles and `DynamicResource App.*` colors (see `Themes/Styles.xaml` and `Core/Theming/ThemeTokens.cs`), never fixed colors, so the tool follows every theme, including custom ones.

### Registration (the one line)

```csharp
PageRegistration.For<WordCounterViewModel, WordCounterPage>(WordCounter, (s, _) => new WordCounterViewModel(s)),
```

Add it to `BuiltInTools.Pages` where the tool should appear in the navigation. The view-model → view template is created from the registration (`Infrastructure/PageTemplates.cs`); no XAML resource file has to be edited.

## 3. How registration works

1. `MainViewModel` takes the registrations in order. Each manifest is **validated** (`ToolCatalog.Validate`): id format and uniqueness, name, description, type, version (`1.0` … `1.2.3.4`), license and attribution, platform, unique operation ids, settings (valid keys, labels, choices, ranges and defaults), settings migrations (versions in range, no duplicates) and dependencies (ids, names, a resolution text, operations that exist, valid minimum versions).
2. A manifest with problems is **rejected**: the tool is left out, the problems are logged and shown in *Diagnostics → Tools* and in the self-check, and the rest of the program starts normally.
3. The tool's stored settings are **upgraded** to its settings version (`ToolSettingsStore.Migrate`).
4. The view-model is created. If its constructor throws, the page is left out (logged, listed in `MainViewModel.LoadFailures`); the program still starts.
5. Registering the same manifest object again (a second window on the same services) is not a conflict; another manifest with the same id is rejected.

**Trust boundary.** Only tools compiled into Tibia Foundry are registered. There is no plugin folder: a folder that contains a manifest is never read or executed, and no code is downloaded. An external program a tool needs is declared as a `DependencyKind.Executable` dependency; it is never downloaded, installed or started without the user's explicit consent, and its absence only disables the operations that need it.

## 4. Declaring what the tool does

| Declaration | Effect in the application |
|---|---|
| `Capabilities` / `Operations[].Capabilities` | `Cancellation`: the status-bar Cancel button and the Cancel button on *Diagnostics → Operations* appear only for operations that declare it. `Progress`, `Batch`, `Preview`, `DryRun`, `Undo`, `Backups`, `Validation` are listed in Diagnostics and in reports. |
| `Inputs`, `Outputs` | listed in *Diagnostics → Tools* and in diagnostics reports |
| `DefaultOutput` | the output kind whose folder the tool uses by default (Settings → Files and folders) |
| `Settings` | validated per-tool settings with generated controls in *Settings → Tools* (Boolean → check box, Choice → list, Integer/Text → text box with validation message); *Restore defaults* per tool. `Visible = false` keeps a value out of Settings (for values the page remembers by itself). |
| `SettingsPrefix` | key prefix in the settings file (default `"<Id>."`). Tools that stored options before the registry existed keep their prefix, as the Client Converter does with `ClientConverter.`. |
| `SettingsVersion`, `SettingsMigrations` | stored settings of an older version are upgraded once, in order; settings written by a newer version are left unchanged |
| `Dependencies` | checked before every operation (§5) and shown as a banner on the page and in Diagnostics |
| `Platform` | minimum Windows version and 64-bit requirement, checked like a dependency |
| `License` | name, attribution, SPDX id and address, shown in Diagnostics and reports. Third-party code that is bundled must also be added to `THIRD-PARTY-NOTICES.txt` (`tools/release/make_notices.py`). |
| `Documentation`, `Limitations` | shown in Diagnostics; keep them honest (do not declare formats or versions the tool does not handle) |

## 5. Dependencies

Each dependency has an id, a name, a kind (`ClientData`, `Library`, `Executable`, `Service`, `Runtime`, `Platform`), `Required`, the operations it applies to (`RequiredFor`, empty = the whole tool), an optional `MinimumVersion` and a **resolution** text that tells the user what to do.

The check itself is a **probe** registered by the application: `services.Tools.AddProbe("id", () => new DependencyProbeResult(DependencyState.Available, "2.0"))`. The built-in probes are in `AppServices.RegisterDependencyProbes` (`tibia-client`, `imagemagick`, `naudio`, and `tibiawiki`/`tibiamaps`, which are *not checked* in advance because Tibia Foundry contacts no web service without a user action).

* A **required** dependency that is missing or older than `MinimumVersion` blocks the operations it applies to: `RunAsync` does not start, the status bar and a message name the dependency and its resolution, and nothing is recorded as run.
* An **optional** dependency that is missing only produces the page banner ("Some features are unavailable: …").
* A dependency without a probe is reported as *not checked*, never as available. A probe that throws counts as missing; it cannot take the program down.

## 6. Running work: progress, cancellation, logging, errors, files

Use `RunAsync` for anything that can take more than a moment:

```csharp
await RunAsync("Exporting", async (progress, ct) => { … }, operation: "export", output: folder,
    onSuccess: () => Services.Status.ShowSaved(file, isFile: true));
```

It gives every tool the same behavior:

* **Dependency check** of the operation (§5).
* **Queueing**: waits for a free slot (Settings → Performance); shown as *Queued* on *Diagnostics → Operations*.
* **Progress**: report `OperationProgress(done, total, message)` with real counts; the status bar, the taskbar button and Diagnostics show it. Do not invent percentages; report `total = 0` when the total is unknown. The remaining time is estimated by Diagnostics only from measured progress.
* **Cancellation**: call `ct.ThrowIfCancellationRequested()` between items and pass `ct` on. Declare `ToolCapabilities.Cancellation` only if stopping leaves outputs consistent. Use `Services.Temp.Begin(…)` for working files (the lease is removed when disposed, also on cancellation) and `Services.CreateOutput(…)` / `AtomicFile` for results (written to a temporary file and moved into place, so a cancelled or failed write leaves nothing half-written).
* **Logging**: every log line written during the operation carries the tool's name and the operation's id (`AppLog.BeginScope`), so *Diagnostics → Log* can filter by tool and operation. Use `AppLog.Info/Warn/Error` with plain messages; secrets are masked automatically, but do not log file contents or credentials.
* **Errors**: exceptions are logged with details, the status shows the error, and (Settings → Notifications) a message explains it. A `DataFileException` gets a plain-words explanation with advice.
* **Outcome**: `onSuccess` sets the final status. Name what was actually written with `Services.Status.ShowSaved(path, isFile)`: the status bar then shows "✓ Saved to <path>" with the path as a link that opens File Explorer with the file selected. Use `ReportBatch` for batches: it never shows a success when something failed.
* **History**: the operation's outcome, duration, counts and output are kept in the operation history (Diagnostics), across restarts.

For files the user chooses, use the shared helpers: `Services.Outputs.SaveFile(kind, …)` (starts in the kind's folder, remembers the chosen folder), `Services.Outputs.PickFolder(kind, …)`, and `Services.Outputs.PrepareOrAsk(kind, …)` for automatic names (creates the folder, explains a folder that cannot be used and lets the user choose another — never another folder silently). Refuse folders of the installed client (`AssetsProject.IsInsideClientInstallation`).

## 7. Tests and documentation

For a new tool, add at least:

* a test that its manifest validates (`EveryBuiltInPageAndScriptHasAValidUniqueManifest` covers every entry of `BuiltInTools.Pages` automatically);
* tests of the tool's core logic in `TibiaFoundry.Core` (no window needed);
* a view-model test that runs its operations through `RunAsync` with `TestData.PathsIn(dir)` services and a `FakeDialogs` (see `ToolArchitectureTests`), so nothing reaches the user's real folders;
* if it writes files: that the status names the written file, that existing files are not replaced silently, and that a cancelled run leaves no partial output;
* a section in `docs/14_Tools_Settings_and_Scripts.md` or its own document, and an entry in the CHANGELOG.

The UI snapshot (`--ui-snapshot`) visits every registered page automatically and reports binding errors.

## 8. Scripts

A script is a `Script` subclass (id, name, description, category, parameters, `Run`, and `Apply` for scripts that change files) listed in `ScriptRegistry.CreateAll`. Its manifest (`script.<id>`) is derived from what it declares: progress, cancellation and batches always; preview, dry run and backups when it changes files; extra capabilities (`ExtraCapabilities`, e.g. `Undo`), dependencies (`Dependencies`, e.g. ImageMagick for the image converter) and the default output kind (`DefaultOutput`, e.g. Manifests for checksum manifests) can be overridden. The Scripts page registers every script's manifest, checks its dependencies before it runs, and runs it through `RunAsync` like any other tool. Scripts never run by themselves and cannot run arbitrary code.
