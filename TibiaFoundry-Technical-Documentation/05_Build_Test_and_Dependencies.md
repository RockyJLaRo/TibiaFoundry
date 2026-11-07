# 05 Build, Test and Dependencies

## Toolchain

| Item | Value |
|---|---|
| SDK | .NET 10 (`global.json`: 10.0.100, rollForward latestFeature). Audit machine: SDK 10.0.402 |
| Version | 8.0.0 (`src/Directory.Build.props`) |
| Package versions | Central (`src/Directory.Packages.props`) |
| Solution | `src/TibiaFoundry.slnx` |
| Portable publish profile | `Properties/PublishProfiles/Portable.pubxml` |

## Audit results (2026-10-21)

| Step | Result | Status |
|---|---|---|
| `dotnet build TibiaFoundry.slnx -c Debug` | 0 warnings, 0 errors | Verified |
| `dotnet test` (TRX) | 522 total: 509 passed, 0 failed, 13 skipped, 1 min 7 s | Verified |

**Skipped tests.** The 13 skipped tests are opt-in. They need a real client, a long measurement or the network, and are switched on by `TF_*` environment variables, for example `TF_LIVE_WIKI`, `TF_FULL_SPRITES` and `TF_CLIENT_STORE_MEASURE`.

**Slowest test.** `CompareAndFilterParity` takes 31 s.

These were not run in this audit:

- the UI snapshot harness (`--ui-snapshot`), because it opens windows;
- the release scripts in `tools/release`.

## Packages

| Package | Version | Used by |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | App |
| NAudio.Core / NAudio.WinMM | 2.2.1 | App |
| NAudio.Vorbis (NVorbis 0.10.4) | 1.5.0 | App |
| Google.Protobuf | 3.36.2 | Core |
| Grpc.Tools (build only) | 2.84.0 | Core |
| Magick.NET-Q16-AnyCPU | 14.16.0 | Core |
| Newtonsoft.Json | 13.0.4 | Core |
| System.Drawing.Common | 10.0.12 | Core |
| xunit.v3 / xunit.runner.visualstudio / Microsoft.NET.Test.Sdk | 1.1.0 / 3.0.2 / 17.12.0 | Tests |

License texts are in `THIRD-PARTY-NOTICES.txt`. The transitive packages are listed in [dependency-inventory.csv](inventories/dependency-inventory.csv).

## Distribution

`dist/` holds the release zips and the unpacked portable folder (about 4.1 GB in total, including baselines). Rules for release files:

- They are hard-linked, so they must never be rewritten in place. Write a temporary file, then replace the original.
- `dist/TibiaFoundry-8.0.0-portable-win-x64` now contains a `TibiaFoundry-Data` folder that was created after the zip. The zip itself is unaffected (F-17).
