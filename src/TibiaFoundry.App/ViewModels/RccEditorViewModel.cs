using TibiaFoundry.Core.Common;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Assets;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Editing;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Rcc;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>One resource in the RCC Editor's list.</summary>
public sealed partial class RccEntryItem(RccEntry entry) : ObservableObject
{
    public RccEntry Entry { get; } = entry;
    public string Path => Entry.Path;
    public string Name => Entry.Name;
    public string Folder => Entry.Folder;
    public string Type => Entry.Extension.TrimStart('.').ToUpperInvariant();
    public string SizeText => Entry.Size is long size ? ReportWriter.FormatSize(size) : "?";
    public long SortSize => Entry.Size ?? Entry.StoredSize;
    public string CompressionText => Entry.Compression switch { RccCompression.Zlib => "zlib", RccCompression.Zstd => "Zstandard", _ => "" };
    public bool IsModified => Entry.IsModified;

    public void Refresh()
    {
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(CompressionText));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Folder));
    }
}

/// <summary>
/// RCC Editor: opens Qt resource files (.rcc) such as the client's graphics_resources.rcc, shows
/// and previews every resource, replaces, adds, renames and removes resources, extracts them
/// (keeping folders) and saves a validated file. Re-implements the features of Nekiro's Rcc Editor
/// without the external rcc.exe it depends on.
/// </summary>
public sealed partial class RccEditorViewModel : PageViewModel
{
    private RccArchive? _archive;
    private List<RccEntryItem> _all = [];

    public RccEditorViewModel(AppServices services) : base(services, BuiltInTools.RccEditor) { }


    public override string? UnsavedChanges => _archive is { IsModified: true } ? L.F("RccEditor.RCCEditorChangesTo0", FileName) : null;

    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _typeFilter = "all";
    [ObservableProperty] private RccEntryItem? _selected;
    [ObservableProperty] private ImageSource? _previewImage;
    [ObservableProperty] private string? _previewText;
    [ObservableProperty] private string _previewInfo = "";
    [ObservableProperty] private string _validation = "";
    [ObservableProperty] private bool _validationOk = true;
    [ObservableProperty] private RccCompressionChoice _compression = RccCompressionChoice.Automatic;

    public ObservableCollection<RccEntryItem> Entries { get; } = [];

    /// <summary>Items selected in the list (set by the view).</summary>
    public IReadOnlyList<RccEntryItem> SelectedItems { get; set; } = [];

    public bool HasFile => _archive != null;
    public string FileName => FilePath == null ? L.T("RccEditor.NewFile") : System.IO.Path.GetFileName(FilePath);
    public string Summary => _archive == null ? L.T("RccEditor.NoFileOpen") :
        L.F("RccEditor.0N0ResourcesFormat12", _archive.Entries.Count, _archive.FormatVersion, _archive.Entries.Count(e => e.IsModified)) + (_archive.IsModified ? L.T("RccEditor.NotSaved") : "");
    public bool IsClientFile => FilePath != null && AssetsProject.IsInsideClientInstallation(FilePath);

    public static IReadOnlyList<KeyValuePair<RccCompressionChoice, string>> CompressionChoices { get; } =
    [
        new(RccCompressionChoice.Automatic, L.T("RccEditor.LikeTheReplacedResource")),
        new(RccCompressionChoice.QtDefault, L.T("RccEditor.QtSRuleZlibIfIt")),
        new(RccCompressionChoice.Never, L.T("RccEditor.NeverCompress")),
        new(RccCompressionChoice.Always, L.T("RccEditor.AlwaysCompressZlib")),
    ];

    /// <summary>The client's resource file (chosen, or bin\graphics_resources.rcc of the client in use), if there is one.</summary>
    public string? ClientResourceFile => Services.Data.ResourceFile;

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnTypeFilterChanged(string value) => ApplyFilter();
    partial void OnSelectedChanged(RccEntryItem? value) => UpdatePreview();

    private void ApplyFilter()
    {
        string s = Search.Trim();
        var shown = _all.Where(i =>
            (s.Length == 0 || i.Path.Contains(s, StringComparison.OrdinalIgnoreCase)) &&
            TypeFilter switch
            {
                "images" => i.Entry.Extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".webp",
                "text" => i.Entry.Extension is ".json" or ".css" or ".txt" or ".qml" or ".js" or ".fnt" or ".xml" or ".hints" or ".ini",
                "changed" => i.IsModified,
                _ => true,
            });
        Entries.Clear();
        foreach (var item in shown) Entries.Add(item);
    }

