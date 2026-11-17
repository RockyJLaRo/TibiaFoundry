"""
Locale key parity of the website (exit code 1 on any problem):

    python -I website/tests/check_i18n.py

For every language of src/registry.json against English (src/locales/en.json):
  * missing keys, empty texts, keys English does not have (typos, removed texts), duplicate keys inside a file;
  * texts that change markup, code or placeholders ({version}, {n}, {link}...);
  * texts identical to English that are not listed as intended in _meta.sameAsEnglish (untranslated new strings);
  * every attribution text (credits.<provider> for each entry of src/providers.json) exists and keeps {link};
  * every text key used by the templates and scripts exists in English.
"""
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
    total = len(english)
    for code in flat:
        print(f"{code:6} {len(flat[code]):4} / {total} texts")
    if problems:
        print(f"\n{len(problems)} problem(s):\n  " + "\n  ".join(problems))
        sys.exit(1)
    print(f"OK: {len(flat)} locales, {total} keys each, no missing, empty, extra, duplicate or untranslated texts.")


if __name__ == "__main__":
    main()
