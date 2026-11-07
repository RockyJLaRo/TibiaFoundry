using System.Windows;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.Themes;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Views.Dialogs;

/// <summary>
/// The archive confirmation (8.5): the client and its figures at a glance, personal data in its
/// own section, and every category in File details. Changing the personal-data option reads the
/// folder again (Archive waits until it has finished), so what is archived is always what is shown.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The token source only lives while the folder is read again and is disposed then.")]
public partial class ArchiveDialog : Window
{
    /// <summary>One line of File details, already formatted.</summary>
    public sealed record DetailRow(string Name, string State, string Files, string Size, string New);

    private readonly ArchiveConfirmation _request;
    private ArchivePreview _preview;
    private CancellationTokenSource? _recalculation;
    private bool _archive;

    private ArchiveDialog(ArchiveConfirmation request)
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        DialogSizing.FitToWorkArea(this);
        _request = request;
        _preview = request.Preview;
        Closing += (_, _) => _recalculation?.Cancel();
        Loaded += (_, _) => ArchiveButton.Focus();
        Render();
    }

    /// <returns>The preview to archive, or null when cancelled.</returns>
    public static ArchivePreview? Show(Window? owner, ArchiveConfirmation request)
    {
        var dlg = new ArchiveDialog(request);
        if (owner != null && owner.IsLoaded) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dlg.ShowDialog() == true && dlg._archive ? dlg._preview : null;
    }

    /// <summary>The dialog without showing it modally (the UI snapshot renders it).</summary>
    internal static ArchiveDialog CreateForSnapshot(ArchiveConfirmation request) => new(request) { ShowActivated = false };

    /// <summary>Opens or closes File details (the UI snapshot renders both).</summary>
    internal void SetDetailsExpanded(bool expanded) => DetailsExpander.IsExpanded = expanded;

    private void Render()
    {
        var p = _preview;
        bool complete = p.Scope == ManifestScopes.CompleteInstallation;
        Heading.Text = L.F("ArchiveDialog.Archive0", _request.Client);
        Subtitle.Text = $"{_request.Server} · {_request.Version}";
        FolderText.Text = _request.Folder;
        FolderText.ToolTip = _request.Folder;

        IncompleteBanner.Visibility = _request.KeepIncomplete ? Visibility.Visible : Visibility.Collapsed;
        IncompleteText.Text = L.Count(p.Scan.Problems.Count, "ArchiveDialog.KeptAsIncomplete");

        FilesValue.Text = p.Files.ToString("N0");
        SizeValue.Text = ReportWriter.FormatSize(p.LogicalBytes);
        NewValue.Text = ReportWriter.FormatSize(p.NewBytes);

        ScopeValue.Text = complete ? ManifestScopes.Display(p.Scope) : ManifestScopes.Display(p.Scope) + L.T("ArchiveDialog.TheFolderAsItIsNot");
        StorageValue.Text = ManifestStorage.Display(p.Storage) + L.T("ArchiveDialog.SetInSettingsTibiaClient");
        bool shared = p.Storage != ManifestStorage.Independent;
        ReusedLabel.Visibility = ReusedValue.Visibility = shared ? Visibility.Visible : Visibility.Collapsed;
        ReusedValue.Text = p.ReusedBytes > 0
            ? L.F("ArchiveDialog.0AlreadyArchivedOrDuplicatedIn", ReportWriter.FormatSize(p.ReusedBytes))
            : L.T("ArchiveDialog.NoneEveryFileIsNewTo");
        string? changes = _request.DescribeChanges?.Invoke(p);
        ChangesLabel.Visibility = ChangesValue.Visibility = changes == null ? Visibility.Collapsed : Visibility.Visible;
        ChangesValue.Text = changes ?? "";

        RenderPersonal();

        DetailRows.ItemsSource = p.Categories.Select(c => new DetailRow(c.Name,
            c.State switch { ArchiveCategoryState.Included => L.T("Common.Archived"), ArchiveCategoryState.Excluded => L.T("ArchiveDialog.LeftOut"), _ => L.T("ArchiveDialog.NotArchived") },
            c.Files.ToString("N0"),
            c.State == ArchiveCategoryState.NotArchived ? "—" : ReportWriter.FormatSize(c.Bytes),
            c.State == ArchiveCategoryState.Included ? ReportWriter.FormatSize(c.NewBytes) : "—")).ToList();
        ProblemList.Visibility = p.Scan.Problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProblemList.Text = string.Join(Environment.NewLine, p.Scan.Problems.Take(8)) + (p.Scan.Problems.Count > 8 ? L.F("ArchiveDialog.0And1N0More", Environment.NewLine, p.Scan.Problems.Count - 8) : "");
    }

    private void RenderPersonal()
    {
        var p = _preview;
        bool any = p.HasExcludedPersonalData || p.IncludesPersonalData;
        PersonalSection.Visibility = Visibility.Visible;
        IncludePersonal.IsChecked = p.IncludesPersonalData;
        IncludePersonal.Visibility = any && _request.Recalculate != null ? Visibility.Visible : Visibility.Collapsed;
        PersonalHelp.Visibility = IncludePersonal.Visibility;
        PersonalHelp.Text = L.T("ArchivePreview.PersonalDataDescription") + L.T("ArchiveDialog.TheArchiveStaysOnThisPC");
        int personalIncluded = p.Categories.Where(c => c.State == ArchiveCategoryState.Included && c.IsPersonal).Sum(c => c.Files);
        PersonalState.Text = !any ? L.T("ArchiveDialog.ThisFolderHasNoPersonalData")
            : p.IncludesPersonalData ? L.F("ArchiveDialog.Included0OfPersonalDataAre", L.Count(personalIncluded, "Noun.file"))
            : L.F("ArchiveDialog.LeftOutByDefault01", L.Count(p.ExcludedFiles, "Noun.file"), ReportWriter.FormatSize(p.ExcludedBytes));
    }

    private async void OnIncludePersonalClick(object sender, RoutedEventArgs e)
    {
        bool include = IncludePersonal.IsChecked == true;
        if (include == _preview.IncludesPersonalData || _request.Recalculate == null) return;
        SetRecalculating(true);
        _recalculation = new CancellationTokenSource();
        var progress = new Progress<OperationProgress>(r =>
        {
            // The scan reports by bytes (per mille); its message says files and sizes in words.
            RecalculatingText.Text = string.IsNullOrWhiteSpace(r.Message) ? L.T("ArchiveDialog.ReadingTheFolderAgain") : L.F("ArchiveDialog.ReadingTheFolderAgain0", r.Message.TrimStart());
            RecalculatingProgress.IsIndeterminate = r.Total <= 0;
            RecalculatingProgress.Value = r.Fraction * 100;
        });
        try
        {
            var preview = await _request.Recalculate(include, progress, _recalculation.Token);
            if (!IsVisible) return;
            _preview = preview;
            Render();
        }
        catch (OperationCanceledException)
        {
            if (IsVisible) RenderPersonal();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            AppLog.Warn("Archive: the folder could not be read again: " + ex.Message);
            if (!IsVisible) return;
            RenderPersonal();
            PersonalState.Text = L.T("ArchiveDialog.TheFolderCouldNotBeRead") + ex.Message;
        }
        finally
        {
            _recalculation.Dispose();
            _recalculation = null;
            if (IsVisible) SetRecalculating(false);
        }
    }

    private void SetRecalculating(bool busy)
    {
        Recalculating.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        RecalculatingText.Text = L.T("ArchiveDialog.ReadingTheFolderAgain");
        RecalculatingProgress.IsIndeterminate = true;
        IncludePersonal.IsEnabled = !busy;
        ArchiveButton.IsEnabled = !busy;
    }

    private void OnArchive(object sender, RoutedEventArgs e)
    {
        if (_recalculation != null) return;   // the figures are being read again: Archive is disabled until then
        _archive = true;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
