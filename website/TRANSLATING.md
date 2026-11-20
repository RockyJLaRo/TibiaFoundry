# Translating tibiafoundry.com

The site exists in five languages. Each is a static page built from the same template:

| Language | Code | File | URL |
|---|---|---|---|
| English (source, default) | `en` | `src/locales/en.json` | `https://tibiafoundry.com/` (also `/en/`, which opens `/`) |
| Português (Brasil) | `pt-BR` | `src/locales/pt-BR.json` | `/pt-br/` |
| Polski | `pl` | `src/locales/pl.json` | `/pl/` |
| Español | `es` | `src/locales/es.json` | `/es/` |
| Deutsch | `de` | `src/locales/de.json` | `/de/` |

## How it works

- `src/templates/*.html` (one per page, inside `layout.html`) contain no visible text, only keys: `{{t:hero.lead}}` (HTML text) and `{{a:hero.zoom}}` (an attribute: markup is removed and quotes escaped). The brand line "Tibia Foundry - All your Tibia-Tools for TibiaWiki" is the program's exact tagline and stays as it is in every language.
- `build_site.py` renders the template once per language. A key missing in a language shows the **English** text. The build lists every missing key, and `--strict` makes the build fail on one.
- The build **fails** if a translation changes the markup (`<b>`, `<span class="accent">`, `<code>`), the content of a `<code>` element or a placeholder. This keeps file names, templates, links and code identical in every language.
- Texts that the scripts need at run time (the `js.*` keys: Tibia data views, errors, tables, news) are embedded in each page as `window.TF_I18N`. In `src/js/*.js`, call `TF.t('js.key', { n: 3 })`; runtime placeholders such as `{n}`, `{date}`, `{time}`, `{world}` are filled by the script and must be kept exactly. Numbers and dates are formatted with `Intl` in the page's language. A missing key falls back to English and logs a warning when the page runs on `localhost` or from disk.
- `website/i18n-report.md` is the completeness report and is rewritten by every build.

## Placeholders in texts

| Placeholder | Becomes |
|---|---|
| `{version}` | the program version, e.g. 8.8.0 |
| `{date}` | the build date in the language's own style (`1. November 2026`, `1 de novembro de 2026`, …) |
| `{num:43000}` | the number grouped the language's way (43,000 · 43.000 · 43 000) |
| `{plural:tools:23}` | the matching form of `plural.tools` for 23, with `{count}` replaced |
| `{page}` | the program page name in screenshot descriptions (the program's own translation) |
| `{app:Tool.asset-library.Name}` | that text of the **program**, in this language (from `src/TibiaFoundry.Core/Localization/Resources/Strings.<code>.xaml`) – e.g. *Asset-Bibliothek*, *Biblioteka zasobów*. Optional: where grammar needs another form, write the name yourself |

Plurals use the CLDR categories: `one` and `other` for English, Portuguese, Spanish and German, and `one`, `few` and `many` for Polish (1 narzędzie, 23 narzędzia, 25 narzędzi). For example:

```json
"plural": { "tools": { "one": "{count} narzędzie", "few": "{count} narzędzia", "many": "{count} narzędzi", "other": "{count} narzędzia" } }
```

## Names of the program's pages and tools

The program is translated too, and every screenshot shows it in the page's language. So the site must use the
**program's own words** for its pages, tabs and buttons (Asset Library, Content Studio, Appearances, Settings →
Attribution, Content Studio → New project, Wiki infobox…): write them as `{app:Key}` (keys: `Tool.<id>.Name`,
`Infobox.Tab`, `Choice.SettingsCategory.Attribution`, `ContentProjects.NewProject`, `Appearances.Outfitter`, …) and
the build inserts the program's translation. The screenshot gallery's tab labels come from the program directly
(`SCREENS` in `build_site.py`). Where a sentence needs an inflected form, put the name in quotes after a noun
(„Seite „{app:…}“", „strona „{app:…}”") or write it yourself.

## What stays in English
- Proper names and terms: Tibia, CipSoft GmbH, TibiaWiki, Fandom, Fansite Programme, RockyLaRo, GitHub, and creature names in alt text (Demon, Behemoth, Hydra).
- Technical content: file names (`dragon-lord.gif`, `index.html`, `THIRD-PARTY-NOTICES.txt`), name templates (`{category}`, `{name}_{scale}x`), wiki and HTML code (`[[File:…]]`, `<img>`), formats (PNG, GIF, JSON, CSV, Markdown, ZIP, SQLite, BBCode, .msi), library names and URLs.
- Community terms that Tibia players use untranslated in each language: outfit, addon, sprite, NPC, quest, fansite. German also keeps Mount; the others use the local word (montaria, montura).

Keys that are intentionally identical to English go in the language's `_meta.sameAsEnglish` list, so the report does not flag them.

## Changing or adding a text

1. Add or change the key in `src/locales/en.json`, then use it in the template (`{{t:section.key}}`).
2. Add the translation to the other four files. Until you do, the page shows English and the build lists the key.
3. Build and check the result:

```bash
python -I website/build_site.py --strict
```

## Adding a language

1. Copy `src/locales/en.json` to `src/locales/<code>.json` and translate it, including `lang.button` and the `js.suggest*` lines. These are written in the new language because they are offered to visitors whose browser prefers it.
2. Add a line to `LANGUAGES` in `build_site.py`: the code, the URL folder, the language's own name, a two-letter badge and the Open Graph locale. If the language groups digits or writes dates differently, also add it to `MONTHS`, `GROUP` and `format_date`.
3. If its plural rules differ from "one/other", add them to `plural_form`.
4. Build. The new page, its hreflang links, the sitemap entry and the link-preview image are generated automatically.

## Review status

The four translations were written by Claude (an AI). They have not yet been reviewed by native speakers. Please ask fluent Tibia players to proofread them, especially the hero texts, the FAQ and the legal notice.

## Checks (9.0.5)

- `python -I website/tests/check_i18n.py` – every language has every English key, no empty texts, no unknown or
  duplicate keys, the same markup and placeholders as English, every data-provider credit keeps its `{link}`, and texts
  identical to English are listed in `_meta.sameAsEnglish` (names and official terms: Highscores, Premium, vocation names,
  the section title *Tibia News & Updates*...).
- English only: page titles, menu items, section headings, tool names and buttons are in Title Case (small words such as
  *and*, *of*, *the* stay lowercase unless first or last). `check_i18n.py` checks it (`--no-title-case` skips it); other
  languages keep their own capitalisation.
- `python -I website/build_site.py --strict` fails on a missing translation or an unused text.
- Official Tibia terms are never translated: character, world, guild, creature and boss names, spell formulas
  (`exura`), API data (vocations, PvP types, news headlines - marked `lang="en"` on the page).

