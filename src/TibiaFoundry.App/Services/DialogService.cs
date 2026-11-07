using TibiaFoundry.Core.Metadata;
using System.Windows;
using Microsoft.Win32;
using TibiaFoundry.App.Views.Dialogs;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

public enum MessageKind { Info, Warning, Error, Question }

/// <summary>One entry of a list to choose from (8.0).</summary>
/// <param name="Group">A heading shown above the entry ("Live installation", "Archived revisions"…), or null.</param>
/// <param name="Enabled">False: shown, but cannot be chosen (for example a folder that is missing).</param>
public sealed record ChoiceItem(string Title, string? Detail = null, string? Group = null, bool Enabled = true);

/// <summary>Options of a list choice: button texts, an optional check box ("Remember my choice") and extra buttons ("Browse for folder…").</summary>
public sealed record ChoiceOptions
{
    public string OkText { get; init; } = L.T("Dialog.OK");
    public string CancelText { get; init; } = L.T("Common.Cancel");
    public int? Selected { get; init; }
    public string? CheckBoxText { get; init; }
    public bool CheckBoxDefault { get; init; }
    /// <summary>Extra buttons: an ID returned in <see cref="ChoiceResult.Button"/>, and the text.</summary>
    public IReadOnlyList<(string Id, string Text)> ExtraButtons { get; init; } = [];
    /// <summary>A summary card above the question (8.8): labeled facts, kept apart from the question and the choices.</summary>
    public IReadOnlyList<ReportRow> Facts { get; init; } = [];
    public string? FactsHeading { get; init; }
    /// <summary>Badges in the summary card (for example the tools that can use a client), with their label.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? TagsLabel { get; init; }
}

/// <summary>The answer of a list choice: the selected item (-1 when none), the check box, and the extra button pressed (null for OK).</summary>
public sealed record ChoiceResult(int Index, bool Checked, string? Button);

/// <summary>
/// The archive confirmation (8.5): the client, the figures of <see cref="Preview"/>, and personal
/// data. Changing the personal-data option reads the folder again through <see cref="Recalculate"/>,
/// so the figures shown are always those of the scan that is archived.
/// </summary>
public sealed class ArchiveConfirmation
{
    /// <summary>The client's name ("Live installation", a folder's label).</summary>
    public required string Client { get; init; }
    /// <summary>"Live server" or "Test server".</summary>
    public required string Server { get; init; }
    /// <summary>The version as shown ("15.33.c9e45e", "15.33.c9e45e? (probably)").</summary>
    public required string Version { get; init; }
    public required string Folder { get; init; }
    public required Core.Clients.ArchivePreview Preview { get; init; }
    /// <summary>The scan has problems and is kept as an incomplete revision.</summary>
    public bool KeepIncomplete { get; init; }
    /// <summary>What changed against the newest revision of this folder ("Unchanged against …"), or null.</summary>
    public Func<Core.Clients.ArchivePreview, string?>? DescribeChanges { get; init; }
    /// <summary>Reads the folder again with (true) or without personal data; runs off the UI thread. Null: the option cannot be changed here.</summary>
    public Func<bool, IProgress<Core.Common.OperationProgress>, CancellationToken, Task<Core.Clients.ArchivePreview>>? Recalculate { get; init; }
}

/// <summary>File/folder pickers and themed message boxes.</summary>
public interface IDialogService
{
    string? OpenFile(string title, string filter, string? initialDirectory = null);
    string[]? OpenFiles(string title, string filter, string? initialDirectory = null);
    string? SaveFile(string title, string filter, string defaultFileName, string? initialDirectory = null);
    string? PickFolder(string title, string? initialDirectory = null);
    void Show(string message, MessageKind kind = MessageKind.Info, string? title = null);
    void ShowError(string message, Exception? exception = null, bool log = true);
    /// <param name="confirmText">The confirm button (default: OK).</param>
    /// <param name="cancelText">The cancel button (default: Cancel).</param>
    bool Confirm(string message, string title, string? confirmText = null, string? cancelText = null);

