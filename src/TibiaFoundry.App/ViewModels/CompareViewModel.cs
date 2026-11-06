using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Common;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Compare;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>A list entry of a comparison result.</summary>
public sealed record CompareEntry(string Key, string State);

/// <summary>"Compare DAT files": differences between two client versions (original CompareDats window).</summary>
public sealed partial class CompareViewModel : PageViewModel
{
    public CompareViewModel(AppServices services) : base(services, BuiltInTools.Compare)
    {
        // The client's own file follows a client switch; a file you chose stays.
        services.Data.ClientFilesChanged += types =>
        {
            if (_newFromClient && (types == null || types.Contains(AssetTypes.Appearances))) { NewFile = ""; _newFromClient = false; }
        };
    }

    /// <summary>True while <see cref="NewFile"/> is the client's appearances file (not one you chose).</summary>
    private bool _newFromClient;


    [ObservableProperty] private string _oldFile = "";
    [ObservableProperty] private string _newFile = "";
    [ObservableProperty] private bool _ignoreSpriteIds = true;
    [ObservableProperty] private IReadOnlyList<CompareCategory> _categories = Array.Empty<CompareCategory>();
    [ObservableProperty] private CompareCategory? _selectedCategory;
    [ObservableProperty] private IReadOnlyList<CompareEntry> _entries = Array.Empty<CompareEntry>();
    [ObservableProperty] private CompareEntry? _selectedEntry;
    [ObservableProperty] private string _changesText = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private bool _hasResult;

    private CompareResult? _result;

    public override Task OnNavigatedToAsync()
    {
        if (string.IsNullOrEmpty(NewFile))
        {
            NewFile = Services.Data.DefaultFile(AssetTypes.Appearances) ?? "";
            _newFromClient = NewFile.Length > 0;
        }
        return Task.CompletedTask;
    }

    partial void OnSelectedCategoryChanged(CompareCategory? value)
    {
        Entries = value == null ? Array.Empty<CompareEntry>() :
            value.Changed.Select(k => new CompareEntry(k, "Changed"))
                 .Concat(value.Added.Select(k => new CompareEntry(k, "New")))
                 .Concat(value.Removed.Select(k => new CompareEntry(k, "Removed"))).ToList();
        SelectedEntry = Entries.FirstOrDefault();
    }

    partial void OnSelectedEntryChanged(CompareEntry? value)
    {
        if (value == null || SelectedCategory == null) { ChangesText = ""; return; }
        ChangesText = value.State switch
        {
            "Changed" => SelectedCategory.Changes.GetValueOrDefault(value.Key) ?? "",
            "New" => L.T("Compare.NotPresentInTheOldFile"),
            _ => L.T("Compare.NotPresentInTheNewFile"),
        };
    }

    /// <summary>Content Studio: render what is new or changed in the client in use since the older appearances file (a visual comparison gallery).</summary>
    [RelayCommand]
    private void VisualGallery()
    {
        string old = OldFile;
        if (old.Length == 0 || !File.Exists(old) || !Path.GetFileName(old).StartsWith("appearances", StringComparison.OrdinalIgnoreCase))
        {
            Services.Dialogs.Show("Choose an older appearances-*.dat as the old file. The gallery shows every object and outfit that is new or changed in the client in use since then.", MessageKind.Info, L.T("Compare.VisualGalleryOfChanges"));
            return;
        }
        Services.RequestContentStudio(studio => studio.CreateUpdateProject(old));
    }

    [RelayCommand] private void BrowseOld() { if (Pick(L.T("Compare.OldFile")) is { } f) OldFile = f; }
    [RelayCommand] private void BrowseNew() { if (Pick(L.T("Compare.NewFile")) is { } f) { NewFile = f; _newFromClient = false; } }

    private string? Pick(string title) =>
        Services.Dialogs.OpenFile(title, L.T("Compare.TibiaDataFilesDatDatBak"), Services.Data.AssetsDirectory);

    [RelayCommand]
    private void Swap()
    {
        (OldFile, NewFile) = (NewFile, OldFile);
        _newFromClient = false;
    }

