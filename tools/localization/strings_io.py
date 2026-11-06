"""Reads and writes the string tables (src/TibiaFoundry.Core/Localization/Resources/Strings.{code}.xaml)."""
import os, re
from collections import OrderedDict
from xml.sax.saxutils import escape
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RESOURCES = os.path.join(ROOT, "src", "TibiaFoundry.Core", "Localization", "Resources")
LANGUAGES = ["en", "pt-BR", "pl", "es", "de"]
NAMES = {"en": "English (the authoritative text and fallback)", "pt-BR": "Brazilian Portuguese", "pl": "Polish", "es": "Spanish (neutral, Latin American)", "de": "German"}
X = "{http://schemas.microsoft.com/winfx/2006/xaml}"


def path(code):
    return os.path.join(RESOURCES, f"Strings.{code}.xaml")


def load(code):
    table = OrderedDict()
    if not os.path.exists(path(code)): return table
    for el in ET.parse(path(code)).getroot():
        key = el.get(X + "Key")
        if key is not None: table[key] = el.text or ""
    return table


def save(code, table, order=None):
    """Writes a table; keys follow `order` (the English key order) when given, extra keys last."""
    keys = list(order) if order is not None else list(table)
    # Entries of one area together (areas in order of first appearance, entries in their order).
    first = {}
    for k in keys: first.setdefault(k.split(".")[0], len(first))
    keys = sorted(keys, key=lambda k: first[k.split(".")[0]])
    keys = [k for k in keys if k in table] + [k for k in table if order is not None and k not in order]
    lines = [
        "<!--",
        f"    Tibia Foundry interface text: {NAMES.get(code, code)}.",
        "    One entry per key; keys are identifiers and are the same in every language. Placeholders {0}, {1}…",
        "    must be kept. Plural forms use the suffixes .one/.few/.many/.other. See docs/Localization.md.",
        "-->",
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
        '                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">',
    ]
    area = None
    for k in keys:
        a = k.split(".")[0]
        if a != area:
            lines.append(f"    <!-- {a} -->")
            area = a
        text = escape(table[k]).replace("\n", "&#10;").replace("\r", "&#13;")
        raw = table[k]
        keep = ' xml:space="preserve"' if raw != raw.strip() or "\n" in raw or "  " in raw else ""
        lines.append(f'    <sys:String x:Key="{k}"{keep}>{text}</sys:String>')
    lines.append("</ResourceDictionary>")
    os.makedirs(RESOURCES, exist_ok=True)
    with open(path(code), "w", encoding="utf-8", newline="\r\n") as f:
        f.write("\n".join(lines) + "\n")


def placeholders(text):
    return sorted(set(re.findall(r"\{(\d+)(?:[,:][^}]*)?\}", text)))
