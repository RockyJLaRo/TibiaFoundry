using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;

namespace TibiaFoundry.Tests;

/// <summary>9.0 data-integrity fixes (audit findings F-05 and F-18 of the 8.x technical audit).</summary>
public class DataIntegrityTests
{
    [Fact]
    public void MovingAFileIntoABackupKeepsItsContentAndRemovesTheOriginal()
    {
        string dir = TestData.NewTempDirectory("move");
        string file = Path.Combine(dir, "records.json.gz");
        File.WriteAllBytes(file, [1, 2, 3, 4]);
        var store = new BackupStore(Path.Combine(dir, "_backups"));
        string stored;
        using (var set = store.Begin("test", BackupKind.Automatic, dir)) stored = set.Add(file, move: true, ct: TestContext.Current.CancellationToken);
        Assert.False(File.Exists(file));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(stored));
    }

    [Fact]
    public void MovingAcrossVolumesCopiesVerifiesAndOnlyThenDeletesTheOriginal()
    {
        // Needs the test output (source drive) and %TEMP% on different volumes, as on the development machine.
        string sourceDir = Path.Combine(AppContext.BaseDirectory, "cross-volume-" + Guid.NewGuid().ToString("N")[..8]);
        string backupDir = TestData.NewTempDirectory("cross-volume");
        Assert.SkipWhen(string.Equals(Path.GetPathRoot(sourceDir), Path.GetPathRoot(backupDir), StringComparison.OrdinalIgnoreCase),
            "The source and %TEMP% are on the same volume.");
        Directory.CreateDirectory(sourceDir);
        try
        {
            string file = Path.Combine(sourceDir, "a.bin");
            byte[] data = new byte[300_000];
            Random.Shared.NextBytes(data);
            File.WriteAllBytes(file, data);
            var store = new BackupStore(backupDir);
            string stored;
            using (var set = store.Begin("test", BackupKind.Automatic)) stored = set.Add(file, move: true, ct: TestContext.Current.CancellationToken);
            Assert.False(File.Exists(file));
            Assert.Equal(data, File.ReadAllBytes(stored));
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    [Fact]
    public void DurableAndBulkAtomicWritesGiveTheSameFilesAndLeaveNoTemporaryFiles()
    {
        string dir = TestData.NewTempDirectory("atomic");
        string a = Path.Combine(dir, "settings.json"), b = Path.Combine(dir, "thumb.png");
        File.WriteAllText(a, "old");
        AtomicFile.WriteAllText(a, "{ \"Language\": \"de\" } ✓");
        AtomicFile.WriteAllBytes(b, [9, 8, 7], durable: false);
        AtomicFile.WriteAllBytes(b, [7, 8, 9]);
        Assert.Equal("{ \"Language\": \"de\" } ✓", File.ReadAllText(a));
        Assert.Equal(new byte[] { 7, 8, 9 }, File.ReadAllBytes(b));
        Assert.NotEqual(0xEF, File.ReadAllBytes(a)[0]);   // no UTF-8 BOM
        Assert.Equal(2, Directory.GetFiles(dir).Length);
    }

    [Fact]
    public void AFailedWriteLeavesTheOldFile()
    {
        string dir = TestData.NewTempDirectory("atomic-fail");
        string file = Path.Combine(dir, "catalog.json");
        File.WriteAllText(file, "old");
        Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(file, temp =>
        {
            File.WriteAllText(temp, "half");
            throw new InvalidOperationException("interrupted");
        }));
        Assert.Equal("old", File.ReadAllText(file));
        Assert.Single(Directory.GetFiles(dir));
    }
}
