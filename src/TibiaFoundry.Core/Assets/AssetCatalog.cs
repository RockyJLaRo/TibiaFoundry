using System.Security.Cryptography;
using System.Text.Json;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Assets;

/// <summary>Sprite sheet layouts (catalog <c>spritetype</c>). Sizes are defined in <see cref="TibiaFormat.SpriteTypes"/>.</summary>
public enum SpriteSheetType
{
    /// <summary>32×32 sprites (12 × 12 per sheet).</summary>
    Small = 0,
    /// <summary>32 wide × 64 tall (12 columns × 6 rows). The original UI labeled this "64x32".</summary>
    Tall = 1,
    /// <summary>64 wide × 32 tall (6 columns × 12 rows). The original UI labeled this "32x64".</summary>
    Wide = 2,
    /// <summary>64×64 sprites (6 × 6 per sheet).</summary>
    Large = 3,
}

/// <summary>One <c>sprites-*.bmp.lzma</c> entry of the catalog.</summary>
public sealed record SpriteSheetInfo(string FileName, SpriteSheetType Type, int FirstSpriteId, int LastSpriteId, int Area)
{
    public const int SheetSize = TibiaFormat.SheetSize;

    public int SpriteWidth => TibiaFormat.SpriteTypes[(int)Type].Width;
    public int SpriteHeight => TibiaFormat.SpriteTypes[(int)Type].Height;
    public int Columns => SheetSize / SpriteWidth;
    public int Rows => SheetSize / SpriteHeight;
    public int SpriteCount => LastSpriteId - FirstSpriteId + 1;
    public string SizeLabel => $"{SpriteWidth}×{SpriteHeight}";
    public bool Contains(int spriteId) => spriteId >= FirstSpriteId && spriteId <= LastSpriteId;
    public override string ToString() => $"{FirstSpriteId}–{LastSpriteId} ({SizeLabel})";
}

/// <summary>
/// Parsed <c>catalog-content.json</c>.
/// </summary>
/// <remarks>
/// The parser is tolerant but never guesses: entries it cannot interpret (unknown sprite types,
/// missing or invalid numbers, unsafe file names, overlapping sprite ranges) are skipped and
/// listed in <see cref="Problems"/>, so one bad entry after a client update does not disable the
/// whole catalog, and a format change is visible in the Compatibility report instead of producing
/// wrong sprites. A catalog that is not a JSON array of entries is rejected as unsupported.
/// </remarks>
public sealed class AssetCatalog
{
    private readonly int[] _firstIds;

    private AssetCatalog(IClientFolder folder, List<SpriteSheetInfo> sheets, Dictionary<string, string> files,
        List<string> problems, Dictionary<string, int> unusedTypes, FileFingerprint fingerprint, string contentSha256)
    {
        ContentSha256 = contentSha256;
        Folder = folder;
        AssetsDirectory = folder.DisplayPath;
        Sheets = sheets;
        Files = files;
        Problems = problems;
        UnusedTypes = unusedTypes;
        Fingerprint = fingerprint;
        _firstIds = sheets.Select(s => s.FirstSpriteId).ToArray();
    }

    /// <summary>The assets folder as shown to the user (a real folder, or an imported version's description).</summary>
    public string AssetsDirectory { get; }

    /// <summary>Where the sheets and data files are read from: a real folder, or an imported version's manifest (7.3).</summary>
    public IClientFolder Folder { get; }

    /// <summary>Valid sprite sheets ordered by first sprite ID (no overlaps).</summary>
    public IReadOnlyList<SpriteSheetInfo> Sheets { get; }

    /// <summary>Non-sprite entries: asset type → file name.</summary>
    public IReadOnlyDictionary<string, string> Files { get; }

    /// <summary>Entries that were skipped and why (empty for a fully understood catalog).</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>Entry types present in the catalog that this version does not use (type → count).</summary>
    public IReadOnlyDictionary<string, int> UnusedTypes { get; }

    /// <summary>SHA-256 of catalog-content.json: identifies the exact sprite set (used for sprite-folder provenance).</summary>
    public string ContentSha256 { get; }

    /// <summary>Size and time stamp of the catalog file when it was read (detects client updates).</summary>
    public FileFingerprint Fingerprint { get; }

    public int SheetCount => Sheets.Count;
    public int TotalSprites => Sheets.Sum(s => s.SpriteCount);
    public int HighestSpriteId => Sheets.Count == 0 ? 0 : Sheets.Max(s => s.LastSpriteId);

    public static AssetCatalog Load(string assetsDirectory) => Load(new PhysicalClientFolder(assetsDirectory));

    /// <summary>Reads the catalog of an assets folder; for an imported version, through its manifest.</summary>
    public static AssetCatalog Load(IClientFolder folder)
    {
        string? path = folder.PathOf(TibiaFormat.AssetCatalogFile);
        if (path == null) throw new FileNotFoundException($"{TibiaFormat.AssetCatalogFile} was not found in the assets folder.", Path.Combine(folder.DisplayPath, TibiaFormat.AssetCatalogFile));
        return Parse(folder, File.ReadAllBytes(path), FileFingerprint.Of(path));
    }

    /// <summary>
    /// Parses catalog content that is not (yet) on disk, for example a catalog the Assets Editor is
    /// about to write, so it can be checked before it replaces the current one.
    /// </summary>
    public static AssetCatalog Parse(string assetsDirectory, byte[] bytes, FileFingerprint? fingerprint = null) =>
        Parse(new PhysicalClientFolder(assetsDirectory), bytes, fingerprint);

