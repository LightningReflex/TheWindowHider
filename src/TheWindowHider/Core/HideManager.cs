using TheWindowHider.Native;

namespace TheWindowHider.Core;

/// <summary>
/// Orchestrates everything: listens for window events, evaluates rules, and applies /
/// removes capture-hiding. Scans run on a dedicated background thread (injection can block
/// briefly) and are coalesced so a burst of events causes at most one scan.
/// </summary>
public sealed class HideManager : IDisposable
{
    private readonly object _gate = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _worker;
    private readonly System.Threading.Timer _safetyTimer;
    private WinEventWatcher? _watcher;

    // Windows we hid ourselves. We only ever un-hide these, never windows an app hid itself.
    private readonly HashSet<IntPtr> _hiddenByUs = new();

    private HideRule[] _rules = Array.Empty<HideRule>();
    private bool _masterEnabled = true;
    private volatile bool _running = true;

    /// <summary>Raised (on a background thread) with the latest window snapshot after each scan.</summary>
    public event Action<IReadOnlyList<WindowInfo>>? WindowsUpdated;

    public HideManager(AppConfig config)
    {
        _masterEnabled = config.MasterEnabled;
        _rules = config.Rules.ToArray();

        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "WindowHider.Scan" };
        _worker.Start();

        // Safety-net poll in case a window slips past the event hooks; also refreshes the UI.
        _safetyTimer = new System.Threading.Timer(_ => RequestScan(), null, 1500, 1500);
    }

    /// <summary>Must be called from the UI (message-pumping) thread.</summary>
    public void AttachWatcher()
    {
        _watcher = new WinEventWatcher();
        _watcher.WindowEvent += _ => RequestScan();
        RequestScan();
    }

    public void UpdateRules(IEnumerable<HideRule> rules)
    {
        lock (_gate) _rules = rules.ToArray();
        RequestScan();
    }

    public void SetMasterEnabled(bool enabled)
    {
        lock (_gate) _masterEnabled = enabled;
        RequestScan();
    }

    public void RequestScan() => _signal.Set();

    private void WorkerLoop()
    {
        while (_running)
        {
            _signal.WaitOne();
            if (!_running) break;

            // Debounce: absorb a burst of events into a single scan.
            Thread.Sleep(60);
            _signal.Reset();

            try { Scan(); }
            catch { /* never let a scan take down the worker */ }
        }
    }

    private void Scan()
    {
        HideRule[] rules;
        bool master;
        lock (_gate)
        {
            rules = _rules;
            master = _masterEnabled;
        }

        List<WindowInfo> windows = WindowEnumerator.GetVisibleWindows();
        var live = new HashSet<IntPtr>();

        foreach (WindowInfo w in windows)
        {
            live.Add(w.Handle);
            bool desired = master && RuleMatcher.ShouldHide(w, rules);
            bool current = w.IsHiddenFromCapture;

            if (desired && !current)
            {
                if (DisplayAffinity.Set(w.Handle, true))
                {
                    _hiddenByUs.Add(w.Handle);
                    w.IsHiddenFromCapture = true;
                }
            }
            else if (!desired && current && _hiddenByUs.Contains(w.Handle))
            {
                if (DisplayAffinity.Set(w.Handle, false))
                {
                    _hiddenByUs.Remove(w.Handle);
                    w.IsHiddenFromCapture = false;
                }
            }
        }

        // Forget handles that no longer exist.
        _hiddenByUs.RemoveWhere(h => !live.Contains(h));

        WindowsUpdated?.Invoke(windows);
    }

    /// <summary>Restores every window we hid, so shutdown leaves nothing invisible.</summary>
    public void UnhideAll()
    {
        foreach (IntPtr h in _hiddenByUs.ToArray())
            DisplayAffinity.Set(h, false);
        _hiddenByUs.Clear();
    }

    public void Dispose()
    {
        _running = false;
        _signal.Set();
        try { _worker.Join(1000); } catch { }
        _safetyTimer.Dispose();
        _watcher?.Dispose();
        _signal.Dispose();
    }
}
