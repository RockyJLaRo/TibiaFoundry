# Tibia Foundry 9.0.2 – Release Notes

Released 2026-11-06 · Windows 10/11 x64 · installer (MSI), portable edition and source. A fix release for 9.0; everything in 9.0 and 9.0.1 is included ([9.0.1 release notes](TibiaFoundry-9.0.1-ReleaseNotes.md), [9.0 release notes](TibiaFoundry-9.0-ReleaseNotes.md)).

## Fixed

* **Wiki infobox: `flavortext` now ends with a full stop.** The client stores an object's look text without its closing full stop; the game adds it when you Look at the object, and TibiaWiki writes it (every one of 107 sampled item pages does). The exporter now adds the full stop unless the text already ends with `.`, `!` or `?`. Example, item 54631 on the installed client:

```
| flavortext    = Offers a soul to the Soulpit. Combine with another soul core to create a random new soul core.
```

  The value now matches the item's TibiaWiki article exactly. Objects without a look text still get no `flavortext` line.

## Version

* **9.0.2**, tag `v9.0.2`, packages `TibiaFoundry-9.0.2-Installer.msi`, `-Portable.zip`, `-Source.zip`. Windows' file version is 9.0.2.0.
* The MSI upgrades 9.0.1, 9.0 and 8.x in place; settings and data are used as they are.
* Not code-signed (Windows SmartScreen may ask once).
