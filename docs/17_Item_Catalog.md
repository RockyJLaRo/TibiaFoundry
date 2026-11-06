# Item Catalog – Tibia Foundry 7.1

Version 7.0 adds the **Item Catalog** (Game Data → Item Catalog): a database of item names and IDs for wiki pages, item databases and fansite references. It keeps every source apart, tells official data from community and third-party data, never mixes identifier types, and shows where each name comes from. Objects the client does not name can be named by other sources or by your own decision (catalogs made with 7.0–7.3 can also hold names read in the client by Item Discovery, which was removed in 8.0). Client and data locations are in `18_Client_Locations.md`; test results in `07_Testing_Report.md`.

## 1. What a catalog holds

A catalog is a folder (default `Documents\Tibia_Foundry\Item_Catalogs\<name>`). It holds **sources**, and each source holds **records**: one fact about one identifier.

| Part | What it is |
|---|---|
| Source | where records come from: the client's data, TibiaWiki, a list, a CSV or JSON file, a third-party `items.xml` or `items.otb`, names observed in the client, your decisions. It states what its identifiers mean, whether it is official, its version, origin, file hash and import time. |
| Record | an identifier with a name (and article, plural), category, properties, how it was obtained (import, your decision; catalogs made with 7.0–7.3 can also hold names read from the client's tooltips or confirmed by you with Item Discovery, which was removed in 8.0: such records still load and count), how sure it is (verified, unverified, unresolved, failed), when, evidence and notes. Values a source does not give stay empty; nothing is guessed. |
| Mapping | a stated correspondence between identifier types, for example an `items.otb` entry's own ID → client ID (exact, for that file's client version). |
| Entry | everything the catalog knows about one identifier, and the name it settles on, with the reason. |

**Identifier types are never mixed.** The same number means different things in different sources. A record's identifier is always stored with its type, and two records are about the same thing only if both match:

| Type | Meaning |
|---|---|
| Tibia object ID | the official object ID of the modern client: the ID in `appearances.dat`, in the game protocol, in hotkeys (`useObject`) and in TibiaWiki's "itemid". |
| items.otb ID | the file's own item ID in a third-party `items.otb` (stored as "OtbServer" in catalog files); meaningful only with that file's version. |
| OTB client ID | the client ID an `items.otb` states, for the client version it was made for. |
| Legacy Tibia.dat ID | an object ID of an old client (renumbered between versions). |
| Unknown | the meaning could not be established. |

items.otb IDs are compared with client IDs only through an `items.otb` mapping (§5).

### How a name is settled

| Agreement | When |
|---|---|
| **official** | the client's own name, or a name observed in the official client and verified (read exactly or confirmed by you) |
| **chosen by you** | your decision in the conflict review (§6) |
| **sources agree** | no official name; two or more sources give the same name |
| **one source** | only one source gives a name |
| **conflict** | sources give different names and none is official (no name is settled) |
| **no name** | no source gives a name |

Names are compared without regard to case, repeated spaces and a leading "a", "an" or "the" ("The Holy Tible" and "holy tible" are the same). A TibiaWiki article title is preferred less than a real name with the same spelling. Within a source whose records are added one by one (names observed in the client), the latest record of an identifier replaces the earlier ones (a confirmation, correction or rejection); the history stays visible.

## 2. Sources and imports

*Import* offers:

* **Client data**: every object of the client in use with the client's own names (official, verified). Objects the client does not name are listed without a name (unresolved). Market category and the properties that matter for comparisons (pickupable, stackable, container, usable, …) come along.
* **TibiaWiki item IDs**: TibiaWiki's "Item IDs" list, object ID → article title (community data). Titles are marked as titles: TibiaWiki capitalizes and disambiguates them, so they can differ from in-game names. Since 7.0.1 the list is read from the item articles the page is made of (the wiki can no longer build the page in time): about 190 requests, about a minute, with progress in the status bar; the result is the page's table. The page itself is the fallback. A list downloaded in the last 24 hours is reused, and a cached copy is used when the site cannot be reached.
* **From a file…**:
  * a list of `[id] name` lines (GetItemNames, LapisItemEditor). A leading "a "/"an " becomes the article. Lines starting with `#`, `;` or `//` are comments. You say what the numbers mean;
  * a CSV file with a header. Columns are found by name: `ID` (required), `Name`, `Article`, `Plural`, `Category`, `Flags` (separated by `;`), `ID type`. The catalog's own CSV exports read back unchanged;
  * a CSV list of names and IDs **without a header** (7.1), such as the market tracker's `items.csv` (`name,id`, MIT-licensed data). Its first lines are shown and you choose whether the ID is the last value of each line (*Name, then ID*) or the first (*ID, then name*); the choice that fits the lines best comes first. The rest of the line is the name, commas included (names in such lists are often not quoted: `Baby Seal (Time Travel, Painted),37748`). Names are taken as written (no article is split off) and stay unverified;
  * a catalog export (JSON): one source per source in the file, with its description;
  * a third-party `items.xml` item list or `items.otb` ID mapping (§4).

