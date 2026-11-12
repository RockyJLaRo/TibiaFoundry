# Tibia Compatibility Report – Tibia Foundry 7.0

This report records which parts of Tibia Foundry depend on the Tibia client and on websites, how the program detects that something changed, and what happens when it does. It describes facts observed on 2026-10-12 and, for the tools added in 5.0, on 2026-10-13. In 7.0 (2026-10-14) every file the check looks at can be located by hand (`18_Client_Locations.md`), the check of one component can run on its own after a location changed. The client's options file and the action-button tooltips were dependencies of Item Discovery from 7.0 to 7.3; it was removed in 8.0, so they are no longer read. It makes **no prediction** about future Tibia versions. Nothing here guarantees that a future update will not break a feature. The goal is that a break is detected, contained to the affected features, explained, and quick to repair (see `11_Maintenance_Guide.md`).

## 1. Status on 2026-10-12

| Client package | Version | Result of the compatibility check | Validation |
|---|---|---|---|
| `Tibia` (live) | 15.33.b21348 | **Supported**: all 7 components | Full test suite, including all 223,694 sprites against the original program's output; outfit, GIF, map, house and export parity |
| `TibiaExternal` (test server) | 15.25.0e418e | **Supported**: all 7 components | Compatibility check, UI package switch and appearance loading. The parity suite runs against the live client only. |
| `Tibia` (live), 2026-10-14 | 15.33.9d148f | **Supported** (validated in 7.0) | Its data files are byte-identical to 15.33.b21348 (only `package.json` changed); the full test suite passed against it (`05_verification/7.0.0/client_15.33.9d148f/validation.txt`). |
| `Tibia` (live), 2026-10-20 | 15.33.c9e45e | **Supported** (validated in 8.0) | Every asset, data and RCC file is byte-identical to 15.33.9d148f; only `package.json` and `package.json.version` changed (compared by SHA-256 between the two revisions archived by 7.3; `05_verification/8.0.0/client_15.33.c9e45e/validation.txt`). |

Both reports are in `05_verification/4.0.0/compatibility_*.txt` (2.1: `05_verification/2.1.0/`). In 5.0 the live client's report is part of each self-test (`05_verification/5.0.0/self_test/`, again *Supported*), and the whole test suite also ran against the test-server client (295 passed, 0 failed).

**Expected findings, not problems**:

* Two catalog types are present but **not used**: `fullmap` and `proficiencies`.
* `staticmapdata` contains two **known unmapped fields** (`Tile` 101 and 102, 2-byte values). The original 1.8.0.26867 ignored them too.

## 2. How compatibility is detected

The check runs in the background at start-up, after a client update is detected, and on demand. Results go to the **Compatibility** page, to banners on the affected pages, and into `--self-test`. On this PC it takes 3–5 s.

1. **Version**: `package.json` → `"version"`.
   * A version listed in `TibiaFormat.ValidatedClientVersions` is *validated*.
   * Any other version is checked file by file, not refused.
2. **Sprite catalog** (`catalog-content.json`):
   * parsed tolerantly; every entry it cannot interpret is listed as a problem;
   * sheet files are checked for existence;
   * **one sheet of every sprite type is decoded**, proving the container, compression, bitmap format and grid.
3. **Each data file** (`appearances`, `staticdata`, `staticmapdata`, `map`, `sounds`):
   1. **Integrity**: the SHA-256 of the content must equal the hash in the file name. A mismatch means damaged, or still being written by the launcher.
   2. **Schema drift**: `SchemaInspector` walks the protobuf wire format against the compiled schema. It reports fields this version does not know (new information) and known fields whose wire type changed (a format change). Google.Protobuf itself skips both silently.
   3. **Load**: failures are classified as *missing*, *unreadable* or *damaged / changed format* (`DataFileException`).
   4. **Record validation**:
      * *appearances*: IDs, frame groups, sprite info, sprite counts, catalog coverage;
      * *staticdata*: creature outfits, house IDs;
      * *staticmapdata*: house tiles;
      * *map*: areas and map tile files;
      * *sounds*: file names and presence.
4. **Client changes while running**: the fingerprints of `package.json` and the catalog are compared on page navigation and window activation. A change unloads all data, re-reads the paths, informs the user and re-runs the check.

### Statuses

