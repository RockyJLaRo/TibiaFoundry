using TibiaFoundry.Core.FileOps;

namespace TibiaFoundry.Core.FileMetadata;

/// <summary>What a provider read from a file's content (the file system part is read separately).</summary>
public sealed record MetadataReadResult(string Format, IReadOnlyList<MetadataField> Fields, IReadOnlyList<string> Warnings)
{
    public MetadataReadResult(string format, IReadOnlyList<MetadataField> fields) : this(format, fields, []) { }
}

/// <summary>
/// The metadata of one family of formats (8.6). A provider says which files it handles and what it
/// can do with them, reads their embedded metadata, and — when it declares
/// <see cref="MetadataCapabilities.Edit"/> — writes changes to a file. Writing always happens on a
/// temporary copy (see <see cref="MetadataEngine"/>), never on the original.
/// </summary>
public interface IMetadataProvider
{
    string Id { get; }
    /// <summary>Shown in the capability table: "Images, audio and video (TagLib#)".</summary>
    string Name { get; }
    /// <summary>The extensions it handles (for documentation and the file picker).</summary>
    IReadOnlyList<string> Extensions { get; }
    /// <summary>What it can do with a file of this kind (it may be less for a particular file: <see cref="CapabilitiesFor"/>).</summary>
    MetadataCapabilities Capabilities { get; }
    /// <summary>What it does not do, in plain words.</summary>
    string Limitations { get; }

    /// <summary>Whether it handles this file (by content when it can; <paramref name="identification"/> is the signature check).</summary>
    bool CanHandle(string path, FormatIdentification identification);

    /// <summary>The capabilities for this particular file (a read-only variant, an encrypted document…).</summary>
    MetadataCapabilities CapabilitiesFor(string path) => Capabilities;

    MetadataReadResult Read(string path, CancellationToken ct);

    /// <summary>Writes changes to <paramref name="path"/> (a temporary copy). Null values remove the field. Only called with fields the read marked editable.</summary>
    void Write(string path, IReadOnlyList<MetadataEdit> edits, CancellationToken ct) =>
        throw new NotSupportedException($"{Name} cannot write metadata.");

    /// <summary>What writing these fields can do beyond changing them (re-encoding, signatures, moved tags), for the confirmation.</summary>
    IReadOnlyList<string> WriteWarnings(string path, IReadOnlyList<MetadataEdit> edits) => [];
}
