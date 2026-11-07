"""
Builds website/site/index.html for Tibia Foundry 8.6.0 from the published 8.5.0 page
(website/live-8.5.0-snapshot/index.html, identical to the repository's main branch on 2026-10-25).

The page stays one self-contained file for GitHub Pages (tibiafoundry.com): the same design, the
same single Google Analytics tag (G-J3EX3FB3PB), plain #anchor navigation with CSS scroll-margin
(no scripted scrolling). New screenshots are embedded as WebP data URIs.

Usage:  python -I website/build_site.py <metadata page PNG> <setup summary PNG> <archive question PNG>
        python -I website/build_site.py --positioning-only website/site/index.html   (identity text only)
        python -I website/build_site.py --release-8.7 website/site/index.html <Home page PNG>
            (updates the built 8.6.0 page to 8.7.0: version, the fansite toolkit section with the Home
             dashboard screenshot from --ui-snapshot (00_Home_dark.png), navigation groups, What's new)
"""
import base64
import io
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "live-8.5.0-snapshot", "index.html")
OUT_DIR = os.path.join(HERE, "site")
VERSION = "8.6.0"
RELEASE = "8.7.0"


def webp(png, width, crop=None):
    """A screenshot as a WebP data URI (ffmpeg; lossy at high quality, text stays sharp). crop: an ffmpeg crop (w:h:x:y)."""
    out = os.path.join(tempfile.mkdtemp(), "shot.webp")
    vf = (f"crop={crop}," if crop else "") + f"scale={width}:-2:flags=lanczos"
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", png, "-vf", vf,
                    "-c:v", "libwebp", "-quality", "88", "-compression_level", "6", out], check=True)
    data = open(out, "rb").read()
    probe = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", out],
                           capture_output=True, text=True, check=True).stdout.strip().split(",")
    return "data:image/webp;base64," + base64.b64encode(data).decode(), int(probe[0]), int(probe[1])


def replace(s, old, new, count=1):
    if s.count(old) < count:
        sys.exit(f"Not found ({s.count(old)}x, expected {count}): {old[:100]!r}")
    return s.replace(old, new) if count > 1 else s.replace(old, new, 1)


def fansite_positioning(s):
    """The page's identity: a toolkit for Tibia fansite administrators, wiki editors, artists and content creators."""
    s = replace(s, '<meta name="description" content="Tibia Foundry is a free Windows app that turns the files of your Tibia client into sprites, GIFs, maps, sounds and data for TibiaWiki, archives client versions and edits file metadata.">',
                '<meta name="description" content="Tibia Foundry is a free, independent Windows toolkit for Tibia fansite administrators, wiki editors, artists and content creators: official game assets, sprites, GIFs, outfits, creatures, maps, sounds and data, ready for community websites.">')
    s = replace(s, '<meta property="og:title" content="Tibia Foundry — sprites, maps, sounds and data from your Tibia client">',
                '<meta property="og:title" content="Tibia Foundry — the desktop toolkit for Tibia fansites, wikis and creators">')
    s = replace(s, '<meta property="og:description" content="A free Windows app that turns the files of your Tibia client into sprites, GIFs, maps, sounds and data for TibiaWiki.">',
                '<meta property="og:description" content="A free, independent Windows toolkit that turns the official files of your Tibia client into graphics, maps, sounds and data for TibiaWiki and Tibia fansites.">')
    s = replace(s, f"Free Windows app for TibiaWiki editors · Version {VERSION}", f"Free toolkit for Tibia fansites and wiki editors · Version {VERSION}")
    s = replace(s, "<h1>Your Tibia client's sprites, maps, sounds and data — ready for TibiaWiki.</h1>",
                "<h1>Official Tibia graphics, maps, sounds and data — ready for your fansite and wiki.</h1>")
    s = replace(s, "<p class=\"lead\">Tibia Foundry reads the files of your installed Tibia client and turns them into material you can use right away: animated GIFs, colored outfits, house and map images, sounds, and item, creature and NPC data.",
                "<p class=\"lead\">Tibia Foundry is an independent desktop toolkit for Tibia fansite administrators, wiki editors, artists, and content creators. It reads the official files of your installed Tibia client and turns them into material you can publish right away: animated GIFs, colored outfits, creature, house and map images, sounds, and item, creature and NPC data.")
    s = replace(s, "Windows 10/11, 64-bit · Free fan project, not affiliated with CipSoft or Fandom",
                "Windows 10/11, 64-bit · Free, independent community project, not affiliated with CipSoft or Fandom")
    s = replace(s, "<p class=\"lead\">Tibia Foundry is a desktop toolkit for working with the files of the Tibia game client. It began as the tool TibiaWiki editors use to extract images and data, and it has grown into one place for everything the client contains: every object, outfit, creature, house, map area, sound and item name.</p>",
                "<p class=\"lead\">Tibia Foundry is an independent desktop toolkit developed to support the Tibia community. It began as the tool TibiaWiki editors use to extract images and data, and it has grown into a collection of asset management, graphics generation, conversion, data exploration and content-production tools for everything the official client contains: every object, outfit, creature, house, map area, sound and item name.</p>")
    s = replace(s, "<p class=\"lead\">It is made for <strong>TibiaWiki editors</strong> first, and is just as useful for <strong>fan-site and tool makers</strong>, <strong>Open Tibia developers</strong> and <strong>players who want to keep old client versions</strong>. You don't need to know the client's file formats: pick what you want, preview it, and export it.</p>",
                "<p class=\"lead\">It is made for <strong>wiki editors</strong>, <strong>fansite administrators</strong>, <strong>artists and graphic designers</strong> and <strong>guide writers and content creators</strong> — including sites taking part in, or preparing for, CipSoft's Fansite Programme — and for <strong>players who want to keep old client versions</strong> for historical reference. You don't need to know the client's file formats: pick what you want, preview it, and export it.</p>")
    s = replace(s, "Collect item names and IDs from the client, TibiaWiki, Open Tibia items.xml/items.otb and CSV files.",
                "Collect item names and IDs from the client, TibiaWiki, CSV files and third-party items.xml/items.otb item lists.")
    s = replace(s, "and convert modern client assets to legacy Tibia.dat/Tibia.spr (10.98) for Open Tibia projects.",
                "and write the official assets in the classic Tibia.dat/Tibia.spr format (10.98) for historical comparisons and legacy-format viewers.")
    s = replace(s, "turns them into images, sounds and data for TibiaWiki and other fan projects.",
                "turns them into images, sounds and data for TibiaWiki, Tibia fansites and other community projects.")
    s = replace(s, "It is a fan project by RockyLaRo.", "It is an independent community project by RockyLaRo.")
    s = replace(s, "Tibia Foundry is an independent fan project and is not affiliated with or endorsed by CipSoft GmbH or Fandom.</p>",
                "Tibia is a trademark of CipSoft GmbH. Tibia Foundry is an independent community project and is not affiliated with or endorsed by CipSoft GmbH or Fandom.</p>")
    return s


