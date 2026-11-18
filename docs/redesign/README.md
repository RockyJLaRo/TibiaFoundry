# Website redesign 2026 – documents

Not part of the published website: `website/build_site.py` only reads `website/src/`, and only `website/site/` is deployed.

| Document | Content |
|---|---|
| [official-fansite-research.md](official-fansite-research.md) | All 19 Promoted + 13 Supported fansites (17 Nov 2026): inventory, methods, synthesis, what was applied |
| [tibia-tools-ecosystem-research.md](tibia-tools-ecosystem-research.md) | Calculators and player tools across official and independent sites; tested vs inspected; head-to-head comparisons; gaps |
| [future-feature-roadmap.md](future-feature-roadmap.md) | 23 prioritised features (P0–P3) with the full schema and the five shortlists – **not implemented** |
| [design-system-and-concepts.md](design-system-and-concepts.md) | Concepts A/B/C, the selected A + C system, tokens, contrast results, components, pixel art, responsive and theme rules |
| [fansite-compliance-audit.md](fansite-compliance-audit.md) | Official agreement vs supplied checklist, 16-point audit, Rocky's actions, traffic-report guide, ongoing checklist |
| [ux-baseline-and-walkthroughs.md](ux-baseline-and-walkthroughs.md) | Problems of the 9.0.5 site and the 11 first-visitor tasks after the redesign |
| [concept-previews/](concept-previews/) | Rendered concepts (`index.html?c=a|b|c&t=light|dark`), their screenshots and the script that takes them |

Large or third-party material is kept **outside** the repository:

- `F:\Users\Rocky\Desktop\TibiaFoundry_Master\Work_Redesign\research\` – fansite captures (`fansites\<slug>\`), contact sheets (`sheets\`), tool pages (`toolpages\`), calculator tests (`calc-tests\`), capture and test scripts (`tools\`).
- `F:\Users\Rocky\Desktop\TibiaFoundry_Master\Work_Redesign\production-screenshots\` – the full screenshot matrix of the redesigned site with `manifest.json` (regenerate: `python website/tests/site_screenshots.py --out <folder>`).
