namespace Pi1.HyperVToolkit.Core.Services;

/// <summary>
/// Central current-result state. Parity intent with PowerShell
/// $Script:LastResult / $Script:LastResultName, but generic and owned by ONE
/// service so the v0.9.4 cross-module $Script: bug cannot recur.
/// Future DataGrid views bind to <see cref="CurrentRows"/>; export reads from here.
/// </summary>
public interface IReportService
{
    /// <summary>Name of the current result (e.g. "Dashboard"). Empty when none.</summary>
    string CurrentName { get; }

    int Count { get; }

    Type? RowType { get; }

    /// <summary>Rows for ItemsSource binding. Null when no current result.</summary>
    System.Collections.IEnumerable? CurrentRows { get; }

    void SetCurrent<T>(IReadOnlyList<T> rows, string name);

    IReadOnlyList<T> GetRows<T>();

    void Clear();

    event EventHandler? CurrentChanged;
}
