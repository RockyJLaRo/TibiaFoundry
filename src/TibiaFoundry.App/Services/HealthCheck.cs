using TibiaFoundry.Core.Common;
using System.Diagnostics;
using System.IO.Compression;
using ImageMagick;
using TibiaFoundry.App.Themes;
using TibiaFoundry.Core.Compression;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Theming;
using TibiaFoundry.Core.Tools;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

/// <summary>How serious a self-check finding is.</summary>
public enum HealthSeverity
{
    Ok,
    /// <summary>Worth knowing; nothing is wrong.</summary>
    Info,
    /// <summary>Something may cause a problem later (for example a folder that does not exist yet).</summary>
    Warning,
    /// <summary>Prevents an operation from working until it is resolved.</summary>
    Error,
}

/// <summary>A remedy the user can apply from the Diagnostics page (always after confirming).</summary>
public enum HealthFix { None, CreateFolder, UseDefaultFolder }

/// <summary>One finding of the self-check.</summary>
public sealed record HealthResult(string Area, string Check, HealthSeverity Severity, string Message, string? Remedy = null,
    HealthFix Fix = HealthFix.None, OutputKind? Folder = null, long Milliseconds = 0)
{
    public string SeverityText => Severity switch { HealthSeverity.Ok => L.T("Dialog.OK"), HealthSeverity.Info => L.T("Choice.LogLevel.Info"), HealthSeverity.Warning => L.T("HealthCheck.Warning"), _ => "Error" };
}

