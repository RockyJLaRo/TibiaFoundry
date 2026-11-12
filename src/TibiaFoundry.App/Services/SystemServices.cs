using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using TibiaFoundry.App.Infrastructure;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Imaging;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

/// <summary>Clipboard access with retries (another process may hold the clipboard briefly).</summary>
public sealed class ClipboardService
{
    public bool SetText(string text) => Retry(() => Clipboard.SetDataObject(text, true));

    /// <summary>Copies an image keeping transparency for apps that read PNG, with a bitmap fallback for others.</summary>
    public bool SetImage(RgbaImage image) => Retry(() =>
    {
        var data = new DataObject();
        data.SetData("PNG", new MemoryStream(ImageIO.EncodePng(image)));
        data.SetImage(Images.ToBitmapSource(image));
        Clipboard.SetDataObject(data, true);
    });

    private static bool Retry(Action action)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                action();
                return true;
            }
            catch (COMException ex) when (attempt < 5)
            {
                AppLog.Verbose($"Clipboard busy ({ex.HResult:X}), retrying.");
                Thread.Sleep(60);
            }
            catch (COMException ex)
            {
                AppLog.Warn("Clipboard unavailable.", ex);
            }
        }
        return false;
    }
}

/// <summary>Outcome of asking Windows to show a file or folder.</summary>
/// <param name="Opened">True when File Explorer was asked successfully.</param>
/// <param name="Selected">True when Explorer was asked to select the file (not only open its folder).</param>
/// <param name="Problem">Why it did not work, in plain words.</param>
public sealed record RevealResult(bool Opened, bool Selected, string? Problem)
{
    public static RevealResult Failed(string problem) => new(false, false, problem);
}

/// <summary>The Windows shell operations the program uses (replaceable in tests).</summary>
public interface IShellIntegration
{
    /// <summary>Opens File Explorer at the file's folder with the file selected.</summary>
    void OpenFolderAndSelect(string file);

    /// <summary>Opens a folder in File Explorer.</summary>
    void OpenFolder(string folder);

    /// <summary>Opens a web page in the default browser.</summary>
    void OpenUrl(string url);
}

/// <summary>
/// Shell integration through Windows APIs: SHOpenFolderAndSelectItems for "show this file", and
/// ShellExecute (Process.Start with UseShellExecute) for folders and web pages. Paths are passed
/// as single values — never assembled into a command line — so spaces, quotes and Unicode
/// characters in names are safe.
/// </summary>
public sealed class WindowsShell : IShellIntegration
{
    public void OpenFolderAndSelect(string file)
    {
        IntPtr pidl = ILCreateFromPathW(file);
        if (pidl == IntPtr.Zero) throw new IOException($"Windows could not locate {file}.");
        try
        {
            int hr = SHOpenFolderAndSelectItems(pidl, 0, null, 0);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            ILFree(pidl);
        }
    }

    public void OpenFolder(string folder) => Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();

    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ILCreateFromPathW(string path);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr[]? items, uint flags);
}

/// <summary>Opens URLs, folders and saved files with the shell (the original used Process.Start(url), which fails on .NET).</summary>
public sealed class ShellService(IShellIntegration? shell = null)
{
    public IShellIntegration Shell { get; set; } = shell ?? new WindowsShell();

    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return;
        try
        {
            Shell.OpenUrl(uri.AbsoluteUri);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or COMException)
        {
            AppLog.Warn($"Could not open {uri.AbsoluteUri}: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens a media file with the program Windows associates with it (8.8: Asset Library). Only
    /// images, sounds and plain data files are opened; programs, scripts and anything else never are.
    /// </summary>
    public bool OpenFile(string path)
    {
        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (!File.Exists(path) || !Core.Library.MediaTypes.IsIndexed(ext, includeData: true) || ext is "lua" or "xml") return false;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn($"Could not open {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Opens a folder, or a file's folder with the file selected (problems are only logged).</summary>
    public void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var result = File.Exists(path) ? Reveal(path) : Directory.Exists(path) ? OpenDirectory(path) : RevealResult.Failed(L.F("SystemServices.0DoesNotExist", path));
        if (!result.Opened) AppLog.Warn($"Could not open {path}: {result.Problem}");
    }

    /// <summary>
    /// Shows a saved file in File Explorer: its folder opens with the file selected. When selecting
    /// is not possible the folder is opened instead; when the file no longer exists, its folder
    /// (if it still exists) is opened. Never throws; the result says what happened.
    /// </summary>
    public RevealResult Reveal(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return RevealResult.Failed(L.T("SystemServices.NoFileWasNamed"));
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return RevealResult.Failed(L.F("SystemServices.0IsNotAValidPath", path)); }

        if (Directory.Exists(full)) return OpenDirectory(full);
        string? folder = Path.GetDirectoryName(full);
        if (File.Exists(full))
        {
            try
            {
                Shell.OpenFolderAndSelect(full);
                return new RevealResult(true, true, null);
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
            {
                AppLog.Warn($"Explorer could not select {full}; opening its folder instead: {ex.Message}", source: "Shell");
            }
            return folder != null ? OpenDirectory(folder) : RevealResult.Failed(full + L.T("SystemServices.HasNoFolder"));
        }
        if (folder != null && Directory.Exists(folder))
        {
            var opened = OpenDirectory(folder);
            return opened.Opened ? opened with { Problem = L.F("SystemServices.0NoLongerExistsItsFolder", Path.GetFileName(full)) } : opened;
        }
        return RevealResult.Failed(L.F("SystemServices.0AndItsFolderNoLonger", full));
    }

    private RevealResult OpenDirectory(string folder)
    {
        try
        {
            Shell.OpenFolder(folder);
            return new RevealResult(true, false, null);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or COMException or IOException or UnauthorizedAccessException)
        {
            return RevealResult.Failed(L.F("SystemServices.FileExplorerCouldNotOpen0", folder, ex.Message));
        }
    }
}
