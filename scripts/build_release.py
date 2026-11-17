"""
Builds every release package of the current version into Release/<version>/ (run from the repository root):

    python -I scripts/build_release.py [--skip-publish] [--skip-site]

The version comes from src/Directory.Build.props. Steps (each stops the script on failure):
  1. dotnet publish: folder edition (self-contained, ReadyToRun) and portable edition (one file) into dist/;
  2. tools/release/package_release.py: third-party notices, edition ZIPs in dist/;
     tools/release/build_installer.py: the Windows Installer package (WiX 5) in dist/;
  3. Release/<version>/:
       TibiaFoundry-<v>.msi             the installer (also inside the MSI ZIP)
       TibiaFoundry-<v>-MSI.zip         TibiaFoundry-<v>.msi
       TibiaFoundry-<v>-Portable.zip    TibiaFoundry.exe + notices + README-Portable.txt
       TibiaFoundry-<v>-Source.zip      every file git tracks at HEAD (the tree must be committed), without the generated website/site
  4. website/build_site.py --strict --checksums (the Downloads page shows the three archives' SHA-256), then
       TibiaFoundry-<v>-Website.zip     website/ (sources, build script, built site, screenshot and test tools, docs) plus the
                                         few program files the build reads (version, interface texts, link icons)
  5. SHA256SUMS.txt for every file above.
Protected paths (games/, pokemon/, tools/, .github/, CNAME, robots.txt) are never written; the website archive leaves out
CNAME and robots.txt of the Pages repository. Nothing is uploaded.
"""
import hashlib
import os
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = os.getcwd()
VERSION = re.search(r"<Version>([^<]+)</Version>", open(os.path.join(ROOT, "src", "Directory.Build.props"), encoding="utf-8").read()).group(1)
DIST = os.path.join(ROOT, "dist")
OUT = os.path.join(ROOT, "Release", VERSION)
SKIP_PARTS = {"__pycache__", ".work", "bin", "obj", "TestResults"}


def run(cmd, cwd=ROOT):
    print("  $ " + " ".join(cmd), flush=True)
    r = subprocess.run(cmd, cwd=cwd)
    if r.returncode:
        sys.exit(f"failed ({r.returncode}): {' '.join(cmd)}")


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def new_zip(path):
    if os.path.exists(path):
        os.remove(path)
    return zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9)


def add_tree(z, folder, prefix, skip=()):
    for base, dirs, files in os.walk(folder):
        dirs[:] = sorted(d for d in dirs if d not in SKIP_PARTS and d not in skip)
        for f in sorted(files):
            full = os.path.join(base, f)
            rel = os.path.relpath(full, folder).replace("\\", "/")
            if rel in skip or f.endswith((".pyc", ".log", ".tmp")):
                continue
            z.write(full, f"{prefix}/{rel}" if prefix else rel)


def git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=True).stdout


