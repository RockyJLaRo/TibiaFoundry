using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Scripts;

/// <summary>Script 2: compares two folders.</summary>
public sealed class DirectoryComparisonScript : Script
{
    public override string Id => "compare-folders";
    public override string Name => L.T("ComparisonScripts.DirectoryComparison");
    public override string Description => L.T("ComparisonScripts.ComparesTwoFoldersFilesOnlyIn");
    public override ScriptCategory Category => ScriptCategory.ComparisonAndVerification;
    public override string SupportedInputs => L.T("ComparisonScripts.TwoFolders");
    public override string Limitations => L.T("ComparisonScripts.WithoutContentComparisonFilesOfEqual");

    public override IReadOnlyList<ScriptParameter> Parameters { get; } =
    [
        new("left", L.T("ComparisonScripts.FirstFolder"), ScriptParameterKind.Folder, L.T("ComparisonScripts.ForExampleTheOlderVersion")),
        new("right", L.T("ComparisonScripts.SecondFolder"), ScriptParameterKind.Folder, L.T("ComparisonScripts.ForExampleTheNewerVersion")),
        RecursiveParameter(),
        new("content", L.T("ComparisonScripts.CompareContent"), ScriptParameterKind.Boolean, L.T("ComparisonScripts.CompareFilesOfEqualSizeWith")) { Default = "false" },
        new("tolerance", L.T("ComparisonScripts.TimeToleranceSeconds"), ScriptParameterKind.Integer, L.T("ComparisonScripts.ModificationTimesCloserThanThisCount")) { Default = "2", Minimum = 0, Maximum = 86400, Advanced = true },
        new("showEqual", L.T("ComparisonScripts.ListEqualFiles"), ScriptParameterKind.Boolean, L.T("ComparisonScripts.AlsoListFilesThatAreThe")) { Default = "false", Advanced = true },
    ];

    public override IReadOnlyList<string> Validate(ScriptArguments args)
    {
        var problems = base.Validate(args).ToList();
        if (problems.Count == 0 && string.Equals(args.PathValue("left"), args.PathValue("right"), StringComparison.OrdinalIgnoreCase))
            problems.Add(L.T("ComparisonScripts.ChooseTwoDifferentFolders"));
        return problems;
    }

