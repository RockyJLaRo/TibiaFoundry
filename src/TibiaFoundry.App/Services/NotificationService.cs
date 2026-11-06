using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TibiaFoundry.Core.Settings;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

/// <summary>
/// Tells the user that a long operation finished (Settings → Notifications): flashes the taskbar
/// button when the window is in the background and plays a sound, only for operations that took
/// at least the configured time, so short actions never cause noise.
/// </summary>
public sealed class NotificationService(AppSettings settings)
{
    /// <summary>What happened at the last notice (for tests and diagnostics).</summary>
    public string? LastNotice { get; private set; }

    public void OperationFinished(string title, TimeSpan duration, bool success)
    {
        var n = settings.Notifications;
        if (duration.TotalSeconds < n.NoticeAfterSeconds) return;
        var notices = new List<string>();
        var window = Application.Current?.MainWindow;
        if (n.FlashTaskbarWhenDone && window != null && !window.IsActive && Flash(window)) notices.Add(L.T("Notification.TaskbarFlashed"));
        if (n.PlaySoundWhenDone)
        {
            (success ? SystemSounds.Asterisk : SystemSounds.Hand).Play();
            notices.Add(L.T("Notification.SoundPlayed"));
        }
        LastNotice = L.F("Notification.01After2N0S", title, (success ? "finished" : "failed"), duration.TotalSeconds) + (notices.Count > 0 ? " (" + string.Join(", ", notices) + ")" : "");
    }

    private static bool Flash(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
            uCount = uint.MaxValue,
            dwTimeout = 0,
        };
        return FlashWindowEx(ref info);
    }

    private const uint FLASHW_TRAY = 0x2;
    private const uint FLASHW_TIMERNOFG = 0xC;

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);
}
