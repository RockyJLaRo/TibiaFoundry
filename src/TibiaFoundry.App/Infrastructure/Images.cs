using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.App.Infrastructure;

/// <summary>Conversions between core images and WPF bitmaps.</summary>
/// <remarks>
/// WPF bitmaps are always created on the UI thread. A DispatcherObject created on a worker
/// thread silently gives that thread its own Dispatcher with a hidden window and a uniquely
/// named window class; on thread-pool threads those are never released, which leaks window
/// classes (user atoms) until window creation fails session-wide.
/// </remarks>
public static class Images
{
    /// <summary>
    /// Loads an image bundled with the program (a WPF resource such as "Assets/Credits/tibia-logo.png").
    /// Returns null, and logs a warning, when the file is missing or cannot be decoded, so callers can
    /// show a text fallback instead of a broken image.
    /// </summary>
    public static BitmapImage? TryLoadResource(string relativePath)
    {
        try
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;   // registers the pack:// scheme outside a running app
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri("pack://application:,,,/" + relativePath.TrimStart('/'), UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or InvalidOperationException or UriFormatException)
        {
            AppLog.Warn($"The bundled image {relativePath} could not be loaded; its name is shown instead.", ex);
            return null;
        }
    }

    /// <summary>
    /// Loads a bundled .ico file and returns its largest image up to <paramref name="maxSize"/> pixels
    /// (it is drawn smaller, so the larger source keeps it sharp on high-DPI screens). Returns null,
    /// with a warning in the log, when the file is missing or damaged.
    /// </summary>
    public static BitmapSource? TryLoadIcon(string relativePath, int maxSize = 48)
    {
        try
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            _ = Application.Current;   // initializes WPF's Application type, which registers the "application" package
            // The assembly is named so the icon also loads without a running Application (tests).
            var uri = new Uri($"pack://application:,,,/{typeof(Images).Assembly.GetName().Name};component/{relativePath.TrimStart('/')}", UriKind.Absolute);
            var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Where(f => f.PixelWidth <= maxSize).OrderByDescending(f => f.PixelWidth).FirstOrDefault()
                ?? decoder.Frames.OrderBy(f => f.PixelWidth).First();
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or InvalidOperationException or UriFormatException or ArgumentException)
        {
            AppLog.Warn($"The bundled icon {relativePath} could not be loaded; a simple badge is shown instead.", ex);
            return null;
        }
    }

    /// <summary>Creates a frozen WPF bitmap from a core image (marshalled to the UI thread if needed).</summary>
    public static BitmapSource ToBitmapSource(RgbaImage image)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) return dispatcher.Invoke(() => Create(image));
        return Create(image);
    }

    /// <summary>Creates a frozen WPF bitmap on the UI thread without blocking the calling worker thread.</summary>
    public static Task<BitmapSource> ToBitmapSourceAsync(RgbaImage image, DispatcherPriority priority = DispatcherPriority.Background)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) return Task.FromResult(Create(image));
        return dispatcher.InvokeAsync(() => Create(image), priority).Task;
    }

    private static BitmapSource Create(RgbaImage image)
    {
        var bmp = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    public static RgbaImage FromBitmapSource(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var image = new RgbaImage(converted.PixelWidth, converted.PixelHeight);
        converted.CopyPixels(image.Pixels, image.Width * 4, 0);
        return image;
    }

    public static IReadOnlyList<BitmapSource> ToBitmapSources(IEnumerable<RgbaImage> frames) => frames.Select(ToBitmapSource).ToList();
}

/// <summary>
/// Thumbnail cache for sprites shown in large virtualised lists. Images are decoded on worker
/// threads (bounded concurrency) and converted to frozen bitmaps on the UI thread at background
/// priority, so scrolling never blocks.
/// </summary>
public sealed class SpriteImageCache : IDisposable
{
    private const int Capacity = 6000;
    private readonly ConcurrentDictionary<uint, BitmapSource?> _cache = new();
    private readonly ConcurrentQueue<uint> _order = new();
    private readonly SemaphoreSlim _gate = new(Math.Max(2, Environment.ProcessorCount / 2));
    private ISpriteSource? _source;

    public ISpriteSource? Source
    {
        get => _source;
        set
        {
            _source = value;
            Clear();
        }
    }

    public bool TryGet(uint spriteId, out BitmapSource? image) => _cache.TryGetValue(spriteId, out image);

    public async Task<BitmapSource?> GetAsync(uint spriteId)
    {
        if (_cache.TryGetValue(spriteId, out var cached)) return cached;
        var source = _source;
        if (source == null) return null;
        RgbaImage? image;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(spriteId, out cached)) return cached;
            image = await Task.Run(() => source.GetSprite(spriteId)).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        var bitmap = image == null ? null : await Images.ToBitmapSourceAsync(image).ConfigureAwait(false);
        if (ReferenceEquals(source, _source))
        {
            _cache[spriteId] = bitmap;
            _order.Enqueue(spriteId);
            while (_cache.Count > Capacity && _order.TryDequeue(out uint old)) _cache.TryRemove(old, out _);
        }
        return bitmap;
    }

    public void Clear()
    {
        _cache.Clear();
        while (_order.TryDequeue(out _)) { }
    }

    public void Dispose() => _gate.Dispose();
}
