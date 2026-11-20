"""
Locale key parity of the website (exit code 1 on any problem):

    python -I website/tests/check_i18n.py [--title-case | --no-title-case]

For every language of src/registry.json against English (src/locales/en.json):
  * missing keys, empty texts, keys English does not have (typos, removed texts), duplicate keys inside a file;
  * texts that change markup, code or placeholders ({version}, {n}, {link}...);
  * texts identical to English that are not listed as intended in _meta.sameAsEnglish (untranslated new strings);
  * every attribution text (credits.<provider> for each entry of src/providers.json) exists and keeps {link};
  * every text key used by the templates and scripts exists in English;
  * English Title Case (on by default, --no-title-case turns it off) for page titles, menu items, section headings, tool
    names and buttons: the keys nav.*, *.title, *.heading, *.button, calc.*.title and home.paths.*, plus the texts the
    templates and scripts put in headings, buttons and .btn links (see title_case_keys).
"""
import html.parser
import importlib.util
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WEBSITE = os.path.dirname(HERE)
SRC = os.path.join(WEBSITE, "src")

spec = importlib.util.spec_from_file_location("build_site", os.path.join(WEBSITE, "build_site.py"))
bs = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bs)


def load_checked(path, problems, code):
    def hook(pairs):
        seen = set()
        for k, _ in pairs:
            if k in seen:
                problems.append(f"[{code}] duplicate key '{k}'")
            seen.add(k)
        return dict(pairs)
    return json.load(open(path, encoding="utf-8"), object_pairs_hook=hook)


# ---------------------------------------------------------------------------------------------- English Title Case
# Page titles, menu items, section headings, tool names and buttons are in Title Case in English (other languages
# follow their own rules). Small words stay lowercase unless they come first or last, or start a part after a colon,
# a full stop, a question or an exclamation mark. Words that are entirely uppercase or contain digits are allowed as written ("GIFs" is mixed
# case, "9.0.5" has digits); so are file extensions (".zip"), {placeholders} and the brand or literal strings below.
SMALL_WORDS = {"a", "an", "and", "as", "at", "but", "by", "for", "in", "of", "on", "or", "the", "to", "vs", "vs.", "with"}
TITLE_CASE_ALLOW = ["Tibia Foundry", "TibiaData", "Tibia.com", "GitHub", "TibiaWiki", "CipSoft GmbH", "tibia.com",
                    "tibiaroute.com", "SHA256SUMS.txt", "e.g."]
# Keys by name: nav.*, *.title, *.heading, *.button, calc.*.title and home.paths.* (the brief's list).
TITLE_CASE_KEY = re.compile(r"^(nav\..+|.+\.title|.+\.heading|.+\.button|home\.paths\..+)$")
# Keys that are not titles: running text, a placeholder and the screen-reader note "(opens in a new tab)" inside .btn links.
TITLE_CASE_EXEMPT = re.compile(r"^(home\.paths\.\w+\.text|nav\.searchPlaceholder|js\.newTab)$")
# Headings, buttons and tool names rendered by build_site.py or by scripts from variables (not found by the scans).
TITLE_CASE_EXTRA = {
    "js.groupPlayers", "js.groupWorlds", "js.groupReference", "js.groupNews",      # hub section headings
    "js.toolCharacter", "js.toolWorlds", "js.toolGuilds", "js.toolHighscores", "js.toolHouses", "js.toolKills",
    "js.toolCreatures", "js.toolSpells", "js.toolBosses", "js.toolFansites", "js.toolNews", "js.toolArchive",   # tool names
    "js.killsOn", "js.guildsOf", "js.housesIn", "js.hubTitle", "js.allTools",       # view titles
    "js.searchBox.recent", "js.searchBox.clearRecent", "js.suggestGo", "js.retry",
    "footer.issues", "footer.repository", "footer.releaseNotes", "lightbox.close", "notice.title",
    "tibia.overview", "contact.reasons.general", "contact.reasons.bug", "contact.reasons.formula",
    "contact.reasons.fansite", "contact.reasons.app", "contact.reasons.dev",
}
# In the templates: a text that is the whole content of a heading, a button, a summary or a .btn link.
TEMPLATE_TITLE_TAGS = {"h1", "h2", "h3", "h4", "button", "summary"}
# In the scripts: section(t("js.x")), h("h2", {... text: t("js.x")}), buttons and .btn links.
SCRIPT_TITLE_PATTERNS = [
    r"""section\(\s*t\(\s*"(js\.[\w.]+)\"""",
    r"""h\("h[1-4]",\s*\{[^{}]*?text:\s*t\(\s*"(js\.[\w.]+)\"""",
    r"""class:\s*"btn[^"]*"[^{}]*?text:\s*t\(\s*"(js\.[\w.]+)\"""",
    r""""copy-label",\s*text:\s*(?:\w+\s*\|\|\s*)?t\(\s*"(js\.[\w.]+)\"""",
    r"""copyButton\([^;]*?,\s*t\(\s*"(js\.[\w.]+)"\s*\)\s*\)""",
    r"""(?:ext|link)\([^;]*?,\s*t\(\s*"(js\.[\w.]+)"\s*\),\s*(?:"btn|\{\s*class:\s*"btn)""",
    r"""icon\("[\w-]+"\),\s*t\(\s*"(js\.[\w.]+)"\s*\)""",
    r"""t\(\s*"(js\.[\w.]+)"\s*\),\s*icon\(""",
]


