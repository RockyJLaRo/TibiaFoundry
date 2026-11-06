using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Clients;

/// <summary>
/// A folder of client files as the readers see it: a file name gives a readable path. For an
/// installed or registered client it is a real folder; for an imported version the names come from
/// the version's manifest and lead to the shared objects in the store (which have no file names of
/// their own), so the readers never need to know how files are stored.
/// </summary>
public interface IClientFolder
{
    /// <summary>What to show for this folder (a real path, or "imported version … (assets)").</summary>
    string DisplayPath { get; }

    /// <summary>The real folder, or null when the files are not in one (an imported version).</summary>
    string? PhysicalPath { get; }

    /// <summary>A readable path for a file of this folder ("catalog-content.json", "sprites-….bmp.lzma"), or null when it has no such file.</summary>
    string? PathOf(string name);
}

/// <summary>A real folder (an installed or registered client, or any folder chosen by the user).</summary>
public sealed class PhysicalClientFolder(string path) : IClientFolder
{
    private HashSet<string>? _listing;

    public string DisplayPath { get; } = path;
    public string? PhysicalPath => DisplayPath;

    /// <summary>
    /// The folder with its file names read once (8.8.1), for work that asks about thousands of files
    /// in a moment, like the compatibility check: one directory listing instead of one file-system
    /// query per sprite sheet, map image or sound. Only for short-lived use: files added or removed
    /// after the listing are not seen. Names with a folder part or unusual characters are still checked
    /// on disk. If the folder cannot be listed, every name is checked on disk.
    /// </summary>
    public static PhysicalClientFolder WithListing(string path)
    {
        var folder = new PhysicalClientFolder(path);
        try
        {
            if (Directory.Exists(path))
                folder._listing = new HashSet<string>(Directory.EnumerateFiles(path).Select(Path.GetFileName).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            folder._listing = null;
        }
        return folder;
    }

    /// <summary>True when the file names were read once (<see cref="WithListing"/>).</summary>
    public bool IsListed => _listing != null;

    /// <summary>True when the folder has a file of this name.</summary>
    public bool Exists(string name) =>
        _listing != null && IsPlainName(name) ? _listing.Contains(name) : File.Exists(Path.Combine(DisplayPath, name));

    public string? PathOf(string name)
    {
        string full = Path.Combine(DisplayPath, name);
        return Exists(name) ? full : null;
    }

    // Names Windows would resolve differently from a listing (trailing dots or spaces, short 8.3 names,
    // streams, sub-folders) are left to the file system.
    private static bool IsPlainName(string name) =>
        name.Length > 0 && name.IndexOfAny(['\\', '/', ':', '~']) < 0 && name[^1] is not ('.' or ' ');

    /// <summary>Where the file would be (whether or not it exists): today's behavior of Path.Combine.</summary>
    public string Expected(string name) => Path.Combine(DisplayPath, name);
}

/// <summary>One folder ("assets", "sounds", "bin") of an imported version.</summary>
public sealed class ManifestClientFolder : IClientFolder
{
    private readonly ManifestClientFiles _files;
    private readonly string _prefix;

    internal ManifestClientFolder(ManifestClientFiles files, string folder)
    {
        _files = files;
        _prefix = folder.Length == 0 ? "" : folder.TrimEnd('/') + "/";
        DisplayPath = $"{files.Description} ({(folder.Length == 0 ? "package" : folder)})";
    }

    public string DisplayPath { get; }
    public string? PhysicalPath => null;
    public string? PathOf(string name) => _files.PathOf(_prefix + name.Replace('\\', '/'));
}

/// <summary>
/// The files of one imported version: each package path of its manifest maps to the shared object
/// with that content. Lookups are a dictionary access; the original installation is never touched.
/// </summary>
public sealed class ManifestClientFiles
{
    private readonly Dictionary<string, string> _paths;

    internal ManifestClientFiles(ClientVersionEntry entry, ClientManifest manifest, ContentStore store, string root)
    {
        Entry = entry;
        Manifest = manifest;
        VersionFolder = Path.GetDirectoryName(entry.ManifestPath(root))!;
        // Shared storage: the object with the file's content; independent storage (8.0): the revision's own copy.
        string copies = Path.Combine(VersionFolder, ClientStore.FilesFolder);
        _paths = manifest.EffectiveStorage == ManifestStorage.Independent
            ? manifest.Files.ToDictionary(f => f.Path, f => PathGuard.ResolveInside(copies, f.Path), StringComparer.OrdinalIgnoreCase)
            : manifest.Files.ToDictionary(f => f.Path, f => store.ObjectPath(f.Sha256), StringComparer.OrdinalIgnoreCase);
        Description = L.F("ClientFiles.Imported01", ClientServers.Display(entry.Server).ToLowerInvariant(), entry.ClientVersion ?? entry.Label);
    }

    public ClientVersionEntry Entry { get; }
    public ClientManifest Manifest { get; }

    /// <summary>The version's folder in the store (manifest.json, and for independent storage its <c>files</c> folder).</summary>
    public string VersionFolder { get; }

    public string Description { get; }

    /// <summary>The object path for a package path ("assets/catalog-content.json"), or null when the version has no such file.</summary>
    public string? PathOf(string packagePath) => _paths.GetValueOrDefault(packagePath.Replace('\\', '/'));

    public IClientFolder Folder(string folder) => new ManifestClientFolder(this, folder);

    public IReadOnlyList<ManifestFile> FilesOfKind(string kind) => Manifest.Files.Where(f => f.Kind == kind).ToList();

    /// <summary>The version's Qt resource files ("bin/graphics_resources.rcc" first).</summary>
    public IReadOnlyList<ManifestFile> ResourceFiles => FilesOfKind(ClientFileKinds.Rcc)
        .OrderBy(f => f.Path.EndsWith("/graphics_resources.rcc", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(f => f.Path, StringComparer.Ordinal).ToList();
}
