using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CodexUsageWidget.App.ViewModels;

namespace CodexUsageWidget.App.Converters;

/// <summary>
/// Converts a <see cref="QuotaCardColorState"/> to a progress bar brush.
/// </summary>
public sealed class ColorStateToBrushConverter : IValueConverter
{
    public System.Windows.Media.Brush NormalBrush { get; set; } = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen);
    public System.Windows.Media.Brush WarningBrush { get; set; } = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
    public System.Windows.Media.Brush CriticalBrush { get; set; } = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.OrangeRed);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is QuotaCardColorState state)
        {
            return state switch
            {
                QuotaCardColorState.Warning => WarningBrush,
                QuotaCardColorState.Critical => CriticalBrush,
                _ => NormalBrush,
            };
        }

        return NormalBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
