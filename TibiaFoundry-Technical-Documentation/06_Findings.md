# 06 Findings

Findings of the 2026-10-21 audit. Severity is the practical impact for a single local user. The same list is in [audit/findings-register.csv](audit/findings-register.csv).

| ID | Area | Severity | Status | Finding | Location |
|---|---|---|---|---|---|
| F-01 | Security | Medium | Inferred | Client-folder write protection knows only the default packages folder plus store and registered folders; a custom install location (Steam, Program Files, custom packages or assets override) is not recognized. | `AssetsProject.IsInsideClientInstallation; RebuilderViewModel.ForbiddenRoots` |
| F-02 | Reliability | Medium | Verified | Sprite extraction (unless Skip existing), sound extraction and map tile extraction write without OutputSession, so existing files are overwritten without asking and without backup. | `SpriteExtractorViewModel; SoundsViewModel; MapToolsViewModel` |
| F-03 | Performance | Low/Medium | Inferred | Synchronous I/O on the UI thread: Clients page refresh reads every manifest twice and enumerates store objects; Rebuilder plan; start-up source inspection and hashing in Ask mode. | `ClientsViewModel.Refresh; RebuilderViewModel.UpdatePlan; ClientStartup.UsableSources` |
| F-04 | Reliability | Low | Verified | ItemCatalogStore.Commit validates mapping source IDs after the snapshot is written. | `ItemCatalogStore.Commit` |
| F-05 | Reliability | Low | Verified | BackupSet.Add(move: true) across volumes deletes the original before checking the copy hash. | `BackupSet.Add` |
| F-06 | Code quality | Low | Verified | Orphaned XML doc comments. | `ClientStore.cs; ClientRegistry.cs` |
| F-07 | Build | Low | Verified | app.manifest assemblyIdentity version is 5.0.0.0; Portable.pubxml hard-codes 8.0.0 in PublishDir. | `app.manifest; Portable.pubxml` |
| F-08 | Code quality | Info | Verified | Six commands have no UI binding (OpenClientFolder, OpenSettings in Diagnostics, RefreshCatalogs, RotateLeft/Right, ThemeColorRow.Select). | `ViewModels` |
| F-09 | Reliability | Info | Verified | Three async void methods; AppearancesViewModel.UpdateDetails has no try/catch (falls back to the global handler). | `App.OnStartup; AppearancesViewModel.UpdateDetails; OutfitterViewModel.Render` |
| F-10 | Security | Low | Verified | Plain HTTP endpoints: test-server asset base (mitigated by hash check on download) and a credit link. | `TestServerAssetsBase; CreditTibia` |
| F-11 | Code quality | Info | Verified | ClientStorageMode is a string instead of an enum. | `AppSettings.ClientStorageMode` |
| F-12 | Reliability | Info | Verified | AppLog keeps at most 1000 pending lines; DiagnosticsCollector MaxLogBytes counts characters, not bytes. | `AppLog; DiagnosticsCollector` |
| F-13 | Code quality | Low | Verified | NumericUpDown adds button Click handlers again when its template is reapplied. | `Controls/NumericUpDown.cs` |
| F-14 | Code quality | Info | Verified | Two types named CatalogEntry (Core.Editing and Core.Items). | `Core` |
| F-15 | Correctness | Low | Inferred | AppearanceGifBuilder ping-pong frames may not form grids for grid effects. | `AppearanceGifBuilder` |
| F-16 | Documentation | Info | Verified | Several existing docs carry old version numbers in their titles (6.0, 7.0, 7.0.1, 7.1). | `docs/08, 10, 11, 13, 15, 16, 17, 18` |
| F-17 | Distribution | Info | Verified | The unpacked portable release folder contains runtime data created after the zip; the zip has only the program. | `dist/TibiaFoundry-8.0.0-portable-win-x64` |
| F-18 | Reliability | Info | Verified | AtomicFile does not flush to disk before replacing; StoreFiles.CopyIn does. | `Core/FileOps/AtomicFile.cs` |

## Strengths

- XML parsing (items.xml, XMP) is safe against external entities.
- Archive and manifest paths are checked by PathGuard; the archive inspector has size limits against zip bombs.
- CSV exports guard against formula injection.
- Logs and diagnostics reports are redacted (user paths, character data).
- Only HttpFetcher uses the network; Core and client handling are otherwise offline.
- Personal client data stays out of archives unless the user includes it (then baselines and manifests list personal paths).
