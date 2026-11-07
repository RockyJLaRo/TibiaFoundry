using TibiaFoundry.Core.Common;
using System.Globalization;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Scripts;

/// <summary>Script 12: an organization and integrity check of folders with Tibia files.</summary>
public sealed class TibiaProjectAuditorScript : Script
{
    public override string Id => "tibia-auditor";
    public override string Name => L.T("TibiaProjectAuditorScript.TibiaProjectFileAuditor");
    public override string Description => L.T("TibiaProjectAuditorScript.ChecksAFolderOfTibiaFiles");
    public override ScriptCategory Category => ScriptCategory.TibiaProjectUtilities;
    public override string SupportedInputs => L.T("TibiaProjectAuditorScript.ClientAssetsFoldersSpriteGalleryAnd");
    public override string Limitations => L.T("TibiaProjectAuditorScript.ThereIsNoUniversalListOf");

    public override IReadOnlyList<ScriptParameter> Parameters { get; } =
    [
        FolderParameter(),
        RecursiveParameter(),
        new("manifest", L.T("TibiaProjectAuditorScript.ExpectedFilesOptional"), ScriptParameterKind.File, L.T("TibiaProjectAuditorScript.AChecksumManifestAsMadeBy")) { Required = false, FileFilter = L.T("TibiaProjectAuditorScript.ChecksumManifestsSha256TxtAllFiles") },
        new("small", L.T("TibiaProjectAuditorScript.TinyFileThresholdBytes"), ScriptParameterKind.Integer, L.T("TibiaProjectAuditorScript.FilesSmallerThanThisButNot")) { Default = "16", Minimum = 0, Maximum = 1024 * 1024, Advanced = true },
        new("sheets", L.T("TibiaProjectAuditorScript.VerifySpriteSheetChecksums"), ScriptParameterKind.Boolean, L.T("TibiaProjectAuditorScript.DecompressEverySpriteSheetListedIn")) { Default = "false", Advanced = true },
    ];

    private static readonly (string Pattern, string Kind)[] KnownTibiaFiles =
    [
        ("catalog-content.json", L.T("TibiaProjectAuditorScript.ClientAssetCatalog")),
        ("catalog-sound.json", L.T("TibiaProjectAuditorScript.ClientSoundCatalog")),
        ("appearances-*.dat", L.T("TibiaProjectAuditorScript.ClientAppearances")),
        ("staticdata-*.dat", L.T("TibiaProjectAuditorScript.ClientStaticData")),
        ("staticmapdata-*.dat", L.T("TibiaProjectAuditorScript.ClientStaticMapData")),
        ("map-*.dat", L.T("TibiaProjectAuditorScript.ClientMap")),
        ("sprites-*.bmp.lzma", L.T("TibiaProjectAuditorScript.ClientSpriteSheet")),
        ("minimap-*.bmp.lzma", L.T("TibiaProjectAuditorScript.ClientMinimapTile")),
        ("satellite-*.bmp.lzma", L.T("TibiaProjectAuditorScript.ClientSatelliteTile")),
        ("subarea-*.bmp.lzma", L.T("TibiaProjectAuditorScript.ClientSubareaTile")),
        ("*.rcc", L.T("TibiaProjectAuditorScript.QtResourceFile")),
        ("Tibia.dat", L.T("TibiaProjectAuditorScript.LegacyObjectFile")),
        ("Tibia.spr", L.T("TibiaProjectAuditorScript.LegacySpriteFile")),
        ("items.otb", L.T("TibiaProjectAuditorScript.ThirdPartyItemIDMapping")),
        ("*.otbm", L.T("TibiaProjectAuditorScript.ThirdPartyMapFile")),
        ("*.aec", L.T("TibiaProjectAuditorScript.ObjectPackage")),
    ];

    public override ScriptResult Run(ScriptContext context, CancellationToken ct)
    {
        var args = context.Args;
        string folder = args.PathValue("folder")!;
        var scan = FileScanner.Scan(folder, args.Bool("recursive"), ct: ct);
        var table = new ReportTable(L.T("TibiaProjectAuditorScript.TibiaProjectAudit"), [L.T("Diagnostics.Finding"), L.T("Common.File"), L.T("Outfitter.Detail")]);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Finding(string finding, string file, string detail)
        {
            table.Add(finding, file, detail);
            counts[finding] = counts.GetValueOrDefault(finding) + 1;
        }
        string Rel(string path) => Path.GetRelativePath(folder, path).Replace('\\', '/');

        // Inventory of recognized Tibia files.
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in scan.Files)
        {
            var match = KnownTibiaFiles.FirstOrDefault(k => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(k.Pattern, f.Name));
            if (match.Kind != null) kinds[match.Kind] = kinds.GetValueOrDefault(match.Kind) + 1;
        }
        foreach (var kv in kinds) table.Summary.Add($"{kv.Key}: {kv.Value:N0}");

