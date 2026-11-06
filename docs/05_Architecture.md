# Architecture – Tibia Foundry 8.0

## 1. Solution layout

```
src/
  TibiaFoundry.slnx              solution (Core, App, Tests)
  Directory.Build.props            shared settings: net10.0-windows (8.0; 7.0–7.3 targeted 10.0.19041.0 for the removed Item Discovery), nullable, analyzers, version 8.0.0
  .editorconfig                    CRLF line endings, indentation
  Directory.Packages.props         central package versions
  global.json                      .NET SDK 10.0.100 (latestFeature roll-forward)
  TibiaFoundry.Core/             UI-free library: formats, rendering, exports
  TibiaFoundry.App/              WPF application (MVVM)
  TibiaFoundry.Tests/            xUnit v3 tests (unit, parity, view-model flows, resources)
tools/ParityHarness/               net48 x86 program that drives the ORIGINAL exe to produce reference outputs
04_reference/                      reference outputs + pinned client data they came from (reference-client.json)
```

About 46,000 lines of C# and XAML in the program and 10,800 lines of tests (excluding generated protobuf code).

## 2. Layers

```
+--------------------------------------------------------------------------------+
| Views (XAML pages, dialogs, gallery window)          TibiaFoundry.App/Views    |
|   bind to                                                                        |
| ViewModels (one per page + panels)                   TibiaFoundry.App/ViewModels|
|   use                                                                            |
| Services: ClientDataService, StatusService, DialogService, ClipboardService,     |
|           ShellService, AudioPlayer, OperationGate, NotificationService,         |
|           6.0: OutputService, BuiltInTools (page registrations), HealthCheck,    |
|                ResourceMonitor, DiagnosticsCollector, ThemeManager/ThemeEngine,  |
|           7.0: LocationService; 7.3: ClientLibrary;                              |
|           8.0: ClientStartup (setup, start-up choice), LauncherActivity          |
|           AppServices (composition root)                                         |
|   call                                                                           |
| Core: Assets, Compression, Sprites, Appearances, Imaging, StaticData, Map,       |
|       Houses, Sounds, Compare, Export, Wiki, Settings, Diagnostics, Protobuf,    |
|       Compatibility (format facts, checks), Net (HTTP), Common (atomic files),   |
|       Metadata (creator attribution in PNG, GIF and text exports),               |
|       5.0: FileOps (file safety), Rcc, Legacy (dat/spr, converter),              |
|            Editing (Assets Editor), Scripts                                      |
|       6.0: Theming (colors, tokens, custom themes), Tools (manifests, registry,  |
|            per-tool settings), FileOps.OutputLocations, Diagnostics (log,      |
|            operations, reports)                                                 |
|       7.0: Locations (client and data locations), Items (item catalog, Open     |
|            Tibia readers, comparison), FileOps.XlsxWriter                        |
+--------------------------------------------------------------------------------+
```

