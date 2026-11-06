using TibiaFoundry.Core.Tools;

namespace TibiaFoundry.Core.Localization;

/// <summary>
/// The display text of tool manifests in the selected language. Manifests keep their English
/// text as identity (logs, the operation history, the Asset Library's "made with" record); the
/// interface shows these translations, keyed by the tool's id:
/// <c>Tool.{id}.Name</c>, <c>.Description</c>, <c>.Limitations</c>, <c>.Op.{operation}</c>,
/// <c>.Setting.{key}.Label/.Help/.Choice.{value}</c>, <c>.Dependency.{id}.Resolution</c>;
/// <c>Dependency.{id}.Name</c> and <c>Group.{name}</c> are shared.
/// </summary>
public static class ToolText
{
    public static string Name(ToolManifest tool) => L.TOr($"Tool.{tool.Id}.Name", tool.Name);

    public static string Description(ToolManifest tool) => L.TOr($"Tool.{tool.Id}.Description", tool.Description);

    public static string? Limitations(ToolManifest tool) => string.IsNullOrEmpty(tool.Limitations) ? tool.Limitations : L.TOr($"Tool.{tool.Id}.Limitations", tool.Limitations);

    /// <summary>A navigation group ("Graphics &amp; Sprites").</summary>
    public static string Group(string category) => L.TOr("Group." + KeyPart(category), category);

    public static string Operation(ToolManifest tool, ToolOperation operation) => L.TOr($"Tool.{tool.Id}.Op.{operation.Id}", operation.Name);

    /// <summary>An operation by id (its id when the tool has no such operation).</summary>
    public static string Operation(ToolManifest tool, string operationId) =>
        tool.Operations.FirstOrDefault(o => o.Id == operationId) is { } op ? Operation(tool, op) : operationId;

    public static string SettingLabel(ToolManifest tool, ToolSettingDefinition setting) => L.TOr($"Tool.{tool.Id}.Setting.{setting.Key}.Label", setting.Label);

    public static string SettingHelp(ToolManifest tool, ToolSettingDefinition setting) => L.TOr($"Tool.{tool.Id}.Setting.{setting.Key}.Help", setting.Help);

    public static string SettingChoice(ToolManifest tool, ToolSettingDefinition setting, string value, string label) =>
        L.TOr($"Tool.{tool.Id}.Setting.{setting.Key}.Choice.{KeyPart(value)}", label);

    public static string DependencyName(ToolDependency dependency) => L.TOr($"Dependency.{dependency.Id}.Name", dependency.Name);

    public static string Resolution(ToolManifest tool, ToolDependency dependency) => L.TOr($"Tool.{tool.Id}.Dependency.{dependency.Id}.Resolution", dependency.Resolution);

    /// <summary>A name made usable inside a key (letters and digits only).</summary>
    public static string KeyPart(string text) => new(text.Where(char.IsAsciiLetterOrDigit).ToArray());
}
