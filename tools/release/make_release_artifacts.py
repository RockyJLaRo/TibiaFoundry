"""
Assembles Release_Artifacts/ from the finished builds (run from the repository root, last):
    python -I tools/release/make_release_artifacts.py
Needs: dist/ from package_release.py and build_installer.py, and website/site from build_site.py.
Writes Source_Code/, Portable/, Installer/ and Website/ (each a ZIP plus README.txt, the README also
inside the ZIP), Checksums/SHA256SUMS.txt and Build_Reports/Package_Manifest.txt. An existing
Release_Artifacts folder is renamed (never deleted). Build_Report.txt and Test_Report.txt are written by hand.
"""
import datetime
import hashlib
import os
import re
import sys
import zipfile

root = os.getcwd()
version = re.search(r"<Version>([^<]+)</Version>", open(os.path.join(root, "src", "Directory.Build.props"), encoding="utf-8").read()).group(1)
dist = os.path.join(root, "dist")
out = os.path.join(root, "Release_Artifacts")
readmes = os.path.join(root, "tools", "release", "artifacts")
portable = os.path.join(dist, f"TibiaFoundry-{version}-portable-win-x64")
msi = os.path.join(dist, f"TibiaFoundry-{version}-win-x64.msi")
source = os.path.join(dist, f"TibiaFoundry-{version}-source.zip")
site = os.path.join(root, "website", "site")
for path in (os.path.join(portable, "TibiaFoundry.exe"), msi, source, os.path.join(site, "index.html")):
    if not os.path.isfile(path):
        sys.exit(f"Missing: {path}")

if os.path.exists(out):
    os.rename(out, out + datetime.datetime.now().strftime(".previous-%Y%m%d-%H%M%S"))
for d in ("Source_Code", "Portable", "Installer", "Website", "Build_Reports", "Checksums"):
    os.makedirs(os.path.join(out, d))


def readme(name):
    return open(os.path.join(readmes, name + "-README.txt"), encoding="utf-8").read().replace("{VERSION}", version)


def add_tree(z, src, prefix, skip=()):
    for base, dirs, files in os.walk(src):
        dirs[:] = sorted(d for d in dirs if d not in skip)
        for f in sorted(files):
            full = os.path.join(base, f)
            z.write(full, (prefix + "/" + os.path.relpath(full, src).replace("\\", "/")).lstrip("/"))


def write_readme(folder, name):
    text = readme(name)
    open(os.path.join(out, folder, "README.txt"), "w", encoding="utf-8", newline="\r\n").write(text)
    return text


def new_zip(folder, name):
    return zipfile.ZipFile(os.path.join(out, folder, name), "w", zipfile.ZIP_DEFLATED, compresslevel=9)


# Source: the package_release.py source ZIP plus the README.
text = write_readme("Source_Code", "Source_Code")
with zipfile.ZipFile(source) as src, new_zip("Source_Code", "TibiaFoundry-Source.zip") as z:
    for info in src.infolist():
        z.writestr(info, src.read(info.filename))
    z.writestr(f"TibiaFoundry-{version}-source/README-SOURCE.txt", text)

text = write_readme("Portable", "Portable")
with new_zip("Portable", "TibiaFoundry-Portable.zip") as z:
    add_tree(z, portable, f"TibiaFoundry-{version}-portable", skip=("TibiaFoundry-Data",))
    z.writestr(f"TibiaFoundry-{version}-portable/README.txt", text)

text = write_readme("Installer", "Installer")
with new_zip("Installer", "TibiaFoundry-Installer.zip") as z:
    z.write(msi, os.path.basename(msi))
    z.writestr("README.txt", text)

text = write_readme("Website", "Website")
with new_zip("Website", "TibiaFoundry-Website.zip") as z:
    add_tree(z, site, "site")
    add_tree(z, os.path.join(root, "website", "src"), "src", skip=("__pycache__",))
    z.write(os.path.join(root, "website", "build_site.py"), "build_site.py")
    z.write(os.path.join(root, "website", "README.md"), "README.md")
    z.writestr("README.txt", text)

archives = [("Source", "Source_Code/TibiaFoundry-Source.zip"), ("Portable", "Portable/TibiaFoundry-Portable.zip"),
            ("Non-portable (installer)", "Installer/TibiaFoundry-Installer.zip"), ("Website", "Website/TibiaFoundry-Website.zip")]
sums = []
rows = []
for label, rel in archives:
    path = os.path.join(out, rel)
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    with zipfile.ZipFile(path) as z:
        if z.testzip() is not None:
            sys.exit(f"Corrupt archive: {rel}")
        entries = len(z.infolist())
    sums.append(f"{h.hexdigest()}  {rel}")
    rows.append((label, rel, os.path.getsize(path), entries, h.hexdigest()))
open(os.path.join(out, "Checksums", "SHA256SUMS.txt"), "w", encoding="utf-8", newline="\n").write("\n".join(sums) + "\n")

lines = [f"Tibia Foundry {version} - package manifest", "=" * 44, "",
         f"Release version:        {version}",
         f"Build date:             {datetime.date.today().isoformat()}",
         "Target framework:       net10.0-windows (.NET 10, WPF)",
         "Runtime identifier:     win-x64 (self-contained)",
         "Build configuration:    Release (ReadyToRun; portable: single file, compressed)", ""]
for label, rel, size, entries, digest in rows:
    lines += [f"{label} ZIP:", f"  File:     Release_Artifacts/{rel}", f"  Size:     {size:,} bytes ({size / 1048576:.1f} MB), {entries:,} entries",
              f"  SHA-256:  {digest}", ""]
lines += ["Build and test status, known limitations: see Build_Report.txt and Test_Report.txt in this folder.", ""]
open(os.path.join(out, "Build_Reports", "Package_Manifest.txt"), "w", encoding="utf-8", newline="\r\n").write("\n".join(lines))
print("\n".join(lines))
