# Tibia Foundry 9.0 – Audit and Release Report

Audit of Tibia Foundry 8.8.2 → 9.0 (2026-11-06). Everything measured was measured on the machine in §C; what was reasoned rather than measured is labelled.

---

## A. Source code audit

### What was examined

| Area | Source |
|---|---|
| Local 8.8.2 source (the baseline) | `src/` (Core 160 C# files / 33.8k lines, App 108 C# files + 43 XAML / 23.6k lines, Tests 63 files / 16.5k lines), `tools/`, `docs/`, earlier audits (`TibiaFoundry-Technical-Documentation/06_Findings.md`, `TibiaFoundry_Performance_Audit.md`) |
| GitHub repository `RockyJLaRo/TibiaFoundry` | branches `main` and `claude/happy-goodall-ec7npn`, 24 commits, no releases, no tags |
| Website | `tibiafoundry.com` (GitHub Pages from `main` /, custom domain) and its source (`website/` of the 8.8.0 source tree; not in the 8.8.2 source package) |
| Dependencies | `dotnet list package --vulnerable --include-transitive`, `--outdated` |

**GitHub vs local.** The repository is the **website** (built pages, images, `CNAME`) plus two unrelated projects (`games/`, `pokemon/`) that this release leaves untouched. It holds **no application source** and **no releases**, so there were no newer code commits or fixes to merge; the local 8.8.2 source is the only code baseline. The branch `claude/happy-goodall-ec7npn` is an older website state (before the 8.8.0 translations). The live pages equal `website/site` of the 8.8.0 source byte for byte except line endings, so `website/src` is the current website source. Every download button pointed to `releases/latest`, which has no release behind it: **the site's downloads did not lead to any file**.

**Working copy.** 8.8.2 was copied to `Source_History/TibiaFoundry-9.0-Source/TibiaFoundry-9.0-source` with the website source added, and put under git (baseline commit first); the 8.8.2 folders and ZIPs are unchanged.

### Architecture: strengths

* Clean split: `TibiaFoundry.Core` (UI-free engine: client formats, compatibility, client store/archive, Asset Library on SQLite, exports, scripts) and `TibiaFoundry.App` (WPF/MVVM with CommunityToolkit.Mvvm, one `AppServices` composition root, `PageViewModel.RunAsync` for every long operation with progress, cancellation and operation tracking).
* Tool manifests (`BuiltInTools`) describe each tool's operations, inputs, outputs and dependencies and are validated by tests.
* File safety is systematic: `AtomicFile`, `OutputSession` (ask/keep both/overwrite with backup), `BackupStore` with hashes, `PathGuard` against traversal, zip-bomb limits, CSV formula-injection guard, XXE-safe XML.
* Localization in five languages with completeness tests; 692 tests (659 passing, 32 skipped without reference data, 1 failing at the start of this audit – see §F).
* Performance was audited in 8.8.1 (two-pass protobuf repair, folder listings instead of per-file queries, parallel indexing, handle-reuse verified writes); the remaining CPU cost is the inherently serial LZMA decoding.

### Weaknesses and findings (ranked)

| # | Finding | Kind | Status in 9.0 |
|---|---|---|---|
| 1 | Website download buttons point to `releases/latest`, but the repository has no releases | Distribution | **Fixed**: direct links to the `v9.0` assets; release published |
| 2 | Version in four places with different values (`Directory.Build.props` 8.8.2, `app.manifest` 5.0.0.0, `Portable.pubxml` 8.8.2 path, User-Agent built from the 3-part assembly version) (old F-07) | Release hygiene | **Fixed**: one source, derived values, tests |
| 3 | A backup "move" across volumes deleted the original before the copy was verified (old F-05) | Data loss | **Fixed** + test on two real volumes |
| 4 | `AtomicFile` did not flush to the disk before replacing (old F-18) | Data loss on power failure | **Fixed** for documents; bulk/regenerable files opt out |
| 5 | `AppearancesViewModel.UpdateDetails` is `async void` without a handler (old F-09) | Reliability | **Fixed** (damaged/locked sheet → preview message + log) |
| 6 | `NumericUpDown` adds button handlers again on every template application (old F-13) | UI bug | **Fixed** |
| 7 | A test asserted the pinned 15.33 client's object counts against whatever client is installed when no reference folder exists | Test reliability | **Fixed** |
| 8 | Sprite, sound and map-tile extraction write without `OutputSession` (old F-02) | Data safety | Kept: re-extracting into the same folder is the expected workflow and "Skip existing" exists; changing it would change established behaviour. Postponed (§J) |
| 9 | Client-folder write protection only knows default locations (old F-01) | Safety | Postponed (§J); editors already save copies elsewhere |
| 10 | Synchronous I/O on the UI thread in Clients refresh and Rebuilder plan (old F-03) | Responsiveness | Measured as 0.2–0.9 s background-able work; postponed (§J) |
| 11 | Two `CatalogEntry` types, `ClientStorageMode` as string (old F-11, F-14) | Code quality | Kept: renaming touches serialized data/public API for no user benefit |

**Dead code.** The 8.x audits removed the dead features (`RemovedFeatureTests` guards them); this audit found no further unreferenced classes. The six commands without a UI binding (old F-08) are reachable from scripts/tests and were kept. `Newtonsoft.Json` is still used (JSON diff, compare and export parity with the original program's output) and stays.

**Security.** No hard-coded credentials; only `HttpFetcher` uses the network (TibiaWiki, test-server assets with hash check); logs are redacted. No vulnerable NuGet packages (direct or transitive). The new exporter only reads the loaded client data and writes `.txt` files chosen by the user, with names sanitized and never replacing a file in batch mode.

### Data management: is a database the right move?

The Asset Library already uses SQLite (8.8) where it pays off (search, filters, paging over tens of thousands of files). Settings, catalogs, workflows and manifests are small JSON documents edited as a whole; moving them into a database would add migrations and a single point of corruption without a measurable gain. 9.0 therefore keeps the storage formats and improves **durability** of those writes instead (§B). No format changed, so no migration is needed and 8.8.2 data is used as is.

## B. Data-management improvements

| Change | Effect |
|---|---|
| `AtomicFile.WriteAllText` / `WriteAllBytes` flush to the disk (`Flush(true)`) before the temporary file replaces the destination | Settings, item catalogs, collections, workflows, manifests, gallery/manifest files, operation history survive a power loss as either the old or the new file |
| Thumbnail cache and extracted sounds pass `durable: false` | Thousands of regenerable files keep the 8.8.1 speed |
| `BackupSet.Add(move: true)`: rename on the same volume, otherwise copy → verify SHA-256 → delete original | No more loss of the original when a cross-volume copy is bad |
| Infobox export reads the existing TibiaWiki item cache (`WikiItemIdTable.TryReadCache`) instead of downloading | No duplicate downloads; the tab never goes online by itself |

Memory and indexing were not changed (the 8.8.1 work already made rescans 66 ms and halved cold indexing); the benchmarks confirm identical allocations.

## C. Performance

**Method.** The 8.8.1 harness (`tools/benchmarks`, Stopwatch + `GC.GetTotalAllocatedBytes`, median of 3 after a warm-up) was compiled once against the untouched 8.8.2 Core and once against 9.0 and run on the same machine: Windows 11 Pro 26200, 16 logical CPUs, .NET 10.0.14, real-time antivirus on, installed live client 15.33.d96497 (5,084 sheets, 43,518 objects, 1,480 outfits). First a full run of 15 scenarios of each build, then three interleaved A/B rounds of the CPU-bound scenarios to separate code from machine variance.

| Scenario | 8.8.2 (ms) | 9.0 (ms) | Difference | Allocated 8.8.2 / 9.0 |
|---|---|---|---|---|
| appearances-load | 243 (240–251) | 248 (226–256) | +2 % | 78.9 / 78.9 MB |
| compat-check (start-up compatibility check) | 593 (565–594) | 566 (550–578) | −5 % | 147 / 147 MB |
| decode-300-sheets | 776 (770–812) | 839 (762–846) | +8 % | 350.6 / 350.6 MB |
| sprite-repo-5k-list-order (scrolling thumbnails) | 714 (689–722) | 706 (691–719) | −1 % | 187.8 / 187.8 MB |
| outfit-render-150 | 799 (780–814) | 805 (736–842) | +1 % | 436.5 / 436.5 MB |
| gif-build-300 | 1,220 (1,113–1,289) | 1,241 (1,225–1,282) | +2 % | 333.3 / 333.3 MB |
| compare-appearances-self | 498 (427–529) | 510 (490–528) | +2 % | 256.9 / 256.9 MB |
| launch-check-quick (every start) | 865 (817–901) | 898 (760–961) | +4 % | 119.1 / 119.1 MB |

Median of three interleaved rounds (each round: median of 3 repetitions), range in brackets. The full sequential run of all 15 scenarios (also catalog-load 36 / 36 ms, folder-inspect, client-scan 3.8 / 4.1 s, extract-300-sheets-png 23.8 / 23.9 s, library-index-cold 6.76 / 6.79 s, library-index-warm 102 / 73 ms) likewise shows identical allocations. Raw results: `tools/benchmarks/results/9.0-*.csv`.

**Reading.** 9.0 changed none of these code paths, and every scenario allocates exactly the same memory in both builds. Wall-time differences between single runs are machine variance (the first sequential run showed 9.0 up to 30 % slower in pure-CPU scenarios whose code is identical; interleaving removes most of it). There is no measured regression and no claimed speed-up: the measurable bottlenecks were removed in 8.8.1 and what remains is the serial LZMA decoding of sprite sheets, which the 8.8.1 audit already investigated without a worthwhile change.

**New code.** Building and writing an infobox: 2,000 objects of the installed client in 433 ms, about 0.2 ms each, including the scan of all 43,518 objects for others with the same name. The infobox is only built while its tab is visible.

**Not measured.** Start-up time of the packaged program is compared in §F (same method as 8.8.1's `startup_perf.ps1`). CPU% and disk counters were not recorded separately; UI navigation timings below the UI harness's settle time cannot be measured.

## D. New functionality: TibiaWiki Infobox Exporter

See `docs/26_TibiaWiki_Infobox_Exporter.md`. In short: Appearances → object → **Wiki infobox** builds `{{Infobox Object}}` from the client's data, following the template's parameter list (read from the template source on 2026-11-06; required parameters: name, itemid, implemented, objectclass, primarytype, pickupable, immobile, notes, and for pickupable objects droppedby, weight, value, npcvalue, npcprice, buyfrom, sellto, marketable) and the formatting of real item pages. Values carry their source; nothing the client does not hold is filled in. The design (`WikiTemplate` + `WikiTemplateWriter` + a builder per template) lets further templates (creatures, NPCs) reuse the writer and the editing panel.

Why this and not a bigger feature set: it removes the most repetitive part of creating or checking an item article (looking up ~25 facts the client already knows, including NPC prices) while staying honest about what the client cannot know. Other ideas were considered and left out to avoid scope creep (§J).

## E. Cleanup and modernization

* Version handling consolidated (one `<Version>`, derived file/assembly versions, manifest aligned, publish profile uses `$(Version)`, User-Agent from `AppIdentity.Version`, packaging scripts read the same value).
* Release scripts: installer named `-Installer.msi`; source package excludes `TestResults`, benchmark artifacts, `*.user`/`*.suo`/`*.log` and the unrelated `games`/`pokemon` folders.
* Dependency: System.Drawing.Common 10.0.12 → 10.0.14 (patch, matches the runtime). Major upgrades available (Magick.NET 15, NAudio 3, xUnit v3 4.x) were **not** taken: they change APIs used by image export, sound playback and every test, for no user-visible benefit in this release.
* No mass refactoring; no code was deleted without proof that it is unused.

## F. Testing

<!--TEST-RESULTS-->

## G. Website

<!--WEBSITE-->

## H. Release packages

<!--PACKAGES-->

## I. Publication

<!--PUBLICATION-->

## J. Remaining issues and postponed work

* **Extraction overwrite policy (old F-02)**: sprite/sound/map-tile extraction still replaces files of an earlier extraction in the same folder (by design; "Skip existing" exists). A per-run "ask / keep both" choice would be the next step.
* **Custom client locations (old F-01)**: write protection knows the default and registered client folders only.
* **UI-thread I/O in Clients refresh / Rebuilder plan (old F-03)**: 0.2–0.9 s; could move to background work.
* **Major dependency upgrades** (Magick.NET 15, NAudio 3, xUnit v3 4.x): need their own test cycle.
* **Infobox**: `implemented`, `weight`, combat values and Rookgaard NPC prices (`npcvaluerook`, `npcpricerook`) cannot come from the client; trades in other currencies are reported, not written. Further templates (Infobox Creature, Infobox NPC) can reuse `WikiTemplateWriter`.
* **Code signing**: packages are not signed.
* **Screenshots**: the website's screenshots are those of 8.8.0 (the pages they show are unchanged); the infobox is shown as real generated markup instead of a screenshot.
