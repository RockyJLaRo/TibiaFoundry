using TibiaFoundry.Core.Common;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.Themes;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Views;

/// <summary>One sprite of the gallery; the image is decoded on first display.</summary>
public sealed class GalleryCell(uint id, ISpriteSource sprites) : ObservableObject
{
    private BitmapSource? _image;
    private RgbaImage? _rgba;
    private bool _requested;
    private bool _isMissing;

    public uint Id { get; } = id;
    public bool IsMissing { get => _isMissing; private set => SetProperty(ref _isMissing, value); }
    public string ToolTip => IsMissing ? L.F("SpriteGalleryWindow.Sprite0WasNotFound", Id) : L.F("SpriteGalleryWindow.Sprite0", Id) + (_rgba != null ? $" · {_rgba.Width}×{_rgba.Height}" : "");

    public BitmapSource? Image
    {
        get
        {
            if (!_requested)
            {
                _requested = true;
                _ = LoadAsync();
            }
            return _image;
        }
    }

    /// <summary>The decoded sprite (loads it synchronously when it has not been displayed yet).</summary>
    public RgbaImage? GetImage()
    {
        if (_rgba == null && !IsMissing) _rgba = Decode();
        return _rgba;
    }

    private RgbaImage? Decode()
    {
        try
        {
            return sprites.GetSprite(Id);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Sprite {Id} could not be read: {ex.Message}");
            return null;
        }
    }

    private async Task LoadAsync()
    {
        var rgba = _rgba ?? await Task.Run(Decode);
        _rgba = rgba;
        if (rgba == null)
        {
            IsMissing = true;
        }
        else
        {
            _image = Images.ToBitmapSource(rgba);
            OnPropertyChanged(nameof(Image));
        }
        OnPropertyChanged(nameof(ToolTip));
    }
}

