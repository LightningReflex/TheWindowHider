using System.Globalization;
using System.Windows;
using System.Windows.Data;
using TheWindowHider.Core;

namespace TheWindowHider.UI;

/// <summary>Renders rule enums as friendly, human-readable labels.</summary>
public sealed class EnumFriendlyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RuleTarget.Process => "Application (.exe)",
        RuleTarget.WindowTitle => "Window title",
        RuleTarget.WindowHandle => "Specific window",
        MatchMode.Equals => "is exactly",
        MatchMode.Contains => "contains",
        MatchMode.StartsWith => "starts with",
        MatchMode.EndsWith => "ends with",
        MatchMode.Regex => "matches regex",
        _ => value?.ToString() ?? ""
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Bool -> Visibility. Pass ConverterParameter="invert" to flip.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        if (parameter as string == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Non-null / non-empty -> Visible, else Collapsed.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool has = value switch
        {
            null => false,
            string s => !string.IsNullOrEmpty(s),
            _ => true
        };
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverts a boolean.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;
}
