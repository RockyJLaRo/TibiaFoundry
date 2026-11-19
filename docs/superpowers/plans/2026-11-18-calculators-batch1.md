# Calculators – batch 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Calculators section to tibiafoundry.com with verified Experience & level (+ share range), Stamina,
Blessings and Character stats calculators and an extended "Today in Tibia" panel (server save, Rashid).

**Architecture:** Each calculator is a real page in `src/registry.json` (`calculators/<slug>/`, 5 languages) built by
the existing `build_site.py`. Pure formula functions live in `src/js/formulas.js` (browser + Node); their sources,
dates and test values live in `src/formulas.json`, which drives the unit tests, the strict-build check and the e2e
check. `src/js/calc.js` binds any calculator template (inputs `name=…`, outputs `data-out=…`) to its formula.

**Tech Stack:** Python 3 static builder, plain browser JS (UMD like `tibiadata.js`), Node `node:test`, Playwright
(venv `F:\Users\Rocky\Desktop\TibiaFoundry_Master\Work_9.0.5\.venv-qa`), Lucide 0.460.0 icons.

**Spec:** `docs/superpowers/specs/2026-11-18-calculators-design.md`

## Global Constraints

- Work in worktree `F:\Users\Rocky\Desktop\TibiaFoundry_Master\Work_Redesign\repo` on branch `calculators` (from `website-redesign`). Never push, deploy, or commit to `source`/`main` without the owner's approval.
- Never modify `games/`, `pokemon/`, `tools/`, `.github/`, `CNAME`, `robots.txt`.
- **A calculator ships only when every part passes** (every vocation, every output). If any formula of a calculator cannot be verified, remove that calculator's page from the registry and report it – do not ship a partial tool.
- **Verification:** a formula needs a published rule (official CipSoft source first, else TibiaWiki EN) **plus at least one independent value** (official example, published table, or two established calculators that agree, captured like `Work_Redesign\research\calc-tests\`). Values computed from the implemented rule do not count as independent. No in-game checks. ≥3 test values per formula.
- Never fabricate a source, value, date or test result. If a source is unreachable or behind a CAPTCHA, do not bypass it; record it as unavailable.
- Number input follows the **page language** (`GROUP` map in `build_site.py`: en `,` pt-BR `.` pl ` ` es ` ` de `.`); shorthand `k`=10³, `kk`=10⁶, `kkk`=10⁹; level/skill fields integers only; each parsed value echoed ("read as …"); unparseable input → inline error, never a guess.
- No frameworks, no external resources (`check_page` forbids them), Lucide icons only, existing CSS tokens only, 360 px without horizontal scroll, 44 px touch targets, results in an `aria-live="polite"` region.
- 5 languages complete (`tests/check_i18n.py` must pass); every new user-visible text is a locale key.
- Share links contain calculator inputs only. No `localStorage` keys are added in batch 1.
- Builds for release use `--checksums F:\Users\Rocky\Desktop\TibiaFoundry_Master\Work_9.0.5\release\Release\9.0.5\SHA256SUMS.txt`.

## Review Focus

1. **Wrong-locale number typed** (e.g. `1.500.000` on the English page) → inline error, not 1.5 or a silent 1000× value. Pinned in Task 1 (`parseNumber` tests).
2. **Server save / Rashid at DST changes and at exactly 10:00 Berlin** → correct countdown and town on 29 Mar 2026, 25 Oct 2026, 09:59:59 vs 10:00:00. Pinned in Task 7.
3. **Edge levels** (level 1, level 8 vocation start, very high levels such as 2,000) → exact integers, no float drift. Pinned in Tasks 2 and 6 (`Number.isSafeInteger` assertions).
4. **Target ≤ current** (level, stamina) → plain inline error, results cleared, no negative times. Pinned in Tasks 3 and 4.
5. **Opening a share link in another language** (`/de/calculators/experience/?from=100&to=200`) → same results, formatted for German. Pinned in Task 3 e2e.

---

### Task 1: Formula registry, number parsing and strict-build check

**Files:**
- Create: `website/src/formulas.json`, `website/src/js/formulas.js`, `website/tests/unit/formulas.test.mjs`
- Modify: `website/build_site.py` (new `check_formulas()` called from `main()` when `--strict`)

**Interfaces:**
- Produces (`formulas.js`, UMD: `window.TFFormulas` / `module.exports`):
  - `parseNumber(text: string, lang: "en"|"pt-BR"|"pl"|"es"|"de", opts?: {integer?: boolean}) -> {value: number} | {error: "empty"|"format"|"integer"|"range"}`
  - `parseDuration(text: string) -> {value: number /* minutes */} | {error: "format"}` (accepts `h:mm`, `hh:mm`)
  - `FORMULAS: {[id: string]: (input: object) -> object}` – filled by Tasks 2, 4, 5, 6, 7.
- Produces (`formulas.json`): `{"formulas": [{"id", "calc", "rule", "sources": [{"url", "kind": "official"|"wiki"|"calculator", "read": "YYYY-MM-DD", "note"}], "tibiaVersion", "checkedOn", "tests": [{"in": {}, "out": {}, "from": "official"|"wiki"|"calculator:<name>"|"computed"}]}]}`

- [ ] **Step 1:** Create branch: `git -C <worktree> switch -c calculators website-redesign`; commit the spec and this plan.
- [ ] **Step 2: Write failing tests** in `formulas.test.mjs` (load like `tibiadata.test.mjs` via `createRequire`):
  - `parseNumber("1,500,000","en")` → `{value:1500000}`; `parseNumber("1.500.000","de")` → `{value:1500000}`; `parseNumber("1 500 000","pl")` → `{value:1500000}`
  - `parseNumber("1.5kk","en")` → `1500000`; `parseNumber("1,5kk","de")` → `1500000`; `parseNumber("250k","es")` → `250000`; `parseNumber("2kkk","en")` → `2000000000`
  - `parseNumber("1.500.000","en")` → `{error:"format"}`; `parseNumber("1,5","en")` → `{error:"format"}`; `parseNumber("","en")` → `{error:"empty"}`; `parseNumber("12.5","en",{integer:true})` → `{error:"integer"}`
  - `parseDuration("39:00")` → `2340`; `parseDuration("0:05")` → `5`; `parseDuration("39")` → `{error:"format"}`
  - Registry test (data-driven): for every entry in `formulas.json`, for every test, `assert.deepEqual(FORMULAS[id](t.in), t.out)`; every entry has ≥3 tests, ≥1 test whose `from` ≠ `computed`, ≥1 source, `checkedOn` matching `^\d{4}-\d{2}-\d{2}$`.
- [ ] **Step 3:** Run `node --test website/tests/unit/formulas.test.mjs` → FAIL (module missing).
- [ ] **Step 4:** Implement `parseNumber`, `parseDuration` and an empty `FORMULAS` in `formulas.js`; `formulas.json` = `{"formulas": []}`. Group separator per language from the same values as `GROUP` in `build_site.py`; decimal separator is `.` for en, `,` otherwise. A group separator must be followed by exactly 3 digits; anything else is `format`.
- [ ] **Step 5:** Add `check_formulas()` to `build_site.py`: for each page with a `calc` key, every id in `page["calc"]` exists in `formulas.json` and meets the rules of Step 2; exit with a clear message otherwise. Run in `main()` before rendering.
- [ ] **Step 6:** `node --test website/tests/unit/formulas.test.mjs` → PASS; `python -I website/build_site.py --strict` → OK.
- [ ] **Step 7:** Commit `feat(calc): formula registry, number parsing, strict check`.

### Task 2: Experience and share-range formulas (verified)

**Files:** Modify `website/src/js/formulas.js`, `website/src/formulas.json`. Research captures go to `Work_Redesign\research\calc-tests\` (outside the repo).

**Interfaces:**
- Produces: `FORMULAS.levelExp({level:int}) -> {exp:int}`; `FORMULAS.levelPlan({from:int, to:int, exph:int, hpd:int}) -> {exp:int, minutes:int, days:int}` (`minutes` = floor(exp/exph·60); `days` = ceil(minutes/(hpd·60))); `FORMULAS.shareRange({level:int}) -> {min:int, max:int}`.

- [ ] **Step 1: Verify sources.** Rule `exp(L) = 50/3·(L³ − 6L² + 17L − 12)` (TibiaWiki "Experience Formula"; record URL + read date). Independent values: 100→200 = 113,695,000 and 75 h 47 min at 1.5kk/h (TibiaRoute, already captured `level-tibiaroute.json`); plus values from TibiaWiki's experience table (e.g. levels 2, 8, 100). Share range: official rule (Tibia.com manual / TibiaWiki "Shared Experience": highest level ≤ 3/2 of lowest) plus one independent value (TibiaPal or NabBot). If share range cannot get an independent value, the whole Experience calculator waits (report and stop the task).
- [ ] **Step 2:** Add the entries with ≥3 tests each to `formulas.json`, including `levelExp({level:1}) → {exp:0}`, `levelExp({level:2000})` (exact integer, `Number.isSafeInteger`), `levelPlan({from:100,to:200,exph:1500000,hpd:24}) → {exp:113695000, minutes:4547, days:4}`, `shareRange({level:100}) → {min:67, max:150}`.
- [ ] **Step 3:** Run unit tests → FAIL (functions missing). **Step 4:** Implement with integer arithmetic (`(50 * (L**3 - 6*L**2 + 17*L - 12)) / 3`, exact). **Step 5:** Unit tests → PASS. **Step 6:** Commit `feat(calc): experience and share range formulas with sources`.

### Task 3: Calculator pages, hub and the Experience page

**Files:**
- Create: `website/src/js/calc.js`, `website/src/templates/calculators.html`, `website/src/templates/calc-experience.html`
- Modify: `website/src/registry.json` (pages), `website/build_site.py` (`render()` scripts and runtime strings), `website/src/templates/home.html` (Calculators destination card), `website/src/styles.css` (calculator components), `website/art/icons.py` + `website/art/lucide/` (`calculator`, `trending-up`, `clock`, `battery-medium`, `heart-pulse`, `sparkles` from Lucide 0.460.0, ISC), all 5 `src/locales/*.json` (`nav.calculators`, `calc.*`), `website/tests/e2e.py` (new check)

**Interfaces:**
- Consumes: `TFFormulas.parseNumber`, `parseDuration`, `FORMULAS` (Task 1–2).
- Registry pages: `{"id":"calculators","path":"calculators/","template":"calculators.html","nav":"nav.calculators","icon":"calculator","placement":["header","menu"]}` placed after `tibia`; `{"id":"calc-experience","path":"calculators/experience/","template":"calc-experience.html","nav":"calc.experience.title","icon":"trending-up","placement":[],"calc":["levelExp","levelPlan","shareRange"]}`.
- Template contract (all calculators): `<form data-calc="<page id>">`; inputs `<input name="<formula input key>" data-kind="int|number|duration" inputmode=…>` each followed by `<p class="echo" data-echo="<name>">` and `<p class="field-error" data-error="<name>">`; outputs `<output data-out="<formula output key>" data-value="<raw>">` inside one `aria-live="polite"` region; buttons `[data-copy]`, `[data-share]`, `[data-reset]`; `<details class="how">` with rule text, source links and "checked on" date rendered by the builder from `formulas.json`.
- Experience page specifics: share range shown for both current and target level (two `shareRange` calls, outputs `data-out="from.min"` etc.); estimated finish date = now + `days` computed in `calc.js` (not in the formula). The spec's optional "% to next level" input is **left out** in batch 1: under the all-or-nothing rule it would need its own verified values and no captured source covers it.
- `calc.js`: on input → parse each field (page language from `TF_I18N.lang`) → if all valid call each formula whose inputs are all present → write formatted text + `data-value` to each `data-out`; reads/writes `location.search` (`history.replaceState`) for share links; copy builds a plain-text summary.
- `render()` loads `["site.js","formulas.js","calc.js"]` on pages with `calc` and on `calculators`; runtime strings export keys starting `calc.` on those pages only.

- [ ] **Step 1: Write the failing e2e check** `calculators(c, browser, base)` in `tests/e2e.py`, added to `CHECKS`: for every registry page with `calc`, in all 5 languages at 360×780: for every test in `formulas.json` of its formulas whose inputs exist on the page, fill the inputs (digits only), assert each `[data-out=k]` has `data-value == str(out[k])`; assert no horizontal overflow (`scrollWidth <= innerWidth`); `to=100, from=200` shows `[data-error]` text and empties outputs; `?from=100&to=200&exph=1500000&hpd=24` on `/de/…` fills the form and shows `113.695.000`; Copy puts text containing the exp value on the clipboard; Reset restores the template defaults and clears the query string; hub lists a card linking to every calc page.
- [ ] **Step 2:** `python -I website/tests/e2e.py --only calculators` → FAIL (no pages).
- [ ] **Step 3:** Implement registry entries, templates, `calc.js`, CSS (one column < 768 px, two columns ≥ 768 px; `.how` collapsed on phones), builder changes (scripts, runtime `calc.*`, rendering of `{{CALC_HOW:<formula id>}}` from `formulas.json`), Lucide icons, home card, breadcrumbs Home › Calculators › name, locale keys in 5 languages.
- [ ] **Step 4:** Strict build (with `--checksums`), `check_i18n.py`, unit tests, `e2e.py` (all 15 checks) → all PASS.
- [ ] **Step 5:** Commit `feat(calc): calculator pages, hub and experience calculator`.

### Task 4: Stamina (formula + page)

**Interfaces:** `FORMULAS.staminaRest({current:int /*min*/, target:int /*min*/}) -> {minutes:int}`; page `calc-stamina` (`calculators/stamina/`, icon `battery-medium`, `calc:["staminaRest"]`), inputs `current`, `target` (`data-kind="duration"`), output `minutes` plus a "full at" local clock time computed in `calc.js` from now (not part of the formula).

- [ ] **Step 1: Verify sources.** Regeneration rule (TibiaWiki "Stamina"; official if available): logged out, regeneration starts after 10 min; below 39:00 1 min per 3 min, from 39:00 to 42:00 1 min per 6 min. Independent values: TibiaPal 39:00→42:00 = 18 h (captured) **and a second calculator** (Exevo Pan – press "Track"; capture JSON+screenshot). Decide with the sources whether the 10-minute delay is included in `minutes`; both calculators must agree with the rule as implemented.
- [ ] **Step 2:** Tests in `formulas.json` (≥3: 39:00→42:00, 14:00→39:00, 41:59→42:00), unit FAIL → implement → PASS.
- [ ] **Step 3:** Template + locale keys; e2e `calculators` picks it up automatically; add case `target < current` → error.
- [ ] **Step 4:** Full suite PASS. **Step 5:** Commit `feat(calc): stamina calculator`.

### Task 5: Blessings (formula + page)

**Interfaces:** `FORMULAS.blessings({level:int}) -> {regularEach:int, regularAll:int, enhancedEach:int, enhancedAll:int, inquisition:int, twistOfFate:int}` (gp); page `calc-blessings` (`calculators/blessings/`, icon `sparkles`), input `level`.

- [ ] **Step 1: Verify sources.** Price rules for regular, enhanced, Blessing of the Inquisition and Twist of Fate (official Tibia.com manual / news first, TibiaWiki "Blessings" second) with an independent value for **each** output (published price table or two calculators). Any output without verification → the whole Blessings calculator waits (remove the page, report).
- [ ] **Step 2:** ≥3 tests including the level brackets' boundaries (each boundary level and the level just above it). Unit FAIL → implement → PASS.
- [ ] **Step 3:** Template + locale keys. **Step 4:** Full suite PASS. **Step 5:** Commit `feat(calc): blessings calculator`.

### Task 6: Character stats (formula + page)

**Interfaces:** `FORMULAS.characterStats({level:int, vocation:"none"|"knight"|"paladin"|"sorcerer"|"druid"|"monk"}) -> {hp:int, mana:int, cap:int}`; page `calc-stats` (`calculators/character-stats/`, icon `heart-pulse`), inputs `level`, `vocation` (`<select>`).

- [ ] **Step 1: Verify sources.** Per-vocation gains and the level-8 start values (TibiaWiki "Hitpoints", "Mana", "Capacity", vocation pages; official manual if present) with an independent value **for every vocation including Monk and "none"** (TibiaWiki Character Stats Calculator output, NabBot docs, or another calculator). Any vocation unverified → the whole calculator waits.
- [ ] **Step 2:** ≥3 tests per vocation (level 1 or 8, a mid level, level 2,000). Unit FAIL → implement → PASS.
- [ ] **Step 3:** Template (select fills from locale keys `calc.vocation.*`) + e2e must also set the `<select>`. **Step 4:** Full suite PASS. **Step 5:** Commit `feat(calc): character stats calculator`.

### Task 7: "Today in Tibia" – server save and Rashid

**Files:** Modify `formulas.js`, `formulas.json`, `src/js/data-app.js` (home/hub panel render), `templates/home.html`, `templates/calculators.html`, locales, `tests/e2e.py`.

**Interfaces:** `FORMULAS.nextServerSave({now:int /*epoch ms*/}) -> {at:int /*epoch ms*/}`; `FORMULAS.rashid({now:int}) -> {town:"svargrond"|"libertybay"|"porthope"|"ankrahmun"|"darashia"|"edron"|"carlin"}`. Time zone via `Intl.DateTimeFormat(..., {timeZone:"Europe/Berlin"})`, no hard-coded UTC offsets.

- [ ] **Step 1: Verify sources.** Server-save time (official Tibia.com / TibiaWiki "Server Save") and Rashid's weekday schedule with the switch at server save (TibiaWiki "Rashid" + a second source such as TibiaRoute/BomDiaTibia daily panels).
- [ ] **Step 2:** Tests (≥3 each, epoch ms in `in`): 2026-03-28 09:59:59 Berlin, 2026-03-29 10:00:00 Berlin (CEST starts), 2026-10-25 09:59:59 and 10:00:00 Berlin (CET starts), a Sunday 09:59:59 (Rashid still Saturday's town) vs 10:00:00. Unit FAIL → implement → PASS.
- [ ] **Step 3:** Panel shows countdown (updates each minute) and local clock time of the next server save, and Rashid's town; e2e uses `page.clock.install(time=…)` to pin two of the test times and checks the rendered town and countdown.
- [ ] **Step 4:** Full suite PASS. **Step 5:** Commit `feat(calc): server save and Rashid in Today in Tibia`.

### Task 8: Release gate for batch 1

- [ ] **Step 1:** Strict build with `--checksums`; `check_i18n.py`; all unit tests; `e2e.py` (15 checks) → all PASS.
- [ ] **Step 2:** Screenshot matrix (`tests/site_screenshots.py`, routes come from the registry) → 0 failures; inspect every calculator page at 360 px and 1440 px, light and dark, in at least 2 languages.
- [ ] **Step 3:** Update `docs/redesign/future-feature-roadmap.md` (status of #1–#3, #6, #7, #9–#11) and write `docs/superpowers/reports/2026-11-calculators-batch1.md`: shipped calculators, sources and checked-on dates, any calculator that waits and why, test results.
- [ ] **Step 4:** Commit; confirm protected paths unchanged (`git diff --name-only website-redesign -- games pokemon tools .github CNAME robots.txt` empty). **Do not push or deploy** – ask the owner.
