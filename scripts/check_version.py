"""
Version verification for a Tibia Foundry release (run from the repository root):

    python -I scripts/check_version.py [--expect 9.0.5] [--future 9.0.6] [--ui] [--msi path] [--zips folder]

1. The single source: <Version> in src/Directory.Build.props (and --expect, when given).
2. Derived values: the Windows four-part version, the built assembly (file, product and assembly version), the
   app.manifest the build stamped, the header text the program renders (from --ui-snapshot reports when present).
3. Stale references: active source, packaging and website files must not name an older release version (history
   files - changelog, release notes, audits - are allowed to).
4. --future V: copies src/ to a temporary folder, sets <Version>V</Version> there (the only edit a release makes),
   builds it and checks that the program's file/product versions, the stamped manifest and - with --ui - the
   rendered window header ("Tibia Foundry vV", from the program's own --ui-snapshot check) all follow.
5. --msi: the ProductVersion of a built installer; --zips: release archive names.
Exit code 1 when anything disagrees.
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = os.getcwd()
SRC = os.path.join(ROOT, "src")
problems = []


def ok(name, cond, detail=""):
    print(f"{'PASS' if cond else 'FAIL'}  {name}" + (f"  ({detail})" if detail else ""))
    if not cond:
        problems.append(name)


def props_version(src=SRC):
    return re.search(r"<Version>([^<]+)</Version>", open(os.path.join(src, "Directory.Build.props"), encoding="utf-8").read()).group(1)


def padded(v):
    parts = [int(x) for x in v.split(".")] + [0, 0, 0]
    return ".".join(str(x) for x in parts[:3]) + ".0"


def file_versions(path):
    """FileVersion and ProductVersion of a Windows executable (PowerShell reads the version resource)."""
    ps = f"$v=(Get-Item -LiteralPath '{path}').VersionInfo; \"$($v.FileVersion)|$($v.ProductVersion)\""
    out = subprocess.run(["powershell", "-NoProfile", "-Command", ps], capture_output=True, text=True).stdout.strip()
    return tuple(out.split("|")) if "|" in out else (None, None)


def msi_product_version(path):
    ps = ("$i=New-Object -ComObject WindowsInstaller.Installer; "
          f"$d=$i.GetType().InvokeMember('OpenDatabase','InvokeMethod',$null,$i,@('{path}',0)); "
          "$v=$d.GetType().InvokeMember('OpenView','InvokeMethod',$null,$d,@(\"SELECT Value FROM Property WHERE Property='ProductVersion'\")); "
          "$v.GetType().InvokeMember('Execute','InvokeMethod',$null,$v,$null); "
          "$r=$v.GetType().InvokeMember('Fetch','InvokeMethod',$null,$v,$null); "
          "$r.GetType().InvokeMember('StringData','GetProperty',$null,$r,1)")
    return subprocess.run(["powershell", "-NoProfile", "-Command", ps], capture_output=True, text=True).stdout.strip()


ACTIVE = [  # (folder or file, extensions) - files that describe the current release
    ("src", (".cs", ".xaml", ".csproj", ".props", ".manifest", ".pubxml", ".json")),
    ("tools/release", (".py", ".wxs", ".txt")),
    ("website/src", (".html", ".json", ".js", ".css")),
    ("website/build_site.py", ()),
    ("README.md", ()), ("README-SOURCE.txt", ()), ("docs/06_Build_and_Setup.md", ()),
]
SKIP_FILES = ("Directory.Packages.props", "global.json", "packages.lock.json")
SKIP_DIRS = ("bin", "obj", "TestResults", "fixtures", "results", "TibiaFoundry.Tests")
COMMENT = re.compile(r"^\s*(//|#|<!--|\*|/\*|rem\b)")


def released_versions():
    """Versions named in CHANGELOG.md headings ("## 9.0.4 ...", "## [9.0.4]")."""
    path = os.path.join(ROOT, "CHANGELOG.md")
    if not os.path.exists(path):
        return set()
    return set(re.findall(r"^#+\s*\[?v?(\d+\.\d+(?:\.\d+)?)", open(path, encoding="utf-8").read(), re.M))


def stale_references(current):
    """Earlier release versions of the current major.minor line (e.g. 9.0.4 when releasing 9.0.5), in any form
    (9.0.4, v9.0.4, 9.0.4.0), in active files. Lines that describe history ("since 9.0.4", "9.0.4:" notes) are allowed."""
    cur = tuple(int(x) for x in (current.split(".") + ["0"])[:3])
    older = {r for r in released_versions() | {f"{cur[0]}.{cur[1]}.{p}" for p in range(1, cur[2])}
             if tuple(int(x) for x in (r.split(".") + ["0"])[:3]) < cur and r.startswith(f"{cur[0]}.{cur[1]}.")}
    if not older:
        return []
    # The release this one replaces is what goes stale; earlier ones only appear as history and examples.
    older = {max(older, key=lambda r: tuple(int(x) for x in (r.split(".") + ["0"])[:3]))}
    pattern = re.compile(r"(?<![\w.])v?(" + "|".join(re.escape(o) for o in sorted(older, key=len, reverse=True)) + r")(?:\.0)?(?![\w.]*\d)")
    found = []
    for entry, exts in ACTIVE:
        full = os.path.join(ROOT, entry)
        files = [full] if os.path.isfile(full) else [os.path.join(d, f) for d, ds, fs in os.walk(full)
                                                       if not any(s in d.replace("\\", "/").split("/") for s in SKIP_DIRS)
                                                       for f in fs if f.endswith(exts) and f not in SKIP_FILES]
        for path in files:
            for n, line in enumerate(open(path, encoding="utf-8", errors="replace").read().splitlines(), 1):
                if COMMENT.match(line) or re.search(r"Audit\.md|ReleaseNotes\.md|TestingReport\.md|\"fix\d+\"", line):
                    continue
                if re.search(r"\b(since|introduced|added in|changed in|until|before|from|history|legacy|deprecat|earlier|previous)\b|\(\d+\.\d+(\.\d+)?\)|^\s*(//|#|<!--|\*|///).*\d+\.\d+(\.\d+)?:", line, re.I):
                    continue
                for m in pattern.finditer(line):
                    found.append(f"{os.path.relpath(path, ROOT)}:{n}: {m.group(0)}: {line.strip()[:120]}")
    return found


def build(src, out, extra=()):
    r = subprocess.run(["dotnet", "build", os.path.join(src, "TibiaFoundry.App", "TibiaFoundry.App.csproj"), "-c", "Release",
                        "-nologo", "-v", "q", "-o", out, *extra], capture_output=True, text=True)
    return r.returncode == 0, (r.stdout + r.stderr)[-2000:]


def ui_header(exe, folder):
    subprocess.run([exe, "--ui-snapshot", folder, "--theme", "dark"], capture_output=True, timeout=1800)
    report = os.path.join(folder, "report.txt")
    if not os.path.exists(report):
        return None, []
    text = open(report, encoding="utf-8", errors="replace").read()
    m = re.search(r"^HEADER-TITLE: (.*)$", text, re.M)
    return (m.group(1).strip() if m else None), [l for l in text.splitlines() if l.startswith("FAIL")]


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace", line_buffering=True)
    ap = argparse.ArgumentParser()
    ap.add_argument("--expect")
    ap.add_argument("--future")
    ap.add_argument("--ui", action="store_true")
    ap.add_argument("--msi")
    ap.add_argument("--zips")
    ap.add_argument("--json")
    a = ap.parse_args()

    v = props_version()
    print(f"Directory.Build.props <Version>: {v}  (Windows: {padded(v)})")
    if a.expect:
        ok(f"configured version is {a.expect}", v == a.expect, v)
    ok("version has the release form major.minor[.patch]", re.fullmatch(r"\d+\.\d+(\.\d+)?", v) is not None, v)

    bin_dir = os.path.join(SRC, "TibiaFoundry.App", "bin", "Release", "net10.0-windows", "win-x64")
    exe = os.path.join(bin_dir, "TibiaFoundry.exe")
    if os.path.exists(exe):
        fv, pv = file_versions(os.path.join(bin_dir, "TibiaFoundry.dll"))
        ok("built TibiaFoundry.dll file version", fv == padded(v), str(fv))
        ok("built TibiaFoundry.dll product version", pv == v, str(pv))
        fv, pv = file_versions(exe)
        ok("built TibiaFoundry.exe file version", fv == padded(v), str(fv))
    else:
        print(f"note  no Release build at {bin_dir} (run dotnet build -c Release)")
    stamped = [os.path.join(d, f) for d, _, fs in os.walk(os.path.join(SRC, "TibiaFoundry.App", "obj", "Release")) for f in fs if f == "app.manifest"]
    if stamped:
        ok("stamped app.manifest", f'assemblyIdentity version="{padded(v)}"' in open(stamped[0], encoding="utf-8").read(), stamped[0])
    ok("app.manifest in source holds no version", 'assemblyIdentity version="0.0.0.0"' in open(os.path.join(SRC, "TibiaFoundry.App", "app.manifest"), encoding="utf-8").read())
    window = open(os.path.join(SRC, "TibiaFoundry.App", "Views", "MainWindow.xaml"), encoding="utf-8").read()
    ok("window header bound to AppIdentity.HeaderTitle", 'Text="{x:Static common:AppIdentity.HeaderTitle}"' in window)
    brand = re.search(r'<StackPanel x:Name="BrandText".*?</StackPanel>', window, re.S).group(0)
    ok("window header contains no version literal", not re.search(r"\d+\.\d+\.\d+", brand))
    ok("installer takes its version from Directory.Build.props",
       'Version="$(var.Version)"' in open(os.path.join(ROOT, "tools", "release", "installer", "TibiaFoundry.wxs"), encoding="utf-8").read())

    stale = stale_references(v)
    ok("no stale release versions in active files", not stale, f"{len(stale)} found")
    for s in stale[:40]:
        print("      " + s)

    snapshot_root = os.path.join(ROOT, "Release", v, "qa-desktop", "ui-snapshot")
    if os.path.exists(os.path.join(snapshot_root, "report.txt")):
        text = open(os.path.join(snapshot_root, "report.txt"), encoding="utf-8", errors="replace").read()
        m = re.search(r"^HEADER-TITLE: (.*)$", text, re.M)
        ok("rendered header (program's --ui-snapshot)", m is not None and m.group(1).strip() == f"Tibia Foundry v{v}", m.group(1) if m else "no HEADER-TITLE line")

    if a.msi:
        mv = msi_product_version(os.path.abspath(a.msi))
        ok(f"MSI ProductVersion ({os.path.basename(a.msi)})", mv == v, mv)
    if a.zips:
        names = sorted(os.listdir(a.zips))
        for kind in ("Source", "Portable", "MSI", "Website"):
            ok(f"archive TibiaFoundry-{v}-{kind}.zip", f"TibiaFoundry-{v}-{kind}.zip" in names)

    if a.future:
        tmp = tempfile.mkdtemp(prefix="tf-future-")
        try:
            src = os.path.join(tmp, "src")
            shutil.copytree(SRC, src, ignore=shutil.ignore_patterns("bin", "obj", "TestResults"))
            props = os.path.join(src, "Directory.Build.props")
            text = open(props, encoding="utf-8").read()
            open(props, "w", encoding="utf-8", newline="").write(text.replace(f"<Version>{v}</Version>", f"<Version>{a.future}</Version>"))
            out = os.path.join(tmp, "out")
            built, log = build(src, out)
            ok(f"future version {a.future}: builds with only <Version> changed", built, "" if built else log)
            if built:
                fv, pv = file_versions(os.path.join(out, "TibiaFoundry.dll"))
                ok(f"future version {a.future}: product version", pv == a.future, str(pv))
                ok(f"future version {a.future}: file version", fv == padded(a.future), str(fv))
                man = [os.path.join(d, f) for d, _, fs in os.walk(os.path.join(src, "TibiaFoundry.App", "obj")) for f in fs if f == "app.manifest"]
                ok(f"future version {a.future}: stamped manifest", bool(man) and f'version="{padded(a.future)}"' in open(man[0], encoding="utf-8").read())
                ok(f"future version {a.future}: header XAML untouched",
                   open(os.path.join(src, "TibiaFoundry.App", "Views", "MainWindow.xaml"), encoding="utf-8").read() == window)
                if a.ui:
                    header, fails = ui_header(os.path.join(out, "TibiaFoundry.exe"), os.path.join(tmp, "ui"))
                    ok(f"future version {a.future}: rendered header", header == f"Tibia Foundry v{a.future}", str(header))
                    if fails:
                        print("      program self-check failures: " + "; ".join(fails[:5]))
        finally:
            shutil.rmtree(tmp, ignore_errors=True)

    if a.json:
        json.dump({"version": v, "problems": problems}, open(a.json, "w", encoding="utf-8"), indent=1)
    print(f"\n{'OK' if not problems else 'FAILED'}: {len(problems)} problem(s)")
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
