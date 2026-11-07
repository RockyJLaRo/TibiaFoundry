# Tibia Foundry: website content brief

Everything a site for Tibia Foundry (tibiafoundry.com or a GitHub Pages site) needs: what it is, every feature, requirements, safety and privacy, history, credits, and what must not be claimed. Facts are as of version 8.0.0 (2026-10-20).

> **8.7.0 (2026-10-31):** the page in `website/site/` describes 8.7.0 (see `website/README.md`): a toolkit for fansite administrators, wiki editors, artists and content creators; 21 pages grouped by task with a Home dashboard; GIF export presets, pixel-exact enlargement and kind folders. Current version 8.7.0.

> **8.6.0 (2026-10-25):** the published page was updated for 8.6.0 in `website/site/` (see `website/README.md`): File Metadata, the Default Live/Test Client, the redesigned setup dialogs, What's new, Help. Facts below that differ: current version 8.6.0; 20 pages (File Metadata added under Tools); the GitHub repository holds the website only, and no release has been published there yet.

---

## 1. Identity

| Item | Value |
|---|---|
| Name | **Tibia Foundry** (identifier `TibiaFoundry`, program `TibiaFoundry.exe`) |
| Tagline (exact wording) | **"Tibia Foundry - All your Tibia-Tools for TibiaWiki"** ("TibiaWiki" links to https://tibia.fandom.com/wiki/Main_Page) |
| One-line description | Tibia Foundry extracts sprites, images, sounds and game data from the installed Tibia client and prepares them for TibiaWiki. |
| Creator | **RockyLaRo**, TibiaWiki Admin |
| Current version | **8.0.0**, released 2026-10-20 |
| Platform | Windows desktop app (.NET 10, WPF), x64 |
| Price / license of the program | Free fan project (no license for the program itself has been chosen yet; see §12) |
| Logo | 32×32 pixel-art logo (`src/TibiaFoundry.App/Assets/Branding/tibia-foundry-logo.png`), an 8× enlargement `tibia-foundry-logo-256.png`, and `tibia-foundry.ico`. Scale only with nearest-neighbor (crisp pixels). It was supplied as the Tibia Forge logo and contains no text. |
| Copyright line | Tibia Foundry © 2026 RockyLaRo (TibiaWiki Admin). Tibia game graphics © CipSoft GmbH. |

**Creator links** (shown on the About page, reuse them on the site):

* TibiaWiki Profile: https://tibia.fandom.com/wiki/User:RockyLaRo
* Tibia Character "Roc ky": https://www.tibia.com/community/?name=Roc+ky
* YouTube: https://www.youtube.com/@Roc_ky
* Instagram: https://www.instagram.com/rocky_tibia/
* GitHub: https://github.com/RockyJLaRo
* Reddit: https://www.reddit.com/user/Roc-ky/

---

## 2. Elevator pitch

Tibia Foundry reads the files of your installed Tibia client and turns them into ready-to-upload material for TibiaWiki: sprites and animated GIFs, outfit and house images, high-resolution maps, sounds, and item, creature, area and NPC data (including NPC trade tables in Lua). It also edits and converts client files (Assets Editor, RCC Editor, Client Converter), keeps a catalog of item names and IDs from every source, stores every Tibia client version you import with shared, deduplicated files, and never changes your Tibia installation.

Short hooks for a hero section:

* Every sprite, outfit, house, map and sound of your Tibia client, ready for TibiaWiki.
* Pixel-perfect: 223,694 sprites checked against the original program, pixel for pixel.
* Your client stays untouched: Tibia Foundry only reads it.
* Every image you export carries your name in its metadata.
* Local only: no account, no telemetry, nothing uploaded.

---

## 3. Requirements and download

* **Windows 10 (version 1809 or later) or Windows 11, 64-bit (x64).** Not 32-bit; ARM64 untested.
* **An installed Tibia client** (live and/or test server). Default package folder: `%LOCALAPPDATA%\Tibia\packages\Tibia`; the test server's `TibiaExternal`. Any client folder can also be chosen by hand.
* **No .NET installation needed** (self-contained).
* No installer and no auto-update: download a zip, unzip, run.

**Two editions** (same features):

| Edition | File | Size (zip) | Where it keeps its data |
|---|---|---|---|
| **Portable** | `TibiaFoundry-8.0.0-portable-win-x64.zip`, one `TibiaFoundry.exe` | 80.0 MB | `TibiaFoundry-Data` next to the exe (settings, logs, backups, client archive). Runs from a USB stick; move the folder and everything moves with it. |
| **Folder** | `TibiaFoundry-8.0.0-win-x64.zip` | 77.3 MB | Settings in your user profile (`%APPDATA%\TibiaFoundry`), client archive in `%LOCALAPPDATA%\TibiaFoundry\Clients` (movable). |

Both zips include `THIRD-PARTY-NOTICES.txt`, and the portable one `README-Portable.txt`. SHA-256 checksums are published in `SHA256SUMS.txt` (put them on the download page).

**Where exports go:** `Documents\Tibia_Foundry\<kind>` (follows a Documents folder moved to OneDrive or another drive). Subfolders: `GIF_Exports`, `Image_Exports`, `Sprite_Exports`, `Sound_Exports`, `Map_Exports`, `Data_Exports`, `RCC_Exports`, `Client_Converter`, `Assets_Editor`, `Scripts`, `Reports`, `Manifests`, `Backups`, `Logs`, `Item_Catalogs`. Each can be changed in Settings; folders are created only when something is saved.

**Upgrading:** settings of every earlier version (1.x to 7.2) are upgraded automatically and nothing is lost; for the portable edition, copy the old `TibiaFoundry-Data` folder next to the new exe.

---

## 4. Features, page by page

The app has 19 pages in three groups, plus a live/test-server switch in the navigation bar.

### 4.1 Client Data

**Appearances**: every object, outfit, effect and missile of the client.
* Four lists with ID, name and preview thumbnail; *Go to ID*, *Name contains*, *Uses sprite*.
* Data panel with every field of the appearance (dotted labels, named enums, automap color swatch, NPC sale entries), property filter, copy one or all values.
* Animated preview with the real sprite delays, *Force delay*, switchable background (checkerboard, magenta, black, white, gray).
* *View all sprites* grid (2/3/4/8 columns, zoom, copy/save single sprites, *Save all*, *Copy IDs*).
* Powerful filters: every flag of the schema, combined as Include AND/OR or Exclude AND/OR; vocation, level, weapon type, market category, light, elevation, cloth slot, imbuement slots, frame-group properties (layers, patterns, animation, phase durations), ID ranges and lists; *Keep Selected*.
* **Export GIF**: idle or moving, facing N/E/S/W, all patterns or one, average/minimum/maximum delays, automatic file names (`<id>_<Idle|Moving>_<Direction>` for outfits).
* **Outfiter sheet** for TibiaWiki's outfitter: PNG file, clipboard PNG, or Base64 data URI.
* **Outfitter**: color an outfit with head/primary/secondary/detail colors from the 133-color palette, addons 1 and 2, idle/moving, mounted with its own mount colors; save as GIF. Pixel-identical to the original program's coloring.
* NPC tools: list of currencies; one NPC's trade list in Lua (sorted by name or ID); **Generate Lua data for all NPCs**, optionally with TibiaWiki's item names.
* Export the filtered list as JSON or an ID/name table, or the whole appearances file as JSON.

**Static Data**: monsters, bosses, achievements, houses and quests.
* Filter by name, ID or sprite; colored outfit thumbnails.
* Animated creature preview with colors and addons; *Open in Outfitter*; export GIF; export JSON.

**Item Catalog** (7.0): a database of item names and IDs.
* Sources kept apart: the client's own data (official), TibiaWiki item IDs (community), `[id] name` lists, CSV (also without a header, such as a market tracker's `items.csv`), catalog JSON, third-party `items.xml` item lists and `items.otb` ID mappings (unverified research leads), names read in the client, and your own decisions.
* Identifier types are never mixed (Tibia object IDs, items.otb IDs and the client IDs they map to, legacy Tibia.dat IDs).
* Each name is settled only with evidence (official, chosen by you, sources agree, one source); conflicts are listed for review, never merged silently; every name shows its sources.
* Search by ID, range, exact or partial name; filters by agreement, source, unnamed in the client, differing names.
* **Rename** any item in place (F2 or double-click), kept as your decision; *Restore original name* undoes it.
* **Compare** two sources, two catalogs or the settled names: added, removed, renamed, category and property changes, moved items.otb IDs, IDs claimed twice, behavior-sensitive objects (doors, containers, teleports, depots, beds, floor changes) marked.
* Coverage per source and relative to the client.
* Exports: name list, CSV, records CSV, **Excel workbook (XLSX)** and JSON. Imports are previewed and replaced records backed up.
* TibiaWiki import: 13,875 item IDs in about 40–60 seconds through the wiki's official API, with progress.