    public override ScriptResult Run(ScriptContext context, CancellationToken ct)
    {
        var args = context.Args;
        string left = args.PathValue("left")!, right = args.PathValue("right")!;
        bool recursive = args.Bool("recursive");
        var a = FileScanner.Scan(left, recursive, ct: ct);
        var b = FileScanner.Scan(right, recursive, ct: ct);
        var mapA = a.Files.ToDictionary(f => Path.GetRelativePath(left, f.FullName), StringComparer.OrdinalIgnoreCase);
        var mapB = b.Files.ToDictionary(f => Path.GetRelativePath(right, f.FullName), StringComparer.OrdinalIgnoreCase);
        var tolerance = TimeSpan.FromSeconds(args.Long("tolerance", 2));
        bool content = args.Bool("content"), showEqual = args.Bool("showEqual");
        var table = new ReportTable(L.T("ComparisonScripts.FolderComparison"), [L.T("Common.Status"), L.T("Common.File"), L.T("ComparisonScripts.SizeFirst"), L.T("ComparisonScripts.SizeSecond"), L.T("ComparisonScripts.ModifiedFirst"), L.T("ComparisonScripts.ModifiedSecond")]);
        var counts = new Dictionary<string, int>();
        var all = mapA.Keys.Union(mapB.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        var statuses = new ConcurrentDictionary<string, string>();
        int done = 0;
        Parallel.ForEach(all, new ParallelOptions { MaxDegreeOfParallelism = context.WorkerThreads, CancellationToken = ct }, rel =>
        {
            mapA.TryGetValue(rel, out var fa);
            mapB.TryGetValue(rel, out var fb);
            string status;
            if (fa == null) status = L.T("ComparisonScripts.OnlyInSecond");
            else if (fb == null) status = L.T("ComparisonScripts.OnlyInFirst");
            else if (fa.Length != fb.Length) status = L.T("ComparisonScripts.DifferentSize");
            else if (content)
            {
                try
                {
                    status = FileHashing.SameContent(fa.FullName, fb.FullName, ct) ? "Equal" : L.T("ComparisonScripts.DifferentContent");
                }
                catch (IOException ex)
                {
                    status = "Unreadable: " + ex.Message;
                }
            }
            else status = (fa.LastWriteTimeUtc - fb.LastWriteTimeUtc).Duration() > tolerance ? L.T("ComparisonScripts.DifferentTime") : "Equal";
            statuses[rel] = status;
            int n = Interlocked.Increment(ref done);
            if (n % 200 == 0) context.Report(n, all.Count, "comparing");
        });
        foreach (string rel in all)
        {
            string status = statuses[rel];
            counts[status] = counts.GetValueOrDefault(status) + 1;
            if (status == "Equal" && !showEqual) continue;
            mapA.TryGetValue(rel, out var fa);
            mapB.TryGetValue(rel, out var fb);
            table.Add(status, rel.Replace('\\', '/'), fa?.Length.ToString(CultureInfo.InvariantCulture) ?? "", fb?.Length.ToString(CultureInfo.InvariantCulture) ?? "",
                fa?.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "", fb?.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "");
        }
        foreach (var kv in counts.OrderBy(k => k.Key)) table.Summary.Add($"{kv.Key}: {kv.Value:N0}");
        var result = new ScriptResult
        {
            Table = table,
            Processed = all.Count,
            Succeeded = all.Count - counts.Where(k => k.Key.StartsWith("Unreadable", StringComparison.Ordinal)).Sum(k => k.Value),
            Summary = L.F("ComparisonScripts.0Compared1N0Equal2", L.Count(all.Count, "Noun.file"), counts.GetValueOrDefault("Equal"), counts.GetValueOrDefault("Only in first"), counts.GetValueOrDefault("Only in second"), counts.Where(k => k.Key.StartsWith("Different", StringComparison.Ordinal)).Sum(k => k.Value)),
        };
        result.Failed = result.Processed - result.Succeeded;
        if (!content) result.Notes.Add(L.T("ComparisonScripts.ContentWasNotComparedEqualSize"));
        result.Notes.AddRange(FileScanner.Notes(a));
        result.Notes.AddRange(FileScanner.Notes(b));
        return result;
    }
}

/// <summary>Script 4: checksums and manifests.</summary>
public sealed class ChecksumScript : Script
{
    public override string Id => "checksums";
    public override string Name => L.T("ComparisonScripts.FileHashAndChecksumCalculator");
    public override string Description => L.T("ComparisonScripts.CalculatesSHA256OrSHA512");
    public override ScriptCategory Category => ScriptCategory.ComparisonAndVerification;
    public override string SupportedInputs => L.T("ComparisonScripts.AnyFilesManifestsInTheHash");
    public override string Limitations => L.T("ComparisonScripts.AMatchingHashProvesThatThe");

    public override bool ChangesFiles => false;

    public override OutputKind DefaultOutput => OutputKind.Manifests;

    public override IReadOnlyList<ScriptParameter> Parameters { get; } =
    [
        new("mode", L.T("Label.Task"), ScriptParameterKind.Choice, L.T("ComparisonScripts.WhatToDo"))
        {
            Default = "hash",
            Choices = [new("hash", L.T("ComparisonScripts.CalculateHashes")), new("create", L.T("ComparisonScripts.CreateAManifest")), new("verify", L.T("ComparisonScripts.VerifyAgainstAManifest"))],
        },
        new("path", L.T("ComparisonScripts.FileOrFolder"), ScriptParameterKind.Path, L.T("ComparisonScripts.ASingleFileOrAFolder")) { VisibleWhen = "mode=hash|create" },
        new("verifyFolder", L.T("ComparisonScripts.FolderToCheck"), ScriptParameterKind.Folder, L.T("ComparisonScripts.PathsInTheManifestAreRelative")) { VisibleWhen = "mode=verify" },
        RecursiveParameter() with { VisibleWhen = "mode=hash|create" },
        new("algorithm", L.T("Label.Algorithm"), ScriptParameterKind.Choice, L.T("ComparisonScripts.SHA256IsRecommended"))
        {
            Default = "Sha256",
            Choices = [new("Sha256", "SHA-256"), new("Sha512", "SHA-512"), new("Sha1", L.T("ComparisonScripts.SHA1OldListsOnly")), new("Md5", L.T("ComparisonScripts.MD5OldListsOnly"))],
        },
        new("manifest", L.T("ComparisonScripts.ManifestFile"), ScriptParameterKind.OutputFile, L.T("ComparisonScripts.WhereTheManifestIsWritten")) { VisibleWhen = "mode=create", FileFilter = L.T("ComparisonScripts.ChecksumManifestsSha256Sha512Sha1Md5") },
        new("manifestIn", L.T("ComparisonScripts.ManifestFile"), ScriptParameterKind.File, L.T("ComparisonScripts.TheManifestToCheckAgainst")) { VisibleWhen = "mode=verify", FileFilter = L.T("ComparisonScripts.ChecksumManifestsSha256Sha512Sha1Md5") },
    ];

