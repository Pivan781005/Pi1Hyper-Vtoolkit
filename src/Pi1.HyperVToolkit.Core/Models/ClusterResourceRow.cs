namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiClusterResources (Get-ClusterResource). Sorted by
// State, OwnerGroup, Name. ALL resource types are kept (no VM-only filter).
public sealed record ClusterResourceRow(
    string Name,
    string State,
    string OwnerGroup,
    string ResourceType,
    string OwnerNode);
