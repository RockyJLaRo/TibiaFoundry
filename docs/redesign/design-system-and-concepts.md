# Design system and concepts – concept A + C, refined in stage 2 ("Ivory & Sapphire")

## The three concepts

Rendered previews, not descriptions: `docs/redesign/concept-previews/index.html?c=a|b|c&t=light|dark` (isolated; the
website build never reads this folder). Screenshots: `docs/redesign/concept-previews/screenshots/concept-{a,b,c}-{light,dark}-{desktop,phone}.jpg`
(12 images, regenerate with `python -I docs/redesign/concept-previews/shoot_concepts.py`). Every concept shows the same real
content: header, logo, navigation, hero with character search, "Today in Tibia" panel, six destinations, a highscores tool
card with a table, the desktop-app block with the download notice, buttons (primary, secondary, quiet, disabled), news
cards and the footer.

| | A – Medieval parchment | B – Dark fantasy / dungeon | C – Modern Tibia Foundry |
|---|---|---|---|
| Surfaces | Warm parchment page and cards | Charcoal stone page with brick texture | Neutral grey page, white cards |
| Frame | Brick header/footer, pixel battlement edge, gold corner pieces | Bricks everywhere, carved shadows | Plain header, hairline borders |
| Type | Palatino for headings **and** body | Palatino headings, sans body | Sans throughout |
| Accent | Bronze buttons, gold ornaments | Gold buttons and headings | Blue |
| Verdict | Strongest identity; serif body hurts data tables; full-contrast bricks too busy | Atmospheric; too dark in light mode; texture behind text lowers readability | Clearest; generic – could be any site |

**Selected: A + C hybrid** – C's layout, spacing, sans body text and components; A's parchment panels, stone
header/footer with the pixel battlement, bronze/gold accents and pixel icons; B's gold headings only in the dark theme.
Research support: TibiaDaily shows parchment works when text stays plain and large; TibiaLight/Rookie show texture
behind text fails; TibiaMisterios/TibiaDraptor show icon tiles scale (see `official-fansite-research.md`).

## Tokens (`website/src/styles.css`, `:root`)

| Token | Light | Dark | Use |
|---|---|---|---|
| `--bg` | `#ebe5d6` light stone | `#1c1915` | Page |
| `--surface` / `-2` / `-3` | `#fbf7ec` / `#f3ecdb` / `#e8dec6` parchment | `#26221c` / `#2d2821` / `#363027` | Cards, bands, table heads |
| `--border` / `--border-strong` | `#d6c8a8` / `#8c7650` | `#463e31` / `#8a7a5e` | Lines / inputs and buttons (≥ 3:1) |
| `--text` / `--text-2` | `#2a2117` / `#5b4c39` | `#efe6d0` / `#c0b294` | Ink |
| `--link` | `#7a3b0b` | `#ebb75f` | Links (always underlined) |
| `--accent` / `--accent-text` | `#8a4813` / `#fffaf0` bronze | `#d79d47` / `#1d150b` | Primary buttons |
| `--gold` / `--gold-bright` | `#7c5409` / `#c8962f` | `#e3b65c` / `#d9a548` | Gold text / ornaments (decorative) |
| `--focus` | `#1f63a3` (logo blue) | `#8cc4ef` | 3 px focus ring |
| `--stone` / `--stone-text` / `--stone-link` | `#48423a` / `#f4ead2` / `#f6d38b` | `#14120f` / `#efe6d0` / `#ebb75f` | Header, footer |
| `--mortar` | `rgb(0 0 0 / .16)` | `rgb(255 240 210 / .05)` | Brick lines (mask) |
| `--px-*` | 15 colours | ink darker | Pixel icon palette |
| `--radius` / `--radius-card` | 6 px / 8 px | | Controls / cards |
| `--shadow` | warm, low | deep, low | Cards |
| `--gutter` | 16 / 24 / 32 px at 0 / 640 / 1024 px | | Side padding |
| `--header-h` | 60 px, 64 px from 1024 px | | Sticky header |
| `--edge` | 16 px | | Battlement height |

**Contrast (WCAG 2.1, computed from the tokens):** every text pair ≥ 4.5:1 in both themes (lowest: light `--gold` on `--bg`
5.34, dark `--text-2` on `--surface-2` 6.98); input/button borders ≥ 3:1 (light 3.47–4.07, dark 3.78–4.19); focus ring
≥ 4.96:1. `--gold-bright` (2.5:1 on light parchment) is used only for decorative corners, rules and gems, never for text.

**Typography:** body `Segoe UI Variable Text / Segoe UI / system-ui / Roboto …` 16 px, line-height 1.6; headings
`Palatino Linotype / Palatino / Book Antiqua / URW Palladio L / P052 / Noto Serif / Georgia` 700, h1 clamp(1.85–3 rem),
h2 clamp(1.4–1.95 rem); monospace `Cascadia Mono / Consolas`. **No web fonts are downloaded** (no font requests, no layout
shift from font swapping); every listed family covers Portuguese, Polish, Spanish and German diacritics (ã õ ç ą ć ę ł ń ó ś ź ż ñ ä ö ü ß).
Tabular numerals in data tables.

