using System.Globalization;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Tools;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Scripts;

/// <summary>Groups shown in Utilities → Scripts.</summary>
public enum ScriptCategory
{
    FileManagement,
    ComparisonAndVerification,
    BatchProcessing,
    ImageUtilities,
    Archives,
    TibiaProjectUtilities,
    DiagnosticsAndReports,
}

/// <summary>Kinds of script inputs; the Scripts page shows a matching control for each.</summary>
public enum ScriptParameterKind
{
    Folder,
    /// <summary>A file or a folder.</summary>
    Path,
    File,
    /// <summary>A file the script writes (a report or manifest).</summary>
    OutputFile,
    OutputFolder,
    Text,
    Integer,
    Boolean,
    Choice,
}

/// <summary>One input of a script.</summary>
public sealed record ScriptParameter(string Key, string Label, ScriptParameterKind Kind, string Help)
{
    public bool Required { get; init; } = true;

    /// <summary>Shown under "Advanced options" (expanded by default when Settings → Scripts says so).</summary>
    public bool Advanced { get; init; }

    public string? Default { get; init; }

    /// <summary>Choices (value, label) for <see cref="ScriptParameterKind.Choice"/>.</summary>
    public IReadOnlyList<ScriptChoice> Choices { get; init; } = [];

    /// <summary>Choices that depend on the current state (for example the list of backups), refreshed when the script is shown.</summary>
    public Func<IReadOnlyList<ScriptChoice>>? ChoicesProvider { get; init; }

    public long Minimum { get; init; } = long.MinValue;
    public long Maximum { get; init; } = long.MaxValue;

    /// <summary>Open/save dialog filter, e.g. "ZIP archives|*.zip".</summary>
    public string? FileFilter { get; init; }

    /// <summary>Only shown (and validated) when another parameter has one of these values: "key=value1|value2".</summary>
    public string? VisibleWhen { get; init; }

