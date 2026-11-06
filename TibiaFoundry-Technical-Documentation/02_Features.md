# 02 Features

The pages appear in navigation order, as registered in `src/TibiaFoundry.App/Services/BuiltInTools.cs`. Each page has a view-model in `ViewModels/` and a view in `Views/Pages/`. *(Verified)*

| Page | Type | Reads | Writes | External dependency |
|---|---|---|---|---|
| Appearances | Viewer | `appearances-*.dat`, sprite sheets | GIF, PNG, JSON, ID tables, Lua | ImageMagick (GIF); TibiaWiki (NPC trades) |
| Static Data | Viewer | `staticdata-*.dat` | JSON, GIF | ImageMagick (GIF) |
| Item Catalog | Viewer | appearances, name lists, CSV, JSON | catalog, TXT/CSV/JSON | TibiaWiki (optional import) |
| Map Tools | Exporter | `map-*.dat`, `staticmapdata-*.dat`, tiles | PNG, JSON | none |
| House Images | Exporter | `staticmapdata-*.dat` | PNG, JSON | none |
| Sounds | Viewer | `sounds-*.dat`, `.ogg` | `.ogg`, JSON | NAudio (bundled) |
| Extract Sprites | Exporter | sprite sheets | PNG and a provenance manifest | Client required |
| Compare Files | Validator | two client data files | text report | none |
| Advanced Tools | Utility | images, `.lzma`, file lists | PNG, decompressed files | Test-server site (download) |
| Assets Editor | Editor | assets folder, `.aec`, images | validated copy of an assets folder, `.aec`, PNG | none |
| RCC Editor | Editor | `.rcc` | `.rcc`, extracted resources | none |
| Client Converter | Converter | assets folder | `Tibia.dat`, `Tibia.spr`, slices | none |
| Client Installation Rebuilder | Utility | archived revision | rebuilt folder, report | none |
| Scripts | Script | files and folders | reports, renamed or copied files (with undo) | none |
| Settings | Application | none | settings | none |
| Diagnostics | Application | logs, operations | diagnostics report `.zip` | none |
| Clients | Application | installed clients, folders | client archive | none |
| Compatibility | Validator | installed client | text report (clipboard) | none |
| About | Application | none | none | none |

## Notes

- **Removed feature.** Item Discovery was removed in 8.0. The remaining traces are intentional:
  - legacy enum values;
  - settings migration (format 7 drops its keys);
  - the clean-up of leftover hotkey backups;
  - `RemovedFeatureTests`.
- **Client choice.** Every page reads the client chosen on the Clients page, which can be the installed client, a registered folder or an archived revision. Switching is refused while an operation is reading the current client (`AppServices.WhyNotSwitchClient`).
- **Scripts.** Only built-in scripts can run. There is no way to run arbitrary code.
- **Commands without a control.** Six commands exist in view-models but no control binds them (see [06 Findings](06_Findings.md), F-08).
