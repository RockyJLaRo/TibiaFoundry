# Branding, Credits and Attribution – Tibia Foundry 6.0

Tibia Foundry 4.0 is the renamed Tibia Forge 3.0, which was itself the renamed and extended version 2.1 of the reconstructed original program. Version 5.0 added the tagline, the site icons of the creator links and the open-source acknowledgments (§1, §3.1). This document describes:

* the identity and the logo (§1–2);
* the Credits section and the open-source acknowledgments (§3);
* the creator attribution embedded in exported files (§4–8);
* migration from earlier versions (§9);
* what changed, how it was verified, and what is still open (§11–14).

**Placeholders.** `<v2>` stands for the identifier of version 2.x (also used by the original 1.x program), `<v3>` for that of version 3.0, and `<original>` for the file names of the original program.

## 1. Identity

| Item | Value | Defined in |
|---|---|---|
| Application name (shown to users) | **Tibia Foundry** | `Core/Common/AppIdentity.Name`, window title, About, `AssemblyTitle`/`Product` |
| Identifier (attribution, User-Agent, files and folders) | **TibiaFoundry** | `AppIdentity.Id` |
| Creator / developer | **RockyLaRo**, TibiaWiki Admin | `AppIdentity.Creator`, `CreatorRole`; assembly `Company`, `Authors`, `Copyright` |
| Website | **https://tibia.fandom.com/** | `AppIdentity.Website` = `ExternalEndpoints.TibiaWikiHome` |
| Version | 6.0.0 | `src/Directory.Build.props` |
| Executable | `TibiaFoundry.exe` (file description "Tibia Foundry") | `TibiaFoundry.App.csproj` |
| Projects and namespaces | `TibiaFoundry.App`, `TibiaFoundry.Core`, `TibiaFoundry.Tests`; `TibiaFoundry.slnx` | `src/` |

The version is 4.0 rather than 3.1 because the program file, its settings folders and the identifier in exported files changed. Settings and files of earlier versions are still read (§9).

### Where the identity appears

* **Window**: title "Tibia Foundry", window and taskbar icon.
* **Navigation header**: the logo at its native 32 px, "Tibia Foundry", and the tagline **"Tibia Foundry - All your Tibia-Tools for TibiaWiki"**. "TibiaWiki" in the tagline is a link to https://tibia.fandom.com/wiki/Main_Page. A line break after "All your" keeps "Tibia-Tools" from wrapping at its hyphen; the words are exactly as specified. With compact navigation (narrow window), only the logo is shown and it carries the tagline as its tooltip.
* **About page**, in this order:
  * the logo at 128 px with name, the tagline (same wording and link), version, a description of the program and "Created by RockyLaRo, TibiaWiki Admin, for TibiaWiki";
  * **Creator**: the creator profile with six icon links;
  * **Attribution in your exports**: the active attribution text;
  * **Credits**: Tibia, CipSoft and TibiaWiki (§3);
  * **Open-source acknowledgments**: Nekiro's RCC Editor and Arch-Mina's ClientConverter and Assets Editor (§3.1);
  * **Open-source components**: the libraries shipped with the program and their licenses, with a pointer to `THIRD-PARTY-NOTICES.txt`;
  * **Runtime environment**.
* **Settings**: the "Creator attribution" card.
* **Messages**: start-up and error dialogs, the self-test and compatibility report headers, the Compatibility page, log lines.
* **Files and network**:
  * the User-Agent `TibiaFoundry/5.0.0 (+https://tibia.fandom.com/)`;
  * log files `TibiaFoundry-<date>.log`;
  * folders `%APPDATA%/TibiaFoundry`, `%LOCALAPPDATA%/TibiaFoundry`, portable `TibiaFoundry-Data`, marker file `TibiaFoundry.portable`;
  * the sprite folder manifest `TibiaFoundry-sprites.json`;
  * the temporary folders `%TEMP%/TibiaFoundry` (sound playback) and `%TEMP%/.net/TibiaFoundry` (single-file runtime).
