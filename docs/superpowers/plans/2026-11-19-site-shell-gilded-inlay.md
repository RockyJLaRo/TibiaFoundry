# Site Shell and Homepage (Gilded Inlay) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give tibiafoundry.com the Gilded Inlay identity, plus:
- a registry-driven header with global Tibia search and a grouped menu
- an app-first homepage
- consolidated credits
- a restyled Contact page

It also fixes the stale boosted-creature cache, and nothing that works today breaks.

**Architecture:**
- The existing Python static generator (`website/build_site.py`) and templates stay.
- CSS moves from one file to layered files in `website/src/css/`, which the build concatenates and minifies into the same `assets/site.css`.
- Menu groups and search types live in `registry.json`.
- New behaviour goes in two small scripts, `nav.js` and `search.js`, that use the existing `TFData` service.

**Tech Stack:** Python 3 (build, i18n and contrast checks), plain JS (UMD, no bundler), `node --test` unit tests, Playwright via `website/tests/e2e.py` and `site_screenshots.py`. No new runtime dependencies apart from the self-hosted Barlow WOFF2 fonts (OFL).

**Spec:** `docs/superpowers/specs/2026-11-19-site-shell-gilded-inlay-design.md`

## Global Constraints

- **Branch:** work on `site-shell`, created from `calculators` (head `0510f99f`) in the worktree `F:\Users\Rocky\Desktop\TibiaFoundry_Master\Work_Redesign\repo`.
  - Local commits on that branch are fine.
  - **Never push, merge, deploy or publish** without the owner's explicit yes.
  - No force-push, `git reset --hard` or `git clean`.
- **Pages paths:** never modify `games/`, `pokemon/`, `tools/`, `.github/`, `CNAME` or `robots.txt` in the Pages repo. Deploy only via `scripts/deploy_site.py`.
- **Build command:** `python -I website/build_site.py --strict --checksums F:/Users/Rocky/Desktop/TibiaFoundry_Master/Work_9.0.5/release/Release/9.0.5/SHA256SUMS.txt`.
  - It must pass at the end of every task.
  - Afterwards, run `git checkout -- website/site` whenever the only diff in `website/site` is line endings.
- **Languages:** en, de, pl, es, pt-BR (English is the default). Every new user-facing string goes in all 5 locale files, with a real translation, never English copied over. `python -I website/tests/check_i18n.py` must pass.
- **English Title Case** applies to page titles, menu items, section headings, tool names and buttons. Small words (a, an, and, as, at, but, by, for, in, of, on, or, the, to, vs, with) stay lowercase unless they come first or last. Other languages follow their own conventions.
- **Tokens:**
  - Dark: canvas `#111417`, surface `#161a1e`, elevated `#1c2126`, border `#2b3137` / `#4a525b`, text `#ece9e2` / `#a9b0b8`, accent `#c9a35a` with `#121212` text on it, inlay = accent at 40 % opacity, 1 px.
  - Light: canvas `#f4f3f0`, surface `#ffffff`, elevated `#ebe9e4`, border `#d9d5cc` / `#9a9384`, text `#1d2024` / `#4d535b`, accent `#8c6a24` (darken until ≥ 4.5:1 on canvas) with white text on it, inlay `#b89650` at 50 %.
  - Radii 2–6 px.
- **Fonts:** Barlow 400/500/600 (body) and Barlow Semi Condensed 600/700 (headings), WOFF2, Latin and Latin-Extended subsets, served from `/assets/fonts/`. No runtime request to Google Fonts. The OFL text goes in `website/THIRD-PARTY-NOTICES.md`.
- **Accessibility:** WCAG 2.2 AA contrast in both themes. Touch targets ≥ 44 px. Visible gold focus ring. `prefers-reduced-motion` respected. No horizontal overflow at 320 px.
- **Keep unchanged:** analytics ID `G-J3EX3FB3PB` and the opt-out, the early theme script (`tf-theme`), all download links and checksums, the contact email, and the creator profiles.
- **New `localStorage` key:** `tf-recent-searches`. List it on the Legal page.
- **Calculator outputs must not change:** the existing `calculators` and `today-in-tibia` e2e checks must keep passing unmodified.

