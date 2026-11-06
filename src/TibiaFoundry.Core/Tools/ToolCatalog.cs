using System.Text.RegularExpressions;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Tools;

/// <summary>State of one dependency at the moment it was checked.</summary>
public enum DependencyState
{
    Available,
    Missing,
    /// <summary>Present, but older than the tool's minimum version or otherwise unusable.</summary>
    Incompatible,
    /// <summary>Not checked in advance (for example a web service, which is only contacted when used).</summary>
    NotChecked,
}

/// <summary>What a dependency probe found.</summary>
public sealed record DependencyProbeResult(DependencyState State, string? Version = null, string? Detail = null);

/// <summary>A dependency of a tool with the result of checking it.</summary>
public sealed record DependencyStatus(ToolDependency Dependency, DependencyState State, string? Version, string Message)
{
    public bool Blocks => Dependency.Required && State is DependencyState.Missing or DependencyState.Incompatible;
}

/// <summary>Whether a tool (or one of its operations) can run now, and why not.</summary>
public sealed record ToolAvailability(bool CanRun, IReadOnlyList<DependencyStatus> Dependencies, string? Problem)
{
    /// <summary>Optional dependencies that are missing: the tool works, with less.</summary>
    public IReadOnlyList<DependencyStatus> Degraded => Dependencies.Where(d => !d.Dependency.Required && d.State is DependencyState.Missing or DependencyState.Incompatible).ToList();
}

/// <summary>A manifest that failed validation; the tool is not offered, the rest of the program works.</summary>
public sealed record RejectedTool(string Id, string Name, IReadOnlyList<string> Problems);

/// <summary>
/// The registry of tools. Manifests are validated when they are registered: an invalid one is
/// rejected with its problems (shown in Diagnostics and logged) instead of stopping the program.
/// Dependencies are checked through probes registered by the application, before an operation
/// starts. Only manifests compiled into Tibia Foundry are registered: nothing is loaded or run
/// from files, so a folder that merely contains a manifest is never executed.
/// </summary>
public sealed partial class ToolCatalog
{
    private readonly object _sync = new();
    private readonly List<ToolManifest> _tools = [];
    private readonly List<RejectedTool> _rejected = [];
    private readonly Dictionary<string, Func<DependencyProbeResult>> _probes = new(StringComparer.Ordinal);

    public IReadOnlyList<ToolManifest> Tools { get { lock (_sync) return _tools.ToList(); } }
    public IReadOnlyList<RejectedTool> Rejected { get { lock (_sync) return _rejected.ToList(); } }

