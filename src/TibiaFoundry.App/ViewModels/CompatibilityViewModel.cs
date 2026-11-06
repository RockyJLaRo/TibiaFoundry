using TibiaFoundry.Core.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>One component row of the Compatibility page.</summary>
public sealed record CompatibilityRow(string Component, string? FileName, CompatibilityStatus Status, string StatusText, string Summary,
    IReadOnlyList<string> Details, string Affected)
{
    /// <summary>The location "Locate…"/"Change…" picks (see <see cref="LocationIds"/>), or null when the row has none.</summary>
    public string? LocationId { get; init; }

    /// <summary>Full path that was checked, or where it was expected.</summary>
    public string? Path { get; init; }

    public bool Required { get; init; } = true;

    /// <summary>"chosen by you", "found automatically", …</summary>
    public string? SourceText { get; init; }

    /// <summary>Locations of this row the user chose ("Reset to automatic" forgets them).</summary>
    public IReadOnlyList<string> ChosenLocationIds { get; init; } = [];

    /// <summary>A location the user chose for this row is unavailable (the automatic one is used meanwhile).</summary>
    public bool ChosenUnavailable { get; init; }

    public bool ShowAffected => Status > CompatibilityStatus.Unfamiliar && Required && Affected.Length > 0;
    public bool HasDetails => Details.Count > 0;
    public bool CanLocate => LocationId != null;
    public bool IsChosen => ChosenLocationIds.Count > 0;

    /// <summary>True when something has to be found: the button says "Locate…" instead of "Change…".</summary>
    public bool NeedsLocation => Status is CompatibilityStatus.Missing or CompatibilityStatus.Unsupported || ChosenUnavailable;

    public string LocateText => NeedsLocation ? L.T("Main.Locate") : L.T("Common.Change");
}

