using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Converters;

/// <summary>
/// Maps <see cref="Severity"/> to a brush. Mirrors the PowerShell
/// Write-PiTable colour rules (green / cyan-blue / yellow-orange / red / gray).
/// </summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush OkBrush = Create("#2E7D32");
    private static readonly SolidColorBrush InfoBrush = Create("#0277BD");
    private static readonly SolidColorBrush WarningBrush = Create("#B26A00");
    private static readonly SolidColorBrush CriticalBrush = Create("#C62828");
    private static readonly SolidColorBrush NeutralBrush = Create("#616161");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Severity severity
            ? severity switch
            {
                Severity.Ok => OkBrush,
                Severity.Info => InfoBrush,
                Severity.Warning => WarningBrush,
                Severity.Critical => CriticalBrush,
                _ => NeutralBrush,
            }
            : NeutralBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Create(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
