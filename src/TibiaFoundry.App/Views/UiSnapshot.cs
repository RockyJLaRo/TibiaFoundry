using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.Themes;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Localization;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.App.Views;

/// <summary>
/// Automated UI verification (<c>--ui-snapshot &lt;folder&gt;</c>): visits every page, waits for its
/// data to load, renders the window to PNG and records WPF binding errors and logged
/// warnings/errors in <c>report.txt</c>. Without <c>--theme</c> both themes are captured.
/// </summary>
public static class UiSnapshot
{
    private static readonly TimeSpan PageTimeout = TimeSpan.FromMinutes(3);

    private static string? _folder;

    public static async Task RunAsync(MainWindow window, MainViewModel vm, string folder, bool bothThemes)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
        var report = new StringBuilder();
        var bindingErrors = new BindingErrorListener();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        var logged = new List<string>();
        void OnLogged(LogLevel level, string message)
        {
            if (level >= LogLevel.Warning) lock (logged) logged.Add($"[{level}] {message}");
        }
        AppLog.MessageLogged += OnLogged;

        report.AppendLine($"UI snapshot {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Window: {window.ActualWidth:0}×{window.ActualHeight:0} (DIP), DPI scale {VisualTreeHelper.GetDpi(window).DpiScaleX:0.##}");
        report.AppendLine($"Navigation expanded: {vm.IsNavExpanded}");
        report.AppendLine($"Language: {L.Language.EnglishName} ({L.Language.Code})");
        report.AppendLine();

