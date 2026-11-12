using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Editing;
using TibiaFoundry.Core.Editing.Model;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>One object in the Assets Editor's list.</summary>
public sealed class EditorListItem : ObservableObject
{
    private readonly SpriteImageCache _cache;
    private BitmapSource? _thumbnail;
    private bool _requested;

    public EditorListItem(EditableAppearance appearance, AppearanceKind kind, SpriteImageCache cache, bool modified)
    {
        Appearance = appearance;
        Kind = kind;
        _cache = cache;
        IsModified = modified;
    }

    public EditableAppearance Appearance { get; }
    public AppearanceKind Kind { get; }
    public uint Id => Appearance.Id;
    public string Name => Appearance.NameText();
    public bool IsModified { get; private set; }

    public BitmapSource? Thumbnail
    {
        get
        {
            if (_thumbnail == null && !_requested && Appearance.PreviewSprite() is uint id)
            {
                _requested = true;
                _ = LoadAsync(id);
            }
            return _thumbnail;
        }
    }

    private async Task LoadAsync(uint id)
    {
        _thumbnail = await _cache.GetAsync(id).ConfigureAwait(true);
        OnPropertyChanged(nameof(Thumbnail));
    }

    public void Refresh(bool modified)
    {
        IsModified = modified;
        _thumbnail = null;
        _requested = false;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(Thumbnail));
    }
}

/// <summary>One sprite of the selected frame group (a cell of the sprite grid).</summary>
public sealed class SpriteCell(int index, string label, uint spriteId, BitmapSource? image, string marker) : ObservableObject
{
    public int Index { get; } = index;
    public string Label { get; } = label;
    public uint SpriteId { get; } = spriteId;
    public string IdText => SpriteId >= AssetsProject.FirstNewSpriteId ? L.F("AssetsEditor.New0", SpriteId - AssetsProject.FirstNewSpriteId + 1) : SpriteId.ToString(CultureInfo.InvariantCulture);
    public BitmapSource? Image { get; } = image;
    public string Marker { get; } = marker;
}

/// <summary>A sprite sheet in the sheet browser.</summary>
public sealed record SheetItem(SpriteSheetInfo Info, bool HasReplacedSprites)
{
    public string Range => $"{Info.FirstSpriteId:N0}–{Info.LastSpriteId:N0}";
    public string Size => Info.SizeLabel;
    public int Count => Info.SpriteCount;
    public string Note => HasReplacedSprites ? "changed" : "";
}

/// <summary>
/// Assets Editor: opens a modern client assets folder, browses and edits every object, outfit,
/// effect and missile (all appearance fields, frame groups, animation, sprites), creates, copies
/// and deletes objects, imports and exports object packages, replaces and adds sprites, and saves a
/// validated copy of the folder. Re-implements the modern-asset features of Arch-Mina's Assets Editor.
/// </summary>
public sealed partial class AssetsEditorViewModel : PageViewModel, IDisposable
{
    private AssetsProject? _project;
    private readonly SpriteImageCache _sprites = new();
    private readonly ProtoPropertyEditor _editor;
    private List<EditorListItem> _all = [];
    private AppearanceFlags? _copiedFlags;
    private int _previewVersion;

    public AssetsEditorViewModel(AppServices services) : base(services, BuiltInTools.AssetsEditor)
    {
        _editor = new ProtoPropertyEditor(OnPropertyEdited);
        FlagFilters = ["(any)", .. AppearanceFlags.Descriptor.Fields.InDeclarationOrder().Select(f => f.Name)];
    }


    public override string? UnsavedChanges => _project is { IsModified: true } p
        ? L.F("AssetsEditor.AssetsEditor01N0New", L.Count(p.ModifiedObjectCount, "Noun.changed_object"), p.AddedSpriteCount, L.Count(p.ReplacedSpriteCount, "Noun.replaced_sprite"), p.Folder)
        : null;

    public ObservableCollection<EditorListItem> Items { get; } = [];
    public ObservableCollection<PropertyNode> Properties { get; } = [];
    public ObservableCollection<SpriteCell> SpriteCells { get; } = [];
    public ObservableCollection<SheetItem> Sheets { get; } = [];
    public IReadOnlyList<string> FlagFilters { get; }
    public IReadOnlyList<EditorListItem> SelectedItems { get; set; } = [];

    [ObservableProperty] private AppearanceKind _kind = AppearanceKind.Object;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _flagFilter = "(any)";
    [ObservableProperty] private EditorListItem? _selected;
    [ObservableProperty] private int _frameGroup;
    [ObservableProperty] private int _direction = 2;
    [ObservableProperty] private bool _addon1;
    [ObservableProperty] private bool _addon2;
    [ObservableProperty] private IReadOnlyList<BitmapSource>? _previewFrames;
    [ObservableProperty] private IReadOnlyList<int>? _previewDelays;
    [ObservableProperty] private string _previewMessage = "";
    [ObservableProperty] private SpriteCell? _selectedCell;
    [ObservableProperty] private string _spriteUses = "";
    [ObservableProperty] private string _layers = "1";
    [ObservableProperty] private string _patternWidth = "1";
    [ObservableProperty] private string _patternHeight = "1";
    [ObservableProperty] private string _patternDepth = "1";
    [ObservableProperty] private string _phases = "1";
    [ObservableProperty] private SheetItem? _selectedSheet;
    [ObservableProperty] private BitmapSource? _sheetImage;
    [ObservableProperty] private string _sheetInfo = "";

