# Architecture

This file records decisions about how the website and the tools are built. The program's own architecture is in
`05_Architecture.md`.

## Styling

### Decision

The site uses **modern plain CSS with design tokens**, split into **cascade layers**. There is no CSS framework and no
extra toolchain. `website/build_site.py` concatenates and minifies the files of `website/src/css/` into one stylesheet,
published as `assets/site.css` with a content hash in its URL.

Why this fits:

- The site is built by Python only. Plain CSS adds no Node or binary build step.
- Theming already runs on tokens (custom properties), so light and dark are a change of values, not of rules.
- The CSS that ships stays readable. Fansite owners can restyle the downloadable tools (sub-project 3) by editing a few
  variables.

### Layer order

`build_site.py` writes `@layer tokens, base, components, pages, utilities;` first and wraps each file in its layer. A
later layer wins over an earlier one whatever the selector specificity, so a page rule never needs a longer selector to
beat a component.

| Layer | Files in `website/src/css/` | Holds |
|---|---|---|
| `tokens` | `tokens.css` | Custom properties: colours (light, dark, forced themes), radii, fonts, spacing |
| `base` | `base.css` | Element defaults, headings, links, focus ring, `.wrap`, `.muted`, `.lead` |
| `components` | `components.css` | Buttons and fields, header, breadcrumbs, page layout, cards, framed panels, footer, icons |
| `pages` | `pages/*.css` (sorted by name) | Rules that belong to one page or one family of pages: home, data, calculators, content |
| `utilities` | `utilities.css` | `.sr-only`, `.pre`, reduced motion, print |

Things to know:

- Unlayered CSS beats all layered CSS. The site has none, so keep it that way.
- `!important` inverts the order: an important rule in an earlier layer beats an important rule in a later one.
- The bundle lists its files in `CSS_LAYERS` and `CSS_FILES` in `build_site.py`. Files of `pages/` are picked up
  automatically. Any other new file must be listed there, in the position where it should sit in its layer.
- `build_site.py --strict` fails when a file under `src/css/` is not in the bundle, or when the old
  `src/styles.css` exists again.
- The minifier removes comments and collapses whitespace. Strings and `url()` stay as written.

### Fonts

The site uses **Barlow** (body, 400/500/600) and **Barlow Semi Condensed** (headings, 600/700), self-hosted. The WOFF2 files
are the latin and latin-ext subsets of `@fontsource/barlow` and `@fontsource/barlow-semi-condensed` 5.3.0 (SIL OFL 1.1,
notice in `website/THIRD-PARTY-NOTICES.md`), kept in `website/src/fonts/`. `css/fonts.css` (first file of the `base` layer)
declares them with `unicode-range` and `font-display: swap`; `build_site.py` copies them to `assets/fonts/`, where the
relative `url()`s of `assets/site.css` find them, and fails when fonts.css names a missing file (`--strict` also when a file
is unused). Pages make no request to Google Fonts or any other CDN. `--font-body` and `--font-display` in `tokens.css` keep
system fonts as the fallback.

### How to add a token

1. Add the custom property to `:root` in `tokens.css` (light values).
2. Add the dark value to both dark blocks of `tokens.css`: the `prefers-color-scheme: dark` block (without a forced light
   theme) and the `:root[data-theme="dark"]` block.
3. Use it as `var(--name)` in `base`, `components`, `pages` or `utilities`. Do not write a literal colour or size where a
   token exists.
4. Run `python -I website/build_site.py --strict` and check both themes.

### Gilded Inlay tokens

Colours are semantic tokens in `tokens.css`: `--bg-canvas`, `--bg-surface`, `--bg-elevated`, `--bg-input`, `--border-subtle`,
`--border-strong`, `--text-primary`, `--text-secondary`, `--text-muted`, `--accent-primary`, `--accent-hover`,
`--accent-subtle`, `--accent-on`, `--inlay`, `--status-*`, `--radius-small|medium|large`, `--space-1` to `--space-8`,
`--font-body`, `--font-display`. `website/tests/check_contrast.py` reads both palettes and asserts WCAG 2.2 AA pairs
(text 4.5:1, borders and focus ring 3:1); run it after any colour change.

- Light accent: `#8c6a24` passes on the canvas `#f4f3f0` at 4.50:1, so no 4 % lightness step was needed. It is 5.00:1 on
  white surfaces. On `--bg-elevated` it is only 4.12:1, so use body text colours, not the accent, for small text there.
