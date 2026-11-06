using System.Windows;
using TibiaFoundry.App.Themes;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Views.Dialogs;

/// <summary>Asks what to do with an output file that already exists (Settings → File safety: "Ask").</summary>
public partial class ConflictDialog : Window
{
    private ConflictChoice _choice = ConflictChoice.Cancel;

    private ConflictDialog()
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        DialogSizing.FitToWorkArea(this);
    }

    public static ConflictAnswer Show(Window? owner, string path, string operation, bool backupsOn)
    {
        var dlg = new ConflictDialog();
        if (owner != null && owner.IsLoaded) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.Heading.Text = L.F("ConflictDialog.01AlreadyExists", operation, Path.GetFileName(path));
        dlg.PathText.Text = path;
        dlg.BackupNote.Text = backupsOn
            ? L.T("ConflictDialog.ReplaceTheExistingFileIsCopied")
            : L.T("ConflictDialog.ReplaceBackupsAreTurnedOffIn");
        dlg.ReplaceButton.ToolTip = dlg.BackupNote.Text;
        dlg.ShowDialog();
        return new ConflictAnswer(dlg._choice, dlg._choice != ConflictChoice.Cancel && dlg.ApplyToAll.IsChecked == true);
    }

    private void Close(ConflictChoice choice)
    {
        _choice = choice;
        DialogResult = choice != ConflictChoice.Cancel;
    }

    private void OnReplace(object sender, RoutedEventArgs e) => Close(ConflictChoice.Overwrite);
    private void OnKeepBoth(object sender, RoutedEventArgs e) => Close(ConflictChoice.KeepBoth);
    private void OnSkip(object sender, RoutedEventArgs e) => Close(ConflictChoice.Skip);
    private void OnCancel(object sender, RoutedEventArgs e) => Close(ConflictChoice.Cancel);
}
