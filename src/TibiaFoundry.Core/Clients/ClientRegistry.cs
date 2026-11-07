using System.Text.Json;
using System.Text.Json.Serialization;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Clients;

/// <summary>What a registration's source looked like at its last scan (for the quick "may have changed" check).</summary>
public sealed record SourceSnapshot(string? Version, string? AssetsJsonSha256, int Files, long Bytes, DateTime NewestWriteUtc);

/// <summary>
/// A client folder Tibia Foundry remembers. "automatic" registrations follow the installed clients
/// (the launcher's folders, found the same way as before); "folder" registrations are folders you
/// chose. Registering never copies, moves or changes anything.
/// </summary>
public sealed class ClientRegistration
{
    public const string Automatic = "automatic";
    public const string Folder = "folder";
    public const string InstalledLive = "installed-live";
    public const string InstalledTest = "installed-test";

    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Server { get; init; }
    public required string Label { get; set; }

    /// <summary>Absolute path of a chosen folder (null for automatic registrations).</summary>
    public string? SourcePath { get; set; }

    /// <summary>Portable edition: the folder relative to the program folder when both are on the same drive (survives another drive letter).</summary>
    public string? RelativeToProgram { get; set; }

    public DateTime? LastScanUtc { get; set; }
    public SourceSnapshot? LastSource { get; set; }

    /// <summary>Keys of the versions imported through this registration, oldest first.</summary>
    public List<string> Versions { get; set; } = [];

    /// <summary>Client version for which the "not stored yet" reminder was dismissed (7.3).</summary>
    public string? ReminderDismissedFor { get; set; }

    /// <summary>8.0: content fingerprints for which "archive this change?" was answered "Don't ask for this version".</summary>
    public List<string> DismissedFingerprints { get; set; } = [];

    /// <summary>8.0: "installed-package", "standalone-folder" or "reconstructed" (null in 7.3 registries: derived from <see cref="Kind"/>).</summary>
    public string? SourceType { get; set; }

    /// <summary>8.0: for a rebuilt folder, the key of the revision it was rebuilt from.</summary>
    public string? ReconstructedFrom { get; set; }

    [JsonIgnore] public string EffectiveSourceType => SourceType ?? (IsAutomatic ? SourceTypes.InstalledPackage : SourceTypes.StandaloneFolder);

    [JsonIgnore] public bool IsAutomatic => Kind == Automatic;

    /// <summary>The ID of the installed (automatic) registration of a server.</summary>
    public static string InstalledFor(ClientScope scope) => scope == ClientScope.Live ? InstalledLive : InstalledTest;

    /// <summary>
    /// The source folder to use: for a chosen folder, the program-relative path when it exists (the
    /// portable program moved together with it), else the absolute path. Automatic registrations
    /// return null (their folder comes from the location rules).
    /// </summary>
    public string? ResolveSource(string? programDirectory)
    {
        if (IsAutomatic) return null;
        if (RelativeToProgram != null && programDirectory != null)
        {
            try
            {
                string relative = Path.GetFullPath(Path.Combine(programDirectory, RelativeToProgram));
                if (Directory.Exists(relative)) return relative;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Fall back to the absolute path.
            }
        }
        return SourcePath;
    }

    /// <summary>Remembers a chosen folder; <paramref name="programDirectory"/> (portable edition) adds a relative form on the same drive.</summary>
    public void SetSource(string folder, string? programDirectory)
    {
        SourcePath = Path.GetFullPath(folder);
        RelativeToProgram = null;
        if (programDirectory != null &&
            string.Equals(Path.GetPathRoot(Path.GetFullPath(programDirectory)), Path.GetPathRoot(SourcePath), StringComparison.OrdinalIgnoreCase))
            RelativeToProgram = Path.GetRelativePath(Path.GetFullPath(programDirectory), SourcePath);
    }
}

/// <summary>Status of an imported version.</summary>
/// <summary>Kinds of client folders (8.0). Stored in the registry and manifests: never rename one.</summary>
public static class SourceTypes
{
    public const string InstalledPackage = "installed-package";
    public const string StandaloneFolder = "standalone-folder";
    public const string Reconstructed = "reconstructed";

    public static string Display(string type) => type switch
    {
        InstalledPackage => L.T("Common.InstalledClient"),
        Reconstructed => L.T("ClientRegistry.RebuiltInstallation"),
        _ => L.T("ClientRegistry.StandaloneFolder"),
    };
}

