using System.Globalization;
using System.Windows.Data;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Converters;

// Displays booleans as localized Yes/No (Áno/Nie). Null renders empty.
// CheckBox columns keep native checkboxes; detail panels use this converter.
public sealed class BoolYesNoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is bool flag)
        {
            return LocalizationService.Instance[flag ? "V_Yes" : "V_No"];
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
