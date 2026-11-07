using TibiaFoundry.Core.Appearances;

namespace TibiaFoundry.Core.Localization;

/// <summary>Translated names of enum values shown in the interface; the key is built from the identifier, never from text.</summary>
public static class EnumText
{
    /// <summary>"Object", "Outfit", "Effect" or "Missile" in the active language.</summary>
    public static string Display(this AppearanceKind kind) => L.TOr("AppearanceKind." + kind, kind.ToString());
}
