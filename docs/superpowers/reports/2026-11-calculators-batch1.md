# Calculators batch 1 – release report (2026-11-19)

Branch `calculators` (from `website-redesign`). Design: `docs/superpowers/specs/2026-11-18-calculators-design.md`. Nothing is pushed or deployed; that waits for the owner's approval.

## What shipped

All four calculators shipped; none was held back.

| Path (per language prefix, e.g. `/en/`) | What it does |
|---|---|
| `/calculators/` | Hub: the four calculators plus the "Today in Tibia" block (Rashid's town, next server save). No TibiaData request on the hub. |
| `/calculators/experience/` | Experience needed between two levels, hunting time at exp/h, days, estimated finish date, party share range at both levels (roadmap #3, #11) |
| `/calculators/stamina/` | Offline time to regain stamina, regeneration part, target clock time (#6) |
| `/calculators/blessings/` | Price of 5 regular blessings, each regular/enhanced, Blessing of the Inquisition, Twist of Fate (#9) |
| `/calculators/character-stats/` | Hit points, mana, capacity per level and vocation, including the Monk (#10) |
| Home and `/calculators/` | "Today in Tibia": server save countdown and Rashid's town (#7) |

Underneath: formula registry `website/src/formulas.json` with `website/src/js/formulas.js` (#2) and the generic calculator system `website/src/js/calc.js` (#1): labelled inputs, validation, results, "How this is calculated" with sources and checked-on dates, copy result, copy share link, reset. Five languages (en, pt-BR, pl, es, de).

## Sources and checked-on dates (from formulas.json)

| Formula | Sources (kind) | Read on |
|---|---|---|
| levelExp, levelPlan | TibiaWiki Experience Formula (wiki); Tibia.com experience table, levels 2, 8, 100, 200, 2000 match exactly (official); tibiaroute.com level calculator, 100 to 200 at 1.5 kk/h, 4 h/day gives 113,695,000 and 75 h 47 min (calculator, exp and minutes only) | 2026-11-18 |
| shareRange | TibiaWiki Party Experience Sharing table (wiki); codingace.net share-exp calculator, level 100 gives 66 / 151 (calculator); Tibia.com FAQ 91 (official, wording approximate) | 2026-11-18 |
| staminaRest | TibiaWiki Stamina (wiki); tibiapal.com/stamina (calculator); exevopan.com/calculators/stamina (calculator) | 2026-11-18 |
| blessings | TibiaWiki Blessings (wiki); TibiaWiki Character Stats Calculator page (calculator); further captures listed in the registry | 2026-11-18 |
| characterStats | Tibia.com manual section 5.1 Characters (official); TibiaRoute's copy of CipSoft news 8543 for the +200 capacity change (news); guildstats.eu (calculator); TibiaWiki Formulae (wiki) | 2026-11-18 / 2026-11-19 |
| nextServerSave | TibiaWiki Server Save (wiki); tibiastats.pl tools (calculator); tibiapal.com (news) | 2026-11-18 / 2026-11-19 |
| rashid | TibiaWiki Rashid (wiki); tibiastats.pl; intibia.com NPC page; tibiawatch.com (all calculator) | 2026-11-18 / 2026-11-19 |

Every formula has an official or wiki source plus at least one independent value; captures live outside the repo in `research/calc-tests/`.

## Findings during verification

- The plan's share range for level 100 (67-150) was wrong. The correct values are 66-151: min = floor(2L/3), max = floor(3L/2) + 1 (wiki table, codingace). The Tibia.com FAQ prose ("not less than two-thirds") is approximate.
- TibiaWiki's Formulae page has errors for Monk mana and for capacity. Monk mana was settled with the official manual and GuildStats; the +200 base capacity from the 21 October 2025 update with CipSoft's news 8543 (via TibiaRoute's copy, because tibia.com sits behind a human check that was not bypassed). Capacity gains apply only once a character levels up.
- Stamina 39:00 to 42:00: TibiaWiki says 18 h 10 min, TibiaPal shows 18 h. The difference is the 10-minute logout delay with no regeneration. The page shows 18 h 10 min as offline time, 18 h as the regeneration part, and explains the delay.
- A coin-reseller site was dropped as a source for Rashid.
- Server save is at 10:00 CET/CEST and the Tibian day changes then, so before 10:00 Rashid is still in the previous day's town (the screenshots at about 04:00 local time correctly showed Port Hope on a Thursday).
- During the earlier tasks, review rounds fixed stale results after refused or overflowing input, clock-dependent results going stale on an open page, and unsourced wording.

## Test results (2026-11-19)

| Check | Result |
|---|---|
| `build_site.py --strict --checksums ...SHA256SUMS.txt` | `site: 65 pages in 5 languages; img: 204 images, 15,130 KB; version 9.0.5, 2026-11-19; SHA-256 shown for 5 archive(s)` |
| `check_i18n.py` | `OK: 5 locales, 933 keys each, no missing, empty, extra, duplicate or untranslated texts.` |
| `node --test formulas.test.mjs` | 19 tests, 19 pass, 0 fail |
| `node tibiadata.test.mjs` | exits cleanly, 0 failures |
| `e2e.py` | `16/16 checks passed` (responsive: 1970 page/route layouts at 9 widths, 320 to 1920 px; calculators: 315 test values shown correctly across 5 languages; today-in-tibia pass) |
| `site_screenshots.py` | `Expected 1600, captured 1600, failed 0, skipped 0, missing 0` (32 routes x 5 languages x 2 themes x 5 viewports) |

Manual inspection of the screenshots (hub and the four calculators, 360 px and 1440 px, light and dark, en and pl): no overflow, clipping or broken layout found; the numbers match hand calculations (experience 113,695,000 and 66-151; Knight level 100 = 1,565 HP / 550 mana / 2,970 oz; blessings at level 100 = 16,000 each, 80,000 for five, 88,000 Inquisition).

## Known limitations

- The Polish and Spanish header navigation wraps to two lines (owner taste call; shorter labels possible).
- The hub shows the four cards as three plus one, with the "Today in Tibia" card half width; the hub has no boosted creature (no TibiaData requests by design).
- Stamina does not model potions or online regeneration. Blessings omit Phoenix Egg, Ashes and Pilgrimage discounts, and red/black skull surcharges beyond a note.
- Level cap is 50,000 in the experience calculator and 20,000 in character stats; results above the safe integer range are refused rather than shown inexact.
- The Experience share query string is kept in the URL and in the language links (GA sees it).
- Some independent sources are thousand-rounded (TibiaPal blessings) or computed by subtraction (Inquisition wiki value); the registry marks which outputs a source actually showed.
- Ledger items that were deferred to the final wave (`.superpowers/sdd/2026-11-18-calculators-batch1/progress.md`: record attributions in characterStats, the "scheduled for" wording, the Rookgaard note, a non-wiki server-save test, source pages with coin-seller promotions) were not re-audited in this release gate.
