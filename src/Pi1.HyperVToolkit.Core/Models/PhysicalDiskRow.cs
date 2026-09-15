namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiPhysicalDisks (display order preserved in the view).
// SizeGB rounded to 1 decimal; sorted by MediaType, BusType, FriendlyName.
// LOCAL semantics.
public sealed record PhysicalDiskRow(
    string FriendlyName,
    string MediaType,
    string BusType,
    double SizeGB,
    string HealthStatus,
    string OperationalStatus,
    bool? CanPool,
    string Usage,
    string SerialNumber,
    long SizeBytes = 0);
