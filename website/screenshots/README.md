# Website screenshots: automatic generation

Every screenshot on tibiafoundry.com is a real capture of the running program, in **every language of the
website** and in **both themes**, made by one command:

```bash
python -I website/screenshots/screenshots.py capture
```

That builds the program from `src/`, copies it into the portable demo copy, starts it once per language with
`--site-screenshots`, checks and compares every capture, stores it in the library, rewrites the inventory and the
coverage report, builds the website and validates every image reference. Run it from the repository root.

| File | What it is |
|---|---|
| `screenshots.py` | The tool (commands below). |
| `config.json` | Demo copy location, frame size, check thresholds, what the fingerprints hash. |
| `manifest.json` | One record per screenshot: file hash, fingerprint, checks, comparison with the previous capture, visual review. Written by the tool; commit it. |
| `inventory.md` | Every screenshot location, discovered from the project. Generated. |
| `coverage-report.md` | Every screenshot × language × theme with capture, state, checks, review and file. Generated. |
| `qa-9.0.4.md` | What the 9.0.4 recreation checked, corrected, and found in the program itself (written by hand). |
| `.work/` | Raw captures, diff images, contact sheets. Not committed (`.gitignore`), not packaged. |
| `../src/images/screens/<code>/<id>-<code>-<theme>.png` | **The library**: lossless 1280×800 window captures, e.g. `screens/pt-BR/home-pt-BR-dark.png`. |
| `../site/img/screens/<id>-<code>-<theme>.webp` | What the website serves (written by `build_site.py`). |

## Requirements

* Windows 10/11 with a desktop session (the program draws its window; it may be covered by other windows).
* .NET SDK (see `src/global.json`), Python 3.10+ with Pillow and NumPy (`pip install pillow numpy`).
* The **portable demo copy** at `C:\Users\Public\Documents\Tibia Foundry Demo\App` (`config.json → app.runtime_dir`):
  a portable Tibia Foundry with its `TibiaFoundry-Data` folder (settings, Asset Library, Content Studio projects).
  The tool replaces only the program files. Before every language run it restores the data folder from
  `config.json → app.baseline_data` (`…\Tibia Foundry Demo\Baseline-Data`; made from the data folder on the first
  run if missing), so every picture starts from the same settings, library and projects – the program saves things
  like "recently used tools" while it captures. Using a folder under `C:\Users\Public` keeps the
  Windows user name out of the pictures; text that still contains a path in the user's folder (the installed
  client) is blurred by the program itself.
* The official Tibia client installed (live). **Compare Files** needs an older appearances file: the tool takes
  the newest `appearances-*.dat*` in the client's assets folder that is not the current one, or the file named by
  the environment variable `TF_SITE_COMPARE_OLD` (or `config.json → compare_old_file`).

## Commands

```bash
# Everything: all locations × all languages × both themes, then build and validate the site
python -I website/screenshots/screenshots.py capture

# Only what is missing or outdated (program, translation, theme or client changed since its capture)
python -I website/screenshots/screenshots.py capture --incremental

# One page, one language, one theme – or any combination (comma-separated)
python -I website/screenshots/screenshots.py capture --pages home
python -I website/screenshots/screenshots.py capture --languages pl,de
python -I website/screenshots/screenshots.py capture --themes light
python -I website/screenshots/screenshots.py capture --pages infobox,compare --languages es --themes dark

# Options: --no-build (use the program already in the demo copy), --no-site (skip the website build),
#          --dry-run (list what would be captured and why)

python -I website/screenshots/screenshots.py status      # missing / outdated / unreviewed, with the reason; exit 1 if not complete
python -I website/screenshots/screenshots.py validate    # files, checks, obsolete files, the built site's references
python -I website/screenshots/screenshots.py sheets      # .work/sheets/<id>.png: every language × theme of a location side by side
python -I website/screenshots/screenshots.py approve --pages home --languages de --note "checked"   # record a visual review
python -I website/screenshots/screenshots.py report      # rewrite inventory.md and coverage-report.md
```