## Review Focus

1. **Long German or Polish menu labels at 1024–1280 px** must not wrap or overflow. The header switches to the drawer first. Test in Task 4.
2. **JavaScript blocked or failed:** every menu destination is still reachable, because each one is also a footer link, and the search form still submits to `tibia/`. Test in Task 4.
3. **localStorage blocked:** theme toggle, search and recent searches work without throwing, and recent searches are simply not kept. Test in Task 5.
4. **TibiaData down:** the boosted cells are hidden, Rashid and server save still show, world/creature/spell suggestions show the error state, and character search still navigates. Test in Task 6.
5. **Character names with spaces, apostrophes or accents** (for example `Roc ky`, `Mc'Lovin`, `Zé Ninja`) are encoded correctly into the `#/character/...` route and land on the right page. Test in Task 5.

---

### Task 1: Boosted Data Expires at Server Save

**Files:**
- Modify: `website/src/js/tibiadata.js` (factory deps, `get()` freshness check, browser wiring on line 16)
- Modify: `website/build_site.py:651-653`, so `formulas.js` is listed **before** `tibiadata.js` on `home` and `tibia`
- Modify: `website/src/registry.json` → `api.expireAtServerSave: ["creatures", "boostablebosses"]`
- Test: `website/tests/unit/tibiadata.test.mjs`

**Interfaces:**
- Produces: the factory accepts `deps.nextServerSave(ms) -> ms`, meaning the first server save strictly after `ms`.
  - In the browser it is wired as `function (ms) { return root.TFFormulas.nextServerSave({ now: ms }).at; }` when `root.TFFormulas` exists.
  - Otherwise it is absent, and the freshness check falls back to TTL only.
  - A cached entry of a kind listed in `config.expireAtServerSave` is fresh only while `now() < nextServerSave(entry.t)` **and** it is within its TTL.

- [ ] **Step 1: Create the branch.** Run `git switch -c site-shell calculators`.
- [ ] **Step 2: Write the failing tests** in `tibiadata.test.mjs`:
  - `test("boosted creature cache expires at server save")`
    - Setup: fake clock at `Date.UTC(2026,10,19,8,50)` (09:50 Berlin), `nextServerSave` from `formulas.js`, `get("creatures")` → 1 fetch.
    - Advance the clock to `Date.UTC(2026,10,19,9,5)` (10:05 Berlin) and call `get("creatures")` again.
    - Assert 2 fetches.
  - `test("boosted cache within the same server day is reused")`: same setup, but the clock moves to 09:59 Berlin. Assert 1 fetch.
  - `test("spells cache ignores server save")`: `spells` across 10:00. Assert 1 fetch.
  - `test("no nextServerSave dependency: TTL only")`: `creatures` across 10:00 with no dep. Assert 1 fetch.
- [ ] **Step 3: Run.** `node --test website/tests/unit` → the 2 server-save tests FAIL.
- [ ] **Step 4: Implement** the freshness rule from Interfaces. The stale-on-error fallback is unchanged.
- [ ] **Step 5: Run.**
  - `node --test website/tests/unit` → all PASS.
  - Run the build command.
  - `python -I website/tests/e2e.py --only tibia-data,today-in-tibia` → PASS.
- [ ] **Step 6: Commit** with the message `fix(data): boosted creature/boss cache expires at server save`.

### Task 2: Layered CSS Architecture (No Visual Change)

**Files:**
- Create: `website/src/css/tokens.css`, `base.css`, `components.css`, `pages/home.css`, `pages/data.css`, `pages/calc.css`, `pages/content.css`, `utilities.css`
- Delete: `website/src/styles.css`, after its rules have moved
- Modify: `website/build_site.py` → `asset_files()` and a new `css_bundle() -> str`
- Create: `docs/ARCHITECTURE.md`, with a "Styling" section covering the decision, the layer order, how to add a token, and why Tailwind was rejected (copy the reasons from spec §4)

