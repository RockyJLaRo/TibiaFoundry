using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Tools;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Scripts;

/// <summary>Record of one rename run, kept so it can be undone.</summary>
public sealed class RenameRecord
{
    public DateTime WhenUtc { get; set; }
    public string Folder { get; set; } = "";
    public List<RenamePair> Renames { get; set; } = [];
}

public sealed record RenamePair(string From, string To);

/// <summary>Script 3: renames groups of files after a preview.</summary>
public sealed class BatchRenamerScript : Script
{
    public const string RecordFolderName = "rename-records";

    public override string Id => "batch-rename";
    public override string Name => L.T("BatchRenamerScript.BatchFileRenamer");
    public override string Description => L.T("BatchRenamerScript.RenamesManyFilesAtOncePrefix");
    public override ScriptCategory Category => ScriptCategory.BatchProcessing;
    public override string SupportedInputs => L.T("BatchRenamerScript.AnyFiles");
    public override string Limitations => L.T("BatchRenamerScript.ChangingAnExtensionDoesNotConvert");
    public override bool ChangesFiles => true;

    protected override ToolCapabilities ExtraCapabilities => ToolCapabilities.Undo;

    public override IReadOnlyList<ScriptParameter> Parameters { get; } =
    [
        new("mode", L.T("Label.Task"), ScriptParameterKind.Choice, L.T("BatchRenamerScript.RenameFilesOrUndoAnEarlier"))
        {
            Default = "rename", Choices = [new("rename", L.T("BatchRenamerScript.RenameFiles")), new("undo", L.T("BatchRenamerScript.UndoAnEarlierRename"))],
        },
        FolderParameter(help: L.T("BatchRenamerScript.TheFolderWhoseFilesAreRenamed")) with { VisibleWhen = "mode=rename" },
        RecursiveParameter(false) with { VisibleWhen = "mode=rename" },
        new("filter", L.T("Common.Files"), ScriptParameterKind.Text, L.T("BatchRenamerScript.WhichFilesToRenameEG")) { Default = "*", VisibleWhen = "mode=rename" },
        new("find", L.T("Label.Find"), ScriptParameterKind.Text, L.T("BatchRenamerScript.TextToReplaceInTheName")) { Required = false, VisibleWhen = "mode=rename" },
        new("replace", L.T("BatchRenamerScript.ReplaceWith"), ScriptParameterKind.Text, L.T("BatchRenamerScript.ReplacementTextMayBeEmpty")) { Required = false, VisibleWhen = "mode=rename" },
        new("regex", L.T("BatchRenamerScript.FindIsARegularExpression"), ScriptParameterKind.Boolean, L.T("BatchRenamerScript.UseNETRegularExpressionSyntax1")) { Default = "false", Advanced = true, VisibleWhen = "mode=rename" },
        new("prefix", L.T("BatchRenamerScript.AddBefore"), ScriptParameterKind.Text, L.T("BatchRenamerScript.TextAddedAtTheStartOf")) { Required = false, VisibleWhen = "mode=rename" },
        new("suffix", L.T("BatchRenamerScript.AddAfter"), ScriptParameterKind.Text, L.T("BatchRenamerScript.TextAddedAtTheEndOf")) { Required = false, VisibleWhen = "mode=rename" },
        new("case", L.T("BatchRenamerScript.LetterCase"), ScriptParameterKind.Choice, L.T("BatchRenamerScript.ChangeTheLetterCaseOfThe"))
        {
            Default = "keep", VisibleWhen = "mode=rename",
            Choices = [new("keep", L.T("Clients.Keep")), new("lower", L.T("BatchRenamerScript.LowerCase")), new("upper", L.T("BatchRenamerScript.UPPERCASE")), new("title", L.T("BatchRenamerScript.TitleCase"))],
        },
        new("spaces", L.T("Label.Spaces"), ScriptParameterKind.Choice, L.T("BatchRenamerScript.CleanUpSpacesInTheName"))
        {
            Default = "keep", VisibleWhen = "mode=rename",
            Choices = [new("keep", L.T("Clients.Keep")), new("trim", L.T("BatchRenamerScript.TrimAndCollapseRepeatedSpaces")), new("underscore", L.T("BatchRenamerScript.ReplaceSpacesWith")), new("wiki", L.T("BatchRenamerScript.TibiaWikiStyleSpacesFirstLetterUpper"))],
        },
        new("number", L.T("Label.Numbering"), ScriptParameterKind.Choice, L.T("BatchRenamerScript.AddARunningNumber"))
        {
            Default = "none", VisibleWhen = "mode=rename",
            Choices = [new("none", L.T("BatchRenamerScript.NoNumbers")), new("prefix", L.T("BatchRenamerScript.NumberBeforeTheName")), new("suffix", L.T("BatchRenamerScript.NumberAfterTheName")), new("replace", L.T("BatchRenamerScript.ReplaceTheNameWithTheNumber"))],
        },
        new("start", L.T("BatchRenamerScript.FirstNumber"), ScriptParameterKind.Integer, L.T("BatchRenamerScript.NumberingStartsHere")) { Default = "1", Minimum = 0, Maximum = 999999999, Advanced = true, VisibleWhen = "mode=rename" },
        new("padding", L.T("Label.Digits"), ScriptParameterKind.Integer, L.T("BatchRenamerScript.PadNumbersWithZerosToThis")) { Default = "0", Minimum = 0, Maximum = 12, Advanced = true, VisibleWhen = "mode=rename" },
        new("extension", L.T("BatchRenamerScript.NewExtension"), ScriptParameterKind.Text, L.T("BatchRenamerScript.ForExamplePngAppliedOnlyWhen")) { Required = false, Advanced = true, VisibleWhen = "mode=rename" },
        new("record", L.T("BatchRenamerScript.RenameToUndo"), ScriptParameterKind.Choice, L.T("BatchRenamerScript.AnEarlierRenameMadeWithThis")) { VisibleWhen = "mode=undo" },
    ];