**Every import is previewed first**: records added, changed and removed against the source it replaces, names used by more than one identifier, lines that could not be used (with their line numbers), and, against the client, names for objects the client does not name, names that differ from the client's and identifiers the client does not list. Nothing is written until you confirm. Importing a source again replaces its records; the old records are backed up first.

Removing a source (Sources tab) moves its records to the backups. Sources whose records are added one by one (names observed in the client, your decisions) cannot be removed as a whole.

## 3. Storage

| File | Contents |
|---|---|
| `catalog.json` | name, description, the sources (written atomically) |
| `sources\<source>.json.gz` | the records and mappings of one imported source; never changed in place, replaced as a whole on re-import after a backup |
| `observations.jsonl` | records added one at a time (your decisions; in catalogs made with 7.0–7.3 also names observed in the client), one JSON object per line, flushed to disk after every line |
| `evidence\<session>\*.png` | catalogs made with 7.0–7.3 only: images of the icon and tooltip a name was read from (Item Discovery, removed in 8.0; kept, never deleted) |

If the program stops while writing the log, the unfinished last line is reported when the catalog is opened and skipped; every complete record before it is kept, and the next record starts on a new line. A snapshot is written before `catalog.json`, so an interruption never leaves `catalog.json` pointing to missing records. When another Tibia Foundry window registered a source while this one had the catalog open, writing `catalog.json` keeps that source.

## 4. Third-party item data

Community-maintained item lists in the `items.xml` and `items.otb` formats name many objects the client itself does not name. For wiki editors and fansite databases they are **research leads**: a name found there tells you what to look for in the game, never what to publish without checking. These files are **your own copies**; Tibia Foundry never bundles or downloads them (they are GPL data). Their records are always "third-party" data: never verified, never official, and kept apart from the client's own names.

**`items.otb`** (an item ID mapping file). The reader follows the file format: four zero bytes, then nodes (`0xFE` start, `0xFF` end, `0xFD` escape). The root's version attribute gives the format, the client version code and build, and a description such as "OTB 3.65.62-13.10". Each item has a group (container, teleport, magic field, writable, key, splash, fluid, door, podium, …), flags (pickupable, movable, stackable, multi-use, readable, hangable, …), its own **item ID** (called the server ID in the format) and the **client ID** it maps to. Each item becomes an items.otb-ID record and an exact items.otb → client mapping for the file's client version.

**`items.xml`** (an item list). You say what the IDs mean: some lists use Tibia object IDs, others the IDs of an `items.otb`. When an `items.otb` lies next to the file, Tibia Foundry offers to import it too. You also name the source (for example "Community item list 15.25"); the name labels its records in the catalog and in exports. The file is read as its format intends:

* `id`, or `fromid`/`toid` ranges (at most 10,000 per element), with `name`, `article`, `plural`;
* the attributes `type`, `containersize`, `floorchange`, `writeable`/`maxtextlen`, `primarytype` and `description`;
* a later definition of the same ID adds to the earlier one;
* IDs below 100 (fluid types) are skipped;
* DOCTYPE declarations are refused, so no external content is ever read.

**Figures** (opt-in test `RealThirdPartyItemDataReproducesTheResearchFigures`, on research copies outside the project, client 15.33):

| Data | Result |
|---|---|
| an `items.otb` (OTB 3.65.62-13.10) | 38,284 items.otb IDs (100–47,311); 1,433 equal to their client ID; 1,418 client IDs with more than one items.otb ID |
| the `items.xml` that uses its IDs | 32,010 named IDs of 100 and above; 15 named without an `items.otb` entry |
| that list vs the client, assuming the OTB client IDs are today's object IDs | names agree for 6,365 of 6,425 identifiers named on both sides (99.1 %); with items.otb IDs used directly as client IDs only 6.0 % agree, so translating through the mapping is essential |
| an `items.xml` that uses client object IDs | 37,507 named IDs; 7,522 the same as the client's names, 283 different, names for 25,133 objects the client does not name, 4,569 IDs the client does not list |

## 5. Comparing

The **Compare** tab compares any two sides: a source of the open catalog, or each identifier's settled name, against a source or the settled names of the same or another catalog (for example the catalogs of two client versions). Nothing is changed by comparing.

It lists:

* identifiers added and removed;
* renames, names that appear and names that disappear;
* category changes, and property changes where both sides use that property;
* items.otb IDs that map to a different client ID in two `items.otb` versions;
* client IDs claimed by several items.otb IDs (the lowest is compared; the others are listed).

Containers, doors, teleports, depots, mailboxes, trash holders, beds, floor changes, magic fields, keys and podiums are **behavior-sensitive**: their changes are marked and can be listed alone.

