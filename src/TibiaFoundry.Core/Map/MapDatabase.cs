using Newtonsoft.Json;
using Tibia.Protobuf.Map;
using Tibia.Protobuf.Shared;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Protobuf;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Map;

/// <summary>Area row (same properties, order and JSON names as the original <c>TibiaArea</c>).</summary>
public sealed class AreaRecord
{
    public string Name { get; init; } = "";
    public string ID { get; init; } = "";
    public string Type { get; init; } = "";
    public uint ParentAreaId { get; init; }
    public string? ParentAreaName { get; init; }
    public List<uint> SubareaIDs { get; init; } = new();
    public List<string> SubareaNames { get; init; } = new();
    public uint X { get; init; }
    public uint Y { get; init; }
    public uint Z { get; init; }
    public List<string> NPCs { get; init; } = new();

    [JsonIgnore] public uint AreaId { get; init; }
}

/// <summary>NPC row (same properties as the original <c>TibiaNPC</c>).</summary>
public sealed class NpcRecord
{
    public string Name { get; init; } = "";
    public string? Subarea { get; init; }
    public uint SubareaId { get; init; }
    public string? Area { get; init; }
    public uint AreaId { get; init; }
    public uint X { get; init; }
    public uint Y { get; init; }
    public uint Z { get; init; }

    [JsonIgnore] public int Index { get; init; }
}

/// <summary>Resource file row (original <c>TibiaMapFile</c>: Type + Areaid, plus extra columns).</summary>
public sealed class MapFileRecord
{
    public string Type { get; init; } = "";
    public string Areaid { get; init; } = "";
    [JsonIgnore] public int Index { get; init; }
    [JsonIgnore] public string FileName { get; init; } = "";
    [JsonIgnore] public string Position { get; init; } = "";
}

/// <summary>Loaded <c>map-*.dat</c> (areas, subareas, NPCs and the map resource files).</summary>
public sealed class MapDatabase
{
    public static readonly string[] FileTypeNames = ["Subarea", "Satellite", "Minimap"];

    private readonly Dictionary<uint, Area> _areasById;

    public MapDatabase(Tibia.Protobuf.Map.Map data)
    {
        Data = data;
        _areasById = new Dictionary<uint, Area>();
        foreach (var a in data.Areas) _areasById.TryAdd(a.AreaId, a);
        Areas = BuildAreas();
        Npcs = BuildNpcs();
        Files = data.ResourceFiles.Select((f, i) => new MapFileRecord
        {
            Type = FileTypeName(f.FileType),
            Areaid = f.AreaId.ToString(),
            Index = i,
            FileName = f.FileName,
            Position = f.TopLeftCoordinate is { } c ? $"{c.X}, {c.Y}, {c.Z}" : "",
        }).ToList();
    }

    public Tibia.Protobuf.Map.Map Data { get; }
    public IReadOnlyList<AreaRecord> Areas { get; }
    public IReadOnlyList<NpcRecord> Npcs { get; }
    public IReadOnlyList<MapFileRecord> Files { get; }

    public static MapDatabase Load(string path) => new(ProtoLoader.Load(path, Tibia.Protobuf.Map.Map.Parser));

    public Area? FindArea(uint id) => _areasById.GetValueOrDefault(id);

    /// <summary>The area whose subarea list contains <paramref name="subareaId"/>.</summary>
    public Area? FindParent(uint subareaId) => Data.Areas.FirstOrDefault(a => a.SubareaIds.Contains(subareaId));

    public static string FileTypeName(MAP_FILE_TYPE type) =>
        (int)type is >= 0 and < 3 ? FileTypeNames[(int)type] : type.ToString();

    private List<AreaRecord> BuildAreas()
    {
        var npcNames = Data.Npcs.GroupBy(n => n.SubareaId).ToDictionary(g => g.Key, g => g.Select(n => n.Name).ToList());
        return Data.Areas.Select(a =>
        {
            // Fixed: the original appended every subarea name twice.
            var subNames = a.SubareaIds.Select(id => FindArea(id)?.Name ?? $"#{id}").ToList();
            Area? parent = a.AreaType == AREA_TYPE.Subarea ? FindParent(a.AreaId) : null;
            return new AreaRecord
            {
                Name = a.Name,
                ID = a.AreaId.ToString(),
                AreaId = a.AreaId,
                Type = a.AreaType.ToString(),
                SubareaIDs = a.SubareaIds.ToList(),
                SubareaNames = subNames,
                ParentAreaId = parent?.AreaId ?? 0,
                ParentAreaName = parent?.Name,
                X = a.LabelCoordinate?.X ?? 0,
                Y = a.LabelCoordinate?.Y ?? 0,
                Z = a.LabelCoordinate?.Z ?? 0,
                NPCs = npcNames.GetValueOrDefault(a.AreaId) ?? new List<string>(),
            };
        }).ToList();
    }

