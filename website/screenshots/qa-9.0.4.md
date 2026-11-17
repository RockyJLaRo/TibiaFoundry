# Screenshot QA – Tibia Foundry 9.0.4 website (2026-11-15)

What was checked when the website screenshots were recreated for 9.0.4, what was corrected, and what remains in
the program itself. The live state of every screenshot is in [coverage-report.md](coverage-report.md); the
locations are in [inventory.md](inventory.md).

## Scope

* **19 screenshot locations** (18 gallery tabs of 8.8.0 plus the new *Appearances → Wiki infobox*), used in the
  gallery, the hero, the Asset Library close-up (a crop), the Content Studio spotlight and the link-preview images.
* **5 languages** (en, pt-BR, pl, es, de — from `build_site.py`, each confirmed against the program's
  `Strings.<code>.xaml`) × **2 themes** = **190 screenshots**, all captured from the running 9.0.4 program.
* Replaced: the 180 screenshots of 8.8.0 (18 × 5 × 2; moved out of the project to
  `Source_History/_website-screenshots-8.8.0-backup/`, together with the obsolete `prepare_images.py`).

## How they were checked

1. **Automatic checks on every capture** (`screenshots.py`): 1586×893, theme brightness of page and title bar,
   not blank, the program's own per-shot result, the language the program started in.
2. **Determinism**: five complete runs plus forced re-captures. Captures whose inputs did not change are
   bit-identical; every change between runs was one of the corrections below (run 2: 130 of 190 identical, run 3:
   160, run 4: 184, run 5: 183 – the 6 Publishing-profile pictures and one log-size line). The comparison is what
   found the animation, scroll-position and log-size problems below.
3. **Visual review** of the full-size captures (all 19 pages in Polish; German, Portuguese and Spanish spot checks)
   and of the contact sheets (`screenshots.py sheets`: every location in all 5 languages × 2 themes side by side).
   All 190 are recorded as reviewed (`screenshots.py approve`), tied to their file hashes.
4. **The built website** (local server, built-in browser): every `<img>` on all 5 language pages resolves
   (50 per page); each page shows only its own language's screenshots; every dark picture has its light twin; no
   unused image; theme toggle switches every screenshot to the matching variant (repeated); switching language
   with the selector keeps the gallery tab and theme and shows the new language's variants (es → de → pt-BR → pl →
   en, alternating themes); header and page checked at 13 widths (320–1920 px) × 5 languages: no overlap, no
   horizontal scrolling; no console messages; every screenshot keeps its aspect ratio.
5. **Program tests**: `dotnet test` – 695 passed, 0 failed, 32 skipped (unchanged skips that need external data).

## Corrected

### Website
* **Screenshots now show 9.0.4** (they showed 8.8.0: no Wiki infobox tab, old Appearances tabs, version 8.8.0).
* **New gallery tab "Wiki infobox"** (the 9.0 headline feature had no screenshot), with captions in 5 languages.
* **Page names matched the screenshots in no language but English.** The site kept the program's page names in
  English ("the program itself is in English"), but the screenshots show the translated program (*Asset-Bibliothek*,
  *Biblioteka zasobów*, *Biblioteca de assets*…). Gallery tab labels, alt texts, captions, window titles, tool lists
  and the instructions that name pages (*Settings → Attribution*, *Content Studio → New project*, *Appearances →
  Wiki infobox*…) now use the program's own translations via `{app:Key}`; sub-view tabs too (pl *Profile
  publikowania* → the program's *Profile publikacji*, pt-BR *Duplicatas* → *Duplicados*).
* **Link previews** (`og-image-<lang>.png`) used the English screenshot for every language; each uses its own now.
* **Alt texts** named the program page in English for every language; they use the translated name now.
* **No fallback any more**: a missing screenshot of a language stopped nothing before (English was shown instead);
  now the build fails, so one language never stands in for another.
* **Header: the version ran into the menu** (es at 1280 px: "9.0.4" over "Funciones") because the logo block could
  shrink; and between 1200 and 1279 px the full menu did not fit in Portuguese and Spanish (horizontal scrolling).
  The logo block no longer shrinks next to the full menu, and the menu button is used below 1280 px.
* Naming: `site/img/screens/<id>-<code>-<theme>.webp` (e.g. `home-pt-BR-dark.webp`) instead of `img/<id>-<theme>`
  plus `img/<folder>/<id>-<theme>`.

### Capture (program's `--site-screenshots` mode only; no effect on normal use)
* **Dark "Settings" said "Active theme: Light"**: the theme was applied for the capture without the preference
  following it. The preference now follows in memory during the capture (never saved).
