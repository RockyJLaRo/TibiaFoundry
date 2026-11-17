# Website – third-party software

The published website (`website/site`) contains **no third-party code**: its scripts (`src/js/*.js`) and style sheet
are written for this project; the only external script is the Google tag (analytics), loaded from Google.

Tools used to build and test the site (not distributed with it):

| Tool | Licence | Used for |
|---|---|---|
| Python 3 | PSF License | `build_site.py`, test and screenshot scripts |
| Pillow | MIT-CMU (HPND) | image conversion (WebP), link-preview images |
| NumPy | BSD-3-Clause | screenshot comparison (`screenshots/screenshots.py`) |
| Playwright for Python 1.55 + Chromium | Apache-2.0 (Chromium: BSD-3-Clause and others) | `tests/e2e.py`, `tests/site_screenshots.py` |
| Node.js 22 (`node --test`) | MIT | `tests/unit/tibiadata.test.mjs` |

Data and content providers (TibiaData, Tibia.com, CipSoft, TibiaWiki, fansites) are credited in `ATTRIBUTION.md` and in
the footer of every page.
