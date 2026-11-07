# 25 – Interface languages (localization)

Tibia Foundry's interface is available in five languages:

| Language | Code | Number and date formats | Notes |
|---|---|---|---|
| English | `en` | en-US | Default, authoritative text and fallback |
| Português (Brasil) | `pt-BR` | pt-BR | Brazilian Portuguese ("você"), not European Portuguese |
| Polski | `pl` | pl-PL | Full diacritics, three plural forms |
| Español | `es` | es-419 | Neutral Latin American Spanish ("tú") |
| Deutsch | `de` | de-DE | Formal "Sie" |

The language button sits next to the light/dark toggle at the bottom of the navigation bar (a globe and the
current language's short label; in the compact navigation it moves under the toggle). It opens a menu with
each language's flag and native name; the current one has a check mark. The menu works with the keyboard
(Tab to the button, Enter or Space to open, arrow keys and Enter to choose, Escape to close). The choice
applies immediately – the open page, its entries and selections stay as they are – and is saved in the
settings file (`"Language"`). An unknown or missing value starts in English. `--language <code>` on the
command line overrides the setting for one run.

## Architecture

```
TibiaFoundry.Core/Localization/
  Languages.cs          the five languages (code, native name, flag, short label, culture); Find() falls back to English
  L.cs                  the string tables and lookups: T, F, Count, TOr, SetLanguage, LanguageChanged, MissingKeys
  ToolText.cs           translated texts of tool manifests (names, descriptions, settings, operations, dependencies)
  EnumText.cs           translated names of enum values shown in the interface
  LocalizationAudit.cs  completeness checks and the report
  Resources/Strings.{code}.xaml   one table per language (embedded resources)
TibiaFoundry.App/Localization/
  LocalizationManager.cs   Apply(code), LocalizedStrings (indexer for bindings), {loc:Tr}, LocFormat and LocOr converters
  Choice.cs                loc:Choice.Prefix and the LocChoice converter (identifiers shown translated)
  LocalizationReport.cs    --localization-report <file>
TibiaFoundry.App/Themes/Flags.xaml   the five flags as vector images
```

* **One key per text.** Keys are identifiers (`Area.Description`, e.g. `Settings.LogFile`) and never change with
  the language. The English table is the list of every key.
* **Fallback.** A missing translation shows the English text; a key that exists nowhere shows the key itself
  (never an empty element) and is logged once (`L.MissingKeys`).
* **XAML** uses `{loc:Tr Key}` – a binding to `LocalizedStrings.Instance[Key]` that refreshes when the language
  changes, so it also works in DataGrid columns, styles, setters and templates.
* **Code** uses `L.T("Key")`, `L.F("Key", args…)` (placeholders `{0}`, `{1:N0}`… formatted in the language's
  culture) and `L.Count(n, "Key", args…)` for counted phrases. View models refresh their bindings on a language
  switch (`PageViewModel.RefreshLanguage`).
* **Plurals** are separate keys with CLDR suffixes: `Key.one` and `Key.other` (English, Portuguese, Spanish,
  German) and `Key.one`, `Key.few`, `Key.many` (Polish: 1 → one; 2–4, 22–24… → few; 0, 5–21, 25… → many).
  Brazilian Portuguese treats 0 as "one". Counted nouns live under `Noun.*` (`L.Count(3, "Noun.file")` → "3 Dateien").
* **Identifiers stay identifiers.** Program logic never compares translated text. Lists whose items are
  identifiers (sort orders, filters, enum values, settings categories) keep their values and are only *shown*
  translated through `loc:Choice.Prefix="Choice.X"` (items) or `{Binding …, Converter={StaticResource LocChoice},
  ConverterParameter=X}` (single values): the key is `X.` + the item's letters and digits, falling back to the item.
  A line whose literal must stay as it is carries the comment `// loc: identity`.
* **Data-driven texts** (tool manifests, settings of tools) use `L.TOr(key, english)`: the English text written in
  the manifest is the fallback, and every such key is listed in the completeness report.
* **Culture.** The UI culture follows the language; the process culture used for parsing numbers, file names and
  stored values is left unchanged, so files and settings read the same in every language.

## Never translated

* Product, company and site names: Tibia, Tibia Foundry, CipSoft (GmbH), TibiaWiki, Fandom, GitHub, tibiamaps.io.
* Format and technology names: JSON, XML, CSV, PNG, GIF, JPG/JPEG, BMP, WebP, ZIP, DAT, SPR, OTB, RCC, LZMA, SHA-256,
  SHA-1, MD5, API, C#, WPF, MVVM, Qt, .NET.
* File names, folder names, paths, extensions, keys, identifiers, command-line options, settings keys, placeholders.
* Client data: object, creature, outfit, NPC and house names, Tibia's own category and vocation names (they come
  from the client and appear as in the game), the tagline "Tibia Foundry - All your Tibia-Tools for TibiaWiki".
