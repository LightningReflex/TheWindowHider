using System.Windows.Input;
using System.Windows.Media.Imaging;
using TheWindowHider.Core;

namespace TheWindowHider.UI;

/// <summary>One row in the "Open windows" list.</summary>
public sealed class WindowRowViewModel : ObservableObject
{
    public IntPtr Handle { get; init; }
    public string ProcessName { get; init; } = "";
    public string ProcessDisplayName { get; init; } = "";
    public string ExecutablePath { get; init; } = "";
    public BitmapSource? Icon { get; init; }

    private string _title = "";
    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    private bool _isHidden;
    public bool IsHidden
    {
        get => _isHidden;
        set
        {
            if (SetField(ref _isHidden, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    private bool _suppress;
    private bool _hideThisWindow;

    /// <summary>Two-way bound to the row toggle: a per-window "hide" override (session only).</summary>
    public bool HideThisWindow
    {
        get => _hideThisWindow;
        set
        {
            if (SetField(ref _hideThisWindow, value) && !_suppress)
                HideToggled?.Invoke(this, value);
        }
    }

    /// <summary>Set the toggle from code without raising <see cref="HideToggled"/>.</summary>
    public void SetHideQuiet(bool value)
    {
        _suppress = true;
        HideThisWindow = value;
        _suppress = false;
    }

    public event EventHandler<bool>? HideToggled;

    public string StatusText => IsHidden ? "Hidden from capture" : "Visible to capture";

    public string SubTitle => $"{ProcessDisplayName}";

    /// <summary>Adds a persistent "hide the whole app" rule for this window's process.</summary>
    public ICommand HideAppCommand { get; set; } = null!;

    public static WindowRowViewModel FromInfo(WindowInfo w) => new()
    {
        Handle = w.Handle,
        Title = w.Title,
        ProcessName = w.ProcessName,
        ProcessDisplayName = w.ProcessDisplayName,
        ExecutablePath = w.ExecutablePath,
        Icon = IconLoader.ForExecutable(w.ExecutablePath),
        IsHidden = w.IsHiddenFromCapture
    };
}
