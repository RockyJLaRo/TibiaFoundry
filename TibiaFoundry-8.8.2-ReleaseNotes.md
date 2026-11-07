# Tibia Foundry 8.8.2 – Release Notes

Released 2026-11-02 · Windows 10/11 x64 · installer (MSI), portable edition and source.

8.8.2 adds batch GIF export to **Game Data → Static Data**. Everything of 8.8.1 is included and unchanged (see `TibiaFoundry-8.8.1-ReleaseNotes.md` for the performance release).

## New

* **Select all creatures and export all their GIFs at once.** On the Monsters and Bosses lists, **Select all** (or Ctrl+A in the list, or Shift/Ctrl-click) selects the creatures, and **Export GIF… (n)** saves the GIF of every selected creature into the GIF folder in one go. With one creature it asks for the file name, as before.
* **Export all monsters and bosses…** saves the GIF of every monster and every boss of the file into the `Monsters` and `Bosses` folders of the GIF folder. On the installed client 15.33.8791c6: 833 monsters and 447 bosses, 1,280 GIFs, in about 100 s, without failures.
* Batch exports run in the background with progress in the status bar and can be cancelled. Each GIF is the same animation as the preview (moving south, with the creature's colors and addons; creatures that look like an object use the object's animation). They carry the creator attribution, and the **Fixed frame delay** option applies to them. Existing files are never replaced: a taken name gets " 2", " 3"…, as in every GIF export. The status bar links to the folder, and the GIFs are added to the Asset Library like every export (when that setting is on).

## Changed

* **Creature names are capitalized like TibiaWiki articles** in the Static Data list, the preview title and the GIF file names: "orc" → **Orc**, "demon lord" → **Demon Lord**, "lord of the elements" → **Lord of the Elements**. The data panel still shows the client's own text.

## Compatibility

* Settings, item catalogs, client archives and Asset Libraries of 8.8.1 and earlier are used as they are. The MSI upgrades 8.8.1 and 8.8.0 in place; user data is never touched.
* New texts are translated into all five interface languages.
* Not code-signed.
