using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TibiaFoundry.App.Views.Pages;

public partial class ClientsPage : UserControl
{
    public ClientsPage() => InitializeComponent();

    /// <summary>The "more actions" buttons open their menu below themselves on a left click (a menu button).</summary>
    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.DataContext = button.DataContext;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
