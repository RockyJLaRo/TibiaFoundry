using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Compatibility;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.Views;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Export;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

public enum SearchMode { GoToId, NameContains, UsesSprite }

public enum PreviewBackground { Checkerboard, Magenta, Black, White, Gray }

/// <summary>
/// The Appearances viewer (original "Appearances Viewer" tab): browse objects, outfits,
/// effects and missiles, inspect their data, filter, export GIFs/sheets/JSON and use the
/// outfitter and NPC tools.
/// </summary>
public sealed partial class AppearancesViewModel : PageViewModel
{
    private readonly Dictionary<AppearanceKind, List<AppearanceItem>> _all = new();
    private readonly Dictionary<AppearanceKind, List<AppearanceItem>> _current = new();
    private readonly Dictionary<AppearanceKind, List<string>> _filterLog = new();
    private AppearanceDatabase? _loadedDb;

    public AppearancesViewModel(AppServices services) : base(services, BuiltInTools.Appearances)
    {
        // Another client, file or folder in use: the next load uses the new default file.
        services.Data.ClientFilesChanged += types => { if (types == null || types.Contains(AssetTypes.Appearances)) DatPath = null; };
        Filters = new AppearanceFilterViewModel(this, services);
        Export = new AppearanceExportViewModel(this, services);
        Outfitter = new OutfitterViewModel(this, services);
        Infobox = new InfoboxViewModel(this, services);
        _showDefaultValues = services.Settings.ShowDefaultValues;
        services.Data.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ClientDataService.Appearances)) OnDatabaseChanged();
            if (e.PropertyName == nameof(ClientDataService.Sprites)) RefreshThumbnails();
        };
        foreach (AppearanceKind k in Enum.GetValues<AppearanceKind>())
        {
            _all[k] = new List<AppearanceItem>();
            _current[k] = new List<AppearanceItem>();
            _filterLog[k] = new List<string>();
        }
    }

    public override IReadOnlyList<string> DataComponents { get; } = [CompatibilityChecker.SpriteCatalogComponent, TibiaFormat.CatalogTypeAppearances];


    public AppearanceFilterViewModel Filters { get; }
    public AppearanceExportViewModel Export { get; }
    public OutfitterViewModel Outfitter { get; }
    public InfoboxViewModel Infobox { get; }

    public AppearanceDatabase? Database => _loadedDb;

    /// <summary>Lets the sub view-models (export, outfitter) run operations with this page's progress handling.</summary>
    public Task<bool> RunExternalAsync(string title, Func<IProgress<Core.Common.OperationProgress>, CancellationToken, Task> work, string? successMessage = null,
        string? operation = null, Action? onSuccess = null, string? output = null) =>
        RunAsync(title, work, successMessage, operation: operation, onSuccess: onSuccess, output: output);

    // ------------------------------------------------------------------ files

    [ObservableProperty] private string? _datPath;
    public string? SpritesFolder => Services.Settings.ActiveSpritesFolder;
    public string SpriteSourceDescription => Services.Settings.SpriteSource switch
    {
        Core.Sprites.SpriteSourceMode.ClientOnly => L.T("Appearances.SpritesAreDecodedFromTheClient"),
        Core.Sprites.SpriteSourceMode.FolderOnly => L.T("Appearances.SpritesAreReadOnlyFromThe"),
        _ => string.IsNullOrEmpty(SpritesFolder) ? L.T("Appearances.NoSpritesFolderIsSetSo")
                                                 : L.T("Appearances.SpritesAreReadFromTheSprites"),
    };

    public bool IsLoaded => _loadedDb != null;

    public override async Task OnNavigatedToAsync()
    {
        Export.RefreshFolder();
        DatPath ??= Services.Data.DefaultFile(AssetTypes.Appearances);
        if (Services.Data.Appearances != null && _loadedDb != Services.Data.Appearances) OnDatabaseChanged();
        else if (Services.Data.Appearances == null && Services.Settings.LoadDataAutomatically && DatPath != null && !IsBusy) await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        DatPath ??= Services.Data.DefaultFile(AssetTypes.Appearances);
        if (DatPath == null || !File.Exists(DatPath))
        {
            Services.Dialogs.Show(L.T("Appearances.NoAppearancesFileWasFoundFor"), MessageKind.Warning, L.T("Appearances.AppearancesFileNotFound"));
            return;
        }
        string path = DatPath;
        await RunAsync(L.T("Appearances.LoadingAppearances"), async (_, _) => await Services.Data.LoadAppearancesAsync(path), L.F("Appearances.Loaded0", Path.GetFileName(path)), operation: "load");
    }

    [RelayCommand]
    private async Task ChangeFileAsync()
    {
        string? file = Services.Dialogs.OpenFile(L.T("Appearances.SelectAnAppearancesFile"), L.T("Appearances.TibiaAppearancesDatDatAllFiles"), Services.Data.AssetsDirectory);
        if (file == null) return;
        await OfferAsDefaultAsync(AssetTypes.Appearances, file);
        DatPath = file;
        await LoadAsync();
    }

    [RelayCommand]
    private void ChangeSpritesFolder()
    {
        string? folder = Services.Dialogs.PickFolder(L.T("Appearances.SelectTheExtractedSpritesFolder"), SpritesFolder ?? Services.Outputs.FolderFor(Core.FileOps.OutputKind.SpriteExports));
        if (folder == null) return;
        if (Services.Settings.UseTestServer) Services.Settings.SpritesFolderTestServer = folder;
        else Services.Settings.SpritesFolder = folder;
        Services.SaveSettings();
        Services.Data.RebuildSprites();
        OnPropertyChanged(nameof(SpritesFolder));
        OnPropertyChanged(nameof(SpriteSourceDescription));
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        var db = _loadedDb;
        if (db == null) { Services.Dialogs.Show(L.T("Appearances.LoadAnAppearancesFileFirst")); return; }
        string? file = Services.Outputs.SaveFile(Core.FileOps.OutputKind.DataExports, L.T("Appearances.ExportAppearancesAsJSON"), L.T("Appearances.JSONFilesJsonJson"), "Appearances.json");
        if (file == null) return;
        await RunAsync(L.T("Appearances.ExportingJSON"), (_, ct) => JsonExport.WriteAsync(db.Data, file, ct), operation: "export-json", output: file,
            onSuccess: () => Saved(file, ". " + Attribution.NoMetadataNote));
    }

    private void OnDatabaseChanged()
    {
        var db = Services.Data.Appearances;
        _loadedDb = db;
        foreach (AppearanceKind k in Enum.GetValues<AppearanceKind>())
        {
            _all[k] = db == null ? new List<AppearanceItem>() : db.List(k).Select(a => new AppearanceItem(a, k, Services.Data.Thumbnails)).ToList();
            _current[k] = _all[k];
            _filterLog[k].Clear();
        }
        if (db?.SourcePath != null) DatPath = db.SourcePath;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(Database));
        OnPropertyChanged(nameof(ObjectCount));
        OnPropertyChanged(nameof(OutfitCount));
        OnPropertyChanged(nameof(EffectCount));
        OnPropertyChanged(nameof(MissileCount));
        Export.OnDatabaseChanged();
        RefreshList();
    }

    private void RefreshThumbnails()
    {
        foreach (var list in _all.Values) foreach (var item in list) item.ResetThumbnail();
        OnPropertyChanged(nameof(SpritesFolder));
        OnPropertyChanged(nameof(SpriteSourceDescription));
        UpdateDetails();
    }

    // ------------------------------------------------------------------ list

    [ObservableProperty] private AppearanceKind _kind = AppearanceKind.Object;
    [ObservableProperty] private IReadOnlyList<AppearanceItem> _visibleItems = Array.Empty<AppearanceItem>();
    [ObservableProperty] private AppearanceItem? _selectedItem;
    [ObservableProperty] private IReadOnlyList<AppearanceItem> _selectedItems = Array.Empty<AppearanceItem>();

    public int ObjectCount => _all[AppearanceKind.Object].Count;
    public int OutfitCount => _all[AppearanceKind.Outfit].Count;
    public int EffectCount => _all[AppearanceKind.Effect].Count;
    public int MissileCount => _all[AppearanceKind.Missile].Count;
    public int TotalCount => _all[Kind].Count;
    public int VisibleCount => VisibleItems.Count;
    public bool IsFiltered => _current[Kind].Count != _all[Kind].Count || _filterLog[Kind].Count > 0;
    public string FilterSummary => _filterLog[Kind].Count == 0 ? "" : string.Join(" → ", _filterLog[Kind]);

    partial void OnKindChanged(AppearanceKind value)
    {
        RefreshList();
        Outfitter.OnSelectionChanged();
    }

    partial void OnSelectedItemChanged(AppearanceItem? value)
    {
        UpdateDetails();
        Outfitter.OnSelectionChanged();
        Export.OnSelectionChanged();
        if (SelectedDetailTab == InfoboxTab) Infobox.OnSelectionChanged();
    }

    /// <summary>Index of the "Wiki infobox" tab; the infobox is built only while it is shown.</summary>
    public const int InfoboxTab = 5;

    partial void OnSelectedDetailTabChanged(int value)
    {
        if (value == InfoboxTab) Infobox.OnSelectionChanged();
    }

    partial void OnSelectedItemsChanged(IReadOnlyList<AppearanceItem> value) => OnPropertyChanged(nameof(SelectedCount));

    public int SelectedCount => Math.Max(SelectedItems.Count, SelectedItem == null ? 0 : 1);

    private void RefreshList(AppearanceItem? select = null)
    {
        var previous = select ?? SelectedItem;
        VisibleItems = _current[Kind];
        SelectedItem = previous != null && previous.Kind == Kind && VisibleItems.Contains(previous) ? previous : (VisibleItems.Count > 0 ? VisibleItems[0] : null);
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(FilterSummary));
    }

    /// <summary>Items the actions apply to: the multi-selection, or the current item.</summary>
    public IReadOnlyList<AppearanceItem> TargetItems => SelectedItems.Count > 0 ? SelectedItems : SelectedItem == null ? Array.Empty<AppearanceItem>() : new[] { SelectedItem };

    /// <summary>Narrows the current list (filters are cumulative, as in the original).</summary>
    public async Task<bool> ApplyCriteriaAsync(IReadOnlyList<AppearanceCriterion> criteria, FilterMode mode, string description)
    {
        var source = _current[Kind];
        var kind = Kind;
        var result = await Task.Run(() => source.Where(i => AppearanceFilter.Evaluate(i.Appearance, criteria, mode)).ToList());
        if (result.Count == 0)
        {
            Services.Dialogs.Show(L.T("Appearances.NoAppearancesMatchTheseCriteriaSo"), MessageKind.Info, L.T("Appearances.NoResults"));
            return false;
        }
        _current[kind] = result;
        _filterLog[kind].Add(description);
        RefreshList();
        Info(L.F("Appearances.0N0Of1N02", result.Count, _all[kind].Count, kind.ToString().ToLowerInvariant()));
        return true;
    }

    [RelayCommand]
    public void ClearFilters()
    {
        _current[Kind] = _all[Kind];
        _filterLog[Kind].Clear();
        RefreshList();
    }

    /// <summary>"Keep Selected": narrows the list to the selected rows (the original button was never implemented).</summary>
    [RelayCommand]
    private async Task KeepSelectedAsync()
    {
        var ids = TargetItems.Select(i => i.Id).ToHashSet();
        if (ids.Count == 0) return;
        await ApplyCriteriaAsync([AppearanceFilter.IdIn(ids, L.F("Appearances.Selected0", ids.Count))], FilterMode.IncludeAll, L.F("Appearances.Selected0", ids.Count));
    }

    // ------------------------------------------------------------------ search

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private SearchMode _searchMode = SearchMode.GoToId;

    public string SearchHint => SearchMode switch
    {
        SearchMode.NameContains => L.T("Appearances.NameContainsEnterToFilter"),
        SearchMode.UsesSprite => L.T("Appearances.SpriteIDEnterToFilter"),
        _ => L.T("Appearances.IDsEG3031Or3031"),
    };

    partial void OnSearchModeChanged(SearchMode value) => OnPropertyChanged(nameof(SearchHint));

    /// <summary>Raised to ask the view to select and scroll to items.</summary>
    public event Action<IReadOnlyList<AppearanceItem>>? SelectRequested;

    [RelayCommand]
    private async Task SearchAsync()
    {
        string text = SearchText.Trim();
        if (!IsLoaded || text.Length == 0) return;
        switch (SearchMode)
        {
            case SearchMode.GoToId:
                if (!AppearanceFilter.TryParseIdList(text, out var ids, out var bad))
                {
                    Services.Dialogs.Show(L.F("Appearances.0IsNotAValidID", bad), MessageKind.Warning, L.T("Common.Search"));
                    return;
                }
                var set = ids.ToHashSet();
                var found = _current[Kind].Where(i => set.Contains(i.Id)).ToList();
                if (found.Count == 0) { Services.Status.Show(L.F("Appearances.No0WithThatIDIn", Kind.ToString().ToLowerInvariant()), StatusKind.Warning); return; }
                SelectRequested?.Invoke(found);
                break;
            case SearchMode.NameContains:
                if (text.Length < 2) { Services.Dialogs.Show(L.T("Appearances.TypeAtLeast2CharactersTo"), MessageKind.Info, L.T("Common.Search")); return; }
                await ApplyCriteriaAsync([AppearanceFilter.NameContains(text)], FilterMode.IncludeAll, L.F("Appearances.NameContains0", text));
                break;
            case SearchMode.UsesSprite:
                if (!uint.TryParse(text, out uint sprite)) { Services.Dialogs.Show(L.T("Appearances.TypeASpriteIDDigitsOnly"), MessageKind.Info, L.T("Common.Search")); return; }
                await ApplyCriteriaAsync([AppearanceFilter.UsesSprite(sprite)], FilterMode.IncludeAll, L.F("Appearances.UsesSprite0", sprite));
                break;
        }
    }

    /// <summary>Selects an appearance (used by Compare, Static Data and other pages).</summary>
    public async Task RevealAsync(AppearanceKind kind, uint id)
    {
        if (!IsLoaded) await LoadAsync();
        if (!IsLoaded) return;
        Kind = kind;
        var item = _current[kind].FirstOrDefault(i => i.Id == id) ?? _all[kind].FirstOrDefault(i => i.Id == id);
        if (item == null) { Services.Status.Show(L.F("Appearances.01NotFound", kind, id), StatusKind.Warning); return; }
        if (!_current[kind].Contains(item)) ClearFilters();
        SelectRequested?.Invoke(new[] { item });
    }

    public async Task OpenInOutfitterAsync(uint outfitId, int[] colors, uint addons)
    {
        await RevealAsync(AppearanceKind.Outfit, outfitId);
        Outfitter.ApplyPreset(colors, addons);
        SelectedDetailTab = 4;
    }

    // ------------------------------------------------------------------ details

    [ObservableProperty] private int _selectedDetailTab;
    [ObservableProperty] private string _detailName = "";
    [ObservableProperty] private string _detailId = "";
    [ObservableProperty] private string _spriteIdsText = "";
    [ObservableProperty] private IReadOnlyList<PropertyRowViewModel> _properties = Array.Empty<PropertyRowViewModel>();
    [ObservableProperty] private IReadOnlyList<BitmapSource> _previewFrames = Array.Empty<BitmapSource>();
    [ObservableProperty] private IReadOnlyList<int> _previewDelays = Array.Empty<int>();
    [ObservableProperty] private bool _animate = true;
    [ObservableProperty] private bool _forceDelay;
    [ObservableProperty] private int _forcedDelayMs = 100;

    /// <summary>Delay passed to the preview: the forced delay when enabled, otherwise 0 (use the sprite's own delays).</summary>
    public int EffectiveDelay => ForceDelay ? ForcedDelayMs : 0;

    partial void OnForceDelayChanged(bool value) => OnPropertyChanged(nameof(EffectiveDelay));

    partial void OnForcedDelayMsChanged(int value) => OnPropertyChanged(nameof(EffectiveDelay));
    [ObservableProperty] private PreviewBackground _previewBackground = PreviewBackground.Checkerboard;
    [ObservableProperty] private string _previewMessage = "";
    [ObservableProperty] private bool _showDefaultValues;
    [ObservableProperty] private string _propertyFilter = "";

    public bool IsPreviewAnimated => PreviewFrames.Count > 1;

    partial void OnPreviewFramesChanged(IReadOnlyList<BitmapSource> value) => OnPropertyChanged(nameof(IsPreviewAnimated));

    partial void OnShowDefaultValuesChanged(bool value)
    {
        Services.Settings.ShowDefaultValues = value;
        UpdateDetails();
    }

    partial void OnPropertyFilterChanged(string value) => UpdateDetails();

    /// <summary>Clicking the preview cycles its background (original behavior, now with a checkerboard).</summary>
    [RelayCommand]
    private void CyclePreviewBackground() =>
        PreviewBackground = (PreviewBackground)(((int)PreviewBackground + 1) % Enum.GetValues<PreviewBackground>().Length);

    private int _detailsVersion;

    private async void UpdateDetails()
    {
        int version = ++_detailsVersion;
        var item = SelectedItem;
        var db = _loadedDb;
        if (item == null || db == null)
        {
            DetailName = DetailId = SpriteIdsText = PreviewMessage = "";
            Properties = Array.Empty<PropertyRowViewModel>();
            PreviewFrames = Array.Empty<BitmapSource>();
            return;
        }
        var a = item.Appearance;
        DetailName = string.IsNullOrEmpty(a.Name) ? L.T("Appearances.NoName") : a.Name;
        DetailId = $"{item.Kind.Display()} {a.Id}";
        SpriteIdsText = string.Join(", ", AppearanceDatabase.DisplaySpriteIds(a));
        var rows = AppearanceProperties.Build(a, db, hideDefaults: !ShowDefaultValues);
        if (!string.IsNullOrWhiteSpace(PropertyFilter))
            rows = rows.Where(r => r.Label.Contains(PropertyFilter, StringComparison.OrdinalIgnoreCase) || r.Value.Contains(PropertyFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        Properties = PropertyRowViewModel.From(rows, Services);

        var animation = AppearanceAnimation.ForPreview(a, item.Kind);
        var sprites = Services.Data.Sprites;
        List<Core.Imaging.RgbaImage?> frames;
        try
        {
            frames = await Task.Run(() => animation.SpriteIds.Select(id => sprites?.GetSprite(id)).ToList());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or DataFileException)
        {
            // 9.0 (audit F-09): a damaged or locked sprite sheet only empties this preview; it reached the global
            // error handler before (async void without a handler).
            if (version != _detailsVersion) return;
            AppLog.Warn($"Preview of {item.Kind} {a.Id}: {ex.Message}");
            PreviewFrames = Array.Empty<BitmapSource>();
            PreviewMessage = L.T("Appearances.SomeSpritesCouldNotBeFound");
            return;
        }
        if (version != _detailsVersion) return;
        if (frames.Count == 0 || frames.Any(f => f == null))
        {
            PreviewFrames = frames.Where(f => f != null).Select(f => Images.ToBitmapSource(f!)).ToList();
            PreviewMessage = frames.Count == 0 ? L.T("Appearances.NoSprites") : L.T("Appearances.SomeSpritesCouldNotBeFound");
        }
        else
        {
            PreviewFrames = frames.Select(f => Images.ToBitmapSource(f!)).ToList();
            PreviewMessage = "";
        }
        PreviewDelays = animation.Delays;
    }

    [RelayCommand]
    private void CopySpriteIds() => Services.Clipboard.SetText(SpriteIdsText);

    [RelayCommand]
    private void CopyProperties() => Services.Clipboard.SetText(string.Join(Environment.NewLine, Properties.Select(p => p.Label + "\t" + p.Value)));

    /// <summary>"View All": every sprite of the first two frame groups in a gallery window.</summary>
    [RelayCommand]
    private void ViewAllSprites()
    {
        var item = SelectedItem;
        if (item == null || Services.Data.Sprites == null) return;
        var ids = AppearanceDatabase.AllSpriteIds(item.Appearance);
        int columns = item.Kind switch { AppearanceKind.Effect => 2, AppearanceKind.Missile => 3, _ => 4 };
        SpriteGalleryWindow.Open(L.F("Appearances.012Sprites", item.Kind, item.Id, ids.Count), ids, Services.Data.Sprites, columns, Services);
    }

    /// <summary>The original "Compare DAT files" button of the Tools tab: opens the Compare page.</summary>
    [RelayCommand]
    private void OpenCompare() => Services.RequestNavigate<CompareViewModel>();
}
