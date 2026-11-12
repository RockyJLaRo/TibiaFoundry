using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Compatibility;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Legacy;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>An optional extended attribute for third-party legacy-format tools (a checkbox on the page).</summary>
public sealed partial class ExtensionOption(LegacyAttribute attribute, string label) : ObservableObject
{
    public LegacyAttribute Attribute { get; } = attribute;
    public string Label { get; } = label;
    [ObservableProperty] private bool _isChecked;
}

/// <summary>
/// Client Converter: turns the modern client's assets into a legacy Tibia.dat and Tibia.spr, and
/// identifies existing legacy files, for historical asset comparisons and graphics work with legacy-format
/// viewers and sprite tools. Re-implements ClientConverter by Arch-Mina (with the legacy
/// export of his Assets Editor) on Tibia Foundry's own readers and writers, with read-back
/// validation of everything it writes. Its options are the settings declared in its manifest
/// (<see cref="BuiltInTools.ClientConverter"/>), so they are validated, can be reset in
/// Settings → Tools, and keep the keys earlier versions stored them under.
/// </summary>
public sealed partial class ClientConverterViewModel : PageViewModel
{
    private bool _loading;

    public ClientConverterViewModel(AppServices services) : base(services, BuiltInTools.ClientConverter)
    {
        Extensions =
        [
            new(LegacyAttribute.ChangedToExpire, L.T("ClientConverter.ChangedToExpire")), new(LegacyAttribute.Corpse, L.T("ClientConverter.Corpse")), new(LegacyAttribute.PlayerCorpse, L.T("ClientConverter.PlayerCorpse")),
            new(LegacyAttribute.CyclopediaItem, L.T("ClientConverter.CyclopediaItem")), new(LegacyAttribute.Ammo, L.T("ClientConverter.Ammunition")), new(LegacyAttribute.ShowOffSocket, L.T("ClientConverter.ShowOffSocket")),
            new(LegacyAttribute.Reportable, L.T("ClientConverter.Reportable")), new(LegacyAttribute.UpgradeClassification, L.T("ClientConverter.UpgradeClassification")), new(LegacyAttribute.WearOut, L.T("ClientConverter.WearOut")),
            new(LegacyAttribute.ClockExpire, L.T("ClientConverter.ClockExpire")), new(LegacyAttribute.Expire, L.T("ClientConverter.Expire")), new(LegacyAttribute.ExpireStop, L.T("ClientConverter.ExpireStop")),
        ];
        _profileId = LegacyClientProfiles.Client11.Id;
        _datSignature = _sprSignature = "";
        LoadOptions();
        services.ToolSettingsChanged += id => { if (id == Manifest.Id) LoadOptions(); };
        // A folder taken from the client in use follows a client switch; a folder you chose stays.
        services.Data.ClientFilesChanged += types =>
        {
            if (types == null && _assetsFromClient) { AssetsFolder = ""; _assetsFromClient = false; }
        };
    }

    /// <summary>True while <see cref="AssetsFolder"/> is the client's own folder (not one you chose).</summary>
    private bool _assetsFromClient;

