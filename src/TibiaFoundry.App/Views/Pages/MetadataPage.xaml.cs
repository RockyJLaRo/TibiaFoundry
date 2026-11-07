using System.Windows;
using System.Windows.Controls;
using TibiaFoundry.App.ViewModels;

namespace TibiaFoundry.App.Views.Pages;

public partial class MetadataPage : UserControl
{
    public MetadataPage() => InitializeComponent();

    private MetadataViewModel? Vm => DataContext as MetadataViewModel;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm != null && ReferenceEquals(e.OriginalSource, FileList)) Vm.SetSelection(FileList.SelectedItems.Cast<MetadataFileItem>().ToList());
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && Vm?.IsBusy == false ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Files and folders dropped anywhere on the page are added to the list.</summary>
    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Vm == null || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        await Vm.AddPathsAsync(paths);
    }
}
