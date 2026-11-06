"""
Splits the English texts that still need translating into chunks for translators, and merges translated
chunks back into the string tables.

  python translation_chunks.py export <folder> [chars-per-chunk]
      writes <folder>/src_NN.tsv: "<id>\t<key>\t<English>" (one line per distinct text; newlines as \\n)
  python translation_chunks.py merge <folder>
      reads <folder>/tr_NN.tsv: "<id>\t<pt-BR>\t<pl>\t<es>\t<de>" and adds the texts to Strings.{code}.xaml
      for every key that has that English text. Lines whose placeholders differ from the English are
      reported and skipped.

Only texts missing from at least one language are exported, so the tool also serves later updates.
"""
import json, os, re, sys
import strings_io

CODES = ["pt-BR", "pl", "es", "de"]


def esc(t): return t.replace("\\", "\\\\").replace("\n", "\\n").replace("\t", "\\t")


def unesc(t):
    out, i = [], 0
    while i < len(t):
        if t[i] == "\\" and i + 1 < len(t):
            out.append({"n": "\n", "t": "\t", "\\": "\\"}.get(t[i + 1], t[i + 1])); i += 2
        else:
            out.append(t[i]); i += 1
    return "".join(out)


def edges(english, tr):
    lead, trail = english[:len(english) - len(english.lstrip(" "))], english[len(english.rstrip(" ")):]
    return lead + tr.strip(" ") + trail


def export(folder, size=9000):
    en = strings_io.load("en")
    tables = {c: strings_io.load(c) for c in CODES}
    texts = {}
    for k, v in en.items():
        if all(k in tables[c] for c in CODES): continue
        texts.setdefault(v, []).append(k)
    os.makedirs(folder, exist_ok=True)
    index = {}
    chunk, n, length = [], 0, 0
    for i, (text, keys) in enumerate(texts.items()):
        index[i] = text
        chunk.append(f"{i}\t{keys[0]}\t{esc(text)}")
        length += len(text) + 20
        if length >= size:
            write(folder, n, chunk); n += 1; chunk, length = [], 0
    if chunk: write(folder, n, chunk); n += 1
    with open(os.path.join(folder, "index.json"), "w", encoding="utf-8") as f: json.dump(index, f, ensure_ascii=False)
    print(f"{len(texts)} texts in {n} chunks")


def write(folder, n, lines):
    with open(os.path.join(folder, f"src_{n:02}.tsv"), "w", encoding="utf-8", newline="\n") as f: f.write("\n".join(lines) + "\n")


def merge(folder):
    en = strings_io.load("en")
    with open(os.path.join(folder, "index.json"), encoding="utf-8") as f: index = {int(k): v for k, v in json.load(f).items()}
    by_text = {}
    for k, v in en.items(): by_text.setdefault(v, []).append(k)
    tables = {c: strings_io.load(c) for c in CODES}
    added, problems = 0, []
    for name in sorted(os.listdir(folder)):
        if not re.fullmatch(r"tr_\d+\.tsv", name): continue
        for line_no, line in enumerate(open(os.path.join(folder, name), encoding="utf-8"), 1):
            line = line.rstrip("\n")
            if not line.strip() or line.startswith("#"): continue
            parts = line.split("\t")
            if len(parts) != 5 or not parts[0].isdigit():
                problems.append(f"{name}:{line_no}: expected 5 columns, got {len(parts)}: {line[:80]}"); continue
            text = index.get(int(parts[0]))
            if text is None: problems.append(f"{name}:{line_no}: unknown id {parts[0]}"); continue
            for code, tr in zip(CODES, parts[1:]):
                # Editors drop spaces at line ends, so the English leading/trailing spaces are applied.
                tr = edges(text, unesc(tr))
                if strings_io.placeholders(tr) != strings_io.placeholders(text):
                    problems.append(f"{name}:{line_no} [{code}] placeholders {strings_io.placeholders(tr)} != {strings_io.placeholders(text)}: {tr[:80]}")
                    continue
                for key in by_text.get(text, []):
                    tables[code][key] = tr
                    added += 1
    for c in CODES: strings_io.save(c, tables[c], order=en)
    print(f"{added} entries written")
    for p in problems: print("PROBLEM", p)


if __name__ == "__main__":
    if sys.argv[1] == "export": export(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 9000)
    else: merge(sys.argv[2])
