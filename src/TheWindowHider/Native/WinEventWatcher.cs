using static TheWindowHider.Native.NativeMethods;

namespace TheWindowHider.Native;

/// <summary>
/// Event-driven replacement for polling. Uses SetWinEventHook to learn the instant a
/// top-level window is created, shown, renamed, or brought to the foreground, so hide
/// rules can be reapplied immediately instead of on a timer.
///
/// Must be constructed on a thread that pumps messages (the WPF UI thread). The callback
/// therefore also fires on that thread.
/// </summary>
internal sealed class WinEventWatcher : IDisposable
{
    // Raised with the handle of a top-level window whose state just changed.
    public event Action<IntPtr>? WindowEvent;

    private readonly WinEventDelegate _callback; // kept alive for the lifetime of the hooks
    private readonly List<IntPtr> _hooks = new();
    private bool _disposed;

    public WinEventWatcher()
    {
        _callback = OnWinEvent;

        // Foreground changes.
        Install(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
        // Object create / destroy / show (0x8000..0x8002).
        Install(EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW);
        // Title changes (so title-based rules react when a window renames itself).
        Install(EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE);
    }

    private void Install(uint min, uint max)
    {
        IntPtr h = SetWinEventHook(min, max, IntPtr.Zero, _callback, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (h != IntPtr.Zero)
            _hooks.Add(h);
    }

    private void OnWinEvent(IntPtr hHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        // Only care about the window object itself, not child controls.
        if (hwnd == IntPtr.Zero) return;
        if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF) return;

        // Normalize to the root owner so events on child frames still map to the app window.
        IntPtr root = GetAncestor(hwnd, GA_ROOT);
        if (root == IntPtr.Zero) root = hwnd;

        WindowEvent?.Invoke(root);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (IntPtr h in _hooks)
            UnhookWinEvent(h);
        _hooks.Clear();
    }
}
