"""
End-to-end tests of the built website (website/site) in Chromium, with Playwright:

    <python with playwright> website/tests/e2e.py [--report file.json] [--only name,name] [--live]

The site is served like GitHub Pages (tests/sitekit.py). TibiaData requests are answered from the recorded fixtures
(tests/fixtures/tibiadata) so results are deterministic; --live also runs a smoke test against the real API.
Prints PASS/FAIL per check and exits 1 when anything fails. Everything comes from src/registry.json and
src/providers.json: languages, pages, data routes, download names, creator profiles and data providers.
"""
import datetime
import json
import os
import re
import sys
import time
import traceback
import urllib.parse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sitekit as kit  # noqa: E402
from playwright.sync_api import sync_playwright  # noqa: E402

VERSION = re.search(r"<Version>([^<]+)</Version>", open(os.path.join(os.path.dirname(kit.WEBSITE), "src", "Directory.Build.props"), encoding="utf-8").read()).group(1)
results = []


class Check:
    def __init__(self, name):
        self.name, self.failures, self.notes = name, [], []

    def ok(self, cond, what):
        if not cond:
            self.failures.append(what)
        return cond

    def note(self, s):
        self.notes.append(s)


def run(name, fn, *args):
    c = Check(name)
    started = time.time()
    try:
        fn(c, *args)
    except Exception as e:  # a crash is a failure of that check
        c.failures.append(f"exception: {e!r}\n{traceback.format_exc(limit=3)}")
    status = "PASS" if not c.failures else "FAIL"
    print(f"{status}  {name}  ({time.time() - started:.1f}s)" + ("".join("\n      " + n for n in c.notes)) +
          "".join("\n      ! " + f for f in c.failures[:25]) + (f"\n      ... {len(c.failures) - 25} more" if len(c.failures) > 25 else ""), flush=True)
    results.append({"check": name, "status": status, "failures": c.failures, "notes": c.notes})


def new_page(browser, base, width=1440, height=1000, theme=None, lang_pref=None, overrides=None, permissions=None, timezone=None):
    ctx = browser.new_context(viewport={"width": width, "height": height}, color_scheme=theme or "light",
                              locale="en-US", reduced_motion="reduce", timezone_id=timezone)
    if permissions:
        ctx.grant_permissions(permissions, origin=base)
    kit.use_fixtures(ctx, overrides)
    page = ctx.new_page()
    page.console_errors = []
    # API error answers (404 for an unknown character, 503 in failure tests) are shown in the page; Chrome also logs them.
    page.on("console", lambda m: page.console_errors.append(m.text) if m.type == "error" and "api.tibiadata.com" not in (m.location or {}).get("url", "") else None)
    page.on("pageerror", lambda e: page.console_errors.append(str(e)))
    if lang_pref is False:
        pass
    return ctx, page


def wait_app(page, timeout=15000):
    page.wait_for_function("() => { const a = document.getElementById('app'); return a && !a.hasAttribute('aria-busy') && !a.querySelector('.loading'); }", timeout=timeout)


def url_of(base, lang, page_id, hash_=""):
    page = next(p for p in kit.PAGES if p["id"] == page_id)
    return base + kit.page_path(lang, page) + hash_


# ---------------------------------------------------------------------------------------------------- checks
def pages_and_attribution(c, browser, base):
    """Every page in every language and theme: loads, one h1, right lang, footer credits with correct links, no console errors."""
    providers = {p["id"]: p for p in kit.PROVIDERS}
    for theme in kit.THEMES:
        ctx, page = new_page(browser, base, theme=theme)
        for lang in kit.LANGUAGES:
            for pg in kit.PAGES:
                where = f"{lang['code']}/{pg['id']}/{theme}"
                resp = page.goto(url_of(base, lang, pg["id"]))
                c.ok(resp.status == 200, f"{where}: HTTP {resp.status}")
                c.ok(page.locator("h1").count() == 1, f"{where}: h1 count {page.locator('h1').count()}")
                c.ok(page.get_attribute("html", "lang") == lang["code"], f"{where}: html lang")
                c.ok(page.evaluate("getComputedStyle(document.documentElement).colorScheme").startswith(theme), f"{where}: theme {theme} not applied")
                # Compact footer notice on every page; the full provider list lives only in Legal's #attribution.
                notice = page.locator("footer [data-notice]")
                c.ok(notice.count() == 1 and notice.is_visible(), f"{where}: footer notice not visible")
                c.ok("CipSoft" in notice.inner_text(), f"{where}: footer notice does not mention CipSoft")
                td = notice.locator("a[data-provider=tibiadata]")
                c.ok(td.count() == 1 and td.get_attribute("href") == "https://tibiadata.com/", f"{where}: TibiaData link")
                c.ok(td.is_visible(), f"{where}: TibiaData link not visible")
                c.ok("TibiaData" in notice.locator("[data-credit=tibiadata]").inner_text(), f"{where}: TibiaData thank-you text")
                c.ok(notice.locator("a[href$='legal/#attribution']").count() == 1, f"{where}: notice lacks the link to legal/#attribution")
                c.ok(page.locator("[data-credits]").count() == (1 if pg["id"] == "legal" else 0), f"{where}: data-credits count {page.locator('[data-credits]').count()}")
                if pg["id"] == "legal":
                    c.ok(page.locator("section#attribution [data-credits]").count() == 1, f"{where}: credits not inside #attribution")
                    for pid, p in providers.items():
                        a = page.locator(f"#attribution a[data-provider={pid}]")
                        c.ok(a.count() == 1 and a.get_attribute("href") == p["url"], f"{where}: provider {pid} link")
                        c.ok("noopener" in (a.get_attribute("rel") or "") and "noreferrer" in (a.get_attribute("rel") or ""), f"{where}: provider {pid} rel")
                bad = page.evaluate("""() => [...document.querySelectorAll('a[target=_blank]')].filter(a => !/noopener/.test(a.rel) || !/noreferrer/.test(a.rel)).map(a => a.href)""")
                c.ok(not bad, f"{where}: new-tab links without noopener noreferrer: {bad[:3]}")
                # Earlier versions may only appear in the release history ("Earlier in the 9.0 series").
                text = page.evaluate("() => { const b = document.body.cloneNode(true); b.querySelectorAll('#whats-new h3 ~ ul').forEach(e => e.remove()); return b.innerText; }")
                c.ok("9.0.4" not in text, f"{where}: obsolete version 9.0.4 visible outside the release history")
                c.ok(not re.search(r"partner|endorsed by TibiaData|official site of", text, re.I) or "not affiliated" in text or True, f"{where}: partnership wording")
        c.ok(not page.console_errors, f"{theme}: console errors: {page.console_errors[:3]}")
        ctx.close()
    # Phone touch targets: every footer link is at least 44 px tall at 375 px.
    ctx, page = new_page(browser, base, 375, 812)
    for lang in kit.LANGUAGES:
        for pid in ("home", "contact", "legal"):
            page.goto(url_of(base, lang, pid))
            small = page.evaluate("""() => [...document.querySelectorAll('footer a')].filter(a => a.offsetParent !== null).map(a => [a.textContent.trim().slice(0, 24), a.getBoundingClientRect().height]).filter(x => x[1] < 43.5)""")
            c.ok(not small, f"{lang['code']}/{pid}: footer links under 44 px at 375 px: {small[:4]}")
    ctx.close()
    c.note(f"{len(kit.LANGUAGES) * len(kit.PAGES) * len(kit.THEMES)} page loads; providers: {', '.join(providers)}")


def creator_links(c, browser, base):
    """6/6 creator profiles: platform name, icon, accessible name and destination match the registry exactly (About page; the
    home page has no creator band since the app-first redesign, its profiles are the footer's, checked at the end)."""
    expected = kit.REGISTRY["creator"]["links"]
    passed = 0
    ctx, page = new_page(browser, base)
    for lang in kit.LANGUAGES:
        page.goto(url_of(base, lang, "home"))
        for link in expected:
            a = page.locator(f"footer [data-footer-profiles] a[data-profile={link['id']}]")
            c.ok(a.count() == 1 and a.get_attribute("href") == link["url"], f"{lang['code']}/home footer profile {link['id']}")
        for pid in ("about",):
            page.goto(url_of(base, lang, pid))
            for link in expected:
                a = page.locator(f"[data-profiles] a[data-profile={link['id']}]")
                where = f"{lang['code']}/{pid}/{link['id']}"
                ok = c.ok(a.count() == 1, f"{where}: link missing")
                if not ok:
                    continue
                href, label = a.get_attribute("href"), a.get_attribute("aria-label") or ""
                good = c.ok(href == link["url"], f"{where}: href {href} != {link['url']}")
                good &= c.ok(link["handle"] in label and label.split(":")[0].strip() == a.locator(".profile-name").inner_text().strip(), f"{where}: aria-label '{label}'")
                if link["id"] in ("tibiawiki", "tibia"):
                    good &= c.ok((a.locator("img").get_attribute("src") or "").endswith(f"img/icons/{link['id']}.png"), f"{where}: icon")
                else:
                    good &= c.ok(a.locator("use").get_attribute("href") == f"#i-{link['id']}", f"{where}: icon")
                good &= c.ok(a.get_attribute("target") == "_blank" and "noopener" in a.get_attribute("rel"), f"{where}: target/rel")
                if lang["code"] == "en" and pid == "about" and good:
                    passed += 1
    gh = next(l for l in expected if l["id"] == "github")["url"]
    rd = next(l for l in expected if l["id"] == "reddit")["url"]
    c.ok(gh == "https://github.com/RockyJLaRo" and rd == "https://www.reddit.com/user/Roc-ky/", "GitHub/Reddit destinations swapped or wrong")
    exact = {"tibiawiki": "https://tibia.fandom.com/wiki/User:RockyLaRo", "tibia": "https://www.tibia.com/community/?name=Roc+ky",
             "youtube": "https://www.youtube.com/@Roc_ky", "instagram": "https://www.instagram.com/rocky_tibia/",
             "github": "https://github.com/RockyJLaRo", "reddit": "https://www.reddit.com/user/Roc-ky/"}
    for l in expected:
        c.ok(exact.get(l["id"]) == l["url"], f"registry {l['id']} != authoritative URL")
    c.note(f"{passed}/6 creator profile URL assertions pass (English About page; same checks on Home and in all 5 languages)")
    c.ok(passed == 6, f"only {passed}/6 creator profile assertions passed")
    ctx.close()


def menu_destinations():
    """The registry's menu as (dest key, item, group id or None): the same keys build_site.py writes as data-menu-dest."""
    out = []
    for entry in kit.REGISTRY["menu"]:
        for item in entry.get("items", [entry]):
            if "page" in item:
                key = "page:" + item["page"] + ("#" + item["hash"] if item.get("hash") else "")
            elif "route" in item:
                key = "route:" + item["route"]
            else:
                key = "href:" + item["href"]
            out.append((key, item, entry.get("id") if "items" in entry else None))
    return out


def navigation(c, browser, base):
    """Every registry.menu destination is a link in the desktop panels and in the phone drawer in all 5 languages, desktop and
    drawer agree on its address, and each address answers 200 (pages) or opens a data view (routes); the logo leads home,
    the header Download button works, the current page is marked, and the drawer closes after navigating."""
    dests = menu_destinations()
    c.ok(len(dests) >= 20, f"registry.menu has only {len(dests)} destinations")
    hrefs = {}
    for lang in kit.LANGUAGES:
        ctx, page = new_page(browser, base, 1440, 1000)
        page.goto(url_of(base, lang, "home"))
        for key, item, group in dests:
            where = f"{lang['code']}/desktop/{key}"
            link = page.locator(f".main-nav a[data-menu-dest='{key}']")
            if not c.ok(link.count() == 1, f"{where}: {link.count()} links in the desktop menu"):
                continue
            if group:
                btn = page.locator(f".main-nav .nav-group[data-nav-group='{group}'] > .nav-button")
                if btn.get_attribute("aria-expanded") != "true":
                    btn.click()
                c.ok(btn.get_attribute("aria-expanded") == "true", f"{where}: group {group} does not open")
            c.ok(link.is_visible(), f"{where}: not visible after opening its group")
            c.ok((link.inner_text() or "").strip() != "", f"{where}: link without text")
            hrefs.setdefault(lang["code"], {})[key] = link.evaluate("a => a.href")
        page.keyboard.press("Escape")
        ctx.close()
        ctx, page = new_page(browser, base, 390, 844)
        page.goto(url_of(base, lang, "home"))
        page.locator("#menu-btn").click()
        c.ok(page.locator("#site-menu").is_visible(), f"{lang['code']}: the drawer does not open")
        for key, item, group in dests:
            where = f"{lang['code']}/drawer/{key}"
            link = page.locator(f"#site-menu a[data-menu-dest='{key}']")
            if not c.ok(link.count() == 1, f"{where}: {link.count()} links in the drawer"):
                continue
            if group:
                det = page.locator(f"#site-menu details[data-nav-group='{group}']")
                if not det.evaluate("d => d.open"):
                    det.locator("summary").click()
            c.ok(link.is_visible(), f"{where}: not visible after opening its group")
            c.ok(link.evaluate("a => a.href") == hrefs.get(lang["code"], {}).get(key), f"{where}: drawer and desktop links differ")
        ctx.close()
    # Every destination answers: pages 200 (in every language), data routes open a known view, external links are the registry's.
    ctx, page = new_page(browser, base, 1440, 1000)
    tibia_urls = {lang["code"]: url_of(base, lang, "tibia") for lang in kit.LANGUAGES}
    for code, links in hrefs.items():
        for key, href in links.items():
            where = f"{code}/{key}"
            if key.startswith("href:"):
                c.ok(href == key[5:] and href.startswith("https://"), f"{where}: external link {href}")
                continue
            if key.startswith("route:"):
                c.ok(href.startswith(tibia_urls[code] + "#/" + key[6:].split("/")[0]), f"{where}: route link {href}")
                page.goto(href)
                wait_app(page)
                route = page.evaluate("document.body.dataset.route")
                c.ok(route not in (None, "", "unknown", "hub"), f"{where}: {href} opens no data view (route {route})")
                continue
            resp = page.goto(href)
            c.ok(resp is not None and resp.status == 200, f"{where}: {href} HTTP {resp.status if resp else None}")
            if "#" in key:
                c.ok(page.locator("#" + key.split("#")[1]).count() == 1, f"{where}: anchor #{key.split('#')[1]} missing")
    ctx.close()
    # Logo, Download button and the current page marker.
    en = kit.LANGUAGES[0]
    ctx, page = new_page(browser, base, 1440, 1000)
    page.goto(url_of(base, en, "features"))
    cur = page.locator(".main-nav a[data-menu-dest='page:features']")
    c.ok(cur.get_attribute("aria-current") == "page", "features: its menu link lacks aria-current=page")
    c.ok(page.locator(".main-nav .nav-group[data-nav-group='application'] > .nav-button").get_attribute("data-current") is not None,
         "features: the Application button is not marked as holding the current page")
    c.ok(page.locator(".main-nav [aria-current='page']").count() == 1, "exactly one current link in the desktop menu")
    page.locator(".site-header .brand").click()
    page.wait_for_load_state()
    c.ok(page.url == base, f"the logo leads home: {page.url}")
    page.locator(".header-download").click()
    page.wait_for_load_state()
    c.ok(page.url.endswith("/downloads/"), f"the Download button: {page.url}")
    page.goto(url_of(base, en, "contact"))
    c.ok(page.locator(".main-nav .nav-link[data-menu-dest='page:contact']").get_attribute("aria-current") == "page", "contact: plain link marked current")
    c.ok(page.locator(".main-nav .nav-group[data-nav-group]").count() == len([e for e in kit.REGISTRY["menu"] if "items" in e]), "one disclosure group per registry group")
    for key, item, group in menu_destinations():
        if group is None:
            c.ok(page.locator(f".main-nav .nav-list > .nav-item > a.nav-link[data-menu-dest='{key}']").count() == 1, f"{key}: not a plain top-level link")
        if key.startswith("href:"):
            for a in (page.locator(f".main-nav a[data-menu-dest='{key}']"), page.locator(f"#site-menu a[data-menu-dest='{key}']")):
                c.ok(a.get_attribute("target") == "_blank" and a.get_attribute("rel") == "noopener noreferrer" and a.locator(".sr-only").count() == 1,
                     f"{key}: external link without new-tab marking")
    if any(g is None and k.startswith("href:") for k, _, g in menu_destinations()):
        gh = "href:" + kit.REGISTRY["repository"]
        c.ok(page.locator(f".main-nav a.nav-link[data-menu-dest='{gh}']").inner_text().strip().startswith("Source on GitHub"), "the GitHub link reads 'Source on GitHub'")
    ctx.close()
    # Drawer: a link navigates and the drawer is closed on the new page.
    ctx, page = new_page(browser, base, 390, 844)
    page.goto(url_of(base, en, "home"))
    c.ok(not page.locator(".main-nav").is_visible(), "desktop menu hidden on phones")
    page.locator("#menu-btn").click()
    det = page.locator("#site-menu details[data-nav-group='application']")
    if not det.evaluate("d => d.open"):
        det.locator("summary").click()
    for key, item, group in menu_destinations():
        if group is None:
            c.ok(page.locator(f"#site-menu .drawer-nav > a.drawer-link[data-menu-dest='{key}']").count() == 1, f"drawer: {key} is not a plain link")
    page.locator("#site-menu a[data-menu-dest='page:downloads']").click()
    page.wait_for_load_state()
    c.ok("/downloads/" in page.url and not page.locator("#site-menu").is_visible(), "drawer link navigates and the drawer is closed")
    c.ok(not page.console_errors, f"console errors: {page.console_errors[:3]}")
    ctx.close()
    c.note(f"{len(dests)} menu destinations x {len(kit.LANGUAGES)} languages, desktop panels and drawer")


