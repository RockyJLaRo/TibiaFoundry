"""
Moves user-facing string literals of C# files into the English string table.

  "Some text"              -> L.T("Area.SomeText")
  $"Saved {n:N0} files."   -> L.F("Area.SavedFiles", n)        (table: "Saved {0:N0} files.")
  "a " + "b"               -> merged into one text first
  Plural.Count(n, "file")  -> L.Count(n, "Noun.file")           (table: Noun.file.one / .other)

Literals that are not interface text stay as they are: log messages (AppLog), exception messages,
attributes, comparisons, keys and indexers, paths, identifiers, regular expressions, JSON names.
Run with --dry-run to write the decisions to a TSV for review.

Usage: python extract_cs.py [--dry-run decisions.tsv] file-or-folder...
"""
import os, re, sys
from collections import defaultdict
import strings_io

# ----------------------------------------------------------------------------- tokenizer

class Lit:
    """A string literal: [start, end) in the source; segments = text pieces and holes (expr, spec)."""
    def __init__(self, start, end, interpolated, segments):
        self.start, self.end, self.interpolated, self.segments = start, end, interpolated, segments

    @property
    def text(self):
        return "".join(s if isinstance(s, str) else "{…}" for s in self.segments)


ESC = {"n": "\n", "r": "\r", "t": "\t", "0": "\0", "\\": "\\", "\"": "\"", "'": "'", "a": "\a", "b": "\b", "f": "\f", "v": "\v"}


def skip_ws_comments(src, i):
    n = len(src)
    while i < n:
        if src.startswith("//", i):
            j = src.find("\n", i)
            i = n if j < 0 else j + 1
        elif src.startswith("/*", i):
            j = src.find("*/", i + 2)
            i = n if j < 0 else j + 2
        elif src[i].isspace():
            i += 1
        else:
            break
    return i


def parse_literal(src, i):
    """Parses the literal starting at i (at $, @ or "); returns (Lit, end) or None."""
    j = i
    interp = verbatim = False
    dollars = 0
    while j < len(src) and src[j] in "$@":
        if src[j] == "$": dollars += 1; interp = True
        else: verbatim = True
        j += 1
    if j >= len(src) or src[j] != '"': return None
    # raw string literal """..."""
    if src.startswith('"""', j):
        q = j
        while q < len(src) and src[q] == '"': q += 1
        quotes = q - j
        end = src.find('"' * quotes, q)
        if end < 0: return None
        body = src[q:end]
        segs = []
        if interp:
            k = 0
            opener = "{" * dollars
            while k < len(body):
                h = body.find(opener, k)
                if h < 0: segs.append(body[k:]); break
                segs.append(body[k:h])
                e = find_hole_end(body, h + dollars)
                expr, spec = split_spec(body[h + dollars:e])
                segs.append(("hole", expr, spec, None))
                k = e + dollars
        else:
            segs = [body]
        # raw strings are mostly multi-line templates (HTML, scripts): never converted
        return Lit(i, end + quotes, interp, ["\0RAW"]), end + quotes
    j += 1
    segs, buf = [], []
    while j < len(src):
        c = src[j]
        if verbatim and c == '"':
            if src.startswith('""', j): buf.append('"'); j += 2; continue
            break
        if not verbatim and c == '"': break
        if not verbatim and c == "\\":
            nx = src[j + 1]
            if nx in ESC: buf.append(ESC[nx]); j += 2; continue
            if nx == "u": buf.append(chr(int(src[j + 2:j + 6], 16))); j += 6; continue
            if nx == "U": buf.append(chr(int(src[j + 2:j + 10], 16))); j += 10; continue
            if nx == "x":
                m = re.match(r"[0-9a-fA-F]{1,4}", src[j + 2:])
                buf.append(chr(int(m.group(0), 16))); j += 2 + len(m.group(0)); continue
            buf.append(nx); j += 2; continue
        if interp and c == "{":
            if src.startswith("{{", j): buf.append("{"); j += 2; continue
            segs.append("".join(buf)); buf = []
            e = find_hole_end(src, j + 1)
            expr, spec = split_spec(src[j + 1:e])
            segs.append(("hole", expr, spec, j + 1))
            j = e + 1
            continue
        if interp and c == "}" and src.startswith("}}", j): buf.append("}"); j += 2; continue
        if c == "\n" and not verbatim: return None
        buf.append(c); j += 1
    segs.append("".join(buf))
    return Lit(i, j + 1, interp, [s for s in segs if s != ""]), j + 1


