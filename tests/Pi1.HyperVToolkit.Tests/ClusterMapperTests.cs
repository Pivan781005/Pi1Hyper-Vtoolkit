using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Infrastructure.Cluster;

namespace Pi1.HyperVToolkit.Tests;

public sealed class ClusterMapperTests
{
    private static Dictionary<string, object?> Bag(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    // ---- Node states ----

    [Theory]
    [InlineData(0, "Up")]
    [InlineData(1, "Down")]
    [InlineData(2, "Paused")]
    [InlineData(3, "Joining")]
    [InlineData(9, "Unknown (9)")]
    [InlineData(null, "")]
    public void NodeState_Table(object? raw, string expected) =>
        Assert.Equal(expected, ClusterMapper.MapNodeState(raw));

    [Fact]
    public void NodeState_StringPassthrough() =>
        Assert.Equal("Up", ClusterMapper.MapNodeState("Up"));

    [Theory]
    [InlineData(0, "NotInitiated")]
    [InlineData(1, "InProgress")]
    [InlineData(2, "Completed")]
    [InlineData(3, "Failed")]
    [InlineData(null, "")]
    public void DrainStatus_Table(object? raw, string expected) =>
        Assert.Equal(expected, ClusterMapper.MapDrainStatus(raw));

    [Fact]
    public void NodeWeight_NumericVerbatim()
    {
        Assert.Equal("1", ClusterMapper.MapNodeWeight(1));
        Assert.Equal("0", ClusterMapper.MapNodeWeight((ushort)0));
        Assert.Equal(string.Empty, ClusterMapper.MapNodeWeight(null));
    }

    [Fact]
    public void MapNode_Full()
    {
        var row = ClusterMapper.MapNode(Bag(
            ("Name", "N1"), ("State", 0), ("DrainStatus", 0), ("NodeWeight", 1), ("FaultDomain", "fd:/dc1/rack2")));
        Assert.Equal(new ClusterNodeRow("N1", "Up", "NotInitiated", "1", "fd:/dc1/rack2"), row);
    }

    [Fact]
    public void MapNode_MissingProperties_EmptyNeverCrash()
    {
        var row = ClusterMapper.MapNode(Bag(("Name", "N1")));
        Assert.Equal("N1", row.Name);
        Assert.Equal(string.Empty, row.State);
        Assert.Equal(string.Empty, row.FaultDomain);
    }

    // ---- Group states ----

    [Theory]
    [InlineData(-1, "Unknown")]
    [InlineData(0, "Online")]
    [InlineData(1, "Offline")]
    [InlineData(2, "Failed")]
    [InlineData(3, "PartialOnline")]
    [InlineData(4, "Pending")]
    [InlineData(42, "Unknown (42)")]
    [InlineData(null, "")]
    public void GroupState_Table(object? raw, string expected) =>
        Assert.Equal(expected, ClusterMapper.MapGroupState(raw));

    [Fact]
    public void MapGroup_PriorityVerbatim_GroupTypeFallbackDerivesVm()
    {
        var resources = new[]
        {
            new ClusterResourceRow("VM1", "Online", "Role1", "Virtual Machine", "N1"),
        };
        var row = ClusterMapper.MapGroup(Bag(
            ("Name", "Role1"), ("State", 0), ("OwnerNode", "N1"), ("Priority", "High")), resources);
        Assert.Equal("Role1", row.Name);
        Assert.Equal("Online", row.State);
        Assert.Equal("N1", row.OwnerNode);
        Assert.Equal("VirtualMachine", row.GroupType);
        Assert.Equal("High", row.Priority);
    }

    [Fact]
    public void MapGroup_NumericPriority3000_Preserved()
    {
        var row = ClusterMapper.MapGroup(Bag(
            ("Name", "R"), ("State", "Online"), ("OwnerNode", "N1"),
            ("GroupType", "VirtualMachine"), ("Priority", 3000)), []);
        Assert.Equal("3000", row.Priority);
    }

    // ---- Resource states ----

    [Theory]
    [InlineData(-1, "Unknown")]
    [InlineData(0, "Inherited")]
    [InlineData(1, "Initializing")]
    [InlineData(2, "Online")]
    [InlineData(3, "Offline")]
    [InlineData(4, "Failed")]
    [InlineData(5, "Pending")]
    [InlineData(6, "OnlinePending")]
    [InlineData(7, "OfflinePending")]
    [InlineData(42, "Unknown (42)")]
    [InlineData(null, "")]
    public void ResourceState_Table(object? raw, string expected) =>
        Assert.Equal(expected, ClusterMapper.MapResourceState(raw));

    [Fact]
    public void MapResource_Full()
    {
        var row = ClusterMapper.MapResource(Bag(
            ("Name", "Disk1"), ("State", 2), ("OwnerGroup", "Available Storage"),
            ("ResourceType", "Physical Disk"), ("OwnerNode", "N1")));
        Assert.Equal(new ClusterResourceRow("Disk1", "Online", "Available Storage", "Physical Disk", "N1"), row);
    }

    // ---- Network ----

    [Theory]
    [InlineData(-1, "Unknown")]
    [InlineData(0, "Unavailable")]
    [InlineData(1, "Down")]
    [InlineData(2, "Partitioned")]
    [InlineData(3, "Up")]
    [InlineData(null, "")]
    public void NetworkState_Table(object? raw, string expected) =>
        Assert.Equal(expected, ClusterMapper.MapNetworkState(raw));

    [Theory]
    [InlineData(0, "None")]
    [InlineData(1, "InternalUse")]
    [InlineData(2, "ClientAccess")]
    [InlineData(3, "InternalAndClient")]
    [InlineData(null, "")]
    public void NetworkRole_Table(object? raw, string expected) =>
        Assert.Equal(expected, ClusterMapper.MapNetworkRole(raw));

    [Fact]
    public void MapNetwork_Full()
    {
        var row = ClusterMapper.MapNetwork(Bag(
            ("Name", "Cluster Network 1"), ("State", 3), ("Role", 3),
            ("Address", "10.0.0.0"), ("AddressMask", "255.255.255.0"),
            ("Metric", 1000), ("AutoMetric", true)));
        Assert.Equal("Cluster Network 1", row.Name);
        Assert.Equal("Up", row.State);
        Assert.Equal("InternalAndClient", row.Role);
        Assert.Equal("10.0.0.0", row.Address);
        Assert.Equal("255.255.255.0", row.AddressMask);
        Assert.Equal(1000, row.Metric);
        Assert.True(row.AutoMetric);
    }

    // ---- Quorum ----

    [Theory]
    [InlineData(1, "Node Majority")]
    [InlineData(2, "Node and Disk Majority")]
    [InlineData(3, "Node and File Share Majority")]
    [InlineData(4, "Node and Cloud Majority")]
    [InlineData(5, "Disk Only")]
    [InlineData(null, "")]
    public void QuorumType_Table(object? raw, string expected) =>
        Assert.Equal(expected, ClusterMapper.MapQuorumType(raw));

    [Fact]
    public void MapQuorum_Full()
    {
        var quorum = ClusterMapper.MapQuorum(Bag(
            ("QuorumType", 3), ("QuorumResource", "File Share Witness")));
        Assert.Equal("Node and File Share Majority", quorum.QuorumType);
        Assert.Equal("File Share Witness", quorum.QuorumResource);
    }

    // ---- Owner references ----

    [Fact]
    public void OwnerName_PlainString() =>
        Assert.Equal("N1", ClusterMapper.OwnerName("N1"));

    [Fact]
    public void OwnerName_WmiReferencePath() =>
        Assert.Equal(
            @"Microsoft:Cluster\N1",
            ClusterMapper.OwnerName(@"\\HOST\root\MSCluster:MSCluster_Node.Name=""Microsoft:Cluster\\N1"""));

    [Fact]
    public void OwnerName_NullOrEmpty() =>
        Assert.Equal(string.Empty, ClusterMapper.OwnerName(null));
}
