using System.Windows;
using TibiaFoundry.App.Themes;

namespace TibiaFoundry.App.Views.Dialogs;

/// <summary>Single- or multi-line text prompt (replaces the original "Extract by ID" and "Sprite ID List" forms).</summary>
public partial class InputDialog : Window
{
    private InputDialog()
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        DialogSizing.FitToWorkArea(this);
    }

    public static string? Show(Window? owner, string title, string message, string initialText, bool multiline)
    {
        var dlg = new InputDialog { Title = title };
        if (owner != null && owner.IsLoaded) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.Message.Text = message;
        dlg.Input.Text = initialText;
        if (multiline)
        {
            dlg.Input.AcceptsReturn = true;
            dlg.Input.TextWrapping = TextWrapping.Wrap;
            dlg.Input.Height = 220;
            dlg.Input.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
            dlg.Width = 560;
        }
        dlg.Loaded += (_, _) =>
        {
            dlg.Input.Focus();
            dlg.Input.SelectAll();
        };
        return dlg.ShowDialog() == true ? dlg.Input.Text : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
