# Tibia Foundry 8.0.0: Technical Documentation

This package gives a general technical description of Tibia Foundry 8.0.0, based on an audit made on 2026-10-21. During the audit, the source was parsed, the solution was built and the test suite was run. The application's code, configuration and existing `docs/` were not changed.

## Contents

| Document | Covers |
|---|---|
| [01 Overview and Architecture](01_Overview_and_Architecture.md) | Purpose, projects, layers, start-up, page model, operation pipeline |
| [02 Features](02_Features.md) | The 19 pages, what they read and write, external dependencies |
| [03 Data, Storage and Formats](03_Data_Storage_and_Formats.md) | Where data lives, the client store, file formats |
| [04 Settings and Services](04_Settings_and_Services.md) | Composition root, services, settings format 7 |
| [05 Build, Test and Dependencies](05_Build_Test_and_Dependencies.md) | Toolchain, packages, test results, distribution |
| [06 Findings](06_Findings.md) | Security, reliability, performance and code-quality findings |
| [07 Limitations and Maintenance](07_Limitations_and_Maintenance.md) | Known limits, technical debt, where changes have impact |
| [08 Glossary](08_Glossary.md) | Terms used in the code and in these documents |
| [diagrams/architecture.mmd](diagrams/architecture.mmd) | Mermaid diagram of the layers and main services |
| [audit/methodology.md](audit/methodology.md) | How the audit was made, what was excluded, open questions |
| [audit/findings-register.csv](audit/findings-register.csv) | The findings as a table |

### Inventories (generated from the source)

| File | Rows |
|---|---|
| [file-inventory.csv](inventories/file-inventory.csv) | Files under `src`, `tools`, `docs` and `website`, plus root files |
| [class-and-interface-inventory.csv](inventories/class-and-interface-inventory.csv) | Every type that the source declares |
| [settings-inventory.csv](inventories/settings-inventory.csv) | Settings properties with their defaults |
| [test-inventory.csv](inventories/test-inventory.csv) | All 522 tests with their outcome in the audit run |
| [dependency-inventory.csv](inventories/dependency-inventory.csv) | NuGet packages for each project |

## Status labels

| Label | Meaning |
|---|---|
| **Verified** | Confirmed by the build, a test run or reading the code. |
| **Inferred** | Follows from reading the code, but was not exercised. |
| **Unverified** | Not checked in this audit. |

The scope is deliberately general. Per-method detail is in the source's own XML documentation and in the generated inventories.