**Spacing:** 4-px base; components use 8/10/12/14/16/18/20/24 px; bands 36/48/60 px vertical at phone/tablet/desktop.

## Components

Header (stone, bricks, brand + version chip, 4 task links, Download button, character search ≥ 1280 px, language menu,
theme toggle; phone: search icon + menu with pixel icons, search field, languages, theme) · breadcrumbs (all inner pages;
the Tibia data route adds the current view) · hero · framed panel (gold pixel corners) · destination cards (pixel icon tile,
title, one-line purpose) · tool cards and grouped tool hub · cards · download cards (file, size, SHA-256) · release-facts
list · download notice (shield icon, parchment, gold left rule) · steps · FAQ details · news items (category colour rule) ·
data tables (sticky head, zebra rows, stacked records < 640 px) · facts lists · chips · badges · notices (error, empty) ·
skeleton loading · buttons (primary, secondary, small, quiet, disabled, copied) · inputs and selects · legal section with
pill navigation · status list · statistics toggle · spoiler warning · footer (battlement edge, brand block, profile icon
row, four link groups, credits, legal links, dynamic year) · lightbox (screenshots).

States: hover (border/background), active (1 px press), `:focus-visible` 3 px ring, disabled 50 %, loading skeleton
(no animation with reduced motion), empty and error notices (from the existing data app), `aria-current` for navigation.

## Original pixel art (`website/art/pixelart.py` → `website/src/images/pixel/`)

All drawn for Tibia Foundry as text grids (no CipSoft graphics traced or copied); crisp `shape-rendering="crispEdges"`.

| Asset | Size | Use |
|---|---|---|
| `icons.svg` – 19 symbols: character, world, guild, highscores, houses, killstatistics, creatures, spells, bosses, fansites, news, download, app (anvil), guides, gem, quill, letter, shield, search | 16×16 each, 18 KB total, one cached file | Destination cards, tool hub, phone menu, buttons, legal headings; colours from `--px-*` tokens (themeable) |
| `battlement.svg` | 24×8 mask, drawn at 48×16 | Top edge of the footer |
| `bricks.svg` | 32×16 mask, drawn at 64×32 | Mortar lines on header and footer |
| `corner.svg` | 10×10 mask, drawn at 20×20 | Gold corners of framed panels |
| `gem.svg` | 8×8 mask, drawn at 16×16 | Centre of the ornament rule |

Masks take their colour from tokens, so one file serves both themes. Scale only by whole multiples.

## Responsive rules

Mobile first. Breakpoints: 640 px (2-column grids, 24 px gutter), 720 px (data split views), 1024 px (header navigation,
3-column grids, hero two columns, 32 px gutter), 1280 px (header search). Tables become stacked records below 640 px. Touch
targets ≥ 44 px for primary controls (36 px for compact secondary buttons). Verified with zero page-level horizontal overflow
at 320, 360, 390, 430, 768, 1024, 1280, 1440 and 1920 px (e2e `responsive`).

## Theme rules

The stored choice (`tf-theme`) is applied before first paint; otherwise the system preference. Dark theme: deep stone,
parchment-coloured text, gold headings (concept B detail), lighter bronze buttons with dark text, darker pixel outlines.
Screenshots of the program show the variant of the active theme.


---

## Stage 2 (18 November 2026): palette, icons and tables

### Palette comparison
Four palettes were rendered on the same real components (header, hero, search, buttons, status chips, task cards, a
sortable table, the download notice, footer) in light and dark: `stage2/palette-lab/index.html?p=p0|p1|p2|p3&t=light|dark`,
screenshots in `stage2/palette-lab/screenshots/` (`sheet.jpg` compares all eight).

