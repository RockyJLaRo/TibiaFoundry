# Client Archiving and the Rebuilder – Tibia Foundry 8.0

8.0 turns the 7.3 client store into a **client archive**:

- it archives a whole installation, not only the files Tibia Foundry reads;
- it loads client folders from anywhere, as they are;
- it rebuilds a normal folder from any archived revision;
- it checks the installed clients at every start;
- it chooses the client at start: first-run setup, "ask which client" and recovery.

The approved plan is in `05_verification/8.0.0/plan/` (all recommended defaults D1–D14; phases 0–6 and 8; storage conversion, phase 7, not done). Evidence is in `05_verification/8.0.0/`.

`20_Client_Management.md` still describes the store itself: content-addressed objects, the portable and folder-edition locations, local only. This document describes what 8.0 adds and changes. Client locations are in `18_Client_Locations.md`.

## 1. Client folders: find, browse, load

**Clients → Find installation** looks in the usual places and validates each candidate by its files:

- `%LOCALAPPDATA%\Tibia\packages`;
- the folders of Tibia's uninstall entries in the registry;
- the Steam libraries listed in `libraryfolders.vdf`;
- the folders already registered.

A candidate is shown only if it really is a client.

**Browse folder…** accepts any folder: an installation, a copied client, a rebuilt installation, or a client's `assets` folder (it then offers the package folder above it). The folder inspector classifies it without hashing anything (≈ 0.4 s on the installed package):

| Class | What it means |
|---|---|
| Complete installation | A client package: `package.json`, the client's lists, `assets`, `bin`. |
| Standalone client | Client data without an installation's lists (a copy, an older version). |
| Assets only | A client's `assets` folder. |
| Not a client folder | Nothing a tool can read. |

For each folder the Clients page shows which tools can use it, and what is missing for the others.

**Loading never copies.** A folder is used where it is and only read. To keep a copy that survives the folder changing or disappearing, *Archive* it.

**Version evidence:**

