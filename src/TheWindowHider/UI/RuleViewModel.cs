using System.Windows.Input;
using TheWindowHider.Core;

namespace TheWindowHider.UI;

/// <summary>Editable wrapper around a persistent <see cref="HideRule"/>.</summary>
public sealed class RuleViewModel : ObservableObject
{
    public HideRule Model { get; }

    /// <summary>Raised whenever any field changes, so the owner can re-sync + persist.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the user asks to delete this rule.</summary>
    public event EventHandler? DeleteRequested;

    public RuleViewModel(HideRule model)
    {
        Model = model;
        DeleteCommand = new RelayCommand(() => DeleteRequested?.Invoke(this, EventArgs.Empty));
    }

    public Array TargetOptions { get; } = Enum.GetValues(typeof(RuleTarget));
    public Array ModeOptions { get; } = Enum.GetValues(typeof(MatchMode));

    public string Summary => Model.Describe();

    public RuleTarget Target
    {
        get => Model.Target;
        set { if (Model.Target != value) { Model.Target = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); Raise(); } }
    }

    public MatchMode Mode
    {
        get => Model.Mode;
        set { if (Model.Mode != value) { Model.Mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); Raise(); } }
    }

    public string Value
    {
        get => Model.Value;
        set { if (Model.Value != value) { Model.Value = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); Raise(); } }
    }

    public bool CaseSensitive
    {
        get => Model.CaseSensitive;
        set { if (Model.CaseSensitive != value) { Model.CaseSensitive = value; OnPropertyChanged(); Raise(); } }
    }

    public bool Enabled
    {
        get => Model.Enabled;
        set { if (Model.Enabled != value) { Model.Enabled = value; OnPropertyChanged(); Raise(); } }
    }

    public bool IsException
    {
        get => Model.IsException;
        set { if (Model.IsException != value) { Model.IsException = value; OnPropertyChanged(); Raise(); } }
    }

    public ICommand DeleteCommand { get; }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
