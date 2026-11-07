using System.Security.Cryptography;
using Google.Protobuf;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Editing.Model;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Protobuf;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Editing;

/// <summary>Outcome of saving a project.</summary>
public sealed class ProjectSaveResult
{
    public string Folder { get; set; } = "";
    public string AppearancesFile { get; set; } = "";
    public int ObjectsChanged { get; set; }
    public int SpritesAdded { get; set; }
    public int SpritesReplaced { get; set; }
    public List<string> SheetsWritten { get; } = [];
    public int FilesCopied { get; set; }
    public List<string> Notes { get; } = [];
    public string? Validation { get; set; }
}

/// <summary>
/// An assets folder opened for editing: the client's catalog, its appearances in editable form,
/// and the sprites (the client's sheets plus sprites added or replaced in this session).
/// Nothing is written until <see cref="Save"/>; a save writes new files under new names (the
/// client names its files after their SHA-256) and replaces catalog-content.json last, so an
/// interrupted save leaves the folder as it was.
/// </summary>
public sealed class AssetsProject : ISpriteSource
{
    /// <summary>Temporary IDs of sprites added in this session; final IDs are assigned when saving.</summary>
    public const uint FirstNewSpriteId = 0x80000000;

    private const int SheetSize = TibiaFormat.SheetSize;
    private const uint EmptyPixel = 0x00FF00FF;

    private readonly Dictionary<uint, RgbaImage> _replaced = new();
    private readonly List<RgbaImage> _added = [];
    private readonly HashSet<(AppearanceKind, uint)> _modified = [];
    private SpriteRepository _sprites;
    private CatalogDocument _catalogDocument;
    private bool _structureChanged;
    private uint? _blankSprite;

    private AssetsProject(string folder, AssetCatalog catalog, CatalogDocument document, EditableAppearances appearances, string appearancesFile, int sheetCache)
    {
        Folder = folder;
        Catalog = catalog;
        _catalogDocument = document;
        Appearances = appearances;
        AppearancesFile = appearancesFile;
        SheetCache = sheetCache;
        _sprites = new SpriteRepository(catalog, null, SpriteSourceMode.ClientOnly, maxCachedSheets: sheetCache);
    }

    public string Folder { get; private set; }
    public AssetCatalog Catalog { get; private set; }
    public EditableAppearances Appearances { get; private set; }
    public string AppearancesFile { get; private set; }
    public int SheetCache { get; }

    /// <summary>True when the folder belongs to an installed Tibia client; such a folder is never saved in place.</summary>
    public bool IsClientInstallation => IsInsideClientInstallation(Folder);

    /// <summary>
    /// True for the installed clients' folders and, since 7.3, the client store and the client folders
    /// registered in it (<see cref="Protect"/>): Tibia Foundry never writes into any of them.
    /// </summary>
    public static bool IsInsideClientInstallation(string folder) =>
        (Directory.Exists(TibiaClientPaths.PackagesRoot) && PathGuard.IsSameOrInside(TibiaClientPaths.PackagesRoot, folder))
        || (ExtraClientRootForTests is { } extra && PathGuard.IsSameOrInside(extra, folder))
        || ProtectedRoots().Any(root => !string.IsNullOrWhiteSpace(root) && PathGuard.IsSameOrInside(root, folder));

    private static readonly List<(WeakReference<object> Owner, Func<object, IEnumerable<string>> Folders)> Protectors = [];

    /// <summary>
    /// Adds folders that are never written to: the client store and the registered client folders of
    /// <paramref name="owner"/> (a client library), asked for each time. Every live owner counts, so
    /// two libraries (tests run several at once) never replace each other's protection.
    /// </summary>
    public static void Protect(object owner, Func<object, IEnumerable<string>> folders)
    {
        lock (Protectors)
        {
            Protectors.RemoveAll(p => !p.Owner.TryGetTarget(out var o) || ReferenceEquals(o, owner));
            Protectors.Add((new WeakReference<object>(owner), folders));
        }
    }

