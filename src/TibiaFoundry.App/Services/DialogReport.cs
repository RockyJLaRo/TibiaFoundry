using TibiaFoundry.Core.Localization;
using System.Text;

namespace TibiaFoundry.App.Services;

/// <summary>How a value of a <see cref="DialogReport"/> is emphasized.</summary>
public enum ReportTone { Normal, Success, Warning, Danger, Muted }

/// <summary>One labeled value ("Version: 15.33.c9e45e"). A path is shown in a monospaced, selectable field with a copy button.</summary>
public sealed record ReportRow(string Label, string Value, ReportTone Tone = ReportTone.Normal, bool IsPath = false)
{
    public static ReportRow Path(string label, string path) => new(label, path, IsPath: true);
}

/// <summary>A small table (header, then rows with the same number of cells). The last columns are numbers when <see cref="NumericColumns"/> says so.</summary>
public sealed record DialogTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, int NumericColumns = 0);

/// <summary>
/// One section of a <see cref="DialogReport"/>: a heading, then (in this order) an introduction,
/// labeled values, a table, a bulleted list and a closing note. Empty parts are not shown.
/// </summary>
public sealed record ReportSection(string Heading)
{
    public string? Text { get; init; }
    public IReadOnlyList<ReportRow> Rows { get; init; } = [];
    public DialogTable? Table { get; init; }
    public IReadOnlyList<string> Bullets { get; init; } = [];
    public string? Note { get; init; }
    /// <summary>Shown collapsed (advanced or technical details); the heading opens it.</summary>
    public bool Collapsed { get; init; }
    /// <summary>The section's frame: warnings and failures stand out.</summary>
    public ReportTone Tone { get; init; }
}

/// <summary>
/// A structured message (8.6): a heading, a one-line summary and sections of labeled values,
/// tables and lists, with up to three buttons. It replaces long paragraphs in message boxes: the
/// facts are easy to scan, paths can be selected and copied, and "Copy details" copies everything
/// as text. <see cref="ToPlainText"/> is also what unattended runs log and what tests read.
/// </summary>
public sealed record DialogReport
{
    /// <summary>The window title.</summary>
    public required string Title { get; init; }
    /// <summary>The large first line ("Tibia Foundry is set up").</summary>
    public required string Heading { get; init; }
    /// <summary>One or two sentences under the heading.</summary>
    public string? Summary { get; init; }
    public MessageKind Kind { get; init; } = MessageKind.Info;
    public IReadOnlyList<ReportSection> Sections { get; init; } = [];
    public string PrimaryText { get; init; } = L.T("Dialog.OK");
    /// <summary>A second answer (returns <see cref="ReportAnswer.Secondary"/>), or null.</summary>
    public string? SecondaryText { get; init; }
    /// <summary>The cancel button (Esc, closing the window), or null for an information without a choice.</summary>
    public string? CancelText { get; init; }

    /// <summary>The whole report as text: for "Copy details", the log of unattended runs and tests.</summary>
    public string ToPlainText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Heading);
        if (!string.IsNullOrWhiteSpace(Summary)) sb.AppendLine(Summary);
        foreach (var section in Sections)
        {
            sb.AppendLine().AppendLine(section.Heading);
            if (!string.IsNullOrWhiteSpace(section.Text)) sb.AppendLine(section.Text);
            foreach (var row in section.Rows) sb.AppendLine($"  {row.Label}: {row.Value}");
            if (section.Table is { } table)
            {
                sb.AppendLine("  " + string.Join(" | ", table.Columns));
                foreach (var cells in table.Rows) sb.AppendLine("  " + string.Join(" | ", cells));
            }
            foreach (string bullet in section.Bullets) sb.AppendLine("  • " + bullet);
            if (!string.IsNullOrWhiteSpace(section.Note)) sb.AppendLine(section.Note);
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>The button that closed a <see cref="DialogReport"/>.</summary>
public enum ReportAnswer { Cancel, Primary, Secondary }
