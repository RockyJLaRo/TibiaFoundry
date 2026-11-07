# The Fansite Toolkit – Tibia Foundry 8.7

Tibia Foundry is an independent desktop toolkit for Tibia fansite administrators, wiki editors, artists, and content creators. Explore official game assets, generate graphics, render outfits and creatures, create maps, process media, and prepare resources for Tibia community websites.

This guide explains what each tool is for when you build or maintain a wiki, a fansite or a guide, and how the tools work together. File formats and implementation details stay in the other documents; links are given where they help.

Tibia Foundry is a community project. It is not affiliated with or endorsed by CipSoft GmbH or Fandom, and it is not part of CipSoft's Fansite Programme. Tibia, its game graphics and data are © CipSoft GmbH; graphics you export remain CipSoft's. Publish them according to CipSoft's rules for fansites and your wiki's image policy.

## 1. Who it is for

| You are… | Tibia Foundry gives you… |
|---|---|
| a **wiki editor** | images and animated GIFs at the client's own size, item IDs and names, creature, NPC and house data, NPC trade tables, house images |
| a **fansite administrator** | consistent galleries, item and creature databases (JSON, CSV, Excel), map references, "what changed in this update" lists, an archive of every client version |
| an **artist or graphic designer** | transparent sprites, pixel-exact enlargements (nearest-neighbor), sprite sheets, the client's interface artwork as reference |
| a **guide writer or content creator** | map images and locations, sounds and music, correctly credited media with creator metadata |

## 2. Navigation

The navigation rail groups the tools by task. Nothing was removed in 8.7; only the grouping changed. The Home dashboard lists the same groups.

| Group | Tools |
|---|---|
| Start | Home |
| Graphics & Sprites | Appearances (with the Outfitter), Extract Sprites, Advanced Tools |
| Game Data | Static Data, Item Catalog, Compare Files |
| Maps & Houses | Map Tools, House Images |
| Sound & Media | Sounds, File Metadata |
| Asset Management | Assets Editor, RCC Editor, Client Converter |
| Client Library | Clients, Client Installation Rebuilder |
| Utilities | Scripts |
| Application | Settings, Compatibility, Diagnostics, About |

A new installation opens on **Home**. When you upgrade, the page you used last (or the start page you chose in *Settings → General*) still opens.

## 3. The Home dashboard

* **Introduction** and links to the Tibia Foundry website, TibiaWiki, the official fansite list on tibia.com, and your output folder.
* **Official client library**: the client every page reads, and *Manage clients…* (the Clients page).
* **Recently used tools**: the last six pages you opened.
* **Recent exports**: the last six files or folders a tool saved (everything announced as "Saved to …" in the status bar). Click one to show it in File Explorer; a file that was moved or deleted is reported, not opened.
* **Graphics and content workflows**: the steps of common tasks, each with a button to the page where it starts (§6).
* **All tools by category**, each with what it is for.

The recent lists are stored in the settings file (`RecentPages`, `RecentExports`); older settings files load without them.

## 4. Fansite export presets (Appearances → Export)

A preset fills in the options of *Export GIF* for a common use. It never locks anything: change any option afterwards and the selector shows **Custom**, with your change kept. The choice is remembered.

| Preset | Size | Animation | Delays | Folders | Use |
|---|---|---|---|---|---|
| TibiaWiki image | 1× | moving, south | average | one folder | item, creature and effect images at the client's own size |
| Creature gallery | 2× | moving, south | average | by kind | creature galleries, bestiary pages |
| Outfit gallery | 2× | idle, south | average | by kind | outfit and addon galleries |
| Item icon | 1× | idle | average | one folder | icons for databases, loot lists and infoboxes |
| Article illustration | 3× | as set | average | one folder | guides and news posts where 32×32 pixels are too small |
| High-resolution reference | 4× | as set | maximum | one folder | artists who trace, recolor or study the pixel art |

**Size** is a whole-number enlargement (1×, 2×, 3×, 4×, 6×, 8×) with nearest-neighbor sampling: every client pixel becomes a sharp square, no blur, no new colors, and transparency is kept. Enlarged files end in `_2x`, `_3x`… so they are never mistaken for the client's own size. Upload 1× images where a wiki expects the original size.

**Sort into Objects, Outfits, Effects and Missiles folders** puts automatically named GIFs into a subfolder per kind of the GIF folder, which keeps galleries and upload batches organized. A single GIF saved under a name you choose goes exactly where you save it.

Every GIF and PNG carries your creator attribution in its metadata (*Settings → Attribution*; see `13_Branding_and_Attribution.md`). The image itself has no watermark.

## 5. Every tool and its fansite use