        try
        {
            var themes = bothThemes ? new[] { ThemePreference.Light, ThemePreference.Dark } : new[] { vm.Theme == ThemePreference.Dark || (vm.Theme == ThemePreference.System && ThemeManager.IsDark) ? ThemePreference.Dark : ThemePreference.Light };
            foreach (var theme in themes)
            {
                ThemeManager.Apply(theme);
                string themeName = theme.ToString().ToLowerInvariant();
                for (int i = 0; i < vm.Pages.Count; i++)
                {
                    var page = vm.Pages[i];
                    int bindingErrorsBefore = bindingErrors.Count;
                    var watch = Stopwatch.StartNew();
                    if (ReferenceEquals(vm.SelectedPage, page)) _ = page.OnNavigatedToAsync();
                    else vm.SelectedPage = page;
                    await WaitForIdleAsync(page);
                    string file = Path.Combine(folder, $"{i:00}_{page.GetType().Name.Replace("ViewModel", "")}_{themeName}.png");
                    Render(window, file);
                    report.AppendLine($"{Path.GetFileName(file),-48} {watch.Elapsed.TotalSeconds,6:0.0}s  status: {vm.Status.Message}");
                    foreach (string error in bindingErrors.Since(bindingErrorsBefore)) report.AppendLine("    binding: " + error);
                }
                await RenderDialogsAsync(folder, themeName, report);
                await RenderReportsAsync(vm.Services, folder, themeName, report);
                await RenderMetadataPageAsync(window, vm, folder, themeName, report);
                report.AppendLine();
            }
            await RunChecksAsync(window, vm, report);
        }
        catch (Exception ex)
        {
            report.AppendLine("Snapshot failed: " + ex);
            AppLog.Error("UI snapshot failed.", ex);
        }
        finally
        {
            AppLog.MessageLogged -= OnLogged;
            PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors);
        }

        report.AppendLine($"Binding errors: {bindingErrors.Count}");
        lock (logged)
        {
            report.AppendLine($"Logged warnings/errors: {logged.Count}");
            foreach (string line in logged) report.AppendLine("    " + line);
        }
        File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString());
    }

    /// <summary>
    /// Scripted window checks: live theme switching (palette, controls and title bar), theme
    /// persistence, compact navigation, minimum size, maximize/restore, client package switching and
    /// window placement persistence. Each check writes PASS or FAIL to the report.
    /// </summary>
    private static async Task RunChecksAsync(MainWindow window, MainViewModel vm, StringBuilder report)
    {
        int failures = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (!ok) failures++;
            report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
        }
        async Task Settle()
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(150);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        Color Brush(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;
        Color WindowColor() => ((SolidColorBrush)window.Background).Color;
        // An input box with the standard style (value displays use a transparent background).
        Color? FirstTextBoxColor() => FindFirst<System.Windows.Controls.TextBox>(window, t => t.Background is SolidColorBrush { Color.A: > 0 })?.Background is SolidColorBrush b ? b.Color : null;
        string SettingsJson() => File.Exists(vm.Services.SettingsStore.FilePath) ? File.ReadAllText(vm.Services.SettingsStore.FilePath) : "";

        report.AppendLine("Window checks");
        vm.SelectedPage = vm.Pages.First(p => p is SettingsViewModel);
        await Settle();

        // Live theme switching: palette swap reaches the window, its controls and the title bar.
        // (Going through System first guarantees that setting Light is a real, saved change.)
        vm.Theme = ThemePreference.System;
        vm.Theme = ThemePreference.Light;
        await Settle();
        var lightWindow = WindowColor();
        var lightBox = FirstTextBoxColor();
        Check("light theme applied", !ThemeManager.IsDark && lightWindow == Brush("App.WindowBackground"), $"window {lightWindow}");
        Check("light title bar", DarkTitleBar(window) == false);
        Check("light theme saved", SettingsJson().Contains("\"Theme\": \"Light\"", StringComparison.Ordinal));

        vm.ToggleThemeCommand.Execute(null);
        await Settle();
        var darkWindow = WindowColor();
        var darkBox = FirstTextBoxColor();
        Check("toggle switches to dark live", ThemeManager.IsDark && vm.IsDarkTheme && darkWindow == Brush("App.WindowBackground") && darkWindow != lightWindow,
              $"window {lightWindow} -> {darkWindow}");
        Check("controls restyled without restart", lightBox != null && darkBox != null && lightBox != darkBox && darkBox == Brush("App.InputBackground"),
              $"text box {lightBox} -> {darkBox}");
        Check("dark title bar", DarkTitleBar(window) == true);
        Check("dark theme saved", SettingsJson().Contains("\"Theme\": \"Dark\"", StringComparison.Ordinal));

        vm.Theme = ThemePreference.System;
        await Settle();
        Check("system theme follows Windows", ThemeManager.IsDark == ThemeManager.SystemUsesDarkTheme(), $"Windows dark mode: {ThemeManager.SystemUsesDarkTheme()}");

        // Branding: window title and icon, the logo in the navigation header, the About page's creator links.
        var languageButton = window.FindName("LanguageButton") as System.Windows.Controls.Button;
        var languageMenu = window.FindName("LanguageMenu") as System.Windows.Controls.ContextMenu;
        int languageItems = 0;
        if (languageButton != null && languageMenu != null)
        {
            languageMenu.PlacementTarget = languageButton;
            languageMenu.IsOpen = true;
            languageMenu.UpdateLayout();
            languageItems = languageMenu.Items.Count;
            languageMenu.IsOpen = false;
        }
        Check("language button beside the theme toggle shows the current language", languageButton is { IsVisible: true } && languageMenu != null
              && languageItems == Languages.All.Count && vm.CurrentLanguage.Code == L.Language.Code,
              $"{vm.CurrentLanguage.ShortLabel}, {languageItems} languages");
        Check("window title and icon are Tibia Foundry", window.Title == "Tibia Foundry" && window.Icon is System.Windows.Media.Imaging.BitmapFrame { PixelWidth: > 0 },
            $"\"{window.Title}\"");
        var navLogo = FindFirst<System.Windows.Controls.Image>(window, i => System.Windows.Automation.AutomationProperties.GetName(i) == "Tibia Foundry");
        Check("logo shown in the navigation header", navLogo is { IsVisible: true, ActualWidth: 32 } && navLogo.Source is System.Windows.Media.Imaging.BitmapSource { PixelWidth: 32 });

        // Tagline next to the logo: exact wording, "TibiaWiki" is a link to the wiki's main page.
        var tagline = window.FindName("Tagline") as System.Windows.Controls.TextBlock;
        static string TextOf(IEnumerable<System.Windows.Documents.Inline> inlines) => string.Concat(inlines.Select(i => i switch
        {
            System.Windows.Documents.Run run => run.Text,
            System.Windows.Documents.Span span => TextOf(span.Inlines),
            _ => "",
        }));
        string taglineText = tagline == null ? "" : TextOf(tagline.Inlines);
        var taglineLink = tagline?.Inlines.OfType<System.Windows.Documents.Hyperlink>().SingleOrDefault();
        string taglineLinkText = taglineLink == null ? "" : TextOf(taglineLink.Inlines);
        // With compact navigation the header shows only the logo; the tagline is then its tooltip.
        bool taglineShown = vm.IsNavExpanded ? tagline is { IsVisible: true } : navLogo?.ToolTip as string == "Tibia Foundry - All your Tibia-Tools for TibiaWiki";
        Check("tagline next to the logo, TibiaWiki linked", tagline != null && taglineShown && taglineText == "Tibia Foundry - All your Tibia-Tools for TibiaWiki"
              && taglineLinkText == "TibiaWiki" && ReferenceEquals(taglineLink!.Command, vm.OpenTibiaWikiCommand),
              $"\"{taglineText}\"{(vm.IsNavExpanded ? "" : ", compact navigation: shown as the logo's tooltip")}");

        vm.SelectedPage = vm.Pages.First(p => p is AboutViewModel);
        await Settle();
        var linkButtons = FindAll<System.Windows.Controls.Button>(window, b => b.DataContext is CreatorLink).ToList();
        int iconCount = linkButtons.Count(b => FindAll<System.Windows.Controls.Image>(b, i => i.IsVisible && i.Source != null).Any());
        Check("About page shows the creator links with icons", linkButtons.Count == 6 && iconCount == 6, $"{linkButtons.Count} links, {iconCount} icons");
        var faviconLinks = linkButtons
            .Select(b => (Link: (CreatorLink)b.DataContext, Shown: FindAll<System.Windows.Controls.Image>(b, i => i.IsVisible && i.Source is System.Windows.Media.Imaging.BitmapSource).Select(i => (System.Windows.Media.Imaging.BitmapSource)i.Source).FirstOrDefault()))
            .Where(x => x.Shown != null).ToList();
        Check("TibiaWiki Profile and Tibia Character show their sites' icons",
              faviconLinks.Select(x => x.Link.Label).SequenceEqual([L.T("About.TibiaWikiProfile"), L.T("About.TibiaCharacter")]) && faviconLinks.Select(x => x.Shown!.PixelWidth).SequenceEqual([48, 32]),
              string.Join(", ", faviconLinks.Select(x => $"{x.Link.Label} {x.Shown!.PixelWidth} px")));

        // Open-source acknowledgments: their own section after the credits, three projects.
        var acknowledgments = FindFirst<System.Windows.Controls.GroupBox>(window, g => g.Header as string == L.T("About.OpenSourceAcknowledgments"));
        var creditsGroup = FindFirst<System.Windows.Controls.GroupBox>(window, g => g.Header as string == L.T("About.Credits"));
        int acknowledged = acknowledgments == null ? 0 : FindAll<System.Windows.Controls.ContentPresenter>(acknowledgments, c => c.Content is Acknowledgment).Count();
        int inCredits = creditsGroup == null ? -1 : FindAll<System.Windows.Controls.ContentPresenter>(creditsGroup, c => c.Content is Acknowledgment).Count();
        Check("open-source acknowledgments listed separately from the credits", acknowledged == 3 && inCredits == 0, $"{acknowledged} in its section, {inCredits} in Credits");
        if (acknowledgments != null && _folder != null)
        {
            acknowledgments.BringIntoView();
            await Settle();
            Render(window, Path.Combine(_folder, "About_acknowledgments.png"));
        }

        // Credits: exactly Tibia, CipSoft and TibiaWiki, with their logos (proportions kept), in both themes.
        ImageSource? lightCipSoftLogo = null;
        foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
        {
            vm.Theme = theme;
            await Settle();
            string detail = CheckCredits(window, out var cipSoftLogo, out bool ok);
            Check($"Credits list exactly Tibia, CipSoft and TibiaWiki with logos and links ({theme.ToString().ToLowerInvariant()})", ok, detail);
            if (theme == ThemePreference.Light) lightCipSoftLogo = cipSoftLogo;
            else Check("CipSoft logo follows the theme", cipSoftLogo != null && lightCipSoftLogo != null && !ReferenceEquals(cipSoftLogo, lightCipSoftLogo));
            if (_folder != null) Render(window, Path.Combine(_folder, $"About_credits_{theme.ToString().ToLowerInvariant()}.png"));
        }
        vm.Theme = ThemePreference.System;
        await Settle();

        // Settings: choosing a category scrolls its section to the top (or as far as the page goes).
        var settingsVm = (SettingsViewModel)vm.Pages.First(p => p is SettingsViewModel);
        vm.SelectedPage = settingsVm;
        await Settle();
        var settingsView = FindFirst<Pages.SettingsPage>(window, _ => true);
        if (settingsView == null) Check("Settings page shown", false);
        else
        {
            var misplaced = new List<string>();
            foreach (string category in settingsVm.Categories)
            {
                settingsVm.SelectedCategory = category;
                await Settle();
                var section = settingsView.Sections.Children.OfType<System.Windows.Controls.GroupBox>().FirstOrDefault(g => Equals(g.Tag, category));
                if (section == null) { misplaced.Add(category + " (no section)"); continue; }
                double top = section.TransformToAncestor(settingsView.Scroller).Transform(new Point(0, 0)).Y;
                bool atEnd = settingsView.Scroller.VerticalOffset >= settingsView.Scroller.ScrollableHeight - 1;
                if (!(Math.Abs(top) <= 20 || (atEnd && top > 0 && top < settingsView.Scroller.ViewportHeight))) misplaced.Add($"{category} at {top:0}");
            }
            Check($"Settings categories jump to their sections ({settingsVm.Categories.Count})", misplaced.Count == 0, string.Join(", ", misplaced));
            settingsVm.SelectedCategory = "Appearance & Themes";
            await Settle();
            if (_folder != null) Render(window, Path.Combine(_folder, "Settings_appearance_and_themes.png"));
            settingsVm.SelectedCategory = "Files and folders";
            await Settle();
            if (_folder != null) Render(window, Path.Combine(_folder, "Settings_files_and_folders.png"));
            // Client and data locations: every file and folder read from the client, with where it came from.
            settingsVm.SelectedCategory = "Tibia client";
            await Settle();
            var locationList = FindFirst<System.Windows.Controls.ItemsControl>(settingsView, c => System.Windows.Automation.AutomationProperties.GetName(c) == L.T("Settings.ClientAndDataLocations"));
            locationList?.BringIntoView();
            await Settle();
            Check($"Settings list every client location ({Core.Locations.LocationResolver.All.Count})",
                locationList != null && settingsVm.LocationRows.Count == Core.Locations.LocationResolver.All.Count && locationList.Items.Count == settingsVm.LocationRows.Count);
            if (_folder != null) Render(window, Path.Combine(_folder, "Settings_client_locations.png"));
            settingsVm.SelectedCategory = "Tools";
            await Settle();
            if (_folder != null) Render(window, Path.Combine(_folder, "Settings_tools.png"));

            // Appearance & Themes: a custom theme changes the real window at once, color by color.
            var editor = settingsVm.Themes;
            var themes = vm.Services.Settings.Themes;
            var custom = new Core.Theming.CustomTheme { Id = Core.Theming.ThemeSettings.NewId(), Name = "Snapshot check", Base = Core.Theming.ThemeBase.Light };
            themes.Custom.Add(custom);
            editor.Refresh();
            editor.Activate(custom.Id);
            settingsVm.SelectedCategory = "Appearance & Themes";
            await Settle();
            var boxBefore = FirstTextBoxColor();
            bool windowSet = editor.TrySetColor("WindowBackground", "#E9DCCB", out _);
            bool inputSet = editor.TrySetColor("InputBackground", "#FFF8EE", out _);
            editor.SelectedColor = editor.Colors.First(c => c.Key == "Accent");
            bool accentSet = editor.TrySetColor("Accent", "#9C3D2E", out _);
            await Settle();
            var windowAfter = WindowColor();
            var boxAfter = FirstTextBoxColor();
            Check("theme colors apply to the window immediately", windowSet && inputSet && accentSet && windowAfter == Color.FromRgb(0xE9, 0xDC, 0xCB)
                  && boxAfter == Color.FromRgb(0xFF, 0xF8, 0xEE) && boxBefore != boxAfter && Brush("App.Accent") == Color.FromRgb(0x9C, 0x3D, 0x2E),
                  $"window {windowAfter}, input {boxBefore} -> {boxAfter}");
            Check("unsaved theme changes are marked and kept apart from the saved theme", editor.HasUnsavedChanges && themes.Find(custom.Id)!.Colors.Count == 0);
            if (_folder != null) Render(window, Path.Combine(_folder, "Settings_theme_editor.png"));
            editor.SaveCommand.Execute(null);
            Check("saved custom theme persisted", SettingsJson().Contains("\"Snapshot check\"", StringComparison.Ordinal) && SettingsJson().Contains("#9C3D2E", StringComparison.Ordinal));
            editor.Activate(ThemeEntry.SystemId);
            themes.Custom.RemoveAll(t => t.Id == custom.Id);
            vm.Services.SaveSettings();
            editor.Refresh();
            await Settle();
            Check("built-in theme restored", ThemeManager.ActiveCustom == null && WindowColor() == Brush("App.WindowBackground") && WindowColor() != windowAfter);
        }

        // Status bar: a saved file is shown as a link with the exact path; Cancel only for cancellable operations.
        if (_folder != null)
        {
            string savedFile = Path.Combine(_folder, "Saved file ü 日本.gif");
            File.WriteAllBytes(savedFile, [0x47, 0x49, 0x46]);
            vm.Status.ShowSaved(savedFile, isFile: true);
            await Settle();
            var statusLink = window.FindName("StatusLink") as System.Windows.Documents.Hyperlink;
            string linkText = statusLink == null ? "" : string.Concat(statusLink.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text));
            Check("saved file shown as a clickable path in the status bar", statusLink is { IsEnabled: true } && linkText == new StatusLink(savedFile, true).Label
                  && vm.Status.FullText == L.T("Status.SavedTo") + savedFile && vm.RevealStatusLinkCommand.CanExecute(null), $"\"{linkText}\"");
            Render(window, Path.Combine(_folder, "status_saved_link.png"));
        }
        System.Windows.Controls.Button? CancelButton() => FindFirst<System.Windows.Controls.Button>(window, b => b.Content as string == L.T("Common.Cancel") && b.Command == vm.CancelOperationCommand);
        using (vm.Status.BeginOperation("Saving (not cancellable)…", canCancel: false))
        {
            await Settle();
            bool hidden = CancelButton() is { IsVisible: false };
            using (vm.Status.BeginOperation("Exporting (cancellable)…", canCancel: true))
            {
                await Settle();
                Check("Cancel appears only for operations that can be cancelled", hidden && CancelButton() is { IsVisible: true });
            }
        }
        vm.Status.Show(L.T("Status.Ready"));

        // Diagnostics: self-check and report preview work in the running program.
        var diagnostics = vm.Pages.OfType<DiagnosticsViewModel>().First();
        vm.SelectedPage = diagnostics;
        await Settle();
        await diagnostics.RunSelfCheckAsync();
        await diagnostics.PreviewReportAsync();
        diagnostics.SelectedTab = 5;
        await Settle();
        Check("Diagnostics self-check and report preview", diagnostics.HealthResults.Count > 10 && diagnostics.PreviewEntries.Count >= 7
              && !diagnostics.HealthResults.Any(r => r.Area == "Subsystems" && r.Severity == "Error"), $"{diagnostics.HealthResults.Count} checks, {diagnostics.PreviewEntries.Count} report files; {diagnostics.HealthSummary}");
        if (_folder != null) Render(window, Path.Combine(_folder, "Diagnostics_report_preview.png"));
        diagnostics.SelectedTab = 3;
        await Settle();
        if (_folder != null) Render(window, Path.Combine(_folder, "Diagnostics_self_check.png"));
        diagnostics.SelectedTab = 0;

        // Settings → Appearance: navigation layout and interface size apply immediately.
        window.WindowState = WindowState.Normal;
        window.Width = 1300;
        window.Height = 820;
        settingsVm.Navigation = NavigationLayout.Compact;
        await Settle();
        bool compactWide = !vm.IsNavExpanded;
        window.Width = 900;
        settingsVm.Navigation = NavigationLayout.Expanded;
        await Settle();
        bool expandedNarrow = vm.IsNavExpanded;
        settingsVm.Navigation = NavigationLayout.Automatic;
        await Settle();
        Check("navigation layout setting applies immediately", compactWide && expandedNarrow && !vm.IsNavExpanded,
              $"compact in a wide window: {compactWide}, names in a narrow window: {expandedNarrow}, automatic narrow: {!vm.IsNavExpanded}");
        settingsVm.UiScale = 1.25;
        await Settle();
        var scaled = window.RootGrid.LayoutTransform as ScaleTransform;
        if (_folder != null) Render(window, Path.Combine(_folder, "Settings_interface_125.png"));
        settingsVm.UiScale = 1.0;
        await Settle();
        Check("interface size setting scales the window immediately", scaled is { ScaleX: 1.25, ScaleY: 1.25 } && window.RootGrid.LayoutTransform == Transform.Identity,
              $"scale {scaled?.ScaleX.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}");
        window.Width = 1300;
        await Settle();

        // Compatibility banner: shown on a page whose data is not fully compatible, hidden otherwise.
        var appearances = vm.Pages.First(p => p is AppearancesViewModel);
        vm.SelectedPage = appearances;
        appearances.CompatibilityNotice = "appearances: Partially compatible — 12 records could not be validated (sample notice for this check).";
        await Settle();
        var banner = FindFirst<System.Windows.Controls.Border>(window, b => System.Windows.Automation.AutomationProperties.GetName(b) == L.T("Main.CompatibilityWarning"));
        Check("compatibility banner appears on affected pages", banner is { IsVisible: true });
        if (_folder != null) Render(window, Path.Combine(_folder, "compatibility_banner.png"));
        appearances.CompatibilityNotice = null;
        await Settle();
        Check("compatibility banner hides when data is fine", banner is { IsVisible: false });
        appearances.DependencyNotice = "Sample: a dependency is missing (notice for this check).";
        await Settle();
        var dependencyBanner = FindFirst<System.Windows.Controls.Border>(window, b => System.Windows.Automation.AutomationProperties.GetName(b) == L.T("Main.MissingDependency"));
        Check("dependency banner appears when a tool's dependency is missing", dependencyBanner is { IsVisible: true });
        appearances.DependencyNotice = null;
        await Settle();
        Check("dependency banner hides again", dependencyBanner is { IsVisible: false });

        // 9.0: Appearances → Wiki infobox builds TibiaWiki's {{Infobox Object}} for the selected object.
        if (appearances is AppearancesViewModel appearancesVm && await vm.Services.Data.EnsureAppearancesAsync() is not null)
        {
            await appearancesVm.RevealAsync(Core.Appearances.AppearanceKind.Object, 3366);
            appearancesVm.SelectedDetailTab = AppearancesViewModel.InfoboxTab;
            await Settle();
            var infobox = appearancesVm.Infobox;
            if (_folder != null) Render(window, Path.Combine(_folder, "Appearances_wiki_infobox.png"));
            Check("wiki infobox tab builds the Infobox Object markup", infobox.HasObject && infobox.Markup.StartsWith("{{Infobox Object|", StringComparison.Ordinal)
                  && infobox.Fields.Count > 10, $"{infobox.Fields.Count} parameters");
            appearancesVm.SelectedDetailTab = 0;
            await Settle();
        }

        // Item Catalog: a catalog of the client's own data (in a temporary folder, removed afterwards) is listed,
        // searched and explained record by record.
        if (vm.Pages.OfType<ItemCatalogViewModel>().FirstOrDefault() is { } catalogVm && await vm.Services.Data.EnsureAppearancesAsync() is { } db)
        {
            string catalogFolder = Path.Combine(Path.GetTempPath(), Core.Common.AppIdentity.Id + "-ui-snapshot", "catalog-" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                var catalog = Core.Items.ItemCatalogStore.Create(catalogFolder, "UI snapshot catalog");
                Core.Items.ItemCatalogStore.Commit(catalog, Core.Items.ItemImporters.FromClient(db, vm.Services.Data.ClientVersion, db.SourcePath));
                vm.SelectedPage = catalogVm;
                await catalogVm.OpenFolderAsync(catalogFolder);
                catalogVm.SearchText = "coin";
                await Settle();
                catalogVm.SelectedRow = catalogVm.VisibleRows.FirstOrDefault(r => r.Name == "gold coin") ?? (catalogVm.VisibleRows.Count > 0 ? catalogVm.VisibleRows[0] : null);
                await Settle();
                Check($"Item Catalog lists the client's objects ({catalog.Entries.Count:N0}) and explains a record",
                    catalogVm.TotalCount == catalog.Entries.Count && catalogVm.VisibleRows.Count > 0 && catalogVm.Details.Count > 0,
                    $"{catalogVm.TotalCount} listed, {catalogVm.VisibleRows.Count} found");
                if (_folder != null) Render(window, Path.Combine(_folder, "ItemCatalog_client.png"));
                catalogVm.SearchText = "";

                // Compare tab: the client's records against the catalog's settled names (identical: no differences).
                catalogVm.RightCatalog = catalogVm.Catalogs.FirstOrDefault(c => string.Equals(c.Folder, catalogFolder, StringComparison.OrdinalIgnoreCase));
                catalogVm.LeftChoice = catalogVm.LeftChoices.FirstOrDefault(c => c.SourceId != null);
                catalogVm.RightChoice = catalogVm.RightChoices.Count > 0 ? catalogVm.RightChoices[0] : null;
                await catalogVm.CompareCommand.ExecuteAsync(null);
                var tabs = FindFirst<Pages.ItemCatalogPage>(window, _ => true) is { } page ? FindFirst<System.Windows.Controls.TabControl>(page, _ => true) : null;
                var compareTab = tabs?.Items.OfType<System.Windows.Controls.TabItem>().FirstOrDefault(t => Equals(t.Header, L.T("Common.Compare")));
                if (compareTab != null) compareTab.IsSelected = true;
                await Settle();
                Check("Item Catalog compares a source with the settled names", catalogVm.ComparisonSummary != null && catalogVm.ComparisonRows.Count == 0 && compareTab is { IsSelected: true },
                    $"{catalogVm.ComparisonRows.Count} differences");
                if (_folder != null) Render(window, Path.Combine(_folder, "ItemCatalog_compare.png"));
                if (tabs != null) tabs.SelectedIndex = 0;

            }
            finally
            {
                try { Directory.Delete(catalogFolder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        // Responsive layout.
        window.WindowState = WindowState.Normal;
        window.Width = 900;
        window.Height = 640;
        await Settle();
        Check("narrow window collapses navigation to icons", !vm.IsNavExpanded, $"{window.ActualWidth:0} px");
        window.Width = 1300;
        await Settle();
        Check("wide window shows navigation labels", vm.IsNavExpanded, $"{window.ActualWidth:0} px");
        window.Width = 200;
        window.Height = 150;
        await Settle();
        Check("minimum size enforced", window.ActualWidth >= window.MinWidth && window.ActualHeight >= window.MinHeight,
              $"{window.ActualWidth:0}x{window.ActualHeight:0}, minimum {window.MinWidth:0}x{window.MinHeight:0}");
        vm.SelectedPage = vm.Pages.First(p => p is AboutViewModel);
        await Settle();
        string narrowCredits = CheckCredits(window, out _, out bool narrowOk);
        Check("Credits fit the smallest window", narrowOk, narrowCredits);
        if (_folder != null) Render(window, Path.Combine(_folder, "About_credits_smallest_window.png"));
        window.WindowState = WindowState.Maximized;
        await Settle();
        Check("maximize fills the work area", window.ActualWidth >= SystemParameters.WorkArea.Width - 16,
              $"{window.ActualWidth:0} of {SystemParameters.WorkArea.Width:0}");
        window.WindowState = WindowState.Normal;
        await Settle();
        Check("restore returns to normal size", window.ActualWidth < SystemParameters.WorkArea.Width - 16, $"{window.ActualWidth:0} px");

        // Client package switching (test server package may not be installed).
        vm.UseTestServer = true;
        await Settle();
        Check("switch to test server", vm.Services.Settings.UseTestServer && (vm.Data.AssetsAvailable || vm.Status.Kind == StatusKind.Warning), vm.Status.Message);
        vm.UseTestServer = false;
        await Settle();
        Check("switch back to live client", !vm.Services.Settings.UseTestServer && vm.Data.AssetsAvailable && vm.Status.Message.Contains(L.T("ClientData.LiveClient"), StringComparison.Ordinal), vm.Status.Message);

        // Placement persistence: the size and position are saved when the window closes.
        window.Left = 60;
        window.Top = 40;
        window.Width = 1234;
        window.Height = 777;
        await Settle();
        window.Close();
        var saved = vm.Services.SettingsStore.Load().Settings.MainWindow;
        Check("window placement saved on close", Math.Abs(saved.Width - 1234) < 1 && Math.Abs(saved.Height - 777) < 1 && Math.Abs(saved.Left - 60) < 1 && Math.Abs(saved.Top - 40) < 1 && !saved.Maximized,
              $"{saved.Left:0},{saved.Top:0} {saved.Width:0}x{saved.Height:0}");

        report.AppendLine($"Window checks failed: {failures}");
        report.AppendLine();
    }

    /// <summary>
    /// Scrolls About → Credits into view and checks it: exactly Tibia, CipSoft and TibiaWiki in that
    /// order with the specified addresses; every logo shown with its own aspect ratio; all entries
    /// the same size and inside the visible width.
    /// </summary>
    private static string CheckCredits(Window window, out ImageSource? cipSoftLogo, out bool ok)
    {
        (string Name, string Url)[] expected =
            [("Tibia", "http://tibia.com/"), ("CipSoft", "https://www.cipsoft.com/en/"), ("TibiaWiki", "https://tibia.fandom.com/wiki/Main_Page")];
        var tiles = FindAll<System.Windows.Controls.Button>(window, b => b.DataContext is CreditEntry).ToList();
        cipSoftLogo = null;
        ok = false;
        if (tiles.Count == 0) return "no credit entries found";
        tiles[^1].BringIntoView();
        window.UpdateLayout();
        var list = FindFirst<System.Windows.Controls.ItemsControl>(window, i => System.Windows.Automation.AutomationProperties.GetName(i) == L.T("About.Credits"));
        var entries = tiles.Select(t => (CreditEntry)t.DataContext).ToList();
        var logos = tiles.Select(t => FindFirst<System.Windows.Controls.Image>(t, i => i.IsVisible && i.Source != null)).ToList();
        bool namesAndLinks = entries.Select(e => (e.Name, e.Url)).SequenceEqual(expected);
        bool allLogos = logos.All(l => l is { ActualWidth: > 0, ActualHeight: > 0 });
        bool proportions = logos.All(l => l is { Source: BitmapSource bmp, ActualWidth: > 0, ActualHeight: > 0 } &&
                                          Math.Abs(l.ActualWidth / l.ActualHeight / ((double)bmp.PixelWidth / bmp.PixelHeight) - 1) < 0.02);
        bool sameSize = tiles.Select(t => (Math.Round(t.ActualWidth), Math.Round(t.ActualHeight))).Distinct().Count() == 1;
        bool inside = list != null && tiles.All(t => t.TranslatePoint(new Point(t.ActualWidth, 0), list).X <= list.ActualWidth + 0.5);
        cipSoftLogo = logos.Count > 1 ? logos[1]?.Source : null;
        ok = tiles.Count == 3 && namesAndLinks && allLogos && proportions && sameSize && inside;
        return $"{tiles.Count} entries: {string.Join(", ", entries.Select(e => e.Name))}; logos {logos.Count(l => l != null)}, " +
               $"proportions kept {proportions}, same size {sameSize} ({tiles[0].ActualWidth:0}×{tiles[0].ActualHeight:0}), inside {inside}";
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    private static bool? DarkTitleBar(Window window)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        return DwmGetWindowAttribute(hwnd, 20, out int value, sizeof(int)) == 0 ? value != 0 : null;
    }

    private static T? FindFirst<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match)) return match;
            if (FindFirst(child, predicate) is { } found) return found;
        }
        return null;
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match)) yield return match;
            foreach (var found in FindAll(child, predicate)) yield return found;
        }
    }

    private static async Task WaitForIdleAsync(PageViewModel page)
    {
        var deadline = DateTime.UtcNow + PageTimeout;
        // Let the navigation start its load, then wait until the page is no longer busy.
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(300);
        while (page.IsBusy && DateTime.UtcNow < deadline) await Task.Delay(100);
        // Lazy thumbnails and sprite previews load asynchronously after the list is shown.
        await Task.Delay(1200);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// Renders the dialogs whose layout depends on their content (8.5): the archive confirmation with
    /// File details closed and open, at its default and at its minimum width. The figures are a
    /// sample made here; the dialog is never shown modally and archives nothing.
    /// </summary>
    private static async Task RenderDialogsAsync(string folder, string themeName, StringBuilder report)
    {
        var files = new List<Core.Clients.ManifestFile>();
        for (int i = 0; i < 40; i++) files.Add(new($"assets/sprites-{i:000}.bmp.lzma", Core.Clients.ClientFileKinds.SpriteSheet, $"{i:x64}", 15_800_000 + i * 1000));
        files.Add(new("conf/clientoptions.json", Core.Clients.ClientFileKinds.Data, new string('c', 64), 48_213));
        var inventory = new Core.Clients.FolderInventory
        {
            Root = @"C:\Users\Example\AppData\Local\Tibia\packages\Tibia",
            Files = [], Directories = [], Problems = [],
            Exclusions =
            [
                new("characterdata/", "personal data: your characters' data", 214, 3_412_000),
                new("minimap/", "personal data: your explored map", 1_830, 96_400_000),
                new("log/", "personal data: the client's logs", 12, 880_000),
                new("client screenshots (<date>_<time>_<character>_<kind>.png, anywhere)", "personal data: screenshots, named after a character", 37, 41_200_000),
            ],
        };
        var scan = new Core.Clients.FolderScan { Inventory = inventory, Files = files, Problems = [], Fingerprint = "sample" };
        var preview = Core.Clients.ArchivePreview.Create(scan, Core.Clients.ManifestScopes.CompleteInstallation, Core.Clients.ManifestStorage.Shared, false,
            (sha, _) => sha.EndsWith('1'));
        var request = new ArchiveConfirmation
        {
            Client = "Live installation", Server = "Live server", Version = "15.33.c9e45e", Folder = inventory.Root, Preview = preview,
            DescribeChanges = _ => "Against Live installation 15.32.a1b2c3: 12 files changed, 3 added, +24.5 MB",
            Recalculate = (_, _, _) => Task.FromResult(preview),
        };
        // A 1366×768 screen at 125 % scaling, less the taskbar: 1093×576 device-independent units.
        var smallScreen = new Rect(0, 0, 1093, 576);
        foreach (var (name, width, expanded, small) in new[] { ("archive", 600.0, false, false), ("archive-details", 600.0, true, false), ("archive-narrow", 480.0, true, false),
                     ("archive-small-screen", 600.0, true, true) })
        {
            var dialog = Dialogs.ArchiveDialog.CreateForSnapshot(request);
            dialog.Width = width;
            if (small) Dialogs.DialogSizing.FitTo(dialog, smallScreen);
            dialog.SetDetailsExpanded(expanded);
            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.Left = -10000;   // rendered, never seen
            dialog.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(200);
            string file = Path.Combine(folder, $"dialog_{name}_{themeName}.png");
            Render(dialog, file);
            report.AppendLine($"{Path.GetFileName(file),-48} {dialog.ActualWidth:0}×{dialog.ActualHeight:0} DIP" +
                              (small ? $"  {(dialog.ActualHeight <= smallScreen.Height ? "PASS" : "FAIL")} fits a {smallScreen.Width:0}×{smallScreen.Height:0} work area" : ""));
            dialog.Close();
        }
    }

    /// <summary>
    /// The structured messages of 8.6 with real content: setup's archive question and summary for this
    /// PC's first usable default client (only read), at the default width, at the minimum width and on
    /// a small screen. Nothing is archived and no setting changes.
    /// </summary>
    private static async Task RenderReportsAsync(AppServices services, string folder, string themeName, StringBuilder report)
    {
        IReadOnlyList<(string Name, DialogReport Report)> reports;
        try
        {
            reports = await new ClientStartup(services).SnapshotReportsAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            report.AppendLine("setup dialogs: not rendered: " + ex.Message);
            return;
        }
        if (reports.Count == 0) report.AppendLine("setup dialogs: not rendered (no usable default client on this PC)");
        var smallScreen = new Rect(0, 0, 1093, 576);
        foreach (var (name, content) in reports)
            foreach (var (variant, width, small) in new[] { ("", 620.0, false), ("-narrow", 440.0, false), ("-small-screen", 620.0, true) })
            {
                var dialog = Dialogs.ReportDialog.CreateForSnapshot(content);
                dialog.Width = width;
                if (small) Dialogs.DialogSizing.FitTo(dialog, smallScreen);
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = -10000;   // rendered, never seen
                dialog.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                await Task.Delay(200);
                string file = Path.Combine(folder, $"dialog_{name}{variant}_{themeName}.png");
                Render(dialog, file);
                report.AppendLine($"{Path.GetFileName(file),-48} {dialog.ActualWidth:0}×{dialog.ActualHeight:0} DIP" +
                                  (small ? $"  {(dialog.ActualHeight <= smallScreen.Height ? "PASS" : "FAIL")} fits a {smallScreen.Width:0}×{smallScreen.Height:0} work area" : ""));
                dialog.Close();
            }
    }

    /// <summary>The File Metadata page with sample files made here (an image, a workbook, a drawing, an archive, the program itself): two files selected, then one.</summary>
    private static async Task RenderMetadataPageAsync(MainWindow window, MainViewModel vm, string folder, string themeName, StringBuilder report)
    {
        if (vm.Pages.OfType<MetadataViewModel>().FirstOrDefault() is not { } page) return;
        string samples = Path.Combine(Path.GetTempPath(), "TibiaFoundry-ui-snapshot-metadata");
        if (page.Files.Count == 0)
        {
            Directory.CreateDirectory(samples);
            string png = Path.Combine(samples, "Rotworm.png");
            using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.SaddleBrown, 32, 32)) image.Write(png, ImageMagick.MagickFormat.Png);
            string jpg = Path.Combine(samples, "Thais at dawn.jpg");
            using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.SteelBlue, 64, 48)) image.Write(jpg, ImageMagick.MagickFormat.Jpeg);
            var table = new Core.FileOps.ReportTable("Items", ["ID", "Name"]);
            table.Rows.Add(["3031", "gold coin"]);
            string xlsx = Path.Combine(samples, "Item list.xlsx");
            Core.FileOps.XlsxWriter.Write(xlsx, [("Items", table)]);
            File.WriteAllText(Path.Combine(samples, "Gold coin.svg"), """<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32"><title>Gold coin</title><circle cx="16" cy="16" r="12" fill="#e0b000"/></svg>""");
            var engine = new Core.FileMetadata.MetadataEngine();
            engine.Save(engine.Load(jpg), [new("title", "Thais at dawn"), new("keywords", "tibia; thais; screenshot"), new("authors", "RockyLaRo"), new("copyright", "Tibia © CipSoft GmbH")], new());
            engine.Save(engine.Load(png), [new("title", "Rotworm"), new("keywords", "creature")], new());
            await page.AddPathsAsync([samples, Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "TibiaFoundry.exe")]);
        }
        vm.SelectedPage = page;
        await WaitForIdleAsync(page);
        var grid = FindFirst<DataGrid>(window, g => g.Name == "FileList");
        if (grid == null) { report.AppendLine("metadata page: list not found"); return; }
        foreach (var (name, count) in new[] { ("metadata-two-files", 2), ("metadata-one-file", 1) })
        {
            grid.SelectedItems.Clear();
            foreach (var item in page.FilesView.Cast<MetadataFileItem>().Where(f => f.Format.Contains("image", StringComparison.OrdinalIgnoreCase) || f.Format.Contains("JPEG", StringComparison.Ordinal)).Take(count))
                grid.SelectedItems.Add(item);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(300);
            string file = Path.Combine(folder, $"{name}_{themeName}.png");
            Render(window, file);
            report.AppendLine($"{Path.GetFileName(file),-48} {page.Files.Count} files, {page.Selection.Count} selected, {page.Groups.Sum(g => g.Rows.Count)} fields shown");
        }
    }

    private static void Render(Window window, string file)
    {
        if (window.Content is not FrameworkElement root) return;
        root.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        int width = (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY);
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // Paint the window background first: the root element itself is transparent.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            // Absolute viewbox: the brush would otherwise stretch the content's bounds and drop its outer margins.
            dc.DrawRectangle(new VisualBrush(root) { Viewbox = new Rect(0, 0, root.ActualWidth, root.ActualHeight), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None },
                null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    /// <summary>Collects WPF data-binding errors (normally only visible in a debugger's output).</summary>
    private sealed class BindingErrorListener : TraceListener
    {
        private readonly List<string> _errors = new();
        private readonly StringBuilder _pending = new();

        public int Count { get { lock (_errors) return _errors.Count; } }

        public IReadOnlyList<string> Since(int index)
        {
            lock (_errors) return _errors.Skip(index).ToList();
        }

        public override void Write(string? message) { lock (_errors) _pending.Append(message); }

        public override void WriteLine(string? message)
        {
            lock (_errors)
            {
                _pending.Append(message);
                _errors.Add(_pending.ToString());
                _pending.Clear();
            }
        }
    }
}
