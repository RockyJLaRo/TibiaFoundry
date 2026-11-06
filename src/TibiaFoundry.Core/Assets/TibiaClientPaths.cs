using System.Text.Json;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Compatibility;

namespace TibiaFoundry.Core.Assets;

/// <summary>Kinds of data files referenced by <c>catalog-content.json</c>.</summary>
public static class AssetTypes
{
    public const string Appearances = "appearances";
    public const string StaticData = "staticdata";
    public const string StaticMapData = "staticmapdata";
    public const string Map = "map";
    public const string Sounds = "sounds";
}

/// <summary>
/// Locates a Tibia client package (<c>%LOCALAPPDATA%\Tibia\packages\&lt;name&gt;</c>) and the
/// data files inside it.
/// </summary>
/// <remarks>
/// The original globbed <c>appearances*.dat</c> and took <c>[0]</c>. On Windows that pattern also
/// matches backup files such as <c>*.dat-bak</c> (8.3 short-name quirk), and it crashed when no
/// file existed. Here the authoritative <c>catalog-content.json</c> is used first and the glob
/// is only a fallback that requires an exact <c>.dat</c> extension.
/// </remarks>
public sealed class TibiaClientPaths
{
    public const string LivePackageName = TibiaFormat.LivePackage;
    public const string DefaultTestPackageName = TibiaFormat.DefaultTestPackage;

    public TibiaClientPaths(string packageDirectory)
    {
        PackageDirectory = packageDirectory;
    }

    public string PackageDirectory { get; }
    public string AssetsDirectory => Path.Combine(PackageDirectory, TibiaFormat.AssetsFolder);
    public string SoundsDirectory => Path.Combine(PackageDirectory, TibiaFormat.SoundsFolder);
    public bool AssetsExist => File.Exists(Path.Combine(AssetsDirectory, TibiaFormat.AssetCatalogFile));

    public static string PackagesRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tibia", "packages");

    public static TibiaClientPaths ForPackage(string packageName) => new(Path.Combine(PackagesRoot, packageName));

    /// <summary>Names of installed packages that contain client assets (e.g. "Tibia", "TibiaExternal").</summary>
    public static IReadOnlyList<string> FindInstalledPackages()
    {
        if (!Directory.Exists(PackagesRoot)) return Array.Empty<string>();
        return Directory.GetDirectories(PackagesRoot)
            .Where(d => File.Exists(Path.Combine(d, "assets", "catalog-content.json")))
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(n => n == LivePackageName ? 0 : 1).ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Full path of the data file of the given type (see <see cref="AssetTypes"/>), or null.</summary>
    public string? FindAssetFile(string type) => FindAssetFile(AssetsDirectory, type);

    public static string? FindAssetFile(string assetsDirectory, string type)
    {
        string? fromCatalog = FindInCatalog(new PhysicalClientFolder(assetsDirectory), TibiaFormat.AssetCatalogFile, type);
        return fromCatalog ?? FindNewest(assetsDirectory, type + "-*.dat");
    }

    /// <summary>The data file of the given type named by the catalog of an assets folder (an imported version's included), or null.</summary>
    public static string? FindAssetFile(IClientFolder assets, string type) =>
        assets.PhysicalPath is { } path ? FindAssetFile(path, type) : FindInCatalog(assets, TibiaFormat.AssetCatalogFile, type);

    public string? FindSoundsFile() => FindSoundsFile(SoundsDirectory);

    /// <summary>The sounds file of a sounds folder (from <c>catalog-sound.json</c>, else the newest <c>sounds-*.dat</c>), or null.</summary>
    public static string? FindSoundsFile(string soundsDirectory)
    {
        string? fromCatalog = FindInCatalog(new PhysicalClientFolder(soundsDirectory), TibiaFormat.SoundCatalogFile, AssetTypes.Sounds);
        return fromCatalog ?? FindNewest(soundsDirectory, "sounds-*.dat");
    }

    public static string? FindSoundsFile(IClientFolder sounds) =>
        sounds.PhysicalPath is { } path ? FindSoundsFile(path) : FindInCatalog(sounds, TibiaFormat.SoundCatalogFile, AssetTypes.Sounds);

    private static string? FindInCatalog(IClientFolder folder, string catalogName, string type)
    {
        string? catalogPath = folder.PathOf(catalogName);
        if (catalogPath == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null; // unsupported structure: fall back to the scan
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object &&
                    entry.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == type &&
                    entry.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.String &&
                    TibiaFormat.SafeFileName(f.GetString()) is { } file &&
                    folder.PathOf(file) is { } full)
                    return full;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged or locked catalog falls back to the file-system scan.
        }
        return null;
    }

    /// <summary>
    /// Client version from the package manifest (<c>package.json</c> → <c>"version"</c>, e.g.
    /// "15.33.b21348"), or null when the manifest is missing or unreadable.
    /// </summary>
    public string? ReadClientVersion() => ReadVersion(Path.Combine(PackageDirectory, TibiaFormat.PackageManifest));

    /// <summary>The <c>"version"</c> of a package manifest file, or null when it is missing or unreadable.</summary>
    public static string? ReadVersion(string manifest)
    {
        if (!File.Exists(manifest)) return null;
        try
        {
            using var stream = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FindNewest(string directory, string pattern)
    {
        if (!Directory.Exists(directory)) return null;
        string extension = Path.GetExtension(pattern);
        return Directory.EnumerateFiles(directory, pattern)
            .Where(f => string.Equals(Path.GetExtension(f), extension, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
