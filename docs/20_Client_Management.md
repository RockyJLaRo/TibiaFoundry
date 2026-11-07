# Client Management – Tibia Foundry 7.3

> **8.0:** the store described here is now the client archive, described in `21_Client_Archiving_and_Rebuilder.md`. What changed:
> - An archive keeps the whole installation, not only the files of the version in §3. Personal data are still left out by default, now visibly and with counts.
> - Revisions can also be independent copies.
> - *Export…* is replaced by the Client Installation Rebuilder.
> - The quick check and the reminder (§5) are replaced by the full check at every start.
> - Revisions archived by 7.3 keep their scope ("Tibia Foundry files only") and are never relabeled.
>
> The storage model (§4), the locations (§2) and the safety rules (§6) still apply.

**Client Library → Clients** keeps track of your Tibia clients for the live server and the test server:

- **Installed clients** are found automatically, as before. They always follow the launcher.
- **Registered folders** are client folders anywhere on this PC, such as a copy, an older version or a custom installation.
- **Imported versions** are snapshots stored in Tibia Foundry's client store.

You choose which client every page reads. Imported versions share every identical file: each file content is stored once. The plan this follows, and its approval, are in `05_verification/7.3.0/`.

Client locations (Settings → Tibia client, Compatibility → Locate…) are described in `18_Client_Locations.md`; they still apply to the installed client. Test results are in `07_Testing_Report.md`.

## 1. Register, import, use

| Operation | What it does | What it never does |
|---|---|---|
| **Register a folder…** | Remembers a client folder: its name, the server it belongs to, and where it is. | Copies, moves or changes anything in the folder. |
| **Import** | Reads every file of the client's version and stores the ones whose content is not stored yet. It then writes the version's **manifest**, the complete list of its files. | Writes into the client folder. Imports personal data (§3). |
| **Use this client** | Every page reads the selected client from now on: the installed client, a registered folder, or an imported version. | Changes the stored versions. |

- **Rescan** reads the folder again and compares it with its newest stored version. When it changed, it shows what changed and offers to import it as a new version.
- **Rename…** changes only the label. Version folders, manifests and file hashes never change.
- **Locate again…** points a registered folder to its new place. Its imported versions work without it in any case.
- **Export…** writes a plain, complete copy of an imported version into an empty folder. Tools that need a real folder, the Assets Editor and the Client Converter, open that copy. When an imported version is in use, they say so and offer the Clients page.
- **Remove…** has two forms:
  - For a registered folder: forget the folder, or also remove its imported versions.
  - For a version: remove it, or remove it and free its space.

  The installed (automatic) entries cannot be removed, and neither can the version in use.

The line under the **Test server** switch in the navigation bar shows the client in use, for example "Live · 15.33.9d148f (imported)". A click opens the Clients page.

## 2. Where the store is (local only)

