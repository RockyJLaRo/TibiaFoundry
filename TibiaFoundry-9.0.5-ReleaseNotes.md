# Tibia Foundry 9.0.5 – Release Notes

Released 2026-11-17 · Windows 10/11 x64 · Windows Installer (MSI, in a ZIP), portable edition, source and website.
A branding, version and website release for 9.0; everything in 9.0 to 9.0.4 is included
([9.0.4 release notes](TibiaFoundry-9.0.4-ReleaseNotes.md)). The program's tools and file formats are unchanged.

## Desktop application

* **Version in the window header.** The header at the top left now has two lines:
  **Tibia Foundry v9.0.5** and the tagline **Tibia Foundry - All your Tibia-Tools for TibiaWiki** (with TibiaWiki linked,
  as before). The first line is bound to the program's version (`AppIdentity.HeaderTitle`), never written into the
  window: the next release changes `<Version>` in `src/Directory.Build.props` and the header follows by itself.
* **One authoritative version, checked.** `src/Directory.Build.props` is the only place the version is written. The
  About and Home pages, the file and product version, the assembly version, the installer (ProductVersion), the
  package names and the website read it from there. `app.manifest` no longer repeats the version by hand: the build
  stamps the four-part form (9.0.5.0) into the copy it embeds.
* **Verification.** New tests check the header against the configured version, that the window contains no version
  of its own, and that 9.0.6, 9.1.0 and 10.0.0 produce the matching header through the real version path. The
  program's built-in UI self-check (`--ui-snapshot`) now checks the rendered header and the About page.
  `scripts/check_version.py --future 9.0.6 --ui` rebuilds the program with only the version changed and confirms
  that it shows "Tibia Foundry v9.0.6".
* **Screenshots** of every documented page were retaken from 9.0.5 in all five languages and both themes, at
  1280×800, now including the About page.

## Website (tibiafoundry.com)

* **Redesigned, phone first.** Seven pages – Home, Tibia Data, Features, Downloads, Guides, About, Contact – in English,
  Português (Brasil), Polski, Español and Deutsch, in light and dark themes built from the program's own colours.
  Tested at 320 to 1920 px wide.
* **Tibia data online** through [TibiaData](https://tibiadata.com/) (API v5): characters with deaths, achievements,
  badges and other characters; worlds and who is online; guilds and members; highscores in 18 categories; houses and
  guildhalls; kill statistics; creatures; spells; boostable bosses; fansites; news, news tickers and the news archive.
  Tables can be filtered, sorted and copied as TibiaWiki wikitext or CSV; every view links to the original on Tibia.com.
* **Tibia News & Updates** on the home page, refreshed automatically, with a manual refresh and the archive.
* **About and Contact:** Rocky LaRochelle, Creator and Developer of Tibia Foundry (Upstate New York, USA), with the six
  community profiles; contact by email (rocky@tibiafoundry.com) – the site has no contact form and sends nothing itself.
* **Credits:** every page thanks TibiaData, with a link, in its footer, and credits Tibia.com, CipSoft, TibiaWiki and the
  fansite programme (*Data & resource credits*).
* **Privacy and security:** no backend; searches are not stored; news HTML goes through an allow-list sanitizer;
  external links open safely; the only tracking is the existing Google tag.

## Downloads

| Archive | Contents |
|---|---|
| `TibiaFoundry-9.0.5-MSI.zip` | `TibiaFoundry-9.0.5.msi`, the Windows Installer: Program Files, Start menu entry, upgrades 8.8 and later in place, settings and library are kept |
| `TibiaFoundry-9.0.5-Portable.zip` | one self-contained `TibiaFoundry.exe` (no .NET installation needed) that keeps its data next to itself |
| `TibiaFoundry-9.0.5-Source.zip` | the complete source: solution, tests, packaging scripts, website sources and tools |
| `TibiaFoundry-9.0.5-Website.zip` | the website: built site, sources, translations, screenshot and test tools, documentation |

SHA-256 checksums: `SHA256SUMS.txt` of the release.

## Known limitations

* Program translation gaps listed in `website/screenshots/qa-9.0.4.md` (a few English labels in other languages) are
  unchanged in 9.0.5; the screenshots show the program as it is.
* The data pages depend on TibiaData being reachable; when it is not, the page says so and offers a retry (an older
  copy is shown, marked as such, when one is available in the browser).
* Per-character link previews (Open Graph) are not generated – the site is static; data views set the page title after
  loading.
* The MSI was validated (product metadata, files, checksums) but not installed on the build machine (per-machine install
  needs an elevated prompt).
