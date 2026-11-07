"""
Builds the Windows Installer package of the folder edition (run from the repository root after the
folder publish and package_release.py, which adds THIRD-PARTY-NOTICES.txt to it):
    python -I tools/release/build_installer.py
Needs the WiX Toolset 5 .NET tool (dotnet tool install -g wix --version 5.0.2). Writes dist/TibiaFoundry-<version>-win-x64.msi.
"""
import os
import re
import subprocess
import sys

root = os.getcwd()
version = re.search(r"<Version>([^<]+)</Version>", open(os.path.join(root, "src", "Directory.Build.props"), encoding="utf-8").read()).group(1)
folder = os.path.join(root, "dist", f"TibiaFoundry-{version}-win-x64")
if not os.path.isfile(os.path.join(folder, "TibiaFoundry.exe")):
    sys.exit(f"Not published: {folder}")
if os.path.exists(os.path.join(folder, "TibiaFoundry.portable")):
    sys.exit("The folder edition must not carry the portable marker.")
msi = os.path.join(root, "dist", f"TibiaFoundry-{version}-win-x64.msi")
subprocess.run(["wix", "build", os.path.join(root, "tools", "release", "installer", "TibiaFoundry.wxs"),
                "-arch", "x64", "-d", f"Version={version}", "-d", f"PublishDir={folder}",
                "-o", msi], check=True)
print(f"{os.path.basename(msi)}: {os.path.getsize(msi) / 1048576:.1f} MB")