* **Executable metadata**: Product "Tibia Foundry", Company "RockyLaRo", Copyright "Tibia Foundry © 2026 RockyLaRo (TibiaWiki Admin). Tibia game graphics © CipSoft GmbH.", Description "Tibia Foundry extracts sprites, images, sounds and game data from the installed Tibia client and prepares them for TibiaWiki", application manifest identity `TibiaFoundry.app` 5.0.0.0.
* **Distribution**: `dist/TibiaFoundry-5.0.0-win-x64.zip`, `dist/TibiaFoundry-5.0.0-portable-win-x64.zip`, `README-Portable.txt`, `THIRD-PARTY-NOTICES.txt`.

There is no installer (the program ships as zips) and no splash screen.

### Creator profile (About page)

Each link is a button with an icon. Only the addresses listed on the About page can be opened from it.

| Label | Address | Icon |
|---|---|---|
| TibiaWiki Profile | https://tibia.fandom.com/wiki/User:RockyLaRo | **the favicon of tibia.fandom.com** |
| Tibia Character | https://www.tibia.com/community/?name=Roc+ky | **the favicon of tibia.com** |
| YouTube | https://www.youtube.com/@Roc_ky | YouTube play button |
| Instagram | https://www.instagram.com/rocky_tibia/ | Instagram camera |
| GitHub | https://github.com/RockyJLaRo | GitHub mark |
| Reddit | https://www.reddit.com/user/Roc-ky/ | Reddit Snoo |

