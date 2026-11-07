using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Net;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>A tool on the Home dashboard: opens its page when clicked.</summary>
public sealed record HomeTool(PageViewModel Page)
{
    public string Title => Page.Title;
    public string Glyph => Page.Glyph;
    public string Description => Page.Subtitle;
    public string AccessibleName => $"{Title}: {Description}";
}

/// <summary>A navigation group of the dashboard with its tools ("Graphics &amp; Sprites": Appearances, Extract Sprites…).</summary>
public sealed record HomeSection(string Name, string Summary, IReadOnlyList<HomeTool> Tools);

/// <summary>A file or folder saved recently, for the dashboard's "Recent exports".</summary>
public sealed record HomeExport(string Path, bool IsFile, DateTime SavedUtc)
{
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : Path;
    public string Detail => $"{SavedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {OutputLocations.ShortenForDisplay(System.IO.Path.GetDirectoryName(Path) ?? Path, 70)}";
    public bool Exists => IsFile ? File.Exists(Path) : Directory.Exists(Path);
}

/// <summary>A workflow suggestion: the steps that turn the client's files into a typical fansite resource.</summary>
public sealed record HomeWorkflow(string Title, string Steps, PageViewModel? Start)
{
    public string StartLabel => Start == null ? "" : "Open " + Start.Title;
}

/// <summary>
/// The Home dashboard: what Tibia Foundry is for, quick access to every tool by category, recently
/// used tools, recent exports, the client in use and common fansite workflows. It only reads what
/// the shell and settings already hold; every action opens another page or File Explorer.
/// </summary>
public sealed partial class HomeViewModel : PageViewModel
{
    private readonly MainViewModel _main;

