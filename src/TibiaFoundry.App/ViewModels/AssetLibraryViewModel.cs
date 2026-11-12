using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Library;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>A thumbnail tile of the asset grid; the picture is loaded in the background.</summary>
public sealed partial class AssetTile(AssetRecord asset) : ObservableObject
{
    public AssetRecord Asset { get; } = asset;
    public long Id => Asset.Id;
    public string Name => Asset.Name;
    public string Detail => string.Join(" · ", new[] { Asset.Format.ToUpperInvariant(), Asset.Dimensions, Asset.IsAnimated ? "animated" : null, Asset.Missing ? "missing" : null }.Where(s => !string.IsNullOrEmpty(s)));
    public string ToolTip => $"{Asset.Name}\n{Asset.Category} · {Detail}\n{Asset.Path}";
    /// <summary>A glyph for files without a picture (sounds, data) or while the thumbnail loads.</summary>
    public string Glyph => Asset.Kind switch { MediaKind.Audio => "", MediaKind.Data => "", _ => "" };
    public bool IsMissing => Asset.Missing;

    [ObservableProperty] private BitmapSource? _thumbnail;
}

/// <summary>A category or collection in the side list.</summary>
public sealed record LibraryListEntry(string Name, int Count, bool IsCustom, long? CollectionId = null)
{
    public string CountText => Count.ToString("N0");
}

/// <summary>An editable custom field of an asset.</summary>
public sealed partial class FieldRow : ObservableObject
{
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _value = "";
}

/// <summary>A group of the duplicate report, as listed.</summary>
public sealed record DuplicateRow(DuplicateGroup Group)
{
    public string Title => $"{Group.Description} — {L.Count(Group.Assets.Count, "Noun.file")}";
    public IReadOnlyList<AssetRecord> Assets => Group.Assets;
    public string Wasted => Group.WastedBytes > 0 ? ReportWriter.FormatSize(Group.WastedBytes) + L.T("AssetLibrary.InExtraCopies") : "";
}

/// <summary>
/// Asset Library (8.8): a searchable, local library of the images, animations, sounds and other files
/// a fansite works with. Files stay where they are; the library indexes them (incrementally), shows
/// thumbnails, keeps metadata, categories, tags, collections and versions, and finds duplicates. Every
/// tool's exports are added automatically (Library settings), and Content Studio turns selections into
/// galleries and export packages.
/// </summary>
public sealed partial class AssetLibraryViewModel : PageViewModel
{
    public const int PageSize = 240;
    public const string AllAssets = "All assets";

