"""Adds the data-driven texts listed in a completeness report (tool manifests) to the English table.

Usage: TibiaFoundry.exe --localization-report report.md, then python add_data_keys.py report.md
"""
import re, sys
import strings_io

en = strings_io.load("en")
section = False
added = 0
for line in open(sys.argv[1], encoding="utf-8"):
    if line.startswith("Data-driven texts"): section = True; continue
    if section and line.startswith("## "): break
    m = re.match(r"- `([^`]+)`: (.*)$", line.rstrip("\n"))
    if section and m and m.group(1) not in en:
        en[m.group(1)] = m.group(2)
        added += 1
strings_io.save("en", en)
print(f"{added} keys added")
