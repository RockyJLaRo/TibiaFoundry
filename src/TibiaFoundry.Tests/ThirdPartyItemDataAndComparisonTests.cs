using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Items;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.Tests;

/// <summary>
/// Third-party item data in the Item Catalog (7.0): items.otb and items.xml are read as their formats
/// intend, items.otb IDs are compared with client IDs only through an items.otb, comparisons report
/// (never merge), decisions resolve conflicts and can be withdrawn, and workbooks are valid Open XML.
/// Only synthetic files are used here; real third-party files (GPL data, never bundled) are checked by
/// the opt-in test at the end.
/// </summary>
public sealed class ThirdPartyItemDataAndComparisonTests(ITestOutputHelper output)
{
    // ------------------------------------------------------------------ a small items.otb writer

    /// <summary>Builds an items.otb: (items.otb ID, client ID or null, group, flags, name or null).</summary>
    private static byte[] Otb(string description, params (ushort Server, ushort? Client, OtbItemGroup Group, uint Flags, string? Name)[] items)
    {
        var bytes = new List<byte> { 0, 0, 0, 0 };
        void Escaped(IEnumerable<byte> data)
        {
            foreach (byte b in data)
            {
                if (b is 0xFD or 0xFE or 0xFF) bytes.Add(0xFD);
                bytes.Add(b);
            }
        }
        bytes.Add(0xFE);
        bytes.Add(0);
        var root = new List<byte>(BitConverter.GetBytes(0u)) { 0x01 };
        root.AddRange(BitConverter.GetBytes((ushort)140));
        root.AddRange(BitConverter.GetBytes(3u));
        root.AddRange(BitConverter.GetBytes(65u));
        root.AddRange(BitConverter.GetBytes(62u));
        var text = new byte[128];
        Encoding.Latin1.GetBytes(description).CopyTo(text, 0);
        root.AddRange(text);
        Escaped(root);
        foreach (var (server, client, group, flags, name) in items)
        {
            bytes.Add(0xFE);
            bytes.Add((byte)group);
            var props = new List<byte>(BitConverter.GetBytes(flags)) { 0x10 };
            props.AddRange(BitConverter.GetBytes((ushort)2));
            props.AddRange(BitConverter.GetBytes(server));
            if (client is { } c)
            {
                props.Add(0x11);
                props.AddRange(BitConverter.GetBytes((ushort)2));
                props.AddRange(BitConverter.GetBytes(c));
            }
            if (name != null)
            {
                byte[] n = Encoding.Latin1.GetBytes(name);
                props.Add(0x12);
                props.AddRange(BitConverter.GetBytes((ushort)n.Length));
                props.AddRange(n);
            }
            Escaped(props);
            bytes.Add(0xFF);
        }
        bytes.Add(0xFF);
        return bytes.ToArray();
    }

    private const uint Pickupable = 1 << 5, Movable = 1 << 6, Stackable = 1 << 7;

    private static ItemSource Source(string id, SourceKind kind, IdType type, bool official = false, string? version = null) => new()
    {
        Id = id, Kind = kind, Name = id, IdType = type, IsOfficial = official, Version = version, ImportedAtUtc = DateTime.UtcNow,
    };

    private static ItemImport FromXml(string xml, ItemSource source, IdType type) =>
        ItemImporters.FromItemsXml(new MemoryStream(Encoding.Latin1.GetBytes(xml)), source, type);

    // ------------------------------------------------------------------ items.otb

