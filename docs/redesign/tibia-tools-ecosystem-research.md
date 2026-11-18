# Tibia tools, calculators and player-utility ecosystem

**Research date:** 17–18 November 2026. Programme status is taken from the official fansite directory read on the same date
(see `official-fansite-research.md`); everything not listed there is **Independent/Unofficial**.

## Method and evidence levels

| Level | Meaning | Tools |
|---|---|---|
| **A – tested** | Same scenario entered by hand (scripted with Playwright in a real Chromium); the visible result was recorded with a screenshot and JSON (`research/calc-tests/`) | 4 exercise-weapon calculators, 1 level calculator, 1 stamina calculator |
| **B – inspected** | Page loaded and its real form fields, buttons, outputs and phone layout recorded (`research/toolpages/<slug>.json` + desktop/phone screenshots) | 45 tool pages |
| **C – documentation only** | Only a description, docs page or search result | marked "C" below |
| **D – identified, not verified** | Could not be opened (blocked or gone) | marked "D" below |

Discovery: web searches for each research category, the tool menus of every official fansite, and the calculator indexes of TibiaRoute, TibiaWatch, Exevo Pan, TibiaPal, TibiaWiki (EN and BR) and NabBot.

## Websites investigated

| Website | Programme status | Tool index visited |
|---|---|---|
| TibiaRoute | Promoted | https://tibiaroute.com/calculators |
| GuildStats.eu | Promoted | https://guildstats.eu/ (boss list https://guildstats.eu/bosses; `/calculator` returned 404) |
| TibiaWiki (Fandom) | Promoted | https://tibia.fandom.com/wiki/Calculators |
| TibiaWiki.com.br | Promoted | https://www.tibiawiki.com.br/wiki/Calculadoras |
| TibiaDraptor | Promoted | https://tibiadraptor.com/ |
| TibiaMaps.io | Promoted | https://tibiamaps.io/map, https://tibiamaps.io/tools/item-prices |
| Hunt-Analyser.com | Promoted | https://www.hunt-analyser.com/ |
| NabBot | Promoted | https://galarzaa90.github.io/NabBot/commands/calculators |
| Tibia Live | Promoted | https://tibia.live/ |
| Rookie.com.pl | Promoted | Calculators page (captured internal page) |
| TibiaVip.app | Supported | https://tibiavip.app/ (internal pages blocked) |
| TibiaMisterios.com.br | Supported | Tool grid on the home page (Split Loot, Character, Spells …) |
| Tibia-Statistic.com | Supported | blocked (Cloudflare) – D |
| Exevo Pan | Independent/Unofficial | https://www.exevopan.com/calculators |
| TibiaWatch | Independent/Unofficial | https://tibiawatch.com/calculators |
| TibiaPal | Independent/Unofficial | https://tibiapal.com/ (lists 10 calculators and 5 tools) |
| TibiaTrade.gg | Independent/Unofficial | https://tibiatrade.gg/ |
| CalculaCentro | Independent/Unofficial (generic calculator farm) | https://calculacentro.com/calculadora/tibia-xp |
| West-Games | Independent/Unofficial (generic blog) | https://west-games.com/tibia-party-loot-calculator/ |
| tibiabosses.com | Independent/Unofficial | https://www.tibiabosses.com/ – **now a Tibia Coins/gift-card shop ("Rei dos Coins")**, no boss data on the research date |

## Tool evaluations (significant tools)

Ev = evidence level. Mobile = scroll width at a 390 px phone viewport (390 = no overflow).

