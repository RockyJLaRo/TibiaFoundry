using System.Diagnostics;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

/// <summary>One measurement of the Tibia Foundry process (all values measured by Windows and .NET).</summary>
/// <param name="CpuPercent">Share of all processor cores used since the previous sample; null for the first sample.</param>
public sealed record ResourceSample(DateTime Time, double? CpuPercent, long WorkingSet, long PrivateBytes, long ManagedHeap,
    int Gen0, int Gen1, int Gen2, int Threads, int Handles, TimeSpan Uptime);

/// <summary>Free space of a drive that holds output folders.</summary>
public sealed record DiskSpace(string Drive, string Folders, long? FreeBytes, long? TotalBytes, string? Problem)
{
    public string Text => Problem ?? L.F("ResourceMonitor.0FreeOf1", ReportWriter.FormatSize(FreeBytes ?? 0), ReportWriter.FormatSize(TotalBytes ?? 0));
}

/// <summary>
/// Measures the process's processor and memory use. It does nothing on its own: the Diagnostics
/// page calls <see cref="Sample"/> on a timer only while it is open and monitoring is on
/// (Settings → Diagnostics), so there is no background polling. Per-operation processor or memory
/// use is not measured, because operations share one process and cannot be told apart reliably.
/// </summary>
public sealed class ResourceMonitor
{
    private TimeSpan? _lastCpu;
    private DateTime _lastAt;

    public ResourceSample Sample()
    {
        using var p = Process.GetCurrentProcess();
        var now = DateTime.UtcNow;
        var cpu = p.TotalProcessorTime;
        double? percent = null;
        if (_lastCpu is { } last && now > _lastAt)
            percent = Math.Clamp((cpu - last).TotalMilliseconds / (now - _lastAt).TotalMilliseconds / Environment.ProcessorCount * 100, 0, 100);
        _lastCpu = cpu;
        _lastAt = now;
        return new ResourceSample(DateTime.Now, percent, p.WorkingSet64, p.PrivateMemorySize64, GC.GetTotalMemory(false),
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), p.Threads.Count, p.HandleCount, now - p.StartTime.ToUniversalTime());
    }

    /// <summary>Free space of the drives that hold the given folders (one entry per drive).</summary>
    public static IReadOnlyList<DiskSpace> Disks(IEnumerable<(string Label, string Path)> folders)
    {
        var result = new List<DiskSpace>();
        foreach (var group in folders.Where(f => !string.IsNullOrWhiteSpace(f.Path))
                     .GroupBy(f => SafeRoot(f.Path), StringComparer.OrdinalIgnoreCase))
        {
            string labels = string.Join(", ", group.Select(g => g.Label).Distinct());
            if (group.Key.Length == 0)
            {
                result.Add(new DiskSpace("?", labels, null, null, L.T("ResourceMonitor.NotAValidPath")));
                continue;
            }
            try
            {
                var drive = new DriveInfo(group.Key);
                result.Add(drive.IsReady
                    ? new DiskSpace(group.Key, labels, drive.AvailableFreeSpace, drive.TotalSize, null)
                    : new DiskSpace(group.Key, labels, null, null, L.T("ResourceMonitor.DriveNotAvailableNotConnectedOr")));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                result.Add(new DiskSpace(group.Key, labels, null, null, L.T("ResourceMonitor.FreeSpaceNotAvailable") + ex.Message));
            }
        }
        return result;
    }

    private static string SafeRoot(string path)
    {
        try { return Path.GetPathRoot(Path.GetFullPath(path)) ?? ""; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return ""; }
    }
}
