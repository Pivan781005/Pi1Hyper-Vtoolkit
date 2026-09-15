using System.Globalization;
using System.Windows.Data;

namespace Pi1.HyperVToolkit.Converters;

/// <summary>
/// Compares an enum value with the enum name passed as ConverterParameter.
/// Used for RadioButton groups bound to <see cref="Core.Models.ScopeMode"/>.
/// </summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not null && string.Equals(value.ToString(), parameter as string, StringComparison.Ordinal);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string name && targetType.IsEnum)
        {
            return Enum.Parse(targetType, name);
        }

        return Binding.DoNothing;
    }
}
