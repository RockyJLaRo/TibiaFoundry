tibiafoundry.com - website for Tibia Foundry {VERSION}
=====================================================

TibiaFoundry-Website.zip holds the ready-to-deploy site and its source.

Structure
  site/                    deploy this folder as is (GitHub Pages)
    index.html             English page (CSS and JavaScript inlined)
    pt-br/ pl/ es/ de/     the page in Português (Brasil), Polski, Español and Deutsch
    en/                    /en/ remembers English and opens /
    img/                   screenshots and samples, shared by all languages
    CNAME                  tibiafoundry.com
    favicon.ico, favicon-192.png, apple-touch-icon.png   icons (search results, tabs, home screens)
    og-image*.png          1200x630 previews for shared links (Discord, Facebook, X ...), one per language
    robots.txt, sitemap.xml
  src/                     source: index.template.html, locales/*.json (texts), styles.css, app.js, images/
  TRANSLATING.md           how to change, add or review translations
  i18n-report.md           translation completeness report
  build_site.py            builds site/ from src/
  README.md                developer notes on the site

Preview locally
  Open site/index.html in a browser, or serve it:
    python -m http.server 8000 --directory site      then open http://localhost:8000/

Build (only after changing src/)
  Needs Python 3 and Pillow (pip install pillow). The builder reads the version from
  src/Directory.Build.props of the source tree, so run it from the full source package:
    python -I website/build_site.py

Deploy to GitHub Pages
  Publish the whole contents of site/ (with all its folders) at the root of the Pages branch (or a /docs folder, or a Pages
  workflow artifact). Keep CNAME: the custom domain is tibiafoundry.com. In the domain's DNS, point
  the apex at GitHub Pages (A records 185.199.108.153, .109.153, .110.153, .111.153) and enable
  "Enforce HTTPS" in the repository's Pages settings.

Analytics
  The page carries one Google tag (Google Analytics 4, measurement ID G-J3EX3FB3PB). It is the only
  resource loaded from another host.

Search engines
  Every language has its own title, description, canonical URL, hreflang links and preview image;
  sitemap.xml lists all five with their alternates. After deploying, open Google Search Console for tibiafoundry.com, submit
  https://tibiafoundry.com/sitemap.xml and use URL inspection > Request indexing; Google replaces the
  old result after it recrawls the page (usually days).

Notes
  * Light and dark themes follow the system setting; the toggle remembers the choice (localStorage).
  * The language menu remembers the choice too; visitors return to the language they picked.
  * Reduced-motion preferences are honored.
  * The page has no server-side code and works from any static host.
