using System.IO.Compression;
using System.Text;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Scripts;

namespace TibiaFoundry.Tests;

/// <summary>Every built-in script of Utilities → Scripts, on temporary folders (never on real user files).</summary>
public class ScriptTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Env
    {
        public string Root { get; } = TestData.NewTempDirectory("scripts");
        public BackupStore Store { get; }
        public TempWorkspace Temp { get; }
        public Env()
        {
            Store = new BackupStore(Path.Combine(Root, "_backups"));
            Temp = new TempWorkspace(Path.Combine(Root, "_temp"));
        }

        public string Dir(string name)
        {
            string d = Path.Combine(Root, name);
            Directory.CreateDirectory(d);
            return d;
        }

        public ScriptContext Context(Script script, Dictionary<string, string?> values, ConflictPrompt? prompt = null, FileSafetyOptions? safety = null)
        {
            var args = new ScriptArguments();
            foreach (var p in script.Parameters) if (p.Default != null) args[p.Key] = p.Default;
            foreach (var kv in values) args[kv.Key] = kv.Value;
            var problems = script.Validate(args);
            Assert.True(problems.Count == 0, string.Join(" | ", problems));
            return new ScriptContext { Args = args, Backups = Store, Temp = Temp, Prompt = prompt, Safety = safety ?? FileSafetyOptions.Default, WorkerThreads = 2 };
        }
    }

    private static IReadOnlyList<Script> All(Env env) => ScriptRegistry.CreateAll(() => env.Store, Path.Combine(env.Root, "_renames"));

    [Fact]
    public void TwelveScriptsAreRegisteredWithDistinctIdsAndDescriptions()
    {
        var env = new Env();
        var scripts = All(env);
        Assert.Equal(12, scripts.Count);
        Assert.Equal(12, scripts.Select(s => s.Id).Distinct().Count());
        Assert.All(scripts, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Description));
            Assert.False(string.IsNullOrWhiteSpace(s.SupportedInputs));
            Assert.NotEmpty(s.Parameters);
            Assert.All(s.Parameters, p => Assert.False(string.IsNullOrWhiteSpace(p.Help)));
        });
        Assert.Equal(7, scripts.Select(s => s.Category).Distinct().Count());
    }

    [Fact]
    public void MissingInputsAreReportedBeforeRunning()
    {
        var script = new DirectoryComparisonScript();
        var args = new ScriptArguments { ["left"] = @"Z:\does\not\exist", ["right"] = "" };
        var problems = script.Validate(args);
        Assert.Contains(problems, p => p.Contains("does not exist", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("required", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateFinderKeepsOneCopyAndMovesTheRestReversibly()
    {
        var env = new Env();
        string dir = env.Dir("dups");
        File.WriteAllText(Path.Combine(dir, "a.txt"), "same content");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "sub", "b.txt"), "same content");
        File.WriteAllText(Path.Combine(dir, "c.txt"), "same content");
        File.WriteAllText(Path.Combine(dir, "d.txt"), "same length!");
        File.SetLastWriteTimeUtc(Path.Combine(dir, "a.txt"), DateTime.UtcNow.AddDays(-3));
        var script = new DuplicateFileFinderScript();
        var context = env.Context(script, new() { ["folder"] = dir, ["action"] = "move", ["keep"] = "oldest" });
        var preview = script.Run(context, Ct);
        Assert.Equal(3, preview.Table!.Rows.Count);
        Assert.Equal(2, preview.Plan!.Count);
        Assert.DoesNotContain(preview.Plan, c => c.Source.EndsWith("a.txt", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(dir, "c.txt"))); // the preview changed nothing

        var applied = script.Apply(context, preview.Plan, Ct);
        Assert.Equal(2, applied.Succeeded);
        Assert.True(File.Exists(Path.Combine(dir, "a.txt")));
        Assert.False(File.Exists(Path.Combine(dir, "c.txt")));
        Assert.True(File.Exists(Path.Combine(dir, "d.txt")));

        // Everything can be put back with Backup and Restore.
        var set = env.Store.List().Single(s => s.Manifest.Kind == BackupKind.Removed);
        var restore = env.Store.Restore(set, set.Manifest.Entries, null, _ => false, Ct);
        Assert.Equal(2, restore.Restored.Count);
        Assert.True(File.Exists(Path.Combine(dir, "c.txt")));
    }

    [Fact]
    public void DuplicateFinderRefusesToApplyWhenTheKeptCopyChanged()
    {
        var env = new Env();
        string dir = env.Dir("dups2");
        File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
        File.WriteAllText(Path.Combine(dir, "b.txt"), "x");
        var script = new DuplicateFileFinderScript();
        var context = env.Context(script, new() { ["folder"] = dir, ["action"] = "move", ["keep"] = "first" });
        var preview = script.Run(context, Ct);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "y");
        Assert.Throws<InvalidOperationException>(() => script.Apply(context, preview.Plan!, Ct));
        Assert.True(File.Exists(Path.Combine(dir, "b.txt")));
    }

    [Fact]
    public void DirectoryComparisonReportsEveryKindOfDifference()
    {
        var env = new Env();
        string a = env.Dir("left"), b = env.Dir("right");
        File.WriteAllText(Path.Combine(a, "same.txt"), "1");
        File.WriteAllText(Path.Combine(b, "same.txt"), "1");
        File.SetLastWriteTimeUtc(Path.Combine(b, "same.txt"), File.GetLastWriteTimeUtc(Path.Combine(a, "same.txt")));
        File.WriteAllText(Path.Combine(a, "only-left.txt"), "1");
        File.WriteAllText(Path.Combine(b, "only-right.txt"), "1");
        File.WriteAllText(Path.Combine(a, "size.txt"), "1");
        File.WriteAllText(Path.Combine(b, "size.txt"), "22");
        File.WriteAllText(Path.Combine(a, "content.txt"), "ab");
        File.WriteAllText(Path.Combine(b, "content.txt"), "ba");
        var script = new DirectoryComparisonScript();
        var result = script.Run(env.Context(script, new() { ["left"] = a, ["right"] = b, ["content"] = "true" }), Ct);
        string Status(string file) => result.Table!.Rows.Single(r => r[1] == file)[0];
        Assert.Equal("Only in first", Status("only-left.txt"));
        Assert.Equal("Only in second", Status("only-right.txt"));
        Assert.Equal("Different size", Status("size.txt"));
        Assert.Equal("Different content", Status("content.txt"));
        Assert.DoesNotContain(result.Table!.Rows, r => r[1] == "same.txt");
    }

    [Fact]
    public void RenamerPreviewsConflictsAppliesAndUndoes()
    {
        var env = new Env();
        string dir = env.Dir("rename");
        File.WriteAllText(Path.Combine(dir, "Fire Sword.png"), "1");
        File.WriteAllText(Path.Combine(dir, "Ice  Sword.png"), "2");
        File.WriteAllText(Path.Combine(dir, "Fire_Sword.png"), "3"); // the target of the first file already exists
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "4");
        var script = All(env).OfType<BatchRenamerScript>().Single();
        var context = env.Context(script, new() { ["folder"] = dir, ["filter"] = "*.png", ["spaces"] = "wiki" });
        var preview = script.Run(context, Ct);
        var fire = preview.Plan!.Single(c => c.Source.EndsWith("Fire Sword.png", StringComparison.Ordinal));
        Assert.True(fire.Blocked);
        var ice = preview.Plan!.Single(c => c.Source.EndsWith("Ice  Sword.png", StringComparison.Ordinal));
        Assert.EndsWith("Ice_Sword.png", ice.Target);
        Assert.True(File.Exists(Path.Combine(dir, "Ice  Sword.png")));

        var applied = script.Apply(context, preview.Plan!, Ct);
        Assert.Equal(1, applied.Succeeded);
        Assert.True(File.Exists(Path.Combine(dir, "Ice_Sword.png")));
        Assert.True(File.Exists(Path.Combine(dir, "Fire Sword.png")));
        Assert.Equal("4", File.ReadAllText(Path.Combine(dir, "notes.txt")));

        var record = Assert.Single(applied.OutputFiles);
        var undo = env.Context(script, new() { ["mode"] = "undo", ["record"] = record });
        var undoPreview = script.Run(undo, Ct);
        script.Apply(undo, undoPreview.Plan!, Ct);
        Assert.True(File.Exists(Path.Combine(dir, "Ice  Sword.png")));
        Assert.False(File.Exists(Path.Combine(dir, "Ice_Sword.png")));
    }

    [Fact]
    public void RenamerSwapsNamesAndOnlyChangesExtensionsThatMatchTheContent()
    {
        var env = new Env();
        string dir = env.Dir("rename2");
        File.WriteAllBytes(Path.Combine(dir, "1.dat"), ImageIO.EncodePng(new RgbaImage(1, 1)));
        File.WriteAllText(Path.Combine(dir, "2.dat"), "text, not an image");
        var script = new BatchRenamerScript { RecordFolder = Path.Combine(env.Root, "_renames") };
        var context = env.Context(script, new() { ["folder"] = dir, ["extension"] = ".png" });
        var preview = script.Run(context, Ct);
        var plan = Assert.Single(preview.Plan!);
        Assert.EndsWith("1.png", plan.Target);
        Assert.Contains(preview.Table!.Rows, r => r[3].Contains("extension kept", StringComparison.Ordinal));

        // Numbering that shifts names onto each other works because renames go through temporary names.
        string swap = env.Dir("swap");
        File.WriteAllText(Path.Combine(swap, "1.txt"), "first");
        File.WriteAllText(Path.Combine(swap, "2.txt"), "second");
        var shift = env.Context(script, new() { ["folder"] = swap, ["number"] = "replace", ["start"] = "2" });
        var shiftPreview = script.Run(shift, Ct);
        script.Apply(shift, shiftPreview.Plan!, Ct);
        Assert.Equal("first", File.ReadAllText(Path.Combine(swap, "2.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(swap, "3.txt")));
    }

    [Fact]
    public void ChecksumManifestsAreCompatibleAndVerify()
    {
        var env = new Env();
        string dir = env.Dir("hashes");
        File.WriteAllText(Path.Combine(dir, "a.txt"), "abc");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "sub", "b.txt"), "b");
        string manifest = Path.Combine(env.Root, "list.sha256");
        var script = new ChecksumScript();
        var created = script.Run(env.Context(script, new() { ["mode"] = "create", ["path"] = dir, ["manifest"] = manifest }), Ct);
        Assert.Equal(2, created.Succeeded);
        Assert.Contains("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  a.txt", File.ReadAllText(manifest));
        File.WriteAllText(Path.Combine(dir, "a.txt"), "changed");
        File.Delete(Path.Combine(dir, "sub", "b.txt"));
        File.WriteAllText(Path.Combine(dir, "extra.txt"), "new");
        var verified = script.Run(env.Context(script, new() { ["mode"] = "verify", ["verifyFolder"] = dir, ["manifestIn"] = manifest }), Ct);
        string Status(string file) => verified.Table!.Rows.Single(r => r[1] == file)[0];
        Assert.Equal("Modified", Status("a.txt"));
        Assert.Equal("Missing", Status("sub/b.txt"));
        Assert.Equal("Not in manifest", Status("extra.txt"));
        Assert.Equal(0, verified.Succeeded);
    }

    [Fact]
    public void InventoryFlagsWrongExtensionsAndSavesCsv()
    {
        var env = new Env();
        string dir = env.Dir("inventory");
        File.WriteAllBytes(Path.Combine(dir, "image.txt"), ImageIO.EncodePng(new RgbaImage(1, 1)));
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "hello");
        string output = Path.Combine(env.Root, "inventory.csv");
        var script = new InventoryScript();
        var result = script.Run(env.Context(script, new() { ["folder"] = dir, ["hashes"] = "true", ["output"] = output }), Ct);
        Assert.Equal("yes", result.Table!.Rows.Single(r => r[0] == "image.txt")[5]);
        Assert.Equal("", result.Table!.Rows.Single(r => r[0] == "notes.txt")[5]);
        string csv = File.ReadAllText(output);
        Assert.Contains("image.txt", csv);
        Assert.Contains(Attribution.TextFor(Attribution.CurrentCreator), csv);
    }

    [Fact]
    public void LargeFilesAreListedLargestFirst()
    {
        var env = new Env();
        string dir = env.Dir("large");
        File.WriteAllBytes(Path.Combine(dir, "big.bin"), new byte[3 * 1024 * 1024]);
        File.WriteAllBytes(Path.Combine(dir, "bigger.bin"), new byte[5 * 1024 * 1024]);
        File.WriteAllBytes(Path.Combine(dir, "small.bin"), new byte[10]);
        var script = new LargeFileFinderScript();
        var result = script.Run(env.Context(script, new() { ["folder"] = dir, ["minMb"] = "1" }), Ct);
        Assert.Equal(["bigger.bin", "big.bin"], result.Table!.Rows.Select(r => Path.GetFileName(r[2])));
    }

    [Fact]
    public void EmptyEntriesAreFoundButProtectedFoldersKept()
    {
        var env = new Env();
        string dir = env.Dir("empty");
        File.WriteAllText(Path.Combine(dir, "zero.txt"), "");
        File.WriteAllText(Path.Combine(dir, "full.txt"), "x");
        Directory.CreateDirectory(Path.Combine(dir, "nothing"));
        Directory.CreateDirectory(Path.Combine(dir, "logs"));
        var script = new EmptyEntryFinderScript();
        var context = env.Context(script, new() { ["folder"] = dir, ["action"] = "move" });
        var preview = script.Run(context, Ct);
        Assert.Equal(2, preview.Plan!.Count); // zero.txt and "nothing"; "logs" is kept
        Assert.Contains(preview.Table!.Rows, r => r[1].EndsWith("logs", StringComparison.Ordinal) && r[3].StartsWith("kept", StringComparison.Ordinal));
        var applied = script.Apply(context, preview.Plan!, Ct);
        Assert.Equal(2, applied.Succeeded);
        Assert.False(Directory.Exists(Path.Combine(dir, "nothing")));
        Assert.True(Directory.Exists(Path.Combine(dir, "logs")));
        var set = env.Store.List().Single();
        env.Store.Restore(set, set.Manifest.Entries, null, _ => false, Ct);
        Assert.True(Directory.Exists(Path.Combine(dir, "nothing")));
        Assert.True(File.Exists(Path.Combine(dir, "zero.txt")));
    }

    [Fact]
    public void ImageConverterScalesPixelArtExactlyAndKeepsAttribution()
    {
        var env = new Env();
        string input = env.Dir("img-in"), output = Path.Combine(env.Root, "img-out");
        var source = SyntheticAssets.Sprite(4, 32, 32);
        var other = Attribution.ForNewAsset("SomeoneElse");
        File.WriteAllBytes(Path.Combine(input, "sprite.png"), ImageIO.EncodePng(source, other));
        File.WriteAllBytes(Path.Combine(input, "anim.gif"), GifWriter.Encode([source, SyntheticAssets.Sprite(5, 32, 32)], [100, 200]));
        var script = new ImageConverterScript();
        var result = script.Run(env.Context(script, new() { ["input"] = input, ["output"] = output, ["resize"] = "percent", ["percent"] = "200" }), Ct);
        Assert.Equal(1, result.Succeeded); // the animated GIF is skipped for PNG
        Assert.Contains(result.Table!.Rows, r => r[3].Contains("animated", StringComparison.Ordinal));
        var png = File.ReadAllBytes(Path.Combine(output, "sprite.png"));
        var scaled = ImageIO.LoadFromBytes(png);
        Assert.Equal((64, 64), (scaled.Width, scaled.Height));
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            uint expected = source[x / 2, y / 2];
            if (expected >> 24 == 0) Assert.Equal(0u, scaled[x, y] >> 24);
            else Assert.Equal(expected, scaled[x, y]);
        }
        var attribution = AttributionReader.Read(png)!;
        Assert.Contains("SomeoneElse", attribution.Creators);
        Assert.Contains(attribution.History, h => h.Contains("Converted to Png at 64×64", StringComparison.Ordinal));

        var gif = script.Run(env.Context(script, new() { ["input"] = Path.Combine(input, "anim.gif"), ["output"] = Path.Combine(env.Root, "gif-out"), ["format"] = "Gif" }), Ct);
        Assert.Equal(1, gif.Succeeded);
        Assert.Equal(2, ImageFile.Decode(File.ReadAllBytes(Path.Combine(env.Root, "gif-out", "anim.gif"))).Frames.Count);
    }

    [Fact]
    public void ImageConverterRequiresConsentForLossyOutputAndNeverWritesIntoTheInput()
    {
        var script = new ImageConverterScript();
        var env = new Env();
        string input = env.Dir("in");
        File.WriteAllBytes(Path.Combine(input, "a.png"), ImageIO.EncodePng(new RgbaImage(4, 4)));
        var args = new ScriptArguments { ["input"] = input, ["output"] = Path.Combine(env.Root, "out"), ["format"] = "Jpeg", ["resize"] = "none", ["filter"] = "Nearest", ["attribution"] = "keep" };
        Assert.Contains(script.Validate(args), p => p.Contains("Allow lossy JPEG", StringComparison.Ordinal));
        args["format"] = "Png";
        args["output"] = input;
        Assert.Contains(script.Validate(args), p => p.Contains("other than the input", StringComparison.Ordinal));
    }

    [Fact]
    public void ResamplingIsDeterministicAndRespectsAlpha()
    {
        var image = new RgbaImage(2, 1);
        image[0, 0] = 0xFFFF0000;
        image[1, 0] = 0x000000FF; // transparent blue must not tint the red edge
        var smooth = ImageResampler.Resize(image, 4, 1, ResampleFilter.Bilinear);
        Assert.All(smooth.Pixels.Where(p => p >> 24 != 0), p => Assert.Equal(0u, p & 0xFF));
        Assert.Equal(image.Pixels, ImageResampler.Resize(image, 2, 1, ResampleFilter.Lanczos3).Pixels);
        var nearest = ImageResampler.Resize(image, 4, 2, ResampleFilter.Nearest);
        Assert.Equal([0xFFFF0000, 0xFFFF0000, 0x000000FF, 0x000000FF], nearest.Pixels.Take(4));
    }

    [Fact]
    public void ArchiveInspectorFlagsUnsafeEntriesAndExtractsOnlySafeOnes()
    {
        var env = new Env();
        string zip = Path.Combine(env.Root, "test.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            void Add(string name, string text)
            {
                using var s = archive.CreateEntry(name).Open();
                s.Write(Encoding.UTF8.GetBytes(text));
            }
            Add("good/readme.txt", "hello");
            Add("../escape.txt", "evil");
            Add("good/readme.txt", "duplicate");
            Add("bomb.txt", new string('0', 2_000_000));
        }
        var script = new ArchiveInspectorScript();
        string dest = Path.Combine(env.Root, "extracted");
        var context = env.Context(script, new() { ["archive"] = zip, ["mode"] = "extract", ["destination"] = dest });
        var preview = script.Run(context, Ct);
        string Problem(string name) => preview.Table!.Rows.First(r => r[0] == name)[4];
        Assert.StartsWith("unsafe path", Problem("../escape.txt"));
        Assert.Contains("ratio", Problem("bomb.txt"));
        Assert.Contains(preview.Table!.Rows, r => r[0] == "good/readme.txt" && r[4].StartsWith("duplicate", StringComparison.Ordinal));
        Assert.Single(preview.Plan!, c => !c.Blocked);
        var applied = script.Apply(context, preview.Plan!, Ct);
        Assert.Equal(1, applied.Succeeded);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(dest, "good", "readme.txt")));
        Assert.False(File.Exists(Path.Combine(env.Root, "escape.txt")));
        Assert.False(File.Exists(Path.Combine(dest, "bomb.txt")));
    }

    [Fact]
    public void FormatInspectorSeparatesIdentificationFromValidation()
    {
        var env = new Env();
        string dir = env.Dir("inspect");
        // Explicit attribution: the current creator is process-wide and other tests change it.
        byte[] png = ImageIO.EncodePng(new RgbaImage(3, 2), Attribution.ForNewAsset("Inspector"));
        File.WriteAllBytes(Path.Combine(dir, "good.png"), png);
        byte[] broken = (byte[])png.Clone();
        broken[30] ^= 0xFF; // inside the IHDR chunk: its checksum no longer matches
        File.WriteAllBytes(Path.Combine(dir, "broken.png"), broken);
        File.WriteAllBytes(Path.Combine(dir, "renamed.gif"), png);
        var script = new FormatInspectorScript();
        var result = script.Run(env.Context(script, new() { ["path"] = dir }), Ct);
        var rows = result.Table!.Rows.ToDictionary(r => r[0]);
        Assert.Equal("decoded completely", rows["good.png"][4]);
        Assert.Contains("3×2", rows["good.png"][5]);
        Assert.Contains("Inspector", rows["good.png"][6]);
        Assert.Contains("checksum", rows["broken.png"][7]);
        Assert.NotEqual("decoded completely", rows["broken.png"][4]);
        Assert.Contains("does not match", rows["renamed.gif"][7]);
    }

    [Fact]
    public void BackupScriptBacksUpListsAndRestores()
    {
        var env = new Env();
        string dir = env.Dir("project");
        File.WriteAllText(Path.Combine(dir, "a.txt"), "v1");
        var script = All(env).OfType<BackupRestoreScript>().Single();
        var backup = script.Run(env.Context(script, new() { ["mode"] = "backup", ["source"] = dir, ["label"] = "before test" }), Ct);
        Assert.Equal(1, backup.Succeeded);
        var list = script.Run(env.Context(script, new() { ["mode"] = "list" }), Ct);
        Assert.Contains(list.Table!.Rows, r => r[1] == "before test");
        File.WriteAllText(Path.Combine(dir, "a.txt"), "v2");
        string setId = env.Store.List().Single().Id;
        int prompts = 0;
        var context = env.Context(script, new() { ["mode"] = "restore", ["set"] = setId }, (_, _, _) => { prompts++; return new ConflictAnswer(ConflictChoice.Overwrite, false); });
        var preview = script.Run(context, Ct);
        Assert.StartsWith("EXISTS", preview.Plan!.Single().Detail);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(dir, "a.txt")));
        var restored = script.Apply(context, preview.Plan!, Ct);
        Assert.Equal(1, restored.Succeeded);
        Assert.Equal(1, prompts);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(dir, "a.txt")));
    }

    [Fact]
    public void AuditorChecksCatalogsAndManifests()
    {
        var env = new Env();
        string assets = SyntheticAssets.Create(Path.Combine(env.Root, "client", "assets"));
        var sheet = Directory.GetFiles(assets, "sprites-*.bmp.lzma").First();
        File.Delete(sheet);
        File.WriteAllText(Path.Combine(assets, "stray.txt"), "");
        var script = new TibiaProjectAuditorScript();
        var result = script.Run(env.Context(script, new() { ["folder"] = Path.Combine(env.Root, "client") }), Ct);
        Assert.Contains(result.Table!.Rows, r => r[0] == "Missing (listed in catalog)" && r[1].EndsWith(Path.GetFileName(sheet), StringComparison.Ordinal));
        Assert.Contains(result.Table!.Rows, r => r[0] == "Empty file" && r[1].EndsWith("stray.txt", StringComparison.Ordinal));
        Assert.Contains(result.Table!.Summary, s => s.StartsWith("Client sprite sheet: 3", StringComparison.Ordinal));
        Assert.True(result.Failed >= 2);
    }

    [Fact]
    public void HistoryKeepsRunsWithoutFileContentsAndCanBeCleared()
    {
        var env = new Env();
        var history = new ScriptHistory(Path.Combine(env.Root, "history.json"));
        for (int i = 0; i < 15; i++)
            history.Add(new ScriptHistoryEntry { ScriptId = "x", ScriptName = "X", Summary = $"run {i}", StartedUtc = DateTime.UtcNow }, maxEntries: 10);
        var entries = history.Load();
        Assert.Equal(10, entries.Count);
        Assert.Equal("run 14", entries[0].Summary);
        history.Clear();
        Assert.Empty(history.Load());
    }
}
