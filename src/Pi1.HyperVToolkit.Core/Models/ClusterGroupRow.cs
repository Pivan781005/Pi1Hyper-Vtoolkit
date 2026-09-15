namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiClusterRoles (Get-ClusterGroup). Sorted by OwnerNode, Name.
// Priority is kept VERBATIM ("High" or "3000" both occur); the distribution
// rule matches either form. OwnerNode is a clean node-name string.
public sealed record ClusterGroupRow(
    string Name,
    string State,
    string OwnerNode,
    string GroupType,
    string Priority);
