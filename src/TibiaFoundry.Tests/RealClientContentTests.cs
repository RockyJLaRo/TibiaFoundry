using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Library;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.StaticData;

namespace TibiaFoundry.Tests;

/// <summary>
/// The content workflows against the installed Tibia client (8.8): every built-in project template
/// that renders from the client runs end to end and writes real images. Skipped when no client is
/// installed. Output goes to a temporary folder.
/// </summary>
public sealed class RealClientContentTests
{
    private sealed record Game(AppearanceDatabase? Appearances, ISpriteSource? Sprites, StaticDataDatabase? StaticData, string? ClientVersion) : IGameAssets;

    private static Game LoadClient()
    {
        string assets = TestData.RequireClientAssets();
        var catalog = AssetCatalog.Load(assets);
        string File(string type) => Path.Combine(assets, catalog.Files[type]);
        return new Game(AppearanceDatabase.Load(File(AssetTypes.Appearances)), new SpriteRepository(catalog, null, SpriteSourceMode.ClientOnly),
            StaticDataDatabase.Load(File(AssetTypes.StaticData)), TestData.Client.ReadClientVersion());
    }

    [Fact]
    public void TheProjectTemplatesRenderFromTheInstalledClient()
    {
        var game = LoadClient();
        string dir = TestData.NewTempDirectory("real-content");
        var lib = AssetLibrary.Open(Path.Combine(dir, "Library"));
        var creatures = game.StaticData!.Monsters.Where(c => c.Outfit is { OutfitId: > 0 }).Take(5).Select(c => c.Name).ToList();
        var outfitIds = game.Appearances!.List(AppearanceKind.Outfit).Take(2).Select(o => o.Id).ToList();

        var gallery = ContentProjects.Create(ContentProjectType.CreatureGallery, "Real creatures");
        gallery.Workflow.Steps[0].Parameters["names"] = string.Join("\n", creatures);
        var outfits = ContentProjects.Create(ContentProjectType.OutfitGallery, "Real outfits");
        outfits.Workflow.Steps[0].Parameters["ids"] = string.Join(", ", outfitIds);
        var items = ContentProjects.Create(ContentProjectType.ItemCollection, "Real items");
        items.Workflow.Steps[0].Parameters["names"] = "gold coin\nplatinum coin";

        foreach (var project in new[] { gallery, outfits, items })
        {
            var context = new WorkflowContext
            {
                OutputFolder = Path.Combine(dir, "out"), RunName = project.Name, Game = game, Library = lib, Creator = "Tester",
                Profile = ExportProfiles.Find(lib.ProfilesFolder, project.ProfileId),
            };
            var result = WorkflowRunner.Run(project.Workflow, context, ct: TestContext.Current.CancellationToken);
            Assert.True(result.Items.Count > 0, $"{project.Name}: {string.Join(" ", result.Problems)}");
            Assert.Empty(result.Problems);
            foreach (var item in result.Items)
            {
                string file = Path.Combine(result.OutputFolder, item.File);
                Assert.True(File.Exists(file), file);
                Assert.Equal("Tester", AttributionReader.Read(file)!.Creators.Single());
            }
            Assert.True(File.Exists(Path.Combine(result.OutputFolder, "manifest.json")));
        }
        Assert.Equal(5, lib.CollectionNamed("Real creatures")!.Count);
        Assert.True(lib.Statistics().Assets >= 5 + 8);    // creatures and four addon variants of two outfits, at least
    }
}
