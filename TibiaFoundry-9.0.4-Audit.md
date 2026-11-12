# Tibia Foundry 9.0.4 – Audit and Release Decision

Audit of Tibia Foundry 9.0.3 → 9.0.4 (2026-11-11). Measured results were measured on one machine (Windows 11 Pro, 16 logical processors, .NET SDK 10.0.404, Tibia client 15.33.124f6c installed); what was reasoned from the code rather than observed is labelled **(by reading)**.

## 1. Baseline

| Fact | Finding |
|---|---|
| Source audited | `TibiaFoundry-9.0.3-Source` (version 9.0.3 in `src/Directory.Build.props`), 330 C# files, 48 XAML files, about 74,000 lines |
| GitHub `source` branch | at **9.0.2** (commit `e2d4d19c`, tag `v9.0.2`). 9.0.3 was never pushed, tagged or released |
| GitHub `main` branch | the deployed website (GitHub Pages, `CNAME` tibiafoundry.com), showing 9.0.2, plus the unrelated `games/` and `pokemon/` folders, `tools/validate-pokemon.mjs` and a Pages validation workflow – all left untouched |
| 9.0.2 → 9.0.3 | 17 files really differ (the rest of a file comparison is line endings); committed as "Tibia Foundry 9.0.3" on top of the `source` history before any 9.0.4 change |
| Baseline build | Release, 0 warnings, 0 errors |
| Baseline tests | 684 passed, 32 skipped (need client versions or files that are not on this machine), 0 failed |
| Dependencies | no package with a known vulnerability (`dotnet list package --vulnerable --include-transitive`) |

### How the audit was done, and its limits

The whole solution was built with the compiler's unused-code and disposal analyzers switched on (IDE0005, IDE0051, IDE0052, IDE0059, IDE0060, CA1001, CA1812, CA1823, CA2000, CA2016, CA2213); the code was searched for the patterns that produce the defect classes of the assignment (`async void`, blocking waits on tasks, process starts, archive extraction, SQL built from text, XML parsing, culture-dependent parsing and formatting, translated text in comparisons and static data, handlers that catch `IOException` only, static event subscriptions, synchronous dispatcher calls, timers, secrets and personal paths); and these files were read in full: `App.xaml.cs`, `SystemServices.cs`, `AtomicFile.cs`, `PathGuard.cs`, `SettingsStore.cs`, `HttpFetcher.cs`, `ProcessingQueue.cs`, `ThumbnailCache.cs`, `AssetIndexer.cs`, the search, backup and restore parts of `AssetLibrary.cs`, the flag code of `ItemImporters.cs`, `CatalogComparison.cs`, `InstallationFinder.cs`, and the release and website scripts.

**Not every line of the 74,000 was read.** The view models, the XAML and the format decoders were covered by the searches, the analyzers and the existing tests, not by reading them from top to bottom. Findings outside what was searched for may remain.

## 2. Findings

Severity: CRITICAL (data loss, security compromise, widespread failure) · HIGH (significant bug or bottleneck) · MEDIUM · LOW. "Confirmed" means the defect follows directly from the code and a regression test now covers it; "potential" means it needs a condition that was not reproduced.

**No CRITICAL finding.**