    [Fact]
    public void ItemsOtbIsReadWithItsVersionMappingsAndEscapedBytes()
    {
        // items.otb ID 254 and client ID 0xFDFE contain bytes that must be escaped in the file.
        byte[] data = Otb("OTB 3.65.62-13.10",
            (100, 100, OtbItemGroup.Ground, 0, null),
            (254, 0xFDFE, OtbItemGroup.None, Movable | Pickupable | Stackable, "gold coin"),
            (1987, 2853, OtbItemGroup.Container, Movable | Pickupable, null),
            (5000, null, OtbItemGroup.Door, 0, null),
            (254, 9, OtbItemGroup.None, 0, null));
        var otb = OtbFile.Parse(data);
        Assert.Equal("13.10", otb.ClientVersion);
        Assert.Equal((3u, 65u, 62u), (otb.MajorVersion, otb.MinorVersion, otb.Build));
        Assert.Equal([100, 254, 1987, 5000], otb.Items.Select(i => (int)i.ServerId));
        Assert.Equal((ushort)0xFDFE, otb.Items[1].ClientId);
        Assert.Contains("appears again", Assert.Single(otb.Problems));

        var import = ItemImporters.FromOtb(otb, Source("otb", SourceKind.OtItemsOtb, IdType.OtbServer));
        Assert.All(import.Observations, o => Assert.Equal(IdType.OtbServer, o.IdType));
        Assert.Equal("13.10", import.Source.Version);
        Assert.Equal(3, import.Mappings.Count);   // 5000 has no client ID: no mapping
        Assert.All(import.Mappings, m => Assert.Equal((IdType.OtbServer, IdType.OtbClient, MappingConfidence.Exact, "13.10"), (m.FromType, m.ToType, m.Confidence, m.ToVersion)));
        Assert.Contains(import.Problems, p => p.Message.Contains("no client ID"));
        var gold = import.Observations.Single(o => o.Id == 254);
        Assert.Equal("gold coin", gold.Name);
        Assert.Equal(["pickupable", "stackable"], gold.Flags!);
        Assert.Contains("container", import.Observations.Single(o => o.Id == 1987).Flags!);
        Assert.Contains("door", import.Observations.Single(o => o.Id == 5000).Flags!);
        Assert.Contains("unmovable", import.Observations.Single(o => o.Id == 100).Flags!);

        Assert.Throws<InvalidDataException>(() => OtbFile.Parse([1, 0, 0, 0, 0xFE, 0]));
        Assert.Throws<InvalidDataException>(() => OtbFile.Parse(data[..^20]));   // cut off inside a node
    }

    // ------------------------------------------------------------------ items.xml

    [Fact]
    public void ItemsXmlIsReadAsTheFormatIntends()
    {
        string xml = """
            <?xml version="1.0" encoding="ISO-8859-1"?>
            <items>
                <item id="1" name="water"/>
                <item id="100" name="void"/>
                <item fromid="101" toid="103" article="a" name="grass"><attribute key="primarytype" value="grounds"/></item>
                <item id="1210" name="closed door"><attribute key="type" value="door"/><attribute key="description" value="It is locked."/></item>
                <item id="1987" article="a" name="bag" plural="bags"><attribute key="containerSize" value="8"/></item>
                <item id="1987"><attribute key="type" value="container"/></item>
                <item id="2000" name="Leprechaun's café"/>
                <item id="abc" name="broken"/>
                <item name="no id"/>
                <item fromid="10" toid="99999" name="too much"/>
            </items>
            """;
        var import = FromXml(xml, Source("clientids", SourceKind.OtItemsXml, IdType.TibiaObject), IdType.TibiaObject);
        Assert.Equal([100u, 101u, 102u, 103u, 1210u, 1987u, 2000u], import.Observations.Select(o => o.Id));
        Assert.All(import.Observations, o => Assert.Equal(IdType.TibiaObject, o.IdType));
        Assert.Equal(("a", "grass", "grounds"), (import.Observations[1].Article, import.Observations[1].Name, import.Observations[1].Category));
        var door = import.Observations.Single(o => o.Id == 1210);
        Assert.Equal(["door"], door.Flags!);
        Assert.Equal("It is locked.", door.Notes);
        // The second definition of 1987 adds what it states and keeps the rest (as the format intends).
        var bag = import.Observations.Single(o => o.Id == 1987);
        Assert.Equal(("bag", "bags", "a"), (bag.Name, bag.Plural, bag.Article));
        Assert.Equal(["container"], bag.Flags!);
        Assert.Equal("Leprechaun's café", import.Observations.Single(o => o.Id == 2000).Name);   // ISO-8859-1 as declared
        Assert.Contains(import.Problems, p => p.Message.Contains("defined again"));
        Assert.Contains(import.Problems, p => p.Message.Contains("\"abc\" is not a number"));
        Assert.Contains(import.Problems, p => p.Message.Contains("without id"));
        Assert.Contains(import.Problems, p => p.Message.Contains("implausible range"));
        Assert.Contains(import.Problems, p => p.Message.Contains("below 100"));
        Assert.All(import.Problems.Where(p => p.Message.Contains("not a number")), p => Assert.True(p.Line > 1));

        // A document type declaration (entities) is refused, not resolved.
        var hostile = FromXml("<?xml version=\"1.0\"?><!DOCTYPE items [<!ENTITY x SYSTEM \"file:///c:/windows/win.ini\">]><items><item id=\"100\" name=\"&x;\"/></items>",
            Source("x", SourceKind.OtItemsXml, IdType.TibiaObject), IdType.TibiaObject);
        Assert.Empty(hostile.Observations);
        Assert.Contains("not valid XML", Assert.Single(hostile.Problems).Message);
        // Also an internal entity, which XML would otherwise expand (the way to "billion laughs" attacks).
        var expanding = FromXml("<?xml version=\"1.0\"?><!DOCTYPE items [<!ENTITY x \"expanded\">]><items><item id=\"100\" name=\"&x;\"/></items>",
            Source("y", SourceKind.OtItemsXml, IdType.TibiaObject), IdType.TibiaObject);
        Assert.Empty(expanding.Observations);
        Assert.Contains("not valid XML", Assert.Single(expanding.Problems).Message);
    }

