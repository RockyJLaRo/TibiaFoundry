"""
Finds places where translated text might be used as program logic: string literals that remain in
the code (comparisons, switch arms, initial values, lists) while the same text is also shown through
the string table. Each hit needs a look: either the literal is an identifier (fine) or the shown
text and the compared value must be separated.

Usage: python audit_logic.py folder...
"""
import os, re, sys
sys.path.insert(0, os.path.dirname(__file__))
import strings_io
from extract_cs import literals

en = strings_io.load("en")
texts = {}
for k, v in en.items(): texts.setdefault(v, k)
hits = 0
for root in sys.argv[1:]:
    for d, _, fs in os.walk(root):
        if os.sep + "bin" in d or os.sep + "obj" in d: continue
        for f in fs:
            if not f.endswith(".cs"): continue
            p = os.path.join(d, f)
            src = open(p, encoding="utf-8-sig").read()
            for lit in literals(src):
                if lit.segments == ["\0RAW"] or lit.interpolated: continue
                t = "".join(s for s in lit.segments if isinstance(s, str))
                if t in texts and re.search(r"[A-Za-z]{2}", t) and t not in ("Tibia", "Tibia Foundry", "TibiaWiki"):
                    line = src.count("\n", 0, lit.start) + 1
                    ctx = src[src.rfind("\n", 0, lit.start) + 1: src.find("\n", lit.start)].strip()
                    print(f"{os.path.relpath(p)}:{line}\t{texts[t]}\t{ctx[:160]}")
                    hits += 1
print(f"{hits} literals share their text with a table entry", file=sys.stderr)