| Status | Rule | Effect |
|---|---|---|
| **Supported** | Validated version and all checks passed | Normal operation |
| **Unfamiliar but compatible** | All checks passed, but the version is not validated or the file contains unknown fields | Normal operation. Unknown fields are ignored (as the original did); the report lists them. |
| **Partially compatible** | Some records or catalog entries failed validation, fields changed type, or sheet files are missing | Pages work with the valid records. Affected pages show a banner; invalid records are flagged or skipped. |
| **Missing** | The file is not there | Only the features that need it are unavailable, with an explanation |
| **Not supported** | The file cannot be read, is damaged (checksum), sheets cannot be decoded, or required content is missing | Features that need the file show a plain-language error naming the file. All other features keep working. |

The overall status is the worst component status. A component only affects the features listed for it (§3, "Features").

## 3. Dependency map: the client

| Dependency | Where it is defined | Features | If it changes |
|---|---|---|---|
| **Install location**: `%LOCALAPPDATA%\Tibia\packages\<Tibia\|TibiaExternal>`, `assets\`, `sounds\` | `TibiaFormat.PackagesFolder`, `LivePackage`, `DefaultTestPackage`, `AssetsFolder`, `SoundsFolder`; `TibiaClientPaths`; 7.0: `LocationResolver` | Everything | Reported as **Missing**, with *Locate…* on the Compatibility page. Every file and folder can be pointed to a new place without a code change (7.0; before 7.0 only the assets folder). |
| **Client version**: `package.json` → `"version"` | `TibiaFormat.PackageManifest`; `TibiaClientPaths.ReadClientVersion` | Version display, validation status, update detection | "Version unknown"; the data files are still checked individually |
| **Asset index**: `catalog-content.json`, a JSON array of `{type, file, spritetype, firstspriteid, lastspriteid, area}` | `TibiaFormat.AssetCatalogFile`, `CatalogType*`, `UsedCatalogTypes`; `AssetCatalog` | All sprite features; locating every data file | Unknown entry types are listed as *unused*. Invalid entries are skipped and listed (**Partial**). A non-array root gives **Not supported** with a clear message. |
| **Sprite sheets**: `sprites-<sha256 of decompressed BMP>.bmp.lzma` | `CipLzma`, `LzmaDecoder`, `BmpDecoder`, `SpriteSheetDecoder`; `TibiaFormat.SpriteTypes`, `SheetSize`, `LzmaMarker*` | Previews, Extract Sprites, GIFs, Outfitter, House Images | **Container**: CIP header `70 0A`, two varints, 5 LZMA property bytes, 8 bytes, raw LZMA1 payload. A different compression gives **Not supported** for the sample sheet, so sprite features are marked affected.<br>**Bitmap**: 32-bit with bitfield masks, 384×384. Another size is accepted if the grid fits, with a notice.<br>**Sprite types**: 0 = 32×32, 1 = 32×64, 2 = 64×32, 3 = 64×64. An unknown type's sheets are skipped and listed (**Partial**).<br>**Order**: row-major from `firstspriteid`, as the original. Changing the order cannot be detected automatically. |
| **Appearances**: `appearances-<sha>.dat`, protobuf `tibia.protobuf.appearances` | `Protos/appearances.proto`, `shared.proto`; `AppearanceDatabase`, `AppearanceProperties`, `OutfitRenderer`, `AppearanceGifBuilder` | Appearances viewer, filters, exports, GIFs, Outfitter, Static Data previews, House Images, NPC trade lists, Compare | New fields → **Unfamiliar**: listed and ignored. Retyped fields → **Partial**. Structural problems, such as counts that do not match the sprite list, are flagged per record, and renderers refuse those records clearly. |
| **Game tables** (not in the data files) | `Appearances/GameTables.cs`:<br>• market categories 0–27;<br>• vocations, weapon types, clothes slots, player actions, loop types;<br>• outfit color palette;<br>• the 216-color automap palette | Data panel names, filters, outfit colors | A new enum value is shown by its number or enum name instead of a label: cosmetic. A changed palette **cannot be detected**; outfit colors would be wrong. Only the outfit parity tests reveal that. |
| **Static data**: `staticdata-<sha>.dat` | `Protos/staticdata_adapted.proto`; `StaticDataDatabase` | Static Data (creatures, bestiary, achievements, houses, bosses, quests), house names | As for appearances |
| **House maps**: `staticmapdata-<sha>.dat` | `Protos/staticmapdata.proto`; `StaticMapDatabase`, `HouseRenderer`; `TibiaFormat.KnownSchemaDrift` | House Images | As for appearances. `Tile` fields 101 and 102 are known and ignored. |
| **World map**: `map-<sha>.dat` plus tiles `minimap-…-<sha>.bmp.lzma`, `satellite-…-<sha>.bmp.lzma`, `subarea-…-<sha>.bmp.lzma` | `Protos/map.proto`; `MapDatabase`, `MapImageTools` | Map Tools: data, tile extraction, HR maps, wiki area lists | Missing tile files and unsafe names are reported. Extracted tiles are named `<type>_<zoom>_<x>_<y>_<z>.png`, the format Build HR reads. |
| **Sounds**: `sounds-<sha>.dat` plus `sounds\sound-<sha>.ogg` (or `.ogg.lzma`) | `Protos/sounds.proto`, `sounds-common.proto`; `SoundDatabase`; NVorbis and NAudio | Sounds: lists, playback, extraction | A missing file is a failure for that sound. A new audio codec makes playback fail for that sound with an error. |
| **Cyclopedia export**: `floor-0Z-map.png`, 256-px tiles, origin x 31744, y 30976 | `TibiaFormat.CyclopediaFloorFile`, `CyclopediaOriginX/Y`; `MapImageTools.CropCyclopediaMaps` | Map Tools: "Crop Cyclopedia to minimap" | Missing floors are reported. **A moved origin cannot be detected**: tiles would get wrong coordinates. |
| **Content-hash naming**: `<type>-<sha256>.<ext>`; for `.lzma`, the hash of the decompressed data | `CompatibilityChecker.ContentHashFromName`, `MatchesHashInName`; `TibiaFormat.ContentHashLength` | Integrity checks, download verification | Verified on all 19,078 hash-named files of both clients. If CipSoft drops the convention, the checks are skipped (no hash means nothing to compare), not failed. |
| **Extracted sprite folders** (made by this program or by 1.x): `<id>.png` plus `TibiaFoundry-sprites.json` | `SpriteFolderManifest`, `SpriteRepository` | "Folder, then client" sprite mode | A folder from another client version is ignored, with a notice. A folder without a manifest is used, but sprites of the wrong size are replaced from the client. |
| **Editing the assets (5.0)**: the appearances file re-written, sheets re-encoded (BMP + CIP LZMA), the catalog re-written | `Protos/Editing/appearances_editable.proto` (proto2 copy of the schema), `AssetsProject`, `BmpEncoder`, `LzmaEncoder`, `CatalogWriter` | Assets Editor | Fields the editable schema does not know are **kept unchanged** (but cannot be edited). Unchanged objects, sheets and the catalog are written byte for byte as the client has them (verified with 15.33 and 15.25). A changed sheet container or bitmap layout would make saving refuse, because every saved file is read back and checked. See `11_Maintenance_Guide.md` §5.8. |
| **`bin\graphics_resources.rcc` (5.0)**: Qt resource format 3, zlib | `Core/Rcc` | RCC Editor | Formats 1–3 are read; a newer format is refused with a message. Zstandard entries are kept but not decompressed. |
| **Legacy targets (5.0)**: 10.98 and Client 11 layouts | `Core/Legacy` | Client Converter | The output formats do not change with Tibia updates. Known modern information without a legacy equivalent is counted as "not stored" in the result; fields the schema does not know are not converted. |

## 4. Dependency map: websites

All addresses are in `Core/Net/ExternalEndpoints.cs`, and all requests go through `Core/Net/HttpFetcher.cs`:

* GET only;
* per-request timeouts;
* bounded retries for transient errors;
* `Retry-After` honoured up to 30 s;
* a size cap;
* atomic writes.

| Site / interface | Used for | Detection and behavior on change | Observed (2026-10-12 unless dated) |
|---|---|---|---|
| **TibiaWiki MediaWiki API, item articles** (7.0.1): `api.php?action=query&generator=categorymembers&gcmtitle=Category:Objects with Object IDs&prop=revisions\|categories&rvprop=content…`, 50 articles per request | The same table, built from the articles the "Item IDs" page lists (Item Catalog, *Generate all NPC trades*) | Read first. The page's query is repeated: articles also in *Objects*, not in *Deprecated* or *Lists*, title order, the *Infobox Object*'s `itemid`. An empty or missing category, or under 1,000 IDs, is reported and the page is asked instead. | **Works** (2026-10-15): 188 requests, about a minute; 13,875 IDs, identical to the page as downloaded on 2026-10-14. |
| **TibiaWiki MediaWiki API, page**: `api.php?action=parse&page=Item_IDs&prop=text&format=json&formatversion=2` | Item name → ID table for *Generate all NPC trades* (the fallback since 7.0.1) | **Order of sources**:<br>• a cache under 24 h old;<br>• the item articles (row above);<br>• this page (120 s timeout, 3 attempts 20 and 40 s apart);<br>• the render URL;<br>• the last good cache, with a notice naming its date;<br>• otherwise a clear error naming every address that failed.<br>When the site does not answer at all, the addresses after the first are not tried.<br>**Parsing**: by table structure (link cell = name, number cell = IDs or ranges). Results under 1,000 IDs are rejected as "layout changed", never used partially. | Worked on 2026-10-12 and 2026-10-14: 13,875 IDs; a cold parse took about 40 s, a cached one 1.5 s. **Fails since 2026-10-15**: "503 Backend fetch failed" after 16 s on every request (the wiki's limit for building a page). |
| **TibiaWiki render URL**: `wiki/Item%20IDs?action=render` | Fallback for the same table; what 1.x used | Any HTTP error is reported. A Cloudflare challenge is reported as "refused automated access" and not retried. | **Blocked for programs**: 403 with `cf-mitigated: challenge`. The program does not attempt to get around it. |
| **Test-server assets**: `https://static.test.tibia.com/launcher/assets-test/assets/` | *Advanced Tools → Download list*: user-supplied list of asset names | Each file is checked against the SHA-256 in its name before it replaces anything. Path entries cannot leave the output folder. | Not contacted during this audit (manual tool; the address comes from the original program) |
| **tibiamaps.io**: `map#x,y,z:0` | Clickable coordinates in data panels and area lists | Only the link breaks; nothing is downloaded | – |
| **TibiaWiki conventions** (output formats) | NPC trade Lua module, ID/name tables, area lists, GIF naming | **Not detectable by the program.** If the wiki changes its templates or module format, the generated text needs a code change (`Export/`, `Wiki/`). | – |
| About-page links: the creator profile (TibiaWiki, Tibia character, YouTube, Instagram, GitHub, Reddit, TibiaWiki) and the Credits (tibia.com, cipsoft.com, the TibiaWiki main page) | Opened in the browser; only the addresses listed on the page can be opened | Only the link breaks | Addresses checked by tests (4.0); the creator links were opened in 3.0 |

