namespace TheWindowHider.Core;

/// <summary>A snapshot of one visible top-level window.</summary>
public sealed class WindowInfo
{
    public IntPtr Handle { get; init; }
    public string Title { get; set; } = "";
    public int ProcessId { get; init; }

    /// <summary>Executable file name including extension, e.g. "chrome.exe" (lower-cased).</summary>
    public string ProcessName { get; init; } = "";

    /// <summary>Full path to the executable, when it could be resolved.</summary>
    public string ExecutablePath { get; init; } = "";

    /// <summary>True when the OS currently reports a non-zero display affinity on this window.</summary>
    public bool IsHiddenFromCapture { get; set; }

    /// <summary>Convenience: process name without the ".exe" suffix, for display.</summary>
    public string ProcessDisplayName =>
        ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? ProcessName[..^4]
            : ProcessName;
}
