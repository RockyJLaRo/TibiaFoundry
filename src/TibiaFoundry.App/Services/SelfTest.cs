using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Common;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Library;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.Rcc;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Sounds;
using TibiaFoundry.Core.Sprites;

namespace TibiaFoundry.App.Services;

/// <summary>
/// <c>--self-test &lt;folder&gt;</c>: checks, without opening a window, that every bundled component
/// works in this build: data folder, WPF/XAML resources and rendering, LZMA + sprite decoding,
/// GDI+ PNG, protobuf, the outfit renderer, ImageMagick GIF writing, sheet encoding, Qt resource
/// files (zlib) and the audio decoders; then the application self-check of the Diagnostics page
/// (settings, output folders, dependencies of every registered tool), which only reads.
/// Writes <c>self-test.txt</c> to the folder and returns the number of failed checks. Checks that
/// need the Tibia client are skipped (not failed) when it is not installed.
/// </summary>
public static class SelfTest
{
    private sealed class SkipException(string reason) : Exception(reason);

    public static async Task<int> RunAsync(string folder, AppPaths paths)
    {
        Directory.CreateDirectory(folder);
        var report = new StringBuilder();
        int failures = 0;
        report.AppendLine($"{AppIdentity.NameAndVersion} self-test {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Program: {Environment.ProcessPath}");
        report.AppendLine($"Runtime: .NET {Environment.Version}, {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}, single-file: {!File.Exists(Path.Combine(AppContext.BaseDirectory, "TibiaFoundry.dll"))}");
        report.AppendLine($"Mode: {(paths.IsPortable ? "portable" : "per-user")}; settings {paths.SettingsFile}; logs {paths.LogDirectory}");
        if (paths.Notice != null) report.AppendLine("Notice: " + paths.Notice);
        report.AppendLine();

        async Task Check(string name, Func<Task<string>> body)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                string detail = await body();
                report.AppendLine($"PASS  {name}  ({detail}; {watch.ElapsedMilliseconds} ms)");
            }
            catch (SkipException skip)
            {
                report.AppendLine($"SKIP  {name}  ({skip.Message})");
            }
            catch (Exception ex)
            {
                failures++;
                report.AppendLine($"FAIL  {name}  ({ex.GetType().Name}: {ex.Message})");
                AppLog.Error($"Self-test '{name}' failed.", ex);
            }
        }
        Task<string> Sync(Func<string> body) => Task.FromResult(body());

        // The live client at the locations chosen in Settings (read only; nothing is saved).
        var (clientSettings, _, _) = new SettingsStore(paths.SettingsFile).Load(previousLocations: paths.PreviousSettingsFiles);
        var locations = clientSettings.LocationsFor(ClientScope.Live);
        string assetsFolder = locations.Resolve(LocationIds.Assets).DisplayPath;
        AssetCatalog? catalog = null;
        AppearanceDatabase? appearances = null;
        void RequireClient()
        {
            if (!locations.Resolve(LocationIds.Assets).Found) throw new SkipException("Tibia client files not found at " + assetsFolder);
        }

        await Check("data folder is writable", () => Sync(() =>
        {
            Directory.CreateDirectory(paths.DataDirectory);
            if (!Core.FileOps.OutputLocations.CanWrite(paths.DataDirectory, out string? problem)) throw new IOException(problem);
            var (_, status, _) = new SettingsStore(paths.SettingsFile).Load();
            return $"{paths.DataDirectory}, settings {status}";
        }));

        await Check("WPF resources and rendering", () => Sync(() =>
        {
            var dark = new ResourceDictionary { Source = new Uri("pack://application:,,,/Themes/Colors.Dark.xaml") };
            var light = new ResourceDictionary { Source = new Uri("pack://application:,,,/Themes/Colors.Light.xaml") };
            if (dark["App.Accent"] is not SolidColorBrush || light["App.Accent"] is not SolidColorBrush) throw new InvalidOperationException("palette brushes missing");
            var logo = new BitmapImage(new Uri("pack://application:,,,/Assets/Branding/tibia-foundry-logo.png"));
            if (logo.PixelWidth != 32 || logo.PixelHeight != 32) throw new InvalidOperationException($"logo is {logo.PixelWidth}x{logo.PixelHeight}, expected 32x32");
            var text = new TextBlock { Text = AppIdentity.Name, FontSize = 20, Foreground = Brushes.Black, Background = Brushes.White };
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            text.Arrange(new Rect(text.DesiredSize));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(text.ActualWidth), (int)Math.Ceiling(text.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(text);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            int dark0 = 0;
            for (int i = 0; i < pixels.Length; i += 4) if (pixels[i] < 128) dark0++;
            if (dark0 == 0) throw new InvalidOperationException("text did not render");
            return $"themes loaded, logo {logo.PixelWidth}x{logo.PixelHeight}, rendered {bitmap.PixelWidth}x{bitmap.PixelHeight} with {dark0} text pixels";
        }));

        await Check("client catalog", () => Sync(() =>
        {
            RequireClient();
            catalog = AssetCatalog.Load(assetsFolder);
            return $"{catalog.SheetCount:N0} sheets, {catalog.TotalSprites:N0} sprites";
        }));

        await Check("LZMA sprite sheet decode + PNG round trip (GDI+)", () => Task.Run(() =>
        {
            RequireClient();
            if (catalog == null) throw new SkipException("catalog unavailable");
            var sheet = catalog.Sheets.First(s => s.Type == SpriteSheetType.Large);
            var sprites = SpriteSheetDecoder.DecodeSprites(catalog, sheet);
            string png = Path.Combine(folder, $"sprite_{sheet.FirstSpriteId}.png");
            ImageIO.SavePng(sprites[0], png);
            if (!sprites[0].PixelEquals(ImageIO.Load(png))) throw new InvalidOperationException("PNG round trip changed pixels");
            return $"sheet {sheet.FirstSpriteId}-{sheet.LastSpriteId}, {sprites.Length} sprites";
        }));

        await Check("appearances (protobuf)", () => Task.Run(() =>
        {
            RequireClient();
            string file = locations.PathOf(LocationIds.Appearances) ?? throw new FileNotFoundException("appearances file not found");
            appearances = AppearanceDatabase.Load(file);
            return $"{appearances.List(AppearanceKind.Object).Count:N0} objects, {appearances.List(AppearanceKind.Outfit).Count:N0} outfits";
        }));

        await Check("outfit render + GIF (ImageMagick)", () => Task.Run(() =>
        {
            RequireClient();
            if (catalog == null || appearances == null) throw new SkipException("appearances unavailable");
            var repo = new SpriteRepository(catalog, null, SpriteSourceMode.ClientOnly);
            var frames = OutfitRenderer.Render(appearances, repo, 128, new OutfitRenderOptions
            {
                FrameGroup = 1, Direction = (int)Direction.South,
                Colors = (int[])GameTables.DefaultOutfitColors.Clone(), MountColors = (int[])GameTables.DefaultOutfitColors.Clone(),
            });
            string gif = Path.Combine(folder, "outfit_128.gif");
            GifWriter.Write(gif, frames, Enumerable.Repeat(100, frames.Count).ToList());
            using var read = new MagickImageCollection(gif);
            if (read.Count != frames.Count) throw new InvalidOperationException($"GIF has {read.Count} frames, expected {frames.Count}");
            return $"{frames.Count} frames, Magick.NET {typeof(MagickImage).Assembly.GetName().Version}";
        }));

        await Check("creator attribution in exported files", () => Task.Run(() =>
        {
            var image = new RgbaImage(4, 4);
            image.Clear(0xFF336699);
            string png = Path.Combine(folder, "attribution.png"), gif = Path.Combine(folder, "attribution.gif");
            ImageIO.SavePng(image, png);
            GifWriter.Write(gif, [image, image], [100, 100]);
            string expected = Attribution.TextFor(Attribution.CurrentCreator);
            if (PngMetadata.Read(File.ReadAllBytes(png))?.Text != expected) throw new InvalidOperationException("PNG attribution missing");
            if (GifMetadata.Read(File.ReadAllBytes(gif))?.Text != expected) throw new InvalidOperationException("GIF attribution missing");
            using (var read = new MagickImage(gif))
                if (read.Comment?.Contains(expected, StringComparison.Ordinal) != true) throw new InvalidOperationException("ImageMagick does not see the GIF comment");
            if (!image.PixelEquals(ImageIO.Load(png))) throw new InvalidOperationException("metadata changed the PNG pixels");
            return $"PNG and GIF carry \"{expected}\"";
        }));

        await Check("Asset Library (SQLite, indexing, search)", () => Task.Run(() =>
        {
            // A throwaway library in the test folder: the native SQLite library and the indexer must work in this build.
            string libraryFolder = Path.Combine(folder, "library-check");
            if (Directory.Exists(libraryFolder)) Directory.Delete(libraryFolder, true);
            var library = AssetLibrary.Open(libraryFolder);
            var files = Directory.GetFiles(folder, "*.png").Concat(Directory.GetFiles(folder, "*.gif")).ToList();
            var result = AssetIndexer.IndexFiles(library, files);
            int found = library.Search(new AssetQuery { Text = "attribution" }).Total;
            AssetLibrary.ReleaseConnections();
            if (result.Added != files.Count || found != 2) throw new InvalidOperationException($"{result.Summary.TrimEnd('.')}; search found {found} of 2");
            return $"{result.Summary.TrimEnd('.')}; search found {found}";
        }));

        await Check("sheet encoding for the Assets Editor (BMP + LZMA)", () => Task.Run(() =>
        {
            RequireClient();
            if (catalog == null) throw new SkipException("catalog unavailable");
            var sheet = catalog.Sheets.First(s => s.Type == SpriteSheetType.Small);
            byte[] bmp = CipLzma.DecompressFile(catalog.GetSheetPath(sheet));
            if (!BmpEncoder.Encode(BmpDecoder.Decode(bmp)).AsSpan().SequenceEqual(bmp)) throw new InvalidOperationException("the re-encoded sheet differs from the client's");
            byte[] packed = CipLzma.Compress(bmp);
            if (!CipLzma.Decompress(packed).AsSpan().SequenceEqual(bmp)) throw new InvalidOperationException("the LZMA round trip changed the sheet");
            return $"sheet {sheet.FirstSpriteId}-{sheet.LastSpriteId} re-encoded byte for byte, {bmp.Length:N0} → {packed.Length:N0} bytes";
        }));

        await Check("Qt resource file (zlib)", () => Task.Run(() =>
        {
            RequireClient();
            string file = locations.PathOf(LocationIds.ResourceFile) ?? throw new SkipException("the client has no graphics_resources.rcc");
            byte[] data = File.ReadAllBytes(file);
            var archive = RccArchive.Parse(data);
            int read = archive.Entries.Count(e => e.CanReadContent && e.GetContent().Length >= 0);
            var report = RccValidator.Validate(data);
            if (!report.IsValid) throw new InvalidOperationException(report.Errors[0]);
            var rebuilt = RccValidator.Validate(archive.Build());
            if (!rebuilt.IsValid) throw new InvalidOperationException("rebuilt file: " + rebuilt.Errors[0]);
            return $"format {report.FormatVersion}, {read:N0} of {report.Files:N0} files read ({report.CompressedZlib} zlib), all found by Qt's lookup; rebuilt copy valid";
        }));

        await Check("file metadata (TagLib#, verified save)", () => Task.Run(() =>
        {
            // A JPEG made here: written through a copy, read back, then removed again. Nothing of the user's is touched.
            string dir = Path.Combine(Path.GetTempPath(), $"TibiaFoundry-selftest-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            try
            {
                string file = Path.Combine(dir, "sample.jpg");
                using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.SteelBlue, 16, 16)) image.Write(file, ImageMagick.MagickFormat.Jpeg);
                var engine = new Core.FileMetadata.MetadataEngine();
                var result = engine.Save(engine.Load(file), [new("title", "Self-test"), new("keywords", "tibia; test")], new Core.FileMetadata.MetadataSaveOptions());
                if (result.Status != Core.FileMetadata.MetadataSaveStatus.Saved) throw new InvalidOperationException(result.Message);
                var read = engine.Load(file);
                if (read.Value("title") != "Self-test") throw new InvalidOperationException("the title did not read back");
                return $"JPEG title and keywords written and verified (TagLib# {typeof(TagLib.File).Assembly.GetName().Version?.ToString(3)}, {engine.Providers.Count} readers)";
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (IOException) { }
            }
        }));

        await Check("sound decoders (NAudio + NVorbis)", () => Task.Run(() =>
        {
            RequireClient();
            string file = locations.PathOf(LocationIds.Sounds) ?? throw new SkipException("no sounds file");
            var sounds = SoundDatabase.Load(file);
            var played = new List<string>();
            foreach (var item in sounds.Items(SoundListKind.Sounds))
            {
                if (item.SoundId is not uint id || sounds.ResolvePlayableFile(id, null) is not { } path) continue;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (played.Contains(ext)) continue;
                using var reader = AudioPlayer.OpenReader(path);
                var buffer = new byte[reader.WaveFormat.AverageBytesPerSecond / 10 + 4];
                int read = reader.Read(buffer, 0, buffer.Length - buffer.Length % reader.WaveFormat.BlockAlign);
                if (read <= 0) throw new InvalidOperationException($"no audio decoded from {Path.GetFileName(path)}");
                played.Add(ext);
                if (played.Count == 2) break;
            }
            if (played.Count == 0) throw new InvalidOperationException("no playable sound found");
            return "decoded " + string.Join(" and ", played) + " (no audio output)";
        }));

        await Check("application self-check (settings, folders, tools and dependencies)", () => Sync(() =>
        {
            // Read-only: the settings are loaded (and upgraded in memory) but never saved here.
            var (settings, status, _) = new SettingsStore(paths.SettingsFile).Load(previousLocations: paths.PreviousSettingsFiles);
            using var services = new AppServices(new SettingsStore(paths.SettingsFile), settings) { Paths = paths, PersistSettings = false };
            foreach (var page in BuiltInTools.Pages) services.Tools.TryRegister(page.Manifest, out _);
            foreach (var script in Core.Scripts.ScriptRegistry.CreateAll(() => services.Backups, services.RenameRecordFolder)) services.Tools.TryRegister(script.Manifest, out _);
            var results = HealthCheck.Run(services, status, readOnlyRun: true);
            foreach (var r in results.Where(r => r.Severity is HealthSeverity.Warning or HealthSeverity.Error))
                report.AppendLine($"      {r.SeverityText}: {r.Area} / {r.Check}: {r.Message}");
            // Problems of this installation's configuration are reported above; only a broken
            // component or tool of the build counts as a self-test failure.
            var broken = results.Where(r => r.Area is "Subsystems" or "Tools" && r.Severity == HealthSeverity.Error).ToList();
            if (broken.Count > 0) throw new InvalidOperationException(string.Join("; ", broken.Select(b => $"{b.Check}: {b.Message}")));
            return $"{results.Count} checks: {Plural.Count(results.Count(r => r.Severity == HealthSeverity.Error), "error")}, {Plural.Count(results.Count(r => r.Severity == HealthSeverity.Warning), "warning")}, " +
                   $"{services.Tools.Tools.Count} tools registered";
        }));

        await Check("client data compatibility", () => Task.Run(() =>
        {
            RequireClient();
            var compatibility = CompatibilityChecker.Check(locations);
            report.AppendLine();
            report.AppendLine(compatibility.ToText().TrimEnd());
            report.AppendLine();
            // Missing or unreadable data is a property of the client installation, not of this build:
            // it is reported in full above, and only an unexpected exception counts as a self-test failure.
            return $"{CompatibilityReport.Describe(compatibility.Overall)}, client {compatibility.ClientVersion ?? "unknown"}";
        }));

        report.AppendLine();
        report.AppendLine($"Failures: {failures}");
        File.WriteAllText(Path.Combine(folder, "self-test.txt"), report.ToString());
        AppLog.Info($"Self-test finished with {Plural.Count(failures, "failure")}.");
        return failures;
    }
}
