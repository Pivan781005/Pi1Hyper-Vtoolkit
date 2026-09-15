namespace Pi1.HyperVToolkit.Core.Models;

/// <summary>Capability probed at startup. Parity with Test-HyperVModule / Test-PiClusterModule.</summary>
public enum CapabilityKind
{
    HyperV,
    FailoverCluster,
    Cim,
}

/// <summary>
/// Result of a single capability probe. Probes never throw:
/// unavailability is data, not an exception.
/// </summary>
/// <param name="Kind">Probed capability.</param>
/// <param name="IsAvailable">True when the capability can be used.</param>
/// <param name="Detail">Slovak human-readable detail for the status bar / warning banner.</param>
public sealed record CapabilityReport(CapabilityKind Kind, bool IsAvailable, string Detail);