    private static List<string> ProtectedRoots()
    {
        var live = new List<(object Owner, Func<object, IEnumerable<string>> Folders)>();
        lock (Protectors)
            foreach (var (owner, folders) in Protectors)
                if (owner.TryGetTarget(out var o)) live.Add((o, folders));
        return live.SelectMany(p => p.Folders(p.Owner)).ToList();
    }

    /// <summary>
    /// Tests only: a temporary folder treated like the installed client, so the refusal to save into
    /// the client can be tested without ever writing near the real one. Per thread.
    /// </summary>
    [ThreadStatic] internal static string? ExtraClientRootForTests;

    public bool IsModified => _modified.Count > 0 || _replaced.Count > 0 || _added.Count > 0 || _structureChanged;
    public int ModifiedObjectCount => _modified.Count;
    public int AddedSpriteCount => _added.Count;
    public int ReplacedSpriteCount => _replaced.Count;

    public static AssetsProject Open(string folder, int sheetCache = SpriteRepository.DefaultCachedSheets)
    {
        folder = PathGuard.Normalize(folder);
        string catalogPath = Path.Combine(folder, TibiaFormat.AssetCatalogFile);
        if (!File.Exists(catalogPath)) throw new FileNotFoundException("This folder has no catalog-content.json, so it is not a Tibia assets folder.", catalogPath);
        byte[] catalogBytes = File.ReadAllBytes(catalogPath);
        var catalog = AssetCatalog.Parse(folder, catalogBytes, FileFingerprint.Of(catalogPath));
        if (catalog.Problems.Count > 0) AppLog.Warn($"Assets Editor: catalog of {folder} has {L.Count(catalog.Problems.Count, "Noun.problem")}: {string.Join(" | ", catalog.Problems.Take(5))}");
        var document = CatalogWriter.Read(catalogBytes);
        string appearancesFile = catalog.Files.TryGetValue(TibiaFormat.CatalogTypeAppearances, out var name)
            ? name : throw new InvalidDataException("catalog-content.json lists no appearances file.");
        byte[] bytes = ProtoLoader.ReadShared(Path.Combine(folder, appearancesFile));
        var appearances = EditableAppearances.Parser.ParseFrom(bytes);
        return new AssetsProject(folder, catalog, document, appearances, appearancesFile, sheetCache);
    }

    // ------------------------------------------------------------------ objects

    public IReadOnlyList<EditableAppearance> List(AppearanceKind kind) => Appearances.List(kind);

    public EditableAppearance? Find(AppearanceKind kind, uint id) => Appearances.List(kind).FirstOrDefault(a => a.Id == id);

    public bool IsObjectModified(AppearanceKind kind, uint id) => _modified.Contains((kind, id));

    /// <summary>Records that an object was edited (called by the editor after every change).</summary>
    public void MarkModified(AppearanceKind kind, uint id) => _modified.Add((kind, id));

    public static uint FirstId(AppearanceKind kind) => kind == AppearanceKind.Object ? 100u : 1u;

    /// <summary>The lowest unused ID above the highest one in use.</summary>
    public uint NextFreeId(AppearanceKind kind)
    {
        var list = Appearances.List(kind);
        return list.Count == 0 ? FirstId(kind) : Math.Max(FirstId(kind), list.Max(a => a.Id) + 1);
    }

