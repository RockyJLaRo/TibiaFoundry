using Google.Protobuf;
using Tibia.Protobuf.Appearances;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Appearances;

/// <summary>The four appearance categories of <c>appearances.dat</c>.</summary>
public enum AppearanceKind { Object = 0, Outfit = 1, Effect = 2, Missile = 3 }

/// <summary>A currency used in NPC trade data (object or quest-flag currency).</summary>
/// <param name="Key">Lower-case display name used for lookup.</param>
/// <param name="ObjectId">Object id for item currencies; null for quest-flag currencies.</param>
public sealed record Currency(string Key, uint? ObjectId)
{
    public override string ToString() => ObjectId is { } id ? $"{Key} - {id}" : $"{Key} - {Key}";
}

/// <summary>
/// A loaded <c>appearances.dat</c> with fast lookups. Replaces the original's parallel
/// ID/sprite lists and <c>BinaryFormatter</c> deep copies.
/// </summary>
public sealed class AppearanceDatabase
{
    private readonly Dictionary<uint, Appearance>[] _byId;

    public AppearanceDatabase(Tibia.Protobuf.Appearances.Appearances data, string? sourcePath = null)
    {
        Data = data;
        SourcePath = sourcePath;
        _byId = new Dictionary<uint, Appearance>[4];
        for (int k = 0; k < 4; k++)
        {
            var dict = new Dictionary<uint, Appearance>();
            foreach (var a in List((AppearanceKind)k)) dict.TryAdd(a.Id, a);
            _byId[k] = dict;
        }
        Currencies = BuildCurrencies();
    }

    public Tibia.Protobuf.Appearances.Appearances Data { get; }
    public string? SourcePath { get; }

    /// <summary>Currencies found in NPC trade data (the original "Currencies" box).</summary>
    public IReadOnlyList<Currency> Currencies { get; }

    public static AppearanceDatabase Load(string path)
    {
        // The original kept the file open (File.OpenRead without dispose), locking it.
        var data = Protobuf.ProtoLoader.Load(path, Tibia.Protobuf.Appearances.Appearances.Parser);
        return new AppearanceDatabase(data, path);
    }

    public IReadOnlyList<Appearance> List(AppearanceKind kind) => kind switch
    {
        AppearanceKind.Object => Data.Object,
        AppearanceKind.Outfit => Data.Outfit,
        AppearanceKind.Effect => Data.Effect,
        _ => Data.Missile,
    };

    public Appearance? Find(AppearanceKind kind, uint id) => _byId[(int)kind].GetValueOrDefault(id);

    public int Count(AppearanceKind kind) => List(kind).Count;

    /// <summary>
    /// Sprite shown in list thumbnails (same choice as the original's initial load):
    /// objects use their first sprite, outfits the south-facing base sprite, effects and
    /// missiles the third sprite (clamped).
    /// </summary>
    public static uint? PreviewSpriteId(Appearance appearance, AppearanceKind kind)
    {
        if (appearance.FrameGroup.Count == 0) return null;
        var ids = appearance.FrameGroup[0].SpriteInfo?.SpriteId;
        if (ids == null || ids.Count == 0) return null;
        int index = kind switch
        {
            AppearanceKind.Outfit => (int)Math.Min(appearance.FrameGroup[0].SpriteInfo.Layers * 2, (uint)ids.Count - 1),
            AppearanceKind.Effect or AppearanceKind.Missile => Math.Min(2, ids.Count - 1),
            _ => 0,
        };
        return ids[index];
    }

    /// <summary>Display name: the object's name, or "#id" for nameless appearances.</summary>
    public static string DisplayName(Appearance a) => string.IsNullOrEmpty(a.Name) ? "" : a.Name;

    private List<Currency> BuildCurrencies()
    {
        var result = new List<Currency>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var objectIds = new List<uint>();
        var questNames = new List<string>();
        foreach (var obj in Data.Object)
        {
            foreach (var npc in obj.Flags?.Npcsaledata ?? new Google.Protobuf.Collections.RepeatedField<AppearanceFlagNPC>())
            {
                if (npc.CurrencyObjectTypeId != 0 && !objectIds.Contains(npc.CurrencyObjectTypeId)) objectIds.Add(npc.CurrencyObjectTypeId);
                if (npc.CurrencyQuestFlagDisplayName.Length > 0 && !questNames.Contains(npc.CurrencyQuestFlagDisplayName)) questNames.Add(npc.CurrencyQuestFlagDisplayName);
            }
        }
        foreach (uint id in objectIds)
        {
            // The original threw when two currency objects shared a name (Dictionary.Add); keep the first.
            string name = (Find(AppearanceKind.Object, id)?.Name ?? L.F("AppearanceDatabase.Object0", id)).ToLowerInvariant();
            if (seen.Add(name)) result.Add(new Currency(name, id));
        }
        foreach (string quest in questNames)
        {
            string key = quest.ToLowerInvariant();
            if (seen.Add(key)) result.Add(new Currency(key, null));
        }
        return result;
    }

    public Currency? FindCurrency(string text) =>
        Currencies.FirstOrDefault(c => string.Equals(c.Key, text.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Sprite IDs of the last frame group (what the original shows in the "SpriteIDs" box).</summary>
    public static IReadOnlyList<uint> DisplaySpriteIds(Appearance a) =>
        a.FrameGroup.Count == 0 ? Array.Empty<uint>() : a.FrameGroup[^1].SpriteInfo?.SpriteId.ToList() ?? (IReadOnlyList<uint>)Array.Empty<uint>();

    /// <summary>All sprite IDs of the first two frame groups (used by "View All").</summary>
    public static List<uint> AllSpriteIds(Appearance a, int maxFrameGroups = 2) =>
        a.FrameGroup.Take(maxFrameGroups).SelectMany(fg => fg.SpriteInfo?.SpriteId ?? (IEnumerable<uint>)Array.Empty<uint>()).ToList();

    /// <summary>Clones an appearance message (protobuf deep copy).</summary>
    public static T Clone<T>(T message) where T : IDeepCloneable<T> => message.Clone();
}
