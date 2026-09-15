namespace Pi1.HyperVToolkit.Core.Utilities;

/// <summary>
/// Parity with PowerShell Format-PiTimeSpan (Pi1.Core.psm1):
/// null -&gt; "-", &lt;1s -&gt; "0m", &gt;=1 day -&gt; "Xd Xh Xm",
/// &gt;=1 hour -&gt; "Xh Xm", otherwise "Xm" (integer truncation).
/// </summary>
public static class PiTimeFormatter
{
    public static string Format(TimeSpan? time)
    {
        if (time is null)
        {
            return "-";
        }

        var value = time.Value;
        if (value.TotalSeconds < 1)
        {
            return "0m";
        }

        if (value.TotalDays >= 1)
        {
            return $"{(int)value.Days}d {(int)value.Hours}h {(int)value.Minutes}m";
        }

        if (value.TotalHours >= 1)
        {
            return $"{(int)value.Hours}h {(int)value.Minutes}m";
        }

        return $"{(int)value.Minutes}m";
    }
}
