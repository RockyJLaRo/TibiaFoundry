using System.Globalization;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Metadata;
using F = TibiaFoundry.Core.FileMetadata.MetadataFields;
using TagTypes = TagLib.TagTypes;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.FileMetadata.Providers;

/// <summary>
/// Images, audio and video through TagLib# (8.6): EXIF, XMP and IPTC of JPEG, TIFF, PNG and GIF;
/// ID3, Vorbis comments, MP4, Matroska, ASF, APE and RIFF tags of audio and video. Only the tag
/// blocks are rewritten; image, audio and video data are copied unchanged, never re-encoded.
/// </summary>
public sealed class TagLibMetadataProvider : IMetadataProvider
{
    /// <summary>Formats whose tags are written (each was written, read back and verified in the tests).</summary>
    private static readonly HashSet<string> Writable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".gif", ".tif", ".tiff",
        ".mp3", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".m4b", ".mp4", ".m4v", ".mov", ".wav", ".aif", ".aiff",
        ".wma", ".wmv", ".asf", ".mkv", ".mka", ".webm", ".avi", ".wv",
    };

    /// <summary>Formats that are only read: no tag storage (BMP, MPEG streams, raw AAC) or tags that are risky to rewrite (camera raw files).</summary>
    private static readonly HashSet<string> ReadOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        ".aac", ".mpg", ".mpeg", ".mpc", ".spx", ".ape", ".dng", ".cr2", ".nef", ".pef", ".arw", ".orf", ".rw2",
    };

    public string Id => "taglib";
    public string Name => L.T("TagLibMetadataProvider.ImagesAudioAndVideoTagLib");
    public IReadOnlyList<string> Extensions { get; } = [.. Writable.Concat(ReadOnly).Order(StringComparer.Ordinal)];
    public MetadataCapabilities Capabilities => MetadataCapabilities.Editable;
    public string Limitations => L.T("TagLibMetadataProvider.CameraRawFilesMPEGStreamsRaw");

    public bool CanHandle(string path, FormatIdentification identification) => Writable.Contains(Path.GetExtension(path)) || ReadOnly.Contains(Path.GetExtension(path));

    public MetadataCapabilities CapabilitiesFor(string path) => Writable.Contains(Path.GetExtension(path)) ? MetadataCapabilities.Editable : MetadataCapabilities.ReadOnly;

    /// <summary>QuickTime movies are ISO media files: TagLib# reads them as MP4 (it has no "mov" type of its own).</summary>
    private static TagLib.File Open(string path) => TagLib.File.Create(new TagLib.File.LocalFileAbstraction(path),
        Path.GetExtension(path).Equals(".mov", StringComparison.OrdinalIgnoreCase) ? "taglib/mp4" : null, TagLib.ReadStyle.Average);

    public MetadataReadResult Read(string path, CancellationToken ct)
    {
        bool editable = Writable.Contains(Path.GetExtension(path));
        var fields = new List<MetadataField>();
        var warnings = new List<string>();
        string format;
        try
        {
            using var file = Open(path);
            format = Describe(file);
            if (file is TagLib.Image.File image) ReadImage(image, fields, editable);
            else ReadMedia(file, fields, editable);
            ReadProperties(file, fields);
            fields.Add(new(F.TagTypes, file.TagTypesOnDisk == TagTypes.None ? L.T("Scripts.None") : file.TagTypesOnDisk.ToString(), false));
            if (file.PossiblyCorrupt) warnings.AddRange(file.CorruptionReasons.Select(r => L.T("TagLibMetadataProvider.PossiblyDamaged") + r));
        }
        catch (TagLib.UnsupportedFormatException ex)
        {
            throw new InvalidDataException("The format is not supported here: " + ex.Message, ex);
        }
        catch (TagLib.CorruptFileException ex)
        {
            throw new InvalidDataException("The file is damaged: " + ex.Message, ex);
        }
        // Tibia Foundry's own attribution (PNG and GIF exports): shown, and checked to survive every save.
        string ext = Path.GetExtension(path);
        if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase) || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase))
        {
            var found = AttributionReader.Read(path);
            if (found != null && (found.Creators.Count > 0 || found.Text != null))
                fields.Add(new(AttributionField, string.Join(" · ", new[] { found.Text, found.Copyright, found.Software }.Where(s => !string.IsNullOrWhiteSpace(s))), false));
        }
        if (!editable) warnings.Add(ReadOnlyReason(ext));
        return new MetadataReadResult(format, fields, warnings);
    }

    public static readonly MetadataFieldDefinition AttributionField = F.Info("tf.attribution", L.T("TagLibMetadataProvider.TibiaFoundryAttribution"), F.Rights) with { Help = L.T("TagLibMetadataProvider.WrittenByTibiaFoundrySExports") };

    private static string ReadOnlyReason(string ext) => ext.ToLowerInvariant() switch
    {
        ".aac" or ".mpg" or ".mpeg" => L.T("TagLibMetadataProvider.RawStreamsOfThisKindHave"),
        _ => L.T("TagLibMetadataProvider.CameraRawAndRarelyUsedFormats"),
    };

    private static string Describe(TagLib.File file)
    {
        string mime = file.MimeType ?? "";
        string kind = mime.StartsWith("taglib/", StringComparison.Ordinal) ? mime[7..].ToUpperInvariant() : mime;
        string media = file.Properties?.MediaTypes switch
        {
            null => "",
            var t when t.Value.HasFlag(TagLib.MediaTypes.Video) => "video",
            var t when t.Value.HasFlag(TagLib.MediaTypes.Photo) => "image",
            var t when t.Value.HasFlag(TagLib.MediaTypes.Audio) => "audio",
            _ => "",
        };
        if (file is TagLib.Image.File && media.Length == 0) media = "image";
        return $"{kind} {media}".Trim();
    }

    private static void ReadImage(TagLib.Image.File file, List<MetadataField> fields, bool editable)
    {
        var t = file.ImageTag;
        fields.Add(new(F.Title, Clean(t?.Title), editable, editable));
        fields.Add(new(F.DescriptionText, Clean(t?.Comment), editable, editable));
        fields.Add(new(F.Keywords, MetadataValues.JoinList(t?.Keywords), editable, editable));
        fields.Add(new(F.Rating, t?.Rating is { } r and > 0 ? r.ToString(CultureInfo.InvariantCulture) : null, editable, editable));
        fields.Add(new(F.Authors, MetadataValues.JoinList(MetadataValues.SplitList(t?.Creator)), editable, editable));
        fields.Add(new(F.Copyright, Clean(t?.Copyright), editable, editable));
        fields.Add(new(F.Software, Clean(t?.Software), false));
        fields.Add(new(F.DateTaken, t?.DateTime is { } d ? MetadataValues.FormatDate(d) : null, editable, editable));
        int orientation = t == null ? 0 : (int)t.Orientation;
        fields.Add(new(F.Orientation, orientation is >= 1 and <= 8 ? orientation.ToString(CultureInfo.InvariantCulture) : null, editable, false));
        fields.Add(new(F.Make, Clean(t?.Make), false));
        fields.Add(new(F.Model, Clean(t?.Model), false));
        fields.Add(new(F.ExposureTime, t?.ExposureTime is { } e ? MetadataValues.FormatNumber(e) : null, false));
        fields.Add(new(F.FNumber, t?.FNumber is { } fn ? MetadataValues.FormatNumber(fn) : null, false));
        fields.Add(new(F.Iso, t?.ISOSpeedRatings is { } iso ? iso.ToString(CultureInfo.InvariantCulture) : null, false));
        fields.Add(new(F.FocalLength, t?.FocalLength is { } fl ? MetadataValues.FormatNumber(fl) : null, false));
        fields.Add(new(F.Latitude, t?.Latitude is { } lat ? MetadataValues.FormatNumber(lat) : null, editable, editable));
        fields.Add(new(F.Longitude, t?.Longitude is { } lon ? MetadataValues.FormatNumber(lon) : null, editable, editable));
        fields.Add(new(F.Altitude, t?.Altitude is { } alt ? MetadataValues.FormatNumber(alt) : null, editable, editable));
    }

    private static void ReadMedia(TagLib.File file, List<MetadataField> fields, bool editable)
    {
        var t = file.Tag;
        fields.Add(new(F.Title, Clean(t.Title), editable, editable));
        fields.Add(new(F.Subtitle, Clean(t.Subtitle), editable, editable));
        fields.Add(new(F.DescriptionText, Clean(t.Description), editable, editable));
        fields.Add(new(F.Comment, Clean(t.Comment), editable, editable));
        fields.Add(new(F.Authors, MetadataValues.JoinList(t.Performers), editable, editable));
        fields.Add(new(F.AlbumArtists, MetadataValues.JoinList(t.AlbumArtists), editable, editable));
        fields.Add(new(F.Composers, MetadataValues.JoinList(t.Composers), editable, editable));
        fields.Add(new(F.Conductor, Clean(t.Conductor), editable, editable));
        fields.Add(new(F.Album, Clean(t.Album), editable, editable));
        fields.Add(new(F.Genres, MetadataValues.JoinList(t.Genres), editable, editable));
        fields.Add(new(F.Year, t.Year > 0 ? t.Year.ToString(CultureInfo.InvariantCulture) : null, editable, editable));
        fields.Add(new(F.Track, t.Track > 0 ? t.Track.ToString(CultureInfo.InvariantCulture) : null, editable, editable));
        fields.Add(new(F.TrackCount, t.TrackCount > 0 ? t.TrackCount.ToString(CultureInfo.InvariantCulture) : null, editable, editable));
        fields.Add(new(F.Disc, t.Disc > 0 ? t.Disc.ToString(CultureInfo.InvariantCulture) : null, editable, editable));
        fields.Add(new(F.DiscCount, t.DiscCount > 0 ? t.DiscCount.ToString(CultureInfo.InvariantCulture) : null, editable, editable));
        fields.Add(new(F.Bpm, t.BeatsPerMinute > 0 ? t.BeatsPerMinute.ToString(CultureInfo.InvariantCulture) : null, editable, editable));
        fields.Add(new(F.Lyrics, Clean(t.Lyrics), editable, editable));
        fields.Add(new(F.Copyright, Clean(t.Copyright), editable, editable));
        fields.Add(new(F.Publisher, Clean(t.Publisher), editable, editable));
        var pictures = t.Pictures ?? [];
        fields.Add(new(F.Pictures, pictures.Length == 0 ? null : $"{pictures.Length} ({string.Join(", ", pictures.Select(p => $"{p.Type}, {p.MimeType}, {ReportWriter.FormatSize(p.Data?.Count ?? 0)}"))})",
            false, editable && pictures.Length > 0));
    }

    private static void ReadProperties(TagLib.File file, List<MetadataField> fields)
    {
        var p = file.Properties;
        if (p == null) return;
        int width = p.PhotoWidth > 0 ? p.PhotoWidth : p.VideoWidth, height = p.PhotoHeight > 0 ? p.PhotoHeight : p.VideoHeight;
        if (width > 0) fields.Add(new(F.Width, width.ToString(CultureInfo.InvariantCulture), false));
        if (height > 0) fields.Add(new(F.Height, height.ToString(CultureInfo.InvariantCulture), false));
        if (p.Duration > TimeSpan.Zero) fields.Add(new(F.Duration, p.Duration.ToString(p.Duration.TotalHours >= 1 ? @"h\:mm\:ss\.fff" : @"m\:ss\.fff", CultureInfo.InvariantCulture), false));
        string codecs = string.Join("; ", p.Codecs.Where(c => c != null).Select(c => c.Description).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct());
        if (codecs.Length > 0) fields.Add(new(F.Codecs, codecs, false));
        if (p.AudioBitrate > 0) fields.Add(new(F.AudioBitrate, p.AudioBitrate.ToString(CultureInfo.InvariantCulture), false));
        if (p.AudioSampleRate > 0) fields.Add(new(F.SampleRate, p.AudioSampleRate.ToString(CultureInfo.InvariantCulture), false));
        if (p.AudioChannels > 0) fields.Add(new(F.Channels, p.AudioChannels.ToString(CultureInfo.InvariantCulture), false));
        if (p.BitsPerSample > 0) fields.Add(new(F.BitsPerSample, p.BitsPerSample.ToString(CultureInfo.InvariantCulture), false));
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public IReadOnlyList<string> WriteWarnings(string path, IReadOnlyList<MetadataEdit> edits)
    {
        var w = new List<string> { L.T("TagLibMetadataProvider.OnlyTheMetadataBlocksAreRewritten") };
        if (edits.Any(e => e.Key == "pictures")) w.Add(L.T("TagLibMetadataProvider.RemovingEmbeddedPicturesDeletesTheCover"));
        if (edits.Any(e => e.Key == "orientation")) w.Add(L.T("TagLibMetadataProvider.ChangingTheOrientationChangesHowViewers"));
        return w;
    }

    public void Write(string path, IReadOnlyList<MetadataEdit> edits, CancellationToken ct)
    {
        if (!Writable.Contains(Path.GetExtension(path))) throw new NotSupportedException(ReadOnlyReason(Path.GetExtension(path)));
        using var file = Open(path);
        if (file is TagLib.Image.File image)
        {
            var onDisk = image.TagTypesOnDisk;
            image.EnsureAvailableTags();
            var t = image.ImageTag ?? throw new InvalidDataException("No metadata block could be created in this image.");
            foreach (var e in edits)
            {
                ct.ThrowIfCancellationRequested();
                switch (e.Key)
                {
                    case "title": t.Title = e.Value; break;
                    case "description": t.Comment = e.Value; break;
                    case "keywords": t.Keywords = [.. MetadataValues.SplitList(e.Value)]; break;
                    case "rating": t.Rating = e.Value == null ? null : uint.Parse(e.Value, CultureInfo.InvariantCulture); break;
                    case "authors": t.Creator = e.Value == null ? null : string.Join("; ", MetadataValues.SplitList(e.Value)); break;
                    case "copyright": t.Copyright = e.Value; break;
                    case "dateTaken": t.DateTime = e.Value == null ? null : Date(e.Value); break;
                    case "orientation": t.Orientation = e.Value == null ? TagLib.Image.ImageOrientation.None : (TagLib.Image.ImageOrientation)int.Parse(e.Value, CultureInfo.InvariantCulture); break;
                    case "latitude": t.Latitude = e.Value == null ? null : Number(e.Value); break;
                    case "longitude": t.Longitude = e.Value == null ? null : Number(e.Value); break;
                    case "altitude": t.Altitude = e.Value == null ? null : Number(e.Value); break;
                    default: throw new NotSupportedException($"\"{e.Key}\" cannot be written to this image.");
                }
            }
            // An EXIF block created here but left empty is written by TagLib# in a form it then reads as
            // damaged: leave it out (the values are in XMP, which every viewer reads).
            if (!onDisk.HasFlag(TagTypes.TiffIFD) && image.GetTag(TagTypes.TiffIFD, false) is TagLib.IFD.IFDTag ifd && ifd.Structure.Directories.All(d => d.Count == 0))
                image.RemoveTags(TagTypes.TiffIFD);
        }
        else
        {
            EnsureTag(file);
            var t = file.Tag;
            foreach (var e in edits)
            {
                ct.ThrowIfCancellationRequested();
                switch (e.Key)
                {
                    case "title": t.Title = e.Value; break;
                    case "subtitle": t.Subtitle = e.Value; break;
                    case "description": t.Description = e.Value; break;
                    case "comment": t.Comment = e.Value; break;
                    case "authors": t.Performers = [.. MetadataValues.SplitList(e.Value)]; break;
                    case "albumArtists": t.AlbumArtists = [.. MetadataValues.SplitList(e.Value)]; break;
                    case "composers": t.Composers = [.. MetadataValues.SplitList(e.Value)]; break;
                    case "conductor": t.Conductor = e.Value; break;
                    case "album": t.Album = e.Value; break;
                    case "genres": t.Genres = [.. MetadataValues.SplitList(e.Value)]; break;
                    case "year": t.Year = Unsigned(e.Value); break;
                    case "track": t.Track = Unsigned(e.Value); break;
                    case "trackCount": t.TrackCount = Unsigned(e.Value); break;
                    case "disc": t.Disc = Unsigned(e.Value); break;
                    case "discCount": t.DiscCount = Unsigned(e.Value); break;
                    case "bpm": t.BeatsPerMinute = Unsigned(e.Value); break;
                    case "lyrics": t.Lyrics = e.Value; break;
                    case "copyright": t.Copyright = e.Value; break;
                    case "publisher": t.Publisher = e.Value; break;
                    case "pictures":
                        if (e.Value != null) throw new NotSupportedException("Pictures can only be removed here.");
                        t.Pictures = [];
                        break;
                    default: throw new NotSupportedException($"\"{e.Key}\" cannot be written to this file.");
                }
            }
        }
        file.Save();
    }

    /// <summary>
    /// Makes sure the file has the tag block its format normally uses (a file without any tag would
    /// otherwise ignore every change): ID3v2 for MP3 and AIFF, Vorbis comments for FLAC and Ogg, MP4
    /// atoms, Matroska tags, ASF, APE, and the RIFF INFO list for WAV and AVI.
    /// </summary>
    private static void EnsureTag(TagLib.File file)
    {
        var type = file switch
        {
            TagLib.Mpeg.AudioFile or TagLib.Aiff.File => TagTypes.Id3v2,
            TagLib.Flac.File or TagLib.Ogg.File => TagTypes.Xiph,
            TagLib.Mpeg4.File => TagTypes.Apple,
            TagLib.Matroska.File => TagTypes.Matroska,
            TagLib.Asf.File => TagTypes.Asf,
            TagLib.Ape.File or TagLib.WavPack.File => TagTypes.Ape,
            TagLib.Riff.File => TagTypes.RiffInfo,
            _ => TagTypes.None,
        };
        if (type != TagTypes.None) file.GetTag(type, true);
    }

    private static uint Unsigned(string? value) => value == null ? 0 : uint.Parse(value, CultureInfo.InvariantCulture);

    private static double Number(string value) => MetadataValues.TryParseNumber(value, out double d) ? d : throw new FormatException($"\"{value}\" is not a number.");

    private static DateTime Date(string value) => MetadataValues.TryParseDate(value, out var d) ? d : throw new FormatException($"\"{value}\" is not a date.");
}
