"""
Builds the release directory of a version (9.0.4), last, from the repository root:
    python -I tools/release/make_release_directory.py [output folder]
Needs dist/ (package_release.py and build_installer.py) and website/site (website/build_site.py).
The default output is Release/TibiaFoundry_Release_<version>; a folder that exists is renamed, never deleted.

  Application/    TibiaFoundry-<v>-Installer.msi, TibiaFoundry-<v>-Portable.zip, TibiaFoundry-<v>-Source.zip
  Website/        TibiaFoundry-<v>-Website-Source.zip   the complete website project (source, build script, built site)
                  TibiaFoundry-<v>-Website-Assets.zip   every image and icon of the built site, at its path on the site
  Checksums/      SHA256SUMS.txt
  Documentation/  ReleaseNotes.md, Changelog.md, AuditReport.md, TestingReport.md, BuildInstructions.md

Every archive is tested, checked for files that must not be in it (build outputs, editor state, logs,
the unrelated "pokemon" and "games" folders) and for the version in its name; the script stops on the first problem.
"""
import datetime
import hashlib
import os
import re
import shutil
import sys
import zipfile

root = os.getcwd()
version = re.search(r"<Version>([^<]+)</Version>", open(os.path.join(root, "src", "Directory.Build.props"), encoding="utf-8").read()).group(1)
dist = os.path.join(root, "dist")
site = os.path.join(root, "website", "site")
readmes = os.path.join(root, "tools", "release", "artifacts")
out = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(root, "Release", f"TibiaFoundry_Release_{version}")

packages = [
    (os.path.join(dist, f"TibiaFoundry-{version}-win-x64.msi"), f"TibiaFoundry-{version}-Installer.msi"),
    (os.path.join(dist, f"TibiaFoundry-{version}-portable-win-x64.zip"), f"TibiaFoundry-{version}-Portable.zip"),
    (os.path.join(dist, f"TibiaFoundry-{version}-source.zip"), f"TibiaFoundry-{version}-Source.zip"),
]
documents = [
    (f"TibiaFoundry-{version}-ReleaseNotes.md", "ReleaseNotes.md"),
    ("CHANGELOG.md", "Changelog.md"),
    (f"TibiaFoundry-{version}-Audit.md", "AuditReport.md"),
    (f"TibiaFoundry-{version}-TestingReport.md", "TestingReport.md"),
    (os.path.join("docs", "06_Build_and_Setup.md"), "BuildInstructions.md"),
]
missing = [p for p, _ in packages if not os.path.isfile(p)] + [d for d, _ in documents if not os.path.isfile(os.path.join(root, d))]
if not os.path.isfile(os.path.join(site, "index.html")):
    missing.append(os.path.join(site, "index.html") + " (run website/build_site.py)")
if missing:
    sys.exit("Missing: " + ", ".join(missing))
built = open(os.path.join(site, "index.html"), encoding="utf-8").read()
if f"releases/download/v{version}/TibiaFoundry-{version}-Installer.msi" not in built:
    sys.exit(f"website/site was not built for {version}; run website/build_site.py")

if os.path.exists(out):
    os.rename(out, out + datetime.datetime.now().strftime(".previous-%Y%m%d-%H%M%S"))
for d in ("Application", "Website", "Checksums", "Documentation"):
    os.makedirs(os.path.join(out, d))


def readme(name):
    return open(os.path.join(readmes, name + "-README.txt"), encoding="utf-8").read().replace("{VERSION}", version)


def add_tree(z, src, prefix, skip=(), only=None):
    count = 0
    for base, dirs, files in os.walk(src):
        dirs[:] = sorted(d for d in dirs if d not in skip)
        for f in sorted(files):
            if only is not None and os.path.splitext(f)[1].lower() not in only:
                continue
            full = os.path.join(base, f)
            z.write(full, (prefix + "/" + os.path.relpath(full, src).replace("\\", "/")).lstrip("/"))
            count += 1
    return count


for path, name in packages:
    shutil.copy2(path, os.path.join(out, "Application", name))

