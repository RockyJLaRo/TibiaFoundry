using TibiaFoundry.Core.Metadata;
using TibiaFoundry.Core.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Editing;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

public enum SheetSource { Files, IdList, LegacyJson, Appearances }

/// <summary>
/// Tools that the original shipped but hid at start-up ("Export Sheets", "Sprites from PNG" and
/// "LZMA Decode" tab pages), restored and fixed.
/// </summary>
public sealed partial class AdvancedToolsViewModel : PageViewModel
{
    public AdvancedToolsViewModel(AppServices services) : base(services, BuiltInTools.AdvancedTools) { }


    // ------------------------------------------------------------------ sheet builder ("Export Sheets")
    [ObservableProperty] private SheetSource _source = SheetSource.Files;
    [ObservableProperty] private IReadOnlyList<string> _files = Array.Empty<string>();
    [ObservableProperty] private string _idList = "";
    [ObservableProperty] private string _spriteFolder = "";
    [ObservableProperty] private string _jsonFile = "";
    [ObservableProperty] private string _sourceId = "";
    [ObservableProperty] private int _columns = 8;
    [ObservableProperty] private int _cellWidth = 64;
    [ObservableProperty] private int _cellHeight = 64;
    [ObservableProperty] private bool _magenta;

    public string FilesSummary => Files.Count == 0 ? L.T("AdvancedTools.NoFilesSelected") : $"{L.Count(Files.Count, "Noun.file")}: " + string.Join(", ", Files.Take(10).Select(Path.GetFileName)) + (Files.Count > 10 ? ", …" : "");