| | Light | Dark | Verdict |
|---|---|---|---|
| p0 stage 1 "parchment & bronze" | brown page, bronze buttons | brown-black, gold headings | too brown/orange, gold everywhere |
| **p1 Ivory & Sapphire** | warm ivory, slate chrome, sapphire actions (the logo's blue), gold accent | deep slate with three surface levels, soft blue actions | **selected**: bright and warm without the brown cast; one action colour; gold kept for accents; dark theme is its own palette |
| p2 refined parchment | lighter than p0, bronze actions | warm charcoal | still brown |
| p3 stone & verdigris | cool stone, teal actions | green-slate | attractive but unrelated to the logo's colours |

### Tokens (website/src/styles.css)

| Token | Light | Dark | Role |
|---|---|---|---|
| `--bg` | `#f5f2eb` | `#14171c` | page |
| `--surface` / `--surface-2` / `--surface-3` | `#fffdf8` / `#f0ebe1` / `#e9e2d5` | `#1c2027` / `#232832` / `#2a303b` | cards; bands; table heads |
| `--row` | `#faf7f1` | `#20252e` | zebra rows |
| `--border` / `--border-strong` | `#e1d9ca` / `#8d8170` | `#323946` / `#6b7686` | lines / inputs and buttons |
| `--text` / `--text-2` | `#24221e` / `#5a554c` | `#ebe7df` / `#a8b0bc` | text |
| `--link` | `#1d5a8e` | `#86bdf0` | links |
| `--accent` / `--accent-hover` / `--accent-text` / `--accent-soft` | `#1f5b8f` / `#184a75` / `#ffffff` / `#e6eef7` | `#5f9fdb` / `#78b0e4` / `#0b1520` / `#1a2a3a` | primary actions (sapphire), selected states, row hover |
| `--gold` / `--gold-bright` / `--gold-soft` | `#7d5a1c` / `#b6893a` / `#f5ead3` | `#d9b56b` / `#d3ad62` / `#2a2416` | gold text and icons / ornaments, header rule / icon tiles, notices |
| `--info` (+`-soft`) | `#1f5b8f` | `#86bdf0` | information |
| `--success` | `#2b7a3f` / `#e3f2e6` | `#7dcf93` / `#19301f` | success |
| `--warning` | `#8a5a00` / `#fbf0d6` | `#e2bb63` / `#302714` | warning |
| `--danger` | `#b42f36` / `#fae4e3` | `#f2878c` / `#3a1f22` | errors (the logo's red) |
| `--focus` | `#1f5b8f` (gold `--stone-link` on the chrome) | `#86bdf0` | focus ring |
| `--stone` / `--stone-2` / `--stone-line` | `#262b33` / `#1d2128` / `#3a414c` | `#0f1216` / `#0b0d10` / `#262c35` | header, footer (slate) |
| `--stone-text` / `--stone-text-2` / `--stone-link` | `#f2eee6` / `#b9bfc9` / `#e7c47c` | `#ebe7df` / `#9aa3af` / `#e2bf72` | text and links on the chrome |
| `--radius` / `--radius-card` | 8 px / 12 px | | controls / cards |

**Contrast:** 35 text/UI pairs × 2 themes = 70 checks, all ≥ WCAG 2.1 AA (text ≥ 4.5:1, lowest 5.25:1 gold on gold-soft;
borders and focus ≥ 3:1, lowest 3.41:1). Dark-theme headings are off-white (gold only as an accent).

### Icon system
**Lucide 0.460.0** (ISC) was chosen over hand-drawn SVG, Unicode glyphs and the stage-1 pixel icons: one consistent
24-px grid, 2-px round strokes, clean geometry, readable at 14–24 px, open licence, no runtime dependency (49 icons are
copied into `website/art/lucide/` and inlined by `website/art/icons.py`). Sizes: 14 px (sort), 18 px (buttons), 20 px
(menus, headings), 22 px (icon tiles, header buttons). Colour = `currentColor`; tiles use `--gold` on `--gold-soft`.
Every icon has one meaning (list in `art/icons.py`); icon-only controls keep their accessible names.

| Was (stage 1) | Now |
|---|---|
| hand-drawn menu / close / search / globe / half-circle theme icons | Lucide `menu`, `x`, `search`, `languages`, `moon`/`sun` (shows the mode you switch to) |
| pixel icons (character, world, trophy, slime, house, flag, chest, anvil, book, quill, letter, shield, gem, scroll …) | `user`, `globe`, `trophy`, `paw-print`, `house`, `flag`, `download`/`package`/`file-archive`/`code-xml`, `monitor`, `book-open`, `user-round`, `mail`, `shield-check`, `coins`, `newspaper`, `users`, `skull`, `crown`, `wand-sparkles`, `archive`, `scroll-text`, `anvil` (home) |
| "▲ / ▼" text glyphs in table heads | `arrow-up-down` (sortable), `arrow-up`, `arrow-down` |
| "✓" and "›" text glyphs | CSS check marks and chevrons |
| no icon for external links | `external-link` mark after every new-tab text link (CSS mask), icon inside external buttons |
| plain copy / pager / refresh buttons | `copy`, `arrow-left`/`arrow-right`, `refresh-cw`, `mail`, `download` |

The pixel art stays where it adds character without competing with text: the footer battlement, the faint bricks on the
footer, the gold corners of featured panels and the gem in the section ornament. The 19 stage-1 pixel icons are kept as an
unpublished archive (`website/art/pixel-icons.svg`).

### Tables
One shared component (`table()` in `src/js/data-app.js`) powers all 13 table instances. Sorting is type-aware: numbers
(also "1,234 gp", "45 %"), ISO dates, version numbers ("1.9" < "1.10"), and text with the page language's collation
(natural numbers: "Flat 2" < "Flat 10"); empty values always last; ties keep the original order; the data are never
modified. Headers show a neutral, ascending or descending icon, set `aria-sort`, and a sort is announced ("Sorted by
Level, descending") in five languages. Numbers and dates start descending, text and ranks ascending; a second click
reverses. Phones (where rows become cards) get a "Sort by" select and a direction button. Tables with more than 25 rows
scroll inside their box with a sticky header; wide tables show scroll shadows at the edges; rows have zebra striping and
a hover tint; empty results show "No matches".