# Website source: the whole website project as it sits in the repository (website/), the file its build reads
# the version from (src/Directory.Build.props) and the built, deploy-ready site (website/site).
website_source = os.path.join(out, "Website", f"TibiaFoundry-{version}-Website-Source.zip")
top = f"TibiaFoundry-{version}-Website-Source"
with zipfile.ZipFile(website_source, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    add_tree(z, os.path.join(root, "website"), top + "/website", skip=("__pycache__",))
    z.write(os.path.join(root, "src", "Directory.Build.props"), top + "/src/Directory.Build.props")
    z.write(os.path.join(root, f"TibiaFoundry-{version}-ReleaseNotes.md"), top + f"/TibiaFoundry-{version}-ReleaseNotes.md")
    z.writestr(top + "/README.txt", readme("Website-Source").replace("\r\n", "\n").replace("\n", "\r\n"))

# Website assets: every image and icon of the built site, at the path the pages load it from.
ASSETS = {".webp", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".avif", ".woff", ".woff2"}
website_assets = os.path.join(out, "Website", f"TibiaFoundry-{version}-Website-Assets.zip")
top = f"TibiaFoundry-{version}-Website-Assets"
with zipfile.ZipFile(website_assets, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    assets = add_tree(z, site, top, only=ASSETS)
    z.writestr(top + "/README.txt", readme("Website-Assets").replace("{COUNT}", str(assets)).replace("\r\n", "\n").replace("\n", "\r\n"))

for source, name in documents:
    shutil.copy2(os.path.join(root, source), os.path.join(out, "Documentation", name))

# ---------------------------------------------------------------- validation
FORBIDDEN_DIRS = {"bin", "obj", ".git", ".vs", "__pycache__", "TestResults", "pokemon", "games", "TibiaFoundry-Data", "logs"}
FORBIDDEN_EXT = {".user", ".suo", ".log", ".pdb", ".tmp"}
problems = []
rows = []
released = [os.path.join(out, "Application", n) for _, n in packages] + [website_source, website_assets]
for path in released:
    name = os.path.basename(path)
    if version not in name:
        problems.append(f"{name}: the version is not in the name")
    entries = None
    if name.endswith(".zip"):
        with zipfile.ZipFile(path) as z:
            bad = z.testzip()
            if bad is not None:
                problems.append(f"{name}: damaged entry {bad}")
            infos = z.infolist()
            entries = len(infos)
            for info in infos:
                parts = info.filename.replace("\\", "/").split("/")
                # The portable program's own files are checked by name only ("logs" and friends are folders of user data).
                if any(p in FORBIDDEN_DIRS for p in parts[:-1]) or parts[-1] == ".git" or os.path.splitext(parts[-1])[1].lower() in FORBIDDEN_EXT:
                    problems.append(f"{name}: must not contain {info.filename}")
    elif open(path, "rb").read(8) != b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1":
        problems.append(f"{name}: not a Windows Installer file")
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    rows.append((os.path.relpath(path, out).replace("\\", "/"), os.path.getsize(path), entries, h.hexdigest()))
if problems:
    sys.exit("Release directory NOT valid:\n  " + "\n  ".join(problems[:40]))

open(os.path.join(out, "Checksums", "SHA256SUMS.txt"), "w", encoding="utf-8", newline="\n").write(
    "".join(f"{digest}  {os.path.basename(rel)}\n" for rel, _, _, digest in rows))
lines = [f"Tibia Foundry {version} - release packages", "=" * 44, "", f"Built {datetime.date.today().isoformat()} from the finalized source.", ""]
for rel, size, entries, digest in rows:
    lines += [rel, f"  size     {size:,} bytes ({size / 1048576:.1f} MB)" + (f", {entries:,} entries" if entries is not None else ""), f"  SHA-256  {digest}", ""]
open(os.path.join(out, "Checksums", "PackageManifest.txt"), "w", encoding="utf-8", newline="\r\n").write("\n".join(lines))
print(out)
print("\n".join(lines))