        // Catalogs: listed files that are missing, files not listed.
        int done = 0;
        foreach (var catalogFile in scan.Files.Where(f => f.Name.Equals(TibiaFormat.AssetCatalogFile, StringComparison.OrdinalIgnoreCase)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var catalog = AssetCatalog.Load(catalogFile.DirectoryName!);
                foreach (var problem in catalog.Problems) Finding(L.T("TibiaProjectAuditorScript.CatalogProblem"), Rel(catalogFile.FullName), problem);
                var listed = new HashSet<string>(catalog.Sheets.Select(s => s.FileName).Concat(catalog.Files.Values), StringComparer.OrdinalIgnoreCase);
                foreach (string name in listed)
                    if (!File.Exists(Path.Combine(catalogFile.DirectoryName!, name))) Finding(L.T("TibiaProjectAuditorScript.MissingListedInCatalog"), Rel(Path.Combine(catalogFile.DirectoryName!, name)), L.T("TibiaProjectAuditorScript.ListedIn") + Rel(catalogFile.FullName));
                foreach (var f in new DirectoryInfo(catalogFile.DirectoryName!).GetFiles())
                    if (!listed.Contains(f.Name) && !f.Name.Equals(TibiaFormat.AssetCatalogFile, StringComparison.OrdinalIgnoreCase) && !f.Name.StartsWith("minimap-", StringComparison.OrdinalIgnoreCase))
                        Finding(L.T("TibiaProjectAuditorScript.NotListedInCatalog"), Rel(f.FullName), L.T("TibiaProjectAuditorScript.TheClientDoesNotUseFiles"));
                if (args.Bool("sheets"))
                {
                    var sheets = catalog.Sheets.Where(s => File.Exists(catalog.GetSheetPath(s))).ToList();
                    Parallel.ForEach(sheets, new ParallelOptions { MaxDegreeOfParallelism = context.WorkerThreads, CancellationToken = ct }, sheet =>
                    {
                        try
                        {
                            Sprites.SpriteSheetDecoder.DecodeSheet(catalog.GetSheetPath(sheet));
                        }
                        catch (InvalidDataException ex)
                        {
                            lock (table) Finding(L.T("TibiaProjectAuditorScript.DamagedSpriteSheet"), Rel(catalog.GetSheetPath(sheet)), ex.Message);
                        }
                        int n = Interlocked.Increment(ref done);
                        if (n % 100 == 0) context.Report(n, sheets.Count, L.T("TibiaProjectAuditorScript.CheckingSpriteSheets"));
                    });
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                Finding(L.T("TibiaProjectAuditorScript.CatalogUnreadable"), Rel(catalogFile.FullName), ex.Message);
            }
        }

        // Duplicate names in different folders.
        foreach (var g in scan.Files.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1 && g.Select(f => f.DirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            foreach (var f in g) Finding(L.T("TibiaProjectAuditorScript.SameNameInSeveralFolders"), Rel(f.FullName), L.F("TibiaProjectAuditorScript.0FilesNamed1", g.Count(), g.Key));

        // Empty, tiny and mislabeled files.
        long small = args.Long("small", 16);
        foreach (var f in scan.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (f.Length == 0) { Finding(L.T("TibiaProjectAuditorScript.EmptyFile"), Rel(f.FullName), ""); continue; }
            if (f.Length < small) Finding(L.T("TibiaProjectAuditorScript.SuspiciouslySmall"), Rel(f.FullName), L.F("TibiaProjectAuditorScript.0Bytes", f.Length));
            try
            {
                var id = FileSignatures.Identify(f.FullName);
                if (id.Format != null && id.Evidence == FormatEvidence.Signature && id.Format.Category != "Text" && !id.Format.ExtensionMatches(f.FullName) && f.Extension.Length > 0)
                    Finding(L.T("TibiaProjectAuditorScript.ExtensionDoesNotMatchContent"), Rel(f.FullName), L.F("TibiaProjectAuditorScript.ContentIs0", id.Format.Name));
                else if (id.Format == null) counts["(unknown format)"] = counts.GetValueOrDefault("(unknown format)") + 1;
            }
            catch (IOException ex)
            {
                Finding("Unreadable", Rel(f.FullName), ex.Message);
            }
        }

        // Expected files from a manifest.
        if (args.PathValue("manifest") is { } manifestPath)
        {
            var expected = ChecksumScript.ParseManifest(File.ReadAllLines(manifestPath), HashAlgorithmKind.Sha256);
            var present = scan.Files.ToDictionary(f => Rel(f.FullName), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in expected)
            {
                if (!present.TryGetValue(kv.Key, out var file)) Finding(L.T("TibiaProjectAuditorScript.MissingInManifest"), kv.Key, "");
                else if (FileHashing.HashFile(file.FullName, ct: ct) != kv.Value) Finding(L.T("TibiaProjectAuditorScript.DifferentFromManifest"), kv.Key, L.T("TibiaProjectAuditorScript.ContentChanged"));
            }
            foreach (var rel in present.Keys.Where(k => !expected.ContainsKey(k)))
                if (!string.Equals(Path.Combine(folder, rel), manifestPath, StringComparison.OrdinalIgnoreCase)) Finding(L.T("TibiaProjectAuditorScript.NotInManifest"), rel, "");
        }

        table.Summary.AddRange(counts.Select(kv => $"{kv.Key}: {kv.Value:N0}"));
        int issues = table.Rows.Count(r => r[0] != L.T("TibiaProjectAuditorScript.NotListedInCatalog") && r[0] != L.T("TibiaProjectAuditorScript.SameNameInSeveralFolders") && r[0] != L.T("TibiaProjectAuditorScript.NotInManifest"));
        var result = new ScriptResult
        {
            Table = table,
            Processed = scan.Files.Count,
            Succeeded = scan.Files.Count,
            Failed = issues,
            Summary = L.F("TibiaProjectAuditorScript.0Audited1N0RecognizedAs", L.Count(scan.Files.Count, "Noun.file"), kinds.Values.Sum(), L.Count(issues, "Noun.problem"), L.Count(table.Rows.Count - issues, "Noun.item")),
        };
        result.Notes.AddRange(FileScanner.Notes(scan));
        return result;
    }
}
