using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Scripts;

/// <summary>One entry of an archive as inspected.</summary>
public sealed record ArchiveEntryInfo(string Name, long Size, long CompressedSize, DateTimeOffset? Modified, bool IsDirectory, string? Problem);

/// <summary>Script 9: inspects ZIP and TAR archives and extracts them safely.</summary>
public sealed class ArchiveInspectorScript : Script
{
    /// <summary>Entries larger than this many times their compressed size are treated as suspicious ("zip bombs").</summary>
    public const int MaxRatio = 200;

    /// <summary>Largest total size extracted in one run (protects the disk from archives that expand enormously).</summary>
    public const long MaxTotalBytes = 20L * 1024 * 1024 * 1024;

    public const int MaxEntries = 200_000;

    public override string Id => "archive-inspector";
    public override string Name => L.T("ArchiveInspectorScript.ArchiveContentsInspector");
    public override string Description => L.T("ArchiveInspectorScript.ListsWhatAZIPOrTAR");
    public override ScriptCategory Category => ScriptCategory.Archives;
    public override string SupportedInputs => L.T("ArchiveInspectorScript.ZIPZipTARTarAndGzip");
    public override string Limitations => L.T("ArchiveInspectorScript.RARAnd7ZipArchivesAre");
    public override bool ChangesFiles => true;
    public override bool WillChangeFiles(ScriptArguments args) => args.Choice("mode") == "extract";

    public override IReadOnlyList<ScriptParameter> Parameters { get; } =
    [
        new("archive", L.T("Common.Archive"), ScriptParameterKind.File, L.T("ArchiveInspectorScript.TheArchiveToInspect")) { FileFilter = L.T("ArchiveInspectorScript.ArchivesZipTarTarGzTgz") },
        new("mode", L.T("Label.Task"), ScriptParameterKind.Choice, L.T("ArchiveInspectorScript.OnlyInspectOrAlsoPrepareExtracting"))
        {
            Default = "inspect", Choices = [new("inspect", L.T("ArchiveInspectorScript.InspectOnly")), new("extract", L.T("ArchiveInspectorScript.PrepareExtraction"))],
        },
        new("destination", L.T("ArchiveInspectorScript.ExtractTo"), ScriptParameterKind.OutputFolder, L.T("ArchiveInspectorScript.FolderTheSafeEntriesAreExtracted")) { VisibleWhen = "mode=extract" },
    ];

    public override ScriptResult Run(ScriptContext context, CancellationToken ct)
    {
        var args = context.Args;
        string archive = args.PathValue("archive")!;
        var entries = Inspect(archive, ct);
        var table = new ReportTable(L.T("ArchiveInspectorScript.ArchiveContents"), [L.T("Label.Entry"), L.T("Common.Size"), L.T("Label.Compressed"), L.T("Choice.Sort.Modified"), L.T("Label.Problem")]);
        foreach (var e in entries)
            table.Add(e.Name, e.IsDirectory ? "(folder)" : e.Size.ToString(CultureInfo.InvariantCulture), e.CompressedSize >= 0 ? e.CompressedSize.ToString(CultureInfo.InvariantCulture) : "",
                e.Modified?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "", e.Problem ?? "");
        long total = entries.Where(e => !e.IsDirectory && e.Problem == null).Sum(e => e.Size);
        int unsafeCount = entries.Count(e => e.Problem != null);
        var result = new ScriptResult
        {
            Table = table,
            Processed = entries.Count,
            Succeeded = entries.Count - unsafeCount,
            Failed = unsafeCount,
            Summary = L.F("ArchiveInspectorScript.0And12WhenExtracted", L.Count(entries.Count(e => !e.IsDirectory), "Noun.file"), L.Count(entries.Count(e => e.IsDirectory), "Noun.folder"), ReportWriter.FormatSize(total), unsafeCount),
        };
        if (args.Choice("mode") == "extract")
        {
            string destination = args.PathValue("destination")!;
            if (total > MaxTotalBytes) throw new InvalidDataException($"The archive would expand to {ReportWriter.FormatSize(total)}, more than the limit of {ReportWriter.FormatSize(MaxTotalBytes)} per extraction.");
            var drive = new DriveInfo(Path.GetPathRoot(destination)!);
            if (drive.IsReady && drive.AvailableFreeSpace < total + 100L * 1024 * 1024)
                throw new IOException($"The drive of {destination} has {ReportWriter.FormatSize(drive.AvailableFreeSpace)} free; the archive needs {ReportWriter.FormatSize(total)}.");
            result.Plan = [];
            for (int index = 0; index < entries.Count; index++)
            {
                var e = entries[index];
                if (e.IsDirectory) continue;
                result.Plan.Add(new PlannedChange
                {
                    Action = L.T("Common.Extract"),
                    Source = e.Name,
                    Target = e.Problem == null ? PathGuard.ResolveInside(destination, e.Name) : null,
                    Detail = e.Problem ?? (File.Exists(Path.Combine(destination, PathGuard.SafeRelativePath(e.Name) ?? "")) ? L.T("ArchiveInspectorScript.ExistsSettingsFileSafetyDecides") : ""),
                    Blocked = e.Problem != null,
                    Selected = e.Problem == null,
                    // Entries are identified by their position: an archive may hold two entries with the same name.
                    Check = index.ToString(CultureInfo.InvariantCulture),
                });
            }
            result.OutputFolder = destination;
            result.Summary += L.T("ArchiveInspectorScript.ReviewTheListAndApplyTo");
        }
        return result;
    }

