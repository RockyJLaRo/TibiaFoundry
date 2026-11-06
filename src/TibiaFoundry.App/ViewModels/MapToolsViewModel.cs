using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Compatibility;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Export;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Map;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

public enum MapListKind { Areas, Npcs, Files }

/// <summary>
/// "Map Tools": areas, subareas, NPCs and map resource files from map.dat; map tile extraction,
/// high-resolution map assembly, Cyclopedia cropping and wiki/table exports.
/// </summary>
public sealed partial class MapToolsViewModel : PageViewModel
{
    private List<ListItem> _items = new();

    public MapToolsViewModel(AppServices services) : base(services, BuiltInTools.MapTools)
    {
        // Another client, file or folder in use: the next load uses the new default file.
        services.Data.ClientFilesChanged += types => { if (types == null || types.Contains(AssetTypes.Map)) DatPath = null; };
        services.Data.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ClientDataService.Map)) Rebuild();
        };
        _extractInput = _automaticExtractInput = services.Data.AssetsDirectory;
        services.Data.ClientFilesChanged += types =>
        {
            if (types != null) return;
            if (ExtractInput == _automaticExtractInput) ExtractInput = services.Data.AssetsDirectory;
            _automaticExtractInput = services.Data.AssetsDirectory;
        };
        _extractOutput = services.Outputs.FolderFor(OutputKind.MapExports);
        _hrInput = services.Outputs.FolderFor(OutputKind.MapExports);
    }

    public override IReadOnlyList<string> DataComponents { get; } = [TibiaFormat.CatalogTypeMap];

    /// <summary>The assets folder the extraction input was set to automatically (it follows the client unless changed on the page).</summary>
    private string _automaticExtractInput;


    [ObservableProperty] private string? _datPath;
    [ObservableProperty] private MapListKind _listKind = MapListKind.Areas;
    [ObservableProperty] private IReadOnlyList<ListItem> _visibleItems = Array.Empty<ListItem>();
    [ObservableProperty] private ListItem? _selectedItem;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private IReadOnlyList<PropertyRowViewModel> _properties = Array.Empty<PropertyRowViewModel>();

    public bool IsLoaded => Services.Data.Map != null;
    public int TotalCount => _items.Count;
    public int VisibleCount => VisibleItems.Count;

    partial void OnListKindChanged(MapListKind value) => Rebuild();
    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSelectedItemChanged(ListItem? value)
    {
        var map = Services.Data.Map;
        if (value == null || map == null) { Properties = Array.Empty<PropertyRowViewModel>(); return; }
        var rows = value.Source switch
        {
            AreaRecord a => map.FindArea(a.AreaId) is { } area ? map.AreaRows(area) : new List<PropertyRow>(),
            NpcRecord n => map.NpcRows(map.Data.Npcs[n.Index]),
            MapFileRecord f => map.FileRows(map.Data.ResourceFiles[f.Index]),
            _ => new List<PropertyRow>(),
        };
        Properties = PropertyRowViewModel.From(rows, Services);
    }

    public override async Task OnNavigatedToAsync()
    {
        DatPath ??= Services.Data.DefaultFile(AssetTypes.Map);
        if (Services.Data.Map == null && Services.Settings.LoadDataAutomatically && DatPath != null && !IsBusy) await LoadAsync();
        else if (_items.Count == 0) Rebuild();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        DatPath ??= Services.Data.DefaultFile(AssetTypes.Map);
        if (DatPath == null || !File.Exists(DatPath)) { Services.Dialogs.Show(L.T("MapTools.NoMapFileWasFoundPick"), MessageKind.Warning, L.T("MapTools.Map")); return; }
        string path = DatPath;
        await RunAsync(L.T("MapTools.LoadingMapData"), async (_, _) => await Services.Data.LoadMapAsync(path), L.F("MapTools.Loaded0", Path.GetFileName(path)));
    }

    [RelayCommand]
    private async Task ChangeFileAsync()
    {
        string? file = Services.Dialogs.OpenFile(L.T("MapTools.SelectAMapFile"), L.T("MapTools.TibiaMapDataMapDatDat"), Services.Data.AssetsDirectory);
        if (file == null) return;
        await OfferAsDefaultAsync(AssetTypes.Map, file);
        DatPath = file;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        var map = Services.Data.Map;
        if (map == null) { Services.Dialogs.Show(L.T("MapTools.LoadTheMapDataFirst")); return; }
        string? file = Services.Outputs.SaveFile(OutputKind.DataExports, L.T("MapTools.ExportMapDataAsJSON"), L.T("MapTools.JSONFilesJsonJson"), "Map.json");
        if (file == null) return;
        await RunAsync(L.T("MapTools.ExportingJSON"), (_, ct) => JsonExport.WriteAsync(map.Data, file, ct), output: file, onSuccess: () => Saved(file, ". " + Attribution.NoMetadataNote));
    }

    private void Rebuild()
    {
        var map = Services.Data.Map;
        OnPropertyChanged(nameof(IsLoaded));
        _items = map == null ? new() : ListKind switch
        {
            MapListKind.Areas => map.Areas.Select(a => new ListItem(a.ID, a.Name, a, a.Type)).ToList(),
            MapListKind.Npcs => map.Npcs.Select(n => new ListItem($"{n.X},{n.Y},{n.Z}", n.Name, n, n.Subarea ?? "")).ToList(),
            _ => map.Files.Select(f => new ListItem(f.Areaid, f.Type, f, f.Position)).ToList(),
        };
        OnPropertyChanged(nameof(TotalCount));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var previous = SelectedItem;
        VisibleItems = _items.Where(i => i.Matches(FilterText.Trim())).ToList();
        SelectedItem = previous != null && VisibleItems.Contains(previous) ? previous : (VisibleItems.Count > 0 ? VisibleItems[0] : null);
        OnPropertyChanged(nameof(VisibleCount));
    }

    // ------------------------------------------------------------------ wiki / table exports
    private MapDatabase? RequireMap()
    {
        if (Services.Data.Map == null) Services.Dialogs.Show(L.T("MapTools.LoadTheMapDataFirst"));
        return Services.Data.Map;
    }

    private void CopyToClipboard(string text, string what)
    {
        if (Services.Clipboard.SetText(text)) Info(L.F("MapTools.0CopiedToTheClipboard", what));
    }

    [RelayCommand] private void CopyWikiAreaList() { if (RequireMap() is { } m) CopyToClipboard(MapExports.WikiAreaList(m), L.T("MapTools.AreaAndSubareaTable")); }
    [RelayCommand] private void CopyLabelsWithCoordinates() { if (RequireMap() is { } m) CopyToClipboard(MapExports.LabelsWithCoordinates(m), L.T("MapTools.MapLabelsWithCoordinates")); }
    [RelayCommand] private void CopyLabelsWithoutCoordinates() { if (RequireMap() is { } m) CopyToClipboard(MapExports.LabelsWithoutCoordinates(m), L.T("MapTools.SubareasWithoutAMapLabel")); }

    /// <summary>Exports the shown list type (areas or NPCs) as JSON (original "Export Data → JSON").</summary>
    [RelayCommand]
    private void ExportListJson() => ExportList("json");

    [RelayCommand]
    private void ExportListTable() => ExportList("txt");

    private void ExportList(string format)
    {
        if (RequireMap() is not { } map) return;
        if (ListKind == MapListKind.Files) { Services.Dialogs.Show(L.T("MapTools.SelectTheAreasOrNPCsList")); return; }
        string name = ListKind == MapListKind.Areas ? L.T("MapTools.Areas") : "NPCs";
        string filter = format == "json" ? L.T("MapTools.JSONFilesJsonJson") : L.T("MapTools.TextFilesTxtTxt");
        string? file = Services.Outputs.SaveFile(OutputKind.DataExports, L.F("MapTools.Export0", name), filter, $"{name}.{format}");
        if (file == null) return;
        Try(L.T("MapTools.Exporting") + name, () =>
        {
            string text = (ListKind, format) switch
            {
                (MapListKind.Areas, "json") => MapExports.AreasJson(map),
                (MapListKind.Areas, _) => MapExports.AreasTable(map),
                (_, "json") => MapExports.NpcsJson(map),
                _ => MapExports.NpcsTable(map),
            };
            AtomicFile.WriteAllText(file, text);
            Saved(file, ". " + Attribution.NoMetadataNote);
        });
    }

    // ------------------------------------------------------------------ extract map files
    [ObservableProperty] private string _extractInput;
    [ObservableProperty] private string _extractOutput;
    [ObservableProperty] private bool _extractSubareas = true;
    [ObservableProperty] private bool _extractSatellite = true;
    [ObservableProperty] private bool _extractMinimap = true;

    [RelayCommand] private void BrowseExtractInput() { if (Services.Dialogs.PickFolder(L.T("MapTools.FolderWithTheMapFiles"), ExtractInput) is { } f) ExtractInput = f; }
    [RelayCommand]
    private void BrowseExtractOutput()
    {
        if (Services.Dialogs.PickFolder(L.T("MapTools.OutputFolderForMapImages"), ExtractOutput) is not { } f) return;
        if (Core.Editing.AssetsProject.IsInsideClientInstallation(f))
        {
            Services.Dialogs.Show(L.T("MapTools.FoldersOfTheInstalledTibiaClient"), MessageKind.Warning, L.T("MapTools.MapImages"));
            return;
        }
        ExtractOutput = f;
        Services.Settings.MapTilesFolder = f;
        Services.SaveSettings();
    }

    [RelayCommand]
    private async Task ExtractAsync()
    {
        if (RequireMap() is not { } map) return;
        if (!ExtractSubareas && !ExtractSatellite && !ExtractMinimap) { Services.Dialogs.Show(L.T("MapTools.CheckAtLeastOneImageType")); return; }
        // Also for a folder typed in the box (Browse checks it too).
        if (Core.Editing.AssetsProject.IsInsideClientInstallation(ExtractOutput))
        {
            Services.Dialogs.Show(L.T("MapTools.FoldersOfTheInstalledTibiaClient2"), MessageKind.Warning, L.T("MapTools.ExtractingMapFiles"));
            return;
        }
        if (OutputLocations.Prepare(ExtractOutput) is { Usable: false } state)
        {
            Services.Dialogs.Show(L.F("MapTools.TheOutputFolderCannotBeUsed", state.Problem), MessageKind.Warning, L.T("MapTools.ExtractingMapFiles"));
            return;
        }
        // An imported client version has no real assets folder: its tiles are read through its manifest.
        var assets = Services.Data.Locations.Resolve(Core.Locations.LocationIds.Assets);
        var options = new MapExtractionOptions(ExtractInput, ExtractOutput, ExtractSubareas, ExtractSatellite, ExtractMinimap)
        {
            Input = assets.IsImported && string.Equals(ExtractInput, assets.DisplayPath, StringComparison.OrdinalIgnoreCase) ? assets.Folder : null,
        };
        BatchResult? result = null;
        string output = ExtractOutput;
        await RunAsync(L.T("MapTools.ExtractingMapFiles"), async (p, ct) => result = await MapImageTools.ExtractAsync(map, options, p, ct), operation: "extract-tiles", output: output,
            onSuccess: () =>
            {
                ReportBatch(result!, "images", output, L.T("MapTools.ExtractingMapFiles"));
                if (result!.Succeeded > 0) HrInput = output;
            });
    }

    [RelayCommand] private void OpenExtractOutput() => Services.Shell.OpenFolder(ExtractOutput);

    // ------------------------------------------------------------------ HR map
    [ObservableProperty] private string _hrInput;
    [ObservableProperty] private HrMapType _hrType = HrMapType.Satellite;
    [ObservableProperty] private int _hrZoomIndex = 2;
    [ObservableProperty] private int _hrTopFloor;
    [ObservableProperty] private bool _hrCumulative = true;
    [ObservableProperty] private bool _hrFloorOffset = true;
    [ObservableProperty] private bool _hrWatermark;

    public IReadOnlyList<string> HrZoomOptions => HrType == HrMapType.Satellite ? ["50%", "100%", "200%"] : ["50%", "100%"];
    public IReadOnlyList<string> HrFloorOptions => [L.T("MapTools.Ground0"), "+1", "+2", "+3", "+4", "+5", "+6", "+7"];

    partial void OnHrTypeChanged(HrMapType value)
    {
        OnPropertyChanged(nameof(HrZoomOptions));
        HrZoomIndex = HrZoomOptions.Count - 1;
    }

    [RelayCommand] private void BrowseHrInput() { if (Services.Dialogs.PickFolder(L.T("MapTools.FolderWithExtractedMapImages"), HrInput) is { } f) HrInput = f; }

    [RelayCommand]
    private async Task BuildHrAsync()
    {
        if (string.IsNullOrWhiteSpace(HrInput) || !Directory.Exists(HrInput))
        {
            Services.Dialogs.Show(L.T("MapTools.ChooseTheFolderWithTheExtracted"), MessageKind.Info, L.T("MapTools.HRMap"));
            return;
        }
        string? file = Services.Outputs.SaveFile(OutputKind.ImageExports, L.T("MapTools.SaveHRMap"), L.T("MapTools.PNGImagesPngPng"), $"{HrType}_{HrZoomOptions[Math.Clamp(HrZoomIndex, 0, HrZoomOptions.Count - 1)].TrimEnd('%')}.png");
        if (file == null) return;
        var options = new HrMapOptions
        {
            InputDirectory = HrInput, Type = HrType, Zoom = HrZoomIndex + 1, FloorsAboveGround = HrTopFloor,
            Cumulative = HrCumulative, FloorOffset = HrFloorOffset, Watermark = HrWatermark,
        };
        // The map is stitched from tile files: tiles extracted by someone else keep their creator.
        var sources = await Task.Run(() => AttributionReader.ReadAll(MapImageTools.TilesUsed(options)));
        if (AttributionForImports(sources, L.T("MapTools.HRMap"), Attribution.TibiaArtworkNotice) is not { } attribution) return;
        await RunAsync(L.T("MapTools.BuildingHRMap"), (p, ct) => Task.Run(() =>
        {
            var image = MapImageTools.BuildHighResMap(options, p, ct);
            ImageIO.SavePng(image, file, attribution);
        }, ct), operation: "hr-map", output: file, onSuccess: () => Saved(file));
    }

    [RelayCommand]
    private async Task CropCyclopediaAsync()
    {
        string? input = Services.Dialogs.PickFolder(L.T("MapTools.FolderWithTheCyclopediaHRFiles"), Services.Outputs.FolderFor(OutputKind.MapExports));
        if (input == null) return;
        string? output = Services.Outputs.PickFolder(OutputKind.MapExports, L.T("MapTools.OutputFolderForTheMinimapTiles"));
        if (output == null) return;
        var sources = await Task.Run(() => AttributionReader.ReadAll(MapImageTools.CyclopediaInputs(input)));
        if (AttributionForImports(sources, L.T("MapTools.CropCyclopediaToMinimap"), Attribution.TibiaArtworkNotice) is not { } attribution) return;
        BatchResult? result = null;
        await RunAsync(L.T("MapTools.CroppingCyclopediaMaps"), (p, ct) => Task.Run(() => result = MapImageTools.CropCyclopediaMaps(input, output, attribution, p, ct), ct),
            output: output, onSuccess: () => ReportBatch(result!, "tiles", output, L.T("MapTools.CropCyclopediaToMinimap")));
    }
}