    // ------------------------------------------------------------------ comparisons

    /// <summary>A catalog with a client, an items.otb (13.10) and an items.xml that uses its IDs.</summary>
    private static ItemCatalog ItemsOtbCatalog()
    {
        var catalog = ItemCatalogStore.Create(Path.Combine(TestData.NewTempDirectory("itemdata"), "ItemData"), "ItemData");
        ItemCatalogStore.Commit(catalog, new ItemImport
        {
            Source = Source("client", SourceKind.OfficialClient, IdType.TibiaObject, official: true, version: "15.33"),
            Observations =
            [
                new() { SourceId = "client", IdType = IdType.TibiaObject, Id = 3031, Name = "gold coin", Status = ObservationStatus.Verified, Flags = ["pickupable", "stackable"] },
                new() { SourceId = "client", IdType = IdType.TibiaObject, Id = 3043, Name = "crystal coin", Status = ObservationStatus.Verified },
                new() { SourceId = "client", IdType = IdType.TibiaObject, Id = 2853, Name = "bag", Status = ObservationStatus.Verified, Flags = ["container", "pickupable"] },
            ],
        });
        var otb = OtbFile.Parse(Otb("OTB 3.65.62-13.10",
            (2148, 3031, OtbItemGroup.None, Movable | Pickupable | Stackable, null),
            (2149, 3031, OtbItemGroup.None, Movable | Pickupable | Stackable, null),
            (2160, 3043, OtbItemGroup.None, Movable | Pickupable | Stackable, null),
            (1987, 2853, OtbItemGroup.Container, Movable | Pickupable, null)));
        ItemCatalogStore.Commit(catalog, ItemImporters.FromOtb(otb, Source("otb-ids", SourceKind.OtItemsOtb, IdType.OtbServer)));
        ItemCatalogStore.Commit(catalog, FromXml("""
            <items>
                <item id="2148" article="a" name="gold coin"/>
                <item id="2160" article="a" name="crystal coins"/>
                <item id="2152" article="a" name="platinum coin"/>
                <item id="1987" article="a" name="bag"><attribute key="containersize" value="8"/></item>
            </items>
            """, Source("otb-xml", SourceKind.OtItemsXml, IdType.OtbServer, version: "13.10"), IdType.OtbServer));
        return catalog;
    }

