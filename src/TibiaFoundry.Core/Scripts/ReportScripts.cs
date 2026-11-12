using System.Collections.Concurrent;
using System.Globalization;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Scripts;

/// <summary>Script 5: a searchable list of every file in a folder.</summary>
public sealed class InventoryScript : Script
{
    public override string Id => "inventory";
    public override string Name => L.T("ReportScripts.FileInventoryAndManifest");

    public override OutputKind DefaultOutput => OutputKind.Manifests;
    public override string Description => L.T("ReportScripts.ListsEveryFileOfAFolder");
    public override ScriptCategory Category => ScriptCategory.DiagnosticsAndReports;
    public override string SupportedInputs => L.T("ReportScripts.AnyFolder");
    public override string Limitations => L.T("ReportScripts.TheDetectedFormatComesFromEach");

    public override IReadOnlyList<ScriptParameter> Parameters { get; } =
    [
        FolderParameter(),
        RecursiveParameter(),
        new("hashes", L.T("ReportScripts.IncludeSHA256"), ScriptParameterKind.Boolean, L.T("ReportScripts.SlowerMakesTheInventoryUsableFor")) { Default = "false" },
        new("formats", L.T("ReportScripts.DetectFormats"), ScriptParameterKind.Boolean, L.T("ReportScripts.ReadEachFileSFirstBytes")) { Default = "true" },
        new("output", L.T("ReportScripts.SaveAs"), ScriptParameterKind.OutputFile, L.T("ReportScripts.CSVOrJSONFileTheExtension")) { Required = false, FileFilter = L.T("ReportScripts.CSVFilesCsvJSONFilesJson") },
    ];

    public override ScriptResult Run(ScriptContext context, CancellationToken ct)
    {
        var args = context.Args;
        string folder = args.PathValue("folder")!;
        var scan = FileScanner.Scan(folder, args.Bool("recursive"), ct: ct);
        bool hashes = args.Bool("hashes"), formats = args.Bool("formats");
        var rows = new string[scan.Files.Count][];
        int done = 0;
        var errors = new ConcurrentBag<string>();
        Parallel.For(0, scan.Files.Count, new ParallelOptions { MaxDegreeOfParallelism = context.WorkerThreads, CancellationToken = ct }, i =>
        {
            var f = scan.Files[i];
            string format = "", mismatch = "", hash = "";
            try
            {
                if (formats)
                {
                    var id = FileSignatures.Identify(f.FullName);
                    format = id.Format?.Name ?? (f.Length == 0 ? "(empty)" : "unknown");
                    if (id.Format != null && id.Evidence == FormatEvidence.Signature && id.Format.Category != "Text" && !id.Format.ExtensionMatches(f.FullName) && f.Extension.Length > 0) mismatch = "yes";
                }
                if (hashes) hash = FileHashing.HashFile(f.FullName, ct: ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{f.FullName}: {ex.Message}");
            }
            rows[i] = [Path.GetRelativePath(folder, f.FullName).Replace('\\', '/'), f.Extension.ToLowerInvariant(), f.Length.ToString(CultureInfo.InvariantCulture),
                f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), format, mismatch, hash];
            int n = Interlocked.Increment(ref done);
            if (n % 200 == 0) context.Report(n, scan.Files.Count, "listing");
        });
        var table = new ReportTable(L.T("ReportScripts.FileInventory"), [L.T("Label.Path"), L.T("Label.Extension"), L.T("Common.Size"), L.T("Choice.Sort.Modified"), L.T("ReportScripts.DetectedFormat"), L.T("ReportScripts.ExtensionMismatch"), "SHA-256"]);
        foreach (var row in rows) table.Add(row);
        foreach (var g in scan.Files.GroupBy(f => f.Extension.Length == 0 ? "(none)" : f.Extension.ToLowerInvariant()).OrderByDescending(g => g.Sum(f => f.Length)))
            table.Summary.Add($"{g.Key}: {L.Count(g.Count(), "Noun.file")}, {ReportWriter.FormatSize(g.Sum(f => f.Length))}");
        var result = new ScriptResult
        {
            Table = table,
            Processed = scan.Files.Count,
            Succeeded = scan.Files.Count - errors.Count,
            Failed = errors.Count,
            Summary = L.F("ReportScripts.0123N0With", L.Count(scan.Files.Count, "Noun.file"), ReportWriter.FormatSize(scan.Files.Sum(f => f.Length)), L.Count(table.Summary.Count, "Noun.extension"), rows.Count(r => r[5] == "yes")),
        };
        result.Errors.AddRange(errors);
        result.Notes.AddRange(FileScanner.Notes(scan));
        if (args.PathValue("output") is { } output)
        {
            using var session = context.CreateOutput(L.T("ReportScripts.FileInventory"));
            string? path = session.WriteFile(output, temp => ReportWriter.WriteTo(temp, table, ReportWriter.FormatFromPath(output), TextAttribution.ReportHeader(Attribution.ForNewAsset(copyright: null))));
            if (path != null)
            {
                result.OutputFiles.Add(path);
                result.ChangesApplied = true;
                result.Summary += L.F("ReportScripts.SavedTo0", path);
            }
        }
        return result;
    }
}

