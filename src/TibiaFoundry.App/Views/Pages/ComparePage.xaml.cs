using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TibiaFoundry.App.ViewModels;

namespace TibiaFoundry.App.Views.Pages;

public partial class ComparePage : UserControl
{
    public ComparePage() => InitializeComponent();

    private void OnEntryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only react to double-clicks on a row, not on the header or scroll bar.
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(EntryList, source) is ListViewItem
            && DataContext is CompareViewModel vm)
        {
            vm.OpenEntryCommand.Execute(null);
        }
    }
}