def find_hole_end(src, j):
    """End (index of the closing brace) of an interpolation hole starting at j."""
    depth = 0
    while j < len(src):
        c = src[j]
        if c in "$@\"":
            r = parse_literal(src, j) if (c == '"' or src[j + 1:j + 2] in ('"', '@', '$')) else None
            if r: j = r[1]; continue
        if c == "'":
            m = re.match(r"'(\\.|[^\\'])+'", src[j:])
            if m: j += len(m.group(0)); continue
        if c in "([{": depth += 1
        elif c in ")]}":
            if depth == 0: return j
            depth -= 1
        j += 1
    raise ValueError("unterminated hole")


def split_spec(hole):
    """Splits "expr,align:format" at top level; returns (expr, ",align:format" or "")."""
    depth = 0
    i = 0
    in_str = None
    while i < len(hole):
        c = hole[i]
        if c == '"':
            r = parse_literal(hole, i)
            if r: i = r[1]; continue
        if c == "'":
            m = re.match(r"'(\\.|[^\\'])+'", hole[i:])
            if m: i += len(m.group(0)); continue
        if c in "([{<" and not (c == "<" and depth == 0): depth += 1
        elif c in ")]}": depth -= 1
        elif c == "?" and depth == 0 and hole[i + 1:i + 2] not in (".", "[", "?"):
            # ternary: a ':' after it belongs to the expression
            depth_t = 0
            k = i + 1
            while k < len(hole):
                if hole[k] == '"':
                    r = parse_literal(hole, k)
                    if r: k = r[1]; continue
                if hole[k] in "([{": depth_t += 1
                elif hole[k] in ")]}": depth_t -= 1
                elif hole[k] == ":" and depth_t == 0: i = k; break
                k += 1
        elif c in ",:" and depth == 0:
            if c == ":" and hole[i + 1:i + 2] == ":": i += 2; continue
            return hole[:i], hole[i:]
        i += 1
    return hole, ""


def literals(src):
    """All top-level string literals of a source file (comments and char literals skipped)."""
    out = []
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if src.startswith("//", i):
            j = src.find("\n", i); i = n if j < 0 else j; continue
        if src.startswith("/*", i):
            j = src.find("*/", i + 2); i = n if j < 0 else j + 2; continue
        if c == "'":
            m = re.match(r"'(\\.|[^\\'\n])+'", src[i:])
            if m: i += len(m.group(0)); continue
        if c in '$@"':
            r = parse_literal(src, i)
            if r:
                out.append(r[0]); i = r[1]; continue
        i += 1
    return out

# ----------------------------------------------------------------------------- context

SKIP_CALL = re.compile(r"""(?x)(
    AppLog\.\w+ | BeginScope | \bDebug\.\w+ | \bTrace\.\w+ | Console\.\w+ | \bnameof | Regex | GeneratedRegex | SuppressMessage
  | Exception | \bPath\.\w+ | Environment\.\w+ | \bFile\.\w+ | \bDirectory\.\w+ | ProcessStartInfo | Process\.Start | OnPropertyChanged
  | \bUri | JsonProperty\w* | JsonPropertyName | FindResource | TryFindResource | SetResourceReference | ResourceKey
  | \.(Equals|StartsWith|EndsWith|Contains|Replace|Split|IndexOf|LastIndexOf|Trim|TrimStart|TrimEnd|Matches|Match|IsMatch|Parse|TryParse
      |GetProperty|TryGetProperty|GetValue|SetValue|TryGetValue|ContainsKey|Remove|GetBool|SetBool|GetInt|SetInt|Find\w*|ReadString|WriteString|Load|Escape|Unescape|Normalize|ToUpper\w*|ToLower\w*|RememberFolder|AddProbe|FindName|Descriptor\w*)
  | ^Equals$ | ^Get$ | ^Set$ | string\.Join | String\.Join | Join | \bAttribute | Obsolete | DebuggerDisplay | Description\b(?!s) | DefaultValue | CallerMemberName
  | Sqlite\w* | Command\w*Text | ExecuteNonQuery | Parameters\.\w+ | AddWithValue | \bSql\b | CreateCommand
  | Tag | GetType | Type\.GetType | Assembly\.\w+ | GetManifestResourceStream | Activator | Marshal | DllImport | LibraryImport
  | Key | Id\b | Hash\w* | Header\w*s\.\w+ | DefaultRequestHeaders | UserAgent | MediaType\w* | ContentType | Encoding\.\w+
  | ExternalEndpoints | Endpoint | Url\w* | Hyperlink | OpenUrl | Navigate\w*
)""")

