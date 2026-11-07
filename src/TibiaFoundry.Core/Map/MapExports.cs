using System.Text;
using Newtonsoft.Json;
using Tibia.Protobuf.Map;

namespace TibiaFoundry.Core.Map;

/// <summary>Text exports of the Map Tools page (wiki lists and tables).</summary>
public static class MapExports
{
    /// <summary>"Generate Wiki Area/Subarea List" (the original button was never wired to its handler).</summary>
    public static string WikiAreaList(MapDatabase map)
    {
        var sb = new StringBuilder();
        foreach (var area in map.Data.Areas)
        {
            if (area.SubareaIds.Count == 0) continue;
            sb.Append("| '''[[").Append(area.Name).Append("]]'''\r\n|\r\n");
            foreach (uint id in area.SubareaIds)
            {
                var sub = map.FindArea(id);
                bool bold = sub?.LabelCoordinate != null;
                sb.Append("# ");
                if (bold) sb.Append("'''");
                sb.Append("[[").Append(sub?.Name ?? $"#{id}").Append("]]");
                if (bold) sb.Append("'''");
                sb.Append("\r\n");
            }
            sb.Append("|-\r\n");
        }
        return sb.ToString();
    }

    /// <summary>"Generate Coords - Labels List": "x, y, z = Name" for labeled subareas.</summary>
    public static string LabelsWithCoordinates(MapDatabase map)
    {
        var sb = new StringBuilder();
        foreach (var a in map.Data.Areas.Where(a => a.AreaType == AREA_TYPE.Subarea && a.LabelCoordinate != null))
            sb.Append($"{a.LabelCoordinate.X}, {a.LabelCoordinate.Y}, {a.LabelCoordinate.Z} = {a.Name}\r\n");
        return sb.ToString();
    }

    /// <summary>"Generate NoCoords - Labels List": "? = Name" for subareas without a label.</summary>
    public static string LabelsWithoutCoordinates(MapDatabase map)
    {
        var sb = new StringBuilder();
        foreach (var a in map.Data.Areas.Where(a => a.AreaType == AREA_TYPE.Subarea && a.LabelCoordinate == null))
            sb.Append("? = ").Append(a.Name).Append("\r\n");
        return sb.ToString();
    }

    public static string AreasJson(MapDatabase map) => JsonConvert.SerializeObject(map.Areas, Formatting.Indented);

    public static string NpcsJson(MapDatabase map) => JsonConvert.SerializeObject(map.Npcs, Formatting.Indented);

    /// <summary>Tab-separated areas table (header typo "LebelCoords" of the original fixed).</summary>
    public static string AreasTable(MapDatabase map)
    {
        var sb = new StringBuilder("Name\tID\tType\tParentID\tParentName\tSubareaIDs\tSubareaNames\tLabelCoords_X\tLabelCoords_Y\tLabelCoords_Z\tNPCs\n");
        foreach (var a in map.Areas)
        {
            sb.Append(a.Name).Append('\t').Append(a.ID).Append('\t').Append(a.Type).Append('\t').Append(a.ParentAreaId).Append('\t')
              .Append(a.ParentAreaName).Append('\t').Append(string.Join(",", a.SubareaIDs)).Append('\t').Append(string.Join(",", a.SubareaNames)).Append('\t')
              .Append(a.X).Append('\t').Append(a.Y).Append('\t').Append(a.Z).Append('\t').Append(string.Join(",", a.NPCs)).Append('\n');
        }
        return sb.ToString();
    }

    public static string NpcsTable(MapDatabase map)
    {
        var sb = new StringBuilder("Name\tSubareaName\tSubareaId\tAreaName\tAreaId\tCoords_X\tCoords_Y\tCoords_Z\n");
        foreach (var n in map.Npcs)
            sb.Append($"{n.Name}\t{n.Subarea}\t{n.SubareaId}\t{n.Area}\t{n.AreaId}\t{n.X}\t{n.Y}\t{n.Z}\n");
        return sb.ToString();
    }
}
