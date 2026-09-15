using System.Text.Json;
using Xunit.Abstractions;

namespace Pi1.HyperVToolkit.Tests.Parity;

/// <summary>
/// Normalizes one JSON row (PS reference or C# collector) into an
/// order-independent string map for deterministic comparison.
/// </summary>
public static class ParityRow
{
    public static Dictionary<string, string> Normalize(JsonElement element, IEnumerable<string> numericFields)
    {
        var numerics = new HashSet<string>(numericFields, StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            map[property.Name] = NormalizeValue(property.Value, numerics.Contains(property.Name)).Trim();
        }

        return map;
    }

    /// <summary>
    /// Generic value normalization. Arrays join deterministically with ", "
    /// (empty array renders "", matching console display of an empty list);
    /// numeric fields format doubles with one decimal. Never throws on
    /// unexpected shapes — unknown values fall back to raw JSON text.
    /// </summary>
    public static string NormalizeValue(JsonElement value, bool numeric)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Number when numeric && value.TryGetDouble(out var d) =>
                d.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(e =>
                e.ValueKind == JsonValueKind.Null || e.ValueKind == JsonValueKind.Undefined
                    ? string.Empty
                    : NormalizeValue(e, numeric))),
            _ => value.GetRawText(),
        };
    }

    public static List<Dictionary<string, string>> ParseRows(string json, IEnumerable<string> numericFields)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null")
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            return [Normalize(root, numericFields)];
        }

        var rows = new List<Dictionary<string, string>>();
        foreach (var element in root.EnumerateArray())
        {
            rows.Add(Normalize(element, numericFields));
        }

        return rows;
    }

    /// <summary>
    /// Compares two row sets by composite key. Returns human-readable differences;
    /// empty means full parity for the compared fields.
    /// </summary>
    public static List<string> Diff(
        List<Dictionary<string, string>> expected,
        List<Dictionary<string, string>> actual,
        Func<Dictionary<string, string>, string> key,
        IEnumerable<string> compareFields,
        Func<string, string, string, bool>? tolerantEquals = null)
    {
        var differences = new List<string>();
        var actualByKey = actual.GroupBy(key).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var exp in expected)
        {
            var k = key(exp);
            if (!actualByKey.TryGetValue(k, out var matches) || matches.Count == 0)
            {
                differences.Add($"MISSING row: {k}");
                continue;
            }

            var act = matches[0];
            matches.RemoveAt(0);
            foreach (var field in compareFields)
            {
                exp.TryGetValue(field, out var e);
                act.TryGetValue(field, out var a);
                // Default comparison is numeric-aware (8 == 8.0) so formatting
                // differences never mask as semantic mismatches. Per-field
                // tolerance (live counters) is opt-in via tolerantEquals.
                var equal = tolerantEquals is not null
                    ? tolerantEquals(field, e ?? string.Empty, a ?? string.Empty)
                    : ParityNumbers.Equals(e, a);
                if (!equal)
                {
                    differences.Add($"DIFF row {k} field {field}: PS='{e}' C#='{a}'");
                }
            }
        }

        foreach (var leftover in actualByKey.Values.SelectMany(v => v))
        {
            differences.Add($"EXTRA row: {key(leftover)}");
        }

        return differences;
    }
}

/// <summary>
/// Format-agnostic numeric equality for parity comparison: 8 == 8.0,
/// 0 == 0.0, 31.9 == 31.90 — while genuinely different values still fail.
/// Non-numeric text always falls back to ordinal string comparison, so values
/// like MAC addresses or version strings are never "numerically" equalized.
/// </summary>
public static class ParityNumbers
{
    public static bool Equals(string? expected, string? actual)
    {
        var e = (expected ?? string.Empty).Trim();
        var a = (actual ?? string.Empty).Trim();
        if (string.Equals(e, a, StringComparison.Ordinal))
        {
            return true;
        }

        if (double.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ed) &&
            double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ad))
        {
            return ed == ad;
        }

        return false;
    }

    public static bool Within(string? expected, string? actual, double tolerance)
    {
        if (Equals(expected, actual))
        {
            return true;
        }

        if (double.TryParse(expected, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ed) &&
            double.TryParse(actual, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ad))
        {
            return Math.Abs(ed - ad) <= tolerance;
        }

        return false;
    }
}

/// <summary>Base with NOT TESTABLE reporting helpers.</summary>
public abstract class ParityTestBase
{
    protected readonly ITestOutputHelper Output;

    protected ParityTestBase(ITestOutputHelper output)
    {
        Output = output;
    }

    protected static void ReportNotTestable(ITestOutputHelper output, string reason)
    {
        output.WriteLine(reason);
    }
}