UI_CALL = re.compile(r"""(?x)(
    Status\.Show\w* | \bShow\w*$ | Dialogs\.\w+ | Confirm\w* | Choose\w* | Ask\w* | OpenFile\w* | SaveFile\w* | PickFolder\w* | Prompt\w*
  | \bInfo$ | \bSaved$ | RunAsync | \bTry$ | ReportBatch | Notify\w* | Toast | MessageBox\.Show | \bWarn$ | Fail$ | Problem$ | Report$
  | \bL\.F | \bL\.T | string\.Format | String\.Format
)""")

UI_PROPERTY = re.compile(r"\b(Title|Header|Label|Text|Message|Description|ToolTip|Tooltip|Hint|Notice|Summary|Problem|Placeholder|StatusText|Caption|Subtitle|Help|Advice|Detail|Details|Explanation|Prompt|Question|Body|Heading|DisplayName|Note|Notes|Warning|Error|Reason|Instructions|Empty\w*|\w+Text|\w+Message|\w+Notice|\w+Label|\w+Title|\w+Hint|\w+Tip|\w+Description)\s*(=>|=|\?\?=)\s*$")


def enclosing_call(src, pos):
    """The callee text of the innermost call/construction whose argument list contains pos (or "")."""
    depth = 0
    i = pos - 1
    while i >= 0:
        c = src[i]
        if c == '"':
            # step over a literal backwards (approximate: find the opening quote)
            j = i - 1
            while j >= 0 and not (src[j] == '"' and src[j - 1] != "\\"): j -= 1
            i = j - 1
            continue
        if c in ")]}": depth += 1
        elif c in "([{":
            if depth == 0:
                if c in "[{": return src[max(0, i - 1):i + 1] if c == "[" else "{"
                m = re.search(r"([\w.?!<>\[\]]+)\s*$", src[max(0, i - 200):i])
                return m.group(1) if m else ""
            depth -= 1
        elif c == ";" and depth == 0:
            return ""
        i -= 1
    return ""


def classify(src, lit, text, path):
    """Returns (convert?, reason)."""
    line_start = src.rfind("\n", 0, lit.start) + 1
    line = src[line_start:src.find("\n", lit.start)]
    before = src[line_start:lit.start]
    after = src[lit.end:lit.end + 40]
    stripped = line.lstrip()
    if lit.segments == ["\0RAW"]: return False, "raw"
    if stripped.startswith("[") and not stripped.startswith("[\""): return False, "attribute"
    if stripped.startswith("case ") or re.search(r"\bcase\s*$", before) or re.search(r"\bis\s*(not\s*)?$", before): return False, "case"
    if re.search(r"(==|!=|\bis\s+not|\bis|\bor|\band)\s*$", before) or re.match(r"\s*(==|!=|=>|or\b|and\b)", after): return False, "comparison"
    if KEEP_MARK in line: return False, "kept"
    if re.search(r"[\w)\]?!]\s*\[\s*$", before): return False, "indexer"
    if re.search(r"\b(source|key|name|id|extension|filter|pattern|format|separator|prefix|suffix|category|tool|setting|scope):\s*$", before, re.I) and not re.search(r"\b(filter):\s*$", before): return False, "named-arg"
    if re.search(r"\bconst\s+string\b", before): return False, "const"
    if not re.search(r"[A-Za-z]{2}", text): return False, "no-letters"
    if re.fullmatch(r"\s*(https?://|www\.)\S*\s*", text): return False, "url"
    callee = enclosing_call(src, lit.start)
    if callee and re.search(r"AppLog\.|BeginScope|Suppressed|CreateOutput|UniqueName|\.ToString$|ToString$", callee): return False, "call:" + callee
    if callee and SKIP_CALL.search(callee) and not UI_CALL.search(callee): return False, "call:" + callee
    if re.fullmatch(r"[yMdHhmsfFtzK:\-./ ,']+", text): return False, "date-format"
    words = re.findall(r"[A-Za-z]+", text)
    has_space = " " in text.strip()
    ui = bool(callee and UI_CALL.search(callee)) or bool(UI_PROPERTY.search(before))
    if not has_space:
        # Single words: identifiers unless they are clearly shown (a dialog button, a status line, a Title = …,
        # a report row or table header).
        if WORDS and re.fullmatch(r"[A-Z][a-z]+(-[a-z]+)?:?|[A-Z][a-z]+ ?[a-z]*", text.strip()) and callee and re.search(WORDS, callee):
            return True, "ui-label"
        if not ui: return False, "single-word"
        if re.search(r"[._/\\]|^[a-z]+[A-Z]|^[a-z0-9-]+$", text.strip()): return False, "identifier"
        return True, "ui-word"
    if re.fullmatch(r"[\w.*|;,\s-]*\*\.[\w*]+[\w.*|;,\s-]*", text) and "|" not in text: return False, "glob"
    first = lit.segments[0] if lit.segments and isinstance(lit.segments[0], str) else ""
    if first.strip().startswith(("<", "--", "SELECT", "INSERT", "UPDATE", "CREATE", "DELETE", "PRAGMA")) or (not lit.interpolated and first.strip().startswith(("{\"", "[{"))): return False, "markup/sql"
    if not any(w[0].islower() or len(w) > 3 for w in words): return False, "technical"
    if re.search(r"\$[a-z_]|\bCOLLATE\b|\bORDER BY\b|\bIN \(|\bLIKE\b|\bJOIN\b|\b[a-z]\.[a-z_]+\s*(=|<|>|IS)", text): return False, "sql"
    if callee and re.search(r"ForDerivedAsset|ForNewAsset|XlsxWriter|Csv\w*|WriteRow|Header\w*Row", callee): return False, "file-content:" + callee
    return True, "text"