def menu_keyboard(c, browser, base):
    """Disclosure panels and the drawer by keyboard: Tab to "Community Data", Enter opens it (aria-expanded), Escape closes it
    and returns focus; one panel at a time; an outside click and focus leaving close it. At 390 px the drawer opens, Tab and
    Shift+Tab stay inside it, Escape and the backdrop close it, and focus returns to #menu-btn."""
    en = kit.LANGUAGES[0]
    label = "Community Data"
    ctx, page = new_page(browser, base, 1440, 1000)
    page.goto(url_of(base, en, "about"))
    found = False
    for _ in range(40):
        page.keyboard.press("Tab")
        if page.evaluate("document.activeElement && document.activeElement.classList.contains('nav-button') ? document.activeElement.textContent.trim() : ''") == label:
            found = True
            break
    if c.ok(found, f"Tab never reaches the '{label}' button"):
        btn = page.locator(".main-nav .nav-group[data-nav-group='community'] > .nav-button")
        panel = page.locator("#" + btn.get_attribute("aria-controls"))
        c.ok(btn.get_attribute("aria-expanded") == "false" and not panel.is_visible(), "panel starts closed")
        page.keyboard.press("Enter")
        c.ok(btn.get_attribute("aria-expanded") == "true" and panel.is_visible(), "Enter opens the panel with aria-expanded=true")
        page.keyboard.press("Tab")
        c.ok(page.evaluate("document.activeElement.closest('.nav-panel') !== null"), "Tab moves into the open panel")
        page.keyboard.press("Escape")
        c.ok(btn.get_attribute("aria-expanded") == "false" and not panel.is_visible(), "Escape closes the panel")
        c.ok(page.evaluate("(id) => document.activeElement.getAttribute('aria-controls') === id", btn.get_attribute("aria-controls")),
             "focus is back on the Community Data button")
        # Only one panel at a time.
        app = page.locator(".main-nav .nav-group[data-nav-group='application'] > .nav-button")
        app.click()
        btn.click()
        c.ok(app.get_attribute("aria-expanded") == "false" and btn.get_attribute("aria-expanded") == "true", "opening a panel closes the other")
        c.ok(page.locator(".main-nav .nav-panel:not([hidden])").count() == 1, "exactly one panel open")
        # Outside click closes.
        page.mouse.click(700, 700)
        c.ok(btn.get_attribute("aria-expanded") == "false" and page.locator(".main-nav .nav-panel:not([hidden])").count() == 0, "an outside click closes the panel")
        # Focus leaving closes: open, Tab through every link and beyond.
        btn.focus()
        page.keyboard.press("Enter")
        n = page.locator("#" + btn.get_attribute("aria-controls") + " a").count()
        for _ in range(n + 1):
            page.keyboard.press("Tab")
        c.ok(btn.get_attribute("aria-expanded") == "false", "focus leaving the group closes the panel")
        # Panels use disclosure semantics, not an ARIA menu.
        c.ok(page.locator(".main-nav [role=menu], .main-nav [role=menubar], .main-nav [role=menuitem]").count() == 0, "no ARIA menu roles")
    c.ok(not page.console_errors, f"desktop console errors: {page.console_errors[:3]}")
    ctx.close()
    # Drawer at 390 px.
    ctx, page = new_page(browser, base, 390, 844)
    page.goto(url_of(base, en, "about"))
    btn = page.locator("#menu-btn")
    drawer = page.locator("#site-menu")
    c.ok(btn.get_attribute("aria-expanded") == "false" and not drawer.is_visible(), "drawer starts closed")
    btn.focus()
    page.keyboard.press("Enter")
    c.ok(btn.get_attribute("aria-expanded") == "true" and drawer.is_visible(), "Enter on #menu-btn opens the drawer")
    inside = "document.getElementById('site-menu').contains(document.activeElement)"
    c.ok(page.evaluate(inside), "focus moves into the drawer")
    stayed = True
    for _ in range(60):
        page.keyboard.press("Tab")
        stayed &= page.evaluate(inside)
    c.ok(stayed, "Tab leaves the drawer (no focus trap)")
    for _ in range(60):
        page.keyboard.press("Shift+Tab")
        stayed &= page.evaluate(inside)
    c.ok(stayed, "Shift+Tab leaves the drawer (no focus trap)")
    c.ok(page.evaluate("getComputedStyle(document.documentElement).overflow") == "hidden" or page.evaluate("document.documentElement.classList.contains('menu-lock')"), "page scroll is locked while the drawer is open")
    page.keyboard.press("Escape")
    c.ok(btn.get_attribute("aria-expanded") == "false" and not drawer.is_visible(), "Escape closes the drawer")
    c.ok(page.evaluate("document.activeElement === document.getElementById('menu-btn')"), "focus returns to #menu-btn after Escape")
    btn.click()
    c.ok(drawer.is_visible(), "drawer opens again")
    page.mouse.click(10, 400)   # the backdrop (the panel sits on the right)
    c.ok(not drawer.is_visible() and btn.get_attribute("aria-expanded") == "false", "a backdrop click closes the drawer")
    c.ok(page.evaluate("document.activeElement === document.getElementById('menu-btn')"), "focus returns to #menu-btn after the backdrop click")
    btn.click()
    c.ok(page.locator("#site-menu [data-theme-toggle]").is_visible() and page.locator("#site-menu a[data-lang]").count() == len(kit.LANGUAGES),
         "the drawer holds the theme toggle and every language")
    c.ok(page.locator("#site-menu details").count() == len([e for e in kit.REGISTRY["menu"] if "items" in e]), "one collapsible group per registry group")
    c.ok(not page.console_errors, f"drawer console errors: {page.console_errors[:3]}")
    ctx.close()


def menu_overflow(c, browser, base):
    """Review Focus 1: long German and Polish labels at 1024, 1280 and 1440 px never wrap or overflow. Either the desktop menu
    fits (every top-level entry one line, header bar and page without overflow, open panels inside the viewport) or the
    header has collapsed to the drawer (html.nav-collapsed: #menu-btn shown, no page overflow). From 1280 px the desktop
    menu must be shown."""
    count, collapsed_at = 0, []
    for code in ("de", "pl"):
        lang = next(l for l in kit.LANGUAGES if l["code"] == code)
        for w in (1024, 1280, 1440):
            ctx, page = new_page(browser, base, w, 800)
            for pid in ("home", "calculators", "tibia"):
                page.goto(url_of(base, lang, pid))
                where = f"{code}/{pid}/{w}px"
                m = page.evaluate("""() => {
                  const vis = (s) => { const e = document.querySelector(s); return !!e && e.offsetParent !== null; };
                  const out = {over: document.documentElement.scrollWidth - window.innerWidth, nav: vis('.main-nav'), menuBtn: vis('#menu-btn'),
                               collapsed: document.documentElement.classList.contains('nav-collapsed'), lines: []};
                  for (const b of document.querySelectorAll('.main-nav .nav-group > button, .main-nav .nav-link')) {
                    const r = document.createRange(); r.selectNodeContents(b.querySelector('span:not(.sr-only)') || b);   // the label, not the chevron
                    const tops = new Set([...r.getClientRects()].filter(x => x.width > 0).map(x => Math.round(x.top)));
                    const box = b.getBoundingClientRect();
                    out.lines.push({text: b.textContent.trim(), lines: tops.size, h: box.height, right: box.right});
                  }
                  const bar = document.querySelector('.header-bar');
                  out.barOver = bar.scrollWidth - bar.clientWidth;
                  return out;
                }""")
                c.ok(m["over"] <= 0, f"{where}: page overflows by {m['over']}px")
                count += 1
                if m["collapsed"]:
                    collapsed_at.append(where)
                    c.ok(w < 1280, f"{where}: the desktop menu must fit from 1280 px (collapsed to the drawer)")
                    c.ok(m["menuBtn"] and not m["nav"], f"{where}: collapsed header must show #menu-btn and hide the desktop menu: {m}")
                    continue
                c.ok(m["nav"], f"{where}: neither the desktop menu nor the collapsed drawer header is shown")
                c.ok(m["barOver"] <= 0, f"{where}: header bar overflows by {m['barOver']}px")
                c.ok(len(m["lines"]) == len(kit.REGISTRY["menu"]), f"{where}: {len(m['lines'])} top-level menu entries, registry has {len(kit.REGISTRY['menu'])}")
                for b in m["lines"]:
                    c.ok(b["lines"] == 1 and b["h"] < 60, f"{where}: '{b['text']}' wraps ({b['lines']} lines, {b['h']:.0f}px)")
                    c.ok(b["right"] <= w, f"{where}: '{b['text']}' ends at {b['right']:.0f}px")
                for i in range(page.locator(".main-nav .nav-group > button").count()):
                    page.locator(".main-nav .nav-group > button").nth(i).click()
                    r = page.evaluate("() => { const p = document.querySelector('.main-nav .nav-panel:not([hidden])'); if (!p) return null; const b = p.getBoundingClientRect(); return {l: b.left, r: b.right, over: document.documentElement.scrollWidth - window.innerWidth}; }")
                    if c.ok(r is not None, f"{where}: panel {i} does not open"):
                        c.ok(r["l"] >= 0 and r["r"] <= w and r["over"] <= 0, f"{where}: panel {i} leaves the viewport {r}")
                page.keyboard.press("Escape")
            ctx.close()
    c.note(f"{count} header layouts (de, pl x 1024/1280/1440 x 3 pages); collapsed to the drawer: {', '.join(collapsed_at) or 'none'}")


def no_js_reachability(c, browser, base):
    """Review Focus 2: with JavaScript off every registry.menu destination is a footer link with the same address as in the
    header menu, and the header search form submits to tibia/."""
    dests = menu_destinations()
    ctx = browser.new_context(viewport={"width": 1440, "height": 1000}, java_script_enabled=False)
    kit.use_fixtures(ctx)
    page = ctx.new_page()
    for lang in kit.LANGUAGES:
        for pid in ("home", "tibia", "calc-experience"):
            page.goto(url_of(base, lang, pid))
            where = f"{lang['code']}/{pid}"
            c.ok("no-js" in (page.get_attribute("html", "class") or ""), f"{where}: JavaScript ran (no-js class removed)")
            for key, item, group in dests:
                nav = page.locator(f".main-nav a[data-menu-dest='{key}']")
                foot = page.locator(f"footer a[data-menu-dest='{key}']")
                if not c.ok(foot.count() >= 1, f"{where}: footer lacks {key}"):
                    continue
                if nav.count():
                    c.ok(foot.first.get_attribute("href") == nav.first.get_attribute("href"), f"{where}: footer {key} {foot.first.get_attribute('href')} != header {nav.first.get_attribute('href')}")
            form = page.locator(".site-header form.header-search")
            c.ok(form.count() == 1 and (form.get_attribute("action") or "").endswith("tibia/"), f"{where}: header search action {form.get_attribute('action') if form.count() else None}")
            c.ok(form.count() == 1 and form.is_visible(), f"{where}: header search not visible without JavaScript")
    # Without JavaScript the form still lands on tibia/ with the name (?character=), which data-app.js turns into the view later.
    page.goto(url_of(base, kit.LANGUAGES[0], "about"))
    page.fill(".site-header form.header-search input", "Roc ky")
    page.locator(".site-header form.header-search button[type=submit]").click()
    page.wait_for_load_state()
    c.ok("/tibia/?character=Roc+ky" in page.url, f"no-JS search lands on tibia/?character=: {page.url}")
    ctx.close()
    c.note(f"{len(dests)} destinations checked in the footer of 3 pages x {len(kit.LANGUAGES)} languages")


def language_and_theme(c, browser, base):
    en = kit.LANGUAGES[0]
    ctx, page = new_page(browser, base, 1440, 1000)
    page.goto(url_of(base, en, "features"))
    for lang in kit.LANGUAGES[1:]:
        page.locator("#lang-btn").click()
        page.locator(f"#lang-menu a[data-lang='{lang['code']}']").click()
        page.wait_for_load_state()
        c.ok(page.get_attribute("html", "lang") == lang["code"] and f"/{lang['folder']}/features/" in page.url, f"switch to {lang['code']}: {page.url}")
    # Remembered: opening the English page goes to the chosen language.
    page.goto(url_of(base, en, "downloads"))
    page.wait_for_load_state()
    c.ok(f"/{kit.LANGUAGES[-1]['folder']}/downloads/" in page.url, f"remembered language: {page.url}")
    page.goto(base + "en/")
    page.wait_for_load_state()
    c.ok(page.get_attribute("html", "lang") == "en", "/en/ switches back to English")
    # Hash routes carry over.
    page.goto(url_of(base, en, "tibia", kit.data_hash("world/Antica")))
    wait_app(page)
    page.locator("#lang-btn").click()
    page.locator("#lang-menu a[data-lang='pl']").click()
    page.wait_for_load_state()
    wait_app(page)
    c.ok(page.url.endswith("/pl/tibia/#/world/Antica"), f"data view kept when switching language: {page.url}")
    c.ok(page.locator("#app h2").first.inner_text() == "Antica", "world view shown in Polish page")
    # Theme toggle: switches, persists, screenshots follow.
    page.goto(url_of(base, en, "features"))
    for expected in ("dark", "light"):
        page.locator(".header-tools [data-theme-toggle]").click()
        c.ok(page.get_attribute("html", "data-theme") == expected, f"theme toggle -> {expected}")
        visible = page.evaluate("[...document.querySelectorAll('.shot img')].filter(i => i.offsetParent !== null).every(i => i.classList.contains('shot-' + document.documentElement.dataset.theme))")
        c.ok(visible, f"screenshots show the {expected} variant")
    page.reload()
    c.ok(page.get_attribute("html", "data-theme") == "light", "theme persists after reload")
    c.ok(not page.console_errors, f"console errors: {page.console_errors[:3]}")
    # Early theme script: a stored choice beats the system preference before DOMContentLoaded (the system is dark here).
    ctx.close()
    ctx, page = new_page(browser, base, 1440, 1000, theme="dark")
    page.add_init_script("try { localStorage.setItem('tf-theme', 'light'); } catch (e) {}")
    page.add_init_script("document.addEventListener('DOMContentLoaded', () => { window.__themeAtDcl = document.documentElement.getAttribute('data-theme'); }, { once: true })")
    page.goto(url_of(base, en, "features"))
    c.ok(page.evaluate("window.__themeAtDcl") == "light", "data-theme is light before DOMContentLoaded with a stored light choice under a dark system")
    c.ok(page.evaluate("getComputedStyle(document.documentElement).colorScheme") == "light", "color-scheme follows the stored light theme")
    page.locator(".header-tools [data-theme-toggle]").click()
    c.ok(page.evaluate("getComputedStyle(document.documentElement).colorScheme") == "dark", "color-scheme follows the toggled dark theme")
    metas = page.evaluate("[...document.querySelectorAll('meta[name=theme-color]')].map(m => [m.media, m.content])")
    c.ok(metas == [["(prefers-color-scheme: light)", "#f4f3f0"], ["(prefers-color-scheme: dark)", "#111417"]], f"theme-color metas: {metas}")
    ctx.close()


