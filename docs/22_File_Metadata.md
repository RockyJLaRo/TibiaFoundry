# File Metadata – Tibia Foundry 8.6

**Sound & Media → File Metadata** inspects, edits, compares and batch-edits the metadata of files: images, audio, video, Office documents, EPUB books, PDFs, archives, programs and Tibia client files. It writes only what a format can store safely, checks every save by reading the result back, and never leaves a file half-written.

## 1. Using it

| Step | How |
|---|---|
| Add files | *Add files…*, *Add folder…* (with *Include subfolders*), or drop files and folders anywhere on the page |
| Find files | Search (names, folders **and** metadata values); filter by format, *With unsaved changes* or *With errors or warnings*; sort by any column |
| Inspect | Select one file: its fields grouped by category (Description, Authorship and rights, Music, Image, Camera, Location, Document, Media properties, Archive, Program, Tibia client data, Technical, File system). Empty categories are not shown; *Show empty fields* lists every field the format has. |
| Edit | Type in a field. A blue bar marks an unsaved change; ↺ undoes it; 🗑 removes the field when you save. Read-only fields are grey and labelled *read only*. Invalid values are explained under the field and never saved. |
| Edit many | Select several files: a field shows its common value or *(different values)*. A value typed there is set in every selected file that can store it; the tooltip says how many can. |
| Save | *Save changes* lists what changes per format, what is removed, and what can happen (see §4), then saves. The result lists saved, unchanged, not applicable and failed files with the reason. |
| Batch edit | Build operations (*Set to*, *Add items*, *Remove items*, *Replace item*, *Remove field*, *Copy from reference*), *Preview for the selected files* (every file, with its changes and what it cannot take), then *Apply…*. Cancel stops between files. |
| Presets | Save the *Set to* operations (and keywords to add) as a preset; *Add to operations* applies it later. A preset only sets its own fields; keywords are added to the existing ones. Presets are validated and kept in the settings (`FileMetadata.presets`). |
| Compare | *Compare the selected files* (up to 20): one row per field, one column per file; *Only differences*, *Include file system properties*. |
| Export | *Export CSV…* / *Export JSON…*: path, format, reader, status (including the last save's result), errors, warnings and every field. CSV is UTF-8 with a byte-order mark; a cell that would start a spreadsheet formula is prefixed with `'`. |

## 2. Formats and what each can do

✔ supported, — not supported. Every format also has the **file system** part (times, read-only, hidden, archive attribute), which can be changed for any readable file.

| Format | Reader | Read | Edit | Remove fields | Notes |
|---|---|---|---|---|---|
| JPEG, TIFF, PNG, GIF | TagLib# | ✔ EXIF, XMP, IPTC (via TagLib#), dimensions | ✔ title, description, keywords, rating, authors, copyright, date taken, orientation, GPS | ✔ | Pixels are never re-encoded (tested). Camera maker, model and exposure are read only. A newly created EXIF block that would stay empty is left out (TagLib# would write it in a form it reads back as damaged). |
| MP3, FLAC, Ogg Vorbis, Opus, M4A/M4B, WAV, AIFF, WMA/ASF, WavPack | TagLib# | ✔ tags, duration, codec, bitrate, sample rate, channels | ✔ title, subtitle, description, comment, artists, album artists, composers, conductor, album, genres, year, track/tracks, disc/discs, BPM, lyrics, copyright, publisher | ✔ incl. embedded pictures | The tag block the format normally uses is created when a file has none (ID3v2, Vorbis comments, MP4, ASF, APE tags of WavPack, RIFF INFO). |
| MP4, M4V, MOV, MKV, MKA, WebM, AVI, WMV | TagLib# | ✔ tags, size, duration, codecs | ✔ same fields as audio | ✔ | Fields a container cannot store are refused when saved (the save is verified). |
| BMP, WebP, ICO, CUR, AVIF, HEIC, JXL, PSD, TGA, DDS | ImageMagick | ✔ size, frames, color space, EXIF/XMP/IPTC/ICC presence and main EXIF fields | — | — | Writing would re-encode the image. |
| Camera raw (DNG, CR2, NEF…), raw AAC, MPEG streams, Monkey's Audio (APE) | TagLib# | ✔ | — | — | Read only, so camera data cannot be damaged. |
| SVG | own | ✔ title, description, size, view box | ✔ title, description | ✔ | The XML text is rewritten (the drawing is unchanged; spacing inside tags may change). |
| DOCX, XLSX, PPTX (and macro/template forms) | own (Office Open XML) | ✔ core and extended properties | ✔ title, subject, authors, keywords, description, category, last modified by, status, language, revision, created, modified | ✔ | Only `docProps/core.xml` is rewritten; every other part is copied unchanged (tested by hash). Encrypted files and .doc/.xls/.ppt are not read. |
| EPUB | own | ✔ Dublin Core metadata, EPUB version | ✔ title, authors, keywords (subjects), description, publisher, language, date, rights | ✔ | The identifier is read only. Removed authors take their EPUB 3 refinements with them. `mimetype` stays first and uncompressed (checked after every save). |
| PDF | own | ✔ document information, version, pages, encryption, XMP presence | — | — | Read only: safe PDF writing needs a full PDF library. Information in compressed object streams or encrypted documents may be unreadable (said in the file's warnings). |
| ZIP (also JAR, APK, NUPKG, CBZ, AEC) | .NET | ✔ entries, sizes, dates, encryption, contents | ✔ archive comment | ✔ comment | Saving the comment rewrites the archive's directory; the packed files are copied unchanged (tested by hash). |
| TAR, TAR.GZ/TGZ | .NET | ✔ entries, sizes, owners, permissions, dates | — | — | |
| EXE, DLL and other Windows programs | .NET | ✔ version resources, machine, subsystem, .NET, signature presence | — | — | Editing resources would invalidate a digital signature; not offered. The signature is shown as present, not verified. |
| Tibia client files (.rcc, .lzma, .spr, .dat, catalogs) | Tibia Foundry's own readers | ✔ format details | — | — | Read only; the Assets Editor, RCC Editor and Client Converter edit them with full validation. |
| 7z, RAR and anything else | identification only | format name | — | — | Only the file system part. |

Tibia Foundry's own attribution in exported PNG and GIF files (*Tibia Foundry attribution*) is shown and is checked to survive every save.

## 3. How a save works

1. The values are normalized (lists, numbers, dates) and validated; fields the file cannot store are refused with the reason.
2. A file changed by another program since it was read (size, time and a hash of its first and last megabyte) is not overwritten: *Read again*, then edit again.
3. A read-only file is not changed unless *Read-only* is cleared in the same save.
4. The changes are written to a temporary copy in the same folder (`.<name>.tf-xxxxxxxx.<ext>`).
5. The copy is read back: every changed field must have the new value, every other field must be unchanged, and the reader must not find the copy damaged. Otherwise nothing is replaced and the result says why.
6. With *Back up files before saving* (on by default), a verified copy of the original goes to Tibia Foundry's backups (Settings → Files and folders).
7. The copy replaces the original in one step (Windows `ReplaceFile`, which keeps the file's attributes and creation time). With *Keep the modified time* (on by default) the modified time is restored.
8. File system changes are applied last.

Errors are reported per file: disk full, file open in another program, access denied, damaged file. A failed file is left as it was; a batch continues with the next file.

## 4. Warnings before saving

The confirmation lists, per format, what changes and what can happen beyond that: removed fields (authorship, copyright and GPS removals are named explicitly), removed embedded pictures, the orientation tag (pixels are not rotated), rewritten SVG text, rewritten archive directories, and the modified time (when *Keep the modified time* is off). Nothing is removed silently.

## 5. Recovery

* Every backup is a verified copy listed under **Scripts → Backup and Restore** (operation *File Metadata: save*), which puts files back.
* A temporary copy left by a crash (`.<name>.tf-xxxxxxxx.<ext>`) can be deleted; the original is never touched before the swap.

## 6. Limitations

* PDF, programs, TAR, raster formats other than JPEG/TIFF/PNG/GIF, camera raw files and Tibia client files are read only.
* IPTC-only fields that have no XMP or EXIF equivalent in TagLib# are not shown separately.
* Custom document properties (Office), EPUB 3 `meta` refinements other than those of removed authors, and Matroska tags beyond the common fields are kept but not shown.
* The comparison shows up to 20 files; exports cover any number.

## 7. Implementation

`Core/FileMetadata`: `MetadataEngine` (provider choice, load, verified save, batch run), `MetadataBatch` (plans, presets, comparison, CSV/JSON), `MetadataFields` (the shared field catalog: one key means the same thing in every format), `FileSystemMetadata`, and one `IMetadataProvider` per format family in `Providers/`. Each provider declares its capabilities and limitations. The page is `App/ViewModels/MetadataViewModel.cs` and `App/Views/Pages/MetadataPage.xaml`; the manifest is `BuiltInTools.FileMetadata`. Tests: `FileMetadataTests` (53 tests on real files made by ffmpeg in `Tests/Fixtures/Metadata`; every editable extension is written, read back and verified).

Dependency added: **TagLibSharp 2.3.0** (LGPL-2.1-only). The folder edition ships it as `TagLibSharp.dll`; the portable edition bundles it into `TibiaFoundry.exe`. Its license is in `THIRD-PARTY-NOTICES.txt`; the source package published with each release lets it be rebuilt with a modified TagLib#.
