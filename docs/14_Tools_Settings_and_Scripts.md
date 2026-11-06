# 14 — Tools, settings and scripts (5.0, updated for 6.0)

> **6.0:** the tools are now registered with manifests (capabilities, dependencies, settings; `16_Adding_a_Tool.md`), write to `Documents\Tibia_Foundry\<kind>` by default, name the saved file in a clickable status message, and appear on the Diagnostics page while they run. The settings gained *Appearance & Themes*, *Tools* and *Diagnostics*; the diagnostics of *Advanced* moved to the Diagnostics page. Details: `15_Themes_Outputs_Tools_and_Diagnostics.md`.

Tibia Foundry 5.0 adds four pages under **Tools**: the **Assets Editor**, the **RCC Editor**, the **Client Converter** and **Scripts**. It also adds a categorized **Settings** page and shared file-safety infrastructure that every tool uses. This document describes what each part does, how it protects files, how it was verified, and what is not implemented.

The features of three open-source projects were the reference: [Nekiro's RCC Editor](https://github.com/nekiro/Nekiro-Rcc-Editor), [ClientConverter](https://github.com/Arch-Mina/ClientConverter) and [Assets Editor](https://github.com/Arch-Mina/Assets-Editor). **No code from these projects is included.** Their license status is described in §12. Every feature was implemented in C# from knowledge of the file formats, and checked against the real client files.

## 1. Shared infrastructure

All tools write files through the same components in `TibiaFoundry.Core/FileOps`:

| Component | What it guarantees |
|---|---|
| `PathGuard` | Paths that come from data (archive entries, resource paths, renamer output, manifests) are checked before use. It refuses absolute paths, `..`, empty segments, invalid characters, trailing dots and spaces, and Windows device names (`CON`, `NUL`, …). A resolved path must stay inside its destination folder. Links and junctions are detected and are never followed by scans. |
| `OutputSession` | Applies the policy for files that already exist: **Ask** (default), **Keep both** (`name (2).ext`), **Replace**, or **Skip**. An operation never overwrites its own input files. Two outputs of one operation never overwrite each other. Every file is written to a temporary file, checked (when validation is on), and only then moved into place. Replaced files are backed up first (when backups are on). |
| `BackupStore` | Backup sets go in the backup folder, one folder per operation, with a `backup.json` manifest. Every copy is verified with SHA-256. A restore refuses damaged copies and backs up any file it overwrites. Retention removes old sets by age and total size at start-up. A set that is still being written is never removed. |
| `TempWorkspace` | Each operation gets its own temporary folder (a lease). Clean-up at start-up and exit skips folders that are still in use. |
| `FileSignatures`, `FileInspector` | Identify a file by its content, not its name. Each result states how deeply the file was checked: *identified*, *header read*, *structure checked*, or *decoded completely*. |
| `FileHashing`, `ReportWriter` | SHA-256 (also SHA-512, SHA-1 and MD5 for old lists), and CSV/JSON/text reports. CSV cells that start with `=`, `+`, `-` or `@` are quoted so spreadsheet programs do not run them as formulas. |

The app adds an **operation gate**: at most *n* long operations run at once (Settings → Performance), and the others wait in a queue with a status message. It also adds **completion notices** (taskbar flash and/or sound after a configurable duration) and taskbar progress.

**Folder precedence** (every tool): the tool's own folder (Settings → Files and folders) → the folder last used with the tool (when *Remember last folders* is on) → its subfolder of the output root (6.0: `Documents\Tibia_Foundry\Assets_Editor`, `RCC_Exports`, `Client_Converter`, `Scripts`; 5.0: `AssetsEditor`, `RCC`, `ClientConverter`, `Scripts` under `Documents\TibiaSprites`). Files are never written anywhere else without the path being shown first.

## 2. RCC Editor (Qt resource files)

Opens `.rcc` files, such as the client's `bin/graphics_resources.rcc`, in formats 1–3.

**How to use**: *Open…* a file, or *Client file* for the installed client's. Select a resource to preview it. *Replace…* (or drop a file on the entry), *Add…*, *Rename…*, *Remove*, *Update from folder…*. Nothing is written until *Save* or *Save as…*. The installed client's file can only be saved as a copy. **Files it changes**: only the file you save to (its previous version goes into a backup) and the files you extract.

* **Browse**: list with name, folder, type, size and compression. Filter by path and type. Preview of images (with size), text and JSON. Files larger than the preview limit (Settings → Performance) are not previewed.
* **Edit**: replace a resource (picker or drag and drop), add files, rename or move a resource, remove resources. Before a replacement, the new file is checked: different format or image size, or a file that is not an image, triggers a warning first (Settings → File safety → *warn about unsupported formats*).
* **Compression of new content**: *like the replaced resource* (default), Qt's rule (zlib only when it saves at least 30 %), never, or always.
* **Extract** selected or all resources. Folders inside the file are kept, so equal file names in different folders never overwrite each other. An `rcc-manifest.csv` lists each file's size, compression and SHA-256.
* **Update from folder**: imports an extracted, edited folder. Files whose content changed replace their resource; new files can be added on request.
* **New from folder**: builds a new resource file from a folder.
* **Save / Save as**: the file is rebuilt (names sorted by Qt's hash, as Qt's lookup requires) and then **validated before it replaces anything**. The validator checks the structure and looks up every file with a port of Qt's `QResourceRoot::findNode`. It also compares the content of every resource after writing. The previous version is backed up. The installed client's file is never overwritten: *Save* offers *Save as* instead.

**Zstandard**: resources compressed with Zstandard (flag 4) are listed and kept unchanged when the file is saved. They cannot be previewed. On extraction they are saved as their raw `.zst` frame, with a note.

**Verified**: the installed client's `graphics_resources.rcc` (format 3, 1,063 files) passes validation, and all 1,063 files are found by the Qt lookup port. Rebuilding it without changes gives a valid file with identical entries. An independent Python reader confirmed the rebuilt file. The upstream editor's flat export overwrote two pairs of equal file names in that file; Tibia Foundry keeps the folders.

## 3. Client Converter

Converts the modern client assets (protobuf appearances and LZMA sprite sheets) into a legacy `Tibia.dat` and `Tibia.spr`, and identifies existing legacy files.

**How to use**: choose the assets folder (*Installed client* fills it in) and an output folder outside it. Choose the target format and options, then *Convert*. The result lists the counts, everything that could not be stored exactly, and the validation. *Identify a legacy Tibia.dat…* reads an existing pair and names its format. **Files it changes**: `Tibia.dat`, `Tibia.spr` and, if chosen, the slices, all in the output folder (existing files are handled by the file-safety policy). The assets are only read.

| Target | Writes | Signatures | Notes |
|---|---|---|---|
| **Client 11 (for 15.x assets)** | yes | 0x4A10 / 0x59E48E02 | 10.10+ layout with idle/moving frame groups, animation timing and 32-bit sprite IDs |
| **Tibia 10.98** | yes | 0x42A3 / 0x53159CA9 | 10.98 layout |
| **Tibia 8.60** | **identify only** | 0x4C2C7993 / 0x4C220594 | reading for *Identify a legacy Tibia.dat*; not written |
| Custom signatures | yes | user-defined | for custom clients that check other signatures |

**Options**: keep transparency (RGBA sprites); do not store empty sprites; shorten market names to 29 characters (client 11 is reported to refuse longer ones); list newer market categories under *Others* (on by default, see below); extended attributes for third-party legacy-format tools (off by default, with a warning, because CipSoft clients cannot read them); also save every 32×32 slice as PNG.

**Lossy conversions are reported, never silent.** The result lists, with counts:

* semi-transparent pixels flattened (without transparency);
* market names shortened;
* market categories mapped to *Others*;
* modern information the legacy format cannot hold (for example names or restrictions of items without market data).

**Market data**: categories, *trade as* and *show as*, the name (Latin-1, as the client stores it), the vocation restriction as the legacy bit mask (knight 1, paladin 2, sorcerer 4, druid 8, monk 16), and the minimum level. Current clients keep the restrictions in the object flags; older ones kept them inside the market data, which is read as a fallback. Clients of the 10.98 era know market categories 1–22. Tibia Coins (23), creature products (24), quivers (25), soul cores (26) and fist weapons (27) came later, so by default they are written as *Others* (9). This is the same choice ClientConverter makes.

**Validation** (always): after writing, `Tibia.spr` is read back and every sprite must decode to exactly the pixels written. `Tibia.dat` is read back with the target layout, and every object must match and every sprite ID must exist.

**Defects of ClientConverter that are not reproduced** (found by reading its source):

* the animation loop *count* was replaced by the loop *type*;
* the start phase was inverted;
* single-phase objects received animation data;
* grounds without speed wrote no value;
* market names were written character by character (UTF-8 for non-ASCII letters) with a character count as length;
* the appearances file was assumed to be the catalog's first entry;
* slice names collided;
* transparency was dropped.

**Verified on the installed client 15.33**: 57,427 objects and 613,032 sprites (118,087 empty tiles skipped) in about 40 s; full validation passed; 0 failures.

**Comparison with a reference conversion** (`ReferenceConversionTests`, opt-in with `TF_REFERENCE_LEGACY`): the reference is a `Tibia.dat`/`Tibia.spr` pair made by ClientConverter from client **15.30**, compared with our conversion of **15.33**. Results for the 57,061 object IDs in both files:

* **98.02 %** have identical attributes (order ignored, because clients read attributes in any order);
* **13,873 of 13,879** sampled sprites (2,872 items) are pixel-identical. The 6 items with differences (49552, 49571, 49590, 52991, 53010, 53181) were not examined further. They are most likely objects that changed between 15.30 and 15.33.

Explained differences:

* The reference writes 0 for every market level and vocation. It reads them from their older place, which is empty in 15.x data. We write the client's values (for example *strong health potion*: level 50, knight + paladin + monk).
* 115 market names differ: 114 shortened to 29 characters, plus one change between versions.
* 9,895 objects differ only in the "exact size" of large objects. The reference uses the first bounding box; we use the client's bounding square.
* Animation timing differs for 10 objects. They were not examined individually in this run. An earlier check of 5 such differences traced each one to the reference's loop-count defect.

**Unexplained**: about 300 single flags (for example *NotMoveable* in 89 objects). These are most likely client changes between 15.30 and 15.33, but this could not be confirmed without the 15.30 assets.

The full report is in `05_verification/5.0.0/reference_conversion.txt`.

## 4. Assets Editor

Opens a modern client assets folder (or the installed client's) and edits it. Saving never changes the installed client in place.

**How to use**: *Open…* an assets folder (the one with `catalog-content.json`) or *Installed client*. Pick an object in the list (search by ID, range or name; filter by flag). Edit its properties on the right and its sprites in the middle: click a cell to replace its image or give it a new one, and set the layout (layers, patterns, phases) with *Apply layout*. *Sprite sheets* browses the sheets. *Save as…* writes a complete copy to an empty folder; *Save* writes back into a folder that is not the installed client. *Export to legacy client…* passes the saved folder to the Client Converter. **Files it changes**: the folder you save to. *Save* keeps old sheet and appearances files and backs up the catalog. *Save as* never touches the source folder.

### Feature inventory against Arch-Mina's Assets Editor

| Upstream feature | Tibia Foundry 5.0 | Notes |
|---|---|---|
| Tibia 12+ assets: open, browse objects/outfits/effects/missiles | **Implemented** | List with thumbnails, search by ID, ID range or name, filter by flag |
| Object modification (all fields) | **Implemented** | Generic property editor over the full appearance schema: every flag, frame group, sprite info and animation field, with presence (set/unset) kept |
| Create / copy / delete objects | **Implemented** | New, duplicate, delete, change ID, revert to the saved version; copy and paste flags |
| Import and export (object packages `.aec`) | **Implemented** | Same structure as the upstream format, as read from its source (sprite images as PNG in field 7). **Not tested with the upstream program**, which was not run. |
| Import objects from another assets folder | **Implemented** | With their sprites (*Copy from folder…*) |
| Sprite sheet modifications | **Implemented** | Replace a sprite everywhere, give one cell a new sprite, add sprites (32×32, 32×64, 64×32, 64×64), change sprite layout (layers, patterns, phases); new sprites go into new sheets of their size; changed sheets are re-encoded |
| Sheet browser | **Implemented** | Browse sheets, find a sprite by ID, see which objects use it, export a sheet as PNG |
| Transparent items in sprite sheets | **Implemented** | Alpha is kept |
| Export to spr/dat | **Implemented** | Through the Client Converter (*Export to legacy client…*) |
| Export to outfit/item images | **Implemented** | PNG (first frame) and animated GIF of the selected objects, with attribution. Colored outfits with addons are made on the Appearances page's outfitter |
| Save | **Implemented** | Save (backs up the catalog, keeps old files) or Save as (complete folder copy); appearances and catalog validated; refuses the installed client in place, overlapping or non-empty targets |
| Tibia 10.98 (legacy dat/spr) editing | **Not implemented** | Legacy files can be written (Client Converter) and identified, but not edited. Planned. |
| Import manager for 10.98 (OBD and similar) | **Not implemented** | Depends on legacy editing |
| "Large spritesheets" | **Not implemented** | Only the client's 384×384 sheets are written |
| Lua scripting and Lua graphs | **Not implemented, deliberately** | Runs arbitrary code; it needs a trust and sandbox model first (see §11) |

**Verified on the installed client 15.33**: opening takes about 0.3 s. *Save as* (6,249 files copied) took 8.8 s, and the result validated. New and replaced sprites read back correctly. All 43,516 unchanged objects are byte-identical after saving, and the catalog is byte-identical (its original spacing and separators are kept). The appearances files of 15.33 and 15.25 round-trip byte for byte through the editable schema (proto2 with field presence; strings are kept as raw Latin-1 bytes).

## 5. Settings

The Settings page lists the categories on the left; choosing one scrolls to its section. Every change is **applied and saved immediately**; no option needs a restart. Every category has its own *Restore defaults* button. *Export settings* and *Import settings* move all settings between computers. An import is validated, and it asks before replacing anything, including the creator name.

| Category | Settings (default) | Effect |
|---|---|---|
| **General** | Start page: last used / a chosen page; remember last folders (on); reopen last files in the editors (off); *Forget remembered folders* | Start-up page, folder pickers, editors |
| **Appearance & Themes** (6.0) | Theme list: System (default), Light, Dark and custom themes; custom theme editor (colors by group, hue/saturation/lightness, hex, reset color/theme, save/revert, new/rename/delete/import/export, readability warnings, live preview); *Restore original appearance*; interface size 80–150 % (100 %); navigation bar: automatic / always names / always icons; remember window size and position (on); animate previews (on); show default values (off); editor list thumbnails (32 px) | Whole window, previews, editors |
| **Tibia client** | Test-server client and package; client assets folder; load data automatically; sprite source | (as in 4.0) |
| **Files and folders** | 6.0: output root (`Documents\Tibia_Foundry`) and one folder per kind of output (GIF, image, sprite, sound, map and data exports, RCC Editor, Client Converter, Assets Editor, Scripts, Reports, Manifests, Backups, Logs, Temporary files), each marked default / chosen / kept from 5.0; where each tool's file pickers start (Assets Editor, RCC Editor, Client Converter) | Folders that cannot be used are explained next to the path; missing ones say they are created when needed; *Change…*, *Use default* and *Open* for each |
| **File safety** | When a file already exists: **Ask** / Keep both / Replace / Skip; back up before replacing (on; turning it off asks for confirmation); backup retention (30 days, 4,096 MB); validate written files (on; turning it off asks); warn about unsupported formats (on); clean temporary files (on) | All tools |
| **Performance** | Long operations at the same time (2); worker threads (automatic); sprite sheets kept in memory (96); stop a batch at the first error (off); taskbar progress (on); preview size limit (32 MB) | All tools |
| **Attribution** | Creator name (RockyLaRo), with a live preview of `Created by <name> using TibiaFoundry for TibiaWiki @ https://tibia.fandom.com/` | Every exported PNG/GIF and text export |
| **Notifications and logging** | Flash taskbar when done (on); sound when done (off); only after (10 s); error dialogs (on); log level (Info); log retention (14 days); log size per day (20 MB); all log files at most (200 MB; shown here since 7.2, before under Diagnostics) | All operations |
| **Tools** (6.0) | The settings each tool declares in its manifest, with generated, validated controls and *Restore defaults* per tool (Client Converter: target client, transparency, skip empty sprites, shorten names, new market categories under Others, sprite slices) | The tool's page follows at once |
| **Scripts** | Preview changes first (on); backups by scripts: follow file safety / always; keep history (on, 200 entries); scripts at the same time (1); show advanced options (off) | Tools → Scripts |
| **Diagnostics** (6.0) | Performance measurement while the Diagnostics page is open (on, every 2 s); operations kept in the history (200); days of logs in a report (3) | Diagnostics page |
| **Advanced** | Storage mode, settings and log file; memory, temporary files and backups; clear caches; remove temporary files; open the backup folder; open Diagnostics; save the session's changes; import the settings of the original 1.x program; reset all settings | — |

**Migration**: settings of format 2 (4.0) are upgraded to format 3 on the first start, and nothing is lost: the new categories start with the 4.0 behavior. 6.0 upgrades format 3 to format 4 (output folders, themes, diagnostics; `15_Themes_Outputs_Tools_and_Diagnostics.md` §2). Values are clamped into valid ranges when they are loaded. Options written by a newer version are kept.

## 6. Scripts (Utilities → Scripts)

### Framework and safety

* Scripts are **built into the program** (C# classes registered in `ScriptRegistry`). There is no script file format and no way to run arbitrary code. A new script is a class deriving from `Script` (parameters, validation, `Run`, and for scripts that change files `Apply`), added to `ScriptRegistry.CreateAll`; the page, history and safety checks need no changes. Scripts **never run by themselves**: not at start-up, and not when a page opens.
* Every input is validated before a run (folders must exist; files that will be written must be outside the input folder, and so on).
* **Preview first**: a script that changes files first produces a plan, with each change, its target and its reason. Nothing changes until you select changes and confirm *Apply*. Changes that would be unsafe are shown blocked. When applying, each change is checked again: files to be moved away must be unchanged since the preview (SHA-256), the copy a duplicate group keeps must still exist unchanged, and a rename target must still be free. Otherwise the change is skipped and reported.
* **Nothing is deleted.** "Removed" files are moved into a backup set of the kind *Removed*, and can be restored with *Backup and Restore*. Files that are replaced are backed up first. Renames are recorded and can be undone.
* All paths from data go through `PathGuard`. Links are never followed. Writes go through `OutputSession`.
* Long runs show progress and can be cancelled. At most *n* scripts run at once (Settings → Scripts).
* **History** (clearable): each preview, run and apply, with time, duration, input folder, counts, summary, backup folder and output files. File contents are never stored.

### Using a script

1. Choose a script on the left (search, or filter by category). Its description, supported inputs and limitations are shown.
2. Fill in the inputs. Folders and files have pickers; *Show advanced options* shows the rest. Values are remembered per script.
3. **Run** (scripts that only read) or **Preview changes** (scripts that change files). The results appear as a table; *Export report…* saves it as CSV, JSON or text, with an attribution header.
4. For a preview: tick the changes you want (blocked ones cannot be ticked), then **Apply** and confirm.
5. *History* lists earlier runs; *Open backup folder* and *Open output folder* go to their files.

**Recovering**: files a script moved away or replaced are in a backup set. Restore them with *Backup and Restore* → *List backups*, then *Restore*. A Batch File Renamer run is undone with its task *Undo an earlier rename*, which uses the record kept in `backups\rename-records`.

### The 12 scripts

| Script | Category | Changes files | What it does |
|---|---|---|---|
| Duplicate File Finder | File management | moves extra copies to a backup (after preview) | Groups by size, confirms with SHA-256, always keeps one copy |
| Directory Comparison | Comparison and verification | no | Only in A, only in B, different size/time/content |
| Batch File Renamer | Batch processing | renames (after preview; undoable) | Prefix/suffix, find/replace (regex), numbering, case, spaces, extensions (only when the content matches); conflicts blocked; swaps handled through temporary names |
| File Hash and Checksum Calculator | Comparison and verification | writes a manifest only | SHA-256/512, SHA-1, MD5; manifests in the `sha256sum` layout; verify against a manifest |
| File Inventory and Manifest | Diagnostics and reports | writes a report only | Paths, sizes, dates, detected formats (optionally SHA-256), totals per extension; CSV/JSON |
| Large File Finder | File management | no | Largest files first; optionally the largest folders |
| Empty Files and Folders | File management | moves to a backup (after preview) | Folders that programs expect are never proposed: version control (`.git`, `.svn`, `.hg`), `node_modules`, logs, caches, temp and backup folders, the Tibia client's folders (`screenshots`, `characterdata`, `minimap`, `conf`, `assets`, …) and system folders |
| Batch Image Converter and Resizer | Image utilities | writes new files | PNG/GIF/BMP/WebP (lossless)/JPEG; nearest-neighbor, bilinear, bicubic and Lanczos scaling; attribution kept in PNG/GIF; JPEG (lossy) must be allowed explicitly; never writes into the input folder |
| Archive Contents Inspector | Archives | extracts safe entries (after preview) | ZIP, TAR, TAR.GZ; flags path traversal, absolute paths, links, duplicate names and compression ratios above 200:1; checks free disk space; 20 GB limit |
| File Format and Metadata Inspector | Diagnostics and reports | no | Content-based identification, extension mismatches, image sizes, PNG/GIF attribution, archive contents, `.rcc`, client LZMA/protobuf and legacy dat/spr files, with the depth of each check |
| Backup and Restore | File management | backs up / restores (after preview) | Lists all backups, including automatic ones and *Removed* sets; a restore backs up what it overwrites |
| Tibia Project File Auditor | Tibia project utilities | no | Client files present, every file `catalog-content.json` lists exists (and optionally matches the hash in its name), unlisted files, duplicates, empty or tiny files, wrong extensions; optional manifest comparison |

### Recommended additional scripts (not implemented)

1. **Sprite sheet consistency check**: decode every sheet of an assets folder and report damaged or mis-sized ones. This partly exists in the Compatibility page and the auditor.
2. **Export folder diff by image content**: compare two sprite export folders pixel by pixel instead of by bytes, so attribution metadata does not count as a difference.
3. **Wiki upload preparation**: check names, sizes, attribution and format against TibiaWiki's file guidelines before an upload.
4. **Old backup and temporary file report**: what the retention would remove next, before it runs.
5. **Client version snapshot**: a manifest of the installed client's assets, to diff after the next update.

## 7. Backups and restore

* **Automatic backups**: any file a tool is about to replace is first copied into a backup set (Settings → File safety → *Back up before replacing*, on by default). This covers editor saves, conversions over existing files, script results and restores. Files that scripts "remove" are moved into a set of the kind *Removed*. Every copy is checked with SHA-256.
* **Manual backups**: *Backup and Restore* → *Back up* copies chosen files or folders into a *Manual* set, with a note.
* **Restore**: *Backup and Restore* → *List backups* shows every set (date, operation, kind, files, size). *Restore* previews which files would be restored and which existing files would be overwritten. Those are backed up first. Restored files go back to their original place, or to a folder you choose.
* **Retention**: at start-up, sets older than the retention period (30 days) are removed, and the oldest sets go when all sets together exceed the size limit (4 GB). Sets that are in use are never removed.
* **Where**: `%LOCALAPPDATA%\TibiaFoundry\backups` (portable: `TibiaFoundry-Data\backups`) or the folder in Settings → Files and folders.

## 8. Supported formats

| Format | Read | Write | Notes |
|---|---|---|---|
| Client assets (`catalog-content.json`, `appearances-*.dat`, `sprites-*.bmp.lzma`) | yes | yes (Assets Editor) | 12+/15.x clients; validated with 15.33 and 15.25 |
| Qt resource files (`.rcc`) | formats 1–3 | formats 1–3 | zlib; Zstandard read and kept, not decompressed |
| Legacy `Tibia.dat` / `Tibia.spr` | 8.60, 10.98, Client 11 (identify) | Client 11, 10.98 | 7.40–7.80 layouts are known to the codec but not offered |
| Object packages (`.aec`) | yes | yes | layout of Arch-Mina's Assets Editor (not tested with it) |
| Images (scripts, editors) | PNG, GIF, BMP, JPEG, TIFF, WebP, ICO | PNG, GIF, BMP, WebP (lossless), JPEG | via ImageMagick; attribution in PNG and GIF |
| Archives (Archive Inspector) | ZIP, TAR, TAR.GZ | extraction of safe entries | no RAR or 7-Zip |
| Checksum manifests | `sha256sum` layout | same | SHA-256/512, SHA-1, MD5 |

## 9. Attribution in the new tools

* **With attribution**: images exported by the Assets Editor (PNG and GIF), the converter's 32×32 slices, the image converter's PNG and GIF results (the creators of the source images are kept unless you choose otherwise), and the reports that scripts save (a header line).
* **Without attribution**: files that must stay exactly as the client has them, such as resources extracted from an `.rcc` file, saved assets folders and the legacy `Tibia.dat`/`Tibia.spr`. Also BMP, WebP and JPEG results of the image converter.

## 10. Troubleshooting

| Problem | What to do |
|---|---|
| "The target folder is not empty" (Assets Editor *Save as*) | Choose an empty or new folder. This keeps saved files from mixing with other files. |
| "This is the folder of an installed Tibia client" | The client's own files are never changed. Use *Save as* to another folder. |
| An RCC file "failed validation" when saving | Nothing was replaced. The message names the resource; check the replaced file (format, size) and save again. |
| The converter reports failures | The listed objects were written empty. The reasons are in the result and in the log. *Stop at the first failed file* (Settings → Performance) stops instead. |
| A script's change was "skipped: changed since the preview" | The file changed after the preview. Run the preview again. |
| An operation says it is waiting | Another long operation is running. The limit is in Settings → Performance (and Settings → Scripts). Esc cancels the waiting operation. |
| A file "already exists" question every time | Settings → File safety → *When a file already exists*. Choose *Keep both*, *Replace* or *Skip* as the default. |
| A folder "cannot be used" | The message says why (drive not connected, no permission, not a folder, invalid name). Choose another folder when asked, or in Settings → Files and folders. *Diagnostics → Self-check* lists every folder's state. |
| A tool says something it needs is missing | The banner and *Diagnostics → Tools* name the dependency and how to resolve it. Only the operations that need it are blocked. |
| Anything else | *Diagnostics → Report*: preview, then save a sanitized diagnostics report (ZIP in `Documents\Tibia_Foundry\Reports`, never sent anywhere) to attach to a bug report. *Diagnostics → Log* filters the logs by tool, operation, level and text. |

## 11. Deferred features and reasons

* **Lua or user-written scripts**: running user code needs a sandbox and trust model (signed scripts, permissions, no file access outside chosen folders). Without one it would contradict the rule "no arbitrary code".
* **Editing legacy `Tibia.dat`/`Tibia.spr`, OBD import**: not implemented; the modern assets are the official source for current graphics.
* **Writing 8.60 files**: 8.60 is read only. Writing it needs the older sprite-ID limits and attribute bytes to be verified against a real 8.60 client, which was not available.
* **Zstandard decompression in the RCC Editor**: no Zstandard library is bundled; such resources are kept and extracted raw.
* **RAR and 7-Zip** in the Archive Inspector: proprietary or unbundled formats; ZIP and TAR only.

## 12. Licensing of the referenced projects

| Project | License found (checked 2026-10-12) | Consequence |
|---|---|---|
| Nekiro's RCC Editor | `"license": "MIT"` in `package.json`; no LICENSE file. The project ships `rcc.exe` binaries (rccextended). | Nothing copied, and no binaries run or shipped. Acknowledged on the About page. |
| ClientConverter (Arch-Mina) | **No license published.** The project also contains closed DLLs. | Without a license, all rights are reserved. Nothing copied; only its output was used for comparison. |
| Assets Editor (Arch-Mina) | **No license published.** | Nothing copied; its feature list was the checklist and its `.aec` structure was read to stay compatible. |

These projects appear in **About → Open-source acknowledgments**, separate from the credits (Tibia, CipSoft, TibiaWiki). The section says that the projects and their authors are not affiliated with Tibia Foundry and do not endorse it.
