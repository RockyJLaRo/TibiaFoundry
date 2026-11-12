using System.Security.Cryptography;
using Google.Protobuf;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.Editing.Model;
using TibiaFoundry.Core.Imaging;

namespace TibiaFoundry.Tests;

/// <summary>
/// Builds a small but complete client assets folder (catalog, appearances, LZMA sprite sheets of
/// all four sizes) in a temporary folder, so the converter and the Assets Editor can be tested
/// end to end without depending on the installed client.
/// </summary>
internal static class SyntheticAssets
{
    /// <summary>A recognizable, partly transparent image for sprite <paramref name="id"/>.</summary>
    public static RgbaImage Sprite(int id, int width, int height)
    {
        var image = new RgbaImage(width, height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            bool visible = (x + y + id) % 5 != 0;
            byte a = (x + id) % 7 == 0 ? (byte)128 : (byte)255;
            image[x, y] = visible ? RgbaImage.Argb(a, (byte)(id * 13 + x), (byte)(id * 7 + y), (byte)(x * y + id)) : 0x00FF00FFu;
        }
        return image;
    }

    /// <summary>Writes the folder and returns it. Sheets: 0–9 (32×32), 10–13 (32×64), 14–17 (64×32), 18–21 (64×64).</summary>
    /// <param name="customize">Changes to the appearances before they are written.</param>
    public static string Create(string folder, Action<EditableAppearances>? customize = null)
    {
        Directory.CreateDirectory(folder);
        var entries = new List<string>();
        var sheets = new (SpriteSheetType Type, int First, int Last)[]
        {
            (SpriteSheetType.Small, 0, 9), (SpriteSheetType.Tall, 10, 13), (SpriteSheetType.Wide, 14, 17), (SpriteSheetType.Large, 18, 21),
        };
        var appearances = BuildAppearances();
        customize?.Invoke(appearances);
        byte[] appearanceBytes = appearances.ToByteArray();
        string appearancesName = $"appearances-{Convert.ToHexStringLower(SHA256.HashData(appearanceBytes))}.dat";
        File.WriteAllBytes(Path.Combine(folder, appearancesName), appearanceBytes);
        entries.Add($"{{\r\n  \"type\":\"appearances\",\r\n  \"file\":\"{appearancesName}\"\r\n}}");
        foreach (var (type, first, last) in sheets)
        {
            var (w, h) = Core.Compatibility.TibiaFormat.SpriteTypes[(int)type];
            var sheet = new RgbaImage(384, 384);
            sheet.Clear(0x00FF00FF);
            int columns = 384 / w;
            for (int id = first; id <= last; id++)
            {
                int i = id - first;
                sheet.CopyImage(Sprite(id, w, h), i % columns * w, i / columns * h);
            }
            byte[] bmp = BmpEncoder.Encode(sheet);
            string name = $"sprites-{Convert.ToHexStringLower(SHA256.HashData(bmp))}.bmp.lzma";
            File.WriteAllBytes(Path.Combine(folder, name), CipLzma.Compress(bmp));
            entries.Add($"{{\r\n  \"type\":\"sprite\",\r\n  \"file\":\"{name}\",\r\n  \"spritetype\":{(int)type},\r\n  \"firstspriteid\":{first},\r\n  \"lastspriteid\":{last},\r\n  \"area\":0\r\n}}");
        }
        File.WriteAllText(Path.Combine(folder, "catalog-content.json"), "[\r\n" + string.Join(", \r\n", entries) + "\r\n]\r\n");
        return folder;
    }

    private static SpriteInfo Info(uint layers, uint px, uint py, uint pz, params uint[] ids)
    {
        var info = new SpriteInfo { Layers = layers, PatternWidth = px, PatternHeight = py, PatternDepth = pz };
        info.SpriteId.AddRange(ids);
        return info;
    }

    /// <summary>
    /// Objects 100 (ground, 32×32), 101 (64×64, animated: ping-pong), 103 (market item with a long
    /// Latin-1 name), outfit 1 (idle + moving, 64×64 with a color layer), effect 1 (32×64), missile 1 (64×32).
    /// </summary>
    public static EditableAppearances BuildAppearances()
    {
        var all = new EditableAppearances();
        var ground = new EditableAppearance { Id = 100, Flags = new AppearanceFlags { Bank = new FlagBank { Waypoints = 150 }, Fullbank = true } };
        ground.FrameGroup.Add(new FrameGroup { FixedFrameGroup = 2, SpriteInfo = Info(1, 1, 1, 1, 0) });
        all.Object.Add(ground);

        var animated = new EditableAppearance { Id = 101, Flags = new AppearanceFlags { AnimateAlways = true, Unpass = true, Corpse = true, Light = new FlagLight { Brightness = 3, Color = 215 } } };
        var animatedInfo = Info(1, 1, 1, 1, 18, 19, 20);
        animatedInfo.BoundingSquare = 60;
        animatedInfo.Animation = new SpriteAnimation { LoopType = -1, Synchronized = true };
        animatedInfo.Animation.SpritePhase.Add(new SpritePhase { DurationMin = 100, DurationMax = 100 });
        animatedInfo.Animation.SpritePhase.Add(new SpritePhase { DurationMin = 150, DurationMax = 200 });
        animatedInfo.Animation.SpritePhase.Add(new SpritePhase { DurationMin = 100, DurationMax = 300 });
        animated.FrameGroup.Add(new FrameGroup { FixedFrameGroup = 2, SpriteInfo = animatedInfo });
        all.Object.Add(animated);

        var item = new EditableAppearance
        {
            Id = 103,
            Name = ByteString.CopyFrom(System.Text.Encoding.Latin1.GetBytes("djinn maître recipe book of the long names")),
            Flags = new AppearanceFlags { Take = true, Cumulative = true, Market = new FlagMarket { Category = 9, TradeAsObjectId = 103, ShowAsObjectId = 103 }, MinimumLevel = 8 },
        };
        item.Flags.RestrictToVocation.Add(1);
        item.Flags.RestrictToVocation.Add(4);
        item.FrameGroup.Add(new FrameGroup { FixedFrameGroup = 2, SpriteInfo = Info(1, 2, 1, 1, 1, 2) });
        all.Object.Add(item);

        var outfit = new EditableAppearance { Id = 1, Flags = new AppearanceFlags() };
        outfit.FrameGroup.Add(new FrameGroup { FixedFrameGroup = 0, SpriteInfo = Info(2, 1, 1, 1, 18, 21) });
        var moving = Info(2, 1, 1, 1, 18, 21, 19, 21);
        moving.Animation = new SpriteAnimation { LoopType = 1, LoopCount = 4 };
        moving.Animation.SpritePhase.Add(new SpritePhase { DurationMin = 50, DurationMax = 50 });
        moving.Animation.SpritePhase.Add(new SpritePhase { DurationMin = 60, DurationMax = 60 });
        outfit.FrameGroup.Add(new FrameGroup { FixedFrameGroup = 1, SpriteInfo = moving });
        all.Outfit.Add(outfit);

        var effect = new EditableAppearance { Id = 1, Flags = new AppearanceFlags() };
        effect.FrameGroup.Add(new FrameGroup { FixedFrameGroup = 2, SpriteInfo = Info(1, 1, 1, 1, 10) });
        all.Effect.Add(effect);
        var missile = new EditableAppearance { Id = 1, Flags = new AppearanceFlags() };
        missile.FrameGroup.Add(new FrameGroup { FixedFrameGroup = 2, SpriteInfo = Info(1, 1, 1, 1, 14) });
        all.Missile.Add(missile);
        return all;
    }
}
