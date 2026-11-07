# Bug Report – the original program (1.8.0.26867) and status in 2.0.0 (rebuild)

> **Note (Tibia Foundry 6.0):** this is a historical record of the original program (version 1.8.0.26867) and the 2.0 reconstruction. The program was later renamed Tibia Forge (version 3.0) and then **Tibia Foundry** (version 4.0). Paths and figures below are those of the earlier version; since 6.0 the earlier product names are written as placeholders (`<original>` for the original program's file names, `<v2>` for the identifier of version 2.x, see `13_Branding_and_Attribution.md` §10). See `07_Testing_Report.md` for current results.

This report lists every defect found in the original program and what the rebuild does about it.

**Evidence codes**

* **H**: reproduced by running the *original* binary.
* **C**: found by reviewing the decompiled source (`01_decompiled/`). The code path is unambiguous, but the defect was not executed.
* **T**: the rebuild has an automated test that exercises the fixed behaviour (test name given).

**Severity**

* **Critical**: crash or data loss.
* **High**: wrong output written or reported as success.
* **Medium**: feature broken or misleading.
* **Low**: cosmetic, maintainability or robustness.

All items below are **fixed** in 2.0.0 unless the status says otherwise.

## 1. Crashes and data loss

| ID | Area | Defect | Sev. | Evidence | Rebuild |
|---|---|---|---|---|---|
| B01 | House Images | Selecting a house when the grid has no selected row throws (`SelectionChanged` → `SelectedRows[0]`). Happens when the data are loaded while the tab is hidden and the tab is then opened, or after `ClearSelection`. | Critical | H | Selection is view-model state; an empty selection clears the preview. |
| B02 | House Images | Houses whose objects have fewer pattern sprites than their pattern size, or that reference unknown objects/sprites, crash the renderer (e.g. houses 10004 and 11015). | Critical | H | Missing sprites are skipped with a warning shown under the image. T: `HousesThatCrashedTheOriginal_Render` |
| B03 | Appearances → Export GIF | Objects with ping-pong animation crash (delay index out of range). | Critical | C | T: `PingPongObject_NoLongerCrashes` |
| B04 | Appearances → Export GIF | A missing sprite file crashes the export (`new Bitmap(path)`). | Critical | C | Sprites come from the extracted folder or, if missing, the client sheets. T: `Repository_FallsBackToClientWhenFileMissing` |
| B05 | Appearances (all previews) | The original only reads pre-extracted `<id>.png` files and crashes on the first missing one. | Critical | C | Same fallback as B04. |
| B06 | Outfit renderer | `NullReferenceException` for outfits with 8 frames and no animation data. | Critical | C | Delays are derived safely. T: `RenderedFrames_MatchOriginal` (51 frames) |
| B07 | Appearances → Filters | The *imbueable* criterion throws `NullReferenceException` for objects without imbueable data. | Critical | C | T: `Filters_MatchOriginal` (scenario documents the fix) |
| B08 | Appearances → Data panel | NPC sale currencies crash when the currency object is missing. | Critical | C | Missing currency shown as its ID. |
| B09 | Sounds | Crash when an ambience object has no sound effects. | Critical | C | Handled. T: `OtherDataJson_MatchesOriginal` loads all sounds |
| B10 | Compare DAT files | "Compare Folders" crashes when one folder lacks a file (`Length < 0` check on a missing path). | Critical | C | Missing files are reported in the notes. |
| B11 | Compare DAT files | The comparison runs on a worker thread but reads UI controls and shows message boxes from it (cross-thread exceptions, random failures). | Critical | C | Comparison runs on a worker thread with progress; UI is touched only on the UI thread. T: `CompareValidatesInputAndDetectsIdenticalFiles` |
| B12 | Static Data | Creatures that use an *object* appearance pass the object ID to the outfit renderer, which crashes or shows an unrelated outfit. | Critical | C | Object appearances are rendered as objects. |
| B13 | Wiki item IDs (Tools) | Download blocks the UI thread with `.Result` and crashes on any network error. | Critical | C | Async download with error message. |
| B14 | Client data lookup | Asset discovery crashes when no matching `.dat` file exists. | Critical | C | Uses `catalog-content.json`; a clear message is shown if files are missing. |
| B15 | Settings | `settings.json` is written next to the EXE, so saving fails when the app is installed in a read-only folder (Program Files). A corrupted file crashes start-up. | Critical | C | Stored in `%APPDATA%\<v2>`, written atomically; a corrupt file is backed up and defaults are used. T: `CorruptFileIsBackedUpAndDefaultsAreUsed` |

## 2. Wrong output or false success

| ID | Area | Defect | Sev. | Evidence | Rebuild |
|---|---|---|---|---|---|
| B16 | Appearances → Export → Outfiter sheet | In "PNG File" mode the chosen file is created **empty**, the real image is written as `mgk.png` in the working directory, and success is reported. | High | H | Writes the chosen file. T: `OutfitSheetPngFileMode_WritesChosenFile` |
| B17 | Extract Sprites | The ".gif" format option is ignored (always PNG). | High | C | GIF honoured. T: `Extractor_WritesOriginalLayout` |
| B18 | Extract Sprites | An output path without a trailing separator produces folder names glued to the path (`…AllSpritesSheet 0-143`). | High | C | `Path.Combine` everywhere. |
| B19 | GIF export | Fully transparent frames become **opaque black** (GDI+ writes zero alpha, which ImageMagick reads as opaque). | High | H | Transparency kept. T: `Exports_MatchOriginalGifs` (allows exactly this documented difference) |
| B20 | GIF export | Pattern grids larger than 2×2 reuse the wrong sprites (the pattern offset wraps at 3). | High | C | Correct indexing. |
| B21 | HR map builder | The world origin is hard-coded (31872, 30976) and the canvas is sized from the first and last file names, so tiles outside that box are clipped. | High | C | The canvas is computed from all tiles. T: `HighResMaps_MatchOriginal` (output identical for the current client) |
| B22 | Map tiles / Cyclopedia crop | Row and column loop counts are swapped, which drops tiles on non-square maps. | High | C | Fixed. T: `ExtractedMapTiles_MatchOriginal` (1,157 tiles identical) |
| B23 | Map data | Every subarea name is appended twice, and the parent area of 437 subareas is left empty in the areas export. | High | H | Fixed. T: `AreasJson_MatchesOriginalApartFromFixes` |
| B24 | Appearances → Filters | *Bank* also requires `Waypoints != 0`. In the current client every bank object has 0 waypoints, so the filter never matches (original 0 results, correct 3,334). | High | H | T: `Filters_MatchOriginal` |
| B25 | Appearances → Filters | *Restricted to vocation = Any* matches every object (a repeated field is never null). | High | C | Matches only restricted objects. |
| B26 | Appearances → Filters | *Width* tests `< 32`, so 32-px-wide bounding boxes match neither "32" nor "64". | High | C | `≤ 32` / `> 32`. |
| B27 | Appearances → Filters | Some comparison criteria ignore the Exclude mode (`^ Include` applied in some predicates only). | High | C | Mode applied uniformly. T: `Filters_MatchOriginal` (50 scenarios incl. exclude modes) |
| B28 | Appearances → Search | "Uses sprite" searches only the first frame group (misses moving/animated groups). | Medium | C | All frame groups. |
| B29 | Appearances → Data panel | Vocation restrictions are never formatted (the code looks for a property `RestrictToProfession` that does not exist). | Medium | C | Vocation names shown. |
| B30 | Outfitter | Mount colours are compared against outfit colours, and three colour branches all test "Head", so changing the part to edit could select the wrong colour. | Medium | C | Separate colour sets for outfit and mount. |
| B31 | Text decoding | Strings stored as Latin-1 in the client (e.g. "djinn maître") come out as replacement characters, and modern Protocol Buffers reject the file entirely. | Medium | H | Decoded as Latin-1. T: `Latin1Strings_AreRepaired`, `AppearancesJson_MatchesOriginal` (2 lines differ, both this fix) |
| B32 | Catalog summary | The sprite-sheet count is one higher than the number of sheets. | Low | H | Counted correctly. T: `CatalogSummary_MatchesOriginalExceptSheetCount` |
| B33 | Map export | The areas table header has the typo "LebelCoords". | Low | C | "LabelCoords". |
| B34 | Name casing | Name capitalisation uses the current culture, so wiki names differ on e.g. Turkish Windows. | Low | C | Invariant culture. |
| B35 | Asset lookup | The `*.dat` glob also matches `*.dat-bak` backups (8.3 short-name quirk), so an old backup could be loaded. | Medium | C | Exact extension; catalog first. |
| B36 | Compare DAT files | Results of the previous comparison leak into the next one; entries removed in the new file are never reported; static map data cannot be compared. | Medium | C | Fresh result per run; removed entries listed; staticmapdata supported. |
| B37 | Appearances → Filters | "Clear Filters" leaves the criteria on the *Adv. Flags 2* tab (weapon type, minimum level) ticked. | Medium | H | Every criterion is cleared. |
| B38 | Appearances | After "Clear Filters" following certain filter runs, the Outfits grid is left empty. | Medium | H | List rebuilt from the unfiltered set. T: `AppearancesSearchOutfitterAndExports` (clear restores the full list) |
| B39 | LZMA container | The header is skipped with two hard-coded byte patterns, which only works because the varints happen to have a fixed length. | Low | C | The header is parsed. T: `CipHeader_IsParsedForEveryAssetKind` |

## 3. Unfinished, unwired and hidden features

| ID | Control (original name) | Defect | Rebuild |
|---|---|---|---|
| B40 | *Keep Selected* (`Dat_FiltersKeep`) | Permanently disabled; the click handler is empty. | Implemented: keeps only the selected appearances in the list. |
| B41 | *Generate Wiki Area/Subarea List* (`Map_GenWikiAreaList`) | No click handler. | Implemented on Map Tools → Export data. |
| B42 | House Images *Change file* (`Static_SelDAT`) | No click handler. | Implemented. |
| B43 | Static Data sprite-ID *Filter* (`button5`) | No click handler. | Implemented as the "Sprite ID" filter box. |
| B44 | *Pattern Stage* (`Gif_PatternsUse`) | Permanently disabled; only its label shows a help message. | Enabled for objects with patterns ("All" or a single pattern); the help text is a tooltip. |
| B45 | *DefaultStartPhase* and *RandomStartPhase* filters (`Dat_Filter_startPhase`, `Dat_Filter_randomStartPhase`) | *DefaultStartPhase* can be ticked but the filter code never reads it, so it silently does nothing. *RandomStartPhase* is permanently disabled. The appearances schema compiled into the original has no start-phase fields (`SpriteAnimation` fields 1 and 3 are absent), so neither filter can work. | **Not implementable with current data.** Not offered as filters; the panel says why. Adding the fields to the schema would change the JSON export, which must stay identical to the original. |
| B46 | Tabs *Export Sheets*, *Sprites from PNG*, *LZMA Decode* | Built but removed from the tab control at start-up (`TabPages.Remove`). | Restored on the **Advanced Tools** page (outfiter sheet builder, sheet slicer, LZMA decode and download). |
| B47 | `GifEncoder` class; `clientbg` and `pbbg` resources | Dead code and two embedded images that nothing references. | Not ported. (`pbbg1` is used, as the House Images background, and is replaced by the theme checkerboard.) |

## 4. Architecture, robustness and maintainability

| ID | Defect | Sev. | Rebuild |
|---|---|---|---|
| B48 | Long operations run on the UI thread, freezing the window for seconds to minutes: sound extraction, map extraction, HR map building, house rendering, monster loading and appearance filtering. | High | All long work is asynchronous, with progress in the status bar and cancellation (Esc or the Cancel button). T: `RunAsyncCanBeCancelledAndIsNotReentrant` |
| B49 | The two `BackgroundWorker`s read UI controls from the worker thread. | High | Inputs are captured before the work starts. |
| B50 | Data files opened with `File.OpenRead` are never closed, so the `.dat` files stay locked until exit. `new Bitmap(path)` locks sprite files. | Medium | Files are read fully and closed; images are decoded into memory. |
| B51 | Thousands of GDI+ bitmaps are created and never disposed. | Medium | Managed pixel buffers; WPF bitmaps are frozen and cached with a bounded LRU. |
| B52 | Obsolete or broken APIs: `BinaryFormatter` deep clone (removed in .NET 9), `WebClient`, and `Process.Start(url)`, which throws on .NET Core. | Medium | Replaced with Protocol Buffers `Clone()`, `HttpClient`, and shell execute with URL validation. |
| B53 | Fixed-size window (780×557 client area, `Fixed3D`, no maximise). Absolute layout is clipped at high DPI. | Medium | Resizable and maximisable, with a minimum size, Per-Monitor-V2 DPI awareness, a responsive layout, and saved placement. |
| B54 | About 40 `MessageBox` calls, many empty `catch {}` blocks, and no log. | Medium | Rolling log file (14 days, 20 MB cap per day), consistent error dialogs, and messages in the status bar. |
| B55 | One 8,000-line form class holding all state in public static fields. | Low | Core library, view-models and views are separated (see Architecture). |
| B56 | The app is 32-bit (`Prefer32Bit`) and can run out of address space when building large HR maps. | Medium | x64. |

## 5. Defects found in the rebuild during development (all fixed)

These issues were found while testing the rebuild. They are listed so the testing record is complete.

| ID | Defect | How it was found | Fix |
|---|---|---|---|
| R01 | A style key `App.ScrollThumb` shadowed the brush with the same name, so the scroll-bar thumb got a `Style` as its background. Every render threw, and the app showed an error dialog in a loop. | First UI snapshot run | Styles renamed. T: `StyleKeysDoNotShadowPaletteKeys` |
| R02 | Repeated UI exceptions produced an endless loop of error dialogs and a 177 MB log file. | Same run | Crash-loop guard (stops after a burst with one message) and a log size cap. T: `LogFileStopsGrowingAtTheSizeLimit` |
| R03 | Thumbnail bitmaps were created on thread-pool threads. Each such thread got its own WPF dispatcher and hidden window, which leaked window classes (user atoms) until Windows refused to create windows. | Unobserved task exceptions "Not enough memory resources" | WPF bitmaps are created only on the UI thread. |
| R04 | If the main window failed to initialise, the process kept running invisibly. | Snapshot run in an exhausted session | Start-up failure is reported and the app exits. |
| R05 | The primary-button text contrast in the dark theme was 4.27:1, below WCAG AA 4.5:1. The "Changed" badge text was 2.5:1. | Contrast test | Accent colour adjusted; badge uses the link colour. T: `TextColoursAreReadable` |
| R06 | `PageViewModel.Info()` method and an observable `Info` property collided on the House Images page. | Code review before the first build | Property renamed `RenderInfo`. |
