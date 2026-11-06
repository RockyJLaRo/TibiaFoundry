using Tibia.Protobuf.Appearances;
using Tibia.Protobuf.Shared;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Wiki;

namespace TibiaFoundry.Tests;

/// <summary>Appearances → Wiki infobox (TibiaWiki's {{Infobox Object}}), 9.0.</summary>
public class InfoboxExportTests
{
    private static Appearance Obj(uint id, string name, Action<AppearanceFlags>? flags = null)
    {
        var a = new Appearance { Id = id, Name = name, Flags = new AppearanceFlags() };
        flags?.Invoke(a.Flags);
        return a;
    }

    private static AppearanceDatabase Db(params Appearance[] objects)
    {
        var data = new Appearances();
        data.Object.AddRange(objects);
        return new AppearanceDatabase(data);
    }

    private static string Value(WikiTemplate t, string name) => t.Field(name)?.Value ?? "";

    private static Appearance Armor() => Obj(3366, "magic plate armor", f =>
    {
        f.Take = true;
        f.Clothes = new AppearanceFlagClothes { Slot = 4 };
        f.Market = new AppearanceFlagMarket { Category = ITEM_CATEGORY.Armors, TradeAsObjectId = 3366, ShowAsObjectId = 3366 };
        f.RestrictToVocation.Add(VOCATION.Paladin);
        f.RestrictToVocation.Add(VOCATION.Knight);
        f.Imbueable = new AppearanceFlagImbueable { SlotCount = 2 };
        f.Upgradeclassification = new AppearanceFlagUpgradeClassification { UpgradeClassification = 2 };
        f.Npcsaledata.Add(new AppearanceFlagNPC { Name = "Rashid", Location = "Svargrond", BuyPrice = 90000 });
        f.Npcsaledata.Add(new AppearanceFlagNPC { Name = "H.L.", Location = "Edron", BuyPrice = 720 });
    });