**Map Tools**: areas, subareas, NPCs and map files.
* Extract satellite, minimap and subarea tiles to PNG.
* **Build HR map**: satellite or minimap, 25/50/100 % zoom, top floor 0 to +7, cumulative floors, floor offset, optional TibiaWiki watermark (off by default).
* Crop Cyclopedia maps to the minimap.
* Exports: "Coords – Labels" lists, areas and NPCs as JSON or table, the Wiki Area/Subarea list; label coordinates link to tibiamaps.io.

**House Images**: render any house from the client's map data.
* One floor or all floors up to the chosen one, optional crop, background transparent, gray, magenta, black or white.
* Copy to the clipboard or save as PNG; house names from static data.

**Sounds**: sounds, sound effects, ambiences, ambience objects and music templates.
* Play/Stop with volume and autoplay; play buttons on sound-ID fields.
* *Extract* every sound as its original OGG/WAV file; export JSON.

### 4.2 Tools

**Client Installation Rebuilder** (8.0): makes a normal folder from any archived revision: a complete installation is rebuilt, any other revision restored as it was. It writes into a new or empty folder only, checks every file before the folder is used, and saves a report (it never claims the client will run).

**Extract Sprites**: save the client's sprite sheets as individual sprites or whole sheets, PNG or GIF.
* Catalog summary (sheets, sprites, highest ID, newest sheet of each size); queue by list, size, *Add all* or sprite ID.
* One folder per sheet optional; *Skip existing files*; progress and cancel.