    /// <summary>A new object with one frame group showing an empty 32×32 sprite, added at the end of its list.</summary>
    public EditableAppearance CreateObject(AppearanceKind kind)
    {
        var list = Appearances.List(kind);
        // Use the frame group type the client uses for this kind.
        int groupType = list.SelectMany(a => a.FrameGroup).Select(g => g.FixedFrameGroup).GroupBy(t => t).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault(kind == AppearanceKind.Outfit ? 0 : 2);
        var appearance = new EditableAppearance
        {
            Id = NextFreeId(kind),
            Flags = new AppearanceFlags(),
        };
        var info = new SpriteInfo { PatternWidth = 1, PatternHeight = 1, PatternDepth = 1, Layers = 1 };
        // One shared transparent sprite for new objects (the client's sprite 0 is not empty).
        if (_blankSprite == null || GetSprite(_blankSprite.Value) == null) _blankSprite = AddSprite(new RgbaImage(32, 32));
        info.SpriteId.Add(_blankSprite.Value);
        appearance.FrameGroup.Add(new FrameGroup { FixedFrameGroup = groupType, Id = 0, SpriteInfo = info });
        list.Add(appearance);
        _modified.Add((kind, appearance.Id));
        _structureChanged = true;
        return appearance;
    }

    /// <summary>Copies an object (all flags, frame groups and sprite references) under a new ID.</summary>
    public EditableAppearance Duplicate(AppearanceKind kind, uint id)
    {
        var source = Find(kind, id) ?? throw new ArgumentException($"{kind} {id} does not exist.");
        var copy = source.Clone();
        copy.Id = NextFreeId(kind);
        Appearances.List(kind).Add(copy);
        _modified.Add((kind, copy.Id));
        _structureChanged = true;
        return copy;
    }

    public bool Delete(AppearanceKind kind, uint id)
    {
        var list = Appearances.List(kind);
        var item = list.FirstOrDefault(a => a.Id == id);
        if (item == null) return false;
        list.Remove(item);
        _modified.Remove((kind, id));
        _structureChanged = true;
        return true;
    }

    /// <summary>Gives an object another ID. Returns the reason when the ID cannot be used.</summary>
    public string? ChangeId(AppearanceKind kind, uint id, uint newId)
    {
        if (newId == id) return null;
        if (newId < FirstId(kind)) return L.F("AssetsProject.0IDsStartAt1", kind, FirstId(kind));
        if (Find(kind, newId) != null) return L.F("AssetsProject.01AlreadyExists", kind, newId);
        var item = Find(kind, id);
        if (item == null) return L.F("AssetsProject.01DoesNotExist", kind, id);
        item.Id = newId;
        // The client's lists are ordered by ID.
        var list = Appearances.List(kind);
        var sorted = list.OrderBy(a => a.Id).ToList();
        list.Clear();
        list.AddRange(sorted);
        _modified.Remove((kind, id));
        _modified.Add((kind, newId));
        _structureChanged = true;
        return null;
    }

    /// <summary>Restores one object as it is in the folder on disk (discarding its unsaved edits).</summary>
    public bool Revert(AppearanceKind kind, uint id)
    {
        var saved = EditableAppearances.Parser.ParseFrom(ProtoLoader.ReadShared(Path.Combine(Folder, AppearancesFile))).List(kind).FirstOrDefault(a => a.Id == id);
        var list = Appearances.List(kind);
        int index = list.ToList().FindIndex(a => a.Id == id);
        if (saved == null || index < 0) return false;
        list[index] = saved;
        _modified.Remove((kind, id));
        return true;
    }

    // ------------------------------------------------------------------ sprites

    public bool IsNewSprite(uint id) => id >= FirstNewSpriteId;

    public bool IsReplacedSprite(uint id) => _replaced.ContainsKey(id);

    /// <summary>The sprite as it is in this session (new and replaced sprites included).</summary>
    public RgbaImage? GetSprite(uint spriteId)
    {
        if (spriteId >= FirstNewSpriteId)
        {
            long index = spriteId - FirstNewSpriteId;
            return index < _added.Count ? _added[(int)index] : null;
        }
        return _replaced.TryGetValue(spriteId, out var image) ? image : _sprites.GetSprite(spriteId);
    }