A full run takes about 2–3 minutes per language. The program's window appears while it captures; leave the
mouse and keyboard alone for that time.

## What the tool finds by itself

* **Languages**: `LANGUAGES` in `website/build_site.py`. For each one the website needs
  `src/locales/<code>.json` and the program needs `Strings.<code>.xaml`; a missing one is reported before anything
  is captured. Languages the program has but the website does not are listed as a note.
* **Locations**: the screenshot gallery (`SCREENS` in `build_site.py`), every `{{IMG:<id>-dark}}` /
  `{{SHOT:<id>:...}}` and `{{IMG:<id>-dark}}` in `src/templates/*.html` (with page and section), the crops in `DERIVED` (traced back to their
  source shot) and the link preview image.
* **Shots the program can take**: the `Shot` list in `src/TibiaFoundry.App/Views/SiteScreenshots.cs`. A location
  without a shot is reported as a problem.

## Adding things

* **A new screenshot**: add a `Shot` to `SiteScreenshots.cs` (name, page view model, how to put the page into the
  state to show), then use it on the website: a gallery tab (`SCREENS` in `build_site.py`, plus
  `screens.caption.<id>` in every locale) or `{{IMG:<id>-dark}}` / `{{IMG:<id>-light}}` in the template. Run
  `capture --incremental`: the new location is missing everywhere, so exactly its 2 × languages files are made.
* **A new language**: add it to `LANGUAGES` in `build_site.py` with its locale file (see `../TRANSLATING.md`); the
  program needs its `Strings.<code>.xaml` and an entry in `Languages.cs`. `capture --incremental` captures every
  location in that language.
* **Changed translations, design or pages**: `capture --incremental` (or a full `capture`). Fingerprints:
  *program* = every source file of `TibiaFoundry.App` and `TibiaFoundry.Core` except translations and theme colours;
  *language* = `Strings.en.xaml` + `Strings.<code>.xaml` (English is the fallback for missing texts);
  *theme* = `Colors.Dark.xaml` / `Colors.Light.xaml`; *client* = the installed client's `catalog-content.json`.
  A translation change therefore refreshes only that language, a colour change only that theme.

## Checks

Every capture is checked before it is stored (a failed check keeps the previous file and is reported):

* size exactly 1280×800 (the program sizes its window frame to that);
* the theme: average brightness of the page area and of the title bar must be dark (≤ 110) or light (≥ 150);
* not blank (brightness spread ≥ 12);
* the program's own result (every requested shot reported `ok`, the page existed, the language it started in).

**Comparison**: each new capture is compared pixel by pixel with the file it replaces. The share of changed pixels
is stored in `manifest.json` and shown in the coverage report; above 0.25 % a side-by-side diff
(`.work/diff/<file>.png`, changes in red) is written. A capture that changed although its fingerprint did not is
reported as an **unexpected change** (an unstable page state, an animation, a client update).

**Visual review** is recorded with `approve` and tied to the file's hash: a new capture of that screenshot is
"pending" again until someone has looked at it (`sheets` makes that quick).

**Website**: `validate` checks that every `<img>` on every built language page resolves, that each language page
shows only its own language's screenshots, that every dark picture has its light twin, that no image in `site/img`
is unused and that no old-style screenshot is left in `src/images`.

## When something fails

* The summary lists each failed screenshot with the reason; the program's own log is in
  `<demo copy>\TibiaFoundry-Data\logs`, its per-run results in `.work/capture/<code>/site-screenshots.txt` / `.json`.
* *timed out*: the program did not finish within `app.timeout_minutes`; it is closed. Often a dialog that should
  not appear in unattended runs, or a page that never stops loading.
* *does not look dark/light*: the theme was not applied before the capture; check `ThemeManager.Apply`.
* *size*: display scaling or a window that could not be resized (another monitor setup); the program fits the
  frame to 1280×800 physical pixels.
* A run that was stopped leaves `.work/lock`; delete it if no capture is running.
