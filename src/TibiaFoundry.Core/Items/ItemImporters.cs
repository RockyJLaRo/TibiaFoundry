using TibiaFoundry.Core.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tibia.Protobuf.Appearances;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Net;
using TibiaFoundry.Core.Wiki;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Items;

/// <summary>
/// Reads item records from the client and from files. Nothing is guessed: a value a source does not
/// give stays empty, an identifier's meaning comes from the source (never from its number), and a
/// line that cannot be used is reported with its number instead of being skipped silently.
/// </summary>
public static class ItemImporters
{
    /// <summary>The highest identifier of the 16-bit ID spaces (client objects, OTB IDs).</summary>
    public const uint MaxId = ushort.MaxValue;

    public const string ClientSourceId = "client";
    public const string TibiaWikiSourceId = "tibiawiki-item-ids";

    // ------------------------------------------------------------------ the official client

    /// <summary>
    /// Every object of the client's appearances: the client's name (official, verified) or, for
    /// objects the client does not name, a record without a name (the object exists; its name is
    /// unresolved). Market category and the properties that matter for comparisons come along.
    /// </summary>
    public static ItemImport FromClient(AppearanceDatabase db, string? clientVersion, string? path)
    {
        DateTime? fileTime = path != null && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        var source = new ItemSource
        {
            Id = ClientSourceId, Kind = SourceKind.OfficialClient, IdType = IdType.TibiaObject, IsOfficial = true,
            Name = L.F("ItemImporters.TibiaClient0", clientVersion ?? L.T("ItemImporters.VersionUnknown")), Distribution = "Tibia", Version = clientVersion,
            Origin = path, FileSha256 = path != null ? HashFromName(path) : null, SourceTimestampUtc = fileTime, ImportedAtUtc = DateTime.UtcNow,
            Notes = L.T("ItemImporters.NamesAndPropertiesFromTheClient"),
        };
        var records = new List<ItemObservation>(db.Count(AppearanceKind.Object));
        foreach (var a in db.List(AppearanceKind.Object))
        {
            string? name = string.IsNullOrWhiteSpace(a.Name) ? null : a.Name.Trim();
            records.Add(new ItemObservation
            {
                SourceId = source.Id, IdType = IdType.TibiaObject, Id = a.Id, Name = name,
                Category = a.Flags?.Market is { } market ? GameTables.MarketCategoryName(market.Category) : null,
                Flags = ClientFlags(a), Method = ObservationMethod.Import,
                Status = name != null ? ObservationStatus.Verified : ObservationStatus.Unresolved,
                Notes = string.IsNullOrWhiteSpace(a.Description) ? null : L.T("ItemImporters.ClientDescription") + a.Description.Trim(),
            });
        }
        return new ItemImport { Source = source with { RecordCount = records.Count }, Observations = records };
    }

    /// <summary>The properties of a client object that matter when comparing catalogs (null when none).</summary>
    public static IReadOnlyList<string>? ClientFlags(Appearance a)
    {
        var f = a.Flags;
        if (f == null) return null;
        var flags = new List<string>();
        if (f.Take) flags.Add("pickupable");
        if (f.Cumulative) flags.Add("stackable");
        if (f.Container) flags.Add("container");
        if (f.Liquidcontainer) flags.Add(ItemFlags.LiquidContainer);
        if (f.Market != null) flags.Add("market");
        if (f.Npcsaledata.Count > 0) flags.Add(ItemFlags.NpcTrade);
        if (f.Cyclopediaitem != null) flags.Add("cyclopedia");
        if (f.Corpse || f.PlayerCorpse) flags.Add("corpse");
        if (f.Write != null || f.WriteOnce != null) flags.Add("writable");
        if (f.Usable || f.Forceuse || f.Multiuse) flags.Add("usable");
        if (f.Unmove) flags.Add("unmovable");
        if (f.Hang) flags.Add("hangable");
        if (f.Wrap || f.Unwrap) flags.Add("wrappable");
        return flags.Count == 0 ? null : flags;
    }

