using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Core.Services;

// Cluster-wide immutable snapshot for ONE Cluster page refresh. All tabs
// (nodes through health) project from this single read — never per-tab
// re-queries. IsAvailable=false with explanatory warnings when clustering
// is absent; never an exception, never fake data.
public sealed record ClusterTopologySnapshot(
    bool IsAvailable,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ClusterNodeRow> Nodes,
    IReadOnlyList<ClusterGroupRow> Groups,
    IReadOnlyList<ClusterResourceRow> Resources,
    IReadOnlyList<ClusterNetworkRow> Networks,
    QuorumInfo? Quorum,
    IReadOnlyList<WitnessRow> WitnessRows,
    IReadOnlyList<ClusterEventRow> Events,
    IReadOnlyList<VmPlacementRow> PlacementRows)
{
    public static ClusterTopologySnapshot Unavailable(IReadOnlyList<string> warnings) =>
        new(false, warnings, [], [], [], [], null, [], [], []);
}
