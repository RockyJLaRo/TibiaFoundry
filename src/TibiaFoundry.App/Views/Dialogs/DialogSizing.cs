using System.Windows;

namespace TibiaFoundry.App.Views.Dialogs;

/// <summary>Keeps the dialogs on small screens and at high display scaling (8.5).</summary>
internal static class DialogSizing
{
    /// <summary>
    /// Limits a dialog to the work area: its height never exceeds it (the content scrolls instead of
    /// the buttons running off the screen), nor does its width.
    /// </summary>
    public static void FitToWorkArea(Window window, double margin = 48) => FitTo(window, SystemParameters.WorkArea, margin);

    /// <summary><see cref="FitToWorkArea"/> for a given work area (in device-independent units; tests and the UI snapshot simulate small screens).</summary>
    internal static void FitTo(Window window, Rect area, double margin = 48)
    {
        double height = area.Height - margin, width = area.Width - margin;
        if (height > 0 && (double.IsInfinity(window.MaxHeight) || window.MaxHeight > height)) window.MaxHeight = Math.Max(window.MinHeight, height);
        if (width > 0 && !double.IsNaN(window.Width) && window.Width > width) window.Width = Math.Max(window.MinWidth, width);
    }
}
