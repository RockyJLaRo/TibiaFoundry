# Testing Report – Tibia Foundry 8.7.0

Everything reported as passed here was executed and its result observed. Automated results and manual inspection are reported separately. Failed and unexpected results are listed, not hidden.

## 8.7.0 (2026-10-31)

8.7.0 is the fansite toolkit release ([23_Fansite_Toolkit.md](23_Fansite_Toolkit.md), `FANSITE_TOOLKIT_TRANSFORMATION_REPORT.md`). Release checks (`11_Maintenance_Guide.md` §11), run on this machine with the installed client 15.33.8791c6:

* **Build** (`dotnet build -c Release`): 0 warnings, 0 errors.
* **Automated tests**: **603 tests, 570 passed, 32 skipped, 1 failed**, 1 min 58 s. New: `FansiteWorkflowTests` (8, all pass).
  * The failure is `ItemCatalogTests.TheClientsOwnNamesAreVerifiedAndEveryObjectIsListed`: it pins 43,516 objects and the installed client now has 43,518. It fails the same way on the unmodified 8.6.0 source; it is not caused by 8.7.0.
* **Published executables** (folder edition, and the portable edition copied into a fresh, empty folder):
  * `--self-test`: 13 PASS, 0 failures for each; both report *Tibia Foundry 8.7.0*. The client is "unfamiliar but compatible" (15.33.8791c6 is not in `TibiaFormat.ValidatedClientVersions`).
  * `--ui-snapshot --theme dark`: 43 PASS, 0 FAIL, 0 binding errors, 0 logged warnings or errors for each; all 21 pages, Home included, render.
* **Not checked by hand**: the Export tab's preset selector in the running program (covered by `FansiteWorkflowTests` and the compiled XAML).

## 8.0.0 (2026-10-21)

8.0.0 implements phases 0–6 and 8 of the client archiving plan of 2026-10-20 (`05_verification/8.0.0/plan/`, approved with all recommended defaults D1–D14; phase 7, storage conversion, not approved):

- a baseline (`15_baseline_7.3.0`, copies whose checksums match the published 7.3.0 zips);
- Item Discovery removed;
- complete archives in two storage modes;
- client folders from anywhere;
- the Client Installation Rebuilder;
- the check at every start;
- first-run setup and the client at start.

Evidence is in `05_verification/8.0.0/`.

**Release checks (`11_Maintenance_Guide.md` §8 and §11):**

* **Automated tests** with `TF_FULL_SPRITES=1`: **522 tests, 511 passed, 0 failed, 11 skipped**, 2 min 16 s (`test_results/tests_8.0.0.txt`). The build had 0 warnings and 0 errors.
  * The 11 skips are opt-in checks:
    * live services (3);
    * a reference conversion, real third-party item data, and Explorer opening a folder;
    * the real installed clients: 7.3's scan and import, 8.0's inventory and launch check;
    * a copy of a real 7.3 archive.

    The 8.0 opt-in checks were run separately (below).
  * **Removed with Item Discovery:** its 5 test classes (`ItemDiscoveryTests`, `ItemDiscoveryCaptureTests`, `ItemDiscoveryObsTests`, `AutomaticHoverTests`, `ObsWebSocketTests`).
  * **New:**
    * `ClientInventoryTests` (9, plus 1 opt-in);
    * `ClientArchiveTests` (14);
    * `ClientMonitorTests` (8, plus 1 opt-in);
    * `ClientStartupTests` (5);
    * `ClientSwitchTests` (2);
    * `RemovedFeatureTests` (6);
    * `ClientsPageTests` rewritten (5, plus 1 opt-in).
  * **Existing tests changed:**
    * the 7.3 reminder tests now drive the launch check;
    * the quick-look test was removed with the quick look;
    * three version checks expect 8 instead of 7;
    * a settings test expects the current format.
* **Reverse checks:** **126 of 126 detected**. 48 checks of Item Discovery and the two of the 7.3 reminder (H90, H91) were retired; H96–H111 are the 16 new ones (`release/mutation_report.txt`).
  * The full run detected 124. H09 and H10 named tests that had been removed with Item Discovery, so no test ran.
  * Their safeguards still apply: decisions in the Item Catalog are appended records, and old catalogs hold client observations. Two replacement tests were added, and both checks are now detected.
* **Removed-feature search** (`tools/verification/removed_feature_search.py`, new): 0 unexplained (`release/removed_feature_search.txt`).
* **The real clients, read only:**
  * **Inventory** (`TF_CLIENT_ARCHIVE_MEASURE=1`, `phase2/installed_packages_inventory.txt`):
    * live 15.33.c9e45e: 8,601 files, 637.1 MB, in 3.6 s twice, with the same fingerprint;
    * test 15.25.0e418e: 13,466 files, 726.4 MB, 4,976 of them in no client list;
    * personal data left out with counts; screenshot names never written.
  * **Launch check** (`TF_LAUNCH_CHECK_MEASURE=1`, `phase5/launch_check_installed.txt`):
    * Full: live 3.9 s, then 3.6 s; test 6.2 s, then 5.8 s;
    * Quick: 0.5 s each, reading 4 files;
    * baselines of 1.9 MB and 3.0 MB; the launcher was not running.
  * **A copy of your 7.3.0 archive** (`TF_OWNER_STORE_COPY`, `phase3/owner_7.3_store_labels.txt`): both revisions are labeled "Tibia Foundry files only (7.3)" and offered as *Restore folder…*; Verify found 2 revisions, 14,198 files, 0 problems; the manifests were unchanged.
  * **15.33.c9e45e validated** (`client_15.33.c9e45e/validation.txt`): every file except `package.json` and its version file is byte-identical to 15.33.9d148f.
* **Published builds** (`dist/TibiaFoundry-8.0.0-*`):
  * the folder edition has 255 files: 7.3.0's 257 without the Windows SDK projection (`Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`), which only Item Discovery needed;
  * zips of 80.0 MB (portable) and 77.3 MB (folder), against 93.4 MB and 88.3 MB for 7.3.0; the difference is the removed projection;
  * `--self-test`: **12 of 12 PASS** for both editions, the portable one from an empty folder; 0 errors and 0 warnings in the application self-check; 31 tools registered (Item Discovery out, the Rebuilder in) (`release/self_test/`);
  * the folder edition's self-test read your per-user settings and left them unchanged.
* **`--ui-snapshot`** (run with your OK, since it opens windows): **43 of 43** window checks PASS, 0 binding errors, for:
  * the folder edition (1400 × 900);
  * the portable edition (1400 × 900);
  * the portable edition in a small dark window (940 × 620).

  The Item Discovery check was removed with the tool. The Clients page and the Rebuilder render in both themes, and no run created a client archive (`release/ui_snapshots/`).
  * The first round of runs also passed 43 of 43. The screenshots showed the navigation bar cutting "Client Installation Rebuilder" off mid-word. Long page names now end with an ellipsis (the tooltip shows the full name), and both editions were published and checked again.