    /// <summary>Reads the archive's directory without extracting anything.</summary>
    public static List<ArchiveEntryInfo> Inspect(string archive, CancellationToken ct)
    {
        var list = new List<ArchiveEntryInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, long size, long compressed, DateTimeOffset? modified, bool dir, string? problem)
        {
            if (list.Count >= MaxEntries) throw new InvalidDataException($"The archive has more than {MaxEntries:N0} entries; it is not inspected further.");
            string? safe = PathGuard.SafeRelativePath(name.TrimEnd('/', '\\'), out string? why);
            if (problem == null && safe == null) problem = L.T("ArchiveInspectorScript.UnsafePath") + why;
            if (name.StartsWith('/') || name.StartsWith('\\') || (name.Length > 1 && name[1] == ':')) problem ??= L.T("ArchiveInspectorScript.AbsolutePath");
            if (problem == null && !dir && !seen.Add(safe!)) problem = L.T("ArchiveInspectorScript.DuplicateNameWouldOverwriteAnEarlier");
            if (problem == null && !dir && compressed > 0 && size / compressed > MaxRatio) problem = L.F("ArchiveInspectorScript.SuspiciousCompressionRatio0N01", size / compressed);
            list.Add(new ArchiveEntryInfo(name, size, compressed, modified, dir, problem));
        }
        var kind = FileSignatures.Identify(archive).Format;
        if (kind == FileSignatures.Zip)
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var e in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                bool dir = e.FullName.EndsWith('/') && e.Length == 0;
                Add(e.FullName, e.Length, e.CompressedLength, e.LastWriteTime, dir, e.IsEncrypted ? L.T("ArchiveInspectorScript.EncryptedCannotBeExtracted") : null);
            }
        }
        else if (kind == FileSignatures.Tar || kind == FileSignatures.GZip)
        {
            using var file = File.OpenRead(archive);
            using Stream stream = kind == FileSignatures.GZip ? new GZipStream(file, CompressionMode.Decompress) : file;
            using var tar = new TarReader(stream);
            TarEntry? e;
            while ((e = tar.GetNextEntry(copyData: false)) != null)
            {
                ct.ThrowIfCancellationRequested();
                string? problem = e.EntryType switch
                {
                    TarEntryType.Directory or TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile => null,
                    TarEntryType.SymbolicLink or TarEntryType.HardLink => L.T("ArchiveInspectorScript.LinkNeverExtracted"),
                    _ => L.F("ArchiveInspectorScript.0EntryNotExtracted", e.EntryType),
                };
                Add(e.Name, e.Length, -1, e.ModificationTime, e.EntryType == TarEntryType.Directory, problem);
            }
        }
        else
        {
            throw new InvalidDataException($"{Path.GetFileName(archive)} is not a ZIP or TAR archive{(kind != null ? $" (it is a {kind.Name})" : "")}.");
        }
        return list;
    }

    /// <summary>Extracts the selected safe entries through the output policy (existing files, backups, temporary files).</summary>
    public override ScriptResult Apply(ScriptContext context, IReadOnlyList<PlannedChange> changes, CancellationToken ct)
    {
        string archive = context.Args.PathValue("archive")!;
        var wanted = changes.Where(c => c.Selected && !c.Blocked && c.Target != null && c.Check != null)
            .ToDictionary(c => int.Parse(c.Check!, System.Globalization.CultureInfo.InvariantCulture), c => c.Target!);
        var result = new ScriptResult { OutputFolder = context.Args.PathValue("destination") };
        using var output = context.CreateOutput("Archive extraction");
        long written = 0;
        int index = -1;
        void Extract(string name, Func<Stream> open, long size)
        {
            index++;
            if (!wanted.TryGetValue(index, out string? target)) return;
            ct.ThrowIfCancellationRequested();
            result.Processed++;
            context.Report(result.Processed, wanted.Count, Path.GetFileName(name));
            try
            {
                string? path = output.WriteFile(target, temp =>
                {
                    using var source = open();
                    using var dest = File.Create(temp);
                    // Never trust the declared size: stop at the limit even if the data keeps coming.
                    var buffer = new byte[1 << 16];
                    long copied = 0;
                    int n;
                    while ((n = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        copied += n;
                        written += n;
                        if (copied > size + 1 || written > MaxTotalBytes) throw new InvalidDataException("the entry is larger than declared; extraction stopped");
                        dest.Write(buffer, 0, n);
                    }
                }, temp =>
                {
                    if (new FileInfo(temp).Length != size) throw new InvalidDataException("the extracted size differs from the archive's directory");
                });
                if (path != null)
                {
                    result.Succeeded++;
                    result.OutputFiles.Add(path);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                result.Failed++;
                result.Errors.Add($"{name}: {ex.Message}");
                if (context.StopOnError) throw;
            }
        }
        var kind = FileSignatures.Identify(archive).Format;
        if (kind == FileSignatures.Zip)
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var e in zip.Entries) Extract(e.FullName, e.Open, e.Length);
        }
        else
        {
            using var file = File.OpenRead(archive);
            using Stream stream = kind == FileSignatures.GZip ? new GZipStream(file, CompressionMode.Decompress) : file;
            using var tar = new TarReader(stream);
            TarEntry? e;
            while ((e = tar.GetNextEntry(copyData: false)) != null)
            {
                // Every entry counts for the position, including those that are never extracted.
                var data = e.DataStream;
                bool regular = data != null && e.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile;
                if (regular) Extract(e.Name, () => new NonClosingStream(data!), e.Length);
                else index++;
            }
        }
        result.ChangesApplied = result.Succeeded > 0;
        result.BackupFolder = output.BackupFolder;
        result.Summary = L.F("ArchiveInspectorScript.0N0Of1Extracted2", result.Succeeded, L.Count(wanted.Count, "Noun.file"), output.Describe());
        return result;
    }

    /// <summary>Lets a TAR entry stream be read through code that disposes it.</summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
