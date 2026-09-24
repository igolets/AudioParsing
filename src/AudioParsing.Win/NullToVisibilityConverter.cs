using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioParsing.Win;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the bound value is non-null and non-empty,
/// otherwise <see cref="Visibility.Collapsed"/>.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        bool hasValue = value switch
        {
            string s => !string.IsNullOrWhiteSpace(s),
            not null => true,
            _ => false,
        };
        bool visible = invert ? !hasValue : hasValue;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
