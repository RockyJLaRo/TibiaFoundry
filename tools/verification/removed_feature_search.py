# Searches the current source, tools, docs and website text for identifiers of Item Discovery, the tool removed in 8.0
# (docs/21_Client_Archiving_and_Rebuilder.md, "Item Discovery removed"), and writes where they remain and why. Build
# output, baselines, recorded evidence and the CHANGELOG (history) are not searched. Run from the repository root:
#   python -I tools/verification/removed_feature_search.py . 05_verification/8.0.0/removed_feature_search.txt
import os, re, sys

root, report = os.path.abspath(sys.argv[1]), sys.argv[2]
IDENTIFIERS = [
    "Item Discovery", "ItemDiscovery", "item-discovery", "item_discovery", "Core.Discovery", "Services.Discovery", "DiscoveryFolder",
    "DiscoverySession", "DiscoveryCollector", "DiscoveryBatch", "ObsWebSocket", "ObsCapture", "PointerMover", "AutomaticHover",
    "GlobalHotkey", "WindowsOcr", "ScreenReader", "UiAutomationProbe", "ClientScreenshotWatcher", "PromptSecret", "ShowSecret",
    "ClientOptionsDocument", "captureSource", "hoverDwellMs", "LocationIds.ClientOptions", "LocationIds.Screenshots", "19_Item_Discovery",
]
PATTERN = re.compile("|".join(re.escape(i) for i in IDENTIFIERS))
SEARCHED = ["src", "tools", "docs", "website", "README.md"]
TEXT = (".cs", ".xaml", ".csproj", ".props", ".slnx", ".json", ".py", ".ps1", ".md", ".txt", ".pubxml", ".xml", ".html", ".css", ".js")
# Files where every match is expected, and why (path relative to the root, then the reason).
EXPLAINED = {
    os.path.join("src", "TibiaFoundry.Core", "Items", "ItemModel.cs"):
        "legacy record kinds and methods kept so catalogs with records Item Discovery collected still load (documented as removed)",
    os.path.join("src", "TibiaFoundry.Core", "Settings", "AppSettings.cs"):
        "settings format 7 removes Item Discovery's settings: the prefix and page name it removes, and the note it shows",
    os.path.join("src", "TibiaFoundry.App", "Services", "LeftoverHotkeyBackups.cs"):
        "finds copies of the client's options file that Item Discovery left behind (decision D7), only to report them",
    os.path.join("src", "TibiaFoundry.Tests", "RemovedFeatureTests.cs"):
        "the tests that prove the removal (names of removed types, settings and the leftover backup notice)",
    os.path.join("tools", "verification", "removed_feature_search.py"):
        "this search's own list of identifiers",
    os.path.join("tools", "verification", "mutation_checks.py"):
        "the header lists the reverse checks retired with Item Discovery",
    os.path.join("tools", "verification", "obsolete_names_search.py"):
        "names the preserved research folder item_discovery_research_2026-10-15 (recorded evidence)",
    os.path.join("docs", "07_Testing_Report.md"):
        "the testing report records every release's results as they were (history)",
}
# Other Markdown, text and Python files: a matching line is explained when it says the tool was removed or withdrawn, or is a
# release-history row (the release notes of README-Portable.txt, a comment of make_notices.py).
LINE_OK = re.compile(r"remov|withdrawn|^\| 7\.\d\.\d \|", re.IGNORECASE)


def listed(numbers):
    return f"{len(numbers)} line(s) ({', '.join(map(str, numbers[:15]))}{', ...' if len(numbers) > 15 else ''})"


explained, unexplained, scanned = [], [], 0
for top in SEARCHED:
    start = os.path.join(root, top)
    if os.path.isfile(start):
        paths = [start]
    else:
        paths = [os.path.join(d, f) for d, ds, fs in os.walk(start) for f in fs
                 if not ({"bin", "obj", "__pycache__", ".vs"} & set(os.path.relpath(d, root).split(os.sep)))]
    for path in sorted(paths):
        if not path.lower().endswith(TEXT):
            continue
        rel = os.path.relpath(path, root)
        scanned += 1
        text = open(path, encoding="utf-8", errors="replace").read()
        hits = sorted({text.count("\n", 0, m.start()) + 1 for m in PATTERN.finditer(text)})
        if not hits:
            continue
        lines = text.split("\n")
        why = EXPLAINED.get(rel)
        open_lines = [] if why else [n for n in hits if not (rel.endswith((".md", ".txt", ".py")) and LINE_OK.search(lines[n - 1]))]
        if open_lines:
            unexplained.append(f"{rel}: {listed(open_lines)}")
        else:
            explained.append(f"{rel}: {listed(hits)} - {why or 'each line says the tool was removed in 8.0, or is a release-history row'}")

out = [f"Identifiers of the removed Item Discovery tool in {', '.join(SEARCHED)} ({scanned} text files searched).", "",
       "Explained:"] + [f"  {e}" for e in explained] + ["", "Not explained (must be empty):"] + [f"  {u}" for u in unexplained] + \
      ["", f"Result: {len(unexplained)} not explained."]
os.makedirs(os.path.dirname(os.path.abspath(report)), exist_ok=True)
open(report, "w", encoding="utf-8").write("\n".join(out) + "\n")
print("\n".join(out[-3:]))
sys.exit(1 if unexplained else 0)