    [Fact]
    public void ItemsOtbIdsAreComparedWithClientIdsOnlyThroughAnItemsOtb()
    {
        var catalog = ItemsOtbCatalog();
        var client = new ComparisonSide(catalog, "client");
        var xml = new ComparisonSide(catalog, "otb-xml");

        // Without the items.otb mapping, items.otb IDs are never taken for client IDs: nothing matches.
        var plain = CatalogComparison.Compare(client, xml, new ComparisonOptions { UseMappings = false });
        Assert.Equal(0, plain.Matched);
        Assert.Equal(3, plain.Count(ChangeKind.Removed));
        Assert.Equal(4, plain.Count(ChangeKind.Added));
        Assert.All(plain.Rows.Where(r => r.Kind == ChangeKind.Added), r => Assert.Equal(IdType.OtbServer, r.IdType));

        // Through the items.otb, but its client IDs kept apart from Tibia object IDs: still no match.
        var mapped = CatalogComparison.Compare(client, xml, new ComparisonOptions { UseMappings = true });
        Assert.Equal(0, mapped.Matched);
        Assert.Contains(mapped.Rows, r => r.IdType == IdType.OtbClient && r.Id == 3031);

        // With the stated assumption: matched by client ID, a rename found, the evidence reported.
        var assumed = CatalogComparison.Compare(client, xml, new ComparisonOptions { UseMappings = true, OtbClientIdsAreObjectIds = true });
        Assert.Equal(3, assumed.Matched);   // 3031, 3043, 2853
        var renamed = Assert.Single(assumed.Rows, r => r.Kind == ChangeKind.Renamed);
        Assert.Equal((3043u, "crystal coin", "a crystal coins"), (renamed.Id, renamed.OldName, renamed.NewName));
        var added = Assert.Single(assumed.Rows, r => r.Kind == ChangeKind.Added);
        Assert.Equal((IdType.OtbServer, 2152u), (added.IdType, added.Id));   // no mapping: stays an items.otb ID
        Assert.Contains(assumed.Notes, n => n.Contains("Assumption"));
        Assert.Contains(assumed.Notes, n => n.Contains("2 of the 3 identifiers named on both sides"));
        // Only the xml side's IDs are translated; its items.otb maps 2148 and 2149 to 3031, but only 2148 has a name in the xml.
        Assert.Empty(assumed.MappingConflicts);
        var withOtb = CatalogComparison.Compare(client, new ComparisonSide(catalog, "otb-ids"), new ComparisonOptions { OtbClientIdsAreObjectIds = true });
        Assert.Contains("client 3031 ← items.otb 2148, 2149", Assert.Single(withOtb.MappingConflicts));

        // The entry of an items.otb ID shows its mapping.
        Assert.Contains(catalog.MappingsFrom(IdType.OtbServer, 2148), m => m.ToType == IdType.OtbClient && m.ToId == 3031);
    }

    [Fact]
    public void ComparingTwoVersionsFindsChangesAndMarksBehaviorSensitiveOnes()
    {
        ItemCatalog Version(string name, string xml)
        {
            var c = ItemCatalogStore.Create(Path.Combine(TestData.NewTempDirectory("ver"), name), name);
            ItemCatalogStore.Commit(c, FromXml(xml, Source("xml", SourceKind.OtItemsXml, IdType.TibiaObject), IdType.TibiaObject));
            return c;
        }
        var old = Version("old", """
            <items>
                <item id="100" name="void"/>
                <item id="1210" name="closed door"><attribute key="type" value="door"/></item>
                <item id="1949" name="scroll"><attribute key="primarytype" value="others"/></item>
                <item id="1987" name="bag"><attribute key="type" value="container"/></item>
                <item id="2000" name="stone"/>
                <item id="2100"/>
            </items>
            """);
        var @new = Version("new", """
            <items>
                <item id="100" name="void"/>
                <item id="1210" name="locked door"><attribute key="type" value="door"/></item>
                <item id="1949" name="scroll"><attribute key="primarytype" value="documents"/></item>
                <item id="2000"/>
                <item id="2001" name="pebble"/>
                <item id="2100"/>
                <item id="2200" name="teleporter"><attribute key="type" value="teleport"/></item>
            </items>
            """);
        var result = CatalogComparison.Compare(new ComparisonSide(old, "xml"), new ComparisonSide(@new, "xml"));
        ComparisonRow Row(uint id) => Assert.Single(result.Rows, r => r.Id == id);
        Assert.Equal((ChangeKind.Renamed, true), (Row(1210).Kind, Row(1210).BehaviorSensitive));
        Assert.Equal((ChangeKind.CategoryChanged, "others", "documents"), (Row(1949).Kind, Row(1949).OldValue, Row(1949).NewValue));
        Assert.Equal((ChangeKind.Removed, true), (Row(1987).Kind, Row(1987).BehaviorSensitive));
        Assert.Equal(ChangeKind.NameRemoved, Row(2000).Kind);
        Assert.Equal((ChangeKind.Added, false), (Row(2001).Kind, Row(2001).BehaviorSensitive));
        Assert.Equal((ChangeKind.Added, true), (Row(2200).Kind, Row(2200).BehaviorSensitive));
        Assert.Equal(1, result.UnnamedOnBothSides);   // 2100
        Assert.Equal(2, result.Unchanged);            // 100, 2100
        Assert.Equal(3, result.BehaviorSensitive);
        var table = result.ToTable();
        Assert.Equal(result.Rows.Count, table.Rows.Count);
        Assert.Contains(table.Summary, l => l.Contains("behavior-sensitive"));
    }