# ----------------------------------------------------------------------------- keys

def slug(text, words=6):
    parts = re.findall(r"[A-Za-z0-9]+", text)
    s = "".join(p[:1].upper() + p[1:] for p in parts[:words])
    return (s or "Text")[:48]


def area_of(path):
    name = os.path.splitext(os.path.basename(path))[0]
    for suffix in ("ViewModel", "Service", "Window", "Page", "Dialog"):
        if name.endswith(suffix) and name != suffix: name = name[: -len(suffix)]; break
    if name.endswith(".xaml"): name = name[:-5]
    if name == "Main": return "Main"
    return name


class Table:
    def __init__(self):
        self.en = strings_io.load("en")
        self.by_text = defaultdict(list)
        for k, v in self.en.items(): self.by_text[v].append(k)

    def key(self, text, area):
        for k in self.by_text.get(text, []):
            if k.startswith(area + ".") or k.startswith("Common."): return k
        base = f"{area}.{slug(text)}"
        key, n = base, 2
        while key in self.en and self.en[key] != text: key, n = f"{base}{n}", n + 1
        self.en[key] = text
        self.by_text[text].append(key)
        return key

# ----------------------------------------------------------------------------- conversion

PLURAL = re.compile(r'Plural\.Count\(')


def convert_plurals(src, table):
    """Plural.Count(n, "file"[, "files"]) -> L.Count(n, "Noun.file")."""
    out, i = [], 0
    for m in PLURAL.finditer(src):
        if m.start() < i: continue
        # find args
        j = m.end()
        depth, k = 0, j
        while k < len(src):
            c = src[k]
            if c == '"':
                r = parse_literal(src, k)
                if r: k = r[1]; continue
            if c in "([{": depth += 1
            elif c in ")]}":
                if depth == 0: break
                depth -= 1
            k += 1
        args = src[j:k]
        am = re.fullmatch(r'\s*(.+?)\s*,\s*"([^"\\]+)"\s*(?:,\s*"([^"\\]+)")?\s*', args, re.S)
        if not am:
            continue
        expr, singular, plural = am.group(1), am.group(2), am.group(3) or am.group(2) + "s"
        key = "Noun." + re.sub(r"[^A-Za-z0-9]+", "_", singular).strip("_")
        if table.en.get(key + ".one", "{0} " + singular) != "{0} " + singular:
            key += "_" + slug(plural, 3)
        table.en.setdefault(key + ".one", "{0} " + singular)
        table.en.setdefault(key + ".other", "{0} " + plural)
        out.append(src[i:m.start()])
        out.append(f'L.Count({expr}, "{key}")')
        i = k + 1
    out.append(src[i:])
    return "".join(out)