    public static AssetCatalog Parse(IClientFolder folder, byte[] bytes, FileFingerprint? fingerprint = null)
    {
        fingerprint ??= FileFingerprint.Of(Path.Combine(folder.DisplayPath, TibiaFormat.AssetCatalogFile));
        string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{TibiaFormat.AssetCatalogFile} is not valid JSON (damaged or an unsupported format): {ex.Message}", ex);
        }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"{TibiaFormat.AssetCatalogFile} has an unsupported structure (expected a list of entries, found {doc.RootElement.ValueKind}). The client format may have changed.");
            return Parse(folder, doc.RootElement, fingerprint.Value, sha);
        }
    }

    private static AssetCatalog Parse(IClientFolder folder, JsonElement root, FileFingerprint fingerprint, string sha)
    {
        var sheets = new List<SpriteSheetInfo>();
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        var unused = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int index = -1;
        foreach (var e in root.EnumerateArray())
        {
            index++;
            if (e.ValueKind != JsonValueKind.Object) { problems.Add(L.F("AssetCatalog.Entry0NotAnObjectSkipped", index)); continue; }
            string type = ReadString(e, "type") ?? "";
            string? rawFile = ReadString(e, "file");
            string? file = TibiaFormat.SafeFileName(rawFile);
            if (file == null) { problems.Add(L.F("AssetCatalog.Entry01MissingOrUnsafe", index, type, rawFile)); continue; }

            bool isSprite = type == TibiaFormat.CatalogTypeSprite || e.TryGetProperty("spritetype", out _);
            if (isSprite)
            {
                if (!TryReadInt(e, "spritetype", out int spriteType) || !TibiaFormat.SpriteTypes.ContainsKey(spriteType))
                {
                    problems.Add(L.F("AssetCatalog.Sheet0UnknownSpriteType1", file, ReadRaw(e, "spritetype"), string.Join(", ", TibiaFormat.SpriteTypes.Keys)));
                    continue;
                }
                if (!TryReadInt(e, "firstspriteid", out int first) || !TryReadInt(e, "lastspriteid", out int last) || first < 0 || last < first)
                {
                    problems.Add(L.F("AssetCatalog.Sheet0InvalidSpriteRange1", file, ReadRaw(e, "firstspriteid"), ReadRaw(e, "lastspriteid")));
                    continue;
                }
                TryReadInt(e, "area", out int area);
                sheets.Add(new SpriteSheetInfo(file, (SpriteSheetType)spriteType, first, last, area));
            }
            else if (type.Length == 0)
            {
                problems.Add(L.F("AssetCatalog.Entry01NoTypeSkipped", index, file));
            }
            else
            {
                if (!TibiaFormat.UsedCatalogTypes.Contains(type)) unused[type] = unused.GetValueOrDefault(type) + 1;
                if (!files.TryAdd(type, file)) problems.Add(L.F("AssetCatalog.Type0IsListedMoreThan", type, files[type]));
            }
        }

        sheets.Sort((a, b) => a.FirstSpriteId.CompareTo(b.FirstSpriteId));
        var valid = new List<SpriteSheetInfo>(sheets.Count);
        foreach (var sheet in sheets)
        {
            if (valid.Count > 0 && sheet.FirstSpriteId <= valid[^1].LastSpriteId)
            {
                problems.Add(L.F("AssetCatalog.Sheet0IDs12Overlaps", sheet.FileName, sheet.FirstSpriteId, sheet.LastSpriteId, valid[^1].FileName));
                continue;
            }
            valid.Add(sheet);
        }
        return new AssetCatalog(folder, valid, files, problems, unused, fingerprint, sha);
    }

    private static string? ReadString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string ReadRaw(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetRawText() : "(missing)";

    private static bool TryReadInt(JsonElement e, string name, out int value)
    {
        value = 0;
        if (!e.TryGetProperty(name, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(v.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    /// <summary>The sheet that contains <paramref name="spriteId"/>, or null.</summary>
    public SpriteSheetInfo? FindSheet(int spriteId)
    {
        int index = Array.BinarySearch(_firstIds, spriteId);
        if (index < 0) index = ~index - 1;
        if (index < 0) return null;
        var sheet = Sheets[index];
        return sheet.Contains(spriteId) ? sheet : null;
    }

    /// <summary>The sheet with the highest first ID of the given type ("newest sheet").</summary>
    public SpriteSheetInfo? NewestSheet(SpriteSheetType type) =>
        Sheets.Where(s => s.Type == type).MaxBy(s => s.FirstSpriteId);

    /// <summary>
    /// The file of a sheet. For a real folder this is where it is expected (callers check that it
    /// exists); for an imported version it is the stored object (or a path that does not exist when the
    /// version has no such file).
    /// </summary>
    public string GetSheetPath(SpriteSheetInfo sheet) => GetFilePath(sheet.FileName);

    /// <summary>The file of an entry of the catalog (a sheet or data file), resolved like <see cref="GetSheetPath"/>.</summary>
    public string GetFilePath(string fileName) =>
        Folder is PhysicalClientFolder physical ? physical.Expected(fileName) : Folder.PathOf(fileName) ?? Path.Combine(AssetsDirectory, fileName);
}

/// <summary>Size and last-write time of a file: a cheap way to notice that the client replaced it.</summary>
public readonly record struct FileFingerprint(string Path, long Length, DateTime LastWriteUtc)
{
    public static FileFingerprint Of(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new FileFingerprint(path, info.Length, info.LastWriteTimeUtc) : new FileFingerprint(path, -1, DateTime.MinValue);
    }

    public bool IsCurrent() => Of(Path) == this;
}
