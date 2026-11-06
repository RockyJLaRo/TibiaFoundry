# 04 Settings and Services

## Composition root

The application has no DI container. `AppServices` (`src/TibiaFoundry.App/Services/AppServices.cs`) creates the services and hands them to every page view-model. *(Verified)*

| Service | Responsibility |
|---|---|
| `ClientDataService` (`Data`) | Loads the active client's appearances, sprites, static data, map and sounds; raises change events |
| `ClientLibrary` (`Clients`) | Client store, registered folders, the client in use per server |
| `LocationService` | Chooses, checks and resets client file locations |
| `OutputService`, `CreateOutput()` | Default output folders, file dialogs, `OutputSession` with the File safety settings |
| `OperationGate` (`Operations`, `ScriptOperations`) | Concurrency limits |
| `OperationTracker` (`Jobs`) | Operation history for Diagnostics |
| `ToolCatalog`, `ToolSettingsStore` | Tool manifests, dependency probes, settings each tool declares |
| `BackupStore`, `TempWorkspace` | Backups and temporary folders, which follow Settings |
| `StatusService`, `NotificationService` | Status bar and notifications |
| `IDialogService`, `ClipboardService`, `ShellService`, `AudioPlayer` | UI and system integration |
| `HttpClient` (`Http`) | Shared client for the few network operations |

Housekeeping runs at start-up and at exit. It removes backups beyond the retention limit and leftover temporary folders.

## Settings

- **File.** `settings.json`, read and written by `SettingsStore` (`src/TibiaFoundry.Core/Settings/`). The current format is 7.
- **Migration.** `AppSettings.Migrate` upgrades older formats and never downgrades. Settings that this build does not know are kept and written back unchanged (`UnknownSettings`).
- **Normalization.** `Normalize` clamps values into valid ranges.
- **Groups.**
  - General, Interface, Locations, Safety, Performance, Notifications, Scripts, Themes and Diagnostics;
  - client choice: `ActiveClientLive`/`ActiveClientTest`, the store folder, the storage mode, personal data, the launch check and monitoring;
  - output folders;
  - GIF and sound options;
  - `ToolState`, which holds the per-tool settings declared in the manifests.

The full list, with types and defaults, is in [settings-inventory.csv](inventories/settings-inventory.csv).