**Interfaces:**
- Produces: `css_bundle()` returns the following, in this fixed order, minified:
  1. `@layer tokens, base, components, pages, utilities;`
  2. each file wrapped in its `@layer`
  3. the `pages/*.css` files in sorted order

  Minification here means removing comments, collapsing whitespace, and keeping strings and `url()` intact.
  The published name stays `site.css` with its content hash.
  `--strict` fails if any `src/css/**/*.css` file is not in the bundle, or if `src/styles.css` still exists.

- [ ] **Step 1: Capture baseline screenshots.** Run `python -I website/tests/site_screenshots.py --views laptop,small-mobile --out <scratch>/before`. If the script has no `--out` option, add one; it defaults to the current output folder.
- [ ] **Step 2: Move the rules.** Move every rule from `styles.css` into the matching layer file without changing its values. Tokens and theme blocks go in `tokens.css`. Element defaults go in `base.css`.
- [ ] **Step 3: Implement `css_bundle()`** and the strict checks.
- [ ] **Step 4: Compare screenshots.**
  - Run the build, then repeat the screenshot run to `<scratch>/after`.
  - Pixel-compare with Pillow `ImageChops.difference(...).getbbox() is None` for every pair.
  - Fix any difference. Cascade layers can change specificity winners, so watch for that.
- [ ] **Step 5: Run the full e2e suite.** `python -I website/tests/e2e.py` → PASS.
- [ ] **Step 6: Commit** with the message `refactor(css): split stylesheet into cascade layers`.

### Task 3: Gilded Inlay Tokens, Fonts and Contrast Check

**Files:**
- Modify: `website/src/css/tokens.css`
  - Add semantic tokens named as in spec §2: `--bg-canvas`, `--bg-surface`, `--bg-elevated`, `--bg-input`, `--border-subtle`, `--border-strong`, `--text-primary`, `--text-secondary`, `--text-muted`, `--accent-primary`, `--accent-hover`, `--accent-subtle`, `--accent-on`, `--inlay`, `--status-success`, `--status-warning`, `--status-danger`, `--radius-small`, `--radius-medium`, `--radius-large`, `--space-1` … `--space-8`, `--font-body`, `--font-display`.
  - Map the old token names to the new ones, then replace old names across `src/css`.
- Modify: the early-theme block and `theme-color` metas in `src/templates/layout.html`: dark `#111417`, light `#f4f3f0`.
- Create: `website/src/fonts/` with the WOFF2 files.
- Create: `website/src/css/fonts.css`, the `@font-face` rules with `unicode-range` for Latin and Latin-Extended and `font-display: swap`. It comes first in the `base` layer.
- Modify: `build_site.py`, so fonts are copied to `site/assets/fonts/`.
- Modify: `website/THIRD-PARTY-NOTICES.md`, adding Barlow's OFL 1.1 text and copyright.
- Create: `website/tests/check_contrast.py`

**Interfaces:**
- Produces: `check_contrast.py` parses the dark and light token blocks from `tokens.css` and asserts these pairs:
  - ≥ 4.5: text-primary/canvas, text-primary/surface, text-secondary/canvas, text-secondary/surface, accent-on/accent-primary, accent-primary/canvas (as link text)
  - ≥ 3.0: border-strong/canvas, accent-primary/surface (focus ring)
  
  It exits non-zero and lists any failing pair.

- [ ] **Step 1: Write `check_contrast.py`.** Run it → FAIL, because the tokens don't exist yet.
- [ ] **Step 2: Fetch the fonts.**
  - Source: `@fontsource/barlow` and `@fontsource/barlow-semi-condensed` from `https://cdn.jsdelivr.net/npm/`, at an exact version at least two weeks old.
  - Files: `barlow-{latin,latin-ext}-{400,500,600}-normal.woff2` and `barlow-semi-condensed-{latin,latin-ext}-{600,700}-normal.woff2`.
  - **Ask the owner first**, stating the file names, the source and the total size.
  - Save into a new empty folder and copy only the `.woff2` files into `src/fonts/`.
