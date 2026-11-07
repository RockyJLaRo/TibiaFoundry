# Audit Methodology

## Method

- **Source.** Every C# file under `src` (266 files) and `tools` (7 files) was parsed with Roslyn, with 0 parse errors. Types, members, documentation comments and complexity were extracted. XAML files were scanned for bindings, commands and automation names.
- **Reading.** The services, the client store, the file operations, the settings and the view-models were read by hand, with notes for each file.
- **Build and tests.** A Debug build and the full test suite were run (see [05](../05_Build_Test_and_Dependencies.md)).
- **Labels.** Findings are labeled Verified (confirmed by code, build or test) or Inferred (from reading the code, not exercised).
- **Read-only.** No application code, configuration or existing docs were changed.

## Exclusions

These were left out:

- **Generated or historical material.** `00_original_backup` … `05_verification` (the original program, its decompiled source and research material) and the release baselines `06_baseline_*` … `15_baseline_*`.
- **Build output.** `dist/`, `bin/` and `obj/`.
- **Drive metadata.** `desktop.ini` files.
- **Anything that opens windows or needs the live client.** The UI snapshot harness, opt-in measurement tests and in-game checks.

## Open questions

- F-01 and F-03 are inferred. Their real-world impact (custom install locations, large archives) was not measured.
- The behavior of the opt-in tests (13 skipped) was not re-checked in this audit.