/// <summary>
/// The self-check of the Diagnostics page: configuration, output folders and their write
/// permissions, the log folder, dependencies of the registered tools, settings migration notes and
/// quick in-memory checks of the main subsystems. It only reads and probes (a probe file is
/// created and removed in existing folders); repairs are offered, never applied automatically.
/// </summary>
public static class HealthCheck
{
    /// <param name="readOnlyRun">The command-line self-test: it never saves settings by design, so that
    /// is not reported as a problem of the user's configuration.</param>
    public static IReadOnlyList<HealthResult> Run(AppServices services, SettingsLoadStatus? loadStatus = null, bool readOnlyRun = false)
    {
        var results = new List<HealthResult>();
        void Add(HealthResult r) => results.Add(r);
        var s = services.Settings;

        // ------------------------------------------------------------ configuration
        Add(new("Configuration", L.T("HealthCheck.SettingsFile"), File.Exists(services.SettingsStore.FilePath) ? HealthSeverity.Ok : HealthSeverity.Info,
            File.Exists(services.SettingsStore.FilePath) ? L.F("HealthCheck.0Format1", services.SettingsStore.FilePath, s.Version) : L.F("HealthCheck.0DoesNotExistYetIt", services.SettingsStore.FilePath)));
        if (loadStatus == SettingsLoadStatus.RecoveredFromCorruption)
            Add(new("Configuration", L.T("HealthCheck.SettingsFile"), HealthSeverity.Warning, L.T("HealthCheck.TheSettingsFileWasDamagedAt"), L.T("HealthCheck.CheckYourSettingsInSettings")));
        if (s.Version > AppSettings.CurrentVersion || s.LoadedVersion > AppSettings.CurrentVersion)
            Add(new("Configuration", L.T("HealthCheck.SettingsFormat"), HealthSeverity.Info, L.F("HealthCheck.TheSettingsWereWrittenByA", s.Version)));
        if (s.UnknownSettings is { Count: > 0 } unknown)
            Add(new("Configuration", L.T("HealthCheck.SettingsOfOtherVersions"), HealthSeverity.Info, L.F("HealthCheck.0OfAnotherVersionAreKept", L.Count(unknown.Count, "Noun.setting"), string.Join(", ", unknown.Keys.Take(8)))));
        foreach (string note in s.MigrationNotes) Add(new("Configuration", L.T("HealthCheck.UpgradeFromAnEarlierVersion"), HealthSeverity.Info, note));
        if (!services.PersistSettings && !readOnlyRun) Add(new("Configuration", L.T("HealthCheck.SavingSettings"), HealthSeverity.Warning, L.T("HealthCheck.SettingsChangesOfThisSessionAre"), L.T("HealthCheck.TurnSaveSettingsChangesMadeIn")));
        if (s.Themes.ActiveCustomTheme != null && s.Themes.Find(s.Themes.ActiveCustomTheme) == null)
            Add(new("Configuration", "Theme", HealthSeverity.Warning, L.T("HealthCheck.TheActiveCustomThemeNoLonger")));
        if (ThemeManager.Engine is { Current.Count: > 0 } engine)
        {
            var warnings = ThemeAccessibility.Check(engine.Current);
            Add(new("Configuration", L.T("HealthCheck.ThemeReadability"), warnings.Count == 0 ? HealthSeverity.Ok : HealthSeverity.Warning,
                warnings.Count == 0 ? L.T("HealthCheck.TheActiveThemeSColorsMeet") : $"{L.Count(warnings.Count, "Noun.readability_problem")}: {warnings[0].Message}",
                warnings.Count == 0 ? null : L.T("HealthCheck.AdjustTheColorsInSettingsAppearance")));
        }

        // ------------------------------------------------------------ folders
        Add(new("Folders", L.T("HealthCheck.DocumentsFolder"), OutputLocations.DocumentsFolder != null ? HealthSeverity.Ok : HealthSeverity.Warning,
            OutputLocations.DocumentsFolder != null ? L.F("HealthCheck.WindowsReports0", OutputLocations.DocumentsFolder) : L.T("HealthCheck.WindowsReportsNoDocumentsFolderSo"),
            OutputLocations.DocumentsFolder != null ? null : L.T("HealthCheck.ChooseAnOutputFolderInSettings")));
        foreach (var info in OutputLocations.Kinds)
        {
            string path = services.Outputs.FolderFor(info.Kind);
            bool chosen = services.Outputs.IsExplicit(info.Kind);
            var state = OutputLocations.Check(path);
            if (!state.Usable)
                Add(new("Folders", info.DisplayName, HealthSeverity.Error, $"{path}: {state.Problem}",
                    chosen ? L.T("HealthCheck.ChooseAnotherFolderInSettingsFiles") : L.T("HealthCheck.ChooseAFolderInSettingsFiles"),
                    chosen ? HealthFix.UseDefaultFolder : HealthFix.None, info.Kind));
            else if (!state.Exists)
                Add(new("Folders", info.DisplayName, HealthSeverity.Info, L.F("HealthCheck.0DoesNotExistYetIt2", state.Path),
                    L.T("HealthCheck.NothingToDoYouCanCreate"), HealthFix.CreateFolder, info.Kind));
            else
                Add(new("Folders", info.DisplayName, HealthSeverity.Ok, L.F("HealthCheck.0Writable1", state.Path, (chosen ? L.T("HealthCheck.ChosenInSettings") : ""))));
        }
        foreach (var (key, folder) in s.General.LastFolders.Where(kv => kv.Key.EndsWith(".output", StringComparison.OrdinalIgnoreCase)))
        {
            var state = OutputLocations.Check(folder);
            if (!state.Usable)
                Add(new("Folders", L.T("HealthCheck.LastUsedFolder"), HealthSeverity.Warning, L.F("HealthCheck.0RememberedFor12", folder, key[..^7], state.Problem), L.T("HealthCheck.SettingsGeneralForgetRememberedFoldersClears")));
        }

        // ------------------------------------------------------------ client locations the user chose (both clients)
        foreach (var scope in new[] { Core.Locations.ClientScope.Live, Core.Locations.ClientScope.TestServer })
        {
            var locations = services.Data.LocationsFor(scope);
            foreach (var def in Core.Locations.LocationResolver.All.Where(d => locations.ChosenPath(d.Id) != null && (d.PerClient || scope == Core.Locations.ClientScope.Live)))
            {
                var r = locations.Resolve(def.Id);
                string name = def.Name + LocationService.ClientSuffix(def.Id, scope);
                Add(r.ChosenUnavailable
                    ? new(L.T("HealthCheck.ClientLocations"), name, HealthSeverity.Warning, $"{r.ChosenPath}: {r.ChosenProblem}" + (r.Found ? L.F("HealthCheck.TheOneFoundAutomaticallyIsUsed", r.Path) : ""),
                        L.T("HealthCheck.LocateItAgainInSettingsTibia"))
                    : new(L.T("HealthCheck.ClientLocations"), name, HealthSeverity.Ok, L.F("HealthCheck.0ChosenInSettings", r.ChosenPath)));
            }
        }

        // ------------------------------------------------------------ client archive (8.0)
        if (services.Clients.IsConfigured)
        {
            try
            {
                if (services.Clients.StoreExists)
                {
                    var store = services.Clients.Store;
                    int damaged = store.Registry.Versions.Count(v => !v.IsUsable);
                    Add(damaged == 0
                        ? new(L.T("HealthCheck.ClientArchive"), "Archive", HealthSeverity.Ok, $"{services.Clients.Root} ({L.Count(store.Registry.Versions.Count, "Noun.revision")})")
                        : new(L.T("HealthCheck.ClientArchive"), "Archive", HealthSeverity.Warning, L.Count(store.Registry.Versions.Count, "HealthCheck.RevisionsDamaged", damaged, services.Clients.Root),
                            L.T("HealthCheck.ClientsVerifyStoreListsThemArchiving")));
                    foreach (var leftover in store.LeftoverRebuilds())
                        Add(new(L.T("HealthCheck.ClientArchive"), L.T("HealthCheck.UnfinishedRebuild"), HealthSeverity.Warning, L.F("HealthCheck.0For1WasLeftWhen", leftover.StagingFolder, leftover.Target),
                            L.T("HealthCheck.ClientsVerifyStoreOffersToRemove")));
                }
                else Add(new(L.T("HealthCheck.ClientArchive"), "Archive", HealthSeverity.Info, L.F("HealthCheck.NothingIsArchivedYet0", services.Clients.Root)));
                foreach (var scope in new[] { Core.Locations.ClientScope.Live, Core.Locations.ClientScope.TestServer })
                {
                    string name = Core.Clients.DefaultClients.LabelOf(scope);
                    if (!services.Settings.Monitors(scope)) { Add(new(L.T("HealthCheck.ClientArchive"), name, HealthSeverity.Info, L.T("HealthCheck.NotCheckedAtStartSettingsTibia"))); continue; }
                    if (services.Clients.InstalledStatusOf(scope) is not { } status) continue;
                    if (status.Problem != null)
                        Add(new(L.T("HealthCheck.ClientArchive"), name, HealthSeverity.Info, L.T("HealthCheck.TheCheckAtThisStartWas") + status.Problem, L.T("HealthCheck.ItIsCheckedAgainAtThe")));
                    else
                        Add(new(L.T("HealthCheck.ClientArchive"), name, status.NeedsArchive ? HealthSeverity.Info : HealthSeverity.Ok,
                            (status.ArchivedAs != null ? L.F("HealthCheck.ArchivedAs0", status.ArchivedAs.Label) : status.Dismissed ? L.T("HealthCheck.NotArchivedDonTAskFor") : L.T("HealthCheck.NotArchivedYet")) +
                            (status.CheckedUtc is { } at ? L.F("HealthCheck.Checked0YyyyMMDdHH", at.ToLocalTime()) : "."),
                            status.NeedsArchive ? L.T("HealthCheck.ClientsArchiveKeepsThisVersion") : null));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Add(new(L.T("HealthCheck.ClientArchive"), "Archive", HealthSeverity.Warning, L.T("HealthCheck.TheClientArchiveCannotBeRead") + ex.Message, L.T("HealthCheck.CheckTheFolderInSettingsTibia")));
            }
        }

        // ------------------------------------------------------------ hotkey copies left by the tool removed in 8.0
        foreach (var backup in LeftoverHotkeyBackups.Find(services.LocalDataDirectory))
            Add(new(L.T("HealthCheck.ClientLocations"), L.T("HealthCheck.ClientHotkeys"), HealthSeverity.Warning, LeftoverHotkeyBackups.Describe(backup),
                L.T("HealthCheck.CopyItBackOnlyIfYour")));

        // ------------------------------------------------------------ logs
        if (AppLog.LogFilePath != null && AppLog.LogDirectory != null && OutputLocations.Check(AppLog.LogDirectory).Usable)
            Add(new("Logs", L.T("HealthCheck.LogFolder"), HealthSeverity.Ok, $"{AppLog.LogFilePath} ({L.Count(AppLog.Files().Count, "Noun.file")}, {ReportWriter.FormatSize(AppLog.Files().Sum(f => f.Length))})"));
        else
            Add(new("Logs", L.T("HealthCheck.LogFolder"), HealthSeverity.Error, L.F("HealthCheck.TheLogCannotBeWritten0", AppLog.LogDirectory ?? L.T("HealthCheck.NoFolder")), L.T("HealthCheck.ChooseAnotherLogFolderInSettings")));

        // ------------------------------------------------------------ tools and dependencies
        foreach (var rejected in services.Tools.Rejected)
            Add(new("Tools", rejected.Name, HealthSeverity.Error, L.T("HealthCheck.NotLoaded") + string.Join(" ", rejected.Problems), L.T("HealthCheck.ThisIsADefectOfThe")));
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in services.Tools.Tools)
        {
            var availability = services.Tools.Availability(tool);
            if (!availability.CanRun)
                Add(new("Tools", tool.Name, HealthSeverity.Error, availability.Problem!, string.Join(" ", availability.Dependencies.Where(d => d.Blocks).Select(d => d.Dependency.Resolution).Distinct())));
            foreach (var d in availability.Dependencies.Where(d => d.State is DependencyState.Missing or DependencyState.Incompatible && !(d.Blocks && d.Dependency.RequiredFor.Count == 0)))
                Add(new("Tools", tool.Name, d.Dependency.Required ? HealthSeverity.Warning : HealthSeverity.Info,
                    d.Dependency.Required ? L.F("HealthCheck.0NeededFor1", d.Message, string.Join(", ", d.Dependency.RequiredFor.Select(op => tool.Operations.FirstOrDefault(o => o.Id == op)?.Name ?? op))) : d.Message,
                    d.Dependency.Resolution));
            foreach (var d in availability.Dependencies.Where(d => d.State == DependencyState.Available))
                if (reported.Add(d.Dependency.Id)) Add(new("Dependencies", d.Dependency.Name, HealthSeverity.Ok, d.Message));
        }
        if (results.All(r => r.Area != "Tools")) Add(new("Tools", L.T("HealthCheck.RegisteredTools"), HealthSeverity.Ok, L.F("HealthCheck.0RegisteredEveryRequiredDependencyIs", L.Count(services.Tools.Tools.Count, "Noun.tool"))));

        // ------------------------------------------------------------ subsystems (in memory)
        Probe(results, L.T("HealthCheck.ImageEncodingImageMagick"), () =>
        {
            using var images = new MagickImageCollection();
            for (int i = 0; i < 2; i++) images.Add(new MagickImage(MagickColors.Red, 2, 2) { AnimationDelay = 10 });
            byte[] gif = images.ToByteArray(MagickFormat.Gif);
            using var back = new MagickImageCollection(gif);
            return back.Count == 2 ? L.F("HealthCheck.2FrameGIFEncodedAndDecoded", gif.Length, typeof(MagickImage).Assembly.GetName().Version?.ToString(3)) : throw new InvalidDataException($"{back.Count} frames read back");
        });
        Probe(results, L.T("HealthCheck.LZMASpriteSheets"), () =>
        {
            byte[] data = new byte[4096];
            new Random(6).NextBytes(data.AsSpan(0, 2048));
            byte[] packed = CipLzma.Compress(data);
            return CipLzma.Decompress(packed).AsSpan().SequenceEqual(data) ? L.F("HealthCheck.RoundTripOf0Bytes", data.Length) : throw new InvalidDataException("the round trip changed the data");
        });
        Probe(results, L.T("HealthCheck.ZlibQtResources"), () =>
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(L.T("HealthCheck.TibiaFoundry"), 64)));
            using var packed = new MemoryStream();
            using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
            packed.Position = 0;
            using var unpacked = new MemoryStream();
            using (var z = new ZLibStream(packed, CompressionMode.Decompress)) z.CopyTo(unpacked);
            return unpacked.ToArray().AsSpan().SequenceEqual(data) ? L.F("HealthCheck.RoundTripOf0Bytes", data.Length) : throw new InvalidDataException("the round trip changed the data");
        });
        Probe(results, L.T("HealthCheck.ProtocolBuffersClientData"), () =>
        {
            var message = new Tibia.Protobuf.Appearances.Appearances();
            message.Object.Add(new Tibia.Protobuf.Appearances.Appearance { Id = 100 });
            var back = Tibia.Protobuf.Appearances.Appearances.Parser.ParseFrom(Google.Protobuf.MessageExtensions.ToByteArray(message));
            return back.Object.Count == 1 && back.Object[0].Id == 100 ? L.T("HealthCheck.AppearancesSchemaRoundTrip") : throw new InvalidDataException("the round trip changed the message");
        });
        Probe(results, L.T("HealthCheck.ThemePalettes"), () =>
        {
            if (ThemeManager.Engine is not { } engine) return L.T("HealthCheck.NotCheckedNoInterface");
            int light = engine.BaseColors(ThemeBase.Light).Count, dark = engine.BaseColors(ThemeBase.Dark).Count;
            return light == ThemeTokens.All.Count && dark == ThemeTokens.All.Count ? L.F("HealthCheck.0ColorsInEachBuiltIn", light) : throw new InvalidDataException($"light {light}, dark {dark}, expected {ThemeTokens.All.Count}");
        });
        return results;
    }

    private static void Probe(List<HealthResult> results, string name, Func<string> body)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            string detail = body();
            results.Add(new HealthResult("Subsystems", name, HealthSeverity.Ok, detail, Milliseconds: watch.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            AppLog.Error($"Self-check \"{name}\" failed.", ex, source: "Diagnostics");
            results.Add(new HealthResult("Subsystems", name, HealthSeverity.Error, $"{ex.GetType().Name}: {ex.Message}",
                L.T("HealthCheck.ReinstallOrReExtractTibiaFoundry"), Milliseconds: watch.ElapsedMilliseconds));
        }
    }
}
