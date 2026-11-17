"""
Website screenshot matrix: every page and Tibia data route x every language x both themes x phone/tablet/desktop.

    <python with playwright> website/tests/site_screenshots.py [--out Release/<version>/qa-screenshots]
        [--routes home,character] [--languages en,de] [--themes dark] [--viewports mobile] [--live] [--png]

Routes, languages, themes and data-route samples come from src/registry.json; viewports from tests/sitekit.py
(SCREENSHOT_VIEWPORTS) - a new page, language or theme is picked up without changing this script. TibiaData is answered
from the recorded fixtures (deterministic; --live uses the real API). Each capture waits for fonts, every image and the
data view, with animations off, and is a full-page image (WebP, or PNG with --png). Every image is validated (language,
theme, width, no loading placeholders, no page-level horizontal overflow, credits present, no broken site images, no
obsolete version) and listed in <out>/manifest.json with its result; the summary lists expected, captured, failed,
skipped and missing combinations. Exit code 1 when any capture fails.
"""
import datetime
import io
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sitekit as kit  # noqa: E402
from PIL import Image  # noqa: E402
from playwright.sync_api import sync_playwright  # noqa: E402

ROOT = os.path.dirname(kit.WEBSITE)
VERSION = re.search(r"<Version>([^<]+)</Version>", open(os.path.join(ROOT, "src", "Directory.Build.props"), encoding="utf-8").read()).group(1)
NO_MOTION = "*,*::before,*::after{animation:none!important;transition:none!important;caret-color:transparent!important}"


def routes():
    """(route id, page id, hash) for every page and data route; the Tibia data page itself is its 'hub' route."""
    out = [(p["id"], p["id"], "") for p in kit.PAGES if p["id"] != "tibia"]
    out += [(r["id"], "tibia", kit.data_hash(r["sample"])) for r in kit.DATA_ROUTES]
    return out


def arg(args, name):
    return args[args.index(name) + 1].split(",") if name in args else None


def settle(page, is_data):
    page.add_style_tag(content=NO_MOTION)
    page.evaluate("document.fonts.ready")
    if is_data:
        page.wait_for_function("() => { const a = document.getElementById('app'); return a && !a.hasAttribute('aria-busy') && !a.querySelector('.loading'); }", timeout=30000)
    if page.locator("[data-live-news]").count():
        page.wait_for_function("() => !document.querySelector('[data-live-news] .loading') && document.querySelector('[data-live-news] .news-item, [data-live-news] .notice')", timeout=30000)
    if page.locator("[data-live-boosted]").count():
        page.wait_for_function("() => !document.querySelector('[data-live-boosted] .loading') && document.querySelector('[data-live-boosted] .boost-card, [data-live-boosted] .notice')", timeout=30000)
    # Lazy images: load them all, then wait until each has finished (loaded or failed).
    page.evaluate("document.querySelectorAll('img[loading=lazy]').forEach(i => i.loading = 'eager')")
    page.wait_for_function("() => [...document.images].every(i => i.complete)", timeout=30000)
    page.wait_for_timeout(150)


def validate(page, lang, theme, width):
    return page.evaluate("""([code, theme, width]) => {
      const p = [];
      if (document.documentElement.lang !== code) p.push('lang ' + document.documentElement.lang);
      const scheme = getComputedStyle(document.documentElement).colorScheme;
      if (!scheme.startsWith(theme)) p.push('theme ' + scheme);
      if (window.innerWidth !== width) p.push('viewport ' + window.innerWidth);
      if (document.querySelector('.loading, .skeleton')) p.push('loading placeholder visible');
      const over = document.documentElement.scrollWidth - document.documentElement.clientWidth;
      if (over > 0) p.push('horizontal overflow ' + over + 'px');
      if (!document.querySelector('footer [data-credit=tibiadata] a[href="https://tibiadata.com/"]')) p.push('TibiaData credit missing');
      const broken = [...document.images].filter(i => i.complete && i.naturalWidth === 0 && i.src.startsWith(location.origin)).map(i => i.src);
      if (broken.length) p.push('broken images ' + broken.slice(0, 3).join(' '));
      const b = document.body.cloneNode(true); b.querySelectorAll('#whats-new h3 ~ ul').forEach(e => e.remove());
      if (/\\b9\\.0\\.4\\b/.test(b.innerText)) p.push('obsolete version 9.0.4 visible');
      const external = [...document.images].filter(i => i.complete && i.naturalWidth === 0 && !i.src.startsWith(location.origin)).length;
      return { problems: p, title: document.title, externalImagesNotLoaded: external };
    }""", [lang["code"], theme, width])