    public override bool WillChangeFiles(ScriptArguments args) => false;

    public override ScriptResult Run(ScriptContext context, CancellationToken ct)
    {
        var args = context.Args;
        var kind = Enum.TryParse<HashAlgorithmKind>(args.Choice("algorithm"), out var k) ? k : HashAlgorithmKind.Sha256;
        return args.Choice("mode") == "verify" ? Verify(context, kind, ct) : Hash(context, kind, args.Choice("mode") == "create", ct);
    }

    private static ScriptResult Hash(ScriptContext context, HashAlgorithmKind kind, bool writeManifest, CancellationToken ct)
    {
        var args = context.Args;
        string path = args.PathValue("path")!;
        string root;
        List<FileInfo> files;
        var notes = new List<string>();
        if (File.Exists(path))
        {
            root = Path.GetDirectoryName(path)!;
            files = [new FileInfo(path)];
        }
        else
        {
            root = path;
            var scan = FileScanner.Scan(path, args.Bool("recursive"), ct: ct);
            files = scan.Files;
            notes.AddRange(FileScanner.Notes(scan));
        }
        string? manifestPath = writeManifest ? args.PathValue("manifest") : null;
        if (manifestPath != null) files = files.Where(f => !string.Equals(f.FullName, manifestPath, StringComparison.OrdinalIgnoreCase)).ToList();
        var hashes = new string?[files.Count];
        var errors = new ConcurrentBag<string>();
        int done = 0;
        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = context.WorkerThreads, CancellationToken = ct }, i =>
        {
            try
            {
                hashes[i] = FileHashing.HashFile(files[i].FullName, kind, ct: ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{files[i].FullName}: {ex.Message}");
            }
            int n = Interlocked.Increment(ref done);
            if (n % 50 == 0) context.Report(n, files.Count, "hashing");
        });
        string name = FileHashing.Name(kind);
        var table = new ReportTable(L.F("ComparisonScripts.0Hashes", name), [name, L.T("Common.File"), L.T("Common.Size")]);
        var manifest = new StringBuilder();
        for (int i = 0; i < files.Count; i++)
        {
            if (hashes[i] == null) continue;
            string rel = Path.GetRelativePath(root, files[i].FullName).Replace('\\', '/');
            table.Add(hashes[i]!, rel, files[i].Length.ToString(CultureInfo.InvariantCulture));
            manifest.Append(hashes[i]).Append("  ").Append(rel).Append('\n');
        }
        var result = new ScriptResult { Table = table, Processed = files.Count, Succeeded = files.Count - errors.Count, Failed = errors.Count };
        result.Errors.AddRange(errors);
        result.Notes.AddRange(notes);
        if (FileHashing.IsWeak(kind)) result.Notes.Add(L.F("ComparisonScripts.0IsNotCollisionResistantUse", name));
        result.Summary = L.F("ComparisonScripts.0HashedWith1", L.Count(result.Succeeded, "Noun.file"), name);
        if (manifestPath != null)
        {
            using var output = context.CreateOutput("Checksum manifest");
            string? written = output.WriteFile(manifestPath, temp => File.WriteAllText(temp, manifest.ToString(), new UTF8Encoding(false)), temp =>
            {
                if (ParseManifest(File.ReadAllLines(temp), kind).Count != result.Succeeded) throw new InvalidDataException("The manifest could not be read back completely.");
            });
            if (written != null)
            {
                result.OutputFiles.Add(written);
                result.ChangesApplied = true;
                result.Summary += L.F("ComparisonScripts.ManifestWritten0PathsRelativeTo", written, root);
            }
            else result.Summary += L.T("ComparisonScripts.TheManifestWasNotWrittenThe");
        }
        return result;
    }

