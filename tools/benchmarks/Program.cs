// Workload benchmarks for the 8.8.1 performance audit. Not part of the product.
// Every scenario only READS the client: outputs and the test library go to <work folder>.
//   dotnet run -c Release -- <package folder> <work folder> [scenario...] [--reps N]
// Prints one line per scenario: median / min / max wall time, managed allocations, Gen0/1/2 counts, peak working set.
using System.Diagnostics;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Clients;
using TibiaFoundry.Core.Compare;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Library;
using TibiaFoundry.Core.Sprites;

if (args.Length < 2)
{
    Console.WriteLine("usage: <package folder> <work folder> [scenario...] [--reps N]");
    return;
}
string package = Path.GetFullPath(args[0]);
string work = Path.GetFullPath(args[1]);
int reps = 5;
var wanted = new List<string>();
for (int i = 2; i < args.Length; i++)
{
    if (args[i] == "--reps") reps = int.Parse(args[++i]);
    else wanted.Add(args[i]);
}
Directory.CreateDirectory(work);
if (wanted.Contains("dump"))
{
    // Writes results whose bytes must not change between versions (compare two dump folders).
    Dump(package, Path.Combine(work, "dump"));
    return;
}

var client = new TibiaClientPaths(package);
string assets = client.AssetsDirectory;
string appearancesFile = Directory.GetFiles(assets, "appearances-*.dat")[0];
var catalog = AssetCatalog.Load(assets);
var db = AppearanceDatabase.Load(appearancesFile);
Console.WriteLine($"client {client.ReadClientVersion()}, {catalog.Sheets.Count} sheets, {db.Count(AppearanceKind.Object)} objects, {db.Count(AppearanceKind.Outfit)} outfits, cpu {Environment.ProcessorCount}");
Console.WriteLine("scenario,reps,median_ms,min_ms,max_ms,alloc_mb,gen0,gen1,gen2,peak_ws_mb");

var outfitIds = db.List(AppearanceKind.Outfit).Where(o => o.FrameGroup.Count > 0 && o.FrameGroup[0].SpriteInfo?.SpriteId.Count > 0).Select(o => o.Id).Take(150).ToList();
var objects = db.List(AppearanceKind.Object).Where(o => o.FrameGroup.Count > 0 && o.FrameGroup[0].SpriteInfo?.SpriteId.Count > 0).ToList();
var rng = new Random(42);
// Thumbnails of the first 5,000 objects in list order (what scrolling the Appearances list asks for).
var spriteIds = objects.Take(5_000).Select(o => (uint)o.FrameGroup[0].SpriteInfo.SpriteId[0]).ToArray();
var gifObjects = objects.Where((_, i) => i % 40 == 0).Take(300).ToList();
var firstSheets = catalog.Sheets.Take(300).ToList();
string extractDir = Path.Combine(work, "extract");

