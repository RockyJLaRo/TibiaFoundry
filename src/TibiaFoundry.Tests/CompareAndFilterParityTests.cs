using Newtonsoft.Json.Linq;
using Tibia.Protobuf.Shared;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Compare;

namespace TibiaFoundry.Tests;

public class CompareAndFilterParityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AppearanceComparison_MatchesOriginal()
    {
        string assets = TestData.RequireClientAssets();
        string? oldFile = Directory.EnumerateFiles(assets, "appearances-*.dat-bak").FirstOrDefault();
        Assert.SkipWhen(oldFile == null, "No appearances .dat-bak (older version) available to compare.");
        foreach (bool ignore in new[] { true, false })
        {
            var reference = JObject.Parse(File.ReadAllText(TestData.RequireReference("original", "compare", $"appearances_compare_ignoreSprites_{ignore}.json")));
            // The comparer requires original file names; a .dat-bak keeps the "appearances-" prefix.
            var result = await DatComparer.CompareFilesAsync(oldFile!, TestData.Client.FindAssetFile("appearances")!, ignore, null, TestContext.Current.CancellationToken);
            foreach (var category in result.Categories)
            {
                var r = reference[category.Name]!;
                Assert.Equal(r["changed"]!.Select(t => (string)t!), category.Changed);
                Assert.Equal(r["added"]!.Select(t => (string)t!), category.Added);
                var changes = (JObject)r["changes"]!;
                Assert.Equal(changes.Count, category.Changes.Count);
                foreach (var (key, text) in category.Changes) Assert.Equal((string)changes[key]!, text);
                output.WriteLine($"ignoreSprites={ignore} {category.Name}: {category.Changed.Count} changed, {category.Added.Count} added, {category.Removed.Count} removed (new info)");
            }
        }
    }

    private sealed record Case(string Name, AppearanceKind Kind, FilterMode Mode, Func<AppearanceDatabase, AppearanceCriterion[]> Criteria);

    private static AppearanceCriterion F(string flag) => AppearanceFilter.Flag(AppearanceFilter.FlagDefinitions.Single(f => f.Name == flag));

    /// <summary>
    /// The same 52 filter scenarios run through the original (parity harness, reference
    /// <c>original_run3/filters</c>). Results must be identical except for documented fixes.
    /// </summary>
    [Fact]
    public void Filters_MatchOriginal()
    {
        var reference = JObject.Parse(File.ReadAllText(TestData.RequireReference("original_run3", "filters", "filter_results.json")));
        var db = SharedData.Appearances;
        uint coinSprite = db.Find(AppearanceKind.Object, 3031)!.FrameGroup[0].SpriteInfo.SpriteId[0];
        var cases = new List<Case>
        {
            new("take", AppearanceKind.Object, FilterMode.IncludeAll, _ => [F("take")]),
            new("take_and_cumulative", AppearanceKind.Object, FilterMode.IncludeAll, _ => [F("take"), F("cumulative")]),
            new("take_or_container", AppearanceKind.Object, FilterMode.IncludeAny, _ => [F("take"), F("container")]),
            new("exclude_and_unpass_unmove", AppearanceKind.Object, FilterMode.ExcludeAll, _ => [F("unpass"), F("unmove")]),
            new("exclude_or_take_unpass", AppearanceKind.Object, FilterMode.ExcludeAny, _ => [F("take"), F("unpass")]),
            new("market_rings", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.MarketCategory(ITEM_CATEGORY.Rings)]),
            new("market_any", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.MarketCategory(null)]),
            new("npcsaledata", AppearanceKind.Object, FilterMode.IncludeAll, _ => [F("npcsaledata")]),
            new("light_any", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Light(-1, -1)]),
            new("light_brightness3", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Light(3, -1)]),
            new("automap_any", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Automap(-1)]),
            new("automap_color86", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Automap(86)]),
            new("cloth_head", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Clothes(1)]),
            new("cloth_all", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Clothes(GameTables.ClothesSlotAll)]),
            new("write_range", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Write(0, 1000)]),
            new("write_once", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.WriteOnce(0, 100000)]),
            new("hook_south", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Hook(HOOK_TYPE.South)]),
            new("hook_east", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Hook(HOOK_TYPE.East)]),
            new("shift", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Shift()]),
            new("height_8", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Height(8)]),
            new("lenshelp_any", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.LensHelp(-1)]),
            new("default_action_any", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.DefaultAction(null)]),
            new("default_action_use", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.DefaultAction(PLAYER_ACTION.Use)]),
            new("changedtoexpire", AppearanceKind.Object, FilterMode.IncludeAll, _ => [F("changedtoexpire")]),
            new("cyclopedia", AppearanceKind.Object, FilterMode.IncludeAll, _ => [F("cyclopedia")]),
            new("has_name", AppearanceKind.Object, FilterMode.IncludeAll, _ => [F("name")]),
            new("description_imbu", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Description("imbu")]),
            new("currency_any", AppearanceKind.Object, FilterMode.IncludeAll, d => [AppearanceFilter.Currency("", d)]),
            new("ammo", AppearanceKind.Object, FilterMode.IncludeAll, _ => [F("ammo")]),
            // The harness left the id box at its default (100), as the original UI does.
            new("proficiency_any", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Proficiency(100)]),
            new("vocation_knight", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Vocation(VOCATION.Knight)]),
            new("weapon_sword", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.WeaponType(WEAPON_TYPE.Sword)]),
            new("min_level_ge100", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.MinimumLevel(100, NumberComparison.AtLeast)]),
            new("classification_any", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.UpgradeClassification(0)]),
            new("classification_2", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.UpgradeClassification(2)]),
            new("framegroups_eq1", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.FrameGroupCount(1, NumberComparison.Equal)]),
            new("animation", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Animated(0)]),
            new("is_opaque", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.IsOpaque(0)]),
            new("layers_eq2", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Layers(0, 2, NumberComparison.Equal)]),
            new("sprites_ge8", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.SpriteCount(0, 8, NumberComparison.AtLeast)]),
            new("pattern_width_eq4", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.PatternWidth(0, 4, NumberComparison.Equal)]),
            new("looptype_pingpong", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.LoopType(0, ANIMATION_LOOP_TYPE.Pingpong)]),
            new("synced", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Synchronized(0)]),
            new("width_64", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.BoundingWidth(0, true)]),
            new("id_range", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.IdAtLeast(3000), AppearanceFilter.IdAtMost(3100)]),
            new("id_list", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.IdIn(new uint[] { 3031, 3035, 3043, 999999 })]),
            new("search_name_sword", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.NameContains("sword")]),
            new("search_sprite", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.UsesSprite(coinSprite)]),
            new("outfits_name_citizen", AppearanceKind.Outfit, FilterMode.IncludeAll, _ => [AppearanceFilter.NameContains("citizen")]),
            new("effects_id_ge50", AppearanceKind.Effect, FilterMode.IncludeAll, _ => [AppearanceFilter.IdAtLeast(100)]),
            // Documented fixes (expected to differ from the original):
            new("bank_any", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Bank(-1, NumberComparison.Equal)]),
            new("imbueable_slots123", AppearanceKind.Object, FilterMode.IncludeAll, _ => [AppearanceFilter.Imbueable(true, true, true)]),
        };
        var knownFixes = new Dictionary<string, string>
        {
            ["bank_any"] = "original required Waypoints != 0 (never true in current data) → 0 results",
            ["imbueable_slots123"] = "original threw NullReferenceException",
        };
        int identical = 0;
        foreach (var c in cases)
        {
            var mine = AppearanceFilter.Apply(db.List(c.Kind), c.Criteria(db), c.Mode).Select(a => a.Id).ToList();
            var r = reference[c.Name]!;
            if (knownFixes.TryGetValue(c.Name, out var why))
            {
                output.WriteLine($"{c.Name}: rebuild {mine.Count} results; original: {(r["exception"] != null ? (string)r["exception"]! : r["filteredDatIds"]!.Count() + " results")} ({why})");
                Assert.NotEmpty(mine);
                continue;
            }
            var expected = r["filteredDatIds"]!.Select(t => (uint)t).ToList();
            Assert.True(expected.SequenceEqual(mine), $"{c.Name}: original {expected.Count}, rebuild {mine.Count}");
            identical++;
        }
        output.WriteLine($"{identical} filter scenarios identical to the original");
    }
}
