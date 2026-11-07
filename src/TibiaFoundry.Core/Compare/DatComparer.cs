using Google.Protobuf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tibia.Protobuf.Appearances;
using Tibia.Protobuf.Map;
using Tibia.Protobuf.Staticdata;
using Tibia.Protobuf.Staticmapdata;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Protobuf;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Compare;

public enum DatFileType { Unknown, Appearances, Map, StaticData, StaticMapData }

/// <summary>Differences found in one category (e.g. "Objects").</summary>
public sealed class CompareCategory(string name, AppearanceKind? appearanceKind = null)
{
    public string Name { get; } = name;
    /// <summary>Set for appearance categories, so results can be opened in the viewer.</summary>
    public AppearanceKind? AppearanceKind { get; } = appearanceKind;
    public List<string> Changed { get; } = new();
    public List<string> Added { get; } = new();
    /// <summary>Entries that exist only in the old file (not reported by the original).</summary>
    public List<string> Removed { get; } = new();
    public Dictionary<string, string> Changes { get; } = new();
    public bool HasDifferences => Changed.Count > 0 || Added.Count > 0 || Removed.Count > 0;
}

public sealed class CompareResult
{
    public List<CompareCategory> Categories { get; } = new();
    public List<string> Notes { get; } = new();
    public IEnumerable<CompareCategory> WithDifferences => Categories.Where(c => c.HasDifferences);
}

/// <summary>
/// Compares two versions of the client data files (the original "Compare DAT files" window).
/// </summary>
/// <remarks>
/// Fixes: the comparison ran with cross-thread UI access and message boxes from a background
/// thread; "Compare Folders" crashed when a folder lacked a file (<c>Length &lt; 0</c> check);
/// results of a previous comparison leaked into the next; static map data was unsupported.
/// </remarks>
public static class DatComparer
{
    private static readonly JsonFormatter ProtoJson = new(JsonFormatter.Settings.Default);

    public static DatFileType DetectType(string path) => Path.GetFileName(path).Split('-')[0].ToLowerInvariant() switch
    {
        "appearances" => DatFileType.Appearances,
        "map" => DatFileType.Map,
        "staticdata" => DatFileType.StaticData,
        "staticmapdata" => DatFileType.StaticMapData,
        _ => DatFileType.Unknown,
    };

    public static Task<CompareResult> CompareFilesAsync(string oldFile, string newFile, bool ignoreSpriteIds,
        IProgress<OperationProgress>? progress, CancellationToken ct) => Task.Run(() =>
    {
        var type = DetectType(oldFile);
        if (type == DatFileType.Unknown || type != DetectType(newFile))
            throw new InvalidOperationException("Select two files of the same type that still have their original names (appearances-…, map-…, staticdata-… or staticmapdata-…).");
        var result = new CompareResult();
        CompareByType(type, oldFile, newFile, ignoreSpriteIds, result, progress, ct);
        return result;
    }, ct);

    public static Task<CompareResult> CompareFoldersAsync(string oldFolder, string newFolder, bool ignoreSpriteIds,
        IProgress<OperationProgress>? progress, CancellationToken ct) => Task.Run(() =>
    {
        var result = new CompareResult();
        foreach (var (type, prefix) in new[] { (DatFileType.Appearances, "appearances"), (DatFileType.Map, "map"), (DatFileType.StaticData, "staticdata"), (DatFileType.StaticMapData, "staticmapdata") })
        {
            ct.ThrowIfCancellationRequested();
            var oldFiles = DatFiles(oldFolder, prefix);
            var newFiles = DatFiles(newFolder, prefix);
            if (oldFiles.Count != 1 || newFiles.Count != 1)
            {
                result.Notes.Add(L.F("DatComparer.0SkippedFound1OldAnd", prefix, oldFiles.Count, L.Count(newFiles.Count, "Noun.new_file")));
                continue;
            }
            CompareByType(type, oldFiles[0], newFiles[0], ignoreSpriteIds, result, progress, ct);
        }
        return result;
    }, ct);

