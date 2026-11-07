// Measurement tool used for docs/09 §5 and 07_Testing_Report.md. Not part of the product.
//   dotnet run -c Release -- decode  <assets folder>      sprite sheet decode time and checksum share
//   dotnet run -c Release -- memory  <package folder>     allocations and retained memory of the compatibility check
//   dotnet run -c Release -- report  <output folder> <package folder>...   compatibility report per package
using System.Diagnostics;
using System.Security.Cryptography;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Sprites;

switch (args.FirstOrDefault())
{
    case "decode": Decode(args[1]); break;
    case "memory": Memory(args[1]); break;
    case "report": Report(args[1], args.Skip(2)); break;
    default: Console.WriteLine("usage: decode <assets> | memory <package> | report <output> <package>..."); break;
}

static void Decode(string assets)
{
    var catalog = AssetCatalog.Load(assets);
    var sheets = catalog.Sheets;
    foreach (var s in sheets.Take(20)) SpriteSheetDecoder.DecodeSheet(catalog.GetSheetPath(s)); // warm-up
    long lzma = 0, sha = 0, bmp = 0;
    var sw = new Stopwatch();
    foreach (var s in sheets)
    {
        string path = catalog.GetSheetPath(s);
        sw.Restart(); var data = CipLzma.DecompressFile(path); lzma += sw.ElapsedTicks;
        sw.Restart(); SHA256.HashData(data); sha += sw.ElapsedTicks;
        sw.Restart(); BmpDecoder.Decode(data); bmp += sw.ElapsedTicks;
    }
    sw.Restart();
    foreach (var s in sheets) SpriteSheetDecoder.DecodeSheet(catalog.GetSheetPath(s));
    long full = sw.ElapsedTicks;
    static double Ms(long t) => t * 1000.0 / Stopwatch.Frequency;
    Console.WriteLine($"sheets: {sheets.Count}");
    Console.WriteLine($"lzma {Ms(lzma):0} ms, sha256 {Ms(sha):0} ms, bmp {Ms(bmp):0} ms, DecodeSheet total {Ms(full):0} ms");
    Console.WriteLine($"checksum share of decode: {100.0 * sha / (lzma + sha + bmp):0.0} %, per sheet {Ms(full) / sheets.Count:0.00} ms");
}

static void Memory(string package)
{
    var client = new TibiaClientPaths(package);
    var db = AppearanceDatabase.Load(Directory.GetFiles(client.AssetsDirectory, "appearances-*.dat")[0]);
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    long before = GC.GetTotalMemory(true), allocBefore = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();
    var report = CompatibilityChecker.Check(client, null, new LoadedData(Appearances: db));
    sw.Stop();
    long allocated = GC.GetTotalAllocatedBytes(true) - allocBefore;
    long liveNoGc = GC.GetTotalMemory(false), after = GC.GetTotalMemory(true);
    Console.WriteLine($"check {sw.ElapsedMilliseconds} ms, overall {report.Overall}");
    Console.WriteLine($"allocated during check: {allocated / 1048576.0:0} MB");
    Console.WriteLine($"managed live before {before / 1048576.0:0} MB, after (no GC) {liveNoGc / 1048576.0:0} MB, after full GC {after / 1048576.0:0} MB");
    GC.KeepAlive(db);
}

static void Report(string output, IEnumerable<string> packages)
{
    foreach (string package in packages)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(package));
        var report = CompatibilityChecker.Check(new TibiaClientPaths(full));
        File.WriteAllText(Path.Combine(output, $"compatibility_{Path.GetFileName(full)}.txt"), report.ToText());
        Console.WriteLine($"{full}: {report.Overall} ({report.ClientVersion})");
    }
}
