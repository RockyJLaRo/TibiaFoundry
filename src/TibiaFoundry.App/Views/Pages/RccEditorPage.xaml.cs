using System.Windows;
using System.Windows.Controls;
using TibiaFoundry.App.ViewModels;

namespace TibiaFoundry.App.Views.Pages;

public partial class RccEditorPage : UserControl
{
    public RccEditorPage() => InitializeComponent();

    private RccEditorViewModel? Vm => DataContext as RccEditorViewModel;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm != null) Vm.SelectedItems = EntryList.SelectedItems.Cast<RccEntryItem>().ToList();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && Vm?.HasFile == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static string? DroppedFile(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files && File.Exists(files[0]) ? files[0] : null;

    /// <summary>A file dropped on the preview replaces the selected resource.</summary>
    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (Vm?.Selected is { } item && DroppedFile(e) is { } file) Vm.ReplaceWith(item, file);
        e.Handled = true;
    }

    /// <summary>A file dropped on an entry replaces that entry.</summary>
    private void OnListDrop(object sender, DragEventArgs e)
    {
        if (Vm == null || DroppedFile(e) is not { } file) return;
        var element = e.OriginalSource as DependencyObject;
        while (element != null && element is not ListViewItem) element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        if (element is ListViewItem { DataContext: RccEntryItem item })
        {
            Vm.Selected = item;
            Vm.ReplaceWith(item, file);
        }
        e.Handled = true;
    }
}
