using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TibiaFoundry.App.ViewModels;

namespace TibiaFoundry.App.Views.Pages;

public partial class SpriteExtractorPage : UserControl
{
    public SpriteExtractorPage() => InitializeComponent();

    private SpriteExtractorViewModel? ViewModel => DataContext as SpriteExtractorViewModel;

    // ListView.SelectedItems is not bindable; mirror the multi-selection into the view-model.
    private void OnAvailableSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } vm) vm.SelectedAvailable = AvailableList.SelectedItems.Cast<SheetRow>().ToList();
    }

    private void OnQueueSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } vm) vm.SelectedQueued = QueueList.SelectedItems.Cast<SheetRow>().ToList();
    }

    private void OnAvailableDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is { } vm && IsOnRow(AvailableList, e)) vm.AddSelectedCommand.Execute(null);
    }

    private void OnQueueDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is { } vm && IsOnRow(QueueList, e)) vm.RemoveSelectedCommand.Execute(null);
    }

    private static bool IsOnRow(ListView list, MouseButtonEventArgs e) =>
        e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(list, source) is ListViewItem;

    private void OnQueueKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && ViewModel is { } vm)
        {
            vm.RemoveSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }
}
