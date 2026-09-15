namespace Pi1.HyperVToolkit.Tests.Parity;

// Test-only uptime comparison for live parity. Uptime is sampled at different
// moments by the PowerShell and C# collectors, so a minute boundary may be
// crossed between the two reads ("9h 53m" vs "9h 54m"). Only the parity
// harness uses this; production formatting (PiTimeFormatter) is unchanged.
public static class ParityUptime
{
    // Parses the rendered Format-PiTimeSpan shape ("3d 4h 5m" | "2h 30m" |
    // "45m" | "0m") into total minutes. Returns null for "-" and for any
    // malformed value — callers must fall back to exact comparison then.
    public static int? TryParseMinutes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "-")
        {
            return null;
        }

        try
        {
            var total = 0;
            var seen = false;
            foreach (var part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.EndsWith('d') && int.TryParse(part[..^1], out var d))
                {
                    total += d * 24 * 60;
                    seen = true;
                }
                else if (part.EndsWith('h') && int.TryParse(part[..^1], out var h))
                {
                    total += h * 60;
                    seen = true;
                }
                else if (part.EndsWith('m') && int.TryParse(part[..^1], out var m))
                {
                    total += m;
                    seen = true;
                }
                else
                {
                    return null;
                }
            }

            return seen ? total : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // True on exact match, or when both sides parse and differ by at most
    // toleranceMinutes. Malformed values only pass when exactly equal.
    public static bool WithinTolerance(string? expected, string? actual, int toleranceMinutes = 1)
    {
        var e = (expected ?? string.Empty).Trim();
        var a = (actual ?? string.Empty).Trim();
        if (string.Equals(e, a, StringComparison.Ordinal))
        {
            return true;
        }

        var eMinutes = TryParseMinutes(e);
        var aMinutes = TryParseMinutes(a);
        if (eMinutes.HasValue && aMinutes.HasValue)
        {
            return Math.Abs(eMinutes.Value - aMinutes.Value) <= toleranceMinutes;
        }

        return false;
    }
}