def release_870(s, home_png):
    """The built 8.6.0 page as the 8.7.0 page: the fansite toolkit (Home, presets, enlargement, task groups)."""
    # The top of the Home page: welcome, client library and recently used tools (recent exports cut off: they show local paths).
    home, hw, hh = webp(home_png, 1218, crop="iw:545:0:0")

    s = replace(s, f"Free toolkit for Tibia fansites and wiki editors · Version {VERSION}", f"Free toolkit for Tibia fansites and wiki editors · Version {RELEASE}")
    s = replace(s, f'<a class="btn btn-ghost" href="#whats-new">What\'s new in {VERSION}</a>', f'<a class="btn btn-ghost" href="#whats-new">What\'s new in {RELEASE}</a>')
    # Nine navigation links keep the sticky bar on one line; the toolkit takes File Metadata's place.
    s = replace(s, '<a href="#metadata">File Metadata</a>', '<a href="#toolkit">For fansites</a>')

    # ---- features: the task groups of 8.7
    s = replace(s, "<p class=\"lead\">Twenty pages, in three groups: client data, tools, and managing your clients.</p>",
                "<p class=\"lead\">Twenty-one pages grouped by task, starting with a Home dashboard that lists every tool with what it is for.</p>")
    start = s.index("<summary>All 20 pages at a glance</summary>")
    end = s.index("<p><strong>Good to know:</strong>", start)
    s = s[:start] + """<summary>All 21 pages at a glance</summary>
      <div class="body">
        <p><strong>Start:</strong> Home</p>
        <p><strong>Graphics &amp; Sprites:</strong> Appearances (with Outfitter and NPC tools) · Extract Sprites · Advanced Tools</p>
        <p><strong>Game Data:</strong> Static Data · Item Catalog · Compare Files</p>
        <p><strong>Maps &amp; Houses:</strong> Map Tools · House Images</p>
        <p><strong>Sound &amp; Media:</strong> Sounds · File Metadata</p>
        <p><strong>Asset Management:</strong> Assets Editor · RCC Editor · Client Converter</p>
        <p><strong>Client Library:</strong> Clients · Client Installation Rebuilder</p>
        <p><strong>Utilities:</strong> Scripts (12 file utilities)</p>
        <p><strong>Application:</strong> Settings (with custom themes) · Compatibility · Diagnostics · About</p>
        """ + s[end:]

    # ---- new section: the fansite toolkit (File Metadata's section becomes the alternate background)
    toolkit = f"""<section id="toolkit">
  <div class="wrap">
    <span class="eyebrow" style="color:var(--accent)">New in {RELEASE}</span>
    <h2>Built for fansites, wikis and creators</h2>
    <p class="lead">Tibia Foundry {RELEASE} starts on a Home dashboard and exports images ready for the page they are meant for: a wiki infobox, a creature gallery, a guide or a reference sheet for artists.</p>
    <div class="split" style="margin-top:28px">
      <div>
        <ul class="checks">
          <li><strong>Home dashboard.</strong> Every tool by category with what it is for, the tools you used last, your recent exports (click one to show it in File Explorer), the client in use, and step-by-step workflows for wiki images, galleries, maps, update articles and item databases.</li>
          <li><strong>Export presets.</strong> TibiaWiki image, Creature gallery, Outfit gallery, Item icon, Article illustration and High-resolution reference. A preset only fills in the options; change any of them and the choice switches to <em>Custom</em>.</li>
          <li><strong>Pixel-exact enlargement.</strong> GIFs at 1×, 2×, 3×, 4×, 6× or 8× with nearest-neighbor scaling: no blur, no new colors, transparency kept. Enlarged files end in <code>_2x</code>, <code>_3x</code>…</li>
          <li><strong>Organized output.</strong> Automatically named GIFs can go into Objects, Outfits, Effects and Missiles folders.</li>
          <li><strong>Navigation by task.</strong> Graphics &amp; Sprites, Game Data, Maps &amp; Houses, Sound &amp; Media, Asset Management, Client Library, Utilities. No tool was removed.</li>
        </ul>
      </div>
      <figure>
        <img class="shot" src="{home}" width="{hw}" height="{hh}" alt="The Home dashboard: the toolkit's purpose, the navigation grouped by task, the official client in use and the recently used tools." loading="lazy">
        <figcaption>The Home dashboard (Tibia Foundry {RELEASE}).</figcaption>
      </figure>
    </div>
  </div>
</section>

<section id="metadata" class="alt">"""
    s = replace(s, '<section id="metadata">', toolkit)
    s = replace(s, f'<span class="eyebrow" style="color:var(--accent)">New in {VERSION}</span>\n    <h2>File Metadata:',
                '<span class="eyebrow" style="color:var(--accent)">Since 8.6</span>\n    <h2>File Metadata:')
    s = replace(s, f"Real screens of Tibia Foundry, reading the Tibia 15.33 client (8.5.0; File Metadata and setup: {VERSION}).",
                f"Real screens of Tibia Foundry 8.5 and 8.6, reading the Tibia 15.33 client. In {RELEASE} the same pages are grouped by task.")

    # ---- what's new: 8.7.0, with 8.6.0 kept below
    start = s.index(f"<h2>What's new in {VERSION}</h2>")
    end = s.index("<details>\n      <summary>Known limitations</summary>", start)
    s = s[:start] + f"""<h2>What's new in {RELEASE}</h2>
    <p class="lead">October 2026. Tibia Foundry is now a toolkit for fansite administrators, wiki editors, artists and content creators. No tool was removed; settings, client archives, revisions and item catalogs of 8.6 and earlier are used as they are.</p>
    <div class="notes">
      <div class="note"><h3>Home dashboard</h3><ul><li>Every tool by category, with what it is for.</li><li>Recently used tools and recent exports.</li><li>Step-by-step workflows, and links to TibiaWiki and the official fansites.</li></ul></div>
      <div class="note"><h3>Fansite exports</h3><ul><li>Six GIF presets, from TibiaWiki images to high-resolution references.</li><li>Pixel-exact enlargement from 1× to 8×.</li><li>Objects, Outfits, Effects and Missiles folders.</li></ul></div>
      <div class="note"><h3>Clearer layout</h3><ul><li>Navigation grouped by task.</li><li>Every tool's description written for wiki and fansite work.</li><li>Item lists from other projects are labeled third-party data: unverified leads.</li></ul></div>
    </div>
    <details>
      <summary>What was new in {VERSION}</summary>
      <div class="body">
        <p><strong>File Metadata:</strong> view, edit, compare and batch-edit the metadata of images, audio, video, documents, books and archives, with batch preview, presets, CSV and JSON reports, and verified saving with optional backups.</p>
        <p><strong>Default clients:</strong> the <strong>Default Live Client</strong> (<code>%LOCALAPPDATA%\\Tibia\\packages\\Tibia</code>) and <strong>Default Test Client</strong> (<code>…\\TibiaExternal</code>) are recognized by name, with their real state.</p>
        <p><strong>Clearer messages:</strong> setup's archive question and summary in sections with copyable paths; every message box has a Copy button and can be resized.</p>
      </div>
    </details>
    """ + s[end:]

    s = replace(s, f"Current version: <strong>{VERSION}</strong> (October 2026).", f"Current version: <strong>{RELEASE}</strong> (October 2026).")
    s = replace(s, "Click the “✓ Saved to…” message to open the folder.</li>",
                "Click the “✓ Saved to…” message to open the folder; Home lists your recent exports.</li>")
    s = replace(s, "generate NPC trade data, compare client versions, and keep and rebuild old client versions.</p>",
                "generate NPC trade data, compare client versions, and keep and rebuild old client versions. Export presets prepare images for TibiaWiki, galleries, guides and artists in one click.</p>")
    s = replace(s, "<p>Yes. Tools → File Metadata edits", "<p>Yes. Sound &amp; Media → File Metadata edits")
    s = replace(s, f"<strong>Tibia Foundry {VERSION}</strong>", f"<strong>Tibia Foundry {RELEASE}</strong>")
    s = replace(s, f"var APP_VERSION = '{VERSION}';", f"var APP_VERSION = '{RELEASE}';")

    # ---- checks: one analytics tag; 8.6.0 only where it names that version
    assert s.count("googletagmanager.com/gtag/js?id=G-J3EX3FB3PB") == 1 and s.count("gtag('config', 'G-J3EX3FB3PB')") == 1
    left = s.count(VERSION)
    allowed = s.count(f"(Tibia Foundry {VERSION})") + s.count(f"archive question ({VERSION})") + s.count(f"What was new in {VERSION}")
    if left != allowed:
        sys.exit(f"Stale version left: {left - allowed} × {VERSION}")
    return s