* **"Recently used tools" on Home differed between languages and between light and dark** (the capture visited
  pages and the program saved them). Each run now starts from the same demo data, and the list is restored before
  every shot.
* **Animated previews were caught on random frames**; animations are paused at their first frame for the capture.
* **"Settings → Attribution" could be scrolled to the wrong place** (the scroll is measured on the layout of the
  moment, which is not settled right after a theme change or when the page opens on that category); the category is
  selected again once the layout is settled.
* **Publishing profiles showed a different profile in each language** (the list is sorted by the profiles'
  translated names and the capture took the first); it now selects *Article publishing* by its id.
* Old log files are cleared before each run (Settings shows their number). What remains variable is the size of the
  current log file in *Settings → Attribution* (e.g. "2.23 KB", about 0.02 % of the pixels).

## Found in the program (9.0.4) – not changed

The screenshots show the program exactly as 9.0.4 is released, so these are visible in the non-English pictures.
Changing the program only for the website would show users something the 9.0.4 download does not have; they belong
in a program release, after which `screenshots.py capture --incremental` refreshes every affected picture.

**Untranslated interface text**
* Content Studio workflow steps: titles *Resize*, *Folders*, *Manifests*; parameter labels (*Creatures*,
  *Animation*, *Direction*, *Animated*, *Mode*, *Scale*, *Width*, *Height*, *Padding*, *Alignment*, *Background*,
  *Format*, *Description*, *Tags*, *Columns*, *Labels*, *Title*, *Theme*, *Kind*, *IDs*, *Names*, *Objects*,
  *Outfits*, *Addons*, *Colors*, *Collection*, *Search*, *Category*, *Folder*); choice values (*Moving*, *South*,
  *Scale*, *Bottom*…) and the step summary (`Mode: Scale · Scale: 2`, "not …"). Source: literals in
  `src/TibiaFoundry.Core/Library/Workflows.cs` (`WorkflowSteps.All`, `WorkflowStep.Summary`). The program already has
  the mechanism for translated display of stored identifiers (`loc:Choice.Prefix`, `L.TOr`).
* Content Studio project list: run outcome *Completed* / *Cancelled* / *Failed* is stored and shown in English
  (`ContentStudioViewModel` records it, `ContentProject.Status` shows it).
* Home: workflow buttons *Open <page>* (`HomeViewModel.StartLabel`: `"Open " + title`).
* Asset Library: *All assets*, and the built-in category names (*Creatures*, *Generated Images*, *Others*) shown as
  stored (`AssetLibraryViewModel.AllAssets`, `LibraryModels`).
* Appearances → Outfitter: the direction under the preview (*Kierunek South*).
* Item Catalog: sort choice *By ID*; record source *official*.
* Assets Editor: flag names *Ground*, *Container*, *Stackable*, *Usable*, *Multi-use*, *Writable* (others are translated).
* Compare Files: the state badge *Changed* in every entry.
* RCC Editor: the size unit *bytes*.

**Number format**: some counts use English digit grouping in every language (Extract Sprites *5,084 wyświetlane
arkusze*, Item Catalog *186 z 43,518*), next to correctly grouped ones (*1 274 zasoby*).

**Clipped text in longer languages** (fixed column widths)
* Extract Sprites: column headers *First ID* / *Last ID* in pt-BR, pl and es (*Primeiro I…*, *Pierwsze I…*), the
  size filter (pl *Wszystkie rozmi…*, pt-BR *Todos os tamanh…*), the label *Recursos del clie…* (es).
* Item Catalog: the *Agreement* header (de *Übereinstimmu…*).
* Compare Files: the category subtitles (es *… 0 eliminad…*).
* Clients: the *In use* badge (de *In Verwe…*, pl *W użyciu*), the *Archived* header (pl *Zarchiwizowa…*), the
  *Kind* cell (pl *Zainstalowany klient (launche…*).
* Settings: the category *Benachrichtigungen und Protokollierung* (de).
* Navigation: long page names end in "…" (pl *Narzędzia zaawansowa…*, *Wyodrębnianie sprite'…* when selected;
  de *Client-Installation wied…*) – by design (ellipsis), but frequent in Polish and German.

**Language-neutral on purpose (not issues)**: the brand line *Tibia Foundry – All your Tibia-Tools for TibiaWiki*
(the program checks it in its own UI test), game data (item, creature, house and area names, static-data and map
field names, catalog values), TibiaWiki template parameters in the infobox, user data in the demo copy (project and
collection names, library descriptions and tags written at export time), file paths.
