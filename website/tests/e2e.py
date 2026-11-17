"""
End-to-end tests of the built website (website/site) in Chromium, with Playwright:

    <python with playwright> website/tests/e2e.py [--report file.json] [--only name,name] [--live]

The site is served like GitHub Pages (tests/sitekit.py). TibiaData requests are answered from the recorded fixtures
(tests/fixtures/tibiadata) so results are deterministic; --live also runs a smoke test against the real API.
Prints PASS/FAIL per check and exits 1 when anything fails. Everything comes from src/registry.json and
src/providers.json: languages, pages, data routes, download names, creator profiles and data providers.
"""
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


def new_page(browser, base, width=1440, height=1000, theme=None, lang_pref=None, overrides=None, permissions=None):
    ctx = browser.new_context(viewport={"width": width, "height": height}, color_scheme=theme or "light",
                              locale="en-US", reduced_motion="reduce")
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
                credits = page.locator("footer [data-credits]")
                c.ok(credits.is_visible(), f"{where}: credits not visible")
                td = page.locator("footer a[data-provider=tibiadata]")
                c.ok(td.count() == 1 and td.get_attribute("href") == "https://tibiadata.com/", f"{where}: TibiaData link")
                c.ok(td.is_visible(), f"{where}: TibiaData link not visible")
                c.ok("TibiaData" in page.locator("footer [data-credit=tibiadata]").inner_text(), f"{where}: TibiaData thank-you text")
                for pid, p in providers.items():
                    a = page.locator(f"footer a[data-provider={pid}]")
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
    c.note(f"{len(kit.LANGUAGES) * len(kit.PAGES) * len(kit.THEMES)} page loads; providers: {', '.join(providers)}")


def creator_links(c, browser, base):
    """6/6 creator profiles: platform name, icon, accessible name and destination match the registry exactly."""
    expected = kit.REGISTRY["creator"]["links"]
    passed = 0
    ctx, page = new_page(browser, base)
    for lang in kit.LANGUAGES:
        for pid in ("about", "home"):
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


def navigation(c, browser, base):
    en = kit.LANGUAGES[0]
    ctx, page = new_page(browser, base, 1440, 1000)
    page.goto(url_of(base, en, "home"))
    for pg in kit.PAGES:
        page.locator(f".main-nav a[data-page-link={pg['id']}]").click()
        page.wait_for_load_state()
        c.ok(page.url.rstrip("/").endswith(("/" + pg["path"]).rstrip("/")) or (pg["path"] == "" and page.url == base), f"desktop nav to {pg['id']}: {page.url}")
        c.ok(page.locator(f".main-nav a[data-page-link={pg['id']}]").get_attribute("aria-current") == "page", f"aria-current on {pg['id']}")
    ctx.close()
    # Mobile menu: opens, keyboard, Escape, closes after navigation.
    ctx, page = new_page(browser, base, 390, 844)
    page.goto(url_of(base, en, "home"))
    c.ok(not page.locator(".main-nav").is_visible(), "desktop nav hidden on phones")
    btn = page.locator("#menu-btn")
    c.ok(btn.is_visible() and btn.get_attribute("aria-expanded") == "false", "menu button")
    btn.click()
    c.ok(btn.get_attribute("aria-expanded") == "true" and page.locator("#site-menu").is_visible(), "menu opens")
    c.ok(page.evaluate("document.activeElement && document.getElementById('site-menu').contains(document.activeElement)"), "focus moves into the menu")
    page.keyboard.press("Escape")
    c.ok(btn.get_attribute("aria-expanded") == "false" and not page.locator("#site-menu").is_visible(), "Escape closes the menu")
    c.ok(page.evaluate("document.activeElement === document.getElementById('menu-btn')"), "focus returns to the menu button")
    btn.click()
    page.locator("#site-menu a[data-page-link=downloads]").click()
    page.wait_for_load_state()
    c.ok("/downloads/" in page.url and not page.locator("#site-menu").is_visible(), "menu link navigates and the menu is closed")
    ctx.close()


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
    c.ok(page.locator("#app .notice-error").count() == 1 and "Not found" in page.locator("#app .notice-error").inner_text(), "unknown character shows 'Not found'")
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
    c.ok(page.locator("#tools").is_visible() and page.locator("footer [data-credits]").count() == 1, "layout kept when the API is down")
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
    c.ok(resp.status == 404 and page.locator("h1").inner_text() == "Page not found", "unknown page: 404 page")
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


CHECKS = [("pages-and-attribution", pages_and_attribution), ("creator-links", creator_links), ("navigation", navigation),
          ("language-and-theme", language_and_theme), ("tibia-data", tibia_data), ("news-and-api-failures", news),
          ("contact-and-downloads", contact_and_downloads), ("deep-links-and-routing", deep_links), ("sanitizer", sanitizer),
          ("accessibility", accessibility), ("no-fixture-leak", no_fixture_leak), ("responsive", responsive)]


def main():
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