    /// <summary>The SHA-256 in a client file name ("appearances-&lt;sha256&gt;.dat"), or null.</summary>
    private static string? HashFromName(string path) => Compatibility.CompatibilityChecker.ContentHashFromName(Path.GetFileName(path));

    // ------------------------------------------------------------------ TibiaWiki

    /// <summary>
    /// TibiaWiki's "Item IDs" table: object ID → article title. Titles are not necessarily the in-game
    /// names (TibiaWiki capitalizes and disambiguates them), so they are marked as titles and unverified.
    /// </summary>
    public static ItemImport FromTibiaWiki(WikiItemIds table)
    {
        var source = new ItemSource
        {
            Id = TibiaWikiSourceId, Kind = SourceKind.TibiaWiki, IdType = IdType.TibiaObject, Name = L.T("ItemImporters.TibiaWikiItemIDs"), Distribution = "TibiaWiki",
            Origin = ExternalEndpoints.ItemIdsRenderUrl.Replace("?action=render", "", StringComparison.Ordinal), SourceTimestampUtc = table.FetchedAtUtc,
            ImportedAtUtc = DateTime.UtcNow,
            Notes = L.T("ItemImporters.ArticleTitlesOfTibiaWikiSItem") +
                    (table.Notice != null ? " " + table.Notice : ""),
        };
        var records = table.Names.OrderBy(kv => kv.Key).Select(kv => new ItemObservation
        {
            SourceId = source.Id, IdType = IdType.TibiaObject, Id = kv.Key, Name = kv.Value.Trim(), NameIsTitle = true,
            Method = ObservationMethod.Import, Status = ObservationStatus.Unverified,
        }).ToList();
        var problems = table.Log.Select(l => new ImportProblem(0, l)).ToList();
        return new ItemImport { Source = source with { RecordCount = records.Count }, Observations = records, Problems = problems };
    }

    // ------------------------------------------------------------------ "[id] name" lists

