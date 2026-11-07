using System.Diagnostics;
using System.Text;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Items;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Wiki;

namespace TibiaFoundry.Tests;

/// <summary>
/// The Item Catalog (7.0): sources are kept apart, identifier types are never mixed, official names
/// are told apart from community and third-party data, conflicts are reported (never merged),
/// imports are previewed and backed up, the observation log survives interruptions, and exports
/// re-import unchanged. Catalogs are written to temporary folders only. They run on their own
/// (<see cref="CatalogCollection"/>): one of them measures how quickly a large catalog loads.
/// </summary>
[Collection(nameof(CatalogCollection))]
public sealed class ItemCatalogTests(ITestOutputHelper output)
{
    // ------------------------------------------------------------------ helpers

    private static ItemSource Source(string id, SourceKind kind, bool official = false, IdType type = IdType.TibiaObject, string? version = null) => new()
    {
        Id = id, Kind = kind, Name = id, IdType = type, IsOfficial = official, Version = version, ImportedAtUtc = DateTime.UtcNow,
    };

    private static ItemObservation Named(string source, uint id, string? name, ObservationStatus status = ObservationStatus.Unverified, IdType type = IdType.TibiaObject,
        bool title = false) => new()
    {
        SourceId = source, IdType = type, Id = id, Name = name, NameIsTitle = title, Status = name == null ? ObservationStatus.Unresolved : status,
    };

    private static ItemImport Import(ItemSource source, params ItemObservation[] records) => new() { Source = source, Observations = records };

    /// <summary>A catalog with a small client (100 named, 101–103 unnamed), TibiaWiki titles and a list.</summary>
    private static ItemCatalog SampleCatalog(string? folder = null)
    {
        var catalog = ItemCatalogStore.Create(folder ?? Path.Combine(TestData.NewTempDirectory("catalog"), "Sample"), "Sample");
        var client = Source("client", SourceKind.OfficialClient, official: true, version: "15.33.test");
        ItemCatalogStore.Commit(catalog, Import(client,
            Named("client", 100, "void", ObservationStatus.Verified), Named("client", 101, null), Named("client", 102, null), Named("client", 103, null),
            Named("client", 105, "gold coin", ObservationStatus.Verified)));
        ItemCatalogStore.Commit(catalog, Import(Source("wiki", SourceKind.TibiaWiki),
            Named("wiki", 101, "Earth", title: true), Named("wiki", 102, "Dirt", title: true)));
        ItemCatalogStore.Commit(catalog, Import(Source("list", SourceKind.NameList),
            Named("list", 101, "earth"), Named("list", 102, "sand"), Named("list", 104, "mystery thing"), Named("list", 105, "golden coin")));
        return catalog;
    }

    // ------------------------------------------------------------------ the model

    [Fact]
    public void AnInvalidImportIsRefusedBeforeAnythingIsWritten()
    {
        var catalog = SampleCatalog();
        string file = ItemCatalogStore.SnapshotPath(catalog.Folder, "list");
        byte[] before = File.ReadAllBytes(file);
        var bad = new ItemImport
        {
            Source = Source("list", SourceKind.NameList),
            Observations = [Named("list", 101, "replaced")],
            Mappings = [new IdMapping(IdType.OtbServer, 1, null, IdType.TibiaObject, 101, null, "another source", MappingConfidence.Exact)],
        };
        Assert.Throws<ArgumentException>(() => ItemCatalogStore.Commit(catalog, bad));
        Assert.Equal(before, File.ReadAllBytes(file));   // the records file was not replaced
        Assert.Equal("earth", ItemCatalogStore.Load(catalog.Folder).Find(IdType.TibiaObject, 101)!.Observations.Single(o => o.SourceId == "list").Name);
    }

    [Fact]
    public void EachIdentifierSettlesOnANameOnlyWithEvidence()
    {
        var catalog = SampleCatalog();
        CatalogEntry E(uint id) => catalog.Find(IdType.TibiaObject, id)!;

        Assert.Equal(Agreement.VerifiedOfficial, E(100).Agreement);
        Assert.Equal("void", E(100).BestName);
        Assert.False(E(100).HasDisagreement);

        // A TibiaWiki title and a list agree (case and article do not matter): two sources, not verified.
        Assert.Equal(Agreement.Corroborated, E(101).Agreement);
        Assert.Equal("earth", E(101).BestName);   // a real name is preferred over the article title
        Assert.True(E(101).UnnamedInClient);

        // Different names and nothing official: a conflict without a settled name, never a merge.
        Assert.Equal(Agreement.Conflict, E(102).Agreement);
        Assert.Null(E(102).BestName);
        Assert.True(E(102).HasDisagreement);
        Assert.Equal(2, E(102).NameGroups.Count);

        Assert.Equal(Agreement.NoName, E(103).Agreement);
        Assert.Equal(Agreement.SingleSource, E(104).Agreement);
        Assert.False(E(104).InOfficialClient);

        // The client's name stays the verified one when another source disagrees; the disagreement is shown.
        Assert.Equal(Agreement.VerifiedOfficial, E(105).Agreement);
        Assert.Equal("gold coin", E(105).BestName);
        Assert.True(E(105).HasDisagreement);
        Assert.Contains("golden coin (list)", E(105).NamesBySource(catalog.SourcesById));
    }

