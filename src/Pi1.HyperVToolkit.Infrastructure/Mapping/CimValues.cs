using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;

namespace Pi1.HyperVToolkit.Infrastructure.Mapping;

/// <summary>
/// Defensive CIM property readers. Missing or mistyped properties yield null
/// (PowerShell renders them as empty); callers apply parity defaults.
/// </summary>
public static partial class CimValues
{
    [GeneratedRegex(@"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")]
    private static partial Regex GuidRegex();

    public static string GetString(IReadOnlyDictionary<string, object?> bag, string name)
    {
        if (bag.TryGetValue(name, out var value) && value is not null)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return string.Empty;
    }

    public static bool? GetBool(IReadOnlyDictionary<string, object?> bag, string name)
    {
        if (!bag.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        if (value is bool b)
        {
            return b;
        }

        return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed) ? parsed : null;
    }

    public static int? GetInt(IReadOnlyDictionary<string, object?> bag, string name)
    {
        var number = GetDouble(bag, name);
        return number.HasValue ? (int)Math.Truncate(number.Value) : null;
    }

    public static long? GetLong(IReadOnlyDictionary<string, object?> bag, string name)
    {
        if (!bag.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception) when (value is string || value is IConvertible)
        {
            return null;
        }
    }

    public static ulong? GetULong(IReadOnlyDictionary<string, object?> bag, string name)
    {
        if (!bag.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception) when (value is string || value is IConvertible)
        {
            return null;
        }
    }

    public static double? GetDouble(IReadOnlyDictionary<string, object?> bag, string name)
    {
        if (!bag.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception) when (value is string || value is IConvertible)
        {
            return null;
        }
    }

    public static string[] GetStringArray(IReadOnlyDictionary<string, object?> bag, string name)
    {
        if (!bag.TryGetValue(name, out var value) || value is null)
        {
            return [];
        }

        if (value is string[] strings)
        {
            return strings.Where(s => s is not null).ToArray();
        }

        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            return enumerable.Cast<object?>()
                .Where(o => o is not null)
                .Select(o => Convert.ToString(o, CultureInfo.InvariantCulture) ?? string.Empty)
                .Where(s => s.Length > 0)
                .ToArray();
        }

        var single = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(single) ? [] : [single];
    }

    /// <summary>Handles both DateTime values and CIM datetime strings.</summary>
    public static DateTime? GetDateTime(IReadOnlyDictionary<string, object?> bag, string name)
    {
        if (!bag.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        if (value is DateTime dt)
        {
            return dt;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(text);
        }
        catch (Exception) when (value is string || value is IConvertible)
        {
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
        }
    }

    /// <summary>All GUIDs embedded in a WMI reference string (InstanceID, Parent, …).</summary>
    public static IReadOnlyList<string> ExtractGuids(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return GuidRegex().Matches(text).Select(m => m.Value.ToUpperInvariant()).ToList();
    }

    /// <summary>PowerShell [math]::Round(x,1) parity (MidpointRounding.ToEven).</summary>
    public static double Round1(double value) => Math.Round(value, 1);

    public static double MegabytesToGigabytes(double megabytes) => Round1(megabytes / 1024.0);

    public static double BytesToGigabytes(double bytes) => Round1(bytes / (1024.0 * 1024.0 * 1024.0));

    /// <summary>Raw byte count for display precision (exact for integer MB sources).</summary>
    public static long MegabytesToBytes(double megabytes) =>
        (long)Math.Round(megabytes * 1024.0 * 1024.0, MidpointRounding.AwayFromZero);
}
