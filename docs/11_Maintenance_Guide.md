# Maintenance Guide – Tibia Foundry 7.0

This guide is for whoever keeps Tibia Foundry working after Tibia updates. It answers:

* which parts depend on the Tibia version (§1);
* how the version is detected (§2);
* what to do and inspect after an update (§3);
* how to refresh the test references (§4);
* how to support a changed format (§5);
* how to update game definitions (§6);
* how to repair web integrations (§7);
* which tests must pass (§8);
* where the logs are (§9);
* how to recover from a failed update or migration (§10);
* how to make a release (§11).

Background: `10_Tibia_Compatibility_Report.md` has the full dependency map; `09_Stability_Audit_Report.md` explains why the safeguards exist.

> **Never modify the Tibia client folder** (`%LOCALAPPDATA%\Tibia`). The program and all tools only read it. If client files are damaged, let the Tibia launcher repair them.

## 1. What depends on the Tibia version

| Area | Files to look at |
|---|---|
| Client layout, file names, sprite types, sheet size, validated versions, known schema drift, Cyclopedia origin | `src/TibiaFoundry.Core/Compatibility/TibiaFormat.cs`: **start here** |
| Data file schemas (protobuf) | `src/TibiaFoundry.Core/Protos/*.proto`: `appearances`, `shared`, `staticdata_adapted`, `staticmapdata`, `map`, `sounds`, `sounds-common` |
| Game tables that are not in the data files | `src/TibiaFoundry.Core/Appearances/GameTables.cs`: market categories, vocations, slots, weapon types, 133-color outfit palette, 216-color automap palette |
| Compression and images | `Core/Compression/CipLzma.cs`, `LzmaDecoder.cs`; `Core/Imaging/BmpDecoder.cs`; `Core/Sprites/SpriteSheetDecoder.cs` |
| Catalog | `Core/Assets/AssetCatalog.cs`, `TibiaClientPaths.cs` |
| Checks and validation | `Core/Compatibility/CompatibilityChecker.cs` (features per file), `DataValidation.cs`, `SchemaInspector.cs` |
| Websites | `Core/Net/ExternalEndpoints.cs` (all addresses), `Core/Wiki/WikiItemIdTable.cs` (item ID parser) |
| Assets Editor (5.0) | `Core/Protos/Editing/appearances_editable.proto` (the editable copy of the appearances schema: **add new fields here too**, see §5.8), `Core/Editing/AssetsProject.cs` (saving: sheets, catalog), `CatalogWriter.cs`, `Core/Imaging/BmpEncoder.cs`, `Core/Compression/LzmaEncoder.cs` |
| Client Converter (5.0) | `Core/Legacy/LegacyFormat.cs` (profiles, signatures), `LegacyAttributes.cs` (attribute bytes per layout), `ClientConverter.cs` (field mapping, market categories) |
| RCC Editor (5.0) | `Core/Rcc/RccFormat.cs` (format versions, flags), `RccValidator.cs` (Qt's lookup) |

## 2. How the version is detected

* **Version**: `TibiaClientPaths.ReadClientVersion()` reads `"version"` from `<package>\package.json`, e.g. `15.33.b21348`.
* **Validated versions**: `TibiaFormat.ValidatedClientVersions` lists the versions this build was tested with. Any other version is accepted when its files pass the checks; it is reported as *Unfamiliar but compatible*.
* **Update detection while running**: `ClientDataService.CheckForClientChange()` compares the size and time of `package.json` and `catalog-content.json` with the values taken when the data were loaded. It runs on every page switch and every time the window is activated, and is skipped while an operation runs.
* **Compatibility check**: `CompatibilityChecker.Check()` runs in the background after start-up and after a detected update. Run it by hand on the **Compatibility** page (*Check again*) or with:

```
TibiaFoundry.exe --self-test <report folder>
```

`self-test.txt` then contains the full report. The exit code is 0 when nothing failed.

## 3. After a Tibia update: checklist

1. **Let the launcher finish.** While it writes files, the check can report *Missing* or *damaged (checksum)*. Wait, then click *Check again*.
2. **Open Compatibility** in the program.
   * Read the client version and every component's status (`10_…` §2).
   * Use **Copy report** to keep a copy.
3. **Interpret the result**:
   * *Supported* or *Unfamiliar but compatible* with no unknown fields: nothing to do. Continue with step 4 to confirm.
   * *Unfamiliar* with **unknown fields listed**: the client gained data. The program works; to use the new data, see §5.1.
   * *Partially compatible*: open **Details**. Typical causes:
     * catalog entries with an unknown sprite type (§5.4);
     * fields with changed types (§5.2);
     * records failing validation.
   * *Not supported*: details name the file and the reason (decode error, checksum, required content missing). See §5.
4. **Run the tests against the new client.** The parity tests skip with a stated reason, because their references belong to the old client. **Every other test must pass.**

   ```
   cd src
   set TF_CLIENT_DIR=%LOCALAPPDATA%\Tibia\packages\Tibia
   dotnet test TibiaFoundry.Tests\TibiaFoundry.Tests.csproj
   set TF_CLIENT_DIR=
   ```

5. **Look at the output.** These changes cannot be detected automatically (`10_…` §5), so look at least at:
   * a few known sprites, e.g. a sword, a creature and an outfit, in Appearances and Extract Sprites;
   * an outfit with colors and addons in the Outfitter: palette;
   * a mounted outfit;
   * a GIF of an animated item: frame order and timing;
   * one house in House Images;
   * one map area: coordinates on tibiamaps.io match the place;
   * the Cyclopedia crop, if it is used: tiles land at the right coordinates.
6. **If all is well**:
   * add the version to `TibiaFormat.ValidatedClientVersions`;
   * refresh the references (§4);
   * release (§11).

## 4. Parity references and the pinned client

The parity tests compare this program's output with output from the **original** program (1.8.0.26867):

* the reference outputs are in `04_reference/`;
* the client they were produced from is pinned in `04_reference/client_15.33.b21348/` (`package.json`, `assets`, `sounds`; read-only copy, 254 MB);
* `04_reference/reference-client.json` records its version and the SHA-256 of its `catalog-content.json`.

`TestData` uses the pinned copy, so **a Tibia update does not break the test suite**. If the client data in use differ from the references (another `TF_CLIENT_DIR`, or no pinned copy), each parity test is **skipped with the reason**. It never passes and never fails falsely.

**To move the references to a new client** (only while the original program can still read that client's data):

1. Make sure the installed client is the new version, and that `03_sandbox_original` holds the untouched original program.
2. Build `tools/ParityHarness` (.NET Framework 4.8, x86). It reads the installed client from `%LOCALAPPDATA%\Tibia\packages\Tibia\assets`.
3. Write to a **new** folder, so the old references stay until the new ones are checked:
   * `ParityHarness sprites <out>\original_sprites`
   * `ParityHarness reference <out>\original <out>\original_sprites all`
4. Copy `package.json`, `assets` and `sounds` of the installed client to `04_reference\client_<version>\`. Write `reference-client.json` with `folder`, `clientVersion` and `catalogSha256` (SHA-256 of `assets\catalog-content.json`).
5. Replace the old reference folders, then run the full suite with `TF_FULL_SPRITES=1`.

**When the original can no longer read the new data**, it cannot produce references. From then on:

* parity against the original is only possible on the pinned old client, and that remains the regression baseline for the decoding logic;
* new-format features are verified by targeted tests plus the visual checks of §3 step 5;
* write down in `07_Testing_Report.md` which features have no original reference.

## 5. Supporting a new or changed format

The Compatibility page details give the evidence for every case: field numbers, wire types, sample values, file names, error messages.

### 5.1 New protobuf fields ("information this version does not know")

The details show `message`, field number, wire type, count and a sample. For example: `tibia.protobuf.staticmapdata.Tile: unknown field 101 (LengthDelimited), 13×, e.g. 2 bytes: 1001`.

1. Decide whether the program needs the field. If not, add `(message, field)` to `TibiaFormat.KnownSchemaDrift` with a comment. **Only do this after checking that ignoring it changes no output**; the report then lists it as *known unmapped*.
2. To use it, add it to the matching `.proto` file with the same number. Choose a type that fits the wire type:
   * `Varint`: `uint32`, `int32`, `bool` or an enum;
   * `Fixed32`, `Fixed64`: `fixed32`/`float`, `fixed64`/`double`;
   * `LengthDelimited`: `string`, `bytes`, a nested message, or a packed repeated field. The sample shows text in quotes, otherwise hex.
3. Build. Grpc.Tools regenerates the classes. Expose the value where it is useful:
   * `AppearanceProperties` for the Data panel;
   * the JSON exports;
   * filters.

   JSON export property names are pinned by parity tests. Add new names; do not rename existing ones.
4. Add a test with a synthetic message containing the field (pattern: `CompatibilityTests.NewFieldsAddedByAnUpdateAreDetectedAndIgnoredSafely`).

### 5.2 Fields whose type changed

The details mark these as **"type changed"**. Google.Protobuf would otherwise drop the value silently.

* Update the `.proto` type.
* Check every use of the property: compile errors guide you.
* Re-run the parity tests on the pinned client to make sure old data still load.
* If old and new clients must both work, keep the old field and add the new one under its own number only if CipSoft did that too. Never guess.

### 5.3 Restructured messages

Renumbered fields, moved sub-messages or new required content are reported as *Not supported*: "required content missing", or many validation issues. Compare the new file with the schema:

* `SchemaInspector` details show what is where;
* `protoc --decode_raw < file.dat` shows the raw structure. `protoc.exe` comes with the Grpc.Tools package: `%USERPROFILE%\.nuget\packages\grpc.tools\<version>\tools\windows_x64\`.

Update the `.proto`, then `DataValidation` if its rules no longer fit.

### 5.4 New sprite type, sheet size or layout

* **New `spritetype` value**: the catalog problem lists it as "unknown sprite type N (skipped)". Add it to `TibiaFormat.SpriteTypes` with its sprite size, which can be derived from a decoded sheet: image size ÷ grid. Add a test sheet case.
* **Different sheet size**: slicing adapts automatically, and the report notes it. Check `TibiaFormat.SheetSize` (validation only) and the parity of a few sprites.
* **Different sprite order inside sheets**: not detectable; it would show as wrong sprites in §3 step 5. Change `SpriteSheetDecoder.Slice`.

### 5.5 New compression or container

* *Not supported* for the sprite catalog with a decode error means the CIP container or LZMA parameters changed. Inspect a file's header:
  * marker `70 0A`;
  * two varints: decompressed size, then the size of the rest;
  * 5 property bytes;
  * 8 size bytes.
* Adjust `CipLzma.TryReadHeader` / `LzmaDecoder`.
* `RobustnessTests` must keep passing; they guard truncation, size limits and fuzzing.
* The checksum in the file name (SHA-256 of the decompressed data) tells you when decoding is right.

### 5.6 New catalog entry types

Unknown types are listed as *not used by this version*, which is harmless. To use one, add a `TibiaFormat.CatalogType…` constant, add it to `UsedCatalogTypes`, write a loader, add it to `CompatibilityChecker` (component, `Features` entry, validation) and give a page its `DataComponents`.

### 5.7 Moved client folder or renamed files

* **Users**: *Locate…* on the Compatibility page, or *Settings → Tibia client → Tibia client and data locations → Change…*, points the program at any file or folder of the client without a code change (7.0, `18_Client_Locations.md`).
* **New file or folder of the client**: add a `LocationDefinition` to `LocationResolver.All` (its parent, component, file filter and checks in `QuickCheck`/`DeepCheck`) and its automatic path in `Automatic`; Settings lists it, and the self-check reports a chosen one.
* **Code**: change `TibiaFormat.PackagesFolder`, `LivePackage`, `AssetsFolder`, `SoundsFolder`, `PackageManifest`, `AssetCatalogFile`.

If CipSoft stops embedding content hashes in file names, integrity checks are skipped automatically: no hash, nothing to compare.

## 6. Item, creature, outfit and mount definitions

There is **no hand-maintained list** of items, creatures, outfits, mounts, effects or missiles. Everything comes from the client:

* `appearances`: objects (items), outfits (including mounts), effects, missiles;
* `staticdata`: creatures (monsters), bestiary classes, achievements, houses, bosses, quests;
* `staticmapdata`: house maps;
* `map`: areas;
* `sounds`.

A Tibia update that adds content needs **no code change**. New items, outfits and mounts appear when the new client data are loaded.

What *is* maintained by hand, all in `GameTables.cs`:

| Table | When to update |
|---|---|
| `MarketCategoryNames` (0–27) | When the client adds a market category. Until then it is shown by its enum name or number. Add the enum value to `Protos/shared.proto` (`ITEM_CATEGORY`) and the name at the same index. |
| `Vocations`, `WeaponTypes`, `ClothesSlotNames`, `PlayerActions`, `LoopTypes` | When the matching enums in `shared.proto` gain values. Update the proto first. |
| `OutfitPaletteHex` (133 colors), `DefaultOutfitColors` | Only if Tibia's outfit color palette changes. The outfit parity tests then fail on the pinned client by design: produce new references (§4) or document the deliberate change. |
| `AutomapColor` (216-color web-safe formula) | Only if minimap colors change |

NPC trade lists take item **names** from TibiaWiki (§7). Names that the wiki does not know yet appear in the export's log.

## 7. Repairing web integrations

All addresses: `Core/Net/ExternalEndpoints.cs`. All requests: `Core/Net/HttpFetcher.cs`. Failures are logged (§9) and shown in plain words in the status bar or a dialog.

### 7.1 TibiaWiki item IDs (Item Catalog, Generate all NPC trades)

The "Item IDs" page is not a table someone maintains: it is a query (a DPL table) over the item articles. Its wikitext, checked 2026-10-15, lists the articles in *Objects with Object IDs* that are also in *Objects*, not in *Deprecated* or *Lists*, in title order, with the `itemid` of their *Infobox Object*. Building that page takes the wiki longer than its 16 s limit since 2026-10-15 ("503 Backend fetch failed" on every request), so 7.0.1 runs the same query itself through the API.

**Order of sources** (`WikiItemIdTable.GetAsync`):

1. a cache under 24 h old (`<data>\cache\wiki-item-ids.json`);
2. the item articles through the MediaWiki API (`WikiItemIdTable.FromArticlesAsync`): the category's size (`ExternalEndpoints.ItemArticlesCountUrl`), then its articles with their wikitext, 50 per request (`ExternalEndpoints.ItemArticlesUrl`, following the API's `continue`); 188 requests and about a minute on 2026-10-15, with progress. Each request: 45 s timeout, 3 attempts about 5 and 10 s apart;
3. the page through the API, `ExternalEndpoints.ItemIdsApiUrl`: 120 s timeout, 3 attempts 20 and 40 s apart;
4. the `action=render` page;
5. the last good cache, with a notice naming its date;
6. otherwise a clear error naming every address that failed.

When TibiaWiki does not answer at all (no connection, unknown host, timeouts), steps 3–4 are skipped: they are on the same site. A URL entered in *Item IDs page URL* (Appearances → Tools) replaces steps 2–4; the default value means "the API".

| Symptom (log / message) | Cause | Repair |
|---|---|---|
| "item articles: … is empty or no longer exists" or "item articles yielded only N IDs" | The wiki changed how it lists or describes items (the category, the infobox or its `itemid`) | Read the "Item IDs" page's wikitext (`api.php?action=query&prop=revisions&titles=Item_IDs&rvprop=content&rvslots=main`), then follow its query in `ExternalEndpoints` (the category constants) and `WikiItemIdTable.FromArticles`. Meanwhile the page itself (step 3) is used if the wiki can build it. |
| "answered 404" / "missingtitle" error in the API JSON | The page was renamed | Update `ExternalEndpoints.ItemIdsPageTitle`, or use a custom URL in the UI meanwhile |
| "refused automated access (it requires a browser check)" | Cloudflare challenge for programs; seen on 2026-10-12 for `action=render` | **Do not work around it.** The API is the supported route. If the API is challenged too, the list cannot be fetched automatically: the last cached copy keeps working (with a notice). Contact the TibiaWiki administrators; the User-Agent names the tool and its version. |
| "Item IDs page: … answered 503" | The wiki gives up building the page after 16 s ("503 Backend fetch failed"); on 2026-10-12 a cold build took about 40 s, since 2026-10-15 it fails every time | Nothing to do while the articles (step 2) work: the page is only their fallback. |
| "item articles: … answered 503" / timeouts | Wiki overloaded | Retry later. Cached data are used meanwhile. |
| "yielded only N IDs (at least 1000 expected); its layout may have changed" | The table structure changed | Capture the page (below), adjust `WikiItemIdTable.Parse`, then add the capture as a fixture test |
| Many lines in the export log about irregular rows | New cell formats, e.g. new range notation | Same as above. The parser keeps going and only logs. |

**Comparing the articles with the page** (not part of routine tests): `TF_LIVE_WIKI=1` runs `LiveServiceChecks`; `TheItemArticlesGiveTheTableOfTheItemIdsPage` compares the table from the articles with the live page, or with a saved API answer for it (`TF_WIKI_PAGE_JSON=<file>`) while the wiki cannot build the page, and writes the comparison to `TF_WIKI_COMPARE_REPORT=<file>`. `tools/verification/tibiawiki_import_check` repeats the import on a published build.

**Capturing the live page as a fixture** (not part of routine tests; it needs the wiki to be able to build the page):

```
set TF_LIVE_WIKI=1
set TF_SAVE_WIKI_HTML=%TEMP%\item_ids.html
dotnet test --filter FullyQualifiedName~LiveServiceChecks
```

Cut a representative excerpt into `src/TibiaFoundry.Tests/Fixtures/Wiki/`, keeping the CC BY-SA attribution comment at the top as in `item_ids_excerpt.html`. Then add or adjust tests in `NetworkTests`.

### 7.2 Test-server asset downloads (Advanced Tools → Download list)

* The base address is an editable field; its default is `ExternalEndpoints.TestServerAssetsBase`.
* Each downloaded file is checked against the SHA-256 in its name before it replaces anything.
* A "checksum" failure means:
  * the server sent something else (error page, outdated file), or
  * the naming convention changed. Compare with `10_…` §3, "Content-hash naming".

### 7.3 tibiamaps.io links and About links

Only the links are affected: update `ExternalEndpoints`.

## 8. Tests that must pass

```
cd src
dotnet build TibiaFoundry.slnx -c Debug          # 0 warnings, 0 errors (analyzers treat issues as warnings)
set TF_FULL_SPRITES=1
dotnet test TibiaFoundry.Tests\TibiaFoundry.Tests.csproj
```

**Expected**: all tests pass. The opt-in live check and the reference comparison are skipped, and so are the full-sprite comparison and the full client conversion if `TF_FULL_SPRITES` is not set. See `07_Testing_Report.md` for the counts of the current release.

| Group | Guards |
|---|---|
| `SpriteDecodingTests`, `OutfitRenderingTests`, `GifExportTests`, `ExportParityTests`, `MapAndHouseParityTests`, `CompareAndFilterParityTests` | Output parity with the original program, on the pinned client |
| `RobustnessTests` | Truncated, oversized, malformed and fuzzed sheets, bitmaps and catalogs; path safety; atomic writes |
| `CompatibilityTests` | The compatibility check, schema drift, damaged and changed files in a client copy, client updates while running, sprite folder provenance, settings from newer versions |
| `NetworkTests` | Retries, timeouts, rate limits, size limits, downloads, wiki parser and fallbacks. All mocked; no network. |
| `ViewModelTests`, `UiFlowTests`, `ThemeResourceTests`, `SettingsTests`, `AppInfrastructureTests`, `AppLogTests` | Pages and flows, UI resources, settings, portable and installed modes |
| `FileOpsTests` (5.0) | Path guard, existing-file policy, validation before replacing, protected inputs, backups and restore, temporary folders, link handling |
| `ClientStoreTests`, `ClientIntegrationTests`, `ClientsPageTests` (7.3) | The client store: discovery of the file set (RCC by header, never personal folders), deduplication across versions and servers, version independence, change detection, interrupted imports and cleanups, portability, store moves, exports, the readers on an imported version, the launcher and the reminder, the Clients page; `TF_CLIENT_STORE_MEASURE=1` (with `TF_CLIENT_STORE_REPORT=<file>`) scans and imports the installed clients into a temporary store (they are only read) |
| `ClientInventoryTests`, `ClientArchiveTests`, `ClientMonitorTests`, `ClientStartupTests`, `ClientSwitchTests` (8.0) | The complete inventory (every file, personal data left out visibly, consistency), the folder inspector, version evidence and Find installation; shared and independent archives, completeness labels, leases, accounting, interruptions, the Rebuilder (never overwrites, verifies before publishing); baselines and the check at every start (Full and Quick, the launcher, Ask/Later/Don't ask, Automatic, Ask quietly); first-run setup, the start-up choice and recovery; every page after a client switch. Opt-in, read only: `TF_CLIENT_ARCHIVE_MEASURE=1` (inventory of the installed clients), `TF_LAUNCH_CHECK_MEASURE=1` (the launch check on them), `TF_OWNER_STORE_COPY=<copy of a TibiaFoundry-Data folder>` (labels of a real 7.3 archive; only ever a copy) |
| `RccAndCompressionTests`, `LegacyAndConverterTests`, `AssetsEditorTests` (5.0) | The installed client's RCC file, byte-identical appearances/catalog/sheet round trips, conversion and its validation, editor saves and their refusals |
| `ScriptTests`, `ToolPageTests`, `SettingsCategoryTests` (5.0) | The 12 scripts (preview, apply, undo, refusals), the new pages through their view-models, settings categories and migration |
| `ReferenceConversionTests` (5.0, opt-in) | Agreement with a Tibia.dat/Tibia.spr pair made by another converter (`TF_REFERENCE_LEGACY`) |
| `LocationTests` (7.0) | Client locations: automatic, chosen, inside a chosen folder, unavailable choices, checks, partial compatibility checks, Locate on the Compatibility page, settings format 5 |
| `ItemCatalogTests`, `ThirdPartyItemDataAndComparisonTests` (7.0) | Item catalog model, storage and its log, imports and previews, exports and re-imports, `items.otb`/`items.xml`, comparisons, decisions, XLSX |

**Environment variables**:

* `TF_FULL_SPRITES=1`: compare all 223,694 sprites with the original and convert the whole client (about 2 min).
* `TF_REFERENCE_LEGACY=<folder>`: compare a conversion with the `Tibia.dat`/`Tibia.spr` in that folder (read only).
* `TF_CLIENT_DIR=<package folder>`: use another client. Parity tests then skip with the reason.
* `TF_REFERENCE_DIR`: a reference folder elsewhere.
* `TF_LIVE_WIKI=1`: the live TibiaWiki check, run by hand only.
* `TF_SAVE_WIKI_HTML=<file>`: save the page during that check.
* 7.0: `TF_OTB_ITEMS_DIR`/`TF_CLIENTID_ITEMS_DIR=<folder>` (your own copies of third-party item data; `TF_CATALOG_REPORT_DIR` saves the comparison workbook), `TF_CATALOG_REPORT=<file>` with `TF_LIVE_WIKI=1` (catalog coverage).

**Also run before a release**:

* the portable and folder editions' `--self-test`, which must exit with 0;
* `--ui-snapshot <folder>`: all window checks must PASS, with 0 binding errors (`06_Build_and_Setup.md`);
* the reverse checks: `python -I tools/verification/mutation_checks.py src report.txt [IDs…]` (about 60 minutes for all 126; the 7.0 group is `H01`–`H12`, `H31`, 7.0.1 `H37`–`H42`, 7.1 `H50`, `H52`, 7.2 `H54`–`H62`, 7.3 `H78`–`H95`, 8.0 `H96`–`H111`; the IDs of removed features are retired, see the script's header). Each line must say DETECTED. The script changes source files one at a time and restores them byte for byte: **do not edit, build or publish from `src` while it runs**;
* the search for traces of removed features: `python -I tools/verification/removed_feature_search.py . <report>` from `Reconstruction`; every match must be explained (8.0: Item Discovery);
* the search for the removed names over every file, including the release zips: `python -I tools/verification/obsolete_names_search.py . <report>` from `Reconstruction` (about 6 minutes). Its last section, *not explained*, must be empty (`13_Branding_and_Attribution.md` §10).

**If UI tests are skipped with "Environment: Windows refused to create the hidden window…"**, the Windows session's window-class table is exhausted. That is not a program fault: sign out or restart Windows and rerun.

## 9. Logs and diagnostics

| What | Installed edition | Portable edition |
|---|---|---|
| Log files (daily; by default 14 days, ≤ 20 MB/day) | `%LOCALAPPDATA%\TibiaFoundry\logs\TibiaFoundry-<yyyyMMdd>.log` | `<exe folder>\TibiaFoundry-Data\logs\` |
| Settings | `%APPDATA%\TibiaFoundry\settings.json` | `TibiaFoundry-Data\settings.json` |
| Wiki cache | `%LOCALAPPDATA%\TibiaFoundry\cache\` | `TibiaFoundry-Data\cache\` |
| Decoded sound cache | `%TEMP%\TibiaFoundry\sounds\` | same |
| Backups (5.0) | `%LOCALAPPDATA%\TibiaFoundry\backups\` | `TibiaFoundry-Data\backups\` |
| Temporary files (5.0) | `%TEMP%\TibiaFoundry\` | `TibiaFoundry-Data\temp\` |
| Script history (5.0) | `%APPDATA%\TibiaFoundry\script-history.json` | `TibiaFoundry-Data\script-history.json` |
| Client store (7.3) | `%LOCALAPPDATA%\TibiaFoundry\Clients\` (or Settings → Tibia client → *Client store*, moved with *Move store…*) | `TibiaFoundry-Data\Clients\` (always; paths inside are relative) |

* The log records:
  * every compatibility check (version, overall status, each component that is not compatible);
  * every client update detected, failed request (with attempt number), failed file and batch failure.
* The status-bar log button opens the folder.
* **Compatibility → Copy report** produces a text report for bug reports.
* `--self-test <folder>` writes `self-test.txt` (and sample outputs) without opening a window.

### 5.8 New appearance fields and the Assets Editor (5.0)

The Assets Editor edits a second copy of the appearances schema, `Protos/Editing/appearances_editable.proto`, in proto2 form: enums as `int32`, strings as `bytes` (the client stores Latin-1), every field with presence. This is what makes saving byte-identical. When §5.1 adds a field to `appearances.proto`, add it to the editable schema with the **same number** and the proto2 type. Unknown fields are kept unchanged in any case, so an older schema never loses data, but it cannot show or edit the new field. Then run `AssetsEditorTests.ClientAppearancesRoundTripByteForByte`.

### 5.9 Legacy formats (Client Converter)

A new legacy target needs a profile in `LegacyFormat.cs` (signatures, layout, sprite-ID width, frame groups) and, if its attribute bytes differ, a table in `LegacyAttributes.cs`. Before a profile is offered for writing, its output should be loaded in that client. The Client 11 and 10.98 outputs of 5.0 were validated by reading them back, not in a real client (`08_Known_Limitations_and_Uncertainties.md` L21); 8.60 stays identify-only.

## 9a. Tools, themes, folders and diagnostics (6.0)

* **Adding a tool**: `16_Adding_a_Tool.md` (manifest, view-model, view, one line in `BuiltInTools.Pages`). Changing a tool's stored settings: increase `SettingsVersion` and add a `ToolSettingsMigration`; never reuse a key with a new meaning.
* **Adding a theme color**: add the brush to both `Themes/Colors.Light.xaml` and `Colors.Dark.xaml` with the value the element has today, add a `ThemeToken` (group, sources, derivation) to `Core/Theming/ThemeTokens.cs` after its sources, and use it through `DynamicResource` in the styles. `ThemeTests.BothPalettesDefineExactlyTheThemeTokens` and the readability tests must pass.
* **Adding a kind of output**: add an `OutputKind` and its `OutputKindInfo` (subfolder, description) in `Core/FileOps/OutputLocations.cs`; it appears in Settings → Files and folders and the self-check automatically. Use `Services.Outputs` to save.
* **Logs** are parsed by `LogReader`; if the line format changes, keep the parser able to read the older format (it reads 5.0 lines).
* **Diagnostics reports**: anything new added to a report must go through `DiagnosticsSanitizer` (`Sanitize` for text, `SanitizeJson` for JSON) and be listed in `DiagnosticsReport.Categories`; `DiagnosticsTests.TheDiagnosticsReportContainsWhatThePreviewShowedAndNothingPersonal` checks the result.
* **Earlier product names**: do not reintroduce them; `BrandingTests.TheRemovedNamesAppearOnlyWhereTheyMustRemain` searches the whole repository (`13_Branding_and_Attribution.md` §10).

## 9b. Item catalogs and locations (7.0)

* **Catalog format**: `ItemCatalogStore.CurrentFormat` (1). A catalog written by a newer format is not opened. Add fields to records as optional properties (null when unknown); never change the meaning of an existing one. A new identifier type needs an `IdType` value and its description in `ItemNames.Describe`; identifiers of different types must never be compared without an `IdMapping`.
* **New import format**: an `ItemImporters` method returning an `ItemImport` (with problems by line), wired into *From a file…*; always say what the identifiers mean, and add a re-import test.
* **After a Tibia update, the client archive** (8.0): run `TF_CLIENT_ARCHIVE_MEASURE=1` and `TF_LAUNCH_CHECK_MEASURE=1` (both read only). The installed clients must scan consistently, and the inspector must still call them complete installations. If the launcher adds a folder of personal data, add it to `ClientScanner.PersonalFolders` and to the reasons in `ClientInventory` (`21_Client_Archiving_and_Rebuilder.md` §2).
* **After a Tibia update, the client store** (7.3): the file set of a version comes from the client's `assets.json` (its `files` list: `localfile`, and `packedhash`/`packedsize` when `"unpack": false`, else `unpackedhash`/`unpackedsize`) and the RCC files from `package.json`. If the launcher changes these files, `ClientScanner` falls back to the catalogs and says so in the import report; check with `TF_CLIENT_STORE_MEASURE=1` (read only) that the installed clients still scan without problems (`20_Client_Management.md` §3).
* **A copy of the client's options file left by Item Discovery** (removed in 8.0): a 7.0–7.3 session interrupted before restoring left the exact original in `%LOCALAPPDATA%\TibiaFoundry\discovery\<date-time>\clientoptions.original.json` (portable: `TibiaFoundry-Data\discovery`). 8.0 reports it at start-up and in Diagnostics (`LeftoverHotkeyBackups`) and never touches it. To restore by hand, close the client and copy it over `conf\clientoptions.json`; `session.json` → `originalSha256` is its SHA-256.

## 10. Recovering from a failed update or migration

| Situation | Recovery |
|---|---|
| The client updated while the program was open | Automatic: on the next page switch or window activation, data are unloaded and reloaded, and the status bar says so. If files were still being written, wait and click *Check again*. |
| The check says *damaged (checksum)* | The client files are incomplete. Let the **Tibia launcher** verify or repair them; never edit them. |
| A new client is *Not supported* for one file | Only the listed features are affected; use the others. Fall back to the **test-server client** (*Use the test server* in the sidebar) if it is still on a supported version, or wait for a Tibia Foundry update (§5). |
| Corrupt settings | Automatic: the file is kept as `settings.json.corrupt-<timestamp>` and defaults are used. Copy values back from it if needed. |
| Settings written by a newer Tibia Foundry | Automatic: a backup `settings.json.v<N>.bak` is written and unknown options are kept, so going back to the newer version loses nothing. |
| Going back to an earlier version | Release zips are self-contained (`dist/`; earlier releases are in `06_baseline_2.0.0/`, `07_baseline_2.1.0/` and `08_baseline_3.0.0/`). Settings are compatible both ways, and an earlier version still finds its own settings because migration never changes them. Keep the newer version's zip to return. |
| Sprites folder from an older client | The program ignores it and says so. Re-extract with *Extract Sprites*, which writes a fresh `TibiaFoundry-sprites.json`, or switch the sprite source to *client only* in Settings. |
| Wrong or outdated wiki data | Delete `cache\wiki-item-ids.json` (§9) and run the export again |
| Sounds play stale audio | Delete `%TEMP%\TibiaFoundry\sounds` (it is rebuilt on demand) |
| An export or download was interrupted | Nothing is half-written: outputs are replaced only after a complete write. Run it again. |
| The program does not start after an update | Run `TibiaFoundry.exe --self-test report` and read `self-test.txt` and the log. The portable edition can be tried from a fresh folder without touching settings. |
| A tool replaced or removed the wrong files (5.0) | Utilities → Scripts → **Backup and Restore** → *List backups*: every replaced file and every file a script moved away is in a backup set; restore the selected files. Renames are undone with the record in `backups\rename-records`. |
| A saved assets folder or RCC file does not work | The previous version of a file saved in place is in the backup folder. *Save as* never touches the source. The installed client is never written to. |
| The client's hotkeys still show an Item Discovery batch (7.0–7.3; the tool was removed in 8.0) | Close the client and copy the reported `clientoptions.original.json` over `conf\clientoptions.json` (§9b). |
| An item catalog reports an incomplete last record (7.0) | Nothing to do: the record that was being written when the program stopped is skipped; every complete record is kept. |

## 11. Making a release

1. Set the version in `src/Directory.Build.props` (`Version`, `FileVersion`, `InformationalVersion`) and the folder in `src/TibiaFoundry.App/Properties/PublishProfiles/Portable.pubxml` (`PublishDir`).
2. Update `TibiaFormat.ValidatedClientVersions` for the versions you validated.
3. Run the checks of §8.
4. Publish:

   ```
   dotnet publish TibiaFoundry.App/TibiaFoundry.App.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o ../dist/TibiaFoundry-<version>-win-x64
   dotnet publish TibiaFoundry.App -p:PublishProfile=Portable
   ```

5. Copy `tools/release/README-Portable.txt` into the portable folder (update its version line and *New in* section first). Make `THIRD-PARTY-NOTICES.txt` and copy it into both folders:

   ```
   python -I tools/release/make_notices.py src THIRD-PARTY-NOTICES.txt dist/TibiaFoundry-<version>-win-x64/TibiaFoundry.deps.json
   ```
6. Run `--self-test` and `--ui-snapshot` with **both** published executables (portable: from a fresh, empty folder).
7. Zip both folders, flat, and write `dist/SHA256SUMS.txt`.
8. Update `CHANGELOG.md` and `07_Testing_Report.md`.