    private void Reload(RccArchive archive, string? path)
    {
        _archive = archive;
        FilePath = path;
        _all = archive.Entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).Select(e => new RccEntryItem(e)).ToList();
        ApplyFilter();
        Selected = Entries.FirstOrDefault();
        RaiseState();
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(HasFile));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsClientFile));
    }

    private bool ConfirmDiscard() =>
        _archive is not { IsModified: true } ||
        Services.Dialogs.Confirm(L.F("RccEditor.0HasUnsavedChangesDiscardThem", FileName), L.T("RccEditor.UnsavedChanges"), L.T("RccEditor.Discard"), L.T("Common.Cancel"));

    public override async Task OnNavigatedToAsync()
    {
        var g = Services.Settings.General;
        if (_archive == null && g.ReopenLastFiles && g.LastFiles.TryGetValue(ToolKeys.RccEditor, out string? last) && File.Exists(last))
            await OpenFileAsync(last);
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!ConfirmDiscard()) return;
        // An imported version's resource file is a stored object: its folder is no place to browse.
        string? start = Services.Settings.InputFolderFor(ToolKeys.RccEditor) ?? (Services.Data.ImportedVersion == null ? System.IO.Path.GetDirectoryName(ClientResourceFile) : null);
        string? file = Services.Dialogs.OpenFile(L.T("RccEditor.OpenAQtResourceFile"), L.T("RccEditor.QtResourceFilesRccRccAll"), start);
        if (file != null) await OpenFileAsync(file);
    }

    /// <summary>The file name to offer for "Save as": the client's own name, also for an imported version (whose stored file has none).</summary>
    private string SuggestedFileName()
    {
        if (FilePath == null) return "resources.rcc";
        foreach (var (name, path) in Services.Data.ResourceFiles)
            if (string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase)) return System.IO.Path.GetFileName(name);
        return System.IO.Path.GetFileName(FilePath);
    }

    [RelayCommand]
    private async Task OpenClientFileAsync()
    {
        if (!ConfirmDiscard()) return;
        if (ClientResourceFile is { } file) await OpenFileAsync(file);
        else if (Services.Dialogs.Confirm(L.F("RccEditor.TheClientSResourceFileWas", Services.Locations.Resolve(Core.Locations.LocationIds.ResourceFile).DisplayPath), L.T("RccEditor.RCCEditor"), L.T("RccEditor.Locate"), L.T("Common.Cancel"))
                 && await Services.Locations.LocateAsync(Core.Locations.LocationIds.ResourceFile) && ClientResourceFile is { } located)
            await OpenFileAsync(located);
    }

    public async Task OpenFileAsync(string file)
    {
        RccArchive? archive = null;
        RccValidationReport? report = null;
        bool ok = await RunAsync(L.T("RccEditor.Opening") + System.IO.Path.GetFileName(file), async (_, ct) =>
        {
            (archive, report) = await Task.Run(() =>
            {
                byte[] data = ProtoReadShared(file);
                return (RccArchive.Parse(data), RccValidator.Validate(data));
            }, ct);
        });
        if (!ok || archive == null) return;
        Reload(archive, file);
        Validation = report!.Summary;
        ValidationOk = report.IsValid;
        Services.Settings.RememberFolder(ToolKeys.RccEditor, "input", System.IO.Path.GetDirectoryName(file));
        Services.Settings.General.LastFiles[ToolKeys.RccEditor] = file;
        Services.SaveSettings();
        Info(L.F("RccEditor.01N0Resources2", System.IO.Path.GetFileName(file), archive.Entries.Count, (IsClientFile ? L.T("RccEditor.ThisIsTheInstalledClientS") : "")));
    }

    private static byte[] ProtoReadShared(string file) => Core.Protobuf.ProtoLoader.ReadShared(file);

    private void UpdatePreview()
    {
        PreviewImage = null;
        PreviewText = null;
        var e = Selected?.Entry;
        if (e == null)
        {
            PreviewInfo = "";
            return;
        }
        var info = new StringBuilder($"{e.Path}\n{(e.Size is long s ? ReportWriter.FormatSize(s) : "size unknown")}");
        if (e.Compression != RccCompression.None) info.Append(L.F("RccEditor.Stored01", ReportWriter.FormatSize(e.StoredSize), (e.Compression == RccCompression.Zlib ? "zlib" : "Zstandard")));
        if (e.LastModifiedUtc is { } t) info.Append(L.F("RccEditor.Modified0YyyyMMDdHH", t));
        if (e.IsModified) info.Append(L.T("RccEditor.ChangedInThisSession2"));
        long limit = Services.Settings.Performance.PreviewSizeLimitMegabytes * 1024L * 1024;
        if (!e.CanReadContent)
        {
            info.Append(L.T("RccEditor.ZstandardCompressedThisVersionCannotDecompress"));
        }
        else if ((e.Size ?? 0) > limit)
        {
            info.Append(L.F("RccEditor.NotPreviewedLargerThan0MB", Services.Settings.Performance.PreviewSizeLimitMegabytes));
        }
        else
        {
            try
            {
                byte[] content = e.GetContent();
                var id = FileSignatures.Identify(content.AsSpan(0, Math.Min(content.Length, FileSignatures.HeaderLength)), e.Name, content.Length);
                if (id.Format?.Category == "Image" && id.Format != FileSignatures.Svg)
                {
                    var image = ImageIO.LoadFromBytes(content);
                    PreviewImage = Images.ToBitmapSource(image);
                    info.Append($"\n{id.Format.Name}, {image.Width}×{image.Height}");
                }
                else if (id.Format?.Category == "Text")
                {
                    string text = Encoding.UTF8.GetString(content);
                    PreviewText = text.Length > 20000 ? text[..20000] + "\n…" : text;
                    info.Append($"\n{id.Format.Name}");
                }
                else
                {
                    info.Append(L.F("RccEditor.0NoPreview", id.Format?.Name ?? L.T("RccEditor.BinaryData")));
                }
                if (id.Format != null && id.Evidence == FormatEvidence.Signature && id.Format.Category != "Text" && !id.Format.ExtensionMatches(e.Name))
                    info.Append(L.F("RccEditor.NoteTheContentIsA0", id.Format.Name, e.Extension));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or System.Runtime.InteropServices.ExternalException or NotSupportedException or ImageMagick.MagickException)
            {
                info.Append(L.T("RccEditor.TheContentCouldNotBePreviewed") + ex.Message);
            }
        }
        PreviewInfo = info.ToString();
    }

    private void AfterEdit(string message)
    {
        foreach (var item in _all) item.Refresh();
        _all = _archive!.Entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).Select(e => _all.FirstOrDefault(i => i.Entry == e) ?? new RccEntryItem(e)).ToList();
        var keep = Selected?.Entry;
        ApplyFilter();
        Selected = Entries.FirstOrDefault(i => i.Entry == keep) ?? Entries.FirstOrDefault();
        UpdatePreview();
        RaiseState();
        Info(message);
    }

    /// <summary>Replaces the selected resource with a file (also used for drag and drop).</summary>
    [RelayCommand]
    private void Replace()
    {
        if (Selected == null) return;
        string? file = Services.Dialogs.OpenFile(L.F("RccEditor.Replace0", Selected.Name), L.F("RccEditor.0Files1AllFiles", Selected.Type, Selected.Entry.Extension), Services.Settings.InputFolderFor(ToolKeys.RccEditor));
        if (file != null) ReplaceWith(Selected, file);
    }

    public void ReplaceWith(RccEntryItem item, string file)
    {
        if (_archive == null) return;
        Try(L.T("RccEditor.Replacing") + item.Name, () =>
        {
            byte[] content = File.ReadAllBytes(file);
            var warnings = RccOperations.CheckReplacement(item.Entry, content);
            if (warnings.Count > 0)
            {
                AppLog.Warn(string.Join(" ", warnings));
                if (Services.Settings.Safety.WarnUnsupportedFormats &&
                    !Services.Dialogs.Confirm(string.Join("\n\n", warnings) + L.T("RccEditor.ReplaceAnyway"), L.T("RccEditor.CheckTheReplacement"), L.T("RccEditor.Replace2"), L.T("Common.Cancel"))) return;
            }
            _archive.Replace(item.Entry, content, Compression);
            Services.Settings.RememberFolder(ToolKeys.RccEditor, "input", System.IO.Path.GetDirectoryName(file));
            AfterEdit(L.F("RccEditor.0ReplacedWith1NotSaved", item.Path, System.IO.Path.GetFileName(file)));
        });
    }

    [RelayCommand]
    private void Add()
    {
        if (_archive == null) return;
        var files = Services.Dialogs.OpenFiles(L.T("RccEditor.AddFilesAsNewResources"), L.T("RccEditor.AllFiles"), Services.Settings.InputFolderFor(ToolKeys.RccEditor));
        if (files == null || files.Length == 0) return;
        string folder = Services.Dialogs.Prompt(L.T("RccEditor.AddResources"), L.T("RccEditor.FolderInsideTheResourceFileFor"), Selected?.Folder ?? "/") ?? "";
        if (folder.Length == 0) return;
        Try(L.T("RccEditor.AddingResources"), () =>
        {
            int added = 0;
            foreach (var f in files)
            {
                _archive.Add(folder.TrimEnd('/') + "/" + System.IO.Path.GetFileName(f), File.ReadAllBytes(f), Compression == RccCompressionChoice.Automatic ? RccCompressionChoice.QtDefault : Compression,
                    (ulong)new DateTimeOffset(File.GetLastWriteTimeUtc(f)).ToUnixTimeMilliseconds());
                added++;
            }
            AfterEdit(L.F("RccEditor.0AddedTo1NotSaved", L.Count(added, "Noun.resource"), folder));
        });
    }

    [RelayCommand]
    private void Rename()
    {
        if (_archive == null || Selected == null) return;
        string? path = Services.Dialogs.Prompt(L.T("RccEditor.RenameOrMove"), L.T("RccEditor.NewPathOfTheResourcePrograms"), Selected.Path);
        if (path == null || path == Selected.Path) return;
        var item = Selected;
        Try(L.T("RccEditor.Renaming"), () =>
        {
            _archive.Rename(item.Entry, path);
            AfterEdit(L.F("RccEditor.RenamedTo0NotSavedYet", item.Entry.Path));
        });
    }

    [RelayCommand]
    private void Remove()
    {
        if (_archive == null) return;
        var items = SelectedItems.Count > 0 ? SelectedItems.ToList() : Selected != null ? [Selected] : [];
        if (items.Count == 0) return;
        if (!Services.Dialogs.Confirm(L.F("RccEditor.Remove0From123", L.Count(items.Count, "Noun.resource"), FileName, string.Join("\n", items.Take(10).Select(i => i.Path)), (items.Count > 10 ? "\n…" : "")),
                L.T("RccEditor.RemoveResources"), L.T("Common.Remove"), L.T("Common.Cancel"))) return;
        foreach (var i in items) _archive.Remove(i.Entry);
        AfterEdit(L.F("RccEditor.0RemovedNotSavedYet", L.Count(items.Count, "Noun.resource")));
    }

    [RelayCommand]
    private Task ExtractSelectedAsync() => ExtractAsync(SelectedItems.Count > 0 ? SelectedItems.Select(i => i.Entry).ToList() : Selected != null ? [Selected.Entry] : []);

    [RelayCommand]
    private Task ExtractAllAsync() => ExtractAsync(_archive?.Entries.ToList() ?? []);

    private async Task ExtractAsync(List<RccEntry> entries)
    {
        if (entries.Count == 0) return;
        string? folder = Services.Outputs.PickFolder(OutputKind.RccExports, L.T("RccEditor.ExtractToFoldersInsideTheResource"));
        if (folder == null) return;
        if (AssetsProject.IsInsideClientInstallation(folder))
        {
            Services.Dialogs.Show(L.T("RccEditor.FoldersOfTheInstalledTibiaClient"), MessageKind.Warning, L.T("RccEditor.Extraction"));
            return;
        }
        RccExtractResult? result = null;
        string description = "";
        await RunAsync(L.T("RccEditor.ExtractingResources"), async (progress, ct) =>
        {
            using var output = Services.CreateOutput("RCC extraction");
            result = await Task.Run(() => RccOperations.Extract(entries, folder, output, true, progress, ct), ct);
            description = output.Describe();
        }, operation: "extract", output: folder, onSuccess: () =>
        {
            var batch = new Core.Common.BatchResult { Succeeded = result!.Written };
            batch.Failures.AddRange(result.Failures);
            batch.Notes.AddRange(result.Notes.Take(3));
            ReportBatch(batch, "resources", folder, L.T("RccEditor.Extraction"));
            if (result.Failures.Count == 0)
                Services.Status.ShowSaved(folder, isFile: false, before: L.F("RccEditor.0ExtractedTo", L.Count(result.Written, "Noun.resource")), after: L.F("RccEditor.WithAManifest01", RccOperations.ManifestFileName, description));
        });
    }

    [RelayCommand]
    private void ImportFolder()
    {
        if (_archive == null) return;
        string? folder = Services.Dialogs.PickFolder(L.T("RccEditor.FolderWithEditedResourcesSameLayout"), Services.Outputs.FolderFor(OutputKind.RccExports));
        if (folder == null) return;
        int answer = Services.Dialogs.Choose(L.T("RccEditor.FilesWhosePathMatchesAResource"),
            L.T("RccEditor.UpdateFromAFolder"), L.T("RccEditor.AlsoAddNewFiles"), L.T("RccEditor.OnlyReplace"));
        if (answer < 0) return;   // Cancel or Esc: nothing changes
        bool addNew = answer == 0;
        Try(L.T("RccEditor.UpdatingFrom") + folder, () =>
        {
            var result = RccOperations.ImportFolder(_archive, folder, addNew, Compression);
            AfterEdit(L.F("RccEditor.0N0Replaced1N0Added", result.Replaced.Count, result.Added.Count, result.Unchanged, result.Skipped.Count));
            if (result.Warnings.Count > 0 && Services.Settings.Safety.WarnUnsupportedFormats)
                Services.Dialogs.Show(string.Join("\n\n", result.Warnings.Take(10)), MessageKind.Warning, L.T("RccEditor.CheckTheseReplacements"));
        });
    }

    [RelayCommand]
    private void NewFromFolder()
    {
        if (!ConfirmDiscard()) return;
        string? folder = Services.Dialogs.PickFolder(L.T("RccEditor.FolderWhoseFilesBecomeTheResources"), Services.Settings.InputFolderFor(ToolKeys.RccEditor));
        if (folder == null) return;
        Try(L.T("RccEditor.CreatingAResourceFile"), () =>
        {
            var archive = RccOperations.CreateFromFolder(folder, 3, Compression == RccCompressionChoice.Automatic ? RccCompressionChoice.QtDefault : Compression);
            Reload(archive, null);
            Validation = L.T("RccEditor.NewFileFormat3NotSaved");
            ValidationOk = true;
            Info(L.F("RccEditor.0N0ResourcesFrom1Use", archive.Entries.Count, folder));
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_archive == null) return;
        if (FilePath == null || IsClientFile)
        {
            if (IsClientFile)
                Services.Dialogs.Show(Services.Data.ImportedVersion != null && FilePath != null && Services.Clients.Root is { } store && Core.FileOps.PathGuard.IsSameOrInside(store, FilePath)
                    ? L.T("RccEditor.ThisIsTheResourceFileOf")
                    : L.T("RccEditor.ThisIsTheInstalledTibiaClient"), MessageKind.Warning, L.T("RccEditor.SaveACopy"));
            await SaveAsAsync();
            return;
        }
        await SaveToAsync(FilePath);
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        if (_archive == null) return;
        string? file = Services.Outputs.SaveFile(OutputKind.RccExports, L.T("RccEditor.SaveTheResourceFile"), L.T("RccEditor.QtResourceFilesRccRcc"), SuggestedFileName());
        if (file == null) return;
        if (AssetsProject.IsInsideClientInstallation(file))
        {
            Services.Dialogs.Show(L.T("RccEditor.FilesInsideATibiaClientOr"), MessageKind.Warning, L.T("RccEditor.SaveACopy"));
            return;
        }
        await SaveToAsync(file);
    }

    private async Task SaveToAsync(string file)
    {
        var archive = _archive!;
        RccValidationReport? report = null;
        bool ok = await RunAsync(L.T("RccEditor.Saving") + System.IO.Path.GetFileName(file), async (_, ct) =>
        {
            await Task.Run(() =>
            {
                byte[] data = archive.Build();
                using var output = new OutputSession(L.T("RccEditor.RCCEditorSave"), Services.Settings.FileSafety with { WhenExists = ExistingFilePolicy.Overwrite }, Services.Backups);
                output.Write(file, temp => File.WriteAllBytes(temp, data), temp =>
                {
                    // Structure, Qt lookup and content of every resource, before anything is replaced.
                    var written = File.ReadAllBytes(temp);
                    report = RccValidator.Validate(written);
                    if (!report.IsValid) throw new InvalidDataException("The written file failed validation: " + report.Errors[0]);
                    var reread = RccArchive.Parse(written);
                    foreach (var e in archive.Entries)
                        if (!(reread.Find(e.Path)?.Stored.AsSpan().SequenceEqual(e.Stored) ?? false)) throw new InvalidDataException($"{e.Path} differs after writing.");
                });
                if (output.BackedUp.Count > 0) AppLog.Info($"RCC Editor: previous {file} backed up to {output.BackupFolder}.");
            }, ct);
        }, operation: "save", output: file);
        if (!ok) return;
        archive.MarkSaved();
        FilePath = file;
        if (report != null)
        {
            Validation = report.Summary;
            ValidationOk = report.IsValid;
        }
        else
        {
            Validation = L.T("RccEditor.SavedWithoutValidationTurnedOffIn");
        }
        AfterEdit(L.F("RccEditor.Saved01", file, Validation));
        Services.Status.ShowSaved(file, isFile: true, after: ". " + Validation);
        Services.Settings.General.LastFiles[ToolKeys.RccEditor] = file;
        Services.SaveSettings();
    }

    [RelayCommand]
    private void OpenFolder() => Services.Shell.OpenFolder(FilePath);
}
