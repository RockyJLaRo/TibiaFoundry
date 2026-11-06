using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Tibia.Protobuf.Appearances;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.Core.Appearances;

namespace TibiaFoundry.App.ViewModels;

/// <summary>
/// Row of the appearance list. The thumbnail is requested only when a row becomes visible
/// (virtualised list), decoded on a worker thread and published back on the UI thread.
/// </summary>
public sealed class AppearanceItem : ObservableObject
{
    private readonly SpriteImageCache _cache;
    private BitmapSource? _thumbnail;
    private bool _requested;

    public AppearanceItem(Appearance appearance, AppearanceKind kind, SpriteImageCache cache)
    {
        Appearance = appearance;
        Kind = kind;
        _cache = cache;
        PreviewSpriteId = AppearanceDatabase.PreviewSpriteId(appearance, kind);
    }

    public Appearance Appearance { get; }
    public AppearanceKind Kind { get; }
    public uint Id => Appearance.Id;
    public string Name => Appearance.Name;
    public uint? PreviewSpriteId { get; }

    public BitmapSource? Thumbnail
    {
        get
        {
            if (_thumbnail == null && !_requested && PreviewSpriteId is uint id)
            {
                if (_cache.TryGet(id, out var cached)) _thumbnail = cached;
                else
                {
                    _requested = true;
                    _ = LoadAsync(id);
                }
            }
            return _thumbnail;
        }
    }

    private async Task LoadAsync(uint spriteId)
    {
        var image = await _cache.GetAsync(spriteId).ConfigureAwait(false);
        if (image == null) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _thumbnail = image;
            OnPropertyChanged(nameof(Thumbnail));
        });
    }

    /// <summary>Forgets the thumbnail (e.g. after the sprite source changed).</summary>
    public void ResetThumbnail()
    {
        _thumbnail = null;
        _requested = false;
        OnPropertyChanged(nameof(Thumbnail));
    }
}