    /// <summary>Width and height of a sprite, or null when it does not exist.</summary>
    public (int Width, int Height)? SpriteSize(uint spriteId)
    {
        if (spriteId >= FirstNewSpriteId)
        {
            long index = spriteId - FirstNewSpriteId;
            return index < _added.Count ? (_added[(int)index].Width, _added[(int)index].Height) : null;
        }
        var sheet = Catalog.FindSheet((int)spriteId);
        return sheet == null ? null : (sheet.SpriteWidth, sheet.SpriteHeight);
    }

    /// <summary>The client's sprite sizes: 32×32, 32×64 (tall), 64×32 (wide) and 64×64.</summary>
    public static SpriteSheetType? TypeForSize(int width, int height) =>
        TibiaFormat.SpriteTypes.Where(t => t.Value.Width == width && t.Value.Height == height).Select(t => (SpriteSheetType?)t.Key).FirstOrDefault();

    /// <summary>Adds a sprite and returns its temporary ID (the final ID is assigned when saving).</summary>
    public uint AddSprite(RgbaImage image)
    {
        if (TypeForSize(image.Width, image.Height) == null)
            throw new ArgumentException($"A {image.Width}×{image.Height} image cannot be a sprite: the client uses 32×32, 32×64, 64×32 and 64×64.");
        _added.Add(Normalize(image));
        return FirstNewSpriteId + (uint)(_added.Count - 1);
    }

    /// <summary>Replaces a sprite's image (same size as before).</summary>
    public void ReplaceSprite(uint spriteId, RgbaImage image)
    {
        var size = SpriteSize(spriteId) ?? throw new ArgumentException($"Sprite {spriteId} does not exist.");
        if (size != (image.Width, image.Height))
            throw new ArgumentException($"Sprite {spriteId} is {size.Width}×{size.Height}; the new image is {image.Width}×{image.Height}. A replacement must have the same size.");
        if (spriteId >= FirstNewSpriteId) _added[(int)(spriteId - FirstNewSpriteId)] = Normalize(image);
        else _replaced[spriteId] = Normalize(image);
    }

    /// <summary>Fully transparent pixels get the client's color (magenta) so new sheets match the client's.</summary>
    private static RgbaImage Normalize(RgbaImage image)
    {
        var copy = image.Clone();
        for (int i = 0; i < copy.Pixels.Length; i++)
            if (copy.Pixels[i] >> 24 == 0) copy.Pixels[i] = EmptyPixel;
        return copy;
    }

    /// <summary>Objects that use a sprite (for "where is this sprite used").</summary>
    public IEnumerable<(AppearanceKind Kind, uint Id)> UsesOf(uint spriteId)
    {
        foreach (var kind in Enum.GetValues<AppearanceKind>())
            foreach (var a in Appearances.List(kind))
                if (a.SpriteIds().Contains(spriteId)) yield return (kind, a.Id);
    }

    /// <summary>Sprite IDs used by objects that are not in any sheet and not new (damaged references).</summary>
    public IReadOnlyList<(AppearanceKind Kind, uint Id, uint SpriteId)> MissingSprites()
    {
        var list = new List<(AppearanceKind, uint, uint)>();
        foreach (var kind in Enum.GetValues<AppearanceKind>())
            foreach (var a in Appearances.List(kind))
                foreach (uint s in a.SpriteIds())
                    if (SpriteSize(s) == null) list.Add((kind, a.Id, s));
        return list;
    }

    // ------------------------------------------------------------------ saving