/// <summary>
/// "Compatibility": shows whether the Tibia client's data can be processed by this version —
/// client version, every data file's status, what is affected, and what to do. Every row with a
/// file or folder can be located or changed in place; the choice is checked, saved and the row is
/// checked again without a restart.
/// </summary>
public sealed partial class CompatibilityViewModel : PageViewModel
{
    public CompatibilityViewModel(AppServices services) : base(services, BuiltInTools.Compatibility)
    {
        services.Data.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ClientDataService.Compatibility)) Apply(Services.Data.Compatibility);
            if (e.PropertyName is nameof(ClientDataService.Sprites) or nameof(ClientDataService.SpriteFolderNotice)) OnPropertyChanged(nameof(SpriteFolderNotice));
        };
    }

    [ObservableProperty] private IReadOnlyList<CompatibilityRow> _rows = Array.Empty<CompatibilityRow>();
    [ObservableProperty] private CompatibilityStatus? _overall;
    [ObservableProperty] private string _overallText = L.T("Compatibility.NotCheckedYet");
    [ObservableProperty] private string _clientText = "";
    [ObservableProperty] private string _checkedText = "";
    [ObservableProperty] private string _advice = L.T("Compatibility.ClickCheckAgain");

    public string? SpriteFolderNotice => Services.Data.SpriteFolderNotice;

    public override async Task OnNavigatedToAsync()
    {
        Apply(Services.Data.Compatibility);
        if (Services.Data.Compatibility == null && !Services.Data.IsCheckingCompatibility && !IsBusy) await CheckAsync();
    }

    [RelayCommand]
    private Task CheckAsync() =>
        RunAsync(L.T("Compatibility.CheckingTheClientData"), async (p, ct) => await Services.Data.CheckCompatibilityAsync(p, ct), L.T("Compatibility.CompatibilityCheckFinished"));

    /// <summary>"Locate…" / "Change…": pick the row's file or folder; it is checked, saved and the row is checked again.</summary>
    [RelayCommand]
    private async Task LocateAsync(CompatibilityRow? row)
    {
        if (row?.LocationId is not { } id || IsBusy || Services.Data.IsCheckingCompatibility) return;
        await Services.Locations.LocateAsync(id);
    }

    /// <summary>"Reset to automatic": forgets the locations chosen for the row.</summary>
    [RelayCommand]
    private async Task ResetLocationAsync(CompatibilityRow? row)
    {
        if (row == null || !row.IsChosen || IsBusy || Services.Data.IsCheckingCompatibility) return;
        foreach (string id in row.ChosenLocationIds) await Services.Locations.ResetAsync(id);
    }

    [RelayCommand]
    private void CopyReport()
    {
        if (Services.Data.Compatibility is not { } report) return;
        string text = report.ToText();
        if (SpriteFolderNotice != null) text += Environment.NewLine + L.T("Compatibility.SpritesFolder") + SpriteFolderNotice + Environment.NewLine;
        text += Environment.NewLine + Services.Data.Locations.Describe();
        text += L.F("Compatibility.01Log23", Environment.NewLine, AppIdentity.NameAndVersion, AppLog.LogFilePath, Environment.NewLine);
        if (Services.Clipboard.SetText(text)) Info(L.T("Compatibility.CompatibilityReportCopiedToTheClipboard"));
    }

    [RelayCommand] private void OpenLogFolder() => Services.Shell.OpenFolder(AppLog.LogFilePath);
    [RelayCommand] private void OpenClientFolder() => Services.Shell.OpenFolder(Services.Data.AssetsDirectory);
    [RelayCommand] private void OpenLocationSettings() => Services.RequestNavigate<SettingsViewModel>();

    /// <summary>Locations shown with a row: the packages folder belongs to the package row.</summary>
    private static IEnumerable<string> LocationsOf(string component) =>
        LocationResolver.All.Where(d => d.Component == component || component == CompatibilityChecker.PackageComponent && d.Id == LocationIds.PackagesFolder).Select(d => d.Id);

    private CompatibilityRow ToRow(ComponentReport c)
    {
        var locations = Services.Data.Locations;
        var ids = LocationsOf(c.Component).ToList();
        var chosen = ids.Where(id => locations.ChosenPath(id) != null).ToList();
        var resolved = c.LocationId != null ? locations.Resolve(c.LocationId) : null;
        string status = c.Required || c.Status != CompatibilityStatus.Missing ? CompatibilityReport.Describe(c.Status) : L.T("Compatibility.NotFoundOptional");
        return new CompatibilityRow(c.Component, c.FileName, c.Status, status, c.Summary, c.Details, string.Join(", ", c.AffectedFeatures))
        {
            LocationId = c.LocationId,
            Path = c.Path,
            Required = c.Required,
            SourceText = resolved?.SourceText,
            ChosenLocationIds = chosen,
            ChosenUnavailable = ids.Any(id => locations.Resolve(id).ChosenUnavailable),
        };
    }

    private void Apply(CompatibilityReport? report)
    {
        if (report == null)
        {
            Rows = Array.Empty<CompatibilityRow>();
            Overall = null;
            OverallText = Services.Data.IsCheckingCompatibility ? "Checking…" : L.T("Compatibility.NotCheckedYet");
            ClientText = Services.Data.ClientVersion is { } v ? L.F("Compatibility.TibiaClient0", v) : L.T("Compatibility.TibiaClientVersionUnknown");
            CheckedText = "";
            Advice = L.T("Compatibility.TheCheckRunsAutomaticallyAfterStart");
            return;
        }
        Overall = report.Overall;
        OverallText = CompatibilityReport.Describe(report.Overall);
        ClientText = L.F("Compatibility.TibiaClient0", report.ClientVersion ?? L.T("Compatibility.VersionUnknown")) + (report.VersionValidated ? L.T("Compatibility.ValidatedWithThisVersion") : L.T("Compatibility.NotValidatedWithThisVersion")) +
                     $" · {Services.Data.PackageDisplayName}";
        CheckedText = L.F("Compatibility.Checked0YyyyMMDdHH", report.CheckedAtUtc.ToLocalTime(), report.Duration.TotalSeconds, report.PackageDirectory);
        Rows = report.Components.Select(ToRow).ToList();
        Advice = report.Overall switch
        {
            CompatibilityStatus.Supported => L.T("Compatibility.EverythingTibiaFoundryReadsWasValidated"),
            CompatibilityStatus.Unfamiliar => L.T("Compatibility.ThisClientWasNotTestedWith"),
            CompatibilityStatus.Partial => L.T("Compatibility.SomeRecordsCouldNotBeUsed"),
            CompatibilityStatus.Missing => L.T("Compatibility.SomeClientFilesAreMissingUse"),
            _ => L.T("Compatibility.SomeFilesCouldNotBeRead"),
        };
    }
}
