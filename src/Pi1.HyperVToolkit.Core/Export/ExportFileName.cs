using System.Text.RegularExpressions;

namespace Pi1.HyperVToolkit.Core.Export;

// Legacy filename semantics (Export-PiLastResultCsv): sanitize the report
// name with [^\w\-] => _, fall back to Pi1_Report when blank, default path
// is Desktop\{Name}_{yyyyMMdd_HHmmss}.{ext}. The clock is caller-supplied so
// tests can freeze 2026-09-15 12:34:56 and prove Advisor_20260915_123456.csv.
public static class ExportFileName
{
    public const string FallbackName = "Pi1_Report";

    public static string Sanitize(string? reportName)
    {
        var safe = Regex.Replace(reportName ?? string.Empty, @"[^\w\-]", "_");
        return string.IsNullOrWhiteSpace(safe) ? FallbackName : safe;
    }

    public static string BuildDefaultFileName(string? reportName, string extension, DateTimeOffset now) =>
        $"{Sanitize(reportName)}_{now:yyyyMMdd_HHmmss}.{extension.TrimStart('.')}";

    public static string DefaultFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string BuildDefaultPath(string? reportName, string extension, DateTimeOffset now) =>
        Path.Combine(DefaultFolder, BuildDefaultFileName(reportName, extension, now));
}
