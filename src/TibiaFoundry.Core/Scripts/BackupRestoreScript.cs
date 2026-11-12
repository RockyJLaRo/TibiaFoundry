using System.Globalization;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Scripts;

/// <summary>Script 11: backups before risky work, and restoring from them.</summary>
public sealed class BackupRestoreScript : Script
{
    private readonly Func<BackupStore> _store;

    /// <param name="store">The application's backup store (the same one the tools use for automatic backups).</param>
    public BackupRestoreScript(Func<BackupStore> store)
    {
        _store = store;
        Parameters =
        [
            new("mode", L.T("Label.Task"), ScriptParameterKind.Choice, L.T("BackupRestoreScript.WhatToDo"))
            {
                Default = "list",
                Choices = [new("backup", L.T("BackupRestoreScript.BackUpFilesOrAFolder")), new("list", L.T("BackupRestoreScript.ListBackups")), new("restore", L.T("BackupRestoreScript.RestoreFromABackup"))],
            },
            new("source", L.T("BackupRestoreScript.FileOrFolder"), ScriptParameterKind.Path, L.T("BackupRestoreScript.WhatToBackUpFoldersKeep")) { VisibleWhen = "mode=backup" },
            RecursiveParameter() with { VisibleWhen = "mode=backup" },
            new("label", L.T("Common.Description"), ScriptParameterKind.Text, L.T("BackupRestoreScript.ANoteToRecognizeTheBackup")) { Required = false, VisibleWhen = "mode=backup" },
            new("set", L.T("Label.Backup"), ScriptParameterKind.Choice, L.T("BackupRestoreScript.TheBackupToRestoreFromNewest")) { VisibleWhen = "mode=restore", ChoicesProvider = SetChoices },
            new("target", L.T("BackupRestoreScript.RestoreTo"), ScriptParameterKind.Choice, L.T("BackupRestoreScript.PutTheFilesBackWhereThey"))
            {
                Default = "original", VisibleWhen = "mode=restore",
                Choices = [new("original", L.T("BackupRestoreScript.TheirOriginalLocations")), new("folder", L.T("BackupRestoreScript.AnotherFolder"))],
            },
            new("targetFolder", L.T("Common.Folder"), ScriptParameterKind.OutputFolder, L.T("BackupRestoreScript.WhereTheRestoredFilesGo")) { VisibleWhen = "target=folder" },
        ];
    }

    public override string Id => "backup-restore";
    public override string Name => L.T("BackupRestoreScript.BackupAndRestore");
    public override string Description => L.T("BackupRestoreScript.BacksUpFilesOrFoldersChecked");
    public override ScriptCategory Category => ScriptCategory.FileManagement;
    public override string SupportedInputs => L.T("BackupRestoreScript.AnyFilesAndFolders");
    public override string Limitations => L.T("BackupRestoreScript.BackupsAreCopiesInTheBackup");
    public override bool ChangesFiles => true;
    public override bool WillChangeFiles(ScriptArguments args) => args.Choice("mode") == "restore";

    public override IReadOnlyList<ScriptParameter> Parameters { get; }

    private IReadOnlyList<ScriptChoice> SetChoices() =>
        _store().List().Select(s => new ScriptChoice(s.Id,
            $"{s.Created:yyyy-MM-dd HH:mm} — {s.Manifest.Operation} ({L.Count(s.FileCount, "Noun.file")}, {ReportWriter.FormatSize(s.TotalSize)}{(s.Manifest.Complete ? "" : ", incomplete")})")).ToList();

    public override ScriptResult Run(ScriptContext context, CancellationToken ct) => context.Args.Choice("mode") switch
    {
        "backup" => BackUp(context, ct),
        "restore" => PreviewRestore(context),
        _ => List(),
    };