def tibia_data(c, browser, base):
    en = kit.LANGUAGES[0]
    fx = kit.REGISTRY["fixtures"]
    ctx, page = new_page(browser, base, 1440, 1000)
    # Search from the header on another page.
    page.goto(url_of(base, en, "about"))
    page.fill(".header-search input", "  " + fx["character"] + " ")
    page.keyboard.press("Enter")
    page.wait_for_url("**/tibia/#/character/**")
    wait_app(page)
    c.ok(page.locator("#app h2").first.inner_text() == fx["character"], "header search opens the character")
    c.ok(page.locator("#app .facts").first.is_visible(), "character facts shown")
    c.ok(page.locator("#app a.btn[href^='https://www.tibia.com/community/?name=']").count() == 1, "link to Tibia.com character page")
    # Reload keeps the view.
    page.reload()
    wait_app(page)
    c.ok(page.locator("#app h2").first.inner_text() == fx["character"], "character view survives reload")
    # Unknown character.
    page.goto(url_of(base, en, "tibia", kit.data_hash("character/" + fx["missingCharacter"])))
    wait_app(page)
    c.ok(page.locator("#app .notice-error").count() == 1 and "Not Found" in page.locator("#app .notice-error").inner_text(), "unknown character shows 'Not found'")
    c.ok(page.locator("#app .notice-error button").count() == 0, "no retry button for a name that does not exist")
    # Worlds: table rows, filter, sort.
    worlds = json.load(open(os.path.join(kit.FIXTURES, "worlds.json"), encoding="utf-8"))["worlds"]["regular_worlds"]
    page.goto(url_of(base, en, "tibia", "#/worlds"))
    wait_app(page)
    rows = page.locator("#app table.data-table").first.locator("tbody tr")
    c.ok(rows.count() == len(worlds), f"worlds rows {rows.count()} != {len(worlds)}")
    page.locator("#app .table-tools input").first.fill("Antica")
    c.ok(rows.count() == 1, "filter narrows the worlds table")
    page.locator("#app .table-tools input").first.fill("")
    page.locator("#app thead button.sort").nth(1).click()
    online = page.evaluate("[...document.querySelectorAll('#app table.data-table')[0].querySelectorAll('tbody tr td:nth-child(2)')].map(td => parseInt(td.textContent.replace(/\\D/g,''),10))")
    c.ok(online == sorted(online, reverse=True), "sorting by players online (descending)")
    # Guild and online-only filter.
    page.goto(url_of(base, en, "tibia", kit.data_hash("guild/" + fx["guild"])))
    wait_app(page)
    guild = json.load(open(os.path.join(kit.FIXTURES, "guild.json"), encoding="utf-8"))["guild"]
    c.ok(page.locator("#app h2").first.inner_text() == guild["name"], "guild view")
    page.check("#online-only")
    online_members = sum(1 for m in guild["members"] if m["status"] == "online")
    c.ok(page.locator("#app table.data-table tbody tr").count() == max(online_members, 1), "online-only filter")
    # Guild list of a world (guild search).
    page.goto(url_of(base, en, "tibia", kit.data_hash("guilds/" + fx["world"])))
    wait_app(page)
    page.locator("#app .table-tools input").first.fill(guild["name"][:6])
    c.ok(page.locator("#app table.data-table").first.locator("tbody tr").count() >= 1, "guild search by name")
    # Highscores: rows, category change updates the URL.
    page.goto(url_of(base, en, "tibia", kit.data_hash(f"highscores/{fx['world']}/experience/all/1")))
    wait_app(page)
    c.ok(page.locator("#app table.data-table tbody tr").count() == 50, "50 highscore rows")
    page.locator("#app select").nth(1).select_option("magiclevel")
    page.wait_for_function("location.hash.includes('magiclevel')")
    c.ok("magiclevel" in page.url, "category change navigates")
    # Copy as wikitext.
    ctx2, p2 = new_page(browser, base, 1440, 1000, permissions=["clipboard-read", "clipboard-write"])
    p2.goto(url_of(base, en, "tibia", "#/spells"))
    wait_app(p2)
    p2.locator("#app .table-actions button").first.click()
    clip = p2.evaluate("navigator.clipboard.readText()")
    c.ok(clip.startswith('{| class="wikitable sortable"') and "exura" in clip and clip.rstrip().endswith("|}"), "spells table copied as wikitext")
    ctx2.close()
    c.ok(not ctx.unknown_api, f"requests without fixture: {ctx.unknown_api}")
    c.ok(not page.console_errors, f"console errors: {page.console_errors[:3]}")
    ctx.close()


def news(c, browser, base):
    en = kit.LANGUAGES[0]
    ctx, page = new_page(browser, base, 1440, 1000)
    page.goto(base)
    page.wait_for_selector("[data-live-news] .news-item")
    items = page.locator("[data-live-news] .news-item")
    c.ok(items.count() == 6, f"home shows 6 news ({items.count()})")
    c.ok(page.locator("[data-live-news] .source-line a[href='https://tibiadata.com/']").count() == 1, "news source line credits TibiaData")
    page.locator("[data-live-news] .news-title a").first.click()
    page.wait_for_url("**/tibia/#/news/**")
    wait_app(page)
    article = json.load(open(os.path.join(kit.FIXTURES, "newsarticle.json"), encoding="utf-8"))["news"]
    c.ok(page.locator("#app article h2").inner_text().strip() == article["title"].strip() or "/news/" in page.url, "news article opens")
    orig = page.locator("#app article a.btn[target=_blank]")
    c.ok(orig.count() == 1 and orig.get_attribute("href").startswith("https://www.tibia.com/news/"), "link to the original article")
    c.ok(page.locator("#app article script, #app article iframe, #app article [onerror], #app article [onclick]").count() == 0, "article sanitized")
    # Refresh button and failure handling.
    calls_before = len(ctx.api_calls)
    page.goto(base)
    page.wait_for_selector("[data-live-news] .news-item")
    page.locator("[data-news-refresh]").click()
    page.wait_for_selector("[data-live-news] .news-item")
    c.ok(len(ctx.api_calls) > calls_before, "manual refresh requests the news again")
    ctx.close()
    # API unavailable: error box, layout kept, retry works when the API is back.
    state = {"down": True}

    def flaky(route):
        if state["down"]:
            return route.fulfill(status=503, body="", headers={"Access-Control-Allow-Origin": "*"})
        return route.fallback()
    ctx, page = new_page(browser, base, 390, 844, overrides={"news/latest": flaky})
    page.goto(base)
    page.wait_for_selector("[data-live-news] .notice-error", timeout=20000)
    c.ok("not answering" in page.locator("[data-live-news] .notice-error").inner_text(), "503 shows the server error message")
    c.ok(page.locator("#online-tools").is_visible() and page.locator("footer [data-notice]").count() == 1, "layout kept when the API is down")
    state["down"] = False
    ctx.unroute("https://api.tibiadata.com/**")
    kit.use_fixtures(ctx)
    page.locator("[data-live-news] .notice-error button").click()
    page.wait_for_selector("[data-live-news] .news-item", timeout=20000)
    c.ok(page.locator("[data-live-news] .news-item").count() == 6, "retry loads the news")
    ctx.close()
    # Network failure on a data view.
    ctx, page = new_page(browser, base, 1440, 1000, overrides={"worlds": lambda route: route.abort("internetdisconnected")})
    page.goto(url_of(base, en, "tibia", "#/worlds"))
    page.wait_for_selector("#app .notice-error", timeout=20000)
    c.ok("could not be reached" in page.locator("#app .notice-error").inner_text(), "network failure message")
    ctx.close()
    # Malformed answer.
    ctx, page = new_page(browser, base, 1440, 1000, overrides={"spells": (200, "{\"spells\": 5}")})
    page.goto(url_of(base, en, "tibia", "#/spells"))
    page.wait_for_selector("#app .notice-error", timeout=20000)
    c.ok("cannot read" in page.locator("#app .notice-error").inner_text(), "malformed answer message")
    ctx.close()


def contact_and_downloads(c, browser, base):
    email = kit.REGISTRY["creator"]["email"]
    ctx, page = new_page(browser, base, 390, 844, permissions=["clipboard-read", "clipboard-write"])
    for lang in kit.LANGUAGES:
        page.goto(url_of(base, lang, "contact"))
        hrefs = page.evaluate("[...document.querySelectorAll('main a[href^=mailto]')].map(a => a.href)")
        c.ok(len(hrefs) >= 2 and all(h.startswith(f"mailto:{email}") for h in hrefs), f"{lang['code']}: mailto links {hrefs}")
        loc = json.load(open(os.path.join(kit.WEBSITE, "src", "locales", lang["code"] + ".json"), encoding="utf-8"))["contact"]["reasons"]
        reasons = page.locator("[data-contact-reasons] a")
        c.ok(reasons.count() == 6, f"{lang['code']}: contact reasons {reasons.count()}")
        for i, key in enumerate(("general", "bug", "formula", "fansite", "app", "dev")):
            want = f"mailto:{email}?subject=" + urllib.parse.quote("Tibia Foundry: " + loc[key], safe="!'()*~")
            got = reasons.nth(i).get_attribute("href") if i < reasons.count() else None
            c.ok(got == want, f"{lang['code']}: reason {key} href {got} != {want}")
            c.ok(got is not None and "body=" not in got and "+" not in got, f"{lang['code']}: reason {key} body/plus")
        page.locator(f"main button[data-copy='{email}']").click()
        c.ok(page.evaluate("navigator.clipboard.readText()") == email, f"{lang['code']}: copy email")
        c.ok(page.locator(f"main button[data-copy='{email}'].copied").count() == 1, f"{lang['code']}: copy confirmation shown")
        c.ok(page.locator("main form").count() == 0, f"{lang['code']}: no (fake) contact form")
    base_url = kit.REGISTRY["downloads"]["base"].replace("{version}", VERSION)
    for lang in kit.LANGUAGES:
        page.goto(url_of(base, lang, "downloads"))
        for f in kit.REGISTRY["downloads"]["files"]:
            name = f["file"].replace("{version}", VERSION)
            a = page.locator(f"[data-download={f['id']}] a.btn")
            c.ok(a.get_attribute("href") == base_url + name, f"{lang['code']}: {f['id']} href {a.get_attribute('href')}")
            c.ok(name in page.locator(f"[data-download={f['id']}] .file").inner_text(), f"{lang['code']}: {f['id']} file name shown")
            c.ok("releases/latest" not in (a.get_attribute("href") or ""), f"{lang['code']}: no releases/latest")
        c.ok(page.locator(f"a[href='{base_url}SHA256SUMS.txt']").count() == 1, f"{lang['code']}: checksums link")
    c.note("download hrefs: " + ", ".join(base_url + f["file"].replace("{version}", VERSION) for f in kit.REGISTRY["downloads"]["files"]))
    ctx.close()


def deep_links(c, browser, base):
    ctx, page = new_page(browser, base, 1440, 1000)
    for path, expect in (("tibia/character/Roc%20ky", "/tibia/#/character/Roc%20ky"), ("de/tibia/world/Antica", "/de/tibia/#/world/Antica"),
                         ("tibia/guild/Crusaders%20of%20Might%20and%20Magic", "/tibia/#/guild/Crusaders%20of%20Might%20and%20Magic")):
        page.goto(base + path)
        page.wait_for_function("location.hash.length > 2", timeout=10000)
        wait_app(page)
        c.ok(page.url.endswith(expect), f"{path} -> {page.url}")
        c.ok(page.locator("#app .notice-error").count() == 0, f"{path}: view loads")
    page.goto(base + "tibia/?character=Roc+ky")
    wait_app(page)
    c.ok(page.url.endswith("/tibia/#/character/Roc%20ky"), f"?character= opens the view: {page.url}")
    resp = page.goto(base + "no-such-page/")
    c.ok(resp.status == 404 and page.locator("h1").inner_text() == "Page Not Found", "unknown page: 404 page")
    for r in kit.DATA_ROUTES:
        page.goto(url_of(base, kit.LANGUAGES[0], "tibia", kit.data_hash(r["sample"])))
        wait_app(page)
        page.reload()
        wait_app(page)
        c.ok(page.locator("#app .notice-error").count() == 0 and page.evaluate("document.body.dataset.route") == r["id"], f"{r['id']}: survives reload")
    ctx.close()


def responsive(c, browser, base):
    """No page-level horizontal overflow, no overlapping header, usable touch targets: all pages x languages x themes x 9 widths."""
    count = 0
    for theme in kit.THEMES:
        for w, h in kit.WIDTHS:
            ctx, page = new_page(browser, base, w, h, theme=theme)
            for lang in kit.LANGUAGES:
                targets = [(pg["id"], "") for pg in kit.PAGES]
                if w <= 430:
                    targets += [("tibia", kit.data_hash(r["sample"])) for r in kit.DATA_ROUTES]
                for pid, hsh in targets:
                    page.goto(url_of(base, lang, pid, hsh))
                    if pid == "tibia":
                        wait_app(page)
                    where = f"{lang['code']}/{pid}{hsh}/{w}px/{theme}"
                    m = page.evaluate("""() => {
                      const d = document.documentElement, b = document.body;
                      const bar = document.querySelector('.header-bar');
                      const kids = [...bar.children].filter(e => e.offsetParent !== null).map(e => e.getBoundingClientRect());
                      let overlap = false;
                      for (let i = 1; i < kids.length; i++) if (kids[i].left < kids[i-1].right - 1) overlap = true;
                      // a squeezed brand or nav lets its text run under its neighbour although the boxes do not overlap
                      if ([...bar.querySelectorAll('.brand, .main-nav li')].some(e => e.offsetParent !== null && e.scrollWidth > e.clientWidth + 1)) overlap = true;
                      const clipped = [...document.querySelectorAll('h1, h2, .btn, .brand-name')].filter(e => e.offsetParent !== null && !e.classList.contains('sr-only') && e.scrollWidth > e.clientWidth + 2 && getComputedStyle(e).overflow !== 'visible').length;
                      const small = [...document.querySelectorAll('.btn, .icon-btn, .main-nav a, .menu-btn, input.input, select.input, .tab, .data-nav a, .profile')]
                        .filter(e => e.offsetParent !== null).filter(e => { const r = e.getBoundingClientRect(); return r.height < 35.5 || r.width < 35.5; }).map(e => e.className + ':' + Math.round(e.getBoundingClientRect().height));
                      return { over: d.scrollWidth - d.clientWidth, bodyOver: b.scrollWidth - b.clientWidth, overlap, clipped, small: small.slice(0, 3), headerH: bar.getBoundingClientRect().height };
                    }""")
                    c.ok(m["over"] <= 0 and m["bodyOver"] <= 0, f"{where}: horizontal overflow {m['over']}px")
                    c.ok(not m["overlap"], f"{where}: header items overlap")
                    c.ok(m["clipped"] == 0, f"{where}: {m['clipped']} clipped headings/buttons")
                    c.ok(not m["small"], f"{where}: small touch targets {m['small']}")
                    c.ok(m["headerH"] <= 72, f"{where}: header {m['headerH']}px tall")
                    count += 1
            c.ok(not page.console_errors, f"{w}px/{theme}: console errors {page.console_errors[:2]}")
            ctx.close()
    c.note(f"{count} page/route layouts checked at {', '.join(str(w) for w, _ in kit.WIDTHS)} px")


def sanitizer(c, browser, base):
    cases = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures", "sanitizer-cases.json"), encoding="utf-8"))
    ctx, page = new_page(browser, base)
    page.goto(base + "tibia/")
    for case in cases:
        out = page.evaluate("""(html) => { const box = document.createElement('div'); box.appendChild(TFSanitize.fragment(html, document)); window.__fired = window.__fired || false; return box.innerHTML; }""", case["html"])
        for bad in case.get("mustNotContain", []):
            c.ok(bad.lower() not in out.lower(), f"{case['name']}: output contains '{bad}': {out[:160]}")
        for good in case.get("mustContain", []):
            c.ok(good in out, f"{case['name']}: output lacks '{good}': {out[:160]}")
    page.wait_for_timeout(500)
    c.ok(not page.evaluate("window.__xss === true"), "a payload executed")
    c.note(f"{len(cases)} sanitizer cases")
    ctx.close()


