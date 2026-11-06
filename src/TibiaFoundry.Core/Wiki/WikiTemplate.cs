using System.Text;

namespace TibiaFoundry.Core.Wiki;

/// <summary>Where the value of a wiki template parameter comes from (shown next to every field so nothing is mistaken for verified data).</summary>
public enum WikiFieldSource
{
    /// <summary>Read directly from the client's own data (appearances.dat).</summary>
    Client,
    /// <summary>Derived from client data by a documented rule (for example the classification from the market category); review before publishing.</summary>
    Derived,
    /// <summary>From TibiaWiki itself (the cached "Item IDs" table).</summary>
    TibiaWiki,
    /// <summary>A structural placeholder the template expects (for example <c>{{Dropped By|}}</c>); no data.</summary>
    Placeholder,
    /// <summary>Not known to Tibia Foundry: the template lists the parameter as required, the editor fills it in.</summary>
    Missing,
    /// <summary>Typed or changed by the user.</summary>
    User,
}

/// <summary>One named parameter of a MediaWiki template call.</summary>
/// <param name="Required">The template puts pages without this parameter into a "Parameter Required" maintenance category.</param>
/// <param name="Multiline">Free text that may span lines (notes, history); other values are kept on one line.</param>
public sealed record WikiField(string Name, string Value, WikiFieldSource Source, bool Required = false, bool Multiline = false, string? Hint = null);

/// <summary>
/// A MediaWiki template call: the template name, fixed leading arguments and ordered named parameters.
/// Exporters (Infobox Object today; creatures, NPCs, other templates later) only build this; <see cref="WikiTemplateWriter"/> formats it.
/// </summary>
public sealed record WikiTemplate(string TemplateName, IReadOnlyList<string> LeadingArguments, IReadOnlyList<WikiField> Fields, IReadOnlyList<string> Warnings)
{
    public WikiField? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);

    /// <summary>Returns a copy with one parameter's value replaced (marked as entered by the user), or added at the end.</summary>
    public WikiTemplate With(string name, string value)
    {
        var list = Fields.ToList();
        int i = list.FindIndex(f => f.Name == name);
        if (i >= 0) list[i] = list[i] with { Value = value, Source = WikiFieldSource.User };
        else list.Add(new WikiField(name, value, WikiFieldSource.User));
        return this with { Fields = list };
    }
}

/// <summary>Options of <see cref="WikiTemplateWriter.Write"/>.</summary>
/// <param name="IncludeEmptyRequired">Write required parameters without a value as <c>| name = </c> so the editor sees what is still missing.</param>
/// <param name="UnknownPlaceholder">Written for empty required parameters (TibiaWiki uses <c>?</c> for "unknown"); empty leaves them blank.</param>
/// <param name="AlignEquals">Pad parameter names so the equals signs line up, as TibiaWiki's item pages do.</param>
public sealed record WikiWriteOptions(bool IncludeEmptyRequired = true, string UnknownPlaceholder = "", bool AlignEquals = true)
{
    public static WikiWriteOptions Default { get; } = new();
}

/// <summary>Formats a <see cref="WikiTemplate"/> as MediaWiki markup.</summary>
public static class WikiTemplateWriter
{
    /// <summary>
    /// <c>{{Template|lead|lead</c>, one <c>| name = value</c> line per parameter, <c>}}</c>. Values are made safe for a
    /// template argument: a literal "|" becomes <c>{{!}}</c> (outside links and nested templates), a stray "}}" is
    /// escaped, single-line values lose their line breaks. Parameters without a value are left out unless required.
    /// </summary>
    public static string Write(WikiTemplate template, WikiWriteOptions? options = null)
    {
        options ??= WikiWriteOptions.Default;
        var lines = new List<(string Name, string Value)>();
        foreach (var f in template.Fields)
        {
            string value = Clean(f.Value, f.Multiline);
            if (value.Length == 0)
            {
                if (!f.Required || !options.IncludeEmptyRequired) continue;
                value = options.UnknownPlaceholder;
            }
            lines.Add((f.Name, value));
        }
        int width = options.AlignEquals && lines.Count > 0 ? Math.Max(13, lines.Max(l => l.Name.Length)) : 0;
        var sb = new StringBuilder("{{").Append(template.TemplateName);
        foreach (string lead in template.LeadingArguments) sb.Append('|').Append(lead);
        sb.Append('\n');
        foreach (var (name, value) in lines)
        {
            sb.Append("| ").Append(options.AlignEquals ? name.PadRight(width) : name).Append(" =");
            if (value.Length > 0) sb.Append(' ').Append(value);
            sb.Append('\n');
        }
        sb.Append("}}");
        return sb.ToString();
    }

    /// <summary>Makes a value safe as a named template argument.</summary>
    public static string Clean(string? value, bool multiline)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string v = value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (!multiline) v = string.Join(" ", v.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return EscapePipes(v, allowTemplates: true);
    }

    /// <summary>
    /// Escapes "|" that would end the argument and "}}" that would end the template, but leaves
    /// <c>[[link|text]]</c> and complete nested templates (<c>{{Dropped By|A|B}}</c>) intact.
    /// </summary>
    private static string EscapePipes(string v, bool allowTemplates)
    {
        var sb = new StringBuilder(v.Length + 8);
        int links = 0, templates = 0;
        for (int i = 0; i < v.Length; i++)
        {
            char c = v[i];
            char next = i + 1 < v.Length ? v[i + 1] : '\0';
            if (c == '[' && next == '[') { links++; sb.Append("[["); i++; continue; }
            if (c == ']' && next == ']' && links > 0) { links--; sb.Append("]]"); i++; continue; }
            if (c == '{' && next == '{')
            {
                if (allowTemplates) { templates++; sb.Append("{{"); }
                else sb.Append("&#123;&#123;");
                i++;
                continue;
            }
            if (c == '}' && next == '}')
            {
                if (templates > 0) { templates--; sb.Append("}}"); }
                else sb.Append("&#125;&#125;");
                i++;
                continue;
            }
            if (c == '|' && links == 0 && templates == 0) { sb.Append("{{!}}"); continue; }
            sb.Append(c);
        }
        // An unclosed "{{" would swallow the rest of the infobox: write every brace pair literally instead.
        return templates > 0 && allowTemplates ? EscapePipes(v, allowTemplates: false) : sb.ToString();
    }
}
