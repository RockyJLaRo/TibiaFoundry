# Tibia Fansite Agreement – compliance audit of tibiafoundry.com

**Verification date:** 17 November 2026 (read live at 23:30 US Eastern).
**Official references read:** Fansite Agreement https://www.tibia.com/community/?subtopic=fansites&page=agreement ·
Fansite Programme https://www.tibia.com/community/?subtopic=fansites&page=programme · directory
https://www.tibia.com/community/?subtopic=fansites · Tibia Rules https://www.tibia.com/support/?subtopic=tibiarules ·
https://www.tibia.com/ · https://www.cipsoft.com/.
**Scope:** the Tibia Foundry website as built from `website/` (branch `website-redesign`). The protected paths of the
Pages repository (`games/`, `pokemon/`, `tools/`, `.github/`, `CNAME`, `robots.txt`) were inspected read-only.
**Status of Tibia Foundry in the programme:** not listed as Promoted or Supported on the verification date; the
agreement's obligations formally bind programme members. This audit applies them as the target standard.

## Official agreement vs the supplied 16-point checklist

The official agreement (unchanged wording on the verification date) contains these requirements that the supplied checklist
states only partly or not at all:

- The contact person must communicate in **English** at an acceptable level and must check **email and the internal fansite
  board regularly**; CipSoft holds the contact person responsible for everything on the site.
- Content must be **about Tibia only**, **updated regularly**, correct and fair, with correct grammar and spelling, and the
  site needs a **professional and appealing design**; promoted fansites must present own content regularly.
- Fan-made articles must be distinguishable from official ones; **no plagiarism**; images/tables from other sites only with
  permission; unconfirmed information may be published if marked (e.g. "This information has not been confirmed by CipSoft").
- Detailed monster and item information is allowed; CipSoft does not help finding it.
- Screenshots containing cheat-tool advertising must be removed immediately.
- Downloads: no cheat tools, illegal software or links to them; a disclaimer that the tools are not made by CipSoft; best
  effort against viruses/backdoors; **CipSoft advises not to offer downloads at all**.
- Traffic statistics **monthly to fansites@tibia.com by the 3rd** (confirmed).
- Cooperation can be ended by either side at any time without reasons (benefits withdrawn); breaches lead to a warning or
  removal; the guidelines may change at any time and changes are announced.
- The programme is currently open only to general Tibia fansites (no guild or single-world sites); applications go to
  fansites@tibia.com, followed by a "Monitored Fansites" period.

## Website implementation (this redesign)

| Area | Where |
|---|---|
| CipSoft copyright, ownership of CipSoft content, non-affiliation, links to Tibia.com and CipSoft | Footer of every page (`footer.legal`, legal links, © year updated at run time), Legal page § Copyright and CipSoft, About § sources, data credits |
| Contact and responsible administrator (Rocky LaRochelle, Tibia character Roc ky with profile link, rocky@tibiafoundry.com) | Footer, Contact page (administrator list), About page, Legal § Responsible administrator |
| Advertising, sponsors, monetisation | Legal § Money and advertising (none; nothing hidden; changes announced first). Code check: no ad scripts, no affiliate or donation links in `website/src` |
| Content focus and accuracy | All site pages are about Tibia or Tibia Foundry; versions and dates come from the build; data views show their source and fetch time |
| Content provenance | Legal § Content sources (Tibia Foundry original / official CipSoft / external TibiaData / community / third-party graphics); "Data: TibiaData · Original on Tibia.com" under every data view; unconfirmed-information label defined |
| Quest spoilers | No quest solutions on the site; Legal § Community rules defines the warning, rendered in five languages |
| Moderation | No forum, comments or uploads on the site; Legal § Community rules: rules, Tibia Rules link, reporting by email; GitHub issues are the only public channel |
| Downloads | Download notice (exact required wording, 5 languages) beside the home download action and above the files on Downloads; official sources (this site + GitHub releases over HTTPS); version, release date, packages, sizes, OS, release notes, source code, SHA-256 per file and SHA256SUMS.txt; "not code-signed"; "no third-party malware scan published"; purpose statement and warning not to play with modified client files. Build check: a page offering a download fails to build without the notice |
| Tibia Rules | Link in every footer and on the Legal page; community rules forbid account trading, RMT, cheats, phishing |
| Programme status | Legal § Fansite Programme status (not a member, no badge, link to the programme); no badges anywhere |
| Privacy and statistics | Legal § Privacy and statistics: Google Analytics 4 disclosed, opt-out button (stored as `tf-analytics=off`, sets `ga-disable-G-J3EX3FB3PB` before the tag configures), Global Privacy Control honoured, TibiaData/static.tibia.com requests and every browser-storage key listed |

