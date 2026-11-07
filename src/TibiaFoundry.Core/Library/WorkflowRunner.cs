using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Sprites;
using TibiaFoundry.Core.StaticData;
using AppearanceKind = TibiaFoundry.Core.Appearances.AppearanceKind;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Library;

/// <summary>The client data a workflow renders from (the client in use, supplied by the application).</summary>
public interface IGameAssets
{
    AppearanceDatabase? Appearances { get; }
    ISpriteSource? Sprites { get; }
    StaticDataDatabase? StaticData { get; }
    string? ClientVersion { get; }
}

/// <summary>Where the files of a run go.</summary>
public enum OutputMode
{
    /// <summary>A new dated folder per run; earlier runs are never touched (default).</summary>
    NewFolderPerRun,
    /// <summary>One folder per project; files with the same name are replaced, other files stay.</summary>
    UpdateInPlace,
}

/// <summary>Everything a run needs besides its steps.</summary>
public sealed record WorkflowContext
{
    public required string OutputFolder { get; init; }
    /// <summary>The project or collection name ({collection} in templates, the run folder's name).</summary>
    public required string RunName { get; init; }
    public ExportProfile Profile { get; init; } = ExportProfiles.TibiaWiki;
    public OutputMode Mode { get; init; } = OutputMode.NewFolderPerRun;
    public AssetLibrary? Library { get; init; }
    public IGameAssets? Game { get; init; }
    /// <summary>The creator name written into files and credits (the configured one when null).</summary>
    public string? Creator { get; init; }
    /// <summary>Reads an older appearances file (for "New and changed since a version").</summary>
    public Func<string, AppearanceDatabase>? LoadAppearances { get; init; }
}

/// <summary>What a run produced.</summary>
public sealed record WorkflowRunResult(string OutputFolder, IReadOnlyList<PublishedItem> Items, IReadOnlyList<string> ExtraFiles, IReadOnlyList<string> Problems, TimeSpan Duration)
{
    public int Files => Items.Count + ExtraFiles.Count;

    public string Summary =>
        L.F("WorkflowRunner.0And1WrittenIn2", L.Count(Items.Count, "Noun.image"), L.Count(ExtraFiles.Count, "Noun.other_file"), Duration.TotalSeconds) +
        (Problems.Count > 0 ? $"; {L.Count(Problems.Count, "Noun.problem")}" : "") + ".";
}

/// <summary>
/// Runs a <see cref="WorkflowDefinition"/> (8.8): the source steps produce images one at a time, each
/// goes through the processing steps and is written, then the output steps make sheets, manifests,
/// galleries, snippets and archives from what was written.
/// </summary>
/// <remarks>
/// <para>Everything is written into a hidden staging folder inside the output folder and moved into
/// place only after the whole run succeeded, so a cancelled or failed run leaves no partial export.
/// Images are processed one at a time (memory stays flat for thousands of images); one image that
/// cannot be made is reported and skipped, the others are still written.</para>
/// <para>Originals are never changed: library and folder images are read, and the results are new files.
/// Every PNG and GIF carries the creator attribution; images made from the client name the user as
/// creator and CipSoft as the owner of the artwork, and images made from attributed files keep their
/// creators.</para>
/// </remarks>
public static class WorkflowRunner
{
    /// <summary>One image on its way through the steps.</summary>
    private sealed class WorkItem
    {
        public required string Name { get; set; }
        public required string Category { get; set; }
        public string? Id { get; set; }
        public string? Version { get; set; }
        public string Animation { get; set; } = "Still";
        public string? Direction { get; set; }
        public string? Group { get; set; }
        public string? Description { get; set; }
        public string? Source { get; set; }
        public List<string> Tags { get; } = [];
        public required List<RgbaImage> Frames { get; set; }
        public required List<int> Delays { get; set; }
        public string? Format { get; set; }
        public string? FileName { get; set; }
        public string? Folder { get; set; }
        public int Scale { get; set; } = 1;
        public FoundAttribution? Original { get; set; }
        public long? LibraryAssetId { get; set; }
    }

    private sealed record Producer(string Label, Func<WorkItem?> Make);

    public static WorkflowRunResult Run(WorkflowDefinition workflow, WorkflowContext context, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        var problems = workflow.Validate();
        if (problems.Count > 0) throw new InvalidOperationException(string.Join(" ", problems));
        var steps = workflow.Steps.Where(s => s.Enabled).ToList();
        CheckRequirements(steps, context);
        var profile = ExportProfiles.Normalize(context.Profile);
        string creator = Attribution.NormalizeCreator(context.Creator) ?? Attribution.CurrentCreator;

        progress?.Report(new OperationProgress(0, 0, L.T("WorkflowRunner.CollectingTheImages")));
        var producers = new List<Producer>();
        var issues = new List<string>();
        foreach (var s in steps.Where(s => s.Definition.Group == StepGroup.Source)) producers.AddRange(Sources(s, context, issues));
        if (producers.Count == 0) throw new InvalidOperationException("The source steps found no images (check the IDs, names, collection or folder).");

        Directory.CreateDirectory(context.OutputFolder);
        string staging = Path.Combine(context.OutputFolder, $".tf-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var written = new List<PublishedItem>();
            var origins = new List<long?>();
            var process = steps.Where(s => s.Definition.Group == StepGroup.Process).ToList();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < producers.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new OperationProgress(i, producers.Count, producers[i].Label));
                try
                {
                    var item = producers[i].Make();
                    if (item == null) continue;
                    foreach (var step in process) Apply(step, item, context, profile, i + 1, creator);
                    Finish(item, process, profile, context, i + 1, creator);
                    var published = Write(item, staging, usedNames, profile, context, creator);
                    written.Add(published);
                    origins.Add(item.LibraryAssetId);
                }
                catch (Exception ex) when (ex is InvalidOperationException or OutfitRenderException or IOException or ArgumentException or InvalidDataException or ImageMagick.MagickException)
                {
                    issues.Add($"{producers[i].Label}: {ex.Message}");
                }
            }
            if (written.Count == 0) throw new InvalidOperationException("No image could be made." + (issues.Count > 0 ? " " + string.Join(" ", issues.Take(5)) : ""));

