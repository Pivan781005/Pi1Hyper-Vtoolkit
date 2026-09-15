using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Pi1.HyperVToolkit.Converters;

// Inverse of BoolToVisibilityConverter: true collapses, false shows.
// Used for unavailable-state banners.
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