* **5.0 changes**: the first two labels are capitalized as specified. The second, redundant *TibiaWiki* entry (https://tibia.fandom.com/) was removed; the wiki stays linked from the profile, the tagline and the credits.
* **Site icons**: the two favicons are bundled unchanged in `Assets/Links/` (sources, sizes and SHA-256 in its README). The page uses the largest image of each file up to 48 px, drawn at 22×22. If a file is missing or damaged, the link shows its vector badge from `Themes/BrandIcons.xaml` (blue "W" for TibiaWiki, gold sword for Tibia) and a warning is logged. The other four icons are vector drawings in the same file.
* **Retrieval (2026-10-13)**: the TibiaWiki icon is the one the wiki's page head names (`static.wikia.nocookie.net/tibia/images/4/4a/Site-favicon.ico`). tibia.com answers scripted requests with a browser check, so its `favicon.ico` was taken from a normal browser session that had already loaded it; the check was not bypassed or completed. The saved copy was verified against the SHA-256 computed in that browser.
* **Links checked**: all six addresses are unchanged since 3.0, when they were checked: YouTube, Instagram, GitHub and Reddit answered 200; TibiaWiki and tibia.com block command-line clients and were opened in a browser. On 2026-10-13 tibia.fandom.com/wiki/Main_Page was opened in a browser again. www.tibia.com showed its browser check, which was not completed.

## 2. Application logo

* **Source**: the official logo supplied on 2026-10-12, a 32×32 RGBA PNG with hard-edged transparency and a black outline that works on light and dark backgrounds. It was supplied as the logo of Tibia Forge and contains no text, so it is used unchanged for Tibia Foundry. **No separate Tibia Foundry logo has been supplied** (see §13).
* **Stored unchanged** as `src/TibiaFoundry.App/Assets/Branding/tibia-foundry-logo.png`, byte-identical to the supplied file (SHA-256 `ec4968ab…c8629`, checked by `BrandingTests`). In 4.0 the files were only renamed from `tibia-forge-*` to `tibia-foundry-*`; their content is identical.

| Variant | How it is made | Used for |
|---|---|---|
| 32 px (master) | as supplied | navigation header, self-test |
| 128 px on screen | the master, displayed with `BitmapScalingMode="NearestNeighbor"` | About page |
| `tibia-foundry.ico`: 16, 24, 32, 48, 64, 128, 256 px | nearest-neighbor sampling only; 16–48 px as 32-bit DIBs, larger ones as PNG | exe icon (Explorer, taskbar, shortcuts), window icon |
| `tibia-foundry-logo-256.png` | exact 8× nearest-neighbor enlargement | README, documentation |

The generator is `tools/branding/make_branding.py`. Every output pixel is a copy of one logo pixel. The application logo is kept apart from the three organization logos of the Credits section.

## 3. Credits

**About → Credits lists exactly three organizations**, each with its name, official logo, a one-line description and its website. The whole entry (logo and name) is one button that opens the website.

| Organization | Website (as specified) | Description shown |
|---|---|---|
| Tibia | http://tibia.com/ | The online role-playing game whose client files Tibia Foundry reads. |
| CipSoft | https://www.cipsoft.com/en/ | The developer and publisher of Tibia, and the owner of its graphics and game data. |
| TibiaWiki | https://tibia.fandom.com/wiki/Main_Page | The community encyclopedia of Tibia that Tibia Foundry prepares images and data for. |

Below the entries: "Tibia, its game graphics and data are © CipSoft GmbH. The logos are shown to credit these organizations and link to their websites. Tibia Foundry is an independent fan project and is not affiliated with or endorsed by CipSoft GmbH or Fandom."

**Logos** (details and checksums in `src/TibiaFoundry.App/Assets/Credits/README.md`):

| File | Official source | Original | In the program |
|---|---|---|---|
| `tibia-logo.png` | CipSoft press material, "Tibia" package, `Logos/Tibia_Logo.png` | 1020×402 PNG | 340×134 (exactly ⅓) |
| `cipsoft-logo-light.png` | CipSoft press material, "CipSoft" package, `Logos/logo-cipsoft-h-pos-rgb.png` (for light backgrounds) | 2583×397 PNG | 436×67 |
| `cipsoft-logo-dark.png` | same package, `Logos/logo-cipsoft-h-neg-rgb.png` (for dark backgrounds) | 2583×397 PNG | 436×67 |
| `tibiawiki-logo.png` | TibiaWiki's own site logo, `static.wikia.nocookie.net/tibia/images/e/e6/Site-logo.png` | 250×65 PNG | unchanged |

* **Where they came from**: CipSoft's press page (https://www.cipsoft.com/en/press/press-material, checked 2026-10-12) offers these logo packages and states that its graphics "can be downloaded and published without prior authorisation by CipSoft"; it asks for a short notification after publication. tibia.com itself only offers a WebP version of its logo, which WPF cannot decode without an optional Windows codec, so the press-kit PNG was used. The TibiaWiki logo is the image the wiki shows in its own header.
* **Processing**: `tools/branding/make_credit_logos.py` checks each original against its reviewed SHA-256, reduces the Tibia and CipSoft logos to about twice their largest on-screen size with Lanczos filtering on premultiplied alpha, and copies the TibiaWiki logo byte for byte. The logos are smooth artwork, not pixel art, so nearest-neighbor scaling is not used for them. Aspect ratios are kept within 0.02 %. Transparency is kept.
* **Presentation**: three tiles of equal size. Each logo fits the same 216×64 box with its own proportions and high-quality scaling. The tiles wrap when the window is narrow. CipSoft's logo switches between its light-background and dark-background versions with the theme.
* **Accessibility**: each logo is named "<organization> logo"; each tile's accessible name says which website it opens, and its help text is the description.
* **Missing files**: if a logo cannot be loaded, the tile shows the organization's name in large text and a warning is logged. A broken image is never shown.
* **Kept, separately**: *Open-source components* lists the libraries shipped with the program and their licenses (.NET and WPF MIT, Google.Protobuf BSD-3-Clause, Magick.NET Apache-2.0 with ImageMagick, NAudio and NVorbis MIT, Newtonsoft.Json MIT, CommunityToolkit.Mvvm MIT, LZMA SDK public domain). This is a license notice, not a credit, and is required by those licenses when the program is redistributed. The creator profile is also a separate section.
* **Full license texts (5.0)**: both editions include `THIRD-PARTY-NOTICES.txt`. It holds the license text or notice of every component, made by `tools/release/make_notices.py` from the license files in the local package cache. The script fails if a package of the build has no entry. Magick.NET's notice covers ImageMagick and the libraries it bundles.

### 3.1 Open-source acknowledgments (5.0)

A separate section after the credits names the open-source projects whose tools were the reference for the RCC Editor, the Client Converter and the Assets Editor:

| Project | Author | Addresses | License found |
|---|---|---|---|
| Nekiro's RCC Editor | Nekiro | https://github.com/nekiro/Nekiro-Rcc-Editor, https://github.com/nekiro | MIT (declared in its `package.json`) |
| ClientConverter | Arch-Mina | https://github.com/Arch-Mina/ClientConverter, https://github.com/Arch-Mina | no license published |
| Assets Editor | Arch-Mina | https://github.com/Arch-Mina/Assets-Editor, https://github.com/Arch-Mina | no license published |

* Each entry links to the project and to its author, and says how the project relates to Tibia Foundry. The section says that Tibia Foundry implements these tools itself and includes no code from them.
* A notice states that the projects and their authors are not affiliated with Tibia Foundry and do not endorse it.
* The credits stay exactly Tibia, CipSoft and TibiaWiki. The acknowledgments are not credits, and `BrandingTests` checks that the two sections stay apart.
* The license findings and their consequences are in `14_Tools_Settings_and_Scripts.md` §12.

## 4. Creator attribution

**Text**, embedded automatically in every supported export:

```
Created by <creator name> using TibiaFoundry for TibiaWiki @ https://tibia.fandom.com/
```

`'Username'` from the specification is replaced by the configured name, without quotes. The identifier is always `TibiaFoundry` and the URL is always the canonical one.

**Creator name** (*Settings → Creator attribution*):

* **Editing**: the text box applies the name while typing (after a 0.4 s pause), saves it, and shows the exact text that is written. The Appearances → Export tab and the About page show the same text, with a button that opens Settings.
* **Default**: `RockyLaRo`. It is a setting, not hard-coded into exports: the code always uses the configured name, and tests export with other names. An empty name restores the default.
* **First start**: on a fresh installation the status bar says "Exported images are attributed to RockyLaRo. If that is not you, set your creator name in Settings."
* **Kept**: the name survives *Reset to defaults*.
* **Imported**: settings of version 3.0 keep their creator name. The author of the settings of versions 1.x (`Author`) and 2.x (`GifAuthor`) becomes the creator name, except the old default author value, which counts as "not set".

**Policy** (`Core/Metadata/Attribution.cs`):

* **New assets**: everything made from client data, such as sprites, sheets, GIFs, outfit images, house images, maps and map tiles.
  * creator = the configured user;
  * a separate rights field credits the depicted artwork: "Tibia game graphics © CipSoft GmbH".
* **Re-exports of imported image files**: the inputs' attribution is read first (PNG text and XMP, GIF comments and XMP; BMP has none).
  * **Inputs name someone else**: a dialog asks once per operation. *Keep original creators* (default, also used in unattended runs) keeps the creators and the text, adds "Edited by <user> using TibiaFoundry …", and appends a history entry. *Attribute to me* names the user and keeps "previously attributed to …" in the history. *Cancel* writes nothing.
  * **Inputs unattributed, or by the same user**: the export is the user's, with no question.
* **History**: every export appends an entry like "2026-10-12T10:26:41-04:00 Created by RockyLaRo with Tibia Foundry 4.0.0", stored in XMP and in a PNG text chunk.

## 5. Format support

| Export | Mechanism | Fields |
|---|---|---|
| **PNG**: every saved image. Sprites (Extract Sprites, gallery), whole sheets, outfit sheets, the sheet builder and slicer, house images, HR maps, extracted map tiles, Cyclopedia tiles, sprites saved into the sprite folder, self-test images | Text chunks after `IHDR`: `tEXt` for ASCII values, UTF-8 `iTXt` for anything else (common readers decode `tEXt` as UTF-8 and garble characters like "©"). Plus an XMP packet in `iTXt` `XML:com.adobe.xmp`. | **Text chunks**: `Author`, `Description` (the sentence), `Comment` (the sentence), `Copyright`, `Software` ("Tibia Foundry 4.0.0"), `Creation Time` (RFC 1123), `TibiaFoundry History`.<br>**XMP**: `dc:creator`, `dc:description`, `dc:rights`, `xmp:CreatorTool`, `xmp:CreateDate`, `xmp:MetadataDate`, `xmpRights:WebStatement`, `photoshop:Credit`, `tf:Attribution`, `tf:Creator`, `tf:ExportedBy`, `tf:Application`, `tf:Website`, `tf:History` (namespace `urn:tibiafoundry:attribution:1.0/`) |
| **GIF**, animated and still: appearance, outfit and creature GIFs, single-sprite GIFs of Extract Sprites | One comment extension with exactly the sentence. Plus an XMP application extension `XMP DataXMP` with the 258-byte magic trailer of the XMP specification, after the global color table and the NETSCAPE loop block. | The sentence; the full XMP set as for PNG |
| **PNG data URI**: *Outfiter sheet → Copy as Base64 data URI* | PNG as above | as PNG |
| **Clipboard image**: *Copy* sheet, house or sprite | The clipboard also gets a "PNG" format with the attributed PNG. The bitmap format next to it cannot carry metadata (Windows limitation). | as PNG, in the PNG format only |
| **Lua module**: *Save Lua for all NPCs* | Two `--` comment lines at the top; the Lua code is unchanged | sentence; "Tibia Foundry 4.0.0, <date>" |
| **Comparison report** (`.txt`) | Two header lines and a blank line | sentence; software and date |
| **JSON exports**, **tab-separated tables**, **clipboard text** | **Not embedded.** JSON has no comment facility and an extra field would change the data that scripts read; a comment row would break a table's header. The status message says that no attribution was embedded. | – |
| **Verbatim client files**: extracted sounds, *Decode LZMA* output, *Download list* files | **Not modified**, deliberately. They are CipSoft's files; "Created by <user>" would misattribute them, and downloads are verified against the checksum in their names. | – |
| JPEG, WebP, SVG, BMP, ICO | Tibia Foundry does not export these. Pixel art is exported losslessly as PNG and GIF. BMP and GIF are accepted as *inputs* to the sheet builder, and their attribution, if any, is kept. | – |

No visible watermark or text is drawn into the artwork by default. The only exception is the optional TibiaWiki watermark of *Build HR map*, which is off by default and described as part of the image in its tooltip.

## 6. Preservation

**Workflows that preserve attribution** (all tested):

| Workflow | Behavior |
|---|---|
| Sheet builder from image files (combine; converts GIF, BMP and PNG to PNG) | creators of the inputs kept; one question for the whole sheet |
| Sheet builder from a sprite folder | same: folder sprites that carry attribution keep it |
| Sheet slicer | every cell keeps the sheet's attribution |
| HR map (stitches extracted tiles) | tile creators kept |
| Cyclopedia floor images to minimap tiles | attribution of the floor images kept |
| Re-encoding a PNG that already has attribution | old text chunks replaced, never duplicated |
| Files exported by Tibia Forge 3.0 | read as before (its XMP namespace `urn:tibiaforge:attribution:1.0/` and its `TibiaForge History` chunk); on re-export the old history is kept and the old history chunk is replaced by `TibiaFoundry History` |
| GIFs of the original 1.x program ("Created by NAME using <v2> for Tibia.Fandom.com") | recognized as created by NAME |
| Batches: Extract Sprites (PNG and GIF), map images, Cyclopedia tiles, slicer, GIF export of several appearances, sprite folder write-through | one attribution per run, built and verified once, embedded in every file |

Renders composed from sprites (GIFs, outfits, houses, sheets from sprite IDs) are new assets of the current user even when the sprites came from a folder someone else extracted: the sprites are CipSoft's artwork, credited in the rights field, and extracting them is not authorship.

## 7. Validation and failure safety

* **Before writing**, the attribution is validated: a creator must be present, and the text must be non-empty, without control characters and at most 4,000 characters. Failure raises `AttributionException`.
* **After embedding**, the PNG or GIF is read back and the sentence and creators are compared (once per batch).
* **The written file** is read back from disk and compared byte for byte before the atomic replace. Any failure deletes the temporary file, so an existing file is never touched and no partial file is left.
* **Reading other files' metadata** is defensive: text chunks with a bad CRC are skipped, compressed text is limited to 1 MB, XML is parsed with DTDs prohibited, and malformed XMP falls back to the text chunks.

## 8. Graphics quality

Embedding only inserts metadata blocks. Tests compare against exports without metadata and confirm unchanged image data (pixel comparison), dimensions, alpha (transparent, half-transparent and opaque pixels), GIF frame count, frame pixels, delays and the infinite loop. All 223,694 sprites still match the original program's output.

## 9. Compatibility and migration

| What | Version 2.x | Version 3.0 | Tibia Foundry 4.0–6.0 | Migration |
|---|---|---|---|---|
| Executable | `<v2>.exe` | `<v3>.exe` | `TibiaFoundry.exe` | new file; earlier releases kept in `07_baseline_2.1.0/` … `11_baseline_6.0.0/` |
| Settings (installed) | `%APPDATA%/<v2>` | `%APPDATA%/<v3>` | `%APPDATA%/TibiaFoundry` | on first start the newest earlier settings file is read and upgraded; the old file is never modified |
| Settings (portable) | `<v2>-Data` | `<v3>-Data` | `TibiaFoundry-Data` | same, next to the program only |
| Portable marker | `<v2>.portable` | `<v3>.portable` | `TibiaFoundry.portable` | all three select portable mode |
| Settings format | 1 (`GifAuthor`) | 2 (`CreatorName`) | 2 (4.0), 3 (5.0), 4 (6.0) | each older format is upgraded once; 5.0 keeps the 6.0 sections it does not know |
| Logs, cache | `…/<v2>/…` | `…/<v3>/…` | `…/TibiaFoundry/…`; 6.0 logs in `Documents/Tibia_Foundry/Logs` (installed edition) | not copied: logs are history, the cache refills itself |
| Sprite folder manifest | `<v2>-sprites.json` | `<v3>-sprites.json` | `TibiaFoundry-sprites.json` | all three are read, so extracted folders stay trusted |
| Attribution identifier | – | `<v3>` (sentence, history keyword, XMP namespace) | `TibiaFoundry` | old files keep their text; their metadata is read (§6) |

Going back to an earlier version keeps working: its files are left untouched. Messages about migrated settings say "an earlier version" or "the original 1.x program".

## 11. Files changed in 4.0 (main ones)

* **Renamed**: `src/TibiaForge.*` became `src/TibiaFoundry.*`; `TibiaFoundry.slnx`; all namespaces; `Assets/Branding/tibia-foundry-*` (same content).
* **New**:
  * `App/Assets/Credits/` with the four logos and a README;
  * `tools/branding/make_credit_logos.py`;
  * `CreditEntry` and the Credits list in `AboutViewModel`; `Images.TryLoadResource`;
  * the credits checks in `Views/UiSnapshot.cs`.
* **Changed**:
  * `Core/Common/AppIdentity.cs` (new identity, both earlier generations in `Legacy`);
  * `App/Services/AppPaths.cs`, `Core/Settings/SettingsStore.cs`, `AppSettings.cs`, `Sprites/SpriteFolderManifest.cs` (migration from versions 3.0 and 2.x);
  * `Core/Metadata/PngMetadata.cs`, `XmpPacket.cs` (new identifier, Tibia Forge 3.0 metadata read);
  * `Core/Net/ExternalEndpoints.cs` (credit addresses);
  * `Views/Pages/AboutPage.xaml` (new Credits section, open-source components);
  * descriptions, labels, tooltips and messages on every page and in the dialogs (see the CHANGELOG);
  * `Directory.Build.props` (version 4.0.0, description), `app.manifest`, `Portable.pubxml`.
* **Tests**: `BrandingTests`, `AttributionTests`, `AttributionWorkflowTests`, `SettingsTests`, `ViewModelTests`, `NetworkTests`; `tools/verification/mutation_checks.py` (T07–T10).

### Branding files changed in 6.0

* **Changed**: `Views/Pages/SettingsPage.xaml` (the import button of Settings → Advanced and its tooltip); `App.xaml.cs` (migration messages); `Core/Common/AppIdentity.cs` (`Version3Id`); comments in Core (formats, settings, proto headers); the tests that used the earlier names (renamed, or using `AppIdentity.Legacy`); `tools/verification/mutation_checks.py` (test names); the README, the CHANGELOG and every document (placeholders).
* **Tests**: `FreshAndMigratedSettingsDoNotBringBackTheOldNames`; `ObsoleteNamesRemainOnlyAsDocumentedMigrationReferences` now also covers the Markdown and proto files of the application and allows only the identifier constants.

### Branding files changed in 5.0

* **New**: `App/Assets/Links/` (the two site icons and a README); `Images.TryLoadIcon`; `Acknowledgment` and `AboutViewModel.Acknowledgments`; `tools/release/make_notices.py`.
* **Changed**:
  * `Views/MainWindow.xaml` (the tagline with its link);
  * `Views/Pages/AboutPage.xaml` (tagline, site icons with badge fallback, acknowledgments section, pointer to the notices);
  * `ViewModels/AboutViewModel.cs` (labels, the redundant TibiaWiki link removed, acknowledgments, the link allowlist);
  * `Core/Net/ExternalEndpoints.cs` (the acknowledgment addresses);
  * `TibiaFoundry.App.csproj` (`Assets\Links\*.ico` as resources).
* **Tests**: `BrandingTests` (labels, site icons and their hashes, badge fallback, tagline in the window and on the About page, acknowledgments apart from the credits); UI snapshot checks; reverse check F16.

## 12. Dependencies

* **None added, removed or changed** (4.0, 5.0 and 6.0).
* The organization logos are static PNG files; no image library is used at run time beyond WPF.
* Pillow (Python) is used only by the two generator scripts in `tools/branding/`, not to build or run the program.

## 13. Tests

* **Branding**: identity and file metadata (4.0); no obsolete names in code or XAML; the logo unchanged and shown with nearest-neighbor scaling; the icon an exact nearest-neighbor copy; creator links.
* **Credits**: exactly Tibia, CipSoft and TibiaWiki with the specified addresses; accessible names; the official logo files with their aspect ratios (within 0.1 %) and transparency; the TibiaWiki logo byte-identical; the logos distinct from the application logo; the page binds the list and has no other credit text; a missing logo falls back to the name.
* **UI checks** (published builds): the Credits list, logos, proportions, equal tile sizes and fit in the light theme, the dark theme and the smallest window; the CipSoft logo follows the theme.
* **Attribution**: all checks of 3.0 with the new identifier, plus files exported by Tibia Forge 3.0 (read, kept, history chunk replaced).
* **Migration**: settings of version 3.0 preferred over those of 2.x; both portable markers; both installed folders.
* **Reverse checks** T07–T10: the Tibia Forge 3.0 XMP fields not read, its history duplicated on re-export, its settings not migrated, and a credit address changed. Each must make the tests fail.

Results are in `docs/07_Testing_Report.md`, `05_verification/4.0.0/`, `05_verification/5.0.0/`, `05_verification/6.0.0/`, `05_verification/7.0.0/` and `05_verification/7.0.1/`.

## 14. Limitations and open points

* **No separate Tibia Foundry logo has been supplied.** The official logo supplied for Tibia Forge (it contains no text) is used. To use a new logo, replace `Assets/Branding/tibia-foundry-logo.png` and run `tools/branding/make_branding.py`; `BrandingTests` must then be given the new file's SHA-256.
* **Third-party logos**: Tibia and CipSoft logos belong to CipSoft GmbH, the TibiaWiki logo to TibiaWiki/Fandom. They are used to credit and link, with a non-endorsement notice. CipSoft asks to be notified after its press graphics are published (press@cipsoft.com); doing so is your decision.
* **Display scaling**: verified at 100 % only. The Tibia and CipSoft logos are stored at about twice their displayed size and stay sharp up to 200 %; the TibiaWiki logo is only available at 250×65 and is slightly enlarged above about 115 %.
* **License texts**: resolved in 5.0. Both editions include `THIRD-PARTY-NOTICES.txt` (§3).
* **Metadata can be removed.** Image editors, optimizers and some upload pipelines strip it. Tibia Foundry makes accidental loss unlikely; it does not make removal impossible.
* **The default creator `RockyLaRo` applies to every installation of this build** until the user sets a name. The first-start message and the Settings card point this out.
* **GIF XMP** is not read by ImageMagick or Pillow; readers that implement the XMP specification (MediaWiki, ExifTool, Adobe) read it, and the GIF comment always carries the sentence.
* **JSON, tab-separated data, clipboard text and verbatim client files** carry no attribution (§5).
