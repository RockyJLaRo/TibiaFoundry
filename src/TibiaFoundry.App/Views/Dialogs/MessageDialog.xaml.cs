using System.Windows;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.Themes;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Views.Dialogs;

/// <summary>Themed replacement for MessageBox (Win32 message boxes cannot follow the dark theme).</summary>
public partial class MessageDialog : Window
{
    private MessageDialog()
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        DialogSizing.FitToWorkArea(this);
    }

    private bool _alternative;

    /// <returns>True when the primary button was pressed.</returns>
    public static bool Show(Window? owner, string title, string message, MessageKind kind, string okText, string? cancelText) =>
        Create(owner, title, message, kind, okText, cancelText).ShowDialog() == true;

    /// <summary>Three choices: returns 0 for the primary button, 1 for the alternative, -1 for cancel or closing.</summary>
    public static int ShowChoice(Window? owner, string title, string message, MessageKind kind, string okText, string alternativeText, string cancelText)
    {
        var dlg = Create(owner, title, message, kind, okText, cancelText);
        dlg.AlternativeButton.Content = alternativeText;
        dlg.AlternativeButton.Visibility = Visibility.Visible;
        bool ok = dlg.ShowDialog() == true;
        return ok ? (dlg._alternative ? 1 : 0) : -1;
    }

    private static MessageDialog Create(Window? owner, string title, string message, MessageKind kind, string okText, string? cancelText)
    {
        var dlg = new MessageDialog { Title = title };
        if (owner != null && owner.IsLoaded) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.MessageText.Text = message;
        // Long messages (lists, file summaries) get a wider box rather than a tall narrow column.
        if (message.Length > 480 || message.Split('\n').Any(line => line.Length > 110))
        {
            dlg.Width = 580;
            DialogSizing.FitToWorkArea(dlg);
        }
        dlg.OkButton.Content = okText;
        if (cancelText == null)
        {
            dlg.CancelButton.Visibility = Visibility.Collapsed;
            dlg.OkButton.IsCancel = true;
        }
        else dlg.CancelButton.Content = cancelText;
        (string glyph, string brush, string soft) = kind switch
        {
            MessageKind.Error => (char.ConvertFromUtf32(0xEA39), "App.Danger", "App.DangerSoft"),
            MessageKind.Warning => (char.ConvertFromUtf32(0xE7BA), "App.Warning", "App.WarningSoft"),
            MessageKind.Question => (char.ConvertFromUtf32(0xE9CE), "App.Accent", "App.AccentSoft"),
            _ => (char.ConvertFromUtf32(0xE946), "App.Accent", "App.AccentSoft"),
        };
        dlg.IconGlyph.Text = glyph;
        dlg.IconGlyph.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, brush);
        dlg.IconBadge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, soft);
        if (kind is MessageKind.Error or MessageKind.Warning) System.Media.SystemSounds.Exclamation.Play();
        return dlg;
    }

    /// <summary>Copies the title and the message (8.6); Ctrl+C does the same when no text is selected.</summary>
    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Title + Environment.NewLine + Environment.NewLine + MessageText.Text);
            CopyText.Text = L.T("MessageDialog.Copied");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            CopyText.Text = L.T("MessageDialog.ClipboardBusy");
        }
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.C && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control && MessageText.SelectionLength == 0)
        {
            OnCopy(this, e);
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnAlternative(object sender, RoutedEventArgs e)
    {
        _alternative = true;
        DialogResult = true;
    }
}
