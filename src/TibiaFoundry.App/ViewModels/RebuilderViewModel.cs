using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>An archived revision as the rebuilder lists it.</summary>
public sealed record RebuildChoice(string Key, string Label, string Server, string Version, string Scope, string Completeness, string Storage, string Archived,
    int Files, long Bytes, bool IsInstallation, bool IsUsable, string Source)
{
    public string Size => ReportWriter.FormatSize(Bytes);
    public string Kind => IsInstallation ? L.T("Rebuilder.RebuildInstallation") : L.T("Rebuilder.RestoreFolder");
    public string Title => $"{Label} · {ClientServers.Display(Server)}";
}

/// <summary>
/// Client Installation Rebuilder (8.0): makes a normal folder from an archived revision, only from that
/// revision's own manifest and the content it references (never from an installed client). A complete
/// installation is rebuilt; any other revision is restored as the folder it was. The output folder
/// must be new or empty: nothing is ever replaced. Every file is verified before the folder is used,
/// and the report says what was restored; it never claims the client will run.
/// </summary>
public sealed partial class RebuilderViewModel : PageViewModel
{
    public RebuilderViewModel(AppServices services) : base(services, BuiltInTools.Rebuilder)
    {
    }

    private ClientLibrary Library => Services.Clients;

    [ObservableProperty] private IReadOnlyList<RebuildChoice> _revisions = [];
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RebuildCommand))] private RebuildChoice? _selected;
    [ObservableProperty] private string _details = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RebuildCommand))] private string _outputFolder = "";
    [ObservableProperty] private string _planText = "";
    [ObservableProperty] private string? _report;
    [ObservableProperty] private string? _lastOutput;

    public string Empty => Library.StoreExists ? "" : L.T("Rebuilder.NothingIsArchivedYetArchiveA");

    public override Task OnNavigatedToAsync()
    {
        Refresh();
        return Task.CompletedTask;
    }

    /// <summary>Opens the page with a revision selected (from the Clients page).</summary>
    public void Select(string key)
    {
        Refresh();
        Selected = Revisions.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Selected;
    }

    [RelayCommand]
    public void Refresh()
    {
        string? keep = Selected?.Key;
        var list = new List<RebuildChoice>();
        try
        {
            if (Library.ExistingStore is { } store)
                foreach (var v in store.Registry.Versions.OrderBy(v => v.Server).ThenByDescending(v => v.ImportedAtUtc))
                {
                    string source = v.RegistrationId != null && store.Registry.Registration(v.RegistrationId) is { } r ? r.Label : L.T("Rebuilder.ArchivedEarlier");
                    string completeness = v.EffectiveScope == ManifestScopes.TibiaFoundryFiles ? L.T("Rebuilder.TibiaFoundryFilesOnly73")
                        : v.Complete ? "Complete" : v.ExcludedFiles > 0 ? L.F("Rebuilder.PersonalDataLeftOut0N0", v.ExcludedFiles) : "Incomplete";
                    list.Add(new RebuildChoice(v.Key, v.Label, v.Server, v.ClientVersion ?? "unknown", ManifestScopes.Display(v.EffectiveScope), completeness,
                        ManifestStorage.Display(v.EffectiveStorage), v.ImportedAtUtc == default ? "" : v.ImportedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                        v.Files, v.Bytes, v.IsCompleteInstallation, v.IsUsable, source));
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Client archive: " + ex.Message);
        }
        Revisions = list;
        // Opened from a tool that needs a folder: the revision in use comes first.
        string? active = Services.Settings.ActiveClient(Services.Settings.ActiveScope);
        Selected = list.FirstOrDefault(r => r.Key == keep) ?? list.FirstOrDefault(r => r.Key == active) ?? list.FirstOrDefault();
        OnPropertyChanged(nameof(Empty));
    }

    partial void OnSelectedChanged(RebuildChoice? value)
    {
        Details = value == null ? "" :
            L.F("Rebuilder.012Version34", value.Kind, value.Title, Environment.NewLine, value.Version, value.Scope, value.Completeness, value.Storage, Environment.NewLine, value.Files, value.Size, value.Archived, value.Source) +
            (value.IsInstallation ? "" : Environment.NewLine + L.T("Rebuilder.ThisRevisionIsNotAComplete")) +
            (value.IsUsable ? "" : Environment.NewLine + L.T("Rebuilder.ThisRevisionIsDamagedOrMissing"));
        UpdatePlan();
    }

    partial void OnOutputFolderChanged(string value) => UpdatePlan();

    private IReadOnlyList<string> ForbiddenRoots() => [TibiaClientPaths.PackagesRoot, .. Library.ProtectedFolders()];

    private void UpdatePlan()
    {
        if (Selected == null || string.IsNullOrWhiteSpace(OutputFolder)) { PlanText = ""; return; }
        try
        {
            var plan = Library.Store.PlanRebuild(Selected.Key, OutputFolder, ForbiddenRoots());
            PlanText = L.F("Rebuilder.0IsNeeded", ReportWriter.FormatSize(plan.LogicalBytes)) +
                       (plan.AvailableBytes is { } free ? L.F("Rebuilder.TheDriveHas0Free", ReportWriter.FormatSize(free)) : ".") +
                       (plan.TargetExists ? L.T("Rebuilder.TheOutputFolderExistsAndIs") : L.T("Rebuilder.TheOutputFolderIsCreated")) +
                       (plan.Blockers.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, plan.Blockers) : "");
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            PlanText = ex.Message;
        }
    }

    /// <summary>Choose where the result goes: a new or empty folder (a folder that has files gets a new subfolder instead).</summary>
    [RelayCommand]
    private void ChooseOutput()
    {
        if (Selected == null) return;
        string? picked = Services.Dialogs.PickFolder(L.T("Rebuilder.WhereTheRebuiltFolderGoesAn"));
        if (picked == null) return;
        string folder = picked;
        bool hasFiles;
        try
        {
            hasFiles = Directory.Exists(picked) && Directory.EnumerateFileSystemEntries(picked).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Services.Dialogs.Show(L.F("Rebuilder.0CannotBeRead1Choose", picked, ex.Message), MessageKind.Warning, Selected.Kind);
            return;
        }
        if (hasFiles)
        {
            string name = FileNames.Sanitize(Selected.Label + " (rebuilt)");
            string sub = Path.Combine(picked, name);
            for (int i = 2; Directory.Exists(sub) || File.Exists(sub); i++) sub = Path.Combine(picked, $"{name} {i}");
            if (!Services.Dialogs.Confirm(L.F("Rebuilder.0IsNotEmptyAndA", picked, Path.GetFileName(sub)),
                    Selected.Kind, L.T("Rebuilder.UseANewFolder"), L.T("Rebuilder.ChooseAnother"))) return;
            folder = sub;
        }
        OutputFolder = folder;
    }

    private bool CanRebuild() => Selected is { IsUsable: true } && !string.IsNullOrWhiteSpace(OutputFolder);

    [RelayCommand(CanExecute = nameof(CanRebuild))]
    private async Task RebuildAsync()
    {
        if (Selected is not { } choice) return;
        RebuildPlan plan;
        try
        {
            plan = Library.Store.PlanRebuild(choice.Key, OutputFolder, ForbiddenRoots());
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Services.Dialogs.Show(ex.Message, MessageKind.Warning, choice.Kind);
            return;
        }
        if (plan.Blockers.Count > 0)
        {
            Services.Dialogs.Show(string.Join(Environment.NewLine, plan.Blockers), MessageKind.Warning, choice.Kind);
            return;
        }
        var question = new DialogReport
        {
            Title = plan.Kind, Kind = MessageKind.Question, Heading = $"{plan.Kind}: {choice.Title}?",
            Summary = choice.IsInstallation ? L.T("Rebuilder.ACompleteInstallationIsRebuiltFrom") : L.T("Rebuilder.TheRevisionIsRestoredAsThe"),
            Sections =
            [
                new ReportSection(L.T("Rebuilder.Revision"))
                {
                    Rows = [new(L.T("Common.Version"), choice.Version), new(L.T("Common.Server"), choice.Server), new(L.T("Rebuilder.Completeness"), choice.Completeness), new(L.T("Common.Files"), $"{L.Count(choice.Files, "Noun.file")}, {ReportWriter.FormatSize(plan.LogicalBytes)}")],
                },
                new ReportSection(L.T("Common.OutputFolder"))
                {
                    Rows = [ReportRow.Path(L.T("ClientConverter.Into"), plan.Target), new(L.T("Rebuilder.FreeSpace"), plan.AvailableBytes is { } free ? ReportWriter.FormatSize(free) : L.T("Common.Unknown"))],
                },
                new ReportSection(L.T("Rebuilder.Safety"))
                {
                    Bullets =
                    [
                        L.T("Rebuilder.EveryFileIsWrittenNextTo"),
                        L.T("Rebuilder.NothingIsReplacedTheOutputFolder"),
                        L.T("Rebuilder.TheArchiveAndTheInstalledClient"),
                    ],
                },
            ],
            PrimaryText = plan.Kind, CancelText = L.T("Common.Cancel"),
        };
        if (Services.Dialogs.ShowReport(question) != ReportAnswer.Primary) return;
        RebuildReport? result = null;
        await RunAsync(plan.Kind, async (p, ct) => await Task.Run(() => result = Library.Store.Rebuild(plan, p, ct), ct), operation: "rebuild", output: plan.Target);
        if (result == null) return;
        Report = result.ToText();
        string? saved = SaveReport(result);
        if (result.Success)
        {
            LastOutput = result.Target;
            Services.Status.ShowSaved(result.Target, isFile: false, before: L.F("Rebuilder.0Finished", result.Kind), after: L.F("Rebuilder.0N0FilesVerified1", result.Files, (saved != null ? L.T("Rebuilder.ReportInReports") : "")));
            if (Services.Dialogs.Confirm(L.T("Rebuilder.AddTheRebuiltFolderToThe"), result.Kind, L.T("Rebuilder.AddIt"), L.T("Rebuilder.NotNow")))
                AddAsClient(result, choice);
        }
        else Services.Dialogs.Show(L.F("Rebuilder.0FailedNothingWasLeftIn", result.Kind, result.Target, Environment.NewLine, Environment.NewLine, string.Join(Environment.NewLine, result.Problems.Take(8))),
            MessageKind.Warning, result.Kind);
        OutputFolder = "";
        Refresh();
    }

    private void AddAsClient(RebuildReport result, RebuildChoice choice)
    {
        try
        {
            var r = Library.Store.RegisterFolder(result.Target, choice.Server, L.F("Rebuilder.0Rebuilt", choice.Label), SourceTypes.Reconstructed, choice.Key);
            Services.Status.Show(L.F("Rebuilder.Added0ToTheClientsPage", r.Label), StatusKind.Success);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Services.Dialogs.Show(ex.Message, MessageKind.Warning, result.Kind);
        }
    }

    /// <summary>The report goes to Documents\Tibia_Foundry\Reports, never into the rebuilt folder (an extra file would change its content).</summary>
    private string? SaveReport(RebuildReport report)
    {
        try
        {
            var state = Services.Outputs.Prepare(OutputKind.Reports);
            if (!state.Usable) return null;
            string file = FileNames.Unique(state.Path, $"Client-rebuild-{FileNames.Sanitize(report.Label)}-{DateTime.Now:yyyyMMdd-HHmmss}", ".txt");
            AtomicFile.WriteAllText(file, report.ToText());
            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("The rebuild report could not be saved: " + ex.Message);
            return null;
        }
    }

    [RelayCommand]
    private void OpenOutput()
    {
        if (LastOutput != null && Directory.Exists(LastOutput)) Services.Shell.OpenFolder(LastOutput);
    }
}
