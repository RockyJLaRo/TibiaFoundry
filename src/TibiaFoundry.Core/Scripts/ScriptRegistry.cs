using System.Text.Json;
using System.Text.Json.Serialization;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;

namespace TibiaFoundry.Core.Scripts;

/// <summary>
/// The built-in scripts. A new script is added by writing a <see cref="Script"/> subclass and
/// listing it here; the Scripts page builds its form from the script's parameters.
/// </summary>
public static class ScriptRegistry
{
    /// <param name="backups">The application's backup store (used by Backup and Restore).</param>
    /// <param name="renameRecords">Folder where the renamer keeps its undo records.</param>
    public static IReadOnlyList<Script> CreateAll(Func<BackupStore> backups, string renameRecords) =>
    [
        new DuplicateFileFinderScript(),
        new LargeFileFinderScript(),
        new EmptyEntryFinderScript(),
        new BackupRestoreScript(backups),
        new DirectoryComparisonScript(),
        new ChecksumScript(),
        new BatchRenamerScript { RecordFolder = renameRecords },
        new ImageConverterScript(),
        new ArchiveInspectorScript(),
        new TibiaProjectAuditorScript(),
        new InventoryScript(),
        new FormatInspectorScript(),
    ];
}

/// <summary>One run of a script, as kept in the history.</summary>
public sealed class ScriptHistoryEntry
{
    public string ScriptId { get; set; } = "";
    public string ScriptName { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public double Seconds { get; set; }

    /// <summary>"Run", "Preview" or "Apply".</summary>
    public string Kind { get; set; } = "Run";

    /// <summary>The main input (folder or file).</summary>
    public string? Input { get; set; }
    public string? Output { get; set; }
    public int Processed { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public bool ChangesApplied { get; set; }
    public bool Cancelled { get; set; }
    public string Summary { get; set; } = "";
    public string? BackupFolder { get; set; }
    public List<string> OutputFiles { get; set; } = [];
}

/// <summary>
/// History of script runs, kept as JSON next to the settings. It records what ran, when, on which
/// folder, with which counts and where the results went — never the content of files.
/// </summary>
public sealed class ScriptHistory(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly object _sync = new();

    public string FilePath { get; } = path;

    public IReadOnlyList<ScriptHistoryEntry> Load()
    {
        lock (_sync)
        {
            try
            {
                return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<ScriptHistoryEntry>>(File.ReadAllText(FilePath), Json) ?? [] : [];
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Script history could not be read; it starts empty: " + ex.Message);
                return [];
            }
        }
    }

    /// <summary>Adds an entry (newest first) and keeps at most <paramref name="maxEntries"/>.</summary>
    public void Add(ScriptHistoryEntry entry, int maxEntries)
    {
        lock (_sync)
        {
            var list = Load().ToList();
            list.Insert(0, entry);
            if (list.Count > maxEntries) list.RemoveRange(maxEntries, list.Count - maxEntries);
            try
            {
                AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(list, Json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Script history could not be saved: " + ex.Message);
            }
        }
    }

    /// <summary>Removes the history (the files the scripts wrote are not touched).</summary>
    public void Clear()
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Script history could not be cleared: " + ex.Message);
            }
        }
    }
}
