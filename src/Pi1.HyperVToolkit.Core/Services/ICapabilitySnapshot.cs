using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Core.Services;

/// <summary>
/// Latest capability probe results, shared by the shell and settings.
/// Mutable by design: MainViewModel refreshes it after probing.
/// </summary>
public interface ICapabilitySnapshot
{
    CapabilityReport? HyperV { get; }
    CapabilityReport? Cluster { get; }
    CapabilityReport? Cim { get; }

    bool IsClusterAvailable { get; }
    bool IsHyperVAvailable { get; }

    void Update(CapabilityReport? hyperV, CapabilityReport? cluster, CapabilityReport? cim);
}
