using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Pi1.HyperVToolkit.Converters;

// Health-score band colors mirroring the console (green >= 90, yellow >= 70,
// red below). Presentation only; bands frozen in Thresholds.
public sealed class HealthScoreToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ExcellentBrush = Create("#2E7D32");
    private static readonly SolidColorBrush GoodBrush = Create("#B26A00");
    private static readonly SolidColorBrush BadBrush = Create("#C62828");
    private static readonly SolidColorBrush NeutralBrush = Create("#616161");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int score)
        {
            if (score >= 90)
            {
                return ExcellentBrush;
            }

            if (score >= 70)
            {
                return GoodBrush;
            }

            return BadBrush;
        }

        return NeutralBrush;
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
