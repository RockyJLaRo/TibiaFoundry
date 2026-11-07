using Tibia.Protobuf.Appearances;
using Tibia.Protobuf.Shared;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.Wiki;

namespace TibiaFoundry.Core.Imaging;

public enum GifDelayMode { Minimum, Average, Maximum }

public enum Direction { North = 0, East = 1, South = 2, West = 3 }

/// <summary>Options of the "Export GIF" box (defaults are the original's defaults).</summary>
public sealed record GifExportOptions
{
    /// <summary>0 = Idle, 1 = Moving (clamped to the appearance's frame groups).</summary>
    public int FrameGroup { get; init; } = 1;
    public Direction Direction { get; init; } = Direction.South;
    /// <summary>Pattern index to export; null = all patterns.</summary>
    public int? Pattern { get; init; }
    public GifDelayMode DelayMode { get; init; } = GifDelayMode.Average;
    /// <summary>Delay used when an appearance has several sprites but no animation (e.g. stack sizes).</summary>
    public int NoAnimationDelay { get; init; } = 1000;
    public bool AddEmptyExtraFrame { get; init; }
    /// <summary>Delay of the extra frame, or extra delay added to the first frame.</summary>
    public int ExtraDelay { get; init; }
    /// <summary>
    /// Whole-number enlargement with nearest-neighbor sampling: every client pixel becomes a square of
    /// Scale × Scale pixels, so pixel art stays sharp and no new colors appear (1 = the client's own size).
    /// </summary>
    public int Scale { get; init; } = 1;

    /// <summary>The largest <see cref="Scale"/> accepted.</summary>
    public const int MaxScale = 8;
}

/// <summary>A GIF ready to be written.</summary>
public sealed record GifAnimation(IReadOnlyList<RgbaImage> Frames, IReadOnlyList<int> Delays, string SuggestedName);

