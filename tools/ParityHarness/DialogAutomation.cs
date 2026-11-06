using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ParityHarness;

/// <summary>
/// Background watchdog that services modal dialogs raised by the original application:
/// message boxes are logged and accepted; Open/Save file dialogs receive the next queued
/// file name (or are cancelled when none is queued).
/// </summary>
internal static class DialogAutomation
{
    public static readonly ConcurrentQueue<string> FileNames = new ConcurrentQueue<string>();
    public static readonly ConcurrentQueue<string> Log = new ConcurrentQueue<string>();
    private static readonly HashSet<IntPtr> Handled = new HashSet<IntPtr>();

    public static void Start()
    {
        var t = new Thread(Loop) { IsBackground = true, Name = "DialogAutomation" };
        t.Start();
    }

    /// <summary>Drains and returns the messages logged since the last call.</summary>
    public static List<string> Drain()
    {
        var list = new List<string>();
        while (Log.TryDequeue(out var s)) list.Add(s);
        return list;
    }

    private static void Loop()
    {
        uint pid = (uint)Process.GetCurrentProcess().Id;
        while (true)
        {
            Thread.Sleep(120);
            var dialogs = new List<IntPtr>();
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out uint wpid);
                if (wpid == pid && IsWindowVisible(h) && ClassOf(h) == "#32770") dialogs.Add(h);
                return true;
            }, IntPtr.Zero);
            foreach (var h in dialogs)
            {
                lock (Handled)
                {
                    if (Handled.Contains(h) && IsWindow(h)) { /* retry below if still open */ }
                }
                try { Service(h); } catch (Exception ex) { Log.Enqueue("AUTOMATION ERROR " + ex.Message); }
            }
        }
    }

    private static void Service(IntPtr dlg)
    {
        Thread.Sleep(150); // let the dialog finish initialising
        if (!IsWindow(dlg)) return;
        string title = TextOf(dlg);
        IntPtr edit = FindFileNameEdit(dlg);
        if (edit != IntPtr.Zero)
        {
            if (FileNames.TryDequeue(out var fileName))
            {
                SendMessage(edit, WM_SETTEXT, IntPtr.Zero, fileName);
                Thread.Sleep(80);
                SendMessage(dlg, WM_COMMAND, (IntPtr)IDOK, IntPtr.Zero);
                Log.Enqueue($"FILEDIALOG '{title}' -> {fileName}");
            }
            else
            {
                SendMessage(dlg, WM_COMMAND, (IntPtr)IDCANCEL, IntPtr.Zero);
                Log.Enqueue($"FILEDIALOG '{title}' cancelled (no queued file name)");
            }
            Thread.Sleep(300);
            return;
        }

        string message = TextOf(GetDlgItem(dlg, 0xFFFF));
        Log.Enqueue($"MSGBOX '{title}': {message.Replace("\r", " ").Replace("\n", " ")}");
        int button = GetDlgItem(dlg, IDYES) != IntPtr.Zero ? IDYES
                   : GetDlgItem(dlg, IDOK) != IntPtr.Zero ? IDOK
                   : IDCANCEL;
        SendMessage(dlg, WM_COMMAND, (IntPtr)button, IntPtr.Zero);
        Thread.Sleep(200);
    }

    private static IntPtr FindFileNameEdit(IntPtr dlg)
    {
        IntPtr found = IntPtr.Zero;
        IntPtr fallback = IntPtr.Zero;
        bool isFileDialog = false;
        EnumChildWindows(dlg, (h, l) =>
        {
            string cls = ClassOf(h);
            if (cls == "DirectUIHWND" || cls == "SHELLDLL_DefView" || cls == "ComboBoxEx32") isFileDialog = true;
            if (cls == "Edit")
            {
                IntPtr parent = GetParent(h);
                int parentId = GetDlgCtrlID(parent);
                int id = GetDlgCtrlID(h);
                if (parentId == 1148 || id == 1152 || id == 1148) { found = h; return false; }
                if (fallback == IntPtr.Zero) fallback = h;
            }
            return true;
        }, IntPtr.Zero);
        if (!isFileDialog) return IntPtr.Zero;
        return found != IntPtr.Zero ? found : fallback;
    }

    private static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string TextOf(IntPtr h)
    {
        if (h == IntPtr.Zero) return "";
        int len = (int)SendMessage(h, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        var sb = new StringBuilder(len + 2);
        SendMessage(h, WM_GETTEXT, (IntPtr)sb.Capacity, sb);
        return sb.ToString();
    }

    private const int WM_SETTEXT = 0x000C, WM_GETTEXT = 0x000D, WM_GETTEXTLENGTH = 0x000E, WM_COMMAND = 0x0111;
    private const int IDOK = 1, IDCANCEL = 2, IDYES = 6;

    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr hDlg, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, string l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, StringBuilder l);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
}