| Tool | Website (status) | URL | Category | Audience | Purpose | Inputs | Outputs | UX quality | Ev | Mobile | Strengths | Weaknesses | Data sources | Accuracy | Maintenance | Tibia Foundry opportunity |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Exercise Weapon Training Calculator | TibiaRoute (P) | https://tibiaroute.com/calculators/excercise-weapon-skill-training | Calculator | Players training skills | Weapons, cost and time from skill X to Y | Vocation, current skill, % to next, target, loyalty, dummy, double event, TC prices | Quantity, gp cost and time per weapon type, cost chart, share link | Good; clear groups | A | 390 | Share link; TC/gp hint | Typo in URL slug; assumed prices not editable on the page | Community formula | Matches TibiaPal exactly on the test (below) | Changes with skill-formula or price changes | Transparent price inputs and formula notes |
| Exercise Weapons Calculator | TibiaPal (unofficial) | https://tibiapal.com/exercise | Calculator | Players training skills | Same | Vocation & skill, current skill, % to next, target, loyalty, event, dummy, "TC over 17,400 gp" | Amount, cost (TC or gp) and time for lasting/durable/regular | Good, plain form | A | 392 (2 px overflow) | Explains the TC/gp switch | 4 ad markers; plain styling | Community formula | Same quantities as TibiaRoute | – | – |
| Exercise Weapons | Exevo Pan (unofficial) | https://www.exevopan.com/calculators/exercise-weapons | Calculator | Players | Same | Current/target skill, % left, dummy, event (vocation chips) | TC and gp cost, best mix of weapon types, time | Very clean, instant results | A | 390 | No button needed; optimal mix | gp cost assumption differs strongly from others | Community formula | gp cost 25 % higher than TibiaRoute for the same case | – | – |
| Exercise Weapons | TibiaWatch (unofficial) | https://tibiawatch.com/calculators/exercise-weapons | Calculator | Players | Same | Vocation, skill type, current/target, % (slider), loyalty, dummy, event | Charges needed, TC and gp cost, weapon mix, time | Polished; states "Formula based on TibiaWiki. Results may differ" | A | 390 | Honest disclaimer; per-server rules | Sliders for % are fiddly on phones; links "Buy at Coins4Gamers" (third-party coin seller) | TibiaWiki formula | 588,028 charges vs 586,000 elsewhere (+0.35 %) | – | – |
| Level Calculator | TibiaRoute (P) | https://tibiaroute.com/calculators/level-calculator | Calculator | Players | Experience and time to a target level | Start, target level, exp/h (kk), hours/day | Total exp, hours, finish date, progression chart, CSV/PNG/SVG export, share link | Good | A | 390 | Date estimate + export | Needs the user's exp/h guess | Official experience formula | Verified: 100→200 = 113,695,000 exp, equal to 50/3·(L³−6L²+17L−12) | Stable (formula unchanged for years) | Simple, verifiable quick win |
| Stamina Calculator | TibiaPal (unofficial) | https://tibiapal.com/stamina | Calculator | Players | Offline time to regain stamina | Current, target stamina (hh:mm) | Rest time and the date/time it is reached | Simple | A | 392 | Gives a clock time | Plain | Community-documented regeneration rule | 39:00→42:00 = 18 h (test) | Changes only if CipSoft changes regeneration | Quick win |
| Stamina Calculator | Exevo Pan (unofficial) | https://www.exevopan.com/calculators/stamina | Tracker/calculator | Players | Same, with tracking | Current/target hh:mm, "Track" | Tracked timers | Clean | B (no result shown without pressing Track; not run) | 390 | Tracking | – | Same | Not tested | – | – |
| Loot Splitter | TibiaRoute (P) | https://tibiaroute.com/calculators/loot-splitter | Parser | Party hunters | Who pays whom after a party hunt | Pasted Party Hunt Analyser text | Transfers, summary | Good | B | 390 | Parses the in-game analyser text | Needs the paste | In-game analyser export | Depends on analyser text format | Breaks when CipSoft changes the export format | Core roadmap candidate |
| Loot Split | Exevo Pan (unofficial) | https://www.exevopan.com/calculators/loot-split | Parser | Party hunters | Same, with history | Pasted session, advanced options | Transfers, copy buttons, history | Very good | B | 390 | History, copy per player | – | Same | Same | Same | – |
| Loot Split | TibiaWatch (unofficial) | https://tibiawatch.com/calculators/loot-split | Parser | Party hunters | Same | Pasted session | "Who pays whom" | Good, explains the method | B | 390 | Explains the calculation | – | Same | Same | Same | – |
| TibiaLootSplit / Split Loot | TibiaPal / TibiaMisterios | linked from their tool menus | Parser | Party hunters | Same | – | – | – | C | – | – | – | – | – | – | – |
| Imbuement Cost Calculator | TibiaRoute (P) | https://tibiaroute.com/calculators/imbue-cost | Calculator | Players | Cheapest way to imbue (items vs gold tokens) | Tier, gold-token price, item prices | Cost, shopping list, **NPC command text** | Good | B | 414 (overflow) | Shopping list and NPC text | Overflows phones | Item lists (community), user prices | Correct only with current prices | Item lists change with updates | – |
| Imbuement Price Calculator | Exevo Pan | https://www.exevopan.com/calculators/imbuements-cost | Calculator | Players | Same | Gold Token price, item prices per imbuement | Cheapest option, copy | Clean | B | 390 | Copy to clipboard | – | Same | Same | Same | – |
| Imbuement Calculator | TibiaWatch | https://tibiawatch.com/calculators/imbuements | Calculator | Players | Same | Prices, "pay with gold tokens" toggles | Items and cost per tier | Good | B | 390 | Per-item toggle | – | Same | Same | Same | – |
| Charm Damage Calculator | Exevo Pan | https://www.exevopan.com/calculators/charm-damage | Calculator | Players | Compare charm damage | Average damage, crit, creature HP | Charm comparison | Clean | B | 390 | Small and focused | – | Community mechanics | Mechanics-sensitive | Charm changes | – |
| Charm Spot Calculator | TibiaRoute (P) | https://tibiaroute.com/calculators/charm-spot-calculator | Planner | Players | Where to unlock charms | Monsters, HP/MP | Recommendation, share link | Good | B | 390 | Share link | – | Bestiary data | – | Bestiary updates | – |
| Bestiary / Bosstiary Tracker | TibiaRoute (P) | https://tibiaroute.com/bestiary-tracker | Tracker | Completionists | Track bestiary and charm points | Kill progress (login) | Progress, best next creatures | Good | B | 390 | Spawn-map link | Login for saving | Bestiary data | – | Each new creature | – |
| Progress trackers | TibiaDraptor (P) | https://tibiadraptor.com/ | Tracker | Completionists | Achievements, bestiary, bosstiary, charms, mounts, outfits, quests, titles | Account + imports | Progress, challenges | Very good | B | 390 | Multi-character | Account required | Manual + imports | – | High | Out of scope (accounts) |
| Hunting places | TibiaRoute (P) | https://tibiaroute.com/hunting-places | Database | Players | Where to hunt by level/vocation | Filters (level, size, boosted…) | Spots with creatures | Good | B | 390 | Filters by party size | – | Community reports | Subjective | Ongoing | – |
| Hunting Places | TibiaPal | https://tibiapal.com/hunting | Database | Players | Same | Level, weapon type, vocation | Tables | OK | B | 392 | Simple | 6 big tables | Community | – | Ongoing | – |
| Hunting Advisor | TibiaWatch | https://tibiawatch.com/calculators/hunting-advisor | Planner | Players | Where to hunt and damage | Level, skills, magic level, loyalty, crit | Ranked respawns | Rich | B | 390 | Combines damage maths with spots | Many inputs | Community formulas | Formula-sensitive | High | – |
| Wheel of Destiny planner | TibiaRoute / TibiaWatch / TibiaPal | https://tibiaroute.com/calculators/wheel-of-destiny, https://tibiawatch.com/calculators/wheel-of-destiny | Planner | Level 51+ players | Plan wheel points | Level, vocation, build code | Perks, codes | Complex | B | TibiaRoute **914 px (overflow)**, TibiaWatch 390 | Build codes | Hard on phones | Community | High | Every wheel change | – |
| Forge calculator | TibiaWatch | https://tibiawatch.com/calculators/forge | Calculator | End-game players | Cheapest fusion/transfer route | Item, tier, prices | Routes, Monte Carlo simulation | Advanced | B | 390 | Simulation | Complex | Community | High | Forge changes | – |
| Weapon Proficiency | TibiaWatch / TibiaPal | https://tibiawatch.com/calculators/weapon-proficiency | Planner | Players | Plan proficiency perks | Weapon, points | Perks | – | B | 390 | – | – | Community | – | Each new weapon | – |
| Hunt timers | TibiaWatch | https://tibiawatch.com/calculators/hunt-timers | Timer | Players | Timers for timed hunts/bosses | – | Timers, keyboard shortcuts | Good | B | 390 | Shortcuts | – | Manual | – | – | – |
| Drome potions | TibiaWatch | https://tibiawatch.com/calculators/drome-potions | Reference | Players | Drome rotation and rewards | Level | Rotation, potions | – | B | 390 | – | – | Manual | – | Each rotation | – |
| Boss Damage Calculator | TibiaRoute (P) | https://tibiaroute.com/calculators/boss-damage | Log parser | Boss teams | Damage per player from server log | Pasted server log | Damage table | Good | B | 390 | Uses existing logs | – | Server log text | Depends on log format | Log format changes | – |
| Stash Scanner | TibiaRoute (P) | https://tibiaroute.com/calculators/stash-scanner | Image recognition | Players | Find daily-delivery and imbuement items in a stash screenshot | Screenshot | Detected items | Novel | B | 390 | Clever use of screenshots | Accuracy unknown | Item sprites | Not tested | Each sprite change | Inspiration only |
| Boss tracker / predictions | Exevo Pan, GuildStats | https://www.exevopan.com/boss-tracker, https://guildstats.eu/bosses | Statistics | Boss hunters | Which bosses may spawn | World | Chance/last seen | Good (Exevo); dense (GuildStats, 8 ad markers) | B | 390 | Per-world view | Predictions are estimates from kill statistics | Kill statistics (official) | Estimates | Daily | Possible with TibiaData kill statistics; must be labelled as estimates |
| Char Bazaar price estimator / history | Exevo Pan | https://www.exevopan.com/calculators/auction-estimations | Statistics | Bazaar buyers/sellers | Estimate a character's price | Level/skill ranges | Average of similar auctions | Clean | B | 390 | Large history | Requires scraping the official bazaar over time | Scraped bazaar history | Statistical | Continuous scraping | Out of scope (no data source without scraping) |
| Party share range | NabBot docs, TibiaPal "Exp Share Calculator" | https://galarzaa90.github.io/NabBot/commands/calculators | Calculator | Party hunters | Level range that can share experience | Level | Min/max level | – | C | – | – | – | Official rule (2/3) | Stable | Low | Quick win |
| Blessings calculator | NabBot docs, TibiaPal, TibiaWiki BR | as above | Calculator | Players | Cost of blessings by level | Level | Prices | – | C | – | – | – | Official prices | Price-change sensitive | Low | Quick win |
| Stats calculator (HP/mana/cap) | NabBot docs, TibiaWiki EN "Character Stats Calculator" | https://tibia.fandom.com/wiki/Character_Stats_Calculator | Calculator | Players | HP, mana, capacity by level and vocation | Level, vocation | Values | – | C | – | – | – | Official formulas | Stable | Low | Quick win |
| Online map viewer | TibiaMaps.io (P) | https://tibiamaps.io/map | Map | Everyone | Explore the map | – | Map, floors, markers | Good | B | 390 | Open data on GitHub | – | Community-explored map data | High | Each map update | Link, don't duplicate |
| Item price fixer | TibiaMaps.io (P) | https://tibiamaps.io/tools/item-prices | Utility | Hunters | Patch the client's custom sale prices file | Pasted JSON | Patched JSON | Clear 3-step UI | B | 390 | Step-by-step | – | User file | – | Low | Inspiration (step layout) |
| TibiaTrade.gg | TibiaTrade (unofficial) | https://tibiatrade.gg/ | Marketplace | Traders | Buy and sell items and houses between players | Search, filters, login | Offers | Good | B | 390 | Cross-world offers | Trading platform – compliance-sensitive area (not linked from Tibia Foundry) | User listings | – | – | Do not integrate |
| Generic calculator farms | CalculaCentro, West-Games | see above | Calculators | Search visitors | XP, loot, stamina | Many | – | Ad-heavy (9 ad markers each) | B | 390 | – | Generic SEO pages, unverified formulas, many ads | Unknown | Unverified | – | Shows that search visitors need trustworthy, sourced calculators |
| tibiabosses.com | (former boss site) | https://www.tibiabosses.com/ | – | – | – | – | – | – | D | – | – | Domain now sells Tibia Coins and gift cards | – | – | – | Never link |