* Text written **into files** that programs read back: the attribution sentence ("Created by … using … for
  TibiaWiki @ …"), attribution history entries, PNG keywords (`Creation Time`), manifest values such as
  `"version file"`, the column names of item-catalog CSV exports (they are imported again), workflow step values.
* Diagnostic report contents and log messages stay in English so that support can read them.

## Glossary

| English | Português (Brasil) | Polski | Español | Deutsch |
|---|---|---|---|---|
| client | cliente | klient | cliente | Client |
| live client | cliente oficial | klient główny | cliente principal | Live-Client |
| test server | servidor de teste | serwer testowy | servidor de pruebas | Testserver |
| sprite | sprite | sprite (pl. sprite'y) | sprite | Sprite |
| sprite sheet | folha de sprites | arkusz sprite'ów | hoja de sprites | Sprite-Sheet |
| appearances | aparências | wyglądy | apariencias | Appearances / Erscheinungsbilder (page) |
| outfit | outfit | strój | outfit | Outfit |
| addon | addon | dodatek | addon | Addon |
| asset | asset | zasób | recurso | Asset |
| Asset Library | Biblioteca de assets | Biblioteka zasobów | Biblioteca de recursos | Asset-Bibliothek |
| Content Studio | Estúdio de conteúdo | Studio treści | Estudio de contenido | Content-Studio |
| item | item | przedmiot | ítem | Item |
| creature | criatura | stwór | criatura | Kreatur |
| missile | projétil | pocisk | proyectil | Geschoss |
| archive (verb / noun) | arquivar / arquivo | archiwizować / archiwum | archivar / archivo | archivieren / Archiv |
| revision | revisão | rewizja | revisión | Revision |
| backup | backup | kopia zapasowa | copia de seguridad | Sicherung |
| log | log | dziennik | registro | Protokoll |
| settings | Configurações | Ustawienia | Configuración | Einstellungen |
| theme | tema | motyw | tema | Design |
| attribution | atribuição | informacja o twórcy | atribución | Urheberangabe |
| export | exportar / exportação | eksport | exportar / exportación | Export / exportieren |
| manifest | manifesto | manifest | manifiesto | Manifest |
| contact sheet | folha de contato | arkusz poglądowy | hoja de contactos | Kontaktabzug |
| nearest neighbor | nearest-neighbor | najbliższy sąsiad | vecino más cercano | Nearest Neighbor |

Spanish uses "tú", Brazilian Portuguese "você", German the formal "Sie"; Polish uses the informal second person.
German compounds with product names are rephrased so that "Tibia Foundry" stays intact ("Logo von Tibia Foundry").

## Adding or changing text

1. Add the English entry to `Strings.en.xaml` (`<sys:String x:Key="Area.Name">Text</sys:String>`; use
   `xml:space="preserve"` when the text starts or ends with spaces or contains line breaks).
2. Use it: `{loc:Tr Area.Name}` in XAML, `L.T("Area.Name")` / `L.F(…)` / `L.Count(…)` in code.
3. Add the same key to the four other tables, or export the missing texts for translators:

   ```
   cd tools/localization
   python translation_chunks.py export <folder>      # writes src_NN.tsv: id, key, English
   python translation_chunks.py merge  <folder>      # reads tr_NN.tsv: id, pt-BR, pl, es, de
   ```

   The merge checks that every translation keeps the English placeholders and applies the English leading and
   trailing spaces. Plural keys need their language's forms (Polish: `.one`, `.few`, `.many`).
4. Check: `TibiaFoundry.exe --localization-report report.md` (coverage, missing keys, placeholder problems,
   missing plural forms, texts equal to English) and the tests in `LocalizationTests`.
5. Look at long texts in German: `TibiaFoundry.exe --ui-snapshot <folder> --language de [--theme dark]` renders
   every page and dialog and runs the window checks.

## Adding a language

1. Add a `LanguageDefinition` to `Languages.All` (code, native name, English name, flag emoji, short label, culture).
2. Add `Strings.<code>.xaml` (copy the English file and translate) – it is embedded automatically.
3. Add the language's plural rule to `L.PluralCategory` and its categories to `LocalizationAudit.Categories`.
4. Add a flag image `Flag.<code>` to `Themes/Flags.xaml`.
5. Add the code to `CODES` in `tools/localization/translation_chunks.py` and `LANGUAGES` in `strings_io.py`.

## Tests

`LocalizationTests`: every language complete with matching placeholders, plural forms and edge spaces; every key
used by XAML (`{loc:Tr}`) and code (`L.T/F/Count`) exists; protected names kept; plural rules; fallback to English;
the language setting saved and invalid values reset; switching the language at run time (text, numbers, plurals).

## Known limitations

* A few texts are built once when their screen or definition is created (file-format names in the metadata
  inspector, metadata field labels, some lists filled when a page opens); after a language switch they change at
  the next start or when the page is opened again. Everything bound through `{loc:Tr}`, `L.T` in view-model
  properties and the `Choice` displays changes immediately.
* Exception messages from the operating system and libraries, and technical self-check item names, are shown as
  they come (usually in English or the Windows language).
* Report-table cells of the file scripts that are also used as identifiers (e.g. comparison statuses "Equal",
  "Modified") stay in English.
* Workflow step choices in Content Studio are stored in workflow files and are shown as stored (English).
* The translations were written for this release without review by native-speaking Tibia players; the glossary
  above is the reference for corrections.