public static class VersionStatus
{
    /// <summary>The manifest is valid and every object exists.</summary>
    public const string Imported = "Imported";
    /// <summary>The manifest is damaged or objects are missing or damaged.</summary>
    public const string Damaged = "Damaged";
    /// <summary>The manifest file is gone.</summary>
    public const string Missing = "Missing";
}

/// <summary>An imported version (a snapshot): its manifest holds the files, this entry the label and figures.</summary>
public sealed class ClientVersionEntry
{
    /// <summary>"Live_Server/Client_15.33.9d148f": the version's folder in the store. Never changes.</summary>
    public required string Key { get; init; }
    public required string Fingerprint { get; init; }
    public required string Server { get; init; }
    public required string Label { get; set; }
    public string? ClientVersion { get; init; }
    public string? DisplayVersion { get; init; }
    public string? Build { get; init; }
    public string? RegistrationId { get; set; }
    public string Status { get; set; } = VersionStatus.Imported;
    public string? Problem { get; set; }
    public DateTime ImportedAtUtc { get; init; }
    public int Files { get; init; }
    public long Bytes { get; init; }
    /// <summary>Bytes this import added to the store (the rest was already stored).</summary>
    public long NewBytes { get; init; }

    /// <summary>8.0: the manifest's scope (refreshed from the manifest whenever the store is opened).</summary>
    public string? Scope { get; set; }
    /// <summary>8.0: the manifest's storage (refreshed from the manifest).</summary>
    public string? Storage { get; set; }
    /// <summary>8.0: nothing excluded and no problem (refreshed from the manifest).</summary>
    public bool Complete { get; set; }
    /// <summary>8.0: files left out on purpose (personal data), from the manifest's exclusions.</summary>
    public int ExcludedFiles { get; set; }
    /// <summary>8.0: the version evidence's status ("Verified", "Conflicting", "Unknown"; null for 7.3 revisions).</summary>
    public string? VersionEvidenceStatus { get; set; }

    [JsonIgnore] public string EffectiveScope => Scope ?? ManifestScopes.TibiaFoundryFiles;
    [JsonIgnore] public string EffectiveStorage => Storage ?? ManifestStorage.Shared;
    [JsonIgnore] public bool IsCompleteInstallation => EffectiveScope == ManifestScopes.CompleteInstallation && Complete;

    public string ManifestPath(string root) => Path.Combine(root, Key.Replace('/', Path.DirectorySeparatorChar), ClientManifest.FileName);

    /// <summary>Copies what the manifest says about scope, storage and completeness (the manifest is the truth).</summary>
    internal ClientVersionEntry Describe(ClientManifest m)
    {
        Scope = m.EffectiveScope;
        Storage = m.EffectiveStorage;
        Complete = m.IsContentComplete;
        ExcludedFiles = m.Exclusions?.Sum(e => e.Files) ?? 0;
        VersionEvidenceStatus = m.VersionStatus;
        return this;
    }
    [JsonIgnore] public bool IsUsable => Status == VersionStatus.Imported;
}

/// <summary>
/// <c>Clients\client-registry.json</c>: registrations and the index of imported versions. The
/// manifests are the truth about versions; the registry adds labels and figures and is rebuilt from
/// the manifests when it is missing or damaged. Every path in it is relative to the store, except
/// the folders you registered.
/// </summary>
public sealed class ClientRegistry
{
    public const string FileName = "client-registry.json";
    public const int CurrentFormat = 1;

    public int Format { get; set; } = CurrentFormat;
    public List<ClientRegistration> Registrations { get; set; } = [];
    public List<ClientVersionEntry> Versions { get; set; } = [];

    /// <summary>Settings this build does not know (written by a newer version): kept unchanged.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    public ClientRegistration? Registration(string id) => Registrations.FirstOrDefault(r => r.Id == id);
    public ClientVersionEntry? Version(string key) => Versions.FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<ClientVersionEntry> VersionsOf(ClientRegistration registration) =>
        registration.Versions.Select(Version).OfType<ClientVersionEntry>();

    public ClientVersionEntry? NewestVersionOf(ClientRegistration registration) => VersionsOf(registration).MaxBy(v => v.ImportedAtUtc);

    public static ClientRegistration DefaultAutomatic(ClientScope scope) => new()
    {
        Id = scope == ClientScope.Live ? ClientRegistration.InstalledLive : ClientRegistration.InstalledTest,
        Kind = ClientRegistration.Automatic,
        Server = ClientServers.Of(scope),
        Label = DefaultClients.LabelOf(scope),
    };

