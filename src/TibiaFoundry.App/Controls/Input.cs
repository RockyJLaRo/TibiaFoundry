using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TibiaFoundry.App.Controls;

/// <summary>Attached input helpers used by the TextBox template.</summary>
public static class Input
{
    /// <summary>Gray hint text shown inside an empty TextBox (e.g. "Filter by name or ID").</summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Input), new FrameworkPropertyMetadata(""));

    public static string GetPlaceholder(DependencyObject element) => (string)element.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject element, string value) => element.SetValue(PlaceholderProperty, value);

    /// <summary>
    /// A TextBox that takes the keyboard focus, with its text selected, whenever it becomes visible
    /// (an editor that appears in place, such as the Item Catalog's rename box).
    /// </summary>
    public static readonly DependencyProperty FocusWhenVisibleProperty =
        DependencyProperty.RegisterAttached("FocusWhenVisible", typeof(bool), typeof(Input), new FrameworkPropertyMetadata(false, OnFocusWhenVisibleChanged));

    public static bool GetFocusWhenVisible(DependencyObject element) => (bool)element.GetValue(FocusWhenVisibleProperty);
    public static void SetFocusWhenVisible(DependencyObject element, bool value) => element.SetValue(FocusWhenVisibleProperty, value);

    private static void OnFocusWhenVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        box.IsVisibleChanged -= FocusIfVisible;
        if ((bool)e.NewValue) box.IsVisibleChanged += FocusIfVisible;
    }

    private static void FocusIfVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || !(bool)e.NewValue) return;
        // After layout: a control that has just become visible cannot take the focus yet.
        box.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!box.IsVisible) return;
            box.Focus();
            Keyboard.Focus(box);
            box.SelectAll();
        });
    }

    /// <summary>
    /// A command run with the parameter "focus" when the element loses the keyboard focus (clicking
    /// elsewhere, switching to another program). Buttons next to the element should not take the focus
    /// (Focusable="False"), so clicking them does not count as leaving it.
    /// </summary>
    public static readonly DependencyProperty LostFocusCommandProperty =
        DependencyProperty.RegisterAttached("LostFocusCommand", typeof(ICommand), typeof(Input), new FrameworkPropertyMetadata(null, OnLostFocusCommandChanged));

    public static ICommand? GetLostFocusCommand(DependencyObject element) => (ICommand?)element.GetValue(LostFocusCommandProperty);
    public static void SetLostFocusCommand(DependencyObject element, ICommand? value) => element.SetValue(LostFocusCommandProperty, value);

    private static void OnLostFocusCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.LostKeyboardFocus -= RunLostFocusCommand;
        if (e.NewValue != null) element.LostKeyboardFocus += RunLostFocusCommand;
    }

    private static void RunLostFocusCommand(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not DependencyObject element || GetLostFocusCommand(element) is not { } command) return;
        // Focus moving to a part of the element itself (for example its context menu) is not leaving it.
        if (e.NewFocus is Visual next && sender is Visual self && self.IsAncestorOf(next)) return;
        if (command.CanExecute("focus")) command.Execute("focus");
    }
}
