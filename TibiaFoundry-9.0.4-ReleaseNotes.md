# Tibia Foundry 9.0.4 – Release Notes

Released 2026-11-11 · Windows 10/11 x64 · installer (MSI), portable edition and source. A fix release for 9.0; everything in 9.0 to 9.0.3 is included ([9.0.3 release notes](TibiaFoundry-9.0.3-ReleaseNotes.md), [9.0.2](TibiaFoundry-9.0.2-ReleaseNotes.md), [9.0.1](TibiaFoundry-9.0.1-ReleaseNotes.md), [9.0](TibiaFoundry-9.0-ReleaseNotes.md)). 9.0.3 was prepared but never published on GitHub, so its two fixes (Outfitter stability, bounded queue history) reach most users with this release. The audit behind this release is in [TibiaFoundry-9.0.4-Audit.md](TibiaFoundry-9.0.4-Audit.md).

## Fixed

* **Item Catalog: item properties were saved in the interface language.** Since 9.0 four properties of an item (*liquid container*, *npc trade*, *magic field*, *trash holder*) were written into a catalog in the language the program was showing. A catalog imported with a German, Spanish, Polish or Portuguese interface therefore held "Magiefeld" where an English one held "magic field": importing the same client again in another language reported changed records, CSV and JSON exports mixed languages, and Compare did not mark doors, fields and floor changes of such catalogs as behaviour-sensitive. The names are now the same in every language. **Existing catalogs need nothing:** translated names are read as their language-independent names; nothing is rewritten until you next import a source.
* **Asset Library: exported folders were only categorized in English.** The folders Tibia Foundry writes itself (`Sprite_Sheets`, `House_Images`, `Map_Sections`, `Map_Exports`) were matched against translated text, so outside English their files were filed under *Other* or a guessed category when added to the library.
* **Folders directly on a drive were refused.** Extracting, restoring or exporting straight into a drive root such as `E:\` failed with "would be written outside E:\", and a library folder on a drive root never counted its own files as managed. Sub-folders were not affected.
* **Content Studio queue: one failing window could stop every waiting job.** If a listener of the queue threw while a job finished (a page that was closing, a history file that could not be written), the next job was never started and stayed "Pending". Every listener is now called on its own and the next job always starts. A failed job's state text is translated.
* **Start-up with unwritable settings folder.** When the settings file was damaged or older and its backup copy could not be written (read-only share, Controlled Folder Access), the program ended before its window opened. It now starts and says the backup failed.
* **Asset Library restore:** if the index of a backup could not be put in place, the index that was just set aside is put back, so a failed restore leaves the library as it was.
* **Thumbnail cache:** its size and "Clear" no longer fail when a thumbnail disappears or is locked meanwhile.

## Faster

* **"Select all" and workflow steps over a large library** read only the asset IDs. On a library of extracted client sprites this takes 3 ms instead of 283 ms per call, and allocates 0.3 MB instead of 21 MB (measured, see the audit).

## Security

* The default address of *Advanced Tools → Download test-server assets* is `https://` (the server redirected `http://` there already).
* No vulnerable NuGet packages (checked against nuget.org on the release day).

## Housekeeping

* 65 unused `using` directives and three unused members removed (found with the compiler's analyzers); two documentation references corrected. No behaviour changes.
* New: `tools/release/make_release_directory.py` builds the release folder with all five packages (installer, portable, source, website source, website assets), tests every archive and writes the checksums.

## Known limitation

* Some labels that are read once when the program starts (metadata field names, output folder names, legacy format names) keep the start language after switching the language in a running program; they follow after a restart. Not new in 9.0.4.

## Version

* **9.0.4**, tag `v9.0.4`, packages `TibiaFoundry-9.0.4-Installer.msi`, `-Portable.zip`, `-Source.zip`, `-Website-Source.zip`, `-Website-Assets.zip`. Windows' file version is 9.0.4.0.
* The MSI upgrades 9.0.x and 8.x in place; settings, catalogs and the Asset Library are used as they are. No data format changed.
* Not code-signed (Windows SmartScreen may ask once).