| Edition | Client store | Why |
|---|---|---|
| Portable | `<program folder>\TibiaFoundry-Data\Clients\` | Next to the settings, logs and backups. Found again from the program's folder at every start, and every path inside the store is relative. Moving or copying the program folder (another folder, another drive letter) keeps every imported version working. |
| Folder edition (zip) | `%LOCALAPPDATA%\TibiaFoundry\Clients\`, or the folder chosen with **Move store…** (Settings → Tibia client → *Client store*) | There is no installer. Each release unzips into a new versioned folder, and Program Files is not writable for a normal user. A store in the program folder would be left behind at every update. `%LOCALAPPDATA%\TibiaFoundry` already holds the logs, cache and backups. |

- If the portable program folder is read-only, the program runs as the folder edition does: settings in the user profile, store in `%LOCALAPPDATA%`. The status bar says so.
- **Move store…** works in three steps:
  1. It copies everything to an empty folder and verifies every file there.
  2. It switches to the new folder.
  3. It removes the old copy.

  An interrupted move leaves a marker file, so the same folder can be used again.
- **Local only:** the store is a plain folder on this PC.
  - Client management has no network code; an architecture test checks for networking types.
  - It does not upload, back up or synchronize anything, and it does not adapt to a cloud-sync program.
  - Since the objects are read-only files, you can still copy the folder yourself.

## 3. What a version contains

The file set is taken from the installation itself:

| Kind (in the manifest) | Found by |
|---|---|
| `asset`, `sprite-sheet`, `data`, `sound` | Every entry of the client's own **`assets.json`**: assets and sounds, with the SHA-256 and size of each file as it is on disk. Without `assets.json` (a custom or older folder): the files named by `catalog-content.json`, `catalog-sound.json` and the map data. The import report says which was used. |
| `rcc` | Every `*.rcc` file listed in **`package.json`**, and every file in the package that starts with the Qt resource header `qres`. Today each client has one, `bin\graphics_resources.rcc`; any number is supported. |
| `meta` | `package.json`, `package.json.version`, `assets.json`, `assets.json.sha256`. |

**Never imported:**
- `conf\`, which holds the hotkeys and options;
- `characterdata\`, which holds character names;
- `minimap\`, your explored map;
- `screenshots\`, `log\`, `cache\` and `crashdump\`;
- `storeimages\` and `npc_pics\`;
- the client program (the rest of `bin\`);
- `*-bak` files.

Measured on 2026-10-16 (read only):
- `assets.json` lists 7,094 files for live 15.33.9d148f, and every one of their hashes matches the file.
- The hash in a sprite file's **name** is not the SHA-256 of the file. It was checked for 20 sheets and none matched. File names are never used as content identity.
- The test package holds 4,952 old files that the launcher left behind and does not list. They are not imported.

## 4. How files are stored

```
Clients\
  client-registry.json          registrations, versions, labels (replaced atomically)
  store.lock                    held by the one operation that changes the store
  Live_Server\Client_15.33.9d148f\manifest.json
  Test_Server\Client_15.25.0e418e\manifest.json
  Shared_Assets\Objects\ab\ab…  one file per unique content (SHA-256), original bytes, read-only
  Shared_Assets\Incoming\…      objects of an import not yet published
  Shared_Assets\Trash\…         objects a cleanup is removing