- [ ] **Step 3: Write the token values** from Global Constraints. If light accent `#8c6a24` fails 4.5:1 on `#f4f3f0`, darken it in steps of 4 % lightness until it passes, and record the final value in `ARCHITECTURE.md`.
- [ ] **Step 4: Run.**
  - `python -I website/tests/check_contrast.py` → PASS.
  - Run the build.
  - Run the full e2e suite. Only the screenshot-based expectations may change; value checks must stay identical.
  - Extend `language-and-theme` with these assertions:
    - With the system set to dark and `tf-theme=light` stored, `data-theme` is `light` on `<html>` before `DOMContentLoaded`.
    - `getComputedStyle(document.documentElement).colorScheme` matches the active theme.
    - The `theme-color` metas hold the new values.
- [ ] **Step 5: Commit** with the message `feat(style): Gilded Inlay tokens, self-hosted Barlow, contrast check`.

### Task 4: Registry-Driven Header Menu and Mobile Drawer

**Files:**
- Modify: `website/src/registry.json`
  - Add `"menu"`: an ordered list of groups `{ "id", "label" (i18n key), "items": [ { "page": <page id> } | { "route": <data route hash, e.g. "worlds"> , "label": <i18n key> } | { "href": <absolute URL>, "label": <i18n key> } ] }`.
  - **Application:** downloads, features, guides, about.
  - **Online Tools:** calculators plus every page with a `calc` key, then `{page:"calculators", hash:"today", label:"nav.todayInTibia"}`, which links to the calculators hub's Today panel. Give that panel `id="today"` if it doesn't have one.
  - **Community Data:** routes `character`, `guilds`, `worlds`, `highscores/all/experience/all/1`, `houses`, `creatures`, `spells`, `killstatistics`, `bosses`, `news`, `fansites`.
  - **Fansite Tools:** `{href:"https://github.com/RockyJLaRo/TibiaFoundry", label:"nav.sourceOnGitHub"}`.
  - **Contact:** page `contact`, rendered as a plain link, not a group.
- Modify: `build_site.py`, so it renders `{{NAV}}` (desktop) and `{{MENU_NAV}}` (drawer) from `registry.menu`. `--strict` fails on an unknown page id or route.
- Modify: `src/templates/layout.html`, the header markup (spec §5 header sketch: search row on top, then brand, menu, language, theme and Download).
- Create: `website/src/js/nav.js`
  - Desktop disclosure panels: a `button[aria-expanded]` controlling a `div[hidden]` panel. Escape, an outside click or focus leaving closes the panel. Only one panel is open at a time.
  - Drawer under 1024 px: focus trap, Escape and backdrop close, focus returns to the button, groups are collapsible `<details>` elements, uses `100dvh` and safe-area padding.
  - The language and theme controls are in the drawer.
- Modify: `src/js/site.js`. Remove the old `setMenu` code that moved to `nav.js`. Keep the language menu, the theme toggle and the language offer.
- Modify: `build_site.py:651`, so `nav.js` loads on every page.
- Test: `website/tests/e2e.py` → rewrite `navigation`, add `menu-keyboard` and `menu-overflow`.

**Interfaces:**
- Produces: `registry.menu` (consumed again in Task 7 for the footer columns), and the nav markup classes `.nav-group`, `.nav-panel`, `.drawer`.

- [ ] **Step 1: Write the failing e2e checks.**
  - `navigation`: every `registry.menu` destination is a link in the desktop panel and in the drawer, in all 5 languages, and each one returns 200 or resolves to a data route.
  - `menu-keyboard`:
    - Tab to "Community Data", press Enter, and the panel opens with `aria-expanded=true`.
    - Escape closes it and focus is back on the button.
    - At 390 px the drawer opens, Tab cycles inside it, Escape closes it, and focus returns to `#menu-btn`.
  - `menu-overflow` (Review Focus 1): at 1024, 1280 and 1440 px in de and pl, `scrollWidth <= innerWidth` and the bounding box of each `.nav-group` button is a single line.
  - `no-js-reachability` (Review Focus 2): with `javaScriptEnabled=False`, every `registry.menu` href appears in the footer, and the header search form `action` ends with `tibia/`.
