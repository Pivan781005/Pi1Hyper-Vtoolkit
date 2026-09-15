using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.Mapping;
using static Pi1.HyperVToolkit.Core.Aggregations.DashboardCalculator;

namespace Pi1.HyperVToolkit.Infrastructure.Cluster;

// Dashboard-only cluster identity (name + node states) over root/MSCluster.
// Deliberately minimal: Phase 4 must not start the Cluster feature page.
// Unavailable clustering yields an empty name and no nodes (dashboard N/A),
// never an exception.
public sealed class ClusterInfoService : IClusterInfoService
{
    private const string Ns = @"root\MSCluster";

    private readonly ICimQuerier _cim;
    private readonly ILogger<ClusterInfoService> _logger;

    public ClusterInfoService(ICimQuerier cim, ILogger<ClusterInfoService> logger)
    {
        _cim = cim ?? throw new ArgumentNullException(nameof(cim));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ClusterSnapshot> GetClusterSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        string name = string.Empty;
        try
        {
            var clusters = await _cim.QueryAsync(
                ".", Ns, "SELECT Name FROM MSCluster_Cluster",
                TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            name = clusters.Count > 0 ? CimValues.GetString(clusters[0], "Name") : string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Klastrová funkcia nie je dostupná; dashboard zobrazí N/A.");
            return new ClusterSnapshot(string.Empty, []);
        }

        IReadOnlyList<(string Name, string State)> nodes = [];
        try
        {
            var nodeBags = await _cim.QueryAsync(
                ".", Ns, "SELECT Name, State FROM MSCluster_Node",
                TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            nodes = nodeBags
                .Select(b => (CimValues.GetString(b, "Name"), MapNodeState(b)))
                .Where(n => !string.IsNullOrEmpty(n.Item1))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Zoznam uzlov klastra nie je dostupný.");
        }

        return new ClusterSnapshot(name, nodes);
    }

    // MSCluster_Node.State: 0 Up, 1 Down, 2 Paused, 3 Joining (MS docs).
    // The dashboard compares State == "Up"; every other state keeps its name.
    private static string MapNodeState(IReadOnlyDictionary<string, object?> bag)
    {
        var raw = CimValues.GetLong(bag, "State");
        if (raw is null && bag.TryGetValue("State", out var text) && text is string name)
        {
            return name;
        }

        return raw switch
        {
            0 => "Up",
            1 => "Down",
            2 => "Paused",
            3 => "Joining",
            null => string.Empty,
            _ => $"Unknown ({raw})",
        };
    }
}
