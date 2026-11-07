using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.Tests;

/// <summary>
/// Default output locations under Documents\Tibia_Foundry: resolution, subfolders, precedence,
/// persistence, migration from 5.0, and folders that cannot be used.
/// </summary>
public sealed class OutputLocationTests
{
    [Fact]
    public void TheDefaultRootIsTibiaFoundryInsideTheDocumentsFolderWindowsReports()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Assert.Equal(Path.Combine(documents, "Tibia_Foundry"), OutputLocations.DefaultRoot);
        Assert.Equal(OutputLocations.DefaultRoot, new AppSettings().OutputFolder);
        Assert.DoesNotContain("%", OutputLocations.DefaultRoot);                  // never a literal placeholder
        Assert.DoesNotContain("<", OutputLocations.DefaultRoot);
        Assert.Equal(
            ["GIF_Exports", "Image_Exports", "Sprite_Exports", "Sound_Exports", "Map_Exports", "Data_Exports", "Item_Catalogs", "RCC_Exports", "Client_Converter",
             "Assets_Editor", "Scripts", "Reports", "Manifests", "Backups", "Logs", "Temporary"],
            OutputLocations.Kinds.Select(k => k.Subfolder));
        var s = new AppSettings { OutputFolder = @"C:\Root" };
        Assert.Equal(@"C:\Root\GIF_Exports", s.EffectiveGifFolder);
        Assert.Equal(@"C:\Root\Sprite_Exports", s.EffectiveSpriteExtractionFolder);
        Assert.Equal(@"C:\Root\Sound_Exports", s.EffectiveSoundsFolder);
        Assert.Equal(@"C:\Root\Map_Exports", s.EffectiveMapTilesFolder);
        Assert.Equal(@"C:\Root\Client_Converter", s.OutputFolderFor(ToolKeys.ClientConverter));
        Assert.Equal(@"C:\Root\Reports", s.OutputFolderFor(OutputKind.Reports));
    }

    [Fact]
    public void ARedirectedOrMissingDocumentsFolderIsFollowedNotGuessed()
    {
        string redirected = TestData.NewTempDirectory("onedrive-documents");
        OutputLocations.DocumentsOverrideForTests.Value = redirected;
        try
        {
            Assert.Equal(Path.Combine(redirected, "Tibia_Foundry"), new AppSettings().OutputFolder);
            OutputLocations.DocumentsOverrideForTests.Value = "";                   // Windows reports no Documents folder
            var s = new AppSettings();
            s.Normalize();
            Assert.Equal("", s.OutputFolder);
            var state = OutputLocations.Check(s.OutputFolderFor(OutputKind.GifExports));
            Assert.False(state.Usable);                                               // no silent fallback to another folder
            Assert.Contains("Documents", state.Problem);
        }
        finally
        {
            OutputLocations.DocumentsOverrideForTests.Value = null;
        }
        Assert.Empty(Directory.EnumerateFileSystemEntries(redirected));              // resolving never creates folders
    }

    [Fact]
    public void ChosenFoldersPersistAndTakePrecedence()
    {
        string dir = TestData.NewTempDirectory("outputs");
        var s = new AppSettings { OutputFolder = Path.Combine(dir, "root") };
        s.Normalize();
        // Precedence for a kind without a 5.0 setting: chosen, then last used, then default.
        Assert.Equal(Path.Combine(dir, "root", "Data_Exports"), s.OutputFolderFor(OutputKind.DataExports));
        s.RememberOutputFolder(OutputKind.DataExports, Path.Combine(dir, "last"));
        Assert.Equal(Path.Combine(dir, "last"), s.OutputFolderFor(OutputKind.DataExports));
        foreach (var info in OutputLocations.Kinds) s.SetExplicitFolder(info.Kind, Path.Combine(dir, "chosen", info.Subfolder));
        Assert.Equal(Path.Combine(dir, "chosen", "Data_Exports"), s.OutputFolderFor(OutputKind.DataExports));
        string file = Path.Combine(dir, "settings.json");
        new SettingsStore(file).Save(s);
        var loaded = new SettingsStore(file).Load().Settings;
        foreach (var info in OutputLocations.Kinds)
            Assert.Equal(Path.Combine(dir, "chosen", info.Subfolder), loaded.ExplicitFolder(info.Kind));
        // The 5.0 properties hold the kinds they held before, so 5.0 still finds them.
        Assert.Equal(Path.Combine(dir, "chosen", "GIF_Exports"), loaded.GifFolder);
        Assert.Equal(Path.Combine(dir, "chosen", "RCC_Exports"), loaded.Locations.Tools[ToolKeys.RccEditor].Output);
        loaded.SetExplicitFolder(OutputKind.RccExports, null);
        Assert.False(loaded.Locations.Tools.ContainsKey(ToolKeys.RccEditor));       // "Use default" clears it
        Assert.Equal(Path.Combine(dir, "root", "RCC_Exports"), loaded.OutputFolderFor(OutputKind.RccExports));
    }

    [Fact]
    public void Version5DefaultsMoveToTheNewRootAndChosenFoldersStay()
    {
        string documents = TestData.NewTempDirectory("documents");
        OutputLocations.DocumentsOverrideForTests.Value = documents;
        try
        {
            string old = Path.Combine(documents, "TibiaSprites");
            string file = Path.Combine(TestData.NewTempDirectory("settings5"), "settings.json");
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(new
            {
                Version = 3,
                OutputFolder = old,
                GifFolder = @"D:\My GIFs",
                General = new { RememberLastFolders = true, LastFolders = new Dictionary<string, string> { ["RccEditor.output"] = Path.Combine(old, "RCC"), ["Scripts.output"] = @"D:\Mine" } },
            }));
            var (s, status, _) = new SettingsStore(file).Load();
            Assert.Equal(SettingsLoadStatus.Loaded, status);
            Assert.Equal(3, s.LoadedVersion);
            Assert.Equal(AppSettings.CurrentVersion, s.Version);
            Assert.Equal(Path.Combine(documents, "Tibia_Foundry"), s.OutputFolder);          // the automatic 5.0 default moved
            Assert.Equal(@"D:\My GIFs", s.GifFolder);                                        // a chosen folder is kept
            Assert.False(s.General.LastFolders.ContainsKey("RccEditor.output"));             // only an old default: forgotten
            Assert.Equal(@"D:\Mine", s.General.LastFolders["Scripts.output"]);               // chosen by the user: kept
            Assert.NotEmpty(s.MigrationNotes);
            Assert.False(Directory.Exists(Path.Combine(documents, "Tibia_Foundry")));        // nothing is created or moved
            Assert.False(Directory.Exists(old));

            File.WriteAllText(file, """{ "Version": 3, "OutputFolder": "E:\\Wiki" }""");
            var custom = new SettingsStore(file).Load().Settings;
            Assert.Equal(@"E:\Wiki", custom.OutputFolder);                                    // a root the user chose is kept
            Assert.Equal(@"E:\Wiki\GIF_Exports", custom.EffectiveGifFolder);
        }
        finally
        {
            OutputLocations.DocumentsOverrideForTests.Value = null;
        }
    }

    [Fact]
    public void BackupsOf5AreKeptWhereTheyAreAfterTheUpgrade()
    {
        string dir = TestData.NewTempDirectory("backups5");
        string oldBackups = Path.Combine(dir, "LocalAppData", "TibiaFoundry", "backups");
        Directory.CreateDirectory(Path.Combine(oldBackups, "20261012-120000 RCC Editor save"));
        var settings = new AppSettings { OutputFolder = Path.Combine(dir, "Documents", "Tibia_Foundry"), LoadedVersion = 3 };
        var installed = new AppPaths(Path.Combine(dir, "Roaming", "settings.json"), Path.Combine(dir, "logs"), IsPortable: false, Notice: null, PreviousSettingsFiles: []);
        using var services = new AppServices(new SettingsStore(installed.SettingsFile), settings) { Paths = installed, PreviousDefaultBackupFolder = oldBackups };
        Assert.Equal(Path.Combine(dir, "Documents", "Tibia_Foundry", "Backups"), services.DefaultBackupFolder);
        string? note = services.MigrateFolders();
        Assert.NotNull(note);
        Assert.Equal(oldBackups, services.BackupFolder);                                      // existing backups stay restorable
        Assert.True(Directory.Exists(Path.Combine(oldBackups, "20261012-120000 RCC Editor save")));

        var fresh = new AppSettings { OutputFolder = Path.Combine(dir, "Documents", "Tibia_Foundry"), LoadedVersion = 3 };
        using var empty = new AppServices(new SettingsStore(installed.SettingsFile), fresh) { Paths = installed, PreviousDefaultBackupFolder = Path.Combine(dir, "none") };
        Assert.Null(empty.MigrateFolders());                                                  // no old backups: the new default applies
        Assert.Equal(Path.Combine(dir, "Documents", "Tibia_Foundry", "Backups"), empty.BackupFolder);
    }

    [Fact]
    public void FoldersThatCannotBeUsedAreExplainedAndNothingIsCreatedElsewhere()
    {
        string dir = TestData.NewTempDirectory("folders");
        // Missing: Check does not create it, Prepare does.
        string missing = Path.Combine(dir, "a b", "Ünïcødé 日本");
        Assert.True(OutputLocations.Check(missing) is { Usable: true, Exists: false });
        Assert.False(Directory.Exists(missing));
        Assert.True(OutputLocations.Prepare(missing) is { Usable: true, Exists: true });
        Assert.True(Directory.Exists(missing));
        Assert.Empty(Directory.EnumerateFileSystemEntries(missing));                          // the write probe leaves nothing behind

        // Invalid, relative, a file, and a drive that is not there.
        Assert.Contains("not a valid", OutputLocations.Check(Path.Combine(dir, "bad|name")).Problem);
        Assert.Contains("not a complete folder path", OutputLocations.Check(@"relative\folder").Problem);
        string fileInTheWay = Path.Combine(dir, "file.txt");
        File.WriteAllText(fileInTheWay, "x");
        Assert.Contains("is a file", OutputLocations.Prepare(fileInTheWay).Problem);
        char? unused = Enumerable.Range('D', 23).Select(c => (char)c).Cast<char?>().FirstOrDefault(c => !Directory.Exists($"{c}:\\"));
        if (unused is { } letter)
        {
            var state = OutputLocations.Prepare($@"{letter}:\Tibia_Foundry\GIF_Exports");
            Assert.False(state.Usable);
            Assert.Contains("not available", state.Problem);
        }

        // No permission: a folder this user may not write to.
        string locked = Path.Combine(dir, "locked");
        Directory.CreateDirectory(locked);
        var info = new DirectoryInfo(locked);
        var security = info.GetAccessControl();
        var me = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(me, FileSystemRights.WriteData | FileSystemRights.CreateDirectories | FileSystemRights.AppendData, AccessControlType.Deny);
        security.AddAccessRule(deny);
        info.SetAccessControl(security);
        try
        {
            var state = OutputLocations.Prepare(locked);
            Assert.False(state.Usable);
            Assert.Contains("permission", state.Problem);
            var inside = OutputLocations.Prepare(Path.Combine(locked, "sub"));
            Assert.False(inside.Usable);
        }
        finally
        {
            security.RemoveAccessRule(deny);
            info.SetAccessControl(security);
        }
    }

    [Fact]
    public void NoOutputFolderCanBeInsideATibiaClient()
    {
        string client = TestData.NewTempDirectory("protected-client");
        Core.Editing.AssetsProject.ExtraClientRootForTests = client;
        try
        {
            string inside = Path.Combine(client, "exports");
            var state = OutputLocations.Prepare(inside);   // typed or remembered, not only picked with Browse
            Assert.False(state.Usable);
            Assert.Contains("never written to", state.Problem);
            Assert.False(Directory.Exists(inside));          // nothing is created there
            Assert.False(OutputLocations.Check(client).Usable);
        }
        finally
        {
            Core.Editing.AssetsProject.ExtraClientRootForTests = null;
        }
    }

    [Fact]
    public void SaveDialogsStartInTheKindsFolderAndUnusableFoldersAreNotReplacedSilently()
    {
        string dir = TestData.NewTempDirectory("outputsvc");
        var settings = new AppSettings { OutputFolder = Path.Combine(dir, "root") };
        settings.Normalize();
        using var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var dialogs = new RecordingDialogs();
        services.Dialogs = dialogs;

        dialogs.NextFile = Path.Combine(dir, "elsewhere", "Map.json");
        string? file = services.Outputs.SaveFile(OutputKind.DataExports, "Export", "JSON|*.json", "Map.json");
        Assert.Equal(dialogs.NextFile, file);
        Assert.Equal(Path.Combine(dir, "root", "Data_Exports"), dialogs.LastInitialDirectory);   // the dialog started in the kind's folder (created)
        Assert.True(Directory.Exists(dialogs.LastInitialDirectory));
        Assert.Equal(Path.Combine(dir, "elsewhere"), services.Outputs.FolderFor(OutputKind.DataExports));   // remembered for next time

        // The GIF folder is a file: the user is told and may choose another; cancelling writes nothing anywhere.
        string blocked = Path.Combine(dir, "blocked");
        File.WriteAllText(blocked, "");
        settings.SetExplicitFolder(OutputKind.GifExports, blocked);
        dialogs.ConfirmAnswer = false;
        Assert.Null(services.Outputs.PrepareOrAsk(OutputKind.GifExports, "Export GIF"));
        Assert.Contains(dialogs.Confirmations, c => c.Contains("is a file", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(dir, "root", "GIF_Exports")));            // no silent fallback to the default
    }

    /// <summary>Fake dialogs that also record where file dialogs start and what was asked.</summary>
    private sealed class RecordingDialogs : IDialogService
    {
        public string? NextFile { get; set; }
        public bool ConfirmAnswer { get; set; }
        public string? LastInitialDirectory { get; private set; }
        public List<string> Confirmations { get; } = [];
        public string? OpenFile(string title, string filter, string? initialDirectory = null) { LastInitialDirectory = initialDirectory; return NextFile; }
        public string[]? OpenFiles(string title, string filter, string? initialDirectory = null) => NextFile == null ? null : [NextFile];
        public string? SaveFile(string title, string filter, string defaultFileName, string? initialDirectory = null) { LastInitialDirectory = initialDirectory; return NextFile; }
        public string? PickFolder(string title, string? initialDirectory = null) { LastInitialDirectory = initialDirectory; return NextFile; }
        public void Show(string message, MessageKind kind = MessageKind.Info, string? title = null) { }
        public void ShowError(string message, Exception? exception = null, bool log = true) { }
        public bool Confirm(string message, string title, string? confirmText = null, string? cancelText = null) { Confirmations.Add(message); return ConfirmAnswer; }
        public int Choose(string message, string title, string first, string second, string? cancelText = null) => -1;
        public string? Prompt(string title, string message, string initialText = "", bool multiline = false) => null;
        public ChoiceResult? ChooseFrom(string title, string message, IReadOnlyList<ChoiceItem> items, ChoiceOptions? options = null) => null;
        public ReportAnswer ShowReport(DialogReport report) => ReportAnswer.Cancel;
        public Core.Clients.ArchivePreview? ConfirmArchive(ArchiveConfirmation request) => null;
        public Core.Metadata.AttributionChoice? ChooseAttribution(Core.Metadata.AttributionReview review, string operation) => Core.Metadata.AttributionChoice.KeepOriginalCreators;
        public ConflictAnswer AskConflict(string path, string operation, bool backupsOn) => new(ConflictChoice.Skip, false);
    }
}
