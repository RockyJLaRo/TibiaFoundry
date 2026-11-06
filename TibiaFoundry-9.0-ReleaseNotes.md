# Tibia Foundry 9.0 – Release Notes

Released 2026-11-06 · Windows 10/11 x64 · installer (MSI), portable edition and source.

9.0 brings **TibiaWiki infoboxes** to the Appearances tool, makes saving and backups safer, and gives the program one version source: from now on public versions have two parts (**9.0**, tag `v9.0`). Everything of 8.8.2 is included.

## New: TibiaWiki infobox exporter

**Appearances → select an object → Wiki infobox.** Tibia Foundry writes TibiaWiki's [`{{Infobox Object}}`](https://tibia.fandom.com/wiki/Template:Infobox_Object) for the object, from the official client data, using the template's own parameters and the conventions of TibiaWiki's item pages.

* **Filled from the client:** `itemid`, `name` and `actualname`, `slot`, `hands`, `weapontype`, `levelrequired`, `vocrequired` ("knights and paladins"), `imbueslots`, `upgradeclass`, `immobile`, `walkable`, `pickupable`, `usable`, `rotatable`, `hangable`, `holdsliquid`, `writable`/`rewritable`/`writechars`, `wrappable`, `unshootable`, `lightradius`/`lightcolor`, `mapcolor`, `stackable`, `marketable`, and the NPC trade: `npcvalue` (best price an NPC pays), `npcprice` (lowest price an NPC asks), `buyfrom` and `sellto` (every trader, with their own price where it differs; `--` when nobody trades).
* **Derived and marked for review:** the classification (`objectclass`, `primarytype`, `secondarytype`) from the market category, only where TibiaWiki's classification is consistent; the article-cased name when TibiaWiki's item list is not cached.
* **Never invented:** `implemented`, `weight`, `value`, `attack`, `defense`, `armor`, `attrib`, `resist`, `notes` and the other values the client's files do not contain. Required ones are listed empty (or `?`, TibiaWiki's "unknown") so you see what is left.
* Every value shows where it came from. Edit any value, add further template parameters, then **Copy markup**, **Save as .txt…**, or **Save all selected…** (one file per selected object; existing files are never replaced). **Use TibiaWiki article names** downloads the wiki's Item IDs list (cached 24 hours) so `name` is the article title.
* Values are escaped for template arguments (`|` → `{{!}}`, stray braces), and the markup follows the layout of the wiki's item pages.

Guide: `docs/26_TibiaWiki_Infobox_Exporter.md`. Example (the installed client's Magic Plate Armor, before you fill in the empty lines):

```
{{Infobox Object|List={{{1|}}}|GetValue={{{GetValue|}}}
| name          = Magic Plate Armor
| actualname    = magic plate armor
| itemid        = 3366
| objectclass   = Body Equipment
| primarytype   = Armors
| slot          = Body
| implemented   =
| immobile      = no
| walkable      = yes
| pickupable    = yes
| vocrequired   = knights and paladins
| imbueslots    = 2
| upgradeclass  = 2
| weight        =
| stackable     = no
| marketable    = yes
| droppedby     = {{Dropped By|}}
| value         =
| npcvalue      = 90000
| npcprice      = 0
| buyfrom       = --
| sellto        = H.L.: 720, Rashid
| notes         =
}}
```

## Reliability

* **Safer saving.** Settings, item catalogs, manifests, collections, workflow files and histories are flushed to the disk before they replace the old file, so a power failure right after saving leaves the old or the new file, never an empty one.
* **Backups across drives.** When a file is moved into a backup on another drive (removing a catalog source, clean-up scripts), the original is deleted only after the copy has been verified. Before, the move could lose the file if the copy was bad.
* **Previews.** A damaged or locked sprite sheet now only empties the Appearances preview, with a note in the log, instead of reaching the global error dialog.
* **Number fields** no longer step two or more times per click after a theme switch.

## Performance

No tool became slower or uses more memory: the workload benchmarks of the 8.8.1 audit (15 scenarios on the installed client 15.33.d96497) allocate exactly the same memory in 8.8.2 and 9.0, and the wall times agree within the machine's run-to-run variation. Building and writing an infobox takes about 0.2 ms (2,000 objects in 433 ms, including the search for other objects with the same name). Details: `TibiaFoundry-9.0-Audit.md`.

## Version 9.0

* One version source, `src/Directory.Build.props`. The About page, the window, the installer, the package names, the User-Agent and the website all say 9.0. Windows' file version is 9.0.0.0 (Windows requires four numbers; this form is never shown).
* Packages: `TibiaFoundry-9.0-Installer.msi`, `TibiaFoundry-9.0-Portable.zip`, `TibiaFoundry-9.0-Source.zip`.

## Compatibility

* Settings, item catalogs, client archives, Asset Libraries and exports of 8.8.2 and earlier are used as they are; nothing is migrated or rewritten. The MSI upgrades 8.x in place (same upgrade code) and never touches user data in `%LOCALAPPDATA%\TibiaFoundry` or the output folders. The portable edition keeps using the `TibiaFoundry-Data` folder next to it.
* All new texts are translated into the five interface languages.
* Dependencies: System.Drawing.Common 10.0.12 → 10.0.14 (patch). No known vulnerable packages.
* Not code-signed (Windows SmartScreen may ask once).
