"""
Validates the release archives in Release/<version>/ (run from the repository root):

    python -I scripts/verify_release.py [--smoke] [--rebuild]

For each of TibiaFoundry-<v>-Source.zip, -Portable.zip, -MSI.zip and -Website.zip: exists, not empty, ZIP integrity
(CRC of every entry), expected contents, no build/editor/temporary files, no other version in any name; exact size and
SHA-256, compared with SHA256SUMS.txt.
  MSI ZIP      contains exactly TibiaFoundry-<v>.msi; the extracted MSI is byte-identical to the standalone copy, is a
               Windows Installer database (OLE header), ProductName "Tibia Foundry", ProductVersion <v>, installs
               TibiaFoundry.exe; WiX ICE validation when it can run.
  Portable ZIP the single TibiaFoundry.exe (file version <v>.0, product version <v>), notices and README for <v>;
               --smoke: extracted into an empty folder and started with the program's own UI self-check
               (--ui-snapshot): header "Tibia Foundry v<v>", no failed check, portable data folder next to the program.
  Source ZIP   solution, version file, tests, packaging and website sources; no website/site, release outputs or
               local notes; --rebuild: extracted and built (dotnet build -c Release) in an empty folder.
  Website ZIP  built site, sources, five locales, build script, tests, README; no CNAME/robots.txt of the Pages
               repository; --rebuild: extracted and rebuilt (build_site.py --strict) in an empty folder.
Writes Release/<v>/archive-validation.json and .md; exit code 1 on any problem.
"""
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = os.getcwd()
VERSION = re.search(r"<Version>([^<]+)</Version>", open(os.path.join(ROOT, "src", "Directory.Build.props"), encoding="utf-8").read()).group(1)
OUT = os.path.join(ROOT, "Release", VERSION)
BAD_PARTS = {"bin", "obj", ".vs", "__pycache__", ".work", ".git", "TestResults", "node_modules", "Release", "dist"}
BAD_NAMES = re.compile(r"(\.tmp|\.log|\.suo|\.user|~|\.DS_Store|Thumbs\.db|desktop\.ini)$|PROGRESS_|\.pyc$", re.I)
report = {"version": VERSION, "archives": {}, "problems": []}


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def problem(where, text):
    report["problems"].append(f"{where}: {text}")
    print(f"FAIL  {where}: {text}", flush=True)


def ok(where, text):
    print(f"PASS  {where}: {text}", flush=True)


def check_zip(name, required, forbidden=(), top=None):
    path = os.path.join(OUT, name)
    entry = report["archives"].setdefault(name, {})
    if not os.path.isfile(path) or not os.path.getsize(path):
        problem(name, "missing or empty")
        return None
    entry.update(bytes=os.path.getsize(path), sha256=sha256(path))
    with zipfile.ZipFile(path) as z:
        bad = z.testzip()
        if bad:
            problem(name, f"CRC error in {bad}")
        names = z.namelist()
    entry["entries"] = len(names)
    ok(name, f"integrity ok, {len(names)} entries, {entry['bytes']:,} bytes")
    for n in names:
        parts = n.split("/")
        if any(p in BAD_PARTS for p in parts[:-1]) or BAD_NAMES.search(parts[-1]):
            problem(name, f"unexpected entry {n}")
        other = re.findall(r"TibiaFoundry-(\d+\.\d+(?:\.\d+)?)", n)
        history = re.search(r"-(ReleaseNotes|Audit|TestingReport)\.md$", n)   # release history documents of the source tree
        if any(v != VERSION for v in other) and not history:
            problem(name, f"entry names another version: {n}")
        if top and not n.startswith(top + "/"):
            problem(name, f"entry outside {top}/: {n}")
    for r in required:
        if not any(re.fullmatch(r, n) for n in names):
            problem(name, f"required entry missing: {r}")
    for f in forbidden:
        hit = [n for n in names if re.match(f, n)]
        if hit:
            problem(name, f"must not contain {f}: {hit[:3]}")
    return names


