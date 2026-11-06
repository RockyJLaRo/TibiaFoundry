using TibiaFoundry.Core.Localization;
namespace TibiaFoundry.Core.Common;

/// <summary>Progress report for long-running operations.</summary>
/// <param name="Done">Units completed.</param>
/// <param name="Total">Total units (0 when unknown).</param>
/// <param name="Message">Short description of the current step.</param>
public readonly record struct OperationProgress(int Done, int Total, string Message)
{
    public double Fraction => Total <= 0 ? 0 : Math.Clamp(Done / (double)Total, 0, 1);
    public int Percent => (int)Math.Round(Fraction * 100);
}

/// <summary>Outcome of a batch operation that can partially succeed.</summary>
public sealed class BatchResult
{
    public int Succeeded { get; set; }
    public int Skipped { get; set; }
    public List<string> Failures { get; } = new();
    /// <summary>Things the user should know even when nothing failed (e.g. a changed behavior).</summary>
    public List<string> Notes { get; } = new();
    public bool HasFailures => Failures.Count > 0;

    public string Summary(string noun) =>
        L.F("OperationProgress.0N01Written", Succeeded, noun) + (Skipped > 0 ? L.F("OperationProgress.0N0Skipped", Skipped) : "") +
        (HasFailures ? L.F("OperationProgress.0N0Failed", Failures.Count) : "") + ".";
}

/// <summary>Helpers for building safe file names.</summary>
public static class FileNames
{
    /// <summary>Longest name produced (leaves room for an extension and a " 99" suffix within 255).</summary>
    public const int MaxLength = 200;

    /// <summary>A usable file name: characters Windows does not allow become '_', and a device name (CON, NUL…) gets a '_' in front.</summary>
    public static string Sanitize(string name)
    {
        var chars = name.Select(c => FileOps.WindowsNames.IsInvalidChar(c) ? '_' : c).ToArray();
        string result = new string(chars).Trim().TrimEnd('.');
        if (result.Length > MaxLength) result = result[..MaxLength].TrimEnd(' ', '.');
        if (result.Length == 0) return "_";
        return FileOps.WindowsNames.IsDeviceName(result) ? "_" + result : result;
    }

    /// <summary>
    /// Returns "<paramref name="baseName"/>.ext", or "baseName 2.ext", "baseName 3.ext"... if taken
    /// (the original's GIF naming scheme).
    /// </summary>
    public static string Unique(string directory, string baseName, string extension)
    {
        string candidate = Path.Combine(directory, baseName + extension);
        if (!File.Exists(candidate)) return candidate;
        for (int n = 2; ; n++)
        {
            candidate = Path.Combine(directory, $"{baseName} {n}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
