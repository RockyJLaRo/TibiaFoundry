using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Legacy;

namespace TibiaFoundry.Tests;

/// <summary>
/// Compares the Client Converter's output with a Tibia.dat/Tibia.spr pair made by another converter
/// (for example Arch-Mina's ClientConverter) from the installed client or a nearby version. Opt-in:
/// set TF_REFERENCE_LEGACY to the folder with the reference pair. The reference files are only read.
/// </summary>
/// <remarks>
/// When the reference was made from another client version, objects that changed between the
/// versions differ too; the report lists every kind of difference so they can be reviewed. The
/// "exact size" of large objects is reported separately: converters derive it differently.
/// </remarks>
public sealed class ReferenceConversionTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void ConversionAgreesWithAReferenceConversion()
    {
        string? reference = TestData.Setting("REFERENCE_LEGACY");
        Assert.SkipWhen(string.IsNullOrEmpty(reference), "Set TF_REFERENCE_LEGACY to a folder with a Tibia.dat/Tibia.spr pair made by another converter.");
        string refDat = Path.Combine(reference!, "Tibia.dat"), refSpr = Path.Combine(reference!, "Tibia.spr");
        Assert.True(File.Exists(refDat) && File.Exists(refSpr), $"{reference} does not contain Tibia.dat and Tibia.spr.");
        string assets = TestData.RequireClientAssets();

        var detected = LegacyDatCodec.Detect(File.ReadAllBytes(refDat));
        Assert.True(detected.HasValue, "The reference Tibia.dat was not recognized.");
        var (theirs, profile) = detected.Value;
        output.WriteLine($"Reference: {refDat} ({new FileInfo(refDat).Length:N0} bytes), {refSpr} ({new FileInfo(refSpr).Length:N0} bytes), format {profile.Name}");

        string folder = TestData.NewTempDirectory("reference-conversion");
        try
        {
            var temp = new TempWorkspace(Path.Combine(folder, "_tmp"));
            using var session = new OutputSession("convert", FileSafetyOptions.Default with { WhenExists = ExistingFilePolicy.Overwrite });
            var result = ClientConverter.Run(new ClientConversionOptions
            {
                AssetsFolder = assets, OutputFolder = folder, Profile = profile, WorkerThreads = Environment.ProcessorCount,
                MaxMarketNameLength = ClientConversionOptions.DefaultMarketNameLimit(profile),
                MaxMarketCategory = ClientConversionOptions.LegacyMarketCategoryLimit,      // the page's default
            }, session, temp, ct: Ct);
            Assert.Empty(result.Failures);
            output.WriteLine($"Ours: {result.DatPath} and {result.SprPath}; {result.Validation}");
            var ours = LegacyDatCodec.Read(result.DatPath!, profile);

            int compared = 0, sameAttributes = 0, sameOrder = 0, sameGeometry = 0, exactSizeOnly = 0, animationDiffers = 0;
            var onlyOurs = new Dictionary<string, int>();
            var onlyTheirs = new Dictionary<string, int>();
            var marketFields = new Dictionary<string, int>();
            var marketExamples = new List<string>();
            var categoryPairs = new Dictionary<string, int>();
            var sample = new List<(LegacyThing Ours, LegacyThing Theirs)>();
            foreach (var category in Enum.GetValues<LegacyCategory>())
            {
                foreach (var their in theirs.List(category))
                {
                    var our = ours.Find(category, their.Id);
                    if (our == null) continue;
                    compared++;
                    // Clients read attributes in any order, so the order is reported but not compared.
                    if (our.Attributes.SequenceEqual(their.Attributes)) sameOrder++;
                    if (our.Attributes.Count == their.Attributes.Count && !our.Attributes.Except(their.Attributes).Any() && !their.Attributes.Except(our.Attributes).Any()) sameAttributes++;
                    else
                    {
                        foreach (var a in our.Attributes.Except(their.Attributes)) Count(onlyOurs, a.Attribute.ToString());
                        foreach (var a in their.Attributes.Except(our.Attributes)) Count(onlyTheirs, a.Attribute.ToString());
                        if (our.Get(LegacyAttribute.Market)?.Market is { } m1 && their.Get(LegacyAttribute.Market)?.Market is { } m2 && !m1.Equals(m2))
                        {
                            string field = m1.Category != m2.Category ? "category" : m1.TradeAs != m2.TradeAs ? "trade as" : m1.ShowAs != m2.ShowAs ? "show as"
                                : !m1.Name.AsSpan().SequenceEqual(m2.Name) ? "name" : m1.Vocation != m2.Vocation ? "vocation" : "level";
                            Count(marketFields, field);
                            if (m1.Category != m2.Category) Count(categoryPairs, $"{m1.Category}→{m2.Category}");
                            if (marketExamples.Count < 12)
                                marketExamples.Add($"{their.Id}: ours ({m1.Category}, {m1.TradeAs}, {m1.ShowAs}, \"{m1.NameText}\", voc {m1.Vocation}, lvl {m1.Level}) / reference ({m2.Category}, {m2.TradeAs}, {m2.ShowAs}, \"{m2.NameText}\", voc {m2.Vocation}, lvl {m2.Level})");
                        }
                    }
                    bool geometry = our.FrameGroups.Count == their.FrameGroups.Count && our.FrameGroups.Zip(their.FrameGroups).All(p => SameGeometry(p.First, p.Second, exactSize: true));
                    bool geometryButSize = !geometry && our.FrameGroups.Count == their.FrameGroups.Count && our.FrameGroups.Zip(their.FrameGroups).All(p => SameGeometry(p.First, p.Second, exactSize: false));
                    if (geometry) sameGeometry++;
                    if (geometryButSize) exactSizeOnly++;
                    if (our.FrameGroups.Count == their.FrameGroups.Count &&
                        our.FrameGroups.Zip(their.FrameGroups).Any(p => !(p.First.Animation == null ? p.Second.Animation == null : p.First.Animation.SameAs(p.Second.Animation))))
                        animationDiffers++;
                    if ((geometry || geometryButSize) && category == LegacyCategory.Item && their.Id % 19 == 0) sample.Add((our, their));
                }
            }

            // Pixels: every sprite of every 19th item whose layout agrees (sprite IDs may differ between converters).
            int spritesCompared = 0, spritesEqual = 0;
            var differing = new List<string>();
            using (var ourSpr = new LegacySprReader(result.SprPath!, profile))
            using (var theirSpr = new LegacySprReader(refSpr, profile))
            {
                foreach (var (our, their) in sample.Take(3000))
                {
                    bool equal = true;
                    foreach (var (g1, g2) in our.FrameGroups.Zip(their.FrameGroups))
                        for (int i = 0; i < g1.Sprites.Length; i++)
                        {
                            spritesCompared++;
                            if (SamePixels(ourSpr.Read(g1.Sprites[i]), theirSpr.Read(g2.Sprites[i]))) spritesEqual++;
                            else equal = false;
                        }
                    if (!equal && differing.Count < 20) differing.Add(their.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            output.WriteLine($"Objects compared (IDs in both files): {compared:N0} (ours {ours.All.Count():N0}, reference {theirs.All.Count():N0})");
            output.WriteLine($"Identical attributes: {sameAttributes:N0} ({100.0 * sameAttributes / compared:0.00} %); also in the same order: {sameOrder:N0}");
            output.WriteLine($"Identical layout and animation (incl. exact size): {sameGeometry:N0}; differing only in exact size: {exactSizeOnly:N0}");
            output.WriteLine($"Objects with different animation timing: {animationDiffers:N0}");
            output.WriteLine($"Attribute differences, only in ours: {Describe(onlyOurs)}");
            output.WriteLine($"Attribute differences, only in the reference: {Describe(onlyTheirs)}");
            output.WriteLine($"Market data that differs, by first differing field: {Describe(marketFields)}");
            output.WriteLine($"Market categories that differ (ours→reference): {Describe(categoryPairs)}");
            foreach (string example in marketExamples) output.WriteLine("    " + example);
            output.WriteLine($"Sprites compared pixel by pixel: {spritesCompared:N0} in {Math.Min(sample.Count, 3000):N0} items; identical {spritesEqual:N0}" +
                             (differing.Count > 0 ? $"; items with differences: {string.Join(", ", differing)}" : ""));

            Assert.True(compared > 1000, "Too few common objects: is the reference made from a Tibia client at all?");
            Assert.True(sameAttributes >= compared * 0.95, "Less than 95 % of the objects have identical attributes.");
            Assert.True(spritesCompared > 0 && spritesEqual >= spritesCompared * 0.99, "Less than 99 % of the compared sprites are identical.");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void Count(Dictionary<string, int> counts, string key) => counts[key] = counts.GetValueOrDefault(key) + 1;

    private static string Describe(Dictionary<string, int> counts) =>
        counts.Count == 0 ? "none" : string.Join(", ", counts.OrderByDescending(c => c.Value).Select(c => $"{c.Key} {c.Value:N0}"));

    private static bool SameGeometry(LegacyFrameGroup a, LegacyFrameGroup b, bool exactSize) =>
        a.Type == b.Type && a.Width == b.Width && a.Height == b.Height && (!exactSize || a.Width == 1 && a.Height == 1 || a.ExactSize == b.ExactSize) &&
        a.Layers == b.Layers && a.PatternX == b.PatternX && a.PatternY == b.PatternY && a.PatternZ == b.PatternZ && a.Phases == b.Phases &&
        a.Sprites.Length == b.Sprites.Length;

    /// <summary>Equal visible pixels; fully transparent pixels count as equal whatever their stored color.</summary>
    private static bool SamePixels(RgbaImage? a, RgbaImage? b)
    {
        if (a == null || b == null) return IsEmpty(a) && IsEmpty(b);
        if (a.Width != b.Width || a.Height != b.Height) return false;
        for (int i = 0; i < a.Pixels.Length; i++)
        {
            uint p = a.Pixels[i], q = b.Pixels[i];
            if (p >> 24 == 0 && q >> 24 == 0) continue;
            if (p != q) return false;
        }
        return true;
    }

    private static bool IsEmpty(RgbaImage? image) => image == null || image.Pixels.All(p => p >> 24 == 0);
}
