using TibiaFoundry.Core.Metadata;
using System.Collections.Concurrent;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Sprites;

/// <summary>Provides sprite images by sprite ID.</summary>
public interface ISpriteSource
{
    /// <summary>Returns the sprite, or null when it cannot be found.</summary>
    RgbaImage? GetSprite(uint spriteId);
}

/// <summary>Where sprites are read from.</summary>
public enum SpriteSourceMode
{
    /// <summary>Extracted PNG folder first; client sheets when a file is missing (default).</summary>
    FolderThenClient,
    /// <summary>Only the extracted PNG folder (the original's behavior).</summary>
    FolderOnly,
    /// <summary>Decode directly from the client's sprite sheets.</summary>
    ClientOnly,
}

/// <summary>
/// Thread-safe sprite provider. The original only read <c>&lt;id&gt;.png</c> files from a
/// pre-extracted folder and crashed when one was missing; this repository can also decode the
/// sheet that contains the sprite directly from the client (with an LRU cache of sheets) and,
/// like the original "Extract on the fly" option, optionally write decoded sheets to the folder.
/// </summary>
public sealed class SpriteRepository : ISpriteSource
{
    public const int DefaultCachedSheets = 96;
    private const int MaxCachedFiles = 4096;
    private readonly int _maxCachedSheets;

    private readonly object _lruLock = new();
    private readonly Dictionary<int, LinkedListNode<(int Key, Lazy<RgbaImage[]> Value)>> _sheetIndex = new();
    private readonly LinkedList<(int Key, Lazy<RgbaImage[]> Value)> _sheetLru = new();
    private readonly ConcurrentDictionary<uint, RgbaImage> _files = new();

    private int _sizeMismatches;
    private int _mixWarnings;

    /// <param name="maxCachedSheets">Decoded sheets kept in memory (Settings → Performance), each about 0.6 MB.</param>
    public SpriteRepository(AssetCatalog? catalog, string? spritesFolder, SpriteSourceMode mode = SpriteSourceMode.FolderThenClient, string? clientVersion = null,
        int maxCachedSheets = DefaultCachedSheets)
    {
        _maxCachedSheets = Math.Max(1, maxCachedSheets);
        Catalog = catalog;
        SpritesFolder = string.IsNullOrWhiteSpace(spritesFolder) ? null : spritesFolder;
        Mode = mode;
        ClientVersion = clientVersion;
        SpriteFolderManifest? manifest = null;
        FolderState = mode == SpriteSourceMode.ClientOnly ? SpriteFolderState.None
            : SpriteFolderManifest.StateOf(SpritesFolder, catalog?.ContentSha256, out manifest);
        FolderManifest = manifest;
        if (FolderState == SpriteFolderState.Stale)
        {
            FolderNotice = L.F("SpriteRepository.TheSpritesFolder0WasExtracted", SpritesFolder, manifest?.ClientVersion ?? L.T("ClientStartup.Unknown2"), manifest?.ExtractedAtUtc.ToLocalTime()) +
                           (mode == SpriteSourceMode.FolderOnly
                               ? L.T("SpriteRepository.ExtractedFolderOnlyIsSelectedSo")
                               : L.T("SpriteRepository.SpritesAreReadFromTheClient"));
            AppLog.Warn(FolderNotice);
        }
    }

    public string? ClientVersion { get; }

    /// <summary>Provenance of <see cref="SpritesFolder"/> compared with the current client.</summary>
    public SpriteFolderState FolderState { get; }
    public SpriteFolderManifest? FolderManifest { get; }

    /// <summary>Message for the user when the folder cannot be trusted (null when fine).</summary>
    public string? FolderNotice { get; }

    /// <summary>Folder sprites ignored because their size did not match the client's sprite size for that ID.</summary>
    public int SizeMismatches => Volatile.Read(ref _sizeMismatches);

    public AssetCatalog? Catalog { get; }
    public string? SpritesFolder { get; }
    public SpriteSourceMode Mode { get; }

    /// <summary>When true, sheets decoded from the client are also saved into <see cref="SpritesFolder"/>.</summary>
    public bool SaveDecodedSheetsToFolder { get; set; }