    /// <summary>A question with two answers and Cancel: returns 0 (first), 1 (second) or -1 (cancelled).</summary>
    int Choose(string message, string title, string first, string second, string? cancelText = null);
    string? Prompt(string title, string message, string initialText = "", bool multiline = false);

    /// <summary>Choose one item of a list (8.0). Null when cancelled.</summary>
    ChoiceResult? ChooseFrom(string title, string message, IReadOnlyList<ChoiceItem> items, ChoiceOptions? options = null);

    /// <summary>
    /// A structured message (8.6): heading, summary and sections of labeled values, tables and lists,
    /// with up to three buttons. Use it instead of <see cref="Show"/> or <see cref="Confirm"/> whenever a
    /// message reports several facts (a client, a path, counts) rather than one sentence.
    /// </summary>
    ReportAnswer ShowReport(DialogReport report);

    /// <summary>Asks to archive a client folder (8.5). Returns the preview to archive — the one shown last, with or without personal data — or null when cancelled.</summary>
    Core.Clients.ArchivePreview? ConfirmArchive(ArchiveConfirmation request);

    /// <summary>
    /// Asks how to attribute a file made from inputs that name another creator. Returns null when
    /// the user cancels. Keeping the original creators is the default and the safe choice.
    /// </summary>
    AttributionChoice? ChooseAttribution(AttributionReview review, string operation);

    /// <summary>
    /// Asks what to do with an output file that already exists. May be called from a worker thread;
    /// the dialog is shown on the UI thread.
    /// </summary>
    Core.FileOps.ConflictAnswer AskConflict(string path, string operation, bool backupsOn);
}

public sealed class DialogService : IDialogService
{
    /// <summary>
    /// When set (automated UI snapshot runs), no window is opened: messages are logged and pickers
    /// return "cancelled", so an unattended run can never block on a modal dialog.
    /// </summary>
    public static bool Unattended { get; set; }

    private static bool Suppressed(string kind, string text)
    {
        if (!Unattended) return false;
        AppLog.Info($"[unattended] {kind} suppressed: {text.ReplaceLineEndings(" ")}");
        return true;
    }

