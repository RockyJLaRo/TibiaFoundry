using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;
using TibiaFoundry.Core.Net;
using TibiaFoundry.Core.Wiki;

namespace TibiaFoundry.App.ViewModels;

/// <summary>One editable parameter of the infobox.</summary>
public sealed partial class InfoboxFieldRow : ObservableObject
{
    private readonly Action _changed;

    public InfoboxFieldRow(WikiField field, Action changed)
    {
        Field = field;
        _changed = changed;
        _value = field.Value;
    }

    public WikiField Field { get; private set; }
    public string Name => Field.Name;
    public bool Required => Field.Required;
    public bool Multiline => Field.Multiline;
    public string? Hint => Field.Hint;

    /// <summary>The source shown next to the value; changes to "Entered by you" when the user edits it.</summary>
    public string SourceText => Field.Source switch
    {
        WikiFieldSource.Client => L.T("Infobox.SourceClient"),
        WikiFieldSource.Derived => L.T("Infobox.SourceDerived"),
        WikiFieldSource.TibiaWiki => L.T("Infobox.SourceTibiaWiki"),
        WikiFieldSource.Placeholder => L.T("Infobox.SourcePlaceholder"),
        WikiFieldSource.Missing => L.T("Infobox.SourceMissing"),
        _ => L.T("Infobox.SourceUser"),
    };

    public bool NeedsInput => Required && string.IsNullOrWhiteSpace(Value);
    public bool NeedsReview => Field.Source == WikiFieldSource.Derived;

    [ObservableProperty] private string _value;

    partial void OnValueChanged(string value)
    {
        Field = Field with { Value = value, Source = WikiFieldSource.User };
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(NeedsInput));
        OnPropertyChanged(nameof(NeedsReview));
        _changed();
    }
}

/// <summary>
/// Appearances → "Wiki infobox": TibiaWiki's <c>{{Infobox Object}}</c> for the selected object, built from the client's
/// data, editable before it is copied or saved (and as files for every selected object).
/// </summary>
public sealed partial class InfoboxViewModel : ObservableObject
{
    private readonly AppearancesViewModel _owner;
    private readonly AppServices _services;
    private WikiTemplate? _template;
    private uint _objectId;
    private IReadOnlyDictionary<uint, string>? _wikiNames;
    private bool _wikiNamesLoaded;

    public InfoboxViewModel(AppearancesViewModel owner, AppServices services)
    {
        _owner = owner;
        _services = services;
    }

    public ObservableCollection<InfoboxFieldRow> Fields { get; } = [];

    [ObservableProperty] private string _markup = "";
    [ObservableProperty] private string _warnings = "";
    [ObservableProperty] private string _emptyMessage = "";
    [ObservableProperty] private bool _hasObject;
    [ObservableProperty] private bool _useQuestionMark;
    [ObservableProperty] private bool _includeEmptyRequired = true;
    [ObservableProperty] private string _wikiNamesInfo = "";
    [ObservableProperty] private IReadOnlyList<string> _addableParameters = Array.Empty<string>();
    [ObservableProperty] private string? _parameterToAdd;

    public int MissingCount => Fields.Count(f => f.NeedsInput);
    public int ReviewCount => Fields.Count(f => f.NeedsReview);
    public string Summary => L.F("Infobox.Summary", Fields.Count, MissingCount, ReviewCount);

    partial void OnUseQuestionMarkChanged(bool value) => Regenerate();
    partial void OnIncludeEmptyRequiredChanged(bool value) => Regenerate();

    private WikiWriteOptions Options => new(IncludeEmptyRequired, UseQuestionMark ? "?" : "");

    /// <summary>Called when the selection or the loaded database changes.</summary>
    public void OnSelectionChanged()
    {
        var item = _owner.SelectedItem;
        var db = _owner.Database;
        Fields.Clear();
        _template = null;
        if (db == null || item == null)
        {
            Show(L.T("Infobox.SelectAnObject"));
            return;
        }
        if (item.Kind != AppearanceKind.Object)
        {
            Show(L.T("Infobox.OnlyObjects"));
            return;
        }
        EnsureWikiNames();
        _objectId = item.Id;
        _template = Build(item.Appearance, db);
        foreach (var f in _template.Fields) Fields.Add(new InfoboxFieldRow(f, Regenerate));
        Warnings = string.Join("\n", _template.Warnings.Select(w => "• " + w));
        EmptyMessage = "";
        HasObject = true;
        Regenerate();
    }

    private WikiTemplate Build(Tibia.Protobuf.Appearances.Appearance obj, AppearanceDatabase db) =>
        InfoboxObjectBuilder.Build(obj, db, _wikiNames != null && _wikiNames.TryGetValue(obj.Id, out var n) ? n : null);

    private void Show(string message)
    {
        HasObject = false;
        EmptyMessage = message;
        Markup = Warnings = "";
        AddableParameters = Array.Empty<string>();
        RaiseCounts();
    }

    /// <summary>Uses TibiaWiki's article names when the item list was downloaded before (NPC trades, or the button here); never goes online by itself.</summary>
    private void EnsureWikiNames()
    {
        if (_wikiNamesLoaded) return;
        _wikiNamesLoaded = true;
        var cached = WikiItemIdTable.TryReadCache(_services.CacheDirectory);
        SetWikiNames(cached);
    }