    [RelayCommand]
    private async Task CompareFilesAsync()
    {
        if (OldFile.Length == 0 || NewFile.Length == 0) { Services.Dialogs.Show(L.T("Compare.SelectBothAnOldAndA"), MessageKind.Info, L.T("Common.Compare")); return; }
        if (DatComparer.DetectType(OldFile) != DatComparer.DetectType(NewFile) || DatComparer.DetectType(OldFile) == DatFileType.Unknown)
        {
            Services.Dialogs.Show(L.T("Compare.SelectTwoFilesOfTheSame"), MessageKind.Warning, L.T("Common.Compare"));
            return;
        }
        string a = OldFile, b = NewFile;
        bool ignore = IgnoreSpriteIds;
        await RunAsync(L.T("Compare.ComparingFiles"), async (p, ct) => Show(await DatComparer.CompareFilesAsync(a, b, ignore, p, ct)));
    }

    /// <summary>Compares every data file found in the folders of the two selected files.</summary>
    [RelayCommand]
    private async Task CompareFoldersAsync()
    {
        string? oldFolder = OldFile.Length > 0 ? Path.GetDirectoryName(OldFile) : Services.Dialogs.PickFolder(L.T("Compare.FolderWithTheOlderFiles"));
        string? newFolder = NewFile.Length > 0 ? Path.GetDirectoryName(NewFile) : Services.Dialogs.PickFolder(L.T("Compare.FolderWithTheNewerFiles"));
        if (oldFolder == null || newFolder == null) return;
        bool ignore = IgnoreSpriteIds;
        await RunAsync(L.T("Compare.ComparingFolders"), async (p, ct) => Show(await DatComparer.CompareFoldersAsync(oldFolder, newFolder, ignore, p, ct)));
    }

    private void Show(CompareResult result)
    {
        _result = result;
        Categories = result.WithDifferences.ToList();
        Notes = string.Join("\n", result.Notes);
        HasResult = true;
        SelectedCategory = Categories.Count > 0 ? Categories[0] : null;
        if (Categories.Count == 0) Services.Dialogs.Show(L.T("Compare.NoChangesBetweenTheFilesWere") + (Notes.Length > 0 ? "\n\n" + Notes : ""), MessageKind.Info, L.T("Common.Compare"));
    }

    /// <summary>Double-click: open the appearance in the viewer (as the original did).</summary>
    [RelayCommand]
    private void OpenEntry()
    {
        if (SelectedCategory?.AppearanceKind is not { } kind || SelectedEntry == null || SelectedEntry.State == "Removed") return;
        if (uint.TryParse(SelectedEntry.Key, out uint id)) Services.RequestOpenAppearance(kind, id);
    }

    [RelayCommand]
    private void SaveReport()
    {
        if (_result == null) return;
        string? file = Services.Outputs.SaveFile(OutputKind.Reports, L.T("Compare.SaveComparisonReport"), L.T("Compare.TextFilesTxtTxt"), "Comparison.txt");
        if (file == null) return;
        Try(L.T("Compare.SavingTheReport"), () =>
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Old: {OldFile}").AppendLine($"New: {NewFile}").AppendLine(L.F("Compare.IgnoreSpriteIDs0", IgnoreSpriteIds)).AppendLine();
            foreach (var c in _result.WithDifferences)
            {
                sb.AppendLine(L.F("Compare.01Changed2New3", c.Name, c.Changed.Count, c.Added.Count, c.Removed.Count));
                if (c.Added.Count > 0) sb.AppendLine("New: " + string.Join(", ", c.Added));
                if (c.Removed.Count > 0) sb.AppendLine("Removed: " + string.Join(", ", c.Removed));
                foreach (var key in c.Changed) sb.AppendLine($"-- {key}").Append(c.Changes.GetValueOrDefault(key));
                sb.AppendLine();
            }
            AtomicFile.WriteAllText(file, TextAttribution.ReportHeader(Attribution.ForNewAsset(copyright: null)) + sb.ToString());
            Saved(file);
        });
    }
}
