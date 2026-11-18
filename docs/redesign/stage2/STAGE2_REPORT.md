# Stage 2 – UI/UX overhaul, universal table sorting, palette and icon system (18 November 2026)

Branch `website-redesign` (worktree `Work_Redesign\repo`), on top of the uncommitted stage-1 redesign. **Nothing is
committed, pushed or deployed.** Design details: `../design-system-and-concepts.md` § Stage 2.

## 1. Visual improvements
- **Palette:** four candidates compared on real components in both themes (`palette-lab/`); "Ivory & Sapphire" chosen –
  warm ivory surfaces instead of brown parchment, slate header/footer with one antique-gold rule, sapphire actions (the
  logo's blue), gold only for accents, separate deep-slate dark theme with three surface levels; off-white headings in dark.
- **Components:** flat buttons with 8-px radius and quick feedback; inputs with a focus halo; 12-px cards with a soft
  shadow and hover lift on clickable cards; rounded featured panel keeping the gold pixel corners; download notice on a
  soft gold note; CSS chevrons and check marks instead of text glyphs; external-link marks on every new-tab link.
- **Tables:** rounded box, clear head, zebra rows, hover tint, sticky head for long tables, scroll shadows, phone sort bar.
- **Fixed defect:** buttons with the `hidden` attribute were still shown ("Show 0 more" under complete lists) – the
  attribute now always wins.

## 2. Palette (light / dark)
See the token table in `../design-system-and-concepts.md`. All 70 checked pairs meet WCAG 2.1 AA.

## 3. Icon system
Lucide 0.460.0 (ISC), 49 icons, inline sprite; replacement table in the design document.

## 4. Tables with sorting (all 13)
| View | Table | Columns now sortable (type) |
|---|---|---|
| Character | Other characters | name, world, status (text), notes (text; was not sortable) |
| Worlds | Regular worlds, tournament worlds | world, location, PvP, transfer, premium, world type (text), online (number), BattlEye (date; was text) |
| World | Online players | name, vocation (text), level (number) |
| Guilds | Active guilds, guilds in formation | name, description (text); logo is an image (not sortable) |
| Guild | Members | rank (guild hierarchy, new), name, title, vocation, status (text), level (number), joined (date) |
| Highscores | Ranking page | rank (number, starts ascending), name, vocation, world, title (text), level, points (number) – sorts the 50 rows of the loaded page (Tibia.com pages the list) |
| Houses | Houses, guildhalls | house (natural text: "Flat 2" before "Flat 10"), status, time left (text), size, rent, current bid (number) |
| Kill statistics | Creatures | creature (text), four kill counts (number) |
| Spells | Spells | name, formula, type, group, premium (text), level, mana, price (number) |
| News archive | Items | date (date), category, type, headline (text); "Original" link is not sortable (a URL) |

Not tables: facts lists (two-column key/value), creature and boss card grids (filterable by name), fansite cards, news lists.

## 5. Fansite contact persons
Every contact on the Fansites view links to `https://www.tibia.com/community/?name=<Name+With+Plus>` (new tab, marked as
external). Verified: Tibia.com's own directory links each contact to the same character page; all 31 distinct names
exist on TibiaData with an exact match (`research/fansite-contacts-verification.json`); the e2e check compares all 32
links with the fixture data.

## 6. Before and after
`before-after/` – 9 side-by-side images (home light/dark desktop and phone, highscores desktop and phone, guild, fansites,
downloads, spells), from the same fixtures: "before" = stage 1 (`Work_Redesign\stage2-before\`, 64 captures), "after" =
the final build.

## 7. Testing
| Check | Result |
|---|---|
| Strict build (`build_site.py --strict`) | OK – 40 pages, 5 languages |
| Locale parity (`tests/check_i18n.py`) | 5 × 817 keys, nothing missing, empty or untranslated (sort labels and announcements included) |
| API client unit tests | 18/18 |
| Playwright e2e | 14/14 – new `table-sorting` (94 column sorts across the worlds table in 5 languages and the guild members table, empty-last, aria-sort, announcements, phone sort bar, 32 fansite contact links) and the existing checks (pages, credits, 6/6 profiles, navigation, compliance, language/theme, data views, API failures, downloads, deep links, sanitizer, accessibility, 1,520 responsive layouts at 320–1920 px). `responsive` twice hit a local `ERR_ADDRESS_IN_USE` (socket exhaustion on this PC) and passed on re-run |
| Contrast (tokens) | 70/70 pairs ≥ WCAG AA |
| Screenshot matrix | 1,350/1,350 captured, 0 failed (27 routes × 5 languages × 2 themes × 360/390/768/1280/1440 px), validated by the pipeline; cross-sections inspected (found and fixed: badge stretching in phone cards, formula wrapping on tablets, a 19-px overflow from the first formula fix) |
| Keyboard | sort buttons are real buttons in the header (Tab/Enter/Space); phone sort select + button; focus rings unchanged |

Remaining issues: highscores sort the 50 loaded rows of the current page (Tibia.com pages the list; the pager is kept);
the house "time left" column is sorted as text (its values are empty in the recorded data, so a duration parser could not
be verified).

## 8. Files
Modified: `website/README.md`, `website/THIRD-PARTY-NOTICES.md`, `website/build_site.py`, `website/i18n-report.md`,
`website/src/js/data-app.js`, `website/src/locales/{en,pt-BR,pl,es,de}.json`, `website/src/providers.json`,
`website/src/registry.json`, `website/src/styles.css`, `website/src/templates/{about,contact,downloads,features,guides,home,layout,tibia}.html`,
`website/tests/e2e.py`, `website/tests/sitekit.py`, `website/art/pixelart.py`, the generated `website/site/` (54 files)
and `docs/redesign/design-system-and-concepts.md`.
Added: `website/art/icons.py`, `website/art/lucide/` (49 SVGs + LICENSE), `website/art/pixel-icons.svg` (archive),
`website/src/ui-icons.svg`, `docs/redesign/stage2/` (this report, palette lab + screenshots, before/after images and scripts).
Removed from the site: `website/src/images/pixel/icons.svg` (pixel icons replaced by Lucide).

## 9. Protected paths
`games/`, `pokemon/`, `tools/`, `.github/`, `CNAME`, `robots.txt`: no changes in the branch and none in the Pages
repository (`git status` empty).

## 10. Git status and readiness
Uncommitted on `website-redesign` (stage 1 + stage 2 together). Ready for review; not committed, pushed or deployed –
waiting for authorisation.