    [Fact]
    public void TwoItemsOtbVersionsRevealItemsOtbIdsMappedToOtherClientIds()
    {
        ItemCatalog WithOtb(string name, params (ushort, ushort?, OtbItemGroup, uint, string?)[] items)
        {
            var c = ItemCatalogStore.Create(Path.Combine(TestData.NewTempDirectory("otbver"), name), name);
            ItemCatalogStore.Commit(c, ItemImporters.FromOtb(OtbFile.Parse(Otb($"OTB 3.0.0-{name}", items)), Source("otb", SourceKind.OtItemsOtb, IdType.OtbServer)));
            return c;
        }
        var a = WithOtb("12.90", (2148, 3031, OtbItemGroup.None, 0, null), (1987, 2853, OtbItemGroup.Container, 0, null), (1210, 1628, OtbItemGroup.Door, 0, null));
        var b = WithOtb("13.10", (2148, 3031, OtbItemGroup.None, 0, null), (1987, 2854, OtbItemGroup.Container, 0, null), (1210, 1629, OtbItemGroup.Door, 0, null));
        var result = CatalogComparison.Compare(new ComparisonSide(a, "otb"), new ComparisonSide(b, "otb"), new ComparisonOptions { UseMappings = true });
        var remapped = result.Rows.Where(r => r.Kind == ChangeKind.Remapped).ToList();
        Assert.Equal([1210u, 1987u], remapped.Select(r => r.Id));
        Assert.All(remapped, r => Assert.True(r.BehaviorSensitive));
        Assert.Equal(("client 2853", "client 2854"), (remapped[1].OldValue, remapped[1].NewValue));
    }

    // ------------------------------------------------------------------ decisions

    [Fact]
    public void ConflictsAreResolvedOnlyByADecisionThatCanBeWithdrawn()
    {
        var catalog = ItemCatalogStore.Create(Path.Combine(TestData.NewTempDirectory("decide"), "D"), "D");
        ItemCatalogStore.Commit(catalog, new ItemImport { Source = Source("a", SourceKind.NameList, IdType.TibiaObject), Observations = [new() { SourceId = "a", IdType = IdType.TibiaObject, Id = 5, Name = "sand" }] });
        ItemCatalogStore.Commit(catalog, new ItemImport { Source = Source("b", SourceKind.NameList, IdType.TibiaObject), Observations = [new() { SourceId = "b", IdType = IdType.TibiaObject, Id = 5, Name = "dirt" }] });
        var entry = catalog.Find(IdType.TibiaObject, 5)!;
        Assert.Equal(Agreement.Conflict, entry.Agreement);

        ItemCatalogStore.Decide(catalog, entry, "dirt", note: "checked in the game");
        var decided = catalog.Find(IdType.TibiaObject, 5)!;
        Assert.Equal((Agreement.Decided, "dirt"), (decided.Agreement, decided.BestName));
        Assert.Equal(2, decided.NameGroups.Count);   // what the sources say stays visible
        Assert.Equal(2, catalog.Observations.Count(o => o.SourceId is "a" or "b"));
        Assert.Equal(SourceKind.UserDecision, catalog.Source(ItemCatalogStore.DecisionsSourceId)!.Kind);

        var reopened = ItemCatalogStore.Load(catalog.Folder);
        Assert.Equal(Agreement.Decided, reopened.Find(IdType.TibiaObject, 5)!.Agreement);
        ItemCatalogStore.Decide(reopened, reopened.Find(IdType.TibiaObject, 5)!, null);
        Assert.Equal(Agreement.Conflict, reopened.Find(IdType.TibiaObject, 5)!.Agreement);
        Assert.Equal(Agreement.Conflict, ItemCatalogStore.Load(catalog.Folder).Find(IdType.TibiaObject, 5)!.Agreement);
        // Decisions are records added one by one: the source cannot be removed as a whole.
        Assert.Throws<InvalidOperationException>(() => ItemCatalogStore.RemoveSource(reopened, ItemCatalogStore.DecisionsSourceId));
    }

