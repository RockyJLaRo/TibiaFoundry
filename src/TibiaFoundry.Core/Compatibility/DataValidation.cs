using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Houses;
using TibiaFoundry.Core.Map;
using TibiaFoundry.Core.Sounds;
using TibiaFoundry.Core.StaticData;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Compatibility;

/// <summary>
/// Outcome of validating one loaded data file: counted issue types with a few examples each, and
/// whether the file is unusable (<see cref="Fatal"/>). Validation never throws on bad data.
/// </summary>
public sealed class ValidationResult(string component)
{
    private const int MaxExamplesPerIssue = 5;
    private readonly Dictionary<string, int> _counts = new();
    private readonly Dictionary<string, List<string>> _examples = new();

    public string Component { get; } = component;
    public int Checked { get; set; }

    /// <summary>Set when the file lacks content every feature needs (e.g. no objects at all).</summary>
    public string? Fatal { get; set; }

    /// <summary>Appearances that failed structural checks (renderers should not trust them).</summary>
    public HashSet<(AppearanceKind Kind, uint Id)> InvalidAppearances { get; } = new();

    public IReadOnlyDictionary<string, int> IssueCounts => _counts;
    public int IssueTotal => _counts.Values.Sum();
    public bool IsClean => Fatal == null && _counts.Count == 0;

    public void Add(string issue, string example)
    {
        _counts[issue] = _counts.GetValueOrDefault(issue) + 1;
        if (!_examples.TryGetValue(issue, out var list)) _examples[issue] = list = new List<string>();
        if (list.Count < MaxExamplesPerIssue) list.Add(example);
    }

    public IEnumerable<string> Describe()
    {
        if (Fatal != null) yield return "Unusable: " + Fatal;
        foreach (var (issue, count) in _counts.OrderByDescending(kv => kv.Value))
            yield return $"{issue}: {count:N0} (e.g. {string.Join("; ", _examples[issue])})";
    }
}

/// <summary>
/// Structural and cross-reference checks for the client data files. They run after a file was
/// parsed and catch changes the parser cannot see: renamed meanings, missing sub-records, broken
/// references between files, or sprite IDs that do not exist in the sprite catalog.
/// </summary>
public static class DataValidation
{
    public static ValidationResult Appearances(AppearanceDatabase db, AssetCatalog? catalog)
    {
        var result = new ValidationResult("appearances");
        if (db.Count(AppearanceKind.Object) == 0) result.Fatal = L.T("DataValidation.TheFileContainsNoObjects");
        else if (db.Count(AppearanceKind.Outfit) == 0) result.Fatal = L.T("DataValidation.TheFileContainsNoOutfits");
        var missingSprites = new HashSet<uint>();
        foreach (AppearanceKind kind in Enum.GetValues<AppearanceKind>())
        {
            var seen = new HashSet<uint>();
            foreach (var a in db.List(kind))
            {
                result.Checked++;
                string where = $"{kind} {a.Id}";
                if (!seen.Add(a.Id)) result.Add(L.T("DataValidation.DuplicateIDs"), where);
                if (a.FrameGroup.Count == 0)
                {
                    result.Add(L.T("DataValidation.AppearancesWithoutFrameGroups"), where);
                    result.InvalidAppearances.Add((kind, a.Id));
                    continue;
                }
                for (int g = 0; g < a.FrameGroup.Count; g++)
                {
                    var info = a.FrameGroup[g].SpriteInfo;
                    if (info == null)
                    {
                        result.Add(L.T("DataValidation.FrameGroupsWithoutSpriteInfo"), L.F("DataValidation.0Group1", where, g));
                        result.InvalidAppearances.Add((kind, a.Id));
                        continue;
                    }
                    if (info.SpriteId.Count == 0)
                    {
                        result.Add(L.T("DataValidation.FrameGroupsWithoutSprites"), L.F("DataValidation.0Group1", where, g));
                        result.InvalidAppearances.Add((kind, a.Id));
                        continue;
                    }
                    long expected = Math.Max(1u, info.Layers) * (long)Math.Max(1u, info.PatternWidth) * Math.Max(1u, info.PatternHeight) *
                                    Math.Max(1u, info.PatternDepth) * Math.Max(1, info.Animation?.SpritePhase.Count ?? 1);
                    if (expected != info.SpriteId.Count)
                    {
                        result.Add(L.T("DataValidation.SpriteCountDiffersFromLayersPatterns"), L.F("DataValidation.0Group12SpritesLayout", where, g, info.SpriteId.Count, expected));
                        result.InvalidAppearances.Add((kind, a.Id));
                    }
                    if (catalog != null)
                    {
                        foreach (uint id in info.SpriteId)
                        {
                            if (catalog.FindSheet((int)Math.Min(id, int.MaxValue)) == null && missingSprites.Add(id))
                                result.Add(L.T("DataValidation.SpriteIDsNotInTheSprite"), L.F("DataValidation.Sprite01", id, where));
                        }
                    }
                }
            }
        }
        return result;
    }

