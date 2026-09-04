using System.ComponentModel;
using System.Windows;
using TheWindowHider.UI;
using Application = System.Windows.Application;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;

namespace TheWindowHider;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TrayIconManager _tray;

    public MainWindow(MainViewModel viewModel, TrayIconManager tray)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _tray = tray;
        DataContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.MasterEnabled))
            _tray.SetMasterState(_viewModel.MasterEnabled);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _viewModel.CloseToTray)
        {
            Hide();
            ShowInTaskbar = false;
        }
    }

    // Rule values commit on focus-leave; Enter commits without leaving the field, so hiding
    // isn't churned on every keystroke while you're still typing the value.
    private void RuleValue_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.CloseToTray)
        {
            // Keep running in the background instead of exiting.
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
        }
        else
        {
            Application.Current.Shutdown();
        }
    }
}
