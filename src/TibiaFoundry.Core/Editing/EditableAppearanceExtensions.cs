using System.Text;
using Google.Protobuf;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Editing.Model;
using TibiaFoundry.Core.Protobuf;

namespace TibiaFoundry.Core.Editing;

/// <summary>Helpers for the editable appearance messages.</summary>
public static class EditableAppearanceExtensions
{
    /// <summary>The client stores text as Latin-1 (ISO-8859-1).</summary>
    public static readonly Encoding ClientText = Encoding.Latin1;

    public static string NameText(this EditableAppearance a) => a.HasName ? ClientText.GetString(a.Name.Span) : "";
    public static string DescriptionText(this EditableAppearance a) => a.HasDescription ? ClientText.GetString(a.Description.Span) : "";

    /// <summary>
    /// Encodes text for the client. Returns null when it contains characters Latin-1 cannot hold
    /// (the client would show them wrongly).
    /// </summary>
    public static ByteString? EncodeClientText(string text)
    {
        foreach (char c in text)
            if (c > 0xFF) return null;
        return ByteString.CopyFrom(ClientText.GetBytes(text));
    }

    public static Google.Protobuf.Collections.RepeatedField<EditableAppearance> List(this EditableAppearances all, AppearanceKind kind) => kind switch
    {
        AppearanceKind.Outfit => all.Outfit,
        AppearanceKind.Effect => all.Effect,
        AppearanceKind.Missile => all.Missile,
        _ => all.Object,
    };

    /// <summary>
    /// The same appearance as a <c>Tibia.Protobuf.Appearances.Appearance</c>, the type the renderers
    /// and exporters of the viewer use. Latin-1 text is converted to UTF-8 on the way.
    /// </summary>
    public static Tibia.Protobuf.Appearances.Appearance ToViewerAppearance(this EditableAppearance a)
    {
        byte[] bytes = a.ToByteArray();
        try
        {
            return Tibia.Protobuf.Appearances.Appearance.Parser.ParseFrom(bytes);
        }
        catch (InvalidProtocolBufferException)
        {
            return Tibia.Protobuf.Appearances.Appearance.Parser.ParseFrom(Utf8Repair.Repair(Tibia.Protobuf.Appearances.Appearance.Descriptor, bytes, out _));
        }
    }

    /// <summary>All sprite IDs an appearance uses, in order.</summary>
    public static IEnumerable<uint> SpriteIds(this EditableAppearance a) =>
        a.FrameGroup.Where(g => g.SpriteInfo != null).SelectMany(g => g.SpriteInfo.SpriteId);

    /// <summary>First sprite of the first frame group (the thumbnail), or null.</summary>
    public static uint? PreviewSprite(this EditableAppearance a)
    {
        foreach (var g in a.FrameGroup)
            if (g.SpriteInfo?.SpriteId.Count > 0) return g.SpriteInfo.SpriteId[0];
        return null;
    }
}
