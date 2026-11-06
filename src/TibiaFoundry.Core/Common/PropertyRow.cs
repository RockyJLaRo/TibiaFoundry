namespace TibiaFoundry.Core.Common;

/// <summary>
/// One row of a "Data" property panel. Optional fields drive extra UI: a color swatch, a
/// clickable map link, a play button for a sound, or sprite thumbnails.
/// </summary>
public sealed record PropertyRow(string Label, string Value)
{
    /// <summary>ARGB color shown next to the value (automap/light colors).</summary>
    public uint? Swatch { get; init; }

    /// <summary>URL opened when the value is clicked (coordinates → tibiamaps.io).</summary>
    public string? Link { get; init; }

    /// <summary>Sound id that can be played from this row.</summary>
    public uint? SoundId { get; init; }

    /// <summary>Sprites displayed as thumbnails (ambience object appearance types).</summary>
    public IReadOnlyList<uint>? SpriteIds { get; init; }
}

public static class MapLinks
{
    /// <summary>The tibiamaps.io URL used by the original for clickable coordinates.</summary>
    public static string TibiaMaps(uint x, uint y, uint z) => TibiaFoundry.Core.Net.ExternalEndpoints.TibiaMapsUrl(x, y, z);
}
