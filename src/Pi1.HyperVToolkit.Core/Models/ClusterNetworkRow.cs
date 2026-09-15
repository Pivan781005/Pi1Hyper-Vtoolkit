namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiClusterNetworks (Get-ClusterNetwork). Sorted by Name.
// Role/Metric mappings are best-effort native (PARTIAL until live verified).
public sealed record ClusterNetworkRow(
    string Name,
    string State,
    string Role,
    string Address,
    string AddressMask,
    int? Metric,
    bool? AutoMetric);
