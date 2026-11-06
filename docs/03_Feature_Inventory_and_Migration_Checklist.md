# Feature Inventory and Migration Checklist

> **Note (Tibia Foundry 6.0):** this is a historical record of the original program (version 1.8.0.26867) and the 2.0 reconstruction. The program was later renamed Tibia Forge (version 3.0) and then **Tibia Foundry** (version 4.0). Paths and figures below are those of the earlier version; since 6.0 the earlier product names are written as placeholders (`<original>` for the original program's file names, `<v2>` for the identifier of version 2.x, see `13_Branding_and_Attribution.md` §10). See `07_Testing_Report.md` for current results.

This table maps every user-facing feature of the original (control names from `02_analysis/original_ui_tree.txt`) to its place in 2.0.

**Status**

| Mark | Meaning |
|---|---|
| ✅ | Migrated |
| 🔧 | Migrated and fixed (bug ID in `04_Bug_Report.md`) |
| ➕ | Unfinished or unwired in 1.x, implemented in 2.0 |
| ♻ | Hidden in 1.x, restored |
| 🔁 | Deliberately changed |
| ⛔ | Not implementable |
| ✖ | Not ported (dead code) |

**Verification**

| Code | Meaning |
|---|---|
| **P** | Parity test: output compared with the original program's output. |
| **F** | Executed by a view-model flow test. |
| **W** | Executed by the scripted window checks (`--ui-snapshot`). |
| **S** | The control renders in the UI snapshots of both themes, but the action was not executed by automation. |
| **K** | Core logic is executed by a test; the button-to-core wiring was not executed. |

Test names refer to the test project in `src/` (named `<v2>.Tests` in 2.0).

## Global

| Original | 2.0 location | Status | Verified |
|---|---|---|---|
| Tabs at the top of a fixed 780×557 window | Navigation rail (grouped, collapses to icons) + page header | 🔁 | W, S |
| *About* button (credits message box) | About page: credits, version, runtime, libraries, links | 🔁 | S |
| *Switch to TS* (`dat_btSwitchTS`) | *Test server* toggle in the navigation rail; package name in Settings | ✅ | W (`switch to test server`) |
| *Save Settings after this session* (`Dat_cbSaveSettings`) | Settings → Maintenance → *Save settings changes made in this session* | ✅ | F (`SettingsAreNotWrittenWhenSavingIsOffForTheSession`) |
| `settings.json` next to the EXE | `%APPDATA%\<v2>\settings.json`; a 1.x file is imported automatically | 🔧 B15 | F (`SettingsTests`) |
| – | Light, Dark and System themes, live switch (F6), persisted | new | W |
| – | Status bar with progress, Cancel (Esc) and a log link | new | F (`RunAsync…` tests) |

## Appearances Viewer

| Original control | 2.0 location | Status | Verified |
|---|---|---|---|
| *Load* / *Change file* / file box (`Dat_LoadDAT`, `Dat_SelDAT`) | Appearances header | ✅ | F |
| *Select Sprites Folder* (`Dat_SelSpriteFolder`) | *Sprites folder…* (header) and Settings → Sprites | 🔧 B05 | F (`Repository_FallsBackToClientWhenFileMissing`) |
| *Extract on the fly* (`dat_ExtractFly`) | Settings → Sprites → *Save sheets decoded…* | ✅ | K |
| *Export as JSON* (`Dat_ExportJSON`) | Header → *Export JSON…* | ✅ | P (`AppearancesJson_MatchesOriginal`), F |
| Objects / Outfits / Effects / Missiles tabs | Kind selector with counts | ✅ | F, S |
| Search box + *Search* (`Dat_SearchName`) | Search mode *Go to ID* / *Name contains* | ✅ | F |
| Search by sprite (`Dat_SearchSprite`) | Search mode *Uses sprite* | 🔧 B28 | K |
| Name / SpriteIDs boxes | Detail header (name, ID, sprite IDs with copy) | ✅ | F |
| Preview picture, click to change background (`dat_SpritePic`) | Preview, click cycles 5 backgrounds | ✅ | S |
| *Animate* (`Dat_Animate`), *ForceDelay* | Preview options | ✅ | F (preview frames) |
| *View All* (`Dat_ViewAllSprites`) → ViewAllSprites window | *View all sprites* → gallery window (columns, background, zoom, save) | ✅ + new | S |
| Data tab (property table) | Data tab, with property filter, *Show unset values* and *Copy all* | 🔧 B29, B08 | F |
| Filters: Include/Exclude, AND/OR | Filters tab → mode | 🔧 B27 | P (`Filters_MatchOriginal`, 50 scenarios) |
| Filters: Basic Flags (37 check boxes) | Flags list (generated from the schema, searchable) | ✅ | P |
| Filters: Adv. Flags 1 (vocation, proficiency, imbueable + slots, description, classification, write_once, currency, defaultAction, light, shift, bank + comparison, write, hook south/east, height, cloth, market category, automap, lenshelp) | Filters → Advanced | 🔧 B07, B24, B25 | P |
| Filters: Adv. Flags 2 (weapon type, min level + comparison) | Filters → Advanced | 🔧 B37 | P |
| Filters: FrameGroup (# sprites, isOpaque, layers, width/height 32/64, pattern depth/width/height, animation, synced, min/max duration, loop type, frame groups) | Filters → Frame group | 🔧 B26 | P |
| Filters: *DefaultStartPhase*, *RandomStartPhase* | Not offered; the panel explains why | ⛔ B45 | – |
| Filters: IDs (≥, ≤, list) + *Insert list* dialog | Filters → IDs | ✅ | P |
| *Apply Filters* / *Clear Filters* | Apply / Clear / Uncheck all | 🔧 B37, B38 | P, F |
| *Keep Selected* (`Dat_FiltersKeep`, disabled, empty handler) | *Keep selected* under the list | ➕ B40 | K |
| Export → *JSON* / *Table (File)* / *Table (Clipboard)* (filtered) | Export tab → Filtered data | ✅ | P (`FilteredExports_MatchOriginal`) |
| Export → *Save as GIF* with Movement, Facing, Delays (avg/min/max), no-animation delay, extra frame and delay, author, auto-name, output folder | Export tab → GIF | 🔧 B03, B19, B20 | P (`Exports_MatchOriginalGifs`, 15 GIFs) |
| Export → *Pattern Stage* (disabled) | Export tab → Pattern (enabled) | ➕ B44 | K |
| Export → *Generate Sheet* (PNG File / PNG Clipboard / Base64, MagentaBG) | Export tab → Outfiter sheet | 🔧 B16 | F (`OutfitSheetPngFileMode_WritesChosenFile`) |
| Tools → Currencies | Tools tab → Currencies | ✅ | S |
| Tools → NPC trade list (name, sort by name/ID, *Create (LUA)*) | Tools tab → NPC trade list | ✅ | P (`NpcTradeLists_MatchOriginal`, 30 NPCs) |
| Tools → *Generate Lua Data for all NPCs* | Tools tab | 🔧 B13 | K |
| Tools → *Compare DAT files* | Tools tab → *Open Compare files* (and the Compare page in the rail) | 🔁 | F (`ToolsCompareButtonOpensComparePage…`) |
| Outfitter: colour part H/P/S/D, palette, *Default Colors*, outfit/mount colour target | Outfitter tab | 🔧 B30 | F, P (`RenderedFrames_MatchOriginal`, 51 frames) |
| Outfitter: facing N/E/S/W, Moving/Idle, Mounted/Walking, addons, mount ID | Outfitter tab | ✅ | F, P |
| Outfitter: *Save as GIF* | Outfitter tab | ✅ | F |

## Static Data

| Original control | 2.0 location | Status | Verified |
|---|---|---|---|
| *Load* / *Change file* / *Export as JSON* | Header | ✅ | F, P (`OtherDataJson_MatchesOriginal`) |
| Tabs Monsters / Bosses / Achievements / Houses / Quests | Kind selector | ✅ | S |
| Name filter (`Monsters_FilterName`), ID search (`Monsters_SearchID`) | Filter box (name or ID) | ✅ | F |
| Sprite-ID *Filter* (`button5`, no handler) | *Sprite ID* filter box | ➕ B43 | K |
| *Clear Filters* (`Monsters_ClearFilters`) | Clear button next to the filters | ✅ | F |
| Visible count | "n of m" under the list | ✅ | S |
| Render complete outfits (thumbnails) | *Coloured thumbnails* | ✅ | S |
| Preview, *Animate*, *ForceDelay* + delay | Preview card | 🔧 B12 | F |
| *Outfitter* (`Monsters_Goto_Outfitter`) | *Open in Outfitter* | ✅ | K |
| *Export GIF* (`Monsters_Export_Gif`) | *Export GIF…* | ✅ | F (`StaticDataCreatureGif`) |
| *Select Sprites Folder* | Settings → Sprites (shared) | 🔁 | – |

## Map Tools

| Original control | 2.0 location | Status | Verified |
|---|---|---|---|
| *Load* / *Change file* / *Export as JSON* | Header | ✅ | P |
| Areas / NPCs / Files lists + Data tab | List + Data tab (coordinates link to tibiamaps.io) | 🔧 B23 | P (`AreasJson…`, `NpcJsonAndTable…`) |
| Extract Map Files (input, output, subareas/minimap/satellites) | *Extract map files* tab | 🔧 B22 | P (`ExtractedMapTiles_MatchOriginal`, 1,157 tiles) |
| *Crop Cyclopedia to minimap* | *Extract map files* tab | 🔧 B22 | K |
| Build HR Map (input, type, zoom, top floor, cumulative, floor offset, watermark) | *Build HR map* tab | 🔧 B21 | P (`HighResMaps_MatchOriginal`) |
| *Generate NoCoords / Coords – Labels List* | *Export data* tab | ✅ | P (`WikiLists_MatchOriginal`) |
| JSON / Table export (areas, NPCs) | *Export data* tab | 🔧 B33 | P |
| *Generate Wiki Area/Subarea List* (no handler) | *Export data* tab | ➕ B41 | K |

## House Images

| Original control | 2.0 location | Status | Verified |
|---|---|---|---|
| *Load* / *Export as JSON* | Header | ✅ | P |
| *Change file* (no handler) | Header | ➕ B42 | K |
| House list, floor, view all floors, crop, background | List + toolbar (and *Fit to window*) | 🔧 B01, B02 | P (`HouseImages_MatchOriginal`, 12 houses; `HousesThatCrashedTheOriginal_Render`) |
| *Copy* / *Save* | Toolbar | ✅ | K |

## Sounds

| Original control | 2.0 location | Status | Verified |
|---|---|---|---|
| *Load* / *Change file* / *Export as JSON* | Header | ✅ | F, P |
| Tabs Sounds / Sound Effects / Ambience / Ambience Objects / MusicTemplate | Kind selector | 🔧 B09 | F (`SoundsListFollowsKindAndFilter`) |
| *PLAY* / *STOP* | Player row (+ volume, autoplay, play buttons in the data list) | ✅ + new | K (audio output not asserted) |
| *Select Sounds Folder* / Export → *Extract*, *Change* | Extract tab | ✅ | P (`ExtractedSounds_MatchOriginal`, 844 files) |

## Extract Sprites

| Original control | 2.0 location | Status | Verified |
|---|---|---|---|
| Assets path *Change*, statistics | Header | 🔧 B32 | P (`CatalogSummary…`), F |
| Sheet list, `>>`, `<<`, *Add all*, *Remove all* | Lists + buttons (double-click, Delete key, *Add shown*) | ✅ | F (`SpriteExtractorQueue`) |
| *Add 32x32 / 32x64 / 64x32 / 64x64* | *Add by size* | ✅ | F |
| *Type ID* (ExtractByID dialog) | *Add by ID…* | ✅ | F |
| Individual sprites / sheets, .png / .gif, output *Change*, *Run* | Output panel → *Extract* (+ one folder per sheet, original names, skip existing) | 🔧 B17, B18 | P (`AllSprites_MatchOriginalExtraction`, 223,694 sprites; `Extractor_WritesOriginalLayout`) |

## Compare DAT files (separate window in 1.x)

| Original | 2.0 location | Status | Verified |
|---|---|---|---|
| Old/new file pickers, *Ignore sprites*, *Compare* | Compare Files page | 🔧 B11, B36 | P (`AppearanceComparison_MatchesOriginal`), F |
| *Compare Folders* | Compare Files page | 🔧 B10 | K |
| Category / entry lists, change text, double-click to open | Compare Files page (+ removed entries, *Save report*) | ✅ + new | S |

## Hidden tabs (removed at start-up in 1.x)

| Original | 2.0 location | Status | Verified |
|---|---|---|---|
| *Export Sheets* (files / list / JSON / appearances, outfit and mount presets, MagentaBG, *Create*) | Advanced Tools → Outfiter sheet | ♻ B46 | K |
| *Sprites from PNG* (select sheets, output, *RUN!*) | Advanced Tools → Slice sheet | ♻ B46 | K |
| *LZMA Decode* (select files, download file list) | Advanced Tools → LZMA | ♻ B46 | P (`DecodedSheet_MatchesOriginalLzmaOutput`) |

## Not ported

| Original | Reason |
|---|---|
| `GifEncoder` class | Dead code, never referenced (B47). |
| `clientbg`, `pbbg` resources | Never referenced by the original's code (B47). `pbbg1`, the House Images preview background, is replaced by the theme's checkerboard. |
| ClickOnce deployment | Replaced by a self-contained folder or zip (no installer or update channel). |

## Migration checklist

- [x] All original files preserved unchanged (SHA-256 verified; `02_analysis/original_file_hashes.csv`).
- [x] Every visible and hidden feature mapped above. 2 filters are not implementable (documented), and 2 dead-code items are not ported.
- [x] All output formats reproduced; parity tests against the original's outputs pass.
- [x] Settings from 1.x imported automatically.
- [x] Light, dark and system themes, live switching and persistence (W).
- [x] Resizable and maximisable window with a minimum size; placement persisted (W).
- [x] Background work with progress and cancellation; errors logged and reported.
- [x] Release build published and smoke-tested (`dist/`, published-build snapshot report).
- [ ] Verification at DPI scales other than 100 % and on other machines (not executed; see Testing Report).
