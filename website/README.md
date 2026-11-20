# tibiafoundry.com – website (9.0.5, 2026 redesign)

A mobile-first, multi-page static site in **five languages** (English, Português (Brasil), Polski, Español, Deutsch),
with a "stone and parchment" design (light and dark; see `docs/redesign/design-system-and-concepts.md`), original pixel art,
and **online Tibia tools** powered by
[TibiaData](https://tibiadata.com/). Built for **GitHub Pages** (static hosting, no backend, no build step on the server).
Translation guide: [TRANSLATING.md](TRANSLATING.md) · API integration: [TIBIADATA.md](TIBIADATA.md) ·
credits: [ATTRIBUTION.md](ATTRIBUTION.md) · completeness: [i18n-report.md](i18n-report.md).

## Pages

| Page | URL (English; other languages under `/pt-br/`, `/pl/`, `/es/`, `/de/`) | Content |
|---|---|---|
| Home | `/` | headline for both audiences, character search, **Today in Tibia** (live boosted creature/boss, players online), six task cards, desktop-app spotlight with the download notice, three feature highlights, **Tibia News & Updates** (live), creator |
| Tibia tools | `/tibia/` + `#/route` | grouped overview with pixel icons; characters, worlds, guilds, highscores, houses, kill statistics, creatures, spells, boostable bosses, fansites, news and archive (20 routes, see `src/registry.json`) |
| Features | `/features/` | audiences, all feature groups, infobox exporter, Asset Library, Content Studio, publishing profiles, screenshot gallery (20 program pages), safety |
| Downloads | `/downloads/` | download notice, MSI (in a ZIP), portable and source archives with canonical v{version} URLs, sizes and SHA-256; release facts (date, systems, signing, scan), safety, requirements, first steps, release notes |
| Guides | `/guides/` | first steps, using the Tibia data tools, documentation links, FAQ |
| About | `/about/` | Rocky LaRochelle – Creator and Developer of Tibia Foundry, Upstate New York, USA; six profiles; project; data credits |
| Contact | `/contact/` | rocky@tibiafoundry.com (mail link and copy button), responsible administrator with Tibia character, GitHub issues – no contact form, nothing is sent from the site |
| Legal & transparency | `/legal/` | copyright and CipSoft, Fansite Programme status, responsible administrator, downloads and safety, money and advertising, privacy and statistics (opt-out), community rules and spoiler policy, content sources |

Every page has the header (the global Tibia search row on top – `registry.json` `search`, `src/js/search.js` – then the brand, the grouped menu, language, theme and Download;
below 1024 px, or when the labels do not fit, a drawer) and breadcrumbs on inner pages. The menu is `menu` in
`src/registry.json`: header panels, drawer and footer link columns all come from it (`src/js/nav.js` runs the panels and
the drawer). The footer has the brand block with the creator's email and six profiles, link groups, **Data &
resource credits** (TibiaData thank-you with link, Tibia.com, CipSoft, TibiaWiki, fansites), links to Tibia.com, CipSoft
and the Tibia Rules, and the copyright year (updated in the browser).

## Structure

| Path | What it is |
|---|---|
| `site/` | **Deploy this folder** (generated): pages per language, `assets/` (CSS and JS with content hashes), `img/`, favicons, link previews, `404.html` (sends `/tibia/character/Name` to `/tibia/#/character/Name`), `.nojekyll`, `sitemap.xml`. `CNAME` and `robots.txt` are the Pages repository's own and are never written over |
| `src/registry.json` | the structure: languages, pages, data routes with their sample (fixture) values, API base and cache times, download files, creator profiles, documentation links |
| `src/providers.json` | attribution registry of every external data or content provider |
| `src/templates/` | `layout.html` (head, header, footer) and one template per page; texts only as keys |
| `src/locales/*.json` | the texts (`en.json` is the source) |
| `src/css/` | mobile-first design system in cascade layers (tokens, base, components, pages, utilities); bundled into `site.css` |
| `src/js/` | `site.js` (texts, theme, menu, language, search, copy, tabs, lightbox), `tibiadata.js` (API client), `sanitize.js` (news HTML), `data-app.js` (router, views, home news) |
| `src/images/` | logo, real Content Studio output, and `screens/<code>/<id>-<code>-<theme>.png`: the program screenshot library (1280×800, 20 pages × 5 languages × 2 themes) |
| `screenshots/` | program screenshot generator ([screenshots/README.md](screenshots/README.md)) |
| `tests/` | `check_i18n.py`, `unit/tibiadata.test.mjs`, `e2e.py`, `site_screenshots.py`, `record_fixtures.py`, `sitekit.py`, `fixtures/` |
| `build_site.py` | builds `site/` and `i18n-report.md` |

## Build and preview

```bash
pip install pillow numpy
python -I website/build_site.py --strict
python -m http.server 8766 --bind 127.0.0.1 --directory website/site
```

`--checksums Release/9.0.5/SHA256SUMS.txt` shows the archives' SHA-256 on the Downloads page (`scripts/build_release.py`
does this). The build refuses a page with unreplaced placeholders, more than one Google tag, resources from other hosts,
not exactly one `<h1>`, a missing or unlinked data credit, or a new-tab link without `rel="noopener noreferrer"`.

## Tests

```bash
python -I website/tests/check_i18n.py                  # locale key parity (5 × all keys) and English Title Case
node --test website/tests/unit/tibiadata.test.mjs      # API client: cache, de-duplication, cancellation, errors, schemas
pip install playwright==1.55.0 && python -m playwright install chromium
python website/tests/e2e.py --report e2e.json          # pages, attribution, 6/6 profiles, navigation, language/theme,
                                                       # Tibia data, news + failures, contact, downloads, deep links,
                                                       # sanitizer, accessibility, fixture leak, responsive (9 widths)
python website/tests/e2e.py --only tibia-data --live   # plus a smoke test against the real API
python website/tests/site_screenshots.py               # 33 routes (404 in English only) × 5 languages × 2 themes × 7 viewports
python -I website/tests/record_fixtures.py             # re-record the TibiaData fixtures
```

`site_screenshots.py` writes full-page screenshots and `manifest.json` to `Release/<version>/qa-screenshots/` (never
committed; `--out <folder>` for another place). The manifest lists page, language, theme, viewport and status (ok / failed)
of every image, plus a layout scan: page overflow, elements past the right edge and text clipped by `overflow: hidden`. Routes, languages, themes and samples come from `src/registry.json`, so new pages are picked up automatically.

## Hosting and routing

GitHub Pages serves `site/` from the root of `RockyJLaRo/TibiaFoundry` (branch `main`, custom domain via its CNAME).
Static pages are real folders, so every page URL works when reloaded. The Tibia data views live in the URL hash
(`/tibia/#/guild/Name`): a reload never reaches the server's routing. Path-style links (`/tibia/guild/Name`) hit GitHub
Pages' `404.html`, which redirects to the hash form. Search engines see static titles and descriptions per page; data
views set the document title after loading (there is no server-side rendering of per-character metadata).

## Deploy

1. Publish the release assets first (the Downloads page links to `releases/download/v{version}/...`).
2. Copy the **contents** of `site/` into the Pages repository root – never touching `games/`, `pokemon/`, `tools/`,
   `.github/`, `CNAME` or `robots.txt` – and remove files of the previous site that no longer exist.
3. Commit and push; check every page and a data view on https://tibiafoundry.com/.

**Analytics:** the Google tag `G-J3EX3FB3PB` is unchanged; click events for downloads, releases, repository and issues
links. No other tracking. Stored in the browser: language, theme, last world (localStorage) and the API cache
(sessionStorage). Searches are not stored.
