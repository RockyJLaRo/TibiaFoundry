# Tibia Foundry 9.0.4 – Testing Report

2026-11-11 · Windows 11 Pro x64, 16 logical processors, .NET SDK 10.0.404, WiX 5.0.2, Python 3.13.5 with Pillow 11.3.0, Tibia client 15.33.124f6c installed. Every result below was observed on that machine; what was **not** tested is listed at the end.

## Build

| Step | Result |
|---|---|
| `dotnet restore` / `dotnet build TibiaFoundry.slnx -c Release` | 0 warnings, 0 errors |
| Folder edition: `dotnet publish … -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true` | 261 files |
| Portable edition: `dotnet publish … -p:PublishProfile=Portable` | one file, `TibiaFoundry.exe`, 90.3 MB |
| `python -I tools/release/package_release.py` | notices (14 sections), three ZIPs |
| `python -I tools/release/build_installer.py` | `TibiaFoundry-9.0.4-win-x64.msi` |
| `python -I website/build_site.py --strict` | five languages, 194 images, no missing translation |
| `python -I tools/release/make_release_directory.py` | five packages, every archive tested, checksums written |

## Automated tests

| Run | Passed | Skipped | Failed |
|---|---|---|---|
| 9.0.3 (baseline, before any change) | 684 | 32 | 0 |
| 9.0.4 | **694** | 32 | **0** |

The 32 skipped tests need files that are not on this machine (other client versions, files of the original 1.x program) and skip by design; no test was removed or disabled. Ten tests are new (`Release904Tests`, `LanguageIndependentDataTests`):

* files on a drive root count as inside it; traversal out of a drive root is still refused;
* a failing queue listener does not stop the queue; a failed job's text is translated in all five languages;
* `SearchIds` returns the same assets in the same order as `Search` for seven kinds of query;
* thumbnail cache size and clearing;
* item flags of the client and of `items.xml` are identical in all five interface languages;
* flags saved in German, Spanish, Polish and Portuguese by 9.0–9.0.3 are read as their neutral names, also from a catalog on disk;
* export folders are categorized identically in all five languages; protected folder names are not translated.

The new tests were written with the fixes and were run against 9.0.4 only; they were not executed against the 9.0.3 code. During the work one full run reported `SpriteDecodingTests.Extractor_WritesOriginalLayout` as failed while other builds were running on the machine; it passed alone and in the complete final run, and the code it exercises was not changed. It is recorded here as an unexplained one-off.

## Program

| Check | Result |
|---|---|
| `TibiaFoundry.exe --self-test <folder>` (folder edition) | exit 0, "Failures: 0"; client data 15.33.124f6c compatible |
| Version metadata of `TibiaFoundry.exe` | file version 9.0.4.0, product version 9.0.4, product "Tibia Foundry" |
| Portable edition started from a fresh folder, six times (three per version, alternated) | main window after 1.45–1.56 s; log says "Tibia Foundry 9.0.4 starting" and "Portable mode: data in …\TibiaFoundry-Data" next to the program; closed normally through its window |
| Portable data | settings and log were written to `TibiaFoundry-Data` beside the executable; nothing was written to `%LOCALAPPDATA%\TibiaFoundry` |

## Installer

| Check | Result |
|---|---|
| MSI properties | ProductName "Tibia Foundry", ProductVersion 9.0.4, UpgradeCode `{6F0C2D5E-8A1B-4E57-9B3C-71D4A2E9F810}` (unchanged, so it upgrades earlier versions), per machine |
| Administrative extraction (`msiexec /a … /qn TARGETDIR=…`) | exit 0; 261 files under `PFiles64\Tibia Foundry`, the same number as the published folder |
| Extracted `TibiaFoundry.exe` | product version 9.0.4; `--self-test` exit 0 |

## Website (built site served with `python -m http.server`, built-in browser)

| Check | Result |
|---|---|
| English, German and Polish pages | load; header and hero say 9.0.4; the 9.0.4 entry is in "What's new" in each language |
| Download buttons | `…/releases/download/v9.0.4/TibiaFoundry-9.0.4-Installer.msi`, `-Portable.zip`, `-Source.zip` |
| Images | none broken (48 image elements on a page; the German page loads its 42 German screenshots) |
| In-page links | every `#…` link has its target |
| Theme toggle | dark → light at once (background `rgb(15,18,22)` → `rgb(246,247,250)`) |
| 375 px wide | no horizontal scrolling; menu button shown |
| Console | no errors |

## Not tested

* **Installing and uninstalling the MSI.** The installer is per machine and needs administrator approval, which an unattended run cannot give; it was extracted and inspected instead. Shortcuts, the "Apps & features" entry, upgrade over an installed 9.0.x and uninstallation were therefore not observed for 9.0.4 (the installer definition is unchanged from 9.0.3).
* A clean Windows installation or another PC.
* The interactive pages by hand: the fixes are covered by automated tests, the program by its self-test and start-up; no manual click-through of every tool was done.
* The screenshots on the website were not taken again: no page of the program looks different in 9.0.4.
* Low disk space, network interruptions and unexpected shutdown were not provoked beyond what the existing tests simulate.