    public static ValidationResult StaticData(StaticDataDatabase db, AppearanceDatabase? appearances)
    {
        var result = new ValidationResult("staticdata");
        if (db.Monsters.Count == 0) result.Fatal = L.T("DataValidation.TheFileContainsNoMonsters");
        var houseIds = new HashSet<uint>();
        foreach (var h in db.Data.Houses)
        {
            result.Checked++;
            if (!houseIds.Add(h.HouseId)) result.Add(L.T("DataValidation.DuplicateHouseIDs"), L.F("DataValidation.House0", h.HouseId));
        }
        foreach (var c in db.Monsters.Concat(db.Bosses))
        {
            result.Checked++;
            string where = $"{(c.IsBoss ? "boss" : "monster")} {c.Race} ({c.Name})";
            if (string.IsNullOrWhiteSpace(c.Name)) result.Add(L.T("DataValidation.CreaturesWithoutAName"), where);
            if (c.Outfit == null) { result.Add(L.T("DataValidation.CreaturesWithoutOutfitData"), where); continue; }
            if (appearances == null) continue;
            bool found = c.Outfit.OutfitId != 0
                ? appearances.Find(AppearanceKind.Outfit, c.Outfit.OutfitId) != null
                : c.Outfit.ObjectAppearanceTypeId == 0 || appearances.Find(AppearanceKind.Object, c.Outfit.ObjectAppearanceTypeId) != null;
            if (!found) result.Add(L.T("DataValidation.CreaturesWhoseLookIsMissingFrom"), L.F("DataValidation.0Outfit1Object2", where, c.Outfit.OutfitId, c.Outfit.ObjectAppearanceTypeId));
        }
        return result;
    }

    public static ValidationResult StaticMap(StaticMapDatabase db, AppearanceDatabase? appearances)
    {
        var result = new ValidationResult("staticmapdata");
        if (db.Data.HouseMaps.Count == 0) result.Fatal = L.T("DataValidation.TheFileContainsNoHouseMaps");
        var ids = new HashSet<uint>();
        var missing = new HashSet<uint>();
        foreach (var house in db.Data.HouseMaps)
        {
            result.Checked++;
            if (!ids.Add(house.HouseId)) result.Add(L.T("DataValidation.DuplicateHouseIDs"), L.F("DataValidation.House0", house.HouseId));
            var list = house.MapInfo?.ObjectListA?.ObjectListB;
            if (house.MapInfo?.Dimension == null || list == null) { result.Add(L.T("DataValidation.HouseMapsWithoutMapData"), L.F("DataValidation.House0", house.HouseId)); continue; }
            if (appearances == null) continue;
            foreach (var column in list.ObjectListC)
                foreach (var tile in column.Tile)
                    if (tile.ObjectId != 0 && appearances.Find(AppearanceKind.Object, tile.ObjectId) == null && missing.Add(tile.ObjectId))
                        result.Add(L.T("DataValidation.ObjectsMissingFromAppearances"), L.F("DataValidation.Object0House1", tile.ObjectId, house.HouseId));
        }
        return result;
    }