    /// <summary>
    /// Reads "[id] name" lines (the format of GetItemNames and LapisItemEditor). A leading "a " or
    /// "an " becomes the article. Blank lines and lines starting with '#', ';' or "//" are comments.
    /// The meaning of the numbers is not in the file: <paramref name="idType"/> says what they are.
    /// </summary>
    public static ItemImport FromNameList(string text, ItemSource source, IdType idType)
    {
        var records = new List<ItemObservation>();
        var problems = new List<ImportProblem>();
        var seen = new Dictionary<uint, int>();
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r').Trim();
            if (i == 0) line = line.TrimStart('﻿');
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';') || line.StartsWith("//", StringComparison.Ordinal)) continue;
            int close = line.IndexOf(']');
            if (!line.StartsWith('[') || close < 2)
            {
                problems.Add(new ImportProblem(i + 1, L.F("ItemImporters.NotOfTheFormIdName", Shorten(line))));
                continue;
            }
            if (!uint.TryParse(line[1..close].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint id))
            {
                problems.Add(new ImportProblem(i + 1, L.F("ItemImporters.0IsNotAnID", Shorten(line[1..close]))));
                continue;
            }
            if (id == 0 || id > MaxId)
            {
                problems.Add(new ImportProblem(i + 1, L.F("ItemImporters.ID0IsOutside11", id, MaxId)));
                continue;
            }
            string rest = line[(close + 1)..].Trim();
            if (rest.Length == 0)
            {
                problems.Add(new ImportProblem(i + 1, L.F("ItemImporters.ID0HasNoName", id)));
                continue;
            }
            if (seen.TryGetValue(id, out int first))
            {
                problems.Add(new ImportProblem(i + 1, L.F("ItemImporters.ID0AppearsAgainFirstOn", id, first)));
                continue;
            }
            seen[id] = i + 1;
            var (article, name) = ItemNames.SplitArticle(rest);
            records.Add(new ItemObservation
            {
                SourceId = source.Id, IdType = idType, Id = id, Name = name, Article = article,
                Method = ObservationMethod.Import, Status = ObservationStatus.Unverified,
            });
        }
        return new ItemImport { Source = source with { IdType = idType, RecordCount = records.Count }, Observations = records, Problems = problems };
    }

    // ------------------------------------------------------------------ third-party item data: items.otb

    /// <summary>
    /// The items of an items.otb as records of the file's own item IDs (group and flags; usually no names,
    /// those are in items.xml) and, for each, the client ID the file maps it to: an exact mapping for the
    /// client version the file was made for. The file's own IDs are never treated as client IDs.
    /// </summary>
    public static ItemImport FromOtb(OtbFile otb, ItemSource source)
    {
        source = source with
        {
            Kind = SourceKind.OtItemsOtb, IdType = IdType.OtbServer, IsOfficial = false, Version = source.Version ?? otb.ClientVersion,
            Distribution = source.Distribution ?? L.T("ItemImporters.ThirdPartyData"),
            Notes = L.F("ItemImporters.0ItemsOtbIDsMappedTo", otb, otb.ClientVersion ?? L.T("ItemImporters.VersionNotStated")),
        };
        var records = new List<ItemObservation>(otb.Items.Count);
        var mappings = new List<IdMapping>(otb.Items.Count);
        int withoutClient = 0;
        foreach (var item in otb.Items)
        {
            records.Add(new ItemObservation
            {
                SourceId = source.Id, IdType = IdType.OtbServer, Id = item.ServerId, Name = item.Name, Article = item.Article,
                Category = item.Group is OtbItemGroup.None or OtbItemGroup.Deprecated ? null : item.Group.ToString(), Flags = OtbFile.FlagsOf(item),
                Method = ObservationMethod.Import, Status = item.Name != null ? ObservationStatus.Unverified : ObservationStatus.Unresolved,
            });
            if (item.ClientId is { } client)
                mappings.Add(new IdMapping(IdType.OtbServer, item.ServerId, source.Version, IdType.OtbClient, client, source.Version, source.Id, MappingConfidence.Exact));
            else withoutClient++;
        }
        var problems = otb.Problems.Select(p => new ImportProblem(0, p)).ToList();
        if (withoutClient > 0) problems.Add(new ImportProblem(0, L.F("ItemImporters.0N0ItemsOtbIDsHave", withoutClient)));
        return new ItemImport { Source = source with { RecordCount = records.Count }, Observations = records, Mappings = mappings, Problems = problems };
    }

    // ------------------------------------------------------------------ third-party item data: items.xml

    /// <summary>Longest fromid–toid range accepted from one item element.</summary>
    public const int MaxXmlRange = 10_000;

    /// <summary>
    /// The items of a third-party items.xml item list:
    /// <c>&lt;item id|fromid+toid name article plural&gt;</c> with <c>&lt;attribute key value&gt;</c> children.
    /// What the IDs mean is not in the file: some lists use client object IDs, others use the IDs of an
    /// items.otb (mapped to client IDs by that file) — <paramref name="idType"/> says which.
    /// A later definition of the same ID replaces the earlier one, as the format intends; it is reported.
    /// IDs below 100 are fluid types, not objects, and are skipped. External entities are never resolved.
    /// </summary>
    public static ItemImport FromItemsXml(Stream xml, ItemSource source, IdType idType)
    {
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
        System.Xml.Linq.XDocument doc;
        try
        {
            using var reader = System.Xml.XmlReader.Create(xml, settings);
            doc = System.Xml.Linq.XDocument.Load(reader, System.Xml.Linq.LoadOptions.SetLineInfo);
        }
        catch (System.Xml.XmlException ex)
        {
            return new ItemImport { Source = source with { IdType = idType }, Observations = [], Problems = [new ImportProblem(ex.LineNumber, L.T("ItemImporters.TheFileIsNotValidXML") + ex.Message)] };
        }
        var problems = new List<ImportProblem>();
        var byId = new Dictionary<uint, (ItemObservation Record, int Line)>();
        int fluids = 0;
        if (doc.Root?.Name.LocalName != "items")
            problems.Add(new ImportProblem(1, L.F("ItemImporters.TheRootElementIs0Not", doc.Root?.Name.LocalName)));
        foreach (var element in doc.Root?.Elements() ?? [])
        {
            int line = ((System.Xml.IXmlLineInfo)element).LineNumber;
            if (element.Name.LocalName != "item") continue;
            string? Attr(string name) => element.Attribute(name)?.Value;
            uint from, to;
            if (Attr("id") is { } idText)
            {
                if (!uint.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out from)) { problems.Add(new ImportProblem(line, L.F("ItemImporters.Id0IsNotANumber", Shorten(idText)))); continue; }
                to = from;
            }
            else if (Attr("fromid") is { } fromText && Attr("toid") is { } toText)
            {
                if (!uint.TryParse(fromText, NumberStyles.None, CultureInfo.InvariantCulture, out from) || !uint.TryParse(toText, NumberStyles.None, CultureInfo.InvariantCulture, out to))
                {
                    problems.Add(new ImportProblem(line, L.F("ItemImporters.FromidToid01AreNot", Shorten(fromText), Shorten(toText))));
                    continue;
                }
                if (to < from || to - from >= MaxXmlRange) { problems.Add(new ImportProblem(line, L.F("ItemImporters.ImplausibleRange01", from, to))); continue; }
            }
            else
            {
                problems.Add(new ImportProblem(line, L.T("ItemImporters.AnItemWithoutIdOrWithout")));
                continue;
            }
            string? name = Attr("name")?.Trim();
            string? article = Attr("article")?.Trim();
            string? plural = Attr("plural")?.Trim();
            string? category = null, description = null;
            var flags = new List<string>();
            foreach (var attribute in element.Elements().Where(e => e.Name.LocalName == "attribute"))
            {
                string key = (attribute.Attribute("key")?.Value ?? "").Trim().ToLowerInvariant();
                string value = (attribute.Attribute("value")?.Value ?? "").Trim();
                switch (key)
                {
                    case "type" when value.Length > 0:
                        string type = value.ToLowerInvariant() switch { "magicfield" => ItemFlags.MagicField, "trashholder" => ItemFlags.TrashHolder, var t => t };
                        if (!flags.Contains(type)) flags.Add(type);
                        break;
                    case "containersize" when !flags.Contains("container"): flags.Add("container"); break;
                    case "floorchange" when !flags.Contains(ItemFlags.FloorChange): flags.Add(ItemFlags.FloorChange); break;
                    case "writeable" or "maxtextlen" when !flags.Contains("writable"): flags.Add("writable"); break;
                    case "primarytype" when value.Length > 0: category = value; break;
                    case "description" when value.Length > 0: description = value; break;
                }
            }
            for (uint id = from; id <= to; id++)
            {
                if (id < 100) { fluids++; continue; }
                if (id > MaxId) { problems.Add(new ImportProblem(line, L.F("ItemImporters.ID0IsAbove1", id, MaxId))); break; }
                var record = new ItemObservation
                {
                    SourceId = source.Id, IdType = idType, Id = id, Name = string.IsNullOrEmpty(name) ? null : name, Article = string.IsNullOrEmpty(article) ? null : article,
                    Plural = string.IsNullOrEmpty(plural) ? null : plural, Category = category, Flags = flags.Count == 0 ? null : flags.ToArray(),
                    Method = ObservationMethod.Import, Status = string.IsNullOrEmpty(name) ? ObservationStatus.Unresolved : ObservationStatus.Unverified,
                    Notes = description,
                };
                if (byId.TryGetValue(id, out var earlier))
                {
                    // As the format intends: the later definition changes what it states and keeps the rest.
                    problems.Add(new ImportProblem(line, L.F("ItemImporters.ID0IsDefinedAgainFirst", id, earlier.Line)));
                    var e = earlier.Record;
                    record = record with
                    {
                        Name = record.Name ?? e.Name, Article = record.Article ?? e.Article, Plural = record.Plural ?? e.Plural, Category = record.Category ?? e.Category,
                        Notes = record.Notes ?? e.Notes, Flags = (e.Flags ?? []).Concat(record.Flags ?? []).Distinct().ToArray() is { Length: > 0 } merged ? merged : null,
                    };
                    record = record with { Status = record.Name == null ? ObservationStatus.Unresolved : ObservationStatus.Unverified };
                }
                byId[id] = (record, line);
            }
        }
        if (fluids > 0) problems.Add(new ImportProblem(0, L.F("ItemImporters.0WithIDsBelow100Fluid", L.Count(fluids, "Noun.definition"))));
        var records = byId.OrderBy(kv => kv.Key).Select(kv => kv.Value.Record).ToList();
        return new ItemImport { Source = source with { IdType = idType, RecordCount = records.Count }, Observations = records, Problems = problems };
    }

    // ------------------------------------------------------------------ CSV

    /// <summary>
    /// Reads a CSV file with a header line. Columns are found by name: "ID" (required), "Name",
    /// "Article", "Plural", "Category", "Flags" (separated by ';'), "ID type" (when present it must match
    /// <paramref name="idType"/>). The catalog's own exports are read back unchanged. Comment lines
    /// starting with '#' before the header are skipped. With <paramref name="headerless"/>, the file is a
    /// list of names and IDs without a header (see <see cref="HeaderlessCsv"/>).
    /// </summary>
    public static ItemImport FromCsv(string text, ItemSource source, IdType idType, HeaderlessCsv? headerless = null)
    {
        if (headerless is { } layout) return FromHeaderlessCsv(text, source, idType, layout);
        var problems = new List<ImportProblem>();
        var rows = CsvText.Parse(text);
        int headerIndex = rows.FindIndex(r => !r.Fields[0].TrimStart().StartsWith('#') && r.Fields.Any(f => f.Length > 0));
        if (headerIndex < 0) return new ItemImport { Source = source with { IdType = idType }, Observations = [], Problems = [new ImportProblem(0, L.T("ItemImporters.TheFileHasNoHeaderLine"))] };
        var header = rows[headerIndex].Fields.Select(h => Normalize(h)).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Contains(h));
        int cId = Col("id"), cName = Col("name"), cArticle = Col("article"), cPlural = Col("plural"), cCategory = Col("category"), cFlags = Col("flags"), cType = Col("idtype");
        if (cId < 0)
            return new ItemImport { Source = source with { IdType = idType }, Observations = [], Problems = [new ImportProblem(rows[headerIndex].Line, L.T("ItemImporters.TheHeaderHasNoIDColumn"))] };
        var records = new List<ItemObservation>();
        var seen = new Dictionary<uint, int>();
        foreach (var row in rows.Skip(headerIndex + 1))
        {
            string Get(int c) => c >= 0 && c < row.Fields.Count ? CsvText.Unguard(row.Fields[c]).Trim() : "";
            if (row.Fields.All(f => f.Length == 0)) continue;
            string idText = Get(cId);
            if (!uint.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out uint id) || id == 0 || id > MaxId)
            {
                problems.Add(new ImportProblem(row.Line, L.F("ItemImporters.0IsNotAnIDBetween", Shorten(idText), MaxId)));
                continue;
            }
            if (cType >= 0 && Get(cType) is { Length: > 0 } t && !(Enum.TryParse<IdType>(t, true, out var rowType) && rowType == idType))
            {
                problems.Add(new ImportProblem(row.Line, L.F("ItemImporters.ID0IsOfType1", id, t, idType)));
                continue;
            }
            if (seen.TryGetValue(id, out int first))
            {
                problems.Add(new ImportProblem(row.Line, L.F("ItemImporters.ID0AppearsAgainFirstOn", id, first)));
                continue;
            }
            seen[id] = row.Line;
            string name = Get(cName), article = Get(cArticle), plural = Get(cPlural), category = Get(cCategory), flags = Get(cFlags);
            records.Add(new ItemObservation
            {
                SourceId = source.Id, IdType = idType, Id = id, Name = name.Length > 0 ? name : null, Article = article.Length > 0 ? article : null,
                Plural = plural.Length > 0 ? plural : null, Category = category.Length > 0 ? category : null,
                Flags = flags.Length > 0 ? flags.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null,
                Method = ObservationMethod.Import, Status = name.Length > 0 ? ObservationStatus.Unverified : ObservationStatus.Unresolved,
            });
        }
        return new ItemImport { Source = source with { IdType = idType, RecordCount = records.Count }, Observations = records, Problems = problems };
    }

    private static string Normalize(string header) => new(CsvText.Unguard(header).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Lines of a CSV file that hold data or a header (not empty, not a '#' comment).</summary>
    private static IEnumerable<CsvText.Row> ContentRows(string text) =>
        CsvText.Parse(text).Where(r => !r.Fields[0].TrimStart().StartsWith('#') && r.Fields.Any(f => f.Trim().Length > 0));

    private static bool IsId(string field, out uint id) =>
        uint.TryParse(CsvText.Unguard(field).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id is > 0 and <= MaxId;

    /// <summary>True when the file's first line names its columns and one of them is "ID" (the form <see cref="FromCsv"/> reads without a layout).</summary>
    public static bool CsvHasHeader(string text) => ContentRows(text).FirstOrDefault() is { } first && first.Fields.Any(f => Normalize(f) == "id");

    /// <summary>
    /// The layout of a list of names and IDs without a header: the one that gives an ID on at least 3
    /// of 4 of its first 50 lines (and more often than the other). Null when neither does. Only a
    /// suggestion: the user chooses.
    /// </summary>
    public static HeaderlessCsv? GuessHeaderless(string text)
    {
        var rows = ContentRows(text).Where(r => r.Fields.Count >= 2).Take(50).ToList();
        if (rows.Count == 0) return null;
        int last = rows.Count(r => IsId(r.Fields[^1], out _)), first = rows.Count(r => IsId(r.Fields[0], out _));
        if (last * 4 >= rows.Count * 3 && last > first) return HeaderlessCsv.NameThenId;
        if (first * 4 >= rows.Count * 3 && first > last) return HeaderlessCsv.IdThenName;
        return null;
    }

    private static ItemImport FromHeaderlessCsv(string text, ItemSource source, IdType idType, HeaderlessCsv layout)
    {
        var problems = new List<ImportProblem>();
        var records = new List<ItemObservation>();
        var seen = new Dictionary<uint, int>();
        foreach (var row in ContentRows(text))
        {
            // The ID is one end of the line; the rest is the name, commas included (names are often not quoted).
            var fields = row.Fields.Select(CsvText.Unguard).ToList();
            string idText = (layout == HeaderlessCsv.NameThenId ? fields[^1] : fields[0]).Trim();
            string name = string.Join(",", layout == HeaderlessCsv.NameThenId ? fields.Take(fields.Count - 1) : fields.Skip(1)).Trim();
            if (fields.Count < 2 || !IsId(idText, out uint id))
            {
                problems.Add(new ImportProblem(row.Line, L.F("ItemImporters.0IsNotAnIDBetween", Shorten(idText), MaxId)));
                continue;
            }
            if (seen.TryGetValue(id, out int firstLine))
            {
                problems.Add(new ImportProblem(row.Line, L.F("ItemImporters.ID0AppearsAgainFirstOn", id, firstLine)));
                continue;
            }
            seen[id] = row.Line;
            records.Add(new ItemObservation
            {
                SourceId = source.Id, IdType = idType, Id = id, Name = name.Length > 0 ? name : null,
                Method = ObservationMethod.Import, Status = name.Length > 0 ? ObservationStatus.Unverified : ObservationStatus.Unresolved,
            });
        }
        return new ItemImport { Source = source with { IdType = idType, RecordCount = records.Count }, Observations = records, Problems = problems };
    }

    // ------------------------------------------------------------------ JSON (catalog exports)

    /// <summary>
    /// Reads a catalog export (<see cref="ItemExports.ToJson"/>): one import per source in the file,
    /// with the source's own description and records (sources of kinds added one by one become
    /// ordinary imported sources). Throws <see cref="InvalidDataException"/> for other JSON files.
    /// </summary>
    public static IReadOnlyList<ItemImport> FromCatalogJson(string json, string origin)
    {
        ItemExports.CatalogExport? export;
        try
        {
            export = JsonSerializer.Deserialize<ItemExports.CatalogExport>(json, ItemCatalogStore.Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("This is not a Tibia Foundry item catalog export: " + ex.Message, ex);
        }
        if (export == null || export.Format != ItemExports.JsonFormatName)
            throw new InvalidDataException("This is not a Tibia Foundry item catalog export (\"format\" is missing or different).");
        if (export.FormatVersion > ItemExports.JsonFormatVersion)
            throw new InvalidDataException($"The export was written by a newer Tibia Foundry (format {export.FormatVersion}).");
        var imports = new List<ItemImport>();
        foreach (var source in export.Sources ?? [])
        {
            var records = (export.Observations ?? []).Where(o => o.SourceId == source.Id).ToList();
            var kind = ItemCatalogStore.IsJournalSource(source) ? SourceKind.Json : source.Kind;
            imports.Add(new ItemImport
            {
                Source = source with
                {
                    Kind = kind, RecordCount = records.Count, ImportedAtUtc = DateTime.UtcNow,
                    Notes = L.F("ItemImporters.0ImportedFrom1Catalog2", source.Notes, origin, export.Catalog).Trim(),
                },
                Observations = records,
                Mappings = (export.Mappings ?? []).Where(m => m.SourceId == source.Id).ToList(),
            });
        }
        return imports;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A new source for a file (name list, CSV…): the ID is derived from the file name and made unique in the catalog.</summary>
    public static ItemSource SourceForFile(string path, SourceKind kind, IdType idType, ItemCatalog? catalog, string? version = null, string? distribution = null)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        string baseId = ItemCatalogStore.SafeId(kind.ToString().ToLowerInvariant() + "-" + stem);
        string id = baseId;
        for (int i = 2; catalog?.Source(id) != null; i++) id = $"{baseId}-{i}";
        return new ItemSource
        {
            Id = id, Kind = kind, IdType = idType, Name = Path.GetFileName(path), Origin = path, Version = version, Distribution = distribution,
            FileSha256 = File.Exists(path) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) : null,
            SourceTimestampUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null, ImportedAtUtc = DateTime.UtcNow,
        };
    }

    private static string Shorten(string s) => s.Length <= 60 ? s : s[..57] + "…";
}

/// <summary>The two layouts of a list of names and IDs without a header line.</summary>
public enum HeaderlessCsv
{
    /// <summary>"name,id" (the market tracker's items.csv): the ID is the last value of each line, everything before it the name.</summary>
    NameThenId,
    /// <summary>"id,name": the ID is the first value of each line, everything after it the name.</summary>
    IdThenName,
}

/// <summary>A minimal RFC 4180 CSV reader (quoted fields, doubled quotes, line breaks inside quotes).</summary>
public static class CsvText
{
    public sealed record Row(int Line, List<string> Fields);

    public static List<Row> Parse(string text)
    {
        var rows = new List<Row>();
        var field = new StringBuilder();
        var fields = new List<string>();
        bool quoted = false, any = false;
        int line = 1, rowLine = 1;
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else { if (c == '\n') line++; field.Append(c); }
                continue;
            }
            switch (c)
            {
                case '"' when field.Length == 0: quoted = true; any = true; break;
                case ',': fields.Add(field.ToString()); field.Clear(); any = true; break;
                case '\r': break;
                case '\n':
                    fields.Add(field.ToString()); field.Clear();
                    if (any || fields.Any(f => f.Length > 0)) rows.Add(new Row(rowLine, fields));
                    fields = []; any = false; line++; rowLine = line;
                    break;
                default: field.Append(c); any = true; break;
            }
        }
        if (any || field.Length > 0)
        {
            fields.Add(field.ToString());
            rows.Add(new Row(rowLine, fields));
        }
        return rows;
    }

    /// <summary>Removes the apostrophe <c>ReportWriter.CsvCell</c> puts before text a spreadsheet would run as a formula.</summary>
    public static string Unguard(string cell) =>
        cell.Length > 1 && cell[0] == '\'' && cell[1] is '=' or '+' or '-' or '@' or '\t' or '\r' ? cell[1..] : cell;
}
