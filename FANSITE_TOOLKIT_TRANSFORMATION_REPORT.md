# Tibia Foundry – Fansite Toolkit Transformation Report

Date: 2026-10-30 · Source: `TibiaFoundry-8.6.0-source` · Released as **8.7.0** (2026-10-31); the changes are listed under *8.7.0* in `CHANGELOG.md`.

**Result in one line:** every tool, editor, converter, importer and exporter was kept. The program, its documentation and the website now present Tibia Foundry as an independent toolkit for Tibia fansite administrators, wiki editors, artists and content creators. A Home dashboard, fansite export presets, pixel-exact GIF enlargement and organized output folders were added, and the navigation is grouped by task.

## 1. Terminology and descriptions changed

| Before | After |
|---|---|
| Navigation groups *Client data / Tools / Application* | *Start, Graphics & Sprites, Game Data, Maps & Houses, Sound & Media, Asset Management, Client Library, Utilities, Application* |
| "Tools → Scripts", "Tools → Client Installation Rebuilder" (dialogs, settings hints, comments) | "Utilities → Scripts", "Client Library → Client Installation Rebuilder" |
| Open Tibia `items.xml` / `items.otb` | third-party `items.xml` item list / `items.otb` item ID mapping (unverified research leads) |
| "OT server ID", "server IDs" (Item Catalog) | "items.otb ID(s)" |
| Import prompt "Server data: which server is … from? e.g. Canary 15.25 / The Forgotten Server 13.10" (with file-name guessing of server names) | "Third-party item data: where is … from? e.g. Community item list 15.25" (no server-name guessing) |
| Source trust label "Open Tibia" | "third-party" |
| "OTClient extension attributes", "OTClient-based clients only" | "extended attributes", "third-party legacy-format tools only" |
| "Only for custom clients that check other signatures" | "Only for legacy-format tools that expect a different signature" |
| Project auditor: "OT server item file", "OT map" | "Third-party item ID mapping", "Third-party map file" |
| Every tool description in the tool registry | rewritten around wiki, fansite, gallery, guide and reference work (§4) |
| About page: "turns them into material for TibiaWiki" | the requested identity text; audience; trademark and independence disclaimer; "not part of CipSoft's Fansite Programme" |
| Assembly description | "independent desktop toolkit for Tibia fansite administrators, wiki editors, artists and content creators…" |
| "independent fan project" (About, README, website) | "independent community project", plus "Tibia is a trademark of CipSoft GmbH" |
| Compare Files hint "between client versions or servers" | "between client versions or other item sources (such as TibiaWiki)" |

## 2. Open Tibia developer references removed

| Location | Reference | Action |
|---|---|---|
| `website/site/index.html` (+ `build_site.py`, which regenerates it) | "just as useful for … **Open Tibia developers**", "for Open Tibia projects", "Open Tibia items.xml/items.otb" | rewritten for wiki editors, fansite administrators, artists, guide writers and the Fansite Programme |
| Item Catalog view-model, page and Core (`ItemModel`, `ItemImporters`, `OtbFile`, `CatalogComparison`, `ItemExports`, `ItemCatalog`) | Open Tibia, Canary, The Forgotten Server, TFS, OT server ID | rewritten (§1) |
| Client Converter page, view-model, `ClientConverter`, `LegacyAttributes`, `LegacyThing`, manifest settings | OTClient | rewritten (§1) |
| `TibiaProjectAuditorScript` | OT server item file, OT map | rewritten |
| Assets Editor manifest limitation | "OTB/OBD … itemFlag.otml" | "third-party item and object formats" |
| Tests | class `OpenTibiaAndComparisonTests`, `RealOpenTibiaData…`, env vars `TF_TFS_DIR`/`TF_CANARY_DIR`, fixture names (forgottenserver, tfs-*, canary) | renamed: `ThirdPartyItemDataAndComparisonTests`, `RealThirdPartyItemData…`, `TF_OTB_ITEMS_DIR`/`TF_CLIENTID_ITEMS_DIR`, neutral fixture names |
| `README.md`, `CHANGELOG.md`, `V8.5.0_…`, `V8.6.0_…AUDIT.md` | Open Tibia data, OTClient, Canary, TFS | rewritten |
| `docs/05, 06, 07, 08, 11, 14, 17` | same | rewritten; two "not implemented, OTServ-specific" rows removed from `docs/14` |
| `tools/release/README-Portable.txt`, `website/TibiaFoundry_Website_Content.md` | same | rewritten |
| `TibiaFoundry-Technical-Documentation/inventories/*.csv` | class/test names and summaries | updated to the new names |

