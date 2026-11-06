using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

/// <summary>
/// Where each kind of output goes, shared by every tool: the folder set in Settings, the folder
/// last used (when remembering is on) or the kind's default subfolder of
/// <c>Documents\Tibia_Foundry</c>. Folders are created when something is written into them; a
/// folder that cannot be used is explained to the user, who can choose another — the program never
/// writes to a different place on its own.
/// </summary>
public sealed class OutputService(AppServices services)
{
    private Core.Settings.AppSettings Settings => services.Settings;

    /// <summary>The folder a kind of output is written to now.</summary>
    public string FolderFor(OutputKind kind) => kind switch
    {
        OutputKind.Backups => services.BackupFolder,
        OutputKind.Logs => services.LogFolder,
        OutputKind.Temporary => services.TempFolder,
        _ => Settings.OutputFolderFor(kind),
    };

    /// <summary>The default folder of a kind (what "Use default" restores).</summary>
    public string DefaultFolderFor(OutputKind kind) => kind switch
    {
        OutputKind.Backups => services.DefaultBackupFolder,
        OutputKind.Logs => services.DefaultLogFolder,
        OutputKind.Temporary => services.DefaultTempFolder,
        _ => Settings.DefaultFolderFor(kind),
    };

    /// <summary>True when the user chose a folder for this kind in Settings.</summary>
    public bool IsExplicit(OutputKind kind) => Settings.ExplicitFolder(kind) != null;

    /// <summary>Creates the folder of a kind when needed and checks it can be written to.</summary>
    public FolderState Prepare(OutputKind kind) => OutputLocations.Prepare(FolderFor(kind));

    /// <summary>
    /// The folder to write a kind of output into, ready to use. When it cannot be used, the user
    /// is told why and may choose another folder for this operation (null = cancelled).
    /// </summary>
    public string? PrepareOrAsk(OutputKind kind, string operation)
    {
        var state = Prepare(kind);
        if (state.Usable) return state.Path;
        AppLog.Warn($"{operation}: the {OutputLocations.Info(kind).DisplayName} folder cannot be used: {state.Problem}", source: "Outputs");
        if (!services.Dialogs.Confirm(L.F("Output.0ChooseAnotherFolderForThis", state.Problem, operation.ToLowerInvariant()),
                operation, L.T("Output.ChooseAFolder"), L.T("Common.Cancel"))) return null;
        string? chosen = services.Dialogs.PickFolder(L.F("Output.0Folder", operation), null);
        if (chosen == null) return null;
        var again = OutputLocations.Prepare(chosen);
        if (!again.Usable)
        {
            services.Dialogs.Show(again.Problem!, MessageKind.Warning, operation);
            return null;
        }
        Settings.RememberOutputFolder(kind, again.Path);
        return again.Path;
    }

    /// <summary>
    /// "Save as" dialog that starts in the kind's folder (created when it does not exist yet). The
    /// folder of the chosen file is remembered for next time (Settings → General). If the default
    /// folder cannot be created, the status bar says why and the dialog starts in Windows' default.
    /// </summary>
    public string? SaveFile(OutputKind kind, string title, string filter, string fileName)
    {
        var state = Prepare(kind);
        if (!state.Usable)
        {
            services.Status.Show(L.F("Output.The0FolderCannotBeUsed", OutputLocations.Info(kind).DisplayName, state.Problem), StatusKind.Warning);
            AppLog.Warn($"{title}: default folder unusable: {state.Problem}", source: "Outputs");
        }
        string? file = services.Dialogs.SaveFile(title, filter, fileName, state.Usable ? state.Path : null);
        if (file != null) Settings.RememberOutputFolder(kind, Path.GetDirectoryName(file));
        return file;
    }

    /// <summary>Folder picker for an output of this kind (starts in its folder); the choice is remembered.</summary>
    public string? PickFolder(OutputKind kind, string title, string? current = null)
    {
        string start = current ?? FolderFor(kind);
        if (!Directory.Exists(start) && Prepare(kind) is { Usable: true } state) start = state.Path;
        string? folder = services.Dialogs.PickFolder(title, start);
        if (folder != null) Settings.RememberOutputFolder(kind, folder);
        return folder;
    }
}