## 5. What cannot be detected automatically

Changes that keep the same wire format and file structure but change **meaning** pass every check:

* A protobuf field reused for different data (same number and type).
* An enum value whose meaning changes.
* Sprites reordered inside sheets.
* A changed outfit palette or automap palette.
* A moved Cyclopedia origin.
* A wiki page whose table keeps its shape but changes column meaning.

The parity tests against the original's outputs, and a look at the program's output after an update, are the only protection. `11_Maintenance_Guide.md` §3 lists what to compare after an update.

## 6. Isolation: what keeps working when a file fails

| Failed component | Unavailable | Still available |
|---|---|---|
| Sprite catalog / sheets | Sprite images everywhere: previews, GIFs, Outfitter, House Images, Extract Sprites | All data views (Appearances, Static Data, Map data, Sounds), exports of data, Compare, Map Tools tile extraction |
| appearances | Appearances page, GIFs, Outfitter, NPC trades, Static Data previews, House Images rendering | Static Data lists, Map Tools, Sounds, Extract Sprites, Compare of other files |
| staticdata | Static Data page, house names | Everything else; House Images shows "House N" |
| staticmapdata | House Images | Everything else |
| map | Map Tools data and extraction | Everything else, including Cyclopedia cropping (it uses client PNG exports) |
| sounds | Sounds page | Everything else |
| TibiaWiki | Fresh NPC trade lists, if no cache exists | Everything else; with a cache, NPC trades work and the page says which copy was used |

This isolation is tested by `CompatibilityTests.DamagedAndChangedFilesAreIsolatedToTheirFeatures`. That test works on a copy of the real client:

* appearances: one byte changed (→ *Not supported*, checksum);
* staticmapdata: removed (→ *Missing*);
* catalog: an entry with an unknown sprite type (→ *Partial*);
* map: replaced by garbage (→ *Not supported*).

It checks that staticdata is still reported as compatible.
