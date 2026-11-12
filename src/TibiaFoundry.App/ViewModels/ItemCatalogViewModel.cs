using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Items;
using TibiaFoundry.Core.Wiki;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>One identifier in the catalog list.</summary>
public sealed partial class CatalogEntryRow(CatalogEntry entry, IReadOnlyDictionary<string, ItemSource> sources) : ObservableObject
{
    private CatalogEntry _entry = entry;
    private IReadOnlyDictionary<string, ItemSource> _sources = sources;

    public CatalogEntry Entry => _entry;
    public string Id => Entry.IdType == IdType.TibiaObject ? Entry.Id.ToString(CultureInfo.InvariantCulture) : $"{Entry.Id} ({Entry.IdType})";
    public uint SortId => Entry.Id;
    public string Name => Entry.BestName ?? (Entry.Agreement == Agreement.Conflict ? "(conflict)" : L.T("ItemCatalog.NoName"));
    public bool HasName => Entry.BestName != null;
    public string AgreementText => ItemCatalogViewModel.Describe(Entry.Agreement);
    public Agreement Agreement => Entry.Agreement;
    public string ClientName => Entry.ClientName ?? (Entry.UnnamedInClient ? L.T("ItemCatalog.NotNamedByTheClient2") : Entry.InOfficialClient ? "" : L.T("ItemCatalog.NotInTheClient"));
    public string OtherNames => string.Join(" | ", Entry.NameGroups.Where(g => Entry.BestName == null || !ItemNames.Same(g[0].Name, Entry.BestName)).Select(g => g[0].FullName));
    public string Category => Entry.Category ?? "";
    public int SourceCount => Entry.Observations.Select(o => o.SourceId).Distinct().Count();
    public bool HasDisagreement => Entry.HasDisagreement;
    public string ToolTip => Entry.NamesBySource(_sources);

    /// <summary>The name is being edited in the list (F2, double-click or "Rename…" in the list's menu).</summary>
    [ObservableProperty] private bool _isEditing;

