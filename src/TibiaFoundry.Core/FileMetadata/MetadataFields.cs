namespace TibiaFoundry.Core.FileMetadata;

/// <summary>
/// Every field the metadata tool knows (8.6). A key means the same thing in every format, so a batch
/// edit of "keywords" or "copyright" reaches every file that can store it; formats that cannot are
/// listed as skipped with the reason, never changed silently.
/// </summary>
public static class MetadataFields
{
    // Categories, in the order the inspector shows them.
    public const string File = "File system";
    public const string Description = "Description";
    public const string Rights = "Authorship and rights";
    public const string Music = "Music";
    public const string Image = "Image";
    public const string Camera = "Camera";
    public const string Location = "Location";
    public const string Document = "Document";
    public const string Media = "Media properties";
    public const string Archive = "Archive";
    public const string Program = "Program";
    public const string Tibia = "Tibia client data";
    public const string Technical = "Technical";

    public static IReadOnlyList<string> CategoryOrder { get; } = [Description, Rights, Music, Image, Camera, Location, Document, Media, Archive, Program, Tibia, Technical, File];

    private static MetadataFieldDefinition T(string key, string label, string category, string? help = null, bool sensitive = false) =>
        new(key, label, category, MetadataValueKind.Text) { Help = help, Sensitive = sensitive, MaxLength = 4096 };

    private static MetadataFieldDefinition L(string key, string label, string category, string? help = null, bool sensitive = false) =>
        new(key, label, category, MetadataValueKind.List) { Help = help ?? Localization.L.T("MetadataFields.SeparateValuesWithASemicolon"), Sensitive = sensitive, MaxLength = 8192 };

    private static MetadataFieldDefinition I(string key, string label, string category, double min, double max, string? help = null) =>
        new(key, label, category, MetadataValueKind.Integer) { Minimum = min, Maximum = max, Help = help };

    private static MetadataFieldDefinition D(string key, string label, string category, string? help = null) =>
        new(key, label, category, MetadataValueKind.Date) { Help = help ?? Localization.L.T("MetadataFields.YyyyMMDdOrYyyyMM") };

    private static MetadataFieldDefinition R(string key, string label, string category, string? help = null) => new(key, label, category, MetadataValueKind.Text) { Help = help };

    // ------------------------------------------------------------------ descriptive (shared by many formats)
    public static readonly MetadataFieldDefinition Title = T("title", Localization.L.T("Label.Title"), Description);
    public static readonly MetadataFieldDefinition Subtitle = T("subtitle", Localization.L.T("Label.Subtitle"), Description);
    public static readonly MetadataFieldDefinition Subject = T("subject", Localization.L.T("Label.Subject"), Description);
    public static readonly MetadataFieldDefinition DescriptionText = new("description", Localization.L.T("Common.Description"), Description, MetadataValueKind.MultilineText) { MaxLength = 65536 };
    public static readonly MetadataFieldDefinition Comment = new("comment", Localization.L.T("Label.Comment"), Description, MetadataValueKind.MultilineText) { MaxLength = 65536 };
    public static readonly MetadataFieldDefinition Keywords = L("keywords", "Keywords", Description);
    public static readonly MetadataFieldDefinition Category = T("category", Localization.L.T("Common.Category"), Description);
    public static readonly MetadataFieldDefinition Language = T("language", Localization.L.T("Main.Language"), Description, Localization.L.T("MetadataFields.ALanguageCodeSuchAsEn"));
    public static readonly MetadataFieldDefinition Rating = I("rating", "Rating", Description, 0, 5, Localization.L.T("MetadataFields.0NoneTo5Stars"));
    public static readonly MetadataFieldDefinition Date = D("date", Localization.L.T("Label.Date"), Description);

    // ------------------------------------------------------------------ authorship and rights
    public static readonly MetadataFieldDefinition Authors = L("authors", Localization.L.T("MetadataFields.AuthorsArtists"), Rights, Localization.L.T("MetadataFields.PeopleWhoMadeItSeparateNames"), sensitive: true);
    public static readonly MetadataFieldDefinition Copyright = T("copyright", Localization.L.T("AssetLibrary.Copyright"), Rights, sensitive: true);
    public static readonly MetadataFieldDefinition Publisher = T("publisher", Localization.L.T("Label.Publisher"), Rights);
    public static readonly MetadataFieldDefinition LastModifiedBy = T("lastModifiedBy", Localization.L.T("MetadataFields.LastModifiedBy"), Rights, sensitive: true);
    public static readonly MetadataFieldDefinition Software = T("software", Localization.L.T("MetadataFields.CreatedWith"), Rights, Localization.L.T("MetadataFields.TheProgramThatMadeOrLast"));

