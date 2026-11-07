using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using ImageMagick;
using TibiaFoundry.Core.FileOps;
using F = TibiaFoundry.Core.FileMetadata.MetadataFields;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.FileMetadata.Providers;

/// <summary>ZIP archives: entries, sizes, dates and encryption (read only), and the archive comment (editable).</summary>
public sealed class ZipMetadataProvider : IMetadataProvider
{
    private const int ListedEntries = 200;

    public string Id => "zip";
    public string Name => L.T("OtherProviders.ZIPArchives");
    public IReadOnlyList<string> Extensions { get; } = [".zip", ".jar", ".apk", ".nupkg", ".cbz", ".aec"];
    public MetadataCapabilities Capabilities => MetadataCapabilities.Editable;
    public string Limitations => L.T("OtherProviders.TheArchiveCommentIsTheOnly");

    public bool CanHandle(string path, FormatIdentification identification) =>
        (identification.Format?.Id == "zip" || Extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) && SafeXml.IsZip(path);

    public MetadataReadResult Read(string path, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(path);
        var files = zip.Entries.Where(e => !e.FullName.EndsWith('/')).ToList();
        long size = files.Sum(e => e.Length), packed = files.Sum(e => e.CompressedLength);
        var times = files.Select(e => e.LastWriteTime.LocalDateTime).ToList();
        int encrypted = files.Count(e => e.IsEncrypted);
        var warnings = new List<string>();
        if (zip.Entries.Count > ListedEntries) warnings.Add(L.F("OtherProviders.TheArchiveHas0N0Entries", zip.Entries.Count, ListedEntries));
        if (encrypted > 0) warnings.Add(L.F("OtherProviders.0N0EntriesArePasswordProtected", encrypted));
        var fields = new List<MetadataField>
        {
            new(F.ArchiveComment, string.IsNullOrEmpty(zip.Comment) ? null : zip.Comment, encrypted == 0, encrypted == 0),
            new(F.Info("entries", L.T("Common.Files"), F.Archive), files.Count.ToString("N0", CultureInfo.InvariantCulture), false),
            new(F.Info("folders", L.T("OtherProviders.Folders"), F.Archive), (zip.Entries.Count - files.Count).ToString("N0", CultureInfo.InvariantCulture), false),
            new(F.Info("unpackedSize", L.T("OtherProviders.UnpackedSize"), F.Archive), ReportWriter.FormatSize(size), false),
            new(F.Info("packedSize", L.T("OtherProviders.PackedSize"), F.Archive), $"{ReportWriter.FormatSize(packed)}{(size > 0 ? $" ({packed * 100.0 / size:0.#} %)" : "")}", false),
            new(F.Info("oldestEntry", L.T("OtherProviders.OldestEntry"), F.Archive), times.Count > 0 ? MetadataValues.FormatDate(times.Min()) : null, false),
            new(F.Info("newestEntry", L.T("OtherProviders.NewestEntry"), F.Archive), times.Count > 0 ? MetadataValues.FormatDate(times.Max()) : null, false),
            new(F.Info("encryptedEntries", L.T("OtherProviders.PasswordProtectedEntries"), F.Archive), encrypted.ToString("N0", CultureInfo.InvariantCulture), false),
            new(F.Info("contents", L.T("OtherProviders.Contents"), F.Archive), string.Join("\n", zip.Entries.Take(ListedEntries).Select(e => $"{e.FullName}  ({ReportWriter.FormatSize(e.Length)}, {MetadataValues.FormatDate(e.LastWriteTime.LocalDateTime)})")), false),
        };
        return new MetadataReadResult(L.T("OtherProviders.ZIPArchive"), fields, warnings);
    }

    public IReadOnlyList<string> WriteWarnings(string path, IReadOnlyList<MetadataEdit> edits) => [L.T("OtherProviders.TheArchiveSDirectoryIsRewritten")];

    public void Write(string path, IReadOnlyList<MetadataEdit> edits, CancellationToken ct)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        foreach (var e in edits)
        {
            if (e.Key != F.ArchiveComment.Key) throw new NotSupportedException($"\"{e.Key}\" cannot be written to a ZIP archive.");
            zip.Comment = e.Value ?? "";
        }
    }
}

/// <summary>TAR archives (also .tar.gz/.tgz), read only: entries, sizes, owners, permissions and dates.</summary>
public sealed class TarMetadataProvider : IMetadataProvider
{
    public string Id => "tar";
    public string Name => L.T("OtherProviders.TARArchivesReadOnly");
    public IReadOnlyList<string> Extensions { get; } = [".tar", ".tgz", ".tar.gz"];
    public MetadataCapabilities Capabilities => MetadataCapabilities.ReadOnly;
    public string Limitations => L.T("OtherProviders.ReadOnlyOtherCompressionsTarXz");

    private static bool Gzipped(string path) => path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

    public bool CanHandle(string path, FormatIdentification identification) =>
        identification.Format?.Id == "tar" || Path.GetExtension(path).Equals(".tar", StringComparison.OrdinalIgnoreCase) || Gzipped(path);

    public MetadataReadResult Read(string path, CancellationToken ct)
    {
        using var file = File.OpenRead(path);
        using Stream stream = Gzipped(path) ? new GZipStream(file, CompressionMode.Decompress) : file;
        using var reader = new TarReader(stream);
        int files = 0, folders = 0, other = 0;
        long size = 0;
        var times = new List<DateTime>();
        var owners = new HashSet<string>(StringComparer.Ordinal);
        var formats = new HashSet<string>(StringComparer.Ordinal);
        var listed = new List<string>();
        while (reader.GetNextEntry() is { } entry)
        {
            ct.ThrowIfCancellationRequested();
            formats.Add(entry.Format.ToString());
            if (entry.EntryType is TarEntryType.Directory) folders++;
            else if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile) { files++; size += entry.Length; }
            else other++;
            times.Add(entry.ModificationTime.LocalDateTime);
            if (entry is PosixTarEntry posix && !string.IsNullOrEmpty(posix.UserName)) owners.Add(posix.UserName);
            if (listed.Count < 200) listed.Add($"{entry.Name}  ({entry.EntryType}, {Convert.ToString((int)entry.Mode, 8)}, {ReportWriter.FormatSize(entry.Length)})");
        }
        return new MetadataReadResult(Gzipped(path) ? L.T("OtherProviders.TARArchiveGzip") : L.T("OtherProviders.TARArchive"),
        [
            new(F.Info("entries", L.T("Common.Files"), F.Archive), files.ToString("N0", CultureInfo.InvariantCulture), false),
            new(F.Info("folders", L.T("OtherProviders.Folders"), F.Archive), folders.ToString("N0", CultureInfo.InvariantCulture), false),
            new(F.Info("otherEntries", L.T("OtherProviders.LinksAndOtherEntries"), F.Archive), other.ToString("N0", CultureInfo.InvariantCulture), false),
            new(F.Info("unpackedSize", L.T("OtherProviders.UnpackedSize"), F.Archive), ReportWriter.FormatSize(size), false),
            new(F.Info("oldestEntry", L.T("OtherProviders.OldestEntry"), F.Archive), times.Count > 0 ? MetadataValues.FormatDate(times.Min()) : null, false),
            new(F.Info("newestEntry", L.T("OtherProviders.NewestEntry"), F.Archive), times.Count > 0 ? MetadataValues.FormatDate(times.Max()) : null, false),
            new(F.Info("owners", L.T("OtherProviders.Owners"), F.Archive), owners.Count > 0 ? string.Join(", ", owners) : null, false),
            new(F.Info("tarFormat", L.T("OtherProviders.TARFormat"), F.Technical), string.Join(", ", formats), false),
            new(F.Info("contents", L.T("OtherProviders.Contents"), F.Archive), string.Join("\n", listed), false),
        ]);
    }
}

/// <summary>Windows programs and libraries (EXE, DLL…), read only: version resources, machine, subsystem, .NET and whether a digital signature is present.</summary>
public sealed class PortableExecutableProvider : IMetadataProvider
{
    public string Id => "pe";
    public string Name => L.T("OtherProviders.WindowsProgramsAndLibrariesReadOnly");
    public IReadOnlyList<string> Extensions { get; } = [".exe", ".dll", ".sys", ".ocx", ".scr", ".cpl", ".msi"];
    public MetadataCapabilities Capabilities => MetadataCapabilities.ReadOnly;
    public string Limitations => L.T("OtherProviders.ReadOnlyChangingAProgramS");

