using System.Globalization;
using System.Windows.Data;
using Pi1.HyperVToolkit.Core.Utilities;

namespace Pi1.HyperVToolkit.Converters;

// Single shared byte-count display: raw bytes (long/ulong/double) render as
// dynamic MB/GB/TB with two decimals ("31.90 GB"); null renders "-".
public sealed class DataSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double? bytes = value switch
        {
            null => null,
            long l => (double)l,
            ulong u => (double)u,
            int i => (double)i,
            double d => d,
            float f => (double)f,
            _ => null,
        };
        return DataSize.FormatBytes(bytes);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
