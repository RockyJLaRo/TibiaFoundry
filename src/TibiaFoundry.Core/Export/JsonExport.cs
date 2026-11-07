using TibiaFoundry.Core.Common;
using System.Text;
using Newtonsoft.Json;
using Tibia.Protobuf.Appearances;

namespace TibiaFoundry.Core.Export;

/// <summary>
/// "Export as JSON" in the original format: Newtonsoft.Json, indented, default values omitted,
/// protobuf property names (e.g. "Object", "FrameGroup", "SpriteInfo").
/// </summary>
public static class JsonExport
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        DefaultValueHandling = DefaultValueHandling.Ignore,
        Formatting = Formatting.Indented,
    };

    public static string Serialize(object data) => JsonConvert.SerializeObject(data, Settings);

    /// <summary>Streams the JSON to disk (appearances exceed 50 MB) and replaces the file atomically.</summary>
    public static async Task WriteAsync(object data, string path, CancellationToken ct = default)
    {
        await Task.Run(() => AtomicFile.Write(path, temp =>
        {
            using var writer = new StreamWriter(temp, false, new UTF8Encoding(false), 1 << 16);
            using var json = new JsonTextWriter(writer) { Formatting = Formatting.Indented };
            JsonSerializer.Create(Settings).Serialize(json, data);
        }), ct);
    }

    /// <summary>Tab-separated "id → name" table of the filtered appearances (Table export).</summary>
    public static string IdNameTable(IEnumerable<Appearance> appearances)
    {
        var sb = new StringBuilder();
        foreach (var a in appearances) sb.Append(a.Id).Append('\t').Append(a.Name).Append(Environment.NewLine);
        return sb.ToString();
    }
}
