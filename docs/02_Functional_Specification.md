# Functional Specification – the original program (1.8.0.26867)

> **Note (Tibia Foundry 6.0):** this is a historical record of the original program (version 1.8.0.26867) and the 2.0 reconstruction. The program was later renamed Tibia Forge (version 3.0) and then **Tibia Foundry** (version 4.0). Paths and figures below are those of the earlier version; since 6.0 the earlier product names are written as placeholders (`<original>` for the original program's file names, `<v2>` for the identifier of version 2.x, see `13_Branding_and_Attribution.md` §10). See `07_Testing_Report.md` for current results.

This document describes what the program does. It covers the original 1.8.0.26867 and the rebuilt 2.0.0. Every statement carries a confidence label.

| Label | Meaning |
|---|---|
| **[V]** Verified | The original binary was run in the parity harness, and the rebuild produces the same output (byte-, pixel- or line-identical unless stated). An automated test checks it. |
| **[C]** Confirmed by code | Read from the decompiled original; the code path is unambiguous. Reimplemented, but not executed against the original. |
| **[I]** Inferred | Purpose or intent deduced from names, UI text or context, not from execution. |
| **[U]** Uncertain | Could not be determined; see section 6. |

## 1. Purpose and users

The program is a desktop utility for editors of **TibiaWiki** (tibia.fandom.com) [I]. It reads the data files of the installed **Tibia** game client and turns them into material for the wiki: [C]

* sprites and animated GIFs;
* item and creature data;
* map images and area lists;
* house images;
* sounds;
* NPC trade tables.

The program is read-only towards the client: it never modifies client files [C].

## 2. Inputs

| Input | Location (default) | Used by |
|---|---|---|
| Client package | `%LOCALAPPDATA%\Tibia\packages\Tibia` (live). The test-server package defaults to `TibiaExternal` [C]. | everything |
| `catalog-content.json` | `<package>\assets` | file discovery; sprite sheet index [V] |
| `appearances-<hash>.dat` (protobuf) | assets | Appearances, Outfitter, Static Data, House Images [V] |
| `staticdata-<hash>.dat` | assets | Static Data, house names [V] |
| `staticmapdata-<hash>.dat` | assets | House Images [V] |
| `map-<hash>.dat` + `*.bmp.lzma` tiles | assets | Map Tools [V] |
| `sprites-<first>-<last>.bmp.lzma` (5,084 sheets on this machine) | assets | all sprite output [V] |
| `sounds-<hash>.dat` + `.ogg`/`.wav` files | `<package>\sounds` | Sounds [V] |
| Extracted sprites folder (`<id>.png`) | user choice (1.x: required) | sprites [C] |
| TibiaWiki "Item IDs" page | `https://tibia.fandom.com/wiki/Item%20IDs?action=render` | NPC trade module (wiki names) [C]. Since 2.1 read through the MediaWiki API; since 7.0.1 from the item articles the page lists (docs/11 §7.1) |

Data formats:

* **CIP LZMA container** [V]:
  * zero padding, then the marker `70 0A`;
  * a varint with the decompressed size, then a varint with the compressed size (= file length − 32);
  * 5 LZMA property bytes and 8 size bytes;
  * the raw LZMA payload.

  Without the marker, the original scans for `5D 00 00 00 02`.
* **Decoded sheets** [V]: 384×384 BITMAPV4 images, 32 bpp, with BGRA bitfields.
* **Sprite sizes by sheet type** [V]: 0 = 32×32, 1 = 32 wide × 64 tall, 2 = 64×32, 3 = 64×64.
* **Sprite numbering** [V]: sprites are numbered row by row from the sheet's first ID.

## 3. Features

### 3.1 Client package and sprites

* **Switch to TS / Live client.** Toggles between the live and the test-server package and reloads the data from that package [C]. In 2.0 this is the navigation-rail toggle.
* **Sprite source** [C]:
  * 1.x reads `<id>.png` from an extracted folder only, and crashes when a file is missing.
  * 2.0 reads the folder first, then decodes the sheet from the client. "Folder only" and "Client only" modes are also available.
* **Extract on the fly** [C]: writes each decoded sheet's sprites into the folder. In 2.0 this is *Save sheets decoded from the client into the extracted folder*.

### 3.2 Appearances viewer

* **Lists.** Four lists: Objects, Outfits, Effects and Missiles, each with ID, name and a preview sprite [V].
  * The preview sprite comes from the first frame group [C]:
    * objects use its first sprite;
    * outfits use sprite `layers × 2`, the south-facing base;
    * effects and missiles use the third sprite.
  * 2.0 shows thumbnails lazily in virtualised lists.
* **Search** [C]:
  * *Go to ID*, from an ID list: selects the matching rows.
  * *Name contains*: at least 2 characters.
  * *Uses sprite*: 1.x searched the first frame group only; 2.0 searches all frame groups.
* **Data panel.** Every protobuf field of the appearance, with dotted labels (`Flags.Light.Brightness`) [C].
  * Default values are hidden unless *Show unset values* is ticked.
  * The automap colour is shown as a swatch, using the formula in `GameTables.AutomapColor` [C].
  * Market categories, clothes slots, vocations, actions and loop types are shown by name [C].
  * NPC sale entries are shown as `name/location/sale/buy[/currency]` [V].
  * 2.0 adds a property filter and *Copy* for each value and for all values.
* **Preview.** Animates the first frame group with the sprite delays [C]. *Force delay* replaces all delays with a fixed value [C].
  * Clicking the preview cycles its background: checkerboard (2.0), magenta, black, white, grey [C].
* **View all sprites.** Every sprite of the appearance in a grid [C]:
  * 2, 3, 4 or 8 columns; the default is 2 for effects, 3 for missiles and 4 otherwise;
  * background Default, Magenta, White or Black.

  2.0 adds zoom, copy/save of single sprites, *Save all* and *Copy IDs*.
* **Filters.** Criteria are combined in four modes: Include AND, Include OR, Exclude AND, Exclude OR [V].
  * Every boolean and presence flag of the appearance schema [V].
  * Advanced 1:
    * restricted to vocation, proficiency, imbueable (with slot counts), description text, upgrade classification;
    * write once, currency, default action, light (`-1` = any), shift/elevation;
    * bank ground speed (=, ≥, ≤), write, hook (south/east), height, cloth slot, market category, automap colour (`-1` = any), lens help [V].
  * Advanced 2: weapon type, minimum level (=, ≥, ≤) [V].
  * Frame group (index chosen per filter) [V]:
    * number of sprites, opaque, layers, bounding-box width/height (≤ 32 / > 32);
    * pattern width/height/depth, animated, synchronised, loop type;
    * minimum and maximum phase duration, number of frame groups.
  * IDs: ≥, ≤, list [V].
  * *Keep Selected* (unfinished in 1.x; implemented in 2.0) narrows the list to the selected rows.
  * The 1.x start-phase filters cannot work with current data (see B45 in the bug report).
* **Export filtered data** [V]:
  * JSON of the filtered appearances;
  * a tab-separated `ID\tName` table, to a file or the clipboard.
* **Export GIF.** For the selected appearances [V]:
  * Movement: Idle or Moving.
  * Facing: N, E, S or W.
  * Pattern stage: all patterns or one; this control is disabled in 1.x and enabled in 2.0.
  * Delay mode: Average, Minimum or Maximum of each phase's min/max.
  * No-animation delay (default 1000 ms); optional empty extra frame; extra or first-frame delay.
  * Author, which goes into the GIF comment.
  * Automatic file names: object name, `<id>_<Idle|Moving>_<Direction>` for outfits, or the ID [C].

  GIF encoding [V]:
  * ImageMagick, delay = ms / 10, disposal = Background, infinite loop;
  * comment `Created by <author> using <original> for Tibia.Fandom.com` (without "by <author>" when the author is empty).
* **Export outfiter sheet** [V]. The outfit's frames are laid out for the TibiaWiki outfitter, with an optional magenta background. Output goes to:
  * a PNG file (broken in 1.x, see B16);
  * the clipboard as PNG;
  * the clipboard as a Base64 data URI.
* **Tools** [V]:
  * the list of currencies;
  * the NPC trade list of one NPC in Lua format, sorted by object name or ID;
  * *Generate Lua data for all NPCs*: optionally downloads the wiki item ID table to use wiki names.
* **Outfitter** [V]. Renders the selected outfit with:
  * four colour parts (head, primary, secondary, detail) from the 133-colour palette, default colours 78/69/58/76;
  * addons 1 and 2, idle/moving, walking/mounted;
  * mount ID (default 1599) and separate mount colours;
  * facing, default colours, and *Save as GIF*.

  Colouring uses the template sprite (yellow head, red primary, green secondary, blue detail) and ImageMagick *Multiply* compositing. 2.0 reproduces it exactly, including 16-bit quantisation, as pixel-identical frames [V].
* **Export as JSON.** The whole appearances file as indented JSON with the original property names [V].

### 3.3 Static Data

* Lists of Monsters, Bosses, Achievements, Houses and Quests [V].
* Filter by name or ID, and by sprite ID (unfinished in 1.x) [C].
* Optional coloured outfit thumbnails (2.0) [C].
* The creature preview animates the creature's outfit with its colours and addons, or its object appearance [C].
* *Open in Outfitter* takes the outfit, colours and addons there; *Export GIF* saves the animation [C].
* Export as JSON [V].

### 3.4 Map Tools

* **Data.** Areas (with subareas, parent, label coordinate and NPCs), NPCs (with position and area), and map files [V].
  * Label coordinates link to tibiamaps.io [C].
* **Extract map files.** Satellite, minimap and subarea tiles are decoded to PNG named `<type>_<zoom or areaId>_<x>_<y>_<z>.png` [V]. In the test, 1,157 tiles were pixel-identical.
* **Crop Cyclopedia maps to minimap** [C].
* **Build HR map** [V]:
  * satellite or minimap; zoom 25, 50 or 100 %; top floor 0 to +7;
  * cumulative floors, floor offset, and an optional TibiaWiki watermark.
* **Export data** [V]:
  * the *Coords – Labels* list (`x, y, z = Name`) and the *NoCoords – Labels* list (`? = Name`);
  * areas and NPCs as JSON or a tab-separated table;
  * the *Wiki Area/Subarea list* (unwired in 1.x, implemented in 2.0) [C].
* Export the map data as JSON [V].

### 3.5 House Images

* Renders a house from `staticmapdata`, using appearances and sprites [V]:
  * one floor, or all floors up to the chosen one;
  * optional crop;
  * background: transparent, grey, magenta, black or white.
* House names come from Static Data [C]. Copy to the clipboard or save as PNG [V].
* *Change file* (unwired in 1.x) and Export JSON [V].

### 3.6 Sounds

* Lists of sounds, sound effects, ambiences, ambience objects and music templates, with their data [V].
* Play and Stop. 2.0 adds a volume control, autoplay, and play buttons on sound-ID fields [C].
* *Extract* copies every sound to `<sound id>.<ext>` in the chosen folder [V]. In the test, 844 files were byte-identical.
* Export JSON [V].

### 3.7 Extract Sprites

* The catalog summary: sheets, sprites, highest ID, and the newest sheet of each size [V].
* A queue of sheets built from the list, by size, *Add all*, or by sprite ID (*Type ID*) [C].
* Exports individual sprites or whole sheets, as PNG or GIF (GIF ignored in 1.x) [V]:
  * optionally one folder per sheet (`Sheet <first>-<last>`);
  * whole sheets named `Sprites <first>-<last>.png` or with the client's file name.
* 2.0 adds *Skip existing files*, progress and cancellation.

### 3.8 Compare files (1.x "Compare DAT files" window)

* Compares two versions of appearances, map or staticdata files. 2.0 adds staticmapdata [V].
* The result is grouped by category with changed and new entries [V]. 2.0 also lists removed entries.
* Change details are shown in the original text format [V]:
  * `Key <name>: <old> !=  <new>`, with the original double space;
  * `Key <name> is not an object in target`.

  *Ignore sprite ID changes* is available.
* *Compare folders* runs the comparison for every matching file in two folders [C].
* Double-clicking an appearance opens it in the viewer [C]. 2.0 adds *Save report*.

### 3.9 Hidden tools (1.x tabs removed at start-up; 2.0 "Advanced Tools")

* **Export Sheets.** Builds an outfiter-style grid sheet from image files, a sprite-ID list, a legacy JSON or a loaded outfit, with column and cell presets (outfits 8×64, mounts 4×64) and an optional magenta background [C].
* **Sprites from PNG.** Slices a sheet into cells, skipping empty cells [C].
* **LZMA Decode.** Decodes CIP LZMA files, or downloads a list of asset files from a base URL [C].

### 3.10 About

Shows the original credits, the version and, in 2.0, the runtime and libraries [C].

### 3.11 Settings and persistence

* **1.x** [C]:
  * `settings.json` next to the EXE, written on exit if *Save Settings after this session* is ticked;
  * fields: OutputPath, AllSpritesPath, AllSoundsPath, AllSpritesPathTS, TSName, Author, GifDir.
* **2.0**:
  * `%APPDATA%\<v2>\settings.json`, written atomically on every change;
  * a 1.x file next to the EXE is imported on the first start;
  * the session-only *Save settings changes made in this session* switch is preserved;
  * stores theme, window placement, last page, folders, sprite source and GIF options;
  * no secrets [V, tested].

## 4. Rebuild-only behaviour (2.0)

* **Window**: resizable and maximisable, with a minimum size of 860×560, Per-Monitor-V2 DPI awareness, saved placement, and a navigation rail that collapses to icons below 1,100 px.
* **Themes**: System, Light and Dark, switched live (F6 toggles) and persisted. The title bar follows the theme.
* **Interaction**:
  * every long operation is asynchronous, with progress, Esc/Cancel, and a single error dialog plus a log entry when it fails;
  * empty states, placeholders and tooltips throughout.
* **Diagnostics**:
  * daily log files under `%LOCALAPPDATA%\<v2>\logs`, kept 14 days and capped at 20 MB per day;
  * a crash-loop guard.
* **Automation**: `--ui-snapshot <folder> [--theme light|dark] [--size WxH]` renders every page and runs the window checks.

## 5. Deliberate output differences from 1.x

All are bug fixes listed in `04_Bug_Report.md`. The important ones:

* fully transparent GIF frames stay transparent (B19);
* Latin-1 names are decoded properly (B31);
* the areas export has no duplicated subarea names and has parent areas filled (B23);
* the *Bank* filter works (B24);
* the sheet count is correct (B32);
* the areas-table header typo is fixed (B33).

## 6. Uncertain or environment-dependent items

| Item | Status |
|---|---|
| ClickOnce auto-update of 1.x | [U] The deployment manifest enables install, but no update URL was exercised. 2.0 ships as a plain folder or zip. |
| TibiaWiki "Item IDs" page layout | [U] The NPC trade module parses the rendered table. A future layout change on the wiki would break name mapping; the module then falls back to client names. |
| Test-server package name | [I] `TibiaExternal` is the default and can be changed in Settings. |
| Start-phase data | [U] Older or future client files may contain `SpriteAnimation` fields 1 and 3. The original's schema omits them, so they cannot be filtered (B45). |
| Map world origin | [V for the current client] The rebuild computes bounds from all tiles, so other clients with different extents are supported [C]. |
| Behaviour at DPI scales other than 100 % | [I] The app is Per-Monitor-V2 aware and all layout is vector-based. Only 100 % was executed (see Testing Report). |
