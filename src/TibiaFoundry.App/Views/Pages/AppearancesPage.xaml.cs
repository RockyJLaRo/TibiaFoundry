using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TibiaFoundry.App.ViewModels;

namespace TibiaFoundry.App.Views.Pages;

public partial class AppearancesPage : UserControl
{
    private AppearancesViewModel? _vm;

    public AppearancesPage()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm != null) _vm.SelectRequested -= OnSelectRequested;
            _vm = e.NewValue as AppearancesViewModel;
            if (_vm != null) _vm.SelectRequested += OnSelectRequested;
        };
        // Ctrl+F focuses the search box.
        InputBindings.Add(new KeyBinding(new RelayKeyCommand(() => { SearchBox.Focus(); SearchBox.SelectAll(); }), Key.F, ModifierKeys.Control));
    }

    /// <summary>The ListView's multi-selection is not bindable; mirror it into the view-model.</summary>
    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        _vm.SelectedItems = List.SelectedItems.OfType<AppearanceItem>().ToList();
    }

    private void OnSelectRequested(IReadOnlyList<AppearanceItem> items)
    {
        Dispatcher.BeginInvoke(() =>
        {
            List.SelectedItems.Clear();
            foreach (var item in items) List.SelectedItems.Add(item);
            if (items.Count > 0)
            {
                List.SelectedItem ??= items[0];
                List.ScrollIntoView(items[0]);
                List.Focus();
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnPreviewClicked(object sender, MouseButtonEventArgs e) => _vm?.CyclePreviewBackgroundCommand.Execute(null);
}

/// <summary>Minimal ICommand for view-only key bindings.</summary>
public sealed class RelayKeyCommand(Action action) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
}