**Compare Files**: what changed between two client versions of the appearances, map, staticdata or staticmapdata files.
* Grouped changed, new and removed entries; *Ignore sprite ID changes*; compare whole folders; double-click opens the object; *Save report*.

**Advanced Tools**:
* **Export Sheets**: build an outfitter-style sheet from images, a sprite-ID list or a loaded outfit (presets: outfits 8×64, mounts 4×64).
* **Sprites from PNG**: slice a sheet into cells, skipping empty ones.
* **LZMA Decode**: decode CIP LZMA files, or download a list of asset files from a base URL (verified against the checksum in their names).

**Assets Editor** (5.0): edit the client's objects, outfits, effects and missiles with their sprites.
* Every appearance field (editor over the full schema), frame groups, animation and sprite layout.
* New, duplicate, delete, change ID, revert; copy and paste flags.
* Replace a sprite everywhere or one cell; add sprites; sheet browser with *find sprite* and sheet export.
* Object packages (`.aec`), import from another assets folder, PNG/GIF export with attribution.
* *Save* and *Save as*, both validated; unchanged data stays byte-identical. Never saves into the installed client.

**RCC Editor** (5.0): Qt resource files (formats 1–3), such as the client's `graphics_resources.rcc`.
* Browse, filter, preview; replace (also by drag and drop), add, rename, remove.
* Extract with folders and a manifest; update from an edited folder; create a new file from a folder.
* Save with validation and a backup of the previous file.

