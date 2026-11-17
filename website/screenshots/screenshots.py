"""
Website screenshot generator for tibiafoundry.com: captures every screenshot the website shows, in every
language of the website and in both themes, from the real program, and keeps the library, its inventory
and its coverage report up to date.

    python -I website/screenshots/screenshots.py capture                 everything (build, capture, site, validate)
    python -I website/screenshots/screenshots.py capture --incremental   only screenshots that are missing or outdated
    python -I website/screenshots/screenshots.py capture --pages home,infobox --languages de --themes dark
    python -I website/screenshots/screenshots.py status                  what is missing or outdated, and why
    python -I website/screenshots/screenshots.py validate                files, checks and the built site's references
    python -I website/screenshots/screenshots.py sheets                  contact sheets for visual review
    python -I website/screenshots/screenshots.py approve --pages home    record a visual review of the current files
    python -I website/screenshots/screenshots.py report                  rewrite inventory.md and coverage-report.md

Nothing is hard-coded here: the languages come from website/build_site.py (LANGUAGES, checked against the program's
Strings.<code>.xaml), the screenshot locations from the page template, the gallery list (build_site.SCREENS) and
the derived crops (build_site.DERIVED), and the shots the program can take from its own list in SiteScreenshots.cs.
Full documentation: website/screenshots/README.md. Needs Pillow and NumPy, the .NET SDK and Windows.
"""
import argparse
import datetime
import glob
import hashlib
import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys
import time

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
WEBSITE = os.path.dirname(HERE)
ROOT = os.path.dirname(WEBSITE)
SRC = os.path.join(WEBSITE, "src")
SHOTS = os.path.join(SRC, "images", "screens")       # the library: screens/<code>/<id>-<code>-<theme>.png
SITE = os.path.join(WEBSITE, "site")
WORK = os.path.join(HERE, ".work")                    # raw captures, diffs, contact sheets (not committed)
MANIFEST = os.path.join(HERE, "manifest.json")
THEMES = ("dark", "light")


# ============================================================================ configuration and discovery
def load_config():
    cfg = json.load(open(os.path.join(HERE, "config.json"), encoding="utf-8"))
    return cfg


def repo_path(p):
    p = os.path.expandvars(p)
    return p if os.path.isabs(p) else os.path.join(ROOT, p)


