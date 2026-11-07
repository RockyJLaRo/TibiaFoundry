using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Compatibility;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Export;
using TibiaFoundry.Core.Houses;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>"House Images": renders houses from staticmapdata.dat (floors, crop, background) for TibiaWiki.</summary>
public sealed partial class HouseImagesViewModel : PageViewModel
{
    private List<ListItem> _items = new();
    private HouseFloors? _floors;
    private RgbaImage? _image;

    public HouseImagesViewModel(AppServices services) : base(services, BuiltInTools.HouseImages)
    {
        // Another client, file or folder in use: the next load uses the new default file.
        services.Data.ClientFilesChanged += types => { if (types == null || types.Contains(AssetTypes.StaticMapData)) DatPath = null; };
        services.Data.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ClientDataService.StaticMap) or nameof(ClientDataService.StaticData)) Rebuild();
        };
    }

    public override IReadOnlyList<string> DataComponents { get; } = [CompatibilityChecker.SpriteCatalogComponent, TibiaFormat.CatalogTypeAppearances, TibiaFormat.CatalogTypeStaticMapData, TibiaFormat.CatalogTypeStaticData];


    [ObservableProperty] private string? _datPath;
    [ObservableProperty] private IReadOnlyList<ListItem> _visibleItems = Array.Empty<ListItem>();
    [ObservableProperty] private ListItem? _selectedItem;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private int _floor;
    [ObservableProperty] private int _minFloor;
    [ObservableProperty] private int _maxFloor;
    [ObservableProperty] private bool _allFloors = true;
    [ObservableProperty] private bool _crop;
    [ObservableProperty] private HouseBackground _background = HouseBackground.Transparent;
    [ObservableProperty] private BitmapSource? _houseImage;
    [ObservableProperty] private bool _fitToWindow = true;
    [ObservableProperty] private string _renderInfo = "";

    public IReadOnlyList<HouseBackground> Backgrounds { get; } = Enum.GetValues<HouseBackground>();
    public bool IsLoaded => Services.Data.StaticMap != null;
    public int VisibleCount => VisibleItems.Count;

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnSelectedItemChanged(ListItem? value) => _ = RenderFloorsAsync();
    partial void OnFloorChanged(int value) => Compose();
    partial void OnAllFloorsChanged(bool value) => Compose();
    partial void OnCropChanged(bool value) => Compose();
    partial void OnBackgroundChanged(HouseBackground value) => Compose();

    public override async Task OnNavigatedToAsync()
    {
        DatPath ??= Services.Data.DefaultFile(AssetTypes.StaticMapData);
        if (Services.Data.StaticMap == null && Services.Settings.LoadDataAutomatically && DatPath != null && !IsBusy) await LoadAsync();
        else if (_items.Count == 0) Rebuild();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        DatPath ??= Services.Data.DefaultFile(AssetTypes.StaticMapData);
        if (DatPath == null || !File.Exists(DatPath)) { Services.Dialogs.Show(L.T("HouseImages.NoStaticmapdataFileWasFoundPick"), MessageKind.Warning, L.T("HouseImages.HouseImages")); return; }
        string path = DatPath;
        await RunAsync(L.T("HouseImages.LoadingHouseMaps"), async (_, _) =>
        {
            await Services.Data.EnsureAppearancesAsync();
            if (Services.Data.StaticData == null && Services.Data.DefaultFile(AssetTypes.StaticData) is { } sd) await Services.Data.LoadStaticDataAsync(sd);
            await Services.Data.LoadStaticMapAsync(path);
        }, L.F("HouseImages.Loaded0", Path.GetFileName(path)));
    }

    /// <summary>"Change file" (the original button had no handler).</summary>
    [RelayCommand]
    private async Task ChangeFileAsync()
    {
        string? file = Services.Dialogs.OpenFile(L.T("HouseImages.SelectAStaticmapdataFile"), L.T("HouseImages.TibiaStaticMapDataDatDat"), Services.Data.AssetsDirectory);
        if (file == null) return;
        await OfferAsDefaultAsync(AssetTypes.StaticMapData, file);
        DatPath = file;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        var db = Services.Data.StaticMap;
        if (db == null) { Services.Dialogs.Show(L.T("HouseImages.LoadTheHouseMapsFirst")); return; }
        string? file = Services.Outputs.SaveFile(OutputKind.DataExports, L.T("HouseImages.ExportStaticMapDataAsJSON"), L.T("HouseImages.JSONFilesJsonJson"), "StaticMapData.json");
        if (file == null) return;
        await RunAsync(L.T("HouseImages.ExportingJSON"), (_, ct) => JsonExport.WriteAsync(db.Data, file, ct), output: file, onSuccess: () => Saved(file, ". " + Attribution.NoMetadataNote));
    }

    private void Rebuild()
    {
        OnPropertyChanged(nameof(IsLoaded));
        var maps = Services.Data.StaticMap;
        var houses = Services.Data.StaticData?.Data.Houses;
        if (maps == null) { _items = new(); ApplyFilter(); return; }
        // First entry wins: a duplicate house ID in staticdata must not break the page (audit A19).
        var names = new Dictionary<uint, Tibia.Protobuf.Staticdata.House>();
        foreach (var h in houses ?? Enumerable.Empty<Tibia.Protobuf.Staticdata.House>()) names.TryAdd(h.HouseId, h);
        _items = maps.Data.HouseMaps.Select(m =>
        {
            names.TryGetValue(m.HouseId, out var h);
            return new ListItem(m.HouseId.ToString(), h?.Name ?? L.F("HouseImages.House0", m.HouseId), m, h?.City ?? "");
        }).OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var previous = SelectedItem;
        VisibleItems = _items.Where(i => i.Matches(FilterText.Trim())).ToList();
        SelectedItem = previous != null && VisibleItems.Contains(previous) ? previous : (VisibleItems.Count > 0 ? VisibleItems[0] : null);
        OnPropertyChanged(nameof(VisibleCount));
    }

    private int _version;

    private async Task RenderFloorsAsync()
    {
        int version = ++_version;
        var item = SelectedItem;
        var appearances = Services.Data.Appearances;
        var sprites = Services.Data.Sprites;
        if (item?.Source is not Tibia.Protobuf.Staticmapdata.HouseMap map || appearances == null || sprites == null)
        {
            _floors = null;
            HouseImage = null;
            return;
        }
        try
        {
            var floors = await Task.Run(() => HouseRenderer.RenderFloors(map, appearances, sprites));
            if (version != _version) return;
            _floors = floors;
            MinFloor = floors.TopFloor;
            MaxFloor = floors.BottomFloor;
            // The original selected the house's top floor; with "View all floors" that shows the whole house.
            if (Floor == floors.TopFloor) Compose(); else Floor = floors.TopFloor;
            RenderInfo = L.F("HouseImages.Floors0123Tiles", floors.TopFloor, floors.BottomFloor, floors.Columns - 1, floors.Rows - 1) +
                   (floors.Warnings.Count > 0 ? $" · {L.Count(floors.Warnings.Count, "Noun.warning")}: {string.Join("; ", floors.Warnings.Take(3))}" : "");
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            if (version != _version) return;
            HouseImage = null;
            RenderInfo = L.T("HouseImages.ThisHouseCouldNotBeRendered") + ex.Message;
        }
    }

    private void Compose()
    {
        if (_floors == null) return;
        _image = HouseRenderer.Compose(_floors, Math.Clamp(Floor, MinFloor, MaxFloor), AllFloors, Crop, Background);
        HouseImage = Images.ToBitmapSource(_image);
    }

    [RelayCommand]
    private void Copy()
    {
        if (_image == null) return;
        if (Services.Clipboard.SetImage(_image)) Info(L.T("HouseImages.HouseImageCopiedToTheClipboard"));
    }

    [RelayCommand]
    private void Save()
    {
        if (_image == null || SelectedItem == null) return;
        string? file = Services.Outputs.SaveFile(OutputKind.ImageExports, L.T("HouseImages.SaveHouseImage"), L.T("HouseImages.PNGImagesPngPng"), FileNames.Sanitize(SelectedItem.Name) + ".png");
        if (file == null) return;
        Try(L.T("HouseImages.SavingTheHouseImage"), () =>
        {
            ImageIO.SavePng(_image, file);
            Saved(file);
        });
    }
}
