using System.Windows.Controls;
using System.Windows.Input;
using TibiaFoundry.App.ViewModels;

namespace TibiaFoundry.App.Views.Pages;

public partial class AssetLibraryPage : UserControl
{
    public AssetLibraryPage() => InitializeComponent();

    /// <summary>The grid allows several selected assets (Ctrl/Shift+click, Ctrl+A); the view-model edits them together.</summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is AssetLibraryViewModel vm) vm.SetSelection(AssetGrid.SelectedItems.Cast<AssetTile>().ToList());
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is AssetLibraryViewModel vm && vm.OpenFileCommand.CanExecute(null)) vm.OpenFileCommand.Execute(null);
    }
}
