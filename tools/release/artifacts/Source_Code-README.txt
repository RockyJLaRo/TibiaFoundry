Tibia Foundry {VERSION} - complete source code
================================================

TibiaFoundry-Source.zip holds everything needed to open, build, test, package and continue developing
Tibia Foundry {VERSION}. Build outputs (bin, obj, dist), editor state and release folders are left out.

Project structure
-----------------
  src/TibiaFoundry.slnx            the solution (open this)
  src/TibiaFoundry.App/            the WPF program (.NET 10, MVVM): Views, ViewModels, Services, Assets
  src/TibiaFoundry.Core/           the engine: client formats, sprites, appearances, Asset Library (SQLite),
                                   content workflows, processing queue, exports
  src/TibiaFoundry.Tests/          xUnit v3 tests (some need a local Tibia client; they skip without one)
  src/Directory.Build.props        version ({VERSION}), shared build settings
  src/Directory.Packages.props     NuGet package versions (central package management)
  docs/                            user and developer documentation (06_Build_and_Setup.md, 11_Maintenance_Guide.md)
  tools/release/                   packaging: package_release.py, make_notices.py, build_installer.py,
                                   make_release_artifacts.py, installer/TibiaFoundry.wxs
  website/                         tibiafoundry.com: src/ (template, CSS, JS, images), build_site.py, site/ (built)

Prerequisites
-------------
  * Windows 10/11 x64
  * .NET 10 SDK (https://dotnet.microsoft.com/download)
  * Optional: Visual Studio 2026 / Rider / VS Code with C# Dev Kit
  * Packaging: Python 3.11+; WiX Toolset 5.0.2 (dotnet tool install -g wix --version 5.0.2)
  * Website: Python 3 with Pillow (pip install pillow)

Open, restore, build, test (from src/)
--------------------------------------
  Open src/TibiaFoundry.slnx in Visual Studio, or use the command line:
    dotnet restore TibiaFoundry.slnx
    dotnet build TibiaFoundry.slnx
    dotnet test TibiaFoundry.slnx
    dotnet run --project TibiaFoundry.App

Distributions (from src/, then from the repository root)
--------------------------------------------------------
  Folder (non-portable) edition, used by the installer:
    dotnet publish TibiaFoundry.App/TibiaFoundry.App.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o ../dist/TibiaFoundry-{VERSION}-win-x64
  Portable edition (one self-contained executable):
    dotnet publish TibiaFoundry.App/TibiaFoundry.App.csproj -p:PublishProfile=Portable
  Then, from the repository root:
    python -I tools/release/package_release.py          notices, edition ZIPs, source ZIP, checksums in dist/
    python -I tools/release/build_installer.py          dist/TibiaFoundry-{VERSION}-win-x64.msi
    python -I website/build_site.py                      website/site/
    python -I tools/release/make_release_artifacts.py    Release_Artifacts/

The program and its data
------------------------
  The installed edition keeps settings, logs and the Asset Library in %LOCALAPPDATA%\TibiaFoundry;
  the portable edition keeps them in TibiaFoundry-Data next to its executable.
