using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Tests;

public sealed class ScopeServiceTests
{
    [Fact]
    public void Default_IsLocalWithMachineName()
    {
        var service = new ScopeService("TESTNODE");
        Assert.Equal(ScopeMode.Local, service.Current.Mode);
        Assert.Equal("TESTNODE", service.Current.SelectedNode);
        Assert.Equal("TESTNODE", service.MachineName);
    }

    [Theory]
    [InlineData(ScopeMode.Local, "TESTNODE", "Lokálny uzol (TESTNODE)")]
    [InlineData(ScopeMode.Cluster, "TESTNODE", "Všetky uzly klastra")]
    public void GetScopeLabel_LocalAndCluster(ScopeMode mode, string machine, string expected)
    {
        var service = new ScopeService(machine);
        service.SetScope(mode, "OTHER");
        Assert.Equal(expected, service.GetScopeLabel());
    }

    [Fact]
    public void GetScopeLabel_Node()
    {
        var service = new ScopeService("TESTNODE");
        service.SetScope(ScopeMode.Node, "NODE2");
        Assert.Equal("Vybraný uzol (NODE2)", service.GetScopeLabel());
    }

    [Fact]
    public void SetScope_Local_NormalizesSelectedNodeToMachine()
    {
        var service = new ScopeService("TESTNODE");
        service.SetScope(ScopeMode.Node, "NODE2");
        service.SetScope(ScopeMode.Local);
        Assert.Equal("TESTNODE", service.Current.SelectedNode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetScope_Node_RequiresNodeName(string? node)
    {
        var service = new ScopeService("TESTNODE");
        Assert.Throws<ArgumentException>(() => service.SetScope(ScopeMode.Node, node));
    }

    [Fact]
    public void SetScope_Node_TrimsName()
    {
        var service = new ScopeService("TESTNODE");
        service.SetScope(ScopeMode.Node, "  NODE2  ");
        Assert.Equal("NODE2", service.Current.SelectedNode);
    }

    [Fact]
    public void SetScope_RaisesScopeChanged()
    {
        var service = new ScopeService("TESTNODE");
        ScopeState? raised = null;
        service.ScopeChanged += (_, state) => raised = state;
        service.SetScope(ScopeMode.Node, "NODE2");
        Assert.Equal(new ScopeState(ScopeMode.Node, "NODE2"), raised);
    }

    [Fact]
    public void TryGetStaticTargetNodes_LocalAndNode()
    {
        var service = new ScopeService("TESTNODE");
        Assert.True(service.TryGetStaticTargetNodes(out var local));
        Assert.Equal(["TESTNODE"], local);

        service.SetScope(ScopeMode.Node, "NODE2");
        Assert.True(service.TryGetStaticTargetNodes(out var node));
        Assert.Equal(["NODE2"], node);
    }

    [Fact]
    public void TryGetStaticTargetNodes_Cluster_ReturnsFalse()
    {
        var service = new ScopeService("TESTNODE");
        service.SetScope(ScopeMode.Cluster);
        Assert.True(service.ClusterEnumerationRequired);
        Assert.False(service.TryGetStaticTargetNodes(out _));
    }

    [Fact]
    public void ClusterEnumerationRequired_FalseForLocalAndNode()
    {
        var service = new ScopeService("TESTNODE");
        Assert.False(service.ClusterEnumerationRequired);
        service.SetScope(ScopeMode.Node, "NODE2");
        Assert.False(service.ClusterEnumerationRequired);
    }
}
