# Tibia Foundry 8.8.1 – Release Notes

Released 2026-11-02 · Windows 10/11 x64 · installer (MSI), portable edition and source.

8.8.1 is a performance release. Every tool, setting, file format and output of 8.8.0 is unchanged; settings, client archives, item catalogs and Asset Libraries of 8.8.0 are used as they are. It also ships the interface-language work (English, Brazilian Portuguese, Polish, Spanish, German) that followed 8.8.0.

All numbers were measured on the same PC with the installed client 15.33.8791c6, 8.8.0 vs 8.8.1 (details: `TibiaFoundry_Performance_Audit.md`).

## Performance improvements

### Application start-up
* The background **compatibility check** after start-up and after a client update takes **0.8–0.9 s instead of 4.0–4.6 s**. It checks the sprite sheets, map images and sounds against one listing of their folder instead of asking Windows about each of ~10,000 files.
* Time until the main window appears is unchanged (about 1.5 s); nothing in start-up was blocking it.

### First-run setup and client management
* **Find installation** (setup, Settings → Tibia client) takes **4.6 s instead of 29 s** on a PC with many screenshots, and allocates 98 MB instead of 2.7 GB. The search for Qt resource files read 64 KB of every file to look at 4 bytes, and the setup only needs to know whether a folder has one.

### Client data and asset processing
* **Loading appearances** allocates **79 MB instead of 1.85 GB** and is about twice as fast. The client's two Latin-1 item names made every load take the repair path, which copied every nested record at every level; the repaired data is byte-for-byte the same as before.
* **Compare Files** on appearances: about twice as fast, 257 MB instead of 3.8 GB.
* **Sprite sheets decode about 30 % faster** (the client's bitmap layout is copied row by row): faster thumbnails, outfit images (−34 %), GIFs (−28 %), sprite extraction and the Assets Editor.

### Exports
* **PNG and GIF exports of many files are about 30 % faster** (extracting 300 sheets to 12,780 PNG files: 32.8 s → 22.6 s). Every file is still written to a temporary file, verified and then moved into place; the verification now reads through the handle that wrote the file, so Windows no longer scans it a second time first.

### Asset Library
* **Rescanning an unchanged folder: 66 ms instead of 3 s** for 12,780 files.
* **First indexing about twice as fast** (15.1 s → 7.2 s for 12,780 files): files are read and inspected in parallel; the library is still written in file order.
* **Searches** evaluate their filter once instead of twice (about 25–50 % faster).

### Memory
* Fewer and smaller allocations in every loading and checking path above (up to −96 %), which means far fewer garbage collections while the program works.

## Other improvements
* `tools/benchmarks`: the workload benchmarks used for this release, with an equivalence check that compares results of two versions byte for byte.
* `tools/release/assemble_release.py`: puts the installer, portable edition and source with checksums into `Release\<version>\`.
* 8 new automated tests for the optimized code paths.
* The source package no longer includes a top-level `Release` folder (finished packages).

## Bug fixes
No functional bugs were fixed; the changes above are performance fixes of existing behavior.

## Compatibility
* Upgrading: the MSI upgrades 8.8.0 in place (same upgrade code; one entry in "Apps & features"). Settings, logs, the Asset Library, client archives and exports are never touched by installing, upgrading or uninstalling.
* The portable edition keeps its data in `TibiaFoundry-Data` next to `TibiaFoundry.exe`, as before.
* Known: `ItemCatalogTests.TheClientsOwnNamesAreVerifiedAndEveryObjectIsListed` expects the object count of client 15.33.9d148f and fails against newer clients (also in 8.8.0); this affects only the test suite.
* The installer and executable are not code-signed.