    internal byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, ClientManifest.Json);

    /// <summary>
    /// Reads the registry and checks it against the manifests in the store: versions whose manifest is
    /// gone or damaged are marked, manifests the registry does not know are added back. A damaged
    /// registry is kept aside (renamed by the caller) and rebuilt; <paramref name="notes"/> says what happened.
    /// </summary>
    internal static ClientRegistry Load(string root, List<string> notes, out bool damaged)
    {
        damaged = false;
        string file = Path.Combine(root, FileName);
        ClientRegistry registry = new();
        if (File.Exists(file))
        {
            try
            {
                registry = JsonSerializer.Deserialize<ClientRegistry>(File.ReadAllBytes(file), ClientManifest.Json) ?? new ClientRegistry();
                if (registry.Format > CurrentFormat) notes.Add(L.T("ClientRegistry.TheClientRegistryWasWrittenBy"));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                damaged = true;
                notes.Add(L.F("ClientRegistry.TheClientRegistryCouldNotBe", ex.Message));
                registry = new ClientRegistry();
            }
        }
        registry.Registrations = registry.Registrations.Where(r => r != null && ClientServers.IsKnown(r.Server) && !string.IsNullOrWhiteSpace(r.Id)).ToList();
        registry.Versions = registry.Versions.Where(v => v != null && ClientServers.IsKnown(v.Server) && !string.IsNullOrWhiteSpace(v.Key)).ToList();
        foreach (var scope in new[] { ClientScope.Live, ClientScope.TestServer })
        {
            var auto = DefaultAutomatic(scope);
            if (registry.Registration(auto.Id) == null) registry.Registrations.Insert(scope == ClientScope.Live ? 0 : Math.Min(1, registry.Registrations.Count), auto);
        }
        registry.Reconcile(root, notes);
        return registry;
    }

    /// <summary>Brings the version index in line with the manifests on disk.</summary>
    private void Reconcile(string root, List<string> notes)
    {
        var found = new Dictionary<string, ClientManifest>(StringComparer.OrdinalIgnoreCase);
        var broken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string server in new[] { ClientServers.Live, ClientServers.Test })
        {
            string dir = Path.Combine(root, server);
            if (!Directory.Exists(dir)) continue;
            foreach (string versionDir in Directory.EnumerateDirectories(dir))
            {
                string key = server + "/" + Path.GetFileName(versionDir);
                string manifest = Path.Combine(versionDir, ClientManifest.FileName);
                if (!File.Exists(manifest)) continue;
                try
                {
                    var m = ClientManifest.Read(manifest);
                    if (m.Server != server) throw new InvalidDataException($"it belongs to {m.Server}, but it is in the {server} folder.");
                    found[key] = m;
                }
                catch (InvalidDataException ex)
                {
                    broken[key] = ex.Message;
                }
            }
        }
        foreach (var entry in Versions)
        {
            if (found.TryGetValue(entry.Key, out var manifest))
            {
                if (entry.Status == VersionStatus.Missing) { entry.Status = VersionStatus.Imported; entry.Problem = null; }
                entry.Describe(manifest);
            }
            else if (broken.TryGetValue(entry.Key, out string? why)) { entry.Status = VersionStatus.Damaged; entry.Problem = why; }
            else { entry.Status = VersionStatus.Missing; entry.Problem = L.T("ClientRegistry.ItsManifestIsNoLongerIn"); }
        }
        foreach (var (key, m) in found.Where(kv => Version(kv.Key) == null))
        {
            Versions.Add(EntryFor(key, m, null, 0, label: null));
            notes.Add(L.F("ClientRegistry.TheStoredVersion0WasAdded", key));
        }
        foreach (var (key, why) in broken.Where(kv => Version(kv.Key) == null))
            Versions.Add(new ClientVersionEntry { Key = key, Fingerprint = "", Server = key.Split('/')[0], Label = key.Split('/')[^1], Status = VersionStatus.Damaged, Problem = why });
        foreach (var registration in Registrations)
            registration.Versions = registration.Versions.Where(k => Version(k) != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static ClientVersionEntry EntryFor(string key, ClientManifest m, string? registrationId, long newBytes, string? label) => new ClientVersionEntry()
    {
        Key = key,
        Fingerprint = m.Fingerprint,
        Server = m.Server,
        Label = string.IsNullOrWhiteSpace(label) ? (m.ClientVersion ?? key.Split('/')[^1]) : label.Trim(),
        ClientVersion = m.ClientVersion,
        DisplayVersion = m.DisplayVersion,
        Build = m.Build,
        RegistrationId = registrationId,
        ImportedAtUtc = m.ImportedAtUtc,
        Files = m.Files.Count,
        Bytes = m.TotalBytes,
        NewBytes = newBytes,
    }.Describe(m);
}
