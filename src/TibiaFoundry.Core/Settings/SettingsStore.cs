using System.Text.Json;
using System.Text.Json.Serialization;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Settings;

/// <summary>Outcome of loading settings, so the UI can tell the user what happened.</summary>
public enum SettingsLoadStatus { Loaded, CreatedDefaults, ImportedLegacy, RecoveredFromCorruption, LoadedFromNewerVersion, MigratedFromPreviousName }

/// <summary>
/// Reads and writes <see cref="AppSettings"/>. Writes are atomic (temp file + replace); a
/// corrupted file is kept as a backup and defaults are used instead of crashing.
/// </summary>
public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public string FilePath { get; } = filePath;

    /// <param name="legacyCandidates">settings.json files of the original 1.x program to import when no settings exist yet.</param>
    /// <param name="previousLocations">
    /// settings.json files of versions 3.0 and 2.x (same format, the folder names those versions used),
    /// newest first. When this program has no settings yet, the first one found is loaded and upgraded;
    /// the old file is only read, so the earlier version keeps working.
    /// </param>
    public (AppSettings Settings, SettingsLoadStatus Status, string? Detail) Load(IEnumerable<string>? legacyCandidates = null,
        IEnumerable<string>? previousLocations = null)
    {
        if (!File.Exists(FilePath))
        {
            foreach (var previous in previousLocations ?? Array.Empty<string>())
            {
                if (!File.Exists(previous)) continue;
                try
                {
                    var migrated = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(previous), Options) ?? new AppSettings();
                    migrated.Normalize();
                    if (migrated.Version <= AppSettings.CurrentVersion) migrated.Migrate();
                    AppLog.Info($"Settings migrated from {previous} (the old file is kept unchanged).");
                    return (migrated, SettingsLoadStatus.MigratedFromPreviousName, previous);
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    AppLog.Warn($"Settings of the previous version ({previous}) could not be read; they were not migrated.", ex);
                }
            }
        }
        if (File.Exists(FilePath))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
                settings.Normalize();
                if (settings.Version > AppSettings.CurrentVersion)
                {
                    // Written by a newer Tibia Foundry: keep a copy before this version ever saves over it.
                    string copy = FilePath + $".v{settings.Version}.bak";
                    try { if (!File.Exists(copy)) File.Copy(FilePath, copy); } catch (IOException) { copy = L.T("SettingsStore.BackupFailed"); }
                    AppLog.Warn($"Settings format {settings.Version} is newer than this version's {AppSettings.CurrentVersion}; unknown options are preserved. Backup: {copy}");
                    return (settings, SettingsLoadStatus.LoadedFromNewerVersion, copy);
                }
                if (settings.Version < AppSettings.CurrentVersion)
                {
                    // Written by an older format: keep it as it was before this version ever saves over it.
                    string copy = FilePath + $".v{settings.Version}.bak";
                    try { if (!File.Exists(copy)) File.Copy(FilePath, copy); } catch (IOException ex) { AppLog.Warn("The settings could not be copied before they were upgraded: " + ex.Message); }
                }
                settings.Migrate();
                return (settings, SettingsLoadStatus.Loaded, null);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                string backup = FilePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                try { File.Copy(FilePath, backup, overwrite: true); } catch (IOException) { backup = L.T("SettingsStore.BackupFailed"); }
                AppLog.Warn($"Settings file was unreadable; defaults used. Backup: {backup}", ex);
                var defaults = new AppSettings();
                defaults.Normalize();
                return (defaults, SettingsLoadStatus.RecoveredFromCorruption, backup);
            }
        }

        foreach (var legacy in legacyCandidates ?? Array.Empty<string>())
        {
            if (!File.Exists(legacy)) continue;
            try
            {
                var imported = LegacySettings.Import(legacy);
                imported.Normalize();
                return (imported, SettingsLoadStatus.ImportedLegacy, legacy);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                AppLog.Warn($"Legacy settings {legacy} could not be imported.", ex);
            }
        }
        var fresh = new AppSettings();
        fresh.Normalize();
        return (fresh, SettingsLoadStatus.CreatedDefaults, null);
    }

    public void Save(AppSettings settings)
    {
        try
        {
            AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Settings could not be saved.", ex);
            throw;
        }
    }
}

/// <summary>
/// Importer for the original <c>settings.json</c> (fields OutputPath, AllSpritesPath,
/// AllSoundsPath, AllSpritesPathTS, TSName, Author, GifDir).
/// </summary>
public static class LegacySettings
{
    private sealed class Legacy
    {
        public string? OutputPath { get; set; }
        public string? AllSpritesPath { get; set; }
        public string? AllSoundsPath { get; set; }
        public string? AllSpritesPathTS { get; set; }
        public string? TSName { get; set; }
        public string? Author { get; set; }
        public string? GifDir { get; set; }
    }

    public static AppSettings Import(string path, AppSettings? into = null)
    {
        var legacy = JsonSerializer.Deserialize<Legacy>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                     ?? throw new JsonException("Empty settings file.");
        var s = into ?? new AppSettings();
        if (!string.IsNullOrWhiteSpace(legacy.OutputPath)) s.OutputFolder = legacy.OutputPath.TrimEnd('\\', '/');
        if (!string.IsNullOrWhiteSpace(legacy.AllSpritesPath)) s.SpritesFolder = legacy.AllSpritesPath;
        if (!string.IsNullOrWhiteSpace(legacy.AllSpritesPathTS)) s.SpritesFolderTestServer = legacy.AllSpritesPathTS;
        if (!string.IsNullOrWhiteSpace(legacy.AllSoundsPath)) s.SoundsFolder = legacy.AllSoundsPath;
        if (!string.IsNullOrWhiteSpace(legacy.TSName)) s.TestServerPackage = legacy.TSName;
        // The 1.x GIF author becomes the creator name, except the 1.x default value.
        if (Metadata.Attribution.NormalizeCreator(legacy.Author) is { } author &&
            !AppIdentity.Legacy.IsDefaultGifAuthor(author))
            s.CreatorName = author;
        if (!string.IsNullOrWhiteSpace(legacy.GifDir)) s.GifFolder = legacy.GifDir;
        return s;
    }
}