def main():
    args = sys.argv[1:]
    os.makedirs(OUT, exist_ok=True)
    folder = os.path.join(DIST, f"TibiaFoundry-{VERSION}-win-x64")
    portable = os.path.join(DIST, f"TibiaFoundry-{VERSION}-portable-win-x64")

    # The source archive is the committed tree: refuse to build from uncommitted changes to tracked files.
    dirty = [l for l in git("status", "--porcelain", "--untracked-files=no").splitlines() if l.strip()]
    if dirty:
        sys.exit("Commit first; uncommitted changes to tracked files:\n  " + "\n  ".join(dirty[:20]))

    if "--skip-publish" not in args:
        print("1. Publishing", flush=True)
        if os.path.exists(DIST):
            shutil.rmtree(DIST)
        run(["dotnet", "publish", "TibiaFoundry.App/TibiaFoundry.App.csproj", "-c", "Release", "-r", "win-x64", "--self-contained", "true",
             "-p:PublishReadyToRun=true", "-o", f"../dist/TibiaFoundry-{VERSION}-win-x64", "-nologo"], cwd=os.path.join(ROOT, "src"))
        run(["dotnet", "publish", "TibiaFoundry.App/TibiaFoundry.App.csproj", "-p:PublishProfile=Portable", "-nologo"], cwd=os.path.join(ROOT, "src"))
        print("2. Notices, edition archives and installer (tools/release)", flush=True)
        run([sys.executable, "-I", "tools/release/package_release.py"])
        run([sys.executable, "-I", "tools/release/build_installer.py"])

    print("3. Release archives", flush=True)
    msi_built = os.path.join(DIST, f"TibiaFoundry-{VERSION}-win-x64.msi")
    msi = os.path.join(OUT, f"TibiaFoundry-{VERSION}.msi")
    shutil.copyfile(msi_built, msi)
    with new_zip(os.path.join(OUT, f"TibiaFoundry-{VERSION}-MSI.zip")) as z:
        z.write(msi, f"TibiaFoundry-{VERSION}.msi")

    # Portable: the single executable, its notices and README (its first line names this version).
    readme = open(os.path.join(portable, "README-Portable.txt"), encoding="utf-8").read().replace("\r\n", "\n").split("\n")
    readme[0] = re.sub(r"Tibia Foundry \S+ - ", f"Tibia Foundry {VERSION} - ", readme[0])
    open(os.path.join(portable, "README-Portable.txt"), "w", encoding="utf-8", newline="\r\n").write("\n".join(readme))
    with new_zip(os.path.join(OUT, f"TibiaFoundry-{VERSION}-Portable.zip")) as z:
        add_tree(z, portable, "", skip=("TibiaFoundry-Data",))

    # Source: exactly what git tracks at HEAD (no local notes, release outputs or build folders), minus the built site.
    tracked = [f for f in git("ls-files", "-z").split("\0") if f]
    top = f"TibiaFoundry-{VERSION}-Source"
    with new_zip(os.path.join(OUT, f"TibiaFoundry-{VERSION}-Source.zip")) as z:
        for f in sorted(tracked):
            if f.startswith("website/site/") or not os.path.isfile(os.path.join(ROOT, f)):
                continue
            z.write(os.path.join(ROOT, f), f"{top}/{f}")

    sums = {}
    for name in (f"TibiaFoundry-{VERSION}-MSI.zip", f"TibiaFoundry-{VERSION}-Portable.zip", f"TibiaFoundry-{VERSION}-Source.zip"):
        sums[name] = sha256(os.path.join(OUT, name))
    with open(os.path.join(OUT, "SHA256SUMS.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("".join(f"{v}  {k}\n" for k, v in sums.items()))

    if "--skip-site" not in args:
        print("4. Website (with the archives' checksums on the Downloads page)", flush=True)
        run([sys.executable, "-I", "website/build_site.py", "--strict", "--checksums", os.path.join(OUT, "SHA256SUMS.txt")])
    wtop = f"TibiaFoundry-{VERSION}-Website"
    with new_zip(os.path.join(OUT, f"TibiaFoundry-{VERSION}-Website.zip")) as z:
        add_tree(z, os.path.join(ROOT, "website"), f"{wtop}/website", skip=("site/CNAME", "site/robots.txt"))
        z.write(os.path.join(ROOT, "src", "Directory.Build.props"), f"{wtop}/src/Directory.Build.props")
        res = os.path.join(ROOT, "src", "TibiaFoundry.Core", "Localization", "Resources")
        for f in sorted(os.listdir(res)):
            if re.fullmatch(r"Strings\.[\w-]+\.xaml", f):
                z.write(os.path.join(res, f), f"{wtop}/src/TibiaFoundry.Core/Localization/Resources/{f}")
        links = os.path.join(ROOT, "src", "TibiaFoundry.App", "Assets", "Links")
        for f in sorted(os.listdir(links)):
            z.write(os.path.join(links, f), f"{wtop}/src/TibiaFoundry.App/Assets/Links/{f}")
        z.writestr(f"{wtop}/README.txt", WEBSITE_README.replace("{v}", VERSION).replace("\n", "\r\n"))

    print("5. Checksums", flush=True)
    names = [f"TibiaFoundry-{VERSION}-{k}.zip" for k in ("Source", "Portable", "MSI", "Website")] + [f"TibiaFoundry-{VERSION}.msi"]
    lines = [f"{sha256(os.path.join(OUT, n))}  {n}" for n in names]
    with open(os.path.join(OUT, "SHA256SUMS.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    for n in names:
        print(f"  {n}: {os.path.getsize(os.path.join(OUT, n)):,} bytes")
    print("\n".join("  " + l for l in lines))


WEBSITE_README = """Tibia Foundry {v} - website
==============================

tibiafoundry.com: a static site in five languages (English, Português (Brasil), Polski, Español, Deutsch) with light
and dark themes and online Tibia data from TibiaData (https://tibiadata.com/). No server code, no build step on the
host: GitHub Pages serves website/site as it is.

  website/site/          the built site, ready to deploy (copy its contents to the Pages repository root;
                         keep that repository's own CNAME and robots.txt - they are not in this archive)
  website/src/           sources: registry.json (pages, routes, downloads, profiles), providers.json (data credits),
                         templates/, locales/, styles.css, js/, images/ (program screenshots in every language and theme)
  website/build_site.py  builds website/site from website/src (Python 3.11+ with Pillow)
  website/screenshots/   regenerates the program screenshots from the running program (Windows)
  website/tests/         locale parity, API client unit tests, Playwright end-to-end tests, the website screenshot
                         matrix and the recorded TibiaData fixtures they use (never deployed)
  src/                   the program files the build reads: the version (Directory.Build.props), the program's
                         interface texts (Strings.<code>.xaml) and two link icons

Rebuild (from the folder that contains website/ and src/):
  pip install pillow
  python -I website/build_site.py --strict

Test:
  python -I website/tests/check_i18n.py
  node --test website/tests/unit/tibiadata.test.mjs
  pip install playwright && python -m playwright install chromium
  python website/tests/e2e.py
  python website/tests/site_screenshots.py --out qa-screenshots

See website/README.md for details, website/TIBIADATA.md for the API integration and website/ATTRIBUTION.md for the
data and resource credits.
"""

if __name__ == "__main__":
    main()
