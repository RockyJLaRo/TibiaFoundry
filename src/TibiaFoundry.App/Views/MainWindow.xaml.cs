using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.Themes;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Views;

/// <summary>
/// Main window: resizable/maximizable, remembers its placement (Settings → Appearance), scales the
/// whole interface, shows progress on the taskbar button, and collapses the navigation rail to
/// icons when the window becomes narrow (or as chosen in Settings).
/// </summary>
public partial class MainWindow : Window
{
    private const double CompactNavThreshold = 1100;
    private readonly MainViewModel _vm;
    private bool _closingConfirmed;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        ThemeManager.Attach(this);
        SizeChanged += (_, _) => UpdateNavMode();
        vm.Status.PropertyChanged += OnStatusChanged;
        vm.Services.InterfaceChanged += ApplyInterface;
        Closing += OnClosing;
        Activated += (_, _) => _vm.OnWindowActivated();
        UpdateStatusIcon();
        ApplyInterface();
        // The navigation groups are named in the selected language: regroup after a switch.
        Core.Localization.L.LanguageChanged += () => Dispatcher.BeginInvoke(() => ((System.Windows.Data.CollectionViewSource)Resources["GroupedPages"]).View?.Refresh());
    }

    private InterfaceSettings Interface => _vm.Services.Settings.Interface;

    /// <summary>Applies Settings → Appearance: interface zoom and navigation layout.</summary>
    public void ApplyInterface()
    {
        double scale = Interface.Scale;
        RootGrid.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(scale, scale);
        UpdateNavMode(force: true);
        UpdateTaskbar();
    }

    private void UpdateNavMode(bool force = false)
    {
        bool expanded = Interface.Navigation switch
        {
            NavigationLayout.Expanded => true,
            NavigationLayout.Compact => false,
            _ => ActualWidth / Math.Max(0.5, Interface.Scale) >= CompactNavThreshold || ActualWidth == 0,
        };
        if (!force && _vm.IsNavExpanded == expanded && NavColumn.Width.Value > 0) return;
        _vm.IsNavExpanded = expanded;
        NavColumn.Width = new GridLength(expanded ? 224 : 64);
        var v = expanded ? Visibility.Visible : Visibility.Collapsed;
        BrandText.Visibility = v;
        TestServerLabel.Visibility = v;
        ThemeLabel.Visibility = v;
        // Narrow rail: the language button goes under the theme switch instead of beside it.
        DockPanel.SetDock(LanguageButton, expanded ? Dock.Right : Dock.Bottom);
        LanguageButton.Margin = expanded ? new Thickness(4, 0, 0, 0) : new Thickness(0, 4, 0, 0);
        ClientInUseButton.Visibility = v;
    }

    /// <summary>Opens the language menu above the button (also with the keyboard: Enter or Space on the button).</summary>
    private void OnLanguageButtonClick(object sender, RoutedEventArgs e)
    {
        LanguageMenu.PlacementTarget = LanguageButton;
        LanguageMenu.DataContext = _vm;
        LanguageMenu.IsOpen = true;
    }

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StatusService.Kind)) UpdateStatusIcon();
        if (e.PropertyName is nameof(StatusService.IsBusy) or nameof(StatusService.Progress) or nameof(StatusService.IsIndeterminate)) UpdateTaskbar();
    }

    /// <summary>Progress of the running operation on the taskbar button (Settings → Performance).</summary>
    private void UpdateTaskbar()
    {
        var status = _vm.Status;
        if (!_vm.Services.Settings.Performance.ShowTaskbarProgress || !status.IsBusy)
        {
            Taskbar.ProgressState = TaskbarItemProgressState.None;
            return;
        }
        Taskbar.ProgressState = status.IsIndeterminate ? TaskbarItemProgressState.Indeterminate : TaskbarItemProgressState.Normal;
        Taskbar.ProgressValue = status.Progress / 100.0;
    }

    private void UpdateStatusIcon()
    {
        (int glyph, string brush) = _vm.Status.Kind switch
        {
            StatusKind.Success => (0xE73E, "App.Success"),
            StatusKind.Warning => (0xE7BA, "App.Warning"),
            StatusKind.Error => (0xEA39, "App.Danger"),
            _ => (0xE946, "App.TextSecondary"),
        };
        StatusIcon.Text = char.ConvertFromUtf32(glyph);
        StatusIcon.SetResourceReference(TextBlock.ForegroundProperty, brush);
    }

    /// <summary>Applies saved placement, keeping the window on a visible part of the desktop.</summary>
    public void RestorePlacement(WindowPlacement placement, Size? forcedSize)
    {
        if (forcedSize is { } size)
        {
            Width = Math.Max(MinWidth, size.Width);
            Height = Math.Max(MinHeight, size.Height);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }
        Width = Math.Max(MinWidth, placement.Width);
        Height = Math.Max(MinHeight, placement.Height);
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!double.IsNaN(placement.Left) && !double.IsNaN(placement.Top))
        {
            var saved = new Rect(placement.Left, placement.Top, Width, Height);
            // Require a reasonable part of the title bar to be visible (monitor may have been removed).
            var titleBar = new Rect(saved.Left + 40, saved.Top, Math.Max(1, saved.Width - 80), 30);
            if (desktop.IntersectsWith(titleBar))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = placement.Left;
                Top = placement.Top;
            }
        }
        Width = Math.Min(Width, desktop.Width);
        Height = Math.Min(Height, desktop.Height);
        if (placement.Maximized) Loaded += (_, _) => WindowState = WindowState.Maximized;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Ask once before anything is lost: editors' unsaved changes, and (8.5) running operations — an
        // archive, an export, the background archive after the launch check — which closing stops.
        var unsaved = _vm.UnsavedChanges;
        var running = _vm.Services.Jobs.Active.Select(o => o.Title).ToList();
        if (_vm.BackgroundArchive is { IsCompleted: false }) running.Add(L.T("MainWindow.ArchivingTheInstalledClientInThe"));
        if (!_closingConfirmed && (unsaved.Count > 0 || running.Count > 0) && !DialogService.Unattended)
        {
            var parts = new List<string>();
            if (unsaved.Count > 0) parts.Add(L.T("MainWindow.TheseChangesHaveNotBeenSaved") + string.Join("\n• ", unsaved));
            if (running.Count > 0) parts.Add(L.T("MainWindow.TheseOperationsAreStillRunningThey") + string.Join("\n• ", running.Distinct()));
            bool close = _vm.Services.Dialogs.Confirm(string.Join("\n\n", parts) + L.T("MainWindow.CloseTibiaFoundryAnyway"),
                unsaved.Count > 0 ? L.T("MainWindow.UnsavedChanges") : L.T("MainWindow.OperationsRunning"), unsaved.Count > 0 ? L.T("MainWindow.CloseWithoutSaving") : L.T("MainWindow.CloseAndStopThem"), L.T("MainWindow.KeepWorking"));
            if (!close)
            {
                e.Cancel = true;
                return;
            }
            _closingConfirmed = true;
        }
        if (Interface.RememberWindowPlacement)
        {
            var p = _vm.Services.Settings.MainWindow;
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            if (!bounds.IsEmpty && bounds.Width > 0)
            {
                p.Left = bounds.Left;
                p.Top = bounds.Top;
                p.Width = bounds.Width;
                p.Height = bounds.Height;
            }
            p.Maximized = WindowState == WindowState.Maximized;
        }
        _vm.Services.Audio.Stop();
        _vm.Services.SaveSettings();
    }
}