def main():
    meta_png, summary_png, question_png = sys.argv[1:4]
    s = io.open(SRC, encoding="utf-8").read()
    meta, mw, mh = webp(meta_png, 1218)
    question, qw, qh = webp(question_png, 584)
    meta_thumb, _, _ = webp(meta_png, 240)

    # ---- head
    s = replace(s, '<meta name="description" content="Tibia Foundry is a free Windows app that turns the files of your Tibia client into sprites, GIFs, maps, sounds and data for TibiaWiki.">',
                '<meta name="description" content="Tibia Foundry is a free Windows app that turns the files of your Tibia client into sprites, GIFs, maps, sounds and data for TibiaWiki, archives client versions and edits file metadata.">')

    # ---- styles: the format table and the release notes (same palette and spacing as the rest)
    s = replace(s, "  /* FAQ */", """  /* Format table (File Metadata) */
  .table-wrap { overflow-x: auto; margin-top: 28px; border: 1px solid var(--line-light); border-radius: var(--radius); }
  table.caps { width: 100%; border-collapse: collapse; font-size: 15px; min-width: 620px; }
  table.caps th, table.caps td { text-align: left; padding: 10px 14px; border-bottom: 1px solid var(--line-light); vertical-align: top; }
  table.caps th { background: var(--paper-2); font-weight: 600; }
  table.caps tr:last-child td { border-bottom: 0; }
  table.caps td:first-child { font-weight: 600; min-width: 190px; }
  /* Phones: each format becomes a small card instead of a wide table row. */
  @media (max-width: 600px) {
    table.caps { min-width: 0; }
    table.caps thead { display: none; }
    table.caps tr { display: block; padding: 12px 14px; border-bottom: 1px solid var(--line-light); }
    table.caps tr:last-child { border-bottom: 0; }
    table.caps td { display: block; border: 0; padding: 2px 0; min-width: 0; }
  }
  .tag { display: inline-block; font-size: 13px; font-weight: 600; padding: 1px 8px; border-radius: 999px; white-space: nowrap; }
  .tag.edit { background: #e3f3e8; color: #1f7a3d; }
  .tag.read { background: #eef1f5; color: #4a5563; }

  /* Release notes and lists */
  .notes { display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; margin-top: 28px; }
  .note { background: var(--paper); border: 1px solid var(--line-light); border-radius: var(--radius); padding: 20px 22px; }
  .note h3 { margin-bottom: 8px; }
  .note ul { margin: 0; padding-left: 20px; color: var(--text-2); font-size: 16px; }
  .note li { margin: 4px 0; }
  ul.plain { color: var(--text-2); padding-left: 20px; }
  ul.plain li { margin: 6px 0; }
  @media (max-width: 900px) { .notes { grid-template-columns: 1fr; } }

  /* FAQ */""")

    # ---- header navigation (nine links: the menu button takes over below 1080 px, so the sticky bar stays one line)
    s = replace(s, "  @media (max-width: 900px) {\n    .top .btn { margin-left: auto; }", "  @media (max-width: 1080px) {\n    .top .btn { margin-left: auto; }")
    s = replace(s, "window.matchMedia('(min-width: 901px)')", "window.matchMedia('(min-width: 1081px)')")
    s = replace(s, '<a href="#about">About</a><a href="#features">Features</a><a href="#screenshots">Screenshots</a><a href="#safety">Safety</a><a href="#start">Get started</a><a href="#requirements">Requirements</a><a href="#faq">FAQ</a>',
                '<a href="#about">About</a><a href="#features">Features</a><a href="#metadata">File Metadata</a><a href="#screenshots">Screenshots</a><a href="#whats-new">What\'s new</a><a href="#start">Get started</a><a href="#requirements">Requirements</a><a href="#help">Help</a><a href="#faq">FAQ</a>')

    # ---- hero
    s = replace(s, "Free Windows app for TibiaWiki editors · Version 8.5.0", f"Free Windows app for TibiaWiki editors · Version {VERSION}")
    s = replace(s, "It also edits and converts client files and keeps an archive of every client version you choose, without ever changing your Tibia installation.",
                "It also edits and converts client files, keeps an archive of every client version you choose, and views and edits the metadata of your images, audio, video and documents — without ever changing your Tibia installation.")
    s = replace(s, '<a class="btn btn-ghost" href="https://github.com/RockyJLaRo/TibiaFoundry" data-track="repository" data-place="hero">View source on GitHub</a>',
                '<a class="btn btn-ghost" href="#whats-new">What\'s new in ' + VERSION + '</a>')

    # ---- features
    s = replace(s, "Nineteen pages, in three groups: client data, tools, and managing your clients.", "Twenty pages, in three groups: client data, tools, and managing your clients.")
    s = replace(s, "<summary>All 19 pages at a glance</summary>", "<summary>All 20 pages at a glance</summary>")
    s = replace(s, "Client Converter · Client Installation Rebuilder · Scripts (12 file utilities)", "Client Converter · Client Installation Rebuilder · File Metadata · Scripts (12 file utilities)")

    # ---- new section: File Metadata (between features and screenshots)
    metadata = f"""<section id="metadata">
  <div class="wrap">
    <span class="eyebrow" style="color:var(--accent)">New in {VERSION}</span>
    <h2>File Metadata: see and edit what's inside your files</h2>
    <p class="lead">A tool for the metadata of almost any file: titles, authors, copyright, keywords, dates, GPS positions, album and track tags, document properties and more. Inspect one file, edit many at once, compare them, or export a report.</p>
    <div class="split" style="margin-top:28px">
      <div>
        <ul class="checks">
          <li><strong>Edit one file or hundreds.</strong> Select several files and type a value once, or build batch operations (set, remove, add or replace keywords, copy from a reference file) and preview them file by file before anything is written.</li>
          <li><strong>Every save is checked.</strong> Changes are written to a copy, read back and verified — the new values must be there and every other field unchanged — before the copy replaces the file. A failure leaves the file as it was.</li>
          <li><strong>Backups and honest results.</strong> Optional verified backups (restorable from Scripts → Backup and Restore), the modified date kept, files changed by another program never overwritten, and a result per file: saved, unchanged, not applicable or failed, with the reason.</li>
          <li><strong>Nothing removed silently.</strong> Removing authorship, copyright or GPS data is named before you confirm. Fields a format cannot store are refused and listed, never dropped.</li>
          <li><strong>Presets and reports.</strong> Save author, copyright and keywords as a preset; export CSV or JSON reports of any number of files.</li>
        </ul>
      </div>
      <figure>
        <img class="shot" id="metaImg" src="{meta}" width="{mw}" height="{mh}" alt="The File Metadata page: a list of files, and the metadata of the selected image grouped by category." loading="lazy">
        <figcaption>The File Metadata page (Tibia Foundry {VERSION}).</figcaption>
      </figure>
    </div>
    <div class="table-wrap">
      <table class="caps">
        <thead><tr><th>Files</th><th>What you can do</th><th>Details</th></tr></thead>
        <tbody>
          <tr><td>JPEG, TIFF, PNG, GIF</td><td><span class="tag edit">Edit</span></td><td>Title, description, keywords, rating, authors, copyright, date taken, orientation, GPS (EXIF, XMP, IPTC). The image itself is never re-encoded.</td></tr>
          <tr><td>MP3, FLAC, Ogg, Opus, M4A, WAV, AIFF, WMA, WavPack</td><td><span class="tag edit">Edit</span></td><td>Title, artists, album, genres, year, track, disc, composers, comment, lyrics, copyright, embedded pictures (remove). Duration, codec and bitrate shown.</td></tr>
          <tr><td>MP4, MOV, MKV, WebM, AVI, WMV</td><td><span class="tag edit">Edit</span></td><td>The same tags as audio; size, duration and codecs shown.</td></tr>
          <tr><td>Word, Excel, PowerPoint (.docx, .xlsx, .pptx)</td><td><span class="tag edit">Edit</span></td><td>Title, subject, authors, keywords, description, category, status, language, dates. The document's content is copied unchanged.</td></tr>
          <tr><td>EPUB books, SVG drawings, ZIP archives</td><td><span class="tag edit">Edit</span></td><td>Book metadata; drawing title and description; archive comment.</td></tr>
          <tr><td>PDF, BMP, WebP, ICO, camera raw, TAR, EXE/DLL, Tibia client files</td><td><span class="tag read">Read only</span></td><td>Document information, image details, archive contents, program version and signature presence, Tibia file details — shown, not changed.</td></tr>
          <tr><td>Any file</td><td><span class="tag edit">Edit</span></td><td>File system dates (created, modified, accessed) and the read-only, hidden and archive attributes, shown apart from the file's own metadata.</td></tr>
        </tbody>
      </table>
    </div>
  </div>
</section>

<section id="screenshots" class="dark">"""
    s = replace(s, '<section id="screenshots" class="dark">', metadata)

    # ---- screenshots: two new real screens of 8.6.0
    s = replace(s, "Real screens from Tibia Foundry 8.5.0, reading the Tibia 15.33 client.",
                f"Real screens of Tibia Foundry, reading the Tibia 15.33 client (8.5.0; File Metadata and setup: {VERSION}).")
    s = replace(s, '<button type="button" role="listitem" data-key="appearances" data-caption="Appearances: every object of the client, with filters."><img src="',
                '<button type="button" role="listitem" data-key="metadata" data-caption="File Metadata: an image\'s fields grouped by category, ready to edit."><img src="'
                + meta_thumb + '" alt="File Metadata" width="240" height="145" loading="lazy" style="object-fit:cover;aspect-ratio:240/145"></button>\n'
                + '        <button type="button" role="listitem" data-key="appearances" data-caption="Appearances: every object of the client, with filters."><img src="')
    # The large File Metadata screenshot is embedded once, in its section; the gallery reads it from there.
    s = replace(s, "shots['appearances'] = document.getElementById('heroImg').src;",
                "shots['appearances'] = document.getElementById('heroImg').src;\n    shots['metadata'] = document.getElementById('metaImg').src;")

    # ---- safety: the redesigned archive question of setup
    s = replace(s, "<li><strong>Personal data stays out.</strong> Client archives leave out hotkeys, character data, screenshots and logs unless you include them. The confirmation shows exactly what will be stored.</li>",
                "<li><strong>Personal data stays out.</strong> Client archives leave out hotkeys, character data, screenshots and logs unless you include them. Setup and the archive confirmation show exactly what is stored and what is left out, category by category.</li>\n"
                "        <li><strong>Edits are verified.</strong> File Metadata writes to a copy, reads it back and checks it before it replaces a file, and can keep a verified backup.</li>")
    start = s.index('<img class="shot" src="data:', s.index('<section id="safety">'))
    end = s.index("</figure>", start)
    s = s[:start] + f'<img class="shot" src="{question}" width="{qw}" height="{qh}" alt="Setup\'s archive question in sections: the client to archive, the personal data left out by category, the storage, and what Not now means." loading="lazy">\n      <figcaption>Setup\'s archive question ({VERSION}), with this PC\'s real client.</figcaption>\n    ' + s[end:]

    # ---- what's new, limitations (before Get started)
    whats_new = f"""<section id="whats-new">
  <div class="wrap">
    <h2>What's new in {VERSION}</h2>
    <p class="lead">October 2026. Settings, client archives and revisions of 8.0 and 8.5 are used as they are.</p>
    <div class="notes">
      <div class="note"><h3>File Metadata</h3><ul><li>View, edit, compare and batch-edit metadata of images, audio, video, documents, books and archives.</li><li>Batch preview, presets, CSV and JSON reports.</li><li>Verified saving with optional backups.</li></ul></div>
      <div class="note"><h3>Default clients</h3><ul><li>The <strong>Default Live Client</strong> (<code>%LOCALAPPDATA%\\Tibia\\packages\\Tibia</code>) and <strong>Default Test Client</strong> (<code>…\\TibiaExternal</code>) are recognized by name.</li><li>Setup shows each one's real state: available, incomplete or not installed.</li></ul></div>
      <div class="note"><h3>Clearer messages</h3><ul><li>Setup's archive question and summary are organized in sections, with copyable paths.</li><li>Every message box has a Copy button and can be resized.</li><li>"1 file", "3 files" instead of "file(s)".</li></ul></div>
    </div>
    <details>
      <summary>Known limitations</summary>
      <div class="body">
        <p>File Metadata reads but does not change PDFs, programs (changing them would break their digital signature), TAR archives, BMP/WebP/ICO and other image formats that would need re-encoding, camera raw files and Tibia client files.</p>
        <p>Files made by the Assets Editor and Client Converter, and rebuilt client installations, are checked file by file but have not been tested in a running game client.</p>
        <p>The program is not code-signed, so Windows SmartScreen may warn the first time it starts.</p>
      </div>
    </details>
  </div>
</section>

<section id="start" class="alt">"""
    s = replace(s, '<section id="start" class="alt">', whats_new)

    # ---- get started, requirements
    s = replace(s, "<li><strong>Choose your client</strong>The first-run setup finds your installed Tibia client. You can also browse to any client folder.</li>",
                "<li><strong>Choose your client</strong>The first-run setup shows the <em>Default Live Client</em> and <em>Default Test Client</em> that the Tibia launcher installed, with their version and state. You can also browse to any client folder.</li>")
    s = replace(s, "<p><strong>Portable:</strong> a single TibiaFoundry.exe that keeps its settings, logs and client archive in a “TibiaFoundry-Data” folder next to it. Good for a USB stick or keeping everything together.</p>",
                "<p><strong>Portable:</strong> a single TibiaFoundry.exe that keeps its settings, logs and client archive in a “TibiaFoundry-Data” folder next to it. Good for a USB stick or keeping everything together.</p>")
    s = replace(s, "<dt>Tibia</dt><dd>An installed Tibia client, or a copy of a client or assets folder.</dd>",
                "<dt>Tibia</dt><dd>An installed Tibia client — by default <code>%LOCALAPPDATA%\\Tibia\\packages\\Tibia</code> (live) and <code>…\\packages\\TibiaExternal</code> (test server) — or a copy of a client or assets folder anywhere.</dd>")
    s = replace(s, "<dt>Internet</dt><dd>Only for importing item data from TibiaWiki.</dd>", "<dt>Internet</dt><dd>Only for importing item data from TibiaWiki and the optional asset download.</dd>")

    # ---- download: honest about where builds are and what the repository holds
    s = replace(s, "<p class=\"lead\">Tibia Foundry is free. Releases are published on GitHub: open the latest release and download the zip for your edition. Each release lists SHA-256 checksums so you can check your download.</p>",
                "<p class=\"lead\">Tibia Foundry is free. Program builds are published as GitHub releases: open the latest release and download the zip for your edition (portable or folder). Each release lists SHA-256 checksums so you can check your download.</p>")
    s = replace(s, """      <a class="btn btn-ghost" href="https://github.com/RockyJLaRo/TibiaFoundry" data-track="repository" data-place="download">Source code on GitHub</a>
      <a class="btn btn-ghost" href="https://github.com/RockyJLaRo/TibiaFoundry" data-track="repository" data-place="download-browse">Browse repository</a>""",
                """      <a class="btn btn-ghost" href="https://github.com/RockyJLaRo/TibiaFoundry" data-track="repository" data-place="download">GitHub repository</a>""")
    s = replace(s, "<p class=\"muted meta\">Current version: <strong>8.5.0</strong> (October 2026). If no release is listed yet, check the repository's Releases section later.</p>",
                f"<p class=\"muted meta\">Current version: <strong>{VERSION}</strong> (October 2026). If the Releases page lists no download yet, the build has not been published there yet; the repository itself holds this website, not the program.</p>")

    # ---- help (before FAQ)
    help_section = """<section id="help" class="alt">
  <div class="wrap">
    <h2>Help and support</h2>
    <div class="notes">
      <div class="note"><h3>Something does not work</h3><ul><li><strong>Application → Diagnostics</strong> shows running and failed operations, the log, and a self-check.</li><li>Diagnostics → Report saves a privacy-safe report (user and computer names are masked).</li></ul></div>
      <div class="note"><h3>After a Tibia update</h3><ul><li>The <strong>Compatibility</strong> page shows which features are affected.</li><li>An archived client version keeps working (Clients page).</li></ul></div>
      <div class="note"><h3>Check a new PC</h3><ul><li>Run <code>TibiaFoundry.exe --self-test report</code>: it checks graphics, client files, sounds, file metadata and settings without opening a window.</li><li>Report problems on <a href="https://github.com/RockyJLaRo/TibiaFoundry/issues" data-track="issues" data-place="help">GitHub Issues</a>.</li></ul></div>
    </div>
  </div>
</section>

<section id="faq">"""
    s = replace(s, '<section id="faq">', help_section)

    # ---- FAQ
    s = replace(s, "<details><summary>Is there a portable version?</summary><div class=\"body\"><p>Yes. The portable edition is a single TibiaFoundry.exe that keeps all its data in a folder next to it. A folder edition is also available. Both have the same features.</p></div></details>",
                "<details><summary>Is there a portable version?</summary><div class=\"body\"><p>Yes. The portable edition is a single TibiaFoundry.exe that keeps all its data in a folder next to it. A folder edition is also available. Both have the same features.</p></div></details>\n"
                "    <details><summary>Can it edit the metadata of my photos, music or documents?</summary><div class=\"body\"><p>Yes. Tools → File Metadata edits titles, authors, copyright, keywords, dates, GPS positions, music tags and document properties of JPEG, PNG, TIFF, GIF, MP3, FLAC, Ogg, M4A, WAV, MP4, MKV, Word, Excel, PowerPoint, EPUB and more, one file or many at once. PDFs, programs and some image formats are read only. Every save is verified, and backups are on by default.</p></div></details>")
    s = replace(s, "<details><summary>Where can I find the source code?</summary><div class=\"body\"><p>On GitHub: <a href=\"https://github.com/RockyJLaRo/TibiaFoundry\" data-track=\"repository\" data-place=\"faq\">github.com/RockyJLaRo/TibiaFoundry</a></p></div></details>",
                "<details><summary>Where can I find the source code?</summary><div class=\"body\"><p>The source code is distributed as a source package (<code>TibiaFoundry-&lt;version&gt;-source.zip</code>) alongside the program builds. The <a href=\"https://github.com/RockyJLaRo/TibiaFoundry\" data-track=\"repository\" data-place=\"faq\">GitHub repository</a> holds this website; program builds are published on its Releases page.</p></div></details>")

    s = replace(s, "to count visits and clicks on its download and source-code links.", "to count visits and clicks on its download, repository and issue links.")

    # ---- footer and analytics events
    s = replace(s, "<strong>Tibia Foundry 8.5.0</strong>", f"<strong>Tibia Foundry {VERSION}</strong>")
    s = replace(s, """      <a href="https://github.com/RockyJLaRo/TibiaFoundry/releases" data-track="releases" data-place="footer">Releases</a>""",
                """      <a href="https://github.com/RockyJLaRo/TibiaFoundry/releases" data-track="releases" data-place="footer">Releases</a>
      <a href="https://github.com/RockyJLaRo/TibiaFoundry/issues" data-track="issues" data-place="footer">Issues</a>""")
    s = replace(s, "Tibia Foundry is an independent fan project and is not affiliated with or endorsed by CipSoft GmbH or Fandom.</p>",
                "Tibia Foundry is an independent fan project and is not affiliated with or endorsed by CipSoft GmbH or Fandom.</p>\n    <p><strong>Open-source components:</strong> .NET and WPF, Google.Protobuf, Magick.NET/ImageMagick, NAudio, NVorbis, Newtonsoft.Json, CommunityToolkit.Mvvm and TagLib# (LGPL-2.1). Their licenses are in THIRD-PARTY-NOTICES.txt in every download.</p>")
    s = replace(s, "var APP_VERSION = '8.5.0';", f"var APP_VERSION = '{VERSION}';")
    s = replace(s, "var EVENTS = { download: 'download_click', releases: 'releases_click', repository: 'repository_click' };",
                "var EVENTS = { download: 'download_click', releases: 'releases_click', repository: 'repository_click', issues: 'issues_click' };")

    # ---- identity: a toolkit for Tibia fansites, wikis and creators
    s = fansite_positioning(s)

    # ---- checks: one analytics tag, no stale version
    assert s.count("googletagmanager.com/gtag/js?id=G-J3EX3FB3PB") == 1 and s.count("gtag('config', 'G-J3EX3FB3PB')") == 1
    stale = [v for v in ["8.5.0"] if v in s.replace("8.5.0; File Metadata", "")]
    if stale:
        sys.exit("Stale version left: " + ", ".join(stale))
    os.makedirs(OUT_DIR, exist_ok=True)
    io.open(os.path.join(OUT_DIR, "index.html"), "w", encoding="utf-8", newline="\n").write(s)
    for name in ["CNAME", "robots.txt"]:
        data = open(os.path.join(HERE, "live-8.5.0-snapshot", name), "rb").read()
        open(os.path.join(OUT_DIR, name), "wb").write(data)
    io.open(os.path.join(OUT_DIR, "sitemap.xml"), "w", encoding="utf-8", newline="\n").write(
        '<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n  <url>\n    <loc>https://tibiafoundry.com/</loc>\n    <lastmod>2026-10-25</lastmod>\n  </url>\n</urlset>\n')
    print(f"{os.path.join(OUT_DIR, 'index.html')}: {len(s):,} characters")


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--positioning-only":
        # Applies only the positioning text to an already built page (no screenshots or ffmpeg needed).
        page = io.open(sys.argv[2], encoding="utf-8").read()
        io.open(sys.argv[2], "w", encoding="utf-8", newline="\n").write(fansite_positioning(page))
        print(f"{sys.argv[2]}: positioning text applied")
    elif len(sys.argv) == 4 and sys.argv[1] == "--release-8.7":
        page = io.open(sys.argv[2], encoding="utf-8").read()
        page = release_870(page, sys.argv[3])
        io.open(sys.argv[2], "w", encoding="utf-8", newline="\n").write(page)
        io.open(os.path.join(os.path.dirname(os.path.abspath(sys.argv[2])), "sitemap.xml"), "w", encoding="utf-8", newline="\n").write(
            '<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n  <url>\n    <loc>https://tibiafoundry.com/</loc>\n    <lastmod>2026-10-31</lastmod>\n  </url>\n</urlset>\n')
        print(f"{sys.argv[2]}: {RELEASE}, {len(page):,} characters")
    else:
        main()
