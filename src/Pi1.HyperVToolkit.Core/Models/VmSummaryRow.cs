namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiVMSummary: EXACTLY six rows in this order; RAM aggregates
// cover RUNNING VMs only. Value is double for numeric-aware parity/display.
// ValueBytes carries raw byte sums for DISPLAY ONLY (null for count rows).
public sealed record VmSummaryRow(
    string Metric,
    double Value,
    long? ValueBytes = null);
