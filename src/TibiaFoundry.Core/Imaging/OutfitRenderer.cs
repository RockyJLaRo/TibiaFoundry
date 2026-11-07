using Tibia.Protobuf.Appearances;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.Core.Imaging;

/// <summary>Parameters of <see cref="OutfitRenderer.Render"/>.</summary>
public sealed record OutfitRenderOptions
{
    /// <summary>0 = idle, 1 = moving (clamped to the outfit's frame groups).</summary>
    public int FrameGroup { get; init; }
    /// <summary>0 = north, 1 = east, 2 = south, 3 = west.</summary>
    public int Direction { get; init; } = 2;
    public bool Mounted { get; init; }
    /// <summary>Outfit palette indexes: head, primary (torso), secondary (legs), detail (feet).</summary>
    public IReadOnlyList<int> Colors { get; init; } = GameTables.DefaultOutfitColors;
    /// <summary>Bit 1 = first addon, bit 2 = second addon.</summary>
    public uint Addons { get; init; }
    public bool OnlyFirstFrame { get; init; }
    /// <summary>Outfit id of a mount to draw underneath (0 = none).</summary>
    public uint MountId { get; init; }
    public IReadOnlyList<int>? MountColors { get; init; }
}

public sealed class OutfitRenderException(string message) : Exception(message);

/// <summary>
/// Renders colored outfit frames (port of the original <c>Helper.GetOutfitFrames</c>).
/// </summary>
/// <remarks>
/// Sprite layout per frame group: index = ((((frame·pD + mount)·pH + addon)·pW + direction)·layers + layer).
/// Layer 0 is the base sprite, layer 1 the color template (yellow=head, red=primary,
/// green=secondary, blue=detail). The original colored the template with a GDI+ color map over
/// white, composited it with ImageMagick "Multiply" and restored the base alpha ("CopyAlpha"),
/// then stacked addons with ImageMagick "Mosaic". The same arithmetic is reproduced here at
/// ImageMagick's 16-bit quantum precision, without the native round-trips.
/// </remarks>
public static class OutfitRenderer
{
    private const uint TemplateYellow = 0xFFFFFF00, TemplateRed = 0xFFFF0000, TemplateGreen = 0xFF00FF00, TemplateBlue = 0xFF0000FF;

    public static List<RgbaImage> Render(AppearanceDatabase db, ISpriteSource sprites, uint outfitId, OutfitRenderOptions options)
    {
        var outfit = db.Find(AppearanceKind.Outfit, outfitId) ?? throw new OutfitRenderException($"Outfit {outfitId} does not exist.");
        if (outfit.FrameGroup.Count == 0) throw new OutfitRenderException($"Outfit {outfitId} has no frame groups.");
        int fg = Math.Clamp(options.FrameGroup, 0, outfit.FrameGroup.Count - 1);
        var info = outfit.FrameGroup[fg].SpriteInfo;
        if (info == null || info.SpriteId.Count == 0)
            throw new OutfitRenderException($"Outfit {outfitId} has no sprite information in frame group {fg} (unsupported or damaged data).");
        int pW = (int)Math.Max(1, info.PatternWidth), pH = (int)Math.Max(1, info.PatternHeight);
        int pD = (int)Math.Max(1, info.PatternDepth), layers = (int)Math.Max(1, info.Layers);
        int perFrame = pW * pH * pD * layers;
        int frameCount = options.OnlyFirstFrame ? 1 : Math.Max(1, info.SpriteId.Count / perFrame);
        int direction = Math.Clamp(options.Direction, 0, pW - 1);
        int mountedOffset = options.Mounted && pD > 1 ? pW * pH * layers : 0;

        RgbaImage Sprite(int index)
        {
            if (index < 0 || index >= info.SpriteId.Count)
                throw new OutfitRenderException($"Outfit {outfitId}: sprite index {index} is outside the frame group.");
            uint id = info.SpriteId[index];
            return sprites.GetSprite(id) ?? throw new OutfitRenderException($"Sprite {id} (outfit {outfitId}) could not be loaded.");
        }

        RgbaImage Colored(int index) =>
            layers > 1 ? ApplyTemplate(Sprite(index), Sprite(index + 1), options.Colors) : Sprite(index);

        var frames = new List<RgbaImage>(frameCount);
        for (int f = 0; f < frameCount; f++)
        {
            int baseIndex = direction * layers + f * perFrame + mountedOffset;
            var frame = Colored(baseIndex);
            if ((options.Addons & 1) == 1 && pH > 1)
                frame = MagickOver(Colored(baseIndex + pW * layers), frame);
            if (options.Addons > 1 && pH > 2)
                frame = MagickOver(Colored(baseIndex + pW * layers * 2), frame);
            frames.Add(frame);
        }

        if (options.MountId != 0)
        {
            var mountFrames = Render(db, sprites, options.MountId, new OutfitRenderOptions
            {
                FrameGroup = options.FrameGroup,
                Direction = options.Direction,
                Colors = options.MountColors ?? GameTables.DefaultOutfitColors,
            });
            return ComposeMounted(frames, mountFrames);
        }
        return frames;
    }