ClientBaseline? launchBaseline = null;
var scenarios = new (string Name, int Reps, Action Run)[]
{
    ("catalog-load", reps, () => AssetCatalog.Load(assets)),
    ("appearances-load", reps, () => AppearanceDatabase.Load(appearancesFile)),
    ("compat-check", reps, () => CompatibilityChecker.Check(client)),
    ("compat-check-loaded", reps, () => CompatibilityChecker.Check(client, null, new LoadedData(Appearances: db))),
    ("decode-300-sheets", reps, () => { foreach (var s in firstSheets) SpriteSheetDecoder.DecodeSheet(catalog.GetSheetPath(s)); }),
    ("decode-all-sheets-parallel", Math.Min(reps, 3), () => Parallel.ForEach(catalog.Sheets, s => SpriteSheetDecoder.DecodeSheet(catalog.GetSheetPath(s)))),
    ("sprite-repo-5k-list-order", reps, () =>
    {
        var repo = new SpriteRepository(catalog, null, SpriteSourceMode.ClientOnly);
        foreach (uint id in spriteIds) repo.GetSprite(id);
    }),
    ("outfit-render-150", reps, () =>
    {
        var repo = new SpriteRepository(catalog, null, SpriteSourceMode.ClientOnly);
        foreach (uint id in outfitIds) OutfitRenderer.Render(db, repo, id, new OutfitRenderOptions { FrameGroup = 1 });
    }),
    ("gif-build-300", reps, () =>
    {
        var repo = new SpriteRepository(catalog, null, SpriteSourceMode.ClientOnly);
        foreach (var a in gifObjects) AppearanceGifBuilder.Build(a, AppearanceKind.Object, new GifExportOptions(), repo);
    }),
    ("compare-appearances-self", reps, () => DatComparer.CompareFilesAsync(appearancesFile, appearancesFile, false, null, CancellationToken.None).GetAwaiter().GetResult()),
    ("folder-inspect", reps, () => FolderInspector.Inspect(package)),
    ("installation-find", Math.Min(reps, 3), () =>
    {
        var found = InstallationFinder.Find(InstallationFinder.DefaultRoots());
        if (Environment.GetEnvironmentVariable("TF_BENCH_VERBOSE") == "1")
            foreach (var c in found.Checked) Console.Error.WriteLine($"find: {c.Path} -> {c.Inspection.Class}");
    }),
    ("client-scan", Math.Min(reps, 3), () => ClientScanner.Scan(package)),
    ("inventory-full-hash", Math.Min(reps, 3), () => ClientInventory.Scan(package)),
    ("launch-check-quick", reps, () =>
    {
        // The start-up check of a monitored client that did not change (quick mode: hashes known from the baseline).
        launchBaseline ??= LaunchCheck.BaselineOf(LaunchCheck.Run(package, null, quick: false, new InventoryOptions()), "bench", "live", false, DateTime.UtcNow, null);
        var r = LaunchCheck.Run(package, launchBaseline, quick: true, new InventoryOptions { Parallelism = 2 });
        if (r.Outcome != LaunchCheckOutcome.Unchanged) throw new InvalidOperationException("launch check: " + r.Outcome);
    }),
    ("extract-300-sheets-png", Math.Min(reps, 3), () =>
    {
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        SpriteExtractor.ExtractAsync(catalog, firstSheets, new SpriteExtractionOptions { OutputDirectory = extractDir }, null, CancellationToken.None).GetAwaiter().GetResult();
    }),
    ("library-index-cold", Math.Min(reps, 3), () =>
    {
        string libDir = Path.Combine(work, "library-cold");
        AssetLibrary.ReleaseConnections();
        if (Directory.Exists(libDir)) Directory.Delete(libDir, true);
        var lib = AssetLibrary.Open(libDir);
        AssetIndexer.IndexFolder(lib, extractDir);
    }),
    ("library-index-warm", reps, () =>
    {
        string libDir = Path.Combine(work, "library-warm");
        var lib = AssetLibrary.Open(libDir);
        var root = lib.Roots().FirstOrDefault();
        if (root == null) AssetIndexer.IndexFolder(lib, extractDir);
        else
        {
            var r = AssetIndexer.IndexRoot(lib, root);
            if (Environment.GetEnvironmentVariable("TF_BENCH_VERBOSE") == "1") Console.Error.WriteLine($"warm: seen {r.Seen}, added {r.Added}, updated {r.Updated}, unchanged {r.Unchanged}");
        }
    }),
    ("library-search-50", reps, () =>
    {
        var lib = AssetLibrary.Open(Path.Combine(work, "library-warm"));
        for (int i = 0; i < 50; i++) lib.Search(new AssetQuery { Text = (i * 37 % 1000).ToString() });
        lib.Facets();
        lib.Statistics();
    }),
};