    private void SetWikiNames(WikiItemIds? table)
    {
        _wikiNames = table?.Names;
        WikiNamesInfo = table == null ? L.T("Infobox.NoWikiNames") : L.F("Infobox.WikiNamesFrom", table.Names.Count, table.FetchedAtUtc.ToLocalTime());
    }

    private WikiTemplate Current()
    {
        var t = _template!;
        return t with { Fields = Fields.Select(r => r.Field).ToList() };
    }

    private void Regenerate()
    {
        if (_template == null) { Markup = ""; RaiseCounts(); return; }
        Markup = WikiTemplateWriter.Write(Current(), Options);
        var present = Fields.Select(f => f.Name).ToHashSet();
        AddableParameters = InfoboxObjectBuilder.ParameterOrder.Where(p => !present.Contains(p)).ToList();
        RaiseCounts();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(ReviewCount));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Adds an optional parameter the client does not know (attack, armor, attrib…), in the template's order.</summary>
    [RelayCommand]
    private void AddParameter()
    {
        if (_template == null || string.IsNullOrEmpty(ParameterToAdd)) return;
        string name = ParameterToAdd;
        if (Fields.Any(f => f.Name == name)) return;
        var order = InfoboxObjectBuilder.ParameterOrder;
        int rank = order.ToList().IndexOf(name);
        int index = Fields.TakeWhile(f => order.ToList().IndexOf(f.Name) < rank).Count();
        var field = new WikiField(name, "", WikiFieldSource.User, InfoboxObjectBuilder.IsRequired(name, Fields.Any(f => f.Name == "pickupable" && f.Value == "yes")),
            name is "notes" or "notes2" or "history" or "flavortext" or "location");
        Fields.Insert(index, new InfoboxFieldRow(field, Regenerate));
        ParameterToAdd = null;
        Regenerate();
    }

    /// <summary>Discards the edits and builds the infobox again from the client's data.</summary>
    [RelayCommand]
    private void Reset() => OnSelectionChanged();

    [RelayCommand]
    private void Copy()
    {
        if (Markup.Length == 0) return;
        if (_services.Clipboard.SetText(Markup)) _services.Status.Show(L.T("Infobox.Copied"), StatusKind.Success);
    }

    [RelayCommand]
    private void Save()
    {
        if (_template == null) return;
        string? file = _services.Outputs.SaveFile(OutputKind.DataExports, L.T("Infobox.SaveTitle"), L.T("Infobox.TextFilter"),
            InfoboxObjectBuilder.FileNameFor(Current(), _objectId));
        if (file == null) return;
        try
        {
            AtomicFile.WriteAllText(file, Markup + "\n");
            _services.Status.ShowSaved(file, isFile: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Dialogs.ShowError(L.T("Infobox.CouldNotSave"), ex);
        }
    }

    /// <summary>One .txt per selected object (built from the client's data; edits made here apply only to the current object).</summary>
    [RelayCommand]
    private async Task ExportSelectedAsync()
    {
        var db = _owner.Database;
        var items = _owner.TargetItems.Where(i => i.Kind == AppearanceKind.Object).ToList();
        if (db == null || items.Count == 0) { _services.Dialogs.Show(L.T("Infobox.SelectAnObject")); return; }
        string? folder = _services.Outputs.PickFolder(OutputKind.DataExports, L.T("Infobox.FolderTitle"));
        if (folder == null) return;
        EnsureWikiNames();
        var options = Options;
        var edited = _template != null ? Current() : null;
        uint editedId = _objectId;
        int written = 0;
        await _owner.RunExternalAsync(L.T("Infobox.Exporting"), async (progress, ct) =>
        {
            await Task.Run(() =>
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < items.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var item = items[i];
                    var template = item.Id == editedId && edited != null ? edited : Build(item.Appearance, db);
                    string name = InfoboxObjectBuilder.FileNameFor(template, item.Id);
                    string path = InfoboxObjectBuilder.UniquePath(folder, name, used);
                    AtomicFile.WriteAllText(path, WikiTemplateWriter.Write(template, options) + "\n");
                    written++;
                    progress.Report(new OperationProgress(i + 1, items.Count, name));
                }
            }, ct);
        }, operation: "infobox-export", output: folder, onSuccess: () => _services.Status.ShowSaved(folder, isFile: false, before: L.F("Infobox.Exported", written)));
    }

    /// <summary>Downloads (or refreshes) TibiaWiki's item list so the infobox uses the wiki's article names.</summary>
    [RelayCommand]
    private async Task GetWikiNamesAsync()
    {
        WikiItemIds? table = null;
        string? problem = null;
        await _owner.RunExternalAsync(L.T("Infobox.DownloadingNames"), async (progress, ct) =>
        {
            try { table = await WikiItemIdTable.GetAsync(_services.Http, _services.CacheDirectory, progress: progress, ct: ct); }
            catch (Exception ex) when (ex is HttpFetchException or InvalidDataException) { problem = ex.Message; }
        }, operation: "wiki-item-ids");
        if (problem != null) { _services.Dialogs.Show(L.F("Infobox.NamesFailed", problem), MessageKind.Warning, L.T("Infobox.Title")); return; }
        if (table == null) return;
        SetWikiNames(table);
        if (table.Notice != null) _services.Status.Show(table.Notice, StatusKind.Warning);
        OnSelectionChanged();
    }
}
