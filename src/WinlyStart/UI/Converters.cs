using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinlyStart.UI;

/// <summary>true → Collapsed, false → Visible.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

/// <summary>One button in the left rail (Documents, Pictures, Calendar, …).</summary>
public sealed class RailItemVm
{
    public required string Glyph { get; init; }
    public required string Tooltip { get; init; }
    public required Action Invoke { get; init; }
}
