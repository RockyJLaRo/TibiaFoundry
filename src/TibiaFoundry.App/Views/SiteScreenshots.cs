using System.Collections;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Themes;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Localization;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.App.Views;

/// <summary>
/// Website screenshots (<c>--site-screenshots &lt;folder&gt; [--language code] [--site-pages a,b] [--site-themes dark,light]</c>):
/// with the normal settings and data (the website uses a portable demo copy), sets each page to the state the website
/// shows, and captures the whole window with its title bar, in the light and the dark theme, as
/// <c>&lt;page&gt;-light.png</c> / <c>&lt;page&gt;-dark.png</c>. Text that shows a path inside the Windows
/// user's folder is blurred. Nothing is saved: themes are applied without being stored.
/// <para>Besides the pictures it writes <c>site-screenshots.txt</c> (readable log) and <c>site-screenshots.json</c>
/// (language, every shot it knows, and the result of each capture), which <c>website/screenshots/screenshots.py</c>
/// reads. The shot list below is the program's side of the website's screenshot inventory: a new website screenshot
/// is a new <see cref="Shot"/> here plus its id in <c>website/build_site.py</c>.</para>
/// </summary>
public static class SiteScreenshots
{
    /// <summary>The window's visible size in the captures (the website's image size).</summary>
    private const int FrameWidth = 1586, FrameHeight = 893;

    private sealed record Shot(string Name, Type Page, Func<dynamic, Task>? Prepare = null);

    /// <summary>Process exit code: 0 when every requested capture was written, 1 otherwise.</summary>
    public static int ExitCode { get; private set; }