    // ------------------------------------------------------------------ XLSX

    [Fact]
    public void WorkbooksAreValidOpenXmlAndNeverRunFormulas()
    {
        var table = new ReportTable("Items", ["ID", "Name", "Note"]);
        table.Add("3031", "gold coin", "");
        table.Add("0042", "=SUM(A1:A2)", "  padded  ");
        table.Add("123456789012345678", "bell\u0001char", new string('x', 40_000));
        var second = new ReportTable("Second", ["A"]);
        second.Add("1");
        string file = Path.Combine(TestData.NewTempDirectory("xlsx"), "out.xlsx");
        XlsxWriter.Save(file, [("Items [main]: a very long sheet name for testing", table), ("Items [main]: a very long sheet name for testing", second)], "Exported for a test\nline two");

        using var zip = ZipFile.OpenRead(file);
        Assert.Superset(new HashSet<string> { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml",
            "xl/worksheets/sheet1.xml", "xl/worksheets/sheet2.xml", "xl/worksheets/sheet3.xml", "docProps/core.xml" }, zip.Entries.Select(e => e.FullName).ToHashSet());
        XDocument Part(string name) { using var s = zip.GetEntry(name)!.Open(); return XDocument.Load(s); }
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetNames = Part("xl/workbook.xml").Descendants(ns + "sheet").Select(s => (string)s.Attribute("name")!).ToList();
        Assert.Equal("About", sheetNames[0]);
        Assert.All(sheetNames, n => Assert.True(n.Length <= 31 && n.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) < 0, n));
        Assert.Equal(3, sheetNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var cells = Part("xl/worksheets/sheet2.xml").Descendants(ns + "c").ToDictionary(c => (string)c.Attribute("r")!);
        Assert.Equal("1", (string?)cells["A1"].Attribute("s"));                     // bold header
        Assert.Equal("3031", cells["A2"].Element(ns + "v")!.Value);                 // a number
        Assert.Equal("inlineStr", (string?)cells["A3"].Attribute("t"));              // "0042" keeps its zeros as text
        Assert.Equal("=SUM(A1:A2)", cells["B3"].Descendants(ns + "t").Single().Value);
        Assert.Empty(cells["B3"].Elements(ns + "f"));                                // never a formula
        Assert.Equal("  padded  ", cells["C3"].Descendants(ns + "t").Single().Value);
        Assert.Equal("inlineStr", (string?)cells["A4"].Attribute("t"));              // too long for an exact number
        Assert.Equal("bellchar", cells["B4"].Descendants(ns + "t").Single().Value);  // control character removed
        Assert.Equal(XlsxWriter.MaxCellLength, cells["C4"].Descendants(ns + "t").Single().Value.Length);
        Assert.Equal("A1:C4", (string?)Part("xl/worksheets/sheet2.xml").Descendants(ns + "autoFilter").Single().Attribute("ref"));
        Assert.Equal("AA10", XlsxWriter.CellName(26, 9));
    }

    // ------------------------------------------------------------------ the page

