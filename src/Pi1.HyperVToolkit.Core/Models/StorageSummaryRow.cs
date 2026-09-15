namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiStorageSummary: EXACTLY four rows in this order.
// Threshold frozen: CSV warns below 15 %, jobs warn when count > 0.
public sealed record StorageSummaryRow(
    string Area,
    int Count,
    string Status);
