using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Tools;

/// <summary>What kind of tool a manifest describes (shown in Diagnostics and used for grouping).</summary>
public enum ToolType { Viewer, Editor, Converter, Exporter, Script, Validator, Utility, Application }

/// <summary>
/// What a tool or an operation supports. The interface adapts to it: for example the Cancel
/// buttons appear only for operations that declare <see cref="Cancellation"/>.
/// </summary>
[Flags]
public enum ToolCapabilities
{
    None = 0,
    /// <summary>Processes many files or items in one run.</summary>
    Batch = 1,
    /// <summary>Reports measured progress (items done of a known total).</summary>
    Progress = 2,
    /// <summary>Can be stopped safely; temporary files are removed and outputs stay consistent.</summary>
    Cancellation = 4,
    /// <summary>Shows what will change before anything is written.</summary>
    Preview = 8,
    /// <summary>Can run without writing anything (a report of what would happen).</summary>
    DryRun = 16,
    /// <summary>Changes can be undone afterwards.</summary>
    Undo = 32,
    /// <summary>Backs up files before replacing them.</summary>
    Backups = 64,
    /// <summary>Reads written files back and checks them before they replace anything.</summary>
    Validation = 128,
}

/// <summary>A file format a tool reads or writes.</summary>
public sealed record DataFormat(string Name, IReadOnlyList<string> Extensions, string? Notes = null)
{
    public override string ToString() => Extensions.Count == 0 ? Name : $"{Name} ({string.Join(", ", Extensions)})";
}

/// <summary>Kinds of things a tool can depend on.</summary>
public enum DependencyKind
{
    /// <summary>Files of the installed Tibia client.</summary>
    ClientData,
    /// <summary>A library shipped with Tibia Foundry (for example ImageMagick's native part).</summary>
    Library,
    /// <summary>An external program. Never downloaded or started without the user's consent.</summary>
    Executable,
    /// <summary>A web service (contacted only when the operation that needs it runs).</summary>
    Service,
    Runtime,
    Platform,
}

/// <summary>
/// Something a tool needs. Required dependencies block the operations they are listed for (with
/// an explanation); optional ones only reduce what the tool can do.
/// </summary>
public sealed record ToolDependency(string Id, string Name, DependencyKind Kind, bool Required, string Resolution)
{
    /// <summary>Lowest version that works ("" = any).</summary>
    public string MinimumVersion { get; init; } = "";

    /// <summary>Operations that need it (empty = the whole tool).</summary>
    public IReadOnlyList<string> RequiredFor { get; init; } = [];

    public bool AppliesTo(string? operation) => RequiredFor.Count == 0 || (operation != null && RequiredFor.Contains(operation, StringComparer.Ordinal));
}

/// <summary>License and attribution of a tool (or of the third-party work it is based on).</summary>
public sealed record ToolLicense(string Name, string Attribution, string? Spdx = null, string? Url = null);

/// <summary>Operating system and process requirements.</summary>
public sealed record PlatformRequirement(Version MinimumWindows, bool Requires64BitProcess = true)
{
    public static PlatformRequirement Windows10x64 { get; } = new(new Version(10, 0, 17763), true);
}

/// <summary>Kinds of tool settings; the Settings page shows a matching control for each.</summary>
public enum ToolSettingKind { Boolean, Integer, Choice, Text }

/// <summary>A setting a tool declares; stored per tool and validated against this definition.</summary>
public sealed record ToolSettingDefinition(string Key, string Label, ToolSettingKind Kind, string Default, string Help)
{
    public long Minimum { get; init; } = long.MinValue;
    public long Maximum { get; init; } = long.MaxValue;

    /// <summary>(value, label) pairs for <see cref="ToolSettingKind.Choice"/>.</summary>
    public IReadOnlyList<(string Value, string Label)> Choices { get; init; } = [];

    /// <summary>Maximum length for <see cref="ToolSettingKind.Text"/>.</summary>
    public int MaxLength { get; init; } = 256;

    /// <summary>Shown in Settings → Tools (false for values the tool remembers by itself, such as the last signature typed).</summary>
    public bool Visible { get; init; } = true;