| Source | Strength |
|---|---|
| `package.json` "version" | strong |
| `package.json.version` | strong |
| `client.exe` version resource (today's clients have none) | strong |
| The same content in an archived revision | weak |

The result is **Verified** (the strong sources agree), **Conflicting** (they disagree; all values are shown), or **Unknown**. An unknown version is shown as "15.33.9d148f? (probably)" or "Unknown version", never as a fact.

## 2. What an archive contains

An archive is a **complete inventory** of the folder, taken from the folder itself:

- every file at any depth, including hidden files, files without an extension, backups, and files no client list names (for example saved web pages next to the client);
- every empty folder;
- each file's size, last-write time and read-only/hidden attributes.

Links (junctions, symbolic links) are listed as problems and never followed.

**Personal data are left out by default** (Settings → Tibia client → *Include personal data*), and every exclusion is recorded with its counts:

- `conf/` (options and hotkeys), except the files the client's own lists name;
- `characterdata/`, `minimap/`, `screenshots/`, `log/`, `cache/`, `crashdump/`, `storeimages/`, `npc_pics/`;
- client screenshots anywhere, recognized by their name pattern. Their names hold a character's name, so they are counted, never listed.

**The archive confirmation (8.5).** Before anything is written, *Archive…* shows:

| Section | Content |
|---|---|
| Header | *Archive <client>*, the server and version, the folder |
| Summary | Files · Archive size (the files as restored) · New disk usage (what is written; contents the archive already holds, and identical files in the folder, are stored once); scope, storage, the space reused, the changes against the newest revision |
| Personal data | Left out by default (files and size), or included. *Include personal data* applies to this archive only (the setting is not changed): the folder is read again, with progress, and every figure is updated; *Archive* waits until then. |
| File details (collapsed) | Each category — client files, options and hotkeys, character data, explored map, screenshots, logs… — with files, size and new disk usage, marked *Archived*, *Left out* or *Not archived*, and the entries that could not be read |

The figures come from `ClientStore.PreviewArchive` (`ArchivePreview`), which uses the same accounting as the free-space check, so *New disk usage* is what that check requires.

**Completeness is stated honestly:**

| Label | When |
|---|---|
| Complete | Nothing was left out and every file was read. |
| Client content complete — personal data left out (N files) | Only personal data were left out. Never called a complete installation. |
| Incomplete (see details) | Kept on purpose although files could not be read; the problems are listed. |
| Tibia Foundry files only (7.3) | A revision archived by 7.3. It keeps its scope and is never relabeled. |

**Consistency.** The scan is accepted only if all of these hold:

- every file was read;
- every file the client's own lists name (`package.json`, `assets.json`) has the client's hash;
- no file changed while it was read;
- a second listing of the folder after hashing equals the first.

Otherwise the archive is refused, unless you keep it as an incomplete revision. It is never published as complete.

Measured on this PC (read only; `05_verification/8.0.0/phase2/installed_packages_inventory.txt`):

| Package | Files archived | Size | Not in any client list | Full scan |
|---|---|---|---|---|
| Live | 8,601 | 637 MB | — | 3.6–3.9 s |
| Test | 13,466 | 726 MB | 4,976 | 5.7–6.2 s |

## 3. Storage modes

| Mode | How | Choose it when |
|---|---|---|
| **Space-efficient** (shared, default) | Each file content is kept once in `Shared_Assets\Objects`. A later revision adds only what changed. Every revision has its own complete manifest, with no chain of revisions. | Almost always. |
| **Independent copies** | Every revision has its own physical copy under `<revision>\files\`, made read-only. | You want revisions with no file in common. |

- The mode applies to future archives only. Existing revisions keep theirs, and converting is not part of 8.0.
- Both modes check every copy before the manifest is published:
  - shared: objects are added with "move without replace";
  - independent: files are written to `<revision>.incoming-<id>`, read back, and then renamed into place.
- Free space is checked first: new content for shared storage, everything for independent storage, plus 200 MB. "Not enough space" stops before anything is written.

**The Storage figures, per revision:**
- the size as restored;
- what only this revision uses (removing it frees that much);
- what it shares with others.

For the whole archive: the size as restored, the size on disk, the share saved by sharing, and what no revision uses any more (*Reclaim space*).

**Removing:**
- Removing a shared revision removes only its manifest. Its content goes only through an explicit *Reclaim space*, after every reference is counted again.
- A revision being read by a rebuild or a verification is **leased** (`Clients\Leases\`, with the process ID and start time). It cannot be removed until the lease ends.

## 4. Comparing revisions

*Compare* reports, by content hash:
- unchanged files;
- changed files (same path, other bytes);
- added and removed files;
- moved or renamed files (the same bytes under another path).

Only what both revisions cover is compared. A complete installation against a 7.3 revision compares only the files Tibia Foundry reads, and folders one side left out are left out of both. What could not be compared is said in plain words.

## 5. Client Installation Rebuilder (Tools)

The Rebuilder makes a normal folder from an archived revision. It uses only that revision's manifest and the content it references, never an installed client.

- **A complete installation is *rebuilt*.** Any other revision is *restored* as the folder it was. The page and the report say which.
- **The output folder must be new or empty.** Nothing is ever replaced. A folder that has files is offered a new subfolder instead.
- The output must not be inside a Tibia client folder, a registered folder, or the archive.
- **How it works:**
  1. Files are written to `<target>.tf-partial-<id>` next to the target, each checked against its recorded hash; recorded times and attributes are restored, and empty folders recreated.
  2. The staging folder is checked as a whole: exactly the recorded entries, each file read back.
  3. Only then is the staging folder renamed into place.

  A failure or cancellation removes the staging folder. If it cannot be removed, it is renamed `.tf-failed-<id>` so it never looks like a result.
- An interrupted rebuild is recorded in `Clients\Rebuilds\`. At the next start the status bar and Diagnostics mention it, and *Verify store* offers to remove the unfinished folder.
- **The report** goes to `Documents\Tibia_Foundry\Reports`, never into the rebuilt folder. It lists what was restored and what the revision had left out. It always says "Runnable: not checked": Tibia Foundry never starts the client.
- The rebuilt folder can be added to the Clients page as a *reconstructed installation* (only read there).
- Tools that need a physical folder (Assets Editor, Client Converter) point to the Rebuilder when an archived revision is in use. The Rebuilder replaces 7.3's *Export*.

## 6. The check at every start

After the first page shows, Tibia Foundry checks each **installed** client (Settings → Tibia client → *Check at start*, live and test separately). It runs in the background and never shows a dialog.

1. **The launcher:** if `Tibia.exe` or `client_launcher.exe` of this installation is running, the check waits for the next start, because the launcher may be updating the client. `client.exe` running does not block: it writes into the personal folders, which the check leaves out.
2. **Reading:**
   - **Full** (default) reads every file with two threads, in the background. On this PC, in the published program: live 5.7 s; test 38.9 s when its 726 MB were not in the disk cache (3.6–5.8 s when they are).
   - **Quick** reads only files whose size or time changed, files that are new, and the version files: 0.5 s on this PC. Quick is labeled honestly: it can miss a change that keeps both size and time. An archive always reads everything.
3. **Consistency:** the rules of §2. A check that saw files change, or files that differ from the client's own lists, is not accepted. Nothing advances, and the next start checks again.
4. **Comparing:**
   - with the **baseline**, the last accepted check (`Clients\Baselines\<registration>.json`: every file's path, size, time and hash, the empty folders, the fingerprint, the time, and the revision with that content);
   - with the archive, by fingerprint.

   A baseline is not an archive, and nothing is called archived because of a baseline. Writing a baseline does not create the archive itself.
5. **What follows** (Settings → Tibia client → *When it changed*):

   | Choice | What happens when the content is not archived yet |
   |---|---|
   | **Ask me** (default, also for upgrades) | A notice above the status bar: "The installed live client changed: 15.33.9d148f → 15.34.… (312 file(s) changed, 4 added, +41 MB)". It offers **Archive**, **Later** (asked again at the next start) and **Don't ask for this version** (stored per content fingerprint). |
   | **Archive it automatically** | It is archived in the background with the chosen storage and personal-data rule. The status bar shows the result. |
   | **Only show it on the Clients page** | No notice. The Clients page shows the reminder. |

   Content already archived, or answered "Don't ask", is left alone. A folder that has disappeared is reported once per start; its archived revisions keep working.

## 7. First-run setup and the client at start

**First-run setup** appears only for a new installation. Settings written by 7.x count as set up (decision D13), so the first 8.0 start of an existing user asks nothing. It is a few short questions, each skippable:

1. **Your Tibia client:** the installations found, or *Browse for a folder…*.
2. **Keeping client versions:**
   - what the client is: class, version evidence, the tools that can use it, what is missing;
   - space-efficient or independent copies, with this client's own size.
3. **When the launcher updates it** (only for an installed client): ask, or archive automatically.
4. **Archive now?**
   - a complete installation: with its size and what is left out;
   - any other folder: *Import this folder into the archive*, clearly not called an installation.

A summary follows. Settings → Tibia client → *Run setup again…* repeats it.

**At start** (Settings → Tibia client → *At start*):

| Situation | Behavior |
|---|---|
| *Use the remembered client* and it can be used | Used, without a question (as in 7.x). |
| The remembered client cannot be used (folder moved, revision damaged, archive missing) | The **recovery dialog**. It offers: the archived revision of the same version (when there is one), the other usable clients, *Locate the folder…* and *Browse for another…*. *Keep my choice* uses the installed client meanwhile and keeps the choice. |
| *Ask which client* | The **selection dialog**, grouped as live installation, test installation, standalone folders, archived revisions and reconstructed installations, with the client in use preselected. *Remember my choice* switches to the remembered client. A choice alone never turns asking off. |
| Exactly one usable client | It is used. |

Settings → Tibia client also has:
- the remembered client, with *Change…* and *Clear* (the installed client; nothing is removed);
- *Browse for a client folder…*;
- the storage mode, personal data, *Check at start* (live, test, Full or Quick) and *When it changed*.

**Safe switching.** Every page drops what it loaded from the previous client when the client changes. A test opens every page on one client, switches, and checks that no page still names the previous client's folder or files. It found two pages that kept a default file and fixed them: Compare Files and the Client Converter now follow the switch, while a file or folder you chose yourself stays. A switch waits while an operation runs. Open documents with unsaved changes (Assets Editor, RCC Editor) stay open and lose nothing.

## 8. Where things are (inside the archive folder)

| Folder | Holds |
|---|---|
| `Live_Server\`, `Test_Server\` | One folder per revision: `Install_…` (complete installation), `Folder_…` (standalone folder), `Client_…` (7.3). Each holds `manifest.json` and, for independent copies, `files\`. |
| `Shared_Assets\Objects\` | Shared content, named by SHA-256. |
| `Baselines\` | The last accepted check per installed client. |
| `Leases\` | Revisions being read right now. |
| `Rebuilds\` | Rebuilds that have not finished. |
| `client-registry.json` | Folders and revisions (format 1, with optional 8.0 fields, so 7.3 still reads it). |

*Move store…* copies all of these except leases.

## 9. Formats and compatibility

- **Manifests, format 2:** scope, storage, source type, complete, exclusions, problems, empty folders, version evidence, and per file the list that names it, its time and attributes. 8.0 reads format 1 (7.3) unchanged.
- **7.3 and an 8.0 archive:**
  - 7.3 refuses format-2 manifests, so it shows 8.0 revisions as not usable, and its own revisions keep working.
  - Its *Reclaim space* removes nothing while any manifest cannot be read, so 7.3 never deletes content an 8.0 revision uses.
  - If 7.3's *Verify store* marks 8.0 revisions as damaged, 8.0's *Verify store* clears that.
- **Settings, format 7:**
  - It adds the storage mode, personal data, change handling, check depth, monitoring per server, the start-up mode and *setup completed*.
  - It drops the removed Item Discovery's settings.
  - The file as it was is kept as `settings.json.v6.bak` before the upgrade.
  - `ClientImportReminders` is kept in step for 7.3 (off = only on the Clients page).

## 10. Limits and what was not verified

- **Converting existing revisions** between storage modes is not part of 8.0 (phase 7 was not approved).
- **Unverified:** whether `client.exe`, while you play, changes a file outside the personal folders. It is assumed it does not. If it did, a check made while playing would see a change, archive or offer it, and the next check would see it again. It is checked by running a check while the client runs.
- **Rebuilt folders are not started.** Whether a rebuilt client runs depends on things outside its files (the launcher, the account, Windows), so the report never claims it.
- **Steam and registry discovery** were written from their documented formats. This PC has neither a Steam installation of Tibia nor an uninstall entry, so they were tested only with test files.
