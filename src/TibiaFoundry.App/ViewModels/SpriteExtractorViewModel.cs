using TibiaFoundry.Core.Compatibility;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>A sprite sheet row of the extractor lists.</summary>
public sealed record SheetRow(SpriteSheetInfo Sheet)
{
    public int FirstId => Sheet.FirstSpriteId;
    public int LastId => Sheet.LastSpriteId;
    public string Size => Sheet.SizeLabel;
    public int Count => Sheet.SpriteCount;
}

/// <summary>"Extract Sprites": dump sprite sheets from the client as individual sprites or whole sheets.</summary>
public sealed partial class SpriteExtractorViewModel : PageViewModel
{
    private List<SheetRow> _allSheets = new();

    public SpriteExtractorViewModel(AppServices services) : base(services, BuiltInTools.SpriteExtractor)
    {
        _outputFolder = services.Outputs.FolderFor(OutputKind.SpriteExports);
    }

    public override IReadOnlyList<string> DataComponents { get; } = [CompatibilityChecker.SpriteCatalogComponent];


    [ObservableProperty] private string _assetsFolder = "";
    [ObservableProperty] private string _outputFolder;
    [ObservableProperty] private IReadOnlyList<SheetRow> _visibleSheets = Array.Empty<SheetRow>();
    [ObservableProperty] private IReadOnlyList<SheetRow> _selectedAvailable = Array.Empty<SheetRow>();
    [ObservableProperty] private IReadOnlyList<SheetRow> _selectedQueued = Array.Empty<SheetRow>();
    [ObservableProperty] private string _sheetFilter = "";
    [ObservableProperty] private SpriteSheetType? _typeFilter;
    [ObservableProperty] private SpriteExportMode _mode = SpriteExportMode.IndividualSprites;
    [ObservableProperty] private SpriteImageFormat _format = SpriteImageFormat.Png;
    [ObservableProperty] private bool _splitFolders;
    [ObservableProperty] private bool _originalNames;
    [ObservableProperty] private bool _skipExisting;
    [ObservableProperty] private string _stats = "";
    [ObservableProperty] private string _newest = "";

    public ObservableCollection<SheetRow> Queue { get; } = new();
    public int QueuedSprites => Queue.Sum(r => r.Count);
    public string QueueSummary => L.F("SpriteExtractor.0N0Sheets1N0Sprites", Queue.Count, QueuedSprites);

    partial void OnSheetFilterChanged(string value) => ApplyFilter();
    partial void OnTypeFilterChanged(SpriteSheetType? value) => ApplyFilter();

    public override Task OnNavigatedToAsync()
    {
        Reload();
        return Task.CompletedTask;
    }

