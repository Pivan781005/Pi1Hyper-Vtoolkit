using static Pi1.HyperVToolkit.Core.Aggregations.DashboardCalculator;

namespace Pi1.HyperVToolkit.Core.Services;

// Minimal cluster identity for the dashboard ONLY (cluster name + node states).
// Phase 4 must NOT grow this into the Cluster feature page: no roles,
// resources, networks, quorum or witness data belong here.
public interface IClusterInfoService
{
    // Local, capability-gated read over root/MSCluster. Returns an empty name
    // and no nodes when clustering is unavailable — callers render N/A.
    Task<ClusterSnapshot> GetClusterSnapshotAsync(
        CancellationToken cancellationToken = default);
}
