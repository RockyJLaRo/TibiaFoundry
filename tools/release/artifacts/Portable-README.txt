Tibia Foundry {VERSION} - portable edition (Windows x64)
=======================================================

TibiaFoundry-Portable.zip holds one self-contained program: no installation and no .NET runtime needed.

  1. Extract the ZIP to any folder you can write to (a USB stick works too), for example
     D:\Tools\TibiaFoundry. Do not run it from inside the ZIP.
  2. Start TibiaFoundry.exe.

Everything the program saves (settings, logs, client archive, the Asset Library) goes into a
"TibiaFoundry-Data" folder next to TibiaFoundry.exe (this build is marked portable inside the executable).
Nothing is written to the registry or to %LOCALAPPDATA%.

Updating: extract the new version over the old one, or into a new folder and copy TibiaFoundry-Data across.
Removing: delete the folder.

Contents
  TibiaFoundry.exe            the program (all libraries bundled)
  README-Portable.txt         release notes and details of the portable edition
  THIRD-PARTY-NOTICES.txt     licenses of the bundled libraries
  README.txt                  this file

Windows SmartScreen may warn on first start because the program is not code-signed; choose
"More info" > "Run anyway" if you trust the source.