**Client Converter** (5.0): modern client assets → legacy `Tibia.dat` + `Tibia.spr` for **Client 11 / Tibia 10.98** (custom signatures possible).
* Options: transparency, empty sprites, market name length, newer market categories, extended attributes for third-party legacy-format tools, 32×32 slices.
* Every lossy step is counted; the written files are read back and checked completely.
* Identifies existing legacy files (8.60, 10.98, Client 11).

**Scripts** (5.0): 12 built-in file utilities. Each previews its changes and asks before applying; nothing is ever deleted (removed files go into a backup set).
1. Duplicate File Finder
2. Directory Comparison
3. Batch File Renamer
4. File Hash and Checksum Calculator
5. File Inventory and Manifest
6. Large File Finder
7. Empty Files and Folders
8. Batch Image Converter and Resizer
9. Archive Contents Inspector
10. File Format and Metadata Inspector
11. Backup and Restore
12. Tibia Project File Auditor

### 4.3 Application

**Clients** (7.3, 8.0): all your Tibia clients in one place, for the live server and the test server.
* **Client folders from anywhere** (8.0): *Find installation* looks in the usual places and checks what it finds; *Browse folder…* takes an installation, a copied client, a rebuilt folder or an assets folder. Each folder is classified (complete installation, standalone client, assets only), shows which tools can use it, and is used where it is.
* **Complete archives** (8.0): every file of an installation, empty folders and file times. Hotkeys, character data, explored map, screenshots and logs are left out unless you include them, and each revision says so ("client content complete — personal data left out").
* **Two storage modes:** space-efficient (each file content once; a later version usually adds a few MB) or independent copies (a full copy per revision).
* **A check at every start** (8.0, in the background, only reading the client): when the launcher changed the installed client and the new version is not archived, a notice offers *Archive*, *Later* or *Don't ask for this version*; or it archives automatically, or only marks the Clients page.
* Compare revisions (changed, added, removed, moved files), rename, verify every stored file by SHA-256, safe removal (content another revision uses is never deleted), reclaim space, move the archive.
* **First-run setup** for new installations, and *ask which client* at start with *Remember my choice*; if the remembered client is gone, a recovery dialog offers its archived revision.

**Compatibility**: can this version read the installed client?
* Checks every component after a Tibia update; reports *Supported*, *Unfamiliar but compatible*, or what is affected, and limits problems to the features that need the affected file.
* *Locate…* any client file or folder by hand (separately for live and test server), *Reset to automatic*.

**Settings**: categories General, Appearance & Themes, Tibia client, Files and folders, File safety, Performance, Attribution, Notifications and logging, Tools, Scripts, Diagnostics, Advanced. Every option applies immediately; each category can be reset; all settings export and import.
* **Appearance & Themes** (6.0): System, Light and Dark themes plus your own themes, with a color editor by group, live preview, readability (WCAG contrast) warnings, theme export/import (`.tftheme.json`) and *Restore original appearance*. F6 toggles light/dark.
* Interface size 80–150 %, compact navigation bar, start page, window placement, preview animation, thumbnail size.
* File safety: when a file exists, Ask / Keep both / Replace / Skip; SHA-256-verified backups with restore and retention.
* Performance: concurrent operations, worker threads, sprite cache.

**Diagnostics** (6.0): running and recent operations with real progress and safe Cancel; process performance; a searchable log; a self-check that repairs only after confirmation; every registered tool with its state; a **privacy-safe diagnostics report** (ZIP, previewed in full, never sent anywhere).

**About**: version, creator profile with six links, the active attribution text, credits (Tibia, CipSoft, TibiaWiki), open-source acknowledgments, open-source components and runtime.

### 4.4 Across the whole app