- [ ] **Step 2: Run.** `python -I website/tests/e2e.py --only navigation,menu-keyboard,menu-overflow,no-js-reachability` → FAIL.
- [ ] **Step 3: Implement** the registry menu, the rendering, the markup, `nav.js` and the styles in `components.css`.
  - Active page: gold 2 px underline.
  - Panels: elevated surface with a 1 px inlay top border.
- [ ] **Step 4: Run.** The same checks plus the full e2e suite → PASS. `check_i18n.py` → PASS.
- [ ] **Step 5: Commit** with the message `feat(nav): registry-driven grouped menu and accessible drawer`.

### Task 5: Global Tibia Search

**Files:**
- Modify: `registry.json`, adding `"search"`:

  ```json
  [
    {"type": "character", "route": "character/{q}", "exact": true},
    {"type": "guild", "route": "guild/{q}", "exact": true},
    {"type": "world", "route": "world/{q}", "list": "worlds"},
    {"type": "creature", "route": "creature/{race}", "list": "creatures"},
    {"type": "spell", "route": "spell/{id}", "list": "spells"},
    {"type": "house", "route": "houses"}
  ]
  ```

  Confirm each route pattern against `dataRoutes` before using it.
- Create: `website/src/js/search.js`
  - A type `<select>`, a combobox input with `role=combobox`, `aria-expanded` and `aria-activedescendant`, and a listbox of suggestions.
  - Suggestions only appear for `list` types. They are loaded through `TFData.get(list)` on first focus after that type is chosen, filtered client-side with case- and accent-insensitive prefix-then-substring matching, and capped at 8.
  - Arrow keys, Enter, Escape. Loading and error states use the existing `js.*` strings.
  - Recent searches: `tf-recent-searches`, a JSON array of `{type, q}` holding at most 8 entries, newest first, deduplicated. They show when the input is empty and focused, with a "Clear Recent Searches" button. All reads and writes are wrapped in try/catch.
