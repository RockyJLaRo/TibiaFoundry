"""Screenshots of the palette lab (docs/redesign/stage2/palette-lab): 4 palettes x light/dark at 1280 px, plus one sheet.
    python -I docs/redesign/stage2/shoot_lab.py
"""
import functools, http.server, os, threading
from playwright.sync_api import sync_playwright
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
OUT = os.path.join(HERE, "palette-lab", "screenshots"); os.makedirs(OUT, exist_ok=True)
class Q(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *a): pass
srv = http.server.ThreadingHTTPServer(("127.0.0.1", 0), functools.partial(Q, directory=ROOT)); threading.Thread(target=srv.serve_forever, daemon=True).start()
base = f"http://127.0.0.1:{srv.server_address[1]}/docs/redesign/stage2/palette-lab/index.html"
shots = []
with sync_playwright() as pw:
    b = pw.chromium.launch()
    for p in ("p0", "p1", "p2", "p3"):
        for t in ("light", "dark"):
            pg = b.new_page(viewport={"width": 1280, "height": 760}); pg.goto(f"{base}?p={p}&t={t}"); pg.wait_for_timeout(200)
            f = os.path.join(OUT, f"{p}-{t}.png"); pg.screenshot(path=f, full_page=True); pg.close(); shots.append((p, t, f))
    b.close()
srv.shutdown()
font = ImageFont.truetype("arialbd.ttf", 26)
ims = [Image.open(f).convert("RGB") for _, _, f in shots]; w, h = ims[0].size; s = 0.5
sheet = Image.new("RGB", (int(w * s) * 2, (int(h * s) + 36) * 4), "white"); d = ImageDraw.Draw(sheet)
for i, (p, t, f) in enumerate(shots):
    x, y = (i % 2) * int(w * s), (i // 2) * (int(h * s) + 36)
    d.text((x + 8, y + 4), f"{p} {t}", fill="black", font=font); sheet.paste(ims[i].resize((int(w * s), int(ims[i].height * s))), (x, y + 36))
sheet.save(os.path.join(OUT, "sheet.jpg"), quality=80)
print("ok", len(shots))
