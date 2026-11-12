using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

public enum OutfitPart { Head, Primary, Secondary, Detail }

/// <summary>One of the 133 outfit colors.</summary>
public sealed record PaletteEntry(int Index, Brush Brush, string Hex);

/// <summary>
/// The Outfitter tab: renders the selected outfit with addons, mount, facing and colors
/// (port of the original <c>UpdateOutfiter</c> and color handlers, with their bugs fixed —
/// mount colors compared against outfit colors, and three branches that all tested "Head").
/// </summary>
public sealed partial class OutfitterViewModel : ObservableObject
{
    private readonly AppearancesViewModel _owner;
    private readonly AppServices _services;
    private readonly int[] _outfitColors = (int[])GameTables.DefaultOutfitColors.Clone();
    private readonly int[] _mountColors = (int[])GameTables.DefaultOutfitColors.Clone();
    private int _renderVersion;
    private bool _suppressPalette;

    public OutfitterViewModel(AppearancesViewModel owner, AppServices services)
    {
        _owner = owner;
        _services = services;
        Palette = GameTables.OutfitPaletteHex.Select((hex, i) =>
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return new PaletteEntry(i, brush, hex);
        }).ToList();
        _selectedColorIndex = _outfitColors[0];
    }

    public IReadOnlyList<PaletteEntry> Palette { get; }

    [ObservableProperty] private bool _hasOutfit;
    [ObservableProperty] private string _outfitTitle = L.T("Outfitter.SelectAnOutfitInTheOutfits");
    [ObservableProperty] private bool _moving;
    [ObservableProperty] private bool _mounted;
    [ObservableProperty] private bool _addon1;
    [ObservableProperty] private bool _addon2;
    [ObservableProperty] private Direction _facing = Direction.South;
    [ObservableProperty] private bool _editMountColors;
    [ObservableProperty] private OutfitPart _part = OutfitPart.Head;
    [ObservableProperty] private int _selectedColorIndex;
    [ObservableProperty] private bool _useMount;
    [ObservableProperty] private int _mountId = 1599;
    [ObservableProperty] private bool _canMove;
    [ObservableProperty] private bool _canMount;
    [ObservableProperty] private bool _hasAddons;
    [ObservableProperty] private IReadOnlyList<BitmapSource> _frames = Array.Empty<BitmapSource>();
    [ObservableProperty] private IReadOnlyList<int> _delays = Array.Empty<int>();
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private PreviewBackground _background = PreviewBackground.Magenta;

    public Brush HeadBrush => Palette[_outfitColors[0]].Brush;
    public Brush PrimaryBrush => Palette[_outfitColors[1]].Brush;
    public Brush SecondaryBrush => Palette[_outfitColors[2]].Brush;
    public Brush DetailBrush => Palette[_outfitColors[3]].Brush;

    private int[] ActiveColors => EditMountColors ? _mountColors : _outfitColors;

    partial void OnMovingChanged(bool value) => Render();
    partial void OnMountedChanged(bool value) => Render();
    partial void OnAddon1Changed(bool value) => Render();
    partial void OnAddon2Changed(bool value) => Render();
    partial void OnFacingChanged(Direction value) => Render();
    partial void OnUseMountChanged(bool value) => Render();
    partial void OnMountIdChanged(int value) { if (UseMount) Render(); }
    partial void OnEditMountColorsChanged(bool value) => SyncPaletteSelection();
    partial void OnPartChanged(OutfitPart value) => SyncPaletteSelection();

    partial void OnSelectedColorIndexChanged(int value)
    {
        if (_suppressPalette || value < 0 || value >= Palette.Count) return;
        var colors = ActiveColors;
        if (colors[(int)Part] == value) return;
        colors[(int)Part] = value;
        RaiseSwatches();
        Render();
    }

    private void SyncPaletteSelection()
    {
        _suppressPalette = true;
        SelectedColorIndex = ActiveColors[(int)Part];
        _suppressPalette = false;
    }

    private void RaiseSwatches()
    {
        OnPropertyChanged(nameof(HeadBrush));
        OnPropertyChanged(nameof(PrimaryBrush));
        OnPropertyChanged(nameof(SecondaryBrush));
        OnPropertyChanged(nameof(DetailBrush));
    }

    [RelayCommand]
    private void Face(Direction direction) => Facing = direction;

    [RelayCommand]
    private void RotateLeft() => Facing = (Direction)(((int)Facing + 3) % 4);

    [RelayCommand]
    private void RotateRight() => Facing = (Direction)(((int)Facing + 1) % 4);

    [RelayCommand]
    private void DefaultColors()
    {
        GameTables.DefaultOutfitColors.CopyTo(_outfitColors, 0);
        GameTables.DefaultOutfitColors.CopyTo(_mountColors, 0);
        SyncPaletteSelection();
        RaiseSwatches();
        Render();
    }

    /// <summary>Colors and addons from a creature (Static Data → "Open in Outfitter").</summary>
    public void ApplyPreset(int[] colors, uint addons)
    {
        for (int i = 0; i < 4 && i < colors.Length; i++) _outfitColors[i] = Math.Clamp(colors[i], 0, Palette.Count - 1);
        Addon1 = (addons & 1) != 0;
        Addon2 = (addons & 2) != 0;
        EditMountColors = false;
        Part = OutfitPart.Head;
        SyncPaletteSelection();
        RaiseSwatches();
        Render();
    }

    public void OnSelectionChanged()
    {
        var item = _owner.SelectedItem;
        var outfit = item?.Kind == AppearanceKind.Outfit ? item.Appearance : null;
        HasOutfit = outfit != null;
        if (outfit == null || outfit.FrameGroup.Count == 0)
        {
            OutfitTitle = L.T("Outfitter.SelectAnOutfitInTheOutfits");
            Frames = Array.Empty<BitmapSource>();
            return;
        }
        OutfitTitle = L.F("Outfitter.Outfit0", outfit.Id);
        var info = outfit.FrameGroup[^1].SpriteInfo;
        if (info == null)
        {
            Message = L.F("Outfitter.Outfit0HasNoSpriteInformation", outfit.Id);
            Frames = Array.Empty<BitmapSource>();
            return;
        }
        CanMove = outfit.FrameGroup.Count > 1;
        CanMount = info.PatternDepth > 1;
        HasAddons = info.PatternHeight > 2;
        if (!CanMove) Moving = false;
        if (!CanMount) Mounted = false;
        if (!HasAddons) { Addon1 = false; Addon2 = false; }
        Render();
    }

    private async void Render()
    {
        var item = _owner.SelectedItem;
        var db = _owner.Database;
        var sprites = _services.Data.Sprites;
        if (item?.Kind != AppearanceKind.Outfit || db == null || sprites == null) return;
        int version = ++_renderVersion;
        var outfit = item.Appearance;
        uint mountId = 0;
        if (UseMount && MountId > 0)
        {
            var mount = db.Find(AppearanceKind.Outfit, (uint)MountId);
            // Same validity rule as the original: a mount has an idle and an 8-phase moving group.
            if (mount != null && mount.FrameGroup.Count == 2 && mount.FrameGroup[1].SpriteInfo?.Animation?.SpritePhase.Count == 8) mountId = (uint)MountId;
            else Message = L.F("Outfitter.Outfit0IsNotAValid", MountId);
        }
        var options = new OutfitRenderOptions
        {
            FrameGroup = Moving ? 1 : 0,
            Direction = (int)Facing,
            Mounted = Mounted,
            Colors = (int[])_outfitColors.Clone(),
            Addons = (Addon1 ? 1u : 0u) + (Addon2 ? 2u : 0u),
            MountId = mountId,
            MountColors = (int[])_mountColors.Clone(),
        };
        try
        {
            var frames = await Task.Run(() => OutfitRenderer.Render(db, sprites, outfit.Id, options));
            if (version != _renderVersion) return;
            var delays = OutfitRenderer.OutfitterDelays(outfit, options.FrameGroup, frames.Count);
            if (delays.Count < frames.Count) delays = Enumerable.Repeat(100, frames.Count).ToList();
            Frames = Images.ToBitmapSources(frames);
            Delays = delays;
            if (mountId != 0 || !UseMount) Message = "";
        }
        catch (OutfitRenderException ex)
        {
            if (version != _renderVersion) return;
            Frames = Array.Empty<BitmapSource>();
            Message = ex.Message;
            AppLog.Warn("Outfitter: " + ex.Message);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or DataFileException)
        {
            // A damaged or locked sprite sheet only empties this preview (same rule as the Appearances preview).
            // Unhandled, every colour or option change reached the global handler, and a quick series of
            // changes tripped its repeated-error shutdown.
            if (version != _renderVersion) return;
            Frames = Array.Empty<BitmapSource>();
            Message = ex.Message;
            AppLog.Warn("Outfitter: " + ex.Message);
        }
    }

    [RelayCommand]
    private void CycleBackground() => Background = (PreviewBackground)(((int)Background + 1) % Enum.GetValues<PreviewBackground>().Length);

    [RelayCommand]
    private void SaveGif()
    {
        var item = _owner.SelectedItem;
        if (item?.Kind != AppearanceKind.Outfit || Frames.Count == 0) { _services.Dialogs.Show(L.T("Outfitter.SelectAnOutfitFirst")); return; }
        string name = $"{item.Id}_{(Moving ? "Moving" : "Idle")}_{Facing}";
        string? file = _services.Outputs.SaveFile(OutputKind.GifExports, L.T("Outfitter.SaveOutfitGIF"), L.T("Outfitter.GIFImagesGifGif"), name + ".gif");
        if (file == null) return;
        try
        {
            var images = Frames.Select(Images.FromBitmapSource).ToList();
            GifWriter.Write(file, images, Delays);
            _services.Status.ShowSaved(file, isFile: true);
            _owner.Export.RefreshFolder();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImageMagick.MagickException)
        {
            _services.Status.Show(L.T("Outfitter.TheGIFCouldNotBeSaved") + ex.Message, StatusKind.Error);
            _services.Dialogs.ShowError(L.T("Outfitter.TheGIFCouldNotBeSaved2"), ex);
        }
    }
}
