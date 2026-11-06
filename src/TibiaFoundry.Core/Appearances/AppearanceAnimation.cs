using Tibia.Protobuf.Appearances;
using Tibia.Protobuf.Shared;

namespace TibiaFoundry.Core.Appearances;

/// <summary>Sprite sequence and per-frame delays of an animation preview.</summary>
public sealed record AnimationFrames(IReadOnlyList<uint> SpriteIds, IReadOnlyList<int> Delays)
{
    public bool IsAnimated => SpriteIds.Count > 1;
    public static readonly AnimationFrames Empty = new(Array.Empty<uint>(), Array.Empty<int>());
}

/// <summary>
/// Preview animation of the appearance viewer (port of the original <c>SelectedAppearanceChanged</c>).
/// Uses the last frame group: for outfits the south-facing base sprite of every frame, for other
/// kinds every sprite in order (so pattern variants such as coin stacks cycle too).
/// Phase durations use DurationMin; "counted" loops end on the first frame and "ping-pong"
/// loops play back again.
/// </summary>
public static class AppearanceAnimation
{
    public static AnimationFrames ForPreview(Appearance appearance, AppearanceKind kind)
    {
        if (appearance.FrameGroup.Count == 0) return AnimationFrames.Empty;
        var info = appearance.FrameGroup[^1].SpriteInfo;
        if (info == null || info.SpriteId.Count == 0) return AnimationFrames.Empty;
        var ids = info.SpriteId;
        int layers = (int)Math.Max(1, info.Layers);
        int front = kind == AppearanceKind.Outfit ? Math.Min(2 * layers, ids.Count - 1) : 0;
        if (ids.Count == 1) return new AnimationFrames(new[] { ids[0] }, new[] { 1000 });

        var animation = info.Animation;
        int frameCount = animation != null ? animation.SpritePhase.Count : ids.Count;
        int stride = (int)(Math.Max(1, info.PatternWidth) * layers * Math.Max(1, info.PatternHeight) * Math.Max(1, info.PatternDepth));
        var sprites = new List<uint>();
        var delays = new List<int>();
        for (int j = 0; j < frameCount; j++)
        {
            int index = kind == AppearanceKind.Outfit ? front + j * stride : j;
            if (index >= ids.Count) continue;
            sprites.Add(ids[index]);
            if (kind == AppearanceKind.Outfit) delays.Add(frameCount > 2 ? 100 : 500);
            else if (animation != null && j < animation.SpritePhase.Count) delays.Add((int)animation.SpritePhase[j].DurationMin);
            else delays.Add(1000);
        }
        if (sprites.Count == 0) return new AnimationFrames(new[] { ids[front] }, new[] { 1000 });

        if (animation != null && kind != AppearanceKind.Outfit)
        {
            if (animation.LoopType == ANIMATION_LOOP_TYPE.Counted)
            {
                sprites.Add(ids[0]);
                delays.Add(1000);
            }
            else if (animation.LoopType == ANIMATION_LOOP_TYPE.Pingpong)
            {
                for (int i = animation.SpritePhase.Count - 2; i >= 1; i--)
                {
                    if (i >= ids.Count) continue;
                    sprites.Add(ids[i]);
                    delays.Add((int)animation.SpritePhase[i].DurationMin);
                }
            }
        }
        return new AnimationFrames(sprites, delays.Select(d => Math.Max(1, d)).ToList());
    }
}