```

- **Content addressing:**
  - An object's name is the SHA-256 of its exact bytes, so identical content resolves to the same object, from any client, live or test.
  - Files are stored whole. The client already splits its data into about 7,000 small files, a sprite sheet averages 18 KB, so a changed sprite costs one sheet. Splitting further would need unpacking LZMA or re-encoding the data, and the stored bytes would no longer be the client's.
  - There is no compression or conversion.
- **One store, not a separate `RCC` folder.** An object's place depends only on its content. RCC files are deduplicated like every other file, and the manifest records each file's kind. The storage panel gives the totals per kind.
- **Manifests:**
  - One per version. Each lists every file the version needs: the client's own path (for example `assets/catalog-content.json`), its kind, its SHA-256 and its size.
  - There is no base version and no parent, so no version depends on another.
  - The manifest's ID is a fingerprint, the SHA-256 of its sorted file list, so the same content always has the same ID.
  - A manifest is written once and never changed. Renaming changes only the label in the registry.
- **Version folders:**
  - The name comes from `package.json`: `Client_<version>`, for example `Client_15.33.9d148f`.
  - Content that has the same version text as a stored version but is different gets `_<8 hex of the fingerprint>`.
  - A client without a version gets `Client_unknown_<8 hex>`.
- **Reading:**
  - The readers ask for the client's file names. An imported version maps each name to its object through its manifest, in one dictionary lookup.
  - The catalog, sprites, appearances, static data, map, sounds, the RCC Editor and the compatibility check all read this way.
  - Sounds are recognized by their header (`OggS`, `RIFF`), because objects have no extension.

## 5. Change detection and the launcher

- **The quick check** compares the installed client with the last scan. It is only a hint: it never decides that nothing changed. It runs at start-up and when the Clients page opens, and looks at:
  - the version in `package.json`;
  - the client's `assets.json.sha256`;
  - the number, size and newest time of the version's files.
- **Rescan and Import hash every file.**
  - On this PC that takes about 4 s for the 238 MB of a client.
  - Each hash is checked against the client's own list. A file that differs means the client is damaged or the launcher is updating it: nothing is stored, and the message says to run the launcher and scan again.
  - Only files of the version count, so a changed program file, hotkeys or log is not a change.
- **What changed** is summarized by kind, for example "229 sprite sheets replaced, 1 RCC file changed, 2 sounds added, 1 sound removed".
  - Sheets are named by a hash, so a changed sheet appears as one removed and one added file. They count as replaced when their sprite range in the catalog is the same.
  - Data files are paired by name (`appearances-…`).
- **The launcher.**
  - The installed (automatic) entries keep following the launcher's folders, exactly as in 7.2: when the launcher updates the client in use, the data reload.
  - An imported version is never changed by a launcher update.
  - The installed entry then shows *Changed since last scan*, and **Import** stores the new version next to the old one.
  - If you are using an imported version, nothing reloads.
- **The reminder.**
  - When the installed client's version is not stored yet, a short status message says so at start-up, never a dialog: it is lost when the launcher updates.
  - The Clients page shows it with **Import it now**, **Not now** (until another version is installed) and **Turn reminders off**.
  - The reminder can also be turned off in Settings → Tibia client.
  - Nothing is ever imported automatically.

## 6. Safety: crashes, removal, integrity

- **An import is all or nothing.** The steps:
  1. It holds the store lock.
  2. It writes a journal of the files it will add.
  3. It copies each new file into `Incoming`, hashing it while it is copied and again afterwards.
  4. It renames each file into `Objects`. An existing object is never overwritten.
  5. It writes and validates the manifest, last.
  6. It updates the registry.

  A failure or cancellation removes what the import added. After a crash, the next start removes the unfinished import's objects and folders. A version is either complete or does not exist.
- **Removing a version never removes objects.**
  - **Reclaim space…** (or *Remove and free its space*) deletes only objects that no remaining manifest names.
  - It is refused when any manifest cannot be read.
  - It keeps objects written in the last hour.
  - It moves objects to `Trash` before deleting them, so an interrupted cleanup is finished, or undone for any object that is needed again, at the next start.
- **Verify store** reads every object again and checks its SHA-256. A damaged or missing object marks its version *Damaged*. Importing the client again repairs it, since identical files are reused.
- **A damaged `client-registry.json`** is kept aside (`client-registry.json.damaged-<time>`) and rebuilt from the manifests. Labels and registered folders may then need to be set again.
- **Client folders are only read.**
  - Every write of client management goes through one class, `StoreFiles`, which refuses any path outside the store. The exceptions are the empty folder of an export or a store move.
  - An architecture test checks that no other file of `Core/Clients` writes.
  - The editors never save into the store or into a registered folder, the same rule as for the installed clients.

## 7. Figures (2026-10-16, this PC, read only for the clients)

| | Live 15.33.9d148f | Test 15.25.0e418e |
|---|---|---|
| Files of the version / size | 7,099 / 238.3 MB | 7,040 / 235.8 MB |
| RCC | `bin/graphics_resources.rcc`, 6,785,684 bytes | `bin/graphics_resources.rcc`, 6,922,564 bytes (different content) |
| Scan (hash every file) | 3.8 s | 4.1 s |
| Import | 16.3 s, 238.3 MB written (first) | 2.4 s, **26.4 MB** written (334 new files; the rest was stored) |
| Rescan unchanged | 4.2 s, nothing written | 3.8 s, nothing written |
| 100,000 file lookups | 25 ms | 14 ms |

Both versions stored: 474.1 MB as separate copies, **264.8 MB** stored (7,433 objects), 44% saved. The breakdown by kind:

| Kind | Objects | Size |
|---|---|---|
| sound | 844 | 105.2 MB |
| sprite sheet | 5,261 | 94.5 MB |
| other asset | 1,308 | 33.0 MB |
| data | 10 | 13.3 MB |
| RCC | 2 | 13.1 MB |
| version files | 8 | 5.7 MB |

Evidence: `05_verification/7.3.0/measurements/`.

## 8. Settings (format 6)

| Setting | Default | Meaning |
|---|---|---|
| `ActiveClientLive`, `ActiveClientTest` | null | The client in use per server. Null means the installed client, as before 7.3. A version key (`Live_Server/Client_…`) means an imported version. Any other value is the ID of a registered folder. Always an ID, never a path. |
| `ClientStoreFolder` | null | Folder edition only: the store folder, where null is `%LOCALAPPDATA%\TibiaFoundry\Clients`. |
| `ClientImportReminders` | true | The "not stored yet" reminder. |

Format 6 only adds these settings, and 7.2 keeps settings it does not know, so you can go back and forth.

## 9. Limitations

- **A version that was never imported is gone once the launcher has updated it.** The launcher replaces files in place. The reminder exists for this reason.
- **The Assets Editor and the Client Converter need a real folder.** For an imported version, export it first (Export…).
- **Whether the text after the last dot of the client version is a build ID is an assumption.** It is shown as *Build* only; identity is the fingerprint.
- **When a registered folder is missing,** Tibia Foundry uses the installed client meanwhile and says so. When an imported version cannot be used, for example because files are missing, the same happens. Your choice is kept in both cases.
