using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.Themes;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Net;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>One clickable link of the creator profile.</summary>
/// <param name="IconKey">Resource key of the badge in Themes/BrandIcons.xaml.</param>
/// <param name="Favicon">
/// The site's own icon (bundled, see Assets/Links/README.md). When it is null (no bundled icon, or the
/// file is missing or damaged) the page shows the badge <paramref name="IconKey"/> instead.
/// </param>
public sealed record CreatorLink(string Label, string Detail, string Url, string IconKey, ImageSource? Favicon = null)
{
    public bool HasFavicon => Favicon != null;
}

/// <summary>
/// An open-source project acknowledged on the About page. Kept apart from the credits: these
/// projects inspired or were used to check tools of Tibia Foundry; their code is not included,
/// and their authors are not affiliated with Tibia Foundry and do not endorse it.
/// </summary>
public sealed record Acknowledgment(string Project, string Author, string ProjectUrl, string AuthorUrl, string Relation, string License)
{
    /// <summary>Text for screen readers.</summary>
    public string AccessibleName => L.F("About.0By1Opens2In", Project, Author, ProjectUrl);
}

/// <summary>
/// One organization in About → Credits: its name, official logo and website. The logo is null when
/// the bundled file is missing or damaged; the page then shows the name in its place.
/// </summary>
public sealed partial class CreditEntry : ObservableObject
{
    private readonly ImageSource? _lightLogo;
    private readonly ImageSource? _darkLogo;

    public CreditEntry(string name, string description, string url, ImageSource? lightLogo, ImageSource? darkLogo = null)
    {
        Name = name;
        Description = description;
        Url = url;
        _lightLogo = lightLogo;
        _darkLogo = darkLogo ?? lightLogo;
    }

    public string Name { get; }
    public string Description { get; }
    public string Url { get; }

    /// <summary>Text for screen readers, e.g. "Tibia logo".</summary>
    public string LogoDescription => L.F("About.0Logo", Name);

    /// <summary>Text for screen readers describing the whole entry.</summary>
    public string AccessibleName => L.F("About.0Opens1InYourWeb", Name, Url);

    /// <summary>The logo version for the current theme (light or dark background).</summary>
    public ImageSource? Logo => ThemeManager.IsDark ? _darkLogo : _lightLogo;

    public bool HasLogo => Logo != null;

    internal void RefreshForTheme()
    {
        OnPropertyChanged(nameof(Logo));
        OnPropertyChanged(nameof(HasLogo));
    }
}

/// <summary>Program identity, creator profile, export attribution, credits and component information.</summary>
public sealed partial class AboutViewModel : PageViewModel
{
    public AboutViewModel(AppServices services) : base(services, BuiltInTools.About)
    {
        services.CreatorChanged += () => OnPropertyChanged(nameof(AttributionPreview));
        Credits =
        [
            new CreditEntry("Tibia", L.T("About.TheOnlineRolePlayingGameWhose"),
                ExternalEndpoints.CreditTibia, Images.TryLoadResource("Assets/Credits/tibia-logo.png")),
            new CreditEntry("CipSoft", L.T("About.TheDeveloperAndPublisherOfTibia"),
                ExternalEndpoints.CreditCipSoft, Images.TryLoadResource("Assets/Credits/cipsoft-logo-light.png"),
                Images.TryLoadResource("Assets/Credits/cipsoft-logo-dark.png")),
            new CreditEntry("TibiaWiki", L.T("About.TheCommunityEncyclopediaOfTibiaThat"),
                ExternalEndpoints.CreditTibiaWiki, Images.TryLoadResource("Assets/Credits/tibiawiki-logo.png")),
        ];
        ThemeManager.ThemeChanged += () =>
        {
            foreach (var credit in Credits) credit.RefreshForTheme();
        };
    }


    public string AppName => AppIdentity.Name;
    public string Version => AppIdentity.Version;
    public string Creator => AppIdentity.Creator;
    public string CreatorRole => AppIdentity.CreatorRole;
    public string Website => AppIdentity.Website;

    /// <summary>Target of "TibiaWiki" in the tagline: the wiki's main page.</summary>
    public string TaglineUrl => ExternalEndpoints.CreditTibiaWiki;

    /// <summary>The attribution embedded in this installation's exports.</summary>
    public string AttributionPreview => Attribution.TextFor(Attribution.CurrentCreator);