    /// <summary>
    /// Shows the identifier's entry after its records changed (a rename, a restored name, a choice in the
    /// conflict review) in this same row, so the list keeps its selection and scroll position.
    /// </summary>
    public void Update(CatalogEntry entry, IReadOnlyDictionary<string, ItemSource> sources)
    {
        _entry = entry;
        _sources = sources;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>One record of the selected identifier (details panel).</summary>
public sealed record ObservationRow(string Source, string Trust, string Name, string Status, string Details);

/// <summary>One of the names the sources give (conflict review): choosing it records a decision.</summary>
public sealed partial class NameChoiceRow(ItemCatalogViewModel owner, string name, string? article, string sources, bool isDecision) : ObservableObject
{
    public string Name { get; } = string.IsNullOrEmpty(article) ? name : article + " " + name;
    public string Sources { get; } = sources;
    /// <summary>This name is the user's current decision.</summary>
    public bool IsDecision { get; } = isDecision;
    public bool CanChoose => !IsDecision;

    [RelayCommand] private Task Choose() => owner.ChooseNameAsync(name, article);
}

/// <summary>One difference found by a comparison.</summary>
public sealed class ComparisonRowItem(ComparisonRow row)
{
    public ComparisonRow Row { get; } = row;
    public string Kind => CatalogComparison.DescribeKind(Row.Kind);
    public string Id => Row.IdType == IdType.TibiaObject ? Row.Id.ToString(CultureInfo.InvariantCulture) : $"{Row.Id} ({Row.IdType})";
    public string Left => Join(Row.OldName, Row.OldValue);
    public string Right => Join(Row.NewName, Row.NewValue);
    public string Sensitive => Row.BehaviorSensitive ? "yes" : "";
    public string Note => Row.Note ?? "";

    private static string Join(string? name, string? value) => name != null && value != null ? $"{name} ({value})" : name ?? value ?? "";
}

/// <summary>What one side of a comparison uses: a source of a catalog, or each identifier's settled name.</summary>
public sealed record ComparisonSourceChoice(string? SourceId, string Label);

/// <summary>A source of the catalog (Sources tab).</summary>
public sealed partial class CatalogSourceRow(ItemCatalogViewModel owner, ItemSource source, int records) : ObservableObject
{
    public ItemSource Source { get; } = source;
    public string Name => Source.Name;
    public string Trust => Source.Trust;
    public string Kind => Source.Kind.ToString();
    public string Version => Source.Version ?? "";
    public string Records => records.ToString("N0", CultureInfo.CurrentCulture);
    public string Imported => Source.ImportedAtUtc == default ? "" : Source.ImportedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    public string IdTypeText => ItemNames.Describe(Source.IdType, Source.Version);
    public string? Origin => Source.Origin;
    public bool CanRemove => !ItemCatalogStore.IsJournalSource(Source);

    [RelayCommand] private Task Remove() => owner.RemoveSourceAsync(Source);
}

/// <summary>
/// Item Catalog: a source-aware database of item names and IDs. Each catalog keeps its sources
/// (the client's own data, TibiaWiki, lists, CSV/JSON files) apart, tells official from other data,
/// never merges different identifier types, and shows where every name comes from. Imports are
/// previewed and confirmed; replaced records are backed up.
/// </summary>
public sealed partial class ItemCatalogViewModel : PageViewModel
{
    private List<CatalogEntryRow> _allRows = [];

    public ItemCatalogViewModel(AppServices services) : base(services, BuiltInTools.ItemCatalog)
    {
        _agreementFilter = AgreementFilters[0];
        _sourceFilter = new(null, L.T("ItemCatalog.AnySource"));
        _sortOrder = SortOrders[0];
        _exportScope = ExportScopes[0];
    }

    private void RememberCatalog(string folder)
    {
        Services.ToolSettings.TrySet(Manifest, "lastCatalog", folder, out _);
        Services.SaveSettings();
    }

    public static string Describe(Agreement agreement) => agreement switch
    {
        Agreement.VerifiedOfficial => "official",
        Agreement.Decided => L.T("ItemCatalog.ChosenByYou"),
        Agreement.Corroborated => L.T("ItemCatalog.SourcesAgree"),
        Agreement.SingleSource => L.T("ItemCatalog.OneSource"),
        Agreement.Conflict => "conflict",
        _ => L.T("ItemCatalog.NoName2"),
    };

    // ------------------------------------------------------------------ catalogs

    public ObservableCollection<CatalogSummary> Catalogs { get; } = new();

    [ObservableProperty] private CatalogSummary? _selectedCatalog;
    [ObservableProperty] private ItemCatalog? _catalog;

    public bool HasCatalog => Catalog != null;
    public string CatalogsFolder => Services.Outputs.FolderFor(OutputKind.ItemCatalogs);

    /// <summary>One-line state of the open catalog (sources, entries, problems).</summary>
    [ObservableProperty] private string _catalogSummary = L.T("ItemCatalog.NoCatalogIsOpen");

    public override Task OnNavigatedToAsync()
    {
        RefreshCatalogs();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void RefreshCatalogs()
    {
        string? current = Catalog?.Folder ?? SelectedCatalog?.Folder;
        Catalogs.Clear();
        foreach (var c in ItemCatalogStore.List(CatalogsFolder)) Catalogs.Add(c);
        string? remembered = Services.ToolSettings.Get(Manifest, "lastCatalog") is { Length: > 0 } last ? last : null;
        var pick = Catalogs.FirstOrDefault(c => string.Equals(c.Folder, current ?? remembered, StringComparison.OrdinalIgnoreCase)) ?? (Catalog == null ? Catalogs.FirstOrDefault() : null);
        if (pick != null && !ReferenceEquals(pick, SelectedCatalog)) SelectedCatalog = pick;
        OnPropertyChanged(nameof(CatalogsFolder));
    }

    partial void OnSelectedCatalogChanged(CatalogSummary? value)
    {
        if (value == null || string.Equals(value.Folder, Catalog?.Folder, StringComparison.OrdinalIgnoreCase)) return;
        if (value.Problem != null)
        {
            Services.Dialogs.Show(L.F("ItemCatalog.TheCatalogIn0CannotBe", value.Folder, value.Problem), MessageKind.Warning, L.T("ItemCatalog.ItemCatalog"));
            return;
        }
        _ = OpenAsync(value.Folder);
    }

    /// <summary>"Open another…": a catalog in any folder (for example one copied from another computer).</summary>
    [RelayCommand]
    private async Task OpenOtherCatalogAsync()
    {
        string? folder = Services.Dialogs.PickFolder(L.T("ItemCatalog.FolderOfAnItemCatalogWith"), Catalog?.Folder ?? CatalogsFolder);
        if (folder == null) return;
        if (!ItemCatalogStore.IsCatalogFolder(folder))
        {
            Services.Dialogs.Show(L.F("ItemCatalog.0HasNoCatalogJsonSo", folder), MessageKind.Warning, L.T("ItemCatalog.OpenCatalog"));
            return;
        }
        await OpenFolderAsync(folder);
    }

    /// <summary>Opens the catalog in a folder and lists it (also when it is outside the catalogs folder).</summary>
    public async Task OpenFolderAsync(string folder)
    {
        await OpenAsync(folder);
        if (Catalog == null || !string.Equals(Catalog.Folder, folder, StringComparison.OrdinalIgnoreCase)) return;
        var listed = Catalogs.FirstOrDefault(c => string.Equals(c.Folder, folder, StringComparison.OrdinalIgnoreCase));
        if (listed == null)
        {
            listed = new CatalogSummary(folder, Catalog.Name, Catalog.Sources.Count, Catalog.Info.CreatedUtc, null);
            Catalogs.Add(listed);
        }
        SelectedCatalog = listed;
    }

    private async Task OpenAsync(string folder)
    {
        ItemCatalog? opened = null;
        if (!await RunAsync(L.T("ItemCatalog.OpeningTheCatalog"), async (_, _) => opened = await Task.Run(() => ItemCatalogStore.Load(folder)), operation: "open")) return;
        SetCatalog(opened);
        RememberCatalog(folder);
        if (opened!.LoadProblems.Count > 0)
            Services.Dialogs.Show(L.T("ItemCatalog.PartsOfTheCatalogCouldNot") + string.Join("\n", opened.LoadProblems), MessageKind.Warning, opened.Name);
    }

    private void SetCatalog(ItemCatalog? catalog)
    {
        Catalog = catalog;
        OnPropertyChanged(nameof(HasCatalog));
        OnPropertyChanged(nameof(LeftChoices));
        var left = LeftChoices;
        LeftChoice = left.FirstOrDefault(c => c.SourceId == LeftChoice?.SourceId) ?? (left.Count > 0 ? left[0] : null);
        if (RightCatalog != null && catalog != null && string.Equals(RightCatalog.Folder, catalog.Folder, StringComparison.OrdinalIgnoreCase))
        {
            _rightLoaded = catalog;
            string? keep = RightChoice?.SourceId;
            RightChoices = [new(null, L.T("ItemCatalog.SettledNamesAllSources")), .. catalog.Sources.Select(s => new ComparisonSourceChoice(s.Id, s.Name))];
            RightChoice = RightChoices.FirstOrDefault(c => c.SourceId == keep) ?? RightChoices[0];
        }
        RebuildRows();
        RefreshSources();
        Coverage = null;
        UpdateSummary();
    }

    /// <summary>"New catalog…": asks for a name and creates an empty catalog in the catalogs folder.</summary>
    [RelayCommand]
    private async Task NewCatalogAsync() => await CreateCatalogAsync();

    private async Task<bool> CreateCatalogAsync()
    {
        string suggestion = Services.Data.ClientVersion is { } v ? L.F("ItemCatalog.Tibia0", v) : L.T("ItemCatalog.ItemCatalog2");
        string? name = Services.Dialogs.Prompt(L.T("ItemCatalog.NewItemCatalog"), L.T("ItemCatalog.NameOfTheCatalogForExample"), suggestion);
        if (string.IsNullOrWhiteSpace(name)) return false;
        string? root = Services.Outputs.PrepareOrAsk(OutputKind.ItemCatalogs, L.T("ItemCatalog.ItemCatalogs"));
        if (root == null) return false;
        ItemCatalog? created = null;
        if (!await RunAsync(L.T("ItemCatalog.CreatingTheCatalog"), (_, _) => Task.Run(() => created = ItemCatalogStore.Create(ItemCatalogStore.FolderFor(root, name), name)), operation: "open"))
            return false;
        SetCatalog(created);
        RefreshCatalogs();
        SelectedCatalog = Catalogs.FirstOrDefault(c => string.Equals(c.Folder, created!.Folder, StringComparison.OrdinalIgnoreCase));
        RememberCatalog(created!.Folder);
        Info(L.F("ItemCatalog.Catalog0CreatedImportTheClient", created.Name));
        return true;
    }

    [RelayCommand]
    private void OpenCatalogFolder() => Services.Shell.OpenFolder(Catalog?.Folder ?? CatalogsFolder);

    // ------------------------------------------------------------------ imports

    /// <summary>Reads records, shows what would change, and adds them after the user confirms.</summary>
    private async Task ImportAsync(string title, Func<IProgress<OperationProgress>, CancellationToken, Task<IReadOnlyList<ItemImport>>> read, string operation = "import")
    {
        if (Catalog == null && !await CreateCatalogAsync()) return;
        IReadOnlyList<ItemImport>? imports = null;
        if (!await RunAsync(title, async (p, ct) => imports = await read(p, ct), operation: operation)) return;
        int done = 0;
        foreach (var import in imports!)
        {
            var preview = Catalog!.Preview(import);
            if (preview.IsEmpty)
            {
                Services.Dialogs.Show(L.T("ItemCatalog.NothingCanBeImported") + preview.ToText(), MessageKind.Warning, title);
                continue;
            }
            LastPreview = preview.ToText();
            if (Services.Dialogs.ShowReport(ImportReport(preview, Catalog.Name, title)) != ReportAnswer.Primary) continue;
            var catalog = Catalog;
            if (!await RunAsync(L.T("ItemCatalog.SavingTheCatalog"), (_, _) => Task.Run(() => ItemCatalogStore.Commit(catalog, import, Services.Backups), CancellationToken.None), operation: "import")) break;
            done++;
        }
        if (done == 0) return;
        SetCatalog(Catalog);
        Info(L.F("ItemCatalog.Imported0Into1", L.Count(done, "Noun.source"), Catalog!.Name));
    }

    /// <summary>
    /// The import confirmation (8.8): the source and what changes as labeled values, the comparison with
    /// the client, and the long lists (names shared by several identifiers, lines not used) in collapsed
    /// sections whose tables scroll, instead of one long block of text. Nothing is left out.
    /// </summary>
    internal static DialogReport ImportReport(ImportPreview preview, string catalogName, string title)
    {
        var s = preview.Import.Source;
        var sections = new List<ReportSection>();
        var sourceRows = new List<ReportRow>
        {
            new(L.T("Common.Source"), $"{s.Name} ({s.Trust}{(s.Version != null ? ", version " + s.Version : "")})"),
            new(L.T("ItemCatalog.Identifiers2"), ItemNames.Describe(s.IdType, s.Version)),
        };
        if (s.Origin != null) sourceRows.Add(ReportRow.Path(L.T("ItemCatalog.From"), s.Origin));
        sections.Add(new ReportSection(L.T("Common.Source")) { Rows = sourceRows });

        var changeRows = new List<ReportRow> { new(L.T("ItemCatalog.Records"), L.F("ItemCatalog.0N01N0WithA", preview.Records, preview.Named)) };
        if (preview.Replaces != null)
        {
            changeRows.Add(new(L.T("ItemCatalog.Replaces"), L.F("ItemCatalog.TheSourceImported0YyyyMM", preview.Replaces.ImportedAtUtc.ToLocalTime())));
            changeRows.Add(new(L.T("ItemCatalog.Added"), preview.Added.ToString("N0"), preview.Added > 0 ? ReportTone.Success : ReportTone.Normal));
            changeRows.Add(new(L.T("ItemCatalog.Changed"), preview.Changed.ToString("N0"), preview.Changed > 0 ? ReportTone.Warning : ReportTone.Normal));
            changeRows.Add(new(L.T("ItemCatalog.Removed"), preview.Removed.ToString("N0"), preview.Removed > 0 ? ReportTone.Warning : ReportTone.Normal));
            changeRows.Add(new(L.T("ItemCatalog.Unchanged"), preview.Unchanged.ToString("N0"), ReportTone.Muted));
        }
        else changeRows.Add(new(L.T("Common.Result"), L.F("ItemCatalog.NewSource0Added", L.Count(preview.Added, "Noun.record")), ReportTone.Success));
        sections.Add(new ReportSection(L.T("ItemCatalog.WhatTheImportChanges"))
        {
            Rows = changeRows,
            Bullets = preview.ChangedExamples.Select(e => "changed " + e).ToList(),
        });

        if (preview.DisagreeWithClient != null)
            sections.Add(new ReportSection(L.T("ItemCatalog.ComparedWithTheClient"))
            {
                Rows =
                [
                    new(L.T("ItemCatalog.NamesForUnnamedObjects"), (preview.NamesForUnnamed ?? 0).ToString("N0")),
                    new(L.T("ItemCatalog.NamesThatDiffer"), preview.DisagreeWithClient.Value.ToString("N0"), preview.DisagreeWithClient > 0 ? ReportTone.Warning : ReportTone.Normal),
                    new(L.T("ItemCatalog.NotListedByTheClient"), (preview.NotInClient ?? 0).ToString("N0")),
                ],
                Note = preview.DisagreeWithClient > 0 ? L.T("ItemCatalog.DifferingNamesAreKeptAsThis") : null,
            });

        if (preview.DuplicateNameCount > 0)
            sections.Add(new ReportSection(L.F("ItemCatalog.NamesUsedByMoreThanOne", preview.DuplicateNameCount))
            {
                Text = L.T("ItemCatalog.CommonInTibiaGroundsWallsDecorations"),
                Table = new DialogTable([L.T("Common.Name"), L.T("ItemCatalog.Identifiers2"), L.T("AssetsEditor.Count")],
                    preview.DuplicateNames.Select(d => (IReadOnlyList<string>)[d.Name, string.Join(", ", d.Ids.Take(12)) + (d.Ids.Count > 12 ? ", …" : ""), d.Ids.Count.ToString("N0")]).ToList(),
                    NumericColumns: 1),
                Note = preview.DuplicateNameCount > preview.DuplicateNames.Count ? L.F("ItemCatalog.The0N0MostUsedNames", preview.DuplicateNames.Count) : null,
                Collapsed = true,
            });

        if (preview.Import.Problems.Count > 0)
            sections.Add(new ReportSection(L.F("ItemCatalog.0NotUsed", L.Count(preview.Import.Problems.Count, "Noun.line")))
            {
                Bullets = preview.Import.Problems.Take(500).Select(p => p.ToString()).ToList(),
                Note = preview.Import.Problems.Count > 500 ? L.F("ItemCatalog.And0N0More", preview.Import.Problems.Count - 500) : null,
                Collapsed = true, Tone = ReportTone.Warning,
            });

        return new DialogReport
        {
            Title = title, Kind = MessageKind.Question,
            Heading = L.F("ItemCatalog.Import0Into1", L.Count(preview.Records, "Noun.record"), catalogName),
            Summary = L.F("ItemCatalog.012N0WithA", s.Name, (s.Version != null && !s.Name.Contains(s.Version, StringComparison.Ordinal) ? " " + s.Version : ""), preview.Named),
            Sections = sections, PrimaryText = L.T("ItemCatalog.Import"), CancelText = L.T("Common.Cancel"),
        };
    }

    /// <summary>The last import preview (shown on the Sources tab, so it can be read again).</summary>
    [ObservableProperty] private string? _lastPreview;

    [RelayCommand]
    private Task ImportClientAsync() => ImportAsync(L.T("ItemCatalog.ImportingTheClientSItems"), async (_, _) =>
    {
        var db = await Services.Data.EnsureAppearancesAsync() ?? throw new FileNotFoundException(
            L.T("ItemCatalog.NoAppearancesFileWasFound"));
        string? path = db.SourcePath ?? Services.Data.DefaultFile(AssetTypes.Appearances);
        string? version = Services.Data.ClientVersion;
        return [await Task.Run(() => ItemImporters.FromClient(db, version, path))];
    });

    [RelayCommand]
    private Task ImportTibiaWikiAsync() => ImportAsync(L.T("ItemCatalog.ImportingTibiaWikiSItemIDs"), async (progress, ct) =>
    {
        var table = await WikiItemIdTable.GetAsync(Services.Http, Services.CacheDirectory, progress: progress, ct: ct);
        return [ItemImporters.FromTibiaWiki(table)];
    }, "import-wiki");

    [RelayCommand]
    private async Task ImportFileAsync()
    {
        string? file = Services.Dialogs.OpenFile(L.T("ItemCatalog.ImportItemNamesAndIDs"),
            L.T("ItemCatalog.ItemDataTxtCsvJsonXml"),
            Services.Settings.InputFolderFor(Manifest.Id) ?? Services.Outputs.FolderFor(OutputKind.DataExports));
        if (file == null) return;
        Services.Settings.RememberFolder(Manifest.Id, "input", Path.GetDirectoryName(file));
        string extension = Path.GetExtension(file).ToLowerInvariant();
        if (extension == ".xml") { await ImportItemsXmlAsync(file); return; }
        if (extension == ".otb") { await ImportOtbAsync(file); return; }
        IdType idType = IdType.TibiaObject;
        if (extension != ".json")
        {
            // The meaning of the numbers is not in a list or a CSV file: the user says what they are.
            int answer = Services.Dialogs.Choose(
                L.F("ItemCatalog.WhatAreTheNumbersIn0", Path.GetFileName(file)),
                L.T("ItemCatalog.ImportItemNamesAndIDs"), L.T("ItemCatalog.TibiaObjectIDs"), L.T("ItemCatalog.SomethingElse"));
            if (answer < 0) return;
            idType = answer == 0 ? IdType.TibiaObject : IdType.Unknown;
        }
        // A CSV list without a header ("name,id" or "id,name"): the user says which value is the ID.
        HeaderlessCsv? headerless = null;
        if (extension == ".csv")
        {
            string? text = null;
            if (!await RunAsync(L.T("ItemCatalog.Reading") + Path.GetFileName(file), async (_, ct) => text = await File.ReadAllTextAsync(file, ct), operation: "import")) return;
            if (!ItemImporters.CsvHasHeader(text!) && ItemImporters.GuessHeaderless(text!) is { } guess)
            {
                var other = guess == HeaderlessCsv.NameThenId ? HeaderlessCsv.IdThenName : HeaderlessCsv.NameThenId;
                static string Layout(HeaderlessCsv l) => l == HeaderlessCsv.NameThenId ? L.T("ItemCatalog.NameThenID") : L.T("ItemCatalog.IDThenName");
                var lines = text!.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#')).Take(4);
                int layout = Services.Dialogs.Choose(
                    L.F("ItemCatalog.0HasNoHeaderLineNaming", Path.GetFileName(file)) +
                    string.Join("\n", lines.Select(l => "    " + (l.Length > 80 ? l[..77] + "…" : l))) +
                    L.T("ItemCatalog.WhichValueOfEachLineIs"),
                    L.T("ItemCatalog.ImportItemNamesAndIDs"), Layout(guess), Layout(other));
                if (layout < 0) return;
                headerless = layout == 0 ? guess : other;
            }
        }
        var catalog = Catalog;
        await ImportAsync(L.F("ItemCatalog.Reading0", Path.GetFileName(file)), async (_, ct) =>
        {
            string text = await File.ReadAllTextAsync(file, ct);
            return extension switch
            {
                ".json" => ItemImporters.FromCatalogJson(text, file),
                ".csv" => [ItemImporters.FromCsv(text, ItemImporters.SourceForFile(file, SourceKind.Csv, idType, catalog ?? Catalog), idType, headerless)],
                _ => [ItemImporters.FromNameList(text, ItemImporters.SourceForFile(file, SourceKind.NameList, idType, catalog ?? Catalog), idType)],
            };
        });
    }

    /// <summary>The source name suggested for third-party item files.</summary>
    private const string ThirdPartyName = "Third-party data";

    /// <summary>Where the file comes from and its client version ("Community item list 15.25"); null when cancelled.</summary>
    private (string Distribution, string? Version)? AskSourceName(string file, string? knownVersion)
    {
        string suggestion = ThirdPartyName;
        if (knownVersion != null) suggestion += " " + knownVersion;
        string? text = Services.Dialogs.Prompt(L.T("ItemCatalog.ThirdPartyItemData"), L.F("ItemCatalog.WhereIs0FromAndFor", Path.GetFileName(file)), suggestion);
        if (text == null) return null;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        string? version = words.Count > 0 && System.Text.RegularExpressions.Regex.IsMatch(words[^1], @"^\d+(\.\d+)+$") ? words[^1] : null;
        if (version != null) words.RemoveAt(words.Count - 1);
        return (words.Count > 0 ? string.Join(' ', words) : ThirdPartyName, version ?? knownVersion);
    }

    /// <summary>
    /// A third-party items.xml item list: the user says whether its IDs are client object IDs or the IDs of
    /// an items.otb; with items.otb IDs, an items.otb next to it can be imported too, so the names can be
    /// compared with the client's own names through its mappings (for example to research names of objects
    /// the client does not name for wiki articles; such names always stay unverified).
    /// </summary>
    private async Task ImportItemsXmlAsync(string file)
    {
        string title = "Import " + Path.GetFileName(file);
        string otb = Path.Combine(Path.GetDirectoryName(file)!, "items.otb");
        bool hasOtb = File.Exists(otb);
        int answer = Services.Dialogs.Choose(L.F("ItemCatalog.WhatAreTheIDsIn0", Path.GetFileName(file)) +
            (hasOtb ? L.T("ItemCatalog.ThereIsAnItemsOtbNext") : ""), title, L.T("ItemCatalog.ClientObjectIDs"), L.T("ItemCatalog.ItemsOtbIDs"));
        if (answer < 0) return;
        var idType = answer == 0 ? IdType.TibiaObject : IdType.OtbServer;
        if (AskSourceName(file, null) is not { } origin) return;
        bool otbKnown = Catalog?.Sources.Any(s => s.Kind == SourceKind.OtItemsOtb && string.Equals(s.Origin, otb, StringComparison.OrdinalIgnoreCase)) == true;
        bool alsoOtb = false;
        if (idType == IdType.OtbServer && hasOtb && !otbKnown)
        {
            int both = Services.Dialogs.Choose(L.F("ItemCatalog.ImportTheItemsOtbNextTo", otb),
                title, L.T("ItemCatalog.ImportBoth"), L.T("ItemCatalog.OnlyItemsXml"));
            if (both < 0) return;   // Cancel or Esc: nothing is imported
            alsoOtb = both == 0;
        }
        var catalog = Catalog;
        await ImportAsync(L.F("ItemCatalog.Reading0", Path.GetFileName(file)), (_, ct) => Task.Run(() =>
        {
            var imports = new List<ItemImport>();
            string? version = origin.Version;
            if (alsoOtb)
            {
                var otbFile = OtbFile.Read(otb);
                version ??= otbFile.ClientVersion;
                imports.Add(ItemImporters.FromOtb(otbFile, ItemImporters.SourceForFile(otb, SourceKind.OtItemsOtb, IdType.OtbServer, catalog ?? Catalog, version, origin.Distribution)
                    with { Name = L.F("ItemCatalog.0ItemsOtb", origin.Distribution) }));
            }
            using var stream = File.OpenRead(file);
            var source = ItemImporters.SourceForFile(file, SourceKind.OtItemsXml, idType, catalog ?? Catalog, version, origin.Distribution) with { Name = L.F("ItemCatalog.0ItemsXml", origin.Distribution) };
            imports.Add(ItemImporters.FromItemsXml(stream, source, idType));
            return (IReadOnlyList<ItemImport>)imports;
        }, ct));
    }

    /// <summary>A third-party items.otb on its own (its item IDs, groups, flags and their client IDs).</summary>
    private async Task ImportOtbAsync(string file)
    {
        if (AskSourceName(file, null) is not { } origin) return;
        var catalog = Catalog;
        await ImportAsync(L.F("ItemCatalog.Reading0", Path.GetFileName(file)), (_, ct) => Task.Run(() =>
        {
            var otb = OtbFile.Read(file);
            var source = ItemImporters.SourceForFile(file, SourceKind.OtItemsOtb, IdType.OtbServer, catalog ?? Catalog, origin.Version ?? otb.ClientVersion, origin.Distribution)
                with { Name = L.F("ItemCatalog.0ItemsOtb", origin.Distribution) };
            return (IReadOnlyList<ItemImport>)[ItemImporters.FromOtb(otb, source)];
        }, ct));
    }

    internal async Task RemoveSourceAsync(ItemSource source)
    {
        var catalog = Catalog;
        if (catalog == null) return;
        if (!Services.Dialogs.Confirm(L.F("ItemCatalog.Remove0AndIts1N0", source.Name, catalog.CountOf(source.Id), catalog.Name), L.T("ItemCatalog.RemoveSource"), L.T("Common.Remove"), L.T("Common.Cancel"))) return;
        if (!await RunAsync(L.T("ItemCatalog.RemovingTheSource"), (_, _) => Task.Run(() => ItemCatalogStore.RemoveSource(catalog, source.Id, Services.Backups), CancellationToken.None), operation: "import")) return;
        SetCatalog(catalog);
        Info(L.F("ItemCatalog.0RemovedFrom1", source.Name, catalog.Name));
    }

    // ------------------------------------------------------------------ sources and coverage

    public ObservableCollection<CatalogSourceRow> Sources { get; } = new();

    [ObservableProperty] private string? _coverage;

    private void RefreshSources()
    {
        Sources.Clear();
        if (Catalog == null) return;
        foreach (var s in Catalog.Sources) Sources.Add(new CatalogSourceRow(this, s, Catalog.CountOf(s.Id)));
    }

    [RelayCommand]
    private async Task ComputeCoverageAsync()
    {
        var catalog = Catalog;
        if (catalog == null) return;
        string? text = null;
        if (!await RunAsync(L.T("ItemCatalog.ComputingCoverage"), async (_, _) => text = await Task.Run(() => CatalogCoverage.Of(catalog).ToText()), operation: "open")) return;
        Coverage = text;
    }

    private void UpdateSummary()
    {
        if (Catalog == null)
        {
            CatalogSummary = Catalogs.Count == 0 ? L.T("ItemCatalog.NoCatalogYetCreateOneThen") : L.T("ItemCatalog.ChooseACatalog");
            return;
        }
        int sources = Catalog.Sources.Count;
        CatalogSummary = L.F("ItemCatalog.0N0IdentifiersFrom1", Catalog.Entries.Count, L.Count(sources, "Noun.source")) +
                         (Catalog.ClientSource is { } c ? L.F("ItemCatalog.Client0", c.Version ?? L.T("ItemCatalog.VersionUnknown")) : L.T("ItemCatalog.NoClientDataImportedYet")) +
                         (Catalog.LoadProblems.Count > 0 ? L.F("ItemCatalog.0WhenOpening", L.Count(Catalog.LoadProblems.Count, "Noun.problem")) : "") + ".";
    }

    // ------------------------------------------------------------------ list, filters, details

    [ObservableProperty] private IReadOnlyList<CatalogEntryRow> _visibleRows = [];
    [ObservableProperty] private CatalogEntryRow? _selectedRow;
    [ObservableProperty] private IReadOnlyList<ObservationRow> _details = [];

    /// <summary>The names to choose from when the sources disagree (empty otherwise).</summary>
    [ObservableProperty] private IReadOnlyList<NameChoiceRow> _nameChoices = [];

    /// <summary>Mappings of the selected identifier ("items.otb ID 2148 → client ID 3031 (13.10)").</summary>
    [ObservableProperty] private string? _mappingText;

    /// <summary>The selected identifier has a name of yours (a rename or a choice in the conflict review) that "Restore original name" removes.</summary>
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(WithdrawDecisionCommand))] private bool _canWithdrawDecision;
    [ObservableProperty] private string _detailsTitle = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _onlyUnnamedInClient;
    [ObservableProperty] private bool _onlyDisagreements;
    [ObservableProperty] private SettingChoice<Agreement?> _agreementFilter;
    [ObservableProperty] private SettingChoice<string?> _sourceFilter;
    [ObservableProperty] private SettingChoice<string> _sortOrder;

    public IReadOnlyList<SettingChoice<Agreement?>> AgreementFilters { get; } =
        [new(null, L.T("Choice.Common.Any")), .. Enum.GetValues<Agreement>().Select(a => new SettingChoice<Agreement?>(a, Describe(a)))];

    /// <summary>"Any source", then "with …" and "without …" for each source.</summary>
    public IReadOnlyList<SettingChoice<string?>> SourceFilters =>
        [new(null, L.T("ItemCatalog.AnySource")), .. (Catalog?.Sources ?? []).SelectMany(s => new[] { new SettingChoice<string?>("+" + s.Id, L.F("ItemCatalog.With0", s.Name)), new SettingChoice<string?>("-" + s.Id, L.F("ItemCatalog.Without0", s.Name)) })];

    public IReadOnlyList<SettingChoice<string>> SortOrders { get; } = [new("id", "By ID"), new("name", L.T("ItemCatalog.ByName")), new("agreement", L.T("ItemCatalog.ByAgreement"))];

    public int TotalCount => _allRows.Count;
    public int VisibleCount => VisibleRows.Count;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnOnlyUnnamedInClientChanged(bool value) => ApplyFilter();
    partial void OnOnlyDisagreementsChanged(bool value) => ApplyFilter();
    partial void OnAgreementFilterChanged(SettingChoice<Agreement?> value) => ApplyFilter();
    partial void OnSourceFilterChanged(SettingChoice<string?> value) => ApplyFilter();
    partial void OnSortOrderChanged(SettingChoice<string> value) => ApplyFilter();
    partial void OnSelectedRowChanged(CatalogEntryRow? value) => ShowDetails(value?.Entry);

    /// <summary>The current filters as a query (also the "matching the filters" export selection).</summary>
    public CatalogQuery CurrentQuery => new()
    {
        Text = SearchText,
        Agreement = AgreementFilter?.Value,
        WithSource = SourceFilter?.Value is { } with && with.StartsWith('+') ? with[1..] : null,
        WithoutSource = SourceFilter?.Value is { } without && without.StartsWith('-') ? without[1..] : null,
        OnlyUnnamedInClient = OnlyUnnamedInClient,
        OnlyDisagreements = OnlyDisagreements,
    };

    private void RebuildRows()
    {
        var catalog = Catalog;
        _allRows = catalog == null ? [] : catalog.Entries.Select(e => new CatalogEntryRow(e, catalog.SourcesById)).ToList();
        OnPropertyChanged(nameof(SourceFilters));
        if (SourceFilter?.Value != null && catalog?.Source(SourceFilter.Value[1..]) == null) SourceFilter = SourceFilters[0];
        OnPropertyChanged(nameof(TotalCount));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = CurrentQuery;
        IEnumerable<CatalogEntryRow> rows = _allRows.Where(r => query.Matches(r.Entry));
        rows = SortOrder?.Value switch
        {
            "name" => rows.OrderBy(r => r.HasName ? 0 : 1).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.SortId),
            "agreement" => rows.OrderBy(r => r.Agreement).ThenBy(r => r.SortId),
            _ => rows,
        };
        VisibleRows = rows.ToList();
        OnPropertyChanged(nameof(VisibleCount));
    }

