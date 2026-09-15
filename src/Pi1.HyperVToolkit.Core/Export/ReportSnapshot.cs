using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Core.Export;

// Immutable point-in-time copy of the IReportService current result, taken at
// export-action time. Later UI navigation or reloads cannot mutate rows while
// the file is being generated. Null when no report was ever published.
public sealed record ReportSnapshot(string Name, IReadOnlyList<object> Rows)
{
    public static ReportSnapshot? Capture(IReportService report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = report.CurrentRows;
        if (rows is null || string.IsNullOrWhiteSpace(report.CurrentName))
        {
            return null;
        }

        var frozen = new List<object>();
        foreach (var row in rows)
        {
            if (row is not null)
            {
                frozen.Add(row);
            }
        }

        return new ReportSnapshot(report.CurrentName, frozen);
    }
}