* **Creator attribution** in every exported PNG and GIF: `Created by <your name> using TibiaFoundry for TibiaWiki @ https://tibia.fandom.com/`, in PNG text chunks and XMP / GIF comment and XMP, plus "Tibia game graphics © CipSoft GmbH" and an edit history. No visible watermark. Re-exports of someone else's images keep their creators unless you choose otherwise. Set your name in *Settings → Attribution* (default: RockyLaRo).
* **"✓ Saved to <file>"** after every export; click the path to open File Explorer with the file selected.
* **Live / test server switch** for every page.
* Resizable window, Per-Monitor DPI aware, keyboard friendly, Esc cancels long operations, every long task runs in the background with progress.
* **Tool registry**: every page and script is described by a manifest (capabilities, settings, dependencies), so new tools plug into the same infrastructure.
* Built-in self-test (`--self-test`, 12 checks) and UI check (`--ui-snapshot`).

---

## 5. Safety and privacy (good for a "Is it safe?" section)

* **Your Tibia installation is only read.** No tool writes into the client folder.
* **Nothing reads or changes the running client**: not its memory, its program or its network traffic.
* **Local only**: no account, no telemetry, no auto-update, no cloud sync. The only network use is TibiaWiki's public API (item IDs, when you ask for it) and the optional asset download in Advanced Tools.
* **Files are safe**: existing files are never overwritten without asking (or your chosen policy); replaced files go to a verified backup; scripts preview before applying and never delete.
* **Diagnostics reports** remove personal folders, user and computer names, e-mail addresses and secrets, and are shown in full before saving.
* No plugins or user scripts are loaded (deliberate, for security).

---

## 6. Quality and numbers (for a "Built carefully" section)

* **223,694 sprites** compared with the original program, pixel for pixel; outputs of the original (GIFs, outfits, JSON, map tiles, sounds) reproduced byte- or pixel-identically.
* **522 automated tests** (511 passed, 0 failed, 11 opt-in skipped) in 8.0.0.
* **126 reverse checks**: known defects and removed safeguards are re-introduced on purpose to prove the tests catch them; 126 of 126 caught.
* **62 defects** of the original program found and fixed (56 in the original, 6 during the rebuild).
* Both editions pass the self-test 12/12 and the UI checks 43/43 with 0 binding errors, in light and dark themes.
* Validated clients: 15.33 (live, up to 15.33.c9e45e) and 15.25 (test server); other versions are checked by the Compatibility page.

---

## 7. Version history (timeline)

| Version | Date | Highlights |
|---|---|---|
| 1.8.0.26867 | (original) | The original TibiaWiki tool this project grew from. |
| 2.0.0 | 2026-10-12 | Full source reconstruction on .NET 10 and WPF; themes, background operations, unfinished features completed, dozens of bugs fixed. |
| 2.1.0 | 2026-10-12 | Stability audit; resilience to Tibia updates (Compatibility page). |
| 3.0.0 | 2026-10-12 | Renamed **Tibia Forge**; creator attribution in exports. |
| 4.0.0 | 2026-10-12 | Renamed **Tibia Foundry**; credits; clearer text. |
| 5.0.0 | 2026-10-13 | Assets Editor, RCC Editor, Client Converter, 12 scripts, file safety, categorized Settings. |
| 6.0.0 | 2026-10-13 | Custom themes, `Documents\Tibia_Foundry` output folder, "✓ Saved to" messages, tool registry, Diagnostics. |
| 7.0.0 | 2026-10-15 | Locate any client file, Item Catalog, third-party item data, Item Discovery (proof of concept). |
| 7.0.1 | 2026-10-15 | TibiaWiki item ID import fixed after a wiki-side outage. |
| 7.1.0 | 2026-10-15 | Item Discovery from client screenshots; headerless CSV import. |
| 7.2.0 | 2026-10-16 | Renaming in the Item Catalog; OBS Game Capture source; automatic hover. |
| 7.3.0 | 2026-10-16 | Clients page and deduplicated client store. |
| 8.0.0 | 2026-10-20 | Complete client archives, client folders from anywhere, Client Installation Rebuilder, a check at every start, first-run setup; Item Discovery removed. |

---

## 8. Credits and legal text (use as written)

