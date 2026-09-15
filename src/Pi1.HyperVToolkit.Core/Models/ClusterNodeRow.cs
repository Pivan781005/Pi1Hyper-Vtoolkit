namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiClusterNodes (Get-ClusterNode). Sorted by Name.
// Missing provider values render empty; no crash, no invented data.
public sealed record ClusterNodeRow(
    string Name,
    string State,
    string DrainStatus,
    string NodeWeight,
    string FaultDomain);
