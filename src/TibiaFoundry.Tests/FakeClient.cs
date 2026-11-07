using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TibiaFoundry.Core.Compatibility;

namespace TibiaFoundry.Tests;

/// <summary>
/// Writes a client installation laid out like the real one: package.json (version, and the RCC file
/// in its "files" list), assets.json with the hash and size of every asset and sound file (packed
/// files, "unpack": false), assets.json.sha256, a Qt resource file, and personal folders and program
/// files that a version must never include.
/// </summary>
internal sealed class FakeClient
{
    public FakeClient(string folder, string version)
    {
        Folder = folder;
        Version = version;
    }

    public string Folder { get; }
    public string Version { get; set; }

    /// <summary>Package path ('/' separated) → content of every asset, sound and RCC file.</summary>
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    /// <summary>First sprite ID of a sheet (sheets not listed get consecutive ranges of 10 in their order).</summary>
    public Dictionary<string, int> SheetFirst { get; } = new(StringComparer.Ordinal);

    /// <summary>When false, assets.json is not written (the scanner must fall back to the catalogs).</summary>
    public bool WriteAssetsJson { get; set; } = true;

    /// <summary>When false, the RCC file is not listed in package.json (it must still be found by its header).</summary>
    public bool ListRccInPackageJson { get; set; } = true;

    /// <summary>A client with a catalog, two sprite sheets, appearances, two sounds and an RCC file.</summary>
    public static FakeClient Standard(string folder, string version = "15.33.9d148f", string seed = "base")
    {
        var c = new FakeClient(folder, version);
        c.Files["assets/sprites-aaaa.bmp.lzma"] = Bytes("sheet 1-10 " + seed);
        c.Files["assets/sprites-bbbb.bmp.lzma"] = Bytes("sheet 11-20 shared");
        c.Files["assets/appearances-1111.dat"] = Bytes("appearances " + seed);
        c.Files["assets/satellite-0001.bmp.lzma"] = Bytes("satellite tile");
        c.Files["sounds/sound-0001.ogg"] = Bytes("OggS sound one");
        c.Files["sounds/sound-0002.ogg"] = Bytes("OggS sound two");
        c.Files["sounds/sounds-2222.dat"] = Bytes("sounds data");
        c.Files["bin/graphics_resources.rcc"] = Rcc("interface " + seed);
        return c;
    }

    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Bytes that start with the Qt resource header.</summary>
    public static byte[] Rcc(string text) => [.. "qres"u8.ToArray(), .. Bytes(text)];

    public static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>Writes (or rewrites) the installation.</summary>
    public FakeClient Write()
    {
        if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        Directory.CreateDirectory(Folder);
        var sheets = Files.Keys.Where(k => k.StartsWith("assets/sprites-", StringComparison.Ordinal)).ToList();
        var catalog = new List<object>();
        int first = 1;
        foreach (string sheet in sheets)
        {
            int start = SheetFirst.TryGetValue(sheet, out int chosen) ? chosen : first;
            catalog.Add(new Dictionary<string, object> { ["type"] = "sprite", ["file"] = sheet["assets/".Length..], ["spritetype"] = 0, ["firstspriteid"] = start, ["lastspriteid"] = start + 9, ["area"] = 0 });
            first += 10;
        }
        foreach (string dat in Files.Keys.Where(k => k.StartsWith("assets/", StringComparison.Ordinal) && k.EndsWith(".dat", StringComparison.Ordinal)))
            catalog.Add(new Dictionary<string, object> { ["type"] = dat["assets/".Length..].Split('-')[0], ["file"] = dat["assets/".Length..] });
        var all = new Dictionary<string, byte[]>(Files, StringComparer.Ordinal)
        {
            ["assets/catalog-content.json"] = JsonSerializer.SerializeToUtf8Bytes(catalog),
        };
        var soundFiles = Files.Keys.Where(k => k.StartsWith("sounds/", StringComparison.Ordinal)).ToList();
        if (soundFiles.Count > 0)
            all["sounds/catalog-sound.json"] = JsonSerializer.SerializeToUtf8Bytes(soundFiles.Where(s => s.EndsWith(".dat", StringComparison.Ordinal))
                .Select(s => new Dictionary<string, object> { ["type"] = "sounds", ["file"] = s["sounds/".Length..] }));
        foreach (var (path, data) in all) WriteFile(path, data);

        if (WriteAssetsJson)
        {
            var list = all.Where(kv => kv.Key.StartsWith("assets/", StringComparison.Ordinal) || kv.Key.StartsWith("sounds/", StringComparison.Ordinal))
                .Select(kv => new Dictionary<string, object> { ["url"] = kv.Key + ".lzma", ["unpack"] = false, ["packedhash"] = Sha(kv.Value), ["packedsize"] = kv.Value.Length, ["localfile"] = kv.Key })
                .ToList();
            byte[] assetsJson = JsonSerializer.SerializeToUtf8Bytes(new { files = list });
            WriteFile("assets.json", assetsJson);
            WriteFile("assets.json.sha256", Bytes(Sha(assetsJson)));
        }
        var packageFiles = new List<object> { new Dictionary<string, object> { ["localfile"] = "bin/client.exe", ["unpackedhash"] = Sha(Bytes("program")), ["unpackedsize"] = 7 } };
        if (ListRccInPackageJson)
            foreach (var (path, data) in Files.Where(kv => kv.Key.EndsWith(".rcc", StringComparison.Ordinal)))
                packageFiles.Add(new Dictionary<string, object> { ["localfile"] = path, ["unpackedhash"] = Sha(data), ["unpackedsize"] = data.Length });
        WriteFile(TibiaFormat.PackageManifest, JsonSerializer.SerializeToUtf8Bytes(new { version = Version, files = packageFiles }));
        WriteFile("package.json.version", Bytes(Version));

        // Never part of a version: the program, personal data, caches and backups.
        WriteFile("bin/client.exe", Bytes("program"));
        WriteFile("conf/clientoptions.json", Bytes("{\"hotkeys\":[]}"));
        WriteFile("characterdata/someone/settings.json", Bytes("{}"));
        WriteFile("minimap/minimap_1.bmp", Bytes("explored"));
        WriteFile("screenshots/shot.png", Bytes("png"));
        WriteFile("log/client.log", Bytes("log"));
        WriteFile("cache/qres-looking.bin", Rcc("cached, not a client resource"));
        WriteFile("assets/appearances-old.dat-bak", Bytes("old"));
        return this;
    }

    public void WriteFile(string path, byte[] data)
    {
        string full = Path.Combine(Folder, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, data);
    }

    public string PathOf(string path) => Path.Combine(Folder, path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Bytes and write time of every file in the installation (to prove nothing was changed).</summary>
    public static Dictionary<string, (string Sha, DateTime Written, FileAttributes Attributes)> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(folder, f),
            f => (Sha(File.ReadAllBytes(f)), File.GetLastWriteTimeUtc(f), File.GetAttributes(f)),
            StringComparer.OrdinalIgnoreCase);
}
