using System.Buffers.Binary;
using System.Globalization;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Rcc;

/// <summary>Result of extracting resources to a folder.</summary>
public sealed class RccExtractResult
{
    public int Written { get; set; }
    public List<string> Notes { get; } = [];
    public List<string> Failures { get; } = [];
    public string? ManifestPath { get; set; }
}

/// <summary>Result of updating an archive from a folder.</summary>
public sealed class RccImportResult
{
    public List<string> Replaced { get; } = [];
    public List<string> Added { get; } = [];
    public int Unchanged { get; set; }
    public List<string> Skipped { get; } = [];
    public List<string> Warnings { get; } = [];
}

/// <summary>Extraction, folder import and content checks for the RCC Editor.</summary>
public static class RccOperations
{
    public const string ManifestFileName = "rcc-manifest.csv";

    /// <summary>
    /// Writes resources below <paramref name="folder"/>, keeping their folder structure (so two
    /// resources with the same name in different folders never overwrite each other), and a
    /// manifest with each file's size, compression and SHA-256. Zstandard-compressed resources are
    /// written as their raw frame with ".zst" appended.
    /// </summary>
    public static RccExtractResult Extract(IReadOnlyList<RccEntry> entries, string folder, OutputSession output, bool writeManifest = true,
        IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        var result = new RccExtractResult();
        var manifest = new ReportTable(L.T("RccOperations.ResourcesExtractedFromTheRCCFile"), ["Resource", "File", "Size", L.T("RccOperations.StoredSize"), "Compression", "SHA-256", L.T("RccOperations.ModifiedUTC")]);
        for (int i = 0; i < entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var e = entries[i];
            progress?.Report(new OperationProgress(i, entries.Count, e.Path));
            try
            {
                byte[] content;
                string relative = e.Path.TrimStart('/');
                if (e.CanReadContent)
                {
                    content = e.GetContent();
                }
                else
                {
                    content = e.Stored;
                    relative += ".zst";
                    result.Notes.Add(L.F("RccOperations.0IsZstandardCompressedItWas", e.Path, Path.GetFileName(relative)));
                }
                string target = PathGuard.ResolveInside(folder, relative);
                string? written = output.WriteAllBytes(target, content, temp =>
                {
                    if (new FileInfo(temp).Length != content.Length) throw new IOException($"{e.Path} was not written completely.");
                });
                if (written == null) continue;
                result.Written++;
                manifest.Add(e.Path, PathGuard.ToPortable(folder, written), content.Length.ToString(CultureInfo.InvariantCulture),
                    e.StoredSize.ToString(CultureInfo.InvariantCulture), e.Compression.ToString(), FileHashing.HashBytes(content),
                    e.LastModifiedUtc?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
            {
                result.Failures.Add($"{e.Path}: {ex.Message}");
            }
        }
        progress?.Report(new OperationProgress(entries.Count, entries.Count, "done"));
        if (writeManifest && result.Written > 0)
        {
            manifest.Summary.Add(L.F("RccOperations.0WrittenBy1On2", L.Count(result.Written, "Noun.resource"), AppIdentity.NameAndVersion, DateTime.Now));
            string path = Path.Combine(folder, ManifestFileName);
            string? written = output.WriteFile(path, temp => File.WriteAllText(temp, ReportWriter.ToCsv(manifest)));
            result.ManifestPath = written;
        }
        return result;
    }

    /// <summary>
    /// Updates an archive from a folder laid out like an extraction: files whose path matches a
    /// resource replace it when their content differs; with <paramref name="addNew"/>, other files
    /// are added. Links, hidden manifest files and unsafe names are skipped.
    /// </summary>
    public static RccImportResult ImportFolder(RccArchive archive, string folder, bool addNew, RccCompressionChoice compression, CancellationToken ct = default)
    {
        var result = new RccImportResult();
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System, IgnoreInaccessible = true };
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options).OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(folder, file.FullName);
            if (string.Equals(relative, ManifestFileName, StringComparison.OrdinalIgnoreCase)) continue;
            string? resourcePath = RccFormat.NormalizePath(relative);
            if (resourcePath == null || relative.EndsWith(".zst", StringComparison.OrdinalIgnoreCase))
            {
                result.Skipped.Add(relative);
                continue;
            }
            byte[] content = File.ReadAllBytes(file.FullName);
            var existing = archive.Find(resourcePath);
            if (existing != null)
            {
                if (!existing.CanReadContent) { result.Skipped.Add(relative + L.T("RccOperations.TheResourceIsZstandardCompressedAnd")); continue; }
                if (existing.GetContent().AsSpan().SequenceEqual(content)) { result.Unchanged++; continue; }
                result.Warnings.AddRange(CheckReplacement(existing, content));
                archive.Replace(existing, content, compression);
                result.Replaced.Add(resourcePath);
            }
            else if (addNew)
            {
                try
                {
                    archive.Add(resourcePath, content, compression, (ulong)new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds());
                    result.Added.Add(resourcePath);
                }
                catch (ArgumentException ex)
                {
                    result.Skipped.Add($"{relative} ({ex.Message})");
                }
            }
            else
            {
                result.Skipped.Add(relative + L.T("RccOperations.NoResourceWithThisPath"));
            }
        }
        return result;
    }

    /// <summary>Builds a new archive from all files in a folder (paths relative to the folder).</summary>
    public static RccArchive CreateFromFolder(string folder, int version, RccCompressionChoice compression, CancellationToken ct = default)
    {
        var archive = RccArchive.Create(version);
        var result = ImportFolder(archive, folder, addNew: true, compression, ct);
        if (archive.Entries.Count == 0) throw new InvalidDataException("The folder contains no files that can be stored in a resource file." + (result.Skipped.Count > 0 ? " Skipped: " + string.Join(", ", result.Skipped.Take(5)) : ""));
        return archive;
    }

    /// <summary>
    /// Warnings about a replacement that may break the program using the file: content in a
    /// different format than the resource's extension, or an image with different dimensions.
    /// </summary>
    public static IReadOnlyList<string> CheckReplacement(RccEntry entry, byte[] content)
    {
        var warnings = new List<string>();
        var found = FileSignatures.Identify(content.AsSpan(0, Math.Min(content.Length, FileSignatures.HeaderLength)), entry.Name, content.Length);
        if (found.Format != null && found.Evidence == FormatEvidence.Signature && found.Format.Category != "Text" && !found.Format.ExtensionMatches(entry.Name))
            warnings.Add(L.F("RccOperations.0TheNewContentIsA", entry.Path, found.Format.Name, entry.Extension));
        if (entry.CanReadContent && ImageSize(content) is { } newSize && ImageSize(entry.GetContent()) is { } oldSize && newSize != oldSize)
            warnings.Add(L.F("RccOperations.0TheNewImageIs1", entry.Path, newSize.Width, newSize.Height, oldSize.Width, oldSize.Height));
        return warnings;
    }

    /// <summary>Width and height from a PNG, GIF, BMP or JPEG header (null for other content).</summary>
    public static (int Width, int Height)? ImageSize(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return (BinaryPrimitives.ReadInt32BigEndian(data[16..]), BinaryPrimitives.ReadInt32BigEndian(data[20..]));
        if (data.Length >= 10 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
            return (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]));
        if (data.Length >= 26 && data[0] == 'B' && data[1] == 'M')
            return (BinaryPrimitives.ReadInt32LittleEndian(data[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[22..])));
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            int i = 2;
            while (i + 9 < data.Length)
            {
                if (data[i] != 0xFF) return null;
                byte marker = data[i + 1];
                int length = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 2)..]);
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    return (BinaryPrimitives.ReadUInt16BigEndian(data[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(i + 5)..]));
                i += 2 + length;
            }
        }
        return null;
    }
}
