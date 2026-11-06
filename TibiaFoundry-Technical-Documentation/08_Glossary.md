# 08 Glossary

| Term | Meaning |
|---|---|
| Appearances | The client's protobuf catalog of objects, outfits, effects and missiles (`appearances-*.dat`) |
| Assets folder | The client folder with `catalog-content.json`, the data files and the sprite sheets |
| Baseline | Recorded state of a monitored installation, used by the launch check |
| Client store | The content-addressed archive of client revisions (`Clients\Shared_Assets`) |
| Launch check | Start-up comparison of a monitored installation with its baseline (full or quick) |
| Lease | A marker that keeps a revision in use from being deleted |
| Live / test server | The two client packages: `Tibia` and the test-server package |
| Manifest (format 2) | `manifest.json` of an archived revision: its files, hashes, sizes and exclusions |
| Operation | Long work run through `RunAsync`, with progress, cancellation and history |
| OutputSession | Writes output files according to File safety (ask, replace, keep both or skip; backups) |
| Revision | One archived state of a client folder |
| RCC | Qt resource container (`.rcc`), such as `graphics_resources.rcc` |
| Shared / independent storage | Each content stored once across revisions, or a full copy per revision |
| Sprite sheet | CIP LZMA-compressed BMP that holds many sprites (`*.bmp.lzma`) |
| Tool manifest | Declaration of a page: id, type, operations, inputs and outputs, dependencies, license |
