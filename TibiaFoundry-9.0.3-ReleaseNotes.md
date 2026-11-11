# Tibia Foundry 9.0.3 – Release Notes

Released 2026-11-10 · Windows 10/11 x64 · installer (MSI), portable edition and source. A fix release for 9.0; everything in 9.0, 9.0.1 and 9.0.2 is included ([9.0.2 release notes](TibiaFoundry-9.0.2-ReleaseNotes.md), [9.0.1 release notes](TibiaFoundry-9.0.1-ReleaseNotes.md), [9.0 release notes](TibiaFoundry-9.0-ReleaseNotes.md)).

## Fixed

* **Outfitter: a damaged or locked sprite sheet could close the program.** Reading errors of the sprite sheet were not handled by the Outfitter preview and reached the global error handler once per change. Dragging a colour slider or switching options quickly produced more than eight errors in five seconds, which the handler treats as a repeating error and shuts the program down. The preview now empties, shows the message and writes it to the log – the same handling the Appearances preview received in 9.0 (audit F-09).
* **Content Studio: the queue history no longer grows without limit.** `queue-history.jsonl` (in the data folder) was only ever appended to, and the Content Studio read the whole file on the UI thread every time a job started or finished, so the page became slower over time. The file now keeps the latest 500 entries once it has grown to about four times that; the page shows the latest 100 as before. The shortened file is written safely (temporary file, flushed, then replaced).

## Version

* **9.0.3**, tag `v9.0.3`, packages `TibiaFoundry-9.0.3-Installer.msi`, `-Portable.zip`, `-Source.zip`. Windows' file version is 9.0.3.0.
* The MSI upgrades 9.0.2, 9.0.1, 9.0 and 8.x in place; settings and data are used as they are. An existing long queue history is shortened the next time a job finishes.
* Not code-signed (Windows SmartScreen may ask once).
