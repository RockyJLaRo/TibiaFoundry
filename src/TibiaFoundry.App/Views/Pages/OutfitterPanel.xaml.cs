using System.Windows.Controls;
using System.Windows.Input;
using TibiaFoundry.App.ViewModels;

namespace TibiaFoundry.App.Views.Pages;

public partial class OutfitterPanel : UserControl
{
    public OutfitterPanel() => InitializeComponent();

    private void OnPreviewClicked(object sender, MouseButtonEventArgs e) => (DataContext as OutfitterViewModel)?.CycleBackgroundCommand.Execute(null);
}