    /// <summary>Folder for undo records (set by the application: the backup folder).</summary>
    public string RecordFolder { get; init; } = Path.Combine(Path.GetTempPath(), AppIdentity.Id, RecordFolderName);

    private IReadOnlyList<ScriptChoice> RecordChoices()
    {
        if (!Directory.Exists(RecordFolder)) return [];
        return Directory.GetFiles(RecordFolder, "rename-*.json").OrderByDescending(f => f).Take(50).Select(f =>
        {
            try
            {
                var r = JsonSerializer.Deserialize<RenameRecord>(File.ReadAllText(f));
                return new ScriptChoice(f, L.F("BatchRenamerScript.0YyyyMMDdHHMm", r!.WhenUtc.ToLocalTime(), L.Count(r.Renames.Count, "Noun.file"), r.Folder));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return new ScriptChoice(f, Path.GetFileName(f) + " (unreadable)");
            }
        }).ToList();
    }

    public override IReadOnlyList<string> Validate(ScriptArguments args)
    {
        var problems = new List<string>();
        foreach (var p in base.Validate(args))
            if (!(p.StartsWith(L.T("BatchRenamerScript.RenameToUndo"), StringComparison.Ordinal) && args.Choice("mode") == "undo" && File.Exists(args.Text("record")))) problems.Add(p);
        if (args.Choice("mode") == "undo")
        {
            if (!File.Exists(args.Text("record")) && problems.Count == 0) problems.Add(L.T("BatchRenamerScript.ChooseARenameToUndo"));
            return problems;
        }
        if (args.Bool("regex") && args.Text("find").Length > 0)
        {
            try { _ = new Regex(args.Raw("find")!, RegexOptions.None, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException ex) { problems.Add(L.T("BatchRenamerScript.FindNotAValidRegularExpression") + ex.Message + ")."); }
        }
        string ext = args.Text("extension");
        if (ext.Length > 0 && (PathGuard.SafeRelativePath("x" + (ext.StartsWith('.') ? ext : "." + ext)) == null || ext.Trim('.').Contains('.')))
            problems.Add(L.T("BatchRenamerScript.NewExtensionNotAValidExtension"));
        bool anything = args.Text("find").Length > 0 || args.Text("prefix").Length > 0 || args.Text("suffix").Length > 0 || args.Choice("case") is not ("keep" or "")
                        || args.Choice("spaces") is not ("keep" or "") || args.Choice("number") is not ("none" or "") || ext.Length > 0;
        if (!anything) problems.Add(L.T("BatchRenamerScript.ChooseAtLeastOneChangeFind"));
        return problems;
    }

    public override ScriptResult Run(ScriptContext context, CancellationToken ct) =>
        context.Args.Choice("mode") == "undo" ? PreviewUndo(context) : PreviewRename(context, ct);

    private ScriptResult PreviewRename(ScriptContext context, CancellationToken ct)
    {
        var args = context.Args;
        string folder = args.PathValue("folder")!;
        var patterns = FileScanner.ParsePatterns(args.Text("filter"));
        var scan = FileScanner.Scan(folder, args.Bool("recursive"), ct: ct);
        var files = scan.Files.Where(f => patterns.Count == 0 || patterns.Any(p => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(p, f.Name))).ToList();
        Regex? regex = args.Bool("regex") && args.Text("find").Length > 0 ? new Regex(args.Raw("find")!, RegexOptions.None, TimeSpan.FromSeconds(1)) : null;
        long number = args.Long("start", 1);
        int padding = args.Int("padding");
        string newExt = args.Text("extension");
        if (newExt.Length > 0 && !newExt.StartsWith('.')) newExt = "." + newExt;
        var proposals = new List<(FileInfo File, string NewName, string Note)>();
        int unchanged = 0;
        foreach (var file in files)
        {
            string stem = Path.GetFileNameWithoutExtension(file.Name);
            string ext = file.Extension;
            string note = "";
            if (args.Text("find").Length > 0)
                stem = regex != null ? regex.Replace(stem, args.Raw("replace") ?? "") : stem.Replace(args.Raw("find")!, args.Raw("replace") ?? "", StringComparison.Ordinal);
            stem = args.Choice("spaces") switch
            {
                "trim" => Regex.Replace(stem.Trim(), " {2,}", " "),
                "underscore" => stem.Trim().Replace(' ', '_'),
                "wiki" => Wiki(stem),
                _ => stem,
            };
            stem = args.Choice("case") switch
            {
                "lower" => stem.ToLowerInvariant(),
                "upper" => stem.ToUpperInvariant(),
                "title" => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(stem.ToLowerInvariant()),
                _ => stem,
            };
            stem = args.Raw("prefix") + stem + args.Raw("suffix");
            string n = number.ToString(CultureInfo.InvariantCulture).PadLeft(padding, '0');
            switch (args.Choice("number"))
            {
                case "prefix": stem = n + stem; number++; break;
                case "suffix": stem += n; number++; break;
                case "replace": stem = n; number++; break;
            }
            if (newExt.Length > 0 && !string.Equals(newExt, ext, StringComparison.OrdinalIgnoreCase))
            {
                var format = FileSignatures.Identify(file.FullName);
                if (format.Format != null && format.Evidence == FormatEvidence.Signature && format.Format.Extensions.Contains(newExt.ToLowerInvariant())) ext = newExt;
                else note = L.F("BatchRenamerScript.ExtensionKeptTheContentIs0", (format.Format?.Name ?? L.T("BatchRenamerScript.OfAnUnknownFormat")), newExt);
            }
            if (stem + ext == file.Name)
            {
                unchanged++;
                if (note.Length > 0) proposals.Add((file, file.Name, note));
                continue;
            }
            proposals.Add((file, stem + ext, note));
        }

        // Conflicts: invalid names, two files with the same new name, and names already taken by files
        // that keep their name (a name freed by another renamed file is fine: renames go through temporary names).
        var table = new ReportTable(L.T("BatchRenamerScript.ProposedNames"), [L.T("BatchRenamerScript.CurrentName"), L.T("BatchRenamerScript.NewName"), L.T("Common.Folder"), L.T("Common.Note")]);
        var renamed = proposals.Where(x => x.NewName != x.File.Name).ToList();
        var problems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in renamed.GroupBy(x => Path.Combine(x.File.DirectoryName!, x.NewName), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var x in group) problems[x.File.FullName] = L.F("BatchRenamerScript.0FilesWouldGetThisName", group.Count());
        foreach (var x in renamed)
            if (PathGuard.SafeRelativePath(x.NewName, out string? why) == null) problems[x.File.FullName] = L.T("BatchRenamerScript.InvalidName") + why;
        bool changed = true;
        while (changed)
        {
            changed = false;
            var moving = new HashSet<string>(renamed.Where(x => !problems.ContainsKey(x.File.FullName)).Select(x => x.File.FullName), StringComparer.OrdinalIgnoreCase);
            foreach (var x in renamed.Where(x => !problems.ContainsKey(x.File.FullName)))
            {
                string target = Path.Combine(x.File.DirectoryName!, x.NewName);
                bool sameFile = string.Equals(target, x.File.FullName, StringComparison.OrdinalIgnoreCase); // a change of letter case only
                if (Directory.Exists(target)) problems[x.File.FullName] = L.T("BatchRenamerScript.AFolderWithThisNameExists");
                else if (!sameFile && File.Exists(target) && !moving.Contains(target)) problems[x.File.FullName] = L.T("BatchRenamerScript.AFileWithThisNameAlready");
                else continue;
                changed = true;
            }
        }
        var plan = new List<PlannedChange>();
        foreach (var (file, newName, note) in proposals)
        {
            if (newName == file.Name)
            {
                table.Add(file.Name, newName, file.DirectoryName!, note);
                continue;
            }
            string target = Path.Combine(file.DirectoryName!, newName);
            bool isBlocked = problems.TryGetValue(file.FullName, out string? problem);
            plan.Add(new PlannedChange { Action = L.T("Clients.Rename"), Source = file.FullName, Target = target, Detail = problem ?? note, Blocked = isBlocked, Selected = !isBlocked });
            table.Add(file.Name, newName, file.DirectoryName!, isBlocked ? L.T("BatchRenamerScript.NOTRenamed") + problem : note);
        }
        int blocked = plan.Count(c => c.Blocked);
        var result = new ScriptResult
        {
            Table = table,
            Plan = plan,
            Processed = files.Count,
            Summary = L.F("BatchRenamerScript.0WouldBeRenamed1N0", L.Count(plan.Count(c => !c.Blocked), "Noun.file"), blocked, unchanged),
        };
        result.Notes.AddRange(FileScanner.Notes(scan));
        return result;
    }

    private static string Wiki(string stem)
    {
        string s = Regex.Replace(stem.Trim(), "[ _]+", "_");
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    private static ScriptResult PreviewUndo(ScriptContext context)
    {
        var record = JsonSerializer.Deserialize<RenameRecord>(File.ReadAllText(context.Args.Text("record")))
                     ?? throw new InvalidDataException("The rename record is empty.");
        var plan = new List<PlannedChange>();
        var table = new ReportTable(L.T("BatchRenamerScript.UndoRename"), [L.T("BatchRenamerScript.CurrentName"), L.T("BatchRenamerScript.OriginalName"), L.T("Common.Note")]);
        foreach (var pair in record.Renames)
        {
            string note = !File.Exists(pair.To) ? L.T("BatchRenamerScript.NoLongerExists") : File.Exists(pair.From) ? L.T("BatchRenamerScript.TheOriginalNameIsInUse") : "";
            plan.Add(new PlannedChange { Action = L.T("BatchRenamerScript.RenameBack"), Source = pair.To, Target = pair.From, Detail = note, Blocked = note.Length > 0, Selected = note.Length == 0 });
            table.Add(pair.To, pair.From, note);
        }
        return new ScriptResult
        {
            Table = table,
            Plan = plan,
            Processed = plan.Count,
            Summary = L.F("BatchRenamerScript.0N0Of1CanGet", plan.Count(c => !c.Blocked), L.Count(plan.Count, "Noun.file")),
        };
    }

    /// <summary>
    /// Renames in two steps (each file first gets a unique temporary name), so names that swap or
    /// shift do not collide. A record of the renames is saved for undo before the second step.
    /// </summary>
    public override ScriptResult Apply(ScriptContext context, IReadOnlyList<PlannedChange> changes, CancellationToken ct)
    {
        var selected = changes.Where(c => c.Selected && !c.Blocked && c.Target != null).ToList();
        var result = new ScriptResult();
        var staged = new List<(PlannedChange Change, string Temp)>();
        foreach (var change in selected)
        {
            ct.ThrowIfCancellationRequested();
            result.Processed++;
            try
            {
                string temp = Path.Combine(Path.GetDirectoryName(change.Source)!, $".tf-rename-{Guid.NewGuid():N}.tmp");
                File.Move(change.Source, temp);
                staged.Add((change, temp));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Failed++;
                result.Errors.Add($"{change.Source}: {ex.Message}");
            }
        }
        var record = new RenameRecord { WhenUtc = DateTime.UtcNow, Folder = context.Args.PathValue("folder") ?? "" };
        int i = 0;
        foreach (var (change, temp) in staged)
        {
            context.Report(i++, staged.Count, Path.GetFileName(change.Target!));
            try
            {
                if (File.Exists(change.Target!)) throw new IOException("a file with the new name appeared in the meantime");
                File.Move(temp, change.Target!);
                record.Renames.Add(new RenamePair(change.Source, change.Target!));
                result.Succeeded++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Put the file back under its old name.
                try { File.Move(temp, change.Source); } catch (IOException) { result.Errors.Add(L.F("BatchRenamerScript.0LeftAs1RenameIt", change.Source, temp)); }
                result.Failed++;
                result.Errors.Add($"{change.Source}: {ex.Message}");
            }
        }
        if (record.Renames.Count > 0 && context.Args.Choice("mode") != "undo")
        {
            Directory.CreateDirectory(RecordFolder);
            string file = Path.Combine(RecordFolder, $"rename-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.json");
            AtomicFile.WriteAllText(file, JsonSerializer.Serialize(record, ReportWriter.JsonOptions));
            result.OutputFiles.Add(file);
            result.Notes.Add(L.T("BatchRenamerScript.ToUndoThisRenameChooseUndo"));
        }
        result.ChangesApplied = result.Succeeded > 0;
        result.Summary = L.F("BatchRenamerScript.0N0Of1Renamed2", result.Succeeded, L.Count(selected.Count, "Noun.file"), (result.Failed > 0 ? L.F("BatchRenamerScript.0N0Failed", result.Failed) : ""));
        return result;
    }

    public BatchRenamerScript()
    {
        // The record list depends on the files present when the script is shown.
        var list = Parameters.ToList();
        int index = list.FindIndex(p => p.Key == "record");
        list[index] = list[index] with { ChoicesProvider = RecordChoices };
        Parameters = list;
    }
}
