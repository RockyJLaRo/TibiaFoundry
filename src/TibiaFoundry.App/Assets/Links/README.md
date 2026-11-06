# Site icons of the creator links

The site icons (favicons) of TibiaWiki and Tibia, shown next to **TibiaWiki Profile** and **Tibia Character** in **About → Creator**. They belong to their owners and are shown only to label links to those sites; this does not imply any endorsement of Tibia Foundry.

| File | Site | Source | Content | SHA-256 |
|---|---|---|---|---|
| `tibiawiki-favicon.ico` | TibiaWiki (tibia.fandom.com) | The icon named by the wiki's own page head (`<link rel="icon">` on https://tibia.fandom.com/wiki/Main_Page): https://static.wikia.nocookie.net/tibia/images/4/4a/Site-favicon.ico | ICO, 48×48, 32×32 and 16×16 (32-bit), 15,086 bytes | `fc3d3562…5e9f76ab` |
| `tibia-favicon.ico` | Tibia (tibia.com) | https://www.tibia.com/favicon.ico (the icon the site's pages show) | ICO, 32×32 and 16×16 (8-bit), 3,638 bytes | `3d0b707d…b0a76b2a` |

Both files were retrieved on 2026-10-13 and are bundled **unchanged** (the full hashes are checked by `BrandingTests.SiteIconsAreTheUnchangedFaviconsOfTheirSites`).

tibia.com answers scripted requests with a browser check. The icon was therefore read from a normal browser session that had already displayed it; the browser check was not bypassed or completed. The copy was verified against the SHA-256 computed in that browser.

**Display.** The page uses the largest image of each file up to 48 px (48 px for TibiaWiki, 32 px for Tibia) and draws it at 22×22 with high-quality scaling, so it stays sharp on high-DPI screens.

**Fallback.** If a file is missing or cannot be decoded, the link shows its simple badge from `Themes/BrandIcons.xaml` instead and a warning is logged. It never shows a broken image.

**Updating.** If a site changes its icon, save the new file under the same name, check that it is an ICO or PNG image (not an executable), and update the hashes here and in `BrandingTests`.
