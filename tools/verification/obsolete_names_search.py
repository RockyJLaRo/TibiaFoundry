# Searches the whole repository (every file, text and binary, and the entries of zip archives) for the names removed
# in 6.0 and writes a report of where they remain and why (docs/13_Branding_and_Attribution.md section 10). The report
# names files, lines and counts, not the matched text, so it does not contain the names itself. Run from the
# repository root:
#   python -I tools/verification/obsolete_names_search.py . 05_verification/7.0.1/obsolete_names_search.txt
import os, re, sys, zipfile, collections

root, report = os.path.abspath(sys.argv[1]), sys.argv[2]
# Any capitalization, with spaces, hyphens, underscores, dots or plus signs between the parts (same as BrandingTests).
NAME = re.compile(rb"tibia[\s_\-.]*wiki[\s_\-.]*tools|lee[\s_\-.+]*kun", re.IGNORECASE)
# .NET string literals are stored as UTF-16 in compiled programs.
NAME16 = [re.compile(re.escape(s.encode("utf-16-le")), re.IGNORECASE) for s in ("TibiaWiki" + "Tools", "Lee" + " kun", "Lee" + "kun")]
user_folder = os.path.basename(os.path.dirname(root)).encode()
user_folder16 = user_folder.decode().encode("utf-16-le")

REASONS = [
    ("00_original_backup", "the original program, preserved byte for byte (its file names, assembly, namespace and its author's notice in its own metadata)"),
    ("01_decompiled", "the decompiled original program (its namespace and assembly name)"),
    ("02_analysis", "analysis of the original program: file hashes, UI tree, recovered schemas"),
    ("03_sandbox_original", "the sandbox copy of the original program"),
    ("04_reference", "outputs and logs of the original program, used as test references"),
    (os.path.join("05_verification", "7.0.1"), None),
    (os.path.join("05_verification", "item_discovery_research_2026-10-15"), None),
    ("05_verification", "recorded evidence of earlier versions (changing it would falsify the records)"),
    ("06_baseline_2.0.0", "the preserved 2.0 release (checksummed)"),
    ("07_baseline_2.1.0", "the preserved 2.1 release (checksummed)"),
    ("08_baseline_3.0.0", "the preserved 3.0 release (checksummed)"),
    ("09_baseline_4.0.0", "the preserved 4.0 release (checksummed)"),
    ("10_baseline_5.0.0", "the preserved 5.0 release (checksummed)"),
    ("11_baseline_6.0.0", "the preserved 6.0 release (checksummed)"),
    ("12_baseline_7.0.1", "the preserved 7.0.1 release (checksummed)"),
    ("13_baseline_7.1.0", "the preserved 7.1.0 release (checksummed)"),
    ("14_baseline_7.2.0", "the preserved 7.2.0 release (checksummed)"),
    ("15_baseline_7.3.0", "the preserved 7.3.0 release (checksummed)"),
    (os.path.join("tools", "ParityHarness"), "compiles against the original program's assembly, so it uses its namespace and file name"),
]
SPECIFIC = {
    os.path.join("src", "TibiaFoundry.Core", "Common", "AppIdentity.cs"): "AppIdentity.Legacy.Version2Id: the folder and file name version 2.x wrote; needed to find and migrate its settings, portable data and sprite folders (read only, never shown)",
    os.path.join("src", "TibiaFoundry.Tests", "BrandingTests.cs"): "the search patterns of the test that enforces the removal",
}
COMPILED = "compiled program: contains the identifier AppIdentity.Legacy.Version2Id as data (the constant is compiled into every assembly that uses it)"
BUILD_OUTPUT = ("build output (bin/obj, recreated by every build, not part of the source or the release): absolute paths of a "
                "temporary working folder whose name the tooling derived from the repository's folder name")


def preserved_reason(rel):
    for prefix, why in REASONS:
        if rel == prefix or rel.startswith(prefix + os.sep):
            return prefix, why
    return None, None


