# Themes, Output Locations, Export Messages, Tools and Diagnostics – Tibia Foundry 6.0

Version 6.0 adds custom themes, one output folder structure under Documents, export messages that name and link the saved file, a registry that describes every tool, and a Diagnostics page. It also removes the earlier product name from everything the project controls (`13_Branding_and_Attribution.md` §10). How to add a tool is in `16_Adding_a_Tool.md`; test results are in `07_Testing_Report.md`.

## 1. Appearance & Themes

*Settings → Appearance & Themes* lists the themes; choosing one applies it at once and saves the choice.

| Theme | |
|---|---|
| **System** (default) | light or dark, following the Windows app mode; changes when Windows changes |
| **Light**, **Dark** | the original palettes, unchanged (`Themes/Colors.Light.xaml`, `Colors.Dark.xaml`) |
| **Custom themes** | made by the user: a built-in palette plus the colors the user changed |

The active theme has a check mark; custom themes with unsaved changes say so. Built-in themes cannot be edited, renamed or deleted.

**Making and managing themes.** *New theme…* copies the active theme (a custom one with its colors, or a built-in palette) under a name the user gives, and activates it. *Rename…* and *Delete…* (with confirmation) act on the active custom theme; deleting it activates the built-in theme. *Export…* writes a `.tftheme.json` file (Data exports folder by default); *Import…* adds a theme from such a file after checking every color — colors that are not valid, and names this version does not know, are listed and left at the base palette's values; a file that is not a theme is refused. At most 50 custom themes are kept; names are unique (a second "Ruby" becomes "Ruby (2)").

**Editing colors.** The color list shows the main colors in groups; *Show all colors* adds the shades that normally follow other colors. Selecting a color shows:

