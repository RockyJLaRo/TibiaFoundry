using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Export;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Wiki;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

public enum SheetOutput { PngClipboard, PngFile, Base64 }

/// <summary>One entry of the preset selector: a built-in preset, or "Custom" (<see cref="Preset"/> null).</summary>
public sealed record GifPresetChoice(GifExportPreset? Preset, string Name, string Description);

/// <summary>The "Export" and "Tools" tabs of the Appearances page.</summary>
public sealed partial class AppearanceExportViewModel : ObservableObject
{
    private readonly AppearancesViewModel _owner;
    private readonly AppServices _services;

    public AppearanceExportViewModel(AppearancesViewModel owner, AppServices services)
    {
        _owner = owner;
        _services = services;
        var s = services.Settings;
        services.CreatorChanged += () => OnPropertyChanged(nameof(AttributionPreview));
        _autoName = s.GifAutoName;
        _delayMode = s.GifDelayMode;
        _noAnimationDelay = s.GifNoAnimationDelay;
        _extraDelay = s.GifExtraDelay;
        _extraFrame = s.GifExtraFrame;
        _scale = s.GifScale;
        _kindFolders = s.GifKindFolders;
        _selectedPreset = Presets.FirstOrDefault(p => p.Preset?.Id == s.GifPreset) ?? Presets[0];
    }

    // ------------------------------------------------------------------ fansite export presets

    /// <summary>"Custom" (the options as set) followed by the built-in presets.</summary>
    public IReadOnlyList<GifPresetChoice> Presets { get; } =
        [new(null, L.T("ThemeEditor.Custom"), L.T("AppearanceExport.YourOwnChoiceOfOptions")), .. GifExportPresets.All.Select(p => new GifPresetChoice(p, p.Name, p.Description))];

    [ObservableProperty] private GifPresetChoice _selectedPreset;

    /// <summary>Whole-number enlargements offered for GIFs (nearest-neighbor, pixel-exact).</summary>
    public IReadOnlyList<int> Scales { get; } = [1, 2, 3, 4, 6, 8];

    /// <summary>Nearest-neighbor enlargement of the exported GIFs (1 = the client's own size).</summary>
    [ObservableProperty] private int _scale;

    /// <summary>Automatically named GIFs go into Objects, Outfits, Effects and Missiles subfolders.</summary>
    [ObservableProperty] private bool _kindFolders;

    private bool _applyingPreset;

    partial void OnSelectedPresetChanged(GifPresetChoice value)
    {
        Persist(s => s.GifPreset = value.Preset?.Id);
        if (value.Preset is not { } p) return;
        _applyingPreset = true;
        try
        {
            Scale = p.Scale;
            if (p.FrameGroup is int group) MovementIndex = group;
            if (p.Direction is { } direction) Direction = direction;
            DelayMode = p.DelayMode;
            ExtraFrame = p.ExtraFrame;
            ExtraDelay = p.ExtraDelay;
            KindFolders = p.KindFolders;
        }
        finally
        {
            _applyingPreset = false;
        }
    }

    /// <summary>An option changed by hand that the preset sets differently: the selector shows "Custom".</summary>
    private void LeavePresetIfChanged()
    {
        if (_applyingPreset || SelectedPreset.Preset is not { } p) return;
        if (!p.Matches(Scale, MovementIndex, Direction, DelayMode, ExtraFrame, ExtraDelay, KindFolders)) SelectedPreset = Presets[0];
    }

    partial void OnScaleChanged(int value)
    {
        Persist(s => s.GifScale = value);
        LeavePresetIfChanged();
    }

    partial void OnKindFoldersChanged(bool value)
    {
        Persist(s => s.GifKindFolders = value);
        LeavePresetIfChanged();
    }

    partial void OnDirectionChanged(Direction value) => LeavePresetIfChanged();
    partial void OnMovementIndexChanged(int value) => LeavePresetIfChanged();