    public bool CanHandle(string path, FormatIdentification identification) =>
        identification.Format?.Id == "pe" || (Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()) && !path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));

    public MetadataReadResult Read(string path, CancellationToken ct)
    {
        var fields = new List<MetadataField>();
        var warnings = new List<string>();
        using (var stream = File.OpenRead(path))
        using (var pe = new PEReader(stream))
        {
            var headers = pe.PEHeaders;
            if (headers.PEHeader == null) throw new InvalidDataException("Not a Windows program or library (no PE header).");
            var security = headers.PEHeader.CertificateTableDirectory;
            fields.Add(new(F.Signature, security.Size > 0 ? L.T("OtherProviders.PresentAuthenticodeNotVerifiedHere") : L.T("Scripts.None"), false));
            fields.Add(new(F.Info("machine", L.T("OtherProviders.Machine"), F.Program), headers.CoffHeader.Machine switch
            {
                Machine.Amd64 => "x64", Machine.I386 => "x86", Machine.Arm64 => "ARM64", Machine.Arm or Machine.ArmThumb2 => "ARM", var m => m.ToString(),
            }, false));
            fields.Add(new(F.Info("subsystem", L.T("OtherProviders.Subsystem"), F.Program), headers.PEHeader.Subsystem.ToString(), false));
            fields.Add(new(F.Info("kind", L.T("Common.Kind"), F.Program), headers.IsDll ? L.T("OtherProviders.LibraryDLL") : headers.IsExe ? "Program" : "Other", false));
            fields.Add(new(F.Info("dotnet", L.T("OtherProviders.NETAssembly"), F.Program), headers.CorHeader != null ? L.T("Metadata.Yes") : L.T("Label.No"), false));
            fields.Add(new(F.Info("linkTime", L.T("OtherProviders.LinkTimeStamp"), F.Technical), L.F("OtherProviders.0x0X8OftenABuild", headers.CoffHeader.TimeDateStamp), false));
        }
        var v = FileVersionInfo.GetVersionInfo(path);
        string? C(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        fields.Add(new(F.DescriptionText, C(v.FileDescription), false));
        fields.Add(new(F.Info("productName", L.T("OtherProviders.Product"), F.Program), C(v.ProductName), false));
        fields.Add(new(F.Company, C(v.CompanyName), false));
        fields.Add(new(F.Copyright, C(v.LegalCopyright), false));
        fields.Add(new(F.Info("fileVersion", L.T("OtherProviders.FileVersion"), F.Program), C(v.FileVersion), false));
        fields.Add(new(F.Info("productVersion", L.T("OtherProviders.ProductVersion"), F.Program), C(v.ProductVersion), false));
        fields.Add(new(F.Info("originalFilename", L.T("OtherProviders.OriginalFileName"), F.Program), C(v.OriginalFilename), false));
        fields.Add(new(F.Info("internalName", L.T("OtherProviders.InternalName"), F.Program), C(v.InternalName), false));
        fields.Add(new(F.Comment, C(v.Comments), false));
        fields.Add(new(F.Language, C(v.Language), false));
        warnings.Add(L.T("OtherProviders.ProgramFilesAreOnlyReadEditing"));
        return new MetadataReadResult(L.T("OtherProviders.WindowsProgramOrLibrary"), fields, warnings);
    }
}

