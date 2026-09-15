using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Infrastructure.Mapping;

namespace Pi1.HyperVToolkit.Infrastructure.Cluster;

// Pure root\MSCluster to row-model mapping. Every method takes plain property
// bags: unit-testable without a cluster. Mapping certainty is marked per
// member: VERIFIED tables come from long-stable MS Failover Cluster API enums
// (clusapi), PARTIAL items need a live-cluster confirmation run and degrade
// to empty/Unknown rather than invented values. Missing properties always
// yield empty strings (never a crash, never fake data).
public static class ClusterMapper
{
    public const string ClusterNamespace = @"root\MSCluster";

    public static ClusterNodeRow MapNode(IReadOnlyDictionary<string, object?> bag) =>
        new(
            CimValues.GetString(bag, "Name"),
            MapNodeState(bag.TryGetValue("State", out var state) ? state : null),
            MapDrainStatus(bag.TryGetValue("DrainStatus", out var drain) ? drain : null),
            MapNodeWeight(bag.TryGetValue("NodeWeight", out var weight) ? weight : null),
            CimValues.GetString(bag, "FaultDomain"));

    // MSCluster_Node.State. VERIFIED convention (already used by the Phase 4
    // dashboard join): 0 Up, 1 Down, 2 Paused, 3 Joining. Provider strings
    // pass through verbatim.
    public static string MapNodeState(object? value)
    {
        if (value is string text && !IsIntegerText(text))
        {
            return text;
        }

        return ToLong(value) switch
        {
            0 => "Up",
            1 => "Down",
            2 => "Paused",
            3 => "Joining",
            null => string.Empty,
            var n => $"Unknown ({n})",
        };
    }

    // PARTIAL: DrainStatus numbering needs live verification; strings pass
    // through verbatim, missing stays empty.
    public static string MapDrainStatus(object? value)
    {
        if (value is string text && !IsIntegerText(text))
        {
            return text;
        }

        return ToLong(value) switch
        {
            0 => "NotInitiated",
            1 => "InProgress",
            2 => "Completed",
            3 => "Failed",
            null => string.Empty,
            var n => $"Unknown ({n})",
        };
    }

    public static string MapNodeWeight(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        var number = ToLong(value);
        return number.HasValue
            ? number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
    }

    public static ClusterGroupRow MapGroup(
        IReadOnlyDictionary<string, object?> bag,
        IReadOnlyList<ClusterResourceRow> resources)
    {
        var name = CimValues.GetString(bag, "Name");
        var groupType = CimValues.GetString(bag, "GroupType");
        if (string.IsNullOrEmpty(groupType))
        {
            // Fallback derivation (same observable result for VM groups):
            // a group hosting a "Virtual Machine" resource is a VM group.
            groupType = resources.Any(r =>
                string.Equals(r.OwnerGroup, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.ResourceType, "Virtual Machine", StringComparison.Ordinal)) ? "VirtualMachine" : string.Empty;
        }

        return new ClusterGroupRow(
            name,
            MapGroupState(bag.TryGetValue("State", out var state) ? state : null),
            OwnerName(bag.TryGetValue("OwnerNode", out var owner) ? owner : null),
            groupType,
            CimValues.GetString(bag, "Priority"));
    }

    // PARTIAL: CLUSTER_GROUP_STATE numbering needs live verification.
    // Best-known: -1 Unknown, 0 Online, 1 Offline, 2 Failed, 3 PartialOnline,
    // 4 Pending. Provider strings pass through verbatim.
    public static string MapGroupState(object? value)
    {
        if (value is string text && !IsIntegerText(text))
        {
            return text;
        }

        return ToLong(value) switch
        {
            -1 => "Unknown",
            0 => "Online",
            1 => "Offline",
            2 => "Failed",
            3 => "PartialOnline",
            4 => "Pending",
            null => string.Empty,
            var n => $"Unknown ({n})",
        };
    }

    public static ClusterResourceRow MapResource(IReadOnlyDictionary<string, object?> bag) =>
        new(
            CimValues.GetString(bag, "Name"),
            MapResourceState(bag.TryGetValue("State", out var state) ? state : null),
            OwnerName(bag.TryGetValue("OwnerGroup", out var group) ? group : null),
            FirstNonEmpty(
                CimValues.GetString(bag, "ResourceType"),
                CimValues.GetString(bag, "Type")),
            OwnerName(bag.TryGetValue("OwnerNode", out var owner) ? owner : null));

