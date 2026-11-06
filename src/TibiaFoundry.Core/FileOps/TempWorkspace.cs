using System.Collections.Concurrent;
using TibiaFoundry.Core.Diagnostics;

namespace TibiaFoundry.Core.FileOps;

/// <summary>
/// Temporary working folders for operations. Each operation gets its own folder (a lease); the
/// folder is deleted when the lease is disposed. Clean-up of left-overs (after a crash or a forced
/// exit) never touches the folder of an operation that is still running.
/// </summary>
public sealed class TempWorkspace
{
    private readonly ConcurrentDictionary<string, byte> _active = new(StringComparer.OrdinalIgnoreCase);

    public TempWorkspace(string root)
    {
        Root = Path.GetFullPath(root);
    }

    /// <summary>Folder that holds all temporary folders of Tibia Foundry.</summary>
    public string Root { get; }

    /// <summary>Folders of operations that are running now.</summary>
    public IReadOnlyCollection<string> ActiveFolders => _active.Keys.ToList();

    /// <summary>Creates a new, empty folder for one operation.</summary>
    public TempLease Begin(string operation)
    {
        string slug = new(operation.Where(char.IsLetterOrDigit).Take(24).ToArray());
        string folder = Path.Combine(Root, $"{(slug.Length == 0 ? "op" : slug)}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        _active[folder] = 0;
        return new TempLease(this, folder);
    }

    internal void Release(string folder, bool delete)
    {
        _active.TryRemove(folder, out _);
        if (delete) TryDeleteDirectory(folder);
    }

    /// <summary>Total size of the temporary folder, in bytes.</summary>
    public long Size()
    {
        if (!Directory.Exists(Root)) return 0;
        long total = 0;
        foreach (var file in new DirectoryInfo(Root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            total += file.Length;
        return total;
    }

    /// <summary>
    /// Deletes left-over folders older than <paramref name="minimumAge"/>, skipping those of running
    /// operations. Returns the number of folders removed.
    /// </summary>
    public int CleanUp(TimeSpan minimumAge)
    {
        if (!Directory.Exists(Root)) return 0;
        int removed = 0;
        foreach (var dir in new DirectoryInfo(Root).EnumerateDirectories())
        {
            if (_active.ContainsKey(dir.FullName)) continue;
            if (DateTime.UtcNow - dir.LastWriteTimeUtc < minimumAge) continue;
            if (TryDeleteDirectory(dir.FullName)) removed++;
        }
        return removed;
    }

    private static bool TryDeleteDirectory(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return true;
            // Never follow links out of the temporary folder.
            var info = new DirectoryInfo(folder);
            if (PathGuard.IsReparsePoint(info))
            {
                info.Delete();
                return true;
            }
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Temporary folder {folder} could not be deleted: {ex.Message}");
            return false;
        }
    }
}

/// <summary>A temporary folder owned by one running operation.</summary>
public sealed class TempLease : IDisposable
{
    private readonly TempWorkspace _owner;
    private bool _disposed;

    internal TempLease(TempWorkspace owner, string folder)
    {
        _owner = owner;
        Folder = folder;
    }

    public string Folder { get; }

    /// <summary>When true the folder is kept after the operation (for diagnosing a failure).</summary>
    public bool Keep { get; set; }

    public string PathFor(string name) => Path.Combine(Folder, name);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.Release(Folder, delete: !Keep);
    }
}
