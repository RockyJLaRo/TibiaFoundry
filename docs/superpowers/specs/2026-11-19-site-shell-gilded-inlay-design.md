# Tibia Foundry site shell and homepage: Gilded Inlay design (19 November 2026)

Status: draft for the owner's approval.
Source: the owner's master brief (pasted 19 Nov 2026), sections 2, 3, 11, 12, 14, 15, 17 and 20.6.
This is **sub-project 1 of 4**. The order agreed with the owner is:
1. Site shell, homepage and credits clean-up.
2. Remaining Tier 1 calculators.
3. Source-code library, MIT licensed, tool code only.
4. Saved characters.

## 1. Intent

Visitors should understand within seconds that Tibia Foundry is three things working together:
- **a desktop app for creators.** This is the flagship and comes first.
- **online Tibia tools.**
- **(soon) code that other fansites can reuse.**

The site went live with the Ivory & Sapphire look on 18 Nov 2026. The owner asked for a substantial visual change to gold and obsidian. From three mockups, the owner chose **C, "Gilded Inlay"**:
- cool slate-obsidian surfaces
- gold used sparingly, as thin inlaid lines, edges and icons
- a sharp, engineered sans-serif

The goal is to look like professional software, not a fantasy forum.

## 2. Goals, non-goals, anti-goals

**Goals**
- A new identity across every existing page, in 5 languages × 2 themes. The page code stays the same; the colours, type and components change.
- **Header**
  - A global Tibia search across the top of every page, with a type picker.
  - A new menu: Application, Online Tools, Community Data, Fansite Tools, Contact. It uses dropdown panels on desktop and an accessible drawer on phones.
- **Homepage, app first**
  - An app hero with real screenshots and the Installer, Portable and Source downloads.
  - The Today in Tibia strip: boosted creature, boosted boss, Rashid, server save.
  - The three paths.
  - App features and a screenshot showcase.
  - Calculators and community shortcuts.
  - News.
  - Fansite and developer teaser.
  - A second download section.
  - The footer.
- **Credits:** the "Data & resource credits" block is currently printed on every page from `layout.html`. It moves into a compact footer notice plus a full Attribution section on the Legal page. No required notice is lost.
- **English Title Case** for page titles, menu items, section headings, tool names and buttons. Other languages keep their own rules.
- **Contact:** restyled. The creator identity, email and all profile links stay unchanged. The listed contact reasons each open an email with a matching subject line.
- **Screenshot matrix** extended to the brief's 7 viewports (320, 375×812, 430×932, 768×1024, 1280×800, 1440×900, 1920×1080) × 5 languages × 2 themes. Every image is inspected before approval.

**Non-goals (later sub-projects)**
- New calculators.
- Source ZIPs, code viewer and embeds.
- Saved characters and favourites.
- Changelog and Screenshots pages. The Application menu links only to pages that exist today: Downloads, Features, Guides, About.
- Changing the desktop app.

**Anti-goals**
- Any wrong or stale value shown as live.
- A menu entry that leads nowhere.
- Lost downloads, analytics (`G-J3EX3FB3PB`) or legal notices.
- Horizontal scroll at 320 px.
- Contrast below WCAG 2.2 AA in either theme.
- Touching `games/`, `pokemon/`, `tools/`, `.github/`, `CNAME` or `robots.txt` on Pages.
- Calling Google Fonts at runtime.

## 3. Constraints

- **Static site:** built by `website/build_site.py` from `src/registry.json`, templates and locale files. Deployed only through `scripts/deploy_site.py` with `--checksums <Release/9.0.5/SHA256SUMS.txt>`, and only with the owner's approval.
- **Search:** everything uses the existing TibiaData v5 service, `src/js/tibiadata.js`. Search only offers what the API actually supports:
  - character and guild need an exact name
  - worlds, creatures and spells are filtered from their full lists
  - houses need a world and a town
- **Privacy:** every new `localStorage` key is listed on the Legal page.
- **Fansite Agreement:** CipSoft credit stays in the footer, and the site makes no claim of official status.

## 4. Approaches

### Styling technology (owner delegated this decision)

| | Option | Verdict |
|---|---|---|
| **Chosen** | **Modern plain CSS with design tokens**, split into cascade layers (`@layer tokens, base, components, pages, utilities`). Python concatenates and minifies it inside `build_site.py`. | Fits the Python-only site build with no new toolchain. Theming already runs on tokens. The CSS that ships stays readable, so fansite owners can restyle the downloadable tools (sub-project 3) by editing a few variables. |
| Rejected | Tailwind CSS (v4 standalone CLI) | Adds a Node/binary build step to every site build. `data-app.js` builds about 1,000 lines of markup at runtime with class strings, so those classes would need safelists. Most importantly, downloadable tool packages would carry either a utility blob or a Tailwind dependency, which works against "easy to customise without our toolchain". |
| Rejected | CSS Modules, or a component framework | Needs a bundler and a framework migration for 13 templates. No benefit to players. |