    // VERIFIED CLUSTER_RESOURCE_STATE (clusapi, long stable): -1 Unknown,
    // 0 Inherited, 1 Initializing, 2 Online, 3 Offline, 4 Failed, 5 Pending,
    // 6 OnlinePending, 7 OfflinePending. Provider strings pass through.
    public static string MapResourceState(object? value)
    {
        if (value is string text && !IsIntegerText(text))
        {
            return text;
        }

        return ToLong(value) switch
        {
            -1 => "Unknown",
            0 => "Inherited",
            1 => "Initializing",
            2 => "Online",
            3 => "Offline",
            4 => "Failed",
            5 => "Pending",
            6 => "OnlinePending",
            7 => "OfflinePending",
            null => string.Empty,
            var n => $"Unknown ({n})",
        };
    }

    public static ClusterNetworkRow MapNetwork(IReadOnlyDictionary<string, object?> bag) =>
        new(
            CimValues.GetString(bag, "Name"),
            MapNetworkState(bag.TryGetValue("State", out var state) ? state : null),
            MapNetworkRole(bag.TryGetValue("Role", out var role) ? role : null),
            CimValues.GetString(bag, "Address"),
            CimValues.GetString(bag, "AddressMask"),
            CimValues.GetInt(bag, "Metric"),
            CimValues.GetBool(bag, "AutoMetric"));

    // PARTIAL: CLUSTER_NETWORK_STATE numbering needs live verification.
    // Best-known: -1 Unknown, 0 Unavailable, 1 Down, 2 Partitioned, 3 Up.
    public static string MapNetworkState(object? value)
    {
        if (value is string text && !IsIntegerText(text))
        {
            return text;
        }

        return ToLong(value) switch
        {
            -1 => "Unknown",
            0 => "Unavailable",
            1 => "Down",
            2 => "Partitioned",
            3 => "Up",
            null => string.Empty,
            var n => $"Unknown ({n})",
        };
    }

    // PARTIAL: CLUSTER_NETWORK_ROLE numbering/display form needs live
    // verification (best-known 0 None, 1 InternalUse, 2 ClientAccess,
    // 3 InternalAndClient). Provider strings pass through verbatim.
    public static string MapNetworkRole(object? value)
    {
        if (value is string text && !IsIntegerText(text))
        {
            return text;
        }

        return ToLong(value) switch
        {
            0 => "None",
            1 => "InternalUse",
            2 => "ClientAccess",
            3 => "InternalAndClient",
            null => string.Empty,
            var n => $"Unknown ({n})",
        };
    }

    public static QuorumInfo MapQuorum(IReadOnlyDictionary<string, object?> bag) =>
        new(
            MapQuorumType(bag.TryGetValue("QuorumType", out var type) ? type : null),
            CimValues.GetString(bag, "QuorumResource"));

    // PARTIAL: quorum-type numbering needs live verification. Best-known
    // display names; provider strings pass through verbatim.
    public static string MapQuorumType(object? value)
    {
        if (value is string text && !IsIntegerText(text))
        {
            return text;
        }

        return ToLong(value) switch
        {
            0 => "Unknown",
            1 => "Node Majority",
            2 => "Node and Disk Majority",
            3 => "Node and File Share Majority",
            4 => "Node and Cloud Majority",
            5 => "Disk Only",
            null => string.Empty,
            var n => $"Unknown ({n})",
        };
    }

    // Owner references arrive either as live CimInstance values (Name key),
    // WMI reference paths, or plain strings. Reuses the storage-graph
    // reference normalizer, then takes a trailing plain name as-is.
    public static string OwnerName(object? value)
    {
        var canonical = ReferenceIds.TryGetReferencedInstanceId(value);
        return canonical ?? string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static bool IsIntegerText(string text) =>
        long.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out _);

    private static long? ToLong(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception) when (value is string || value is IConvertible)
        {
            return null;
        }
    }
}
