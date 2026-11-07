using TibiaFoundry.Core.Compatibility;
using CommunityToolkit.Mvvm.ComponentModel;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Localization;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Tools;

namespace TibiaFoundry.App.ViewModels;

/// <summary>
/// Base class of every page in the navigation rail. A page is described by its
/// <see cref="ToolManifest"/> (name, icon, group, capabilities, dependencies); long operations go
/// through <see cref="RunAsync"/>, which gives every tool the same dependency checks, queueing,
/// progress, cancellation, logging, operation history and success/failure messages.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The token source only lives for the duration of RunAsync and is disposed there.")]
public abstract partial class PageViewModel(AppServices services, ToolManifest manifest) : ObservableObject
{
    protected AppServices Services { get; } = services;

    /// <summary>The page's entry in the tool registry.</summary>
    public ToolManifest Manifest { get; } = manifest;

    public virtual string Title => ToolText.Name(Manifest);

    /// <summary>Segoe Fluent/MDL2 icon glyph for the navigation rail.</summary>
    public virtual string Glyph => Manifest.Glyph;

    /// <summary>Short description shown under the page title.</summary>
    public virtual string Subtitle => ToolText.Description(Manifest);

    /// <summary>Navigation group header shown above this page.</summary>
    public virtual string Group => ToolText.Group(Manifest.Category);

    /// <summary>
    /// After a language switch: re-reads every computed text of the page. Pages whose lists hold
    /// translated text built from data override this to rebuild those lists; entered values,
    /// selections and settings are kept.
    /// </summary>
    public virtual void RefreshLanguage()
    {
        OnPropertyChanged(string.Empty);
        RefreshAvailability();
    }

    [ObservableProperty] private bool _isBusy;

    /// <summary>
    /// Set when client data this page depends on is not fully compatible (shown as a banner under
    /// the page title, so affected features are clearly marked while the rest stays usable).
    /// </summary>
    [ObservableProperty] private string? _compatibilityNotice;

    /// <summary>The client location the compatibility banner offers to locate (a missing or unreadable file this page needs), or null.</summary>
    [ObservableProperty] private string? _compatibilityLocation;

    /// <summary>Set when a dependency the page needs is missing (shown as a banner; affected operations do not start).</summary>
    [ObservableProperty] private string? _dependencyNotice;

    /// <summary>The client location the dependency banner offers to locate (the client files are missing), or null.</summary>
    [ObservableProperty] private string? _dependencyLocation;

    /// <summary>Compatibility components (see <c>CompatibilityChecker</c>) whose problems affect this page.</summary>
    public virtual IReadOnlyList<string> DataComponents => Array.Empty<string>();

    private CancellationTokenSource? _cts;

    /// <summary>The page's running operation in the operation tracker (Diagnostics), or null.</summary>
    public OperationRecord? CurrentOperation { get; private set; }

    /// <summary>Called whenever the page becomes visible.</summary>
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;

    /// <summary>
    /// After "Change file" picked a client data file other than the one in use: asks whether to use
    /// it from now on (it is checked and saved as the client's location) or only until the program
    /// closes or the client changes. Not asked when the file is already the one in use.
    /// </summary>
    protected async Task OfferAsDefaultAsync(string locationId, string file)
    {
        if (string.Equals(Services.Data.DefaultFile(locationId), file, StringComparison.OrdinalIgnoreCase)) return;
        var def = Core.Locations.LocationResolver.Definition(locationId);
        string what = char.ToLowerInvariant(def.Name[0]) + def.Name[1..] + LocationService.ClientSuffix(locationId, Services.Settings.ActiveScope);
        if (Services.Dialogs.Confirm(L.F("Page.Use0AsThe1From", Path.GetFileName(file), what, file), def.Name, L.T("Page.UseFromNowOn"), L.T("Page.OnlyThisTime")))
            await Services.Locations.UseAsync(locationId, file);
    }

    /// <summary>
    /// For tools that work on a real assets folder (Assets Editor, Client Converter): when the client in
    /// use is an archived revision, which has no folder of its own, explains how to get one (the Client
    /// Installation Rebuilder) and offers to open it. Returns true when the tool can go on.
    /// </summary>
    protected bool RequireRealClientFolder(string tool)
    {
        if (Services.Data.ImportedVersion is not { } version) return true;
        if (Services.Dialogs.Confirm(L.F("Page.TheClientInUseIsAn", version.Entry.Label, tool), tool, L.T("Page.OpenTheRebuilder"), L.T("Common.Cancel")))
            Services.RequestNavigate<RebuilderViewModel>();
        return false;
    }

    /// <summary>The assets folder of the client in use when it is a real folder; null for an imported version.</summary>
    protected string? RealAssetsFolder() => Services.Data.ImportedVersion == null ? Services.Data.AssetsDirectory : null;

    /// <summary>What would be lost if the program closed now (an editor's unsaved changes), or null.</summary>
    public virtual string? UnsavedChanges => null;

    /// <summary>Cancels the page's running operation, if it can be cancelled.</summary>
    public void CancelOperation()
    {
        if (CurrentOperation is { CanCancel: false }) return;
        _cts?.Cancel();
    }