## Head-to-head: exercise weapons (tested)

Scenario: Knight, melee skill 100 with 100 % still to go, target 110, loyalty 0 %, no dummy, no event (`research/calc-tests/exercise-*.json`, screenshots `.jpg`).

| | TibiaRoute (P) | TibiaPal | Exevo Pan | TibiaWatch |
|---|---|---|---|---|
| Regular weapons | 1,172 | 1,172 | – (mix) | – (mix) |
| Durable / Lasting | 326 / 41 | 326 / 41 | – | – |
| Charges implied | 586,000 | 586,000 | 586,000 (40 lasting + 5 durable + 2 regular) | **588,028** (40 + 6 + 3) |
| Cost in Tibia Coins (regular) | – | 29,300 TC | 29,300 TC | 29,415 TC |
| Cost in gold | 406.94 kk (regular) | – (TC shown) | **508,680,556 gp** | 408,541,666 gp |
| Time | 325 h 33 min | 325 h 33 min | 13 d 13 h 34 min | 13 d 14 h 50 min |
| TC-vs-gold hint | buy from NPC if TC > 13,888 gp | buy TC if TC < 17,400 gp | – | – |
| Explains assumptions | Hint text | Hint text | No | "Formula based on TibiaWiki. Results may differ." |
| Phone | 390 | 392 | 390 | 390 (sliders) |