Automated evidence: `website/build_site.py --strict` (footer links, year, profiles, notice next to downloads, credits, one h1,
`rel="noopener noreferrer"`) and `website/tests/e2e.py` check `compliance` (notice before every download button in 5
languages, footer profiles with exact URLs, CipSoft/Tibia/Rules links, Legal sections, statistics opt-out round trip).

## Final 16-point audit

| # | Agreement point | Requirement | Website implementation | Status | Evidence or remaining action |
|---|---|---|---|---|---|
| 1 | Copyright and Attribution | Tibia/CipSoft copyright and non-affiliation information, linked | Footer on all 40 pages + Legal § Copyright; links to tibia.com and cipsoft.com | **Compliant** | Build check + e2e `compliance` (5 languages) |
| 2 | Contact Information | Accessible contact section and working email | Contact page, footer email link, About page | **Compliant** | `mailto:rocky@tibiafoundry.com` checked by e2e; no contact form |
| 3 | Responsible Administrator | Name, Tibia character and email published; English communication; regular checking of email and the internal board | Name, character (Roc ky, profile link) and email on Contact, About, Legal, footer | **Operational Action Required** | Website part done. Only Rocky can ensure the mailbox is read regularly (no response time is promised on the site) and, if accepted into the programme, the internal fansite board |
| 4 | Suitable Advertisements | Age-appropriate, controlled advertising | No advertising of any kind | **Compliant** | Code review of `website/src`: no ad scripts or ad slots. If ads are ever added: only controllable, minor-safe providers, labelled as ads |
| 5 | Transparent Monetization | Honest funding disclosure; no deceptive monetisation | Legal § Money and advertising: no ads, sponsors, donations, affiliate links or paid features; no mining, no hidden resource use, no view-botting | **Compliant** | Code review; statement must be updated before any monetisation starts |
| 6 | Tibia-Related, Accurate Content | Tibia-only, accurate, regularly updated, good spelling | Every Tibia Foundry page is about Tibia; texts reviewed in 5 languages | **Partially Compliant** | The same domain also serves **non-Tibia projects** in protected folders: `/games/` ("Pixel-Quill", a learning game) and `/pokemon/` ("Pokémon Learning Adventure"). They are not linked from the Tibia Foundry pages or sitemap, but they are reachable on tibiafoundry.com. Protected → not changed. Recommended: move them to a separate domain/subdomain. "Updated regularly" is an ongoing duty |
| 7 | Original Content and Attribution | Clear provenance, permissions, labels for unconfirmed information | Legal § Content sources; source line under every data view; credits; no copied fansite content; pixel art original | **Compliant** | Program screenshots contain CipSoft graphics – attributed as CipSoft's. Label for unconfirmed information defined (none published) |
| 8 | Quest Spoilers | Warning before complete solutions | No quest solutions published; warning text and style defined in 5 languages | **Compliant** | Not applicable today; use `.spoiler` + `legal.spoilerWarning.*` before any future solution |
| 9 | Moderation and Standards | Ability to moderate; behaviour standards; no hack links | No user-generated content on the site; rules and reporting address published | **Operational Action Required** | GitHub issues of RockyJLaRo/TibiaFoundry are public and linked; Rocky must moderate them (remove spam, hack links, RMT). The site does not claim active moderation |
| 10 | Download Safety and Disclaimer | Disclaimer before download; no cheat tools; best effort against malware | Required notice beside every download action; official HTTPS sources; SHA-256; release facts; honest signing/scan status | **Partially Compliant** | Not code-signed; **no malware scan performed or published** for 9.0.5 – Rocky should scan each release (e.g. VirusTotal or Microsoft Defender) and may then add the result. The agreement advises against downloads – kept deliberately (core product) with the disclaimer. See point 11 for the client-file editors |
| 11 | Tibia Rules | No facilitation of rule violations; rules linked | Tibia Rules linked on every page; community rules; purpose statement; warning against playing with modified client files | **Partially Compliant** | Needs Rocky's review: the desktop app includes an **Assets Editor, RCC Editor and Client Converter** that write modified copies of client files (docs `14_Tools_Settings_and_Scripts.md`). They never change the installed client, and the site now warns not to play with modified files, but whether distributing such editors is acceptable under the Tibia Rules should be confirmed with CipSoft (fansites@tibia.com) before applying to the programme. No code-level game interaction found (no process-memory or input-injection APIs in `src/`) |
| 12 | Monthly Traffic Reporting | Monthly statistics to fansites@tibia.com by the 3rd | Google Analytics 4 (G-J3EX3FB3PB) kept; disclosed; reporting guide below | **Operational Action Required** | Applies once in the programme; reports are not sent automatically and none were sent by this work |
| 13 | Fansite Programme Status | Accurate status; no unauthorised badges | Legal § status: not a member, no badge; programme linked | **Compliant** | Searched built site: no Promoted/Supported badge images or claims |
| 14 | Cooperation and Programme Terms | Accurate programme wording; nothing permanent or guaranteed | No benefits claimed; status statement will be updated if it changes | **Compliant** | Wording reviewed in 5 languages |
| 15 | Ongoing Compliance | Repeatable checklist | Checklist below | **Compliant** | Using it is Rocky's task |
| 16 | Future Agreement Changes | Process to monitor official changes | Review procedure below (quarterly + on announcements) | **Operational Action Required** | Rocky re-reads the agreement page and compares it with the summary above |

