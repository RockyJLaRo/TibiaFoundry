# Credit logos

Logos of the three organizations listed in **About → Credits**. They belong to their owners. They are shown only to credit these organizations and to link to their websites; this does not imply any endorsement of Tibia Foundry.

| File | Organization | Source (official) | Original | Shown |
|---|---|---|---|---|
| `tibia-logo.png` | Tibia | CipSoft press material, "Tibia" package: `Logos/Tibia_Logo.png` (https://www.cipsoft.com/en/press/press-material) | 1020×402 PNG, SHA-256 `4ccd20cd…c39ee1` | reduced to 340×134 (exactly ⅓) |
| `cipsoft-logo-light.png` | CipSoft | CipSoft press material, "CipSoft" package: `Logos/logo-cipsoft-h-pos-rgb.png` (positive version, for light backgrounds) | 2583×397 PNG, SHA-256 `91084db1…7c8d61` | reduced to 436×67 |
| `cipsoft-logo-dark.png` | CipSoft | same package: `Logos/logo-cipsoft-h-neg-rgb.png` (negative version, for dark backgrounds) | 2583×397 PNG, SHA-256 `a34f0567…86d5bb` | reduced to 436×67 |
| `tibiawiki-logo.png` | TibiaWiki | The wiki's own site logo: https://static.wikia.nocookie.net/tibia/images/e/e6/Site-logo.png | 250×65 PNG, SHA-256 `117c6e33…0d4dbd` | unchanged |

**Terms.** The CipSoft press page (checked 2026-10-12) says that its photos, screenshots and artworks "can be downloaded and published without prior authorisation by CipSoft"; it asks for a short notification after publication (press@cipsoft.com). The TibiaWiki logo is the wiki's own header image.

**Rebuilding.** Download the four originals into a folder, then run:

```
python -I tools/branding/make_credit_logos.py <folder>
```

The script refuses files whose SHA-256 differs from the reviewed originals. The logos are smooth artwork, so they are reduced with Lanczos filtering, not nearest-neighbor scaling, and their aspect ratio is kept within 0.1 %. Only the light or dark CipSoft logo is shown, depending on the theme.

If a file is missing or cannot be decoded, the About page shows the organization's name in its place and logs a warning. It never shows a broken image.
