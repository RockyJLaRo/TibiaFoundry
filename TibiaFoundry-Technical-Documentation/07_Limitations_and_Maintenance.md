# 07 Limitations and Maintenance

## Known limitations (as declared in the tool manifests and docs)

- **Assets Editor.** Does not edit legacy dat/spr, OTB/OBD, large sprite sheets, Lua or `itemFlag.otml`.
- **Client Converter.** Writes the Client 11 and 10.98 layouts. 8.60 files can be identified but not written.
- **RCC Editor.** Zstandard entries are kept but not decompressed.
- **Rebuilder.** "Content-complete" means that every recorded file was restored. Whether the client starts from the rebuilt folder is not checked.
- **Item Catalog.** TibiaWiki's item list gives article titles, which can differ from in-game names.
- **Client store.** Local only; nothing is uploaded or synchronized.
- **Platform.** Windows 10/11 x64 only (WPF).

## Technical debt (general)

- **UI-thread I/O on large archives.** The Clients and Rebuilder pages do synchronous I/O on the UI thread, and the cost grows with the size of the archive (F-03).
- **Outputs outside File safety.** Some batch exports bypass `OutputSession` (F-02).
- **Small inconsistencies.** These have low impact:
  - `ClientStorageMode` is a string, not an enum;
  - two types are named `CatalogEntry`;
  - the `app.manifest` version is stale, and the publish path is hard-coded (F-07);
  - several existing doc titles name old versions (F-16).
- **Largest files.** `AssetsEditorViewModel` (921 lines) and `ClientsViewModel` (895 lines) are the largest view-models. `UiSnapshot.RunChecksAsync` is the most complex method (107 branches).

## Where changes have impact

| Change | Check |
|---|---|
| Add a page | `BuiltInTools` manifest and registration, `PageTemplates`, a view and a view-model. See `docs/16_Adding_a_Tool.md`. |
| New setting | `AppSettings` (+ `Normalize`/`Migrate`; raise `CurrentVersion` when the format changes), the Settings page, migration tests |
| Client store or manifest format | `ClientStore`, the Rebuilder, the launch check, the store tests; keep reading format 2 |
| Readers for new client data | `Core/Data` readers and `.proto` files; the Compatibility checks; parity tests |
| Anything that writes files | Use `OutputSession` or `AtomicFile`; never write inside a client installation |

## Recommended order of fixes

1. F-02, because it is the only gap where existing files can be overwritten without being asked.
2. F-01.
3. F-03.
4. The rest, opportunistically.