    public static ValidationResult Map(MapDatabase db, string? assetsDirectory) =>
        MapIn(db, assetsDirectory == null ? null : new Clients.PhysicalClientFolder(assetsDirectory));

    /// <summary>Checks the map data; its image files are looked up in <paramref name="assets"/> (a real folder or an imported version).</summary>
    public static ValidationResult MapIn(MapDatabase db, Clients.IClientFolder? assets)
    {
        var result = new ValidationResult("map");
        // Thousands of map images: a real folder is listed once (8.8.1).
        if (assets is Clients.PhysicalClientFolder { IsListed: false } physical) assets = Clients.PhysicalClientFolder.WithListing(physical.DisplayPath);
        if (db.Data.Areas.Count == 0) result.Fatal = L.T("DataValidation.TheFileContainsNoAreas");
        var areaIds = new HashSet<uint>();
        foreach (var area in db.Data.Areas)
        {
            result.Checked++;
            if (!areaIds.Add(area.AreaId)) result.Add(L.T("DataValidation.DuplicateAreaIDs"), L.F("DataValidation.Area0", area.AreaId));
        }
        foreach (var area in db.Data.Areas)
            foreach (uint sub in area.SubareaIds)
                if (!areaIds.Contains(sub)) result.Add(L.T("DataValidation.SubareaReferencesToUnknownAreas"), L.F("DataValidation.Area01", area.AreaId, sub));
        foreach (var npc in db.Data.Npcs)
        {
            result.Checked++;
            if (npc.SubareaId != 0 && !areaIds.Contains(npc.SubareaId)) result.Add(L.T("DataValidation.NPCsInUnknownSubareas"), $"{npc.Name} → {npc.SubareaId}");
        }
        foreach (var file in db.Data.ResourceFiles)
        {
            result.Checked++;
            string? name = TibiaFormat.SafeFileName(file.FileName);
            if (name == null) result.Add(L.T("DataValidation.MapFilesWithUnsafeOrEmpty"), $"\"{file.FileName}\"");
            else if (assets != null && assets.PathOf(name) == null) result.Add(L.T("DataValidation.MapImageFilesMissingOnDisk"), name);
        }
        return result;
    }

    public static ValidationResult Sounds(SoundDatabase db)
    {
        var result = new ValidationResult("sounds");
        if (db.Data.Sound.Count == 0) result.Fatal = L.T("DataValidation.TheFileContainsNoSounds");
        // A real sounds folder is listed once (8.8.1) instead of being asked about every sound file twice.
        var folder = db.SoundsFolder is Clients.PhysicalClientFolder { IsListed: false } physical ? Clients.PhysicalClientFolder.WithListing(physical.DisplayPath) : db.SoundsFolder;
        var ids = new HashSet<uint>();
        foreach (var s in db.Data.Sound)
        {
            result.Checked++;
            if (!ids.Add(s.Id)) result.Add(L.T("DataValidation.DuplicateSoundIDs"), L.F("DataValidation.Sound0", s.Id));
            string? name = TibiaFormat.SafeFileName(s.Filename);
            if (name == null) { result.Add(L.T("DataValidation.SoundsWithUnsafeOrEmptyFile"), L.F("DataValidation.Sound01", s.Id, s.Filename)); continue; }
            if (folder != null && folder.PathOf(name) == null && folder.PathOf(name + ".lzma") == null)
                result.Add(L.T("DataValidation.SoundFilesMissingOnDisk"), L.F("DataValidation.Sound012", s.Id, name));
        }
        foreach (var e in db.Data.NumericSoundEffect)
        {
            result.Checked++;
            var refs = e.SimpleSoundEffect != null ? new[] { e.SimpleSoundEffect.SoundId } : e.RandomSoundEffect?.RandomSoundId.ToArray() ?? Array.Empty<uint>();
            foreach (uint id in refs)
                if (!ids.Contains(id)) result.Add(L.T("DataValidation.SoundEffectsReferringToUnknownSounds"), L.F("DataValidation.Effect0Sound1", e.Id, id));
        }
        return result;
    }
}
