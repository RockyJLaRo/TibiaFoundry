using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Items;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Wiki;

// Usage: ImportCheck <work folder (new)> <Tibia Foundry build folder>
// Repeats on a portable copy what failed in 7.0.0 (2026-10-15): on the Item Catalog page, "TibiaWiki item IDs" with no
// catalog yet, the catalog named as suggested, the list downloaded from the live TibiaWiki, the import confirmed.
internal static class Program
{
    private static readonly StringBuilder Report = new();
    private static int _failures;
    private static Process? _app;

    private static void Note(string s)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {s}";
        Console.WriteLine(line);
        Report.AppendLine(line);
    }

    private static void Check(bool ok, string what)
    {
        Note((ok ? "PASS " : "FAIL ") + what);
        if (!ok) _failures++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        string work = Path.GetFullPath(args[0]), appBin = args[1];
        try
        {
            Run(work, appBin);
        }
        catch (Exception ex)
        {
            Check(false, "the check stopped: " + ex);
        }
        finally
        {
            try { if (_app is { HasExited: false }) { _app.CloseMainWindow(); if (!_app.WaitForExit(15000)) _app.Kill(); } } catch { }
            Note($"Result: {(_failures == 0 ? "all checks passed" : $"{_failures} check(s) failed")}.");
            if (Directory.Exists(work)) File.WriteAllText(Path.Combine(work, "import_check_report.txt"), Report.ToString());
        }
        return _failures == 0 ? 0 : 1;
    }

    private static void Run(string work, string appBin)
    {
        if (Directory.Exists(work)) throw new InvalidOperationException("The work folder must be new: " + work);
        Directory.CreateDirectory(work);

        // ------------------------------------------------------------ setup: a portable copy, outputs in the work folder
        string app = Path.Combine(work, "app");
        CopyDir(appBin, app);
        File.WriteAllText(Path.Combine(app, "TibiaFoundry.portable"), "");
        var settings = new AppSettings { OutputFolder = Path.Combine(work, "out") };
        settings.Normalize();
        string data = Path.Combine(app, "TibiaFoundry-Data");
        Directory.CreateDirectory(data);
        new SettingsStore(Path.Combine(data, "settings.json")).Save(settings);
        string catalogs = settings.OutputFolderFor(OutputKind.ItemCatalogs);

        _app = Process.Start(new ProcessStartInfo(Path.Combine(app, "TibiaFoundry.exe")) { WorkingDirectory = app, UseShellExecute = false })!;
        var main = WaitFor(() => Window("Tibia Foundry"), "the main window", 60);
        Check(true, $"Tibia Foundry {FileVersionInfo.GetVersionInfo(Path.Combine(app, "TibiaFoundry.exe")).ProductVersion} started (portable copy)");
        Thread.Sleep(1500);

        // ------------------------------------------------------------ the steps of the failed import
        var nav = WaitFor(() => main.FindFirst(TreeScope.Descendants, And(ControlType.List, "Navigation")), "the navigation list");
        var container = (ItemContainerPattern)nav.GetCurrentPattern(ItemContainerPattern.Pattern);
        AutomationElement? item = null;
        for (AutomationElement? li = container.FindItemByProperty(null, null, null); li != null && item == null; li = container.FindItemByProperty(li, null, null))
        {
            if (li.TryGetCurrentPattern(VirtualizedItemPattern.Pattern, out var v)) ((VirtualizedItemPattern)v).Realize();
            string label = li.Current.Name + " " + string.Join(" ", li.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)).Cast<AutomationElement>().Select(t => t.Current.Name));
            if (label.Contains("Item Catalog", StringComparison.Ordinal) || label.Contains("ItemCatalogViewModel", StringComparison.Ordinal)) item = li;
        }
        if (item == null) throw new InvalidOperationException("Item Catalog is not in the navigation.");
        ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Invoke(Button(main, "TibiaWiki item IDs"));

        var prompt = WaitFor(() => Window("New item catalog"), "the prompt for the catalog's name", 20);
        var input = WaitFor(() => prompt.FindFirst(TreeScope.Descendants, And(ControlType.Edit, "Input")), "the name box");
        string name = ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
        Check(name.Length > 0, $"no catalog yet: the program asks for a name and suggests \"{name}\"");
        Invoke(Button(prompt, "OK"));
        WaitUntil(() => Window("New item catalog") == null, "the prompt to close", 20);

        // The download: the status bar shows the progress until the preview (or an error) appears.
        var watch = Stopwatch.StartNew();
        var seen = new List<string>();
        AutomationElement? answer = null;
        while (watch.Elapsed < TimeSpan.FromMinutes(10))
        {
            answer = Dialog();
            if (answer != null) break;
            try
            {
                string status = main.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "StatusText"))?.Current.Name ?? "";
                string count = main.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)).Cast<AutomationElement>()
                    .Select(t => t.Current.Name).FirstOrDefault(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d[\d,.\s]* / \d")) ?? "";
                string shown = $"{watch.Elapsed.TotalSeconds,3:0} s: {status} [{count}]";
                if (status.Length > 0 && (seen.Count == 0 || seen[^1][7..] != shown[7..])) seen.Add(shown);
            }
            catch (ElementNotAvailableException) { }
            Thread.Sleep(400);
        }
        if (answer == null) throw new TimeoutException("No preview and no message within 10 minutes.");
        var progress = seen.Where(s => s.Contains("item articles", StringComparison.Ordinal)).ToList();
        foreach (string s in progress.Take(3).Concat(progress.Count > 6 ? ["…"] : []).Concat(progress.Skip(Math.Max(3, progress.Count - 3)))) Note("  status " + s);
        Check(progress.Count >= 3, $"the download's progress was shown in the status bar ({progress.Count} different states)");

        string text = AllText(answer);
        bool preview = answer.Current.Name == "Importing TibiaWiki's item IDs" && ButtonNamed(answer, "Import") != null;
        Check(preview, $"after {watch.Elapsed.TotalSeconds:0} s the import preview appeared (\"{answer.Current.Name}\"): {First(text.Replace(Environment.NewLine, " "), 400)}");
        if (!preview) return;
        Invoke(Button(answer, "Import"));

        // ------------------------------------------------------------ the result, read from disk
        var catalog = WaitFor(() =>
        {
            if (!Directory.Exists(catalogs)) return null;
            foreach (string dir in Directory.GetDirectories(catalogs))
            {
                try
                {
                    var c = ItemCatalogStore.Load(dir);
                    if (c.Sources.Any(s => s.Kind == SourceKind.TibiaWiki)) return c;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException) { }
            }
            return null;
        }, "the catalog with TibiaWiki's records", 120);
        var wiki = catalog.Sources.First(s => s.Kind == SourceKind.TibiaWiki);
        int names = catalog.Entries.Count(e => e.Observations.Any(o => o.SourceId == wiki.Id));
        Check(catalog.Name == name && wiki.RecordCount >= WikiItemIdTable.MinimumExpectedIds && names == wiki.RecordCount,
            $"catalog \"{catalog.Name}\" holds the TibiaWiki source: {wiki.RecordCount:N0} records, {names:N0} identifiers named by it");
        string cache = Path.Combine(data, "cache", WikiItemIdTable.CacheFileName);
        int? cachedSource = null;
        if (File.Exists(cache))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(cache));
            cachedSource = doc.RootElement.GetProperty("Source").GetInt32();
        }
        Check(cachedSource == (int)WikiTableSource.Articles, $"the list was cached, read from the item articles ({cache.Replace(work, "<work>")})");
        WaitUntil(() => (main.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "StatusText"))?.Current.Name ?? "").Contains("Imported 1 source", StringComparison.Ordinal),
            "the status \"Imported 1 source(s)\"", 30);
        Check(true, "the status bar says: " + main.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "StatusText"))!.Current.Name);

        // ------------------------------------------------------------ close
        ((WindowPattern)main.GetCurrentPattern(WindowPattern.Pattern)).Close();
        Check(_app.WaitForExit(20000), "Tibia Foundry closed");
        var logs = Directory.GetFiles(Path.Combine(data, "logs"), "*.log");
        var lines = logs.SelectMany(File.ReadAllLines).ToList();
        foreach (string l in lines.Where(l => l.Contains("TibiaWiki", StringComparison.Ordinal) || l.Contains("Item Catalog", StringComparison.Ordinal))) Note("  log: " + l);
        foreach (string l in lines.Where(l => l.Contains("[Warning", StringComparison.Ordinal))) Note("  log warning: " + l);
        var errors = lines.Where(l => l.Contains("[Error", StringComparison.Ordinal) || l.Contains("[Critical", StringComparison.Ordinal) || l.Contains("Unhandled", StringComparison.Ordinal)).ToList();
        foreach (string l in errors) Note("  log error: " + l);
        Check(errors.Count == 0, $"the log has no errors ({logs.Length} log file(s), {lines.Count} lines)");
    }

    // ------------------------------------------------------------ UI Automation helpers

    private static Condition And(ControlType type, string name) =>
        new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, type), new PropertyCondition(AutomationElement.NameProperty, name));

    private static IEnumerable<AutomationElement> AppWindows() =>
        AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, _app!.Id)).Cast<AutomationElement>();

    private static AutomationElement? Window(string name)
    {
        foreach (var w in AppWindows())
        {
            if (w.Current.Name == name) return w;
            var inner = w.FindFirst(TreeScope.Children, And(ControlType.Window, name));
            if (inner != null) return inner;
        }
        return null;
    }

    /// <summary>A window of the program other than the main window (a prompt, a preview or a message).</summary>
    private static AutomationElement? Dialog()
    {
        foreach (var w in AppWindows())
        {
            if (w.Current.Name != "Tibia Foundry") return w;
            if (w.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)) is { } inner) return inner;
        }
        return null;
    }

    private static AutomationElement? ButtonNamed(AutomationElement root, string name) => root.FindFirst(TreeScope.Descendants, And(ControlType.Button, name));

    private static AutomationElement Button(AutomationElement root, string name) => WaitFor(() => ButtonNamed(root, name), "the button " + name, 20);

    private static string AllText(AutomationElement root) =>
        string.Join(" ", root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)).Cast<AutomationElement>().Select(t => t.Current.Name)) + " " +
        string.Join(" ", root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)).Cast<AutomationElement>()
            .Select(e => e.TryGetCurrentPattern(ValuePattern.Pattern, out var p) ? ((ValuePattern)p).Current.Value : ""));

    private static void Invoke(AutomationElement e)
    {
        // A button that opens a modal dialog may not return from Invoke until the dialog closes.
        var t = Task.Run(() => ((InvokePattern)e.GetCurrentPattern(InvokePattern.Pattern)).Invoke());
        t.Wait(TimeSpan.FromSeconds(3));
    }

    private static T WaitFor<T>(Func<T?> find, string what, int seconds = 30) where T : class
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            try
            {
                if (find() is { } found) return found;
            }
            catch (ElementNotAvailableException) { }
            catch (IOException) { }
            Thread.Sleep(150);
        }
        throw new TimeoutException($"Waited {seconds} s for {what}.");
    }

    private static void WaitUntil(Func<bool> condition, string what, int seconds = 30) => WaitFor(() => condition() ? "" : null, what, seconds);

    private static string First(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target);
        }
    }

    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