    [Fact]
    public void IdentifierTypesAreNeverMixed()
    {
        var catalog = SampleCatalog();
        // The same number with another meaning (an items.otb ID, a list of unknown meaning) is a different entry.
        ItemCatalogStore.Commit(catalog, Import(Source("otb", SourceKind.OtItemsOtb, type: IdType.OtbServer, version: "13.10"), Named("otb", 100, "grass", type: IdType.OtbServer)));
        ItemCatalogStore.Commit(catalog, Import(Source("odd", SourceKind.NameList, type: IdType.Unknown), Named("odd", 100, "whatever", type: IdType.Unknown)));

        var official = catalog.Find(IdType.TibiaObject, 100)!;
        Assert.Single(official.Observations);
        Assert.Equal(Agreement.VerifiedOfficial, official.Agreement);
        Assert.Equal("grass", catalog.Find(IdType.OtbServer, 100)!.BestName);
        Assert.Equal("whatever", catalog.Find(IdType.Unknown, 100)!.BestName);
        Assert.Equal(3, catalog.Query(new CatalogQuery { Text = "100" }).Count());
        Assert.Single(catalog.Query(new CatalogQuery { Text = "100", IdType = IdType.OtbServer }));

        // Coverage compares only Tibia object IDs with the client.
        var coverage = CatalogCoverage.Of(catalog);
        var otb = coverage.Sources.Single(s => s.Source.Id == "otb");
        Assert.Null(otb.InClient);
        var list = coverage.Sources.Single(s => s.Source.Id == "list");
        Assert.Equal(1, list.NotInClient);   // 104
        Assert.Equal(1, list.DifferentFromClient);   // 105
        Assert.Equal(2, list.NamesForUnnamed);   // 101, 102
        Assert.Contains("must belong", Assert.Throws<ArgumentException>(() =>
            ItemCatalogStore.Commit(catalog, Import(Source("x", SourceKind.NameList), Named("y", 1, "z")))).Message);
    }

    [Fact]
    public void QueriesFindIdsRangesExactAndPartialNames()
    {
        var catalog = SampleCatalog();
        uint[] Ids(CatalogQuery q) => catalog.Query(q).Select(e => e.Id).ToArray();
        Assert.Equal([101u, 102u, 103u], Ids(new CatalogQuery { Text = "101-103" }));
        Assert.Equal([101u], Ids(new CatalogQuery { Text = "=Earth" }));
        Assert.Equal([105u], Ids(new CatalogQuery { Text = "coin" }));
        Assert.Equal([101u, 102u, 103u], Ids(new CatalogQuery { OnlyUnnamedInClient = true }));
        Assert.Equal([102u, 105u], Ids(new CatalogQuery { OnlyDisagreements = true }));
        Assert.Equal([100u, 103u], Ids(new CatalogQuery { WithoutSource = "list" }));
        Assert.Equal([102u], Ids(new CatalogQuery { Agreement = Agreement.Conflict }));
    }

    // ------------------------------------------------------------------ renaming (7.2)

    [Fact]
    public void TypedNamesAreCleanedAndCheckedTheSameWayEverywhere()
    {
        string? Clean(string? text) => ItemNames.CleanTyped(text, out _);
        string? Problem(string? text) { ItemNames.CleanTyped(text, out string? p); return p; }
        Assert.Equal("a gold coin", Clean("  a   gold coin "));
        Assert.Equal("Ferumbras' teddy", Clean("Ferumbras' teddy"));
        Assert.Equal("Type a name.", Problem(""));
        Assert.Equal("Type a name.", Problem("   "));
        Assert.Equal("Type a name.", Problem(null));
        Assert.Contains("control characters", Problem("two\nlines"));
        Assert.Contains("control characters", Problem("tab\there"));
        Assert.Null(Clean("line\r\n"));
        Assert.Equal(ItemNames.MaxTypedLength, Clean(new string('x', ItemNames.MaxTypedLength))!.Length);
        Assert.Contains("at most 100", Problem(new string('x', ItemNames.MaxTypedLength + 1)));
    }

    [Fact]
    public void ARenameIsKeptAsYourDecisionAndLeavesEveryOtherRecordAlone()
    {
        var catalog = SampleCatalog();
        ItemCatalogStore.Commit(catalog, Import(Source("otb", SourceKind.OtItemsOtb, type: IdType.OtbServer, version: "13.10"), Named("otb", 100, "grass", type: IdType.OtbServer)));
        string sources = Path.Combine(catalog.Folder, ItemCatalogStore.SourcesFolder);
        Dictionary<string, string> Hashes() => Directory.GetFiles(sources).ToDictionary(f => Path.GetFileName(f), f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));
        var before = Hashes();
        var recordsBefore = catalog.Observations.ToList();
        var mappingsBefore = catalog.Mappings.ToList();

        // The client names 100 "void": your name is used, the client's stays visible and the other sources are untouched.
        var entry = catalog.Find(IdType.TibiaObject, 100)!;
        ItemCatalogStore.Rename(catalog, entry, "  a   void   stone ");
        var renamed = catalog.Find(IdType.TibiaObject, 100)!;
        Assert.Equal((Agreement.Decided, "a void stone", "void"), (renamed.Agreement, renamed.BestName, renamed.ClientName));
        Assert.Equal(("a", "void stone"), (renamed.Decision!.Article, renamed.Decision.Name));
        Assert.Equal((IdType.TibiaObject, 100u), (renamed.Decision.IdType, renamed.Decision.Id));
        Assert.Contains("was: \"void\"", renamed.Decision.Notes);
        Assert.Equal("grass", catalog.Find(IdType.OtbServer, 100)!.BestName);   // the same number with another meaning is another identifier
        Assert.Equal(recordsBefore, catalog.Observations.Where(o => o.SourceId != ItemCatalogStore.DecisionsSourceId));
        Assert.Equal(mappingsBefore, catalog.Mappings);
        Assert.Equal(before, Hashes());   // no source's records file was written