**Overall:** the website implements every point that a website can implement. Tibia Foundry is **not fully compliant**:
points 6, 10 and 11 are partially compliant (non-Tibia projects on the same domain; no malware scan; client-file editors
need confirmation), and points 3, 9, 12 and 16 depend on Rocky's ongoing actions.

## Exact actions for Rocky

1. Decide where `/games/` and `/pokemon/` should live (separate domain or subdomain) – they are protected, so this redesign did not move them.
2. Scan every release archive before publishing (and optionally publish the scan link on the Downloads page – change `download.facts.scanValue`).
3. Ask fansites@tibia.com whether distributing the Assets Editor / RCC Editor / Client Converter is acceptable; adjust the program or the website text if not.
4. Read rocky@tibiafoundry.com regularly; if accepted into the programme, also the internal fansite board.
5. Moderate the public GitHub issues.
6. If accepted into the programme: send the monthly traffic report (guide below) by the 3rd; add the badge only then, and update Legal § Fansite Programme status.
7. Decide on analytics consent for EU/EEA and Brazilian visitors (see Privacy note) – a legal decision this audit cannot make.
8. Run the checklist below after significant updates and every three months.

## Privacy note (not legal advice)

Google Analytics sets cookies and processes visit data. Under EU/EEA ePrivacy and GDPR rules, analytics cookies generally
need **prior consent**; Brazil's LGPD requires a legal basis and transparency. The redesign keeps the existing tag (as
required), discloses it, offers an opt-out and honours Global Privacy Control, but does **not** ask for prior consent.
Options for Rocky: (a) add a consent banner with GA Consent Mode (analytics off until accepted, as TibiaWars does);
(b) switch to cookieless analytics; (c) keep the current opt-out after legal advice.

## Monthly traffic report – administrator guide

| Item | Value |
|---|---|
| Analytics source | Google Analytics 4 property with measurement ID G-J3EX3FB3PB (analytics.google.com, signed in as the property owner) |
| Period | The previous calendar month (1st–last day) |
| Metrics to include | Users, new users, sessions, views, average engagement time, top pages, top countries, devices; optionally download clicks (event `download_click`) |
| Export | In GA4's Reports section open the engagement and acquisition overviews (menu names can differ by GA version), set the date range to last month, then use the report's share/download option to save a PDF or CSV |
| Deadline | By the **3rd** of each month |
| Recipient | **fansites@tibia.com** (subject e.g. "Tibia Foundry – traffic statistics <Month YYYY>") |
| Who sends | Rocky, manually. Nothing is sent automatically; do not publish the raw reports on the site |
| Log | Keep a private list of the dates reports were sent |

## Ongoing compliance checklist

Run after: a significant website update · a change to downloads or the release process · any advertising, sponsorship,
donation or affiliate arrangement · a new community or submission feature · a new external integration · an announced
change of the official agreement · and at least every three months.

1. **Copyright:** footer and Legal page name CipSoft, state copyright/ownership and non-affiliation, link tibia.com and cipsoft.com.
2. **Contact:** email link works; Contact page reachable from every page.
3. **Administrator:** name, character (Roc ky) and email current; mailbox read.
4. **Ads:** none, or only controlled, minor-safe ads labelled as ads.
5. **Money:** Legal § Money and advertising matches reality.
6. **Content:** only Tibia/Tibia Foundry content; versions, dates and descriptions current; spelling checked in 5 languages (`tests/check_i18n.py`); non-Tibia projects on the domain reviewed.
7. **Sources:** every new data view names its source; nothing copied from other fansites; unconfirmed information labelled.
8. **Spoilers:** any quest solution sits behind the spoiler warning.
9. **Moderation:** GitHub issues free of spam, hack links and RMT.
10. **Downloads:** notice beside every download action; links point to the official GitHub release over HTTPS; SHA-256 matches; release scanned; signing and scan statements true.
11. **Tibia Rules:** no cheat tools, bots, RMT or links to them; rules link present; desktop-app features reviewed.
12. **Traffic report:** sent by the 3rd (programme members).
13. **Programme status:** badges only if granted; status text current.
14. **Programme terms:** no claim of permanent or guaranteed status.
15. **Checklist:** this list updated when the site gains new features.
16. **Agreement changes:** re-read https://www.tibia.com/community/?subtopic=fansites&page=agreement and compare with the summary at the top of this file; update the site and this audit if it changed.