    public bool HasProject => _project != null;
    public bool HasSelection => Selected != null;
    public bool IsOutfit => Kind == AppearanceKind.Outfit;
    public string FolderText => _project?.Folder ?? L.T("AssetsEditor.NoFolderOpen");
    public bool IsClientFolder => _project?.IsClientInstallation == true;
    public int ThumbnailSize => Services.Settings.Interface.ThumbnailSize;
    public IReadOnlyList<int> FrameGroups => Selected == null ? [] : Enumerable.Range(0, Selected.Appearance.FrameGroup.Count).ToList();

    public string Summary => _project == null ? L.T("AssetsEditor.NoAssetsFolderOpen") :
        L.F("AssetsEditor.0N0Objects1N0Outfits", _project.List(AppearanceKind.Object).Count, _project.List(AppearanceKind.Outfit).Count, _project.List(AppearanceKind.Effect).Count, _project.List(AppearanceKind.Missile).Count, _project.Catalog.Sheets.Count) +
        (_project.IsModified ? L.F("AssetsEditor.NOTSAVED01N0New", L.Count(_project.ModifiedObjectCount, "Noun.object"), _project.AddedSpriteCount, L.Count(_project.ReplacedSpriteCount, "Noun.replaced_sprite")) : "");

    // ------------------------------------------------------------------ opening

    public override async Task OnNavigatedToAsync()
    {
        OnPropertyChanged(nameof(ThumbnailSize));
        var g = Services.Settings.General;
        if (_project == null && g.ReopenLastFiles && g.LastFiles.TryGetValue(ToolKeys.AssetsEditor, out string? last) && File.Exists(Path.Combine(last, TibiaFormat.AssetCatalogFile)))
            await OpenFolderAsync(last);
    }