* **Settings between 7.3.0 and 8.0.0** (run with your OK), both portable editions taking turns on one data folder (`migration/migration_check.txt`):
  1. 7.3.0 writes format 6. The reminders were then turned off and the creator and theme set, as a 7.3 user would have them.
  2. 8.0.0 upgrades the file to format 7:
     * it keeps the creator and theme;
     * it counts setup as done, so no setup is shown;
     * it uses the remembered client;
     * reminders off became *Only show it on the Clients page*;
     * the 7.3 file is kept as `settings.json.v6.bak`;
     * no client archive is created, and the log has no error.
  3. 7.3.0 keeps the 8.0.0 values and does not downgrade the format.
  4. 8.0.0 afterwards still has every value, with no error in the logs.
* **The check at every start in the published program:** 8.0.0 was left open 60 s in that folder.
  * The live client was checked in 5.7 s.
  * The test client took 38.9 s: its 726 MB were not in the disk cache and are read by two background threads.
  * Both baselines were written (2.0 MB and 3.1 MB). The archive itself was not created.
  * In the round trip's 8.0.0 starts the window closed after about 11 s, before the check finished; the program exited normally.
* **Preserved files** (`release/preserved_files_check.txt`):
  * the original program, 24 of 24;
  * every baseline from 2.0.0 to 7.3.0;
  * the 20 entries of earlier releases in `dist/SHA256SUMS.txt`.

  `dist/SHA256SUMS.txt` and both zips were written through temporary files and moved into place.
* **Names search** over all 249,953 files, including the 8.0.0 zips, with `15_baseline_7.3.0` among the preserved folders: 0 unexplained (`release/obsolete_names_search.txt`).
* **Not run:**
  * a check while you play: whether `client.exe` changes files outside the personal folders (`08_…` L47);
  * starting a rebuilt client (never done by Tibia Foundry);
  * Find installation on a PC with a Steam installation or an uninstall entry;
  * the TibiaWiki import check (live wiki, unchanged code);
  * the start-up measurement.

**Found and fixed during development:**

* **Two pages kept the previous client's default file after a switch** (Compare Files, Client Converter). The new all-pages switching test found them.
* **A race in the editors' folder protection:** a global list replaced by weak owners. The Item Discovery removal exposed it in parallel tests.
* **Three slips caught by the build or the tests:**
  * a script edit wrote nine lines of `ClientLibrary.cs` in the wrong encoding;
  * a property name clashed with a static class;
  * a local function hid `System.IO.File`.
* **Folder inspection was too slow** (2.9 s): one directory listing replaced the file checks, and it now takes 0.4 s.
* **The timing test `LargeCatalogsLoadAndFilterQuickly` failed once** in a parallel run, and passed alone and in every later full run.

## 7.3.0 (2026-10-16)

7.3.0 implements phases 0–4 of the client management plan of 2026-10-16 (`05_verification/7.3.0/plan/`, approved with your decisions on the store locations, local-only operation, RCC files, the launcher and the reminder): a baseline (`14_baseline_7.2.0`), the content-addressed client store, its integration with the readers, the Clients page, and this release. Evidence: `05_verification/7.3.0/`; paths of the temporary working folder are shown as `<scratch>`.

**Release checks (`11_Maintenance_Guide.md` §8 and §11):**

* **Phase 0 baseline**: before any change, 490 tests, 482 passed, 0 failed, 8 skipped (`test_results/phase0_baseline_tests.txt`). The baseline's checksums match the published 7.2.0 zips.
* **Automated tests** with `TF_FULL_SPRITES=1`, the Tibia client closed: **535 tests, 525 passed, 0 failed, 10 skipped**, 3 min 22 s (`test_results/tests_7.3.0.txt`).
  * The 10 skips are the 8 documented opt-in checks and the 2 new opt-in measurements on the real clients, which were run separately (below).
  * 45 tests are new: `ClientStoreTests` (31), `ClientIntegrationTests` (10), `ClientsPageTests` (4).
  * One existing test changed: `LocationTests.ChosenLocationsAreSavedWhereEarlierVersionsKeepThem` expected the settings format number 5 and now expects the current format (6, which only adds the client store settings). No other existing test was changed.
* **Reverse checks**: **160 of 160 detected**, H78–H95 being the 18 new ones for the client store. The restored build compiled (`mutation_report.txt`).
  * The full run detected 159. H80, a header scan that reads the personal folders, was missed: the second rule still dropped what it found, so no test failed.
  * A test now checks that the scan never opens those folders, and H80 is detected.
* **The real clients, read only** (`measurements/`):
  * **Scan** (`TF_CLIENT_STORE_MEASURE=1`): live 15.33.9d148f 7,099 files, 238.3 MB, in 3.6 s; test 15.25.0e418e 7,040 files, 235.8 MB, in 4.0 s.
  * **Lists and RCC files**: every file matched the client's own list. Each client has one RCC file, and live's differs from test's.
  * **Import into a temporary store**: live in 16.3 s; then test in 2.4 s with only 26.4 MB new.
  * **Unchanged rescans**: 4.2 s and 3.8 s, nothing written.
  * **Read-back**: sprites read from the store equal those read from the client folders, and the catalog hashes match.
  * **Lookups**: 100,000 took 14–25 ms.
  * **Store size**: 474.1 MB as separate copies, 264.8 MB stored (44% saved). The client folders' files were the same before and after.
* **Published builds** (`dist/TibiaFoundry-7.3.0-*`):
  * the folder edition has 257 files, as in 7.2.0;
  * zips of 93.4 MB (portable) and 88.3 MB (folder);
  * `--self-test`: 12 of 12 PASS for both editions, the portable one from an empty folder, with 0 errors and 0 warnings in the application self-check and 31 tools registered (`self_test/`). The self-tests created no client store.
* **Preserved files**: the original program 24 of 24, and every baseline from 2.0.0 to 7.2.0 matches its checksums (`preserved_files_check.txt`). `dist/SHA256SUMS.txt` was written through a temporary file.
* **Names search** over all 253,321 files, including the 7.3.0 zips, with `14_baseline_7.2.0` added to the preserved folders: 0 unexplained (`obsolete_names_search.txt`).
* **`--ui-snapshot`** (run with your OK, since it opens windows): **44 of 44** window checks PASS, 0 binding errors, for:
  * the folder edition (1400 × 900);
  * the portable edition (1400 × 900);
  * the portable edition in a small dark window (940 × 620).

  The new Clients page renders in both themes, with the reminder for the installed live client 15.33.9d148f, which is not stored yet. No run created a client store (`ui_snapshots/`).
* **Settings between 7.2.0 and 7.3.0** (run with your OK), both portable editions taking turns on one data folder (`migration/migration_check.txt`):
  1. 7.2.0 writes format 5.
  2. 7.3.0 upgrades it to format 6. It keeps the creator, the theme and the test-server switch, adds reminders on with no client chosen, creates no store, and logs no error.
  3. 7.2.0 then keeps the 7.3.0 value (reminders off) and does not downgrade the format. As designed, it saves a copy of the newer file first (`settings.json.v6.bak`).
  4. 7.3.0 afterwards still has every value, with no error in the logs.