*Translate items.otb IDs with items.otb* compares items.otb IDs through the client IDs their `items.otb` maps them to; they are never compared as client IDs themselves. *Treat items.otb client IDs as Tibia object IDs* is an **assumption** (an `items.otb` states client IDs for its own client version); the result states it and says how often names agree under it.

*Save result…* writes the differences as an Excel workbook, CSV or text.

The catalog comparison does not compare appearances or sprites; the Compare Files page does that for client files.

## 6. Renaming and the conflict review

**Renaming (7.2).** Any identifier can get a name of your own in a catalog:

* in the list: select it and press **F2**, double-click its name, or choose *Rename…* in the list's menu (right-click). The name becomes a text box: **Enter** or ✓ saves, **Esc** or ✕ cancels, and clicking elsewhere cancels too (the status bar says so), because a saved name is a permanent record;
* in the details: **Rename…** asks for the name in a dialog.

Your name is recorded as **your decision**, a record of its own in the catalog's journal (`observations.jsonl`, source "Your decisions"). Nothing else is written: no source's records file, no other record, no mapping, and nothing of the client (sprites and attributes are in the client's files, which a catalog never writes). The record keeps the identifier and its type unchanged; only the name and its article are taken from what you type ("a" or "an" in front becomes the article). The list shows the identifier as "chosen by you", its *Client name* column keeps the client's own name, and search still finds it by every name its sources give. The row is updated where it is, so the list keeps its selection, order and scroll position; sorting and filters take the new name into account the next time they are applied.

* A name is refused, with the reason under the text box (or in the status bar for the dialog), when it is empty, contains a line break, tab or other control character, or is longer than 100 characters. Spaces around it are removed and repeated spaces become one. The same rules apply to Item Discovery's *Correct…*.
* Renaming an object **the client names itself** asks once: the client's name stays visible and *Restore original name* brings it back. The same question is asked when *Use this name* would replace the client's name.
* **Restore original name** (details, or the list's menu) removes your name: the identifier settles again on what its sources give (§1), and the history keeps both records.

Every catalog export uses the settled name, so a rename shows up in the name list, CSV, XLSX and JSON exports. Pages that show the client's own data do not use catalogs: Appearances (its names and its ID table), the Assets Editor, and the NPC trade export (TibiaWiki's titles).

**Conflict review.** When the sources of an identifier give different names, its details list every name with the sources that give it. *Use this name* records one of them as your decision, the same way as a rename; no source is changed, and what each source says stays visible.

## 7. Exports

The scope selector chooses what is exported: the identifiers shown (with the current filters), all identifiers, only official or chosen names, or only identifiers without a settled name.

| Export | Contents |
|---|---|
| Name list | `[id] name` lines with a header saying what the numbers mean (readable by LapisItemEditor) |
| CSV | one row per identifier: settled name, how sure, every other name with its sources |
| Records CSV | one row per record, with source, status and method |
| XLSX | an Excel workbook: an About sheet, then Items, Records and Sources |
| JSON | the identifiers with every record and source; importable into another catalog |

**The XLSX writer** is built in (no new package). It writes inline strings and stores whole numbers up to 15 digits without a leading zero as numbers. The header row is bold and frozen, with filters. Control characters are removed, cells are limited to 32,767 characters and sheet names are made valid. **Values are never written as formulas.** CSV cells that a spreadsheet would run as a formula get a leading apostrophe; the CSV reader removes it again. The writer was checked with openpyxl and with Microsoft Excel (a 38,411-row workbook opened without a repair prompt).

Exports made from TibiaWiki data say so: TibiaWiki's content is licensed CC BY-SA, and anything published with it must carry the same license (`THIRD-PARTY-NOTICES.txt`).

## 8. Coverage

*Sources and coverage → Compute coverage* counts what the catalog covers, per source and relative to the client. **Completeness can only be stated relative to the client's own list of objects**; an object no source names is not therefore nonexistent.

Measured on 2026-10-14 (client 15.33, TibiaWiki fetched the same day; `05_verification/7.0.0/item_catalog/`):

* the client lists 43,516 objects; 8,978 named, 34,538 without a name;
* TibiaWiki's item IDs give 13,875 article titles: 7,306 the same as the client's name, 1,049 different (mostly capitalization and disambiguation), 5,504 for objects the client does not name, 16 for IDs the client does not list;
* 29,034 objects without a client name are named by neither source; a third-party `items.xml` that uses client object IDs names 25,133 of the 34,538 (unverified research leads).

## 9. Limitations

* TibiaWiki's item list gives article titles, not in-game names.
* Third-party names can be wrong or placeholders; they stay labeled as such and are never verified.
* An `items.otb` mapping is tied to the client version it was made for; there is no automatic version shift.
* The catalog is per installation of Tibia Foundry; it is not synchronized anywhere.
