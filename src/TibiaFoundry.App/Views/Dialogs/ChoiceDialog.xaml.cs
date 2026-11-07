using System.Windows;
using System.Windows.Controls;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.Themes;

namespace TibiaFoundry.App.Views.Dialogs;

/// <summary>A themed list to choose one item from, with an optional check box and extra buttons (8.0: clients and revisions).</summary>
public partial class ChoiceDialog : Window
{
    private sealed record Row(string Title, string? Detail, string? Group, bool Enabled, bool ShowGroup)
    {
        public double Opacity => Enabled ? 1 : 0.55;
    }

    private ChoiceDialog()
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        DialogSizing.FitToWorkArea(this);
    }

    private string? _button;

    public static ChoiceResult? Show(Window? owner, string title, string message, IReadOnlyList<ChoiceItem> items, ChoiceOptions options)
    {
        var dlg = new ChoiceDialog { Title = title };
        if (owner != null && owner.IsLoaded) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.MessageText.Text = message;
        dlg.RenderFacts(options);
        dlg.Items.ItemsSource = items.Select((i, n) => new Row(i.Title, i.Detail, i.Group, i.Enabled, i.Group != null && (n == 0 || items[n - 1].Group != i.Group))).ToList();
        int initial = options.Selected is { } s && s >= 0 && s < items.Count && items[s].Enabled ? s : items.ToList().FindIndex(i => i.Enabled);
        dlg.Items.SelectedIndex = initial;
        dlg.Items.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        dlg.OkButton.Content = options.OkText;
        dlg.CancelButton.Content = options.CancelText;
        dlg.OkButton.IsEnabled = items.Count == 0 || initial >= 0;
        if (options.CheckBoxText != null)
        {
            dlg.Option.Content = options.CheckBoxText;
            dlg.Option.IsChecked = options.CheckBoxDefault;
            dlg.Option.Visibility = Visibility.Visible;
        }
        foreach (var (id, text) in options.ExtraButtons)
        {
            var button = new Button { Content = text, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), Tag = id };
            button.Click += (_, _) => { dlg._button = id; dlg.DialogResult = true; };
            dlg.ExtraButtons.Children.Add(button);
        }
        if (dlg.ShowDialog() != true) return null;
        return new ChoiceResult(dlg.Items.SelectedIndex, dlg.Option.IsChecked == true, dlg._button);
    }

    /// <summary>The summary card: one row per fact (label | value; paths monospaced), then the badges.</summary>
    private void RenderFacts(ChoiceOptions options)
    {
        if (options.Facts.Count == 0 && options.Tags.Count == 0) return;
        FactsCard.Visibility = Visibility.Visible;
        FactsHeading.Text = options.FactsHeading ?? "";
        FactsHeading.Visibility = string.IsNullOrEmpty(options.FactsHeading) ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Margin = new Thickness(22, 14, 22, 10);
        FactsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 160 });
        FactsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < options.Facts.Count; i++)
        {
            var fact = options.Facts[i];
            FactsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = fact.Label, Margin = new Thickness(0, 2, 16, 2), VerticalAlignment = VerticalAlignment.Top };
            label.SetResourceReference(TextBlock.ForegroundProperty, "App.TextSecondary");
            Grid.SetRow(label, i);
            FactsGrid.Children.Add(label);
            var value = new TextBox { Text = fact.Value, Margin = new Thickness(0, 2, 0, 2) };
            value.SetResourceReference(StyleProperty, "ValueText");
            if (fact.IsPath)
            {
                value.SetResourceReference(FontFamilyProperty, "App.MonoFont");
                value.FontSize = 12;
            }
            if (fact.Tone == ReportTone.Warning) value.SetResourceReference(ForegroundProperty, "App.Warning");
            System.Windows.Automation.AutomationProperties.SetName(value, fact.Label);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            FactsGrid.Children.Add(value);
        }
        if (options.Tags.Count == 0) return;
        TagsLabel.Text = options.TagsLabel ?? "";
        TagsLabel.Visibility = string.IsNullOrEmpty(options.TagsLabel) ? Visibility.Collapsed : Visibility.Visible;
        Tags.Visibility = Visibility.Visible;
        foreach (string tag in options.Tags)
        {
            var chip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 6, 6) };
            chip.SetResourceReference(Border.BackgroundProperty, "App.AccentSoft");
            var text = new TextBlock { Text = tag, FontSize = 12 };
            chip.Child = text;
            Tags.Children.Add(chip);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OkButton.IsEnabled = Items.SelectedItem is Row { Enabled: true };
    }

    private void OnDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Items.SelectedItem is Row { Enabled: true }) DialogResult = true;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
