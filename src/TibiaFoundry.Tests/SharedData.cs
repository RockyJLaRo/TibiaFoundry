using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.Tests;

/// <summary>Lazily loaded client data shared by integration tests (parsing appearances takes ~1 s).</summary>
internal static class SharedData
{
    private static readonly Lazy<(AppearanceDatabase Db, SpriteRepository Sprites)?> Loaded = new(() =>
    {
        if (!TestData.Client.AssetsExist) return null;
        var catalog = AssetCatalog.Load(TestData.Client.AssetsDirectory);
        var db = AppearanceDatabase.Load(TestData.Client.FindAssetFile(AssetTypes.Appearances)!);
        return (db, new SpriteRepository(catalog, null, SpriteSourceMode.ClientOnly));
    });

    public static AppearanceDatabase Appearances
    {
        get
        {
            Assert.SkipWhen(Loaded.Value == null, "The Tibia client is not installed on this machine.");
            return Loaded.Value!.Value.Db;
        }
    }

    public static SpriteRepository Sprites
    {
        get
        {
            Assert.SkipWhen(Loaded.Value == null, "The Tibia client is not installed on this machine.");
            return Loaded.Value!.Value.Sprites;
        }
    }
}