- Modify: `layout.html`, the header search row. Remove the old `data-character-search` forms.
- Modify: `build_site.py:651`, so `tibiadata.js`, `sanitize.js` and `search.js` load on every page, with `formulas.js` before `tibiadata.js` (keep Task 1's order).
- Modify: `src/templates/legal.html`, so the storage list includes `tf-recent-searches` with a one-line purpose, in 5 languages.
- Test: `e2e.py` → new `global-search` check.

**Interfaces:**
- Consumes: `TFData.get(name)` and `TF_SITE.paths.tibiaHash` (existing).
- Produces: `search.js` exposes `window.TFSearch.routeFor(type, item) -> string` (a hash route), which is also used by Task 6's community shortcuts.

- [ ] **Step 1: Write the failing `global-search` check.** It uses fixtures: `tests/fixtures/tibiadata` already includes worlds, creatures and spells. It checks:
  - Character `Roc ky` + Enter lands on `#/character/Roc%20ky`.
  - Review Focus 5: `Mc'Lovin` and `Zé Ninja` round-trip through the URL and back into the page's name field.
  - World type: typing `ant` lists only worlds that contain "ant". ArrowDown + Enter opens `#/world/<Name>`.
  - Creature `dem` shows Demon and links by race. Spell `exura` shows suggestions.
  - House opens the houses route.
  - After 9 searches only 8 recents remain, newest first. "Clear Recent Searches" empties them.
  - Review Focus 3: with `localStorage` throwing (a page init script overriding `Storage.prototype.setItem` and `getItem` to throw), a search still navigates and no console errors appear.
- [ ] **Step 2: Run.** `python -I website/tests/e2e.py --only global-search` → FAIL.
- [ ] **Step 3: Implement** `search.js`, the markup, the styles, the locale keys (5 languages) and the Legal entry.
- [ ] **Step 4: Run.** `global-search` + the full e2e suite + `check_i18n.py` → PASS.
- [ ] **Step 5: Commit** with the message `feat(search): global Tibia search with type picker and recent searches`.

### Task 6: App-First Homepage

**Files:**
- Rewrite: `website/src/templates/home.html`, with sections in spec §5 order: hero, today strip, three paths, features and showcase, calculators and community shortcuts, news, "For Fansite Builders", second download section.
- Modify: `website/src/css/pages/home.css`
- Modify: `website/src/js/data-app.js`
  - The home today strip reuses `renderBoosted`, but renders into four `.today-cell` elements.
  - A failed or empty boosted cell gets `hidden`. It never shows placeholder text.
  - Rashid and server save come from `today.js`. Add `today.js` to the home page script list.
- Modify: the 5 locale files under `home.*`.
  - Hero H1: `"Everything You Need to Create for Tibia."`
  - Paths: `"Get the Desktop App"`, `"Use Online Tools"`, `"Build Your Fansite"`
  - Teaser heading: `"For Fansite Builders"`
- Showcase screenshots, per language from `images/screens/<lang>/`: `asset-library`, `extract-sprites`, `map-tools`, `content-studio`, in the `-dark` or `-light` variant chosen by a `<picture>` with `prefers-color-scheme`, and toggled with `data-theme`. Give explicit `width` and `height`, and `loading="lazy"` on everything except the hero.
- The hero download buttons reuse the existing downloads data: Installer is primary, then Portable, then Source, plus version, date and the checksums link. No new download URLs.
- Test: `e2e.py` → update the `news` and `pages-and-attribution` home expectations, and add a `homepage` check.

**Interfaces:**
- Consumes: `TFSearch.routeFor` (Task 5) for the community shortcuts, and `registry.menu` (Task 4) for the calculator list.

- [ ] **Step 1: Write the failing `homepage` check.**
  - The H1 text in en is the exact hero title.
  - The first `<section>` contains the Installer download link, whose href matches the Downloads page installer href.
  - The today strip has 4 cells. Rashid matches `TFFormulas.rashid({now})`.
  - Review Focus 4: with TibiaData routes aborted, both boosted cells are `hidden`, Rashid and server save are visible, and no "undefined" or "null" text appears anywhere in `main`.
  - The "Build Your Fansite" link equals `https://github.com/RockyJLaRo/TibiaFoundry`.
  - In both themes, the visible showcase images are the matching `-dark` or `-light` variants.
  - At 320 px there is no overflow.
- [ ] **Step 2: Run.** `--only homepage` → FAIL.
- [ ] **Step 3: Implement** the homepage, its styles and the 5 locales.
- [ ] **Step 4: Run.** `homepage`, `news` and the full e2e suite + `check_i18n.py` + build → PASS.
- [ ] **Step 5: Commit** with the message `feat(home): app-first homepage with live today strip`.

### Task 7: Footer, Consolidated Credits and Contact

**Files:**
- Modify: `layout.html` footer.
  - Columns are built from `registry.menu`: Application, Online Tools, Community Data, Contact. Every menu destination must appear.
  - The creator line and profiles stay (`{{FOOTER_PROFILES}}`).
  - Remove the `<section class="credits" data-credits>` block.
  - Add `<p class="footer-notice" data-notice>`. It carries:
    - CipSoft © with "Tibia is a trademark of CipSoft GmbH"
    - "Data: TibiaData" (linked, with the thanks wording `credits.tibiadata`)
    - the independent-project sentence `credits.independent`
    - a link to the Legal page's `#attribution`
  - The legal links, the © line and the version line stay.
- Modify: `legal.html`, adding `<section id="attribution">` with `{{CREDITS}}` (the full `credits_html` output) under the heading `credits.title`. That key's English value changes to `"Data & Resource Credits"`, and the About page no longer prints `{{CREDITS}}`.
- Modify: `build_site.py`. Fill `{{CREDITS}}` only in `legal.html`. `--strict` fails if any other page renders `data-credits`.
- Modify: `contact.html`.
  - A two-column card: the left column holds the email, the Write button and the profiles, exactly as today. The right column holds "Contact Reasons", a list of 6 `mailto:` links. Each link's `subject` is `"Tibia Foundry: <reason>"` in the page language: General Inquiry, Bug Report, Calculator Formula Correction, Fansite Integration, Application Support, Developer Question.
  - Bodies are empty, and the subjects are URL-encoded.
- Test: `e2e.py`.
  - `pages-and-attribution` changes. Every page has a footer `[data-notice]` with a working TibiaData link, a CipSoft mention and a link to `legal/#attribution`. No page except Legal contains `[data-credits]`. Legal's `#attribution` contains all `providers.json` entries.
  - `contact-and-downloads` gains a check that the 6 reason links are `mailto:` the existing contact email, with the localized subject.
  - `creator-links` stays unchanged and must still pass.

- [ ] **Step 1: Update the e2e checks** as described. Run them → FAIL.
- [ ] **Step 2: Implement** the footer, the Legal attribution section, the build rule, the Contact page and the 5 locales.
- [ ] **Step 3: Run.** The full e2e suite + `check_i18n.py` + build → PASS.
- [ ] **Step 4: Commit** with the message `feat(footer): compact notices, credits on Legal, contact reasons`.

### Task 8: English Title Case and Full Re-Skin Pass

**Files:**
- Modify: `website/tests/check_i18n.py`
  - Add `--title-case` (on by default) for `en.json` keys matching `nav.*`, `*.title`, `*.heading`, `*.button`, `calc.*.title` and `home.paths.*`. The rule is the one in Global Constraints.
  - Words that are entirely uppercase or contain digits are allowed as written, e.g. "GIFs", "9.0.5".
  - An allowlist in the script covers brand or literal strings, e.g. "Tibia Foundry", "TibiaData", "Tibia.com".
- Modify: `en.json`, fixing the reported strings.
- Modify: the `src/css/pages/*.css` and `components.css` rules that still look like the old Ivory & Sapphire: cards, tables, inputs, buttons, calculator workbench, data views, downloads, guides, about.
  - Gold appears only as the inlay, focus, active and primary-button colour.
  - Cards get a 2 px left gold edge only where they are links.
- Modify: `website/tests/sitekit.py`, changing `SCREENSHOT_VIEWPORTS` to `{"narrow-mobile": (320, 812), "mobile": (375, 812), "large-mobile": (430, 932), "tablet": (768, 1024), "laptop": (1280, 800), "desktop": (1440, 900), "wide": (1920, 1080)}`.
- Modify: `website/tests/site_screenshots.py`, so the manifest records page, language, theme, viewport and status (ok / failed).

- [ ] **Step 1: Run.** `python -I website/tests/check_i18n.py` → FAIL, listing the strings that aren't in Title Case.
- [ ] **Step 2: Fix the English strings.** Run again → PASS.
- [ ] **Step 3: Re-skin.** Then run `python -I website/tests/site_screenshots.py` for the full matrix (every page × 5 languages × 2 themes × 7 viewports).
- [ ] **Step 4: Inspect the images** (open them, don't just read the script output). Fix clipped text, overflow, misalignment, wrong-theme fragments, contrast problems and long-translation breaks. Repeat until clean.
- [ ] **Step 5: Run.** Full e2e suite, unit tests, `check_contrast.py`, `check_i18n.py` and the build → all PASS.
- [ ] **Step 6: Commit** with the message `feat(style): Gilded Inlay across all pages, Title Case, 7-viewport screenshots`.

### Task 9: Release Check and Preview

- [ ] **Step 1: Run everything** on a clean build:
  - the build
  - `node --test website/tests/unit`
  - `check_i18n.py`
  - `check_contrast.py`
  - full `e2e.py`
  - the full screenshot matrix

  Record the counts in `docs/superpowers/plans/2026-11-19-site-shell-gilded-inlay.md` under a "Results" heading. Report failures as they are, never claim a pass that didn't run.
- [ ] **Step 2: Start the local preview** with the `site-preview` launch configuration on port 8765. Spot-check home, a calculator, a data route, Contact and Legal in both themes at 375 and 1440 px.
- [ ] **Step 3: Send the owner** a representative screenshot set: home, menu open, drawer open, search with suggestions, Contact and footer, at 375 and 1440 px, in en/de and both themes. Ask for approval before any commit to Pages or deploy.