    private List<NpcRecord> BuildNpcs() => Data.Npcs.Select((n, i) =>
    {
        var parent = FindParent(n.SubareaId);
        return new NpcRecord
        {
            Name = n.Name,
            Index = i,
            SubareaId = n.SubareaId,
            Subarea = FindArea(n.SubareaId)?.Name,
            Area = parent?.Name,
            AreaId = parent?.AreaId ?? 0,
            X = n.TileCoordinate?.X ?? 0,
            Y = n.TileCoordinate?.Y ?? 0,
            Z = n.TileCoordinate?.Z ?? 0,
        };
    }).ToList();

    // ------------------------------------------------------------------ data panel rows

    public List<PropertyRow> AreaRows(Area area)
    {
        var rows = new List<PropertyRow>
        {
            new("AreaId", area.AreaId.ToString()),
            new(L.T("Common.Name"), area.Name),
            new("AreaType", area.AreaType.ToString()),
        };
        AddParentAndNpcs(rows, area.AreaId);
        if (area.SubareaIds.Count > 0)
        {
            rows.Add(new("SubareaIds", string.Join(", ", area.SubareaIds)));
            rows.Add(new("SubareaNames", string.Join(", ", area.SubareaIds.Select(id => FindArea(id)?.Name ?? $"#{id}"))));
        }
        if (area.LabelCoordinate != null) rows.Add(CoordinateRow("LabelCoordinate", area.LabelCoordinate));
        rows.Add(new("RejectDonations", area.RejectDonations.ToString()));
        if (!string.IsNullOrEmpty(area.Alias)) rows.Add(new("Alias", area.Alias));
        return rows;
    }

    public List<PropertyRow> NpcRows(Npc npc)
    {
        var rows = new List<PropertyRow> { new(L.T("Common.Name"), npc.Name) };
        if (npc.TileCoordinate != null) rows.Add(CoordinateRow("TileCoordinate", npc.TileCoordinate));
        rows.Add(new("SubareaId", npc.SubareaId.ToString()));
        rows.Add(new("SubareaNames", FindArea(npc.SubareaId)?.Name ?? L.T("MapDatabase.UnknownSubarea")));
        if (FindParent(npc.SubareaId) is { } parent)
        {
            rows.Add(new("ParentAreaName", parent.Name));
            rows.Add(new("ParentAreaID", parent.AreaId.ToString()));
        }
        return rows;
    }

    public List<PropertyRow> FileRows(MapFile file)
    {
        var rows = new List<PropertyRow>
        {
            new("FileType", file.FileType.ToString()),
        };
        if (file.TopLeftCoordinate != null) rows.Add(CoordinateRow("TopLeftCoordinate", file.TopLeftCoordinate));
        rows.Add(new("FileName", file.FileName));
        rows.Add(new("FieldsWidth", file.FieldsWidth.ToString()));
        rows.Add(new("FieldsHeight", file.FieldsHeight.ToString()));
        rows.Add(new("AreaId", file.AreaId.ToString()));
        if (file.AreaId != 0) AddParentAndNpcs(rows, file.AreaId);
        rows.Add(new("ScaleFactor", file.ScaleFactor.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return rows;
    }

    private void AddParentAndNpcs(List<PropertyRow> rows, uint areaId)
    {
        if (FindParent(areaId) is { } parent)
        {
            rows.Add(new("ParentAreaName", parent.Name));
            rows.Add(new("ParentAreaID", parent.AreaId.ToString()));
        }
        var npcs = Data.Npcs.Where(n => n.SubareaId == areaId).Select(n => n.Name).ToList();
        if (npcs.Count > 0) rows.Add(new("NPCs", string.Join(", ", npcs)));
    }

    private static PropertyRow CoordinateRow(string name, Coordinate c) =>
        new(name, $"[{c.X}, {c.Y}, {c.Z}]") { Link = MapLinks.TibiaMaps(c.X, c.Y, c.Z) };
}
