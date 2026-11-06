using Newtonsoft.Json.Linq;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Export;
using TibiaFoundry.Core.Houses;
using TibiaFoundry.Core.Map;
using TibiaFoundry.Core.Protobuf;
using TibiaFoundry.Core.Sounds;
using TibiaFoundry.Core.StaticData;
using TibiaFoundry.Core.Wiki;

namespace TibiaFoundry.Tests;

/// <summary>Byte/text parity of exports against outputs of the original application.</summary>
public class ExportParityTests(ITestOutputHelper output)
{
    /// <summary>
    /// Compares JSON line by line. Lines containing U+FFFD in the original are allowed to differ:
    /// the original replaced Latin-1 characters with U+FFFD, the rebuild decodes them correctly.
    /// </summary>
    private void AssertJsonParity(string expectedPath, string actualJson, string what)
    {
        var expected = File.ReadAllLines(expectedPath);
        var actual = actualJson.Split(["\r\n", "\n"], StringSplitOptions.None);
        Assert.Equal(expected.Length, actual.Length);
        int latin1Fixes = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] == actual[i]) continue;
            if (expected[i].Contains('�')) { latin1Fixes++; continue; }
            Assert.Fail($"{what}: line {i + 1} differs:\n  original: {expected[i]}\n  rebuild:  {actual[i]}");
        }
        output.WriteLine($"{what}: {expected.Length} lines identical except {latin1Fixes} line(s) with repaired Latin-1 text");
    }

    [Fact]
    public void AppearancesJson_MatchesOriginal()
    {
        string reference = TestData.RequireReference("original", "json", "appearances.json");
        AssertJsonParity(reference, JsonExport.Serialize(SharedData.Appearances.Data), "appearances.json");
    }

    [Fact]
    public void OtherDataJson_MatchesOriginal()
    {
        TestData.RequireClientAssets();
        var c = TestData.Client;
        AssertJsonParity(TestData.RequireReference("original", "json", "staticdata.json"),
            JsonExport.Serialize(StaticDataDatabase.Load(c.FindAssetFile(AssetTypes.StaticData)!).Data), "staticdata.json");
        AssertJsonParity(TestData.RequireReference("original", "json", "staticmapdata.json"),
            JsonExport.Serialize(StaticMapDatabase.Load(c.FindAssetFile(AssetTypes.StaticMapData)!).Data), "staticmapdata.json");
        AssertJsonParity(TestData.RequireReference("original", "json", "map.json"),
            JsonExport.Serialize(MapDatabase.Load(c.FindAssetFile(AssetTypes.Map)!).Data), "map.json");
        AssertJsonParity(TestData.RequireReference("original", "json", "sounds.json"),
            JsonExport.Serialize(SoundDatabase.Load(c.FindSoundsFile()!).Data), "sounds.json");
    }

    [Fact]
    public void FilteredExports_MatchOriginal()
    {
        var db = SharedData.Appearances;
        var take = AppearanceFilter.Apply(db.List(AppearanceKind.Object), [AppearanceFilter.Flag(AppearanceFilter.FlagDefinitions.First(f => f.Name == "take"))], FilterMode.IncludeAll);
        AssertJsonParity(TestData.RequireReference("original", "json", "filtered_take.json"), JsonExport.Serialize(take), "filtered_take.json");
        string table = File.ReadAllText(TestData.RequireReference("original", "json", "filtered_take.txt"));
        Assert.Equal(table, JsonExport.IdNameTable(take));
    }

    private static MapDatabase LoadMap()
    {
        TestData.RequireClientAssets();
        return MapDatabase.Load(TestData.Client.FindAssetFile(AssetTypes.Map)!);
    }

    [Fact]
    public void WikiLists_MatchOriginal()
    {
        var map = LoadMap();
        Assert.Equal(File.ReadAllText(TestData.RequireReference("original", "wiki", "wiki_area_list.txt")), MapExports.WikiAreaList(map));
        Assert.Equal(File.ReadAllText(TestData.RequireReference("original", "wiki", "wiki_coords_labels.txt")), MapExports.LabelsWithCoordinates(map));
        Assert.Equal(File.ReadAllText(TestData.RequireReference("original", "wiki", "wiki_nocoords_labels.txt")), MapExports.LabelsWithoutCoordinates(map));
    }

    [Fact]
    public void NpcJsonAndTable_MatchOriginal()
    {
        var map = LoadMap();
        Assert.Equal(File.ReadAllText(TestData.RequireReference("original", "json", "map_npcs.json")), MapExports.NpcsJson(map));
        Assert.Equal(File.ReadAllText(TestData.RequireReference("original", "json", "map_npcs.txt")), MapExports.NpcsTable(map));
    }

    /// <summary>
    /// Areas: identical except the two documented fixes — subarea names are no longer listed twice,
    /// and subareas now get their parent area (the original compared against "AREA_TYPE_SUBAREA",
    /// which never matched).
    /// </summary>
    [Fact]
    public void AreasJson_MatchesOriginalApartFromFixes()
    {
        var map = LoadMap();
        var original = JArray.Parse(File.ReadAllText(TestData.RequireReference("original", "json", "map_areas.json")));
        var mine = JArray.Parse(MapExports.AreasJson(map));
        Assert.Equal(original.Count, mine.Count);
        int parentsAdded = 0;
        for (int i = 0; i < original.Count; i++)
        {
            var o = (JObject)original[i];
            var m = (JObject)mine[i];
            var origNames = o["SubareaNames"]!.Select(t => (string)t!).ToList();
            var myNames = m["SubareaNames"]!.Select(t => (string)t!).ToList();
            Assert.Equal(myNames.Concat(myNames), origNames); // original duplicated the list
            foreach (var key in new[] { "Name", "ID", "Type", "SubareaIDs", "X", "Y", "Z", "NPCs" })
                Assert.True(JToken.DeepEquals(o[key], m[key]), $"area {o["ID"]} field {key}");
            Assert.Equal(0, (int)o["ParentAreaId"]!);
            if ((string)m["Type"]! == "Subarea" && (int)m["ParentAreaId"]! != 0) parentsAdded++;
        }
        output.WriteLine($"{original.Count} areas; parent filled for {parentsAdded} subareas");
        Assert.True(parentsAdded > 0);
    }

    [Fact]
    public void NpcTradeLists_MatchOriginal()
    {
        var db = SharedData.Appearances;
        string dir = TestData.RequireReference("original", "npc");
        int compared = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.lua"))
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            int us = stem.LastIndexOf('_');
            string npc = stem[..us];
            var sort = stem[(us + 1)..] == "byid" ? NpcTradeSort.ById : NpcTradeSort.ByName;
            // File names were sanitised by the harness; resolve the real NPC name.
            string? realName = NpcTradeExporter.NpcNames(db).FirstOrDefault(n => Core.Common.FileNames.Sanitize(n) == npc || n == npc);
            Assert.NotNull(realName);
            Assert.Equal(File.ReadAllText(file), NpcTradeExporter.ForNpc(db, realName!, sort).Lua);
            compared++;
        }
        output.WriteLine($"{compared} NPC trade lists identical");
        Assert.True(compared >= 10);
    }

    [Fact]
    public async Task ExtractedSounds_MatchOriginal()
    {
        string refDir = TestData.RequireReference("original", "sounds");
        TestData.RequireClientAssets();
        var sounds = SoundDatabase.Load(TestData.Client.FindSoundsFile()!);
        string outDir = TestData.NewTempDirectory("sounds");
        var result = await sounds.ExtractAsync(outDir, null, TestContext.Current.CancellationToken);
        Assert.False(result.HasFailures);
        var expected = Directory.GetFiles(refDir).Select(Path.GetFileName).OrderBy(f => f).ToList();
        var actual = Directory.GetFiles(outDir).Select(Path.GetFileName).OrderBy(f => f).ToList();
        Assert.Equal(expected, actual);
        foreach (var name in expected)
            Assert.True(File.ReadAllBytes(Path.Combine(refDir, name!)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(outDir, name!))), name);
        output.WriteLine($"{expected.Count} sound files byte-identical");
        Directory.Delete(outDir, true);
    }

    [Fact]
    public void CatalogSummary_MatchesOriginalExceptSheetCount()
    {
        var summary = File.ReadAllLines(TestData.RequireReference("original", "lzma", "catalog_summary.txt"))
            .Select(l => l.Split('=')).ToDictionary(p => p[0], p => p[1]);
        var catalog = AssetCatalog.Load(TestData.RequireClientAssets());
        // The original displayed one sheet too many ("+ 1").
        Assert.Equal(int.Parse(summary["nSheets"]) - 1, catalog.SheetCount);
        Assert.Equal(int.Parse(summary["nSprites"]), catalog.HighestSpriteId);
        Assert.Equal(summary["newest3232"], catalog.NewestSheet(SpriteSheetType.Small)!.FirstSpriteId.ToString());
        Assert.Equal(summary["newest6432"], catalog.NewestSheet(SpriteSheetType.Tall)!.FirstSpriteId.ToString());
        Assert.Equal(summary["newest3264"], catalog.NewestSheet(SpriteSheetType.Wide)!.FirstSpriteId.ToString());
        Assert.Equal(summary["newest6464"], catalog.NewestSheet(SpriteSheetType.Large)!.FirstSpriteId.ToString());
    }

    [Fact]
    public void Latin1Strings_AreRepaired()
    {
        TestData.RequireClientAssets();
        string path = TestData.Client.FindAssetFile(AssetTypes.Appearances)!;
        var data = Google.Protobuf.ByteString.CopyFrom(ProtoLoader.ReadShared(path));
        var db = SharedData.Appearances;
        var nonAscii = db.List(AppearanceKind.Object).Where(o => o.Name.Any(c => c > 127) || o.Description.Any(c => c > 127)).Take(5).ToList();
        foreach (var o in nonAscii) output.WriteLine($"{o.Id}: {o.Name} | {o.Description}");
        Assert.DoesNotContain(db.List(AppearanceKind.Object), o => o.Name.Contains('�') || o.Description.Contains('�'));
        Assert.True(data.Length > 0);
    }
}
