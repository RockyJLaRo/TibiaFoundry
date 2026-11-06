# Stability Audit Report – version 2.0 → 2.1

> **Note (Tibia Foundry 6.0):** this is a historical record of the stability audit of version 2.1.0. The program was later renamed Tibia Forge (version 3.0) and then **Tibia Foundry** (version 4.0). Paths and figures below are those of the earlier version; since 6.0 the earlier product name is written as the placeholder `<v2>` (see `13_Branding_and_Attribution.md` §10). See `07_Testing_Report.md` for current results.

This report records an independent audit of the 2.0.0 reconstruction: what was found, how severe it is, why it happens, and what was done about it.

* §1–2 were written **before** any change was made (Phase 1 baseline).
* §3 records the implemented fixes and how each was verified. §4 lists problems found *during* the work, §5 the performance and resource review, §6 what remains open.

## 1. Baseline (before changes)

**Preserved version**: `06_baseline_2.0.0/`

* `src-2.0.0-baseline.zip`: 150 source files;
* `docs-2.0.0-baseline.zip`;
* the 2.0.0 portable release and its checksums;
* `test_results/baseline.trx`.

**Baseline test run** (2026-10-12): **69 passed, 0 failed, 0 skipped**, with the full 223,694-sprite comparison enabled.

* Passing tests show that the current client data is processed like the original program. They do **not** cover malformed, truncated, changed or future data, network failures, or client updates while the program runs. That is where this audit found most problems.

**Environment**: Tibia client 15.33.b21348 (live package `Tibia`), test client 15.25.0e418e (`TibiaExternal`), Windows 11 Pro 10.0.26200, .NET 10.0.12.

**Dependencies**: `dotnet list package --vulnerable --include-transitive` reports **no known vulnerabilities**.

* Patch and minor updates are available for CommunityToolkit.Mvvm, Newtonsoft.Json, System.Drawing.Common, Google.Protobuf and Grpc.Tools.
* Major updates (Magick.NET 15, NAudio 3, xUnit 4, Test SDK 18) are deliberately not taken. They can change GIF encoding or audio behaviour, which the parity tests pin.

**Origin of features**:

* *Recovered*: all output logic. It is ported from the decompiled original and parity-tested.
* *Recreated*: the whole UI layer (WPF instead of WinForms).
* *New in 2.0*: themes, sprite decoding from the client, async/cancel, logging, settings store, portable mode, self-test, UI snapshot.

No original behaviour was found to be lost; the feature inventory (`03_…`) was re-checked against `02_analysis/original_ui_tree.txt`.

## 2. Findings

Severity:

* **Critical**: crash or data loss in normal use.
* **High**: wrong output, a crash on plausible input, or a likely failure after a Tibia update.
* **Medium**: misleading behaviour, robustness or integrity gap.
* **Low**: hygiene.