What changes in practice:
- **Files:** `src/styles.css` (54 KB, 702 lines) becomes `src/css/` with `tokens.css`, `base.css`, `components.css`, `pages/*.css` and `utilities.css`.
- **Token names** follow the brief's semantic scheme: `--bg-canvas`, `--bg-surface`, `--bg-elevated`, `--bg-input`, `--border-subtle`, `--border-strong`, `--text-primary/secondary/muted`, `--accent-primary/hover/subtle`, `--status-*`, `--radius-*`, `--space-*`, plus a `--inlay` gold line token.
- **Fluid type** uses `clamp()`. Layout uses grid with `minmax()` and `auto-fit`, and container queries for cards and the calculator workbench. Mobile height uses `dvh` and safe-area insets.
- **Fonts:** Barlow (body) and Barlow Semi Condensed (headings), both under the SIL Open Font License. They are **self-hosted** as WOFF2 files with Latin and Latin-Extended subsets, which covers pl, de, pt-BR and es. The OFL text goes into THIRD-PARTY-NOTICES.
- **No new runtime dependencies.** The only new files are the two font families.
- The choice is documented in `docs/ARCHITECTURE.md`, under "Styling".

### Homepage direction (owner picked C)

A: Forge (warm, glowing frame, serif) and B: Obsidian & Alabaster (dark band over light sections) were shown and not chosen.

## 5. The design

### Tokens: Gilded Inlay

Contrast targets and their exact values are verified by the existing contrast check.

| Token | Dark | Light |
|---|---|---|
| canvas | `#111417` slate-obsidian | `#f4f3f0` cool stone-white |
| surface / elevated | `#161a1e` / `#1c2126` | `#ffffff` / `#ebe9e4` |
| border subtle / strong | `#2b3137` / `#4a525b` | `#d9d5cc` / `#9a9384` |
| text primary / secondary | `#ece9e2` / `#a9b0b8` | `#1d2024` / `#4d535b` |
| accent (gold) | `#c9a35a`; text on it `#121212` | `#8c6a24`, darkened if needed to reach ≥ 4.5:1 on canvas; white text on it |
| inlay line | gold at 40 % opacity, 1 px | `#b89650` at 50 %, 1 px |

- **Shape:** small radii (2–6 px).
- **Brand mark:** a small gold-outlined diamond next to the "Tibia Foundry" wordmark. The existing logo stays for the favicon and the app.
- **Gold is for:** the active nav underline, the primary button, focus rings, card left edges and section dividers. It is never used for large fills.
- **Theme behaviour stays as it is today:** the system preference applies unless the visitor chooses one, which is stored in `tf-theme`. An early script prevents a flash of the wrong theme, `color-scheme` stays in sync, and the `theme-color` metas are updated.

### Header (every page)

```
┌ [Character ▾][ Search characters, guilds, worlds, creatures, spells… ][Search] ┐  search row
├ ◇ Tibia Foundry   Application▾ Online Tools▾ Community Data▾ Fansite Tools Contact   EN▾ ◐ [Download] ┤
└──────────────────────────── gold inlay line ───────────────────────────────────┘
```

**Search**
- Search types: Character, Guild, World, Creature, Spell, House. Each one goes to the existing data route.
- Character and guild open the exact-name page and show a "not found" state.
- World, creature and spell show real suggestions from the full lists, loaded on demand. The suggestions support arrow keys, Enter and Escape.
- House opens the houses page with the world and town pickers.
- Up to 8 recent searches are kept in `localStorage` key `tf-recent-searches`. They can be cleared and are listed on the Legal page.
- Loading and error states come from the existing API service.

**Menu**
- **Desktop:** disclosure dropdown panels (buttons with `aria-expanded`, not an ARIA menu). They close with Escape, on an outside click, and when focus leaves.
  - **Application:** Download, Features, Guides, About.
  - **Online Tools:** a "Calculators" group with the tool list from the registry, and "Today in Tibia".
  - **Community Data:** Characters, Guilds, Worlds, Highscores, Houses, Creatures, Spells, Kill Statistics, Bosses, News, Fansites.
- **Phones (< 1024 px):** a drawer with collapsible groups. The language and theme controls are inside it. It traps focus, closes with Escape or a backdrop click, and returns focus to the button.
- **Fansite Tools:**
  - Until sub-project 3 ships, it is a single link to the public GitHub repository, labelled "Source on GitHub".
  - When the library ships, it becomes the directory.

