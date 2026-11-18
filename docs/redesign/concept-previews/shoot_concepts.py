"""Screenshots of the three design concepts (docs/redesign/concept-previews) in both themes on a phone and a desktop.

    python -I docs/redesign/concept-previews/shoot_concepts.py [out_dir]   (needs Playwright; serves the repository root
    on a local port; out_dir defaults to docs/redesign/concept-previews/screenshots)

Design exploration only: nothing here is part of the published website (website/build_site.py never reads it).
"""
import functools, http.server, os, sys, threading
from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
WEB = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "screenshots")
os.makedirs(out, exist_ok=True)
class Quiet(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *a):
        pass


handler = functools.partial(Quiet, directory=WEB)
srv = http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler)
threading.Thread(target=srv.serve_forever, daemon=True).start()
base = f"http://127.0.0.1:{srv.server_address[1]}/docs/redesign/concept-previews/index.html"
with sync_playwright() as pw:
    b = pw.chromium.launch()
    for concept in "abc":
        for theme in ("light", "dark"):
            for name, vp, mobile in (("desktop", (1440, 900), False), ("phone", (390, 844), True)):
                ctx = b.new_context(viewport={"width": vp[0], "height": vp[1]}, is_mobile=mobile, device_scale_factor=1 if not mobile else 2)
                p = ctx.new_page()
                p.goto(f"{base}?c={concept}&t={theme}")
                p.wait_for_timeout(300)
                p.screenshot(path=os.path.join(out, f"concept-{concept}-{theme}-{name}.jpg"), type="jpeg", quality=72, full_page=True)
                ctx.close()
    b.close()
srv.shutdown()
print("12 concept screenshots in", out)
