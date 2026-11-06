using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Common;

namespace TibiaFoundry.App.ViewModels;

/// <summary>A row of a "Data" panel with its optional swatch, link, play button and sprite thumbnails.</summary>
public sealed partial class PropertyRowViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<uint>? _play;

    public PropertyRowViewModel(PropertyRow row, AppServices services, Action<uint>? play = null)
    {
        Row = row;
        _services = services;
        _play = play;
        if (row.Swatch is uint argb)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            brush.Freeze();
            Swatch = brush;
        }
        if (row.SpriteIds is { Count: > 0 } ids) _ = LoadSpritesAsync(ids);
    }

    public PropertyRow Row { get; }
    public string Label => Row.Label;
    public string Value => Row.Value;
    public Brush? Swatch { get; }
    public bool HasLink => Row.Link != null;
    public bool CanPlay => Row.SoundId != null && _play != null;

    [ObservableProperty] private IReadOnlyList<BitmapSource> _sprites = Array.Empty<BitmapSource>();

    [RelayCommand]
    private void OpenLink()
    {
        if (Row.Link != null) _services.Shell.OpenUrl(Row.Link);
    }

    [RelayCommand]
    private void Play()
    {
        if (Row.SoundId is uint id) _play?.Invoke(id);
    }

    [RelayCommand]
    private void Copy() => _services.Clipboard.SetText(Row.Value);

    private async Task LoadSpritesAsync(IReadOnlyList<uint> objectIds)
    {
        var db = _services.Data.Appearances;
        if (db == null) return;
        var list = new List<BitmapSource>();
        foreach (uint objectId in objectIds)
        {
            var obj = db.Find(Core.Appearances.AppearanceKind.Object, objectId);
            uint? sprite = obj == null ? null : Core.Appearances.AppearanceDatabase.PreviewSpriteId(obj, Core.Appearances.AppearanceKind.Object);
            if (sprite is uint s && await _services.Data.Thumbnails.GetAsync(s) is { } bmp) list.Add(bmp);
        }
        Sprites = list;
    }

    public static IReadOnlyList<PropertyRowViewModel> From(IEnumerable<PropertyRow> rows, AppServices services, Action<uint>? play = null) =>
        rows.Select(r => new PropertyRowViewModel(r, services, play)).ToList();
}
