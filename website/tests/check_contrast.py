"""WCAG 2.2 AA contrast check of the Gilded Inlay design tokens.

Parses the light (:root) and dark (:root[data-theme="dark"]) token blocks of src/css/tokens.css, checks that the
prefers-color-scheme dark block carries the same values, then asserts the contrast ratio of the colour pairs below.
Prints every pair with its ratio; exits 1 and lists the failing pairs when any pair is too low.

Run: python -I website/tests/check_contrast.py
"""
import re
import sys
from pathlib import Path

TOKENS = Path(__file__).resolve().parent.parent / "src" / "css" / "tokens.css"

# (foreground token, background token, minimum ratio, what it is)
PAIRS = [
    ("text-primary", "bg-canvas", 4.5, "body text"),
    ("text-primary", "bg-surface", 4.5, "text on cards"),
    ("text-primary", "bg-elevated", 4.5, "text on raised panels"),
    ("text-secondary", "bg-canvas", 4.5, "secondary text"),
    ("text-secondary", "bg-surface", 4.5, "secondary text on cards"),
    ("text-muted", "bg-canvas", 4.5, "muted text"),
    ("accent-on", "accent-primary", 4.5, "text on the primary button"),
    ("accent-primary", "bg-canvas", 4.5, "link text"),
    ("accent-primary", "bg-surface", 4.5, "link text on cards"),
    ("border-strong", "bg-canvas", 3.0, "input and button borders"),
    ("border-strong", "bg-surface", 3.0, "input and button borders on cards"),
    ("accent-primary", "bg-surface", 3.0, "focus ring"),
    ("status-success", "bg-surface", 4.5, "success text"),
    ("status-warning", "bg-surface", 4.5, "warning text"),
    ("status-danger", "bg-surface", 4.5, "danger text"),
]


def block(css, selector):
    m = re.search(re.escape(selector) + r"\s*\{(.*?)\}", css, re.S)
    if not m:
        raise SystemExit(f"tokens.css: no block for {selector!r}")
    return dict(re.findall(r"--([a-z0-9-]+)\s*:\s*([^;]+?)\s*;", m.group(1)))


def media_dark_block(css):
    m = re.search(r"@media \(prefers-color-scheme: dark\)\s*\{\s*:root:not\(\[data-theme=\"light\"\]\)\s*\{(.*?)\}", css, re.S)
    if not m:
        raise SystemExit("tokens.css: no prefers-color-scheme dark block")
    return dict(re.findall(r"--([a-z0-9-]+)\s*:\s*([^;]+?)\s*;", m.group(1)))


def lum(hexv):
    h = hexv.lstrip("#")
    if len(h) == 3:
        h = "".join(ch * 2 for ch in h)
    chans = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    lin = [c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4 for c in chans]
    return 0.2126 * lin[0] + 0.7152 * lin[1] + 0.0722 * lin[2]


def ratio(a, b):
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)


def main():
    css = TOKENS.read_text(encoding="utf-8")
    light = block(css, ":root")
    dark = block(css, ':root[data-theme="dark"]')
    failures = []
    media = media_dark_block(css)
    for k, v in dark.items():
        if media.get(k) != v:
            failures.append(f"dark media block differs from [data-theme=dark] for --{k}: {media.get(k)!r} vs {v!r}")
    for name, theme in (("light", light), ("dark", dark)):
        print(f"\n{name} theme")
        for fg, bg, minimum, what in PAIRS:
            try:
                f, b = theme[fg], theme[bg]
                if not (re.fullmatch(r"#[0-9a-fA-F]{3,6}", f) and re.fullmatch(r"#[0-9a-fA-F]{3,6}", b)):
                    raise KeyError(f"non-hex value ({f}, {b})")
            except KeyError as e:
                failures.append(f"{name}: --{fg} on --{bg} ({what}): token missing or not a hex colour: {e}")
                print(f"  MISSING {fg:15} on {bg:12} {what}")
                continue
            r = ratio(f, b)
            ok = r >= minimum
            print(f"  {'ok  ' if ok else 'FAIL'}    {fg:15} {f} on {bg:12} {b}  {r:5.2f}  (>= {minimum})  {what}")
            if not ok:
                failures.append(f"{name}: --{fg} {f} on --{bg} {b} = {r:.2f}, needs {minimum} ({what})")
    if failures:
        print("\nFAILING:")
        for line in failures:
            print("  " + line)
        sys.exit(1)
    print("\nAll contrast pairs pass.")


if __name__ == "__main__":
    main()
