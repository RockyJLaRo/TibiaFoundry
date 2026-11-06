# Changelog

## 9.0.1 – 2026-11-06 (infobox flavortext)

### Fixed

* **Appearances → Wiki infobox** now fills `flavortext` with the object's look text from the client (`Appearance.Description`), e.g. item 54631 (moonstone excavator soul core): *Offers a soul to the Soulpit. Combine with another soul core to create a random new soul core*. Before, the parameter was left out even when the client had the text.
* Version numbering: patch releases have three parts (**9.0.1**, tag `v9.0.1`); the Windows file version is padded to 9.0.1.0.

## 9.0 – 2026-11-06 (TibiaWiki infoboxes, safer saving, one version source)

Versions are two-part from now on (**9.0**, tag `v9.0`). Settings, item catalogs, client archives, Asset Libraries and exports of 8.8.2 and earlier are used as they are; the MSI upgrades 8.x in place and never touches user data. Audit, measurements and decisions: [TibiaFoundry-9.0-Audit.md](TibiaFoundry-9.0-Audit.md).

### Added

* **Appearances → Wiki infobox** ([docs/26_TibiaWiki_Infobox_Exporter.md](docs/26_TibiaWiki_Infobox_Exporter.md)): TibiaWiki's `{{Infobox Object}}` for the selected object, built from the client's data and the template's own parameter list (checked against `Template:Infobox Object` and real item pages). Filled from the client: `name` (TibiaWiki article name when the wiki's item list is cached, otherwise article casing), `actualname`, `itemid`, classification from the market category (where the wiki's classification is consistent), `slot`, `hands`, `weapontype`, `levelrequired`, `vocrequired`, `imbueslots`, `upgradeclass`, `immobile`, `walkable`, `pickupable`, `usable`, `rotatable`, `hangable`, `holdsliquid`, `writable`/`rewritable`/`writechars`, `wrappable`, `unshootable`, `lightradius`/`lightcolor`, `mapcolor`, `stackable`, `marketable` and the NPC trade (`npcvalue`, `npcprice`, `buyfrom`, `sellto`). Every value shows its source (client data, derived – review, TibiaWiki, needs your input, entered by you); values the client does not contain are never guessed and required ones are listed empty (optionally `?`). Values are editable, further template parameters can be added in the template's order, the markup updates as you type and is escaped for template arguments (`{{!}}`, stray braces). **Copy markup**, **Save as .txt…**, **Save all selected…** (one file per object, never replacing a file) and **Use TibiaWiki article names**.
* Core: `WikiTemplate`, `WikiTemplateWriter`, `InfoboxObjectBuilder` (reusable for further templates); `WikiItemIdTable.TryReadCache`; tool manifest operations `infobox-export` and `wiki-item-ids`.
* Tests: `InfoboxExportTests` (20, including every object of the installed client and values checked against TibiaWiki), `DataIntegrityTests` (4).
* `docs/26_TibiaWiki_Infobox_Exporter.md`, `TibiaFoundry-9.0-Audit.md`, `TibiaFoundry-9.0-ReleaseNotes.md`.

### Changed

* **One version source**: `src/Directory.Build.props` `<Version>9.0</Version>`; file and assembly versions are derived (9.0.0.0, Windows needs four numbers), `app.manifest` follows (it said 5.0.0.0), the portable publish folder and the User-Agent (`TibiaFoundry/9.0`) use it. Tests check that the public version has two parts.
* **Durable saving** (audit F-18): `AtomicFile.WriteAllText`/`WriteAllBytes` flush the data to the disk before the new file replaces the old one (settings, item catalogs, manifests, collections, workflow files, history). Thumbnails and extracted sounds, which are rebuilt on demand and written by the thousand, skip the flush.
* Release packages are named `TibiaFoundry-9.0-Installer.msi`, `TibiaFoundry-9.0-Portable.zip` and `TibiaFoundry-9.0-Source.zip`; the source package also leaves out `TestResults`, benchmark artifacts and `*.user`/`*.suo`/`*.log` files.
* The Appearances tool description mentions the infobox (all five languages).
* System.Drawing.Common 10.0.12 → 10.0.14 (patch release, matches the .NET runtime). No vulnerable packages (`dotnet list package --vulnerable --include-transitive`).
* UI snapshot (`--ui-snapshot`): a scripted check opens Appearances → Wiki infobox for object 3366 and renders it.
* Website (`website/`, tibiafoundry.com): 9.0 in five languages, a TibiaWiki infobox section with real generated markup, release notes of 9.0, download buttons that point directly to the `v9.0` release files (they pointed to `releases/latest`, which had no release).

### Fixed

* **A backup "move" to another drive** (removing a catalog source, clean-ups) deleted the original before the copy was verified (audit F-05); it now copies, verifies and only then deletes.
* **Numeric fields** stepped two or more times per click after their template was applied again, for example after switching the theme (audit F-13).
* **Appearances preview**: a damaged or locked sprite sheet reached the global error dialog (an `async void` without a handler, audit F-09); it now empties the preview with a message and a log entry.
* `ItemCatalogTests.TheClientsOwnNamesAreVerifiedAndEveryObjectIsListed` compared the installed client with the figures of the pinned reference client when no reference folder was present, and failed after every client update.

## 8.8.2 – 2026-11-02 (Static Data: batch creature GIFs)

### Added

* **Static Data → Monsters / Bosses**: multi-selection (Select all, Ctrl+A, Shift/Ctrl-click) and **Export GIF… (n)** for all selected creatures at once into the GIF folder; **Export all monsters and bosses…** into its `Monsters` and `Bosses` folders. Background work with progress and cancellation, the fixed frame delay option, one attribution per run, never replacing a file (" 2", " 3"…), a clickable folder in the status bar and automatic Asset Library adds. Core: `CreatureGifExport` (planning of unique names, parallel rendering and writing, failures per creature); operation `creature-gifs` in the tool manifest.
* Tests: `CreatureGifExportTests` (10) and `StaticDataBatchGifTests` (real client: selection export and all 1,280 creatures).

### Changed

* Creature names in TibiaWiki article casing in the Static Data list, the preview title and GIF file names ("orc" → "Orc", "demon lord" → "Demon Lord").
## 8.8.1 – 2026-11-02 (performance; interface languages)

A performance release, measured against 8.8.0 on the installed client (15.33.8791c6) before and after every change; the interface-language work below, unreleased until now, ships in it. No tool, setting, file format or output changed. Details and all measurements: [TibiaFoundry_Performance_Audit.md](TibiaFoundry_Performance_Audit.md).

### Performance

* **Loading appearances** allocates 79 MB instead of 1.85 GB (the client's two Latin-1 names made every load take the repair path, which copied each nested message per level) and takes about half the time. The repaired bytes are identical to 8.8.0's.
* **Compatibility check** (at start-up, after a client update, the Compatibility page): 0.8–0.9 s instead of 4.0–4.6 s in the program. The sprite sheets, map images and sounds are checked against one listing of their folder instead of one file-system query per file, and the schema inspection reads nested messages without copying them.
* **Find installation** (first-run setup, Settings → Tibia client): 4.6 s instead of 29 s here, 98 MB instead of 2.7 GB allocated. The search for Qt resource files read 64 KB of every file to look at 4 bytes, and the setup only needs to know whether there is one.
* **Sprite sheets** decode about 30 % faster (the client's bitmap layout is copied row by row): thumbnails, outfits, GIFs, extraction and the Assets Editor benefit.
* **PNG and GIF exports** are about 30 % faster for many small files: the written file is read back through the same handle (opening it again made Windows scan it before the check could run).
* **Asset Library**: rescanning an unchanged folder takes 66 ms instead of 3 s for 12,780 files (the sizes and times of the folder listing are used instead of asking about every file again); first indexing is about twice as fast (files are inspected in parallel, written in file order); searches evaluate their filter once instead of twice.
* **Compare Files** (appearances): 3.8 GB → 257 MB allocated, about twice as fast.

### Added

* `tools/benchmarks`: the workload benchmarks of the audit (reads the installed client only), with an equivalence dump that compares results byte for byte between versions; results in `tools/benchmarks/results`.
* `tools/release/assemble_release.py`: collects the installer, the portable edition and the source under `Release\<version>\` with checksums.
* Tests: `PerformanceOptimizationTests` (8), including the Latin-1 repair against the 8.8.0 algorithm on the installed client's file.

### Changed

* The source package leaves out the top-level `Release` folder.

## Interface languages (since 8.8.0)

The whole interface is available in English, Brazilian Portuguese, Polish, Spanish (Latin American) and German. Guide: [docs/25_Localization.md](docs/25_Localization.md); completeness: [docs/Localization-Completeness-Report.md](docs/Localization-Completeness-Report.md).

### Added

* **Language button** next to the light/dark toggle: a globe with the current language, a menu with flags and native names, keyboard accessible, in both themes and the compact navigation. The language changes immediately without losing the open page or its entries, and is saved in the settings (`"Language"`, default English; unknown values start in English). `--language <code>` overrides it for one run.
* String tables `Strings.{en,pt-BR,pl,es,de}.xaml` (about 5,700 keys each) for every page, dialog, menu, tooltip, status message, tool manifest, script and report; numbers and dates in the language's format; plural forms per language (Polish one/few/many).
* `--localization-report <file>`: coverage per language, missing keys, placeholder problems, missing plural forms, texts equal to English and data-driven texts without an entry.
* Tests: `LocalizationTests` (complete tables, placeholders, plural rules, edge spaces, every key used by XAML and code exists, protected names kept, setting persistence, run-time switching).
* `tools/localization`: scripts that extract texts, export and merge translations and look for translated text used as logic.

### Changed

* The UI snapshot (`--ui-snapshot`) takes `--language` and checks the language button; its checks no longer depend on English text.
* Settings: the category list is a little wider so long German and Polish names fit.

### Fixed

* Texts that were never shown translated because they lived in field initializers, constants and binding placeholders (for example "Ready", the sprites-folder placeholder, built-in theme names) now use the string tables.
* Text that is written into files and read back (attribution sentences and history, PNG `Creation Time`, `"version file"` in manifests) stays English in every language, and comparisons no longer depend on translated text.

## 8.8.0 – 2026-10-31 (Asset Library, Content Studio, processing queue, publishing profiles, clearer screens)

Tibia Foundry becomes a content-production environment for the whole fansite community: a searchable library of every resource, and reusable projects that render and publish galleries and packages for any website. Every 8.7.0 tool, setting, client archive and item catalog is kept and used as it is. Guide: [docs/24_Asset_Library_and_Content_Studio.md](docs/24_Asset_Library_and_Content_Studio.md).

### Added

* **Asset Library** (new group *Content Production*): a local SQLite index (`Library\library.db`) of images, animations and sounds, with thumbnails (nearest-neighbour for pixel art), search across names, IDs, tags, descriptions and custom fields, filters (category, collection, format, client version, source, tag, creator, animation, export status, date added, width), paging and sorting; metadata editing for one or many assets; built-in and custom categories; tags and custom fields; related assets; collections (create, rename, describe, duplicate, delete, export and import as `*.tfcollection.json`); incremental indexing of folders (missing files are marked, never removed; changed files keep their metadata and record their earlier content as versions); "Copy into the library" for files from temporary places; duplicate detection (identical files, the same graphic at another size, visually similar images – report only); integrity verification; backup and restore with validation.
* **Every export of every tool is added to the library automatically** (with the tool and client version), optionally into a collection (Library settings).
* **Content Studio**: content projects (creature gallery, outfit gallery, item image collection, map reference project, fansite update resources, wiki image preparation, custom asset export) with history; a workflow builder (sources, per-image steps, outputs) with templates, export and import of workflow files; a picker for the client's creatures, outfits and named objects; publishing profiles (TibiaWiki, Custom fansite, Gallery website, Article publishing, Personal archive – all editable – and your own) with file-name and folder templates, canvas, scale, format, manifests, credit line and snippets.
* **Processing queue**: background jobs with progress, cancellation, retry, error details, a concurrency limit and a history file; runs are staged and moved into place only when complete.
* Outputs: PNG/GIF with embedded attribution, contact sheets, comparison sheets (addon variants, directions), JSON/CSV/Markdown manifests, self-contained HTML galleries (search, categories, full-size view, metadata, light/dark), text snippets (wiki links, HTML, BBCode…), ZIP packages.
* **New and changed since a version**: renders every object and outfit that is new or whose sprites changed since an older appearances file (update articles). Compare Files → *Visual gallery…* starts it.
* Static Data → *Gallery of the list…*; Asset Library → *Create a project…*; Home: the Content Production group and two workflow entries.
* New dependency: Microsoft.Data.Sqlite 10.0.14 (MIT) with SQLitePCLRaw 2.1.13 (Apache-2.0) and SQLite (public domain); `tools/release/make_notices.py` lists them.
* Settings section `Library` (defaults keep the earlier behaviour plus automatic library adds); 8.7.0 settings load without it.
* Tests: `AssetLibraryTests` (11), `ContentWorkflowTests` (13), `RealClientContentTests` (1, needs the installed client).

### Changed

* **Home**: the client library, recent tools and recent exports cards share one row height and keep their shape when empty (centred empty states with an icon); the page uses more of a wide window.
* **Clients page**: *Client folders* and *Archived revisions* are cards with a header (title, count and the actions; rarer actions in a "more" menu), tables without horizontal scroll bars (the folder path moved to the details), the selected folder's and revision's details as labeled values, and the left-out personal data as a small table instead of lines of text; an empty state when no revision is archived.
* **Importing items** (Item Catalog): the confirmation is a structured report – source, what changes (added, changed, removed, unchanged), the comparison with the client, and the names shared by several identifiers as a scrollable, collapsible table – instead of one long block of text. Nothing was removed.
* **Setup, 2 of 4**: the client's facts are a summary card (labeled values, the folder in a monospaced field, the tools that can use it as badges) above the storage question.
* Report dialogs: tables and lists with more than 12 entries scroll inside their section, so long messages keep a sensible size.
* `ShellService.OpenFile` opens media files with their default program (images, sounds and data files only; never programs or scripts).

### Fixed

* Home: the *Recently used tools* and *Recent exports* cards collapsed to their content and floated at the top of the row when empty.
* Clients page: single-row tables showed a horizontal scroll bar; long status texts were cut without a tooltip.

## 8.7.0 – 2026-10-31 (fansite toolkit: Home dashboard, export presets, pixel-exact enlargement, navigation by task)

Tibia Foundry is now presented as an independent desktop toolkit for Tibia fansite administrators, wiki editors, artists and content creators. No tool, converter, importer, exporter or editor was removed; settings, client archives, item catalogs and exports of 8.6.0 and earlier are used as they are. Guide: [docs/23_Fansite_Toolkit.md](docs/23_Fansite_Toolkit.md).

### Added

* **Home dashboard** (new start page for new installations; an existing start page choice is kept): the toolkit's purpose, every tool by category with what it is for, recently used tools, recent exports (click to show in File Explorer), the client in use with a link to the Clients page, step-by-step fansite workflows, and links to the website, TibiaWiki and the official fansite list.
* **Fansite export presets** for animated GIFs (Appearances → Export): TibiaWiki image, Creature gallery, Outfit gallery, Item icon, Article illustration, High-resolution reference. A preset only fills in the options; changing one shows *Custom* and keeps the change.
* **Pixel-exact enlargement** of exported GIFs: 1×, 2×, 3×, 4×, 6× or 8× with nearest-neighbor sampling (no blur, no new colors, transparency kept); enlarged files end in `_2x`, `_3x`….
* **Sort into Objects, Outfits, Effects and Missiles folders** for automatically named GIFs.
* New settings `GifScale`, `GifKindFolders`, `GifPreset`, `RecentPages`, `RecentExports`, with defaults that keep the earlier behavior.
* Tests: `FansiteWorkflowTests` (enlargement is an exact nearest-neighbor copy, presets, the Home dashboard, recent lists, settings without the new values).

### Changed

* **Navigation grouped by task**: Start, Graphics & Sprites, Game Data, Maps & Houses, Sound & Media, Asset Management, Client Library, Utilities, Application (was: Client data, Tools, Application). References such as "Tools → Scripts" now read "Utilities → Scripts".
* **Descriptions of every tool** rewritten around wiki and fansite work; the About page, README, website and portable readme describe the toolkit's purpose and audience and state that it is an independent community project, not affiliated with or endorsed by CipSoft GmbH or Fandom.
* **Item Catalog**: `items.xml` and `items.otb` files are presented as third-party item data (unverified research leads for naming objects the client does not name). The import no longer suggests server names; you name the source yourself. "Server IDs" are shown as "items.otb IDs". Stored identifiers are unchanged, so existing catalogs open as before.
* **Client Converter**: presented for historical comparisons, legacy-format viewers and identifying old client files; the optional extended attributes are described as being for third-party legacy-format tools.
* **Tibia Project File Auditor**: `items.otb` and `*.otbm` files are listed as third-party item ID mappings and map files.
* The opt-in research test is now `ThirdPartyItemDataAndComparisonTests.RealThirdPartyItemDataReproducesTheResearchFigures`, configured with `TF_OTB_ITEMS_DIR`/`TF_CLIENTID_ITEMS_DIR` (renamed with the old variables).

### Removed

* Nothing from the program. Two "not implemented" rows that only described server-specific formats were dropped from `docs/14_Tools_Settings_and_Scripts.md`.

## 8.6.0 – 2026-10-25 (File Metadata tool, redesigned setup dialogs, Default Live and Test Client, message audit)

Details, the dialog audit and the verification: [V8.6.0_CHANGELOG_AND_AUDIT.md](V8.6.0_CHANGELOG_AND_AUDIT.md). Settings, archives, revisions and file formats are unchanged from 8.5.0; one setting group is added (`FileMetadata.*`).

### Added

* **File Metadata** (Tools; [docs/22_File_Metadata.md](docs/22_File_Metadata.md)): view, edit, compare and batch-edit the metadata of files.
  * Images (JPEG, TIFF, PNG, GIF: EXIF/XMP/IPTC through TagLib#), audio (MP3, FLAC, Ogg Vorbis, Opus, M4A/M4B, WAV, AIFF, WMA/ASF, WavPack), video (MP4, M4V, MOV, MKV, MKA, WebM, AVI, WMV), Word/Excel/PowerPoint document properties, EPUB metadata, SVG title and description, and the ZIP comment are **editable**.
  * PDF, BMP, WebP, ICO and other raster images, camera raw files, TAR archives, Windows programs (version resources, signature presence) and Tibia client files are **read only**, with the reason shown.
  * File system properties (created, modified, accessed, read-only, hidden, archive) for every file, shown apart from the embedded metadata.
  * Add files and folders (with subfolders), drag and drop, search in names and values, format and status filters, sorting, multi-selection; editing several files at once.
  * Batch operations with a per-file preview: set, remove, add/remove/replace list items, copy from a reference file. Presets of values. Comparison of up to 20 files. CSV and JSON reports.
  * Safe saving: validation, refusal of files changed by another program, write to a copy, read-back verification of every changed and unchanged field, optional verified backups (restorable with Scripts → Backup and Restore), one-step replacement, the modified time kept, cancellation between files, a per-file result.
  * New dependency: TagLibSharp 2.3.0 (LGPL-2.1, unmodified): `TagLibSharp.dll` in the folder edition, bundled into the portable `TibiaFoundry.exe`; the source package is published with each release.
  * The self-test (`--self-test`) writes and verifies a JPEG's metadata.
* **Default Live Client and Default Test Client**: `%LOCALAPPDATA%\Tibia\packages\Tibia` and `…\TibiaExternal` (from the Windows profile, never a user name; the packages folder chosen in Settings applies). Setup lists both with their state (available, usable but incomplete, incomplete, not a client, not installed, cannot be read); one that is not usable is shown but cannot be chosen. The labels are used in setup, on the Clients page, in Settings → Tibia client → Check at start and in the Diagnostics self-check.
* A structured message (`DialogReport`, `IDialogService.ShowReport`): heading, summary and sections of labeled values, tables and lists; paths in a monospaced field with a copy button; *Copy details*; resizable; scrolls on small screens.

### Changed

* **Setup, 4 of 4** (*Archive this installation now?*): sections *Client to archive* (client, server, version, what it is, files and size, folder), *Left out: personal data* (a table of categories with files and sizes; empty categories say "none"), *Storage* (mode, space needed, archive folder, free space on its drive) and *If you choose Not now* (what happens, and what the check at the next start does with your step-3 choice).
* **Setup summary** (*Tibia Foundry is set up*): sections *Active client* (client, server, version, status), *Installation* (folder, contents), *Archive and storage* (the actual outcome, including a failure and its reason; storage; personal data; archive folder), *Other default clients* (their real state) and *Next steps*.
* Setup run again never replaces a client you chose before without asking.
* Setup step 2 lists the client's facts one per line.
* Message boxes: *Copy* button (Ctrl+C), resizable, Esc closes information messages. List choices show a group's heading once. Other dense messages became structured: the rebuild confirmation, the inconsistent-snapshot question, unfinished rebuild folders, and "not a client folder".
* Counted nouns read naturally everywhere ("1 file", "3 files", "1 entry"), with verb agreement ("1 article was"), instead of "file(s)" (about 60 messages).

### Fixed

* Setup could not say that the archive step failed in its summary (it showed a separate warning, then "not archived (…)").
* Setup did not say when a default client was missing or could not be used, and named clients only by their folder.

## 8.5.0 – 2026-10-22 (archive confirmation redesign, reliability and message fixes)

Details, root causes and verification: [V8.5.0_CHANGELOG_AND_AUDIT.md](V8.5.0_CHANGELOG_AND_AUDIT.md). Settings, archives, revisions and file formats are unchanged from 8.0.0.

### Changed

* **Archive confirmation** (Clients → Archive…, Check for changes → Archive…): a dedicated dialog replaces the dense text message.
  * Header: *Archive <client>*, with the server, the version and the folder.
  * Summary: files, archive size (as restored), new disk usage (what is written), scope, storage, the space reused from the archive, and the changes against the newest revision.
  * Personal data in its own section, with *Include personal data*. Ticking it reads the folder again (with progress; Archive waits) and updates every figure, so what is archived is exactly what is shown. The privacy note is in the section; the separate second confirmation is gone.
  * *File details* (collapsed): every category with its files, size and new disk usage, marked *Archived*, *Left out* or *Not archived*, and the entries that could not be read.
  * The figures come from a new domain model, `ArchivePreview` (`ClientStore.PreviewArchive`), which uses the same accounting as the free-space check.
* Long message dialogs get a wider box instead of a tall, narrow column.
* Error dialogs show the reason in plain words and point to Diagnostics → Log; the exception type is only logged.
* The rebuild confirmation lists the target and the files on their own lines; its button says what it does (*Rebuild installation* / *Restore folder*).
* Download-limit messages use the common size format (MB) instead of raw byte counts.

### Fixed

* *Check for changes → Archive…* read and hashed every file of the client twice; it now archives the scan it just made.
* The archive's space figures were computed on the UI thread; they are now computed in the background.
* When the client kept changing during three reads, archiving stopped without a word; the status bar now says why.
* `BrandingTests.TheRemovedNamesAppearOnlyWhereTheyMustRemain` failed whenever the repository was not in its original folder: a documentation line named that folder.
* Esc on a two-way import question (Assets Editor *Import objects*, RCC Editor *Update from a folder*, Item Catalog items.xml + items.otb) carried out the second option instead of cancelling.
* Assets Editor: the status after saving was garbled at the drive letter's colon; a failed or cancelled *Save as* left a partial folder that blocked the next save there; *Import from folder* errors were unhandled; image export of an object without frame groups failed.
* Item Catalog: an import with an invalid mapping replaced the records file before failing, leaving the catalog inconsistent.
* The check that no output goes into a Tibia client or the client archive now applies to every output folder, also typed ones (the Client Converter had none).
* *Import settings* and *Reset all settings* changed the client archive location, the client in use and the setup flag.
* Closing the program while an operation ran gave no warning.
* The "not archived yet" notice stayed after archiving from the Clients page.
* Map tools *Build HR map* without an input folder, Diagnostics report preview errors, an unreadable Rebuilder output folder and some sound playback errors reached the global error handler; a failed playback left the file and audio device open.
* Untitled message boxes were titled "Info" or "Warning".

### Added

* The UI snapshot (`--ui-snapshot`) renders the archive confirmation (closed, with File details, at minimum width) in both themes, and renders dialogs without cropping their margins.
* Tests: the preview's accounting (shared, independent, duplicates, already archived, personal data), including personal data from the confirmation, cancelling it, and *Check for changes* reading the client once.

## 8.0.0 – 2026-10-20 (complete client archiving, Rebuilder, check at every start; Item Discovery removed)

The plan of 2026-10-20 (`05_verification/8.0.0/plan/`) was approved with all recommended defaults (D1–D14), for phases 0–6 and 8. Phase 7, converting existing revisions between storage modes, was not approved and is not included. 7.3.0 is preserved in `15_baseline_7.3.0/`.

### Added

* **Complete archives** (`docs/21_Client_Archiving_and_Rebuilder.md`): an archive keeps the whole installation:
  * every file at any depth, hidden and unlisted files, empty folders, times and attributes;
  * personal data are left out by default, visibly and with counts, and the revision is then labeled "client content complete — personal data left out", never "complete installation";
  * a scan is accepted only when every file was read, matches the client's own lists, did not change while it was read, and a second listing equals the first.
* **Two storage modes:** space-efficient (each content once, the default) or independent copies (a read-only physical copy per revision). Every copy is checked before a revision is published; free space is checked first. Settings → Tibia client chooses the mode for future archives.
* **Client folders from anywhere:**
  * *Find installation* (the packages folder, Tibia's uninstall entries, Steam libraries, registered folders);
  * *Browse folder…* for an installation, a copied client, a rebuilt installation or an assets folder;
  * each folder is classified (complete installation, standalone client, assets only), with the tools that can use it and version evidence (verified, conflicting or unknown). A loaded folder is used where it is and only read.
* **Client Installation Rebuilder** (Tools): rebuilds a complete installation, or restores any other revision as the folder it was, into a new or empty folder. It never overwrites: files are written next to the target, verified, then renamed into place. Interrupted rebuilds are cleaned up on request. A report goes to Reports and always says "Runnable: not checked". It replaces *Export…*.
* **The check at every start** of the installed clients, in the background:
  * Full (every file, in the background with two threads: 5.7 s live and up to 39 s test on this PC when the files are not cached) or Quick (files whose size or time changed, 0.5 s; it can miss a change that keeps both);
  * it waits while the Tibia launcher runs; a check that saw files change is not accepted;
  * when the content is not archived: *Ask me* (default: a notice with Archive, Later, Don't ask for this version), *Archive it automatically*, or *Only show it on the Clients page*.
* **First-run setup** for new installations (client, storage, what happens when it changes, archive now, summary), never for upgrades.
* **At start:** *use the remembered client* or *ask which client* (with Remember my choice). A recovery dialog appears when the remembered client cannot be used: the archived revision of the same version, Locate, Browse, or another client; nothing is discarded.
* **Settings → Tibia client:** storage mode, personal data, check at start (live, test, Full or Quick), when it changed, at start, the remembered client (Change…, Clear), *Browse for a client folder…*, *Run setup again…*.
* **Compare** reports moved and renamed files, and only compares what both revisions cover.
* **Storage figures per revision:** size as restored, what only it uses, what it shares. Revisions being read are leased and cannot be removed.
* **Diagnostics → Client archive:** the archive, damaged revisions, unfinished rebuilds, and the last check of each installed client.

### Changed

* **The Clients page** lists client folders and archived revisions with their scope, storage, completeness and version evidence.
* **Switching clients:** every page was checked to drop the previous client's files; Compare Files and the Client Converter now follow a switch. A switch waits while an operation runs.
* **Manifests are format 2**, and 8.0 reads 7.3's format 1. The registry stays format 1 with optional fields, so 7.3 still reads it. **Settings are format 7.** The previous file is kept as `settings.json.v6.bak`.
* **The target framework is `net10.0-windows` again** (7.0–7.3 needed `10.0.19041.0` only for Item Discovery's text recognition).
* **The editors' protection** of client folders and the archive no longer depends on a global list, which fixes a race between parallel tests.

### Removed

* **Item Discovery**, its page, settings, OBS connection, hotkey writes, text recognition, documentation (`docs/19`) and end-to-end harness, together with the 48 reverse checks that tested it.
  * Catalogs that hold records it collected still open, and those records keep their source.
  * If copies of the client's options file that it made were never put back, they are reported once (status bar and Diagnostics) and never touched.
  * A new check (`tools/verification/removed_feature_search.py`) finds no unexplained trace.
* **The 7.3 quick look and reminder**, replaced by the check at every start (their two reverse checks, H90 and H91, are retired; 16 new ones, H96–H111, cover 8.0).

### Not changed

* The installed clients are read exactly as before while no other client is chosen. Revisions archived by 7.3 keep their scope ("Tibia Foundry files only") and work as before.
* Tibia client folders are only ever read. Everything stays on this PC.

## 7.3.0 – 2026-10-16 (client management, deduplicated client store)

The client management plan of 2026-10-16 (`05_verification/7.3.0/plan/`) was approved, with your decisions on the store locations, local-only operation, RCC files and the launcher, for its phases 0–4. 7.2.0 is preserved in `14_baseline_7.2.0/`.

### Added

* **The Clients page** (Application → Clients, `docs/20_Client_Management.md`):
  * the installed clients (automatic, as before), registered client folders anywhere on the PC, and imported versions, for the live server and the test server;
  * *Register a folder…*, *Import*, *Rescan*, *Use this client*, *Rename…*, *Locate again…*, *Export…*, *Remove…*, *Verify store*, *Reclaim space…*, *Move store…* (folder edition);
  * a line under the server switch shows the client in use.
* **A content-addressed client store:**
  * every file content of every imported version is stored once (`Shared_Assets\Objects`, named by its SHA-256, original bytes), and each version has a complete manifest with no base version;
  * RCC files are found from the installation (`package.json` and the `qres` header) and shared like every other file;
  * the two installed clients take 264.8 MB instead of 474.1 MB.
* **Where the store is:**
  * portable: `TibiaFoundry-Data\Clients`, relative paths, so the folder can be moved;
  * folder edition: `%LOCALAPPDATA%\TibiaFoundry\Clients`, or a folder you choose. Every release unzips into a new folder, so the program folder would lose the store at each update.
  * Local only: no network code, nothing uploaded or synchronized.
* **The launcher:**
  * the installed clients keep following it; imported versions never change;
  * a changed installation is noticed, summarized ("229 sprite sheets replaced, …") and offered for import;
  * a reminder (on by default, a status message, never a dialog) says when the installed version is not stored yet, with *Import it now*, *Not now* and *Turn reminders off*.
* **Settings format 6** adds `ActiveClientLive`, `ActiveClientTest`, `ClientStoreFolder` and `ClientImportReminders`; 7.2 keeps them.

### Changed

* **The readers accept an imported version** through `IClientFolder`: `AssetCatalog` (sheets), `TibiaClientPaths` (data files from the catalog), `SoundDatabase`, map tile extraction, the map and sound checks, and the compatibility check. `LocationResolver` has an imported version as a fourth source.
* **The audio player** recognizes OGG and WAV by their header, so stored sounds without a file name play.
* **The editors' "never write into a client" rule** also covers the client store and registered folders.
* **The Assets Editor and the Client Converter** explain how to export an imported version when one is in use.
* **The RCC Editor** opens the version's RCC file and suggests its real name for *Save as*.

### Not changed

* The installed clients are read exactly as in 7.2 while no other client is chosen; nothing is created until you register or import something.
* Item Discovery keeps using the installed client's options file and screenshots.

## 7.2.0 – 2026-10-16 (item renames, consolidations, OBS, automatic hover)

The investigation of 2026-10-15 (item name editing, duplicate functionality, automated OBS recording; `05_verification/7.2_phases_0-3/`) was approved for its phases 0–3 (a baseline, renames in the Item Catalog, consolidations, and the OBS connection with an OBS capture source for the guided mode) and, on 2026-10-16, for phase 4 (automatic hover), before the plan's live tests A1–A5. 7.1.0 is preserved in `13_baseline_7.1.0/`.

### Added

* **Renaming in the Item Catalog** (`docs/17_Item_Catalog.md` §6): any identifier, in place in the list (F2, a double-click on the name, or the list's menu) or with *Rename…* in the details. A rename is kept as your decision, so no source and no other record changes; *Restore original name* removes it. Renaming an object the client names itself asks once. *Rename…* and *Restore original name* replace *Another name…* and *Withdraw my decision*, which were only shown when sources disagreed.
* **Item Discovery: capture source "OBS (Game Capture)"** (`docs/19_Item_Discovery.md` §4): the client's window reports display affinity WDA_MONITOR, so Windows' own capture (the *Screen* source, OBS's Display and Window Capture) shows it without content. Tibia Foundry now asks OBS, through OBS's WebSocket server, for a picture of its Game Capture source of the client while you rest the pointer on a button, and reads it like a screen capture. OBS's password is asked when connecting and kept in memory only. Tibia Foundry asks OBS for its version and pictures only; it attaches nothing to the client. *Test reading under the pointer* shows OBS's picture, so you can see whether the tooltip is in it.
* **Item Discovery: use a hotkey set already in the client** (§3), for example sets you filled with object IDs yourself: the options file is only read and nothing needs restoring; the objects the client names are the control objects.
* `ObsWebSocket` (`Core/Net`): a connection to OBS's WebSocket server (obs-websocket 5) with the recording's requests and events, tested against a stand-in OBS. The guided mode uses none of the recording requests.
* **Item Discovery: automatic hover** (`docs/19_Item_Discovery.md` §4, page step 5; OBS source only). After you calibrate the action bars once (rest the pointer on the first and last visible button of each bar and press the capture key; the buttons are recognized by their icons), Tibia Foundry places the pointer on each button of the session's hotkey set in turn, waits (settle 150 ms, dwell 1,200 ms), reads the tooltip from OBS's picture and records the result per button (`hover.jsonl`). It only places the pointer with `SetCursorPos`, in `Services/Automation/PointerMover.cs` alone; nothing in the program can click or type (architecture test, reverse check H74). It pauses at once when you move the mouse, another window comes to the front, the window moves or changes size, 3 icons in a row are wrong, OBS stops or the recording folder runs out of space; the stop key (Pause) stops it. It can start, pause and stop OBS's recording (refused if OBS overwrites files, unless you allow it), and suggests the next numbered set. A test run of 10 buttons comes first. Tested against a simulated client; the live tests A2–A4 are open.
* Settings → Tools → Item Discovery: seven *Automatic hover* settings (dwell, settle, control objects per set, stop key, record with OBS, stop recording at the end, minimum free space).

### Changed

* Item Discovery's capture log and the new hover log share one JSON-lines writer and reader (an unfinished last line is still reported and skipped).
* Names you type (a rename, *Use this name*, Item Discovery's *Correct…*) follow one rule: surrounding and repeated spaces are removed; empty names, line breaks and other control characters, and names longer than 100 characters are refused with the reason.
* Adding a record to a catalog (a rename, a decision, a name read by Item Discovery) builds only that identifier's entry again: 15 ms in a catalog of 150,000 records instead of a full rebuild.
* **One set of Windows name rules** (`Core/FileOps/WindowsNames.cs`) for file names, output folders, paths from archives and manifests, and item catalog folders. They were written three times with different lists. `CONIN$` and `CONOUT$` now count as device names everywhere, and a catalog named like a device ("CON", "NUL"…) gets a usable folder (`_CON`).
* **One write probe** for folders (`OutputLocations.CanWrite`): the start-up check of the program folder, Settings and the self-test each had their own.
* Settings: *All log files together at most* is shown under *Notifications and logging*, next to the other two log limits (it was under *Diagnostics*), and is reset with them. The setting itself is unchanged.
* Compare Files and Item Catalog → Compare say what the other compares; the Appearances ID and name table says that it holds the client's own names.

### Removed

* Seven public methods nothing called, among them the direct download of TibiaWiki's "Item IDs" page, which the wiki can no longer build (7.0.1 reads the item articles instead).

## 7.1.0 – 2026-10-15 (Item Discovery from client screenshots)

The research of 2026-10-15 (`05_verification/item_discovery_research_2026-10-15/Item_Discovery_Research_Report.md`) found that the client's program can keep its window out of screen captures, so Item Discovery's screen capture is probably black on the real client. This version implements the plan's steps H.1–H.5 approved by the owner. 7.0.1 is preserved in `12_baseline_7.0.1/`. Nothing reads or changes the client process, its memory or its traffic.

### Added

* **Capture source "Client screenshots"** (Item Discovery, step 1 of the page and Settings → Tools; `docs/19_Item_Discovery.md`): you press the client's own screenshot key while a tooltip shows; Tibia Foundry reads each new *Hotkey* screenshot from the client's screenshots folder (read only; screenshots the client takes by itself are ignored), finds the button from where the pointer was at the screenshot's moment (any display scaling), and reads it as a screen capture. A screenshot is not used when the pointer was not over the client or had not rested for the rest delay. The default stays *Screen*.
* **Black captures are named**: a black capture of the client's window says that the client hides its window from screen capture and to use client screenshots (in *Test reading under the pointer* and while collecting); Diagnostics → Self-check shows the capture source and whether the screenshots folder exists.
* **Client screenshots folder** as a client location (`docs/18_Client_Locations.md`).
* **CSV lists without a header** (Item Catalog → Import → From a file…): name,id or id,name lists such as the market tracker's `items.csv`, whose names hold unquoted commas; you choose which value is the ID after seeing the first lines (`docs/17_Item_Catalog.md`).

### Changed

* Captures record their source, and names read from client screenshots are recorded with their own method (`ClientScreenshot`); logs and catalogs of 7.0 read unchanged. 7.0.x skips catalog records of the new method as damaged.
* The Tibia client is recognized by its program path asked with limited query rights; 7.0 asked for access to the process's modules and memory (refused for the running client, so it was treated as the client anyway).

### Verification

* 458 tests: 450 passed, 0 failed, 8 skipped (the opt-in checks), including the full sprite comparison. 13 tests are new.
* 118 of 118 reverse checks detected; the 11 new ones (H43–H53) guard the 7.1 changes.
* Published builds: self-test 12/12 and UI checks 44/44 for both editions (also in a small dark window), with 0 binding errors. The Item Discovery end-to-end check with the simulated client passes 62 of 62. Settings move between 7.0.1 and 7.1.0 in both directions without loss.
* While the release was made, rewriting `dist/SHA256SUMS.txt` in place also changed the 7.0.1 baseline's copy, because the two files are hard-linked. The baseline copy was restored to its recorded bytes, and every baseline was checked again. Details are in `docs/07_Testing_Report.md`.

### Not verified

* On the real client: whether its screen captures are black, and whether its screenshots show the tooltip (the research report's proof-of-concept steps 0 and 1, in your session).

## 7.0.1 – 2026-10-15 (TibiaWiki item IDs)

### Fixed

* **Importing TibiaWiki's item IDs failed** (Item Catalog, and *Generate Lua data for all NPCs*). Since 2026-10-15 the wiki cannot build its "Item IDs" page within its own time limit: every request ended after 16 s with "503 Backend fetch failed", and the old fallback address answers programs with a browser check. The list is now read from the item articles the page is made of, through the same documented API: about 190 requests of 50 articles, about a minute, with progress in the status bar. The result is the page's table: on 2026-10-15 it was identical to the page as downloaded the day before (13,875 IDs, the same names, the same IDs claimed twice and the same irregular entry). The page itself is still asked when the articles cannot be read, then the old address; a cached copy is used when none of them works.
* When TibiaWiki does not answer at all (no connection), the other addresses on the same site are no longer tried and waited for one after another.
* A server's Retry-After no longer shortens the waits between attempts: TibiaWiki's 503 says 5 s, so the three attempts at the page came 5 s apart instead of 20 and 40 s.
* The error message names each address that failed in words ("item articles", "Item IDs page") instead of internal names.

### Verification

* A check that repeats the failed import on the published program against the live TibiaWiki (`tools/verification/tibiawiki_import_check`).
* The end-to-end check of Item Discovery looked for "ERROR" in the program's log, which writes "[Error   ]", so its log step could not fail; it now finds errors. The 7.0.0 run's log has none (checked by hand).
* The program's UI check (`--ui-snapshot`) chose an Item Discovery batch before its own catalog had loaded when the user already had a catalog open, and failed; it now waits for its own catalog.

Settings, catalogs and every other feature are unchanged from 7.0.0.

## 7.0.0 – 2026-10-15 (client locations, Item Catalog, third-party item data, Item Discovery)

Every client file can be located by hand, a new Item Catalog keeps item names and IDs from every source apart, third-party `items.xml` and `items.otb` item files can be imported and compared, and Item Discovery is a guided proof of concept for reading the names the client does not have in its files. Details are in `docs/17_Item_Catalog.md`, `docs/18_Client_Locations.md` and `docs/19_Item_Discovery.md`. 6.0.0 is preserved in `11_baseline_6.0.0/`. All output that matched the original program still matches.

### Added

* **Client and data locations** (`docs/18_Client_Locations.md`):
  * every file and folder Tibia Foundry reads from the client (packages and package folder, version file, assets folder, the four data files, sounds folder and file, resource file, extracted sprites, and the new client options file) can be located by hand, separately for the live client and the test server;
  * *Locate…* / *Change…* and *Reset to automatic* on the Compatibility page: a new location is checked first, saved, and applied at once; only the affected components are checked again;
  * Settings → Tibia client lists every location with its path and how it was found;
  * a choice that is unavailable (a disconnected drive) is kept, reported, and used again when it is back.
* **Item Catalog** (Client data; `docs/17_Item_Catalog.md`): a database of item names and IDs:
  * sources kept apart: the client's own data (official), TibiaWiki's item IDs (community; article titles are marked as titles), `[id] name` lists, CSV, catalog JSON, third-party item files, names read in the client, and your decisions;
  * identifier types are never mixed (Tibia object IDs, OTB server and client IDs, legacy Tibia.dat IDs);
  * every identifier settles on a name only with evidence (official, chosen by you, sources agree, one source), conflicts are listed and never merged, and every name shows its sources;
  * imports are previewed and replaced records are backed up; records added one by one survive an interruption;
  * search by ID, range, exact or partial name; filters by agreement, source, unnamed in the client, differing names;
  * exports: name list, CSV, records CSV, Excel workbook (XLSX) and JSON (importable);
  * coverage per source and relative to the client.
* **Third-party item data**: `items.otb` (with its version, its own IDs → client ID mappings, groups and flags) and `items.xml` (with either ID convention, read as the format intends; DOCTYPE refused). Files are your own copies, are never bundled, and their names stay unverified research leads.
* **Comparing** (Item Catalog → Compare): two sources, two catalogs or the settled names; added, removed, renamed, named, unnamed, category and property changes, server IDs that moved to another client ID, client IDs claimed twice, and the behavior-sensitive objects (containers, doors, teleports, depots, beds, floor changes, …) marked. Server IDs are compared with client IDs only through an `items.otb` mapping; treating its client IDs as today's object IDs is an assumption the result states. Results save as XLSX, CSV or text.
* **Conflict review**: *Use this name*, *Another name…* and *Withdraw my decision*, kept as your own records.
* **Excel workbooks** from a built-in writer (no new package): numbers as numbers, bold frozen header, filters, an About sheet, never formulas.
* **Item Discovery** (Client data; proof of concept, guided only; `docs/19_Item_Discovery.md`):
  * puts objects the client does not name on the action bars of a hotkey set while the client is closed, after an exact backup, and restores the options file byte for byte afterwards (every write is refused when it is not safe);
  * while you hover the buttons in the game, recognizes each object by its icon and reads the tooltip (UI Automation, then Windows text recognition with three readings and a vote); control objects check the reading;
  * names go to an item catalog at once, unverified until you confirm or correct them; evidence images are kept;
  * a measured report with the proof of concept's success criteria;
  * a batch that may still be in your hotkeys after a crash is reported at the next start.

  It never moves the pointer, clicks or types in the client, and never changes the client program or its connection. An automatic mode is not built.
* **Diagnostics → Self-check**: chosen client locations, and a discovery batch that was not restored.
* **Verification**: an end-to-end check of Item Discovery that drives the program through UI Automation against a simulated client (`tools/verification/item_discovery_e2e`), and a test that reads the client's own icons on painted action bars through the collector.

### Changed

* **Target framework** `net10.0-windows10.0.19041.0` (Windows 10 SDK projection, for Windows text recognition); everything but text recognition still runs on earlier Windows 10 versions (`SupportedOSPlatformVersion` 10.0.17763.0). No package was added.
* **Settings format 5**: chosen client locations (`ClientLocations`). The live client's assets folder stays where 6.0 reads it. Upgrading gives the test server the assets folder that was chosen for both clients; 6.0 can still read format 5.
* The client version, the sounds and the resource file follow the chosen locations; the live client and the test server keep separate assets folders.
* New output kind `Item_Catalogs` under `Documents\Tibia_Foundry`.
* TibiaWiki downloads retry three times, 20 and 40 seconds apart.
* The client folder is never written to, with one exception: Item Discovery's batch in the client's options file, only while the client is closed and after an exact backup.

### Fixed (found during the 7.0 work)

* The client version, the sounds and the resource file always came from the installed client, also when another assets folder was chosen; a chosen client could get the wrong version.
* One assets folder was shared by the live client and the test server.
* After switching between the live client and the test server, or after a client update, pages could keep using the other client's file, and the compatibility report of the other client stayed visible.
* Defects found and fixed in the new code before release: a catalog source replaced while its records were still being read lost them; a record appended after an interrupted line was unreadable; a JSON file of another program could be mistaken for a catalog export; the UI check selected the wrong tab and still passed; a batch refused because the options file changed meanwhile left a session that blocked the next batch; a batch chosen but not yet written was lost when the Item Discovery page was opened again.
* Found by the end-to-end check and the measurement on the client's own icons (`docs/19_Item_Discovery.md` §6): collecting failed on every tick when it started with the pointer on the screen's left or top edge; a screen that cannot be read (for example while Windows is locked) raised an error instead of a message; icons starting on an odd pixel were missed among similar ground tiles; sparse icons were not refined; sprites larger than a button made of thin lines could not be recognized (they are now left out of batches); sprites that are not square were recognized only when drawn with their proportions kept (now also when stretched to the button).

### Not included

* An automatic mode for Item Discovery (only after the guided proof of concept meets its criteria, and after asking again).
* TibiaData or other online services (they have no item data).

## 6.0.0 – 2026-10-13 (themes, output folders, export messages, tool registry, diagnostics)

Custom themes, one output folder structure under Documents, export messages that name and link the saved file, a manifest-based tool registry and a Diagnostics page; the earlier product name and the original developer's name were removed from everything the project controls. Details are in `docs/15_Themes_Outputs_Tools_and_Diagnostics.md` and `docs/16_Adding_a_Tool.md`. 5.0.0 is preserved in `10_baseline_5.0.0/`. All output that matched the original program still matches (the full 223,694-sprite comparison passes), and no package was added or changed.

### Added

* **Settings → Appearance & Themes**:
  * the built-in System, Light and Dark themes, plus custom themes the user creates, names, saves, renames, deletes, exports and imports (`.tftheme.json`); built-in themes are protected;
  * a color editor by group (backgrounds, text and links, accent and buttons, hover and selection, borders, inputs, status colors, tooltips) with a hex box, hue, saturation and lightness sliders, *Reset this color* and *Reset all colors*;
  * derived colors (hover, pressed, selection, links, borders, soft backgrounds) follow the colors they are made from unless they were changed themselves;
  * live preview on the whole interface, with *Save theme* and *Revert*, and a question before unsaved changes are left;
  * readability warnings (WCAG contrast ratios, distinguishable states) as colors change;
  * *Restore original appearance*;
  * three new palette colors with their previous values: button text, input background and input text.
* **One output root**: `Documents\Tibia_Foundry`, resolved through Windows (a redirected or OneDrive Documents folder is followed). It has one subfolder per kind of output (`GIF_Exports`, `Image_Exports`, `Sprite_Exports`, `Sound_Exports`, `Map_Exports`, `Data_Exports`, `RCC_Exports`, `Client_Converter`, `Assets_Editor`, `Scripts`, `Reports`, `Manifests`, `Backups`, `Logs`):
  * folders are created only when something is saved into them;
  * folders are checked before writing, and problems are explained (drive missing, no permission, invalid name, a file in the way); the user is asked for another folder instead of a silent fallback;
  * each kind can be changed, reset and opened in Settings → Files and folders, and is marked default, chosen, or kept from 5.0.
* **Export messages**: "✓ Saved to <file>" after a successful export only. The path is a link: it shows the full path as a tooltip, is shortened in the middle when long, and opens File Explorer with the file selected (`SHOpenFolderAndSelectItems`, no command line). If selecting is not possible, the folder is opened, and a failure to open it is reported separately. Partial batches are warnings, and failures are errors. This applies to every export, the tools, scripts and reports.
* **Tool registry**: every page and script is described by a manifest:
  * identity, version, type and navigation;
  * capabilities per operation, inputs, outputs and the default output kind;
  * declared settings with validation, defaults, reset and migration;
  * required and optional dependencies, platform requirements, license and attribution.

  The Cancel button appears only for operations that can be cancelled safely. Dependencies are checked before an operation starts and shown in a banner. Settings → Tools generates the controls for each tool's settings. Invalid manifests are rejected without stopping the program. All 16 pages and 12 scripts are registered, including the RCC Editor, Client Converter, Assets Editor and Scripts. A guide with a worked example: `docs/16_Adding_a_Tool.md`.
* **Application → Diagnostics** (also from the status bar):
  * **Operations**: running and recent operations with real progress, an estimate of the remaining time only when it can be measured, Cancel when safe, output and log, and a history across restarts.
  * **Performance**: process measurements, taken only while the page is open, which can be turned off.
  * **Log**: a structured log with filters by period, level, tool, operation and text.
  * **Self-check**: checks settings, folders, tools and dependencies, and repairs only after confirmation.
  * **Tools**: every registered tool with its state.
  * **Report**: a sanitized diagnostics ZIP in `Reports`, previewed in full before saving and never sent anywhere. Full paths are included only by an explicit, confirmed opt-in.
* **Logging**: the levels Debug, Info, Warning, Error and Critical; tool and operation scopes; secrets masked; repeated messages collapsed; retention by age and total size (new setting, 200 MB); a session past midnight continues in the next day's file.
* **Self-test**: a 12th check, the application self-check.
* **Settings → Tools** and **Settings → Diagnostics** categories.

### Changed

* **Removed names**: the earlier product name of versions 1.x–2.x and the original program's developer no longer appear anywhere the project controls:
  * the interface (including Settings → Advanced), messages and migration notes;
  * defaults and file names;
  * comments, tests, fixtures and scripts;
  * build and packaging files, the README, the CHANGELOG and the documents.

  Older settings are still found and migrated through one internal identifier. `docs/13_Branding_and_Attribution.md` §10 lists where the names must remain and why.
* Settings format 4. Settings of 5.0 are upgraded on the first start, and nothing is lost:
  * a default output folder that was still `Documents\TibiaSprites` becomes `Documents\Tibia_Foundry`, and folders the user chose stay;
  * existing 5.0 backups stay where they are and remain restorable;
  * no file is moved.

  5.0 can still read format 4.
* Installed edition: logs and backups now default to `Documents\Tibia_Foundry` (portable: unchanged, next to the program).
* The pages' title, icon, group and description come from their manifests; the page templates are registered from the registry.
* The Client Converter's options are declared tool settings (same keys as in 5.0).
* The diagnostics of Settings → Advanced moved to the Diagnostics page.
* Export destinations never start in or accept the installed client's folders (LZMA decoder, map, sound and sprite extraction, Advanced Tools).
* Command-line options are logged by name only.

### Fixed (found during the 6.0 work)

* An Assets Editor message claimed that object packages can be read by another editor; this was never verified, so the claim was removed.
* A relative folder path was made absolute (against the program's folder) before it was checked; it is now refused as typed.
* A diagnostics report could not be written when the saved window position was not a number; the report now records it as such.
* *Reset all settings* would also have deleted custom themes; it now keeps them (and the creator name) and activates the default theme.
* While the Diagnostics page was hidden, operation progress still refreshed it; refreshes now happen only while it is visible and are throttled.
* `--ui-snapshot` runs, which use their own temporary settings, still wrote their log to the installed edition's log folder and their operation history next to the user's settings. Both now stay in the snapshot's temporary folder, and the history is always kept next to the settings file in use.
* `--self-test` listed its own read-only mode as a configuration warning ("settings changes are not saved"). That warning now appears only when the user turned saving off.
* Tests: the repository-wide name search could not read files that another process was still writing (a console capture). It now reads with shared access. Two migration tests carried the old identifiers in their test names; they now take a version number or a placeholder.

## 5.0.0 – 2026-10-13 (tools, settings, scripts and file safety)

Four new tools, a categorized Settings page, built-in file scripts and shared file-safety infrastructure. Details are in `docs/14_Tools_Settings_and_Scripts.md`. 4.0.0 is preserved in `09_baseline_4.0.0/`. All output that matched the original program still matches (the full 223,694-sprite comparison passes).

### Added

* **Assets Editor** (Tools): opens a modern client assets folder (or the installed client's) and edits every object, outfit, effect and missile:
  * every appearance field (generic property editor over the full schema), frame groups, animation and sprite layout;
  * new, duplicate, delete, change ID, revert; copy and paste flags;
  * replace a sprite everywhere or give one cell a new sprite; add sprites; sheet browser with *find sprite* and sheet export;
  * object packages (`.aec`), import from another assets folder, PNG/GIF image export with attribution;
  * *Save* (backs up what it replaces) and *Save as*. Both are validated, and the installed client is never changed in place.
  * Unchanged objects, the catalog and re-encoded sheets stay byte-identical.
* **RCC Editor** (Tools): Qt resource files (formats 1–3), such as the client's `graphics_resources.rcc`:
  * browse, filter and preview;
  * replace (also by drag and drop), add, rename, remove;
  * extract with folders and a manifest; update from an edited folder; new file from a folder;
  * save with validation against a port of Qt's lookup, and a backup of the previous file.
* **Client Converter** (Tools): modern assets → legacy `Tibia.dat` + `Tibia.spr` for Client 11 or Tibia 10.98 (custom signatures possible). Options: transparency, empty sprites, market name length, newer market categories, extended attributes for third-party legacy-format tools, 32×32 slices. Every lossy step is counted in the result, and the written files are read back and checked completely. Existing legacy files can be identified (8.60, 10.98, Client 11).
* **Tools → Scripts**: 12 built-in scripts:
  * Duplicate File Finder, Directory Comparison, Batch File Renamer;
  * File Hash and Checksum Calculator, File Inventory and Manifest, Large File Finder;
  * Empty Files and Folders, Batch Image Converter and Resizer, Archive Contents Inspector;
  * File Format and Metadata Inspector, Backup and Restore, Tibia Project File Auditor.

  They have categories, search, input validation, preview-then-apply with confirmation, progress and cancellation, and a clearable history (metadata only). Scripts never run by themselves, there is no user code, and nothing is deleted: files a script removes go into a backup set.
* **File safety for every tool**:
  * the existing-file policy: Ask, Keep both, Replace or Skip;
  * protected input files;
  * write to a temporary file → validate → replace;
  * SHA-256-verified backups with restore and retention;
  * per-operation temporary folders;
  * path-traversal and link protection for paths that come from data.
* **Settings, in categories**: General, Appearance, Tibia client, Files and folders, File safety, Performance, Attribution, Notifications and logging, Scripts, Advanced.
  * New options: start page, remembered folders, reopen last files, interface size (80–150 %), navigation bar mode, window placement, preview animation, thumbnail size, per-tool folders, backup/temp/log folders, file-safety options, concurrent operations, worker threads, sprite cache, stop on error, taskbar progress, preview size limit, completion notices, log level/retention/size, and script options.
  * Every option applies immediately. Every category can be restored on its own. All settings can be exported and imported.
* **About**:
  * the tagline "Tibia Foundry - All your Tibia-Tools for TibiaWiki" (also next to the logo in the navigation header), with "TibiaWiki" linking to https://tibia.fandom.com/wiki/Main_Page;
  * the site icons of TibiaWiki and tibia.com on the creator links;
  * a separate **Open-source acknowledgments** section (Nekiro's RCC Editor; Arch-Mina's ClientConverter and Assets Editor), with a no-endorsement notice.
* **`THIRD-PARTY-NOTICES.txt`** in both editions: the license texts and notices of every bundled component (`tools/release/make_notices.py`).
* **Self-test**: two new checks, sheet encoding (BMP + LZMA) and the client's Qt resource file (zlib).

### Changed

* About → Creator: the labels are now **TibiaWiki Profile** and **Tibia Character**, and the redundant second *TibiaWiki* link was removed. The credits are unchanged: exactly Tibia, CipSoft and TibiaWiki.
* The navigation header shows the tagline instead of "Tibia client data for TibiaWiki". With compact navigation, the logo shows it as its tooltip.
* Settings format 3: the format-2 file of 4.0 is upgraded on the first start, and nothing is lost.
* Log level, retention and size per day are now settings (defaults as before: Info, 14 days, 20 MB).
* American English throughout comments, messages and labels (for example *color*, *gray*, *behavior*, *recognized*).

### Fixed (found during the 5.0 work)

* The header tagline wrapped in the middle of "Tibia-Tools"; it now breaks after "All your".
* The Assets Editor's properties panel was cut off at the default window size; its columns now share the width.
* The RCC Editor showed a green check next to "No file open".
* A reverse check showed that a damaged `.rcc` file whose entries Qt cannot look up, and saving assets into the installed client, were not covered by tests. Both are now tested (`ValidatorFindsFilesQtCannotLookUp`, `SavingIntoTheInstalledClientIsRefusedBeforeAnythingIsWritten`).
* View-model tests no longer use the user's real data folders for backups, temporary files and script history.

### Licensing

No code from Nekiro's RCC Editor (MIT declared in its `package.json`), ClientConverter or Assets Editor (no license published) is included. All features were implemented from knowledge of the file formats. No NuGet package was added, removed or updated.

### Tests

* 318 tests with `TF_FULL_SPRITES=1`: 316 passed, 0 failed. Skipped: the opt-in live wiki check, and the opt-in reference comparison, which was run separately with a ClientConverter 15.30 conversion and passed (98.02 % identical attributes, with the differences explained).
* **Reverse checks**: 43 of 43 detected (17 audited defects, 10 attribution safeguards, and F01–F16 for the 5.0 safeguards).
* **UI checks** of both published editions: 31/31 window checks, including the tagline, the site icons, the acknowledgments, Settings category navigation, the navigation-bar mode and the interface size; 0 binding errors in 15 pages × 2 themes. Self-test 11/11.
* Settings move between 4.0 and 5.0 in both directions without loss (checked with the published builds). Start-up time and memory are unchanged.

### Not included (see `docs/14_Tools_Settings_and_Scripts.md` §11)

Lua or user scripts; legacy dat/spr editing, the OTB editor and OBD import; writing 8.60 files; the `itemFlag.otml` sidecar; Zstandard decompression in the RCC Editor; RAR and 7-Zip archives.

## 4.0.0 – 2026-10-12 (Tibia Foundry: new name, credits and clearer text)

Tibia Forge is now **Tibia Foundry**, created and maintained by RockyLaRo (TibiaWiki Admin). Details are in `docs/13_Branding_and_Attribution.md`. 3.0.0 is preserved in `08_baseline_3.0.0/`. All output that matched the original program still matches.

### Changed

* **Name and identity**:
  * window title, navigation header, About, messages, reports, self-test and User-Agent now say Tibia Foundry / TibiaFoundry;
  * `TibiaFoundry.exe` (file description "Tibia Foundry", company RockyLaRo, version 4.0.0);
  * projects and namespaces `TibiaFoundry.*`.
* **Attribution text**: "Created by <name> using TibiaFoundry for TibiaWiki @ https://tibia.fandom.com/". The XMP namespace is `urn:tibiafoundry:attribution:1.0/` and the PNG history chunk is `TibiaFoundry History`.
* **Folders**: `%APPDATA%\TibiaFoundry`, `%LOCALAPPDATA%\TibiaFoundry`, portable `TibiaFoundry-Data`, marker `TibiaFoundry.portable`, sprite folder manifest `TibiaFoundry-sprites.json`.
* **About → Credits** now lists exactly three organizations, each with its official logo and a link: Tibia (http://tibia.com/), CipSoft (https://www.cipsoft.com/en/) and TibiaWiki (https://tibia.fandom.com/wiki/Main_Page).
  * The logos come from CipSoft's press material and from TibiaWiki itself.
  * CipSoft's logo follows the light or dark theme.
  * If a logo file is missing, the organization's name is shown instead.
  * A notice states that Tibia Foundry is not affiliated with or endorsed by CipSoft GmbH or Fandom.
* **Former credits removed** from the program and its documentation: the original program's developer, a former Dat Viewer author, the author of the sprite extraction code and its link, and the LZMA SDK author's name. The library list became *Open-source components* with each component's license.
* **Descriptions rewritten** on every page, in the dialogs and in the status messages: what each control does and what happens before it happens, in consistent terms and American English. Corrections of wording that did not match the behavior:
  * *Map Tools → Export*: the Areas/NPCs export always saves the complete list; it said "as shown".
  * *Sounds → Extract*: files are saved as `<id>.ogg` or `<id>.wav`; it said every file became `.ogg`.
  * *Filters → List of IDs*: the error message now names all accepted separators (commas, spaces, new lines).
  * *Appearances*: "Force delay" is now "Fixed frame delay" and says that it affects only the preview; on Static Data it says that it also applies to the exported GIF.
  * *Extract Sprites → Skip existing files* explains that sprites of another client version are replaced, not skipped.
  * *Settings*: the folder defaults are explained, and the sprites-folder *Clear* buttons say that no files are deleted.

### Migration

* **Tibia Forge 3.0 settings** (`%APPDATA%\TibiaForge`, or `TibiaForge-Data` next to the program) are carried over on the first start, before the settings of version 2.x; the old files are not changed.
* **Old portable markers and sprite folder manifests** of 3.0 and 2.x are still recognized.
* **Images exported by Tibia Forge 3.0** keep their attribution. Their XMP and history are read, and the old history chunk is replaced (not duplicated) when such an image is exported again.

### Tests

* 197 tests (196 passed, 1 opt-in live check skipped), up from 192.
* **New**: the Credits section (exactly three organizations, addresses, official logo files, aspect ratios, accessibility, missing-logo fallback), files exported by Tibia Forge 3.0, migration from 3.0, and UI checks of the Credits in both themes and the smallest window.
* **Reverse checks**: T07–T10 added (Tibia Forge 3.0 metadata and settings, credit addresses).

## 3.0.0 – 2026-10-12 (Tibia Forge: rebrand and creator attribution)

The program is now **Tibia Forge**, created and maintained by RockyLaRo (TibiaWiki Admin). Details are in `docs/13_Branding_and_Attribution.md`. 2.1.0 is preserved in `07_baseline_2.1.0/`. All output that matched the original program still matches; the GIF comment is now the creator attribution.

### Added

* **Creator attribution in every exported image.** PNG and GIF files carry "Created by <name> using TibiaForge for TibiaWiki @ https://tibia.fandom.com/":
  * **PNG**: text chunks plus XMP.
  * **GIF**: one comment plus XMP.
  * **Fields**: creator, application, website, rights ("Tibia game graphics © CipSoft GmbH"), date and provenance history.
  * **Covered**: single exports, batches (Extract Sprites, map tiles, Cyclopedia crop, multi-GIF export), the Base64 data URI and the clipboard's PNG format.
  * **Safety**: every export is verified before it is kept; a failure leaves no file behind.
  * **Text exports**: generated Lua modules and comparison reports carry it as a comment.
* **Creator name setting** (*Settings → Creator attribution*) with a live preview of the embedded text. The default is RockyLaRo; the name is kept by *Reset to defaults*; a notice on first start.
* **Provenance**: the sheet builder, slicer, HR map and Cyclopedia crop read the attribution of their input files. Files by another creator keep that creator unless you choose otherwise in a dialog; your edit is recorded in their history.
* **About page**: the official logo, the creator profile with seven icon links (TibiaWiki, Tibia character, YouTube, Instagram, GitHub, Reddit, TibiaWiki home), the active attribution, and credits.
* **Official logo and icon**: the supplied 32×32 logo is used unchanged. The Windows icon (16–256 px) and a 256 px logo are made with nearest-neighbor scaling only (`tools/branding/make_branding.py`).

### Changed

* **Name and identity**:
  * window title, navigation header, messages, reports, self-test and User-Agent now say Tibia Forge / TibiaForge;
  * `TibiaForge.exe` (file description "Tibia Forge", company RockyLaRo);
  * projects and namespaces `TibiaForge.*`.
* **Folders**: `%APPDATA%\TibiaForge`, `%LOCALAPPDATA%\TibiaForge`, portable `TibiaForge-Data`, marker `TibiaForge.portable`, sprite folder manifest `TibiaForge-sprites.json`.
* **Light theme**: clearer separation between sections. The page background and navigation rail are darker, cards have visible outlines, and card headers are tinted; secondary text was darkened so all text stays above WCAG AA. The dark theme is unchanged.
* **Settings format 2**: the GIF author is now the creator name. The Export tab shows the attribution with a "Change…" button instead of its own author box.
* Single-sprite GIFs of Extract Sprites now carry the attribution too (the original left them without a comment).

### Migration

* **Settings of version 2.x** (`%APPDATA%\<v2>`, or `<v2>-Data` next to the program) are carried over on the first start; the old files are not changed.
* **The old portable marker** and **old sprite folder manifests** are still recognized.
* **The old GIF author** (2.x `GifAuthor`, 1.x `Author`) becomes the creator name, except the default author value of the original program.

### Tests

* 192 tests (191 passed, 1 opt-in live check skipped), up from 159.
* **New**: branding (identity, logo hash, icon scaling, links, no obsolete names), attribution (formats, independent readers, pixels, transparency, animation, provenance, batches, malformed metadata, failures), workflows (conflict dialog, creator setting, migration, data-export notes), light-theme contrast, and UI checks for the title, icon, logo and About links.
* **Reverse checks**: six attribution checks added (`tools/verification/mutation_checks.py`).
* **Test switches** are now `TF_FULL_SPRITES`, `TF_CLIENT_DIR`, `TF_REFERENCE_DIR`, `TF_LIVE_WIKI` and `TF_SAVE_WIKI_HTML`; the `TWT_` names still work.

## 2.1.0 – 2026-10-12 (stability audit and Tibia update resilience)

Result of an independent audit of 2.0.0: findings A01–A38 in `docs/09_Stability_Audit_Report.md`. 2.0.0 is preserved in `06_baseline_2.0.0/`. All output that matched the original still matches: 223,694 sprites, outfits, GIFs, maps, houses, sounds and exports.

### Added

* **Compatibility page** (Application group) and a background check at start-up and after client updates. It shows:
  * the client version and whether it was validated;
  * per data file: integrity (SHA-256 in the file name), unknown or retyped protobuf fields, record validation, status and affected features;
  * plain-language advice and *Copy report*.
* Pages whose data are affected show a warning banner with *Details…*. `--self-test` includes the full report.
* **Client update detection while running**: data are reloaded on the next page switch or window activation, and the user is told.
* **Sprite folder provenance**: extracted folders get `<v2>-sprites.json`. Folders from another client version are ignored, with a notice, and wrong-size sprites are replaced from the client.
* **Download verification**: *Download list* checks every file against the SHA-256 in its name before it replaces anything.
* TibiaWiki item IDs:
  * MediaWiki API first, the old render URL as a fallback;
  * a 24 h cache; the last good copy is used, with a notice, when the wiki is unreachable;
  * a structure-based parser that rejects implausible results;
  * a 120 s timeout for slow page parses.
* Settings from a newer version keep their unknown options, and a backup `settings.json.v<N>.bak` is made.
* Maintenance documentation: `docs/10_Tibia_Compatibility_Report.md` (dependency map) and `docs/11_Maintenance_Guide.md` (update procedure).

### Fixed

* Truncated or oversized LZMA data, malformed bitmaps and inconsistent sheet layouts are rejected with a message naming the file, instead of decoding to garbage or allocating gigabytes (A01–A05, A29).
* `catalog-content.json`: unknown sprite types, bad numbers, overlaps and unexpected structure are reported and skipped, never guessed (A06, A08).
* File names coming from client data can no longer point outside their folder (A07). Windows device names are handled (A21).
* Partial batch results are no longer shown as green success (A13). Missing sounds count as failures (A12).
* All outputs, caches and the settings file are written atomically (A14, A37).
* Decoded-sound cache is keyed by content (A11). Duplicate house IDs (A19) and impossible map tile names (A20) no longer crash.
* Data-file errors are classified and explained (A17). Structurally broken appearances are flagged, and renderers fail clearly (A18).
* *Reset to defaults* no longer downgrades settings written by a newer version (A28).
* Network:
  * per-request timeouts only, so the old 60 s client-wide limit no longer cuts slow requests (A35);
  * a Cloudflare browser check is reported as such and never worked around (A34);
  * the User-Agent states the real version (A30).

### Changed

* Version-sensitive constants are centralised in `Core/Compatibility/TibiaFormat.cs`, and external addresses in `Core/Net/ExternalEndpoints.cs` (A24, A36).
* Dependencies:
  * Google.Protobuf 3.36.2, Grpc.Tools 2.84.0;
  * Newtonsoft.Json 13.0.4;
  * System.Drawing.Common 10.0.12;
  * CommunityToolkit.Mvvm 8.4.2.
* Tests (159, up from 69):
  * robustness, fuzzing, compatibility, mocked-network, view-model and UI-flow tests;
  * parity tests use a pinned copy of the reference client, so a Tibia update no longer breaks them (A38);
  * UI test infrastructure no longer hangs when Windows refuses a window (A31).
* Source line endings are normalized to CRLF, with an `.editorconfig` (A32).

## 2.0.0 – 2026-10-12 (reconstruction of 1.8.0.26867)

The program was rebuilt from source recovered by decompilation, on .NET 10 + WPF. Every original feature is kept; behavior is verified against outputs of the original binary. Bug IDs refer to `docs/04_Bug_Report.md`.

### Added

* **Portable edition** (`dist/<v2>-2.0.0-portable-win-x64.zip`): one self-contained executable that keeps its settings and logs in `<v2>-Data` next to itself. Any edition becomes portable when a `<v2>.portable` file is placed next to its exe; it falls back to the user profile if the folder is read-only.
* `--self-test <folder>`: a headless check of all bundled components (graphics, sprites, ImageMagick, audio decoders, data folder).
* Light, dark and System themes, switched live (F6) and remembered. Title bars follow the theme. Contrast meets WCAG AA.
* A resizable, maximizable window with a minimum size, Per-Monitor-V2 DPI awareness and saved placement. The navigation rail collapses to icons on narrow windows.
* Progress, cancellation (Esc) and clear error messages for all long operations. A daily log file opens from the status bar.
* Sprites are decoded directly from the client when no extracted file exists (folder, then client, by default).
* Placeholders, tooltips, empty-state messages, property filter and copy buttons, and lazy thumbnails in all lists.
* Page-specific additions:
  * Sprite gallery: zoom, save one or all sprites, copy IDs.
  * Compare: removed entries, staticmapdata support, *Save report*.
  * Extract Sprites: *Skip existing files*, *Add shown*, keyboard and double-click handling.
  * Sounds: volume, autoplay, play buttons on sound IDs.
* `--ui-snapshot` automation: renders every page and runs the window checks.

### Restored

* The hidden tabs *Export Sheets*, *Sprites from PNG* and *LZMA Decode* are now **Advanced Tools** (B46).
* Unfinished controls now work: *Keep Selected* (B40), *Generate Wiki Area/Subarea List* (B41), House *Change file* (B42), the Static Data sprite filter (B43) and *Pattern Stage* (B44).

### Fixed

* 15 crash paths (B01–B15), including house rendering, ping-pong GIFs, missing sprite files, Compare Folders and corrupt settings.
* Wrong output (B16–B39):
  * the outfit sheet "PNG File" mode, which wrote an empty file;
  * the ignored GIF option in Extract Sprites;
  * black transparent GIF frames;
  * clipped HR maps;
  * duplicated subarea names;
  * the *Bank*, *Vocation (any)* and *Width* filters, and exclude mode;
  * Latin-1 names;
  * the sheet count.
* Architecture issues (B48–B56): UI freezes, cross-thread access, locked files, undisposed bitmaps, obsolete APIs and the 32-bit process.

### Changed

* Settings moved to `%APPDATA%\<v2>\settings.json`; 1.x settings are imported automatically.
* *Compare DAT files* is a page; the Tools tab button opens it.
* Distributed as a self-contained x64 folder or zip instead of ClickOnce.

### Not carried over

* The *DefaultStartPhase* and *RandomStartPhase* filters: no data in the current client format, and they did not work in 1.x (B45).
* Dead code: the `GifEncoder` class and the `clientbg`/`pbbg` images (B47).

## 1.8.0.26867 – original

The original program. Preserved unchanged in `00_original_backup/`.
