# Tibia Foundry 8.8.1 – Performance Audit

Audit of Tibia Foundry 8.8.0 → 8.8.1 (2026-11-02). Everything below was measured on the machine described in §B. Static reasoning is labelled as such. Raw results: `tools/benchmarks/results/`.

---

## A. Project overview

**Source examined:** the complete 8.8.0 source tree (no git repository; a pristine copy was kept outside the tree as the baseline and for diffs).

| Project | Role | Size |
|---|---|---|
| `src/TibiaFoundry.Core` | UI-free engine: client formats (protobuf `.dat`, CIP LZMA sheets, BMP, legacy DAT/SPR, RCC), compatibility checks, client store/archive/inventory, Asset Library (SQLite), exports, scripts | ~230 files |
| `src/TibiaFoundry.App` | WPF/MVVM program (CommunityToolkit.Mvvm): 23 pages, dialogs, services, themes, localization | ~110 files |
| `src/TibiaFoundry.Tests` | xUnit v3 tests (unit, parity, view models, resources); some need the installed client | 673 tests (681 in 8.8.1) |
| `tools/release` | `package_release.py` (notices, ZIPs, source ZIP), `build_installer.py` (WiX 5 MSI), `make_notices.py`, `TibiaFoundry.wxs` | |
| `tools/verification` | `measure` (decode/memory tool), `startup_perf.ps1`, search scripts | |

**Architecture (relevant to performance):**

* Composition root `App.OnStartup` → `AppServices` → `MainViewModel` (pages from `BuiltInTools`) → `MainWindow`. After the window shows: start-up housekeeping (background), client choice (`ClientStartup`: first-run setup / remembered client), the first page, the **background compatibility check**, then the **launch check** of monitored installed clients (background, 2 threads).
* `ClientDataService` owns the shared client data (catalog, `SpriteRepository` with an LRU of 96 decoded sheets, appearances and other databases) and shares in-flight loads between pages.
* Shared hot paths used by many tools: `ProtoLoader`/`Utf8Repair` (every `.dat` load), `SpriteSheetDecoder` (LZMA → SHA-256 check → BMP; every sprite shown or exported), `ImageIO.SavePng`/`GifWriter.Write` → `AtomicFile` (every image export), `PhysicalClientFolder` (client file lookups), `FolderInspector`/`ClientScanner` (setup, Clients page, launch check), `AssetIndexer`/`AssetLibrary` (library, automatic export indexing).

## B. Profiling methodology

**Machine:** Windows 11 Pro 10.0.26200, 16 logical processors, .NET SDK 10.0.403 / runtime 10.0.14, Release builds. Real-time antivirus active (it visibly shapes file-system costs; see §C). Data on drive F: (source) and C: (client, `%TEMP%`).

**Dataset:** the installed live client 15.33.8791c6 (`%LOCALAPPDATA%\Tibia\packages\Tibia`): 5,084 sprite sheets, 43,518 objects, 1,480 outfits, 6,250 asset files (129 MB); the package folder holds 17,528 files / 4.9 GB including personal data (screenshots). The launcher folder above it has a further ~7,000 screenshot files. **Only read**; no client file was modified.

**Tools:**

| Tool | Used for |
|---|---|
| `tools/benchmarks` (new, Stopwatch + `GC.GetTotalAllocatedBytes` + GC counts + peak working set) | 21 workload scenarios; the same harness compiled against the 8.8.0 and the 8.8.1 Core (`-p:TfSrc=`) |
| `dotnet-trace` 10.0 (`dotnet-sampled-thread-time`, `report topN`) | wall-clock call-stack sampling of every hot scenario and of the running program (setup dialog, start-up) |
| `tools/verification/measure decode` | split of sheet decoding into LZMA / SHA-256 / BMP |
| `tools/verification/startup_perf.ps1` and an extended loop | time to main window, memory, CPU, start-up compatibility check duration from the program log |
| `--ui-snapshot` | every page in both themes: navigation, binding errors, warnings |
| `--self-test` | headless end-to-end check of the packaged programs |

**Method:** warm-up run, then N repetitions (5, or 3 for long scenarios) with a full GC before each; median reported, min/max kept in the CSVs; allocations are per repetition; peak working set is per process (cumulative across scenarios, so only indicative). Every optimization was measured on its own before the next one; changes without a measurable benefit were reverted (§D). Final numbers come from one back-to-back run of both builds (§E, table 1); an earlier run is shown for corroboration (table 2), because machine speed varied by up to ~40 % during the session (antivirus, background load) — compare within a table, not across.