        // Search finds the identifier by its new name and by the names its sources give.
        Assert.Equal([100u], catalog.Query(new CatalogQuery { Text = "void stone" }).Select(e => e.Id));
        Assert.Contains(catalog.Query(new CatalogQuery { Text = "=void" }), e => e.Id == 100);

        // A name that cannot be used, or no change, is refused and nothing is written.
        int journal = File.ReadAllLines(Path.Combine(catalog.Folder, ItemCatalogStore.JournalFile)).Length;
        Assert.Contains("Type a name", Assert.Throws<ArgumentException>(() => ItemCatalogStore.Rename(catalog, renamed, " ")).Message);
        Assert.Contains("control characters", Assert.Throws<ArgumentException>(() => ItemCatalogStore.Rename(catalog, renamed, "void\nstone")).Message);
        Assert.Contains("already", Assert.Throws<ArgumentException>(() => ItemCatalogStore.Rename(catalog, renamed, "a void stone")).Message);
        Assert.Throws<InvalidOperationException>(() => ItemCatalogStore.RestoreOriginalName(catalog, catalog.Find(IdType.TibiaObject, 103)!));
        Assert.Equal(journal, File.ReadAllLines(Path.Combine(catalog.Folder, ItemCatalogStore.JournalFile)).Length);

        // It survives a restart; restoring brings back what the sources give, and both records stay in the history.
        var reopened = ItemCatalogStore.Load(catalog.Folder);
        Assert.Equal("a void stone", reopened.Find(IdType.TibiaObject, 100)!.BestName);
        ItemCatalogStore.RestoreOriginalName(reopened, reopened.Find(IdType.TibiaObject, 100)!);
        var restored = ItemCatalogStore.Load(catalog.Folder).Find(IdType.TibiaObject, 100)!;
        Assert.Equal((Agreement.VerifiedOfficial, "void"), (restored.Agreement, restored.BestName));
        Assert.Equal(2, restored.Observations.Count(o => o.Method == ObservationMethod.UserDecision));
        Assert.Equal(before, Hashes());