Final search (§12 of the brief): no remaining occurrence of Open Tibia, OpenTibia, OTClient, OTServ, OTS, OT server/map/developer, Canary, Forgotten Server, TFS, private server, alternative server, server development or server compatibility in C#, XAML, Markdown, CSV, text, Python or HTML — with the exceptions listed in §11.

## 3–4. Every tool reviewed, and how it was adapted

| Tool | Status | Fansite adaptation |
|---|---|---|
| **Home** | **New** | Purpose, every tool by category, recent tools, recent exports, client library, workflows, links to website / TibiaWiki / official fansite list |
| Appearances (+ Outfitter, NPC tools, Outfiter sheet) | **Improved** | Fansite export presets, 1×–8× nearest-neighbor enlargement, per-kind output folders, description for articles and galleries |
| Extract Sprites | Adapted | Galleries, wiki images, artwork references (transparent sprites) |
| Advanced Tools | Adapted | Sprite sheets for galleries; official test-server asset previews |
| Static Data | Adapted | Bestiary, boss, achievement, quest and house databases; creature GIFs |
| Item Catalog | Adapted | Item databases and wiki item-ID checks; third-party item files kept as unverified research leads |
| Compare Files | Adapted | Update articles and change logs between official client versions |
| Map Tools | Adapted | Guide maps, location articles |
| House Images | Adapted | House articles and fansite house guides |
| Sounds | Adapted | Media pages and sound references |
| File Metadata | Adapted | Creator/copyright on everything published (moved to Sound & Media) |
| Assets Editor | Adapted | Inspecting and preparing official assets, exporting images |
| RCC Editor | Adapted | Interface artwork, icons and backgrounds as references |
| Client Converter | Adapted | Historical comparisons, legacy-format viewers, identifying old client files; info banner on the page |
| Clients | Adapted | "Official client library" for historical reference (moved to Client Library) |
| Client Installation Rebuilder | Adapted | Browsable copies of archived official clients |
| Scripts (12 utilities, incl. Tibia Project File Auditor) | Adapted | Batch renaming, nearest-neighbor resizing of galleries, export-library audits |
| Settings | Retained | Group hint texts updated |
| Compatibility | Retained | — |
| Diagnostics | Retained | — |
| About | **Improved** | New identity, audience, disclaimers |

No capability was removed or restricted. The `items.xml`/`items.otb` readers, the OTB format parser, the legacy `Tibia.dat`/`Tibia.spr` writer and its extended-attribute option all remain functional.

## 5. Functionality genuinely removed

None. Only documentation rows describing *not implemented* server-specific features (an OTB editor and an `itemFlag.otml` sidecar) were dropped from `docs/14_Tools_Settings_and_Scripts.md`.

## 6. New or improved fansite workflows

* **Export presets** (Appearances → Export): TibiaWiki image (1×), Creature gallery (2×, moving, sorted), Outfit gallery (2×, idle, sorted), Item icon (1×, idle), Article illustration (3×), High-resolution reference (4×, maximum delays). A preset fills in options only; any manual change switches the selector to *Custom* and keeps the change; the choice is remembered.
* **Pixel-exact enlargement**: `GifExportOptions.Scale` (1–8) applied with the existing `ImageResampler` nearest-neighbor filter; transparency and colors preserved; enlarged files end in `_Nx`. Tested pixel by pixel.
* **Organized output**: *Sort into Objects, Outfits, Effects and Missiles folders* for automatically named GIFs.
* **Recent exports**: every "Saved to …" (all tools) is recorded and listed on Home; click reveals it in File Explorer.
* **Workflow guide** on Home and in `docs/23_Fansite_Toolkit.md` (wiki images, galleries, maps and houses, update articles, item databases, batch processing and crediting).
* Existing custom export options are all still there.

