using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;

namespace TibiaFoundry.Tests;

public class ProtoLoadingTests(ITestOutputHelper output)
{
    [Fact]
    public void Appearances_Parse()
    {
        TestData.RequireClientAssets();
        string path = TestData.Client.FindAssetFile(AssetTypes.Appearances)!;
        try
        {
            var db = AppearanceDatabase.Load(path);
            output.WriteLine($"objects {db.Count(AppearanceKind.Object)}, outfits {db.Count(AppearanceKind.Outfit)}, effects {db.Count(AppearanceKind.Effect)}, missiles {db.Count(AppearanceKind.Missile)}");
            Assert.True(db.Count(AppearanceKind.Object) > 1000);
        }
        catch (Exception ex)
        {
            output.WriteLine(ex.ToString());
            throw;
        }
    }
}
