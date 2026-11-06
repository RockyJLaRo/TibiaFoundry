namespace TibiaFoundry.Core.FileOps;

/// <summary>
/// The rules Windows applies to file and folder names, in one place: the characters it does not
/// allow, the device names (CON, NUL, COM1…, also with an extension such as "nul.txt") and the dot or
/// space it removes from the end of a name. Used wherever a name is made or checked: file names
/// (<see cref="Common.FileNames"/>), output folders (<see cref="OutputLocations"/>), paths from archives,
/// resource files and manifests (<see cref="PathGuard"/>), and item catalog folders.
/// </summary>
public static class WindowsNames
{
    private static readonly HashSet<char> InvalidChars = new(Path.GetInvalidFileNameChars());

    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "CONIN$", "CONOUT$",
    };

    /// <summary>A character Windows does not allow in a file or folder name (among them \ / : * ? " &lt; &gt; | and control characters).</summary>
    public static bool IsInvalidChar(char c) => InvalidChars.Contains(c);

    public static bool HasInvalidChars(string name) => name.Any(IsInvalidChar);

    /// <summary>A device name, alone or with an extension ("CON", "nul.txt"): Windows opens the device instead of a file.</summary>
    public static bool IsDeviceName(string name) => DeviceNames.Contains(name.Split('.')[0]);

    /// <summary>Windows removes a dot or space at the end of a name, so such a name never stays as written.</summary>
    public static bool EndsWithDotOrSpace(string name) => name.EndsWith('.') || name.EndsWith(' ');
}
