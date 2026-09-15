using System.Globalization;
using System.Windows.Data;
using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Converters;

// VM summary value display: byte sums render as dynamic MB/GB/TB
// ("31.90 GB"); count rows (no bytes) render as plain integers.
public sealed class VmSummaryValueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        double value = 0;
        if (values.Length > 0)
        {
            value = values[0] switch
            {
                double d => d,
                int i => (double)i,
                long l => (double)l,
                _ => 0,
            };
        }

        long? bytes = values.Length > 1 ? values[1] switch
        {
            long l => (long?)l,
            int i => (long?)i,
            double d => (long?)d,
            _ => null,
        } : null;

        if (bytes.HasValue)
        {
            return DataSize.FormatBytes(bytes.Value);
        }

        return value.ToString("0", CultureInfo.InvariantCulture);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
