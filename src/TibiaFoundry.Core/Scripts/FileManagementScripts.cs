using TibiaFoundry.Core.Common;
using System.Collections.Concurrent;
using System.Globalization;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Scripts;

/// <summary>Moving files out of the way, reversibly (shared by the clean-up scripts).</summary>
internal static class Removal
{
    /// <summary>
    /// Moves the selected files into one backup set (kind "Removed") and deletes the selected empty
    /// folders after recording them, so Backup and Restore can put everything back. A file whose
    /// content changed since the preview (different SHA-256 in <see cref="PlannedChange.Check"/>) is
    /// left alone.
    /// </summary>
    public static ScriptResult Apply(ScriptContext context, string operation, IReadOnlyList<PlannedChange> changes, CancellationToken ct)
    {
        var result = new ScriptResult();
        var selected = changes.Where(c => c.Selected && !c.Blocked).ToList();
        using var set = context.Backups.Begin(operation, BackupKind.Removed);
        int i = 0;
        // Files first, then folders deepest first (a folder may contain removed empty folders).
        foreach (var change in selected.OrderBy(c => Directory.Exists(c.Source) ? 1 : 0).ThenByDescending(c => c.Source.Length))
        {
            ct.ThrowIfCancellationRequested();
            context.Report(i++, selected.Count, Path.GetFileName(change.Source));
            result.Processed++;
            try
            {
                if (Directory.Exists(change.Source))
                {
                    if (Directory.EnumerateFileSystemEntries(change.Source).Any())
                    {
                        result.Failed++;
                        result.Errors.Add(L.F("FileManagementScripts.0NoLongerEmptyLeftIn", change.Source));
                        continue;
                    }
                    set.AddEmptyDirectory(change.Source);
                    Directory.Delete(change.Source);
                }
                else if (File.Exists(change.Source))
                {
                    if (change.Check != null && FileHashing.HashFile(change.Source, ct: ct) != change.Check)
                    {
                        result.Failed++;
                        result.Errors.Add(L.F("FileManagementScripts.0ChangedSinceThePreviewLeft", change.Source));
                        continue;
                    }
                    set.Add(change.Source, move: true, ct: ct);
                }
                else
                {
                    result.Failed++;
                    result.Errors.Add(L.F("FileManagementScripts.0NoLongerExists", change.Source));
                    continue;
                }
                result.Succeeded++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Failed++;
                result.Errors.Add($"{change.Source}: {ex.Message}");
                if (context.StopOnError) break;
            }
        }
        set.Complete();
        result.ChangesApplied = result.Succeeded > 0;
        result.BackupFolder = set.Folder;
        result.Summary = L.F("FileManagementScripts.0N0Of1MovedTo", result.Succeeded, L.Count(selected.Count, "Noun.item"), (result.Failed > 0 ? L.F("FileManagementScripts.0N0LeftInPlace", result.Failed) : ""));
        return result;
    }
}

/// <summary>Script 1: finds files with identical content.</summary>
public sealed class DuplicateFileFinderScript : Script
{
    public override string Id => "duplicate-finder";
    public override string Name => L.T("FileManagementScripts.DuplicateFileFinder");
    public override string Description => L.T("FileManagementScripts.FindsFilesWithIdenticalContentCandidates");
    public override ScriptCategory Category => ScriptCategory.FileManagement;
    public override string SupportedInputs => L.T("FileManagementScripts.AnyFiles");
    public override string Limitations => L.T("FileManagementScripts.FilesAreComparedByContentOnly");
    public override bool ChangesFiles => true;
    public override bool WillChangeFiles(ScriptArguments args) => args.Choice("action") == "move";

    public override IReadOnlyList<ScriptParameter> Parameters { get; } =
    [
        FolderParameter(),
        RecursiveParameter(),
        new("action", L.T("Label.Then"), ScriptParameterKind.Choice, L.T("FileManagementScripts.OnlyListTheDuplicatesOrAlso"))
        {
            Default = "report",
            Choices = [new("report", L.T("FileManagementScripts.OnlyListDuplicates")), new("move", L.T("FileManagementScripts.PrepareMovingExtraCopiesToThe"))],
        },
        new("keep", L.T("FileManagementScripts.CopyToKeep"), ScriptParameterKind.Choice, L.T("FileManagementScripts.WhichCopyOfEachGroupStays"))
        {
            Default = "oldest", VisibleWhen = "action=move",
            Choices = [new("oldest", L.T("FileManagementScripts.TheOldestFile")), new("newest", L.T("FileManagementScripts.TheNewestFile")), new("shortest", L.T("FileManagementScripts.TheShortestPath")), new("first", L.T("FileManagementScripts.TheFirstInAlphabeticalOrder"))],
        },
        new("minSize", L.T("FileManagementScripts.MinimumSizeBytes"), ScriptParameterKind.Integer, L.T("FileManagementScripts.SmallerFilesAreIgnoredEmptyFiles")) { Default = "1", Minimum = 1, Advanced = true },
        new("exclude", L.T("FileManagementScripts.LeaveOut"), ScriptParameterKind.Text, L.T("FileManagementScripts.NamesOrPatternsSeparatedByE")) { Required = false, Advanced = true, Default = L.T("FileManagementScripts.GitThumbsDbDesktopIni") },
    ];