    public RgbaImage? GetSprite(uint spriteId)
    {
        var sheetForId = Catalog?.FindSheet((int)Math.Min(spriteId, int.MaxValue));
        // A folder from another client version is only used for IDs the client does not have,
        // unless the user explicitly chose "Extracted folder only".
        bool useFolder = Mode == SpriteSourceMode.FolderOnly || (FolderState != SpriteFolderState.Stale || sheetForId == null);
        if (Mode != SpriteSourceMode.ClientOnly && SpritesFolder != null && useFolder)
        {
            if (_files.TryGetValue(spriteId, out var cached)) return cached;
            string path = Path.Combine(SpritesFolder, spriteId + ".png");
            if (File.Exists(path))
            {
                try
                {
                    var image = ImageIO.Load(path);
                    if (sheetForId != null && (image.Width != sheetForId.SpriteWidth || image.Height != sheetForId.SpriteHeight) && Mode != SpriteSourceMode.FolderOnly)
                    {
                        // Wrong size for this ID in the current client: the file belongs to another version.
                        if (Interlocked.Increment(ref _sizeMismatches) <= 5)
                            AppLog.Warn($"Sprite {spriteId} in {SpritesFolder} is {image.Width}×{image.Height}, the client's is {sheetForId.SpriteWidth}×{sheetForId.SpriteHeight}; using the client sprite.");
                    }
                    else
                    {
                        if (_files.Count > MaxCachedFiles) _files.Clear();
                        _files[spriteId] = image;
                        return image;
                    }
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or InvalidDataException or UnauthorizedAccessException)
                {
                    AppLog.Warn($"Could not read sprite file {path}: {ex.Message}");
                }
            }
        }
        if (Mode == SpriteSourceMode.FolderOnly || Catalog == null) return null;

        var sheet = sheetForId;
        if (sheet == null) return null;
        var sprites = GetSheetSprites(sheet);
        int index = (int)spriteId - sheet.FirstSpriteId;
        return sprites != null && index < sprites.Length ? sprites[index] : null;
    }

    /// <summary>True when the sprite can be resolved (file or client sheet).</summary>
    public bool Exists(uint spriteId) =>
        (Mode != SpriteSourceMode.ClientOnly && SpritesFolder != null && File.Exists(Path.Combine(SpritesFolder, spriteId + ".png"))) ||
        (Mode != SpriteSourceMode.FolderOnly && Catalog?.FindSheet((int)spriteId) != null);

    private RgbaImage[]? GetSheetSprites(SpriteSheetInfo sheet)
    {
        Lazy<RgbaImage[]> lazy;
        lock (_lruLock)
        {
            if (_sheetIndex.TryGetValue(sheet.FirstSpriteId, out var node))
            {
                _sheetLru.Remove(node);
                _sheetLru.AddFirst(node);
                lazy = node.Value.Value;
            }
            else
            {
                lazy = new Lazy<RgbaImage[]>(() => LoadSheet(sheet), LazyThreadSafetyMode.ExecutionAndPublication);
                _sheetIndex[sheet.FirstSpriteId] = _sheetLru.AddFirst((sheet.FirstSpriteId, lazy));
                while (_sheetLru.Count > _maxCachedSheets)
                {
                    var last = _sheetLru.Last!;
                    _sheetLru.RemoveLast();
                    _sheetIndex.Remove(last.Value.Key);
                }
            }
        }
        try
        {
            return lazy.Value;
        }
        catch (Exception ex)
        {
            lock (_lruLock)
            {
                if (_sheetIndex.Remove(sheet.FirstSpriteId, out var node)) _sheetLru.Remove(node);
            }
            AppLog.Warn($"Could not decode sprite sheet {sheet.FileName}: {ex.Message}");
            return null;
        }
    }

    private RgbaImage[] LoadSheet(SpriteSheetInfo sheet)
    {
        var sprites = SpriteSheetDecoder.DecodeSprites(Catalog!, sheet);
        if (SaveDecodedSheetsToFolder && SpritesFolder != null && !CanWriteToFolder())
        {
            if (Interlocked.Increment(ref _mixWarnings) == 1)
                AppLog.Warn($"Decoded sheets are not saved to {SpritesFolder}: it holds sprites of another or unknown client version, and mixing versions would make it unreliable.");
        }
        else if (SaveDecodedSheetsToFolder && SpritesFolder != null)
        {
            try
            {
                Directory.CreateDirectory(SpritesFolder);
                if (FolderState != SpriteFolderState.Current && Catalog != null)
                    new SpriteFolderManifest(Common.AppIdentity.Id, ClientVersion, Catalog.ContentSha256, DateTime.UtcNow, Catalog.TotalSprites).Write(SpritesFolder);
                var attribution = Attribution.ForNewAsset();
                for (int i = 0; i < sprites.Length; i++)
                {
                    string path = Path.Combine(SpritesFolder, (sheet.FirstSpriteId + i) + ".png");
                    if (!File.Exists(path)) ImageIO.SavePng(sprites[i], path, attribution);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn($"Could not save sheet {sheet} to {SpritesFolder}: {ex.Message}");
            }
        }
        return sprites;
    }

    /// <summary>Decoded sheets may only be added to a folder of this exact client, or to an empty new folder.</summary>
    private bool CanWriteToFolder() =>
        FolderState == SpriteFolderState.Current ||
        (FolderState is SpriteFolderState.Unknown or SpriteFolderState.None &&
         (!Directory.Exists(SpritesFolder) || !Directory.EnumerateFiles(SpritesFolder!, "*.png").Any()));

    /// <summary>Number of decoded sheets currently held in memory.</summary>
    public int CachedSheets
    {
        get
        {
            lock (_lruLock) return _sheetLru.Count;
        }
    }

    public int MaxCachedSheets => _maxCachedSheets;

    public void ClearCache()
    {
        lock (_lruLock)
        {
            _sheetIndex.Clear();
            _sheetLru.Clear();
        }
        _files.Clear();
    }
}