def own_reason(rel, binary):
    if rel in SPECIFIC:
        return SPECIFIC[rel]
    name = os.path.basename(rel)
    if binary and name.startswith("TibiaFoundry") and name.endswith((".exe", ".dll")):
        return COMPILED
    if rel.startswith("src" + os.sep) and ({"bin", "obj"} & set(rel.split(os.sep))):
        return BUILD_OUTPUT
    return None


def blobs(path, rel, data):
    """The file itself, and for a zip archive each entry as "<zip>!<entry>"."""
    yield rel, None, data
    if rel.lower().endswith(".zip"):
        try:
            with zipfile.ZipFile(path) as z:
                for info in z.infolist():
                    if info.file_size < 200 * 1024 * 1024:
                        yield rel + "!" + info.filename, info.filename.replace("/", os.sep), z.read(info)
        except (zipfile.BadZipFile, OSError):
            pass


def count(blob):
    hits, lines = 0, []
    for m in NAME.finditer(blob):
        if blob[m.start():m.start() + len(user_folder)].lower() == user_folder.lower():
            continue  # the user's own folder name in an absolute path
        hits += 1
        lines.append(blob.count(b"\n", 0, m.start()) + 1)
    for p in NAME16:
        for m in p.finditer(blob):
            if blob[m.start():m.start() + len(user_folder16)].lower() == user_folder16.lower():
                continue
            hits += 1
    return hits, lines


by_area = collections.Counter()
listed, unexplained, scanned = [], [], 0
for dirpath, dirnames, filenames in os.walk(root):
    dirnames[:] = [d for d in dirnames if d not in (".vs", "__pycache__")]
    for name in filenames:
        path = os.path.join(dirpath, name)
        rel = os.path.relpath(path, root)
        scanned += 1
        try:
            data = open(path, "rb").read()
        except OSError as e:
            unexplained.append(f"{rel}: could not be read ({e})")
            continue
        prefix, preserved_why = preserved_reason(rel)
        for vrel, entry, blob in blobs(path, rel, data):
            hits, lines = count(blob)
            if hits == 0:
                continue
            binary = b"\0" in blob[:8192]
            where = f"{vrel}: {hits} match(es)" + (f" (lines {', '.join(map(str, sorted(set(lines))[:20]))})" if lines and not binary else "")
            if preserved_why:
                if entry is None:
                    by_area[(prefix, preserved_why)] += hits
                continue
            # Outside the preserved folders (and everything in the 7.0.1 evidence): every match needs its own reason.
            # A zip's own bytes are compressed; its matches are judged by its entries.
            if entry is None and rel.lower().endswith(".zip"):
                continue
            why = own_reason(entry if entry is not None else rel, binary)
            if why is None:
                unexplained.append(where)
            else:
                listed.append(f"{where} - {why}")

out = [
    "Repository-wide search for the names removed in 6.0 (docs/13_Branding_and_Attribution.md section 10)",
    f"Root: <repository>   Files searched: {scanned:,} (all files, text and binary, and the entries of zip archives)",
    "Pattern: the earlier product name and the original developer's name, any capitalization, with spaces, hyphens,",
    "underscores, dots or plus signs between the parts; also as UTF-16 (compiled programs). The repository's parent",
    "folder name in absolute paths is the user's own folder name and is not counted.",
    "",
    "== Matches outside the preserved folders (each must stay) ==",
    *(listed or ["(none)"]),
    "",
    "== Matches inside preserved folders (counts per folder; zip entries not counted separately) ==",
    *[f"{top}: {n:,} match(es) - {why}" for (top, why), n in sorted(by_area.items())],
    "",
    "== Matches that are not explained (must be empty) ==",
    *(unexplained or ["(none)"]),
]
os.makedirs(os.path.dirname(os.path.abspath(report)), exist_ok=True)
open(report, "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
print(f"files: {scanned:,}; outside preserved folders: {len(listed)}; unexplained: {len(unexplained)}")
for line in unexplained[:50]:
    print("UNEXPLAINED", line)
