# 24 – Asset Library and Content Studio (8.8)

Tibia Foundry 8.8 adds a **Content Production** group to the navigation with two pages:

* **Asset Library** – a local, searchable library of every image, animation and sound you work with.
* **Content Studio** – reusable content projects, a workflow builder, publishing profiles for any website, and a background processing queue.

Both are local-first: nothing is uploaded, no account is needed, and everything works offline.

---

## 1. Asset Library

### 1.1 What it stores

| Part | Where (installed edition / portable edition) |
|---|---|
| Index (SQLite, write-ahead log) | `%LOCALAPPDATA%\TibiaFoundry\Library\library.db` / `TibiaFoundry-Data\Library\library.db` |
| Thumbnail cache (can be deleted any time) | `…\Library\thumbnails\` |
| Files copied into the library ("Copy into the library") | `…\Library\files\yyyy-MM\` |
| Publishing profiles, content projects, workflow templates (JSON) | `…\Library\profiles\`, `projects\`, `workflows\` |

The folder can be changed on the page (**Folders and storage → Library folder → Change…**); the setting is `Library.Folder` in `settings.json`.

**Files are referenced where they are and only read.** Removing an asset from the library never deletes a file. Large collections are not duplicated.

### 1.2 Database design (schema 1)

| Table | Content |
|---|---|
| `assets` | path (unique), name, category, description, source, source tool, client version, game ID, format, width, height, frames, size, SHA-256, difference hash, pixel key, created/modified/file-modified times, creator, attribution, copyright, export status, missing flag, managed-copy flag, root |
| `asset_tags` | tags and keywords (case-insensitive) |
| `asset_fields` | custom metadata fields (key → value) |
| `asset_relations` | related assets (stored in both directions) |
| `asset_versions` | earlier contents of a file (hash, size, time) recorded when a rescan finds it changed |
| `categories` | user-created categories (the 18 built-in ones are fixed in code) |
| `collections`, `collection_items` | named, ordered sets of assets with description and thumbnail |
| `roots` | indexed folders (recursive, category, source, last scan) |
| `meta` | schema version; a library written by a newer version is refused, never altered |

Foreign keys cascade, so removing an asset removes its tags, fields, relations, versions and collection places – never the file.

### 1.3 Supported asset types and categories

Indexed formats: PNG, GIF (animated), BMP, JPEG, WebP, TIFF, OGG, WAV, MP3, FLAC, Opus, M4A (data files – JSON, CSV, TXT, MD, Lua, XML – only on request).

Built-in categories: Items, Creatures, Outfits, Outfit Addons, Mounts, Effects, Projectiles, Environment Objects, Sprites, Sprite Sheets, Maps, Map Sections, Houses, Sounds, Animations, Generated Images, Custom Artwork, Other Media. Users can add, rename and delete their own categories (deleting moves the assets to Other Media).

The category of a new file is inferred from the nearest folder name (`…\Outfits\…` → Outfits, `Missiles` → Projectiles, `Map_Exports` → Maps…), then from the file (audio → Sounds, `sprites-*` or `*sheet*` → Sprite Sheets, large images → Maps, animated GIFs → Animations, images Tibia Foundry made → Generated Images, images attributed to someone else → Custom Artwork). The client version is taken from a version-like folder name (`15.33.8791c6`), the game ID from a leading number of the file name (`1234_Idle_South`).

### 1.4 Indexing

* **Add folder…** registers a folder and indexes it in the background (Content Studio → Queue shows progress).
* **Rescan** is incremental: a file whose size and modified time are unchanged is not read again. Changed files keep their metadata and get a version record; files that disappeared are **marked missing, never removed**, and come back when found again.
* **Add files…** keeps files where they are, or copies them into the library (identical content already in the library is reused, never stored twice).
* **Every export of every tool is added automatically** (Library settings → "Add every file I export to the library"): sprites from Extract Sprites, GIFs and outfits from Appearances and the Outfitter, creature GIFs from Static Data, maps from Map Tools, house images, extracted sounds, script output. The source tool and the client version are recorded. Saved folders with more than 20,000 files are left for **Add folder…**. An optional collection collects all new exports (for example "Summer Update Resources").

Embedded attribution (PNG/GIF/JPEG: creators, attribution text, copyright) is read into the creator, attribution and copyright fields.

### 1.5 Search and filters

The search box matches every word against name, file path, description, game ID, tags and custom fields; a number also matches the asset number and game ID. Filters: category or collection, format, client version, source (tool or folder), tag, creator, animation (animated/still), export status (Not exported / Exported / Published), date added, width range, and missing files. Results are paged (240 per page) and sortable by name, newest, modified, category, size or game ID – the database does the filtering, so thousands of assets stay fast.

### 1.6 Previews and details

Thumbnail grid (pixel art enlarged by whole numbers with nearest-neighbour sampling, transparency on a checkerboard); the details panel animates GIFs with their own timing, plays sounds, and shows every fact (path, format, size, frames, file size, dates, source, SHA-256, collections, earlier versions, related assets). Name, category, description, tags, client version, game ID, creator, attribution, copyright, export status and custom fields can be edited – for one asset or for many at once (multi-selection applies the shared fields to all). Library edits never change the file (File Metadata edits files).

### 1.7 Collections

Create, rename, describe (description + thumbnail), duplicate, delete, export and import. A collection mixes categories and keeps its order. Exported collections (`*.tfcollection.json`) contain file names, SHA-256 hashes and metadata – not the files; importing matches assets by content hash first, then by path, and lists what is not in this library. Only metadata is read from such a file.

### 1.8 Duplicate detection

**Find duplicates** reports three kinds of groups and **never deletes, moves or merges anything**:

| Kind | How it is found |
|---|---|
| Identical files | same SHA-256, also with different names and from different client versions |
| The same graphic at another size | same *pixel key*: the pixels after undoing whole-number nearest-neighbour enlargement and removing transparent borders (a sprite and its 2× export, a padded copy) |
| Visually similar | 64-bit difference hash within a Hamming distance (Library settings `SimilarityThreshold`, default 4), similar aspect ratio, at least 24×24 px; candidates are found by hash slices (pigeonhole), so 100,000 images compare quickly |

Each group says whether the names differ and whether the files come from different client versions, and how much space identical copies take.

### 1.9 Integrity, backup and restore

* **Verify the library** – SQLite integrity check plus reading every file again: missing files and files changed since they were indexed.
* **Back up…** – a ZIP with a consistent copy of the index (SQLite online backup, safe while the library is in use) and the profiles, projects and workflows. The indexed files themselves are not part of the library and are not copied.
* **Restore…** – checks the backup (entry names, SQLite integrity, schema) before anything changes; the current index is kept as `library.db.before-restore-<date>`. Only known parts of a backup are extracted, and no entry can leave the library folder.

---

## 2. Content Studio

### 2.1 Projects

A project remembers **what** to make (its workflow, whose source steps hold the chosen creatures, outfits, items or collection), **how** (publishing profile, output folder, output mode) and **what happened** (history of runs). Templates:

| Template | Default workflow |
|---|---|
| Creature gallery | render creatures (moving, animated) → 2× → 128×128 canvas, bottom-aligned → description → `Creatures/` → contact sheet → JSON+CSV → HTML gallery → add to the library |
| Outfit gallery | render outfits with every addon variant → 2× → canvas → `Outfits/<outfit>/` → comparison sheet per outfit → manifests → gallery → library |
| Item image collection | render objects by name/ID → 32×32 centred canvas → `{id}-{name}` → contact sheet → JSON/CSV/Markdown → ZIP → library |
| Map reference project | library assets of category Maps → fit 2048 px → manifests → gallery (maps are made in Map Tools and House Images and reach the library automatically) |
| Fansite update resources | new and changed objects/outfits since an older `appearances-*.dat` → trim → 2× → `New|Changed/<category>/` → contact sheets per category → manifests → gallery → library |
| Wiki image preparation | a library collection → trim → PNG/GIF → one wiki file link per image → manifests |
| Custom asset export | any library collection or selection → the chosen profile |

Projects can be created from other pages: **Asset Library → Create a project…** (selection or collection), **Static Data → Gallery of the list…** (the creatures shown), **Compare Files → Visual gallery…** (what is new or changed since the old appearances file).

### 2.2 Workflow builder

Steps are grouped as **Sources** (Library assets, Import a folder, Render creatures, Render outfits, Render items and objects, New and changed since a version, Sprites by ID), **Each image** (Trim transparent borders, Resize – whole-number nearest-neighbour or fit-into-box with nearest or smooth filtering –, Consistent canvas with padding/alignment/background, File format, Description and tags, File names, Folders) and **Outputs** (Contact sheet, Comparison sheets, Manifests JSON/CSV/Markdown, HTML gallery, Text snippets, ZIP archive, Add to the library). Steps can be added, removed, reordered, switched off and configured; render steps have a picker listing the client's creatures, outfits or named objects.

Workflows are saved as templates or exported/imported as `*.tfworkflow.json`. A workflow file only describes steps with text settings; nothing in it is executed as code.

Template variables for names, folders and texts: `{name} {id} {category} {version} {animation} {direction} {width} {height} {dimensions} {scale} {collection} {index} {index3} {date} {creator} {format} {file} {folder} {attribution} {website} {source} {group}`. In file and folder names, characters Windows does not allow are replaced, and a folder template can never leave the output folder (`..` and absolute paths are removed).

### 2.3 How a run works

1. The source steps produce images one at a time (memory stays flat for thousands of images).
2. Each image goes through the processing steps; what the workflow does not specify, the profile does (scale, canvas, format, names, folders).
3. Every PNG/GIF is written with the creator attribution embedded. Images rendered from the client name the user as creator and CipSoft as owner of the artwork; images made from attributed files keep their original creators.
4. Output steps make sheets, manifests, galleries, snippets and archives from what was written.
5. Everything is written into a hidden staging folder inside the output folder and **moved into place only when the whole run succeeded**. A cancelled or failed run leaves no partial export.
6. Output modes: a new dated folder per run (default; earlier runs are never touched) or one folder updated in place (same-named files replaced atomically, other files kept).
7. One image that cannot be made is reported and skipped; the others are written. The run's notes appear in the project's history.

Originals are never changed: library and folder images are read, results are new files. Animation timing is kept from the client (or the source GIF); enlargement is nearest-neighbour, so pixel art stays sharp and transparency is preserved.

### 2.4 Processing queue

Content projects, indexing and rescans run in the **Queue** tab: pending, running, completed, failed and cancelled jobs with progress, **Cancel** (pending jobs at once, running jobs at the next image), **Retry** (failed or cancelled), **Details** (result, error and technical details) and a history of finished jobs (`queue-history.jsonl` next to the settings). Library settings choose how many jobs run at once (1–4). Closing the program while jobs are queued asks first.

### 2.5 Publishing profiles

A profile describes one website: name, website, description, image format (keep / PNG / GIF; animations as GIF or first-frame PNG), whole-number scale (1–8), fixed canvas (or none), padding, background (transparent or a colour), trimming, file-name and folder templates, letter case, space replacement, manifests (JSON, CSV, Markdown index, HTML gallery), the credit line, and a text snippet per image. Editable examples: **TibiaWiki** (1×, game names, `[[File:…]]` links), **Custom fansite** (2×, lower-case hyphenated names, HTML `<img>` snippets), **Gallery website** (2× on a 128×128 canvas with an HTML gallery), **Article publishing** (3×, Markdown with credit lines), **Personal archive** (original size by version and category). Editing a built-in profile stores a copy; *Reset to the original* restores it. No profile assumes the requirements of another site.

### 2.6 HTML gallery

`index.html` with its CSS and script inside, next to the images: thumbnails, full-size view with metadata and credit, category chips, search, animations, responsive grid, light/dark/auto theme with a toggle (remembered in the browser), reduced-motion support. No server, database or external file is needed – it works from disk or any web host. Asset text is written as JSON and shown with `textContent`, so names can never inject markup. CSV manifests prefix cells that a spreadsheet would run as formulas.

---

## 3. Integration with the other tools

| Tool | Integration |
|---|---|
| Extract Sprites, Appearances (GIF export, Outfitter), Static Data (creature GIF), Map Tools, House Images, Sounds, Advanced Tools, Scripts, RCC Editor, Assets Editor | every saved file or folder is added to the library automatically, with the tool and client version recorded, optionally into a collection |
| Static Data | *Gallery of the list…* creates a creature gallery project with the creatures shown |
| Compare Files | *Visual gallery…* creates an update project of everything new or changed since the old appearances file |
| Asset Library | *Create a project…* sends a selection or collection to Content Studio |
| Home | the Content Production group, two new workflow entries, recent exports note that exports are also in the library |
| Diagnostics/closing | queued jobs are reported when the program closes |

## 4. Settings (`settings.json`, section `Library`)

| Setting | Default | Meaning |
|---|---|---|
| `Folder` | null | library folder (null = default for the edition) |
| `AddExportsAutomatically` | true | add every export |
| `ExportCollection` | null | collection for new exports |
| `AutoAddMaxFiles` | 20000 | larger saved folders are not added automatically |
| `SimilarityThreshold` | 4 | visual similarity (0–16 bits) |
| `QueueConcurrency` | 1 | jobs at a time (1–4) |
| `ContentOutputFolder` | null | default output (null = `Documents\Tibia_Foundry\Content Projects`) |
| `LastProjectId` | null | project opened last |

Settings files of 8.7.0 and earlier load without this section (defaults apply); 8.7.0 ignores it.

## 5. Dependency

Microsoft.Data.Sqlite 10.0.14 (MIT) with SQLitePCLRaw 2.1.13 (Apache-2.0) and the native SQLite library (public domain), listed in `THIRD-PARTY-NOTICES.txt`. The single-file portable edition bundles the native library like the other native libraries.

## 6. Limitations

* Map and house images are made by Map Tools and House Images; Content Studio collects them, it does not render maps itself.
* Visual similarity is a hint for review, never proof; nothing is deleted on that basis.
* Audio files are indexed and played, but not processed by workflows.
* Thumbnails are not generated for images above 8192×8192 px or visual signatures above 4096×4096 px (very large maps); they are still indexed and searchable.
