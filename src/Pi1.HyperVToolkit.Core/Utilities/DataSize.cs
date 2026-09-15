namespace Pi1.HyperVToolkit.Core.Utilities;

// ONE reusable data-size display formatter. Binary Windows/PowerShell-style
// boundaries (1 KB = 1024 B); labels MB / GB / TB (never MiB/GiB/TiB).
// Invariant-culture formatting ("31.90 GB") used consistently in both UI
// languages. Null/unavailable renders "-" and stays distinct from zero
// ("0.00 MB"). Negative values (e.g. memory difference) keep their sign.
public static class DataSize
{
    private const double Kb = 1024.0;
    private const double Mb = 1024.0 * 1024.0;
    private const double Gb = 1024.0 * 1024.0 * 1024.0;
    private const double Tb = 1024.0 * 1024.0 * 1024.0 * 1024.0;

    public static string FormatBytes(double? bytes)
    {
        if (bytes is null || double.IsNaN(bytes.Value))
        {
            return "-";
        }

        var value = bytes.Value;
        var sign = value < 0 ? "-" : string.Empty;
        var abs = Math.Abs(value);

        string text;
        if (abs < Gb)
        {
            text = (abs / Mb).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        }
        else if (abs < Tb)
        {
            text = (abs / Gb).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " GB";
        }
        else
        {
            text = (abs / Tb).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " TB";
        }

        return sign + text;
    }

    public static string FormatBytes(long? bytes) =>
        FormatBytes(bytes.HasValue ? (double?)bytes.Value : null);

    public static string FormatBytes(ulong? bytes) =>
        FormatBytes(bytes.HasValue ? (double?)bytes.Value : null);

    public static double GigabytesToBytes(double gigabytes) => gigabytes * Gb;
}
