using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;
using Pi1.HyperVToolkit.Core.Cluster;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.Common;
using Pi1.HyperVToolkit.Infrastructure.Mapping;

namespace Pi1.HyperVToolkit.Infrastructure.Cluster;

// Native cluster-wide inventory over root\MSCluster (+ local event log).
// STRICTLY READ-ONLY: queries and event-log reads only; no method invocation,
// no state change. ONE snapshot per refresh — every Cluster tab projects from
// it, never per-tab re-queries. Missing capability/classes/rows degrade to an
// unavailable snapshot or partial rows with warnings; nothing is invented.
public sealed partial class ClusterService : IClusterService
{
    private const string Ns = ClusterMapper.ClusterNamespace;

    [GeneratedRegex("Witness", RegexOptions.IgnoreCase)]
    private static partial Regex WitnessTypeRegex();

    [GeneratedRegex("Witness|Quorum", RegexOptions.IgnoreCase)]
    private static partial Regex WitnessNameRegex();

    private readonly ICimQuerier _cim;
    private readonly ClusterEventReader _events;
    private readonly ILogger<ClusterService> _logger;

    public ClusterService(ICimQuerier cim, ClusterEventReader events, ILogger<ClusterService> logger)
    {
        _cim = cim ?? throw new ArgumentNullException(nameof(cim));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ClusterTopologySnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        // Capability gate: the cluster object itself must resolve first.
        IReadOnlyList<IReadOnlyDictionary<string, object?>> clusterBags;
        try
        {
            clusterBags = await _cim.QueryAsync(
                ".", Ns, "SELECT Name, QuorumType, QuorumResource FROM MSCluster_Cluster",
                CimTimeouts.Query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Klastrová funkcia nie je dostupná.");
            return ClusterTopologySnapshot.Unavailable(
                ["Cluster capability unavailable on this machine."]);
        }

        if (clusterBags.Count == 0)
        {
            return ClusterTopologySnapshot.Unavailable(
                ["Cluster capability unavailable on this machine."]);
        }

        var nodes = await CollectAsync(
            "SELECT Name, State, DrainStatus, NodeWeight, FaultDomain FROM MSCluster_Node",
            ClusterMapper.MapNode, "uzly klastra", warnings, cancellationToken).ConfigureAwait(false);
        nodes = nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // Resources first: group mapping derives VM groups from them.
        var resources = await CollectAsync(
            "SELECT Name, State, OwnerGroup, OwnerNode, ResourceType FROM MSCluster_Resource",
            ClusterMapper.MapResource, "prostriedky klastra", warnings, cancellationToken).ConfigureAwait(false);
        resources = resources
            .OrderBy(r => r.State, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.OwnerGroup, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var groups = await CollectAsync(
            "SELECT Name, State, OwnerNode, GroupType, Priority FROM MSCluster_ResourceGroup",
            b => ClusterMapper.MapGroup(b, resources), "skupiny klastra", warnings, cancellationToken).ConfigureAwait(false);
        groups = groups
            .OrderBy(g => g.OwnerNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var networks = await CollectAsync(
            "SELECT Name, State, Role, Address, AddressMask, Metric, AutoMetric FROM MSCluster_Network",
            ClusterMapper.MapNetwork, "siete klastra", warnings, cancellationToken).ConfigureAwait(false);
        networks = networks.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var quorum = clusterBags
            .Select(ClusterMapper.MapQuorum)
            .FirstOrDefault(q => !string.IsNullOrEmpty(q.QuorumType) || !string.IsNullOrEmpty(q.QuorumResource));

        var witness = BuildWitness(quorum, resources);
        var placement = await BuildPlacementAsync(groups, resources, warnings, cancellationToken).ConfigureAwait(false);
        var events = ReadEvents(warnings);

        return new ClusterTopologySnapshot(
            true, warnings, nodes, groups, resources, networks, quorum, witness, events, placement);
    }

    private async Task<List<T>> CollectAsync<T>(
        string wql,
        Func<IReadOnlyDictionary<string, object?>, T> map,
        string area,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var bags = await _cim.QueryAsync(".", Ns, wql, CimTimeouts.Query, cancellationToken).ConfigureAwait(false);
            return bags.Select(b =>
            {
                try
                {
                    return (true, Row: map(b));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Mapovanie riadka {Area} zlyhalo.", area);
                    return (false, Row: default!);
                }
            }).Where(r => r.Item1).Select(r => r.Row).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Načítanie {Area} zlyhalo.", area);
            warnings.Add($"Nepodarilo sa načítať {area}.");
            return [];
        }
    }

    private List<WitnessRow> BuildWitness(QuorumInfo? quorum, IReadOnlyList<ClusterResourceRow> resources)
    {
        var rows = new List<WitnessRow>();
        if (quorum is not null)
        {
            rows.Add(new WitnessRow("Quorum", "Quorum", quorum.QuorumType, string.Empty, string.Empty, string.Empty, quorum.QuorumResource));
        }

        // Reference matching: type -match "Witness" OR name -match "Witness|Quorum".
        // Detail parameters have NO verified native read (no method invocation
        // without a documented read-only surface): Detail stays empty and the
        // gap is marked PARTIAL rather than filled with guesses.
        foreach (var resource in resources.Where(r =>
                     (!string.IsNullOrEmpty(r.ResourceType) && WitnessTypeRegex().IsMatch(r.ResourceType)) ||
                     (!string.IsNullOrEmpty(r.Name) && WitnessNameRegex().IsMatch(r.Name))))
        {
            rows.Add(new WitnessRow(
                "Resource", resource.Name, resource.ResourceType, resource.State,
                resource.OwnerGroup, resource.OwnerNode, string.Empty));
        }

        return rows;
    }

    private async Task<List<VmPlacementRow>> BuildPlacementAsync(
        IReadOnlyList<ClusterGroupRow> groups,
        IReadOnlyList<ClusterResourceRow> resources,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var rows = new List<VmPlacementRow>();
        foreach (var group in groups.Where(g =>
                     string.Equals(g.GroupType, "VirtualMachine", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var preferred = await QueryOwnerNamesAsync(
                "MSCluster_ResourceGroupToPossibleOwner", group.Name, cancellationToken).ConfigureAwait(false);

            var possible = new List<object?>();
            try
            {
                var vmResource = resources.FirstOrDefault(r =>
                    string.Equals(r.OwnerGroup, group.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.ResourceType, "Virtual Machine", StringComparison.Ordinal));
                if (vmResource is not null)
                {
                    possible.AddRange(await QueryResourceOwnerNamesAsync(
                        vmResource.Name, cancellationToken).ConfigureAwait(false));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Possible owners pre {Group} nedostupní.", group.Name);
            }

            var groupBag = await QueryGroupDetailsAsync(group.Name, cancellationToken).ConfigureAwait(false);
            rows.Add(new VmPlacementRow(
                group.Name,
                group.State,
                group.OwnerNode,
                group.Priority,
                OwnerNames.Normalize(preferred),
                OwnerNames.Normalize(possible),
                JoinBagList(groupBag, "AntiAffinityClassNames"),
                BagString(groupBag, "AutoFailbackType"),
                FailbackWindow(groupBag)));
        }

        return rows;
    }

    // Owner-node association join. The association row shape is PARTIAL
    // (needs live verification), so the join is deliberately strict: a row
    // contributes owner names ONLY when one of its values references THIS
    // group/resource (exact or WMI-path containment). No global merging of
    // unrelated rows. Owner candidates come from a bounded allowlist
    // mirroring the reference candidate properties.
    private async Task<List<object?>> QueryOwnerNamesAsync(
        string associationClass,
        string ownerOfName,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _cim.QueryAsync(
                ".", Ns, $"SELECT * FROM {associationClass}", CimTimeouts.Query, cancellationToken).ConfigureAwait(false);
            var names = new List<object?>();
            foreach (var row in rows.Where(r => RowReferences(r, ownerOfName)))
            {
                foreach (var key in new[] { "PossibleOwner", "Owner", "OwnerNode", "Node", "Name", "NodeName", "ClusterNode" })
                {
                    if (row.TryGetValue(key, out var value) && value is not null)
                    {
                        names.Add(NormalizeAssociationValue(value));
                    }
                }
            }

            return names;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Owner nodes pre {Name} nedostupní.", ownerOfName);
            return [];
        }
    }

    private static bool RowReferences(IReadOnlyDictionary<string, object?> row, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var value in row.Values)
        {
            switch (value)
            {
                case string text when text.Contains(name, StringComparison.OrdinalIgnoreCase):
                    return true;
                case CimInstance instance:
                    foreach (var property in instance.CimInstanceProperties)
                    {
                        if (property.Value is string propertyText &&
                            propertyText.Contains(name, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    break;
            }
        }

        return false;
    }

    private static object? NormalizeAssociationValue(object? value)
    {
        if (value is string text)
        {
            return text;
        }

        if (value is CimInstance)
        {
            // Flatten to a bag so OwnerNames applies its candidate list
            // without Core depending on Management.Infrastructure.
            var bag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in ((CimInstance)value).CimInstanceProperties)
            {
                bag[property.Name] = property.Value;
            }

            return bag;
        }

        return null;
    }

    private Task<List<object?>> QueryResourceOwnerNamesAsync(
        string resourceName,
        CancellationToken cancellationToken) =>
        QueryOwnerNamesAsync("MSCluster_ResourceToPossibleOwner", resourceName, cancellationToken);

    private async Task<IReadOnlyDictionary<string, object?>?> QueryGroupDetailsAsync(
        string groupName,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _cim.QueryAsync(
                ".", Ns,
                $"SELECT AntiAffinityClassNames, AutoFailbackType, FailbackWindowStart, FailbackWindowEnd FROM MSCluster_ResourceGroup WHERE Name = '{Escape(groupName)}'",
                CimTimeouts.Query, cancellationToken).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Detaily skupiny {Group} nedostupné.", groupName);
            return null;
        }
    }

    private static string Escape(string value) => value.Replace("'", "''");

    private static string JoinBagList(IReadOnlyDictionary<string, object?>? bag, string key)
    {
        if (bag is null || !bag.TryGetValue(key, out var value) || value is null)
        {
            return string.Empty;
        }

        if (value is string single)
        {
            return single;
        }

        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            return string.Join(", ", enumerable.Cast<object?>()
                .Where(o => o is not null)
                .Select(o => Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
                .Where(s => s.Length > 0));
        }

        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string BagString(IReadOnlyDictionary<string, object?>? bag, string key)
    {
        if (bag is null || !bag.TryGetValue(key, out var value) || value is null)
        {
            return string.Empty;
        }

        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string FailbackWindow(IReadOnlyDictionary<string, object?>? bag)
    {
        var start = BagString(bag, "FailbackWindowStart");
        var end = BagString(bag, "FailbackWindowEnd");
        return !string.IsNullOrEmpty(start) || !string.IsNullOrEmpty(end) ? $"{start}-{end}" : string.Empty;
    }

    private List<ClusterEventRow> ReadEvents(List<string> warnings)
    {
        try
        {
            return _events.Read().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Čítanie cluster eventov zlyhalo.");
            warnings.Add("Nepodarilo sa načítať cluster eventy.");
            return [];
        }
    }
}