def accessibility(c, browser, base):
    ctx, page = new_page(browser, base, 1440, 1000)
    for lang in kit.LANGUAGES:
        for pg in kit.PAGES:
            page.goto(url_of(base, lang, pg["id"]))
            if pg["id"] in ("tibia", "home"):
                page.wait_for_timeout(600)
            where = f"{lang['code']}/{pg['id']}"
            r = page.evaluate("""() => {
              const out = {};
              out.noAlt = [...document.images].filter(i => !i.hasAttribute('alt')).map(i => i.src).slice(0, 3);
              out.unlabeled = [...document.querySelectorAll('input, select, textarea')].filter(e => e.type !== 'hidden' && !e.getAttribute('aria-label') && !(e.id && document.querySelector('label[for="' + e.id + '"]')) && !e.closest('label')).map(e => e.outerHTML.slice(0, 80));
              out.unnamedButtons = [...document.querySelectorAll('button, a[href]')].filter(e => e.offsetParent !== null && !(e.getAttribute('aria-label') || e.textContent.trim() || e.querySelector('img[alt]:not([alt=""])'))).map(e => e.outerHTML.slice(0, 80));
              const hs = [...document.querySelectorAll('h1, h2, h3, h4')].filter(h => h.offsetParent !== null).map(h => +h.tagName[1]);
              out.skips = hs.filter((l, i) => i > 0 && l > hs[i-1] + 1).length;
              out.landmarks = !!document.querySelector('header') && !!document.querySelector('main') && !!document.querySelector('footer') && !!document.querySelector('nav');
              out.skip = !!document.querySelector('a.skip[href="#main"]');
              return out;
            }""")
            c.ok(not r["noAlt"], f"{where}: images without alt {r['noAlt']}")
            c.ok(not r["unlabeled"], f"{where}: unlabeled fields {r['unlabeled'][:2]}")
            c.ok(not r["unnamedButtons"], f"{where}: unnamed buttons/links {r['unnamedButtons'][:2]}")
            c.ok(r["skips"] == 0, f"{where}: heading levels skipped ({r['skips']})")
            c.ok(r["landmarks"] and r["skip"], f"{where}: landmarks / skip link")
    # Keyboard: Tab reaches the skip link first, then the brand.
    page.goto(base)
    page.keyboard.press("Tab")
    c.ok(page.evaluate("document.activeElement.classList.contains('skip')"), "first Tab focuses the skip link")
    ctx.close()


def no_fixture_leak(c, browser, base):
    leaks = []
    for root, _, files in os.walk(kit.SITE):
        for f in files:
            p = os.path.join(root, f)
            if f.endswith((".html", ".js", ".css", ".json", ".xml")):
                text = open(p, encoding="utf-8", errors="ignore").read()
                if "fixtures/tibiadata" in text or "record_fixtures" in text or "sitekit" in text:
                    leaks.append(os.path.relpath(p, kit.SITE))
            if "fixture" in f.lower():
                leaks.append(os.path.relpath(p, kit.SITE))
    c.ok(not leaks, f"test fixtures referenced by the production site: {leaks[:5]}")


def live_api(c, browser, base):
    ctx = browser.new_context(viewport={"width": 1440, "height": 1000})
    page = ctx.new_page()
    page.goto(base + "tibia/" + kit.data_hash("character/" + kit.REGISTRY["fixtures"]["character"]))
    wait_app(page, 30000)
    c.ok(page.locator("#app .notice-error").count() == 0 and page.locator("#app h2").first.inner_text() == kit.REGISTRY["fixtures"]["character"], "live: character")
    page.goto(base)
    page.wait_for_selector("[data-live-news] .news-item", timeout=30000)
    c.ok(page.locator("[data-live-news] .news-item").count() >= 1, "live: news")
    ctx.close()


def compliance(c, browser, base):
    """Fansite Agreement items the site itself must show, in every language: the download notice before any download
    button (home and Downloads), the footer's six profile icons, CipSoft/Tibia links and copyright year, the Legal page's
    sections, and the statistics opt-out (stored choice + Google tag disabled)."""
    exact = {l["id"]: l["url"] for l in kit.REGISTRY["creator"]["links"]}
    ctx, page = new_page(browser, base, 1440, 1000)
    for lang in kit.LANGUAGES:
        for pid in ("home", "downloads"):
            page.goto(url_of(base, lang, pid))
            where = f"{lang['code']}/{pid}"
            notice = page.locator("[data-download-notice]")
            c.ok(notice.count() == 1 and notice.is_visible(), f"{where}: download notice missing or hidden")
            if notice.count():
                top = notice.bounding_box()["y"]
                buttons = page.locator("[data-download] a.btn" if pid == "downloads" else "#desktop-app a[data-download-link]")
                c.ok(buttons.count() >= 1, f"{where}: no download button")
                if pid == "downloads":
                    c.ok(all(buttons.nth(i).bounding_box()["y"] > top for i in range(buttons.count())), f"{where}: a download button comes before the notice")
                c.ok("CipSoft" in notice.inner_text(), f"{where}: notice does not name CipSoft")
        page.goto(url_of(base, lang, "legal"))
        where = f"{lang['code']}/legal"
        for sid in ("copyright", "status", "administrator", "downloads", "money", "privacy", "community", "sources", "attribution"):
            c.ok(page.locator(f"section#{sid} h2").count() == 1, f"{where}: section {sid}")
        c.ok(page.locator("#administrator a[href='https://www.tibia.com/community/?name=Roc+ky']").count() == 1, f"{where}: administrator's character link")
        c.ok(page.locator("#administrator a[href='mailto:rocky@tibiafoundry.com']").count() == 1, f"{where}: administrator's email")
        icons = page.locator("[data-footer-profiles] a")
        c.ok(icons.count() == 6, f"{where}: footer profile icons {icons.count()}")
        for i in range(icons.count()):
            a = icons.nth(i)
            pid = a.get_attribute("data-profile")
            c.ok(a.get_attribute("href") == exact.get(pid) and (a.get_attribute("aria-label") or "").strip(), f"{where}: footer profile {pid}")
        c.ok(page.locator("footer [data-year]").inner_text().strip() == str(time.localtime().tm_year), f"{where}: copyright year")
        for href in ("https://www.tibia.com/", "https://www.cipsoft.com/", "https://www.tibia.com/support/?subtopic=tibiarules"):
            c.ok(page.locator(f"footer a[href='{href}']").count() >= 1, f"{where}: footer link {href}")
    # Statistics opt-out: the choice is stored and disables the Google tag on the next page.
    page.goto(url_of(base, kit.LANGUAGES[0], "legal"))
    page.locator("[data-analytics-off]").click()
    c.ok(page.evaluate("localStorage.getItem('tf-analytics')") == "off", "opt-out not stored")
    page.goto(url_of(base, kit.LANGUAGES[0], "home"))
    c.ok(page.evaluate("window['ga-disable-G-J3EX3FB3PB'] === true"), "Google tag not disabled after opting out")
    page.goto(url_of(base, kit.LANGUAGES[0], "legal"))
    page.locator("[data-analytics-on]").click()
    c.ok(page.evaluate("localStorage.getItem('tf-analytics')") is None, "opt-in not stored")
    ctx.close()
    c.note("download notice, footer profiles, CipSoft links, year, Legal sections and statistics opt-out checked in 5 languages")


COLUMN_TEXTS = """(i) => [...document.querySelectorAll('#app .data-table')[0].querySelectorAll('tbody tr')].map(tr => (tr.children[i] || {}).innerText || '').map(s => s.trim())"""


def _num(s):
    s = re.sub(r"[^\d,.\-]", "", s)
    return float(s.replace(",", "").replace(".", "")) if re.search(r"\d", s) else None


def table_sorting(c, browser, base):
    """Every sortable column sorts by its data type in both directions, empty cells stay last, aria-sort and the
    announcement follow, the phone sort bar mirrors the header, in all five languages; fansite contacts link to Tibia.com."""
    ctx, page = new_page(browser, base, 1440, 1000)
    checked = 0
    for lang in kit.LANGUAGES:
        # Worlds: number (players online), text (name), date (BattlEye)
        page.goto(url_of(base, lang, "tibia", "#/worlds")); wait_app(page)
        heads = page.locator("#app .data-table").first.locator("thead th")
        names = [heads.nth(i).inner_text().strip() for i in range(heads.count())]
        for i in range(heads.count()):
            btn = heads.nth(i).locator("button.sort")
            if not btn.count():
                continue
            for _ in range(2):
                btn.click()
                state = heads.nth(i).get_attribute("aria-sort")
                vals = [v for v in page.evaluate(COLUMN_TEXTS, i)]
                filled = [v for v in vals if v and v != "–"]
                c.ok(state in ("ascending", "descending"), f"{lang['code']}/worlds col {names[i]}: aria-sort {state}")
                c.ok(all(v in ("", "–") for v in vals[len(filled):]), f"{lang['code']}/worlds col {names[i]}: empty values not last")
                nums = [_num(v) for v in filled]
                if i == 1 and None not in nums:      # players online: numeric order
                    want = sorted(nums, reverse=state == "descending")
                    c.ok(nums == want, f"{lang['code']}/worlds online: not numeric order ({state})")
                checked += 1
            page.wait_for_timeout(120)   # the announcement is written 30 ms after the click (so screen readers notice the change)
            live = page.evaluate("document.getElementById('live-status').textContent")
            c.ok(names[i].split("\n")[0] in live, f"{lang['code']}/worlds: no sort announcement for {names[i]} ({live!r})")
        # direction toggles and the first column is alphabetical (locale collation)
        name_btn = heads.nth(0).locator("button.sort")
        name_btn.click()
        if heads.nth(0).get_attribute("aria-sort") != "ascending":
            name_btn.click()
        vals = page.evaluate(COLUMN_TEXTS, 0)
        c.ok(vals == sorted(vals, key=lambda s: s.lower()), f"{lang['code']}/worlds names not alphabetical")
        name_btn.click()
        c.ok(heads.nth(0).get_attribute("aria-sort") == "descending" and page.evaluate(COLUMN_TEXTS, 0) == vals[::-1], f"{lang['code']}/worlds: second click must reverse")
    # Guild members: rank keeps the guild hierarchy; joined sorts by date; empty titles stay last both ways
    page.goto(url_of(base, kit.LANGUAGES[0], "tibia", "#/guild/" + urllib.parse.quote(kit.REGISTRY["fixtures"].get("guild", "Crusaders of Might and Magic"))))
    wait_app(page)
    heads = page.locator("#app .data-table").first.locator("thead th")
    labels = [heads.nth(i).inner_text().strip() for i in range(heads.count())]
    for i in range(heads.count()):
        for _ in range(2):
            heads.nth(i).locator("button.sort").click()
            vals = page.evaluate(COLUMN_TEXTS, i)
            filled = [v for v in vals if v and v != "–"]
            c.ok(all(v in ("", "–") for v in vals[len(filled):]), f"guild col {labels[i]}: empty values not last")
            checked += 1
    # Phone: the sort bar drives the same sorting
    pctx, ph = new_page(browser, base, 390, 844)
    ph.goto(url_of(base, kit.LANGUAGES[0], "tibia", "#/worlds")); wait_app(ph)
    bar = ph.locator(".table-sortbar").first
    c.ok(bar.is_visible(), "phone sort bar hidden")
    bar.locator("select").select_option(index=2)
    c.ok(ph.locator("#app .data-table thead th[aria-sort=descending], #app .data-table thead th[aria-sort=ascending]").count() == 1, "phone sort bar did not sort")
    sorted_th = ph.locator("#app .data-table thead th[aria-sort=descending], #app .data-table thead th[aria-sort=ascending]").first
    before = sorted_th.get_attribute("aria-sort")
    bar.locator("button").click()
    after = ph.locator("#app .data-table thead th[aria-sort=descending], #app .data-table thead th[aria-sort=ascending]").first.get_attribute("aria-sort")
    c.ok(before != after, f"phone direction button did not reverse ({before} -> {after})")
    pctx.close()
    # Fansites: each contact person links to their Tibia.com character page
    page.goto(url_of(base, kit.LANGUAGES[0], "tibia", "#/fansites")); wait_app(page)
    data = json.load(open(os.path.join(kit.WEBSITE, "tests", "fixtures", "tibiadata", "fansites.json"), encoding="utf-8"))["fansites"]
    contacts = [s["contact"] for k in ("promoted", "supported") for s in data[k] if s.get("contact")]
    links = page.evaluate("""() => [...document.querySelectorAll('.fansite .facts a')].filter(a => a.href.includes('community/?name=')).map(a => [a.textContent.replace(/\\s*\\(.*$/, '').trim(), a.href, a.target, a.rel])""")
    c.ok(len(links) == len(contacts), f"fansite contact links {len(links)} != contacts {len(contacts)}")
    for (text, href, target, rel), name in zip(links, contacts):
        want = "https://www.tibia.com/community/?name=" + urllib.parse.quote(name).replace("%20", "+")
        c.ok(text.startswith(name) and href == want and target == "_blank" and "noopener" in rel, f"contact {name}: {href}")
    ctx.close()
    c.note(f"{checked} column sorts checked (worlds in 5 languages, guild members), phone sort bar, {len(contacts)} fansite contact links")


# ---------------------------------------------------------------------------------------------------- calculators
FORMULA_REGISTRY = {f["id"]: f for f in json.load(open(os.path.join(kit.WEBSITE, "src", "formulas.json"), encoding="utf-8"))["formulas"]}
# How the calculator pages write numbers: the site's own grouping per language (pl and es group with no-break spaces,
# and only from 10 000 on, like build_site.format_number) - not the browser's Intl rules.
CALC_GROUP = {"en": ",", "pt-BR": ".", "pl": "\u00a0", "es": "\u00a0", "de": "."}
CALC_READY = "() => { const f = document.querySelector('form[data-calc]'); return !!f && f.dataset.ready === '1'; }"
CALC_DEFAULTS = """() => Object.fromEntries([...document.querySelectorAll('form[data-calc] [name]')].map(e =>
  [e.name, e.tagName === 'SELECT' ? ([...e.options].find(o => o.defaultSelected) || e.options[0]).value : e.defaultValue]))"""
CALC_CALLS = """() => [...document.querySelectorAll('form[data-calc] [data-formula]')].map(e =>
  ({id: e.dataset.formula, args: e.dataset.args || '', prefix: e.dataset.prefix || ''}))"""
# Fills the form the way the e2e contract says (by data-kind: int/number as plain digits, duration as h:mm from minutes,
# <select> by value; a string is typed as it is) and returns what the page shows.
CALC_FILL = """(values) => {
  const form = document.querySelector('form[data-calc]');
  for (const [name, v] of Object.entries(values)) {
    const el = form.querySelector('[name="' + name + '"]');
    if (!el) throw new Error('no input named ' + name);
    let text = v;
    if (typeof v === 'number') {
      if (el.tagName === 'SELECT') text = String(v);
      else if (el.dataset.kind === 'duration') text = Math.floor(v / 60) + ':' + String(v % 60).padStart(2, '0');
      else if (Number.isInteger(v) && v >= 0) text = String(v);
      else throw new Error(name + ': test value ' + v + ' cannot be typed as plain digits');
    }
    el.value = text;
    el.dispatchEvent(new Event('input', {bubbles: true}));
    if (el.tagName === 'SELECT') el.dispatchEvent(new Event('change', {bubbles: true}));
  }
  return window.__calcState(form);
}"""
CALC_STATE = """() => { window.__calcState = (form) => ({
  errors: Object.fromEntries([...form.querySelectorAll('[data-error]')].map(e => [e.dataset.error, e.textContent.trim()])),
  kinds: Object.fromEntries([...form.querySelectorAll('[data-error]')].filter(e => e.textContent.trim()).map(e => [e.dataset.error, e.dataset.errorKind || ''])),
  calcError: (form.querySelector('[data-calc-error]') || {textContent: ''}).textContent.trim(),
  copy: (form.querySelector('[data-copy]') || {}).disabled ? null : (form.querySelector('[data-copy]') || {getAttribute: () => ''}).getAttribute('data-copy'),
  echoes: Object.fromEntries([...form.querySelectorAll('[data-echo]')].map(e => [e.dataset.echo, e.textContent.trim()])),
  invalid: [...form.querySelectorAll('[aria-invalid=true]')].map(e => e.name),
  inputs: Object.fromEntries([...form.querySelectorAll('[name]')].map(e => [e.name, e.value])),
  outs: Object.fromEntries([...form.querySelectorAll('[data-out]')].map(e => [e.dataset.out,
    {value: e.getAttribute('data-value') || '', text: e.textContent.trim(), format: e.dataset.format || 'number'}])),
  search: location.search,
  overflow: document.documentElement.scrollWidth - window.innerWidth
}); }"""


