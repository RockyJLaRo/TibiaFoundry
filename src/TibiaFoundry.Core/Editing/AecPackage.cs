using TibiaFoundry.Core.Common;
using System.Runtime.InteropServices;
using Google.Protobuf;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Editing.Model;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Editing;

/// <summary>Result of importing an object package.</summary>
public sealed class AecImportResult
{
    public List<(AppearanceKind Kind, uint OriginalId, uint NewId)> Imported { get; } = [];
    public int SpritesAdded { get; set; }
    public int SpritesReused { get; set; }
    public List<string> Problems { get; } = [];
}

/// <summary>
/// Object packages (.aec), the exchange format of Arch-Mina's Assets Editor: an appearances message
/// whose objects carry their sprite images (PNG) in field 7, one per sprite reference in order,
/// with the sprite IDs renumbered from 0. The layout follows that program's source; exchanging
/// packages with it has not been tested (it was not run). Field 7 is never written into the
/// client's appearances file.
/// </summary>
public static class AecPackage
{
    public const string Extension = ".aec";
    private const int SpriteDataField = 7;

    /// <summary>Builds a package from objects of a project.</summary>
    public static byte[] Export(AssetsProject project, IEnumerable<(AppearanceKind Kind, uint Id)> objects)
    {
        using var ms = new MemoryStream();
        var output = new CodedOutputStream(ms);
        uint counter = 0;
        var attribution = Attribution.ForNewAsset();
        foreach (var (kind, id) in objects.OrderBy(o => o.Kind))
        {
            var appearance = (project.Find(kind, id) ?? throw new ArgumentException($"{kind} {id} does not exist.")).Clone();
            var images = new List<byte[]>();
            foreach (var g in appearance.FrameGroup.Where(g => g.SpriteInfo != null))
            {
                for (int i = 0; i < g.SpriteInfo.SpriteId.Count; i++)
                {
                    var sprite = project.GetSprite(g.SpriteInfo.SpriteId[i]) ?? throw new InvalidDataException($"{kind} {id}: sprite {g.SpriteInfo.SpriteId[i]} cannot be read.");
                    images.Add(ImageIO.EncodePng(sprite, attribution));
                    g.SpriteInfo.SpriteId[i] = counter++;
                }
            }
            using var item = new MemoryStream();
            var itemOutput = new CodedOutputStream(item);
            appearance.WriteTo(itemOutput);
            foreach (var png in images)
            {
                itemOutput.WriteTag(SpriteDataField, WireFormat.WireType.LengthDelimited);
                itemOutput.WriteBytes(ByteString.CopyFrom(png));
            }
            itemOutput.Flush();
            output.WriteTag(FieldOf(kind), WireFormat.WireType.LengthDelimited);
            output.WriteBytes(ByteString.CopyFrom(item.ToArray()));
        }
        output.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Adds the objects of a package to a project. Their sprites become new sprites (identical
    /// images are stored once). With <paramref name="keepIds"/>, an object keeps its ID when it is
    /// free; otherwise it gets the next free ID.
    /// </summary>
    public static AecImportResult Import(AssetsProject project, byte[] data, bool keepIds)
    {
        var result = new AecImportResult();
        var input = new CodedInputStream(data);
        var known = new Dictionary<string, uint>();
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            int field = WireFormat.GetTagFieldNumber(tag);
            if (field is < 1 or > 4 || WireFormat.GetTagWireType(tag) != WireFormat.WireType.LengthDelimited)
            {
                input.SkipLastField();
                continue;
            }
            var kind = (AppearanceKind)(field - 1);
            var (appearance, images) = Split(input.ReadBytes().ToByteArray());
            uint originalId = appearance.Id;
            int references = appearance.SpriteIds().Count();
            if (images.Count < references)
            {
                result.Problems.Add(L.F("AecPackage.01ThePackageHas2", kind, originalId, L.Count(images.Count, "Noun.sprite_image"), L.Count(references, "Noun.sprite_reference")));
                continue;
            }
            try
            {
                int next = 0;
                foreach (var g in appearance.FrameGroup.Where(g => g.SpriteInfo != null))
                {
                    for (int i = 0; i < g.SpriteInfo.SpriteId.Count; i++)
                    {
                        byte[] png = images[next++];
                        string hash = FileHashing.HashBytes(png);
                        if (!known.TryGetValue(hash, out uint spriteId))
                        {
                            spriteId = project.AddSprite(ImageIO.LoadFromBytes(png));
                            known[hash] = spriteId;
                            result.SpritesAdded++;
                        }
                        else
                        {
                            result.SpritesReused++;
                        }
                        g.SpriteInfo.SpriteId[i] = spriteId;
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or ExternalException)
            {
                result.Problems.Add($"{kind} {originalId}: {ex.Message}");
                continue;
            }
            uint newId = keepIds && originalId >= AssetsProject.FirstId(kind) && project.Find(kind, originalId) == null ? originalId : project.NextFreeId(kind);
            appearance.Id = newId;
            project.Appearances.List(kind).Add(appearance);
            var list = project.Appearances.List(kind);
            var sorted = list.OrderBy(a => a.Id).ToList();
            list.Clear();
            list.AddRange(sorted);
            project.MarkModified(kind, newId);
            result.Imported.Add((kind, originalId, newId));
        }
        return result;
    }

    private static int FieldOf(AppearanceKind kind) => (int)kind + 1;

    /// <summary>Separates the sprite images (field 7) from an appearance, which is parsed without them.</summary>
    private static (EditableAppearance Appearance, List<byte[]> Images) Split(byte[] data)
    {
        var images = new List<byte[]>();
        var input = new CodedInputStream(data);
        using var rest = new MemoryStream(data.Length);
        var output = new CodedOutputStream(rest);
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == SpriteDataField && WireFormat.GetTagWireType(tag) == WireFormat.WireType.LengthDelimited)
            {
                images.Add(input.ReadBytes().ToByteArray());
                continue;
            }
            output.WriteTag(tag);
            switch (WireFormat.GetTagWireType(tag))
            {
                case WireFormat.WireType.Varint: output.WriteUInt64(input.ReadUInt64()); break;
                case WireFormat.WireType.Fixed32: output.WriteFixed32(input.ReadFixed32()); break;
                case WireFormat.WireType.Fixed64: output.WriteFixed64(input.ReadFixed64()); break;
                case WireFormat.WireType.LengthDelimited: output.WriteBytes(input.ReadBytes()); break;
                default: throw new InvalidDataException("The package contains an unsupported field type.");
            }
        }
        output.Flush();
        return (EditableAppearance.Parser.ParseFrom(rest.ToArray()), images);
    }
}