        // An identifier without a settled name (sources disagree) can be named too.
        ItemCatalogStore.Rename(reopened, reopened.Find(IdType.TibiaObject, 102)!, "dirt");
        var named = ItemCatalogStore.Load(catalog.Folder).Find(IdType.TibiaObject, 102)!;
        Assert.Equal((Agreement.Decided, "dirt"), (named.Agreement, named.BestName));
        Assert.Contains("it had no settled name", named.Decision!.Notes);
    }

    [Fact]
    public void RecordsAddedOneByOneGiveTheSameEntriesAsReadingTheCatalogAgain()
    {
        var catalog = SampleCatalog();
        var heldBefore = catalog.Find(IdType.TibiaObject, 101)!;
        int heldRecords = heldBefore.Observations.Count;
        var observed = Source("hover", SourceKind.ClientObservation, official: true);
        ItemCatalogStore.Append(catalog, observed, [Named("hover", 101, "earth", ObservationStatus.Verified) with { Method = ObservationMethod.ClientConfirmedByUser }]);
        ItemCatalogStore.Append(catalog, catalog.Source("hover")!, [Named("hover", 999, "brand new", ObservationStatus.Unverified), Named("hover", 50, "low id", ObservationStatus.Unverified)]);
        ItemCatalogStore.Rename(catalog, catalog.Find(IdType.TibiaObject, 105)!, "a shiny coin");
        ItemCatalogStore.Decide(catalog, catalog.Find(IdType.TibiaObject, 102)!, "sand");
        ItemCatalogStore.RestoreOriginalName(catalog, catalog.Find(IdType.TibiaObject, 102)!);

        var reread = ItemCatalogStore.Load(catalog.Folder);
        static object Shape(CatalogEntry e) => (e.Key, e.Agreement, e.BestName, e.BestNameReason, e.ClientName, e.HasDisagreement, e.UnnamedInClient,
            e.Observations.Count, e.Current.Count, e.NameGroups.Count, e.Category, string.Join(",", e.Flags));
        Assert.Equal(reread.Entries.Select(Shape), catalog.Entries.Select(Shape));   // same entries, in the same order
        Assert.Equal([50u, 100u, 101u, 102u, 103u, 104u, 105u, 999u], catalog.Entries.Select(e => e.Id));
        foreach (string text in new[] { "earth", "shiny", "brand", "Chosen over", "restored", "105" })
            Assert.Equal(reread.Query(new CatalogQuery { Text = text }).Select(e => e.Key), catalog.Query(new CatalogQuery { Text = text }).Select(e => e.Key));
        // An entry handed out earlier keeps the records it had.
        Assert.Equal(heldRecords, heldBefore.Observations.Count);
    }

    [Fact]
    public async Task ThePageRenamesANameInPlaceAndRestoresTheOriginal()
    {
        string dir = TestData.NewTempDirectory("catalogrename");
        var settings = new AppSettings { OutputFolder = Path.Combine(dir, "out"), LoadDataAutomatically = false };
        settings.Normalize();
        using var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var dialogs = new FakeDialogs();
        services.Dialogs = dialogs;
        SampleCatalog(Path.Combine(services.Outputs.FolderFor(OutputKind.ItemCatalogs), "Sample"));
        var vm = new ItemCatalogViewModel(services);
        await vm.OnNavigatedToAsync();
        for (int i = 0; i < 200 && (vm.Catalog == null || vm.IsBusy); i++) await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(6, vm.TotalCount);
        var list = vm.VisibleRows;
        CatalogEntryRow Row(uint id) => vm.VisibleRows.Single(r => r.Entry.Id == id && r.Entry.IdType == IdType.TibiaObject);

        // F2 on a row the client does not name: an invalid name is explained under the editor, which stays open.
        var row = Row(101);
        vm.SelectedRow = row;
        vm.BeginRenameCommand.Execute(null);
        Assert.True(row.IsEditing);
        Assert.Equal("earth", vm.EditText);
        vm.EditText = "   ";
        await vm.CommitRenameCommand.ExecuteAsync(null);
        Assert.Equal("Type a name.", vm.EditProblem);
        Assert.True(row.IsEditing);
        vm.EditText = "an   earth clod";
        await vm.CommitRenameCommand.ExecuteAsync(null);
        Assert.False(row.IsEditing);
        Assert.Null(vm.EditingRow);
        Assert.Same(list, vm.VisibleRows);   // the list was not rebuilt: selection and scroll position stay
        Assert.Same(row, vm.SelectedRow);
        Assert.Equal(("an earth clod", "chosen by you"), (row.Name, row.AgreementText));
        Assert.Contains("renamed to \"an earth clod\"", services.Status.Message);
        Assert.True(vm.CanWithdrawDecision);
        Assert.Contains(vm.Sources, s => s.Source.Id == ItemCatalogStore.DecisionsSourceId);
        Assert.Contains(vm.SourceFilters, f => f.Label == "With Your decisions");
        Assert.Empty(dialogs.ConfirmQuestions);   // the client gives 101 no name: nothing to confirm

        // Esc and clicking elsewhere drop what was typed; nothing is written.
        string journal = Path.Combine(vm.Catalog!.Folder, ItemCatalogStore.JournalFile);
        int lines = File.ReadAllLines(journal).Length;
        vm.BeginRenameCommand.Execute(row);
        vm.EditText = "something else";
        vm.CancelRenameCommand.Execute("key");
        Assert.False(row.IsEditing);
        vm.BeginRenameCommand.Execute(row);
        vm.EditText = "something else";
        vm.CancelRenameCommand.Execute("focus");
        Assert.Contains("Rename cancelled", services.Status.Message);
        Assert.Equal(lines, File.ReadAllLines(journal).Length);
        Assert.Equal("an earth clod", row.Name);

        // The client names 105 "gold coin": you are asked once; declining keeps the client's name.
        var gold = Row(105);
        vm.SelectedRow = gold;
        vm.BeginRenameCommand.Execute(null);
        vm.EditText = "a gold piece";
        dialogs.ConfirmAnswer = false;
        await vm.CommitRenameCommand.ExecuteAsync(null);
        Assert.Contains("The client calls object 105 \"gold coin\"", Assert.Single(dialogs.ConfirmQuestions));
        Assert.Equal("gold coin", gold.Name);
        Assert.False(gold.IsEditing);
        Assert.Equal(lines, File.ReadAllLines(journal).Length);
        dialogs.ConfirmAnswer = true;
        dialogs.PromptAnswers.Enqueue("a gold piece");   // "Rename…" in the details: the same rules and the same question
        await vm.DecideOtherNameCommand.ExecuteAsync(null);
        Assert.Equal(2, dialogs.ConfirmQuestions.Count);
        Assert.Equal(("a gold piece", "gold coin"), (gold.Name, gold.ClientName));
        dialogs.PromptAnswers.Enqueue("a gold piece");
        await vm.DecideOtherNameCommand.ExecuteAsync(null);
        Assert.Contains("No change", services.Status.Message);
        dialogs.PromptAnswers.Enqueue("two\nlines");
        await vm.DecideOtherNameCommand.ExecuteAsync(null);
        Assert.Contains("control characters", services.Status.Message);

        // The names are there after a restart; restoring gives the client's name back.
        var again = new ItemCatalogViewModel(services);
        await again.OnNavigatedToAsync();
        for (int i = 0; i < 200 && (again.Catalog == null || again.IsBusy); i++) await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal("an earth clod", again.VisibleRows.Single(r => r.Entry.Id == 101).Name);
        again.SelectedRow = again.VisibleRows.Single(r => r.Entry.Id == 105);
        Assert.Equal("a gold piece", again.SelectedRow.Name);
        await again.WithdrawDecisionCommand.ExecuteAsync(null);
        Assert.Equal(("gold coin", "official"), (again.SelectedRow.Name, again.SelectedRow.AgreementText));
        Assert.False(again.CanWithdrawDecision);
        Assert.Contains("Original name of 105 restored: \"gold coin\"", services.Status.Message);

        // Sorting and filters see the new names once they are applied again.
        again.SortOrder = again.SortOrders.Single(s => s.Value == "name");
        Assert.Equal(["an earth clod", "gold coin", "mystery thing", "void"], again.VisibleRows.Where(r => r.HasName).Select(r => r.Name));
        again.AgreementFilter = again.AgreementFilters.Single(f => f.Value == Agreement.Decided);
        Assert.Equal([101u], again.VisibleRows.Select(r => r.Entry.Id));
    }

    // ------------------------------------------------------------------ storage

    [Fact]
    public void ImportsArePreviewedAndReplacedRecordsAreBackedUp()
    {
        var catalog = SampleCatalog();
        var backups = new BackupStore(TestData.NewTempDirectory("backups"));
        var list = catalog.Source("list")!;
        var second = Import(list with { ImportedAtUtc = DateTime.UtcNow }, Named("list", 101, "earth"), Named("list", 102, "dirt"), Named("list", 106, "new thing"));
        var preview = catalog.Preview(second);
        Assert.Same(list, preview.Replaces);
        Assert.Equal((1, 1, 2, 1), (preview.Added, preview.Changed, preview.Removed, preview.Unchanged));   // 106 new, 102 renamed, 104 and 105 gone, 101 same
        Assert.Contains("\"sand\" → \"dirt\"", preview.ToText());
        Assert.Null(catalog.Find(IdType.TibiaObject, 106));   // nothing changes before the commit

        ItemCatalogStore.Commit(catalog, second, backups);
        Assert.Equal("new thing", catalog.Find(IdType.TibiaObject, 106)!.BestName);
        Assert.Null(catalog.Find(IdType.TibiaObject, 104));
        Assert.Equal(Agreement.Corroborated, catalog.Find(IdType.TibiaObject, 102)!.Agreement);   // "Dirt" (wiki) and "dirt" (list) now agree
        var set = Assert.Single(backups.List());
        Assert.Single(set.Manifest.Entries);

        // What was written is what is read back.
        var reloaded = ItemCatalogStore.Load(catalog.Folder);
        Assert.Empty(reloaded.LoadProblems);
        Assert.Equal(catalog.Observations.OrderBy(o => o.SourceId).ThenBy(o => o.Id), reloaded.Observations.OrderBy(o => o.SourceId).ThenBy(o => o.Id));
        Assert.Equal(catalog.Sources.Select(s => s.Id), reloaded.Sources.Select(s => s.Id));

        // Removing a source moves its records to the backups.
        ItemCatalogStore.RemoveSource(reloaded, "wiki", backups);
        Assert.Null(reloaded.Source("wiki"));
        Assert.False(File.Exists(ItemCatalogStore.SnapshotPath(reloaded.Folder, "wiki")));
        Assert.Equal(2, backups.List().Count);
        Assert.Null(ItemCatalogStore.Load(reloaded.Folder).Source("wiki"));
    }

    [Fact]
    public void TheObservationLogKeepsEveryCompleteRecordAfterAnInterruption()
    {
        var catalog = SampleCatalog();
        var collected = Source("hover", SourceKind.ClientObservation, official: true);
        ItemCatalogStore.Append(catalog, collected, [Named("hover", 101, "earth", ObservationStatus.Verified) with { Method = ObservationMethod.ClientConfirmedByUser }]);
        Assert.Equal(Agreement.VerifiedOfficial, catalog.Find(IdType.TibiaObject, 101)!.Agreement);

        // The program stops while writing a record: the half-written line is reported, everything else kept.
        string journal = Path.Combine(catalog.Folder, ItemCatalogStore.JournalFile);
        File.AppendAllText(journal, """{"sourceId":"hover","idType":"TibiaObject","id":102,"na""");
        var reopened = ItemCatalogStore.Load(catalog.Folder);
        Assert.Contains(reopened.LoadProblems, p => p.Contains("incomplete"));
        Assert.Equal(Agreement.VerifiedOfficial, reopened.Find(IdType.TibiaObject, 101)!.Agreement);
        Assert.Equal(Agreement.Conflict, reopened.Find(IdType.TibiaObject, 102)!.Agreement);

        // Collecting goes on: the next record starts on a new line and is read.
        ItemCatalogStore.Append(reopened, reopened.Source("hover")!, [Named("hover", 103, "stone floor", ObservationStatus.Verified)]);
        var again = ItemCatalogStore.Load(catalog.Folder);
        Assert.Equal("stone floor", again.Find(IdType.TibiaObject, 103)!.BestName);
        Assert.Contains(again.LoadProblems, p => p.Contains("damaged"));
        Assert.Equal(2, again.CountOf("hover"));
        // Imported sources are not appended one by one, and journal sources are not imported.
        Assert.Throws<ArgumentException>(() => ItemCatalogStore.Append(again, again.Source("list")!, [Named("list", 1, "x")]));
        Assert.Throws<InvalidOperationException>(() => ItemCatalogStore.Commit(again, Import(collected, Named("hover", 1, "x"))));
    }

    [Fact]
    public void ACatalogFromANewerVersionIsNotOpened()
    {
        var catalog = SampleCatalog();
        string file = Path.Combine(catalog.Folder, ItemCatalogStore.CatalogFile);
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"formatVersion\": 1", "\"formatVersion\": 99"));
        Assert.Contains("newer Tibia Foundry", Assert.Throws<InvalidDataException>(() => ItemCatalogStore.Load(catalog.Folder)).Message);
        Assert.Contains("newer", ItemCatalogStore.List(Path.GetDirectoryName(catalog.Folder)!).Single().Problem);
    }

    // ------------------------------------------------------------------ imports and exports

    [Fact]
    public void NameListsAreReadStrictlyAndProblemsNameTheirLine()
    {
        string text = "﻿# a comment\r\n[100] void\r\n[3457] a shovel\r\n[35523] an exotic amulet\r\n\r\n// another comment\r\n" +
                      "[3457] a second shovel\r\n100 no brackets\r\n[abc] letters\r\n[70000] too high\r\n[0] zero\r\n[200]\r\n[201] the holy Tible\r\n";
        var import = ItemImporters.FromNameList(text, Source("names", SourceKind.NameList), IdType.TibiaObject);
        Assert.Equal([100u, 3457u, 35523u, 201u], import.Observations.Select(o => o.Id));
        Assert.Equal(("a", "shovel"), (import.Observations[1].Article, import.Observations[1].Name));
        Assert.Equal(("an", "exotic amulet"), (import.Observations[2].Article, import.Observations[2].Name));
        Assert.Equal((null, "the holy Tible"), (import.Observations[3].Article, import.Observations[3].Name));   // "the" stays in the name
        Assert.All(import.Observations, o => Assert.Equal(ObservationStatus.Unverified, o.Status));
        Assert.Equal([7, 8, 9, 10, 11, 12], import.Problems.Select(p => p.Line));
        Assert.Contains("first on line 3", import.Problems[0].Message);
        Assert.Contains("outside", import.Problems[3].Message);
    }

    [Fact]
    public void ExportsSayWhatTheNumbersMeanAndReimportUnchanged()
    {
        var catalog = SampleCatalog();
        var entries = catalog.Entries.ToList();

        // Name list: a header without ']' (LapisItemEditor skips such lines), only settled names, articles kept.
        string names = ItemExports.ToNameList(catalog, entries, IdType.TibiaObject, "all [test] identifiers");
        var headerLines = names.Split("\r\n").Where(l => l.StartsWith('#')).ToList();
        Assert.All(headerLines, l => Assert.DoesNotContain("]", l));
        Assert.Contains(headerLines, l => l.Contains("Tibia object ID") && l.Contains("client 15.33.test"));
        Assert.Contains(headerLines, l => l.Contains("2 selected identifiers without a settled name are left out"));   // 102 conflict, 103 none
        Assert.Contains(headerLines, l => l.Contains("TibiaWiki") && l.Contains("CC BY-SA"));   // the catalog holds TibiaWiki titles
        var reread = ItemImporters.FromNameList(names, Source("again", SourceKind.NameList), IdType.TibiaObject);
        Assert.Empty(reread.Problems);
        Assert.Equal(entries.Where(e => e.BestName != null).Select(e => (e.Id, e.BestName)), reread.Observations.Select(o => (o.Id, o.FullName)));

        // CSV (one row per identifier) back into a source: the same names, articles, categories and flags.
        var single = ItemCatalogStore.Create(Path.Combine(TestData.NewTempDirectory("single"), "One"), "One");
        ItemCatalogStore.Commit(single, Import(Source("list", SourceKind.NameList),
            Named("list", 3031, "gold coin") with { Article = "a", Category = "Valuables", Flags = ["pickupable", "stackable"] },
            Named("list", 3035, "=formula, \"quoted\"") with { Plural = "formulas" },
            Named("list", 3043, null)));
        string csv = ItemExports.EntriesTable(single, single.Entries) is var table ? ReportWriter.ToCsv(table, ItemExports.Header(single, IdType.TibiaObject, "all")) : "";
        var fromCsv = ItemImporters.FromCsv(csv, Source("csv", SourceKind.Csv), IdType.TibiaObject);
        Assert.Empty(fromCsv.Problems);
        Assert.Equal(single.Observations.Select(o => (o.Id, o.Name, o.Article, o.Category, string.Join(";", o.Flags ?? []))),
            fromCsv.Observations.Select(o => (o.Id, o.Name, o.Article, o.Category, string.Join(";", o.Flags ?? []))));
        Assert.Contains("'=formula", csv);   // a spreadsheet would run it otherwise; the import removes the guard again
        Assert.DoesNotContain("CC BY-SA", csv);   // no TibiaWiki data in this catalog

        // JSON keeps every source and record (re-importable into another catalog).
        string json = ItemExports.ToJson(catalog, entries, "all");
        var imports = ItemImporters.FromCatalogJson(json, "test.json");
        Assert.Equal(catalog.Sources.Select(s => (s.Id, s.Kind, s.IsOfficial, s.Version)), imports.Select(i => (i.Source.Id, i.Source.Kind, i.Source.IsOfficial, i.Source.Version)));
        Assert.Equal(catalog.Observations, imports.SelectMany(i => i.Observations));
        Assert.Throws<InvalidDataException>(() => ItemImporters.FromCatalogJson("""{ "something": 1 }""", "other.json"));
    }

    [Fact]
    public void AHeaderlessListOfNamesAndIdsIsReadWithTheLayoutYouChoose()
    {
        // As the market tracker's items.csv: no header, the name first and the ID last; names may hold commas, quoted or not.
        string csv = "200 Theons,37712\r\nBaby Seal (Time Travel, Painted),37748\r\n\"Quoted, Name\",3031\r\n# a comment\r\nNo ID,\r\nAbacus,8156\r\nAbacus again,8156\r\n";
        Assert.False(ItemImporters.CsvHasHeader(csv));
        Assert.Equal(HeaderlessCsv.NameThenId, ItemImporters.GuessHeaderless(csv));
        var import = ItemImporters.FromCsv(csv, Source("market", SourceKind.Csv), IdType.TibiaObject, HeaderlessCsv.NameThenId);
        Assert.Equal([(37712u, "200 Theons"), (37748u, "Baby Seal (Time Travel, Painted)"), (3031u, "Quoted, Name"), (8156u, "Abacus")],
            import.Observations.Select(o => (o.Id, o.Name!)));
        Assert.All(import.Observations, o => Assert.Equal((ObservationStatus.Unverified, ObservationMethod.Import, (string?)null), (o.Status, o.Method, o.Article)));
        Assert.Equal([5, 7], import.Problems.Select(p => p.Line));   // the line without an ID, and the repeated ID (the first is kept)

        // The other way round: the ID first, the rest of the line is the name.
        string idFirst = "37712,200 Theons\r\n37748,Baby Seal (Time Travel, Painted)\r\n";
        Assert.Equal(HeaderlessCsv.IdThenName, ItemImporters.GuessHeaderless(idFirst));
        Assert.Equal(["200 Theons", "Baby Seal (Time Travel, Painted)"], ItemImporters.FromCsv(idFirst, Source("ids", SourceKind.Csv), IdType.TibiaObject, HeaderlessCsv.IdThenName).Observations.Select(o => o.Name));

        // A file with a header is read as before; text without IDs is not a list of names and IDs.
        Assert.True(ItemImporters.CsvHasHeader("# exported\r\nID,Name\r\n3031,gold coin\r\n"));
        Assert.Null(ItemImporters.GuessHeaderless("gold coin,coins\r\nrope,tools\r\n"));
    }

    [Fact]
    public void TibiaWikiTitlesAreMarkedAsTitlesAndNotVerified()
    {
        var table = new WikiItemIds(new Dictionary<uint, string> { [3031] = "Gold Coin", [25101] = "Abandoned Imbuing Shrine" }, ["ID 9 appears more than once"],
            WikiTableSource.Api, new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc));
        var import = ItemImporters.FromTibiaWiki(table);
        Assert.Equal(SourceKind.TibiaWiki, import.Source.Kind);
        Assert.False(import.Source.IsOfficial);
        Assert.Equal(table.FetchedAtUtc, import.Source.SourceTimestampUtc);
        Assert.All(import.Observations, o => Assert.True(o.NameIsTitle && o.Status == ObservationStatus.Unverified));
        Assert.Contains("CC BY-SA", import.Source.Notes);
        Assert.Single(import.Problems);
    }

    [Fact]
    public void TheClientsOwnNamesAreVerifiedAndEveryObjectIsListed()
    {
        TestData.RequireClientAssets();
        var db = SharedData.Appearances;
        var import = ItemImporters.FromClient(db, TestData.Client.ReadClientVersion(), db.SourcePath);
        Assert.True(import.Source.IsOfficial);
        Assert.Equal(db.Count(Core.Appearances.AppearanceKind.Object), import.Observations.Count);
        Assert.All(import.Observations, o => Assert.Equal(o.HasName ? ObservationStatus.Verified : ObservationStatus.Unresolved, o.Status));
        int named = import.Observations.Count(o => o.HasName);
        output.WriteLine($"client {import.Source.Version}: {import.Observations.Count} objects, {named} named, {import.Observations.Count - named} unnamed");
        // Only the pinned reference client has these exact figures; a live client changes them with every update
        // (9.0: without a reference folder the check ran against any installed client and failed after an update).
        if (TestData.ReferenceClient != null && TestData.ReferenceMismatch == null)
        {
            // The pinned client 15.33 (and 15.33.9d148f, which has the same data): measured in the 7.0 research.
            Assert.Equal(43_516, import.Observations.Count);
            Assert.Equal(8_978, named);
            Assert.Equal(7_252, import.Observations.Count(o => o.Flags?.Contains("pickupable") == true));
            Assert.All(import.Observations.Where(o => o.Flags?.Contains("pickupable") == true), o => Assert.True(o.HasName));
        }
        var gold = import.Observations.Single(o => o.Id == 3031);
        Assert.Equal("gold coin", gold.Name);
        Assert.Contains("stackable", gold.Flags!);
        Assert.Contains(import.Observations, o => o.Category != null);   // market categories come along
    }

    [Fact]
    public void LargeCatalogsLoadAndFilterQuickly()
    {
        var catalog = ItemCatalogStore.Create(Path.Combine(TestData.NewTempDirectory("large"), "Large"), "Large");
        const int perSource = 50_000;
        foreach (string id in new[] { "a", "b", "c" })
        {
            var records = Enumerable.Range(100, perSource).Select(i => new ItemObservation
            {
                SourceId = id, IdType = IdType.TibiaObject, Id = (uint)i, Name = i % 7 == 0 ? null : $"{id} item {i % 9_000}", Category = "Others",
                Status = ObservationStatus.Unverified, Flags = i % 3 == 0 ? ["pickupable"] : null,
            }).ToArray();
            ItemCatalogStore.Commit(catalog, Import(Source(id, id == "a" ? SourceKind.OfficialClient : SourceKind.NameList, official: id == "a"), records));
        }
        var watch = Stopwatch.StartNew();
        var loaded = ItemCatalogStore.Load(catalog.Folder);
        long load = watch.ElapsedMilliseconds;
        watch.Restart();
        int hits = loaded.Query(new CatalogQuery { Text = "item 123", OnlyDisagreements = true }).Count();
        long filter = watch.ElapsedMilliseconds;
        // A rename (7.2) builds only its own entry again, not the whole catalog.
        watch.Restart();
        ItemCatalogStore.Rename(loaded, loaded.Find(IdType.TibiaObject, 4_321)!, "renamed thing");
        long rename = watch.ElapsedMilliseconds;
        output.WriteLine($"{loaded.Observations.Count:N0} records, {loaded.Entries.Count:N0} identifiers: load {load} ms, filter {filter} ms ({hits} hits), rename {rename} ms");
        Assert.Equal(3 * perSource + 1, loaded.Observations.Count);
        Assert.Equal("renamed thing", loaded.Find(IdType.TibiaObject, 4_321)!.BestName);
        Assert.True(load + filter < 5_000, $"load {load} ms + filter {filter} ms");
        Assert.True(rename < 200, $"rename {rename} ms");
    }

    // ------------------------------------------------------------------ the page

    [Fact]
    public async Task ThePageCreatesACatalogImportsAListAndExportsIt()
    {
        string dir = TestData.NewTempDirectory("catalogpage");
        var settings = new AppSettings { OutputFolder = Path.Combine(dir, "out"), LoadDataAutomatically = false };
        settings.Normalize();
        using var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var dialogs = new FakeDialogs();
        services.Dialogs = dialogs;
        var vm = new ItemCatalogViewModel(services);
        await vm.OnNavigatedToAsync();
        Assert.False(vm.HasCatalog);

        dialogs.PromptAnswers.Enqueue("My catalog");
        await vm.NewCatalogCommand.ExecuteAsync(null);
        Assert.True(vm.HasCatalog);
        Assert.StartsWith(Path.Combine(dir, "out", "Item_Catalogs"), vm.Catalog!.Folder);

        string list = Path.Combine(dir, "names.txt");
        File.WriteAllText(list, "[3031] a gold coin\r\n[3035] a platinum coin\r\nbroken line\r\n", Encoding.UTF8);
        dialogs.NextFile = list;
        dialogs.ChooseAnswer = 0;        // Tibia object IDs
        dialogs.ConfirmAnswer = true;    // import after the preview
        await vm.ImportFileCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.TotalCount);
        Assert.Contains("line 3", vm.LastPreview);
        Assert.Contains("names", vm.Sources.Single().Name);

        vm.SearchText = "platinum";
        var row = Assert.Single(vm.VisibleRows);
        Assert.Equal("a platinum coin", row.Name);
        Assert.Equal("one source", row.AgreementText);
        vm.SelectedRow = row;
        Assert.Contains("unknown origin", vm.Details.Single().Trust);

        string export = Path.Combine(dir, "export.txt");
        dialogs.NextFile = export;
        await vm.ExportNameListCommand.ExecuteAsync(null);   // scope: the identifiers shown
        string text = File.ReadAllText(export);
        Assert.Contains("[3035] a platinum coin", text);
        Assert.DoesNotContain("[3031]", text);
        Assert.Contains("search \"platinum\"", text);

        // The catalog opens again next time.
        var again = new ItemCatalogViewModel(services);
        await again.OnNavigatedToAsync();
        Assert.Equal(vm.Catalog.Folder, again.SelectedCatalog?.Folder);
    }

    [Fact]
    public async Task ThePageAsksWhichValueIsTheIdInAListWithoutHeader()
    {
        string dir = TestData.NewTempDirectory("catalogheaderless");
        var settings = new AppSettings { OutputFolder = Path.Combine(dir, "out"), LoadDataAutomatically = false };
        settings.Normalize();
        using var services = new AppServices(new SettingsStore(Path.Combine(dir, "settings.json")), settings) { Paths = TestData.PathsIn(dir) };
        var dialogs = new FakeDialogs();
        services.Dialogs = dialogs;
        var vm = new ItemCatalogViewModel(services);
        await vm.OnNavigatedToAsync();
        dialogs.PromptAnswers.Enqueue("Lists");
        await vm.NewCatalogCommand.ExecuteAsync(null);

        string file = Path.Combine(dir, "items.csv");
        File.WriteAllText(file, "200 Theons,37712\r\nBaby Seal (Time Travel, Painted),37748\r\nAbacus,8156\r\n", Encoding.UTF8);
        dialogs.NextFile = file;
        dialogs.ChooseAnswer = -1;       // "What are the numbers?" cancelled: nothing is asked or read further
        await vm.ImportFileCommand.ExecuteAsync(null);
        Assert.Single(dialogs.ChoiceQuestions);
        Assert.Equal(0, vm.TotalCount);

        dialogs.ChooseAnswer = 0;        // Tibia object IDs; then the layout Tibia Foundry suggests first (name, then ID)
        dialogs.ConfirmAnswer = true;
        await vm.ImportFileCommand.ExecuteAsync(null);
        string question = dialogs.ChoiceQuestions[^1];
        Assert.Contains("no header", question);
        Assert.Contains("Baby Seal (Time Travel, Painted),37748", question);   // its first lines are shown
        Assert.Equal(3, vm.TotalCount);
        vm.SearchText = "seal";
        Assert.Equal(("Baby Seal (Time Travel, Painted)", "37748"), (Assert.Single(vm.VisibleRows).Name, Assert.Single(vm.VisibleRows).Id));
    }
}