    [Fact]
    public async Task ThePageImportsItemsXmlWithItsItemsOtbComparesAndDecides()
    {
        string dir = TestData.NewTempDirectory("otpage");
        var settings = new AppSettings { OutputFolder = Path.Combine(dir, "out"), LoadDataAutomatically = false };
        settings.Normalize();
        using var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var dialogs = new FakeDialogs { ConfirmAnswer = true };
        services.Dialogs = dialogs;
        var vm = new ItemCatalogViewModel(services);
        await vm.OnNavigatedToAsync();
        dialogs.PromptAnswers.Enqueue("Item data test");
        await vm.NewCatalogCommand.ExecuteAsync(null);

        // A name list of client IDs first (as if from the official client).
        string list = Path.Combine(dir, "names.txt");
        File.WriteAllText(list, "[3031] a gold coin\r\n[3043] a crystal coin\r\n");
        dialogs.NextFile = list;
        dialogs.ChooseAnswer = 0;
        await vm.ImportFileCommand.ExecuteAsync(null);

        // An items.xml with items.otb IDs and its items.otb next to it: both are imported.
        string itemData = Path.Combine(dir, "itemdata", "data", "items");
        Directory.CreateDirectory(itemData);
        File.WriteAllBytes(Path.Combine(itemData, "items.otb"), Otb("OTB 3.65.62-13.10", (2148, 3031, OtbItemGroup.None, Stackable, null), (2160, 3043, OtbItemGroup.None, Stackable, null)));
        File.WriteAllText(Path.Combine(itemData, "items.xml"), """<items><item id="2148" article="a" name="gold coin"/><item id="2160" article="a" name="crystal coins"/></items>""");
        dialogs.NextFile = Path.Combine(itemData, "items.xml");
        dialogs.ChooseAnswers.Enqueue(1);                           // items.otb IDs
        dialogs.ChooseAnswers.Enqueue(0);                           // import the items.otb too
        dialogs.ChooseAnswer = 1;                                   // later questions: as before
        dialogs.PromptAnswers.Enqueue("Community data 13.10");
        await vm.ImportFileCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Sources.Count);
        var xmlSource = vm.Catalog!.Sources.Single(s => s.Kind == SourceKind.OtItemsXml);
        Assert.Equal(("Community data items.xml", "13.10", IdType.OtbServer), (xmlSource.Name, xmlSource.Version, xmlSource.IdType));
        Assert.Equal(2, vm.Catalog.Mappings.Count);

        // Compare the list with the items.xml, assuming the items.otb's client IDs are object IDs.
        vm.LeftChoice = vm.LeftChoices.Single(c => c.SourceId == vm.Catalog.Sources.Single(s => s.Kind == SourceKind.NameList).Id);
        vm.RightCatalog = vm.Catalogs.Single();
        vm.RightChoice = vm.RightChoices.Single(c => c.SourceId == xmlSource.Id);
        vm.CompareOtbAsObjects = true;
        await vm.CompareCommand.ExecuteAsync(null);
        var renamed = Assert.Single(vm.ComparisonRows);
        Assert.Equal("renamed", renamed.Kind);
        Assert.Contains("1 of the 2", vm.ComparisonSummary);
        string report = Path.Combine(dir, "comparison.xlsx");
        dialogs.NextFile = report;
        await vm.ExportComparisonCommand.ExecuteAsync(null);
        Assert.True(File.Exists(report));

