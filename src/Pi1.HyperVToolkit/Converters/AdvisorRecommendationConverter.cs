using System.Globalization;
using System.Windows.Data;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Converters;

// Advisor recommendation display: semantic (Rule, IsSensitive) from Core
// renders localized WPF text. Testable without STA (plain code).
// Sensitive override applies to Info findings only (Pressure keeps its own
// text), exactly like Show-PiAdvisor.
public sealed class AdvisorRecommendationConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var loc = LocalizationService.Instance;
        if (values.Length == 0 || values[0] is not AdvisorRule rule)
        {
            return string.Empty;
        }

        var sensitive = values.Length > 1 && values[1] is bool flag && flag;
        if (rule == AdvisorRule.Pressure)
        {
            return loc["Diag_Pressure"];
        }

        if (sensitive)
        {
            return loc["Diag_Sensitive"];
        }

        return rule switch
        {
            AdvisorRule.LargeReserve => loc["Diag_LargeReserve"],
            AdvisorRule.Reserve => loc["Diag_Reserve"],
            _ => string.Empty,
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
