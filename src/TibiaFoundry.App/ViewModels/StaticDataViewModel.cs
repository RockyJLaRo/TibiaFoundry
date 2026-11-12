using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Compatibility;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Export;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.StaticData;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>
/// "Static Data": monsters, bosses, achievements, houses and quests from staticdata.dat, with
/// animated creature previews, outfitter hand-off and GIF export.
/// </summary>
public sealed partial class StaticDataViewModel : PageViewModel
{
    private List<ListItem> _items = new();

    public StaticDataViewModel(AppServices services) : base(services, BuiltInTools.StaticData)
    {
        // Another client, file or folder in use: the next load uses the new default file.
        services.Data.ClientFilesChanged += types => { if (types == null || types.Contains(AssetTypes.StaticData)) DatPath = null; };
        services.Data.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ClientDataService.StaticData) or nameof(ClientDataService.Appearances) or nameof(ClientDataService.Sprites)) Rebuild();
        };
    }

    public override IReadOnlyList<string> DataComponents { get; } = [CompatibilityChecker.SpriteCatalogComponent, TibiaFormat.CatalogTypeAppearances, TibiaFormat.CatalogTypeStaticData];


    [ObservableProperty] private string? _datPath;
    [ObservableProperty] private StaticDataKind _kind = StaticDataKind.Monsters;
    [ObservableProperty] private IReadOnlyList<ListItem> _visibleItems = Array.Empty<ListItem>();
    [ObservableProperty] private ListItem? _selectedItem;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _spriteFilter = "";
    [ObservableProperty] private bool _renderOutfits;
    [ObservableProperty] private IReadOnlyList<PropertyRowViewModel> _properties = Array.Empty<PropertyRowViewModel>();
    [ObservableProperty] private IReadOnlyList<BitmapSource> _frames = Array.Empty<BitmapSource>();
    [ObservableProperty] private IReadOnlyList<int> _delays = Array.Empty<int>();
    [ObservableProperty] private bool _animate = true;
    [ObservableProperty] private bool _forceDelay;
    [ObservableProperty] private int _forcedDelayMs = 100;

    /// <summary>Delay passed to the preview: the forced delay when enabled, otherwise 0 (use the sprite's own delays).</summary>
    public int EffectiveDelay => ForceDelay ? ForcedDelayMs : 0;

    partial void OnForceDelayChanged(bool value) => OnPropertyChanged(nameof(EffectiveDelay));

    partial void OnForcedDelayMsChanged(int value) => OnPropertyChanged(nameof(EffectiveDelay));
    [ObservableProperty] private string _previewMessage = "";
    [ObservableProperty] private PreviewBackground _previewBackground = PreviewBackground.Checkerboard;

    public bool IsLoaded => Services.Data.StaticData != null;
    public bool IsCreatureList => Kind is StaticDataKind.Monsters or StaticDataKind.Bosses;
    public int TotalCount => _items.Count;
    public int VisibleCount => VisibleItems.Count;

    /// <summary>The rows selected in the list (the ListView's multi-selection, mirrored by the view).</summary>
    [ObservableProperty] private IReadOnlyList<ListItem> _selectedItems = Array.Empty<ListItem>();

    /// <summary>Asks the view to select these rows (Select all).</summary>
    public event Action<IReadOnlyList<ListItem>>? SelectRequested;

    /// <summary>The creatures an export applies to: the selected rows, or the current row.</summary>
    public IReadOnlyList<Creature> TargetCreatures =>
        (SelectedItems.Count > 0 ? SelectedItems : SelectedItem == null ? [] : [SelectedItem]).Select(i => i.Source).OfType<Creature>().ToList();

    public int SelectedCount => TargetCreatures.Count;

    partial void OnSelectedItemsChanged(IReadOnlyList<ListItem> value) => OnPropertyChanged(nameof(SelectedCount));

    partial void OnKindChanged(StaticDataKind value) => Rebuild();
    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnSpriteFilterChanged(string value) => ApplyFilter();
    partial void OnRenderOutfitsChanged(bool value) => Rebuild();
    partial void OnSelectedItemChanged(ListItem? value)
    {
        OnPropertyChanged(nameof(SelectedCount));
        _ = UpdateDetailsAsync();
    }

    public override async Task OnNavigatedToAsync()
    {
        DatPath ??= Services.Data.DefaultFile(AssetTypes.StaticData);
        if (Services.Data.StaticData == null && Services.Settings.LoadDataAutomatically && DatPath != null && !IsBusy) await LoadAsync();
        else if (_items.Count == 0) Rebuild();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        DatPath ??= Services.Data.DefaultFile(AssetTypes.StaticData);
        if (DatPath == null || !File.Exists(DatPath))
        {
            Services.Dialogs.Show(L.T("StaticData.NoStaticdataFileWasFoundPick"), MessageKind.Warning, L.T("StaticData.StaticData"));
            return;
        }
        string path = DatPath;
        await RunAsync(L.T("StaticData.LoadingStaticData"), async (_, _) =>
        {
            await Services.Data.EnsureAppearancesAsync();
            await Services.Data.LoadStaticDataAsync(path);
        }, L.F("StaticData.Loaded0", Path.GetFileName(path)));
    }

    /// <summary>Original "Clear Filters".</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        FilterText = "";
        SpriteFilter = "";
    }

    [RelayCommand]
    private async Task ChangeFileAsync()
    {
        string? file = Services.Dialogs.OpenFile(L.T("StaticData.SelectAStaticdataFile"), L.T("StaticData.TibiaStaticDataDatDatAll"), Services.Data.AssetsDirectory);
        if (file == null) return;
        await OfferAsDefaultAsync(AssetTypes.StaticData, file);
        DatPath = file;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        var db = Services.Data.StaticData;
        if (db == null) { Services.Dialogs.Show(L.T("StaticData.LoadTheStaticDataFirst")); return; }
        string? file = Services.Outputs.SaveFile(OutputKind.DataExports, L.T("StaticData.ExportStaticDataAsJSON"), L.T("StaticData.JSONFilesJsonJson"), "StaticData.json");
        if (file == null) return;
        await RunAsync(L.T("StaticData.ExportingJSON"), (_, ct) => JsonExport.WriteAsync(db.Data, file, ct), operation: "export-json", output: file, onSuccess: () => Saved(file, ". " + Attribution.NoMetadataNote));
    }

    private void Rebuild()
    {
        var db = Services.Data.StaticData;
        var appearances = Services.Data.Appearances;
        var sprites = Services.Data.Sprites;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(IsCreatureList));
        if (db == null) { _items = new(); ApplyFilter(); return; }
        bool render = RenderOutfits;
        Func<RgbaImage?>? Thumb(Creature c) => appearances == null || sprites == null ? null : () => CreatureImages.Thumbnail(c, appearances, sprites, render);
        _items = Kind switch
        {
            // 8.8.2: creature names in TibiaWiki article casing ("demon lord" → "Demon Lord"), as their GIFs are named.
            StaticDataKind.Monsters => db.Monsters.Select(c => new ListItem(c.Race.ToString(), CreatureGifExport.DisplayName(c), c, "", Thumb(c))).ToList(),
            StaticDataKind.Bosses => db.Bosses.Select(c => new ListItem(c.Race.ToString(), CreatureGifExport.DisplayName(c), c, "", Thumb(c))).ToList(),
            StaticDataKind.Achievements => db.Data.Achievements.Select(a => new ListItem(a.AchievementId.ToString(), a.Name, a, L.F("StaticData.Grade0", a.Grade))).ToList(),
            StaticDataKind.Houses => db.Data.Houses.Select(h => new ListItem(h.HouseId.ToString(), h.Name, h, h.City)).ToList(),
            _ => db.Data.Quests.Select(q => new ListItem(q.QuestId.ToString(), q.Name, q)).ToList(),
        };
        OnPropertyChanged(nameof(TotalCount));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string text = FilterText.Trim();
        IEnumerable<ListItem> items = _items.Where(i => i.Matches(text));
        if (IsCreatureList && uint.TryParse(SpriteFilter.Trim(), out uint spriteId) && Services.Data.Appearances is { } db)
            items = items.Where(i => i.Source is Creature c && CreatureUsesSprite(c, spriteId, db));
        var previous = SelectedItem;
        VisibleItems = items.ToList();
        SelectedItem = previous != null && VisibleItems.Contains(previous) ? previous : (VisibleItems.Count > 0 ? VisibleItems[0] : null);
        OnPropertyChanged(nameof(VisibleCount));
    }

    /// <summary>The original's Static Data "Sprite ID" filter had no handler; this implements it.</summary>
    private static bool CreatureUsesSprite(Creature c, uint spriteId, AppearanceDatabase db)
    {
        if (c.Outfit == null) return false;
        var appearance = c.Outfit.OutfitId != 0 ? db.Find(AppearanceKind.Outfit, c.Outfit.OutfitId) : db.Find(AppearanceKind.Object, c.Outfit.ObjectAppearanceTypeId);
        return appearance != null && AppearanceFilter.UsesSprite(spriteId).Matches(appearance);
    }

    private int _version;

    private async Task UpdateDetailsAsync()
    {
        int version = ++_version;
        var item = SelectedItem;
        Frames = Array.Empty<BitmapSource>();
        PreviewMessage = "";
        if (item == null) { Properties = Array.Empty<PropertyRowViewModel>(); return; }
        Properties = PropertyRowViewModel.From(StaticDataDatabase.Rows((IMessage)(item.Source is Creature cr ? cr.Source : item.Source)), Services);
        if (item.Source is not Creature creature) return;
        var appearances = Services.Data.Appearances;
        var sprites = Services.Data.Sprites;
        if (appearances == null || sprites == null) { PreviewMessage = L.T("StaticData.LoadAppearancesToSeeTheCreature"); return; }
        try
        {
            var animation = await Task.Run(() => CreatureImages.Animate(creature, appearances, sprites));
            if (version != _version) return;
            if (animation == null) { PreviewMessage = L.T("StaticData.ThisCreatureHasNoOutfit"); return; }
            Frames = Images.ToBitmapSources(animation.Frames);
            Delays = animation.Delays;
        }
        catch (OutfitRenderException ex)
        {
            if (version == _version) PreviewMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void CycleBackground() => PreviewBackground = (PreviewBackground)(((int)PreviewBackground + 1) % Enum.GetValues<PreviewBackground>().Length);

    /// <summary>"Outfitter": opens the creature's outfit with its colors and addons in the Appearances outfitter.</summary>
    /// <summary>Content Studio: a creature gallery project with the creatures the list shows (after filters).</summary>
    [RelayCommand]
    private void CreateGallery()
    {
        var names = VisibleItems.Select(i => i.Source).OfType<Creature>().Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0)
        {
            Services.Dialogs.Show(L.T("StaticData.LoadTheStaticDataAndShow"), MessageKind.Info, L.T("StaticData.CreatureGallery"));
            return;
        }
        if (names.Count > 1 && !Services.Dialogs.Confirm(L.F("StaticData.CreateACreatureGalleryProjectWith", L.Count(names.Count, "Noun.creature")), L.T("StaticData.CreatureGallery"), L.T("StaticData.CreateProject"), L.T("Common.Cancel"))) return;
        Services.RequestContentStudio(studio => studio.CreateCreatureGallery(names));
    }

    [RelayCommand]
    private void OpenInOutfitter()
    {
        if (SelectedItem?.Source is not Creature { Outfit: { } outfit })
        {
            Services.Dialogs.Show(L.T("StaticData.OnlyMonstersAndBossesCanBe"), MessageKind.Info, L.T("StaticData.Outfitter"));
            return;
        }
        if (outfit.OutfitId == 0)
        {
            Services.Dialogs.Show(L.T("StaticData.ThisCreatureLooksLikeAnObject"), MessageKind.Info, L.T("StaticData.Outfitter"));
            return;
        }
        Services.RequestOpenOutfitter(outfit.OutfitId, StaticDataDatabase.ColorsOf(outfit), outfit.Addons);
    }

    /// <summary>Selects every row the list shows (Ctrl+A in the list does the same).</summary>
    [RelayCommand]
    private void SelectAll()
    {
        SelectedItems = VisibleItems;
        SelectRequested?.Invoke(VisibleItems);
    }

    /// <summary>
    /// "Export GIF…": one creature asks for the file (as before); several selected creatures are saved
    /// at once into the GIF folder, named after the creatures (8.8.2).
    /// </summary>
    [RelayCommand]
    private async Task ExportGifAsync()
    {
        var creatures = TargetCreatures;
        if (creatures.Count > 1)
        {
            await ExportCreatureGifsAsync(creatures.Select(c => (c, (string?)null)).ToList());
            return;
        }
        if (SelectedItem?.Source is not Creature creature || Frames.Count == 0)
        {
            Services.Dialogs.Show(L.T("StaticData.SelectAMonsterOrBossWith"), MessageKind.Info, L.T("StaticData.ExportGIF2"));
            return;
        }
        string name = CreatureGifExport.DisplayName(creature);
        string? file = Services.Outputs.SaveFile(OutputKind.GifExports, L.T("StaticData.SaveCreatureGIF"), L.T("StaticData.GIFImagesGifGif"), FileNames.Sanitize(name) + ".gif");
        if (file == null) return;
        if (!Try(L.T("StaticData.SavingTheGIF"), () =>
            {
                GifWriter.Write(file, Frames.Select(Images.FromBitmapSource).ToList(), ForceDelay ? new[] { ForcedDelayMs } : Delays);
                Saved(file);
            }))
            Services.Status.Show(L.T("StaticData.TheGIFCouldNotBeSaved"), StatusKind.Error);
    }

    /// <summary>"Export all monsters and bosses…": every creature of the file, into "Monsters" and "Bosses" folders of the GIF folder (8.8.2).</summary>
    [RelayCommand]
    private async Task ExportAllCreatureGifsAsync()
    {
        var db = Services.Data.StaticData;
        if (db == null) { Services.Dialogs.Show(L.T("StaticData.LoadTheStaticDataFirst")); return; }
        // The folder names are written to disk, so they stay English in every language.
        var all = db.Monsters.Select(c => (c, (string?)"Monsters")).Concat(db.Bosses.Select(c => (c, (string?)"Bosses"))).ToList(); // loc: identity
        await ExportCreatureGifsAsync(all);
    }

    /// <summary>Saves the GIFs of many creatures into the GIF folder (or a sub-folder of it), never replacing a file.</summary>
    private async Task ExportCreatureGifsAsync(IReadOnlyList<(Creature Creature, string? Subfolder)> creatures)
    {
        var appearances = await Services.Data.EnsureAppearancesAsync();
        var sprites = Services.Data.Sprites;
        if (appearances == null || sprites == null)
        {
            Services.Dialogs.Show(L.T("StaticData.LoadAppearancesToSeeTheCreature"), MessageKind.Info, L.T("StaticData.ExportGIF2"));
            return;
        }
        if (creatures.Count == 0) return;
        string? folder = Services.Outputs.PrepareOrAsk(OutputKind.GifExports, L.T("StaticData.ExportGIF2"));
        if (folder == null) return;
        if (!Services.Dialogs.Confirm(L.F("StaticData.Export0CreatureGIFsInto1", creatures.Count, folder), L.T("StaticData.ExportGIF2"), L.T("StaticData.ExportGIF2"), L.T("Common.Cancel"))) return;
        int? delay = ForceDelay ? ForcedDelayMs : null;
        var attribution = Attribution.ForNewAsset(); // one attribution for the whole batch
        var saved = new List<string>();
        BatchResult? result = null;
        string outputFolder = folder;
        await RunAsync(L.T("AppearanceExport.ExportingGIFs"), async (progress, ct) =>
        {
            result = await Task.Run(() =>
            {
                var jobs = CreatureGifExport.Plan(creatures.Select(c => (c.Creature, c.Subfolder == null ? outputFolder : Path.Combine(outputFolder, c.Subfolder))));
                return CreatureGifExport.Export(jobs, appearances, sprites, delay, attribution, Services.Settings.EffectiveWorkerThreads, saved, progress, ct);
            }, ct);
        }, operation: "creature-gifs", output: folder, onSuccess: () => ShowGifOutcome(creatures.Count, saved, result!, outputFolder));
        if (result is { HasFailures: true } r)
            Services.Dialogs.Show(L.F("AppearanceExport.0GIFSCouldNotBe", r.Failures.Count, string.Join("\n", r.Failures.Take(20))), MessageKind.Warning, L.T("StaticData.ExportGIF2"));
    }

    /// <summary>The status after a batch: the folder as a clickable link (so the GIFs are added to the Asset Library like every export).</summary>
    private void ShowGifOutcome(int requested, IReadOnlyList<string> saved, BatchResult result, string folder)
    {
        if (saved.Count == 0)
        {
            string reason = result.Failures.FirstOrDefault() is { } f ? f[(f.IndexOf(':') + 1)..].Trim() : L.T("AppearanceExport.UnknownReason");
            Services.Status.Show(L.F("AppearanceExport.NoneOfThe0GIFsCould", requested, reason), StatusKind.Error);
            return;
        }
        if (!result.HasFailures)
            Services.Status.ShowSaved(folder, isFile: false, before: L.F("AppearanceExport.Saved0N0GIFsTo", saved.Count));
        else
            Services.Status.ShowSaved(folder, isFile: false, before: L.F("AppearanceExport.Saved0N0Of1N0", saved.Count, requested),
                after: L.F("AppearanceExport.0N0FailedListedInThe", result.Failures.Count), kind: StatusKind.Warning);
    }
}