/// <summary>Script 10: what a file really is, and what it says about itself.</summary>
public sealed class FormatInspectorScript : Script
{
    public override string Id => "format-inspector";
    public override string Name => L.T("ReportScripts.FileFormatAndMetadataInspector");
    public override string Description => L.T("ReportScripts.IdentifiesFilesByTheirContentFlags");
    public override ScriptCategory Category => ScriptCategory.DiagnosticsAndReports;
    public override string SupportedInputs => L.T("ReportScripts.AnyFilesDeeperChecksForPNG");
    public override string Limitations => L.T("ReportScripts.IdentifiedOnlyMeansJustTheSignature");

    public override IReadOnlyList<ScriptParameter> Parameters { get; } =
    [
        new("path", L.T("ReportScripts.FileOrFolder"), ScriptParameterKind.Path, L.T("ReportScripts.ASingleFileOrAFolder")),
        RecursiveParameter(false),
        new("decode", L.T("ReportScripts.DecodeImagesCompletely"), ScriptParameterKind.Boolean, L.T("ReportScripts.DecodeEveryPixelFindsDamageThat")) { Default = "true", Advanced = true },
    ];

    public override ScriptResult Run(ScriptContext context, CancellationToken ct)
    {
        var args = context.Args;
        string path = args.PathValue("path")!;
        var notes = new List<string>();
        List<FileInfo> files;
        string root;
        if (File.Exists(path))
        {
            files = [new FileInfo(path)];
            root = Path.GetDirectoryName(path)!;
        }
        else
        {
            var scan = FileScanner.Scan(path, args.Bool("recursive"), ct: ct);
            files = scan.Files;
            root = path;
            notes.AddRange(FileScanner.Notes(scan));
        }
        var results = new InspectionResult[files.Count];
        int done = 0;
        bool decode = args.Bool("decode");
        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = context.WorkerThreads, CancellationToken = ct }, i =>
        {
            results[i] = FileInspector.Inspect(files[i].FullName, decode, ct);
            int n = Interlocked.Increment(ref done);
            if (n % 20 == 0) context.Report(n, files.Count, "inspecting");
        });
        var table = new ReportTable(L.T("ReportScripts.FileInspection"), [L.T("Common.File"), L.T("Common.Size"), L.T("Common.Format"), L.T("ReportScripts.IdentifiedBy"), L.T("OtherProviders.Checked"), L.T("Common.Details"), L.T("Common.Attribution"), L.T("Label.Problems")]);
        foreach (var r in results)
            table.Add(Path.GetRelativePath(root, r.Path).Replace('\\', '/'), r.Size.ToString(CultureInfo.InvariantCulture), r.Format?.Name ?? "unknown",
                r.Format == null ? "" : r.Evidence switch { FormatEvidence.Name => L.T("ReportScripts.NameOnly"), FormatEvidence.Parsed => "parsing", _ => "signature" },
                r.LevelText, r.DetailText, r.Attribution is { IsEmpty: false } a ? string.Join("; ", a.Creators) + (a.Text != null ? $" ({a.Text})" : "") : "",
                string.Join(" ", r.Problems));
        int problems = results.Count(r => r.Problems.Count > 0);
        var result = new ScriptResult
        {
            Table = table,
            Processed = files.Count,
            Succeeded = files.Count - problems,
            Failed = problems,
            Summary = L.F("ReportScripts.0Inspected1N0CheckedCompletely", L.Count(files.Count, "Noun.file"), results.Count(r => r.Level >= InspectionLevel.StructureChecked), results.Count(r => r.ExtensionMismatch), results.Count(r => r.Format == null), results.Count(r => r.Attribution is { IsEmpty: false }), problems),
        };
        result.Notes.AddRange(notes);
        return result;
    }
}
