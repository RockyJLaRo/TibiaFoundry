using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Localization;

/// <summary><c>TibiaFoundry.exe --localization-report &lt;file&gt;</c>: writes the translation completeness report (Markdown).</summary>
public static class LocalizationReport
{
    /// <summary>Every data-driven text of the built-in tools (names, descriptions, operations, settings, dependencies).</summary>
    public static IReadOnlyDictionary<string, string> ToolTexts()
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tool in BuiltInTools.Pages.Select(p => p.Manifest))
        {
            Add($"Tool.{tool.Id}.Name", tool.Name);
            Add($"Tool.{tool.Id}.Description", tool.Description);
            if (!string.IsNullOrEmpty(tool.Limitations)) Add($"Tool.{tool.Id}.Limitations", tool.Limitations);
            Add("Group." + ToolText.KeyPart(tool.Category), tool.Category);
            foreach (var op in tool.Operations) Add($"Tool.{tool.Id}.Op.{op.Id}", op.Name);
            foreach (var setting in tool.Settings.Where(s => s.Visible))
            {
                Add($"Tool.{tool.Id}.Setting.{setting.Key}.Label", setting.Label);
                Add($"Tool.{tool.Id}.Setting.{setting.Key}.Help", setting.Help);
                foreach (var (value, label) in setting.Choices) Add($"Tool.{tool.Id}.Setting.{setting.Key}.Choice.{ToolText.KeyPart(value)}", label);
            }
            foreach (var dependency in tool.Dependencies)
            {
                Add($"Dependency.{dependency.Id}.Name", dependency.Name);
                Add($"Tool.{tool.Id}.Dependency.{dependency.Id}.Resolution", dependency.Resolution);
            }
        }
        return texts;

        void Add(string key, string text) => texts.TryAdd(key, text);
    }

    /// <summary>Writes the report; returns the number of problems (0 = every language complete).</summary>
    public static int Write(string file)
    {
        var withoutEntry = ToolTexts().Concat(L.DataKeys).Where(p => !L.English.ContainsKey(p.Key)).DistinctBy(p => p.Key).OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        string? folder = Path.GetDirectoryName(Path.GetFullPath(file));
        if (folder != null) Directory.CreateDirectory(folder);
        File.WriteAllText(file, LocalizationAudit.Report(withoutEntry));
        return withoutEntry.Count + Languages.All.Sum(l => LocalizationAudit.Check(l).Problems);
    }
}