def main():
    args = sys.argv[1:]
    out = os.path.abspath(args[args.index("--out") + 1]) if "--out" in args else os.path.join(ROOT, "Release", VERSION, "qa-screenshots")
    want_routes, want_langs = arg(args, "--routes"), arg(args, "--languages")
    want_themes, want_views = arg(args, "--themes"), arg(args, "--viewports")
    fmt = "png" if "--png" in args else "webp"
    all_routes = routes()
    selected = [(rid, pid, h) for rid, pid, h in all_routes if not want_routes or rid in want_routes]
    langs = [l for l in kit.LANGUAGES if not want_langs or l["code"] in want_langs]
    themes = [t for t in kit.THEMES if not want_themes or t in want_themes]
    views = {k: v for k, v in kit.SCREENSHOT_VIEWPORTS.items() if not want_views or k in want_views}
    expected = len(selected) * len(langs) * len(themes) * len(views)
    print(f"{expected} screenshots: {len(selected)} routes x {len(langs)} languages x {len(themes)} themes x {len(views)} viewports -> {out}", flush=True)
    os.makedirs(out, exist_ok=True)
    manifest_path = os.path.join(out, "manifest.json")
    old = json.load(open(manifest_path, encoding="utf-8")) if os.path.exists(manifest_path) else {"shots": []}
    keep = {(s["route"], s["locale"], s["theme"], s["viewport"]): s for s in old.get("shots", [])}
    entries, failed = [], 0
    started = time.time()
    with kit.SiteServer() as server, sync_playwright() as pw:
        browser = pw.chromium.launch()
        for vname, (w, h) in views.items():
            for theme in themes:
                for lang in langs:
                    # The browser speaks the page's language, as a visitor of that page would (no language offer bar).
                    ctx = browser.new_context(viewport={"width": w, "height": h}, color_scheme=theme, reduced_motion="reduce",
                                              locale=lang["intl"], device_scale_factor=1)
                    if "--live" not in args:
                        kit.use_fixtures(ctx)
                    page = ctx.new_page()
                    for rid, pid, hsh in selected:
                        pg = next(p for p in kit.PAGES if p["id"] == pid)
                        url = server.url + kit.page_path(lang, pg) + hsh
                        rel = f"{vname}/{lang['code']}/{theme}/{rid}.{fmt}"
                        entry = {"route": rid, "page": pid, "hash": hsh, "url": url.replace(server.url, "/"), "locale": lang["code"],
                                 "theme": theme, "viewport": vname, "size": [w, h], "path": rel, "version": VERSION,
                                 "captured": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds")}
                        try:
                            page.goto(url, wait_until="load")
                            settle(page, pid == "tibia")
                            check = validate(page, lang, theme, w)
                            png = page.screenshot(full_page=True, animations="disabled")
                            image = Image.open(io.BytesIO(png))
                            if image.width != w:
                                check["problems"].append(f"image width {image.width}")
                            if fmt == "webp" and image.height > 16383:   # WebP's limit: very long pages are stored as PNG
                                rel = rel[:-4] + "png"
                                entry["path"] = rel
                            target = os.path.join(out, rel)
                            os.makedirs(os.path.dirname(target), exist_ok=True)
                            if rel.endswith(".png") and fmt == "webp":
                                image.save(target, "PNG", optimize=False, compress_level=7)
                            elif fmt == "png":
                                open(target, "wb").write(png)
                            else:
                                image.convert("RGB").save(target, "WEBP", quality=80, method=4)
                            if not os.path.getsize(target):
                                check["problems"].append("empty file")
                            entry.update(title=check["title"], pixels=[image.width, image.height], bytes=os.path.getsize(target),
                                         externalImagesNotLoaded=check["externalImagesNotLoaded"],
                                         result="ok" if not check["problems"] else "failed", problems=check["problems"])
                        except Exception as e:  # recorded, never silently dropped
                            entry.update(result="failed", problems=[f"capture error: {e}"])
                        if entry["result"] != "ok":
                            failed += 1
                            print(f"  FAILED {rel}: {'; '.join(entry['problems'])}", flush=True)
                        entries.append(entry)
                        keep.pop((rid, lang["code"], theme, vname), None)
                        if len(entries) % 50 == 0:
                            print(f"  {len(entries)}/{expected} ({time.time() - started:.0f}s)", flush=True)
                    if getattr(ctx, "unknown_api", None):
                        print(f"  note: requests without fixture in {vname}/{theme}/{lang['code']}: {sorted(set(ctx.unknown_api))}")
                    ctx.close()
        browser.close()
    # Shots of earlier runs that this (filtered) run did not retake stay listed.
    all_entries = entries + list(keep.values())
    full = [(rid, l["code"], t, v) for rid, _, _ in all_routes for l in kit.LANGUAGES for t in kit.THEMES for v in kit.SCREENSHOT_VIEWPORTS]
    have = {(e["route"], e["locale"], e["theme"], e["viewport"]) for e in all_entries if e.get("result") == "ok"}
    missing = [f"{r}/{c}/{t}/{v}" for r, c, t, v in full if (r, c, t, v) not in have]
    summary = {"expected": len(full), "captured_ok": len(have), "failed": sum(1 for e in all_entries if e.get("result") != "ok"),
               "skipped": 0, "missing": missing, "this_run": {"expected": expected, "captured": len(entries), "failed": failed,
               "seconds": round(time.time() - started)}}
    json.dump({"version": VERSION, "generated": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
               "routes": [r for r, _, _ in all_routes], "locales": [l["code"] for l in kit.LANGUAGES], "themes": kit.THEMES,
               "viewports": kit.SCREENSHOT_VIEWPORTS, "fixtures": "live API" if "--live" in args else "tests/fixtures/tibiadata",
               "summary": summary, "shots": sorted(all_entries, key=lambda e: (e["viewport"], e["locale"], e["theme"], e["route"]))},
              open(manifest_path, "w", encoding="utf-8"), indent=1)
    print(f"\nExpected {summary['expected']}, captured {summary['captured_ok']}, failed {summary['failed']}, skipped 0, missing {len(missing)}"
          f"  (this run: {len(entries)} in {summary['this_run']['seconds']}s)\nManifest: {manifest_path}")
    sys.exit(1 if failed or missing else 0)


if __name__ == "__main__":
    main()