    private static List<string> DatFiles(string folder, string prefix) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, prefix + "-*.dat").Where(f => f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)).ToList()
            : new List<string>();

    private static void CompareByType(DatFileType type, string oldFile, string newFile, bool ignoreSprites, CompareResult result,
        IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        switch (type)
        {
            case DatFileType.Appearances:
            {
                var oldData = ProtoLoader.Load(oldFile, Tibia.Protobuf.Appearances.Appearances.Parser);
                var newData = ProtoLoader.Load(newFile, Tibia.Protobuf.Appearances.Appearances.Parser);
                int total = newData.Object.Count + newData.Outfit.Count + newData.Effect.Count + newData.Missile.Count, done = 0;
                void Step() { if (++done % 250 == 0 || done == total) progress?.Report(new OperationProgress(done, total, L.T("DatComparer.ComparingAppearances"))); }
                result.Categories.Add(CompareAppearances(L.T("Common.Objects"), AppearanceKind.Object, oldData.Object, newData.Object, ignoreSprites, Step, ct));
                result.Categories.Add(CompareAppearances(L.T("Common.Outfits"), AppearanceKind.Outfit, oldData.Outfit, newData.Outfit, ignoreSprites, Step, ct));
                result.Categories.Add(CompareAppearances(L.T("Common.Effects"), AppearanceKind.Effect, oldData.Effect, newData.Effect, ignoreSprites, Step, ct));
                result.Categories.Add(CompareAppearances(L.T("Common.Missiles"), AppearanceKind.Missile, oldData.Missile, newData.Missile, ignoreSprites, Step, ct));
                break;
            }
            case DatFileType.Map:
            {
                var oldMap = ProtoLoader.Load(oldFile, Tibia.Protobuf.Map.Map.Parser);
                var newMap = ProtoLoader.Load(newFile, Tibia.Protobuf.Map.Map.Parser);
                result.Categories.Add(CompareKeyed(L.T("MapTools.Areas"), oldMap.Areas, newMap.Areas, a => a.AreaId.ToString(), NewtonsoftJson, ct, a => a.Name + "_" + a.AreaId));
                result.Categories.Add(CompareKeyed("Npcs", oldMap.Npcs, newMap.Npcs, n => $"{n.Name}_{n.TileCoordinate?.X}_{n.TileCoordinate?.Y}_{n.TileCoordinate?.Z}", NewtonsoftJson, ct));
                progress?.Report(new OperationProgress(1, 1, L.T("DatComparer.MapCompared")));
                break;
            }
            case DatFileType.StaticData:
            {
                var oldData = ProtoLoader.Load(oldFile, Tibia.Protobuf.Staticdata.StaticData.Parser);
                var newData = ProtoLoader.Load(newFile, Tibia.Protobuf.Staticdata.StaticData.Parser);
                result.Categories.Add(CompareKeyed(L.T("StaticData.Monsters"), oldData.Monster, newData.Monster, m => m.Race.ToString(), ProtoJsonOf, ct, m => m.Name + "_" + m.Race));
                result.Categories.Add(CompareKeyed(L.T("StaticData.Bosses"), oldData.Bosses, newData.Bosses, b => b.Race.ToString(), ProtoJsonOf, ct, b => b.Name + "_" + b.Race));
                result.Categories.Add(CompareKeyed(L.T("StaticData.Achievements"), oldData.Achievements, newData.Achievements, a => a.AchievementId.ToString(), ProtoJsonOf, ct, a => a.Name + "_" + a.AchievementId));
                result.Categories.Add(CompareKeyed(L.T("Common.Houses"), oldData.Houses, newData.Houses, h => h.HouseId.ToString(), ProtoJsonOf, ct, h => h.Name + "_" + h.HouseId));
                result.Categories.Add(CompareKeyed(L.T("StaticData.Quests"), oldData.Quests, newData.Quests, q => q.QuestId.ToString(), ProtoJsonOf, ct, q => q.Name + "_" + q.QuestId));
                progress?.Report(new OperationProgress(1, 1, L.T("DatComparer.StaticDataCompared")));
                break;
            }
            case DatFileType.StaticMapData:
            {
                var oldData = ProtoLoader.Load(oldFile, StaticMapData.Parser);
                var newData = ProtoLoader.Load(newFile, StaticMapData.Parser);
                result.Categories.Add(CompareHouseMaps(oldData, newData, ct));
                progress?.Report(new OperationProgress(1, 1, L.T("DatComparer.StaticMapDataCompared")));
                break;
            }
        }
    }

    private static CompareCategory CompareAppearances(string name, AppearanceKind kind, IReadOnlyList<Appearance> oldList, IReadOnlyList<Appearance> newList,
        bool ignoreSprites, Action step, CancellationToken ct)
    {
        var category = new CompareCategory(name, kind);
        var oldById = new Dictionary<uint, Appearance>();
        foreach (var a in oldList) oldById.TryAdd(a.Id, a);
        var newIds = new HashSet<uint>();
        foreach (var current in newList)
        {
            ct.ThrowIfCancellationRequested();
            newIds.Add(current.Id);
            step();
            if (!oldById.TryGetValue(current.Id, out var previous))
            {
                category.Added.Add(current.Id.ToString());
                continue;
            }
            var a = current.Clone();
            var b = previous.Clone();
            if (ignoreSprites)
            {
                foreach (var fg in a.FrameGroup) fg.SpriteInfo?.SpriteId.Clear();
                foreach (var fg in b.FrameGroup) fg.SpriteInfo?.SpriteId.Clear();
            }
            if (a.Equals(b)) continue;
            category.Changed.Add(current.Id.ToString());
            category.Changes[current.Id.ToString()] = JsonDiff.Compare(JObject.Parse(ProtoJson.Format(a)), JObject.Parse(ProtoJson.Format(b)));
        }
        category.Removed.AddRange(oldList.Where(o => !newIds.Contains(o.Id)).Select(o => o.Id.ToString()));
        return category;
    }

    private static string NewtonsoftJson(IMessage m) => JsonConvert.SerializeObject(m);
    private static string ProtoJsonOf(IMessage m) => ProtoJson.Format(m);

    /// <param name="key">Identity used to pair old and new entries.</param>
    /// <param name="label">Display label (defaults to the key).</param>
    private static CompareCategory CompareKeyed<T>(string name, IReadOnlyList<T> oldList, IReadOnlyList<T> newList, Func<T, string> key,
        Func<IMessage, string> toJson, CancellationToken ct, Func<T, string>? label = null) where T : IMessage<T>
    {
        label ??= key;
        var category = new CompareCategory(name);
        var oldByKey = new Dictionary<string, T>();
        foreach (var o in oldList) oldByKey.TryAdd(key(o), o);
        var newKeys = new HashSet<string>();
        foreach (var current in newList)
        {
            ct.ThrowIfCancellationRequested();
            string k = key(current);
            newKeys.Add(k);
            if (!oldByKey.TryGetValue(k, out var previous)) { category.Added.Add(label(current)); continue; }
            if (current.Equals(previous)) continue;
            string display = label(current);
            category.Changed.Add(display);
            category.Changes.TryAdd(display, JsonDiff.Compare(JObject.Parse(toJson(current)), JObject.Parse(toJson(previous))));
        }
        category.Removed.AddRange(oldList.Where(o => !newKeys.Contains(key(o))).Select(label));
        return category;
    }

    private static CompareCategory CompareHouseMaps(StaticMapData oldData, StaticMapData newData, CancellationToken ct)
    {
        var category = new CompareCategory(L.T("DatComparer.HouseMaps"));
        var oldById = oldData.HouseMaps.GroupBy(h => h.HouseId).ToDictionary(g => g.Key, g => g.First());
        var newIds = new HashSet<uint>();
        foreach (var house in newData.HouseMaps)
        {
            ct.ThrowIfCancellationRequested();
            newIds.Add(house.HouseId);
            string key = house.HouseId.ToString();
            if (!oldById.TryGetValue(house.HouseId, out var previous)) { category.Added.Add(key); continue; }
            if (house.Equals(previous)) continue;
            category.Changed.Add(key);
            var notes = new List<string>();
            var a = house.MapInfo; var b = previous.MapInfo;
            if (!Equals(a?.HouseCoords, b?.HouseCoords)) notes.Add(L.F("DatComparer.Coordinates01", a?.HouseCoords, b?.HouseCoords));
            if (!Equals(a?.Dimension, b?.Dimension)) notes.Add(L.F("DatComparer.Dimension01", a?.Dimension, b?.Dimension));
            int tilesA = a?.ObjectListA?.ObjectListB?.ObjectListC.Count ?? 0, tilesB = b?.ObjectListA?.ObjectListB?.ObjectListC.Count ?? 0;
            if (!Equals(a?.ObjectListA, b?.ObjectListA)) notes.Add(L.F("DatComparer.TileDataChanged0TileEntries", tilesA, tilesB));
            category.Changes[key] = string.Join(Environment.NewLine, notes) + Environment.NewLine;
        }
        category.Removed.AddRange(oldData.HouseMaps.Where(h => !newIds.Contains(h.HouseId)).Select(h => h.HouseId.ToString()));
        return category;
    }
}