| ID | Area | Finding | Sev. | Cause | Planned fix |
|---|---|---|---|---|---|
| A01 | LZMA decoder | A **truncated** file decodes to garbage without any error. The range coder feeds zeros past the end of input when the size is known. | High | `NextByte()` returns 0 past the end; only the unknown-size mode checked overrun | Fail with "file truncated" when the input is exhausted |
| A02 | CIP container / LZMA | The declared output size (up to 2 GB) is trusted and allocated up front. A corrupt header causes `OutOfMemoryException` or a multi-GB allocation. | High | No plausibility limit | Cap the declared size (256 MB) and check it against the payload; report corrupt data |
| A03 | LZMA decoder | An end marker before the declared size returns a short buffer silently. | Medium | Treated as normal termination | Treat as corruption when the size is known |
| A04 | BMP decoder | Width, height, pixel offset and mask position are trusted: negative, zero or huge values raise raw exceptions or huge allocations. | High | No header validation | Validate every field against the buffer and a size cap |
| A05 | Sprite sheets | The sheet layout is assumed to be 384×384, and the slicer silently returns fewer sprites than the catalog claims. Callers then index past the end, or a resized sheet is cut wrongly. | High | Constant layout, no validation | Derive the grid from the decoded image, validate it against the catalog entry, and report mismatches |
| A06 | `catalog-content.json` | Several failure modes: an unknown `spritetype` is treated as 32×32 (wrong sprites); non-numeric or out-of-range fields crash (`int.Parse`); a non-array root throws `InvalidOperationException`; overlapping or inverted sprite ranges are not detected (lookup returns wrong sheets). | High | Parser assumes the current format | Tolerant parser that **records problems**: skip invalid entries, reject unknown sheet types, detect overlaps, verify the root structure |
| A07 | Security / paths | File names from client data are combined into paths unchecked: catalog sheet and data files, sound file names, and *original sheet names* on export. `..\` or rooted names could read or write outside the intended folders. | High | No validation of data-supplied names | Accept only plain file names; reject others with a message |
| A08 | Client paths | `FindInCatalog` catches `JsonException` only. A catalog with a different root type throws an uncaught `InvalidOperationException` during page navigation. | Medium | Narrow catch | Use the shared tolerant catalog reader |
| A09 | Client updates | If the Tibia launcher updates the client while the tool runs, the cached catalog and loaded databases go stale. Sprite IDs shift between versions, so **wrong sprites** can be shown or exported, or files are "missing". Nothing detects this. | High | No change detection | Fingerprint the client (version + catalog). Check it on navigation and window activation; on change, unload stale data, refresh and tell the user |
| A10 | Sprite folders | The default *folder, then client* mode trusts an extracted `<id>.png` folder that may come from an **older client**, e.g. imported from 1.x settings. Sprite IDs change between versions, so outdated sprites are used silently. | High | No provenance check | The extractor writes a manifest (client version, catalog hash). The repository rejects folder sprites whose size disagrees with the client and warns when the manifest is from another client version |
| A11 | Sounds | Decoded WAVs are cached in `%TEMP%` **by sound ID**. After an update the same ID can be a different sound, and the old audio is replayed. | Medium | Cache key independent of content | Key the cache by the content-hashed source file name |
| A12 | Sounds | Sounds referenced by `sounds.dat` but missing on disk are counted as "skipped", so a partial extraction looks successful. | Medium | Wrong classification | Count them as failures with the file name |
| A13 | All batch operations | The status bar shows a green success message when some items failed (the warning dialog comes afterwards). | Medium | `Info()` used regardless of the result | One `ReportBatch` helper: success, warning (partial) or error (nothing written) |
| A14 | File output | Exports write non-atomically: tables, Lua module and log, compare report, map lists, LZMA decode, downloads, sound extraction. Atomic writers use a fixed `<file>.tmp` that collides between concurrent writes and is left behind after a failure. | Medium | Ad-hoc writes | `AtomicFile` helper (unique temp file in the target folder, replace on success, delete on failure) used everywhere |
| A15 | Downloads | The *Download list* tool has no response-size limit, no content validation, no retry for transient errors, no 429 handling, and a partially downloaded file overwrites the existing one. | Medium | Minimal implementation | Shared HTTP helper (timeouts, bounded retries with backoff, Retry-After, size cap); atomic writes |
| A16 | TibiaWiki integration | The item-ID parser splits on the literal `<tr>`, skips a fixed 2 rows, and accepts **any** non-empty result, so a partial parse after a page redesign would silently mislabel items. It uses the HTML render URL instead of the documented MediaWiki API, has no caching, and makes no plausibility check. | Medium | Fragile scraping | API first (`api.php?action=parse`), render URL as a fallback; structure-tolerant parsing; coverage checks; a cache with an age limit and an offline fallback that says so |
| A17 | Protobuf data files | Parse failures surface as raw `InvalidProtocolBufferException` text. New fields added by a client update are **ignored silently** (no drift detection), and removed or retyped fields are not detected. | High | No validation layer | Classify errors (missing, damaged, unsupported) per file. Add a schema inspector that walks the wire format against the compiled schema and reports unknown or retyped fields |
| A18 | Appearance structure | Renderers assume every frame group has sprite info and that the sprite count matches layers × patterns × phases. Several places dereference without null checks, so a format change causes `NullReferenceException`s or wrong frames. | Medium | No structural validation | Validate on load: record and flag inconsistent appearances; make renderers null-safe |
| A19 | House Images | A duplicate house ID in staticdata crashes the page (`ToDictionary`). | Medium | Unique key assumed | Use the first entry and record the duplicate |
| A20 | Map tiles | `int.Parse` on tile-name numbers overflows on long digit runs. | Low | Unchecked parse | `TryParse`; skip and report the file |
| A21 | File names | `Sanitize` ignores Windows device names (`CON`, `NUL`, `COM1`…) and over-long names. | Low | Incomplete rule set | Handle device names and limit the length |
| A22 | Settings | Properties written by a **newer** version are dropped when this version saves (downgrade data loss). The `Version` field is never interpreted. | Medium | No extension data | Preserve unknown JSON properties; warn about newer settings versions and do not write an older version over them silently |
| A23 | Diagnostics | No way to see which client version is installed, whether its data are supported, or which features are affected after an update. | High | Missing feature | **Compatibility** page and start-up check: version, per-file status (supported, unfamiliar but valid, partial, unsupported), integrity, affected features, guidance |
| A24 | Maintainability | Version-sensitive constants are spread across modules (file prefixes, sheet geometry, sprite types, palette and tables, URLs) and the update procedure is undocumented. | Medium | Organic growth | Central `TibiaFormat` definitions, a dependency map and a maintenance guide |
| A25 | Loading | Two pages can load the same file at the same time (e.g. Static Data and Appearances), doubling time and memory. | Low | No in-flight sharing | Share an in-flight load per file |
| A26 | Dependencies | Patch updates are available. | Low | – | Take patch and minor updates; verify with the full parity suite |

## 3. Fixes and verification

**How fixes were verified**:

* **Test**: a regression test that exercises the fixed behaviour.
* **Reverse check**: the original defect was put back into the code temporarily, and the named test had to **fail**. A test that passes against both the old and the new code proves nothing. 17 defects were checked this way; all 17 were caught.
  * Script: `05_verification/2.1.0/scripts/mutation_checks.py`.
  * Results: `05_verification/2.1.0/mutation_report.txt`.
* Features that could only be checked by reading the code are marked *review only*.

Final suite: **159 tests: 158 passed, 1 skipped** (the opt-in live TibiaWiki check), **0 failed**, with the full sprite comparison enabled. Baseline: 69 tests. See `05_verification/2.1.0/test_results/`.

| ID | Status | What was done (main files) | Verified by |
|---|---|---|---|
| A01 | Fixed | `LzmaDecoder`: with a known output size, running out of input is an error ("compressed data ended after X of Y bytes"). `CipLzma` additionally checks the container length exactly (A29). | `LzmaDecoderDetectsTruncatedInputOnItsOwn`, `TruncatedSheetIsReportedNotDecodedToGarbage`, `FuzzedSheetsFailSafely`; reverse check ✔ |
| A02 | Fixed | Declared sizes over 256 MB are rejected before any allocation (`LzmaDecoder`, `CipLzma.MaxDecompressedSize`). The compressed size is checked against the file. | `ImplausibleDeclaredSizeIsRejectedWithoutAllocating` (asserts < 64 MB allocated); reverse check ✔ |
| A03 | Fixed | An end marker before the declared size is reported as damage. | Fuzz test only (`FuzzedSheetsFailSafely`, 150 seeded variants); no targeted test |
| A04 | Fixed | `BmpDecoder` validates width, height (≤ 16384), header size, pixel offset and mask position against the buffer. Every failure is an `InvalidDataException` naming the file. | `MalformedBitmapsAreRejected` (theory), `ValidSyntheticBitmapDecodes`, `FuzzedSheetsFailSafely` |
| A05 | Fixed | `SpriteSheetDecoder.Slice` derives the grid from the image, refuses a sheet that cannot hold the catalog's range, and returns exactly `SpriteCount` sprites. `DecodeSheet` checks the SHA-256 of the decompressed bitmap against the hash in the file name. | `SheetLayoutIsValidatedAgainstTheCatalog`, `SheetWithWrongChecksumNameIsRejected`, `AllSprites_MatchOriginalExtraction` (223,694 sprites); reverse check ✔ |
| A06 | Fixed | `AssetCatalog` is a tolerant parser: invalid entries are skipped and listed in `Problems` (unknown sprite type, bad or inverted range, unsafe name, overlap, non-object). A non-array root or invalid JSON gives a clear `InvalidDataException`. Numbers may be numbers or strings. | `CatalogSkipsAndReportsEntriesItCannotUnderstand`, `CatalogWithChangedStructureIsRejectedClearly`, `FuzzedCatalogsFailSafely` (300 variants), `LargeCatalogParsesQuickly` (200,000 entries); reverse check ✔ |
| A07 | Fixed | Every file name that comes from data goes through `TibiaFormat.SafeFileName` or `ResolveUnder`: catalog sheets, data files, sound files, original sheet names, download list entries, map tiles. Unsafe names are reported, not used. | `DataSuppliedFileNamesAreValidated`, `ResolveUnderNeverLeavesTheRoot`, `SoundFileNamesFromDataCannotEscapeTheirFolders`, `DownloadListKeepsEveryFileInsideTheOutputFolder`; reverse check ✔ |
| A08 | Fixed | `TibiaClientPaths.FindInCatalog` tolerates any root type and IO errors. `ReadClientVersion()` reads `package.json`. | `ClientPathsSurviveAChangedCatalogAndManifest` |
| A09 | Fixed | `ClientDataService` fingerprints `package.json` and the catalog (path, size, time).<br>• **When it checks**: on page navigation and window activation, never while an operation runs.<br>• **On a change**: it unloads all data and thumbnails, re-reads the paths, tells the user (old → new version), and re-runs the compatibility check. | `ClientUpdateWhileRunningDiscardsStaleData` (a launcher update is simulated by rewriting the catalog); reverse check ✔ |
| A10 | Fixed | The extractor writes `<v2>-sprites.json`: client version, catalog SHA-256, time and count.<br>• **Folder from another client**: ignored, except in *folder only* mode, with a warning.<br>• **Folder sprite of the wrong size**: replaced by the client sprite.<br>• **Stale folder, "skip existing" set**: refreshed instead of mixed. | `SpriteFoldersFromAnotherClientVersionAreNotTrusted`, `FolderSpritesOfTheWrongSizeAreReplacedByClientSprites`, `ExtractorRecordsProvenanceAndRefreshesStaleFolders`; reverse check ✔ |
| A11 | Fixed | The decoded-sound cache is keyed by the content-hashed client file name and written atomically. | `DecodedSoundCacheFollowsTheClientFileNotTheSoundId`; reverse check ✔ |
| A12 | Fixed | Missing or unsafe sound files are failures named in the result, not "skipped". | `SoundFileNamesFromDataCannotEscapeTheirFolders` |
| A13 | Fixed | `PageViewModel.ReportBatch`: green only when everything succeeded; amber for partial results or notes; red when nothing was written. Used by every batch operation. | `BatchResultsAreReportedHonestly`; reverse check ✔ |
| A14 | Fixed | `AtomicFile`: a unique hidden temporary file in the target folder, a move on success, deletion on failure. It is used by every export and download, the caches, the sprite manifest and the settings file (A37). | `AtomicWriteNeverLeavesPartialFiles`, `FailedDownloadsNeverReplaceExistingFiles`, `SaveAndLoadRoundTripsPreferences` (no temporary file left) |
| A15 | Fixed | `HttpFetcher` is the only network code:<br>• GET only; 45 s timeout; 3 attempts with exponential backoff and jitter.<br>• Honours `Retry-After` up to 30 s; 32 MB size cap; atomic download.<br>• Timeouts are per request only; there is no client-wide limit (A35). A browser challenge is reported as a refusal and not retried (A34).<br>• *Download list* also verifies each file against the SHA-256 in its name before it replaces anything. The convention was checked on all 19,078 hash-named files of both installed clients: the decompressed content for `.lzma`, the file itself otherwise. | `TransientFailuresAreRetriedWithBackoff`, `PermanentErrorsAreNotRetried`, `RateLimitingHonorsRetryAfterWithinLimits`, `UnresponsiveServersTimeOutAndGiveUp`, `OversizedResponsesAreRejected`, `CancellationStopsImmediately`, `DownloadedClientFilesAreCheckedAgainstTheHashInTheirNames`, `ChecksumInNameIsCheckedOnTheDecompressedContentOfLzmaFiles`, `BrowserChallengesAreReportedAndNotRetried`; reverse check ✔ |
| A16 | Fixed | `WikiItemIdTable.GetAsync`, in order:<br>• a fresh cache (24 h);<br>• the MediaWiki API (`action=parse`);<br>• the `action=render` page;<br>• the last good cache, with a notice;<br>• otherwise a clear error.<br>The parser works on table structure (link cell = name, ID cell = numbers or ranges), logs irregular rows, and rejects results under 1,000 IDs. On the live page it finds 13,875 IDs, a superset of the 2.0 parser's 13,857. | `RealPageExcerptIsParsedIncludingItsIrregularities` (real 46-row excerpt, CC BY-SA), `ChangedPageLayoutsAreReportedNotSilentlyAccepted`, `ApiIsUsedFirstAndTheResultIsCached`, `RenderPageIsTheFallbackWhenTheApiFails`, `StaleCacheIsUsedAndAnnouncedWhenTheWikiIsUnreachable`, `NoNetworkAndNoCacheIsAClearError`, `DamagedCacheIsIgnored`, `ApiResponsesInBothFormatVersionsAreUnderstood`, `WikiRequestsAllowForSlowPageParsing`; reverse check ✔. The live check was run three times on 2026-10-12:<br>• passed, 43 s (cold server-side parse);<br>• **failed**: the API answered 503 twice and the fallback was refused; the error was clear;<br>• passed, 1 s.<br>Logs are in `05_verification/2.1.0/test_results/`. |
| A17 | Fixed | `ProtoLoader` turns every failure into a `DataFileException`: *missing*, *unreadable* or *damaged / changed format*, naming the file, with advice in plain words. `SchemaInspector` walks the protobuf wire format against the compiled schema and reports unknown fields (new client data) and fields whose wire type changed. Drift that has been validated (`staticmapdata Tile` 101/102) is listed as known. | `LoadErrorsSayWhatIsWrongWithWhichFile`, `FuzzedDataFilesFailWithDataFileExceptionOnly`, `NewFieldsAddedByAnUpdateAreDetectedAndIgnoredSafely`, `FieldsWhoseTypeChangedAreDetected`, `KnownDriftOfValidatedDataIsNotReportedAsNew`, `RealAppearancesHaveNoUnknownSchemaFields`, `DataFileProblemsAreExplainedInPlainWords`; reverse check ✔ |
| A18 | Fixed | `DataValidation.Appearances` flags:<br>• duplicate IDs and missing frame groups or sprite info;<br>• sprite counts that disagree with layers × patterns × phases;<br>• sprite IDs missing from the catalog.<br>The outfit, GIF, house and sheet renderers fail with a clear message instead of a `NullReferenceException`. | `ValidationFlagsStructurallyBrokenAppearances`, `RenderersFailClearlyOnStructurallyBrokenData` |
| A19 | Fixed | House names: the first entry wins; duplicates no longer crash the page. | `HouseListSurvivesDuplicateHouseIds`; reverse check ✔ |
| A20 | Fixed | Tile names use `TryParse`; impossible coordinates are skipped. | `MapTilesWithImpossibleCoordinatesAreSkipped`; reverse check ✔ |
| A21 | Fixed | `FileNames.Sanitize` prefixes device names (`CON`, `NUL`, `COM1`…) with `_` and limits names to 200 characters. | `FileNamesAvoidWindowsDeviceNames`, `LongFileNamesAreShortened`; reverse check ✔ |
| A22 | Fixed | Unknown settings are preserved (`[JsonExtensionData]`). Settings from a newer format are backed up (`settings.json.v{N}.bak`) and the user is told. *Reset to defaults* keeps both (A28). | `SettingsFromANewerVersionKeepUnknownOptions`, `ResetToDefaultsKeepsOptionsFromNewerVersions`; reverse check ✔ |
| A23 | Fixed | New **Compatibility** page (Application group):<br>• client version and whether it was validated;<br>• per-file status and integrity, schema drift, record validation;<br>• affected features and plain-language advice; *Copy report*.<br>It also adds a background check at start-up and after client updates, a warning banner on affected pages, and a compatibility check in `--self-test`. | `ClientDataInUseIsReportedAsCompatible`, `UnknownClientVersionWithValidDataIsUnfamiliarButCompatible`, `DamagedAndChangedFilesAreIsolatedToTheirFeatures`, `MissingPackageIsReportedNotThrown`; UI snapshot checks "compatibility banner appears / hides"; screenshots in `05_verification/2.1.0/ui_snapshots` |
| A24 | Fixed | Version-sensitive constants are now in `Core/Compatibility/TibiaFormat.cs`; external addresses in `Core/Net/ExternalEndpoints.cs` (the review found five literal URLs left elsewhere, A36). The dependency map is `10_Tibia_Compatibility_Report.md`; the procedure is `11_Maintenance_Guide.md`. | Review, plus a search for literal `http` addresses outside `ExternalEndpoints` (none left) |
| A25 | Fixed | Concurrent requests for the same file share one load (`ClientDataService.Shared`). | `ClientUpdateWhileRunningDiscardsStaleData` (asserts the same task) |
| A26 | Done | Updated:<br>• Google.Protobuf 3.31.1 → 3.36.2, Grpc.Tools 2.72.0 → 2.84.0;<br>• Newtonsoft.Json 13.0.3 → 13.0.4;<br>• System.Drawing.Common 10.0.0 → 10.0.12;<br>• CommunityToolkit.Mvvm 8.4.0 → 8.4.2.<br>Major versions were deliberately not taken. | Full parity suite, including all 223,694 sprites, outfit and GIF parity |

## 4. Problems found during the work

| ID | Sev. | Finding | Fix | Verified by |
|---|---|---|---|---|
| A27 | Medium | `ClientDataService.Catalog` threw `InvalidDataException` for a damaged catalog. It was found by the new client-update test, where it surfaced during page navigation. | Caught; the reason is exposed as `CatalogError` and shown on the Compatibility page | `ClientUpdateWhileRunningDiscardsStaleData` |
| A28 | Medium | *Reset to defaults* overwrote the settings format number and dropped options written by a newer version: a silent downgrade. | Both are excluded from the reset | `ResetToDefaultsKeepsOptionsFromNewerVersions`; reverse check ✔ |
| A29 | Low | The container truncation check compared against the whole file, so up to ~32 missing bytes passed it. The decoder check (A01) still caught them. | Exact check. The compressed size counts every byte after its field; this was verified on all 17,367 LZMA files of both installed clients. | `TruncatedSheetIsReportedNotDecodedToGarbage` (1, 5 and 20 bytes missing) |
| A30 | Low | The TibiaWiki User-Agent said "2.0" whatever the version. | Taken from the assembly version | `RequestsIdentifyTheToolAndItsVersion` |
| A31 | Medium (tests) | The UI test helper hung for its full timeout when Windows refused the dispatcher's hidden window ("Not enough memory resources"): the retry reused the thread's half-built dispatcher, which never runs queued work. It was diagnosed with trace points and a minimal test (`TmpA`: services only), which hung the same way. | Every attempt now uses a new thread. A 30 s start watchdog turns "the test body never started" into a skip that states the environmental reason, never a pass. A retry is recorded as a test warning. | 3 consecutive UI-flow runs and the final full run, 0 hangs |
| A32 | Low | Line endings were mixed: in 2.0.0, 98 LF, 46 CRLF and 2 mixed files. | Normalised to CRLF; `.editorconfig` added (fixtures untouched) | Byte check of all 163 text files |
| A33 | Low | The background compatibility check allocates about 335 MB of temporary data at each start; about 3 MB stays live. Idle memory is about 49 MB higher until the garbage collector runs. | Accepted and documented (§5). Forcing collections or caching results would add complexity for little gain. | Measurement, §5 |
| A34 | Medium | The fallback address of the TibiaWiki item list (`?action=render`) now answers non-browser clients with a Cloudflare challenge (403, `cf-mitigated: challenge`). It was reported as a bare "403 Forbidden". | Recognised and reported as "refused automated access (it requires a browser check)"; never retried and never worked around. The documented API remains the primary source. | `BrowserChallengesAreReportedAndNotRetried`; observed live |
| A35 | Medium | The shared `HttpClient` had a 60 s client-wide timeout that silently overrode the per-request limits. A cold parse of the 1.2 MB wiki page took about 40 s, close to the 45 s default, so the wiki could fail just from being slow. | Client-wide timeout removed (only `HttpFetcher` uses the client). The wiki request gets 120 s and 2 attempts (`WikiItemIdTable.WikiFetchOptions`). | `WikiRequestsAllowForSlowPageParsing` |
| A36 | Low | Five literal URLs bypassed `ExternalEndpoints`: three About links, the tibiamaps.io link and the User-Agent. | They now use `ExternalEndpoints` | Search for literal URLs |
| A37 | Low | The settings file was still written through a fixed `settings.json.tmp`. | `AtomicFile` | `SaveAndLoadRoundTripsPreferences` |
| A38 | High (maintenance) | The parity tests compared **whatever client was installed** with reference outputs made from client 15.33.b21348, and the references did not record their client. After the next Tibia update, 19 parity tests would have failed although the program was fine, burying any real failure. | The reference client is pinned:<br>• a read-only copy in `04_reference/client_15.33.b21348` (7,097 files, byte-identical);<br>• `reference-client.json` records its version and catalog SHA-256;<br>• `TestData` uses that copy (`TWT_CLIENT_DIR` overrides it).<br>Parity tests **skip with the reason** when the data in use differ from the references. | Full suite against the test-server client 15.25 (`TWT_CLIENT_DIR`): 139 passed, 20 skipped with reasons (19 parity, 1 live), 0 failed. Normal run: 158 passed. |

## 5. Performance and resource review

Measured on this PC (Windows 11, .NET 10.0.12). Start-up rows show the range of two runs per version, using the portable build in a fresh folder. The scripts are in `05_verification/2.1.0/scripts/`.

| Measure | 2.0.0 | 2.1.0 | Note |
|---|---|---|---|
| Start → main window | 1.35–1.50 s | 1.34–1.41 s | Unchanged |
| Start → appearances loaded | 1.43–1.45 s | 1.44–1.45 s | Unchanged; the check starts afterwards |
| Compatibility check (background) | – | 3.1–4.3 s (done ~5 s after start) | Never blocks the window. It reuses data a page has already loaded. |
| Working set after 20 s | 301 MB | 350 MB | See A33: transient; about 3 MB of it stays live after a full GC |
| CPU in the first 20 s | 4.3 s | 8.2–8.6 s | The check: SHA-256 of all data files, schema walk, validation, one sample sheet per type |
| Sheet decode (5,084 sheets, single thread) | 15.4 s (computed: 2.1.0 minus the checksum) | 16.9 s | The SHA-256 check costs 9.1%, about 0.3 ms per 3.3 ms sheet |
| Full test suite | 69 tests | 159 tests, 71–116 s | Includes the 223,694-sprite comparison |

**Caches are bounded**:

* decoded sheets: 96 (≈ 55 MB);
* thumbnails: 6,000 (≈ 24 MB at 32×32);
* individual sprite files: 4,096 entries.

**Loading**: duplicate loads are shared (A25); a client update releases everything (A09).

## 6. Open points

* **No real client update has been observed.** A09 and A10 were tested by simulating updates: a rewritten catalog, a changed manifest. The Tibia launcher may update files in an order the simulation does not cover. A launcher update that is still in progress shows up as missing or damaged files until it finishes, and *Check again* clears that.
* **Future formats cannot be tested.** The compatibility tests use synthetic changes to the current format: added fields, retyped fields, unknown sheet types, damaged or missing files. Nothing here claims what a future Tibia version will look like. A new sprite type, compression scheme or a protobuf field renumbering will need a code update; the procedure is in `11_Maintenance_Guide.md`.
* **A03** has no targeted test (fuzzing only).
* **A24 and A30** were verified by review only.
* The live TibiaWiki check is opt-in (`TWT_LIVE_WIKI=1`), by design not part of the routine suite. Its three runs (two passes, one wiki-side 503) show the API is usable but not always available. The `action=render` fallback is currently blocked for programs (A34), so a wiki outage is bridged only by the program's cache (24 h fresh, older copies with a notice).
