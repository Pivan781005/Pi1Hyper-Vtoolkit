using System.Globalization;
using System.Windows.Data;
using Pi1.HyperVToolkit.Core.Localization;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Converters;

// Localized short date/time display, consistent with the selected UI language
// (sk-SK vs en-US). Null renders "-".
public sealed class LocalizedDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime date)
        {
            return "-";
        }

        var uiCulture = LocalizationService.Instance.CurrentLanguage == AppLanguage.English
            ? CultureInfo.GetCultureInfo("en-US")
            : CultureInfo.GetCultureInfo("sk-SK");
        return date.ToString("g", uiCulture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