    public bool IsVisible(ScriptArguments args)
    {
        if (VisibleWhen == null) return true;
        int eq = VisibleWhen.IndexOf('=');
        string key = VisibleWhen[..eq];
        return VisibleWhen[(eq + 1)..].Split('|').Contains(args.Raw(key) ?? "", StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record ScriptChoice(string Value, string Label);

/// <summary>Values entered for a script's parameters (as text), with typed access.</summary>
public sealed class ScriptArguments
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    public ScriptArguments() { }

    public ScriptArguments(IReadOnlyDictionary<string, string?> values)
    {
        foreach (var kv in values) _values[kv.Key] = kv.Value;
    }

    public string? this[string key]
    {
        get => Raw(key);
        set => _values[key] = value;
    }

    public string? Raw(string key) => _values.TryGetValue(key, out var v) ? v : null;
    public string Text(string key) => Raw(key)?.Trim() ?? "";
    public bool Bool(string key) => bool.TryParse(Raw(key), out bool b) && b;
    public long Long(string key, long fallback = 0) => long.TryParse(Raw(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : fallback;
    public int Int(string key, int fallback = 0) => (int)Math.Clamp(Long(key, fallback), int.MinValue, int.MaxValue);
    public string Choice(string key) => Text(key);
    public IReadOnlyDictionary<string, string?> Values => _values;

    /// <summary>Path value as a full path (empty → null).</summary>
    public string? PathValue(string key)
    {
        string t = Text(key).Trim('"');
        if (t.Length == 0) return null;
        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(t));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public ScriptArguments Clone() => new(_values);
}

/// <summary>Everything a script may use while it runs.</summary>
public sealed class ScriptContext
{
    public required ScriptArguments Args { get; init; }
    public IProgress<OperationProgress>? Progress { get; init; }
    public FileSafetyOptions Safety { get; init; } = FileSafetyOptions.Default;
    public required BackupStore Backups { get; init; }
    public required TempWorkspace Temp { get; init; }
    public ConflictPrompt? Prompt { get; init; }
    public int WorkerThreads { get; init; } = Environment.ProcessorCount;
    public bool StopOnError { get; init; }

    /// <summary>Back up files a script changes even when general backups are off (Settings → Scripts).</summary>
    public bool ForceBackups { get; init; }

    public OutputSession CreateOutput(string operation) =>
        new(operation, Safety with { BackupBeforeOverwrite = Safety.BackupBeforeOverwrite || ForceBackups }, Backups, Prompt);

    public bool BackupsEnabled => Safety.BackupBeforeOverwrite || ForceBackups;

    public void Report(int done, int total, string message) => Progress?.Report(new OperationProgress(done, total, message));
}

/// <summary>One change a script proposes (shown in the preview; applied only after confirmation).</summary>
public sealed class PlannedChange
{
    public required string Action { get; init; }
    public required string Source { get; init; }
    public string? Target { get; init; }
    public string Detail { get; init; } = "";

    /// <summary>The user may untick changes in the preview; only ticked ones are applied.</summary>
    public bool Selected { get; set; } = true;

    /// <summary>Changes that must not be applied (for example a name conflict); shown but not selectable.</summary>
    public bool Blocked { get; init; }

    /// <summary>Data the script needs to apply the change (for example the hash a file must still have).</summary>
    public string? Check { get; init; }
}

/// <summary>Result of running (or previewing) a script.</summary>
public sealed class ScriptResult
{
    public string Summary { get; set; } = "";
    public List<string> Notes { get; } = [];
    public List<string> Errors { get; } = [];
    public ReportTable? Table { get; set; }

    /// <summary>Changes proposed by a preview; null for scripts that do not change files.</summary>
    public List<PlannedChange>? Plan { get; set; }

    public int Processed { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }

    /// <summary>True when files were changed or written.</summary>
    public bool ChangesApplied { get; set; }

    public List<string> OutputFiles { get; } = [];
    public string? OutputFolder { get; set; }

    /// <summary>Backup set that holds the files this run replaced or moved away (for undo).</summary>
    public string? BackupFolder { get; set; }
}

/// <summary>
/// A built-in file utility of Utilities → Scripts. Scripts are registered code, not user-supplied
/// programs: they never run automatically, validate their inputs, report progress, can be
/// cancelled, and — when they change files — first produce a preview (<see cref="ScriptResult.Plan"/>) that is
/// applied only after the user confirms it.
/// </summary>
public abstract class Script
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract ScriptCategory Category { get; }

    /// <summary>What the script reads, e.g. "Any files" or "ZIP and TAR archives".</summary>
    public abstract string SupportedInputs { get; }

    /// <summary>What the script does not do, or cannot check.</summary>
    public virtual string Limitations => "";

    public abstract IReadOnlyList<ScriptParameter> Parameters { get; }

    /// <summary>Capabilities beyond those every script has (progress, cancellation, batches; preview and backups for scripts that change files).</summary>
    protected virtual ToolCapabilities ExtraCapabilities => ToolCapabilities.None;

    /// <summary>What the script needs besides Tibia Foundry itself (checked before it runs).</summary>
    public virtual IReadOnlyList<ToolDependency> Dependencies => [];

    /// <summary>Where the script's reports or files go by default (Settings → Files and folders).</summary>
    public virtual OutputKind DefaultOutput => OutputKind.Scripts;

    private ToolManifest? _manifest;

    /// <summary>
    /// The script's entry in the tool registry, built from what the script declares, so scripts are
    /// listed, checked and reported like every other tool.
    /// </summary>
    public ToolManifest Manifest => _manifest ??= new ToolManifest
    {
        Id = "script." + Id,
        Name = Name,
        Description = Description,
        Type = ToolType.Script,
        Version = "1.0",
        Category = "Scripts: " + CategoryName(Category),
        Glyph = "\uE943",
        Capabilities = ToolCapabilities.Progress | ToolCapabilities.Cancellation | ToolCapabilities.Batch
                       | (ChangesFiles ? ToolCapabilities.Preview | ToolCapabilities.Backups : ToolCapabilities.None) | ExtraCapabilities,
        Operations = ChangesFiles
            ? [new ToolOperation("preview", L.T("Script.PreviewChanges"), ToolCapabilities.Progress | ToolCapabilities.Cancellation | ToolCapabilities.Preview | ToolCapabilities.DryRun),
               new ToolOperation("apply", L.T("Script.ApplyChanges"), ToolCapabilities.Progress | ToolCapabilities.Cancellation | ToolCapabilities.Backups)]
            : [new ToolOperation("run", "Run", ToolCapabilities.Progress | ToolCapabilities.Cancellation)],
        Inputs = [new DataFormat(SupportedInputs, [])],
        Outputs = Parameters.Any(p => p.Kind == ScriptParameterKind.OutputFile) ? [new DataFormat(L.T("Script.ReportsAndManifests"), [".csv", ".json", ".txt"])] : [],
        DefaultOutput = DefaultOutput,
        Dependencies = Dependencies,
        License = BuiltInLicense,
        Documentation = ["docs/14_Tools_Settings_and_Scripts.md §6 (" + Name + ")"],
        Limitations = Limitations,
    };

    /// <summary>License of the scripts: part of Tibia Foundry.</summary>
    public static ToolLicense BuiltInLicense { get; } = new(L.T("Script.TibiaFoundry"), L.T("Script.PartOfTibiaFoundryRockyLaRoTibiaWiki"));

    /// <summary>True when the script can change, move or remove files (it then previews first).</summary>
    public virtual bool ChangesFiles => false;

    /// <summary>True when, with these arguments, the run will change files (the page then asks for a preview first).</summary>
    public virtual bool WillChangeFiles(ScriptArguments args) => ChangesFiles;

    /// <summary>A copy of the arguments in which every empty value is replaced by its parameter's default.</summary>
    public ScriptArguments WithDefaults(ScriptArguments args)
    {
        var copy = args.Clone();
        foreach (var p in Parameters)
            if (p.Default != null && copy.Text(p.Key).Length == 0) copy[p.Key] = p.Default;
        return copy;
    }

    /// <summary>Checks the arguments; returns the problems (empty when the script can run).</summary>
    public virtual IReadOnlyList<string> Validate(ScriptArguments args)
    {
        var problems = new List<string>();
        foreach (var p in Parameters.Where(p => p.IsVisible(args)))
        {
            string value = args.Text(p.Key);
            if (value.Length == 0)
            {
                // An unticked box is a valid answer, and an empty field with a default uses the default.
                if (p.Required && p.Kind != ScriptParameterKind.Boolean && p.Default == null) problems.Add(L.F("Script.0Required", p.Label));
                continue;
            }
            string? path = p.Kind is ScriptParameterKind.Folder or ScriptParameterKind.File or ScriptParameterKind.Path or ScriptParameterKind.OutputFile or ScriptParameterKind.OutputFolder
                ? args.PathValue(p.Key) : null;
            switch (p.Kind)
            {
                case ScriptParameterKind.Folder when path == null || !Directory.Exists(path):
                    problems.Add(L.F("Script.0TheFolder1DoesNot", p.Label, value));
                    break;
                case ScriptParameterKind.File when path == null || !File.Exists(path):
                    problems.Add(L.F("Script.0TheFile1DoesNot", p.Label, value));
                    break;
                case ScriptParameterKind.Path when path == null || (!File.Exists(path) && !Directory.Exists(path)):
                    problems.Add(L.F("Script.01DoesNotExist", p.Label, value));
                    break;
                case ScriptParameterKind.OutputFile or ScriptParameterKind.OutputFolder when path == null:
                    problems.Add(L.F("Script.01IsNotAValid", p.Label, value));
                    break;
                case ScriptParameterKind.Integer when !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) || n < p.Minimum || n > p.Maximum:
                    problems.Add(L.F("Script.0EnterAWholeNumber1", p.Label, (p.Minimum > long.MinValue ? L.F("Script.From0N0", p.Minimum) : ""), (p.Maximum < long.MaxValue ? L.F("Script.To0N0", p.Maximum) : "")));
                    break;
                case ScriptParameterKind.Choice:
                    var choices = p.ChoicesProvider?.Invoke() ?? p.Choices;
                    if (!choices.Any(c => string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase))) problems.Add(L.F("Script.0ChooseOneOfTheListed", p.Label));
                    break;
            }
        }
        return problems;
    }

    /// <summary>Runs the script: read-only work, or the preview of a script that changes files.</summary>
    public abstract ScriptResult Run(ScriptContext context, CancellationToken ct);

    /// <summary>Applies the selected changes of a preview. Only called for <see cref="ChangesFiles"/> scripts after confirmation.</summary>
    public virtual ScriptResult Apply(ScriptContext context, IReadOnlyList<PlannedChange> changes, CancellationToken ct) =>
        throw new NotSupportedException($"{Name} does not change files.");

    public static string CategoryName(ScriptCategory category) => category switch
    {
        ScriptCategory.FileManagement => L.T("Script.FileManagement"),
        ScriptCategory.ComparisonAndVerification => L.T("Script.ComparisonAndVerification"),
        ScriptCategory.BatchProcessing => L.T("Script.BatchProcessing"),
        ScriptCategory.ImageUtilities => L.T("Script.ImageUtilities"),
        ScriptCategory.Archives => L.T("Script.Archives"),
        ScriptCategory.TibiaProjectUtilities => L.T("Script.TibiaProjectUtilities"),
        _ => L.T("Script.DiagnosticsAndReports"),
    };

    // Shared parameter definitions.
    protected static ScriptParameter FolderParameter(string key = "folder", string? label = null, string? help = null) =>
        new(key, label ?? L.T("Common.Folder"), ScriptParameterKind.Folder, help ?? L.T("Script.TheFolderToScan"));

    protected static ScriptParameter RecursiveParameter(bool defaultValue = true) =>
        new("recursive", L.T("Script.IncludeSubfolders"), ScriptParameterKind.Boolean, L.T("Script.AlsoScanTheFoldersInsideThe")) { Default = defaultValue ? "true" : "false" };
}
