# Data and resource credits – tibiafoundry.com

The registry is `src/providers.json`; the build renders it as **Data & resource credits** in the footer of every page
(all five languages, both themes) and on the About page, and refuses to write a page where a credit or its link is
missing. `tests/e2e.py` (*pages-and-attribution*) checks the rendered links on every page, language and theme.

| Provider | Link | What the site uses | Where | Credit shown | Terms and notes |
|---|---|---|---|---|---|
| **TibiaData** | https://tibiadata.com/ (docs: https://docs.tibiadata.com/) | Public API v5: characters, worlds, guilds, highscores, houses, kill statistics, creatures, spells, boostable bosses, fansites, news | Tibia data pages; home news and boosted creature/boss | Footer: “Thanks to TibiaData for providing the Tibia data used on this site.” (translated; “TibiaData” links to https://tibiadata.com/); “Data: TibiaData” link under every data view | Free public API, no key, no production rate limit; cache times respected. No partnership or endorsement. |
| **Tibia.com** | https://www.tibia.com/ | Original source of the data above; news articles and their images, creature, boss, guild, house and badge images loaded from static.tibia.com (not copied) | Same | Footer credit; “Original on Tibia.com” link under every view and on every article | Tibia © CipSoft GmbH. |
| **CipSoft GmbH** | https://www.cipsoft.com/en/ | Rights holder of Tibia: game graphics in program screenshots and pixel-art samples, all game data | Every page | Footer credit and legal notice | Independent fan project; not affiliated with or endorsed by CipSoft. |
| **TibiaWiki** | https://tibia.fandom.com/wiki/Main_Page | Named and linked as the wiki the program works for; template names described | Features, guides, footer | Footer credit | No TibiaWiki content is copied. Not affiliated with Fandom. |
| **Tibia fansites** | https://www.tibia.com/community/?subtopic=fansites | Fansite directory (names, logos, links as published on Tibia.com through TibiaData) | `#/fansites` | Footer credit; each fansite named and linked | Independent websites; no relationship with Tibia Foundry implied. |

The Google tag (`G-J3EX3FB3PB`, unchanged since earlier versions) is analytics, not a data provider.

Software licences of the desktop program are listed in `THIRD-PARTY-NOTICES.txt` (generated for each release). The
website ships no third-party code; see `website/THIRD-PARTY-NOTICES.md` for the tools used to build and test it.