    private void ShowDetails(CatalogEntry? entry)
    {
        if (entry == null || Catalog == null)
        {
            Details = [];
            DetailsTitle = "";
            NameChoices = [];
            MappingText = null;
            CanWithdrawDecision = false;
            return;
        }
        ShowReview(entry);
        var catalog = Catalog;
        string what = entry.IdType switch
        {
            IdType.TibiaObject => L.T("AppearanceKind.Object"),
            IdType.OtbServer => L.T("ItemCatalog.ItemsOtbID"),
            IdType.OtbClient => L.T("ItemCatalog.OTBClientID"),
            IdType.LegacyDatClient => L.T("ItemCatalog.LegacyID"),
            _ => L.T("ItemCatalog.IDOfUnknownType"),
        };
        DetailsTitle = $"{what} {entry.Id}: {entry.BestName ?? "no settled name"} — {Describe(entry.Agreement)}" +
                       (entry.BestNameReason != null ? $" ({entry.BestNameReason})" : "");
        Details = entry.Observations.Select(o =>
        {
            var s = catalog.Source(o.SourceId);
            var parts = new List<string>();
            if (o.NameIsTitle) parts.Add(L.T("ItemCatalog.TibiaWikiArticleTitle"));
            if (o.Plural != null) parts.Add("plural: " + o.Plural);
            if (o.Category != null) parts.Add("category: " + o.Category);
            if (o.Flags is { Count: > 0 } f) parts.Add(string.Join(", ", f));
            if (o.Method != ObservationMethod.Import) parts.Add(o.Method.ToString());
            if (o.ObservedAtUtc is { } when) parts.Add("observed " + when.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
            if (o.Evidence != null) parts.Add("evidence: " + o.Evidence);
            if (o.Notes != null) parts.Add(o.Notes);
            return new ObservationRow(s?.Name ?? o.SourceId, s?.Trust ?? "", o.FullName ?? L.T("ItemCatalog.NoName"), o.Status.ToString().ToLowerInvariant(), string.Join("; ", parts));
        }).ToList();
    }

    private void ShowReview(CatalogEntry entry)
    {
        var catalog = Catalog!;
        string? decided = entry.Decision?.HasName == true ? ItemNames.Key(entry.Decision.Name) : null;
        NameChoices = entry.NameGroups.Count > 1 || entry.HasDisagreement || decided != null
            ? entry.NameGroups.Select(g =>
            {
                var first = g.FirstOrDefault(o => !o.NameIsTitle) ?? g[0];
                string sources = string.Join(", ", g.Select(o => catalog.Source(o.SourceId)?.Name ?? o.SourceId).Distinct());
                return new NameChoiceRow(this, first.Name!, first.Article, sources, decided == ItemNames.Key(first.Name));
            }).ToList()
            : [];
        CanWithdrawDecision = entry.Decision?.HasName == true;
        var lines = catalog.MappingsFrom(entry.IdType, entry.Id).Select(m => $"→ {m.ToType} {m.ToId}{(m.ToVersion != null ? $" (client {m.ToVersion})" : "")}, {m.Confidence.ToString().ToLowerInvariant()}")
            .Concat(catalog.MappingsTo(entry.IdType, entry.Id).Select(m => $"← {m.FromType} {m.FromId}{(m.FromVersion != null ? $" ({m.FromVersion})" : "")}, {m.Confidence.ToString().ToLowerInvariant()}"))
            .Take(12).ToList();
        MappingText = lines.Count == 0 ? null : "Mappings: " + string.Join("; ", lines);
    }

    /// <summary>"Use this name" in the conflict review: one of the sources' names, kept as your decision.</summary>
    internal async Task ChooseNameAsync(string name, string? article)
    {
        var catalog = Catalog;
        var entry = SelectedRow?.Entry;
        if (catalog == null || entry == null) return;
        string full = string.IsNullOrEmpty(article) ? name : article + " " + name;
        if (!ConfirmOverridingClient(entry, full)) { Services.Status.Show(L.T("ItemCatalog.NothingChanged"), StatusKind.Info); return; }
        var others = entry.NameGroups.Select(g => g[0].FullName).Where(n => !ItemNames.Same(n, name)).ToList();
        string? note = others.Count > 0 ? L.T("ItemCatalog.ChosenOver") + string.Join("; ", others) : null;
        if (!await RunAsync(L.T("ItemCatalog.SavingTheDecision"), (_, _) => Task.Run(() => ItemCatalogStore.Decide(catalog, entry, name, article, note), CancellationToken.None), operation: "import")) return;
        Refresh(entry.Key);
        Info(L.F("ItemCatalog.0ChosenFor1", full, entry.Key));
    }

    /// <summary>"Rename…" in the details: a name of your own for the selected identifier (for example after checking in the game).</summary>
    [RelayCommand]
    private async Task DecideOtherNameAsync()
    {
        if (SelectedRow?.Entry is not { } entry) return;
        string? text = Services.Dialogs.Prompt(L.T("ItemCatalog.Rename"), L.F("ItemCatalog.NameOf0InThisCatalog", entry.Key), entry.BestName ?? "");
        if (text == null) return;   // cancelled
        if (CheckNewName(entry, text, problem => Services.Status.Show(problem, StatusKind.Warning)) is not { } name) return;
        await RenameAsync(entry, name);
    }

    /// <summary>"Restore original name": removes your name; the identifier settles again on what its sources give.</summary>
    [RelayCommand(CanExecute = nameof(CanWithdrawDecision))]
    private async Task WithdrawDecisionAsync()
    {
        var catalog = Catalog;
        var entry = SelectedRow?.Entry;
        if (catalog == null || entry?.Decision?.HasName != true) return;
        if (!await RunAsync(L.T("ItemCatalog.RestoringTheOriginalName"), (_, _) => Task.Run(() => ItemCatalogStore.RestoreOriginalName(catalog, entry), CancellationToken.None), operation: "import")) return;
        Refresh(entry.Key);
        var now = catalog.Find(entry.IdType, entry.Id);
        Info(L.F("ItemCatalog.OriginalNameOf0Restored1", entry.Key, (now?.BestName is { } restored ? $"\"{restored}\"" : L.T("ItemCatalog.NoSettledName")), Describe(now?.Agreement ?? Agreement.NoName)));
    }

    // ------------------------------------------------------------------ renaming in the list

    /// <summary>The row whose name is being edited in the list (null when none).</summary>
    [ObservableProperty] private CatalogEntryRow? _editingRow;

    /// <summary>The name being typed in the list.</summary>
    [ObservableProperty] private string _editText = "";

    /// <summary>Why the typed name cannot be saved (shown under it), or null.</summary>
    [ObservableProperty] private string? _editProblem;

    /// <summary>While the confirmation for renaming a client-named object is open, the editor losing the focus does not cancel.</summary>
    private bool _confirming;

    /// <summary>F2, a double-click on the name or "Rename…" in the list's menu: edits the name of a row in place.</summary>
    [RelayCommand]
    private void BeginRename(CatalogEntryRow? row)
    {
        row ??= SelectedRow;
        if (row == null || Catalog == null || IsBusy) return;
        EndEditing();
        SelectedRow = row;
        EditText = row.Entry.BestName ?? "";
        EditProblem = null;
        row.IsEditing = true;
        EditingRow = row;
    }

    /// <summary>Enter or ✓: saves the typed name as your decision (the sources' names stay as they are).</summary>
    [RelayCommand]
    private async Task CommitRenameAsync()
    {
        if (EditingRow is not { } row) return;
        var entry = row.Entry;
        string? name;
        EditProblem = null;
        _confirming = true;
        try
        {
            name = CheckNewName(entry, EditText, problem => EditProblem = problem);
        }
        finally
        {
            _confirming = false;
        }
        if (name == null && EditProblem != null && EditingRow == row) return;   // the message is shown under the name; editing goes on
        EndEditing();
        if (name != null) await RenameAsync(entry, name);
    }

    /// <summary>Esc, ✕ or clicking elsewhere ("focus"): the typed name is dropped; nothing is saved.</summary>
    [RelayCommand]
    private void CancelRename(string? reason)
    {
        if (EditingRow == null || _confirming) return;
        EndEditing();
        if (reason == "focus") Services.Status.Show(L.T("ItemCatalog.RenameCancelledPressEnterOrClick"), StatusKind.Info);
    }

    private void EndEditing()
    {
        if (EditingRow != null) EditingRow.IsEditing = false;
        EditingRow = null;
        EditProblem = null;
    }

    /// <summary>
    /// The typed name, cleaned, when it can be saved; otherwise null after telling why through
    /// <paramref name="problem"/> (invalid) or the status bar (no change, or you kept the client's name).
    /// Renaming an object the client names itself asks once.
    /// </summary>
    private string? CheckNewName(CatalogEntry entry, string text, Action<string> problem)
    {
        if (ItemNames.CleanTyped(text, out string? why) is not { } cleaned)
        {
            problem(why! + (why == L.T("ItemModel.TypeAName") && entry.Decision?.HasName == true ? L.T("ItemCatalog.ToRemoveYourNameUseRestore") : ""));
            return null;
        }
        if (cleaned == entry.BestName)
        {
            Services.Status.Show(L.F("ItemCatalog.NoChange0IsAlreadyThe", cleaned, entry.Key), StatusKind.Info);
            return null;
        }
        if (!ConfirmOverridingClient(entry, cleaned))
        {
            Services.Status.Show(L.T("ItemCatalog.RenameCancelledTheClientSName"), StatusKind.Info);
            return null;
        }
        return cleaned;
    }

    /// <summary>True unless the client names the object itself differently and you decline using your name instead.</summary>
    private bool ConfirmOverridingClient(CatalogEntry entry, string newName)
    {
        if (entry.Official?.FullName is not { } official || official == newName) return true;
        return Services.Dialogs.Confirm(
            L.F("ItemCatalog.TheClientCallsObject01", entry.Key, official, newName), L.T("ItemCatalog.Rename"), L.T("ItemCatalog.UseMyName"), L.T("Common.Cancel"));
    }

    private async Task RenameAsync(CatalogEntry entry, string name)
    {
        var catalog = Catalog;
        if (catalog == null) return;
        if (!await RunAsync(L.T("ItemCatalog.SavingTheName"), (_, _) => Task.Run(() => ItemCatalogStore.Rename(catalog, entry, name), CancellationToken.None), operation: "import")) return;
        Refresh(entry.Key);
        Info(L.F("ItemCatalog.0RenamedTo1KeptAs", entry.Key, catalog.Find(entry.IdType, entry.Id)?.BestName));
    }

    /// <summary>
    /// After records of one identifier were added: its row shows them in place (the list keeps its
    /// selection, order and scroll position until the filters change), and the details and counts follow.
    /// </summary>
    private void Refresh(ItemKey key)
    {
        var catalog = Catalog!;
        var entry = catalog.Find(key.Type, key.Id);
        var row = _allRows.FirstOrDefault(r => r.Entry.Key == key);
        bool newSource = Sources.Count != catalog.Sources.Count;   // the first decision registers "Your decisions"
        if (entry == null || row == null) RebuildRows();
        else
        {
            row.Update(entry, catalog.SourcesById);
            if (newSource) OnPropertyChanged(nameof(SourceFilters));
        }
        if (newSource) OnPropertyChanged(nameof(LeftChoices));
        RefreshSources();
        UpdateSummary();
        if (SelectedRow?.Entry.Key == key) ShowDetails(SelectedRow.Entry);
    }

    // ------------------------------------------------------------------ comparison

    public IReadOnlyList<ComparisonSourceChoice> LeftChoices =>
        Catalog == null ? [] : [new(null, L.T("ItemCatalog.SettledNamesAllSources")), .. Catalog.Sources.Select(s => new ComparisonSourceChoice(s.Id, s.Name))];

    [ObservableProperty] private ComparisonSourceChoice? _leftChoice;
    [ObservableProperty] private CatalogSummary? _rightCatalog;
    [ObservableProperty] private IReadOnlyList<ComparisonSourceChoice> _rightChoices = [];
    [ObservableProperty] private ComparisonSourceChoice? _rightChoice;
    [ObservableProperty] private bool _compareUseMappings = true;
    [ObservableProperty] private bool _compareOtbAsObjects;
    [ObservableProperty] private bool _onlyBehaviorSensitive;
    [ObservableProperty] private IReadOnlyList<ComparisonRowItem> _comparisonRows = [];
    [ObservableProperty] private string? _comparisonSummary;
    private CatalogComparison? _comparison;
    private ItemCatalog? _rightLoaded;

    partial void OnOnlyBehaviorSensitiveChanged(bool value) => ApplyComparisonFilter();

    partial void OnRightCatalogChanged(CatalogSummary? value) => _ = LoadRightAsync(value);

    private async Task LoadRightAsync(CatalogSummary? summary)
    {
        RightChoices = [];
        RightChoice = null;
        _rightLoaded = null;
        if (summary == null) return;
        if (Catalog != null && string.Equals(summary.Folder, Catalog.Folder, StringComparison.OrdinalIgnoreCase)) _rightLoaded = Catalog;
        else
        {
            ItemCatalog? loaded = null;
            if (!await RunAsync(L.T("ItemCatalog.OpeningTheCatalogToCompareWith"), async (_, _) => loaded = await Task.Run(() => ItemCatalogStore.Load(summary.Folder)), operation: "open")) return;
            _rightLoaded = loaded;
        }
        RightChoices = [new(null, L.T("ItemCatalog.SettledNamesAllSources")), .. _rightLoaded!.Sources.Select(s => new ComparisonSourceChoice(s.Id, s.Name))];
        RightChoice = RightChoices[0];
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        var left = Catalog;
        var right = _rightLoaded;
        if (left == null || right == null || LeftChoice == null || RightChoice == null)
        {
            Services.Dialogs.Show(L.T("ItemCatalog.ChooseWhatToCompareOnBoth"), MessageKind.Info, L.T("Common.Compare"));
            return;
        }
        var options = new ComparisonOptions { UseMappings = CompareUseMappings, OtbClientIdsAreObjectIds = CompareOtbAsObjects };
        var a = new ComparisonSide(left, LeftChoice.SourceId);
        var b = new ComparisonSide(right, RightChoice.SourceId);
        CatalogComparison? result = null;
        if (!await RunAsync(L.T("ItemCatalog.Comparing"), async (_, ct) => result = await Task.Run(() => CatalogComparison.Compare(a, b, options), ct), operation: "open")) return;
        _comparison = result;
        ComparisonSummary = result!.ToText();
        ApplyComparisonFilter();
        Info(L.F("ItemCatalog.ComparisonFinished0N0Differences", result.Rows.Count));
    }

    private void ApplyComparisonFilter() =>
        ComparisonRows = _comparison == null ? [] : _comparison.Rows.Where(r => !OnlyBehaviorSensitive || r.BehaviorSensitive).Select(r => new ComparisonRowItem(r)).ToList();

    [RelayCommand]
    private async Task ExportComparisonAsync()
    {
        var comparison = _comparison;
        if (comparison == null) { Services.Dialogs.Show(L.T("ItemCatalog.CompareFirst"), MessageKind.Info, L.T("Common.Export")); return; }
        string? file = Services.Outputs.SaveFile(OutputKind.Reports, L.T("ItemCatalog.SaveTheComparison"), L.T("ItemCatalog.ExcelWorkbookXlsxXlsxCSVCsv"), "item-comparison.xlsx");
        if (file == null) return;
        await RunAsync(L.T("ItemCatalog.SavingTheComparison"), (_, ct) => Task.Run(() =>
        {
            var table = comparison.ToTable();
            string about = string.Join("\n", comparison.SummaryLines().Prepend(L.F("ItemCatalog.TibiaFoundry0ItemComparison1", Core.Common.AppIdentity.Version, DateTime.Now)));
            if (Path.GetExtension(file).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) XlsxWriter.Save(file, [("Differences", table)], about);
            else if (Path.GetExtension(file).Equals(".txt", StringComparison.OrdinalIgnoreCase)) AtomicFile.WriteAllText(file, comparison.ToText() + Environment.NewLine + ReportWriter.ToText(table));
            else ReportWriter.Save(table, file, about);
        }, ct), operation: "export", output: file, onSuccess: () => Saved(file, L.F("ItemCatalog.0N0Differences", comparison.Rows.Count)));
    }

    // ------------------------------------------------------------------ exports

    public IReadOnlyList<SettingChoice<string>> ExportScopes { get; } =
        [new("filtered", L.T("ItemCatalog.TheIdentifiersShown")), new("all", L.T("ItemCatalog.AllIdentifiers2")), new("verified", L.T("ItemCatalog.OnlyOfficialOrChosenNames")), new("unresolved", L.T("ItemCatalog.OnlyThoseWithoutASettledName"))];

    [ObservableProperty] private SettingChoice<string> _exportScope;

    private (List<CatalogEntry> Entries, string Description) Selection()
    {
        var catalog = Catalog!;
        return ExportScope.Value switch
        {
            "all" => (catalog.Entries.ToList(), L.T("ItemCatalog.AllIdentifiers")),
            "verified" => (catalog.Entries.Where(e => e.Agreement is Agreement.VerifiedOfficial or Agreement.Decided).ToList(), L.T("ItemCatalog.IdentifiersWithAnOfficialOrChosen")),
            "unresolved" => (catalog.Entries.Where(e => e.BestName == null).ToList(), L.T("ItemCatalog.IdentifiersWithoutASettledName")),
            _ => (VisibleRows.Select(r => r.Entry).ToList(), L.T("ItemCatalog.TheIdentifiersMatchingTheFilters") + DescribeFilters() + ")"),
        };
    }

    private string DescribeFilters()
    {
        var parts = new List<string>();
        if (SearchText.Trim().Length > 0) parts.Add(L.F("ItemCatalog.Search0", SearchText.Trim()));
        if (AgreementFilter?.Value != null) parts.Add(AgreementFilter.Label);
        if (SourceFilter?.Value != null) parts.Add(SourceFilter.Label);
        if (OnlyUnnamedInClient) parts.Add(L.T("ItemCatalog.NotNamedByTheClient3"));
        if (OnlyDisagreements) parts.Add(L.T("ItemCatalog.DifferingNames2"));
        return parts.Count == 0 ? L.T("ItemCatalog.NoFilter") : string.Join(", ", parts);
    }

    [RelayCommand] private Task ExportNameListAsync() => ExportAsync("names", L.T("ItemCatalog.NameListFilter"), ".txt");
    [RelayCommand] private Task ExportCsvAsync() => ExportAsync("csv", "CSV, one row per identifier (*.csv)|*.csv", ".csv");
    [RelayCommand] private Task ExportRecordsAsync() => ExportAsync("records", "CSV, every record with its source (*.csv)|*.csv", "-records.csv");
    [RelayCommand] private Task ExportJsonAsync() => ExportAsync("json", L.T("ItemCatalog.CatalogExportFilter"), ".json");
    [RelayCommand] private Task ExportXlsxAsync() => ExportAsync("xlsx", L.T("ItemCatalog.ExcelWorkbookFilter"), ".xlsx");

    private async Task ExportAsync(string format, string filter, string suffix)
    {
        var catalog = Catalog;
        if (catalog == null) { Services.Dialogs.Show(L.T("ItemCatalog.OpenOrCreateACatalogFirst"), MessageKind.Info, L.T("Common.Export")); return; }
        var (entries, description) = Selection();
        if (entries.Count == 0) { Services.Dialogs.Show(L.T("ItemCatalog.TheSelectionIsEmptyNothingTo"), MessageKind.Info, L.T("Common.Export")); return; }
        string? file = Services.Outputs.SaveFile(OutputKind.DataExports, L.T("ItemCatalog.ExportTheItemCatalog"), filter, ItemCatalogStore.SafeId(catalog.Name) + suffix);
        if (file == null) return;
        await RunAsync(L.T("ItemCatalog.ExportingTheCatalog"), (_, ct) => Task.Run(() =>
        {
            var idType = entries.Select(e => e.IdType).GroupBy(t => t).OrderByDescending(g => g.Count()).First().Key;
            string header = ItemExports.Header(catalog, idType, description);
            switch (format)
            {
                case "names": AtomicFile.WriteAllText(file, ItemExports.ToNameList(catalog, entries, idType, description)); break;
                case "json": AtomicFile.WriteAllText(file, ItemExports.ToJson(catalog, entries, description)); break;
                case "records": ReportWriter.Save(ItemExports.ObservationsTable(catalog, entries), file, header); break;
                case "xlsx":
                    XlsxWriter.Save(file, [("Items", ItemExports.EntriesTable(catalog, entries)), ("Records", ItemExports.ObservationsTable(catalog, entries)),
                        ("Sources", ItemExports.SourcesTable(catalog))], header);
                    break;
                default: ReportWriter.Save(ItemExports.EntriesTable(catalog, entries), file, header); break;
            }
        }, ct), operation: "export", output: file, onSuccess: () => Saved(file, L.F("ItemCatalog.0N0Identifiers", entries.Count)));
    }
}
