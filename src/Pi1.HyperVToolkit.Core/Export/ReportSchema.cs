namespace Pi1.HyperVToolkit.Core.Export;

// Deterministic export contract for one stable ResultName: ordered visible
// columns only. The SAME schema drives CSV, HTML and JSON so the three
// formats can never invent different columns.
public sealed record ReportSchema(
    string Name,
    IReadOnlyList<ReportColumn> Columns);
