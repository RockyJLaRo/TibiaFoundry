using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Common;

namespace TibiaFoundry.Tests;

/// <summary>
/// The version shown in the window header, on the About and Home pages and in file metadata comes from one place:
/// &lt;Version&gt; in src/Directory.Build.props. Changing it there is all a release needs; no view contains a version.
/// </summary>
public sealed partial class VersionDisplayTests
{
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

    private static string PropsVersion() =>
        Regex.Match(File.ReadAllText(Path.Combine(SourceRoot(), "Directory.Build.props")), "<Version>([^<]+)</Version>").Groups[1].Value;

    [GeneratedRegex(@"\d+\.\d+(\.\d+)*")]
    private static partial Regex VersionLiteral();

    [Fact]
    public void TheHeaderShowsTheConfiguredVersion()
    {
        Assert.Equal(PropsVersion(), AppIdentity.Version);
        Assert.Equal($"Tibia Foundry v{PropsVersion()}", AppIdentity.HeaderTitle);
        Assert.Equal("Tibia Foundry - All your Tibia-Tools for TibiaWiki", AppIdentity.Tagline);
    }

    [Fact]
    public void TheHeaderIsBoundToTheVersionAndContainsNoVersionOfItsOwn()
    {
        string window = File.ReadAllText(Path.Combine(SourceRoot(), "TibiaFoundry.App", "Views", "MainWindow.xaml"));
        string brand = Regex.Match(window, "<StackPanel x:Name=\"BrandText\".*?</StackPanel>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(brand);
        var title = Regex.Match(brand, "<TextBlock x:Name=\"HeaderTitle\"[^>]*/>", RegexOptions.Singleline).Value;
        Assert.Contains("Text=\"{x:Static common:AppIdentity.HeaderTitle}\"", title);
        Assert.Contains("xmlns:common=\"clr-namespace:TibiaFoundry.Core.Common;assembly=TibiaFoundry.Core\"", window);
        Assert.DoesNotMatch(VersionLiteral(), brand);
        // The header is line 1 (title) above line 2 (tagline).
        Assert.True(brand.IndexOf("x:Name=\"HeaderTitle\"", StringComparison.Ordinal) < brand.IndexOf("x:Name=\"Tagline\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("9.0.5", "Tibia Foundry v9.0.5")]
    [InlineData("9.0.6", "Tibia Foundry v9.0.6")]
    [InlineData("9.1.0", "Tibia Foundry v9.1.0")]
    [InlineData("10.0.0", "Tibia Foundry v10.0.0")]
    [InlineData("9.0.6+4b35cd62", "Tibia Foundry v9.0.6")]
    public void AnotherConfiguredVersionChangesTheHeaderWithoutTouchingIt(string informationalVersion, string expected)
    {
        // The same path as a real build: <Version> becomes the assembly's informational version, which AppIdentity reads.
        var name = new AssemblyName("FutureTibiaFoundry" + Guid.NewGuid().ToString("N"));
        var builder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect,
        [
            new CustomAttributeBuilder(typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, [informationalVersion]),
        ]);
        Assert.Equal(expected, AppIdentity.FormatHeaderTitle(AppIdentity.VersionOf(builder)));
    }

    [Fact]
    public void WithoutAnInformationalVersionTheAssemblyVersionIsUsed()
    {
        var builder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Old") { Version = new Version(9, 0, 6, 0) }, AssemblyBuilderAccess.RunAndCollect);
        Assert.Equal("Tibia Foundry v9.0.6", AppIdentity.FormatHeaderTitle(AppIdentity.VersionOf(builder)));
    }

    [Fact]
    public void AboutHomeAndTheAssemblyAgreeWithTheHeader()
    {
        foreach (var vm in new[] { "AboutViewModel.cs", "HomeViewModel.cs" })
            Assert.Contains("public string Version => AppIdentity.Version;", File.ReadAllText(Path.Combine(SourceRoot(), "TibiaFoundry.App", "ViewModels", vm)));
        Assert.Equal(AppIdentity.Version, AppIdentity.VersionOf(typeof(AboutViewModel).Assembly));
        Assert.Equal($"Tibia Foundry {PropsVersion()}", AppIdentity.NameAndVersion);
        var padded = new Version(PropsVersion() + (PropsVersion().Count(c => c == '.') == 1 ? ".0" : "") + ".0");
        Assert.Equal(padded, typeof(AboutViewModel).Assembly.GetName().Version);
        Assert.Equal(padded.ToString(), typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version);
        // Views show the version only through bindings to these properties.
        foreach (var page in new[] { "AboutPage.xaml", "HomePage.xaml" })
        {
            string xaml = File.ReadAllText(Path.Combine(SourceRoot(), "TibiaFoundry.App", "Views", "Pages", page));
            Assert.Contains("{Binding Version, Mode=OneWay}", xaml);
            Assert.DoesNotContain(PropsVersion(), xaml);
        }
    }
}
