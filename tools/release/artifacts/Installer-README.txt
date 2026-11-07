Tibia Foundry {VERSION} - Windows installer (x64)
================================================

TibiaFoundry-Installer.zip holds a Windows Installer package (MSI):

  TibiaFoundry-{VERSION}-win-x64.msi   the installer (self-contained: no .NET runtime needed)
  README.txt                           this file

Install
  Extract the ZIP and double-click the MSI (administrator rights are needed: it installs for all users).
  It installs to C:\Program Files\Tibia Foundry and adds "Tibia Foundry" to the Start menu.
  Silent install:   msiexec /i TibiaFoundry-{VERSION}-win-x64.msi /qn

Upgrade
  Installing a newer MSI replaces the older version in place. Installing an older version over a newer
  one is refused.

Uninstall
  Settings > Apps > Installed apps > Tibia Foundry > Uninstall, or:
  msiexec /x TibiaFoundry-{VERSION}-win-x64.msi

Your data
  Settings, logs, the client archive and the Asset Library are kept in %LOCALAPPDATA%\TibiaFoundry,
  and exports in the output folder you chose; installing, upgrading and uninstalling never touch them.
  Delete that folder yourself if you want them gone.

The MSI is not code-signed; Windows SmartScreen may warn before it runs.
Prefer no installation? Use the portable edition (TibiaFoundry-Portable.zip).
