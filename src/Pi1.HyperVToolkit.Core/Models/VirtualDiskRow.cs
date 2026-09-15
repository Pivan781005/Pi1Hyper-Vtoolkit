namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiVirtualDisks: GB rounded to 1 decimal, sorted by
// FriendlyName. LOCAL semantics.
public sealed record VirtualDiskRow(
    string FriendlyName,
    string HealthStatus,
    string OperationalStatus,
    string ResiliencySettingName,
    string ProvisioningType,
    double SizeGB,
    double AllocatedGB,
    long SizeBytes = 0,
    long AllocatedBytes = 0);
