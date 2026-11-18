# Website redesign 2026 – final report

Branch `website-redesign` (worktree `Work_Redesign\repo`, from `source` b06f1dd7 = v9.0.5). **Nothing is committed,
pushed or deployed.** Work dates: 17–18 November 2026.

## A. Existing website review
- **Architecture:** static multi-page site built by `website/build_site.py` from `website/src/` (registry, providers,
  templates, five locales, one stylesheet, four scripts); TibiaData-backed hash-routed tool app (20 views); GitHub Pages;
  Google Analytics 4. Details: `ux-baseline-and-walkthroughs.md`.
- **Existing functionality:** 12 data tools (characters, guilds, worlds, highscores, houses, kill statistics, creatures,
  spells, bosses, fansites, news, archive), live news and boosted cards, downloads with SHA-256, five languages, two themes.
  **No calculators existed** – no formulas were changed.
- **UX problems (B1–B11):** download-first home page, crowded header (1180 px breakpoint), inconsistent naming, flat tool
  grid, no breadcrumbs, thin footer, no required download notice, **undisclosed Google Analytics**, no transparency
  statements, generic identity, long phone page.
- **Deliberately unchanged:** routes and URLs, data client, sanitiser, caching, router, language and theme mechanics, SEO
  metadata, 404 deep links, Google tag ID, desktop application, release artefacts, every protected path.
- **Protected `tools/`** (Pages repo) holds only `validate-pokemon.mjs` (a deployment check) – no Tibia tools to restyle.

## B–D. Research and roadmap
See `official-fansite-research.md` (32/32 sites, 2 inaccessible behind human verification), `tibia-tools-ecosystem-research.md`
(45 tool pages inspected, 6 calculators tested, head-to-head tables) and `future-feature-roadmap.md` (23 items, shortlists).

## E. Design and implementation
Concepts A/B/C rendered and captured (`concept-previews/`); A + C selected (`design-system-and-concepts.md`). Implemented:
new tokens and components (`styles.css`), header with four task links + Download, phone menu with pixel icons, breadcrumbs,
task-first home (search, Today in Tibia, six task cards, app spotlight with notice, features, news, creator), grouped tool
hub with icons, Downloads with release facts and safety, new **Legal & transparency** page, complete footer, 19 original
pixel icons and four ornament masks, WCAG AA token contrast, no layout shift on the data page.

## F–G. Localization, compliance, testing
See `fansite-compliance-audit.md` and the summary in the chat report of 18 November 2026; production screenshots in
`Work_Redesign\production-screenshots\` (manifest inside).