    // ------------------------------------------------------------------ music
    public static readonly MetadataFieldDefinition Album = T("album", Localization.L.T("Label.Album"), Music);
    public static readonly MetadataFieldDefinition AlbumArtists = L("albumArtists", Localization.L.T("MetadataFields.AlbumArtists"), Music);
    public static readonly MetadataFieldDefinition Composers = L("composers", "Composers", Music);
    public static readonly MetadataFieldDefinition Conductor = T("conductor", Localization.L.T("Label.Conductor"), Music);
    public static readonly MetadataFieldDefinition Genres = L("genres", "Genres", Music);
    public static readonly MetadataFieldDefinition Year = I("year", "Year", Music, 0, 9999);
    public static readonly MetadataFieldDefinition Track = I("track", "Track", Music, 0, 9999);
    public static readonly MetadataFieldDefinition TrackCount = I("trackCount", Localization.L.T("MetadataFields.TracksOnTheAlbum"), Music, 0, 9999);
    public static readonly MetadataFieldDefinition Disc = I("disc", "Disc", Music, 0, 999);
    public static readonly MetadataFieldDefinition DiscCount = I("discCount", "Discs", Music, 0, 999);
    public static readonly MetadataFieldDefinition Bpm = I("bpm", Localization.L.T("MetadataFields.BeatsPerMinute"), Music, 0, 999);
    public static readonly MetadataFieldDefinition Lyrics = new("lyrics", Localization.L.T("Label.Lyrics"), Music, MetadataValueKind.MultilineText) { MaxLength = 1 << 20 };
    public static readonly MetadataFieldDefinition Pictures = R("pictures", Localization.L.T("MetadataFields.EmbeddedPictures"), Music, Localization.L.T("MetadataFields.CoverArtAndOtherPicturesStored"));

    // ------------------------------------------------------------------ images and cameras
    public static readonly MetadataFieldDefinition Orientation = new("orientation", Localization.L.T("Label.Orientation"), Image, MetadataValueKind.Choice)
    {
        Choices = ["1", "2", "3", "4", "5", "6", "7", "8"],
        Help = Localization.L.T("MetadataFields.EXIFOrientation1Normal3Rotated"),
    };
    public static readonly MetadataFieldDefinition DateTaken = D("dateTaken", Localization.L.T("MetadataFields.DateTaken"), Camera);
    public static readonly MetadataFieldDefinition Make = T("make", Localization.L.T("MetadataFields.CameraMaker"), Camera);
    public static readonly MetadataFieldDefinition Model = T("model", Localization.L.T("MetadataFields.CameraModel"), Camera);
    public static readonly MetadataFieldDefinition ExposureTime = R("exposureTime", Localization.L.T("MetadataFields.ExposureTimeS"), Camera);
    public static readonly MetadataFieldDefinition FNumber = R("fNumber", Localization.L.T("Label.Fnumber"), Camera);
    public static readonly MetadataFieldDefinition Iso = R("iso", Localization.L.T("MetadataFields.ISOSpeed"), Camera);
    public static readonly MetadataFieldDefinition FocalLength = R("focalLength", Localization.L.T("MetadataFields.FocalLengthMm"), Camera);
    public static readonly MetadataFieldDefinition Latitude = new("latitude", Localization.L.T("Label.Latitude"), Location, MetadataValueKind.Decimal) { Minimum = -90, Maximum = 90, Sensitive = true, Help = Localization.L.T("MetadataFields.DecimalDegreesNorthPositiveGPSData") };
    public static readonly MetadataFieldDefinition Longitude = new("longitude", Localization.L.T("Label.Longitude"), Location, MetadataValueKind.Decimal) { Minimum = -180, Maximum = 180, Sensitive = true, Help = Localization.L.T("MetadataFields.DecimalDegreesEastPositive") };
    public static readonly MetadataFieldDefinition Altitude = new("altitude", Localization.L.T("MetadataFields.AltitudeM"), Location, MetadataValueKind.Decimal) { Minimum = -20000, Maximum = 100000, Sensitive = true };

    // ------------------------------------------------------------------ documents
    public static readonly MetadataFieldDefinition Created = D("created", Localization.L.T("Label.Created"), Document, Localization.L.T("MetadataFields.TheDateStoredInTheDocument"));
    public static readonly MetadataFieldDefinition Modified = D("modified", Localization.L.T("Choice.Sort.Modified"), Document, Localization.L.T("MetadataFields.TheDateStoredInTheDocument"));
    public static readonly MetadataFieldDefinition Revision = T("revision", Localization.L.T("Rebuilder.Revision"), Document);
    public static readonly MetadataFieldDefinition ContentStatus = T("contentStatus", Localization.L.T("Common.Status"), Document, Localization.L.T("MetadataFields.DraftFinal"));
    public static readonly MetadataFieldDefinition Identifier = T("identifier", Localization.L.T("Label.Identifier"), Document, Localization.L.T("MetadataFields.ISBNUUIDOrAnotherIdentifier"));
    public static readonly MetadataFieldDefinition Company = R("company", Localization.L.T("Label.Company"), Document);
    public static readonly MetadataFieldDefinition Pages = R("pages", Localization.L.T("Label.Pages"), Document);
    public static readonly MetadataFieldDefinition Words = R("words", Localization.L.T("Label.Words"), Document);

