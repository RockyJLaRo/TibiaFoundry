using System.Diagnostics;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Clients;

/// <summary>
/// What the files of a folder say about its client version (8.0): every piece of evidence, and the
/// result. A folder's name, dates or a label are never evidence.
/// </summary>
public sealed class VersionReport
{
    public const string Verified = "Verified";
    public const string Conflicting = "Conflicting";
    public const string Unknown = "Unknown";

    /// <summary><see cref="Verified"/> (strong evidence that agrees), <see cref="Conflicting"/> (strong evidence that disagrees) or <see cref="Unknown"/>.</summary>
    public required string Status { get; init; }

    /// <summary>The version the strongest evidence gives (also when conflicting or only probable), or null.</summary>
    public string? Version { get; init; }

    public required IReadOnlyList<VersionClue> Clues { get; init; }

    /// <summary>For labels: "15.33.c9e45e", "15.33.c9e45e? (conflicting evidence)", "15.33.c9e45e? (probably)" or "Unknown version".</summary>
    public string Display => Status switch
    {
        Verified => Version!,
        Conflicting => L.F("VersionEvidence.0ConflictingEvidence", Version),
        _ => Version != null ? L.F("VersionEvidence.0Probably", Version) : L.T("VersionEvidence.UnknownVersion"),
    };

    /// <summary>Every clue on one line ("package.json: 15.33.c9e45e; ...").</summary>
    public string Details => Clues.Count == 0 ? L.T("VersionEvidence.NoVersionInformationWasFoundIn") : string.Join("; ", Clues.Select(c => $"{c.Source}: {c.Value}{(c.Strong ? "" : " (weak)")}"));

    public static VersionReport None { get; } = new() { Status = Unknown, Clues = [] };
}

/// <summary>Collects the version evidence of a client package folder or an assets folder.</summary>
public static class VersionEvidence
{
    public const string FromPackageJson = "package.json";
    public const string FromVersionFile = "package.json.version";
    public const string FromExecutable = "client.exe version resource";

    /// <summary>
    /// Reads the evidence. <paramref name="knownContent"/> maps the SHA-256 of files stored in
    /// revisions (assets.json, catalog-content.json, appearances data) to their version, and
    /// "name:&lt;file name&gt;" to the version of a stored revision with that data file name (weak).
    /// </summary>
    public static VersionReport Collect(string folder, IReadOnlyDictionary<string, string>? knownContent = null)
    {
        var clues = new List<VersionClue>();
        string package = folder, assets = Path.Combine(folder, TibiaFormat.AssetsFolder);
        if (!File.Exists(Path.Combine(assets, TibiaFormat.AssetCatalogFile)) && File.Exists(Path.Combine(folder, TibiaFormat.AssetCatalogFile)))
            assets = folder;   // an assets folder on its own

        if (TibiaClientPaths.ReadVersion(Path.Combine(package, TibiaFormat.PackageManifest)) is { } fromPackage)
            clues.Add(new VersionClue(FromPackageJson, fromPackage, true));
        if (ReadText(Path.Combine(package, ClientScanner.VersionFile)) is { } fromFile)
            clues.Add(new VersionClue(FromVersionFile, fromFile, true));
        string exe = Path.Combine(package, "bin", "client.exe");
        if (File.Exists(exe))
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                string? v = !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion : info.FileVersion;
                if (!string.IsNullOrWhiteSpace(v)) clues.Add(new VersionClue(FromExecutable, v.Trim(), true));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // No version resource can be read: not evidence either way.
            }
        }
        if (knownContent is { Count: > 0 })
        {
            foreach (var (path, what) in new[]
                     {
                         (Path.Combine(package, ClientScanner.AssetsJson), "assets.json"),
                         (Path.Combine(assets, TibiaFormat.AssetCatalogFile), "catalog-content.json"),
                         (TibiaClientPaths.FindAssetFile(assets, AssetTypes.Appearances), L.T("VersionEvidence.AppearancesData")),
                     })
            {
                if (path == null || !File.Exists(path)) continue;
                try
                {
                    string sha = FileHashing.HashFile(path);
                    if (knownContent.TryGetValue(sha, out string? known)) clues.Add(new VersionClue(L.F("VersionEvidence.0IdenticalToAStoredRevision", what), known, true));
                    else if (what == L.T("VersionEvidence.AppearancesData") && knownContent.TryGetValue("name:" + Path.GetFileName(path), out string? byName))
                        clues.Add(new VersionClue(L.T("VersionEvidence.AppearancesFileNameOfAStored"), byName, false));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Unreadable: no evidence.
                }
            }
        }
        return Judge(clues);
    }

    /// <summary>The result of a set of clues: strong clues must agree (an executable's resource only on major.minor).</summary>
    public static VersionReport Judge(IReadOnlyList<VersionClue> clues)
    {
        var strong = clues.Where(c => c.Strong).ToList();
        var full = strong.Where(c => c.Source != FromExecutable).ToList();
        string? version = full.FirstOrDefault()?.Value ?? clues.FirstOrDefault(c => c.Source != FromExecutable)?.Value ?? strong.FirstOrDefault()?.Value;
        if (strong.Count == 0) return new VersionReport { Status = VersionReport.Unknown, Version = version, Clues = clues };
        bool conflict = full.Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        var exe = strong.FirstOrDefault(c => c.Source == FromExecutable);
        if (exe != null && full.Count > 0 && MajorMinor(exe.Value) is { } a && MajorMinor(full[0].Value) is { } b && a != b) conflict = true;
        return new VersionReport { Status = conflict ? VersionReport.Conflicting : VersionReport.Verified, Version = version, Clues = clues };
    }

    private static string? MajorMinor(string version)
    {
        var parts = version.Split('.');
        return parts.Length >= 2 && int.TryParse(parts[0], out int major) && int.TryParse(parts[1], out int minor) ? $"{major}.{minor}" : null;
    }

    private static string? ReadText(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 256) return null;
            string text = File.ReadAllText(path).Trim();
            return text.Length > 0 && !text.Contains('\n') ? text : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
