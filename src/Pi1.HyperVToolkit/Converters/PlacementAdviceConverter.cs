using System.Globalization;
using System.Windows.Data;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Converters;

// Placement Advisor recommendation display: semantic kind (+count) from Core
// renders localized WPF text. Testable without STA (plain code).
public sealed class PlacementAdviceConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var loc = LocalizationService.Instance;
        if (values.Length == 0 || values[0] is not PlacementAdviceKind kind)
        {
            return string.Empty;
        }

        var count = values.Length > 1 && values[1] is int n ? n : 0;
        return kind switch
        {
            PlacementAdviceKind.Balanced => loc["Adv_Balanced"],
            PlacementAdviceKind.HighDemand => loc["Adv_HighDemand"],
            PlacementAdviceKind.HighAssigned => loc["Adv_HighAssigned"],
            PlacementAdviceKind.NoPreferredOwners => string.Format(CultureInfo.InvariantCulture, loc["Adv_NoPreferred"], count),
            _ => string.Empty,
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
