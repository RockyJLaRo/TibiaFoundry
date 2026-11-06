using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

/// <summary>
/// Choosing, checking and resetting the client's files and folders: "Locate…" and "Change…" on the
/// Compatibility page, the list in Settings → Client and data locations, the page banners and the
/// pages' "use this file by default". A choice is checked before it is saved (a file or folder that
/// cannot be used is refused and nothing changes), saved at once, and applied without a restart:
/// what was loaded from the affected files is discarded and the affected compatibility rows are
/// checked again.
/// </summary>
public sealed class LocationService(AppServices services)
{
    /// <summary>Raised after a location was chosen or reset (on the UI thread).</summary>
    public event Action? LocationsChanged;

    private ClientScope ScopeOrActive(ClientScope? scope) => scope ?? services.Settings.ActiveScope;

    public ResolvedLocation Resolve(string id, ClientScope? scope = null) => services.Data.LocationsFor(ScopeOrActive(scope)).Resolve(id);

    /// <summary>" (live client)" / " (test server)" for locations that differ per client, else "".</summary>
    public static string ClientSuffix(string id, ClientScope scope) =>
        !LocationResolver.Definition(id).PerClient ? "" : scope == ClientScope.Live ? L.T("Location.LiveClient") : L.T("Location.TestServer");

    /// <summary>Shows the picker for a location, checks the choice and applies it. True when a new location is in use.</summary>
    public async Task<bool> LocateAsync(string id, ClientScope? scope = null)
    {
        var s = ScopeOrActive(scope);
        var current = Resolve(id, s);
        var def = current.Definition;
        string title = L.F("Location.LocateThe01", LowerFirst(def.Name), ClientSuffix(id, s));
        string start = StartFolder(current);
        string? picked = def.Kind == LocationKind.File
            ? services.Dialogs.OpenFile(title, def.FileFilter ?? L.T("Location.AllFiles"), start)
            : services.Dialogs.PickFolder(title, start);
        return picked != null && await UseAsync(id, picked, s);
    }

    /// <summary>
    /// Checks a path for a location and, when it can be used, saves it as the user's choice and
    /// applies it. A path that cannot be used is explained and nothing changes.
    /// </summary>
    public async Task<bool> UseAsync(string id, string path, ClientScope? scope = null)
    {
        var s = ScopeOrActive(scope);
        var def = LocationResolver.Definition(id);
        var (normalized, note) = LocationResolver.NormalizeChoice(id, path);
        string? problem;
        try
        {
            problem = await Task.Run(() => Check(id, normalized, s));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            problem = ex.Message;
        }
        if (problem != null)
        {
            AppLog.Info($"Location \"{id}\" ({s}) not changed: {normalized} — {problem}");
            services.Dialogs.Show(L.F("Location.The01WasNotChanged", LowerFirst(def.Name), ClientSuffix(id, s), normalized, problem), MessageKind.Warning, def.Name);
            return false;
        }
        services.Settings.SetChosenLocation(id, s, normalized);
        services.SaveSettings();
        AppLog.Info($"Location \"{id}\" ({s}) set to {normalized}.");
        await ApplyAsync(id, s);
        services.Status.Show($"{def.Name}{ClientSuffix(id, s)}: {normalized}{(note != null ? $" ({note})" : "")}.", StatusKind.Success);
        return true;
    }

    /// <summary>Forgets the user's choice: the location is found automatically again.</summary>
    public async Task ResetAsync(string id, ClientScope? scope = null)
    {
        var s = ScopeOrActive(scope);
        var def = LocationResolver.Definition(id);
        if (services.Settings.ChosenLocation(id, s) == null) return;
        services.Settings.SetChosenLocation(id, s, null);
        services.SaveSettings();
        AppLog.Info($"Location \"{id}\" ({s}) reset to automatic.");
        await ApplyAsync(id, s);
        var now = Resolve(id, s);
        services.Status.Show(now.Found
            ? L.F("Location.01IsFoundAutomaticallyAgain", def.Name, ClientSuffix(id, s), now.Path)
            : L.F("Location.01IsAutomaticAgainBut", def.Name, ClientSuffix(id, s), now.Problem), now.Found ? StatusKind.Success : StatusKind.Warning);
    }

    /// <summary>Why a path cannot be used for a location (null when it can). Runs on a worker thread.</summary>
    private string? Check(string id, string path, ClientScope scope)
    {
        if (LocationResolver.DeepCheck(id, path) is { } problem) return problem;
        if (!LocationIds.DataFiles.Contains(id) && id != LocationIds.ResourceFile) return null;
        // A data file or the resource file must be readable: the compatibility check of that one
        // component decides, with the candidate in place of the current file.
        var chosen = new Dictionary<string, string>(services.Settings.ChosenLocations(scope), StringComparer.Ordinal) { [id] = path };
        var probe = new LocationResolver(scope, services.Settings.PackageNameFor(scope), chosen,
            reportedOptional: id == LocationIds.ResourceFile ? [LocationIds.ResourceFile] : null);
        string component = LocationResolver.Definition(id).Component!;
        var report = CompatibilityChecker.Check(probe, only: new HashSet<string>(StringComparer.Ordinal) { component });
        var row = report.Components.FirstOrDefault(c => c.Component == component);
        return row is { Status: CompatibilityStatus.Missing or CompatibilityStatus.Unsupported } ? row.Summary : null;
    }

    /// <summary>Applies a changed location to the client in use (the other client's choice only takes effect when it is in use).</summary>
    private async Task ApplyAsync(string id, ClientScope scope)
    {
        if (scope == services.Settings.ActiveScope || !LocationResolver.Definition(id).PerClient)
        {
            services.Data.ApplyLocationChange(id);
            try
            {
                await services.Data.RecheckAsync(id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLog.Warn("Checking the client files again after a location change failed.", ex);
            }
        }
        LocationsChanged?.Invoke();
    }

    /// <summary>The folder a picker starts in: the location's folder, else the nearest folder that exists.</summary>
    private static string StartFolder(ResolvedLocation location)
    {
        string path = location.DisplayPath;
        try
        {
            string? dir = location.Definition.Kind == LocationKind.Folder && Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) dir = Path.GetDirectoryName(dir);
            return dir ?? "";
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }

    private static string LowerFirst(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