Findings: the skill maths agree (TibiaWatch +0.35 %), but **gold costs differ by up to 25 %** and the "buy coins or gold" thresholds disagree (13,888 vs 17,400 gp), because each site uses a different, mostly unstated weapon price. None lets the player enter the NPC price or shows the per-weapon price it assumed. Easiest to understand: TibiaPal/TibiaRoute (plain tables); most useful output: Exevo Pan (optimal mix, instant). Best maintained signal: TibiaWatch (states its formula source).

## Head-to-head: loot splitters (inspected)

All four (TibiaRoute, Exevo Pan, TibiaWatch, TibiaPal/TibiaMisterios) parse the in-game Party Hunt Analyser text. Exevo Pan adds history and per-player copy buttons; TibiaWatch explains the method. None was found that also accounts for imbuement/supply costs entered separately or that works offline after the page is loaded – not tested in depth (no real party log was used).

## Head-to-head: level and experience (tested)

TibiaRoute's level calculator returned 113,695,000 experience for level 100→200, exactly the value of the official formula 50/3·(L³ − 6L² + 17L − 12); the time (75 h 47 min at 1.5 kk/h) is arithmetically correct. The Portuguese wiki's experience calculator page returned 404 on the research date. Generic farms (CalculaCentro) offer the same with 9 ad markers and no formula source.

