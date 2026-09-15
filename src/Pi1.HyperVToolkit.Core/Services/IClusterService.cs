using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Core.Services;

// Cluster-wide read-only inventory. ONE snapshot per refresh; the view model
// derives every tab from it. Never throws for missing capability —
// IsAvailable=false snapshots carry warnings instead.
public interface IClusterService
{
    Task<ClusterTopologySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