    // ------------------------------------------------------------------ read-only properties
    public static readonly MetadataFieldDefinition Width = R("width", Localization.L.T("MetadataFields.WidthPx"), Media);
    public static readonly MetadataFieldDefinition Height = R("height", Localization.L.T("MetadataFields.HeightPx"), Media);
    public static readonly MetadataFieldDefinition Duration = R("duration", Localization.L.T("ContentStudio.Duration"), Media);
    public static readonly MetadataFieldDefinition Codecs = R("codecs", Localization.L.T("Label.Codecs"), Media);
    public static readonly MetadataFieldDefinition AudioBitrate = R("audioBitrate", Localization.L.T("MetadataFields.AudioBitrateKbitS"), Media);
    public static readonly MetadataFieldDefinition SampleRate = R("sampleRate", Localization.L.T("MetadataFields.SampleRateHz"), Media);
    public static readonly MetadataFieldDefinition Channels = R("channels", Localization.L.T("Label.Channels"), Media);
    public static readonly MetadataFieldDefinition BitsPerSample = R("bitsPerSample", Localization.L.T("MetadataFields.BitsPerSample"), Media);
    public static readonly MetadataFieldDefinition TagTypes = R("tagTypes", Localization.L.T("MetadataFields.MetadataBlocksInTheFile"), Technical);
    public static readonly MetadataFieldDefinition Signature = R("signature", Localization.L.T("MetadataFields.DigitalSignature"), Program);
    public static readonly MetadataFieldDefinition ArchiveComment = new("archiveComment", Localization.L.T("MetadataFields.ArchiveComment"), Archive, MetadataValueKind.MultilineText) { MaxLength = 65535 };

    // ------------------------------------------------------------------ file system (outside the file's content)
    public static readonly MetadataFieldDefinition FsName = R("fs.name", Localization.L.T("Common.Name"), File);
    public static readonly MetadataFieldDefinition FsFolder = R("fs.folder", Localization.L.T("Common.Folder"), File);
    public static readonly MetadataFieldDefinition FsSize = R("fs.size", Localization.L.T("Common.Size"), File);
    public static readonly MetadataFieldDefinition FsCreated = D("fs.created", Localization.L.T("Label.Created"), File, Localization.L.T("MetadataFields.WhenTheFileWasCreatedOn"));
    public static readonly MetadataFieldDefinition FsModified = D("fs.modified", Localization.L.T("Choice.Sort.Modified"), File, Localization.L.T("MetadataFields.WhenTheFileSContentLast"));
    public static readonly MetadataFieldDefinition FsAccessed = D("fs.accessed", Localization.L.T("Label.Accessed"), File, Localization.L.T("MetadataFields.WhenTheFileWasLastOpened"));
    public static readonly MetadataFieldDefinition FsReadOnly = new("fs.readOnly", Localization.L.T("Label.Readonly"), File, MetadataValueKind.Boolean);
    public static readonly MetadataFieldDefinition FsHidden = new("fs.hidden", Localization.L.T("Label.Hidden"), File, MetadataValueKind.Boolean);
    public static readonly MetadataFieldDefinition FsArchive = new("fs.archive", Localization.L.T("MetadataFields.ReadyForArchivingA"), File, MetadataValueKind.Boolean) { Help = Localization.L.T("MetadataFields.TheWindowsArchiveAttributeUsedBy") };
    public static readonly MetadataFieldDefinition FsSystem = R("fs.system", Localization.L.T("MetadataFields.SystemFile"), File);

    /// <summary>A read-only field that only one format has ("Entries", "Machine"…), shown in <paramref name="category"/>.</summary>
    public static MetadataFieldDefinition Info(string key, string label, string category) => R(key, label, category);

    private static readonly Lazy<IReadOnlyDictionary<string, MetadataFieldDefinition>> Known = new(() =>
        typeof(MetadataFields).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(MetadataFieldDefinition))
            .Select(f => (MetadataFieldDefinition)f.GetValue(null)!)
            .ToDictionary(d => d.Key, StringComparer.Ordinal));

    /// <summary>Every shared field by key.</summary>
    public static IReadOnlyDictionary<string, MetadataFieldDefinition> All => Known.Value;

    /// <summary>The fields a batch edit or a preset can set (descriptive, rights and music; never technical or file system values).</summary>
    public static IReadOnlyList<MetadataFieldDefinition> BatchEditable { get; } =
    [
        Title, Subject, DescriptionText, Comment, Keywords, Category, Language, Rating, Date,
        Authors, Copyright, Publisher, LastModifiedBy,
        Album, AlbumArtists, Composers, Genres, Year, Track, TrackCount, Disc, DiscCount,
        DateTaken, Orientation, Latitude, Longitude, Altitude, Created, Modified, ContentStatus,
    ];
}
