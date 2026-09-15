using System.Globalization;
using System.Windows.Data;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Converters;

// Failover Simulation advice display: semantic kind from Core renders
// localized WPF text. Testable without STA (plain code).
public sealed class FailoverAdviceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var loc = LocalizationService.Instance;
        return value is FailoverAdviceKind kind
            ? kind switch
            {
                FailoverAdviceKind.Viable => loc["Fo_Viable"],
                FailoverAdviceKind.Tight => loc["Fo_Tight"],
                FailoverAdviceKind.Critical => loc["Fo_Critical"],
                FailoverAdviceKind.AssignedHigh => loc["Fo_AssignedHigh"],
                _ => string.Empty,
            }
            : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