* **Not run**:
  * the end-to-end Item Discovery check, which moves the mouse and is unaffected by this release;
  * the TibiaWiki import check (live wiki, unchanged code);
  * the start-up measurement;
  * a manual session with the Clients page on your clients (import, switch, export, reclaim), which is yours to do.

**Found and fixed during development:**

* Five code issues:
  * Errors inside the parallel loops reached callers wrapped in `AggregateException`, so a changed file looked like a general failure. The loops now rethrow the first real error.
  * A Qt resource file without the `.rcc` extension in `bin` was found by its header but then dropped. Files are now included by kind.
  * The change summary paired sounds with the same name stem as "changed". Only data and RCC files are paired by name now; sheets are paired by their sprite range.
  * A new overload made an existing call `DataValidation.Map(db, null)` ambiguous. It was renamed `MapIn`, so the existing tests stay unchanged.
  * In the measurement test, the switch name had a doubled `TF_` prefix, so the test skipped. Corrected and rerun.
* Line endings: a few edited files had become LF only. They were converted back to CRLF, and the one test file whose content was unchanged matches the baseline byte for byte.

## 7.2.0 (2026-10-16)

7.2.0 implements phases 0–4 of the plan of 2026-10-15 (`05_verification/7.2_phases_0-3/`): a baseline (`13_baseline_7.1.0`), renames in the Item Catalog, the consolidations A1–A5, the OBS capture source and sessions on existing hotkey sets, and automatic hover. Evidence: `05_verification/7.2.0/` (release) and `05_verification/7.2_phases_0-3/` (per phase); paths of the temporary working folder are shown as `<scratch>`.

**Release checks (`11_Maintenance_Guide.md` §8 and §11):**

