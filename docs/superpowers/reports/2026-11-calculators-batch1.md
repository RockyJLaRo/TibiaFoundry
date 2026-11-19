# Calculators batch 1 – release report (2026-11-19)

Branch `calculators` (from `website-redesign`). Design: `docs/superpowers/specs/2026-11-18-calculators-design.md`. Nothing is pushed or deployed; that waits for the owner's approval.
This report includes the final review's fix wave (findings in `.superpowers/sdd/2026-11-18-calculators-batch1/final-fix-brief.md`).

## What shipped

All four calculators shipped; none was held back.

English pages live at the site root; the other languages under `/pt-br/`, `/pl/`, `/es/` and `/de/` (for example `/de/calculators/experience/`). `/en/...` only redirects to the root page.

| Path (English; other languages add their prefix) | What it does |
|---|---|
| `/calculators/` | Hub: the four calculators plus the "Today in Tibia" block (Rashid's town, next server save). No TibiaData request on the hub. |
| `/calculators/experience/` | Experience needed between two levels, hunting time at exp/h, days, estimated finish date, party share range at both levels (roadmap #3, #11) |
| `/calculators/stamina/` | Offline time to regain stamina, regeneration part, the local clock time the target is reached (#6) |
| `/calculators/blessings/` | 5 regular blessings, each regular/enhanced, 2 enhanced, Blessing of the Inquisition, all 7 (with and without Inquisition), Twist of Fate (#9) |
| `/calculators/character-stats/` | Hit points, mana, capacity per level and vocation, including the Monk (#10) |
| Home and `/calculators/` | "Today in Tibia": server save countdown and Rashid's town (#7) |

Underneath: formula registry `website/src/formulas.json` with `website/src/js/formulas.js` (#2) and the generic calculator system `website/src/js/calc.js` (#1): labelled inputs, validation, results, "How this is calculated" with sources, per-source qualifiers and checked-on dates, copy result, copy share link, reset. Five languages (en, pt-BR, pl, es, de).

Level inputs accept 1 to 20,000 on every calculator (above about 20,700 some results would no longer be exact integers); results beyond the safe integer range are refused with an inline error rather than shown inexact.

## Sources and checked-on dates (from formulas.json)

| Formula | checkedOn | Sources shown on the page (kind; qualifier) |
|---|---|---|
| levelExp | 2026-11-18 | TibiaWiki Experience Formula (wiki); Tibia.com experience table, levels 2, 8, 100, 200, 2000 match exactly (official) |
| levelPlan | 2026-11-18 | the two above; tibiaroute.com level calculator, 100 to 200 at 1.5 kk/h, 4 h/day = 113,695,000 and 75 h 47 min (calculator; exp and minutes only) |
| shareRange | 2026-11-19 | TibiaWiki Party Experience Sharing table (wiki); TibiaWiki Character Stats Calculator, level 8 = 5 to 13 (calculator); tibiapal.com/expshare, captured 2026-11-19: 100 = 66 to 151, 3 = 2 to 5, 1000 = 666 to 1501 and five more levels, all equal to the table (calculator; qualifier: for level 1 it gives 1 as the lowest level, the table 0) |
| staminaRest | 2026-11-19 | TibiaWiki Stamina (wiki; 39:00 to 42:00 = 18 h 10 min offline); tibiapal.com/stamina, six cases captured (calculator; qualifier: regeneration time only, without the 10-minute logout delay) |
| blessings | 2026-11-19 | TibiaWiki Blessings (wiki); TibiaWiki Character Stats Calculator, 12 levels, columns one regular, one enhanced, "All 7" and "All 7 (using Henricus)" (calculator); tibiapal.com/bless (calculator; qualifier: rounded to thousands of gold, not used below level 30 where it shows negative prices) |
| characterStats | 2026-11-19 | Tibia.com manual 5.1 Characters (official); TibiaRoute's copy of CipSoft's news "A New Helping Hand II" of 15 October 2025, +200 base capacity (news; qualifier: TibiaRoute's copy of CipSoft's news); TibiaWiki Character Stats Calculator (calculator); guildstats.eu (calculator; qualifier: hit points and mana only, its capacity is from before the October 2025 update) |
| nextServerSave | 2026-11-19 | TibiaWiki Server Save (wiki); tibiastats.pl tools, "Server save in 03:33:41" at 05:26:14 UTC on 2026-11-19, i.e. 09:00 UTC = 10:00 Berlin within seconds (calculator); tibiapal.com announcements "10am CEST (server save)" (news) |
| rashid | 2026-11-19 | TibiaWiki Rashid (wiki); tibiastats.pl tools, "Rashid is in Port Hope" at 05:26:14 UTC Thursday (calculator); tibiaroute.com header tile with Rashid's figure labelled "Port Hope" at 05:26:04 UTC Thursday (calculator; the tile does not write the name Rashid) |

Every formula has an official or wiki source plus at least one value from a different, captured source. Captures (JSON + screenshots) live outside the repo in `research/calc-tests/`, excerpts in `research/sources/`. `build_site.py --strict` now also checks that every source kind is one of official, wiki, calculator, news and that every non-computed test names a listed source.

### Source decisions (final review)

- Removed from every page: Tibiawatch ("Buy at Coins4Gamers"), Intibia ("Get Tibia Coins", "Play Tibia Blackjack") and Exevo Pan ("PLAY TIBIABLACKJACK") – coin-seller or gambling promotions; Etien Reseller had been removed earlier. Their values were replaced by TibiaRoute (Rashid) and a new TibiaPal capture of the same stamina cases (all equal). The built site contains none of these names (checked by e2e).
- Removed: codingace.net (share range; its value was never captured) and the Tibia.com FAQ prose on the share range ("two-thirds", which would suggest 67-150 for level 100 against the table's 66-151). The captured TibiaPal Exp Share Calculator shows 66-151 for level 100, so ruling R13 (withhold Experience if an established calculator shows 67-150) was not triggered.
- Removed: TibiaWiki Formulae page (its capacity is 200 oz lower, from before the October 2025 update, and its Monk mana 5(2L - 9) disagrees with every other source).
- Added a localized qualifier wherever a shown source displays something different from our result (another rounding, part of the values, regeneration without the logout delay, a copy of CipSoft's news).
- Wording: the capacity change is "the October 2025 update (CipSoft news, 15 October 2025)" – the news only says the release "is scheduled for ... Tuesday, October 21st"; the Rookgaard note now matches TibiaWiki's Capacity page ("Choosing the vocation after level 8 gives lower values than shown, for example permanently less capacity for knights and paladins").

## Findings during verification

- The plan's share range for level 100 (67-150) was wrong. The correct values are 66-151: min = floor(2L/3), max = floor(3L/2) + 1 (TibiaWiki table, TibiaPal capture).
- TibiaWiki's Formulae page has errors for Monk mana and capacity. Monk mana was settled with the official manual and GuildStats; the +200 base capacity with CipSoft's news of 15 October 2025 (via TibiaRoute's copy, because tibia.com sits behind a human check that was not bypassed). Capacity gains apply only once a character levels up.
- Stamina 39:00 to 42:00: TibiaWiki says 18 h 10 min, TibiaPal shows 18 h. The difference is the 10-minute logout delay with no regeneration. The page shows 18 h 10 min as offline time, 18 h as the regeneration part, explains the delay, and the TibiaPal source carries that qualifier.
- Server save is at 10:00 CET/CEST and the Tibian day changes then, so before 10:00 Berlin time Rashid is still in the previous day's town (the e2e check pins Thursday 06:26 Berlin and expects Port Hope, as TibiaStats and TibiaRoute showed at that time).
- Review rounds fixed stale results after refused or overflowing input, clock-dependent results going stale on an open page, unsourced wording, and (final wave) screen-reader announcements: results are plain `<span>` values in one polite live region with atomic rows (label read with its value), unchanged values are not rewritten, and the clock-dependent rows (finish date, stamina "reached on") sit outside the live region, so a minute tick announces nothing.
- Copy result uses "-" and normal spaces only (game chat). The address bar follows the input about one second after typing stops (each history change can be a Google Analytics page view with enhanced measurement); language links, Copy and Share always use the current input.

## Test results (2026-11-19, after the final fix wave)

| Check | Result |
|---|---|
| `build_site.py --strict --checksums ...SHA256SUMS.txt` | `site: 65 pages in 5 languages; img: 204 images, 15,130 KB; version 9.0.5, 2026-11-19; SHA-256 shown for 5 archive(s)` |
| `check_i18n.py` | `OK: 5 locales, 940 keys each, no missing, empty, extra, duplicate or untranslated texts.` |
| `node --test formulas.test.mjs` | 21 tests, 21 pass, 0 fail |
| `node tibiadata.test.mjs` | 18 tests, 18 pass, 0 fail |
| `e2e.py` | full run 15/16: all pass except `responsive` (local `net::ERR_ADDRESS_IN_USE` console errors at 320px/dark, 768px/dark); `--only responsive` re-run alone: 1/1 PASS (1970 layouts). calculators: 330 test values shown correctly across 5 languages; today-in-tibia PASS |

What the e2e value count means: the `calculators` check types every formulas.json test of the four calculator pages into the page (through its data-formula mapping) in each of the 5 languages and compares every output's raw value and its formatted text. Per language: levelExp 6, levelPlan 3 (its fourth test, 100 to 100, is refused by the page's "target above current" rule and shows no number), shareRange 7, staminaRest 7, blessings 20, characterStats 23 = 66 test inputs, so 330 across 5 languages. The two "Today in Tibia" formulas are checked by the unit tests and by the `today-in-tibia` check at pinned times.

Hand checks (not from a captured source): level 100 blessings = 16,000 each regular, 80,000 for five, 20,800 each enhanced, 41,600 for two, 88,000 Inquisition, 121,600 all 7, 129,600 all 7 with Inquisition, Twist of Fate 16,000. Knight level 100 = 1,565 HP / 550 mana / 2,970 oz is also in the GuildStats (HP, mana) and TibiaWiki calculator (all three) captures.

## Known limitations

- The Polish and Spanish header navigation wraps to two lines (owner taste call; shorter labels possible).
- The hub shows the four cards as three plus one, with the "Today in Tibia" card half width; the hub has no boosted creature (no TibiaData requests by design).
- Stamina does not model potions or online regeneration. Blessings omit Phoenix Egg, Pilgrimage of Ashes discounts and Store prices (stated in a note); red/black skull characters pay full price (note).
- Character stats assume the vocation was chosen at level 8; later choices give lower values (note on the page).
- The share range for level 1 shows 0 as the lowest level (TibiaWiki table); TibiaPal shows 1. The page states this as a qualifier of the TibiaPal source.
- Server save does not model extended maintenance days (note on the page).
- The Experience page's "Estimated finish" row is shown last in the results panel, because clock-dependent rows sit outside the live region.
- Shared query strings (levels, exp/h, stamina times) are part of the URL that Google Analytics records; no personal data. The URL now changes at most once per typing pause.
- Some independent values are thousand-rounded (TibiaPal blessings, cited only where our exact value is a whole number of thousands); the Inquisition price on its own is derived (TibiaWiki shows only the "All 7 using Henricus" total, which is what is cited); the registry's `cites` marks which outputs a source actually showed.
- tibia.com pages (news, FAQ) could not be read directly (human check, not bypassed); the official experience table and manual were read earlier and are listed.
