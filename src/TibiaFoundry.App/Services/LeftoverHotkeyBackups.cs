using System.Text.Json;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

/// <summary>A copy of the Tibia client's options file that a session of the removed hotkey tool never put back.</summary>
/// <param name="CreatedUtc">When the session started (null when unknown).</param>
/// <param name="OptionsPath">The client's options file the session changed (null when unknown).</param>
/// <param name="BackupPath">The copy of the options file as it was before the session.</param>
public sealed record LeftoverHotkeyBackup(DateTime? CreatedUtc, string? OptionsPath, string BackupPath);

/// <summary>
/// 8.0 removed the hotkey tool of 7.0–7.3 (it wrote batches of objects into the client's options
/// file and restored the original afterwards). A session interrupted before restoring left the only
/// copy of the original options file in <c>discovery\&lt;session&gt;\clientoptions.original.json</c>
/// under the local data folder. This only finds such copies so the user can be told where they are;
/// it never reads, writes or deletes anything in the client's folders. Kept for the 8.x releases.
/// </summary>
public static class LeftoverHotkeyBackups
{
    /// <summary>The folder name the removed tool used under the local data folder.</summary>
    public const string FolderName = "discovery";

    private const string SessionFile = "session.json";
    private const string BackupFile = "clientoptions.original.json";

    /// <summary>Sessions in <c>Prepared</c> or <c>Applied</c> state whose backup file still exists, newest first.</summary>
    public static IReadOnlyList<LeftoverHotkeyBackup> Find(string localDataDirectory)
    {
        var result = new List<LeftoverHotkeyBackup>();
        string root = Path.Combine(localDataDirectory, FolderName);
        try
        {
            if (!Directory.Exists(root)) return result;
            foreach (string folder in Directory.EnumerateDirectories(root))
            {
                string session = Path.Combine(folder, SessionFile), backup = Path.Combine(folder, BackupFile);
                if (!File.Exists(session) || !File.Exists(backup)) continue;
                try
                {
                    using var stream = new FileStream(session, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var doc = JsonDocument.Parse(stream);
                    var e = doc.RootElement;
                    string? state = Text(e, "state");
                    if (state is not ("Prepared" or "Applied")) continue;
                    DateTime? created = Text(e, "createdUtc") is { } t && DateTime.TryParse(t, null, System.Globalization.DateTimeStyles.RoundtripKind, out var c) ? c : null;
                    result.Add(new LeftoverHotkeyBackup(created, Text(e, "optionsPath"), backup));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    AppLog.Warn($"A session file of the removed hotkey tool cannot be read ({folder}): {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("The folder of the removed hotkey tool cannot be checked: " + ex.Message);
        }
        return result.OrderByDescending(b => b.CreatedUtc ?? DateTime.MinValue).ToList();

        static string? Text(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>What to tell the user about one copy.</summary>
    public static string Describe(LeftoverHotkeyBackup b) =>
        L.F("LeftoverHotkeyBackups.ACopyOfYourTibiaClient", (b.CreatedUtc is { } c ? c.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : L.T("LeftoverHotkeyBackups.AnEarlierSession")), b.BackupPath, b.OptionsPath ?? L.T("LeftoverHotkeyBackups.ConfClientoptionsJsonInTheClient"));
}