    // ------------------------------------------------------------------ GIF options
    [ObservableProperty] private bool _autoName;
    [ObservableProperty] private GifDelayMode _delayMode;
    [ObservableProperty] private int _noAnimationDelay;
    [ObservableProperty] private int _extraDelay;
    [ObservableProperty] private bool _extraFrame;

    /// <summary>
    /// Where GIFs are saved: the folder chosen here or in Settings, or the default
    /// Documents\Tibia_Foundry\GIF_Exports (created when the first GIF is saved).
    /// </summary>
    public string OutputFolder => _services.Outputs.FolderFor(OutputKind.GifExports);

    /// <summary>The folder may have changed in Settings while this page was hidden.</summary>
    public void RefreshFolder() => OnPropertyChanged(nameof(OutputFolder));
    [ObservableProperty] private Direction _direction = Direction.South;
    [ObservableProperty] private int _movementIndex = 1; // 0 idle, 1 moving
    [ObservableProperty] private IReadOnlyList<string> _patternOptions = Array.Empty<string>();
    [ObservableProperty] private string? _selectedPattern;

    public bool HasPatterns => PatternOptions.Count > 0;

    /// <summary>The attribution embedded in every exported GIF and image (creator name set in Settings).</summary>
    public string AttributionPreview => Attribution.TextFor(Attribution.CurrentCreator);

    [RelayCommand]
    private void ChangeCreator() => _services.RequestNavigate<SettingsViewModel>();
    public IReadOnlyList<Direction> Directions { get; } = Enum.GetValues<Direction>();

    partial void OnAutoNameChanged(bool value) => Persist(s => s.GifAutoName = value);
    partial void OnDelayModeChanged(GifDelayMode value)
    {
        Persist(s => s.GifDelayMode = value);
        LeavePresetIfChanged();
    }

    partial void OnNoAnimationDelayChanged(int value) => Persist(s => s.GifNoAnimationDelay = value);

    partial void OnExtraDelayChanged(int value)
    {
        Persist(s => s.GifExtraDelay = value);
        LeavePresetIfChanged();
    }