    /// <summary>Reads "hash  path" or "hash *path" lines; returns path → hash.</summary>
    public static Dictionary<string, string> ParseManifest(IEnumerable<string> lines, HashAlgorithmKind kind)
    {
        int length = FileHashing.HexLength(kind);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            string line = raw.TrimEnd('\r');
            if (line.Length < length + 2 || line.StartsWith('#')) continue;
            string hash = line[..length];
            if (!hash.All(Uri.IsHexDigit) || line[length] != ' ') continue;
            string path = line[(length + 1)..].TrimStart(' ', '*').Replace('\\', '/');
            if (path.Length > 0) map[path] = hash.ToLowerInvariant();
        }
        return map;
    }

    private static ScriptResult Verify(ScriptContext context, HashAlgorithmKind kind, CancellationToken ct)
    {
        var args = context.Args;
        string folder = args.PathValue("verifyFolder")!;
        string manifestPath = args.PathValue("manifestIn")!;
        var expected = ParseManifest(File.ReadAllLines(manifestPath), kind);
        if (expected.Count == 0)
            throw new InvalidDataException($"No {FileHashing.Name(kind)} entries were found in {Path.GetFileName(manifestPath)}. Check the algorithm and the file.");
        var table = new ReportTable(L.T("ComparisonScripts.ManifestVerification"), [L.T("Common.Status"), L.T("Common.File"), L.T("Label.Expected"), L.T("Label.Actual")]);
        var statuses = new ConcurrentDictionary<string, (string Status, string Actual)>();
        int done = 0;
        Parallel.ForEach(expected, new ParallelOptions { MaxDegreeOfParallelism = context.WorkerThreads, CancellationToken = ct }, kv =>
        {
            string? safe = PathGuard.SafeRelativePath(kv.Key);
            if (safe == null) { statuses[kv.Key] = (L.T("ComparisonScripts.UnsafePathNotChecked"), ""); return; }
            string full = Path.Combine(folder, safe);
            if (!File.Exists(full)) statuses[kv.Key] = ("Missing", "");
            else
            {
                try
                {
                    string actual = FileHashing.HashFile(full, kind, ct: ct);
                    statuses[kv.Key] = (actual == kv.Value ? "Matches" : "Modified", actual);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    statuses[kv.Key] = ("Unreadable", ex.Message);
                }
            }
            int n = Interlocked.Increment(ref done);
            if (n % 50 == 0) context.Report(n, expected.Count, "verifying");
        });
        foreach (var kv in expected.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            table.Add(statuses[kv.Key].Status, kv.Key, kv.Value, statuses[kv.Key].Actual);
        // Files present but not in the manifest.
        var scan = FileScanner.Scan(folder, recursive: true, ct: ct);
        int extra = 0;
        foreach (var f in scan.Files)
        {
            string rel = Path.GetRelativePath(folder, f.FullName).Replace('\\', '/');
            if (expected.ContainsKey(rel) || string.Equals(f.FullName, manifestPath, StringComparison.OrdinalIgnoreCase)) continue;
            table.Add(L.T("ComparisonScripts.NotInManifest"), rel, "", "");
            extra++;
        }
        int matches = statuses.Values.Count(s => s.Status == "Matches");
        var result = new ScriptResult
        {
            Table = table,
            Processed = expected.Count,
            Succeeded = matches,
            Failed = expected.Count - matches,
            Summary = L.F("ComparisonScripts.0N0Of123", matches, L.Count(expected.Count, "Noun.file"), (expected.Count == 1 ? "matches" : "match"), statuses.Values.Count(s => s.Status == "Modified"), statuses.Values.Count(s => s.Status == "Missing"), extra),
        };
        result.Notes.Add(L.T("ComparisonScripts.MatchingHashesShowIdenticalContentNot"));
        return result;
    }
}
