using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TibiaFoundry.App.Controls;

/// <summary>
/// Plays a sequence of frames with per-frame delays (or a forced delay), drawn centered and scaled
/// by an integer-friendly zoom with nearest-neighbor sampling so pixel art stays crisp.
/// The timer only runs while the control is loaded and visible.
/// </summary>
public sealed class AnimatedSprite : FrameworkElement
{
    public static readonly DependencyProperty FramesProperty = DependencyProperty.Register(nameof(Frames), typeof(IReadOnlyList<BitmapSource>), typeof(AnimatedSprite),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((AnimatedSprite)d).Restart()));
    public static readonly DependencyProperty DelaysProperty = DependencyProperty.Register(nameof(Delays), typeof(IReadOnlyList<int>), typeof(AnimatedSprite),
        new PropertyMetadata(null, (d, _) => ((AnimatedSprite)d).Restart()));
    public static readonly DependencyProperty IsAnimatingProperty = DependencyProperty.Register(nameof(IsAnimating), typeof(bool), typeof(AnimatedSprite),
        new PropertyMetadata(true, (d, _) => ((AnimatedSprite)d).Restart()));
    public static readonly DependencyProperty ForcedDelayProperty = DependencyProperty.Register(nameof(ForcedDelay), typeof(int), typeof(AnimatedSprite),
        new PropertyMetadata(0));
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(AnimatedSprite),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FitProperty = DependencyProperty.Register(nameof(Fit), typeof(bool), typeof(AnimatedSprite),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
    private int _frame;

    private static bool _animationsEnabled = true;

    /// <summary>Settings → Appearance → "Animate previews". When off, every preview shows its first frame.</summary>
    public static bool AnimationsEnabled
    {
        get => _animationsEnabled;
        set
        {
            if (_animationsEnabled == value) return;
            _animationsEnabled = value;
            AnimationsEnabledChanged?.Invoke();
        }
    }

    private static event Action? AnimationsEnabledChanged;

    public AnimatedSprite()
    {
        _timer.Tick += (_, _) => Advance();
        Loaded += (_, _) =>
        {
            AnimationsEnabledChanged += Restart;
            Restart();
        };
        Unloaded += (_, _) =>
        {
            AnimationsEnabledChanged -= Restart;
            _timer.Stop();
        };
        IsVisibleChanged += (_, _) => Restart();
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    public IReadOnlyList<BitmapSource>? Frames { get => (IReadOnlyList<BitmapSource>?)GetValue(FramesProperty); set => SetValue(FramesProperty, value); }
    public IReadOnlyList<int>? Delays { get => (IReadOnlyList<int>?)GetValue(DelaysProperty); set => SetValue(DelaysProperty, value); }
    public bool IsAnimating { get => (bool)GetValue(IsAnimatingProperty); set => SetValue(IsAnimatingProperty, value); }
    /// <summary>When greater than zero, every frame uses this delay (the original "ForceDelay").</summary>
    public int ForcedDelay { get => (int)GetValue(ForcedDelayProperty); set => SetValue(ForcedDelayProperty, value); }
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    /// <summary>Scale the frame to the available space (keeping pixels square) instead of using <see cref="Zoom"/>.</summary>
    public bool Fit { get => (bool)GetValue(FitProperty); set => SetValue(FitProperty, value); }

    private BitmapSource? Current => Frames is { Count: > 0 } f ? f[Math.Min(_frame, f.Count - 1)] : null;

    private void Restart()
    {
        _timer.Stop();
        _frame = 0;
        InvalidateVisual();
        if (IsAnimating && AnimationsEnabled && IsLoaded && IsVisible && Frames is { Count: > 1 })
        {
            _timer.Interval = TimeSpan.FromMilliseconds(DelayOf(0));
            _timer.Start();
        }
    }

    private int DelayOf(int index)
    {
        if (ForcedDelay > 0) return ForcedDelay;
        var delays = Delays;
        int d = delays is { Count: > 0 } ? delays[index % delays.Count] : 100;
        return Math.Max(10, d);
    }

    private void Advance()
    {
        var frames = Frames;
        if (frames == null || frames.Count < 2) { _timer.Stop(); return; }
        _frame = (_frame + 1) % frames.Count;
        _timer.Interval = TimeSpan.FromMilliseconds(DelayOf(_frame));
        InvalidateVisual();
    }

    private double EffectiveZoom(Size available, BitmapSource frame)
    {
        if (!Fit) return Zoom;
        double zx = double.IsInfinity(available.Width) ? Zoom : available.Width / frame.PixelWidth;
        double zy = double.IsInfinity(available.Height) ? Zoom : available.Height / frame.PixelHeight;
        double z = Math.Min(zx, zy);
        return z >= 1 ? Math.Floor(z) : z;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var f = Frames is { Count: > 0 } frames ? frames[0] : null;
        if (f == null) return new Size(0, 0);
        if (Fit) return new Size(double.IsInfinity(availableSize.Width) ? f.PixelWidth * Zoom : availableSize.Width,
                                 double.IsInfinity(availableSize.Height) ? f.PixelHeight * Zoom : availableSize.Height);
        int maxW = Frames!.Max(x => x.PixelWidth), maxH = Frames!.Max(x => x.PixelHeight);
        return new Size(maxW * Zoom, maxH * Zoom);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var frame = Current;
        if (frame == null) return;
        double z = EffectiveZoom(RenderSize, frame);
        double w = frame.PixelWidth * z, h = frame.PixelHeight * z;
        // Sprites anchor at their bottom-right like in the client; center the largest frame box.
        int maxW = Frames!.Max(x => x.PixelWidth), maxH = Frames!.Max(x => x.PixelHeight);
        double boxW = maxW * z, boxH = maxH * z;
        double left = (ActualWidth - boxW) / 2 + (boxW - w);
        double top = (ActualHeight - boxH) / 2 + (boxH - h);
        drawingContext.DrawImage(frame, new Rect(Math.Round(left), Math.Round(top), w, h));
    }
}
