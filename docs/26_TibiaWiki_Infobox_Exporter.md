# 26 TibiaWiki Infobox Exporter (9.0)

**Where:** Appearances → select an object → **Wiki infobox** tab.
**What:** MediaWiki markup for TibiaWiki's [`Template:Infobox Object`](https://tibia.fandom.com/wiki/Template:Infobox_Object), filled from the installed client's own data, editable before it is copied or saved.

## Using it

1. Open **Appearances** and load the client's appearances (automatic when *Load data automatically* is on).
2. Select an object (the list must show *Objects*; outfits, effects and missiles have no item infobox).
3. Open the **Wiki infobox** tab. Every parameter is listed with its value and where the value came from:

   | Source shown | Meaning |
   |---|---|
   | Client data | Read from appearances.dat (flags, slot, level, vocations, NPC trades…). |
   | Derived – review | Worked out from client data by a documented rule (article-cased name, classification from the market category). Check it. |
   | TibiaWiki | The article name from TibiaWiki's *Item IDs* list. |
   | Template placeholder | Structure the template expects (`{{Dropped By|}}`), not data. |
   | Needs your input | Required by the template but not in the client's files (implemented, weight, value, notes…). |
   | Entered by you | A value you typed or changed. |

4. Edit any value (notes and history accept several lines). **Add** inserts further parameters of the template (attack, armor, attrib, resist…) at their usual position.
5. **Copy markup** puts the infobox on the clipboard; **Save as .txt…** writes it to a file. **Save all selected…** writes one `Name (id).txt` per selected object into a folder (existing files are never replaced: `Name (id) 2.txt`…).

Options: *List missing required parameters* writes them as empty lines (so the gaps are visible); *Write ? for unknown values* fills them with `?`, TibiaWiki's convention for "unknown". **Use TibiaWiki article names** downloads the wiki's *Item IDs* list (cached 24 hours, the same cache the NPC trade export uses) so `name` is the article title; otherwise the client name in article casing is used and marked for review. The tab never goes online by itself.

## What is filled in, and from where

Checked on 2026-11-06 against the template's source and the item pages Magic Plate Armor, Sword, Gold Coin, Bow, Crossbow, Spellbook, Wand of Vortex, Snakebite Rod, Scroll, Parchment, Backpack, Torch, Health Potion and others.

| Parameter | From the client | Rule |
|---|---|---|
| `name` | object name | TibiaWiki article name when the item list is available, else article casing ("magic plate armor" → "Magic Plate Armor") |
| `actualname` | object name | as the client shows it |
| `itemid` | object ID | other objects with the same name are reported (TibiaWiki lists all IDs of one item) |
| `objectclass`, `primarytype`, `secondarytype` | market category | only categories whose wiki classification is consistent (Armors → Body Equipment / Armors, Swords → Weapons / Sword Weapons, Potions → Plants, Animal Products, Food and Drink / Liquids / Potions…); Wands and Rods by vocation, spellbooks by vocation, bows, crossbows and throwing weapons by weapon type |
| `flavortext` | look text (`Appearance.Description`) | as the client writes it, with the closing full stop the game adds on Look and TibiaWiki writes (9.0.2); left out when the object has none |
| `slot` | clothes slot | Head, Neck, Container, Body, Shield Hand, Weapon Hand, Legs, Feet, Finger, Extra Slot, Both Hands |
| `hands`, `weapontype` | weapon type + slot | One/Two; Sword, Axe, Club, Distance (bow, crossbow, throwing) |
| `levelrequired`, `vocrequired` | minimum level, vocation restriction | "knights and paladins" style; promoted-only items are reported |
| `imbueslots`, `upgradeclass` | imbueable, upgrade classification | |
| `immobile`, `walkable`, `pickupable`, `usable`, `rotatable`, `hangable`, `holdsliquid`, `wrappable`, `unshootable` | flags unmove, unpass, take, usable, rotate, hang, liquidcontainer, wrap, unsight | `unshootable` only for objects that cannot be picked up |
| `writable`, `rewritable`, `writechars` | write / write once | |
| `lightradius`, `lightcolor`, `mapcolor` | light, automap colour | palette indices, as `{{RGB Light Color}}` expects |
| `stackable`, `marketable` | cumulative, market | pickupable objects only |
| `npcvalue`, `npcprice`, `buyfrom`, `sellto` | NPC sale data | highest price an NPC pays / lowest price an NPC asks; other NPCs listed with their price when it differs ("H.L.: 720"); `--` and `0` when nobody trades; trades in other currencies are left out and reported |
| `droppedby` | – | `{{Dropped By|}}` (filled by the wiki from creature pages) |

**Never filled in** (not in the client's files): `implemented`, `weight`, `value`, `attack`, `defense`, `armor`, `attrib`, `resist`, `plural`, `article`, `notes`, `history` and the other combat and lore parameters. Required ones are listed empty and marked *Needs your input*.

## Markup rules

The output follows TibiaWiki's item pages: `{{Infobox Object|List={{{1|}}}|GetValue={{{GetValue|}}}`, one `| name = value` line per parameter with aligned equals signs, `}}`. Values are made safe for a template argument: a literal `|` becomes `{{!}}` (links `[[a|b]]` and nested templates are kept), a stray `}}` or an unclosed `{{` is written as HTML entities, single-line values lose line breaks.

## For developers

* Core: `TibiaFoundry.Core.Wiki.WikiTemplate` (template name, leading arguments, ordered `WikiField`s with a `WikiFieldSource`), `WikiTemplateWriter` (formatting and escaping) and `InfoboxObjectBuilder` (the Infobox Object rules). A new export (creatures, NPCs, other templates) is another builder that returns a `WikiTemplate`; the writer and the panel's editing model are reused.
* App: `InfoboxViewModel` / `InfoboxPanel` (Appearances page, tab index `AppearancesViewModel.InfoboxTab`). The infobox is built only while the tab is visible.
* Tests: `InfoboxExportTests` (complete and incomplete data, special characters, multiline values, several attributes and trades, file names, every object of the installed client).
* Tool manifest: operations `infobox-export` and `wiki-item-ids`, output *TibiaWiki infobox (.txt)*.
