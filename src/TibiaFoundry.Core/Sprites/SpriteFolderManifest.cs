using System.Text.Json;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;

namespace TibiaFoundry.Core.Sprites;

/// <summary>How trustworthy an extracted sprites folder is for the current client.</summary>
public enum SpriteFolderState
{
    /// <summary>No folder configured.</summary>
    None,
    /// <summary>Extracted from exactly this client's sprite catalog.</summary>
    Current,
    /// <summary>Extracted from a different client version: sprite IDs may refer to other images now.</summary>
    Stale,
    /// <summary>No provenance information (made by the original 1.x program, by hand, or empty).</summary>
    Unknown,
}

/// <summary>
/// Provenance file written next to extracted sprites (<c>TibiaFoundry-sprites.json</c>). Sprite
/// IDs are reassigned between Tibia versions, so a folder extracted from an older client can show
/// the wrong image for an ID; the manifest lets the program detect that (audit finding A10).
/// </summary>
public sealed record SpriteFolderManifest(string Tool, string? ClientVersion, string CatalogSha256, DateTime ExtractedAtUtc, int SpriteCount)
{
    public const string FileName = AppIdentity.Id + "-sprites.json";

    /// <summary>Reads the manifest; folders extracted by versions 3.0 and 2.x carry it under the names those versions used.</summary>
    public static SpriteFolderManifest? Read(string folder)
    {
        string? path = AppIdentity.Legacy.SpriteFolderManifests.Prepend(FileName).Select(n => Path.Combine(folder, n)).FirstOrDefault(File.Exists);
        if (path == null) return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<SpriteFolderManifest>(File.ReadAllBytes(path));
            return manifest is { CatalogSha256.Length: > 0 } ? manifest : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Warn($"Unreadable sprite folder manifest {path}: {ex.Message}");
            return null;
        }
    }

    public void Write(string folder) =>
        AtomicFile.WriteAllBytes(Path.Combine(folder, FileName), JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>Classifies <paramref name="folder"/> against the catalog of the running client.</summary>
    public static SpriteFolderState StateOf(string? folder, string? catalogSha256, out SpriteFolderManifest? manifest)
    {
        manifest = null;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return SpriteFolderState.None;
        manifest = Read(folder);
        if (manifest == null || catalogSha256 == null) return SpriteFolderState.Unknown;
        return string.Equals(manifest.CatalogSha256, catalogSha256, StringComparison.OrdinalIgnoreCase) ? SpriteFolderState.Current : SpriteFolderState.Stale;
    }
}
