using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CodexUsageWidget.App.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the bound string is not null or empty,
/// otherwise <see cref="Visibility.Collapsed"/>.
/// </summary>
public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string text && !string.IsNullOrEmpty(text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
