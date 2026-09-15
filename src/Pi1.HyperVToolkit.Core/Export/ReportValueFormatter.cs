using System.Globalization;

namespace Pi1.HyperVToolkit.Core.Export;

// Deterministic text rendering for CSV/HTML. InvariantCulture keeps report
// numbers unambiguous on any machine locale (12.5 never becomes "12,5").
// Semantics mirror Export-Csv: booleans render True/False, null renders empty.
internal static class ReportValueFormatter
{
    internal static string ToDisplayString(object? value) => value switch
    {
        null => string.Empty,
        bool flag => flag ? "True" : "False",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty,
    };
}
