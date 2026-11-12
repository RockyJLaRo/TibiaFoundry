tibiafoundry.com - complete website project for Tibia Foundry {VERSION}
=======================================================================

TibiaFoundry-{VERSION}-Website-Source.zip holds everything needed to inspect, change, build and deploy
the website: its source, its build script, the file the build reads the version from, and the built site.

Structure (inside TibiaFoundry-{VERSION}-Website-Source/)
  website/
    src/
      index.template.html    markup; every visible text is a key ({{t:key}}, {{a:key}}, {{j:key}})
      locales/*.json         the texts: en.json (source) and pt-BR, pl, es, de
      styles.css             design: colour tokens for the light and dark themes, layout, responsive rules
      app.js                 language selector, theme toggle, menu, screenshot tabs, lightbox
      images/                source images (screenshots per language, logo, Content Studio samples)
      prepare_images.py      how the source images are made from program screenshots
    build_site.py            builds website/site from website/src in all five languages
    site/                    the built site for {VERSION}: DEPLOY THIS FOLDER AS IT IS
    README.md                developer notes: structure, preview, build, deployment, what was tested
    TRANSLATING.md           how to change, add or review translations
    i18n-report.md           translation completeness report (written by the build)
    legacy/                  the 8.5-8.7 page and its build script (kept for reference, not deployed)
  src/Directory.Build.props  the one place the version is written; the build reads it from here
  TibiaFoundry-{VERSION}-ReleaseNotes.md

Preview
  python -m http.server 8766 --bind 127.0.0.1 --directory website/site
  then open http://127.0.0.1:8766/ (English), /de/, /es/, /pl/ or /pt-br/

Build (only after changing website/src)
  Needs Python 3 and Pillow (pip install pillow). From the folder that holds website/ and src/:
    python -I website/build_site.py            add --strict to fail on a missing translation

Deploy (GitHub Pages, repository RockyJLaRo/TibiaFoundry, branch main, served from the root)
  1. Publish the GitHub release v{VERSION} with TibiaFoundry-{VERSION}-Installer.msi, -Portable.zip and
     -Source.zip first: the download buttons link to those files.
  2. Copy the whole contents of website/site (index.html, en/, pt-br/, pl/, es/, de/, img/, the icons,
     og-image*.png, CNAME, robots.txt, sitemap.xml) over the root of branch main. Leave everything else on
     that branch as it is - in particular .github/, tools/, games/ and pokemon/, which are not part of this site.
  3. Commit and push; Pages publishes within a minute or two. Keep CNAME (tibiafoundry.com).

Notes
  * The page has no server-side code. One Google tag (Google Analytics 4) is the only resource from another host.
  * Light and dark themes and the language choice are remembered in the browser (localStorage).
