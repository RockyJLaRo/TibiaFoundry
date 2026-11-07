using System.Reflection;
using Google.Protobuf;
using Tibia.Protobuf.Staticdata;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Protobuf;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.Core.StaticData;

public enum StaticDataKind { Monsters, Achievements, Houses, Bosses, Quests }

/// <summary>A monster or boss (both have a name, race and outfit).</summary>
public sealed record Creature(string Name, uint Race, Outfit? Outfit, IMessage Source, bool IsBoss);

/// <summary>Loaded <c>staticdata-*.dat</c>.</summary>
public sealed class StaticDataDatabase
{
    public StaticDataDatabase(Tibia.Protobuf.Staticdata.StaticData data)
    {
        Data = data;
        Monsters = data.Monster.Select(m => new Creature(m.Name, m.Race, m.Outfit, m, false)).ToList();
        Bosses = data.Bosses.Select(b => new Creature(b.Name, b.Race, b.Outfit, b, true)).ToList();
    }

    public Tibia.Protobuf.Staticdata.StaticData Data { get; }
    public IReadOnlyList<Creature> Monsters { get; }
    public IReadOnlyList<Creature> Bosses { get; }

    public static StaticDataDatabase Load(string path) => new(ProtoLoader.Load(path, Tibia.Protobuf.Staticdata.StaticData.Parser));

    /// <summary>
    /// Data panel rows (original <c>SelectedStaticChanged</c>/<c>SelectedCreatureChanged</c>):
    /// every property except false booleans; outfit colors shown as "head/torso/legs/detail";
    /// for creatures the outfit's own fields follow.
    /// </summary>
    public static List<PropertyRow> Rows(IMessage message)
    {
        var rows = new List<PropertyRow>();
        AddMessageRows(rows, message);
        var outfitProp = message.GetType().GetProperty("Outfit");
        if (outfitProp?.GetValue(message) is IMessage outfit) AddMessageRows(rows, outfit);
        return rows;
    }

    private static void AddMessageRows(List<PropertyRow> rows, IMessage message)
    {
        foreach (var prop in message.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0 || prop.Name == "Outfit") continue;
            object? value = prop.GetValue(message);
            if (value == null || value is false) continue;
            string text = value switch
            {
                OutfitColors c => $"{c.HeadColor}/{c.TorsoColor}/{c.LegsColor}/{c.DetailColor}",
                Tibia.Protobuf.Shared.Coordinate c => $"[{c.X}, {c.Y}, {c.Z}]",
                _ => value.ToString() ?? "",
            };
            var row = new PropertyRow(prop.Name, text);
            if (value is Tibia.Protobuf.Shared.Coordinate coord) row = row with { Link = MapLinks.TibiaMaps(coord.X, coord.Y, coord.Z) };
            rows.Add(row);
        }
    }

    public static int[] ColorsOf(Outfit? outfit) => outfit?.Outfitcolors is { } c
        ? [(int)c.HeadColor, (int)c.TorsoColor, (int)c.LegsColor, (int)c.DetailColor]
        : [0, 0, 0, 0];
}

/// <summary>Animated preview of a creature.</summary>
public sealed record CreatureAnimation(IReadOnlyList<RgbaImage> Frames, IReadOnlyList<int> Delays);

/// <summary>
/// Creature thumbnails and animated previews (Static Data page).
/// </summary>
/// <remarks>
/// Outfit creatures are rendered moving south with their colors and addons (original
/// behavior). Creatures that look like an object (e.g. some bosses) use the object's animation;
/// the original passed the object id to the outfit renderer, which crashed or showed a wrong outfit.
/// </remarks>
public static class CreatureImages
{
    /// <summary>List thumbnail: the raw south-facing base sprite, or the rendered first frame when <paramref name="renderOutfit"/>.</summary>
    public static RgbaImage? Thumbnail(Creature creature, AppearanceDatabase db, ISpriteSource sprites, bool renderOutfit)
    {
        var outfit = creature.Outfit;
        if (outfit == null) return null;
        if (outfit.ObjectAppearanceTypeId != 0)
        {
            var obj = db.Find(AppearanceKind.Object, outfit.ObjectAppearanceTypeId);
            uint? id = obj == null ? null : AppearanceDatabase.PreviewSpriteId(obj, AppearanceKind.Object);
            return id is { } sid ? sprites.GetSprite(sid) : null;
        }
        var appearance = db.Find(AppearanceKind.Outfit, outfit.OutfitId);
        if (appearance == null) return null;
        if (renderOutfit)
        {
            try
            {
                return OutfitRenderer.Render(db, sprites, outfit.OutfitId, new OutfitRenderOptions
                {
                    FrameGroup = 0, Direction = 2, Colors = StaticDataDatabase.ColorsOf(outfit), Addons = outfit.Addons, OnlyFirstFrame = true,
                })[0];
            }
            catch (OutfitRenderException) { return null; }
        }
        uint? sprite = AppearanceDatabase.PreviewSpriteId(appearance, AppearanceKind.Outfit);
        return sprite is { } s ? sprites.GetSprite(s) : null;
    }

    public static CreatureAnimation? Animate(Creature creature, AppearanceDatabase db, ISpriteSource sprites)
    {
        var outfit = creature.Outfit;
        if (outfit == null || (outfit.OutfitId == 0 && outfit.ObjectAppearanceTypeId == 0)) return null;
        if (outfit.OutfitId != 0)
        {
            var appearance = db.Find(AppearanceKind.Outfit, outfit.OutfitId);
            if (appearance == null) return null;
            var frames = OutfitRenderer.Render(db, sprites, outfit.OutfitId, new OutfitRenderOptions
            {
                FrameGroup = 1, Direction = 2, Colors = StaticDataDatabase.ColorsOf(outfit), Addons = outfit.Addons,
            });
            int delay = frames.Count > 2 ? 100 : 300;
            return new CreatureAnimation(frames, Enumerable.Repeat(delay, frames.Count).ToList());
        }
        var obj = db.Find(AppearanceKind.Object, outfit.ObjectAppearanceTypeId);
        if (obj == null) return null;
        var anim = AppearanceAnimation.ForPreview(obj, AppearanceKind.Object);
        var images = anim.SpriteIds.Select(sprites.GetSprite).ToList();
        if (images.Any(i => i == null)) return null;
        return new CreatureAnimation(images!, anim.Delays);
    }
}
