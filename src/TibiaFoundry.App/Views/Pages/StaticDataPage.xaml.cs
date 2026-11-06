using System.Windows.Controls;
using System.Windows.Input;
using TibiaFoundry.App.ViewModels;

namespace TibiaFoundry.App.Views.Pages;

public partial class StaticDataPage : UserControl
{
    private StaticDataViewModel? _vm;

    public StaticDataPage()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm != null) _vm.SelectRequested -= OnSelectRequested;
            _vm = e.NewValue as StaticDataViewModel;
            if (_vm != null) _vm.SelectRequested += OnSelectRequested;
        };
    }

    /// <summary>The ListView's multi-selection is not bindable; mirror it into the view-model.</summary>
    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm != null) _vm.SelectedItems = List.SelectedItems.OfType<ListItem>().ToList();
    }

    private void OnSelectRequested(IReadOnlyList<ListItem> items)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (items.Count == List.Items.Count) List.SelectAll();
            else
            {
                List.SelectedItems.Clear();
                foreach (var item in items) List.SelectedItems.Add(item);
            }
            List.Focus();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnPreviewClicked(object sender, MouseButtonEventArgs e) => _vm?.CycleBackgroundCommand.Execute(null);
}
