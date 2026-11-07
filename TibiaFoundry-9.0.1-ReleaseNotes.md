# Tibia Foundry 9.0.1 – Release Notes

Released 2026-11-06 · Windows 10/11 x64 · installer (MSI), portable edition and source. A fix release for 9.0; everything of 9.0 is included ([9.0 release notes](TibiaFoundry-9.0-ReleaseNotes.md)).

## Fixed

* **Wiki infobox: `flavortext`.** The object's look text in the client (`Appearance.Description`) is now written as TibiaWiki's `flavortext`, in the template's order (after the classification, before `implemented`). Before, it was left out even when the client had the text. Example, item 54631 on the installed client:

```
| name          = Moonstone Excavator Soul Core
| actualname    = moonstone excavator soul core
| itemid        = 54631
| objectclass   =
| primarytype   =
| flavortext    = Offers a soul to the Soulpit. Combine with another soul core to create a random new soul core
| implemented   =
```

  Objects without a look text get no `flavortext` line. Like every value, it can be edited before copying.

## Version

* Patch releases have three parts: **9.0.1**, tag `v9.0.1`, packages `TibiaFoundry-9.0.1-Installer.msi`, `-Portable.zip`, `-Source.zip`. Windows' file version is 9.0.1.0.
* The MSI upgrades 9.0 and 8.x in place; settings and data are used as they are.
* Not code-signed (Windows SmartScreen may ask once).
