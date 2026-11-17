"""
Records real TibiaData responses as deterministic test fixtures (tests and screenshots only - never deployed):

    python -I website/tests/record_fixtures.py            # (re)record every fixture
    python -I website/tests/record_fixtures.py --check    # only verify that the canonical examples still exist

The canonical examples come from src/registry.json "fixtures" (character Roc ky, world Antica, a guild, a house, a
creature, a spell and a news article). Every request the site makes for the registry's sample routes, the home page
and the site navigation is recorded as fixtures/tibiadata/<name>.json; fixtures/tibiadata/manifest.json maps each API
path to its file and records when and from which API version it was taken. Answers that are errors (the missing
character) are stored with their HTTP status.
"""
import datetime
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
WEBSITE = os.path.dirname(HERE)
REGISTRY = json.load(open(os.path.join(WEBSITE, "src", "registry.json"), encoding="utf-8"))
OUT = os.path.join(HERE, "fixtures", "tibiadata")
BASE = REGISTRY["api"]["base"]


def q(s):
    return urllib.parse.quote(str(s), safe="")


def paths():
    f = REGISTRY["fixtures"]
    w = f["world"]
    return {
        "worlds": "worlds", "world": f"world/{q(w)}", "character": f"character/{q(f['character'])}",
        "character-missing": f"character/{q(f['missingCharacter'])}", "guilds": f"guilds/{q(w)}", "guild": f"guild/{q(f['guild'])}",
        "highscores": f"highscores/{q(w)}/experience/all/1", "highscores-all": "highscores/all/experience/all/1",
        "highscores-magic": f"highscores/{q(w)}/magiclevel/all/1",
        "houses": f"houses/{q(f['house']['world'])}/{q(f['house']['town'])}", "house": f"house/{q(f['house']['world'])}/{f['house']['id']}",
        "killstatistics": f"killstatistics/{q(w)}", "creatures": "creatures", "creature": f"creature/{q(f['creature'])}",
        "spells": "spells", "spell": f"spell/{q(f['spell'])}", "boostablebosses": "boostablebosses", "fansites": "fansites",
        "news": "news/latest", "newsticker": "news/newsticker", "newsarchive": "news/archive/90", "newsarticle": f"news/id/{f['news']}",
    }


def fetch(path):
    req = urllib.request.Request(BASE + path, headers={"Accept": "application/json", "User-Agent": "TibiaFoundry-website-tests"})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return r.status, json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read().decode("utf-8") or "null")


def main():
    check = "--check" in sys.argv[1:]
    os.makedirs(OUT, exist_ok=True)
    manifest = {"api": BASE, "recorded": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
                "purpose": "Test and screenshot fixtures only. Never served by the production website.", "files": {}}
    problems = []
    for name, path in paths().items():
        status, body = fetch(path)
        expected = 404 if name == "character-missing" else 200
        if status != expected:
            problems.append(f"{name}: {path} answered {status}, expected {expected}")
            continue
        if check:
            print(f"ok   {name:18} {path}")
            continue
        file = re.sub(r"[^A-Za-z0-9._-]+", "_", name) + ".json"
        with open(os.path.join(OUT, file), "w", encoding="utf-8", newline="\n") as f:
            json.dump(body, f, ensure_ascii=False, indent=1)
        manifest["files"][path] = {"file": file, "status": status, "api_release": ((body or {}).get("information") or {}).get("api", {}).get("release")}
        print(f"saved {name:18} {path} -> {file}")
    if not check:
        with open(os.path.join(OUT, "manifest.json"), "w", encoding="utf-8", newline="\n") as f:
            json.dump(manifest, f, indent=1)
    if problems:
        sys.exit("Fixture problems:\n  " + "\n  ".join(problems))


if __name__ == "__main__":
    main()