    /// <summary>Colors a base sprite with its template (Multiply + CopyAlpha, see remarks).</summary>
    public static RgbaImage ApplyTemplate(RgbaImage baseSprite, RgbaImage template, IReadOnlyList<int> colors)
    {
        uint head = GameTables.OutfitColor(colors[0]), primary = GameTables.OutfitColor(colors[1]);
        uint secondary = GameTables.OutfitColor(colors[2]), detail = GameTables.OutfitColor(colors[3]);
        var result = new RgbaImage(baseSprite.Width, baseSprite.Height);
        for (int i = 0; i < result.Pixels.Length; i++)
        {
            uint b = baseSprite.Pixels[i];
            uint m = i < template.Pixels.Length ? template.Pixels[i] : 0;
            // Template drawn over white with the color map applied to exact template colors.
            uint mapped = m switch
            {
                TemplateYellow => head,
                TemplateRed => primary,
                TemplateGreen => secondary,
                TemplateBlue => detail,
                _ => m,
            };
            uint mask = RgbaImage.BlendOver(mapped, 0xFFFFFFFF);
            double da = (b >> 24) / 255.0;
            uint r = Multiply((mask >> 16) & 0xFF, (b >> 16) & 0xFF, da);
            uint g = Multiply((mask >> 8) & 0xFF, (b >> 8) & 0xFF, da);
            uint bl = Multiply(mask & 0xFF, b & 0xFF, da);
            result.Pixels[i] = (b & 0xFF000000) | r << 16 | g << 8 | bl;
        }
        return result;
    }

    /// <summary>ImageMagick Multiply with an opaque source: Sc·(Dc·Da + 1 − Da), quantised to Q16 then 8 bits.</summary>
    private static uint Multiply(uint source8, uint dest8, double destAlpha)
    {
        double sc = source8 / 255.0, dc = dest8 / 255.0;
        double v = sc * (dc * destAlpha + 1 - destAlpha);
        return QuantumTo8(v);
    }

    /// <summary>
    /// Reproduces the original pipeline exactly: ImageMagick Q16 <c>ClampToQuantum</c> (value + 0.5,
    /// truncated), the frame written as a 16-bit PNG, then GDI+ reducing 16 → 8 bits by truncation
    /// (q·255/65535). Verified pixel-exact against frames produced by the original binary.
    /// </summary>
    private static uint QuantumTo8(double value)
    {
        double scaled = 65535.0 * value;
        uint q = scaled <= 0 ? 0 : scaled >= 65535 ? 65535u : (uint)(scaled + 0.5);
        return q * 255 / 65535;
    }

    /// <summary>ImageMagick "Over" (used by Mosaic) of <paramref name="src"/> onto <paramref name="dst"/>.</summary>
    public static RgbaImage MagickOver(RgbaImage src, RgbaImage dst)
    {
        int w = Math.Max(src.Width, dst.Width), h = Math.Max(src.Height, dst.Height);
        var result = new RgbaImage(w, h);
        result.CopyImage(dst, 0, 0);
        for (int y = 0; y < src.Height; y++)
        {
            for (int x = 0; x < src.Width; x++)
            {
                uint s = src[x, y];
                uint d = result[x, y];
                double sa = (s >> 24) / 255.0, da = (d >> 24) / 255.0;
                if (sa == 0) continue;
                double alpha = sa + da - sa * da;
                double gamma = alpha <= 0 ? 0 : 1.0 / alpha;
                uint Channel(int shift)
                {
                    double sc = ((s >> shift) & 0xFF) / 255.0, dc = ((d >> shift) & 0xFF) / 255.0;
                    return QuantumTo8(gamma * (sc * sa + dc * da * (1 - sa)));
                }
                result[x, y] = QuantumTo8(alpha) << 24 | Channel(16) << 16 | Channel(8) << 8 | Channel(0);
            }
        }
        return result;
    }

    /// <summary>
    /// Places mount and rider on a 64×64 canvas, repeating the shorter animation so both loop
    /// together (least common multiple of the frame counts), as the original did.
    /// </summary>
    public static List<RgbaImage> ComposeMounted(IReadOnlyList<RgbaImage> rider, IReadOnlyList<RgbaImage> mount)
    {
        if (mount.Count == 0) return rider.ToList();
        int lcm = Lcm(rider.Count, mount.Count);
        var result = new List<RgbaImage>(lcm);
        for (int i = 0; i < lcm; i++)
        {
            var m = mount[i % mount.Count];
            var r = rider[i % rider.Count];
            var canvas = new RgbaImage(64, 64);
            canvas.DrawImage(m, 64 - m.Width, 64 - m.Width);
            canvas.DrawImage(r, 64 - r.Width, 64 - r.Width);
            result.Add(canvas);
        }
        return result;
    }

    public static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }

    public static int Lcm(int a, int b) => a / Gcd(a, b) * b;

    /// <summary>
    /// Delays used by the Outfitter preview/GIF (original <c>UpdateOutfiter</c>): 100 ms for the
    /// 8-frame walking cycle whose first phase is 300 ms, 300 ms without animation, otherwise the
    /// average of each phase's min/max. Fixed a NullReferenceException for 8-frame outfits
    /// without animation data.
    /// </summary>
    public static List<int> OutfitterDelays(Appearance outfit, int frameGroup, int frameCount)
    {
        int fg = Math.Clamp(frameGroup, 0, outfit.FrameGroup.Count - 1);
        var animation = outfit.FrameGroup.Count == 0 ? null : outfit.FrameGroup[fg].SpriteInfo?.Animation;
        var delays = new List<int>();
        for (int i = 0; i < frameCount; i++)
        {
            if (animation == null || i >= animation.SpritePhase.Count) delays.Add(animation == null ? 300 : 100);
            else if (frameCount == 8 && animation.SpritePhase[0].DurationMin == 300) delays.Add(100);
            else delays.Add((int)((animation.SpritePhase[i].DurationMin + animation.SpritePhase[i].DurationMax) / 2));
        }
        return delays;
    }
}