    /// <summary>Checks a value; returns the normalized value, or null with the reason.</summary>
    public string? Validate(string? value, out string? problem)
    {
        problem = null;
        string v = (value ?? "").Trim();
        switch (Kind)
        {
            case ToolSettingKind.Boolean:
                if (bool.TryParse(v, out bool b)) return b ? "true" : "false";
                problem = L.F("ToolManifest.0ExpectedTrueOrFalse", Label);
                return null;
            case ToolSettingKind.Integer:
                if (long.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long n) && n >= Minimum && n <= Maximum)
                    return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                problem = L.F("ToolManifest.0EnterAWholeNumber1", Label, (Minimum > long.MinValue ? L.F("ToolManifest.From0N0", Minimum) : ""), (Maximum < long.MaxValue ? L.F("ToolManifest.To0N0", Maximum) : ""));
                return null;
            case ToolSettingKind.Choice:
                var match = Choices.FirstOrDefault(c => string.Equals(c.Value, v, StringComparison.OrdinalIgnoreCase));
                if (match.Value != null) return match.Value;
                problem = L.F("ToolManifest.0ChooseOneOf1", Label, string.Join(", ", Choices.Select(c => c.Label)));
                return null;
            default:
                if (v.Length <= MaxLength && !v.Any(char.IsControl)) return v;
                problem = L.F("ToolManifest.0AtMost1CharactersWithout", Label, MaxLength);
                return null;
        }
    }
}

/// <summary>A step that upgrades a tool's stored settings from one settings version to the next.</summary>
public sealed record ToolSettingsMigration(int FromVersion, string Description, Action<IDictionary<string, string>> Apply);

/// <summary>One operation of a tool, with its own capabilities (a tool may support more in some operations than in others).</summary>
public sealed record ToolOperation(string Id, string Name, ToolCapabilities Capabilities);

/// <summary>
/// Structured description of a tool: identity, what it does and supports, what it reads and
/// writes, its settings and dependencies, license and documentation. Every page and script of
/// Tibia Foundry is described by one; the application builds navigation, settings, dependency
/// checks, diagnostics and capability-dependent controls from it. See docs/16_Adding_a_Tool.md.
/// </summary>
public sealed record ToolManifest
{
    /// <summary>Unique, stable identifier: lower case words separated by dots or hyphens (e.g. "rcc-editor", "script.checksum").</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required ToolType Type { get; init; }

    /// <summary>Version of the tool's behavior and settings ("1.0", "2.1.0").</summary>
    public required string Version { get; init; }

    /// <summary>Navigation group ("Client data", "Tools", "Application") or, for scripts, their category.</summary>
    public string Category { get; init; } = "Tools";

    /// <summary>Segoe Fluent/MDL2 icon of the navigation entry.</summary>
    public string Glyph { get; init; } = "";

    public ToolCapabilities Capabilities { get; init; }
    public IReadOnlyList<ToolOperation> Operations { get; init; } = [];
    public IReadOnlyList<DataFormat> Inputs { get; init; } = [];
    public IReadOnlyList<DataFormat> Outputs { get; init; } = [];

    /// <summary>Where the tool's output goes by default (Settings → Files and folders).</summary>
    public OutputKind? DefaultOutput { get; init; }

    public IReadOnlyList<ToolSettingDefinition> Settings { get; init; } = [];

    /// <summary>
    /// Prefix of the tool's keys in the stored settings (default "&lt;Id&gt;."). Tools that stored options
    /// before the registry existed keep their old prefix, so nothing is lost on upgrade.
    /// </summary>
    public string? SettingsPrefix { get; init; }

    public string EffectiveSettingsPrefix => SettingsPrefix ?? Id + ".";

    /// <summary>Version of the settings layout; stored settings of an older version are upgraded with <see cref="SettingsMigrations"/>.</summary>
    public int SettingsVersion { get; init; } = 1;
    public IReadOnlyList<ToolSettingsMigration> SettingsMigrations { get; init; } = [];

    public IReadOnlyList<ToolDependency> Dependencies { get; init; } = [];
    public PlatformRequirement Platform { get; init; } = PlatformRequirement.Windows10x64;

    public required ToolLicense License { get; init; }

    /// <summary>Documentation sections (file and heading), shown in Diagnostics and the tool's help.</summary>
    public IReadOnlyList<string> Documentation { get; init; } = [];

    /// <summary>Limits stated plainly, for the help and Diagnostics.</summary>
    public string Limitations { get; init; } = "";

    public bool Supports(ToolCapabilities capability) => (Capabilities & capability) == capability;

    /// <summary>Capabilities of one operation (the tool's own when the operation is not listed).</summary>
    public ToolCapabilities CapabilitiesOf(string? operation) =>
        Operations.FirstOrDefault(o => string.Equals(o.Id, operation, StringComparison.Ordinal))?.Capabilities ?? Capabilities;

    public ToolSettingDefinition? Setting(string key) => Settings.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));

    public static string CapabilityNames(ToolCapabilities capabilities) => capabilities == ToolCapabilities.None
        ? L.T("ToolManifest.NoneDeclared")
        : string.Join(", ", Enum.GetValues<ToolCapabilities>().Where(c => c != ToolCapabilities.None && capabilities.HasFlag(c)).Select(c => c switch
        {
            ToolCapabilities.DryRun => L.T("ToolManifest.DryRun"),
            _ => c.ToString().ToLowerInvariant(),
        }));
}
