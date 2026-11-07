using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Net;

namespace TibiaFoundry.Tests;

/// <summary>The Tibia Foundry identity: names, metadata, logo, icon and creator links.</summary>
public sealed partial class BrandingTests(ITestOutputHelper output)
{
    /// <summary>SHA-256 of the official logo as supplied (32×32 RGBA PNG).</summary>
    private const string SuppliedLogoSha256 = "ec4968abea4c1cc75750e684b6a3af29c2fa234c327df1302a6ec0e1c44c8629";

    private static string SourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "TibiaFoundry.App"))) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "TibiaFoundry.App"))) return Path.Combine(dir.FullName, "src");
        }
        Assert.Skip("Source folder not found.");
        return "";
    }

    /// <summary>The version in src/Directory.Build.props, the single source of the version.</summary>
    private static string PropsVersion() =>
        System.Text.RegularExpressions.Regex.Match(File.ReadAllText(Path.Combine(SourceRoot(), "Directory.Build.props")), "<Version>([^<]+)</Version>").Groups[1].Value;

    private static string BrandingFolder => Path.Combine(SourceRoot(), "TibiaFoundry.App", "Assets", "Branding");

    /// <summary>The product name of version 3.0 (application code and XAML).</summary>
    [GeneratedRegex(@"tibia ?forge", RegexOptions.IgnoreCase)]
    private static partial Regex ObsoleteName();

    /// <summary>A XAML file with its {loc:Tr Key} references replaced by the English text (9.0: the text lives in the string table).</summary>
    private static string EnglishXaml(string file) =>
        Regex.Replace(File.ReadAllText(file), @"\{loc:Tr ([\w.-]+)\}", m => System.Security.SecurityElement.Escape(TibiaFoundry.Core.Localization.L.English[m.Groups[1].Value]));

    [Fact]
    public void FreshAndMigratedSettingsDoNotBringBackTheOldNames()
    {
        var fresh = new Core.Settings.AppSettings();
        fresh.Normalize();
        string json = System.Text.Json.JsonSerializer.Serialize(fresh, new System.Text.Json.JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
        Assert.DoesNotMatch(ObsoleteName(), json);

        string dir = TestData.NewTempDirectory("brand-migrate");
        string old = Path.Combine(dir, AppIdentity.Legacy.Version3Id, "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllText(old, """{ "Version": 1, "Theme": "Dark", "GifAuthor": "Dana", "OutputFolder": "D:\\Wiki" }""");
        var store = new Core.Settings.SettingsStore(Path.Combine(dir, AppIdentity.Id, "settings.json"));
        var (migrated, status, _) = store.Load(previousLocations: [old]);
        Assert.Equal(Core.Settings.SettingsLoadStatus.MigratedFromPreviousName, status);
        store.Save(migrated);
        string saved = File.ReadAllText(store.FilePath);
        Assert.DoesNotMatch(ObsoleteName(), saved);
        Assert.All(migrated.MigrationNotes, n => Assert.DoesNotMatch(ObsoleteName(), n));
    }

    [Fact]
    public void ObsoleteNamesRemainOnlyAsDocumentedMigrationReferences()
    {
        string root = SourceRoot();
        var problems = new List<string>();
        foreach (string project in new[] { "TibiaFoundry.App", "TibiaFoundry.Core" })
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, project), "*", SearchOption.AllDirectories)
                         .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                         .Where(f => f.EndsWith(".cs") || f.EndsWith(".xaml") || f.EndsWith(".csproj") || f.EndsWith(".manifest") || f.EndsWith(".pubxml") || f.EndsWith(".md") || f.EndsWith(".proto")))
            {
                string[] lines = File.ReadAllLines(file);
                int legacyStart = Path.GetFileName(file) == "AppIdentity.cs" ? Array.FindIndex(lines, l => l.Contains("class Legacy")) : -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (!ObsoleteName().IsMatch(line)) continue;
                    string trimmed = line.TrimStart();
                    bool allowed =
                        (legacyStart >= 0 && i > legacyStart && line.Contains("public const string", StringComparison.Ordinal))   // AppIdentity.Legacy: identifiers read for migration
                        || line.Contains("LegacyNamespace = ", StringComparison.Ordinal);                                           // XMP namespace of version 3.0, read only
                    if (!allowed) problems.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {trimmed}");
                }
            }
        }
        foreach (string p in problems) output.WriteLine(p);
        Assert.Empty(problems);
        string branding = Path.Combine(root, "TibiaFoundry.App", "Assets", "Branding");
        Assert.DoesNotContain(Directory.EnumerateFiles(branding), f => Path.GetFileName(f).Contains("forge", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(root, "TibiaFoundry.App", "Assets", "app.ico")));
        Assert.False(File.Exists(Path.Combine(root, "TibiaFoundry.App", "Assets", "tibiawiki_logo.png")));
    }

    [Fact]
    public void ApplicationIdentityAndFileMetadataAreTibiaFoundry()
    {
        Assert.Equal("Tibia Foundry", AppIdentity.Name);
        Assert.Equal("TibiaFoundry", AppIdentity.Id);
        Assert.Equal("RockyLaRo", AppIdentity.Creator);
        Assert.Equal("TibiaWiki Admin", AppIdentity.CreatorRole);
        Assert.Equal("https://tibia.fandom.com/", AppIdentity.Website);
        // Public versions are "9.0" or "9.0.1", never "9.0.0"; the padded form is only the Windows file version.
        Assert.Equal(PropsVersion(), AppIdentity.Version);
        Assert.Matches(@"^\d+\.\d+(\.[1-9]\d*)?$", AppIdentity.Version);

        var app = typeof(AppServices).Assembly;
        Assert.Equal("TibiaFoundry", app.GetName().Name);                                                      // TibiaFoundry.exe
        Assert.Equal("Tibia Foundry", app.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
        Assert.Equal("Tibia Foundry", app.GetCustomAttribute<AssemblyTitleAttribute>()!.Title);                 // Windows "File description"
        Assert.Equal("RockyLaRo", app.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company);
        Assert.Contains("RockyLaRo", app.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright);
        foreach (var text in new[] { app.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright, app.GetCustomAttribute<AssemblyDescriptionAttribute>()!.Description })
            Assert.DoesNotMatch(ObsoleteName(), text);
        var props = new Version(PropsVersion());
        var padded = new Version(props.Major, props.Minor, Math.Max(0, props.Build), 0);
        Assert.Equal(padded, app.GetName().Version);

        string manifest = File.ReadAllText(Path.Combine(SourceRoot(), "TibiaFoundry.App", "app.manifest"));
        Assert.Contains("name=\"TibiaFoundry.app\"", manifest);
        Assert.Contains($"<assemblyIdentity version=\"{padded}\"", manifest);
        string window = File.ReadAllText(Path.Combine(SourceRoot(), "TibiaFoundry.App", "Views", "MainWindow.xaml"));
        Assert.Contains("Title=\"Tibia Foundry\"", window);
        Assert.Contains("Icon=\"/Assets/Branding/tibia-foundry.ico\"", window);
        string project = File.ReadAllText(Path.Combine(SourceRoot(), "TibiaFoundry.App", "TibiaFoundry.App.csproj"));
        Assert.Contains(@"<ApplicationIcon>Assets\Branding\tibia-foundry.ico</ApplicationIcon>", project);
    }

    [Fact]
    public void TheSuppliedLogoIsUsedUnchanged()
    {
        string logo = Path.Combine(BrandingFolder, "tibia-foundry-logo.png");
        Assert.Equal(SuppliedLogoSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(logo))).ToLowerInvariant());
        var image = ImageIO.Load(logo);
        Assert.Equal((32, 32), (image.Width, image.Height));
        Assert.All(image.Pixels, p => Assert.True(p >> 24 is 0 or 255));                                     // hard-edged pixel art

        // Shown crisp: nearest-neighbor scaling wherever the logo is displayed.
        foreach (string xaml in new[] { Path.Combine("Views", "MainWindow.xaml"), Path.Combine("Views", "Pages", "AboutPage.xaml") })
        {
            string text = File.ReadAllText(Path.Combine(SourceRoot(), "TibiaFoundry.App", xaml));
            var tag = Regex.Match(text, "<Image[^>]*tibia-foundry-logo\\.png[^>]*>", RegexOptions.Singleline).Value;
            Assert.Contains("BitmapScalingMode=\"NearestNeighbor\"", tag);
        }
    }

    [Fact]
    public void TheIconAndLargeLogoAreNearestNeighborCopiesOfTheLogo()
    {
        var logo = ImageIO.Load(Path.Combine(BrandingFolder, "tibia-foundry-logo.png"));
        var colors = logo.Pixels.Where(p => p >> 24 != 0).ToHashSet();
        var entries = ReadIcon(File.ReadAllBytes(Path.Combine(BrandingFolder, "tibia-foundry.ico")));
        Assert.Equal([16, 24, 32, 48, 64, 128, 256], entries.Keys.Order());
        Assert.True(logo.PixelEquals(entries[32]));
        foreach (int size in new[] { 64, 128, 256 })
            Assert.True(entries[size].PixelEquals(Enlarge(logo, size / 32)), $"{size} px is not an exact {size / 32}x enlargement");
        foreach (var (size, image) in entries)
        {
            Assert.All(image.Pixels, p => Assert.True(p >> 24 is 0 or 255, $"{size} px: blended alpha"));
            Assert.All(image.Pixels.Where(p => p >> 24 != 0), p => Assert.Contains(p, colors));            // no new (blurred) colors
        }
        Assert.True(Enlarge(logo, 8).PixelEquals(ImageIO.Load(Path.Combine(BrandingFolder, "tibia-foundry-logo-256.png"))));
    }

    [Fact]
    public void CreatorLinksPointToTheIntendedProfiles()
    {
        string dir = TestData.NewTempDirectory("about");
        var settings = new Core.Settings.AppSettings { LoadDataAutomatically = false };
        settings.Normalize();
        using var services = new AppServices(new Core.Settings.SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var about = new AboutViewModel(services);
        Assert.Equal(
        [
            "https://tibia.fandom.com/wiki/User:RockyLaRo",
            "https://www.tibia.com/community/?name=Roc+ky",
            "https://www.youtube.com/@Roc_ky",
            "https://www.instagram.com/rocky_tibia/",
            "https://github.com/RockyJLaRo",
            "https://www.reddit.com/user/Roc-ky/",
        ], about.CreatorLinks.Select(l => l.Url));
        Assert.All(about.CreatorLinks, l =>
        {
            Assert.True(Uri.TryCreate(l.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps, l.Url);
            Assert.False(string.IsNullOrWhiteSpace(l.Label));
            Assert.StartsWith("BrandIcon.", l.IconKey);
        });
        // The redundant second TibiaWiki entry is gone; the wiki is linked from the profile, the tagline and the credits.
        Assert.Equal(["TibiaWiki Profile", "Tibia Character", "YouTube", "Instagram", "GitHub", "Reddit"], about.CreatorLinks.Select(l => l.Label));
        Assert.Single(about.CreatorLinks, l => l.Url.Contains("fandom.com", StringComparison.Ordinal));
        Assert.Equal("https://tibia.fandom.com/", ExternalEndpoints.TibiaWikiHome);

        // The two Tibia entries show the sites' own icons (largest frame up to 48 px); the others show badges.
        var wiki = about.CreatorLinks[0];
        var character = about.CreatorLinks[1];
        Assert.True(wiki.HasFavicon && character.HasFavicon);
        Assert.Equal(48, ((System.Windows.Media.Imaging.BitmapSource)wiki.Favicon!).PixelWidth);
        Assert.Equal(32, ((System.Windows.Media.Imaging.BitmapSource)character.Favicon!).PixelWidth);
        Assert.All(about.CreatorLinks.Skip(2), l => Assert.False(l.HasFavicon));

        // Every badge key exists in Themes/BrandIcons.xaml (the badge is also the fallback of a missing favicon).
        string icons = File.ReadAllText(Path.Combine(SourceRoot(), "TibiaFoundry.App", "Themes", "BrandIcons.xaml"));
        Assert.All(about.CreatorLinks, l => Assert.Contains($"x:Key=\"{l.IconKey}\"", icons));
    }

    /// <summary>SHA-256 of the site icons as served by tibia.fandom.com and tibia.com on 2026-10-13.</summary>
    private const string TibiaWikiFaviconSha256 = "fc3d35622bb6664c0681077b447bcf735376a6b971b05673bf00add65e9f76ab";
    private const string TibiaFaviconSha256 = "3d0b707dcea8507bea2156f7d6b5391b98113ec652591af8a6477f44b0a76b2a";

    [Fact]
    public void SiteIconsAreTheUnchangedFaviconsOfTheirSites()
    {
        string links = Path.Combine(SourceRoot(), "TibiaFoundry.App", "Assets", "Links");
        Assert.Equal(["tibia-favicon.ico", "tibiawiki-favicon.ico"], Directory.EnumerateFiles(links, "*.ico").Select(Path.GetFileName).Order());
        Assert.Equal(TibiaWikiFaviconSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(links, "tibiawiki-favicon.ico")))).ToLowerInvariant());
        Assert.Equal(TibiaFaviconSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(links, "tibia-favicon.ico")))).ToLowerInvariant());
        Assert.Contains("<Resource Include=\"Assets\\Links\\*.ico\" />", File.ReadAllText(Path.Combine(SourceRoot(), "TibiaFoundry.App", "TibiaFoundry.App.csproj")));
        Assert.Equal(AboutViewModel.TibiaWikiFavicon, "Assets/Links/tibiawiki-favicon.ico");
        Assert.Equal(AboutViewModel.TibiaFavicon, "Assets/Links/tibia-favicon.ico");
    }

    [Fact]
    public void AMissingFaviconFallsBackToTheBadge()
    {
        Assert.Null(TibiaFoundry.App.Infrastructure.Images.TryLoadIcon("Assets/Links/does-not-exist.ico"));
        var link = new CreatorLink("Example", "detail", "https://example.com/", "BrandIcon.TibiaWiki", Favicon: null);
        Assert.False(link.HasFavicon);

        // The page shows the favicon when there is one and the badge otherwise.
        string page = EnglishXaml(Path.Combine(SourceRoot(), "TibiaFoundry.App", "Views", "Pages", "AboutPage.xaml"));
        Assert.Contains("Source=\"{Binding Favicon}\"", page);
        Assert.Contains("Visibility=\"{Binding HasFavicon, Converter={StaticResource BoolToVisibility}, ConverterParameter=Invert}\"", page);
    }

    /// <summary>
    /// The tagline next to the logo has the exact wording, and "TibiaWiki" links to the wiki's main page
    /// (main window header and About page).
    /// </summary>
    [Fact]
    public void TheTaglineLinksTibiaWikiToItsMainPage()
    {
        string window = File.ReadAllText(Path.Combine(SourceRoot(), "TibiaFoundry.App", "Views", "MainWindow.xaml"));
        var tagline = Regex.Match(window, "<TextBlock x:Name=\"Tagline\".*?</TextBlock>", RegexOptions.Singleline).Value;
        // Exact wording, then the link (the line break only keeps "Tibia-Tools" from wrapping at its hyphen).
        Assert.Contains("<Run Text=\"Tibia Foundry - All your \" /><LineBreak /><Run Text=\"Tibia-Tools for \" /><Hyperlink", tagline);
        Assert.Matches("<Hyperlink[^>]*OpenTibiaWikiCommand[^>]*><Run Text=\"TibiaWiki\" /></Hyperlink></TextBlock>$", tagline);
        // With compact navigation only the logo is shown; it carries the tagline as its tooltip.
        var logo = Regex.Match(window, "<Image[^>]*tibia-foundry-logo\\.png[^>]*>", RegexOptions.Singleline).Value;
        Assert.Contains("ToolTip=\"Tibia Foundry - All your Tibia-Tools for TibiaWiki\"", logo);
        string main = File.ReadAllText(Path.Combine(SourceRoot(), "TibiaFoundry.App", "ViewModels", "MainViewModel.cs"));
        Assert.Contains("OpenUrl(Core.Net.ExternalEndpoints.CreditTibiaWiki)", main);
        Assert.Equal("https://tibia.fandom.com/wiki/Main_Page", ExternalEndpoints.CreditTibiaWiki);

        string about = EnglishXaml(Path.Combine(SourceRoot(), "TibiaFoundry.App", "Views", "Pages", "AboutPage.xaml"));
        Assert.Matches("<Run Text=\"Tibia Foundry - All your Tibia-Tools for\" />\\s*<Hyperlink[^>]*CommandParameter=\"\\{Binding TaglineUrl\\}\"[^>]*>\\s*<Run Text=\"TibiaWiki\" />", about);
    }

    /// <summary>
    /// The open-source acknowledgments are their own section with exactly the three projects, their
    /// authors and addresses, and say that the projects do not endorse Tibia Foundry.
    /// </summary>
    [Fact]
    public void OpenSourceAcknowledgmentsAreSeparateFromTheCredits()
    {
        string dir = TestData.NewTempDirectory("acknowledgments");
        var settings = new Core.Settings.AppSettings { LoadDataAutomatically = false };
        settings.Normalize();
        using var services = new AppServices(new Core.Settings.SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var about = new AboutViewModel(services);
        Assert.Equal(
        [
            ("Nekiro's RCC Editor", "Nekiro", "https://github.com/nekiro/Nekiro-Rcc-Editor", "https://github.com/nekiro"),
            ("ClientConverter", "Arch-Mina", "https://github.com/Arch-Mina/ClientConverter", "https://github.com/Arch-Mina"),
            ("Assets Editor", "Arch-Mina", "https://github.com/Arch-Mina/Assets-Editor", "https://github.com/Arch-Mina"),
        ], about.Acknowledgments.Select(a => (a.Project, a.Author, a.ProjectUrl, a.AuthorUrl)));
        Assert.All(about.Acknowledgments, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Relation));
            Assert.False(string.IsNullOrWhiteSpace(a.License));
            Assert.Contains(a.ProjectUrl, a.AccessibleName);
        });
        Assert.Equal(["Tibia", "CipSoft", "TibiaWiki"], about.Credits.Select(c => c.Name));                     // credits unchanged

        string page = EnglishXaml(Path.Combine(SourceRoot(), "TibiaFoundry.App", "Views", "Pages", "AboutPage.xaml"));
        string credits = Regex.Match(page, "<GroupBox Header=\"Credits\".*?</GroupBox>", RegexOptions.Singleline).Value;
        string acknowledgments = Regex.Match(page, "<GroupBox Header=\"Open-source acknowledgments\".*?</GroupBox>", RegexOptions.Singleline).Value;
        Assert.Contains("ItemsSource=\"{Binding Acknowledgments}\"", acknowledgments);
        Assert.DoesNotContain("Acknowledgments", credits);
        Assert.DoesNotContain("Nekiro", credits);
        Assert.Contains("do not endorse it", acknowledgments);
        Assert.Contains("includes no code from them", acknowledgments);
    }

    /// <summary>
    /// About → Credits lists exactly Tibia, CipSoft and TibiaWiki with the specified addresses and their
    /// official logos (see Assets/Credits/README.md), kept apart from the creator profile.
    /// </summary>
    [Fact]
    public void CreditsListExactlyTibiaCipSoftAndTibiaWikiWithTheirLogos()
    {
        string dir = TestData.NewTempDirectory("credits");
        var settings = new Core.Settings.AppSettings { LoadDataAutomatically = false };
        settings.Normalize();
        using var services = new AppServices(new Core.Settings.SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var about = new AboutViewModel(services);
        Assert.Equal(["Tibia", "CipSoft", "TibiaWiki"], about.Credits.Select(c => c.Name));
        Assert.Equal(["http://tibia.com/", "https://www.cipsoft.com/en/", "https://tibia.fandom.com/wiki/Main_Page"], about.Credits.Select(c => c.Url));
        Assert.All(about.Credits, c =>
        {
            Assert.Equal($"{c.Name} logo", c.LogoDescription);                                                  // read by screen readers
            Assert.Contains(c.Url, c.AccessibleName);
            Assert.False(string.IsNullOrWhiteSpace(c.Description));
            Assert.DoesNotMatch(ObsoleteName(), c.Description);
        });

        // The official files, reduced only with their aspect ratio kept; the TibiaWiki logo is byte-identical.
        string credits = Path.Combine(SourceRoot(), "TibiaFoundry.App", "Assets", "Credits");
        (string File, int Width, int Height)[] originals =
            [("tibia-logo.png", 1020, 402), ("cipsoft-logo-light.png", 2583, 397), ("cipsoft-logo-dark.png", 2583, 397), ("tibiawiki-logo.png", 250, 65)];
        foreach (var (file, width, height) in originals)
        {
            var image = ImageIO.Load(Path.Combine(credits, file));
            double ratio = (double)image.Width / image.Height / ((double)width / height);
            output.WriteLine($"{file}: {image.Width}×{image.Height}, aspect ratio error {Math.Abs(ratio - 1):P3}");
            Assert.InRange(ratio, 0.999, 1.001);
            Assert.Contains(image.Pixels, p => p >> 24 == 0);                                                 // transparent background kept
        }
        Assert.Equal("117c6e334d0954d8c55ac8df6ae227eeee077bc767911f9ff352960a77fd4dbd",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(credits, "tibiawiki-logo.png")))).ToLowerInvariant());
        Assert.Equal(["cipsoft-logo-dark.png", "cipsoft-logo-light.png", "tibia-logo.png", "tibiawiki-logo.png"],
            Directory.EnumerateFiles(credits, "*.png").Select(Path.GetFileName).Order());
        string appLogo = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(BrandingFolder, "tibia-foundry-logo.png"))));
        Assert.All(Directory.EnumerateFiles(credits, "*.png"), f => Assert.NotEqual(appLogo, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))));

        // The page binds the Credits list and has no other credit text; the creator profile is a separate section.
        string page = EnglishXaml(Path.Combine(SourceRoot(), "TibiaFoundry.App", "Views", "Pages", "AboutPage.xaml"));
        string creditsSection = Regex.Match(page, "<GroupBox Header=\"Credits\".*?</GroupBox>", RegexOptions.Singleline).Value;
        Assert.Contains("ItemsSource=\"{Binding Credits}\"", creditsSection);
        Assert.DoesNotContain("Creator", creditsSection);
        Assert.DoesNotMatch(ObsoleteName(), page);
        Assert.Contains("<GroupBox Header=\"Creator\"", page);
    }

    [Fact]
    public void AMissingCreditLogoFallsBackToTheName()
    {
        Assert.Null(TibiaFoundry.App.Infrastructure.Images.TryLoadResource("Assets/Credits/does-not-exist.png"));
        var entry = new CreditEntry("Example", "An entry without a logo.", "https://example.com/", lightLogo: null);
        Assert.False(entry.HasLogo);                                                                          // the page shows the name instead
        Assert.Null(entry.Logo);
    }

    private static RgbaImage Enlarge(RgbaImage image, int factor)
    {
        var result = new RgbaImage(image.Width * factor, image.Height * factor);
        for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
                result.Pixels[y * result.Width + x] = image.Pixels[y / factor * image.Width + x / factor];
        return result;
    }

    /// <summary>Decodes every image of an .ico file (PNG entries and 32-bit DIB entries).</summary>
    private static Dictionary<int, RgbaImage> ReadIcon(byte[] ico)
    {
        var result = new Dictionary<int, RgbaImage>();
        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            var entry = ico.AsSpan(6 + 16 * i, 16);
            int size = entry[0] == 0 ? 256 : entry[0];
            int length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            byte[] data = ico.AsSpan(offset, length).ToArray();
            if (data[0] == 0x89 && data[1] == (byte)'P')
            {
                result[size] = ImageIO.LoadFromBytes(data);
                continue;
            }
            Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14)));                 // 32 bits per pixel
            var image = new RgbaImage(size, size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int p = 40 + ((size - 1 - y) * size + x) * 4;                                         // bottom-up BGRA
                    image.Pixels[y * size + x] = (uint)(data[p + 3] << 24 | data[p + 2] << 16 | data[p + 1] << 8 | data[p]);
                }
            result[size] = image;
        }
        return result;
    }
}
