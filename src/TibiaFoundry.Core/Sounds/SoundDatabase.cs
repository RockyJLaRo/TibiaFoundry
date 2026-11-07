using TibiaFoundry.Core.Compatibility;
using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Tibia.Protobuf.Sound;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Protobuf;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Sounds;

public enum SoundListKind { Sounds, SoundEffects, Ambiences, AmbienceObjects, MusicTemplates }

/// <summary>A row of one of the sound lists (ID and, where meaningful, the main sound id).</summary>
public sealed record SoundListItem(uint Id, string Detail, IMessage Source)
{
    public uint? SoundId { get; init; }
}

/// <summary>Loaded <c>sounds-*.dat</c>.</summary>
public sealed class SoundDatabase
{
    private readonly Dictionary<uint, Sound> _sounds = new();
    private readonly Dictionary<uint, NumericSoundEffect> _effects = new();

    public SoundDatabase(Tibia.Protobuf.Sound.Sounds data, string? soundsDirectory)
        : this(data, soundsDirectory == null ? null : new PhysicalClientFolder(soundsDirectory))
    {
    }

    public SoundDatabase(Tibia.Protobuf.Sound.Sounds data, IClientFolder? soundsFolder)
    {
        Data = data;
        SoundsFolder = soundsFolder;
        foreach (var s in data.Sound) _sounds.TryAdd(s.Id, s);
        foreach (var e in data.NumericSoundEffect) _effects.TryAdd(e.Id, e);
    }

    public Tibia.Protobuf.Sound.Sounds Data { get; }

    /// <summary>The client's sounds folder (source of extraction and fallback playback), as shown to the user.</summary>
    public string? SoundsDirectory => SoundsFolder?.DisplayPath;

    /// <summary>Where the sound files are read from: a real folder, or an imported version (7.3).</summary>
    public IClientFolder? SoundsFolder { get; }

    /// <summary>A readable path of a file of the sounds folder, or null when it has none.</summary>
    public string? ClientFile(string name) => SoundsFolder?.PathOf(name);

    public static SoundDatabase Load(string path) => Load(path, null);

    /// <summary>Loads a sounds file; its sound files are looked up in <paramref name="soundsFolder"/> (default: the file's own folder).</summary>
    public static SoundDatabase Load(string path, IClientFolder? soundsFolder) =>
        new(ProtoLoader.Load(path, Tibia.Protobuf.Sound.Sounds.Parser), soundsFolder ?? (Path.GetDirectoryName(path) is { } dir ? new PhysicalClientFolder(dir) : null));

    public Sound? FindSound(uint id) => _sounds.GetValueOrDefault(id);

    public IReadOnlyList<SoundListItem> Items(SoundListKind kind) => kind switch
    {
        SoundListKind.Sounds => Data.Sound.Select(s => new SoundListItem(s.Id, s.Filename, s) { SoundId = s.Id }).ToList(),
        SoundListKind.SoundEffects => Data.NumericSoundEffect.Select(e => new SoundListItem(e.Id, e.NumericSoundType.ToString(), e)
        {
            SoundId = e.SimpleSoundEffect?.SoundId ?? e.RandomSoundEffect?.RandomSoundId.FirstOrDefault(),
        }).ToList(),
        SoundListKind.Ambiences => Data.AmbienceStream.Select(a => new SoundListItem(a.Id, a.LoopingSoundId.ToString(), a) { SoundId = a.LoopingSoundId }).ToList(),
        // Fixed: the original crashed when an ambience object had no sound effects.
        SoundListKind.AmbienceObjects => Data.AmbienceObjectStream.Select(a => new SoundListItem(a.Id, a.SoundEffects.FirstOrDefault()?.LoopingSoundId.ToString() ?? "", a)
        {
            SoundId = a.SoundEffects.FirstOrDefault()?.LoopingSoundId,
        }).ToList(),
        _ => Data.MusicTemplate.Select(m => new SoundListItem(m.Id, m.SoundId.ToString(), m) { SoundId = m.SoundId }).ToList(),
    };