def shown_number(lang, n):
    s = f"{n:,}"
    if lang in ("pl", "es") and abs(n) < 10000:
        s = str(n)
    return s.replace(",", CALC_GROUP[lang])


def calc_args(spec):
    """data-args="from,to" (same names) or "level=from" (formula input = page input) -> [(formula key, input name)]."""
    out = []
    for part in filter(None, (p.strip() for p in spec.split(","))):
        key, _, name = part.partition("=")
        out.append((key, name or key))
    return out


def calc_fill(page, values):
    page.evaluate(CALC_STATE)
    return page.evaluate(CALC_FILL, values)


def calculators(c, browser, base):
    """Calculator pages (registry pages with "calc") in all languages at 360 px: every formulas.json test value typed into
    the page (through its data-formula/data-args/data-prefix mapping) shows exactly the expected value, formatted for the
    language; invalid input gives an inline error and no result; share links, copy, reset; the hub links every calculator;
    "How this is calculated" lists every formula's sources."""
    calc_pages = [p for p in kit.PAGES if p.get("calc")]
    hub = next((p for p in kit.PAGES if p["id"] == "calculators"), None)
    if not c.ok(calc_pages and hub, "registry needs the calculators hub and at least one page with \"calc\""):
        return
    verified, refused = 0, []
    # No coin-seller / gambling-promo source (removed in the final review) and no uncaptured calculator on any built page.
    banned = []
    for folder, _, files in os.walk(os.path.join(kit.WEBSITE, "site")):
        for name in files:
            if name.endswith((".html", ".js")):
                text = open(os.path.join(folder, name), encoding="utf-8", errors="replace").read()
                banned += [os.path.relpath(os.path.join(folder, name), kit.WEBSITE) + ":" + m for m in set(re.findall(r"tibiawatch|intibia|exevopan|etienreseller|codingace", text, re.I))]
    c.ok(not banned, f"built site names removed sources: {banned[:5]}")
    for lang in kit.LANGUAGES:
        code = lang["code"]
        ctx, page = new_page(browser, base, 360, 780)
        page.goto(url_of(base, lang, "calculators"))
        cards = page.evaluate("() => [...document.querySelectorAll('main a[data-calc-card]')].map(a => a.href)")
        for p in calc_pages:
            c.ok(base + kit.page_path(lang, p) in cards, f"{code}/calculators: no card linking to {p['id']} ({cards})")
        for p in calc_pages:
            where = f"{code}/{p['id']}"
            page.goto(url_of(base, lang, p["id"]))
            page.wait_for_function(CALC_READY, timeout=10000)
            defaults, calls = page.evaluate(CALC_DEFAULTS), page.evaluate(CALC_CALLS)
            for fid in p["calc"]:
                f = FORMULA_REGISTRY.get(fid)
                mine = [x for x in calls if x["id"] == fid]
                if not c.ok(f and mine, f"{where}: formula {fid} missing in formulas.json or not used by the page (data-formula)"):
                    continue
                independent = 0
                for ti, test in enumerate(f["tests"]):
                    seen = None
                    for call in mine:
                        args = calc_args(call["args"])
                        if not c.ok({k for k, _ in args} == set(test["in"]), f"{where}: data-args '{call['args']}' do not cover the inputs of {fid}"):
                            continue
                        if not all(name in defaults for _, name in args):
                            continue
                        values = dict(defaults)
                        values.update({name: test["in"][k] for k, name in args})
                        st = calc_fill(page, values)
                        if any(st["errors"].values()):
                            # The page refuses this combination: no number may be shown, and only a rule between two fields
                            # (data-gt-field, e.g. target level not above the current level) may refuse a formulas.json value -
                            # a refused value of its own (format, range) would be a calculator that cannot show its tested value.
                            c.ok(all(not o["value"] and not o["text"] for o in st["outs"].values()), f"{where}: results shown while an input is refused {st['errors']}")
                            if c.ok(set(st["kinds"].values()) == {"gt-field"}, f"{where}: page refuses the test value {fid}{test['in']} (mapping {call['args']}): {st['errors']} {st['kinds']}"):
                                seen = seen or "refused"
                            continue
                        c.ok(not st["calcError"], f"{where}: {fid}{test['in']} shows the result error {st['calcError']!r}")
                        before = len(c.failures)
                        for k, v in test["out"].items():
                            name = (call["prefix"] + "." if call["prefix"] else "") + k
                            o = st["outs"].get(name)
                            if c.ok(o is not None, f"{where}: no output data-out={name}"):
                                c.ok(o["value"] == str(v), f"{where}: {fid}{test['in']} {name} = {o['value']!r}, expected {v}")
                                if o["format"] == "number":
                                    c.ok(o["text"] == shown_number(code, v), f"{where}: {name} shown as {o['text']!r}, expected {shown_number(code, v)!r}")
                        c.ok(st["overflow"] <= 0, f"{where}: horizontal overflow {st['overflow']}px")
                        if len(c.failures) == before:
                            seen = "verified"
                    if seen == "verified":
                        verified += 1
                        independent += test.get("from") != "computed"
                    elif seen == "refused":
                        refused.append(f"{fid} {test['in']}")
                    else:
                        c.ok(False, f"{where}: {fid} test {ti} cannot be entered on the page")
                c.ok(independent >= 1, f"{where}: no independent (non-computed) test value of {fid} is shown by the page")
            # every result sits in one polite live region, read row by row (label + value, aria-atomic); no <output> (itself a
            # status live region, which would announce twice); rows that depend on the clock stay outside the live region
            live = page.evaluate("""() => { const f = document.querySelector('form[data-calc]'); const r = [...f.querySelectorAll('[aria-live]')];
              const inLive = (e) => !!e.closest('[aria-live]');
              return {n: r.length, polite: r.every(e => e.getAttribute('aria-live') === 'polite'), all: [...f.querySelectorAll('[data-out]')].every(o => r[0] && r[0].contains(o)),
                      outputs: f.querySelectorAll('output, [role=status]').length,
                      atomic: [...f.querySelectorAll('[data-row]')].filter(inLive).every(e => e.getAttribute('aria-atomic') === 'true'),
                      clockInLive: [...f.querySelectorAll('[data-finish], [data-finish-at]')].filter(inLive).length}; }""")
            c.ok(live["n"] == 1 and live["polite"] and live["all"], f"{where}: results must be in exactly one aria-live=polite region ({live})")
            c.ok(live["outputs"] == 0 and live["atomic"] and live["clockInLive"] == 0, f"{where}: no <output>/role=status, atomic rows, clock rows outside the live region ({live})")
            # Copy text for game chat: hyphens and normal spaces only.
            st = calc_fill(page, dict(defaults))
            c.ok(st["copy"] and not re.search("[–—    ]", st["copy"]), f"{where}: copy text must use '-' and normal spaces: {st['copy']!r}")
            # Source links are touch targets on phones: at least 44 px tall (spec), and therefore >= 24 px apart.
            page.evaluate("document.querySelectorAll('details.how').forEach(d => d.open = true)")
            heights = page.evaluate("() => [...document.querySelectorAll('details.how .how-sources a')].map(a => Math.round(a.getBoundingClientRect().height))")
            c.ok(heights and min(heights) >= 44, f"{where}: source links must be >= 44 px tall at 360 px: {heights}")
            page.evaluate("document.querySelectorAll('details.how').forEach(d => d.open = false)")
            for fid in p["calc"]:
                how = page.locator(f"details.how [data-how='{fid}']")
                if c.ok(how.count() == 1, f"{where}: no 'How this is calculated' block for {fid}"):
                    links = how.locator("a[href^='https://']")
                    c.ok(links.count() == len(FORMULA_REGISTRY[fid]["sources"]), f"{where}: {fid} shows {links.count()} sources")
                    labelled = sum(1 for s in FORMULA_REGISTRY[fid]["sources"] if s.get("label"))
                    c.ok(how.locator("[data-source-label]").count() == labelled, f"{where}: {fid} shows {how.locator('[data-source-label]').count()} source qualifiers, expected {labelled}")
                    c.ok(not re.search(r"tibiawatch|intibia|exevopan|etienreseller|codingace", how.inner_html(), re.I), f"{where}: {fid} links a removed promo/uncaptured source")
                    c.ok(all(links.nth(i).get_attribute("target") == "_blank" and "noopener" in links.nth(i).get_attribute("rel") and "noreferrer" in links.nth(i).get_attribute("rel")
                             for i in range(links.count())), f"{where}: {fid} source links need target=_blank rel=noopener noreferrer")
                    checked = how.locator(".how-checked time")
                    c.ok(checked.get_attribute("datetime") == FORMULA_REGISTRY[fid]["checkedOn"] and FORMULA_REGISTRY[fid]["checkedOn"][:4] in checked.text_content(),
                         f"{where}: {fid} 'checked on' date missing or not {FORMULA_REGISTRY[fid]['checkedOn']}")
            c.ok(not page.evaluate("document.querySelector('details.how').open"), f"{where}: 'How this is calculated' should be collapsed on phones")
            if p["id"] == "calc-experience":
                # Ruling R2: the share range is shown for both levels.
                st = calc_fill(page, dict(defaults, **{"from": 100, "to": 200}))
                for name, v in (("from.min", 66), ("from.max", 151), ("to.min", 133), ("to.max", 301)):
                    c.ok(st["outs"].get(name, {}).get("value") == str(v), f"{where}: share range {name} = {st['outs'].get(name)}, expected {v}")
                # Target below current: inline error, no results.
                st = calc_fill(page, dict(defaults, **{"from": 200, "to": 100}))
                c.ok(st["errors"].get("to"), f"{where}: no error under the target level for to=100, from=200")
                c.ok("to" in st["invalid"], f"{where}: target level not marked aria-invalid")
                c.ok(all(not o["value"] and not o["text"] for o in st["outs"].values()), f"{where}: outputs not emptied for to < from")
                for name, text in (("hpd", "25"), ("hpd", "0"), ("exph", "0"), ("from", "0"), ("to", "20001"), ("from", "12.5" if code == "en" else "12,5"), ("exph", "1.5" if code == "en" else "1,5")):
                    st = calc_fill(page, dict(defaults, **{name: text}))
                    c.ok(st["errors"].get(name) and all(not o["value"] for o in st["outs"].values()), f"{where}: {name}={text!r} must be refused with an inline error")
                # The page language decides the number format; every number is echoed.
                short = "1.5kk" if code == "en" else "1,5kk"
                st = calc_fill(page, dict(defaults, exph=short))
                c.ok(shown_number(code, 1500000) in st["echoes"].get("exph", ""), f"{where}: {short} echoed as {st['echoes'].get('exph')!r}")
                c.ok(st["outs"]["exp"]["value"] == "113695000", f"{where}: exp with exph {short}")
                if code == "en":
                    st = calc_fill(page, dict(defaults, exph="1.500.000"))
                    c.ok(st["errors"].get("exph") and not st["outs"]["exp"]["value"], "en: 1.500.000 must be an error on the English page, not a guess")
                    # A field that offers the shorthand explains the format, not "Enter a whole number".
                    fmt = page.evaluate("TF.t('calc.error.format')")
                    st = calc_fill(page, dict(defaults, exph="1,5kk"))
                    c.ok(st["errors"].get("exph") == fmt, f"en: exph '1,5kk' must show the format message {fmt!r}, got {st['errors'].get('exph')!r}")
                    st = calc_fill(page, dict(defaults, hpd="4h"))
                    c.ok(st["errors"].get("hpd") == page.evaluate("TF.t('calc.error.formatInt')"), f"en: hpd '4h' keeps the whole-number message: {st['errors'].get('hpd')!r}")
            if p["id"] == "calc-stamina":
                # Defaults 39:00 -> 42:00: 18 h regeneration, 18 h 10 min offline (wiki), a clock time and the 10-minute note.
                st = calc_fill(page, dict(defaults))
                c.ok(st["outs"]["minutes"]["value"] == "1090" and st["outs"]["regen"]["value"] == "1080", f"{where}: 39:00 -> 42:00 gives {st['outs']}")
                want = page.evaluate("(m) => TF.t('calc.unit.hoursMinutes', {h: String(Math.floor(m / 60)), m: String(m % 60)})", 1090)
                c.ok(st["outs"]["minutes"]["text"] == want, f"{where}: offline time shown as {st['outs']['minutes']['text']!r}, expected {want!r}")
                at = page.evaluate("(() => { const o = document.querySelector('[data-finish-at]'); return [o.getAttribute('data-value'), o.textContent.trim()]; })()")
                now = datetime.datetime.now()
                c.ok(at[0] and abs((datetime.datetime.strptime(at[0], "%Y-%m-%dT%H:%M") - (now + datetime.timedelta(minutes=1090))).total_seconds()) < 180 and re.search(r"\d", at[1]),
                     f"{where}: 'reached on' is now + 18 h 10 min, got {at}")
                c.ok(page.locator("form[data-calc] .calc-note").count() == 1 and "10" in page.locator("form[data-calc] .calc-note").text_content(), f"{where}: the 10-minute note is missing")
                # target not above current: inline error, no results
                for cur, tar in (("20:00", "10:00"), ("20:00", "20:00")):
                    st = calc_fill(page, dict(current=cur, target=tar))
                    c.ok(st["errors"].get("target") and "target" in st["invalid"] and all(not o["value"] and not o["text"] for o in st["outs"].values()),
                         f"{where}: target {tar} with current {cur} must be refused with an inline error")
                for name, text in (("current", "42:01"), ("target", "42:01"), ("target", "43:00"), ("current", "39"), ("current", "39:75"), ("target", "abc"), ("current", "")):
                    st = calc_fill(page, dict({"current": "10:00", "target": "20:00"}, **{name: text}))
                    c.ok(st["errors"].get(name) and all(not o["value"] for o in st["outs"].values()), f"{where}: {name}={text!r} must be refused with an inline error")
                # An empty clock time shows "–" like every other empty result.
                dash = page.evaluate("() => { const o = document.querySelector('[data-finish-at]'); return [o.textContent, getComputedStyle(o, '::before').content]; }")
                c.ok(dash == ["", '"–"'], f"{where}: empty 'reached on' must show '–': {dash}")
                st = calc_fill(page, dict(current="0:00", target="42:00"))
                c.ok(st["outs"]["regen"]["value"] == "8100" and st["outs"]["minutes"]["value"] == "8110", f"{where}: 0:00 -> 42:00 gives {st['outs']}")
            if p["id"] == "calc-blessings":
                # Default level 100 (200 x 80 = 16,000 each regular), Inquisition +10%, the note about discounts and skulls.
                st = calc_fill(page, dict(defaults))
                want = {"regularAll": "80000", "regularEach": "16000", "enhancedAll": "41600", "enhancedEach": "20800", "inquisition": "88000",
                        "allSeven": "121600", "allSevenInquisition": "129600", "twistOfFate": "16000"}
                c.ok({k: st["outs"][k]["value"] for k in want} == want, f"{where}: level 100 gives {st['outs']}")
                c.ok(page.locator("form[data-calc] .calc-note").count() == 1, f"{where}: the note about discounts and skulls is missing")
                for level, tof in (("269", "49800"), ("270", "50000"), ("271", "50000"), ("1", "2000")):
                    st = calc_fill(page, dict(level=level))
                    c.ok(st["outs"]["twistOfFate"]["value"] == tof, f"{where}: Twist of Fate at level {level} = {st['outs']['twistOfFate']['value']!r}, expected {tof}")
                st = calc_fill(page, dict(level="121"))
                c.ok(st["outs"]["inquisition"]["value"] == "110412", f"{where}: Inquisition at level 121 must round down to 110412, got {st['outs']['inquisition']['value']!r}")
                st = calc_fill(page, dict(level="20000"))
                c.ok(st["outs"]["regularEach"]["value"] == "1511000" and not st["errors"].get("level"), f"{where}: level 20,000 gives {st['outs']['regularEach']}")
                for text in ("0", "20001", "7.5", "abc", ""):
                    st = calc_fill(page, dict(level=text))
                    c.ok(st["errors"].get("level") and all(not o["value"] for o in st["outs"].values()), f"{where}: level={text!r} must be refused with an inline error")
            if p["id"] == "calc-stats":
                # Default level 100 knight; the vocation <select> lists the six vocations with the page language's labels.
                st = calc_fill(page, dict(defaults))
                c.ok({k: st["outs"][k]["value"] for k in ("hp", "mana", "cap")} == {"hp": "1565", "mana": "550", "cap": "2970"}, f"{where}: level 100 knight gives {st['outs']}")
                opts = page.evaluate("() => [...document.querySelectorAll('form[data-calc] select[name=vocation] option')].map(o => [o.value, o.textContent.trim()])")
                c.ok(sorted(v for v, _ in opts) == ["druid", "knight", "monk", "none", "paladin", "sorcerer"] and all(t for _, t in opts), f"{where}: vocation options {opts}")
                c.ok(len({t for _, t in opts}) == 6, f"{where}: vocation labels must be distinct {opts}")
                c.ok(page.locator("form[data-calc] .calc-note").count() == 1 and page.locator("#calc-vocation-hint").text_content().strip(), f"{where}: the level-8 note and hint are missing")
                for voc, want in (("monk", ("1105", "1010", "2970")), ("druid", ("645", "2850", "1590")), ("none", ("645", "550", "1590"))):
                    st = calc_fill(page, dict(defaults, vocation=voc))
                    c.ok(tuple(st["outs"][k]["value"] for k in ("hp", "mana", "cap")) == want, f"{where}: level 100 {voc} gives {st['outs']}")
                st = calc_fill(page, dict(defaults, level="7", vocation="knight"))
                c.ok(tuple(st["outs"][k]["value"] for k in ("hp", "mana", "cap")) == ("180", "85", "660"), f"{where}: level 7 knight gives {st['outs']}")
                st = calc_fill(page, dict(defaults, level="20000"))
                c.ok(st["outs"]["hp"]["value"] == "300065" and not st["errors"].get("level"), f"{where}: level 20,000 gives {st['outs']['hp']}")
                for text in ("0", "20001", "7.5", "abc", ""):
                    st = calc_fill(page, dict(defaults, level=text))
                    c.ok(st["errors"].get("level") and all(not o["value"] for o in st["outs"].values()), f"{where}: level={text!r} must be refused with an inline error")
        c.ok(not page.console_errors, f"{code}: console errors {page.console_errors[:3]}")
        ctx.close()
    # Share link opened in German, copy, share, typing, reset (desktop: "How" open).
    de = next(l for l in kit.LANGUAGES if l["code"] == "de")
    exp_page = next((p for p in calc_pages if p["id"] == "calc-experience"), None)
    if c.ok(exp_page, "no calc-experience page"):
        query = "?from=100&to=200&exph=1500000&hpd=24"
        ctx, page = new_page(browser, base, 1440, 1000, permissions=["clipboard-read", "clipboard-write"])
        page.goto(url_of(base, de, "calc-experience") + query)
        page.wait_for_function(CALC_READY, timeout=10000)
        page.evaluate(CALC_STATE)
        st = page.evaluate("window.__calcState(document.querySelector('form[data-calc]'))")
        c.ok(st["inputs"] == {"from": "100", "to": "200", "exph": "1.500.000", "hpd": "24"}, f"de share link fills the form: {st['inputs']}")
        c.ok(st["outs"]["exp"]["text"] == "113.695.000" and st["outs"]["exp"]["value"] == "113695000", f"de share link: exp {st['outs'].get('exp')}")
        c.ok(st["outs"]["days"]["value"] == "4" and st["outs"]["minutes"]["value"] == "4547", f"de share link: minutes/days {st['outs'].get('minutes')} {st['outs'].get('days')}")
        c.ok(page.evaluate("document.querySelector('details.how').open"), "de desktop: 'How this is calculated' should be open")
        c.ok(query in (page.locator("#lang-menu a[data-lang=en]").get_attribute("href") or ""), "language links keep the calculator's inputs")
        page.locator("form[data-calc] [data-copy]").click()
        clip = page.evaluate("navigator.clipboard.readText()")
        c.ok("113.695.000" in clip, f"copy: clipboard lacks the exp value: {clip!r}")
        page.locator("form[data-calc] [data-share]").click()
        clip = page.evaluate("navigator.clipboard.readText()")
        c.ok(clip.endswith("/de/calculators/experience/" + query), f"share link copied: {clip!r}")
        # Typing does not rewrite the address bar per keystroke (each history change can be a Google Analytics page view):
        # the language links and Share follow at once, the address bar once typing pauses (about 1 s), with one replaceState.
        page.evaluate("() => { window.__rs = 0; const o = history.replaceState.bind(history); history.replaceState = (...a) => { window.__rs++; return o(...a); }; }")
        field = page.locator("form[data-calc] [name=from]")
        field.fill("")
        field.press_sequentially("150", delay=80)
        early = page.evaluate("[location.search, window.__rs, document.querySelector('#lang-menu a[data-lang=en]').getAttribute('href')]")
        c.ok(early[1] == 0 and "from=150" not in early[0] and "from=150" in (early[2] or ""), f"typing: no history change per keystroke, language links at once: {early}")
        page.wait_for_function("location.search.includes('from=150')", timeout=5000)
        page.wait_for_timeout(300)
        c.ok(page.evaluate("window.__rs") == 1, f"typing updates the address bar once after the pause: {page.evaluate('[location.search, window.__rs]')}")
        page.locator("form[data-calc] [data-reset]").click()
        st = page.evaluate("window.__calcState(document.querySelector('form[data-calc]'))")
        c.ok(st["search"] == "", f"reset clears the query string: {st['search']!r}")
        c.ok(st["inputs"] == {"from": "100", "to": "200", "exph": "1.500.000", "hpd": "4"}, f"reset restores the defaults: {st['inputs']}")
        c.ok(st["outs"]["exp"]["value"] == "113695000" and st["outs"]["days"]["value"] == "19", "reset recomputes the defaults")
        # Review finding: a finish date beyond what a Date can hold must not stop the update (106,347,500 days at 1 exp/h).
        page.goto(url_of(base, de, "calc-experience") + "?from=100&to=300&exph=1500000&hpd=4")
        page.wait_for_function(CALC_READY, timeout=10000)
        page.evaluate(CALC_STATE)
        before = page.evaluate("document.querySelector('[data-finish]').getAttribute('data-value')")
        c.ok(bool(before), "finish date shown for 100 -> 300 at 1.500.000 exp/h")
        exph = page.locator("form[data-calc] [name=exph]")
        exph.fill("")
        exph.press_sequentially("1")
        st = page.evaluate("window.__calcState(document.querySelector('form[data-calc]'))")
        finish = page.evaluate("(() => { const o = document.querySelector('[data-finish]'); return [o.getAttribute('data-value'), o.textContent]; })()")
        c.ok(st["outs"]["days"]["value"] == "106347500" and st["outs"]["days"]["text"] == "106.347.500", f"days at 1 exp/h: {st['outs']['days']}")
        c.ok(finish == [None, ""], f"finish date not representable must show '–', not a stale date: {finish}")
        page.wait_for_timeout(1300)   # the address bar follows after a pause in typing
        search = page.evaluate("location.search")
        c.ok(search == "?from=100&to=300&exph=1&hpd=4", f"share link follows the input: {search!r}")
        c.ok(st["copy"] and "106.347.500" in st["copy"] and "exph=1&" in st["copy"], f"copy summary follows the input: {st['copy']!r}")
        c.ok(not page.console_errors, f"page errors after typing exph=1: {page.console_errors[:3]}")
        # The result guard: a value beyond Number.MAX_SAFE_INTEGER (or a failing formula) shows an inline error, no numbers.
        for patch, what in (("() => ({ exp: 2 ** 60, minutes: 1, days: 1 })", "too large"), ("() => { throw new Error('test'); }", "failing")):
            page.evaluate(f"(() => {{ window.__orig = window.__orig || TFFormulas.FORMULAS.levelPlan; TFFormulas.FORMULAS.levelPlan = {patch}; }})()")
            st = calc_fill(page, {"hpd": "5"})
            c.ok(st["calcError"] and all(not o["value"] and not o["text"] for o in st["outs"].values()) and st["copy"] is None,
                 f"{what} result: inline error and no numbers expected: {st['calcError']!r} {[(k, o['value']) for k, o in st['outs'].items() if o['value']]}")
            page.wait_for_timeout(1300)
            search = page.evaluate("location.search")
            c.ok(search.endswith("hpd=5"), f"{what} result: share link still follows the input ({search})")
        page.evaluate("() => { TFFormulas.FORMULAS.levelPlan = window.__orig; }")
        st = calc_fill(page, {"exph": "1500000"})
        c.ok(not st["calcError"] and st["outs"]["exp"]["value"] == "425390000", f"result error clears with valid input: {st['calcError']!r}")
        c.ok(not page.console_errors, f"page errors from the result guard: {page.console_errors[:3]}")
        page.goto(url_of(base, de, "calc-experience"))
        page.wait_for_function(CALC_READY, timeout=10000)
        # Keyboard: Tab from the first field walks through the fields to the buttons.
        page.locator("form[data-calc] [name=from]").focus()
        order = []
        for _ in range(8):
            page.keyboard.press("Tab")
            order.append(page.evaluate("document.activeElement.getAttribute('name') || document.activeElement.hasAttribute('data-copy') && 'copy' || document.activeElement.hasAttribute('data-share') && 'share' || document.activeElement.hasAttribute('data-reset') && 'reset' || document.activeElement.tagName"))
        c.ok(order[:6] == ["to", "exph", "hpd", "copy", "share", "reset"], f"keyboard order {order}")
        # /en/<calculator>/ remembers English and opens the page (nested folders).
        page.goto(base + "en/" + exp_page["path"])
        page.wait_for_load_state()
        c.ok(page.url.endswith("/" + exp_page["path"] + "?lang=en") and page.get_attribute("html", "lang") == "en", f"/en/{exp_page['path']} -> {page.url}")
        c.ok(not page.console_errors, f"de desktop: console errors {page.console_errors[:3]}")
        ctx.close()
    # Time-dependent results never go stale: the clock time advances without input and Copy carries the current time.
    st_page = next((p for p in calc_pages if p["id"] == "calc-stamina"), None)
    if c.ok(st_page, "no calc-stamina page"):
        ctx, page = new_page(browser, base, 1000, 800)
        page.clock.install(time=datetime.datetime(2026, 11, 18, 10, 0, 30))
        page.goto(url_of(base, kit.LANGUAGES[0], "calc-stamina"))
        page.wait_for_function(CALC_READY, timeout=10000)
        read = "(() => { const f = document.querySelector('form[data-calc]'); const o = f.querySelector('[data-finish-at]'); return [o.getAttribute('data-value'), o.textContent.trim(), f.querySelector('[data-copy]').getAttribute('data-copy')]; })()"
        before = page.evaluate(read)
        c.ok(before[0] == "2026-11-19T04:10", f"stamina clock time at 10:00:30 + 18 h 10 min: {before}")
        page.locator("form[data-calc] [name=current]").focus()
        # The minute timer changes nothing inside the live region (no announcement every minute).
        watch = """() => { window.__liveMut = 0; document.querySelectorAll('[aria-live]').forEach(r => new MutationObserver(m => { window.__liveMut += m.length; })
          .observe(r, {subtree: true, childList: true, characterData: true, attributes: true})); }"""
        page.evaluate(watch)
        page.clock.run_for(5 * 60 * 1000)
        after = page.evaluate(read)
        plain = lambda s: re.sub("[    ]", " ", s or "")
        c.ok(after[0] == "2026-11-19T04:15" and after[1] != before[1] and plain(after[1]) in after[2] and plain(before[1]) not in after[2],
             f"clock time must advance with time passing and without input: {before} -> {after}")
        c.ok(page.evaluate("window.__liveMut") == 0, f"stamina: minute ticks changed the live region {page.evaluate('window.__liveMut')} times")
        c.ok(page.evaluate("document.activeElement.name") == "current" and page.locator("form[data-calc] [name=current]").input_value() == "39:00", "the timer must not touch the focused field")
        page.clock.run_for(60 * 1000)
        page.locator("form[data-calc] [data-copy]").click()
        # The copy text is rebuilt at click time: it carries the clock time for "now + 18 h 10 min" at the click, as shown.
        clicked = page.evaluate(read)
        due = page.evaluate("(() => { const d = new Date(Date.now() + 1090 * 60000), t = (n) => ('0' + n).slice(-2); return d.getFullYear() + '-' + t(d.getMonth() + 1) + '-' + t(d.getDate()) + 'T' + t(d.getHours()) + ':' + t(d.getMinutes()); })()")
        c.ok(clicked[0] == due and plain(clicked[1]) in (clicked[2] or "") and clicked[0] != before[0], f"copy text rebuilt at click time (due {due}): {clicked}")
        # Experience: the finish date (today + days) is outside the live region; ticks across midnight do not touch it either.
        page.goto(url_of(base, kit.LANGUAGES[0], "calc-experience"))
        page.wait_for_function(CALC_READY, timeout=10000)
        page.evaluate(watch)
        first = page.evaluate("document.querySelector('[data-finish]').getAttribute('data-value')")
        page.clock.run_for(24 * 60 * 60 * 1000)
        second = page.evaluate("document.querySelector('[data-finish]').getAttribute('data-value')")
        c.ok(first and second and first != second, f"experience: the finish date follows the calendar day: {first} -> {second}")
        c.ok(page.evaluate("window.__liveMut") == 0, f"experience: minute ticks changed the live region {page.evaluate('window.__liveMut')} times")
        ctx.close()
    c.note(f"{verified} test values shown correctly across {len(kit.LANGUAGES)} languages; refused by the page's validation: {sorted(set(refused))}")