            var extra = new List<string>();
            var outputs = steps.Where(s => s.Definition.Group == StepGroup.Output && s.Type is not (WorkflowStepType.Zip or WorkflowStepType.AddToLibrary)).ToList();
            // The profile's own outputs, unless a step asks for them already.
            if (!outputs.Any(s => s.Type == WorkflowStepType.Manifest) && (profile.WriteJsonManifest || profile.WriteCsvManifest || profile.WriteMarkdownIndex))
                outputs.Add(new WorkflowStep(WorkflowStepType.Manifest, ("json", Text(profile.WriteJsonManifest)), ("csv", Text(profile.WriteCsvManifest)), ("markdown", Text(profile.WriteMarkdownIndex))));
            if (!outputs.Any(s => s.Type == WorkflowStepType.Gallery) && profile.WriteGallery) outputs.Add(new WorkflowStep(WorkflowStepType.Gallery));
            if (!outputs.Any(s => s.Type == WorkflowStepType.Documentation) && profile.DocumentationTemplate.Length > 0) outputs.Add(new WorkflowStep(WorkflowStepType.Documentation));
            foreach (var step in outputs)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new OperationProgress(producers.Count, producers.Count, step.Definition.Title));
                extra.AddRange(Output(step, written, staging, context, profile, creator, issues));
            }
            foreach (var zip in steps.Where(s => s.Type == WorkflowStepType.Zip))
            {
                ct.ThrowIfCancellationRequested();
                extra.Add(MakeZip(zip, staging, context));
            }

            ct.ThrowIfCancellationRequested();
            string final = Commit(staging, context);
            var result = new WorkflowRunResult(final, written, extra, issues, DateTime.UtcNow - started);
            foreach (var add in steps.Where(s => s.Type == WorkflowStepType.AddToLibrary)) AddToLibrary(add, result, origins, context, issues);
            return result with { Problems = issues };
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static string Text(bool b) => b ? "true" : "false";

    private static void CheckRequirements(List<WorkflowStep> steps, WorkflowContext context)
    {
        foreach (var s in steps)
        {
            var d = s.Definition;
            if (d.NeedsClient && (context.Game?.Appearances == null || context.Game.Sprites == null))
                throw new InvalidOperationException($"\"{d.Title}\" needs the Tibia client's appearances and sprites. Set a client on the Clients page (or in Settings → Tibia client) first.");
            if (d.NeedsStaticData && context.Game?.StaticData == null)
                throw new InvalidOperationException($"\"{d.Title}\" needs the client's static data (creatures). Open Static Data once, or check that the client has a staticdata file.");
            if (d.Type is WorkflowStepType.SelectLibraryAssets or WorkflowStepType.AddToLibrary && context.Library == null)
                throw new InvalidOperationException($"\"{d.Title}\" needs the Asset Library, which is not available.");
        }
    }

    // ------------------------------------------------------------------ sources

    private static IEnumerable<Producer> Sources(WorkflowStep step, WorkflowContext context, List<string> problems)
    {
        var game = context.Game;
        switch (step.Type)
        {
            case WorkflowStepType.SelectLibraryAssets:
            {
                var lib = context.Library!;
                var ids = new List<long>();
                string col = step.Get("collection").Trim();
                if (col.Length > 0)
                {
                    var c = lib.CollectionNamed(TemplateText.Expand(col, new Dictionary<string, string?> { ["collection"] = context.RunName }))
                            ?? throw new InvalidOperationException($"The collection \"{col}\" does not exist.");
                    ids.AddRange(lib.CollectionItems(c.Id));
                }
                foreach (string part in step.Get("assetIds").Split([',', ' ', ';', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    if (long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out long id)) ids.Add(id);
                string query = step.Get("query").Trim(), category = step.Get("category").Trim();
                if (query.Length > 0 || (category.Length > 0 && ids.Count == 0 && col.Length == 0))
                    ids.AddRange(lib.SearchIds(new AssetQuery { Text = query.Length > 0 ? query : null, Category = category.Length > 0 ? category : null, IncludeMissing = false }));
                var assets = lib.Get(ids.Distinct()).Where(a => !a.Missing && a.Kind is MediaKind.Image or MediaKind.Animation);
                if (category.Length > 0) assets = assets.Where(a => string.Equals(a.Category, category, StringComparison.OrdinalIgnoreCase));
                foreach (var a in assets.ToList())
                    yield return new Producer(a.Name, () =>
                    {
                        var (frames, delays) = MediaInspector.LoadFrames(a.Path);
                        var item = new WorkItem
                        {
                            Name = a.Name, Category = a.Category, Id = a.OriginalId, Version = a.ClientVersion, Description = a.Description, Source = a.Path,
                            Frames = frames.ToList(), Delays = delays.ToList(), Animation = frames.Count > 1 ? "Animated" : "Still",
                            Original = AttributionReader.Read(a.Path), LibraryAssetId = a.Id,
                        };
                        item.Tags.AddRange(a.Tags);
                        return item;
                    });
                break;
            }
            case WorkflowStepType.ImportFolder:
            {
                string folder = step.Get("folder");
                if (!Directory.Exists(folder)) throw new InvalidOperationException($"The folder \"{folder}\" does not exist.");
                var files = AssetIndexer.Enumerate(folder, new IndexOptions { Recursive = step.GetBool("recursive"), MaxFiles = 100_000 }, CancellationToken.None, out _)
                    .Where(f => MediaTypes.Images.Contains(Path.GetExtension(f).TrimStart('.').ToLowerInvariant())).ToList();
                foreach (string f in files)
                    yield return new Producer(Path.GetFileName(f), () =>
                    {
                        var (frames, delays) = MediaInspector.LoadFrames(f);
                        var facts = new MediaFacts(Path.GetExtension(f).TrimStart('.'), frames[0].Width, frames[0].Height, frames.Count, 0, default, "", null, null, null, null);
                        return new WorkItem
                        {
                            Name = AssetIndexer.NameFromFile(f), Category = AssetIndexer.InferCategory(f, facts), Id = AssetIndexer.IdFromName(Path.GetFileNameWithoutExtension(f)),
                            Version = AssetIndexer.VersionFromPath(f), Source = f, Frames = frames.ToList(), Delays = delays.ToList(),
                            Animation = frames.Count > 1 ? "Animated" : "Still", Original = AttributionReader.Read(f),
                        };
                    });
                break;
            }
            case WorkflowStepType.RenderCreatures:
            {
                var db = game!.Appearances!;
                var sprites = game.Sprites!;
                var data = game.StaticData!;
                var all = data.Monsters.Concat(step.GetBool("includeBosses") ? data.Bosses : []).GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                var wanted = Lines(step.Get("names"));
                List<Creature> chosen;
                if (wanted.Contains("*")) chosen = all.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
                else
                {
                    var byName = all.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
                    chosen = [];
                    foreach (string n in wanted)
                        if (byName.TryGetValue(n, out var c)) chosen.Add(c);
                        else problems.Add(L.F("WorkflowRunner.Creature0IsNotInThe", n));
                }
                int fg = step.Get("animation") == "Idle" ? 0 : 1;
                bool animated = step.GetBool("animated");
                var directions = step.GetBool("allDirections") ? [0, 1, 2, 3] : new[] { DirectionIndex(step.Get("direction")) };
                foreach (var c in chosen)
                    foreach (int dir in directions)
                    {
                        var creature = c;
                        int d = dir;
                        yield return new Producer(directions.Length > 1 ? $"{c.Name} ({DirectionName(d)})" : c.Name, () =>
                        {
                            var (frames, delays) = RenderCreature(creature, db, sprites, fg, d, animated);
                            return new WorkItem
                            {
                                // Names as articles write them ("orc warlord" → "Orc Warlord").
                                Name = Wiki.NameFormatter.Capitalize(creature.Name), Category = AssetCategories.Creatures, Id = creature.Race.ToString(CultureInfo.InvariantCulture), Version = game.ClientVersion,
                                Animation = fg == 0 ? "Idle" : "Moving", Direction = DirectionName(d), Group = directions.Length > 1 ? Wiki.NameFormatter.Capitalize(creature.Name) : null,
                                Frames = frames, Delays = delays, Source = L.T("WorkflowRunner.ClientStaticData") + (creature.IsBoss ? " (boss)" : ""),
                            };
                        });
                    }
                break;
            }
            case WorkflowStepType.RenderOutfits:
            {
                var db = game!.Appearances!;
                var sprites = game.Sprites!;
                var ids = ParseIds(step.Get("ids"), 5000);
                if (ids.Count == 0) problems.Add(L.T("WorkflowRunner.RenderOutfitsNoOutfitIDsWere"));
                int fg = step.Get("animation") == "Idle" ? 0 : 1;
                bool animated = step.GetBool("animated");
                var colors = ParseColors(step.Get("colors"));
                uint mount = (uint)step.GetInt("mount");
                string addonChoice = step.Get("addons");
                var addonSets = addonChoice switch { "None" => new uint[] { 0 }, "First" => [1], "Second" => [2], "Each variant" => [0, 1, 2, 3], _ => [3] };
                var directions = step.GetBool("allDirections") ? [0, 1, 2, 3] : new[] { DirectionIndex(step.Get("direction")) };
                bool grouped = addonSets.Length > 1 || directions.Length > 1;
                foreach (uint id in ids)
                {
                    var appearance = db.Find(AppearanceKind.Outfit, id);
                    if (appearance == null) { problems.Add(L.F("WorkflowRunner.Outfit0DoesNotExistIn", id)); continue; }
                    string name = string.IsNullOrWhiteSpace(appearance.Name) ? L.F("WorkflowRunner.Outfit0", id) : Wiki.NameFormatter.Capitalize(appearance.Name);
                    foreach (uint addons in addonSets)
                        foreach (int dir in directions)
                        {
                            uint a = addons;
                            int d = dir;
                            string variant = string.Join(", ", new[] { addonSets.Length > 1 ? AddonName(a) : null, directions.Length > 1 ? DirectionName(d) : null }.Where(x => x != null));
                            yield return new Producer(variant.Length > 0 ? $"{name} ({variant})" : name, () =>
                            {
                                var frames = OutfitRenderer.Render(db, sprites, id, new OutfitRenderOptions
                                {
                                    FrameGroup = fg, Direction = d, Colors = colors, Addons = a, MountId = mount, Mounted = mount != 0, OnlyFirstFrame = !animated,
                                });
                                var item = new WorkItem
                                {
                                    Name = variant.Length > 0 ? $"{name} {variant.Replace(",", "")}" : name, Category = mount != 0 ? AssetCategories.Mounts : a != 0 ? AssetCategories.OutfitAddons : AssetCategories.Outfits,
                                    Id = id.ToString(CultureInfo.InvariantCulture), Version = game.ClientVersion, Animation = fg == 0 ? "Idle" : "Moving", Direction = DirectionName(d),
                                    Group = grouped ? name : null, Frames = frames, Delays = Enumerable.Repeat(frames.Count > 2 ? 100 : 300, frames.Count).ToList(), Source = L.T("WorkflowRunner.ClientAppearances"),
                                };
                                item.Tags.Add("addons: " + AddonName(a).ToLowerInvariant());
                                return item;
                            });
                        }
                }
                break;
            }
            case WorkflowStepType.RenderObjects:
            {
                var db = game!.Appearances!;
                var sprites = game.Sprites!;
                var kind = step.Get("kind") switch { "Effect" => AppearanceKind.Effect, "Missile" => AppearanceKind.Missile, _ => AppearanceKind.Object };
                var ids = ParseIds(step.Get("ids"), 100_000).ToList();
                var names = Lines(step.Get("names"));
                if (names.Count > 0)
                    ids.AddRange(db.List(kind).Where(a => !string.IsNullOrEmpty(a.Name) && names.Any(n => a.Name.Contains(n, StringComparison.OrdinalIgnoreCase))).Select(a => a.Id));
                bool onlyNamed = step.GetBool("onlyNamed") && kind == AppearanceKind.Object, animated = step.GetBool("animated");
                foreach (uint id in ids.Distinct())
                {
                    var appearance = db.Find(kind, id);
                    if (appearance == null) { problems.Add(L.F("WorkflowRunner.01DoesNotExistIn", kind, id)); continue; }
                    if (onlyNamed && string.IsNullOrWhiteSpace(appearance.Name)) continue;
                    var a = appearance;
                    yield return new Producer(Label(a, kind), () => RenderAppearance(db, a, kind, sprites, animated, game.ClientVersion, L.T("WorkflowRunner.ClientAppearances")));
                }
                break;
            }
            case WorkflowStepType.RenderChangedAppearances:
            {
                var db = game!.Appearances!;
                var sprites = game.Sprites!;
                string previousFile = step.Get("previous");
                if (!File.Exists(previousFile)) throw new InvalidOperationException("Choose the older appearances file to compare with (\"New and changed since a version\").");
                var previous = (context.LoadAppearances ?? AppearanceDatabase.Load)(previousFile);
                bool changed = step.GetBool("changed"), animated = step.GetBool("animated");
                var kinds = new List<AppearanceKind>();
                if (step.GetBool("objects")) kinds.Add(AppearanceKind.Object);
                if (step.GetBool("outfits")) kinds.Add(AppearanceKind.Outfit);
                foreach (var kind in kinds)
                    foreach (var a in db.List(kind))
                    {
                        var old = previous.Find(kind, a.Id);
                        string? state = old == null ? "New" : changed && !AppearanceDatabase.AllSpriteIds(old).SequenceEqual(AppearanceDatabase.AllSpriteIds(a)) ? "Changed" : null;
                        if (state == null) continue;
                        var appearance = a;
                        var k = kind;
                        yield return new Producer($"{Label(a, kind)} ({state.ToLowerInvariant()})", () =>
                        {
                            var item = RenderAppearance(db, appearance, k, sprites, animated, game.ClientVersion, L.F("WorkflowRunner.0Since1", state, Path.GetFileName(previousFile)));
                            item.Group = state;
                            item.Tags.Add(state.ToLowerInvariant());
                            item.Description = L.F("WorkflowRunner.0InClient1ComparedWith", state, game.ClientVersion ?? "?", Path.GetFileName(previousFile));
                            return item;
                        });
                    }
                break;
            }
            case WorkflowStepType.ExtractSprites:
            {
                var sprites = game!.Sprites!;
                foreach (uint id in ParseIds(step.Get("ids"), 100_000))
                {
                    uint sid = id;
                    yield return new Producer(L.F("WorkflowRunner.Sprite0", sid), () =>
                    {
                        var image = sprites.GetSprite(sid) ?? throw new InvalidOperationException($"Sprite {sid} could not be loaded.");
                        return new WorkItem
                        {
                            Name = L.F("WorkflowRunner.Sprite0", sid), Category = AssetCategories.Sprites, Id = sid.ToString(CultureInfo.InvariantCulture), Version = game.ClientVersion,
                            Frames = [image], Delays = [0], Source = L.T("WorkflowRunner.ClientSpriteSheets"),
                        };
                    });
                }
                break;
            }
        }
    }

    private static string Label(Tibia.Protobuf.Appearances.Appearance a, AppearanceKind kind) =>
        string.IsNullOrWhiteSpace(a.Name) ? $"{kind} {a.Id}" : $"{Wiki.NameFormatter.Capitalize(a.Name)} ({a.Id})";

    private static WorkItem RenderAppearance(AppearanceDatabase db, Tibia.Protobuf.Appearances.Appearance a, AppearanceKind kind, ISpriteSource sprites, bool animated, string? version, string source)
    {
        List<RgbaImage> frames;
        List<int> delays;
        if (kind == AppearanceKind.Outfit)
        {
            // Outfits: the plain outfit (default colors, no addons), moving south.
            frames = OutfitRenderer.Render(db, sprites, a.Id, new OutfitRenderOptions { FrameGroup = 1, Direction = 2, OnlyFirstFrame = !animated });
            delays = Enumerable.Repeat(frames.Count > 2 ? 100 : 300, frames.Count).ToList();
        }
        else
        {
            int patterns = AppearanceGifBuilder.PatternCount(a);
            var gif = AppearanceGifBuilder.Build(a, kind, new GifExportOptions { FrameGroup = 0, Pattern = patterns > 0 && kind == AppearanceKind.Object ? 0 : null }, sprites);
            frames = animated ? gif.Frames.ToList() : [gif.Frames[0]];
            delays = animated ? gif.Delays.ToList() : [0];
        }
        string name = string.IsNullOrWhiteSpace(a.Name) ? $"{kind} {a.Id}" : Wiki.NameFormatter.Capitalize(a.Name);
        return new WorkItem
        {
            Name = name, Id = a.Id.ToString(CultureInfo.InvariantCulture), Version = version, Frames = frames, Delays = delays, Source = source,
            Animation = frames.Count > 1 ? "Animated" : "Still",
            Category = kind switch { AppearanceKind.Outfit => AssetCategories.Outfits, AppearanceKind.Effect => AssetCategories.Effects, AppearanceKind.Missile => AssetCategories.Projectiles, _ => AssetCategories.Items },
        };
    }

    private static (List<RgbaImage> Frames, List<int> Delays) RenderCreature(Creature creature, AppearanceDatabase db, ISpriteSource sprites, int frameGroup, int direction, bool animated)
    {
        var outfit = creature.Outfit ?? throw new InvalidOperationException($"{creature.Name} has no look in the client's data.");
        if (outfit.OutfitId != 0)
        {
            var frames = OutfitRenderer.Render(db, sprites, outfit.OutfitId, new OutfitRenderOptions
            {
                FrameGroup = frameGroup, Direction = direction, Colors = StaticDataDatabase.ColorsOf(outfit), Addons = outfit.Addons, OnlyFirstFrame = !animated,
            });
            return (frames, Enumerable.Repeat(frames.Count > 2 ? 100 : 300, frames.Count).ToList());
        }
        var obj = db.Find(AppearanceKind.Object, outfit.ObjectAppearanceTypeId) ?? throw new InvalidOperationException($"{creature.Name} looks like object {outfit.ObjectAppearanceTypeId}, which does not exist.");
        var anim = AppearanceAnimation.ForPreview(obj, AppearanceKind.Object);
        var images = anim.SpriteIds.Select(id => sprites.GetSprite(id) ?? throw new InvalidOperationException($"Sprite {id} could not be loaded.")).ToList();
        if (images.Count == 0) throw new InvalidOperationException($"{creature.Name} has no sprites.");
        return animated ? (images, anim.Delays.ToList()) : ([images[0]], [0]);
    }

    private static int DirectionIndex(string name) => name switch { "North" => 0, "East" => 1, "West" => 3, _ => 2 };
    private static string DirectionName(int d) => d switch { 0 => "North", 1 => "East", 3 => "West", _ => "South" };
    private static string AddonName(uint a) => a switch { 0 => L.T("WorkflowRunner.NoAddons"), 1 => L.T("WorkflowRunner.FirstAddon"), 2 => L.T("WorkflowRunner.SecondAddon"), _ => L.T("WorkflowRunner.BothAddons") };

    private static List<string> Lines(string text) =>
        text.Split(['\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>"128-134, 136" → 128…134, 136 (at most <paramref name="limit"/> IDs).</summary>
    public static IReadOnlyList<uint> ParseIds(string text, int limit)
    {
        var ids = new List<uint>();
        foreach (string part in text.Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] range = part.Split('-', 2, StringSplitOptions.TrimEntries);
            if (!uint.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint from)) continue;
            uint to = from;
            if (range.Length == 2 && !uint.TryParse(range[1], NumberStyles.None, CultureInfo.InvariantCulture, out to)) continue;
            if (to < from) (from, to) = (to, from);
            for (uint i = from; i <= to && ids.Count < limit; i++) ids.Add(i);
            if (ids.Count >= limit) break;
        }
        return ids.Distinct().ToList();
    }

    private static int[] ParseColors(string text)
    {
        var parts = text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).Select(p => int.TryParse(p, out int n) ? Math.Clamp(n, 0, 132) : -1).Where(n => n >= 0).ToArray();
        return parts.Length == 4 ? parts : GameTables.DefaultOutfitColors.ToArray();
    }

    // ------------------------------------------------------------------ processing

    private static void Apply(WorkflowStep step, WorkItem item, WorkflowContext context, ExportProfile profile, int index, string creator)
    {
        switch (step.Type)
        {
            case WorkflowStepType.Trim:
                Trim(item);
                break;
            case WorkflowStepType.Resize:
                if (step.Get("mode") == "Fit")
                {
                    int maxW = step.GetInt("maxWidth"), maxH = step.GetInt("maxHeight");
                    var first = item.Frames[0];
                    double f = Math.Min((double)maxW / first.Width, (double)maxH / first.Height);
                    bool nearest = step.Get("filter") != "Smooth";
                    if (nearest && f >= 1) f = Math.Floor(f);
                    int w = Math.Max(1, (int)Math.Round(first.Width * f)), h = Math.Max(1, (int)Math.Round(first.Height * f));
                    if (w != first.Width || h != first.Height)
                        item.Frames = item.Frames.Select(fr => ImageResampler.Resize(fr, w, h, nearest ? ResampleFilter.Nearest : ResampleFilter.Lanczos3)).ToList();
                    item.Scale = Math.Max(1, (int)Math.Round(f));
                }
                else Scale(item, step.GetInt("scale"));
                break;
            case WorkflowStepType.Canvas:
                Canvas(item, step.GetInt("width"), step.GetInt("height"), step.GetInt("padding"), step.Get("align"), step.Get("background"));
                break;
            case WorkflowStepType.ConvertFormat:
            {
                string format = step.Get("format");
                bool animated = item.Frames.Count > 1;
                item.Format = format switch
                {
                    "png" => "png",
                    "gif" => "gif",
                    _ => animated && step.Get("animatedAs") != "First frame PNG" ? "gif" : "png",
                };
                break;
            }
            case WorkflowStepType.ApplyMetadata:
            {
                string template = step.Get("descriptionTemplate");
                if (template.Length > 0) item.Description = TemplateText.Expand(template, Values(item, context, profile, index, creator));
                item.Tags.AddRange(AssetLibrary.NormalizeTags([step.Get("tags")]));
                break;
            }
            case WorkflowStepType.Rename:
                item.FileName = TemplateText.FileName(step.Get("template"), Values(item, context, profile, index, creator), profile.Case, profile.SpaceReplacement);
                break;
            case WorkflowStepType.Organize:
                item.Folder = TemplateText.Folder(step.Get("template"), Values(item, context, profile, index, creator), profile.Case, profile.SpaceReplacement);
                break;
        }
    }

    /// <summary>What the profile does when the workflow has no step for it.</summary>
    private static void Finish(WorkItem item, List<WorkflowStep> process, ExportProfile profile, WorkflowContext context, int index, string creator)
    {
        bool Has(WorkflowStepType t) => process.Any(s => s.Type == t);
        // Order matters: trim, scale and canvas happen before the names (which can contain the size).
        if (!Has(WorkflowStepType.Trim) && profile.TrimTransparent) Trim(item);
        if (!Has(WorkflowStepType.Resize) && profile.Scale > 1) Scale(item, profile.Scale);
        if (!Has(WorkflowStepType.Canvas) && (profile.CanvasWidth > 0 || profile.CanvasHeight > 0 || profile.Padding > 0 || profile.Background != "transparent"))
            Canvas(item, profile.CanvasWidth, profile.CanvasHeight, profile.Padding, "Bottom", profile.Background);
        item.Format ??= profile.FormatFor(item.Frames.Count > 1);
        if (item.Format == "png" && item.Frames.Count > 1)
        {
            item.Frames = [item.Frames[0]];
            item.Delays = [0];
        }
        item.FileName ??= TemplateText.FileName(profile.FileNameTemplate, Values(item, context, profile, index, creator), profile.Case, profile.SpaceReplacement);
        item.Folder ??= TemplateText.Folder(profile.FolderTemplate, Values(item, context, profile, index, creator), profile.Case, profile.SpaceReplacement);
    }

    private static Dictionary<string, string?> Values(WorkItem item, WorkflowContext context, ExportProfile profile, int index, string creator) =>
        TemplateText.Values(item.Name, item.Id, item.Category, item.Version, item.Animation, item.Direction, item.Frames[0].Width, item.Frames[0].Height, item.Scale,
            context.RunName, index, creator, item.Format ?? profile.FormatFor(item.Frames.Count > 1), profile.Website, item.Source, item.Group);

    private static void Trim(WorkItem item)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        foreach (var f in item.Frames)
        {
            var copy = f.Clone();
            for (int i = 0; i < copy.Pixels.Length; i++) if (copy.Pixels[i] >> 24 == 0) copy.Pixels[i] = 0;
            if (copy.FindContentBounds(0) is not { } b) continue;
            minX = Math.Min(minX, b.X);
            minY = Math.Min(minY, b.Y);
            maxX = Math.Max(maxX, b.X + b.Width - 1);
            maxY = Math.Max(maxY, b.Y + b.Height - 1);
        }
        if (maxX < 0) return;
        item.Frames = item.Frames.Select(f => f.Crop(minX, minY, maxX - minX + 1, maxY - minY + 1)).ToList();
    }

    private static void Scale(WorkItem item, int scale)
    {
        scale = Math.Clamp(scale, 1, 8);
        if (scale == 1) return;
        item.Frames = item.Frames.Select(f => ImageResampler.Resize(f, f.Width * scale, f.Height * scale, ResampleFilter.Nearest)).ToList();
        item.Scale *= scale;
    }

    private static void Canvas(WorkItem item, int width, int height, int padding, string align, string background)
    {
        var first = item.Frames[0];
        int w = Math.Max(width > 0 ? width : first.Width + 2 * padding, first.Width + 2 * padding);
        int h = Math.Max(height > 0 ? height : first.Height + 2 * padding, first.Height + 2 * padding);
        uint bg = ParseColor(background);
        item.Frames = item.Frames.Select(f =>
        {
            var c = new RgbaImage(w, h);
            if (bg != 0) c.Clear(bg);
            int x = align == "Top left" ? padding : (w - f.Width) / 2;
            int y = align switch { "Top left" => padding, "Center" => (h - f.Height) / 2, _ => h - padding - f.Height };
            c.DrawImage(f, x, y);
            return c;
        }).ToList();
    }

    private static uint ParseColor(string text) =>
        text.Length == 7 && text[0] == '#' && uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb) ? 0xFF000000 | rgb : 0;

    // ------------------------------------------------------------------ writing

    private static PublishedItem Write(WorkItem item, string staging, HashSet<string> usedNames, ExportProfile profile, WorkflowContext context, string creator)
    {
        string format = item.Format ?? "png";
        string folder = item.Folder ?? "";
        string stem = item.FileName ?? FileNames.Sanitize(item.Name);
        string relative = Path.Combine(folder, stem + "." + format);
        for (int n = 2; !usedNames.Add(relative); n++) relative = Path.Combine(folder, $"{stem} ({n}).{format}");
        string path = Path.Combine(staging, relative);
        if (!PathGuardInside(staging, path)) throw new InvalidOperationException($"The file name \"{relative}\" would leave the output folder.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var attribution = item.Original is { IsEmpty: false } original
            ? Attribution.ForDerivedAsset([original], AttributionChoice.KeepOriginalCreators, creator, action: "Processed for publishing")
            : Attribution.ForNewAsset(creator);
        if (format == "gif") GifWriter.Write(path, item.Frames, item.Delays.Count > 0 ? item.Delays : [100], attribution);
        else ImageIO.SavePng(item.Frames[0], path, attribution);
        var values = Values(item, context, profile, 0, creator);
        values["file"] = Path.GetFileName(relative);
        return new PublishedItem
        {
            Name = item.Name, File = relative.Replace('\\', '/'), Category = item.Category, Id = item.Id, ClientVersion = item.Version, Description = item.Description,
            Group = item.Group, Format = format, Width = item.Frames[0].Width, Height = item.Frames[0].Height, Frames = format == "gif" ? item.Frames.Count : 1,
            Attribution = TemplateText.Expand(profile.AttributionTemplate, values), Source = item.Source, Tags = item.Tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Metadata = new Dictionary<string, string> { ["Animation"] = item.Animation, ["Direction"] = item.Direction ?? "", ["Scale"] = item.Scale + "×" }
                .Where(k => k.Value.Length > 0).ToDictionary(k => k.Key, k => k.Value),
        };
    }

    private static bool PathGuardInside(string root, string path) => FileOps.PathGuard.IsInside(root, Path.GetFullPath(path));

    private static IEnumerable<string> Output(WorkflowStep step, List<PublishedItem> items, string staging, WorkflowContext context, ExportProfile profile, string creator, List<string> issues)
    {
        var vars = new Dictionary<string, string?> { ["collection"] = context.RunName, ["website"] = profile.Website, ["creator"] = creator, ["date"] = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        string footer = L.F("WorkflowRunner.TibiaGraphicsCipSoftGmbHPreparedBy", creator, AppIdentity.NameAndVersion);
        switch (step.Type)
        {
            case WorkflowStepType.Manifest:
                if (step.GetBool("json")) { AtomicFile.WriteAllText(Path.Combine(staging, "manifest.json"), ManifestWriter.Json(items, context.RunName, profile)); yield return "manifest.json"; }
                if (step.GetBool("csv")) { AtomicFile.WriteAllText(Path.Combine(staging, "manifest.csv"), ManifestWriter.Csv(items)); yield return "manifest.csv"; }
                if (step.GetBool("markdown")) { AtomicFile.WriteAllText(Path.Combine(staging, "index.md"), ManifestWriter.Markdown(items, context.RunName, footer)); yield return "index.md"; }
                break;
            case WorkflowStepType.Gallery:
            {
                string title = TemplateText.Expand(step.Get("title"), vars);
                var theme = step.Get("theme") switch { "Light" => GalleryTheme.Light, "Dark" => GalleryTheme.Dark, _ => GalleryTheme.Auto };
                GalleryGenerator.Write(staging, items, new GalleryOptions
                {
                    Title = title.Length > 0 ? title : context.RunName, Subtitle = $"{L.Count(items.Count, "Noun.image")} · {profile.Website}".Trim(' ', '·'),
                    Theme = theme, TileSize = step.GetInt("tileSize"), Footer = footer,
                });
                yield return "index.html";
                break;
            }
            case WorkflowStepType.Documentation:
            {
                string template = step.Get("template") is { Length: > 0 } t ? t : profile.DocumentationTemplate;
                if (template.Length == 0) { issues.Add(L.T("WorkflowRunner.TextSnippetsNoTemplateSetOne")); break; }
                string file = FileNames.Sanitize(step.Get("fileName") is { Length: > 0 } f ? f : profile.DocumentationFileName);
                var sb = new StringBuilder();
                foreach (var i in items)
                {
                    var values = new Dictionary<string, string?>(vars)
                    {
                        ["name"] = i.Name, ["id"] = i.Id, ["category"] = i.Category, ["version"] = i.ClientVersion, ["width"] = i.Width.ToString(CultureInfo.InvariantCulture),
                        ["height"] = i.Height.ToString(CultureInfo.InvariantCulture), ["dimensions"] = $"{i.Width}×{i.Height}", ["format"] = i.Format, ["group"] = i.Group,
                        ["file"] = Path.GetFileName(i.File), ["folder"] = Path.GetDirectoryName(i.File)?.Replace('\\', '/') ?? "", ["attribution"] = i.Attribution,
                    };
                    sb.AppendLine(TemplateText.Expand(template.Replace("\\n", "\n"), values));
                }
                AtomicFile.WriteAllText(Path.Combine(staging, file), sb.ToString());
                yield return file;
                break;
            }
            case WorkflowStepType.ContactSheet:
            {
                bool labels = step.GetBool("labels");
                int columns = step.GetInt("columns");
                uint bg = ParseColor(step.Get("background"));
                var groups = step.GetBool("perCategory") ? items.GroupBy(i => i.Category).Select(g => (Name: g.Key, Items: g.ToList())).ToList() : [(Name: "", Items: items)];
                foreach (var (name, list) in groups)
                {
                    string file = name.Length == 0 ? "contact-sheet.png" : $"contact-sheet-{FileNames.Sanitize(name).ToLowerInvariant().Replace(' ', '-')}.png";
                    var sheet = Sheet(list.Select(i => (i.Name, Path.Combine(staging, i.File))).ToList(), columns, labels, bg, list.Count > 400);
                    ImageIO.SavePng(sheet, Path.Combine(staging, file), Attribution.ForNewAsset(creator));
                    yield return file;
                }
                break;
            }
            case WorkflowStepType.ComparisonSheets:
            {
                bool labels = step.GetBool("labels");
                var groups = items.Where(i => i.Group != null).GroupBy(i => i.Group!).Where(g => g.Count() > 1).ToList();
                if (groups.Count == 0) { issues.Add(L.T("WorkflowRunner.ComparisonSheetsNoGroupsUseAll")); break; }
                foreach (var g in groups)
                {
                    string file = $"comparison/{FileNames.Sanitize(g.Key)}.png";
                    // Label what differs within the group: the addons, the direction, or both.
                    bool directions = g.Select(i => i.Metadata.GetValueOrDefault("Direction")).Distinct().Count() > 1;
                    bool addons = g.Select(i => AddonsOf(i)).Distinct().Count() > 1;
                    var sheet = Sheet(g.Select(i => (Variant(i, addons, directions), Path.Combine(staging, i.File))).ToList(), g.Count(), labels, 0, false);
                    Directory.CreateDirectory(Path.Combine(staging, "comparison"));
                    ImageIO.SavePng(sheet, Path.Combine(staging, file), Attribution.ForNewAsset(creator));
                    yield return file;
                }
                break;
            }
        }
    }

    private static string? AddonsOf(PublishedItem i) => i.Tags.FirstOrDefault(t => t.StartsWith("addons: ", StringComparison.Ordinal))?[8..];

    private static string Variant(PublishedItem i, bool addons, bool directions)
    {
        string text = string.Join(", ", new[] { addons ? AddonsOf(i) : null, directions ? i.Metadata.GetValueOrDefault("Direction")?.ToLowerInvariant() : null }.Where(s => !string.IsNullOrEmpty(s)));
        if (text.Length == 0) text = i.Name;
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>A grid of the first frames of written images, each cell the size of the largest image, with optional labels.</summary>
    private static RgbaImage Sheet(List<(string Label, string Path)> entries, int columns, bool labels, uint background, bool reduce)
    {
        var images = entries.Select(e => MediaInspector.FirstFrame(File.ReadAllBytes(e.Path), System.IO.Path.GetExtension(e.Path).TrimStart('.'))).ToList();
        if (reduce) images = images.Select(i => i.Width > 64 || i.Height > 64 ? ThumbnailCache.Fit(i, 64) : i).ToList();
        int cellW = images.Max(i => i.Width) + 8, cellH = images.Max(i => i.Height) + 8;
        int labelH = labels ? 18 : 0;
        if (labels) cellW = Math.Max(cellW, 96);
        if (columns <= 0) columns = Math.Clamp((int)Math.Ceiling(Math.Sqrt(images.Count * (double)(cellH + labelH) / cellW)), 1, 16);
        int rows = (images.Count + columns - 1) / columns;
        long pixels = (long)columns * cellW * rows * (cellH + labelH);
        if (pixels > 16384L * 16384) throw new InvalidOperationException("The contact sheet would be too large; use \"One sheet per category\" or fewer images.");
        var sheet = new RgbaImage(columns * cellW, rows * (cellH + labelH));
        if (background != 0) sheet.Clear(background);
        for (int i = 0; i < images.Count; i++)
        {
            int cx = i % columns * cellW, cy = i / columns * (cellH + labelH);
            sheet.DrawImage(images[i], cx + (cellW - images[i].Width) / 2, cy + cellH - 4 - images[i].Height);
        }
        if (!labels) return sheet;
        using var bitmap = ImageIO.ToBitmap(sheet);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var font = new Font(L.T("WorkflowRunner.SegoeUI"), 8f, FontStyle.Regular, GraphicsUnit.Point);
            bool dark = background != 0 && ((background >> 16 & 0xFF) * 299 + (background >> 8 & 0xFF) * 587 + (background & 0xFF) * 114) / 1000 < 128;
            using var text = new SolidBrush(background == 0 ? Color.FromArgb(255, 40, 44, 52) : dark ? Color.White : Color.Black);
            using var halo = new SolidBrush(background == 0 ? Color.FromArgb(200, 255, 255, 255) : Color.Transparent);
            using var format = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            for (int i = 0; i < entries.Count; i++)
            {
                var rect = new RectangleF(i % columns * cellW + 2, i / columns * (cellH + labelH) + cellH, cellW - 4, labelH);
                if (background == 0) g.FillRectangle(halo, rect);
                g.DrawString(entries[i].Label, font, text, rect, format);
            }
        }
        return ImageIO.FromBitmap(bitmap);
    }

    private static string MakeZip(WorkflowStep step, string staging, WorkflowContext context)
    {
        string name = FileNames.Sanitize(TemplateText.Expand(step.Get("fileName"), new Dictionary<string, string?> { ["collection"] = context.RunName, ["date"] = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }));
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name += ".zip";
        string temp = Path.Combine(Path.GetDirectoryName(staging)!, $".tf-zip-{Guid.NewGuid():N}.zip");
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                foreach (string f in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                    zip.CreateEntryFromFile(f, Path.GetRelativePath(staging, f).Replace('\\', '/'), CompressionLevel.Optimal);
            File.Move(temp, Path.Combine(staging, name), overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return name;
    }

    /// <summary>Moves the staged files into place. Returns the final folder.</summary>
    private static string Commit(string staging, WorkflowContext context)
    {
        string project = FileNames.Sanitize(context.RunName);
        if (context.Mode == OutputMode.UpdateInPlace)
        {
            string target = Path.Combine(context.OutputFolder, project);
            foreach (string f in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                string to = Path.Combine(target, Path.GetRelativePath(staging, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(f, to, overwrite: true);
            }
            return target;
        }
        string folder = Path.Combine(context.OutputFolder, project);
        Directory.CreateDirectory(folder);
        string run = Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture));
        for (int n = 2; Directory.Exists(run); n++) run = Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture) + $" ({n})");
        Directory.Move(staging, run);
        return run;
    }

    private static void AddToLibrary(WorkflowStep step, WorkflowRunResult result, List<long?> origins, WorkflowContext context, List<string> issues)
    {
        var lib = context.Library!;
        try
        {
            string collection = TemplateText.Expand(step.Get("collection"), new Dictionary<string, string?> { ["collection"] = context.RunName }).Trim();
            var tags = AssetLibrary.NormalizeTags([step.Get("tags")]);
            foreach (var group in result.Items.GroupBy(i => (i.Category, i.ClientVersion)))
            {
                var files = group.Select(i => Path.Combine(result.OutputFolder, i.File)).ToList();
                AssetIndexer.IndexFiles(lib, files, new IndexOptions
                {
                    Category = group.Key.Category, ClientVersion = group.Key.ClientVersion, Source = L.F("WorkflowRunner.ContentStudio0", context.RunName), SourceTool = L.T("WorkflowRunner.ContentStudio"),
                    Tags = tags, Collection = collection.Length > 0 ? collection : null,
                });
                foreach (var i in group)
                    if (lib.FindByPath(Path.Combine(result.OutputFolder, i.File)) is { } a)
                        lib.Update([a.Id], new AssetEdit { Name = i.Name, Description = i.Description, OriginalId = i.Id, ExportStatus = ExportStates.Exported, Tags = a.Tags.Concat(i.Tags).ToList() });
            }
            var sources = origins.OfType<long>().Distinct().ToList();
            if (sources.Count > 0) lib.Update(sources, new AssetEdit { ExportStatus = ExportStates.Exported });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            issues.Add(L.T("WorkflowRunner.AddingTheImagesToTheLibrary") + ex.Message + L.T("WorkflowRunner.TheFilesWereWritten"));
        }
    }
}