        // Decide a name for an identifier whose sources disagree.
        vm.SearchText = "";
        vm.OnlyDisagreements = false;
        vm.SelectedRow = vm.VisibleRows.First(r => r.Entry.IdType == IdType.TibiaObject && r.Entry.Id == 3043);
        Assert.Empty(vm.NameChoices);   // one source names 3043 as a client ID: nothing to decide
        vm.SelectedRow = vm.VisibleRows.First(r => r.Entry.IdType == IdType.OtbServer && r.Entry.Id == 2160);
        Assert.Contains("OtbClient 3043", vm.MappingText);
        dialogs.PromptAnswers.Enqueue("a crystal coin");
        await vm.DecideOtherNameCommand.ExecuteAsync(null);
        Assert.Equal("a crystal coin", vm.SelectedRow!.Name);
        Assert.Equal("chosen by you", vm.SelectedRow.AgreementText);
        Assert.True(vm.CanWithdrawDecision);
    }

    // ------------------------------------------------------------------ real third-party item data (opt-in)

    /// <summary>
    /// The research figures of the 7.0 plan, from the user's own copies of the files (TF_OTB_ITEMS_DIR: a
    /// folder with an items.otb and an items.xml that uses its IDs; TF_CLIENTID_ITEMS_DIR: a folder with an
    /// items.xml that uses client object IDs). The files are GPL data and are never part of Tibia Foundry.
    /// </summary>
    [Fact]
    public void RealThirdPartyItemDataReproducesTheResearchFigures()
    {
        string? otbData = TestData.Setting("OTB_ITEMS_DIR"), clientIdData = TestData.Setting("CLIENTID_ITEMS_DIR");
        Assert.SkipWhen(otbData == null && clientIdData == null, "Real third-party item data (set TF_OTB_ITEMS_DIR and/or TF_CLIENTID_ITEMS_DIR).");
        var catalog = ItemCatalogStore.Create(Path.Combine(TestData.NewTempDirectory("realitems"), "Real"), "Real");
        if (otbData != null)
        {
            var otb = OtbFile.Read(Path.Combine(otbData, "items.otb"));
            var otbImport = ItemImporters.FromOtb(otb, Source("otb-ids", SourceKind.OtItemsOtb, IdType.OtbServer));
            ItemCatalogStore.Commit(catalog, otbImport);
            using var stream = File.OpenRead(Path.Combine(otbData, "items.xml"));
            var xml = ItemImporters.FromItemsXml(stream, Source("otb-xml", SourceKind.OtItemsXml, IdType.OtbServer, version: otb.ClientVersion), IdType.OtbServer);
            ItemCatalogStore.Commit(catalog, xml);
            var servers = otb.Items.Select(i => (uint)i.ServerId).ToHashSet();
            int same = otb.Items.Count(i => i.ClientId == i.ServerId);
            int shared = otb.Items.Where(i => i.ClientId != null).GroupBy(i => i.ClientId).Count(g => g.Count() > 1);
            int named = xml.Observations.Count(o => o.HasName);
            int namedWithoutOtb = xml.Observations.Count(o => o.HasName && !servers.Contains(o.Id));
            output.WriteLine($"items.otb {otb}: items.otb IDs {servers.Count:N0} ({servers.Min()}–{servers.Max()}), equal to client ID {same:N0}, client IDs with >1 items.otb ID {shared:N0}; " +
                             $"items.xml named items.otb IDs {named:N0} (IDs ≥ 100), named without an items.otb entry {namedWithoutOtb:N0}; problems: {xml.Problems.Count}");
            Assert.Equal("13.10", otb.ClientVersion);
            Assert.Equal(38_284, servers.Count);
            Assert.Equal((100u, 47_311u), (servers.Min(), servers.Max()));
            Assert.Equal(1_433, same);
            Assert.Equal(1_418, shared);
            if (TestData.Client.AssetsExist)
            {
                var db = SharedData.Appearances;
                ItemCatalogStore.Commit(catalog, ItemImporters.FromClient(db, TestData.Client.ReadClientVersion(), db.SourcePath));
                var result = CatalogComparison.Compare(new ComparisonSide(catalog, ItemImporters.ClientSourceId), new ComparisonSide(catalog, "otb-xml"),
                    new ComparisonOptions { UseMappings = true, OtbClientIdsAreObjectIds = true });
                foreach (string line in result.SummaryLines()) output.WriteLine("items.otb data vs client: " + line);
                // Release evidence: the comparison as a workbook (opened afterwards in other programs to check the file).
                if (TestData.Setting("CATALOG_REPORT_DIR") is { Length: > 0 } dir)
                    XlsxWriter.Save(Path.Combine(dir, "otb_data_vs_client.xlsx"), [("Differences", result.ToTable())], string.Join("\n", result.SummaryLines()));
            }
        }
        if (clientIdData != null)
        {
            using var stream = File.OpenRead(Path.Combine(clientIdData, "items.xml"));
            var xml = ItemImporters.FromItemsXml(stream, Source("clientids", SourceKind.OtItemsXml, IdType.TibiaObject), IdType.TibiaObject);
            output.WriteLine($"Client-ID items.xml: {xml.Observations.Count(o => o.HasName):N0} named IDs ≥ 100 ({xml.Observations.Min(o => o.Id)}–{xml.Observations.Max(o => o.Id)}); problems: {xml.Problems.Count}");
            ItemCatalogStore.Commit(catalog, xml);
            if (TestData.Client.AssetsExist && catalog.Source(ItemImporters.ClientSourceId) == null)
            {
                var db = SharedData.Appearances;
                ItemCatalogStore.Commit(catalog, ItemImporters.FromClient(db, TestData.Client.ReadClientVersion(), db.SourcePath));
            }
            if (catalog.ClientSource != null)
            {
                var coverage = CatalogCoverage.Of(catalog).Sources.Single(s => s.Source.Id == "clientids");
                output.WriteLine($"Client-ID items.xml vs client: same {coverage.SameAsClient:N0}, different {coverage.DifferentFromClient:N0}, names for unnamed {coverage.NamesForUnnamed:N0}, not in client {coverage.NotInClient:N0}");
            }
        }
    }
}