## Community gaps (evidence-based)

1. **Hidden assumptions in money calculators.** Exercise-weapon gold costs vary by 25 % across four popular tools for the same case; none shows or lets you edit the per-weapon price. → A calculator that shows the formula, the assumed prices and lets players edit them.
2. **Phone layouts of planners.** Wheel of Destiny (TibiaRoute 914 px), imbuement (414 px) and several wiki calculators (`width=1000`) are not phone-friendly.
3. **Scattered daily facts.** Boosted creature/boss, Rashid, server save, events and drome rotation are spread across sites (BomDiaTibia, TibiaRoute, TibiaVip, TibiaWatch, TibiaWiki). Tibia Foundry already shows boosted creature/boss live.
4. **Trust in search results.** Generic calculator farms rank for Tibia calculators with heavy ads and unsourced formulas.
5. **Dead or repurposed tools.** tibiabosses.com now sells coins; TibiaPal's imbuing page returned 503/404; TibiaWiki BR's experience calculator 404 – a maintained, sourced set of basic calculators has value.
6. **Fansite-creator tooling.** No site offers web tools for fansite authors (sprite/infobox helpers) – Tibia Foundry's desktop app already covers this niche.

## Data, accuracy and maintenance

| Source | Reliable? | Legal/technical use | Patch sensitivity | Fallback |
|---|---|---|---|---|
| TibiaData API v4 (already used) | Yes; public, CORS-enabled, documented cache times | Allowed for public use; credited | Follows Tibia.com changes | Cached last-good answer (already implemented) |
| Official formulas (experience, party share, HP/mana/cap) | Stable for years | Published by CipSoft/wikis | Low | Static code |
| Community formulas (exercise, charms, imbuements) | Good but versioned | Cite TibiaWiki as source | Medium–high | Show formula date and source |
| Prices (exercise weapons, gold tokens, items) | Change with the market | User-entered values | High | Let the user enter prices |
| In-game analyser text (loot split, damage) | Stable format, unannounced changes possible | User-pasted, local only | Medium | Parse defensively, show errors |
| Char Bazaar history, market history | Requires scraping Tibia.com over time | Not offered by TibiaData; scraping avoided | – | Out of scope |