    /// <summary>Reads the options from the tool's settings (validated; invalid values read as defaults).</summary>
    private void LoadOptions()
    {
        var store = Services.ToolSettings;
        _loading = true;
        try
        {
            ProfileId = store.Get(Manifest, "profile");
            Transparency = store.GetBool(Manifest, "transparency");
            SkipEmpty = store.GetBool(Manifest, "skipEmpty");
            ShortenNames = store.GetBool(Manifest, "shortenNames");
            MapNewMarketCategories = store.GetBool(Manifest, "mapMarketCategories");
            WriteSlices = store.GetBool(Manifest, "slices");
            DatSignature = store.Get(Manifest, "dat");
            SprSignature = store.Get(Manifest, "spr");
            var chosen = store.Get(Manifest, "extensions").Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var e in Extensions) e.IsChecked = chosen.Contains(e.Attribute.ToString());
        }
        finally
        {
            _loading = false;
        }
    }


    [ObservableProperty] private string _assetsFolder = "";
    [ObservableProperty] private string _outputFolder = "";
    [ObservableProperty] private string _profileId;
    [ObservableProperty] private bool _transparency;
    [ObservableProperty] private bool _skipEmpty;
    [ObservableProperty] private bool _shortenNames;
    [ObservableProperty] private bool _mapNewMarketCategories;
    [ObservableProperty] private string _datSignature;
    [ObservableProperty] private string _sprSignature;
    [ObservableProperty] private bool _writeSlices;
    [ObservableProperty] private string _resultSummary = "";
    [ObservableProperty] private string _validation = "";
    [ObservableProperty] private string _inspection = "";

    public ObservableCollection<string> ResultDetails { get; } = [];
    public IReadOnlyList<ExtensionOption> Extensions { get; }

    public static IReadOnlyList<LegacyClientProfile> Profiles => LegacyClientProfiles.Writable;

    public LegacyClientProfile Profile => Profiles.FirstOrDefault(p => p.Id == ProfileId) ?? LegacyClientProfiles.Client11;
    public string ProfileDescription => Profile.Description;
    public string SlicesFolder => Path.Combine(OutputFolder, "slices");

    partial void OnProfileIdChanged(string value)
    {
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(ProfileDescription));
        if (_loading) return;
        DatSignature = "";
        SprSignature = "";
    }

    partial void OnOutputFolderChanged(string value) => OnPropertyChanged(nameof(SlicesFolder));

    public override Task OnNavigatedToAsync()
    {
        if (AssetsFolder.Length == 0)
        {
            string? chosen = Services.Settings.InputFolderFor(ToolKeys.ClientConverter);
            AssetsFolder = chosen ?? RealAssetsFolder() ?? "";
            _assetsFromClient = chosen == null && AssetsFolder.Length > 0;
        }
        if (OutputFolder.Length == 0) OutputFolder = Services.Outputs.FolderFor(OutputKind.ClientConverter);
        return Task.CompletedTask;
    }

    /// <summary>Called by the Assets Editor ("Export to legacy client") with the folder it saved.</summary>
    public void UseAssetsFolder(string folder)
    {
        AssetsFolder = folder;
        _assetsFromClient = false;
    }

    [RelayCommand]
    private void ChooseAssets()
    {
        string? folder = Services.Dialogs.PickFolder(L.T("ClientConverter.ModernClientAssetsFolderWithCatalog"), AssetsFolder);
        if (folder == null) return;
        if (!File.Exists(Path.Combine(folder, TibiaFormat.AssetCatalogFile)))
        {
            Services.Dialogs.Show(L.T("ClientConverter.ThisFolderHasNoCatalogContent"), MessageKind.Warning, L.T("ClientConverter.ClientConverter"));
            return;
        }
        AssetsFolder = folder;
        _assetsFromClient = false;
        Services.Settings.RememberFolder(ToolKeys.ClientConverter, "input", folder);
    }

    [RelayCommand]
    private void UseInstalledClient()
    {
        if (!RequireRealClientFolder(L.T("ClientConverter.TheClientConverter"))) return;
        AssetsFolder = Services.Data.AssetsDirectory;
        _assetsFromClient = true;
    }

    [RelayCommand]
    private void ChooseOutput()
    {
        string? folder = Services.Outputs.PickFolder(OutputKind.ClientConverter, L.T("ClientConverter.FolderForTibiaDatAndTibia"), OutputFolder);
        if (folder == null) return;
        OutputFolder = folder;
    }

    [RelayCommand]
    private void OpenOutput() => Services.Shell.OpenFolder(Directory.Exists(OutputFolder) ? OutputFolder : null);

    private static uint? ParseHex(string text)
    {
        string t = text.Trim();
        if (t.Length == 0) return null;
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        return uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v) ? v : throw new FormatException($"\"{text}\" is not a hexadecimal number.");
    }

    /// <summary>Stores the options as the tool's settings (each value is validated against the manifest).</summary>
    private void SaveState()
    {
        var store = Services.ToolSettings;
        store.TrySet(Manifest, "profile", ProfileId, out _);
        store.SetBool(Manifest, "transparency", Transparency);
        store.SetBool(Manifest, "skipEmpty", SkipEmpty);
        store.SetBool(Manifest, "shortenNames", ShortenNames);
        store.SetBool(Manifest, "mapMarketCategories", MapNewMarketCategories);
        store.SetBool(Manifest, "slices", WriteSlices);
        store.TrySet(Manifest, "dat", DatSignature, out _);
        store.TrySet(Manifest, "spr", SprSignature, out _);
        store.TrySet(Manifest, "extensions", string.Join(",", Extensions.Where(e => e.IsChecked).Select(e => e.Attribute)), out _);
        Services.SaveSettings();
    }

    [RelayCommand]
    private async Task ConvertAsync()
    {
        LegacyClientProfile profile;
        try
        {
            profile = Profile.WithTransparency(Transparency);
            uint? dat = ParseHex(DatSignature), spr = ParseHex(SprSignature);
            if (dat != null || spr != null) profile = profile.WithSignatures(dat ?? profile.DatSignature, spr ?? profile.SprSignature);
        }
        catch (FormatException ex)
        {
            Services.Dialogs.Show(ex.Message, MessageKind.Warning, L.T("ClientConverter.ClientConverter"));
            return;
        }
        if (!File.Exists(Path.Combine(AssetsFolder, TibiaFormat.AssetCatalogFile)))
        {
            Services.Dialogs.Show(L.T("ClientConverter.ChooseAClientAssetsFolderFirst"), MessageKind.Warning, L.T("ClientConverter.ClientConverter"));
            return;
        }
        if (PathGuard.IsSameOrInside(AssetsFolder, OutputFolder))
        {
            Services.Dialogs.Show(L.T("ClientConverter.ChooseAnOutputFolderOutsideThe"), MessageKind.Warning, L.T("ClientConverter.ClientConverter"));
            return;
        }
        if (OutputLocations.Prepare(OutputFolder) is { Usable: false } folderState)
        {
            Services.Dialogs.Show(L.F("ClientConverter.TheOutputFolderCannotBeUsed", folderState.Problem), MessageKind.Warning, L.T("ClientConverter.ClientConverter"));
            return;
        }
        var extensions = Extensions.Where(e => e.IsChecked).Select(e => e.Attribute).ToHashSet();
        if (extensions.Count > 0 && !Services.Dialogs.Confirm(
                L.F("ClientConverter.YouChose0ExtendedAttributeS", extensions.Count),
                L.T("ClientConverter.ExtendedAttributes"), L.T("ClientConverter.WriteThem"), L.T("Common.Cancel"))) return;
        SaveState();
        var options = new ClientConversionOptions
        {
            AssetsFolder = AssetsFolder,
            OutputFolder = OutputFolder,
            Profile = profile,
            SkipEmptySprites = SkipEmpty,
            ExtensionAttributes = extensions,
            MaxMarketNameLength = ShortenNames ? ClientConversionOptions.DefaultMarketNameLimit(profile) : 0,
            MaxMarketCategory = MapNewMarketCategories ? ClientConversionOptions.LegacyMarketCategoryLimit : 0,
            SlicesFolder = WriteSlices ? SlicesFolder : null,
            WorkerThreads = Services.Settings.EffectiveWorkerThreads,
            StopOnError = Services.Settings.Performance.StopBatchOnError,
        };
        ClientConversionResult? result = null;
        string description = "";
        ResultDetails.Clear();
        await RunAsync(L.F("ClientConverter.ConvertingTo0", profile.Name), async (progress, ct) =>
        {
            using var output = Services.CreateOutput(L.T("ClientConverter.ClientConverter"));
            result = await Task.Run(() => ClientConverter.Run(options, output, Services.Temp, progress, ct), ct);
            description = output.Describe();
        }, operation: "convert", output: OutputFolder, onSuccess: () => ShowResult(result!, description));
    }

    /// <summary>Shows the conversion result; the status names the written Tibia.dat (clickable) only when files were written.</summary>
    private void ShowResult(ClientConversionResult result, string description)
    {
        ResultSummary = result.SprPath == null
            ? L.T("ClientConverter.NothingWasWritten") + string.Join(" ", result.Notes)
            : L.F("ClientConverter.0N0Items1N0Outfits", result.Items, result.Outfits, result.Effects, result.Missiles, result.SpritesWritten, result.EmptyTilesSkipped, result.Duration.TotalSeconds, description);
        Validation = result.Validation ?? (Services.Settings.Safety.ValidateOutputs ? "" : L.T("ClientConverter.ValidationIsTurnedOffInSettings"));
        foreach (var n in result.Notes) ResultDetails.Add(n);
        if (result.SlicesWritten > 0) ResultDetails.Add(L.F("ClientConverter.0N0SlicesWrittenTo1", result.SlicesWritten, SlicesFolder));
        foreach (var kv in result.NotStored) ResultDetails.Add(L.F("ClientConverter.NotStoredTheLegacyFormatHas", kv.Key, L.Count(kv.Value, "Noun.object")));
        foreach (var f in result.Failures.Take(50)) ResultDetails.Add(L.T("ClientConverter.WrittenEmpty") + f);
        if (result.DatPath == null)
        {
            Services.Status.Show(ResultSummary, StatusKind.Warning);
            return;
        }
        if (result.Failures.Count > 0)
        {
            AppLog.Warn($"Client Converter: {L.Count(result.Failures.Count, "Noun.object")} written empty: {string.Join(" | ", result.Failures.Take(50))}");
            Services.Status.ShowSaved(result.DatPath, isFile: true, before: L.T("ClientConverter.ConvertedWithProblems"),
                after: L.F("ClientConverter.AndTibiaSpr0WrittenEmpty", L.Count(result.Failures.Count, "Noun.object")), kind: StatusKind.Warning);
        }
        else
        {
            Services.Status.ShowSaved(result.DatPath, isFile: true, before: L.T("ClientConverter.SavedTo"), after: L.F("ClientConverter.AndTibiaSpr0N0Items", result.Items, result.SpritesWritten));
        }
    }

    /// <summary>Identifies a legacy Tibia.dat (and the Tibia.spr next to it): layout, counts, signatures.</summary>
    [RelayCommand]
    private async Task InspectAsync()
    {
        string? file = Services.Dialogs.OpenFile(L.T("ClientConverter.ChooseALegacyTibiaDat"), L.T("ClientConverter.TibiaDatDatAllFiles"), OutputFolder);
        if (file == null) return;
        string text = "";
        await RunAsync(L.T("ClientConverter.Inspecting") + Path.GetFileName(file), async (_, ct) =>
        {
            text = await Task.Run(() =>
            {
                var detected = LegacyDatCodec.Detect(File.ReadAllBytes(file));
                if (detected is not { } d) return L.F("ClientConverter.0NoKnownClientLayout7", file);
                var lines = new List<string>
                {
                    $"{file}",
                    L.F("ClientConverter.Layout01", d.Profile.Name, (LegacyClientProfiles.FromSignatures(d.File.Signature, 0) is { } known ? L.F("ClientConverter.Signature0x0X81", d.File.Signature, known.Name) : L.F("ClientConverter.Signature0x0X8", d.File.Signature))),
                    L.F("ClientConverter.0N0ItemsIDs1001", d.File.Items.Count, 99 + d.File.Items.Count, d.File.Outfits.Count, d.File.Effects.Count, d.File.Missiles.Count, d.File.MaxSpriteId),
                };
                string spr = Path.Combine(Path.GetDirectoryName(file)!, "Tibia.spr");
                if (File.Exists(spr))
                {
                    using var reader = new LegacySprReader(spr, d.Profile);
                    lines.Add(L.F("ClientConverter.TibiaSprSignature0x0X8", reader.Signature, reader.Count, Core.FileOps.ReportWriter.FormatSize(reader.Length)) +
                              (d.File.MaxSpriteId > reader.Count ? L.F("ClientConverter.WARNINGTibiaDatUsesSprite0", d.File.MaxSpriteId) : L.T("ClientConverter.EverySpriteIDTibiaDatUses")));
                }
                return string.Join("\n", lines);
            }, ct);
        });
        Inspection = text;
    }
}