    /// <summary>Checks the page's dependencies and updates <see cref="DependencyNotice"/>.</summary>
    public ToolAvailability RefreshAvailability()
    {
        var availability = Services.Tools.Availability(Manifest);
        var optional = availability.Degraded;
        DependencyNotice = !availability.CanRun ? availability.Problem
            : optional.Count > 0 ? L.T("Page.SomeFeaturesAreUnavailable") + string.Join(" ", optional.Select(d => d.Message)) : null;
        DependencyLocation = DependencyNotice != null && Manifest.Dependencies.Any(d => d.Kind == DependencyKind.ClientData) && !Services.Data.AssetsAvailable
            ? Core.Locations.LocationIds.Assets : null;
        return availability;
    }

    /// <summary>
    /// Runs a long operation with progress in the status bar, cancellation (when the tool declares
    /// it), logging and a user-facing error message. Required dependencies of the operation are
    /// checked first; a missing one is explained and nothing starts. Operations wait for a free
    /// slot when the limit of concurrent operations (Settings → Performance) is reached, appear on
    /// the Diagnostics page while queued and running, and announce their end (Settings →
    /// Notifications). Returns false when the operation failed, was cancelled or could not start.
    /// </summary>
    /// <param name="successMessage">Status text after success (ignored when <paramref name="onSuccess"/> is given).</param>
    /// <param name="gate">An additional limit, for example the one for scripts.</param>
    /// <param name="operation">The manifest operation (for its dependencies and capabilities).</param>
    /// <param name="onSuccess">Sets the final status itself, after the work completed (for messages that name what was actually written).</param>
    /// <param name="output">Where the result goes, for the operation history.</param>
    protected async Task<bool> RunAsync(string title, Func<IProgress<OperationProgress>, CancellationToken, Task> work, string? successMessage = null,
        OperationGate? gate = null, string? operation = null, Action? onSuccess = null, string? output = null)
    {
        if (IsBusy) return false;
        var availability = Services.Tools.Availability(Manifest, operation);
        if (!availability.CanRun)
        {
            AppLog.Warn($"{title} did not start: {availability.Problem}", source: Manifest.Name);
            Services.Status.Show(L.F("Page.0CannotStart1", title, availability.Problem), StatusKind.Error);
            Services.Dialogs.Show(L.F("Page.0CannotStart12", title, availability.Problem), MessageKind.Warning, title);
            DependencyNotice = availability.Problem;
            DependencyLocation = Manifest.Dependencies.Any(d => d.Kind == DependencyKind.ClientData) && !Services.Data.AssetsAvailable ? Core.Locations.LocationIds.Assets : null;
            return false;
        }
        bool canCancel = Manifest.CapabilitiesOf(operation).HasFlag(ToolCapabilities.Cancellation);
        IsBusy = true;
        _cts = new CancellationTokenSource();
        var job = Services.Jobs.Create(Manifest.Id, Manifest.Name, title, canCancel, _cts, output);
        CurrentOperation = job;
        using var logScope = AppLog.BeginScope(Manifest.Name, job.Id);
        using var scope = Services.Status.BeginOperation(title + "…", canCancel);
        scope.Reported += p => Services.Jobs.Report(job, p);
        var started = DateTime.UtcNow;
        bool success = false;
        IDisposable? slot = null, extra = null;
        try
        {
            if (gate != null)
            {
                if (gate.IsFull) Services.Status.Show(L.F("Page.0WaitingUntilAnotherScriptFinishes", title, gate.Limit));
                extra = await gate.EnterAsync(_cts.Token);
            }
            if (Services.Operations.IsFull) Services.Status.Show(L.F("Page.0WaitingUntilAnotherOperationFinishes", title, Services.Operations.Limit));
            slot = await Services.Operations.EnterAsync(_cts.Token);
            started = DateTime.UtcNow;
            Services.Jobs.Start(job);
            await work(scope.Reporter, _cts.Token);
            if (onSuccess != null) onSuccess();
            else Services.Status.Show(successMessage ?? title + L.T("Page.Finished"), StatusKind.Success);
            success = true;
            var kind = Services.Status.Kind;
            if (kind is StatusKind.Warning or StatusKind.Error) Services.Jobs.Warn(job, Services.Status.FullText);
            Services.Jobs.Finish(job, kind == StatusKind.Error ? OperationState.Failed : OperationState.Completed, Services.Status.FullText);
            return true;
        }
        catch (OperationCanceledException)
        {
            Services.Status.Show(title + L.T("Page.Cancelled"), StatusKind.Warning);
            Services.Jobs.Finish(job, OperationState.Cancelled, L.T("Page.CancelledByTheUser"));
            return false;
        }
        catch (DataFileException ex)
        {
            // A client data file is missing, locked, damaged or in a changed format: explain it in
            // plain words with advice instead of an exception dump.
            AppLog.Warn($"{title}: {ex.Kind} {ex.FilePath}: {ex.Message}", ex.InnerException);
            Services.Status.Show(L.F("Page.0Failed12", title, ex.FileName, ex.Kind switch { DataProblemKind.Missing => L.T("Page.IsMissing"), DataProblemKind.Unreadable => L.T("Page.CouldNotBeRead"), _ => L.T("Page.IsDamagedOrInAFormat") }), StatusKind.Error);
            Services.Jobs.Error(job, ex.Message);
            Services.Jobs.Finish(job, OperationState.Failed, Services.Status.FullText);
            if (Services.Settings.Notifications.ShowErrorDialogs) Services.Dialogs.Show($"{ex.Message}\n\n{ex.Advice}", MessageKind.Error, title);
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Error(title + " failed.", ex);
            Services.Status.Show(title + L.T("Page.Failed") + ex.Message + (Services.Settings.Notifications.ShowErrorDialogs ? "" : L.T("Page.DetailsInTheLog")), StatusKind.Error);
            Services.Jobs.Error(job, ex.Message);
            Services.Jobs.Finish(job, OperationState.Failed, ex.Message);
            if (Services.Settings.Notifications.ShowErrorDialogs) Services.Dialogs.ShowError(L.F("Page.0Failed", title), ex, log: false);
            return false;
        }
        finally
        {
            if (!job.IsFinished) Services.Jobs.Finish(job, OperationState.Cancelled, L.T("Page.EndedBeforeItStarted"));
            slot?.Dispose();
            extra?.Dispose();
            if (slot != null) Services.Notifications.OperationFinished(title, DateTime.UtcNow - started, success);
            _cts.Dispose();
            _cts = null;
            CurrentOperation = null;
            IsBusy = false;
        }
    }

