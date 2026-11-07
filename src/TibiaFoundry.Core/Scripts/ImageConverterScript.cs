using TibiaFoundry.Core.Common;
using System.Collections.Concurrent;
using System.Globalization;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Tools;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Scripts;

/// <summary>Script 8: converts and resizes images in bulk.</summary>
public sealed class ImageConverterScript : Script
{
    public override string Id => "image-convert";
    public override string Name => L.T("ImageConverterScript.BatchImageConverterAndResizer");

    public override IReadOnlyList<ToolDependency> Dependencies { get; } =
    [
        new("imagemagick", L.T("ImageConverterScript.ImageMagickMagickNETBundled"), DependencyKind.Library, Required: true,
            L.T("ImageConverterScript.ItIsPartOfTibiaFoundry")) { MinimumVersion = "14.0" },
    ];
    public override string Description => L.T("ImageConverterScript.ConvertsImagesBetweenPNGGIFBMP");
    public override ScriptCategory Category => ScriptCategory.ImageUtilities;
    public override string SupportedInputs => L.T("ImageConverterScript.PNGGIFAlsoAnimatedBMPJPEG");
    public override string Limitations => L.T("ImageConverterScript.JPEGOutputLosesQualityAndTransparency");

    public override IReadOnlyList<ScriptParameter> Parameters { get; } =
    [
        new("input", L.T("Common.Images"), ScriptParameterKind.Path, L.T("ImageConverterScript.AnImageFileOrAFolder")),
        RecursiveParameter(false),
        new("output", L.T("Common.OutputFolder"), ScriptParameterKind.OutputFolder, L.T("ImageConverterScript.WhereTheConvertedImagesAreWritten")),
        new("format", L.T("Common.Format"), ScriptParameterKind.Choice, L.T("ImageConverterScript.FormatOfTheNewFiles"))
        {
            Default = "Png",
            Choices = [new("Png", L.T("ImageConverterScript.PNGLosslessWithAttribution")), new("Gif", L.T("ImageConverterScript.GIFKeepsAnimationsWithAttribution")), new("Bmp", L.T("ImageConverterScript.BMPLossless")), new("WebPLossless", L.T("ImageConverterScript.WebPLossless")), new("Jpeg", L.T("ImageConverterScript.JPEGLossyNoTransparency"))],
        },
        new("resize", L.T("Common.Size"), ScriptParameterKind.Choice, L.T("ImageConverterScript.ResizeTheImages"))
        {
            Default = "none",
            Choices = [new("none", L.T("ImageConverterScript.KeepTheSize")), new("percent", L.T("ImageConverterScript.ScaleByPercentage")), new("exact", L.T("ImageConverterScript.ExactWidthAndHeight")), new("fit", L.T("ImageConverterScript.FitWithinWidthAndHeightKeeps"))],
        },
        new("percent", L.T("Label.Percentage"), ScriptParameterKind.Integer, L.T("ImageConverterScript.100Unchanged200TwiceAsLarge")) { Default = "200", Minimum = 1, Maximum = 10000, VisibleWhen = "resize=percent" },
        new("width", L.T("Label.Width"), ScriptParameterKind.Integer, L.T("ImageConverterScript.InPixels")) { Default = "64", Minimum = 1, Maximum = 16384, VisibleWhen = "resize=exact|fit" },
        new("height", L.T("Label.Height"), ScriptParameterKind.Integer, L.T("ImageConverterScript.InPixels")) { Default = "64", Minimum = 1, Maximum = 16384, VisibleWhen = "resize=exact|fit" },
        new("filter", L.T("Label.Scaling"), ScriptParameterKind.Choice, L.T("ImageConverterScript.NearestNeighborKeepsPixelArtSharp"))
        {
            Default = "Nearest",
            Choices = [new("Nearest", L.T("ImageConverterScript.NearestNeighborPixelArt")), new("Bilinear", L.T("Label.Bilinear")), new("Bicubic", L.T("Label.Bicubic")), new("Lanczos3", L.T("ImageConverterScript.LanczosSharpestSmooth"))],
        },
        new("allowLossy", L.T("ImageConverterScript.AllowLossyJPEG"), ScriptParameterKind.Boolean, L.T("ImageConverterScript.JPEGOutputIsOnlyWrittenWhen")) { Default = "false", VisibleWhen = "format=Jpeg" },
        new("quality", L.T("ImageConverterScript.JPEGQuality"), ScriptParameterKind.Integer, "1–100.") { Default = "92", Minimum = 1, Maximum = 100, Advanced = true, VisibleWhen = "format=Jpeg" },
        new("attribution", L.T("Common.Attribution"), ScriptParameterKind.Choice, L.T("ImageConverterScript.WhoTheResultsCreditPNGAnd"))
        {
            Default = "keep", Advanced = true,
            Choices = [new("keep", L.T("ImageConverterScript.KeepTheOriginalCreatorsRecommended")), new("me", L.T("ImageConverterScript.AttributeToMeOnlyForMy"))],
        },
    ];