| # | Severity | Kind | Where | Finding | Status |
|---|---|---|---|---|---|
| F-01 | HIGH | Data consistency, confirmed | `Core/Items/ItemImporters.cs` `ClientFlags`, `FromItemsXml`; `Core/Items/CatalogComparison.cs` `BehaviorFlags` | Four item properties were written into catalogs in the interface language, and three behaviour-sensitive names were compared in it. Catalogs made in different languages disagree; exports mix languages; `BehaviorFlags` is filled once, in whatever language was active first | Fixed: `ItemFlags`; read-time mapping in `ItemCatalogStore.Load` |
| F-02 | HIGH | Functional, confirmed | `Core/Library/AssetIndexer.cs` `FolderCategories` | Folder names of Tibia Foundry's own exports matched against translated text (and against capitalized German text after lower-casing the path): no match outside English | Fixed |
| F-03 | HIGH | Functional, confirmed | `Core/FileOps/PathGuard.cs` `IsInside`, `IsSameOrInside`, `ResolveInside` | A drive root keeps its separator when normalized; appending another matched nothing, so nothing was "inside" `E:\` and writing there was refused | Fixed |
| F-04 | MEDIUM | Reliability, potential | `Core/Library/ProcessingQueue.cs` `RunAsync` | `Record` and `Changed` ran before `Pump()` without protection; a throwing listener left every waiting job pending | Fixed |
| F-05 | MEDIUM | Reliability, potential | `Core/Settings/SettingsStore.cs` `Load` | Backup copies caught `IOException` only; `UnauthorizedAccessException` escaped during start-up | Fixed |
| F-06 | MEDIUM | Data safety, potential | `Core/Library/AssetLibrary.cs` `Restore` | If the restored index could not be moved in after the current one was set aside, the library was left without an index (the old one survived only under its `.before-restore-*` name) | Fixed: put back |
| F-07 | MEDIUM | Performance, measured | `Core/Library/AssetLibrary.cs` `SearchIds` | Built the full record, tags and fields of up to 100,000 assets to return their IDs | Fixed |
| F-08 | LOW | Reliability | `Core/Library/ThumbnailCache.cs` | `Usage` failed if a file vanished while listing; `Clear` on a locked file; `Fit` divided by zero for an empty image | Fixed |
| F-09 | LOW | Security | `Core/Net/ExternalEndpoints.cs` `TestServerAssetsBase` | Default download address was `http://`; the server answers 301 to `https://` | Fixed |
| F-10 | LOW | Logic string | `Core/Scripts/FileManagementScripts.cs` `ProtectedNames` | "System Volume Information" came from the string table (it happens to be untranslated) | Fixed |
| F-11 | LOW | Dead code | 57 files | 65 unused `using` directives; `ThemeEditorViewModel.BasePalette`, `SvgMetadataProvider.Svg`, `LzmaDecoder.DecoderState.PosStatesMax` unused; two unresolved documentation references | Removed / corrected |
| F-12 | MEDIUM | Usability, confirmed **(by reading)** | `Core/FileMetadata/MetadataFields.cs`, `Core/FileOps/OutputLocations.cs`, `Core/Legacy/LegacyFormat.cs`, `App/ViewModels/ProtoPropertyEditor.cs` | Labels held in static fields are read once; after switching the language in the running program they keep the old language until a restart | **Open** – documented as a known limitation; a fix changes the type of widely used properties and belongs in a feature release |
| F-13 | LOW | Performance, measured | `Core/Clients/InstallationFinder.cs` `Find` | 4.7 s to check three client folders on this machine. Runs only in first-run set-up and "Look for installations", behind a progress display | **Open** – not profiled further |
| F-14 | LOW | Unused state | `Core/Compression/LzmaEncoder.cs` `_isRepG1`, `_isRepG2`; `LzmaDecoder.cs` `_lp` | Assigned, never read | **Left** – harmless, and the LZMA code is verified byte for byte by existing tests |
| F-15 | LOW | Maintainability | `Directory.Packages.props` | Newer major versions exist (Magick.NET 15, NAudio 3, xunit.v3 4, Microsoft.Data.Sqlite 11); none fixes a vulnerability | **Left** for a feature release |
| F-16 | LOW | Disposal | `Core/Editing/AecPackage.cs`, `App/ViewModels/MetadataViewModel.cs`, `ScriptsViewModel.cs` | CA2000 on `CodedInputStream`/`CodedOutputStream` over memory and on `DataTable` – nothing unmanaged is held | **Left** |

### What was looked for and not found

