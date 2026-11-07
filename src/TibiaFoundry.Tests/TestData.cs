using TibiaFoundry.Core.Assets;

namespace TibiaFoundry.Tests;

/// <summary>
/// Locates real data used by integration tests:
/// <list type="bullet">
/// <item>reference outputs produced by the original program (1.8.0.26867)
/// (<c>Reconstruction\04_reference</c>), overridable with TF_REFERENCE_DIR;</item>
/// <item>the client data those outputs were produced from: the pinned copy named in
/// <c>04_reference\reference-client.json</c>, so a Tibia update does not invalidate the parity
/// tests. Without it, the installed client (<c>%LOCALAPPDATA%\Tibia\packages\Tibia</c>) is used;
/// TF_CLIENT_DIR selects any other client package (e.g. to try a new client version).</item>
/// </list>
/// Tests that need data which is not present, or whose reference outputs belong to other client
/// data, are reported as skipped with the reason, never as passed.
/// </summary>
internal static class TestData
{
    /// <summary>Test switch <c>TF_&lt;name&gt;</c>.</summary>
    public static string? Setting(string name) =>
        Environment.GetEnvironmentVariable("TF_" + name) is { Length: > 0 } value ? value : null;

    /// <summary>The author name stored in the reference GIFs of the original program.</summary>
    public const string ReferenceAuthor = "ParityHarness";

    // Initialization order matters: the client depends on the reference folder.
    public static string? ReferenceDir { get; } = FindReferenceDir();

    public static ReferenceClientInfo? ReferenceClient { get; } = ReadReferenceClient();

    public static TibiaClientPaths Client { get; } = FindClient();

    /// <summary>The client the reference outputs came from (<c>04_reference\reference-client.json</c>).</summary>
    public sealed record ReferenceClientInfo(string Folder, string ClientVersion, string CatalogSha256);

    private static ReferenceClientInfo? ReadReferenceClient()
    {
        string? file = ReferenceDir == null ? null : Path.Combine(ReferenceDir, "reference-client.json");
        if (file == null || !File.Exists(file)) return null;
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
        var root = json.RootElement;
        return new ReferenceClientInfo(root.GetProperty("folder").GetString()!, root.GetProperty("clientVersion").GetString()!, root.GetProperty("catalogSha256").GetString()!);
    }

    private static TibiaClientPaths FindClient()
    {
        string? env = Setting("CLIENT_DIR");
        if (!string.IsNullOrEmpty(env)) return new TibiaClientPaths(env);
        if (ReferenceDir != null && ReferenceClient != null && Directory.Exists(Path.Combine(ReferenceDir, ReferenceClient.Folder)))
            return new TibiaClientPaths(Path.Combine(ReferenceDir, ReferenceClient.Folder));
        return TibiaClientPaths.ForPackage(TibiaClientPaths.LivePackageName);
    }

    /// <summary>
    /// Null when <see cref="Client"/> holds the data the reference outputs were produced from;
    /// otherwise the reason a parity comparison would be meaningless.
    /// </summary>
    public static string? ReferenceMismatch { get; } = FindReferenceMismatch();

    private static string? FindReferenceMismatch()
    {
        if (ReferenceClient == null) return null; // older reference folders carry no manifest: compare as before
        string catalog = Path.Combine(Client.AssetsDirectory, "catalog-content.json");
        if (!File.Exists(catalog)) return null;   // RequireClientAssets reports the missing client
        string actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(catalog)));
        return actual.Equals(ReferenceClient.CatalogSha256, StringComparison.OrdinalIgnoreCase) ? null :
            $"The reference outputs were produced from client {ReferenceClient.ClientVersion}, but the client data in use ({Client.PackageDirectory}, " +
            $"version {Client.ReadClientVersion() ?? "unknown"}) differ. Use the pinned copy ({ReferenceClient.Folder}) or regenerate the references " +
            "(docs/11_Maintenance_Guide.md §4).";
    }

    private static string? FindReferenceDir()
    {
        string? env = Setting("REFERENCE_DIR");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "04_reference");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    public static string RequireClientAssets()
    {
        Assert.SkipUnless(Client.AssetsExist, "The Tibia client is not installed on this machine.");
        return Client.AssetsDirectory;
    }

    /// <summary>Path inside the reference folder; skips the test when it is missing.</summary>
    public static string RequireReference(params string[] parts)
    {
        Assert.SkipWhen(ReferenceDir == null, "Reference outputs of the original application are not available.");
        Assert.SkipWhen(ReferenceMismatch != null, ReferenceMismatch ?? "");
        string path = Path.Combine([ReferenceDir!, .. parts]);
        Assert.SkipUnless(File.Exists(path) || Directory.Exists(path), $"Reference item not found: {path}");
        return path;
    }

    public static string NewTempDirectory(string name = "tmp")
    {
        string dir = Path.Combine(Path.GetTempPath(), "TibiaFoundryTests", name + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Program folders inside a test folder (portable layout), so backups, temporary files, logs and
    /// script history of view-model tests never go to the user's real data folders.
    /// </summary>
    public static TibiaFoundry.App.Services.AppPaths PathsIn(string dir) =>
        new(Path.Combine(dir, "settings.json"), Path.Combine(dir, "logs"), IsPortable: true, Notice: null, PreviousSettingsFiles: []);
}
