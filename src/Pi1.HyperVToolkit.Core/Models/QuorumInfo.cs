namespace Pi1.HyperVToolkit.Core.Models;

// Normalized Get-ClusterQuorum output (Show-PiQuorum parity). Read-only:
// the application never configures quorum.
public sealed record QuorumInfo(
    string QuorumType,
    string QuorumResource);