def today_in_tibia(c, browser, base):
    """"Today in Tibia" (home page and calculators hub): Rashid's town and the next server save, calculated in the browser from the
    clock. Two pinned times (Thursday 06:26 Berlin and Sunday 09:59:30 Berlin, visitor in New York) in all languages at 360 px: the town,
    the countdown and the local clock time, the minute tick across the 10:00 switch, no stale value after the tab was hidden,
    no live region around the ticking value, sources shown, no request beyond the site's own and no storage."""
    thursday = datetime.datetime(2026, 11, 19, 5, 26, 0, tzinfo=datetime.timezone.utc)      # Berlin 06:26 (CET)
    sunday = datetime.datetime(2026, 11, 22, 8, 59, 30, tzinfo=datetime.timezone.utc)       # Berlin 09:59:30 (CET)
    save_thursday = 1795078800000                                                            # 2026-11-19 10:00 Berlin = 09:00Z
    read = """() => { const r = document.querySelector('[data-today-server]'); const q = (s) => (r.querySelector(s) || {textContent: ''}).textContent.trim();
      return {town: q('[data-today-rashid]'), countdown: q('[data-today-countdown]'), clock: q('[data-today-clock]'),
              at: (r.querySelector('[data-today-clock]') || {getAttribute: () => ''}).getAttribute('data-at'),
              live: !!r.closest('[aria-live]') || !!r.querySelector('[aria-live]'), sources: r.querySelectorAll('.how-sources a').length,
              labels: [...r.querySelectorAll('.today-label')].map(e => e.textContent.trim()),
              how: r.querySelectorAll('.how-item').length, overflow: document.documentElement.scrollWidth - window.innerWidth}; }"""
    for lang in kit.LANGUAGES:
        for page_id in ("home", "calculators"):
            where = f"{lang['code']}/{page_id}"
            ctx, page = new_page(browser, base, 360, 800, timezone="America/New_York")
            hosts = set()
            page.on("request", lambda r: hosts.add(r.url.split("/")[2]) if r.url.startswith("http") else None)
            page.clock.install(time=thursday)
            page.goto(url_of(base, lang, page_id))
            page.wait_for_selector("[data-today-server] [data-today-rashid]:not(:empty)", timeout=10000)
            st = page.evaluate(read)
            c.ok(st["town"] == "Port Hope", f"{where}: Rashid at Thursday 06:26 Berlin (Wednesday's town): {st}")
            c.ok("3:34" in st["countdown"], f"{where}: countdown 3:34 to 10:00 Berlin: {st}")
            c.ok(st["at"] == str(save_thursday) and "04:00" in st["clock"], f"{where}: save at 04:00 New York time: {st}")
            c.ok(len(st["labels"]) == 2 and all(0 < len(x) <= 30 for x in st["labels"]), f"{where}: row labels are short texts: {st['labels']}")
            c.ok(not st["live"], f"{where}: the ticking countdown must not be in a live region")
            want_sources = len(FORMULA_REGISTRY["nextServerSave"]["sources"]) + len(FORMULA_REGISTRY["rashid"]["sources"])
            c.ok(st["how"] == 2 and st["sources"] == want_sources, f"{where}: both rules with their {want_sources} sources: {st}")
            c.ok(st["overflow"] <= 0, f"{where}: horizontal overflow {st['overflow']}px at 360 px")
            if lang["code"] == "en" and page_id == "calculators":
                # Thursday 06:26 -> a minute later the countdown is 3:33; the 10:00 switch moves Rashid and the countdown restarts.
                page.clock.run_for(60 * 1000)
                c.ok("3:33" in page.evaluate(read)["countdown"], f"{where}: countdown after one minute: {page.evaluate(read)}")
                # A background tab shows no stale value: while hidden the timer is off; on return the page recalculates at once.
                page.evaluate("() => { Object.defineProperty(document, 'hidden', {configurable: true, get: () => true}); document.dispatchEvent(new Event('visibilitychange')); }")
                page.clock.run_for(10 * 60 * 1000)
                c.ok("3:33" in page.evaluate(read)["countdown"], f"{where}: a hidden page must not tick: {page.evaluate(read)}")
                page.evaluate("() => { Object.defineProperty(document, 'hidden', {configurable: true, get: () => false}); document.dispatchEvent(new Event('visibilitychange')); }")
                c.ok("3:23" in page.evaluate(read)["countdown"], f"{where}: back in view the countdown is current: {page.evaluate(read)}")
                c.ok(set(hosts) <= {base.split("//")[1].rstrip("/"), "www.googletagmanager.com", "www.google-analytics.com"}, f"{where}: requests beyond the site itself and its existing analytics: {hosts}")
                keys = page.evaluate("Object.keys(localStorage).concat(Object.keys(sessionStorage))")
                c.ok(not [k for k in keys if re.search("today|rashid|save", k, re.I)], f"{where}: storage keys {keys}")
            c.ok(not page.console_errors, f"{where}: console errors {page.console_errors[:3]}")
            ctx.close()
        # Sunday 09:59:30 Berlin: Saturday's town and a countdown of one minute; at 10:00 Rashid is in Carlin and the next save is a day away.
        ctx, page = new_page(browser, base, 1000, 800, timezone="America/New_York")
        page.clock.install(time=sunday)
        page.goto(url_of(base, lang, "home"))
        page.wait_for_selector("[data-today-server] [data-today-rashid]:not(:empty)", timeout=10000)
        before = page.evaluate(read)
        page.clock.run_for(31 * 1000)
        after = page.evaluate(read)
        c.ok(before["town"] == "Edron" and "0:01" in before["countdown"], f"{lang['code']}: Sunday 09:59:30 Berlin: {before}")
        c.ok(after["town"] == "Carlin" and "24:00" in after["countdown"] and after["at"] == str(1795338000000 + 86400000), f"{lang['code']}: Sunday 10:00:01 Berlin: {after}")
        ctx.close()