    public static async Task RunAsync(MainWindow window, MainViewModel vm, string folder, IReadOnlyCollection<string>? pages = null, IReadOnlyCollection<string>? themes = null)
    {
        Directory.CreateDirectory(folder);
        var log = new List<string> { $"Language: {L.Language.Code}" };
        var results = new List<Dictionary<string, object?>>();
        window.WindowState = WindowState.Normal;
        window.Left = 40;
        window.Top = 40;
        await FitFrameAsync(window);

        var shots = new List<Shot>
        {
            new("home", typeof(HomeViewModel)),
            new("asset-library", typeof(AssetLibraryViewModel), async p =>
            {
                p.Tab = 0;
                p.SearchText = "dragon";
                await Settle(p);
                p.SelectedTile = Pick((IEnumerable)p.Tiles, t => (string)t.Asset.Name == "Dragon Lord") ?? First((IEnumerable)p.Tiles);
            }),
            new("duplicates", typeof(AssetLibraryViewModel), async p =>
            {
                p.Tab = 1;
                await ((IAsyncRelayCommand)p.FindDuplicatesCommand).ExecuteAsync(null);
            }),
            new("content-studio", typeof(ContentStudioViewModel), async p =>
            {
                p.Tab = 0;
                var projects = ((IEnumerable)p.Projects).Cast<object>().ToList();
                p.SelectedProject = (dynamic?)(projects.Count > 1 ? projects[1] : projects.FirstOrDefault());
                await Settle(p);
                p.SelectedStep = First((IEnumerable)p.Steps);
            }),
            new("profiles", typeof(ContentStudioViewModel), p =>
            {
                p.Tab = 1;
                // By id: the list is sorted by the profiles' translated names, so "the first" differs between languages.
                p.SelectedProfile = Pick((IEnumerable)p.Profiles, x => (string)x.Id == "article") ?? First((IEnumerable)p.Profiles);
                return Task.CompletedTask;
            }),
            new("appearances", typeof(AppearancesViewModel), async p =>
            {
                p.Kind = Core.Appearances.AppearanceKind.Outfit;
                await Settle(p);
                p.SearchText = "131";
                await ((IAsyncRelayCommand)p.SearchCommand).ExecuteAsync(null);
                p.SelectedItem = Pick((IEnumerable)p.VisibleItems, i => (uint)i.Appearance.Id == 131) ?? p.SelectedItem;
                p.SelectedDetailTab = 4;
                await Settle(p);
                dynamic o = p.Outfitter;
                o.Addon1 = true;
                o.Addon2 = true;
                o.UseMount = true;
                o.MountId = 1599;
                o.Mounted = true;
            }),
            // 9.0: Appearances → Wiki infobox, for the object the website's infobox sample shows (Magic Plate Armor).
            new("infobox", typeof(AppearancesViewModel), async p =>
            {
                p.Kind = Core.Appearances.AppearanceKind.Object;
                await Settle(p);
                p.SearchText = "3366";
                await ((IAsyncRelayCommand)p.SearchCommand).ExecuteAsync(null);
                await Settle(p);
                p.SelectedItem = Pick((IEnumerable)p.VisibleItems, i => (uint)i.Appearance.Id == 3366) ?? p.SelectedItem;
                p.SelectedDetailTab = AppearancesViewModel.InfoboxTab;
            }),
            new("extract-sprites", typeof(SpriteExtractorViewModel), async p =>
            {
                await Settle(p);
                if (((IList)p.Queue).Count == 0)
                {
                    var rows = ((IEnumerable)p.VisibleSheets).Cast<object>().Where(r => (int)((dynamic)r).FirstId >= 3383).ToList();
                    p.SelectedAvailable = (dynamic)Cast(rows, ((IEnumerable)p.VisibleSheets).GetType().GetGenericArguments().FirstOrDefault() ?? rows.FirstOrDefault()?.GetType());
                    ((IRelayCommand)p.AddSelectedCommand).Execute(null);
                }
            }),
            new("static-data", typeof(StaticDataViewModel), async p =>
            {
                p.RenderOutfits = true;
                p.FilterText = "dragon";
                await Settle(p);
                p.SelectedItem = Pick((IEnumerable)p.VisibleItems, i => (string)i.Id == "39") ?? First((IEnumerable)p.VisibleItems);
            }),
            new("item-catalog", typeof(ItemCatalogViewModel), async p =>
            {
                await Settle(p);
                p.SearchText = "sword";
                await Settle(p);
                p.SelectedRow = Pick((IEnumerable)p.VisibleRows, r => (string)r.Id == "661") ?? First((IEnumerable)p.VisibleRows);
            }),
            new("compare", typeof(CompareViewModel), async p =>
            {
                string? old = Environment.GetEnvironmentVariable("TF_SITE_COMPARE_OLD");
                if (old != null && File.Exists(old))
                {
                    p.OldFile = old;
                    await ((IAsyncRelayCommand)p.CompareFilesCommand).ExecuteAsync(null);
                    await Settle(p);
                    p.SelectedCategory = First((IEnumerable)p.Categories);
                    await Settle(p);
                    p.SelectedEntry = Pick((IEnumerable)p.Entries, e => (string)e.Key == "27697") ?? First((IEnumerable)p.Entries);
                }
            }),
            new("map-tools", typeof(MapToolsViewModel), async p => { await Settle(p); p.SelectedItem = First((IEnumerable)p.VisibleItems); }),
            new("house-images", typeof(HouseImagesViewModel), async p => { await Settle(p); p.SelectedItem = First((IEnumerable)p.VisibleItems); }),
            new("sounds", typeof(SoundsViewModel), async p => { await Settle(p); p.SelectedItem = First((IEnumerable)p.VisibleItems); }),
            new("assets-editor", typeof(AssetsEditorViewModel), async p =>
            {
                if (((IList)p.Items).Count == 0) await ((IAsyncRelayCommand)p.OpenClientCommand).ExecuteAsync(null);
                await Settle(p);
                p.Selected = Pick((IEnumerable)p.Items, i => (uint)i.Id == 100) ?? First((IEnumerable)p.Items);
            }),
            new("rcc-editor", typeof(RccEditorViewModel), async p =>
            {
                if (((IList)p.Entries).Count == 0) await ((IAsyncRelayCommand)p.OpenClientFileCommand).ExecuteAsync(null);
                await Settle(p);
                p.Search = "skin/classic";
                await Settle(p);
                p.Selected = Pick((IEnumerable)p.Entries, e => ((string)e.Path).EndsWith("button-gold-up.png", StringComparison.Ordinal)) ?? First((IEnumerable)p.Entries);
            }),
            new("clients", typeof(ClientsViewModel)),
            new("settings", typeof(SettingsViewModel), p => SelectSettingsCategory(p, "General")),
            new("settings-attribution", typeof(SettingsViewModel), p => SelectSettingsCategory(p, "Attribution")),
        };

        var unknown = pages?.Where(n => !shots.Any(s => s.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList() ?? [];
        foreach (string name in unknown) log.Add($"{name}  FAILED: no such screenshot (known: {string.Join(", ", shots.Select(s => s.Name))})");
        var selected = shots.Where(s => pages == null || pages.Count == 0 || pages.Any(n => n.Equals(s.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        int failures = unknown.Count;
        // The Settings page shows the theme preference as "Active theme"; while a theme is captured the preference
        // says the same in memory (restored below, so nothing is saved) – otherwise the dark pictures read "Light".
        var settings = vm.Services.Settings;
        var (savedTheme, savedCustom) = (settings.Theme, settings.Themes.ActiveCustomTheme);
        // Visiting the pages makes them "recently used"; Home must show the same list in every picture.
        var savedRecent = settings.RecentPages.ToList();
        // Animated previews show their first frame, so a picture never depends on when it was taken.
        bool savedAnimations = Controls.AnimatedSprite.AnimationsEnabled;
        Controls.AnimatedSprite.AnimationsEnabled = false;
        try
        {
            foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
            {
                string themeName = theme == ThemePreference.Dark ? "dark" : "light";
                if (themes != null && themes.Count > 0 && !themes.Contains(themeName, StringComparer.OrdinalIgnoreCase)) continue;
                settings.Theme = theme;
                settings.Themes.ActiveCustomTheme = null;
                ThemeManager.Apply(theme);
                if (vm.Pages.OfType<SettingsViewModel>().FirstOrDefault() is { } settingsPage) settingsPage.Themes.Refresh();
                foreach (var shot in selected)
                {
                    var started = DateTime.UtcNow;
                    var result = new Dictionary<string, object?> { ["name"] = shot.Name, ["page"] = shot.Page.Name, ["theme"] = themeName, ["file"] = $"{shot.Name}-{themeName}.png" };
                    results.Add(result);
                    try
                    {
                        var page = vm.Pages.First(p => p.GetType() == shot.Page);
                        settings.RecentPages.Clear();
                        settings.RecentPages.AddRange(savedRecent);
                        if (!ReferenceEquals(vm.SelectedPage, page)) vm.SelectedPage = page;
                        await Settle(page);
                        if (shot.Prepare != null) await shot.Prepare(page);
                        await Settle(page);
                        ScrollNavigation(window, page);
                        ((dynamic)vm).ClientChange = null; // the "not archived yet" reminder
                        vm.Status.Show(L.T("Status.Ready"));
                        await Settle(page);
                        string file = Path.Combine(folder, $"{shot.Name}-{themeName}.png");
                        int blurred = Capture(window, file);
                        log.Add($"{shot.Name}-{themeName}.png  blurred {blurred}");
                        result["status"] = "ok";
                        result["blurred"] = blurred;
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        log.Add($"{shot.Name}-{themeName}.png  FAILED: {ex.Message}");
                        AppLog.Error($"Site screenshot {shot.Name} failed.", ex);
                        result["status"] = "failed";
                        result["error"] = ex.GetType().Name + ": " + ex.Message;
                    }
                    result["seconds"] = Math.Round((DateTime.UtcNow - started).TotalSeconds, 1);
                }
            }
        }
        finally
        {
            settings.Theme = savedTheme;
            settings.Themes.ActiveCustomTheme = savedCustom;
            settings.RecentPages.Clear();
            settings.RecentPages.AddRange(savedRecent);
            Controls.AnimatedSprite.AnimationsEnabled = savedAnimations;
        }
        File.WriteAllLines(Path.Combine(folder, "site-screenshots.txt"), log);
        var summary = new Dictionary<string, object?>
        {
            ["app"] = Core.Common.AppIdentity.NameAndVersion,
            ["language"] = L.Language.Code,
            ["languages"] = Languages.All.Select(l => l.Code).ToList(),
            ["frame"] = new[] { FrameWidth, FrameHeight },
            ["available"] = shots.Select(s => new Dictionary<string, string> { ["name"] = s.Name, ["page"] = s.Page.Name }).ToList(),
            ["unknown"] = unknown,
            ["results"] = results,
        };
        File.WriteAllText(Path.Combine(folder, "site-screenshots.json"),
            System.Text.Json.JsonSerializer.Serialize(summary, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        ExitCode = failures == 0 ? 0 : 1;
    }

    /// <summary>A typed array of the items (for properties of type IReadOnlyList&lt;T&gt;).</summary>
    private static object Cast(List<object> items, Type? type)
    {
        var array = Array.CreateInstance(type ?? typeof(object), items.Count);
        for (int i = 0; i < items.Count; i++) array.SetValue(items[i], i);
        return array;
    }

    private static dynamic? First(IEnumerable items) => items.Cast<object>().FirstOrDefault();

    /// <summary>
    /// Settings scrolls a category to the top when it is selected, measured on the layout of that moment. Right after
    /// a theme change the sections are still being laid out, so the category is selected again once they are settled.
    /// </summary>
    private static async Task SelectSettingsCategory(dynamic page, string category)
    {
        page.SelectedCategory = category;
        await Settle(page);
        page.SelectedCategory = category == "General" ? "Advanced" : "General";
        page.SelectedCategory = category;
    }

    private static dynamic? Pick(IEnumerable items, Func<dynamic, bool> match)
    {
        foreach (object item in items)
        {
            try { if (match(item)) return item; } catch (Exception) { /* a different item type */ }
        }
        return null;
    }

    private static async Task Settle(object page)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(400);
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (page is PageViewModel { IsBusy: true } && DateTime.UtcNow < deadline) await Task.Delay(100);
        await Task.Delay(900);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Scrolls the navigation so the open page is in view with its group heading.</summary>
    private static void ScrollNavigation(MainWindow window, PageViewModel page)
    {
        if (window.FindName("Nav") is not ListBox nav || nav.ItemContainerGenerator.ContainerFromItem(page) is not FrameworkElement item) return;
        var scroll = FindChild<ScrollViewer>(nav);
        if (scroll == null) return;
        // As in a normal session: the list stays at the top unless the page is further down, then it is scrolled to the end.
        scroll.ScrollToTop();
        window.UpdateLayout();
        double top = item.TransformToAncestor(scroll).Transform(new Point(0, 0)).Y;
        if (top + item.ActualHeight > scroll.ActualHeight) scroll.ScrollToEnd(); // ActualHeight: the list may scroll by items
        window.UpdateLayout();
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            if (FindChild<T>(child) is { } found) return found;
        }
        return null;
    }

    // ------------------------------------------------------------------ window capture

    private static async Task FitFrameAsync(Window window)
    {
        window.Width = 1600;
        window.Height = 900;
        for (int i = 0; i < 3; i++)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(200);
            var frame = FrameBounds(window);
            double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
            int dw = FrameWidth - (frame.Right - frame.Left), dh = FrameHeight - (frame.Bottom - frame.Top);
            if (dw == 0 && dh == 0) return;
            window.Width += dw / scale;
            window.Height += dh / scale;
        }
    }

    /// <summary>Captures the visible window (title bar included), blurs user paths and saves a PNG. Returns how many areas were blurred.</summary>
    private static int Capture(Window window, string file)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        GetWindowRect(hwnd, out var rect);
        var frame = FrameBounds(window);
        int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
        IntPtr screen = GetDC(IntPtr.Zero), dc = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, w, h);
        IntPtr old = SelectObject(dc, bmp);
        try
        {
            PrintWindow(hwnd, dc, 2 /* PW_RENDERFULLCONTENT */);
            SelectObject(dc, old);
            var full = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var crop = new CroppedBitmap(full, new Int32Rect(frame.Left - rect.Left, frame.Top - rect.Top, frame.Right - frame.Left, frame.Bottom - frame.Top));
            var image = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
            int stride = image.PixelWidth * 4;
            var pixels = new byte[stride * image.PixelHeight];
            image.CopyPixels(pixels, stride, 0);
            var areas = UserPathAreas(window, frame);
            foreach (var area in areas) Blur(pixels, image.PixelWidth, image.PixelHeight, area);
            var result = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(result));
            using var stream = File.Create(file);
            encoder.Save(stream);
            return areas.Count;
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(dc);
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>Areas (in capture pixels) of visible texts that contain a path in the Windows user's folder.</summary>
    private static List<Int32Rect> UserPathAreas(Window window, RECT frame)
    {
        string marker = @"\Users\" + Environment.UserName;
        var result = new List<Int32Rect>();
        void Visit(DependencyObject node)
        {
            if (node is UIElement { IsVisible: false }) return;
            string? text = node switch { TextBox t => t.Text, TextBlock b => b.Text, _ => null };
            if (text != null && text.Contains(marker, StringComparison.OrdinalIgnoreCase) && node is FrameworkElement e && e.ActualWidth > 0)
            {
                var a = e.PointToScreen(new Point(0, 0));
                var b = e.PointToScreen(new Point(e.ActualWidth, e.ActualHeight));
                int x = (int)a.X - frame.Left - 2, y = (int)a.Y - frame.Top - 2;
                result.Add(new Int32Rect(x, y, (int)(b.X - a.X) + 4, (int)(b.Y - a.Y) + 4));
                return;
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Visit(VisualTreeHelper.GetChild(node, i));
        }
        Visit(window);
        return result;
    }

    /// <summary>A strong box blur (three passes) over one area of a BGRA image.</summary>
    private static void Blur(byte[] px, int width, int height, Int32Rect area)
    {
        int x0 = Math.Max(0, area.X), y0 = Math.Max(0, area.Y), x1 = Math.Min(width, area.X + area.Width), y1 = Math.Min(height, area.Y + area.Height);
        if (x1 <= x0 || y1 <= y0) return;
        const int r = 6;
        var tmp = new byte[px.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            Array.Copy(px, tmp, px.Length);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int sb = 0, sg = 0, sr = 0, n = 0;
                    for (int yy = Math.Max(y0, y - r); yy <= Math.Min(y1 - 1, y + r); yy += 2)
                        for (int xx = Math.Max(x0, x - r); xx <= Math.Min(x1 - 1, x + r); xx += 2)
                        {
                            int i = (yy * width + xx) * 4;
                            sb += tmp[i]; sg += tmp[i + 1]; sr += tmp[i + 2]; n++;
                        }
                    int o = (y * width + x) * 4;
                    px[o] = (byte)(sb / n); px[o + 1] = (byte)(sg / n); px[o + 2] = (byte)(sr / n);
                }
        }
    }

    private static RECT FrameBounds(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (DwmGetWindowAttribute(hwnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out RECT r, Marshal.SizeOf<RECT>()) != 0) GetWindowRect(hwnd, out r);
        return r;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
}