def merge_concats(src):
    """"a" + "b" (both plain or both interpolated literals, possibly over several lines) -> one literal."""
    changed = True
    while changed:
        changed = False
        lits = literals(src)
        for a, b in zip(lits, lits[1:]):
            between = src[a.end:b.start]
            if not re.fullmatch(r"\s*\+\s*", between): continue
            if a.segments == ["\0RAW"] or b.segments == ["\0RAW"]: continue
            # only merge when nothing binds tighter on either side (e.g. not x + "a" + "b" where the left side matters — that is still fine)
            ta, tb = src[a.start:a.end], src[b.start:b.end]
            if ta.startswith("@") or tb.startswith("@") or ta.startswith("$@") or tb.startswith("$@"): continue
            ia, ib = ta.startswith("$"), tb.startswith("$")
            body_a = ta[(2 if ia else 1):-1]
            body_b = tb[(2 if ib else 1):-1]
            if ia != ib:
                # make both interpolated: escape braces of the plain one
                if not ia: body_a = body_a.replace("{", "{{").replace("}", "}}")
                if not ib: body_b = body_b.replace("{", "{{").replace("}", "}}")
            prefix = "$" if (ia or ib) else ""
            src = src[:a.start] + prefix + '"' + body_a + body_b + '"' + src[b.end:]
            changed = True
            break
    return src


def to_format(lit, convert_expr):
    """Interpolated literal -> (format text, [argument expressions])."""
    fmt, args = [], []
    for s in lit.segments:
        if isinstance(s, str):
            fmt.append(s.replace("{", "{{").replace("}", "}}"))
        else:
            _, expr, spec, _ = s
            fmt.append("{" + str(len(args)) + spec + "}")
            args.append(convert_expr(expr.strip()))
    return "".join(fmt), args


def convert_source(src, path, table, decisions):
    area = area_of(path)
    src = convert_plurals(src, table)
    src = merge_concats(src)

    def convert_expr(expr):
        return convert_range(expr, nested=True)

    def convert_range(code, nested=False):
        out, i = [], 0
        for lit in literals(code):
            if lit.segments == ["\0RAW"]: continue
            text = "".join(s if isinstance(s, str) else "{0}" for s in lit.segments)
            ok, why = classify(code, lit, text, path)
            if decisions is not None and not nested:
                decisions.append((path, ok, why + "|" + enclosing_call(code, lit.start),text.replace("\n", "\\n").replace("\t", " ")[:200]))
            if not ok: continue
            out.append(code[i:lit.start])
            if lit.interpolated and any(not isinstance(s, str) for s in lit.segments):
                fmt, args = to_format(lit, convert_expr)
                key = table.key(fmt, area)
                out.append(f'L.F("{key}", {", ".join(args)})')
            else:
                plain = "".join(lit.segments)
                key = table.key(plain, area)
                out.append(f'L.T("{key}")')
            i = lit.end
        out.append(code[i:])
        return "".join(out)

    return convert_range(src)


def ensure_using(src):
    if "using TibiaFoundry.Core.Localization;" in src or "L.T(" not in src and "L.F(" not in src and "L.Count(" not in src: return src
    m = list(re.finditer(r"^using [^;]+;\r?\n", src, re.M))
    nl = "\r\n" if "\r\n" in src else "\n"
    if m:
        return src[:m[-1].end()] + "using TibiaFoundry.Core.Localization;" + nl + src[m[-1].end():]
    return "using TibiaFoundry.Core.Localization;" + nl + src


WORDS = None
# A line containing this comment is never converted (identity values that are also shown).
KEEP_MARK = "// loc: identity"


def main():
    global WORDS
    args = sys.argv[1:]
    if args and args[0] == "--words": WORDS, args = args[1], args[2:]
    dry = None
    if args and args[0] == "--dry-run": dry, args = args[1], args[2:]
    files = []
    for a in args:
        if os.path.isdir(a):
            for d, _, fs in os.walk(a):
                if os.sep + "bin" in d or os.sep + "obj" in d: continue
                files += [os.path.join(d, f) for f in fs if f.endswith(".cs")]
        else:
            files.append(a)
    table = Table()
    decisions = [] if dry else None
    changed = 0
    for path in sorted(files):
        with open(path, encoding="utf-8-sig", newline="") as f: src = f.read()
        new = ensure_using(convert_source(src, path, table, decisions))
        if new != src:
            changed += 1
            if not dry:
                with open(path, "w", encoding="utf-8", newline="") as f: f.write(new)
    if dry:
        with open(dry, "w", encoding="utf-8") as f:
            for p, ok, why, text in decisions: f.write(f"{os.path.basename(p)}\t{'Y' if ok else '-'}\t{why}\t{text}\n")
        print(f"{sum(1 for d in decisions if d[1])} of {len(decisions)} literals would be converted in {changed} files")
    else:
        strings_io.save("en", table.en)
        print(f"{changed} files changed; {len(table.en)} keys")


if __name__ == "__main__":
    main()
