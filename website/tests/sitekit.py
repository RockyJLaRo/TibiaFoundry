"""
Shared helpers of the website tests and the screenshot generator:

* SiteServer: serves website/site/ on 127.0.0.1 like GitHub Pages (index.html for folders, 404.html with status 404);
* use_fixtures(context): answers every TibiaData request from tests/fixtures/tibiadata (recorded real responses), so
  tests and screenshots are deterministic; requests without a fixture are recorded in .unknown and answered 404;
* registry, languages, pages, data routes, viewports and themes from src/registry.json - nothing is hard-coded.
"""
import functools
import http.server
import json
import os
import socketserver
import threading
import urllib.parse

HERE = os.path.dirname(os.path.abspath(__file__))
WEBSITE = os.path.dirname(HERE)
SITE = os.path.join(WEBSITE, "site")
REGISTRY = json.load(open(os.path.join(WEBSITE, "src", "registry.json"), encoding="utf-8"))
PROVIDERS = json.load(open(os.path.join(WEBSITE, "src", "providers.json"), encoding="utf-8"))["providers"]
FIXTURES = os.path.join(HERE, "fixtures", "tibiadata")
LANGUAGES = REGISTRY["languages"]
PAGES = REGISTRY["pages"]
DATA_ROUTES = REGISTRY["dataRoutes"]
THEMES = REGISTRY["themes"]
API_BASE = REGISTRY["api"]["base"]
WIDTHS = [(320, 800), (360, 800), (390, 844), (430, 932), (768, 1024), (1024, 768), (1280, 800), (1440, 1000), (1920, 1080)]
SCREENSHOT_VIEWPORTS = {"small-mobile": (360, 780), "mobile": (390, 844), "tablet": (768, 1024), "laptop": (1280, 800), "desktop": (1440, 1000)}


class _Handler(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def send_error(self, code, message=None, explain=None):
        # GitHub Pages answers unknown paths with the site's 404.html.
        if code == 404 and os.path.exists(os.path.join(SITE, "404.html")):
            body = open(os.path.join(SITE, "404.html"), "rb").read()
            self.send_response(404)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        super().send_error(code, message, explain)


class _Server(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = True

    def handle_error(self, request, client_address):
        pass  # a browser that stops a download (navigation) is not an error


class SiteServer:
    def __init__(self, port=0):
        handler = functools.partial(_Handler, directory=SITE)
        self.httpd = _Server(("127.0.0.1", port), handler)
        self.url = f"http://127.0.0.1:{self.httpd.server_address[1]}/"
        self.thread = threading.Thread(target=self.httpd.serve_forever, daemon=True)

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, *exc):
        self.httpd.shutdown()


def page_path(lang, page):
    """URL path of a page in a language, relative to the site root ('', 'de/', 'tibia/', 'de/tibia/')."""
    folder = lang["folder"]
    return (folder + "/" if folder else "") + page["path"]


def data_hash(sample):
    return "#/" + "/".join(urllib.parse.quote(p, safe="") for p in sample.split("/")) if sample else "#/"


def manifest():
    return json.load(open(os.path.join(FIXTURES, "manifest.json"), encoding="utf-8"))


def use_fixtures(context, overrides=None):
    """Routes api.tibiadata.com to the recorded fixtures. overrides: {api path prefix: callable(route) or (status, body)}."""
    files = manifest()["files"]
    context.unknown_api = []
    context.api_calls = []

    def handle(route):
        url = route.request.url
        path = url[len(API_BASE):] if url.startswith(API_BASE) else urllib.parse.urlparse(url).path.lstrip("/")
        context.api_calls.append(path)
        for prefix, answer in (overrides or {}).items():
            if path.startswith(prefix):
                if callable(answer):
                    return answer(route)
                status, body = answer
                return route.fulfill(status=status, body=body, headers={"Access-Control-Allow-Origin": "*", "Content-Type": "application/json"})
        entry = files.get(path)
        if not entry:
            context.unknown_api.append(path)
            return route.fulfill(status=404, body=json.dumps({"information": {"status": {"http_code": 404, "message": "no fixture"}}}),
                                 headers={"Access-Control-Allow-Origin": "*", "Content-Type": "application/json"})
        body = open(os.path.join(FIXTURES, entry["file"]), "rb").read()
        route.fulfill(status=entry["status"], body=body, headers={"Access-Control-Allow-Origin": "*", "Content-Type": "application/json; charset=utf-8"})

    context.route("https://api.tibiadata.com/**", handle)