    [Fact]
    public void AnItemWithCompleteClientDataFillsEverythingTheClientKnows()
    {
        var t = InfoboxObjectBuilder.Build(Armor(), Db(Armor()));
        Assert.Equal("Infobox Object", t.TemplateName);
        Assert.Equal("Magic Plate Armor", Value(t, "name"));
        Assert.Equal("magic plate armor", Value(t, "actualname"));
        Assert.Equal("3366", Value(t, "itemid"));
        Assert.Equal("Body Equipment", Value(t, "objectclass"));
        Assert.Equal("Armors", Value(t, "primarytype"));
        Assert.Equal("Body", Value(t, "slot"));
        Assert.Equal("knights and paladins", Value(t, "vocrequired"));
        Assert.Equal("2", Value(t, "imbueslots"));
        Assert.Equal("2", Value(t, "upgradeclass"));
        Assert.Equal("no", Value(t, "immobile"));
        Assert.Equal("yes", Value(t, "walkable"));
        Assert.Equal("yes", Value(t, "pickupable"));
        Assert.Equal("no", Value(t, "stackable"));
        Assert.Equal("yes", Value(t, "marketable"));
        Assert.Equal("{{Dropped By|}}", Value(t, "droppedby"));
        Assert.Equal("90000", Value(t, "npcvalue"));
        Assert.Equal("0", Value(t, "npcprice"));
        Assert.Equal("--", Value(t, "buyfrom"));
        Assert.Equal("H.L.: 720, Rashid", Value(t, "sellto"));
        Assert.Equal(WikiFieldSource.Client, t.Field("slot")!.Source);
        Assert.Equal(WikiFieldSource.Derived, t.Field("objectclass")!.Source);
        Assert.Equal(WikiFieldSource.Derived, t.Field("name")!.Source);

        string markup = WikiTemplateWriter.Write(t);
        Assert.StartsWith("{{Infobox Object|List={{{1|}}}|GetValue={{{GetValue|}}}\n| name          = Magic Plate Armor\n", markup);
        Assert.EndsWith("\n}}", markup);
        // Field order follows TibiaWiki's item pages.
        Assert.True(markup.IndexOf("| itemid", StringComparison.Ordinal) < markup.IndexOf("| objectclass", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("| weight", StringComparison.Ordinal) < markup.IndexOf("| marketable", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("| sellto", StringComparison.Ordinal) < markup.IndexOf("| notes", StringComparison.Ordinal));
    }

    [Fact]
    public void NothingTheClientDoesNotKnowIsInvented()
    {
        var t = InfoboxObjectBuilder.Build(Armor(), Db(Armor()));
        foreach (string unknown in new[] { "attack", "defense", "armor", "attrib", "resist", "plural", "article", "flavortext", "history" })
            Assert.Null(t.Field(unknown));
        // Required by the template but unknown: listed empty, marked as missing.
        foreach (string required in new[] { "implemented", "notes", "weight", "value" })
        {
            var f = t.Field(required)!;
            Assert.Equal("", f.Value);
            Assert.Equal(WikiFieldSource.Missing, f.Source);
            Assert.True(f.Required);
        }
        string markup = WikiTemplateWriter.Write(t);
        Assert.Contains("\n| weight        =\n", markup);
        Assert.Contains("\n| weight        = ?\n", WikiTemplateWriter.Write(t, new WikiWriteOptions(UnknownPlaceholder: "?")));
        Assert.DoesNotContain("| weight", WikiTemplateWriter.Write(t, new WikiWriteOptions(IncludeEmptyRequired: false)));
    }

    [Fact]
    public void AnObjectWithIncompleteMetadataStillGivesAValidInfobox()
    {
        var bare = Obj(12345, "");
        var t = InfoboxObjectBuilder.Build(bare);
        Assert.Contains(t.Warnings, w => w.Length > 0);
        Assert.Equal("12345", Value(t, "itemid"));
        Assert.Equal("", Value(t, "name"));
        Assert.Equal("no", Value(t, "pickupable"));
        // Not pickupable: the item-only parameters are not required and not written.
        Assert.Null(t.Field("weight"));
        Assert.Null(t.Field("droppedby"));
        string markup = WikiTemplateWriter.Write(t);
        Assert.Contains("| name          =\n", markup);
        Assert.Equal(markup.Count(c => c == '{'), markup.Count(c => c == '}'));

        var noFlags = new Appearance { Id = 7, Name = "stone" };
        Assert.Equal("no", Value(InfoboxObjectBuilder.Build(noFlags), "immobile"));
    }

    [Fact]
    public void TheClientDescriptionBecomesTheFlavortext()
    {
        var core = Obj(54631, "soul core");
        core.Description = "Offers a soul to the Soulpit. Combine with another soul core to create a random new soul core";
        var t = InfoboxObjectBuilder.Build(core);
        var field = t.Field("flavortext");
        Assert.NotNull(field);
        Assert.Equal(core.Description, field!.Value);
        Assert.Equal(WikiFieldSource.Client, field.Source);
        // Written after slot/types and before implemented, like TibiaWiki's item pages.
        var names = t.Fields.Select(f => f.Name).ToList();
        Assert.True(names.IndexOf("flavortext") < names.IndexOf("implemented"));
        Assert.Contains("| flavortext    = Offers a soul to the Soulpit.", WikiTemplateWriter.Write(t));

        Assert.Null(InfoboxObjectBuilder.Build(Obj(1, "stone")).Field("flavortext"));
    }

    [Fact]
    public void ImmobileFurnitureGetsItsFieldProperties()
    {
        var lamp = Obj(2900, "street lamp", f =>
        {
            f.Unmove = true;
            f.Unpass = true;
            f.Unsight = true;
            f.Rotate = true;
            f.Light = new AppearanceFlagLight { Brightness = 7, Color = 206 };
            f.Automap = new AppearanceFlagAutomap { Color = 129 };
        });
        var t = InfoboxObjectBuilder.Build(lamp);
        Assert.Equal("yes", Value(t, "immobile"));
        Assert.Equal("no", Value(t, "walkable"));
        Assert.Equal("yes", Value(t, "unshootable"));
        Assert.Equal("yes", Value(t, "rotatable"));
        Assert.Equal("7", Value(t, "lightradius"));
        Assert.Equal("206", Value(t, "lightcolor"));
        Assert.Equal("129", Value(t, "mapcolor"));
    }

    [Fact]
    public void WeaponsWritableItemsAndPotionsUseTheWikiConventions()
    {
        var bow = Obj(3350, "bow", f => { f.Take = true; f.Clothes = new() { Slot = 0 }; f.WeaponType = WEAPON_TYPE.Bow; f.Market = new() { Category = ITEM_CATEGORY.DistanceWeapons }; });
        var t = InfoboxObjectBuilder.Build(bow);
        Assert.Equal(("Weapons", "Distance Weapons", "Bows"), (Value(t, "objectclass"), Value(t, "primarytype"), Value(t, "secondarytype")));
        Assert.Equal(("Both Hands", "Two", "Distance"), (Value(t, "slot"), Value(t, "hands"), Value(t, "weapontype")));

        var sword = Obj(3264, "sword", f => { f.Take = true; f.Clothes = new() { Slot = 6 }; f.WeaponType = WEAPON_TYPE.Sword; f.MinimumLevel = 0; });
        t = InfoboxObjectBuilder.Build(sword);
        Assert.Equal(("One", "Sword"), (Value(t, "hands"), Value(t, "weapontype")));
        Assert.Null(t.Field("levelrequired"));

        var wand = Obj(3074, "wand of vortex", f => { f.Take = true; f.MinimumLevel = 6; f.Market = new() { Category = ITEM_CATEGORY.WandsRods }; f.RestrictToVocation.Add(VOCATION.Sorcerer); });
        t = InfoboxObjectBuilder.Build(wand);
        Assert.Equal(("Wands", "6", "sorcerers"), (Value(t, "primarytype"), Value(t, "levelrequired"), Value(t, "vocrequired")));

        var book = Obj(3059, "spellbook", f => { f.Take = true; f.Market = new() { Category = ITEM_CATEGORY.Shields }; f.RestrictToVocation.Add(VOCATION.Sorcerer); f.RestrictToVocation.Add(VOCATION.Druid); });
        t = InfoboxObjectBuilder.Build(book);
        Assert.Equal(("Spellbooks", "Shields", "sorcerers and druids"), (Value(t, "primarytype"), Value(t, "secondarytype"), Value(t, "vocrequired")));

        var scroll = Obj(2815, "scroll", f => { f.Take = true; f.Write = new() { MaxTextLength = 511 }; });
        t = InfoboxObjectBuilder.Build(scroll);
        Assert.Equal(("yes", "yes", "511"), (Value(t, "writable"), Value(t, "rewritable"), Value(t, "writechars")));

        var parchment = Obj(2817, "parchment", f => { f.Take = true; f.WriteOnce = new() { MaxTextLengthOnce = 1023 }; });
        t = InfoboxObjectBuilder.Build(parchment);
        Assert.Equal(("yes", "1023"), (Value(t, "writable"), Value(t, "writechars")));
        Assert.Null(t.Field("rewritable"));

        var potion = Obj(266, "health potion", f => { f.Take = true; f.Cumulative = true; f.Market = new() { Category = ITEM_CATEGORY.Potions }; });
        t = InfoboxObjectBuilder.Build(potion);
        Assert.Equal(("Plants, Animal Products, Food and Drink", "Liquids", "Potions", "yes"),
            (Value(t, "objectclass"), Value(t, "primarytype"), Value(t, "secondarytype"), Value(t, "stackable")));

        var all = Obj(1, "rune", f => { foreach (var v in new[] { VOCATION.Monk, VOCATION.Druid, VOCATION.Sorcerer, VOCATION.Paladin, VOCATION.Knight }) f.RestrictToVocation.Add(v); });
        Assert.Equal("knights, paladins, sorcerers, druids and monks", Value(InfoboxObjectBuilder.Build(all), "vocrequired"));
    }

    [Fact]
    public void NpcTradesPickTheBestPricesAndListTheOthersWithTheirPrice()
    {
        var gold = Obj(3031, "gold coin");
        var token = Obj(22721, "gold token");
        var sword = Obj(3264, "sword", f =>
        {
            f.Take = true;
            f.Npcsaledata.Add(new AppearanceFlagNPC { Name = "Willard", SalePrice = 85, BuyPrice = 25 });
            f.Npcsaledata.Add(new AppearanceFlagNPC { Name = "Blind Orc", SalePrice = 95 });
            f.Npcsaledata.Add(new AppearanceFlagNPC { Name = "H.L.", BuyPrice = 7 });
            f.Npcsaledata.Add(new AppearanceFlagNPC { Name = "Baltim", SalePrice = 85, BuyPrice = 25, CurrencyObjectTypeId = 3031 });
            f.Npcsaledata.Add(new AppearanceFlagNPC { Name = "Token Trader", SalePrice = 3, CurrencyObjectTypeId = 22721 });
        });
        var t = InfoboxObjectBuilder.Build(sword, Db(gold, token, sword));
        Assert.Equal("25", Value(t, "npcvalue"));
        Assert.Equal("85", Value(t, "npcprice"));
        Assert.Equal("Baltim, Blind Orc: 95, Willard", Value(t, "buyfrom"));
        Assert.Equal("Baltim, H.L.: 7, Willard", Value(t, "sellto"));
        Assert.Contains(t.Warnings, w => w.Contains("Token Trader"));
        Assert.Equal("no", Value(t, "marketable"));
    }

    [Fact]
    public void ObjectsSharingANameAreReported()
    {
        var a = Obj(100, "torch", f => f.Take = true);
        var b = Obj(101, "torch", f => f.Take = true);
        var t = InfoboxObjectBuilder.Build(a, Db(a, b));
        Assert.Contains(t.Warnings, w => w.Contains("101"));
    }

    [Fact]
    public void TheWikiArticleNameWinsOverTheClientName()
    {
        var t = InfoboxObjectBuilder.Build(Armor(), null, "Magic Plate Armor");
        Assert.Equal(WikiFieldSource.TibiaWiki, t.Field("name")!.Source);
        var named = InfoboxObjectBuilder.Build(Obj(1, "book of the dead"));
        Assert.Equal("Book of the Dead", Value(named, "name"));
    }

    [Theory]
    [InlineData("a | b", "a {{!}} b")]
    [InlineData("see [[Sword|the sword]]", "see [[Sword|the sword]]")]
    [InlineData("{{Dropped By|Demon|Orshabaal}}", "{{Dropped By|Demon|Orshabaal}}")]
    [InlineData("broken }} end", "broken &#125;&#125; end")]
    [InlineData("open {{ never closed | x", "open &#123;&#123; never closed {{!}} x")]
    [InlineData("x = y", "x = y")]
    [InlineData("[[unclosed | link", "[[unclosed {{!}} link")]
    [InlineData("Äëö 'Ñ' “quoted” ★", "Äëö 'Ñ' “quoted” ★")]
    [InlineData("  padded  ", "padded")]
    public void ValuesAreMadeSafeForATemplateArgument(string input, string expected) =>
        Assert.Equal(expected, WikiTemplateWriter.Clean(input, multiline: false));

    [Fact]
    public void MultilineValuesKeepTheirLinesAndSingleLineValuesAreJoined()
    {
        Assert.Equal("first\n* second", WikiTemplateWriter.Clean("first\r\n* second\r\n", multiline: true));
        Assert.Equal("first second", WikiTemplateWriter.Clean("first\r\n\r\n second", multiline: false));
        var t = InfoboxObjectBuilder.Build(Armor()).With("notes", "Line one.\nLine two with a [[Link|label]].").With("name", "Weird | Name");
        string markup = WikiTemplateWriter.Write(t);
        Assert.Contains("| notes         = Line one.\nLine two with a [[Link|label]].\n", markup);
        Assert.Contains("| name          = Weird {{!}} Name\n", markup);
        Assert.Equal(WikiFieldSource.User, t.Field("notes")!.Source);
    }

    [Fact]
    public void ParametersMatchTheTemplate()
    {
        // Every parameter the builder can write is one the template reads (checked against its source, 2026-11-06).
        var order = InfoboxObjectBuilder.ParameterOrder;
        Assert.Equal(order.Count, order.Distinct().Count());
        Assert.Subset(order.ToHashSet(), InfoboxObjectBuilder.RequiredAlways.ToHashSet());
        Assert.Subset(order.ToHashSet(), InfoboxObjectBuilder.RequiredForItems.ToHashSet());
        var t = InfoboxObjectBuilder.Build(Armor(), Db(Armor()));
        Assert.All(t.Fields, f => Assert.Contains(f.Name, order));
    }

    [Fact]
    public void FileNamesAreSafeAndBatchExportsNeverReplaceFiles()
    {
        var t = InfoboxObjectBuilder.Build(Obj(5, "x")).With("name", "What? <A:B>");
        string name = InfoboxObjectBuilder.FileNameFor(t, 5);
        Assert.EndsWith(" (5).txt", name);
        Assert.DoesNotContain(name, c => Path.GetInvalidFileNameChars().Contains(c));

        string dir = TestData.NewTempDirectory("infobox");
        File.WriteAllText(Path.Combine(dir, "A (1).txt"), "existing");
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(dir, "A (1) 2.txt"), InfoboxObjectBuilder.UniquePath(dir, "A (1).txt", taken));
        Assert.Equal(Path.Combine(dir, "A (1) 3.txt"), InfoboxObjectBuilder.UniquePath(dir, "A (1).txt", taken));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(dir, "A (1).txt")));
    }

    [Fact]
    public void RealClientObjectsMatchTheirTibiaWikiArticles()
    {
        TestData.RequireClientAssets();
        var db = SharedData.Appearances;
        // Values checked on TibiaWiki (Magic Plate Armor, Sword, Gold Coin, Backpack), 2026-11-06.
        var mpa = InfoboxObjectBuilder.Build(db.Find(AppearanceKind.Object, 3366)!, db);
        Assert.Equal(("Body", "knights and paladins", "2", "2"), (Value(mpa, "slot"), Value(mpa, "vocrequired"), Value(mpa, "imbueslots"), Value(mpa, "upgradeclass")));
        Assert.Equal(("Body Equipment", "Armors", "yes", "no"), (Value(mpa, "objectclass"), Value(mpa, "primarytype"), Value(mpa, "marketable"), Value(mpa, "stackable")));
        Assert.Equal(("0", "--"), (Value(mpa, "npcprice"), Value(mpa, "buyfrom")));
        Assert.Contains("Rashid", Value(mpa, "sellto"));

        var sword = InfoboxObjectBuilder.Build(db.Find(AppearanceKind.Object, 3264)!, db);
        Assert.Equal(("Weapon Hand", "One", "Sword", "Sword Weapons"), (Value(sword, "slot"), Value(sword, "hands"), Value(sword, "weapontype"), Value(sword, "primarytype")));

        var gold = InfoboxObjectBuilder.Build(db.Find(AppearanceKind.Object, 3031)!, db);
        Assert.Equal(("gold coin", "yes", "no"), (Value(gold, "actualname"), Value(gold, "stackable"), Value(gold, "marketable")));
        // Not on the market, so no market category: the classification is left for the editor, not guessed.
        Assert.Equal(WikiFieldSource.Missing, gold.Field("primarytype")!.Source);

        var backpack = InfoboxObjectBuilder.Build(db.Find(AppearanceKind.Object, 2854)!, db);
        Assert.Equal(("Container", "Household Items", "Containers"), (Value(backpack, "slot"), Value(backpack, "objectclass"), Value(backpack, "primarytype")));

        // The client's look text is the wiki's flavortext (soul core, reported by the user).
        var soulCore = db.Find(AppearanceKind.Object, 54631)!;
        var core = InfoboxObjectBuilder.Build(soulCore, db);
        Assert.StartsWith("Offers a soul to the Soulpit.", Value(core, "flavortext"));
        Assert.Equal(soulCore.Description.Trim(), Value(core, "flavortext"));

        // Every object of the client gives balanced markup.
        foreach (var obj in db.List(AppearanceKind.Object))
        {
            string markup = WikiTemplateWriter.Write(InfoboxObjectBuilder.Build(obj, db: null));
            Assert.StartsWith("{{Infobox Object|", markup);
            Assert.EndsWith("}}", markup);
        }
    }
}