    public override ScriptResult Run(ScriptContext context, CancellationToken ct)
    {
        var args = context.Args;
        string folder = args.PathValue("folder")!;
        var scan = FileScanner.Scan(folder, args.Bool("recursive"), FileScanner.ParsePatterns(args.Text("exclude")), ct: ct);
        long minSize = Math.Max(1, args.Long("minSize", 1));
        var bySize = scan.Files.Where(f => f.Length >= minSize).GroupBy(f => f.Length).Where(g => g.Count() > 1).ToList();
        var candidates = bySize.SelectMany(g => g).ToList();
        var hashes = new ConcurrentDictionary<string, string>();
        var errors = new ConcurrentBag<string>();
        int done = 0;
        Parallel.ForEach(candidates, new ParallelOptions { MaxDegreeOfParallelism = context.WorkerThreads, CancellationToken = ct }, file =>
        {
            try
            {
                hashes[file.FullName] = FileHashing.HashFile(file.FullName, ct: ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{file.FullName}: {ex.Message}");
            }
            int n = Interlocked.Increment(ref done);
            if (n % 50 == 0) context.Report(n, candidates.Count, L.T("FileManagementScripts.ComparingContent"));
        });
        var groups = candidates.Where(f => hashes.ContainsKey(f.FullName))
            .GroupBy(f => (f.Length, Hash: hashes[f.FullName])).Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Key.Length * (g.Count() - 1)).ToList();

        var table = new ReportTable(L.T("FileManagementScripts.DuplicateFiles"), [L.T("Label.Group"), L.T("Common.Size"), "SHA-256", L.T("Common.File"), L.T("Choice.Sort.Modified"), L.T("Label.Kept")]);
        var plan = new List<PlannedChange>();
        bool move = args.Choice("action") == "move";
        int groupNo = 0;
        long wasted = 0;
        foreach (var g in groups)
        {
            groupNo++;
            var keep = args.Choice("keep") switch
            {
                "newest" => g.OrderByDescending(f => f.LastWriteTimeUtc).First(),
                "shortest" => g.OrderBy(f => f.FullName.Length).ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).First(),
                "first" => g.OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).First(),
                _ => g.OrderBy(f => f.LastWriteTimeUtc).First(),
            };
            wasted += g.Key.Length * (g.Count() - 1);
            foreach (var f in g.OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase))
            {
                bool kept = f.FullName == keep.FullName;
                table.Add(groupNo.ToString(CultureInfo.InvariantCulture), f.Length.ToString(CultureInfo.InvariantCulture), g.Key.Hash, f.FullName,
                    f.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), kept ? "yes" : "");
                if (move && !kept)
                    plan.Add(new PlannedChange { Action = L.T("FileManagementScripts.MoveToBackup"), Source = f.FullName, Detail = L.F("FileManagementScripts.CopyOf0", keep.FullName), Check = g.Key.Hash });
            }
        }
        var result = new ScriptResult
        {
            Table = table,
            Processed = scan.Files.Count,
            Succeeded = scan.Files.Count - errors.Count,
            Failed = errors.Count,
            Plan = move ? plan : null,
            Summary = groups.Count == 0
                ? L.F("FileManagementScripts.NoDuplicatesAmong0", L.Count(scan.Files.Count, "Noun.file"))
                : L.F("FileManagementScripts.0OfIdenticalFiles1N0", L.Count(groups.Count, "Noun.group"), groups.Sum(g => g.Count() - 1), ReportWriter.FormatSize(wasted)) +
                  (move ? L.T("FileManagementScripts.ReviewTheListAndApplyTo") : ""),
        };
        result.Errors.AddRange(errors);
        result.Notes.AddRange(FileScanner.Notes(scan));
        return result;
    }

    public override ScriptResult Apply(ScriptContext context, IReadOnlyList<PlannedChange> changes, CancellationToken ct)
    {
        // Never remove every copy of a group: the kept file is not in the plan, but check it still exists.
        foreach (var change in changes.Where(c => c.Selected))
        {
            string keptPath = change.Detail["copy of ".Length..];
            if (!File.Exists(keptPath) || FileHashing.HashFile(keptPath, ct: ct) != change.Check)
                throw new InvalidOperationException($"The copy to keep ({keptPath}) is missing or changed since the preview. Run the script again before applying.");
        }
        return Removal.Apply(context, L.T("FileManagementScripts.DuplicateFileFinder"), changes, ct);
    }
}