    public AssetLibraryViewModel(AppServices services, MainViewModel main) : base(services, BuiltInTools.AssetLibrary)
    {
        _main = main;
        services.Library.LibraryChanged += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { if (_isShown) _ = RefreshAsync(); });
    }

    private readonly MainViewModel _main;
    private bool _isShown;
    private int _thumbVersion;

    private AssetLibrary? Lib => Services.Library.TryLibrary(out _);

    // ------------------------------------------------------------------ state

    public ObservableCollection<AssetTile> Tiles { get; } = [];
    public ObservableCollection<LibraryListEntry> CategoryList { get; } = [];
    public ObservableCollection<LibraryListEntry> CollectionList { get; } = [];
    public ObservableCollection<DuplicateRow> Duplicates { get; } = [];
    public ObservableCollection<LibraryRoot> Roots { get; } = [];
    public ObservableCollection<FieldRow> Fields { get; } = [];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private LibraryListEntry? _selectedCategory;
    [ObservableProperty] private LibraryListEntry? _selectedCollection;
    [ObservableProperty] private string? _formatFilter;
    [ObservableProperty] private string? _versionFilter;
    [ObservableProperty] private string? _sourceFilter;
    [ObservableProperty] private string? _tagFilter;
    [ObservableProperty] private string _creatorFilter = "";
    [ObservableProperty] private string _animationFilter = "Any";
    [ObservableProperty] private string _statusFilter = "Any";
    [ObservableProperty] private string _minWidth = "";
    [ObservableProperty] private string _maxWidth = "";
    [ObservableProperty] private string _addedWithin = "Any time";
    [ObservableProperty] private bool _hideMissing;
    [ObservableProperty] private AssetSort _sort = AssetSort.Name;
    [ObservableProperty] private int _pageIndex;
    [ObservableProperty] private int _total;
    [ObservableProperty] private string _statistics = "";
    [ObservableProperty] private string? _problem;
    [ObservableProperty] private int _tab;

    public IReadOnlyList<string> Formats { get; private set; } = [];
    public IReadOnlyList<string> Versions { get; private set; } = [];
    public IReadOnlyList<string> Sources { get; private set; } = [];
    public IReadOnlyList<string> TagChoices { get; private set; } = [];
    public IReadOnlyList<string> AllCategories { get; private set; } = [];
    public static IReadOnlyList<string> AnimationChoices { get; } = ["Any", "Animated", "Still"];
    public static IReadOnlyList<string> StatusChoices { get; } = ["Any", .. ExportStates.All];
    public static IReadOnlyList<string> DateChoices { get; } = ["Any time", "Today", "Last 7 days", "Last 30 days", "Last year"]; // loc: identity (shown through Choice.Prefix)
    public static IReadOnlyList<AssetSort> SortChoices { get; } = Enum.GetValues<AssetSort>();
    public static IReadOnlyList<string> ExportStatusChoices => ExportStates.All;

    public string PageText => Total == 0 ? L.T("AssetLibrary.NoAssets") : L.F("AssetLibrary.0N01N0Of2", PageIndex * PageSize + 1, Math.Min(Total, (PageIndex + 1) * PageSize), Total);
    public bool CanPrevious => PageIndex > 0;
    public bool CanNext => (PageIndex + 1) * PageSize < Total;
    public bool IsEmpty => Total == 0;
    public bool LibraryIsEmpty { get; private set; }

    public string EmptyText => LibraryIsEmpty
        ? L.T("AssetLibrary.YourLibraryIsEmptyAddA")
        : L.T("AssetLibrary.NothingMatchesClearTheSearchOr");

    partial void OnSearchTextChanged(string value) => Requery();
    partial void OnSelectedCategoryChanged(LibraryListEntry? value) { if (value != null) SelectedCollection = null; Requery(); }
    partial void OnSelectedCollectionChanged(LibraryListEntry? value) { if (value != null) SelectedCategory = null; Requery(); OnPropertyChanged(nameof(HasCollection)); }
    partial void OnFormatFilterChanged(string? value) => Requery();
    partial void OnVersionFilterChanged(string? value) => Requery();
    partial void OnSourceFilterChanged(string? value) => Requery();
    partial void OnTagFilterChanged(string? value) => Requery();
    partial void OnCreatorFilterChanged(string value) => Requery();
    partial void OnAnimationFilterChanged(string value) => Requery();
    partial void OnStatusFilterChanged(string value) => Requery();
    partial void OnMinWidthChanged(string value) => Requery();
    partial void OnMaxWidthChanged(string value) => Requery();
    partial void OnAddedWithinChanged(string value) => Requery();
    partial void OnHideMissingChanged(bool value) => Requery();
    partial void OnSortChanged(AssetSort value) => Requery();
    partial void OnTabChanged(int value) { if (value == 2) LoadRoots(); }

    public bool HasCollection => SelectedCollection != null;

    private bool _suspendQuery;

    private void Requery()
    {
        if (_suspendQuery) return;
        PageIndex = 0;
        _ = LoadPageAsync();
    }

    public override async Task OnNavigatedToAsync()
    {
        _isShown = true;
        await RefreshAsync();
    }

    /// <summary>Reloads the side lists, the filter choices, the statistics and the current page.</summary>
    public async Task RefreshAsync()
    {
        var lib = Services.Library.TryLibrary(out string? problem);
        Problem = problem == null ? null : L.F("AssetLibrary.TheAssetLibraryCannotBeOpened", problem);
        if (lib == null) return;
        var (categories, collections, facets, stats) = await Task.Run(() => (lib.Categories(), lib.Collections(), lib.Facets(), lib.Statistics()));
        string? category = SelectedCategory?.Name;
        long? collection = SelectedCollection?.CollectionId;
        CategoryList.Clear();
        CategoryList.Add(new LibraryListEntry(AllAssets, stats.Assets, false));
        foreach (var c in categories.Where(c => c.Count > 0 || c.IsCustom)) CategoryList.Add(new LibraryListEntry(c.Name, c.Count, c.IsCustom));
        CollectionList.Clear();
        foreach (var c in collections) CollectionList.Add(new LibraryListEntry(c.Name, c.Count, true, c.Id));
        AllCategories = categories.Select(c => c.Name).ToList();
        Formats = facets.Formats.Select(f => f.Value).ToList();
        Versions = facets.ClientVersions.Select(f => f.Value).ToList();
        Sources = facets.Sources.Select(f => f.Value).ToList();
        TagChoices = facets.Tags.Select(f => f.Value).ToList();
        OnPropertyChanged(nameof(AllCategories));
        OnPropertyChanged(nameof(Formats));
        OnPropertyChanged(nameof(Versions));
        OnPropertyChanged(nameof(Sources));
        OnPropertyChanged(nameof(TagChoices));
        LibraryIsEmpty = stats.Assets == 0;
        OnPropertyChanged(nameof(LibraryIsEmpty));
        Statistics = L.F("AssetLibrary.0123Indexed", L.Count(stats.Assets, "Noun.asset"), ReportWriter.FormatSize(stats.Bytes), L.Count(stats.Collections, "Noun.collection"), L.Count(stats.Roots, "Noun.folder")) +
                     (stats.Missing > 0 ? L.F("AssetLibrary.0N0Missing", stats.Missing) : "") + (stats.LastScanUtc is { } t ? L.F("AssetLibrary.LastScan0YyyyMMDd", t.ToLocalTime()) : "");
        // Keep the selection across a refresh without re-running the query for each list.
        _suspendQuery = true;
        try
        {
            var col = collection == null ? null : CollectionList.FirstOrDefault(c => c.CollectionId == collection);
            SelectedCategory = col != null ? null : CategoryList.FirstOrDefault(c => c.Name == category) ?? CategoryList[0];
            SelectedCollection = col;
        }
        finally
        {
            _suspendQuery = false;
        }
        OnPropertyChanged(nameof(HasCollection));
        OnPropertyChanged(nameof(LibraryFolder));
        OnPropertyChanged(nameof(AddExportsAutomatically));
        OnPropertyChanged(nameof(AutoExportCollection));
        await LoadPageAsync();
    }

    private AssetQuery Query()
    {
        static int? Number(string s) => int.TryParse(s, out int n) && n > 0 ? n : null;
        DateTime? from = AddedWithin switch
        {
            "Today" => DateTime.Today.ToUniversalTime(),
            "Last 7 days" => DateTime.UtcNow.AddDays(-7),
            "Last 30 days" => DateTime.UtcNow.AddDays(-30),
            "Last year" => DateTime.UtcNow.AddYears(-1),
            _ => null,
        };
        return new AssetQuery
        {
            Text = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            Category = SelectedCategory is { Name: not AllAssets } c ? c.Name : null,
            CollectionId = SelectedCollection?.CollectionId,
            Format = FormatFilter, ClientVersion = VersionFilter, Source = SourceFilter, Tag = TagFilter,
            Creator = string.IsNullOrWhiteSpace(CreatorFilter) ? null : CreatorFilter.Trim(),
            Animated = AnimationFilter switch { "Animated" => true, "Still" => false, _ => null },
            ExportStatus = StatusFilter == "Any" ? null : StatusFilter,
            MinWidth = Number(MinWidth), MaxWidth = Number(MaxWidth), CreatedFromUtc = from, IncludeMissing = !HideMissing, Sort = Sort,
            Offset = PageIndex * PageSize, Limit = PageSize,
        };
    }

    private int _loadVersion;

    private async Task LoadPageAsync()
    {
        var lib = Lib;
        if (lib == null) return;
        int version = ++_loadVersion;
        var query = Query();
        AssetPage page;
        try
        {
            page = await Task.Run(() => lib.Search(query));
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            Problem = L.T("AssetLibrary.SearchingTheLibraryFailed") + ex.Message;
            return;
        }
        if (version != _loadVersion) return;
        long? keep = SelectedTile?.Id;
        Tiles.Clear();
        foreach (var a in page.Assets) Tiles.Add(new AssetTile(a));
        Total = page.Total;
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(CanPrevious));
        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        SelectedTile = Tiles.FirstOrDefault(t => t.Id == keep) ?? Tiles.FirstOrDefault();
        LoadThumbnails(lib);
    }

    private void LoadThumbnails(AssetLibrary lib)
    {
        // A newer page stops the older page's loading (the version changes).
        int version = Interlocked.Increment(ref _thumbVersion);
        var tiles = Tiles.ToList();
        _ = Task.Run(() =>
        {
            foreach (var tile in tiles)
            {
                if (version != Volatile.Read(ref _thumbVersion)) return;
                string? path = ThumbnailCache.Get(lib, tile.Asset, 96);
                if (path == null) continue;
                var bmp = LoadBitmap(path);
                if (bmp != null) System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => tile.Thumbnail = bmp);
            }
        });
    }

    internal static BitmapSource? LoadBitmap(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    [RelayCommand]
    private Task PreviousPage()
    {
        if (!CanPrevious) return Task.CompletedTask;
        PageIndex--;
        return LoadPageAsync();
    }

    [RelayCommand]
    private Task NextPage()
    {
        if (!CanNext) return Task.CompletedTask;
        PageIndex++;
        return LoadPageAsync();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suspendQuery = true;
        try
        {
            SearchText = "";
            FormatFilter = VersionFilter = SourceFilter = TagFilter = null;
            CreatorFilter = MinWidth = MaxWidth = "";
            AnimationFilter = StatusFilter = "Any";
            AddedWithin = "Any time"; // loc: identity
            HideMissing = false;
            SelectedCategory = CategoryList.FirstOrDefault();
        }
        finally
        {
            _suspendQuery = false;
        }
        Requery();
    }

    // ------------------------------------------------------------------ selection and details

    [ObservableProperty] private AssetTile? _selectedTile;

    /// <summary>All selected tiles (set by the view; the details show the first).</summary>
    public IReadOnlyList<AssetTile> SelectedTiles { get; set; } = [];

    public int SelectionCount => SelectedTiles.Count;
    public string SelectionText => SelectedTiles.Count > 1 ? L.F("AssetLibrary.0N0SelectedEditsApplyTo", SelectedTiles.Count) : "";

    public void SetSelection(IReadOnlyList<AssetTile> tiles)
    {
        SelectedTiles = tiles;
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(HasSelection));
    }

    public bool HasSelection => SelectedTile != null;

    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editCategory = "";
    [ObservableProperty] private string _editDescription = "";
    [ObservableProperty] private string _editTags = "";
    [ObservableProperty] private string _editVersion = "";
    [ObservableProperty] private string _editOriginalId = "";
    [ObservableProperty] private string _editCreator = "";
    [ObservableProperty] private string _editAttribution = "";
    [ObservableProperty] private string _editCopyright = "";
    [ObservableProperty] private string _editStatus = ExportStates.NotExported;
    [ObservableProperty] private IReadOnlyList<System.Windows.Media.Imaging.BitmapSource>? _previewFrames;
    [ObservableProperty] private IReadOnlyList<int>? _previewDelays;
    [ObservableProperty] private string _facts = "";
    [ObservableProperty] private string _memberships = "";
    [ObservableProperty] private string _versionHistory = "";
    [ObservableProperty] private IReadOnlyList<AssetRecord> _related = [];

    public bool IsAudio => SelectedTile?.Asset.Kind == MediaKind.Audio;
    public bool HasPreview => PreviewFrames is { Count: > 0 };

    partial void OnSelectedTileChanged(AssetTile? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsAudio));
        _ = ShowDetailsAsync(value);
    }

    private int _detailsVersion;

    /// <summary>The asset whose values the editor was filled with, so a save of several assets writes only what was changed.</summary>
    private AssetRecord? _shownDetails;

    private async Task ShowDetailsAsync(AssetTile? tile)
    {
        int version = ++_detailsVersion;
        PreviewFrames = null;
        PreviewDelays = null;
        OnPropertyChanged(nameof(HasPreview));
        Fields.Clear();
        if (tile == null || Lib is not { } lib) return;
        var a = await Task.Run(() => lib.Get(tile.Id)) ?? tile.Asset;
        if (version != _detailsVersion) return;
        _shownDetails = a;
        EditName = a.Name;
        EditCategory = a.Category;
        EditDescription = a.Description ?? "";
        EditTags = string.Join(", ", a.Tags);
        EditVersion = a.ClientVersion ?? "";
        EditOriginalId = a.OriginalId ?? "";
        EditCreator = a.Creator ?? "";
        EditAttribution = a.Attribution ?? "";
        EditCopyright = a.Copyright ?? "";
        EditStatus = a.ExportStatus;
        foreach (var (k, v) in a.Fields) Fields.Add(new FieldRow { Key = k, Value = v });
        Facts = string.Join(Environment.NewLine, new[]
        {
            a.Path,
            $"{a.Format.ToUpperInvariant()}{(a.Width > 0 ? $" · {a.Width}×{a.Height}" : "")}{(a.Frames > 1 ? $" · {a.Frames} frames" : "")} · {ReportWriter.FormatSize(a.SizeBytes)}" + (a.Managed ? L.T("AssetLibrary.CopyKeptInTheLibrary") : ""),
            L.F("AssetLibrary.Added0YyyyMMDdHH", a.CreatedUtc.ToLocalTime(), a.ModifiedUtc.ToLocalTime(), a.FileModifiedUtc.ToLocalTime()),
            a.Source != null || a.SourceTool != null ? L.F("AssetLibrary.Source0", a.Source ?? a.SourceTool) : null,
            a.Sha256 != null ? $"SHA-256: {a.Sha256}" : null,
            a.Missing ? L.T("AssetLibrary.TheFileWasNotFoundAt") : null,
        }.Where(s => s != null));
        var (collections, versions, related) = await Task.Run(() => (lib.CollectionsOf(a.Id), lib.Versions(a.Id), lib.Related(a.Id)));
        if (version != _detailsVersion) return;
        Memberships = collections.Count == 0 ? L.T("AssetLibrary.InNoCollection") : "In " + string.Join(", ", collections) + ".";
        VersionHistory = versions.Count == 0 ? "" : $"{L.Count(versions.Count, "Noun.earlier_version")}: " +
            string.Join("; ", versions.Take(5).Select(v => $"{v.SeenUtc.ToLocalTime():yyyy-MM-dd} ({ReportWriter.FormatSize(v.SizeBytes)}, {v.Sha256?[..8]})"));
        Related = related;
        if (a.Kind is MediaKind.Image or MediaKind.Animation && !a.Missing && a.SizeBytes <= Services.Settings.Performance.PreviewSizeLimitMegabytes * 1024L * 1024)
        {
            try
            {
                var (frames, delays) = await Task.Run(() => MediaInspector.LoadFrames(a.Path));
                if (version != _detailsVersion) return;
                PreviewFrames = frames.Take(400).Select(Infrastructure.Images.ToBitmapSource).ToList();
                PreviewDelays = delays.Select(d => Math.Max(20, d)).ToList();
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidDataException or ImageMagick.MagickException or UnauthorizedAccessException)
            {
                Facts += Environment.NewLine + L.T("AssetLibrary.ThePreviewCouldNotBeMade") + ex.Message;
            }
        }
        OnPropertyChanged(nameof(HasPreview));
    }

    private IReadOnlyList<long> SelectedIds() => SelectedTiles.Count > 0 ? SelectedTiles.Select(t => t.Id).ToList() : SelectedTile != null ? [SelectedTile.Id] : [];

    [RelayCommand]
    private async Task SaveDetailsAsync()
    {
        var ids = SelectedIds();
        if (ids.Count == 0 || Lib is not { } lib) return;
        bool many = ids.Count > 1;
        // With several assets selected, only the fields the user changed are written (each asset keeps its own
        // description, version and so on), never the name, ID or attribution of one; tags are added and removed.
        var shown = _shownDetails;
        string? Changed(string now, string? before) => many && string.Equals(now.Trim(), (before ?? "").Trim(), StringComparison.Ordinal) ? null : now;
        var tags = AssetLibrary.NormalizeTags([EditTags]);
        var edit = new AssetEdit
        {
            Name = many ? null : EditName,
            Category = string.IsNullOrWhiteSpace(EditCategory) || Changed(EditCategory, shown?.Category) == null ? null : EditCategory.Trim(),
            Description = Changed(EditDescription, shown?.Description), ClientVersion = Changed(EditVersion, shown?.ClientVersion),
            OriginalId = many ? null : EditOriginalId, Creator = Changed(EditCreator, shown?.Creator), Attribution = many ? null : EditAttribution,
            Copyright = Changed(EditCopyright, shown?.Copyright), ExportStatus = Changed(EditStatus, shown?.ExportStatus),
            Tags = many ? null : tags,
            Fields = many ? null : Fields.Where(f => f.Key.Trim().Length > 0).GroupBy(f => f.Key.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last().Value),
        };
        if (many)
        {
            var before = shown?.Tags ?? [];
            var added = tags.Except(before, StringComparer.OrdinalIgnoreCase).ToList();
            var removed = before.Except(tags, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (added.Count > 0 || removed.Count > 0)
            {
                bool tagged = Try(L.T("AssetLibrary.SavingTheTags"), () =>
                {
                    foreach (long id in ids)
                        if (lib.Get(id) is { } asset)
                            lib.Update([id], new AssetEdit { Tags = asset.Tags.Where(t => !removed.Contains(t)).Concat(added).Distinct(StringComparer.OrdinalIgnoreCase).ToList() });
                });
                if (!tagged) return;
            }
        }
        if (!Try(L.T("AssetLibrary.SavingTheAssetDetails"), () => lib.Update(ids, edit))) return;
        Info(L.F("AssetLibrary.SavedTheDetailsOf0The", L.Count(ids.Count, "Noun.asset")));
        await RefreshAsync();
    }

    [RelayCommand]
    private void AddField() => Fields.Add(new FieldRow { Key = L.T("Metadata.Field"), Value = "" });

    [RelayCommand]
    private void RemoveField(FieldRow? row)
    {
        if (row != null) Fields.Remove(row);
    }

    [RelayCommand]
    private void OpenFile()
    {
        if (SelectedTile?.Asset is not { } a) return;
        if (!File.Exists(a.Path)) { Services.Status.Show(L.F("AssetLibrary.0NoLongerExists", a.Path), StatusKind.Warning); return; }
        Services.Shell.OpenFile(a.Path);
    }

    [RelayCommand]
    private void ShowInExplorer()
    {
        if (SelectedTile?.Asset is { } a) Services.RevealSaved(new StatusLink(a.Path, true));
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (SelectedTile?.Asset is { } a) Services.Clipboard.SetText(string.Join(Environment.NewLine, SelectedTiles.Count > 1 ? SelectedTiles.Select(t => t.Asset.Path) : [a.Path]));
    }

    [RelayCommand]
    private void PlaySound()
    {
        if (SelectedTile?.Asset is { Kind: MediaKind.Audio } a && File.Exists(a.Path))
            Try(L.T("AssetLibrary.PlayingTheSound"), () => Services.Audio.Play(a.Path));
    }

    [RelayCommand]
    private void StopSound() => Services.Audio.Stop();

    [RelayCommand]
    private void OpenRelated(AssetRecord? asset)
    {
        if (asset == null) return;
        SearchText = asset.OriginalId ?? asset.Name;
    }

    [RelayCommand]
    private async Task RelateSelectedAsync()
    {
        var ids = SelectedIds();
        if (ids.Count < 2 || Lib is not { } lib)
        {
            Services.Dialogs.Show(L.T("AssetLibrary.SelectTwoOrMoreAssetsCtrl"), MessageKind.Info, L.T("AssetLibrary.RelatedAssets"));
            return;
        }
        Try(L.T("AssetLibrary.RelatingTheAssets"), () =>
        {
            foreach (long a in ids) foreach (long b in ids) lib.Relate(a, b);
        });
        Info(L.F("AssetLibrary.0MarkedAsRelatedToEach", L.Count(ids.Count, "Noun.asset")));
        await ShowDetailsAsync(SelectedTile);
    }

    [RelayCommand]
    private async Task RemoveFromLibraryAsync()
    {
        var ids = SelectedIds();
        if (ids.Count == 0 || Lib is not { } lib) return;
        if (!Services.Dialogs.Confirm(L.F("AssetLibrary.Remove0FromTheLibraryThe", L.Count(ids.Count, "Noun.asset")), L.T("AssetLibrary.RemoveFromTheLibrary"), L.T("Common.Remove"), L.T("Common.Cancel"))) return;
        int n = 0;
        if (!Try(L.T("AssetLibrary.RemovingTheAssets"), () => n = lib.Remove(ids))) return;
        Info(L.F("AssetLibrary.0RemovedFromTheLibraryNo", L.Count(n, "Noun.asset")));
        await RefreshAsync();
    }

    // ------------------------------------------------------------------ adding

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        string? folder = Services.Dialogs.PickFolder(L.T("AssetLibrary.AddAFolderToTheAsset"));
        if (folder == null || Lib is not { } lib) return;
        if (PathGuard.IsSameOrInside(lib.Folder, folder) || PathGuard.IsSameOrInside(folder, lib.Folder))
        {
            Services.Dialogs.Show(L.T("AssetLibrary.ThisFolderContainsOrIsInside"), MessageKind.Warning, L.T("AssetLibrary.AddFolder"));
            return;
        }
        var options = new IndexOptions { Source = "Folder " + folder, ClientVersion = null };
        Services.Library.Queue.Enqueue(L.F("AssetLibrary.Indexing0", Path.GetFileName(folder.TrimEnd('\\'))), "Indexing", (p, ct) => Task.Run(() =>
        {
            var r = AssetIndexer.IndexFolder(lib, folder, options, p, ct);
            Services.Library.RaiseLibraryChanged();
            return r.Summary;
        }, ct));
        Info(L.F("AssetLibrary.Indexing0InTheBackgroundContent", folder));
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        string[]? files = Services.Dialogs.OpenFiles(L.T("AssetLibrary.AddFilesToTheAssetLibrary"),
            L.T("AssetLibrary.ImagesAnimationsAndSoundsPngGif"));
        if (files == null || files.Length == 0 || Lib is not { } lib) return;
        bool copy = Services.Dialogs.Choose(L.F("AssetLibrary.Add0ToTheLibraryKeep", L.Count(files.Length, "Noun.file")), L.T("AssetLibrary.AddFiles"), L.T("AssetLibrary.KeepWhereTheyAre"), L.T("AssetLibrary.CopyIntoTheLibrary")) switch
        {
            0 => false,
            1 => true,
            _ => (bool?)null,
        } ?? false;
        if (!await RunAsync(L.T("AssetLibrary.AddingFilesToTheLibrary"), async (p, ct) =>
            {
                var r = await Task.Run(() => AssetIndexer.IndexFiles(lib, files, new IndexOptions { Source = L.T("AssetLibrary.AddedByHand"), CopyIntoLibrary = copy }, null, p, ct: ct), ct);
                Services.Status.Show(L.T("AssetLibrary.AssetLibrary") + r.Summary, r.Problems.Count > 0 ? StatusKind.Warning : StatusKind.Success);
            }, onSuccess: () => { })) return;
        await RefreshAsync();
    }

    [RelayCommand]
    private void RescanAll()
    {
        if (Lib is not { } lib) return;
        var roots = lib.Roots();
        if (roots.Count == 0)
        {
            Services.Dialogs.Show(L.T("AssetLibrary.NoFolderIsIndexedYetAdd"), MessageKind.Info, L.T("AssetLibrary.Rescan"));
            return;
        }
        foreach (var root in roots)
        {
            var r0 = root;
            Services.Library.Queue.Enqueue(L.F("AssetLibrary.Rescanning0", Path.GetFileName(r0.Path)), "Indexing", (p, ct) => Task.Run(() =>
            {
                var r = AssetIndexer.IndexRoot(lib, r0, null, p, ct);
                Services.Library.RaiseLibraryChanged();
                return r.Summary;
            }, ct));
        }
        Info(L.F("AssetLibrary.Rescanning0InTheBackgroundOnly", L.Count(roots.Count, "Noun.folder")));
    }

    // ------------------------------------------------------------------ categories and collections

    [RelayCommand]
    private async Task NewCategoryAsync()
    {
        string? name = Services.Dialogs.Prompt(L.T("AssetLibrary.NewCategory"), L.T("AssetLibrary.NameOfTheCategoryForExample"));
        if (string.IsNullOrWhiteSpace(name) || Lib is not { } lib) return;
        if (!Try(L.T("AssetLibrary.CreatingTheCategory"), () => lib.AddCategory(name))) return;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RenameCategoryAsync()
    {
        if (SelectedCategory is not { IsCustom: true } c || Lib is not { } lib)
        {
            Services.Dialogs.Show(L.T("AssetLibrary.OnlyCategoriesYouCreatedCanBe"), MessageKind.Info, L.T("AssetLibrary.RenameCategory"));
            return;
        }
        string? name = Services.Dialogs.Prompt(L.T("AssetLibrary.RenameCategory"), L.T("AssetLibrary.NewName"), c.Name);
        if (string.IsNullOrWhiteSpace(name) || !Try(L.T("AssetLibrary.RenamingTheCategory"), () => lib.RenameCategory(c.Name, name))) return;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync()
    {
        if (SelectedCategory is not { IsCustom: true } c || Lib is not { } lib)
        {
            Services.Dialogs.Show(L.T("AssetLibrary.OnlyCategoriesYouCreatedCanBe2"), MessageKind.Info, L.T("AssetLibrary.DeleteCategory"));
            return;
        }
        if (!Services.Dialogs.Confirm(L.F("AssetLibrary.DeleteTheCategory0Its1", c.Name, L.Count(c.Count, "Noun.asset"), AssetCategories.Other), L.T("AssetLibrary.DeleteCategory"), L.T("AssetLibrary.Delete"), L.T("Common.Cancel"))) return;
        if (!Try(L.T("AssetLibrary.DeletingTheCategory"), () => lib.DeleteCategory(c.Name))) return;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task NewCollectionAsync()
    {
        string? name = Services.Dialogs.Prompt(L.T("AssetLibrary.NewCollection"), L.T("AssetLibrary.NameOfTheCollectionForExample"));
        if (string.IsNullOrWhiteSpace(name) || Lib is not { } lib) return;
        long id = 0;
        if (!Try(L.T("AssetLibrary.CreatingTheCollection"), () => id = lib.CreateCollection(name))) return;
        var ids = SelectedIds();
        if (ids.Count > 0 && Services.Dialogs.Confirm(L.F("AssetLibrary.AddThe0To1", L.Count(ids.Count, "Noun.selected_asset"), name), L.T("AssetLibrary.NewCollection"), L.T("AssetLibrary.AddThem"), L.T("AssetLibrary.LeaveItEmpty")))
            lib.AddToCollection(id, ids);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task AddToCollectionAsync()
    {
        var ids = SelectedIds();
        if (ids.Count == 0 || Lib is not { } lib) return;
        var collections = lib.Collections();
        var items = collections.Select(c => new ChoiceItem(c.Name, $"{L.Count(c.Count, "Noun.asset")}{(c.Description != null ? " · " + c.Description : "")}")).ToList();
        var answer = Services.Dialogs.ChooseFrom(L.T("AssetLibrary.AddToACollection"), L.F("AssetLibrary.AddThe0ToWhichCollection", L.Count(ids.Count, "Noun.selected_asset")), items,
            new ChoiceOptions { OkText = L.T("Common.Add"), ExtraButtons = [("new", L.T("AssetLibrary.NewCollection2"))] });
        if (answer == null) return;
        long target;
        if (answer.Button == "new")
        {
            string? name = Services.Dialogs.Prompt(L.T("AssetLibrary.NewCollection"), L.T("AssetLibrary.NameOfTheCollection"));
            if (string.IsNullOrWhiteSpace(name)) return;
            target = 0;
            if (!Try(L.T("AssetLibrary.CreatingTheCollection"), () => target = lib.CreateCollection(name))) return;
        }
        else if (answer.Index >= 0 && answer.Index < collections.Count) target = collections[answer.Index].Id;
        else return;
        int added = 0;
        if (!Try(L.T("AssetLibrary.AddingToTheCollection"), () => added = lib.AddToCollection(target, ids))) return;
        Info(L.F("AssetLibrary.0AddedTo1", L.Count(added, "Noun.asset"), lib.Collection(target)?.Name) + (added < ids.Count ? L.F("AssetLibrary.0N0WereAlreadyInIt", ids.Count - added) : "."));
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RemoveFromCollectionAsync()
    {
        var ids = SelectedIds();
        if (SelectedCollection?.CollectionId is not { } col || ids.Count == 0 || Lib is not { } lib) return;
        if (!Try(L.T("AssetLibrary.RemovingFromTheCollection"), () => lib.RemoveFromCollection(col, ids))) return;
        Info(L.F("AssetLibrary.0RemovedFrom1TheyStay", L.Count(ids.Count, "Noun.asset"), SelectedCollection.Name));
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RenameCollectionAsync()
    {
        if (SelectedCollection is not { CollectionId: { } id } c || Lib is not { } lib) return;
        string? name = Services.Dialogs.Prompt(L.T("AssetLibrary.RenameCollection"), L.T("AssetLibrary.NewName"), c.Name);
        if (string.IsNullOrWhiteSpace(name) || !Try(L.T("AssetLibrary.RenamingTheCollection"), () => lib.RenameCollection(id, name))) return;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DescribeCollectionAsync()
    {
        if (SelectedCollection is not { CollectionId: { } id } || Lib is not { } lib) return;
        var col = lib.Collection(id);
        if (col == null) return;
        string? text = Services.Dialogs.Prompt(L.T("AssetLibrary.CollectionDescription"), L.F("AssetLibrary.DescriptionOf0ShownInGalleries", col.Name), col.Description ?? "", multiline: true);
        if (text == null) return;
        if (!Try(L.T("AssetLibrary.SavingTheDescription"), () => lib.DescribeCollection(id, text, SelectedTile?.Id ?? col.ThumbnailAssetId))) return;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DuplicateCollectionAsync()
    {
        if (SelectedCollection is not { CollectionId: { } id } c || Lib is not { } lib) return;
        string? name = Services.Dialogs.Prompt(L.T("AssetLibrary.DuplicateCollection"), L.T("AssetLibrary.NameOfTheCopy"), c.Name + L.T("AssetLibrary.Copy"));
        if (string.IsNullOrWhiteSpace(name) || !Try(L.T("AssetLibrary.DuplicatingTheCollection"), () => lib.DuplicateCollection(id, name))) return;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteCollectionAsync()
    {
        if (SelectedCollection is not { CollectionId: { } id } c || Lib is not { } lib) return;
        if (!Services.Dialogs.Confirm(L.F("AssetLibrary.DeleteTheCollection0Its1", c.Name, L.Count(c.Count, "Noun.asset")), L.T("AssetLibrary.DeleteCollection"), L.T("AssetLibrary.Delete"), L.T("Common.Cancel"))) return;
        if (!Try(L.T("AssetLibrary.DeletingTheCollection"), () => lib.DeleteCollection(id))) return;
        SelectedCollection = null;
        await RefreshAsync();
    }

    [RelayCommand]
    private void ExportCollection()
    {
        if (SelectedCollection is not { CollectionId: { } id } c || Lib is not { } lib) return;
        string? file = Services.Dialogs.SaveFile(L.T("AssetLibrary.ExportTheCollection"), L.T("AssetLibrary.TibiaFoundryCollectionTfcollectionJsonTfcollecti"), FileNames.Sanitize(c.Name) + ".tfcollection.json");
        if (file == null) return;
        if (Try(L.T("AssetLibrary.ExportingTheCollection"), () => lib.ExportCollection(id, file))) Saved(file, L.T("AssetLibrary.NamesHashesAndMetadataTheFiles"));
    }

    [RelayCommand]
    private async Task ImportCollectionAsync()
    {
        string? file = Services.Dialogs.OpenFile(L.T("AssetLibrary.ImportACollection"), L.T("AssetLibrary.TibiaFoundryCollectionTfcollectionJsonJson"));
        if (file == null || Lib is not { } lib) return;
        (long CollectionId, string Name, int Matched, IReadOnlyList<string> NotFound) result = (0, "", 0, []);
        if (!Try(L.T("AssetLibrary.ImportingTheCollection"), () => result = lib.ImportCollection(file))) return;
        Services.Dialogs.ShowReport(new DialogReport
        {
            Title = L.T("AssetLibrary.ImportACollection"), Heading = L.F("AssetLibrary.Collection0Imported", result.Name),
            Summary = L.F("AssetLibrary.0OfThisLibraryMatchedBy", L.Count(result.Matched, "Noun.asset")),
            Kind = result.NotFound.Count > 0 ? MessageKind.Warning : MessageKind.Info,
            Sections = result.NotFound.Count == 0 ? [] :
            [
                new ReportSection(L.F("AssetLibrary.NotInThisLibrary0N0", result.NotFound.Count))
                {
                    Text = L.T("AssetLibrary.AddTheFoldersWithTheseFiles"), Bullets = result.NotFound.Take(200).ToList(), Collapsed = result.NotFound.Count > 10,
                },
            ],
        });
        await RefreshAsync();
    }

    // ------------------------------------------------------------------ duplicates

    [ObservableProperty] private string _duplicateSummary = L.T("AssetLibrary.FindDuplicatesComparesEveryAsset");

    [RelayCommand]
    private async Task FindDuplicatesAsync()
    {
        if (Lib is not { } lib) return;
        Tab = 1;
        DuplicateReport? report = null;
        int threshold = Services.Settings.Library.SimilarityThreshold;
        if (!await RunAsync(L.T("AssetLibrary.FindingDuplicates"), async (p, ct) => report = await Task.Run(() => DuplicateFinder.Find(lib, threshold, true, p, ct), ct), onSuccess: () => { })) return;
        Duplicates.Clear();
        foreach (var g in report!.Groups.Take(1000)) Duplicates.Add(new DuplicateRow(g));
        DuplicateSummary = report.Summary + (report.WastedBytes > 0 ? L.F("AssetLibrary.IdenticalCopiesTake0ThatOne", ReportWriter.FormatSize(report.WastedBytes)) : "") +
                           L.T("AssetLibrary.ReviewEachGroupOnlyYouDecide");
        Info(report.Summary);
    }

    [RelayCommand]
    private void RevealAsset(AssetRecord? asset)
    {
        if (asset != null) Services.RevealSaved(new StatusLink(asset.Path, true));
    }

    [RelayCommand]
    private void ShowAssetInBrowser(AssetRecord? asset)
    {
        if (asset == null) return;
        Tab = 0;
        SelectedCategory = CategoryList.FirstOrDefault();
        SearchText = Path.GetFileNameWithoutExtension(asset.Path);
    }

    // ------------------------------------------------------------------ folders, storage and safety

    private void LoadRoots()
    {
        Roots.Clear();
        if (Lib is { } lib) foreach (var r in lib.Roots()) Roots.Add(r);
    }

    [RelayCommand]
    private void RescanRoot(LibraryRoot? root)
    {
        if (root == null || Lib is not { } lib) return;
        Services.Library.Queue.Enqueue(L.F("AssetLibrary.Rescanning0", Path.GetFileName(root.Path)), "Indexing", (p, ct) => Task.Run(() =>
        {
            var r = AssetIndexer.IndexRoot(lib, root, null, p, ct);
            Services.Library.RaiseLibraryChanged();
            return r.Summary;
        }, ct));
        Info(L.F("AssetLibrary.Rescanning0InTheBackground", root.Path));
    }

    [RelayCommand]
    private void RemoveRoot(LibraryRoot? root)
    {
        if (root == null || Lib is not { } lib) return;
        if (!Services.Dialogs.Confirm(L.F("AssetLibrary.StopIndexing0ItsAssetsStay", root.Path), L.T("AssetLibrary.IndexedFolders"), L.T("AssetLibrary.StopIndexing2"), L.T("Common.Cancel"))) return;
        if (Try(L.T("AssetLibrary.RemovingTheFolder"), () => lib.RemoveRoot(root.Id))) LoadRoots();
    }

    public string LibraryFolder => Services.Library.Folder;

    public bool AddExportsAutomatically
    {
        get => Services.Settings.Library.AddExportsAutomatically;
        set
        {
            Services.Settings.Library.AddExportsAutomatically = value;
            Services.SaveSettings();
            OnPropertyChanged();
        }
    }

    public string AutoExportCollection
    {
        get => Services.Settings.Library.ExportCollection ?? "";
        set
        {
            Services.Settings.Library.ExportCollection = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            Services.SaveSettings();
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private string _storageReport = "";

    [RelayCommand]
    private async Task VerifyLibraryAsync()
    {
        if (Lib is not { } lib) return;
        string check = "";
        int missing = 0, changed = 0, total = 0;
        var problems = new List<string>();
        if (!await RunAsync(L.T("AssetLibrary.VerifyingTheLibrary"), async (p, ct) => await Task.Run(() =>
            {
                check = lib.CheckDatabase();
                for (int offset = 0; ; offset += 2000)
                {
                    var page = lib.Search(new AssetQuery { Offset = offset, Limit = 2000, Sort = AssetSort.Id });
                    foreach (var a in page.Assets)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (++total % 50 == 0) p.Report(new OperationProgress(total, page.Total, a.Name));
                        if (!File.Exists(a.Path)) { missing++; if (problems.Count < 200) problems.Add("Missing: " + a.Path); continue; }
                        if (a.Sha256 == null) continue;
                        string sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(a.Path)));
                        if (!string.Equals(sha, a.Sha256, StringComparison.OrdinalIgnoreCase)) { changed++; if (problems.Count < 200) problems.Add(L.T("AssetLibrary.ChangedSinceItWasIndexed") + a.Path); }
                    }
                    if (page.Assets.Count < 2000) break;
                }
            }, ct), onSuccess: () => { })) return;
        bool ok = check == "ok" && missing == 0 && changed == 0;
        StorageReport = L.F("AssetLibrary.Index01Checked2N0", (check == "ok" ? "consistent" : check), L.Count(total, "Noun.file"), missing, changed);
        Services.Status.Show(L.T("AssetLibrary.LibraryVerified") + StorageReport, ok ? StatusKind.Success : StatusKind.Warning);
        Services.Dialogs.ShowReport(new DialogReport
        {
            Title = L.T("AssetLibrary.VerifyTheLibrary"), Heading = ok ? L.T("AssetLibrary.EverythingMatches") : L.T("AssetLibrary.SomeFilesDifferFromTheIndex"),
            Summary = StorageReport + (changed > 0 ? L.T("AssetLibrary.RescanTheFoldersToRecordThe") : ""),
            Kind = ok ? MessageKind.Info : MessageKind.Warning,
            Sections = problems.Count == 0 ? [] : [new ReportSection(L.T("Common.Files")) { Bullets = problems, Collapsed = problems.Count > 8 }],
        });
    }

    [RelayCommand]
    private void BackupLibrary()
    {
        if (Lib is not { } lib) return;
        string? file = Services.Dialogs.SaveFile(L.T("AssetLibrary.BackUpTheAssetLibrary"), L.T("AssetLibrary.ZIPArchiveZipZip"), $"TibiaFoundry-Library-{DateTime.Now:yyyy-MM-dd}.zip",
            Services.Outputs.FolderFor(OutputKind.Backups));
        if (file == null) return;
        _ = RunAsync(L.T("AssetLibrary.BackingUpTheLibrary"), (_, _) => Task.Run(() => lib.Backup(file), CancellationToken.None), onSuccess: () => Saved(file, L.T("AssetLibrary.IndexCollectionsProfilesProjectsAndWorkflows")));
    }

    [RelayCommand]
    private async Task RestoreLibraryAsync()
    {
        string? file = Services.Dialogs.OpenFile(L.T("AssetLibrary.RestoreTheAssetLibraryFromA"), L.T("AssetLibrary.ZIPArchiveZipZip"), Services.Outputs.FolderFor(OutputKind.Backups));
        if (file == null) return;
        if (!Services.Dialogs.Confirm(L.F("AssetLibrary.ReplaceTheLibrarySIndexCollections", file), L.T("AssetLibrary.RestoreTheLibrary"), L.T("AssetLibrary.Restore2"), L.T("Common.Cancel"))) return;
        string folder = Services.Library.Folder;
        if (!await RunAsync(L.T("AssetLibrary.RestoringTheLibrary"), (_, _) => Task.Run(() =>
            {
                AssetLibrary.ReleaseConnections();
                AssetLibrary.Restore(folder, file);
            }, CancellationToken.None), L.T("AssetLibrary.TheLibraryWasRestoredFromThe"))) return;
        Services.Library.Reopen();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ChangeLibraryFolderAsync()
    {
        string? folder = Services.Dialogs.PickFolder(L.T("AssetLibrary.FolderForTheAssetLibraryAn"), Services.Library.Folder);
        if (folder == null) return;
        if (!Services.Dialogs.Confirm(L.F("AssetLibrary.Use0ForTheAssetLibrary", folder, Services.Library.Folder), L.T("AssetLibrary.LibraryFolder"), L.T("AssetLibrary.UseThisFolder"), L.T("Common.Cancel"))) return;
        try
        {
            AssetLibrary.Open(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            Services.Dialogs.Show(L.F("AssetLibrary.TheLibraryCannotBeUsedIn", folder, ex.Message), MessageKind.Warning, L.T("AssetLibrary.LibraryFolder"));
            return;
        }
        Services.Settings.Library.Folder = string.Equals(folder, Services.Library.DefaultFolder, StringComparison.OrdinalIgnoreCase) ? null : folder;
        Services.SaveSettings();
        Services.Library.Reopen();
        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenLibraryFolder() => Services.Shell.OpenFolder(Services.Library.Folder);

    [RelayCommand]
    private void ClearThumbnails()
    {
        if (Lib is not { } lib) return;
        int n = ThumbnailCache.Clear(lib);
        Info(L.F("AssetLibrary.0DeletedFromTheCacheThey", L.Count(n, "Noun.thumbnail")));
    }

    // ------------------------------------------------------------------ content production

    /// <summary>Opens Content Studio with a new project that exports the selected assets (or the current collection).</summary>
    [RelayCommand]
    private void CreateProject()
    {
        var studio = _main.Pages.OfType<ContentStudioViewModel>().FirstOrDefault();
        if (studio == null) return;
        var ids = SelectedIds();
        string? collection = SelectedCollection?.Name;
        if (ids.Count <= 1 && collection == null)
        {
            Services.Dialogs.Show(L.T("AssetLibrary.SelectSeveralAssetsCtrlClickOr"), MessageKind.Info, L.T("AssetLibrary.CreateAProject2"));
            return;
        }
        studio.CreateFromLibrary(collection, ids.Count > 1 ? ids : []);
        _main.SelectedPage = studio;
    }

    public override string? UnsavedChanges => null;
}