def load_build_site():
    spec = importlib.util.spec_from_file_location("build_site", os.path.join(WEBSITE, "build_site.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def shot_file(sid, code, theme):
    """The library file of one screenshot (the naming convention: <id>-<code>-<theme>.png in screens/<code>/)."""
    return os.path.join(SHOTS, code, f"{sid}-{code}-{theme}.png")


def rel(path):
    return os.path.relpath(path, ROOT).replace("\\", "/")


class Inventory:
    """Everything that defines the screenshot library, discovered from the project itself."""

    def __init__(self, cfg):
        self.cfg = cfg
        bs = load_build_site()
        self.build_site = bs
        self.languages = [l[0] for l in bs.LANGUAGES]
        self.language_names = {l[0]: l[2] for l in bs.LANGUAGES}
        self.problems, self.notes = [], []

        # Languages: the website's list, each with its locale file and the program's own translation.
        for code in self.languages:
            if not os.path.exists(os.path.join(SRC, "locales", code + ".json")):
                self.problems.append(f"language {code}: website/src/locales/{code}.json is missing")
            if not os.path.exists(self.language_file(code)):
                self.problems.append(f"language {code}: the program has no {rel(self.language_file(code))}, "
                                     "so its screenshots cannot show the program in that language")
        app_langs = sorted(re.findall(r"Strings\.([\w-]+)\.xaml",
                                      " ".join(os.listdir(os.path.dirname(self.language_file("en"))))))
        extra = [c for c in app_langs if c not in self.languages]
        if extra:
            self.notes.append("the program also speaks " + ", ".join(extra) + ", which the website does not offer yet")

        # Shots the program can take (its own list).
        src = open(repo_path(cfg["app"]["shot_list"]), encoding="utf-8-sig").read()
        self.app_shots = dict(re.findall(r'new\("([\w-]+)",\s*typeof\((\w+)\)', src))

        # Where the website uses screenshots.
        self.derived = {name.rsplit("-", 1)[0]: (srcfile.rsplit("-", 1)[0], box) for name, srcfile, box in bs.DERIVED}
        self.locations = {}   # base shot id -> list of (where, image id, size)
        size = "×".join(str(x) for x in bs.SCREEN_SIZE)
        for sid, *_ in bs.SCREENS:
            self._use(sid, "Features page – screenshot gallery, tab " + sid, sid, size)
        # Screenshots placed in the page templates: {{SHOT:<id>:...}} (both themes) and {{IMG:<id>-dark}}.
        templates = os.path.join(SRC, "templates")
        for name in sorted(os.listdir(templates)):
            text = open(os.path.join(templates, name), encoding="utf-8").read()
            page = name.rsplit(".", 1)[0]
            for m in re.finditer(r"\{\{SHOT:([\w-]+):", text):
                section = re.findall(r'<section\b[^>]*\bid="([\w-]+)"', text[:m.start()])
                self._use(m.group(1), f"{page} page" + (f", section #{section[-1]}" if section else ""), m.group(1), size)
            for m in re.finditer(r"\{\{IMG:([\w-]+)-dark\}\}", text):
                self._use(m.group(1), f"{page} page", m.group(1), size)
        if "write_preview" in open(os.path.join(WEBSITE, "build_site.py"), encoding="utf-8").read():
            self._use("asset-library", "link preview og-image*.png (dark theme only, scaled into 1200×630)", "og-image", "760 wide")
        self.pages = list(self.locations)
        for sid in self.pages:
            if sid not in self.app_shots:
                self.problems.append(f"screenshot '{sid}' is used by the website, but SiteScreenshots.cs cannot take it "
                                     "(add a Shot with that name)")
        unused = [s for s in self.app_shots if s not in self.locations]
        if unused:
            self.notes.append("the program can also take " + ", ".join(unused) + ", which the website does not show")

    def _use(self, sid, where, image, size):
        self.locations.setdefault(sid, []).append((where, image, size))

    def language_file(self, code):
        return repo_path(self.cfg["fingerprint"]["language_file"].replace("{code}", code))

    def expected(self, pages=None, languages=None, themes=None):
        return [(p, c, t) for p in self.pages if not pages or p in pages
                for c in self.languages if not languages or c in languages
                for t in THEMES if not themes or t in themes]


# ============================================================================ fingerprints (what a screenshot depends on)
def hash_files(paths, exclude=()):
    h = hashlib.sha256()
    files = []
    for p in paths:
        p = repo_path(p)
        if os.path.isdir(p):
            for folder, dirs, names in os.walk(p):
                dirs[:] = sorted(d for d in dirs if d not in ("bin", "obj"))
                files += [os.path.join(folder, n) for n in names]
        elif os.path.exists(p):
            files.append(p)
    for f in sorted(files):
        if any(re.search(x, f.replace("\\", "/")) for x in exclude):
            continue
        h.update(rel(f).encode())
        data = open(f, "rb").read()
        if f.endswith((".cs", ".xaml", ".props", ".csproj", ".json", ".txt", ".md")):
            data = data.replace(b"\r\n", b"\n")   # line endings never change a picture
        h.update(hashlib.sha256(data).digest())
    return h.hexdigest()[:16]


def client_assets(cfg):
    folder = os.path.expandvars(cfg["client_assets"])
    catalog = os.path.join(folder, "catalog-content.json")
    return folder, catalog if os.path.exists(catalog) else None


def fingerprints(cfg, inv):
    """Components per (language, theme). A screenshot is outdated when one of its components changed."""
    fp = cfg["fingerprint"]
    app = hash_files(fp["app_sources"], exclude=(r"/Localization/Resources/Strings\.", r"/Themes/Colors\.(Dark|Light)\.xaml$"))
    _, catalog = client_assets(cfg)
    client = hashlib.sha256(open(catalog, "rb").read()).hexdigest()[:16] if catalog else "unknown"
    english = hash_files([inv.language_file("en")])
    langs = {c: hash_files([inv.language_file("en"), inv.language_file(c)]) if c != "en" else english for c in inv.languages}
    themes = {t: hash_files([fp["theme_file"].replace("{Theme}", t.capitalize())]) for t in THEMES}
    return {(c, t): {"app": app, "language": langs[c], "theme": themes[t], "client": client}
            for c in inv.languages for t in THEMES}


def combine(components):
    return hashlib.sha256(json.dumps(components, sort_keys=True).encode()).hexdigest()[:16]


# ============================================================================ manifest
def load_manifest():
    if os.path.exists(MANIFEST):
        return json.load(open(MANIFEST, encoding="utf-8"))
    return {"screenshots": {}}


def save_manifest(manifest):
    manifest["screenshots"] = dict(sorted(manifest["screenshots"].items()))
    tmp = MANIFEST + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=1, ensure_ascii=False)
        f.write("\n")
    os.replace(tmp, MANIFEST)


def key(sid, code, theme):
    return f"{sid}/{code}/{theme}"


def sha256_file(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def state(manifest, fps, sid, code, theme):
    """('ok' | 'missing' | 'outdated' | 'unrecorded' | 'modified', reasons)."""
    path = shot_file(sid, code, theme)
    rec = manifest["screenshots"].get(key(sid, code, theme))
    if not os.path.exists(path):
        return "missing", ["no file"]
    if not rec:
        return "unrecorded", ["file exists but was not made by this tool"]
    if rec.get("sha256") != sha256_file(path):
        return "modified", ["the file was changed after it was captured"]
    now = fps[(code, theme)]
    changed = [name for name, value in now.items() if rec.get("components", {}).get(name) != value]
    if changed:
        why = {"app": "program changed", "language": "translation changed", "theme": "theme colours changed",
               "client": "Tibia client updated"}
        return "outdated", [why.get(c, c) for c in changed]
    return "ok", []


# ============================================================================ image checks and comparison
def luminance(img):
    a = np.asarray(img.convert("RGB"), dtype=np.float32)
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def check_image(path, theme, cfg):
    """Automatic checks of one capture; returns (checks dict, list of failures)."""
    img = Image.open(path)
    failures, checks = [], {}
    w, h = cfg["frame"]
    checks["size"] = f"{img.width}x{img.height}"
    if (img.width, img.height) != (w, h):
        failures.append(f"size {img.width}x{img.height}, expected {w}x{h}")
    lum = luminance(img)
    body = lum[40:-40, 230:]                      # the page area (no title bar, navigation or status bar)
    checks["luminance"] = round(float(body.mean()), 1)
    checks["title_bar_luminance"] = round(float(lum[:28].mean()), 1)
    checks["contrast"] = round(float(lum.std()), 1)
    c = cfg["checks"]
    if theme == "dark" and (checks["luminance"] > c["dark_max_luminance"] or checks["title_bar_luminance"] > c["dark_max_luminance"]):
        failures.append(f"does not look dark (page {checks['luminance']}, title bar {checks['title_bar_luminance']})")
    if theme == "light" and (checks["luminance"] < c["light_min_luminance"] or checks["title_bar_luminance"] < c["light_min_luminance"]):
        failures.append(f"does not look light (page {checks['luminance']}, title bar {checks['title_bar_luminance']})")
    if checks["contrast"] < c["min_contrast_stddev"]:
        failures.append(f"almost uniform image (contrast {checks['contrast']}): blank or not rendered")
    return checks, failures


def compare(old_path, new_path, cfg, diff_out):
    """Pixel comparison of the previous and the new capture; writes a highlighted diff image when they differ."""
    old, new = Image.open(old_path).convert("RGB"), Image.open(new_path).convert("RGB")
    if old.size != new.size:
        return {"changed_percent": 100.0, "note": f"size {old.size} -> {new.size}"}
    a, b = np.asarray(old, dtype=np.int16), np.asarray(new, dtype=np.int16)
    mask = np.abs(a - b).max(axis=2) > cfg["diff"]["pixel_threshold"]
    pct = round(100.0 * float(mask.mean()), 3)
    result = {"changed_percent": pct}
    if mask.any():
        ys, xs = np.nonzero(mask)
        result["box"] = [int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1]
        if pct >= cfg["diff"]["changed_percent"]:
            base = (np.asarray(new, dtype=np.float32) * 0.35).astype(np.uint8)
            base[mask] = (255, 40, 40)
            both = Image.new("RGB", (new.width * 2, new.height))
            both.paste(old, (0, 0))
            both.paste(Image.fromarray(base), (new.width, 0))
            os.makedirs(os.path.dirname(diff_out), exist_ok=True)
            both.resize((new.width, new.height // 2)).save(diff_out)
            result["diff_image"] = rel(diff_out)
    return result


# ============================================================================ build, deploy, run
def run(cmd, **kw):
    print("  $ " + " ".join(f'"{c}"' if " " in c else c for c in cmd))
    return subprocess.run(cmd, **kw)


def build_and_deploy(cfg):
    app = cfg["app"]
    r = run(["dotnet", "build", repo_path(app["project"]), "-c", "Release", "-nologo", "-v", "q"])
    if r.returncode != 0:
        sys.exit("The program does not build; nothing was captured.")
    out, target = repo_path(app["build_output"]), app["runtime_dir"]
    if not os.path.exists(os.path.join(out, "TibiaFoundry.exe")):
        sys.exit(f"No TibiaFoundry.exe in {out} after the build.")
    if not os.path.isdir(os.path.join(target, "TibiaFoundry-Data")):
        sys.exit(f"{target} is not a portable demo copy (no TibiaFoundry-Data folder). See README.md, 'Demo copy'.")
    # Program files only: the demo copy's data folder and portable marker stay as they are.
    copied = 0
    for folder, _, names in os.walk(out):
        for n in names:
            s = os.path.join(folder, n)
            d = os.path.join(target, os.path.relpath(s, out))
            os.makedirs(os.path.dirname(d), exist_ok=True)
            shutil.copy2(s, d)
            copied += 1
    print(f"  deployed {copied} program files to {target}")


def compare_old_file(cfg):
    """The older appearances file Compare Files shows (env TF_SITE_COMPARE_OLD, a path in config, or 'auto')."""
    value = os.environ.get("TF_SITE_COMPARE_OLD") or cfg.get("compare_old_file")
    if value and value != "auto":
        return value if os.path.exists(value) else None
    folder, catalog = client_assets(cfg)
    current = None
    if catalog:
        current = next((e["file"] for e in json.load(open(catalog, encoding="utf-8")) if e.get("type") == "appearances"), None)
    candidates = [p for p in glob.glob(os.path.join(folder, "appearances-*.dat*")) if os.path.basename(p) != current]
    return max(candidates, key=os.path.getmtime) if candidates else None


def reset_demo_data(cfg):
    """Every run starts from the same data (settings, recent pages, library, projects, no old logs): the program saves
    what a run changes (recently used tools, window placement…), which would otherwise differ between languages."""
    baseline = cfg["app"].get("baseline_data")
    data = os.path.join(cfg["app"]["runtime_dir"], "TibiaFoundry-Data")
    if not baseline:
        return
    if not os.path.isdir(baseline):
        shutil.copytree(data, baseline, ignore=shutil.ignore_patterns("logs"))
        print(f"  created the demo data baseline {baseline} from {data} (every run now starts from it)")
    # Old logs go too: Settings shows how many log files there are. (The run's own log stays until the next run.)
    for name in os.listdir(data):
        path = os.path.join(data, name)
        shutil.rmtree(path, ignore_errors=True) if os.path.isdir(path) else os.remove(path)
    shutil.copytree(baseline, data, dirs_exist_ok=True, ignore=shutil.ignore_patterns("logs"))


def capture_language(cfg, code, pages, themes, out):
    exe = os.path.join(cfg["app"]["runtime_dir"], "TibiaFoundry.exe")
    reset_demo_data(cfg)
    if os.path.exists(out):
        shutil.rmtree(out)
    os.makedirs(out)
    env = dict(os.environ)
    old = compare_old_file(cfg)
    if old:
        env["TF_SITE_COMPARE_OLD"] = old
    cmd = [exe, "--site-screenshots", out, "--language", code, "--site-pages", ",".join(pages), "--site-themes", ",".join(themes)]
    started = time.time()
    try:
        r = run(cmd, env=env, timeout=cfg["app"]["timeout_minutes"] * 60)
        code_ = r.returncode
    except subprocess.TimeoutExpired:
        subprocess.run(["taskkill", "/F", "/IM", "TibiaFoundry.exe"], capture_output=True)
        return None, f"timed out after {cfg['app']['timeout_minutes']} min"
    summary = os.path.join(out, "site-screenshots.json")
    if not os.path.exists(summary):
        log = os.path.join(cfg["app"]["runtime_dir"], "TibiaFoundry-Data", "logs")
        return None, f"the program ended (exit {code_}) without writing site-screenshots.json; see its log in {log}"
    data = json.load(open(summary, encoding="utf-8-sig"))
    data["exit_code"], data["seconds"] = code_, round(time.time() - started)
    return data, None


# ============================================================================ commands
def parse_list(value):
    return [v.strip() for v in value.split(",") if v.strip()] if value else None


def select(inv, args):
    pages, langs, themes = parse_list(args.pages), parse_list(args.languages), parse_list(args.themes)
    for name, given, known in (("page", pages, inv.pages), ("language", langs, inv.languages), ("theme", themes, THEMES)):
        bad = [g for g in (given or []) if g not in known]
        if bad:
            sys.exit(f"Unknown {name}: {', '.join(bad)} (known: {', '.join(known)})")
    return pages, langs, themes


def cmd_capture(args, cfg):
    inv = Inventory(cfg)
    if inv.problems:
        sys.exit("Cannot capture:\n  " + "\n  ".join(inv.problems))
    pages, langs, themes = select(inv, args)
    manifest = load_manifest()
    fps = fingerprints(cfg, inv)
    targets = inv.expected(pages, langs, themes)
    skipped = []
    if args.incremental:
        keep = [t for t in targets if state(manifest, fps, *t)[0] != "ok"]
        skipped = [t for t in targets if t not in keep]
        targets = keep
    print(f"{len(targets)} screenshot(s) to capture, {len(skipped)} up to date"
          + (f" ({len(inv.pages)} locations × {len(inv.languages)} languages × 2 themes in all)" if not args.incremental else ""))
    if args.dry_run or not targets:
        for t in targets:
            print("  would capture " + key(*t) + "  (" + ", ".join(state(manifest, fps, *t)[1]) + ")")
        if not targets and not args.dry_run and not args.no_site:
            build_site_and_validate(cfg, inv)
        return 0
    if not args.no_build:
        print("Building and deploying the program")
        build_and_deploy(cfg)

    counts = {"generated": 0, "updated": 0, "unchanged": 0, "failed": 0, "skipped": len(skipped)}
    failures, warnings = [], []
    now = datetime.datetime.now().isoformat(timespec="seconds")
    for code in inv.languages:
        mine = [t for t in targets if t[1] == code]
        if not mine:
            continue
        lang_pages = sorted({t[0] for t in mine}, key=inv.pages.index)
        lang_themes = sorted({t[2] for t in mine})
        print(f"Capturing {code}: {len(lang_pages)} page(s) × {len(lang_themes)} theme(s)")
        out = os.path.join(WORK, "capture", code)
        data, error = capture_language(cfg, code, lang_pages, lang_themes, out)
        if error:
            for t in mine:
                failures.append((key(*t), error))
            counts["failed"] += len(mine)
            continue
        if data.get("language") != code:
            for t in mine:
                failures.append((key(*t), f"the program started in '{data.get('language')}', not '{code}'"))
            counts["failed"] += len(mine)
            continue
        results = {(r["name"], r["theme"]): r for r in data["results"]}
        for sid, _, theme in mine:
            r = results.get((sid, theme))
            k = key(sid, code, theme)
            if not r or r.get("status") != "ok":
                failures.append((k, (r or {}).get("error", "not captured by the program")))
                counts["failed"] += 1
                continue
            raw = os.path.join(out, r["file"])
            checks, problems = check_image(raw, theme, cfg)
            if problems:
                failures.append((k, "; ".join(problems)))
                counts["failed"] += 1
                continue
            dest = shot_file(sid, code, theme)
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            tmp = dest + ".new.png"
            Image.open(raw).convert("RGB").save(tmp, optimize=True)
            prev = manifest["screenshots"].get(k, {})
            diff = None
            if os.path.exists(dest):
                diff = compare(dest, tmp, cfg, os.path.join(WORK, "diff", f"{sid}-{code}-{theme}.png"))
                same_inputs = prev.get("fingerprint") == combine(fps[(code, theme)])
                if same_inputs and diff["changed_percent"] >= cfg["diff"]["changed_percent"]:
                    warnings.append(f"{k}: changed by {diff['changed_percent']} % although nothing it depends on changed "
                                    f"(unexpected change; see {diff.get('diff_image', 'the diff')})")
            sha = sha256_file(tmp)
            if os.path.exists(dest) and sha256_file(dest) == sha:
                os.remove(tmp)
                counts["unchanged"] += 1
            else:
                counts["updated" if os.path.exists(dest) else "generated"] += 1
                os.replace(tmp, dest)
            review = prev.get("review") if prev.get("review", {}).get("sha256") == sha else {"status": "pending"}
            manifest["screenshots"][k] = {
                "file": rel(dest), "sha256": sha, "width": checks["size"], "captured": now,
                "app": data.get("app"), "fingerprint": combine(fps[(code, theme)]), "components": fps[(code, theme)],
                "checks": checks, "blurred_areas": r.get("blurred", 0), "diff_vs_previous": diff, "review": review,
            }
        save_manifest(manifest)

    write_reports(cfg, inv, manifest, fps, failures)
    print("\nSummary: " + ", ".join(f"{v} {k}" for k, v in counts.items()))
    for k, e in failures:
        print(f"  FAILED {k}: {e}")
    for w in warnings:
        print(f"  WARNING {w}")
    if not args.no_site:
        ok = build_site_and_validate(cfg, inv)
        if not ok:
            return 1
    return 1 if failures else 0


def build_site_and_validate(cfg, inv):
    print("Building the website")
    r = run([sys.executable, "-I", os.path.join(WEBSITE, "build_site.py")])
    if r.returncode != 0:
        print("  the website build failed")
        return False
    return validate(cfg, inv, verbose=True) == 0


def cmd_status(args, cfg):
    inv = Inventory(cfg)
    pages, langs, themes = select(inv, args)
    manifest = load_manifest()
    fps = fingerprints(cfg, inv)
    by_state = {}
    for t in inv.expected(pages, langs, themes):
        s, why = state(manifest, fps, *t)
        by_state.setdefault(s, []).append((key(*t), why))
    total = sum(len(v) for v in by_state.values())
    print(f"{total} screenshots expected: " + ", ".join(f"{len(v)} {k}" for k, v in sorted(by_state.items())))
    for s, items in sorted(by_state.items()):
        if s == "ok":
            continue
        for k, why in items:
            print(f"  {s:10} {k}  ({', '.join(why)})")
    pending = [k for k, r in manifest["screenshots"].items() if r.get("review", {}).get("status") != "approved"]
    if pending:
        print(f"  {len(pending)} not visually reviewed yet (approve them after looking at them: see README.md)")
    for p in inv.problems:
        print("  PROBLEM " + p)
    for n in inv.notes:
        print("  note: " + n)
    return 0 if set(by_state) <= {"ok"} and not inv.problems else 1


def validate(cfg, inv, verbose=True):
    """Every expected screenshot exists and passes its checks; the built site references the right files."""
    errors, warnings = [], []
    manifest = load_manifest()
    expected = inv.expected()
    for sid, code, theme in expected:
        path = shot_file(sid, code, theme)
        if not os.path.exists(path):
            errors.append(f"missing {rel(path)}")
            continue
        _, problems = check_image(path, theme, cfg)
        errors += [f"{rel(path)}: {p}" for p in problems]
        rec = manifest["screenshots"].get(key(sid, code, theme))
        if not rec or rec.get("sha256") != sha256_file(path):
            warnings.append(f"{rel(path)} is not recorded in manifest.json (not made by this tool, or edited)")
    # Files in the library that no location uses any more.
    wanted = {os.path.normcase(shot_file(*t)) for t in expected}
    for f in glob.glob(os.path.join(SHOTS, "**", "*.png"), recursive=True):
        if os.path.normcase(f) not in wanted:
            warnings.append(f"obsolete screenshot {rel(f)} (no website location uses it)")
    # Old layouts that must be gone.
    for old in glob.glob(os.path.join(SRC, "images", "*-dark.png")) + glob.glob(os.path.join(SRC, "images", "*-light.png")):
        errors.append(f"old-style screenshot {rel(old)} still exists (the library is screens/<code>/)")

    # The built website.
    pages = sorted(glob.glob(os.path.join(SITE, "**", "index.html"), recursive=True))
    if not pages:
        warnings.append("website/site has not been built; its references were not checked")
    langs = {l[1]: l[0] for l in inv.build_site.LANGUAGES}
    referenced = set()
    for page in pages:
        html = open(page, encoding="utf-8").read()
        folder = os.path.relpath(os.path.dirname(page), SITE).replace("\\", "/")
        folder = "" if folder == "." else folder
        if folder not in langs:
            continue
        code = langs[folder]
        for src in re.findall(r'<img\b[^>]*\bsrc="([^"]+)"', html):
            if src.startswith(("http:", "https:", "data:")):
                continue
            target = os.path.normpath(os.path.join(os.path.dirname(page), src))
            referenced.add(os.path.normcase(target))
            if not os.path.exists(target):
                errors.append(f"site/{folder + '/' if folder else ''}index.html: broken image {src}")
            m = re.search(r"img/screens/[\w-]+?-([A-Za-z]{2}(?:-[A-Za-z]{2})?)-(dark|light)\.webp$", src)
            if m and m.group(1) != code:
                errors.append(f"site/{folder + '/' if folder else ''}index.html ({code}) shows a {m.group(1)} screenshot: {src}")
        # Every themed picture has both variants.
        for pair in re.findall(r'class="shot-dark" src="([^"]+)"[^>]*><img class="shot-light" src="([^"]+)"', html):
            if pair[0].replace("-dark.", "-light.") != pair[1]:
                errors.append(f"{code}: dark/light pair does not match: {pair}")
        n_dark, n_light = html.count('class="shot-dark"'), html.count('class="shot-light"')
        if n_dark != n_light:
            errors.append(f"{code}: {n_dark} dark but {n_light} light screenshots")
        for p in re.findall(r'href="([^"#?]+\.(?:png|webp|ico|xml|txt))"', html):
            if p.startswith("http"):
                continue
            # Root-relative links ("/favicon.ico") resolve against the site root (GitHub Pages serves site/ at /).
            target = os.path.join(SITE, p.lstrip("/")) if p.startswith("/") else os.path.join(os.path.dirname(page), p)
            if not os.path.exists(os.path.normpath(target)):
                errors.append(f"{code}: broken link {p}")
    if pages:
        for f in glob.glob(os.path.join(SITE, "img", "**", "*.*"), recursive=True):
            if os.path.normcase(os.path.normpath(f)) not in referenced:
                warnings.append(f"site/{os.path.relpath(f, SITE)} is not used by any page")
    if verbose:
        print(f"Validation: {len(expected)} expected screenshots, {len(pages)} built pages, {len(errors)} error(s), {len(warnings)} warning(s)")
        for e in errors:
            print("  ERROR " + e)
        for w in warnings:
            print("  warning " + w)
    return 1 if errors else 0


def cmd_validate(args, cfg):
    inv = Inventory(cfg)
    for p in inv.problems:
        print("PROBLEM " + p)
    return validate(cfg, inv) or (1 if inv.problems else 0)


def font(size):
    for name in ("segoeuib.ttf", "arialbd.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default(size)


def cmd_sheets(args, cfg):
    """Contact sheets for visual review: one per location, every language (rows) in both themes (columns)."""
    inv = Inventory(cfg)
    pages, langs, themes = select(inv, args)
    out = os.path.join(WORK, "sheets")
    os.makedirs(out, exist_ok=True)
    scale = args.scale
    for sid in pages or inv.pages:
        codes = langs or inv.languages
        ts = themes or list(THEMES)
        w, h = int(cfg["frame"][0] * scale), int(cfg["frame"][1] * scale)
        sheet = Image.new("RGB", (w * len(ts) + 10 * (len(ts) - 1), (h + 30) * len(codes)), (128, 128, 128))
        d = ImageDraw.Draw(sheet)
        for row, code in enumerate(codes):
            for col, theme in enumerate(ts):
                path = shot_file(sid, code, theme)
                x, y = col * (w + 10), row * (h + 30)
                d.text((x + 6, y + 4), f"{sid}  {code}  {theme}", font=font(18), fill=(255, 255, 255))
                if os.path.exists(path):
                    sheet.paste(Image.open(path).convert("RGB").resize((w, h), Image.LANCZOS), (x, y + 30))
                else:
                    d.text((x + 20, y + 60), "MISSING", font=font(40), fill=(255, 60, 60))
        sheet.save(os.path.join(out, f"{sid}.png"))
    print(f"Contact sheets in {out}")
    return 0


def cmd_approve(args, cfg):
    """Records that the current files were looked at (the record is dropped as soon as a file changes)."""
    inv = Inventory(cfg)
    pages, langs, themes = select(inv, args)
    manifest = load_manifest()
    n = 0
    today = datetime.date.today().isoformat()
    for t in inv.expected(pages, langs, themes):
        rec = manifest["screenshots"].get(key(*t))
        path = shot_file(*t)
        if rec and os.path.exists(path) and rec["sha256"] == sha256_file(path):
            rec["review"] = {"status": "approved", "sha256": rec["sha256"], "date": today, "note": args.note or ""}
            n += 1
        else:
            print(f"  not approved (missing or not recorded): {key(*t)}")
    save_manifest(manifest)
    print(f"{n} screenshot(s) marked as visually reviewed")
    write_reports(cfg, inv, manifest, fingerprints(cfg, inv), [])
    return 0


def cmd_report(args, cfg):
    inv = Inventory(cfg)
    write_reports(cfg, inv, load_manifest(), fingerprints(cfg, inv), [])
    return 0


# ============================================================================ reports
def write_reports(cfg, inv, manifest, fps, failures):
    today = datetime.date.today().isoformat()
    total = len(inv.expected())
    lines = ["# Website screenshot inventory", "",
             f"Generated by `website/screenshots/screenshots.py` on {today} from the project itself (template, "
             "`build_site.py` and the program's `SiteScreenshots.cs`). Do not edit by hand.", "",
             f"**Languages ({len(inv.languages)}):** " + ", ".join(f"{inv.language_names[c]} (`{c}`)" for c in inv.languages), "",
             f"**Themes (2):** dark, light", "",
             f"**Locations (unique screenshots taken from the program): {len(inv.pages)}** → "
             f"{len(inv.pages)} × {len(inv.languages)} × 2 = **{total} screenshots**", "",
             "| # | Screenshot id | Program page | Used on the website | Shown at |", "|---:|---|---|---|---|"]
    for i, sid in enumerate(inv.pages, 1):
        uses = "<br>".join(w for w, _, _ in inv.locations[sid])
        sizes = ", ".join(sorted({s for _, _, s in inv.locations[sid]}))
        lines.append(f"| {i} | `{sid}` | {inv.app_shots.get(sid, '**not in the program**')} | {uses} | {sizes} |")
    lines += ["", "Files: `website/src/images/screens/<code>/<id>-<code>-<theme>.png` (lossless master, 1280×800 window "
              "capture with title bar); the website build writes `site/img/screens/<id>-<code>-<theme>.webp`.", "",
              "Not screenshots (language- and theme-neutral, kept as they are): `logo.png`, `output-creatures.png`, "
              "`output-items.png`, `output-outfit-129.png` (real Content Studio output), and the infobox sample, which is "
              "text in the page.", ""]
    if inv.problems or inv.notes:
        lines += ["## Problems and notes", ""] + [f"- **problem:** {p}" for p in inv.problems] + [f"- note: {n}" for n in inv.notes] + [""]
    open(os.path.join(HERE, "inventory.md"), "w", encoding="utf-8", newline="\n").write("\n".join(lines))

    rows, counts = [], {}
    approved = 0
    for sid, code, theme in inv.expected():
        s, why = state(manifest, fps, sid, code, theme)
        rec = manifest["screenshots"].get(key(sid, code, theme), {})
        review = rec.get("review", {})
        reviewed = review.get("status") == "approved" and review.get("sha256") == rec.get("sha256") and s != "missing"
        approved += reviewed
        counts[s] = counts.get(s, 0) + 1
        checks = rec.get("checks", {})
        diff = rec.get("diff_vs_previous") or {}
        auto = "pass" if s in ("ok", "outdated") and checks else "–"
        rows.append(f"| `{sid}` | {code} | {theme} | {'captured ' + rec['captured'][:10] if rec else 'not captured'} | "
                    f"{s}{' (' + ', '.join(why) + ')' if why and s != 'ok' else ''} | {auto} | "
                    f"{'reviewed ' + review.get('date', '') if reviewed else 'pending'} | "
                    f"{str(diff.get('changed_percent', '')) + ' %' if diff else 'new'} | "
                    f"`{rel(shot_file(sid, code, theme)).replace('website/src/images/', '')}` |")
    head = ["# Website screenshot coverage", "",
            f"Generated by `website/screenshots/screenshots.py` on {today}. Do not edit by hand.", "",
            f"- Expected: **{total}** ({len(inv.pages)} locations × {len(inv.languages)} languages × 2 themes)",
            "- " + ", ".join(f"{k}: **{v}**" for k, v in sorted(counts.items())),
            f"- Visually reviewed (current file): **{approved}** of {total}", "",
            "*State*: ok = captured from the current program, translation, theme and client; outdated = one of those "
            "changed since (regenerate with `capture --incremental`). *Checks*: size, theme brightness of page and "
            "title bar, not blank. *Diff*: share of pixels that changed against the previous capture.", ""]
    if failures:
        head += ["## Failed in the last run", ""] + [f"- `{k}`: {e}" for k, e in failures] + [""]
    head += ["| Screenshot | Language | Theme | Capture | State | Checks | Visual review | Diff | File (in website/src/images/) |",
             "|---|---|---|---|---|---|---|---:|---|"]
    open(os.path.join(HERE, "coverage-report.md"), "w", encoding="utf-8", newline="\n").write("\n".join(head + rows) + "\n")


# ============================================================================ main
def main():
    parser = argparse.ArgumentParser(description="Website screenshot generator (see website/screenshots/README.md)")
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("capture", "status", "validate", "sheets", "approve", "report"):
        p = sub.add_parser(name)
        p.add_argument("--pages", help="comma-separated screenshot ids (default: all)")
        p.add_argument("--languages", help="comma-separated language codes (default: all)")
        p.add_argument("--themes", help="dark, light or both (default: both)")
        if name == "capture":
            p.add_argument("--incremental", action="store_true", help="only missing or outdated screenshots")
            p.add_argument("--no-build", action="store_true", help="use the program already in the demo copy")
            p.add_argument("--no-site", action="store_true", help="do not build and validate the website afterwards")
            p.add_argument("--dry-run", action="store_true", help="only list what would be captured")
        if name == "sheets":
            p.add_argument("--scale", type=float, default=0.5)
        if name == "approve":
            p.add_argument("--note", default="")
    args = parser.parse_args()
    sys.stdout.reconfigure(line_buffering=True)   # progress shows at once, also when piped to a log
    cfg = load_config()
    os.makedirs(WORK, exist_ok=True)
    lock = os.path.join(WORK, "lock")
    if args.command == "capture":
        if os.path.exists(lock) and time.time() - os.path.getmtime(lock) < 3 * 3600:
            sys.exit(f"Another capture seems to be running ({lock}); delete the file if it is not.")
        open(lock, "w").write(str(os.getpid()))
    try:
        return {"capture": cmd_capture, "status": cmd_status, "validate": cmd_validate, "sheets": cmd_sheets,
                "approve": cmd_approve, "report": cmd_report}[args.command](args, cfg)
    finally:
        if args.command == "capture" and os.path.exists(lock):
            os.remove(lock)


if __name__ == "__main__":
    sys.exit(main())
