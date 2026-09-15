namespace Pi1.HyperVToolkit.Core.Models;

// Pure aggregation over PhysicalDiskRow (Show-PiPhysicalDiskSummary parity).
// Group is "MediaType, BusType" exactly like PowerShell Group-Object naming.
public sealed record PhysicalDiskSummaryRow(
    string Group,
    int Count,
    double TotalGB,
    int Healthy,
    int NotHealthy,
    int CanPool,
    long TotalBytes = 0);
