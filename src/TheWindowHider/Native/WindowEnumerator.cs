using System.IO;
using System.Text;
using TheWindowHider.Core;
using static TheWindowHider.Native.NativeMethods;

namespace TheWindowHider.Native;

/// <summary>
/// Enumerates real, user-facing top-level windows and resolves each one's owning process.
/// Filters out cloaked windows, tool windows and untitled shells so the list matches what
/// a person would actually think of as "a window".
/// </summary>
internal static class WindowEnumerator
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    public static List<WindowInfo> GetVisibleWindows()
    {
        var result = new List<WindowInfo>();
        IntPtr shell = GetShellWindow();

        EnumWindows((hWnd, _) =>
        {
            if (hWnd == shell) return true;
            if (!IsWindowVisible(hWnd)) return true;

            // Skip DWM-cloaked windows (e.g. suspended UWP apps on other virtual desktops).
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            // Skip tool windows such as floating palettes and tray helpers, which aren't real app windows.
            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

            int length = GetWindowTextLength(hWnd);
            if (length == 0) return true;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            string title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            GetWindowThreadProcessId(hWnd, out int pid);
            var (name, path) = ResolveProcess(pid);

            GetWindowDisplayAffinity(hWnd, out int affinity);

            result.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = title,
                ProcessId = pid,
                ProcessName = name,
                ExecutablePath = path,
                IsHiddenFromCapture = affinity != WDA_NONE
            });
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static (string name, string path) ResolveProcess(int pid)
    {
        if (pid <= 0) return ("", "");

        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero)
            return ("", "");

        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            if (QueryFullProcessImageName(h, 0, sb, ref size))
            {
                string full = sb.ToString();
                string file = Path.GetFileName(full);
                return (file.ToLowerInvariant(), full);
            }
        }
        catch
        {
            // fall through
        }
        finally
        {
            CloseHandle(h);
        }
        return ("", "");
    }

    /// <summary>Reads the current display affinity for a single window (cheap, cross-process safe).</summary>
    public static bool IsHidden(IntPtr hWnd)
    {
        GetWindowDisplayAffinity(hWnd, out int affinity);
        return affinity != WDA_NONE;
    }
}