    private void Reload()
    {
        AssetsFolder = Services.Data.AssetsDirectory;
        var catalog = Services.Data.Catalog;
        if (catalog == null)
        {
            _allSheets = new();
            Stats = L.T("SpriteExtractor.NoClientFilesWereFoundIn");
            Newest = "";
        }
        else
        {
            _allSheets = catalog.Sheets.Select(s => new SheetRow(s)).ToList();
            Stats = L.F("SpriteExtractor.0N0Sheets1N0Sprites2", catalog.SheetCount, catalog.TotalSprites, catalog.HighestSpriteId);
            string N(SpriteSheetType t) => catalog.NewestSheet(t)?.FirstSpriteId.ToString("N0") ?? "-";
            Newest = L.F("SpriteExtractor.FirstSpriteIDOfTheNewest", N(SpriteSheetType.Small), N(SpriteSheetType.Tall), N(SpriteSheetType.Wide), N(SpriteSheetType.Large));
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        IEnumerable<SheetRow> rows = _allSheets;
        if (TypeFilter is { } t) rows = rows.Where(r => r.Sheet.Type == t);
        string text = SheetFilter.Trim();
        if (int.TryParse(text, out int id)) rows = rows.Where(r => r.Sheet.Contains(id) || r.FirstId.ToString().StartsWith(text, StringComparison.Ordinal));
        VisibleSheets = rows.ToList();
    }

    private void Enqueue(IEnumerable<SheetRow> rows)
    {
        var existing = Queue.Select(q => q.FirstId).ToHashSet();
        foreach (var r in rows.Where(r => existing.Add(r.FirstId)).OrderBy(r => r.FirstId)) Queue.Add(r);
        OnPropertyChanged(nameof(QueueSummary));
        OnPropertyChanged(nameof(QueuedSprites));
    }

    [RelayCommand] private void AddSelected() => Enqueue(SelectedAvailable);
    [RelayCommand] private void AddVisible() => Enqueue(VisibleSheets);
    [RelayCommand] private void AddAll() => Enqueue(_allSheets);
    [RelayCommand] private void AddType(SpriteSheetType type) => Enqueue(_allSheets.Where(r => r.Sheet.Type == type));

    [RelayCommand]
    private void RemoveSelected()
    {
        foreach (var r in SelectedQueued.ToList()) Queue.Remove(r);
        OnPropertyChanged(nameof(QueueSummary));
        OnPropertyChanged(nameof(QueuedSprites));
    }

    [RelayCommand]
    private void RemoveAll()
    {
        Queue.Clear();
        OnPropertyChanged(nameof(QueueSummary));
        OnPropertyChanged(nameof(QueuedSprites));
    }

    /// <summary>Original "Type ID": add the sheet(s) containing the given sprite IDs.</summary>
    [RelayCommand]
    private void AddBySpriteId()
    {
        var catalog = Services.Data.Catalog;
        if (catalog == null) return;
        string? text = Services.Dialogs.Prompt(L.T("SpriteExtractor.AddSheetsBySpriteID"), L.T("SpriteExtractor.SpriteIDsSeparatedByCommasOr"));
        if (text == null) return;
        if (!Core.Appearances.AppearanceFilter.TryParseIdList(text, out var ids, out var bad))
        {
            Services.Dialogs.Show(L.F("SpriteExtractor.0IsNotAValidSprite", bad), MessageKind.Warning, L.T("SpriteExtractor.AddBySpriteID"));
            return;
        }
        var missing = new List<uint>();
        var rows = new List<SheetRow>();
        foreach (uint id in ids)
        {
            var sheet = catalog.FindSheet((int)id);
            if (sheet == null) missing.Add(id);
            else rows.Add(new SheetRow(sheet));
        }
        Enqueue(rows);
        if (missing.Count > 0) Services.Dialogs.Show(L.F("SpriteExtractor.TheseSpriteIDsAreNotIn", string.Join(", ", missing)), MessageKind.Warning, L.T("SpriteExtractor.AddBySpriteID"));
    }

    /// <summary>"Change…": the client's assets folder (checked, remembered for the client in use; Settings → Tibia client).</summary>
    [RelayCommand]
    private async Task ChangeAssetsFolderAsync()
    {
        if (!await Services.Locations.LocateAsync(Core.Locations.LocationIds.Assets)) return;
        Queue.Clear();
        Reload();
    }

    [RelayCommand]
    private void ChangeOutputFolder()
    {
        string? folder = Services.Dialogs.PickFolder(L.T("SpriteExtractor.OutputFolderForSprites"), OutputFolder);
        if (folder == null) return;
        if (Core.Editing.AssetsProject.IsInsideClientInstallation(folder))
        {
            Services.Dialogs.Show(L.T("SpriteExtractor.FoldersOfTheInstalledTibiaClient"), MessageKind.Warning, L.T("SpriteExtractor.ExtractSprites"));
            return;
        }
        OutputFolder = folder;
        Services.Settings.SpriteExtractionFolder = folder;
        Services.SaveSettings();
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (Directory.Exists(OutputFolder)) Services.RevealSaved(new StatusLink(OutputFolder, IsFile: false));
        else Services.Status.Show(L.F("SpriteExtractor.0DoesNotExistYetIt", OutputFolder), StatusKind.Info);
    }

    [RelayCommand]
    private async Task StartExtractionAsync()
    {
        var catalog = Services.Data.Catalog;
        if (catalog == null) { Services.Dialogs.Show(L.T("SpriteExtractor.NoClientFilesAreAvailableCheck")); return; }
        if (Queue.Count == 0) { Services.Dialogs.Show(L.T("SpriteExtractor.AddAtLeastOneSheetTo"), MessageKind.Info, L.T("SpriteExtractor.ExtractSprites")); return; }
        var options = new SpriteExtractionOptions
        {
            OutputDirectory = OutputFolder, Mode = Mode, Format = Format, SplitIntoSheetFolders = SplitFolders,
            UseOriginalSheetNames = OriginalNames, SkipExisting = SkipExisting, ClientVersion = Services.Data.ClientVersion,
        };
        if (OutputLocations.Prepare(OutputFolder) is { Usable: false } state)
        {
            Services.Dialogs.Show(L.F("SpriteExtractor.TheOutputFolderCannotBeUsed", state.Problem), MessageKind.Warning, L.T("SpriteExtractor.ExtractSprites"));
            return;
        }
        var sheets = Queue.Select(q => q.Sheet).ToList();
        BatchResult? result = null;
        string folder = OutputFolder;
        await RunAsync(L.T("SpriteExtractor.ExtractingSprites"), async (p, ct) => result = await SpriteExtractor.ExtractAsync(catalog, sheets, options, p, ct), output: folder,
            onSuccess: () => ReportBatch(result!, Mode == SpriteExportMode.WholeSheets ? "sheets" : "sprites", folder, L.T("SpriteExtractor.ExtractingSprites")));
    }
}