* No `BinaryFormatter`, no type-name JSON deserialization, no XML parsed with DTDs enabled (every reader sets `DtdProcessing.Prohibit` and a null resolver).
* SQL: values are parameters; the only text placed into SQL is column lists, integer IDs and clamped numbers.
* Archive extraction and restore go through `PathGuard` or an explicit containment check; only known entries of a library backup are restored.
* Shell starts use `UseShellExecute` with a single value (never a command line); files are opened only for a fixed list of media types; URLs only for http(s).
* No blocking waits on tasks, no synchronous dispatcher calls, no secrets or personal paths in the source (one e-mail address in `THIRD-PARTY-NOTICES.txt` belongs to a third-party licence text).
* The four `async void` methods all handle their expected exceptions (two were fixed in 9.0 and 9.0.3).
* Culture: numbers written to data files use the invariant culture; the remaining culture-dependent formatting is in messages shown to the user.

## 3. Performance

Workload benchmarks (`tools/benchmarks`, same harness and work folder for both versions, median of the repetitions):

| Scenario | 9.0.3 | 9.0.4 | Note |
|---|---|---|---|
| `library-search-ids-x20` (20 × all IDs of the library), 5 reps | 5,657 ms, 425 MB allocated | **66 ms, 5 MB** | F-07: 283 ms → 3.3 ms per call |
| `library-search-50` (50 text searches), 5 reps | 1,351 ms | 1,267 ms | unchanged within noise |
| `library-index-warm`, 5 reps | 84.9 ms | 73.9 ms | unchanged within noise |

Start-up of the portable edition from a fresh folder (`tools/verification/startup_perf.ps1`, runs alternated):

| Run | 9.0.3 window after | 9.0.4 window after | 9.0.3 working set | 9.0.4 working set |
|---|---|---|---|---|
| 1 | 1,649 ms | 1,561 ms | 283 MB | 287 MB |
| 2 | 1,461 ms | 1,469 ms | 286 MB | 287 MB |
| 3 | 1,356 ms | 1,448 ms | 283 MB | 286 MB |

Start-up and memory are **unchanged**; nothing in 9.0.4 was meant to change them, and no improvement is claimed. The other benchmark scenarios were measured for 9.0.3 only (they exercise code 9.0.4 did not touch): catalog load 39 ms, appearances load 249 ms, compatibility check 606 ms, 300 sheets decoded 881 ms, 150 outfits rendered 825 ms, 300 GIFs 1,238 ms, cold library index 6,020 ms, client scan 4,547 ms, installation search 4,753 ms (F-13).

## 4. Data management

Storage stays as it is: settings and catalogs as JSON written atomically and flushed, the Asset Library in SQLite with an online backup. No format, schema or folder changed in 9.0.4, so there is **no migration**. The one data correction (F-01) is done when a catalog is read; files are not rewritten until the user imports a source again, and a catalog opened by 9.0.3 afterwards still reads (it would show the neutral English names).

## 5. Release version decision

1. **Critical findings:** none.
2. **High-priority findings:** F-01, F-02, F-03 – all fixed with small, local changes and regression tests.
3. **Architectural changes required:** none. The architecture (UI-free core, MVVM application, one version source, atomic writes, path guard, central HTTP fetcher) is sound; the defects were local.
4. **Data migration:** none. F-01 is corrected while reading.
5. **Breaking changes:** none. A catalog made in a non-English interface now shows and exports the four properties in English, which is what every other property already did.
6. **New capabilities:** none for users. For maintainers: the release-directory script and two website packages.
7. **Risks of staying on 9.0.4:** F-12 remains (labels after a live language switch); major dependency updates wait.
8. **Risks of moving to 9.1.0:** it would announce new capabilities that do not exist, and would invite bundling the larger changes (F-12, dependency majors) without the testing a feature release needs.
9. **Recommendation: 9.0.4.** The findings are ordinary maintenance: bug fixes, one incremental optimization, clean-up. A high-priority bug fix does not need a minor version.
