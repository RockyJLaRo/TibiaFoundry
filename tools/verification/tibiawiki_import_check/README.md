# TibiaWiki import: check of the published program

In 7.0.0 the import of TibiaWiki's item IDs failed: the wiki can no longer build its "Item IDs" page in time
("503 Backend fetch failed" after 16 s, every time), and the old fallback address answers programs with a browser
check. 7.0.1 reads the item articles the page is made of instead (`docs/11_Maintenance_Guide.md` §7.1).

This check repeats the failed steps on the real program, against the live TibiaWiki, the way a person does them:

* a portable copy of the given build in a new work folder (outputs also there, nothing else is touched);
* Item Catalog → *TibiaWiki item IDs* with no catalog yet → the program asks for a name; the suggestion is kept;
* the download: the status bar's progress is recorded until the preview appears (or a message, which fails the check);
* *Import* in the preview; the catalog is then read from disk: its TibiaWiki source must hold at least 1,000 records,
  each naming an identifier, and the downloaded list must be cached as read from the item articles;
* the status bar's message, closing the program, and the log (errors fail the check; warnings are listed).

## Running it

```powershell
powershell -ExecutionPolicy Bypass -File tools\verification\tibiawiki_import_check\run_check.ps1 -App dist\TibiaFoundry-7.0.1-win-x64
```

Without `-App` it builds the program (Release) first. The tool is built into `%TEMP%\TibiaFoundry-wikicheck`; the work
folder (`-Work`, default in `%TEMP%`) gets `import_check_report.txt`. The exit code is 0 when every check passed.
It needs the internet (about 190 requests to TibiaWiki's API, about a minute) and an unlocked desktop: the program's
window opens, but the mouse and keyboard are not used.
