# 03 Data, Storage and Formats

## Locations

There are two editions: an installed edition (per user) and a portable edition. The portable edition keeps everything in `TibiaFoundry-Data` next to the program. *(Verified: `AppPaths`, `AppServices`)*

| Data | Installed edition | Portable edition |
|---|---|---|
| Settings (`settings.json`) | `%APPDATA%\TibiaFoundry` | `TibiaFoundry-Data` |
| Logs | `Logs` under the output root | `TibiaFoundry-Data` |
| Output root | `Documents\Tibia_Foundry` (one subfolder for each kind of output) | same, configurable |
| Backups | `Backups` under the output root | `TibiaFoundry-Data` |
| Client store | `%LOCALAPPDATA%\TibiaFoundry\Clients` (movable) | `TibiaFoundry-Data\Clients` |
| Caches (TibiaWiki item list) | next to the settings | next to the settings |

The program never writes into a Tibia client installation:

- **Saves.** Editor saves into a client folder are redirected to a copy.
- **Output folders.** Output folders inside the installation are refused.
- **Limit of the check.** The check knows only the default packages folder and the folders that are registered or archived (see F-01).

## Client store (7.3, extended in 8.0)

Content-addressed storage: `Clients\Shared_Assets\Objects\<2 hex>\<sha256>`.

- **Revisions.** Each archived revision has a format-2 `manifest.json` that lists its files, sizes, hashes and the exclusions.
- **Storage modes.** In "shared" mode each content is stored once across revisions. In "independent" mode each revision keeps its own copy.
- **Leases.** Leases protect revisions that are in use from deletion.
- **Baselines.** Baselines record the state of monitored installations for the launch check (full or quick).
- **Rebuilder.** The Rebuilder rebuilds a folder from a manifest and verifies every file by its hash.
- **Personal data.** Hotkeys, character data, minimap, screenshots, logs and caches are left out unless `ArchivePersonalData` is on.

## Formats read and written

| Format | Code | Notes |
|---|---|---|
| Appearances, static data, map, sounds (`*.dat`) | `Core/Data`, protobuf (`.proto` in Core) | Read only |
| Sprite sheets `*.bmp.lzma` | `Core/Compression`, `Core/Sprites` | CIP LZMA; BMP decoding and encoding |
| `catalog-content.json` | `Core/Clients`, `Core/Editing` | Index of the client's assets |
| Legacy `Tibia.dat` / `Tibia.spr` | `Core/Legacy` | Writes 10.98 and Client 11 layouts; identifies 8.60 |
| Qt `.rcc` | `Core/Rcc` | Formats 1–3 with zlib; Zstandard entries are kept but not decoded |
| `.aec` object package | `Core/Editing` | Exports and imports objects in the Assets Editor |
| GIF / PNG | `Core/Imaging` (Magick.NET, System.Drawing) | Attribution is written into exports |
| Item catalog | `catalog.json`, `*.json.gz` snapshots, `observations.jsonl` | `ItemCatalogStore` |

## Write safety

| Helper | Behavior |
|---|---|
| `AtomicFile` | Writes to a temporary file, then replaces the target. It does not call fsync (see F-18). |
| `StoreFiles.CopyIn` | Flushes to disk. |
| `BackupStore` | Keeps the replaced and removed files. |
| `PathGuard` | Blocks path traversal from archive and manifest entries. |