    /// <summary>Runs a quick action with consistent error reporting (no progress).</summary>
    protected bool Try(string what, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(what + " failed.", ex, source: Manifest.Name);
            Services.Dialogs.ShowError(L.F("Page.0Failed", what), ex, log: false);
            return false;
        }
    }

    protected void Info(string message) => Services.Status.Show(message, StatusKind.Success);

    /// <summary>"✓ Saved to &lt;file&gt;" with the file clickable in the status bar (only call after the file was written).</summary>
    protected void Saved(string file, string after = "") => Services.Status.ShowSaved(file, isFile: true, after: after);

    /// <summary>
    /// Attribution for a file made from imported image files (combined, sliced, cropped, stitched,
    /// converted). Inputs that name another creator keep that creator unless the user explicitly
    /// decides otherwise in a dialog; inputs without attribution make it the user's new asset.
    /// Returns null when the user cancels.
    /// </summary>
    /// <param name="copyrightIfUnknown">Rights notice when no input carries one (Tibia graphics → CipSoft notice).</param>
    protected AttributionInfo? AttributionForImports(IReadOnlyList<FoundAttribution?> sources, string operation, string? copyrightIfUnknown)
    {
        var review = Attribution.Review(sources);
        var choice = AttributionChoice.KeepOriginalCreators;
        if (review.HasConflict)
        {
            if (Services.Dialogs.ChooseAttribution(review, operation) is not { } picked) return null;
            choice = picked;
            AppLog.Info($"{operation}: inputs attributed to {string.Join(", ", review.OtherCreators)}; user chose {choice}.");
        }
        return Attribution.ForDerivedAsset(review.Sources, choice, copyrightIfUnknown: copyrightIfUnknown);
    }

    /// <summary>
    /// Reports a batch result honestly: success only when everything worked, a warning (with the
    /// failures listed) when some items failed, an error when nothing was written. The destination
    /// folder is shown as a clickable path.
    /// </summary>
    protected void ReportBatch(BatchResult result, string noun, string? destination, string failureTitle)
    {
        string summary = result.Summary(noun);
        string notes = result.Notes.Count > 0 ? " " + string.Join(" ", result.Notes) : "";
        void ShowSummary(StatusKind kind)
        {
            if (destination != null && result.Succeeded > 0)
                Services.Status.ShowSaved(destination, isFile: false, before: summary.TrimEnd('.') + " → ", after: notes, kind: kind);
            else
                Services.Status.Show(summary + (destination != null ? " → " + destination : "") + notes, kind);
        }
        if (!result.HasFailures)
        {
            ShowSummary(result.Notes.Count > 0 ? StatusKind.Warning : StatusKind.Success);
            return;
        }
        AppLog.Warn($"{failureTitle}: {L.Count(result.Failures.Count, "Noun.failure")}. {string.Join(" | ", result.Failures.Take(50))}");
        bool nothing = result.Succeeded == 0;
        if (nothing) Services.Status.Show(L.F("Page.0NothingWasWritten1N0", failureTitle, result.Failures.Count), StatusKind.Error);
        else ShowSummary(StatusKind.Warning);
        string list = string.Join("\n", result.Failures.Take(25));
        if (result.Failures.Count > 25) list += L.F("Page.And0N0MoreListedIn", result.Failures.Count - 25);
        Services.Dialogs.Show($"{summary}{(destination != null ? " → " + destination : "")}{notes}\n\n{list}", nothing ? MessageKind.Error : MessageKind.Warning, failureTitle);
    }
}
