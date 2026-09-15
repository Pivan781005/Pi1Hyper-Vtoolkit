namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiStoragePools: non-primordial pools, GB rounded to 1 decimal,
// sorted by FriendlyName. LOCAL semantics.
public sealed record StoragePoolRow(
    string FriendlyName,
    string HealthStatus,
    string OperationalStatus,
    double SizeGB,
    double AllocatedGB,
    double FreeGB,
    long SizeBytes = 0,
    long AllocatedBytes = 0,
    long FreeBytes = 0);