    partial void OnExtraFrameChanged(bool value)
    {
        Persist(s => s.GifExtraFrame = value);
        LeavePresetIfChanged();
    }
    partial void OnPatternOptionsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasPatterns));

    private void Persist(Action<Core.Settings.AppSettings> change)
    {
        change(_services.Settings);
        _services.SaveSettings();
    }

    /// <summary>Pattern selector for the selected appearance ("All" plus each pattern index).</summary>
    public void OnSelectionChanged()
    {
        var item = _owner.SelectedItem;
        int count = item == null ? 0 : AppearanceGifBuilder.PatternCount(item.Appearance);
        PatternOptions = count == 0 ? Array.Empty<string>() : Enumerable.Range(0, count).Select(i => i.ToString()).Append("All").ToList();
        SelectedPattern = count == 0 ? null : "All";
    }

    public void OnDatabaseChanged()
    {
        NpcNames = _owner.Database == null ? Array.Empty<string>() : NpcTradeExporter.NpcNames(_owner.Database);
        Currencies = _owner.Database?.Currencies.Select(c => c.ToString()).ToList() ?? new List<string>();
        _owner.Filters.RefreshCurrencies();
    }

    private GifExportOptions BuildOptions() => new()
    {
        FrameGroup = MovementIndex,
        Direction = Direction,
        Pattern = SelectedPattern is null or "All" ? null : int.Parse(SelectedPattern),
        DelayMode = DelayMode,
        NoAnimationDelay = NoAnimationDelay,
        AddEmptyExtraFrame = ExtraFrame,
        ExtraDelay = ExtraDelay,
        Scale = Scale,
    };

    [RelayCommand]
    private void ChangeOutputFolder()
    {
        string? folder = _services.Dialogs.PickFolder(L.T("AppearanceExport.GIFOutputFolder"), OutputFolder);
        if (folder == null) return;
        var state = OutputLocations.Prepare(folder);
        if (!state.Usable)
        {
            _services.Dialogs.Show(L.F("AppearanceExport.0CannotBeUsed1", folder, state.Problem), MessageKind.Warning, L.T("AppearanceExport.GIFOutputFolder"));
            return;
        }
        Persist(s => s.GifFolder = state.Path);
        RefreshFolder();
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (Directory.Exists(OutputFolder)) _services.RevealSaved(new StatusLink(OutputFolder, IsFile: false));
        else _services.Status.Show(L.F("AppearanceExport.0DoesNotExistYetIt", OutputFolder), StatusKind.Info);
    }

    /// <summary>
    /// "Save as GIF". One appearance without auto-naming asks for a file; several appearances are
    /// always named automatically (as the original forced) into the GIF folder, never replacing an
    /// existing file. The status names the saved file (or folder) only after the GIFs were written,
    /// as a clickable path; when nothing could be saved it shows an error instead.
    /// </summary>
    [RelayCommand]
    private async Task SaveGifAsync()
    {
        var items = _owner.TargetItems;
        var sprites = _services.Data.Sprites;
        if (items.Count == 0 || sprites == null) { _services.Dialogs.Show(L.T("AppearanceExport.SelectAtLeastOneAppearance")); return; }
        var options = BuildOptions();
        var attribution = Attribution.ForNewAsset(); // one attribution for the whole batch
        bool auto = AutoName || items.Count > 1;
        string? singleTarget = null;
        string? folder;
        if (!auto)
        {
            string suggested = AppearanceGifBuilder.SuggestedName(items[0].Appearance, items[0].Kind, options);
            singleTarget = _services.Outputs.SaveFile(OutputKind.GifExports, L.T("AppearanceExport.SaveGIF"), L.T("AppearanceExport.GIFImagesGifGif"), FileNames.Sanitize(suggested) + ".gif");
            if (singleTarget == null) return;
            folder = Path.GetDirectoryName(singleTarget);
            RefreshFolder();
        }
        else
        {
            folder = _services.Outputs.PrepareOrAsk(OutputKind.GifExports, L.T("AppearanceExport.ExportGIF"));
            if (folder == null) return;
        }
        var work = items.Select(i => (i.Appearance, i.Kind)).ToList();
        var outcome = new GifExportOutcome(work.Count);
        bool kindFolders = KindFolders && singleTarget == null;
        await _owner.RunExternalAsync(L.T(work.Count > 1 ? "AppearanceExport.ExportingGIFs" : "AppearanceExport.ExportingGIF"), async (progress, ct) =>
        {
            await Task.Run(() =>
            {
                for (int i = 0; i < work.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var (a, kind) = work[i];
                    try
                    {
                        var gif = AppearanceGifBuilder.Build(a, kind, options, sprites);
                        string target = kindFolders ? Directory.CreateDirectory(Path.Combine(folder!, GifExportPresets.KindFolder(kind))).FullName : folder!;
                        string path = singleTarget ?? FileNames.Unique(target, FileNames.Sanitize(gif.SuggestedName), ".gif");
                        GifWriter.Write(path, gif.Frames, gif.Delays, attribution);
                        if (!File.Exists(path)) throw new IOException($"{path} was not created.");
                        outcome.Saved.Add(path);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ImageMagick.MagickException)
                    {
                        outcome.Failures.Add($"{kind} {a.Id}: {ex.Message}");
                    }
                    progress.Report(new OperationProgress(i + 1, work.Count, $"{kind} {a.Id}"));
                }
            }, ct);
        }, operation: "export-gif", output: singleTarget ?? folder, onSuccess: () => ShowGifOutcome(outcome, folder!));
        if (outcome.Failures.Count > 0)
            _services.Dialogs.Show(L.F("AppearanceExport.0GIFSCouldNotBe", outcome.Failures.Count, string.Join("\n", outcome.Failures.Take(20))), MessageKind.Warning, L.T("AppearanceExport.ExportGIF"));
    }

    /// <summary>What a GIF export produced.</summary>
    public sealed class GifExportOutcome(int requested)
    {
        public int Requested { get; } = requested;
        public List<string> Saved { get; } = [];
        public List<string> Failures { get; } = [];
    }

    /// <summary>
    /// The status after a GIF export: "Saved to &lt;file&gt;" for one GIF, "Saved N GIFs to &lt;folder&gt;"
    /// for several (the path is clickable), a warning when some failed, an error when none was saved.
    /// </summary>
    public void ShowGifOutcome(GifExportOutcome outcome, string folder)
    {
        int saved = outcome.Saved.Count;
        if (saved == 0)
        {
            string reason = outcome.Failures.FirstOrDefault() is { } f ? f[(f.IndexOf(':') + 1)..].Trim() : L.T("AppearanceExport.UnknownReason");
            _services.Status.Show(outcome.Requested == 1 ? L.F("AppearanceExport.TheGIFCouldNotBeSaved", reason) : L.F("AppearanceExport.NoneOfThe0GIFsCould", outcome.Requested, reason), StatusKind.Error);
            return;
        }
        if (outcome.Requested == 1)
        {
            _services.Status.ShowSaved(outcome.Saved[0], isFile: true);
            return;
        }
        string target = outcome.Saved.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? Path.GetDirectoryName(outcome.Saved[0])! : folder;
        if (outcome.Failures.Count == 0)
            _services.Status.ShowSaved(target, isFile: false, before: L.F("AppearanceExport.Saved0N0GIFsTo", saved));
        else
            _services.Status.ShowSaved(target, isFile: false, before: L.F("AppearanceExport.Saved0N0Of1N0", saved, outcome.Requested), after: L.F("AppearanceExport.0N0FailedListedInThe", outcome.Failures.Count), kind: StatusKind.Warning);
    }

    // ------------------------------------------------------------------ outfit sheet
    [ObservableProperty] private SheetOutput _sheetOutput = SheetOutput.Base64;
    [ObservableProperty] private bool _sheetMagenta = true;

    /// <summary>"Generate Sheet" (outfits). Fixed: PNG File mode now writes the chosen file.</summary>
    [RelayCommand]
    private void GenerateSheet()
    {
        var item = _owner.SelectedItem;
        if (item == null || item.Kind != AppearanceKind.Outfit || _services.Data.Sprites == null)
        {
            _services.Dialogs.Show(L.T("AppearanceExport.SelectAnOutfitInTheOutfits"), MessageKind.Info, L.T("Common.OutfiterSheet"));
            return;
        }
        try
        {
            var sheet = SheetComposer.OutfitSheet(item.Appearance, _services.Data.Sprites, SheetMagenta);
            switch (SheetOutput)
            {
                case SheetOutput.PngFile:
                    string? file = _services.Outputs.SaveFile(OutputKind.ImageExports, L.T("AppearanceExport.SaveOutfiterSheet"), L.T("AppearanceExport.PNGImagesPngPng"), L.F("AppearanceExport.Outfit0Png", item.Id));
                    if (file == null) return;
                    ImageIO.SavePng(sheet, file);
                    _services.Status.ShowSaved(file, isFile: true);
                    break;
                case SheetOutput.PngClipboard:
                    _services.Clipboard.SetImage(sheet);
                    _services.Status.Show(L.T("AppearanceExport.PNGImageCopiedToTheClipboard"), StatusKind.Success);
                    break;
                default:
                    _services.Clipboard.SetText(ImageIO.ToPngDataUri(sheet));
                    _services.Status.Show(L.T("AppearanceExport.Base64DataURIOfThePNG"), StatusKind.Success);
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _services.Dialogs.ShowError(L.T("AppearanceExport.TheSheetCouldNotBeCreated"), ex);
        }
    }

    // ------------------------------------------------------------------ filtered data
    private List<Tibia.Protobuf.Appearances.Appearance> FilteredAppearances() => _owner.VisibleItems.Select(i => i.Appearance).ToList();

    [RelayCommand]
    private async Task ExportFilteredJsonAsync()
    {
        if (!_owner.IsLoaded) { _services.Dialogs.Show(L.T("AppearanceExport.LoadAnAppearancesFileFirst")); return; }
        string? file = _services.Outputs.SaveFile(OutputKind.DataExports, L.T("AppearanceExport.SaveListedAppearancesAsJSON"), L.T("AppearanceExport.JSONFilesJsonJson"), $"{_owner.Kind}s.json");
        if (file == null) return;
        var data = FilteredAppearances();
        await _owner.RunExternalAsync(L.T("AppearanceExport.ExportingJSON"), (_, ct) => JsonExport.WriteAsync(data, file, ct), operation: "export-json", output: file,
            onSuccess: () => _services.Status.ShowSaved(file, isFile: true, before: L.F("AppearanceExport.Saved0N0AppearancesTo", data.Count), after: ". " + Attribution.NoMetadataNote));
    }

    [RelayCommand]
    private void ExportFilteredTable()
    {
        if (!_owner.IsLoaded) { _services.Dialogs.Show(L.T("AppearanceExport.LoadAnAppearancesFileFirst")); return; }
        string? file = _services.Outputs.SaveFile(OutputKind.DataExports, L.T("AppearanceExport.SaveIDAndNameTable2"), L.T("AppearanceExport.TextFilesTxtTxt"), $"{_owner.Kind}s.txt");
        if (file == null) return;
        try
        {
            AtomicFile.WriteAllText(file, JsonExport.IdNameTable(FilteredAppearances()));
            _services.Status.ShowSaved(file, isFile: true, after: ". " + Attribution.NoMetadataNote);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Dialogs.ShowError(L.T("AppearanceExport.TheTableCouldNotBeSaved"), ex);
        }
    }

    [RelayCommand]
    private void CopyFilteredTable()
    {
        if (!_owner.IsLoaded) { _services.Dialogs.Show(L.T("AppearanceExport.LoadAnAppearancesFileFirst")); return; }
        if (_services.Clipboard.SetText(JsonExport.IdNameTable(FilteredAppearances())))
            _services.Status.Show(L.F("AppearanceExport.0N0RowsCopiedToThe", _owner.VisibleCount), StatusKind.Success);
    }

    // ------------------------------------------------------------------ tools (NPC trades)
    [ObservableProperty] private IReadOnlyList<string> _npcNames = Array.Empty<string>();
    [ObservableProperty] private string _npcName = "";
    [ObservableProperty] private NpcTradeSort _npcSort = NpcTradeSort.ByName;
    [ObservableProperty] private IReadOnlyList<string> _currencies = Array.Empty<string>();
    [ObservableProperty] private string _wikiItemsUrl = WikiItemIdTable.DefaultUrl;

    /// <summary>"Create (LUA format)" for one NPC → clipboard.</summary>
    [RelayCommand]
    private void CreateNpcTradeList()
    {
        var db = _owner.Database;
        if (db == null) { _services.Dialogs.Show(L.T("AppearanceExport.LoadAnAppearancesFileFirst")); return; }
        if (string.IsNullOrWhiteSpace(NpcName)) { _services.Dialogs.Show(L.T("AppearanceExport.TypeOrPickAnNPCName"), MessageKind.Info, L.T("AppearanceExport.NPCTrades")); return; }
        var list = NpcTradeExporter.ForNpc(db, NpcName.Trim(), NpcSort);
        if (list.Buys == 0 && list.Sells == 0)
        {
            _services.Dialogs.Show(L.F("AppearanceExport.NoTradeDataWasFoundFor", NpcName), MessageKind.Info, L.T("AppearanceExport.NPCTrades"));
            return;
        }
        _services.Clipboard.SetText(list.Lua);
        string message = L.F("AppearanceExport.TradeListCopiedToTheClipboard", list.Buys, list.Sells);
        if (list.Warnings.Count > 0) _services.Dialogs.Show(message + "\n\n" + string.Join("\n", list.Warnings), MessageKind.Warning, L.T("AppearanceExport.NPCTrades"));
        else _services.Status.Show(message, StatusKind.Success);
    }

    /// <summary>"Generate Lua Data for all NPCs": downloads TibiaWiki item names, writes the module and a log.</summary>
    [RelayCommand]
    private async Task GenerateAllNpcTradesAsync()
    {
        var db = _owner.Database;
        if (db == null) { _services.Dialogs.Show(L.T("AppearanceExport.LoadAnAppearancesFileFirst")); return; }
        string? file = _services.Outputs.SaveFile(OutputKind.DataExports, L.T("AppearanceExport.SaveLuaModule"), L.T("AppearanceExport.LuaFilesLuaLua"), "ItemsNPCTrades.lua");
        if (file == null) return;
        string url = WikiItemsUrl;
        var attribution = Attribution.ForNewAsset();
        string? problem = null;
        string? notice = null;
        bool ok = await _owner.RunExternalAsync(L.T("AppearanceExport.GeneratingNPCTradeData"), async (progress, ct) =>
        {
            WikiItemIds? wiki = null;
            try
            {
                progress.Report(new OperationProgress(0, 0, L.T("AppearanceExport.DownloadingTheItemIDListFrom")));
                // Cached for 24 h; read through the documented API (the item articles, then the page),
                // then the render page; a stale copy is used (and announced) when TibiaWiki cannot be reached.
                wiki = await WikiItemIdTable.GetAsync(_services.Http, _services.CacheDirectory, url, forceRefresh: false, progress: progress, ct: ct);
                notice = wiki.Notice;
            }
            catch (Exception ex) when (ex is HttpFetchException or InvalidDataException)
            {
                problem = ex.Message;
            }
            var module = await Task.Run(() => NpcTradeExporter.AllNpcs(db, wiki?.Names, wiki?.Log), ct);
            string log = module.Log;
            if (problem != null) log = L.F("AppearanceExport.TibiaWikiItemIDsCouldNotBe", problem) + log;
            else if (notice != null) log = notice + "\n" + log;
            else if (wiki != null) log = L.F("AppearanceExport.TibiaWikiItemIDs0N0Source", wiki.Names.Count, wiki.Source, wiki.FetchedAtUtc.ToLocalTime()) + log;
            // The generated module carries the attribution as a Lua comment (the code itself is unchanged).
            await AtomicFile.WriteAllTextAsync(file, TextAttribution.LuaHeader(attribution) + module.Lua, ct);
            await AtomicFile.WriteAllTextAsync(file + ".log", log, ct);
        }, operation: "npc-trades", output: file, onSuccess: () =>
        {
            if (problem != null) _services.Status.ShowSaved(file, isFile: true, after: L.T("AppearanceExport.WithClientItemNamesTibiaWikiUnavailable"), kind: StatusKind.Warning);
            else if (notice != null) _services.Status.ShowSaved(file, isFile: true, after: L.T("AppearanceExport.UsingACachedTibiaWikiItemList"), kind: StatusKind.Warning);
            else _services.Status.ShowSaved(file, isFile: true, after: L.F("AppearanceExport.Log0Log", Path.GetFileName(file)));
        });
        if (ok && problem != null)
            _services.Dialogs.Show(L.F("AppearanceExport.TheLuaModuleWasWrittenUsing", problem), MessageKind.Warning, L.T("AppearanceExport.NPCTrades"));
    }
}
