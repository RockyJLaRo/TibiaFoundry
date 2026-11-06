using System.Text;
using Tibia.Protobuf.Appearances;
using TibiaFoundry.Core.Appearances;

namespace TibiaFoundry.Core.Wiki;

public enum NpcTradeSort { ByName, ById }

/// <summary>Result of the single-NPC trade export.</summary>
public sealed record NpcTradeList(string Lua, int Buys, int Sells, IReadOnlyList<string> Warnings);

/// <summary>Result of the all-NPC Lua export.</summary>
public sealed record NpcTradeModule(string Lua, string Log, int NpcCount);

/// <summary>NPC trade data exports for TibiaWiki's Lua modules (Appearances → Tools).</summary>
public static class NpcTradeExporter
{
    /// <summary>Distinct NPC names that trade anything, sorted.</summary>
    public static IReadOnlyList<string> NpcNames(AppearanceDatabase db) =>
        db.List(AppearanceKind.Object).SelectMany(o => o.Flags?.Npcsaledata ?? Enumerable.Empty<AppearanceFlagNPC>())
          .Select(n => n.Name).Where(n => n.Length > 0).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// "Create (LUA format)" for one NPC: <c>["NPC"] = { ["location"] = "", ["buys"] = {...}, ["sells"] = {...} },</c>
    /// with object names in title case; output format identical to the original.
    /// </summary>
    public static NpcTradeList ForNpc(AppearanceDatabase db, string npc, NpcTradeSort sort)
    {
        var buys = new List<KeyValuePair<string, uint>>();
        var sells = new List<KeyValuePair<string, uint>>();
        var warnings = new List<string>();
        foreach (var obj in db.List(AppearanceKind.Object))
        {
            var entry = obj.Flags?.Npcsaledata.FirstOrDefault(n => n.Name == npc);
            if (entry == null) continue;
            string name = NameFormatter.TitleCase(obj.Name);
            if (entry.BuyPrice != 0)
            {
                if (buys.Any(b => b.Key == name)) warnings.Add("NPC buys at least 2 objects with identical names, but only one was added to the list. Review manually.");
                else buys.Add(new(name, entry.BuyPrice));
            }
            if (entry.SalePrice != 0)
            {
                if (sells.Any(s => s.Key == name)) warnings.Add("NPC sells at least 2 objects with identical names, but only one was added to the list. Review manually.");
                else sells.Add(new(name, entry.SalePrice));
            }
        }
        if (sort == NpcTradeSort.ByName)
        {
            buys = buys.OrderBy(b => b.Key, WikiNameComparer.Instance).ToList();
            sells = sells.OrderBy(s => s.Key, WikiNameComparer.Instance).ToList();
        }
        var sb = new StringBuilder();
        sb.Append("\t[\"").Append(npc).Append("\"] = {\n");
        sb.Append("\t\t[\"location\"] = \"\",\n");
        sb.Append("\t\t[\"buys\"] = {\n");
        foreach (var b in buys) sb.Append("\t\t\t{[\"item\"] = \"").Append(b.Key).Append("\", [\"price\"] = ").Append(b.Value).Append("},\n");
        sb.Append("\t\t\t},\n");
        sb.Append("\t\t[\"sells\"] = {\n");
        foreach (var s in sells) sb.Append("\t\t\t{[\"item\"] = \"").Append(s.Key).Append("\", [\"price\"] = ").Append(s.Value).Append("},\n");
        sb.Append("\t\t}\n\t},");
        return new NpcTradeList(sb.ToString(), buys.Count, sells.Count, warnings.Distinct().ToList());
    }

    /// <summary>
    /// "Generate Lua Data for all NPCs": a Lua module returning every NPC's location, buy and sell
    /// lists. Item names come from TibiaWiki's "Item IDs" table when available (otherwise the client
    /// name is used and logged).
    /// </summary>
    public static NpcTradeModule AllNpcs(AppearanceDatabase db, IReadOnlyDictionary<uint, string>? wikiNames, IEnumerable<string>? initialLog = null)
    {
        var comparer = WikiNameComparer.Instance;
        var npcs = new SortedDictionary<string, (string Location, SortedDictionary<string, uint> Buys, SortedDictionary<string, uint> Sells)>(comparer);
        var log = new List<string>(initialLog ?? Array.Empty<string>()) { " ======= ", "" };
        foreach (var obj in db.List(AppearanceKind.Object))
        {
            if (obj.Flags == null || obj.Flags.Npcsaledata.Count == 0) continue;
            foreach (var trade in obj.Flags.Npcsaledata)
            {
                if (!npcs.TryGetValue(trade.Name, out var data))
                {
                    data = (trade.Location, new SortedDictionary<string, uint>(comparer), new SortedDictionary<string, uint>(comparer));
                    npcs[trade.Name] = data;
                }
                string name;
                if (wikiNames != null && wikiNames.TryGetValue(obj.Id, out var wiki)) name = wiki;
                else
                {
                    log.Add($"Missing ID on TibiaWiki: {obj.Id} - {obj.Name}");
                    name = obj.Name;
                }
                if (trade.BuyPrice != 0) data.Buys.TryAdd(name, trade.BuyPrice);
                if (trade.SalePrice != 0) data.Sells.TryAdd(name, trade.SalePrice);
            }
        }
        var sb = new StringBuilder("return {\n\t");
        foreach (var (npc, data) in npcs)
        {
            sb.Append("[\"").Append(npc).Append("\"] = {\n\t\t");
            sb.Append("[\"location\"] = \"").Append(data.Location).Append("\",\n\t\t");
            sb.Append("[\"buys\"] = {\n\t\t\t");
            foreach (var (item, price) in data.Buys) sb.Append("{[\"item\"] = \"").Append(item).Append("\", ").Append("[\"price\"] = ").Append(price).Append("},\n\t\t\t");
            sb.Append("},\n\t\t");
            sb.Append("[\"sells\"] = {\n\t\t\t");
            foreach (var (item, price) in data.Sells) sb.Append("{[\"item\"] = \"").Append(item).Append("\", ").Append("[\"price\"] = ").Append(price).Append("},\n\t\t\t");
            sb.Append("}\n\t");
            sb.Append("},\n\t");
        }
        sb.Append('}');
        return new NpcTradeModule(sb.ToString(), string.Join("\n", log.Distinct()), npcs.Count);
    }
}
