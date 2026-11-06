# Build, Setup and Configuration

## 1. Running the release (end users)

There are two editions. Both are **self-contained**: no installation, no .NET runtime.

| Edition | File | Settings and logs |
|---|---|---|
| **Portable** (recommended for USB sticks and shared PCs) | `dist/TibiaFoundry-7.1.0-portable-win-x64.zip`: a single `TibiaFoundry.exe` (99 MB) + `README-Portable.txt` + `THIRD-PARTY-NOTICES.txt` | `TibiaFoundry-Data\` next to the exe (settings, logs, backups); exports in `Documents\Tibia_Foundry` |
| Folder | `dist/TibiaFoundry-7.1.0-win-x64.zip`: `TibiaFoundry.exe` + its runtime files + `THIRD-PARTY-NOTICES.txt` (257 files) | settings in `%APPDATA%\TibiaFoundry`; logs, backups and exports in `Documents\Tibia_Foundry` |

To use either edition, unzip it anywhere you can write to and start `TibiaFoundry.exe`. `dist/SHA256SUMS.txt` lists checksums (also of the 7.0.1 and 7.0.0 zips, which are still in `dist/`). Earlier releases are kept in `06_baseline_2.0.0/` to `12_baseline_7.0.1/`.

Since 8.0 neither edition includes the Windows SDK projection (`Microsoft.Windows.SDK.NET.dll`, 55 MB): only the text recognition of Item Discovery needed it, and Item Discovery was removed in 8.0. The portable edition unpacks its compressed contents into memory when it starts (`07_Testing_Report.md` has the measured start-up times and sizes).

**Portable mode details.**

* The portable exe always uses `TibiaFoundry-Data\` next to itself.
* Any edition becomes portable if an empty file named `TibiaFoundry.portable` is placed next to its exe.
* If the program folder is read-only, the app falls back to the per-user folders and says so in the status bar.
* Settings → Advanced → *Storage* shows the mode in use.
* Like every self-contained single-file .NET program, the portable exe unpacks its native libraries once (6 files, about 31 MB: WPF graphics, ImageMagick, C runtime) into `%TEMP%\.net\TibiaFoundry`. This is the only thing it writes outside its own folder; it is a disposable cache.

| Requirement | Notes |
|---|---|
| Windows 10 or 11, x64 | Built and tested only on Windows 11 Pro 10.0.26200 |
| Tibia client installed | `%LOCALAPPDATA%\Tibia\packages\Tibia`. A test-server package (default `TibiaExternal`) is optional. |
| Disk space | 225 MB for the program (folder edition), plus your exports and backups (Diagnostics → Performance shows the free space where they go) |
| Internet | Only for TibiaWiki's item IDs (*Item Catalog → TibiaWiki item IDs* and *Generate Lua data for all NPCs*: about 190 requests to the MediaWiki API, about a minute, cached 24 h; an older cached copy is used with a notice when the wiki is unreachable) and *Advanced Tools → Download list* |

**First start**:

* If a `settings.json` of the original 1.x program lies next to the new EXE, its folders are imported and its "Author" becomes the creator name. Settings of versions 3.0 and 2.x are carried over automatically; the old files are not changed (see `13_Branding_and_Attribution.md` §9).
* The theme follows Windows. Choose another theme or make your own in *Settings → Appearance & Themes*, or press **F6** to toggle the built-in light and dark themes.
* Exports go to `Documents\Tibia_Foundry`, one subfolder per kind (`GIF_Exports`, `Image_Exports`, …), created when first used. *Settings → Files and folders* shows and changes every folder. After an export, the status bar names the saved file; click the path to show it in File Explorer.
* **Upgrading from 7.1.0**: nothing changes in the settings. Catalogs with renames open in 7.1 (renames are decisions, which 7.1 reads).
* **Upgrading from 7.0.0 or 7.0.1**: nothing changes in the settings. Portable edition: copy the `TibiaFoundry-Data` folder that is next to the old exe next to the new exe.
* **Upgrading from 6.0**: the settings are upgraded (format 5) with every value kept; the assets folder you chose now applies to the live client and, as a separate choice, to the test server. 6.0 can still read the upgraded file and keeps the 7.0 values. Portable edition: copy the `TibiaFoundry-Data` folder that is next to the 6.0 exe next to the 7.0 exe.
* **Upgrading from 5.0**: the settings are upgraded (format 4). An output folder that was still the 5.0 default (`Documents\TibiaSprites`) moves to `Documents\Tibia_Foundry`; folders you chose are kept; existing backups stay where they are. Nothing is moved or deleted; the status bar says where new files go. 5.0 can still read the upgraded settings.
* With *Load data automatically* on (the default), each page loads its client file when first opened.
* A compatibility check of the installed client runs in the background (3–5 s). Its result is on the **Compatibility** page; pages whose data are affected show a warning banner.

**Uninstall**:

* Portable: delete the exe and `TibiaFoundry-Data`. Optionally delete `%TEMP%\.net\TibiaFoundry`.
* Folder edition: delete the program folder. To also remove settings, caches and older logs, delete `%APPDATA%\TibiaFoundry` and `%LOCALAPPDATA%\TibiaFoundry`.
* Your exports, logs, backups and reports stay in `Documents\Tibia_Foundry` until you delete them.

## 2. Building from source (developers)

Prerequisites:

* .NET SDK **10.0.100** or newer 10.0.x (`src/global.json` rolls forward to the latest feature band).
* Windows (WPF). Any IDE with .NET 10 support, or just the `dotnet` CLI.

All commands run in `Reconstruction/src`.

**Build**:

```bash
dotnet build TibiaFoundry.slnx -c Release
```

The build has no warnings under the `latest-recommended` analyzers. Protobuf classes are generated from `TibiaFoundry.Core/Protos/*.proto` during the build (Grpc.Tools).

**Run from source**:

```bash
dotnet run --project TibiaFoundry.App -c Debug
```

**Tests**:

```bash
dotnet test TibiaFoundry.Tests/TibiaFoundry.Tests.csproj
```

* Tests that need the Tibia client or the reference outputs **skip** (not pass) when that data is absent. Reference outputs live in `Reconstruction/04_reference`, or set `TF_REFERENCE_DIR`.
* The tests use the **pinned client** named in `04_reference/reference-client.json` (a copy of client 15.33.b21348), so a Tibia update does not break them. Without that copy, the installed client is used.
* The full comparison of all 223,694 sprites with the original's extraction and the conversion of the whole client are opt-in: set `TF_FULL_SPRITES=1` (about 2 min more).
* No routine test uses the network, the user's data folders or the client folder for writing. Expected result for 7.1.0 with `TF_FULL_SPRITES=1`: 458 tests, 450 passed, 8 skipped (opt-in: the three live TibiaWiki checks, the reference comparison, the real Explorer check, the third-party item data research figures, the installed client's options file, and the text-recognition measurement). The Explorer check (`TF_SHELL_CHECK=1`) opens a File Explorer window on a temporary folder and closes it again.

| Variable | Effect |
|---|---|
| `TF_FULL_SPRITES=1` | Compare every sprite with the original's extraction; convert the whole client to Tibia.dat/Tibia.spr |
| `TF_REFERENCE_LEGACY=<folder>` | Compare a conversion with the `Tibia.dat`/`Tibia.spr` in that folder (made by another converter; only read) |
| `TF_CLIENT_DIR=<package folder>` | Test against another client, e.g. the installed one after an update. Parity tests then skip with the reason. |
| `TF_REFERENCE_DIR=<folder>` | Reference outputs elsewhere |
| `TF_LIVE_WIKI=1` | Run the live TibiaWiki checks (`LiveServiceChecks`). Manual use only. |
| `TF_WIKI_PAGE_JSON=<file>`, `TF_WIKI_COMPARE_REPORT=<file>` (7.0.1) | Compare the table from the item articles with a saved API answer for the "Item IDs" page when the wiki cannot build the live page; write the comparison |
| `TF_SAVE_WIKI_HTML=<file>` | During the live check, save the page HTML (for new parser fixtures; needs the wiki to be able to build the page) |
| `TF_SHELL_CHECK=1` | Run the real File Explorer check (`ExportStatusTests.ExplorerOpensTheFolderWithTheFileSelected`): opens a window on a temporary folder with the file selected, then closes it |
| `TF_OTB_ITEMS_DIR`, `TF_CLIENTID_ITEMS_DIR=<folder>` (7.0) | Reproduce the third-party item data research figures with your own copies: a folder with an `items.otb` and the `items.xml` that uses its IDs, and/or a folder with an `items.xml` that uses client object IDs (`TF_CATALOG_REPORT_DIR` saves the comparison workbook) |
| `TF_CATALOG_REPORT=<file>` with `TF_LIVE_WIKI=1` (7.0) | Coverage of a catalog of the client and TibiaWiki |

The `TWT_` spelling of these variables (version 2.x) is still accepted.

**Reverse checks** (about 25 min for all 118): `python -I tools/verification/mutation_checks.py src report.txt [IDs…]`, run from `Reconstruction`. It puts each audited defect back, or removes a safeguard, temporarily and confirms the tests catch it, then restores the files byte-for-byte. Do not edit, build or publish from `src` while it runs.

**Measurements** (optional): `tools/verification/measure` (decode timing, memory of the compatibility check, reports per client package) and `tools/verification/startup_perf.ps1` (time to window, memory).

**TibiaWiki import check** (7.0.1): `powershell -ExecutionPolicy Bypass -File tools\verification\tibiawiki_import_check\run_check.ps1 -App dist\TibiaFoundry-7.0.1-win-x64` repeats the import that failed in 7.0.0 on a portable copy of a build, against the live TibiaWiki: Item Catalog → *TibiaWiki item IDs* with no catalog yet, the suggested name, the download with its progress, *Import*, then the catalog read from disk, the cache and the log (`tools/verification/tibiawiki_import_check/README.md`). It needs the internet and opens the program's window for about a minute; it does not use the mouse.

**Publish** (what `dist/` contains):

```bash
dotnet publish TibiaFoundry.App/TibiaFoundry.App.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o ../dist/TibiaFoundry-7.1.0-win-x64
```

For a smaller framework-dependent build that needs the .NET 10 Desktop Runtime, use `--self-contained false`.

**Publish the portable single-file edition** (profile `TibiaFoundry.App/Properties/PublishProfiles/Portable.pubxml`):

```bash
dotnet publish TibiaFoundry.App/TibiaFoundry.App.csproj -p:PublishProfile=Portable
```

The profile sets single file, native libraries included, compression, ReadyToRun, embedded symbols and `PortableBuild=true`. Output goes to `dist/TibiaFoundry-7.1.0-portable-win-x64/`. Copy `README-Portable.txt` and `THIRD-PARTY-NOTICES.txt` next to it (`11_Maintenance_Guide.md` §11).

**Self-test** (no window; for checking a build or a new PC):

```bash
TibiaFoundry.exe --self-test <folder>
```

It writes `<folder>\self-test.txt` with PASS, FAIL or SKIP for each check; the exit code is 0 when nothing failed, otherwise 1. The checks cover:

* the data folder;
* WPF/XAML resources and rendering;
* the client catalog;
* LZMA sprite decoding with a GDI+ PNG round trip;
* protobuf appearances;
* outfit rendering with an ImageMagick GIF;
* creator attribution in exported PNG and GIF files;
* sprite sheet encoding for the Assets Editor (a client sheet re-encoded byte for byte, LZMA round trip);
* the client's Qt resource file (all files read, including zlib, every file found by Qt's lookup, a rebuilt copy valid);
* the NAudio/NVorbis decoders (no sound is played);
* the client data compatibility check. The full report is included;
* the application self-check of the Diagnostics page (settings, every output folder, the log folder, the registered tools and their dependencies, the main components). It is read-only: nothing is repaired and the settings are not saved. Problems of the configuration are listed as warnings; only a broken component or tool fails the self-test.

**Automated UI check**: renders every page, then runs the window checks: theme switching and persistence, branding (title, icon, logo, tagline, creator links with site icons, credits, acknowledgments), the compatibility banner, Settings category navigation, the navigation-bar mode and interface size, resizing, maximize/restore, package switching and placement; since 6.0 also live custom-theme colors, unsaved/saved/restored themes, the clickable saved-file link in the status bar, Cancel only for cancellable operations, the Diagnostics self-check and report preview, and the dependency banner; since 7.0 also every client location in Settings, and the Item Catalog with the client's objects and a comparison (8.0: 43 checks after the Item Discovery check was removed with the tool). The report is written to `<folder>\report.txt`, and the exit code is 0 on completion.

```bash
TibiaFoundry.exe --ui-snapshot <folder> [--theme light|dark] [--size 1400x900]
```

* Without `--theme`, both themes are captured.
* Snapshot runs keep their settings, log and operation history in `%TEMP%\TibiaFoundry-ui-snapshot` (6.0: the log and history no longer go to the user's folders), never open dialogs, and exit within 10 minutes at most. The Diagnostics self-check they run tests whether the output folders can be written by creating and removing a temporary file in folders that exist.

## 3. Rebuilding the reference outputs (optional)

`tools/ParityHarness` is a .NET Framework 4.8 **x86** console program. It loads the **original** program's executable (1.8.0.26867, in `00_original_backup/`) through reflection and calls its handlers to produce reference outputs.

* Run it only against the sandbox copy in `03_sandbox_original`, never against the user's install.
* Commands:
  * `sprites`: extract every sprite with the original;
  * `screens`: off-screen screenshots of every original tab;
  * `reference <section>`, where the section is one of `resources`, `json`, `lzma`, `filters`, `wiki`, `npc`, `outfits`, `gifs`, `sheet`, `houses`, `mapextract`, `hrmap`, `sounds`, `compare`.
* Outputs go to `04_reference`.
* The harness reads the **installed** client (read-only). After regenerating, also re-pin that client in `04_reference` and update `reference-client.json`. The procedure is in `11_Maintenance_Guide.md` §4.

## 4. Configuration reference

**Settings file**: `%APPDATA%\TibiaFoundry\settings.json`, or `TibiaFoundry-Data\settings.json` in portable mode (UTF-8 JSON, enums by name). It is written atomically on every change unless *Save settings changes made in this session* is off.

| Key | Default | Meaning |
|---|---|---|
| `Theme` | `System` | The built-in theme: `System`, `Light` or `Dark` (used when no custom theme is active) |
| `MainWindow` | 1280×820, centered | `Left`, `Top`, `Width`, `Height`, `Maximized` (restored only if visible on a monitor) |
| `LastPage` | – | Page shown at start |
| `ShowDefaultValues` | `false` | Data panel shows unset fields |
| `UseTestServer` / `TestServerPackage` | `false` / `TibiaExternal` | Client package in use |
| `AssetsFolderOverride` | – | Use a different `assets` folder (must contain `catalog-content.json`) |
| `LoadDataAutomatically` | `true` | Load each page's file on first visit |
| `SpritesFolder` / `SpritesFolderTestServer` | – | Extracted `<id>.png` folders (optional) |
| `SpriteSource` | `FolderThenClient` | `FolderThenClient`, `FolderOnly` (1.x behavior) or `ClientOnly` |
| `SaveDecodedSheets` | `false` | Write sprites decoded from the client into the sprites folder ("extract on the fly") |
| `OutputFolder` | `Documents\Tibia_Foundry` (5.0: `Documents\TibiaSprites`) | Root of the output folders; Documents is resolved through Windows |
| `SoundsFolder`, `GifFolder`, `MapTilesFolder`, `SpriteExtractionFolder` | empty = `<root>\Sound_Exports`, `GIF_Exports`, `Map_Exports`, `Sprite_Exports` | Folders chosen for these kinds of output |
| `OutputFolders` (6.0) | empty | Folders chosen for `ImageExports`, `DataExports`, `Reports`, `Manifests` (kind → folder) |
| `CreatorName` | `RockyLaRo` | Creator name embedded in every exported PNG and GIF ("Created by <name> using TibiaFoundry for TibiaWiki @ https://tibia.fandom.com/"). Kept by *Reset to defaults*. Replaces the `GifAuthor` of version 2.x, which is migrated once. |
| `GifAutoName`, `GifDelayMode`, `GifNoAnimationDelay`, `GifExtraDelay`, `GifExtraFrame` | `true`, `Average`, 1000, 0, `false` | GIF export options |
| `SoundAutoplay`, `SoundVolume` | `false`, 0.8 | Sound player |

**Setting categories (format 3, since 5.0)**. Each is a JSON object. The Settings page shows them in the categories described in `14_Tools_Settings_and_Scripts.md` §5.

| Key | Contents (defaults) |
|---|---|
| `General` | `StartupPage` (`LastUsed` or `Specific`) and `StartupPageName`; `RememberLastFolders` (`true`) with `LastFolders`; `ReopenLastFiles` (`false`) with `LastFiles` |
| `Interface` | `Scale` (1.0, 0.8–1.5); `Navigation` (`Automatic`, `Expanded`, `Compact`); `RememberWindowPlacement` (`true`); `AnimatePreviews` (`true`); `ThumbnailSize` (32) |
| `Locations` | `BackupFolder`, `TempFolder`, `LogFolder` (empty = defaults); `Tools`: per-tool `Input`/`Output` folders for `AssetsEditor`, `RccEditor`, `ClientConverter` |
| `Safety` | `WhenOutputExists` (`Ask`, `KeepBoth`, `Overwrite`, `Skip`); `BackupBeforeChanges` (`true`); `BackupRetentionDays` (30); `BackupMaxSizeMegabytes` (4096); `ValidateOutputs` (`true`); `WarnUnsupportedFormats` (`true`); `CleanTemporaryFiles` (`true`) |
| `Performance` | `MaxConcurrentOperations` (2); `WorkerThreads` (0 = one per core); `SpriteSheetCache` (96); `StopBatchOnError` (`false`); `ShowTaskbarProgress` (`true`); `PreviewSizeLimitMegabytes` (32) |
| `Notifications` | `FlashTaskbarWhenDone` (`true`); `PlaySoundWhenDone` (`false`); `NoticeAfterSeconds` (10); `ShowErrorDialogs` (`true`); `LogLevel` (`Info`); `LogRetentionDays` (14); `LogMaxMegabytesPerDay` (20) |
| `Scripts` | `PreviewFirst` (`true`); `Backups` (`FollowGlobal` or `Always`); `KeepHistory` (`true`); `MaxHistoryEntries` (200); `MaxConcurrentScripts` (1); `ShowAdvancedOptions` (`false`) |
| `ToolState` | Last inputs of the tools (`Scripts.<id>.<input>`) and the settings tools declare in their manifests (`<prefix>.<key>`, e.g. `ClientConverter.profile`, with `<prefix>.$settingsVersion`); no file contents |

**Sections added in format 4 (6.0)** — top-level, so 5.0 keeps them unchanged when it saves:

| Key | Contents (defaults) |
|---|---|
| `Themes` | `ActiveCustomTheme` (null = the built-in `Theme`); `Custom`: `Id`, `Name`, `Base` (`Light`/`Dark`), `Colors` (token → `#RRGGBB`, only the changed colors), `ModifiedUtc` |
| `Diagnostics` | `PerformanceMonitoring` (`true`); `SampleIntervalSeconds` (2: 1, 2, 5, 10 or 30); `OperationHistoryEntries` (200); `LogMaxTotalMegabytes` (200); `ReportLogDays` (3) |
| `OutputFolders` | see above |

A format-2 file (4.0) is upgraded on the first start without losing anything; the new categories start with the 4.0 behavior. A format-3 file (5.0) is upgraded to format 4 as described in `15_Themes_Outputs_Tools_and_Diagnostics.md` §2.

Out-of-range values are clamped on load. A file that cannot be parsed is renamed `settings.json.corrupt-<timestamp>`, and defaults are used; a message in the status bar says so.

**Settings written by a newer Tibia Foundry** (`Version` greater than this program's): options this version does not know are kept and written back unchanged. A one-time backup `settings.json.v<N>.bak` is made, and the status bar says so. *Reset to defaults* also keeps them.

**Logs**: `Documents\Tibia_Foundry\Logs\TibiaFoundry-<yyyyMMdd>.log` (portable: `TibiaFoundry-Data\logs`; or the folder set in Settings). The status-bar log button opens the folder; *Diagnostics → Log* filters them. Level, retention (default 14 days) and the size per day (default 20 MB) are set in Settings → Notifications and logging, the total size (default 200 MB) in Settings → Diagnostics.

**Backups and temporary files** (5.0): backups of replaced and removed files go to `Documents\Tibia_Foundry\Backups` (6.0; an existing 5.0 backup folder `%LOCALAPPDATA%\TibiaFoundry\backups\` is kept while it holds backups; portable: `TibiaFoundry-Data\backups`), one folder per operation with a `backup.json` manifest. Temporary working files go to `%TEMP%\TibiaFoundry\` (portable: `TibiaFoundry-Data\temp`). Both folders can be changed in Settings → Files and folders. Retention and clean-up are set in Settings → File safety.

**Cache**: `%LOCALAPPDATA%\TibiaFoundry\cache\` (portable: `TibiaFoundry-Data\cache`). It holds the last good TibiaWiki item table and is safe to delete.

**Command line**:

| Argument | Effect |
|---|---|
| `--ui-snapshot <folder>` | Automated UI rendering and window checks, then exit |
| `--self-test <folder>` | Headless component self-test and the application self-check (read-only), then exit (exit code 0 = no failures, 1 = failures) |
| `--theme light\|dark` | Force a theme for this run |
| `--size <W>x<H>` | Initial window size for this run |

**Keyboard**:

| Key | Action |
|---|---|
| F6 | Toggle the built-in light and dark themes |
| Esc | Cancel the running operation |
| Ctrl+F | Focus the Appearances search |
| Delete | Remove the selected sheets from the Extract Sprites queue |
| Double-click | Add or remove sheets (Extract Sprites); open an appearance (Compare) |