/// <summary>Script 6: lists the largest files and folders.</summary>
public sealed class LargeFileFinderScript : Script
{
    public override string Id => "large-files";
    public override string Name => L.T("FileManagementScripts.LargeFileFinder");
    public override string Description => L.T("FileManagementScripts.ListsFilesAboveASizeThreshold");
    public override ScriptCategory Category => ScriptCategory.FileManagement;
    public override string SupportedInputs => L.T("FileManagementScripts.AnyFiles");
    public override string Limitations => L.T("FileManagementScripts.SizesAreFileSizesNotSpace");

    public override IReadOnlyList<ScriptParameter> Parameters { get; } =
    [
        FolderParameter(),
        RecursiveParameter(),
        new("minMb", L.T("FileManagementScripts.MinimumSizeMB"), ScriptParameterKind.Integer, L.T("FileManagementScripts.FilesSmallerThanThisAreNot")) { Default = "100", Minimum = 0, Maximum = 1024 * 1024 },
        new("top", L.T("FileManagementScripts.MaximumFilesListed"), ScriptParameterKind.Integer, L.T("FileManagementScripts.OnlyTheLargestOnesAreListed")) { Default = "200", Minimum = 1, Maximum = 100000, Advanced = true },
        new("folders", L.T("FileManagementScripts.SummarizeSubfolders"), ScriptParameterKind.Boolean, L.T("FileManagementScripts.AlsoListTheTenSubfoldersWith")) { Default = "true" },
    ];

    public override ScriptResult Run(ScriptContext context, CancellationToken ct)
    {
        var args = context.Args;
        string folder = args.PathValue("folder")!;
        var scan = FileScanner.Scan(folder, args.Bool("recursive"), ct: ct);
        long threshold = args.Long("minMb", 100) * 1024 * 1024;
        var large = scan.Files.Where(f => f.Length >= threshold).OrderByDescending(f => f.Length).Take(args.Int("top", 200)).ToList();
        var table = new ReportTable(L.T("FileManagementScripts.LargeFiles"), [L.T("Common.Size"), L.T("FileManagementScripts.SizeBytes"), L.T("Common.File"), L.T("Choice.Sort.Modified")]);
        foreach (var f in large)
            table.Add(ReportWriter.FormatSize(f.Length), f.Length.ToString(CultureInfo.InvariantCulture), f.FullName, f.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        long total = scan.Files.Sum(f => f.Length);
        var result = new ScriptResult
        {
            Table = table,
            Processed = scan.Files.Count,
            Succeeded = scan.Files.Count,
            Summary = L.F("FileManagementScripts.0OfAtLeast1The", L.Count(large.Count, "Noun.file"), ReportWriter.FormatSize(threshold), L.Count(scan.Files.Count, "Noun.file"), ReportWriter.FormatSize(total)),
        };
        if (args.Bool("folders"))
        {
            var bySub = scan.Files.GroupBy(f =>
            {
                string rel = Path.GetRelativePath(folder, f.FullName);
                int sep = rel.IndexOf(Path.DirectorySeparatorChar);
                return sep < 0 ? L.T("FileManagementScripts.FilesDirectlyInTheFolder") : rel[..sep];
            }).Select(g => (Name: g.Key, Size: g.Sum(f => f.Length), Count: g.Count())).OrderByDescending(x => x.Size).Take(10);
            foreach (var (name, size, count) in bySub)
                table.Summary.Add(L.F("FileManagementScripts.01In2", name, ReportWriter.FormatSize(size), L.Count(count, "Noun.file")));
            result.Notes.Add(L.T("FileManagementScripts.LargestSubfoldersAreListedAboveThe"));
        }
        result.Notes.AddRange(FileScanner.Notes(scan));
        return result;
    }
}

/// <summary>Script 7: finds empty files and folders.</summary>
public sealed class EmptyEntryFinderScript : Script
{
    /// <summary>Folder names that programs expect to exist even when empty; never proposed for removal.</summary>
    public static readonly IReadOnlySet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".idea", "node_modules", "logs", "log", "cache", "temp", "tmp", "backups",
        "screenshots", "characterdata", "minimap", "conf", "crashdump", "storeimages", "assets", "sounds", "bin",
        "$RECYCLE.BIN", "System Volume Information",
    };

    public override string Id => "empty-finder";
    public override string Name => L.T("FileManagementScripts.EmptyFilesAndFolders");
    public override string Description => L.T("FileManagementScripts.FindsFilesOfZeroBytesAnd");
    public override ScriptCategory Category => ScriptCategory.FileManagement;
    public override string SupportedInputs => L.T("FileManagementScripts.AnyFolder");
    public override string Limitations => L.T("FileManagementScripts.AFolderCountsAsEmptyOnly");
    public override bool ChangesFiles => true;
    public override bool WillChangeFiles(ScriptArguments args) => args.Choice("action") == "move";