    /// <summary>
    /// Saves to <paramref name="targetFolder"/>. Saving to another folder first copies every file
    /// of the current folder there (a complete client assets folder); saving in place only adds
    /// files and replaces catalog-content.json (backed up through <paramref name="output"/>).
    /// </summary>
    public ProjectSaveResult Save(string targetFolder, OutputSession output, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        targetFolder = PathGuard.Normalize(targetFolder);
        bool inPlace = string.Equals(targetFolder, Folder, StringComparison.OrdinalIgnoreCase);
        if (inPlace && IsClientInstallation)
            throw new InvalidOperationException("This is the folder of an installed Tibia client. Save a copy to another folder instead: changing the client's own files may break it, and the launcher replaces them on its next update.");
        if (!inPlace && PathGuard.Overlaps(targetFolder, Folder))
            throw new InvalidOperationException("The target folder must not be inside the folder being edited (or contain it).");
        if (!inPlace && Directory.Exists(targetFolder) && Directory.EnumerateFileSystemEntries(targetFolder).Any())
            throw new InvalidOperationException("The target folder is not empty. Choose an empty or new folder so no existing files are mixed with the saved assets.");
        var result = new ProjectSaveResult { Folder = targetFolder, ObjectsChanged = _modified.Count, SpritesAdded = _added.Count, SpritesReplaced = _replaced.Count };

        // 1. Final IDs for new sprites, grouped by size so each new sheet covers one ID range.
        var appearances = Appearances.Clone();
        int nextId = Catalog.HighestSpriteId + 1;
        var idMap = new Dictionary<uint, uint>();
        var newSheets = new List<(SpriteSheetType Type, int First, List<RgbaImage> Sprites)>();
        foreach (var group in _added.Select((image, index) => (image, index)).GroupBy(x => TypeForSize(x.image.Width, x.image.Height)!.Value).OrderBy(g => g.Key))
        {
            var (w, h) = TibiaFormat.SpriteTypes[(int)group.Key];
            int capacity = (SheetSize / w) * (SheetSize / h);
            foreach (var chunk in group.Chunk(capacity))
            {
                newSheets.Add((group.Key, nextId, chunk.Select(c => c.image).ToList()));
                foreach (var (_, index) in chunk) idMap[FirstNewSpriteId + (uint)index] = (uint)nextId++;
            }
        }
        foreach (var kind in Enum.GetValues<AppearanceKind>())
            foreach (var a in appearances.List(kind))
                foreach (var g in a.FrameGroup.Where(g => g.SpriteInfo != null))
                    for (int i = 0; i < g.SpriteInfo.SpriteId.Count; i++)
                        if (g.SpriteInfo.SpriteId[i] >= FirstNewSpriteId)
                            g.SpriteInfo.SpriteId[i] = idMap.TryGetValue(g.SpriteInfo.SpriteId[i], out uint final) ? final
                                : throw new InvalidDataException($"{kind} {a.Id} refers to a new sprite that no longer exists.");

        // 2. Encode changed and new sheets (in memory; sheets are 0.6 MB each before compression).
        var files = new List<(string Name, byte[] Data)>();
        var document = _catalogDocument.Clone();
        var entries = document.Entries;
        var expectations = new List<(uint Id, RgbaImage Image)>();
        int step = 0, steps = 2 + newSheets.Count + _replaced.Keys.Select(id => Catalog.FindSheet((int)id)).Distinct().Count();
        foreach (var sheetGroup in _replaced.GroupBy(r => Catalog.FindSheet((int)r.Key)!))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new OperationProgress(++step, steps, L.T("AssetsProject.ReEncodingSpriteSheets")));
            var sheet = sheetGroup.Key;
            var image = SpriteSheetDecoder.DecodeSheet(Catalog.GetSheetPath(sheet));
            foreach (var (id, sprite) in sheetGroup)
            {
                int index = (int)id - sheet.FirstSpriteId;
                image.CopyImage(sprite, index % sheet.Columns * sheet.SpriteWidth, index / sheet.Columns * sheet.SpriteHeight);
                expectations.Add((id, sprite));
            }
            var (name, data) = EncodeSheet(image, ct);
            files.Add((name, data));
            var entry = entries.First(e => e.Type == TibiaFormat.CatalogTypeSprite && e.File == sheet.FileName);
            entry.SetString("file", name);
            result.SheetsWritten.Add(name);
        }
        foreach (var (type, first, sprites) in newSheets)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new OperationProgress(++step, steps, L.T("AssetsProject.CreatingSpriteSheets")));
            var (w, h) = TibiaFormat.SpriteTypes[(int)type];
            int columns = SheetSize / w;
            var image = new RgbaImage(SheetSize, SheetSize);
            image.Clear(EmptyPixel);
            for (int i = 0; i < sprites.Count; i++)
            {
                image.CopyImage(sprites[i], i % columns * w, i / columns * h);
                expectations.Add(((uint)(first + i), sprites[i]));
            }
            var (name, data) = EncodeSheet(image, ct);
            files.Add((name, data));
            entries.Add(CatalogEntry.Sprite(name, type, first, first + sprites.Count - 1));
            result.SheetsWritten.Add(name);
        }

        // 3. Appearances, named after their SHA-256 like the client's.
        byte[] appearanceBytes = appearances.ToByteArray();
        string appearancesName = AppearancesFile;
        if (_modified.Count > 0 || _structureChanged || idMap.Count > 0)
        {
            appearancesName = $"appearances-{Convert.ToHexStringLower(SHA256.HashData(appearanceBytes))}.dat";
            files.Add((appearancesName, appearanceBytes));
            entries.First(e => e.Type == TibiaFormat.CatalogTypeAppearances).SetString("file", appearancesName);
        }
        byte[] catalogBytes = CatalogWriter.Write(document);

        // 4. Check the new catalog before anything is written.
        var newCatalog = AssetCatalog.Parse(targetFolder, catalogBytes);
        if (newCatalog.Problems.Count > Catalog.Problems.Count)
            throw new InvalidDataException("The new catalog would have problems: " + string.Join(" | ", newCatalog.Problems.Except(Catalog.Problems).Take(5)));
        foreach (var kind in Enum.GetValues<AppearanceKind>())
            foreach (var a in appearances.List(kind))
                foreach (uint s in a.SpriteIds())
                    if (newCatalog.FindSheet((int)s) == null)
                        throw new InvalidDataException($"{kind} {a.Id} uses sprite {s}, which would not be in any sprite sheet.");

        // 5. Write: copy (Save As), then new files, then the catalog last.
        bool createdTarget = !Directory.Exists(targetFolder);
        Directory.CreateDirectory(targetFolder);
        try
        {
            if (!inPlace)
            {
                var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TibiaFormat.AssetCatalogFile };
                var sources = Directory.GetFiles(Folder);
                for (int i = 0; i < sources.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (skip.Contains(Path.GetFileName(sources[i]))) continue;
                    if (i % 200 == 0) progress?.Report(new OperationProgress(i, sources.Length, L.T("AssetsProject.CopyingTheAssetsFolder")));
                    File.Copy(sources[i], Path.Combine(targetFolder, Path.GetFileName(sources[i])));
                    result.FilesCopied++;
                }
            }
            progress?.Report(new OperationProgress(++step, steps, L.T("AssetsProject.WritingNewFiles")));
            foreach (var (name, data) in files)
            {
                string path = Path.Combine(targetFolder, name);
                // Names are content hashes: an existing file with this name already has this content.
                if (File.Exists(path) && FileHashing.SameContentAs(path, data)) continue;
                output.Write(path, temp => File.WriteAllBytes(temp, data), temp =>
                {
                    if (!File.ReadAllBytes(temp).AsSpan().SequenceEqual(data)) throw new IOException($"{name} could not be verified after writing.");
                });
            }
            string catalogPath = Path.Combine(targetFolder, TibiaFormat.AssetCatalogFile);
            output.Write(catalogPath, temp => File.WriteAllBytes(temp, catalogBytes), temp => Validate(targetFolder, File.ReadAllBytes(temp), appearanceBytes, expectations, result));
        }
        catch when (!inPlace)
        {
            // Save As into a folder that was empty: remove the partial copy, so a failed or cancelled
            // save leaves nothing half-written and the same folder can be chosen again.
            RemovePartialCopy(targetFolder, createdTarget);
            throw;
        }
        if (!output.Options.ValidateOutputs) result.Validation = null;

        // 6. The project now reflects the saved folder.
        Folder = targetFolder;
        Catalog = AssetCatalog.Load(targetFolder);
        _catalogDocument = document;
        Appearances = appearances;
        AppearancesFile = appearancesName;
        _sprites = new SpriteRepository(Catalog, null, SpriteSourceMode.ClientOnly, maxCachedSheets: SheetCache);
        _replaced.Clear();
        _added.Clear();
        _blankSprite = null;
        _modified.Clear();
        _structureChanged = false;
        result.AppearancesFile = appearancesName;
        if (idMap.Count > 0) result.Notes.Add(L.F("AssetsProject.NewSpritesReceivedTheIDs0", idMap.Values.Min(), idMap.Values.Max()));
        if (inPlace && files.Count > 0) result.Notes.Add(L.T("AssetsProject.ThePreviousAppearancesAndSpriteSheet"));
        AppLog.Info($"Assets Editor saved {targetFolder}: {L.Count(result.ObjectsChanged, "Noun.object")} changed, {L.Count(result.SpritesAdded, "Noun.sprite")} added, {result.SpritesReplaced} replaced, {L.Count(result.SheetsWritten.Count, "Noun.sheet")} written.");
        return result;
    }

    private static void RemovePartialCopy(string folder, bool removeFolder)
    {
        try
        {
            if (removeFolder) Directory.Delete(folder, recursive: true);
            else
                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                {
                    if (entry is DirectoryInfo d) d.Delete(recursive: true);
                    else entry.Delete();
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Assets Editor: the partial copy in {folder} could not be removed completely ({ex.Message}); delete it before saving there again.");
        }
    }

    /// <summary>BMP in the client's layout, compressed into the client's LZMA container, named after the BMP's SHA-256.</summary>
    private static (string Name, byte[] Data) EncodeSheet(RgbaImage sheet, CancellationToken ct)
    {
        byte[] bmp = BmpEncoder.Encode(sheet);
        string name = $"sprites-{Convert.ToHexStringLower(SHA256.HashData(bmp))}.bmp.lzma";
        return (name, CipLzma.Compress(bmp, ct));
    }

    /// <summary>Checks a saved folder: the catalog reads without new problems, the appearances file is the one written, and every new or replaced sprite decodes to the intended pixels.</summary>
    private static void Validate(string folder, byte[] catalogBytes, byte[] appearanceBytes, List<(uint Id, RgbaImage Image)> expectations, ProjectSaveResult result)
    {
        var catalog = AssetCatalog.Parse(folder, catalogBytes);
        string appearancesFile = catalog.Files[TibiaFormat.CatalogTypeAppearances];
        if (!File.ReadAllBytes(Path.Combine(folder, appearancesFile)).AsSpan().SequenceEqual(appearanceBytes))
            throw new InvalidDataException("Validation: the appearances file on disk differs from the one written.");
        EditableAppearances.Parser.ParseFrom(appearanceBytes);
        var repo = new SpriteRepository(catalog, null, SpriteSourceMode.ClientOnly);
        foreach (var (id, image) in expectations)
        {
            var read = repo.GetSprite(id) ?? throw new InvalidDataException($"Validation: sprite {id} cannot be read from the saved sheets.");
            if (!read.PixelEquals(image)) throw new InvalidDataException($"Validation: sprite {id} in the saved sheets differs from the intended image.");
        }
        foreach (var sheet in catalog.Sheets)
            if (!File.Exists(catalog.GetSheetPath(sheet))) throw new InvalidDataException($"Validation: {sheet.FileName} is listed but missing.");
        result.Validation = L.F("AssetsProject.SavedFolderCheckedCatalogWith0", catalog.Sheets.Count, expectations.Count);
    }
}
