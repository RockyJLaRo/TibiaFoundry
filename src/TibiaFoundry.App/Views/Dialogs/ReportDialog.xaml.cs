using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.Themes;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Views.Dialogs;

/// <summary>
/// Shows a <see cref="DialogReport"/> (8.6): heading and summary, then each section with its
/// labeled values, table, list and note. Paths are selectable with a copy button; every value can be
/// selected; "Copy details" copies the whole report. The dialog can be resized, and its content
/// scrolls when it does not fit the screen.
/// </summary>
public partial class ReportDialog : Window
{
    private readonly DialogReport _report;
    private ReportAnswer _answer = ReportAnswer.Cancel;

    private ReportDialog(DialogReport report)
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        DialogSizing.FitToWorkArea(this);
        _report = report;
        Render();
        Loaded += (_, _) => PrimaryButton.Focus();
    }

    public static ReportAnswer Show(Window? owner, DialogReport report)
    {
        var dlg = new ReportDialog(report);
        if (owner != null && owner.IsLoaded) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (report.Kind is MessageKind.Error or MessageKind.Warning) System.Media.SystemSounds.Exclamation.Play();
        dlg.ShowDialog();
        return dlg._answer;
    }

    /// <summary>The dialog without showing it modally (the UI snapshot renders it).</summary>
    internal static ReportDialog CreateForSnapshot(DialogReport report) => new(report) { ShowActivated = false };

    private void Render()
    {
        Title = _report.Title;
        Heading.Text = _report.Heading;
        Summary.Text = _report.Summary ?? "";
        Summary.Visibility = string.IsNullOrWhiteSpace(_report.Summary) ? Visibility.Collapsed : Visibility.Visible;
        (string glyph, string brush, string soft) = _report.Kind switch
        {
            MessageKind.Error => ("", "App.Danger", "App.DangerSoft"),
            MessageKind.Warning => ("", "App.Warning", "App.WarningSoft"),
            MessageKind.Question => ("", "App.Accent", "App.AccentSoft"),
            _ => ("", "App.Accent", "App.AccentSoft"),
        };
        IconGlyph.Text = glyph;
        IconGlyph.SetResourceReference(TextBlock.ForegroundProperty, brush);
        IconBadge.SetResourceReference(Border.BackgroundProperty, soft);

        PrimaryButton.Content = _report.PrimaryText;
        if (_report.SecondaryText != null)
        {
            SecondaryButton.Content = _report.SecondaryText;
            SecondaryButton.Visibility = Visibility.Visible;
        }
        if (_report.CancelText == null)
        {
            CancelButton.Visibility = Visibility.Collapsed;
            // Without a cancel button, Esc and closing the window still answer: as the only button.
            PrimaryButton.IsCancel = _report.SecondaryText == null;
        }
        else CancelButton.Content = _report.CancelText;

        foreach (var section in _report.Sections) Body.Children.Add(BuildSection(section));
    }

    private static UIElement BuildSection(ReportSection section)
    {
        var content = new StackPanel();
        if (!string.IsNullOrWhiteSpace(section.Text)) content.Children.Add(Selectable(section.Text, margin: new Thickness(0, 0, 0, 6)));
        if (section.Rows.Count > 0) content.Children.Add(BuildRows(section.Rows));
        if (section.Table is { } table) content.Children.Add(BuildTable(table, section.Heading));
        if (section.Bullets.Count > 0)
        {
            var list = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            AutomationProperties.SetName(list, section.Heading);
            foreach (string bullet in section.Bullets)
            {
                var line = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var dot = new TextBlock { Text = "•", Margin = new Thickness(2, 0, 8, 0) };
                dot.SetResourceReference(TextBlock.ForegroundProperty, "App.TextSecondary");
                DockPanel.SetDock(dot, Dock.Left);
                line.Children.Add(dot);
                line.Children.Add(Selectable(bullet));
                list.Children.Add(line);
            }
            content.Children.Add(Scrolling(list, section.Bullets.Count));
        }
        if (!string.IsNullOrWhiteSpace(section.Note))
        {
            var note = Selectable(section.Note, margin: new Thickness(0, 6, 0, 0));
            note.FontSize = 12;
            note.SetResourceReference(ForegroundProperty, "App.TextSecondary");
            content.Children.Add(note);
        }

        var frame = new Border
        {
            BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 10),
            CornerRadius = (CornerRadius)Application.Current.FindResource("App.Radius"),
        };
        frame.SetResourceReference(Border.BackgroundProperty, section.Tone switch
        {
            ReportTone.Warning => "App.WarningSoft",
            ReportTone.Danger => "App.DangerSoft",
            ReportTone.Success => "App.SuccessSoft",
            _ => "App.SurfaceAlt",
        });
        frame.SetResourceReference(Border.BorderBrushProperty, "App.Border");

        if (section.Collapsed)
        {
            var expander = new Expander { Header = section.Heading, Content = content, Margin = new Thickness(0, 0, 0, 10) };
            content.Margin = new Thickness(0, 6, 0, 0);
            AutomationProperties.SetName(expander, section.Heading);
            return expander;
        }
        var stack = new StackPanel();
        var heading = new TextBlock { Text = section.Heading, Margin = new Thickness(0, 0, 0, 6) };
        heading.SetResourceReference(StyleProperty, "SectionTitle");
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        stack.Children.Add(heading);
        stack.Children.Add(content);
        frame.Child = stack;
        return frame;
    }

    private static Grid BuildRows(IReadOnlyList<ReportRow> rows)
    {
        var grid = new Grid();
        // One label width for every section, so the values line up from section to section.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 200, SharedSizeGroup = "Label" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = row.Label, Margin = new Thickness(0, 3, 16, 3), VerticalAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.Wrap };
            label.SetResourceReference(TextBlock.ForegroundProperty, "App.TextSecondary");
            Grid.SetRow(label, i);
            grid.Children.Add(label);
            FrameworkElement value = row.IsPath ? PathField(row) : Selectable(row.Value, row.Tone, new Thickness(0, 3, 0, 3));
            AutomationProperties.SetName(value, row.Label);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
        }
        return grid;
    }

    /// <summary>A path: monospaced, wrapped at any character, selectable, with a copy button.</summary>
    private static FrameworkElement PathField(ReportRow row)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
        var copy = new Button { Content = "", Padding = new Thickness(6, 2, 6, 2), MinHeight = 24, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, ToolTip = L.T("ReportDialog.CopyThePath") };
        copy.SetResourceReference(StyleProperty, "SubtleButton");
        copy.SetResourceReference(FontFamilyProperty, "App.IconFont");
        AutomationProperties.SetName(copy, "Copy " + row.Label);
        copy.Click += (_, _) => TryCopy(row.Value);
        DockPanel.SetDock(copy, Dock.Right);
        panel.Children.Add(copy);
        var text = Selectable(row.Value);
        text.SetResourceReference(FontFamilyProperty, "App.MonoFont");
        text.FontSize = 12;
        var box = new Border { Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(1), Child = text, CornerRadius = (CornerRadius)Application.Current.FindResource("App.Radius") };
        box.SetResourceReference(Border.BackgroundProperty, "App.SurfaceSunken");
        box.SetResourceReference(Border.BorderBrushProperty, "App.Border");
        panel.Children.Add(box);
        AutomationProperties.SetName(panel, row.Label);
        return panel;
    }

    private static FrameworkElement BuildTable(DialogTable table, string name)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 4) };
        AutomationProperties.SetName(grid, name);
        int columns = table.Columns.Count;
        for (int c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        void Add(string text, int row, int column, bool header)
        {
            bool numeric = column >= columns - table.NumericColumns;
            var cell = new TextBlock
            {
                Text = text, Margin = new Thickness(0, 3, column == columns - 1 ? 0 : 16, 3), TextWrapping = column == 0 ? TextWrapping.Wrap : TextWrapping.NoWrap,
                HorizontalAlignment = numeric ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };
            if (header) cell.SetResourceReference(TextBlock.ForegroundProperty, "App.TextSecondary");
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < columns; c++) Add(table.Columns[c], 0, c, header: true);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var rule = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
        rule.SetResourceReference(Border.BorderBrushProperty, "App.Border");
        Grid.SetRow(rule, 1);
        Grid.SetColumnSpan(rule, columns);
        grid.Children.Add(rule);
        for (int r = 0; r < table.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < columns; c++) Add(c < table.Rows[r].Count ? table.Rows[r][c] : "", r + 2, c, header: false);
        }
        return Scrolling(grid, table.Rows.Count);
    }

    /// <summary>
    /// Long tables and lists scroll inside their section (8.8), so a dialog with hundreds of entries
    /// (duplicate names, file lists) keeps a sensible size instead of stretching to the screen's height.
    /// </summary>
    private static FrameworkElement Scrolling(FrameworkElement content, int entries)
    {
        if (entries <= LongListThreshold) return content;
        var scroll = new ScrollViewer
        {
            Content = content, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 6, 0), Focusable = true,
        };
        AutomationProperties.SetName(scroll, AutomationProperties.GetName(content));
        return scroll;
    }

    /// <summary>Tables and lists with more entries than this scroll inside their section.</summary>
    internal const int LongListThreshold = 12;

    /// <summary>Text that can be selected and copied (a read-only, borderless text box).</summary>
    private static TextBox Selectable(string text, ReportTone tone = ReportTone.Normal, Thickness? margin = null)
    {
        var box = new TextBox { Text = text, Margin = margin ?? new Thickness(0) };
        box.SetResourceReference(StyleProperty, "ValueText");
        string? brush = tone switch
        {
            ReportTone.Success => "App.Success",
            ReportTone.Warning => "App.Warning",
            ReportTone.Danger => "App.Danger",
            ReportTone.Muted => "App.TextSecondary",
            _ => null,
        };
        if (brush != null) box.SetResourceReference(ForegroundProperty, brush);
        if (tone is ReportTone.Success or ReportTone.Warning or ReportTone.Danger) box.FontWeight = FontWeights.SemiBold;
        return box;
    }

    private static bool TryCopy(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;   // the clipboard is busy (another program holds it); nothing is lost
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e) => CopyText.Text = TryCopy(_report.ToPlainText()) ? L.T("MessageDialog.Copied") : L.T("ReportDialog.ClipboardBusyTryAgain");

    private void OnPrimary(object sender, RoutedEventArgs e) => Close(ReportAnswer.Primary);

    private void OnSecondary(object sender, RoutedEventArgs e) => Close(ReportAnswer.Secondary);

    private void OnCancel(object sender, RoutedEventArgs e) => Close(ReportAnswer.Cancel);

    private void Close(ReportAnswer answer)
    {
        _answer = answer;
        DialogResult = answer != ReportAnswer.Cancel;
    }
}