**Limitations:**

* No Visual Studio profiler / PerfView / WPR were used (not installed; `dotnet-trace` covered the need). CPU% and disk counters were not recorded separately; thread-time sampling shows where wall time goes (CPU or waiting on I/O).
* UI navigation could not be timed below the `--ui-snapshot` harness's fixed settle wait (~1.6 s per page); only pages with real work show differences.
* In-program start-up was measured with fresh portable data folders (no first-run setup when `ClientSetupCompleted` is preset). Long-running sessions (hours) were not profiled.
* One test environment (one client version, one disk, one antivirus).

## C. Original performance findings (8.8.0)

Ranked by measured impact × how often users meet it.

| # | Location | Operation / evidence | Root cause | Kind | Priority |
|---|---|---|---|---|---|
| 1 | `Protobuf/ProtoLoader.cs` `Utf8Repair.Rewrite` | Every `appearances.dat` load: **1.85 GB allocated, 231 Gen0 GCs** for a 5 MB file; trace: Rewrite 50 %, GC polling 26 %, `MemberwiseClone`/`Array.Clone` 19 %. The live client has 2 Latin-1 names, so *every* load takes this path (confirmed in the program's logs since October). Also doubles **Compare Files** (3.8 GB). | Each nested message copied to a new array + `MemoryStream` + `ByteString` on every level | Memory, CPU | **Critical** |
| 2 | `Compatibility/CompatibilityChecker.cs` `CheckCatalog`; `DataValidation.MapIn/Sounds` | Start-up / update compatibility check: **3.3–4.9 s** benchmark, 4.0–4.6 s in the program log; trace: **`File.Exists` 81 %** | ~10,000 `File.Exists` calls for sheets (counted twice), one per map image, two per sound; each costs ~0.2–0.4 ms with real-time scanning | Disk, start-up | **Critical** |
| 3 | `Compatibility/SchemaInspector.cs` `Walk` | Part of every check: copies every nested message (`data.ToArray()`) and every length-delimited field | Same pattern as #1 | Memory | High |
| 4 | `Clients/ClientScanner.cs` `FindResourceFiles` ← `FolderInspector.Capabilities` ← `InstallationFinder.Find` | First-run setup "find installation": **19.8–29.4 s, 2.67 GB allocated**; trace: `FileStream` ctor 82 %. Windows' uninstall entry points to the launcher folder, which is inspected as a client; the search recurses into nested packages' screenshot folders | Opening every file with a **64 KB buffered, sequential-scan** stream to read 4 bytes (reads 64 KB of each file); callers needing "is there one?" enumerate all | Disk, memory, first-run UX | **Critical** |
| 5 | `Common/AtomicFile` via `ImageIO.SavePng`, `GifWriter.Write` | Extracting 300 sheets (12,780 PNG): **29.5–32.8 s**; PNG encoding only ~1 %; `AtomicFile.Write` 73 %, of which **re-opening the temp file to verify (33 %)**, rename (18 %) | Opening a just-written file triggers an antivirus scan before the read | Disk | High |
| 6 | `Library/AssetIndexer.cs` `IndexFiles` | Cold index of 12,780 files: **14.5–15.1 s**; strictly sequential read + hash + decode + metadata per file | No overlap of per-file I/O latency | Disk, CPU | High |
| 7 | `Library/AssetIndexer.cs` `IndexFiles` | Rescan of an unchanged folder: **2.8–3.0 s**; trace: `FileInfo.Exists` 98 % | Every file is stat-ed again although the directory enumeration already returned size and time | Disk | High (runs on every library refresh) |
| 8 | `Imaging/BmpDecoder.cs` | 20 % of sheet decoding (3.6–3.8 s over all sheets) | Per-pixel unpacking with mask arithmetic although the client layout already is the in-memory layout | CPU | Medium (every sprite) |
| 9 | `Library/AssetLibrary.cs` `Search` | 50 searches: 1.7–2.2 s | Filter (LIKE + EXISTS subqueries) evaluated twice: COUNT query + page query | CPU (SQLite) | Medium |
| 10 | `Compression/LzmaDecoder.cs` | 62 % of sheet decoding; 32–56 % of outfit render / GIF build | Inherent: serial range decoding (~200 MB/s already) | CPU | Investigated; no change kept |

Investigated and **not** found to be bottlenecks: catalog loading (≈35 ms), folder inspection of one client (≈250 ms), the quick launch check at each start (≈0.9–1.1 s, background, 2 threads), client hashing (`ClientScanner.Scan`/`ClientInventory`: I/O bound on 0.5–1 GB, already parallel with pooled 1 MB buffers), thumbnail cache (bounded, decoded off the UI thread, frozen bitmaps), app start-up to window (≈1.5 s; no blocking work before the window).

## D. Implemented improvements

| # | Files | Change | Reasoning / verification |
|---|---|---|---|
| 1 | `Protobuf/ProtoWireReader.cs` (new), `Protobuf/ProtoLoader.cs` | `Utf8Repair` rewritten as two passes over slices of the one input: *Measure* (rewritten size of every nested message, in pre-order) then *Write* into a single output array of the final size. Same canonical varints, same Latin-1→UTF-8 conversion (`Utf8.IsValid` for the strictness check). Damaged wire data still surfaces as `InvalidDataException` → `DataFileException`. | Output **byte-identical** to 8.8.0 on the real file and a synthetic file (test compares with the 8.8.0 algorithm); fuzz test caught and drove one fix during the work. |
| 2 | `Compatibility/SchemaInspector.cs` | Walks spans with `ProtoWireReader`; truncation still reported as `Truncated`. | Drift report identical (equivalence dump). |
| 3 | `Clients/ClientFiles.cs`, `Compatibility/CompatibilityChecker.cs`, `Compatibility/DataValidation.cs` | `PhysicalClientFolder.WithListing`: one directory listing per check, case-insensitive, used for sheets, map images and sounds. Names Windows would resolve differently from a listing (sub-paths, `:`, `~`, trailing dot/space) still go to the file system; a folder that cannot be listed falls back to per-file queries. Scoped to one check only (no stale cache). | Full compatibility report identical; tests compare listing answers with `File.Exists`. |
| 4 | `Clients/ClientScanner.cs`, `Clients/FolderInspector.cs` | Resource-file header read unbuffered (4 bytes); new `HasResourceFile` stops at the first match; `FindResourceFiles` keeps its sorted full result. | Same classifications of every found folder; new test for both functions. |
| 5 | `Common/AtomicFile.cs`, `Imaging/ImageIO.cs`, `Imaging/GifWriter.cs` | `AtomicFile.WriteVerified`: write, flush, read back **through the same handle**, compare, then move into place; temp file removed on failure. Same atomicity and verification guarantee. | Pixels of extracted PNGs identical; new test (replace, empty, no temp leftovers). |
| 6 | `Library/AssetIndexer.cs` | Files needing description are inspected in parallel per batch of 200 (`IndexOptions.MaxDegreeOfParallelism`, default min(cores−1, 8)); results, problems and database writes keep file order; unexpected exceptions still propagate. | Existing incremental-index tests pass; new test checks problem order. |
| 7 | `Library/AssetIndexer.cs` | Root scans pass the enumerated `FileInfo` (size, time from the directory listing) to the comparison instead of stat-ing each file again. | Identical result (12,780 unchanged). |
| 8 | `Imaging/BmpDecoder.cs` | 32-bit images with the standard ARGB masks (every client sheet) are copied row by row; other layouts use the original path. | 400+ decoded sheets identical (and every sheet is SHA-256-checked anyway). |
| 9 | `Library/AssetLibrary.cs` | `Search` returns the total via `COUNT(*) OVER ()` with the page; falls back to a COUNT query for pages past the end. | New test: totals on a middle page, past the end, and for no match. |

**Tried and reverted (no measurable benefit):** an LZMA decoder rewrite (ref-struct range coder, block copies) — A/B over all 5,084 sheets: 10.2/10.6 s old vs 10.9/9.9 s new, within noise; a portable build **without** single-file compression — 85 MB less memory at start but ~400 ms *slower* to the main window (1.87 s vs 1.47 s), so compression stays.

**Not changed on purpose:** the verified atomic write still renames a temp file (atomicity), sheet SHA-256 verification stays (integrity), the launch check's first-run full hashing stays (it creates the baseline).

## E. Benchmark comparison

**Table 1 – final back-to-back run** (`tools/benchmarks/results/8.8.0-baseline.csv`, `8.8.1.csv`; median ms, allocations per run).

| Scenario | 8.8.0 ms | 8.8.1 ms | Change | 8.8.0 alloc | 8.8.1 alloc |
|---|---:|---:|---:|---:|---:|
| Load appearances | 403.8 | 191.3 | **−53 %** | 1,848 MB | 79 MB (**−96 %**) |
| Compatibility check (cold) | 4,915.8 | 417.8 | **−92 %** | 2,180 MB | 147 MB |
| Compatibility check (appearances loaded) | 4,758.9 | 169.3 | **−96 %** | 332 MB | 67 MB |
| Decode 300 sheets (1 thread) | 1,110.0 | 675.9 | **−39 %** | 351 MB | 351 MB |
| Decode all 5,084 sheets (parallel) | 3,426.1 | 2,332.4 | **−32 %** | 5,992 MB | 5,992 MB |
| 5,000 thumbnails in list order | 846.0 | 596.6 | **−29 %** | 188 MB | 188 MB |
| Render 150 outfits | 1,047.7 | 688.5 | **−34 %** | 437 MB | 437 MB |
| Build 300 GIFs | 1,424.3 | 1,028.6 | **−28 %** | 333 MB | 333 MB |
| Compare appearances | 1,114.3 | 524.6 | **−53 %** | 3,794 MB | 257 MB (**−93 %**) |
| Find installation (setup) | 29,423.2 | 4,564.5 | **−84 %** | 2,669 MB | 98 MB (**−96 %**) |
| Extract 300 sheets → 12,780 PNG | 32,828.1 | 22,618.2 | **−31 %** | 692 MB | 651 MB |
| Library: index 12,780 files (cold) | 15,127.1 | 7,245.8 | **−52 %** | 1,071 MB | 1,072 MB |
| Library: rescan, unchanged | 2,971.6 | 65.6 | **−98 %** | 11 MB | 10 MB |
| Library: 50 searches + facets | 1,736.0 | 1,307.0 | −25 % | 4 MB | 4 MB |
| Client scan (hash version files) | 4,542.2 | 4,375.1 | ≈ (noise) | 78 MB | 36 MB |
| Full inventory hash | 5,205.0 | 4,549.0 | ≈ (I/O, noise) | 83 MB | 83 MB |
| Launch check (quick) | 1,096.9 | 895.9 | ≈ | 119 MB | 119 MB |
| Catalog load / folder inspect | 37.6 / 256.0 | 36.2 / 250.3 | ≈ | | |

**Table 2 – earlier run** (individual change measurements, same harness; machine was faster then): appearances 315 → 215 ms (1.85 GB → 79 MB); compatibility check 3,269 → 563 ms; with data loaded 2,847 → 231 ms; extraction 29.5 → 21.5 s; cold index 14.5 → 7.5 s; rescan 2.83 → 0.07 s; 50 searches 2.20 → 1.13 s; find installation 19.8 → 3.35 s. BMP unpacking over all sheets 3.79 → 0.47 s (`measure decode`).

**In the program (portable build, fresh data folder, setup preset, 4 runs each):** start-up compatibility check from the log **4.0–4.6 s → 0.8–0.9 s**; time to main window 1.5–1.8 s both (unchanged: no blocking start-up work was found); working set after 25 s ≈ 320–370 MB both; CPU in the first 25 s 31 → 28 s (the rest is the first-time launch-check baseline, by design). `--ui-snapshot`: 46 page renders, 0 binding errors and 0 warnings in both builds; Compatibility page 4.6 → 2.6 s, other pages unchanged (harness settle time).

**Trade-offs / regressions:** none measured. Peak working set of the benchmark process unchanged (~400 MB). Parallel indexing uses up to 8 threads during a library scan (bounded; database writes remain single-threaded).

## F. Testing results

* **Build:** clean Release build of the solution after deleting all `bin`/`obj`: 0 warnings, 0 errors.
* **Automated tests:** 681 total — **648 passed, 32 skipped** (need `04_reference` parity data or opt-in environment variables), **1 failed**: `ItemCatalogTests.TheClientsOwnNamesAreVerifiedAndEveryObjectIsListed` expects 43,516 objects of client 15.33.9d148f; the installed client is now 15.33.8791c6 with 43,518. It fails identically on unmodified 8.8.0 (640 passed / 32 skipped / 1 failed); it is environmental and was left unchanged.
* **New tests:** `PerformanceOptimizationTests` (8): Latin-1 repair vs the 8.8.0 algorithm (synthetic and the installed client's appearances), damaged data, schema drift samples, folder listing vs file system, verified writes, resource-file search, search totals and ordered problems with parallel indexing.
* **Equivalence dump** (`TibiaFoundry.Benchmarks <package> <work> dump`, 992 checks, 8.8.0 vs 8.8.1): repaired bytes and schema drift of all five data files, parsed appearances, the full compatibility report, 400+ decoded sheets and extracted PNG pixels — **all identical**.
* **Runtime:** portable `--self-test` from a clean extracted folder: 13/13 PASS; GUI launch shows "Tibia Foundry 8.8.1 starting", portable data next to the exe.
* **Remaining warnings/limitations:** see §B; the fuzz test (`FuzzedDataFilesFailWithDataFileExceptionOnly`) passes.

## G. Outstanding opportunities

1. **Export file-system cost** (still ~70 % of extraction): temp-create + rename per file under antivirus. Options: write each batch into a staging folder and move it once (changes the "per-file atomic" model; needs design), or let users exclude the output folder from scanning (documentation).
2. **LZMA decoding** (62 % of sheet decode): a faster decoder would need an unsafe/SIMD-free tight loop or a native library; measured gain of a managed rewrite was nil. A persistent decoded-sheet cache (validated by the sheet's SHA-256 file name) would avoid repeated decoding across sessions at the cost of disk space.
3. **Library search**: full-text index (SQLite FTS5) for name/path/tag search on very large libraries.
4. **Appearances retained by the compatibility check**: the check loads appearances when no page has, then drops them; publishing them to `ClientDataService` would save a later load at the price of ~150 MB held for users who never open those pages — a policy decision.
5. **Launch check first run** hashes the whole client (5–7 s background per client); already background and bounded.
6. Not measured: multi-hour sessions, very large item catalogs, map tools on full maps.

## H. Final assessment

8.8.1 is measurably faster where users wait most: start-up's background compatibility check (~5× faster), the first-run installation search (6× faster, 27× fewer allocations), every appearances load and file comparison (~2×, ~20× less memory), sprite decoding for all sprite tools (~30 %), image exports (~30 %) and the Asset Library (rescans ~45×, indexing ~2×). Behavior and outputs are unchanged (verified byte for byte where possible). Remaining limits are file-system bound (export writes, hashing) or algorithmic (LZMA). Recommended next priorities: §G 1, then 2.

## I. Release build verification

Built from this source tree (8.8.1), Release, win-x64, self-contained, ReadyToRun.

| Deliverable | Build | Validation |
|---|---|---|
| `TibiaFoundry-8.8.1-Setup.msi` | WiX 5.0.2 via `build_installer.py` from the folder edition | Genuine MSI (OLE compound file `D0 CF 11 E0`); decompiled: Name "Tibia Foundry", Version 8.8.1, UpgradeCode `{6F0C2D5E-8A1B-4E57-9B3C-71D4A2E9F810}` (unchanged), new ProductCode, MajorUpgrade, `ProgramFiles64Folder\Tibia Foundry`, 261 files, Start-menu shortcut, ARP icon/URL. Installed in an isolated Windows Sandbox: 8.8.0 MSI → 8.8.1 MSI upgrade exit 0, **one** "Apps & features" entry (8.8.1), exe FileVersion 8.8.1.0 / ProductVersion 8.8.1, shortcut present, no portable marker, GUI window shown; uninstall exit 0, program folder and shortcut removed, user data in AppData/LocalAppData/Documents kept. The installed `--self-test` there reported 2 failures, both attributable to the sandbox: no Tibia client installed, and SQLite "disk I/O error" because the report folder was the host-mapped share (WAL mode); a confirming re-run with a local folder was stopped at the user's request and is **not** done. Not code-signed (no signing configured). |
| `TibiaFoundry-8.8.1-Portable.zip` | `dotnet publish -p:PublishProfile=Portable` (single file) + `package_release.py` | ZIP test OK (3 entries: `TibiaFoundry.exe`, `README-Portable.txt`, `THIRD-PARTY-NOTICES.txt`); extracted to a clean folder: FileVersion 8.8.1.0, `--self-test` 13/13 PASS with the installed client, portable data in `TibiaFoundry-Data` next to the exe, GUI starts. |
| `TibiaFoundry-8.8.1-Source.zip` | `package_release.py` (excludes `bin`, `obj`, `dist`, `.vs`, `Release`, …) | See `ReleaseManifest.md` (integrity, contents, clean rebuild from the extracted copy). |

Version consistency: `Directory.Build.props` (8.8.1 / 8.8.1.0), executable version resources, program log ("Tibia Foundry 8.8.1 starting"), MSI ProductVersion, ARP DisplayVersion, portable README, source README. Checksums and sizes: `Release\8.8.1\ReleaseManifest.md`.
