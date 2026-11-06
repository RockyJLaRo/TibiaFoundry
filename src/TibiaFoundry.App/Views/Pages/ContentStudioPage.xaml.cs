using System.Windows.Controls;
using TibiaFoundry.App.ViewModels;

namespace TibiaFoundry.App.Views.Pages;

public partial class ContentStudioPage : UserControl
{
    public ContentStudioPage() => InitializeComponent();

    /// <summary>The picker allows several entries (Ctrl/Shift+click); "Add the selected entries" adds them to the step.</summary>
    private void OnPickerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ContentStudioViewModel vm) vm.PickedItems = Picker.SelectedItems.Cast<PickerItem>().ToList();
    }
}