    private ScriptResult List()
    {
        var store = _store();
        var sets = store.List();
        var table = new ReportTable("Backups", [L.T("Label.Created"), L.T("Common.Description"), L.T("Common.Kind"), L.T("Common.Files"), L.T("Common.Size"), L.T("Label.Complete"), L.T("Common.Folder")]);
        foreach (var s in sets)
            table.Add(s.Created.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), s.Manifest.Operation, s.Manifest.Kind.ToString(), s.FileCount.ToString(CultureInfo.InvariantCulture),
                ReportWriter.FormatSize(s.TotalSize), s.Manifest.Complete ? "yes" : "no", s.Folder);
        var history = store.History();
        foreach (var h in history.Take(10))
            table.Summary.Add(L.F("BackupRestoreScript.Restored0YyyyMMDdHH", h.WhenUtc.ToLocalTime(), L.Count(h.Restored, "Noun.file"), h.SetId, (h.Failed > 0 ? L.F("BackupRestoreScript.0N0Failed", h.Failed) : "")));
        return new ScriptResult
        {
            Table = table,
            Processed = sets.Count,
            Succeeded = sets.Count,
            OutputFolder = store.Root,
            Summary = L.F("BackupRestoreScript.01In23Recorded", L.Count(sets.Count, "Noun.backup"), ReportWriter.FormatSize(sets.Sum(s => s.TotalSize)), store.Root, L.Count(history.Count, "Noun.restore")),
        };
    }

    private ScriptResult BackUp(ScriptContext context, CancellationToken ct)
    {
        var args = context.Args;
        string source = args.PathValue("source")!;
        var store = _store();
        if (PathGuard.Overlaps(source, store.Root)) throw new InvalidOperationException("The backup folder cannot back up itself (or a folder that contains it).");
        string label = args.Text("label").Length > 0 ? args.Text("label") : L.F("BackupRestoreScript.BackupOf0", Path.GetFileName(PathGuard.Normalize(source)));
        var result = new ScriptResult();
        List<FileInfo> files;
        string? root;
        if (File.Exists(source))
        {
            files = [new FileInfo(source)];
            root = null;
        }
        else
        {
            var scan = FileScanner.Scan(source, args.Bool("recursive"), ct: ct);
            files = scan.Files;
            root = source;
            result.Notes.AddRange(FileScanner.Notes(scan));
        }
        using var set = store.Begin(label, BackupKind.Manual, root);
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            context.Report(i, files.Count, files[i].Name);
            result.Processed++;
            try
            {
                set.Add(files[i].FullName, ct: ct);
                result.Succeeded++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Failed++;
                result.Errors.Add($"{files[i].FullName}: {ex.Message}");
                if (context.StopOnError) break;
            }
        }
        set.Complete();
        result.ChangesApplied = result.Succeeded > 0;
        result.OutputFolder = set.Folder;
        result.BackupFolder = set.Folder;
        result.Summary = L.F("BackupRestoreScript.0N0Of1BackedUp", result.Succeeded, L.Count(files.Count, "Noun.file"), ReportWriter.FormatSize(set.Manifest.Entries.Sum(e => e.Size)), set.Folder);
        return result;
    }

    private ScriptResult PreviewRestore(ScriptContext context)
    {
        var args = context.Args;
        var set = _store().List().FirstOrDefault(s => s.Id == args.Choice("set")) ?? throw new InvalidOperationException("The chosen backup no longer exists.");
        string? folder = args.Choice("target") == "folder" ? args.PathValue("targetFolder") : null;
        var plan = new List<PlannedChange>();
        var table = new ReportTable(L.F("BackupRestoreScript.RestoreFrom0", set.Id), [L.T("Common.File"), L.T("BackupRestoreScript.RestoreTo"), L.T("Common.Note")]);
        foreach (var entry in set.Manifest.Entries)
        {
            string target;
            string note;
            bool blocked = false;
            try
            {
                target = BackupStore.TargetFor(set, entry, folder);
                note = entry.IsDirectory ? (Directory.Exists(target) ? L.T("BackupRestoreScript.FolderExists") : L.T("BackupRestoreScript.FolderIsRecreated"))
                    : File.Exists(target) ? L.T("BackupRestoreScript.EXISTSYouAreAskedBeforeIt") : "";
            }
            catch (UnsafePathException ex)
            {
                target = "";
                note = ex.Message;
                blocked = true;
            }
            plan.Add(new PlannedChange { Action = entry.IsDirectory ? L.T("BackupRestoreScript.RecreateFolder") : L.T("AssetLibrary.Restore2"), Source = entry.Stored.Length > 0 ? entry.Stored : entry.Original, Target = target, Detail = note, Blocked = blocked, Selected = !blocked, Check = entry.Original });
            table.Add(entry.IsDirectory ? entry.Original + "\\" : Path.GetFileName(entry.Original), target, note);
        }
        return new ScriptResult
        {
            Table = table,
            Plan = plan,
            Processed = plan.Count,
            Summary = L.F("BackupRestoreScript.0N0EntrIesCanBe", plan.Count(p => !p.Blocked), plan.Count(p => p.Detail.StartsWith("EXISTS", StringComparison.Ordinal))),
        };
    }

    public override ScriptResult Apply(ScriptContext context, IReadOnlyList<PlannedChange> changes, CancellationToken ct)
    {
        var args = context.Args;
        var store = _store();
        var set = store.List().FirstOrDefault(s => s.Id == args.Choice("set")) ?? throw new InvalidOperationException("The chosen backup no longer exists.");
        string? folder = args.Choice("target") == "folder" ? args.PathValue("targetFolder") : null;
        var wanted = new HashSet<string>(changes.Where(c => c.Selected && !c.Blocked).Select(c => c.Check!), StringComparer.OrdinalIgnoreCase);
        var entries = set.Manifest.Entries.Where(e => wanted.Contains(e.Original)).ToList();
        bool? all = null;
        var restore = store.Restore(set, entries, folder, path =>
        {
            if (all is { } decided) return decided;
            if (context.Prompt == null) return false;
            var answer = context.Prompt(path, L.T("BackupRestoreScript.Restore"), 0);
            bool yes = answer.Choice == ConflictChoice.Overwrite;
            if (answer.Choice == ConflictChoice.Cancel) throw new OperationCanceledException();
            if (answer.ApplyToAll) all = yes;
            return yes;
        }, ct);
        var result = new ScriptResult
        {
            Processed = entries.Count,
            Succeeded = restore.Restored.Count,
            Failed = restore.Failed.Count,
            ChangesApplied = restore.Restored.Count > 0,
            OutputFolder = folder,
            Summary = L.F("BackupRestoreScript.0N0Restored1N0Skipped", restore.Restored.Count, restore.Skipped.Count, restore.Failed.Count),
        };
        result.Errors.AddRange(restore.Failed);
        result.Notes.AddRange(restore.Skipped.Take(20).Select(s => "Skipped: " + s));
        return result;
    }
}
