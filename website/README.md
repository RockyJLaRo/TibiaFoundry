# tibiafoundry.com – website (9.0.4)

A redesigned, single-page product site for Tibia Foundry in **five languages** (English, Português (Brasil), Polski, Español, Deutsch), built for **GitHub Pages** (static hosting, no backend, no build step on the server). Translation guide: [TRANSLATING.md](TRANSLATING.md); completeness report: [i18n-report.md](i18n-report.md).

## Structure

| Path | What it is |
|---|---|
| `site/` | **Deploy this folder.** `index.html` (English) and `pt-br/`, `pl/`, `es/`, `de/` (CSS and JavaScript inline), `en/` (opens `/`), `img/` (logo and pixel-art samples, shared) with `img/screens/<id>-<code>-<theme>.webp` (every language's own screenshots in both themes), favicons, `og-image*.png` (one link preview per language), `CNAME`, `robots.txt`, `sitemap.xml` |
| `src/index.template.html` | Markup only; every visible text is a key (`{{t:key}}`, `{{a:key}}`, `{{j:key}}`), plus `{{IMG:name}}` and the build's own placeholders |
| `src/locales/*.json` | The texts: `en.json` (source) and one file per language |
| `src/styles.css` | Design: colour tokens for light and dark themes, layout, components, responsive rules, reduced motion |
| `src/app.js` | Language selector (remembered; keeps the section, screenshot tab and open questions when switching), offer of the browser's language, `TF.t()` for run-time texts, theme toggle, menu, screenshot tabs, lightbox, reveal-on-scroll, analytics click events |
| `src/images/` | Shared images (logo, real Content Studio output: creature, item and outfit sheets) and `screens/<code>/<id>-<code>-<theme>.png`, the screenshot library: real 9.0.4 window captures of the program in each language and theme |
| `screenshots/` | The screenshot generator (`screenshots.py`), its configuration, `manifest.json`, the generated `inventory.md` and `coverage-report.md`, and its documentation ([screenshots/README.md](screenshots/README.md)) |
| `build_site.py` | Builds `site/` from `src/` in every language and writes `i18n-report.md` |
| `legacy/` | The 8.5–8.7 page, its build script and notes (not deployed) |

## Preview locally

```bash
python -m http.server 8766 --bind 127.0.0.1 --directory website/site
```

Open http://127.0.0.1:8766/ (English) or http://127.0.0.1:8766/de/ and so on.

## Screenshots

Every screenshot is captured from the running program, in every website language and in both themes, by one command
(it also builds and validates the site):

```bash
python -I website/screenshots/screenshots.py capture                # everything
python -I website/screenshots/screenshots.py capture --incremental  # only missing or outdated screenshots
python -I website/screenshots/screenshots.py status                 # what is missing or outdated, and why
```

The program does the capturing itself (`TibiaFoundry.exe --site-screenshots <folder> --language <code>
[--site-pages a,b] [--site-themes dark,light]`) on the portable demo copy
(`C:\Users\Public\Documents\Tibia Foundry Demo\App`, so no personal path appears; paths in the user's folder are
blurred automatically). Languages, locations, checks, comparison, adding a page or a language: see
[screenshots/README.md](screenshots/README.md); the current inventory and coverage are in
[screenshots/inventory.md](screenshots/inventory.md) and [screenshots/coverage-report.md](screenshots/coverage-report.md).
Page and tool names on the site are the program's own translations (`{app:Key}` in the locale texts), so the words
next to a screenshot are the words in it.

## Build

```bash
pip install pillow numpy
python -I website/build_site.py            # add --strict to fail on a missing translation
```

The version comes from `src/Directory.Build.props`. Screenshots are written to `site/img/` as WebP (quality 80); the pixel-art samples and the logo losslessly, so they stay pixel-exact. Each language shows the program in its own language and only its own screenshots (`img/screens/<id>-<code>-<theme>.webp`); a missing screenshot stops the build instead of showing another language's. The logo and the pixel-art samples are shared. The script refuses to write a page with unreplaced placeholders, more than one Google tag, any resource loaded from another host, or a translation that changes markup, code or placeholders.

## Deploy to GitHub Pages

1. Copy the **whole contents** of `site/` (including the folders `img/`, `en/`, `pt-br/`, `pl/`, `es/` and `de/`) to the root of the Pages repository (`RockyJLaRo/TibiaFoundry`, branch `main`).
2. After deploying, submit `https://tibiafoundry.com/sitemap.xml` in Google Search Console: it lists all five languages with hreflang alternates.
3. Commit and push; Pages publishes within a minute or two.

**Domain:** `CNAME` contains `tibiafoundry.com` – keep it, and keep the custom-domain setting and DNS of the repository as they are.

**Analytics:** the Google tag `G-J3EX3FB3PB` is unchanged (one tag in `<head>` of every language), plus one event per download, releases, repository and issues link (`data-track`, with `app_version` 8.8.0 and `page_language`). No other tracking, cookies or external requests. The language choice is stored in `localStorage` (`tf-lang`), like the theme.

**Downloads:** every download button points to `https://github.com/RockyJLaRo/TibiaFoundry/releases/latest`. Upload the 8.8.0 release files (installer, portable ZIP, source ZIP) there before or right after deploying, so the links serve 8.8.0.

## Sections

Hero (what it is, for whom, download and GitHub) · Who it is for · Features (nine capabilities with their tools) · Asset Library · Content Studio (steps and real generated output) · Publishing profiles · Screenshots (tabs, lightbox) · Safety · What's new in 8.8.0 · Download (installer, portable, source; requirements; first steps) · FAQ · Support band · Footer (links, disclaimer, open-source components).

Only features that exist in 8.8.0 are described. The screenshots are real; local paths that contain the Windows user name are blurred.

## Tested (2026-10-31, built-in browser over `python -m http.server`)

| Check | Result |
|---|---|
| 1280 px, 768 px, 375 px | no horizontal scrolling; menu button below 1080 px; cards and spotlights stack |
| Dark and light (system preference) | every surface follows the theme; hero and spotlight screenshots switch with it |
| Theme toggle | switches at once, remembered after reload (`localStorage`), button label updates |
| Navigation, menu | every in-page link resolves; the mobile menu opens, closes on a link and on Esc |
| Screenshot tabs, lightbox | tabs switch (also with arrow keys); lightbox opens the visible variant, closes on click/Esc |
| Images | 17 image elements, none broken; all embedded |
| Console | no errors or warnings |
| Reduced motion | reveal animations and hover movement are off |

## Languages and search engines

- English stays at `/`, so existing links and rankings are kept. The other languages live at `/pt-br/`, `/pl/`, `/es/` and `/de/`. `/en/` stores the choice of English and opens `/?lang=en`.
- Every page has its own `lang`, title, description, Open Graph and Twitter texts and link-preview image, a self-referencing canonical URL, `hreflang` links to all five languages plus `x-default`, and `og:locale` with its alternates.
- A visitor who picked a language is taken there from `/` on later visits. Search engines store nothing and so always see the English page; nothing is redirected by browser language. Instead, a visitor whose browser prefers another of the five languages sees a one-line offer in that language, which can be dismissed.
- Pages work without JavaScript; the language links in the footer are ordinary links.

## Tested in five languages (2026-11-01, built-in browser over `python -m http.server`)

| Check | Result |
|---|---|
| 375, 768, 1100, 1200, 1280 px × 5 languages | no horizontal scrolling, no header overlap, no clipped text; menu button below 1200 px (1280 px since 2026-11-15) |
| Language menu | opens by click and keyboard (arrows wrap, Esc returns focus); the current language is marked with `aria-current` |
| Switching | the section in view (within 4 px), screenshot tab and open FAQ answers carry over; the hash is kept |
| Persistence | the choice survives reloads; `/` and `/#download` open the saved language; `/en/` switches back to English |
| Images | all 42 files load on every page |
| Plurals, numbers, dates | 23 narzędzia, 22 zmiennymi, 43.000, 1. November 2026 and so on, as expected |

## Tested with the 9.0.4 screenshots (2026-11-15, built-in browser over `python -m http.server`)

190 screenshots (19 locations × 5 languages × 2 themes) captured from the running 9.0.4 program; details in
[screenshots/qa-9.0.4.md](screenshots/qa-9.0.4.md).

| Check | Result |
|---|---|
| Images | all 50 image elements resolve on every language page; each page shows only its own language's screenshots; every dark picture has its light twin |
| Theme toggle | every screenshot switches to the matching variant, repeatedly |
| Language switching | es → de → pt-BR → pl → en with the selector, alternating themes: tab and theme carry over, the new language's screenshots appear |
| 320–1920 px (13 widths) × 5 languages | no header overlap, no horizontal scrolling; screenshots keep their aspect ratio |
| Console | no messages |
| `screenshots.py validate` | 0 errors, 0 warnings |
