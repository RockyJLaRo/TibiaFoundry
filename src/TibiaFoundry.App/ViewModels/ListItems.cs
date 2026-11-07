using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.Core.Imaging;

namespace TibiaFoundry.App.ViewModels;

/// <summary>
/// Generic list row with an id, a name, optional detail text and a lazily rendered thumbnail.
/// </summary>
public sealed class ListItem : ObservableObject
{
    private readonly Func<RgbaImage?>? _thumbnailLoader;
    private BitmapSource? _thumbnail;
    private bool _requested;

    public ListItem(string id, string name, object source, string detail = "", Func<RgbaImage?>? thumbnailLoader = null)
    {
        Id = id;
        Name = name;
        Source = source;
        Detail = detail;
        _thumbnailLoader = thumbnailLoader;
    }

    public string Id { get; }
    public string Name { get; }
    public string Detail { get; }
    public object Source { get; }
    public bool HasThumbnail => _thumbnailLoader != null;

    public BitmapSource? Thumbnail
    {
        get
        {
            if (_thumbnail == null && !_requested && _thumbnailLoader != null)
            {
                _requested = true;
                var loader = _thumbnailLoader;
                Task.Run(() =>
                {
                    try { return loader(); }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or OutfitRenderException) { return null; }
                }).ContinueWith(t =>
                {
                    if (t.Result is not { } image) return;
                    // The bitmap is created on the UI thread (see Images).
                    Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                    {
                        _thumbnail = Images.ToBitmapSource(image);
                        OnPropertyChanged(nameof(Thumbnail));
                    });
                }, TaskScheduler.Default);
            }
            return _thumbnail;
        }
    }

    public bool Matches(string text) =>
        text.Length == 0 || Name.Contains(text, StringComparison.OrdinalIgnoreCase) || Id.Contains(text, StringComparison.OrdinalIgnoreCase) || Detail.Contains(text, StringComparison.OrdinalIgnoreCase);
}