## 7. Navigation and UI

* Navigation regrouped by task (§1); page order follows the groups. All 20 original pages kept; Home added (21 pages).
* New installations start on Home; existing users keep their last page or chosen start page (`LastPage`, `StartupPageName` unchanged).
* Home is built from the registered pages, so new tools appear automatically in the right section.
* MVVM, dependency registration through `BuiltInTools.Pages`/manifests, services, logging, async operations and cancellation are unchanged; Home uses the existing `PageViewModel` base and commands.

## 8. Documentation

* New: `docs/23_Fansite_Toolkit.md` (audience, navigation, Home, presets, every tool's fansite use, workflows, third-party data policy, data compatibility); `FANSITE_TOOLKIT_TRANSFORMATION_REPORT.md` (this file).
* Rewritten: `README.md` (identity, audience, toolkit table, new features), About page, `CHANGELOG.md` (Unreleased entry and historical wording), `docs/05, 06, 07, 08, 11, 14, 16, 17, 20, 22`, `V8.5.0_…`, `V8.6.0_…`, `README-Portable.txt`, website page and content file.
* Technical format documentation (OTB node format, items.xml rules, legacy layouts) was kept, reworded without server positioning.

## 9–10. Build and test results

All run on this machine on 2026-10-30, after the final changes.

| Check | Before (unmodified source) | After |
|---|---|---|
| `dotnet build TibiaFoundry.slnx` | 0 warnings, 0 errors | 0 warnings, 0 errors |
| `dotnet test` | 595 tests: 562 passed, 32 skipped, **1 failed** | 603 tests: 570 passed, 32 skipped, **1 failed** (the same pre-existing one, §11) |
| New tests (`FansiteWorkflowTests`, 8) | — | all pass |
| `TibiaFoundry.exe --self-test` | — | 13 PASS, 0 failures (exit 0) |
| `TibiaFoundry.exe --ui-snapshot --theme dark` (starts the real app, visits all 21 pages, dialogs, theme switching) | — | 43 PASS, 0 binding errors (exit 0); Home and every regrouped page render |

Tests changed because behavior changed on purpose: `UiFlowTests.ToolsCompareButton…` (a new installation now starts on Home; it also checks the recent-pages list), the renamed third-party item data tests (new prompt answers and labels), one comment in `ItemCatalogTests`.

One timing test (`ItemCatalogTests.LargeCatalogsLoadAndFilterQuickly`) failed once while the machine was busy with a large file comparison; rerun alone three times it passed each time (rename 13–14 ms against a 200 ms limit) and it passed in the final full run.

Not verified by hand: clicking through the Export tab's preset selector in the running app (covered by the view-model test and by compiling the XAML; the UI snapshot does not open that tab), and the published website (not deployed).

## 11. Remaining concerns

* **Internal identifiers kept for compatibility**: the enum values `SourceKind.OtItemsXml`, `SourceKind.OtItemsOtb`, `IdType.OtbServer` and the record property `OtbItem.ServerId` are stored in users' catalog files; renaming them would break existing catalogs. They are not shown in the interface.
* **`website/live-8.5.0-snapshot/`** is the byte-identical copy of the page published before 8.6 and still says "Open Tibia developers". It is the input `build_site.py` transforms (so the old strings also appear inside `build_site.py` as text to be replaced). It is not deployed; delete or archive it if you prefer not to keep it.
* **The live website** (tibiafoundry.com) is not updated by these changes: publish `website/site/` to the GitHub Pages repository.
* **Website and released version**: the website describes the released 8.6.0 program, so it was repositioned but does not yet advertise the Home dashboard and presets. These changes are released as 8.7.0; update the website's download links when the 8.7.0 files are uploaded.
* **Screenshots** in `docs/screenshots/8.6.0/` and the website show the old navigation groups; regenerate them with `TibiaFoundry.exe --ui-snapshot <folder>` after release.
* A pre-existing test (`ItemCatalogTests.TheClientsOwnNamesAreVerifiedAndEveryObjectIsListed`) fails on this machine because the installed Tibia client now has 43,518 objects instead of the 43,516 the test pins; it also fails on the unmodified source.