**Credits:** [Tibia](http://tibia.com/), [CipSoft](https://www.cipsoft.com/en/), [TibiaWiki](https://tibia.fandom.com/wiki/Main_Page).

**Disclaimer (footer):** Tibia, its game graphics and data are © CipSoft GmbH. Tibia Foundry is an independent fan project and is not affiliated with or endorsed by CipSoft GmbH or Fandom.

**Open-source acknowledgments** (the reference for three tools; Tibia Foundry includes no code from them, and they do not endorse it):
* [Nekiro's RCC Editor](https://github.com/nekiro/Nekiro-Rcc-Editor) by [Nekiro](https://github.com/nekiro)
* [ClientConverter](https://github.com/Arch-Mina/ClientConverter) by [Arch-Mina](https://github.com/Arch-Mina)
* [Assets Editor](https://github.com/Arch-Mina/Assets-Editor) by [Arch-Mina](https://github.com/Arch-Mina)

**Open-source components shipped:** .NET and WPF (MIT), Google.Protobuf (BSD-3-Clause), Magick.NET with ImageMagick (Apache-2.0), NAudio and NVorbis (MIT), Newtonsoft.Json (MIT), CommunityToolkit.Mvvm (MIT), LZMA SDK (public domain). Full texts: `THIRD-PARTY-NOTICES.txt`.

**Logos:** CipSoft's press page allows its Tibia and CipSoft logos to be published without prior authorization and asks for a short notification after publication. If the site shows them, send CipSoft that notification.

---

## 9. Do not claim (not verified)

Keep these off the site, or state them as limits:

* Converted (Client Converter) or edited (Assets Editor) files have **not been loaded in a real game client**; `.aec` packages were not exchanged with Arch-Mina's Assets Editor.
* Tested at 100 % display scaling on Windows 11 only; Windows 10, other scales and screen readers are untested.
* No guarantee that a future Tibia update will not break a feature (the Compatibility page says what is affected).
* A rebuilt client installation has **not been started** by Tibia Foundry; whether it runs is not claimed.
* Converting existing archived revisions between storage modes is not available.

---

## 10. Suggested site structure

1. **Home**: logo, tagline, hero hook, two download buttons (Portable / Folder), 4–6 feature tiles, screenshot.
2. **Features**: the sections of §4 with one screenshot each (Appearances, Outfitter, Item Catalog, Map Tools HR map, House Images, Assets Editor, Clients).
3. **Download**: requirements (§3), both editions, SHA-256 checksums, upgrade notes, changelog link.
4. **Safety & privacy** (§5).
5. **Changelog** (§7, details from `CHANGELOG.md`).
6. **Docs / FAQ**: getting started, set your creator name, where exports go, after a Tibia update.
7. **About**: creator profile and links, credits, acknowledgments, disclaimer (§8).

**FAQ starters:**
* *Does it change my Tibia client?* No; it only reads it.
* *Can I get banned?* The extractors and editors never touch the running game.
* *Do I need .NET?* No.
* *Where are my files?* `Documents\Tibia_Foundry`.
* *Why is my name in the images?* Attribution metadata; set your own name in Settings → Attribution.
* *Tibia updated and something stopped working?* Open Compatibility. To keep the old version, archive it before the launcher updates; Tibia Foundry checks the installed clients at every start and offers to.

**Screenshots:** the UI checks render every page in light and dark themes (`05_verification/8.0.0/release/ui_snapshots/`); they are a starting point, but take fresh ones of real use for the site.

---

## 11. GitHub Pages notes

* A GitHub Pages site can be served from the creator's GitHub (https://github.com/RockyJLaRo) and pointed at tibiafoundry.com with a custom domain.
* Release zips (88–94 MB) fit GitHub Releases (2 GB limit per file) but not a repository file (100 MB limit, 50 MB warning). Host downloads as release assets.

## 12. Open decisions for you

* A license for Tibia Foundry itself (none is published yet).
* Whether the source code will be public.
* A dedicated Tibia Foundry logo (the current one was supplied for Tibia Forge).
* The website's own privacy statement (no analytics would match the app's "local only" stance).