- Gold is never the colour of text. It marks: 1 px inlay lines (`--inlay`: header and footer edges, section dividers, the
  top of featured panels such as `.framed` and the contact card), link underlines (links are body-coloured text with a
  1 px gold underline, 2 px on hover), the focus ring, active states (current menu item, data tab, screenshot tab, chosen
  language: a 2 px gold underline or left bar), the primary button, and the 2 px left edge of cards that are a link as a
  whole (`.tool-card`, `.dest-card`, `a.boost-card`, `a.mini-card`, `.profile`, `.reason`, `.path-card`, `.tool-link`;
  each sets it in its own rule). Cards that only contain links have no gold edge. Icons use `--text-secondary`.
- Surfaces are flat: cards, tables and panels have 1 px borders and no shadow; `--shadow` is for floating layers only
  (menu panels, search popup, drawer, language menu). Radii stay within `--radius-small|medium|large` (2-6 px).
- `--bg-table-head` (table heads) and `--skeleton-shine` (the moving band of loading placeholders) are their own tokens;
  `check_contrast.py` covers every text/background pair the pages use, including table heads, fields, zebra rows,
  error notices and badges.
- `--border-strong` is `#8c8576` (light) and `#646d76` (dark), a little darker/lighter than the first design values
  `#9a9384` and `#4a525b`, which gave only 2.75:1 and 2.33:1 against the canvas.
- `--font-body` and `--font-display` name Barlow first and fall back to system fonts until the font files are bundled.

### Why Tailwind was rejected

- It adds a Node or binary (v4 standalone CLI) build step to every site build.
- `data-app.js` builds about 1,000 lines of markup at run time with class strings, so those classes would need safelists.
- Most importantly, downloadable tool packages would carry either a utility blob or a Tailwind dependency. That works
  against "easy to customise without our toolchain".

CSS Modules or a component framework were rejected too: they need a bundler and a framework migration for 13 templates,
with no benefit to players.

## Menu

- `registry.json` `menu` is the only list of menu destinations. `build_site.py` renders it three times: the desktop
  disclosure panels (`{{NAV}}`), the phone drawer (`{{MENU_NAV}}`) and the footer link columns (`{{FOOTER_MENU}}`,
  `{{FOOTER_MENU_LINKS}}`). So every destination is a plain footer link that works without JavaScript.
- Entries are groups (`id`, `label`, `items`) or a plain top-level link: Contact (`{page}`) and, until the fansite tool
  library ships, Fansite Tools as `{href, label}` ("Source on GitHub", opening in a new tab like every external link). Items are `{page[, hash][, label]}`,
  `{route, label[, icon]}` or `{href, label[, icon]}`. A route that stops before a pattern's `:world` or `:town` gets the
  default world and town. `--strict` fails on an unknown page or data route, and when a calculator page is missing.
- Every link carries `data-menu-dest` (`page:<id>[#hash]`, `route:<route>`, `href:<url>`); the tests use it.
- `src/js/nav.js`: desktop panels are buttons with `aria-expanded` (not an ARIA menu). Below 1024 px the menu button
  opens a modal drawer with a focus trap. When the desktop header does not fit on one line (long labels, for example German, Polish,
  Spanish or Portuguese at 1024 px), `html.nav-collapsed` switches it to the drawer. An inline script right after the header sets
  that class before the first paint, and `nav.js` keeps it current on resize.

## Global search

- `registry.json` `search` lists the header search's types in order (character, guild, world, creature, spell, house).
  Each `route` is a data route with `{placeholders}`; `exact` types need the exact name, `list` types suggest from a
  TibiaData list, `browse` is the list page opened when nothing specific was picked. `--strict` checks every route.
- `src/js/search.js` (every page, after `tibiadata.js`): an ARIA combobox with a listbox popup. Lists load on the first
  focus after a type is chosen; matching ignores case and accents, prefix matches first, at most 8.
  `window.TFSearch.routeFor(type, item)` returns the hash route for other pages to reuse.
- Recent searches live in `localStorage` `tf-recent-searches` (at most 8, newest first), listed on the Legal page.
  Every storage access is guarded; when storage is blocked they last for the page only.
- Without JavaScript the form is a plain `GET tibia/?character=` search; the type picker is hidden and has no name.