/// <summary>
/// Builds the frames of the "Save as GIF" export (port of the original <c>Export_Appearance_Gif</c>).
/// Fixed: ping-pong animations crashed (delay index out of range); pattern grids larger than 2×2
/// reused the wrong sprites (the offset wrapped at 3); missing sprite files crashed.
/// </summary>
public static class AppearanceGifBuilder
{
    public static GifAnimation Build(Appearance appearance, AppearanceKind kind, GifExportOptions options, ISpriteSource sprites)
    {
        if (appearance.FrameGroup.Count == 0) throw new InvalidOperationException($"Appearance {appearance.Id} has no sprites.");
        int fg = Math.Clamp(options.FrameGroup, 0, appearance.FrameGroup.Count - 1);
        var info = appearance.FrameGroup[fg].SpriteInfo;
        if (info == null || info.SpriteId.Count == 0)
            throw new InvalidOperationException($"Appearance {appearance.Id} has no sprite information in frame group {fg} (unsupported or damaged data).");
        List<uint> ids = info.SpriteId.ToList();
        int pW = (int)Math.Max(1, info.PatternWidth), pH = (int)Math.Max(1, info.PatternHeight);

        if (kind == AppearanceKind.Outfit)
        {
            int layers = (int)Math.Max(1, info.Layers), addons = pH, mounts = (int)Math.Max(1, info.PatternDepth);
            int perFrame = 4 * layers * addons * mounts;
            int wanted = (int)options.Direction * layers;
            ids = ids.Where((_, n) => n % perFrame == wanted).ToList();
        }
        else if ((pW > 1 || pH > 1) && options.Pattern is int pattern)
        {
            ids = ids.Where((_, n) => n % (pW * pH) == pattern).ToList();
        }
        if (ids.Count == 0) throw new InvalidOperationException($"Appearance {appearance.Id}: no sprites match the selected options.");

        var animation = info.Animation;
        List<int> phaseDelays;
        if (ids.Count > 1 && animation == null)
        {
            ids = ids.Distinct().ToList();
            phaseDelays = Enumerable.Repeat(options.NoAnimationDelay, ids.Count).ToList();
        }
        else if (kind == AppearanceKind.Outfit)
        {
            phaseDelays = Enumerable.Repeat(100, ids.Count).ToList();
        }
        else if (animation != null && animation.SpritePhase.Count > 0)
        {
            phaseDelays = animation.SpritePhase.Select(p => options.DelayMode switch
            {
                GifDelayMode.Minimum => (int)p.DurationMin,
                GifDelayMode.Maximum => (int)p.DurationMax,
                _ => (int)((p.DurationMin + p.DurationMax) / 2),
            }).ToList();
        }
        else
        {
            phaseDelays = [100];
        }

        RgbaImage Sprite(uint id) => sprites.GetSprite(id) ?? throw new InvalidOperationException($"Sprite {id} could not be loaded.");

        // Effects with patterns are exported as a grid of all patterns per frame.
        int gridW = kind == AppearanceKind.Effect ? pW : 1;
        int gridH = kind == AppearanceKind.Effect ? pH : 1;
        int perGrid = gridW * gridH;
        int frameCount = Math.Max(1, ids.Count / perGrid);
        var frames = new List<RgbaImage>();
        var delays = new List<int>();
        for (int f = 0; f < frameCount; f++)
        {
            var reference = Sprite(ids[f * perGrid]);
            var frame = new RgbaImage(reference.Width + 32 * (gridW - 1), reference.Height + 32 * (gridH - 1));
            int offset = 0;
            for (int h = 0; h < gridH; h++)
            {
                for (int w = 0; w < gridW; w++)
                {
                    int index = f * perGrid + offset++;
                    if (index < ids.Count) frame.DrawImage(Sprite(ids[index]), w * 32, h * 32);
                }
            }
            frames.Add(frame);
            delays.Add(phaseDelays[f % phaseDelays.Count]);
        }

        if (animation?.LoopType == ANIMATION_LOOP_TYPE.Pingpong)
        {
            for (int i = ids.Count - 2; i > 0; i--)
            {
                frames.Add(Sprite(ids[i]));
                delays.Add(phaseDelays[i % phaseDelays.Count]);
            }
        }

        if (options.AddEmptyExtraFrame)
        {
            frames.Add(new RgbaImage(frames[0].Width, frames[0].Height));
            delays.Add(options.ExtraDelay);
        }
        else
        {
            delays[0] += options.ExtraDelay;
        }

        int scale = Math.Clamp(options.Scale, 1, GifExportOptions.MaxScale);
        if (scale > 1) frames = frames.Select(f => ImageResampler.Resize(f, f.Width * scale, f.Height * scale, ResampleFilter.Nearest)).ToList();
        return new GifAnimation(frames, delays, SuggestedName(appearance, kind, options));
    }

    /// <summary>
    /// File name used by "Automatically name files": object name (or id), outfits "id_Idle_South", others id;
    /// enlarged GIFs end in "_2x", "_3x"… so they are never mistaken for the client's own size.
    /// </summary>
    public static string SuggestedName(Appearance appearance, AppearanceKind kind, GifExportOptions options)
    {
        string name = kind switch
        {
            AppearanceKind.Object => string.IsNullOrEmpty(appearance.Name) ? appearance.Id.ToString() : appearance.Name,
            AppearanceKind.Outfit => $"{appearance.Id}_{(options.FrameGroup == 0 ? "Idle" : "Moving")}_{options.Direction}",
            _ => appearance.Id.ToString(),
        };
        name = NameFormatter.Capitalize(name);
        return options.Scale > 1 ? $"{name}_{Math.Min(options.Scale, GifExportOptions.MaxScale)}x" : name;
    }

    /// <summary>Number of patterns available for the "Pattern Stage" selector (0 when not applicable).</summary>
    public static int PatternCount(Appearance appearance)
    {
        if (appearance.FrameGroup.Count == 0) return 0;
        var info = appearance.FrameGroup[^1].SpriteInfo;
        if (info == null) return 0;
        return info.PatternWidth > 1 || info.PatternHeight > 1 ? (int)(info.PatternWidth * info.PatternHeight) : 0;
    }
}