    public override IReadOnlyList<string> Validate(ScriptArguments args)
    {
        var problems = base.Validate(args).ToList();
        if (problems.Count > 0) return problems;
        string input = args.PathValue("input")!, output = args.PathValue("output")!;
        string inputFolder = Directory.Exists(input) ? input : Path.GetDirectoryName(input)!;
        if (string.Equals(PathGuard.Normalize(inputFolder), PathGuard.Normalize(output), StringComparison.OrdinalIgnoreCase))
            problems.Add(L.T("ImageConverterScript.OutputFolderChooseAFolderOther"));
        if (args.Choice("format") == "Jpeg" && !args.Bool("allowLossy"))
            problems.Add(L.T("ImageConverterScript.JPEGLosesQualityAndTransparencyTick"));
        return problems;
    }

    public override ScriptResult Run(ScriptContext context, CancellationToken ct)
    {
        var args = context.Args;
        string input = args.PathValue("input")!;
        string output = args.PathValue("output")!;
        var format = Enum.Parse<ImageOutputFormat>(args.Choice("format"));
        var filter = Enum.Parse<ResampleFilter>(args.Choice("filter"));
        string root;
        List<FileInfo> files;
        var result = new ScriptResult { OutputFolder = output };
        if (File.Exists(input))
        {
            root = Path.GetDirectoryName(input)!;
            files = [new FileInfo(input)];
        }
        else
        {
            root = input;
            var scan = FileScanner.Scan(input, args.Bool("recursive"), ct: ct);
            // Never read the output folder's own results when it lies inside the input folder.
            files = scan.Files.Where(f => ImageFile.ReadableExtensions.Contains(f.Extension) && !PathGuard.IsSameOrInside(output, f.FullName)).ToList();
            result.Notes.AddRange(FileScanner.Notes(scan));
        }
        var table = new ReportTable(L.T("ImageConverterScript.ConvertedImages"), [L.T("Common.Source"), L.T("Common.Result"), L.T("Common.Size"), L.T("Common.Note")]);
        var rows = new ConcurrentDictionary<int, string[]>();
        var errors = new ConcurrentBag<string>();
        using var session = context.CreateOutput("Image converter");
        session.Protect(files.Select(f => f.FullName));
        int done = 0, written = 0, skipped = 0;
        var choice = args.Choice("attribution") == "me" ? AttributionChoice.AttributeToMe : AttributionChoice.KeepOriginalCreators;
        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = context.WorkerThreads, CancellationToken = ct }, (i, state) =>
        {
            var file = files[i];
            try
            {
                byte[] data = File.ReadAllBytes(file.FullName);
                var image = ImageFile.Decode(data);
                string note = "";
                if (image.IsAnimated && format != ImageOutputFormat.Gif)
                {
                    rows[i] = [file.FullName, "", "", L.F("ImageConverterScript.SkippedAnimated0FramesOnlyGIF", image.Frames.Count)];
                    Interlocked.Increment(ref skipped);
                    return;
                }
                var (w, h) = TargetSize(args, image.First.Width, image.First.Height);
                var frames = image.Frames.Select(f => w == f.Width && h == f.Height ? f : ImageResampler.Resize(f, w, h, filter)).ToList();
                if (format == ImageOutputFormat.Jpeg && frames[0].Pixels.Any(p => p >> 24 != 255)) note = L.T("ImageConverterScript.TransparencyReplacedByWhiteJPEG");
                if (format == ImageOutputFormat.Gif && frames.Any(f => f.Pixels.Any(p => p >> 24 is > 0 and < 255))) note = L.T("ImageConverterScript.PartialTransparencyReducedToOnOff");
                var attribution = Attribution.ForDerivedAsset([AttributionReader.Read(data)], choice, copyrightIfUnknown: null, action: $"Converted to {format}{(w != image.First.Width || h != image.First.Height ? $" at {w}×{h}" : "")}");
                byte[] encoded = ImageFile.Encode(frames, image.DelaysMs, format, attribution, args.Int("quality", 92));
                string relative = Path.ChangeExtension(Path.GetRelativePath(root, file.FullName), ImageFile.ExtensionOf(format));
                string target = PathGuard.ResolveInside(output, relative);
                string? path = session.WriteAllBytes(target, encoded, temp =>
                {
                    var check = ImageFile.Decode(File.ReadAllBytes(temp));
                    if (check.First.Width != w || check.First.Height != h) throw new InvalidDataException("The written image has the wrong size.");
                });
                if (path == null)
                {
                    rows[i] = [file.FullName, "", "", L.T("ImageConverterScript.SkippedTheResultFileAlreadyExisted")];
                    Interlocked.Increment(ref skipped);
                    return;
                }
                Interlocked.Increment(ref written);
                rows[i] = [file.FullName, path, $"{w}×{h}", note];
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ImageMagick.MagickException or ArgumentException or UnauthorizedAccessException or AttributionException)
            {
                errors.Add($"{file.FullName}: {ex.Message}");
                rows[i] = [file.FullName, "", "", "failed: " + ex.Message];
                if (context.StopOnError) state.Stop();
            }
            finally
            {
                int n = Interlocked.Increment(ref done);
                if (n % 10 == 0) context.Report(n, files.Count, "converting");
            }
        });
        foreach (var row in rows.OrderBy(r => r.Key)) table.Add(row.Value);
        result.Table = table;
        result.Processed = files.Count;
        result.Succeeded = written;
        result.Failed = errors.Count;
        result.Errors.AddRange(errors);
        result.ChangesApplied = written > 0;
        result.BackupFolder = session.BackupFolder;
        result.Summary = L.F("ImageConverterScript.0N0Of1ConvertedTo", written, L.Count(files.Count, "Noun.image"), format, (skipped > 0 ? L.F("ImageConverterScript.0N0Skipped", skipped) : ""), (!errors.IsEmpty ? L.F("ImageConverterScript.0N0Failed", errors.Count) : ""), session.Describe());
        if (format is ImageOutputFormat.Bmp or ImageOutputFormat.WebPLossless or ImageOutputFormat.Jpeg)
            result.Notes.Add(L.F("ImageConverterScript.0FilesCarryNoCreatorAttribution", format));
        return result;
    }

    private static (int Width, int Height) TargetSize(ScriptArguments args, int width, int height)
    {
        switch (args.Choice("resize"))
        {
            case "percent":
                double f = args.Long("percent", 100) / 100.0;
                return (Math.Max(1, (int)Math.Round(width * f)), Math.Max(1, (int)Math.Round(height * f)));
            case "exact":
                return (args.Int("width", width), args.Int("height", height));
            case "fit":
                double scale = Math.Min((double)args.Int("width", width) / width, (double)args.Int("height", height) / height);
                return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
            default:
                return (width, height);
        }
    }
}