* the new color next to the base palette's color;
* a **hex** box (`#RRGGBB`, `RRGGBB`, `#RGB`; an opaque `#FFRRGGBB` is accepted). Text that is not a color is explained under the box and not applied; a complete valid value applies at once;
* **Hue** (0–359°), **Saturation** and **Lightness** (0–100 %) sliders, each over a gradient of the values it can take;
* *Reset this color* (back to the base palette's value, so it follows its sources again) and *Reset all colors…*.

**Colors that can be changed** (each is a resource key `App.<Key>` used by every style):

| Group | Colors |
|---|---|
| Backgrounds | main background, panels and cards, secondary panels, navigation sidebar, header and status bar, card titles, lists and grids, read-only fields |
| Text and links | primary, secondary and disabled text, links (including the saved-file link in the status bar) |
| Accent and buttons | accent, text on accent, accent hover/pressed/soft, button background, button text |
| Hover, selection and focus | hover, pressed, selected item and its text, inactive selection, navigation hover and selected entry, focus outline |
| Borders and separators | borders, separators, strong borders, button borders |
| Input fields | input background, input text, input border and its hover state |
| Success, warning and error | the three status colors and their soft backgrounds |
| Tooltips, other | tooltip background and text, scroll bars |

`ButtonText`, `InputBackground` and `InputText` were added in 6.0 with the values those elements already had, so the built-in look is unchanged. The definitions, groups and derivation rules are in `Core/Theming/ThemeTokens.cs`.

**Derived colors.** Most colors are made from a few others (for example the accent's hover shade, the selection and the links from the accent; borders and hover from the panel and text colors). A derived color is recomputed only when a color it is made from was changed and it was not changed itself; otherwise it keeps the base palette's hand-tuned value. A theme without changes is therefore exactly its built-in palette (tested for both palettes). Links and text on accent, buttons, inputs and selections are derived so that they stay readable (at least 4.5:1).

**Live application.** Changes apply to the whole interface immediately — every window, dialog, editor, list, preview background, progress bar and the status bar, since all styles use the palette through `DynamicResource`. Changing one color only recolors the brushes whose color changed (`ThemeEngine.UpdateColors`); no resource dictionary is replaced and nothing else is redrawn. Switching to a theme with another base palette replaces the palette once. Window title bars follow dark and light. No restart is needed.

**Save, revert, reset.** Edits are kept as *unsaved changes* (applied, but not written) until *Save theme*; *Revert* restores the saved theme's colors. Choosing another theme, pressing F6 or closing the program with unsaved changes asks first (save, discard or stay). *Restore original appearance…* makes the System theme active again; custom themes stay saved.

**Readability.** The editor lists problems as they appear (`Core/Theming/ThemeModel.cs`, `ThemeAccessibility`): primary text on every background, secondary text, links, text on accent, button and input text, selected items, the selected navigation entry and tooltips need 4.5:1; status colors, the focus outline and secondary text on the main background and navigation need 3:1; input borders 1.5:1; hover, selection and navigation states must remain distinguishable from their background (1.1:1); disabled text must differ from normal text (1.3:1). Problems are warnings, not blocks. Both built-in palettes pass every check (tested).

**Persistence.** The active theme and the custom themes are stored in the settings file (section `Themes`, settings format 4) and restored at start-up. Theme operations change only that section and the built-in theme choice. Loaded themes are repaired gracefully: colors that are not valid are dropped (and logged), names are made unique, duplicate ids are replaced, an active theme that no longer exists falls back to the built-in theme, and colors of tokens this version does not know (from a newer version) are kept. Version 5.0 keeps the `Themes` section unchanged when it saves (it keeps unknown top-level settings).

**Theme colors never reach exports.** Exports are produced by `TibiaFoundry.Core`, which has no access to the interface palette (checked by a test); preview backgrounds (checkerboard, magenta, black, white, gray) are fixed and are not theme colors either.

**F6 and the navigation button** switch between the built-in Light and Dark themes (opposite to the current brightness); an active custom theme stays saved and can be chosen again.

## 2. Output locations

Everything Tibia Foundry saves is organized under one root:

```
<Documents>\Tibia_Foundry\
```

`<Documents>` is the folder Windows reports for the current user (the known-folder API behind `Environment.SpecialFolder.MyDocuments`), so a Documents folder that was moved to another drive or into OneDrive is followed — on the development machine it is `F:\Users\Rocky\Documents`. No folder with a placeholder in its name is ever created. If Windows reports no Documents folder (for example under a service account), there is no default: every save explains that a folder must be chosen.

| Kind | Default subfolder | Used by |
|---|---|---|
| GIF exports | `GIF_Exports` | Appearances → Save as GIF, outfitter GIF, creature GIF (Static Data) |
| Image exports | `Image_Exports` | outfit sheets, house images, HR maps, sheets made and sliced by Advanced Tools |
| Sprite exports | `Sprite_Exports` | Extract Sprites, the sprite gallery |
| Sound exports | `Sound_Exports` | Sounds → Extract |
| Map exports | `Map_Exports` | Map Tools → extracted map tiles, Cyclopedia tiles |
| Data exports | `Data_Exports` | JSON exports, ID tables, Lua modules, decoded and downloaded asset files, exported settings and themes |
| RCC Editor | `RCC_Exports` | extracted resources, saved copies of resource files |
| Client Converter | `Client_Converter` | converted `Tibia.dat`/`Tibia.spr` |
| Assets Editor | `Assets_Editor` | saved copies of assets folders, exported images, object packages |
| Scripts | `Scripts` | files written by scripts (converted images, extracted archives, restored files) |
| Reports | `Reports` | diagnostics reports, comparison reports, script reports |
| Manifests | `Manifests` | file inventories and checksum manifests |
| Backups | `Backups` | copies of replaced and removed files (installed edition; the portable edition keeps them next to the program) |
| Logs | `Logs` | the log files (installed edition; portable: next to the program) |
| Temporary files | — | the Windows temporary folder (installed) or next to the program (portable): working files are short-lived and are better not synchronized by OneDrive; a folder can be chosen |

**Which folder is used.** For each kind: the folder chosen in *Settings → Files and folders*, then the folder last used for it (when *Remember the folders last used* is on, Settings → General), then its default subfolder of the root. The root itself can be changed; every kind without a folder of its own follows it.

**Creating and checking folders.** A folder is created when something is first saved in it (or, for "Save as" dialogs, when the dialog opens in it) — not at start-up, so unused kinds leave no empty folders. Before writing, the folder is checked (`OutputLocations.Prepare`): the path must be complete and valid (no characters or names Windows forbids), its drive or share must be available, it must not be a file, and a file must be creatable in it. The problem is explained in plain words ("The drive F:\ is not available. Connect it, or choose another folder.", "You do not have permission to write to …"). Automatic exports then offer to choose another folder for this operation; nothing is ever written to a different place without the user choosing it. Folders of the installed Tibia client are refused as destinations.

**Existing files** are never replaced silently: automatically named exports get a free name ("name 2.gif"); "Save as" dialogs ask before replacing; all other writes follow *Settings → File safety → When a file already exists* (ask, keep both, replace with backup, skip).

**Source and output stay apart.** Defaults are under Documents, never next to the inputs; the LZMA decoder and map, sound and sprite extraction no longer start in or accept the client's own folders.

**Settings and upgrades** (settings format 4). The kinds that had settings in 5.0 keep them (the 5.0 GIF, sound, map and sprite folders, the tools' output folders, backup, temporary and log folders); the new kinds are stored in `OutputFolders`. Every kind has *Change…*, *Use default* and *Open* in Settings, and is marked "default", "chosen by you" or "kept from the previous version". Upgrading from 5.0 (`AppSettings.MigrateOutputFolders`, `AppServices.MigrateFolders`):

* a root that was still the automatic 5.0 default (`Documents\TibiaSprites`) moves to `Documents\Tibia_Foundry`; a root the user chose is kept (new exports go into the new subfolders there);
* folders the user chose are kept; remembered "last used" folders that were only 5.0 default subfolders are forgotten;
* when the 5.0 default backup folder (`%LOCALAPPDATA%\TibiaFoundry\backups`) holds backups, it stays the backup folder, so they remain restorable ("kept from the previous version"; *Use default* switches to the new one);
* **no file is moved or deleted**; exports of earlier versions stay where they are. The start-up message says where new files go.

*Restore defaults for Files and folders* returns every kind to its default under the root.

## 3. Export messages

After a successful export the status bar shows, for example:

> ✓ Saved to F:\Users\Rocky\Documents\Tibia_Foundry\GIF_Exports\Gold Coin.gif

* The path is the file that was actually written (for automatic names, the name that was free), shown only after writing succeeded.
* The path is a **link**: underlined in the link color, with a hand cursor, a hover color and a tooltip with the full path. Long paths are shortened in the middle for display ("C:\Users\…\GIF_Exports\Gold Coin.gif"); the link always uses the exact path. Only the path is clickable.
* **Clicking** opens File Explorer at the folder with the file **selected**, through the Windows shell API (`SHOpenFolderAndSelectItems`). The path is passed as one value, never built into a command line, so spaces, quotes and Unicode characters are safe. If selecting is not possible, the folder is opened instead; if the file was moved away later, its folder is opened and the user is told; if even that fails, a separate message explains it — the export's success message stays.
* Several GIFs: "✓ Saved 12 GIFs to <folder>" (the folder opens). Some failed: a **warning** ("Saved 10 of 12 GIFs to <folder>; 2 failed"), with the failures listed. None saved: an **error** that names the reason, never a check mark.

The same behavior applies to the other exports: Appearances JSON, ID tables and Lua modules; outfit sheets and outfitter GIFs; Static Data JSON and creature GIFs; Map Tools JSON, lists, tiles, HR maps and Cyclopedia tiles; House Images PNG and JSON; Sounds JSON and extraction; Extract Sprites; the sprite gallery (also in its own footer); Compare reports; Advanced Tools sheets, slices, decoded and downloaded files; Assets Editor sprites, sheets, packages, images and saved folders; RCC Editor extraction and saves; Client Converter output (the written `Tibia.dat`); script reports and outputs; exported settings and themes; diagnostics reports. Batches report honestly (`ReportBatch`): success only when everything was written.

## 4. Tool registry

Every page and every script is described by a `ToolManifest` (`Core/Tools`): identity, type, version, navigation group, capabilities per operation, accepted inputs and produced outputs, default output kind, declared settings with their version and migrations, dependencies (required or optional, per operation, with minimum versions and a resolution text), platform requirements, license and attribution, documentation and limitations. `ToolCatalog` validates manifests and checks dependencies through probes; `ToolSettingsStore` stores validated per-tool settings. The pages are registered from `App/Services/BuiltInTools.cs` (16 in 6.0; 18 since 7.0 added the Item Catalog and Item Discovery; 7.3 added Clients; 8.0 removed Item Discovery and added the Client Installation Rebuilder), the 12 scripts from their own declarations. Details, the example and the rules are in `16_Adding_a_Tool.md`.

What the application derives from the manifests:

* **navigation** (name, icon, group, order) and the view templates;
* **capability-dependent controls**: the Cancel buttons appear only for operations that declare safe cancellation (for example RCC and Assets Editor saves cannot be cancelled; exports and scripts can);
* **dependency checks** before an operation starts, a banner on the page (*Some features are unavailable: …* or what blocks the tool) and the list in *Diagnostics → Tools*;
* **Settings → Tools**: generated, validated controls for each tool's declared settings, with *Restore defaults* per tool. The Client Converter's options are now declared settings (stored under the same keys as in 5.0, so nothing is lost);
* the **operation history**, logging scope, success and failure messages through the shared `RunAsync`;
* the license and attribution shown in Diagnostics and in reports (the RCC Editor, Client Converter and Assets Editor manifests name the open-source projects they were informed by, without code from them).

Invalid manifests are rejected with their problems and a page whose view-model cannot be created is left out; the program starts either way. Only built-in tools are loaded.

## 5. Diagnostics

*Application → Diagnostics* (also the chart icon in the status bar).

**Operations.** Queued and running operations with their tool, state, current step (as the operation reports it), output, progress as items done of a reported total (or "this operation reports no total"), elapsed time, rate, warnings and errors. The remaining time is shown, marked as an estimate, only after at least 3 seconds and 2 % of the work, from the measured rate; otherwise "—". *Cancel…* (with confirmation) appears only for operations that can be cancelled safely; a cancelled operation removes its temporary files (leases are disposed) and leaves no half-written output (outputs are written atomically). Several operations can run at once (Settings → Performance); further ones are shown as queued. The history keeps finished operations (completed, completed with warnings, failed, cancelled) with duration, waiting time, item counts, output and the first problem, across restarts (`operations.json` next to the settings, bounded by *Settings → Diagnostics*); *Output* shows the result in File Explorer and *Log* shows the operation's log lines.

**Performance.** Measured by Windows and .NET for the whole process: processor use (average since the previous measurement), working set, private memory, managed memory and garbage collections, threads and handles, running time, running and waiting operations, recent failures, the average rate of recent operations, and the free space of every drive that holds an output folder (an unavailable drive is reported as such). Per-operation processor and memory use is shown as *not available* — operations share one process and cannot be measured apart reliably. Measurements are taken only while the page is open, every 1–30 seconds (default 2), and can be turned off; nothing is sampled in the background.

**Log.** All tools and scripts write to one log (`Core/Diagnostics/AppLog.cs`): `yyyy-MM-dd HH:mm:ss.fff [Level] [Thread] [Tool] [op:id] message`, with exception details on the following lines. Levels: Debug, Info, Warning, Error, Critical (Critical marks failures that stop the program). Operations log their start (and waiting time), end, duration and item counts; dependency failures, file problems and cancellations are logged with their context. The page filters by period, minimum level, tool, operation and text (the newest 2,000 matches), shows an entry's details and opens the log folder. Rotation and retention: one file per day (a session past midnight continues in the next day's file); a day's file stops at the size set in Settings (20 MB by default); at start-up files older than the retention (14 days) are removed, then the oldest until all files together are below the total size (200 MB by default); today's file is kept. The same message repeated within 10 seconds is written once, followed by "repeated N more time(s)". Passwords, tokens, keys, session ids, authorization headers, private keys and secret URL parameters are masked before anything is written (`LogRedactor`). Log lines written before the log folder is known (while settings load) are kept in memory and written then.

**Self-check** (`App/Services/HealthCheck.cs`). Checks the settings file and format, settings of other versions, upgrade notes, whether settings are being saved, the active theme and its readability; the Documents folder; **every output folder** (valid, drive available, writable, or "created when needed"); remembered folders that no longer work; the log folder; every registered tool and its dependencies (blocking ones are errors, others warnings or information); and the main components in memory (ImageMagick GIF encoding, LZMA, zlib, protocol buffers, the theme palettes). **Errors** prevent an operation from working; **warnings** point at a possible problem. Fixes (*Create folder…*, *Use the default folder…*) are offered and applied only after confirmation; checking never repairs anything. The command-line `--self-test` runs the same check (read-only).

**Tools.** Every registered tool: type, version, status, capabilities, dependencies and their state, formats, license; rejected manifests with their problems.

**Report.** *Generate a diagnostics report*: choose the categories (summary and system information are always included; settings, tools and dependencies, self-check, recent operations, performance and logs can be left out), then **Preview contents**: the report is collected and sanitized, and every file is listed and can be read before anything is saved. *Save report…* writes exactly the previewed files, plus `manifest.json` (file list with sizes and SHA-256, whether it is sanitized, "transmitted: false"), as `TibiaFoundry-diagnostics-<date>-<time>.zip` in the Reports folder; the status bar links to it. Changing an option after the preview requires a new preview. Contents: `summary.txt` (readable overview: versions, edition, client, tools, self-check problems, recent operations, warnings and errors of the session, the file list), `system.json` (Tibia Foundry, Windows, .NET, architecture, processors, memory, bundled library versions, the *names* of the command-line options only), `settings.json`, `tools.json`, `selfcheck.json`, `operations.json`, `metrics.json`, `logs/*.log` (the last 1–14 days, default 3, at most 16 MB).

## 6. Privacy of diagnostics reports

By default (`Core/Diagnostics/DiagnosticsReport.cs`, `DiagnosticsSanitizer`):

* passwords, API keys, tokens, session ids, authorization headers, private keys and secret URL parameters are masked;
* personal folders become placeholders: `%USERPROFILE%`, `%DOCUMENTS%`, `%DESKTOP%`, `%APPDATA%`, `%LOCALAPPDATA%`, `%TEMP%`, `%ONEDRIVE%`, `%OUTPUT%` (the output root) and `%PROGRAM%`; other absolute paths keep only their drive and last two names (`D:\…\Tibia\assets`), network paths lose their server name;
* the Windows user name becomes `<user>`, the computer name `<computer>`, e-mail addresses `<e-mail>`;
* JSON files are sanitized value by value (paths inside JSON are escaped and would otherwise be missed);
* the **creator name** is left out of the settings; **environment variables** are not included; the command line is reduced to the names of its options;
* **no file contents**: no exported graphics, source assets, client files, documents or personal files; logs never contain file contents;
* nothing is uploaded or sent: the report is a local file, and the code that builds it uses no network API (checked by a test).

*Include full folder paths, user name and computer name* is an explicit opt-in for a single report: it asks for confirmation, explains what it adds, is off again next time, keeps masking secrets, and the manifest and summary record that it was used.

## 7. Settings added in 6.0

| Setting | Default | Where |
|---|---|---|
| Custom themes, active custom theme | none, System theme | Settings → Appearance & Themes (`Themes`) |
| Output root | `Documents\Tibia_Foundry` | Settings → Files and folders (`OutputFolder`) |
| Folders of the new output kinds | defaults under the root | Settings → Files and folders (`OutputFolders`) |
| Per-tool settings | declared defaults | Settings → Tools (`ToolState`, "tool.key") |
| Performance monitoring, interval | on, 2 s | Settings → Diagnostics, Diagnostics → Performance |
| Operations kept in the history | 200 | Settings → Diagnostics |
| All log files at most | 200 MB | Settings → Notifications and logging (7.2; with the other log limits, and reset with them) |
| Days of logs in a report | 3 | Settings → Diagnostics |

Each category has *Restore defaults*. Settings format 4 is written; 5.0 reads it, keeps the new sections and keeps working with its own options (`10_baseline_5.0.0`).

## 8. Files

* **Core**: `FileOps/OutputLocations.cs`; `Theming/ThemeColor.cs`, `ThemeTokens.cs`, `ThemeModel.cs`; `Tools/ToolManifest.cs`, `ToolCatalog.cs`, `ToolSettingsStore.cs`; `Diagnostics/AppLog.cs` (rewritten), `LogTools.cs`, `OperationTracker.cs`, `DiagnosticsReport.cs`; `Settings/AppSettings.cs` (format 4, migration); `Scripts/Script.cs` (manifests).
* **App**: `Themes/ThemeManager.cs` (engine), `Colors.*.xaml` (three new colors), `Styles.xaml`; `Services/BuiltInTools.cs`, `OutputService.cs`, `HealthCheck.cs`, `ResourceMonitor.cs`, `DiagnosticsCollector.cs`, `StatusService.cs` (links), `SystemServices.cs` (shell), `AppServices.cs`, `AppPaths.cs`, `SelfTest.cs`; `Infrastructure/PageTemplates.cs`; `ViewModels/PageViewModel.cs` (manifest, `RunAsync`), `ThemeEditorViewModel.cs`, `DiagnosticsViewModel.cs`, `SettingsViewModel.cs`, `MainViewModel.cs` and every page that saves files; `Views/Pages/DiagnosticsPage.xaml`, `SettingsPage.xaml`, `MainWindow.xaml`, `SpriteGalleryWindow.xaml.cs`, `UiSnapshot.cs`.
* **Tests**: `ThemeTests`, `OutputLocationTests`, `ExportStatusTests`, `ToolArchitectureTests`, `DiagnosticsTests`, extended `BrandingTests`, and updates of existing tests.

No package was added or changed.

## 9. Limitations

* **Explorer selection** was verified on this computer (Windows 11) with a real Explorer window (opt-in test `ExplorerOpensTheFolderWithTheFileSelected`). Other shells or policies that block `SHOpenFolderAndSelectItems` fall back to opening the folder.
* **Per-operation CPU and memory** are not measured (shared process); only process-wide values are shown.
* **Remaining time** is only as good as an operation's progress reports; operations that report no total have no estimate.
* **Temporary files** of the installed edition stay in the Windows temporary folder by default rather than `Documents\Tibia_Foundry\Temporary`; a folder can be chosen.
* **Theme contrast checks** cover the main pairs, not every combination of every custom color; they warn but do not prevent choices.
* **Sanitization** recognizes the known personal folders, the user and computer names, e-mail addresses and common secret formats. Names typed into free text (for example a theme name or a script parameter) are not recognized as personal unless they match these; review a report before sharing it.
* **Third-party plugins** are not supported (by design); external programs can only be declared as dependencies.