    private bool ConfirmDiscard() =>
        _project is not { IsModified: true } ||
        Services.Dialogs.Confirm(L.T("AssetsEditor.TheOpenAssetsFolderHasUnsaved"), L.T("AssetsEditor.UnsavedChanges"), L.T("AssetsEditor.Discard"), L.T("Common.Cancel"));

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!ConfirmDiscard()) return;
        string? folder = Services.Dialogs.PickFolder(L.T("AssetsEditor.ClientAssetsFolderWithCatalogContent"), Services.Settings.InputFolderFor(ToolKeys.AssetsEditor) ?? RealAssetsFolder());
        if (folder != null) await OpenFolderAsync(folder);
    }

    [RelayCommand]
    private async Task OpenClientAsync()
    {
        if (!ConfirmDiscard()) return;
        if (!RequireRealClientFolder(L.T("AssetsEditor.TheAssetsEditor"))) return;
        await OpenFolderAsync(Services.Data.AssetsDirectory);
    }

    public async Task OpenFolderAsync(string folder)
    {
        AssetsProject? project = null;
        bool ok = await RunAsync(L.T("AssetsEditor.Opening") + folder, async (_, ct) =>
        {
            project = await Task.Run(() => AssetsProject.Open(folder, Services.Settings.Performance.SpriteSheetCache), ct);
        });
        if (!ok || project == null) return;
        _project = project;
        _sprites.Source = project;
        Services.Settings.RememberFolder(ToolKeys.AssetsEditor, "input", folder);
        Services.Settings.General.LastFiles[ToolKeys.AssetsEditor] = folder;
        Services.SaveSettings();
        RebuildList();
        RebuildSheets();
        RaiseProject();
        Info(project.IsClientInstallation
            ? L.T("AssetsEditor.OpenedTheInstalledClientSAssets")
            : L.F("AssetsEditor.Opened0", folder));
    }

    private void RaiseProject()
    {
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(FolderText));
        OnPropertyChanged(nameof(IsClientFolder));
        OnPropertyChanged(nameof(Summary));
    }

    // ------------------------------------------------------------------ list

    partial void OnKindChanged(AppearanceKind value)
    {
        OnPropertyChanged(nameof(IsOutfit));
        RebuildList();
    }

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnFlagFilterChanged(string value) => ApplyFilter();

    private void RebuildList()
    {
        if (_project == null) return;
        _all = _project.List(Kind).Select(a => new EditorListItem(a, Kind, _sprites, _project.IsObjectModified(Kind, a.Id))).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string s = Search.Trim();
        var field = FlagFilter == "(any)" ? null : AppearanceFlags.Descriptor.FindFieldByName(FlagFilter);
        (uint Low, uint High)? range = null;
        if (s.Contains('-') && s.Split('-') is [var a, var b] && uint.TryParse(a, out uint lo) && uint.TryParse(b, out uint hi)) range = (lo, hi);
        var keep = Selected?.Appearance;
        var shown = _all.Where(i =>
            (s.Length == 0 || (range is { } r ? i.Id >= r.Low && i.Id <= r.High : i.Id.ToString(CultureInfo.InvariantCulture).StartsWith(s, StringComparison.Ordinal) || i.Name.Contains(s, StringComparison.OrdinalIgnoreCase))) &&
            (field == null || (i.Appearance.Flags != null && HasValue(field, i.Appearance.Flags))));
        Items.Clear();
        foreach (var item in shown) Items.Add(item);
        Selected = Items.FirstOrDefault(i => i.Appearance == keep) ?? Items.FirstOrDefault();
    }

    private static bool HasValue(Google.Protobuf.Reflection.FieldDescriptor field, IMessage flags)
    {
        if (field.IsRepeated) return ((System.Collections.IList)field.Accessor.GetValue(flags)).Count > 0;
        if (!field.Accessor.HasValue(flags)) return false;
        return field.FieldType != Google.Protobuf.Reflection.FieldType.Bool || (bool)field.Accessor.GetValue(flags);
    }

    partial void OnSelectedChanged(EditorListItem? value)
    {
        FrameGroup = 0;
        RebuildProperties();
        LoadLayout();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(FrameGroups));
        _ = RefreshVisualsAsync();
    }

    partial void OnFrameGroupChanged(int value)
    {
        LoadLayout();
        _ = RefreshVisualsAsync();
    }

    partial void OnDirectionChanged(int value) => _ = RefreshVisualsAsync();
    partial void OnAddon1Changed(bool value) => _ = RefreshVisualsAsync();
    partial void OnAddon2Changed(bool value) => _ = RefreshVisualsAsync();

    private void RebuildProperties()
    {
        Properties.Clear();
        if (Selected == null) return;
        foreach (var node in _editor.Build(Selected.Appearance)) Properties.Add(node);
    }

    private void OnPropertyEdited(bool structural)
    {
        if (_project == null || Selected == null) return;
        _project.MarkModified(Selected.Kind, Selected.Id);
        Selected.Refresh(true);
        if (structural) RebuildProperties();
        OnPropertyChanged(nameof(Summary));
        _ = RefreshVisualsAsync();
    }

    private void Touch(EditorListItem item)
    {
        _project!.MarkModified(item.Kind, item.Id);
        item.Refresh(true);
        OnPropertyChanged(nameof(Summary));
    }

    // ------------------------------------------------------------------ preview and sprites

    private SpriteInfo? CurrentInfo => Selected?.Appearance.FrameGroup is { Count: > 0 } groups ? groups[Math.Clamp(FrameGroup, 0, groups.Count - 1)].SpriteInfo : null;

    private async Task RefreshVisualsAsync()
    {
        int version = ++_previewVersion;
        SpriteCells.Clear();
        SelectedCell = null;
        PreviewFrames = null;
        PreviewMessage = "";
        if (_project == null || Selected == null) return;
        var project = _project;
        var appearance = Selected.Appearance.Clone();
        var kind = Selected.Kind;
        int group = FrameGroup, direction = Direction;
        uint addons = (Addon1 ? 1u : 0) | (Addon2 ? 2u : 0);
        var info = CurrentInfo?.Clone();
        try
        {
            var (frames, delays) = await Task.Run(() => BuildPreview(project, appearance, kind, group, direction, addons));
            if (version != _previewVersion) return;
            PreviewFrames = frames.Select(Images.ToBitmapSource).ToList();
            PreviewDelays = delays;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or OutfitRenderException or ArgumentException or IndexOutOfRangeException)
        {
            if (version == _previewVersion) PreviewMessage = L.T("AssetsEditor.NoPreview") + ex.Message;
        }
        if (info == null || version != _previewVersion) return;
        int layers = (int)Math.Max(1, info.Layers), pw = (int)Math.Max(1, info.PatternWidth), ph = (int)Math.Max(1, info.PatternHeight), pd = (int)Math.Max(1, info.PatternDepth);
        for (int i = 0; i < info.SpriteId.Count && i < 512; i++)
        {
            int rest = i;
            int layer = rest % layers; rest /= layers;
            int x = rest % pw; rest /= pw;
            int y = rest % ph; rest /= ph;
            int z = rest % pd; rest /= pd;
            uint id = info.SpriteId[i];
            var bitmap = await _sprites.GetAsync(id);
            if (version != _previewVersion) return;
            string marker = project.IsNewSprite(id) ? "new" : project.IsReplacedSprite(id) ? "replaced" : project.SpriteSize(id) == null ? "missing" : "";
            SpriteCells.Add(new SpriteCell(i, L.F("AssetsEditor.Layer0X1Y2", layer, x, y, z, rest), id, bitmap, marker));
        }
    }

    private static (List<RgbaImage> Frames, List<int> Delays) BuildPreview(AssetsProject project, EditableAppearance appearance, AppearanceKind kind, int group, int direction, uint addons)
    {
        var viewer = appearance.ToViewerAppearance();
        if (kind == AppearanceKind.Outfit)
        {
            var db = new AppearanceDatabase(new Tibia.Protobuf.Appearances.Appearances { Outfit = { viewer } });
            var frames = OutfitRenderer.Render(db, project, viewer.Id, new OutfitRenderOptions { FrameGroup = group, Direction = direction, Addons = addons });
            return (frames, OutfitRenderer.OutfitterDelays(viewer, group, frames.Count));
        }
        var gif = AppearanceGifBuilder.Build(viewer, kind, new GifExportOptions { FrameGroup = group, Direction = (Direction)direction, Pattern = 0 }, project);
        return (gif.Frames.ToList(), gif.Delays.ToList());
    }

    partial void OnSelectedCellChanged(SpriteCell? value)
    {
        if (_project == null || value == null)
        {
            SpriteUses = "";
            return;
        }
        var uses = _project.UsesOf(value.SpriteId).Take(21).ToList();
        var size = _project.SpriteSize(value.SpriteId);
        SpriteUses = L.F("AssetsEditor.Sprite012", value.IdText, (size is { } s ? $" ({s.Width}×{s.Height})" : " (missing)"), value.Label) +
                     (uses.Count == 0 ? L.T("AssetsEditor.NotUsedByAnyObject") : L.T("AssetsEditor.UsedBy") + string.Join(", ", uses.Take(20).Select(u => $"{u.Kind.Display()} {u.Id}")) + (uses.Count > 20 ? L.T("AssetsEditor.AndMore") : "") + ".");
    }

    private void LoadLayout()
    {
        var info = CurrentInfo;
        Layers = (info?.Layers ?? 1).ToString(CultureInfo.InvariantCulture);
        PatternWidth = (info?.PatternWidth ?? 1).ToString(CultureInfo.InvariantCulture);
        PatternHeight = (info?.PatternHeight ?? 1).ToString(CultureInfo.InvariantCulture);
        PatternDepth = (info?.PatternDepth ?? 1).ToString(CultureInfo.InvariantCulture);
        Phases = (info?.Animation?.SpritePhase.Count is > 0 ? info.Animation.SpritePhase.Count : 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Applies the sprite layout: the sprite list grows (new entries get an empty sprite of the
    /// group's size) or shrinks, and the animation gets one phase per frame.
    /// </summary>
    [RelayCommand]
    private void ApplyLayout()
    {
        if (_project == null || Selected == null || CurrentInfo is not { } info) return;
        if (!uint.TryParse(Layers, out uint layers) || !uint.TryParse(PatternWidth, out uint pw) || !uint.TryParse(PatternHeight, out uint ph) ||
            !uint.TryParse(PatternDepth, out uint pd) || !uint.TryParse(Phases, out uint phases) ||
            layers is < 1 or > 16 || pw is < 1 or > 16 || ph is < 1 or > 16 || pd is < 1 or > 16 || phases is < 1 or > 255)
        {
            Services.Dialogs.Show(L.T("AssetsEditor.LayersAndPatternsMustBeBetween"), MessageKind.Warning, L.T("AssetsEditor.SpriteLayout"));
            return;
        }
        uint needed = layers * pw * ph * pd * phases;
        if (needed > 4096)
        {
            Services.Dialogs.Show(L.F("AssetsEditor.ThatLayoutNeeds0N0Sprites", needed), MessageKind.Warning, L.T("AssetsEditor.SpriteLayout"));
            return;
        }
        var size = info.SpriteId.Count > 0 ? _project.SpriteSize(info.SpriteId[0]) : (32, 32);
        if (needed > info.SpriteId.Count)
        {
            uint blank = _project.AddSprite(new RgbaImage(size?.Width ?? 32, size?.Height ?? 32));
            while (info.SpriteId.Count < needed) info.SpriteId.Add(blank);
        }
        while (info.SpriteId.Count > needed) info.SpriteId.RemoveAt(info.SpriteId.Count - 1);
        info.Layers = layers;
        info.PatternWidth = pw;
        info.PatternHeight = ph;
        info.PatternDepth = pd;
        if (phases > 1)
        {
            info.Animation ??= new SpriteAnimation { LoopType = 0 };
            var list = info.Animation.SpritePhase;
            var template = list.Count > 0 ? list[^1] : new SpritePhase { DurationMin = 100, DurationMax = 100 };
            while (list.Count < phases) list.Add(template.Clone());
            while (list.Count > phases) list.RemoveAt(list.Count - 1);
        }
        else
        {
            info.Animation = null;
        }
        Touch(Selected);
        RebuildProperties();
        _ = RefreshVisualsAsync();
        Info(L.F("AssetsEditor.SpriteLayoutApplied0N0Sprites", needed));
    }

    private RgbaImage? PickImage(string title)
    {
        string? file = Services.Dialogs.OpenFile(title, L.T("AssetsEditor.ImagesPngGifBmpAllFiles"), Services.Settings.InputFolderFor(ToolKeys.AssetsEditor));
        if (file == null) return null;
        Services.Settings.RememberFolder(ToolKeys.AssetsEditor, "input", Path.GetDirectoryName(file));
        return ImageFile.Decode(File.ReadAllBytes(file)).First;
    }

    /// <summary>Replaces the image of the selected sprite (everywhere it is used).</summary>
    [RelayCommand]
    private void ReplaceSpriteImage()
    {
        if (_project == null || SelectedCell is not { } cell) return;
        int uses = _project.UsesOf(cell.SpriteId).Count();
        if (uses > 1 && !Services.Dialogs.Confirm(L.F("AssetsEditor.Sprite0IsUsedBy1", cell.IdText, uses), L.T("AssetsEditor.ReplaceSprite"), L.T("AssetsEditor.ReplaceEverywhere"), L.T("Common.Cancel"))) return;
        Try(L.T("AssetsEditor.ReplacingTheSprite"), () =>
        {
            var image = PickImage(L.F("AssetsEditor.NewImageForSprite0", cell.IdText));
            if (image == null) return;
            _project.ReplaceSprite(cell.SpriteId, image);
            _sprites.Clear();
            foreach (var item in _all) item.Refresh(_project.IsObjectModified(item.Kind, item.Id));
            OnPropertyChanged(nameof(Summary));
            _ = RefreshVisualsAsync();
            Info(L.F("AssetsEditor.Sprite0ReplacedNotSavedYet", cell.IdText));
        });
    }

    /// <summary>Gives the selected cell a new sprite made from an image (only this object changes).</summary>
    [RelayCommand]
    private void NewImageForCell()
    {
        if (_project == null || Selected == null || SelectedCell is not { } cell || CurrentInfo is not { } info) return;
        Try(L.T("AssetsEditor.AddingTheSprite"), () =>
        {
            var image = PickImage(L.T("AssetsEditor.ImageForThisSpriteCell"));
            if (image == null) return;
            var expected = _project.SpriteSize(cell.SpriteId);
            if (expected is { } e && (image.Width, image.Height) != e)
                throw new ArgumentException($"This frame group uses {e.Width}×{e.Height} sprites; the image is {image.Width}×{image.Height}.");
            info.SpriteId[cell.Index] = _project.AddSprite(image);
            Touch(Selected);
            _ = RefreshVisualsAsync();
            Info(L.T("AssetsEditor.NewSpriteAssignedToTheCell"));
        });
    }

    [RelayCommand]
    private void SetSpriteId()
    {
        if (_project == null || Selected == null || SelectedCell is not { } cell || CurrentInfo is not { } info) return;
        string? text = Services.Dialogs.Prompt(L.T("Common.SpriteID"), L.T("AssetsEditor.IDOfAnExistingSpriteOf"), cell.SpriteId.ToString(CultureInfo.InvariantCulture));
        if (text == null) return;
        if (!uint.TryParse(text, out uint id) || _project.SpriteSize(id) is not { } size)
        {
            Services.Dialogs.Show(L.F("AssetsEditor.Sprite0DoesNotExist", text), MessageKind.Warning, L.T("Common.SpriteID"));
            return;
        }
        if (_project.SpriteSize(cell.SpriteId) is { } current && current != size)
        {
            Services.Dialogs.Show(L.F("AssetsEditor.Sprite0Is12This", id, size.Width, size.Height, current.Width, current.Height), MessageKind.Warning, L.T("Common.SpriteID"));
            return;
        }
        info.SpriteId[cell.Index] = id;
        Touch(Selected);
        _ = RefreshVisualsAsync();
    }

    [RelayCommand]
    private void ExportSprite()
    {
        if (_project == null || SelectedCell is not { } cell || _project.GetSprite(cell.SpriteId) is not { } image) return;
        string? file = Services.Outputs.SaveFile(OutputKind.AssetsEditor, L.T("AssetsEditor.SaveTheSpriteAsPNG"), L.T("AssetsEditor.PNGImagesPng"), $"{cell.IdText.Replace(' ', '_')}.png");
        if (file == null) return;
        Try(L.T("AssetsEditor.SavingTheSprite"), () =>
        {
            using var output = Services.CreateOutput(L.T("Output.Spriteexport"));
            if (output.WriteAllBytes(file, ImageIO.EncodePng(image)) is { } written) Saved(written);
            else Services.Status.Show(L.T("AssetsEditor.TheSpriteWasNotSavedThe"), StatusKind.Warning);
        });
    }

    // ------------------------------------------------------------------ objects

    [RelayCommand]
    private void NewObject()
    {
        if (_project == null) return;
        var created = _project.CreateObject(Kind);
        RebuildList();
        Selected = Items.FirstOrDefault(i => i.Appearance == created);
        OnPropertyChanged(nameof(Summary));
        Info(L.F("AssetsEditor.01CreatedWithAnEmpty", Kind, created.Id));
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (_project == null || Selected == null) return;
        var copy = _project.Duplicate(Kind, Selected.Id);
        RebuildList();
        Selected = Items.FirstOrDefault(i => i.Appearance == copy);
        OnPropertyChanged(nameof(Summary));
        Info(L.F("AssetsEditor.CopiedTo01NotSaved", Kind, copy.Id));
    }

    [RelayCommand]
    private void Delete()
    {
        if (_project == null) return;
        var items = SelectedItems.Count > 0 ? SelectedItems.ToList() : Selected != null ? [Selected] : [];
        if (items.Count == 0) return;
        if (!Services.Dialogs.Confirm(L.F("AssetsEditor.Delete0N01S2", items.Count, Kind.ToString().ToLowerInvariant(), string.Join(", ", items.Take(12).Select(i => i.Id)), (items.Count > 12 ? " …" : "")),
                L.T("AssetsEditor.DeleteObjects"), L.T("AssetsEditor.Delete"), L.T("Common.Cancel"))) return;
        foreach (var i in items) _project.Delete(i.Kind, i.Id);
        RebuildList();
        OnPropertyChanged(nameof(Summary));
        Info(L.F("AssetsEditor.0N0DeletedNotSavedYet", items.Count));
    }

    [RelayCommand]
    private void ChangeId()
    {
        if (_project == null || Selected == null) return;
        string? text = Services.Dialogs.Prompt(L.T("AssetsEditor.ChangeID2"), L.F("AssetsEditor.NewIDFor01Must", Kind, Selected.Id), Selected.Id.ToString(CultureInfo.InvariantCulture));
        if (text == null || !uint.TryParse(text, out uint id)) return;
        var appearance = Selected.Appearance;
        string? problem = _project.ChangeId(Kind, appearance.Id, id);
        if (problem != null)
        {
            Services.Dialogs.Show(problem, MessageKind.Warning, L.T("AssetsEditor.ChangeID2"));
            return;
        }
        RebuildList();
        Selected = Items.FirstOrDefault(i => i.Appearance == appearance);
    }

    [RelayCommand]
    private void Revert()
    {
        if (_project == null || Selected == null) return;
        var id = Selected.Id;
        if (!_project.Revert(Kind, id))
        {
            Services.Dialogs.Show(L.T("AssetsEditor.ThisObjectIsNotInThe"), MessageKind.Info, L.T("Common.Revert"));
            return;
        }
        RebuildList();
        Selected = Items.FirstOrDefault(i => i.Id == id);
        OnPropertyChanged(nameof(Summary));
    }

    [RelayCommand]
    private void CopyFlags()
    {
        if (Selected?.Appearance.Flags is not { } flags) return;
        _copiedFlags = flags.Clone();
        Info(L.F("AssetsEditor.FlagsOf01Copied", Kind, Selected.Id));
    }

    [RelayCommand]
    private void PasteFlags()
    {
        if (_project == null || _copiedFlags == null) return;
        var items = SelectedItems.Count > 0 ? SelectedItems.ToList() : Selected != null ? [Selected] : [];
        if (items.Count == 0) return;
        if (!Services.Dialogs.Confirm(L.F("AssetsEditor.ReplaceAllFlagsOf0With", L.Count(items.Count, "Noun.object")), L.T("AssetsEditor.PasteFlags"), L.T("AssetsEditor.Paste"), L.T("Common.Cancel"))) return;
        foreach (var i in items)
        {
            i.Appearance.Flags = _copiedFlags.Clone();
            Touch(i);
        }
        RebuildProperties();
        Info(L.F("AssetsEditor.FlagsPastedInto0NotSaved", L.Count(items.Count, "Noun.object")));
    }

    // ------------------------------------------------------------------ packages, images, sprites

    [RelayCommand]
    private void ExportPackage()
    {
        if (_project == null) return;
        var items = SelectedItems.Count > 0 ? SelectedItems.ToList() : Selected != null ? [Selected] : [];
        if (items.Count == 0) return;
        string? file = Services.Outputs.SaveFile(OutputKind.AssetsEditor, L.T("AssetsEditor.SaveAnObjectPackage"), L.T("AssetsEditor.ObjectPackagesAecAec"), $"{Kind.ToString().ToLowerInvariant()}-{items[0].Id}.aec");
        if (file == null) return;
        Try(L.T("AssetsEditor.ExportingObjects"), () =>
        {
            byte[] package = AecPackage.Export(_project, items.Select(i => (i.Kind, i.Id)));
            using var output = Services.CreateOutput(L.T("TibiaProjectAuditorScript.ObjectPackage"));
            if (output.WriteAllBytes(file, package) is { } written)
                Services.Status.ShowSaved(written, isFile: true, before: L.F("AssetsEditor.0WithTheirSpritesSavedTo", L.Count(items.Count, "Noun.object")));
            else Services.Status.Show(L.T("AssetsEditor.ThePackageWasNotSavedThe"), StatusKind.Warning);
        });
    }

    [RelayCommand]
    private void ImportPackage()
    {
        if (_project == null) return;
        string? file = Services.Dialogs.OpenFile(L.T("AssetsEditor.ImportAnObjectPackage"), L.T("AssetsEditor.ObjectPackagesAecAecAllFiles"), Services.Settings.InputFolderFor(ToolKeys.AssetsEditor));
        if (file == null) return;
        int ids = Services.Dialogs.Choose(L.T("AssetsEditor.KeepTheObjectsIDsWhereThey"), L.T("AssetsEditor.ImportObjects"), L.T("AssetsEditor.KeepFreeIDs"), "New IDs");
        if (ids < 0) return;   // Cancel or Esc: nothing is imported
        bool keepIds = ids == 0;
        Try(L.T("AssetsEditor.ImportingObjects"), () =>
        {
            var result = AecPackage.Import(_project, File.ReadAllBytes(file), keepIds);
            RebuildList();
            OnPropertyChanged(nameof(Summary));
            string text = L.F("AssetsEditor.0Imported123N0", L.Count(result.Imported.Count, "Noun.object"), string.Join(", ", result.Imported.Take(10).Select(i => $"{i.Kind.Display()} {i.NewId}")), L.Count(result.SpritesAdded, "Noun.new_sprite"), result.SpritesReused);
            if (result.Problems.Count > 0) Services.Dialogs.Show(text + "\n\n" + string.Join("\n", result.Problems.Take(15)), MessageKind.Warning, L.T("AssetsEditor.ImportObjects"));
            else Info(text);
        });
    }

    /// <summary>Copies objects (with their sprites) from another assets folder, for example another client version.</summary>
    [RelayCommand]
    private async Task ImportFromFolderAsync()
    {
        if (_project == null) return;
        string? folder = Services.Dialogs.PickFolder(L.T("AssetsEditor.OtherClientAssetsFolderToCopy"), Services.Settings.InputFolderFor(ToolKeys.AssetsEditor));
        if (folder == null) return;
        string? ids = Services.Dialogs.Prompt(L.T("AssetsEditor.ImportObjects"), L.F("AssetsEditor.0IDsToCopyForExample", Kind), "");
        if (string.IsNullOrWhiteSpace(ids)) return;
        var wanted = ParseIds(ids);
        if (wanted == null)
        {
            Services.Dialogs.Show(L.T("AssetsEditor.WriteIDsSeparatedByCommasOr"), MessageKind.Warning, L.T("AssetsEditor.ImportObjects"));
            return;
        }
        byte[]? package = null;
        int found = 0;
        bool ok = await RunAsync(L.T("AssetsEditor.Reading") + folder, async (_, ct) =>
        {
            package = await Task.Run(() =>
            {
                var other = AssetsProject.Open(folder, Services.Settings.Performance.SpriteSheetCache);
                var existing = wanted.Where(id => other.Find(Kind, id) != null).Select(id => (Kind, id)).ToList();
                found = existing.Count;
                return AecPackage.Export(other, existing);
            }, ct);
        });
        if (!ok || package == null) return;
        var project = _project;
        Try(L.T("AssetsEditor.ImportingObjects"), () =>
        {
            var result = AecPackage.Import(project, package, keepIds: false);
            RebuildList();
            OnPropertyChanged(nameof(Summary));
            if (found == 0) Services.Status.Show(L.F("AssetsEditor.NoneOfThe0N0ID", wanted.Count, Kind, folder), StatusKind.Warning);
            else Info(L.F("AssetsEditor.0N0Of1Copied2", result.Imported.Count, L.Count(wanted.Count, "Noun.object"), found, L.Count(result.SpritesAdded, "Noun.new_sprite"), string.Join(", ", result.Imported.Take(10).Select(i => i.NewId))));
        });
    }

    private static List<uint>? ParseIds(string text)
    {
        var list = new List<uint>();
        foreach (var part in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            var range = part.Split('-');
            if (range.Length == 1 && uint.TryParse(range[0], out uint single)) list.Add(single);
            else if (range.Length == 2 && uint.TryParse(range[0], out uint a) && uint.TryParse(range[1], out uint b) && b >= a && b - a < 10000)
                for (uint i = a; i <= b; i++) list.Add(i);
            else return null;
        }
        return list.Distinct().ToList();
    }

    /// <summary>Saves the selected objects as PNG (first frame) and GIF (animation) images with attribution.</summary>
    [RelayCommand]
    private async Task ExportImagesAsync()
    {
        if (_project == null) return;
        var items = SelectedItems.Count > 0 ? SelectedItems.ToList() : Selected != null ? [Selected] : [];
        if (items.Count == 0) return;
        string? folder = Services.Outputs.PickFolder(OutputKind.AssetsEditor, L.T("AssetsEditor.FolderForTheImages"));
        if (folder == null) return;
        if (AssetsProject.IsInsideClientInstallation(folder))
        {
            Services.Dialogs.Show(L.T("AssetsEditor.FoldersOfTheInstalledTibiaClient"), MessageKind.Warning, L.T("AssetsEditor.ImageExport"));
            return;
        }
        var project = _project;
        int group = FrameGroup, direction = Direction;
        var batch = new Core.Common.BatchResult();
        string description = "";
        await RunAsync(L.T("AssetsEditor.ExportingImages"), async (progress, ct) =>
        {
            await Task.Run(() =>
            {
                using var output = Services.CreateOutput("Assets Editor images");
                var attribution = Attribution.ForNewAsset();
                for (int i = 0; i < items.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    progress.Report(new Core.Common.OperationProgress(i, items.Count, $"{items[i].Kind} {items[i].Id}"));
                    try
                    {
                        var (frames, delays) = BuildPreview(project, items[i].Appearance, items[i].Kind, Math.Max(0, Math.Min(group, items[i].Appearance.FrameGroup.Count - 1)), direction, 0);
                        string stem = $"{items[i].Kind.ToString().ToLowerInvariant()}_{items[i].Id}";
                        output.WriteAllBytes(Path.Combine(folder, stem + ".png"), ImageIO.EncodePng(frames[0], attribution));
                        if (frames.Count > 1) output.WriteAllBytes(Path.Combine(folder, stem + ".gif"), GifWriter.Encode(frames, delays, attribution));
                        batch.Succeeded++;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or OutfitRenderException or IOException or ArgumentException)
                    {
                        batch.Failures.Add($"{items[i].Kind} {items[i].Id}: {ex.Message}");
                    }
                }
                description = output.Describe();
            }, ct);
        }, operation: "export-images", output: folder, onSuccess: () => ReportBatch(batch, "objects", folder, L.T("AssetsEditor.ImageExport")));
    }

    [RelayCommand]
    private void ImportSprites()
    {
        if (_project == null) return;
        var files = Services.Dialogs.OpenFiles(L.T("AssetsEditor.ImagesToAddAsNewSprites"), L.T("AssetsEditor.ImagesPngGifBmpAllFiles"), Services.Settings.InputFolderFor(ToolKeys.AssetsEditor));
        if (files == null || files.Length == 0) return;
        var added = new List<string>();
        var failed = new List<string>();
        foreach (var f in files)
        {
            try
            {
                uint id = _project.AddSprite(ImageFile.Decode(File.ReadAllBytes(f)).First);
                added.Add(L.F("AssetsEditor.0New1", Path.GetFileName(f), id - AssetsProject.FirstNewSpriteId + 1));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or ImageMagick.MagickException or IOException)
            {
                failed.Add($"{Path.GetFileName(f)}: {ex.Message}");
            }
        }
        OnPropertyChanged(nameof(Summary));
        Services.Dialogs.Show(L.F("AssetsEditor.0TheyGetTheirFinalIDs", L.Count(added.Count, "Noun.new_sprite"), string.Join("\n", added.Take(20))) +
                              (failed.Count > 0 ? L.F("AssetsEditor.NotAdded0", string.Join("\n", failed.Take(20))) : "") +
                              L.T("AssetsEditor.AssignThemToObjectsWithNew"), failed.Count > 0 ? MessageKind.Warning : MessageKind.Info, L.T("AssetsEditor.ImportSprites2"));
    }

    // ------------------------------------------------------------------ sheets

    private void RebuildSheets()
    {
        Sheets.Clear();
        if (_project == null) return;
        foreach (var sheet in _project.Catalog.Sheets)
            Sheets.Add(new SheetItem(sheet, false));
    }

    partial void OnSelectedSheetChanged(SheetItem? value) => _ = LoadSheetAsync(value);

    private async Task LoadSheetAsync(SheetItem? item)
    {
        SheetImage = null;
        SheetInfo = "";
        if (_project == null || item == null) return;
        var project = _project;
        try
        {
            var image = await Task.Run(() =>
            {
                var sheet = new RgbaImage(TibiaFormat.SheetSize, TibiaFormat.SheetSize);
                for (int i = 0; i < item.Info.SpriteCount; i++)
                    if (project.GetSprite((uint)(item.Info.FirstSpriteId + i)) is { } sprite)
                        sheet.CopyImage(sprite, i % item.Info.Columns * item.Info.SpriteWidth, i / item.Info.Columns * item.Info.SpriteHeight);
                return sheet;
            });
            SheetImage = Images.ToBitmapSource(image);
            SheetInfo = L.F("AssetsEditor.0Sprites1234", item.Info.FileName, item.Range, item.Size, item.Info.Columns, item.Info.Rows);
        }
        catch (InvalidDataException ex)
        {
            SheetInfo = L.T("AssetsEditor.TheSheetCouldNotBeRead") + ex.Message;
        }
    }

    /// <summary>Called by the view with the clicked position (in sheet pixels).</summary>
    public void SheetClicked(double x, double y)
    {
        if (_project == null || SelectedSheet is not { } s) return;
        int col = (int)(x / s.Info.SpriteWidth), row = (int)(y / s.Info.SpriteHeight);
        int index = row * s.Info.Columns + col;
        if (col < 0 || col >= s.Info.Columns || index < 0 || index >= s.Info.SpriteCount) return;
        uint id = (uint)(s.Info.FirstSpriteId + index);
        var uses = _project.UsesOf(id).Take(21).ToList();
        SheetInfo = L.F("AssetsEditor.Sprite01", id, s.Size) + (uses.Count == 0 ? L.T("AssetsEditor.NotUsedByAnyObject2") : L.T("AssetsEditor.UsedBy2") + string.Join(", ", uses.Take(20).Select(u => $"{u.Kind.Display()} {u.Id}")) + (uses.Count > 20 ? L.T("AssetsEditor.AndMore2") : "."));
    }

    [RelayCommand]
    private void ExportSheet()
    {
        if (SheetImage == null || SelectedSheet == null) return;
        string? file = Services.Outputs.SaveFile(OutputKind.AssetsEditor, L.T("AssetsEditor.SaveTheSheetAsPNG"), L.T("AssetsEditor.PNGImagesPng"), $"sheet_{SelectedSheet.Info.FirstSpriteId}-{SelectedSheet.Info.LastSpriteId}.png");
        if (file == null) return;
        Try(L.T("AssetsEditor.SavingTheSheet"), () =>
        {
            using var output = Services.CreateOutput(L.T("Output.Sheetexport"));
            if (output.WriteAllBytes(file, ImageIO.EncodePng(Images.FromBitmapSource(SheetImage))) is { } written) Saved(written);
            else Services.Status.Show(L.T("AssetsEditor.TheSheetWasNotSavedThe"), StatusKind.Warning);
        });
    }

    [RelayCommand]
    private void FindSprite()
    {
        if (_project == null) return;
        string? text = Services.Dialogs.Prompt(L.T("AssetsEditor.FindASprite"), L.T("AssetsEditor.SpriteID"), "");
        if (text == null || !int.TryParse(text, out int id)) return;
        var sheet = Sheets.FirstOrDefault(s => s.Info.Contains(id));
        if (sheet == null)
        {
            Services.Dialogs.Show(L.F("AssetsEditor.NoSheetContainsSprite0", id), MessageKind.Info, L.T("AssetsEditor.FindASprite"));
            return;
        }
        SelectedSheet = sheet;
    }

    // ------------------------------------------------------------------ saving

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_project == null) return;
        if (_project.IsClientInstallation)
        {
            Services.Dialogs.Show(L.T("AssetsEditor.ThisIsTheInstalledTibiaClient"),
                MessageKind.Info, L.T("AssetsEditor.SaveACopy"));
            await SaveAsAsync();
            return;
        }
        await SaveToAsync(_project.Folder);
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        if (_project == null) return;
        string? folder = Services.Outputs.PickFolder(OutputKind.AssetsEditor, L.T("AssetsEditor.EmptyFolderForTheSavedAssets"));
        if (folder == null) return;
        if (AssetsProject.IsInsideClientInstallation(folder))
        {
            Services.Dialogs.Show(L.T("AssetsEditor.FoldersOfTheInstalledTibiaClient"), MessageKind.Warning, L.T("AssetsEditor.SaveACopy"));
            return;
        }
        await SaveToAsync(folder);
    }

    private async Task SaveToAsync(string folder)
    {
        var project = _project!;
        ProjectSaveResult? result = null;
        bool ok = await RunAsync(L.T("AssetsEditor.SavingTheAssets"), async (progress, ct) =>
        {
            using var output = Services.CreateOutput("Assets Editor save");
            result = await Task.Run(() => project.Save(folder, output, progress, ct), ct);
        }, operation: "save", output: folder, onSuccess: () => Services.Status.ShowSaved(result!.Folder, isFile: false, before: L.T("AssetsEditor.SavedTo"),
            after: L.F("AssetsEditor.0Changed1Added2N0", L.Count(result.ObjectsChanged, "Noun.object"), L.Count(result.SpritesAdded, "Noun.sprite"), result.SpritesReplaced)));
        if (!ok || result == null) return;
        _sprites.Clear();
        Services.Settings.General.LastFiles[ToolKeys.AssetsEditor] = project.Folder;
        Services.SaveSettings();
        RebuildList();
        RebuildSheets();
        RaiseProject();
        // The details after the folder (the folder's own "C:" must not be taken for the separator).
        string details = L.F("AssetsEditor.0Changed1Added2N02", L.Count(result.ObjectsChanged, "Noun.object"), L.Count(result.SpritesAdded, "Noun.sprite"), result.SpritesReplaced, L.Count(result.SheetsWritten.Count, "Noun.sheet")) +
                         (result.Validation ?? L.T("AssetsEditor.ValidationIsTurnedOffInSettings")) + (result.Notes.Count > 0 ? " " + string.Join(" ", result.Notes) : "");
        Services.Status.ShowSaved(result.Folder, isFile: false, before: L.T("AssetsEditor.SavedTo"), after: details);
        AppLog.Info($"Assets Editor: saved to {result.Folder}{details}");
    }

    /// <summary>Converts the (saved) folder with the Client Converter.</summary>
    [RelayCommand]
    private async Task ExportLegacyAsync()
    {
        if (_project == null) return;
        if (_project.IsModified)
        {
            if (!Services.Dialogs.Confirm(L.T("AssetsEditor.TheClientConverterReadsSavedFiles"), L.T("AssetsEditor.ExportToALegacyClient"), L.T("Common.Save"), L.T("Common.Cancel"))) return;
            await SaveAsync();
            if (_project.IsModified) return;
        }
        string folder = _project.Folder;
        OpenInConverter?.Invoke(folder);
        Services.Status.Show(L.F("AssetsEditor.ClientConverterAssetsFolderSetTo", folder), StatusKind.Info);
    }

    /// <summary>Shows the Client Converter with a folder (set by the shell).</summary>
    public Action<string>? OpenInConverter { get; set; }

    public void Dispose() => _sprites.Dispose();
}
