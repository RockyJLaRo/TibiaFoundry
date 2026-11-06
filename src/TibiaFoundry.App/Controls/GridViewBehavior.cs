using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TibiaFoundry.App.Controls;

/// <summary>
/// Makes the last column of a <see cref="GridView"/> fill the remaining width of its ListView, so
/// lists never show a horizontal scroll bar just because the fixed column widths do not add up.
/// Columns the user resizes keep their width; only the last column is adjusted.
/// </summary>
public static class GridViewBehavior
{
    private const double MinLastColumnWidth = 60;

    public static readonly DependencyProperty FillLastColumnProperty =
        DependencyProperty.RegisterAttached("FillLastColumn", typeof(bool), typeof(GridViewBehavior),
            new PropertyMetadata(false, OnFillLastColumnChanged));

    public static bool GetFillLastColumn(DependencyObject element) => (bool)element.GetValue(FillLastColumnProperty);
    public static void SetFillLastColumn(DependencyObject element, bool value) => element.SetValue(FillLastColumnProperty, value);

    private static void OnFillLastColumnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListView list) return;
        if (e.NewValue is true)
        {
            list.SizeChanged += OnSizeChanged;
            list.Loaded += OnLoaded;
        }
        else
        {
            list.SizeChanged -= OnSizeChanged;
            list.Loaded -= OnLoaded;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Fill((ListView)sender);
    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Fill((ListView)sender);

    private static void Fill(ListView list)
    {
        if (list.View is not GridView view || view.Columns.Count == 0 || list.ActualWidth <= 0) return;
        double available = list.ActualWidth - list.BorderThickness.Left - list.BorderThickness.Right - list.Padding.Left - list.Padding.Right - 8;
        if (FindScrollViewer(list) is { ComputedVerticalScrollBarVisibility: Visibility.Visible }) available -= SystemParameters.VerticalScrollBarWidth;
        double others = 0;
        for (int i = 0; i < view.Columns.Count - 1; i++) others += double.IsNaN(view.Columns[i].Width) ? view.Columns[i].ActualWidth : view.Columns[i].Width;
        view.Columns[^1].Width = Math.Max(MinLastColumnWidth, Math.Floor(available - others));
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }
}
