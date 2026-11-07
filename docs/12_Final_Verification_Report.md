# Final Verification Report – version 2.1.0

> **Note (Tibia Foundry 7.0):** this is a historical record of the final verification of version 2.1.0. The program was later renamed Tibia Forge (version 3.0) and then **Tibia Foundry** (version 4.0). Paths and figures below are those of the earlier version; since 6.0 the earlier product name is written as the placeholder `<v2>` (see `13_Branding_and_Attribution.md` §10). See `07_Testing_Report.md` for current results.

Date: 2026-10-12. This report states what the final 2.1.0 build was verified with, and where the evidence is. Everything marked ✔ was **executed and its result observed**. Items that were not executed are listed in §7.

## 1. Final build

| Item | Value |
|---|---|
| Version | 2.1.0 (file version 2.1.0.0), "2.1.0 (reconstruction of 1.8.0.26867)" |
| Source | `src/` (solution `<v2>.slnx`: Core, App, Tests). CRLF line endings, `.editorconfig`. |
| Clean build, Release and Debug | ✔ **0 warnings, 0 errors** (`--no-incremental`, `latest-recommended` analyzers) |
| Portable edition | `dist/<v2>-2.1.0-portable-win-x64/<v2>.exe`: 82,073,389 bytes, single self-contained file, plus `README-Portable.txt` |
| Folder edition | `dist/<v2>-2.1.0-win-x64/`: 254 files, self-contained, ReadyToRun |
| Checksums | `dist/SHA256SUMS.txt`:<br>• portable exe `c1bb97c3d7918f4a3802d47790caf45cc05032d5e45a843f8455585edb9a0d82`;<br>• portable zip `220bf8fd…`;<br>• folder zip `928f39a7…` |
| Zip integrity | ✔ The exe inside the portable zip is byte-identical to the exe that passed every check below |
| Previous version | 2.0.0 preserved, unchanged, in `06_baseline_2.0.0/` (source, docs and release zips with their original checksums) |

## 2. Automated tests

| Run | Result | Evidence |
|---|---|---|
| Full suite, final source, `TF_FULL_SPRITES=1` (09:39–09:40) | ✔ **159 tests: 158 passed, 0 failed, 1 skipped** (the opt-in live check). Includes all 223,694 sprites against the original program's output. | `05_verification/2.1.0/test_results/full-2.1.0.trx`, `console.txt` |
| Full suite against another client version (15.25.0e418e via `TF_CLIENT_DIR`) | ✔ 139 passed, 20 skipped with reasons (19 parity, 1 live), **0 failed** | `test_results/other_client_15.25_console.txt` |
| Reverse checks: 17 audited defects re-introduced one at a time | ✔ **17/17 caught** by their tests; all source files restored byte-for-byte | `mutation_report.txt`, `scripts/mutation_checks.py` |
| Live TibiaWiki API (opt-in, by hand) | ✔ Passed twice (13,875 IDs). One run failed on a wiki-side 503; the program's error and fallback behaved as designed. | `test_results/live_wiki_check_*.txt` |

**Test categories present**:

* unit;
* integration on real client data;
* regression and parity against the original;
* compatibility fixtures: synthetic changes only, no invented future formats;
* mocked network;
* fuzzing: 150 sheets, 300 catalogs and protobuf data files, with fixed seeds;
* view-model and UI-flow tests;
* scripted UI window checks.

## 3. Published builds

| Check | Portable (fresh empty folder) | Folder edition |
|---|---|---|
| `--self-test` | ✔ 8/8 PASS, exit 0 (compatibility: *Supported*, 4.0 s) | ✔ 8/8 PASS, exit 0 |
| `--ui-snapshot` 1400×900, light + dark | ✔ 22 pages, **0 binding errors**, 0 logged warnings, **18/18 window checks** | ✔ same, 18/18 |
| `--ui-snapshot` 940×620 dark (compact navigation) | ✔ 11 pages, 0 binding errors, 18/18 | – |
| Normal start and close | ✔ Window 1.5 s; check finished 5.1 s; clean exit; settings written next to the exe | – |

Evidence: `05_verification/2.1.0/self_test/`, `ui_snapshots/`, `startup_performance.csv`.

## 4. Compatibility of the installed clients

| Client | Result |
|---|---|
| Live `Tibia` 15.33.b21348 | ✔ **Supported**. 7/7 components; all data files match the SHA-256 in their names; no unknown schema fields except the 2 known `staticmapdata` fields. |
| Test server `TibiaExternal` 15.25.0e418e | ✔ **Supported**. 7/7 components. |

Evidence: `05_verification/2.1.0/compatibility_Tibia.txt`, `compatibility_TibiaExternal.txt`.

## 5. Requirement coverage