class _TitleScan(html.parser.HTMLParser):
    """Collects {{t:key}} texts whose nearest enclosing element is a heading, a button, a summary or a .btn link."""

    def __init__(self):
        super().__init__(convert_charrefs=False)
        self.stack, self.keys = [], set()

    def handle_starttag(self, tag, attrs):
        if tag in ("br", "img", "input", "meta", "link", "use", "source", "hr"):
            return
        cls = dict(attrs).get("class") or ""
        self.stack.append(tag in TEMPLATE_TITLE_TAGS or (tag == "a" and re.search(r"\bbtn\b", cls) is not None))

    def handle_endtag(self, tag):
        if self.stack and tag not in ("br", "img", "input", "meta", "link", "use", "source", "hr"):
            self.stack.pop()

    def handle_data(self, data):
        if any(self.stack):
            self.keys |= set(re.findall(r"\{\{t:([\w.-]+)\}\}", data))


def title_case_keys(english):
    keys = {k for k in english if TITLE_CASE_KEY.match(k)}
    for name in os.listdir(os.path.join(SRC, "templates")):
        scan = _TitleScan()
        scan.feed(open(os.path.join(SRC, "templates", name), encoding="utf-8").read())
        keys |= scan.keys
    for name in os.listdir(os.path.join(SRC, "js")):
        code = open(os.path.join(SRC, "js", name), encoding="utf-8").read()
        for pattern in SCRIPT_TITLE_PATTERNS:
            keys |= set(re.findall(pattern, code))
    keys |= TITLE_CASE_EXTRA
    return sorted(k for k in keys if isinstance(english.get(k), str) and not TITLE_CASE_EXEMPT.match(k))


def title_case_words(text):
    """The words of a text that break English Title Case (empty when the text is fine)."""
    for literal in TITLE_CASE_ALLOW:
        text = text.replace(literal, " Literal ")
    text = re.sub(r"<[^>]+>|\{[^}]*\}", " Literal ", text)
    tokens = text.split()
    bad = []
    starts_part = True
    for i, token in enumerate(tokens):
        word = token.strip("()[]\"'“”‘’«»,;!?…")
        ends_part = token.endswith((":", ".", "!", "?")) and not token.endswith("...")
        word = word.rstrip(":.")
        last = i == len(tokens) - 1
        if not word or word in ("–", "—", "-", "&", "/", "·", "+", "×") or not re.search(r"[A-Za-z]", word) \
                or re.search(r"\d", word) or word.startswith("."):
            continue
        for j, part in enumerate(p for p in re.split(r"[-/]", word) if p):
            letters = re.sub(r"[^A-Za-z]", "", part)
            if not letters or letters.isupper() or re.search(r"[A-Z]", part[1:]):
                continue   # all caps, or a mixed-case name such as GIFs or iOS
            first_or_last = (starts_part and j == 0) or last
            if part.lower() in SMALL_WORDS and not first_or_last:
                if part != part.lower():
                    bad.append(part)
            elif not part[0].isupper():
                bad.append(part)
        starts_part = ends_part
    return bad


def title_case_problems(english):
    problems = []
    for key in title_case_keys(english):
        bad = title_case_words(english[key])
        if bad:
            problems.append(f"[en] '{key}' is not in Title Case ({', '.join(bad)}): {english[key]!r}")
    return problems


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    problems = []
    trees = {code: load_checked(os.path.join(SRC, "locales", code + ".json"), problems, code) for code, *_ in bs.LANGUAGES}
    flat = {code: bs.flatten(t) for code, t in trees.items()}
    english = flat[bs.DEFAULT]
    problems += bs.validate(flat)
    for code, texts in flat.items():
        if code == bs.DEFAULT:
            continue
        intended = set(trees[code].get("_meta", {}).get("sameAsEnglish", []))
        for key, value in english.items():
            if key not in texts:
                problems.append(f"[{code}] missing '{key}'")
            elif texts[key] in ("", None) or (isinstance(texts[key], str) and not texts[key].strip()):
                problems.append(f"[{code}] empty '{key}'")
            elif texts[key] == value and key not in intended:
                problems.append(f"[{code}] '{key}' is still English (translate it or list it in _meta.sameAsEnglish)")
    for p in json.load(open(os.path.join(SRC, "providers.json"), encoding="utf-8"))["providers"]:
        for code, texts in flat.items():
            text = texts.get(f"credits.{p['id']}", "")
            if "{link}" not in text:
                problems.append(f"[{code}] attribution credits.{p['id']} missing or without {{link}}")
    # Keys used by templates and scripts.
    used = set()
    for folder, pattern in ((os.path.join(SRC, "templates"), r"\{\{[ta]:([\w.-]+)\}\}"), (os.path.join(SRC, "js"), r"""\bt\(\s*["'](js\.[\w.]+)["']""")):
        for name in os.listdir(folder):
            used |= set(re.findall(pattern, open(os.path.join(folder, name), encoding="utf-8").read()))
    for key in sorted(used):
        if key not in english and not key.endswith("."):
            problems.append(f"[en] '{key}' is used by a template or script but missing")
    if "--no-title-case" not in sys.argv[1:]:
        title_problems = title_case_problems(english)
        print(f"Title Case: {len(title_case_keys(english))} English titles, headings and buttons checked")
        problems += title_problems
    total = len(english)
    for code in flat:
        print(f"{code:6} {len(flat[code]):4} / {total} texts")
    if problems:
        print(f"\n{len(problems)} problem(s):\n  " + "\n  ".join(problems))
        sys.exit(1)
    print(f"OK: {len(flat)} locales, {total} keys each, no missing, empty, extra, duplicate or untranslated texts.")


if __name__ == "__main__":
    main()