All menu lists come from `registry.json`, never from hand-written HTML.

### Homepage (top to bottom)

1. **App hero:**
   - H1 "Everything You Need to Create for Tibia." and the one-sentence pitch.
   - Download 9.0.5 Installer (primary), Portable, Source.
   - Version, date and the SHA-256 link from the release checksums.
   - The Asset Library screenshot in a thin gold frame, a dark or light screenshot to match the theme.
   - The trust line "Windows 10/11, free, no account, works offline".
2. **Today in Tibia strip:** four compact cells.
   - Boosted creature and boosted boss are live from TibiaData, each linking to its creature or boss page.
   - Rashid and the server save countdown come from the existing verified formulas.
   - While loading the cells show skeletons. When unavailable a cell is hidden, never filled with a guess.
3. **Three paths:** Get the Desktop App, Use Online Tools, Build Your Fansite. The last links to GitHub until the library exists.
4. **App features:** the existing features content in 6 tiles, plus a 4-screen showcase strip (Asset Library, Extract Sprites, Map Tools, Content Studio) with captions.
5. **Calculators and community shortcuts:**
   - The 4 live calculators come from the registry.
   - The community shortcuts are Characters, Guilds, Worlds, Highscores, Bosses and Kill Statistics.
6. **News:** the existing live news band.
7. **For Fansite Builders:** a short teaser. It says that sub-project 3 will offer the code behind every tool under MIT, with GitHub available now. There is no fake download button.
8. **Second download section:** repeats the three downloads.
9. **Footer:**
   - Columns: Application, Online Tools, Community Data, Contact.
   - A creator line: Rocky LaRochelle, with links to the profiles.
   - The compact required notices: CipSoft ©, TibiaData attribution, the independent-fan-project statement, and a link to the full Attribution section on the Legal page.
   - The analytics opt-out link stays.

**Phones:** a single column. The hero screenshot sits under the buttons. The status strip becomes 2×2. Touch targets are at least 44 px.

### Fix included: stale boosted creature

`tibiadata.js` caches `creatures` (which carries the boosted creature) and `boostablebosses` for up to 24 h in sessionStorage. A tab kept open across server save therefore shows yesterday's boosted creature. The fix: the cache for boosted data expires at the **next server save** (`FORMULAS.nextServerSave`) or after the normal TTL, whichever comes first. A unit test covers this.

### Other pages

- Every page gets the new tokens, header and footer.
- The calculators keep their layout and logic. Only their styles change, so their verified outputs are untouched. The existing e2e value checks prove this.
- Contact: email and profiles stay, restyled as a two-column card. Contact reasons and their email subjects:
  - General inquiry
  - Bug report
  - Calculator formula correction
  - Fansite integration
  - Application support
  - Developer question

## 6. Testing and release gates

- `build_site.py --strict --checksums …`
- `tests/check_i18n.py`: the 5 languages stay in parity, including the new menu, search and Title Case keys.
- Unit tests: the formulas, plus the new boosted-cache test.
- `tests/e2e.py`: the existing 14+ checks pass, plus new ones:
  - the menu with keyboard and Escape
  - drawer focus trap
  - search types and recent searches
  - no overflow at 320 px
  - theme with no flash
  - contrast in both themes
  - footer notices present on every page
  - credits block gone from page bodies
- **Screenshot matrix:** 7 viewports × 5 languages × 2 themes for every page, with a manifest. I inspect the images, fix what I find, and send you the key set before asking to deploy.
- Local preview on port 8765, then your approval, then deploy. Nothing is published, committed or pushed without your yes.

## 7. Left to the builder

- **Branch:** create `site-shell` from `calculators`, so it has the live calculators.
- Exact spacing and type scale within the tokens. Icon choices from the Lucide sprite.
- Which 4 app screenshots go in the showcase, picked from `images/screens/<lang>/`. Each language gets its own screenshots.
- Title Case wording of English strings. Other languages are only touched where a string itself changes.
- Fetching the Barlow WOFF2 files (OFL, from the official Google Fonts / Barlow repository). I'll name the files, source and size when I fetch them.

### Calls I made (check these while reading)

- The Today strip sits directly under the hero, not after the showcase as in the brief's list. This puts the daily-return information above the fold. It matches the mockup you picked.
- News and the creator line stay. Creator identity moves from a homepage band into the footer and Contact.
- Changelog and Screenshots pages are deferred, so the menu has no dead links.
- Fansite Tools links to GitHub until sub-project 3 ships.