/// <summary>
/// "View all sprites" window: every sprite of an appearance in a grid with a selectable number of
/// columns and background (the original ViewAllSprites form), plus zoom, copy and save.
/// </summary>
public partial class SpriteGalleryWindow : Window
{
    public static readonly DependencyProperty CellSizeProperty =
        DependencyProperty.Register(nameof(CellSize), typeof(double), typeof(SpriteGalleryWindow), new PropertyMetadata(66.0));

    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(SpriteGalleryWindow), new PropertyMetadata(1.0));

    public static readonly DependencyProperty CellBackgroundProperty =
        DependencyProperty.Register(nameof(CellBackground), typeof(Brush), typeof(SpriteGalleryWindow), new PropertyMetadata(null));

    private static readonly int[] ColumnChoices = [2, 3, 4, 8];

    private readonly List<GalleryCell> _cells;
    private readonly ClipboardService _clipboard = new();
    private readonly AppServices _services;
    private int _columns;

    private IDialogService _dialogs => _services.Dialogs;

    public double CellSize { get => (double)GetValue(CellSizeProperty); set => SetValue(CellSizeProperty, value); }
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public Brush? CellBackground { get => (Brush?)GetValue(CellBackgroundProperty); set => SetValue(CellBackgroundProperty, value); }

    public SpriteGalleryWindow(string title, IReadOnlyList<uint> ids, ISpriteSource sprites, int columns, AppServices services)
    {
        _services = services;
        InitializeComponent();
        Title = title;
        _cells = ids.Select(id => new GalleryCell(id, sprites)).ToList();
        _columns = ColumnChoices.Contains(columns) ? columns : 4;
        SetResourceReference(CellBackgroundProperty, "App.Checkerboard");
        ColumnsBox.SelectedIndex = Array.IndexOf(ColumnChoices, _columns);
        Footer.Text = L.F("SpriteGalleryWindow.0N0SpritesRightClickA", ids.Count);
        ThemeManager.Attach(this);
        Regroup();
    }

    /// <summary>Opens a non-modal gallery owned by the main window.</summary>
    public static void Open(string title, IReadOnlyList<uint> ids, ISpriteSource sprites, int columns, AppServices services)
    {
        var window = new SpriteGalleryWindow(title, ids, sprites, columns, services);
        if (Application.Current?.MainWindow is { IsVisible: true } main && !ReferenceEquals(main, window)) window.Owner = main;
        window.Show();
    }

    private void Regroup()
    {
        if (_cells == null) return;
        RowsList.ItemsSource = _cells.Chunk(_columns).ToList();
        // Width for the chosen column count so the grid is centered without horizontal scrolling.
        double cell = CellSize + 6;
        double wanted = Math.Max(MinWidth, _columns * cell + 80);
        if (IsLoaded && wanted > ActualWidth && WindowState == WindowState.Normal)
        {
            Width = Math.Min(wanted, SystemParameters.WorkArea.Width);
        }
        else if (!IsLoaded)
        {
            Width = Math.Min(Math.Max(Width, wanted), SystemParameters.WorkArea.Width);
        }
    }

    private void OnColumnsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColumnsBox.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int n) && n != _columns)
        {
            _columns = n;
            Regroup();
        }
    }

    private void OnZoomChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ZoomBox?.SelectedItem is ComboBoxItem { Tag: string tag } && double.TryParse(tag, System.Globalization.CultureInfo.InvariantCulture, out double z))
        {
            Zoom = z;
            CellSize = 64 * z + 2;
            Regroup();
        }
    }

    private void OnBackgroundChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        if (ReferenceEquals(sender, BgDefault)) SetResourceReference(CellBackgroundProperty, "App.Checkerboard");
        else if (ReferenceEquals(sender, BgMagenta)) CellBackground = Brushes.Magenta;
        else if (ReferenceEquals(sender, BgWhite)) CellBackground = Brushes.White;
        else if (ReferenceEquals(sender, BgBlack)) CellBackground = Brushes.Black;
    }

    private void OnCopyIds(object sender, RoutedEventArgs e) => _clipboard.SetText(string.Join(",", _cells.Select(c => c.Id)));

    /// <summary>Saves every sprite as &lt;id&gt;.png into a folder (Sprite exports by default); existing files follow Settings → File safety.</summary>
    private void OnSaveAll(object sender, RoutedEventArgs e)
    {
        string? folder = _services.Outputs.PickFolder(OutputKind.SpriteExports, L.T("SpriteGalleryWindow.SaveAllSpritesTo"));
        if (folder == null) return;
        int saved = 0, kept = 0;
        var missing = new List<uint>();
        try
        {
            using var output = _services.CreateOutput(L.T("Output.SpriteGallery"));
            foreach (var cell in _cells)
            {
                var image = cell.GetImage();
                if (image == null) { missing.Add(cell.Id); continue; }
                if (output.WriteFile(Path.Combine(folder, cell.Id + ".png"), temp => ImageIO.SavePng(image, temp)) != null) saved++;
                else kept++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            AppLog.Error("Saving gallery sprites failed.", ex);
            _services.Status.Show(L.T("SpriteGalleryWindow.SavingTheSpritesFailed") + ex.Message, StatusKind.Error);
            _dialogs.ShowError(L.T("SpriteGalleryWindow.SavingTheSpritesFailed2"), ex, log: false);
            return;
        }
        string after = (kept > 0 ? L.F("SpriteGalleryWindow.0Kept", L.Count(kept, "Noun.existing_file")) : "") + (missing.Count > 0 ? L.F("SpriteGalleryWindow.0Missing1", missing.Count, string.Join(", ", missing.Take(10))) : "");
        ShowSavedFooter(L.F("SpriteGalleryWindow.Saved0N0SpritesTo", saved), folder, after);
        _services.Status.ShowSaved(folder, isFile: false, before: L.F("SpriteGalleryWindow.Saved0N0SpritesTo", saved), after: after, kind: missing.Count + kept > 0 ? StatusKind.Warning : StatusKind.Success);
    }

    /// <summary>The footer names where the sprites went; the folder is a link that opens File Explorer.</summary>
    private void ShowSavedFooter(string before, string path, string after)
    {
        Footer.Inlines.Clear();
        Footer.Inlines.Add(new System.Windows.Documents.Run(before));
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(OutputLocations.ShortenForDisplay(path, 70))) { ToolTip = L.T("SpriteGalleryWindow.OpenInFileExplorer") + path };
        link.Click += (_, _) => _services.RevealSaved(new StatusLink(path, File.Exists(path)));
        Footer.Inlines.Add(link);
        if (after.Length > 0) Footer.Inlines.Add(new System.Windows.Documents.Run(after));
    }

    private static GalleryCell? CellOf(object sender) =>
        (sender as MenuItem)?.Parent is ContextMenu { PlacementTarget: FrameworkElement { DataContext: GalleryCell cell } } ? cell : null;

    private void OnCopyCellId(object sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _clipboard.SetText(cell.Id.ToString());
    }

    private void OnCopyCellImage(object sender, RoutedEventArgs e)
    {
        if (CellOf(sender)?.GetImage() is { } image) _clipboard.SetImage(image);
    }

    private void OnSaveCellImage(object sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is not { } cell || cell.GetImage() is not { } image) return;
        string? file = _services.Outputs.SaveFile(OutputKind.SpriteExports, L.T("SpriteGalleryWindow.SaveSprite"), L.T("SpriteGalleryWindow.PNGImagesPngPng"), cell.Id + ".png");
        if (file == null) return;
        try
        {
            ImageIO.SavePng(image, file);
            ShowSavedFooter(L.T("SpriteGalleryWindow.SavedTo"), file, "");
            _services.Status.ShowSaved(file, isFile: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Status.Show(L.T("SpriteGalleryWindow.SavingTheSpriteFailed") + ex.Message, StatusKind.Error);
            _dialogs.ShowError(L.T("SpriteGalleryWindow.SavingTheSpriteFailed2"), ex);
        }
    }
}
