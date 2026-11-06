using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Compatibility;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Export;
using TibiaFoundry.Core.Sounds;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>"Sounds": sounds, effects, ambiences and music from sounds.dat, with playback and extraction.</summary>
public sealed partial class SoundsViewModel : PageViewModel
{
    private List<ListItem> _items = new();

    public SoundsViewModel(AppServices services) : base(services, BuiltInTools.Sounds)
    {
        // Another client, file or folder in use: the next load uses the new default file.
        services.Data.ClientFilesChanged += types => { if (types == null || types.Contains("sounds")) DatPath = null; };
        services.Data.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ClientDataService.Sounds)) Rebuild();
        };
        services.Audio.PlaybackStateChanged += file =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => NowPlaying = file == null ? "" : Path.GetFileName(file));
        _autoplay = services.Settings.SoundAutoplay;
        _volume = services.Settings.SoundVolume * 100;
        _extractFolder = services.Outputs.FolderFor(OutputKind.SoundExports);
    }

    public override IReadOnlyList<string> DataComponents { get; } = [TibiaFormat.CatalogTypeSounds];


    [ObservableProperty] private string? _datPath;
    [ObservableProperty] private SoundListKind _kind = SoundListKind.Sounds;
    [ObservableProperty] private IReadOnlyList<ListItem> _visibleItems = Array.Empty<ListItem>();
    [ObservableProperty] private ListItem? _selectedItem;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private IReadOnlyList<PropertyRowViewModel> _properties = Array.Empty<PropertyRowViewModel>();
    [ObservableProperty] private bool _autoplay;
    [ObservableProperty] private double _volume;
    [ObservableProperty] private string _nowPlaying = "";
    [ObservableProperty] private string _extractFolder;

    public bool IsLoaded => Services.Data.Sounds != null;
    public int VisibleCount => VisibleItems.Count;

    partial void OnKindChanged(SoundListKind value) => Rebuild();
    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnAutoplayChanged(bool value)
    {
        Services.Settings.SoundAutoplay = value;
        Services.SaveSettings();
    }

    partial void OnVolumeChanged(double value)
    {
        Services.Audio.Volume = (float)(value / 100);
        Services.Settings.SoundVolume = value / 100;
    }

    partial void OnSelectedItemChanged(ListItem? value)
    {
        var db = Services.Data.Sounds;
        if (value == null || db == null) { Properties = Array.Empty<PropertyRowViewModel>(); return; }
        Properties = PropertyRowViewModel.From(db.Rows((IMessage)value.Source), Services, PlaySound);
        Services.Audio.Stop();
        if (Autoplay) PlaySelected();
    }

    public override async Task OnNavigatedToAsync()
    {
        DatPath ??= Services.Data.DefaultFile("sounds");
        if (Services.Data.Sounds == null && Services.Settings.LoadDataAutomatically && DatPath != null && !IsBusy) await LoadAsync();
        else if (_items.Count == 0) Rebuild();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        DatPath ??= Services.Data.DefaultFile("sounds");
        if (DatPath == null || !File.Exists(DatPath)) { Services.Dialogs.Show(L.T("Sounds.NoSoundsFileWasFoundIn"), MessageKind.Warning, L.T("Sounds.Sounds")); return; }
        string path = DatPath;
        await RunAsync(L.T("Sounds.LoadingSounds"), async (_, _) => await Services.Data.LoadSoundsAsync(path), L.F("Sounds.Loaded0", Path.GetFileName(path)));
    }

    [RelayCommand]
    private async Task ChangeFileAsync()
    {
        string? file = Services.Dialogs.OpenFile(L.T("Sounds.SelectASoundsFile"), L.T("Sounds.TibiaSoundsSoundsDatDatAll"), Services.Data.SoundsDirectory);
        if (file == null) return;
        await OfferAsDefaultAsync("sounds", file);
        DatPath = file;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        var db = Services.Data.Sounds;
        if (db == null) { Services.Dialogs.Show(L.T("Sounds.LoadTheSoundsFirst")); return; }
        string? file = Services.Outputs.SaveFile(OutputKind.DataExports, L.T("Sounds.ExportSoundsAsJSON"), L.T("Sounds.JSONFilesJsonJson"), "Sounds.json");
        if (file == null) return;
        await RunAsync(L.T("Sounds.ExportingJSON"), (_, ct) => JsonExport.WriteAsync(db.Data, file, ct), output: file, onSuccess: () => Saved(file, ". " + Attribution.NoMetadataNote));
    }

    private void Rebuild()
    {
        OnPropertyChanged(nameof(IsLoaded));
        var db = Services.Data.Sounds;
        _items = db == null ? new() : db.Items(Kind).Select(i => new ListItem(i.Id.ToString(), i.Detail, i.Source)).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var previous = SelectedItem;
        VisibleItems = _items.Where(i => i.Matches(FilterText.Trim())).ToList();
        SelectedItem = previous != null && VisibleItems.Contains(previous) ? previous : (VisibleItems.Count > 0 ? VisibleItems[0] : null);
        OnPropertyChanged(nameof(VisibleCount));
    }

    /// <summary>Plays the sound behind the selected row (for effects: the first variant).</summary>
    [RelayCommand]
    private void PlaySelected()
    {
        var db = Services.Data.Sounds;
        if (db == null || SelectedItem == null) return;
        var item = db.Items(Kind).FirstOrDefault(i => ReferenceEquals(i.Source, SelectedItem.Source));
        if (item?.SoundId is uint id) PlaySound(id);
        else Services.Status.Show(L.T("Sounds.ThisEntryHasNoSoundTo"), StatusKind.Info);
    }

    [RelayCommand]
    private void Stop() => Services.Audio.Stop();

    private void PlaySound(uint soundId)
    {
        var db = Services.Data.Sounds;
        string? file = db?.ResolvePlayableFile(soundId, ExtractFolder);
        if (file == null)
        {
            Services.Status.Show(L.F("Sounds.Sound0WasFoundNeitherIn", soundId), StatusKind.Warning);
            return;
        }
        try
        {
            Services.Audio.Play(file);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NAudio.MmException or FormatException or InvalidDataException or NotSupportedException or ArgumentException or UnauthorizedAccessException)
        {
            Services.Status.Show(L.F("Sounds.Sound0CouldNotBePlayed", soundId, ex.Message), StatusKind.Error);
        }
    }

    [RelayCommand]
    private void ChangeExtractFolder()
    {
        if (Services.Dialogs.PickFolder(L.T("Sounds.FolderForExtractedSounds"), ExtractFolder) is not { } folder) return;
        if (Core.Editing.AssetsProject.IsInsideClientInstallation(folder))
        {
            Services.Dialogs.Show(L.T("Sounds.FoldersOfTheInstalledTibiaClient"), MessageKind.Warning, L.T("Sounds.ExtractedSounds"));
            return;
        }
        ExtractFolder = folder;
        Services.Settings.SoundsFolder = folder;
        Services.SaveSettings();
    }

    [RelayCommand] private void OpenExtractFolder() => Services.Shell.OpenFolder(ExtractFolder);

    [RelayCommand]
    private async Task ExtractAsync()
    {
        var db = Services.Data.Sounds;
        if (db == null) { Services.Dialogs.Show(L.T("Sounds.LoadTheSoundsFirst")); return; }
        string folder = ExtractFolder;
        if (OutputLocations.Prepare(folder) is { Usable: false } state)
        {
            Services.Dialogs.Show(L.F("Sounds.TheSoundsFolderCannotBeUsed", state.Problem), MessageKind.Warning, L.T("Sounds.ExtractingSounds"));
            return;
        }
        BatchResult? result = null;
        await RunAsync(L.T("Sounds.ExtractingSounds"), async (p, ct) => result = await db.ExtractAsync(folder, p, ct), operation: "extract", output: folder,
            onSuccess: () => ReportBatch(result!, L.T("Sounds.SoundFiles"), folder, L.T("Sounds.ExtractingSounds")));
    }
}
