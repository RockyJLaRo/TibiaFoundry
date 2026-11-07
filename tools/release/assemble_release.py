"""
Collects the finished packages of a version into Release/<version>/ (8.8.1), after
package_release.py and build_installer.py (run from the repository root):
    python -I tools/release/assemble_release.py
Copies, under the release file names (9.0: the installer is "-Installer.msi", the names the website and
the GitHub release use; <v> is the version from src/Directory.Build.props, e.g. 9.0 or 9.0.1),
    dist/TibiaFoundry-<v>-win-x64.msi            -> Release/<v>/TibiaFoundry-<v>-Installer.msi
    dist/TibiaFoundry-<v>-portable-win-x64.zip   -> Release/<v>/TibiaFoundry-<v>-Portable.zip
    dist/TibiaFoundry-<v>-source.zip             -> Release/<v>/TibiaFoundry-<v>-Source.zip
and the release notes and performance audit from the repository root, then writes SHA256SUMS.txt.
Files already in Release/<v>/ that are not one of these are left alone. ReleaseManifest.md is
written by hand from the validation results (it needs the results of installing and running).
"""
import hashlib
import os
import re
import shutil
import sys

root = os.getcwd()
version = re.search(r"<Version>([^<]+)</Version>", open(os.path.join(root, "src", "Directory.Build.props"), encoding="utf-8").read()).group(1)
dist = os.path.join(root, "dist")
target = os.path.join(root, "Release", version)
packages = [
    (os.path.join(dist, f"TibiaFoundry-{version}-win-x64.msi"), f"TibiaFoundry-{version}-Installer.msi"),
    (os.path.join(dist, f"TibiaFoundry-{version}-portable-win-x64.zip"), f"TibiaFoundry-{version}-Portable.zip"),
    (os.path.join(dist, f"TibiaFoundry-{version}-source.zip"), f"TibiaFoundry-{version}-Source.zip"),
]
documents = [f"TibiaFoundry-{version}-ReleaseNotes.md", "TibiaFoundry_Performance_Audit.md"]
if os.path.isfile(os.path.join(root, f"TibiaFoundry-{version}-Audit.md")):
    documents.append(f"TibiaFoundry-{version}-Audit.md")
missing = [p for p, _ in packages if not os.path.isfile(p)] + [d for d in documents if not os.path.isfile(os.path.join(root, d))]
if missing:
    sys.exit("Missing: " + ", ".join(missing))
os.makedirs(target, exist_ok=True)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


lines = []
for source, name in packages:
    destination = os.path.join(target, name)
    shutil.copyfile(source, destination)
    if sha256(source) != sha256(destination):
        sys.exit(f"{name}: the copy differs from {source}")
    lines.append(f"{sha256(destination)}  {name}")
    print(f"{name}: {os.path.getsize(destination):,} bytes")
for name in documents:
    shutil.copyfile(os.path.join(root, name), os.path.join(target, name))
open(os.path.join(target, "SHA256SUMS.txt"), "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
print("\n".join(lines))