    partial void OnFilesChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(FilesSummary));

    [RelayCommand]
    private void SelectFiles()
    {
        var files = Services.Dialogs.OpenFiles(L.T("AdvancedTools.SelectSpriteImagesInSheetOrder"), L.T("AdvancedTools.ImagesPngGifBmpPngGif"), Services.Outputs.FolderFor(OutputKind.SpriteExports));
        if (files != null) Files = files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    [RelayCommand] private void SelectSpriteFolder() { if (Services.Dialogs.PickFolder(L.T("AdvancedTools.SpritesFolderPick"), SpriteFolder) is { } f) SpriteFolder = f; }
    [RelayCommand] private void SelectJson() { if (Services.Dialogs.OpenFile(L.T("AdvancedTools.AppearancesJsonFile"), L.T("AppearanceExport.JSONFilesJsonJson")) is { } f) JsonFile = f; }

    [RelayCommand]
    private void PresetOutfits() { Columns = 8; CellWidth = 64; CellHeight = 64; }

    [RelayCommand]
    private void PresetMounts() { Columns = 4; CellWidth = 64; CellHeight = 64; }

    [RelayCommand]
    private void InsertIdList()
    {
        string? text = Services.Dialogs.Prompt(L.T("AdvancedTools.SpriteIDList"), L.T("AdvancedTools.SpriteIDsSeparatedByCommasSpaces2"), IdList, multiline: true);
        if (text != null) IdList = text;
    }

    [RelayCommand]
    private async Task CreateSheetAsync()
    {
        List<RgbaImage> images;
        List<string> inputFiles;
        try
        {
            (images, inputFiles) = await Task.Run(CollectImages);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or IOException or Newtonsoft.Json.JsonException)
        {
            Services.Dialogs.Show(ex.Message, MessageKind.Warning, L.T("AdvancedTools.CreateSheet2"));
            return;
        }
        string? file = Services.Outputs.SaveFile(OutputKind.ImageExports, L.T("AdvancedTools.SaveSpriteSheet"), L.T("AdvancedTools.PNGImagesPngPng"), "Sheet.png");
        if (file == null) return;
        // Image files read here may already name a creator: that attribution is kept.
        var sources = await Task.Run(() => AttributionReader.ReadAll(inputFiles));
        string? notice = Source == SheetSource.Files ? null : Attribution.TibiaArtworkNotice;
        if (AttributionForImports(sources, L.T("AdvancedTools.CreateSheet2"), notice) is not { } attribution) return;
        Try(L.T("AdvancedTools.CreatingTheSheet"), () =>
        {
            ImageIO.SavePng(SheetComposer.Grid(images, Columns, CellWidth, CellHeight, Magenta), file, attribution);
            Saved(file, L.F("AdvancedTools.0Images", images.Count));
        });
    }

    /// <returns>The images and the files they were read from (client sprites have no file).</returns>
    private (List<RgbaImage> Images, List<string> Files) CollectImages()
    {
        if (Source == SheetSource.Files)
        {
            if (Files.Count == 0) throw new InvalidOperationException("Select the image files first.");
            return (Files.Select(ImageIO.Load).ToList(), Files.ToList());
        }
        List<uint> ids = Source switch
        {
            SheetSource.IdList => AppearanceFilter.TryParseIdList(IdList, out var parsed, out var bad) ? parsed : throw new InvalidOperationException($"\"{bad}\" is not a valid sprite ID."),
            SheetSource.LegacyJson => File.Exists(JsonFile) ? SheetComposer.SpriteIdsFromLegacyJson(File.ReadAllText(JsonFile), SourceId.Trim()) : throw new InvalidOperationException("Select the JSON file first."),
            _ => SheetComposer.SpriteIdsFromAppearance(Services.Data.Appearances ?? throw new InvalidOperationException("Load the appearances on the Appearances page first."),
                    uint.TryParse(SourceId.Trim(), out uint oid) ? oid : throw new InvalidOperationException("Type a numeric outfit ID.")),
        };
        if (ids.Count == 0) throw new InvalidOperationException("No sprite IDs to place.");
        var images = new List<RgbaImage>();
        var files = new List<string>();
        var missing = new List<uint>();
        foreach (uint id in ids)
        {
            RgbaImage? img = null;
            string path = Path.Combine(SpriteFolder, id + ".png");
            if (SpriteFolder.Length > 0 && File.Exists(path))
            {
                img = ImageIO.Load(path);
                files.Add(path);
            }
            img ??= Services.Data.Sprites?.GetSprite(id);
            if (img == null) missing.Add(id);
            else images.Add(img);
        }
        if (missing.Count > 0) throw new InvalidOperationException("These sprites could not be found: " + string.Join(" ", missing));
        return (images, files);
    }

    // ------------------------------------------------------------------ sheet slicer ("Sprites from PNG")
    [ObservableProperty] private string _sliceFile = "";
    [ObservableProperty] private int _sliceWidth = 64;
    [ObservableProperty] private int _sliceHeight = 64;
    [ObservableProperty] private string _slicePrefix = "";
    [ObservableProperty] private string _sliceOutput = "";

    [RelayCommand]
    private void SelectSliceFile()
    {
        if (Services.Dialogs.OpenFile(L.T("AdvancedTools.SelectASheet"), L.T("AdvancedTools.PNGImagesPngPng"), Services.Outputs.FolderFor(OutputKind.ImageExports)) is not { } f) return;
        SliceFile = f;
        // Cut images go to the image exports folder by default, apart from the sheet itself.
        if (SliceOutput.Length == 0) SliceOutput = Services.Outputs.FolderFor(OutputKind.ImageExports);
    }

    [RelayCommand] private void SelectSliceOutput() { if (Services.Outputs.PickFolder(OutputKind.ImageExports, L.T("Common.OutputFolder"), SliceOutput.Length > 0 ? SliceOutput : null) is { } f) SliceOutput = f; }

    [RelayCommand]
    private void Slice()
    {
        if (!File.Exists(SliceFile)) { Services.Dialogs.Show(L.T("AdvancedTools.SelectASheetImageFirst")); return; }
        if (SliceOutput.Length == 0) { Services.Dialogs.Show(L.T("AdvancedTools.SelectAnOutputFolderFirst")); return; }
        string dir = Path.Combine(SliceOutput, Path.GetFileNameWithoutExtension(SliceFile));
        if (OutputLocations.Prepare(SliceOutput) is { Usable: false } state) { Services.Dialogs.Show(L.F("AdvancedTools.TheOutputFolderCannotBeUsed", state.Problem), MessageKind.Warning, L.T("AdvancedTools.SliceSheet")); return; }
        // The cut images keep the sheet's own attribution (its creator stays credited).
        if (AttributionForImports(AttributionReader.ReadAll([SliceFile]), L.T("AdvancedTools.SliceSheet"), null) is not { } attribution) return;
        Try(L.T("AdvancedTools.SlicingTheSheet"), () =>
        {
            int written = SheetComposer.SliceOutfitSheet(ImageIO.Load(SliceFile), SliceWidth, SliceHeight, dir, SlicePrefix, attribution);
            Services.Status.ShowSaved(dir, isFile: false, before: L.F("AdvancedTools.Saved0ImagesTo", written));
        });
    }

    // ------------------------------------------------------------------ LZMA
    [ObservableProperty] private string _downloadBaseUrl = ExternalEndpoints.TestServerAssetsBase;

    [RelayCommand]
    private async Task DecodeFilesAsync()
    {
        var files = Services.Dialogs.OpenFiles(L.T("AdvancedTools.CipSoftLZMAFilesToDecode"), L.T("AdvancedTools.LZMAFilesLzmaLzmaAllFiles"), Services.Data.AssetsDirectory);
        if (files == null) return;
        // Decoded files go to the data exports folder by default, never next to the client's own files.
        string? output = Services.Outputs.PickFolder(OutputKind.DataExports, L.T("AdvancedTools.FolderForTheDecodedFiles"));
        if (output == null) return;
        if (AssetsProject.IsInsideClientInstallation(output))
        {
            Services.Dialogs.Show(L.T("AdvancedTools.FoldersOfTheInstalledTibiaClient"), MessageKind.Warning, L.T("AdvancedTools.DecodingLZMAFiles"));
            return;
        }
        var result = new BatchResult();
        await RunAsync(L.T("AdvancedTools.DecodingLZMAFiles"), (p, ct) => Task.Run(() =>
        {
            for (int i = 0; i < files.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    LzmaFileTool.Decode(files[i], output);
                    result.Succeeded++;
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    result.Failures.Add($"{Path.GetFileName(files[i])}: {ex.Message}");
                }
                p.Report(new OperationProgress(i + 1, files.Length, Path.GetFileName(files[i])));
            }
        }, ct), operation: "decompress", output: output, onSuccess: () => ReportBatch(result, L.T("AdvancedTools.DecodedFiles"), output, L.T("AdvancedTools.DecodingLZMAFiles")));
    }

    [RelayCommand]
    private async Task DownloadListAsync()
    {
        string? list = Services.Dialogs.OpenFile(L.T("AdvancedTools.TextFileWithOneAssetFile"), L.T("AdvancedTools.TextFilesTxtTxtAllFiles"));
        if (list == null) return;
        string? output = Services.Outputs.PickFolder(OutputKind.DataExports, L.T("AdvancedTools.DownloadFolder"));
        if (output == null) return;
        if (AssetsProject.IsInsideClientInstallation(output))
        {
            Services.Dialogs.Show(L.T("AdvancedTools.FoldersOfTheInstalledTibiaClient"), MessageKind.Warning, L.T("AdvancedTools.DownloadingFiles"));
            return;
        }
        string baseUrl = DownloadBaseUrl;
        BatchResult? result = null;
        await RunAsync(L.T("AdvancedTools.DownloadingFiles"), async (p, ct) => result = await LzmaFileTool.DownloadListAsync(Services.Http, list, baseUrl, output, p, ct),
            operation: "download", output: output, onSuccess: () => ReportBatch(result!, "files", output, L.T("AdvancedTools.DownloadingFiles")));
    }
}