    public override IReadOnlyList<ScriptParameter> Parameters { get; } =
    [
        FolderParameter(),
        RecursiveParameter(),
        new("files", L.T("FileManagementScripts.FindEmptyFiles"), ScriptParameterKind.Boolean, L.T("FileManagementScripts.FilesOf0Bytes")) { Default = "true" },
        new("folders", L.T("FileManagementScripts.FindEmptyFolders"), ScriptParameterKind.Boolean, L.T("FileManagementScripts.FoldersWithNoFilesAndNo")) { Default = "true" },
        new("action", L.T("Label.Then"), ScriptParameterKind.Choice, L.T("FileManagementScripts.OnlyListThemOrAlsoPrepare"))
        {
            Default = "report", Choices = [new("report", L.T("FileManagementScripts.OnlyList")), new("move", L.T("FileManagementScripts.PrepareMovingThemToTheBackup"))],
        },
        new("exclude", L.T("FileManagementScripts.LeaveOut"), ScriptParameterKind.Text, L.T("FileManagementScripts.NamesOrPatternsSeparatedByThese")) { Required = false, Advanced = true, Default = L.T("FileManagementScripts.GitNodeModules") },
    ];

    public override ScriptResult Run(ScriptContext context, CancellationToken ct)
    {
        var args = context.Args;
        string folder = args.PathValue("folder")!;
        var scan = FileScanner.Scan(folder, args.Bool("recursive"), FileScanner.ParsePatterns(args.Text("exclude")), includeDirectories: true, ct: ct);
        var table = new ReportTable(L.T("FileManagementScripts.EmptyFilesAndFolders2"), [L.T("Common.Type"), L.T("Label.Path"), L.T("Choice.Sort.Modified"), L.T("Common.Note")]);
        var plan = new List<PlannedChange>();
        bool move = args.Choice("action") == "move";
        if (args.Bool("files"))
        {
            foreach (var f in scan.Files.Where(f => f.Length == 0))
            {
                table.Add(L.T("Common.File"), f.FullName, f.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), "");
                if (move) plan.Add(new PlannedChange { Action = L.T("FileManagementScripts.MoveToBackup"), Source = f.FullName, Detail = L.T("FileManagementScripts.EmptyFile"), Check = FileHashing.HashBytes([]) });
            }
        }
        int protectedCount = 0;
        if (args.Bool("folders"))
        {
            var nonEmpty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in scan.Files)
                for (var d = f.Directory; d != null && PathGuard.IsInside(folder, d.FullName); d = d.Parent) nonEmpty.Add(d.FullName);
            foreach (var link in scan.SkippedLinks)
                for (var d = Directory.GetParent(link); d != null && PathGuard.IsInside(folder, d.FullName); d = d.Parent) nonEmpty.Add(d.FullName);
            foreach (var d in scan.Directories.Where(d => !nonEmpty.Contains(d.FullName)))
            {
                bool isProtected = ProtectedNames.Contains(d.Name) || Path.GetRelativePath(folder, d.FullName).Split(Path.DirectorySeparatorChar).Any(ProtectedNames.Contains);
                if (isProtected) protectedCount++;
                table.Add(L.T("Common.Folder"), d.FullName, d.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), isProtected ? L.T("FileManagementScripts.KeptProgramsMayExpectThisFolder") : "");
                if (move && !isProtected) plan.Add(new PlannedChange { Action = L.T("FileManagementScripts.RemoveEmptyFolder"), Source = d.FullName, Detail = L.T("FileManagementScripts.EmptyFolderRecordedSoItCan") });
            }
        }
        var result = new ScriptResult
        {
            Table = table,
            Processed = scan.Files.Count + scan.Directories.Count,
            Succeeded = scan.Files.Count + scan.Directories.Count,
            Plan = move ? plan : null,
            Summary = L.F("FileManagementScripts.0N0EmptyFileSAnd", table.Rows.Count(r => r[0] == L.T("Common.File")), table.Rows.Count(r => r[0] == L.T("Common.Folder"))) +
                      (protectedCount > 0 ? L.Count(protectedCount, "FileManagementScripts.FoldersKept") : "") + ".",
        };
        result.Notes.AddRange(FileScanner.Notes(scan));
        return result;
    }

    public override ScriptResult Apply(ScriptContext context, IReadOnlyList<PlannedChange> changes, CancellationToken ct) =>
        Removal.Apply(context, L.T("FileManagementScripts.EmptyFilesAndFolders"), changes, ct);
}