    public ToolManifest? Find(string id) { lock (_sync) return _tools.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal)); }

    /// <summary>Registers a tool; returns false (and records the problems) when its manifest is invalid.</summary>
    public bool TryRegister(ToolManifest manifest, out IReadOnlyList<string> problems)
    {
        lock (_sync)
        {
            if (manifest != null && _tools.Any(t => ReferenceEquals(t, manifest)))
            {
                problems = [];
                return true; // the same tool registered again (for example by a second window): nothing changes
            }
            problems = Validate(manifest, _tools.Select(t => t.Id));
            if (problems.Count > 0)
            {
                _rejected.Add(new RejectedTool(manifest?.Id ?? L.T("ToolCatalog.NoId"), manifest?.Name ?? L.T("ToolCatalog.NoName"), problems));
                AppLog.Error($"Tool \"{manifest?.Name}\" ({manifest?.Id}) was not registered: {string.Join(" ", problems)}", source: "Tools");
                return false;
            }
            _tools.Add(manifest!);
            return true;
        }
    }

    /// <summary>Supplies the check of a dependency id (for example "imagemagick" or "tibia-client").</summary>
    public void AddProbe(string dependencyId, Func<DependencyProbeResult> probe)
    {
        lock (_sync) _probes[dependencyId] = probe;
    }

    [GeneratedRegex(@"^[a-z0-9]+(?:[.-][a-z0-9]+)*$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*$")]
    private static partial Regex SettingKeyPattern();

    [GeneratedRegex(@"^\d+(\.\d+){1,3}$")]
    private static partial Regex VersionPattern();

    /// <summary>Checks a manifest; returns its problems (empty when it is valid).</summary>
    public static IReadOnlyList<string> Validate(ToolManifest? m, IEnumerable<string>? existingIds = null)
    {
        var p = new List<string>();
        if (m == null) return ["The manifest is missing."];
        if (string.IsNullOrWhiteSpace(m.Id) || m.Id.Length > 64 || !IdPattern().IsMatch(m.Id))
            p.Add(L.F("ToolCatalog.TheId0IsNotValid", m.Id));
        else if (existingIds?.Contains(m.Id, StringComparer.Ordinal) == true)
            p.Add(L.F("ToolCatalog.TheId0IsAlreadyUsed", m.Id));
        if (string.IsNullOrWhiteSpace(m.Name) || m.Name.Length > 60) p.Add(L.T("ToolCatalog.TheNameMustBe1To"));
        if (string.IsNullOrWhiteSpace(m.Description)) p.Add(L.T("ToolCatalog.TheDescriptionIsMissing"));
        if (!Enum.IsDefined(m.Type)) p.Add(L.T("ToolCatalog.TheToolTypeIsNotValid"));
        if (string.IsNullOrWhiteSpace(m.Version) || !VersionPattern().IsMatch(m.Version)) p.Add(L.F("ToolCatalog.TheVersion0IsNotValid", m.Version));
        if (m.License == null || string.IsNullOrWhiteSpace(m.License.Name) || string.IsNullOrWhiteSpace(m.License.Attribution))
            p.Add(L.T("ToolCatalog.LicenseAndAttributionAreRequired"));
        if (m.Platform?.MinimumWindows == null) p.Add(L.T("ToolCatalog.ThePlatformRequirementIsMissing"));

        var operations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var o in m.Operations ?? [])
        {
            if (o == null || string.IsNullOrWhiteSpace(o.Id) || string.IsNullOrWhiteSpace(o.Name)) p.Add(L.T("ToolCatalog.AnOperationHasNoIdOr"));
            else if (!operations.Add(o.Id)) p.Add(L.F("ToolCatalog.TheOperationId0IsUsed", o.Id));
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in m.Settings ?? [])
        {
            if (s == null || string.IsNullOrWhiteSpace(s.Key) || !SettingKeyPattern().IsMatch(s.Key)) { p.Add(L.F("ToolCatalog.TheSettingKey0IsNot", s?.Key)); continue; }
            if (!keys.Add(s.Key)) p.Add(L.F("ToolCatalog.TheSetting0IsDeclaredTwice", s.Key));
            if (string.IsNullOrWhiteSpace(s.Label)) p.Add(L.F("ToolCatalog.TheSetting0HasNoLabel", s.Key));
            if (s.Kind == ToolSettingKind.Choice && s.Choices.Count == 0) p.Add(L.F("ToolCatalog.TheSetting0HasNoChoices", s.Key));
            if (s.Minimum > s.Maximum) p.Add(L.F("ToolCatalog.TheSetting0HasAMinimum", s.Key));
            if (s.Validate(s.Default, out string? problem) == null) p.Add(L.F("ToolCatalog.TheDefaultOf0IsNot", s.Key, problem));
        }
        if (m.SettingsVersion < 1) p.Add(L.T("ToolCatalog.TheSettingsVersionMustBe1"));
        var from = new HashSet<int>();
        foreach (var mig in m.SettingsMigrations ?? [])
        {
            if (mig == null || mig.Apply == null) { p.Add(L.T("ToolCatalog.ASettingsMigrationHasNoAction")); continue; }
            if (mig.FromVersion < 1 || mig.FromVersion >= m.SettingsVersion) p.Add(L.F("ToolCatalog.ASettingsMigrationStartsAtVersion", mig.FromVersion, m.SettingsVersion - 1));
            if (!from.Add(mig.FromVersion)) p.Add(L.F("ToolCatalog.TwoSettingsMigrationsStartAtVersion", mig.FromVersion));
        }

        var dependencyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in m.Dependencies ?? [])
        {
            if (d == null || string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name)) { p.Add(L.T("ToolCatalog.ADependencyHasNoIdOr")); continue; }
            if (!dependencyIds.Add(d.Id)) p.Add(L.F("ToolCatalog.TheDependency0IsDeclaredTwice", d.Id));
            if (string.IsNullOrWhiteSpace(d.Resolution)) p.Add(L.F("ToolCatalog.TheDependency0DoesNotSay", d.Id));
            foreach (string op in d.RequiredFor.Where(op => !operations.Contains(op)))
                p.Add(L.F("ToolCatalog.TheDependency0NamesTheOperation", d.Id, op));
            if (d.MinimumVersion.Length > 0 && !System.Version.TryParse(d.MinimumVersion, out _)) p.Add(L.F("ToolCatalog.TheMinimumVersionOf0Is", d.Id));
        }
        return p;
    }

    /// <summary>Checks the platform and the dependencies that apply to an operation (null = the whole tool).</summary>
    public IReadOnlyList<DependencyStatus> CheckDependencies(ToolManifest tool, string? operation = null)
    {
        var results = new List<DependencyStatus>();
        foreach (var d in tool.Dependencies.Where(d => d.AppliesTo(operation) || operation == null))
        {
            Func<DependencyProbeResult>? probe;
            lock (_sync) probe = _probes.GetValueOrDefault(d.Id);
            DependencyProbeResult found;
            try
            {
                found = probe?.Invoke() ?? new DependencyProbeResult(DependencyState.NotChecked, Detail: L.T("ToolCatalog.NoCheckIsAvailableItIs"));
            }
            catch (Exception ex)
            {
                // A failing check must not take the program down; it counts as missing.
                AppLog.Warn($"The check of {d.Name} failed.", ex, source: "Tools");
                found = new DependencyProbeResult(DependencyState.Missing, Detail: L.T("ToolCatalog.TheCheckFailed") + ex.Message);
            }
            if (found.State == DependencyState.Available && d.MinimumVersion.Length > 0 && found.Version != null &&
                System.Version.TryParse(Numeric(found.Version), out var have) && System.Version.TryParse(d.MinimumVersion, out var need) && have < need)
                found = found with { State = DependencyState.Incompatible, Detail = L.F("ToolCatalog.Version0IsOlderThanThe", found.Version, d.MinimumVersion) };
            string message = found.State switch
            {
                DependencyState.Available => L.F("ToolCatalog.0Available1", d.Name, (found.Version != null ? " (" + found.Version + ")" : "")),
                DependencyState.NotChecked => $"{d.Name}: {found.Detail ?? "checked when it is used"}.",
                _ => $"{d.Name}: {(found.State == DependencyState.Missing ? "missing" : "not compatible")}{(found.Detail != null ? " — " + found.Detail : "")}. {d.Resolution}",
            };
            results.Add(new DependencyStatus(d, found.State, found.Version, message));
        }
        return results;
    }

    private static string Numeric(string version) => new string(version.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');

    /// <summary>
    /// Whether the tool can run now: the platform matches and no required dependency of the
    /// operation is missing. The problem text names what is missing and how to resolve it.
    /// </summary>
    public ToolAvailability Availability(ToolManifest tool, string? operation = null)
    {
        var problems = new List<string>();
        if (Environment.OSVersion.Platform != PlatformID.Win32NT || Environment.OSVersion.Version < tool.Platform.MinimumWindows)
            problems.Add(L.F("ToolCatalog.0NeedsWindows1OrLater", tool.Name, tool.Platform.MinimumWindows));
        if (tool.Platform.Requires64BitProcess && !Environment.Is64BitProcess)
            problems.Add(L.F("ToolCatalog.0NeedsThe64BitEdition", tool.Name));
        var dependencies = CheckDependencies(tool, operation);
        // For the tool as a whole, only dependencies of every operation block; the others block their operations.
        problems.AddRange(dependencies.Where(d => d.Blocks && (operation != null || d.Dependency.RequiredFor.Count == 0)).Select(d => d.Message));
        return new ToolAvailability(problems.Count == 0, dependencies, problems.Count == 0 ? null : string.Join(" ", problems));
    }
}
