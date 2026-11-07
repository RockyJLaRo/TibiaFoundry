using System.Reflection;

namespace TibiaFoundry.Core.Common;

/// <summary>
/// The application's identity in one place. User-facing text uses <see cref="Name"/>
/// ("Tibia Foundry"); attribution, file and folder names use <see cref="Id"/> ("TibiaFoundry").
/// Names of the predecessor that must stay readable for migration are in <see cref="Legacy"/>.
/// </summary>
public static class AppIdentity
{
    /// <summary>Name shown to users.</summary>
    public const string Name = "Tibia Foundry";

    /// <summary>Identifier used in the attribution text, the User-Agent, file and folder names.</summary>
    public const string Id = "TibiaFoundry";

    /// <summary>Creator and developer of Tibia Foundry.</summary>
    public const string Creator = "RockyLaRo";

    public const string CreatorRole = "TibiaWiki Admin";

    /// <summary>Canonical website the application works for (TibiaWiki).</summary>
    public const string Website = "https://tibia.fandom.com/";

    /// <summary>Version shown to users, e.g. "9.0" or "9.0.1" (src/Directory.Build.props is the single source).</summary>
    public static string Version { get; } =
        typeof(AppIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+', ' ')[0]
        ?? (typeof(AppIdentity).Assembly.GetName().Version is { } v ? v.ToString(v.Build > 0 ? 3 : 2) : "0.0");

    /// <summary>"Tibia Foundry 9.0".</summary>
    public static string NameAndVersion => $"{Name} {Version}";

    /// <summary>
    /// File and folder identifiers that version 3.0 used before the program was named Tibia Foundry.
    /// They are only read, never written, so existing settings, portable data, sprite folders and
    /// exported files keep working, and they appear nowhere in the interface. Older settings files
    /// can still be imported by hand (Settings → Advanced).
    /// </summary>
    public static class Legacy
    {
        /// <summary>Identifier of version 3.0 (its folders, data folder, marker and manifest names).</summary>
        public const string Version3Id = "TibiaForge";

        /// <summary>Settings and log folders under %APPDATA% / %LOCALAPPDATA%.</summary>
        public static IReadOnlyList<string> AppFolders { get; } = [Version3Id];
        /// <summary>Portable data folders next to the program.</summary>
        public static IReadOnlyList<string> PortableDataFolders { get; } = [Version3Id + "-Data"];
        /// <summary>Marker files that switch the program to portable mode.</summary>
        public static IReadOnlyList<string> PortableMarkerFiles { get; } = [Version3Id + ".portable"];
        /// <summary>Provenance files written into extracted sprite folders.</summary>
        public static IReadOnlyList<string> SpriteFolderManifests { get; } = [Version3Id + "-sprites.json"];

        /// <summary>SHA-256 of the default "Author" value of 1.x settings files (lowercase, UTF-8).</summary>
        private const string DefaultGifAuthorSha256 = "d7ce2de9b96514f7c7a7c9404c8333d634fcd511d4be12f6e19588d00ed3371a";

        /// <summary>
        /// True for the default "Author" value of 1.x settings files. It counts as "not set" when such a
        /// file is imported, so exports are never attributed to it by accident.
        /// </summary>
        public static bool IsDefaultGifAuthor(string? author) =>
            !string.IsNullOrWhiteSpace(author) &&
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(author.Trim().ToLowerInvariant())))
                .Equals(DefaultGifAuthorSha256, StringComparison.OrdinalIgnoreCase);
    }
}