    public string Runtime => $".NET {Environment.Version} · {(Environment.Is64BitProcess ? "64-bit" : "32-bit")} · {System.Runtime.InteropServices.RuntimeInformation.OSDescription}";

    /// <summary>Bundled site icons (favicons) of the creator links.</summary>
    public const string TibiaWikiFavicon = "Assets/Links/tibiawiki-favicon.ico";
    public const string TibiaFavicon = "Assets/Links/tibia-favicon.ico";

    /// <summary>The creator's profiles. The TibiaWiki and Tibia entries show the sites' own icons.</summary>
    public IReadOnlyList<CreatorLink> CreatorLinks { get; } =
    [
        new(L.T("About.TibiaWikiProfile"), "User:RockyLaRo", ExternalEndpoints.CreatorTibiaWikiProfile, "BrandIcon.TibiaWiki", Images.TryLoadIcon(TibiaWikiFavicon)),
        new(L.T("About.TibiaCharacter"), L.T("About.RocKy"), ExternalEndpoints.CreatorTibiaCharacter, "BrandIcon.Tibia", Images.TryLoadIcon(TibiaFavicon)),
        new("YouTube", "@Roc_ky", ExternalEndpoints.CreatorYouTube, "BrandIcon.YouTube"),
        new("Instagram", "rocky_tibia", ExternalEndpoints.CreatorInstagram, "BrandIcon.Instagram"),
        new("GitHub", "RockyJLaRo", ExternalEndpoints.CreatorGitHub, "BrandIcon.GitHub"),
        new("Reddit", "u/Roc-ky", ExternalEndpoints.CreatorReddit, "BrandIcon.Reddit"),
    ];

    /// <summary>Exactly Tibia, CipSoft and TibiaWiki, in that order.</summary>
    public IReadOnlyList<CreditEntry> Credits { get; }

    /// <summary>
    /// Open-source projects whose tools inspired, or were used to check, the RCC Editor, Client
    /// Converter and Assets Editor. Separate from <see cref="Credits"/>; no code of theirs is included.
    /// </summary>
    public IReadOnlyList<Acknowledgment> Acknowledgments { get; } =
    [
        new(L.T("About.NekiroSRCCEditor"), "Nekiro", ExternalEndpoints.NekiroRccEditor, ExternalEndpoints.NekiroProfile,
            L.T("About.AnEditorForTheClientS"),
            L.T("About.MITDeclaredInTheProjectS")),
        new("ClientConverter", "Arch-Mina", ExternalEndpoints.ArchMinaClientConverter, ExternalEndpoints.ArchMinaProfile,
            L.T("About.ConvertsTheModernClientSAssets"),
            L.T("About.NoLicensePublished")),
        new(L.T("About.AssetsEditor"), "Arch-Mina", ExternalEndpoints.ArchMinaAssetsEditor, ExternalEndpoints.ArchMinaProfile,
            L.T("About.AnEditorForTheModernClient"),
            L.T("About.NoLicensePublished")),
    ];

    /// <summary>Open-source components shipped with the program, with their licenses.</summary>
    public IReadOnlyList<string> Components { get; } =
    [
        L.T("About.GoogleProtobufBSD3ClauseReads"),
        L.T("About.MagickNETImageMagickApache20"),
        L.T("About.NAudioAndNVorbisMITDecodeAnd"),
        L.T("About.NewtonsoftJsonMITWritesTheJSON"),
        L.T("About.TagLibLGPL21ReadsAnd"),
        L.T("About.CommunityToolkitMvvmMITUserInterfaceInfrastructu"),
        L.T("About.LZMASDKPublicDomainBasisOf"),
        L.T("About.NETAndWPFMITRuntimeUser"),
    ];

    [RelayCommand]
    private void OpenLink(string? url)
    {
        // Only the addresses listed on this page are opened.
        if (url != null && (CreatorLinks.Any(l => l.Url == url) || Credits.Any(c => c.Url == url)
            || Acknowledgments.Any(a => a.ProjectUrl == url || a.AuthorUrl == url)))
            Services.Shell.OpenUrl(url);
    }

    [RelayCommand] private void OpenWebsite() => Services.Shell.OpenUrl(ExternalEndpoints.TibiaWikiHome);
    [RelayCommand] private void ChangeCreator() => Services.RequestNavigate<SettingsViewModel>();
}