foreach (var (name, n, run) in scenarios)
{
    if (wanted.Count > 0 && !wanted.Contains(name)) continue;
    run(); // warm-up (JIT, file cache)
    var times = new List<double>();
    long allocBefore = GC.GetTotalAllocatedBytes(true);
    int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
    var proc = Process.GetCurrentProcess();
    long peak = 0;
    for (int r = 0; r < n; r++)
    {
        GC.Collect(); GC.WaitForPendingFinalizers();
        var sw = Stopwatch.StartNew();
        run();
        times.Add(sw.Elapsed.TotalMilliseconds);
        proc.Refresh();
        peak = Math.Max(peak, proc.PeakWorkingSet64);
    }
    long alloc = (GC.GetTotalAllocatedBytes(true) - allocBefore) / n;
    times.Sort();
    Console.WriteLine($"{name},{n},{times[n / 2]:0.0},{times[0]:0.0},{times[^1]:0.0},{alloc / 1048576.0:0.0},{(GC.CollectionCount(0) - g0) / (double)n:0.#},{(GC.CollectionCount(1) - g1) / (double)n:0.#},{(GC.CollectionCount(2) - g2) / (double)n:0.#},{peak / 1048576.0:0}");
}
AssetLibrary.ReleaseConnections();

static void Dump(string package, string folder)
{
    Directory.CreateDirectory(folder);
    var client = new TibiaClientPaths(package);
    var lines = new List<string>();
    var descriptors = new Dictionary<string, Google.Protobuf.Reflection.MessageDescriptor>
    {
        ["appearances"] = Tibia.Protobuf.Appearances.Appearances.Descriptor,
        ["staticdata"] = Tibia.Protobuf.Staticdata.StaticData.Descriptor,
        ["staticmapdata"] = Tibia.Protobuf.Staticmapdata.StaticMapData.Descriptor,
        ["map"] = Tibia.Protobuf.Map.Map.Descriptor,
        ["sounds"] = Tibia.Protobuf.Sound.Sounds.Descriptor,
    };
    var files = Directory.GetFiles(client.AssetsDirectory, "*.dat").ToList();
    if (client.FindSoundsFile() is { } sounds) files.Add(sounds);
    foreach (string file in files.Order(StringComparer.Ordinal))
    {
        string type = Path.GetFileName(file).Split('-')[0];
        if (!descriptors.TryGetValue(type, out var descriptor)) continue;
        byte[] bytes = File.ReadAllBytes(file);
        byte[] repaired = TibiaFoundry.Core.Protobuf.Utf8Repair.Repair(descriptor, bytes, out int fixedStrings);
        lines.Add($"{type} repair fixed={fixedStrings} sha={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(repaired))} len={repaired.Length}");
        var schema = SchemaInspector.Inspect(descriptor, bytes);
        lines.Add($"{type} schema truncated={schema.Truncated} error={schema.Error} drift={string.Join(" | ", schema.Drift.Select(d => d.ToString()))}");
    }
    var db = AppearanceDatabase.Load(Directory.GetFiles(client.AssetsDirectory, "appearances-*.dat")[0]);
    lines.Add($"appearances parsed sha={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Google.Protobuf.MessageExtensions.ToByteArray(db.Data)))}");
    var report = CompatibilityChecker.Check(client);
    lines.Add($"compat overall={report.Overall} version={report.ClientVersion}");
    foreach (var c in report.Components) lines.Add($"compat {c.Component}|{c.Status}|{c.Summary}|{string.Join(" ; ", c.Details)}");
    var catalog = AssetCatalog.Load(client.AssetsDirectory);
    foreach (var s in catalog.Sheets.Where((_, i) => i < 150 || i % 20 == 0))
    {
        var image = SpriteSheetDecoder.DecodeSheet(catalog.GetSheetPath(s));
        lines.Add($"sheet {s.FirstSpriteId} {image.Width}x{image.Height} {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image.Bytes))}");
    }
    string png = Path.Combine(folder, "png");
    if (Directory.Exists(png)) Directory.Delete(png, true);
    SpriteExtractor.ExtractAsync(catalog, catalog.Sheets.Take(4).ToList(), new SpriteExtractionOptions { OutputDirectory = png }, null, CancellationToken.None).GetAwaiter().GetResult();
    foreach (string f in Directory.GetFiles(png, "*.png").Order(StringComparer.Ordinal))
        lines.Add($"png {Path.GetFileName(f)} {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ImageIO.Load(f).Bytes))}");
    File.WriteAllLines(Path.Combine(folder, "dump.txt"), lines);
    Console.WriteLine($"dump: {lines.Count} lines");
}