    private static Window? Owner => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    private static string? ExistingDirectory(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try
        {
            while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) dir = Path.GetDirectoryName(dir);
        }
        catch (ArgumentException) { return null; }
        return string.IsNullOrEmpty(dir) ? null : dir;
    }

    public string? OpenFile(string title, string filter, string? initialDirectory = null)
    {
        if (Suppressed("open-file dialog", title)) return null;
        var dlg = new OpenFileDialog { Title = title, Filter = filter, InitialDirectory = ExistingDirectory(initialDirectory) ?? "", CheckFileExists = true };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public string[]? OpenFiles(string title, string filter, string? initialDirectory = null)
    {
        if (Suppressed("open-files dialog", title)) return null;
        var dlg = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true, InitialDirectory = ExistingDirectory(initialDirectory) ?? "" };
        return dlg.ShowDialog(Owner) == true ? dlg.FileNames : null;
    }

    public string? SaveFile(string title, string filter, string defaultFileName, string? initialDirectory = null)
    {
        if (Suppressed("save-file dialog", title)) return null;
        var dlg = new SaveFileDialog
        {
            Title = title, Filter = filter, FileName = defaultFileName, AddExtension = true, OverwritePrompt = true,
            InitialDirectory = ExistingDirectory(initialDirectory) ?? "",
        };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public string? PickFolder(string title, string? initialDirectory = null)
    {
        if (Suppressed("folder dialog", title)) return null;
        var dlg = new OpenFolderDialog { Title = title, InitialDirectory = ExistingDirectory(initialDirectory) ?? "" };
        return dlg.ShowDialog(Owner) == true ? dlg.FolderName : null;
    }

    public void Show(string message, MessageKind kind = MessageKind.Info, string? title = null)
    {
        if (Suppressed($"{kind} message", (title ?? "") + ": " + message)) return;
        MessageDialog.Show(Owner, title ?? Core.Common.AppIdentity.Name, message, kind, L.T("Dialog.OK"), null);
    }

    public void ShowError(string message, Exception? exception = null, bool log = true)
    {
        if (exception != null && log) AppLog.Error(message, exception);
        if (Suppressed("error message", message + (exception != null ? " " + exception.Message : ""))) return;
        // The reason in plain words; the exception type and stack trace go to the log only.
        string details = exception == null || string.IsNullOrWhiteSpace(exception.Message) || message.Contains(exception.Message, StringComparison.Ordinal)
            ? message : $"{message}\n\n{exception.Message}";
        if (AppLog.LogFilePath != null && exception != null) details += L.F("Dialog.TechnicalDetailsWereSavedInThe", AppLog.LogFilePath);
        MessageDialog.Show(Owner, L.T("Dialog.Error"), details, MessageKind.Error, L.T("Dialog.OK"), null);
    }

    public bool Confirm(string message, string title, string? confirmText = null, string? cancelText = null) =>
        !Suppressed("confirmation", title + ": " + message) && MessageDialog.Show(Owner, title, message, MessageKind.Question, confirmText ?? L.T("Dialog.OK"), cancelText ?? L.T("Common.Cancel"));

    public int Choose(string message, string title, string first, string second, string? cancelText = null) =>
        Suppressed("question", title + ": " + message) ? -1 : MessageDialog.ShowChoice(Owner, title, message, MessageKind.Question, first, second, cancelText ?? L.T("Common.Cancel"));

    public string? Prompt(string title, string message, string initialText = "", bool multiline = false) =>
        Suppressed("input prompt", title) ? null : InputDialog.Show(Owner, title, message, initialText, multiline);

    public ChoiceResult? ChooseFrom(string title, string message, IReadOnlyList<ChoiceItem> items, ChoiceOptions? options = null) =>
        Suppressed("list choice", title + ": " + message) ? null : ChoiceDialog.Show(Owner, title, message, items, options ?? new ChoiceOptions());

    public ReportAnswer ShowReport(DialogReport report) =>
        Suppressed("report", report.Title + ": " + report.ToPlainText()) ? ReportAnswer.Cancel : ReportDialog.Show(Owner, report);

    public Core.Clients.ArchivePreview? ConfirmArchive(ArchiveConfirmation request) =>
        Suppressed("archive confirmation", $"{request.Client} ({request.Version})") ? null : ArchiveDialog.Show(Owner, request);

    public Core.FileOps.ConflictAnswer AskConflict(string path, string operation, bool backupsOn)
    {
        // Unattended runs never replace files without a person deciding.
        if (Suppressed("existing-file question", operation + ": " + path)) return new(Core.FileOps.ConflictChoice.Skip, true);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
            return dispatcher.Invoke(() => ConflictDialog.Show(Owner, path, operation, backupsOn));
        return ConflictDialog.Show(Owner, path, operation, backupsOn);
    }

    public AttributionChoice? ChooseAttribution(AttributionReview review, string operation)
    {
        // Unattended runs keep the original creators: never re-attribute without a person deciding.
        if (Suppressed("attribution choice", operation + ": " + review.Describe())) return AttributionChoice.KeepOriginalCreators;
        string message = review.Describe() + L.T("Dialog.KeepOriginalCreatorsRecommendedTheNew");
        return MessageDialog.ShowChoice(Owner, operation + L.T("Dialog.Attribution"), message, MessageKind.Question, L.T("Dialog.KeepOriginalCreators"), L.T("Dialog.AttributeToMe"), L.T("Common.Cancel")) switch
        {
            0 => AttributionChoice.KeepOriginalCreators,
            1 => AttributionChoice.AttributeToMe,
            _ => null,
        };
    }
}