/// <summary>Images that TagLib# does not read (WebP, icons, AVIF, HEIC…), through ImageMagick, read only: size, color, and the EXIF, XMP, IPTC and ICC profiles.</summary>
public sealed class RasterImageProvider : IMetadataProvider
{
    public string Id => "magick";
    public string Name => L.T("OtherProviders.OtherImagesBMPWebPICOAVIF");
    public IReadOnlyList<string> Extensions { get; } = [".bmp", ".dib", ".webp", ".ico", ".cur", ".avif", ".heic", ".heif", ".jxl", ".psd", ".tga", ".dds", ".qoi", ".jp2"];
    public MetadataCapabilities Capabilities => MetadataCapabilities.ReadOnly;
    public string Limitations => L.T("OtherProviders.ReadOnlyWritingTheseFormatsWould");

    public bool CanHandle(string path, FormatIdentification identification) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()) || identification.Format?.Id is "webp" or "ico" or "bmp";

    public MetadataReadResult Read(string path, CancellationToken ct)
    {
        using var images = new MagickImageCollection();
        images.Ping(path);
        if (images.Count == 0) throw new InvalidDataException("No image was found in the file.");
        var image = images[0];
        var fields = new List<MetadataField>
        {
            new(F.Width, image.Width.ToString(CultureInfo.InvariantCulture), false),
            new(F.Height, image.Height.ToString(CultureInfo.InvariantCulture), false),
            new(F.Info("frames", L.T("OtherProviders.ImagesOrFrames"), F.Image), images.Count.ToString(CultureInfo.InvariantCulture), false),
            new(F.Info("colorSpace", L.T("OtherProviders.ColorSpace"), F.Image), image.ColorSpace.ToString(), false),
        };
        var exif = image.GetExifProfile();
        if (exif != null)
        {
            string? Tag<T>(ExifTag<T> tag) => exif.GetValue(tag)?.Value?.ToString() is { Length: > 0 } s ? s.Trim() : null;
            fields.Add(new(F.DescriptionText, Tag(ExifTag.ImageDescription), false));
            fields.Add(new(F.Authors, Tag(ExifTag.Artist), false));
            fields.Add(new(F.Copyright, Tag(ExifTag.Copyright), false));
            fields.Add(new(F.Make, Tag(ExifTag.Make), false));
            fields.Add(new(F.Model, Tag(ExifTag.Model), false));
            fields.Add(new(F.Software, Tag(ExifTag.Software), false));
            if (Tag(ExifTag.DateTimeOriginal) is { } taken && DateTime.TryParseExact(taken, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                fields.Add(new(F.DateTaken, MetadataValues.FormatDate(d), false));
        }
        var profiles = new List<string>();
        if (exif != null) profiles.Add("EXIF");
        if (image.GetXmpProfile() != null) profiles.Add("XMP");
        if (image.GetIptcProfile() != null) profiles.Add("IPTC");
        var icc = image.GetColorProfile();
        if (icc != null) profiles.Add("ICC");
        fields.Add(new(F.TagTypes, profiles.Count > 0 ? string.Join(", ", profiles) : L.T("Scripts.None"), false));
        if (icc != null) fields.Add(new(F.Info("iccProfile", L.T("OtherProviders.ColorProfile"), F.Image), icc.Description ?? icc.Model ?? "Present", false));
        return new MetadataReadResult(L.F("OtherProviders.0Image", image.Format), fields, [L.T("OtherProviders.ThisImageFormatIsOnlyRead")]);
    }
}

/// <summary>
/// Tibia client files, read only, through Tibia Foundry's own readers: sprite sheets and other CIP
/// LZMA containers, appearances and other protobuf data, Qt resource files, legacy Tibia.dat and
/// Tibia.spr, and the client's catalogs. Undocumented binary structures are never written.
/// </summary>
public sealed class TibiaMetadataProvider : IMetadataProvider
{
    private static readonly string[] Names = ["catalog-content.json", "catalog-sound.json", "package.json", "assets.json"];

    public string Id => "tibia";
    public string Name => L.T("OtherProviders.TibiaClientDataReadOnly");
    public IReadOnlyList<string> Extensions { get; } = [".lzma", ".rcc", ".spr", ".dat", "catalog-content.json", "package.json"];
    public MetadataCapabilities Capabilities => MetadataCapabilities.ReadOnly;
    public string Limitations => L.T("OtherProviders.ReadOnlyTheEditorsAssetsEditor");

    public bool CanHandle(string path, FormatIdentification identification) =>
        (identification.Format?.Category is { } category && (category == FileOps.FileInspector.ClientProtobuf.Category || category == FileOps.FileSignatures.QtResource.Category)) || Names.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
        || (Path.GetExtension(path).Equals(".dat", StringComparison.OrdinalIgnoreCase) && identification.Format == null);

    public MetadataReadResult Read(string path, CancellationToken ct)
    {
        var inspection = FileInspector.Inspect(path, decodeImages: false, ct);
        var fields = new List<MetadataField>();
        foreach (var (label, value) in inspection.Details)
        {
            // The key follows the label, so the same detail of two files lines up in comparisons and exports.
            string key = "tibia." + new string(label.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (fields.All(f => f.Key != key)) fields.Add(new(F.Info(key, label, F.Tibia), value, false));
        }
        fields.Add(new(F.Info("tibia.check", L.T("OtherProviders.Checked"), F.Tibia), inspection.LevelText, false));
        return new MetadataReadResult(inspection.Format?.Name ?? L.T("OtherProviders.TibiaClientFile"), fields, inspection.Problems);
    }
}
