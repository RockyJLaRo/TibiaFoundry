using System.Globalization;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Tools;

/// <summary>
/// Per-tool settings declared in a <see cref="ToolManifest"/>, stored in the settings file's tool
/// section ("prefix.key" → value). Values are validated against their definitions when read and
/// written: an invalid stored value reads as the default (and is reported once in the log), an
/// invalid new value is refused with a message. Each tool's settings carry a version so they can be
/// upgraded with the tool's migrations.
/// </summary>
public sealed class ToolSettingsStore(IDictionary<string, string> storage)
{
    /// <summary>Key (after the tool's prefix) of the stored settings version.</summary>
    public const string VersionKey = "$settingsVersion";

    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    private static string Key(ToolManifest tool, string key) => tool.EffectiveSettingsPrefix + key;

    /// <summary>The validated value of a setting (its default when nothing valid is stored).</summary>
    public string Get(ToolManifest tool, string key)
    {
        var definition = tool.Setting(key) ?? throw new ArgumentException($"{tool.Name} declares no setting \"{key}\".", nameof(key));
        if (!storage.TryGetValue(Key(tool, key), out string? stored)) return definition.Default;
        string? valid = definition.Validate(stored, out string? problem);
        if (valid != null) return valid;
        if (_reported.Add(Key(tool, key))) AppLog.Warn($"{tool.Name}: the stored value of \"{definition.Label}\" is not valid ({problem}); the default is used.", source: "Tools");
        return definition.Default;
    }

    public bool GetBool(ToolManifest tool, string key) => Get(tool, key) == "true";

    public long GetLong(ToolManifest tool, string key) => long.Parse(Get(tool, key), CultureInfo.InvariantCulture);

    /// <summary>Stores a value after validating it; returns false with the reason when it is not valid.</summary>
    public bool TrySet(ToolManifest tool, string key, string? value, out string? problem)
    {
        var definition = tool.Setting(key) ?? throw new ArgumentException($"{tool.Name} declares no setting \"{key}\".", nameof(key));
        string? valid = definition.Validate(value, out problem);
        if (valid == null) return false;
        storage[Key(tool, key)] = valid;
        storage[Key(tool, VersionKey)] = tool.SettingsVersion.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    public void SetBool(ToolManifest tool, string key, bool value) => TrySet(tool, key, value ? "true" : "false", out _);

    /// <summary>Whether a setting holds its default value.</summary>
    public bool IsDefault(ToolManifest tool, string key) => Get(tool, key) == tool.Setting(key)!.Default;

    /// <summary>Restores every declared setting of the tool to its default (other tools are not touched).</summary>
    public void Reset(ToolManifest tool)
    {
        foreach (var s in tool.Settings) storage.Remove(Key(tool, s.Key));
        storage.Remove(Key(tool, VersionKey));
    }

    /// <summary>The version the stored settings were written with (the current one when none are stored).</summary>
    public int StoredVersion(ToolManifest tool)
    {
        if (storage.TryGetValue(Key(tool, VersionKey), out string? v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1) return n;
        // Settings stored before versions were recorded are version 1.
        return tool.Settings.Any(s => storage.ContainsKey(Key(tool, s.Key))) ? 1 : tool.SettingsVersion;
    }

    /// <summary>
    /// Upgrades the tool's stored settings to its current settings version by running its
    /// migrations in order. Settings written by a newer version are left unchanged. Returns what was
    /// done (empty when nothing had to change).
    /// </summary>
    public IReadOnlyList<string> Migrate(ToolManifest tool)
    {
        int stored = StoredVersion(tool);
        if (stored >= tool.SettingsVersion) return [];
        string prefix = tool.EffectiveSettingsPrefix;
        var values = storage.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal) && kv.Key != prefix + VersionKey)
            .ToDictionary(kv => kv.Key[prefix.Length..], kv => kv.Value, StringComparer.Ordinal);
        var done = new List<string>();
        foreach (var step in tool.SettingsMigrations.Where(m => m.FromVersion >= stored).OrderBy(m => m.FromVersion))
        {
            step.Apply(values);
            done.Add(L.F("ToolSettingsStore.0Settings123", tool.Name, step.FromVersion, step.FromVersion + 1, step.Description));
        }
        foreach (string key in storage.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList()) storage.Remove(key);
        foreach (var (key, value) in values) storage[prefix + key] = value;
        storage[prefix + VersionKey] = tool.SettingsVersion.ToString(CultureInfo.InvariantCulture);
        foreach (string line in done) AppLog.Info(line, source: "Tools");
        return done;
    }
}
