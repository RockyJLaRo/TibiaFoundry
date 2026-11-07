Tibia Foundry 9.0.1 - Portable edition (Windows x64)
==================================================

Tibia Foundry - All your Tibia-Tools for TibiaWiki (https://tibia.fandom.com/wiki/Main_Page)

Tibia Foundry is an independent desktop toolkit for Tibia fansite
administrators, wiki editors, artists, and content creators. It reads the
official files of your installed Tibia client and turns them into material
for TibiaWiki (https://tibia.fandom.com/), fansites, guides and galleries:
sprites and animated GIFs, outfit, creature and house images, maps, sounds,
item and creature data, and NPC trade tables. Its asset tools inspect and
convert the official files:

  * Assets Editor    - inspect the client's objects, outfits, effects and
                       missiles with their sprites, export images, and save
                       a checked copy
  * RCC Editor       - open, preview, extract and rebuild Qt resource files
                       such as the client's graphics_resources.rcc
  * Client Converter - write the official assets as a classic Tibia.dat and
                       Tibia.spr (Client 11 or Tibia 10.98 layouts) for
                       historical comparisons and legacy-format viewers
  * Scripts          - 12 file utilities: duplicates, folder comparison,
                       renaming, checksums, inventories, large and empty
                       files, image conversion, archives, file formats,
                       backups and a Tibia project audit

New in 9.0: TibiaWiki infoboxes. In Appearances, select an object and open
the Wiki infobox tab: Tibia Foundry writes TibiaWiki's {{Infobox Object}}
markup from the client's own data (ID, names, flavortext, slot, level and vocations,
imbuement slots, object flags, light, map colour, NPC prices and traders).
Every value shows where it came from; what the client does not know
(implemented, weight, attack, armor...) is never guessed and is listed for
you to fill in. Copy it, save it as .txt, or save one file per selected
object. Saving is also safer: documents are flushed to the disk before they
replace the old file.

New in 8.8.2: Static Data exports the GIFs of all selected creatures at once
(Select all, then Export GIF...), or of every monster and boss, and shows
creature names capitalized like TibiaWiki articles (Orc, Demon Lord).

New in 8.8.1: a performance release. Every tool works as before; the start-up
compatibility check, loading appearances, comparing files, sprite and GIF
exports and the Asset Library (indexing, rescans, search) are faster and use
far less memory. See the release notes for the measurements.

New in 8.8.0: the Asset Library and Content Studio.
  * Asset Library (Content Production): a searchable, local library of your
    images, animations and sounds. Folders are indexed where they are (only
    new and changed files are read again); every file you export from any
    tool is added automatically. Thumbnails, categories (your own too), tags,
    custom fields, collections (export and import them), related assets,
    earlier versions of changed files, duplicate detection (identical files,
    the same graphic at another size, similar images - nothing is ever
    deleted), integrity check, backup and restore. Stored with SQLite.
  * Content Studio: reusable projects - creature gallery, outfit gallery,
    item image collection, map references, update resources (what is new
    or changed since an older client), wiki image preparation, custom
    export. A workflow builder combines steps without programming (render,
    trim, resize, canvas, format, names, folders, contact and comparison
    sheets, JSON/CSV/Markdown manifests, a self-contained HTML gallery, text
    snippets, ZIP, add to the library). Projects run in a background queue
    with progress, cancellation, retry and history; a cancelled or failed
    run leaves no partial files.
  * Publishing profiles for any website (TibiaWiki, your fansite, galleries,
    articles, a personal archive): size, canvas, format, file name and folder
    templates, credit line and snippets. Editable; create your own.
  * Clearer screens: equal-height Home cards, the Clients page in cards with
    details as labeled values, a structured item-import confirmation, and a
    client summary card in setup.
  * Its data is kept in TibiaFoundry-Data\Library (see below). Settings and
    archives of 8.7.0 are used as they are.

New in 8.7.0: the fansite toolkit.
  * Home: every tool by category with what it is for, recently used tools,
    recent exports (click to show them in File Explorer), the client in use,
    step-by-step workflows, and links to TibiaWiki and the official fansites.
  * The navigation is grouped by task: Graphics & Sprites, Game Data, Maps &
    Houses, Sound & Media, Asset Management, Client Library, Utilities.
  * GIF export presets (Appearances > Export): TibiaWiki image, Creature
    gallery, Outfit gallery, Item icon, Article illustration, High-resolution
    reference. Any option changed by hand switches back to Custom.
  * Pixel-exact enlargement 1x to 8x (no blur, transparency kept; files end
    in _2x, _3x...), and optional Objects/Outfits/Effects/Missiles folders.
  * Settings, client archives and item catalogs of 8.6.0 are used as they are.

New in 8.6.0: File Metadata, clearer setup and messages.
  * File Metadata (Sound & Media): view, edit, compare and batch-edit the metadata
    of images (EXIF, XMP, IPTC), audio and video tags, Word/Excel/PowerPoint
    properties, EPUB books, SVG drawings and ZIP comments. PDFs, programs,
    other archives and image formats and Tibia client files are read only.
    Every save goes to a copy first, is read back and checked, and can be
    backed up (Utilities > Scripts > Backup and Restore puts files back).
  * The Default Live Client (packages\Tibia) and Default Test Client
    (packages\TibiaExternal) are named in setup with their real state.
  * Setup's archive question and summary are organized in sections; paths
    can be copied. Message boxes have a Copy button and can be resized.

New in 8.5.0: a clearer archive confirmation, and fixes.
  * Archiving a client shows the client and version, the number of files,
    the archive size and the new disk usage at a glance. Personal data has
    its own section; "Include personal data" updates the figures at once.
    File details lists every category: archived, left out or unreadable.
  * "Check for changes" followed by "Archive..." reads the client once
    instead of twice.
  * Clearer error, rebuild and download-limit messages.
  * Closing the program while an archive or export runs asks first.
  * Esc on an import question now cancels; no tool writes into a Tibia
    client folder, also when the folder is typed.
  * Import settings / Reset all settings no longer move your client archive.
  Settings, archives and revisions are the same as in 8.0.0.

New in 8.0.0: complete client archives.
  * Archive a whole Tibia installation: every file, empty folders and file
    times. Your hotkeys, character data, explored map, screenshots and logs
    are left out unless you include them, and each revision says so.
  * Space-efficient revisions keep each file content once; independent
    copies keep a full copy per revision. Choose in Settings > Tibia client.
  * Load a client folder from anywhere (an installation, a copy, a rebuilt
    folder or an assets folder): Clients > Find installation / Browse folder.
  * Client Installation Rebuilder (Client Library): makes a normal, checked folder
    from any archived revision. It never replaces files.
  * At every start the installed clients are checked in the background
    (only read). When the launcher changed one and the new version is not
    archived yet, a notice offers Archive, Later or Don't ask.
  * Item Discovery was removed.

New in 7.3.0: the Clients page.
  * Register any Tibia client folder (a copy, an older version, a custom
    installation), import a client as a stored version, and choose which
    client every page reads - for the live server and the test server.
  * Imported versions share every identical file: each file content is
    kept once, so each further version only adds what changed. Removing a
    version never removes files another version uses.
  * The installed clients keep following the launcher. A launcher update
    never changes an imported version; the program notices the change and
    offers to import the new version (a short reminder you can turn off).
  * Tibia client folders are only ever read. Hotkeys, character data, your
    explored minimap, screenshots and logs are never imported. Everything
    stays on this PC: nothing is uploaded or synchronized.

New in 7.2.0:
  * Item Catalog: rename any item in place (F2, a double-click, or the
    list's menu); a rename is kept as your decision and "Restore original
    name" undoes it. No source file changes.

New in 7.1.0:
  * Item Catalog imports CSV lists of names and IDs without a header line,
    such as the market tracker's items.csv.

New in 7.0.1: importing TibiaWiki's item IDs works again. TibiaWiki can no
longer build its "Item IDs" page in time, so the list is now read from the
item articles it is made of (about a minute, with progress).

New in 7.0:
  * Locate any client file - every file and folder the program reads from
    the client can be pointed elsewhere ("Locate..." on the Compatibility
    page, or Settings > Tibia client), for the live client and the test
    server separately
  * Item Catalog - item names and IDs from the client, TibiaWiki, lists,
    CSV and third-party items.xml/items.otb item files, kept apart by source, with
    conflict review, comparisons, coverage and exports (CSV, JSON, name
    lists, Excel)

New in 6.0: your own color themes, one output folder under
Documents\Tibia_Foundry, "Saved to <file>" links and the Diagnostics page.

Created by RockyLaRo, TibiaWiki Admin
  TibiaWiki Profile:  https://tibia.fandom.com/wiki/User:RockyLaRo
  Tibia Character:    https://www.tibia.com/community/?name=Roc+ky
  YouTube:            https://www.youtube.com/@Roc_ky
  Instagram:          https://www.instagram.com/rocky_tibia/
  GitHub:             https://github.com/RockyJLaRo
  Reddit:             https://www.reddit.com/user/Roc-ky/

WHAT THIS IS
  One self-contained program file. No installation and no .NET runtime are
  needed. Copy TibiaFoundry.exe to any folder you can write to (a USB stick,
  Documents, a tools folder) and double-click it.

  Requirements: Windows 10 or 11 (64-bit) and an installed Tibia client
  (%LOCALAPPDATA%\Tibia\packages\Tibia), or a folder with the client's files.

YOUR NAME IN EXPORTED IMAGES
  Every PNG and GIF image you export carries this line in its metadata (the
  image itself is not changed):

      Created by <your name> using TibiaFoundry for TibiaWiki @ https://tibia.fandom.com/

  The name is "RockyLaRo" until you set your own in Settings > Attribution.
  If images you combine or edit name another creator, you are asked before
  that creator is replaced.

YOUR FILES ARE SAFE
  * The tools never change the installed Tibia client. The editors save
    copies of client files elsewhere.
  * When a file already exists, you are asked first (Settings > File safety:
    ask, keep both, replace or skip).
  * Files that are replaced, and files a script moves away, are kept in the
    backup folder. Utilities > Scripts > Backup and Restore lists them and puts
    them back.
  * Scripts that change files show every change first and only apply what
    you confirm. Scripts never run by themselves.
  * If a folder cannot be used (drive missing, no permission), you are told
    why and asked for another one. Nothing is saved somewhere else silently.

WHERE YOUR EXPORTS GO
  Into your Documents folder (wherever Windows keeps it, also when it is in
  OneDrive or on another drive):

      Documents\Tibia_Foundry\GIF_Exports\
      Documents\Tibia_Foundry\Image_Exports\
      Documents\Tibia_Foundry\Sprite_Exports\
      Documents\Tibia_Foundry\Sound_Exports\
      Documents\Tibia_Foundry\Map_Exports\
      Documents\Tibia_Foundry\Data_Exports\
      Documents\Tibia_Foundry\RCC_Exports\
      Documents\Tibia_Foundry\Client_Converter\
      Documents\Tibia_Foundry\Assets_Editor\
      Documents\Tibia_Foundry\Scripts\
      Documents\Tibia_Foundry\Reports\       (diagnostics reports)
      Documents\Tibia_Foundry\Manifests\     (checksums, inventories)
      Documents\Tibia_Foundry\Item_Catalogs\ (item catalogs)
      Documents\Tibia_Foundry\Content Projects\ (Content Studio runs)

  A folder is created only when something is saved into it. Each one can be
  changed in Settings > Files and folders ("Use default" brings it back).

WHERE IT KEEPS ITS OWN DATA
  Settings, logs, backups and temporary files are stored next to the
  program, in:

      TibiaFoundry-Data\settings.json      (also your custom themes)
      TibiaFoundry-Data\logs\
      TibiaFoundry-Data\backups\           (replaced and removed files)
      TibiaFoundry-Data\temp\              (work in progress; cleaned up)
      TibiaFoundry-Data\operations.json    (recent operations, no file contents)
      TibiaFoundry-Data\script-history.json
      TibiaFoundry-Data\cache\             (last good copy of the TibiaWiki item IDs)
      TibiaFoundry-Data\Clients\           (client archive: revisions, shared
                                           content, the last check of each
                                           installed client)
      TibiaFoundry-Data\Library\           (Asset Library: library.db, thumbnails,
                                           copied files, profiles, projects,
                                           workflows)
      TibiaFoundry-Data\queue-history.jsonl (finished Content Studio jobs)

  Nothing is written to your user profile (AppData). To keep your settings
  and imported clients, move or copy the program together with its
  TibiaFoundry-Data folder; imported clients keep working in the new place.
  Settings > Advanced > Storage shows which mode is in use.

  If the folder is read-only (for example a write-protected stick), the
  program still runs. It then stores its settings in your user profile and
  says so in the status bar.

UPGRADING
  * From Tibia Foundry 8.6 or 8.7: replace TibiaFoundry.exe and keep the
    TibiaFoundry-Data folder. 8.8 adds the Library folder and one settings
    section; nothing else changes, and 8.7 can still use the folder.
  * From Tibia Foundry 8.0 or 8.5: replace TibiaFoundry.exe and keep the
    TibiaFoundry-Data folder. Nothing changes in it (8.6 only adds the File
    Metadata options); 8.0 and 8.5 can still use it.
  * From Tibia Foundry 7.3: replace TibiaFoundry.exe and keep the
    TibiaFoundry-Data folder. Nothing is asked at the first start; versions
    imported by 7.3 keep working (labeled "Tibia Foundry files only"). The
    settings are upgraded (a copy of the old file is kept as
    settings.json.v6.bak). 7.3 can still use its own versions, but not the
    revisions 8.0 archives.
  * From Tibia Foundry 7.1 or 7.2: replace TibiaFoundry.exe and keep the
    TibiaFoundry-Data folder (or copy it next to the new TibiaFoundry.exe).
    The settings gain the client store settings; 7.2 can still read them.
  * From Tibia Foundry 7.0.0 or 7.0.1: replace TibiaFoundry.exe and keep
    the TibiaFoundry-Data folder (or copy that folder next to the new
    TibiaFoundry.exe). Nothing in it changes. Names that 7.1 reads from
    client screenshots are skipped by 7.0.x, so open such catalogs with 7.1.
  * From Tibia Foundry 6.0: replace TibiaFoundry.exe and keep the
    TibiaFoundry-Data folder. The settings are upgraded on the first start
    (the test server keeps the assets folder you had chosen); 6.0 can still
    read them.
  * From Tibia Foundry 5.0 or 4.0: replace TibiaFoundry.exe and keep the
    TibiaFoundry-Data folder. The settings are upgraded on the first start;
    nothing is lost. Export folders that were still the old default
    (Documents\TibiaSprites) move to Documents\Tibia_Foundry; folders you
    chose yourself stay as they are. Files already exported are not moved.
  * From Tibia Forge 3.0, version 2.x or the original 1.x program: put
    TibiaFoundry.exe in the same folder as the old program. On the first
    start, its settings (TibiaForge-Data, the 2.x data folder, or
    settings.json of 1.x) are carried over. The old files are not changed,
    so the old version keeps working. Images exported by Tibia Forge keep
    their attribution.

AFTER A TIBIA UPDATE
  The program checks the installed client's files at start-up, after a client
  update, and whenever you click "Check again" on the Compatibility page
  (Application > Compatibility). Each file is shown as:

      Supported                 - validated with this version
      Unfamiliar but compatible - a newer client, but every check passed
      Partially compatible      - some records failed the checks; the
                                  affected pages show a warning, the rest
                                  works normally
      Missing / Not supported   - the file is absent or cannot be read; only
                                  the features that need it are affected

  "Copy report" puts the full report on the clipboard for a bug report.

IF SOMETHING GOES WRONG
  Open Application > Diagnostics:
    * Operations - what is running, its progress, and what finished or failed
    * Log        - filter by level, tool or text; "Open log folder"
    * Self-check - checks settings, folders and tools; it repairs nothing
                   unless you click a repair and confirm it
    * Report     - preview, then save a diagnostics report (ZIP) into
                   Documents\Tibia_Foundry\Reports. Your user and computer
                   names, the paths of your personal folders, e-mail
                   addresses and passwords or keys are replaced before
                   anything is shown; full paths are included only if you
                   tick the box and confirm. The report is never sent
                   anywhere - attach it to a bug report yourself if you want.

GOOD TO KNOW
  * The first start takes a few seconds longer. Like every self-contained
    .NET single-file program, it unpacks its graphics and image libraries
    (about 31 MB) once into %TEMP%\.net\TibiaFoundry. Windows Disk Cleanup
    may remove that folder; it is recreated on the next start.
  * TibiaWiki is contacted only by features that need it (the NPC trade
    module). If the wiki cannot be reached, the last saved copy is used and
    the program says so.
  * F6 switches between the light and dark theme. Esc stops a running
    operation that can be stopped.
  * Settings > Appearance & Themes > "Restore original appearance" undoes
    every color change at once.

CHECK THAT EVERYTHING WORKS ON A NEW PC (optional)
  Open a command prompt in this folder and run:

      TibiaFoundry.exe --self-test self-test-report

  This opens no window. It writes self-test-report\self-test.txt with
  PASS or FAIL for:
    * the data folder;
    * graphics and the logo;
    * client files and sprite decoding;
    * outfit rendering and GIF writing;
    * creator attribution in exported PNG and GIF files;
    * sprite sheet encoding (Assets Editor) and the client's Qt resource file;
    * sound decoding;
    * file metadata (a JPEG's title and keywords written and verified);
    * client data compatibility (the full compatibility report);
    * the application self-check (settings, folders, tools and their
      dependencies).

  To see the result code afterwards, run:

      echo %ERRORLEVEL%

  It shows 0 when there were no failures.

REMOVE
  Delete TibiaFoundry.exe and the TibiaFoundry-Data folder (check its
  backups folder first). Your exports stay in Documents\Tibia_Foundry until
  you delete them. Optionally also delete %TEMP%\.net\TibiaFoundry and
  %TEMP%\TibiaFoundry (sound playback cache).

CREDITS
  Tibia (http://tibia.com/), CipSoft (https://www.cipsoft.com/en/) and
  TibiaWiki (https://tibia.fandom.com/wiki/Main_Page).
  Tibia, its game graphics and data are (c) CipSoft GmbH. Tibia Foundry is an
  independent fan project and is not affiliated with or endorsed by CipSoft
  GmbH or Fandom.

OPEN-SOURCE ACKNOWLEDGMENTS
  The RCC Editor, the Client Converter and the Assets Editor were made with
  these projects as reference; Tibia Foundry includes no code from them:
    Nekiro's RCC Editor by Nekiro   https://github.com/nekiro/Nekiro-Rcc-Editor
    ClientConverter by Arch-Mina    https://github.com/Arch-Mina/ClientConverter
    Assets Editor by Arch-Mina      https://github.com/Arch-Mina/Assets-Editor
  These projects and their authors are not affiliated with Tibia Foundry and
  do not endorse it.

LICENSES
  THIRD-PARTY-NOTICES.txt (next to this file) contains the licenses and
  notices of the components included in the program.

Full documentation: the docs folder of the source package (README.md,
13_Branding_and_Attribution.md, 14_Tools_Settings_and_Scripts.md,
15_Themes_Outputs_Tools_and_Diagnostics.md, 17_Item_Catalog.md,
18_Client_Locations.md, 20_Client_Management.md, 22_File_Metadata.md,
24_Asset_Library_and_Content_Studio.md,
Build and Setup, Maintenance Guide).