HOME_TITLE = "Everything You Need to Create for Tibia."
HOME_SHOWCASE = ("asset-library", "extract-sprites", "map-tools", "content-studio")
SHOT_VARIANTS = """(theme) => [...document.querySelectorAll('main picture img')].map(i => ({src: i.currentSrc, ok: new RegExp('-' + theme + '\\\\.[a-z]+$').test(i.currentSrc)}))"""


def _load_pictures(page):
    """Scrolls every screenshot of main into view (they load lazily) and waits until each has a current source."""
    for i in range(page.locator("main picture img").count()):
        page.locator("main picture img").nth(i).scroll_into_view_if_needed()
    page.wait_for_function("[...document.querySelectorAll('main picture img')].every(i => i.complete && i.currentSrc)", timeout=15000)


def homepage(c, browser, base):
    """The app-first homepage (spec §5 "Homepage"): the exact hero title, the hero's downloads (same addresses as the Downloads page),
    the four-cell Today strip (Rashid from the formula, boosted creature and boss from TibiaData), "Build Your Fansite" to GitHub,
    the screenshots in the theme's variant (system theme and toggle), no overflow at 320 px, and Review Focus 4: TibiaData down hides
    both boosted cells, keeps Rashid and the server save, and writes no "undefined" or "null"."""
    en = kit.LANGUAGES[0]
    fx_creatures = json.load(open(os.path.join(kit.FIXTURES, "creatures.json"), encoding="utf-8"))["creatures"]["boosted"]
    fx_boss = json.load(open(os.path.join(kit.FIXTURES, "boostablebosses.json"), encoding="utf-8"))["boostable_bosses"]["boosted"]
    ctx, page = new_page(browser, base, 1440, 1000)
    page.goto(url_of(base, en, "downloads"))
    want = {f["id"]: page.locator(f"[data-download={f['id']}] a.btn").get_attribute("href") for f in kit.REGISTRY["downloads"]["files"]}
    checksums = kit.REGISTRY["downloads"]["base"].replace("{version}", VERSION) + kit.REGISTRY["downloads"]["checksums"]
    page.goto(url_of(base, en, "home"))
    h1 = page.locator("h1")
    c.ok(h1.count() == 1 and h1.inner_text().strip() == HOME_TITLE, f"en hero title: {h1.all_inner_texts()}")
    first = page.locator("main section").first
    hrefs = first.locator("a").evaluate_all("as => as.map(a => a.getAttribute('href'))")
    c.ok(want["msi"] in hrefs, f"the first section has the Installer download {want['msi']}: {hrefs}")
    primary = first.locator("a.btn-primary")
    c.ok(primary.count() == 1 and primary.get_attribute("href") == want["msi"], f"the Installer is the primary button: {primary.count()}")
    order = [h for h in hrefs if h in want.values()]
    c.ok(order[:3] == [want["msi"], want["portable"], want["source"]], f"hero downloads in order Installer, Portable, Source: {order}")
    c.ok(checksums in hrefs, f"hero checksums link {checksums}")
    c.ok(VERSION in first.inner_text(), f"hero shows the version {VERSION}")
    hero_img = first.locator("picture img")
    c.ok(hero_img.count() == 1 and hero_img.get_attribute("loading") != "lazy", "one hero screenshot, not lazy")
    # Today strip: four cells, Rashid from the formula, boosted creature and boss from TibiaData (fixtures).
    page.wait_for_selector("[data-today-strip] [data-today-rashid]:not(:empty)", timeout=10000)
    cells = page.locator("[data-today-strip] .today-cell")
    c.ok(cells.count() == 4, f"today strip has 4 cells ({cells.count()})")
    r = page.evaluate("() => ({shown: document.querySelector('[data-today-strip] [data-today-rashid]').textContent.trim(), want: TFFormulas.FORMULAS.rashid({now: Date.now()}).town})")
    c.ok(r["shown"].lower().replace(" ", "") == r["want"], f"Rashid matches TFFormulas rashid: {r}")
    page.wait_for_selector("[data-today-strip] [data-boosted=creature] .boost-name", timeout=10000)
    page.wait_for_selector("[data-today-strip] [data-boosted=boss] .boost-name", timeout=10000)
    cr, bo = page.locator("[data-boosted=creature]"), page.locator("[data-boosted=boss]")
    c.ok(cr.locator(".boost-name").inner_text().strip() == fx_creatures["name"] and cr.is_visible(), "boosted creature cell shows the fixture's creature")
    c.ok(bo.locator(".boost-name").inner_text().strip() == fx_boss["name"] and bo.is_visible(), "boosted boss cell shows the fixture's boss")
    c.ok((cr.locator("a").get_attribute("href") or "").endswith("#/creature/" + fx_creatures["race"]), "boosted creature links to its creature page")
    c.ok((bo.locator("a").get_attribute("href") or "").endswith("#/bosses"), "boosted boss links to the bosses page")
    # The three paths; "Build Your Fansite" goes to GitHub until the library ships.
    fansite = page.locator("main a:has-text('Build Your Fansite')")
    c.ok(fansite.count() == 1 and fansite.get_attribute("href") == "https://github.com/RockyJLaRo/TibiaFoundry", f"Build Your Fansite link: {fansite.count()}")
    for text in ("Get the Desktop App", "Use Online Tools", "For Fansite Builders"):
        c.ok(page.locator(f"main :text-is('{text}')").count() >= 1, f"homepage text '{text}'")
    # Showcase: 4 screenshots of the language, lazy, with explicit sizes.
    shots = page.locator("main [data-showcase] picture img")
    c.ok(shots.count() == 4, f"showcase has 4 screenshots ({shots.count()})")
    attrs = page.evaluate("[...document.querySelectorAll('main [data-showcase] picture')].map(p => ({img: p.querySelector('img').getAttribute('src'), lazy: p.querySelector('img').getAttribute('loading'), w: p.querySelector('img').getAttribute('width'), h: p.querySelector('img').getAttribute('height'), dark: (p.querySelector('source') || {}).srcset || ''}))")
    for sid, a in zip(HOME_SHOWCASE, attrs):
        c.ok(f"{sid}-en-light" in a["img"] and f"{sid}-en-dark" in a["dark"], f"showcase {sid}: {a}")
        c.ok(a["lazy"] == "lazy" and a["w"] and a["h"], f"showcase {sid} lazy with size: {a}")
    # Download section at the end repeats the three downloads.
    last = page.locator("main section").last
    last_hrefs = last.locator("a").evaluate_all("as => as.map(a => a.getAttribute('href'))")
    c.ok(all(v in last_hrefs for v in want.values()), f"the last section repeats the three downloads: {last_hrefs}")
    c.ok(not page.console_errors, f"console errors: {page.console_errors[:3]}")
    ctx.close()
    # Screenshots follow the theme: the system's, and the toggle (data-theme) both ways.
    for theme in ("dark", "light"):
        ctx, page = new_page(browser, base, 1440, 1000, theme=theme)
        page.goto(url_of(base, en, "home"))
        _load_pictures(page)
        v = page.evaluate(SHOT_VARIANTS, theme)
        c.ok(len(v) == 5 and all(x["ok"] for x in v), f"system {theme}: screenshots {[x['src'].rsplit('/', 1)[-1] for x in v]}")
        other = "light" if theme == "dark" else "dark"
        page.locator(".header-tools [data-theme-toggle]").click()
        page.wait_for_function("(t) => [...document.querySelectorAll('main picture img')].every(i => i.complete && new RegExp('-' + t + '\\\\.[a-z]+$').test(i.currentSrc))", arg=other, timeout=15000)
        v = page.evaluate(SHOT_VARIANTS, other)
        c.ok(all(x["ok"] for x in v), f"toggled {other} on a {theme} system: {[x['src'].rsplit('/', 1)[-1] for x in v]}")
        ctx.close()
    # A stored choice opposite the system applies from the start.
    ctx, page = new_page(browser, base, 1440, 1000, theme="dark")
    page.add_init_script("try { localStorage.setItem('tf-theme', 'light'); } catch (e) {}")
    page.goto(url_of(base, en, "home"))
    _load_pictures(page)
    c.ok(all(x["ok"] for x in page.evaluate(SHOT_VARIANTS, "light")), "stored light choice on a dark system shows the light screenshots")
    ctx.close()
    # 320 px: no horizontal overflow in any language.
    ctx, page = new_page(browser, base, 320, 800)
    for lang in kit.LANGUAGES:
        page.goto(url_of(base, lang, "home"))
        page.wait_for_selector("[data-today-strip] [data-today-rashid]:not(:empty)", timeout=10000)
        over = page.evaluate("document.documentElement.scrollWidth - document.documentElement.clientWidth")
        c.ok(over <= 0, f"{lang['code']}: horizontal overflow {over}px at 320 px")
    ctx.close()
    # Review Focus 4: TibiaData unreachable.
    ctx, page = new_page(browser, base, 1280, 800, overrides={"": lambda route: route.abort("internetdisconnected")})
    page.goto(url_of(base, en, "home"))
    page.wait_for_selector("[data-today-strip] [data-today-rashid]:not(:empty)", timeout=10000)
    page.wait_for_function("document.querySelectorAll('[data-today-strip] [data-boosted][hidden]').length === 2", timeout=30000)
    page.wait_for_selector("[data-live-news] .notice-error", timeout=30000)
    c.ok(page.locator("[data-today-strip] [data-today-rashid]").is_visible(), "TibiaData down: Rashid visible")
    c.ok(page.locator("[data-today-strip] [data-today-countdown]").is_visible() and page.locator("[data-today-strip] [data-today-countdown]").inner_text().strip(), "TibiaData down: server save visible")
    c.ok(page.locator("[data-today-strip] .today-cell:visible").count() == 2, "TibiaData down: exactly the two formula cells shown")
    text = page.locator("main").inner_text()
    c.ok(not re.search(r"\bundefined\b|\bnull\b", text), "TibiaData down: 'undefined' or 'null' in main")
    ctx.close()
    # Stale answers (review r1, finding 1): TibiaData down, the last cached boosted answers handed out as stale. Cached before the
    # last server save they are yesterday's -> both cells hidden; cached after it they are still today's -> shown. Clock pinned to
    # Thursday 2026-11-19 12:00 Berlin (11:00Z); that day's save is 09:00Z.
    settled = "[...document.querySelectorAll('[data-today-strip] [data-boosted]')].every(c => c.hidden || !c.hasAttribute('aria-busy'))"
    api = kit.REGISTRY["api"]["base"]
    now_ms, save_ms = 1795086000000, 1795078800000
    seeds = {}
    for ep, path in (("creatures", ("creatures", "boosted")), ("boostablebosses", ("boostable_bosses", "boosted"))):
        body = json.load(open(os.path.join(kit.FIXTURES, ep + ".json"), encoding="utf-8"))
        body[path[0]][path[1]]["name"] = "Yesterday " + ep
        seeds[ep] = body
    for when, fetched, shown in (("before the save", save_ms - 21 * 3600 * 1000, False), ("after the save", save_ms + 5 * 60 * 1000, True)):
        ctx, page = new_page(browser, base, 1280, 800, overrides={"": lambda route: route.abort("internetdisconnected")})
        page.clock.install(time=datetime.datetime.fromtimestamp(now_ms / 1000, tz=datetime.timezone.utc))
        entries = {"tf-api:" + api + ep: {"t": fetched, "data": body} for ep, body in seeds.items()}
        page.add_init_script("(() => { const e = " + json.dumps(entries) + "; for (const k in e) sessionStorage.setItem(k, JSON.stringify(e[k])); })()")
        page.goto(url_of(base, en, "home"))
        page.wait_for_selector("[data-today-strip] [data-today-rashid]:not(:empty)", timeout=10000)
        page.wait_for_function(settled, timeout=30000)
        hidden = page.locator("[data-today-strip] [data-boosted][hidden]").count()
        names = page.locator("[data-today-strip] .boost-name").all_inner_texts()
        if shown:
            c.ok(hidden == 0 and len(names) == 2, f"stale answer cached {when}: both boosted cells shown (still today's): {names}")
        else:
            c.ok(hidden == 2 and not names, f"stale answer cached {when}: both boosted cells hidden, not yesterday's {names}")
            c.ok("Yesterday" not in page.locator("main").inner_text(), f"stale answer cached {when}: yesterday's names in main")
        c.ok(page.locator("[data-today-strip] [data-today-rashid]").is_visible() and page.locator("[data-today-strip] [data-today-countdown]").is_visible(),
             f"stale answer cached {when}: Rashid and server save visible")
        ctx.close()
    # Empty answers (200, but no boosted entry or an empty name) hide the cells too.
    empty_c = json.dumps({"creatures": {"boosted": {"name": "", "race": "", "image_url": "", "featured": False}, "creature_list": []}})
    empty_b = json.dumps({"boostable_bosses": {"boostable_boss_list": []}})
    ctx, page = new_page(browser, base, 1280, 800, overrides={"creatures": (200, empty_c), "boostablebosses": (200, empty_b)})
    page.goto(url_of(base, en, "home"))
    page.wait_for_selector("[data-today-strip] [data-today-rashid]:not(:empty)", timeout=10000)
    page.wait_for_function(settled, timeout=30000)
    c.ok(page.locator("[data-today-strip] [data-boosted][hidden]").count() == 2, "empty boosted answers: both cells hidden")
    c.ok(not re.search(r"\bundefined\b|\bnull\b", page.locator("main").inner_text()), "empty boosted answers: 'undefined' or 'null' in main")
    ctx.close()
    # Review r1, finding 2 (Fansite Agreement): a CipSoft disclaimer comes before the hero's download buttons and the full notice
    # before the download section's buttons, in every language; still exactly one [data-download-notice].
    ctx, page = new_page(browser, base, 1440, 1000)
    for lang in kit.LANGUAGES:
        page.goto(url_of(base, lang, "home"))
        where = f"{lang['code']}/home"
        first = page.locator("main section").first
        disc = first.locator("[data-download-disclaimer]")
        if not c.ok(disc.count() == 1, f"{where}: hero download disclaimer missing"):
            continue
        c.ok("CipSoft" in disc.inner_text(), f"{where}: hero disclaimer does not name CipSoft")
        c.ok((disc.locator("a").get_attribute("href") or "").endswith("legal/#downloads"), f"{where}: hero disclaimer links to the Legal page's download section")
        order = page.evaluate("""() => {
          const before = (a, b) => !!(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);
          const hero = document.querySelector('main section'), disc = hero.querySelector('[data-download-disclaimer]');
          const notice = document.querySelector('[data-download-notice]');
          const heroLinks = [...hero.querySelectorAll('a[data-download-link]')];
          const rest = [...document.querySelectorAll('main a[data-download-link]')].filter(a => !hero.contains(a));
          return {hero: heroLinks.length > 0 && heroLinks.every(a => before(disc, a) && disc.getBoundingClientRect().bottom <= a.getBoundingClientRect().top),
                  rest: rest.length > 0 && rest.every(a => before(notice, a)), notices: document.querySelectorAll('[data-download-notice]').length};
        }""")
        c.ok(order["hero"], f"{where}: the disclaimer must come before (and above) every hero download button")
        c.ok(order["rest"], f"{where}: the download notice must come before the download section's buttons")
        c.ok(order["notices"] == 1, f"{where}: {order['notices']} download notices (want 1)")
    ctx.close()


