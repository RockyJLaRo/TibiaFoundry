using System.Windows.Controls;
using System.Windows.Input;
using TibiaFoundry.App.ViewModels;

namespace TibiaFoundry.App.Views.Pages;

public partial class AssetsEditorPage : UserControl
{
    public AssetsEditorPage() => InitializeComponent();

    private AssetsEditorViewModel? Vm => DataContext as AssetsEditorViewModel;

    private void OnObjectSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm != null) Vm.SelectedItems = ObjectList.SelectedItems.Cast<EditorListItem>().ToList();
    }

    private void OnSheetClick(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(SheetView);
        // The sheet is shown at its own size (384 × 384).
        Vm?.SheetClicked(p.X * 384 / SheetView.ActualWidth, p.Y * 384 / SheetView.ActualHeight);
    }
}
