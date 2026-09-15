using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Tests;

// Shared configurable cluster fake: unavailable by default (matches the
// non-clustered dev machine), seedable with a full snapshot per test.
public sealed class FakeClusterService : IClusterService
{
    public ClusterTopologySnapshot Snapshot { get; set; } =
        ClusterTopologySnapshot.Unavailable(["Cluster capability unavailable on this machine."]);

    public int Calls { get; private set; }

    public Task<ClusterTopologySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        // Mirrors production (MmiCimQuerier checks cancellation first).
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(Snapshot);
    }
}
