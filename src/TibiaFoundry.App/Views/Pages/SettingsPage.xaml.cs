using System.Windows;
using System.Windows.Controls;

namespace TibiaFoundry.App.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    /// <summary>Scrolls the chosen category to the top of the page.</summary>
    private void OnCategorySelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not string category) return;
        var section = Sections.Children.OfType<GroupBox>().FirstOrDefault(g => Equals(g.Tag, category));
        if (section == null) return;
        var offset = section.TransformToAncestor(Sections).Transform(new Point(0, 0)).Y;
        Scroller.ScrollToVerticalOffset(offset);
    }
}