| Tool | What it does | For wikis and fansites |
|---|---|---|
| **Home** | Overview, recent tools and exports, workflows | Start here; jump back to your last export |
| **Appearances** | Browse every object, outfit, effect and missile; filters; GIF export with presets; Outfitter (colors, addons, mounts); Outfiter sheets; ID/name tables; JSON; NPC trade data in Lua | Item and creature images, outfit images in any colors, the TibiaWiki Outfiter sheet, NPC trade tables, item lists |
| **Extract Sprites** | Saves sprite sheets as individual transparent PNGs or whole sheets, with a provenance manifest | Galleries, artwork references, a local sprite library for designers |
| **Advanced Tools** | Builds and slices sprite sheets; decompresses `.lzma` assets; downloads test-server asset files | Composite sheets for galleries; previews of upcoming content from the official test server |
| **Static Data** | Creatures, bosses, achievements, houses and quests; JSON export; creature GIFs | Bestiary and boss pages, achievement lists, quest and house databases |
| **Item Catalog** | Item names and IDs from the client, TibiaWiki, lists, CSV and third-party item files, kept apart by source; conflict review; comparisons; coverage; CSV, JSON, name list and Excel exports | Item databases, checking wiki item IDs against the client, finding objects that still need an article (`17_Item_Catalog.md`) |
| **Compare Files** | What changed between two versions of the client's appearances, map, staticdata and staticmapdata | Update articles and change logs: new, changed and removed objects, creatures and map data |
| **Map Tools** | Areas, subareas, NPC positions and map data; map tile extraction; high-resolution maps; tibiamaps.io links | Location articles, guide maps, hunting-ground references |
| **House Images** | Renders every house, one floor or all, as PNG | House articles and house guides |
| **Sounds** | Plays and extracts the client's sounds, effects, ambience and music | Media pages, sound references for articles and videos |
| **File Metadata** | Views, edits, compares and batch-edits metadata (author, copyright, keywords, dates…) of images, audio, video and documents; presets; reports (`22_File_Metadata.md`) | Checking and setting creator and copyright on everything you publish; consistent keywords for galleries |
| **Assets Editor** | Opens an assets folder; inspects and edits objects, outfits, effects and missiles with their sprites; exports images; saves a validated copy (never the installed client) | Inspecting how an object is built (frame groups, patterns, flags) before rendering it; exporting images of several objects at once |
| **RCC Editor** | Opens, previews, extracts, replaces and rebuilds Qt `.rcc` resource files | The client's interface artwork, icons and backgrounds as references for guides and fansite design |
| **Client Converter** | Writes the official modern assets as a classic `Tibia.dat` and `Tibia.spr` (Client 11, Tibia 10.98 layouts); identifies legacy files (8.60, 10.98, Client 11); optional PNG slices | Historical comparisons with classic-era objects; opening the assets in legacy-format viewers and sprite tools; identifying old client files found in archives |
| **Clients** | Registers installed clients and folders from anywhere; archives complete client revisions (space-efficient); chooses the client every page reads (live and test server) | An official client library for historical articles and for comparing updates |
| **Client Installation Rebuilder** | Rebuilds a normal folder from an archived revision, verified file by file | A browsable copy of an old client for the asset tools or reference |
| **Scripts** | 12 file utilities with preview, backups and undo: duplicates, comparisons, batch renaming, checksums, batch image conversion and resizing (nearest-neighbor available), archives, backups and restore, reports, the Tibia project auditor | Bulk renaming for wiki upload conventions, resizing whole galleries without blurring pixel art, checking an export library for duplicates |
| **Settings** | Behavior, appearance and themes, folders, file safety, performance, attribution, notifications, tools and scripts | Your creator name; where each kind of export goes |
| **Compatibility** | Whether this version reads the installed client's data | Check after every Tibia update before producing new graphics |
| **Diagnostics** | Operations, performance, logs, self-check, a privacy-safe report | When something goes wrong |
| **About** | Purpose, version, creator, attribution, credits, acknowledgments | Credits and license information |

## 6. Workflows

**Creature or item images for a wiki article.** Appearances → find the object, outfit or creature → Export tab → preset *TibiaWiki image* → *Save as GIF*. For creatures, Static Data → *Save creature GIF* saves the animation shown in its preview (with an optional fixed delay).

**A consistent gallery.** Appearances → select several outfits (or objects) → preset *Outfit gallery* or *Creature gallery* → *Save as GIF*. Files are named automatically and sorted into a folder per kind. Use the Outfitter tab for colors and addons; *Outfiter sheet* builds the sheet TibiaWiki's Outfiter uses.

**Map references and house images.** Map Tools for areas, NPC positions, map tiles and high-resolution maps; House Images for every house.

**What changed in the latest update.** Clients → archive the installed client before and after the update → Compare Files on the two versions' files → the report lists added, changed and removed objects and data. The Item Catalog compares names between two catalogs of different versions.

**Item database.** Item Catalog → import the client and TibiaWiki's item IDs → review conflicts → export as CSV, JSON or Excel.

**Batch-process and credit published images.** Scripts → *Batch Image Converter and Resizer* (choose nearest-neighbor for pixel art) and *Batch File Renamer*; File Metadata to check or set author, copyright and keywords on a whole folder before uploading.

## 7. Third-party item data

The Item Catalog can import community-maintained item lists in the `items.xml` and `items.otb` formats. They name many objects the client does not name, so they are useful **research leads** when documenting items. Their names are always labeled third-party and unverified, are never mixed with the client's official names, and should be checked in the game before they are published. The files are your own copies; Tibia Foundry never bundles or downloads them (`17_Item_Catalog.md` §4).

## 8. Compatibility of your data

* Settings, client archives, item catalogs and exports of earlier versions are used as they are. The new settings (`GifScale`, `GifKindFolders`, `GifPreset`, `RecentPages`, `RecentExports`) have defaults that keep the earlier behavior: 1× GIFs, one output folder, *Custom* options.
* Catalog files keep their stored identifiers (for example the source kinds `OtItemsXml`/`OtItemsOtb` and the identifier type `OtbServer`), so catalogs made with any earlier version open unchanged; only the wording on screen changed.