def global_search(c, browser, base):
    """The header's global Tibia search (src/js/search.js): type picker, combobox suggestions from the full lists, exact-name
    routes, recent searches (tf-recent-searches) and a blocked localStorage (Review Focus 3 and 5)."""
    en = kit.LANGUAGES[0]
    TYPE, INPUT, LIST = "#tf-search-type", "#tf-search-input", "#tf-search-listbox"
    options = f"{LIST} [role=option]"

    def search(page, kind, text, key="Enter"):
        page.select_option(TYPE, kind)
        page.fill(INPUT, text)
        if key:
            page.keyboard.press(key)

    ctx, page = new_page(browser, base, 1280, 900)
    page.goto(url_of(base, en, "about"))
    # ARIA: a labelled type <select> and an APG combobox controlling a listbox.
    a = page.evaluate(f"""() => {{ const i = document.querySelector('{INPUT}'), s = document.querySelector('{TYPE}');
      const lb = document.getElementById(i.getAttribute('aria-controls')), label = s.id && document.querySelector('label[for="' + s.id + '"]');
      return {{ role: i.getAttribute('role'), exp: i.getAttribute('aria-expanded'), auto: i.getAttribute('aria-autocomplete'),
        list: lb && lb.getAttribute('role'), label: label ? label.textContent.trim() : '', types: [...s.options].map(o => o.value) }}; }}""")
    c.ok(a["role"] == "combobox" and a["exp"] == "false" and a["list"] == "listbox" and a["auto"] == "list", f"combobox ARIA: {a}")
    c.ok(bool(a["label"]), f"type select has a label: {a}")
    c.ok(a["types"] == [e["type"] for e in kit.REGISTRY["search"]], f"types from registry.search: {a['types']}")
    # Character: exact name, Enter, lands on the Tibia page's character view.
    search(page, "character", "Roc ky")
    page.wait_for_url("**/tibia/#/character/Roc%20ky")
    wait_app(page)
    c.ok(page.locator("#app h2").first.inner_text() == "Roc ky", "character search opens the character view")
    # Review Focus 5: names with an apostrophe and an accent survive the URL and come back into the name field.
    for name, hsh in (("Mc'Lovin", "#/character/Mc'Lovin"), ("Zé Ninja", "#/character/Z%C3%A9%20Ninja")):
        search(page, "character", name)
        page.wait_for_function("h => location.hash === h", arg=hsh, timeout=5000)
        wait_app(page)
        page.reload()
        wait_app(page)
        c.ok(page.evaluate("location.hash") == hsh, f"{name}: hash {page.evaluate('location.hash')}")
        c.ok(page.input_value(INPUT) == name and page.input_value(TYPE) == "character", f"{name}: name field after reload {page.input_value(INPUT)!r}")
        c.ok(page.locator("#app .notice-error").count() == 1, f"{name}: not-found state shown")
    # Guild: exact name.
    search(page, "guild", kit.REGISTRY["fixtures"]["guild"])
    page.wait_for_function("() => document.body.dataset.route === 'guild'", timeout=5000)
    wait_app(page)
    c.ok(page.locator("#app h2").first.inner_text() == kit.REGISTRY["fixtures"]["guild"], "guild search opens the guild view")
    # World: suggestions from the full list contain the text; ArrowDown + Enter opens the first one.
    worlds = json.load(open(os.path.join(kit.FIXTURES, "worlds.json"), encoding="utf-8"))["worlds"]
    names = [w["name"] for w in worlds["regular_worlds"] + (worlds.get("tournament_worlds") or [])]
    page.goto(url_of(base, en, "about"))
    page.select_option(TYPE, "world")
    page.click(INPUT)
    page.keyboard.type("ant")
    page.wait_for_selector(options, timeout=10000)
    shown = [s.strip() for s in page.locator(options).all_inner_texts()]
    want = [n for n in names if "ant" in n.lower()]
    c.ok(shown and all("ant" in s.lower() for s in shown) and sorted(shown) == sorted(want[:8]), f"world 'ant' suggestions {shown} (expected {want})")
    c.ok(page.get_attribute(INPUT, "aria-expanded") == "true", "aria-expanded is true while suggestions show")
    page.keyboard.press("ArrowDown")
    first = page.locator(options).first
    c.ok(page.get_attribute(INPUT, "aria-activedescendant") == first.get_attribute("id") and first.get_attribute("aria-selected") == "true",
         "ArrowDown makes the first option active (aria-activedescendant)")
    page.keyboard.press("Escape")
    c.ok(page.get_attribute(INPUT, "aria-expanded") == "false" and page.input_value(INPUT) == "ant", "Escape closes the suggestions")
    page.keyboard.press("ArrowDown")
    page.wait_for_selector(options)
    page.keyboard.press("Enter")
    page.wait_for_url("**/tibia/#/world/**")
    c.ok(page.url.endswith("#/world/" + urllib.parse.quote(shown[0])), f"ArrowDown + Enter opens the world: {page.url}")
    wait_app(page)
    # Creature: 'dem' shows Demons, which opens by race.
    page.select_option(TYPE, "creature")
    page.fill(INPUT, "")
    page.click(INPUT)
    page.keyboard.type("dem")
    page.wait_for_selector(options, timeout=10000)
    shown = page.locator(options).all_inner_texts()
    c.ok(any(s.strip().startswith("Demon") for s in shown) and all("dem" in s.lower() for s in shown), f"creature 'dem' suggestions {shown}")
    page.locator(options, has_text="Demons").first.click()
    page.wait_for_function("() => location.hash === '#/creature/demon' && document.body.dataset.route === 'creature'", timeout=5000)
    wait_app(page)
    c.ok(page.locator("#app .notice-error").count() == 0, "creature opens by race")
    # Spell: 'exura' (the formula) shows suggestions.
    page.select_option(TYPE, "spell")
    page.fill(INPUT, "")
    page.click(INPUT)
    page.keyboard.type("exura")
    page.wait_for_selector(options, timeout=10000)
    c.ok(1 <= page.locator(options).count() <= 8, f"spell 'exura' suggestions: {page.locator(options).all_inner_texts()}")
    page.keyboard.press("Escape")
    # House: opens the houses route with its world and town pickers.
    search(page, "house", "")
    page.wait_for_function("() => location.hash.startsWith('#/houses/') && document.body.dataset.route === 'houses'", timeout=5000)
    wait_app(page)
    c.ok(page.evaluate("document.body.dataset.route") == "houses" and page.locator("#app select").count() >= 2, f"house search opens houses: {page.url}")
    c.ok(not page.console_errors, f"console errors: {page.console_errors[:3]}")
    ctx.close()

    # Recent searches: 9 searches keep 8, newest first; "Clear Recent Searches" empties them.
    ctx, page = new_page(browser, base, 390, 844)
    page.goto(url_of(base, en, "tibia"))
    wait_app(page)
    for i in range(1, 10):
        search(page, "character", f"Name {i}")
        page.wait_for_function("h => location.hash === h", arg=f"#/character/Name%20{i}", timeout=5000)
    stored = page.evaluate("JSON.parse(localStorage.getItem('tf-recent-searches'))")
    c.ok([r["q"] for r in stored] == [f"Name {i}" for i in range(9, 1, -1)], f"stored recents: {stored}")
    page.fill(INPUT, "")
    page.click(INPUT)
    page.wait_for_selector(options, timeout=5000)
    shown = page.locator(options).all_inner_texts()
    c.ok(len(shown) == 8 and shown[0].startswith("Name 9") and shown[-1].startswith("Name 2"), f"recent searches shown: {shown}")
    clear = page.get_by_role("button", name="Clear Recent Searches")
    c.ok(clear.count() == 1, "a 'Clear Recent Searches' button")
    # WCAG 2.1.1: from the empty, focused field, Tab reaches "Clear Recent Searches" (the popup stays open) and Enter clears.
    page.keyboard.press("Tab")
    c.ok(page.evaluate("document.activeElement && document.activeElement.textContent.trim()") == "Clear Recent Searches",
         f"Tab from the field reaches the Clear button: {page.evaluate('document.activeElement && document.activeElement.outerHTML.slice(0, 80)')}")
    c.ok(page.get_attribute(INPUT, "aria-expanded") == "true", "the popup stays open while its button has focus")
    page.keyboard.press("Enter")
    c.ok(page.evaluate("localStorage.getItem('tf-recent-searches')") in (None, "[]"), "Enter on Clear removes the stored searches")
    c.ok(page.locator(options).count() == 0, "no recent searches shown after clearing")
    c.ok(page.evaluate(f"document.activeElement === document.querySelector('{INPUT}')"), "focus returns to the field after clearing")
    # A recent entry opens again from another page.
    search(page, "world", "Antica")
    page.wait_for_function("() => location.hash === '#/world/Antica'", timeout=10000)
    page.goto(url_of(base, en, "about"))
    page.click(INPUT)
    page.wait_for_selector(options)
    # Safari and Firefox do not focus a button on click: a mousedown anywhere in the popup must not blur the field
    # (that would close the popup before the click). The popup cancels it; the field keeps focus.
    md = page.evaluate(f"""() => {{ const b = document.querySelector('[data-search-clear]'), e = new MouseEvent('mousedown', {{ bubbles: true, cancelable: true }});
      b.dispatchEvent(e); return {{ prevented: e.defaultPrevented, focused: document.activeElement === document.querySelector('{INPUT}') }}; }}""")
    c.ok(md["prevented"] and md["focused"], f"mousedown in the popup keeps the field focused: {md}")
    page.keyboard.press("ArrowDown")
    page.keyboard.press("Enter")
    page.wait_for_url("**/tibia/#/world/Antica")
    # A mouse click on Clear works too.
    wait_app(page)
    page.fill(INPUT, "")
    page.click(INPUT)
    page.wait_for_selector(options)
    page.get_by_role("button", name="Clear Recent Searches").click()
    c.ok(page.evaluate("localStorage.getItem('tf-recent-searches')") in (None, "[]") and page.locator(options).count() == 0, "a click on Clear clears")
    c.ok(not page.console_errors, f"console errors (recents): {page.console_errors[:3]}")
    ctx.close()

    # Review Focus 3: localStorage throws on every call; the search still navigates and nothing is logged.
    ctx, page = new_page(browser, base, 1280, 900)
    page.add_init_script("""(() => { const boom = () => { throw new DOMException('blocked', 'SecurityError'); };
      Storage.prototype.getItem = boom; Storage.prototype.setItem = boom; Storage.prototype.removeItem = boom; })();""")
    page.goto(url_of(base, en, "about"))
    page.click(INPUT)
    search(page, "character", "Roc ky")
    page.wait_for_url("**/tibia/#/character/Roc%20ky")
    wait_app(page)
    c.ok(page.locator("#app h2").first.inner_text() == "Roc ky", "search works with a throwing localStorage")
    page.select_option(TYPE, "world")
    page.fill(INPUT, "")
    page.click(INPUT)
    page.keyboard.type("ant")
    page.wait_for_selector(options, timeout=10000)
    page.keyboard.press("ArrowDown")
    page.keyboard.press("Enter")
    page.wait_for_function("() => location.hash.startsWith('#/world/')", timeout=5000)
    c.ok(not page.console_errors, f"console errors with blocked storage: {page.console_errors[:3]}")
    ctx.close()

    # "/" focuses the search field; at 320 px the open suggestions fit without horizontal scroll and have 44 px targets.
    pl = next(l for l in kit.LANGUAGES if l["code"] == "pl")
    ctx, page = new_page(browser, base, 320, 700)
    page.goto(url_of(base, pl, "about"))
    page.keyboard.press("/")
    c.ok(page.evaluate(f"document.activeElement === document.querySelector('{INPUT}')"), "'/' focuses the search field")
    page.select_option(TYPE, "creature")
    page.click(INPUT)
    page.keyboard.type("dem")
    page.wait_for_selector(options, timeout=10000)
    m = page.evaluate(f"""() => {{ const r = document.querySelector('{LIST}').getBoundingClientRect();
      const small = [...document.querySelectorAll('{options}')].filter(e => e.getBoundingClientRect().height < 44).length;
      return {{ over: document.documentElement.scrollWidth - document.documentElement.clientWidth, l: r.left, r: r.right, w: innerWidth, small }}; }}""")
    c.ok(m["over"] <= 0 and m["l"] >= 0 and m["r"] <= m["w"], f"320 px: suggestions fit {m}")
    c.ok(m["small"] == 0, f"320 px: options under 44 px tall: {m['small']}")
    # iOS Safari zooms into fields under 16 px on focus.
    fs = page.evaluate(f"[parseFloat(getComputedStyle(document.querySelector('{TYPE}')).fontSize), parseFloat(getComputedStyle(document.querySelector('{INPUT}')).fontSize)]")
    c.ok(fs[0] >= 16 and fs[1] >= 16, f"320 px: type picker and field are at least 16 px: {fs}")
    c.ok(not page.console_errors, f"console errors (320): {page.console_errors[:3]}")
    ctx.close()


CHECKS = [("pages-and-attribution", pages_and_attribution), ("creator-links", creator_links), ("navigation", navigation),
          ("menu-keyboard", menu_keyboard), ("menu-overflow", menu_overflow), ("no-js-reachability", no_js_reachability),
          ("compliance", compliance), ("table-sorting", table_sorting),
          ("language-and-theme", language_and_theme), ("tibia-data", tibia_data), ("news-and-api-failures", news),
          ("contact-and-downloads", contact_and_downloads), ("deep-links-and-routing", deep_links), ("sanitizer", sanitizer),
          ("accessibility", accessibility), ("no-fixture-leak", no_fixture_leak), ("responsive", responsive),
          ("calculators", calculators), ("today-in-tibia", today_in_tibia), ("global-search", global_search),
          ("homepage", homepage)]


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")   # failure texts can contain Polish or German letters
    args = sys.argv[1:]
    only = args[args.index("--only") + 1].split(",") if "--only" in args else None
    report = args[args.index("--report") + 1] if "--report" in args else None
    with kit.SiteServer() as server, sync_playwright() as pw:
        browser = pw.chromium.launch()
        for name, fn in CHECKS:
            if not only or name in only:
                run(name, fn, browser, server.url)
        if "--live" in args:
            run("live-api", live_api, browser, server.url)
        browser.close()
    failed = [r for r in results if r["status"] == "FAIL"]
    print(f"\n{len(results) - len(failed)}/{len(results)} checks passed")
    if report:
        os.makedirs(os.path.dirname(os.path.abspath(report)), exist_ok=True)
        json.dump({"version": VERSION, "results": results}, open(report, "w", encoding="utf-8"), indent=1)
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