    /// <summary>File name of an extracted sound: "&lt;id&gt;&lt;ext&gt;" (e.g. "17.ogg").</summary>
    public static string ExtractedName(Sound sound) => sound.Id + Path.GetExtension(TibiaFormat.SafeFileName(sound.Filename) ?? "");

    /// <summary>
    /// Path of a playable file for a sound: the extracted copy when present, otherwise the client
    /// file itself (OGG) — so playback no longer requires extracting everything first.
    /// </summary>
    public string? ResolvePlayableFile(uint soundId, string? extractedDirectory)
    {
        var sound = FindSound(soundId);
        if (sound == null) return null;
        if (!string.IsNullOrEmpty(extractedDirectory))
        {
            string extracted = Path.Combine(extractedDirectory, ExtractedName(sound));
            if (File.Exists(extracted)) return extracted;
        }
        if (SoundsFolder == null || TibiaFormat.SafeFileName(sound.Filename) is not { } safeName) return null;
        if (ClientFile(safeName) is { } client) return client;
        if (ClientFile(safeName + ".lzma") is { } compressed)
        {
            // Cache by the content-hashed client file name, not by sound ID: after a client update the
            // same ID can refer to a different sound, and an ID-keyed cache would replay the old one.
            string temp = Path.Combine(Path.GetTempPath(), AppIdentity.Id, "sounds", safeName);
            if (!File.Exists(temp)) AtomicFile.WriteAllBytes(temp, CipLzma.DecompressFile(compressed), durable: false);
            return temp;
        }
        return null;
    }

    // ------------------------------------------------------------------ data panel rows

    /// <summary>
    /// Rows for the Data panel (original <c>SelectedSoundChanged</c>/<c>AddSoundPropertyToList</c>):
    /// rows that reference a sound get a play button; ambience objects show the sprites of their
    /// counted appearance types.
    /// </summary>
    public List<PropertyRow> Rows(IMessage item)
    {
        var rows = new List<PropertyRow>();
        switch (item)
        {
            case NumericSoundEffect e:
                foreach (var prop in Props(e))
                {
                    if (prop.Name == "RandomSoundEffect")
                    {
                        if (e.RandomSoundEffect != null)
                            for (int k = 0; k < e.RandomSoundEffect.RandomSoundId.Count; k++)
                                rows.Add(SoundRow("Sound_" + k, e.RandomSoundEffect.RandomSoundId[k]));
                    }
                    else if (prop.Name == "SimpleSoundEffect")
                    {
                        if (e.SimpleSoundEffect != null) rows.Add(SoundRow("Sound_Simple", e.SimpleSoundEffect.SoundId));
                    }
                    else AddPlain(rows, prop.Name, prop.GetValue(e));
                }
                break;
            case AmbienceStream a:
                foreach (var prop in Props(a))
                {
                    if (prop.Name != "DelayedEffects") { AddPlain(rows, prop.Name, prop.GetValue(a)); continue; }
                    for (int m = 0; m < a.DelayedEffects.Count; m++)
                    {
                        var delayed = a.DelayedEffects[m];
                        rows.Add(new PropertyRow("NumericId_" + m, delayed.NumericSoundEffectId.ToString()));
                        var effect = _effects.GetValueOrDefault(delayed.NumericSoundEffectId);
                        if (effect?.SimpleSoundEffect != null)
                        {
                            rows.Add(SoundRow("Sound_" + m, effect.SimpleSoundEffect.SoundId));
                            rows.Add(new PropertyRow("DelaySound_" + m, delayed.DelaySeconds.ToString()));
                        }
                        else if (effect?.RandomSoundEffect != null)
                        {
                            for (int k = 0; k < effect.RandomSoundEffect.RandomSoundId.Count; k++)
                                rows.Add(SoundRow($"Sound_{m}_Type_{k}", effect.RandomSoundEffect.RandomSoundId[k]));
                            rows.Add(new PropertyRow("DelaySound_" + m, delayed.DelaySeconds.ToString()));
                        }
                        else rows.Add(new PropertyRow("EmptySound_" + m, delayed.NumericSoundEffectId.ToString()));
                    }
                }
                if (a.LoopingSoundId != 0) MarkPlayable(rows, "LoopingSoundId", a.LoopingSoundId);
                break;
            case AmbienceObjectStream o:
                foreach (var prop in Props(o))
                {
                    if (prop.Name == "SoundEffects")
                    {
                        for (int j = 0; j < o.SoundEffects.Count; j++)
                        {
                            rows.Add(SoundRow("Sound_" + j, o.SoundEffects[j].LoopingSoundId));
                            rows.Add(new PropertyRow("Count_" + j, o.SoundEffects[j].Count.ToString()));
                        }
                    }
                    else if (prop.Name == "CountedAppearanceTypes")
                    {
                        rows.Add(new PropertyRow("CountedAppearanceTypes", o.CountedAppearanceTypes.ToString()) { SpriteIds = o.CountedAppearanceTypes.ToList() });
                    }
                    else AddPlain(rows, prop.Name, prop.GetValue(o));
                }
                break;
            default:
                foreach (var prop in Props(item)) AddPlain(rows, prop.Name, prop.GetValue(item));
                if (item is MusicTemplate music) MarkPlayable(rows, "SoundId", music.SoundId);
                if (item is Sound sound) MarkPlayable(rows, "Id", sound.Id);
                break;
        }
        return rows;
    }