* **Core** has no WPF dependency. Images are `RgbaImage` (managed ARGB buffer); GDI+ is used only for PNG/BMP codecs, and ImageMagick only for GIFs. Everything in Core is tested directly against the original's reference outputs.
* **App** follows MVVM with CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`):
  * Views contain no logic beyond view concerns: syncing multi-selection, double-click, and keyboard shortcuts.
  * Each page view-model derives from `PageViewModel`, which receives the page's `ToolManifest` (name, icon, group, capabilities, dependencies) and provides `RunAsync` (dependency check, queueing, busy state, progress, capability-dependent cancellation, logging scope, operation history, success and error messages) and `Try` (quick actions).
  * The pages are registered in `Services/BuiltInTools.cs` (manifest, view-model type, view type, factory). `MainViewModel` validates and registers each manifest in `AppServices.Tools`, upgrades the tool's stored settings, and creates the page; `Infrastructure/PageTemplates.cs` creates the view-model → view templates from the same registrations. A rejected manifest or a page that fails to construct is left out without stopping start-up. See `16_Adding_a_Tool.md`.
* **Branding**: `Assets/Branding` holds the official 32×32 logo (shown with nearest-neighbor scaling) and the icon generated from it; `Themes/BrandIcons.xaml` holds the vector link icons of the About page.
* **Attribution at run time**: `App.OnStartup` sets `Attribution.CurrentCreator` from the settings. `AppServices.SetCreatorName` changes it (Settings page) and raises `CreatorChanged` for the previews. Imports that already name another creator go through `PageViewModel.AttributionForImports`, which asks the user once per operation (`IDialogService.ChooseAttribution`).
* **Composition root**: `App.OnStartup` creates `AppServices` once (settings, data, status, dialogs, clipboard, shell, audio, HTTP; 6.0: output folders, the tool catalog with its dependency probes, per-tool settings, the operation tracker), then `MainViewModel` (navigation from the registrations, theme, package switch) and `MainWindow`. Pages talk to each other through `AppServices` events (`RequestOpenAppearance`, `RequestOpenOutfitter`, `RequestNavigate<T>`, `ToolSettingsChanged`).

## 3. Core modules

| Module | Responsibility |
|---|---|
| `Assets` | `TibiaClientPaths` (live/test packages, sounds folder, client version); `AssetCatalog` (tolerant `catalog-content.json` parser that records problems, sprite sheet index, lookup by sprite ID, file fingerprints) |
| `Compression` | `LzmaDecoder` (array-based LZMA SDK port); `CipLzma` (CIP container); `LzmaFileTool` (decode and download tools) |
| `Sprites` | `SpriteSheetDecoder` (BMP → sprites; checksum of the decompressed sheet; grid derived from the image and validated against the catalog); `SpriteRepository` (folder/client source, LRU of 96 decoded sheets, write-through "extract on the fly", rejects folders from other client versions); `SpriteExtractor`; `SpriteFolderManifest` (`TibiaFoundry-sprites.json`) |
| `Protobuf` + `Protos/*.proto` | Schemas recovered from the original's embedded descriptors, compiled at build time by Grpc.Tools. `ProtoLoader` repairs Latin-1 strings that are not valid UTF-8 (descriptor-driven wire rewrite) |
| `Appearances` | `AppearanceDatabase` (lists, lookup, currencies, preview sprite); `AppearanceProperties` (data panel rows); `AppearanceFilter` (criteria + 4 modes); `GameTables` (names, palettes, automap colors) |
| `Imaging` | `OutfitRenderer` (template coloring with ImageMagick-exact multiply and 16-bit quantization, addons, mounts); `AppearanceGifBuilder`; `GifWriter`; `SheetComposer`; `ImageIO`; `BmpDecoder` |
| `StaticData`, `Map`, `Houses`, `Sounds` | Loading, list rows and exports per data file. `MapImageTools` (tile extraction, HR map, watermark, Cyclopedia crop); `HouseRenderer` |
| `Compare` | `DatComparer` (file/folder comparison per category); `JsonDiff` (original text format) |
| `Export`, `Wiki` | JSON exports with the original property names; NPC trade Lua; wiki item IDs (MediaWiki API first, render fallback, 24 h cache, stale cache with notice, structure-based parser with plausibility check); NLS-compatible name sorting (`WikiNameComparer`) |
| `Settings` | `AppSettings` (validated by `Normalize`; unknown options from newer versions kept in `UnknownSettings`); `SettingsStore` (atomic save, corrupt-file backup, newer-version backup, 1.x import) |
| `Compatibility` (2.1) | `TibiaFormat`: every client-layout fact in one place. `CompatibilityChecker`: version, integrity, schema drift, load and validation per file → statuses and affected features. `SchemaInspector`: protobuf wire walk against the compiled schema. `DataValidation`: record checks. `DataFileException`: missing, unreadable or undecodable, with advice. See `10_Tibia_Compatibility_Report.md`. |
| `Net` (2.1) | `HttpFetcher`: the only HTTP code. GET, per-request timeout, bounded retries with backoff, `Retry-After`, size cap, atomic download with optional verification, browser-challenge detection. `ExternalEndpoints`: every external address. |
| `Common` | `AtomicFile` (unique temp file + move; used for every output); `FileNames.Sanitize` (device names, length); progress and `BatchResult`; `AppIdentity` (name "Tibia Foundry", identifier "TibiaFoundry", creator, website, version, and the legacy names read for migration) |
| `Metadata` (since 3.0) | Creator attribution in exported files. `Attribution`: the policy, i.e. the configured creator, the required sentence, new versus derived assets, conflict review, validation. `PngMetadata` (text chunks and XMP), `GifMetadata` (comment and XMP application extension), `XmpPacket`, `AttributionReader` (reads existing attribution before re-export), `TextAttribution` (Lua and report headers). `ImageIO` and `GifWriter` embed it in every file; batches pass one `AttributionInfo`, built and verified once. See `13_Branding_and_Attribution.md`. |
| `Diagnostics` | `AppLog` (6.0: one file per day with day rollover; level, source and operation id per line; Critical level; secret masking (`LogRedactor`); repeated-message suppression; retention by age and total size; lines before initialization kept in memory). `LogReader` (parses this and the 5.0 format; filters). `OperationTracker` (queued, running, finished operations; measured progress, estimates only from measured rates; bounded persistent history). `DiagnosticsReport` (`DiagnosticsSanitizer`, report entries, ZIP with manifest) |
| `Theming` (6.0) | `ThemeColor` (hex parsing and validation, HSL, WCAG contrast), `ThemeTokens` (the customizable colors, groups and derivation rules), `ThemeResolver` (base palette + changed colors → full palette; derived colors follow changed sources), `ThemeAccessibility` (readability checks), `ThemeSettings`/`CustomTheme` (stored themes, repair on load), `ThemeFile` (export/import) |
| `Tools` (6.0) | `ToolManifest` (identity, capabilities per operation, formats, default output, settings and migrations, dependencies, platform, license, documentation), `ToolCatalog` (validation, registration, dependency probes, availability), `ToolSettingsStore` (validated per-tool settings in `ToolState`, versioned, migrations) |
| `FileOps.OutputLocations` (6.0) | The output kinds and their `Documents\Tibia_Foundry` subfolders; Documents resolved through Windows; folder checks and on-demand creation with plain-language problems; display shortening of long paths |
| `FileOps` (5.0) | Shared file safety for every tool: `WindowsNames` (7.2: the one set of Windows name rules: characters, device names, trailing dot or space; used by `FileNames`, `OutputLocations`, `PathGuard` and item catalog folders), `PathGuard` (path traversal, device names, links), `OutputSession` (existing-file policy, protected inputs, temp-write → validate → replace, backups), `BackupStore` (SHA-256-verified backup sets, restore, retention), `TempWorkspace` (per-operation leases), `FileSignatures`/`FileInspector` (content identification with the depth of the check), `FileHashing`, `FileScanner` (no link following), `ReportWriter` (CSV/JSON/text, formula-safe CSV). See `14_Tools_Settings_and_Scripts.md` §1. |
| `Rcc` (5.0) | Qt resource files: `RccArchive` (formats 1–3, zlib, Zstandard kept as is; edit, rebuild with Qt's hash order), `RccValidator` (structure + a port of Qt's `findNode` lookup), `RccOperations` (extract with folders and manifest, update from folder, create from folder) |
| `Clients` (7.3) | The client store. `ContentStore` (content-addressed objects `Shared_Assets\Objects\<2 hex>\<SHA-256>`, read-only; write to `Incoming`, hash twice, rename without replacing; mark-and-sweep cleanup through `Trash`; crash recovery; the writer lock), `ClientManifest` (one complete file list per version: client path, kind, SHA-256, size; the fingerprint is its ID; `ManifestDiff`), `ClientRegistry` (`client-registry.json`: registrations, versions, labels; rebuilt from the manifests when damaged), `ClientScanner` (the file set from the client's `assets.json`, the RCC files from `package.json` and their `qres` header; personal folders never; hashing checked against the client's own hashes), `ClientStore` (register, import all-or-nothing, remove, reclaim, verify, export, move), `ClientFiles` (`IClientFolder`: a real folder, or an imported version's names → objects). `StoreFiles` is the only class that writes, and only inside the store (architecture test). See `20_Client_Management.md`.<br>**8.0** adds:<br>• `ClientInventory`: the complete inventory of a folder (every file and empty folder, personal data left out visibly, two listings plus the client's hashes for consistency);<br>• `FolderInspector`: class, capabilities per tool, no hashing;<br>• `VersionEvidence`;<br>• `InstallationFinder`: packages, uninstall entries, Steam libraries;<br>• `ClientStore.Archive`: complete-installation and standalone scopes; shared or independent storage, read back before publishing; leases; per-revision accounting;<br>• `ClientStore.Rebuild`: staging `.tf-partial-`, verify, rename into place, never overwrite;<br>• `ClientMonitor`: baselines and the launch check, Full or Quick.<br>Manifests are format 2. See `21_Client_Archiving_and_Rebuilder.md`. |
| `Locations` (7.0) | `LocationResolver`: every client file and folder (packages, package, version file, assets, data files, sounds, resource file, options file, extracted sprites) per client; chosen → inside a chosen folder → automatic → not found; quick and deep checks; which compatibility components a change affects. 7.3: an imported version as a fourth source (its files through the manifest; the options file and screenshots stay the installed client's). See `18_Client_Locations.md`. |
| `Items` (7.0) | `ItemModel` (identifier types, sources, records, mappings, agreement, name comparison), `ItemCatalog` (entries and the settled name, queries, import previews), `ItemCatalogStore` (catalog folder: `catalog.json`, gzip snapshots, the `observations.jsonl` log), `ItemImporters` (client, TibiaWiki, name lists, CSV, catalog JSON, `items.xml`), `OtbFile` (`items.otb` reader), `CatalogComparison`, `ItemExports`. See `17_Item_Catalog.md`. |
| `FileOps.XlsxWriter` (7.0) | A minimal Open XML workbook writer (inline strings, numbers, frozen bold header, filters, an About sheet; never formulas) |
| `Legacy` (5.0) | Legacy client formats: `LegacyDatCodec` (7.40–10.98/client 11 layouts, detection), `LegacySprFile` (reader and writer, RGB/RGBA), `LegacyClientProfiles`, `ClientConverter` (modern assets → validated Tibia.dat/Tibia.spr, lossy steps counted) |
| `Editing` (5.0) | Assets Editor model: `AssetsProject` (open, edit objects and sprites, save with re-encoded sheets and validation), editable proto2 schema `Protos/Editing/appearances_editable.proto` (byte-identical round trip), `CatalogWriter` (byte-identical catalog), `AecPackage` (object packages) |
| `Scripts` (5.0) | `Script` framework (parameters, validation, preview plan, apply), 12 built-in scripts, `ScriptRegistry`, `ScriptHistory` |
| `Compression`, `Imaging` additions (5.0) | `LzmaEncoder` and `CipLzma.Compress` (client sheet containers), `BmpEncoder` (byte-identical client sheets), `ImageResampler` (nearest, bilinear, bicubic, Lanczos with premultiplied alpha), `ImageFile` (decode/encode for the editors and scripts) |

## 4. Threading model

* The UI thread owns all WPF objects. **WPF bitmaps and brushes are created only on the UI thread** (`Images.ToBitmapSource` and `ToBitmapSourceAsync` marshal if called elsewhere). A WPF object created on a worker thread gives that thread its own Dispatcher and hidden window, which leaks window classes (see R03 in the bug report).
* Long operations run through `PageViewModel.RunAsync`:
  1. Work runs on the thread pool (`Task.Run` inside Core or the view-model).
  2. Progress is reported through `IProgress<OperationProgress>`, which is marshaled to the UI by `Progress<T>`.
  3. A `CancellationToken` is wired to Esc and the status-bar Cancel button.
  4. Only one operation per page runs at a time.
* Thumbnails in virtualized lists are decoded on demand on worker threads (bounded concurrency) and converted to frozen bitmaps on the UI thread at background priority.
* Core types that are shared across threads (`SpriteRepository`, `AppearanceDatabase`) are immutable after loading or use locks.
* `ClientDataService` shares an in-flight load per file: two pages asking for the same file get the same task.
* The **compatibility check** runs on the thread pool after start-up and after a detected client update. It reuses data a page has already loaded and never blocks the UI.
* **Operation tracking (6.0)**: every `RunAsync` operation is registered in `AppServices.Jobs` (queued → running → completed / completed with warnings / failed / cancelled). Progress reports reach the tracker through the same `Progress<T>` on the UI thread; the Diagnostics page refreshes at most four times a second, and only while it is visible. Cancellation from Diagnostics cancels the operation's token, only for operations whose manifest declares cancellation.
* **Operation limit (5.0)**: `RunAsync` waits on `AppServices.Operations` (Settings → Performance: long operations at the same time) and scripts also on `ScriptOperations`; a waiting operation says in the status bar that it is waiting until another operation finishes (and how many may run at a time), and it can be cancelled while it waits. Parallel work inside one operation uses the *worker threads* setting.
* **Client updates** are detected on the UI thread (page switch, window activation) by comparing file fingerprints. This is skipped while any page operation runs; on a change, all loaded data and thumbnails are released.
* **Item catalogs (7.0)** are not shared between pages: each page opens its own copy. Writing `catalog.json` keeps sources another copy registered (for example another Tibia Foundry window).

## 5. Theming

* `Themes/Colors.Light.xaml` and `Themes/Colors.Dark.xaml` define the same `App.*` brush keys: surfaces, borders, text, accent, states, inputs and checkerboard. They are the two built-in palettes and the bases of custom themes; `Core/Theming/ThemeTokens.cs` lists the same keys (checked by a test) with their groups and derivation rules.
* `Themes/Styles.xaml` restyles every control in use (buttons, inputs, lists, grids, tabs, scroll bars, menus, tooltips…). It references palette brushes only through `DynamicResource`. Buttons use `App.ButtonText`, inputs `App.InputBackground` and `App.InputText` (6.0).
* `ThemeEngine` (in `ThemeManager.cs`) builds the palette dictionary in code from a base palette and the resolved colors of a custom theme, with its own unfrozen brushes, and swaps it into `Application.Resources` once per theme switch, which restyles all open windows. While a custom theme is edited, `UpdateColors` only changes the `Color` of the brushes whose color changed — no dictionary is replaced and nothing else is redrawn. `ThemeManager` sets the DWM immersive dark-mode attribute (20) so title bars match, follows the Windows app mode for *System*, and keeps the active custom theme. The editor is `ThemeEditorViewModel` (Settings → Appearance & Themes); see `15_Themes_Outputs_Tools_and_Diagnostics.md` §1.
* Accessibility: text and accent contrast meet WCAG AA in both palettes (enforced by `ThemeResourceTests.TextColorsAreReadable`). Controls have keyboard focus visuals and `AutomationProperties.Name`.

## 6. Window and layout

* `MainWindow`:
  * resizable and maximizable, minimum 860×560;
  * the navigation rail collapses to icons below 1,100 px (Settings → Appearance can keep it expanded or compact);
  * the interface size setting (80–150 %) scales the whole window with a layout transform;
  * placement (position, size, maximized) is saved on close and restored, kept on a visible monitor (unless turned off);
  * closing asks first when an editor has unsaved changes.
* Per-Monitor-V2 DPI awareness and long-path awareness are declared in `app.manifest`.
* Pages use `Grid`/`DockPanel` layouts with `GridSplitter`s, `MinWidth`s and wrap panels. GridView lists stretch their last column (`GridViewBehavior`).

## 7. Configuration and data storage

| What | Where | Notes |
|---|---|---|
| Settings | `%APPDATA%\TibiaFoundry\settings.json`; portable: `<exe folder>\TibiaFoundry-Data\settings.json` | JSON, enums by name, written atomically (temp file + move). A corrupt file is kept as `settings.json.corrupt-<timestamp>` and defaults are used. No secrets. |
| Logs | 6.0: `Documents\Tibia_Foundry\Logs\TibiaFoundry-<yyyyMMdd>.log` (5.0: `%LOCALAPPDATA%\TibiaFoundry\logs`); portable: `TibiaFoundry-Data\logs` | 14 days retained, 20 MB per day, 200 MB in total |
| Mode selection | `AppPaths.Resolve` | Portable if built with `PortableBuild=true` or a `TibiaFoundry.portable` file is next to the exe, and the folder is writable; otherwise per-user, with a notice |
| Single-file native cache (portable exe) | `%TEMP%\.net\TibiaFoundry` | Created by the .NET single-file host for WPF/ImageMagick native DLLs |
| 1.x settings | `settings.json` next to the 2.0 EXE | Imported on the first start only |
| Output | 6.0: `Documents\Tibia_Foundry\<kind>` (GIF_Exports, Image_Exports, … Reports, Manifests; 7.0: Item_Catalogs) or folders chosen in Settings; 5.0: `Documents\TibiaSprites` | The client folder is never written to; folders are created when first used |
| Item catalogs (7.0) | `Documents\Tibia_Foundry\Item_Catalogs\<name>\` | `catalog.json`, `sources\*.json.gz`, `observations.jsonl`, `evidence\` |
| Client locations (7.0) | `settings.json` → `ClientLocations` (format 5) | `live:<id>`, `test:<id>`, `packages` |
| Client archive (7.3; 8.0) | portable: `TibiaFoundry-Data\Clients`; folder edition: `%LOCALAPPDATA%\TibiaFoundry\Clients` or the folder chosen | Revisions (`Live_Server\…`, `Test_Server\…`), `Shared_Assets\Objects`. 8.0 adds `Baselines\` (the last check of each installed client), `Leases\` (revisions being read) and `Rebuilds\` (unfinished rebuilds). |
| UI snapshot runs | `%TEMP%\TibiaFoundry-ui-snapshot\` (`settings.json`, `logs\`, `operations.json`) | Isolated from the user's settings, log and operation history |
| Download cache (2.1) | `%LOCALAPPDATA%\TibiaFoundry\cache\`; portable: `TibiaFoundry-Data\cache` | `wiki-item-ids.json`: last good TibiaWiki item table |
| Decoded sound cache | `%TEMP%\TibiaFoundry\sounds\` | Keyed by the content-hashed client file name |
| Sprite folder provenance (2.1) | `TibiaFoundry-sprites.json` in an extracted sprites folder | Client version and catalog hash the folder was made from |
| Settings from a newer version | `settings.json.v<N>.bak` next to `settings.json` | Written once before this version saves over them |
| Backups (5.0) | 6.0: `Documents\Tibia_Foundry\Backups` (5.0's `%LOCALAPPDATA%\TibiaFoundry\backups` is kept when it holds backups); portable: `TibiaFoundry-Data\backups`; or the folder set in Settings | One folder per operation with `backup.json`; retention by age and size; `restore-history.json` |
| Temporary files (5.0) | `%TEMP%\TibiaFoundry\`; portable: `TibiaFoundry-Data\temp`; or the folder set in Settings | One lease folder per operation; cleaned at start-up and exit (except folders in use) |
| Script history (5.0) | `script-history.json` next to `settings.json` | Metadata only (no file contents); clearable |
| Rename records (5.0) | `backups\rename-records\` | Used to undo a Batch File Renamer run |
| Operation history (6.0) | `operations.json` next to `settings.json` | Names, durations, counts, outputs and outcomes only; bounded (Settings → Diagnostics); clearable |
| Diagnostics reports (6.0) | `Documents\Tibia_Foundry\Reports\TibiaFoundry-diagnostics-<date>-<time>.zip` | Created only on request, sanitized by default, never sent |

## 8. Error handling

* Expected failures (missing files, invalid input, cancelled work) show a specific message or a status-bar warning.
* Client data problems raise `DataFileException`, which names the file, the kind (missing, unreadable, damaged or changed format) and advice. `PageViewModel.RunAsync` turns it into a plain-language dialog. Only the features that need that file are affected; the page banner and the Compatibility page say which.
* Batch operations report through `ReportBatch`: success (green), partial (amber, failures listed), nothing written (red).
* Damaged input is rejected, not decoded to garbage: truncated or oversized LZMA, invalid bitmaps, sheets whose content does not match the checksum in their names, unsafe file names in data.
* Unexpected exceptions in page operations are logged with a stack trace, shown once in an error dialog that points to the log, and the operation is marked failed. The application keeps running.
* `DispatcherUnhandledException` is the last resort. A burst of more than 8 exceptions in 5 s (a layout or render loop) shuts the app down cleanly with one message instead of looping.
* Start-up failures (e.g. Windows refusing to create the window) are reported, and the process exits.

## 9. Testing architecture

* `TestData` locates `04_reference` (or `TF_REFERENCE_DIR`) and uses the **pinned client** it names (`reference-client.json`), so a Tibia update does not invalidate the parity tests. `TF_CLIENT_DIR` selects another client; parity tests then skip with the reason. Tests whose data are missing are **skipped**, never passed.
* Network tests use a scripted `HttpMessageHandler`; no routine test contacts a website. `LiveServiceChecks` is opt-in (`TF_LIVE_WIKI=1`).
* Parity tests compare the rebuild's outputs with outputs produced by the original binary through `tools/ParityHarness`.
* View-model tests replace `IDialogService` with `FakeDialogs`.
* Tests that need WPF objects run on a private STA dispatcher (`Sta.Run`), which is shut down afterwards. If Windows refuses the dispatcher's window, each retry uses a new thread; if the test body cannot start, the test is skipped with the environmental reason.
* `--ui-snapshot` renders every page in both themes, captures binding errors, and runs scripted window checks (themes, persistence, resizing, maximizing, package switching, and since 5.0 the tagline, site icons, acknowledgments, Settings category navigation, navigation layout and interface size).
* View-model tests of 5.0 give `AppServices` program folders inside the test folder (`TestData.PathsIn`), so backups, temporary files and script history never reach the user's data folders.
* `ReferenceConversionTests` (opt-in, `TF_REFERENCE_LEGACY`) compares a conversion with a reference pair made by another converter.
* 7.0 opt-in checks: `TF_OTB_ITEMS_DIR`/`TF_CLIENTID_ITEMS_DIR` (third-party item data you provide, never bundled), `TF_LIVE_WIKI` with `TF_CATALOG_REPORT` (catalog coverage).
