using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using TheWindowHider.Core;

namespace TheWindowHider.UI;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly HideManager _manager;
    private readonly Dispatcher _dispatcher;

    // Handle rules created from per-window toggles; kept in memory only.
    private readonly List<HideRule> _sessionRules = new();

    public ObservableCollection<WindowRowViewModel> Windows { get; } = new();
    public ObservableCollection<RuleViewModel> Rules { get; } = new();
    public ICollectionView WindowsView { get; }

    public ICommand AddRuleCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand InstallCommand { get; }

    public MainViewModel(AppConfig config, HideManager manager, Dispatcher dispatcher)
    {
        _config = config;
        _manager = manager;
        _dispatcher = dispatcher;

        _masterEnabled = config.MasterEnabled;
        _startMinimized = config.StartMinimized;
        _closeToTray = config.CloseToTray;
        _startWithWindows = StartupManager.IsEnabled();

        foreach (HideRule rule in config.Rules)
            AddRuleViewModel(new RuleViewModel(rule));

        WindowsView = CollectionViewSource.GetDefaultView(Windows);
        WindowsView.Filter = FilterWindow;
        if (WindowsView is ListCollectionView lcv)
            lcv.CustomSort = new WindowRowComparer();

        AddRuleCommand = new RelayCommand(AddBlankRule);
        RefreshCommand = new RelayCommand(() => _manager.RequestScan());
        InstallCommand = new RelayCommand(DoInstall, () => Installer.CanOfferInstall);

        _manager.WindowsUpdated += OnWindowsUpdated;
        PushEffectiveRules();
    }

    // ---- top-level toggles / settings ----

    private bool _masterEnabled;
    public bool MasterEnabled
    {
        get => _masterEnabled;
        set
        {
            if (!SetField(ref _masterEnabled, value)) return;
            _config.MasterEnabled = value;
            _config.Save();
            _manager.SetMasterEnabled(value);
            OnPropertyChanged(nameof(StatusSummary));
            OnPropertyChanged(nameof(MasterStateText));
        }
    }

    public string MasterStateText => MasterEnabled ? "Hiding is ON" : "Hiding is paused";

    private bool _startWithWindows;
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!SetField(ref _startWithWindows, value)) return;
            StartupManager.Set(value);
            _config.StartWithWindows = value;
            _config.Save();
        }
    }

    private bool _startMinimized;
    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (!SetField(ref _startMinimized, value)) return;
            _config.StartMinimized = value;
            _config.Save();
        }
    }

    private bool _closeToTray;
    public bool CloseToTray
    {
        get => _closeToTray;
        set
        {
            if (!SetField(ref _closeToTray, value)) return;
            _config.CloseToTray = value;
            _config.Save();
        }
    }

    // ---- install (single-file build only) ----

    /// <summary>Show the install card only for a single-file deployment.</summary>
    public bool ShowInstallSection => Installer.IsSingleFileDeployment;

    public bool CanInstall => Installer.CanOfferInstall;

    public string InstallStatusText => Installer.IsRunningFromInstall
        ? "Installed, and running from your programs folder."
        : Installer.IsInstalledOnDisk
            ? "Installed. Launch it from the Start Menu shortcut next time."
            : "Running as a portable file. Install it to add a Start Menu shortcut and an uninstall entry.";

    private void DoInstall()
    {
        bool ok = Installer.TryInstall(out string message);
        System.Windows.MessageBox.Show(message, "The Window Hider",
            MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
        RefreshInstallState();
    }

    public void RefreshInstallState()
    {
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(InstallStatusText));
        CommandManager.InvalidateRequerySuggested();
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
                WindowsView.Refresh();
        }
    }

    public int HiddenCount => Windows.Count(w => w.IsHidden);

    public string StatusSummary => !MasterEnabled
        ? "Paused: nothing is being hidden"
        : $"{HiddenCount} of {Windows.Count} windows hidden from capture";

    // ---- window list reconciliation (called on background thread) ----

    private void OnWindowsUpdated(IReadOnlyList<WindowInfo> windows)
    {
        if (_dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(() => Reconcile(windows));
    }

    private void Reconcile(IReadOnlyList<WindowInfo> windows)
    {
        var live = new HashSet<IntPtr>();
        var byHandle = Windows.ToDictionary(w => w.Handle);

        foreach (WindowInfo info in windows)
        {
            live.Add(info.Handle);
            bool targeted = _sessionRules.Any(r => r.Value == info.Handle.ToInt64().ToString());

            if (byHandle.TryGetValue(info.Handle, out WindowRowViewModel? row))
            {
                row.Title = info.Title;
                row.IsHidden = info.IsHiddenFromCapture;
                row.SetHideQuiet(targeted);
            }
            else
            {
                WindowRowViewModel newRow = WindowRowViewModel.FromInfo(info);
                newRow.HideAppCommand = new RelayCommand(() => AddProcessRule(newRow.ProcessName));
                newRow.HideToggled += OnWindowHideToggled;
                newRow.SetHideQuiet(targeted);
                Windows.Add(newRow);
            }
        }

        for (int i = Windows.Count - 1; i >= 0; i--)
        {
            if (!live.Contains(Windows[i].Handle))
            {
                Windows[i].HideToggled -= OnWindowHideToggled;
                Windows.RemoveAt(i);
            }
        }

        // Drop session rules whose window is gone.
        int removed = _sessionRules.RemoveAll(r => !live.Contains((IntPtr)long.Parse(r.Value)));
        if (removed > 0) PushEffectiveRules();

        WindowsView.Refresh();
        OnPropertyChanged(nameof(HiddenCount));
        OnPropertyChanged(nameof(StatusSummary));
    }

    private bool FilterWindow(object obj)
    {
        if (obj is not WindowRowViewModel row) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        string q = SearchText.Trim();
        return row.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.ProcessDisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || row.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    // ---- per-window toggle ----

    private void OnWindowHideToggled(object? sender, bool hide)
    {
        if (sender is not WindowRowViewModel row) return;
        string key = row.Handle.ToInt64().ToString();

        if (hide)
        {
            if (!_sessionRules.Any(r => r.Value == key))
                _sessionRules.Add(new HideRule { Target = RuleTarget.WindowHandle, Value = key });
        }
        else
        {
            _sessionRules.RemoveAll(r => r.Value == key);
        }
        PushEffectiveRules();
    }

    // ---- rules ----

    private void AddBlankRule()
    {
        var rule = new HideRule { Target = RuleTarget.Process, Mode = MatchMode.Equals, Value = "" };
        AddRuleViewModel(new RuleViewModel(rule));
        PersistAndPush();
    }

    private void AddProcessRule(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return;
        bool exists = Rules.Any(r =>
            r.Model.Target == RuleTarget.Process &&
            r.Model.Mode == MatchMode.Equals &&
            string.Equals(r.Model.Value, processName, StringComparison.OrdinalIgnoreCase) &&
            !r.Model.IsException);
        if (exists) return;

        var rule = new HideRule { Target = RuleTarget.Process, Mode = MatchMode.Equals, Value = processName };
        AddRuleViewModel(new RuleViewModel(rule));
        PersistAndPush();
    }

    private void AddRuleViewModel(RuleViewModel vm)
    {
        vm.Changed += (_, _) => PersistAndPush();
        vm.DeleteRequested += OnRuleDeleteRequested;
        Rules.Add(vm);
    }

    private void OnRuleDeleteRequested(object? sender, EventArgs e)
    {
        if (sender is not RuleViewModel vm) return;
        vm.DeleteRequested -= OnRuleDeleteRequested;
        Rules.Remove(vm);
        PersistAndPush();
    }

    private void PersistAndPush()
    {
        _config.Rules = Rules.Select(r => r.Model).ToList();
        _config.Save();
        PushEffectiveRules();
    }

    private void PushEffectiveRules()
    {
        var effective = Rules.Select(r => r.Model).Concat(_sessionRules).ToList();
        _manager.UpdateRules(effective);
    }

    private sealed class WindowRowComparer : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not WindowRowViewModel a || y is not WindowRowViewModel b) return 0;
            int p = string.Compare(a.ProcessDisplayName, b.ProcessDisplayName, StringComparison.OrdinalIgnoreCase);
            return p != 0 ? p : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        }
    }
}
