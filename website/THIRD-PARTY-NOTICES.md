# Website – third-party software

The published website (`website/site`) contains **no third-party code**: its scripts (`src/js/*.js`) and style sheet
are written for this project; the only external script is the Google tag (analytics), loaded from Google.

**Third-party artwork in the published pages:** the interface icons are 49 icons of **Lucide 0.460.0**
(https://lucide.dev), copied unchanged into `art/lucide/` and inlined into every page by `art/icons.py`. Lucide is
licensed under the ISC License (portions from Feather, MIT); its licence text follows. The profile icons in the footer
and on the About page are the platforms' own logos (YouTube, Instagram, GitHub, Reddit, TibiaWiki, Tibia.com), used only
to link to those profiles. The pixel ornaments (battlement, bricks, corners, gem) are original work for Tibia Foundry.

<details><summary>Lucide licence</summary>

```
ISC License

Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part of Feather (MIT). All other copyright (c) for Lucide are held by Lucide Contributors 2022.

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```
</details>

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
