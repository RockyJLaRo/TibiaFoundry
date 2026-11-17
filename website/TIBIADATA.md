# TibiaData integration – tibiafoundry.com 9.0.5

The Tibia data pages (`/tibia/`, `/<lang>/tibia/`) and the home page's *Tibia News & Updates* and *Boosted today* use the
public [TibiaData](https://tibiadata.com/) API. TibiaData is a private project that serves information from
[Tibia.com](https://www.tibia.com/) as JSON; it is not part of Tibia Foundry and does not endorse it.

## Version decision (verified 2026-11-17)

| Item | Finding |
|---|---|
| Current production version | **v5.1.0** – `https://api.tibiadata.com/v5/` answers 200 for every documented endpoint |
| Specification | OpenAPI 2 at https://docs.tibiadata.com/swagger.json (v3, v4 and v5 paths) |
| Older versions | v4 is deprecated on 2026-12-31 (https://tibiadata.com/docs/); v3 since 2024-01-31 – not used |
| Fallback | none needed: v5 is live, documented and covers every feature |
| Browser access (CORS) | `Access-Control-Allow-Origin: *` – the visitor's browser calls the API directly; no proxy, no key, no backend |
| Rate limits | none in production (highscores in "restricted mode"); https://tibiadata.com/faq/ |
| TibiaData's cache | Cloudflare: 60 s worlds; 120 s guilds; 300 s characters, houses, kill statistics, news; 900 s bosses, creatures, fansites, highscores, spells |
| Attribution | TibiaData's data is based on tibia.com; Tibia is © CipSoft GmbH. The site thanks TibiaData with a link in the footer of every page and names it, with a link, under every data view |

## Capability matrix

All paths are relative to `https://api.tibiadata.com/v5/`. *Cache* is our client cache (memory + sessionStorage), never
shorter than TibiaData's own. *Tests*: unit = `tests/unit/tibiadata.test.mjs` (fixture parsing, schema check), e2e =
`tests/e2e.py`, shots = `tests/site_screenshots.py`.

| Endpoint | Purpose | Parameters | Fields used | Website feature | Cache | Tests | Status |
|---|---|---|---|---|---:|---|---|
| `character/{name}` | one character | name | name, former names, title, sex, vocation, level, achievement points, world, former worlds, residence, married to, houses, guild + rank, position, last login, account status, unlocked titles, traded, deletion date, comment; account information; account badges; achievements; deaths (time, level, killers, assists, reason); other characters | Characters (`#/character/<name>`), header/home search, copy as wikitext | 300 s | unit, e2e (valid, unknown, reload), shots | implemented |
| `worlds` | all worlds | – | players online, record, regular and tournament worlds (name, online, location, PvP type, transfer, premium, BattlEye, world type) | Worlds (`#/worlds`), world pickers, hub "players online" | 120 s | unit, e2e (rows, filter, sort), shots | implemented |
| `world/{name}` | one world | name | status, online, record, creation, location, PvP, transfer, premium, BattlEye, type, quest titles, online players | World (`#/world/<name>`) with links to its highscores, guilds, houses, kill statistics | 120 s | unit, e2e, shots | implemented |
| `guilds/{world}` | guilds of a world | world | active and forming guilds (name, logo, description) | Guilds (`#/guilds/<world>`), guild search by name | 180 s | unit, e2e, shots | implemented |
| `guild/{name}` | one guild | name | logo, world, description, founded, active, applications, war, homepage, guildhalls, counts, members (rank, name, title, vocation, level, status, joined), invites, disband | Guild (`#/guild/<name>`), online-only filter | 180 s | unit, e2e, shots | implemented |
| `highscores/{world}/{category}/{vocation}/{page}` | rankings | world or `all`; 18 categories; 6 vocation filters; page | rank, name, vocation, world, level, title, value; page info; highscore age | Highscores (`#/highscores/...`) with selectors and paging | 900 s | unit, e2e (rows, category change), shots | implemented |
| `houses/{world}/{town}` | houses of a town | world, town (18 towns) | houses and guildhalls (name, id, size, rent, rented, auctioned, current bid, time left) | Houses (`#/houses/<world>/<town>`) | 300 s | unit, shots | implemented |
| `house/{world}/{house_id}` | one house | world, id | name, type, town, beds, size, rent, image, status (rented/auctioned, owner, paid until, moving, bid, bidder, auction end, original text) | House (`#/house/<world>/<id>`) | 300 s | unit, shots | implemented |
| `killstatistics/{world}` | kills of a world | world | per race: killed and players killed, last day and week; totals | Kill statistics (`#/killstatistics/<world>`) | 900 s | unit, shots | implemented |
| `creatures` | creature library | – | boosted creature; list (name, race, image, featured) | Creatures (`#/creatures`), boosted creature on hub and home | 86 400 s | unit, shots | implemented |
| `creature/{race}` | one creature | race | name, image, description, behaviour, hit points, experience, immune/strong/weak/healed, paralysable, summon/convince mana, sees invisible, loot | Creature (`#/creature/<race>`), copy as wikitable | 86 400 s | unit, shots | implemented |
| `spells` | spell library | – | name, id, formula, level, mana, price, group, instant/rune, premium | Spells (`#/spells`) with type filter, copy as wikitext/CSV | 86 400 s | unit, e2e (wikitext copy), shots | implemented |
| `spell/{spell_id}` | one spell | id | spell information (formula, vocations, type, damage, group, level, mana, soul, amount, price, cities, cooldowns, premium), rune information | Spell (`#/spell/<id>`) | 86 400 s | unit, shots | implemented |
| `boostablebosses` | boostable bosses | – | boosted boss; list (name, image) | Boostable bosses (`#/bosses`), boosted boss on hub and home | 1 200 s | unit, shots | implemented |
| `fansites` | fansite programme | – | promoted and supported fansites: name, logo, homepage, contact, content types, languages, specials, social media, fansite item | Fansites (`#/fansites`) | 86 400 s | unit, shots | implemented |
| `news/latest` | news of 90 days | – | id, date, headline, category, type, url | Home "Tibia News & Updates" (6 latest, refresh button), News (`#/news`) | 1 200 s | unit, e2e (list, refresh, failure, retry), shots | implemented |
| `news/newsticker` | news tickers of 90 days | – | as above | News (`#/news`, ticker section) | 1 200 s | unit, shots | implemented |
| `news/archive/{days}` | archive | days (30/90/180/365 offered; 1–3650 accepted) | as above | News archive (`#/news/archive/<days>`) | 1 800 s | unit, shots | implemented |
| `news/archive` | archive of 90 days | – | – | same data as `news/archive/90`, which the site uses | – | – | not used (duplicate) |
| `news/id/{news_id}` | one article | id | title, date, category, url, content_html (sanitized), content (fallback) | Article (`#/news/<id>`) with link to the original | 1 800 s | unit, e2e (sanitized, original link), sanitizer cases, shots | implemented |

Not available in the API (and therefore not shown, never invented): character history beyond Tibia.com's own fields (only
former names/worlds and the recent deaths Tibia.com lists), guild histories, auction history, creature locations and
difficulty.

## Architecture

* `src/js/tibiadata.js` – the only code that talks to the API: endpoint table with URL encoding, schema checks per
  endpoint, cache per endpoint kind (`registry.json` `api.cacheSeconds`), in-flight de-duplication, cancellation
  (`AbortSignal`; the network request stops when no caller waits), one retry for network errors, 429 and 5xx
  (Retry-After honoured, max. 5 s), error kinds (`notfound`, `invalid`, `network`, `server`, `ratelimit`, `malformed`,
  `aborted`), stale fallback (the last good answer, marked as older copy) when TibiaData is down.
* `src/js/sanitize.js` – allow-list sanitizer for news HTML (inert `<template>` parsing, no `innerHTML` with API data
  anywhere else; links only http(s) with `rel="noopener noreferrer nofollow"`, images only https; scripts, styles, frames,
  forms, SVG/MathML, event handlers, `style`/`class`/`id` removed). 18 malicious and legitimate cases in
  `tests/fixtures/sanitizer-cases.json`, run in Chromium by `tests/e2e.py`.
* `src/js/data-app.js` – hash router (routes from `registry.json` `dataRoutes`), views, loading skeletons, empty
  states, error messages with retry, "Data: TibiaData · Original on Tibia.com · fetched at" line under every view,
  sortable/filterable tables that become cards on phones, copy as wikitext/CSV (CSV cells that start with `= + - @` are
  prefixed so spreadsheets never run them as formulas).
* Searches are never stored. The only stored data: the cache (sessionStorage, this tab), the chosen language, theme and
  last world (localStorage).

## Fixtures

`tests/record_fixtures.py` records real v5 responses for the canonical examples in `registry.json` `fixtures` – character
**Roc ky**, world **Antica**, guild **Crusaders of Might and Magic**, house **Antica/Thais 10301**, creature **demon**,
spell **lighthealing**, news **9050**, and a missing character for the 404 case – into `tests/fixtures/tibiadata/`
(manifest with recording time and API release). They are public Tibia.com data and are used only by the tests and the
screenshot generator, which answer `api.tibiadata.com` from them inside the test browser. The production site has no test
mode and never loads them (`tests/e2e.py` check *no-fixture-leak*).