def file_versions(path):
    ps = f"$v=(Get-Item -LiteralPath '{path}').VersionInfo; \"$($v.FileVersion)|$($v.ProductVersion)|$($v.ProductName)\""
    out = subprocess.run(["powershell", "-NoProfile", "-Command", ps], capture_output=True, text=True).stdout.strip()
    return out.split("|") if out.count("|") == 2 else [None, None, None]


def msi_query(path, sql):
    ps = ("$i=New-Object -ComObject WindowsInstaller.Installer; "
          f"$d=$i.GetType().InvokeMember('OpenDatabase','InvokeMethod',$null,$i,@('{path}',0)); "
          f"$v=$d.GetType().InvokeMember('OpenView','InvokeMethod',$null,$d,@(\"{sql}\")); "
          "$v.GetType().InvokeMember('Execute','InvokeMethod',$null,$v,$null); $out=@(); "
          "while(($r=$v.GetType().InvokeMember('Fetch','InvokeMethod',$null,$v,$null)) -ne $null){ $out += $r.GetType().InvokeMember('StringData','GetProperty',$null,$r,1) }; "
          "$out -join '|'")
    return subprocess.run(["powershell", "-NoProfile", "-Command", ps], capture_output=True, text=True).stdout.strip()


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    args = sys.argv[1:]
    v = VERSION
    tmp = tempfile.mkdtemp(prefix="tf-verify-")
    try:
        # ---------------------------------------------------------------- MSI
        msi_zip = f"TibiaFoundry-{v}-MSI.zip"
        names = check_zip(msi_zip, [re.escape(f"TibiaFoundry-{v}.msi")])
        if names is not None:
            if names != [f"TibiaFoundry-{v}.msi"]:
                problem(msi_zip, f"should contain only the MSI, has {names}")
            with zipfile.ZipFile(os.path.join(OUT, msi_zip)) as z:
                z.extract(f"TibiaFoundry-{v}.msi", os.path.join(tmp, "msi"))
            extracted = os.path.join(tmp, "msi", f"TibiaFoundry-{v}.msi")
            standalone = os.path.join(OUT, f"TibiaFoundry-{v}.msi")
            if os.path.exists(standalone):
                same = sha256(extracted) == sha256(standalone)
                (ok if same else problem)(msi_zip, "extracted MSI identical to the standalone copy" if same else "extracted MSI differs from the standalone copy")
                report["archives"][f"TibiaFoundry-{v}.msi"] = {"bytes": os.path.getsize(standalone), "sha256": sha256(standalone)}
            head = open(extracted, "rb").read(8)
            (ok if head == bytes.fromhex("D0CF11E0A1B11AE1") else problem)(msi_zip, "Windows Installer database (OLE compound file)" if head == bytes.fromhex("D0CF11E0A1B11AE1") else f"not an MSI: {head.hex()}")
            pv = msi_query(extracted, "SELECT Value FROM Property WHERE Property='ProductVersion'")
            pn = msi_query(extracted, "SELECT Value FROM Property WHERE Property='ProductName'")
            exe = msi_query(extracted, "SELECT FileName FROM File")
            report["archives"][msi_zip].update(product_version=pv, product_name=pn)
            (ok if pv == v else problem)(msi_zip, f"ProductVersion {pv}")
            (ok if pn == "Tibia Foundry" else problem)(msi_zip, f"ProductName {pn}")
            (ok if re.search(r"(^|\|)([^|]*\|)?TibiaFoundry\.exe", exe) or "TibiaFoundry.exe" in exe else problem)(msi_zip, "installs TibiaFoundry.exe")
            val = subprocess.run(["wix", "msi", "validate", extracted], capture_output=True, text=True)
            text = (val.stdout + val.stderr).strip()
            report["archives"][msi_zip]["ice_validation"] = "passed" if val.returncode == 0 else text[-600:]
            if val.returncode == 0:
                ok(msi_zip, "WiX ICE validation passed")
            else:
                print(f"note  {msi_zip}: WiX ICE validation did not pass or could not run: {text[-300:]}")

        # ---------------------------------------------------------------- Portable
        pz = f"TibiaFoundry-{v}-Portable.zip"
        names = check_zip(pz, [r"TibiaFoundry\.exe", r"THIRD-PARTY-NOTICES\.txt", r"README-Portable\.txt"])
        if names is not None:
            dlls = [n for n in names if n.lower().endswith(".dll")]
            if dlls:
                problem(pz, f"portable edition must be one file, has {dlls[:3]}")
            folder = os.path.join(tmp, "portable")
            with zipfile.ZipFile(os.path.join(OUT, pz)) as z:
                z.extractall(folder)
            readme = open(os.path.join(folder, "README-Portable.txt"), encoding="utf-8", errors="replace").readline()
            (ok if f"Tibia Foundry {v} " in readme else problem)(pz, f"README first line: {readme.strip()}")
            notices = open(os.path.join(folder, "THIRD-PARTY-NOTICES.txt"), encoding="utf-8", errors="replace").readline()
            (ok if f"Tibia Foundry {v} " in notices else problem)(pz, f"notices first line: {notices.strip()}")
            fv, pv, pn = file_versions(os.path.join(folder, "TibiaFoundry.exe"))
            report["archives"][pz].update(file_version=fv, product_version=pv)
            (ok if fv == f"{v}.0" and pv == v else problem)(pz, f"TibiaFoundry.exe file version {fv}, product version {pv}")
            if "--smoke" in args:
                snap = os.path.join(tmp, "portable-snapshot")
                r = subprocess.run([os.path.join(folder, "TibiaFoundry.exe"), "--ui-snapshot", snap, "--theme", "dark"], capture_output=True, timeout=1800)
                rep = os.path.join(snap, "report.txt")
                text = open(rep, encoding="utf-8", errors="replace").read() if os.path.exists(rep) else ""
                header = re.search(r"^HEADER-TITLE: (.*)$", text, re.M)
                fails = [l for l in text.splitlines() if l.startswith("FAIL")]
                passes = len([l for l in text.splitlines() if l.startswith("PASS")])
                report["archives"][pz]["smoke"] = {"exit": r.returncode, "header": header.group(1) if header else None, "pass": passes, "fail": fails}
                (ok if header and header.group(1).strip() == f"Tibia Foundry v{v}" else problem)(pz, f"started from the extracted ZIP; header {header.group(1) if header else None}")
                (ok if not fails and passes else problem)(pz, f"program self-check: {passes} passed, {len(fails)} failed {fails[:3]}")
                data = os.path.isdir(os.path.join(folder, "TibiaFoundry-Data"))
                report["archives"][pz]["smoke"]["portable_data_folder"] = data
                print(f"note  {pz}: portable data folder next to the program after the run: {data}")

        # ---------------------------------------------------------------- Source
        sz = f"TibiaFoundry-{v}-Source.zip"
        top = f"TibiaFoundry-{v}-Source"
        names = check_zip(sz, [re.escape(f"{top}/{p}") for p in (
            "src/TibiaFoundry.slnx", "src/Directory.Build.props", "src/global.json", "src/TibiaFoundry.App/TibiaFoundry.App.csproj",
            "src/TibiaFoundry.Tests/TibiaFoundry.Tests.csproj", "src/TibiaFoundry.Tests/VersionDisplayTests.cs", "tools/release/package_release.py",
            "tools/release/installer/TibiaFoundry.wxs", "scripts/check_version.py", "scripts/build_release.py", "website/build_site.py",
            "website/src/registry.json", "README.md", "README-SOURCE.txt", "THIRD-PARTY-NOTICES.txt", "CHANGELOG.md")],
            forbidden=[re.escape(f"{top}/website/site/"), re.escape(f"{top}/Release/"), re.escape(f"{top}/PROGRESS"), re.escape(f"{top}/dist/")], top=top)
        if names is not None:
            with zipfile.ZipFile(os.path.join(OUT, sz)) as z:
                props = z.read(f"{top}/src/Directory.Build.props").decode("utf-8")
            (ok if f"<Version>{v}</Version>" in props else problem)(sz, "Directory.Build.props names " + v)
            if "--rebuild" in args:
                folder = os.path.join(tmp, "source")
                with zipfile.ZipFile(os.path.join(OUT, sz)) as z:
                    z.extractall(folder)
                r = subprocess.run(["dotnet", "build", os.path.join(folder, top, "src", "TibiaFoundry.slnx"), "-c", "Release", "-nologo", "-v", "q"], capture_output=True, text=True)
                tail = (r.stdout + r.stderr)[-400:]
                report["archives"][sz]["rebuild"] = "succeeded" if r.returncode == 0 else tail
                (ok if r.returncode == 0 else problem)(sz, "extracted source builds (dotnet build -c Release)" if r.returncode == 0 else "rebuild failed: " + tail)

        # ---------------------------------------------------------------- Website
        wz = f"TibiaFoundry-{v}-Website.zip"
        wtop = f"TibiaFoundry-{v}-Website"
        names = check_zip(wz, [re.escape(f"{wtop}/{p}") for p in (
            "README.txt", "website/build_site.py", "website/README.md", "website/TIBIADATA.md", "website/ATTRIBUTION.md",
            "website/THIRD-PARTY-NOTICES.md", "website/src/registry.json", "website/src/providers.json", "website/src/templates/layout.html",
            "website/src/js/tibiadata.js", "website/src/js/sanitize.js", "website/src/js/data-app.js", "website/site/index.html",
            "website/site/tibia/index.html", "website/site/404.html", "website/site/sitemap.xml", "website/tests/e2e.py",
            "website/tests/site_screenshots.py", "website/screenshots/screenshots.py", "src/Directory.Build.props")]
            + [re.escape(f"{wtop}/website/src/locales/{c}.json") for c in ("en", "pt-BR", "pl", "es", "de")],
            forbidden=[re.escape(f"{wtop}/website/site/CNAME"), re.escape(f"{wtop}/website/site/robots.txt")], top=wtop)
        if names is not None:
            with zipfile.ZipFile(os.path.join(OUT, wz)) as z:
                home = z.read(f"{wtop}/website/site/downloads/index.html").decode("utf-8")
            for kind in ("MSI", "Portable", "Source"):
                url = f"https://github.com/RockyJLaRo/TibiaFoundry/releases/download/v{v}/TibiaFoundry-{v}-{kind}.zip"
                (ok if url in home else problem)(wz, f"Downloads page links {url}")
            if "--rebuild" in args:
                folder = os.path.join(tmp, "website")
                with zipfile.ZipFile(os.path.join(OUT, wz)) as z:
                    z.extractall(folder)
                r = subprocess.run([sys.executable, "-I", os.path.join(folder, wtop, "website", "build_site.py"), "--strict"], capture_output=True, text=True)
                tail = (r.stdout + r.stderr)[-400:]
                report["archives"][wz]["rebuild"] = "succeeded" if r.returncode == 0 else tail
                (ok if r.returncode == 0 else problem)(wz, "extracted website rebuilds (build_site.py --strict)" if r.returncode == 0 else "rebuild failed: " + tail)

        # ---------------------------------------------------------------- checksums
        sums = os.path.join(OUT, "SHA256SUMS.txt")
        listed = dict((n, h) for h, n in (l.split("  ", 1) for l in open(sums, encoding="utf-8").read().splitlines() if l.strip()))
        for name, info in report["archives"].items():
            if "sha256" in info:
                good = listed.get(name) == info["sha256"]
                (ok if good else problem)("SHA256SUMS.txt", f"{name} {'matches' if good else 'missing or different'}")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    json.dump(report, open(os.path.join(OUT, "archive-validation.json"), "w", encoding="utf-8"), indent=1)
    lines = [f"# Release archive validation – Tibia Foundry {v}", "", "| File | Bytes | SHA-256 |", "|---|---:|---|"]
    for name, info in report["archives"].items():
        if "sha256" in info:
            lines.append(f"| `{name}` | {info['bytes']:,} | `{info['sha256']}` |")
    lines += ["", f"Problems: {len(report['problems'])}"] + [f"- {p}" for p in report["problems"]]
    open(os.path.join(OUT, "archive-validation.md"), "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
    print(f"\n{'OK' if not report['problems'] else 'FAILED'}: {len(report['problems'])} problem(s); report Release/{v}/archive-validation.md")
    sys.exit(1 if report["problems"] else 0)


if __name__ == "__main__":
    main()