| Requirement (audit specification) | Status | Where |
|---|---|---|
| Independent audit; do not assume the reconstruction is correct | ✔ 38 findings: 26 before changes, 12 during the work | `09_Stability_Audit_Report.md` §2, §4 |
| Fix the bugs (implemented, not just recommended) | ✔ All findings fixed, except A33, which was measured and accepted with reasons | `09` §3–§4 |
| Version detection and compatibility layer | ✔ `package.json` version; validated versions; per-file statuses Supported / Unfamiliar / Partial / Missing / Not supported | `10` §2 |
| Format change detection | ✔ Integrity checksums; protobuf schema drift (unknown or retyped fields); catalog problems; sample decode per sprite type; record validation | `10` §2; `CompatibilityTests` |
| Graceful failure, isolation of affected features | ✔ `DataFileException` with plain advice; per-page banners; isolation tested on a damaged client copy | `10` §6; `DamagedAndChangedFilesAreIsolatedToTheirFeatures` |
| Client updates while running | ✔ Detected on page switch and window activation; stale data released | A09; `ClientUpdateWhileRunningDiscardsStaleData` |
| External website/API resilience | ✔ Timeouts, bounded retries with backoff, `Retry-After`, size caps, response validation, cache with age, offline fallback with notice, browser-challenge detection | `NetworkTests`; A15, A16, A34, A35 |
| Compatibility status / diagnostic UI | ✔ Compatibility page, *Copy report*, banners, self-test report | screenshots `09_Compatibility_*.png`, `compatibility_banner.png` |
| Safe recovery, backward compatibility, migrations | ✔ Newer settings preserved and backed up; corrupt settings backed up; atomic writes everywhere; 2.0.0 kept for rollback | A14, A22, A28, A37; `11` §10 |
| Performance and resources | ✔ Measured. Start-up unchanged; check runs in the background; checksum costs 9 % of sheet decode; caches bounded. | `09` §5 |
| Security and data integrity | ✔ Path traversal blocked; device names; atomic writes; downloads verified against content hashes; GET-only network; no executables downloaded or run; no secrets stored | A07, A14, A15, A21 |
| Usability of failures | ✔ Plain-language messages naming the file and the cause; amber/red status for partial or failed batches; banners | A13, A17, A23 |
| Tests: unit, integration, regression, compatibility fixtures, mocked network, fuzz | ✔ §2 | `07_Testing_Report.md` |
| Routine tests independent of live websites | ✔ The live check is opt-in only | `LiveServiceChecks` |
| Deliverables: build, source, audit report, compatibility report, tests and fixtures, build instructions, maintenance guide, limitations, this report | ✔ | §6 |
| Original program and supplied files unchanged | ✔ 24/24 original files and 24/24 backup copies match `02_analysis/original_file_hashes.csv` (checked 09:47) | – |
| Tibia client folder and user output folders never modified | ✔ All client access is read-only. The pinned test copy was made by reading. The program writes only to chosen output folders, its data folder and `%TEMP%` caches. | `05_Architecture.md` §7 |

## 6. Deliverables

| Deliverable | Location |
|---|---|
| Improved build | `dist/` (both editions, zips, `SHA256SUMS.txt`) |
| Complete source | `src/` |
| Stability audit report | `docs/09_Stability_Audit_Report.md` |
| Tibia compatibility report and dependency map | `docs/10_Tibia_Compatibility_Report.md` |
| Regression suite and compatibility fixtures | `src/<v2>.Tests/` (`Fixtures/Wiki/item_ids_excerpt.html`; synthetic fixtures are built in the tests); pinned reference client `04_reference/client_15.33.b21348` + `reference-client.json` |
| Build instructions | `docs/06_Build_and_Setup.md` |
| Maintenance guide | `docs/11_Maintenance_Guide.md` |
| Known limitations | `docs/08_Known_Limitations_and_Uncertainties.md` |
| Testing report and evidence | `docs/07_Testing_Report.md`, `05_verification/2.1.0/` |
| Changelog | `CHANGELOG.md` |

## 7. Not verified, and limits of this verification

* **No guarantee for future Tibia updates.** Changes were simulated synthetically. Real future formats are unknown, and changes of meaning with an unchanged structure cannot be detected (`10` §5).
* No real client update was observed while the program ran.
* Display scales other than 100 %, other machines and Windows versions, screen readers and high contrast, audible playback, clipboard contents.
* The *Download list* tool against the real test server (mocked only).
* The `action=render` fallback could not be exercised live: it is blocked for programs.

## 8. Note for this PC

The Windows session's window-class (user atom) table was close to exhaustion during the work: 200–480 free slots out of 16,384. While it stays like that, Windows may intermittently refuse to open windows, for any program. The program's tests now skip with that reason instead of hanging. **Signing out of Windows or restarting resets the table.**