    public HomeViewModel(AppServices services, MainViewModel main) : base(services, BuiltInTools.Home)
    {
        _main = main;
        services.Data.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ClientDataService.ClientVersion) or nameof(ClientDataService.PackageDisplayName) or nameof(ClientDataService.ImportedVersion))
                OnPropertyChanged(nameof(ClientInUse));
        };
    }

    /// <summary>The identity statement of the program.</summary>
    public static string Introduction =>
        L.T("Home.TibiaFoundryIsAnIndependentDesktop");

    public string IntroductionText => Introduction;

    /// <summary>Short notes on what each navigation group is for.</summary>
    private static readonly Dictionary<string, string> Summaries = new(StringComparer.Ordinal)
    {
        [BuiltInTools.ContentGroup] = "Your searchable Asset Library, and projects that turn client data and library assets into galleries and export packages.",
        [BuiltInTools.GraphicsGroup] = "Sprites, animated GIFs, outfits, creatures and sprite sheets for articles and galleries.",
        [BuiltInTools.GameDataGroup] = "Creatures, items, houses, quests and the changes between client versions, for databases and wiki pages.",
        [BuiltInTools.MapsGroup] = "Map images, areas, NPC positions and house maps for guides and location articles.",
        [BuiltInTools.MediaGroup] = "Sounds and music of the client, and the metadata and attribution of your published files.",
        [BuiltInTools.AssetsGroup] = "Inspect, edit and convert the official assets and the client's resource files.",
        [BuiltInTools.ClientLibraryGroup] = "Keep official client versions for historical reference and asset access.",
        [BuiltInTools.UtilitiesGroup] = "Batch renaming, image conversion, checksums, comparisons, backups and audits.",
        [BuiltInTools.ApplicationGroup] = "Settings, compatibility, diagnostics and credits.",
    };

    /// <summary>Every tool, grouped as in the navigation (Home itself left out).</summary>
    public IReadOnlyList<HomeSection> Sections =>
        _main.Pages.Where(p => p != this).GroupBy(p => p.Group)
            .Select(g => new HomeSection(g.Key, Summaries.GetValueOrDefault(g.Key, ""), g.Select(p => new HomeTool(p)).ToList())).ToList();

    /// <summary>The pages opened most recently (newest first), without Home.</summary>
    public IReadOnlyList<HomeTool> RecentTools =>
        Services.Settings.RecentPages.Select(name => _main.Pages.FirstOrDefault(p => p.GetType().Name == name))
            .OfType<PageViewModel>().Where(p => p != this).Take(6).Select(p => new HomeTool(p)).ToList();

    public bool HasRecentTools => RecentTools.Count > 0;

    /// <summary>The files and folders saved most recently (newest first).</summary>
    public ObservableCollection<HomeExport> RecentExports { get; } = [];

    public bool HasRecentExports => RecentExports.Count > 0;

    /// <summary>The client every page reads ("Live client · 15.33.9d148f").</summary>
    public string ClientInUse => _main.ClientInUse;

    /// <summary>Typical fansite workflows, each starting on the page that does the first step.</summary>
    public IReadOnlyList<HomeWorkflow> Workflows =>
    [
        new(L.T("Home.GenerateACompleteCreatureOutfitOr"),
            L.T("Home.ContentStudioNewProjectChooseCreatures"), Page<ContentStudioViewModel>()),
        new(L.T("Home.KeepEveryResourceSearchableInOne"),
            L.T("Home.AssetLibraryAddFolderYourExports"), Page<AssetLibraryViewModel>()),
        new(L.T("Home.CreatureOrItemImagesForA"),
            L.T("Home.AppearancesFindTheObjectOrOutfit"), Page<AppearancesViewModel>()),
        new(L.T("Home.AConsistentOutfitOrCreatureGallery"),
            L.T("Home.AppearancesSelectSeveralOutfitsPresetOutfit"), Page<AppearancesViewModel>()),
        new(L.T("Home.MapReferencesAndHouseImages"),
            L.T("Home.MapToolsForMapImagesAreas"), Page<MapToolsViewModel>()),
        new(L.T("Home.WhatChangedInTheLatestClient"),
            L.T("Home.ClientsArchiveEachOfficialVersionCompare"), Page<CompareViewModel>()),
        new(L.T("Home.BatchProcessAndCreditPublishedImages"),
            L.T("Home.ScriptsBatchImageConverterAndResizer"), Page<ScriptsViewModel>()),
    ];

    private PageViewModel? Page<T>() where T : PageViewModel => _main.Pages.OfType<T>().FirstOrDefault();

    public override Task OnNavigatedToAsync()
    {
        RecentExports.Clear();
        foreach (var e in Services.Settings.RecentExports.Take(6)) RecentExports.Add(new HomeExport(e.Path, e.IsFile, e.SavedUtc));
        OnPropertyChanged(nameof(HasRecentExports));
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(RecentTools));
        OnPropertyChanged(nameof(HasRecentTools));
        OnPropertyChanged(nameof(ClientInUse));
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void OpenTool(PageViewModel? page)
    {
        if (page != null && _main.Pages.Contains(page)) _main.SelectedPage = page;
    }

    [RelayCommand]
    private void RevealExport(HomeExport? export)
    {
        if (export == null) return;
        if (export.Exists) Services.RevealSaved(new StatusLink(export.Path, export.IsFile));
        else Services.Status.Show(L.F("Home.0NoLongerExistsItWas", export.Path), StatusKind.Warning);
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        string root = Services.Settings.OutputFolder;
        if (Directory.Exists(root)) Services.Shell.OpenFolder(root);
        else Services.Status.Show(L.F("Home.0DoesNotExistYetIt", root), StatusKind.Info);
    }

    [RelayCommand] private void OpenClients() => OpenTool(Page<ClientsViewModel>());
    [RelayCommand] private void OpenWebsite() => Services.Shell.OpenUrl(ExternalEndpoints.ProjectWebsite);
    [RelayCommand] private void OpenTibiaWiki() => Services.Shell.OpenUrl(ExternalEndpoints.CreditTibiaWiki);
    [RelayCommand] private void OpenFansites() => Services.Shell.OpenUrl(ExternalEndpoints.TibiaFansites);

    public string Version => AppIdentity.Version;
}
