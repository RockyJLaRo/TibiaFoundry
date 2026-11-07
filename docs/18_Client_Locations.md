# Client and Data Locations – Tibia Foundry 7.1

Version 7.0 lets every file and folder Tibia Foundry reads from the Tibia client be located by hand: from the Compatibility page ("Locate…") or in Settings → Tibia client. Before 7.0 only the assets folder could be chosen, and the client version, the sounds and the resource file always came from the installed client. The Item Catalog is described in `17_Item_Catalog.md`.

**7.3:** the locations below are those of the **installed client** (the "Installed client (automatic)" entry of the Clients page). The Clients page can also choose:

* **A registered client folder.** Its own files are used, and the choices below do not apply to it, except the packages folder and the extracted sprites folder.
* **An imported version.** Its files are read through its manifest from the client store; the client's options file and screenshots stay the installed client's.

See `20_Client_Management.md`.

**8.0:** client folders can be loaded from anywhere: an installation, a copied client, a rebuilt installation or an assets folder (Clients → Find installation / Browse folder…, or Settings → Tibia client → *Browse for a client folder…*). Such a folder is classified and used as it is, and the locations below still apply only to the installed client. See `21_Client_Archiving_and_Rebuilder.md`.

**8.6:** the installed clients are named **Default Live Client** (`<packages>\Tibia`) and **Default Test Client** (`<packages>\TibiaExternal`, or the test-server package chosen in Settings). `<packages>` is `%LOCALAPPDATA%\Tibia\packages` of the signed-in Windows user (Windows' known folder, never built from a user name; on the release PC `C:\Users\Rocky\AppData\Local\Tibia\packages`), or the packages folder chosen here. First-run setup lists both with their state, read from their files (`Core/Clients/DefaultClients.cs`):

| State | Meaning | Can be chosen |
|---|---|---|
| Available | a complete installation | yes |
| Usable (not a complete installation) | a copied client or an assets folder | yes |
| Incomplete | some client files, not enough for any tool | no |
| No client found | the folder holds something else | no |
| Not installed | the folder does not exist | no |
| Cannot be read | permissions, a disconnected drive, an invalid path | no |

Any other folder can still be chosen with *Browse for a folder…*; it is remembered as before. Setup run again does not replace a client you chose before without asking.

## 1. The locations

| Location | Kind | Normally | Needed |
|---|---|---|---|
| Tibia packages folder | folder | `%LOCALAPPDATA%\Tibia\packages` (shared by both clients) | — |
| Client package folder | folder | `<packages>\Tibia`, or the test-server package | yes |
| Client version file | file | `<package>\package.json` | — |
| Client assets folder | folder | `<package>\assets` (with `catalog-content.json`) | yes |
| Appearances, static data, static map data, map files | files | named in `catalog-content.json` | yes |
| Client sounds folder | folder | `<package>\sounds` (with `catalog-sound.json`) | yes |
| Sounds file | file | named in `catalog-sound.json` | yes |
| Client resource file | file | `<package>\bin\graphics_resources.rcc` | optional |
| Extracted sprites folder | folder | none (set one to read sprites from extracted PNG files first) | optional |

The live client and the test server each have their own locations; the packages folder is shared. The definitions are in `Core/Locations/LocationResolver.cs`.

## 2. How a location is found

Each location is resolved in this order:

1. **the one you chose**, if it can be used;
2. **the one inside a folder you chose** (for example the appearances file of an assets folder you chose);
3. **the one found automatically** in the installed client;
4. otherwise it is **not found**, with the reason and where it was expected.

A choice that is unavailable (for example on a disconnected drive) is **kept**: a notice says so, the automatic location is used meanwhile, and your choice is used again when it is back. The version file follows a chosen assets folder: the `package.json` next to it, otherwise the version is unknown (a client version is never taken from another client).

**Checks.** Start-up and page loads use quick checks only (exists, file or folder, right extension, small headers such as the `qres` signature of a resource file). A location you pick gets a deeper check first: a folder must contain what it is for (an assets folder a readable sprite catalog; a package folder `package.json` or the assets catalog); a data file is checked by the compatibility check of that one component; the options file must be the client's options (hotkey sets). A wrong pick is explained and not saved. Picking the package folder where its assets or sounds folder is needed (and the other way round) is corrected, and the correction is shown.

## 3. Locate… and Settings

**Compatibility page.** A row whose file is missing, unreadable or in an unknown format offers *Locate…* (or *Change…* when it is found). The chosen location is checked, saved and applied at once: only the affected components are checked again, the other rows stay as they are, and pages forget the file they would load by default, so the next load uses the new one. *Reset to automatic* forgets the choice. *All locations…* opens the list in Settings.

**Settings → Tibia client → Tibia client and data locations** lists every location of the live client or the test server (a selector switches between them) with its path, how it was found, and *Change…* / *Automatic*.

Pages that use a missing file show a banner that offers *Locate…* for it.

## 4. Settings format 5

Choices are stored in `ClientLocations` (`live:<id>`, `test:<id>`, or `packages` for the shared folder). The assets folder of the live client stays in `AssetsFolderOverride` and the sprites folders in `SpritesFolder`/`SpritesFolderTestServer`, so 6.0 can still read the file. Upgrading from format 4 gives the test server the assets folder that was chosen for both clients before.

## 5. What else changed

* The client version, the sounds and the resource file follow the chosen locations (before 7.0 they always came from the installed client).
* The live client and the test server keep separate assets folders.
* Switching between them, or a client update, makes every page forget the files of the other client; the compatibility report is cleared and checked again.
* The Diagnostics self-check lists every chosen location and whether it is available.
* The diagnostics report and the compatibility report (copied or saved from the Compatibility page) list every location, its path and how it was found.