* **Automated tests** with `TF_FULL_SPRITES=1`, the Tibia client closed: **490 tests, 482 passed, 0 failed, 8 skipped** (the documented opt-in checks), 3 min 29 s (`test_results/`). 32 tests are new since 7.1.0: renames (4), consolidations (4), OBS (9), automatic hover (`AutomaticHoverTests`, 15, against a simulated client), and others extended.
* **Reverse checks**: **142 of 142 detected**: the 118 of 7.1.0, H54–H67 (renames, consolidations, OBS) and H68–H77 (automatic hover: a moved mouse, wrong icons, the retry, another window in front, the calibration's checks, hidden buttons, a click import in the pointer mover, the countdown, the control objects, a resized window). The restored build compiled (`mutation_report.txt`).
* **Published builds** (`dist/TibiaFoundry-7.2.0-*`; 257 files in the folder edition as in 7.1.0; the portable exe 99.5 MB; zips of 93.1 MB (portable) and 88.1 MB (folder), compressed more than 7.1.0's; the notices differ from 7.1.0's only in the version line):
  * `--self-test`: 12 of 12 PASS for both editions, the portable one from an empty folder; 0 errors and 0 warnings in the application self-check (`self_test/`).
  * `--ui-snapshot`: **44 of 44** window checks PASS, 0 binding errors, 0 logged warnings or errors, for the folder edition (1400 × 900), the portable edition (1400 × 900) and the portable edition in a small dark window (940 × 620) (`ui_snapshots/`).
* **Preserved files**: the original program 24 of 24, and every baseline from 2.0.0 to 7.1.0 matches its checksums (`preserved_files_check.txt`). `dist/SHA256SUMS.txt` was written through a temporary file (it has one link).
* **Names search** over all 244,416 files, including the 7.2.0 zips, with `13_baseline_7.1.0` added to the preserved folders: 0 unexplained (`obsolete_names_search.txt`).
* **Not run**: the end-to-end check with the simulated client (it moves the mouse on the desktop; the guided screen path it covers is unchanged except for a shared icon-preparation step, which the page tests run), the settings move between 7.1.0 and 7.2.0 (only tool settings with defaults were added; the settings format is unchanged), the TibiaWiki import check (live wiki; unchanged code), the start-up measurement, and every live test on the real client (A1–A5 of the plan; automatic hover has run only against the simulated client).

**Found and fixed during development:**

* Google Drive for desktop kept `obj\…\TibiaFoundry.g.resources` open while uploading it, so 23 of 47 reverse-check builds of phase 3 failed ("used by another process"). They passed when rerun with the file moved aside before each build; no planted defect failed to compile for a code reason (`05_verification/7.2_phases_0-3/mutation/mutation_phase3.txt`).
* `RunAsyncReportsSuccess` failed once in a full run under load (a progress report arriving after "All done."): a race of the tests only, which have no UI thread. It passed 25 of 25 times when repeated; the code involved is unchanged since 7.1.0. Not fixed in this release.
* After publishing, Item Discovery's first panel still said "guided"; it was relabeled and both editions were published and checked again.

## 7.1.0 (2026-10-15)

7.1.0 implements steps H.1–H.5 of the Item Discovery research (`05_verification/item_discovery_research_2026-10-15/`, §J): client screenshots as a capture source, black captures named, least process access, provenance of the capture source, CSV lists without a header. Each was built test first. Evidence: `05_verification/7.1.0/`; paths of the temporary working folder are shown as `<scratch>`.

**Release checks (`11_Maintenance_Guide.md` §8 and §11), on the published builds where they apply:**

* **Automated tests** with `TF_FULL_SPRITES=1` (all 223,694 sprites and the whole client conversion): **458 tests, 450 passed, 0 failed, 8 skipped** (the documented opt-in checks), 3 min 18 s (`test_results/`).
* **Reverse checks**: **118 of 118 detected**: the 107 of 7.0.1, and H43–H53 for 7.1 (the targeted checks below, now part of `mutation_checks.py`). The restored build compiled (`mutation_report.txt`).
* **Published builds** (`dist/TibiaFoundry-7.1.0-*`; the same 257 files as 7.0.1 in the folder edition, and in the portable edition one exe of 99 MB plus the README and notices; zips of 97.4 MB (portable) and 92.2 MB (folder); the notices differ from 7.0.1's only in the version line):
  * `--self-test`: 12 of 12 PASS for both editions, the portable one from an empty folder; 0 errors and 0 warnings in the application self-check (`self_test/`).
  * `--ui-snapshot`: **44 of 44** window checks PASS, 0 binding errors, 0 logged warnings or errors. That held for the folder edition (1400 × 900), the portable edition (1400 × 900) and the portable edition in a small dark window (940 × 620) (`ui_snapshots/`). The Item Discovery page shows the new *Capture source* choice in step 1.
  * **End-to-end check of Item Discovery** with the simulated client, on the published folder edition: **62 of 62 PASS**, no SKIP (`item_discovery/end_to_end/`). It identified 27 of 27 icons, read 5 of 5 control objects correctly and read 22 of 22 names exactly. Rest captures started 913–1,028 ms after the pointer stopped (rest delay 800 ms) and capture-key captures 464–482 ms after; processing took 32 ms per capture (median). It also checked review, the byte-for-byte restore and the report, found no error in the log, and left the installed client's options file unchanged. The simulated client does not hide its window, so this checks the screen source; the screenshot source is checked by the tests below.
  * **Settings between 7.0.1 and 7.1.0**, both portable editions taking turns on one data folder: format 5 throughout and values kept. 7.1.0 made no upgrade and wrote no backup. 7.0.1 keeps the new capture source and screenshots folder, and 7.1.0 still has them afterwards. There was no error in either log (`migration_check.txt`, `migration_files/`).
* **Preserved files**: the original program 24 of 24, and every baseline from 2.0.0 to 7.0.1 matches its checksums (`preserved_files_check.txt`). While the release was zipped, `dist/SHA256SUMS.txt` was first rewritten in place. That file shared its storage with `12_baseline_7.0.1/dist/SHA256SUMS.txt`, through a hard link made when the baseline was created, so the baseline copy changed too. It was restored to its recorded bytes (SHA-256 `dc1328dc…`). `dist` now has a file of its own, and the check above was run after the repair.
* **Names search** over all 242,932 files, including the 7.1.0 zips: 0 unexplained (`obsolete_names_search.txt`).
* **Not run**: the TibiaWiki import check (`tibiawiki_import_check`; it contacts the live wiki, and the wiki code is unchanged since 7.0.1), the start-up measurement, and the live proof of concept (steps 0–2, the owner's session).

**Development checks (before the release):**

* **Automated tests** (Debug, normal build folders): **458 tests, 448 passed, 0 failed, 10 skipped**, 2 min 40 s. The 10 skipped are the same opt-in tests as in 7.0.1's run of 2026-10-15 (445 / 435 / 0 / 10). 13 tests are new: `ItemDiscoveryCaptureTests` (9), `ClientScreenshotsGoThroughTheCollectorLikeScreenCaptures`, `ThePageCollectsFromTheClientsScreenshotsWhenChosen`, `AHeaderlessListOfNamesAndIdsIsReadWithTheLayoutYouChoose`, `ThePageAsksWhichValueIsTheIdInAListWithoutHeader`. No existing test was changed.
* **Build**: the whole solution rebuilt without incremental build: 0 warnings, 0 errors.
* **Targeted mutation checks** (each change must make its tests fail; the file is always restored): 11 of 11 caught. These covered the pointer-to-pixel mapping, the gap limit, the rest time, PNG completeness, automatic screenshots, the rest check, the black threshold, commas in names, the recorded method, the page's layout question, and the path asked through the process's modules. A first run caught 10 of 11: the PNG completeness check had no test of its own. It now has one (`ClientScreenshotWatcher.IsCompletePng`).
* **Real data, read only**: the market tracker's `items.csv` (a copy in the temporary folder) through the new importer. 7,607 lines gave 7,581 records, 54 names with commas and no names lost. There were 26 problems: 25 repeated IDs (the first is kept) and one ID above 65535. That is 7,582 distinct IDs, as counted in the research.
* **Names search** (`tools/verification/obsolete_names_search.py`, with `12_baseline_7.0.1` added to its preserved folders): 0 unexplained.

## 7.0.1

7.0.1 fixes the import of TibiaWiki's item IDs; its verification is §0, with the evidence in `05_verification/7.0.1/`. §1–§9 are the report of 7.0.0, whose evidence is in `05_verification/7.0.0/`; apart from the TibiaWiki download and the program's UI check, 7.0.1 is the 7.0.0 code. Paths of the temporary working folder in the evidence are shown as `<scratch>`.

## 0. Version 7.0.1 (2026-10-15)

**What failed.** In the published 7.0.0 build, *Item Catalog → TibiaWiki item IDs* failed after 59 s (the owner's log, 09:34): the "Item IDs" page answered "503 Backend fetch failed" three times, each after 16 s and 5 s apart, and the render address answered with Cloudflare's browser check. Asked again at 10:55, 11:12 and 11:47: the same 503 after 16 s. The page's wikitext shows why: it is a query (a DPL table) over 9,345 item articles that the wiki builds on request and no longer finishes within its 16 s limit. *Generate Lua data for all NPCs* uses the same download and failed the same way.

**The fix** (`CHANGELOG.md`, `11_Maintenance_Guide.md` §7.1): the program runs the page's query itself through the MediaWiki API: the category's size, then its articles with their wikitext, 50 per request, following the API's continuation; the same filters (also in *Objects*, not in *Deprecated* or *Lists*), the same order (so an ID claimed twice goes to the same article), the *Infobox Object*'s `itemid` read like the page's ID cells. The page and the render address remain as fallbacks; when the site does not answer at all they are not tried as well. A server's Retry-After no longer shortens the planned waits.

| Evidence | Files |
|---|---|
| Full automated run (final source) | `test_results/full-7.0.1.trx`, `test_results/console.txt`, `test_results/run_times.txt` |
| The table from the articles compared with the page; a real catalog of the client and TibiaWiki | `tibiawiki/articles_compared_with_page.txt`, `tibiawiki/coverage_client_and_tibiawiki.txt` |
| The failed import repeated on the **published** builds | `tibiawiki/import_check_folder_edition.txt`, `tibiawiki/import_check_portable_edition.txt` (tool: `tools/verification/tibiawiki_import_check`) |
| Reverse checks | `mutation_report.txt` |
| Self-tests and UI runs of the **published** builds | `self_test/`, `ui_snapshots/*/report.txt` and screenshots |
| Settings migration 6.0 ↔ 7.0.1 | `migration_check.txt`, `migration_files/` |
| The first 7.0.1 publish (its UI check found a fault, see below) | `first_publish/` |
| Search for the removed names; preserved files | `obsolete_names_search.txt`, `preserved_files_check.txt` |

**Results**:

* **Automated tests** (final source, 2026-10-15 12:02:54 → 12:05:48, Debug, `TF_FULL_SPRITES=1`): **445 tests (367 methods): 437 passed, 0 failed, 8 skipped**. The skips are the opt-in checks of 7.0.0 and the new live comparison. New: six `NetworkTests` (the articles read batch by batch along the continuation, with progress, and cached; the page's filters, order, comments, nested templates and irregular entries; an article sent in parts and a repeated batch; the page as fallback; every failed address named; Retry-After as the shortest wait); changed: the offline tests now also check that no other address is tried.
* **Live TibiaWiki** (opt-in, `TF_LIVE_WIKI=1`, all three checks passed): the articles in 188 requests and 44–58 s; **13,875 IDs, identical to the page as downloaded on 2026-10-14**: the same names, the same 193 IDs claimed twice, the same range and the same irregular entry; the live page failed again (503 after 16.2 s). A real catalog of the client and TibiaWiki gives exactly the 7.0.0 coverage figures (13,875 records; 7,306 names the same as the client's, 1,049 different, 5,504 for objects the client does not name).
* **Reverse checks: 107/107 caught**, six new (H37–H42): H37 puts the 7.0.0 failure back (only the page asked), H38–H40 the page's order, the Deprecated filter and the continuation, H41 the waits for other addresses when the site does not answer, H42 a Retry-After that shortens the backoff. All 107 ran in one run (11:20–11:42, 22 minutes); H32 was skipped there because its test does not run while a Tibia client is running (the owner's client was open) and was caught when run again at 12:06 with the client closed.
* **Release build** (`--no-incremental`): 0 warnings, 0 errors.
* **Published builds** (final, run from copies; the portable import check from the zip's contents):

| Check | Portable edition | Folder edition |
|---|---|---|
| **The failed import repeated** (Item Catalog → *TibiaWiki item IDs* with no catalog, suggested name, live download, *Import*) | **9/9 PASS**: progress in the status bar throughout (66 states of "Reading TibiaWiki's item articles: … of 9,345"), the preview after 39 s, **13,875 records** in the new catalog, each naming an identifier, cached as read from the articles, "Imported 1 source(s)", no error in the log | **9/9 PASS**, preview after 41 s, 13,875 records |
| `--self-test` | **12/12 PASS**, 0 warnings | **12/12 PASS** |
| `--ui-snapshot` 1400×900, both themes | **44/44 PASS**, 0 binding errors, 0 logged warnings | **44/44 PASS** |
| `--ui-snapshot` 940×620, dark, compact navigation | **44/44 PASS** | – |
| Migration 6.0 → 7.0.1 → 6.0 → 7.0.1 | every value kept, format 5, `settings.json.v5.bak` identical (`migration_check.txt`) | – |
| Zip contents | 3 files, each identical to the folder | 257 files, each identical to the folder |
| `THIRD-PARTY-NOTICES.txt` | regenerated; identical to 7.0.0's but for the version line | same |

* **Search for the removed names and preserved files** (after all runs): a search of all 241,744 files, including binaries and the entries of the 7.0.1 and 7.0.0 release zips, explains every match (`obsolete_names_search.txt`); the 24 files of the original program and every baseline from 2.0.0 to 6.0.0 match their checksums (`preserved_files_check.txt`); the 7.0.0 zips and executables in `dist/` are unchanged (their checksums are kept in `dist/SHA256SUMS.txt`).

**Failed or unexpected during the 7.0.1 work (not hidden)**:

| Where | What was seen | Cause | Resolution |
|---|---|---|---|
| UI check of the first 7.0.1 publish | 43/44 in all three runs: "Item Discovery chooses a batch" with 0 objects (`first_publish/`) | The owner's own catalog (created at 09:34 for the failed import) was in `Documents\Tibia_Foundry\Item_Catalogs`; the check waited for *a* catalog on the Item Discovery page, found the owner's already open and chose the batch before its own had loaded. In 7.0.0 there was no catalog there. | The check waits for its own catalog (`UiSnapshot.cs`); the builds were published again and every check of the published builds repeated: 44/44. The owner's catalog was only read (its `catalog.json` is unchanged since 09:34). |
| Reverse check H32 | Skipped, not caught, in the full run | Its test does not run while a Tibia client runs (writing the options file would be refused); the owner's client was open | Run again with the client closed: caught |
| End-to-end check of Item Discovery (`item_discovery_e2e`) | Its step "the log has no errors" looked for `ERROR`, while the log writes `[Error   ]`: the step could not fail | A defect of the check | It now looks for `[Error`, `[Critical` and `Unhandled`. The log of the 7.0.0 run it reported on (`05_verification/7.0.0/item_discovery/end_to_end/published_folder_edition_app.log`) was read: 35 lines, all `[Info    ]`. |
| Portable import check, first run on the final build | Passed, but the copy it ran from contained a `TibiaFoundry-Data` folder: the owner had started the new build from `dist/` at 12:08 | – | Run again from the zip's contents: 9/9 (this is the reported result) |

**Not repeated for 7.0.1** (the code they check is unchanged): the Item Discovery measurements and its end-to-end check on the screen (it moves the mouse for two minutes), start-up timing, the real File Explorer check, the third-party item data figures, the installed options file's round trip. Their 7.0.0 results are in §5–§6.


**7.0.0 evidence** (`05_verification/7.0.0/`):

| Evidence | Files |
|---|---|
| Final automated run (final source) | `test_results/full-7.0.0.trx`, `test_results/console.txt`, `test_results/run_times.txt` |
| Baseline before the 7.0 work (6.0.0 source) | `baseline_6.0.0_tests/` |
| The installed client 15.33.9d148f validated | `client_15.33.9d148f/validation.txt` and its test run |
| Item Catalog and third-party item data | `item_catalog/` (coverage of the client and TibiaWiki, the third-party data figures, the Excel check) |
| Item Discovery measurements | `item_discovery/` (text recognition at 10–12 px and at larger sizes, the client's icons on painted action bars, icon-matching time, the installed options file's round trip) |
| Item Discovery end-to-end check | `item_discovery/end_to_end/` (the final published build; the two earlier runs that found faults) |
| Reverse checks (re-introduced defects, removed safeguards) | `mutation_report.txt`, script `tools/verification/mutation_checks.py` |
| Real File Explorer check (opt-in) | `explorer_check_console.txt` |
| Self-tests of the **published** builds | `self_test/portable/`, `self_test/folder_edition/` |
| UI runs of the **published** builds | `ui_snapshots/*/report.txt` and screenshots (18 pages in both themes, the new Item Catalog, comparison, Item Discovery batch and client-locations views) |
| Start-up and memory | `startup_performance.csv` (portable editions of 6.0.0 and 7.0.0, alternating), `startup_folder_edition.txt` |
| Settings migration 6.0 ↔ 7.0 | `migration_check.txt`, `migration_files/` |
| Search for the removed names | `obsolete_names_search.txt`, script `tools/verification/obsolete_names_search.py` |
| Preserved files | `preserved_files_check.txt` |

The reports of earlier versions are in `11_baseline_6.0.0/docs-6.0.0.zip`, the earlier baselines and `05_verification/`.

## 1. Environment

| Item | Value |
|---|---|
| OS | Windows 11 Pro 10.0.26200, x64, display scale 100 %, work area 2560 px wide, Windows app mode **dark**. During part of the checks (2026-10-15, at night) the desktop was locked; the screen steps were run when it was unlocked again (§7) |
| Documents folder | Redirected to another drive: Windows reports `F:\Users\Rocky\Documents` |
| .NET | SDK 10.0.402, runtime 10.0.13 (the published builds carry 10.0.13; 6.0.0 carried 10.0.12) |
| Windows text recognition | English (United States) |
| Test client data | **Pinned copy** of client 15.33.b21348 in `04_reference/client_15.33.b21348` (5,084 sprite sheets, 223,694 sprites) |
| Installed client | Live `Tibia` **15.33.9d148f**, only read (its data files are byte-identical to 15.33.b21348; `client_15.33.9d148f/validation.txt`). Its options file (`conf\clientoptions.json`, 2,270,886 bytes) was only read; every edit went to copies. |
| Reference outputs | Produced by the **original** program (1.8.0.26867) through `tools/ParityHarness` (`04_reference/`) |
| Builds under test | Debug and Release (`--no-incremental`): **0 warnings, 0 errors**. Self-tests, UI runs, start-up, migration and the end-to-end check used the published builds in `dist/`, run from copies. |

## 2. Automated tests (xUnit v3)

**Before the 7.0 work** (Phase 0): the 6.0.0 source passed 376 tests, 373 passed, 3 skipped (`baseline_6.0.0_tests/`).

**Final run**: 2026-10-15, 07:07:15 → 07:10:23, on the final source, Debug build, with `TF_FULL_SPRITES=1`.

**439 tests (361 methods): 432 passed, 0 failed, 7 skipped.** The skipped tests are opt-in:

* the two live TibiaWiki checks (`TF_LIVE_WIKI=1`; the catalog coverage check ran during the 7.0 work, `item_catalog/coverage_client_and_tibiawiki.txt`);
* the comparison with a reference conversion (`TF_REFERENCE_LEGACY`; conversion code unchanged since 5.0);
* the real File Explorer check (`TF_SHELL_CHECK=1`): ran separately on the final source and **passed**: Explorer showed the folder `…\Saved GIFs ü 日本` with `Gold Coin (3031).gif` selected (`explorer_check_console.txt`);
* the third-party item data research figures (`TF_OTB_ITEMS_DIR`/`TF_CLIENTID_ITEMS_DIR`; ran during the 7.0 work with research copies of community item files, results in `item_catalog/`);
* the installed client's options file (`TF_REAL_CLIENTOPTIONS=1`): ran separately on the final source and **passed**: the 2,270,886-byte file written back byte for byte; a 450-object batch in a copy changed only the new set, the set in use and the automatic switching; restored byte for byte (`item_discovery/installed_options_file_round_trip.txt`);
* the text-recognition measurement (`TF_OCR_MEASURE=1`): ran separately (`item_discovery/text_recognition_*.txt`).

| File | Methods (rows) | What it covers | New in 7.0 |
|---|---:|---|---|
| `LocationTests` | 16 | Every client location: automatic, chosen, inside a chosen folder, unavailable choices kept and reported, quick and deep checks, a partial compatibility check after a change, *Locate…* on the Compatibility page, settings format 5 and its upgrade | new |
| `ItemCatalogTests` | 12 (run on their own) | Catalog model and evidence rules (identifier types never mixed, conflicts never merged), storage and its append-only log (interrupted lines, records after them), imports with previews and backups, exports and re-imports (CSV, JSON, XLSX), TibiaWiki's CC BY-SA statement, large catalogs quickly | new |
| `ThirdPartyItemDataAndComparisonTests` | 9 (1 opt-in) | `items.otb` and `items.xml` (both ID conventions; DOCTYPE and internal entities refused), mappings, comparisons of sources and catalogs, decisions, the Excel writer | new |
| `ItemDiscoveryTests` | 25 (2 opt-in; run on their own) | The options file's exact round trip and every refusal of a write and a restore; interrupted writes; batches; icon recognition (also the client's own ground tiles at every pixel offset, and tall sprites kept in proportion or stretched); tooltip text; whole captures with Windows text recognition; the client's own icons on painted action bars read through the collector; review, resume; UI Automation trust; the page writing and restoring a batch; the pointer's rest anywhere on the screen | new |
| `BrandingTests`, `ToolArchitectureTests`, `DiagnosticsTests`, `UiFlowTests`, `ViewModelTests`, `NetworkTests` and others | as in 6.0 | Version 7.0.0, 30 registered tools, the new output kind, User-Agent `TibiaFoundry/7.0.0` | updated |
| `SpriteDecodingTests` and the parity tests | 7, 3, 3, 1, 10, 4, 2 | Output parity with the original program, including **all 223,694 sprites** against the original's extraction | – |

**Parity with the original program is unchanged**: all 223,694 sprites, 51/51 outfit frames, 15/15 GIFs, and the JSON, wiki and Lua exports. The full conversion of the client to `Tibia.dat`/`Tibia.spr` with complete read-back ran in this run as well.

## 3. Verification of the 7.0 requirements

| Requirement | Automated | Measured, published builds and manual inspection |
|---|---|---|
| Client and data locations (Phase 1) | `LocationTests` (16); reverse checks H01–H04 | UI check "Settings list every client location (13)"; migration steps 2–4 (format 5, the 6.0 assets folder kept for the live client and given to the test server, 6.0 keeps the 7.0 values); the installed client 15.33.9d148f validated (`client_15.33.9d148f/`) |
| Item Catalog (Phase 2) | `ItemCatalogTests` (12); reverse checks H05–H10, H31 | UI checks "Item Catalog lists the client's objects (43,516) and explains a record" and "compares a source with the settled names"; coverage of the client and TibiaWiki (`item_catalog/coverage_client_and_tibiawiki.txt`; `17_Item_Catalog.md`); XLSX opened in Excel and read with openpyxl (`item_catalog/xlsx_check.txt`) |
| Third-party item data and comparison (Phase 3) | `ThirdPartyItemDataAndComparisonTests` (9); reverse checks H11, H12 | The research figures reproduced from research copies of community item files (`item_catalog/`) |
| Item Discovery, guided proof of concept (Phase 4) | `ItemDiscoveryTests` (25); reverse checks H13–H30, H32–H36 | The installed options file's round trip (passed, §2); text recognition on the client's names (§6); the client's own icons on painted action bars (§6); the end-to-end check of the published build on the real screen (§5); UI check "Item Discovery chooses a batch: 50 objects, 10 controls, every icon shown". **The live session in the game was not run** (it needs the owner's account; §8). |
| Security limits of the specification | Reverse checks H13–H21, H29 (no write while the client runs, exact backups and restores) | No input is sent to the client (`ScreenReader` only reads; the end-to-end check's pointer moves come from the check itself); no credentials or tokens are stored; third-party data stays labeled and separate from official data; no automatic mode is built |
| Documentation and release (Phase 6) | This report | `17_Item_Catalog.md`, `18_Client_Locations.md`, `19_Item_Discovery.md`; `CHANGELOG.md`; notices regenerated with the Windows SDK projection |

## 4. Reverse checks

Each of **101** defects or removed safeguards was put into the source, one at a time, and the named tests had to **fail**:

* the 17 audited defects of 2.1 (A..), the 10 attribution and identity safeguards of 3.0 and 4.0 (T01–T10), the 16 file-safety and tool safeguards of 5.0 (F01–F16) and the 22 safeguards of 6.0 (G01–G22);
* **36 new safeguards of 7.0 (H01–H36)**:
  * client locations: a chosen location ignored; the version taken from the installed client although another assets folder is chosen; an unavailable choice forgotten silently; *Locate* saving a location that failed its check;
  * Item Catalog: identifier types mixed; conflicting names merged; a record after an unfinished line lost; replaced records not backed up; a source registered by another page dropped; a reviewed name not replacing the reading; TibiaWiki exports without their license;
  * third-party item data: DOCTYPE declarations processed in `items.xml`; items.otb IDs compared as object IDs without a mapping;
  * Item Discovery, the options file: a batch written while the client runs; a client started while the batch is prepared not noticed; a file that cannot be written back exactly, or of an unknown version, written; a second batch before the first is restored; a damaged backup restored; a restore while the client runs; a restored session restored again; the client's own saved options lost on restore; keys not in the client's order; a file that changed meanwhile overwritten; a refused batch blocking the next one;
  * Item Discovery, collecting: look-alike icons not reported as ambiguous; two look-alike objects in one batch; UI Automation trusted before control objects prove it, or kept after a wrong one; names not recorded in the catalog at once; an object read twice counted twice; a chosen batch lost when the page is shown again; the pointer's rest overflowing at the screen's far edges; icons on odd pixels halved from the wrong pixel; sprites that fade when shrunk put in batches; sprites that are not square found only with their proportions kept.

**Result: 101/101 caught** (`mutation_report.txt`): H01–H35 and the checks of earlier versions in one run (2026-10-15, 20 minutes); after the last change to the icon matcher, the five icon checks (H23, H24, H34, H35 and the new H36) again. The build after restoring the files succeeded each time. Three of the new checks (H11, H32, H34) were not caught at first; each showed a gap in the tests, which were extended (§7).

## 5. Published builds and practical checks

All on the **final published builds** in `dist/`, each run from a copy (the portable exe from a new, empty folder).

| Check | Portable edition | Folder edition |
|---|---|---|
| Executable metadata | Description and product "Tibia Foundry", company RockyLaRo, version 7.0.0.0, copyright "Tibia Foundry © 2026 RockyLaRo (TibiaWiki Admin). Tibia game graphics © CipSoft GmbH." | same |
| `--self-test` | **12/12 PASS**, 0 warnings, exit 0 (the application self-check: 28 checks, 30 tools registered; the installed client 15.33.9d148f rated *Supported*) | **12/12 PASS**, 0 warnings, exit 0 |
| `--ui-snapshot` 1400×900, both themes | 18 pages × 2 themes, 0 binding errors, 0 logged warnings, **44/44 window checks PASS** (new: client locations, Item Catalog list and comparison, Item Discovery batch) | **44/44 PASS** |
| `--ui-snapshot` 940×620, dark, compact navigation | 0 binding errors, **44/44 PASS** | – |
| Start-up (normal start, then close; medians of 4 runs each, alternating with 6.0.0 in the same session; measured on the first 7.0.0 publish, whose start-up code is the same: the last change concerns only the icon matcher, which is not used at start) | Window at 1.58 s, appearances loaded at 1.60 s, compatibility result at 5.38 s, working set 405 MB, 8.8 s CPU. **6.0.0: 1.40 s, 1.50 s, 4.97 s, 355 MB, 8.4 s.** No failed start. | Two starts each: 7.0.0 window at 1.31 and 1.40 s, working set 283 and 293 MB; **6.0.0: 1.42 and 1.28 s, 281 and 284 MB** |
| Migration 6.0 → 7.0 | A settings file written by the 6.0.0 build, with a creator name, the dark theme, a GIF delay, a volume, an assets folder chosen for both clients and a custom theme, was upgraded by 7.0.0 to format 5 with every value kept; the live client keeps the assets folder where 6.0 reads it and the test server gets the same folder as its own choice; the 6.0 sections (themes, diagnostics, output folders, safety, performance, scripts) unchanged. | – |
| 7.0 → 6.0 → 7.0 | With a resource-file location and the Item Catalog's last catalog added, the 6.0.0 build opened the 7.0 file, made `settings.json.v5.bak` (identical to the file) and kept the 7.0 values unchanged when it saved. 7.0.0 then still had them, and the custom theme was still active. | – |
| Item Discovery end-to-end check (`tools/verification/item_discovery_e2e`) | – | On a portable copy of the folder edition, against the simulated client and a copy of the installed options file, on the real screen: **62/62 checks passed**: batch chosen; writing refused while the simulated client ran; the batch written and the edited copy read independently (exactly the 27 buttons, the new set in use, bars and tooltips on); collecting started with the pointer in the screen's corner; the capture key ignored away from the client; every button hovered (rest captures 0.83–1.04 s after the pointer stopped, capture-key captures 0.46–0.48 s); 27 of 27 objects identified, 5 of 5 control objects correct, 22 of 22 names exact; a name confirmed, corrected and rejected in the page; restoring refused while the client ran; restored byte for byte; report saved; no error in the log; the installed options file unchanged. |
| Zip contents | 3 files, each identical to the folder | 257 files, each identical to the folder |
| `THIRD-PARTY-NOTICES.txt` | Regenerated: new section for the Windows SDK projection (`Microsoft.Windows.SDK.NET` 10.0.19041.57, Windows SDK license terms; `WinRT.Runtime`, MIT); runtime 10.0.13 | same |
| Isolation | Snapshot runs, self-tests, migration and start-up runs used temporary folders; `%APPDATA%\TibiaFoundry` received no new file. The folder edition's `--self-test` wrote its log into `Documents\Tibia_Foundry\Logs` (by design: it checks the real installation). | same |

The portable edition starts about 0.2 s later and uses about 50 MB more memory than 6.0.0, while the folder edition starts as 6.0.0 did: the single file now carries the Windows SDK projection (55 MB), and the portable edition unpacks its compressed contents into memory when it starts. Only `WinRT.Runtime.dll` (1.4 MB) is loaded at start-up; the projection is used when Item Discovery reads text.

**Preserved files** (checked after all runs, `preserved_files_check.txt`): the 24 files of the original program match `02_analysis/original_file_hashes.csv`. The baselines match their checksum files: 2.0.0 6/6, and 2.1.0, 3.0.0, 4.0.0, 5.0.0 and 6.0.0 5/5 each. The 6.0.0 build was only run from copies in temporary folders. The installed client and its options file were only read (the options file's SHA-256 is unchanged).

**Reviewed by eye** (screenshots of the published builds): the Item Discovery page with a chosen batch (icons, control objects with their client names, the summary); the Item Catalog with the client's objects and a record explained; the comparison view; Settings → Tibia client with every location.

## 6. Item Discovery measured without the game

Details in `19_Item_Discovery.md` §6. On images Tibia Foundry painted itself, not the client's rendering:

* **Text recognition** of 300 of the client's names drawn as tooltips: 95.7 % exact at 10 px, 99.7 % at 11 px, 100 % at 12 px (2026-10-14); 98.0 % at 14 px, 95.3 % at 16 px, 98.7 % at 17 px, where apostrophes are the usual error (all six sizes measured again on the final source, 2026-10-15, with the same figures).
* **The client's own icons on painted action bars** (Release build, 90 objects, buttons an odd number of pixels apart, 2026-10-15): at 32 px, 90 of 90 identified, none taken for another object, 10 of 10 control objects correct, 79 of 80 names exact, 47 ms per capture; at 48 px, 88 of 90 identified, none taken for another object, 10 of 10 control objects correct, 72 of the 78 identified exact, 67 ms per capture.
* **Icon matching time**, 900 images, Release build: images for the seven sizes prepared in 1.2 s, the first capture (every size tried) 1.3 s, then 128 ms per capture (limits 10 s and 1 s).

## 7. Failed or unexpected results (not hidden)

| When | Result | Cause | Action |
|---|---|---|---|
| Reverse checks during the 7.0 work | H11 (DOCTYPE in `items.xml`) not caught | The test refused an external entity only; an internal entity was not tested | Test extended; caught |
| Reverse checks | H32 (a chosen batch lost when the page is shown again) not caught | The page test had no earlier session, so reloading picked nothing and the batch stayed | The test first writes and restores an earlier batch; caught |
| Reverse checks | H34 (icons halved from one pixel only) not caught at first | Once one icon matched, the pixel start was learned and every later icon of the test's bars used it | A test with one of the client's ground tiles on an odd pixel as a session's first capture; caught |
| Development | A batch refused because the options file changed meanwhile left a session that blocked the next batch | The session was marked "prepared" before the check | Marked "cancelled" when nothing reached the file; reverse check H30 |
| Full test runs during development | Timing tests failed when the discovery tests ran in parallel with them | Icon matching and text recognition kept every core busy | The discovery tests run in a collection of their own |
| Text recognition, first design | 87.0 % exact at 10 px | Enlarging with nearest-neighbor sampling | Bicubic and Lanczos enlargements, three readings and a vote (95.7 %) |
| End-to-end check, first runs | Collecting failed on every tick when it started with the pointer on the screen's left or top edge (`OverflowException` in the rest timer; `item_discovery/end_to_end/earlier_run_found_edge_failure_*`) | A "no position yet" marker of `int.MinValue` overflowed when subtracted from 0 | `PointerRest` without a marker and with 64-bit differences; a screen that cannot be read now gives a message instead of an error; test `ThePointersRestIsMeasuredAnywhereOnTheScreen`, reverse check H33 |
| End-to-end check | No capture on a button; the window under the pointer was Windows' lock screen | The desktop was locked during the night's checks | The check now detects this and reports `SKIP` for the hovering steps; they were run when the desktop was unlocked |
| End-to-end check on the real screen, first run | 25 of 27 objects identified; 3 checks failed (`item_discovery/end_to_end/earlier_run_found_tall_sprites_report.txt`) | The two tall sprites (32 × 64) were stretched to the square button by the stand-in client, and only their proportions-kept form was tried | Both forms are tried for sprites that are not square (`ATallSpriteIsFoundWithItsProportionsKeptOrStretched`, reverse check H36); the builds were published again and every check of the published builds repeated: 62/62 |
| Full test run after that change | 1 failed: `LargeCatalogsLoadAndFilterQuickly` (5,427 ms, limit 5,000 ms; under 1 s on its own) | It ran in parallel with the full sprite comparison and conversion | The item catalog tests run on their own, like the discovery tests; the next full run passed |
| End-to-end check | Its own faults: navigation not found (a grouped, virtualized list), the catalog read from a label, rows counted while virtualized, a dialog's text not read, a button looked up while a dialog closed | The harness | Fixed in the harness |
| The client's own icons on painted bars, first measurement | 15 of 18, then 81 of 90 objects identified at 32 px (none taken for another object) | Icons on odd pixels halved from the wrong pixel (ground tiles crowded out); sparse icons not refined; thin-line sprites unrecognizable once shrunk | Halving from all four pixel starts, a share of the batch refined, thin-line sprites left out of batches: 90 of 90 |
| Icon matching, after that fix | 1,086 ms per capture with 900 images (limit 1,000 ms; it was 180 ms) | Four halvings searched for every capture | The last icon's pixel start first, the other three only without a clear match; the SIMD lanes added once per position: 138 ms |
| Icon matching, a speed-up tried | 21 of 90 identified at 48 px | Stopping at the first size with a clear match: a smooth icon also fits clearly at a neighboring size, and that wrong size was kept | Not used: every size is tried |
| Icon matching, an accuracy change tried | No gain for thin sprites; the second-best fit of some icons rose | Comparing only fully opaque pixels | Not used |
| Self-test script | Counted the report's last line "Failures: 0" as a failure | Case-insensitive match in the script | Script fixed; the reports show 12/12 PASS |
| Start-up script | Measured nothing at first | Its function name was taken by a PowerShell alias | Script fixed |
| Start-up | Portable edition 0.18 s slower, 50 MB more memory than 6.0.0 | The Windows SDK projection inside the single file (§5) | Documented (`06_Build_and_Setup.md` §1) |
| Line endings | 50 changed files had LF line endings | Written by scripts | Normalized to CRLF before the final builds (the client-options fixture keeps LF on purpose) |
| The 6.0 baseline folder | `11_baseline_6.0.0/dist/TibiaFoundry-6.0.0-portable-win-x64/` holds a `TibiaFoundry-Data` folder | The owner's own sessions with the 6.0 portable edition (its settings and a custom theme), moved with `dist/` when the baseline was made, and one later start from there | Left unchanged; the checksummed baseline files are not affected (§5) |

## 8. Not executed (stated so it is not assumed)

* **The Item Discovery live session in the game** (`19_Item_Discovery.md` §3): it needs the owner's account logged in. No figure from the real client exists; whether the client shows the tooltips, how well its rendering is read and whether UI Automation sees them is open.
* How the client itself draws icons that are not square or are scaled, and its tooltips: the end-to-end check's stand-in client shows one possibility of each.
* Display scales other than 100 % for the new pages; screen readers; Windows 10 and other machines; the XLSX files in spreadsheet programs other than Excel; a disconnected network or removable drive while running (simulated with a missing folder); the start-up reminder after a real crash (simulated in tests).
* Loading converted or edited client files in a real game client, and the reference conversion (both unchanged from 5.0).

## 9. Conclusion

* All 432 executed tests pass on the final source, including full sprite parity with the original and a full validated conversion of the client.
* All 101 re-introduced defects and removed safeguards are caught, 36 of them new in 7.0.
* Both published editions pass their 12 self-test checks without warnings and all 44 UI window checks, including the compact window.
* Settings move between 6.0 and 7.0 in both directions without loss.
* Item Discovery works end to end in the published build on the real screen against a simulated client (62/62 checks: 27 of 27 icons, 22 of 22 names, review, byte-for-byte restore); on painted action bars the client's own icons are identified (90 of 90 at 32 px, none ever taken for another object) and names read (79 of 80 exact). The live session in the game remains to be run.
* The folder edition starts as 6.0.0 did; the portable edition 0.2 s later with 50 MB more memory.
* The removed names remain only where listed in `13_Branding_and_Attribution.md` §10: a search of all 240,715 files, including binaries and the entries of the release zips, explains every match (`obsolete_names_search.txt`; the 6.0 baseline was added to the preserved folders of the search).
* The remaining gaps are in §8 and `08_Known_Limitations_and_Uncertainties.md`.
