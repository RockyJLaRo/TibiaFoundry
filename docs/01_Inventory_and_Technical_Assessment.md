# Phase 1 – File Inventory and Technical Assessment

> **Note (Tibia Foundry 6.0):** this is a historical record of the original program (version 1.8.0.26867) and the 2.0 reconstruction. The program was later renamed Tibia Forge (version 3.0) and then **Tibia Foundry** (version 4.0). Paths and figures below are those of the earlier version; since 6.0 the earlier product names are written as placeholders (`<original>` for the original program's file names, `<v2>` for the identifier of version 2.x, see `13_Branding_and_Attribution.md` §10). See `07_Testing_Report.md` for current results.

Subject: **the original program, version 1.8.0.26867** (the folder that contains `Reconstruction`)
Publisher and copyright of the original: as recorded in its assembly metadata (© 2025); the files are kept unchanged in `00_original_backup/`
Investigation date: 2026-10-11

All original files were left untouched. A byte-for-byte copy lives in
`Reconstruction/00_original_backup/` and SHA-256 hashes are recorded in
`Reconstruction/02_analysis/original_file_hashes.csv` (copy verified: 0 mismatches).

## 1. Complete file inventory

| File | Size (bytes) | Type | Role |
|---|---:|---|---|
| `<original>.exe` | 733,696 | .NET Framework 4.8 managed PE (AnyCPU, *Prefer32Bit*) | **Entry point** – WinForms application |
| `<original>.pdb` | 1,089,024 | Portable PDB | Debug symbols (local variable names, sequence points) |
| `<original>.exe.config` | 1,049 | XML | Runtime `supportedRuntime v4.0 / .NETFramework 4.8` + binding redirects |
| `<original>.exe.manifest` | 14,794 | ClickOnce application manifest | Lists all dependencies + `<original>.ico` (not shipped) |
| `<original>.application` | 1,881 | ClickOnce deployment manifest | v1.8.0.0, install=true, publisher field set |
| `settings.json` | 360 | JSON | **User data** – paths + GIF author (written by the app on exit) |
| `Google.Protobuf.dll` / `.pdb` | 483,432 / 184,012 | Managed 3.27.5 | Protobuf runtime for the Tibia `.dat` files |
| `Magick.NET-Q16-AnyCPU.dll` | 1,154,736 | Managed 14.16.0 | ImageMagick wrapper (GIF writing, compositing, trim) |
| `Magick.NET.Core.dll` | 1,534,640 | Managed 14.16.0 | ImageMagick core interfaces |
| `Magick.Native-Q16-{x86,x64,arm64}.dll` | 19–24 MB each | Native 7.1.2-29 | ImageMagick native engine (loaded per process architecture) |
| `NAudio.Core.dll` | 187,904 | Managed 2.0.0 | Audio pipeline |
| `NAudio.Vorbis.dll` / `NVorbis.dll` | 14,848 / 80,896 | Managed 1.5.0 / 0.10.4 | OGG Vorbis decoding |
| `Newtonsoft.Json.dll` | 653,824 | Managed 10.0.3 | JSON (catalog parsing, all JSON exports, settings) |
| `taglib-sharp.dll` + `policy.2.0.taglib-sharp.dll` | 443,392 / 3,072 | Managed 2.1.0 (2012) | Writes the GIF comment block ("Created by … using <original>") |
| `System.Buffers/Memory/Numerics.Vectors/Runtime.CompilerServices.Unsafe/ValueTuple.dll` | 16–141 KB | Managed BCL back-ports | Protobuf/NAudio support on .NET Framework |

No databases, caches, scripts, plugins or external executables are shipped.
`settings.json` is the only persistent user state. It is written next to the EXE
(`Application.StartupPath`) when the window closes.

### Missing / environmental items
* `<original>.ico` is referenced by the ClickOnce manifest but is absent. It is **not**
  needed at run time, because the icon is embedded in the EXE (recovered as `app.ico`).
* Runtime requirement: .NET Framework 4.8 (present on Windows 10/11).
* Data requirement: a Tibia client installation (`%LOCALAPPDATA%\Tibia\packages\Tibia\`) with
  `assets\` and `sounds\`. Present on this machine: 5,084 sprite sheets, appearances/staticdata/
  staticmapdata/map `.dat` files, 1,157 map tiles, 846 sounds.
* Previously extracted sprites (104,855 PNGs in `Documents\TibiaSprites\Sprites_8-13-2026`) and
  GIFs created by the original exist. They are used as **read-only reference outputs**.

## 2. Technology identification

| Aspect | Finding | Evidence |
|---|---|---|
| Language | C# | IL + PDB, compiler-generated closures |
| Framework | .NET Framework 4.8, **Windows Forms** | `TargetFramework net48`, `UseWindowsForms`, `System.Windows.Forms` usage |
| Architecture | AnyCPU, *Prefer32Bit* → runs as a **32-bit** process | PE flags, x86 Magick.Native loaded |
| Packaging | ClickOnce (`.application` + `.exe.manifest`) | manifests |
| Entry point | `Program.Main` → `Application.Run(new MainForm())` | decompiled `Program.cs` |
| Debug info | Portable PDB matches the EXE (local names recovered) | `--use-varnames-from-pdb` worked |

## 3. Source-recovery assessment

Managed IL with a matching PDB decompiles with very high fidelity. ILSpy 11.1 (`ilspycmd`)
was used. The raw output is in `Reconstruction/01_decompiled/` and is kept for reference only.

| Component | Lines | Recovery |
|---|---:|---|
| `MainForm` (all tabs, ~170 handlers) | 14,097 | **Complete logic recovered**; designer code recovered as `InitializeComponent` |
| `CompareDats` window | 1,233 | Complete |
| `Helper` (outfit rendering, GIF export, name casing) | 259 | Complete |
| `ViewAllSprites`, `ExtractByID`, `InsertSpriteID` dialogs | ~400 | Complete |
| `GifEncoder` | 171 | Complete. **Dead code** (never referenced) |
| Model classes (`TibiaArea`, `TibiaNPC`, …) | ~700 | Complete |
| Protobuf message classes (7 schemas) | ~6,000 generated | **Original `.proto` schemas recovered exactly** from the embedded `FileDescriptorProto` (see `02_analysis/recovered_protos/`). Namespaces and reflection class names regenerate identically. |
| `SevenZip.*` | ~3,000 | The public-domain **LZMA SDK** (C#), unmodified |
| Embedded resources | – | `app.ico`, `tw` (TibiaWiki watermark), `pbbg1` (House Images preview background), and `clientbg` and `pbbg`, which are never referenced (corrected after the Phase 1 draft) |

**Conclusion:** all of the application logic can be recovered directly. Nothing has to be
inferred from black-box behaviour alone. Behaviour that the code does not determine (the exact
pixels drawn by GDI+, ImageMagick quantisation) will be checked against reference outputs
from the original binary, run in a test harness.

## 4. Preliminary technical assessment of the original

* **One 8,000-line form class** holds all UI, state and logic, coordinated through public static
  fields (`MainForm.datfile`, `DatSpritesDir`, colour indexes).
* **Fixed-size window** (`FormBorderStyle.Fixed3D`, `MaximizeBox=false`, 780×557 client area,
  absolute-positioned controls). It cannot be resized and it clips at high DPI.
* **No theming**. Uses system colours only.
* **Error handling**: about 40 `MessageBox` calls, many silent `catch {}` blocks, and many unguarded
  `.First()`, `[0]`, `int.Parse` and `new Bitmap(path)` calls that crash on unexpected data
  (details in the bug report).
* **Threading**: most long operations (sound extraction, map extraction, HR map build, house
  rendering, monster loading) run **on the UI thread**. The two `BackgroundWorker`s read
  UI controls from the worker thread.
* **Resource leaks**: `File.OpenRead` streams passed to protobuf parsers are never closed (the
  `.dat` files stay locked), `new Bitmap(path)` locks sprite files, and thousands of GDI+ bitmaps are
  never disposed.
* **Obsolete APIs**: `BinaryFormatter` (used for "deep clone"; removed in .NET 9),
  `WebClient`, `Process.Start(url)` (fails on .NET Core).
* **Hidden/unfinished features**: three tab pages (*Export Sheets*, *Sprites from PNG*,
  *LZMA Decode*) are removed from the tab control at start-up. Several controls have no
  handler or an empty one ("Keep Selected", Static-Data sprite filter, House-Images "Change
  file", "Generate Wiki Area/Subarea List").
* **Persistence**: `settings.json` next to the executable (fails when installed under
  Program Files; ClickOnce installs elsewhere).

## 5. Recovery & rebuild strategy

1. Keep the original untouched; run it only from `Reconstruction/03_sandbox_original/`.
2. Use the decompiled code as the **reference implementation**. Use the recovered `.proto` files
   as the schema source, so protoc regenerates the message classes at build time.
3. Build a **parity harness** (net48) that loads the *original* `<original>.exe` via reflection.
   It produces reference outputs (JSON, sprites, outfit frames, GIFs, wiki text, house images,
   map tiles, sound files) and off-screen screenshots of every original screen.
4. Re-implement on **.NET 10 (LTS) + WPF** with a separate, UI-free core library, MVVM view-models,
   a complete light/dark/system theme system and a resizable layout.
5. Verify the rebuild against the harness outputs and the user's existing extractions.