    private static IEnumerable<PropertyInfo> Props(IMessage m) =>
        m.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0);

    private static void AddPlain(List<PropertyRow> rows, string name, object? value)
    {
        if (value == null) return;
        rows.Add(new PropertyRow(name, value.ToString() ?? ""));
    }

    private PropertyRow SoundRow(string label, uint soundId) =>
        new(label, soundId.ToString()) { SoundId = FindSound(soundId) != null ? soundId : null };

    private void MarkPlayable(List<PropertyRow> rows, string label, uint soundId)
    {
        int i = rows.FindIndex(r => r.Label == label);
        if (i >= 0 && FindSound(soundId) != null) rows[i] = rows[i] with { SoundId = soundId };
    }

    // ------------------------------------------------------------------ extraction

    /// <summary>
    /// "Extract": copies OGG files and decompresses LZMA-packed WAV files into
    /// <paramref name="outputDirectory"/> as "&lt;id&gt;.&lt;ext&gt;". Runs off the UI thread, creates
    /// the folder, reports progress and continues past missing files (the original froze the UI
    /// and aborted on the first problem).
    /// </summary>
    public async Task<BatchResult> ExtractAsync(string outputDirectory, IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        if (SoundsFolder == null) throw new InvalidOperationException("The client sounds folder is unknown.");
        Directory.CreateDirectory(outputDirectory);
        var result = new BatchResult();
        var sounds = Data.Sound.ToList();
        await Task.Run(() =>
        {
            for (int i = 0; i < sounds.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var s = sounds[i];
                if (TibiaFormat.SafeFileName(s.Filename) is not { } name)
                {
                    result.Failures.Add(L.F("SoundDatabase.Sound0UnsafeOrEmptyFile", s.Id, s.Filename));
                    continue;
                }
                string ext = Path.GetExtension(name).ToLowerInvariant();
                string target = Path.Combine(outputDirectory, ExtractedName(s));
                string? source = ClientFile(name);
                string? packed = ClientFile(name + ".lzma");
                try
                {
                    if (ext == ".wav" && packed != null)
                        AtomicFile.WriteAllBytes(target, CipLzma.DecompressFile(packed), durable: false);
                    else if (source != null)
                        AtomicFile.Copy(source, target);
                    else
                    {
                        // Referenced by sounds.dat but not on disk: an incomplete client update, not a success.
                        result.Failures.Add(L.F("SoundDatabase.0ListedInTheSoundsData", name, SoundsDirectory));
                        continue;
                    }
                    result.Succeeded++;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    result.Failures.Add($"{s.Filename}: {ex.Message}");
                    AppLog.Warn($"Sound {s.Id} extraction failed: {ex.Message}");
                }
                progress?.Report(new OperationProgress(i + 1, sounds.Count, s.Filename));
            }
        }, ct);
        return result;
    }
}
