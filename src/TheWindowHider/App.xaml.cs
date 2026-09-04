using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TheWindowHider.Core;
using TheWindowHider.Native;
using TheWindowHider.UI;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace TheWindowHider;

public partial class App : Application
{
    private const string MutexName = "TheWindowHider.SingleInstance.v1";
    private const string ShowEventName = "TheWindowHider.ShowWindow.v1";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private AppConfig _config = null!;
    private HideManager _manager = null!;
    private TrayIconManager _tray = null!;
    private MainViewModel _viewModel = null!;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Debug/capture aid: forcing software rendering lets tools screenshot the WPF surface.
        if (Environment.GetEnvironmentVariable("WH_SOFTWARE_RENDER") == "1")
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        bool silent = e.Args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

        // ---- uninstall entry point (from Apps & features) ----
        if (e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            Installer.Uninstall();
            if (!silent)
                MessageBox.Show("The Window Hider has been uninstalled.", "The Window Hider",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // ---- scriptable install (e.g. "TheWindowHider.exe --install --silent") ----
        if (e.Args.Any(a => a.Equals("--install", StringComparison.OrdinalIgnoreCase)))
        {
            bool ok = Installer.TryInstall(out string installMessage);
            if (!silent)
                MessageBox.Show(installMessage, "The Window Hider",
                    MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // ---- single instance ----
        _mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            // Ask the already-running instance to surface its window, then quit.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(ShowMainWindow), null, -1, false);

        Privileges.TryEnableDebugPrivilege();

        _config = AppConfig.Load();
        _manager = new HideManager(_config);
        _viewModel = new MainViewModel(_config, _manager, Dispatcher);

        _tray = new TrayIconManager();
        _tray.SetMasterState(_config.MasterEnabled);
        _tray.OpenRequested += ShowMainWindow;
        _tray.ExitRequested += () => Shutdown();
        _tray.SetHidingRequested += state => Dispatcher.BeginInvoke(() => _viewModel.MasterEnabled = state);

        _window = new MainWindow(_viewModel, _tray);

        // Start the event-driven watcher on this (UI, message-pumping) thread.
        _manager.AttachWatcher();

        bool startHidden = _config.StartMinimized ||
                           e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                                           a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        if (!startHidden)
            ShowMainWindow();

        MaybeOfferInstall();
    }

    /// <summary>
    /// One-time offer, on first run of the portable exe, to install into the programs folder.
    /// Shown at most once; afterwards the user can install from the Settings tab.
    /// </summary>
    private void MaybeOfferInstall()
    {
        if (!Installer.CanOfferInstall || _config.InstallPromptShown)
            return;

        _config.InstallPromptShown = true;
        _config.Save();

        MessageBoxResult choice = MessageBox.Show(
            "The Window Hider is running as a portable file:\n" +
            $"{Installer.CurrentExePath}\n\n" +
            "Install it to your programs folder? This adds a Start Menu shortcut and an entry in " +
            "Apps & features, and lets you delete the file you downloaded.\n\n" +
            "You can also do this any time from the Settings tab.",
            "Install The Window Hider?",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (choice != MessageBoxResult.Yes)
            return;

        bool ok = Installer.TryInstall(out string message);
        MessageBox.Show(message, "The Window Hider",
            MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
        _viewModel.RefreshInstallState();
    }

    private void ShowMainWindow()
    {
        if (_window == null) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.ShowInTaskbar = true;
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _manager?.UnhideAll(); } catch { }
        try { _manager?.Dispose(); } catch { }
        try { _tray?.Dispose(); } catch { }
        try { _showEvent?.Dispose(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
